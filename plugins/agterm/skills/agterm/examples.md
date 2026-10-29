# agterm control recipes

Worked `agtermctl` examples. See `reference.md` for exact flags and return shapes. All assume
`agtermctl` is on PATH and you are inside an agterm session (`AGTERM_ENABLED=1`).

## Inspect the current state

```bash
agtermctl tree --json        # workspaces -> sessions, active/split/overlay/scratch/flagged flags, surface ids
agtermctl window list --json # windows, with open/active flags
```

agterm-win does not report what a pane is running: the tree's `foreground` / `splitForeground` fields are
never set.

## Reset the restore-on-restart commands

agterm-win does not capture a pane's foreground command at quit. `restore clear` drops any captured command
that the window state files still hold from an older snapshot. It does not clear `session restore` pins
or the command of a `session new --command` session:

```bash
agtermctl restore clear
```

## Pin what a pane restores (per-session override)

`session restore` pins a shell line that a pane re-runs on the NEXT launch. It is written now and consumed
at the next launch (it never touches the running session), and it is sticky — it fires again on every
restart until cleared. Read it back on `tree` as the node's `restoreCommand` / `splitRestoreCommand`.

The line is typed into the pane's shell (PowerShell by default) at launch only while
`restoreRunningCommand` is `true` in `~/.agterm-win/settings.json`. With the setting off, the pin is still
saved and the reply says `saved, but "Restore running commands on restart" is off, so the override will not run`.

```bash
agtermctl session restore "claude --resume abc123" --target "$AGTERM_SESSION_ID"  # pin a shell line
agtermctl session restore --none  --target "$AGTERM_SESSION_ID"   # pin nothing: restore a plain shell
agtermctl session restore --clear --target "$AGTERM_SESSION_ID"   # drop the override; normal restore rules apply
agtermctl session restore "npm run dev" --target "$AGTERM_SESSION_ID" --pane-id "$AGTERM_PANE_ID"  # a split pane by its live slot
```

The pinned value is SHELL CODE: it persists in the window's state file (`windows\<id>.json`), is readable
via `tree`, and may enter shell history when it runs — so it must not carry secrets, and only
safely-interpolated values (a UUID-shaped session id) belong in it.

## Keep a forking agent session reattaching across restarts (a SessionStart hook)

`claude --resume <id> --fork-session` mints a NEW claude session on every agterm restart, so restoring it
verbatim never reattaches the session you were in. Fix it by rewriting the override to the LIVE session id
on every start, from a Claude Code `SessionStart` hook — it runs inside the session's shell, where
`$AGTERM_SESSION_ID` and `$AGTERM_PANE_ID` are exported:

```bash
# in the SessionStart hook (the selector is --target; there is no --session).
# the hook's stdin JSON carries the live id as `session_id` — there is NO $CLAUDE_SESSION_ID variable.
sid=$(jq -r '.session_id // empty')
[ -n "$sid" ] || exit 0   # no id: leave the existing pin alone rather than pinning a broken line
agtermctl session restore "claude --resume $sid" \
    --target "$AGTERM_SESSION_ID" --pane-id "$AGTERM_PANE_ID"
```

Because the hook rewrites the override on every start, it always tracks the live child, so the next
restart reattaches instead of forking. Ownership flips to whoever sets it: write it once by hand and forget,
and it stays pinned to a STALE id — that is why this is a hook-driven override, not a set-once setting.

Read the id from the hook's stdin (`$CLAUDE_CODE_SESSION_ID` is exported in the session's environment as
well, but the stdin `session_id` is what the hook is handed). GUARD against an empty value before pinning:
a pin is sticky and persisted, so `claude --resume ` with an empty argument would be re-typed on every
launch until cleared. The id is UUID-shaped and safe to interpolate; never build the pinned line from
untrusted or secret values.

On Windows, `cookbook/claude-session-resume` in the agterm-win repository does this in PowerShell. It
also clears the pin from a `SessionEnd` hook when the user leaves Claude (`reason` `prompt_input_exit`
or `logout`), so that pane restarts as a plain shell. A pane closed by an app quit ends Claude with
`reason` `other`, which keeps the pin. `install.ps1` registers both hooks in `~/.claude/settings.json`.

## Create a session and type into it

`session new` returns the new id and focuses the session. Capture the id, then type. The session is
realized eagerly, so no `--select` is needed.

```bash
sid=$(agtermctl session new --cwd "$HOME/project" --json | jq -r '.result.id')
agtermctl session type "git status" --target "$sid"
agtermctl session type $'\n' --target "$sid"     # send Return (or include it in the text)
agtermctl session split on --target "$sid"                    # open a split first
agtermctl session type $'ls\n' --target "$sid" --pane right   # then type into the split pane
```

Typing goes to the session's main (left) pane by default; `--pane right` targets the split pane and
errors with `session has no split pane` when there is none. In a custom keymap command, the `{AGT_PANE}`
token expands to the pane the shortcut fired from, so `session type --pane {AGT_PANE}` types back into it.

Run a command AS the session's process (closes when it exits, no echoed command line):

```bash
agtermctl session new --command "ssh host -p 22"     # an .exe on PATH: argv-split (quotes respected), no shell, no echo
agtermctl session new --command "pwsh -NoProfile -Command 'npm run dev'"   # only .exe is found on PATH, so wrap a .cmd/.ps1 shim (npm) in a shell
agtermctl session new --command "pwsh -NoProfile -Command 'make test'" --wait   # HOLD the pane open after the command exits so its final output stays readable; --wait needs --command
```

A program that cannot start is reported in the window's failure bar.

Create a session pre-named (label set at creation, no follow-up rename):

```bash
agtermctl session new --name "myhost" --command "ssh user@host"
```

Open a session in a named workspace, creating the workspace once and reusing it after (idempotent — no
duplicate "servers" workspace on repeated calls):

```bash
agtermctl session new --workspace-name servers --create-workspace --name "myhost" --command "ssh user@host"
```

Create a session in the background — do NOT switch to it. The current selection and keyboard focus stay
put; the new session appears in the sidebar but is not `active` in `tree` (the read-back). It is the
inverse of the overlay's `--follow`:

```bash
agtermctl session new --cwd "$HOME/project" --no-select
```

## Duplicate a session (a second shell in the same directory)

`session duplicate` creates a fresh session — a plain shell — in the SAME workspace as the target,
directly AFTER it, rooted at the target's focused-pane cwd, then selects + focuses it and prints the new
id. ONLY the directory carries over: no custom name, `--command`, split, scratch, status, flag, font size,
or background. It is `session new --cwd <source cwd> --after <source>` in one atomic round-trip, and the
control half of the sidebar row's **Duplicate Session** context-menu item.

```bash
agtermctl session duplicate                                    # a second shell beside the current session, same cwd
sid=$(agtermctl session duplicate --target 3f2a --json | jq -r '.result.id')
agtermctl session type $'npm run dev\n' --target "$sid"        # run something in the copy, source left alone
```

Read it back off `tree` — there is no new tree field: the duplicate's node appears directly after its
source, carrying the source's focused-pane cwd. That equals the source node's `tree.cwd` for a non-split
session (and a split focused on its primary pane); for a split focused off its primary the source node's
`tree.cwd` reports the primary while the duplicate carries the focused pane's directory, so compare against
the pane you duplicated from.

## Build a small layout

```bash
ws=$(agtermctl workspace new "build" --json | jq -r '.result.id')
a=$(agtermctl session new --workspace "$ws" --cwd "$HOME/proj" --json | jq -r '.result.id')
agtermctl session rename "server" --target "$a"
agtermctl session split on --target "$a"          # second shell side by side
agtermctl session resize --split-ratio 0.7 --target "$a"   # left pane gets 70% (prints 0.700)
b=$(agtermctl session new --workspace "$ws" --json | jq -r '.result.id')
agtermctl session rename "logs" --target "$b"
```

## Place a session next to another instead of appending

`session new` appends at the end of the workspace by default. `--after`/`--before` place it directly
after/before an anchor session in one round-trip — no `move --to up` walk. The anchor is a session
address (id / unique prefix / `active`) and carries its own workspace, so it names the destination
itself (mutually exclusive with `--workspace`/`--workspace-name`).

```bash
# the headline case: create right after the current session
agtermctl session new --after active

# create right before a specific session (by unique prefix)
agtermctl session new --before 3f2a --name "notes"
```

`session move` gains the same placement mode. Relocate a session and slot it after/before an anchor —
wherever the anchor lives, even in another workspace — in one shot, with no visible row-by-row shuffle:

```bash
# move the current session to sit right after another (cross-workspace if the anchor is elsewhere)
agtermctl session move --after 3f2a --target active
agtermctl session move --before "$logs" --target "$server"

# move several sessions together as one ordered block
agtermctl session move "$ws" --target "$server" --target "$logs"
agtermctl session move --after "$anchor" --target "$server" --target "$logs"

# close several sessions with one grace-period undo
agtermctl session close --target "$server" --target "$logs"
```

`--after`/`--before` are mutually exclusive with each other, with `--to`, and with a destination
workspace — the anchor already picks the workspace. Repeated `--target` is only for workspace and
after/before placement, not `--to up|down|top|bottom`.

## Resize the split divider from a keybinding

The divider is otherwise mouse-drag only — there is no built-in resize action, so bind keys to the CLI
with `command "<name>" <chord> <shell…>` custom actions in `keymap.conf` (then `agtermctl keymap reload`):

```conf
# grow/shrink the left pane by 5% per press; ctrl+opt+0 (Ctrl+Alt+0) resets to an even split
command "grow left pane"  ctrl+opt+l agtermctl session resize --grow-left 0.05
command "grow right pane" ctrl+opt+h agtermctl session resize --grow-right 0.05
command "even split"      ctrl+opt+0 agtermctl session resize --split-ratio 0.5
```

`opt` is Alt. A `cmd+` chord means Ctrl on Windows, so `cmd+ctrl+l` would be bare Ctrl+L, which the shell
needs. The command line runs in PowerShell 7 when `pwsh.exe` is on PATH, else in `cmd.exe`, so `agtermctl`
must be on the app's PATH.

`--split-ratio` is absolute (0..1); `--grow-left`/`--grow-right` are relative nudges. All clamp to
0.05..0.95 and print the applied fraction.

## Run a program in a blocking overlay and read its status

`--block` waits for the program to exit and makes agtermctl exit with the program's status. The
program renders normally in the overlay; read its OUTPUT from the program's own output file.

Pass `--target "$AGTERM_SESSION_ID"` so the overlay attaches to YOUR (the calling) session. Without
`--target` it opens on whatever session is currently active — so if the user has moved to another
session or workspace, an agent (e.g. running revdiff) pops a blocking full-pane overlay on the WRONG
session. Always target your own session for these recipes.

The overlay runs its command line through `cmd /c`, in the target session's directory unless `--cwd`
says otherwise. `cmd` does not understand Git Bash paths such as `/tmp/notes.md` inside the command
string, so write the output next to the working directory:

```bash
agtermctl session overlay open "revdiff HEAD~3 --output notes.md" --cwd "$PWD" --target "$AGTERM_SESSION_ID" --block   # this session
echo "exit status: $?"
cat notes.md
```

Floating panel variant (session stays visible behind it). Like a full overlay it opens in the background
without switching the user; add `--follow` when you want the user pulled to the overlay:

```bash
agtermctl session overlay open "lazygit" --target "$AGTERM_SESSION_ID" --size-percent 70   # cmd /c finds anything on PATH, .cmd shims included
# tint the overlay pane so it stands out from the session behind it:
agtermctl session overlay open "revdiff HEAD~3" --target "$AGTERM_SESSION_ID" --size-percent 80 --background-color "#2a1a3a"
# switch the user to the target as it opens:
agtermctl session overlay open "revdiff HEAD~3" --target "$AGTERM_SESSION_ID" --size-percent 80 --follow
# resize the open overlay in place (the program keeps running): shrink to a floating panel, then back to full
agtermctl session overlay resize --size-percent 60 --target "$AGTERM_SESSION_ID"
agtermctl session overlay resize --full --target "$AGTERM_SESSION_ID"
# ... later
agtermctl session overlay close
```

Manual open + poll for status instead of `--block`:

```bash
agtermctl session overlay open "make test" --target "$AGTERM_SESSION_ID"   # this session
agtermctl session overlay result --json   # errors `overlay still running` until it exits, then result.exitCode
```

## Set a background watermark or color

`session background` stores a backdrop spec on the session: an image or rasterized-text watermark, or a
solid terminal background color. The spec is per session, survives a relaunch, and reads back on `tree` as
the session node's `background`. agterm-win validates and stores the spec but does not draw it: the pane
looks the same with or without it.

```bash
# rasterized text watermark on this session, faint
agtermctl session background text "STAGING" --color '#ff5500' --opacity 0.15 --target "$AGTERM_SESSION_ID"

# an image (PNG/JPEG; the file must exist), scaled to cover the window
agtermctl session background image ~/logo.png --fit cover --opacity 0.2 --target "$AGTERM_SESSION_ID"

# a solid background color
agtermctl session background color '#3a0d0d' --target "$AGTERM_SESSION_ID"

# read it back, then remove it
agtermctl tree --json | jq --arg s "$AGTERM_SESSION_ID" '.result.tree.workspaces[].sessions[] | select(.id == $s) | .background'
agtermctl session background clear --target "$AGTERM_SESSION_ID"
```

`--opacity` is 0.0–1.0; `--fit` is `contain` (default) / `cover` / `stretch` / `none`; `--position` is
`center` (default) or an edge/corner anchor.

## Toggle the scratch terminal

A third per-session full-coverage shell. Hide keeps it alive; `exit` in it recreates on next show.

```bash
agtermctl session scratch on        # show (selects the target)
agtermctl session scratch off       # hide, shell stays alive
agtermctl session scratch toggle
agtermctl session scratch on --command "lazygit"   # run a program instead of a shell (run-once); argv-split, no shell, so an .exe on PATH
```

## Drive the quick terminal

The quick terminal is one app-level detached panel, outside the session tree. Show it, type into it and read it back, with no `--target`/`--pane`. Control `quick show` pins it; its shell stays alive when hidden.

```bash
agtermctl quick show                                 # show and pin the detached panel
agtermctl quick type 'ls -la'$'\n'                   # inject keystrokes (\n runs it)
echo "some payload" | agtermctl quick type --stdin   # pipe stdin in (e.g. a paste helper)
agtermctl quick text --all                           # read its screen + scrollback back
agtermctl tree --json | jq .result.tree.quickVisible   # is it open right now?
```

## Flag a working set and view just the flagged sessions

Flag a few sessions across workspaces, then flip the sidebar to the flat flagged list (each row labeled
`session : workspace`). The flag is durable (persisted per session); `sidebar mode` is per-window.

```bash
agtermctl session flag on --target "$AGTERM_SESSION_ID"   # flag this session
agtermctl session flag on --target a1b2                   # flag another (any workspace)
agtermctl sidebar mode flagged                            # show only the flagged sessions
agtermctl session go --to next                            # in flagged mode, nav steps the flagged set only
agtermctl sidebar mode tree                               # back to the full tree
agtermctl session flag clear                              # unflag everything in the window
```

## Acknowledge a driven session's notifications without stealing focus

An orchestrator relaying a session's output elsewhere (Telegram, another agent) fires `notify` to signal
"your turn", which raises the session's red unseen badge. Nothing normally clears that badge except
visiting the session — which pulls the selection to it. `session seen` clears it in place, so the badge
stays a real attention signal on the sessions a human tends while the driven ones stay clean.

```bash
agtermctl notify "your turn" --target "$SID"             # raises the unseen badge (body is positional)
agtermctl tree --json | jq '.result.tree.workspaces[].sessions[] | {id, unseen}'  # read the counts
agtermctl session seen --target "$SID"                   # clear it, selection/focus unchanged
```

## Focus a single workspace

Collapse the sidebar tree to one workspace's sessions (hiding the others), with the full tree one
command away. Per-window and persisted; orthogonal to `sidebar mode`. While focused, `session go`
navigation is scoped to that workspace's sessions; unfocusing restores stepping over all sessions.

```bash
agtermctl workspace focus on --target "$AGTERM_WORKSPACE_ID"  # zoom to this workspace
agtermctl workspace focus toggle --target a1b2                # flip focus on another workspace
agtermctl workspace focus off                                 # restore the full tree
```

## Expand or collapse the sidebar tree

Open every workspace at once, or collapse all but the active one (the workspace of the active session,
which stays expanded and scrolled into view) to cut clutter. Defaults to the frontmost window; pass
`--window` to target any open window. A no-op in flagged mode.

```bash
agtermctl sidebar expand                                 # expand every workspace (frontmost window)
agtermctl sidebar collapse                               # collapse all but the active workspace
agtermctl sidebar collapse --window "$AGTERM_WINDOW_ID"  # collapse a specific window's sidebar
```

## Build a collapsed workspace and fill it quietly

Collapse or expand ONE workspace by id (the per-workspace pair, unlike `sidebar expand`/`collapse` which
act on all of them). Create a workspace already collapsed with `workspace new --collapsed`, then add
sessions with `session new --no-select` so it never opens or steals the current selection — the recipe
for staging a batch of sessions out of the way. Read the open/closed state back from the tree workspace
node's `collapsed` flag (`true` when collapsed, omitted when expanded).

```bash
ws=$(agtermctl workspace new "batch" --collapsed --json | jq -r '.result.id')
for dir in ~/a ~/b ~/c; do
  agtermctl session new --cwd "$dir" --workspace "$ws" --no-select   # added, but the workspace stays shut
done
agtermctl workspace expand --target "$ws"    # open it when you want to see the staged sessions
agtermctl workspace collapse --target "$ws"  # fold it away again

# toggle: read the flag first, then flip
collapsed=$(agtermctl tree --json | jq -r --arg w "$ws" '.result.tree.workspaces[] | select(.id==$w) | .collapsed // false')
[ "$collapsed" = true ] && agtermctl workspace expand --target "$ws" || agtermctl workspace collapse --target "$ws"
```

## Copy a selection and reuse it

`session copy` returns the selection as text (it does not use the system clipboard). Pipe it onward.

```bash
sel=$(agtermctl session copy --json | jq -r '.result.text')
agtermctl session type "$sel" --target "$other"
```

`session select-all` selects the whole buffer, then `session copy` reads it back (or use `session text --all`):

```bash
agtermctl session select-all --target "$other"
buf=$(agtermctl session copy --target "$other" --json | jq -r '.result.text')
```

`session paste` pastes the system clipboard into a session — the control analogue of Ctrl+V:

```bash
printf 'deploy staging' | clip
agtermctl session paste --target "$other"   # lands at the prompt, not submitted
```

## Read a session's buffer as text

`session text` returns the terminal buffer as plain text in `result.text` — the visible screen by
default, the whole scrollback with `--all`, or the last N lines with `--lines N`. Pipe it into
`grep`/`fzf` to extract URLs, paths, etc.

```bash
agtermctl session text                         # the visible screen of the focused pane
agtermctl session text --lines 50              # the last 50 lines of the buffer
agtermctl session text --pane right            # the split pane (errors if there is no split)
agtermctl session text --pane scratch --all    # the scratch terminal's full buffer, even while it's hidden
# extract every URL from the full scrollback:
agtermctl session text --all --json | jq -r '.result.text' | grep -oE 'https?://[^ ]+'
```

`--pane scratch` reads (and `session type --pane scratch` writes) the session's scratch terminal whether
or not it is on screen, since its shell is kept alive when hidden. Handy for "I ran a deploy in the
scratch, read its output and tell me what broke" without leaving the scratch open:

```bash
agtermctl session scratch on                             # open the scratch once so it exists
agtermctl session type $'./deploy.ps1\n' --pane scratch   # run it in the scratch (even after you hide it)
agtermctl session text --pane scratch --all              # read the result back
```

## Search the terminal scrollback

`session search` opens a search bar over the focused terminal and highlights matches in the live
output. It returns the "N of M" counter; step matches with `--next`/`--prev`, close with `--close`.

```bash
agtermctl session search "error"          # highlight matches, print the counter (e.g. "1 of 7")
agtermctl session search --next           # step to the next match
agtermctl session search --prev           # step back
n=$(agtermctl session search "warn" --json | jq -r '.result.count')   # how many matches
agtermctl session search --close          # close the search bar
```

## Notify the user in a specific session

```bash
agtermctl notify "build finished" --title "CI"                 # active session
agtermctl notify "tests failed" --target "$sid"               # a specific session
```

## Wait for a session status

Subscribe before starting the work, then select the first matching status event. The initial read
subscribes from now, so an old completed state is not mistaken for a new completion.

```bash
export target="$AGTERM_SESSION_ID"
agtermctl events --json --kind status |
  jq --unbuffered -e 'select(.session == env.target and .payload.status == "completed")' |
  head -n 1
```

The pipeline ends after the match. A transport or cursor failure makes `agtermctl events` exit
non-zero; preserve pipeline status in automation that must distinguish a match from a failed stream.

## Relay accepted notifications

The notification event exists even when desktop banners are disabled. Forward each accepted event as
NDJSON to another process without parsing human output:

```bash
agtermctl events --json --kind notify |
  jq --unbuffered -c '{at: .ts, window, workspace, session, title: .payload.title, body: .payload.body}' |
  ./notification-relay
```

Only `agtermctl notify` produces these events. agterm-win does not turn a program's OSC notification
escape into a notification.

## Clean up after sessions close

Lifecycle events follow visible tree membership. A soft close emits `session.closed` immediately, and
an undo later emits `session.created` for the same id. Delay irreversible cleanup if undo matters.

```bash
agtermctl events --json --kind session.closed |
  jq --unbuffered -r '.session' |
  while IFS= read -r sid; do
    ./release-session-resources "$sid"
  done
```

For resumable consumers, save the `run` and `next` fields from raw `events.read` batch responses and
restart with `agtermctl events --run "$run" --after "$next" --json`. The streaming JSON lines are bare
events and do not include the run id. If the command reports `event run changed`, `event cursor
expired`, or `event cursor is ahead of the current sequence`, stop loudly. Rebootstrap only after the
caller accepts that events may have been lost; never replace the cursor automatically.

## Agent status glyph

```bash
agtermctl session status active --blink --target "$AGTERM_SESSION_ID"   # working
agtermctl session status completed --auto-reset --target "$AGTERM_SESSION_ID"  # one-shot done flash
agtermctl session status blocked --sound default --target "$AGTERM_SESSION_ID" # needs input, with a beep
agtermctl session status completed --sound "Windows Notify Calendar" --target "$AGTERM_SESSION_ID" # choose an installed name from Settings
agtermctl session status blocked --color '#ff0000' --target "$AGTERM_SESSION_ID" # per-call red tint (reverts on next status)
agtermctl session status blocked --shape triangle --target "$AGTERM_SESSION_ID"  # per-call silhouette (reverts on next status)
agtermctl session status blocked --pane right --target "$AGTERM_SESSION_ID"     # a split-pane agent tags its pane (see below)
agtermctl session status idle --target "$AGTERM_SESSION_ID"             # clear
```

## Tag the pane that set the status

An agent running in a split or scratch pane sets `--pane` so the status records the pane it came from;
`tree` reports it as the session's `statusPane`. When that pane goes away (the split closes, the scratch
shell exits), agterm-win clears the status, so no glyph is left without a pane behind it. Without `--pane`
the status counts as coming from the main (`left`) pane. agterm-win does not clear a status on a keystroke
and does not move focus into the tagged pane: attention navigation, in the GUI or through
`session go --to next-attention`, only changes the selected session.

```bash
# an agent working in the split pane; a custom keymap command gets it as {AGT_PANE}, else name it
agtermctl session status active --pane right --target "$AGTERM_SESSION_ID"   # working, in the split
agtermctl session status blocked --pane right --target "$AGTERM_SESSION_ID"  # needs input, in the split

# an agent working in the scratch terminal (even while it is hidden)
agtermctl session status blocked --pane scratch --target "$AGTERM_SESSION_ID" # needs input, in the scratch

# read back which pane blocked
agtermctl tree --json | jq -r '.result.tree.workspaces[].sessions[] | select(.status) | "\(.name): \(.status) in \(.statusPane // "left")"'
```

`--pane left` (or omitting it) is the main pane. Feed a keymap command's `{AGT_PANE}` token straight through
(`session status blocked --pane {AGT_PANE}`) to tag the exact pane a shortcut fired from.

The installed agent-status hook also forwards each surface's `$AGTERM_PANE_ID` as `session status --pane-id`,
a stable per-surface token that resolves to the pane's LIVE slot and overrides a stale `--pane` after a split
survivor is promoted into the main pane and then re-split; scripts driving `--pane` directly need not set it.

## Zoom a terminal surface by control id

```bash
# Fill the window with the active terminal surface; call again to leave zoom.
agtermctl surface zoom

# Zoom the active session's right split pane by id, even if the split is hidden.
sid=${AGTERM_SESSION_ID:?}
surface=$(agtermctl tree --json |
  jq -r --arg sid "$sid" '.result.tree.workspaces[].sessions[]
    | select(.id == $sid)
    | .surfaces[]
    | select(.kind == "right")
    | .id')
agtermctl surface zoom show --target "$surface"
agtermctl surface zoom hide --target "$surface"

# Read the current zoom back (the zoomed surface's control id; null when nothing is zoomed).
agtermctl tree --json | jq -r '.result.tree.zoomedSurface'
```

`surface zoom` is not `window zoom`: it does not move/resize the window and must not change split
ratios, sidebar state, focus, or split/scratch visibility. Surface ids come from `tree --json`.

## Watch several sessions at once in a dashboard grid

The dashboard shows several sessions' live output in one view-only grid — for watching several agents or
builds at once. The cell unit is a session+pane: a non-split session is one cell, and a SPLIT session
shows as TWO cells (its left/primary and right/split panes), capped at 9 cells total. No cell takes input:
the keyboard navigates a highlight (arrows), Enter jumps into the highlighted session AND focuses that
exact pane then closes, Esc closes. Open it over the socket with explicit session ids, or with `--mru` to
pull the window's most-recently-used sessions automatically. The most-recently-used grid also has a built-in
opener — **Ctrl+Shift+D** (the `dashboard` action), the title bar's **Dashboard** button, or the command
palette's **Dashboard** toggle it auto-sized (the `dashboard --mru --auto-size` equivalent), so the
recent-sessions view needs no script for the common case.

```bash
# grid of three sessions, cells auto-sized to the grid (shrinking as it grows)
agtermctl dashboard "$a" "$b" "$c" --auto-size

# no ids: fill the grid from the window's most-recently-used sessions (up to 9, fewer if fewer)
agtermctl dashboard --mru --auto-size

# an absolute cell font in points instead of --auto-size (the two are mutually exclusive)
agtermctl dashboard "$a" "$b" --font-size 12

# open in a specific window (default is the frontmost)
agtermctl dashboard "$a" "$b" --window "$AGTERM_WINDOW_ID"

# read back what the open dashboard is showing (all null when none is open); members are pane refs
# (`<id>:left`/`<id>:right`), so a split session shows both its :left and :right cells
agtermctl tree --json | jq '.result.tree | {dashboardMembers, dashboardHighlighted, dashboardFontSize, dashboardFontMode}'

# close it (or press Enter/Esc in the grid)
agtermctl dashboard --close
```

The MRU grid is already on **Ctrl+Shift+D** (the built-in `dashboard` action) — rebind that chord in
`keymap.conf` with `map <chord> dashboard`. To dashboard a FIXED set of explicit ids instead, bind a
`keymap.conf` custom action (then `agtermctl keymap reload`). The command line runs in PowerShell 7 or
`cmd.exe`, so write the session ids into it:

```conf
command "Dashboard build hosts" ctrl+a>d agtermctl dashboard 3f2a 9c1e --auto-size
```

`--mru` is mutually exclusive with explicit ids and `--close`, and errors with `no recent sessions` when
the window has none. The 9-cell cap counts PANES (a split session is two cells), so a set whose panes
exceed 9 keeps the first 9 panes and the response reports the dropped-pane count; ids are deduped. The
dashboard and terminal zoom are mutually exclusive (opening one closes the other). Opening/closing resizes
each pane's pty to/from its cell, so a running program may redraw — view-only means no input, not no
process effect.

## Ask the user to choose from a generated list

Pipe nonblank labels into `pick`. The call blocks until the user chooses or cancels, then prints a bare
JSON result:

```bash
choice=$(
  printf '%s\n' staging production |
    agtermctl pick --prompt "Deploy where?"
)

case $(jq -r '.result' <<<"$choice") in
  picked) jq -r '.id' <<<"$choice" ;;
  custom) jq -r '.query' <<<"$choice" ;;
esac
```

Use a JSON array when ids differ from labels or rows need subtitles. `--allow-custom` adds the current
nonmatching query as a choice:

```bash
jq -n '[
  {id: "stg", label: "Staging", subtitle: "staging.example.com"},
  {id: "prod", label: "Production", subtitle: "production.example.com"}
]' | agtermctl pick --prompt "Deploy target" --allow-custom
```

Typing matches labels only, so consequence text in a subtitle cannot filter one row out and leave its riskier
neighbour alone and preselected. An empty query keeps the order the items were supplied in, so the intended
default can be listed first.

With `--allow-custom` the item list may be empty, which is a rename prompt: `--query` seeds the current
value and the answer comes back as a custom result. `pick` reads stdin unconditionally, so redirect it or
the call blocks:

```bash
agtermctl pick --allow-custom --prompt "Rename to" --query "$current" < /dev/null |
  jq -r 'select(.result == "custom") | .query'
```

The seeded value is offered as the custom row on open; without `--query` nothing is listed until the user
types, since that row needs a nonblank query. Esc cancels.

Open without blocking when another process owns the lifecycle. The picker id reads back from the target
window's tree as `pickPending`; poll the same window, or cancel by exact id:

```bash
pick_id=$(
  printf '%s\n' alpha beta gamma |
    agtermctl pick --no-block --window "$AGTERM_WINDOW_ID" |
    jq -r '.id'
)

agtermctl tree --json --window "$AGTERM_WINDOW_ID" |
  jq -r '.result.tree.pickPending // empty'
agtermctl pick result "$pick_id" --window "$AGTERM_WINDOW_ID"
agtermctl pick cancel "$pick_id" --window "$AGTERM_WINDOW_ID"
```

Add `--follow` to raise a background target window when the picker opens. Without it, the picker waits in
that window without stealing focus.

## Require a yes/no answer in a cleanup hook

A shell cleanup hook can require approval before removing `./build`. This example runs inside
agterm and requires `jq`. Only an `answered` result with id `yes` reaches the removal:

```bash
#!/usr/bin/env bash
answer=$(agtermctl ask "Remove ./build?" \
  --message "Delete $PWD/build." \
  --button yes=Delete --button no=Keep --default no --destructive yes \
  --window "${AGTERM_WINDOW_ID:?run this hook inside agterm}" --follow \
  --socket "${AGTERM_SOCKET:?run this hook inside agterm}") || exit 1

printf '%s\n' "$answer" |
  jq -e '.result == "answered" and .id == "yes"' >/dev/null || exit 1
rm -rf -- ./build
```

Choosing Keep returns an answer with exit 0, so the id check is required. Esc and Alt+W (the
`close_session` chord) return `escaped` with exit 3; cancellation returns `cancelled` with exit 2. Both
stop the hook. `ask` leaves the hook's stdin untouched. Without `--target`, the default `--style terminal`
asks in the window's selected session, and `--style gui` centers the question over the window's terminal
area. The terminal style uses the terminal theme; `--style gui` uses the palette's material and native
buttons. The hook's behavior is the same.

## Say what you are doing while the user waits

`session hud` posts a passive panel over a session. The session keeps focus and stays typable under it, so
it is safe to leave up while you compute something. Cover the gap before a picker, then take it down:

```bash
me="$AGTERM_SESSION_ID"

agtermctl session hud "gathering options…" --spinner --detail "scanning branches" --target "$me"

branches=$(git for-each-ref --format='%(refname:short)' refs/heads)

agtermctl session hud update "ready" --detail "pick a branch" --target "$me"
choice=$(printf '%s\n' "$branches" | agtermctl pick --prompt "Check out which branch?")

agtermctl session hud close --target "$me"
```

In a split, keep the panel inside the calling agent's pane and follow that shell if pane roles change:

```bash
agtermctl session hud "gathering options…" --spinner --position bottom-right \
  --pane "$AGTERM_PANE" --pane-id "$AGTERM_PANE_ID" --target "$AGTERM_SESSION_ID"
```

The stable pane ID wins when it resolves. `--pane` is the fallback for older or unknown IDs. The selected
pane supplies the size, 80% cap, anchor, and 10% edge margin; `--sticky` drops the margin and, off center,
lifts the WIDTH cap to 100. A hidden pane keeps its HUD and shows it again
when restored; closing the pane closes its HUD.

`session hud update` repaints in place, no re-spawn and no blink, and it replaces the whole spec: `--detail`,
the spinner, `--text-color`, `--pane`, and `--pane-id` are dropped unless repeated. `--spinner-style bar|braille|circle|blocks|dot` picks the look and
turns the spinner on by itself (`dot` blinks instead of animating, for a panel up for minutes), and an
update may switch style mid-flight; `--spinner-style none` stops it, which is also what a read-back's
`none` echoes back to. `--position` anchors the panel to any of the nine
`top-left|top-center|top-right|center-left|center|center-right|bottom-left|bottom-center|bottom-right`
(default `center`), the same anchors `session background` takes, each off-center one keeping a fixed margin
off that pane edge on its own — a corner is what keeps the panel clear of the text being read, and the bare
`top`/`bottom` still work for the middle column. `--text-color '#rrggbb'` colors the text (an update can
change it, unlike `--background-color`), and `--size-percent N` overrides the panel's WIDTH; its height
always follows the message.

Read it back from the session node, and note the panel does NOT set `overlay` — one slot, and whichever
occupant holds it is the one that reports:

```bash
agtermctl tree --json | jq -r --arg s "$me" '
  .result.tree.workspaces[].sessions[] | select(.id == $s) | .hud.message // "no hud"'
```

Nothing announces a HUD as an event, so poll `tree` when another process owns the lifecycle. The slot is
shared with `session overlay open`, which means a second `session hud` replaces the first, a
`session overlay open` replaces a HUD, and `session overlay result` over one errors
`no overlay result: the slot holds a hud`. A HUD over
a RUNNING program is refused instead: a message is replaceable, a program is not.

### Keep a status board in a HUD

A controller agent driving worker sessions can keep a short status board in a corner of its own session
instead of printing it into its chat. Write the board to a file and post it with `--markdown`; `--font-size`
keeps it small, and a later `update` replaces it in place:

```bash
cat > /tmp/status.md <<'EOF'
## Workers
- **api** refactor: tests green
- **web** login page: waiting on review
- **infra** migration: *blocked*, needs a token
EOF

agtermctl session hud --file /tmp/status.md --markdown --font-size 11 --position top-right \
  --target "$AGTERM_SESSION_ID"

# after rewriting the file
agtermctl session hud update --file /tmp/status.md --markdown --position top-right \
  --target "$AGTERM_SESSION_ID"
```

An update replaces the whole spec, so repeat `--markdown` and `--position`; the font stays what the panel
opened with. A single newline inside a paragraph is a space, so keep entries as list items or end a line with
two spaces. A board taller than the panel is clipped, its excess rows giving way to a dim `… N more`. The
file is read once per command; nothing watches it, so push each change with an `update`.

## Navigate and manage windows

```bash
agtermctl session go --to next            # step selection to the next session
agtermctl session go --to next-attention  # jump to the next blocked/completed session
w=$(agtermctl window new "scratch" --json | jq -r '.result.id')
agtermctl window resize "$w" --width 1200 --height 800
agtermctl window move "$w" --x 100 --y 100 --display 0
agtermctl window zoom "$w"                 # maximize-to-screen toggle (call again to restore)
agtermctl window fullscreen "$w"           # native full screen toggle (Alt+Enter)
agtermctl window select "$w"
```

## Reload the keymap after editing it

```bash
$EDITOR ~/.agterm-win/keymap.conf
agtermctl keymap reload          # prints ok, or the parse-diagnostic count
```

`keymap.conf` lives in the `configDirectory` folder from `settings.json` when that is set.

## Apply a settings.json edit

agterm-win has no `ghostty.conf`. Its settings are `settings.json` in the state directory (`~/.agterm-win` by
default):

```bash
$EDITOR ~/.agterm-win/settings.json     # e.g. change "fontSize"
agtermctl config reload                 # re-read settings.json and apply it to every pane; prints ok
```

## Set the terminal theme

The app default is the bundled `agterm` theme; setting no theme uses agterm-win's built-in Windows palette.

```bash
agtermctl theme list                         # bundled themes, the current one marked *
agtermctl theme list --json | jq -r '.result.themes[]'   # just the names
agtermctl theme set "Dracula"                # set + persist it app-wide (unknown name errors)
agtermctl theme set "agterm"                 # back to the app default theme
agtermctl theme set                          # no theme: the built-in Windows palette

# follow the Windows light/dark app mode automatically — setting a dark theme starts tracking:
agtermctl theme set --dark "agterm"          # light side seeds from the current theme
agtermctl theme set "Builtin Light"          # while tracking, a name replaces the LIGHT side (pair kept)
agtermctl theme list --json | jq '.result | {sync, light, dark}'   # inspect the sync state
agtermctl theme set --dark none              # stop tracking; the light theme stays as the single theme
```

## Give a workspace an icon

The icon is a Segoe Fluent Icons name, or the path of an `.ico`, `.png` or `.svg` file. Names match exactly, including case. No name clears the icon.

```bash
agtermctl icon list | grep Map                       # find a name
agtermctl workspace icon MapPin --target active      # set it on the current workspace
agtermctl workspace icon --target active             # clear it
```

To use the project's own icon, look for a favicon in the session's project folder (`public/`, `static/`, `wwwroot/`, `src/assets/`). Projects made from one template often share the same favicon, so when there are several candidates, ask the user which one to use before setting it. A relative path is resolved against the current directory.

```bash
find . -path ./node_modules -prune -o \( -iname 'favicon*' -o -iname '*.ico' \) -print   # candidates
agtermctl workspace icon public/favicon.ico --target active                               # use one
```

## Targeting another window's tree

```bash
agtermctl tree --json --window work          # the "work" window's tree (prefix match)
agtermctl session new --window work --cwd "$HOME"
```
