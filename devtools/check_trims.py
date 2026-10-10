#!/usr/bin/env python3
"""Proves the trims the game draws from the strip are the trims they replaced,
and composes them in runs on `dist/_trims.png` and `dist/_trimshapes.png`.

Every trim is drawn from one strip texture laid out as geometry
(Graphic_StripTrim, StripTrimGeometry). This renders every shape at every
rotation from that strip with the same geometry (``strip_trim.py``, pinned to
the C# by a golden file), one texel to a pixel, and compares it with the
drawing of that shape and facing in ``make_trim_art.py``, which is the art the
trims shipped as textures until 2026-10.

The comparison allows exactly one kind of difference: the corners where two
bands now meet in a mitre instead of a butt joint. At each such corner three
squares of 2 x 2 texels change: the outer corner, where a lit lip and a shaded
one now meet on the diagonal; the groove crossing, which the old art drew
darker by laying both grooves over it, where the mitre draws each once; and
the inner lip corner, which the old art left as plain fill. Any other
differing pixel fails the run.

Failures that exit non-zero:

* the Python geometry no longer matching ``strip_trim_geometry.txt``, the file
  the harness's ``trims.geometry`` case holds the C# to;
* the strip no longer being the straight's own rows, or its halves bleeding;
* a shape differing from its drawing anywhere but its mitre corners;
* the reference drawings drifting apart: the runner's rail off the straight's,
  or ``_frame`` (which draws the end cap and the frame) no longer redrawing the
  straight, the corner and the runner pixel for pixel;
* anything in the trim folder but the strip and the six menu icons, a stale
  facing or a ``.dds`` cache among them.

Run after any edit to the drawing or the geometry::

    python3 devtools/make_trim_art.py
    python3 devtools/check_trims.py
"""

import math
import os

import strip_render
import strip_trim
import trim_icons
from make_trim_art import OUT_DIR as TEXTURES
from make_trim_art import (STRIP_BANDS, STRIP_HALF, _frame, draw_border_corner,
                           draw_border_double, draw_border_edge,
                           draw_border_end_cap, draw_border_frame,
                           draw_border_inside_corner)
from trim_kit import MOD_ROOT, SUPERSAMPLE, TILE, Canvas, read_rgba_png, write_rgba_png

#: Cell size on the sheets, in final pixels. A room seen from the game camera,
#: not a texture viewer.
CELL = 128

FLOOR = 150          # mid-grey floor, so the bare bed between rails reads
WALL = 55
OUTSIDE = 110        # ground outside a building, on the outside scenes
COLS, ROWS = 6, 6
SHAPE_COLS, SHAPE_ROWS = 12, 6

OUT_DIR = os.path.join(MOD_ROOT, "dist")

#: Every family of trims: its texture stem, how many variants its strip holds,
#: and how many layers of bands (2 with a paint overlay). The floor border's
#: strip is drawn by make_trim_art.py and held to its drawings below; the rest
#: come from make_trim_styles.py.
FAMILIES = (
    ("FloorBorder", 1, 1),
    ("InlayBorder", 1, 1),
    ("VineBorder", 4, 2),
    ("PebbleBorder", 4, 1),
)

#: What the trim folder holds, and nothing else: each family's strip and one
#: menu icon per shape.
SHIPPED = {f"{stem}Strip.png" for stem, _, _ in FAMILIES} | {
    f"{stem}{suffix}_MenuIcon.png" for stem, _, _ in FAMILIES for suffix in trim_icons.SUFFIX.values()}

#: How far a variant strip's joints may be off, per channel, on either of
#: seam_mismatch's two measures, before a seam shows.
SEAM_TOLERANCE = 24

FACINGS = ("north", "east", "south", "west")

#: Each shape's drawing, by facing, as it shipped.
REFERENCE = {
    "Straight": draw_border_edge,
    "Corner": draw_border_corner,
    "InsideCorner": draw_border_inside_corner,
    "Runner": draw_border_double,
    "EndCap": draw_border_end_cap,
    "Frame": lambda canvas, facing: draw_border_frame(canvas),
}

#: The texels at a mitre corner allowed to differ from the butt-jointed
#: drawing, as ranges of depth from each of the two edges: the outer corner,
#: the groove crossing and the inner lip corner.
MITRE_SQUARES = ((0, 2), (15, 17), (30, 32))


def drawn(draw, *args):
    canvas = Canvas(TILE)
    draw(canvas, *args)
    return canvas


def render(shape, rot, strip, size=TILE, cell=(0, 0), bands=3):
    """One trim, rendered from the strip as the game lays it out: each pixel
    centre is sampled at the nearest texel of whichever polygon holds it.

    Returns RGBA rows, top (north) to bottom."""
    width, height, rows = strip
    layout = strip_trim.Layout(strip_trim.period_of(width, height, bands), bands)
    polygons = strip_trim.polygons(shape, rot)
    out = []
    for j in range(size):
        z = 0.5 - (j + 0.5) / size
        row = bytearray(4 * size)
        for i in range(size):
            x = (i + 0.5) / size - 0.5
            for edge, points in polygons:
                if strip_trim.inside(points, x, z):
                    u, v = strip_trim.uv(edge, cell, x, z, layout)
                    texel_row = min(height - 1, int(math.floor((1.0 - v) * height)))
                    texel_col = int(math.floor((u % 1.0) * width)) % width
                    row[4 * i:4 * i + 4] = rows[texel_row][4 * texel_col:4 * texel_col + 4]
                    break
        out.append(bytes(row))
    return out


def as_canvas(rows):
    """Rendered RGBA rows as a Canvas, so the sheet composer can paste them."""
    size = len(rows)
    canvas = Canvas(size)
    for j, row in enumerate(rows):
        for i in range(size):
            value, alpha = row[4 * i], row[4 * i + 3]
            for dy in range(SUPERSAMPLE):
                base = (j * SUPERSAMPLE + dy) * canvas.width + i * SUPERSAMPLE
                for dx in range(SUPERSAMPLE):
                    canvas.value[base + dx] = value
                    canvas.alpha[base + dx] = alpha
    return canvas


def mitre_corners(shape, rot):
    """Pairs of adjacent edges that meet in a mitre on this piece."""
    if shape == "InsideCorner":
        return [(rot & 3, (rot + 1) & 3)]
    if shape not in strip_trim.ARMS:
        return []
    edges = {(rot + arm) & 3 for arm in strip_trim.ARMS[shape]}
    return [(e, (e + 1) & 3) for e in sorted(edges) if (e + 1) & 3 in edges]


def depth_from(edge, i, j, size=TILE):
    """Texels from the given edge to pixel (i, j)."""
    return (j, size - 1 - i, size - 1 - j, i)[edge]


def in_mitre_square(corners, i, j):
    for a, b in corners:
        da, db = depth_from(a, i, j), depth_from(b, i, j)
        for low, high in MITRE_SQUARES:
            if low <= da < high and low <= db < high:
                return True
    return False


def compare_with_drawing(shape, rot, strip):
    """(pixels differing in a mitre square, pixels differing anywhere else, the
    first of those)."""
    reference = drawn(REFERENCE[shape], FACINGS[rot])._resolve_pixels()
    mine = render(shape, rot, strip)
    corners = mitre_corners(shape, rot)
    allowed = stray = 0
    first = None
    for j in range(TILE):
        for i in range(TILE):
            if reference[j][4 * i:4 * i + 4] == mine[j][4 * i:4 * i + 4]:
                continue
            if in_mitre_square(corners, i, j):
                allowed += 1
            else:
                stray += 1
                first = first or (i, j)
    return allowed, stray, first


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


def place(sheet, canvas, col, row):
    """Paste one tile at exactly its cell, as the trims draw (drawSize 1)."""
    sheet.paste(canvas, col * CELL, row * CELL, size=CELL)


def build_sheet(piece):
    """Runs: a corridor of runners, runners into straights, the vertical
    runner both ways, and a run that stops."""
    sheet = Canvas((COLS * CELL, ROWS * CELL))
    sheet.rect(0, 0, COLS * CELL, ROWS * CELL, FLOOR)
    for col in range(COLS):                       # A: horizontal run
        place(sheet, piece("Runner", 2), col, 0)
    for col in range(3):                          # B: runner meets straight
        place(sheet, piece("Runner", 2), col, 1)
    for col in range(3, COLS):
        place(sheet, piece("Straight", 2), col, 1)
    for row in range(3, ROWS):                    # C: east beside west
        place(sheet, piece("Runner", 1), 0, row)
        place(sheet, piece("Runner", 3), 1, row)
    for row in range(3, ROWS - 1):                # D: a run that stops
        place(sheet, piece("Runner", 1), 4, row)
    return sheet


def build_shapes_sheet(piece):
    """The joins, each scene laid out as a player would build it."""
    sheet = Canvas((SHAPE_COLS * CELL, SHAPE_ROWS * CELL))
    sheet.rect(0, 0, SHAPE_COLS * CELL, SHAPE_ROWS * CELL, FLOOR)

    # E: a wall block jutting into a room. Strips run along both of its faces,
    # and the tile diagonal to its corner takes the inside corner.
    walls = [(2, 0), (3, 0), (2, 1), (3, 1)]
    for row in (0, 1):
        place(sheet, piece("Straight", 1), 1, row)
    for col in (2, 3):
        place(sheet, piece("Straight", 0), col, 2)
    place(sheet, piece("InsideCorner", 0), 1, 2)

    # F: a one-wide path, capped at both ends, upright and lying down.
    place(sheet, piece("EndCap", 0), 5, 0)
    for row in (1, 2):
        place(sheet, piece("Runner", 1), 5, row)
    place(sheet, piece("EndCap", 2), 5, 3)
    place(sheet, piece("EndCap", 3), 0, 5)
    for col in (1, 2):
        place(sheet, piece("Runner", 2), col, 5)
    place(sheet, piece("EndCap", 1), 3, 5)

    # G: a runner comes up from the south and turns east. The turning tile
    # stacks a corner (its outer rails) and an inside corner (its inner joint).
    for row in (2, 3):
        place(sheet, piece("Runner", 1), 7, row)
    place(sheet, piece("Corner", 3), 7, 1)
    place(sheet, piece("InsideCorner", 1), 7, 1)
    for col in (8, 9):
        place(sheet, piece("Runner", 2), col, 1)

    # H: frames, as stepping stones and as a block.
    for row in (0, 2, 4):
        place(sheet, piece("Frame", 0), 11, row)
    for col, row in ((8, 4), (9, 4), (8, 5), (9, 5)):
        place(sheet, piece("Frame", 0), col, row)

    # Walls last: in game they draw above the trims, which sit at a floor
    # altitude.
    for col, row in walls:
        sheet.rect(col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL, WALL)
    return sheet


def seam_mismatch(strip, variants, columns=1, reach=3):
    """How far a strip's variants are from joining cleanly, per channel, as
    (ends, step).

    Any variant may follow any, so every variant must end alike and begin
    alike: `ends` is the widest spread between variants over their first and
    last `columns` columns, which is as far as the game's filtering reads
    across a joint.

    And no joint may step: `step` is how far a variant's last column is from
    any variant's first beyond the largest difference between neighbouring
    columns within `reach` of the joint on either side. Something drawn across
    the joint, as the vine's shared leaf is, differs there as it does a column
    or two away, even where a slanted line of it crosses the joint; a stem
    leaving a tile at another height than it entered jumps at the joint where
    it is otherwise smooth.

    Grey is weighed by alpha, as it is drawn: a texel the strip leaves
    transparent, as the vine's leaves leave the floor past its field, carries
    a grey the game never shows."""
    width, _, rows = strip
    seg = width // variants
    ends = step = 0
    for row in rows:
        for c in (0, 3):
            def at(k, off):
                i = 4 * (k * seg + off)
                return row[i + 3] if c == 3 else row[i] * row[i + 3] // 255
            for off in list(range(columns)) + list(range(seg - columns, seg)):
                values = [at(k, off) for k in range(variants)]
                ends = max(ends, max(values) - min(values))
            for a in range(variants):
                for b in range(variants):
                    near = max([abs(at(a, seg - 2 - i) - at(a, seg - 1 - i)) for i in range(reach)]
                               + [abs(at(b, i) - at(b, i + 1)) for i in range(reach)])
                    step = max(step, abs(at(a, seg - 1) - at(b, 0)) - near)
    return ends, step


# ---- diagonal walls, as players build rooms with them -------------------------

def scene_octagon(outside):
    """An octagonal room, each corner bevelled by two diagonal cells: from
    inside (the trims on the floor) or from outside (round the walls)."""
    outline = [(1, 3), (1, 7), (3, 9), (9, 9), (11, 7), (11, 3), (9, 1), (3, 1)]
    if not outside:
        pieces = ([("Straight", 3, (1, z)) for z in range(3, 7)]
                  + [("Straight", 1, (10, z)) for z in range(3, 7)]
                  + [("Straight", 0, (x, 8)) for x in range(3, 9)]
                  + [("Straight", 2, (x, 1)) for x in range(3, 9)]
                  + [("Diagonal", 0, (1, 7)), ("Diagonal", 0, (2, 8)),
                     ("Diagonal", 1, (9, 8)), ("Diagonal", 1, (10, 7)),
                     ("Diagonal", 2, (10, 2)), ("Diagonal", 2, (9, 1)),
                     ("Diagonal", 3, (2, 1)), ("Diagonal", 3, (1, 2))])
    else:
        pieces = ([("Straight", 1, (0, z)) for z in range(3, 7)]
                  + [("Straight", 3, (11, z)) for z in range(3, 7)]
                  + [("Straight", 2, (x, 9)) for x in range(3, 9)]
                  + [("Straight", 0, (x, 0)) for x in range(3, 9)]
                  + [("Diagonal", 2, (1, 7)), ("Diagonal", 2, (2, 8)),
                     ("Diagonal", 3, (9, 8)), ("Diagonal", 3, (10, 7)),
                     ("Diagonal", 0, (10, 2)), ("Diagonal", 0, (9, 1)),
                     ("Diagonal", 1, (2, 1)), ("Diagonal", 1, (1, 2))])
    return outline, outside, pieces


def scene_diamond(outside):
    """A diamond room, every wall diagonal: four tips, from inside or out."""
    outline = [(6, 1), (1, 6), (6, 11), (11, 6)]
    pieces = []
    for i in range(5):
        rots = (2, 3, 0, 1) if outside else (0, 1, 2, 3)
        pieces += [("Diagonal", rots[0], (1 + i, 6 + i)), ("Diagonal", rots[1], (6 + i, 10 - i)),
                   ("Diagonal", rots[2], (10 - i, 5 - i)), ("Diagonal", rots[3], (5 - i, 1 + i))]
    return outline, outside, pieces


def scene_corridor(length):
    """A corridor of runners, both rails, for how a pattern repeats."""
    outline = [(-1, 0), (-1, 1), (length + 1, 1), (length + 1, 0)]
    return outline, False, [("Runner", 0, (x, 0)) for x in range(length)]


def render_scene(scene, strip, layout, window, per_tile):
    """A scene's floor, walls and trims, rendered from the strip: RGBA rows.
    Each trim draws every layer its strip holds, the overlay over the bands,
    as the game prints them."""
    outline, outside, pieces = scene
    x0, z0, x1, z1 = window
    image = strip_render.Image(int(round((x1 - x0) * per_tile)), int(round((z1 - z0) * per_tile)), window)
    image.fill(OUTSIDE if outside else WALL)
    image.fill_polygon(outline, WALL if outside else FLOOR)
    pieces_at = strip_trim.scene_pieces(pieces)
    for shape, rot, cell in sorted(pieces, key=lambda p: p[0] == "Diagonal"):
        ends = (strip_trim.SQUARE, strip_trim.SQUARE)
        if shape == "Diagonal":
            ends = strip_trim.diagonal_ends(cell, rot, pieces_at)
        for each in layout.each_layer():
            for points, uvs in strip_trim.textured(shape, rot, cell, each, ends):
                image.draw([(cell[0] + 0.5 + x, cell[1] + 0.5 + z) for x, z in points], uvs, strip)
    return image.rows()


def compose(panels, gap=12, background=24):
    """Panels of RGBA rows side by side, tops aligned."""
    height = max(len(p) for p in panels)
    width = sum(len(p[0]) // 4 for p in panels) + gap * (len(panels) - 1)
    fill = bytes((background, background, background, 255))
    rows = []
    for y in range(height):
        row = bytearray()
        for k, panel in enumerate(panels):
            w = len(panel[0]) // 4
            row += panel[y] if y < len(panel) else fill * w
            if k < len(panels) - 1:
                row += fill * gap
        rows.append(bytes(row))
    return width, height, rows


def stack(blocks, gap=12, background=24):
    """Composed blocks of rows one above another, lefts aligned."""
    width = max(w for w, _, _ in blocks)
    fill = bytes((background, background, background, 255))
    rows = []
    for k, (w, _, block) in enumerate(blocks):
        for row in block:
            rows.append(row + fill * (width - w))
        if k < len(blocks) - 1:
            rows += [fill * width] * gap
    return width, len(rows), rows


def family_sheet(stem, strip, variants, layers=1):
    """The family's diagonal scenes, whole and with each join close up, and a
    corridor of runners for how its pattern repeats."""
    layout = trim_icons.layout_of(strip, STRIP_BANDS, variants, layers)
    whole = compose([
        render_scene(scene_octagon(False), strip, layout, (0, 0, 12, 10), 40),
        render_scene(scene_octagon(True), strip, layout, (-0.5, -0.5, 12.5, 10.5), 40),
        render_scene(scene_diamond(False), strip, layout, (0, 0, 12, 12), 40),
        render_scene(scene_diamond(True), strip, layout, (0, 0, 12, 12), 40),
    ])
    close = compose([
        render_scene(scene_octagon(False), strip, layout, (0.6, 5.6, 3.8, 8.8), 128),
        render_scene(scene_octagon(True), strip, layout, (0.2, 6.2, 3.4, 9.4), 128),
        render_scene(scene_diamond(False), strip, layout, (4.4, 8.2, 7.6, 11.4), 128),
        render_scene(scene_diamond(True), strip, layout, (4.4, 8.8, 7.6, 12.0), 128),
    ])
    corridor = compose([render_scene(scene_corridor(16), strip, layout, (-0.25, -0.5, 16.25, 1.5), 72)])
    return stack([whole, close, corridor])


if __name__ == "__main__":
    failures = []

    # The geometry the game draws, as far as this file is concerned.
    with open(strip_trim.GOLDEN) as golden:
        expected = [line for line in golden.read().splitlines() if line]
    if not strip_trim.golden_matches(strip_trim.golden_lines(), expected):
        failures.append("strip_trim.py no longer matches strip_trim_geometry.txt: the "
                        "Python model has drifted from the file the C# is held to")
    else:
        print(f"  geometry: {len(expected)} lines match the golden file")

    # Nothing in the folder but what ships.
    present = set(os.listdir(TEXTURES))
    stray = sorted(present - SHIPPED)
    missing = sorted(SHIPPED - present)
    if stray or missing:
        failures.append("trim folder: " + "; ".join(
            ([f"stray {', '.join(stray)}"] if stray else [])
            + ([f"missing {', '.join(missing)}"] if missing else [])))
    else:
        print(f"  trim folder: {len(FAMILIES)} strips and their {len(SHIPPED) - len(FAMILIES)} icons, nothing else")

    # The strip is the straight's own rows, its first two bands apart, and its
    # third the two halfway between, depth for depth.
    strip = read_rgba_png(os.path.join(TEXTURES, "FloorBorderStrip.png"))
    width, height, rows = strip
    north = drawn(draw_border_edge, "north")._resolve_pixels()
    south = drawn(draw_border_edge, "south")._resolve_pixels()
    if height != STRIP_BANDS * STRIP_HALF:
        failures.append(f"strip is {width}x{height}, want {STRIP_BANDS * STRIP_HALF} rows")
    else:
        off = [y for y in range(STRIP_HALF)
               if rows[y] != north[y][:4 * width]
               or rows[STRIP_HALF + y] != south[TILE - STRIP_HALF + y][:4 * width]]
        side = [y for y in range(STRIP_HALF)
                if rows[2 * STRIP_HALF + y] != bytes(
                    (a + b + 1) // 2 for a, b in zip(rows[y], rows[2 * STRIP_HALF - 1 - y]))]
        if off:
            failures.append(f"strip rows {off[:5]} are not the straight's own rows")
        elif side:
            failures.append(f"side band rows {side[:5]} are not the north and south bands' mean")
        else:
            print(f"  strip: {width}x{height}, row for row the straight's north and south bands, "
                  "then their mean")

    # Every other family's strip: three bands, twice over with a paint
    # overlay, one tile to a variant, and every variant meeting every other at
    # its ends, in both layers. An overlay must hold something.
    for stem, variants, layers in FAMILIES[1:]:
        other = read_rgba_png(os.path.join(TEXTURES, f"{stem}Strip.png"))
        w, h, other_rows = other
        if (w, h) != (TILE * variants, layers * STRIP_BANDS * STRIP_HALF):
            failures.append(f"{stem}Strip is {w}x{h}, want {TILE * variants}x{layers * STRIP_BANDS * STRIP_HALF}")
            continue
        ends, step = seam_mismatch(other, variants)
        overlay = ""
        if layers > 1:
            half = STRIP_BANDS * STRIP_HALF
            coverage = sum(row[i] for row in other_rows[half:] for i in range(3, 4 * w, 4)) / (255 * w * half)
            overlay = f", overlay covers {coverage:.1%} of its half"
            if coverage == 0:
                failures.append(f"{stem}Strip: its paint overlay is empty")
        if variants > 1 and max(ends, step) > SEAM_TOLERANCE:
            failures.append(f"{stem}Strip: its variants' ends differ by up to {ends}, and a joint "
                            f"steps by up to {step}, where they meet")
        else:
            print(f"  {stem}Strip: {w}x{h}, {variants} variant(s)"
                  + (f", ends alike within {ends}, joints step by {step}" if variants > 1 else "") + overlay)

    # The reference drawings still agree with each other.
    runner_h = drawn(draw_border_double, "south")
    straight_s = drawn(draw_border_edge, "south")
    drift = first_difference(runner_h, straight_s, TILE - 40, TILE)
    if drift:
        failures.append(f"the runner's south rail differs from the straight's at {drift}")
    pairs = (
        [(draw_border_edge, f, (f,)) for f in FACINGS]
        + [(draw_border_corner, "south", ("south", "west")),
           (draw_border_corner, "east", ("south", "east")),
           (draw_border_corner, "west", ("north", "west")),
           (draw_border_corner, "north", ("north", "east"))]
        + [(draw_border_double, f, ("north", "south")) for f in ("north", "south")]
        + [(draw_border_double, f, ("east", "west")) for f in ("east", "west")]
    )
    redraws = [f"{draw.__name__}({facing})" for draw, facing, edges in pairs
               if first_difference(drawn(draw, facing), drawn(_frame, edges), 0, TILE)]
    if redraws:
        failures.append("_frame no longer redraws: " + ", ".join(redraws))
    if not drift and not redraws:
        print(f"  drawings: the runner's rail is the straight's; _frame redraws all {len(pairs)}")

    # Every shape, every rotation, against its drawing. The frame has one
    # drawing and is held to it at all four, since code can give a
    # non-rotatable thing any rotation and it must draw the same.
    for shape in strip_trim.SHAPES:
        for rot, facing in enumerate(FACINGS):
            allowed, stray_pixels, first = compare_with_drawing(shape, rot, strip)
            if stray_pixels:
                failures.append(f"{shape} {facing}: {stray_pixels} pixels differ from its "
                                f"drawing outside the mitre corners, first at {first}")
            else:
                print(f"  {shape:12s} {facing:5s}  matches its drawing"
                      + (f", {allowed} pixels changed at its mitres" if allowed else ""))

    cache = {}

    def piece(shape, rot):
        if (shape, rot) not in cache:
            cache[(shape, rot)] = as_canvas(render(shape, rot, strip))
        return cache[(shape, rot)]

    os.makedirs(OUT_DIR, exist_ok=True)
    build_sheet(piece).save_png(os.path.join(OUT_DIR, "_trims.png"))
    print("  row 1     six runners: the corridor run")
    print("  row 2     three runners, then three straight borders")
    print("  rows 4-6  col 1 east beside col 2 west; col 5 a run that stops")
    build_shapes_sheet(piece).save_png(os.path.join(OUT_DIR, "_trimshapes.png"))
    print("  shapes    E cols 1-4: an inside corner around a wall block")
    print("            F col 6 and row 6: a one-wide path capped at both ends")
    print("            G cols 8-10: a runner turning, corner + inside corner stacked")
    print("            H col 12 and cols 9-10: frames apart and in a block")

    # Each family on diagonal walls: an octagon and a diamond, from inside and
    # from outside, their joins close up, and a corridor of runners.
    for stem, variants, layers in FAMILIES:
        family = read_rgba_png(os.path.join(TEXTURES, f"{stem}Strip.png"))
        w, h, sheet_rows = family_sheet(stem, family, variants, layers)
        write_rgba_png(os.path.join(OUT_DIR, f"_trims_{stem}.png"), w, h, sheet_rows)
    print("  families  dist/_trims_<family>.png: octagon and diamond, inside and out,")
    print("            the joins close up, and a corridor of runners")

    if failures:
        raise SystemExit("trims:\n  " + "\n  ".join(failures))
