# B10: overlay visibility and point sizes

Upstream v0.40.0 can hide an overlay without closing it, and can size an overlay in points (`docs/upstream/delta-ed831ab-to-v0.40.0.md` §5, upstream `7f450d17` and `9bbb3408`). The port has only the session-wide program overlay: no pane overlays and no HTML pages. It takes the two features in two PRs.

## B10a: hide, show and toggle

- Wire commands `session.overlay.hide`, `session.overlay.show` and `session.overlay.toggle`, each with an optional `pane`. The answer is `{id, text}`, where `text` is the state left behind: `hidden` or `shown`.
- Errors as upstream: `no such session`, and `no overlay` when the session has nothing to hide or show. With `--pane left|right` the port always answers `no overlay`, because it has no pane overlays.
- A hidden overlay keeps running and keeps its exit status. `result`, `close` and `text` work on it and do not show it.
- `session overlay open` and a HUD open on a session with a hidden overlay answer `overlay hidden`. This check comes before `overlay already open`.
- Closing the overlay or opening a new one clears the hidden state. A `--wait` overlay stays hidden after its program exits.
- `tree` reports `hiddenOverlays: ["session"]`, omitted when nothing is hidden. `overlay` and `overlaySizePercent` keep reporting that the slot is taken. The human tree output tags the session `(overlay hidden)`. `tree.changed` fires only when the state actually changes.
- A hidden overlay does not cover the session: the page layout, focus, zoom, search, swap panes and the `ask` cover all see the terminal under it. Close Session (Alt+W) skips it. The close confirmation says the hidden overlays close with the session.
- The builtin action `toggle_overlay` on Ctrl+Shift+J (upstream Cmd+Shift+J). A keymap that already binds `cmd+shift+j` to another action keeps it, and `toggle_overlay` then has no chord. The palette row "Toggle Overlays" is listed while the active session has an overlay, shown or hidden.
- Opening the keymap or hooks editor on a session whose overlay is hidden is refused, and the app plays a sound, as upstream beeps.
- CLI `agtermctl session overlay hide|show|toggle [--pane left|right]`, with upstream's help text.
- Not in B10a: the title-bar button `overlayToggle`. It needs `shownInterfaceElements`, which the port does not have yet (PLATFORM-NOTES).

Status: done.

## B10b: `--width` and `--height` in points

- `session overlay open` and `session overlay resize` take `--width` and `--height` in points. Each replaces `--size-percent` on its own axis and is capped at 80% of the session.
- `tree` reports the laid-out size as `overlayWidth` and `overlayHeight`.

Status: not started.
