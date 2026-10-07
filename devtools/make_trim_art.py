#!/usr/bin/env python3
"""The trim pieces' art: the floor border, its mitred corner, the inside
corner, the double-rail runner, the end cap and the frame, and three menu
icons. Drawing plus manifest; run it to regenerate.

    python3 devtools/make_trim_art.py
    python3 devtools/check_trims.py

The trims are 1×1 non-edifice buildings carrying a band along one or more
edges of their cell, rotated to pick which. They moved here from Fine
Establishments with their art; this is that mod's drawing code, unchanged in
every pixel.

THE BORDER hugs ONE edge, full length, so a row reads as one continuous line.
It rotates rather than links: the first design was a centred linked band,
geometrically flawless and wrong, because borders exist to trace edges (wall
lines, counter feet) and a link mask can never say which edge to hug.
Graphic_Multi with all four textures. West is authored rather than left to
the engine's mirror of east: a right-edge strip flipped horizontally is a
left-edge strip in shape, but its lit lip lands on the inside, the opposite of
every authored piece it meets, which showed as a step in the lip where a west
strip met a corner (2026-10-06). Not linked, so no 75% crop, and kept out of
the game's static atlas (Patch_TrimAtlas): authored pixels are screen truth,
and the strip must bleed to its tile edge exactly.

THE CORNER owns both bands of an L in one image, which is what makes a clean
join possible at all: two separate strips can never meet properly, since
neither texture knows the other. It is chiral, so all four facings ship.

THE RUNNER owns both opposite edges (`| |`, or `=` turned a quarter), so a
corridor gets its two rails from one drag. Four textures, two drawings, and
west must be AUTHORED rather than mirrored; see ``draw_border_double``.

THE INSIDE CORNER is the square two strips leave open where a line turns
around an inside corner: the strips on the two neighbouring tiles meet only at
a point, a band's width short. It carries both neighbours' bands on and joins
them. Chiral, so all four facings ship.

THE END CAP owns three edges (closing a runner or a one-wide path) and THE
FRAME all four (a single framed tile). Both are drawn by ``_frame``, the
corner's joining rules generalised to any set of edges; ``check_trims.py``
proves the generalisation by redrawing every shipped piece with it. The end
cap's west is authored for the runner's reason; the frame is one texture,
since it never rotates.

Stuffable (Woody/Stony/Metallic) and paintable: greyscale, single-material,
no mask, which are the colour channels the engine actually supports.
"""

import os

from trim_kit import DARK, HILITE, LIGHT, MOD_ROOT, SHADE, TILE, Canvas

#: Strip width in authored pixels, drawn 1:1 (no atlas crop), so this IS the
#: on-screen fraction. A quarter tile (60) read far too heavy beside Flooring
#: Floors' own border in play (2026-08-01); halved to 32, one eighth of a
#: tile. One constant to nudge either way.
BAND = 32
EDGE = 3                    # dark keyline on the strip's inner side

#: Where the textures go. Defs point at ``NeatEdges/Trim/<stem>``.
OUT_DIR = os.path.join(MOD_ROOT, "Textures", "NeatEdges", "Trim")


def draw_border_corner(canvas, facing):
    """Two strips meeting in an L; the grooves stop where they meet.

    Bespoke rather than two straight strips layered: layering let one band's
    fill erase the other's inner keyline in the overlap square (a lighter
    patch in play, 2026-08-01), and independent grooves crossed, which a
    diagonal mitre line then papered over. Drawn as one shape: a single fill
    union, an L keyline that follows the union's inner edge, and centre
    grooves that RUN INTO EACH OTHER and stop, so no mitre line is needed.

    Canonical art is the south+west corner; the other three are coordinate
    flips. Facing → corner: south=SW, east=SE, west=NW, north=NE (clockwise
    cycling; all four textures explicit because the piece is chiral).
    """
    size = canvas.final_width
    scale = size / TILE
    band = int(BAND * scale)
    edge = max(2, int(EDGE * scale))
    mid = band // 2
    flip_x = facing in ("east", "north")
    flip_y = facing in ("west", "north")

    def rect(x0, y0, x1, y1, value, **kwargs):
        if flip_x:
            x0, x1 = size - x1, size - x0
        if flip_y:
            y0, y1 = size - y1, size - y0
        canvas.rect(x0, y0, x1, y1, value, **kwargs)

    # Fill union: vertical band down the west edge, horizontal along south.
    rect(0, 0, band, size, LIGHT)
    rect(0, size - band, size, size, LIGHT)
    # Inner keyline following the union's L: vertical reveal beside the
    # vertical band, horizontal reveal above the horizontal band.
    rect(band, 0, band + edge, size - band, DARK)
    rect(band, size - band - edge, size, size - band, DARK)
    rect(band, size - band - edge, band + edge, size - band, DARK)
    # Centre grooves, each stopping at the other: the L join, no mitre.
    rect(mid - 1, 0, mid + 1, size - mid + 1, SHADE, alpha=150)
    rect(mid - 1, size - mid - 1, size, size - mid + 1, SHADE, alpha=150)
    # Bevels: the light is ABSOLUTE (west/top edges lit, east/bottom shaded,
    # exactly as the straight pieces draw themselves), so a flip moves a
    # bevel's geometry but its colour is re-decided from its FINAL side.
    # Flipping colour with geometry was the joint bug seen in play
    # (2026-08-01): a corner's inner edge went bright where the straight
    # above it was dark, and its outer highlight read as a yellow seam. Each
    # bevel runs parallel to its band and stops at the other band, so nothing
    # ever crosses a joint.
    for bx0, by0, bx1, by1, _axis, side in (
            (0, 0, 2, size, "v", "w"),                       # outer, full run
            (band - 2, 0, band, size - band, "v", "e"),      # inner, clipped
            (0, size - 2, size, size, "h", "s"),             # outer, full run
            (band, size - band, size, size - band + 2, "h", "n")):
        if flip_x:
            bx0, bx1 = size - bx1, size - bx0
            side = {"w": "e", "e": "w"}.get(side, side)
        if flip_y:
            by0, by1 = size - by1, size - by0
            side = {"n": "s", "s": "n"}.get(side, side)
        lit = side in ("w", "n")
        canvas.rect(bx0, by0, bx1, by1, HILITE if lit else SHADE,
                    alpha=110 if lit else 120)


def _band(canvas, edge, inset=0):
    """ONE full-length strip hugging ``edge``: the family's single band.

    Every straight piece is this, so nothing can drift: the border draws one,
    the runner draws two opposite ones, and the menu icons draw them pulled
    inward via ``inset``. ``edge`` is a real compass edge (all four are legal
    here, unlike ``draw_border_edge``'s facings).

    The keyline and bevels are ABSOLUTE-light, decided by which edge the band
    hugs: the north/west lip catches the highlight and the south/east lip the
    shade, and the dark keyline always sits on the band's INNER side. That is
    the corner's hard-won rule (2026-08-01): a bevel whose colour follows the
    geometry instead of its final side is what put a bright inner edge under a
    dark straight and a yellow seam at the joint.
    """
    size = canvas.final_width
    scale = size / TILE
    band = int(BAND * scale)
    lip = max(2, int(EDGE * scale))

    if edge in ("north", "south"):
        y0 = inset if edge == "north" else size - inset - band
        y1 = y0 + band
        key0, key1 = (y0, y1 + lip) if edge == "north" else (y0 - lip, y1)
        canvas.rect(0, key0, size, key1, DARK)             # inner keyline
        canvas.rect(0, y0, size, y1, LIGHT)
        canvas.rect(0, (y0 + y1) // 2 - 1, size, (y0 + y1) // 2 + 1,
                    SHADE, alpha=150)
        canvas.rect(0, y0, size, y0 + 2, HILITE, alpha=110)
        canvas.rect(0, y1 - 2, size, y1, SHADE, alpha=120)
    elif edge in ("east", "west"):
        x0 = inset if edge == "west" else size - inset - band
        x1 = x0 + band
        key0, key1 = (x0, x1 + lip) if edge == "west" else (x0 - lip, x1)
        canvas.rect(key0, 0, key1, size, DARK)
        canvas.rect(x0, 0, x1, size, LIGHT)
        canvas.rect((x0 + x1) // 2 - 1, 0, (x0 + x1) // 2 + 1, size,
                    SHADE, alpha=150)
        canvas.rect(x0, 0, x0 + 2, size, HILITE, alpha=110)
        canvas.rect(x1 - 2, 0, x1, size, SHADE, alpha=120)
    else:
        raise ValueError(f"unknown edge {edge!r}")


#: The two vertical bands draw their bevels before the horizontal ones, the
#: order the corner has always used: where two outer lips meet in a tile's
#: corner, the horizontal lip lands on top.
_VERTICAL = ("west", "east")
_HORIZONTAL = ("north", "south")


def _frame(canvas, edges):
    """Bands along any set of edges, joined the way the corner joins its two.

    The corner's rules, generalised so the end cap and the frame are drawn by
    them rather than by hand: one fill union; a keyline on each band's inner
    side that stops at the inner edge of any band crossing it; centre grooves
    that run into a crossing band's groove and stop there; bevels lit by their
    final side, north and west lips light and south and east shaded. Outer
    lips run the whole edge, inner lips stop where a crossing band begins.

    It also draws every shipped piece: one edge is ``_band``, two adjacent
    edges are ``draw_border_corner`` and two opposite ones are the runner.
    ``check_trims.py`` fails the moment any of those twelve facings stops
    matching pixel for pixel, which is what makes the shapes drawn here
    members of the family rather than lookalikes.
    """
    edges = set(edges)
    unknown = edges - set(_VERTICAL + _HORIZONTAL)
    if unknown:
        raise ValueError(f"unknown edges {sorted(unknown)!r}")
    size = canvas.final_width
    scale = size / TILE
    band = int(BAND * scale)
    key = max(2, int(EDGE * scale))
    mid = band // 2
    n, e, s, w = ("north" in edges, "east" in edges,
                  "south" in edges, "west" in edges)

    # Where a vertical band's keyline and inner lip stop (the inner edge of a
    # crossing band) and where its groove stops (the far side of the crossing
    # groove); likewise for the horizontal bands.
    v_inner = (band if n else 0, size - band if s else size)
    v_groove = (mid - 1 if n else 0, size - mid + 1 if s else size)
    h_inner = (band if w else 0, size - band if e else size)
    h_groove = (mid - 1 if w else 0, size - mid + 1 if e else size)

    if w:
        canvas.rect(0, 0, band, size, LIGHT)
    if e:
        canvas.rect(size - band, 0, size, size, LIGHT)
    if n:
        canvas.rect(0, 0, size, band, LIGHT)
    if s:
        canvas.rect(0, size - band, size, size, LIGHT)

    if w:
        canvas.rect(band, v_inner[0], band + key, v_inner[1], DARK)
    if e:
        canvas.rect(size - band - key, v_inner[0], size - band, v_inner[1], DARK)
    if n:
        canvas.rect(h_inner[0], band, h_inner[1], band + key, DARK)
    if s:
        canvas.rect(h_inner[0], size - band - key, h_inner[1], size - band, DARK)

    if w:
        canvas.rect(mid - 1, v_groove[0], mid + 1, v_groove[1], SHADE, alpha=150)
    if e:
        canvas.rect(size - mid - 1, v_groove[0], size - mid + 1, v_groove[1],
                    SHADE, alpha=150)
    if n:
        canvas.rect(h_groove[0], mid - 1, h_groove[1], mid + 1, SHADE, alpha=150)
    if s:
        canvas.rect(h_groove[0], size - mid - 1, h_groove[1], size - mid + 1,
                    SHADE, alpha=150)

    lit, shade = (HILITE, 110), (SHADE, 120)
    lips = []
    if w:
        lips += [((0, 0, 2, size), lit),
                 ((band - 2, v_inner[0], band, v_inner[1]), shade)]
    if e:
        lips += [((size - 2, 0, size, size), shade),
                 ((size - band, v_inner[0], size - band + 2, v_inner[1]), lit)]
    if n:
        lips += [((0, 0, size, 2), lit),
                 ((h_inner[0], band - 2, h_inner[1], band), shade)]
    if s:
        lips += [((0, size - 2, size, size), shade),
                 ((h_inner[0], size - band, h_inner[1], size - band + 2), lit)]
    for (x0, y0, x1, y1), (value, alpha) in lips:
        canvas.rect(x0, y0, x1, y1, value, alpha=alpha)


def draw_border_edge(canvas, facing):
    """The strip hugging one edge, full length. Any of the four facings.

    West ships as its own texture, like the runner's and the end cap's. It was
    left to the engine's mirror of east until 2026-10-06, on the belief that a
    single strip could take it; mirrored, the strip's lit lip sits on its
    inner side, so wherever it met a corner, an inside corner or a cap, all of
    them lit the other way, the lip stepped at the joint.
    """
    if facing not in ("north", "south", "east", "west"):
        raise ValueError(f"unknown facing {facing!r}")
    _band(canvas, facing)


def draw_border_double(canvas, facing):
    """BOTH opposite edges at once: the runner. Any of the four facings.

    One def covers both orientations because the figure is 180°-SYMMETRIC:
    north is south's drawing and west is east's, so four facings cost two
    drawings.

    **West ships EXPLICITLY, and must.** The engine would auto-mirror it from
    east, and a mirror flips the absolute lighting: the west rail's lit outer
    lip lands on the east rail's shaded one and vice versa, so a west-rotated
    runner would be lit from the wrong side and disagree with every other
    piece in the family. The single strip's west was left to the mirror until
    2026-10-06, on the belief that one rail could take it; it could not,
    because it meets pieces lit the right way (see ``draw_border_edge``).

    Two opposite bands never touch (each is ``BAND + EDGE`` deep against a 256
    tile), so drawing order does not matter and the bed between them stays
    bare floor.
    """
    if facing in ("north", "south"):
        _band(canvas, "north")
        _band(canvas, "south")
    elif facing in ("east", "west"):
        _band(canvas, "east")
        _band(canvas, "west")
    else:
        raise ValueError(f"unknown facing {facing!r}")


#: The end cap's three edges per facing: the facing names the CLOSED end, the
#: middle band, so a cap facing north closes a run that comes up from the
#: south. As quarter-turns from the facing that is -1, 0, +1, which is what
#: the def's `edges` say.
END_CAP_EDGES = {
    "north": ("west", "north", "east"),
    "east": ("north", "east", "south"),
    "south": ("east", "south", "west"),
    "west": ("south", "west", "north"),
}


def draw_border_end_cap(canvas, facing):
    """Three strips in a U: the end of a runner, or of a one-wide path.

    All four facings ship. The U is mirror-symmetric in shape but not in
    light: mirrored, its lit lips land on the shaded side, the runner's
    reason for an authored west.
    """
    if facing not in END_CAP_EDGES:
        raise ValueError(f"unknown facing {facing!r}")
    _frame(canvas, END_CAP_EDGES[facing])


def draw_border_frame(canvas):
    """A strip around all four edges: one framed tile. One texture, because
    the piece never rotates, so there is nothing to mirror and no facing."""
    _frame(canvas, ("north", "east", "south", "west"))


def draw_border_inside_corner(canvas, facing):
    """The square an inside corner is missing, and the joint that fills it.

    Where a line of strips turns around an inside corner (a wall jutting into
    the room), the strips on the two neighbouring tiles each run the full
    length of their own tile and meet only at a point, leaving a band's width
    of square open in the tile between them. This is that square, with both
    neighbours' features carried on and joined: their keylines meet around the
    corner that points into the room, their grooves run into each other and
    stop, and their inner lips run along the square's two room-facing sides.
    The other two sides are joints where the neighbours' bands carry on, so
    they have no lip at all.

    Canonical art is the south-west square; facing picks the corner as for the
    corner piece: south=SW, east=SE, west=NW, north=NE. A square in the
    north-east corner goes between a strip facing east on the tile above and a
    strip facing north on the tile to the right.
    """
    if facing not in ("north", "east", "south", "west"):
        raise ValueError(f"unknown facing {facing!r}")
    size = canvas.final_width
    scale = size / TILE
    band = int(BAND * scale)
    key = max(2, int(EDGE * scale))
    mid = band // 2
    flip_x = facing in ("east", "north")
    flip_y = facing in ("west", "north")

    def rect(x0, y0, x1, y1, value, **kwargs):
        if flip_x:
            x0, x1 = size - x1, size - x0
        if flip_y:
            y0, y1 = size - y1, size - y0
        canvas.rect(x0, y0, x1, y1, value, **kwargs)

    # In the canonical corner the west neighbour carries a south strip and the
    # south neighbour a west strip; every coordinate below continues one of
    # theirs exactly, so the joint draws what the two strips would have.
    rect(0, size - band, band, size, LIGHT)
    rect(0, size - band - key, band + key, size - band, DARK)
    rect(band, size - band - key, band + key, size, DARK)
    rect(0, size - mid - 1, mid + 1, size - mid + 1, SHADE, alpha=150)
    rect(mid - 1, size - mid - 1, mid + 1, size, SHADE, alpha=150)
    # Vertical before horizontal, as everywhere in the family, and the light
    # re-decided from each lip's final side.
    for x0, y0, x1, y1, side in (
            (band - 2, size - band, band, size, "e"),
            (0, size - band, band, size - band + 2, "n")):
        if flip_x:
            x0, x1 = size - x1, size - x0
            side = {"w": "e", "e": "w"}.get(side, side)
        if flip_y:
            y0, y1 = size - y1, size - y0
            side = {"n": "s", "s": "n"}.get(side, side)
        lit = side in ("w", "n")
        canvas.rect(x0, y0, x1, y1, HILITE if lit else SHADE,
                    alpha=110 if lit else 120)


def draw_border_icon(canvas):
    """Menu icon: the band at full length through the centre.

    The band runs edge to edge through the centre because a band hugging the
    icon's edge is exactly what a small menu button loses. It is the button
    face only: the placement ghost comes from the def's own facings, since
    the engine builds a ghost from ``uiIconPath`` only for linked graphics and
    doors (a centred linked band was this piece's first design, which is where
    an older note here saying otherwise came from).
    """
    size = canvas.final_width
    centre = size // 2
    scale = size / TILE
    half = max(1, int(BAND * scale) // 2)
    edge = max(2, int(EDGE * scale))
    canvas.rect(0, centre - half - edge, size, centre + half + edge, DARK)
    canvas.rect(0, centre - half, size, centre + half, LIGHT)
    canvas.rect(0, centre - 1, size, centre + 1, SHADE, alpha=150)
    canvas.rect(0, centre - half, size, centre - half + 2, HILITE, alpha=110)
    canvas.rect(0, centre + half - 2, size, centre + half, SHADE, alpha=120)


def draw_border_double_icon(canvas):
    """Menu face for the runner: both rails, pulled off the icon's edges.

    Drawn horizontal to match ``defaultPlacingRot South``, with the keylines
    facing each other across the bed, so the button reads as a runner rather
    than as two unrelated strips.
    """
    inset = canvas.final_width // 8
    _band(canvas, "north", inset=inset)
    _band(canvas, "south", inset=inset)


def draw_border_inside_icon(canvas):
    """Menu face for the inside corner: the joint with its two strips.

    The texture alone is a small square in one corner of the tile, which a
    menu button shrinks to a few pixels. The icon draws the whole bend
    instead, centred: the south-west joint of ``defaultPlacingRot South`` with
    a stub of each neighbouring strip, keylines on the outside of the bend,
    where the room is. That is the one thing telling it apart from the
    corner's face, whose keylines are on the inside.
    """
    size = canvas.final_width
    scale = size / TILE
    band = int(BAND * scale)
    key = max(2, int(EDGE * scale))
    mid = band // 2
    p = size // 2 - band // 2          # the joint's square, centred
    q = p
    # One arm from the left edge into the joint, one from the joint down to
    # the bottom edge: the bend the square exists to complete.
    canvas.rect(0, q, p + band, q + band, LIGHT)
    canvas.rect(p, q, p + band, size, LIGHT)
    canvas.rect(0, q - key, p + band + key, q, DARK)
    canvas.rect(p + band, q - key, p + band + key, size, DARK)
    canvas.rect(0, q + mid - 1, p + mid + 1, q + mid + 1, SHADE, alpha=150)
    canvas.rect(p + mid - 1, q + mid - 1, p + mid + 1, size, SHADE, alpha=150)
    # Outer lips first (the wall side, stopping at each other), then the lips
    # on the room side, which run on around the bend.
    canvas.rect(p, q + band, p + 2, size, HILITE, alpha=110)
    canvas.rect(0, q + band - 2, p, q + band, SHADE, alpha=120)
    canvas.rect(p + band - 2, q, p + band, size, SHADE, alpha=120)
    canvas.rect(0, q, p + band, q + 2, HILITE, alpha=110)


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)

    def write(stem, draw, *args):
        canvas = Canvas(TILE)
        draw(canvas, *args)
        canvas.save_png(os.path.join(OUT_DIR, f"{stem}.png"))

    # The border: all four facings, west authored (see draw_border_edge).
    for facing in ("north", "south", "east", "west"):
        write(f"FloorBorderEdge_{facing}", draw_border_edge, facing)
    # The corner is chiral, so all four facings ship explicitly.
    for facing in ("north", "south", "east", "west"):
        write(f"FloorBorderCorner_{facing}", draw_border_corner, facing)
    # The runner: four files, two drawings. West is explicit on purpose.
    for facing in ("north", "south", "east", "west"):
        write(f"FloorBorderDouble_{facing}", draw_border_double, facing)
    # The inside corner is chiral like the corner; the end cap's west is
    # explicit for the runner's reason; the frame never rotates.
    for facing in ("north", "south", "east", "west"):
        write(f"FloorBorderInsideCorner_{facing}", draw_border_inside_corner, facing)
        write(f"FloorBorderEndCap_{facing}", draw_border_end_cap, facing)
    write("FloorBorderFrame", draw_border_frame)
    write("FloorBorder_MenuIcon", draw_border_icon)
    write("FloorBorderDouble_MenuIcon", draw_border_double_icon)
    write("FloorBorderInsideCorner_MenuIcon", draw_border_inside_icon)
