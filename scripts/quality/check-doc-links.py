#!/usr/bin/env python3

from __future__ import annotations

import re
import sys
from pathlib import Path


LINK_PATTERN = re.compile(r"\[[^\]]+\]\(([^)]+)\)")
EXTERNAL_PREFIXES = ("http://", "https://", "mailto:", "#")
IGNORED_LITERAL_TARGETS = {"url"}


def main() -> int:
    repo_root = Path(__file__).resolve().parents[2]
    markdown_files = list_markdown_files(repo_root)

    failures: list[str] = []

    for source_file in markdown_files:
        text = source_file.read_text(encoding="utf-8")
        for match in LINK_PATTERN.finditer(text):
            maybe_failure = validate_target(repo_root, source_file, match.group(1).strip())
            if maybe_failure is not None:
                failures.append(maybe_failure)

    if failures:
        print("Documentation link check failed:")
        for failure in failures:
            print(f"- {failure}")
        return 1

    print("Documentation link check passed.")
    return 0


def list_markdown_files(repo_root: Path) -> list[Path]:
    return [repo_root / "README.md"] + sorted((repo_root / "docs").rglob("*.md"))


def validate_target(repo_root: Path, source_file: Path, raw_target: str) -> str | None:
    if not raw_target or raw_target.startswith(EXTERNAL_PREFIXES):
        return None

    relative_source = source_file.relative_to(repo_root).as_posix()
    if raw_target.startswith("file://"):
        return f"{relative_source}: disallowed file:// link target '{raw_target}'"

    target_without_anchor = raw_target.split("#", 1)[0].strip()
    if not target_without_anchor or target_without_anchor in IGNORED_LITERAL_TARGETS:
        return None

    resolved_target = (source_file.parent / target_without_anchor).resolve()
    if resolved_target.exists():
        return None

    return f"{relative_source}: missing link target '{raw_target}'"


if __name__ == "__main__":
    sys.exit(main())
