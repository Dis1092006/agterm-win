// The JS half of the agterm terminal bridge, v2: ONE page hosts MANY xterm.js panes keyed by
// paneId (see XtermPage.cs, the C# half). Protocol:
//   host → page  (PostWebMessageAsJson):  {type:'output', paneId, data:<base64 VT bytes>}
//                                         {type:'options', paneId, fontSize?}
//   page → host  (chrome.webview.postMessage): {type:'ready'}                    page boot, once
//                                              {type:'data', paneId, data}       user keystrokes
//                                              {type:'binary', paneId, data}
//                                              {type:'userKeystroke', paneId, code, key, ctrl, alt, shift, meta}
//                                              {type:'resize', paneId, cols, rows}
//                                              {type:'title', paneId, title}     OSC 0/1/2
//                                              {type:'cwd', paneId, uri}         OSC 7 / OSC 9;9
//                                              {type:'chord', paneId, generation, ctrl, alt, shift, key}
//                                              {type:'customHotkey', paneId, generation, customCommandId, ctrl, alt, shift, key}
//                                              {type:'pasteRequested', paneId}   right-click paste
//                                              {type:'switcher', paneId?, key}   Ctrl-Tab switcher: key is
//                                                tab|reverseTab|escape|controlReleased
//                                              {type:'dashboardHighlight'|'dashboardActivate', controlRef}
//                                              {type:'dashboardClose'}
//                                              {type:'hudMetrics', paneId, fontSize, cellWidth, cellHeight, session:{width,height}, left?, right?, pane?}
//   host-invoked (ExecuteScriptAsync, result = JSON): agterm.openPane(paneId, {fontSize, theme}) → {cols,rows},
//     agterm.showPane(paneId), agterm.closePane(paneId), agterm.applyLayout(layout), and the per-pane reads/writes:
//     text/selection/selectAll/serialize/serializeForRestore/fit/search/searchClose/paste/setFontSize/getFontSize/focus —
//     each takes paneId first; every call on an unknown paneId returns null. Page-global:
//     agterm.setHotkeyManifest({generation,leaderTimeoutMs,builtinSingles,customBindings,
//       zoomPassthroughSingles,dashboardToggleSingles}) — atomic hotkey table.
//     agterm.setChords([{ctrl,alt,shift,key}]) — compatibility built-in-only hotkey table.
//     agterm.setSwitcherActive(bool): whether the window's Ctrl-Tab switch is still in progress.
//     agterm.applyRendererSettings(settings), agterm.getRendererSettings(paneId),
//     agterm.applyPaneThemes({paneId: theme|null}): a pane's own colors (a workspace theme); null
//       returns the pane to the page settings, and panes not named keep their assignment.
//     agterm.setTerminalBackground(color|null): the page, split gap and empty layout background
//       (the selected session's workspace theme); null returns to the page settings' background.
//     agterm.getRendererCapabilities(paneId).
//     agterm.applyHud(payload|null): the session.hud panel, drawn as the host sized it.
//     layout schema:
//       {type:'single', generation, paneId, title} |
//       {type:'empty', generation} |
//       {type:'zoomed', generation, paneId} |
//       {type:'split', generation, leftPaneId, rightPaneId, ratio, focusedPaneId} |
//       {type:'dashboard', generation, cells:[{paneId,controlRef}], highlightedControlRef, fontSize?} |
//       {type:'layered', generation,
//          base:{type:'single', paneId} | {type:'split', leftPaneId, rightPaneId, ratio, focusedPaneId},
//          layer:{paneId, sizePercent?, backgroundColor?}}.
//     applyLayout rejects stale generations (< highest accepted) with false and does no DOM changes.
// The FIRST opened pane is shown automatically; window resizes refit all visible panes.
"use strict";

const panes = new Map(); // paneId → {term, fitAddon, searchAddon, serializeAddon, lastSearch, el, webgl2Available, webglAddonAvailable, rendererBackend, webglAddon?}
let visiblePaneId = null;
let visibleLayout = { type: "empty", paneIds: [] };
let programmaticFocusPaneId = null;
let highestAcceptedLayoutGeneration = null;
let interceptChords = []; // [{ctrl, alt, shift, key}] — hotkeys the HOST owns; see 'chords' below
let hotkeyGeneration = 0;
// a Ctrl-Tab switch in progress: assumed from the page's own Ctrl+Tab, cleared by the Ctrl release or
// by the host (a switch that did not begin, or one that ended in the window)
let switcherActive = false;
let customHotkeyBindings = [];
let zoomPassthroughChords = [];
let dashboardToggleChords = [];
let armedCustomChords = [];
let customLeaderTimer = null;
let customLeaderTimeoutMs = 1500;
const container = document.getElementById("panes");
const searchBarEl = document.getElementById("search-bar");
const searchInputEl = document.getElementById("search-input");
const searchStatusEl = document.getElementById("search-status");
const searchPreviousEl = document.getElementById("search-previous");
const searchNextEl = document.getElementById("search-next");
const searchCloseEl = document.getElementById("search-close");
const SPLIT_MIN_RATIO = 0.05;
const SPLIT_MAX_RATIO = 0.95;
const SPLIT_DIVIDER_WIDTH_PX = 4;
const OVERLAY_CATCHER_Z_INDEX = 10;
const OVERLAY_LAYER_Z_INDEX = 20;
const HEX_COLOR_RE = /^#[0-9a-fA-F]{6}$/;
const HEX_DIGITS_RE = /^[0-9a-fA-F]{6}$/;
// HudLayout's panel constants in AgTerm.Core: the page draws the box the store measured
const HUD_PADDING_WIDTH_PX = 8;
const HUD_PADDING_HEIGHT_PX = 6;
const HUD_FRAME_COLUMNS = 2;
const HUD_FRAME_ROWS = 1;
const HUD_EDGE_MARGIN = 0.1;
const HUD_BORDER = "1px solid rgba(255, 255, 255, 0.3)";
const HUD_CORNER_RADIUS_PX = 8;
const HUD_BANDS = Object.freeze({ top: -1, left: -1, center: 0, bottom: 1, right: 1 });

let splitDividerEl = null;
let splitDragState = null;
let splitFitRafHandle = 0;
let overlayCatcherEl = null;
let dashboardActivationTimer = null;
let askLayerEl = null;
let askResizeObserver = null;
let askState = null; // { askId, paneId, coveredPaneIds, releasedLayerPaneId, payload }
let hudLayerEl = null;
let hudState = null; // { revision, payload }
let hudSpinnerTimer = null;
let hudSpinnerFrame = 0;
let hudProbeEl = null;
let hudRefreshQueued = false;
let lastHudMetricsKey = null;

function searchDecorations() {
  return {
    matchOverviewRuler: "#888888",
    activeMatchColorOverviewRuler: "#ffffff",
  };
}

function searchOptions(incremental = false) {
  return {
    caseSensitive: true,
    incremental,
    decorations: searchDecorations(),
  };
}

function openSearch(paneId, needle, displayText, focusInput = true) {
  const pane = panes.get(paneId);
  if (!pane || !searchBarEl || !searchInputEl || !searchStatusEl) return false;
  if (searchBarEl.parentElement !== pane.el) pane.el.appendChild(searchBarEl);
  searchBarEl.dataset.paneId = paneId;
  searchInputEl.value = typeof needle === "string" ? needle : "";
  searchStatusEl.textContent =
    typeof displayText === "string" ? displayText : "";
  searchBarEl.hidden = false;
  if (focusInput) {
    searchInputEl.focus();
    searchInputEl.select();
  }
  return true;
}

function closeSearch(paneId, restoreFocus) {
  if (
    !searchBarEl ||
    searchBarEl.hidden ||
    searchBarEl.dataset.paneId !== paneId
  )
    return false;
  const pane = panes.get(paneId);
  if (pane) {
    pane.searchAddon.clearDecorations();
    pane.term.clearSelection();
    pane.lastSearch = { index: -1, count: 0 };
    pane.appliedSearch = { needle: "", selected: null };
  }
  searchBarEl.hidden = true;
  delete searchBarEl.dataset.paneId;
  if (restoreFocus) {
    const target = visibleFocusedPaneId();
    if (target) focusPane(target, false);
  }
  return true;
}

function runSearchFromBar(direction, queryOnly) {
  if (!searchBarEl || searchBarEl.hidden || !searchInputEl) return false;
  const paneId = searchBarEl.dataset.paneId;
  const pane = panes.get(paneId);
  if (!pane) return false;
  const needle = searchInputEl.value;
  if (needle.length === 0) {
    pane.searchAddon.clearDecorations();
    pane.term.clearSelection();
    pane.lastSearch = { index: -1, count: 0 };
    pane.appliedSearch = { needle: "", selected: null };
    post({ type: "searchUpdated", paneId, needle, count: 0, selected: null });
    return true;
  }
  const opts = searchOptions(queryOnly);
  const found =
    direction === "prev"
      ? pane.searchAddon.findPrevious(needle, opts)
      : pane.searchAddon.findNext(needle, opts);
  const selected =
    !queryOnly && found && pane.lastSearch.index >= 0
      ? pane.lastSearch.index + 1
      : null;
  pane.appliedSearch = { needle, selected };
  post({
    type: "searchUpdated",
    paneId,
    needle,
    count: pane.lastSearch.count,
    selected,
  });
  return true;
}

function applySearch(state) {
  if (!state || typeof state.paneId !== "string") return false;
  const pane = panes.get(state.paneId);
  if (!pane) return false;
  const needle = typeof state.needle === "string" ? state.needle : "";
  const selected =
    Number.isInteger(state.selected) && state.selected > 0
      ? state.selected
      : null;
  const previous = pane.appliedSearch;

  if (needle !== previous.needle) {
    if (needle.length === 0) {
      pane.searchAddon.clearDecorations();
      pane.term.clearSelection();
      pane.lastSearch = { index: -1, count: 0 };
    } else {
      pane.searchAddon.findNext(needle, searchOptions(true));
    }
    pane.appliedSearch = { needle, selected: null };
  }

  if (needle.length > 0 && selected !== pane.appliedSearch.selected) {
    const count = pane.lastSearch.count;
    const current = pane.appliedSearch.selected;
    if (selected === null) {
      // The incremental query already highlights the first hit; model selection remains unset
      // until an explicit next/previous command.
    } else if (current === null && selected === 1) {
      // The addon's incremental query is already on the first hit.
    } else if (current === null && count > 0 && selected === count) {
      pane.searchAddon.findPrevious(needle, searchOptions());
    } else if (
      current !== null &&
      count > 0 &&
      selected === (current % count) + 1
    ) {
      pane.searchAddon.findNext(needle, searchOptions());
    } else if (
      current !== null &&
      count > 0 &&
      selected === (current <= 1 ? count : current - 1)
    ) {
      pane.searchAddon.findPrevious(needle, searchOptions());
    } else {
      pane.searchAddon.findNext(needle, searchOptions(true));
      for (let index = 1; index < selected; index += 1)
        pane.searchAddon.findNext(needle, searchOptions());
    }
    pane.appliedSearch = { needle, selected };
  }

  return openSearch(
    state.paneId,
    needle,
    typeof state.displayText === "string" ? state.displayText : "",
    state.focusInput === true,
  );
}

function hideSearch(state) {
  if (!state || !searchBarEl) return false;
  const displayedOwnerPaneId = searchBarEl.dataset.paneId;
  if (typeof state.clearOwnerPaneId === "string") {
    const owner = panes.get(state.clearOwnerPaneId);
    if (owner) {
      owner.searchAddon.clearDecorations();
      owner.term.clearSelection();
      owner.lastSearch = { index: -1, count: 0 };
      owner.appliedSearch = { needle: "", selected: null };
    }
  }
  const shouldHide =
    typeof state.clearOwnerPaneId !== "string" ||
    displayedOwnerPaneId === state.clearOwnerPaneId;
  if (shouldHide) {
    searchBarEl.hidden = true;
    delete searchBarEl.dataset.paneId;
    if (typeof state.restorePaneId === "string")
      focusPane(state.restorePaneId, false);
  }
  return true;
}

function getSearchState(paneId) {
  const pane = panes.get(paneId);
  if (!pane) return null;
  return {
    needle: pane.appliedSearch.needle,
    count: pane.lastSearch.count,
    selected: pane.appliedSearch.selected,
  };
}

const DEFAULT_RENDERER_SETTINGS = Object.freeze({
  fontFamily: "Cascadia Mono, Consolas, monospace",
  fontSize: 14,
  foregroundColor: "#CCCCCC",
  backgroundColor: "#0C0C0C",
  cursorColor: "#FFFFFF",
  selectionBackgroundColor: "#FFFFFF40",
  cursorTextColor: null,
  selectionForegroundColor: null,
  backgroundOpacity: 1,
  backgroundBlur: 0,
  cursorBlink: true,
  cursorStyle: "block",
  scrollSensitivity: 3,
  scrollback: 10000,
  rightClickPaste: true,
  screenReaderMode: false,
  ansiColors: null,
  preservePaneFontSizes: false,
});

const ANSI_THEME_KEYS = Object.freeze([
  "black",
  "red",
  "green",
  "yellow",
  "blue",
  "magenta",
  "cyan",
  "white",
  "brightBlack",
  "brightRed",
  "brightGreen",
  "brightYellow",
  "brightBlue",
  "brightMagenta",
  "brightCyan",
  "brightWhite",
]);

let rendererSettings = { ...DEFAULT_RENDERER_SETTINGS };

// the renderer settings a pane's own theme replaces; font, opacity, cursor and scroll stay global
const PANE_THEME_KEYS = Object.freeze([
  "foregroundColor",
  "backgroundColor",
  "cursorColor",
  "cursorTextColor",
  "selectionBackgroundColor",
  "selectionForegroundColor",
  "ansiColors",
]);

let terminalBackgroundColor = null;

function clampNumber(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function getVisiblePaneIds() {
  if (!visibleLayout || !Array.isArray(visibleLayout.paneIds)) return [];
  return visibleLayout.paneIds;
}

function emitPaneFocus(paneId) {
  if (!panes.has(paneId)) return;
  post({ type: "paneFocus", paneId });
  if (visibleLayout && visibleLayout.type === "split")
    visibleLayout.focusedPaneId = paneId;
}

function focusPane(paneId, emitEvent) {
  const pane = panes.get(paneId);
  if (!pane) return false;
  if (askCovers(paneId)) {
    focusAskLayer();
    if (emitEvent) emitPaneFocus(paneId);
    return true;
  }
  programmaticFocusPaneId = paneId;
  try {
    pane.term.focus();
  } finally {
    programmaticFocusPaneId = null;
  }
  if (emitEvent) emitPaneFocus(paneId);
  return true;
}

function setVisibleLayout(nextLayout) {
  visibleLayout = nextLayout;
  const paneIds = getVisiblePaneIds();
  visiblePaneId = paneIds.length > 0 ? paneIds[0] : null;
}

function visibleFocusedPaneId() {
  if (!visibleLayout) return null;
  if (visibleLayout.type === "single" || visibleLayout.type === "zoomed")
    return visibleLayout.paneId;
  if (visibleLayout.type === "split") return visibleLayout.focusedPaneId;
  if (visibleLayout.type === "layered") return visibleLayout.layerPaneId;
  if (visibleLayout.type === "dashboard")
    return visibleLayout.highlightedPaneId;
  return null;
}

function hideAllPanes() {
  for (const [, pane] of panes) pane.el.style.display = "none";
}

function stopSplitDrag(postResize) {
  if (!splitDragState || !splitDragState.active) {
    splitDragState = null;
    return;
  }

  const state = splitDragState;
  splitDragState = null;

  if (
    splitDividerEl &&
    typeof splitDividerEl.releasePointerCapture === "function"
  ) {
    try {
      if (splitDividerEl.hasPointerCapture(state.pointerId))
        splitDividerEl.releasePointerCapture(state.pointerId);
    } catch {
      // Ignore pointer capture release failures.
    }
  }

  window.removeEventListener("pointermove", onSplitDragPointerMove);
  window.removeEventListener("pointerup", onSplitDragPointerUp);
  window.removeEventListener("pointercancel", onSplitDragPointerCancel);

  if (postResize) {
    fitAndReportPaneIds([state.leftPaneId, state.rightPaneId]);
    post({
      type: "splitResize",
      leftPaneId: state.leftPaneId,
      rightPaneId: state.rightPaneId,
      ratio: state.ratio,
    });
  }
}

function removeSplitDivider() {
  stopSplitDrag(false);
  if (splitDividerEl) {
    splitDividerEl.removeEventListener(
      "pointerdown",
      onSplitDividerPointerDown,
    );
    splitDividerEl.remove();
    splitDividerEl = null;
  }
}

function clearSplitPresentation() {
  if (splitFitRafHandle !== 0) {
    window.cancelAnimationFrame(splitFitRafHandle);
    splitFitRafHandle = 0;
  }
  removeSplitDivider();
  container.style.display = "";
  container.style.gridTemplateColumns = "";
  container.style.gridTemplateRows = "";
}

function clearDashboardPresentation() {
  if (dashboardActivationTimer !== null) {
    clearTimeout(dashboardActivationTimer);
    dashboardActivationTimer = null;
  }
  container.classList.remove("dashboard-layout");
  container.style.gridTemplateColumns = "";
  container.style.gridTemplateRows = "";
  for (const [, pane] of panes) {
    if (typeof pane.sessionFontSize === "number")
      pane.term.options.fontSize = pane.sessionFontSize;
    pane.el.classList.remove("dashboard-cell", "dashboard-highlighted");
    delete pane.el.dataset.dashboardControlRef;
    pane.el.style.gridColumn = "";
    pane.el.style.gridRow = "";
  }
}

function removeOverlayCatcher() {
  if (!overlayCatcherEl) return;
  overlayCatcherEl.removeEventListener(
    "pointerdown",
    onOverlayCatcherPointerDown,
  );
  overlayCatcherEl.removeEventListener("click", onOverlayCatcherClick);
  overlayCatcherEl.remove();
  overlayCatcherEl = null;
}

function clearPaneOverlayStyles(pane) {
  if (pane.overlayBackgroundColor !== null) {
    pane.overlayBackgroundColor = null;
    applyRendererSettingsToPane(pane, rendererSettings);
  }
  pane.el.classList.remove("overlay-base-pane", "overlay-layer-pane");
  pane.el.style.position = "";
  pane.el.style.inset = "";
  pane.el.style.top = "";
  pane.el.style.left = "";
  pane.el.style.transform = "";
  pane.el.style.width = "";
  pane.el.style.height = "";
  pane.el.style.zIndex = "";
  pane.el.style.background = "";
  pane.el.style.borderRadius = "";
  pane.el.style.boxShadow = "";
  pane.el.style.border = "";
  pane.el.style.pointerEvents = "";
  pane.el.style.visibility = "";
}

function clearLayeredPresentationStyles() {
  removeOverlayCatcher();
  container.classList.remove("layered-layout");
  container.style.position = "";
  for (const [, pane] of panes) clearPaneOverlayStyles(pane);
}

function onOverlayCatcherPointerDown(ev) {
  if (!visibleLayout || visibleLayout.type !== "layered") return;
  if (ev.preventDefault) ev.preventDefault();
  if (ev.stopPropagation) ev.stopPropagation();
  focusPane(visibleLayout.layerPaneId, false);
}

function onOverlayCatcherClick(ev) {
  if (!visibleLayout || visibleLayout.type !== "layered") return;
  if (ev.preventDefault) ev.preventDefault();
  if (ev.stopPropagation) ev.stopPropagation();
  focusPane(visibleLayout.layerPaneId, true);
}

function ensureOverlayCatcher() {
  if (!overlayCatcherEl) {
    const catcher = document.createElement("div");
    catcher.className = "overlay-catcher";
    catcher.addEventListener("pointerdown", onOverlayCatcherPointerDown);
    catcher.addEventListener("click", onOverlayCatcherClick);
    overlayCatcherEl = catcher;
  }

  if (overlayCatcherEl.parentElement !== container)
    container.appendChild(overlayCatcherEl);

  overlayCatcherEl.style.display = "block";
  overlayCatcherEl.style.position = "absolute";
  overlayCatcherEl.style.inset = "0";
  overlayCatcherEl.style.zIndex = String(OVERLAY_CATCHER_Z_INDEX);
  overlayCatcherEl.style.pointerEvents = "auto";
  overlayCatcherEl.style.background = "transparent";

  return overlayCatcherEl;
}

function hasOnlyKeys(value, keys) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const allowed = new Set(keys);
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) return false;
  }
  return true;
}

function normalizeKnownPaneId(value) {
  if (typeof value !== "string") return null;
  const paneId = value.trim();
  if (paneId.length === 0) return null;
  if (!panes.has(paneId)) return null;
  return paneId;
}

function validateLayeredBaseLayout(baseLayout) {
  if (
    !hasOnlyKeys(baseLayout, [
      "type",
      "paneId",
      "leftPaneId",
      "rightPaneId",
      "ratio",
      "focusedPaneId",
    ])
  ) {
    return null;
  }

  if (typeof baseLayout.type !== "string") return null;

  if (baseLayout.type === "empty") {
    if (!hasOnlyKeys(baseLayout, ["type"])) return null;
    return { type: "empty", paneIds: [] };
  }

  if (baseLayout.type === "single") {
    if (!hasOnlyKeys(baseLayout, ["type", "paneId"])) return null;
    const paneId = normalizeKnownPaneId(baseLayout.paneId);
    if (!paneId) return null;
    return { type: "single", paneIds: [paneId], paneId };
  }

  if (baseLayout.type === "split") {
    if (
      !hasOnlyKeys(baseLayout, [
        "type",
        "leftPaneId",
        "rightPaneId",
        "ratio",
        "focusedPaneId",
      ])
    ) {
      return null;
    }

    const validSplitLayout = validateSplitLayout(baseLayout);
    if (!validSplitLayout) return null;
    return {
      type: "split",
      paneIds: [validSplitLayout.leftPaneId, validSplitLayout.rightPaneId],
      split: validSplitLayout,
    };
  }

  return null;
}

function validateLayeredLayout(layout) {
  if (!hasOnlyKeys(layout, ["type", "generation", "base", "layer"]))
    return null;

  const base = validateLayeredBaseLayout(layout.base);
  if (!base) return null;

  if (!hasOnlyKeys(layout.layer, ["paneId", "sizePercent", "backgroundColor"]))
    return null;

  const layerPaneId = normalizeKnownPaneId(layout.layer.paneId);
  if (!layerPaneId) return null;
  if (base.paneIds.includes(layerPaneId)) return null;

  let sizePercent = null;
  if (
    layout.layer.sizePercent !== undefined &&
    layout.layer.sizePercent !== null
  ) {
    if (
      typeof layout.layer.sizePercent !== "number" ||
      !Number.isFinite(layout.layer.sizePercent) ||
      !Number.isInteger(layout.layer.sizePercent)
    ) {
      return null;
    }

    if (layout.layer.sizePercent < 1 || layout.layer.sizePercent > 100)
      return null;
    sizePercent = layout.layer.sizePercent;
  }

  let backgroundColor = null;
  if (
    layout.layer.backgroundColor !== undefined &&
    layout.layer.backgroundColor !== null
  ) {
    if (typeof layout.layer.backgroundColor !== "string") return null;
    if (!HEX_COLOR_RE.test(layout.layer.backgroundColor)) return null;
    backgroundColor = layout.layer.backgroundColor;
  }

  return {
    base,
    layerPaneId,
    sizePercent,
    backgroundColor,
  };
}

function applyLayeredPresentation(validLayered, shouldFocusLayer) {
  if (validLayered.base.type === "single") {
    if (!applySinglePresentation(validLayered.base.paneId, false, false))
      return false;
  } else if (validLayered.base.type === "split") {
    if (!applySplitPresentation(validLayered.base.split, false, false))
      return false;
  } else {
    applyEmptyPresentation();
  }

  const layerPane = panes.get(validLayered.layerPaneId);
  if (!layerPane) return false;

  container.classList.add("layered-layout");
  container.style.position = "relative";
  ensureOverlayCatcher();

  for (const basePaneId of validLayered.base.paneIds) {
    const basePane = panes.get(basePaneId);
    if (!basePane) return false;
    basePane.el.classList.add("overlay-base-pane");
    // Full cover keeps base xterms mounted and sized so output continues without a remount.
    // visibility, unlike opacity, lets a pane question inside the base pane show itself again.
    basePane.el.style.visibility = validLayered.sizePercent === null ? "hidden" : "";
    basePane.el.style.pointerEvents =
      validLayered.sizePercent === null ? "none" : "auto";
  }

  layerPane.el.classList.add("overlay-layer-pane");
  layerPane.el.style.display = "block";
  layerPane.el.style.gridColumn = "";
  layerPane.el.style.gridRow = "";
  layerPane.el.style.position = "absolute";
  layerPane.el.style.pointerEvents = "auto";
  layerPane.el.style.zIndex = String(OVERLAY_LAYER_Z_INDEX);

  if (validLayered.sizePercent === null) {
    layerPane.el.style.inset = "0";
    layerPane.el.style.width = "100%";
    layerPane.el.style.height = "100%";
  } else {
    const size = `${validLayered.sizePercent}%`;
    layerPane.el.style.top = "50%";
    layerPane.el.style.left = "50%";
    layerPane.el.style.transform = "translate(-50%, -50%)";
    layerPane.el.style.width = size;
    layerPane.el.style.height = size;
    layerPane.el.style.borderRadius = "10px";
    layerPane.el.style.border = "1px solid rgba(255, 255, 255, 0.2)";
    layerPane.el.style.boxShadow = "0 16px 48px rgba(0, 0, 0, 0.45)";
  }

  layerPane.overlayBackgroundColor = validLayered.backgroundColor;
  applyRendererSettingsToPane(layerPane, rendererSettings);

  setVisibleLayout({
    type: "layered",
    paneIds: [...validLayered.base.paneIds, validLayered.layerPaneId],
    base: validLayered.base,
    layerPaneId: validLayered.layerPaneId,
    sizePercent: validLayered.sizePercent,
    backgroundColor: validLayered.backgroundColor,
  });

  fitAndReportPaneIds([...validLayered.base.paneIds, validLayered.layerPaneId]);
  if (shouldFocusLayer) focusPane(validLayered.layerPaneId, true);

  return true;
}

function scheduleSplitFit(leftPaneId, rightPaneId) {
  if (splitFitRafHandle !== 0) return;
  splitFitRafHandle = window.requestAnimationFrame(() => {
    splitFitRafHandle = 0;
    fitAndReportPaneIds([leftPaneId, rightPaneId]);
  });
}

function setSplitGridColumns(ratio) {
  container.style.gridTemplateColumns = `${ratio}fr ${SPLIT_DIVIDER_WIDTH_PX}px ${1 - ratio}fr`;
}

function ensureSplitDivider() {
  if (splitDividerEl) return splitDividerEl;

  const divider = document.createElement("div");
  divider.className = "split-divider";
  divider.setAttribute("role", "separator");
  divider.setAttribute("tabindex", "0");
  divider.setAttribute("aria-orientation", "vertical");
  divider.setAttribute("aria-valuemin", String(SPLIT_MIN_RATIO * 100));
  divider.setAttribute("aria-valuemax", String(SPLIT_MAX_RATIO * 100));
  divider.addEventListener("pointerdown", onSplitDividerPointerDown);
  splitDividerEl = divider;
  return divider;
}

function updateSplitDividerAriaRatio(ratio) {
  if (!splitDividerEl) return;
  splitDividerEl.setAttribute("aria-valuenow", String(Math.round(ratio * 100)));
}

function validateSplitLayout(layout) {
  if (typeof layout.leftPaneId !== "string") return null;
  if (typeof layout.rightPaneId !== "string") return null;
  if (typeof layout.focusedPaneId !== "string") return null;
  if (typeof layout.ratio !== "number" || !Number.isFinite(layout.ratio))
    return null;

  const leftPaneId = layout.leftPaneId.trim();
  const rightPaneId = layout.rightPaneId.trim();
  const focusedPaneId = layout.focusedPaneId.trim();
  if (leftPaneId.length === 0 || rightPaneId.length === 0) return null;
  if (leftPaneId === rightPaneId) return null;
  if (!panes.has(leftPaneId) || !panes.has(rightPaneId)) return null;

  if (focusedPaneId !== leftPaneId && focusedPaneId !== rightPaneId)
    return null;

  return {
    leftPaneId,
    rightPaneId,
    ratio: clampNumber(layout.ratio, SPLIT_MIN_RATIO, SPLIT_MAX_RATIO),
    focusedPaneId,
  };
}

function applySinglePresentation(paneId, shouldFitAndReport, shouldFocus) {
  const pane = panes.get(paneId);
  if (!pane) return false;

  clearLayeredPresentationStyles();
  clearSplitPresentation();
  clearDashboardPresentation();
  for (const [id, p] of panes)
    p.el.style.display = id === paneId ? "block" : "none";
  pane.el.style.gridColumn = "";
  pane.el.style.gridRow = "";

  setVisibleLayout({ type: "single", paneIds: [paneId], paneId });

  if (shouldFitAndReport) fitAndReport(paneId);
  if (shouldFocus) focusPane(paneId, true);
  return true;
}

function applyEmptyPresentation() {
  clearLayeredPresentationStyles();
  clearSplitPresentation();
  clearDashboardPresentation();
  hideAllPanes();
  setVisibleLayout({ type: "empty", paneIds: [] });
}

function applySplitPresentation(splitLayout, shouldFitAndReport, shouldFocus) {
  const leftPane = panes.get(splitLayout.leftPaneId);
  const rightPane = panes.get(splitLayout.rightPaneId);
  if (!leftPane || !rightPane) return false;

  clearLayeredPresentationStyles();
  clearSplitPresentation();
  clearDashboardPresentation();
  const divider = ensureSplitDivider();

  container.style.display = "grid";
  container.style.gridTemplateRows = "100%";
  setSplitGridColumns(splitLayout.ratio);

  if (divider.parentElement !== container) container.appendChild(divider);
  divider.style.display = "block";
  divider.style.gridColumn = "2";
  divider.style.gridRow = "1";
  updateSplitDividerAriaRatio(splitLayout.ratio);

  for (const [id, pane] of panes) {
    const visible =
      id === splitLayout.leftPaneId || id === splitLayout.rightPaneId;
    pane.el.style.display = visible ? "block" : "none";
    if (id === splitLayout.leftPaneId) {
      pane.el.style.gridColumn = "1";
      pane.el.style.gridRow = "1";
    } else if (id === splitLayout.rightPaneId) {
      pane.el.style.gridColumn = "3";
      pane.el.style.gridRow = "1";
    } else {
      pane.el.style.gridColumn = "";
      pane.el.style.gridRow = "";
    }
  }

  setVisibleLayout({
    type: "split",
    paneIds: [splitLayout.leftPaneId, splitLayout.rightPaneId],
    leftPaneId: splitLayout.leftPaneId,
    rightPaneId: splitLayout.rightPaneId,
    focusedPaneId: splitLayout.focusedPaneId,
    ratio: splitLayout.ratio,
  });

  if (shouldFitAndReport)
    fitAndReportPaneIds([splitLayout.leftPaneId, splitLayout.rightPaneId]);
  if (shouldFocus) focusPane(splitLayout.focusedPaneId, true);

  return true;
}

function validateDashboardLayout(layout) {
  if (
    !hasOnlyKeys(layout, [
      "type",
      "generation",
      "cells",
      "highlightedControlRef",
      "fontSize",
    ]) ||
    !Array.isArray(layout.cells) ||
    layout.cells.length < 1 ||
    layout.cells.length > 9 ||
    typeof layout.highlightedControlRef !== "string" ||
    layout.highlightedControlRef.length === 0
  ) {
    return null;
  }

  if (
    layout.fontSize !== undefined &&
    (typeof layout.fontSize !== "number" ||
      !Number.isFinite(layout.fontSize) ||
      layout.fontSize <= 0)
  ) {
    return null;
  }

  const seenPaneIds = new Set();
  const seenControlRefs = new Set();
  const cells = [];
  for (const cell of layout.cells) {
    if (!hasOnlyKeys(cell, ["paneId", "controlRef"])) return null;
    const paneId = normalizeKnownPaneId(cell.paneId);
    if (
      !paneId ||
      typeof cell.controlRef !== "string" ||
      cell.controlRef.length === 0
    )
      return null;
    if (seenPaneIds.has(paneId) || seenControlRefs.has(cell.controlRef))
      return null;
    seenPaneIds.add(paneId);
    seenControlRefs.add(cell.controlRef);
    cells.push({ paneId, controlRef: cell.controlRef });
  }

  const highlighted = cells.find(
    (cell) => cell.controlRef === layout.highlightedControlRef,
  );
  if (!highlighted) return null;
  return {
    cells,
    highlightedControlRef: layout.highlightedControlRef,
    highlightedPaneId: highlighted.paneId,
    fontSize: layout.fontSize ?? null,
  };
}

function applyDashboardPresentation(dashboard) {
  clearLayeredPresentationStyles();
  clearSplitPresentation();
  clearDashboardPresentation();

  const count = dashboard.cells.length;
  const columns = Math.ceil(Math.sqrt(count));
  const rows = Math.ceil(count / columns);
  container.classList.add("dashboard-layout");
  container.style.gridTemplateColumns = `repeat(${columns}, minmax(0, 1fr))`;
  container.style.gridTemplateRows = `repeat(${rows}, minmax(0, 1fr))`;

  const paneIds = dashboard.cells.map((cell) => cell.paneId);
  for (const [id, pane] of panes) {
    const index = paneIds.indexOf(id);
    pane.el.style.display = index >= 0 ? "block" : "none";
    if (index < 0) continue;
    const cell = dashboard.cells[index];
    pane.el.classList.add("dashboard-cell");
    pane.el.classList.toggle(
      "dashboard-highlighted",
      cell.controlRef === dashboard.highlightedControlRef,
    );
    pane.el.dataset.dashboardControlRef = cell.controlRef;
    if (dashboard.fontSize !== null)
      pane.term.options.fontSize = dashboard.fontSize;
  }

  setVisibleLayout({
    type: "dashboard",
    paneIds,
    cells: dashboard.cells,
    highlightedControlRef: dashboard.highlightedControlRef,
    highlightedPaneId: dashboard.highlightedPaneId,
    fontSize: dashboard.fontSize,
  });
  fitAndReportPaneIds(paneIds);
  focusPane(dashboard.highlightedPaneId, false);
  return true;
}

function setDashboardHighlight(controlRef, emitEvent) {
  if (!visibleLayout || visibleLayout.type !== "dashboard") return false;
  const cell = visibleLayout.cells.find(
    (candidate) => candidate.controlRef === controlRef,
  );
  if (!cell) return false;

  visibleLayout.highlightedControlRef = cell.controlRef;
  visibleLayout.highlightedPaneId = cell.paneId;
  for (const candidate of visibleLayout.cells) {
    panes
      .get(candidate.paneId)
      ?.el.classList.toggle(
        "dashboard-highlighted",
        candidate.controlRef === controlRef,
      );
  }
  if (emitEvent) post({ type: "dashboardHighlight", controlRef });
  return true;
}

function moveDashboardHighlight(direction) {
  if (!visibleLayout || visibleLayout.type !== "dashboard") return false;
  const cells = visibleLayout.cells;
  const from = cells.findIndex(
    (cell) => cell.controlRef === visibleLayout.highlightedControlRef,
  );
  if (from < 0) return false;
  const columns = Math.ceil(Math.sqrt(cells.length));
  const column = from % columns;
  const row = Math.floor(from / columns);
  let to = from;
  if (direction === "left" && column > 0) to = from - 1;
  if (direction === "right" && column + 1 < columns && from + 1 < cells.length)
    to = from + 1;
  if (direction === "up" && row > 0) to = from - columns;
  if (direction === "down" && (row + 1) * columns + column < cells.length)
    to = (row + 1) * columns + column;
  return setDashboardHighlight(cells[to].controlRef, to !== from);
}

function onDashboardKeyDown(ev) {
  if (!visibleLayout || visibleLayout.type !== "dashboard") return;
  const directions = {
    ArrowLeft: "left",
    ArrowRight: "right",
    ArrowUp: "up",
    ArrowDown: "down",
  };
  if (directions[ev.key]) {
    moveDashboardHighlight(directions[ev.key]);
  } else if (ev.key === "Enter") {
    post({
      type: "dashboardActivate",
      controlRef: visibleLayout.highlightedControlRef,
    });
  } else if (ev.key === "Escape") {
    post({ type: "dashboardClose" });
  } else {
    const key = normalizeKey(ev.key);
    const chord = key
      ? {
          ctrl: ev.ctrlKey,
          alt: ev.altKey,
          shift: ev.shiftKey,
          key,
        }
      : null;
    if (
      chord &&
      dashboardToggleChords.some((candidate) =>
        samePhysicalChord(candidate, chord),
      )
    ) {
      post({
        type: "chord",
        paneId: visibleLayout.highlightedPaneId,
        generation: hotkeyGeneration,
        ctrl: chord.ctrl,
        alt: chord.alt,
        shift: chord.shift,
        key: chord.key,
      });
    }
  }
  if (ev.preventDefault) ev.preventDefault();
  if (ev.stopImmediatePropagation) ev.stopImmediatePropagation();
}

function onDashboardClick(ev) {
  if (!visibleLayout || visibleLayout.type !== "dashboard") return;
  const cellElement = ev.target?.closest?.("[data-dashboard-control-ref]");
  const controlRef = cellElement?.dataset?.dashboardControlRef;
  if (!controlRef || !setDashboardHighlight(controlRef, true)) return;
  if (ev.preventDefault) ev.preventDefault();
  if (ev.stopImmediatePropagation) ev.stopImmediatePropagation();

  if (dashboardActivationTimer !== null) clearTimeout(dashboardActivationTimer);
  const expectedLayout = visibleLayout;
  dashboardActivationTimer = setTimeout(() => {
    dashboardActivationTimer = null;
    if (
      visibleLayout === expectedLayout &&
      visibleLayout.highlightedControlRef === controlRef
    ) {
      post({ type: "dashboardActivate", controlRef });
    }
  }, 180);
}

function onDashboardPointerDown(ev) {
  if (!visibleLayout || visibleLayout.type !== "dashboard") return;
  if (ev.preventDefault) ev.preventDefault();
  if (ev.stopImmediatePropagation) ev.stopImmediatePropagation();
}

function ratioFromClientX(clientX) {
  const rect = container.getBoundingClientRect();
  if (!Number.isFinite(rect.width) || rect.width <= 0) return null;
  const rawRatio = (clientX - rect.left) / rect.width;
  return clampNumber(rawRatio, SPLIT_MIN_RATIO, SPLIT_MAX_RATIO);
}

function onSplitDividerPointerDown(ev) {
  if (!visibleLayout || visibleLayout.type !== "split") return;

  const ratio = ratioFromClientX(ev.clientX);
  const startRatio = ratio === null ? visibleLayout.ratio : ratio;
  splitDragState = {
    active: true,
    pointerId: ev.pointerId,
    leftPaneId: visibleLayout.leftPaneId,
    rightPaneId: visibleLayout.rightPaneId,
    ratio: startRatio,
  };

  if (
    splitDividerEl &&
    typeof splitDividerEl.setPointerCapture === "function"
  ) {
    try {
      splitDividerEl.setPointerCapture(ev.pointerId);
    } catch {
      // Ignore capture failures and continue with window listeners.
    }
  }

  setSplitGridColumns(startRatio);
  updateSplitDividerAriaRatio(startRatio);
  if (ev.preventDefault) ev.preventDefault();

  window.addEventListener("pointermove", onSplitDragPointerMove);
  window.addEventListener("pointerup", onSplitDragPointerUp);
  window.addEventListener("pointercancel", onSplitDragPointerCancel);
}

function onSplitDragPointerMove(ev) {
  if (!splitDragState || !splitDragState.active) return;
  const ratio = ratioFromClientX(ev.clientX);
  if (ratio === null) return;

  splitDragState.ratio = ratio;
  setSplitGridColumns(ratio);
  updateSplitDividerAriaRatio(ratio);
  scheduleSplitFit(splitDragState.leftPaneId, splitDragState.rightPaneId);
}

function onSplitDragPointerUp(ev) {
  if (!splitDragState || !splitDragState.active) return;
  if (splitDragState.pointerId !== ev.pointerId) return;

  const ratio = ratioFromClientX(ev.clientX);
  if (ratio !== null) splitDragState.ratio = ratio;

  const dragState = {
    leftPaneId: splitDragState.leftPaneId,
    rightPaneId: splitDragState.rightPaneId,
    ratio: splitDragState.ratio,
  };

  stopSplitDrag(false);

  const nextLayout = {
    leftPaneId: dragState.leftPaneId,
    rightPaneId: dragState.rightPaneId,
    ratio: dragState.ratio,
    focusedPaneId: visibleLayout.focusedPaneId,
  };

  applySplitPresentation(nextLayout, true, false);
  post({
    type: "splitResize",
    leftPaneId: dragState.leftPaneId,
    rightPaneId: dragState.rightPaneId,
    ratio: dragState.ratio,
  });
}

function onSplitDragPointerCancel(ev) {
  if (!splitDragState || !splitDragState.active) return;
  if (splitDragState.pointerId !== ev.pointerId) return;
  stopSplitDrag(false);
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

function normalizeAnsiColors(value, fallback) {
  // C# sends every key and writes null for "this theme has none"; a key missing from a partial
  // payload (tests, older callers) keeps the previous value instead
  if (value === null) return null;
  if (
    !Array.isArray(value) ||
    value.length !== ANSI_THEME_KEYS.length ||
    value.some(
      (color) => typeof color !== "string" || color.trim().length === 0,
    )
  )
    return fallback;
  return value.map((color) => color.trim());
}

function normalizeOptionalColor(value, fallback) {
  if (value === null) return null;
  return normalizeNonEmptyString(value, fallback);
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
    cursorTextColor: normalizeOptionalColor(
      source.cursorTextColor,
      fallback.cursorTextColor,
    ),
    selectionForegroundColor: normalizeOptionalColor(
      source.selectionForegroundColor,
      fallback.selectionForegroundColor,
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
    ansiColors: normalizeAnsiColors(source.ansiColors, fallback.ansiColors),
    preservePaneFontSizes: normalizeBoolean(
      source.preservePaneFontSizes,
      fallback.preservePaneFontSizes,
    ),
  };
}

function normalizePaneTheme(theme) {
  if (theme === null || typeof theme !== "object") return null;
  const normalized = normalizeRendererSettings(theme, rendererSettings);
  return Object.fromEntries(PANE_THEME_KEYS.map((key) => [key, normalized[key]]));
}

function paneSettings(pane, settings) {
  return pane && pane.themeOverride ? { ...settings, ...pane.themeOverride } : settings;
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
  const theme = {
    foreground: settings.foregroundColor,
    background: effectiveThemeBackground(
      settings.backgroundColor,
      settings.backgroundOpacity,
    ),
    cursor: settings.cursorColor,
    selectionBackground: settings.selectionBackgroundColor,
  };
  if (settings.cursorTextColor !== null)
    theme.cursorAccent = settings.cursorTextColor;
  if (settings.selectionForegroundColor !== null)
    theme.selectionForeground = settings.selectionForegroundColor;
  if (settings.ansiColors !== null) {
    ANSI_THEME_KEYS.forEach((key, index) => {
      theme[key] = settings.ansiColors[index];
    });
  }
  return theme;
}

// US scan codes of the letter keys, the same table xterm.js's win32-input-mode encoder uses
const LETTER_SCAN_CODES = {
  A: 30, B: 48, C: 46, D: 32, E: 18, F: 33, G: 34, H: 35, I: 23, J: 36, K: 37, L: 38, M: 50,
  N: 49, O: 24, P: 25, Q: 16, R: 19, S: 31, T: 20, U: 22, V: 47, W: 17, X: 45, Y: 21, Z: 44,
};

// A win32-input-mode record (ESC [ Vk ; Sc ; Uc ; Kd ; Cs ; Rc _) for Ctrl and a letter key
// ("KeyZ"), with the lowercase Latin letter as the character. PSReadLine names a key whose
// character is a control character through the keyboard layout, so on a non-Latin layout 0x1a
// becomes Ctrl+я; with the letter it is Ctrl+z on any layout. The other fields copy xterm's own
// encoder: Cs is LEFT_CTRL (xterm sets RIGHT_CTRL only for the ControlRight key itself, and
// PowerShell sees Control either way), and no lock flags.
function win32CtrlLetter(code, keyDown) {
  const letter = code.slice(3);
  return `\x1b[${letter.charCodeAt(0)};${LETTER_SCAN_CODES[letter]};`
    + `${letter.toLowerCase().charCodeAt(0)};${keyDown ? 1 : 0};8;1_`;
}

// the event.code a physical key reports, for the keys tests drive (letters/digits are positional
// names, not the produced character; the rest are named after the key itself)
function syntheticCodeFromKey(key) {
  if (typeof key !== "string" || key.length === 0) return "";
  if (key.length === 1) {
    const c = key.toUpperCase();
    if (c >= "A" && c <= "Z") return `Key${c}`;
    if (c >= "0" && c <= "9") return `Digit${c}`;
    switch (key) {
      case " ":
        return "Space";
      case "`":
      case "~":
        return "Backquote";
      default:
        return "";
    }
  }
  switch (key) {
    case "Enter":
    case "return":
      return "Enter";
    case "Tab":
    case "tab":
      return "Tab";
    case "Backspace":
    case "delete":
      return "Backspace";
    case "Escape":
      return "Escape";
    case "ArrowLeft":
    case "left":
      return "ArrowLeft";
    case "ArrowUp":
    case "up":
      return "ArrowUp";
    case "ArrowRight":
    case "right":
      return "ArrowRight";
    case "ArrowDown":
    case "down":
      return "ArrowDown";
    case "Space":
    case "space":
      return "Space";
    default:
      return "";
  }
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
    // Ghostty reports these on macOS; on Windows they are the engine's, and an app that never
    // asks for them still gets the legacy encoding. Without the kitty protocol Enter and
    // Shift+Enter are both CR, so Claude Code cannot bind newline-without-submit.
    vtExtensions: { kittyKeyboard: true, win32InputMode: true },
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
    settings.backgroundOpacity < 1
      ? "transparent"
      : terminalBackgroundColor ?? settings.backgroundColor;
  document.documentElement.style.background = background;
  document.body.style.background = background;
}

function setTerminalBackground(color) {
  terminalBackgroundColor =
    typeof color === "string" && color.trim().length > 0 ? color.trim() : null;
  applyPageBackground(rendererSettings);
  return true;
}

// a question or a HUD without a pane covers the whole session; the focused pane carries the
// session's theme
function colorsForPane(paneId) {
  const id = paneId ?? visibleFocusedPaneId();
  return paneSettings(id ? panes.get(id) : null, rendererSettings);
}

function applyRendererSettingsToPane(pane, settings, fontSizeOverride) {
  const opts = buildTerminalOptions(paneSettings(pane, settings), fontSizeOverride);
  if (pane.overlayBackgroundColor !== null)
    opts.theme.background = pane.overlayBackgroundColor;
  // xterm fills only whole rows; the strip below the last row must not show the pane under the layer
  if (pane.el.classList.contains("overlay-layer-pane"))
    pane.el.style.background = opts.theme.background;
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

function paneFontSize(paneId, pane) {
  return visibleLayout?.type === "dashboard" &&
    visibleLayout.paneIds.includes(paneId) &&
    visibleLayout.fontSize !== null
    ? visibleLayout.fontSize
    : pane.sessionFontSize;
}

function applyRendererSettings(settings) {
  rendererSettings = normalizeRendererSettings(settings, rendererSettings);
  for (const [paneId, pane] of panes) {
    if (!rendererSettings.preservePaneFontSizes)
      pane.sessionFontSize = rendererSettings.fontSize;
    applyRendererSettingsToPane(pane, rendererSettings, paneFontSize(paneId, pane));
  }
  applyPageBackground(rendererSettings);
  if (hudState) renderHud();
  fitAndReportVisiblePanes();
  return panes.size;
}

function applyPaneThemes(assignments) {
  if (!assignments || typeof assignments !== "object") return 0;
  let applied = 0;
  for (const [paneId, theme] of Object.entries(assignments)) {
    const pane = panes.get(paneId);
    if (!pane) continue;
    pane.themeOverride = normalizePaneTheme(theme);
    applyRendererSettingsToPane(pane, rendererSettings, paneFontSize(paneId, pane));
    applied++;
  }
  if (hudState) renderHud();
  return applied;
}

function getRendererSettings(paneId) {
  const pane = panes.get(paneId);
  if (!pane) return null;
  const termOptions = pane.term.options;
  const theme = termOptions.theme || {};
  const effective = paneSettings(pane, rendererSettings);
  return {
    fontFamily: termOptions.fontFamily,
    fontSize: termOptions.fontSize,
    foregroundColor: theme.foreground || effective.foregroundColor,
    backgroundColor:
      theme.background ||
      effectiveThemeBackground(
        effective.backgroundColor,
        rendererSettings.backgroundOpacity,
      ),
    cursorColor: theme.cursor || effective.cursorColor,
    selectionBackgroundColor:
      theme.selectionBackground || effective.selectionBackgroundColor,
    cursorTextColor: theme.cursorAccent ?? null,
    selectionForegroundColor: theme.selectionForeground ?? null,
    backgroundOpacity: rendererSettings.backgroundOpacity,
    backgroundBlur: rendererSettings.backgroundBlur,
    cursorBlink: !!termOptions.cursorBlink,
    cursorStyle: termOptions.cursorStyle,
    scrollSensitivity: termOptions.scrollSensitivity,
    scrollback: termOptions.scrollback,
    rightClickPaste: rendererSettings.rightClickPaste,
    screenReaderMode: !!termOptions.screenReaderMode,
    ansiColors:
      effective.ansiColors === null
        ? null
        : ANSI_THEME_KEYS.map((key) => theme[key]),
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

function keymapKey(ev) {
  const key = normalizeKey(ev.key);
  if (
    typeof ev.key === "string" &&
    ev.key.length === 1 &&
    !/^[\x00-\x7f]$/.test(ev.key) &&
    !(ev.ctrlKey && ev.altKey) &&
    /^Key[A-Z]$/.test(ev.code || "")
  ) {
    return ev.code.slice(3).toLowerCase();
  }
  return key;
}

// Terminal-style ask (upstream AskDialogView, terminal style). The host owns the highlight and the
// answer; the page draws the payload and reports intents, so both dialog styles share one model.
function rgbaFromHex(color, alpha) {
  const rgb = parseHexRgb(color);
  return rgb ? `rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, ${alpha})` : color;
}

function askIntentFromKey(ev) {
  if (ev.ctrlKey || ev.altKey || ev.metaKey) return null;
  switch (ev.key) {
    case "Tab":
      return { intent: ev.shiftKey ? "backward" : "forward" };
    case "ArrowRight":
    case "ArrowDown":
      return { intent: "forward" };
    case "ArrowLeft":
    case "ArrowUp":
      return { intent: "backward" };
    case "Enter":
      return { intent: "activate" };
    case "Escape":
      return { intent: "cancel" };
  }
  // the virtual-key rule: on a non-Latin layout the key's Latin position names the hotkey
  const letter = /^[a-zA-Z]$/.test(ev.key)
    ? ev.key.toLowerCase()
    : /^Key[A-Z]$/.test(ev.code || "")
      ? ev.code.slice(3).toLowerCase()
      : null;
  return letter ? { intent: "hotkey", letter } : null;
}

// the layer sits inside its pane element, whose focusin would report a pane focus nobody asked for
function focusAskLayer() {
  programmaticFocusPaneId = askState.paneId;
  try {
    askLayerEl.focus();
  } finally {
    programmaticFocusPaneId = null;
  }
}

function askCovers(paneId) {
  return (
    askLayerEl !== null &&
    askState.coveredPaneIds.includes(paneId) &&
    askState.releasedLayerPaneId !== paneId
  );
}

function askCoversLayer(paneId) {
  return (
    askState?.paneId != null &&
    visibleLayout?.type === "layered" &&
    visibleLayout.layerPaneId === paneId &&
    askCovers(paneId)
  );
}

// Upstream deferMouseToAsk: a click on the program outside a pane question gives the program the
// keys. Over a split the session's focus moves to the other side, as upstream flips splitFocused,
// and the host republishes the question without the layer; the release holds until then. A pane
// shown alone has no other side, so the release holds until the user clicks the question or a new
// one arrives.
function releaseAskLayer(layerPaneId) {
  askState.releasedLayerPaneId = layerPaneId;
  const base = visibleLayout.base;
  const otherSide =
    base.type === "split" ? base.paneIds.find((id) => id !== askState.paneId) : undefined;
  if (otherSide) emitPaneFocus(otherSide);
}

// Upstream wantsFocus turns true when the focused pane becomes covered, for example when the
// overlay opens over a pending question or the session's focus comes back to the question's side.
function updateAskCoverage(coveredPaneIds) {
  const focusedPaneId = visibleFocusedPaneId();
  const wasCovered = askCovers(focusedPaneId);
  askState.coveredPaneIds = coveredPaneIds;
  if (!coveredPaneIds.includes(askState.releasedLayerPaneId)) askState.releasedLayerPaneId = null;
  if (!wasCovered && askCovers(focusedPaneId)) focusAskLayer();
}

// The pane that takes the keys after the answer, and that window chords over the question come
// from. A session-wide question uses the pane the layout focuses: the split's right pane, or the
// scratch or overlay drawn over the main pane. A pane question under a layer gives way to the layer,
// as upstream releases focus to the topmost surface: a full-size layer hides its base pane, which
// cannot take focus, and a partial one would send the keys to a shell drawn under it.
function askOwnerPaneId() {
  if (!askState) return null;
  const underLayer =
    visibleLayout?.type === "layered" && askState.paneId !== visibleLayout.layerPaneId;
  if (underLayer) return visibleFocusedPaneId();
  return askState.paneId ?? visibleFocusedPaneId();
}

// window chords keep working over a question; only built-in chords, as the menu would
function routeAskChord(ev) {
  const key = keymapKey(ev);
  const paneId = askOwnerPaneId();
  if (key === null || paneId === null) return;
  const chord = { ctrl: ev.ctrlKey, alt: ev.altKey, shift: ev.shiftKey, key };
  if (!interceptChords.some((c) => samePhysicalChord(c, chord))) return;
  post({ type: "chord", paneId, generation: hotkeyGeneration, ...chord });
}

function onAskKeyDown(ev) {
  if (!askState) return;
  ev.preventDefault();
  ev.stopPropagation();
  const intent = askIntentFromKey(ev);
  if (intent) post({ type: "askIntent", askId: askState.askId, ...intent });
  else routeAskChord(ev);
}

function askFontSize(paneId) {
  const pane = paneId ? panes.get(paneId) : null;
  return pane ? pane.term.options.fontSize : rendererSettings.fontSize;
}

function buildAskLayer(payload) {
  const layer = document.createElement("div");
  layer.className = payload.paneId ? "ask-layer" : "ask-layer ask-session";
  layer.tabIndex = 0;
  layer.dataset.askId = payload.askId;
  layer.setAttribute("role", "dialog");
  layer.setAttribute("aria-label", payload.title);
  layer.addEventListener("keydown", onAskKeyDown);
  layer.addEventListener("focus", () => post({ type: "askFocus", askId: payload.askId }));
  // a layer being removed is reported by removeAsk, so a blur fired during removal must stay silent
  layer.addEventListener("blur", () => {
    if (askLayerEl === layer) post({ type: "askFocus", askId: null });
  });
  layer.addEventListener("pointerdown", (ev) => {
    if (ev.target.closest(".ask-button")) return;
    ev.preventDefault();
    askState.releasedLayerPaneId = null;
    focusAskLayer();
    if (payload.paneId) emitPaneFocus(payload.paneId);
  });

  const colors = colorsForPane(payload.paneId);
  const fg = colors.foregroundColor;
  const bg = colors.backgroundColor;
  const fontSize = askFontSize(payload.paneId);
  const cell = Math.max(8, fontSize * 0.6);

  const panel = document.createElement("div");
  panel.className = "ask-panel";
  panel.style.background = bg;
  panel.style.color = fg;
  panel.style.borderColor = rgbaFromHex(fg, 0.3);
  panel.style.fontFamily = rendererSettings.fontFamily;
  panel.style.fontSize = `${fontSize}px`;
  panel.style.padding = `${2 * cell}px`;
  panel.style.gap = `${1.5 * cell}px`;

  const title = document.createElement("div");
  title.className = "ask-title";
  title.textContent = payload.title;
  panel.appendChild(title);
  if (payload.message) {
    const message = document.createElement("div");
    message.className = "ask-message";
    message.style.color = rgbaFromHex(fg, 0.7);
    message.textContent = payload.message;
    panel.appendChild(message);
  }

  const row = document.createElement("div");
  row.className = "ask-buttons-row";
  row.style.justifyContent =
    payload.align === "left" ? "flex-start" : payload.align === "center" ? "center" : "flex-end";
  const buttons = document.createElement("div");
  buttons.className = "ask-buttons";
  buttons.style.gap = `${cell}px`;
  payload.buttons.forEach((choice, index) => {
    const button = document.createElement("button");
    button.className = choice.destructive ? "ask-button ask-destructive" : "ask-button";
    button.tabIndex = -1;
    button.style.padding = `${0.4 * cell}px ${0.5 * cell}px`;
    for (const run of choice.runs) {
      const node = run.underline ? document.createElement("u") : document.createElement("span");
      node.textContent = run.text;
      button.appendChild(node);
    }
    // answering with the mouse must not move focus
    button.addEventListener("pointerdown", (ev) => ev.preventDefault());
    button.addEventListener("click", () =>
      post({ type: "askIntent", askId: payload.askId, intent: "press", index }),
    );
    buttons.appendChild(button);
  });
  row.appendChild(buttons);
  panel.appendChild(row);
  layer.appendChild(panel);
  return layer;
}

function applyAskHighlight(payload) {
  if (!askLayerEl) return;
  const colors = colorsForPane(payload.paneId);
  const fg = colors.foregroundColor;
  const bg = colors.backgroundColor;
  askLayerEl.querySelectorAll(".ask-button").forEach((button, index) => {
    const on = index === payload.highlighted;
    button.classList.toggle("ask-highlighted", on);
    button.style.background = on ? fg : rgbaFromHex(fg, 0.12);
    button.style.color = on ? bg : fg;
  });
}

function layoutAsk(payload) {
  if (!askLayerEl) return;
  const panel = askLayerEl.querySelector(".ask-panel");
  const buttons = askLayerEl.querySelector(".ask-buttons");
  const cell = Math.max(8, askFontSize(payload.paneId) * 0.6);
  const area = askLayerEl.getBoundingClientRect();
  const maxWidth =
    typeof payload.width === "number"
      ? (area.width * payload.width) / 100
      : Math.min(area.width * 0.9, 72 * cell);
  panel.style.maxWidth = `${Math.max(0, maxWidth)}px`;
  panel.style.width = typeof payload.width === "number" ? `${Math.max(0, maxWidth)}px` : "max-content";
  panel.style.maxHeight = `${Math.max(0, area.height - 2 * cell)}px`;
  buttons.classList.remove("ask-column");
  if (buttons.scrollWidth > buttons.parentElement.clientWidth + 0.5)
    buttons.classList.add("ask-column");
}

function removeAsk() {
  if (!askLayerEl) return;
  const hadFocus = askLayerEl.contains(document.activeElement);
  const paneId = askOwnerPaneId();
  if (askResizeObserver) askResizeObserver.disconnect();
  askResizeObserver = null;
  const layer = askLayerEl;
  askLayerEl = null;
  askState = null;
  layer.remove();
  if (hadFocus) post({ type: "askFocus", askId: null });
  if (hadFocus && paneId) focusPane(paneId, false);
}

function applyAsk(payload) {
  if (!payload) {
    removeAsk();
    return true;
  }
  const sameAsk = askState && askState.askId === payload.askId && askState.paneId === payload.paneId;
  if (!sameAsk) {
    const coveredFocus = visibleFocusedPaneId();
    removeAsk();
    const host = payload.paneId ? panes.get(payload.paneId)?.el : document.body;
    if (!host) return false;
    askLayerEl = buildAskLayer(payload);
    host.appendChild(askLayerEl);
    askState = {
      askId: payload.askId,
      paneId: payload.paneId,
      coveredPaneIds: payload.coveredPaneIds,
      releasedLayerPaneId: null,
    };
    askResizeObserver = new ResizeObserver(() => {
      if (askState && askState.payload) layoutAsk(askState.payload);
    });
    askResizeObserver.observe(host);
    if (payload.coveredPaneIds.includes(coveredFocus)) focusAskLayer();
  } else {
    updateAskCoverage(payload.coveredPaneIds);
  }
  applyAskHighlight(payload);
  layoutAsk(payload);
  askState.payload = payload;
  return true;
}

window.addEventListener("resize", () => {
  if (askState && askState.payload) layoutAsk(askState.payload);
});

// session.hud (upstream WindowContentView+Detail.swift and hud.sh). The host wraps and sizes the
// panel; the page draws the rows it gets and reports the cell and the pane sizes the host measures
// with, so the panel on screen and the sizes `tree` reads back come from the same numbers. The panel
// is passive: it takes no focus and no pointer input.
const hudGeometryObserver = new ResizeObserver(() => scheduleHudRefresh());
hudGeometryObserver.observe(container);

// the store keeps a color as the caller sent it, and the #rrggbb rule lets the hash be left out
function hudColor(color, fallback) {
  if (typeof color !== "string") return fallback;
  if (HEX_COLOR_RE.test(color)) return color;
  return HEX_DIGITS_RE.test(color) ? `#${color}` : fallback;
}

// The store fixes a panel's font at open, as upstream reads it once, so a later session zoom does
// not resize it. Before any panel is up the host scales the renderer-font cell to the font it opens with.
function hudFontSize(payload) {
  if (payload && typeof payload.fontSize === "number" && Number.isFinite(payload.fontSize))
    return payload.fontSize;
  return rendererSettings.fontSize;
}

// HudLayout counts columns and rows in this cell: the advance of one digit and its line box
function measureHudCell(fontSize) {
  if (!hudProbeEl) {
    hudProbeEl = document.createElement("span");
    hudProbeEl.className = "hud-probe";
    hudProbeEl.setAttribute("aria-hidden", "true");
    hudProbeEl.textContent = "0";
    document.body.appendChild(hudProbeEl);
  }
  hudProbeEl.style.fontFamily = rendererSettings.fontFamily;
  hudProbeEl.style.fontSize = `${fontSize}px`;
  const rect = hudProbeEl.getBoundingClientRect();
  return { width: rect.width, height: rect.height };
}

// The panes of the base layout the page shows. The page does not know pane roles: a hidden split
// shows its right pane alone, so a single pane is reported as `pane` and the host names its role.
// Zoom and the dashboard draw no panel, and their cells are not the session's panes, so they report
// nothing.
function hudMeasuredPanes() {
  const layout = visibleLayout?.type === "layered" ? visibleLayout.base : visibleLayout;
  if (!layout || (layout.type !== "single" && layout.type !== "split")) return null;
  const [firstPaneId, rightPaneId = null] = layout.paneIds;
  return { firstPaneId, rightPaneId };
}

function hudRect(el) {
  const rect = el.getBoundingClientRect();
  return { width: rect.width, height: rect.height };
}

function hudMetricsMessage() {
  const shown = hudMeasuredPanes();
  const first = shown ? panes.get(shown.firstPaneId) : null;
  if (!first) return null;
  const right = shown.rightPaneId ? panes.get(shown.rightPaneId) : null;
  if (shown.rightPaneId && !right) return null;
  const fontSize = hudFontSize(hudState ? hudState.payload : null);
  const cell = measureHudCell(fontSize);
  const message = {
    type: "hudMetrics",
    paneId: shown.firstPaneId,
    fontSize,
    // the probe font is the drawn panel's, and the page posts nothing when the panel closes, so the
    // store reads the font of a HUD with none of its own from here
    baseFontSize: rendererSettings.fontSize,
    cellWidth: cell.width,
    cellHeight: cell.height,
    session: hudRect(container),
  };
  if (right) {
    message.left = hudRect(first.el);
    message.right = hudRect(right.el);
  } else {
    message.pane = hudRect(first.el);
  }
  return message;
}

// equal values are not posted again: the host answers new metrics with a redraw, which measures again
function reportHudMetrics() {
  const message = hudMetricsMessage();
  if (!message) return;
  const key = JSON.stringify(message);
  if (key === lastHudMetricsKey) return;
  lastHudMetricsKey = key;
  post(message);
}

// fitAndReport runs once per pane on a layout change; one refresh after the last of them is enough
function scheduleHudRefresh() {
  if (hudRefreshQueued) return;
  hudRefreshQueued = true;
  queueMicrotask(() => {
    hudRefreshQueued = false;
    if (hudState) renderHud();
    reportHudMetrics();
  });
}

function hudBands(position) {
  const [vertical, horizontal] =
    position === "center" ? ["center", "center"] : String(position).split("-");
  return { vertical: HUD_BANDS[vertical] ?? 0, horizontal: HUD_BANDS[horizontal] ?? 0 };
}

// upstream OverlayPanelStyle.offset: half the room the panel and its edge margin leave on the axis,
// toward the anchor's edge
function hudOffset(extent, fraction, band, margin) {
  return band * Math.max(0, extent * ((1 - fraction) / 2 - margin));
}

function layoutHud() {
  const host = hudLayerEl.parentElement;
  if (!host) return;
  const payload = hudState.payload;
  const area = host.getBoundingClientRect();
  const width = (area.width * payload.widthPercent) / 100;
  const height =
    typeof payload.heightPoints === "number"
      ? payload.heightPoints
      : (area.height * payload.heightPercent) / 100;
  const margin = payload.sticky ? 0 : HUD_EDGE_MARGIN;
  const bands = hudBands(payload.position);
  const left =
    (area.width - width) / 2 +
    hudOffset(area.width, payload.widthPercent / 100, bands.horizontal, margin);
  const top =
    (area.height - height) / 2 +
    hudOffset(area.height, height / Math.max(area.height, 1), bands.vertical, margin);
  hudLayerEl.style.left = `${left}px`;
  hudLayerEl.style.top = `${top}px`;
  hudLayerEl.style.width = `${width}px`;
  hudLayerEl.style.height = `${height}px`;
}

function hudSpinnerGlyph() {
  const frames = hudState.payload.spinnerFrames;
  return Array.isArray(frames) && frames.length > 0
    ? frames[hudSpinnerFrame % frames.length]
    : null;
}

function buildHudRow(row, glyph, cell) {
  const el = document.createElement("div");
  el.className = row.dim ? "hud-row hud-dim" : "hud-row";
  el.style.height = `${cell.height}px`;
  el.style.lineHeight = `${cell.height}px`;
  if (glyph !== null) {
    const spinner = document.createElement("span");
    spinner.className = "hud-spinner";
    spinner.textContent = glyph;
    el.appendChild(spinner);
  }
  const text = document.createElement("span");
  text.className = "hud-text";
  text.textContent = glyph !== null ? ` ${row.text}` : row.text;
  el.appendChild(text);
  return el;
}

function renderHud() {
  const payload = hudState.payload;
  const fontSize = hudFontSize(payload);
  const cell = measureHudCell(fontSize);
  const frameRows = payload.frame ? HUD_FRAME_ROWS : 0;
  const style = hudLayerEl.style;
  style.fontFamily = rendererSettings.fontFamily;
  style.fontSize = `${fontSize}px`;
  const colors = colorsForPane(payload.paneId ?? null);
  style.background = hudColor(payload.background, colors.backgroundColor);
  style.color = hudColor(payload.foreground, colors.foregroundColor);
  style.border = payload.frame ? HUD_BORDER : "0";
  style.borderRadius = payload.frame ? `${HUD_CORNER_RADIUS_PX}px` : "0";
  style.padding =
    `${HUD_PADDING_HEIGHT_PX + frameRows * cell.height}px ` +
    `${HUD_PADDING_WIDTH_PX + HUD_FRAME_COLUMNS * cell.width}px`;
  const glyph = hudSpinnerGlyph();
  if (payload.markdown === true)
    hudLayerEl.replaceChildren(buildHudMarkdownBlock(payload.rows, glyph, cell));
  else
    hudLayerEl.replaceChildren(
      ...payload.rows.map((row, index) => buildHudRow(row, index === 0 ? glyph : null, cell)),
    );
  layoutHud();
}

function stopHudSpinner() {
  if (hudSpinnerTimer !== null) clearInterval(hudSpinnerTimer);
  hudSpinnerTimer = null;
  hudSpinnerFrame = 0;
}

function startHudSpinner() {
  const frames = hudState.payload.spinnerFrames;
  const interval = hudState.payload.spinnerInterval;
  if (!Array.isArray(frames) || frames.length < 2 || !(interval > 0)) return;
  hudSpinnerTimer = setInterval(() => {
    hudSpinnerFrame = (hudSpinnerFrame + 1) % frames.length;
    const glyph = hudLayerEl ? hudLayerEl.querySelector(".hud-spinner") : null;
    if (glyph) glyph.textContent = frames[hudSpinnerFrame];
  }, interval * 1000);
}

function removeHud() {
  stopHudSpinner();
  if (hudLayerEl) hudLayerEl.remove();
  hudLayerEl = null;
  hudState = null;
  setHudLinkHover(false);
}

function sameHudSpinnerFrames(left, right) {
  if (!Array.isArray(left) || !Array.isArray(right)) return left === right;
  return left.length === right.length && left.every((frame, index) => frame === right[index]);
}

// The revision is counted per session and this page serves every session of its window, so an
// equal revision alone can come from another session's panel. The timer is kept only when it
// would write the same frames at the same pace into the same host.
function keepsHudSpinner(payload) {
  if (hudState === null) return false;
  const shown = hudState.payload;
  return (
    hudState.revision === payload.revision &&
    (shown.paneId ?? null) === (payload.paneId ?? null) &&
    shown.spinnerInterval === payload.spinnerInterval &&
    sameHudSpinnerFrames(shown.spinnerFrames, payload.spinnerFrames)
  );
}

function applyHud(payload) {
  if (!payload) {
    removeHud();
    return true;
  }
  const host = payload.paneId ? panes.get(payload.paneId)?.el : container;
  if (!host) {
    removeHud();
    return false;
  }
  // a re-measure of the same panel keeps the spinner turning; an open or an update brings a new
  // revision and restarts it with that revision's frames
  const samePanel = keepsHudSpinner(payload);
  if (!samePanel) stopHudSpinner();
  if (!hudLayerEl) {
    hudLayerEl = document.createElement("div");
    hudLayerEl.className = "hud-layer";
  }
  if (hudLayerEl.parentElement !== host) host.appendChild(hudLayerEl);
  hudState = { revision: payload.revision, payload };
  renderHud();
  if (!samePanel) startHudSpinner();
  reportHudMetrics();
  return true;
}

// Markdown HUD rows (spec 5.3). The host lays the rows out; the page only turns runs into spans.
const HUD_RUN_CLASSES = [
  ["bold", "hud-run-bold"],
  ["italic", "hud-run-italic"],
  ["strike", "hud-run-strike"],
  ["dim", "hud-run-dim"],
  ["underline", "hud-run-underline"],
];

function buildHudRun(run) {
  const span = document.createElement("span");
  span.className = [
    "hud-run",
    ...HUD_RUN_CLASSES.filter(([key]) => run[key] === true).map(([, name]) => name),
  ].join(" ");
  if (typeof run.link === "string") {
    span.classList.add("hud-link");
    span.dataset.link = run.link;
  }
  span.textContent = run.text;
  return span;
}

// The host indents every row after the first by the spinner gutter, so only the first row gets the
// glyph and its space, as buildHudRow does for a plain row. The space is a span of its own: a row is
// a flex box, which drops a text node that holds only white space.
function buildHudMarkdownBlock(rows, glyph, cell) {
  const block = document.createElement("div");
  block.className = "hud-block";
  rows.forEach((row, index) => {
    const line = document.createElement("div");
    line.className = "hud-row";
    line.style.height = `${cell.height}px`;
    line.style.lineHeight = `${cell.height}px`;
    if (index === 0 && glyph !== null) {
      const spinner = document.createElement("span");
      spinner.className = "hud-spinner";
      spinner.textContent = glyph;
      const gap = document.createElement("span");
      gap.textContent = " ";
      line.append(spinner, gap);
    }
    for (const run of Array.isArray(row.runs) ? row.runs : []) line.appendChild(buildHudRun(run));
    block.appendChild(line);
  });
  return block;
}

// Ctrl+click stands in for upstream's command-click (HudLinkClick.swift). The panel keeps
// pointer-events: none, so hit testing never finds it: the panel and its links are found by rectangle.
// As upstream, the press is consumed with its release anywhere over the panel, link or not.
let hudPressState = null; // "down" until the release, then "up" until the click that follows it
let hudPointer = null; // the last pointer position, for the cursor when only Ctrl changes

function insideRect(rect, x, y) {
  return x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom;
}

// the layer is the panel itself, so its rectangle is the panel's
function hudPanelContains(x, y) {
  return hudLayerEl !== null && insideRect(hudLayerEl.getBoundingClientRect(), x, y);
}

function hudLinkAt(x, y) {
  if (!hudLayerEl) return null;
  for (const span of hudLayerEl.querySelectorAll(".hud-link")) {
    for (const rect of span.getClientRects()) if (insideRect(rect, x, y)) return span.dataset.link;
  }
  return null;
}

function isHudLinkChord(ev) {
  return ev.ctrlKey && !ev.shiftKey && !ev.altKey && !ev.metaKey;
}

// a question drawn above the HUD and the search bar keep their own clicks
function hudOwnsPointAt(target, x, y) {
  return (
    target instanceof Element &&
    target.closest(".ask-layer, #search-bar") === null &&
    hudPanelContains(x, y)
  );
}

function consumeHudEvent(ev) {
  ev.preventDefault();
  ev.stopPropagation();
}

function onHudPointerDown(ev) {
  hudPressState = null;
  if (ev.button !== 0 || !isHudLinkChord(ev) || !hudOwnsPointAt(ev.target, ev.clientX, ev.clientY)) return;
  consumeHudEvent(ev);
  hudPressState = "down";
  const url = hudLinkAt(ev.clientX, ev.clientY);
  if (url) post({ type: "hudLink", url });
}

function onHudPointerUp(ev) {
  if (hudPressState !== "down") return;
  consumeHudEvent(ev);
  hudPressState = "up";
}

function onHudClick(ev) {
  if (hudPressState !== "up") return;
  consumeHudEvent(ev);
  hudPressState = null;
}

function setHudLinkHover(on) {
  document.documentElement.classList.toggle("hud-link-hover", on);
}

function updateHudLinkHover(chordHeld) {
  if (!chordHeld || !hudLayerEl || hudPointer === null) {
    setHudLinkHover(false);
    return;
  }
  const { x, y } = hudPointer;
  setHudLinkHover(hudOwnsPointAt(document.elementFromPoint(x, y), x, y) && hudLinkAt(x, y) !== null);
}

document.addEventListener("pointerdown", onHudPointerDown, true);
document.addEventListener("pointerup", onHudPointerUp, true);
document.addEventListener("pointercancel", () => { hudPressState = null; }, true);
document.addEventListener("click", onHudClick, true);
document.addEventListener(
  "pointermove",
  (ev) => {
    hudPointer = { x: ev.clientX, y: ev.clientY };
    updateHudLinkHover(isHudLinkChord(ev));
  },
  { capture: true, passive: true },
);
document.addEventListener(
  "keydown",
  (ev) => {
    if (ev.key === "Control") updateHudLinkHover(isHudLinkChord(ev));
  },
  true,
);
document.addEventListener(
  "keyup",
  (ev) => {
    if (ev.key === "Control") updateHudLinkHover(false);
  },
  true,
);
window.addEventListener("blur", () => setHudLinkHover(false));

// upstream SessionSwitcher's key monitor: Ctrl+Tab and Ctrl+Shift+Tab are always the switcher's
// (keymap.conf cannot bind them), Esc only while a switch is in progress
function routeSwitcherKey(paneId, ev) {
  if (ev.key === "Tab" && ev.ctrlKey) {
    switcherActive = true;
    post({ type: "switcher", paneId, key: ev.shiftKey ? "reverseTab" : "tab" });
    return true;
  }
  if (ev.key === "Escape" && switcherActive) {
    switcherActive = false;
    post({ type: "switcher", paneId, key: "escape" });
    return true;
  }
  return false;
}

// releasing Ctrl commits the switch; on the document, so it is seen whichever pane has the keys
document.addEventListener(
  "keyup",
  (ev) => {
    if (ev.key !== "Control" || !switcherActive) return;
    switcherActive = false;
    post({ type: "switcher", key: "controlReleased" });
  },
  true,
);

function samePhysicalChord(left, right) {
  return (
    left.ctrl === right.ctrl &&
    left.alt === right.alt &&
    left.shift === right.shift &&
    left.key === right.key
  );
}

function isChordPrefix(prefix, chords) {
  if (prefix.length > chords.length) return false;
  return prefix.every((chord, index) =>
    samePhysicalChord(chord, chords[index]),
  );
}

function resetCustomLeader() {
  armedCustomChords = [];
  if (customLeaderTimer !== null) clearTimeout(customLeaderTimer);
  customLeaderTimer = null;
}

function armCustomLeader(chords) {
  resetCustomLeader();
  armedCustomChords = chords;
  customLeaderTimer = setTimeout(resetCustomLeader, customLeaderTimeoutMs);
}

function processCustomChord(paneId, chord, allowRootRetry) {
  const candidate = [...armedCustomChords, chord];
  const exact = customHotkeyBindings.find(
    (binding) =>
      binding.chords.length === candidate.length &&
      isChordPrefix(candidate, binding.chords),
  );
  if (exact) {
    resetCustomLeader();
    post({
      type: "customHotkey",
      paneId,
      generation: hotkeyGeneration,
      customCommandId: exact.commandId,
      ctrl: chord.ctrl,
      alt: chord.alt,
      shift: chord.shift,
      key: chord.key,
    });
    return true;
  }

  if (
    customHotkeyBindings.some(
      (binding) =>
        candidate.length < binding.chords.length &&
        isChordPrefix(candidate, binding.chords),
    )
  ) {
    armCustomLeader(candidate);
    return true;
  }

  const wasArmed = armedCustomChords.length > 0;
  resetCustomLeader();
  return wasArmed && allowRootRetry
    ? processCustomChord(paneId, chord, false)
    : false;
}

function validatePhysicalChord(chord) {
  return (
    chord &&
    typeof chord === "object" &&
    typeof chord.ctrl === "boolean" &&
    typeof chord.alt === "boolean" &&
    typeof chord.shift === "boolean" &&
    typeof chord.key === "string" &&
    chord.key.length > 0
  );
}

function setHotkeyManifest(manifest) {
  if (!manifest || typeof manifest !== "object") return false;
  if (!Number.isSafeInteger(manifest.generation) || manifest.generation < 0)
    return false;
  if (manifest.generation < hotkeyGeneration) return false;
  if (
    !Number.isSafeInteger(manifest.leaderTimeoutMs) ||
    manifest.leaderTimeoutMs <= 0 ||
    !Array.isArray(manifest.builtinSingles) ||
    !manifest.builtinSingles.every(validatePhysicalChord) ||
    (manifest.zoomPassthroughSingles !== undefined &&
      (!Array.isArray(manifest.zoomPassthroughSingles) ||
        !manifest.zoomPassthroughSingles.every(validatePhysicalChord))) ||
    (manifest.dashboardToggleSingles !== undefined &&
      (!Array.isArray(manifest.dashboardToggleSingles) ||
        !manifest.dashboardToggleSingles.every(validatePhysicalChord))) ||
    !Array.isArray(manifest.customBindings)
  )
    return false;

  const customBindings = [];
  for (const binding of manifest.customBindings) {
    if (
      !binding ||
      typeof binding.commandId !== "string" ||
      binding.commandId.length === 0 ||
      !Array.isArray(binding.chords) ||
      binding.chords.length === 0 ||
      !binding.chords.every(validatePhysicalChord)
    )
      return false;
    customBindings.push({
      commandId: binding.commandId,
      chords: binding.chords.map((chord) => ({ ...chord })),
    });
  }

  resetCustomLeader();
  hotkeyGeneration = manifest.generation;
  customLeaderTimeoutMs = manifest.leaderTimeoutMs;
  interceptChords = manifest.builtinSingles.map((chord) => ({ ...chord }));
  zoomPassthroughChords = (manifest.zoomPassthroughSingles || []).map(
    (chord) => ({ ...chord }),
  );
  dashboardToggleChords = (manifest.dashboardToggleSingles || []).map(
    (chord) => ({ ...chord }),
  );
  customHotkeyBindings = customBindings;
  return true;
}

const post = (msg) => window.chrome.webview.postMessage(msg);

document.addEventListener("keydown", onDashboardKeyDown, true);
container.addEventListener("pointerdown", onDashboardPointerDown, true);
container.addEventListener("click", onDashboardClick, true);

if (searchInputEl) {
  searchInputEl.addEventListener("input", () => runSearchFromBar("next", true));
  searchInputEl.addEventListener("keydown", (ev) => {
    if (ev.key === "Escape") {
      const paneId = searchBarEl ? searchBarEl.dataset.paneId : null;
      if (paneId && closeSearch(paneId, true))
        post({ type: "searchClosed", paneId });
      ev.preventDefault();
      ev.stopPropagation();
      return;
    }
    if (ev.key === "Enter") {
      runSearchFromBar(ev.shiftKey ? "prev" : "next", false);
      ev.preventDefault();
      ev.stopPropagation();
    }
  });
}
if (searchPreviousEl)
  searchPreviousEl.addEventListener("click", () =>
    runSearchFromBar("prev", false),
  );
if (searchNextEl)
  searchNextEl.addEventListener("click", () => runSearchFromBar("next", false));
if (searchCloseEl) {
  searchCloseEl.addEventListener("click", () => {
    const paneId = searchBarEl ? searchBarEl.dataset.paneId : null;
    if (paneId && closeSearch(paneId, true))
      post({ type: "searchClosed", paneId });
  });
}

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
  hudGeometryObserver.observe(el);

  // a pane of a themed workspace opens in that theme, so it never shows the global one first
  const themeOverride = normalizePaneTheme(opts && opts.theme ? opts.theme : null);
  const term = new Terminal(
    buildTerminalOptions(
      paneSettings({ themeOverride }, rendererSettings),
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
    sessionFontSize: term.options.fontSize,
    overlayBackgroundColor: null,
    themeOverride,
    lastSearch: { index: -1, count: 0 },
    appliedSearch: { needle: "", selected: null },
    el,
    // The PowerShell integration's readline marker (OSC 7777), heard only in a pane the host
    // started with that integration. readline: PSReadLine reads input now; readlinePaste: the key
    // that runs its Paste function. rewrittenKeys: event.code of a pressed key the page sent in
    // its own form → the release to send for it ("" for none).
    readlineListening: !!(opts && opts.readlineMarker),
    readline: false,
    readlinePaste: null,
    rewrittenKeys: new Map(),
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

  // win32-input-mode decides the encoding only while both the pane option (off while a program
  // has kitty keyboard flags pushed) and the program's DECSET 9001 are on
  const win32Encodes = () =>
    !!term.options.vtExtensions?.win32InputMode && !!term.modes.win32InputMode;

  // While PSReadLine reads input, a paste goes to its Paste function: it reads the clipboard
  // itself and inserts the text as one edit, so one Ctrl+Z undoes the whole paste and a newline
  // does not run the line. Typed in by the app, each character would be its own undo step.
  // code is the pressed key, whose release is then sent in the same form (or not at all).
  const sendReadlinePaste = (code) => {
    if (!pane.readline) return false;
    if (pane.readlinePaste === "shift-insert") {
      pane.term.input("\x1b[2;2~", true);
      if (code) pane.rewrittenKeys.set(code, "");
      return true;
    }
    if (pane.readlinePaste === "ctrl-v" && win32Encodes()) {
      pane.term.input(win32CtrlLetter("KeyV", true), true);
      if (code) pane.rewrittenKeys.set(code, win32CtrlLetter("KeyV", false));
      else pane.term.input(win32CtrlLetter("KeyV", false), true);
      return true;
    }
    return false;
  };

  // xterm sends a release for a key whose press the handler consumed; for a key the page sent in
  // its own form, the page sends the matching release instead
  const releaseRewrittenKey = (ev) => {
    if (!pane.rewrittenKeys.has(ev.code)) return true;
    const release = pane.rewrittenKeys.get(ev.code);
    pane.rewrittenKeys.delete(ev.code);
    if (release) pane.term.input(release, true);
    if (ev.preventDefault) ev.preventDefault();
    return false;
  };
  // a key released while the page had no focus never reports it
  term.textarea?.addEventListener("blur", () => pane.rewrittenKeys.clear());

  // While PSReadLine reads input, Ctrl and a letter key on a non-Latin layout go as the record
  // with the Latin letter (see win32CtrlLetter), so Ctrl+Z on Russian runs Undo, not Ctrl+я.
  // Ctrl+Shift is left alone: xterm's record already matches a Ctrl+Shift binding there.
  const rewriteCtrlLetter = (ev) => {
    if (!pane.readline || !win32Encodes()) return false;
    if (!ev.ctrlKey || ev.shiftKey || ev.altKey || ev.metaKey) return false;
    if (!/^Key[A-Z]$/.test(ev.code || "")) return false;
    if (typeof ev.key !== "string" || ev.key.length !== 1 || /^[a-zA-Z]$/.test(ev.key)) return false;
    pane.term.input(win32CtrlLetter(ev.code, true), true);
    pane.rewrittenKeys.set(ev.code, win32CtrlLetter(ev.code, false));
    return true;
  };

  // right-click paste is controlled by page-global renderer settings and can be toggled at runtime.
  el.addEventListener(
    "contextmenu",
    (ev) => {
      if (!rendererSettings.rightClickPaste) return;
      if (ev.preventDefault) ev.preventDefault();
      if (!sendReadlinePaste(null)) post({ type: "pasteRequested", paneId });
    },
    true,
  );

  // Clipboard chords, after the keymap so a user binding still wins. The browser clipboard API
  // cannot serve a copy here: writeText throws NotAllowedError without a user gesture, and the
  // engine's key event is not one, so the host does the write the way it already does the read
  // for right-click paste. Ctrl+C sends 0x03 when there is nothing to copy, while the other copy
  // chords are consumed.
  const routeClipboardChord = (ev) => {
    const insert = ev.key === "Insert";
    const letter = /^[a-zA-Z]$/.test(ev.key)
      ? ev.key.toLowerCase()
      : !ev.altKey && /^Key[CV]$/.test(ev.code || "")
        ? ev.code.slice(3).toLowerCase()
        : "";
    const isCopy = (ev.ctrlKey && letter === "c") || (ev.ctrlKey && insert);
    const isPaste = (ev.ctrlKey && letter === "v") || (ev.shiftKey && insert);
    if (!isCopy && !isPaste) return false;
    if (isPaste) {
      if (!sendReadlinePaste(ev.code)) post({ type: "pasteRequested", paneId });
      return true;
    }
    const selection = pane.term.getSelection();
    if (!selection) {
      if (letter === "c" && ev.ctrlKey && !ev.shiftKey && !ev.altKey
          && !/^[a-zA-Z]$/.test(ev.key)) {
        // Let xterm encode the physical Ctrl+C for the active keyboard protocol.
        Object.defineProperty(ev, "key", { value: "c" });
      }
      return ev.ctrlKey && !ev.altKey && (insert || (letter === "c" && ev.shiftKey));
    }
    post({ type: "copyRequested", paneId, text: selection });
    return true;
  };

  const modifierOnlyKeys = new Set([
    "Control", "Shift", "Alt", "Meta", "AltGraph", "CapsLock", "OS",
    "NumLock", "ScrollLock", "Fn", "FnLock", "Symbol", "SymbolLock",
    "Super", "Hyper",
  ]);
  const forwardUserKeystroke = (ev) => {
    if (modifierOnlyKeys.has(ev.key)) return true;
    post({
      type: "userKeystroke", paneId, code: ev.code, key: ev.key,
      ctrl: ev.ctrlKey, alt: ev.altKey, shift: ev.shiftKey, meta: ev.metaKey,
    });
    return true;
  };

  // app hotkeys never reach the shell: a keydown matching the host-pushed chord table goes
  // back as a chord message instead (the host's KeybindMatcher dispatches it)
  term.attachCustomKeyEventHandler((ev) => {
    if (ev.type === "keyup") return releaseRewrittenKey(ev);
    if (ev.type !== "keydown") return true;
    // a new press replaces any release left over from an earlier press of the same key
    pane.rewrittenKeys.delete(ev.code);
    if (routeSwitcherKey(paneId, ev)) {
      if (ev.preventDefault) ev.preventDefault();
      return false;
    }
    if (ev.key === "Escape" && armedCustomChords.length > 0) {
      resetCustomLeader();
      if (ev.preventDefault) ev.preventDefault();
      return false;
    }
    const key = keymapKey(ev);
    // Insert is not in the keymap grammar, so keymapKey drops it: route the clipboard chords
    // before that. A key the keymap can express is routed after it, so a user binding still wins.
    if (key === null) {
      if (!routeClipboardChord(ev)) return forwardUserKeystroke(ev);
      if (ev.preventDefault) ev.preventDefault();
      return false;
    }
    const chord = {
      ctrl: ev.ctrlKey,
      alt: ev.altKey,
      shift: ev.shiftKey,
      key,
    };
    const customWasArmed = armedCustomChords.length > 0;
    let handled = customWasArmed
      ? processCustomChord(paneId, chord, true)
      : false;
    const builtinHit =
      !handled &&
      !(
        visibleLayout?.type === "zoomed" &&
        zoomPassthroughChords.some((c) => samePhysicalChord(c, chord))
      ) &&
      interceptChords.some((c) => samePhysicalChord(c, chord));
    if (builtinHit) {
      resetCustomLeader();
      post({
        type: "chord",
        paneId,
        generation: hotkeyGeneration,
        ctrl: ev.ctrlKey,
        alt: ev.altKey,
        shift: ev.shiftKey,
        key,
      });
      handled = true;
    } else if (!handled && !customWasArmed) {
      handled = processCustomChord(paneId, chord, true);
    }
    if (!handled) handled = routeClipboardChord(ev);
    if (!handled && rewriteCtrlLetter(ev)) {
      forwardUserKeystroke(ev);
      if (ev.preventDefault) ev.preventDefault();
      return false;
    }
    if (!handled) {
      if (ev.ctrlKey && !ev.altKey && key !== normalizeKey(ev.key)
          && /^[a-z]$/.test(key || "")) {
        Object.defineProperty(ev, "key", { value: key });
      }
      return forwardUserKeystroke(ev);
    }
    if (ev.preventDefault) ev.preventDefault();
    return false;
  });

  // user input → pty (xterm encodes Enter as CR, arrows as VT sequences, and — once an app
  // pushes the kitty keyboard protocol — keys in their CSI u form; see vtExtensions above)
  term.onData((data) => post({ type: "data", paneId, data }));
  term.onBinary((data) => post({ type: "binary", paneId, data }));

  // Any user/programmatic focus transition on a pane is surfaced to host orchestration.
  el.addEventListener("focusin", () => {
    if (programmaticFocusPaneId !== paneId) emitPaneFocus(paneId);
  });

  // only a pointer counts as a click: a WebView2 focus restore and a host refocus also fire focusin
  el.addEventListener(
    "pointerdown",
    () => {
      if (askCoversLayer(paneId)) releaseAskLayer(paneId);
    },
    true,
  );

  // The pushed kitty flags live on the engine's core service; 0 means nothing is pushed.
  const kittyFlags = () => term._core?.coreService?.kittyKeyboard?.flags ?? 0;

  // An app that pushes the kitty keyboard protocol has asked for CSI u encoding, but the engine
  // checks win32-input-mode first and answers from event.code, so Shift+Enter would leave as a
  // win32 key record the app does not read. Claude Code enables both (DECSET 9001, then CSI > 5 u)
  // and loses Shift+Enter that way. Suspending mode 9001 while a kitty request is in effect keeps
  // the encoding the app actually asked for; popping the stack restores it.
  // Returning false leaves the sequences to xterm's own handlers.
  const syncKeyboardPrecedence = () => {
    const kittyRequested = kittyFlags() > 0;
    if (kittyRequested === pane.kittySuspendedWin32) return;
    pane.kittySuspendedWin32 = kittyRequested;
    term.options.vtExtensions = {
      kittyKeyboard: true,
      win32InputMode: !kittyRequested,
    };
  };
  term.parser.registerCsiHandler({ prefix: ">", final: "u" }, () => {
    queueMicrotask(syncKeyboardPrecedence);
    return false;
  });
  term.parser.registerCsiHandler({ prefix: "<", final: "u" }, () => {
    queueMicrotask(syncKeyboardPrecedence);
    return false;
  });

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

  // OSC 7777 — integration.ps1's private readline marker:
  // "agterm-readline=1[;paste=shift-insert|ctrl-v]" before PSReadLine reads a line,
  // "agterm-readline=0" once it returns. Any other payload changes nothing.
  if (pane.readlineListening) {
    term.parser.registerOscHandler(7777, (data) => {
      const m = /^agterm-readline=([01])(?:;paste=(shift-insert|ctrl-v))?$/.exec(data);
      if (m) {
        pane.readline = m[1] === "1";
        pane.readlinePaste = pane.readline && m[2] ? m[2] : null;
      }
      return true;
    });
  }

  // track search hits so a read-back after find can report "N of M"
  searchAddon.onDidChangeResults((r) => {
    pane.lastSearch = {
      index: r ? r.resultIndex : -1,
      count: r ? r.resultCount : 0,
    };
    queueMicrotask(() => {
      if (
        !searchBarEl ||
        searchBarEl.hidden ||
        searchBarEl.dataset.paneId !== paneId ||
        !pane.appliedSearch.needle
      )
        return;
      const needle = pane.appliedSearch.needle;
      const count = pane.lastSearch.count;
      if (pane.lastReportedSearch?.needle === needle && pane.lastReportedSearch.count === count)
        return;
      pane.lastReportedSearch = { needle, count };
      post({ type: "searchResultsChanged", paneId, needle, count });
    });
  });

  fitAddon.fit();
  const size = { cols: term.cols, rows: term.rows };

  if (getVisiblePaneIds().length === 0) {
    setVisibleLayout({ type: "single", paneIds: [paneId], paneId });
    focusPane(paneId, true);
  } else {
    el.style.display = "none";
  }
  return size;
}

function showPane(paneId) {
  if (!panes.has(paneId)) return null;
  return applySinglePresentation(paneId, true, true);
}

function closePane(paneId) {
  const pane = panes.get(paneId);
  if (!pane) return null;
  closeSearch(paneId, false);

  const paneIsVisible = getVisiblePaneIds().includes(paneId);
  if (paneIsVisible) applyEmptyPresentation();

  panes.delete(paneId);
  pane.term.dispose();
  hudGeometryObserver.unobserve(pane.el);
  if (hudLayerEl && pane.el.contains(hudLayerEl)) removeHud();
  pane.el.remove();

  if (!paneIsVisible && visiblePaneId === paneId) visiblePaneId = null;

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
    applyEmptyPresentation();
    highestAcceptedLayoutGeneration = generation;
    return true;
  }

  if (layout.type === "single") {
    if (typeof layout.paneId !== "string" || typeof layout.title !== "string")
      return false;
    if (!panes.has(layout.paneId)) return false;
    const shouldFocus = visibleFocusedPaneId() !== layout.paneId;
    if (!applySinglePresentation(layout.paneId, true, shouldFocus))
      return false;
    highestAcceptedLayoutGeneration = generation;
    return true;
  }

  if (layout.type === "zoomed") {
    if (!hasOnlyKeys(layout, ["type", "generation", "paneId"])) return false;
    const paneId = normalizeKnownPaneId(layout.paneId);
    if (!paneId) return false;
    const shouldFocus = visibleFocusedPaneId() !== paneId;
    if (!applySinglePresentation(paneId, true, shouldFocus)) return false;
    setVisibleLayout({ type: "zoomed", paneIds: [paneId], paneId });
    highestAcceptedLayoutGeneration = generation;
    return true;
  }

  if (layout.type === "dashboard") {
    const dashboard = validateDashboardLayout(layout);
    if (!dashboard || !applyDashboardPresentation(dashboard)) return false;
    highestAcceptedLayoutGeneration = generation;
    return true;
  }

  if (layout.type === "split") {
    const validSplitLayout = validateSplitLayout(layout);
    if (!validSplitLayout) return false;

    const shouldFocus =
      visibleFocusedPaneId() !== validSplitLayout.focusedPaneId;
    if (!applySplitPresentation(validSplitLayout, true, shouldFocus))
      return false;
    highestAcceptedLayoutGeneration = generation;
    return true;
  }

  if (layout.type === "layered") {
    const validLayeredLayout = validateLayeredLayout(layout);
    if (!validLayeredLayout) return false;
    const shouldFocusLayer =
      visibleFocusedPaneId() !== validLayeredLayout.layerPaneId;
    if (!applyLayeredPresentation(validLayeredLayout, shouldFocusLayer))
      return false;
    highestAcceptedLayoutGeneration = generation;
    return true;
  }

  return false;
}

function fitAndReport(paneId) {
  const pane = panes.get(paneId);
  if (!pane) return null;
  pane.fitAddon.fit();
  scheduleHudRefresh();
  post({ type: "resize", paneId, cols: pane.term.cols, rows: pane.term.rows });
  return { cols: pane.term.cols, rows: pane.term.rows };
}

function fitAndReportPaneIds(paneIds) {
  const seen = new Set();
  for (const paneId of paneIds) {
    if (seen.has(paneId)) continue;
    seen.add(paneId);
    fitAndReport(paneId);
  }
}

function fitAndReportVisiblePanes() {
  fitAndReportPaneIds(getVisiblePaneIds());
}

window.addEventListener("resize", () => {
  fitAndReportVisiblePanes();
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
      if (typeof msg.fontSize === "number") {
        pane.sessionFontSize = msg.fontSize;
        if (
          visibleLayout?.type !== "dashboard" ||
          !visibleLayout.paneIds.includes(msg.paneId) ||
          visibleLayout.fontSize === null
        ) {
          pane.term.options.fontSize = msg.fontSize;
        }
      }
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
  openSearch,
  closeSearch,
  applySearch,
  hideSearch,
  getSearchState,
  applyRendererSettings,
  applyPaneThemes,
  setTerminalBackground,
  getRendererSettings,
  getRendererCapabilities,
  applyLayout,
  applyAsk,
  applyHud,
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
  // the saved output for a relaunch: the normal screen only, and no mode switches, so a full-screen
  // app or a pushed keyboard mode from the old shell cannot leak into the new one
  serializeForRestore: (paneId) =>
    withPane(paneId, (p) =>
      p.serializeAddon.serialize({ excludeAltBuffer: true, excludeModes: true }),
    ),
  search: (paneId, needle, dir) =>
    withPane(paneId, (p) => {
      const opts = searchOptions();
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
      p.sessionFontSize = px;
      if (
        visibleLayout?.type !== "dashboard" ||
        !visibleLayout.paneIds.includes(paneId) ||
        visibleLayout.fontSize === null
      ) {
        p.term.options.fontSize = px;
      }
      fitAndReport(paneId);
      return p.sessionFontSize;
    }),
  getFontSize: (paneId) => withPane(paneId, (p) => p.term.options.fontSize),
  focus: (paneId) => focusPane(paneId, true),
  // page-global hotkey table (host-invoked so a following dispatch cannot outrun it)
  setChords: (items) => {
    resetCustomLeader();
    hotkeyGeneration += 1;
    interceptChords = items || [];
    customHotkeyBindings = [];
    zoomPassthroughChords = [];
    dashboardToggleChords = [];
    return interceptChords.length;
  },
  setHotkeyManifest,
  setSwitcherActive: (active) => {
    switcherActive = !!active;
    return true;
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
      case "Escape":
        return 27;
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
  // test-only: the pane's real textarea, so a test can dispatch an event it built itself
  textareaFor: (paneId) => withPane(paneId, (p) => p.term.textarea),
  // test-only: does win32-input-mode still decide the encoding? mode 9001 stays set while the
  // kitty protocol is pushed, so the option is what the engine actually consults.
  win32InputModeEncodes: (paneId) =>
    withPane(paneId, (p) => !!p.term.options.vtExtensions?.win32InputMode
      && !!p.term.modes.win32InputMode),
  // test-only: is DECSET 9001 set? the app sets it; whether it decides the encoding is
  // win32InputModeEncodes above
  win32InputModeActive: (paneId) =>
    withPane(paneId, (p) => !!p.term.modes.win32InputMode),
  // test-only: the readline marker state of a pane
  readlineState: (paneId) =>
    withPane(paneId, (p) => ({
      listening: p.readlineListening, reading: p.readline, paste: p.readlinePaste,
    })),
  // test-only: a synthetic keyup on the document (the Ctrl release the switcher waits for)
  dispatchKeyUp: (key) => {
    document.dispatchEvent(new KeyboardEvent("keyup", { key }));
    return true;
  },
  // test-only: run a synthetic keydown through the real xterm key path (chord interception).
  // event.code matters as much as event.key: win32-input-mode encodes from code and is consulted
  // before the kitty protocol, so an event without one takes a path no hardware key takes.
  dispatchKey: (paneId, opts) =>
    withPane(paneId, (p) => {
      const ev = new KeyboardEvent("keydown", {
        key: opts.key,
        code: opts.code || syntheticCodeFromKey(opts.key),
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
