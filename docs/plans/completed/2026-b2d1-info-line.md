# B2d1: session info line, wire and model

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B2 (sidebar rows) is split into B2a `lastSelectedAt`, B2b icon toggles, B2c tint and B2d info line. B2d is split in two: B2d1 is the model, wire, CLI, tree, expiry and the `sessionInfoDisplay` setting field; B2d2 draws the line in the sidebar and adds the Settings picker. This is B2d1. It follows `docs/upstream/delta-ed831ab-to-v0.40.0.md`, section 3.

## Upstream commits

- `80f822bf` (#864): `session.info` with `--markdown` and `--hide-after`, `info`, `infoMarkdown` and `infoHideAfter` on the tree session node, and the `sessionInfoDisplay` setting.

## Tasks

1. Core model: `SessionInfo` with upstream's limits and refusals, and `SessionInfoMarkup`, the inline markdown and `^[text](attributes)` parser. Done.
2. Store: `SetInfo`, the `--hide-after` expiry, and dropping the line on clear, session close, workspace removal, soft close of a timed line and a finalized close. The line is never saved or duplicated. Done.
3. Tree: `info`, `infoMarkdown` and `infoHideAfter` on the session node, omitted when there is no line. Done.
4. Protocol: the command, `ControlDispatcher` with upstream's error strings, and the headless host. Done.
5. Setting: `sessionInfoDisplay` with `EffectiveSessionInfoDisplay`. The Settings editor lists it as deferred until the picker exists. Done.
6. Live host: a window close and the app exit drop the lines and cancel their timers. Done.
7. CLI: `session info`. Done.
8. E2E: the wire over the pipe, and a CLI round trip. Done.
9. Skill, parity, PLATFORM-NOTES, catalog lines, versions. Done.

## Decisions

- The expiry timer lives in the store, on the same host lock as the HUD auto-hide, not in the control server as upstream has it.
- The skill says the sidebar does not draw the line yet, and the command help leaves out upstream's sentence about the Settings picker. B2d2 adds both.
- B2d2 follows the overlap decisions agreed for the row and label theme styles and for tints: in row style the line sits inside the colored row and the selection frame encloses it; in label style it sits below the theme block in the normal colors; markup colors take the ANSI colors of the theme under the text; a name tint does not color the line; a hidden icon moves the line left; a long line ends with an ellipsis.
