#!/usr/bin/env python3
"""
segoe_icons.py: extract the Segoe Fluent Icons names and code points into a TSV.

Source: hub/apps/design/iconography/segoe-fluent-icons-font.md in
https://github.com/MicrosoftDocs/windows-dev-docs (branch `docs`). Each table row looks like

    | :::image ... ::: | e700 | :::no-loc text="GlobalNavButton"::: |

Usage:
    python -I tools/segoe_icons.py --source <path-to-md> --out src/AgTerm.Core/Icons/segoe-fluent-icons.tsv
"""
from __future__ import annotations

import argparse
import re
import sys

ROW = re.compile(r'^\|.*\|\s*([0-9a-fA-F]{4,5})\s*\|\s*:::no-loc text="([^"]*)":::\s*\|\s*$')
NAME = re.compile(r'[A-Za-z0-9]+')


def parse(lines: list[str]) -> list[tuple[str, str]]:
    rows: list[tuple[str, str]] = []
    seen: set[str] = set()
    previous = -1
    for line in lines:
        m = ROW.match(line.strip())
        if not m:
            continue
        code, name = m.group(1).lower(), m.group(2)
        if not NAME.fullmatch(name):
            raise ValueError(f"invalid icon name {name!r}")
        if name in seen:
            raise ValueError(f"duplicate icon name {name!r}")
        value = int(code, 16)
        if value <= previous:
            raise ValueError(f"code point {code} of {name} is not ascending")
        seen.add(name)
        previous = value
        rows.append((name, code))
    if not rows:
        raise ValueError("no icon rows found")
    return rows


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    parser.add_argument("--source", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    with open(args.source, encoding="utf-8") as f:
        rows = parse(f.read().splitlines())
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        for name, code in rows:
            f.write(f"{name}\t{code}\n")
    print(f"{len(rows)} icons -> {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
