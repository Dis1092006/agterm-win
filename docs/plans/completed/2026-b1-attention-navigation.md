# B1: attention navigation and close/scratch changes

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B1 is the first item. It follows `docs/upstream/delta-ed831ab-to-v0.40.0.md`.

## Upstream commits

- `6864e35a` (#807): setting `attentionIncludesActive`.
- `ffffd2b8` (#824): setting `attentionNavigationAcrossWindows`.
- `395030d9` (#763): Mark for Attention on the session row menu.
- `0107bc95` (#804): `session close --undoable`.
- `da375b93` (#730): `session scratch --follow`; `scratch on` no longer selects.

## Tasks

1. Settings: both attention settings in `AppSettings`, the settings editor, the store and the `tree` fields. Done.
2. Store rule: `StopsAttentionNavigation` is blocked, completed, or active when the setting is on. `NeedsAttention` is unchanged. Done.
3. Core navigator `AttentionNavigator.Step` over a list of windows. Done.
4. `AppStore.MarkForAttention` and the sidebar menu items. Done.
5. Live cross-window step `LiveApplicationHost.StepAttentionAsync`, with the blocked-pane reveal, and the two chords wired to it. Done.
6. `session close --undoable`: args, result, headless and live hosts, CLI output. Done.
7. `session scratch --follow`: args, hosts, CLI. Done.
8. Parity lists, PLATFORM-NOTES, skill text, catalog lines, versions. Done.

## Decisions

- With the cross-window setting off, the chords step inside the source window and still reveal the blocked pane, as upstream's `stepAttention` does.
- `session go` stays window-scoped.
- The live batch close follows `closeGraceUndoEnabled`. The headless host keeps the grace off.
- `scratchGeneration` is not needed: `session scratch on --command` already respawns a shown scratch here.
