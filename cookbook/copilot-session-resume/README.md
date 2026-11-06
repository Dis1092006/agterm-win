# GitHub Copilot CLI session resume (Windows)

After agterm-win restarts, each pane that was running GitHub Copilot CLI reopens its own Copilot session. A pane you had left Copilot in comes back as a plain shell.

## What it does

- **Copilot `sessionStart` hook.** Pins `copilot --resume=<session id>` as the pane's restore command (`agtermctl session restore`). Copilot fires this hook at the first prompt of a session, not when it starts. After `/clear` or `/resume` the next prompt pins the new session.
- **Copilot `sessionEnd` hook.** Clears the pin when the session ends with reason `user_exit` or `complete`. `/exit`, `/clear`, Ctrl+C twice and Ctrl+D twice end it with `user_exit`; a `copilot -p` run ends with `complete`. The hook clears only a pin that names the ending session, so a late end of the session before `/clear` does not drop the new pin, and neither does a pin set by someone else. Other reasons (`error`, `abort`, `timeout`) keep the pin.
- **App quit.** Closing a pane also ends Copilot with `user_exit`: Copilot cannot tell it apart from `/exit`. On quit, agterm-win stops the control pipe for a window before it closes the panes, so the clear is refused and the pin survives the restart. This was measured against Copilot CLI 1.0.105 on a pseudoconsole closed the way agterm-win closes a pane.
- **Safety.** The pin is shell code typed into the shell on every launch, so only a UUID-shaped id goes into it.
- **Scope.** Outside agterm-win (`AGTERM_SESSION_ID` unset) the hook does nothing. Subagent events are ignored.

Upstream agterm has no Copilot recipe. Its [claude-session-resume](https://github.com/umputun/agterm/tree/master/cookbook/claude-session-resume) recipe wraps the agent in zsh/fish and relies on the command a pane was running at quit, which agterm-win does not capture (see `docs/PLATFORM-NOTES.md`). Here the pin comes from Copilot's own hooks, as in `cookbook/claude-session-resume`.

## Requirements

- agterm-win with `agtermctl` on `PATH`: *Help > Install Command Line Tool…* puts it there. Alternatively, set `AGTERMCTL` to the path of `agtermctl.exe`.
- **Restore running commands on restart** turned on: `"restoreRunningCommand": true` in `~\.agterm-win\settings.json`, read when a window opens. Without it agterm-win keeps the pin and opens a plain shell.
- GitHub Copilot CLI. Windows PowerShell 5.1 runs both the hook and the installer, so pwsh is not needed.

## Setup

From a clone of this repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\cookbook\copilot-session-resume\install.ps1
```

The installer:

- copies `copilot-resume-hook.ps1` to `~\.config\agterm\copilot-restore\`;
- writes `hooks\copilot-session-resume.json` under `~\.copilot`, or under `COPILOT_HOME` when it is set. Copilot reads every hooks file in that folder, so the file sits beside the agent-status hooks agterm-win installs and does not change them.

Running it again changes nothing. `-HooksPath` and `-InstallDirectory` override both locations.

Copilot reads hooks when it starts. Restart running Copilot sessions to pick up the change.

To remove the recipe:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\cookbook\copilot-session-resume\install.ps1 -Uninstall
```

To drop one pane's pin by hand: `agtermctl session restore --clear --target <session id>`.

## Check it

1. Run `copilot` in a pane and send a prompt.
2. Run `agtermctl tree --json` and find that session: `restoreCommand` is `copilot --resume=<id>`.
3. Quit and start agterm-win. The pane reopens that session.
4. Send a prompt, then type `/exit`. `restoreCommand` is gone, and the next restart opens a plain shell.

## Limits

- **A resumed session you leave without a prompt keeps its pin.** Copilot fires no hook for a session that gets no prompt, so `/exit` right after a restart does not clear the pin, and the next restart resumes the same session again. Send a prompt first, or clear the pin by hand.
- **`/clear` drops the pin until the next prompt.** The old session ends with `user_exit` and clears its pin; the new session pins itself at its first prompt. A quit in between restores a plain shell.
- **One session per pane.** A split pane has its own pin (`AGTERM_PANE_ID`). A second Copilot in the same pane, for example a `copilot -p` run from a tool call, pins its own session and then clears it, so the pane loses the pin of the first one until that one starts a new session.
- **The scratch terminal is never restored**, so Copilot there pins nothing.
- **It depends on Copilot CLI's hook payloads:** `sessionId`, `reason`, `agentId` and `parentAgentId`. A change there can require a recipe update.
