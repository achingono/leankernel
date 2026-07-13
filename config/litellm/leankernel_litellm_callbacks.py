from __future__ import annotations

import fcntl
import json
import os
import subprocess
import threading
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from litellm.integrations.custom_logger import CustomLogger


SYNCABLE_PROVIDERS = {"azure", "gemini", "groq"}
PROVIDER_ALIASES = {
    "azure_ai": "azure",
}


def utc_timestamp() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def env_int(name: str, default: int, minimum: int) -> int:
    raw = os.getenv(name, "").strip()
    if not raw:
        return default
    try:
        return max(minimum, int(raw))
    except ValueError:
        return default


def response_model_name(response: Any) -> str | None:
    if response is None:
        return None

    model = getattr(response, "model", None)
    if isinstance(model, str) and model:
        return model

    if isinstance(response, dict):
        model = response.get("model")
        if isinstance(model, str) and model:
            return model

    return None


class LiteLlmRouteMonitor:
    def __init__(self) -> None:
        self.route_log_path = Path(os.getenv("LITELLM_ROUTE_LOG_PATH", "/app/logs/litellm-route-events.jsonl"))
        self.route_log_max_bytes = env_int("LITELLM_ROUTE_LOG_MAX_BYTES", 10 * 1024 * 1024, 1024)
        self.route_log_backup_count = env_int("LITELLM_ROUTE_LOG_BACKUP_COUNT", 5, 1)
        self.sync_status_path = Path(os.getenv("LITELLM_LIMIT_SYNC_STATUS_PATH", "/app/logs/litellm-limit-sync-status.json"))
        self.drift_report_path = Path(os.getenv("LITELLM_LIMIT_DRIFT_REPORT_PATH", "/app/logs/litellm-model-limit-drift.json"))
        self.sync_lock_path = Path(os.getenv("LITELLM_LIMIT_SYNC_LOCK_PATH", "/tmp/litellm-model-limit-sync.lock"))
        self.spec_path = Path(os.getenv("LITELLM_SPEC_PATH", "/app/litellm_spec.yaml"))
        self.render_script_path = Path(os.getenv("LITELLM_RENDER_SCRIPT_PATH", "/app/render_litellm_config.py"))
        self.rendered_config_path = Path(os.getenv("LITELLM_RENDERED_CONFIG_PATH", "/app/config/litellm/litellm_config.generated.yaml"))
        self.sync_script_path = Path(os.getenv("LITELLM_LIMIT_SYNC_SCRIPT_PATH", "/app/sync_litellm_model_limits.py"))
        self.sync_interval_seconds = env_int("LITELLM_LIMIT_SYNC_INTERVAL_SECONDS", 900, 60)
        self.sync_check_seconds = env_int("LITELLM_LIMIT_SYNC_CHECK_SECONDS", 15, 5)
        self.off_hours_restart_enabled = os.getenv("LITELLM_OFF_HOURS_RESTART_ENABLED", "true").strip().lower() in {
            "1", "true", "yes", "on"
        }
        self.off_hours_restart_check_seconds = env_int("LITELLM_OFF_HOURS_RESTART_CHECK_SECONDS", 300, 60)
        self.off_hours_restart_window_start_hour = env_int("LITELLM_OFF_HOURS_RESTART_WINDOW_START_HOUR", 2, 0)
        self.off_hours_restart_window_end_hour = env_int("LITELLM_OFF_HOURS_RESTART_WINDOW_END_HOUR", 5, 1)
        self.off_hours_restart_state_path = Path(
            os.getenv("LITELLM_OFF_HOURS_RESTART_STATE_PATH", "/app/logs/litellm-offhours-restart-state.json")
        )
        self._last_off_hours_restart_check = 0.0
        self._lock = threading.Lock()
        self._pending_providers: set[str] = set()
        self._last_sync_started_at = 0.0
        self._worker_started = False

    def ensure_worker(self) -> None:
        with self._lock:
            if self._worker_started:
                return
            worker = threading.Thread(target=self._sync_worker, name="litellm-limit-sync", daemon=True)
            worker.start()
            self._worker_started = True

    def record_route(self, request_data: dict[str, Any], response: Any, litellm_call_info: dict[str, Any] | None) -> None:
        info = litellm_call_info or {}
        raw_provider = info.get("custom_llm_provider")
        provider = normalize_provider(raw_provider)
        event = {
            "timestamp": utc_timestamp(),
            "requested_model": request_data.get("model"),
            "provider": provider,
            "provider_raw": raw_provider,
            "model_id": info.get("model_id"),
            "api_base": info.get("api_base"),
            "response_model": response_model_name(response),
        }

        self.route_log_path.parent.mkdir(parents=True, exist_ok=True)
        self._rotate_route_log_if_needed()
        with self.route_log_path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(event, sort_keys=True) + "\n")

        print(json.dumps({"event": "litellm_route", **event}, sort_keys=True), flush=True)

        if isinstance(provider, str) and provider in SYNCABLE_PROVIDERS:
            with self._lock:
                self._pending_providers.add(provider)

    def _rotate_route_log_if_needed(self) -> None:
        if not self.route_log_path.exists():
            return

        try:
            if self.route_log_path.stat().st_size < self.route_log_max_bytes:
                return
        except OSError:
            return

        for idx in range(self.route_log_backup_count - 1, 0, -1):
            src = self.route_log_path.with_name(f"{self.route_log_path.name}.{idx}")
            dst = self.route_log_path.with_name(f"{self.route_log_path.name}.{idx + 1}")
            if src.exists():
                src.replace(dst)

        first_backup = self.route_log_path.with_name(f"{self.route_log_path.name}.1")
        self.route_log_path.replace(first_backup)

    def _sync_worker(self) -> None:
        while True:
            time.sleep(self.sync_check_seconds)
            self._check_off_hours_restart()
            providers = self._next_sync_batch()
            if not providers:
                continue
            self._run_sync(providers)

    def _check_off_hours_restart(self) -> None:
        if not self.off_hours_restart_enabled:
            return

        now = time.time()
        if now - self._last_off_hours_restart_check < self.off_hours_restart_check_seconds:
            return
        self._last_off_hours_restart_check = now

        if not self.spec_path.exists():
            return

        spec_mtime = self.spec_path.stat().st_mtime
        state = self._load_off_hours_restart_state(spec_mtime)
        last_restarted_spec_mtime = float(state.get("last_restarted_spec_mtime", spec_mtime))

        # No source-spec update since the last tracked restart checkpoint.
        if spec_mtime <= last_restarted_spec_mtime:
            return

        hour = datetime.now().hour
        if not self._is_off_hours_window(hour):
            return

        state["last_restarted_spec_mtime"] = spec_mtime
        state["last_restart_at"] = utc_timestamp()
        self._write_off_hours_restart_state(state)

        print(
            json.dumps(
                {
                    "event": "litellm_offhours_restart",
                    "reason": "source_spec_updated",
                    "spec_path": str(self.spec_path),
                    "spec_mtime": spec_mtime,
                    "window_start_hour": self.off_hours_restart_window_start_hour,
                    "window_end_hour": self.off_hours_restart_window_end_hour,
                },
                sort_keys=True,
            ),
            flush=True,
        )
        # Exit the process so Docker restart policy can restart the container cleanly.
        os._exit(0)

    def _is_off_hours_window(self, current_hour: int) -> bool:
        start = self.off_hours_restart_window_start_hour % 24
        end = self.off_hours_restart_window_end_hour % 24
        if start == end:
            return True
        if start < end:
            return start <= current_hour < end
        return current_hour >= start or current_hour < end

    def _load_off_hours_restart_state(self, spec_mtime: float) -> dict[str, Any]:
        if not self.off_hours_restart_state_path.exists():
            initial = {
                "initialized_at": utc_timestamp(),
                "last_restarted_spec_mtime": spec_mtime,
                "last_restart_at": None,
            }
            self._write_off_hours_restart_state(initial)
            return initial
        try:
            raw = self.off_hours_restart_state_path.read_text(encoding="utf-8")
            parsed = json.loads(raw)
            if isinstance(parsed, dict):
                return parsed
        except (OSError, json.JSONDecodeError):
            pass
        fallback = {
            "initialized_at": utc_timestamp(),
            "last_restarted_spec_mtime": spec_mtime,
            "last_restart_at": None,
        }
        self._write_off_hours_restart_state(fallback)
        return fallback

    def _write_off_hours_restart_state(self, state: dict[str, Any]) -> None:
        self.off_hours_restart_state_path.parent.mkdir(parents=True, exist_ok=True)
        self.off_hours_restart_state_path.write_text(
            json.dumps(state, indent=2, sort_keys=True), encoding="utf-8"
        )

    def _next_sync_batch(self) -> list[str]:
        with self._lock:
            if not self._pending_providers:
                return []
            if time.time() - self._last_sync_started_at < self.sync_interval_seconds:
                return []
            providers = sorted(self._pending_providers)
            self._pending_providers.clear()
            self._last_sync_started_at = time.time()
            return providers

    def _run_sync(self, providers: list[str]) -> None:
        lock_handle = self._acquire_sync_lock()
        if lock_handle is None:
            with self._lock:
                self._pending_providers.update(providers)
            return

        status = {
            "timestamp": utc_timestamp(),
            "providers": providers,
            "sync_command": [],
            "sync_exit_code": None,
            "sync_stdout": "",
            "sync_stderr": "",
            "render_exit_code": None,
            "render_stdout": "",
            "render_stderr": "",
            "drift_changes": 0,
        }

        try:
            sync_command = [
                "python3",
                str(self.sync_script_path),
                "--config",
                str(self.spec_path),
                "--write",
                "--drift-report",
                str(self.drift_report_path),
                "--providers",
                ",".join(providers),
            ]
            status["sync_command"] = sync_command
            sync_result = subprocess.run(sync_command, capture_output=True, text=True, check=False)
            status["sync_exit_code"] = sync_result.returncode
            status["sync_stdout"] = sync_result.stdout.strip()
            status["sync_stderr"] = sync_result.stderr.strip()

            drift_changes = self._read_drift_change_count()
            status["drift_changes"] = drift_changes

            if sync_result.returncode == 0 and drift_changes > 0:
                render_command = [
                    "python3",
                    str(self.render_script_path),
                    str(self.spec_path),
                    str(self.rendered_config_path),
                ]
                render_result = subprocess.run(render_command, capture_output=True, text=True, check=False)
                status["render_exit_code"] = render_result.returncode
                status["render_stdout"] = render_result.stdout.strip()
                status["render_stderr"] = render_result.stderr.strip()

            if sync_result.returncode != 0:
                with self._lock:
                    self._pending_providers.update(providers)
        finally:
            self._write_status(status)
            print(json.dumps({"event": "litellm_limit_sync", **status}, sort_keys=True), flush=True)
            self._release_sync_lock(lock_handle)

    def _acquire_sync_lock(self):
        self.sync_lock_path.parent.mkdir(parents=True, exist_ok=True)
        handle = self.sync_lock_path.open("a+", encoding="utf-8")
        try:
            fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            return handle
        except BlockingIOError:
            handle.close()
            return None

    def _release_sync_lock(self, handle) -> None:
        try:
            fcntl.flock(handle.fileno(), fcntl.LOCK_UN)
        finally:
            handle.close()

    def _read_drift_change_count(self) -> int:
        if not self.drift_report_path.exists():
            return 0
        try:
            report = json.loads(self.drift_report_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            return 0
        total_changes = report.get("total_changes")
        return total_changes if isinstance(total_changes, int) else 0

    def _write_status(self, status: dict[str, Any]) -> None:
        self.sync_status_path.parent.mkdir(parents=True, exist_ok=True)
        self.sync_status_path.write_text(json.dumps(status, indent=2, sort_keys=True), encoding="utf-8")


monitor = LiteLlmRouteMonitor()
monitor.ensure_worker()


def normalize_provider(provider: Any) -> str | None:
    if not isinstance(provider, str):
        return None
    normalized = provider.strip().lower()
    return PROVIDER_ALIASES.get(normalized, normalized)


class LeanKernelLiteLlmCallbacks(CustomLogger):
    async def async_post_call_response_headers_hook(
        self,
        data: dict[str, Any],
        user_api_key_dict: Any,
        response: Any,
        request_headers: dict[str, str] | None = None,
        litellm_call_info: dict[str, Any] | None = None,
    ) -> dict[str, str] | None:
        monitor.record_route(data, response, litellm_call_info)
        return None


proxy_handler_instance = LeanKernelLiteLlmCallbacks()