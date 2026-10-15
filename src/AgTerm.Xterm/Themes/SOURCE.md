# Bundled terminal themes (upstream's set, unchanged)

The theme names and colors match upstream agterm v0.36.0 (`ed831ab`), which bundles the Ghostty theme pack plus its own `agterm` theme.

| Item | Source |
|---|---|
| `ghostty/` (602 files) | `https://deps.files.ghostty.org/ghostty-themes-release-20260810-152212-0173c3c.tgz`, the `iterm2_themes` dependency in Ghostty's `build.zig.zon` at `683d8db643b95cf229bfb5fe9fab9ae677920343` (upstream's `GHOSTTY_REV` in `scripts/setup.sh`). Zig hash `N-V-__8AAPpcBAD4_75xLBbLiYqdojOwQP74eoWmpL3jPrBl`, SHA-256 `ea9878471420ee5b12e7f2ff480099c954ea50e573a1bdf83f43e105c9be63f0`. The files are the `ghostty/` directory of the archive. |
| `agterm` | upstream `agterm/Resources/custom-themes/agterm` at `ed831ab`. |
| `LICENSE` | mbadolato/iTerm2-Color-Schemes, the source of the Ghostty pack, fetched 2026-10-15. |

The files are embedded in `AgTerm.Xterm.dll` (see `AgTerm.Xterm.csproj`); the file name is the theme name. `.gitattributes` keeps their bytes as published.

To update with a new upstream pin:

1. Read `GHOSTTY_REV` in upstream `scripts/setup.sh` at the new pin.
2. Read the `iterm2_themes` URL and hash in `build.zig.zon` of `ghostty-org/ghostty` at that revision.
3. Download and unpack the archive, then replace `ghostty/` with its `ghostty/` directory.
4. Copy upstream's `agterm/Resources/custom-themes/agterm` from the new pin.
5. Update this file and the expected count in `tests/AgTerm.Xterm.Tests/EmbeddedThemePackTests.cs`.
6. Run `dotnet test tests/AgTerm.Xterm.Tests --filter "FullyQualifiedName~EmbeddedThemePackTests"`.
