# Upstream agterm changes from `ed831ab` to v0.40.0

Snapshot of 2026-11-04. It compares `ed831ab` (v0.36.0, 2026-10-09), where `docs/upstream/delta-7df70fe-to-v0.36.0.md` ends, with the tag v0.40.0 `c63e5158` (2026-11-02). There are 138 commits between them.

This file continues that catalog and has the same purpose: a list to choose features from, one at a time. The pin stays `7df70fe`. "The first catalog" below means `delta-7df70fe-to-v0.36.0.md`. When a change extends an item there, this file names the section and does not repeat the item.

Sources:

- Upstream `CHANGELOG.md`, sections v0.37.0 to v0.40.0. `#NNN` below is an upstream PR.
- `git log ed831ab..v0.40.0` in the upstream repository.
- `docs/upstream/surface-v0.40.0.json`: the output of `tools/extract_surface.py` at `c63e5158`, in the same format as `parity/surface.json`.

`agtermctl version` with the `installed:` line (#723) is in the v0.37.0 changelog, but it was merged before `ed831ab` (`18da29cb`), and `result.installed` is already in `surface-v0.36.0.json`. It belongs to the first catalog, section 10.

## Port status

Each item ends with its state in agterm-win:

- `Port: done`: implemented, with the agterm-win PR or commit.
- `Port: partly`: some of the item is implemented; the line says what is missing.
- `Port: not done`: not implemented.
- `Port: not applicable`: the item has no Windows form; the line says why.

A port PR is written `agterm-win #N`. Work merged before the repository was on GitHub has no GitHub PR. It is written `port PR N` with its commit, or by commit only when the commit has no PR number. A command or field taken from upstream is also listed in `parity/adopted.json`, and a difference the port keeps on purpose is described in `docs/PLATFORM-NOTES.md`.

## Size

| | `ed831ab` | v0.40.0 `c63e5158` | difference |
|---|---|---|---|
| Protocol commands | 110 | 131 | +21 |
| ControlArgs | 86 | 102 | +16 |
| ControlResult | 32 | 45 | +13 |
| Session node in `tree` | 49 | 61 | +12 |
| Workspace node in `tree` | 6 | 8 | +2 |
| Window node in `tree` | 10 | 10 | 0 |
| BuiltinAction | 49 | 54 | +5 |
| `{AGT_X}` tokens | 12 | 12 | 0 |
| AppSettings | 50 | 55 | +5 |
| Snapshot | 9 | 10 | +1 |
| WorkspaceSnapshot | 4 | 5 | +1 |
| SessionSnapshot | 23 | 27 | +4 |

Nothing is removed.

The snapshot does not have event kinds. `ControlEvents.swift` has three new ones: `pane.focused`, `app.launched` and `message` (section 10). The snapshot also misses two fields that `tree` builds by hand: `flaggedOrder` at the top of `tree` (section 4) and `surfaces[].pendingMessages` (section 1).

None of the names in `parity/windows-only.json` is in the v0.40.0 snapshot.

## 1. Message bus between panes

- `msg.send`, `msg.read`, `msg.ack` #847. Each live pane has a mailbox for the current app run. `msg send TEXT --to SESSION:PANE` (repeatable) or `--all [--workspace W]` delivers to each recipient separately. `msg read [--ack] [--wait]` returns the oldest messages, and `msg ack ID...` removes handled ones. The sender is the calling pane, from `AGTERM_SESSION_ID` and `AGTERM_PANE_ID`, or `--as`. Args: `recipients`, `identity`, `ack`, `ids`. Results: `messageID`, `delivered`, `rejected`, `messages`, `remaining`, `acked`. Port: done (B6).
- Closing the pane, its session, workspace or window, `session restart` and a respawn drop the mailbox. Nothing is saved across a quit. Port: done (B6). The port has no `session restart` and no respawn; a reload of the terminal page drops the window's mailboxes (PLATFORM-NOTES).
- `tree` reports `surfaces[].pendingMessages`. A `message` event names the sender and the recipient, never the text. Port: done (B6).

## 2. Companion sessions

Remote companions are in section 9.

- A companion belongs to a parent session and nests under it in the sidebar #840. It folds, reorders among its siblings, moves and closes with its parent, and close undo brings the whole group back. New Companion is in the row menu, the palette and the action `new_companion` (no chord). `session new --parent SID` sends `session.companion.new`. Port: not done.
- A page companion: `session overlay open --html|--url --companion` sends `session.companion.page`. It adds a page row with no shell, and the row is not restored after a relaunch #840. It needs HTML overlays (first catalog, section 3). Port: not done.
- Duplicate as Companion: the action `duplicate_companion` (no chord); `session duplicate --companion` sends `session.companion.duplicate` #840. It copies the split layout and starts another copy of each pane's foreground program. It creates nothing when a pane cannot be copied. Fixes: the refusal shows in a panel over the session, and a program whose binary an upgrade removed starts from the pane's own PATH #841; the copy focuses its left pane #842; a program that has its own session id in its arguments gets the copy's id #843. Port: not done.
- `session.collapse`, `session.expand` fold a parent. `tree` reports `parent`, `collapsed` and `content`. Results: `parent` (the new companion's parent), `closed` (every session `session.close` closed, companions included). Port: not done.
- While a parent has companions, its shell exiting starts a fresh shell in the same directory. Flat lists (palette, Ctrl-Tab) label a companion `parent › child`, and a folded parent shows a marker for a blocked, completed or unseen companion. Port: not done.

**Windows:** Duplicate as Companion reads each pane's foreground program and its arguments. The port has no foreground pid (CLAUDE.md, the parity policy), so this part needs its own Windows form.

## 3. Sidebar rows

- Tint #836: `session.tint <color|clear> [--on icon|name|icon,name]`, `workspace.tint`, and `--tint`, `--tint-on` on `session new` and `workspace new`. Seven colors, on the icon, the name or both. A workspace tint colors only its own row. Args `tint`, `tintOn`; `tint` and `tintOn` on the session and workspace nodes in `tree`. Port: done. The wire, CLI, tree and saved state came in B2c1; the sidebar drawing, the Tint submenu and the "Tint Session" palette rows in B2c2. The port has its own `workspace.theme` and `workspace.icon` (PLATFORM-NOTES "Windows-only extensions"); the names do not clash.
- Session info line #864: `session.info TEXT`, with `--markdown` and `--hide-after SECONDS`. One line under the session row. It is not saved across a restart. `tree` reports `info`, `infoMarkdown` and `infoHideAfter`. The setting `sessionInfoDisplay` shows it always, in the flagged view only, or never. Port: done. The wire, CLI, tree, expiry and the `sessionInfoDisplay` field came in B2d1; the sidebar drawing and the Settings picker came in B2d2.
- Workspace icons and session icons can be turned off, as `workspaceIcons` and `sessionIcons` in `shownInterfaceElements`; both are on by default #865. A hidden icon also hides the state it shows. Port: done (B2b). The port keeps the pin's `hiddenInterfaceElements` (first catalog, section 10).
- `lastSelectedAt` in `tree` and in the saved session: the last time the session became or stopped being its window's selection #818. Port: done (B2a).

## 4. Flagged view and workspace focus

These extend the first catalog, section 6. The flagged layouts (B5a), Flag All Sessions (B5b), the membership editors (B5c) and focus as a set of workspaces (A5) are ported.

- Reorder the flagged view #833. The flat and plain layouts keep a saved order per window; a drag works across workspaces. A newly flagged session goes to the end, and next and previous session follow the displayed order. `session flag move --before|--after ID` is mode `move` on `session.flag`; `before` and `after` are existing args. `tree` reports `flaggedOrder` at the top level, and the window snapshot saves it. Port: partly (B5d1, B5d2). The order, `session flag move`, the tree field, the snapshot field and the drag are done; spring-loaded expansion is not done (PLATFORM-NOTES).
- Flag All Sessions on the workspace row menu #787: `workspace.flag on|off|toggle [--target W]`. It flags sessions the tree layout hides, too. Port: done (B5b).
- Edit Flagged Sessions and Edit Focused Workspaces #803: palettes that list every session or workspace of the window with its mark; Return toggles a row and the palette stays open. The actions `edit_flagged_sessions` and `edit_focused_workspaces` have no chord. Port: done (B5c).

## 5. Overlays and pages

These extend the first catalog, sections 3 and 5. The port has program overlays from the pin, but no HTML overlays and no pane overlays.

- Hide overlays without closing them #790: `session.overlay.hide|show|toggle [--pane left|right]`, the action `toggle_overlay` on `cmd+shift+j`. A hidden program keeps running and keeps its exit status; a hidden page stays loaded. A HUD and an ask are never hidden. `tree` reports `hiddenOverlays` and `htmlOverlays[].hidden`. A title-bar button is `overlayToggle` in `shownInterfaceElements`, off by default. A keymap that already binds `cmd+shift+j` keeps it. The page strip shows Hide only while that button is on #834. Port: not done. Hiding a program overlay does not need HTML overlays.
- `--width` and `--height` in points on `session overlay open` and `session overlay resize` #798. Each replaces `--size-percent` on its own axis and is capped at 80% of the session. `tree` reports the laid-out size as `overlayWidth` and `overlayHeight`. Port: not done.
- A pane overlay draws over a session-wide program or page #831. Port: not done. The port has no pane overlays.
- Image page #844: `session overlay open --image FILE` (arg `image`), and Cmd-click on an image path in terminal output. The panel takes the shape of the image. Any other local path, except a markdown file, now reveals in Finder. Port: not done.
- Markdown page #866: `session overlay open --markdown FILE` (arg `markdownFile`), and Cmd-click on a `.md` or `.markdown` path. The page uses the terminal theme and loads nothing from the network. It runs no script from the file. Port: not done.
- Links in a smaller overlay #812, #813: `linkOpenMode` gets `overlay95`, `overlay75` and `overlay50`, also set with `browser links`. With an overlay mode, Cmd+Shift-click opens a link in the browser, and Shift-click extends a selection. Port: not done. `linkOpenMode` itself is not adopted (first catalog, section 3).
- `--chromeless` for a page served from this machine #750: a `--url` on host `127.0.0.1`, `::1` or `localhost` exactly. `--browse` and `--navigation` are refused with it. Port: not done.
- Esc on a page that does not handle it took a full-screen window out of full screen #754. Cmd+Z in a form field of a page during a session's close grace brought the closed session back #827. Port: not done. Both come with HTML overlays.

**Windows:** Ctrl+click already reveals a plain drive path in Explorer, a port addition (PLATFORM-NOTES "Terminal links"). Upstream now reveals bare paths too, except images and markdown files, which open as a page. With Ctrl as the Windows Cmd, `toggle_overlay` would get Ctrl+Shift+J; nothing in the port binds that chord now.

## 6. Forms in `ask`

These extend the first catalog, section 4. The port's `ask` has buttons only.

- `--input line|multiline` adds a text field, with `--value` and `--placeholder`; the answer carries `text` #778, #779. `--choice id=label`, up to 50 items, makes a radio list, or a checkbox list with `--multiple`, with `--choice-subtitle` and `--checked`; the answer carries `selected`. `--other PLACEHOLDER` adds a free-form line after the items. A form gets OK and Cancel without `--button`, and `--cancel id` names the button that acts as Esc. Args: `cancelButton`, `input`, `placeholder`, `choices`, `multiple`, `checked`, `other`. Port: not done.
- The GUI one-line field matches the dialog: a rounded box with a thin outline #842. Port: not applicable: visual chrome, which parity skips.
- `ask --hotkey` letters were dead on non-Latin layouts #771. Now an ASCII-capable layout matches the typed letter, and any other layout the US letter at that physical position. Port: done in a Windows form in the ask port (commits `b03f3c4` to `838afa6`). `AskKeys` matches the virtual key, and Windows gives a non-Latin layout the virtual keys of the US letters.

## 7. Open Recent from the CLI

- `recent.list`, `recent.open [ID] [--window W]`, `recent.clear` #744. `recent list` prints the File ▸ Open Recent entries, newest first. `recent open` reopens one by id or prefix, or the newest without an id, through the menu's restore path. A reopened session keeps its id, split, flag, context and restore pins. It goes to `--window` or the frontmost window, unless another window already holds its session or workspace; the window is not raised. `--json` carries `closedAt`, the workspace id and the session ids. Results: `recent`, `recentOpen`. Port: done (B3). The port has the store behind it: `RecentClosedStore` in `recent-closed.json`, and `reopen_recent` on Ctrl+Shift+T (PLATFORM-NOTES). No name clashes.

## 8. Navigation and agent status

- The setting `attentionNavigationAcrossWindows`, off by default: next and previous attention step through every open window and bring forward the window they stop in #824. `session go --to next-attention|prev-attention` stay in its window. Port: done (B1, `parity-b1-attention`).
- The setting `attentionIncludesActive`, off by default: the attention steps and `session go --to next-attention|prev-attention` stop on active sessions too #807. Port: done (B1).
- Mark for Attention on the session row menu gives an idle session a `completed` status that survives a visit #763. Port: done (B1).
- `agtermctl session wait --target S --until completed --until blocked [--timeout SEC]` #765. It is a CLI command built on the event stream (`status`, `session.closed`) and `tree`, not a wire command. Exit 2 means the session left the tree, 124 the timeout. Port: done (B3), with no refused-connect retry.
- The agent status glyph in session pickers; `pick` items take `status`, `statusColor` and `statusShape` #774. Port: done in agterm-win #22 (upstream `dc1110bf`). The `zmx tree` status fields of the same change are not ported: the port has no zmx.
- `session close --undoable` gives one session the close grace the close chord gets; it falls back to a hard close when undo after closing is off #804. Arg and result `undoable`. Port: done (B1). The batch form on the live host now follows `closeGraceUndoEnabled` (PLATFORM-NOTES).
- With two OpenCode v1 permission prompts open, answering the first cleared `blocked` #734. Port: not done. The port has no OpenCode integration (first catalog, section 9).
- The Codex status hook checks for a blocked prompt every 2 seconds #802 and ignores Codex's memory thread #822. The bash status hook keeps the exit status for later `PROMPT_COMMAND` entries #828. Port: not applicable: the port does not install Codex or bash status hooks (PLATFORM-NOTES "Help menu").

## 9. Live and remote sessions

All items here depend on the first catalog, sections 1 and 2.

- HTML pages on a remote session #793. A page opens on the presenting Mac. A URL page loads there with that Mac's `--persistent` storage, and a loopback URL is refused. A file page is served from the origin over ssh: the viewer's helper claims the page with a single-use token through `session.overlay.page.serve`, and then the connection carries page frames. `tree` lists the page with `remote: true`. Port: not done.
- Overlays with `--width` and `--height` on a remote session #850. An overlay program on a remote session gets agterm's bundled `xterm-ghostty` terminfo #854. The status sound plays on the presenting Mac #775. A form `ask` goes to the presenting Mac only when its agterm supports forms #778. Port: not done.
- Remote companions #840, #849. On a remote row, the origin creates the companion, and this Mac attaches it. `zmx attach HOST ID --companions` sends `zmx.attach.group` (result `remoteGroup`) and attaches a session with all its companions. `zmx tree` lists each session's `companions`. `session.companion.page.attach` gives a viewer a single-use token for `session.overlay.page.serve`. Port: not done.
- fish as the login shell for Live sessions #761, #760, #768, #780. Port: not done.
- **Behavior change:** a Live `session new --command` pane closes when its command exits, and `--wait` keeps the final output #840. The output and scrollback stay when the command exits #859. Port: not done.
- Fixes: Ctrl-C on a restored or `--command` Live pane closed the pane and ended its daemon #773; `zmx tree` left out a session whose split stayed hidden across a relaunch #764; the bundled zmx publishes its socket only once it listens #793; `liveAttribution: unknown` after the session host ran for three days #733; the window-close sheet said closing ends the Live sessions #743. Port: not done.

## 10. Events and hooks

These extend the first catalog, section 8. The port has the event stream from the pin and, since B7, `hooks.conf`.

- `app.launched` fires once per app run, after `hooks.conf` is loaded and the control socket accepts connections #736. Port: done (B3, B7).
- `tree.changed` also fires when a program, page, pane overlay or HUD opens or closes #736. Port: partly done (B3): session overlays and HUDs; pane, page and remote overlays wait for those features. The port fires `tree.changed` on changes of the session tree and on those.
- `pane.focused` fires when focus moves between the split panes of a session. It carries the session, `pane` and `paneID`. A move and back within 100 ms is not reported #785. Port: done (B3).
- `message` from the message bus (section 1) #847. Port: done (B6).

## 11. Settings and persistence

Nothing is removed or renamed.

- `AppSettings`, new fields: `attentionIncludesActive` #807, `attentionNavigationAcrossWindows` #824 (both ported in B1), `ignoreReduceTransparency` #814, `sessionInfoDisplay` #864 (the field in B2d1, the picker in B2d2), `terminalInputSource` #727. Port: partly done; the two attention fields and `sessionInfoDisplay` are done.
- `ignoreReduceTransparency` keeps the window's opacity and blur while macOS Reduce Transparency is on; palettes still follow the system #814. Port: not done. The Windows switch is Settings > Personalization > Colors > Transparency effects, and the port reads it for overlay panels only (PLATFORM-NOTES "Windows-only extensions").
- `terminalInputSource` locks one keyboard layout while a terminal has the keyboard #727. A layout switch made in a terminal is switched back, and the previous layout returns when focus leaves. It has no control command. Port: not done.
- New values of existing settings: `overlayToggle`, `workspaceIcons` and `sessionIcons` in `shownInterfaceElements` (sections 3 and 5); `overlay95`, `overlay75` and `overlay50` in `linkOpenMode` (section 5). Port: not done.
- Snapshot: `flaggedOrder` #833. WorkspaceSnapshot: `tint` #836. SessionSnapshot: `tint` #836, `lastSelectedAt` #818, `parentID` and `collapsed` #840. Port: partly. `lastSelectedAt` (B2a), both `tint` fields (B2c1) and `flaggedOrder` (B5d1) are done; the rest is not done.

## 12. Behavior changes in commands already implemented

These items change what agterm-win already does. They do not depend on new features. Each adopted change is recorded in PLATFORM-NOTES "Upstream behavior adopted beyond the pin" with its upstream sha.

- A mouse selection no longer replaces the system clipboard; `session.selectall` writes the selection buffer #792. Port: done in agterm-win #24 (upstream `bca6ad8b`). The Windows-only `copyOnSelect` plays the role of Ghostty's `copy-on-select`; see PLATFORM-NOTES "Selection buffer and middle-click paste".
- A custom command in a session whose directory was removed now runs in HOME instead of being refused #740 (`e6fadf08`). `AGT_SESSION_PWD` still carries the path the session reported. This reverses upstream `312987af`, which the port adopted in `1668561`: the port refuses with `session directory <path> no longer exists`. Port: not done. See Open questions.
- **Behavior change:** `session scratch on --target A` shows the scratch on A without switching to A; `--follow` selects A #730 (`da375b93`). Port: done (B1).
- `session scratch on --command CMD` against a scratch already shown answered ok and never ran CMD #730. Port: done since `5dd02cf`: the port already closed the shown scratch and started it again with CMD, which is what upstream now does.
- A second instance on a state directory in use exits with status 1 before it reads or writes anything #749. Port: done in a Windows form in port PR 53 (`fa75507`): a second launch hands its activation to the running app and exits. See PLATFORM-NOTES "One instance per state directory".
- `agtermctl notify` no longer shares a notification slot with the session's OSC 9/777 banners, so the next terminal banner does not replace the note #772. Port: not done. In the port, an explicit `notify` and an OSC banner from the main pane get the same toast Tag and Group, so the banner replaces the note.
- A custom `command` cmd chord matches through the layout's Command table, as the menu does #796. Port: not applicable: Windows layouts have no separate Command table.

## 13. Outside parity

- macOS and rendering: a freeze when a busy terminal closed, fixed in libghostty #819; the generated config written before libghostty loads it #814; `split-divider-color` #829; a window drag on macOS 27.2 #869; AppKit tooltips #816; slow launch with many restored sessions, from SwiftUI layout #747; the selected row with a custom theme #746; the resize cursor after an ask #858; status glyphs pushed out of view by a companion row #841; a pane ask shading only its pane over a session-wide overlay #832; icons beside the toggles in Settings ▸ Interface #810.
- Homebrew cask: quarantine on upgrade #784, #801.
- No code: cookbook recipes #742, #752, #766, #783, #786, #789, #805, #806, #830, `08ccd78d`, #846, #853, #855, #860; documentation #725, #821.

## Open questions

- #740 reverses the refusal the port adopted from `312987af` (section 12). Either the port follows upstream and runs the command in the user profile directory, or it keeps the refusal and records the difference in PLATFORM-NOTES.
- `session.overlay.page.serve` and `session.companion.page.attach` are used only between two agterm apps (section 9), like `session.overlay.job.run` in the first catalog. Decide whether such commands go into parity with the remote feature.

## How to recompute

```
git -C <agterm-clone> fetch origin --tags
git -C <agterm-clone> log --oneline ed831ab..v0.40.0
```

A surface snapshot of the tag, without a checkout:

```
python tools/extract_surface.py --agterm "<agterm-clone>" --ref "v0.40.0" --out "surface-new.json"
```

Compare the result with `docs/upstream/surface-v0.36.0.json` for the size table, and with `parity/adopted.json` and `parity/windows-only.json` for name clashes. Event kinds are not in the snapshot; read them from `git -C <agterm-clone> show v0.40.0:agtermCore/Sources/agtermCore/ControlEvents.swift`.
