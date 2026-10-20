# agterm-win

A Windows-native companion to [umputun/agterm](https://github.com/umputun/agterm) — the two-level
`Workspace → Session` model and the agent **control API**, on a Windows stack (ConPTY + WinUI/Avalonia,
CLI on System.CommandLine). Rendering/chrome are re-implemented natively; the drivable surface stays in
**strict API parity** so existing agterm scripts work here unchanged. The agent skill is this repository's own copy of upstream's skill, adapted to Windows (see [Agent skill](#agent-skill)).

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

Build output follows the SDK's [artifacts output layout](https://learn.microsoft.com/dotnet/core/sdk/artifacts-output)
(`UseArtifactsOutput` in `Directory.Build.props`), so everything lands under `artifacts\` instead of a
`bin\`/`obj\` pair per project: `artifacts\bin\<Project>\debug\`, `artifacts\obj\<Project>\`, and
publish output under `artifacts\publish\`.

**Publish (for daily use).** `publish.proj` puts the two exes you actually run into one directory,
single-file, with nothing else beside them:

```powershell
dotnet build publish.proj                                  # x64 → artifacts\publish\agterm-win\release_x64\
dotnet build publish.proj -p:RunnablePlatform=ARM64        # same for ARM64
dotnet build publish.proj -p:RunnableOutDir=C:\tools\agterm-win # somewhere on PATH
```

`agterm-win.exe` (the `AgTerm.App` project) is ~162 MB: it is self-contained, so the Windows App SDK and the .NET runtime are
inside it and no install is required. `agtermctl.exe` is self-contained as well (~75 MB): a
framework-dependent exe placed next to the app's runtime picks up its `hostfxr.dll`, takes the directory for
a self-contained install and fails with "You must install or update .NET" even when .NET is installed.

**Run (manual).** After `dotnet build`, the app exe lands at
`artifacts\bin\AgTerm.App\debug\agterm-win.exe` and the CLI at
`artifacts\bin\AgTerm.Cli\debug\agtermctl.exe`.
A bare launch uses the real state (`~\.agterm-win` — the home directory, not `%LOCALAPPDATA%`;
`docs/PLATFORM-NOTES.md` records why). For experiments, run an **isolated
instance** — a separate state dir also derives a separate control pipe, so it never touches your
real workspaces:

```powershell
$env:AGTERM_STATE_DIR = "$env:TEMP\agterm-dev"
& ".\artifacts\bin\AgTerm.App\debug\agterm-win.exe"
```

`agtermctl` finds the app only when both derive the same pipe: run it from a shell with the same
`AGTERM_STATE_DIR` (or pass `--socket` explicitly). One app instance per state dir — the pipe is
shared.

**Run (Rider).** Open `AgTerm.slnx` and pick the **`Debug | x64`** solution configuration (the same
AnyCPU trap). Rider detects WinUI 3 as **UWP** and its auto-created UWP run configuration has no
environment-variables support — don't use it. Instead add a **“.NET Executable”** configuration:
exe path = `artifacts\bin\AgTerm.App\debug\agterm-win.exe`, Before launch =
Build Project `AgTerm.App` (with `Debug | x64` active). Keep two copies: `App (dev)` with
`AGTERM_STATE_DIR=<explicit temp path>\agterm-dev` (Rider does not expand `%TEMP%`) and `App (real)`
with none. C# debugging is ordinary .NET; the xterm.js side lives in WebView2 — inspect it with
WebView2 DevTools, not the C# debugger.

## Agent skill

The agent skill in `plugins/agterm/skills/agterm` teaches a coding agent to drive agterm-win with `agtermctl`. It is upstream's skill adapted to Windows; `docs/PLATFORM-NOTES.md`, "Agent skill", lists what changed.

Install it in one of two ways:

- From the app: Help ▸ Install Agent Skill… in the title bar. It copies the skill to `~\.claude\skills\agterm` and `~\.codex\skills\agterm`. Copilot CLI does not read these directories, so Copilot CLI needs the plugin.
- As a plugin from the marketplace in this repository:
  - Claude Code: `/plugin marketplace add Dis1092006/agterm-win`, then `/plugin install agterm@agterm-win`.
  - Codex: `codex plugin marketplace add Dis1092006/agterm-win`, then `codex plugin add agterm@agterm-win`.
  - Copilot CLI: `copilot plugin marketplace add Dis1092006/agterm-win`, then `copilot plugin install agterm@agterm-win`.

Use one of them, not both. With both, the agent loads two copies of the skill.

The repository is private. The plugin commands work only for a user who has access to it: git must be able to clone `https://github.com/Dis1092006/agterm-win` with that user's credentials.

## Hotkeys on Windows

Keymap modifiers map as `cmd`→**Ctrl**, `ctrl`→Ctrl, `opt`→Alt, `shift`→Shift (spec §9 leaves the
mapping to the platform; rationale and the candidate-priority rule are in
`docs/PLATFORM-NOTES.md`).

That fold would put 13 shortcuts on bare Ctrl, which in a Windows terminal belongs to the shell:
Ctrl+D ends input, Ctrl+Z suspends, Ctrl+W deletes a word, Ctrl+P walks history. Every shortcut that
would land there ships on Alt with the same key instead. Shortcuts that already carry Shift or Alt
keep the upstream chord, because a shell never reads those. A `map` line in `keymap.conf` overrides
either default.

| Action                 | macOS default | Windows          | Action                       | macOS default | Windows              |
| ---------------------- | ------------- | ---------------- | ---------------------------- | ------------- | -------------------- |
| `new_window`           | ⌘⌥N           | Ctrl+Alt+N       | `toggle_fullscreen`          | ⌃⌘F           | Alt+Enter            |
| `new_workspace`        | ⌘⇧N           | Ctrl+Shift+N     | `toggle_flag`                | ⌘⇧F           | Ctrl+Shift+F         |
| `new_session`          | ⌘N            | Alt+N            | `focus_left_pane`            | ⌘⌥←           | Ctrl+Alt+←           |
| `open_directory`       | ⌘O            | Alt+O            | `focus_right_pane`           | ⌘⌥→           | Ctrl+Alt+→           |
| `close_session`        | ⌘W            | Alt+W            | `previous_session`           | ⌘⌥↑           | Ctrl+Alt+↑           |
| `reopen_recent`        | ⌘⇧T           | Ctrl+Shift+T     | `next_session`               | ⌘⌥↓           | Ctrl+Alt+↓           |
| `undo_close`           | ⌘Z            | Alt+Z            | `previous_attention_session` | ⌃⌥↑           | _(shadowed — remap)_ |
| `increase_font_size`   | ⌘+            | Alt+Plus         | `next_attention_session`     | ⌃⌥↓           | _(shadowed — remap)_ |
| `decrease_font_size`   | ⌘-            | Alt+Minus        | `quick_terminal`             | ⌃`            | Alt+\`               |
| `reset_font_size`      | ⌘0            | Alt+0            | `session_palette`            | ⌃P            | Alt+P                |
| `toggle_split`         | ⌘D            | Alt+D            | `command_palette`            | ⌃⇧P           | Ctrl+Shift+P         |
| `toggle_scratch`       | ⌘J            | Alt+J            | `custom_command_palette`     | ⌃⇧O           | Ctrl+Shift+O         |
| `toggle_terminal_zoom` | ⌘⇧↩           | Ctrl+Shift+Enter | `show_attention`             | ⌃⇧I           | Ctrl+Shift+I         |
| `toggle_search`        | ⌘F            | Alt+F            | `dashboard`                  | ⌘⇧D           | Ctrl+Shift+D         |
| `toggle_sidebar`       | ⌃⌘S           | Alt+S            |                              |               |                      |

Keyless actions (`rename_*`, `delete_*`, `duplicate_session`, `clear_status`, `select_theme`,
`toggle_flagged_view`, `focus_workspace`, `first_session`, `last_session`) bind via `map` in
`keymap.conf`. _Shadowed_: `cmd` and `ctrl` fold onto the same physical key, and the cmd
interpretation wins — `⌃⌥↑↓` lose to `⌘⌥↑↓`; `map` them to free chords
(e.g. `map ctrl+shift+down next_attention_session`). Both carry Alt, so the shell is unaffected.
F2 additionally renames the selected sidebar row (a UI gesture, not a keymap bind). Palettes/split/scratch/zoom/fullscreen/quick/
dashboard hotkeys are implemented in the live app.
