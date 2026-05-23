#!/usr/bin/env python3
from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path


def export_with_sips(svg_path: Path, out_path: Path) -> None:
    cmd = [
        "sips",
        "-s",
        "format",
        "png",
        str(svg_path),
        "--out",
        str(out_path),
    ]
    subprocess.run(cmd, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)


def main() -> int:
    if shutil.which("sips") is None:
        print("sips is required on macOS for full-canvas SVG -> PNG export.")
        return 1

    root = Path(__file__).resolve().parent
    png_dir = root / "png"
    png_dir.mkdir(parents=True, exist_ok=True)

    svgs = sorted(p for p in root.glob("*.svg"))
    if not svgs:
        print("No SVG files found.")
        return 1

    for svg in svgs:
        out = png_dir / f"{svg.stem}.png"
        export_with_sips(svg, out)
        print(f"exported: {svg.name} -> {out.name}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
