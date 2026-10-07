#!/usr/bin/env python3
"""Card frames from the stage captures: crop, house title and fade, then the GIF.

    python3 compose_stage.py NAME SOFT.png HARD.png [OTHER.png ...]

Each capture is the stage's 8 x 5 cell frame. The card keeps the bottom 4.5
rows (16:9 at eight cells across), so the half row cut at the top sits under
the title. Authored at 2x like the earlier cards (title 88 pt at +26, a 180 px
black-to-clear fade), exported at 640x360. The first two frames become the
loop, through neat-edges' own transition-gif.py.
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "build")
# Shift Change's copy of the house title font, beside this repo in the constellation.
FONT = os.path.join(HERE, "..", "..", "shift-change", "media", "RimWordFont.ttf")
GIF_TOOL = os.path.join(HERE, "transition-gif.py")
W, H = 1280, 720
CELLS_W, CELLS_H = 8, 5


def size(path):
    w, h = subprocess.run(["magick", "identify", "-format", "%w %h", path],
                          capture_output=True, text=True, check=True).stdout.split()
    return int(w), int(h)


def hero_args():
    """HERO=path.png: an overlay centred in the space under the title."""
    hero = os.environ.get("HERO")
    if not hero:
        return []
    w, h = size(hero)
    x, y = round(W / 2 - w / 2), round((100 + H) / 2 - h / 2)
    return [hero, "-gravity", "northwest", "-geometry", f"+{x}+{y}", "-composite"]


def card(capture, name, title):
    w, h = size(capture)
    cell = h / CELLS_H
    print(f"{name}: {w}x{h}, {w / CELLS_W:.1f} x {cell:.1f} px per cell")
    top = round(cell / 2)
    big = os.path.join(OUT, f"{name}@2x.png")
    subprocess.run(["magick", capture, "-crop", f"{w}x{h - top}+0+{top}", "+repage",
                    "-filter", "Lanczos", "-resize", f"{W}x{H}!"]
                   + hero_args() +
                   ["(", "-size", f"{W}x180", "gradient:black-none", ")", "-geometry", "+0+0", "-composite",
                    title, "-gravity", "north", "-geometry", "+0+26", "-composite",
                    "-alpha", "off", big], check=True)
    small = os.path.join(OUT, f"{name}.png")
    subprocess.run(["magick", big, "-filter", "Lanczos", "-resize", "640x360", small], check=True)
    return small


def main():
    os.makedirs(OUT, exist_ok=True)
    title = os.path.join(OUT, "title.png")
    subprocess.run(["magick", "-background", "none", "-fill", "#f5f0e7", "-font", FONT,
                    "-pointsize", "88", "label:NEAT EDGES", "-trim", "+repage", title], check=True)
    name = sys.argv[1]
    cards = [card(path, os.path.splitext(os.path.basename(path))[0], title) for path in sys.argv[2:]]
    gif = os.path.join(OUT, f"{name}.gif")
    subprocess.run([sys.executable, GIF_TOOL, cards[0], cards[1], gif], check=True)
    pair = os.path.join(OUT, f"{name}-pair.png")
    args = []
    for c in cards:
        args += [c, "(", "-size", "12x360", "xc:#1d1d1d", ")"]
    subprocess.run(["magick"] + args[:-5] + ["+append", pair], check=True)
    print(gif)
    print(pair)


if __name__ == "__main__":
    main()
