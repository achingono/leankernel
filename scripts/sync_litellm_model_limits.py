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


def provider_config(config: dict[str, Any], name: str) -> dict[str, Any] | None:
	providers = config.get("providers", {})
	provider = providers.get(name) if isinstance(providers, dict) else None
	return provider if isinstance(provider, dict) else None


def provider_api_key(provider_spec: dict[str, Any]) -> str | None:
	key_name, _ = first_env_key(provider_spec)
	if not key_name:
		return None
	api_key = os.getenv(key_name, "")
	return api_key or None


def metadata_by_id(data: dict[str, Any], collection_key: str = "data") -> dict[str, dict[str, Any]]:
	by_id: dict[str, dict[str, Any]] = {}
	for raw in data.get(collection_key, []):
		rid = raw.get("id")
		if isinstance(rid, str):
			by_id[rid] = raw
	return by_id


def gemini_metadata_by_name(data: dict[str, Any]) -> dict[str, dict[str, Any]]:
	by_name: dict[str, dict[str, Any]] = {}
	for raw in data.get("models", []):
		name = raw.get("name")
		if isinstance(name, str) and name.startswith("models/"):
			by_name[name.replace("models/", "", 1)] = raw
	return by_name


def updateable_model_name(model: Any) -> str | None:
	if not isinstance(model, dict) or model.get("mode") == "embedding":
		return None
	name = model.get("name")
	return name if isinstance(name, str) else None


def update_model_field(
	provider: str,
	model: dict[str, Any],
	model_id: str,
	model_name: str,
	field: str,
	new_value: Any,
) -> int:
	if not isinstance(new_value, int) or model.get(field) == new_value:
		return 0
	record_drift(provider, model_id, model_name, field, model.get(field), new_value)
	model[field] = new_value
	return 1


def apply_model_limits(
	provider: str,
	model: dict[str, Any],
	model_name: str,
	out_limit: Any,
	in_limit: Any,
) -> int:
	model_id = model.get("id", model_name)
	return update_model_field(provider, model, model_id, model_name, "max_tokens", out_limit) + update_model_field(
		provider,
		model,
		model_id,
		model_name,
		"context_window",
		in_limit,
	)


def update_provider_limits(
	provider: str,
	provider_spec: dict[str, Any],
	metadata_for_name: Any,
	limits_from_metadata: Any,
) -> int:
	changed = 0
	for model in provider_spec.get("models", []):
		name = updateable_model_name(model)
		if not name:
			continue
		meta = metadata_for_name(name)
		if meta:
			changed += apply_model_limits(provider, model, name, *limits_from_metadata(meta))
	return changed


def update_gemini_limits(config: dict[str, Any]) -> int:
	gemini = provider_config(config, "gemini")
	if not gemini:
		return 0
	api_key = provider_api_key(gemini)
	if not api_key:
		return 0

	data = fetch_json(
		f"https://generativelanguage.googleapis.com/v1beta/models?key={urllib.parse.quote(api_key)}"
	)
	if not data:
		return 0

	by_name = gemini_metadata_by_name(data)
	return update_provider_limits(
		"gemini",
		gemini,
		by_name.get,
		lambda meta: (meta.get("outputTokenLimit"), meta.get("inputTokenLimit")),
	)


def azure_models_request(ref: Any) -> tuple[str, dict[str, str]] | None:
	if not isinstance(ref, dict):
		return None
	key_name = ref.get("name")
	api_base_env = ref.get("api_base_env")
	if not isinstance(key_name, str) or not isinstance(api_base_env, str):
		return None
	api_key = os.getenv(key_name, "").strip()
	base = os.getenv(api_base_env, "").strip().rstrip("/")
	if not api_key or not base:
		return None
	return f"{base}/openai/v1/models", {"api-key": api_key, "Authorization": f"Bearer {api_key}"}


def fetch_azure_metadata(azure: dict[str, Any]) -> dict[str, Any] | None:
	keys = azure.get("keys", [])
	if not isinstance(keys, list):
		return None

	for ref in keys:
		request = azure_models_request(ref)
		if not request:
			continue
		url, headers = request
		data = fetch_json(url, headers=headers)
		if data:
			return data
	return None


def update_azure_limits(config: dict[str, Any]) -> int:
	azure = provider_config(config, "azure")
	if not azure:
		return 0

	data = fetch_azure_metadata(azure)
	if not data:
		return 0

	by_id = metadata_by_id(data)
	return update_provider_limits(
		"azure",
		azure,
		lambda name: by_id.get(name) or by_id.get(canonical_azure_model_name(name)),
		lambda meta: (
			meta.get("output_token_limit") or meta.get("max_output_tokens"),
			meta.get("input_token_limit") or meta.get("context_length"),
		),
	)


def update_groq_limits(config: dict[str, Any]) -> int:
	groq = provider_config(config, "groq")
	if not groq:
		return 0
	api_key = provider_api_key(groq)
	if not api_key:
		return 0

	data = fetch_json(
		"https://api.groq.com/openai/v1/models",
		headers={"Authorization": f"Bearer {api_key}"},
	)
	if not data:
		return 0

	by_id = metadata_by_id(data)
	return update_provider_limits(
		"groq",
		groq,
		by_id.get,
		lambda meta: (meta.get("max_completion_tokens"), meta.get("context_window")),
	)


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
