# keymap list and keymap run: design

## Intent

`keymap.reload` is the only keymap command on the pipe today, and it answers with a diagnostic count. A script or an agent cannot read what the keymap resolved to, and cannot start one of the user's custom commands. The Keyboard Shortcuts view (Ctrl+Shift+F1) shows most of this, but only to a person.

Upstream added both verbs after the parity pin: `keymap.list` in `f4b7a4aa` (#301) and `keymap.run` in `49628f8a`. This port takes them now, before the custom command failure panel (`docs/plans/2026-10-26-custom-command-error-hud.md`), so that the failure panel's three options can be read back through `keymap list` when they arrive.

## Goals

- `agtermctl keymap list [--json]`, wire `keymap.list`: the resolved keymap, as upstream reports it, minus the macOS-only parts.
- `agtermctl keymap run NAME [--target T] [--window W]`, wire `keymap.run`: start a custom command by its exact name against a session, as the custom command palette does.
- Byte-compatible JSON for every field that is kept, and the upstream error strings verbatim.

## Non-goals

- `menu[]`: the key equivalents of the macOS menu bar. The port has no menu that carries chords. The field is omitted, as upstream omits it when it is nil.
- `alternates` (chord alternatives with `|`, upstream #420) and `--repeat` (upstream #682). The port has neither feature, so `alternates` and an action's `repeats` are never written.
- `errorHud`, `errorPosition`, `errorPane` on commands. They come with the failure panel work.
- Running a command from an HTML page's own session. The port has no HTML overlays.
- Changing the Keyboard Shortcuts view or `keymap.reload`.

## Anti-goals

- A `keymap list` that disagrees with what `keymap reload` just loaded. The list comes from the same `Keymap` object the windows arm, not from a second parse of the file.
- A `keymap run` that runs a command differently from the palette: other working directory, other environment, other pane.

## Contract

### keymap.list

App-global, no target, no args. `ok` with `result.keymap`:

```json
{
  "path": "C:\\Users\\me\\.agterm-win\\keymap.conf",
  "actions": [
    { "action": "new_window", "chord": "ctrl+alt+n" },
    { "action": "toggle_split", "chord": "ctrl+shift+d", "overridden": true },
    { "action": "rename_session" }
  ],
  "commands": [
    { "name": "Build", "shortcut": "ctrl+shift+b", "repeats": false },
    { "name": "Notes", "repeats": false }
  ],
  "diagnostics": [ { "line": 4, "message": "unknown verb 'mapp'" } ]
}
```

- `path`: the `keymap.conf` the keymap was loaded from, the same path `keymap.reload` reads.
- `actions[]`: every one of the port's 41 builtin actions, in `BuiltinActions.All` order, bound or not. `action` is the wire name. `chord` is the resolved chord (`Keymap.EquivalentFor`, so an unmoved action shows its Windows default) in the Windows spelling of the keymap.conf syntax: the modifiers `ctrl`, `alt`, `shift` in that order, then the key, where `cmd` and `ctrl` both become `ctrl` and `opt` becomes `alt` (the keys `WindowsChordDecoder.Effective` resolves them to). So `new_window` shows `ctrl+alt+n` and `toggle_split` shows `alt+d`; the string can be pasted into a `map` line. It is omitted only for the actions with no shipped chord (`DefaultChord` is null and no `map` line binds them); a `map` line cannot unbind an action. `overridden: true` only when the resolved chord is a different key from the shipped one, compared after `WindowsChordDecoder.Effective` on both sides with `WindowsDefaultChords.ShippedFor(action)` as the shipped one, so `map ctrl+alt+n new_window` reports no override; omitted otherwise. The action names in the examples are only examples.
- `commands[]`: the custom commands in file order, after the parser's conflict pass. `name`; `shortcut` in the file's own spelling, omitted for a palette-only command (including one whose shortcut the conflict pass dropped); `repeats` always present and always `false` (upstream encodes it as a non-optional Bool).
- `diagnostics[]`: `line` and `message` for each parser diagnostic (the list `keymap.reload` counts), followed by the Windows shadowing diagnostics the Keyboard Shortcuts view also shows (`WindowCommandRouter.EffectiveWindowsChordDiagnostics`, for example `effective Windows chord 'Ctrl+F' resolves to built-in 'toggle_search'; built-in 'toggle_fullscreen' is shadowed`). Without them an action can be listed with a chord that never fires. Line 0 is the cross-section sentinel. The `keymap.reload` count stays parser-only, as upstream.
- Field names, order and omission follow upstream `ControlKeymap.swift` at `ed831ab`.

Human output, upstream `SocketClient.formatKeymap` without the `menu:` block and without `--repeat` marks:

```
keymap: C:\Users\me\.agterm-win\keymap.conf

actions:
    new_window      ctrl+alt+n
  * toggle_split    ctrl+shift+d
    rename_session  -

commands:
    Build  ctrl+shift+b
    Notes  (palette only)

diagnostics:
    line 4: unknown verb 'mapp'
```

The `commands:` and `diagnostics:` blocks are left out when empty. A diagnostic on line 0 prints without `line 0:`.

### keymap.run

Takes `args.name`, an optional target (default `active`) and `args.window`. The name is matched exactly, no trimming. Errors, verbatim:

- `keymap.run requires a command name`: name missing or empty.
- `no custom command named NAME`: no command with that name in the current keymap.
- `custom commands are not available`: the host cannot run them (the headless host).
- `no such session: TARGET`, `ambiguous session prefix 'TARGET' → …` and the usual window resolution errors.
- `NAME did not start: REASON`: the launch failed, for example `session directory <path> no longer exists`.

On success `ok` with `result.id`, the session's id. The reply means the process started. Exit status and output are not reported. The human output is `ok`.

The command runs as it does from the custom command palette for that session: the session's focused pane, primary or split and never the scratch terminal or an overlay; that pane's working directory and selection; the same `AGT_*` values and tokens. The target session does not have to be the active one, and running it does not select it.

## Constraints

- Parity policy: wire names, args, results and error strings match upstream. Record `keymap.list` (`f4b7a4aa`), `keymap.run` (`49628f8a`) and the result field `keymap` (`f4b7a4aa`) in `parity/adopted.json`; `name` is already in the pin. The parity tests check that adopted entries exist in `docs/upstream/surface-v0.36.0.json`.
- `docs/PLATFORM-NOTES.md`, section `## Upstream features adopted beyond the pin`: what is left out and why (menu, alternates, repeats, HTML pages), that `chord` shows Windows defaults, and that the headless host refuses `keymap.run`.
- The skill (`plugins/agterm/skills/agterm/SKILL.md`, `reference.md`) names both commands, the command summary count goes from 65 to 67, and `SkillTextTests` stay green. The plugin version goes from 0.1.9 to 0.1.10 in all three manifests.
- The app version goes up (main is 0.1.31).
- In a multi-window app, `keymap.list` answers from the frontmost window: every window loads the same file and `keymap.reload` reloads all of them. `keymap.run` runs in the window that owns the target session, so it joins the single-session-target routing in `LiveApplicationHost`.
- The list and `keymap.run` use the keymap `HeadlessControlActions` last loaded (`LoadKeymap` or `ReloadKeymap`) and its diagnostics, not a fresh parse. The window arms exactly that object, but a moment later (dispatcher queue, then the hotkey manifest publish), and a failed publish leaves the old one armed. Answering from the window's armed copy made `keymap reload` followed by `keymap list` show the old file, and upstream's `keymap.list` also reads what `reload` loaded.
- `keymap.run` against a session other than the active one needs a resolver path by session id: `CustomCommandInvocationResolver.ResolveSession` uses `store.ActiveSession` for the palette today.
- TDD, warnings as errors, test projects run one at a time.

## Decided

- Both verbs in one PR; the failure panel follows in its own PR.
- `menu[]`, `alternates`, action `repeats` are left out; command `repeats` is written as `false`.
- `--error-*` fields are not added in this PR.
- A second `command` line whose name is already taken is skipped with the diagnostic `command '<name>' is already defined; command skipped`, as upstream `68e17937` (#584) does, so a name addresses at most one command. This is recorded in PLATFORM-NOTES `## Upstream behavior adopted beyond the pin`. (owner, 2026-10-26)
- Action chords are shown in the Windows spelling of the keymap.conf syntax (`ctrl+alt+n`, not `cmd+opt+n`), and `overridden` compares the Windows keys, not the written chords. A custom command's `shortcut` stays in the file's own spelling, as upstream. (owner, 2026-10-26)
- `diagnostics[]` adds the Windows shadowing diagnostics after the parser's. (builder's call, written down)
- Spec and plan live in `docs/plans/`, in English.

## Left to the builder

- How the live app awaits the process start for `keymap.run` before it answers (through the window controller or a host callback), as long as the reply comes after the start and a launch failure becomes the `did not start` error.
- Where the projection from `Keymap` plus diagnostics to the wire payload lives, as long as it is host-free and unit tested.
- The exact column padding of the human output, as long as it matches upstream's rule: the action names are padded to the longest one.
