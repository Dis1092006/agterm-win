# H1: HTML file pages in the overlay slot

Upstream v0.36.0 can show a local HTML file in the overlay slot instead of a program (`docs/upstream/delta-7df70fe-to-v0.36.0.md` §3, upstream #659, `11fe64eb`). The port takes the file page in two PRs: the control surface first, then the live rendering. URLs, zoom, persistent pages, the page bridge, `--browse`, pictures, markdown and companions are later items.

## H1a: control surface, model and headless host

- Wire command `session.overlay.open` takes `html`, `navigation` and `javascript`. The page replaces the program: `html` with a command is refused with `session.overlay.open takes a command or --html, not both`, and `html` with `wait` with `session.overlay.open: --wait cannot be combined with --html`.
- `navigation` or `javascript` without `html` is refused with `session.overlay.open: --navigation requires --html or --url` and `session.overlay.open: --js requires --html or --url`. These checks run before `session.overlay.open requires a command`, and all content checks run before the color, size and point checks, as upstream orders them.
- `cwd` is the read grant of a page. The file and the grant are checked as upstream's `HtmlOverlay.grantError`, in its order: `html file must be an absolute path`, `cwd must be an absolute path`, `cwd must not be / or the home directory`, `cwd must be a directory containing the html file`, `html file is outside cwd`. Each is prefixed with `session.overlay.open: `. Containment compares whole path components. Windows rules are in PLATFORM-NOTES.
- The open answer is `{id, pageID}`. A page closes a HUD, refuses an occupied slot with `overlay already open`, and refuses a hidden slot with `overlay hidden`. `--follow` selects the session.
- New wire commands `session.overlay.reload [current] [pane]` and `session.overlay.navigate to=back|forward|browser|finder [pane]`. Errors: `no such session`, `no overlay`, `the overlay is not an html page`, and `session.overlay.navigate requires back, forward, browser or finder`. A valid `pane` answers `no overlay`, because the port has no pane overlays.
- Reload bumps the page's reload revision, sets the state to `loading` and drops the error. A reload of the original file also drops the current page.
- Navigate on a page that has no web view answers `html overlay not realized`. The headless host never realizes a page.
- `session.overlay.result` on a page answers `no overlay result: the slot holds an html page`. Hide, show, toggle, resize and close work on a page as on a program.
- `tree` reports `overlay: true` and an `htmlOverlays` list with the page node: `file`, `cwd`, `state`, `error`, `page`, `title`, `canGoBack`, `canGoForward`, `navigation`, `javascript`, `chromeless`, `persistent`, `browse`, `id` and `hidden`, in upstream's key order and omission rules.
- A page has no terminal surface, exit status or zoom target. Close, session close, workspace close and the end of an undoable close release the page exactly once.
- CLI `agtermctl session overlay open --html FILE [--cwd DIR] [--navigation] [--js]`, `session overlay reload [--current]` and `session overlay navigate STEP`. The CLI makes `--html` and a page's `--cwd` absolute against the caller's directory. `--block` with `--html` is refused: it needs the page bridge.
- The live app answers `--html` with `html overlays are not supported yet`.

## H1b: the page in the live app

H1b is two PRs. H1b-1 is the page engine with a scripted web view and a live WebView2 under test, and no UI: `HtmlOverlayPage`, `HtmlOverlayRegistry` and the `IHtmlPageView` seam in `AgTerm.Xterm`, `HtmlNavigationPolicy` and `HtmlOverlayTheme` in the core. H1b-2 puts the page in the window, adds the strip, focus and Alt+W, and turns `--html` on in the live app.

- A WebView2 over the session area shows the file page, full or floating, with the overlay's background color.
- A file page with a grant loads through a virtual host mapped to the grant directory. A page without a grant loads the file text, so it reads no other file.
- `--js` turns page scripts on. The theme stylesheet and the `--agterm-*` variables are injected either way.
- Navigation follows upstream's file policy: a file inside the grant loads in place, everything else is cancelled, and a user-activated web link opens in the default browser.
- The load state, current page, title and history reach the store, so `tree` reports them. Reload and navigate drive the web view. `finder` reveals the file in Explorer.
- `--navigation` adds back, forward, reload, open in browser and show in Explorer buttons over the page.
- Keyboard focus goes to the page while it covers the session. Close Session (Alt+W) closes the page.
