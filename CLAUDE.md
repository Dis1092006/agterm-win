# agterm-win

Windows port of [umputun/agterm](https://github.com/umputun/agterm). It re-implements agterm's design and control API on .NET; it is not a fork of the Swift code.

## Parity policy (binding)

The contract matched byte for byte is the `agtermctl` CLI and the JSON wire protocol:

- **Match:** command names, args, results, error strings and semantics; keymap grammar, `BuiltinAction` names and default chords; `{AGT_X}` tokens; settings and persistence field names.
- **Adapt** (record in `docs/PLATFORM-NOTES.md`): the transport (unix socket → named pipe); features with no Windows analogue (foreground pid, kitty graphics) return a well-formed `{"ok":false,"error":…}`, never a crash.
- **Skip:** rendering, visual chrome, macOS installers, `debug.appearance`.

The parity pin is upstream `7df70fe`, and `parity/surface.json` is its snapshot. The pin does not move: upstream features after it are taken one at a time.

- `docs/upstream/delta-7df70fe-to-v0.36.0.md` lists what upstream added after the pin; `docs/upstream/surface-v0.36.0.json` is the upstream snapshot at `ed831ab`.
- A command, arg or field taken from upstream goes into `parity/adopted.json` with its upstream sha.
- A command or field upstream does not have goes into `parity/windows-only.json` and the PLATFORM-NOTES section "Windows-only extensions".
- A change in how upstream answers an already implemented command goes into the PLATFORM-NOTES section "Upstream behavior adopted beyond the pin".
- Parity tests check that the port equals `surface.json` + `adopted.json` + `windows-only.json`.

## Sources of truth, in order

1. `docs/agterm-windows-spec.md`: the portable spec at the pin (data model §2–3, resolver §4, protocol §5–6, subsystems §7, settings §8, keymap §9, events §11).
2. `parity/*.json`: the machine-checked lists.
3. The upstream Swift source (`agtermCore/Sources/agtermCore` and its tests) in a local clone of umputun/agterm: the reference when the spec is ambiguous, and the only one for features after the pin. Read a given revision with `git show <rev>:<path>`. Never edit the clone.
4. A divergence forced by the platform goes into `docs/PLATFORM-NOTES.md`.

## Workflow (binding)

- **TDD, strictly:** write the failing test first, watch it fail, then write minimal code to pass (superpowers:test-driven-development). No production code without a failing test.
- JSON serialization must match Swift `Codable` output: camelCase property names exactly as in the Swift structs, `null`/absent optionals **omitted** from output (never emitted as `null`).
- Wire error strings are part of the contract: copy them verbatim from the spec or the Swift source.
- The agent skill (`plugins/agterm/skills/agterm`) describes the control surface. A change to a wire command, a CLI flag or an `AGTERM_*` variable updates the skill in the same PR. The tests in `tests/AgTerm.Cli.Tests/SkillTextTests.cs` enforce it.
- Plans, specs and other docs are written in English. Plans and design specs are committed to `docs/plans/`, not `docs/superpowers/`. When the work is merged, the plan moves to `docs/plans/completed/`.
- Every PR raises the app version, `<Version>` in `Directory.Build.props`, whatever it changes.
- A change to the skill raises the plugin version: the `version` in `plugins/agterm/.claude-plugin/plugin.json`, `plugins/agterm/.codex-plugin/plugin.json` and the plugin entry of `.claude-plugin/marketplace.json`. Claude Code does not update an installed plugin while that string stays the same. The plugin version is separate from the app version, so a PR that leaves the skill alone leaves it alone too. `SkillTextTests` checks that the three are equal.

## Layout

```
AgTerm.slnx
src/AgTerm.Core/            host-free core library (net10.0, no UI dependencies; analogue of agtermCore)
src/AgTerm.ControlServer/   named-pipe control server + headless host (HeadlessControlActions)
src/AgTerm.Cli/             agtermctl (System.CommandLine)
src/AgTerm.Terminal/        ConPTY backend (P/Invoke, no UI deps)
src/AgTerm.Xterm/           WebView2/xterm.js bridge + real surfaces + LiveControlHost
src/AgTerm.App/             WinUI 3 app shell (thin; logic lives below)
tests/*/                    xUnit suites per project; AgTerm.E2E = headless wire E2E;
                            AgTerm.Xterm.Tests hosts the LIVE (ConPTY+WebView2) integration/E2E
docs/agterm-windows-spec.md the portable spec at the pin
docs/PLATFORM-NOTES.md      adaptations, Windows-only extensions, upstream changes adopted beyond the pin
docs/upstream/              the catalog and snapshot of upstream after the pin
docs/plans/                 plans and specs; finished ones in docs/plans/completed/
parity/                     surface.json (the pin), adopted.json, windows-only.json
plugins/agterm/             the agent skill and its plugin manifests
tools/extract_surface.py    surface extractor
```

## Build & test

```
dotnet build AgTerm.slnx
dotnet test AgTerm.slnx -m:1                     # WinUI app builds separately:
dotnet build src/AgTerm.App -p:Platform=x64
```

Both builds treat warnings as errors; use `--no-incremental` to check all warnings after a local build.

Run test projects one at a time with `-m:1`, as CI does. Parallel projects make the live ConPTY and WebView2 suites contend for CPU.
