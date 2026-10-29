# Workspace icon: design

## Why

The owner keeps 6 to 10 workspaces open. Each one can have its own theme, and the owner tells them apart by the theme color. With that many workspaces the colors stop being enough: two workspaces get similar themes, some have none, and the right one is not found at once. A second cue of a different kind, a shape, fixes this.

Every workspace row now shows the same `square.grid.2x2` icon (PR #9). This feature lets the user pick an icon per workspace from the Segoe icon font, so `Sites` can show a map and `Connect.Events` an arrow.

Two other sources were tried and dropped:

- A favicon from the project folder. Across the owner's 110 repositories, about 7 have an icon of their own. Most others share a template icon: `Sites` and `Connect.Events` would get the same Envase icon, byte for byte. A workspace also has no folder of its own; `Connect.Events` spans six repositories.
- Emoji. They work and cost less, because Windows already has an emoji picker (Win+.). The owner compared both live and found the Segoe icons look much better: they take the row's text color and match the session icons.

## Goals

- The user picks a Segoe icon for a workspace from a palette, mostly by browsing a grid of icons, sometimes by typing part of its name.
- The palette previews the icon under the cursor on that workspace's sidebar row. Enter or a click saves it, Esc and every other close restore the saved icon.
- The icon is drawn in the row's text color in all three looks: no theme colors, `row` and `label`.
- An agent or a script sets and clears the icon through `agtermctl`, and can list the icon names.
- The icon survives a relaunch.

## Non-goals

- Emoji, image files, favicons.
- An icon anywhere except the workspace row in the sidebar tree: not in palettes, the Ctrl-Tab switcher, the title bar, the window title or the flagged view.
- A builtin action or a default chord for the icon palette.
- An icon per session.

## Anti-goals

- An icon that silently turns into an empty box or a missing-glyph square. A name the app does not know, or a glyph the machine's fonts do not have, shows the default grid icon.
- A palette that loses the user's place. Browsing is the main way in, so the grid must keep up with arrow keys and scrolling through the whole font.

## Decided with the owner

- Segoe icons, not emoji (compared live on 2026-10-29).
- The grid is for browsing; the name filter is secondary.
- All icons of the font are offered, not a curated subset.
- The value is stored as the icon's name (for example `MapPin`), not its code point.
- `agtermctl workspace icon NAME` sets it, `agtermctl workspace icon` without a name clears it, following `workspace theme`.
- `icon.list` lists the names, following `theme.list`.
- The default stays upstream's `square.grid.2x2`, drawn as the four-square Path from PR #9.

## Constraints

- Parity: this is a Windows-only extension. The command, the tree field and the snapshot field go into `parity/windows-only.json` and the PLATFORM-NOTES section "Windows-only extensions". Upstream has no workspace icon at the pin or in `docs/upstream/surface-v0.36.0.json`, which the parity tests check.
- The agent skill (`plugins/agterm/skills/agterm`) documents the new command and flag, and the plugin version goes up in all three manifests (`SkillTextTests`).
- Every PR raises `<Version>` in `Directory.Build.props`.
- TDD as in `CLAUDE.md`. JSON fields are camelCase, and an absent icon is omitted, never written as `null`.
- The app supports Windows 10 build 19041 and later (PLATFORM-NOTES, "App-played notify suppression"). `Segoe Fluent Icons` ships with Windows 11. Windows 10 has `Segoe MDL2 Assets`, which shares most code points but lacks the newer glyphs. WinUI's `SymbolThemeFontFamily` falls back from one to the other.

## The surface

Modeled on `workspace.theme` (PLATFORM-NOTES, "Windows-only extensions"):

- `workspace.icon` selects the workspace as `workspace.rename` does and answers `{"id":...}`. `args.name` sets the icon; a missing or blank name clears it. A name not in the icon catalog gives `unknown icon: X`.
- `agtermctl workspace icon [NAME] [--target W] [--window ...]`.
- `icon.list` answers the catalog's names in catalog order, as `theme.list` answers theme names. `agtermctl icon list` prints them one per line. Without it an agent has no way to learn a name.
- The workspace node in `tree` gets `icon` with the stored name, also when the catalog does not know it.
- The workspace in the window snapshot gets `icon`. It reads leniently, as `theme` does: a value that is not a string, or a blank one, reads as no icon, and the rest of the snapshot loads.
- Names match exactly, as theme names do.
- The wire side checks names against the committed catalog only, never against the machine's fonts (designer's call). This keeps the control server and the headless host free of font code, and their tests deterministic. On a machine whose fonts lack a glyph (Windows 10, or an older Segoe Fluent Icons), a name `icon.list` lists and `workspace.icon` accepts draws as the grid icon. The skill and PLATFORM-NOTES say so in one line. The palette, which runs in the app, hides such glyphs.

## The catalog

- A table of names and code points for the Segoe Fluent Icons font, generated by a script in `tools/` from Microsoft's published icon list ("Segoe Fluent Icons font" on Microsoft Learn), and committed. The source and how to regenerate it are written next to the table, as `src/AgTerm.Xterm/Themes/SOURCE.md` does for the theme pack. If the list's license asks for attribution, it goes into `THIRD-PARTY-NOTICES.md`.
- Names must be unique in the table. Where Microsoft's list repeats a description, the script makes the names unique in a way a person can still read.
- The catalog order is the code point order. Microsoft keeps related glyphs close together, which is what makes browsing work.
- The table lives where both the control server (to check names) and the app (to draw them) can use it.

## The palette

- The workspace row menu gets `Icon…` next to `Theme…`.
- It opens a palette in the same window, styled like the other palettes: a filter box on top, a grid of glyphs below, the name of the glyph under the cursor shown somewhere in the palette. The first cell is the default grid icon, labeled `Default`; picking it clears the icon.
- The grid opens with the current icon selected and scrolled into view.
- Arrow keys move in two dimensions, Page Up and Page Down move by a screen, Enter accepts, Esc cancels. Typing goes to the filter box, which narrows the grid to names containing the text, case-insensitive.
- The glyph under the cursor shows on that workspace's sidebar row at once, in this window only. Accept sends `workspace.icon` to the window, the same path `agtermctl` uses, so the palette gets the same answers and errors. Esc and every other close show the saved icon again.
- Like the other palettes it does not open while a pane is zoomed.

## The row

- A workspace row with a known icon shows that glyph in the icon slot PR #9 added, at 16 px, in the row's text color (`RowForeground` in `row`, the secondary text color otherwise, as the grid icon).
- No icon, an unknown name, or a glyph missing from the machine's fonts shows the grid icon.

## Left to the builder

- How to find out whether a glyph exists in the fonts the machine resolves `SymbolThemeFontFamily` to, and whether the grid hides glyphs the machine cannot draw (preferred) or shows them as unavailable.
- The palette layout: grid control, cell size, columns, where the name is shown, how the filter box and the grid share keyboard focus. It should reuse the existing palette chrome (`PaletteView`, `PaletteController`, `PaletteLayout`) where that fits.
- How the preview reaches the sidebar row, following `SidebarView.PreviewWorkspaceTheme` and `EndWorkspaceThemePreview`.
- The split into PRs. A natural split is the surface, catalog and row first, then the palette. Each PR raises the version; the one that touches the skill raises the plugin version.
- The plan file: written to `docs/plans/`, and moved to `docs/plans/completed/` by the PR that finishes it.
