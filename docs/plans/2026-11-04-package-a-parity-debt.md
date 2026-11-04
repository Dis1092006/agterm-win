# Package A: parity debt from the upstream delta catalogs

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement each PR task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Close the small and medium "Port: not done" items of `docs/upstream/delta-7df70fe-to-v0.36.0.md` that need no new subsystem, in six PRs, so that later features (message bus, hooks, HTML overlays) build on the same wire surface as upstream.

**Why this order:** A1 and A2 are wire-only. A2 gives every pane a stable id that the message bus (second catalog, section 1) addresses. A5 changes the saved format, so it goes before A3, A4 and A6 add fields to it.

## PR slices

| PR | Items | Catalog |
|---|---|---|
| A1 | `version`, `sidebar.width`, `workspace.go`, `window.go` | 1 §6, §10 |
| A2 | `--pane-id` on `session type`, `session text`; `surfaces[].paneID`; `result.pane`; `AGT_PANE_ID`; `unknown pane id: X` | 1 §5, §12 |
| A5 | `focusedWorkspaceIDs` + `focusEnabled` migration; an unknown optional value loads as absent | 1 §11 |
| A3 | `session.context`, `realized`, `splitCwd` in `tree` | 1 §10 |
| A4 | horizontal split (`axis`), `session.split.close`, `session.swap`, `surface.cursor`, divider double click | 1 §5 |
| A6 | `statusChangedAt`, `statusReset`, a blocked pane is not overwritten by its neighbor | 1 §9 |

Each PR: raises `<Version>`, updates the skill and the plugin version when a wire command or a CLI flag changes, adds its names to `parity/adopted.json`, records every difference from upstream in `docs/PLATFORM-NOTES.md` ("Upstream features adopted beyond the pin"), and flips the `Port:` line of its items in the catalog. The last PR moves this plan to `docs/plans/completed/`.

## A1: version, sidebar.width, workspace.go, window.go

Upstream reference (`git -C <agterm-clone> show v0.40.0:<path>`): `ControlDispatcher.swift` (`.version`, `.sidebarWidth`, `.workspaceGo`, `.windowGo`), `ControlServer+AppCommands.swift` (`setSidebarWidth`, `appIdentity`), `ControlServer+WorkspaceCommands.swift` (`goWorkspace`), `ControlServer+WindowCommands.swift` (`windowGo`), `AppStore.swift` (`navigateWorkspace`), `AppStore+Focus.swift` (`canStepWorkspaces`), `AppStore+Sidebar.swift`, `WindowLibrary.swift` (`navigateWindow`), `AppIdentity.swift`, `SocketClient.swift` (`formatIdentity`).

### Global Constraints

- Wire names `version`, `sidebar.width`, `workspace.go`, `window.go`. Args: `sidebarWidth` (new), `to` and `window` (in the pin). Results: `app`, `installed`, `sidebarWidth` (new); `id` (in the pin).
- Error strings verbatim: `sidebar.width requires a width in points`, `workspace.go requires --to next|prev`, `no other workspace to navigate to`, `window.go requires --to next|prev`, `no other open window to navigate to`.
- `sidebar.width`: a non-finite or missing width is refused; a finite one is clamped to `SidebarWidthMin..SidebarWidthMax` (160..560, already in `AppStore`); the answer is the stored width in `result.sidebarWidth`, `ok` either way; a clean no-op write when the value did not change. `--window` selects the open window, default frontmost.
- `workspace.go`: steps the current workspace through `VisibleWorkspaces`, wrapping; collapse state is not a term; `to` is `next` or `prev` (also `previous`, as upstream `WorkspaceNavigation(wire:)` — read it before writing the parser); refused with `no other workspace to navigate to` when the sidebar is in flagged mode, when there are no visible workspaces, or when the current workspace is the only visible one. When the current workspace is outside the visible set, `next` lands on the first and `prev` on the last. Answers `id` of the workspace.
- `window.go`: steps through the open windows in library order, wrapping from the active window; refused below two open windows; raises the target like `window.select` does for an open window; answers `id`. Closed windows are never reopened.
- `version`: app-global, no target, no `--window`, works with no open window. `result.app = {version, commit?}`; `commit` is omitted when empty or the literal `unknown`. `result.installed` is the identity of the executable on disk, read fresh on every call, omitted when it cannot be read or names no version. CLI prints `VERSION` or `VERSION (COMMIT)`, then an `installed: ...` line only when `installed` differs from `app`.
- Windows form of the identity: version is the app `<Version>` (assembly informational version without the `+commit` suffix); commit is the suffix when the build recorded one; installed is the file version info of `Environment.ProcessPath`. Record the difference in PLATFORM-NOTES.
- Not in A1: the actions `previous_window`, `next_window`, `previous_workspace`, `next_workspace`, `toggle_workspace_collapse`; `tree.app`. Both come with the palette and keymap work, because a new `BuiltinAction` changes the count `keymap.list` and the tests pin.
- `parity/adopted.json`, with the upstream sha of the commit that added each name: `version` and result `app` (`5d22904a`), result `installed` (`18da29cb`), `sidebar.width`, arg `sidebarWidth` and result `sidebarWidth` (`7977f202`), `workspace.go` (`3624dd05`), `window.go` (`99c8c50d`).
- TDD, each test seen failing first. Warnings are errors. Test projects run one at a time; Xterm with `--blame-hang-timeout 4m`.

### Tasks

- [x] **Task 1: store.** `AppStore.SetSidebarWidth(double)`, `CanStepWorkspaces`, `NavigateWorkspace(next|prev)` in `src/AgTerm.Core/Model/AppStore.cs`. Tests in `tests/AgTerm.Core.Tests/Model/`: clamp both ends, unchanged value does not save, wrap both ways, focus filter confines the step, flagged mode and a lone workspace refuse, a current workspace outside the visible set.
- [x] **Task 2: protocol.** `Command.Version`, `SidebarWidth`, `WorkspaceGo`, `WindowGo` and wire names in `Command.cs`; `ControlArgs.SidebarWidth`; `ControlResult.App`, `Installed`, `SidebarWidth`; `ControlAppIdentity` record; JSON context entries; dispatcher cases and the verbatim errors; `IControlActions` methods. Tests: decode and encode round trips, omission of null fields, every error string.
- [x] **Task 3: headless host.** `HeadlessControlActions` answers all four (headless `window.go` has one window, so it answers the refusal). Tests in `tests/AgTerm.ControlServer.Tests` over the pipe (`HeadlessVersionWidthGoTests`); a separate E2E suite would repeat them.
- [x] **Task 4: live host.** `LiveApplicationHost`: `version` as an app-global command; `sidebar.width` through the window adapter; `workspace.go` through the window adapter; `window.go` next to `SelectWindowAsync`, using `_windows`/`_openWindows` order and `ActivateWindowAsync` + `ReportActivatedAsync`. The sidebar view applies the width through the same snapshot as `sidebar.expand`. Live tests are in `tests/AgTerm.Xterm.Tests/LiveWindowControlE2ETests.cs`; the width reads back from the window's store, because `tree` does not carry it.
- [x] **Task 5: CLI.** `agtermctl version`, `sidebar width N`, `workspace go --to next|prev`, `window go --to next|prev`; the human formatter for `version`. Tests next to the existing CLI tests, including the `installed:` line.
- [x] **Task 6: docs and parity.** `parity/adopted.json`, PLATFORM-NOTES entry, the skill text and the plugin version in the three files, the `Port:` lines in the first catalog (§6 `window.go`, `workspace.go`, `sidebar.width`; §10 `version`), `<Version>` in `Directory.Build.props`. Run `dotnet test AgTerm.Core.Tests`, `AgTerm.Cli.Tests` (skill and parity tests), then the rest one project at a time.

## Review Focus

- `workspace.go` in flagged mode and with one workspace answers the refusal, not `ok`.
- `window.go` never reopens a closed window and does not change the order of `window.list`.
- `version` works while no window is open, and `installed` follows the file on disk, not the running assembly.
- A width set over the pipe survives a relaunch (it is in `Snapshot.sidebarWidth` already).

## A2: --pane-id on session type and session text

Upstream reference: `eab672be` (#698) — `Session.swift` (`paneAddress`), `ControlServer+SurfaceIO.swift` (`readSessionText`), `ControlServer+SessionActions.swift` (`typeSession`, `setSessionRestore`), `ControlProjection.swift` (`ControlSurfaceNode.paneID`), `ControlDispatcher.swift`; `cf31c5d4` — `CommandContext` (`AGT_PANE_ID`).

### Global Constraints

- `session.type` and `session.text` take `paneID` (already in the pin as an arg). `session.type` is new here; `session.text` reads it too.
- One rule for `session.text`, `session.type` and `session.restore`: a live token wins over `pane`; an empty token counts as absent; an unknown token with no `pane` beside it fails with `unknown pane id: <token>`; an unknown token with `pane` falls back to `pane`. `session.status` keeps its own fallback. Core primitive: `Session.PaneAddress(token, pane)` returning `Pane(StatusPane?)` or `UnknownToken(string)`.
- `result.pane` (`left|right|scratch`) on every successful `session.text` (the surface actually read: scratch, split or main by identity) and `session.type` (the resolved pane, `left` when none).
- `tree` `surfaces[].paneID` is the `PaneToken` of the surface in that slot, omitted when absent or empty. The overlay slot never has one. It follows the slot, so it moves with a split promotion.
- `AGT_PANE_ID` keymap token, right after `AGT_PANE`: the token of the surface in the slot `AGT_PANE` names; empty for an empty slot or a session-free context. It is not session-scoped: it stays allowed in a sessionless launcher.
- Behavior change to record in PLATFORM-NOTES ("Upstream behavior adopted beyond the pin"): `session text --pane-id <unknown>` with no `--pane` used to read the on-screen pane and answer ok; it now errors.
- Not in A2: `surface.cursor --pane-id` (A4). The port realizes surfaces synchronously inside the store, so there is no wait loop to make token-aware; record that in PLATFORM-NOTES.
- `parity/adopted.json`: `result.pane` already adopted (`1b765e69`); no new command, arg or result name. The surface node and the token list have no parity section, so the tests below pin them.
- TDD, each test seen failing first. Warnings are errors. Test projects run one at a time; Xterm with `--blame-hang-timeout 4m`.

### Tasks

- [x] **Task 1: core.** `Session.PaneAddress` and `Session.PaneToken(StatusPane)` (empty for an empty slot) in `Session.cs`. Tests in `SessionTests.cs`: the eight upstream cases (live token, token with other pane, gone token with pane, gone token alone, empty with pane, empty alone, null with pane, null alone).
- [x] **Task 2: tree.** `ControlSurfaceNode.PaneId` (`paneID`, omitted when null) and `AppStore.ControlTree` filling it per slot. Tests: JSON encodes and omits; tree reports each slot's token and follows a swap/promotion.
- [x] **Task 3: protocol.** `PaneId` on `ControlSessionTypeOptions` and `ControlSessionTextOptions` (trailing, default null); the dispatcher forwards `args.paneID`. Tests in `ControlDispatcherTests.cs`.
- [x] **Task 4: headless host.** `TypeSession` and `ReadSessionText` resolve through `PaneAddress`, answer `unknown pane id: X` and `result.pane`; `SetSessionRestore` moves onto `PaneAddress` with its behavior unchanged. Tests in `tests/AgTerm.ControlServer.Tests` over the pipe; the live host shares these implementations, so a live test only checks one real round trip.
- [x] **Task 5: keymap token.** `CommandContext.PaneId` and the `AGT_PANE_ID` row; `CustomCommandExecution` fills it from the focused slot's surface. Tests: `CommandContextTests` (eleven keys, expand, sessionless launcher allowed), `LiveCustomCommandTests`.
- [x] **Task 6: CLI.** `--pane-id` on `session type` and `session text` with the upstream help text. Tests next to the existing CLI tests.
- [x] **Task 7: docs and parity.** Skill (`SKILL.md`, `reference.md`, `examples.md`), plugin version 0.1.26 in the three files, `<Version>` 0.1.75, PLATFORM-NOTES, `Port:` lines in the first catalog (§5 and §12, lines 92 and 167). `docs/agterm-windows-spec.md` stays at the pin, so its token list is not changed. Run `AgTerm.Core.Tests`, `AgTerm.Cli.Tests`, then the rest one project at a time.

## A5: focus as a set of workspaces

Scope grew beyond the snapshot migration by decision of the owner: A5 is the whole of upstream #297 (`6722755a`), because the migration is only the saved half of that feature. Upstream reference: `6722755a` and its follow-ups `146747da` (the active session stays inside the visible set), `13a8d3d9`, `85acea33`, `d0fd07b7` (lossy snapshot decode, legacy migration). Files at v0.36.0: `AppStore+Focus.swift`, `AppStore+CloseReselection.swift`, `AppStore+PendingClose.swift`, `AppStore+RecentClosed.swift`, `Snapshot.swift`, `ControlModes.swift`, `ControlDispatcher.swift`, `ControlProjection.swift`, `SidebarDrop.swift`, `agtermctlKit/WorkspaceCommands.swift`, `PaletteCatalog.swift`, `BuiltinAction.swift`. Their tests are the spec: `AppStoreFocusTests`, `PersistenceTests`, `ControlDispatcherWorkspaceTests`, `ControlModesTests`, `RecentClosedTests`, `SidebarDropTests`.

### Global Constraints

- The store holds `FocusedWorkspaceIds` (a set) and `FocusEnabled`. `CommitFocus(ids, enabled)` is the single write point: `enabled` with an empty set is clamped to false, an unchanged write is skipped, otherwise it forgets a hidden fresh workspace, prunes the sidebar selection, reselects when the selection is hidden, and saves.
- `DisableFocusIfSelectionOutsideSet` replaces `AutoUnfocusIfOutsideFocus`. It switches the filter off and keeps the set. It does nothing in flagged mode.
- `AddWorkspace(clearFocus)` and `EnsureWorkspace(clearFocus)` become `revealNewWorkspace`. A new workspace joins the set only while the filter is on.
- Snapshot: `focusedWorkspaceIDs` (tree order, omitted when empty) and `focusEnabled` (true or omitted). A file that has the legacy `focusedWorkspaceID` and no `focusedWorkspaceIDs` key reads as a one-member enabled set. The legacy key is never written. A malformed optional at any depth reads as absent and keeps the tree.
- Wire: `workspace.focus` takes `add`; the invalid-mode error is `invalid focus mode: <raw> (on|off|toggle|add)`. `workspace.filter` is new (window scoped, modes `on|off|toggle`, default `toggle`, error `invalid workspace filter mode: <raw>`). `tree` has top-level `workspaceFilter`; a workspace node's `focused` is membership, whatever the filter says.
- Close records carry a `focusMember` flag, so undo and Reopen Closed Item put the workspace back into the set. An entry written before the flag has none.
- Out of scope: palette edit of the set (#803, v0.39.0), flagged-mode reselect on `SetFlag` and `SetSidebarMode` (the other half of `146747da`).
- TDD, each test seen failing first. Warnings are errors. Test projects run one at a time; Xterm with `--blame-hang-timeout 4m`.

### Tasks

- [x] **Task 1: snapshot.** `FocusedWorkspaceIds` and `FocusEnabled` on `Snapshot`, a lossy decode for every optional (workspace and session too), the legacy migration. Tests in `SnapshotTests.cs` and `PersistenceStoreTests`.
- [x] **Task 2: core model.** `AppStore.Focus.cs`, the lifecycle hooks (select, add, move, remove, close reselect, restore), tree `Focused` and `WorkspaceFilter`, `focusMember` on pending-close and recent-closed records, `SidebarProjection` and `SidebarDrop`. Port `AppStoreFocusTests`; update the existing tests that read `FocusedWorkspaceId`.
- [x] **Task 3: protocol and hosts.** `workspace.filter`, the `add` mode, the dispatcher, `HeadlessControlActions`, the live host.
- [x] **Task 4: CLI.** `workspace focus [on|off|toggle|add]`, `workspace filter [on|off|toggle]`.
- [x] **Task 5: action and palette.** `toggle_workspace_filter` (no chord), palette "Add Workspace to Focus", "Toggle Workspace Filter", "Clear Focus".
- [x] **Task 6: sidebar UI.** Marked rows, the row menu entry, the bottom-bar filter toggle in place of the focus pill, `SidebarController`.
- [x] **Task 7: docs and parity.** Skill (`SKILL.md`, `reference.md`, `examples.md`), plugin version 0.1.27 in the three files, `<Version>` 0.1.76, `parity/adopted.json`, PLATFORM-NOTES, `Port:` lines in the first catalog (§6 and §11), wire-command count 81 in `SkillTextTests`.

## A3, A4, A6

Detailed tasks are written when each PR starts, from the same sources: the catalog item, the upstream sha it names, and `git show v0.40.0:<path>`. A4 `session.swap` must keep pane ids in place, as the port's pane ids are stable identities.
