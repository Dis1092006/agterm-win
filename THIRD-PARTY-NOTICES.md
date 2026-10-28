# Third-party notices

This repository contains material from the projects below. All three are under the MIT License.

## agterm

Source: https://github.com/umputun/agterm

Used in this repository:

- The control API: command names, arguments, results and error strings, the keymap grammar, the builtin action names and the settings and snapshot field names. `parity/surface.json` records them.
- Tests ported from agterm, in the `Ported` folders under `tests/`.
- The agent skill in `plugins/agterm/skills/agterm`, adapted to Windows.
- The `agterm` terminal theme in `src/AgTerm.Xterm/Themes/agterm`.

```
MIT License

Copyright (c) 2026 Umputun

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## xterm.js

Source: https://github.com/xtermjs/xterm.js

Used in this repository: `xterm.js`, `xterm.css`, `addon-fit.js`, `addon-search.js` and `addon-serialize.js` in `src/AgTerm.Xterm/Assets/vendor`. `MANIFEST.md` in that folder lists the versions.

```
Copyright (c) 2017-2019, The xterm.js authors (https://github.com/xtermjs/xterm.js)
Copyright (c) 2014-2016, SourceLair Private Company (https://www.sourcelair.com)
Copyright (c) 2012-2013, Christopher Jeffrey (https://github.com/chjj/)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

Two addons carry their own copyright line under the same license: `addon-fit` is Copyright (c) 2019, The xterm.js authors, and `addon-search` is Copyright (c) 2017, The xterm.js authors. `addon-serialize` has no notice of its own and is covered by the notice above.

## iTerm2-Color-Schemes

Source: https://github.com/mbadolato/iTerm2-Color-Schemes

Used in this repository: the Ghostty theme pack in `src/AgTerm.Xterm/Themes/ghostty`. `src/AgTerm.Xterm/Themes/SOURCE.md` names the archive it came from, and `src/AgTerm.Xterm/Themes/LICENSE` holds the license text, Copyright (c) 2011 to Present Mark Badolato.
