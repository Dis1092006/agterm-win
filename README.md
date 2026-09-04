# agterm-win

A Windows-native companion to [umputun/agterm](https://github.com/umputun/agterm) — the two-level
`Workspace → Session` model and the agent **control API**, on a Windows stack (ConPTY + WinUI/Avalonia,
CLI on System.CommandLine). Rendering/chrome are re-implemented natively; the drivable surface stays in
**strict API parity** so existing agterm scripts and the bundled agent-skill work here unchanged.

**Status:** Phases 1-5 are complete. The Core model, control plane, live ConPTY/WebView2 renderer,
Windows shell, Phase 5 workflows, and x64/ARM64 release gates are implemented and validated.

> This is an independent product inspired by agterm, **not** a fork of its macOS code. agterm is
> deliberately a native macOS app; this reuses its _design + wire contract_, not its Swift/SwiftUI/libghostty.

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
   asserted field-for-field. Catches _semantics_ the name-level diff can't see.
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

## Running

`AgTerm.App` is a WinUI 3 app, **unpackaged + self-contained** (`WindowsPackageType=None`,
`WindowsAppSDKSelfContained=true`): a plain exe, no MSIX and no Windows App SDK runtime install
(WebView2 Evergreen ships with Windows 11).

**Build.** The app declares `Platforms=x64;ARM64` (no AnyCPU), which is a trap for direct builds:

```powershell
dotnet build AgTerm.slnx                              # solution build — works on any platform
                                                      # (the .slnx maps AgTerm.App to x64)
dotnet build src/AgTerm.App -p:Platform=x64           # direct project build — the -p:Platform=x64
                                                      # is REQUIRED, or WindowsAppSDKSelfContained
                                                      # fails with "requires a supported Windows
                                                      # architecture" (AnyCPU default)
```

**Run (manual).** The exe lands at
`src\AgTerm.App\bin\x64\Debug\net8.0-windows10.0.19041.0\AgTerm.App.exe`.
A bare launch uses the real state (`%LOCALAPPDATA%\agterm`). For experiments, run an **isolated
instance** — a separate state dir also derives a separate control pipe, so it never touches your
real workspaces:

```powershell
$env:AGTERM_STATE_DIR = "$env:TEMP\agterm-dev"
& ".\src\AgTerm.App\bin\x64\Debug\net8.0-windows10.0.19041.0\AgTerm.App.exe"
```

`agtermctl` finds the app only when both derive the same pipe: run it from a shell with the same
`AGTERM_STATE_DIR` (or pass `--socket` explicitly). One app instance per state dir — the pipe is
shared. The CLI exe: `src\AgTerm.Cli\bin\Debug\net8.0\agtermctl.exe`.

**Run (Rider).** Open `AgTerm.slnx` and pick the **`Debug | x64`** solution configuration (the same
AnyCPU trap). Rider detects WinUI 3 as **UWP** and its auto-created UWP run configuration has no
environment-variables support — don't use it. Instead add a **“.NET Executable”** configuration:
exe path = `src\AgTerm.App\bin\x64\Debug\net8.0-windows10.0.19041.0\AgTerm.App.exe`, Before launch =
Build Project `AgTerm.App` (with `Debug | x64` active). Keep two copies: `App (dev)` with
`AGTERM_STATE_DIR=<explicit temp path>\agterm-dev` (Rider does not expand `%TEMP%`) and `App (real)`
with none. C# debugging is ordinary .NET; the xterm.js side lives in WebView2 — inspect it with
WebView2 DevTools, not the C# debugger.

## Hotkeys on Windows

Keymap modifiers map as `cmd`→**Ctrl**, `ctrl`→Ctrl, `opt`→Alt, `shift`→Shift (spec §9 leaves the
mapping to the platform; rationale and the candidate-priority rule are in
`docs/PLATFORM-NOTES.md`). Default chords, pinned to upstream and translated:

| Action                   | macOS default | Windows      | Action                       | macOS default | Windows              |
| ------------------------ | ------------- | ------------ | ---------------------------- | ------------- | -------------------- |
| `new_window`             | ⌘⌥N           | Ctrl+Alt+N   | `toggle_split`               | ⌘D            | Ctrl+D               |
| `new_workspace`          | ⌘⇧N           | Ctrl+Shift+N | `toggle_scratch`             | ⌘J            | Ctrl+J               |
| `new_session`            | ⌘N            | Ctrl+N       | `toggle_terminal_zoom`       | ⌘⇧↩           | Ctrl+Shift+Enter     |
| `open_directory`         | ⌘O            | Ctrl+O       | `toggle_search`              | ⌘F            | Ctrl+F               |
| `close_session`          | ⌘W            | Ctrl+W       | `toggle_sidebar`             | ⌃⌘S           | Ctrl+S               |
| `reopen_recent`          | ⌘⇧T           | Ctrl+Shift+T | `toggle_fullscreen`          | ⌃⌘F           | _(shadowed — remap)_ |
| `undo_close`             | ⌘Z            | Ctrl+Z       | `toggle_flag`                | ⌘⇧F           | Ctrl+Shift+F         |
| `increase_font_size`     | ⌘+            | Ctrl+Plus    | `focus_left_pane`            | ⌘⌥←           | Ctrl+Alt+←           |
| `decrease_font_size`     | ⌘-            | Ctrl+Minus   | `focus_right_pane`           | ⌘⌥→           | Ctrl+Alt+→           |
| `reset_font_size`        | ⌘0            | Ctrl+0       | `previous_session`           | ⌘⌥↑           | Ctrl+Alt+↑           |
| `quick_terminal`         | ⌃\`           | Ctrl+\`      | `next_session`               | ⌘⌥↓           | Ctrl+Alt+↓           |
| `session_palette`        | ⌃P            | Ctrl+P       | `previous_attention_session` | ⌃⌥↑           | _(shadowed — remap)_ |
| `command_palette`        | ⌃⇧P           | Ctrl+Shift+P | `next_attention_session`     | ⌃⌥↓           | _(shadowed — remap)_ |
| `custom_command_palette` | ⌃⇧O           | Ctrl+Shift+O | `dashboard`                  | ⌘⇧D           | Ctrl+Shift+D         |
| `show_attention`         | ⌃⇧I           | Ctrl+Shift+I |                              |               |                      |

Keyless actions (`rename_*`, `delete_*`, `duplicate_session`, `clear_status`, `select_theme`,
`toggle_flagged_view`, `focus_workspace`, `first_session`, `last_session`) bind via `map` in
`keymap.conf`. _Shadowed_: `cmd` and `ctrl` fold onto the same physical key, and the cmd
interpretation wins — `⌃⌘F` loses to `⌘F`, `⌃⌥↑↓` lose to `⌘⌥↑↓`; `map` them to free chords
(e.g. `map ctrl+shift+down next_attention_session`). F2 additionally renames the selected
sidebar row (a UI gesture, not a keymap bind). Palettes/split/scratch/zoom/fullscreen/quick/
dashboard hotkeys are implemented in the live app.
