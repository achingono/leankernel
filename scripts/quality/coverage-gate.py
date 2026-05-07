#!/usr/bin/env python3
"""Aggregate Cobertura line coverage and enforce a minimum threshold."""

from __future__ import annotations

import argparse
import glob
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--threshold",
        type=float,
        default=80.0,
        help="Minimum required line coverage percentage. Default: 80.",
    )
    parser.add_argument(
        "reports",
        nargs="+",
        help="Cobertura XML files or glob patterns.",
    )
    return parser.parse_args()


def expand_reports(patterns: list[str]) -> list[Path]:
    reports: list[Path] = []
    for pattern in patterns:
        matches = [Path(path) for path in glob.glob(pattern, recursive=True)]
        if matches:
            reports.extend(matches)
            continue

        path = Path(pattern)
        if path.exists():
            reports.append(path)

    return sorted(set(reports))


def normalize_filename(filename: str) -> str:
    return filename.replace("\\", "/")


def aggregate_coverage(reports: list[Path]) -> tuple[int, int]:
    lines: dict[tuple[str, int], int] = {}

    for report in reports:
        root = ET.parse(report).getroot()
        for class_node in root.findall(".//class"):
            filename = class_node.attrib.get("filename")
            if not filename:
                continue

            normalized = normalize_filename(filename)
            if "/LeanKernel.Tests." in normalized or "/obj/" in normalized or "/bin/" in normalized:
                continue

            for line in class_node.findall("./lines/line"):
                number_text = line.attrib.get("number")
                hits_text = line.attrib.get("hits", "0")
                if number_text is None:
                    continue

                key = (normalized, int(number_text))
                hits = int(hits_text)
                lines[key] = max(lines.get(key, 0), hits)

    covered = sum(1 for hits in lines.values() if hits > 0)
    return covered, len(lines)


def main() -> int:
    args = parse_args()
    reports = expand_reports(args.reports)
    if not reports:
        print("No Cobertura coverage reports found.", file=sys.stderr)
        return 2

    covered, total = aggregate_coverage(reports)
    if total == 0:
        print("Coverage reports contained no production lines.", file=sys.stderr)
        return 2

    percent = covered / total * 100
    print(f"Line coverage: {percent:.2f}% ({covered}/{total})")

    if percent + 1e-9 < args.threshold:
        print(
            f"Coverage gate failed: {percent:.2f}% is below {args.threshold:.2f}%.",
            file=sys.stderr,
        )
        return 1

    print(f"Coverage gate passed: threshold {args.threshold:.2f}%.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
