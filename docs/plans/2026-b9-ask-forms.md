# B9: forms in `ask`

Upstream v0.40.0 extends `ask` with a text field, a choice list and a free-form "other" row (`docs/upstream/delta-ed831ab-to-v0.40.0.md` §6, upstream `96b0a6c6` and the commits after it). The port takes them in three PRs. The plan stays in `docs/plans/` until B9c is merged.

## B9a: text field

- Wire args `input`, `value`, `placeholder` and `cancelButton`, with upstream's validation order and error strings.
- A form without `--button` gets `ok=OK` (the default) and `cancel=Cancel` (the cancel button). The cancel button resolves `escaped`, as Esc does.
- The answer carries `text`.
- Both styles draw the field: the terminal page in `bridge.js`, and a TextBox in the gui `AskView`.
- The draft lives in the store, so a terminal question keeps its text across a page reload. Field rules are in `AskFieldText` and `AskKeys`.
- Windows adaptation: a multiline field answers on Ctrl+Enter, where upstream uses Cmd-Return (PLATFORM-NOTES).

Status: done.

## B9b: choice list

- Wire args `choices`, `multiple` and `checked`; CLI `--choice id=label`, `--choice-subtitle`, `--multiple`, `--checked`. At most 50 items.
- A radio list, or a checkbox list with `--multiple`. The answer carries `selected`.
- Both styles, with the same keys as upstream: Up and Down move the list cursor, Space checks the cursor's item, a click checks the clicked one. Left and Right move the button highlight; without a list Up and Down move it too.
- The cursor and the checked items live in the store draft, next to the field text.
- Windows adaptation: the `gui` markers are Segoe Fluent glyphs (PLATFORM-NOTES).

Status: done.

## B9c: the "other" row

- Wire arg `other`; CLI `--other PLACEHOLDER`.
- A free-form line after the items. Its text goes into the answer.

Status: not started.
