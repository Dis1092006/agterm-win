# B2d2: session info line, sidebar drawing and Settings picker

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B2d is the session info line. B2d1 brought the model, wire, CLI, tree, expiry and the `sessionInfoDisplay` setting field. This is B2d2: the sidebar draws the line and the Settings window gets the picker. It follows `docs/upstream/delta-ed831ab-to-v0.40.0.md`, section 3.

## Upstream commits

- `80f822bf` (#864): `SidebarInfoMetrics`, `SidebarInfoCellView`, the taller row with its title band, and the Session info picker on the Interface tab.

## Tasks

1. Store: a set, a clear and an expiry of the line refresh the sidebar, not only the tree. Done.
2. Projection: `SidebarRow.Info` on session rows, and `SessionInfoDisplays.Shows` for the off, flagged and always gate. Done.
3. Settings editor: the `sessionInfoDisplay` Choice field on the Interface tab, with Always stored as no key. Done.
4. Xterm: `SidebarInfoMetrics`, `SidebarInfoInks` for the row and label theme styles, and `SidebarInfoLine.Runs` with upstream's opacities. Done.
5. App: the row grows by the line, the title keeps its band, the info `TextBlock` with its runs, ellipsis and tooltip, and the sidebar foreground passed to the rows. Done.
6. CLI help, skill, PLATFORM-NOTES, catalog lines, versions. Done.

## Decisions

- The line follows the overlap decisions from B2d1: in row style it sits inside the colored row and the selection frame, in the theme's colors; in label style it sits under the block on the sidebar background; a name tint does not color it; a hidden icon moves it left with the name.
- Color emoji stay at the text size, because a WinUI `Run` has no baseline offset to center a smaller glyph.
- The line height is the font size times 1.33, the height of a Segoe UI line.
- The Settings field has a hint, as the port's other fields do.
