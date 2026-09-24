#!/usr/bin/env python3
"""The trim pieces' art: the floor border, its mitred corner, the double-rail
runner, and the two menu icons. Drawing plus manifest; run it to regenerate.

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
Graphic_Multi with three textures, north/south/east; west auto-mirrors east,
which is CORRECT here, since a right-edge strip flipped horizontally is a
left-edge strip. No atlas and so no 75% crop: authored pixels are screen truth,
and the strip must bleed to its tile edge exactly.

THE CORNER owns both bands of an L in one image, which is what makes a clean
join possible at all: two separate strips can never meet properly, since
neither texture knows the other. It is chiral, so all four facings ship.

THE RUNNER owns both opposite edges (`| |`, or `=` turned a quarter), so a
corridor gets its two rails from one drag. Four textures, two drawings, and
west must be AUTHORED rather than mirrored; see ``draw_border_double``.

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


def draw_border_edge(canvas, facing):
    """The strip hugging one edge, full length. ``facing``: north/south/east.

    West is refused rather than drawn: the def ships three textures and the
    engine auto-mirrors east, which is correct for a single strip.
    """
    if facing not in ("north", "south", "east"):
        raise ValueError(f"unknown facing {facing!r} (west auto-mirrors east)")
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
    piece in the family. Mirroring is right for the single strip; it is wrong
    the moment a piece owns both rails.

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


def draw_border_icon(canvas):
    """Menu icon AND placement ghost: the band at full length through the centre.

    Not cosmetic. The ghost is built from ``uiIconPath``, and with the field
    unset merely SELECTING the designator throws every frame. The band runs
    edge to edge through the centre because a band hugging the icon's edge is
    exactly what a small menu button loses.
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


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)

    def write(stem, draw, *args):
        canvas = Canvas(TILE)
        draw(canvas, *args)
        canvas.save_png(os.path.join(OUT_DIR, f"{stem}.png"))

    # The border: west auto-mirrors east, correctly.
    for facing in ("north", "south", "east"):
        write(f"FloorBorderEdge_{facing}", draw_border_edge, facing)
    # The corner is chiral, so all four facings ship explicitly.
    for facing in ("north", "south", "east", "west"):
        write(f"FloorBorderCorner_{facing}", draw_border_corner, facing)
    # The runner: four files, two drawings. West is explicit on purpose.
    for facing in ("north", "south", "east", "west"):
        write(f"FloorBorderDouble_{facing}", draw_border_double, facing)
    write("FloorBorder_MenuIcon", draw_border_icon)
    write("FloorBorderDouble_MenuIcon", draw_border_double_icon)
