# B2c2: sidebar row tint, drawing, menu and palette

B2c is split in two: B2c1 is the model, wire, CLI, tree and saved state; B2c2 draws the tint in the sidebar and adds the row menu and palette entries. This is B2c2. It follows `docs/upstream/delta-ed831ab-to-v0.40.0.md`, section 3.

## Upstream commits

- `f3fea57b` (#836): the UI half. The sidebar draws the tint, the row menu has a Tint submenu, and the palette has "Tint Session" rows.

## Tasks

1. Core: `SidebarRow.Tint` on the workspace and session rows. Done.
2. Colors: `SidebarTintInks` from a theme's ANSI palette, with upstream's macOS colors as the fallback; `SidebarRowTint` picks the colors of the background under each part; `ChromePalette.SidebarInks` and `SidebarRowColors.Inks`. Done.
3. Drawing: the icon and name brushes, the flag and focus fill, the image-file silhouette (`SvgSilhouette`, `BitmapSilhouette`), and "<color> tint" in the row's accessible name. Done.
4. Row menu: `SidebarTintMenu` entries, `SidebarController.SetTint` and `SetWorkspaceTint`, the Tint submenu in the session and workspace menus. Done.
5. Palette: the "Tint Session" rows, `TryParseTint`, and `WindowPaletteActions.TintSelectedSession`, which reads the selection when the row runs. Done.
6. Skill, PLATFORM-NOTES, catalog line, versions. Done.

## Decisions

- A part on a theme-colored background takes that theme's ANSI color. A row-style theme colors both parts; a label-style theme colors only the name, and the icon takes the sidebar's colors.
- With a label-style theme the tint colors the text, and the block around the name keeps its theme colors.
- A tinted image-file icon becomes a one-color silhouette. An image without transparency becomes a solid block.
- A workspace tint colors only its own row.
- The flag fill and the focus fill of a tinted icon take the tint color.
- A part on the plain sidebar takes the global theme's colors, and upstream's macOS colors when the palette lacks an entry.
