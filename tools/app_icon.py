#!/usr/bin/env python3
"""
app_icon.py: draw the application icons in `src/AgTerm.App/Assets`.

The icon is a window: a sidebar with three agent status dots (blocked, completed, active, in the
default status colors of `AppSettings`) and a pane with a cursor. Two palettes:

  AppIcon.ico               dark frame, light pane. The exe's own icon, and the window icon on a light taskbar.
  AppIcon-dark-taskbar.ico  light frame, dark pane. The window icon on a dark taskbar.

16, 20, 24 and 32 px each have their own drawing on a whole-pixel grid. Shrinking the 256 px drawing to
those sizes blurs the dots and the cursor. 40, 48, 64, 128 and 256 px are rendered from the 256 px drawing.

Usage:
    python -m pip install pillow resvg-py
    python tools/app_icon.py [--out-dir <dir>] [--png-dir <dir>]

`--png-dir` also writes every frame of both icons as a PNG, for checking pixels.
"""
from __future__ import annotations

import argparse
import io
import pathlib

import resvg_py
from PIL import Image

ORANGE = "#FF9500"   # AppSettings.DefaultBlockedStatusColorHex
GREEN = "#28CD41"    # AppSettings.DefaultCompletedStatusColorHex
LIGHT = "#DBD9E6"    # AppSettings.DefaultActiveStatusColorHex
DARK_GREY = "#2B2B30"
LIGHT_GREY = "#F4F4F7"

# The cursor takes the frame color. On the light frame the active dot is mid grey, because LIGHT
# disappears there.
PALETTES = {
    "AppIcon": dict(frame=DARK_GREY, pane=LIGHT_GREY, dots=(ORANGE, GREEN, LIGHT)),
    "AppIcon-dark-taskbar": dict(frame=LIGHT_GREY, pane=DARK_GREY, dots=(ORANGE, GREEN, "#8E8C9C")),
}

ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)
DEFAULT_OUT_DIR = pathlib.Path(__file__).resolve().parent.parent / "src" / "AgTerm.App" / "Assets"

# Per grid size: the frame (x, y, w, h, radius), the pane (x, y, w, h, radius), the dot column x,
# the three dot centers y, the dot radius and the cursor (x, y, w, h, radius). The 16 px grid draws
# the dots as squares, because a 2 px circle is not round.
GRIDS = {
    16: dict(frame=(0.5, 1.5, 15, 13, 2), pane=(6, 3, 8, 10, 1), dot_x=3, dot_y=(5, 8, 11), dot_r=1,
             cursor=(7, 10, 3, 2, 0), square_dots=True),
    20: dict(frame=(1, 3, 18, 14, 2.5), pane=(7, 5, 10, 10, 1), dot_x=4, dot_y=(7, 10, 13), dot_r=1.3,
             cursor=(9, 12, 3, 2, 0)),
    24: dict(frame=(1, 3, 22, 18, 3), pane=(9, 5, 12, 14, 1.5), dot_x=5, dot_y=(8, 12, 16), dot_r=1.6,
             cursor=(11, 15, 4, 2, 0)),
    32: dict(frame=(1, 4, 30, 24, 4), pane=(12, 7, 16, 18, 2), dot_x=6, dot_y=(10, 16, 22), dot_r=2,
             cursor=(14, 20, 6, 3, 0.5)),
    256: dict(frame=(12, 36, 232, 184, 32), pane=(92, 56, 132, 144, 16), dot_x=52, dot_y=(84, 128, 172),
              dot_r=15, cursor=(114, 160, 44, 20, 3)),
}


def rect(box, fill):
    x, y, w, h, r = box
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{fill}"/>'


def dot(g, cy, fill):
    x, r = g["dot_x"], g["dot_r"]
    if g.get("square_dots"):
        return rect((x - r, cy - r, 2 * r, 2 * r, 0), fill)
    return f'<circle cx="{x}" cy="{cy}" r="{r}" fill="{fill}"/>'


def svg(grid, palette):
    g = GRIDS[grid]
    body = (rect(g["frame"], palette["frame"]) + rect(g["pane"], palette["pane"])
            + "".join(dot(g, y, c) for y, c in zip(g["dot_y"], palette["dots"]))
            + rect(g["cursor"], palette["frame"]))
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{grid}" height="{grid}" '
            f'viewBox="0 0 {grid} {grid}">{body}</svg>')


def frame(size, palette):
    grid = size if size in GRIDS else 256
    png = resvg_py.svg_to_bytes(svg_string=svg(grid, palette), width=size, height=size)
    return Image.open(io.BytesIO(bytes(png))).convert("RGBA")


def write_icon(name, palette, out_dir, png_dir):
    frames = {s: frame(s, palette) for s in ICO_SIZES}
    out = out_dir / f"{name}.ico"
    # without append_images Pillow would shrink the 256 frame into every size
    frames[256].save(out, format="ICO", sizes=[(s, s) for s in ICO_SIZES],
                     append_images=[frames[s] for s in ICO_SIZES if s != 256])
    if png_dir:
        png_dir.mkdir(parents=True, exist_ok=True)
        for s, im in frames.items():
            im.save(png_dir / f"{name}-{s}.png")
    print(f"wrote {out} ({', '.join(map(str, ICO_SIZES))})")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    parser.add_argument("--out-dir", type=pathlib.Path, default=DEFAULT_OUT_DIR)
    parser.add_argument("--png-dir", type=pathlib.Path)
    args = parser.parse_args()
    for name, palette in PALETTES.items():
        write_icon(name, palette, args.out_dir, args.png_dir)


if __name__ == "__main__":
    main()
