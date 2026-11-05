# B7: event hooks and the new events

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B7 is the seventh item. It follows `docs/upstream/delta-7df70fe-to-v0.36.0.md` section 8 and `docs/upstream/delta-ed831ab-to-v0.40.0.md` section 10.

## Upstream commits

- `a4944436` (#607): `hooks.conf`, `hooks.list`, `hooks.reload`, `pane.split`, `pane.scratch`.
- #694: `session.selected` with `previous`, and `previous` on `status`.
- #610: `remote.opened` and `remote.closed`, parse-only here.

## Tasks

1. Events: `session.selected`, `pane.split`, `pane.scratch`, `previous`, and the parse-only `remote.*` kinds. Done.
2. Hooks core: the `hooks.conf` parser with diagnostics, the starter file, the per-line scheduler (one child at a time, 256 pending, oldest dropped), and the controller that reads the event ring. Done.
3. Protocol: `hooks.reload` and `hooks.list`, the `hooks` result field, dispatcher refusal of a target or `--window`. Done.
4. CLI: `agtermctl hooks reload` and `hooks list`. Done.
5. Hosts: the headless host and the live host share one controller; `HookProcessRunner` runs `cmd.exe /d /s /c` with the `AGT_*` variables. Done.
6. App UI: palette Edit Hooks and Reload Hooks, a Reload button and diagnostics in Settings, a banner for skipped lines and for a failed hook. Done.
7. Parity lists, PLATFORM-NOTES, skill text, catalog lines, versions. Done.

## Decisions

- Hooks are app-wide. One controller serves every window.
- The port has no File menu, so Edit Hooks and Reload Hooks are palette rows and Settings has the reload button.
- The shell is cmd.exe. A hook line reads `%VAR%`, not `$VAR`.
- `remote.opened`, `remote.closed` and `AGT_EVENT_HOST` stay inert: there are no remote sessions.
- The `message` event and the message bus are B6.
