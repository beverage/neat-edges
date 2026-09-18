#!/usr/bin/env python3
"""Generate Neat Edges' two placeholder textures.

Neither is ever drawn on the map — Building_InvisibleEdge no-ops Print — so
these exist purely as the PLACEMENT GHOST and the build-menu icon. That is not
optional decoration: a fully transparent texture was tried first and made the
marker unplaceable, because the ghost, the rotation preview and the selected
thing all draw from the same graphic and there was nothing to aim with.

  EdgeOne  a band along the NORTH edge. Graphic_Single, so the building's
           rotation spins it — which is exactly right, since `Rotation` names
           the hugged edge. One file covers all four facings.
  EdgeAll  a ring. The all-sides marker cannot borrow a one-sided band without
           misdescribing itself in the menu.

Greyscale by house convention: ghosts are tinted at draw time.

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


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    write_png(os.path.join(OUT_DIR, "EdgeOne.png"), one)
    write_png(os.path.join(OUT_DIR, "EdgeAll.png"), ring)
