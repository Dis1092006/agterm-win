# B5c: Edit Flagged Sessions and Edit Focused Workspaces

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B5 is the flagged view. This is B5c: two palettes that list every session or workspace of the window with its mark, where Return toggles the mark and the palette stays open. It follows `docs/upstream/delta-ed831ab-to-v0.40.0.md`, section 4.

## Upstream commits

- `a2552826` (#803): the actions `edit_flagged_sessions` and `edit_focused_workspaces`, the two palette modes with their footer hint, the command palette rows, and the empty state of the flagged view with its "Add Sessions…" link.

## Tasks

1. Core: the two `BuiltinAction` values with no chord, the keybindings catalog, `parity/adopted.json`, the parity surface on `surface-v0.40.0.json`. Done.
2. Palette: the membership modes in `WindowPaletteProjection`, the row mark, the footer hint, the keep-open accept and the mark refresh. Done.
3. Actions: `WindowPaletteActions.ToggleMembership`, the router entries, the command palette rows. Done.
4. Main window: the handlers, the editor branch of the accept, and the mark and hint refresh on every sidebar snapshot. Done.
5. Sidebar: the empty flagged state and its link. Done.
6. Skill, PLATFORM-NOTES, catalog line, versions. Done.

## Decisions

- The focus editor fills the four-square grid for a member, as the sidebar does. Upstream uses a heavier stroke.
- The flagged editor draws a flag, filled for a flagged session and outlined otherwise.
- The command palette hides the rows when they cannot run. Upstream disables them.
- The empty flagged state comes in this PR, since its link runs `edit_flagged_sessions`.
