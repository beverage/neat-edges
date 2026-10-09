#!/usr/bin/env python3
"""The strip trims' geometry, mirrored from Source/NeatEdges/StripTrimGeometry.cs.

Graphic_StripTrim lays one strip texture out as geometry: each shape is bands
("arms") along its tile's edges, cut at 45 degrees where two meet, and the
inside corner is the band square at one corner, split on its diagonal. This
module computes the same polygons and UVs, so ``check_trims.py`` can render the
shapes offline exactly as the game draws them and compare them with the art
they replaced.

A mirror, not an approximation: both sides are pinned to one golden file,
``strip_trim_geometry.txt``. ``check_trims.py`` fails if this module disagrees
with it, and the harness's ``trims.geometry`` case fails if the C# does. At the
golden file's cell and period every coordinate and UV is a multiple of 1/4096,
so float and double agree exactly and the file can hold integers.

Coordinates are relative to the tile's centre, x east and z north, a tile
spanning -0.5 to 0.5, already turned to the world. Edges are 0 north, 1 east,
2 south, 3 west, and rotations count the same way.

    python3 devtools/strip_trim.py --write-golden    # after a deliberate change
"""

import math
import os
import sys

#: How far a band's quad reaches into its tile. StripTrimGeometry.Depth.
DEPTH = 0.25

SHAPES = ("Straight", "Corner", "InsideCorner", "Runner", "EndCap", "Frame")
ROTATIONS = ("North", "East", "South", "West")

#: The edges each shape draws bands along, in quarter-turns clockwise from the
#: thing's Rotation. StripTrimGeometry.Arms.
ARMS = {
    "Straight": (0,),
    "Runner": (0, 2),
    "Corner": (0, 1),
    "EndCap": (3, 0, 1),
    "Frame": (0, 1, 2, 3),
}

HERE = os.path.dirname(os.path.abspath(__file__))
GOLDEN = os.path.join(HERE, "strip_trim_geometry.txt")
GOLDEN_CELL = (3, 5)
GOLDEN_PERIOD = 1.0
SCALE = 4096


def turn(x, z, quarter_turns):
    """A point turned clockwise about the tile's centre."""
    for _ in range(quarter_turns & 3):
        x, z = z, -x
    return x, z


def polygons(shape, rot):
    """Every polygon one trim draws, as ``(edge, [(x, z), ...])``, in the order
    the C# appends them. Each is convex and listed clockwise seen from above."""
    h, d = 0.5, DEPTH
    if shape == "InsideCorner":
        north, east = rot & 3, (rot + 1) & 3
        return [
            (north, [turn(h, h, rot), turn(h, h - d, rot), turn(h - d, h - d, rot)]),
            (east, [turn(h, h, rot), turn(h - d, h - d, rot), turn(h - d, h, rot)]),
        ]
    arms = ARMS[shape]
    edges = 0
    for arm in arms:
        edges |= 1 << ((rot + arm) & 3)
    out = []
    for arm in arms:
        edge = (rot + arm) & 3
        cut_west = d if edges & (1 << ((edge + 3) & 3)) else 0.0
        cut_east = d if edges & (1 << ((edge + 1) & 3)) else 0.0
        out.append((edge, [turn(-h, h, edge), turn(h, h, edge),
                           turn(h - cut_east, h - d, edge), turn(-h + cut_west, h - d, edge)]))
    return out


def uv(edge, cell, x, z, period):
    """Where a point samples the strip. StripTrimGeometry.UV, line for line."""
    cx, cz = cell
    if edge == 0:
        depth, origin, along, line, side = 0.5 - z, cx, x, cz + 1, 1
    elif edge == 1:
        depth, origin, along, line, side = 0.5 - x, cz, z, cx + 1, 1
    elif edge == 2:
        depth, origin, along, line, side = z + 0.5, cx, x, cz, 0
    else:
        depth, origin, along, line, side = x + 0.5, cz, z, cx, 0
    half = depth / DEPTH * 0.5
    v = 1.0 - half if edge in (0, 3) else half
    offset = ((7 * line + 3 * side) & 15) * period / 16.0
    start = origin + offset
    u = (start + 0.5 + along) / period - math.floor(start / period)
    return u, v


def period_of(width, height):
    """StripTrimGeometry.PeriodOf: the band length one repeat covers, in tiles."""
    if height < 2:
        return 1.0
    return DEPTH * width / (height / 2.0)


def inside(points, x, z):
    """Whether (x, z) lies in a clockwise convex polygon, edges included."""
    n = len(points)
    for i in range(n):
        ax, az = points[i]
        bx, bz = points[(i + 1) % n]
        if (bx - ax) * (z - az) - (bz - az) * (x - ax) > 1e-12:
            return False
    return True


def golden_lines(cell=GOLDEN_CELL, period=GOLDEN_PERIOD):
    """One line per shape and rotation: every vertex as x z u v, times 4096."""
    lines = []
    for shape in SHAPES:
        for rot, name in enumerate(ROTATIONS):
            values = []
            for edge, points in polygons(shape, rot):
                for x, z in points:
                    u, v = uv(edge, cell, x, z, period)
                    values += [x, z, u, v]
            scaled = [value * SCALE for value in values]
            if any(s != int(s) for s in scaled):
                raise ValueError(f"{shape} {name}: a value is not a multiple of 1/{SCALE}, "
                                 "so float and double could disagree")
            lines.append(f"{shape} {name} " + " ".join(str(int(s)) for s in scaled))
    return lines


if __name__ == "__main__":
    if "--write-golden" in sys.argv:
        with open(GOLDEN, "w") as out:
            out.write("\n".join(golden_lines()) + "\n")
        print("wrote", os.path.relpath(GOLDEN, os.path.dirname(HERE)))
    else:
        print("\n".join(golden_lines()))
