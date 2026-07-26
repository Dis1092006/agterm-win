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
//   host-invoked (ExecuteScriptAsync, result = JSON): agterm.openPane(paneId, {fontSize}) → {cols,rows},
//     agterm.showPane(paneId), agterm.closePane(paneId), and the per-pane reads/writes:
//     text/selection/selectAll/serialize/fit/search/searchClose/paste/setFontSize/getFontSize/focus —
//     each takes paneId first; every call on an unknown paneId returns null.
// The FIRST opened pane is shown automatically; window resizes refit only the visible pane.
'use strict';

const panes = new Map(); // paneId → {term, fitAddon, searchAddon, serializeAddon, lastSearch, el}
let visiblePaneId = null;
const container = document.getElementById('panes');

const post = (msg) => window.chrome.webview.postMessage(msg);

const b64ToBytes = (b64) => {
    const bin = atob(b64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes;
};

function openPane(paneId, opts) {
    if (panes.has(paneId)) return null;
    const el = document.createElement('div');
    el.className = 'pane';
    el.style.display = 'none';
    container.appendChild(el);

    const term = new Terminal({
        allowProposedApi: true,
        cursorBlink: true,
        fontFamily: 'Cascadia Mono, Consolas, monospace',
        fontSize: (opts && typeof opts.fontSize === 'number') ? opts.fontSize : 14,
        scrollback: 10000,
    });
    const fitAddon = new FitAddon.FitAddon();
    const searchAddon = new SearchAddon.SearchAddon();
    const serializeAddon = new SerializeAddon.SerializeAddon();
    term.loadAddon(fitAddon);
    term.loadAddon(searchAddon);
    term.loadAddon(serializeAddon);

    const pane = { term, fitAddon, searchAddon, serializeAddon, lastSearch: { index: -1, count: 0 }, el };
    panes.set(paneId, pane);

    // measure hidden panes too: render into the (visible-sized) container while display:none is
    // only toggled after open — xterm needs a laid-out element, so flip on, open+fit, restore.
    el.style.display = 'block';
    term.open(el);

    // user input → pty (xterm already encodes Enter as CR, arrows as VT sequences, etc.)
    term.onData((data) => post({ type: 'data', paneId, data }));
    term.onBinary((data) => post({ type: 'binary', paneId, data }));

    // OSC 0/1/2 → session title (manual VT parsing is banned; xterm's handler is the source)
    term.onTitleChange((title) => post({ type: 'title', paneId, title }));

    // OSC 7 → session cwd; forwarded verbatim, parsed on the C# side. Return false so xterm's own
    // OSC 7 handling (if any) still runs.
    term.parser.registerOscHandler(7, (uri) => {
        post({ type: 'cwd', paneId, uri });
        return false;
    });

    // OSC 9;9 — the ConEmu / Windows Terminal cwd convention (`ESC]9;9;"<path>"ESC\`); the handler
    // for identifier 9 receives everything after "9;", so a cwd report starts with another "9;".
    term.parser.registerOscHandler(9, (data) => {
        if (data.startsWith('9;')) post({ type: 'cwd', paneId, uri: data.slice(2) });
        return false;
    });

    // track search hits so a read-back after find can report "N of M"
    searchAddon.onDidChangeResults((r) => {
        pane.lastSearch = { index: r ? r.resultIndex : -1, count: r ? r.resultCount : 0 };
    });

    fitAddon.fit();
    const size = { cols: term.cols, rows: term.rows };

    if (visiblePaneId === null) {
        visiblePaneId = paneId;
        term.focus();
    } else {
        el.style.display = 'none';
    }
    return size;
}

function showPane(paneId) {
    const pane = panes.get(paneId);
    if (!pane) return null;
    for (const [id, p] of panes) p.el.style.display = id === paneId ? 'block' : 'none';
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

function fitAndReport(paneId) {
    const pane = panes.get(paneId);
    if (!pane) return null;
    pane.fitAddon.fit();
    post({ type: 'resize', paneId, cols: pane.term.cols, rows: pane.term.rows });
    return { cols: pane.term.cols, rows: pane.term.rows };
}
window.addEventListener('resize', () => {
    if (visiblePaneId !== null) fitAndReport(visiblePaneId);
});

window.chrome.webview.addEventListener('message', (e) => {
    const msg = e.data;
    if (!msg || !msg.type) return;
    const pane = panes.get(msg.paneId);
    if (!pane) return;
    switch (msg.type) {
        case 'output':
            pane.term.write(b64ToBytes(msg.data));
            break;
        case 'options':
            if (typeof msg.fontSize === 'number') pane.term.options.fontSize = msg.fontSize;
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
        if (!line) { out.push(''); continue; }
        const text = line.translateToString(true);
        // a soft-wrapped row continues its predecessor: one logical line, not two
        if (line.isWrapped && out.length > 0) out[out.length - 1] += text;
        else out.push(text);
    }
    // drop trailing blank rows (an idle screen read otherwise ends in dozens of empty lines)
    while (out.length > 0 && out[out.length - 1] === '') out.pop();
    return (lines != null && lines > 0 ? out.slice(-lines) : out).join('\n');
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
    fit: (paneId) => fitAndReport(paneId),
    text: (paneId, all, lines) => withPane(paneId, (p) => bufferText(p.term, all, lines)),
    selection: (paneId) => withPane(paneId, (p) => p.term.getSelection()),
    selectAll: (paneId) => withPane(paneId, (p) => { p.term.selectAll(); return true; }),
    serialize: (paneId) => withPane(paneId, (p) => p.serializeAddon.serialize()),
    search: (paneId, needle, dir) => withPane(paneId, (p) => {
        const opts = { decorations: { matchOverviewRuler: '#888888', activeMatchColorOverviewRuler: '#ffffff' } };
        const found = dir === 'prev' ? p.searchAddon.findPrevious(needle, opts)
                                     : p.searchAddon.findNext(needle, opts);
        return { found, index: p.lastSearch.index, count: p.lastSearch.count };
    }),
    searchClose: (paneId) => withPane(paneId, (p) => { p.searchAddon.clearDecorations(); p.term.clearSelection(); return true; }),
    paste: (paneId, text) => withPane(paneId, (p) => { p.term.paste(text); return true; }),
    setFontSize: (paneId, px) => withPane(paneId, (p) => {
        p.term.options.fontSize = px;
        fitAndReport(paneId);
        return p.term.options.fontSize;
    }),
    getFontSize: (paneId) => withPane(paneId, (p) => p.term.options.fontSize),
    focus: (paneId) => withPane(paneId, (p) => { p.term.focus(); return true; }),
};

post({ type: 'ready' });
