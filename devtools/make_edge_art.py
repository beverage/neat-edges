#!/usr/bin/env python3
"""Generate Neat Edges' placeholder textures.

None is ever drawn on the map. The marker texture exists purely as the
PLACEMENT GHOST and the build-menu icon — Building_InvisibleEdge no-ops Print.
That is not optional decoration: a fully transparent texture was tried first and
made the marker unplaceable, because the ghost, the rotation preview and the
selected thing all draw from the same graphic and there was nothing to aim with.

  EdgeOne     a band along the NORTH edge. Graphic_Single, so the building's
              rotation spins it — which is exactly right, since `Rotation`
              names the hugged edge. One file covers all four facings.
  AreaExpand  a ring with a plus: the painted area's paint tool. The ring
              says what a painted tile is (every edge hard); the plus says add.
  AreaClear   the ring with a minus: its clear tool.

There was an EdgeAll (the bare ring) for an all-sides marker; that marker was
removed when the painted area replaced it, and the ring lives on in the tools.

Greyscale by house convention: ghosts are tinted at draw time, and the two tool
icons match the marker they sit beside on the Floors tab.

PNGs are written with zlib/struct rather than PIL, matching Fine
Establishments' devtools — no imaging dependency anywhere in the constellation.

    python3 devtools/make_edge_art.py
"""
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


def write_png(path, pixel_on):
    raw = bytearray()
    for y in range(SIZE):
        raw.append(0)                       # filter: None
        for x in range(SIZE):
            if pixel_on(x, y):
                raw.extend(bytes((TONE, TONE, TONE, 255)))
            else:
                raw.extend(b"\x00\x00\x00\x00")

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
           + chunk(b"IEND", b""))

    with open(path, "wb") as f:
        f.write(png)
    print(f"wrote {os.path.basename(path)} ({len(png)} bytes)")


def one(x, y):
    # Row 0 is the TOP of the texture, which RimWorld draws as north.
    return INSET <= y < INSET + THICK


def ring(x, y):
    d = min(x, y, SIZE - 1 - x, SIZE - 1 - y)
    return INSET <= d < INSET + THICK


# The sign inside the tool icons: bars as thick as the ring, reaching a bit
# under halfway to it, so the sign reads at gizmo size without touching it.
SIGN_REACH = 0.24 * SIZE


def _bar(along, across):
    centre = SIZE / 2
    return (abs(along + 0.5 - centre) < SIGN_REACH
            and abs(across + 0.5 - centre) < THICK / 2)


def minus(x, y):
    return _bar(x, y)


def plus(x, y):
    return _bar(x, y) or _bar(y, x)


def area_expand(x, y):
    return ring(x, y) or plus(x, y)


def area_clear(x, y):
    return ring(x, y) or minus(x, y)


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    write_png(os.path.join(OUT_DIR, "EdgeOne.png"), one)
    write_png(os.path.join(OUT_DIR, "AreaExpand.png"), area_expand)
    write_png(os.path.join(OUT_DIR, "AreaClear.png"), area_clear)
