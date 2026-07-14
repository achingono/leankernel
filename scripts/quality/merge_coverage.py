#!/usr/bin/env python3
"""Merges multiple OpenCover XML coverage reports by taking the max visit count
for each sequence point across all reports. The merged result is written to
coverage-results/sonar/coverage.opencover.xml so Sonar sees combined coverage
from both unit and integration test runs."""

import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def merge_reports(results_dir: Path) -> None:
    reports = sorted(results_dir.glob("**/coverage.opencover.xml"))
    if not reports:
        print("No coverage reports found.", file=sys.stderr)
        sys.exit(1)

    if len(reports) == 1:
        dest = results_dir / "coverage.opencover.xml"
        if reports[0] != dest:
            import shutil
            shutil.copyfile(reports[0], dest)
        print(f"Single report: {reports[0]}")
        return

    # Collect max visit counts keyed by (fileid, start_line)
    visit_counts: dict = {}
    base_tree = None

    for report in reports:
        tree = ET.parse(report)
        root = tree.getroot()
        if base_tree is None:
            base_tree = tree
        for sp in root.findall(".//SequencePoint"):
            file_ref = sp.attrib.get("fileid", "")
            start_line = sp.attrib.get("sl", "")
            key = (file_ref, start_line)
            vc = int(sp.attrib.get("vc", "0"))
            old = visit_counts.get(key, 0)
            visit_counts[key] = max(old, vc)

    if base_tree is None:
        sys.exit("No reports parsed")

    base_root = base_tree.getroot()
    for sp in base_root.findall(".//SequencePoint"):
        file_ref = sp.attrib.get("fileid", "")
        start_line = sp.attrib.get("sl", "")
        key = (file_ref, start_line)
        sp.set("vc", str(visit_counts.get(key, int(sp.attrib.get("vc", "0")))))

    total = len(base_root.findall(".//SequencePoint"))
    covered = sum(
        1 for sp in base_root.findall(".//SequencePoint")
        if int(sp.attrib.get("vc", "0")) > 0
    )
    pct = covered / total * 100 if total else 0
    print(f"Merged {len(reports)} reports: {covered}/{total} ({pct:.1f}%)")

    dest = results_dir / "coverage.opencover.xml"
    base_tree.write(dest, xml_declaration=True, encoding="utf-8")
    print(f"Written merged report to {dest}")


if __name__ == "__main__":
    merge_reports(Path("coverage-results/sonar"))
