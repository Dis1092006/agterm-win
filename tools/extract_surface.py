#!/usr/bin/env python3
"""
extract_surface.py — snapshot the *drivable surface* of an agterm checkout.

Parses the host-free, declarative Swift sources in `agtermCore` (no Swift compiler
needed — these are flat enums/structs) and emits a normalized `surface.json`
describing everything a Windows port must stay in API-parity with:

  - control commands (swift case -> wire string)
  - ControlArgs / ControlResult / ControlSessionNode / ControlWindowNode fields
  - BuiltinAction keymap actions + their default chords
  - {AGT_X} custom-command tokens
  - AppSettings fields
  - Snapshot / SessionSnapshot persistence fields

The output is deterministic (sorted where order is irrelevant, source-order where it
matters) so two runs on the same agterm revision diff cleanly. Feed it into a CI gate:
diff this snapshot against your committed `parity.json`; any new command/arg/action/field
is a parity task (implement) or an explicit skip.

Usage:
    python extract_surface.py --agterm <path-to-agterm-checkout> [--out surface.json]

Defaults `--agterm` to C:\\git\\github\\umputun\\agterm (the known dev checkout).
Exits non-zero if a source file is missing or a type block can't be found — a silent
partial snapshot is worse than a loud failure for a parity gate.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

CORE = Path("agtermCore/Sources/agtermCore")


class ExtractError(RuntimeError):
    pass


def read(root: Path, rel: str) -> str:
    p = root / rel
    if not p.is_file():
        raise ExtractError(f"missing source file: {p}")
    return p.read_text(encoding="utf-8")


def type_body(text: str, decl_regex: str) -> str:
    """Return the `{...}` body of the first type whose declaration matches decl_regex,
    matched by brace balancing so nested `{}` (methods, closures) don't end it early."""
    m = re.search(decl_regex, text)
    if not m:
        raise ExtractError(f"type not found: /{decl_regex}/")
    i = text.index("{", m.end() - 1 if text[m.end() - 1] == "{" else m.start())
    depth = 0
    for j in range(i, len(text)):
        c = text[j]
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return text[i + 1 : j]
    raise ExtractError(f"unbalanced braces for /{decl_regex}/")


def enum_cases(body: str) -> list[dict]:
    """Enum *declaration* cases (skips `case .x:` switch arms, which start with a dot).
    Handles comma-separated cases on one line and optional `= "raw"` values."""
    out: list[dict] = []
    for line in body.splitlines():
        s = line.strip()
        if not s.startswith("case "):
            continue
        rest = s[len("case ") :]
        if rest.lstrip().startswith("."):  # switch arm, not a declaration
            continue
        # split top-level commas (declaration cases have no closures, so a plain split is safe)
        for part in rest.split(","):
            part = part.strip()
            m = re.match(r'^([A-Za-z_]\w*)\s*(?:=\s*"([^"]*)")?', part)
            if not m:
                continue
            name = m.group(1)
            raw = m.group(2) if m.group(2) is not None else name
            out.append({"swift": name, "wire": raw})
    return out


def public_vars(body: str) -> list[dict]:
    """`public let|var name: Type` stored fields (order-preserving), default value stripped.

    Both forms matter: mutable model/args use `public var`, while the immutable `tree` projection
    nodes (ControlSessionNode/WorkspaceNode/WindowNode) use `public let`."""
    out: list[dict] = []
    for m in re.finditer(r"public (?:let|var)\s+([A-Za-z_]\w*)\s*:\s*([^\n=]+?)\s*(?:=|$)", body, re.MULTILINE):
        out.append({"name": m.group(1), "type": m.group(2).strip()})
    return out


def default_chords(text: str) -> dict[str, str | None]:
    """Map BuiltinAction raw name -> chord string like 'command+option+n', or None.

    Parses the `defaultChord` switch: `case .x: return Chord(mods: [.a, .b], key: "n")`
    and the grouped `case .a, .b, ...: return nil` arms. Requires the case->raw map."""
    return _default_chords_impl(text)


def _default_chords_impl(text: str) -> dict[str, str | None]:
    # swift case name -> raw wire name, from the enum declaration
    body = type_body(text, r"enum BuiltinAction\s*:")
    swift_to_raw = {c["swift"]: c["wire"] for c in enum_cases(body)}

    # isolate the defaultChord computed property's switch
    chord_body = type_body(text, r"var defaultChord\s*:")
    result: dict[str, str | None] = {}
    # split into `case ...: <body>` arms
    arms = re.split(r"\bcase\s+", chord_body)
    for arm in arms[1:]:
        # arm looks like ".newWindow: return Chord(mods: [.command, .option], key: \"n\")\n        case ..."
        head, _, tail = arm.partition(":")
        names = [n.strip().lstrip(".") for n in head.split(",") if n.strip()]
        cm = re.search(r'Chord\(mods:\s*\[([^\]]*)\]\s*,\s*key:\s*"([^"]*)"\)', tail)
        if cm:
            mods = [x.strip().lstrip(".") for x in cm.group(1).split(",") if x.strip()]
            chord = "+".join(mods + [cm.group(2)])
            for n in names:
                if n in swift_to_raw:
                    result[swift_to_raw[n]] = chord
        elif re.search(r"return nil", tail):
            for n in names:
                if n in swift_to_raw:
                    result[swift_to_raw[n]] = None
    return result


def tokens(text: str) -> list[str]:
    """{AGT_X} token names from CommandContext.tokens (source order)."""
    return re.findall(r'\("(AGT_[A-Z_]+)"', text)


def git_rev(root: Path) -> str | None:
    try:
        out = subprocess.run(
            ["git", "-C", str(root), "rev-parse", "HEAD"],
            capture_output=True, text=True, check=True,
        )
        return out.stdout.strip()
    except Exception:
        return None


def extract(root: Path) -> dict:
    proto = read(root, str(CORE / "ControlProtocol.swift"))
    builtin = read(root, str(CORE / "BuiltinAction.swift"))
    custom = read(root, str(CORE / "CustomCommand.swift"))
    settings = read(root, str(CORE / "AppSettings.swift"))
    snapshot = read(root, str(CORE / "Snapshot.swift"))

    commands = enum_cases(type_body(proto, r"enum Command\s*:"))
    actions = enum_cases(type_body(builtin, r"enum BuiltinAction\s*:"))
    chords = default_chords(builtin)

    surface = {
        "agtermRevision": git_rev(root),
        "counts": {
            "commands": len(commands),
            "builtinActions": len(actions),
        },
        "control": {
            "commands": commands,
            "args": public_vars(type_body(proto, r"struct ControlArgs\s*:")),
            "result": public_vars(type_body(proto, r"struct ControlResult\s*:")),
            "sessionNode": public_vars(type_body(proto, r"struct ControlSessionNode\s*:")),
            "workspaceNode": public_vars(type_body(proto, r"struct ControlWorkspaceNode\s*:")),
            "windowNode": public_vars(type_body(proto, r"struct ControlWindowNode\s*:")),
        },
        "keymap": {
            "actions": [{**a, "defaultChord": chords.get(a["wire"])} for a in actions],
            "tokens": tokens(custom),
        },
        "settings": public_vars(type_body(settings, r"struct AppSettings\s*:")),
        "persistence": {
            "snapshot": public_vars(type_body(snapshot, r"struct Snapshot\s*:")),
            "workspaceSnapshot": public_vars(type_body(snapshot, r"struct WorkspaceSnapshot\s*:")),
            "sessionSnapshot": public_vars(type_body(snapshot, r"struct SessionSnapshot\s*:")),
        },
    }
    return surface


def main() -> int:
    ap = argparse.ArgumentParser(description="Snapshot agterm's drivable surface into surface.json")
    ap.add_argument("--agterm", default=r"C:\git\github\umputun\agterm",
                    help="path to the agterm checkout (default: C:\\git\\github\\umputun\\agterm)")
    ap.add_argument("--out", default="-", help="output file, or '-' for stdout (default)")
    args = ap.parse_args()

    root = Path(args.agterm)
    if not (root / CORE).is_dir():
        print(f"error: {root / CORE} not found — is --agterm pointing at an agterm checkout?", file=sys.stderr)
        return 2
    try:
        surface = extract(root)
    except ExtractError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2

    text = json.dumps(surface, indent=2, ensure_ascii=False) + "\n"
    if args.out == "-":
        sys.stdout.write(text)
    else:
        Path(args.out).write_text(text, encoding="utf-8")
        print(f"wrote {args.out} "
              f"({surface['counts']['commands']} commands, "
              f"{surface['counts']['builtinActions']} actions)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
