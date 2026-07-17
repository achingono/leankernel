"""LeanKernel Webwright MCP Server.

Exposes browser automation tools over the Model Context Protocol.
Each tool delegates to ``run_cli.py`` which connects to the shared
Playwright service via WebSocket and drives an LLM through a browsing task.
"""

from __future__ import annotations

import asyncio
import base64
import contextlib
import hashlib
import json
import logging
import os
import signal
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any

from mcp.server.fastmcp import FastMCP
from starlette.requests import Request
from starlette.responses import JSONResponse
from starlette.routing import Route

logger = logging.getLogger("webwright.mcp")

OUTPUT_ROOT = Path(os.getenv("OUTPUT_ROOT", "/app/outputs")).resolve()
CONFIG_ROOT = Path(os.getenv("CONFIG_ROOT", "/app/config")).resolve()
LITELLM_BASE_URL = os.getenv("LITELLM_BASE_URL", "http://litellm:4000")
LITELLM_API_KEY = os.getenv("LITELLM_API_KEY", "")
WEBWRIGHT_MODEL = os.getenv("WEBWRIGHT_MODEL", "gpt-4o")
MAX_CONCURRENT_RUNS = int(os.getenv("MAX_CONCURRENT_RUNS", "2"))
MAX_QUEUE_DEPTH = int(os.getenv("MAX_QUEUE_DEPTH", "20"))


def _run_timeout_seconds() -> int:
    configured = os.getenv("RUN_WALL_CLOCK_SECONDS")
    if configured:
        return max(1, int(configured))
    legacy_ms = os.getenv("WEBWRIGHT_RUN_TIMEOUT_MS")
    if legacy_ms:
        return max(1, (int(legacy_ms) + 999) // 1000)
    return 600


RUN_WALL_CLOCK_SECONDS = _run_timeout_seconds()
ARTIFACT_TTL_HOURS = int(os.getenv("ARTIFACT_TTL_HOURS", "24"))
ARTIFACT_MAX_BYTES = int(os.getenv("ARTIFACT_MAX_BYTES", "50000000"))
METADATA_FILE = "leankernel_run.json"
UTC = timezone.utc

mcp = FastMCP(
    "leankernel-webwright-mcp",
    json_response=True,
    host="0.0.0.0",
)


# ---------------------------------------------------------------------------
# Run state management
# ---------------------------------------------------------------------------

@dataclass
class RunState:
    run_id: str
    payload: dict[str, Any]
    payload_hash: str
    status: str = "queued"
    submitted_at: datetime = field(default_factory=lambda: datetime.now(UTC))
    started_at: datetime | None = None
    completed_at: datetime | None = None
    exit_code: int | None = None
    final_datum: str | None = None
    artifacts: list[dict[str, Any]] = field(default_factory=list)
    error: dict[str, Any] | None = None
    process: asyncio.subprocess.Process | None = None

    @property
    def run_dir(self) -> Path:
        return OUTPUT_ROOT / self.run_id


class RunManager:
    def __init__(self) -> None:
        self.runs: dict[str, RunState] = {}
        self.idempotency: dict[str, str] = {}
        self.queue: asyncio.Queue[str] = asyncio.Queue(maxsize=MAX_QUEUE_DEPTH)
        self.request_locks: dict[str, asyncio.Lock] = {}
        self.workers: list[asyncio.Task[None]] = []
        self.sweeper: asyncio.Task[None] | None = None

    async def start(self) -> None:
        OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
        render_config()
        self._reconcile()
        for _ in range(MAX_CONCURRENT_RUNS):
            self.workers.append(asyncio.create_task(self._worker_loop()))
        self.sweeper = asyncio.create_task(self._sweep_loop())

    async def stop(self) -> None:
        for task in self.workers:
            task.cancel()
        if self.sweeper is not None:
            self.sweeper.cancel()
        for state in self.runs.values():
            if state.process and state.process.returncode is None:
                with contextlib.suppress(ProcessLookupError):
                    state.process.send_signal(signal.SIGTERM)
        await asyncio.gather(*self.workers, return_exceptions=True)
        if self.sweeper is not None:
            await asyncio.gather(self.sweeper, return_exceptions=True)

    # -- public API --------------------------------------------------------

    def submit(self, task: str, start_url: str | None = None,
               request_id: str | None = None, model: str | None = None) -> dict[str, Any]:
        payload: dict[str, Any] = {"task": task.strip()}
        if start_url:
            payload["startUrl"] = start_url.strip()
        if request_id:
            payload["requestId"] = request_id.strip()
        if model:
            payload["model"] = model.strip()
        payload_hash = _hash(payload)

        if request_id:
            existing_id = self.idempotency.get(request_id)
            if existing_id:
                existing = self.runs.get(existing_id)
                if existing is None:
                    self.idempotency.pop(request_id, None)
                else:
                    if existing.payload_hash != payload_hash:
                        raise ValueError("requestId was already used with a different payload.")
                    return _submission_summary(existing)

        run_id = str(uuid.uuid4())
        state = RunState(run_id=run_id, payload=payload, payload_hash=payload_hash)
        self.runs[run_id] = state
        if request_id:
            self.idempotency[request_id] = run_id
        state.run_dir.mkdir(parents=True, exist_ok=False)
        _persist(state)

        try:
            self.queue.put_nowait(run_id)
        except asyncio.QueueFull:
            state.status = "failed"
            state.error = {"code": "LIMIT_EXCEEDED", "message": "Browser run queue is full."}
            _persist(state)
            raise RuntimeError("Browser run queue is full.")

        return _submission_summary(state, self.queue.qsize())

    def get(self, run_id: str) -> RunState:
        state = self.runs.get(run_id)
        if state is None:
            raise KeyError(f"Run '{run_id}' was not found.")
        return state

    async def cancel(self, run_id: str) -> dict[str, str]:
        state = self.get(run_id)
        if state.status in {"succeeded", "failed", "cancelled", "timed_out"}:
            return {"runId": run_id, "status": state.status, "message": "Run is already terminal."}
        state.status = "cancelled"
        state.completed_at = datetime.now(UTC)
        state.error = {"code": "CANCELLED", "message": "Browser run was cancelled."}
        if state.process and state.process.returncode is None:
            with contextlib.suppress(ProcessLookupError):
                state.process.send_signal(signal.SIGTERM)
            with contextlib.suppress(asyncio.TimeoutError):
                await asyncio.wait_for(state.process.wait(), timeout=5)
            if state.process.returncode is None:
                with contextlib.suppress(ProcessLookupError):
                    state.process.kill()
        _persist(state)
        return {"runId": run_id, "status": state.status, "message": "Cancellation requested."}

    def get_artifact(self, run_id: str, artifact_id: str) -> dict[str, Any]:
        state = self.get(run_id)
        artifact = next((a for a in state.artifacts if a["id"] == artifact_id), None)
        if artifact is None:
            raise KeyError(f"Artifact '{artifact_id}' was not found for run '{run_id}'.")
        run_dir = _latest_run_dir(state)
        path = run_dir / artifact["path"]
        data = path.read_bytes()
        return {
            "id": artifact["id"],
            "kind": artifact["kind"],
            "contentType": artifact["contentType"],
            "bytes": len(data),
            "dataBase64": base64.b64encode(data).decode("ascii"),
        }

    # -- internal ----------------------------------------------------------

    async def _worker_loop(self) -> None:
        while True:
            run_id = await self.queue.get()
            try:
                state = self.runs.get(run_id)
                if state is None or state.status == "cancelled":
                    continue
                lock = self.request_locks.setdefault("default", asyncio.Lock())
                async with lock:
                    if state.status != "cancelled":
                        await self._run(state)
            finally:
                self.queue.task_done()

    async def _run(self, state: RunState) -> None:
        state.status = "running"
        state.started_at = datetime.now(UTC)
        _persist(state)

        command = _build_command(state)
        env = os.environ.copy()
        env.update({
            "LITELLM_BASE_URL": LITELLM_BASE_URL,
            "LITELLM_API_KEY": LITELLM_API_KEY,
            "WEBWRIGHT_MODEL": state.payload.get("model") or WEBWRIGHT_MODEL,
            "OPENAI_BASE_URL": LITELLM_BASE_URL,
            "OPENAI_API_KEY": LITELLM_API_KEY,
            "LEANKERNEL_BROWSER_RUN_ID": state.run_id,
        })
        log_path = state.run_dir / "sidecar_webwright.log"
        with log_path.open("ab") as log_file:
            try:
                state.process = await asyncio.create_subprocess_exec(
                    *command, stdout=log_file, stderr=log_file, cwd="/app", env=env,
                )
                state.exit_code = await asyncio.wait_for(
                    state.process.wait(), timeout=RUN_WALL_CLOCK_SECONDS,
                )
                if state.status == "cancelled":
                    return
                if state.exit_code == 0:
                    state.status = "succeeded"
                    state.final_datum = _extract_final_datum(state)
                else:
                    state.status = "failed"
                    state.error = {
                        "code": "WEBWRIGHT_FAILED",
                        "message": "Webwright exited with a non-zero status.",
                        "details": {"exitCode": state.exit_code},
                    }
            except asyncio.TimeoutError:
                state.status = "timed_out"
                state.error = {"code": "TIMEOUT", "message": "Browser run exceeded the wall-clock limit."}
                if state.process and state.process.returncode is None:
                    with contextlib.suppress(ProcessLookupError):
                        state.process.kill()
            except Exception as exc:
                state.status = "failed"
                state.error = {"code": "INTERNAL_ERROR", "message": str(exc)}
            finally:
                state.completed_at = datetime.now(UTC)
                state.artifacts = _build_manifest(state)
                _persist(state)

    def _reconcile(self) -> None:
        for run_dir in OUTPUT_ROOT.glob("*"):
            meta = run_dir / METADATA_FILE
            if not meta.is_file():
                continue
            try:
                data = json.loads(meta.read_text(encoding="utf-8"))
                state = _state_from_json(data)
            except (OSError, json.JSONDecodeError, KeyError, TypeError):
                continue
            if state.status in {"queued", "running"}:
                state.status = "failed"
                state.completed_at = datetime.now(UTC)
                state.error = {"code": "SERVICE_RESTARTED", "message": "Service restarted before the run completed."}
                _persist(state)
            self.runs[state.run_id] = state
            rid = state.payload.get("requestId")
            if rid:
                self.idempotency[rid] = state.run_id

    async def _sweep_loop(self) -> None:
        while True:
            await asyncio.sleep(3600)
            cutoff = datetime.now(UTC) - timedelta(hours=ARTIFACT_TTL_HOURS)
            for run_id, state in list(self.runs.items()):
                if state.completed_at and state.completed_at < cutoff:
                    import shutil
                    shutil.rmtree(state.run_dir, ignore_errors=True)
                    self.runs.pop(run_id, None)
                    self._clear_idempotency(run_id)

    def _clear_idempotency(self, run_id: str) -> None:
        for request_id, mapped_run_id in list(self.idempotency.items()):
            if mapped_run_id == run_id:
                self.idempotency.pop(request_id, None)


manager = RunManager()


# ---------------------------------------------------------------------------
# MCP lifespan — start/stop the run manager
# ---------------------------------------------------------------------------

from contextlib import asynccontextmanager
from collections.abc import AsyncIterator


@asynccontextmanager
async def lifespan(_server: FastMCP) -> AsyncIterator[None]:
    await manager.start()
    try:
        yield
    finally:
        await manager.stop()


mcp = FastMCP(
    "leankernel-webwright-mcp",
    json_response=True,
    host="0.0.0.0",
    lifespan=lifespan,
)


# ---------------------------------------------------------------------------
# MCP Tools
# ---------------------------------------------------------------------------

@mcp.tool()
def browser_run_task(
    task: str,
    startUrl: str | None = None,
    requestId: str | None = None,
    model: str | None = None,
) -> dict[str, Any]:
    """Queue a high-level browser task and return a run id."""
    return manager.submit(task, start_url=startUrl, request_id=requestId, model=model)


@mcp.tool()
def browser_get_run(runId: str) -> dict[str, Any]:
    """Get current status for a previously queued browser run."""
    state = manager.get(runId)
    return _state_summary(state)


@mcp.tool()
async def browser_cancel_run(runId: str) -> dict[str, Any]:
    """Cancel a queued or running browser run."""
    return await manager.cancel(runId)


@mcp.tool()
def browser_get_artifact(runId: str, artifactId: str) -> dict[str, Any]:
    """Read a run artifact and return metadata plus base64 payload."""
    return manager.get_artifact(runId, artifactId)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _hash(payload: dict[str, Any]) -> str:
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()
    return hashlib.sha256(encoded).hexdigest()


def _persist(state: RunState) -> None:
    state.run_dir.mkdir(parents=True, exist_ok=True)
    meta = state.run_dir / METADATA_FILE
    meta.write_text(json.dumps(_state_to_json(state), indent=2), encoding="utf-8")


def _build_command(state: RunState) -> list[str]:
    cmd = [
        "python", "/app/app/run_cli.py",
        "-c", str(CONFIG_ROOT / "leankernel.runtime.yaml"),
        "-t", state.payload["task"],
        "--task-id", state.run_id,
        "-o", str(OUTPUT_ROOT),
    ]
    if state.payload.get("startUrl"):
        cmd.extend(["--start-url", state.payload["startUrl"]])
    return cmd


def render_config() -> None:
    src = CONFIG_ROOT / "leankernel.yaml"
    dst = CONFIG_ROOT / "leankernel.runtime.yaml"
    text = src.read_text(encoding="utf-8")
    for key, val in {
        "${LITELLM_BASE_URL}": LITELLM_BASE_URL,
        "${LITELLM_API_KEY}": LITELLM_API_KEY,
        "${WEBWRIGHT_MODEL}": WEBWRIGHT_MODEL,
    }.items():
        text = text.replace(key, val)
    dst.write_text(text, encoding="utf-8")


def _latest_run_dir(state: RunState) -> Path:
    final_runs = state.run_dir / "final_runs"
    candidates = sorted(p for p in final_runs.glob("run_*") if p.is_dir())
    return candidates[-1].resolve() if candidates else state.run_dir


def _build_manifest(state: RunState) -> list[dict[str, Any]]:
    run_dir = _latest_run_dir(state)
    manifest: list[dict[str, Any]] = []
    total = 0

    screenshots_dir = run_dir / "screenshots"
    for i, p in enumerate(sorted(screenshots_dir.glob("*.png")), 1):
        if not p.is_file():
            continue
        size = p.stat().st_size
        if total + size > ARTIFACT_MAX_BYTES:
            continue
        total += size
        manifest.append({
            "id": f"screenshot-{i}-{uuid.uuid4().hex}",
            "kind": "screenshot",
            "displayName": str(p.relative_to(run_dir)),
            "contentType": "image/png",
            "bytes": size,
            "path": str(p.relative_to(run_dir)),
        })

    extras = [
        ("script", "script", run_dir / "final_script.py", "text/x-python"),
        ("log", "log", run_dir / "final_script_log.txt", "text/plain"),
    ]
    for stable_name, kind, path, ct in extras:
        if not path.is_file():
            continue
        size = path.stat().st_size
        if total + size > ARTIFACT_MAX_BYTES:
            continue
        total += size
        manifest.append({
            "id": f"{stable_name}-{uuid.uuid4().hex}",
            "kind": kind,
            "displayName": str(path.relative_to(run_dir)),
            "contentType": ct,
            "bytes": size,
            "path": str(path.relative_to(run_dir)),
        })

    return manifest


def _extract_final_datum(state: RunState) -> str | None:
    run_dir = _latest_run_dir(state)
    report = run_dir / "report.json"
    if report.is_file():
        with contextlib.suppress(json.JSONDecodeError, OSError):
            data = json.loads(report.read_text(encoding="utf-8"))
            for key in ("finalDatum", "final_datum", "answer", "summary"):
                val = data.get(key)
                if isinstance(val, str) and val.strip():
                    return val.strip()
    log = run_dir / "final_script_log.txt"
    if log.is_file():
        with contextlib.suppress(OSError, UnicodeDecodeError):
            text = log.read_text(encoding="utf-8", errors="replace")[-4000:]
            return text.strip() or None
    return None


def _state_to_json(state: RunState) -> dict[str, Any]:
    return {
        "runId": state.run_id,
        "payload": state.payload,
        "payloadHash": state.payload_hash,
        "status": state.status,
        "submittedAt": state.submitted_at.isoformat(),
        "startedAt": state.started_at.isoformat() if state.started_at else None,
        "completedAt": state.completed_at.isoformat() if state.completed_at else None,
        "exitCode": state.exit_code,
        "finalDatum": state.final_datum,
        "artifacts": state.artifacts,
        "error": state.error,
    }


def _state_from_json(data: dict[str, Any]) -> RunState:
    def _parse_dt(v: str | None) -> datetime | None:
        return datetime.fromisoformat(v) if v else None
    return RunState(
        run_id=data["runId"],
        payload=data["payload"],
        payload_hash=data["payloadHash"],
        status=data.get("status", "failed"),
        submitted_at=_parse_dt(data.get("submittedAt")) or datetime.now(UTC),
        started_at=_parse_dt(data.get("startedAt")),
        completed_at=_parse_dt(data.get("completedAt")),
        exit_code=data.get("exitCode"),
        final_datum=data.get("finalDatum"),
        artifacts=data.get("artifacts") or [],
        error=data.get("error"),
    )


def _state_summary(state: RunState) -> dict[str, Any]:
    return {
        "runId": state.run_id,
        "task": state.payload.get("task"),
        "status": state.status,
        "submittedAt": state.submitted_at.isoformat(),
        "startedAt": state.started_at.isoformat() if state.started_at else None,
        "completedAt": state.completed_at.isoformat() if state.completed_at else None,
        "finalDatum": state.final_datum,
        "error": state.error,
        "artifacts": [
            {k: a[k] for k in ("id", "kind", "displayName", "bytes", "contentType") if k in a}
            for a in state.artifacts
        ],
    }


def _submission_summary(state: RunState, queue_size: int | None = None) -> dict[str, Any]:
    result: dict[str, Any] = {
        "runId": state.run_id,
        "status": state.status,
        "submittedAt": state.submitted_at.isoformat(),
    }
    if queue_size is not None:
        result["queuePosition"] = queue_size
    return result


# ---------------------------------------------------------------------------
# Entry point — add /ping health endpoint to the MCP Streamable HTTP app
# ---------------------------------------------------------------------------


async def ping(_request: Request) -> JSONResponse:
    return JSONResponse({"status": "ok"})


app = mcp.streamable_http_app()
app.routes.insert(0, Route("/ping", ping, methods=["GET"]))

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
