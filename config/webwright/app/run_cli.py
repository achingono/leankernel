#!/usr/bin/env python3
"""Headless browser runner for LeanKernel Webwright.

Spawned as a subprocess by the webwright FastAPI sidecar. Connects to
Playwright via WebSocket and uses an LLM (via LiteLLM) to execute
natural-language browsing tasks.
"""

from __future__ import annotations

import argparse
import asyncio
from datetime import datetime
import functools
import json
import logging
import os
import pathlib
import re
import sys
import textwrap
import traceback
import typing


logger = logging.getLogger("run_cli")
TASK_COMPLETE_SENTINEL = "##TASK_COMPLETE##"
MAX_STEPS = 25
SCREENSHOT_DELAY_MS = 800


class RunConfig(typing.NamedTuple):
    config_path: str
    task: str
    task_id: str
    output_dir: str
    start_url: str | None


class LiteLLMConfig(typing.NamedTuple):
    base_url: str
    api_key: str
    model: str


@functools.lru_cache(maxsize=1)
def load_config(config_path: str) -> LiteLLMConfig:
    import yaml as _yaml
    with open(config_path) as f:
        raw = _yaml.safe_load(f) or {}
    model_cfg = raw.get("model", {})
    return LiteLLMConfig(
        base_url=model_cfg.get("base_url", os.environ.get("LITELLM_BASE_URL", "http://litellm:4000")),
        api_key=model_cfg.get("api_key", os.environ.get("LITELLM_API_KEY", "")),
        model=model_cfg.get("model", os.environ.get("WEBWRIGHT_MODEL", "gpt-4o")),
    )


def setup_logging(run_dir: pathlib.Path) -> logging.FileHandler:
    run_dir.mkdir(parents=True, exist_ok=True)
    handler = logging.FileHandler(run_dir / "run_cli.log", encoding="utf-8")
    handler.setFormatter(logging.Formatter("%(asctime)s [%(levelname)s] %(message)s"))
    root = logging.getLogger()
    root.setLevel(logging.INFO)
    root.addHandler(handler)
    return handler


class _Capture:
    def __init__(self, lines: list) -> None:
        self.lines = lines
    def write(self, text: str) -> None:
        self.lines.append(text)
    def flush(self) -> None:
        pass


async def exec_code(
    code: str,
    page: typing.Any,
    context: typing.Any,
    loop_artifacts: dict,
    timeout: int = 30,
) -> str:
    ns = {
        "page": page,
        "context": context,
        "artifacts": loop_artifacts,
        "datetime": __import__("datetime"),
        "json": json,
        "os": os,
        "re": re,
        "pathlib": pathlib,
        "asyncio": asyncio,
    }
    lines: list[str] = []
    sys.stdout = _Capture(lines)
    try:
        if "await" in code:
            wrapped = "async def _exec_runner():\n" + textwrap.indent(code.strip(), "    ")
            exec(wrapped, ns)
            await asyncio.wait_for(ns["_exec_runner"](), timeout=timeout)
        else:
            exec(code, ns)
        return "".join(lines)
    except asyncio.CancelledError:
        raise
    except Exception as exc:
        tb = traceback.format_exc()
        logger.error("Code execution failed:\n%s\n--- code ---\n%s", tb, code)
        return f"<error>{type(exc).__name__}: {exc}</error>"
    finally:
        sys.stdout = sys.__stdout__


def extract_code_blocks(text: str) -> list[str]:
    blocks = re.split(r"(?:^|\n)```(?:python)?\s*\n?", text)
    results = []
    for i, block in enumerate(blocks):
        if i % 2 == 1:
            code = block.strip()
            if code.endswith("```"):
                code = code[:-3].strip()
            if code:
                results.append(code)
    return results


def extract_task_complete(text: str) -> str | None:
    """If the LLM signals completion, extract the summary."""
    m = re.search(rf"{TASK_COMPLETE_SENTINEL}\s*(.*)", text, re.DOTALL)
    if m:
        return m.group(1).strip()
    return None


def build_system_prompt() -> str:
    return """You are a browser automation agent. Your job is to complete a user's task by controlling a Playwright browser.

You have access to these variables in Python:
- `page` — Playwright Page object
- `context` — Playwright BrowserContext object
- `artifacts` — dict for storing intermediate results

Useful page methods:
- `page.goto(url)` — navigate to URL
- `page.click(selector)` — click element
- `page.fill(selector, text)` — fill input field
- `page.type(selector, text, delay=50)` — type into field
- `page.select_option(selector, value=...)` — select dropdown
- `page.wait_for_selector(selector, timeout=5000)` — wait for element
- `page.wait_for_timeout(ms)` — wait milliseconds
- `page.title()` — get page title
- `page.url` — current URL
- `page.content()` — full HTML
- `page.evaluate(js)` — run JavaScript
- `page.locator(selector).inner_text()` — get element text
- `page.locator(selector).count()` — count matching elements
- `page.keyboard.press(key)` — press keyboard key
- `page.keyboard.type(text)` — type text
- `page.screenshot()` — take screenshot

Each step, output Python code inside a ```python code block.
The code will be executed. After execution, a new screenshot will be taken.

When the task is fully complete, output:
```
##TASK_COMPLETE## <summary of what was accomplished>
```

CRITICAL RULES:
1. Always wait for elements to be ready before interacting.
2. Never output HTML or explanations outside code blocks — only code or ##TASK_COMPLETE##.
3. If you encounter an error, try a different approach.
4. Be thorough — explore the page, read content, interact as needed.
5. Complete the FULL task, don't stop after one action.
6. Use page.wait_for_timeout between actions for visual stability.
"""


def build_step_prompt(task: str, url: str, title: str, step: int, previous_error: str | None = None) -> str:
    parts = [
        f"Task: {task}",
        f"Step {step}/{MAX_STEPS}",
        f"Current URL: {url}",
        f"Page title: {title}",
    ]
    if previous_error:
        parts.append(f"\nPrevious step error: {previous_error}\nTry a different approach.")
    parts.append("\nGenerate the next Python/Playwright code block, or ##TASK_COMPLETE## if done:")
    return "\n".join(parts)


async def run_llm(messages: list[dict], config: LiteLLMConfig) -> str:
    import openai
    client = openai.OpenAI(base_url=config.base_url, api_key=config.api_key)
    response = client.chat.completions.create(
        model=config.model,
        messages=messages,
        temperature=0.1,
        max_tokens=4096,
    )
    return response.choices[0].message.content or ""


async def run_browser_task(config: LiteLLMConfig, run_cfg: RunConfig) -> dict:
    from playwright.async_api import async_playwright

    run_dir = pathlib.Path(run_cfg.output_dir) / run_cfg.task_id
    screenshots_dir = run_dir / "screenshots"
    screenshots_dir.mkdir(parents=True, exist_ok=True)
    log_path = run_dir / "run_cli.log"
    report_path = run_dir / "report.json"
    script_path = run_dir / "final_script.py"
    script_log_path = run_dir / "final_script_log.txt"

    manifest: list[dict] = []
    all_code: list[str] = []
    all_logs: list[str] = []
    final_datum: str | None = None

    async with async_playwright() as pw:
        ws_endpoint = os.environ.get(
            "PLAYWRIGHT_WS_ENDPOINT",
            "ws://platform_playwright:6000/",
        )
        logger.info("Connecting to Playwright at %s", ws_endpoint)
        browser = await pw.chromium.connect(ws_endpoint)
        context = await browser.new_context(
            viewport={"width": 1280, "height": 800},
            user_agent=(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                "AppleWebKit/537.36 (KHTML, like Gecko) "
                "Chrome/120.0.0.0 Safari/537.36"
            ),
        )

        page = await context.new_page()
        loop_artifacts: dict = {}

        if run_cfg.start_url:
            logger.info("Navigating to start URL: %s", run_cfg.start_url)
            await page.goto(run_cfg.start_url, wait_until="domcontentloaded", timeout=30000)
            await page.wait_for_timeout(2000)

        messages: list[dict] = [
            {"role": "system", "content": build_system_prompt()},
        ]

        previous_error: str | None = None

        for step in range(1, MAX_STEPS + 1):
            url = page.url
            title = await page.title()

            prompt = build_step_prompt(run_cfg.task, url, title, step, previous_error)
            messages.append({"role": "user", "content": prompt})

            logger.info("LLM step %d — url=%s title=%s", step, url, title)
            response_text = await run_llm(messages, config)
            messages.append({"role": "assistant", "content": response_text})

            # Check for completion
            summary = extract_task_complete(response_text)
            if summary:
                final_datum = summary
                logger.info("Task complete at step %d: %s", step, summary)
                break

            # Execute code blocks
            code_blocks = extract_code_blocks(response_text)
            if not code_blocks:
                logger.warning("No code blocks in LLM response at step %d", step)
                previous_error = "No code was generated. Generate Python code or ##TASK_COMPLETE##."
                continue

            executed_any = False
            for code in code_blocks:
                logger.info("Executing code block (%d chars)...", len(code))
                all_code.append(f"# Step {step}\n{code}\n")
                output = await exec_code(code, page, context, loop_artifacts, timeout=30)
                executed_any = True
                timestamp = datetime.now(tz=None).isoformat()
                log_entry = f"[{timestamp}] STEP {step}\n--- code ---\n{code}\n--- output ---\n{output}\n"
                all_logs.append(log_entry)
                logger.info("Step %d output: %s", step, output[:200])

                if output.startswith("<error>"):
                    previous_error = output
                else:
                    previous_error = None

            # Screenshot after each step
            await page.wait_for_timeout(SCREENSHOT_DELAY_MS)
            screenshot_path = screenshots_dir / f"step_{step:03d}.png"
            try:
                await page.screenshot(path=str(screenshot_path), full_page=False)
                manifest.append({
                    "id": f"screenshot-{step}",
                    "kind": "screenshot",
                    "displayName": f"step_{step:03d}.png",
                    "contentType": "image/png",
                    "bytes": screenshot_path.stat().st_size,
                    "path": str(screenshot_path.relative_to(run_dir)),
                })
            except Exception as exc:
                logger.warning("Screenshot failed at step %d: %s", step, exc)

            if not executed_any:
                previous_error = "No code was generated. Generate Python code or ##TASK_COMPLETE##."
        else:
            logger.warning("Reached max steps (%d) without completing task", MAX_STEPS)

    # Final report
    report = {
        "taskId": run_cfg.task_id,
        "task": run_cfg.task,
        "status": "completed" if final_datum else "partial",
        "finalDatum": final_datum,
        "steps": len(all_code),
        "screenshots": len(manifest),
    }
    report_path.write_text(json.dumps(report, indent=2))

    # Save final script
    script_path.write_text("\n".join(all_code))
    script_log_path.write_text("".join(all_logs))

    # Build full manifest
    script_rel = str(script_path.relative_to(run_dir))
    log_rel = str(script_log_path.relative_to(run_dir))
    if script_size := safe_size(script_path):
        manifest.insert(0, {
            "id": "script",
            "kind": "script",
            "displayName": "final_script.py",
            "contentType": "text/x-python",
            "bytes": script_size,
            "path": script_rel,
        })
    if log_size := safe_size(script_log_path):
        manifest.insert(1, {
            "id": "log",
            "kind": "log",
            "displayName": "final_script_log.txt",
            "contentType": "text/plain",
            "bytes": log_size,
            "path": log_rel,
        })

    return {
        "runId": run_cfg.task_id,
        "status": "completed" if final_datum else "partial",
        "finalDatum": final_datum,
        "manifest": manifest,
        "report": report,
    }


def safe_size(path: pathlib.Path) -> int | None:
    try:
        return path.stat().st_size
    except OSError:
        return None


def parse_args(argv: list[str] | None = None) -> RunConfig:
    parser = argparse.ArgumentParser(description="LeanKernel Webwright Browser Runner")
    parser.add_argument("-c", "--config", required=True, help="YAML config path")
    parser.add_argument("-t", "--task", required=True, help="Natural language task")
    parser.add_argument("--task-id", required=True, help="Unique task ID")
    parser.add_argument("-o", "--output", required=True, help="Output directory")
    parser.add_argument("--start-url", help="Starting URL")
    parsed = parser.parse_args(argv)
    return RunConfig(
        config_path=parsed.config,
        task=parsed.task,
        task_id=parsed.task_id,
        output_dir=parsed.output,
        start_url=parsed.start_url,
    )


async def main(argv: list[str] | None = None) -> int:
    run_cfg = parse_args(argv)
    run_dir = pathlib.Path(run_cfg.output_dir) / run_cfg.task_id
    handler = setup_logging(run_dir)

    try:
        llm_cfg = load_config(run_cfg.config_path)
        logger.info(
            "Starting task=%s model=%s url=%s",
            run_cfg.task_id, llm_cfg.model, run_cfg.start_url or "(none)",
        )
        result = await run_browser_task(llm_cfg, run_cfg)
        logger.info("Task finished: status=%s", result["status"])
        return 0
    except asyncio.CancelledError:
        logger.warning("Task cancelled")
        return 1
    except Exception:
        logger.exception("Unhandled error in run_cli")
        return 1
    finally:
        handler.close()
        logger.removeHandler(handler)


if __name__ == "__main__":
    exit_code = asyncio.run(main())
    sys.exit(exit_code)
