# agterm-win

Windows-native companion to [umputun/agterm] with **strict control-API parity**. This repo re-implements
the design + wire contract on .NET; it is NOT a fork of the macOS Swift code.

## Parity policy (binding)

The contract matched byte-for-byte is the `agtermctl` CLI + JSON wire protocol:

- **Match:** command names (66, see `parity/surface.json`), args, results, error strings and semantics;
  keymap grammar + `BuiltinAction` names/default chords; `{AGT_X}` tokens; settings/persistence field names.
- **Adapt** (document in `docs/PLATFORM-NOTES.md`): transport (unix socket → named pipe); features with no
  Windows analogue (foreground-pid, kitty graphics) return well-formed `{"ok":false,"error":…}` — never
  a crash.
- **Skip:** rendering, visual chrome, macOS installers, `debug.appearance` (test-only, out of parity).

Current parity pin: agterm `7df70fe` (66 commands, 41 builtin actions). Machine snapshot:
`parity/surface.json`. Human spec: `docs/agterm-windows-spec.md` (авторитетное ТЗ).

## Source of truth & disambiguation order

1. `docs/agterm-windows-spec.md` — the portable spec (data model §2–3, resolver §4, protocol §5–6,
   subsystems §7, settings §8, keymap §9, events §11).
2. `parity/surface.json` — machine-checked field/command lists.
3. Swift original at `C:\git\github\umputun\agterm` (`agtermCore/Sources/agtermCore` + its tests) —
   read-only reference for semantics when the spec is ambiguous. Never edit it.
4. Any divergence forced by the platform → record in `docs/PLATFORM-NOTES.md`.

## Workflow (binding)

- **TDD, strictly:** write the failing test first, watch it fail, then write minimal code to pass
  (superpowers:test-driven-development). No production code without a failing test.
- JSON serialization must match Swift `Codable` output: camelCase property names exactly as in the Swift
  structs, `null`/absent optionals **omitted** from output (never emitted as `null`).
- Wire error strings are part of the contract — copy them verbatim from the spec/Swift source.

## Layout

```
AgTerm.slnx
src/AgTerm.Core/            host-free core library (net8.0, zero UI deps — analogue of agtermCore)
src/AgTerm.ControlServer/   named-pipe control server + headless host (HeadlessControlActions)
src/AgTerm.Cli/             agtermctl (System.CommandLine)
src/AgTerm.Terminal/        ConPTY backend (P/Invoke, no UI deps)
src/AgTerm.Xterm/           WebView2/xterm.js bridge + real surfaces + LiveControlHost
src/AgTerm.App/             WinUI 3 app shell (thin; logic lives below)
tests/*/                    xUnit suites per project; AgTerm.E2E = headless wire E2E;
                            AgTerm.Xterm.Tests hosts the LIVE (ConPTY+WebView2) integration/E2E
docs/agterm-windows-spec.md the spec (ТЗ)
docs/PLATFORM-NOTES.md      documented adaptations/divergences
parity/surface.json         extracted upstream surface (pin 7df70fe)
tools/extract_surface.py    surface extractor
```

## Build & test

```
dotnet build AgTerm.slnx
dotnet test AgTerm.slnx                          # WinUI app builds separately:
dotnet build src/AgTerm.App -p:Platform=x64
```

Phases 1–4 done: Core, control plane (pipe + agtermctl), real terminal (ConPTY + WinUI 3 +
WebView2/xterm.js, live control server, persistence across restarts), two-level sidebar +
multi-session window (ONE WebView2 per window, pane-id bridge v2, eager deck, SidebarController
over the same store seam as the socket, keymap-driven hotkeys with the documented cmd→Ctrl
mapping). Phase 5 next: palettes, live split/scratch/overlay rendering, dashboard/zoom, quick
terminal UI, custom-command chords.
