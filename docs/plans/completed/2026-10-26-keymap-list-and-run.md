# keymap list and keymap run Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `keymap.list` (the resolved keymap on the pipe) and `keymap.run` (start a custom command by name against a session), as upstream `f4b7a4aa` and `49628f8a` define them, adapted to Windows.

**Architecture:** A host-free projection in Core turns a `Keymap` plus its diagnostics into the wire payload. `HeadlessControlActions` answers `keymap.list` from the keymap the window armed (a provider the window sets) and runs `keymap.run` through a runner the window sets; the headless host has no runner and refuses. The CLI gets two subcommands and a human formatter.

**Tech Stack:** .NET 10, System.Text.Json source generation (`ControlJsonContext`), System.CommandLine, xUnit 2, WinUI 3 app shell.

**Spec:** `docs/plans/completed/2026-10-26-keymap-list-and-run-design.md`. Upstream reference: `git -C <agterm-clone> show <sha>:<path>` with `ed831ab` for `ControlKeymap.swift`, `SocketClient.swift` (`formatKeymap`), `ControlServer+AppCommands.swift`, `ControlDispatcher.swift`, and `68e17937` for the duplicate-name rule.

## Global Constraints

- Wire names `keymap.list` and `keymap.run`; result field `keymap`; arg `name` (already in the pin).
- Error strings verbatim: `keymap.run requires a command name`, `no custom command named <name>`, `custom commands are not available`, `no such session`, `<name> did not start: <reason>`. Order of checks: name, command, runner, session.
- Duplicate name diagnostic verbatim: `command '<name>' is already defined; command skipped`.
- Payload field names and omission follow upstream `ControlKeymap.swift` at `ed831ab`: `path`, `actions[]` (`action`, `chord?`, `overridden?`), `commands[]` (`name`, `shortcut?`, `repeats`), `diagnostics[]` (`line`, `message`). `menu`, `alternates` and an action's `repeats` are never written. A command's `repeats` is always written, always `false`.
- Action chord spelling: modifiers `ctrl`, `alt`, `shift` in that order, then the key, from `WindowsChordDecoder.Effective`. `overridden: true` only when `Effective(resolved) != Effective(WindowsDefaultChords.ShippedFor(action))`.
- Command `shortcut` is the string as written in keymap.conf.
- Every PR raises `<Version>` in `Directory.Build.props` (main is 0.1.31). The skill change raises the plugin version 0.1.9 to 0.1.10 in `plugins/agterm/.claude-plugin/plugin.json`, `plugins/agterm/.codex-plugin/plugin.json`, `.claude-plugin/marketplace.json`.
- TDD, each test seen failing first. Comments say why, not what. Warnings are errors. Test projects run one at a time; Xterm with `--blame-hang-timeout 4m`.

## Review Focus

- `keymap run` against a session in a second window: it must run in that window, with that session's pane and directory, and must not select the session. Test in Task 5.
- `keymap run` for a session whose focused pane is the split: the command gets `AGT_PANE=right` and the split's directory; a session showing its scratch still runs on the primary or split, never scratch. Test in Task 5.
- A name with leading or trailing spaces is not trimmed: `keymap run " Build"` answers `no custom command named  Build`. Test in Task 4.
- `keymap list` right after `keymap reload` with a broken line: the new diagnostic is in `diagnostics[]` once the window armed the new keymap. Test in Task 5.
- An empty keymap still lists all 41 actions and the two Windows shadowing diagnostics the Keyboard Shortcuts view shows today. Test in Task 2.

---

### Task 1: Skip a second command with a taken name

**Files:**
- Modify: `src/AgTerm.Core/Keymap/KeymapParser.cs` (`ParseCommandLine`)
- Test: `tests/AgTerm.Core.Tests/Keymap/KeymapParserTests.cs`

- [ ] **Step 1: Write the failing tests**
  - `A_second_command_with_a_taken_name_is_skipped`: two `command "Build" ...` lines with different chords give one command (the first, with its chord) and one diagnostic on line 2 with the exact text from Global Constraints.
  - `Names_differing_only_in_case_are_distinct`: `command "build" ...` and `command "Build" ...` give two commands (upstream compares with `==`).
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.Core.Tests --filter "FullyQualifiedName~KeymapParserTests"`. Expected: the first test fails with two commands.
- [ ] **Step 3: Implement** the check in `ParseCommandLine`, after the shell line is known to be non-empty, before `commands.Add`, ordinal comparison.
- [ ] **Step 4: Run** `dotnet test tests/AgTerm.Core.Tests`. Expected: all pass.
- [ ] **Step 5: Commit** `Skip a custom command whose name is already taken`

### Task 2: Project the keymap for the wire

**Files:**
- Create: `src/AgTerm.Core/Protocol/ControlKeymap.cs`
- Create: `src/AgTerm.Core/Keymap/WindowsChordShadowing.cs` (moved from `src/AgTerm.App/WindowCommandRouter.cs:103-175`, with `FormatPhysicalChord`)
- Modify: `src/AgTerm.App/WindowCommandRouter.cs` (`EffectiveWindowsChordDiagnostics` delegates to the Core function), `src/AgTerm.Core/Protocol/ControlRequestResponse.cs` (`ControlResult.Keymap`), `src/AgTerm.Core/Protocol/ControlJsonContext.cs`
- Test: `tests/AgTerm.Core.Tests/Protocol/ControlKeymapTests.cs` (new)

**Interfaces:**
- Produces:
  - `public sealed record ControlKeymapAction { string Action; string? Chord; bool? Overridden; }`, `public sealed record ControlKeymapCommand { string Name; string? Shortcut; bool Repeats; }`, `public sealed record ControlKeymapDiagnostic { int Line; string Message; }`, `public sealed record ControlKeymap { string Path; ControlKeymapAction[] Actions; ControlKeymapCommand[] Commands; ControlKeymapDiagnostic[] Diagnostics; }` with `JsonPropertyName` camelCase and nulls omitted like the rest of the protocol.
  - `public static ControlKeymap ControlKeymapProjection.Project(Keymap keymap, IReadOnlyList<KeymapDiagnostic> parserDiagnostics, string path)`: the parser diagnostics, then `WindowsChordShadowing.Diagnostics(keymap)`.
  - `public static string WindowsChordShadowing.Spelling(PhysicalChord chord)`: `ctrl+alt+shift+key` order, lower case, as keymap.conf takes it.
  - `public static IReadOnlyList<KeymapDiagnostic> WindowsChordShadowing.Diagnostics(Keymap keymap)`: the moved body, same text.
  - `ControlResult` gains `[JsonPropertyName("keymap")] public ControlKeymap? Keymap { get; init; }`.

- [ ] **Step 1: Write the failing tests**
  - `An_empty_keymap_lists_every_builtin_action_in_order` (41 entries, `BuiltinActions.All` order).
  - `An_unmoved_action_shows_its_windows_default_in_windows_spelling` (`new_window` gives `ctrl+alt+n`, `toggle_split` gives `alt+d`, no `overridden`).
  - `An_action_without_a_chord_has_no_chord_field` (`rename_session`).
  - `A_map_line_to_another_key_marks_the_action_overridden` (`map ctrl+shift+d toggle_split`).
  - `A_map_line_to_the_same_windows_key_is_not_an_override` (`map ctrl+alt+n new_window`).
  - `Commands_keep_file_order_and_their_written_shortcut` (one with `cmd+shift+b`, one palette-only: `shortcut` omitted, `repeats` false on both).
  - `Diagnostics_list_the_parser_ones_then_the_windows_shadowing_ones` (empty keymap gives exactly the two line-0 shadowing diagnostics `WindowCommandRouterTests` pins today).
  - `The_payload_serializes_with_upstream_field_names_and_omissions`: `ControlJson.Serialize` of a response with this result contains `"keymap":{"path":`, `"repeats":false`, no `"menu"`, no `"alternates"`, no `"overridden":false`, no `"chord":null`.
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.Core.Tests --filter "FullyQualifiedName~ControlKeymapTests"`. Expected: build error, the types do not exist.
- [ ] **Step 3: Implement** the types, the projection, the move (App keeps `EffectiveWindowsChordDiagnostics` as a one-line delegate so its callers and `WindowCommandRouterTests` stay), and the JSON context entries.
- [ ] **Step 4: Run** `dotnet test tests/AgTerm.Core.Tests`, then `dotnet test tests/AgTerm.Xterm.Tests --blame-hang-timeout 4m --filter "FullyQualifiedName~WindowCommandRouterTests"`. Expected: all pass.
- [ ] **Step 5: Commit** `Project the resolved keymap into a wire payload`

### Task 3: Dispatch keymap.list and keymap.run

**Files:**
- Modify: `src/AgTerm.Core/Protocol/Command.cs` (enum and wire table), `src/AgTerm.Core/Protocol/ControlActions.cs` (`IControlActions`), `src/AgTerm.Core/Protocol/ControlDispatcher.cs`, `parity/adopted.json`
- Modify: `tests/AgTerm.Core.Tests/Ported/MockControlActions.cs`
- Test: `tests/AgTerm.Core.Tests/Ported/ControlDispatcherTests.cs`, the parity tests that read `parity/adopted.json` (`tests/AgTerm.Core.Tests/TestSupport/ParitySurface.cs` users)

**Interfaces:**
- Produces: `Command.KeymapList` (`keymap.list`), `Command.KeymapRun` (`keymap.run`); on `IControlActions`: `ControlResponse ListKeymap()` and `Task<ControlResponse> RunCustomCommand(string name, string? target, string? window)`.

- [ ] **Step 1: Write the failing tests**: `keymap.list` reaches `ListKeymap`; `keymap.run` with `name` reaches `RunCustomCommand` with the target and `args.window`; `keymap.run` with no name and with `""` answers `keymap.run requires a command name` without calling the actions; a name with spaces is passed unchanged. The parity test fails until `adopted.json` lists `keymap.list` (`f4b7a4aa`), `keymap.run` (`49628f8a`) and the result field `keymap` (`f4b7a4aa`).
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.Core.Tests`. Expected: build errors for the new members.
- [ ] **Step 3: Implement**: both commands go through `DispatchAppCommand` like `keymap.reload`; `keymap.run` is awaited.
- [ ] **Step 4: Run** `dotnet test tests/AgTerm.Core.Tests`. Expected: all pass.
- [ ] **Step 5: Commit** `Dispatch keymap.list and keymap.run`

### Task 4: Answer them in the host

**Files:**
- Modify: `src/AgTerm.ControlServer/HeadlessControlActions.cs` (keymap section near `ReloadKeymap`)
- Modify: `src/AgTerm.Xterm/LiveApplicationHost.cs:1263` (`UsesSingleSessionTarget` gains `Command.KeymapRun`)
- Test: `tests/AgTerm.ControlServer.Tests/HeadlessKeymapTests.cs` (new, in the style of `HeadlessHudTests`), `tests/AgTerm.E2E` (one pipe test per verb)

**Interfaces:**
- Consumes: `ControlKeymapProjection.Project` (Task 2), `IControlActions` members (Task 3).
- Produces on `HeadlessControlActions`:
  - `public Func<(Keymap Keymap, IReadOnlyList<KeymapDiagnostic> Diagnostics)>? ArmedKeymap { get; set; }`: set by a window; null means answer from the last `LoadKeymap`/`ReloadKeymap` result, which the actions now keep.
  - `public Func<CustomCommand, Guid, Task<string?>>? CustomCommandRunner { get; set; }`: starts the command against the session id; null when it started, else the reason.
  - `ListKeymap()` returns `ok` with `result.keymap` from `Project(keymap, diagnostics, _keymap.Path)`.
  - `RunCustomCommand` checks, in order: the command by exact name in the armed keymap, then the runner, then the session through the usual resolution; then awaits the runner outside the store gate and returns `ok` with `result.id`, or `<name> did not start: <reason>`.

- [ ] **Step 1: Write the failing tests**: headless `keymap.list` returns the path of the config dir's `keymap.conf`, 41 actions and a command from a written file; after `keymap.reload` of a changed file the list changes; `keymap.run` of a known name answers `custom commands are not available`; of an unknown name answers `no custom command named Nope`; `" Build"` is not found. With a test runner set: `ok` and the session id; a runner reason gives `Build did not start: <reason>`; a bad target gives the resolver's error and the runner is not called. E2E: `agtermctl keymap list --json` over the pipe has `result.keymap.actions`.
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.ControlServer.Tests --filter "FullyQualifiedName~HeadlessKeymapTests"`. Expected: fails, members missing.
- [ ] **Step 3: Implement** as in Interfaces, awaiting UI work outside the gate the way `TypeSession` does.
- [ ] **Step 4: Run** `dotnet test tests/AgTerm.ControlServer.Tests`, `dotnet test tests/AgTerm.E2E`. Expected: all pass.
- [ ] **Step 5: Commit** `Answer keymap.list and keymap.run in the host`

### Task 5: Run a custom command from the pipe in the window

**Files:**
- Modify: `src/AgTerm.Xterm/CustomCommandExecution.cs` (`CustomCommandExecutionRequest` gains `Guid? SessionId`; `CustomCommandInvocationResolver.ResolveSession` uses it before the active session)
- Modify: `src/AgTerm.App/MainWindow.xaml.cs` (sets `ArmedKeymap` and `CustomCommandRunner` where it subscribes to `KeymapReloaded`, about line 1345; `ExecuteCustomCommandAsync` split so the pipe and the palette share one start path)
- Test: `tests/AgTerm.Xterm.Tests/CustomCommandEnvironmentTests.cs` (resolver), `tests/AgTerm.Xterm.Tests/LiveWindowControlE2ETests.cs` (routing), a source test for the MainWindow wiring in the style of `SingleInstanceSourceTests`

**Interfaces:**
- Consumes: `ArmedKeymap`, `CustomCommandRunner` (Task 4).
- Produces: `CustomCommandExecutionRequest(Guid CommandId, CustomCommandSource Source, string? PaneId = null, long? HotkeyGeneration = null, string Selection = "", Guid? SessionId = null)`. With `SessionId` set, the resolver takes that session and its focused pane: `Right` when `SplitFocused` and a split surface exists, else `Left`; never `Scratch`.

- [ ] **Step 1: Write the failing tests**
  - Resolver: `A_request_for_a_session_runs_against_it_not_the_active_one` (cwd, `AGT_SESSION_ID`, `AGT_PANE=left`); `A_request_for_a_split_focused_session_takes_the_split` (`AGT_PANE=right`, the split's cwd); `A_session_with_its_scratch_shown_still_runs_on_primary_or_split`; an unknown `SessionId` resolves to null.
  - Routing: `keymap.run` with the id of a session in the second window reaches the second window's runner (set a test runner on both windows' actions) and leaves both windows' selected sessions unchanged.
  - Source: `ArmedKeymap` returns `_hotkeyStates.Active.Keymap` with the diagnostics armed with it; `CustomCommandRunner` builds a `Palette`-source request with `SessionId` and returns the `CustomCommandNotStartedException` message, and the palette path still queues `CustomCommandFailureNotice.NotStarted`.
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.Xterm.Tests --blame-hang-timeout 4m --filter "FullyQualifiedName~CustomCommandEnvironmentTests|FullyQualifiedName~LiveWindowControlE2ETests|FullyQualifiedName~KeymapRunWiringSourceTests"`. Expected: build errors, then failures.
- [ ] **Step 3: Implement.** The provider is called from the dispatch thread, so the window publishes the armed keymap and its diagnostics as one immutable pair with `Volatile.Write` when it arms, and the provider reads that pair. The runner reads the selection from the resolved pane's surface on the UI thread, as `ExecuteCustomCommandAsync` does, and returns once the process started.
- [ ] **Step 4: Run** `dotnet build src/AgTerm.App -p:Platform=x64`, then `dotnet test tests/AgTerm.Xterm.Tests --blame-hang-timeout 4m`. Expected: 0 warnings, all pass.
- [ ] **Step 5: Commit** `Run a custom command from keymap.run in the window that owns the session`

### Task 6: CLI subcommands and output

**Files:**
- Modify: `src/AgTerm.Cli/MiscCommands.cs` (`keymap` gains `list` and `run`), `src/AgTerm.Cli/ClientOutput.cs` (`FormatKeymap`, routed before the `Text` branch)
- Test: `tests/AgTerm.Cli.Tests/KeymapCommandTests.cs` (new)

**Interfaces:**
- Consumes: `ControlResult.Keymap` (Task 2), `WireCommand.KeymapList`/`KeymapRun` (Task 3).
- Produces: `agtermctl keymap list [--json] [--socket S]`; `agtermctl keymap run NAME [--target T] [--window W] [--json] [--socket S]`, help text from upstream `49628f8a` `MiscCommands.swift` with "palette" in place of macOS wording; `public static string ClientOutput.FormatKeymap(ControlKeymap keymap)`.

- [ ] **Step 1: Write the failing tests**: request building for both (`keymap run "Build" --target abc --window w1` gives `cmd keymap.run`, `target abc`, `args.name Build`, `args.window w1`); `FormatKeymap` output equals upstream's rule for a sample payload: header `keymap: <path>`, blank line, `actions:` rows `  <mark> <name padded to the longest>  <chord or ->`, `commands:` rows `    <name>  <shortcut or (palette only)>`, `diagnostics:` rows `    line N: <message>` or `    <message>` for line 0, empty blocks left out, no trailing newline; `keymap run` success prints `ok`.
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.Cli.Tests --filter "FullyQualifiedName~KeymapCommandTests"`. Expected: failures.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** `dotnet test tests/AgTerm.Cli.Tests`. Expected: all pass, `Every_agtermctl_line_in_a_code_block_parses` included once Task 7 adds lines.
- [ ] **Step 5: Commit** `Add agtermctl keymap list and keymap run`

### Task 7: Documentation and versions

**Files:**
- Modify: `plugins/agterm/skills/agterm/SKILL.md` (summary count 65 to 67, `keymap` line), `plugins/agterm/skills/agterm/reference.md` (`## keymap`: both commands, from upstream `ed831ab` reference.md 1490-1518 without `menu[]`, alternates, `--repeat` and HTML pages, with Windows chord spelling)
- Modify: the three plugin manifests (0.1.10), `Directory.Build.props` (0.1.32)
- Modify: `docs/PLATFORM-NOTES.md`: `## Upstream features adopted beyond the pin` gets both verbs and what is left out; `## Upstream behavior adopted beyond the pin` gets the duplicate-name rule (`68e17937`); `docs/upstream/delta-7df70fe-to-v0.36.0.md` marks the `keymap.list` and `keymap.run` lines adopted the way earlier items are marked
- Test: `tests/AgTerm.Cli.Tests/SkillTextTests.cs` (existing)

- [ ] **Step 1:** Write the skill and notes changes.
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.Cli.Tests`. Expected: all pass, including `Reference_names_every_wire_command_in_parity` and `Plugin_manifests_carry_one_version`.
- [ ] **Step 3: Commit** `Document keymap list and keymap run`

### Task 8: Owner's manual check

- [ ] Build the debug app and give the owner PowerShell steps with an isolated `AGTERM_STATE_DIR` and `AGTERM_CONTROL_SOCKET`, a `keymap.conf` with `map ctrl+` select_theme`, `command "Hello" ctrl+shift+9 Set-Content -Path "$env:TEMP\agterm-hello.txt" -Value $env:AGT_SESSION_ID`, a duplicate `command "Hello"` line and a broken line. Check: `keymap list` shows `* select_theme  ctrl+``, `new_window  ctrl+alt+n`, one `Hello`, the duplicate and broken-line diagnostics; `keymap run Hello --target <id of a background session>` writes that id to the file and does not select the session; `keymap run Nope` prints `error: no custom command named Nope`.
