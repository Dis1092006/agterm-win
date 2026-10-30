<!-- agterm-skill -->

# Troubleshooting agterm-win and reporting problems

Two jobs: (1) diagnose a problem from inside an agterm-win session, (2) help the user file it on the repo
as a GitHub issue (a bug, a feature request or a question) — safely, never posting without approval.

## Diagnosing from inside a session

You are inside agterm-win (`AGTERM_ENABLED=1`). Use:

- **Live state** — `agtermctl tree --json`, `agtermctl window list --json`.
- **Keymap problems** — `agtermctl keymap reload` prints the parse-diagnostic count (`ok` = clean). Open **Help > Keyboard Shortcuts** or press **Ctrl+Shift+F1** to read effective bindings, conflicts and diagnostics. Search includes names, groups, chords and diagnostic text. The view updates after reload. Ctrl+Shift+F1 is fixed and is not in `keymap.conf`. The file is read as UTF-8; a UTF-16 file (for example one written by Windows PowerShell 5.1 `Out-File`) gives a single `could not read keymap.conf: …` diagnostic.
- **Settings** - `agtermctl config reload` re-reads `settings.json` and always prints `ok`. A failed
  reload shows the "Config reload failed" bar in the window. There is no ghostty config in agterm-win.
  `restoreRunningCommand` is read when a window opens, so a change to it needs a restart of agterm-win.
- **Logs** — agterm-win writes no log file. It writes diagnostics through `System.Diagnostics.Trace`
  (OutputDebugString); read them with a debug-output viewer such as Sysinternals DebugView. Traced:
  keymap chord conflicts (`keymap: …`), notification registration and show failures, unsupported
  notification sounds, `Settings reload failed: …`, and WebView2 process failures. Failures the user
  must see go to the failure bar (InfoBar) in the window: "Terminal failed to start", "Config reload
  failed", "Command failed".
- **Files** — keymap `~\.agterm-win\keymap.conf` (`<configDirectory>\keymap.conf` when `settings.json`
  sets `configDirectory`); settings `~\.agterm-win\settings.json`; window state
  `~\.agterm-win\windows\<window id>.json`; socket path in `$AGTERM_SOCKET`.

### "Custom action does nothing"

Causes, in order: a parse error (see the diagnostics); the chord conflicts with a built-in or another
custom command and was dropped to palette-only (it still runs from the custom command palette,
`Ctrl+Shift+O`); a reserved chord (`ctrl+tab` with any modifiers, `ctrl+1`/`ctrl+2`); a modifier-less key
(the line becomes palette-only — a custom chord needs a modifier); `cmd` and `ctrl` both mean Ctrl on
Windows, so `cmd+k` and `ctrl+k` are the same chord; it fires only while a terminal pane has keyboard
focus, not from a palette or the inline rename box; it runs in `pwsh.exe -NoLogo -NoProfile
-NonInteractive -Command` (or `%COMSPEC% /d /c` when `pwsh.exe` is not on PATH), so profile aliases and
functions are missing and PATH is the one agterm-win started with — use full paths. A non-zero exit posts
no failure bar; only a missing session directory posts "Command failed". Reload after edits:
`agtermctl keymap reload`.

### "An overlay or --command session opens then instantly closes"

The program exited immediately. `session overlay open` runs its command through `%COMSPEC% /c`; check
`agtermctl session overlay result --json` — `exitCode: 9009` is cmd's "is not recognized" (command not
found). `session new --command` and `session scratch --command` start the program directly, with no
shell; a program that is not found shows the "Terminal failed to start" bar with
`CreateProcessW failed for: <command>`. All three use the environment agterm-win started with, so a PATH
change made after agterm-win started is not seen until it restarts. Fix: give a full path
(`C:\Tools\mytool.exe`) or wrap the command in a shell (`pwsh -c 'mytool'`). Any OTHER exit code just means
the program ran and exited on its own — the overlay/session closes when its command finishes, by design.

Copy and paste chords are handled in the terminal pane, after the keymap, so a user `map` on the same
chord wins.

Ctrl+C, Ctrl+Shift+C and Ctrl+Insert copy the selection. With no selection the key goes to the program,
so Ctrl+C still sends an interrupt (0x03). Ctrl+V, Ctrl+Shift+V and Shift+Insert paste the clipboard
text. A clipboard with no text pastes nothing. Right-click pastes too, unless `settings.json` sets
`"rightClickPaste": false`. There is no select-all chord; use `agtermctl session select-all`.

A mouse selection goes to agterm-win's own selection buffer, and a middle-click pastes it (multi-line
text asks first when the program has bracketed paste off). The Windows clipboard changes only with
Ctrl+C or when `settings.json` sets `"copyOnSelect": "clipboard"`; `"off"` keeps selections nowhere.
`agtermctl session select-all` also fills the selection buffer, and the Windows clipboard too in
`"clipboard"` mode.

### "My session restore override didn't fire"

You set `session restore` but the pane came back as a plain shell (or re-ran the old captured command).
Check, in order:

- **The "Restore running commands on restart" setting is off.** The override obeys the same master switch
  as the rest of restore; a `set`/`--none` while it is off succeeds but nothing runs on relaunch (the
  response says so in `result.text`). Set `"restoreRunningCommand": true` in
  `~\.agterm-win\settings.json` and restart agterm-win; the setting is read when a window opens.
- **The pane resolved to the scratch, or you pinned `--pane right` on a session with no split.** Both are
  rejected at set time (`the scratch terminal is never restored` / `session has no split`), so nothing was
  pinned — re-read the command's output.
- **It already fired once this launch.** The override is consumed once per launch: after it runs, a second
  surface for the same pane in the SAME session gets a plain shell. It is still pinned — `tree` reports
  `restoreCommand` — and fires again on the NEXT restart.
- **The split was hidden at quit.** A hidden split is not restored at all, so its override describes a pane
  that no longer exists: the pin is DROPPED on that launch (`tree` stops reporting `splitRestoreCommand`)
  rather than left to fire into a later manual `Alt+D` split. Show the split before quitting, and re-pin
  after a launch that dropped it. The split pin is also dropped when the split pane's shell exits and the
  split closes.
- **You reopened a closed session or a closed window, not relaunched the app.** The override fires only on
  an app-launch restore — reopening a recently closed session and reopening a closed window do NOT arm it.
  Quit and relaunch agterm-win to see it fire.

Confirm what is pinned from `tree --json`: the node's `restoreCommand` (main pane) / `splitRestoreCommand`
(split pane) reports the persisted value, which survives after the override fires, so a read at any point
shows the truth.

### "notify says ok but no notification appears"

Check the `notificationsEnabled` field in `~\.agterm-win\settings.json` first (apply a change with
`agtermctl config reload`). With it `false`, `notify` succeeds and the unseen badge still rises, but no
banner is posted — the command answers `ok` with
`result.text` = `badge updated, but "Show notification banners" is off, so no banner was posted`
(a delivered notification carries no `result.text`). agterm-win posts banners as Windows app
notifications; when it cannot register or show one (for example when it runs elevated), it shows the
notification in a bar inside the window instead. Windows notification settings and Do not disturb can
also hide a banner that was posted.

To separate "never posted" from "posted but not shown": `tree --json` shows a rising `unseen` on the
target session whenever the command reached the notification path, and Trace (see Logs above) records
notification registration and show failures.

### "The agent-status glyph updates the wrong session"

One session's glyph blinks/changes while the work is happening in a DIFFERENT session — typically when
the agents run inside tmux (or a tmux-backed session manager like agent-deck). Cause: the working
process inherited another session's `AGTERM_SESSION_ID`, and the script that sets the status passes
that id as `--target`. The usual carrier is a long-lived daemon started from inside an agterm-win
session — a tmux server captures the spawning shell's `AGTERM_*` into its GLOBAL environment
(`tmux show-environment -g | grep AGTERM`), and every pane created on that server inherits it, no
matter which client attaches. Diagnose: in the shell that runs the agent, compare
`echo "$AGTERM_SESSION_ID"` with the session's `id` in `agtermctl tree --json` — if the id is not the
session the process lives in, it leaked. Fix a poisoned tmux server without restarting it:
`for v in AGTERM_ENABLED AGTERM_PANE AGTERM_PANE_ID AGTERM_SESSION_ID AGTERM_SOCKET AGTERM_WINDOW_ID AGTERM_WORKSPACE_ID; do tmux set-environment -g -r "$v"; done`,
then restart the affected panes/processes (a respawn is enough; existing processes keep their
inherited copy). Prevent it: start daemons and session managers with the variables scrubbed
(`env -u AGTERM_SESSION_ID … <cmd>`, full list in SKILL.md), or from a shell outside agterm-win.

## Reporting: decide bug vs unsupported FIRST

- A **supported** thing misbehaves (a documented command/feature does the wrong thing, a crash, a parse
  bug) → a GitHub **issue** filed as a bug.
- The user wants something **not supported**, or it is a question / idea / "can it do X" → a GitHub
  **issue** that says in its title that it is a feature request or a question. Do NOT file a feature
  request as a bug. The repository has no Discussions.

## Hard rules for filing

1. **Never run any `gh` command without the user's explicit approval in this conversation.** Drafting
   is fine; posting needs a clear go-ahead ("post it").
2. **Check tooling first** — `gh auth status`. If `gh` is missing or not logged in, do NOT install or
   authenticate it. Give the user the prefilled content plus the URL to paste it into:
   - issue: <https://github.com/Dis1092006/agterm-win/issues/new>
3. **Draft first.** Show the user the full title and body, and get explicit approval before any `gh`.
4. **Scrub sensitive content** before showing or posting: API tokens/keys, passwords, internal
   hostnames/IPs, usernames embedded in absolute paths (replace with `~` or `<user>`), private repo
   names, and the contents of a selection / `session.copy` / clipboard. When unsure, ask.
5. **Gather the repro facts yourself** where you can: the build version (`agtermctl --version` prints the
   version and commit of the `agtermctl.exe` published next to `agterm-win.exe`), `agtermctl tree --json`
   shape, a scrubbed `keymap.conf` excerpt, a scrubbed DebugView (Trace) excerpt, and the text of any
   failure bar.

## Issue template (bug)

```
Title: <short, specific>

What happened: <one or two sentences>
Expected vs actual: <…>
Steps to reproduce:
1. …
2. …
Environment: agterm-win <version>, Windows <version>
Logs: <scrubbed DebugView (Trace) excerpt and failure bar text, if any>
Config: <scrubbed keymap.conf lines, if keymap-related>
```

File it (only after approval) with `--body-file -` so a multi-line body is not mangled by quoting:

```bash
gh issue create -R Dis1092006/agterm-win --title "<title>" --body-file - <<'EOF'
<body>
EOF
```

## Feature request / question

```bash
gh issue create -R Dis1092006/agterm-win --title "Feature request: <title>" --body-file - <<'EOF'
<body>
EOF
```

Use a `Feature request:` or `Question:` title prefix. Same draft-first, scrub, and explicit-approval
rules apply.
