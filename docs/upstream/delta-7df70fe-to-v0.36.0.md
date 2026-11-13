# Upstream agterm changes after the pin `7df70fe`

Snapshot of 2026-10-10. It compares the parity pin `7df70fe` (2026-07-24, v0.16.1 + 18 commits) with `origin/master` `ed831ab` (v0.36.0, 2026-10-09). There are 578 commits between them.

From here agterm-win does not follow upstream history in order. Features are taken one at a time, and this file is the catalog to choose from: what upstream has, which commands and fields belong to it, and what it breaks in work already done.

Sources:

- Upstream `CHANGELOG.md`, sections v0.17.0 to v0.36.0. `#NNN` below is an upstream PR.
- `docs/upstream/surface-v0.36.0.json`: the output of `tools/extract_surface.py` at `ed831ab`, in the same format as `parity/surface.json`.

## Port status

Each item ends with its state in agterm-win:

- `Port: done`: implemented, with the agterm-win PR or commit.
- `Port: partly`: some of the item is implemented; the line says what is missing.
- `Port: not done`: not implemented.
- `Port: not applicable`: the item has no Windows form; the line says why.

A port PR is written `agterm-win #N`. Work merged before the repository was on GitHub has no GitHub PR. It is written `port PR N` with its commit, or by commit only when the commit has no PR number. A command or field taken from upstream is also listed in `parity/adopted.json`, and a difference the port keeps on purpose is described in `docs/PLATFORM-NOTES.md`.

## Size

| | pin `7df70fe` | `ed831ab` | difference |
|---|---|---|---|
| Protocol commands | 66 | 110 | +44 |
| ControlArgs | 53 | 86 | +33 |
| ControlResult | 14 | 32 | +18 |
| Session node in `tree` | 28 | 49 | +21 |
| Window node in `tree` | 9 | 10 | +1 |
| BuiltinAction | 41 | 49 | +8 |
| `{AGT_X}` tokens | 10 | 12 | +2 |
| AppSettings | 37 | 50 | +13 |
| SessionSnapshot | 15 | 23 | +8 |

Nothing is removed, except `Snapshot.focusedWorkspaceID` (see section 11).

From v0.17.0 the pin already has: arrows in chords (#291), status glyph shapes (#292), status tooltips (#283), the sidebar only in the active window (#285), Codex questions anywhere in the message (#282), the suppressed-banner message. The Dock menu (#284) came after the pin.

## 1. Live sessions (zmx): processes survive a restart

- The `live` restore mode next to `fresh` and `rerun`: `restore.mode`, the setting `restoreMode`. Each main and split pane runs through the zmx multiplexer. Quitting the app drops only the connection; the process keeps running, and the next launch attaches to it again. The mode is fixed for the life of the process #515. Port: not done.
- `zmx.list`, `zmx.prune`, `zmx.kill`, `zmx.reset` (Reset Live Sessions #587, #650), `zmx.screen` (the daemon screen by name #694). Port: not done.
- `restore.capture`: fill captured commands on request, not only at quit #452. Port: not done.
- Session host: a helper process that keeps TCC attribution on the app #574. In `tree`: `backedByZmx`, `liveAttribution`, `splitLiveAttribution`, `liveReset`. Port: not done.
- `session.restart --command LINE (--pane-id | --pane)`: a new login shell in the same pane; `--pane-id` and `AGTERM_*` stay #699. Without `--command` it repeats the argv of the foreground program, and the reply has `restart.replayedArgv` #701. Port: not done.
- Closing a pane sends hangup to the foreground job #699. Port: done in agterm-win #36, in a Windows form. Windows has no process group bound to a terminal, so attached processes get `CTRL_CLOSE` from conhost and the port terminates the ones that never reach the pane console; see PLATFORM-NOTES "Closing a pane ends the processes that use its console".
- A `session new --command` longer than 1024 bytes goes to zmx as a payload, not through the pty #533. Port: not done.

**Windows:** there is no zmx. It needs a daemon of our own that holds the ConPTY, or an Adapt with `{"ok":false,...}`. This is the largest block; section 2 and part of section 9 depend on it.

## 2. Remote sessions over ssh (depend on section 1)

- `zmx.tree HOST`, `zmx.attach HOST SESSION [--window]`: a session from another machine shows in the sidebar like a local one #524, #566. Port: not done.
- Presentation stream `zmx.present`: status, notifications, HUD, ask and overlay are mirrored to the attached machine; roles `presenter` and `mirror`. In `tree`: `presentation`, `presenters`, `remoteOverlays` #629, #630. Port: not done.
- Pane lead: `session.lead`, `surfaces[].lead`. Only the lead sets the size #635. Port: not done.
- The split layout and `session.context` are mirrored from the origin #634, #636. Port: not done.
- Reconnect after ssh exits with code 255; the reason and the count are in `tree` as `reconnect`. Options `ServerAliveInterval=5`, `ServerAliveCountMax=2`. A retry right after wake #655, #683, #687, #694. Port: not done.
- `remotes.conf` and File > Attach Remote, the action `attach_remote` on Cmd+Shift+R #710. Port: not done.
- Events `remote.opened` and `remote.closed` #610. Token `AGT_SESSION_HOST` #576. Port: not done.
- `agtermctl terminfo install DESTINATION` #609. Port: not done.
- In `tree`: `remoteHost`. The window title shows a cloud icon and the ssh target #572. Port: not done.

## 3. HTML overlays (WebView)

- `session overlay open --html FILE | --url URL` in the session, floating and pane slots #659. Port: `--html` and `--url` in the session slot, full and floating, in the core, the protocol, the CLI, the headless host and the live app. Pane slots: not done.
- Flags: `--js`, `--navigation`, `--cwd DIR` (file access), `--chromeless` #678, `--persistent` (shared cookies, localStorage and IndexedDB) #689, `--browse` (navigation to other sites) #708, `--block` #672. Port: `--js` and `--navigation` are taken with `--html` and `--url`, `--cwd` with `--html`; the rest is not done.
- `session.overlay.reload`, `session.overlay.navigate back|forward|browser|finder` #663, `session.overlay.submit`, `session overlay result --page ID`. Port: reload and navigate are taken; submit and `--page` are not done.
- Bridge to the control API: `data-agterm="<cmd>"` without JS, `agterm.request(cmd, {target, args})` with `--js`. Commands without a target act on their own session, window or pane. Streaming commands are refused #672. Port: not done.
- `window.close()` on a `--js` page closes the overlay with the result `dismissed` #704. Port: not done.
- Page zoom with Cmd+/Cmd-/Cmd+0 and `font inc|dec|reset`, one value for the app: the setting `htmlOverlayZoom` #670. Port: not done.
- `browser.clear`, `browser.links [browser|overlay]`, the setting `linkOpenMode`: a link from the terminal opens in the browser or in an overlay #708. Port: not done.
- The page gets the terminal theme through CSS variables. Port: not done.
- In `tree`: `htmlOverlays[]` with `id`, `page`, `zoom`, `persistent`, `browse`. In the reply: `pageID`, `pageOutcome`. Port: `htmlOverlays[]` without `zoom` and `pageID` are taken; `pageOutcome` is not done.

**Windows:** WebView2 is already in `AgTerm.Xterm`.

## 4. UI primitives for scripts

`pick`, `ask` and `session.hud` are implemented; see PLATFORM-NOTES "Upstream features adopted beyond the pin".

- **pick**: `pick.open`, `pick.result`, `pick.cancel`. Items from stdin as lines or JSON, blocking and non-blocking modes, `allowCustom`, `--query`, `--select ID`. Only the label is matched; with an empty query the caller's order is kept; an empty list is allowed. In `tree`: `pickPending` #316, #339, #597. Port: done in commits `8943d22` to `76564fb`. The picker is centered in the window, not over the terminal area (#367, section 10).
- **ask**: `ask.open`, `ask.result`, `ask.cancel`. 1 to 6 buttons; the result is a button, `escaped` or `cancelled`. The `terminal` style belongs to a session or a pane and covers only it; the `gui` style is modal for the window and shares the slot with `pick`. In `tree`: `ask` on the session, `askPending` at the top #556, #561. Port: done in commits `b03f3c4` to `838afa6`. Remote presentation of a question (section 2) is not ported.
- **HUD**: `session.hud.open`, `.update`, `.close` #361. Position on a 3x3 grid; `top` and `bottom` stay as aliases #386. `--pane`, `--pane-id` #536. `--markdown` up to 4096 characters, `--font-size`, `--file` #641. `--hide-after` #613. `--sticky`, `--no-frame` #709. Markdown links open on Command-click #707. In `tree`: `hud`. Port: done in commits `6c06ca4` to `872931c`. A link opens on Ctrl+click. Alt+W takes a HUD and the other covers down before it closes the session, as upstream's Command-W, since port PR 26 (`cc8f6bb`); see PLATFORM-NOTES, the `session.hud` entry.

## 5. Splits and pane addressing

- Horizontal split: `axis` on `session.split`, `top` and `bottom` as aliases, `splitAxis` in `tree` and in the snapshot. A new action `toggle_horizontal_split` on Cmd+Shift+D. **Dashboard moved from Cmd+Shift+D to Cmd+Shift+G**: this changes the default chord of an action that is already implemented #427. Port: done (upstream `eb29d556`). The `dashboard` chord moved as upstream's did; `DashboardTarget` aliases are the next line.
- `session.split.close`: close the whole split pane #421. `hasSplit` in `tree` tells a hidden split from a missing one. Port: done (`79852e71`, `a5856949`).
- `session.swap`: swap the terminals of the two panes; the axis and the ratio stay #513. Port: done (`db62080d`).
- `--pane-id` (the stable `$AGTERM_PANE_ID`) on `session type`, `session text`, `surface cursor`; `surfaces[].paneID` in `tree`; `result.pane` #698. Token `AGT_PANE_ID` #603. Port: done (`eab672be`, `cf31c5d4`, and `surface cursor` with `2279c722`).
- An overlay on one pane: `--pane left|right` on `session overlay open|close|result`, `paneOverlays` in `tree` #343. Port: not done.
- A background per pane: `session background ... --pane`, `paneBackgrounds` in `tree` and in the snapshot #647. Port: not done.
- `session.paste --pane` #529. `surface.cursor` #451 (port: `surface.cursor` done, `2279c722`). `session.overlay.copy`, `session.overlay.text` #437. Port: not done for the rest of this line.
- Dashboard takes `id:left` and `id:right` #334. Port: not done. `dashboard` takes session ids only.
- A double click on the divider sets the split to 50/50 #357. Port: done (A4b).

## 6. Navigation, windows, sidebar

- `window.go --to next|prev`, actions `previous_window` and `next_window` (no chord) #591. Port: the command `window.go` is done (`window go`); the actions are not.
- `window.minimize on|off|toggle`, `window new --minimized`, `minimized` in `window list` #294. `window resize` returns the size it applied #566. Port: done in agterm-win. The resize echo puts `width` and `height` in the result and prints `W H` in the CLI; minimize follows upstream `28cad2c1`.
- `workspace.go --to next|prev`, actions `previous_workspace`, `next_workspace`, `toggle_workspace_collapse` (no chord) #436. Port: the command `workspace.go` is done (`workspace go`); the actions are not.
- Focus as a set of workspaces: `workspace.filter`, `workspace focus add`, the action `toggle_workspace_filter` #297. Port: done in agterm-win A5.
- Flagged view layout: `sidebar.flagged-layout flat|tree|plain|toggle`, the setting `flaggedViewLayout` #621, #718. Port: done (B5a).
- `sidebar.width` #512. Port: done.
- The attention list (Ctrl+Shift+I) over all windows #596. Port: not done. The port lists the sessions of the current window.
- Where a new session goes: at the end or after the current one, the setting `newSessionPlacement` #665. Port: not done.
- A click on a workspace row expands it: `workspaceRowClickExpands` #342. Port: not done.
- Dock menu #284, New Window in it #319. Port: not applicable: Windows has no Dock.
- Quick terminal as a floating panel with a global hotkey #441; its size as a percent of the screen, `quickTerminalSizePercent` #459. Port: partly. The panel and the hotkey are done in agterm-win #12 (upstream `f8679fde`); `quickTerminalSizePercent` is not done. See PLATFORM-NOTES "Detached quick panel and global hotkey".

## 7. Keymap

`keymap.list`, `keymap.run`, the skip of a repeated `command` name and the `--error-*` options are implemented; see PLATFORM-NOTES "Upstream features adopted beyond the pin". Not taken: `menu[]`, `|` alternatives, `--repeat`, running from the session of an HTML page.

- F1 to F20 with and without modifiers, as a leader, as `global-hotkey` #619. Port: not done.
- Alternatives with `|` in one `map` or `command` line; leader sequences for builtin actions #420. Port: not done.
- `--repeat` for a leader (as tmux `bind -r`), `repeats` in `keymap list` #682. Port: not done.
- `global-hotkey` #441. Port: done in agterm-win #12. It takes one modified chord, without alternatives or leaders.
- `keymap.list`: what the bindings resolved to, the menu, diagnostics #301. `keymap.run NAME` #694. Port: done in port PR 57 (`026c271`). `chord` is in the Windows spelling, and there is no `menu[]`.
- `--error-hud`, `--error-position`, `--error-pane` on custom commands #613, #622. Port: done in port PR 58 (`1617b3b`).
- Chords resolve by layout: on a non-Latin layout a binding matches the physical key #310. Ctrl+J and Ctrl+K in palettes work on Cyrillic #705. Port: done. #310 in a Windows form in port PR 16 (`b1b92b5`), PR 21 (`977c365`), PR 59 (`69c2fe1`) and PR 60 (`4b2ed49`); see PLATFORM-NOTES "Letter keys on non-Latin layouts". #705 in `058517e`: Ctrl+J and Ctrl+K came with upstream `2fb0ca72` (#695), and the port matches them by virtual key, so they work on a Cyrillic layout too.
- Two lines on one chord are no longer decided by their order in the file #444. Port: not done. The parser keeps the pin rule: of two overrides on one chord, the later one loses.
- A repeated `command` name is skipped with a diagnostic #582. Port: done in port PR 57 (`026c271`, upstream `68e17937`).
- A builtin action takes a chord from a standard menu item and gives it back when freed #680. Port: not applicable: the port has no menu bar.

## 8. Events and hooks

- `hooks.conf` (`on <kind> <command>`), the event JSON on stdin, `AGT_*` in the environment; `hooks.list`, `hooks.reload` #607. Port: done (B7). The shell is cmd.exe; see PLATFORM-NOTES "Event hooks".
- New events: `pane.split`, `pane.scratch` #607, `session.selected` with `previous` #694, `remote.opened`, `remote.closed` #610. `status` got `previous`. Port: done (B7). `remote.opened` and `remote.closed` parse but never fire: the port has no remote sessions.
- `agtermctl events` retries after a refused connection (250 ms to 4 s) from the same cursor and exits after about 30 s #716. Socket backlog 128 instead of 8 #717. Port: #716 done. A busy pipe is retried, a missing pipe is fatal, and the 30 s budget is counted by the clock (PLATFORM-NOTES). #717 has no analogue: the control pipe has one server instance, and a second client waits in its connect call until the current connection closes.

## 9. Agent status

- `statusReset`: on first key, on Enter, disabled. `session type` follows it too #585. Port: done in the A6 PR (version 0.1.80); see PLATFORM-NOTES "Agent status on terminal input".
- `statusChangedAt` in `tree`, set on every accepted set #465, #617. Port: done in the A6 PR.
- The blocked status of a split pane is not overwritten by its neighbor #523. Port: done in the A6 PR.
- New integrations: OpenCode #289, OpenCode 2 #696, Copilot (recipe) #464. Claude hooks set `blocked` when a permission prompt opens #721. Port: partly. OpenCode is not done. Copilot CLI is done in a Windows form in agterm-win #2: *Install Agent Status Hooks…* writes Copilot CLI hooks. #721 is done in port PR 22 (`6a4eb66`). See PLATFORM-NOTES "Help menu".
- `foregroundShell`, `splitForegroundShell` in `tree` #525. Port: not done.

## 10. Other control API changes

- `version` #476. Port: done (without `tree.app`).
- `session.context`: a line about the session's purpose in the title and in `tree`; it survives a restart, and `session.duplicate` does not copy it #522. Port: done in agterm-win A3. The title bar has `sessionContext`; the remote `effectiveContext` is not ported.
- `realized` in `tree` #417, `splitCwd` #566, `splitCommandWait`. Port: `realized` and `splitCwd` done in agterm-win A3; `splitCommandWait` not done (no split `--command`).
- `agtermctl --json` prints the server line without re-encoding it #628. Port: done (PLATFORM-NOTES).
- `agtermctl clipboard set [TEXT]`: a CLI subcommand; it is not in the protocol command list #722. Port: done. It writes the Windows clipboard; the zmx and OSC 52 paths are not ported (PLATFORM-NOTES).
- When the socket is taken, `agtermctl` tells "the app is not running" from "an owner exists but does not accept" #633. Port: done. The port asks the pipe with `WaitNamedPipe` instead of reading a lock file (PLATFORM-NOTES).
- Workspace name in the window title #601; a custom-command button in the title bar #582, with the most-run commands on top #584 (`68e17937`). The list of shown elements is `shownInterfaceElements`. Port: done. `shownInterfaceElements` and the workspace name in B11a, the custom-command button as a menu, with its run counts in `custom-command-usage.json`, in B11b (PLATFORM-NOTES).
- Font of palettes and the picker: `interfaceFontSize` #367. Cursor: `cursorStyle`, `cursorBlink` #487. Port: not done.
- A welcome window on first launch: `welcomeShown` #353. Port: not done.

## 11. Persistence and settings (breaking)

- `Snapshot`: `focusedWorkspaceID` is replaced by `focusedWorkspaceIDs` and `focusEnabled`. Old `workspaces.json` files have to be migrated on read. Port: done in agterm-win A5.
- `SessionSnapshot`, new fields: `paneIdentity`, `splitPaneIdentity`, `hasSplit`, `splitAxis`, `context`, `splitInitialCommand`, `splitCommandWait`, `paneBackgrounds`. Port: `context` done in agterm-win A3; the others not done.
- An unknown value of an optional field in `workspaces.json` becomes nil instead of failing the whole snapshot #363. Port: done with focus as a set of workspaces (PLATFORM-NOTES).
- `AppSettings`, new fields: `cursorBlink`, `cursorStyle`, `flaggedViewLayout`, `htmlOverlayZoom`, `interfaceFontSize`, `linkOpenMode`, `newSessionPlacement`, `quickTerminalSizePercent`, `restoreMode`, `shownInterfaceElements`, `statusReset`, `welcomeShown`, `workspaceRowClickExpands`. Port: partly: `flaggedViewLayout` (B5a), `statusReset` and `shownInterfaceElements` (B11a) are done.

## 12. Behavior changes in commands already implemented

Each adopted change is recorded in PLATFORM-NOTES "Upstream behavior adopted beyond the pin" with its upstream sha.

These items change what agterm-win already does at the pin. They do not depend on new features and may be needed in any update.

- `session text --pane-id` with an unknown id answers `unknown pane id: <id>`; before, it read the visible pane and answered ok #698. Port: done, see PLATFORM-NOTES "Upstream behavior adopted beyond the pin".
- `session.paste`, `session.selectall`, `session.copy` without a real surface answer `session not realized` #425. Port: done in `aaeec2d` (upstream `9f145925`).
- `session type`, `quick type`: a NUL byte is refused `6bc8a0b7` (issue #455); invalid UTF-8 in `--stdin` is refused #521; the final Return goes 10 ms after the text #681. Port: done in `bc1f162`, `8e6be4a` and `99659f2` (upstream `6bc8a0b7`, `7b8f63cf`, `2cd47dca`).
- `session.overlay.open --size-percent` outside 1..100 is refused #498. Port: done in `e76ffb8` (upstream `94c509bf`).
- The dispatcher reports `invalid pane: X` for `session.type`, `session.text` and `font` (#530). The aliases `primary`, `split`, `top`, `bottom` came with horizontal splits (#427) and are not part of this change. Port: done in `c7582f8` (upstream `b3ef1baf`); the aliases are done in A4b.
- `session.type` on the main pane without `--select` answers `session not realized` instead of `session not realized; use select`, and `--select` selects the session only when the surface is not ready (#351). Port: done in `ae922d8` (upstream `22d7e1c1`).
- A new workspace becomes the target before the selected session changes; `workspace select` always retargets #329. Port: done in `7791760` (upstream `0106275a`). agterm-win #31 keeps this workspace open on `sidebar.collapse`.
- `window new` answers after the window is attached; `window list` does not return a stale cache; a minimized window stops being frontmost; `window select` works when the app is not active #294. Port: done. `window select` is done in `9d0ba24` (upstream `28cad2c1`), and the frontmost handoff with `window.minimize`. `window new` already waited until the window is ready, and `window list` reads open windows live, so the port has no stale cache.
- The error of a custom command in a removed directory: `session directory <path> no longer exists`. Port: done in `1668561` (upstream `312987af`).
- Values with a line break in names and `--cwd` are sanitized before they reach `{AGT_*}` #354. Port: done in `4af1bf6` (upstream `97d4e6af`).
- A custom command from the right pane gets the right pane's cwd in `AGT_SESSION_PWD` #482. Port: done in `70162b9` (upstream `67d65657`).
- A recorded command start with control bytes is refused, not replayed #457. Port: done in `c77bf09` (upstream `131dfc61` and `ef46edcd`).

## 13. Outside parity

- macOS: TCC and entitlements, Accessibility and Dictation, the locale on macOS 27, freeing GPU surfaces of hidden panes, libghostty and Zig updates, the Homebrew cask, Reduce Motion.
- No code: recipes in `cookbook/`, documentation.
- The bundled agent skill and the plugin marketplace for Claude Code and Codex are no longer outside parity: the port keeps its own adapted copy of the skill and its own marketplace (`docs/PLATFORM-NOTES.md`, section "Agent skill").

## Upstream changes after `ed831ab`

`docs/upstream/delta-ed831ab-to-v0.40.0.md` lists them, up to v0.40.0, with their port status.

## Open questions

- `session.overlay.job.run` is in the command list. It looks like an internal command for overlays that a presenter starts (section 2). Before it goes into parity, read it in `ControlProtocol.swift`.

## How to recompute

```
git -C <agterm-clone> fetch origin
git -C <agterm-clone> log --oneline 7df70fe..origin/master
```

A surface snapshot of any revision, without a checkout:

```
python tools/extract_surface.py --agterm "<agterm-clone>" --ref "origin/master" --out "surface-new.json"
```

The script looks for types in all `agtermCore` files, so moving a type between files does not break it. Computed properties (`effectiveX: T { ... }`) are not in the snapshot. Compare the result with `parity/surface.json`.
