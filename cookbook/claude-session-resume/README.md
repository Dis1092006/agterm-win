# Claude Code session resume (Windows)

After agterm-win restarts, each pane that was running Claude Code reopens its own conversation. A pane you had left Claude in comes back as a plain shell.

## What it does

- **Claude Code `SessionStart` hook.** Pins `claude --resume <live id>` as the pane's restore command (`agtermctl session restore`). The hook fires on startup, `--resume`/`/resume`, `/clear` and compaction, so the pin always names the conversation the pane is in now.
- **Claude Code `SessionEnd` hook.** Clears the pin when you leave Claude yourself: `/exit`, Ctrl+D (reason `prompt_input_exit`) or `/logout`. Closing the pane, which an app quit does to every pane, ends Claude with reason `other` and keeps the pin. This was measured against Claude Code 2.1.310 on a real pseudoconsole closed the way agterm-win closes a pane.
- **Safety.** The pin is shell code typed into the shell on every launch, so only a UUID-shaped id goes into it.
- **Scope.** Outside agterm-win (`AGTERM_SESSION_ID` unset) the hook does nothing.

Upstream's [claude-session-resume](https://github.com/umputun/agterm/tree/master/cookbook/claude-session-resume) recipe solves this with a zsh/fish wrapper around `claude`, because upstream replays the command a pane was running at quit. agterm-win does not capture that command (see `docs/PLATFORM-NOTES.md`), so here the pin comes from Claude's own hooks.

## Requirements

- agterm-win with `agtermctl` on `PATH`: *Help > Install Command Line Tool…* puts it there. Alternatively, set `AGTERMCTL` to the path of `agtermctl.exe`.
- **Restore running commands on restart** turned on: `"restoreRunningCommand": true` in `~\.agterm-win\settings.json`, read when a window opens. Without it agterm-win keeps the pin and opens a plain shell.
- Claude Code. Windows PowerShell 5.1 runs both the hook and the installer, so pwsh is not needed.

## Setup

From a clone of this repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\cookbook\claude-session-resume\install.ps1
```

The installer:

- copies `claude-resume-hook.ps1` to `~\.config\agterm\claude-restore\`;
- registers the hook for `SessionStart` and `SessionEnd` in `~\.claude\settings.json`, or under `CLAUDE_CONFIG_DIR` when it is set.

It keeps every other setting and hook, and backs the file up as `settings.json.<timestamp>.bak` before it changes it. Running it again changes nothing. It replaces a hook from the earlier pin-only version, `pin-claude-resume.ps1`, but leaves that file on disk. `-SettingsPath` and `-InstallDirectory` override both locations.

Claude Code reads hooks when a session starts. Restart running Claude sessions, or open `/hooks` in them, to pick up the change.

To remove the recipe:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\cookbook\claude-session-resume\install.ps1 -Uninstall
```

To drop one pane's pin by hand: `agtermctl session restore --clear --target <session id>`.

## Check it

1. Run `claude` in a pane and say something.
2. Run `agtermctl tree --json` and find that session: `restoreCommand` is `claude --resume <id>`.
3. Quit and start agterm-win. The pane reopens that conversation.
4. Type `/exit`. `restoreCommand` is gone, and the next restart opens a plain shell.

## Limits

- **One conversation per pane.** A split pane has its own pin (`AGTERM_PANE_ID`). Two Claude processes in the same pane overwrite each other's pin, and the last one to start wins.
- **Claude cleans up old transcripts** (`cleanupPeriodDays`, 30 days by default). A pane pinned to a deleted conversation shows Claude's error on restart. Then run `/exit` or clear the pin.
- **A crash or a kill keeps the pin.** Claude ends with reason `other` there too, so the pane reopens the conversation. That is usually what you want after a crash.
- **It depends on Claude Code's hook payloads:** `session_id`, `hook_event_name` and `reason`. A change there can require a recipe update.
