# Vendored xterm.js bundles (no CDN — parity/app policy: assets ship in the app)

Pinned versions (npm registry, UMD `lib/*.js` builds, fetched 2026-09-11):

| Package | Version | File |
|---|---|---|
| @xterm/xterm | 6.1.0-beta.304 | xterm.js, xterm.css |
| @xterm/addon-fit | 0.12.0-beta.301 | addon-fit.js |
| @xterm/addon-search | 0.17.0-beta.301 | addon-search.js |
| @xterm/addon-serialize | 0.15.0-beta.301 | addon-serialize.js |

The core is on a 6.1.0 beta for `vtExtensions` (`kittyKeyboard`, `win32InputMode`), which 6.0.0 has
no equivalent of: without the kitty keyboard protocol Enter and Shift+Enter both reach the pty as
CR, so Claude Code cannot bind newline-without-submit. The addon versions lag the core by design —
their published peer range is `^6.1.0-beta.304`, so this set is what upstream pairs, and `npm
install` of matching `-beta.301` numbers across all four fails ERESOLVE.

To bump: `npm install @xterm/xterm@<v> @xterm/addon-fit@<v> @xterm/addon-search@<v>
@xterm/addon-serialize@<v>`, copy `lib/<name>.js` (and `css/xterm.css`) here, update this table.
