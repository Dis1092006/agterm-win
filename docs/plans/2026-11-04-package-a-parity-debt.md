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

## A2 to A6

Detailed tasks are written when the PR starts, from the same sources: the catalog item, the upstream sha it names, and `git show v0.40.0:<path>`. Known dependencies: A2 needs the pane id model that `session overlay --pane` and `AGTERM_PANE_ID` already use in the port (read it before planning); A5 needs a loader that turns an unknown optional value into absent without dropping the snapshot (#363); A4 `session.swap` must keep pane ids in place, as the port's pane ids are stable identities.
