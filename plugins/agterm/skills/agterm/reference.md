# agterm control reference

Full detail for every `agtermctl` command. See `SKILL.md` for the model and addressing overview, and
`examples.md` for recipes. Each command names its wire command (the `command` field of the JSON
request) as "Wire: `name`.".

## Connection and output

- **Transport**: the app listens on a Windows named pipe. `AGTERM_SOCKET` holds a socket path
  (`<state dir>\agterm.sock`), not a pipe name; agtermctl derives the pipe name from that path. The
  variable can be absent; then run agtermctl without `--socket`.
- **Socket resolution** (when `--socket` is omitted): agtermctl uses the state directory's
  socket, `~\.agterm-win\agterm.sock` by default. It does not read `AGTERM_SOCKET` itself. Passing
  `--socket "$AGTERM_SOCKET"` is the safe explicit form when the variable is set.
- **`--json`**: prints the raw response object. Without it, ordinary mutations print `ok`, batch
  close/move prints the affected session count, and `tree`/`window list` print a human listing. Use
  `--json` when you need to read ids or values back.
- **Response shape**: `{"ok": true, "result": {…}}` or `{"ok": false, "error": "<message>"}`.
  `result` carries one of: `id` (affected/new session/workspace/window), `text` (session copy/text),
  `exitCode` (overlay result), `count` (diagnostics/search), `affected` (sessions actually changed by a
  batch close/move), `tree` (the tree), `windows` (window list), `ratio` (session resize), `theme`,
  `themes`, `sync`, `light`, `dark` (theme), `events` (events.read), `pick` (pick result), `ask` (ask
  result), `pane` (the pane `ask open` placed a terminal question on). The process exit code is non-zero
  when `ok` is false, and the error goes to stderr as `error: <message>`.
- **Options go after the subcommand**: `agtermctl session type "ls" --target active`, never before it.

## events

`agtermctl events [--json] [--kind KIND ...] [--run UUID --after SEQ] [--limit N]` continuously
prints control events. Wire: `events.read`. Each poll is one ordinary pipe connection and one
`events.read` response. The CLI immediately reads again after a non-empty page and waits 250 ms only
after an empty page.

With no cursor, the first read subscribes from now: it returns an empty batch anchored at the current
tail, and the CLI prints only later events. The app keeps a non-destructive ring of the latest 4,096
events for its current process run. Independent readers do not consume one another's events.

The five event kinds and payloads are:

- `status`: `name`, normalized `status` (`idle`|`active`|`blocked`|`completed`), a `blink` boolean,
  and optional `pane`, `color` and `shape` (the last two being the per-call `--color`/`--shape`
  overrides). An event fires whenever the whole indicator changes, not just the state name — so a
  change to `blink`, `pane`, `color` or `shape` alone is a real event you can watch, while re-asserting
  an identical indicator emits nothing. Clearing emits `idle`.
- `notify`: `name`, effective `title`, and `body`. It is emitted for an accepted notification,
  including when desktop banners are disabled.
- `session.created` / `session.closed`: session `name`, emitted when the session enters or leaves a
  visible window tree. Undo emits a new `session.created`; grace-period finalization does not emit a
  second close.
- `tree.changed`: an empty payload and the affected window id. Name, membership, and ordering changes
  emit it. Read `tree --json` for the current snapshot.

Every event has `seq` (app-wide sequence), `ts` (Unix timestamp), `kind`, optional
`window`/`workspace`/`session` ids, and `payload`. Human mode prints one compact line. `--json` emits
one bare `ControlEvent` JSON object per line and flushes promptly.

`--kind` may be repeated or comma-separated. Unknown kinds are errors. Filtering advances the global
cursor across nonmatching events, so changing a filter does not replay skipped history. `--limit`
defaults to 100 and accepts 1 through 1,000.

The raw `events.read` response stores the batch under `result.events`:

```json
{"ok":true,"result":{"events":{"run":"01234567-89AB-CDEF-0123-456789ABCDEF","next":42,"items":[]}}}
```

A no-cursor response supplies the `run` and `next` anchor. Resume with both values:
`agtermctl events --run RUN --after NEXT`. The options must appear together. The streaming `--json`
format contains bare events rather than this batch envelope, so a restart-safe client must retain the
cursor from raw `events.read` responses.

Cursor failures return `ok: false`, one of `event run changed`, `event cursor expired`, or
`event cursor is ahead of the current sequence`, plus the current empty anchor under
`result.events`. Treat them as data-loss boundaries. Do not silently use the supplied anchor unless
the caller explicitly accepts dropping the missing interval. `agtermctl events` exits non-zero on a
cursor, transport, or server error and does not retry forever while the app is absent.

## Addressing

- `--target` defaults to `active` (the selected session / current workspace). Accepts a full UUID
  (case-insensitive) or a unique prefix. Zero matches → `no such <noun>: <target>`; ambiguous prefix →
  `ambiguous <noun> prefix '<target>' → <ids>` (the error lists candidates).
- **For an agent, `active` is the USER's GUI-selected session, not yours.** Your shell is
  `$AGTERM_SESSION_ID`; the user is usually on a different session while you work. Pass
  `--target "$AGTERM_SESSION_ID"` on any session-scoped command (`overlay open`, `scratch`, `type`,
  `text`, `background`, `status`, `copy`, …) that must act on the session you run in — otherwise it hits
  whatever the user has selected. `overlay open` opens in the background without switching the user
  (both full and floating); pass `--follow` to additionally SELECT the target, switching the user to it.
- `--window <id|prefix|active>` (on session/workspace/tree/font/notify commands) picks which window's
  tree to act on; default is the frontmost. With `--window` set, that window must be open. Without it,
  an id/prefix session target is matched across all open windows.
- `window.*` commands take the window selector as a positional argument, default `active` (frontmost).
- A window need not be open to be a `window.*` target (e.g. `window select` opens a closed one).

## tree

`agtermctl tree [--json] [--window W]` — the workspace/session tree. Wire: `tree`. Each session node:
`id`, `name`, `cwd`, `title` (the raw OSC terminal title — e.g. a remote host over SSH — omitted
when none reported; distinct from `name`, the derived sidebar label), `active` (selected),
`split` (split shown), `splitRatio` (the left-pane fraction 0.05–0.95 of a session that HAS a split —
shown or hidden; omitted when there's no split or the ratio was never explicitly set (divider at the
default 0.5) — the read side
of `session resize`, record it to restore the exact divider position),
`splitFocused` (which pane holds focus in a session that HAS a split — `true` = the split/right pane,
`false` = the main/left pane; omitted when there's no split; the read side of `session focus`, record it
to restore focus via `session focus --pane left|right`),
`commandWait` (whether a `--command` session was created with `--wait` to hold open after the command
exits — the read side of `session new --wait`; omitted for a plain or non-holding session),
`overlay` (overlay shown; reads `false` while the overlay slot holds a HUD),
`overlaySizePercent` (an open overlay's size — the
floating panel's percent of the pane, 1–100; omitted = a full-pane overlay or no overlay, so gate on
`overlay` first; the read side of `session overlay resize`, e.g. record it before switching to `--full`
to restore the exact size), `hud` (the open HUD message panel, see `session hud`; omitted when none),
`ask` (`{id, pane}` of a pending terminal-style question on this session, see `ask`; omitted when none;
a gui-style question is reported by the top-level `askPending` instead), `scratch` (scratch shown),
`flagged` (in the
flagged working-set), `status` (the agent-status — `active`|`completed`|`blocked` — omitted when
idle), `statusPane` (which pane set that status — `left` (main) | `right` (split) | `scratch` — the
`--pane` value from `session status`, omitted when unset or idle; gated on the same non-idle condition
as `status`, so it is never reported without a `status`), `statusBlink` (`true` when the status glyph is
set to blink — the `--blink` value; omitted when idle or not blinking), `statusColor` (the `#rrggbb`
glyph-tint override — the `--color` value; omitted when idle or using the configured color) and
`statusShape` (the glyph silhouette override — the `--shape` value, one of
`circle`|`square`|`triangle`|`diamond`|`capsule`|`star`; omitted when idle or using the configured shape.
Like `statusColor` it reports the PER-CALL override only, so a shape set in settings.json
(`activeStatusShape` and so on) reads back as absent),
`foreground`/`splitForeground` (upstream reports each pane's foreground process here; agterm-win never
reads a pane's foreground process, so the running app always omits both),
`restoreCommand`/`splitRestoreCommand` (each pane's persisted restore-command override — the read side of
`session restore`: omitted = no override, `""` = pinned to nothing (a plain shell), a
command string = the shell line that runs on the next launch; reported from persisted state, so a read
after the override already fired still reports what is pinned), `background` (the
background spec set via `session background` — a `{kind, text?, imagePath?, colorHex?, opacity?, fit?,
position?, repeats?}` object; `kind` is `image`/`text`/`color` — omitted when none is set), `unseen`
(the unseen-notification badge count — raised by `notify`, cleared by `session seen`, session selection, or window activation when that session's main or split pane has accepted focus — omitted
when zero), `fontSize`/`splitFontSize`/`scratchFontSize` (the LIVE font size in points of each pane —
the read side of `font --pane`; each omitted when that pane isn't realized. `fontSize` tracks the
default/left target (the main pane, or the promoted split survivor once the primary exits — the same pane
`font --pane left` writes); only the main pane's size survives a relaunch, so the split/scratch sizes and a
promoted survivor are live-only — read them back here rather than from the snapshot), and `surfaces` (array
of `{id, kind, active, visible}` where `kind` is `left`|`right`|`scratch`|`overlay`).
The surface `id` is the address for `surface zoom`; hidden-but-alive split/scratch surfaces are included
so a script can zoom them without changing split/scratch visibility first. Caveat: `active`/`visible`
derive from the session's own flags, not from zoom — and `visible` reads false for a pane behind a
FLOATING overlay even though it is visually on screen; address by `id`/`kind`, and read the zoom state
from the top-level `zoomedSurface`. Workspace nodes carry
`id`, `name`, `active`, `sessions`, `focused` (whether the sidebar
tree is collapsed to this workspace — the read side of `workspace focus`, distinct from `active` the
SELECTED workspace; omitted unless this is the focused one, and absent entirely when nothing is focused),
and `collapsed` (whether this workspace is COLLAPSED in the sidebar tree — the read side of
`workspace collapse`/`workspace expand` and `workspace new --collapsed`; `true` when collapsed, omitted
when expanded, so an all-expanded tree carries no `collapsed` keys).

The tree object itself carries twelve top-level read-only fields: `idleMs` (milliseconds since the last
user input in the window, omitted before any activity), `autoFollowMs` (the window's Auto-follow
timeout in milliseconds, omitted when Auto-follow is disabled), `sidebarVisible` (whether the
window's sidebar is currently shown — the read side of the write-only `sidebar` command, so a script
can restore it, e.g. a tmux-style zoom that hides the sidebar and must re-show it only when it was
visible before), `sidebarMode` (`tree` or `flagged` — the sidebar view mode, the read side of
`sidebar mode`), `quickVisible` (whether the window's quick terminal is currently shown — the read
side of the write-only `quick` command, so a script can make the toggle idempotent), `zoomedSurface`
(the control id of the surface terminal zoom currently fills the window with —
`surface:<session-id>:<kind>` or `quick`; omitted when nothing is zoomed — the read side of the
write-only `surface zoom` command, so a script can check "is it already zoomed" and
record-then-restore), and the four read sides of the write-only `dashboard` command (all omitted when
no dashboard is open): `dashboardMembers` (the pane refs the open dashboard shows, in grid order —
`<session-id>:left` for a primary pane, `<session-id>:right` for a split pane, so a split session appears
as both), `dashboardHighlighted` (the highlighted cell's pane ref — the one Enter jumps into, focusing
that exact pane), `dashboardFontSize` (the absolute font size in points applied to the cells, omitted when
the mode is `untouched`), and `dashboardFontMode` (`auto` for `--auto-size`, `fixed` for `--font-size`, or
`untouched`), plus `pickPending` (the id of the window's pending `pick`, omitted when none) and
`askPending` (the id of the window's pending gui-style `ask`, omitted when none). `idleMs` is live
and grows while the window is idle, so it is on `tree` only, never `window.list`; `sidebarVisible` is on
both; `sidebarMode`, `quickVisible`, `zoomedSurface`, and the four `dashboard*` fields are `tree`-only
(a GUI/keyboard change would leave a cached copy stale).
All twelve are read-only projections of GUI state.

## workspace

- `workspace new [name] [--collapsed] [--window W]` — create a workspace; returns its id. Wire:
  `workspace.new`. Name defaults
  to an auto-generated one. `--collapsed` creates it CLOSED in the sidebar tree so a script can build a
  workspace and fill it with `session new --no-select` without it ever opening (a fresh workspace is
  expanded by default). Read the state back from the tree workspace node's `collapsed` flag.
- `workspace rename <name> [--target] [--window W]`. Wire: `workspace.rename`.
- `workspace delete [--target] [--window W]` — keep-at-least-one; deleting the last workspace errors.
  Wire: `workspace.delete`.
- `workspace select [--target] [--window W]`. Wire: `workspace.select`.
- `workspace move --to up|down|top|bottom [--target] [--window W]` — reorder among siblings. Wire:
  `workspace.move`. Missing
  or invalid `--to` errors. Note: `--target active` resolves to the current workspace, which with no
  selected session falls back to the last workspace; address a specific workspace by id to step the
  same one.
- `workspace focus [on|off|toggle] [--target] [--window W]` — collapse the sidebar tree to a single
  workspace's subtree (hiding the others), or restore the full tree; returns the workspace id. Wire:
  `workspace.focus`. `on`
  focuses the target, `off` unfocuses it only when it is the currently focused one, `toggle` (default)
  flips. Per-window and persisted; orthogonal to `sidebar mode` (the flagged flat list ignores focus).
  While a workspace is focused, `session go` navigation is scoped to that workspace's sessions (and to
  the flagged set in flagged mode); an explicit `session select` of a session outside the focused
  workspace still auto-unfocuses to reveal it. An unknown mode errors.
- `workspace collapse [--target] [--window W]` — collapse ONE workspace's subtree in the sidebar tree
  (hide its sessions); returns the workspace id. Wire: `workspace.collapse`. The per-workspace
  counterpart of `sidebar collapse`
  (which collapses ALL but the active workspace) — this targets exactly the addressed workspace and does
  not depend on which one is active. Idempotent. Persisted. Read back from the tree workspace node's
  `collapsed` flag.
- `workspace expand [--target] [--window W]` — expand ONE workspace's subtree (show its sessions);
  returns the workspace id. Wire: `workspace.expand`. The inverse of `workspace collapse`, and the
  per-workspace counterpart of
  `sidebar expand`. Idempotent. To TOGGLE a workspace, read its `collapsed` flag off `tree` first, then
  call `expand` or `collapse`.

## session

- `session new [--cwd DIR] [--workspace W] [--workspace-name NAME] [--create-workspace] [--command CMD] [--wait] [--name NAME] [--after SID | --before SID] [--no-select] [--window W]`
  — create a session and focus it; returns the new id. Wire: `session.new`. `--cwd` sets the start
  directory (default the user's home directory). The destination workspace is addressed one of two
  mutually-exclusive ways: `--workspace`
  (id / unique prefix / `active`, the default) or `--workspace-name` (the sidebar label) — the latter
  errors if no workspace has that name unless `--create-workspace` is also passed, which reuses an
  existing one or creates it when absent (idempotent). `--command` runs that command as the session's
  process instead of the default shell (no echoed command line; the session closes when the command
  exits). It runs argv-style (tokenized, quotes respected, but NO shell), so shell operators (`;`,
  `&&`, `$VAR`, redirects, globs) are not interpreted. Windows resolves the program name the usual way
  (`CreateProcessW`); when the program cannot start, the window shows the "Terminal failed to start"
  failure bar. Wrap in a shell for shell syntax —
  `--command "pwsh -NoLogo -Command 'Get-ChildItem; git status'"` —
  or give an absolute path. Without `--command` the session runs the default shell: `pwsh.exe` from
  `PATH`, else `powershell.exe` from `PATH`, else `%COMSPEC%`, else `cmd.exe`.
  `--wait` (only with `--command`, else an error) HOLDS the session open after the command exits
  with the final output intact instead of closing immediately —
  so you can read a build/test/deploy's final output or an early failure that would otherwise flash and
  vanish. It persists across restart (unlike an overlay's live-only `--wait`), so a restored command
  session that re-runs its command holds again; read it back on `tree`'s `commandWait`.
  The command is persisted (`SessionSnapshot.initialCommand`) and re-runs on restore when
  `restoreRunningCommand` in settings.json is `true` (default off → a restored session is a plain
  shell). `--name`
  seeds the session's custom name (the sidebar label; blank/omitted leaves the auto basename),
  equivalent to a `session rename` right after create. `--after SID` / `--before SID` place the new
  session directly after / before an anchor session instead of appending at the end (the anchor is a
  session address — id / unique prefix / `active`). The anchor CARRIES ITS OWN WORKSPACE (resolved
  across all workspaces), so it names the destination workspace itself — `--after`/`--before` are
  therefore mutually exclusive with each other and with `--workspace`/`--workspace-name` (the anchor
  already picks the workspace). `agtermctl session new --after active` is the headline case: create
  right after the current session in one round-trip. `--no-select` creates the session in the BACKGROUND:
  it is added to the sidebar but NOT selected or focused, so the current selection and focus are left
  untouched (the new node is not `active` in `tree` — that flag is the read-back); omit it for the default
  select-and-focus behavior. Every other addressing/placement option composes with it, and a background
  `--create-workspace` create does not clear a focused-workspace filter either (it leaves the sidebar view
  put instead of revealing the new workspace).
- `session duplicate [--target] [--window W]` — create a fresh session in the SAME workspace as the
  target, inserted directly AFTER it, rooted at the target's focused-pane working directory (the live
  cwd the sidebar row shows); selects + focuses the new session and
  returns its id. Wire: `session.duplicate`. There are NO other options — the target session names both
  the destination workspace
  and the cwd — and `--target` defaults to `active`. It is equivalent to
  `session new --cwd <source cwd> --after <source>` in ONE atomic round-trip.
  ONLY the directory carries over: the duplicate is a plain default shell with the auto basename, and it
  does NOT inherit the source's custom name, `--command`, split, scratch, status, flag, font size, or
  background — it is "new session seeded with the source's cwd", not a clone of state. Errors: the usual
  resolver errors for an unresolvable / ambiguous target, and `could not duplicate session` when creation
  fails. READ-BACK: no new tree field — `tree` itself is the read-back, since the new session node appears
  directly after its source, carrying the source's focused-pane cwd. That equals the source node's
  `tree.cwd` for a non-split session (and a split focused on its primary pane); for a split focused off its
  primary the source node's `tree.cwd` reports the primary pane while the duplicate carries the focused
  pane's directory. It is the control half of the sidebar row's **Duplicate Session** context-menu item
  (single-selection only).
- `session close [--target T ...] [--window W]` — close one session, or repeat `--target` to close
  several sessions in the same window/store. Wire: `session.close`. Batch close honors the grace-undo
  setting (`closeGraceUndoEnabled` in settings.json, default on): one grouped
  undo/reopen record when enabled, immediate close when disabled. Returns `result.affected`.
- `session select [--target] [--window W]`. Wire: `session.select`.
- `session rename <name> [--target] [--window W]`. Wire: `session.rename`.
- `session reveal [--target] [--window W]` — select the target session. Wire: `session.reveal`. On
  Windows it does not open a file manager (the sidebar row menu's "Reveal in Explorer" item does). It
  errors with `session cwd is not an existing directory` when the session's directory no longer exists.
- `session go --to next|prev|first|last|next-attention|prev-attention [--window W]` — move the
  selection relative to the CURRENT one (no `--target`). Wire: `session.go`. Operates over the
  VISIBLE/FILTERED set: the
  flagged sessions in flagged mode, the focused workspace's sessions when a workspace is focused, else
  all sessions (clearing the flag/focus restores the full set). next/prev wrap around at the ends (last→first,
  first→last); first/last jump to the ends of that set; next-attention/prev-attention step only through the filtered
  sessions needing attention (status blocked/completed), wrapping. Returns the newly selected id.
- `session move <workspace> [--target] [--window W]` — relocate the session to another workspace
  (appends). Wire: `session.move`. OR `session move --to up|down|top|bottom [--target]` — reorder
  within its workspace. OR
  `session move --after SID | --before SID [--target]` — place the session directly after / before an
  anchor session (id / unique prefix / `active`). The anchor CARRIES ITS OWN WORKSPACE (resolved across
  all workspaces), so it relocates + positions in one shot, wherever the anchor lives — cross-workspace
  placement falls out for free. Exactly one placement intent is required among {positional workspace,
  `--to`, `--after`/`--before`}; `--after`/`--before` are mutually exclusive with each other, with `--to`,
  and with a destination workspace (the anchor already names the workspace).
  Repeat `--target` for a batch move with the workspace and after/before placement forms; the sessions
  move as one ordered block after all sources are removed. Repeated `--target` is rejected with
  `--to up|down|top|bottom` because relative reorder is per-session. Batch moves return `result.affected`,
  counting only sessions whose position/workspace changed.
- `session type <text> [--stdin] [--select] [--pane left|right|scratch] [--target] [--window W]` — inject text
  as real keystrokes (printable runs plus Return for each newline; no bracketed-paste markers). Wire:
  `session.type`.
  `--stdin` reads the text from stdin instead of the argument. `--select` selects (and realizes) a
  never-shown session before injecting. Any realized session is normally typable without `--select`.
  `--pane left` types into the main pane (the default when omitted), `--pane right` into the split pane
  (errors with `session has no split pane` when the session has no split), `--pane scratch` into the
  session's scratch terminal even while it is hidden (`session has no scratch terminal` when none opened);
  like `session text`, no `other` value. `--select` realizes the MAIN pane only — a split pane must
  already exist.
- `session copy [--target] [--window W]` — returns `result.text` with the session's current selection.
  Wire: `session.copy`.
  Does NOT touch the system clipboard (pipe the returned text into another `session type`). No/empty
  selection → `no selection` error. Selection is readable on any realized session regardless of focus.
- `session paste [--target] [--window W]` — paste the Windows clipboard text into the
  session's main pane. Wire: `session.paste`. An empty clipboard pastes nothing. Read it back with
  `session text`. A never-shown session → `session not realized`.
- `session select-all [--target] [--window W]` — select the session's entire terminal buffer (main pane).
  Wire: `session.selectall`. Read the resulting selection
  back with `session copy`. A never-shown session → `session not realized`.
- `session text [--all] [--lines N] [--pane left|right|scratch] [--target] [--window W]` — returns `result.text`
  with the session's terminal buffer as PLAIN TEXT (no ANSI/color). Wire: `session.text`. By default it
  reads the VISIBLE
  SCREEN of the on-screen pane. `--all` reads the whole buffer including scrollback; `--lines N` reads the
  full buffer and keeps only the last N CONTENT lines (trailing blank rows trimmed; `--all` and `--lines`
  are mutually exclusive and `--lines` must be > 0 — enforced server-side too). `--pane left` reads the
  main pane, `--pane right` the split pane (errors if the session has no split), `--pane scratch` the
  session's scratch terminal even while it is hidden (its buffer is kept alive; `session has no scratch
  terminal` when none opened); omit `--pane` for the visible pane (the scratch terminal when it covers the
  session, else the focused pane). NOTE: unlike
  `session focus`, `--pane` here has NO `other` value — only `left`/`right`/`scratch`. A genuinely BLANK screen is
  NOT an error (returns `ok` with an empty string, unlike `session copy`'s `no selection`), but a failed
  read IS an error (`failed to read surface buffer`). Pipe the text into `grep`/`fzf` to extract URLs,
  paths, etc.
- `session search [needle] [--next|--prev|--close] [--target] [--window W]` — search the target
  session's live terminal scrollback. Wire: `session.search`. Selects the target first (so the search
  bar and match highlights
  render). With a `needle` it sets the query (opening the bar if needed) and highlights matches; with no
  needle and no flag it just opens the empty bar. `--next`/`--prev` step the selected match;
  `--close` closes the bar (the three flags are mutually exclusive). Returns `result.count` (total
  matches) and `result.text` (the counter string: "N of M", "M matches", or "no matches"); the count
  settles asynchronously, so the command waits briefly for it. Without `--json` it prints `result.text`
  (or `ok` on close / an empty bar).
- `session split [on|off|toggle] [--target] [--window W]` — side-by-side second shell. Wire:
  `session.split`. `off` HIDES it
  but keeps the shell alive (mirrors Alt+D); the pane's surface is torn down only when its shell exits.
  Unknown mode errors.
- `session scratch [on|off|toggle] [--command CMD] [--target] [--window W]` — a third, full-coverage
  shell that renders like a full overlay but behaves like the split. Wire: `session.scratch`. `off` hides
  it keep-alive; typing
  `exit` in it closes it and the next `on` spawns a fresh shell. `on` selects the target first (the
  scratch is full-coverage and owns focus). `--command` (only when showing) runs that program as the
  scratch's process instead of the default shell — argv-style (no shell, tokenized like
  `session new --command`: wrap in `"pwsh -NoLogo -Command '…'"` for shell syntax or use an
  absolute path) and RUN-ONCE like `session new --command` (after it exits, the next `on` is a plain
  shell). A scratch is expendable, so passing `--command` while one is already open respawns it. Not
  persisted. Unknown mode errors. The tree's `scratch` flag tracks visibility.
- `session focus [left|right|other] [--target] [--window W]` — move keyboard focus between the two
  split panes (`other` toggles, the default). Wire: `session.focus`. Errors when the session has no split.
  Works whether the
  split is shown side-by-side or hidden (maximized) — when hidden, focusing a pane swaps which one shows.
- `session resize (--split-ratio R | --grow-left D | --grow-right D) [--target] [--window W]` — move the
  split DIVIDER. Wire: `session.resize`. (The divider is otherwise mouse-drag only; there is no
  GUI/menu/keymap action, so bind a
  key by mapping a `command "agtermctl session resize …"` custom action). Provide exactly one form:
  `--split-ratio` sets the absolute left-pane fraction (`0..1`); `--grow-left D` / `--grow-right D` nudge
  it by the fraction `D` (grow-left shrinks the right pane and vice-versa). The result is clamped to
  `0.05..0.95` and persisted, and the applied (clamped) fraction is printed (and returned as `result.ratio`
  under `--json`). Errors when the session has no split. Resizing a hidden split updates the stored
  fraction; it takes effect when the split is next shown.
- `session status <idle|active|completed|blocked> [--blink] [--auto-reset] [--sound NAME] [--color #rrggbb] [--shape circle|square|triangle|diamond|capsule|star] [--pane left|right|scratch] [--pane-id TOKEN] [--target] [--window W]` —
  set the sidebar agent-status glyph. Wire: `session.status`. `--blink` requests an attention pulse.
  `--auto-reset` clears it back to idle once the session
  is visited (use for a one-shot completion flash). `--sound` requests a
  one-shot sound when the status is set: `default` and `beep` play the Windows default sound. Other
  names are accepted on the wire but are not played (the app records a diagnostic instead).
  Without `--sound`, a `blocked` status plays the sound named by `blockedStatusSoundName` in
  settings.json if the user set one; an explicit `--sound` always overrides it. The same sound name
  within 200 ms is played once.
  `--color` (`#rrggbb`) overrides the glyph tint for THIS call only — it rides the status, so the next
  `session status` without `--color` reverts to the configured color (`activeStatusColorHex` and so on
  in settings.json; a malformed hex errors).
  Use it to distinguish states beyond the fixed palette (e.g. a caller-specific blocked color).
  `--shape` (`circle`, `square`, `triangle`, `diamond`, `capsule`, `star`) overrides the glyph SILHOUETTE
  the same way — it rides the status, so the next `session status` without `--shape` reverts to the
  configured shape (`activeStatusShape` and so on in settings.json), else the built-in plain circle (an
  unknown name errors, listing the six).
  Shape is a second channel alongside the tint, so a session stays distinguishable at a glance and for a
  color-blind user; use it to mark one session for the length of a run.
  The value is read back on `tree` as the session node's `statusShape` (the per-call override only, like
  `statusColor`), and rides the `status` control event's `shape` payload field so an `events` consumer can
  explain a shape-only change.
  A `--shape` on `idle` is accepted and ignored, since an idle session draws no glyph.
  `--pane` (`left`|`right`|`scratch`, `left`=main, `right`=split; defaults to `left` when omitted) records
  which pane set the status. When that pane goes away (the split closes, the scratch shell exits),
  agterm-win clears the status. agterm-win does not clear a status on a keystroke. Attention navigation
  (in the GUI or `session go --to next-attention|prev-attention`) only STEPS the selection to attention
  sessions; it does not move focus into the tagged pane. An agent that runs in a split or scratch should
  set its own pane. The value is read back on `tree` as the session node's `statusPane`. An invalid
  value errors (`--pane must be left, right, or scratch`).
  `--pane-id` is the surface's stable spawn token (the shell's `$AGTERM_PANE_ID`) — the agent-status hook
  forwards it automatically. When it resolves against the session's LIVE surfaces it OVERRIDES `--pane`, so
  a status from a pane whose baked role went stale (a split survivor promoted into the main pane, then a
  re-split) lands on the pane's CURRENT slot instead of the stale role; an absent/unknown token falls back
  to `--pane`. Scripts normally set `--pane` directly and leave `--pane-id` to the hook.
  An unknown state errors. Setting non-idle is for agents/hooks; `idle` clears it (also available in the GUI).
- `session flag [on|off|toggle|clear] [--target] [--window W]` — flag/unflag a session for the flagged
  working-set view (a durable, persisted membership). Wire: `session.flag`. `on`/`off`/`toggle` act on
  `--target` (default
  `active`) and are idempotent; `clear` ignores the target and unflags every session in the window.
  Pair with `sidebar mode flagged` to see just the flagged sessions as a flat `session : workspace`
  list. Unknown mode errors. The tree's `flagged` flag tracks membership.
- `session seen [--target] [--window W]` — clear the session's unseen-notification badge without changing
  the selection, focus, or agent status. Wire: `session.seen`. It is the focus-free counterpart to
  `notify`: `notify` raises the red badge, and the other way to clear it is visiting the
  session. Idempotent — a no-op when the badge is already zero. Read the current count from the tree node's
  `unseen` field. This lets an orchestrator acknowledge a driven session's notifications over the pipe
  while keeping the badge a real attention signal on the sessions a human tends.
- `session restore (<command> | --none | --clear) [--pane left|right] [--pane-id TOKEN] [--target] [--window W]`
  — pin the command a pane re-runs on the NEXT launch. Wire: `session.restore`. Provide exactly
  one of: a `<command>` shell line to pin, `--none` to pin nothing (the pane restores a plain shell),
  or `--clear` to drop the override.
  Tri-state, read back on the tree node as `restoreCommand` (main pane) / `splitRestoreCommand` (split
  pane): omitted = no override, `""` = pinned to nothing, a command = the pinned line.
  The override is written NOW and consumed on the next launch — it never touches the running session — and
  it is STICKY: it fires again on every restart until cleared. It wins over EVERYTHING else the pane could
  restore, including the session's own `session new --command`, which a pinned line (or
  `--none`) suppresses — so a restored `--command` session runs the pinned line instead, as typed input
  rather than the exec path, and its `--wait` close-on-exit behavior no longer applies.
  It is gated on `restoreRunningCommand` in settings.json, read when a window opens (a pinned command while
  the setting is off succeeds with a note in `result.text` that it will not run; `--none`/`--clear` get no
  note, since their outcome is delivered either way). It is typed
  verbatim as one line into the pane's shell (pwsh or cmd), so `cd x && claude --resume y` works as
  written. A line that contains control characters is not typed.
  A split HIDDEN at quit is not restored, so its pin is dropped on that launch rather than left to fire
  into a later manual split.
  It exists for NON-IDEMPOTENT commands — `claude --resume <id> --fork-session` mints a NEW session on every
  restart, so restoring it verbatim never reattaches the session the user was in. A Claude Code
  `SessionStart` hook that rewrites the override to the live session id on every start makes the next
  restart reattach instead of fork (see examples.md). Ownership flips to whoever sets it: write once and
  forget, and it stays pinned to a stale id — that is the deliberate hook-driven tradeoff.
  `--pane` (default `left`) picks the pane; `--pane right` errors when the session has no split, and
  `scratch` is rejected (`the scratch terminal is never restored`). `--pane-id` (the shell's
  `$AGTERM_PANE_ID`) resolves the pane's LIVE slot, so a hook in a promoted-then-re-split pane still pins
  the right one; UNLIKE `session status`, a token that does not resolve is an error unless `--pane` is also
  given as the fallback (a silent main-pane default here would overwrite the wrong pane).
  The pinned value is SHELL CODE: it persists in the window's state file (`windows/<id>.json`), is readable
  via `tree`, and may enter shell history when it runs — so it must not carry secrets, and only
  safely-interpolated values (a UUID-shaped session id) belong in it. It persists immediately, so a
  force-quit does not lose a hook's write; if that write fails the command answers with an ERROR rather
  than `ok` (the previous override is still in effect and still fires, so re-issue the same request to
  retry). Not to be confused with the app-global `restore clear`, which
  clears every session's persisted captured foreground command (agterm-win captures none itself, so only
  ones loaded from an older snapshot); this is per-session and clears only the override.
- `session background image <path> [--opacity F] [--fit contain|cover|stretch|none] [--position P] [--repeat] [--target] [--window W]`
  — set an image watermark behind the terminal. Wire: `session.background` (all four forms).
  `--opacity` is 0.0–1.0 (default 1.0);
  `--fit` defaults to `contain`; `--position` is `center` (default) or an edge/corner anchor
  (`top-left`, `top-center`, `top-right`, `center-left`, `center-right`, `bottom-left`, `bottom-center`,
  `bottom-right`); `--repeat` tiles to fill blank space. Errors on a bad fit/position, an out-of-range
  `--opacity` (must be 0.0–1.0), a path that does not end in `.png`, `.jpg` or `.jpeg`
  (`unsupported image (PNG or JPEG only): <path>`), a missing file, or a path containing control
  characters.
- `session background text <text> [--color #rrggbb] [--opacity F] [--fit ...] [--position ...] [--target] [--window W]`
  — set a text watermark behind the terminal. `--color` must be a `#rrggbb` hex value;
  `--opacity`/`--fit`/`--position` as above. `text` is capped at 256
  characters (a watermark is a word or two).
- `session background color <#rrggbb> [--target] [--window W]` — set a SOLID terminal background color.
  Takes no opacity. Errors on a malformed color
  (must be a `#rrggbb` hex value).
- `session background clear [--target] [--window W]` — remove the session's background.
  Per session; persisted, so it survives a relaunch. agterm-win validates, stores and reports the
  background, but the window does not draw it: the panes keep the theme background. Read the current
  background back from a session's `background` field in `tree --json` (a `{kind, colorHex, …}` object,
  omitted when none).
- `session overlay open <command> [--cwd DIR] [--wait] [--block] [--size-percent N] [--background-color #rrggbb] [--follow] [--target] [--window W]`
  — run `command` in an ephemeral terminal on top of the session; it closes when the command exits.
  Wire: `session.overlay.open`.
  `command` runs through `%COMSPEC% /c` (cmd.exe), so cmd operators (`&&`, `|`, `>`) DO work here. For
  PowerShell syntax, wrap it: `"pwsh -NoLogo -Command '…'"`. A command that is not found makes cmd
  exit non-zero — the overlay flashes open then vanishes and `overlay result` reports that code.
  Full-size by default (hides the session); `--size-percent N` (1–100) makes it a floating framed panel
  with the session visible behind. **By default the overlay does NOT switch the active session** — full
  and floating both open on `--target` and run their program in the background, appearing when the user
  visits that session. **Pass `--follow` to select the target after opening** (a no-op if it is already
  active); use it when you want the user pulled to the overlay, omit it to open quietly. `--background-color #rrggbb` gives the overlay pane its own solid
  background color, independent of the session's own `session background color` (nil = the default theme
  background). `--wait` keeps the overlay open after the command exits (close it with
  `session overlay close`). `--block` waits for the command to exit and makes agtermctl exit with the
  command's status
  (cannot combine with `--wait`); the program renders normally — capture its OUTPUT via the program's
  own output file, not the control channel. Returns the overlay's session id. `--target` defaults to
  `active`, so an automated caller should pass `--target "$AGTERM_SESSION_ID"` — otherwise a (usually
  blocking, full-pane) overlay lands on whatever session is currently active, not the calling one.
- `session overlay resize (--size-percent N | --full) [--target] [--window W]` — resize an ALREADY-OPEN
  overlay in place. Wire: `session.overlay.resize`. Exactly one of `--size-percent N` (1–100, makes it a
  floating framed panel) or
  `--full` (switches it back to the full-pane overlay that hides the session) is required; passing both
  or neither, or a percent outside 1–100, is an error. The overlay program keeps running across the
  resize — it is a layout re-flow, never a re-spawn. Errors `no overlay` when none is open. Returns the
  session id.
- `session overlay close [--target] [--window W]` — close (destroy) the overlay. Wire:
  `session.overlay.close`.
- `session overlay result [--target] [--window W]` — returns `result.exitCode` once the overlay has
  closed. Wire: `session.overlay.result`. Errors `overlay still running` while up, `no overlay result`
  if none ran.

- `session hud [open] <message>|--file FILE [--markdown] [--font-size PT] [--detail T] [--spinner] [--spinner-style S] [--position P] [--background-color #rrggbb] [--text-color #rrggbb] [--size-percent N] [--sticky] [--no-frame] [--hide-after SECONDS] [--pane P] [--pane-id ID] [--target] [--window W]`
  — post a PASSIVE message panel over the session and return its id. Wire: `session.hud.open`.
  The panel is an HTML layer in the terminal page. It occupies the same session-wide slot
  as `session overlay open`, but carries a message rather than a program: it takes no input, the session
  keeps keyboard focus and stays typable, and the terminal behind it is neither dimmed nor click-blocked.
  Meant for the seconds before an agent can show anything — computing the items for `pick`, waiting on a
  slow command — so the user reads what is happening in the session he is about to be pulled into.
  `open` is the group's default subcommand (`session hud "gathering options…"`); a message that is
  literally `update` or `close` needs the explicit `session hud open` verb. `--detail` adds a dim second line,
  `--spinner` animates a glyph beside the message in the default `bar` style, `--spinner-style` picks
  another from `bar|braille|circle|blocks|dot` and turns the spinner on by itself — `dot` blinks rather than
  animating, for a panel that sits up for minutes. `--spinner-style none` is accepted too and leaves the
  panel static, so the `none` a read-back reports can be echoed straight back; an unknown name is refused
  `invalid spinner: <value> (bar|braille|circle|blocks|dot|none)`. `--position` anchors the panel to one of
  the nine `top-left|top-center|top-right|center-left|center|center-right|bottom-left|bottom-center|bottom-right`
  (default `center`), the same anchors `session background` takes; every anchor off center holds a fixed
  margin off that pane edge on each axis it names, so a panel at the largest allowed size never overhangs.
  `--hide-after SECONDS` makes the panel take itself down; omitted or 0 leaves it up until something closes
  it, and anything outside `0...86400` is refused rather than clamped. Every successful open or update restarts the interval and an omitted value cancels it, like every other
  option an update replaces rather than patches, while a refused write leaves the live panel's own deadline
  alone. The clock is elapsed lifetime rather than viewing time: it runs while the session is unselected, its
  pane hidden or its window minimized, and expiry closes the panel without selecting anything. A session
  closed with undo still pending takes a TIMED panel down with it; one posted without `--hide-after` comes
  back with the session. `hud.hideAfter` reads back the configured seconds, 0 for a panel that stays, never
  a countdown.
  `--pane primary|left|top|split|right|bottom` makes the selected pane the bounds for measurement, explicit
  size, anchor, and margin. `--pane-id` takes the shell's stable `$AGTERM_PANE_ID`; a live token overrides
  `--pane`, while an unknown token uses that role as fallback or errors without one. The stored identity follows
  pane swap and promotion. Open refuses a pane that is not rendered. Hiding the target suppresses the panel,
  and showing it restores the panel. Destroying the target closes the HUD.
  A corner is what keeps a long-lived panel out of the text the user is reading. The bare `top`/`bottom`
  this argument shipped with are still accepted for `top-center`/`bottom-center`, and `hud.position` reports
  the canonical anchor whichever spelling was sent. The panel is measured from the message against its own font on BOTH
  axes separately — width from the longest wrapped line, height from the number of them — so a title and a
  subtitle give a wide, short panel rather than a square one. `--size-percent N` (1–100) overrides the WIDTH
  only; the height always follows the message, since a caller-set height could only strand it in an empty
  box. The effective width is bounded to 10–80% of the pane, or up to 100% with `--sticky` off center, and
  a HUD cannot be made full-size, so without it a requested 100 reads
  back as 80. Both effective shares read back, as `hud.sizePercent` and `hud.heightPercent`. The height
  follows the rendered rows, capped at 80% of the pane, so a pane narrowed until a line wraps gains that row.
  Markdown wraps at the panel's text width once a width is set, by `--size-percent` or by
  `session overlay resize`, and at 60 columns at most while the app measures the width.
  `--sticky` drops the edge margin, so the panel sits flush against the edge or corner `--position` names
  (`center` has no edge and ignores it); it does not force full width. `--no-frame` draws no border, no
  rounded corners and no blank row above and below the text, keeping the opaque backing. Together they make
  a caption, a strip across the top as tall as its text:
  `session hud --markdown --sticky --no-frame --position top --size-percent 100 "**deploy api** [PR](https://…) · [docs](https://…)"`.
  It is still the one transient slot: `session overlay open` closes it, a restart drops it, and zoom, the
  dashboard and the quick terminal hide it, so a script re-posts it after a restart. A frameless panel is
  its rendered rows plus 12 pixels of padding tall. Both read back, as `hud.sticky` and
  `hud.frame`, and an update that omits either returns the panel to its margin or its frame. `--background-color #rrggbb` gives the panel its own solid
  background, read once when the panel is created; `--text-color #rrggbb` colors the TEXT and, unlike the
  background, an update can change it. Both read back, as
  `hud.backgroundColor` and `hud.textColor`. Message and detail are capped at 256 characters and
  reject control characters — newline included, since the panel prints straight into a live terminal and
  `--detail` is the second line on offer.
  `--markdown` renders the message as standard markdown (CommonMark plus pipe tables, parsed with Markdig):
  headings, bold, italic,
  strikethrough, nested lists, code blocks, block quotes, rules and tables; an image shows its
  alt text and raw HTML stays literal. A link shows its label, underlined when a Ctrl+click opens it:
  `http`, `https`, `mailto` and `ftp` open with the system handler, a local `file://` link (empty or
  `localhost` host) selects the file in Explorer, and a link to anything else is its plain label. A link
  URL must be printable ASCII as written; percent-encode spaces and non-ASCII characters. That Ctrl+click
  is the one click the panel takes, and it moves no focus. It raises the message cap to 4096 characters
  and allows newlines and tabs
  in it; every other control character is still refused and the detail keeps the plain rules. Markdown
  semantics apply: a single newline inside a paragraph is a space, so end a line with two spaces or a
  backslash, or use list items, to keep rows apart; lists always render tight. Text wraps as the sizing
  paragraph above says, while table rows stay intact, and the rows sit left-aligned as one block. What does not
  fit the panel is clipped: a table row too wide ends in `…`, and rows past the panel's height give way to a
  dim `… N more`, itself clipped in a narrow panel. A table is framed in box-drawing borders with a rule under its header; trailing
  all-empty table rows and an all-empty header row are not shown, the latter leaving no header rule.
  A markdown message that renders nothing visible is refused like an empty one.
  `--file FILE` reads the message from a UTF-8 file instead of the argument, exactly one of the two, once per
  command (nothing watches the file), dropping a leading BOM and one trailing newline. `agtermctl` reads it
  before sending and
  fails there with `cannot read --file <path>: <reason>` or `--file <path> is not valid UTF-8`; passing both or
  neither fails with `MESSAGE and --file are mutually exclusive` or `provide MESSAGE or --file`, and every cap
  still applies to what is sent. `--font-size PT` (6–72, drawn as CSS pixels) sets the
  panel's own font, used for its text and its measurement; it is fixed for the panel's life, and omitting
  it uses the session's size at open. A window resize or divider drag re-measures the panel by itself.
  Errors `session.hud.open requires a message` on a missing or
  empty message, `hud text must not contain control characters`, `hud message too long (max 256
  characters)` (4096 with `--markdown`) / `hud detail too long (max 256 characters)`,
  `font-size must be 6...72 points` from the CLI (`session.hud.open: --font-size must be 6...72 points` from
  the raw protocol), `invalid color: <value> (#rrggbb)`,
  `invalid text color: <value> (#rrggbb)`,
  `invalid position: <value> (top-left|top-center|top-right|center-left|center|center-right|bottom-left|bottom-center|bottom-right|top|bottom)`,
  `invalid spinner: <value> (bar|braille|circle|blocks|dot|none)`,
  `--pane must be left or right`, `hud pane must be left or right` when a pane ID resolves to scratch,
  `pane not visible`, `unknown pane id: <token>`,
  and `session.hud.open: --size-percent must be 1...100`.
  A second `hud` replaces the first; a `session overlay open` replaces a HUD, while a HUD over a RUNNING
  program is refused with `overlay already open` — a message is replaceable, a program is not.
- `session hud update <message>|--file FILE [--markdown] [--detail T] [--spinner] [--spinner-style S] [--position P] [--text-color #rrggbb] [--size-percent N] [--sticky] [--no-frame] [--hide-after SECONDS] [--pane P] [--pane-id ID] [--target] [--window W]`
  — repaint the live panel in place: no re-spawn, no blink, the panel does not flicker. Wire:
  `session.hud.update`. It REPLACES the
  whole spec rather than patching it, so `--detail`, the spinner, `--position`, `--text-color`, and pane selectors must be
  repeated to survive and an omitted one drops. `--spinner-style` may name a DIFFERENT style than the panel
  opened with, and `--text-color` a different color; the look changes in place. It shares `open`'s message,
  text, color, position, spinner, and pane-spelling
  validation. Pane lifecycle differs: update accepts a hidden target, while a missing split errors
  `session has no split` instead of `pane not visible`. There is no `--background-color`: the panel reads
  that once at creation, so only a fresh `session hud` can change it, and `tree` keeps reporting the creation
  color across updates. The same holds for the font: `update` takes no `--font-size`, and a raw protocol
  update carrying `fontSize` is refused with
  `session.hud.update: --font-size is fixed at open; reopen the hud to change it`. `--markdown` must be repeated
  like every other option, or the panel returns to plain text. Errors `no hud` when none is up.
- `session hud close [--target] [--window W]` — take the panel down. Wire: `session.hud.close`. Errors
  `no hud` when none is up, so it is not idempotent. A program overlay in the same slot is left alone;
  `session overlay close` also takes a HUD down. Closing the session with
  Alt+W is a soft close: a HUD with `--hide-after` closes, one without stays and comes back on undo.

## window

- `window new [name]` — create and open a window; returns its id. Wire: `window.new`.
- `window list` — `result.windows`, each with `id`, `name`, `open`, `active`, `autoFollowMs` (the
  window's Auto-follow timeout in milliseconds, omitted when Auto-follow is disabled), and
  `sidebarVisible` (whether that window's sidebar is shown, read from the open window's store — omitted
  for a closed window with no live store), and `geometry` (the open window's live frame `{x, y, width,
  height, display}` in the SAME units `window move`/`window resize` take — `x`/`y` top-left relative to
  `display`, y down — omitted for a closed window; the read side of `window move`/`window resize`, so
  record it, move/resize, then restore the exact frame), plus `fullscreen` and `zoomed` (whether the
  window is full screen / maximized — the read side of `window fullscreen` / `window
  zoom`, so a script can make those toggles idempotent; both omitted for a closed window). Wire:
  `window.list`. The
  `geometry`/`fullscreen`/`zoomed` fields come only from open windows, so a hand-drag or a GUI toggle is
  reflected without needing another command. Unlike `tree`, `window.list` does NOT carry `idleMs`.
- `window select <id>` — raise it if open, else open it. Wire: `window.select`. Windows may refuse to bring the window to the foreground for a background caller; then the taskbar button flashes, and the window still counts as frontmost for routing. Activation clears the unseen count of the session whose main or split pane has accepted focus, including a zoomed pane or highlighted dashboard cell. That session need not be selected. Other sessions keep their counts. The command waits for this policy before replying.
- `window close <id>` — close the on-screen window (the record is kept; reopen with select). Wire:
  `window.close`. Closing the last open window exits agterm-win after it answers; the record is kept as
  closed and the next launch reopens it.
- `window rename <id> <name>`. Wire: `window.rename`.
- `window delete <id>` — keep-at-least-one; deleting the last errors. Wire: `window.delete`.
- `window resize <id> --width W --height H` — frame size in logical points. Wire: `window.resize`. The
  window must be open. The size is
  clamped into `[320×240, the display's work area]`, so an oversized or under-min request is
  bounded to fit rather than applied verbatim.
- `window move <id> --x X --y Y [--display N]` — top-left position in logical points, relative to the full
  bounds of display `N` (default the window's current display; y measured from the display top). Wire:
  `window.move`. The window must be open. The
  origin is clamped so an off-screen request keeps an 80-point strip of the window on the target display.
- `window zoom <id>` — toggle the window between its normal frame and a maximized (fill-screen, NOT
  full screen) frame. Wire: `window.zoom`. A second call restores the prior frame.
  The window must be open. This is the control half of the title bar's maximize button.
- `window fullscreen <id>` — toggle full screen (no title bar, no taskbar). Wire: `window.fullscreen`. A
  second call exits. The window must be open. This is the control half of the `toggle_fullscreen`
  built-in action (Alt+Return, rebindable) — distinct from `zoom`, which only maximizes the frame.

`window resize`/`move` are control-native (no GUI equivalent — the title bar already drags-to-resize).

## surface

Entering zoom or changing its target ends search in the window's active session, including when zooming the quick terminal or a background session. Zooming a background session also ends that session's search. Leaving zoom does not reopen either search.

`agtermctl surface zoom [show|hide|toggle] [--target SURFACE_ID|active|quick] [--window W]` — zoom one
terminal surface to fill the window's terminal area. Wire: `surface.zoom`. `SURFACE_ID` comes from
`agtermctl tree --json` at `.result.tree.workspaces[].sessions[].surfaces[].id`, for example
`surface:<session-id>:right`. Omit `--target` (or pass `active`) to act on the active surface in the
frontmost or `--window` window; `quick` addresses a quick-terminal zoom (the id the command itself
returns when the quick terminal is the zoom target).

`show` is idempotent; `hide` exits zoom and is idempotent too (when an explicit id is provided, it
only clears that same zoom target, and succeeds as a no-op even if that surface has since vanished);
`toggle` enters when unzoomed and exits when that surface is already zoomed. Read the current zoom
back from the tree's top-level `zoomedSurface` (the zoomed surface's control id, omitted when nothing
is zoomed). This is NOT
`window zoom`: it does not change the window frame, and it does not change split ratios, focus,
sidebar state, or split/scratch visibility. While zoomed, the hidden deck keeps running:
`session.split`/`session.scratch`/overlay opens on the zoomed session still spawn their shells behind the
zoom layer. With a pick pending in the window, turning a zoom on answers `pick pending`; with a window
question (`ask --style gui`) pending it answers `ask pending`. Use `surface zoom` when the user/agent
needs a pane fullscreen inside agterm-win; use `window zoom` only to maximize the whole window on screen.

## dashboard

`agtermctl dashboard <ids…> [--font-size N | --auto-size] [--window W]` opens a per-window, view-only
grid of the named sessions' live panes; `agtermctl dashboard --mru [--font-size N | --auto-size]
[--window W]` opens the window's most-recently-used sessions instead of naming ids; `agtermctl dashboard
--close [--window W]` closes the open one. The cell unit is a session+pane: a non-split session is ONE
cell, and a SPLIT session shows as TWO cells — its left/primary pane and its right/split pane. The
positional ids are session addresses (id / unique prefix / `active`); unresolved ids are dropped and ids
are deduped by resolved session. The 9-cell cap counts PANES (laid out `ceil(sqrt(n))`), applied after
each session expands into its pane cells: if the panes exceed 9 the first 9 are kept and the dropped-pane
count is reported in the response text (`dropped N pane(s) beyond the 9-cell limit`, appended to any
`unresolved:` note with `; `). `--window` targets a specific window's dashboard (default: the frontmost).
`--mru` draws its members from the window's recency (most-recent first); it is mutually exclusive with
explicit ids and `--close`, composes with the font flags and `--window`, and errors with `no recent
sessions` when the window has none.

The most-recently-used grid also has a GUI opener — **Ctrl+Shift+D** (the `dashboard` built-in action,
rebindable in `keymap.conf`), the title bar's **Dashboard** button, and the command palette's
**Dashboard** entry all TOGGLE the
frontmost window's dashboard: open it over the window's most-recently-used sessions auto-sized (identical to
`dashboard --mru --auto-size`) when closed, close it when open. It is a no-op while terminal zoom is active.
There is no new control command for it — the control `dashboard` command is unchanged. Wire:
`dashboard` (open, `--mru` and `--close` alike).

It is **view-only**: no cell forwards keyboard or mouse input to its program — the whole grid shows live
output, and once open the keyboard drives it. A click on a cell highlights it and then jumps into it like
Enter. Arrow keys move a highlight between cells (2-D, no wrap; clamped into a
ragged last row), Enter jumps into the highlighted session AND focuses that exact pane (selecting the
session, focusing the primary pane for a `:left` cell or the split pane for a `:right` cell, then closing
the dashboard), and Esc closes it (leaving the selection as it was). Because a cell forwards no input, a
program you dashboard keeps running but you cannot type into it from the grid — jump in with Enter first.

Font size is optional and mutually exclusive: `--font-size N` sets an absolute cell font in points
(must be finite and positive), while `--auto-size` sizes the cells relative to the settings.json
`fontSize`, shrinking as the grid grows so a dense 3×3 stays readable. Omit both to leave each pane's own
font untouched. The applied size and mode read back on the tree's top-level `dashboardFontSize` /
`dashboardFontMode`; the member pane refs and the highlighted cell read back on `dashboardMembers` /
`dashboardHighlighted` (each a `<session-id>:left`/`<session-id>:right` pane ref).

The dashboard and terminal zoom are **mutually exclusive**: opening a dashboard closes any active zoom,
and a zoom becoming active while the dashboard is open closes the dashboard. Opening (and closing) the
dashboard resizes each pane's pty to (and back from) its cell, so a running program receives a resize
event and may redraw — "view-only" means no input reaches the cell, not that the pane's process is
untouched.

Invalid invocations error (rejected at the CLI and re-checked server-side): `--font-size` with
`--auto-size`, a non-positive `--font-size`, `--close` combined with ids, `--mru`, or a font option,
`--mru` combined with explicit ids, and an open with neither ids nor `--mru`.

## pick

`agtermctl pick [--prompt TEXT] [--query TEXT] [--select ID] [--allow-custom] [--follow] [--window W] [--no-block]`
reads choices from stdin and opens a fuzzy picker centered in the target window. Wire: `pick.open`.
`pick` defaults to the open
subcommand, so `agtermctl pick open` is not required. Stdin is read unconditionally, so a call that supplies
no items needs `< /dev/null` or it blocks. Stdin that is not UTF-8 text or a JSON item array fails with
`stdin must be UTF-8 text or a JSON item array` (exit 1); a leading BOM is dropped. With no open window
the open fails with `no open window`.

The first non-whitespace input byte selects the format. `[` starts a JSON array of objects with required
`id` and `label` strings plus an optional `subtitle`; any other input is split into lines, blank and
whitespace-only lines are dropped, and each remaining line becomes both the id and label. Item ids must
be unique, labels must not be empty, labels and subtitles may not contain control characters, and a
picker accepts at most 1,000 items. An empty list is rejected with `pick.open requires at least one item`
unless `--allow-custom` is set, which accepts it and opens a plain text prompt. Omitting `items` altogether
is rejected with `pick.open requires items`, reachable only over the raw protocol: the CLI always sends the
list it parsed, empty or not.

The query matches item labels only; a subtitle is displayed but never searched, so consequence text on one
row cannot filter out its safer neighbour. An empty query lists the items in the order the caller supplied
them, so without `--select` the first item is the one Return runs on open.

`--prompt` sets the query field's placeholder text. `--query` prefills it and filters on open, which ranks
by match score and so does not preserve the supplied order; the seeded text opens selected, so the first
keystroke replaces it rather than appending. `--select ID` opens with that item highlighted and scrolled
into view, so Return on an untouched picker runs it and Up/Down read relative to it; the id must name a
supplied item (`pick select must name an item id` otherwise, an `--allow-custom` empty list included), and a
`--query` that filters it out leaves the first visible row highlighted. The seed is consumed at open and
has no tree read-back; the result's `id` and `index` report what was picked. `--allow-custom` adds a row for a nonmatching
query and returns it as a custom result; with an empty item list that row is the only possible one, and it
appears as soon as the query is nonblank, prefilled or typed; whitespace and newlines are trimmed first.
A background `--window` target is raised only with `--follow`. Pick shares its window modal slot with
GUI asks. A competing open fails with `pick already pending`, or `ask already pending` when a GUI ask
owns the slot. Terminal asks use separate session slots.

The default call polls until the user answers and prints one bare JSON result:

```json
{"result":"picked","id":"production","label":"Production","index":1}
{"result":"custom","query":"new target"}
{"result":"cancelled"}
```

Picked and custom results exit 0. Cancellation exits 2. Protocol, validation, transport, and server
failures exit 1. The blocking client polls every 100 ms for the first second, then every 500 ms.

`--no-block` returns immediately with `{"id":"<pick-id>"}`. Use
`agtermctl pick result <pick-id> [--window W]` for a one-shot read; it prints the same bare result JSON,
including `{"result":"pending"}`, and exits 1 while pending. Wire: `pick.result`. Use
`agtermctl pick cancel <pick-id> [--window W]` to cancel it. Wire: `pick.cancel`. Result and cancel
require the exact picker
id. Without `--window`, the globally unique id keeps result/cancel pinned to its owning window even if
the frontmost window changes. With `--window`, a live or close-retained result must belong to that
window. A wrong id returns `unknown pick: <id>`.

Read the live picker id from the tree's top-level `pickPending` field. It is omitted after selection,
custom input, cancellation, or window closure. Esc, closing the picker, closing its window, and Alt+W
(`close_session`) resolve it as cancelled. When agterm-win exits, a blocking client may observe a
transport failure instead of the final cancellation.
A terminal result stays readable by its own id after the next picker opens in that window, and after the
window closes — including permanent window deletion — so a blocking caller always reads back the answer
it waited for. Results age out oldest-first: the 8 most recent per open window, and 32 across closed ones.

## ask

`agtermctl ask [open] TITLE --button ID=LABEL [--button ...] [--message TEXT]` opens a question without
reading stdin. Wire: `ask.open`. Use explicit `ask open` when the title is `open`, `result`, or `cancel`.

`--style terminal` is the default and uses the window's selected session, with one pending ask per
session. It is drawn inside the terminal page over the pane or the whole session, and it hides while the
quick terminal is shown. `--target` can name an unselected session without selecting it; its ask waits hidden.
`--pane left|right` and `--pane-id TOKEN` narrow placement without requiring a target. A live token takes
precedence over the role, and the pane must be laid out by its session at open.
`--style gui` uses the window modal slot shared with pick. Without a target, it centers over the
terminal area, excluding the sidebar. An explicit GUI target must be selected in its window;
GUI pane selectors require it. Terminal asks can coexist with GUI asks and picks.
`--window W` selects the window; `--follow` raises it without changing session selection.

Supply one to six buttons with unique ids and nonempty labels. A token without `=` is both id and label;
otherwise the first `=` separates them. Title, message, and labels reject control characters.
`--default ID` sets the initial highlight; otherwise the first non-destructive button is highlighted,
or the first button if it is the only choice. Tab/arrows move the highlight, Return answers, and
`--hotkey ID=LETTER` assigns a unique ASCII letter shortcut; it matches the key at that Latin letter's
position, so it also works on a non-Latin keyboard layout. `--destructive ID` cannot name the default.
`--align left|center|right` defaults to right. `--width N` fixes the width to 10...100 percent of the
region; omitted means content sizing. Invalid style, alignment, and width return `unknown style`,
`unknown align`, and `width must be 10 to 100` respectively.

The blocking call prints one bare result:

```json
{"result":"answered","id":"yes","label":"Yes","index":0}
{"result":"escaped"}
{"result":"cancelled"}
```

Index follows caller order. An answer exits 0, including a named No button; inspect `id` before acting.
Esc on a question, or Alt+W (`close_session`) over a GUI question, returns `escaped` with exit 3.
Cancellation exits 2; failure exits 1.
`--no-block` prints `{"id":"<ask-id>"}`. `ask result ID [--window W]` prints the current or finished result,
including `{"result":"pending"}` with exit 1. Wire: `ask.result`. `ask cancel ID [--window W]` cancels a
pending question and
returns `ok`; cancelling a retained finished result is a successful no-op. Wire: `ask.cancel`. Both
commands use the exact
global id and reject a mismatched explicit window.

Session nodes expose terminal asks as `ask: {id, pane?}`; `pane` follows the current left/right role and
is omitted for
session-wide placement. Top-level `askPending` is the window's GUI slot. Resolution removes the field
for that slot. The raw open reply echoes `result.pane` for pane placement. The latest 32 finished results
are retained across both styles, including after owner closure; pending requests are never evicted.
App shutdown can interrupt polling. Ask emits no events.

## quick

`agtermctl quick [show|hide|toggle]` — the frontmost window's quick terminal (a single scratch
terminal layer over 90% of the window, not in the tree; its shell stays alive across hides). Wire:
`quick`. The default chord is Alt+\` (`quick_terminal`). Read its visibility back from the tree's
top-level `quickVisible`.
While terminal zoom is active, `show` errors with `terminal zoom active`; with a pick pending it errors
with `pick pending`. `hide` always succeeds (a
zoomed quick terminal exits its zoom first), so cleanup scripts can dismiss it unconditionally. The quick
terminal's shell gets only `AGTERM_ENABLED`, `AGTERM_WINDOW_ID` and `AGTERM_SOCKET`.

`agtermctl quick type TEXT` (or `--stdin`) — inject `TEXT` as literal keystrokes into the frontmost
window's quick terminal, the quick-terminal twin of `session type`. Wire: `quick.type`. There is no
`--target`/`--window`
(always the frontmost window's quick terminal) and no `--pane` (a single surface). It polls briefly for
the surface to come up, so `quick show; quick type` back-to-back is reliable (the layer mounts a beat
after `quick show` flips visibility). Errors with `quick terminal not open` when the layer has never
been shown, `quick terminal not realized` if a shown surface never comes up in time. Typing into a
shown-then-hidden quick terminal still works (its shell stays alive).

`agtermctl quick text [--all] [--lines N]` — print the frontmost window's quick-terminal buffer as
plain text (the read-back for `quick type`; does not touch the system clipboard). Wire: `quick.text`.
`--all` reads the
full screen + scrollback, `--lines N` keeps only the last N (mutually exclusive). Polls for the surface
like `quick type`. Errors with `quick terminal not open` (never shown), `failed to read surface buffer`
(shown surface never realized in time).

## sidebar

`agtermctl sidebar [show|hide|toggle]` — show/hide the frontmost window's workspace/session sidebar.
Wire: `sidebar`. `toggle` is the default; an unknown mode is an error. The GUI half is the title-bar
button, the Ctrl+Shift+P palette "Toggle Sidebar", and the Alt+S keymap action (`toggle_sidebar`).

`agtermctl sidebar mode [tree|flagged|toggle]` — flip the frontmost window's sidebar VIEW between the
workspace tree and the flat flagged working-set list (the durable per-session `flag`; each flagged row
is labeled `session : workspace`, even across workspaces). Wire: `sidebar.mode`. `toggle` is the
default; idempotent
(delta-computed); an unknown mode is an error. Persisted
per-window. While in `flagged` mode, `session go` navigation (and the Ctrl+Tab MRU switcher) is scoped
to the flagged sessions only; back in `tree` it spans the focused workspace's sessions (when focused)
or all sessions. The GUI half is the sidebar's flagged-view button and the Ctrl+Shift+P palette
("Show Flagged Sessions" / "Show All Sessions"). Use with `session flag` to build and view a
cross-workspace working set.

`agtermctl sidebar expand [--window W]` — expand every workspace row in a window's sidebar tree.
Wire: `sidebar.expand`.
Defaults to the frontmost window; `--window` (id / prefix / `active`) targets any OPEN window, so a
script can expand a background window's tree. Idempotent (a clean no-op when all are already expanded);
a graceful no-op in `flagged` mode (no workspace rows); a named-but-closed window errors.

`agtermctl sidebar collapse [--window W]` — collapse every workspace EXCEPT the active one (the
workspace of the active session), which stays expanded and is scrolled into view. Wire:
`sidebar.collapse`. Same `--window`
selector and defaults as `expand`. Idempotent; a graceful no-op in `flagged` mode; a named-but-closed
window errors.

## notify

`agtermctl notify <body> [--title T] [--target] [--window W]` — post a Windows desktop notification
attributed to a session (default: the active session of the frontmost window). Wire: `notify`.
`--title` defaults to
the session name. agterm-win does not handle a click on the banner, so the click does not select the
session. This is the only way to post a banner. Control-native (no GUI/menu equivalent). The banner is a
Windows App SDK app notification; when the app runs elevated, system notifications are disabled by
policy, or registration fails, an InfoBar in the session's window shows it instead. The session's window
must be open (`session's window is not open` otherwise).

The banner is gated by settings.json `notificationsEnabled` (a banner is posted unless it is `false`);
the unseen badge is
not. With banners off the command still succeeds and still raises the badge, but no banner is posted
— so it answers `ok` with an advisory `result.text` (`badge updated, but "Show notification banners" is
off, so no banner was posted`) instead of a bare `ok`. Treat the presence of `result.text` as "no banner
appeared"; a delivered notification carries none. The badge itself reads back on `tree` as `unseen`;
settings.json `notificationBadgeEnabled` only hides it in the sidebar.

Returning to a window applies the same badge policy as `window select`, even when no other agterm-win window was activated in between. The policy clears the badge of the session whose main or split pane has accepted focus, without a session switch. An overlay, scratch, or quick pane clears no session badge. Zoom and dashboard panes follow the same owner check even when their session is not selected.

For agentic attention (waiting on input, or a finished result), prefer `session status` over `notify`.
The two overlap, either can raise an "I need you" signal, but a notification is a
one-shot banner and an unseen badge that stays until the session is visited, while `session status` is a
typed state (`active`/`blocked`/`completed`) that stays on the row until acted on, is more precise, and
drives the attention list, the optional title-bar attention button (settings.json `attentionButtonEnabled`),
and attention navigation (`session go --to next-attention`). Neither survives an app restart. Keep
`notify` for a one-off nudge that needs no follow-up.

## font

`agtermctl font inc|dec|reset [--pane left|right|scratch] [--target] [--window W]` — increase / decrease /
reset the font size of a session pane. Wire: `font.inc`, `font.dec`, `font.reset`. `--pane` picks which
surface's font to change, like `session type`
and `session text`: omitted or `left` is the main pane, `right` the split pane (errors with `session has
no split pane` when the session has no split), `scratch` the session's scratch terminal (settable even
while hidden). No `other` value. Only the MAIN pane's size is persisted across relaunch; a split/scratch
pane's font change is live-only. The GUI chords are Alt++ / Alt+- / Alt+0. Read the
resulting size back from
`tree` — `fontSize` (main), `splitFontSize`, `scratchFontSize`, each in points and omitted when that pane
isn't realized.

## keymap

`agtermctl keymap reload` — re-read and apply `keymap.conf`; returns `result.count` = the number of
parse diagnostics (0 = clean; the human output prints `ok` for 0, else `N diagnostic(s)`). Wire:
`keymap.reload`. App-global (no `--window`). The reply carries only the count, not the diagnostic texts.

**Help > Keyboard Shortcuts** (or **Ctrl+Shift+F1**) opens a read-only, searchable list of effective Windows bindings, grouped by action category and custom commands. It includes unbound actions, shortcut conflicts and keymap diagnostics. An open list updates after `agtermctl keymap reload` and keeps its search text. Esc closes it and returns focus to the terminal. The opener chord is fixed and is not in `keymap.conf`; it cannot be rebound or unbound there. The list is a Windows UI addition, not a control command or a new builtin action.

### keymap.conf format

The file lives at `<config dir>\keymap.conf` (the config dir is settings.json `configDirectory`, default
`~\.agterm-win`). Two verbs, line-based; blank lines and `#` comments ignored:

- `map <chord> <action>` — rebind a built-in action to a single chord (no leaders for built-ins).
- `command "<name>" [chord] <shell...>` — define a custom shell command, listed in the separate custom
  command palette (`Ctrl+Shift+O`). The quoted name may contain spaces. The post-name token is the chord only if it
  parses AND carries a modifier (a bare modifier-less key is rejected). A custom chord may be a leader
  sequence (chords joined by `>`, e.g. `ctrl+a>g`); the next chord must follow within 1.5 s, and Escape
  abandons the prefix. No chord → palette-only.

A **chord** is modifier words joined by `+` then a base key: modifiers `ctrl`, `cmd`, `opt`, `shift`
(also `control`, `command`, `option`, `alt`). On Windows `cmd` and `ctrl` are both Ctrl, `opt`/`alt` is
Alt; there is no separate Windows-key modifier. The
base key is a single character or `tab`/`space`/`return`/`delete`/`left`/`right`/`up`/`down`; `esc` is
not bindable. A key typed
with Shift is written `shift+<base>` (`shift+/` = `?`, `shift+=` = `+`, `shift+5` = `%`) — the base key,
not the shifted glyph. `+`/`>` can't be a bare key token (they are the separators), though those keys are
bindable via `shift+=`/`shift+.`. A `map` line may not bind a bare, modifier-less arrow (`map left …`) —
a built-in chord is always on, so a bare arrow would swallow the key everywhere;
any modifier makes it bindable. Some chords are reserved (Ctrl+Tab with any other modifiers, for the
session switcher, and plain Ctrl+1 / Ctrl+2) and cannot be bound.

Custom commands run through `pwsh.exe -NoLogo -NoProfile -NonInteractive -Command <line>`, or
`%COMSPEC% /d /c <line>` when pwsh is not on PATH. Tokens are expanded into that line raw — prefer the
`$env:AGT_*` environment form for untrusted content. A program in the pane can set the session title
(OSC) and working directory (OSC 7),
so `{AGT_SESSION_NAME}` and `{AGT_SESSION_PWD}` are as untrusted as `{AGT_SELECTION}`; use the
environment form for any of them:

- `{AGT_SESSION_NAME}` / `$env:AGT_SESSION_NAME` — the session's display name (its custom name, else the
  focused pane's terminal title, settable via OSC, else the folder name of its working directory).
- `{AGT_SESSION_PWD}` / `$env:AGT_SESSION_PWD` — the focused pane's working directory.
- `{AGT_SELECTION}` / `$env:AGT_SELECTION` — the current selection.
- `{AGT_PANE}` / `$env:AGT_PANE` — the pane the command fired from: `left` (main), `right` (split), or
  `scratch` (the session's scratch terminal). Feed it back as `session type --pane $env:AGT_PANE` to type
  into the very pane the shortcut was pressed in.
- Plus `AGT_SESSION_ID`, `AGT_WORKSPACE_ID`, `AGT_WORKSPACE_NAME`, `AGT_WINDOW_ID`, `AGT_WINDOW_NAME`
  and `AGT_SOCKET`, as tokens and environment variables.

Built-in action names for `map` include: `new_window`, `new_workspace`, `new_session`,
`open_directory`, `rename_session`, `duplicate_session`, `close_session`, `reopen_recent`, `undo_close`, `clear_status`, `increase_font_size`,
`decrease_font_size`, `reset_font_size`, `toggle_split`, `toggle_scratch`, `toggle_sidebar`, `quick_terminal`,
`session_palette`, `command_palette`, `custom_command_palette`, `dashboard`, and the navigation actions (`previous_session`, `next_session`,
`first_session`, `last_session`, `previous_attention_session`, `next_attention_session`,
`focus_left_pane`, `focus_right_pane`, `select_theme`). Editing the keymap from a terminal: open
`keymap.conf` in an editor, then `agtermctl keymap reload`.

## config

`agtermctl config reload` - re-read settings.json and apply its theme, font and notification settings to
every window; it also clears per-session font zoom. Wire: `config.reload`. agterm-win reads no ghostty
config, so `result.count` is always 0. App-global (no `--window`).

### settings.json

`settings.json` in the state directory (`~\.agterm-win` by default) holds the app settings; agterm-win has no Settings window, so edit the file
and run `agtermctl config reload`. Fields this reference names: `fontFamily`, `fontSize`, `theme`,
`darkTheme`, `followSystemAppearance`, `backgroundOpacity`, `backgroundBlur`, `notificationsEnabled`,
`notificationBadgeEnabled`, `notificationSoundName`, `blockedStatusSoundName`, the
`active`/`blocked`/`completed` `StatusColorHex` and `StatusShape` fields, `configDirectory`,
`restoreRunningCommand`, `newSessionDirectory` and `closeGraceUndoEnabled`. An omitted field keeps its
default.

Windows-only `overlayOpacity` controls the background of Keyboard Shortcuts, palettes, the session switcher and session questions. Absent or `null` keeps the current theme look. An explicit number from `0` (transparent) to `1` (opaque) applies that opacity to an opaque light/dark theme base color; text, controls and scrims are unchanged. Finite out-of-range numbers are clamped when applied, without changing the saved value. A wrong JSON type, an overflowing number or malformed JSON triggers the existing whole-file recovery to default settings. Windows Settings > Personalization > Colors > Transparency effects off forces opaque panel backgrounds regardless of this setting; switching it on restores the configured opacity or the current theme look. Windows switch changes apply while the app runs. After editing `settings.json`, run `agtermctl config reload` to update all windows, including panels already open. For example, `"overlayOpacity": 1` makes panels opaque even when Windows transparency effects are on.

Windows-only `closeOnShellExit` decides whether a shell pane (the main or split pane of a session created without `--command`) closes when its shell exits. `graceful`, the default (also when absent or unknown), closes it on exit code 0 and keeps it open on any other code; `always` closes it on any code; `never` keeps it open on any code. A held pane shows `[process exited with code N; press any key to close]`, and the next key the user presses in it closes it. `session type` writes to the dead shell and does not close it: close a held session with `agtermctl session close --target <id>`. A `--command` session still closes or holds by `--wait`.

## theme

The app's out-of-the-box default theme is the bundled **agterm** theme (a fresh install opens on it).
A separate **default terminal** entry means "no theme" — the built-in base colors (`theme` absent). The
catalog is the 603 names of upstream agterm: the Ghostty theme pack plus `agterm`, embedded in the app.

`agtermctl theme list` — list the bundled theme names; returns `result.themes` (the names),
`result.theme` (the current plain theme, absent = the built-in base colors / "default terminal"), and
`result.sync` with `result.light`/`result.dark` (the per-appearance themes). Wire: `theme.list`. While
syncing,
`result.theme` is absent — the state rides the three sync fields. Human output prints one name per
line with a leading "default terminal" row, the active one(s) marked `* `; when syncing, a header notes
the light/dark pair and both sides are marked.

`agtermctl theme set [name]` — set and persist the terminal theme app-wide (settings.json `theme` and
`darkTheme`), per slot. Wire: `theme.set`.
- `theme set <name>` sets the light/single theme; a dark theme, if set, is KEPT (syncing stays on).
  Omit the name for the built-in base colors ("default terminal") — with a dark theme set, that
  clears BOTH (an unnamed side can't be part of a pair).
- `theme set --dark <name>` sets the dark theme — the terminal then tracks the Windows light/dark app
  mode (settings.json `followSystemAppearance`), applying the matching side automatically as it
  switches (the light side seeds from
  the current theme, else `Builtin Light`). `--light <name>` is an alias for the positional name.
- `theme set --dark none` clears the dark theme — tracking stops, the light theme stays as the single
  theme.
The response always echoes the full state (`result.theme`/`sync`/`light`/`dark`). An unknown name
returns `unknown theme: <name>`; a positional name combined with `--light` returns
`theme.set takes either a name or --light, not both`. Human
output prints `ok`. App-global (no `--window`). The GUI's live-preview picker (`select_theme`,
"Select Theme…" in the command palette)
previews live; committing it replaces the CURRENT appearance's side when syncing (the pair is
kept); over the pipe `theme set` is the commit, with no preview.

## restore

`agtermctl restore clear` — clear every session's saved CAPTURED foreground command and persist.
Wire: `restore.clear`. agterm-win never captures a
foreground command itself, so the only captured commands are ones loaded from an older snapshot. It does
NOT clear
a `session.new --command` session's own command (`initialCommand`, the durable creation identity), which
still re-runs on restore when settings.json `restoreRunningCommand` is `true`. App-global (no
`--window`), prints `ok`.

For a PER-SESSION, per-pane override that pins (or suppresses) what a pane restores, use
`session restore` (in the session section above): it is what a `SessionStart` hook rewrites to reattach
a non-idempotent command. `restore clear`
here is app-global and touches only the captured commands, not those overrides.

## Errors you may see

Many of these are the start of the message; the full error adds the rejected value or the accepted values.

`no such <noun>: <target>` / `ambiguous <noun> prefix …` (target resolution), `no such session`, `invalid split mode` /
`invalid scratch mode`, `session has no split` (focus), `no selection` (copy), `overlay already open` /
`no overlay` / `overlay still running` / `no overlay result` (overlay), `invalid flag mode` (session flag),
`invalid fit` / `invalid position` / `invalid opacity` / `invalid color` / `text too long` /
`unsupported image (PNG or JPEG only)` / `no such image file` / `image path must not contain control characters` / `invalid background mode` (session background),
`invalid sidebar mode` (sidebar), `invalid focus mode` (workspace focus),
`pick pending` / `ask pending` (quick show, surface zoom, dashboard, search while a picker or window
question is up), `quick terminal not open` / `quick terminal not realized` (quick type) /
`failed to read surface buffer` (quick text / session text),
`invalid restore mode` / `session.restore set requires a command` / `command must not contain control characters` /
`command too long (max 1024 bytes)` / `the scratch terminal is never restored` / `unknown pane id` /
`failed to save the restore override, the previous value is still in effect` (session
restore; a `session restore --pane right` on a session with no split also returns `session has no split`),
`window not open — window.select it first`
(resize/move/`--window`), `unknown theme: <name>` (theme set),
`invalid color (expected #rrggbb)` (session status --color),
`invalid shape: <value> (circle|square|triangle|diamond|capsule|star)` (session status --shape over the
raw pipe; the `agtermctl` CLI rejects the same value locally with
`shape must be one of: circle, square, triangle, diamond, capsule, star`),
`--pane must be left, right, or scratch` (the `--pane` value check — the `agtermctl` CLI rejects a bad pane
with this for session status/type/text, and over the raw pipe `session.status` returns this same string;
`session.type`/`session.text` over the raw pipe instead return `invalid pane: <value>`). Unknown commands
fail to decode and return a structured error, never a crash.
