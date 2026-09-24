#!/usr/bin/env python3
"""Generate Neat Edges' marker ghost and UI icons.

None is ever drawn on the map. The marker texture exists purely as the
PLACEMENT GHOST and the build-menu icon — Building_InvisibleEdge no-ops Print.
That is not optional decoration: a fully transparent texture was tried first and
made the marker unplaceable, because the ghost, the rotation preview and the
selected thing all draw from the same graphic and there was nothing to aim with.

  EdgeOne        a band along the NORTH edge. Graphic_Single, so the building's
                 rotation spins it — which is exactly right, since `Rotation`
                 names the hugged edge. One file covers all four facings.
  AreaExpand     the painted area's paint tool: a crisp stone square over a
                 soft patch of soil, each with an interior trim, in vanilla's
                 area-tool style (see below).
  AreaClear      the same under vanilla's clear slash: its clear tool. Vanilla
                 marks every clear tool that way, so the pair reads like the
                 home and allowed-area pairs beside it.
  OverlayToggle  a grey ring (a tile hard on every side) for the bottom-right
                 overlay toggle, in vanilla's toggle style (see below).

There was an EdgeAll (the bare ring) for an all-sides marker; that marker was
removed when the painted area replaced it, and the ring lives on in the toggle.

The ghost is greyscale, because ghosts are tinted at draw time. The tools and
the toggle copy the vanilla icons they sit among instead.

THE TOGGLE ICON IS PIXEL ART, drawn to match its neighbours on the toggle row.
Sampled from a 1:1 screenshot of that row, every vanilla toggle is 24 px pixel
art: a flat grey fill of 124, a pure-black outline 1 px wide, 121 on each
shape's right column and bottom row, and about 2 px of transparent margin, with
no anti-aliasing anywhere. So the ring is drawn on that 24 px grid by hand
(FRAME below) and written at 2x, nearest-neighbour. At the normal UI scale the
engine draws it at 24 px, where each 2x2 block averages to exactly the art's
pixel; at 2x UI scale it stays sharp.

PNGs are written with zlib/struct rather than PIL, matching Fine
Establishments' devtools — no imaging dependency anywhere in the constellation.

    python3 devtools/make_edge_art.py
"""
import math
import os
import struct
import zlib

OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Textures", "NeatEdges")

SIZE = 256
THICK = 26      # band width in px
INSET = 6       # gap from the tile boundary, so the mark reads as inside it
TONE = 205      # mid-light grey


def chunk(kind, payload):
    body = kind + payload
    return (struct.pack(">I", len(payload)) + body
            + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF))


def save_rgba(path, width, height, pixel):
    """Writes an RGBA PNG from pixel(x, y) -> (r, g, b, a), and deletes any
    `.dds` beside it: the game reads a dds in preference to the PNG with no
    timestamp check, and a texture cache on this machine writes them into mod
    folders, so a stale one would silently pin the old art."""
    raw = bytearray()
    for y in range(height):
        raw.append(0)                       # filter: None
        for x in range(width):
            raw.extend(bytes(pixel(x, y)))

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
           + chunk(b"IEND", b""))

    with open(path, "wb") as f:
        f.write(png)

    dds = os.path.splitext(path)[0] + ".dds"
    if os.path.exists(dds):
        os.remove(dds)
        print(f"removed stale {os.path.basename(dds)}")
    print(f"wrote {os.path.basename(path)} ({len(png)} bytes)")


def write_png(path, pixel_on):
    on = (TONE, TONE, TONE, 255)
    off = (0, 0, 0, 0)
    save_rgba(path, SIZE, SIZE, lambda x, y: on if pixel_on(x, y) else off)


def one(x, y):
    # Row 0 is the TOP of the texture, which RimWorld draws as north.
    return INSET <= y < INSET + THICK


# ---- the area tools: vanilla's area-tool style ---------------------------

#: The tools sit with vanilla's area tools on the Zone tab, measured there at
#: 1:1 (the home and allowed-area pairs): flat colour, a 2 px pure-black
#: outline, and every "clear" tool is its expand tool under ONE shared slash.
#:
#: The picture follows Perspective: Paths' own area icon, the mod whose areas
#: this one adopts and yields to: a soft square behind at upper left, a crisp
#: one in front at lower right, laid out as its icon measures but drawn fresh.
#: Ours is two colours, soft sandy soil behind and cut stone in front, each
#: with its own interior trim, which is the mod's theme in miniature. Not the
#: area's cyan, which is the home area's colour in the menus, and warmer than
#: vanilla's cool shrink-zones grey (121, 123, 144), the other grey square.
#:
#: The slash is replicated from vanilla's measurements, not redrawn: red
#: (152, 27, 32) with its own 2 px black outline, climbing at 37.1 degrees from
#: lower left to upper right, about 9 px thick across the band, with vertically
#: cut ends, running on a 64 px icon from x 7 to 54 with its top edge at y 11
#: on the right.
AREA_GRID = 64          # vanilla draws its tool icons ~64 px on a 75 px button
AREA_SCALE = 2          # written at 2x, like the toggle
SUPERSAMPLE = 4         # per written pixel, so the slash's diagonals are smooth

BLACK = (0, 0, 0)
OUTLINE = 2
TRIM = 3                # the interior border, just inside each outline

SOFT_BOX = (7, 40)      # the back square on the 64 px icon, end exclusive
HARD_BOX = (24, 57)     # the front square
SOIL, SOIL_TRIM = (190, 152, 104), (146, 112, 74)
STONE, STONE_TRIM = (152, 148, 140), (110, 106, 100)
BLUR_SIGMA = 2.5        # icon px: the soft square fades over ~8 px

SLASH_RED = (152, 27, 32)
SLASH_X = (7.0, 54.0)       # its vertical ends
SLASH_TOP_AT_RIGHT = 11.0   # the band's top edge at its right end
SLASH_SLOPE = 0.756         # rise per px towards the right: tan(37.1 degrees)
SLASH_THICK = 11.0          # vertical extent of the red, ~9 px across the band
# A 2 px outline across a slanted edge spans more than 2 px vertically.
SLASH_GROW = OUTLINE / math.cos(math.atan(SLASH_SLOPE))


def _square_at(u, v, box, fill, trim):
    """A trimmed square's colour at (u, v) on the 64 px icon, or None: the
    outline, then the trim just inside it, then the fill."""
    lo, hi = box
    if not (lo <= u < hi and lo <= v < hi):
        return None
    edge = min(u - lo, v - lo, hi - u, hi - v)
    if edge < OUTLINE:
        return BLACK
    return trim if edge < OUTLINE + TRIM else fill


def _soft_layer():
    """The back square at the written size, outline and trim included, then
    Gaussian-blurred as one layer. Rows of (r, g, b, a), alpha 0..1, colour
    not premultiplied; the blur runs on premultiplied values."""
    side = AREA_GRID * AREA_SCALE
    layer = []
    for y in range(side):
        row = []
        for x in range(side):
            c = _square_at((x + 0.5) / AREA_SCALE, (y + 0.5) / AREA_SCALE,
                           SOFT_BOX, SOIL, SOIL_TRIM)
            row.append([0.0, 0.0, 0.0, 0.0] if c is None else [c[0], c[1], c[2], 1.0])
        layer.append(row)

    sigma = BLUR_SIGMA * AREA_SCALE
    radius = int(3 * sigma)
    kernel = [math.exp(-(i * i) / (2 * sigma * sigma)) for i in range(-radius, radius + 1)]
    total = sum(kernel)
    kernel = [k / total for k in kernel]

    def blur(src, horizontal):
        out = []
        for y in range(side):
            row = []
            for x in range(side):
                acc = [0.0, 0.0, 0.0, 0.0]
                for i, k in enumerate(kernel):
                    d = i - radius
                    sx, sy = (x + d, y) if horizontal else (x, y + d)
                    if 0 <= sx < side and 0 <= sy < side:
                        p = src[sy][sx]
                        a = p[3]
                        acc[0] += p[0] * a * k
                        acc[1] += p[1] * a * k
                        acc[2] += p[2] * a * k
                        acc[3] += a * k
                a = acc[3]
                row.append([acc[0] / a, acc[1] / a, acc[2] / a, a] if a > 0
                           else [0.0, 0.0, 0.0, 0.0])
            out.append(row)
        return out

    return blur(blur(layer, True), False)


def _slash_at(u, v):
    """The slash's colour at (u, v) on the 64 px icon, or None."""
    x0, x1 = SLASH_X
    top = SLASH_TOP_AT_RIGHT + (x1 - u) * SLASH_SLOPE
    if x0 <= u < x1 and top <= v < top + SLASH_THICK:
        return SLASH_RED
    if (x0 - OUTLINE <= u < x1 + OUTLINE
            and top - SLASH_GROW <= v < top + SLASH_THICK + SLASH_GROW):
        return BLACK
    return None


def write_area_icons():
    """Both tools, over one shared blurred soft layer: the crisp square and,
    on the clear tool, the slash, supersampled on top of it."""
    side = AREA_GRID * AREA_SCALE
    samples = SUPERSAMPLE * SUPERSAMPLE
    bottom = _soft_layer()

    def pixel_for(slashed):
        def pixel(x, y):
            base = bottom[y][x]
            r = g = b = a = 0.0
            for i in range(SUPERSAMPLE):
                for j in range(SUPERSAMPLE):
                    u = (x + (i + 0.5) / SUPERSAMPLE) / AREA_SCALE
                    v = (y + (j + 0.5) / SUPERSAMPLE) / AREA_SCALE
                    c = _slash_at(u, v) if slashed else None
                    if c is None:
                        c = _square_at(u, v, HARD_BOX, STONE, STONE_TRIM)
                    if c is None:
                        w = base[3]
                        r += base[0] * w
                        g += base[1] * w
                        b += base[2] * w
                        a += w
                    else:
                        r += c[0]
                        g += c[1]
                        b += c[2]
                        a += 1.0
            if a == 0:
                return (0, 0, 0, 0)
            return (round(r / a), round(g / a), round(b / a), round(255 * a / samples))
        return pixel

    save_rgba(os.path.join(OUT_DIR, "AreaExpand.png"), side, side, pixel_for(False))
    save_rgba(os.path.join(OUT_DIR, "AreaClear.png"), side, side, pixel_for(True))


# ---- the overlay toggle: vanilla's toggle style -------------------------

#: Sampled from vanilla's toggle row at 1:1: fill, its right/bottom shade, and
#: the outline. Change these only against a fresh sample of the game.
TOGGLE_PALETTE = {
    ".": (0, 0, 0, 0),
    "K": (0, 0, 0, 255),
    "G": (124, 124, 124, 255),
    "S": (121, 121, 121, 255),
}

#: The ring on vanilla's 24 px toggle grid: outline outside and in, a 4 px band
#: (3 px on the shaded sides, plus the shade), 2 px of margin like the rest.
FRAME = """
........................
........................
..KKKKKKKKKKKKKKKKKKKK..
..KGGGGGGGGGGGGGGGGGSK..
..KGGGGGGGGGGGGGGGGGSK..
..KGGGGGGGGGGGGGGGGGSK..
..KGGGGGGGGGGGGGGGGGSK..
..KGGGGKKKKKKKKKKGGGSK..
..KGGGGK........KGGGSK..
..KGGGGK........KGGGSK..
..KGGGGK........KGGGSK..
..KGGGGK........KGGGSK..
..KGGGGK........KGGGSK..
..KGGGGK........KGGGSK..
..KGGGGK........KGGGSK..
..KGGGGK........KGGGSK..
..KGGGGKKKKKKKKKKGGGSK..
..KGGGGGGGGGGGGGGGGGSK..
..KGGGGGGGGGGGGGGGGGSK..
..KGGGGGGGGGGGGGGGGGSK..
..KSSSSSSSSSSSSSSSSSSK..
..KKKKKKKKKKKKKKKKKKKK..
........................
........................
"""

TOGGLE_GRID = 24
TOGGLE_SCALE = 2


def write_pixel_art(path, art):
    rows = art.strip("\n").split("\n")
    assert len(rows) == TOGGLE_GRID and all(len(r) == TOGGLE_GRID for r in rows), \
        "pixel art must be %d x %d" % (TOGGLE_GRID, TOGGLE_GRID)
    side = TOGGLE_GRID * TOGGLE_SCALE
    save_rgba(path, side, side, lambda x, y: TOGGLE_PALETTE[
        rows[y // TOGGLE_SCALE][x // TOGGLE_SCALE]])


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    write_png(os.path.join(OUT_DIR, "EdgeOne.png"), one)
    write_area_icons()
    write_pixel_art(os.path.join(OUT_DIR, "OverlayToggle.png"), FRAME)
