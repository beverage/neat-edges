#!/usr/bin/env python3
"""`dist/_trims.png`: the trim pieces in RUNS and beside each other.

No trim is ever seen as its own texture: a border is one tile of a line that
has to read continuous, and the runner has to agree with the straight it is
made of. So the sheet composes runs, and models the two engine transforms that
decide what the joints look like:

* **drawSize 1.04**: each tile's quad is 4% larger than its cell, centred, so
  a joint is covered by a neighbour's stable interior texels rather than by two
  edge-texel columns the sampler resolves differently as the camera moves (the
  shimmer seen in play, 2026-08-01).
* **Graphic_Multi picks the AUTHORED facing, no spin**: east differs from north
  on every piece here, so nothing is rotated at draw time.

Scenes, top to bottom:

  A  a six-tile horizontal run of the runner        corridor continuity
  B  three runners then three straight borders      the two pieces' rails
                                                    must land on one line
  C  a three-tall runner run in EAST beside the
     same run in WEST                               the explicit-west check
  D  a two-tall run that stops                      the open end

Two failures exit non-zero:

* the runner's rail drifting off the straight border's rail. They share
  ``_band`` today, and this is what keeps that true;
* the explicit ``FloorBorderDouble_west.png`` going missing while the art
  still needs it. The engine auto-mirrors west from east, and a mirror flips
  the absolute lighting across both rails at once. Deleting that file
  reintroduces the bug silently, with nothing in the log.

Run after any edit to ``make_trim_art.py``::

    python3 devtools/make_trim_art.py
    python3 devtools/check_trims.py
"""

import os

from make_trim_art import OUT_DIR as TEXTURES
from make_trim_art import draw_border_double, draw_border_edge
from trim_kit import MOD_ROOT, SUPERSAMPLE, Canvas

#: Cell size on the sheet, in final pixels. The art is authored at 256; the
#: sheet is a room seen from the game camera, not a texture viewer.
CELL = 128

#: The defs' drawSize. Keep in step with `NeatEdges_Trims.xml`: a joint check
#: that does not overdraw is checking a game we do not ship.
OVERDRAW = 1.04

FLOOR = 150          # mid-grey floor, so the bare bed between rails reads
COLS, ROWS = 6, 6

OUT_DIR = os.path.join(MOD_ROOT, "dist")


def piece(draw, facing):
    canvas = Canvas(256)
    draw(canvas, facing)
    return canvas


def place(sheet, canvas, col, row):
    """Paste one tile the way the engine draws it: 4% over, centred."""
    quad = int(round(CELL * OVERDRAW))
    off = (quad - CELL) / 2
    sheet.paste(canvas, int(col * CELL - off), int(row * CELL - off), size=quad)


def first_difference(a, b, y0, y1):
    """First (x, y) in final pixels where two canvases disagree, else None."""
    width = a.width
    for y in range(y0 * SUPERSAMPLE, y1 * SUPERSAMPLE):
        row = y * width
        for x in range(width):
            if (a.value[row + x] != b.value[row + x]
                    or a.alpha[row + x] != b.alpha[row + x]):
                return x // SUPERSAMPLE, y // SUPERSAMPLE
    return None


def mirrors_cleanly(canvas):
    """True if flipping this canvas horizontally leaves it unchanged.

    When it is False the piece MUST ship an authored west texture, because the
    engine's auto-mirror would not merely move the art, it would light it from
    the other side.
    """
    width = canvas.width
    for y in range(canvas.height):
        row = y * width
        for x in range(width // 2):
            if (canvas.value[row + x] != canvas.value[row + width - 1 - x]
                    or canvas.alpha[row + x] != canvas.alpha[row + width - 1 - x]):
                return False
    return True


def build_sheet(runner_h, runner_v_east, runner_v_west, straight_s):
    sheet = Canvas((COLS * CELL, ROWS * CELL))
    sheet.rect(0, 0, COLS * CELL, ROWS * CELL, FLOOR)

    for col in range(COLS):                       # A: horizontal run
        place(sheet, runner_h, col, 0)

    for col in range(3):                          # B: runner meets straight
        place(sheet, runner_h, col, 1)
    for col in range(3, COLS):
        place(sheet, straight_s, col, 1)

    for row in range(3, ROWS):                    # C: east beside west
        place(sheet, runner_v_east, 0, row)
        place(sheet, runner_v_west, 1, row)

    for row in range(3, ROWS - 1):                # D: a run that stops
        place(sheet, runner_v_east, 4, row)

    return sheet


if __name__ == "__main__":
    runner_h = piece(draw_border_double, "south")
    runner_v_east = piece(draw_border_double, "east")
    runner_v_west = piece(draw_border_double, "west")
    straight_s = piece(draw_border_edge, "south")

    failures = []

    # The runner is two of the straight's band. If that ever stops being
    # literally true, a run of runners and a run of borders stop lining up,
    # and the drift is a few pixels: exactly the size nobody spots on a lone
    # texture.
    band_top = 256 - 40          # BAND + EDGE, with room to spare
    drift = first_difference(runner_h, straight_s, band_top, 256)
    if drift:
        failures.append(
            f"runner's south rail differs from the straight border's at "
            f"{drift}: the two have stopped sharing _band")
    else:
        print(f"  south rail: identical to the straight border below y="
              f"{band_top}")

    # West is authored precisely because the mirror would not be innocent.
    west_png = os.path.join(TEXTURES, "FloorBorderDouble_west.png")
    if mirrors_cleanly(runner_v_east):
        print("  east facing is mirror-symmetric: an explicit west is no "
              "longer needed (the def and generator can drop it)")
    elif not os.path.exists(west_png):
        failures.append(
            "FloorBorderDouble_west.png is missing and the east facing is "
            "NOT mirror-symmetric, so the engine will mirror it and light "
            "both rails from the wrong side")
    else:
        print("  west: authored (the mirror would flip the lighting)")

    os.makedirs(OUT_DIR, exist_ok=True)
    out = os.path.join(OUT_DIR, "_trims.png")
    build_sheet(runner_h, runner_v_east, runner_v_west, straight_s).save_png(out)
    print("  row 1     six runners: the corridor run")
    print("  row 2     three runners, then three straight borders")
    print("  rows 4-6  col 1 east beside col 2 west; col 5 a run that stops")

    if failures:
        raise SystemExit("trims:\n  " + "\n  ".join(failures))
