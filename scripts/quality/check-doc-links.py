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
    markdown_files = [repo_root / "README.md"] + sorted((repo_root / "docs").rglob("*.md"))

    failures: list[str] = []

    for source_file in markdown_files:
        text = source_file.read_text(encoding="utf-8")
        for match in LINK_PATTERN.finditer(text):
            raw_target = match.group(1).strip()
            if not raw_target:
                continue

            if raw_target.startswith(EXTERNAL_PREFIXES):
                continue

            if raw_target.startswith("file://"):
                relative_source = source_file.relative_to(repo_root).as_posix()
                failures.append(
                    f"{relative_source}: disallowed file:// link target '{raw_target}'"
                )
                continue

            target_without_anchor = raw_target.split("#", 1)[0].strip()
            if not target_without_anchor or target_without_anchor in IGNORED_LITERAL_TARGETS:
                continue

            resolved_target = (source_file.parent / target_without_anchor).resolve()
            if not resolved_target.exists():
                relative_source = source_file.relative_to(repo_root).as_posix()
                failures.append(
                    f"{relative_source}: missing link target '{raw_target}'"
                )

    if failures:
        print("Documentation link check failed:")
        for failure in failures:
            print(f"- {failure}")
        return 1

    print("Documentation link check passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
