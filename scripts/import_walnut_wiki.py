#!/usr/bin/env python3
"""Import walnut wiki into LeanKernel 5W1H format and derive where entities.

Usage:
  python3 scripts/import_walnut_wiki.py
"""

from __future__ import annotations

import argparse
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

DIMENSIONS = ("who", "what", "where", "when", "why", "how")

# Terms users are likely to expect in where, even if sparse.
ENTITY_HINTS = {
    "OTPP",
    "DevStride",
    "BriteCore",
    "Microsoft",
    "Toronto",
    "GTA",
    "VCA",
    "Databricks",
    "Snowflake",
    "BuildOps",
    "CoderPad",
    "Cognichip",
    "Grafana",
    "Andiamo",
    "Forma",
    "ProServeIT",
}

ENTITY_BLACKLIST = {
    "AI",
    "API",
    "BIBLE",
    "BIWEEKLY",
    "CEO",
    "CLEAR",
    "CRM",
    "CS",
    "ENG",
    "FIXED",
    "GET",
    "IC",
    "IMPACT",
    "IMPORTED",
    "JACK",
    "JOB",
    "LEDGER",
    "MONTHLY",
    "MSP",
    "OTHER",
    "PDF",
    "RSVP",
    "SCHEMA",
    "STAR",
    "SYNCED",
    "TJX",
    "TRACKER",
    "TURBOCOACH",
    "US",
    "VARIABLE",
    "WEEKLY",
    "WHATSAPP",
    "WIKI",
    "WORD",
    "THE",
    "WORKPLACE",
    "MEETING",
    "BLANCO",
    "BEFORE",
    "AFTER",
    "DURING",
    "LISTINGS",
    "MORTGAGE",
    "CECS",
    "TSTV",
    "DAST",
    "SDLC",
    "README",
    "AUTOPAY",
    "SIMPLEFIN",
    "SONARQUBE",
    "SUCCESSFINDER",
    "TEACHERSCOMPETENCYREPORT",
    "HIGHLIGHTSREPORT",
    "PRODUCTHUNT",
    "CUEMAR",
    "CUEMARSHAL",
    "FIREFLY",
    "GITHUB",
    "EDTECH",
}

PLACE_HINTS = {"Toronto", "GTA", "Ontario", "Canada"}
ORG_HINTS = {
    "OTPP",
    "DevStride",
    "BriteCore",
    "Microsoft",
    "VCA",
    "BuildOps",
    "ProServeIT",
    "Snowflake",
    "Databricks",
    "CoderPad",
    "Cognichip",
    "Grafana",
    "Andiamo",
    "Forma",
}

ENTITY_STOPWORDS = {
    "WHO",
    "WHAT",
    "WHERE",
    "WHEN",
    "WHY",
    "HOW",
    "HIGH",
    "MEDIUM",
    "LOW",
    "CAD",
    "USD",
    "APR",
    "MAY",
    "JAN",
    "FEB",
    "MAR",
    "JUN",
    "JUL",
    "AUG",
    "SEP",
    "OCT",
    "NOV",
    "DEC",
}


@dataclass
class ImportStats:
    imported_pages: int = 0
    skipped_pages: int = 0
    where_entities_written: int = 0


def slugify(text: str) -> str:
    text = text.lower()
    text = re.sub(r"[^a-z0-9-]+", "-", text)
    text = re.sub(r"-+", "-", text).strip("-")
    return text or "imported"


def map_dimension(rel: Path) -> str:
    parts = rel.parts
    top = parts[0].lower() if parts else ""
    if top in {"identity", "relationships"}:
        return "who"
    if top in {"financial", "career", "ventures"}:
        return "what"
    if top == "context":
        return "when"
    if top == "wisdom":
        if len(parts) > 1 and parts[1].lower() == "faith":
            return "why"
        return "how"
    if top == "sources":
        return "where"
    return "what"


def subject_from_markdown(content: str, fallback: str) -> str:
    for line in content.splitlines():
        line = line.strip()
        if line.startswith("# "):
            return line[2:].strip()[:180]
    return fallback


def extract_fact_lines(content: str, fallback_source: str) -> list[str]:
    facts: list[str] = []
    for line in content.splitlines():
        stripped = line.strip()
        if stripped.startswith("- ") and not stripped.startswith("- ["):
            cleaned = re.sub(r"\s*<!--\{[^}]*\}-->\s*$", "", stripped[2:]).strip()
            if cleaned:
                facts.append(cleaned)

    if not facts:
        for line in content.splitlines():
            stripped = line.strip()
            if stripped.startswith("> "):
                facts.append(stripped[2:].strip())
                break

    if not facts:
        facts = [f"Imported from walnut workspace path: {fallback_source}"]

    # Preserve order, remove duplicates.
    deduped: list[str] = []
    seen: set[str] = set()
    for fact in facts:
        if fact not in seen:
            deduped.append(fact)
            seen.add(fact)
    return deduped[:20]


def entity_candidates_from_text(text: str) -> list[str]:
    candidates: list[str] = []

    # Long name + acronym forms (e.g., Ontario Teachers' Pension Plan (OTPP)).
    for match in re.finditer(r"([A-Z][A-Za-z'&.\- ]{4,80})\s*\(([A-Z]{2,10})\)", text):
        long_name = re.sub(r"\s+", " ", match.group(1)).strip(" -")
        acronym = match.group(2).strip()
        if long_name:
            candidates.append(long_name)
        candidates.append(acronym)

    # Acronyms and mixed-case org/product names.
    candidates.extend(re.findall(r"\b[A-Z]{2,10}\b", text))
    candidates.extend(re.findall(r"\b[A-Z][a-z]+(?:[A-Z][a-z]+)+\b", text))

    # Common geo and org names that may be title-case only.
    candidates.extend(re.findall(r"\b(Toronto|GTA|Canada|Ontario|Microsoft|Snowflake|Databricks)\b", text))

    cleaned: list[str] = []
    for raw in candidates:
        token = raw.strip().strip(".,:;()[]{}")
        if not token:
            continue
        if token.upper() in ENTITY_STOPWORDS:
            continue
        if token.upper() in ENTITY_BLACKLIST:
            continue
        if token.isupper() and len(token) < 3:
            continue
        cleaned.append(token)

    return cleaned


def normalize_entity(token: str) -> str:
    # Canonicalize a few expected variants.
    mappings = {
        "Britecore": "BriteCore",
        "PROSERVEIT": "ProServeIT",
        "Forma.ai": "Forma",
    }
    if token in mappings:
        return mappings[token]
    if token.upper() in mappings:
        return mappings[token.upper()]
    return token


def looks_like_org_or_place_line(line: str) -> bool:
    return bool(
        re.search(
            r"\b(geographic|location|office|remote|onsite|workplace|employer|company|organization|based|in\s+toronto|in\s+gta|at\s+[A-Z]|joined\s+[A-Z]|partner|founded|building)\b",
            line,
            flags=re.IGNORECASE,
        )
    )


def is_high_signal_entity(entity: str, count: int) -> bool:
    if entity.upper() in ENTITY_BLACKLIST:
        return False
    if entity in ORG_HINTS:
        return count > 0
    if entity in ENTITY_HINTS:
        return count > 0
    if entity in PLACE_HINTS:
        return count > 0
    if entity.isupper() and len(entity) >= 3:
        return count >= 2
    if re.match(r"^[A-Z][a-z]+(?:[A-Z][a-z]+)+$", entity):
        return count >= 1
    if re.match(r"^[A-Z][a-z]+$", entity):
        return False
    if re.match(r"^[A-Z][A-Za-z0-9.+\-]{2,}$", entity):
        return count >= 2 and any(ch.isdigit() or ch in ".+-" for ch in entity)
    return False


def build_where_entities(target_wiki_root: Path, extra_scan_files: list[Path] | None = None) -> dict[str, list[str]]:
    evidence: dict[str, list[str]] = defaultdict(list)
    counts: Counter[str] = Counter()

    for md in sorted(target_wiki_root.rglob("*.md")):
        if md.parent.name not in DIMENSIONS:
            continue
        if md.parent.name == "where":
            # Avoid self-referential feedback from previously generated where files.
            continue
        text = md.read_text(encoding="utf-8", errors="ignore")

        # Prefer WHERE sections when present; fallback to whole text.
        where_match = re.search(r"^##\s+WHERE\s*$([\s\S]*?)(^##\s+|\Z)", text, flags=re.MULTILINE)
        source_text = where_match.group(1) if where_match else text

        for line in source_text.splitlines():
            if not looks_like_org_or_place_line(line):
                continue
            for token in entity_candidates_from_text(line):
                canon = normalize_entity(token)
                counts[canon] += 1
                if len(evidence[canon]) < 8:
                    snippet = line.strip()[:220]
                    if snippet:
                        evidence[canon].append(f"Seen in {md.parent.name}/{md.name}: {snippet}")

    for extra in extra_scan_files or []:
        if not extra.exists():
            continue
        text = extra.read_text(encoding="utf-8", errors="ignore")
        for line in text.splitlines():
            if not looks_like_org_or_place_line(line):
                continue
            for token in entity_candidates_from_text(line):
                canon = normalize_entity(token)
                counts[canon] += 1
                if len(evidence[canon]) < 8:
                    snippet = line.strip()[:220]
                    if snippet:
                        evidence[canon].append(f"Seen in extra/{extra.name}: {snippet}")

    # Keep entities that are frequent or explicitly hinted and present.
    selected: dict[str, list[str]] = {}
    for entity, count in counts.items():
        if is_high_signal_entity(entity, count):
            selected[entity] = evidence.get(entity, [])

    return selected


def write_where_entity_files(where_dir: Path, entities: dict[str, list[str]]) -> int:
    now = datetime.now(timezone.utc).isoformat()
    today = datetime.now(timezone.utc).date().isoformat()
    written = 0

    for existing in where_dir.glob("entity-*.md"):
        existing.unlink(missing_ok=True)

    for entity in sorted(entities):
        slug = slugify(entity)
        path = where_dir / f"entity-{slug}.md"
        lines = [
            "---",
            f"id: where-entity-{slug}",
            "dimension: where",
            f"subject: {entity}",
            f"lastAccessed: {now}",
            "accessCount: 0",
            "---",
            "",
            f"# {entity}",
            "",
        ]

        facts = entities[entity] or [f"Entity detected in imported wiki corpus: {entity}"]
        for fact in facts[:8]:
            lines.append(
                f"- {fact} <!--{{confidence: 0.65, source: walnut:entity-scan, confirmed: {today}}}-->"
            )

        path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        written += 1

    return written


def import_pages(source_wiki_root: Path, target_wiki_root: Path, extra_scan_files: list[Path] | None = None) -> ImportStats:
    stats = ImportStats()
    now = datetime.now(timezone.utc).isoformat()
    today = datetime.now(timezone.utc).date().isoformat()

    for dim in DIMENSIONS:
        (target_wiki_root / dim).mkdir(parents=True, exist_ok=True)

    for md in sorted(source_wiki_root.rglob("*.md")):
        rel = md.relative_to(source_wiki_root)

        # Keep sources/index.md; skip top index/log/schema files.
        if rel.parts == ("index.md",) or rel.parts == ("log.md",):
            stats.skipped_pages += 1
            continue
        if md.name.startswith("WIKI-SCHEMA"):
            stats.skipped_pages += 1
            continue

        content = md.read_text(encoding="utf-8", errors="ignore").strip()
        if not content:
            stats.skipped_pages += 1
            continue

        dim = map_dimension(rel)
        slug = slugify("-".join(rel.with_suffix("").parts))
        subject = subject_from_markdown(content, rel.stem.replace("-", " "))
        facts = extract_fact_lines(content, rel.as_posix())

        out_path = target_wiki_root / dim / f"{slug}.md"
        lines = [
            "---",
            f"id: {dim}-{slug}",
            f"dimension: {dim}",
            f"subject: {subject}",
            f"lastAccessed: {now}",
            "accessCount: 0",
            "---",
            "",
            f"# {subject}",
            "",
        ]

        for fact in facts:
            lines.append(
                f"- {fact} <!--{{confidence: 0.70, source: walnut:{rel.as_posix()}, confirmed: {today}}}-->"
            )

        lines.extend([
            "",
            "## Source",
            f"- Imported from walnut workspace path: {rel.as_posix()}",
            "",
            "## Imported Content",
            content,
            "",
        ])

        out_path.write_text("\n".join(lines), encoding="utf-8")
        stats.imported_pages += 1

    entities = build_where_entities(target_wiki_root, extra_scan_files=extra_scan_files)
    stats.where_entities_written = write_where_entity_files(target_wiki_root / "where", entities)
    return stats


def main() -> None:
    parser = argparse.ArgumentParser(description="Import walnut wiki into LeanKernel wiki format")
    parser.add_argument(
        "--source-wiki-root",
        default="data/import/walnut-workspaces/main/wiki",
        help="Path to staged walnut wiki root",
    )
    parser.add_argument(
        "--target-wiki-root",
        default="data/wiki",
        help="Path to LeanKernel wiki root",
    )
    parser.add_argument(
        "--extra-scan-glob",
        action="append",
        default=["data/import/walnut-workspaces/main/tmp_*.txt"],
        help="Additional glob patterns to scan for where entities",
    )
    args = parser.parse_args()

    source = Path(args.source_wiki_root)
    target = Path(args.target_wiki_root)

    if not source.exists():
        raise SystemExit(f"Source wiki root not found: {source}")

    extra_files: list[Path] = []
    for pattern in args.extra_scan_glob:
        extra_files.extend(Path(".").glob(pattern))

    stats = import_pages(source, target, extra_scan_files=extra_files)
    print(f"IMPORTED_PAGES={stats.imported_pages}")
    print(f"SKIPPED_PAGES={stats.skipped_pages}")
    print(f"WHERE_ENTITIES_WRITTEN={stats.where_entities_written}")


if __name__ == "__main__":
    main()
