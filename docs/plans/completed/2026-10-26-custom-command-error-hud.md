# Custom Command Failure Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A custom command in `keymap.conf` can opt into a ten-second HUD that appears over its session when the command fails, with `--error-hud`, `--error-position POS` and `--error-pane left|right`.

**Architecture:** The keymap parser reads the three options as a prefix of the shell body and stores them on `CustomCommand`. An opted-in command starts through a new native launcher that sends its stderr to a temp file; when it exits non-zero (or never starts), the window opens a HUD with the name, the reason and the last usable stderr line through `HeadlessControlActions`, falling back to the whole session when the pane is not shown. Commands without `--error-hud` run exactly as today. `keymap list` reads the three options back, as upstream `17e37f38` does.

**Tech Stack:** .NET 10, xUnit 2, P/Invoke `CreateProcessW` (existing `ConPtyNative`), WinUI 3 app shell.

**Spec:** upstream agterm `9247b96f` (#613, failure panel) and `17e37f38` (#622, opt-in flags). Read with `git -C C:/git/github/umputun/agterm show <sha>`. Behavior text: upstream `plugins/agterm/skills/agterm/reference.md` at `ed831ab`, lines 1563-1582, and `.claude/rules/keymap.md` at `ed831ab`, lines 37-42 and 114-122. Read-back: upstream `17e37f38` `ControlKeymap.swift` (`ControlKeymapCommand`) and `SocketClient.swift` (`formatKeymap`). HUD auto-hide (`hideAfter`), `keymap list` and `keymap run` are already ported (PR #57).

## Global Constraints

- Grammar, from upstream `parseCommandOptions`: `command "<name>" [chord] [options] <shell...>`. Options are a contiguous prefix after the optional chord, any order: `--error-hud`, `--error-position POS`, `--error-pane left|right`. The first token that does not start with `--error-` ends the prefix and stays shell text. A `--` token at that point is consumed and ends the prefix. A chord is never looked for after the options.
- Diagnostics, copied verbatim (each skips the command, one diagnostic per line): `command '<name>' has unknown option '<option>'`, `command '<name>' repeats option '<option>'`, `command '<name>' requires a valid HUD position after --error-position`, `command '<name>' requires left or right after --error-pane`, `command '<name>' requires --error-hud for error placement options`, `command '<name>' has no shell line`.
- Defaults: `ErrorHud` false, `ErrorPosition` `HudPositions.Default` (center), `ErrorPane` null. Positions parse with `HudPositions.Parse` (nine names plus `top`/`bottom`).
- Panel: `HudSpec` with `Message = CommandFailure.Message(name, reason)`, `Detail = CommandFailure.Detail(tail)`, `Position = ErrorPosition`, `HideAfter = 10`. Reason is `exit <code>` for a command that ran, the launch error text for one that never started.
- Only opted-in commands capture stderr. Stdin and stdout go to `NUL`. Last `16 * 1024` bytes read from the sampled size, then the file is deleted. Exit code 0 reports nothing whatever was printed.
- Placement: an explicit pane is resolved at failure time with the HUD's own rule (`requireVisible: true`); a rejection falls back to the whole session at the same position and writes a `Debug.WriteLine`. An `overlay already open` refusal (a program overlay) is logged and not retried. A sessionless keybind gets no panel.
- The existing `Command failed` InfoBar for a command that never started stays as it is. A non-zero exit without `--error-hud` still reports nothing (upstream posts a banner; this port has none for that case, see Task 7).
- Read-back in `keymap list`: each `commands[]` entry gains `errorHud` (always written, bool), `errorPosition` (always written, the canonical name from `HudPositions.Wire`, `center` by default) and `errorPane` (`left` or `right`, omitted for the whole session), in upstream `CodingKeys` order: `name`, `shortcut`, `errorHud`, `errorPosition`, `errorPane`, `repeats`. The human row appends `  --error-hud --error-position <pos>` and ` --error-pane <pane>` for an opted-in command only.
- Every PR raises `<Version>` in `Directory.Build.props` (main is 0.1.33, so 0.1.34). A skill change raises the plugin version in `plugins/agterm/.claude-plugin/plugin.json`, `plugins/agterm/.codex-plugin/plugin.json` and `.claude-plugin/marketplace.json` (now 0.1.10, so 0.1.11).
- TDD: each test is seen failing before its code exists. Comments say why, not what. Warnings are errors.
- Run test projects one at a time; Xterm with `--blame-hang-timeout 4m`.

## Review Focus

- A background descendant that keeps the stderr file open (`start /b` in cmd, `Start-Process -NoNewWindow` in pwsh) must not delay the failure report: the wait is on the command's own process handle. Test in Task 4.
- A command that writes nothing to stderr and exits non-zero shows the message with no detail line. Test in Task 5.
- A pane named by `--error-pane right` in a session without a shown split falls back to session-wide instead of failing. Test in Task 6.
- An opted-in command whose session directory is gone shows both the InfoBar and the panel, with the reason `session directory <path> no longer exists`. Test in Task 5.
- A shell body that itself contains `--error-pane right` after an ordinary token keeps it as shell text. Test in Task 1.

---

### Task 1: Parse the error options in keymap.conf

**Files:**
- Modify: `src/AgTerm.Core/Keymap/CustomCommand.cs`
- Modify: `src/AgTerm.Core/Keymap/KeymapParser.cs:295-339`
- Test: `tests/AgTerm.Core.Tests/Keymap/CommandErrorOptionsTests.cs` (new)

**Interfaces:**
- Produces: on `CustomCommand`, settable properties `bool ErrorHud` (default false), `HudPosition ErrorPosition` (default `HudPositions.Default`), `OverlayPane? ErrorPane` (default null). Constructor unchanged.

- [ ] **Step 1: Write the failing tests**, a port of upstream `CommandErrorOptionsTests.swift` at `ed831ab` without the `--repeat` and `ControlKeymap` cases:
  - `A_plain_command_has_no_error_panel`: `command "probe" ./script` gives `ErrorHud` false, `Center`, `ErrorPane` null.
  - `Error_hud_after_a_chord_or_the_name_turns_the_panel_on` (theory over `""` and `"ctrl+shift+p "`): no diagnostics, `ErrorHud` true, `Shortcut` equals the trimmed chord, `Command == "./script"`.
  - `Options_in_any_order_set_position_and_pane` (theory over the three orders of `--error-hud`, `--error-position top-right`, `--error-pane left`): `TopRight`, `Left`, `Command == "./script"`.
  - `Every_hud_position_name_is_accepted` (theory over the nine names plus `top`, `bottom`): `ErrorPosition == HudPositions.Parse(name)`.
  - `An_option_after_the_first_shell_token_is_shell_text`: `--error-hud ./script --error-pane right` gives `Command == "./script --error-pane right"`.
  - `Double_dash_ends_the_options`: `-- --error-hud 'two  spaces'` gives no diagnostics and `Command == "--error-hud 'two  spaces'"`.
  - `A_chord_after_the_options_is_shell_text`: `--error-hud ctrl+a>p ./script` gives empty `Shortcut` and `Command == "ctrl+a>p ./script"`.
  - `Malformed_options_skip_the_command` (theory over upstream's list: `"--error-position top echo ok"`, `"--error-pane left echo ok"`, `"--error-hud --error-position"`, `"--error-hud --error-pane"`, `"--error-hud --error-position sideways echo ok"`, `"--error-hud --error-pane scratch echo ok"`, `"--error-hud --error-position --error-pane left echo ok"`, `"--error-hud --error-hud echo ok"`, `"--error-hud --error-position top --error-position bottom echo ok"`, `"--error-hud --error-pane left --error-pane right echo ok"`, `"--error-positon top echo ok"`, `"--error-hud=on echo ok"`, `"--error-hud"`, `"--error-hud --"`, `"--"`): no commands, exactly one diagnostic, on line 1.
  - `Malformed_option_messages_match_upstream`: one case per Global Constraints message, asserting the exact text.

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/AgTerm.Core.Tests --filter "FullyQualifiedName~CommandErrorOptionsTests"`
Expected: build error, `ErrorHud` is not a member of `CustomCommand`.

- [ ] **Step 3: Implement**

Add the three properties to `CustomCommand`. In `KeymapParser.ParseCommandLine`, replace the empty-shell-line check with a call to `private static CustomCommand? ParseCommandOptions(string body, string name, string shortcut, int line, List<KeymapDiagnostic> diagnostics)`, which follows upstream's loop (see Spec, `17e37f38` `Keymap.swift`) and returns null after adding the diagnostic. Update the class summary's grammar line.

- [ ] **Step 4: Run the Core tests**

Run: `dotnet test tests/AgTerm.Core.Tests`
Expected: all pass, including the existing `KeymapParserTests`.

- [ ] **Step 5: Commit** `Parse --error-hud, --error-position and --error-pane in keymap.conf`

### Task 2: Read the options back in keymap list

**Files:**
- Modify: `src/AgTerm.Core/Protocol/ControlKeymap.cs` (`ControlKeymapCommand`, `ControlKeymapProjection.Project`), `src/AgTerm.Cli/ClientOutput.cs` (`FormatKeymap`)
- Test: `tests/AgTerm.Core.Tests/Protocol/ControlKeymapTests.cs`, `tests/AgTerm.Cli.Tests/KeymapCommandTests.cs`

**Interfaces:**
- Consumes: `CustomCommand.ErrorHud/ErrorPosition/ErrorPane` (Task 1).
- Produces: `ControlKeymapCommand` gains `bool ErrorHud` (`[JsonIgnore(Condition = JsonIgnoreCondition.Never)]`, like `Repeats`), `string ErrorPosition` (always written) and `string? ErrorPane`, declared in upstream's order between `Shortcut` and `Repeats`.

- [ ] **Step 1: Write the failing tests**, a port of upstream's read-back case (`ed831ab` `CommandErrorOptionsTests.swift`, the test with `command "quiet"` and `command "loud" --error-hud --error-position bottom --error-pane right`): `quiet` projects `errorHud:false`, `errorPosition:"center"`, no `errorPane`; `loud` projects `true`, `"bottom-center"` (the `bottom` alias reads back canonical), `"right"`. A serialization test asserts the field order in the JSON text. CLI: `FormatKeymap` prints `    loud  (palette only)  --error-hud --error-position bottom-center --error-pane right` and `    quiet  (palette only)` unchanged.
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.Core.Tests --filter "FullyQualifiedName~ControlKeymapTests"` and `dotnet test tests/AgTerm.Cli.Tests --filter "FullyQualifiedName~KeymapCommandTests"`. Expected: build errors for the new members.
- [ ] **Step 3: Implement** the fields, the projection and the human row.
- [ ] **Step 4: Run** `dotnet test tests/AgTerm.Core.Tests` and `dotnet test tests/AgTerm.Cli.Tests`. Expected: all pass.
- [ ] **Step 5: Commit** `Read the error options back in keymap list`

### Task 3: Shape the failure text

**Files:**
- Create: `src/AgTerm.Core/Keymap/CommandFailure.cs`
- Test: `tests/AgTerm.Core.Tests/Keymap/CommandFailureTests.cs` (new)

**Interfaces:**
- Produces: `public static class CommandFailure` with `public const int TailLimit = 16 * 1024;`, `public static string? Detail(ReadOnlySpan<byte> tail)`, `public static string Message(string name, string reason)`.

- [ ] **Step 1: Write the failing tests**, a port of upstream `CommandFailureTests.swift` at `ed831ab` (all 13 cases, same inputs and expected strings), with `HudLayout.TextLength` and `HudSpec.MaxTextLength` in place of the Swift names.

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/AgTerm.Core.Tests --filter "FullyQualifiedName~CommandFailureTests"`
Expected: build error, `CommandFailure` does not exist.

- [ ] **Step 3: Implement** following upstream `CommandFailure.swift`: lenient UTF-8 decode (`Encoding.UTF8.GetString` replaces a cut scalar), split on `\r` and `\n`, clean each line from the last (strip ESC sequences: CSI to a final byte 0x40-0x7E, OSC to BEL or `ESC \`, two-character forms; then `TerminalText.Sanitized`; then trim), cap to `HudSpec.MaxTextLength` in `HudLayout.TextLength` units by removing from the end. `Message` keeps the reason and truncates the name.

- [ ] **Step 4: Run the Core tests**

Run: `dotnet test tests/AgTerm.Core.Tests`
Expected: all pass.

- [ ] **Step 5: Commit** `Shape a failed custom command's message and stderr line for a HUD`

### Task 4: Start a command with stderr in a temp file

**Files:**
- Create: `src/AgTerm.Terminal/StderrCapturingProcess.cs`
- Modify: `src/AgTerm.Terminal/ConPtyNative.cs` (add `SetHandleInformation`, `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`, `CREATE_NO_WINDOW` if missing)
- Test: `tests/AgTerm.Terminal.Tests/StderrCapturingProcessTests.cs` (new)

**Interfaces:**
- Consumes: `CommandFailure.TailLimit` (Task 3).
- Produces: `public sealed class StderrCapturingProcess` with `public static StderrCapturingProcess Start(ProcessStartInfo startInfo)`, `public int ProcessId { get; }`, `public Task<CapturedExit> Exited { get; }`; `public sealed record CapturedExit(int ExitCode, byte[] StderrTail)`. `Start` throws `Win32Exception` when `CreateProcessW` fails.

- [ ] **Step 1: Write the failing tests** (cmd.exe from `%COMSPEC%`, so they run on any Windows machine):
  - `The_exit_code_and_stderr_reach_the_caller`: `/d /c "echo boom 1>&2 & exit /b 3"` gives `ExitCode == 3` and a tail containing `boom`.
  - `A_clean_exit_reports_code_zero`: `/d /c "echo fine 1>&2"` gives 0.
  - `Only_the_last_tail_limit_bytes_are_kept`: a command writing 40 KiB to stderr gives `StderrTail.Length == CommandFailure.TailLimit`, ending with the last line written.
  - `The_temp_file_is_removed_after_exit`: no `agterm-cmd-*.stderr` file from this run remains in `Path.GetTempPath()`.
  - `A_background_descendant_does_not_delay_the_exit`: `/d /c "start /b ping -n 6 127.0.0.1 >nul & exit /b 2"` completes in under 3 s with code 2.
  - `Environment_and_working_directory_are_applied`: `startInfo.Environment["AGT_PROBE"] = "x1"`, `WorkingDirectory` a temp dir; `echo %AGT_PROBE% %CD% 1>&2` shows both.
  - `A_missing_program_throws_Win32Exception`.

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/AgTerm.Terminal.Tests --filter "FullyQualifiedName~StderrCapturingProcessTests"`
Expected: build error, `StderrCapturingProcess` does not exist.

- [ ] **Step 3: Implement**

Build the command line from `startInfo.FileName` plus `WindowsCommandLine.FromArgv(ArgumentList)` when the list is non-empty, else plus the raw `Arguments`; the environment block from `startInfo.Environment` (sorted, double-NUL terminated, as `ConPtySession.BuildEnvironmentBlock`). Open `NUL` for read and write and the temp file `Path.Combine(Path.GetTempPath(), $"agterm-cmd-{Guid.NewGuid():N}.stderr")` with `FileShare.ReadWrite | FileShare.Delete`, mark the three inheritable, and pass only them through a `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` with `STARTF_USESTDHANDLES`, `bInheritHandles: true`, flags `EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW`. Close the parent's copies right after the call. `Exited` waits on the process handle, reads `GetExitCodeProcess`, reads `min(size, TailLimit)` bytes ending at the size sampled once, and deletes the file whatever the code. On a failed `CreateProcessW` delete the file before throwing.

- [ ] **Step 4: Run the Terminal tests**

Run: `dotnet test tests/AgTerm.Terminal.Tests`
Expected: all pass.

- [ ] **Step 5: Commit** `Start a process with its stderr in a temp file`

### Task 5: Report a failed opted-in command

**Files:**
- Modify: `src/AgTerm.Xterm/CustomCommandExecution.cs`
- Create: `src/AgTerm.Xterm/CustomCommandFailureReporter.cs`
- Test: `tests/AgTerm.Xterm.Tests/CustomCommandFailureReporterTests.cs` (new), `tests/AgTerm.Xterm.Tests/CustomCommandEnvironmentTests.cs` (existing, extend)

**Interfaces:**
- Consumes: `CustomCommand.ErrorHud/ErrorPosition/ErrorPane` (Task 1), `CommandFailure` (Task 3), `StderrCapturingProcess` (Task 4).
- Produces:
  - `CustomCommandInvocation` gains `Guid? SessionId`, `bool ErrorHud`, `HudPosition ErrorPosition`, `OverlayPane? ErrorPane` (optional trailing parameters, defaults null/false/`HudPositions.Default`/null). The resolver fills them; `SessionId` is null for a sessionless keybind.
  - `CustomCommandProcessHandle` gains `Task<CustomCommandExit> Finished`; `public sealed record CustomCommandExit(int ExitCode, string? Detail)`. A command without `ErrorHud` keeps `Process.Start` and reports `Detail` null.
  - `public sealed record CustomCommandFailurePanel(Guid SessionId, HudSpec Spec, OverlayPane? Pane)`.
  - `public static class CustomCommandFailureReporter` with `public const double PanelSeconds = 10;`, `public static CustomCommandFailurePanel? ForExit(CustomCommandInvocation invocation, CustomCommandExit exit)` (null for code 0, no `ErrorHud`, or no session) and `public static CustomCommandFailurePanel? ForNotStarted(CustomCommandInvocation invocation, string reason)`.

- [ ] **Step 1: Write the failing tests**
  - Reporter: `A_non_zero_exit_of_an_opted_in_command_gives_a_ten_second_panel` (message `Build: exit 3`, detail passed through, position and pane from the invocation, `HideAfter == 10`); `Exit_zero_gives_no_panel`; `A_command_without_error_hud_gives_no_panel`; `A_sessionless_command_gives_no_panel`; `A_failure_with_no_stderr_has_no_detail`; `A_command_that_never_started_gives_the_reason_as_the_message` (`Build: session directory C:\gone no longer exists`).
  - Runner (in `CustomCommandEnvironmentTests` style, with `new CustomCommandProcessRunner(which: _ => null)` so cmd.exe runs the line on any machine): `An_opted_in_command_reports_its_exit_code_and_last_stderr_line` runs `echo first 1>&2 & echo build broke 1>&2 & exit /b 3` through the real `Start` and asserts `Finished` gives 3 and `build broke`; `A_plain_command_reports_its_exit_code_without_detail`; `The_resolver_carries_the_session_and_the_error_options`.

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/AgTerm.Xterm.Tests --blame-hang-timeout 4m --filter "FullyQualifiedName~CustomCommandFailureReporterTests|FullyQualifiedName~CustomCommandEnvironmentTests"`
Expected: build errors for the new members.

- [ ] **Step 3: Implement** the members above. In the synchronous `Start` (which `StartAsync` wraps), an `ErrorHud` invocation goes through `StderrCapturingProcess.Start(CreateStartInfo(...))`; `Finished` maps `CapturedExit` to `CustomCommandExit(code, CommandFailure.Detail(tail))`. A `Win32Exception` from either path becomes `CustomCommandNotStartedException(ex.Message)` so the caller has one not-started path.

- [ ] **Step 4: Run the Xterm tests for this area**

Run: the command from Step 2.
Expected: all pass.

- [ ] **Step 5: Commit** `Report a failed custom command that opted into a failure panel`

### Task 6: Open the panel over the session

**Files:**
- Modify: `src/AgTerm.ControlServer/HeadlessControlActions.cs` (next to `OpenHud`)
- Modify: `src/AgTerm.App/MainWindow.xaml.cs` (`StartCustomCommand`, the start helper shared by `ExecuteCustomCommandAsync` and `RunCustomCommandFromPipe`)
- Test: `tests/AgTerm.ControlServer.Tests/HeadlessHudTests.cs` (extend), `tests/AgTerm.Xterm.Tests/CustomCommandFailureWiringSourceTests.cs` (new, source test in the style of `SingleInstanceSourceTests`)

**Interfaces:**
- Consumes: `CustomCommandFailurePanel`, `CustomCommandFailureReporter` (Task 5).
- Produces: `public ControlResponse OpenCommandFailureHud(Guid sessionId, HudSpec spec, OverlayPane? pane)` on `HeadlessControlActions`: resolves the pane with `PanePlacement.TryResolve(..., requireVisible: true, ...)`, falls back to `paneIdentity: null` on rejection and sets `public string? LastFailurePanelFallback` (the rejection text) for the caller's log, then `store.OpenHud`. Returns `overlay already open` when the store refuses.

- [ ] **Step 1: Write the failing tests**
  - Headless: `A_failure_panel_opens_over_the_session_with_its_auto_hide` (tree `hud` shows the message and `hideAfter` 10); `A_failure_panel_for_a_hidden_pane_falls_back_to_the_whole_session` (`--error-pane right` without a split: ok, no pane identity, fallback text set); `A_failure_panel_does_not_replace_a_program_overlay` (open `overlay` first: response error `overlay already open`).
  - Source: `StartCustomCommand` keeps `QueueFailureNotice(CustomCommandFailureNotice.NotStarted(...))`, hands `ForNotStarted` and, after `Finished`, `ForExit` panels to `OpenCommandFailureHud` inside `host.StoreCoordinator.MutateAsync` started from `Task.Run`, and contains no direct call to `OpenCommandFailureHud`.

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/AgTerm.ControlServer.Tests --filter "FullyQualifiedName~HeadlessHudTests"`
Expected: build error, `OpenCommandFailureHud` does not exist.

- [ ] **Step 3: Implement** `OpenCommandFailureHud`, then wire `StartCustomCommand`. It runs on the dispatch thread under the host gate when `keymap run` calls it (every dispatch is synchronous under `lock(_gate)`), so it must never open the panel itself: it schedules the open with `Task.Run(() => host.StoreCoordinator.MutateAsync(...))`, observed with `ObserveTaskFault`, for the not-started case and, after `Finished`, for a non-zero exit. The InfoBar stays as today. Log a fallback or a refusal with `System.Diagnostics.Debug.WriteLine`, naming the command and the reason.

- [ ] **Step 4: Build the app and run the suites touched**

Run: `dotnet build AgTerm.slnx`, `dotnet build src/AgTerm.App -p:Platform=x64`, `dotnet test tests/AgTerm.ControlServer.Tests`, `dotnet test tests/AgTerm.Xterm.Tests --blame-hang-timeout 4m`
Expected: 0 warnings, all pass.

- [ ] **Step 5: Commit** `Show a failed custom command's panel over its session`

### Task 7: Document the options and raise the versions

**Files:**
- Modify: `plugins/agterm/skills/agterm/reference.md` (`### keymap.conf format`), `plugins/agterm/skills/agterm/SKILL.md` (keymap paragraph)
- Modify: `plugins/agterm/.claude-plugin/plugin.json`, `plugins/agterm/.codex-plugin/plugin.json`, `.claude-plugin/marketplace.json` (0.1.10 to 0.1.11)
- Modify: `docs/PLATFORM-NOTES.md` (`## Upstream features adopted beyond the pin`), `docs/upstream/delta-7df70fe-to-v0.36.0.md:109`, `Directory.Build.props` (0.1.33 to 0.1.34)
- Test: `tests/AgTerm.Cli.Tests/SkillTextTests.cs` (existing tests must pass)

- [ ] **Step 1:** In `reference.md`, change the `command` grammar line to `command "<name>" [chord] [error options] <shell...>` and add upstream's paragraph (`ed831ab` reference.md 1563-1582) adapted: no `--repeat`, Windows example bodies (`.\build.ps1`), no macOS banner sentence; say that a command without `--error-hud` reports only a start failure, through the `Command failed` bar. Add one line to `SKILL.md` like upstream's (`ed831ab` SKILL.md 635-637). In the `keymap list` part of reference.md, name `errorHud`, `errorPosition` and `errorPane` on `commands[]` and the human row's error options; in the `keymap run` part add upstream's note that a command with `--error-hud` still shows its panel (`ed831ab` reference.md 1490-1496).
- [ ] **Step 2:** In PLATFORM-NOTES, add an entry: the three options from `9247b96f`/`17e37f38`; stderr goes to a temp file through `CreateProcessW` with an inherited-handle list; `keymap list` reads them back; a non-zero exit without `--error-hud` shows nothing, where upstream posts a banner. Mark the catalog line 109 as adopted the way earlier adopted items are marked.
- [ ] **Step 3:** Raise the plugin version in the three files and the app version.
- [ ] **Step 4: Run** `dotnet test tests/AgTerm.Cli.Tests`. Expected: all pass, `Plugin_manifests_carry_one_version` included.
- [ ] **Step 5: Commit** `Document the custom command failure panel`

### Task 8: Owner's manual check

- [ ] Build the debug app (`dotnet build src/AgTerm.App -p:Platform=x64`) and give the owner PowerShell steps with an isolated `AGTERM_STATE_DIR` and `AGTERM_CONTROL_SOCKET`: a `keymap.conf` with `command "Fail" ctrl+shift+9 --error-hud Write-Error 'build broke'; exit 3`, one with `--error-position top-right --error-pane right`, and one plain failing command. Run them with `agtermctl keymap run Fail --target <session id>` as well as by the chord, and check `keymap list` shows the error options on the opted-in rows only. Expected: a panel `Fail: exit 3` with the error line, gone after about 10 s; the pane variant over the split when it is shown and over the session when it is not; nothing for the plain command.
