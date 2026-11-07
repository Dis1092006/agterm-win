# Agent session resume setting

## Goal

After agterm-win restarts, a pane that ran Claude Code, GitHub Copilot CLI or Codex reopens the same agent session. The user turns this on with one setting. No repository clone and no script run by hand.

Today this needs a cookbook recipe per agent, run from a clone, plus the separate `restoreRunningCommand` setting. When either is missing, nothing tells the user.

## Decisions

- A Windows-only setting `resumeAgentSessions` in Settings > General, shown nested under "Restore running commands on restart". It is disabled while that setting is off, the same way `autoFollowStayOnActive` depends on the auto-follow timeout (`SettingsEditor.IsEnabled`).
- One hook handler for all agents: a Windows-only CLI subcommand `agtermctl agent-hook resume <agent> <start|end>`. It reads the hook payload from stdin. agtermctl is AOT: about 50 ms warm, against about 0.5-1 s for powershell.exe.
- The hook command line in each agent's config never changes between app updates. Codex asks the user to trust a hook again when its command changes.
- `cookbook/claude-session-resume` and `cookbook/copilot-session-resume` are removed. Turning the setting on replaces what they installed.
- The agent status hooks (*Install Agent Status Hooks...*) are not changed here.

## Agents

| | Claude Code | Copilot CLI | Codex |
|---|---|---|---|
| Config | `~\.claude\settings.json` (`CLAUDE_CONFIG_DIR`) | `hooks\*.json` under `~\.copilot` (`COPILOT_HOME`) | `hooks.json` under `~\.codex` (`CODEX_HOME`) |
| How we write it | merge our entries, backup | our own file | merge our entries, backup |
| Start event | `SessionStart` (startup, resume, clear, compact) | `sessionStart`, at the first prompt | `SessionStart` (startup, resume, clear, compact, fork), at launch |
| Id field | `session_id` | `sessionId` | `session_id` |
| Pin | `claude --resume <id>` | `copilot --resume=<id>` | `codex resume <id>` |
| Clear on end reason | `prompt_input_exit`, `logout` | `user_exit`, `complete` | any (`other` is the only value) |
| Ignore | - | payload with `agentId` or `parentAgentId` | - |
| Source of the facts | the current recipe, in use | live probe, 1.0.105 | live probe, 0.163.0 (start only, see Risks) |

The end handler clears a pin only when the pane's pin is still the command for this session id. On an app quit the clear is refused anyway: `LiveApplicationHost` stops dispatching a window's control commands before it shuts down the panes.

## Parts

### 1. Decision logic (AgTerm.Core)

`AgentResumeProfile` holds one row of the table above. `AgentResumeHook.Decide(profile, event, payload)` returns `Pin(command)`, `ClearIfPinned(command)` or `Ignore`. It is pure, and it checks that the id is UUID-shaped, because the pin is typed into a shell.

Tests: real payloads recorded from each CLI, every reason value, a bad id, subagent payloads, an unknown agent.

### 2. `agtermctl agent-hook resume <agent> <start|end>` (AgTerm.Cli)

- Reads `AGTERM_SESSION_ID`, `AGTERM_PANE_ID`, `AGTERM_PANE`, `AGTERM_WINDOW_ID` and `AGTERM_SOCKET`, and the payload from stdin.
- Start: `session.restore` with mode `set`.
- End: `tree`, finds the pane's pin (`restoreCommand`, or `splitRestoreCommand` for a right pane, nothing for scratch), and sends `session.restore` with mode `clear` only if the pin matches. This is the same logic as the current Copilot recipe, moved into C#.
- Always exits 0 and prints nothing on success, so a failure never blocks the agent. A failure goes to stderr as `error: <message>`; the wrapper in part 3 sends stderr to a log next to itself, as the status hook wrapper does.
- No wire change: the subcommand only calls `session.restore` and `tree`.
- Goes into a new `cli` section of `parity/windows-only.json`, PLATFORM-NOTES "Windows-only extensions", and the skill.

Tests: CLI tests against the headless host, the same cases as `CopilotSessionResumeRecipeTests` and `ClaudeSessionResumeRecipeTests`, for all three agents.

### 3. Installer (AgTerm.Core/Install)

`AgentResumeInstaller.Apply(home, enabled, toolPath, env)` returns the state per agent: `installed`, `not found` (no config directory), or `failed: <reason>`.

- Writes `~\.config\agterm\agent-resume\agterm-agent-resume.cmd`. It exits at once without `AGTERM_SESSION_ID`, then calls the tool path saved at install time. This file can change with each install. The hook command lines point to it and do not change.
- Claude: adds `SessionStart` and `SessionEnd` entries to settings.json, keeps every other entry, backs up the file before a change. Removes entries that run `claude-resume-hook.ps1` (the old recipe).
- Copilot: writes `hooks\agterm-win-resume.json`. Deletes `hooks\copilot-session-resume.json` (the old recipe).
- Codex: adds `SessionStart` and `SessionEnd` entries to `hooks.json`, keeps other entries, backs up before a change.
- Off: removes our entries and our files, and leaves everything else.
- Old recipe scripts in `~\.config\agterm\claude-restore` and `copilot-restore` are deleted.

Tests: temp directories, the same style as `AgentHooksInstallerTests`. Covered: fresh install, a second run changes nothing, other hooks are kept, migration from each old recipe, uninstall, missing agents.

### 4. Setting and UI (AgTerm.Core, AgTerm.App)

- `AppSettings.ResumeAgentSessions` (`resumeAgentSessions`, bool, off when absent). Added to `parity/windows-only.json` settings.
- Settings row "Resume agent sessions after restart", nested under the restore row, disabled while restore is off. `SettingField` gets a parent key for the indent and the enabled check.
- The hint under the row shows the result per agent, for example: "Claude Code, Codex: on. Copilot CLI: not found." When restore is off: "Turn on Restore running commands on restart first."
- For Codex the hint adds: "Codex asks you to trust the new hooks the next time it starts."
- Turning the setting on or off runs the installer at once. While the setting is on, each app launch runs it again. That repairs removed hooks, picks up an agent installed later and updates the tool path in the wrapper.
- A failure shows the existing failure InfoBar.

### 5. Cleanup and docs

- Delete `cookbook/claude-session-resume`, `cookbook/copilot-session-resume`, `ClaudeSessionResumeRecipeTests` and `CopilotSessionResumeRecipeTests`.
- PLATFORM-NOTES: replace the two recipe bullets with the setting. Note that upstream's claude and codex resume recipes (zsh wrappers) are covered by the setting on Windows.
- `examples.md`: replace the two recipe paragraphs with the setting, and keep one short example of pinning a command from any agent's hook with `agtermctl session restore`.
- README and `CONTRIBUTING.md:112`.
- App version, and the plugin version in the three manifests (skill change).

## PRs

1. Parts 1 and 2: decision logic and the CLI subcommand. Nothing uses it yet.
2. Parts 3, 4 and 5: installer, setting, UI, recipe removal.

## Risks

- **Codex end hook not measured.** Codex is not logged in on this machine, so the probe got 401 and no `SessionEnd`. Must check with a logged-in Codex: `SessionEnd` on `/exit`, and that a cold agtermctl start (2.3 s measured once) fits Codex's 3 s `SessionEnd` limit.
- **Codex trust.** Untrusted hooks are skipped silently. Codex shows a review screen at the next interactive start. If the user declines, resume does not work for Codex, and we cannot see that.
- **A nested agent run in the same pane** (`copilot -p`, `codex exec`) pins and clears its own session, so the pane loses the outer session's pin. Neither CLI marks a non-interactive run in the payload.
- **Copilot pins at the first prompt only.** A Copilot session closed before any prompt is not restored.
