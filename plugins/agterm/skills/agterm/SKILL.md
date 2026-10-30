---
name: agterm
description: >
  Drive agterm-win, a Windows terminal app, programmatically via its agtermctl CLI and a local
  control named pipe. Use when running inside an agterm-win session and asked to control the terminal:
  create, rename, close, select, or reorder sessions and workspaces; split panes; toggle the
  per-session scratch terminal; open or close overlay terminals and read their exit status; type
  into a session, copy its selection, or search its scrollback; post desktop notifications; manage windows (new, list,
  select, close, resize, move); change font size; or reload and edit the keymap and the app
  settings. Also covers the
  window/workspace/session addressing model and the AGTERM_* environment a spawned shell sees, plus
  subscribe to status, notification, session lifecycle, and tree-change events; diagnose problems
  (keymap, custom actions, diagnostics); and file a bug, question or feature request as a
  GitHub issue.
when_to_use: >
  Trigger on: agterm, agterm-win, agtermctl, agterm control socket, session.new, session.close, session.type,
  session.split, session.scratch, session.focus, session.resize, surface.zoom, dashboard, session.go, session.copy, session.paste, session.selectall, session.text, session.search, session.status,
  session.flag, session.seen, session.reveal, session.duplicate, session.background, session.overlay, workspace.new, workspace.select, workspace.move, workspace.focus, window.new, window.list,
  window.select, window.resize, window.move, window.zoom, window.fullscreen, quick terminal, sidebar, sidebar.mode, sidebar.expand, sidebar.collapse, flagged, notify, font.inc, keymap.reload, keymap.list, keymap.run, config.reload,
  theme.set, theme.list, events, events.read, event subscription, select theme, edit keymap,
  AGTERM_SESSION_ID, AGTERM_SOCKET, and asks to drive or script agterm. Also: troubleshoot agterm,
  keymap chord does nothing, custom action / custom command not working, agterm diagnostics, file an agterm
  bug, report an agterm issue, ask an agterm question / feature request.
allowed-tools: Bash(agtermctl:*)
---

<!-- agterm-skill -->

# Driving agterm

agterm-win is a Windows terminal. It exposes a programmatic control channel over a local named
pipe, driven by the companion CLI `agtermctl`. Use it to build and steer terminal layouts, run
programs in overlays, type into sessions, notify the user in the exact session you are working in,
and subscribe to control events. Events cover status, notifications, session lifecycle, and
structural tree changes. They do not stream terminal output; use `session text` to read a buffer.

## Am I inside agterm?

Each shell agterm-win spawns gets these environment variables. Check `AGTERM_ENABLED` before assuming
the control channel is available:

- `AGTERM_ENABLED=1` — this shell runs inside agterm-win.
- `AGTERM_SESSION_ID` — the current session's UUID (the session this shell belongs to).
- `AGTERM_WINDOW_ID` / `AGTERM_WORKSPACE_ID` — the owning window / workspace UUIDs.
- `AGTERM_SOCKET` — the control socket path this app uses (`agterm.sock` in the state directory,
  `~\.agterm-win` by default). The named pipe name is derived from it. It can be absent; then run
  `agtermctl` without `--socket`.
- `AGTERM_PANE` / `AGTERM_PANE_ID` — the surface's pane role (`left`|`right`|`scratch`) and a stable
  per-surface token; the agent-status hook forwards them as `session status --pane` / `--pane-id`. The
  token resolves the pane's LIVE slot, so a promoted-then-re-split agent still tags the right pane.

The app-level quick terminal is not in the tree. Its shell gets only `AGTERM_ENABLED` and `AGTERM_SOCKET`, with no window, session, workspace or pane identifiers. An overlay gets no `AGTERM_PANE`/`AGTERM_PANE_ID`. Custom-command actions from `keymap.conf` run with no `AGTERM_*` variables at all.

These variables are inherited by every process the session's shell spawns — including long-lived
daemons that outlive the shell. A terminal multiplexer server, a session manager (agent-deck and the like), or
any background service started from inside a session captures the spawning session's `AGTERM_*` and
passes it to every child it ever creates, so status hooks running in those children resolve
`$AGTERM_SESSION_ID` to the session that happened to start the daemon and report to the WRONG session.
Before starting such a process from inside agterm-win, scrub the variables
(`env -u AGTERM_ENABLED -u AGTERM_PANE -u AGTERM_PANE_ID -u AGTERM_SESSION_ID -u AGTERM_SOCKET -u AGTERM_WINDOW_ID -u AGTERM_WORKSPACE_ID <cmd>`
in Git Bash; see "PowerShell" below for the PowerShell form);
see troubleshooting.md ("agent-status glyph updates the wrong session") for diagnosing and fixing a
daemon that already holds the wrong values.

## Running agtermctl

`agtermctl.exe` ships next to `agterm-win.exe` in the published app folder. agtermctl must be on PATH
(install it from agterm-win's Help ▸ Install Command Line Tool… in the title bar, or the same item in the
command palette). To do it by hand, add the folder of `agtermctl.exe` to PATH. If it is not on PATH,
invoke it by absolute path.

- Without `--socket`, agtermctl uses the socket of the default state directory (`~\.agterm-win`). It
  does not read `AGTERM_SOCKET`. To be explicit, pass `--socket "$AGTERM_SOCKET"`.
- `--socket` and other options go **after** the subcommand: `agtermctl tree --json`, not
  `agtermctl --json tree`.
- The pipe accepts connections only from the current Windows user.
- Add `--json` to any command to get the raw JSON response (machine-readable). Without it, ordinary
  mutations print `ok`, batch close/move prints the affected session count, and `tree`/`list` print a
  human listing.
- Most commands make one request per invocation. A blocking `pick`, `ask` or `overlay open --block`
  polls for the result, and `events` polls with a fresh connection for each request. Mutating commands
  return the affected/new id; batch session mutations return the number actually changed. Create
  commands (`session new`, `session duplicate`, `workspace new`, `window new`) print the new id.

## PowerShell

The default session shell is PowerShell 7 (`pwsh`) when it is on PATH, otherwise `%COMSPEC%` (normally
`cmd.exe`). In PowerShell, read the session variables as `$env:AGTERM_SESSION_ID`, `$env:AGTERM_PANE_ID`,
and so on. Check `$env:AGTERM_ENABLED -eq '1'` before calling agtermctl. Put `--socket $env:AGTERM_SOCKET` after
the subcommand, like every other option:

```powershell
if ($env:AGTERM_ENABLED -eq '1') {
    agtermctl session status completed --target $env:AGTERM_SESSION_ID --pane-id $env:AGTERM_PANE_ID --socket $env:AGTERM_SOCKET
}
```

No agtermctl option takes JSON as an argument, so there is no JSON quoting to get right on the
command line. JSON goes in only on `pick`'s stdin, and `--json` gives JSON output. Parse the output
with `ConvertFrom-Json`:

```powershell
$tree = (agtermctl tree --json | ConvertFrom-Json).result.tree
$tree.workspaces.sessions | Where-Object id -eq $env:AGTERM_SESSION_ID
```

To pass `pick` items as JSON, build them as objects and use `ConvertTo-Json -InputObject`. Piping a
one-item list into `ConvertTo-Json` gives a single object, not an array; `-InputObject` (or `-AsArray`
in PowerShell 7) keeps the array. `pick` prints its result as bare JSON:

```powershell
$items = @(@{ id = 'a'; label = 'Option A' }, @{ id = 'b'; label = 'Option B' })
$choice = ConvertTo-Json -InputObject $items -Compress | agtermctl pick --prompt "Pick one" | ConvertFrom-Json
```

Windows PowerShell 5.1 pipes text to native programs as ASCII (`$OutputEncoding` is `us-ascii`), so
non-ASCII labels arrive as `?`. Set `$OutputEncoding = [System.Text.UTF8Encoding]::new($false)` first.
PowerShell 7 already pipes UTF-8. agtermctl drops a leading UTF-8 BOM from stdin. Captured or piped
agtermctl output is UTF-8, and both PowerShell versions decode it with `[Console]::OutputEncoding`,
the console code page: set `[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)` too,
or non-ASCII names come back garbled. Output shown on the console needs nothing. Do not use the
backtick line continuation inside an agtermctl line; keep each agtermctl call on one line.

To start a long-lived daemon without the session's values, remove them first. This also clears them
for the rest of this shell:

```powershell
Remove-Item Env:AGTERM_ENABLED, Env:AGTERM_PANE, Env:AGTERM_PANE_ID, Env:AGTERM_SESSION_ID, Env:AGTERM_SOCKET, Env:AGTERM_WINDOW_ID, Env:AGTERM_WORKSPACE_ID -ErrorAction SilentlyContinue
```

## The model

A **window** is the top level: a named bundle rendered in its own top-level Windows window. Each window
holds a tree of **workspaces**, each holding **sessions**. A session has a primary shell and can also
have: a **split** pane (a second shell side by side), a **scratch** terminal (a third full-coverage
shell, toggled like the split), and an ephemeral **overlay** (runs one program on top, then vanishes).
The app also has one **quick terminal**, a detached floating panel with its own shell and renderer, outside every window's tree. A human summon hides on focus loss; `quick show` pins it until hidden. Its shell survives hides and ordinary-window closes.

Inspect the live tree any time with `agtermctl tree --json` (workspaces → sessions, each with
`id`, `name`, `cwd`, `title`, `active`, `split`, `overlay`, `scratch`, `status`, `background`, `surfaces`). `title` is the raw OSC
terminal title (e.g. a remote host over SSH), omitted when none was reported — read it when a
session's local `cwd` is stale because it's connected to a remote. `surfaces[].id` is the
control address for `surface zoom` (`left`, `right`, `scratch`, or `overlay`), including
hidden-but-alive split/scratch surfaces. The tree object also carries five
read-only top-level fields: `idleMs` (ms since the last user input in the window), `autoFollowMs`
(the Auto-follow timeout in ms, omitted when Disabled), `sidebarVisible` (whether the window's
sidebar is currently shown — the read side of the write-only `sidebar` command), `sidebarMode`
(`tree` or `flagged` — the read side of `sidebar mode`), and `quickVisible` (the same app-level panel visibility in every window's tree). List windows with
`agtermctl window list --json`; each window also reports `autoFollowMs`, `sidebarVisible`, `geometry`
(the live frame `{x, y, width, height, display}` in the units `window move`/`window resize` take — the
read side, so record it then restore the exact frame; logical points relative to the display, `y`
grows downward), and `fullscreen`/`zoomed` (the read side of
`window fullscreen`/`window zoom`, so a script can make those toggles idempotent) — all omitted for a
closed window, but not the live `idleMs`, which is `tree`-only.

Settings > Agent Status can auto-follow the oldest blocked session after 5, 10, 30, or 60 seconds, or 5 minutes idle. It is Disabled by default. "Auto-follow away from a running session" is on by default; turn it off to stay on an `active` session. A followed block is consumed until its status leaves and re-enters `blocked`. User input and attention changes restart the interval. Editors and modal UI suppress auto-follow; closing them alone does not restart it. A background window may change selection but does not raise itself or reveal the status pane; only the foreground window reveals it, and not while zoomed. `tree.autoFollowMs` and `window list` report the applied timeout.

## Addressing

Commands that target a session or workspace take `--target` (default `active`):

- `active` — the selected session / current workspace.
- a full UUID (case-insensitive), or a unique **prefix** of one (git-style). Zero matches → `notFound`
  error; two or more → `ambiguous` error listing candidates.

`window.*` commands take the window id/prefix/`active` as a positional argument. Most other commands
accept `--window <id|prefix|active>` after the subcommand to operate on a specific window's tree
(default: the frontmost).

Scripts rarely type ids: create with `*.new` (capture the returned id), or act on `active`.

**Agents: `active` is almost never your own session.** `active` is the session the USER has selected in
the GUI; your shell runs in `$AGTERM_SESSION_ID`, and the user is usually on a different session while
you work. For any session-scoped command meant to act on *this* session — `overlay open`, `scratch`,
`type`, `text`, `background`, `status`, `copy`, … — pass `--target "$AGTERM_SESSION_ID"`. Omit it and
you open overlays / type into whatever the user has selected, not your own session.

## Launching a program in a session

**Bind it at creation.** `session new --command` (and `scratch --command`) makes the program the session
process, so no shell line is involved:

```bash
agtermctl session new --cwd ~/proj --name worker \
  --command "pwsh -NoLogo -Command 'claude (Get-Content -Raw ~/brief.md)'"   # pwsh resolves .cmd/.ps1 shims
```

`session type` drives an ALREADY-RUNNING program — it is not a launcher. Its keystrokes land in a line
buffer you do not own: a newline submits (a multi-line brief becomes N premature Enters), and the user
or a concurrent agent writes to that same buffer. An untargeted `session type` from another agent hits
whatever is `active`, and `session new` focuses — so a just-created session is briefly `active`, a stray
prompt concatenates with yours, and the program starts on the merged line. (`--no-select` skips the
focus, but the newline and shared-buffer hazards of `type`-as-launcher remain — `--command` is still the
rule.) After `--command`, confirm with `session text --target <new-id>` that your program is running,
not a bare shell prompt. agterm-win does not report a pane's foreground process in `tree`.

## Command summary (67 commands)

Run `agtermctl <area> <cmd> --help` for exact flags. Full detail in **reference.md**; recipes in
**examples.md**.

**tree** — print the workspace/session tree (`--json` for structured). The wire format has
`foreground`/`splitForeground` (the live argv of each pane's foreground process), but agterm-win does
not capture the foreground process, so both are always omitted. Each session node carries
`restoreCommand`/`splitRestoreCommand` (each pane's
persisted restore-command override set via `session restore` — the read side: omitted = auto-capture, `""`
= pinned to nothing (a plain shell), a command = the shell line that runs on the next launch), `status` (the agent-status set
via `session status`: `active`|`completed`|`blocked`, omitted when idle), `statusPane` (which pane set
that status: `left` (main) | `right` (split) | `scratch`, from `session status --pane`, omitted when
unset or idle), `statusBlink`/`statusColor`/`statusShape` (the status glyph's `--blink` flag, its `--color`
`#rrggbb` tint and its `--shape` silhouette from `session status`, omitted when idle / not blinking / using
the configured color or shape — the tint and the silhouette report the per-call override only), `background` (the background
spec — image/text watermark or solid color — set via `session background`, omitted when none — the read side of set/clear),
`unseen` (the unseen-notification badge count — raised by `notify`, cleared by `session
seen` — omitted when zero), `commandWait` (whether a `--command` session was created with `--wait` to
hold open after the command exits — the read side of `session new --wait`, omitted for a plain or
non-holding session), `overlaySizePercent` (an open overlay's floating-panel percent 1–100,
omitted for a full-pane overlay or no overlay so gate on `overlay` first; the read side of `overlay
resize` for a record-then-restore zoom), `splitRatio` (the left-pane divider fraction 0.05–0.95 of a
session that has a split — shown or hidden; omitted when there's no split or the ratio was never set (at
the default 0.5) —
the read side of `session resize`, record it to restore the exact divider), `splitFocused`
(which pane holds focus in a session that has a split — `true` = split/right, `false` = main/left; omitted
when there's no split; the read side of `session focus`, record it to restore focus), and `surfaces`
(`id`, `kind`, `active`, `visible`) for `surface zoom`. The tree top level carries `zoomedSurface`
(the control id of the currently zoomed surface, omitted when nothing is zoomed — the read side of
`surface zoom`, so a script can check the zoom state and record-then-restore). It also carries the read
side of the `dashboard` command (all omitted when no dashboard is open): `dashboardMembers` (the pane refs
the open dashboard shows, in grid order — `<session-id>:left` for a primary pane, `<session-id>:right` for
a split pane, so a split session appears as both), `dashboardHighlighted` (the highlighted cell's pane ref —
the one Enter jumps into, focusing that exact pane), `dashboardFontSize` (the absolute font size in points
applied to the cells, omitted when untouched), and `dashboardFontMode` (`auto`|`fixed`|`untouched`).

**events**: continuously print control events, subscribing from the current tail when no cursor is
given. Use `--json` for one bare event object per line; filter with repeatable or comma-separated
`--kind status|notify|session.created|session.closed|tree.changed`; resume with paired
`--run RUN --after SEQ`; and set page size with `--limit 1...1000`. The app retains 4,096 events for
one process run. Cursor run changes, expiry, and ahead-of-tail errors are fatal and are never silently
rebaselined. There is no terminal-output event stream.

**workspace** — `new [name] [--collapsed]` (`--collapsed` creates it closed in the sidebar so you can fill
it with `session new --no-select` without it opening) · `rename <name>` · `delete` · `select` ·
`move --to up|down|top|bottom` ·
`theme [NAME]` (the workspace's own terminal theme; no name clears it) · `icon [NAME]` (the workspace's own sidebar icon, a Segoe Fluent Icons name or the path of an .ico, .png or .svg file such as a project's favicon; no name clears it; `agtermctl icon list` prints the names) ·
`focus [on|off|toggle]` (collapse the sidebar tree to a single workspace; read back which workspace is
focused from the tree workspace node's `focused` flag) ·
`collapse [--target W] [--window W]` · `expand [--target W] [--window W]` (collapse/expand ONE workspace
in the sidebar tree — the per-workspace pair, distinct from the all-workspace `sidebar expand`/`collapse`;
read the open/closed state back from the tree workspace node's `collapsed` flag, `true` when collapsed and
omitted when expanded).

**session**
- `new [--cwd DIR] [--workspace W] [--workspace-name NAME] [--create-workspace] [--command CMD] [--wait] [--name NAME] [--after SID | --before SID] [--no-select]` —
  create (and focus) a session. Target the workspace by id/prefix (`--workspace`) OR by name
  (`--workspace-name`, mutually exclusive); add `--create-workspace` to reuse-or-create the named
  workspace when absent. `--command` runs that program as the session process instead of the default shell
  (argv-only, split on whitespace with single/double quotes grouping; no shell operators). Windows finds the
  program the way `CreateProcessW` does, on the app's own `PATH`, adding `.exe` when there is no extension.
  A `.cmd`/`.ps1` shim, an alias, or a directory your shell profile adds to PATH needs an absolute path or a
  `pwsh -NoLogo -Command '…'` wrapper. The same rule applies to `scratch --command`; `overlay open` runs its
  command through `%COMSPEC% /c` instead (see below);
  `--wait` (with `--command`, else an error) HOLDS the session open after the command exits, showing the
  press-any-key prompt with the final output intact instead of closing (persists across restart, unlike an
  overlay's live-only wait; read back on `tree`'s `commandWait`);
  `--name` seeds the sidebar label (default: the auto basename). `--after`/`--before` place it directly
  after/before an anchor session (id/prefix/`active`) instead of appending — the anchor carries its own
  workspace, so it's mutually exclusive with `--workspace`/`--workspace-name`. `new --after active` =
  create right after the current session. `--no-select` creates the session in the BACKGROUND — it is
  added to the sidebar but NOT selected or focused, leaving the current selection untouched (the new node
  is not `active` in `tree`); omit it for the default select-and-focus behavior.
- `duplicate [--target]` — create a fresh session (a plain login shell) in the target's workspace, right
  after it, rooted at the target's focused-pane cwd; selects + focuses it and returns the new id. ONLY the
  directory carries over — no custom name, command, split, scratch, status, flag, font size, or background.
  Equivalent to `session new --cwd <source cwd> --after <source>` in one round-trip. Read it back from
  `tree`: the new node sits directly after its source carrying the source's focused-pane cwd (equal to the
  source node's `tree.cwd` unless the source is a split focused off its primary pane, where `tree.cwd`
  reports the primary).
- `close [--target T ...]` — close one session, or repeat `--target` to close a batch with one
  grace-period undo.
- `select` · `rename <name>` · `reveal` (checks that the focused pane's cwd exists and selects the session;
  it does not open Explorer — the sidebar's **Reveal in Explorer** item does).
- `go --to next|prev|first|last|next-attention|prev-attention` — move the selection between sessions.
- `move <workspace>` (relocate) or `move --to up|down|top|bottom` (reorder within the workspace) or
  `move --after SID | --before SID` (place after/before an anchor session; the anchor carries its own
  workspace, so this relocates + positions in one shot, even cross-workspace). For workspace and
  after/before placement, repeat `--target` to move several sessions as one ordered block. Do not repeat
  `--target` with `--to up|down|top|bottom`.
- `type <text> [--stdin] [--select] [--pane left|right|scratch]` — inject keystrokes (real typing, Enter
  included) into the main pane, the split pane with `--pane right`, or the scratch terminal (even hidden)
  with `--pane scratch`. Pass `--target "$AGTERM_SESSION_ID"` to type into YOUR session, not the user's
  active one (see Addressing).
- `copy` — print the session's selected text (does NOT touch the system clipboard).
- `paste` — paste the system clipboard into the session (the socket analogue of Ctrl+V / Ctrl+Shift+V;
  read it back with `session text`).
- `select-all` — select the session's entire terminal buffer (read the
  selection back with `session copy`).
- `text [--all] [--lines N] [--pane left|right|scratch]` — print the session buffer as plain text. Default
  is the visible screen of the focused pane; `--pane scratch` reads the scratch terminal even while hidden;
  `--all` adds scrollback; `--lines N` keeps the last N lines.
- `search [needle] [--next|--prev|--close]` — search the terminal scrollback; prints the "N of M" counter.
- `split [on|off|toggle]` — side-by-side second shell (hide keeps it alive).
- `scratch [on|off|toggle] [--command CMD]` — full-coverage third shell (hide keeps it alive; `exit`
  recreates). `--command` (when showing) runs a program instead of a shell, run-once like `session new
  --command` (respawns the scratch if one is open). Target your own session with
  `--target "$AGTERM_SESSION_ID"` (see Addressing).
- `focus [left|right|other]` — move focus between split panes.
- `resize --split-ratio R | --grow-left D | --grow-right D` — move the split divider (no GUI/keymap
  equivalent — bind it via a `command "agtermctl session resize …"` custom action). `--split-ratio` sets
  the absolute left-pane fraction (0..1, clamped to 0.05..0.95); `--grow-left`/`--grow-right` nudge it by
  a fraction. Prints the applied (clamped) fraction.
- `status <idle|active|completed|blocked> [--blink] [--auto-reset] [--sound NAME] [--color #rrggbb] [--shape SHAPE] [--pane left|right|scratch] [--pane-id TOKEN]` — set the sidebar agent glyph (`--sound default` and `--sound beep` are aliases for the same Windows system alert; use a Windows WAV filename stem such as `"Windows Notify Calendar"` or a user WAV name for a distinct sound. Unknown names return `unknown sound: <name> (use 'default', 'beep', or one of: <Windows list>)` without changing the status. Names are case-insensitive; empty means no explicit sound. Settings lists available sounds with Preview. Put user WAV files in `<state directory>\sounds` (normally `~\.agterm-win\sounds`); user files override same-name Windows files. `--color` tints the glyph for this call only, reverting on the next status set without it; `--shape` (`circle`, `square`, `triangle`, `diamond`, `capsule`, `star`) picks its silhouette for this call only and reverts the same way, read back as the tree `statusShape` field; `--pane` records which pane set it — `left`=main, `right`=split, `scratch` — so the status is cleared when that pane goes away, read back as the tree `statusPane` field (a keystroke does not clear a status, and attention navigation only steps the selection, it does not reveal the pane); `--pane-id` is the hook-forwarded stable surface token (`$AGTERM_PANE_ID`) that resolves the pane's live slot and overrides a stale `--pane` after a promote + re-split — scripts set `--pane` directly and leave `--pane-id` to the hook).
- `flag [on|off|toggle|clear]` — flag a session for the flagged working-set view (`clear` unflags all).
- `seen [--target] [--window W]` — clear the session's unseen-notification badge WITHOUT changing the
  selection or focus (the focus-free counterpart to `notify`, which raises the badge). Idempotent — a
  no-op when already zero. Read the current count from the tree node's `unseen` field. Use it so an
  orchestrator can acknowledge a driven session's notifications without pulling focus to it.
- `restore ("cmd" | --none | --clear) [--pane left|right] [--pane-id TOKEN]` — pin what a pane re-runs on
  the NEXT launch, overriding the captured foreground command. A `"cmd"` shell line pins it, `--none` pins
  nothing (a plain shell), `--clear` drops the override back to auto-capture. Written now, consumed on the
  next launch (it never touches the running session), and STICKY — fires again on every restart until
  cleared. Gated on the `restoreRunningCommand` setting ("Restore running commands on restart"; a set
  while it is off succeeds with a note that nothing will run). Read back as the tree node's
  `restoreCommand`/`splitRestoreCommand`. `--pane right` needs a split; `scratch` is rejected. `--pane-id`
  (the shell's `$AGTERM_PANE_ID`) resolves the pane's live slot — unlike `session status`, a token that
  does not resolve errors unless `--pane` is also given. For a non-idempotent command like
  `claude --resume … --fork-session` (which mints a new session on every restart), a Claude Code
  `SessionStart` hook rewrites the override to the live id on every start so the next restart reattaches
  instead of forking. The pinned value is shell code stored in the state file and readable via `tree`, so
  it must not carry secrets. See examples.md.
- `background image <path> [--opacity F] [--fit contain|cover|stretch|none] [--position P] [--repeat]` ·
  `background text <text> [--color #rrggbb] [--opacity F] [--fit ...] [--position ...]` ·
  `background color <#rrggbb>` · `background clear` — set an image (PNG/JPEG) or text watermark, or a
  solid terminal background color, for the session. Per session; survives restart; read back as the tree
  node's `background`. `--opacity` 0.0–1.0. agterm-win validates and stores the spec, but the window does
  not draw it.
- `overlay open <command> [--cwd DIR] [--wait] [--block] [--size-percent N] [--background-color #rrggbb] [--follow]` ·
  `overlay resize (--size-percent N | --full)` ·
  `overlay close` ·
  `overlay result` — run a program on top of a session; `--block` waits and exits with its status.
  `overlay resize` changes an ALREADY-OPEN overlay: `--size-percent N` (1-100) makes it a floating panel,
  `--full` switches it back to the full-pane overlay; the program keeps running (no re-spawn).
  Target with `--target "$AGTERM_SESSION_ID"` for YOUR session (default `active` is the user's selection).
  **By default `overlay open` does NOT switch the user** — full and floating (`--size-percent`) both open
  on `--target` and run their program in the background; the panel appears when the user visits that
  session. **Pass `--follow` to select the target after opening** (a no-op if it is already active): use
  `--follow` when you want the user pulled to the overlay, omit it to open quietly on your own or another
  session.
  `--background-color` gives the overlay pane its own solid color, independent of the session's. The
  command runs through `%COMSPEC% /c`, so cmd.exe resolves it on PATH.

- `session hud [open] <message> [--detail T] [--spinner] [--spinner-style S] [--position P] [--background-color #rrggbb] [--text-color #rrggbb] [--size-percent N] [--sticky] [--no-frame] [--hide-after SECONDS] [--pane P] [--pane-id ID]` ·
  `session hud update <message> [--detail T] [--spinner] [--spinner-style S] [--position P] [--text-color #rrggbb] [--size-percent N] [--sticky] [--no-frame] [--hide-after SECONDS] [--pane P] [--pane-id ID]` ·
  `session hud close` — post a small **passive** panel over the session saying what you are doing
  ("gathering options…"). Unlike an overlay it takes no input and steals nothing: the session keeps keyboard
  focus, the user keeps typing, and the terminal behind it is neither dimmed nor click-blocked. With
  `--markdown` a `[label](url)` link is underlined and opens on Ctrl+click, so the panel can link the PR or
  ticket it is about. Use it
  for the seconds an agent needs before it can show something (computing picker items, waiting on a slow
  command), then take it down. `open` is the default subcommand, so `session hud "…"` posts; a message that is
  literally `update` or `close` needs the explicit `session hud open` verb. `--detail` adds a dim second line,
  `--spinner` animates a glyph in the default `bar` style and `--spinner-style bar|braille|circle|blocks|dot`
  picks another, turning the spinner on by itself (`dot` blinks instead of animating, for a panel up for
  minutes; an update may switch style in place). `--spinner-style none` is accepted and leaves the panel
  static, so the `none` a read-back reports round-trips. `--position` anchors it to any of the nine
  `top-left|top-center|top-right|center-left|center|center-right|bottom-left|bottom-center|bottom-right`
  (default `center`), the same set `session background` takes; every anchor off center holds a fixed margin
  off that pane edge automatically, so a corner keeps the panel out of the text the user is reading. The
  bare `top`/`bottom` are still accepted for `top-center`/`bottom-center`, and the read-back reports the
  canonical anchor. `--pane primary|left|top|split|right|bottom` makes that pane the coordinate space for
  the anchor, size cap, and margin. `--pane-id "$AGTERM_PANE_ID"` follows the same shell after pane swaps or
  promotion and overrides `--pane` when it resolves. An unknown token needs a `--pane` fallback. Open refuses
  a pane that is not visible. Hiding a target keeps the HUD alive until the pane returns; closing it closes
  the HUD. The panel is sized from the message on both axes:
  width from the longest line, height from the number of them — so a title and a subtitle give a wide, short
  panel, not a square one. `--size-percent N` (1-100) overrides the WIDTH only, bounded to 10-80% of the
  pane, or up to 100% with `--sticky` off center, since a message must never cover the session it is about.
  The height always follows the message. `--sticky` (no margin at the edge or corner `--position` names)
  and `--no-frame` (no border, rounding or blank rows) pin a caption to an edge; reference.md has the
  example. `--text-color` colors the panel's TEXT and `--background-color` its
  backing, independently. `session hud update` repaints in place with no re-spawn and no blink,
  and REPLACES the whole spec. Repeat `--detail`/`--spinner`/`--text-color`/`--sticky`/`--no-frame`/`--pane`/`--pane-id` to keep them, since an omitted
  one drops. It takes no `--background-color`: the surface reads that once at creation, so only a fresh
  `session hud` changes it and `tree` keeps reporting the creation color across updates, while the text color rides
  the panel's body file and an update recolors it in place. Message and detail are capped at 256 characters and reject control characters, newline included.
  It occupies the SAME slot as `session overlay open`, so: a second `session hud` replaces the first,
  `session overlay open` replaces a HUD (a running program is never replaced), `session overlay close`
  takes a HUD down, `session overlay result` refuses with
  `no overlay result: the slot holds a hud`, `session overlay resize --size-percent`
  works on it while `--full` is refused (`a hud is always floating: pass --size-percent, not --full`),
  and `surface zoom` will not address it. `session hud update`/`session hud close` with none up answer `no hud`. Read it
  back from the tree node's `hud` object; nothing announces it as an event, so poll `tree`.

**window** — `new [name]` · `list` · `select <id>` · `close <id>` · `rename <id> <name>` ·
`delete <id>` · `resize <id> --width W --height H` · `move <id> --x X --y Y [--display N]` ·
`zoom <id>` (maximize/restore toggle) ·
`fullscreen <id>` (toggle full screen, the `toggle_fullscreen` action, Alt+Enter by default). `select`
makes the window frontmost for routing; Windows may refuse to raise it, and then its taskbar button
flashes. `resize` is clamped to the display's work area, and `move` keeps part of the window on screen.
Closing the last window ends the app; the next launch opens it again.

**surface** — `zoom [show|hide|toggle] [--target SURFACE_ID|active|quick] [--window W]` zooms a session surface inside its window. Omit `--target` to use that window's active session surface, never quick. Explicit `--target quick` independently expands the visible detached panel to its monitor's work area; it ignores `--window` and refuses a hidden panel with `surface not available: quick`, even for `hide`. While quick is zoomed, every tree reports `zoomedSurface: "quick"`; hiding or unzooming it restores the window's own zoom readback.

**dashboard** — `dashboard <ids…> [--font-size N | --auto-size] [--window W]` opens a view-only grid
showing the named sessions' live panes; `dashboard --mru [--font-size N | --auto-size] [--window W]`
opens the window's most-recently-used sessions instead of naming ids; `dashboard --close [--window W]`
closes it. The cell unit is a session+pane: a non-split session is one cell, and a SPLIT session shows as
TWO cells (its left/primary pane and its right/split pane). View-only: no cell takes input — the keyboard
drives it (arrows move the highlight, Enter jumps into the highlighted session AND focuses that exact pane
then closes, Esc closes). `--font-size N` sets an absolute cell font in points; `--auto-size` sizes cells
relative to the Settings default font, shrinking as the grid grows (the two are mutually exclusive; a
non-positive size is rejected). The 9-cell cap counts PANES (laid out `ceil(sqrt(n))`), so a set whose
panes exceed 9 is capped to the first 9 panes and the dropped-pane count is reported; ids are deduped and
honor `--window` (default frontmost). `--mru` is mutually exclusive with explicit ids and `--close`, and
composes with the font flags. Read the state back from the tree's top-level `dashboardMembers`
(pane refs `<id>:left`/`<id>:right`, in grid order) / `dashboardHighlighted` (a pane ref) /
`dashboardFontSize`/`dashboardFontMode`. Zoom and the dashboard are mutually exclusive: opening one CLOSES
the other. Opening/closing resizes each pane's pty to its cell, so programs may redraw — view-only
means no input, not no process effect. The most-recently-used grid also has a GUI opener: **Ctrl+Shift+D** (the
`dashboard` built-in action), the title-bar **Dashboard** button, and the command palette's **Dashboard** entry
TOGGLE the frontmost window's MRU dashboard auto-sized (identical to `dashboard --mru --auto-size`); no new
control command, the socket `dashboard` command is unchanged.

**pick**: `pick [--prompt TEXT] [--query TEXT] [--select ID] [--allow-custom] [--follow] [--window W] [--no-block]`
reads choices from stdin and opens the target window's native fuzzy picker. Supply nonblank lines (each line
is both the id and label) or a JSON array of `{id,label,subtitle?}` items; typing matches labels only, and an
empty query keeps the supplied order, so without `--select` the caller's first item is the one Enter runs. `--query` prefills
the field and filters on open, which re-ranks and drops that order. `--select ID` opens with that item
highlighted and scrolled into view (it must name a supplied item; a `--query` that hides it leaves the first
visible row). An empty item list is accepted only with
`--allow-custom`, giving a plain text prompt; stdin is read either way, so an itemless call needs
`< /dev/null` or it blocks. The default blocks until the user chooses or cancels and prints the bare JSON
result. `--no-block` prints the picker id instead;
`pick result ID [--window W]` reads it later, and `pick cancel ID [--window W]` cancels it.
Pick shares its window modal slot with GUI asks. A background target is raised only with `--follow`.
Read the live picker id from the tree's top-level `pickPending` field.

**ask**: `ask TITLE --button ID=LABEL [--button ...] [--message TEXT]` opens a question and waits for
an answer. The default `--style terminal` uses the selected session, with one pending ask per session.
`--pane` or `--pane-id` narrows it to a pane without requiring `--target`. An explicit unselected session
is accepted and keeps its ask hidden and pending. A terminal ask leaves the rest of the window usable.
`--style gui` uses the window modal slot shared with pick; GUI pane placement requires a selected
`--target`. Without a target, GUI style centers over the window's terminal area, excluding the sidebar.
`--window` selects the window; `--follow` raises it without changing session selection.
`--default ID` seeds the highlight, `--hotkey ID=LETTER` adds a shortcut, and `--destructive ID` marks a
button that cannot be the default. `--align left|center|right` aligns the buttons; `--width N` fixes the
panel width to 10...100 percent of its region. Exit 0 means answered, including No: inspect `.id`.
Esc or the `close_session` chord (Alt+W by default) on the interactive ask return `escaped` with exit 3;
cancellation returns `cancelled`
with exit 2. `--no-block` returns an id for `ask result ID` or `ask cancel ID`; explicit `--window` must
match its owner. Tree exposes terminal asks on session nodes as `ask: {id, pane?}` and GUI asks as
top-level `askPending`. See [reference.md](reference.md#ask) for result formats and command details.

**quick** — `[show|hide|toggle]` (visibility; read back from the tree's `quickVisible`) ·
`type TEXT` (or `--stdin`) inject keystrokes into the app-level quick terminal ·
`text [--all] [--lines N]` read its screen back — the twins of `session type`/`session text`,
frontmost-window-only (no `--target`/`--window`/`--pane`).

**sidebar** — `[show|hide|toggle]` (visibility; read back from the tree's `sidebarVisible`) ·
`mode [tree|flagged|toggle]` (flip between the workspace tree and the flat flagged working-set list; read
back from the tree's top-level `sidebarMode`) · `expand [--window W]` (expand every workspace) ·
`collapse [--window W]` (collapse all workspaces except the active one, which stays expanded).
Visibility/mode act on the frontmost window; `expand`/`collapse` default to the frontmost but take a
`--window` selector to target any open window.

With Settings > Interface > Show sidebar only in the active window (`autoHideSidebarInactiveWindows`) enabled, every terminal-window activation shows that window's sidebar and hides the others. Manual visibility changes last until the next activation. Disabling the setting keeps the current visibility of each window.

**notify** — `notify <body> [--title T]` — post a desktop notification attributed to a session. To signal that you need the user, prefer `session status` (`blocked`/`completed`), a persistent typed attention state rather than a one-shot banner; keep `notify` for a one-off nudge.

Settings > Notifications > Flash taskbar button offers Off (default) and Once. Once flashes the notification's owning terminal window while the app is in the background, even with banners off, Do Not Disturb enabled, or Windows notifications disabled for the app. It adds no sound. Only one window has pending notification attention; repeats do not restart it. Real activation of any agterm window, switching the setting Off, closing the target, or shutdown clears it. Stored `dockBounce: "untilFocused"` runs as Once without being rewritten; unknown values run as Off and remain preserved.

Main and split panes also accept terminal OSC 9 text and OSC 777 `notify;title;body` (BEL or ST terminated). Recognized OSC 9 ConEmu subcommands are not notifications; OSC 9;9 still reports cwd. Scratch, overlay and quick terminal notifications have no effects. An actually focused firing pane suppresses all OSC notification effects, but a visible unfocused split pane does not. Accepted OSC notifications raise the unseen count, emit `notify`, and request the same taskbar attention before the banner toggle. They do not change agent status. There is one app-wide OSC notification budget: one per second, or five seconds for the same last title/body; explicit `notify` bypasses it. Toasts replace by window/session/pane. Clicking reveals that target, reopening a saved window if needed; actual terminal focus removes that session's delivered banners and clears unseen. `session seen` clears only the count. Payloads over 2,047 UTF-8 bytes are dropped; titles and bodies are sanitized plain text, capped at 63 and 255 UTF-8 bytes. See reference.md for the safety limits and focus rules.

**font** — `font inc|dec|reset [--pane left|right|scratch]` — change a session pane's font size (omitted/`left` = main pane, `right` = the split pane, `scratch` = the scratch terminal). The wire format has `fontSize`/`splitFontSize`/`scratchFontSize` per pane, but agterm-win always omits them from `tree`.

**keymap** — `keymap reload` — re-read `keymap.conf` (`~\.agterm-win\keymap.conf` by default; prints the parse-diagnostic count) ·
`keymap list [--json]` — print what the keymap resolved to: every built-in action with its Windows chord (`*` marks one the
file moved), the custom commands and the diagnostics ·
`keymap run NAME [--target T] [--window W]` — start the custom command named NAME against a session, as the custom command palette
does. The reply means the process started; its exit status and output are not reported.

Custom commands opt into a failure panel with `command "Build" [chord] --error-hud .\build.ps1`, placed
with `--error-position POS` and `--error-pane left|right`; see
[keymap.conf format](reference.md#keymapconf-format) for the parsing rules and defaults.

**config** - `config reload` - re-read and apply `settings.json` from the state directory, and reset every
session's font size to the configured base size (prints `ok`; the diagnostic count is always 0).

**theme** — `theme list` (bundled themes, current marked `*`) · `theme set [name]` — set + persist the
terminal theme app-wide, per slot: a NAME sets the light/single theme (a dark theme, if set, is kept);
`theme set --dark <name>` sets the dark theme, which makes the terminal track the Windows light/dark
appearance automatically; `theme set --dark none` stops tracking. The app default is the bundled
**agterm** theme; omit the name for the built-in terminal colors (listed as "default terminal"); an unknown name errors.

**restore** — `restore clear` — clear every session's saved foreground command so the next restart
restores plain shells. agterm-win does not capture foreground commands, so this only clears ones
loaded from an older snapshot.

## Troubleshooting and reporting

When the user hits a problem (a keymap chord that does nothing, a custom action that does nothing,
notifications missing), diagnose it from inside the session first: inspect `agtermctl tree --json`,
run `agtermctl keymap reload` for the parse-diagnostic count, and check the window for a failure
InfoBar. agterm-win writes no log file; its diagnostics go to `System.Diagnostics.Trace`, which the user
can read with DebugView. If it turns out to be a bug, offer to help file it.

**Filing is opt-in and draft-first.** Never run a `gh` command without the user's explicit approval.
Bugs, questions and feature requests all go to GitHub issues at
https://github.com/Dis1092006/agterm-win/issues. Decide first whether it is a bug (a supported feature
misbehaving) or something not supported / a question / an idea, and say which in the title. Draft the
title and body, show it to the user, scrub anything private (tokens, hostnames, usernames in paths,
selection/clipboard text), and only post after an explicit go-ahead. If `gh` is missing or not
authenticated, hand the user the prefilled text plus the new-issue URL instead.

Full detail, templates, and the exact `gh` commands are in **troubleshooting.md**.

## Reference files

- **reference.md** — full per-command detail: every flag, the JSON return shapes
  (`result.id`/`text`/`exitCode`/`count`/`affected`/`tree`/`windows`), error strings, the scratch/overlay/split
  lifecycle, and the keymap.conf format (`map` / `command`, chords, leaders, `{AGT_X}` tokens).
- **examples.md** — copy-paste agtermctl recipes for common tasks (build a layout, run a program in a
  blocking overlay and read its status, type into a fresh session, notify, inspect the tree).
- **troubleshooting.md** — diagnosing common problems (keymap, custom actions, diagnostics) and the
  issue reporting workflow (draft-first, scrub, never post without approval).

Read those files when you need exact flags, return shapes, or worked examples.
