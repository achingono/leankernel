#!/usr/bin/env python3
"""Import wiki markdown into the canonical LeanKernel/GBrain format.

The importer is intentionally conservative:
- It preserves existing canonical files.
- It rewrites legacy wiki pages into the canonical frontmatter + `lk-facts`
  structure.
- It keeps historical/conflicting facts instead of collapsing them away.
- In dry-run mode it can emit a full preview tree and a machine-readable report
  without touching the source corpus.
"""

from __future__ import annotations

import argparse
import dataclasses
import difflib
import json
import re
import sys
import os
import urllib.request
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import yaml


DIMENSIONS = {"who", "what", "when", "where", "why", "how"}
FACT_CONTEXT_KEYS = ("who", "what", "when", "where", "why", "how")
FRONTMATTER_KEYS = ("id", "type", "dimension", "subject", "summary", "lastAccessed", "accessCount", "aliases", "tags")
FACT_TOP_LEVEL_KEYS = {"claim", "normalizedKey", "sourceQuote", "source", "confidence", "lastConfirmed", "context", "tags"}


@dataclass
class WikiFact:
    claim: str
    normalized_key: str
    source_quote: str = ""
    source: str = ""
    confidence: float = 0.0
    last_confirmed: str = ""
    context: dict[str, str] = field(default_factory=dict)
    tags: list[str] = field(default_factory=list)


@dataclass
class WikiEntry:
    source_path: Path
    relative_source_path: Path
    target_path: Path
    frontmatter: dict[str, Any]
    subject: str
    dimension: str
    summary: str
    aliases: list[str]
    tags: list[str]
    related_lines: list[str]
    facts: list[WikiFact]
    source_text: str
    rendered_text: str
    status: str
    notes: list[str] = field(default_factory=list)


def main() -> int:
    args = parse_args()
    source_root = args.source_root.resolve()
    target_root = args.target_root.resolve()
    dry_run = args.dry_run or not args.write

    if not source_root.exists():
        print(f"Source root does not exist: {source_root}", file=sys.stderr)
        return 2

    entries: list[WikiEntry] = []
    seen = 0
    for file_path in sorted(source_root.rglob("*.md")):
        if should_skip(file_path):
            continue
        if seen < args.offset:
            seen += 1
            continue
        if args.limit and len(entries) >= args.limit:
            break
        entries.append(import_entry(file_path, source_root, target_root, args))
        seen += 1

    report = build_report(entries, source_root, target_root, dry_run)

    if args.preview_dir:
        write_preview(entries, args.preview_dir.resolve(), source_root)
        write_json(report, args.preview_dir.resolve() / "import-report.json")

    if args.write and not dry_run:
        write_entries(entries, target_root)
        write_json(build_index_payload(entries), target_root / ".LeanKernel" / "index.json")
        write_json(build_migration_ledger(entries), target_root / ".LeanKernel" / "migration.json")
        (target_root / ".LeanKernel" / "migration.completed").write_text(
            datetime.now(timezone.utc).isoformat(),
            encoding="utf-8",
        )

    if args.report_json:
        write_json(report, args.report_json.resolve())
    else:
        print(json.dumps(report, indent=2, ensure_ascii=False))

    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Import LeanKernel wiki markdown.")
    parser.add_argument(
        "--source-root",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "data" / "wiki",
        help="Source wiki root (default: repo data/wiki).",
    )
    parser.add_argument(
        "--target-root",
        type=Path,
        default=Path("/tmp/leankernel-wiki-import-target"),
        help="Canonical wiki root to write when --write is set.",
    )
    parser.add_argument(
        "--preview-dir",
        type=Path,
        help="Optional directory to receive dry-run preview files and report.",
    )
    parser.add_argument(
        "--report-json",
        type=Path,
        help="Write the machine-readable report to this path instead of stdout.",
    )
    parser.add_argument(
        "--write",
        action="store_true",
        help="Write canonical files and metadata instead of only simulating.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        default=False,
        help="Render and validate output without modifying the target root.",
    )

    parser.add_argument(
        "--extract-llm",
        action="store_true",
        default=False,
        help="Use an LLM (local Ollama) to extract facts from wiki body instead of parsing lk-facts blocks.",
    )
    parser.add_argument(
        "--consolidate-llm",
        action="store_true",
        default=False,
        help="Use an LLM to consolidate/dedupe facts across legacy lk-facts and ## Facts.",
    )
    parser.add_argument(
        "--llm-provider",
        type=str,
        default="ollama",
        choices=["ollama", "litellm"],
        help="LLM backend provider (default: ollama).",
    )
    parser.add_argument(
        "--llm-model",
        type=str,
        default="",
        help="Model name (overrides env vars OLLAMA_MODEL / LITELLM_MODEL).",
    )
    parser.add_argument(
        "--llm-temperature",
        type=float,
        default=0.0,
        help="Temperature for LLM extraction/consolidation.",
    )
    parser.add_argument(
        "--ollama-base-url",
        type=str,
        default="",
        help="Ollama server URL (overrides OLLAMA_BASE_URL env, default http://localhost:11434).",
    )
    parser.add_argument(
        "--offset",
        type=int,
        default=0,
        help="Skip the first N wiki files (after skip filter). Default: 0.",
    )
    parser.add_argument(
        "--limit",
        type=int,
        default=0,
        help="Only process the first N wiki files (after skip filter). Default: 0 = no limit.",
    )
    return parser.parse_args()


def should_skip(path: Path) -> bool:
    return any(part.startswith(".") for part in path.parts)


def import_entry(file_path: Path, source_root: Path, target_root: Path, args: argparse.Namespace) -> WikiEntry:
    source_text = file_path.read_text(encoding="utf-8")
    frontmatter, body = split_frontmatter(source_text)
    fm = parse_frontmatter_block(frontmatter) if frontmatter.strip() else {}
    if not isinstance(fm, dict):
        fm = {}

    dimension = str(fm.get("dimension") or infer_dimension(file_path, source_root)).strip().lower()
    subject = str(fm.get("subject") or infer_subject_from_path(file_path)).strip()
    summary = normalize_whitespace(str(fm.get("summary") or extract_section_text(body, "Summary") or ""))
    aliases = normalize_string_list(fm.get("aliases"))
    tags = normalize_string_list(fm.get("tags"))
    related_lines = extract_related_lines(body)

    facts = parse_facts(body, subject, dimension, tags, file_path, args)
    if not facts:
        facts = fallback_fact_from_body(body, subject, dimension, file_path, tags)

    target_path = target_root / dimension / f"{slugify(subject)}.md"
    entry_id = f"{dimension}-{slugify(subject)}"
    fm_out = build_frontmatter(entry_id, dimension, subject, summary, fm, aliases, tags)

    rendered_text = render_entry(fm_out, subject, summary, facts, aliases, related_lines)
    status = "unchanged" if normalize_newlines(rendered_text) == normalize_newlines(source_text) else "normalized"

    notes = []
    if not body_contains_lk_facts(body):
        notes.append("legacy-format-converted")
    if any(f.tags and ("historical" in f.tags or "superseded" in f.tags) for f in facts):
        notes.append("historical-facts-preserved")
    conflicts = detect_conflicts(facts)
    notes.extend(conflicts)

    return WikiEntry(
        source_path=file_path,
        relative_source_path=file_path.relative_to(source_root),
        target_path=target_path,
        frontmatter=fm_out,
        subject=subject,
        dimension=dimension,
        summary=summary,
        aliases=aliases,
        tags=tags,
        related_lines=related_lines,
        facts=facts,
        source_text=source_text,
        rendered_text=rendered_text,
        status=status,
        notes=notes,
    )


def split_frontmatter(text: str) -> tuple[str, str]:
    if not text.startswith("---\n"):
        return "", text
    end = text.find("\n---\n", 4)
    if end == -1:
        return "", text
    frontmatter = text[4:end]
    body = text[end + 5 :]
    return frontmatter, body.lstrip("\n")


def parse_frontmatter_block(frontmatter: str) -> dict[str, Any]:
    parsed: dict[str, Any] = {}
    current_list_key: str | None = None

    for raw_line in frontmatter.splitlines():
        line = raw_line.rstrip()
        stripped = line.strip()
        if not stripped:
            continue

        if stripped.startswith("- ") and current_list_key:
            parsed.setdefault(current_list_key, []).append(unquote_yaml_value(stripped[2:].strip()) or "")
            continue

        if ":" not in stripped:
            current_list_key = None
            continue

        key, value = stripped.split(":", 1)
        key = key.strip()
        value = value.strip()
        if value == "":
            parsed[key] = []
            current_list_key = key
        else:
            parsed[key] = unquote_yaml_value(value) or ""
            current_list_key = None

    return parsed


def infer_dimension(file_path: Path, source_root: Path) -> str:
    rel = file_path.relative_to(source_root)
    candidate = rel.parts[0] if rel.parts else ""
    return candidate if candidate in DIMENSIONS else "what"


def infer_subject_from_path(file_path: Path) -> str:
    return file_path.stem.replace("-", " ").strip()


def normalize_string_list(value: Any) -> list[str]:
    if not value:
        return []
    if isinstance(value, list):
        return [normalize_whitespace(str(item)) for item in value if normalize_whitespace(str(item))]
    if isinstance(value, str):
        return [normalize_whitespace(value)] if normalize_whitespace(value) else []
    return [normalize_whitespace(str(value))]


def body_contains_lk_facts(body: str) -> bool:
    return "```yaml lk-facts" in body.lower()


def _llm_chat(
    messages: list[dict[str, str]],
    args: argparse.Namespace,
    timeout: int = 120,
) -> dict[str, Any] | None:
    """Send a chat request to the configured LLM provider and return the parsed completion body."""
    provider = (args.llm_provider or os.environ.get("LLM_PROVIDER") or "ollama").strip().lower()
    model = args.llm_model.strip() or os.environ.get("OLLAMA_MODEL") or os.environ.get("LITELLM_MODEL") or "llama3.2"
    temperature = float(args.llm_temperature)

    if provider == "ollama":
        base_url = args.ollama_base_url.strip() or os.environ.get("OLLAMA_BASE_URL") or "http://localhost:11434"
        url = base_url.rstrip("/") + "/api/chat"
        headers = {"Content-Type": "application/json"}
        request = {
            "model": model,
            "stream": False,
            "options": {"temperature": temperature},
            "messages": messages,
        }
    else:
        base_url = os.environ.get("LITELLM_BASE_URL") or os.environ.get("LITELLM_URL") or "http://litellm:4000/"
        api_key = os.environ.get("LITELLM_API_KEY") or os.environ.get("LITELLM_KEY") or ""
        url = base_url.rstrip("/") + "/chat/completions"
        headers = {"Content-Type": "application/json"}
        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"
        request = {
            "model": model,
            "temperature": temperature,
            "messages": messages,
        }

    req = urllib.request.Request(
        url,
        data=json.dumps(request).encode("utf-8"),
        headers=headers,
        method="POST",
    )

    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except Exception as e:
        print(f"LLM request failed: {e}", file=sys.stderr)
        return None


def _extract_content(completion: dict[str, Any] | None, provider: str) -> str | None:
    if completion is None:
        return None
    try:
        if provider == "ollama":
            return completion.get("message", {}).get("content")
        return completion.get("choices", [{}])[0].get("message", {}).get("content")
    except (KeyError, IndexError, TypeError):
        return None


def extract_facts_llm(
    body: str,
    subject: str,
    dimension: str,
    entry_tags: list[str],
    file_path: Path,
    args: argparse.Namespace,
) -> list[WikiFact]:
    """Extract facts from wiki body using an LLM (default: local Ollama).

    Falls back to traditional parsing if the LLM call fails.
    """
    system = (
        "You extract structured knowledge facts from wiki pages. "
        "Return ONLY valid JSON — an array of fact objects. "
        "No explanation, no markdown fences."
    )

    user = {
        "instruction": (
            "Extract all distinct factual claims from the wiki page below. "
            "For each fact output an object with these keys:\n"
            "- claim (string, required): the factual statement\n"
            "- normalizedKey (string): slug for deduplication using dimension, subject, and claim\n"
            "- sourceQuote (string): verbatim text supporting this claim from the page\n"
            "- confidence (number, 0.0-1.0): how certain the claim appears\n"
            "- context (object): optional who/what/when/where/why/how keys\n"
        ),
        "subject": subject,
        "dimension": dimension,
        "pageBody": body,
    }

    completion = _llm_chat(
        [
            {"role": "system", "content": system},
            {"role": "user", "content": json.dumps(user, ensure_ascii=False)},
        ],
        args,
        timeout=120,
    )

    provider = (args.llm_provider or os.environ.get("LLM_PROVIDER") or "ollama").strip().lower()
    content = _extract_content(completion, provider)
    if content is None:
        print("LLM extraction failed; falling back to traditional parsing.", file=sys.stderr)
        return _parse_facts_traditional(body, subject, dimension, entry_tags, file_path)

    try:
        parsed = json.loads(content)
    except json.JSONDecodeError:
        # Try to extract JSON from markdown fences.
        match = re.search(r"```(?:json)?\s*\n?(.*?)```", content, re.DOTALL)
        if match:
            try:
                parsed = json.loads(match.group(1))
            except json.JSONDecodeError:
                print("LLM extraction parse failed; falling back to traditional parsing.", file=sys.stderr)
                return _parse_facts_traditional(body, subject, dimension, entry_tags, file_path)
        else:
            print("LLM extraction parse failed; falling back to traditional parsing.", file=sys.stderr)
            return _parse_facts_traditional(body, subject, dimension, entry_tags, file_path)

    if not isinstance(parsed, list) or not parsed:
        print("LLM extraction returned empty; falling back to traditional parsing.", file=sys.stderr)
        return _parse_facts_traditional(body, subject, dimension, entry_tags, file_path)

    facts = []
    for item in parsed:
        if not isinstance(item, dict):
            continue
        facts.append(normalize_fact(item, subject, dimension, entry_tags, file_path))

    return facts if facts else _parse_facts_traditional(body, subject, dimension, entry_tags, file_path)


def parse_facts(
    body: str,
    subject: str,
    dimension: str,
    entry_tags: list[str],
    file_path: Path,
    args: argparse.Namespace,
) -> list[WikiFact]:
    if args.extract_llm:
        extracted = extract_facts_llm(body, subject, dimension, entry_tags, file_path, args)
        # Also include traditionally extracted facts as supplementary input for consolidation.
        traditional = _parse_facts_traditional(body, subject, dimension, entry_tags, file_path)
        all_facts = extracted + traditional
        if args.consolidate_llm:
            return consolidate_facts_llm(all_facts, args)
        return consolidate_facts(all_facts)

    return _parse_facts_traditional(body, subject, dimension, entry_tags, file_path, args)


def _parse_facts_traditional(
    body: str,
    subject: str,
    dimension: str,
    entry_tags: list[str],
    file_path: Path,
    args: argparse.Namespace | None = None,
) -> list[WikiFact]:
    facts: list[WikiFact] = []

    # Legacy lk-facts (structured YAML). When present, it is the authoritative
    # representation — skip the human-readable bullets below to avoid dupes.
    block = extract_lk_facts_block(body)
    if block is not None:
        facts.extend([normalize_fact(raw, subject, dimension, entry_tags, file_path) for raw in block])

    # Newer/alternate bullet facts — only used when there is no lk-facts block.
    if block is None:
        facts_section = extract_section_text(body, "Facts")
        bullets = extract_bullets(facts_section)
        facts.extend([normalize_fact({"claim": bullet}, subject, dimension, entry_tags, file_path) for bullet in bullets])

    if not facts:
        facts = fallback_fact_from_body(body, subject, dimension, file_path, entry_tags)

    if args and args.consolidate_llm:
        return consolidate_facts_llm(facts, args)
    return consolidate_facts(facts)


def consolidate_facts(facts: list[WikiFact]) -> list[WikiFact]:
    """Deterministically consolidate facts.

    Rules:
    - Dedupe by normalized_key.
    - Prefer higher confidence.
    - Prefer non-empty source/source_quote/last_confirmed.
    - Merge context by key (prefer longer non-empty values).
    - Merge tags (deduped).
    """

    if not facts:
        return []

    by_key: dict[str, WikiFact] = {}
    for fact in facts:
        key = fact.normalized_key
        if key not in by_key:
            by_key[key] = fact
            continue

        existing = by_key[key]

        if fact.confidence > existing.confidence:
            existing.confidence = fact.confidence

        if fact.source_quote and not existing.source_quote:
            existing.source_quote = fact.source_quote
        if fact.source and not existing.source:
            existing.source = fact.source
        if fact.last_confirmed and not existing.last_confirmed:
            existing.last_confirmed = fact.last_confirmed

        # Merge context.
        for ctx_key, ctx_val in fact.context.items():
            if not ctx_val:
                continue
            if ctx_key not in existing.context or not existing.context[ctx_key]:
                existing.context[ctx_key] = ctx_val
            else:
                # Prefer the longer value (more specific).
                if len(ctx_val) > len(existing.context[ctx_key]):
                    existing.context[ctx_key] = ctx_val

        # Merge tags.
        if fact.tags:
            existing.tags = dedupe_strings(existing.tags + fact.tags)

        # Prefer the longer claim text if they differ.
        if fact.claim and (not existing.claim or len(fact.claim) > len(existing.claim)):
            existing.claim = fact.claim

    # Stable ordering for deterministic output.
    return sorted(by_key.values(), key=lambda f: (f.normalized_key, f.claim))


def consolidate_facts_llm(facts: list[WikiFact], args: argparse.Namespace) -> list[WikiFact]:
    """Consolidate facts using an LLM (default: local Ollama).

    Falls back to deterministic consolidation on any error.
    """

    if not facts:
        return []

    payload_facts = [
        {
            "claim": f.claim,
            "normalizedKey": f.normalized_key,
            "sourceQuote": f.source_quote,
            "source": f.source,
            "confidence": f.confidence,
            "lastConfirmed": f.last_confirmed,
            "context": f.context,
            "tags": f.tags,
        }
        for f in facts
    ]

    completion = _llm_chat(
        [
            {
                "role": "system",
                "content": (
                    "You consolidate knowledge base facts extracted from wiki pages. "
                    "Dedupe and merge facts that refer to the same underlying claim, preserving nuance and context. "
                    "Return ONLY valid JSON."
                ),
            },
            {
                "role": "user",
                "content": json.dumps(
                    {
                        "instruction": (
                            "Return a consolidated array of facts. "
                            "Dedupe by meaning (not just exact text). If two items are the same, merge them. "
                            "Preserve the most specific claim text. Merge context keys (prefer non-empty, longer values). "
                            "Merge tags with dedupe. Prefer non-empty sourceQuote/source/lastConfirmed. "
                            "Output objects with keys: claim, normalizedKey, sourceQuote, source, confidence, lastConfirmed, context, tags. "
                            "confidence must be a number between 0 and 1."
                        ),
                        "facts": payload_facts,
                    },
                    ensure_ascii=False,
                ),
            },
        ],
        args,
        timeout=60,
    )

    provider = (args.llm_provider or os.environ.get("LLM_PROVIDER") or "ollama").strip().lower()
    content = _extract_content(completion, provider)
    if content is None:
        print("LLM consolidation failed; falling back to deterministic.", file=sys.stderr)
        return consolidate_facts(facts)

    try:
        consolidated = json.loads(content)
    except json.JSONDecodeError:
        # Try to extract JSON from markdown fences.
        match = re.search(r"```(?:json)?\s*\n?(.*?)```", content, re.DOTALL)
        if match:
            try:
                consolidated = json.loads(match.group(1))
            except json.JSONDecodeError:
                print("LLM consolidation response parse failed; falling back to deterministic.", file=sys.stderr)
                return consolidate_facts(facts)
        else:
            print("LLM consolidation response parse failed; falling back to deterministic.", file=sys.stderr)
            return consolidate_facts(facts)

    if not isinstance(consolidated, list):
        return consolidate_facts(facts)

    out: list[WikiFact] = []
    for item in consolidated:
        if not isinstance(item, dict):
            continue
        out.append(
            WikiFact(
                claim=normalize_whitespace(str(item.get("claim") or "")),
                normalized_key=normalize_whitespace(str(item.get("normalizedKey") or "")),
                source_quote=normalize_whitespace(str(item.get("sourceQuote") or "")),
                source=normalize_whitespace(str(item.get("source") or "")),
                confidence=float(item.get("confidence") or 0.0),
                last_confirmed=normalize_whitespace(str(item.get("lastConfirmed") or "")),
                context=item.get("context") if isinstance(item.get("context"), dict) else {},
                tags=item.get("tags") if isinstance(item.get("tags"), list) else [],
            )
        )

    # Final safety: re-dedupe deterministically in case the LLM returned duplicates.
    return consolidate_facts(out)


def fallback_fact_from_body(body: str, subject: str, dimension: str, file_path: Path, entry_tags: list[str]) -> list[WikiFact]:
    summary = normalize_whitespace(extract_section_text(body, "Summary") or body.strip())
    if not summary:
        return []
    return [normalize_fact({"claim": summary}, subject, dimension, entry_tags, file_path)]


def extract_lk_facts_block(body: str) -> list[dict[str, Any]] | None:
    lines = body.splitlines()
    start = None
    for idx, line in enumerate(lines):
        if line.strip().lower() == "```yaml lk-facts":
            start = idx + 1
            break
    if start is None:
        return None
    end = None
    for idx in range(start, len(lines)):
        if lines[idx].strip() == "```":
            end = idx
            break
    if end is None:
        return None
    return parse_lk_fact_lines(lines[start:end])


def parse_lk_fact_lines(lines: list[str]) -> list[dict[str, Any]]:
    facts: list[dict[str, Any]] = []
    current: dict[str, Any] | None = None
    section: str | None = None
    current_scalar_key: str | None = None
    last_context_key: str | None = None

    for raw_line in lines:
        stripped = raw_line.strip()
        if not stripped:
            if current_scalar_key and current is not None and current.get(current_scalar_key):
                current[current_scalar_key] = f"{current[current_scalar_key]}\n"
            elif section == "context" and last_context_key and current is not None:
                current["context"][last_context_key] = f"{current['context'].get(last_context_key, '')}\n"
            continue

        if stripped.startswith("- claim:"):
            if current is not None:
                facts.append(current)
            current = {
                "claim": unquote_yaml_value(stripped[len("- claim:") :].strip()),
                "context": {},
                "tags": [],
            }
            section = None
            current_scalar_key = "claim"
            last_context_key = None
            continue

        if current is None:
            continue

        if stripped == "context:":
            section = "context"
            current_scalar_key = None
            last_context_key = None
            continue
        if stripped == "tags:":
            section = "tags"
            current_scalar_key = None
            last_context_key = None
            continue

        if section == "tags" and stripped.startswith("- "):
            current["tags"].append(unquote_yaml_value(stripped[2:].strip()) or "")
            continue

        if section == "context":
            if ":" in stripped:
                key, value = stripped.split(":", 1)
                key = key.strip()
                if key in FACT_CONTEXT_KEYS:
                    last_context_key = key
                    current["context"][last_context_key] = unquote_yaml_value(value.strip()) or ""
                    continue
            if last_context_key:
                existing = current["context"].get(last_context_key, "")
                current["context"][last_context_key] = (existing + "\n" + stripped).strip()
            continue

        if ":" in stripped:
            key, value = stripped.split(":", 1)
            key = key.strip()
            value = value.strip()
            if key not in FACT_TOP_LEVEL_KEYS:
                if current_scalar_key and current_scalar_key != "confidence":
                    existing = str(current.get(current_scalar_key, ""))
                    current[current_scalar_key] = (existing + "\n" + stripped).strip()
                continue
            if key == "confidence":
                try:
                    current[key] = float(value)
                except ValueError:
                    current[key] = 0.0
                current_scalar_key = None
            else:
                current[key] = unquote_yaml_value(value) or ""
                current_scalar_key = key
            last_context_key = None
            continue

        if current_scalar_key and current_scalar_key != "confidence":
            existing = str(current.get(current_scalar_key, ""))
            current[current_scalar_key] = (existing + "\n" + stripped).strip()
        elif section == "context" and last_context_key:
            existing = current["context"].get(last_context_key, "")
            current["context"][last_context_key] = (existing + "\n" + stripped).strip()

    if current is not None:
        facts.append(current)

    return facts


def unquote_yaml_value(value: str) -> str | None:
    if value in {"", "''"}:
        return None
    if len(value) >= 2 and value[0] == "'" and value[-1] == "'":
        return value[1:-1].replace("''", "'")
    if len(value) >= 2 and value[0] == '"' and value[-1] == '"':
        return value[1:-1].replace('\\"', '"')
    return value


def extract_section_text(body: str, section_name: str) -> str:
    lines = body.splitlines()
    start = None
    for idx, line in enumerate(lines):
        if line.strip().lower() == f"## {section_name.lower()}":
            start = idx + 1
            break
    if start is None:
        return ""

    collected: list[str] = []
    for idx in range(start, len(lines)):
        line = lines[idx]
        if line.startswith("## ") and line.strip().lower() != f"## {section_name.lower()}":
            break
        collected.append(line)
    return "\n".join(collected).strip()


def extract_related_lines(body: str) -> list[str]:
    text = extract_section_text(body, "Related")
    if not text:
        return []
    lines = []
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("- "):
            lines.append(stripped)
    return lines


def extract_bullets(text: str) -> list[str]:
    if not text:
        return []
    bullets: list[str] = []
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("- "):
            claim = stripped[2:].strip()
            if claim:
                bullets.append(claim)
    return bullets


def normalize_fact(raw: dict[str, Any], subject: str, dimension: str, entry_tags: list[str], file_path: Path) -> WikiFact:
    context = raw.get("context") if isinstance(raw.get("context"), dict) else {}
    if not isinstance(context, dict):
        context = {}

    claim = normalize_whitespace(str(raw.get("claim") or raw.get("summaryHint") or ""))
    normalized_key = normalize_whitespace(str(raw.get("normalizedKey") or f"{dimension}-{slugify(subject)}|{slugify(claim)}"))
    source_quote = normalize_whitespace(str(raw.get("sourceQuote") or raw.get("source_quote") or ""))
    source = normalize_whitespace(str(raw.get("source") or f"import:{file_path.as_posix()}"))
    last_confirmed = normalize_whitespace(str(raw.get("lastConfirmed") or ""))
    confidence = parse_confidence(raw.get("confidence"), source_quote, claim)
    context_out: dict[str, str] = {}

    for key in FACT_CONTEXT_KEYS:
        value = context.get(key)
        if value is None:
            value = raw.get(key)
        if value is None:
            continue
        normalized = normalize_whitespace(str(value))
        if normalized:
            context_out[key] = normalized

    fact_tags = dedupe_strings(entry_tags + normalize_string_list(raw.get("tags")))
    if is_historical_fact(claim, source_quote, context_out):
        fact_tags = dedupe_strings(fact_tags + ["historical"])
    if is_superseded_fact(claim, source_quote):
        fact_tags = dedupe_strings(fact_tags + ["superseded"])
    if is_correction_fact(claim, source_quote):
        fact_tags = dedupe_strings(fact_tags + ["correction"])

    return WikiFact(
        claim=claim,
        normalized_key=normalized_key,
        source_quote=source_quote,
        source=source,
        confidence=confidence,
        last_confirmed=last_confirmed,
        context=context_out,
        tags=fact_tags,
    )


def parse_confidence(raw_value: Any, source_quote: str, claim: str) -> float:
    try:
        if raw_value is not None:
            value = float(raw_value)
            return clamp(value)
    except (TypeError, ValueError):
        pass

    score = 0.75 if claim else 0.5
    if source_quote:
        score += 0.15
    if any(marker in claim.lower() for marker in ("previously", "no longer", "used to", "formerly", "was not", "replaced")):
        score -= 0.05
    return clamp(score)


def clamp(value: float) -> float:
    return max(0.0, min(1.0, round(value, 2)))


def is_historical_fact(claim: str, source_quote: str, context: dict[str, str]) -> bool:
    haystack = " ".join([claim, source_quote, " ".join(context.values())]).lower()
    return any(marker in haystack for marker in ("previously", "used to", "formerly", "no longer", "not anymore", "before", "historical"))


def is_superseded_fact(claim: str, source_quote: str) -> bool:
    haystack = f"{claim} {source_quote}".lower()
    return any(marker in haystack for marker in ("replaced", "superseded", "changed", "later became", "now"))


def is_correction_fact(claim: str, source_quote: str) -> bool:
    haystack = f"{claim} {source_quote}".lower()
    return any(marker in haystack for marker in ("incorrect", "was not", "wrong", "correction", "clarified"))


def detect_conflicts(facts: list[WikiFact]) -> list[str]:
    grouped: dict[str, list[WikiFact]] = defaultdict(list)
    for fact in facts:
        grouped[fact.normalized_key].append(fact)

    notes: list[str] = []
    for key, items in grouped.items():
        claims = {item.claim for item in items}
        if len(items) > 1 and len(claims) > 1:
            notes.append(f"conflict:{key}")
    return notes


def build_frontmatter(
    entry_id: str,
    dimension: str,
    subject: str,
    summary: str,
    source_frontmatter: dict[str, Any],
    aliases: list[str],
    tags: list[str],
) -> dict[str, Any]:
    frontmatter = {
        "id": entry_id,
        "type": "wiki",
        "dimension": dimension,
        "subject": subject,
        "summary": summary,
    }
    if "lastAccessed" in source_frontmatter:
        frontmatter["lastAccessed"] = source_frontmatter["lastAccessed"]
    if "accessCount" in source_frontmatter:
        frontmatter["accessCount"] = source_frontmatter["accessCount"]
    elif source_frontmatter.get("accessCount") is None:
        frontmatter["accessCount"] = 0
    if aliases:
        frontmatter["aliases"] = aliases
    if tags:
        frontmatter["tags"] = tags
    return frontmatter


def render_entry(
    frontmatter: dict[str, Any],
    subject: str,
    summary: str,
    facts: list[WikiFact],
    aliases: list[str],
    related_lines: list[str],
) -> str:
    parts = [
        render_frontmatter(frontmatter),
        "",
        f"# {subject}",
        "",
        "## Summary",
        "",
        summary or "",
        "",
        "## Facts",
        "",
    ]
    parts.extend(render_fact_bullets(facts))
    parts.extend(["", "## Also Known As", ""])
    parts.extend([f"- {alias}" for alias in aliases] if aliases else [""])
    parts.extend(["", "## Related", ""])
    parts.extend(related_lines if related_lines else [""])
    return "\n".join(parts).rstrip() + "\n"


def render_frontmatter(frontmatter: dict[str, Any]) -> str:
    lines = ["---"]
    for key in FRONTMATTER_KEYS:
        if key not in frontmatter:
            continue
        value = frontmatter[key]
        if isinstance(value, list):
            lines.append(f"{key}:")
            for item in value:
                lines.append(f"  - {yaml_scalar(item)}")
        else:
            lines.append(f"{key}: {yaml_scalar(value)}")
    for key, value in frontmatter.items():
        if key in FRONTMATTER_KEYS:
            continue
        if isinstance(value, list):
            lines.append(f"{key}:")
            for item in value:
                lines.append(f"  - {yaml_scalar(item)}")
        else:
            lines.append(f"{key}: {yaml_scalar(value)}")
    lines.append("---")
    return "\n".join(lines)


def render_fact_bullets(facts: list[WikiFact]) -> list[str]:
    lines: list[str] = []
    for fact in facts:
        lines.append(f"- {fact.claim}")
    if not lines:
        lines.append("- ")
    return lines


def render_fact_block(facts: list[WikiFact]) -> str:
    # Legacy placeholder: the new importer does not emit lk-facts blocks.
    return ""


def yaml_scalar(value: Any) -> str:
    if value is None:
        return "''"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        if isinstance(value, float) and value.is_integer():
            return str(int(value))
        return str(value)
    text = normalize_whitespace(str(value))
    if not text:
        return "''"
    if _needs_yaml_quoting(text):
        escaped = text.replace("'", "''")
        return f"'{escaped}'"
    return text


def _needs_yaml_quoting(text: str) -> bool:
    YAML_SPECIAL_START = re.compile(r'^[&*!|>\'\"%@`\\]')
    YAML_SPECIAL_PATTERN = re.compile(r'[#{}\[\]<>|!%&*?@`]')
    YAML_BOOL_NULL = {"true", "false", "yes", "no", "on", "off", "null", "~"}
    if YAML_SPECIAL_START.search(text):
        return True
    if YAML_SPECIAL_PATTERN.search(text):
        return True
    if ": " in text or text.endswith(":"):
        return True
    if text.lower() in YAML_BOOL_NULL:
        return True
    try:
        float(text)
        return True
    except ValueError:
        pass
    return False


def normalize_whitespace(value: str) -> str:
    return re.sub(r"\s+", " ", value).strip()


def dedupe_strings(items: list[str]) -> list[str]:
    seen: set[str] = set()
    result: list[str] = []
    for item in items:
        normalized = normalize_whitespace(str(item))
        if not normalized or normalized in seen:
            continue
        seen.add(normalized)
        result.append(normalized)
    return result


def slugify(value: str) -> str:
    value = normalize_whitespace(value).lower()
    value = re.sub(r"[^a-z0-9]+", "-", value)
    return value.strip("-") or "untitled"


def normalize_newlines(value: str) -> str:
    return value.replace("\r\n", "\n").strip() + "\n"


def build_index_payload(entries: list[WikiEntry]) -> dict[str, Any]:
    payload_entries = []
    fact_pointers: dict[str, list[dict[str, str]]] = {dim: [] for dim in DIMENSIONS}

    for entry in entries:
        fact_keys = [fact.normalized_key for fact in entry.facts]
        payload_entries.append(
            {
                "id": entry.frontmatter["id"],
                "dimension": entry.dimension,
                "subject": entry.subject,
                "normalizedSubject": normalize_whitespace(entry.subject).lower(),
                "summary": entry.summary,
                "aliases": entry.aliases,
                "tags": entry.tags,
                "filePath": entry.relative_source_path.as_posix(),
                "factCount": len(entry.facts),
                "maxConfidence": max((fact.confidence for fact in entry.facts), default=0.0),
                "lastConfirmed": max((fact.last_confirmed for fact in entry.facts if fact.last_confirmed), default=""),
                "sources": sorted({fact.source for fact in entry.facts if fact.source}),
                "factKeys": fact_keys,
            }
        )
        for fact in entry.facts:
            for dim in FACT_CONTEXT_KEYS:
                if fact.context.get(dim):
                    fact_pointers[dim].append({"factKey": fact.normalized_key, "entryId": entry.frontmatter["id"]})

    return {
        "version": 2,
        "builtAt": datetime.now(timezone.utc).isoformat(),
        "entries": payload_entries,
        "factPointers": fact_pointers,
    }


def build_migration_ledger(entries: list[WikiEntry]) -> dict[str, Any]:
    return {
        "builtAt": datetime.now(timezone.utc).isoformat(),
        "entries": [
            {
                "sourcePath": entry.relative_source_path.as_posix(),
                "targetPath": entry.target_path.as_posix(),
                "entryId": entry.frontmatter["id"],
                "dimension": entry.dimension,
                "factKeys": [fact.normalized_key for fact in entry.facts],
                "status": entry.status,
                "notes": entry.notes,
            }
            for entry in entries
        ],
    }


def build_report(entries: list[WikiEntry], source_root: Path, target_root: Path, dry_run: bool) -> dict[str, Any]:
    changed = [entry for entry in entries if entry.status != "unchanged"]
    conflicts = [note for entry in entries for note in entry.notes if note.startswith("conflict:")]
    return {
        "sourceRoot": source_root.as_posix(),
        "targetRoot": target_root.as_posix(),
        "dryRun": dry_run,
        "fileCount": len(entries),
        "unchangedCount": sum(1 for entry in entries if entry.status == "unchanged"),
        "normalizedCount": sum(1 for entry in entries if entry.status == "normalized"),
        "conflictCount": len(conflicts),
        "changedSamples": [
            {
                "sourcePath": entry.relative_source_path.as_posix(),
                "targetPath": entry.target_path.as_posix(),
                "status": entry.status,
                "notes": entry.notes,
                "diff": render_diff(entry.source_text, entry.rendered_text),
            }
            for entry in changed[:5]
        ],
    }


def render_diff(before: str, after: str) -> str:
    diff = difflib.unified_diff(
        before.splitlines(),
        after.splitlines(),
        fromfile="source",
        tofile="rendered",
        lineterm="",
    )
    return "\n".join(diff)


def write_preview(entries: list[WikiEntry], preview_dir: Path, source_root: Path) -> None:
    if preview_dir.exists():
        pass
    preview_dir.mkdir(parents=True, exist_ok=True)
    for entry in entries:
        preview_path = preview_dir / entry.dimension / f"{slugify(entry.subject)}.md"
        preview_path.parent.mkdir(parents=True, exist_ok=True)
        preview_path.write_text(entry.rendered_text, encoding="utf-8")


def write_entries(entries: list[WikiEntry], target_root: Path) -> None:
    target_root.mkdir(parents=True, exist_ok=True)
    for entry in entries:
        path = target_root / entry.dimension / f"{slugify(entry.subject)}.md"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(entry.rendered_text, encoding="utf-8")


def write_json(payload: dict[str, Any], path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())
