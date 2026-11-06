# B5d: Flagged view order

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B5 is the flagged view. This is B5d: the flat and plain layouts of the flagged view keep a saved order per window, and the user can change it. It follows `docs/upstream/delta-ed831ab-to-v0.40.0.md`, section 4. It comes in two PRs: B5d1 is the order and its control surface, B5d2 is the drag.

## Upstream commits

- `8088e146` (#833): the saved order, `session flag move --before|--after`, `flaggedOrder` in the tree and in the window snapshot, the drag in all three layouts of the flagged view.

## Tasks

1. Core: the order model (`AppStore.FlaggedOrder.cs`), the hooks on flag, unflag, close, workspace removal, undo of a close and reopen, the close reselection by the saved order, `flaggedOrder` in the snapshot and the tree, `parity/adopted.json`. Done (B5d1).
2. Sidebar projection: the `flat` and `plain` layouts draw the saved order; navigation follows it. Done (B5d1).
3. Wire and CLI: mode `move` on `session.flag` in the dispatcher, the headless host and `agtermctl session flag move`. Done (B5d1).
4. Live: a window store takes the app-wide flagged layout before its restore. Done (B5d1).
5. Skill, PLATFORM-NOTES, catalog lines, versions. Done (B5d1).
6. Drag: reorder the rows of the flagged view by drag, in the `flat`, `plain` and `tree` layouts. B5d2.

## Decisions

- Spring-loaded expansion (a collapsed workspace opens while a drag rests on it) is not part of B5d. The port has none in the ordinary tree either; PLATFORM-NOTES records the difference.
- The headless host keeps the `flat` layout, the upstream default; only the live host reads the setting.
