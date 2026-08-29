// The JS half of the agterm terminal bridge, v2: ONE page hosts MANY xterm.js panes keyed by
// paneId (see XtermPage.cs, the C# half). Protocol:
//   host → page  (PostWebMessageAsJson):  {type:'output', paneId, data:<base64 VT bytes>}
//                                         {type:'options', paneId, fontSize?}
//   page → host  (chrome.webview.postMessage): {type:'ready'}                    page boot, once
//                                              {type:'data', paneId, data}       user keystrokes
//                                              {type:'binary', paneId, data}
//                                              {type:'resize', paneId, cols, rows}
//                                              {type:'title', paneId, title}     OSC 0/1/2
//                                              {type:'cwd', paneId, uri}         OSC 7 / OSC 9;9
//                                              {type:'chord', paneId, ctrl, alt, shift, key}
//                                              {type:'pasteRequested', paneId}   right-click paste
//   host-invoked (ExecuteScriptAsync, result = JSON): agterm.openPane(paneId, {fontSize}) → {cols,rows},
//     agterm.showPane(paneId), agterm.closePane(paneId), agterm.applyLayout(layout), and the per-pane reads/writes:
//     text/selection/selectAll/serialize/fit/search/searchClose/paste/setFontSize/getFontSize/focus —
//     each takes paneId first; every call on an unknown paneId returns null. Page-global:
//     agterm.setChords([{ctrl,alt,shift,key}]) — the hotkey table the panes intercept.
//     agterm.applyRendererSettings(settings), agterm.getRendererSettings(paneId),
//     agterm.getRendererCapabilities(paneId).
//     layout schema: {type:'single', generation, paneId, title} | {type:'empty', generation}.
//     applyLayout rejects stale generations (< highest accepted) with false and does no DOM changes.
// The FIRST opened pane is shown automatically; window resizes refit only the visible pane.
"use strict";

const panes = new Map(); // paneId → {term, fitAddon, searchAddon, serializeAddon, lastSearch, el, webgl2Available, webglAddonAvailable, rendererBackend, webglAddon?}
let visiblePaneId = null;
let highestAcceptedLayoutGeneration = null;
let interceptChords = []; // [{ctrl, alt, shift, key}] — hotkeys the HOST owns; see 'chords' below
const container = document.getElementById("panes");

const DEFAULT_RENDERER_SETTINGS = Object.freeze({
  fontFamily: "Cascadia Mono, Consolas, monospace",
  fontSize: 14,
  foregroundColor: "#CCCCCC",
  backgroundColor: "#0C0C0C",
  cursorColor: "#FFFFFF",
  selectionBackgroundColor: "#FFFFFF40",
  backgroundOpacity: 1,
  backgroundBlur: 0,
  cursorBlink: true,
  cursorStyle: "block",
  scrollSensitivity: 3,
  scrollback: 10000,
  rightClickPaste: true,
  screenReaderMode: false,
});

let rendererSettings = { ...DEFAULT_RENDERER_SETTINGS };

function clampNumber(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function normalizeNonEmptyString(value, fallback) {
  if (typeof value !== "string") return fallback;
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : fallback;
}

function normalizeFiniteNumber(value, fallback, min, max) {
  if (typeof value !== "number" || !Number.isFinite(value)) return fallback;
  return clampNumber(value, min, max);
}

function normalizeInteger(value, fallback, min, max) {
  if (typeof value !== "number" || !Number.isFinite(value)) return fallback;
  return Math.trunc(clampNumber(value, min, max));
}

function normalizeBoolean(value, fallback) {
  if (typeof value !== "boolean") return fallback;
  return value;
}

function normalizeCursorStyle(value, fallback) {
  if (value === "block" || value === "underline" || value === "bar")
    return value;
  return fallback;
}

function normalizeRendererSettings(payload, fallback) {
  const source = payload && typeof payload === "object" ? payload : {};
  return {
    fontFamily: normalizeNonEmptyString(source.fontFamily, fallback.fontFamily),
    fontSize: normalizeFiniteNumber(source.fontSize, fallback.fontSize, 6, 72),
    foregroundColor: normalizeNonEmptyString(
      source.foregroundColor,
      fallback.foregroundColor,
    ),
    backgroundColor: normalizeNonEmptyString(
      source.backgroundColor,
      fallback.backgroundColor,
    ),
    cursorColor: normalizeNonEmptyString(
      source.cursorColor,
      fallback.cursorColor,
    ),
    selectionBackgroundColor: normalizeNonEmptyString(
      source.selectionBackgroundColor,
      fallback.selectionBackgroundColor,
    ),
    backgroundOpacity: normalizeFiniteNumber(
      source.backgroundOpacity,
      fallback.backgroundOpacity,
      0,
      1,
    ),
    backgroundBlur: normalizeInteger(
      source.backgroundBlur,
      fallback.backgroundBlur,
      0,
      100,
    ),
    cursorBlink: normalizeBoolean(source.cursorBlink, fallback.cursorBlink),
    cursorStyle: normalizeCursorStyle(source.cursorStyle, fallback.cursorStyle),
    scrollSensitivity: normalizeFiniteNumber(
      source.scrollSensitivity,
      fallback.scrollSensitivity,
      0.1,
      10,
    ),
    scrollback: normalizeInteger(
      source.scrollback,
      fallback.scrollback,
      0,
      200000,
    ),
    rightClickPaste: normalizeBoolean(
      source.rightClickPaste,
      fallback.rightClickPaste,
    ),
    screenReaderMode: normalizeBoolean(
      source.screenReaderMode,
      fallback.screenReaderMode,
    ),
  };
}

function parseHexRgb(color) {
  if (typeof color !== "string") return null;
  const m = /^#([0-9a-fA-F]{6})$/.exec(color.trim());
  if (!m) return null;
  const hex = m[1];
  return {
    r: parseInt(hex.slice(0, 2), 16),
    g: parseInt(hex.slice(2, 4), 16),
    b: parseInt(hex.slice(4, 6), 16),
  };
}

function effectiveThemeBackground(backgroundColor, backgroundOpacity) {
  if (backgroundOpacity >= 1) return backgroundColor;
  const rgb = parseHexRgb(backgroundColor);
  if (!rgb) return backgroundColor;
  return `rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, ${backgroundOpacity})`;
}

function buildTheme(settings) {
  return {
    foreground: settings.foregroundColor,
    background: effectiveThemeBackground(
      settings.backgroundColor,
      settings.backgroundOpacity,
    ),
    cursor: settings.cursorColor,
    selectionBackground: settings.selectionBackgroundColor,
  };
}

function buildTerminalOptions(settings, fontSizeOverride) {
  const fontSize =
    typeof fontSizeOverride === "number" && Number.isFinite(fontSizeOverride)
      ? fontSizeOverride
      : settings.fontSize;
  return {
    allowProposedApi: true,
    allowTransparency: true,
    cursorBlink: settings.cursorBlink,
    cursorStyle: settings.cursorStyle,
    fontFamily: settings.fontFamily,
    fontSize,
    scrollSensitivity: settings.scrollSensitivity,
    scrollback: settings.scrollback,
    screenReaderMode: settings.screenReaderMode,
    theme: buildTheme(settings),
  };
}

function probeWebgl2Availability() {
  try {
    const canvas = document.createElement("canvas");
    const context = canvas.getContext("webgl2");
    return !!context;
  } catch {
    return false;
  }
}

function getWebglAddonConstructor() {
  const addonNamespace = window.WebglAddon;
  if (!addonNamespace || typeof addonNamespace !== "object") return null;
  const ctor = addonNamespace.WebglAddon;
  return typeof ctor === "function" ? ctor : null;
}

function applyPageBackground(settings) {
  const background =
    settings.backgroundOpacity < 1 ? "transparent" : settings.backgroundColor;
  document.documentElement.style.background = background;
  document.body.style.background = background;
}

function applyRendererSettingsToPane(pane, settings, fontSizeOverride) {
  const opts = buildTerminalOptions(settings, fontSizeOverride);
  pane.term.options.allowTransparency = true;
  pane.term.options.cursorBlink = opts.cursorBlink;
  pane.term.options.cursorStyle = opts.cursorStyle;
  pane.term.options.fontFamily = opts.fontFamily;
  pane.term.options.fontSize = opts.fontSize;
  pane.term.options.scrollSensitivity = opts.scrollSensitivity;
  pane.term.options.scrollback = opts.scrollback;
  pane.term.options.screenReaderMode = opts.screenReaderMode;
  pane.term.options.theme = opts.theme;
}

function applyRendererSettings(settings) {
  rendererSettings = normalizeRendererSettings(settings, rendererSettings);
  for (const [, pane] of panes)
    applyRendererSettingsToPane(pane, rendererSettings);
  applyPageBackground(rendererSettings);
  if (visiblePaneId !== null) fitAndReport(visiblePaneId);
  return panes.size;
}

function getRendererSettings(paneId) {
  const pane = panes.get(paneId);
  if (!pane) return null;
  const termOptions = pane.term.options;
  const theme = termOptions.theme || {};
  return {
    fontFamily: termOptions.fontFamily,
    fontSize: termOptions.fontSize,
    foregroundColor: theme.foreground || rendererSettings.foregroundColor,
    backgroundColor:
      theme.background ||
      effectiveThemeBackground(
        rendererSettings.backgroundColor,
        rendererSettings.backgroundOpacity,
      ),
    cursorColor: theme.cursor || rendererSettings.cursorColor,
    selectionBackgroundColor:
      theme.selectionBackground || rendererSettings.selectionBackgroundColor,
    backgroundOpacity: rendererSettings.backgroundOpacity,
    backgroundBlur: rendererSettings.backgroundBlur,
    cursorBlink: !!termOptions.cursorBlink,
    cursorStyle: termOptions.cursorStyle,
    scrollSensitivity: termOptions.scrollSensitivity,
    scrollback: termOptions.scrollback,
    rightClickPaste: rendererSettings.rightClickPaste,
    screenReaderMode: !!termOptions.screenReaderMode,
  };
}

function getRendererCapabilities(paneId) {
  const pane = panes.get(paneId);
  if (!pane) return null;
  return {
    webgl2Available: !!pane.webgl2Available,
    webglAddonAvailable: !!pane.webglAddonAvailable,
    rendererBackend: pane.rendererBackend || "builtIn",
  };
}

// e.key → the keymap key name (lowercased char or a named key); null = not interceptable
function normalizeKey(key) {
  if (typeof key !== "string") return null;
  if (key.length === 1) return key === " " ? "space" : key.toLowerCase();
  switch (key) {
    case "Tab":
      return "tab";
    case "Enter":
      return "return";
    case "Backspace":
      return "delete";
    case "ArrowLeft":
      return "left";
    case "ArrowRight":
      return "right";
    case "ArrowUp":
      return "up";
    case "ArrowDown":
      return "down";
    default:
      return null;
  }
}

const post = (msg) => window.chrome.webview.postMessage(msg);

const b64ToBytes = (b64) => {
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
};

function openPane(paneId, opts) {
  if (panes.has(paneId)) return null;
  const el = document.createElement("div");
  el.className = "pane";
  el.dataset.paneId = paneId;
  el.style.display = "none";
  container.appendChild(el);

  const term = new Terminal(
    buildTerminalOptions(
      rendererSettings,
      opts && typeof opts.fontSize === "number" ? opts.fontSize : null,
    ),
  );
  const fitAddon = new FitAddon.FitAddon();
  const searchAddon = new SearchAddon.SearchAddon();
  const serializeAddon = new SerializeAddon.SerializeAddon();
  term.loadAddon(fitAddon);
  term.loadAddon(searchAddon);
  term.loadAddon(serializeAddon);

  const pane = {
    term,
    fitAddon,
    searchAddon,
    serializeAddon,
    lastSearch: { index: -1, count: 0 },
    el,
  };
  panes.set(paneId, pane);

  // measure hidden panes too: render into the (visible-sized) container while display:none is
  // only toggled after open — xterm needs a laid-out element, so flip on, open+fit, restore.
  el.style.display = "block";
  term.open(el);

  const webgl2Available = probeWebgl2Availability();
  const webglAddonConstructor = getWebglAddonConstructor();
  const webglAddonAvailable = webglAddonConstructor !== null;
  pane.webgl2Available = webgl2Available;
  pane.webglAddonAvailable = webglAddonAvailable;
  pane.rendererBackend = "builtIn";
  pane.webglAddon = null;

  if (webgl2Available && webglAddonAvailable) {
    try {
      const webglAddon = new webglAddonConstructor();
      term.loadAddon(webglAddon);
      pane.webglAddon = webglAddon;
      pane.rendererBackend = "webgl";
      if (typeof webglAddon.onContextLoss === "function") {
        webglAddon.onContextLoss(() => {
          try {
            webglAddon.dispose();
          } catch {
            // Keep pane alive even if addon cleanup fails.
          }
          pane.webglAddon = null;
          pane.rendererBackend = "builtIn";
        });
      }
    } catch {
      pane.webglAddon = null;
      pane.rendererBackend = "builtIn";
    }
  }

  // right-click paste is controlled by page-global renderer settings and can be toggled at runtime.
  el.addEventListener(
    "contextmenu",
    (ev) => {
      if (!rendererSettings.rightClickPaste) return;
      if (ev.preventDefault) ev.preventDefault();
      post({ type: "pasteRequested", paneId });
    },
    true,
  );

  // app hotkeys never reach the shell: a keydown matching the host-pushed chord table goes
  // back as a chord message instead (the host's KeybindMatcher dispatches it)
  term.attachCustomKeyEventHandler((ev) => {
    if (ev.type !== "keydown") return true;
    const key = normalizeKey(ev.key);
    if (key === null) return true;
    const hit = interceptChords.some(
      (c) =>
        c.ctrl === ev.ctrlKey &&
        c.alt === ev.altKey &&
        c.shift === ev.shiftKey &&
        c.key === key,
    );
    if (!hit) return true;
    post({
      type: "chord",
      paneId,
      ctrl: ev.ctrlKey,
      alt: ev.altKey,
      shift: ev.shiftKey,
      key,
    });
    if (ev.preventDefault) ev.preventDefault();
    return false;
  });

  // user input → pty (xterm already encodes Enter as CR, arrows as VT sequences, etc.)
  term.onData((data) => post({ type: "data", paneId, data }));
  term.onBinary((data) => post({ type: "binary", paneId, data }));

  // OSC 0/1/2 → session title (manual VT parsing is banned; xterm's handler is the source)
  term.onTitleChange((title) => post({ type: "title", paneId, title }));

  // OSC 7 → session cwd; forwarded verbatim, parsed on the C# side. Return false so xterm's own
  // OSC 7 handling (if any) still runs.
  term.parser.registerOscHandler(7, (uri) => {
    post({ type: "cwd", paneId, uri });
    return false;
  });

  // OSC 9;9 — the ConEmu / Windows Terminal cwd convention (`ESC]9;9;"<path>"ESC\`); the handler
  // for identifier 9 receives everything after "9;", so a cwd report starts with another "9;".
  term.parser.registerOscHandler(9, (data) => {
    if (data.startsWith("9;"))
      post({ type: "cwd", paneId, uri: data.slice(2) });
    return false;
  });

  // track search hits so a read-back after find can report "N of M"
  searchAddon.onDidChangeResults((r) => {
    pane.lastSearch = {
      index: r ? r.resultIndex : -1,
      count: r ? r.resultCount : 0,
    };
  });

  fitAddon.fit();
  const size = { cols: term.cols, rows: term.rows };

  if (visiblePaneId === null) {
    visiblePaneId = paneId;
    term.focus();
  } else {
    el.style.display = "none";
  }
  return size;
}

function showPane(paneId) {
  const pane = panes.get(paneId);
  if (!pane) return null;
  for (const [id, p] of panes)
    p.el.style.display = id === paneId ? "block" : "none";
  visiblePaneId = paneId;
  fitAndReport(paneId);
  pane.term.focus();
  return true;
}

function closePane(paneId) {
  const pane = panes.get(paneId);
  if (!pane) return null;
  panes.delete(paneId);
  pane.term.dispose();
  pane.el.remove();
  if (visiblePaneId === paneId) visiblePaneId = null;
  return true;
}

function applyLayout(layout) {
  if (!layout || typeof layout !== "object") return false;
  if (typeof layout.type !== "string") return false;
  if (
    typeof layout.generation !== "number" ||
    !Number.isInteger(layout.generation)
  )
    return false;

  const generation = layout.generation;
  if (
    highestAcceptedLayoutGeneration !== null &&
    generation < highestAcceptedLayoutGeneration
  )
    return false;

  if (layout.type === "empty") {
    for (const [, p] of panes) p.el.style.display = "none";
    visiblePaneId = null;
    highestAcceptedLayoutGeneration = generation;
    return true;
  }

  if (layout.type === "single") {
    if (typeof layout.paneId !== "string" || typeof layout.title !== "string")
      return false;
    if (!panes.has(layout.paneId)) return false;
    const shown = showPane(layout.paneId);
    if (shown !== true) return false;
    highestAcceptedLayoutGeneration = generation;
    return true;
  }

  return false;
}

function fitAndReport(paneId) {
  const pane = panes.get(paneId);
  if (!pane) return null;
  pane.fitAddon.fit();
  post({ type: "resize", paneId, cols: pane.term.cols, rows: pane.term.rows });
  return { cols: pane.term.cols, rows: pane.term.rows };
}
window.addEventListener("resize", () => {
  if (visiblePaneId !== null) fitAndReport(visiblePaneId);
});

window.chrome.webview.addEventListener("message", (e) => {
  const msg = e.data;
  if (!msg || !msg.type) return;
  const pane = panes.get(msg.paneId);
  if (!pane) return;
  switch (msg.type) {
    case "output":
      pane.term.write(b64ToBytes(msg.data));
      break;
    case "options":
      if (typeof msg.fontSize === "number")
        pane.term.options.fontSize = msg.fontSize;
      fitAndReport(msg.paneId);
      break;
  }
});

// plain-text buffer read (session.text): default = the visible screen, all = screen + scrollback,
// lines = the last N lines of screen + scrollback. Lines are right-trimmed like the upstream read.
function bufferText(term, all, lines) {
  const buf = term.buffer.active;
  let start, end;
  if (lines != null && lines > 0) {
    // last N CONTENT lines of screen + scrollback: scan everything, trim, then tail-slice
    start = 0;
    end = buf.length;
  } else if (all) {
    start = 0;
    end = buf.length;
  } else {
    start = buf.viewportY;
    end = Math.min(buf.length, buf.viewportY + term.rows);
  }
  const out = [];
  for (let i = start; i < end; i++) {
    const line = buf.getLine(i);
    if (!line) {
      out.push("");
      continue;
    }
    const text = line.translateToString(true);
    // a soft-wrapped row continues its predecessor: one logical line, not two
    if (line.isWrapped && out.length > 0) out[out.length - 1] += text;
    else out.push(text);
  }
  // drop trailing blank rows (an idle screen read otherwise ends in dozens of empty lines)
  while (out.length > 0 && out[out.length - 1] === "") out.pop();
  return (lines != null && lines > 0 ? out.slice(-lines) : out).join("\n");
}

/// every entry answers null for an unknown paneId — the C# side's "not realized" shape
const withPane = (paneId, fn) => {
  const pane = panes.get(paneId);
  return pane ? fn(pane) : null;
};

window.agterm = {
  openPane,
  showPane,
  closePane,
  applyRendererSettings,
  getRendererSettings,
  getRendererCapabilities,
  applyLayout,
  fit: (paneId) => fitAndReport(paneId),
  text: (paneId, all, lines) =>
    withPane(paneId, (p) => bufferText(p.term, all, lines)),
  selection: (paneId) => withPane(paneId, (p) => p.term.getSelection()),
  selectAll: (paneId) =>
    withPane(paneId, (p) => {
      p.term.selectAll();
      return true;
    }),
  serialize: (paneId) => withPane(paneId, (p) => p.serializeAddon.serialize()),
  search: (paneId, needle, dir) =>
    withPane(paneId, (p) => {
      const opts = {
        decorations: {
          matchOverviewRuler: "#888888",
          activeMatchColorOverviewRuler: "#ffffff",
        },
      };
      const found =
        dir === "prev"
          ? p.searchAddon.findPrevious(needle, opts)
          : p.searchAddon.findNext(needle, opts);
      return { found, index: p.lastSearch.index, count: p.lastSearch.count };
    }),
  searchClose: (paneId) =>
    withPane(paneId, (p) => {
      p.searchAddon.clearDecorations();
      p.term.clearSelection();
      return true;
    }),
  paste: (paneId, text) =>
    withPane(paneId, (p) => {
      p.term.paste(text);
      return true;
    }),
  setFontSize: (paneId, px) =>
    withPane(paneId, (p) => {
      p.term.options.fontSize = px;
      fitAndReport(paneId);
      return p.term.options.fontSize;
    }),
  getFontSize: (paneId) => withPane(paneId, (p) => p.term.options.fontSize),
  focus: (paneId) =>
    withPane(paneId, (p) => {
      p.term.focus();
      return true;
    }),
  // page-global hotkey table (host-invoked so a following dispatch cannot outrun it)
  setChords: (items) => {
    interceptChords = items || [];
    return interceptChords.length;
  },
  syntheticKeyCodeFromKey: (key) => {
    if (typeof key !== "string") return 0;
    if (key.length === 1) return key.toUpperCase().charCodeAt(0);
    switch (key) {
      case "Tab":
      case "tab":
        return 9;
      case "Enter":
      case "return":
        return 13;
      case "Backspace":
      case "delete":
        return 8;
      case "ArrowLeft":
      case "left":
        return 37;
      case "ArrowUp":
      case "up":
        return 38;
      case "ArrowRight":
      case "right":
        return 39;
      case "ArrowDown":
      case "down":
        return 40;
      case " ":
      case "Space":
      case "space":
        return 32;
      default:
        return 0;
    }
  },
  // test-only: run a synthetic keydown through the real xterm key path (chord interception)
  dispatchKey: (paneId, opts) =>
    withPane(paneId, (p) => {
      const ev = new KeyboardEvent("keydown", {
        key: opts.key,
        ctrlKey: !!opts.ctrlKey,
        altKey: !!opts.altKey,
        shiftKey: !!opts.shiftKey,
        cancelable: true,
      });
      const code = window.agterm.syntheticKeyCodeFromKey(opts.key);
      Object.defineProperty(ev, "keyCode", { value: code, configurable: true });
      Object.defineProperty(ev, "which", { value: code, configurable: true });
      p.term.textarea.dispatchEvent(ev);
      return true;
    }),
};

applyPageBackground(rendererSettings);

post({ type: "ready" });
