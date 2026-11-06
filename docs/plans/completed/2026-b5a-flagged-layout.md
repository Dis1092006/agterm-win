# B5a: flagged view layout

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B5 is the flagged view. This is B5a: the flagged view gets three layouts, `flat`, `plain` and `tree`. It follows `docs/upstream/delta-7df70fe-to-v0.36.0.md`, section 6, and `docs/upstream/delta-ed831ab-to-v0.40.0.md`, section 4.

## Upstream commits

- `902cd853` (#621): `FlaggedViewLayout` with `flat` and `tree`, the `flaggedViewLayout` setting, `sidebar.flagged-layout`, `sidebarFlaggedLayout` on the tree, and the flagged tree in the sidebar.
- `f8c3eba3` (#718): the `plain` layout.

## Tasks

1. Core: `FlaggedViewLayout`, the `flaggedViewLayout` setting with `flat` for a missing or unknown value, and the Settings editor picker on the General tab. Done.
2. Store and projection: `AppStore.FlaggedLayout`, `RendersWorkspaceRows`, the expand and collapse gates, and the flat, plain and tree projections of the flagged view. A layout change redraws the sidebar. Done.
3. Protocol: `sidebar.flagged-layout` with its modes and error, `sidebarFlaggedLayout` on the tree, and `parity/adopted.json`. Done.
4. Hosts: the headless and live hosts set the layout and ask the app to save it. The save keeps pane zoom and puts the old layout back when it fails. Done.
5. CLI: `agtermctl sidebar flagged-layout`. Done.
6. Sidebar: the flagged tree's workspace rows indent, toggle, expand and rename as in the ordinary tree, the rename seeds the name the layout shows, and the palette's Expand and Collapse follow `RendersWorkspaceRows`. Done.
7. Skill, PLATFORM-NOTES, catalog lines, versions. Done.

## Decisions

- The layout is app-wide, as upstream. The store holds it, and the setting is its saved form.
- The flagged tree shares the collapse state with the ordinary tree, as upstream.
- The port does not expand the selected session's workspace in the view on a view switch. It has no view-only expansion in the ordinary tree either.
- A drag in the flagged tree does not move a row, until the flagged reorder (B5d).
