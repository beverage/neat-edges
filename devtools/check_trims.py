#!/usr/bin/env python3
"""`dist/_trims.png`: the trim pieces in RUNS and beside each other.

No trim is ever seen as its own texture: a border is one tile of a line that
has to read continuous, and the runner has to agree with the straight it is
made of. So the sheet composes runs, and models the two engine transforms that
decide what the joints look like:

* **drawSize 1**: each tile's quad is exactly its cell, so a joint is two
  textures meeting at a tile boundary and the sheet shows it as the game does.
  The defs drew 4% oversize until 2026-10-06, to cover a hairline the game's
  static atlas put at every quad edge (the shimmer seen in play, 2026-08-01);
  ``Patch_TrimAtlas`` now keeps the trims out of that atlas and clamps their
  edges, which removes the hairline instead of moving it onto the neighbour.
* **Graphic_Multi picks the AUTHORED facing, no spin**: east differs from north
  on every piece here, so nothing is rotated at draw time.

Scenes, top to bottom:

  A  a six-tile horizontal run of the runner        corridor continuity
  B  three runners then three straight borders      the two pieces' rails
                                                    must land on one line
  C  a three-tall runner run in EAST beside the
     same run in WEST                               the explicit-west check
  D  a two-tall run that stops                      the open end

A second sheet, `dist/_trimshapes.png`, composes the JOINS, where a piece has
to agree with its neighbours rather than with copies of itself:

  E  an inside corner around a wall block           the square the two strips
                                                    leave open, filled
  F  a one-wide path capped at both ends, upright
     and lying down                                 the caps' three bands
  G  a runner turning a corner: a corner and an
     inside corner stacked on the turning tile      outer rails and inner joint
  H  frames, apart and in a 2x2 block               a single framed tile

Failures that exit non-zero:

* the runner's rail drifting off the straight border's rail. They share
  ``_band`` today, and this is what keeps that true;
* ``_frame``, which draws the end cap and the frame, failing to redraw any of
  the twelve shipped straight, corner and runner facings pixel for pixel. The
  new shapes join by the corner's rules only while this holds;
* an authored west going missing while the art still needs it: the
  straight's, the runner's and the end cap's (the engine auto-mirrors west
  from east, and a mirror flips the absolute lighting), and every facing of
  the inside corner, which is chiral. Deleting one of those files
  reintroduces the bug silently, with nothing in the log.

Run after any edit to ``make_trim_art.py``::

    python3 devtools/make_trim_art.py
    python3 devtools/check_trims.py
"""

import os

from make_trim_art import OUT_DIR as TEXTURES
from make_trim_art import (_frame, draw_border_corner, draw_border_double,
                           draw_border_edge, draw_border_end_cap,
                           draw_border_frame, draw_border_inside_corner)
from trim_kit import MOD_ROOT, SUPERSAMPLE, Canvas

#: Cell size on the sheet, in final pixels. The art is authored at 256; the
#: sheet is a room seen from the game camera, not a texture viewer.
CELL = 128

#: The defs' drawSize. Keep in step with `NeatEdges_Trims.xml`: a joint check
#: at another size is checking a game we do not ship.
OVERDRAW = 1.0

FLOOR = 150          # mid-grey floor, so the bare bed between rails reads
COLS, ROWS = 6, 6

OUT_DIR = os.path.join(MOD_ROOT, "dist")


def piece(draw, *args):
    canvas = Canvas(256)
    draw(canvas, *args)
    return canvas


def place(sheet, canvas, col, row):
    """Paste one tile the way the engine draws it: ``OVERDRAW`` times its
    cell, centred."""
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


#: Which edges each shipped facing covers, as ``_frame`` must redraw it. The
#: corner's facing names its corner clockwise: south=SW, east=SE, west=NW,
#: north=NE.
SHIPPED = (
    [(draw_border_edge, f, (f,)) for f in ("north", "south", "east", "west")]
    + [(draw_border_corner, "south", ("south", "west")),
       (draw_border_corner, "east", ("south", "east")),
       (draw_border_corner, "west", ("north", "west")),
       (draw_border_corner, "north", ("north", "east"))]
    + [(draw_border_double, f, ("north", "south")) for f in ("north", "south")]
    + [(draw_border_double, f, ("east", "west")) for f in ("east", "west")]
)

#: Files the art needs and the engine would otherwise invent: every west that
#: is authored, and every facing of the chiral inside corner.
REQUIRED_FILES = (
    ["FloorBorderEdge_west", "FloorBorderDouble_west", "FloorBorderEndCap_west"]
    + [f"FloorBorderInsideCorner_{f}" for f in ("north", "east", "south", "west")]
)

SHAPE_COLS, SHAPE_ROWS = 12, 6
WALL = 55


def build_shapes_sheet():
    """The joins, each scene laid out as a player would build it."""
    straight = {f: piece(draw_border_edge, f) for f in ("north", "south", "east")}
    runner = {f: piece(draw_border_double, f) for f in ("south", "east")}
    cap = {f: piece(draw_border_end_cap, f)
           for f in ("north", "east", "south", "west")}
    inside = {f: piece(draw_border_inside_corner, f)
              for f in ("north", "east", "south", "west")}
    corner_west = piece(draw_border_corner, "west")
    frame = piece(draw_border_frame)

    sheet = Canvas((SHAPE_COLS * CELL, SHAPE_ROWS * CELL))
    sheet.rect(0, 0, SHAPE_COLS * CELL, SHAPE_ROWS * CELL, FLOOR)

    # E: a wall block jutting into a room. Strips run along both of its faces,
    # and the tile diagonal to its corner takes the inside corner.
    walls = [(2, 0), (3, 0), (2, 1), (3, 1)]
    for row in (0, 1):
        place(sheet, straight["east"], 1, row)
    for col in (2, 3):
        place(sheet, straight["north"], col, 2)
    place(sheet, inside["north"], 1, 2)

    # F: a one-wide path, capped at both ends, upright and lying down.
    place(sheet, cap["north"], 5, 0)
    for row in (1, 2):
        place(sheet, runner["east"], 5, row)
    place(sheet, cap["south"], 5, 3)
    place(sheet, cap["west"], 0, 5)
    for col in (1, 2):
        place(sheet, runner["south"], col, 5)
    place(sheet, cap["east"], 3, 5)

    # G: a runner comes up from the south and turns east. The turning tile
    # stacks a corner (its outer rails) and an inside corner (its inner joint).
    for row in (2, 3):
        place(sheet, runner["east"], 7, row)
    place(sheet, corner_west, 7, 1)
    place(sheet, inside["east"], 7, 1)
    for col in (8, 9):
        place(sheet, runner["south"], col, 1)

    # H: frames, as stepping stones and as a block.
    for row in (0, 2, 4):
        place(sheet, frame, 11, row)
    for col, row in ((8, 4), (9, 4), (8, 5), (9, 5)):
        place(sheet, frame, col, row)

    # Walls last: in game they draw above the trims, which sit at a floor
    # altitude.
    for col, row in walls:
        sheet.rect(col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL, WALL)
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

    # The new shapes are drawn by _frame, so it has to BE the family's drawing.
    mismatches = []
    for draw, facing, edges in SHIPPED:
        at = first_difference(piece(draw, facing), piece(_frame, edges), 0, 256)
        if at:
            mismatches.append(f"{draw.__name__}({facing}) at {at}")
    if mismatches:
        failures.append("_frame no longer redraws the shipped pieces: "
                        + "; ".join(mismatches))
    else:
        print(f"  _frame: redraws all {len(SHIPPED)} shipped facings pixel "
              f"for pixel")

    if not mirrors_cleanly(piece(draw_border_end_cap, "east")):
        print("  end cap: west must be authored (the mirror would flip the "
              "lighting)")
    missing = [name for name in REQUIRED_FILES
               if not os.path.exists(os.path.join(TEXTURES, name + ".png"))]
    if missing:
        failures.append("missing authored facings, which the engine would "
                        "mirror or leave blank: " + ", ".join(missing))
    else:
        print(f"  authored facings: all {len(REQUIRED_FILES)} present")

    os.makedirs(OUT_DIR, exist_ok=True)
    out = os.path.join(OUT_DIR, "_trims.png")
    build_sheet(runner_h, runner_v_east, runner_v_west, straight_s).save_png(out)
    print("  row 1     six runners: the corridor run")
    print("  row 2     three runners, then three straight borders")
    print("  rows 4-6  col 1 east beside col 2 west; col 5 a run that stops")

    shapes_out = os.path.join(OUT_DIR, "_trimshapes.png")
    build_shapes_sheet().save_png(shapes_out)
    print("  shapes    E cols 1-4: an inside corner around a wall block")
    print("            F col 6 and row 6: a one-wide path capped at both ends")
    print("            G cols 8-10: a runner turning, corner + inside corner "
          "stacked")
    print("            H col 12 and cols 9-10: frames apart and in a block")

    if failures:
        raise SystemExit("trims:\n  " + "\n  ".join(failures))
