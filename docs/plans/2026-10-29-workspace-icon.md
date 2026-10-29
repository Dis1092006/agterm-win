# Workspace Icon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user give each workspace a Segoe icon, set through `workspace.icon` and a browsable icon palette, shown on the workspace's sidebar row.

**Architecture:** A committed table of Segoe Fluent Icons names and code points lives in AgTerm.Core, so the control server checks names and the app draws them from one source. The surface copies `workspace.theme`: a Windows-only command, a tree field and a snapshot field. The row resolves the name to a glyph through a GDI glyph probe and falls back to the grid Path. The palette is a new grid view over a host-free model in AgTerm.Xterm, with the preview held like `SidebarRowThemes`.

**Tech Stack:** .NET 10, WinUI 3, xUnit, System.CommandLine, Python 3 for the generator.

**Spec:** `docs/plans/2026-10-29-workspace-icon-design.md`

## Global Constraints

- Windows-only surface: `workspace.icon`, `icon.list`, the tree field `icon`, the snapshot field `icon`, the result field `icons` go into `parity/windows-only.json`; none of them may exist in `docs/upstream/surface-v0.36.0.json`.
- Wire error for an unknown name: `unknown icon: X`, where X is the name as given after trimming.
- Names match exactly (ordinal, case-sensitive). The palette filter is case-insensitive.
- JSON names are camelCase; an absent icon is omitted, never written as `null`.
- The snapshot `icon` reads leniently: a non-string or blank value reads as no icon, and the rest of the snapshot loads.
- A name the catalog lacks, or a glyph the machine's fonts lack, draws the grid Path. Never an empty box or a missing-glyph square.
- The app supports Windows 10 build 19041 and later. CI runs on Windows runners that may not have `Segoe Fluent Icons`.
- TDD for every task: failing test, watch it fail, minimal code, pass.
- Part A ships as one PR with app version `0.1.48` and plugin version `0.1.15`. Part B ships as a second PR with app version `0.1.49`. The plugin version stays unless Part B touches the skill.
- Docs in English. The PR that finishes Part B moves this plan and the design to `docs/plans/completed/`.
- Build and test: `dotnet build AgTerm.slnx`, `dotnet test AgTerm.slnx -m:1`, `dotnet build src/AgTerm.App -p:Platform=x64`. Warnings are errors.

## Review Focus

- A name in the wrong case (`mappin`) must give `unknown icon: mappin`, not set an icon that later draws as the grid. Test in Task 3.
- A snapshot whose workspace `icon` is a number, an object or blank must load with no icon and keep every workspace. Test in Task 2.
- A machine without `Segoe Fluent Icons` (Windows 10, the CI runner) must draw the grid for a missing glyph and must not fail tests that assume Fluent-only glyphs. Test in Task 6 with a fake probe; Task 6's probe test uses a code point both fonts have.
- A workspace closed and then restored from the recently-closed list must keep its icon, as it keeps its theme. Test in Task 2.
- A palette filter that matches nothing must leave nothing selected, and Enter must then do nothing: no save, no exception. Test in Task 8.

---

# Part A: surface, catalog and row (PR 1)

### Task 1: Icon catalog

**Files:**
- Create: `tools/segoe_icons.py`
- Create: `src/AgTerm.Core/Icons/segoe-fluent-icons.tsv`
- Create: `src/AgTerm.Core/Icons/SOURCE.md`
- Create: `src/AgTerm.Core/Icons/SegoeIconCatalog.cs`
- Modify: `src/AgTerm.Core/AgTerm.Core.csproj` (embed the TSV)
- Modify: `THIRD-PARTY-NOTICES.md`
- Test: `tests/AgTerm.Core.Tests/Icons/SegoeIconCatalogTests.cs`

**Interfaces:**
- Produces: `public sealed record SegoeIcon(string Name, int CodePoint)`; `public static class SegoeIconCatalog { IReadOnlyList<SegoeIcon> All; bool TryGet(string name, out SegoeIcon icon); }` in namespace `AgTerm.Core.Icons`.

- [ ] **Step 1: Write the generator.** `tools/segoe_icons.py --source <path-to-md> --out src/AgTerm.Core/Icons/segoe-fluent-icons.tsv` reads `hub/apps/design/iconography/segoe-fluent-icons-font.md` from https://github.com/MicrosoftDocs/windows-dev-docs (branch `docs`). It takes each table row `| ... | <hex> | :::no-loc text="<Name>"::: |`, writes `Name<TAB>hex` lines in source order, and fails when a name repeats, a name is not `[A-Za-z0-9]+`, or the code points are not ascending. At the time of writing the file has 1533 rows, `e700 GlobalNavButton` to `f8cc`.
- [ ] **Step 2: Generate and commit the TSV.** `SOURCE.md` names the repository, branch, path, the commit the TSV was generated from, the license (CC BY 4.0) and the regenerate command. Add a CC BY 4.0 attribution entry for the icon names to `THIRD-PARTY-NOTICES.md`.
- [ ] **Step 3: Write the failing tests** in `SegoeIconCatalogTests`:
  - `The_catalog_has_every_icon_of_the_list_in_code_point_order`: `All.Count == 1533`, `All[0] == new SegoeIcon("GlobalNavButton", 0xE700)`, code points strictly ascending.
  - `Names_are_unique`.
  - `A_name_resolves_to_its_code_point`: `TryGet("MapPin", out var icon)` is true and `icon.CodePoint == 0xE707`.
  - `A_name_in_another_case_is_unknown`: `TryGet("mappin", out _)` is false.
- [ ] **Step 4: Run** `dotnet test tests/AgTerm.Core.Tests --filter SegoeIconCatalogTests`. Expected: build error, `SegoeIconCatalog` does not exist.
- [ ] **Step 5: Implement `SegoeIconCatalog`.** Load the embedded TSV once (lazy), keep a list and an ordinal dictionary.
- [ ] **Step 6: Run the tests.** Expected: PASS.
- [ ] **Step 7: Commit** "Add the Segoe Fluent Icons catalog".

### Task 2: Store and snapshot

**Files:**
- Modify: `src/AgTerm.Core/Model/Workspace.cs` (`IconName`, `ToSnapshot`)
- Modify: `src/AgTerm.Core/Model/Snapshot.cs` (`WorkspaceSnapshot.Icon`)
- Create: `src/AgTerm.Core/Model/AppStore.Icons.cs`
- Modify: `src/AgTerm.Core/Model/AppStore.cs` (snapshot at ~1032, restore at ~1078 and ~1246)
- Modify: `src/AgTerm.Core/Model/AppStore.PendingClose.cs` (~423)
- Modify: `parity/windows-only.json` (`workspaceSnapshot`)
- Test: `tests/AgTerm.Core.Tests/Model/SnapshotTests.cs`, `tests/AgTerm.Core.Tests/Model/AppStoreIconTests.cs`, `tests/AgTerm.Core.Tests/Protocol/WindowsOnlySurfaceTests.cs`

**Interfaces:**
- Produces: `Workspace.IconName { get; set; }` (`string?`); `WorkspaceSnapshot.Icon` (`[JsonPropertyName("icon")]`, `[JsonConverter(typeof(LossyThemeNameConverter))]`, declared after `Theme`); `AppStore.SetWorkspaceIcon(Guid workspaceId, string? iconName)`: blank clears, no catalog check, a change calls `ScheduleTreeChanged()` and `Save()`, the same value changes nothing.

- [ ] **Step 1: Write the failing tests.**
  - `SnapshotTests`: a workspace with `IconName = "MapPin"` writes `"icon":"MapPin"`; one without writes no `icon` key; reading `"icon":42`, `"icon":{}` and `"icon":"  "` gives `Icon == null` and keeps the workspace; the snapshot field list equals pin + `WindowsOnly("workspaceSnapshot")` (the existing test, with `icon` added to the JSON file).
  - `AppStoreIconTests`: `Setting_an_icon_stores_it_and_schedules_tree_changed`; `A_blank_icon_clears_it`; `Setting_the_same_icon_changes_nothing` (no tree.changed); `A_restored_snapshot_keeps_the_icon`; `A_workspace_restored_from_recently_closed_keeps_its_icon` (follow the theme test for `AppStore.PendingClose`).
  - `WindowsOnlySurfaceTests.The_list_names_...`: `WindowsOnly("workspaceSnapshot") == ["theme", "icon"]`.
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.Core.Tests -m:1`. Expected: build errors for `IconName`, `Icon`, `SetWorkspaceIcon`.
- [ ] **Step 3: Implement.** Copy `IconName` everywhere `ThemeName` is copied in `Workspace.ToSnapshot`, `AppStore.cs` and `AppStore.PendingClose.cs`. Add `{ "name": "icon", "reason": "Persists the workspace's own icon." }` to `workspaceSnapshot` in `parity/windows-only.json`.
- [ ] **Step 4: Run the Core tests.** Expected: PASS.
- [ ] **Step 5: Commit** "Store a workspace icon and persist it".

### Task 3: Wire commands and tree field

**Files:**
- Modify: `src/AgTerm.Core/Protocol/Command.cs` (enum + wire map next to `WorkspaceTheme` and `ThemeList`)
- Modify: `src/AgTerm.Core/Protocol/ControlTreeNodes.cs` (`ControlWorkspaceNode.Icon` after `Theme`)
- Modify: `src/AgTerm.Core/Protocol/ControlRequestResponse.cs` (`ControlResult.Icons`, declared last)
- Modify: `src/AgTerm.Core/Protocol/ControlActions.cs`, `src/AgTerm.Core/Protocol/ControlDispatcher.cs`
- Modify: `src/AgTerm.Core/Model/AppStore.cs` (tree node at ~335)
- Modify: `src/AgTerm.ControlServer/HeadlessControlActions.cs`
- Modify: `parity/windows-only.json` (`commands`, `workspaceNode`, new `result` section)
- Test: `tests/AgTerm.ControlServer.Tests/HeadlessWorkspaceIconTests.cs`, `tests/AgTerm.Core.Tests/Protocol/CommandTests.cs`, `TreeNodeTests.cs`, `ControlPickTests.cs`, `WindowsOnlySurfaceTests.cs`

**Interfaces:**
- Consumes: `SegoeIconCatalog.TryGet` (Task 1), `AppStore.SetWorkspaceIcon` (Task 2).
- Produces: `Command.WorkspaceIcon` = `"workspace.icon"`, `Command.IconList` = `"icon.list"`; `IControlActions.SetWorkspaceIcon(string? target, string? window, string? iconName)` and `IControlActions.ListIcons()`; `ControlWorkspaceNode.Icon` (`string?`, `"icon"`); `ControlResult.Icons` (`string[]?`, `"icons"`).

- [ ] **Step 1: Write the failing tests** in `HeadlessWorkspaceIconTests` (follow the headless `workspace.theme` tests):
  - `workspace.icon` with `name: "MapPin"` answers `{"ok":true,"result":{"id":"<ID>"}}` and `tree` shows `"icon":"MapPin"` on that workspace.
  - `name: "mappin"` answers `{"ok":false,"error":"unknown icon: mappin"}` and changes nothing.
  - A missing or blank name clears the icon; `tree` then has no `icon` key.
  - An unknown target answers the same error `workspace.theme` gives for it.
  - `icon.list` answers `result.icons` with 1533 names, first `GlobalNavButton`, and no other result field.
  - Parity: `CommandTests` and `TreeNodeTests` pick up the new entries from the JSON; `ControlPickTests`' result-field test expects pin + adopted + `WindowsOnly("result")`, with `Icons` set in its sample result; `WindowsOnlySurfaceTests` expects `commands == ["workspace.theme", "workspace.icon", "icon.list"]`, `workspaceNode == ["theme", "icon"]`, `result == ["icons"]`, and adds `[InlineData("result")]` to the upstream check.
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.Core.Tests -m:1` and `dotnet test tests/AgTerm.ControlServer.Tests`. Expected: build errors, then the parity tests fail on the missing entries.
- [ ] **Step 3: Implement.** Route `WorkspaceIcon` with the workspace commands (`ControlDispatcher.cs:43`) and `IconList` with the app commands (`:48`). `HeadlessControlActions.SetWorkspaceIcon` trims the name as `SetWorkspaceTheme` does, resolves the workspace first, then checks `SegoeIconCatalog.TryGet`. Add the JSON entries with reasons: `workspace.icon` "Sets or clears a workspace's own icon.", `icon.list` "Lists the icon names workspace.icon accepts.", node `icon` "Reports the workspace's own icon in tree.", result `icons` "The icon names icon.list answers.". Any other `IControlActions` implementation in tests gets the two members.
- [ ] **Step 4: Run** both test projects. Expected: PASS.
- [ ] **Step 5: Commit** "Add workspace.icon and icon.list".

### Task 4: agtermctl

**Files:**
- Modify: `src/AgTerm.Cli/WorkspaceCommands.cs` (`Icon` + `BuildIcon`, registered after `BuildTheme`)
- Create: `src/AgTerm.Cli/IconCommands.cs`
- Modify: `src/AgTerm.Cli/Agtermctl.cs` (register next to `ThemeCommands.Build`)
- Modify: `src/AgTerm.Cli/ClientOutput.cs`
- Test: `tests/AgTerm.Cli.Tests/WorkspaceIconCommandsTests.cs`

**Interfaces:**
- Consumes: `WireCommand.WorkspaceIcon`, `WireCommand.IconList`, `ControlResult.Icons` (Task 3).
- Produces: `agtermctl workspace icon [NAME] [--target W] [--window ...]`, `agtermctl icon list`.

- [ ] **Step 1: Write the failing tests.**
  - `workspace icon MapPin --target 9f3c` makes `{cmd: "workspace.icon", target: "9f3c", args.name: "MapPin"}`; with `--window w` the window is in args, as for `workspace theme`.
  - `workspace icon --target 9f3c` makes the request with no name.
  - `icon list` makes `{cmd: "icon.list"}`.
  - A response with `result.icons = ["A", "B"]` prints `A\nB`.
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.Cli.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement.** Descriptions: `workspace icon` "Set a workspace's own icon, or clear it without a name (Windows only).", argument "Icon name from `agtermctl icon list`; omit it to show the default icon again."; `icon` "Icon commands."; `icon list` "List the icon names workspace icon accepts (Windows only)." In `ClientOutput`, print `Icons` one per line before the `Text` branch.
- [ ] **Step 4: Run the CLI tests.** Expected: PASS.
- [ ] **Step 5: Commit** "Add agtermctl workspace icon and icon list".

### Task 5: Skill and platform notes

**Files:**
- Modify: `plugins/agterm/skills/agterm/reference.md`, `SKILL.md`, `examples.md`
- Modify: `plugins/agterm/.claude-plugin/plugin.json`, `plugins/agterm/.codex-plugin/plugin.json`, `.claude-plugin/marketplace.json` (`0.1.15`)
- Modify: `docs/PLATFORM-NOTES.md` ("Windows-only extensions")
- Test: `tests/AgTerm.Cli.Tests/SkillTextTests.cs`

- [ ] **Step 1: Write the failing test** `Reference_names_every_windows_only_command`: every `commands[].wire` in `parity/windows-only.json` appears in `reference.md` as `` `name` ``. It fails on `workspace.theme`, which the reference does not document yet, as well as on the two new commands.
- [ ] **Step 2: Run** `dotnet test tests/AgTerm.Cli.Tests --filter SkillTextTests`. Expected: FAIL naming `workspace.theme`, `workspace.icon`, `icon.list`.
- [ ] **Step 3: Write the docs.** `reference.md`: entries for `workspace.theme`, `workspace.icon` and `icon.list`, with the error string, the clearing rule, exact-case names and the line "On a machine whose fonts lack the glyph (Windows 10), an accepted name draws as the default icon." `SKILL.md`: one line under the workspace commands. `examples.md`: `agtermctl icon list | Select-String Map` and `agtermctl workspace icon MapPin --target active` (both must parse, `Every_agtermctl_line_in_a_code_block_parses`). PLATFORM-NOTES: a bullet "A workspace icon" after the theme bullet, with the same sub-bullets as the reference, plus the catalog source. Raise the plugin version in all three manifests.
- [ ] **Step 4: Run the CLI tests.** Expected: PASS.
- [ ] **Step 5: Commit** "Document workspace icon in the skill and platform notes".

### Task 6: Draw the icon on the row

**Files:**
- Modify: `src/AgTerm.Core/Model/SidebarProjection.cs` (`SidebarRow.IconName`, set on workspace rows)
- Create: `src/AgTerm.Xterm/SymbolGlyphs.cs`
- Create: `src/AgTerm.Xterm/SidebarWorkspaceIcon.cs`
- Modify: `src/AgTerm.App/SidebarRowViewModel.cs`, `src/AgTerm.App/SidebarView.xaml`, `src/AgTerm.App/SidebarView.xaml.cs`
- Modify: `Directory.Build.props` (`0.1.48`)
- Test: `tests/AgTerm.Core.Tests/Model/SidebarProjectionTests.cs`, `tests/AgTerm.Xterm.Tests/SymbolGlyphsTests.cs`, `tests/AgTerm.Xterm.Tests/SidebarWorkspaceIconTests.cs`, `tests/AgTerm.Xterm.Tests/SidebarRowNestingSourceTests.cs`

**Interfaces:**
- Consumes: `Workspace.IconName` (Task 2), `SegoeIconCatalog` (Task 1).
- Produces: `SidebarRow.IconName` (`string?`); `SymbolGlyphs.Has(int codePoint) -> bool` (cached per code point); `SidebarWorkspaceIcon.Glyph(string? iconName, Func<int, bool> hasGlyph) -> string?`, the glyph text or null for the grid; view model `WorkspaceGlyph` (`string`), `WorkspaceGlyphVisibility`, `GridIconVisibility`.

- [ ] **Step 1: Write the failing tests.**
  - `SidebarProjectionTests`: a workspace with `IconName = "MapPin"` projects `IconName == "MapPin"` on its workspace row; session rows have null.
  - `SidebarWorkspaceIconTests`: `Glyph("MapPin", _ => true) == ""`; `Glyph("MapPin", _ => false) == null`; `Glyph("NoSuchIcon", _ => true) == null`; `Glyph(null, _ => true) == null`.
  - `SymbolGlyphsTests`: `Has(0xE707)` is true (both Segoe Fluent Icons and Segoe MDL2 Assets have it); `Has(0xE000)` is false.
  - Source tests: the icon slot of `SidebarView.xaml` has a `FontIcon` bound to `WorkspaceGlyph` and `WorkspaceGlyphVisibility`, with `{ThemeResource SymbolThemeFontFamily}`, `FontSize="14"`, `Width="16"`, `Margin="0,0,6,0"`, `local:SidebarRowBrushes.Foreground="{x:Bind RowForeground, Mode=OneWay}"` and the secondary-text style the grid Path has; the grid Path binds `GridIconVisibility`; `Update` raises the three new names.
- [ ] **Step 2: Run** the Core and Xterm tests (non-live filter `FullyQualifiedName~Sidebar|FullyQualifiedName~SymbolGlyphs`). Expected: FAIL.
- [ ] **Step 3: Implement.** `SymbolGlyphs` asks GDI (`CreateFontW` + `GetGlyphIndicesW` with `GGI_MARK_NONEXISTING_GLYPHS`) for `Segoe Fluent Icons`, then `Segoe MDL2 Assets`, the order `SymbolThemeFontFamily` resolves; a font that does not exist counts as having no glyphs. The view model calls `SidebarWorkspaceIcon.Glyph(_row.IconName, SymbolGlyphs.Has)`. Raise the version.
- [ ] **Step 4: Run** the tests and `dotnet build src/AgTerm.App -p:Platform=x64 --no-incremental`. Expected: PASS, no warnings.
- [ ] **Step 5: Full suite** `dotnet test AgTerm.slnx -m:1`. Expected: PASS; report any failure by name.
- [ ] **Step 6: Manual check** on demo data with its own `AGTERM_STATE_DIR`: `agtermctl workspace icon MapPin`, then `Forward`, then a clear, in `row`, `label` and no-theme looks; a relaunch keeps the icon.
- [ ] **Step 7: Commit** "Draw the workspace icon on its sidebar row". Open PR 1 after the owner's go.

---

# Part B: the icon palette (PR 2)

### Task 7: Preview holder

**Files:**
- Create: `src/AgTerm.Xterm/SidebarRowIcons.cs`
- Modify: `src/AgTerm.App/SidebarView.xaml.cs`
- Test: `tests/AgTerm.Xterm.Tests/SidebarRowIconsTests.cs`, a source test next to `SidebarThemeColorsSourceTests`

**Interfaces:**
- Consumes: `SidebarRow.IconName` (Task 6).
- Produces: `SidebarRowIcons` with `string? IconNameFor(SidebarRow row)`, `void Preview(Guid workspaceId, string? iconName)`, `void EndPreview()`, `void Saved(Guid workspaceId, string? iconName, long untilGeneration)`, `void Observe(long generation)`, the same contract as `SidebarRowThemes`; `SidebarView.PreviewWorkspaceIcon(Guid, string?)`, `EndWorkspaceIconPreview()`, `WorkspaceIconSaved(Guid, string?)`.

- [ ] **Step 1: Write the failing tests**, mirroring `SidebarRowThemesTests`: a preview overrides only that workspace's row; `EndPreview` shows the row's own name; `Saved` keeps the saved name until a snapshot of `untilGeneration` or later is observed; a preview of `null` shows the default.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** and make the view model take its icon name from `SidebarRowIcons.IconNameFor`.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** "Preview a workspace icon on its row".

### Task 8: Palette model

**Files:**
- Create: `src/AgTerm.Xterm/IconPaletteModel.cs`
- Test: `tests/AgTerm.Xterm.Tests/IconPaletteModelTests.cs`

**Interfaces:**
- Consumes: `SegoeIconCatalog.All` (Task 1).
- Produces: `IconPaletteModel(IReadOnlyList<SegoeIcon> icons, Func<int, bool> hasGlyph, string? current)`; `IReadOnlyList<IconPaletteCell> Cells` where `IconPaletteCell(string? Name, string Label, string? Glyph)` and the first cell is `(null, "Default", null)`; `int? Selected`; `void Filter(string text)`; `void Move(IconPaletteMove move, int columns, int rowsPerPage)` with `IconPaletteMove { Left, Right, Up, Down, PageUp, PageDown, Home, End }`; `string? SelectedName`; `bool CanAccept`.

- [ ] **Step 1: Write the failing tests.**
  - Cells are Default then every icon `hasGlyph` accepts, in catalog order; an icon it rejects is absent.
  - The initial selection is the current icon's cell, or Default when `current` is null or unknown.
  - `Filter("map")` drops Default, keeps `MapPin` and `MapLayers`, and `Filter("MAP")` gives the same cells; `Filter("")` brings Default back; the selection stays on the same icon when it survives the filter, else moves to the first cell.
  - `Filter("zzzz")` leaves `Cells` empty, `Selected == null`, `CanAccept == false`.
  - `Move`: `Right` at the last cell stays; `Down` moves by `columns` and clamps to the last cell; `PageDown` moves by `columns * rowsPerPage`; `Up` from the first row stays.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** "Add the icon palette model".

### Task 9: Palette view and window wiring

**Files:**
- Create: `src/AgTerm.App/IconPaletteView.xaml`, `src/AgTerm.App/IconPaletteView.xaml.cs`
- Modify: `src/AgTerm.App/SidebarView.xaml.cs` (`Icon…` item after `Theme…` at ~594, event `WorkspaceIconRequested`)
- Modify: `src/AgTerm.App/MainWindow.xaml.cs`
- Modify: `src/AgTerm.Xterm/LiveApplicationHost.cs` (`SetWorkspaceIconAsync`)
- Modify: `Directory.Build.props` (`0.1.49`)
- Test: `tests/AgTerm.Xterm.Tests/IconPaletteSourceTests.cs`, a live test next to the workspace theme live tests for `SetWorkspaceIconAsync`

**Interfaces:**
- Consumes: Tasks 7 and 8.
- Produces: `LiveApplicationHost.SetWorkspaceIconAsync(Guid windowId, Guid workspaceId, string? iconName, CancellationToken = default) -> Task<ControlResponse>`, a copy of `SetWorkspaceThemeAsync` with `Command.WorkspaceIcon`.

- [ ] **Step 1: Write the failing tests.**
  - Live: `SetWorkspaceIconAsync` with `MapPin` makes `tree` report it; with `mappin` it answers `unknown icon: mappin`.
  - Source: the workspace menu adds `Item("Icon…", () => WorkspaceIconRequested?.Invoke(row.Id))` right after `Theme…`; MainWindow previews through `Sidebar.PreviewWorkspaceIcon`, ends through `Sidebar.EndWorkspaceIconPreview`, saves through `_applicationHost.SetWorkspaceIconAsync` and on success calls `Sidebar.WorkspaceIconSaved`; a failure shows `LiveFailureNotice("Icon could not be applied", response.Error ?? "workspace.icon failed")`; the palette does not open while a pane is zoomed, as the theme palette.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.** The view hosts the filter box above a grid of 16 px glyph cells (`SymbolThemeFontFamily`) with the selected cell's label shown under the grid; it reuses the palette chrome and placement of `PaletteView` and `PaletteLayout`. Keys: arrows, Page Up/Down, Home/End through `IconPaletteModel.Move`; Enter accepts when `CanAccept`; Esc and every other close end the preview. The grid opens with the current icon scrolled into view.
- [ ] **Step 4: Run** the tests and the app build. Expected: PASS, no warnings.
- [ ] **Step 5: Full suite** `dotnet test AgTerm.slnx -m:1`. Expected: PASS; report any failure by name.
- [ ] **Step 6: Manual check** with the owner on demo data: browse the whole grid with keys and the wheel, filter `map`, preview on the row, Enter saves, Esc restores, `Default` clears, in `row` and `label`.
- [ ] **Step 7: Commit** "Pick a workspace icon from a palette".

### Task 10: Close the plan

**Files:**
- Modify: `docs/PLATFORM-NOTES.md` (the workspace icon bullet: the palette)
- Move: `docs/plans/2026-10-29-workspace-icon.md` and `docs/plans/2026-10-29-workspace-icon-design.md` to `docs/plans/completed/`

- [ ] **Step 1:** Add the palette sub-bullet, modeled on the `Theme…` one: `Icon…` in the workspace menu, the grid, the filter, the preview in its own window only, Enter or click saves, Esc and every other close restore, not while a pane is zoomed, no builtin action or chord.
- [ ] **Step 2:** `git mv` both plan files to `docs/plans/completed/`.
- [ ] **Step 3: Commit** "Document the icon palette and close the plan". Open PR 2 after the owner's go.
