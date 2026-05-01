import os
import sys
from pathlib import Path

OPTIONAL_PROVIDER_MARKERS = {
    '<<: *provider-groq1': ("GROQ_API_KEY",),
    '<<: *provider-groq2': ("GROQ_API_KEY_2",),
    '<<: *provider-gemini1': ("GEMINI_API_KEY",),
    '<<: *provider-gemini2': ("GEMINI_API_KEY_2",),
    '<<: *provider-gemini3': ("GEMINI_API_KEY_3",),
    '<<: *provider-azure1': ("AZURE_AI_API_KEY", "AZURE_AI_API_BASE"),
    '<<: *provider-azure2': ("AZURE_AI_API_KEY_2", "AZURE_AI_API_BASE_2"),
    '<<: *provider-local': ("OLLAMA_BASE_URL",),
}

def has_value(name: str) -> bool:
    return bool(os.getenv(name, "").strip())

def has_values(names: tuple[str, ...]) -> bool:
    return all(has_value(name) for name in names)

def should_keep_block(block: list[str]) -> bool:
    block_text = "".join(block)
    for marker, env_names in OPTIONAL_PROVIDER_MARKERS.items():
        if marker in block_text:
            return has_values(env_names)
    return True

def filter_model_blocks(lines: list[str]) -> list[str]:
    output: list[str] = []
    in_model_list = False
    current_block: list[str] = []
    for line in lines:
        if not in_model_list:
            output.append(line)
            if line.startswith("model_list:"):
                in_model_list = True
            continue
        if line.startswith("router_settings:"):
            if current_block and should_keep_block(current_block):
                output.extend(current_block)
            current_block = []
            output.append(line)
            in_model_list = False
            continue
        if line.startswith("  - model_name:"):
            if current_block and should_keep_block(current_block):
                output.extend(current_block)
            current_block = [line]
            continue
        if current_block:
            current_block.append(line)
        else:
            output.append(line)
    if current_block and should_keep_block(current_block):
        output.extend(current_block)
    return output

def render_config(template_path: Path, output_path: Path) -> None:
    with template_path.open("r", encoding="utf-8") as handle:
        rendered_lines = filter_model_blocks(handle.readlines())
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8") as handle:
        handle.writelines(rendered_lines)

def main() -> int:
    template_path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("/app/config.yaml")
    output_path = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("/tmp/litellm_config.yaml")
    render_config(template_path, output_path)
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
