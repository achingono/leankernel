#!/usr/bin/env python3
"""
LeanKernel Signal HTTP bridge.

Wraps signal-cli in jsonRpc mode and exposes a minimal REST API so that
LeanKernel-engine can talk to Signal over plain HTTP instead of spawning a
child process. Running signal-cli in its own container eliminates config-lock
contention and makes the whole pipeline more resilient.

Endpoints (compatible with bbernhard/signal-cli-rest-api conventions):
  GET  /v1/health
  GET  /v1/receive/<account>?timeout=<seconds>   — long-poll for inbound messages
  POST /v2/send                                  — send a message
  PUT  /v1/typing-indicator/<account>            — start typing indicator
  DELETE /v1/typing-indicator/<account>          — stop typing indicator
  GET  /v1/attachments/<id>                      — download a received attachment
"""

import asyncio
import json
import os
import sys
import uuid
from pathlib import Path
from typing import Optional

from aiohttp import web

ACCOUNT: str = os.environ.get("SIGNAL_ACCOUNT", "")
CLI_PATH: str = os.environ.get("SIGNAL_CLI_PATH", "/usr/bin/signal-cli")
DATA_DIR: str = os.path.expanduser(
    os.environ.get("SIGNAL_DATA_DIR", "~/.local/share/signal-cli")
)
PORT: int = int(os.environ.get("PORT", "8080"))
DEFAULT_POLL_TIMEOUT: int = int(os.environ.get("RECEIVE_TIMEOUT_SECONDS", "10"))


class SignalBridge:
    """Manages the signal-cli subprocess and marshals JSON-RPC <-> asyncio."""

    def __init__(self) -> None:
        self._process: Optional[asyncio.subprocess.Process] = None
        self._queue: asyncio.Queue = asyncio.Queue()
        self._pending: dict[str, asyncio.Future] = {}
        self._write_lock = asyncio.Lock()

    async def start(self) -> None:
        self._process = await asyncio.create_subprocess_exec(
            CLI_PATH,
            "-a",
            ACCOUNT,
            "jsonRpc",
            stdin=asyncio.subprocess.PIPE,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
        )
        asyncio.create_task(self._read_loop(), name="signal-read")
        asyncio.create_task(self._stderr_loop(), name="signal-stderr")
        print(f"signal-cli started (PID {self._process.pid})", flush=True)

    @property
    def is_healthy(self) -> bool:
        return self._process is not None and self._process.returncode is None

    async def _read_loop(self) -> None:
        assert self._process and self._process.stdout
        while True:
            try:
                line = await self._process.stdout.readline()
                if not line:
                    print("signal-cli stdout closed", file=sys.stderr, flush=True)
                    break
                obj = json.loads(line)
                rid = str(obj.get("id", ""))
                if rid and rid in self._pending:
                    fut = self._pending.pop(rid)
                    if not fut.done():
                        fut.set_result(obj)
                elif obj.get("method") == "receive":
                    await self._queue.put(obj.get("params", {}))
            except Exception as exc:
                print(f"signal-cli read error: {exc}", file=sys.stderr, flush=True)

    async def _stderr_loop(self) -> None:
        assert self._process and self._process.stderr
        while True:
            line = await self._process.stderr.readline()
            if not line:
                break
            print(f"[signal-cli] {line.decode().rstrip()}", file=sys.stderr, flush=True)

    async def _write(self, obj: dict) -> None:
        assert self._process and self._process.stdin
        data = (json.dumps(obj) + "\n").encode()
        async with self._write_lock:
            self._process.stdin.write(data)
            await self._process.stdin.drain()

    async def request(self, method: str, params: dict, timeout: float = 30.0) -> dict:
        rid = uuid.uuid4().hex
        loop = asyncio.get_event_loop()
        fut: asyncio.Future = loop.create_future()
        self._pending[rid] = fut
        await self._write({"jsonrpc": "2.0", "id": rid, "method": method, "params": params})
        try:
            return await asyncio.wait_for(asyncio.shield(fut), timeout=timeout)
        except Exception:
            self._pending.pop(rid, None)
            raise

    async def notify(self, method: str, params: dict) -> None:
        """Fire-and-forget JSON-RPC notification (no id, no response expected)."""
        await self._write({"jsonrpc": "2.0", "method": method, "params": params})


bridge: Optional[SignalBridge] = None


# ── Handlers ─────────────────────────────────────────────────────────────────

async def handle_health(req: web.Request) -> web.Response:
    if bridge and bridge.is_healthy:
        return web.json_response({"status": "ok"})
    return web.json_response({"status": "degraded"}, status=503)


async def handle_receive(req: web.Request) -> web.Response:
    """Long-poll: drain queued messages or block for up to <timeout> seconds."""
    assert bridge
    timeout = int(req.rel_url.query.get("timeout", str(DEFAULT_POLL_TIMEOUT)))
    account = req.match_info["account"]

    msgs: list[dict] = []

    # Drain all messages that are already waiting.
    while not bridge._queue.empty():
        msgs.append(bridge._queue.get_nowait())

    # If none were queued, block until one arrives or the poll window expires.
    if not msgs:
        try:
            msg = await asyncio.wait_for(bridge._queue.get(), timeout=timeout)
            msgs.append(msg)
            # Drain any that arrived while we were waiting.
            while not bridge._queue.empty():
                msgs.append(bridge._queue.get_nowait())
        except asyncio.TimeoutError:
            pass

    result = []
    for m in msgs:
        # Normalise: params may be {envelope:…} or {result:{envelope:…}}
        if "envelope" in m:
            env = m["envelope"]
        elif "result" in m and "envelope" in m["result"]:
            env = m["result"]["envelope"]
        else:
            env = m
        result.append({"envelope": env, "account": account})

    return web.json_response(result)


async def handle_send(req: web.Request) -> web.Response:
    assert bridge
    body = await req.json()
    try:
        await bridge.request(
            "send",
            {
                "recipient": body.get("recipients", []),
                "message": body.get("message", ""),
            },
            timeout=30.0,
        )
        return web.json_response({"timestamp": 0})
    except asyncio.TimeoutError:
        return web.json_response({"error": "send timed out"}, status=504)
    except Exception as exc:
        return web.json_response({"error": str(exc)}, status=500)


async def handle_typing_start(req: web.Request) -> web.Response:
    assert bridge
    body = await req.json()
    try:
        await bridge.notify("sendTyping", {"recipient": body.get("recipient", "")})
    except Exception as exc:
        print(f"typing start failed: {exc}", file=sys.stderr, flush=True)
    return web.json_response({})


async def handle_typing_stop(req: web.Request) -> web.Response:
    assert bridge
    body = await req.json()
    try:
        await bridge.notify(
            "sendTyping", {"recipient": body.get("recipient", ""), "stop": True}
        )
    except Exception as exc:
        print(f"typing stop failed: {exc}", file=sys.stderr, flush=True)
    return web.json_response({})


async def handle_attachment(req: web.Request) -> web.Response:
    aid = req.match_info["id"]
    search_dirs = [
        Path(DATA_DIR) / "attachments",
        Path.home() / ".local" / "share" / "signal-cli" / "attachments",
    ]
    for d in search_dirs:
        if not d.is_dir():
            continue
        # Exact match first (signal-cli-native stores without extension)
        p = d / aid
        if p.is_file():
            return web.FileResponse(p)
        # Match with any extension
        for candidate in d.glob(f"{aid}*"):
            if candidate.is_file():
                return web.FileResponse(candidate)
    return web.json_response({"error": "attachment not found"}, status=404)


# ── App setup ─────────────────────────────────────────────────────────────────

async def on_startup(app: web.Application) -> None:
    global bridge
    bridge = SignalBridge()
    await bridge.start()


def build_app() -> web.Application:
    app = web.Application()
    app.on_startup.append(on_startup)
    app.router.add_get("/v1/health", handle_health)
    app.router.add_get("/v1/receive/{account}", handle_receive)
    app.router.add_post("/v2/send", handle_send)
    app.router.add_put("/v1/typing-indicator/{account}", handle_typing_start)
    app.router.add_delete("/v1/typing-indicator/{account}", handle_typing_stop)
    app.router.add_get("/v1/attachments/{id}", handle_attachment)
    return app


if __name__ == "__main__":
    if not ACCOUNT:
        print("ERROR: SIGNAL_ACCOUNT environment variable is not set", file=sys.stderr)
        sys.exit(1)
    web.run_app(build_app(), host="0.0.0.0", port=PORT, access_log=None, print=None)
