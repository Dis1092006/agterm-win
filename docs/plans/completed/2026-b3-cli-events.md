# B3: CLI commands and events

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B3 is the third item. It follows `docs/upstream/delta-ed831ab-to-v0.40.0.md`.

## Upstream commits

- `fff13ef2` (#765): `agtermctl session wait`.
- `6b564749` (#744): `recent.list`, `recent.open`, `recent.clear`.
- `fbe72e25` (#722): `agtermctl clipboard set`.
- `8c5ac7a0` (#736): `app.launched` and a wider `tree.changed`.
- `e958477b` (#785): `pane.focused`.

## Tasks

1. `session wait`: a CLI loop over `events.read` and `tree`, with exit codes 0, 2 and 124. Done.
2. `clipboard set`: Windows clipboard, UTF-8 check, stdin. Done.
3. `recent.*`: Core protocol, dispatcher, headless and live hosts, CLI. Done.
4. Events: `pane.focused` with a 100 ms debounce, `app.launched` once per run, `tree.changed` for overlays and HUDs. Done.
5. Parity lists, PLATFORM-NOTES, skill text, catalog lines, versions. Done.

## Decisions

- `pane.focused`, `pane.split`, `pane.scratch`, `session.selected`, `remote.*` and `message` events: only `pane.focused` and `app.launched` are here. The rest belong to B7 and later items.
- `app.launched` is emitted when the control server first accepts connections, once per process run.
- Pane, page and remote overlay triggers of `tree.changed` wait for B10 and B11.
- `recent.open` routes to the window that holds the entry's sessions, else its workspace, else the requested window.
