# B5b: flag every session of a workspace

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B5 is the flagged view. This is B5b: one action flags or unflags every session of a workspace, from the workspace row menu and from `workspace.flag`. It follows `docs/upstream/delta-ed831ab-to-v0.40.0.md`, section 4.

## Upstream commits

- `14afbfd5` (#787): `AppStore.applyWorkspaceFlag` and `allSessionsFlagged`, `workspace.flag`, `agtermctl workspace flag`, and Flag All Sessions / Unflag All Sessions on the workspace row menu.
- `146747da`, its flagged-view half: a flag change or a switch to the flagged view moves a hidden selection to the most recent visible session. The upstream tests of `14afbfd5` depend on it.

## Tasks

1. Store: the flagged-view reselect in `SetSidebarMode` and both `SetFlag` overloads, then `AllSessionsFlagged` and `ApplyWorkspaceFlag`. Done.
2. Protocol: `workspace.flag` with its modes and error, the dispatcher case, and `parity/adopted.json`. Done.
3. Hosts: the headless host and the live host's workspace-target routing. Done.
4. CLI: `agtermctl workspace flag [on|off|toggle]`. Done.
5. Sidebar: the workspace menu item, its title flip and its disabled state on an empty workspace. Done.
6. Skill, PLATFORM-NOTES, catalog line, versions. Done.

## Decisions

- The flagged-view half of `146747da` is ported in this PR, since the upstream tests of `14afbfd5` need it.
- The menu item has its own group after Focus and Add to Focus, before Delete Workspace, as upstream. Tint stays after Icon….
