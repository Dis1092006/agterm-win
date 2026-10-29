# Segoe Fluent Icons catalog

`segoe-fluent-icons.tsv` lists the icon names and code points of the Segoe Fluent Icons font, one `Name<TAB>hex` line each, in code point order.

- Repository: https://github.com/MicrosoftDocs/windows-dev-docs
- Branch: `docs`
- Path: `hub/apps/design/iconography/segoe-fluent-icons-font.md`
- Commit: `7ad8a35fd9a697a7caf2af6b751ab1cbd6c48115` (2026-03-13)
- License: CC BY 4.0, https://creativecommons.org/licenses/by/4.0/. See `THIRD-PARTY-NOTICES.md`.

Regenerate from a copy of the source file:

```
python -I tools/segoe_icons.py --source <path-to-md> --out src/AgTerm.Core/Icons/segoe-fluent-icons.tsv
```
