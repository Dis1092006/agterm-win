# B2b: workspace and session icon toggles

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B2 (sidebar rows) is split into four PRs: B2a `lastSelectedAt`, B2b icon toggles, B2c tint, B2d info line. This is B2b. It follows `docs/upstream/delta-ed831ab-to-v0.40.0.md`, section 3.

## Upstream commits

- `91332589` (#865): the interface elements `workspaceIcons` and `sessionIcons`. A hidden icon gives its width and its gap to the name, and hides the state it shows.

## Tasks

1. Core: `InterfaceElement.WorkspaceIcons` and `SessionIcons`, after `workspaceAddSession`, as upstream's order. Done.
2. Xterm: `SidebarRowIcon.IsShown` per row kind, and `SidebarRowIndent.Of` with the icon hidden. Done.
3. App: the row view model hides the workspace icons and the session icon, and moves the session row left. Done.
4. PLATFORM-NOTES, catalog line, version. Done.

## Decisions

- A hidden session icon collapses the icon and moves the row content left by the icon column and its spacing, 22 px. The name then sits where the icon was, in the tree and in the flagged view.
- The workspace icons sit in the name's grid with their own margin, so collapsing them leaves no gap and needs no shift.
- No skill or parity change: the skill does not list interface elements, and the parity files do not list their values.
