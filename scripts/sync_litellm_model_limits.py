#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any

import yaml


def load_env_file(path: Path) -> None:
	if not path.exists():
		return
	for line in path.read_text(encoding="utf-8").splitlines():
		raw = line.strip()
		if not raw or raw.startswith("#") or "=" not in raw:
			continue
		key, value = raw.split("=", 1)
		key = key.strip()
		value = value.strip().strip('"').strip("'")
		if key and key not in os.environ:
			os.environ[key] = value


def fetch_json(url: str, headers: dict[str, str] | None = None, timeout: int = 30) -> dict[str, Any] | None:
	req = urllib.request.Request(url, headers=headers or {})
	try:
		with urllib.request.urlopen(req, timeout=timeout) as resp:
			return json.loads(resp.read().decode("utf-8", "ignore"))
	except Exception as exc:
		print(f"WARN: fetch failed for {url}: {exc}", file=sys.stderr)
		return None


def first_env_key(provider_spec: dict[str, Any]) -> tuple[str | None, str | None]:
	keys = provider_spec.get("keys", [])
	if not keys:
		return None, None
	ref = keys[0]
	key_name = ref.get("name") if isinstance(ref, dict) else None
	if not key_name or not os.getenv(key_name):
		return None, None
	api_base_env = ref.get("api_base_env") if isinstance(ref, dict) else None
	base = os.getenv(api_base_env) if api_base_env else None
	return key_name, base


def provider_model_map(provider_spec: dict[str, Any]) -> dict[str, dict[str, Any]]:
	models = provider_spec.get("models", [])
	result: dict[str, dict[str, Any]] = {}
	for m in models:
		if not isinstance(m, dict):
			continue
		mid = m.get("id")
		if isinstance(mid, str) and mid:
			result[mid] = m
	return result


def canonical_azure_model_name(deployment_name: str) -> str:
	return re.sub(r"-\d+$", "", deployment_name)


def update_gemini_limits(config: dict[str, Any]) -> int:
	providers = config.get("providers", {})
	gemini = providers.get("gemini")
	if not isinstance(gemini, dict):
		return 0
	key_name, _ = first_env_key(gemini)
	if not key_name:
		return 0
	api_key = os.getenv(key_name, "")
	if not api_key:
		return 0

	data = fetch_json(
		f"https://generativelanguage.googleapis.com/v1beta/models?key={urllib.parse.quote(api_key)}"
	)
	if not data:
		return 0

	by_name: dict[str, dict[str, Any]] = {}
	for raw in data.get("models", []):
		name = raw.get("name")
		if isinstance(name, str) and name.startswith("models/"):
			by_name[name.replace("models/", "", 1)] = raw

	changed = 0
	for model in gemini.get("models", []):
		if not isinstance(model, dict) or model.get("mode") == "embedding":
			continue
		name = model.get("name")
		if not isinstance(name, str):
			continue
		meta = by_name.get(name)
		if not meta:
			continue
		model_id = model.get("id", name)
		output_limit = meta.get("outputTokenLimit")
		input_limit = meta.get("inputTokenLimit")
		if isinstance(output_limit, int) and model.get("max_tokens") != output_limit:
			record_drift("gemini", model_id, name, "max_tokens", model.get("max_tokens"), output_limit)
			model["max_tokens"] = output_limit
			changed += 1
		if isinstance(input_limit, int) and model.get("context_window") != input_limit:
			record_drift("gemini", model_id, name, "context_window", model.get("context_window"), input_limit)
			model["context_window"] = input_limit
			changed += 1
	return changed


def update_azure_limits(config: dict[str, Any]) -> int:
	providers = config.get("providers", {})
	azure = providers.get("azure")
	if not isinstance(azure, dict):
		return 0

	data: dict[str, Any] | None = None
	keys = azure.get("keys", [])
	if not isinstance(keys, list):
		return 0

	for ref in keys:
		if not isinstance(ref, dict):
			continue
		key_name = ref.get("name")
		api_base_env = ref.get("api_base_env")
		if not isinstance(key_name, str) or not isinstance(api_base_env, str):
			continue
		api_key = os.getenv(key_name, "").strip()
		base = os.getenv(api_base_env, "").strip().rstrip("/")
		if not api_key or not base:
			continue

		data = fetch_json(
			f"{base}/openai/v1/models",
			headers={"api-key": api_key, "Authorization": f"Bearer {api_key}"},
		)
		if data:
			break

	if not data:
		return 0

	by_id: dict[str, dict[str, Any]] = {}
	for raw in data.get("data", []):
		rid = raw.get("id")
		if isinstance(rid, str):
			by_id[rid] = raw

	changed = 0
	for model in azure.get("models", []):
		if not isinstance(model, dict) or model.get("mode") == "embedding":
			continue
		name = model.get("name")
		if not isinstance(name, str):
			continue

		# Azure deployment names in this repo use suffixes like -1, while model metadata often does not.
		candidates = [name, canonical_azure_model_name(name)]
		meta = None
		for candidate in candidates:
			meta = by_id.get(candidate)
			if meta:
				break
		if not meta:
			continue

		model_id = model.get("id", name)
		out_limit = meta.get("output_token_limit") or meta.get("max_output_tokens")
		in_limit = meta.get("input_token_limit") or meta.get("context_length")

		if isinstance(out_limit, int) and model.get("max_tokens") != out_limit:
			record_drift("azure", model_id, name, "max_tokens", model.get("max_tokens"), out_limit)
			model["max_tokens"] = out_limit
			changed += 1
		if isinstance(in_limit, int) and model.get("context_window") != in_limit:
			record_drift("azure", model_id, name, "context_window", model.get("context_window"), in_limit)
			model["context_window"] = in_limit
			changed += 1
	return changed


def update_groq_limits(config: dict[str, Any]) -> int:
	providers = config.get("providers", {})
	groq = providers.get("groq")
	if not isinstance(groq, dict):
		return 0
	key_name, _ = first_env_key(groq)
	if not key_name:
		return 0
	api_key = os.getenv(key_name, "")
	if not api_key:
		return 0

	data = fetch_json(
		"https://api.groq.com/openai/v1/models",
		headers={"Authorization": f"Bearer {api_key}"},
	)
	if not data:
		return 0

	by_id: dict[str, dict[str, Any]] = {}
	for raw in data.get("data", []):
		rid = raw.get("id")
		if isinstance(rid, str):
			by_id[rid] = raw

	changed = 0
	for model in groq.get("models", []):
		if not isinstance(model, dict) or model.get("mode") == "embedding":
			continue
		name = model.get("name")
		if not isinstance(name, str):
			continue
		meta = by_id.get(name)
		if not meta:
			continue
		model_id = model.get("id", name)
		out_limit = meta.get("max_completion_tokens")
		in_limit = meta.get("context_window")
		if isinstance(out_limit, int) and model.get("max_tokens") != out_limit:
			record_drift("groq", model_id, name, "max_tokens", model.get("max_tokens"), out_limit)
			model["max_tokens"] = out_limit
			changed += 1
		if isinstance(in_limit, int) and model.get("context_window") != in_limit:
			record_drift("groq", model_id, name, "context_window", model.get("context_window"), in_limit)
			model["context_window"] = in_limit
			changed += 1
	return changed


def parse_args() -> argparse.Namespace:
	parser = argparse.ArgumentParser(description="Sync LiteLLM model limits from live provider metadata")
	parser.add_argument(
		"--config",
		default="config/litellm/config.yaml",
		help="Path to LiteLLM source config",
	)
	parser.add_argument(
		"--env-file",
		default=".env",
		help="Optional .env path used when env vars are not already exported",
	)
	parser.add_argument(
		"--write",
		action="store_true",
		help="Write updates back to config file (default is dry-run)",
	)
	parser.add_argument(
		"--drift-report",
		metavar="FILE",
		help="Write a JSON drift report to FILE (field-level diff per model)",
	)
	return parser.parse_args()


# ---------------------------------------------------------------------------
# Drift tracking
# ---------------------------------------------------------------------------

_drift_entries: list[dict[str, Any]] = []


def record_drift(provider: str, model_id: str, model_name: str, field: str, old_value: Any, new_value: Any) -> None:
	"""Record a single field-level change for the drift report."""
	_drift_entries.append({
		"provider": provider,
		"model_id": model_id,
		"model_name": model_name,
		"field": field,
		"old_value": old_value,
		"new_value": new_value,
	})
	print(f"  DRIFT [{provider}/{model_id}] {field}: {old_value!r} -> {new_value!r}")


def write_drift_report(path: str) -> None:
	report = {
		"generated_at": __import__("datetime").datetime.utcnow().isoformat() + "Z",
		"total_changes": len(_drift_entries),
		"changes": _drift_entries,
	}
	Path(path).write_text(json.dumps(report, indent=2), encoding="utf-8")
	print(f"Drift report written to {path} ({len(_drift_entries)} change(s))")


def main() -> int:
	args = parse_args()
	config_path = Path(args.config)
	env_path = Path(args.env_file)

	load_env_file(env_path)
	if not config_path.exists():
		print(f"ERROR: config file not found: {config_path}", file=sys.stderr)
		return 1

	config = yaml.safe_load(config_path.read_text(encoding="utf-8"))
	if not isinstance(config, dict):
		print("ERROR: config root must be a mapping", file=sys.stderr)
		return 1

	changes = 0
	changes += update_groq_limits(config)
	changes += update_gemini_limits(config)
	changes += update_azure_limits(config)

	if changes == 0:
		print("No model limit updates found.")
		if args.drift_report:
			write_drift_report(args.drift_report)
		return 0

	print(f"Detected {changes} model-limit field updates.")
	if args.write:
		config_path.write_text(yaml.safe_dump(config, sort_keys=False), encoding="utf-8")
		print(f"Updated {config_path}")
	else:
		print("Dry-run complete. Re-run with --write to persist updates.")

	if args.drift_report:
		write_drift_report(args.drift_report)

	return 0


if __name__ == "__main__":
	raise SystemExit(main())
