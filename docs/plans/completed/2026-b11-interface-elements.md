# B11: opt-in interface elements

Upstream v0.40.0 has three title bar elements that are off until the user turns them on: the workspace name in the title (`78000ae2`, #598), the custom-commands button (`28f79f17`, #582) and the overlay button (`7f450d17`, #790). Upstream keeps them in a second settings list, `shownInterfaceElements`. The port has only the pin's `hiddenInterfaceElements` (`docs/upstream/delta-7df70fe-to-v0.36.0.md` §10, `docs/upstream/delta-ed831ab-to-v0.40.0.md` §5 and §11). The port takes them in two PRs.

## B11a: `shownInterfaceElements`, the workspace name and the overlay button

- `InterfaceElement` gets `workspaceName` and `overlayToggle`, in upstream's declaration order. Both are hidden by default, as upstream's `hiddenByDefault`.
- `AppSettings` gets `shownInterfaceElements`, a list of raw names. It decodes the same tolerant way as `hiddenInterfaceElements` and is omitted when null.
- An element is hidden when it is hidden by default and not in `shownInterfaceElements`, or when it is shown by default and is in `hiddenInterfaceElements`. Unknown names are ignored, as upstream's `resolvedHiddenInterfaceElements`.
- The Interface toggle in Settings writes `shownInterfaceElements` for an element hidden by default and `hiddenInterfaceElements` for the rest. Upstream writes the changed list sorted and drops it when it becomes empty; the port does the same for `shownInterfaceElements`.
- The title bar shows "workspace — session — window" when `workspaceName` is on. The workspace is the one that holds the active session. A blank name is dropped, and a name longer than 24 characters is cut to 24 and gets "…", as upstream `TitlebarComposition`. The OS window title does not change.
- The overlay button sits in the second button group, after split, and counts toward that group for the dividers. It toggles the active session's overlay like `toggle_overlay`. It is disabled when the session has no overlay, and also while the terminal zoom or the dashboard is open. Its glyph is crossed out while the overlay is hidden. Its tooltip is "Hide Overlays" or "Show Overlays" with the `toggle_overlay` chord.
- Not in B11a: `remoteHost` (the port has no remote sessions) and `customCommands` (B11b).

Status: done (#82).

## B11b: the custom-commands button

- `InterfaceElement` gets `customCommands`, hidden by default.
- The button sits last in the third group and opens a menu of the keymap's `command` lines, each with its chord. It is disabled when the keymap has no command or no session is active. A click runs the command the way the palette does and returns focus to the terminal.
- When the keymap has more than 5 commands, the 5 most run ones lead the menu above a separator. Both groups keep keymap order.
- Run counts are kept in `custom-command-usage.json` in the state directory, as upstream `CustomCommandUsageStore`. A run from a chord, the palette, the menu or `keymap.run` counts: the runner counts a command once its process starts.

Status: done.
