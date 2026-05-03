from __future__ import annotations

import copy
import os
import sys
from pathlib import Path
from typing import Any

import yaml


PROVIDER_PREFIX_OVERRIDES = {
    "azure": "azure_ai",
    "local": "ollama",
    "github_copilot": "openai",
}

GENERAL_SETTINGS_DEFAULT = {
    "master_key": "os.environ/LITELLM_MASTER_KEY",
    "drop_params": True,
    "set_verbose": False,
    "json_logs": True,
    "max_parallel_requests": 100,
    "global_max_parallel_requests": 1000,
}

LITELLM_SETTINGS_DEFAULT = {
    "drop_params": True,
    "set_verbose": False,
    "cache": False,
}

ROUTER_SETTINGS_DEFAULT = {
    "routing_strategy": "simple-shuffle",
    "enable_pre_call_checks": True,
    "num_retries": 7,
    "retry_after": 5,
    "timeout": 120,
    "cooldown_time": 60,
}


class SpecValidationError(ValueError):
    pass


def has_value(name: str) -> bool:
    return bool(os.getenv(name, "").strip())


def ensure_mapping(value: Any, path: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise SpecValidationError(f"{path} must be a mapping")
    return value


def ensure_list(value: Any, path: str) -> list[Any]:
    if not isinstance(value, list):
        raise SpecValidationError(f"{path} must be a list")
    return value


def ensure_string(value: Any, path: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise SpecValidationError(f"{path} must be a non-empty string")
    return value


def ensure_number(value: Any, path: str) -> int | float:
    if not isinstance(value, (int, float)):
        raise SpecValidationError(f"{path} must be numeric")
    return value


def ensure_string_list(value: Any, path: str) -> list[str]:
    values = ensure_list(value, path)
    output: list[str] = []
    for idx, item in enumerate(values):
        output.append(ensure_string(item, f"{path}[{idx}]"))
    return output


def parse_env_ref(value: Any, path: str) -> str:
    env_ref = ensure_mapping(value, path)
    source = ensure_string(env_ref.get("source"), f"{path}.source")
    if source != "env":
        raise SpecValidationError(f"{path}.source must be 'env'")
    return ensure_string(env_ref.get("name"), f"{path}.name")


def infer_provider_prefix(provider_name: str, provider_spec: dict[str, Any]) -> str:
    explicit = provider_spec.get("litellm_provider")
    if explicit is not None:
        return ensure_string(explicit, f"providers.{provider_name}.litellm_provider")
    return PROVIDER_PREFIX_OVERRIDES.get(provider_name, provider_name)


def parse_provider_models(provider_name: str, provider_spec: dict[str, Any]) -> dict[str, dict[str, Any]]:
    path = f"providers.{provider_name}.models"
    raw_models = ensure_list(provider_spec.get("models", []), path)
    parsed: dict[str, dict[str, Any]] = {}
    for idx, raw_model in enumerate(raw_models):
        model_path = f"{path}[{idx}]"
        model_spec = ensure_mapping(raw_model, model_path)
        model_id = ensure_string(model_spec.get("id"), f"{model_path}.id")
        name = ensure_string(model_spec.get("name"), f"{model_path}.name")
        mode = model_spec.get("mode", "chat")
        mode = ensure_string(mode, f"{model_path}.mode")
        raw_litellm_params = model_spec.get("litellm_params", {})
        litellm_params = ensure_mapping(raw_litellm_params, f"{model_path}.litellm_params")
        model_info: dict[str, Any]
        if mode == "embedding":
            dimensions = model_spec.get("dimensions")
            if dimensions is None:
                raise SpecValidationError(f"{model_path}.dimensions is required for embedding models")
            model_info = {"mode": "embedding", "dimensions": ensure_number(dimensions, f"{model_path}.dimensions")}
        else:
            max_tokens = model_spec.get("max_tokens", model_spec.get("context_window", 8192))
            model_info = {
                "mode": "chat",
                "max_tokens": ensure_number(max_tokens, f"{model_path}.max_tokens"),
                "supports_function_calling": bool(model_spec.get("supports_function_calling", True)),
            }
        parsed[model_id] = {
            "name": name,
            "model_info": model_info,
            "litellm_params": copy.deepcopy(litellm_params),
        }
    return parsed


def parse_provider_keys(provider_name: str, provider_spec: dict[str, Any]) -> dict[str, dict[str, Any]]:
    keys = ensure_list(provider_spec.get("keys", []), f"providers.{provider_name}.keys")
    base_urls = ensure_list(provider_spec.get("base_url", []), f"providers.{provider_name}.base_url")

    parsed: dict[str, dict[str, Any]] = {}
    prefix = provider_name

    if keys:
        for idx, key_ref in enumerate(keys, start=1):
            key_path = f"providers.{provider_name}.keys[{idx-1}]"
            key_spec = ensure_mapping(key_ref, key_path)
            api_key_env = parse_env_ref(key_spec, key_path)
            api_base_env = key_spec.get("api_base_env")
            api_base_env_name = None
            if api_base_env is not None:
                api_base_env_name = ensure_string(api_base_env, f"{key_path}.api_base_env")
            elif base_urls:
                url_ref = parse_env_ref(
                    base_urls[min(idx - 1, len(base_urls) - 1)],
                    f"providers.{provider_name}.base_url[{min(idx - 1, len(base_urls) - 1)}]",
                )
                api_base_env_name = url_ref

            required_env = [api_key_env]
            litellm_params: dict[str, Any] = {"api_key": f"os.environ/{api_key_env}"}
            if api_base_env_name:
                required_env.append(api_base_env_name)
                litellm_params["api_base"] = f"os.environ/{api_base_env_name}"

            parsed[f"{prefix}{idx}"] = {
                "provider": provider_name,
                "required_env": required_env,
                "litellm_params": litellm_params,
            }
    elif base_urls:
        for idx, base_ref in enumerate(base_urls, start=1):
            base_env = parse_env_ref(base_ref, f"providers.{provider_name}.base_url[{idx-1}]")
            litellm_params: dict[str, Any] = {"api_base": f"os.environ/{base_env}"}
            if provider_name in {"ollama", "local"}:
                litellm_params["api_key"] = "local"
            parsed[f"{prefix}{idx}"] = {
                "provider": provider_name,
                "required_env": [base_env],
                "litellm_params": litellm_params,
            }
    else:
        raise SpecValidationError(
            f"providers.{provider_name} must define at least one key or base_url entry"
        )

    return parsed


def normalize_fallbacks(
    fallback_map: dict[str, Any], path: str, valid_sources: set[str], valid_targets: set[str]
) -> list[dict[str, list[str]]]:
    result: list[dict[str, list[str]]] = []
    for source, raw_targets in fallback_map.items():
        source_name = ensure_string(source, f"{path} key")
        if source_name not in valid_sources:
            raise SpecValidationError(f"{path}.{source_name} references unknown source")
        targets = ensure_string_list(raw_targets, f"{path}.{source_name}")
        for idx, target in enumerate(targets):
            if target not in valid_targets:
                raise SpecValidationError(
                    f"{path}.{source_name}[{idx}] references unknown target '{target}'"
                )
        result.append({source_name: targets})
    return result


def build_output(spec: dict[str, Any]) -> dict[str, Any]:
    providers = ensure_mapping(spec.get("providers"), "providers")
    routes = ensure_mapping(spec.get("routes"), "routes")
    aliases = ensure_mapping(spec.get("aliases", {}), "aliases")
    router = ensure_mapping(spec.get("router", {}), "router")

    provider_models: dict[str, dict[str, dict[str, Any]]] = {}
    provider_keys: dict[str, dict[str, Any]] = {}
    provider_route_keys: dict[str, list[str]] = {}
    provider_prefixes: dict[str, str] = {}

    for provider_name, raw_provider in providers.items():
        name = ensure_string(provider_name, "providers key")
        provider_spec = ensure_mapping(raw_provider, f"providers.{name}")
        provider_prefixes[name] = infer_provider_prefix(name, provider_spec)
        provider_models[name] = parse_provider_models(name, provider_spec)
        if not provider_models[name]:
            raise SpecValidationError(f"providers.{name}.models must include at least one model")
        parsed_keys = parse_provider_keys(name, provider_spec)
        provider_keys.update(parsed_keys)
        provider_route_keys[name] = list(parsed_keys.keys())

    available_keys = {
        key_name
        for key_name, key_spec in provider_keys.items()
        if all(has_value(env_name) for env_name in key_spec["required_env"])
    }

    route_deployments: dict[str, list[dict[str, Any]]] = {route_name: [] for route_name in routes}
    model_list: list[dict[str, Any]] = []

    for route_name, raw_entries in routes.items():
        route = ensure_string(route_name, "routes key")
        entries = ensure_list(raw_entries, f"routes.{route}")
        for idx, raw_entry in enumerate(entries):
            entry_path = f"routes.{route}[{idx}]"
            entry = ensure_mapping(raw_entry, entry_path)
            provider = ensure_string(entry.get("provider"), f"{entry_path}.provider")
            model_id = ensure_string(entry.get("model"), f"{entry_path}.model")
            order = ensure_number(entry.get("order"), f"{entry_path}.order")
            if provider not in provider_models:
                raise SpecValidationError(f"{entry_path}.provider '{provider}' is not defined")
            if model_id not in provider_models[provider]:
                raise SpecValidationError(
                    f"{entry_path}.model '{model_id}' is not defined under providers.{provider}.models"
                )
            selected_keys = entry.get("keys", provider_route_keys[provider])
            selected_keys = ensure_string_list(selected_keys, f"{entry_path}.keys")
            model_spec = provider_models[provider][model_id]
            full_model_name = f"{provider_prefixes[provider]}/{model_spec['name']}"

            for key_name in selected_keys:
                if key_name not in provider_keys:
                    raise SpecValidationError(f"{entry_path}.keys references unknown key '{key_name}'")
                key_spec = provider_keys[key_name]
                if key_spec["provider"] != provider:
                    raise SpecValidationError(
                        f"{entry_path}.keys '{key_name}' belongs to provider '{key_spec['provider']}', not '{provider}'"
                    )
                if key_name not in available_keys:
                    continue
                litellm_params = copy.deepcopy(key_spec["litellm_params"])
                litellm_params["model"] = full_model_name
                litellm_params.update(copy.deepcopy(model_spec.get("litellm_params", {})))
                litellm_params["order"] = order
                deployment = {
                    "model_name": route,
                    "litellm_params": litellm_params,
                    "model_info": copy.deepcopy(model_spec["model_info"]),
                }
                route_deployments[route].append(deployment)
                model_list.append(copy.deepcopy(deployment))

    alias_map: dict[str, str] = {}
    for alias_name, route_name in aliases.items():
        alias = ensure_string(alias_name, "aliases key")
        route = ensure_string(route_name, f"aliases.{alias}")
        if route not in route_deployments:
            raise SpecValidationError(f"aliases.{alias} points to unknown route '{route}'")
        alias_map[alias] = route
        for deployment in route_deployments[route]:
            alias_deployment = copy.deepcopy(deployment)
            alias_deployment["model_name"] = alias
            model_list.append(alias_deployment)

    route_names = set(route_deployments.keys())
    alias_names = set(alias_map.keys())
    fallback_targets = route_names | alias_names

    route_fallbacks = normalize_fallbacks(
        ensure_mapping(router.get("route_fallbacks", {}), "router.route_fallbacks"),
        "router.route_fallbacks",
        valid_sources=route_names,
        valid_targets=fallback_targets,
    )
    alias_fallbacks = normalize_fallbacks(
        ensure_mapping(router.get("alias_fallbacks", {}), "router.alias_fallbacks"),
        "router.alias_fallbacks",
        valid_sources=alias_names,
        valid_targets=fallback_targets,
    )

    router_settings = copy.deepcopy(ROUTER_SETTINGS_DEFAULT)
    for key in ROUTER_SETTINGS_DEFAULT:
        if key in router:
            router_settings[key] = router[key]
    router_settings["fallbacks"] = route_fallbacks + alias_fallbacks
    router_settings["model_group_alias"] = alias_map

    general_settings = copy.deepcopy(GENERAL_SETTINGS_DEFAULT)
    if "fallback_on_status_codes" in router:
        general_settings["fallback_on_status_codes"] = ensure_list(
            router["fallback_on_status_codes"], "router.fallback_on_status_codes"
        )
    else:
        general_settings["fallback_on_status_codes"] = [429, 500, 502, 503, 504]

    return {
        "model_list": model_list,
        "router_settings": router_settings,
        "general_settings": general_settings,
        "litellm_settings": copy.deepcopy(LITELLM_SETTINGS_DEFAULT),
    }


def render_config(spec_path: Path, output_path: Path) -> None:
    with spec_path.open("r", encoding="utf-8") as handle:
        loaded = yaml.safe_load(handle)
    spec = ensure_mapping(loaded, "root")
    rendered = build_output(spec)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8") as handle:
        yaml.safe_dump(rendered, handle, sort_keys=False)


def main() -> int:
    spec_path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("/app/litellm_spec.yaml")
    output_path = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("/tmp/litellm_config.yaml")
    try:
        render_config(spec_path, output_path)
    except (SpecValidationError, yaml.YAMLError, OSError) as exc:
        print(f"Failed to render LiteLLM config: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
