#!/usr/bin/env python3
"""Menu icons rendered from a trim family's strip, one per shape, so a new
family costs one drawing. Imported, not run.

Each icon is the shape laid out from the strip by ``strip_trim.py``'s geometry
and rendered by ``strip_render.py``, framed for a small button the way the
hand-drawn floor border icons are: the straight's band through the centre, the
runner's rails pulled in off the edges, the inside corner as the whole bend it
completes, the diagonal corner to corner. The corner, end cap and frame show
their default-rotation facing, as their drawn icons do.
"""

import strip_render
import strip_trim
from strip_trim import DEPTH, Layout

#: Icon size in pixels. A build button draws its icon at about 75, so 128 keeps
#: a margin for zoomed UI at a quarter of a 256 icon's video memory: with 28
#: icons that is about 0.6 MB rather than 2.4.
ICON = 128

#: Each shape's icon stem suffix, after the family's stem. The runner keeps
#: the floor border's historical "Double".
SUFFIX = {
    "Straight": "",
    "Corner": "Corner",
    "InsideCorner": "InsideCorner",
    "Runner": "Double",
    "EndCap": "EndCap",
    "Frame": "Frame",
    "Diagonal": "Diagonal",
}


def _straight_band(edge, shift_x, shift_z, layout):
    """A straight band moved by (shift_x, shift_z), sampled as if it had not
    moved, so the art reads as the band at another place in the tile."""
    out = []
    for points, uvs in strip_trim.textured("Straight", edge, (0, 0), layout):
        out.append(([(x + shift_x, z + shift_z) for x, z in points], uvs))
    return out


def pieces_for(shape, layout):
    """(window, [(points, uvs), ...]) for one shape's icon."""
    window = (-0.5, -0.5, 0.5, 0.5)
    if shape == "Straight":
        # the north band, its quad centred on the tile
        return window, _straight_band(0, 0.0, -(0.5 - DEPTH / 2), layout)
    if shape == "Runner":
        inset = 1 / 8
        return window, (_straight_band(0, 0.0, -inset, layout) + _straight_band(2, 0.0, inset, layout))
    if shape == "InsideCorner":
        # the south-west square, between the south band of the tile to its west
        # and the west band of the tile to its south, centred
        pieces = []
        for piece_shape, rot, cell in (("Straight", 2, (-1, 0)), ("Straight", 3, (0, -1)),
                                       ("InsideCorner", 2, (0, 0))):
            for points, uvs in strip_trim.textured(piece_shape, rot, cell, layout):
                pieces.append(([(x + cell[0], z + cell[1]) for x, z in points], uvs))
        centre = -0.5 + DEPTH / 2
        return (centre - 0.5, centre - 0.5, centre + 0.5, centre + 0.5), pieces
    if shape == "Diagonal":
        # rot 2, the default placing rotation; the band centred on the tile's
        # diagonal and run on past both ends to the icon's corners
        rot = 2
        a, b, t, n = strip_trim.diagonal_frame(rot)
        reach = 0.6
        quad = [(a[0] - reach * t[0], a[1] - reach * t[1]), (b[0] + reach * t[0], b[1] + reach * t[1]),
                (b[0] + reach * t[0] + DEPTH * n[0], b[1] + reach * t[1] + DEPTH * n[1]),
                (a[0] - reach * t[0] + DEPTH * n[0], a[1] - reach * t[1] + DEPTH * n[1])]
        shift = (-DEPTH / 2 * n[0], -DEPTH / 2 * n[1])
        pieces = []
        world = strip_trim.diagonal_world(rot, (0, 0))
        for segment, piece in strip_trim.stretches(quad, world, layout):
            uvs = [strip_trim.diagonal_uv(rot, (0, 0), x, z, layout, segment) for x, z in piece]
            pieces.append(([(x + shift[0], z + shift[1]) for x, z in piece], uvs))
        return window, pieces
    rot = {"Corner": 2, "EndCap": 2, "Frame": 0}[shape]
    return window, strip_trim.textured(shape, rot, (0, 0), layout)


def render(shape, strip, layout, size=ICON):
    """RGBA rows of one shape's icon: every layer the strip holds, the paint
    overlay over the bands, all in one grey, as the button tints it."""
    image = None
    for each in layout.each_layer():
        window, pieces = pieces_for(shape, each)
        image = image or strip_render.Image(size, size, window)
        for points, uvs in pieces:
            image.draw(points, uvs, strip)
    return image.rows()


def layout_of(strip, bands, variants, layers=1):
    width, height, _ = strip
    return Layout(strip_trim.period_of(width, height, bands, layers), bands, variants, layers)
