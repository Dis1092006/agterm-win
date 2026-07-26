// The JS half of the agterm terminal bridge. Protocol (see XtermTerminal.cs, the C# half):
//   host → page  (PostWebMessageAsJson):  {type:'output', data:<base64 VT bytes>}
//                                         {type:'options', fontSize?}
//   page → host  (chrome.webview.postMessage): {type:'ready', cols, rows}
//                                              {type:'data', data:<string>}      user keystrokes
//                                              {type:'resize', cols, rows}       after a fit
//                                              {type:'title', title}             OSC 0/1/2
//                                              {type:'cwd', uri}                 OSC 7
//   host-invoked reads (ExecuteScriptAsync, result = JSON):  agterm.text(all, lines),
//     agterm.selection(), agterm.selectAll(), agterm.serialize(), agterm.fit(),
//     agterm.search(needle, dir), agterm.searchClose(), agterm.setFontSize(px), agterm.getFontSize()
'use strict';

const term = new Terminal({
    allowProposedApi: true,
    cursorBlink: true,
    fontFamily: 'Cascadia Mono, Consolas, monospace',
    fontSize: 14,
    scrollback: 10000,
});
const fitAddon = new FitAddon.FitAddon();
const searchAddon = new SearchAddon.SearchAddon();
const serializeAddon = new SerializeAddon.SerializeAddon();
term.loadAddon(fitAddon);
term.loadAddon(searchAddon);
term.loadAddon(serializeAddon);

const post = (msg) => window.chrome.webview.postMessage(msg);

term.open(document.getElementById('terminal'));

// user input → pty (xterm already encodes Enter as CR, arrows as VT sequences, etc.)
term.onData((data) => post({ type: 'data', data }));
term.onBinary((data) => post({ type: 'binary', data }));

// OSC 0/1/2 → session title (manual VT parsing is banned; xterm's handler is the source)
term.onTitleChange((title) => post({ type: 'title', title }));

// OSC 7 → session cwd; payload is a file:// URI (or a bare path from some shells) — forwarded
// verbatim, parsed on the C# side. Return false so xterm's own OSC 7 handling (if any) still runs.
term.parser.registerOscHandler(7, (uri) => {
    post({ type: 'cwd', uri });
    return false;
});

// OSC 9;9 — the ConEmu / Windows Terminal cwd convention (`ESC]9;9;"<path>"ESC\`); the handler
// for identifier 9 receives everything after "9;", so a cwd report starts with another "9;".
term.parser.registerOscHandler(9, (data) => {
    if (data.startsWith('9;')) post({ type: 'cwd', uri: data.slice(2) });
    return false;
});

// track search hits so a read-back after find can report "N of M"
let lastSearch = { index: -1, count: 0 };
searchAddon.onDidChangeResults((r) => {
    lastSearch = { index: r ? r.resultIndex : -1, count: r ? r.resultCount : 0 };
});

const b64ToBytes = (b64) => {
    const bin = atob(b64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
    return bytes;
};

window.chrome.webview.addEventListener('message', (e) => {
    const msg = e.data;
    if (!msg || !msg.type) return;
    switch (msg.type) {
        case 'output':
            term.write(b64ToBytes(msg.data));
            break;
        case 'options':
            if (typeof msg.fontSize === 'number') term.options.fontSize = msg.fontSize;
            fitAndReport();
            break;
    }
});

function fitAndReport() {
    fitAddon.fit();
    post({ type: 'resize', cols: term.cols, rows: term.rows });
    return { cols: term.cols, rows: term.rows };
}
window.addEventListener('resize', fitAndReport);

// plain-text buffer read (session.text): default = the visible screen, all = screen + scrollback,
// lines = the last N lines of screen + scrollback. Lines are right-trimmed like the upstream read.
function bufferText(all, lines) {
    const buf = term.buffer.active;
    let start, end;
    if (lines != null && lines > 0) {
        end = buf.length;
        start = Math.max(0, end - lines);
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
        out.push(line ? line.translateToString(true) : '');
    }
    // drop trailing blank rows (an idle screen read otherwise ends in dozens of empty lines)
    while (out.length > 0 && out[out.length - 1] === '') out.pop();
    return out.join('\n');
}

window.agterm = {
    fit: fitAndReport,
    text: (all, lines) => bufferText(all, lines),
    selection: () => term.getSelection(),
    selectAll: () => { term.selectAll(); return true; },
    serialize: () => serializeAddon.serialize(),
    search: (needle, dir) => {
        const opts = { decorations: { matchOverviewRuler: '#888888', activeMatchColorOverviewRuler: '#ffffff' } };
        const found = dir === 'prev' ? searchAddon.findPrevious(needle, opts)
                                     : searchAddon.findNext(needle, opts);
        return { found, index: lastSearch.index, count: lastSearch.count };
    },
    searchClose: () => { searchAddon.clearDecorations(); term.clearSelection(); return true; },
    setFontSize: (px) => { term.options.fontSize = px; fitAndReport(); return term.options.fontSize; },
    getFontSize: () => term.options.fontSize,
    focus: () => { term.focus(); return true; },
};

term.focus();
post({ type: 'ready', cols: term.cols, rows: term.rows });
