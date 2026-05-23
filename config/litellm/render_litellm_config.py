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
    "callbacks": ["leankernel_litellm_callbacks.proxy_handler_instance"],
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


def merge_settings(defaults: dict[str, Any], overrides: Any, path: str) -> dict[str, Any]:
    merged = copy.deepcopy(defaults)
    if overrides is None:
        return merged

    override_mapping = ensure_mapping(overrides, path)
    for key, value in override_mapping.items():
        merged[key] = copy.deepcopy(value)

    return merged


def merge_callbacks(defaults: list[str], overrides: Any, path: str) -> list[str]:
    callbacks: list[str] = []
    seen: set[str] = set()

    for callback in defaults:
        if callback not in seen:
            callbacks.append(callback)
            seen.add(callback)

    if overrides is None:
        return callbacks

    for callback in ensure_string_list(overrides, path):
        if callback not in seen:
            callbacks.append(callback)
            seen.add(callback)

    return callbacks


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
        if not bool(model_spec.get("enabled", True)):
            continue
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
            "use_responses_api": bool(model_spec.get("use_responses_api", False)),
        }
    return parsed


def parse_provider_keys(provider_name: str, provider_spec: dict[str, Any]) -> dict[str, dict[str, Any]]:
    keys = ensure_list(provider_spec.get("keys", []), f"providers.{provider_name}.keys")
    base_urls = ensure_list(provider_spec.get("base_url", []), f"providers.{provider_name}.base_url")

    prefix = provider_name

    if keys:
        return dict(
            provider_key_entry(provider_name, prefix, idx, key_ref, base_urls)
            for idx, key_ref in enumerate(keys, start=1)
        )

    if base_urls:
        return dict(
            provider_base_url_entry(provider_name, prefix, idx, base_ref)
            for idx, base_ref in enumerate(base_urls, start=1)
        )

    raise SpecValidationError(
        f"providers.{provider_name} must define at least one key or base_url entry"
    )


def provider_key_entry(
    provider_name: str,
    prefix: str,
    idx: int,
    key_ref: Any,
    base_urls: list[Any],
) -> tuple[str, dict[str, Any]]:
    key_path = f"providers.{provider_name}.keys[{idx-1}]"
    key_spec = ensure_mapping(key_ref, key_path)
    api_key_env = parse_env_ref(key_spec, key_path)
    api_base_env_name = provider_key_base_env(provider_name, key_spec, key_path, base_urls, idx)

    required_env = [api_key_env]
    litellm_params: dict[str, Any] = {"api_key": f"os.environ/{api_key_env}"}
    if api_base_env_name:
        required_env.append(api_base_env_name)
        litellm_params["api_base"] = f"os.environ/{api_base_env_name}"

    return f"{prefix}{idx}", {
        "provider": provider_name,
        "required_env": required_env,
        "litellm_params": litellm_params,
        "enabled": bool(key_spec.get("enabled", True)),
    }


def provider_key_base_env(
    provider_name: str,
    key_spec: dict[str, Any],
    key_path: str,
    base_urls: list[Any],
    idx: int,
) -> str | None:
    api_base_env = key_spec.get("api_base_env")
    if api_base_env is not None:
        return ensure_string(api_base_env, f"{key_path}.api_base_env")
    if not base_urls:
        return None

    base_idx = min(idx - 1, len(base_urls) - 1)
    return parse_env_ref(
        base_urls[base_idx],
        f"providers.{provider_name}.base_url[{base_idx}]",
    )


def provider_base_url_entry(
    provider_name: str,
    prefix: str,
    idx: int,
    base_ref: Any,
) -> tuple[str, dict[str, Any]]:
    base_env = parse_env_ref(base_ref, f"providers.{provider_name}.base_url[{idx-1}]")
    litellm_params: dict[str, Any] = {"api_base": f"os.environ/{base_env}"}
    if provider_name in {"ollama", "local"}:
        litellm_params["api_key"] = "local"
    return f"{prefix}{idx}", {
        "provider": provider_name,
        "required_env": [base_env],
        "litellm_params": litellm_params,
        "enabled": True,
    }


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


def parse_providers(
    providers: dict[str, Any],
) -> tuple[
    dict[str, dict[str, dict[str, Any]]],
    dict[str, dict[str, Any]],
    dict[str, list[str]],
    dict[str, str],
]:
    provider_models: dict[str, dict[str, dict[str, Any]]] = {}
    provider_keys: dict[str, dict[str, Any]] = {}
    provider_route_keys: dict[str, list[str]] = {}
    provider_prefixes: dict[str, str] = {}

    for provider_name, raw_provider in providers.items():
        name = ensure_string(provider_name, "providers key")
        provider_spec = ensure_mapping(raw_provider, f"providers.{name}")
        provider_enabled = bool(provider_spec.get("enabled", True))
        provider_prefixes[name] = infer_provider_prefix(name, provider_spec)
        provider_models[name] = parse_provider_models(name, provider_spec)
        if not provider_models[name] and provider_enabled:
            raise SpecValidationError(f"providers.{name}.models must include at least one model")
        parsed_keys = parse_provider_keys(name, provider_spec)
        if not provider_enabled:
            for key_data in parsed_keys.values():
                key_data["enabled"] = False
        provider_keys.update(parsed_keys)
        provider_route_keys[name] = list(parsed_keys.keys())

    return provider_models, provider_keys, provider_route_keys, provider_prefixes


def enabled_key_names(provider_keys: dict[str, dict[str, Any]]) -> set[str]:
    return {
        key_name
        for key_name, key_spec in provider_keys.items()
        if key_spec.get("enabled", True) and all(has_value(env_name) for env_name in key_spec["required_env"])
    }


def model_full_name(provider_prefix: str, model_spec: dict[str, Any]) -> str:
    if model_spec.get("use_responses_api", False):
        return f"{provider_prefix}/responses/{model_spec['name']}"
    return f"{provider_prefix}/{model_spec['name']}"


def build_route_deployments(
    routes: dict[str, Any],
    provider_models: dict[str, dict[str, dict[str, Any]]],
    provider_keys: dict[str, dict[str, Any]],
    provider_route_keys: dict[str, list[str]],
    provider_prefixes: dict[str, str],
    available_keys: set[str],
) -> tuple[dict[str, list[dict[str, Any]]], list[dict[str, Any]]]:
    route_deployments: dict[str, list[dict[str, Any]]] = {}
    model_list: list[dict[str, Any]] = []

    for route_name, raw_entries in routes.items():
        route = ensure_string(route_name, "routes key")
        route_deployments[route] = []
        entries = ensure_list(raw_entries, f"routes.{route}")
        for idx, raw_entry in enumerate(entries):
            deployments = route_entry_deployments(
                route,
                idx,
                raw_entry,
                provider_models,
                provider_keys,
                provider_route_keys,
                provider_prefixes,
                available_keys,
            )
            route_deployments[route].extend(deployments)
            model_list.extend(copy.deepcopy(deployment) for deployment in deployments)

    return route_deployments, model_list


def route_entry_deployments(
    route: str,
    idx: int,
    raw_entry: Any,
    provider_models: dict[str, dict[str, dict[str, Any]]],
    provider_keys: dict[str, dict[str, Any]],
    provider_route_keys: dict[str, list[str]],
    provider_prefixes: dict[str, str],
    available_keys: set[str],
) -> list[dict[str, Any]]:
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

    model_spec = provider_models[provider][model_id]
    selected_keys = entry.get("keys", provider_route_keys[provider])
    return [
        deployment
        for key_name in ensure_string_list(selected_keys, f"{entry_path}.keys")
        if (
            deployment := route_key_deployment(
                route,
                key_name,
                entry_path,
                provider,
                model_spec,
                order,
                provider_keys,
                provider_prefixes,
                available_keys,
            )
        )
    ]


def route_key_deployment(
    route: str,
    key_name: str,
    entry_path: str,
    provider: str,
    model_spec: dict[str, Any],
    order: int | float,
    provider_keys: dict[str, dict[str, Any]],
    provider_prefixes: dict[str, str],
    available_keys: set[str],
) -> dict[str, Any] | None:
    if key_name not in provider_keys:
        raise SpecValidationError(f"{entry_path}.keys references unknown key '{key_name}'")

    key_spec = provider_keys[key_name]
    if key_spec["provider"] != provider:
        raise SpecValidationError(
            f"{entry_path}.keys '{key_name}' belongs to provider '{key_spec['provider']}', not '{provider}'"
        )
    if key_name not in available_keys:
        return None

    litellm_params = copy.deepcopy(key_spec["litellm_params"])
    litellm_params["model"] = model_full_name(provider_prefixes[provider], model_spec)
    litellm_params.update(copy.deepcopy(model_spec.get("litellm_params", {})))
    litellm_params["order"] = order
    return {
        "model_name": route,
        "litellm_params": litellm_params,
        "model_info": copy.deepcopy(model_spec["model_info"]),
    }


def add_alias_deployments(
    aliases: dict[str, Any],
    route_deployments: dict[str, list[dict[str, Any]]],
    model_list: list[dict[str, Any]],
) -> dict[str, str]:
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

    return alias_map


def build_router_settings(
    router: dict[str, Any],
    route_names: set[str],
    alias_names: set[str],
    alias_map: dict[str, str],
) -> dict[str, Any]:
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

    return router_settings


def build_general_settings(router: dict[str, Any], spec_general_settings: Any) -> dict[str, Any]:
    general_settings = merge_settings(GENERAL_SETTINGS_DEFAULT, spec_general_settings, "general_settings")
    if "fallback_on_status_codes" in router:
        general_settings["fallback_on_status_codes"] = ensure_list(
            router["fallback_on_status_codes"], "router.fallback_on_status_codes"
        )
    elif "fallback_on_status_codes" not in general_settings:
        general_settings["fallback_on_status_codes"] = [429, 500, 502, 503, 504]

    return general_settings


def build_litellm_settings(spec_litellm_settings: Any) -> dict[str, Any]:
    litellm_settings = merge_settings(LITELLM_SETTINGS_DEFAULT, spec_litellm_settings, "litellm_settings")
    litellm_settings["callbacks"] = merge_callbacks(
        LITELLM_SETTINGS_DEFAULT.get("callbacks", []),
        litellm_settings.get("callbacks"),
        "litellm_settings.callbacks",
    )
    return litellm_settings


def build_output(spec: dict[str, Any]) -> dict[str, Any]:
    providers = ensure_mapping(spec.get("providers"), "providers")
    routes = ensure_mapping(spec.get("routes"), "routes")
    aliases = ensure_mapping(spec.get("aliases", {}), "aliases")
    router = ensure_mapping(spec.get("router", {}), "router")
    general_settings_spec = spec.get("general_settings")
    litellm_settings_spec = spec.get("litellm_settings")

    provider_models, provider_keys, provider_route_keys, provider_prefixes = parse_providers(providers)
    route_deployments, model_list = build_route_deployments(
        routes,
        provider_models,
        provider_keys,
        provider_route_keys,
        provider_prefixes,
        enabled_key_names(provider_keys),
    )
    alias_map = add_alias_deployments(aliases, route_deployments, model_list)

    return {
        "model_list": model_list,
        "router_settings": build_router_settings(
            router,
            set(route_deployments.keys()),
            set(alias_map.keys()),
            alias_map,
        ),
        "general_settings": build_general_settings(router, general_settings_spec),
        "litellm_settings": build_litellm_settings(litellm_settings_spec),
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
