from __future__ import annotations

import asyncio
import contextlib
import hashlib
import hmac
import ipaddress
import json
import os
import re
import shutil
import signal
import socket
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from importlib import metadata
from pathlib import Path
from typing import Any
from urllib.parse import urlparse
from urllib.request import urlopen

from fastapi import Depends, FastAPI, HTTPException, Request
from fastapi.responses import FileResponse, JSONResponse, Response
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from pydantic import BaseModel, Field


OUTPUT_ROOT = Path(os.getenv("OUTPUT_ROOT", "/app/outputs")).resolve()
CONFIG_ROOT = Path(os.getenv("CONFIG_ROOT", "/app/config")).resolve()
API_TOKEN = os.getenv("API_TOKEN", "")
LITELLM_BASE_URL = os.getenv("LITELLM_BASE_URL", "http" + "://litellm:4000")
LITELLM_API_KEY = os.getenv("LITELLM_API_KEY", "")
WEBWRIGHT_MODEL = os.getenv("WEBWRIGHT_MODEL", "gpt-4o")
MAX_CONCURRENT_RUNS = int(os.getenv("MAX_CONCURRENT_RUNS", "2"))
MAX_QUEUE_DEPTH = int(os.getenv("MAX_QUEUE_DEPTH", "20"))
RUN_WALL_CLOCK_SECONDS = int(os.getenv("RUN_WALL_CLOCK_SECONDS", "600"))
ARTIFACT_TTL_HOURS = int(os.getenv("ARTIFACT_TTL_HOURS", "24"))
ARTIFACT_MAX_BYTES = int(os.getenv("ARTIFACT_MAX_BYTES", "50000000"))
TASK_MAX_BYTES = 4096
METADATA_FILE = "leankernel_run.json"
UTC = timezone.utc
BEARER_SECRET_PATTERN = re.compile(rb"(?i)(bearer\s+)[a-z0-9._~+/=-]+")
KEY_SECRET_PATTERN = re.compile(rb"(?i)((?:api[-_]?key|token|secret)\s*[:=]\s*)[^\s,;]+")
ARTIFACT_NOT_FOUND = "Artifact was not found."
METADATA_SERVICE_ADDRESS = ipaddress.ip_address(0xA9FEA9FE)

security = HTTPBearer(auto_error=False)
app = FastAPI(title="LeanKernel Browser Service", version="1.0.0")


class BrowserRunRequest(BaseModel):
    task: str = Field(min_length=1)
    startUrl: str | None = None
    model: str | None = None
    requestKey: str | None = None
    requestId: str | None = None


class ErrorEnvelope(BaseModel):
    code: str
    message: str
    details: dict[str, Any] | None = None


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
    request_key: str = "default"

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
        render_webwright_config()
        self.reconcile_runs()
        for _ in range(MAX_CONCURRENT_RUNS):
            self.workers.append(asyncio.create_task(self.worker_loop()))
        self.sweeper = asyncio.create_task(self.sweep_loop())

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

    def submit(self, request: BrowserRunRequest) -> dict[str, Any]:
        payload = normalize_payload(request)
        payload_hash = hash_payload(payload)
        request_id = payload.get("requestId")
        if request_id:
            existing_id = self.idempotency.get(request_id)
            if existing_id:
                existing = self.runs[existing_id]
                if existing.payload_hash != payload_hash:
                    raise sidecar_error("CONFLICT", "request_id was already used with a different payload.", 409)
                return submission_response(existing)

        run_id = str(uuid.uuid4())
        state = RunState(
            run_id=run_id,
            payload=payload,
            payload_hash=payload_hash,
            request_key=payload.get("requestKey") or "default",
        )
        self.runs[run_id] = state
        if request_id:
            self.idempotency[request_id] = run_id
        state.run_dir.mkdir(parents=True, exist_ok=False)
        self.persist(state)

        try:
            self.queue.put_nowait(run_id)
        except asyncio.QueueFull as exc:
            state.status = "failed"
            state.error = {"code": "LIMIT_EXCEEDED", "message": "Browser run queue is full."}
            self.persist(state)
            raise sidecar_error("LIMIT_EXCEEDED", "Browser run queue is full.", 429) from exc

        return submission_response(state, self.queue.qsize())

    def get(self, run_id: str) -> RunState:
        state = self.runs.get(run_id)
        if state is None:
            raise sidecar_error("NOT_FOUND", f"Browser run '{run_id}' was not found.", 404)
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
        self.persist(state)
        return {"runId": run_id, "status": state.status, "message": "Cancellation requested."}

    async def worker_loop(self) -> None:
        while True:
            run_id = await self.queue.get()
            try:
                state = self.runs.get(run_id)
                if state is None or state.status == "cancelled":
                    continue
                lock = self.request_locks.setdefault(state.request_key, asyncio.Lock())
                async with lock:
                    if state.status != "cancelled":
                        await self.run_webwright(state)
            finally:
                self.queue.task_done()

    async def run_webwright(self, state: RunState) -> None:
        state.status = "running"
        state.started_at = datetime.now(UTC)
        self.persist(state)
        command = build_webwright_command(state)
        env = os.environ.copy()
        env.update(
            {
                "LITELLM_BASE_URL": LITELLM_BASE_URL,
                "LITELLM_API_KEY": LITELLM_API_KEY,
                "WEBWRIGHT_MODEL": state.payload.get("model") or WEBWRIGHT_MODEL,
                "OPENAI_BASE_URL": LITELLM_BASE_URL,
                "OPENAI_API_KEY": LITELLM_API_KEY,
                "LEANKERNEL_BROWSER_RUN_ID": state.run_id,
            }
        )
        log_path = state.run_dir / "sidecar_webwright.log"
        with log_path.open("ab") as log_file:
            try:
                state.process = await asyncio.create_subprocess_exec(
                    *command,
                    stdout=log_file,
                    stderr=log_file,
                    cwd="/app",
                    env=env,
                )
                state.exit_code = await asyncio.wait_for(state.process.wait(), timeout=RUN_WALL_CLOCK_SECONDS)
                if state.status == "cancelled":
                    return
                if state.exit_code == 0:
                    state.status = "succeeded"
                    state.final_datum = extract_final_datum(state)
                else:
                    state.status = "failed"
                    state.error = {
                        "code": "WEBWRIGHT_FAILED",
                        "message": "Webwright exited with a non-zero status.",
                        "details": {"exitCode": state.exit_code, "stderrTail": tail_text(log_path)},
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
                state.artifacts = build_manifest(state)
                self.persist(state)

    def persist(self, state: RunState) -> None:
        state.run_dir.mkdir(parents=True, exist_ok=True)
        metadata_path = state.run_dir / METADATA_FILE
        metadata_path.write_text(json.dumps(state_to_json(state), indent=2), encoding="utf-8")

    def reconcile_runs(self) -> None:
        for run_dir in OUTPUT_ROOT.glob("*"):
            metadata_path = run_dir / METADATA_FILE
            if not metadata_path.is_file():
                continue
            try:
                data = json.loads(metadata_path.read_text(encoding="utf-8"))
                state = state_from_json(data)
            except (OSError, json.JSONDecodeError, KeyError, TypeError):
                continue
            if state.status in {"queued", "running"}:
                state.status = "failed"
                state.completed_at = datetime.now(UTC)
                state.error = {"code": "SERVICE_RESTARTED", "message": "Browser service restarted before the run completed."}
                self.persist(state)
            self.runs[state.run_id] = state
            request_id = state.payload.get("requestId")
            if request_id:
                self.idempotency[request_id] = state.run_id

    async def sweep_loop(self) -> None:
        while True:
            await asyncio.sleep(3600)
            cutoff = datetime.now(UTC) - timedelta(hours=ARTIFACT_TTL_HOURS)
            for run_id, state in list(self.runs.items()):
                completed_at = state.completed_at
                if completed_at is not None and completed_at < cutoff:
                    shutil.rmtree(state.run_dir, ignore_errors=True)
                    self.runs.pop(run_id, None)


manager = RunManager()


@app.on_event("startup")
async def startup() -> None:
    await manager.start()


@app.on_event("shutdown")
async def shutdown() -> None:
    await manager.stop()


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok"}


async def require_auth(credentials: HTTPAuthorizationCredentials | None = Depends(security)) -> None:
    if not API_TOKEN:
        raise sidecar_error("SERVICE_UNAVAILABLE", "Browser service API token is not configured.", 503)
    token = credentials.credentials if credentials and credentials.scheme.lower() == "bearer" else ""
    if not hmac.compare_digest(token, API_TOKEN):
        raise sidecar_error("UNAUTHORIZED", "Browser service authorization failed.", 401)


@app.get("/ready")
async def ready(_: None = Depends(require_auth)) -> dict[str, Any]:
    return {
        "status": "ready" if API_TOKEN else "auth_not_configured",
        "webwrightVersion": package_version("webwright"),
        "playwrightVersion": package_version("playwright"),
        "liteLlmReachable": await probe_litellm(),
        "queueDepth": manager.queue.qsize(),
    }


@app.post("/runs", status_code=202)
async def submit_run(request: BrowserRunRequest, _: None = Depends(require_auth)) -> dict[str, Any]:
    validate_payload(request)
    return manager.submit(request)


@app.get("/runs/{run_id}")
async def get_run(run_id: str, _: None = Depends(require_auth)) -> dict[str, Any]:
    return state_to_response(manager.get(run_id))


@app.get("/runs/{run_id}/artifacts/{artifact_id}")
async def get_artifact(run_id: str, artifact_id: str, _: None = Depends(require_auth)) -> Response:
    state = manager.get(run_id)
    artifact = next((item for item in state.artifacts if item["id"] == artifact_id), None)
    if artifact is None:
        raise sidecar_error("NOT_FOUND", ARTIFACT_NOT_FOUND, 404)

    run_dir = latest_run_dir(state)
    path = confined_path(run_dir, artifact["path"])
    content_type = artifact["contentType"]
    if artifact["kind"] == "log":
        return Response(redact(path.read_bytes()), media_type=content_type)
    return FileResponse(path, media_type=content_type, filename=Path(artifact["displayName"]).name)


@app.delete("/runs/{run_id}")
async def cancel_run(run_id: str, _: None = Depends(require_auth)) -> dict[str, str]:
    return await manager.cancel(run_id)


def validate_payload(request: BrowserRunRequest) -> None:
    if len(request.task.encode("utf-8")) > TASK_MAX_BYTES:
        raise sidecar_error("VALIDATION_ERROR", f"task must be no more than {TASK_MAX_BYTES} UTF-8 bytes.", 400)
    if request.startUrl:
        validate_url(request.startUrl)


def validate_url(value: str) -> None:
    parsed = urlparse(value)
    if parsed.scheme not in {"http", "https"} or not parsed.hostname:
        raise sidecar_error("VALIDATION_ERROR", "startUrl must be an absolute HTTP or HTTPS URL.", 400)
    hostname = parsed.hostname.lower()
    allowlist = split_env("DOMAIN_ALLOWLIST")
    denylist = split_env("DOMAIN_DENYLIST")
    if denylist and any(host_matches(hostname, item) for item in denylist):
        raise sidecar_error("VALIDATION_ERROR", "startUrl host is denied by policy.", 400)
    if allowlist and not any(host_matches(hostname, item) for item in allowlist):
        raise sidecar_error("VALIDATION_ERROR", "startUrl host is not allowed by policy.", 400)
    for address in resolve_host(hostname):
        if is_blocked_ip(address):
            raise sidecar_error("VALIDATION_ERROR", "startUrl resolves to a blocked IP range.", 400)


def resolve_host(hostname: str) -> list[ipaddress._BaseAddress]:
    try:
        infos = socket.getaddrinfo(hostname, None)
    except socket.gaierror as exc:
        raise sidecar_error("VALIDATION_ERROR", "startUrl host could not be resolved.", 400) from exc
    addresses: list[ipaddress._BaseAddress] = []
    for info in infos:
        address = info[4][0]
        with contextlib.suppress(ValueError):
            addresses.append(ipaddress.ip_address(address))
    return addresses


def is_blocked_ip(address: ipaddress._BaseAddress) -> bool:
    return (
        address.is_private
        or address.is_loopback
        or address.is_link_local
        or address.is_multicast
        or address.is_unspecified
        or address == METADATA_SERVICE_ADDRESS
    )


def host_matches(hostname: str, pattern: str) -> bool:
    pattern = pattern.strip().lower()
    return hostname == pattern or hostname.endswith("." + pattern)


def split_env(name: str) -> list[str]:
    return [item.strip() for item in os.getenv(name, "").split(",") if item.strip()]


def normalize_payload(request: BrowserRunRequest) -> dict[str, Any]:
    return {
        "task": request.task.strip(),
        "startUrl": request.startUrl.strip() if request.startUrl else None,
        "model": request.model.strip() if request.model else None,
        "requestKey": request.requestKey.strip() if request.requestKey else None,
        "requestId": request.requestId.strip() if request.requestId else None,
    }


def hash_payload(payload: dict[str, Any]) -> str:
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def build_webwright_command(state: RunState) -> list[str]:
    command = [
        "python",
        "-m",
        "webwright.run.cli",
        "-c",
        str(CONFIG_ROOT / "leankernel.runtime.yaml"),
        "-t",
        state.payload["task"],
        "--task-id",
        state.run_id,
        "-o",
        str(OUTPUT_ROOT),
    ]
    if state.payload.get("startUrl"):
        command.extend(["--start-url", state.payload["startUrl"]])
    return command


def render_webwright_config() -> None:
    template_path = CONFIG_ROOT / "leankernel.yaml"
    rendered_path = CONFIG_ROOT / "leankernel.runtime.yaml"
    text = template_path.read_text(encoding="utf-8")
    replacements = {
        "${LITELLM_BASE_URL}": LITELLM_BASE_URL,
        "${LITELLM_API_KEY}": LITELLM_API_KEY,
        "${WEBWRIGHT_MODEL}": WEBWRIGHT_MODEL,
    }
    for key, value in replacements.items():
        text = text.replace(key, value)
    rendered_path.write_text(text, encoding="utf-8")


def latest_run_dir(state: RunState) -> Path:
    final_runs = state.run_dir / "final_runs"
    candidates = [path for path in final_runs.glob("run_*") if path.is_dir()]
    if not candidates:
        return state.run_dir
    return sorted(candidates)[-1].resolve()


def build_manifest(state: RunState) -> list[dict[str, Any]]:
    run_dir = latest_run_dir(state)
    entries: list[tuple[str, str, Path, str]] = [
        ("script", "script", run_dir / "final_script.py", "text/x-python"),
        ("log", "log", run_dir / "final_script_log.txt", "text/plain"),
    ]
    screenshots_dir = run_dir / "screenshots"
    for index, path in enumerate(sorted(screenshots_dir.glob("*.png")), start=1):
        entries.append((f"screenshot-{index}", "screenshot", path, "image/png"))

    manifest: list[dict[str, Any]] = []
    total_bytes = 0
    for stable_name, kind, path, content_type in entries:
        if not path.is_file():
            continue
        resolved = confined_path(run_dir, str(path.relative_to(run_dir)))
        size = resolved.stat().st_size
        if total_bytes + size > ARTIFACT_MAX_BYTES:
            continue
        total_bytes += size
        manifest.append(
            {
                "id": f"{stable_name}-{uuid.uuid4().hex}",
                "kind": kind,
                "displayName": str(resolved.relative_to(run_dir)),
                "contentType": content_type,
                "bytes": size,
                "path": str(resolved.relative_to(run_dir)),
            }
        )
    return manifest


def confined_path(run_dir: Path, relative_path: str) -> Path:
    if Path(relative_path).is_absolute():
        raise sidecar_error("NOT_FOUND", ARTIFACT_NOT_FOUND, 404)
    root = run_dir.resolve(strict=True)
    candidate = root / relative_path
    if candidate.is_symlink():
        raise sidecar_error("NOT_FOUND", ARTIFACT_NOT_FOUND, 404)
    target = candidate.resolve(strict=True)
    try:
        target.relative_to(root)
    except ValueError as exc:
        raise sidecar_error("NOT_FOUND", ARTIFACT_NOT_FOUND, 404) from exc
    return target


def extract_final_datum(state: RunState) -> str | None:
    run_dir = latest_run_dir(state)
    report_path = run_dir / "report.json"
    if report_path.is_file():
        with contextlib.suppress(json.JSONDecodeError, OSError):
            data = json.loads(report_path.read_text(encoding="utf-8"))
            for key in ("finalDatum", "final_datum", "answer", "summary"):
                value = data.get(key)
                if isinstance(value, str) and value.strip():
                    return value.strip()
    log_path = run_dir / "final_script_log.txt"
    if log_path.is_file():
        text = tail_text(log_path, 4000)
        return text.strip() or None
    return None


def redact(data: bytes) -> bytes:
    redacted = BEARER_SECRET_PATTERN.sub(lambda match: match.group(1) + b"[REDACTED]", data)
    return KEY_SECRET_PATTERN.sub(lambda match: match.group(1) + b"[REDACTED]", redacted)


def tail_text(path: Path, max_chars: int = 2000) -> str:
    with contextlib.suppress(OSError, UnicodeDecodeError):
        return path.read_text(encoding="utf-8", errors="replace")[-max_chars:]
    return ""


async def probe_litellm() -> bool:
    def check() -> bool:
        try:
            with urlopen(f"{LITELLM_BASE_URL.rstrip('/')}/health/liveliness", timeout=3) as response:
                return 200 <= response.status < 300
        except Exception:
            return False

    return await asyncio.to_thread(check)


def package_version(name: str) -> str:
    try:
        return metadata.version(name)
    except metadata.PackageNotFoundError:
        return "unknown"


def state_to_json(state: RunState) -> dict[str, Any]:
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
        "requestKey": state.request_key,
    }


def state_from_json(data: dict[str, Any]) -> RunState:
    return RunState(
        run_id=data["runId"],
        payload=data["payload"],
        payload_hash=data["payloadHash"],
        status=data.get("status", "failed"),
        submitted_at=parse_dt(data.get("submittedAt")) or datetime.now(UTC),
        started_at=parse_dt(data.get("startedAt")),
        completed_at=parse_dt(data.get("completedAt")),
        exit_code=data.get("exitCode"),
        final_datum=data.get("finalDatum"),
        artifacts=data.get("artifacts") or [],
        error=data.get("error"),
        request_key=data.get("requestKey") or "default",
    )


def state_to_response(state: RunState) -> dict[str, Any]:
    response = state_to_json(state)
    response.pop("payload", None)
    response.pop("payloadHash", None)
    response.pop("requestKey", None)
    response["artifacts"] = [dict(artifact) for artifact in state.artifacts]
    for artifact in response["artifacts"]:
        artifact.pop("path", None)
    return response


def submission_response(state: RunState, queue_position: int | None = None) -> dict[str, Any]:
    return {
        "runId": state.run_id,
        "status": state.status,
        "submittedAt": state.submitted_at.isoformat(),
        "queuePosition": queue_position,
    }


def parse_dt(value: str | None) -> datetime | None:
    if not value:
        return None
    return datetime.fromisoformat(value)


def sidecar_error(code: str, message: str, status_code: int, details: dict[str, Any] | None = None) -> HTTPException:
    return HTTPException(status_code=status_code, detail={"code": code, "message": message, "details": details})


@app.exception_handler(HTTPException)
async def http_exception_handler(_: Request, exc: HTTPException) -> JSONResponse:
    detail = exc.detail if isinstance(exc.detail, dict) else {"code": "INTERNAL_ERROR", "message": str(exc.detail)}
    envelope = ErrorEnvelope(**detail)
    return JSONResponse(status_code=exc.status_code, content=envelope.model_dump(exclude_none=True))
