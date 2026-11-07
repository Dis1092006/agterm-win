# Codex session resume without the shared app-server: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** With `resumeAgentSessions` on, a pane that ran Codex comes back with its own Codex session, the same way Claude Code and Copilot CLI do.

**Architecture:** Codex 0.167 runs sessions and hooks in a shared background app-server by default. That process carries the `AGTERM_*` environment of the pane that started it first, so `agent-hook resume codex` pins the wrong pane. The fix has three parts. The resume setting turns `features.daemon_auto_start` off in `~\.codex\config.toml`, so a plain `codex` runs in its pane. The hook ignores a call from the shared server. The pin is `codex --no-daemon resume <id>`.

**Tech Stack:** .NET 10, xUnit, System.CommandLine. No TOML library: the config edit is a line edit.

**Spec:** this document. Background and measurements: the "Measured with Codex 0.167.0" section below. Earlier design: `docs/plans/completed/2026-11-agent-session-resume.md` (#69, #71).

## Measured with Codex 0.167.0 (2026-11-07, this machine)

| Codex run as | Where hooks run | Result |
|---|---|---|
| `codex` (default, shared app-server) | the app-server, with the env of the pane that started it | pin lands on that other pane; `/exit` does not clear it |
| `codex --no-daemon` | the pane | pin set on the first message, cleared about 2 s after `/exit` |
| `codex -c features.daemon_auto_start=false`, no app-server running | the pane | same as `--no-daemon`, no prompt |
| same, app-server running | — | Codex prompts "Background server has incompatible feature settings" (run without daemon / restart / cancel) |

- A hook run by the app-server has `CODEX_DAEMON_SHUTDOWN_SOCKET=1` in its environment. A hook run in the pane does not. No other variable besides `AGTERM_*` and `PATH` differs. The variable is internal and undocumented.
- Codex fires `SessionStart` on the first turn, not at startup. A Codex pane with no message gets no pin.
- A plain `codex` starts the app-server again whenever `daemon_auto_start` is on.

## Global Constraints

- TDD: a failing test before each piece of production code (CLAUDE.md).
- App version `0.1.108` → `0.1.109` (main took 0.1.108 in #75) in `Directory.Build.props`. The skill changes, so the plugin version `0.1.52` → `0.1.53` in `plugins/agterm/.claude-plugin/plugin.json`, `plugins/agterm/.codex-plugin/plugin.json` and `.claude-plugin/marketplace.json`.
- No wire command or field changes. `agent-hook resume` stays the Windows-only CLI listed in `parity/windows-only.json`. The setting name `resumeAgentSessions` stays.
- The config line agterm-win owns is exactly `daemon_auto_start = false # agterm-win`.
- New pin prefix for Codex: `codex --no-daemon resume `. The pin from 0.1.107, `codex resume <id>`, still clears on end.
- The guard variable: `CODEX_DAEMON_SHUTDOWN_SOCKET` (any non-empty value).
- This plan is done in one PR, so the last task moves it to `docs/plans/completed/`.

## Review Focus

- `config.toml` with CRLF line endings: the edit keeps CRLF everywhere (Task 2 test `Enable_keeps_crlf_line_endings`).
- The user set `daemon_auto_start` themselves (either value): agterm-win never changes or removes that line. The hint explains that `true` keeps Codex out of resume (Task 2 `Enable_leaves_a_user_value_alone`, Task 3 `Describe_explains_a_user_daemon_setting`).
- Turning the setting off with other keys under `[features]`: only our line goes, the user's keys and the header stay (Task 2 `Disable_removes_only_our_line`).
- A pane pinned by 0.1.107 (`codex resume <id>`) and then `/exit`: the pin is cleared (Task 1 `Codex_end_clears_the_pin_from_before_no_daemon`).
- A hook from the shared app-server: nothing is sent to the pipe, exit 0 (Task 1 `Codex_hook_from_the_shared_app_server_sends_nothing`).

---

### Task 1: The Codex hook ignores the shared app-server and pins `codex --no-daemon resume`

**Files:**
- Modify: `src/AgTerm.Core/Agents/AgentResume.cs`
- Modify: `src/AgTerm.Cli/AgentHookCommands.cs`
- Test: `tests/AgTerm.Core.Tests/Agents/AgentResumeHookTests.cs`, `tests/AgTerm.Cli.Tests/AgentHookCommandTests.cs`, `tests/AgTerm.E2E/AgentResumeHookE2ETests.cs`

**Interfaces:**
- Produces: `AgentResumeProfile` gains `string? LegacyPinPrefix` and `string? IgnoredWhenSet` (both default `null`). Codex: `PinPrefix = "codex --no-daemon resume "`, `LegacyPinPrefix = "codex resume "`, `IgnoredWhenSet = "CODEX_DAEMON_SHUTDOWN_SOCKET"`.
- Produces: `AgentResumeDecision(AgentResumeVerb Verb, string? Command, string? LegacyCommand = null)`, and `ClearIfPinned(string command, string? legacyCommand = null)`.

- [ ] **Step 1: Write the failing tests**

In `AgentResumeHookTests`:
- update the existing codex start and end cases: start gives `AgentResumeDecision.Pin("codex --no-daemon resume " + Id)`, end gives `AgentResumeDecision.ClearIfPinned("codex --no-daemon resume " + Id, "codex resume " + Id)`;
- `Claude_end_has_no_legacy_command`: the claude end decision has `LegacyCommand == null`.

In `AgentHookCommandTests`:
- `Codex_hook_from_the_shared_app_server_sends_nothing`: `PaneEnvironment` plus `CODEX_DAEMON_SHUTDOWN_SOCKET = "1"`, `["codex","start"]` with a valid start payload → exit 0, `run.Sent` is empty, `run.Error` is empty;
- `Codex_end_clears_the_pin_from_before_no_daemon`: tree answers `TreeWith("codex resume " + Id)`, `["codex","end"]` with `End("other")` → the last request is `session.restore` with `Mode == "clear"`;
- update the existing codex end tests to the new pin `codex --no-daemon resume `.

In `AgentResumeHookE2ETests`: `[InlineData("codex", "codex --no-daemon resume ")]`, and every literal `"codex resume " + SessionId` becomes the new prefix.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AgTerm.Core.Tests -m:1 --filter FullyQualifiedName~AgentResumeHook` and `dotnet test tests/AgTerm.Cli.Tests -m:1 --filter FullyQualifiedName~AgentHookCommand`
Expected: FAIL. The pin is still `codex resume `, and the daemon-env run sends `session.restore`.

- [ ] **Step 3: Implement**

- `AgentResumeHook.Decide`: on end, `ClearIfPinned(pin, profile.LegacyPinPrefix is { } legacy ? legacy + id : null)`.
- `Resume.Execute`: right after the `AGTERM_SESSION_ID` check, return 0 when `Agent.IgnoredWhenSet` is set and `env.GetEnv(it)` is non-empty. In the clear path, clear when `current.RestoreCommand` equals `decision.Command` or a non-null `decision.LegacyCommand`.
- Comment on the codex profile line: the shared app-server carries another pane's environment (measured with 0.167.0).

- [ ] **Step 4: Run the tests to verify they pass**

Run the two commands from Step 2, then `dotnet test tests/AgTerm.E2E -m:1 --filter FullyQualifiedName~AgentResumeHook`
Expected: PASS.

- [ ] **Step 5: Commit**

`git commit -m "Ignore Codex hooks from the shared app-server and pin codex --no-daemon resume"`

---

### Task 2: A line editor for `features.daemon_auto_start` in Codex `config.toml`

**Files:**
- Create: `src/AgTerm.Core/Install/CodexFeatureConfig.cs`
- Test: `tests/AgTerm.Core.Tests/Install/CodexFeatureConfigTests.cs`

**Interfaces:**
- Produces:
  - `public enum CodexDaemonSetting { Unset, Ours, UserOff, UserOn }`
  - `public static class CodexFeatureConfig`
  - `public const string OwnedLine = "daemon_auto_start = false # agterm-win";`
  - `public static CodexDaemonSetting Inspect(string? toml)`
  - `public static string? Enable(string? toml)`: the new text, or null when nothing changes
  - `public static string? Disable(string? toml)`: the new text, or null when nothing changes
  - Both throw `InvalidDataException("config.toml sets features inline, so agterm-win left it unchanged.")` for a top-level `features = { … }`.

Parsing rules (the algorithm the tests do not fully fix):
- Work line by line and keep the file's line ending (`\r\n` when the file has one, else `\n`).
- A line matching `^\s*\[` starts a table. `^\s*\[\s*features\s*\]\s*(#.*)?$` starts `[features]`.
- In `[features]`, `^\s*daemon_auto_start\s*=\s*(true|false)\b`. Before the first table, `^\s*features\s*\.\s*daemon_auto_start\s*=\s*(true|false)\b`. The exact `OwnedLine` (trimmed) is `Ours`, any other match is `UserOff` or `UserOn`.
- `Enable` on `Unset`: insert `OwnedLine` right after the `[features]` header. With no header, append a blank line (when the file is not empty), `[features]`, then `OwnedLine`, with a final newline.
- `Disable` removes the `Ours` line only.

- [ ] **Step 1: Write the failing tests** (xUnit `[Fact]`s, exact strings)

- `Enable_on_a_missing_file_writes_a_features_table`: `Enable(null)` == `"[features]\ndaemon_auto_start = false # agterm-win\n"`.
- `Enable_appends_the_table_after_other_settings`: `"model = \"o3\"\n"` → `"model = \"o3\"\n\n[features]\ndaemon_auto_start = false # agterm-win\n"`.
- `Enable_adds_the_line_under_an_existing_features_table`: `"[features]\nhooks = true\n[tui]\n"` → `"[features]\ndaemon_auto_start = false # agterm-win\nhooks = true\n[tui]\n"`.
- `Enable_keeps_crlf_line_endings`: as above with `\r\n` everywhere, and the result has no bare `\n`.
- `Enable_leaves_a_user_value_alone`: for `"[features]\ndaemon_auto_start = true\n"`, `"[features]\ndaemon_auto_start=false\n"` and `"features.daemon_auto_start = true\n"`, `Enable` returns null.
- `Enable_is_idempotent`: `Enable(Enable(null))` is null.
- `Inspect_reports_each_state`: Unset for `null` and `"[tui]\n"`, Ours for the `Enable(null)` text, UserOn and UserOff for the user lines above. A `daemon_auto_start` under `[tui]` is Unset.
- `Disable_removes_only_our_line`: `"[features]\ndaemon_auto_start = false # agterm-win\nhooks = true\n"` → `"[features]\nhooks = true\n"`.
- `Disable_leaves_a_user_value_alone`: `Disable("[features]\ndaemon_auto_start = false\n")` is null, and `Disable(null)` is null.
- `An_inline_features_table_is_refused`: `Enable("features = { hooks = true }\n")` throws `InvalidDataException` with the message above.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AgTerm.Core.Tests -m:1 --filter FullyQualifiedName~CodexFeatureConfig`
Expected: FAIL to compile, `CodexFeatureConfig` does not exist.

- [ ] **Step 3: Implement `CodexFeatureConfig` by the rules above**

- [ ] **Step 4: Run the tests to verify they pass**

Same command. Expected: PASS.

- [ ] **Step 5: Commit**

`git commit -m "Add a line editor for Codex features.daemon_auto_start"`

---

### Task 3: The resume setting turns the Codex app-server off, and the hint says so

**Files:**
- Modify: `src/AgTerm.Core/Install/AgentResumeInstaller.cs`
- Test: `tests/AgTerm.Core.Tests/Install/AgentResumeInstallerTests.cs`

**Interfaces:**
- Consumes: `CodexFeatureConfig.Inspect/Enable/Disable` (Task 2). `AgentHooksInstaller.WriteConfig(path, existing, merged)` writes atomically with a `.bak`, as for `hooks.json`.
- Produces: `AgentResumeAgentStatus.Reason` may be set on an `On` agent too. `Describe` prints it as a sentence after the state groups.

Copy (exact):
- Codex on, replacing the current Codex sentence: `"Codex asks you to trust the new hooks the next time it starts. If Codex's background server is running, stop it once with codex app-server daemon stop."`
- Codex on with `UserOn`, as the agent's Reason: `"daemon_auto_start = true in Codex config.toml keeps Codex sessions in its background server, so they are not resumed."`

- [ ] **Step 1: Write the failing tests**

- `EnablingWritesTheCodexDaemonSetting`: after `Apply(…, enabled: true, …)` with `~\.codex` present, `~\.codex\config.toml` has `OwnedLine` under `[features]`.
- `EnablingKeepsTheUserCodexConfigAndBacksItUp`: an existing `config.toml` with `model = "o3"` keeps that line, and `config.toml.bak` equals the old text.
- `DisablingRemovesOnlyTheCodexDaemonLine`: enable, add `hooks = true` under `[features]` by hand, disable → `config.toml` has no `OwnedLine` and still has `hooks = true`.
- `A_user_daemon_setting_is_left_alone`: `config.toml` = `"[features]\ndaemon_auto_start = true\n"` → unchanged after enable, Codex `State == On`, and its `Reason` equals the UserOn copy.
- `An_inline_features_table_fails_Codex_only`: `features = { hooks = true }` → Codex `Failed` with the Task 2 message, `hooks.json` still written, and Claude and Copilot unaffected.
- `Describe_explains_a_user_daemon_setting`: `new AgentResumeReport([new("Codex", On, "<UserOn copy>")]).Describe()` == `"Codex: on. <UserOn copy> Codex asks you to trust the new hooks the next time it starts. If Codex's background server is running, stop it once with codex app-server daemon stop."`.
- Update `DescribeGroupsTheAgentsByState` to the new Codex sentence.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AgTerm.Core.Tests -m:1 --filter FullyQualifiedName~AgentResumeInstaller`
Expected: FAIL. No `config.toml` is written and the old sentence remains.

- [ ] **Step 3: Implement**

- In the `"codex"` arm of `Apply`, after `ApplyEvents` returns true, call a new `private static string? ApplyCodexConfig(string codexDirectory, bool enabled)`. It reads `config.toml` (absent → null), calls `Enable` or `Disable`, writes through `WriteConfig` when the result is non-null, and returns the UserOn Reason when `Inspect` says `UserOn`. `InvalidDataException` falls into the existing `catch` and makes Codex `Failed`.
- `Describe`: after the state groups, add the `Reason` of each non-failed agent that has one, then the Codex sentence.

- [ ] **Step 4: Run the tests to verify they pass**

Same command, then the whole Core suite: `dotnet test tests/AgTerm.Core.Tests -m:1`. Expected: PASS.

- [ ] **Step 5: Commit**

`git commit -m "Turn the Codex shared app-server off with the resume setting"`

---

### Task 4: Docs, skill, versions

**Files:**
- Modify: `docs/PLATFORM-NOTES.md` (the agent session resume bullets, about lines 937–948), `README.md:168`, `plugins/agterm/skills/agterm/reference.md` (about 1498–1520), `plugins/agterm/skills/agterm/troubleshooting.md` (about 66–80), `plugins/agterm/skills/agterm/examples.md` (about 73–78), `Directory.Build.props`, the three plugin version files.
- Move: this plan to `docs/plans/completed/2026-11-07-codex-resume-no-daemon.md`.

- [ ] **Step 1: Update the text**

- PLATFORM-NOTES: the Codex pin is `codex --no-daemon resume <id>`. The `0.1.107` pin `codex resume <id>` still clears. Why: Codex 0.167 runs hooks in a shared app-server with another pane's environment. The setting writes the owned line to `config.toml` and removes it when turned off, and a user value is left alone. The guard on `CODEX_DAEMON_SHUTDOWN_SOCKET`, internal to Codex: if Codex drops it, a hook from the shared server pins the wrong pane again. Codex pins on the first message. Stop a running server once.
- reference.md: the new Codex pin, the guard, and pinning on the first message.
- troubleshooting.md, the "agent session did not come back" list: add "**Codex's background server is running.**", with `codex app-server daemon stop` and the `daemon_auto_start = true` case. Add "**No message was sent.**" (Codex pins on the first message). Replace the old Codex trust bullet text only if it conflicts.
- README and examples.md: one sentence each on the background server.
- Versions: `0.1.109`; plugin `0.1.53` in all three files.
- `git mv` this plan to `docs/plans/completed/`.

- [ ] **Step 2: Verify**

Run: `dotnet build AgTerm.slnx --no-incremental`, `dotnet test tests/AgTerm.Cli.Tests -m:1` (SkillTextTests), `dotnet test tests/AgTerm.Core.Tests -m:1`, `dotnet test tests/AgTerm.E2E -m:1`, and `dotnet build src/AgTerm.App -p:Platform=x64`.
Expected: no warnings, all PASS.

- [ ] **Step 3: Live check with a logged-in Codex**

Publish, turn the setting off and on, run `codex app-server daemon stop`, run `codex` in a new pane and send one message. Check `agtermctl tree --json`: that pane has `codex --no-daemon resume <id>`. `/exit` clears it. Run again, restart agterm-win: the session is back.

- [ ] **Step 4: Commit**

`git commit -m "Document Codex resume without the shared app-server; bump to 0.1.109, plugin 0.1.53"`
