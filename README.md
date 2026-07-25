# agterm-win

A Windows-native companion to [umputun/agterm](https://github.com/umputun/agterm) — the two-level
`Workspace → Session` model and the agent **control API**, on a Windows stack (ConPTY + WinUI/Avalonia,
CLI on System.CommandLine). Rendering/chrome are re-implemented natively; the drivable surface stays in
**strict API parity** so existing agterm scripts and the bundled agent-skill work here unchanged.

> This is an independent product inspired by agterm, **not** a fork of its macOS code. agterm is
> deliberately a native macOS app; this reuses its *design + wire contract*, not its Swift/SwiftUI/libghostty.

## Parity policy (strict)

The contract we must match byte-for-byte is the **`agtermctl` CLI + the JSON wire protocol** — that is
what scripts and the agent-skill (`Bash(agtermctl *)`) actually call. Explicitly:

- **Match:** command names, args, results, error semantics; `agtermctl` subcommands/flags and both its
  human and `--json` output; keymap grammar + `BuiltinAction` names/default chords; `{AGT_X}` tokens;
  settings/persistence field names.
- **Adapt (documented in `PLATFORM-NOTES.md`):** transport (unix socket → named pipe, hidden behind
  `agtermctl --socket`); anything with no Windows analogue (foreground-pid for `tree.foreground` /
  restore-running-command; kitty-graphics image display). These return a well-formed
  `{"ok":false,"error":…}` — never a crash or garbage — so a foreign script gets a protocol-shaped answer.
- **Skip:** rendering, visual chrome, macOS installers. Recorded as intentional in the parity manifest so
  the drift gate stops flagging them.

## Staying current with upstream

Track **releases (`v*` tags) + `CHANGELOG.md`**, not `main`. The best human-readable diff of the drivable
surface between releases is upstream's `agterm/Resources/agent-skill/` (a HARD keep-in-sync doc mirror of
the control API). The machine gate is here:

### `tools/extract_surface.py`

Snapshots the drivable surface of an agterm checkout into `parity/surface.json` by parsing the host-free
declarative sources (`ControlProtocol.swift`, `BuiltinAction.swift`, `CustomCommand.swift`,
`AppSettings.swift`, `Snapshot.swift`) — no Swift compiler needed.

```sh
python tools/extract_surface.py --agterm C:\git\github\umputun\agterm --out parity/surface.json
```

`surface.json` captures, per agterm git revision:

- `control.commands` — 49 commands (swift case → wire string)
- `control.args` / `result` / `sessionNode` / `workspaceNode` / `windowNode` — field lists
- `keymap.actions` — 35 `BuiltinAction`s + default chords; `keymap.tokens` — the `{AGT_X}` set
- `settings` — `AppSettings` fields; `persistence` — `Snapshot`/`SessionSnapshot` fields

### The drift gate (CI)

1. **Surface diff** — re-extract on a new agterm pin and diff against the committed `parity/surface.json`.
   Any new command/arg/action/field is a parity task (implement) or an explicit skip. **Blocking.**
2. **Golden wire tests** — request→response fixtures (snapshot them from a live agterm via `agtermctl`),
   asserted field-for-field. Catches *semantics* the name-level diff can't see.
3. **CLI snapshot tests** — `agtermctl <cmd>` human and `--json` output vs recorded baselines.

Bump the parity version in this README when you re-pin: **current parity target — agterm
`7df70fe` (post-v0.16.1; 66 commands, 41 actions).**

> Drift history: `d32dbbf` → `7df70fe` (3 weeks, 240 commits): +17 commands (incl. the new
> `events.read` subscription subsystem — a cursor+ring poll, which retires the old "no events by
> design" note), +6 builtin actions, +21 settings fields, arrow keys became expressible in keymap
> chords (#291). `docs/agterm-windows-spec.md` sections 5–7, 9, 11 are updated to this pin.

## Layout

```
tools/extract_surface.py   surface extractor (the drift-gate input)
parity/surface.json        latest extracted upstream surface snapshot
docs/                       (planned) agterm-windows-spec.md — the portable data/protocol/keymap spec
```

See the portable spec (`agterm-windows-spec.md`) for the full data model, protocol, and keymap format
that drive the implementation.
