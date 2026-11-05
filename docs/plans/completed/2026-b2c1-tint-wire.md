# B2c1: sidebar row tint, wire and model

Package B takes the remaining upstream changes after `ed831ab` in a fixed order. B2 (sidebar rows) is split into B2a `lastSelectedAt`, B2b icon toggles, B2c tint and B2d info line. B2c is split in two: B2c1 is the model, wire, CLI, tree and saved state; B2c2 draws the tint in the sidebar and adds the row menu and palette entries. This is B2c1. It follows `docs/upstream/delta-ed831ab-to-v0.40.0.md`, section 3.

## Upstream commits

- `f3fea57b` (#836): `session.tint` and `workspace.tint`, `--tint` and `--tint-on` on `session new` and `workspace new`, `tint` and `tintOn` on the tree nodes and in the saved state.

## Tasks

1. Core model: `TintColor`, `TintParts` and `SidebarTint` with `Recolor` and `Place`; `Tint` on `Session` and `Workspace`. Done.
2. Store and saved state: `SetSessionTint`, `SetWorkspaceTint`, `SetTintColor`, `SetTintParts`; `tint` in the session and workspace snapshots; an undo or a reopen that rebuilds a removed workspace gives it back its tint; a duplicate starts untinted. Done.
3. Tree: `tint` and `tintOn` on the session and workspace nodes, omitted when untinted. Done.
4. Protocol: the commands, the args `tint` and `tintOn`, `ControlDispatcher.Tint` with upstream's error strings, the creation tint on `session.new` and `workspace.new`, and the headless host. Done.
5. CLI: `session tint`, `workspace tint`, `--tint` and `--tint-on` on both `new` commands. Done.
6. E2E: the upstream server tests over the pipe, and a CLI round trip. Done.
7. Skill, parity, PLATFORM-NOTES, catalog lines, versions. Done.

## Decisions

- `IControlActions` keeps the tintless `CreateSession` and `CreateWorkspace`. The tint overloads have a default body that refuses a tint, as upstream's `ControlActions` does, so a host without tint support answers with an error instead of dropping it.
- The live host reuses `HeadlessControlActions`, so it gets tint support with no extra code.
- The skill says the sidebar does not draw the tint yet. B2c2 removes that sentence.
