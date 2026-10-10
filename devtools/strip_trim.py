#!/usr/bin/env python3
"""The strip trims' geometry, mirrored from Source/NeatEdges/StripTrimGeometry.cs.

Graphic_StripTrim lays one strip texture out as geometry: each shape is bands
("arms") along its tile's edges, cut at 45 degrees where two meet, and the
inside corner is the band square at one corner, split on its diagonal. The
diagonal lies along its tile's diagonal instead, and cuts each end by what
meets it there. This module computes the same polygons and UVs, so
``check_trims.py`` can render the shapes offline exactly as the game draws them.

A mirror, not an approximation: both sides are pinned to one golden file,
``strip_trim_geometry.txt``. ``check_trims.py`` fails if this module disagrees
with it, and the harness's ``trims.geometry`` case fails if the C# does. Every
value is written as a whole number of 1/4096ths and compared within one; the
straights' values at the golden cell and period are exact.

Coordinates are relative to the tile's centre, x east and z north, a tile
spanning -0.5 to 0.5, already turned to the world. Edges are 0 north, 1 east,
2 south, 3 west, and rotations count the same way.

THE STRIP holds two bands, or three. Rows top to bottom: the band on a north
edge, outer edge first; the band on a south edge, outer edge last; and, in a
three-band strip, the side-lit band for diagonals whose wall lies north-east or
south-west, outer edge first, so its edge row meets the south band's. West
arms take the north band and east arms the south, which keeps the light
north-west on every rotation. A strip may also hold several variants side by
side along its length, each a tile long (or a period's share); every tile's
stretch of a band picks one from a hash of the band's line, side and position,
so a run never repeats on a beat. Variants have to start and end alike. A
strip with a paint overlay holds its bands in its top half and the overlay, the
same bands mirrored, in its bottom half; a trim draws each polygon twice, once
from each half, the overlay on top.

THE DIAGONAL lives on a diagonal wall's cell, its band along the wall's face in
the open half and reaching past the cell into the corners of the two cells
beside it. Facing north its wall fills the north-west half; each rotation turns
that clockwise. Each end reads what else ends at that corner: the next
diagonal of a run shares a square joint; a straight band or another diagonal
turning there meets it in a mitre on the bisector. Where the band turns on the
inside of a corner the two overlap, and the diagonal prints a little higher so
its mitre wins; on the outside they leave a wedge, which the diagonal fills,
drawing the straight's share in the straight's own mapping. Straights never
read their neighbours.

    python3 devtools/strip_trim.py --write-golden    # after a deliberate change
"""

import math
import os
import sys

#: How far a band's quad reaches into its tile. StripTrimGeometry.Depth.
DEPTH = 0.25

#: The six shapes of the golden file's first block, which predates the rest.
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

#: The strip's bands, by which edge of the band faces the light.
NORTH, SOUTH, SIDE = 0, 1, 2

R2 = math.sqrt(0.5)
M32 = 0xFFFFFFFF

HERE = os.path.dirname(os.path.abspath(__file__))
GOLDEN = os.path.join(HERE, "strip_trim_geometry.txt")
GOLDEN_CELL = (3, 5)
GOLDEN_PERIOD = 1.0
SCALE = 4096


class Layout:
    """How a strip is laid out: the band length its whole width covers, in
    tiles; how many bands it holds; how many variants sit along it; and how
    many layers of those bands it stacks (2 with a paint overlay), with the
    layer this samples."""

    def __init__(self, period=1.0, bands=2, variants=1, layers=1, layer=0):
        self.period = period
        self.bands = bands
        self.variants = variants
        self.layers = layers
        self.layer = layer

    def overlay(self):
        """The same strip, sampling its paint overlay."""
        return Layout(self.period, self.bands, self.variants, self.layers, 1)

    def each_layer(self):
        """The strip once per layer it holds, bands first: the order a trim prints them."""
        return [Layout(self.period, self.bands, self.variants, self.layers, k) for k in range(self.layers)]


def turn(x, z, quarter_turns):
    """A point turned clockwise about the tile's centre."""
    for _ in range(quarter_turns & 3):
        x, z = z, -x
    return x, z


def period_of(width, height, bands=2, layers=1):
    """StripTrimGeometry.PeriodOf: the band length the strip's whole width
    covers, in tiles, at square texels."""
    rows = bands * max(1, layers)
    if height < rows:
        return 1.0
    return DEPTH * width / (height / float(rows))


def band_v(band, fraction, bands, layers=1, layer=0):
    """V for a point ``fraction`` of the way through a band's depth, 0 at its
    outer edge. A side band asked of a two-band strip reads the north band.
    The paint overlay is the bands mirrored into the strip's bottom half, so
    every band meets one like it where the halves join."""
    h = 1.0 / (bands * max(1, layers))
    if band == SIDE and bands < 3:
        band = NORTH
    if band == NORTH:
        v = 1.0 - fraction * h
    elif band == SOUTH:
        v = 1.0 - 2.0 * h + fraction * h
    else:
        v = 1.0 - 2.0 * h - fraction * h
    return 1.0 - v if layer == 1 else v


def variant_pick(line, side, segment, variants):
    """Which variant a band's stretch draws: a 32-bit hash, C# and Python alike."""
    h = (line * 0x9E3779B1) & M32
    h ^= (side * 0x85EBCA77) & M32
    h ^= (segment * 0xC2B2AE3D) & M32
    h ^= h >> 15
    h = (h * 0x2C1B3C6D) & M32
    h ^= h >> 12
    h = (h * 0x297A2D39) & M32
    h ^= h >> 15
    return h % variants


def band_u(origin, local, line, side, layout, segment):
    """U for a point ``local`` along a band from ``origin``, both in tiles.

    One variant: the strip repeats along the band, every line and side starting
    at one of 16 offsets so parallel runs and a runner's rails do not start in
    step. A whole number of repeats is taken off so the value stays small.
    Several: the point lies in stretch ``segment``, one variant long, which
    draws the variant the hash picks."""
    period = layout.period
    if layout.variants <= 1:
        offset = ((7 * line + 3 * side) & 15) * period / 16.0
        start = origin + offset
        return (start + local) / period - math.floor(start / period)
    stretch = period / layout.variants
    pick = variant_pick(line, side, segment, layout.variants)
    return (pick + (origin + local) / stretch - segment) / layout.variants


def straight_frame(edge, cell):
    """For a band on ``edge`` of ``cell``: (depth(x, z), origin, local(x, z),
    line, side, band). Local runs from the cell's west or south edge."""
    cx, cz = cell
    if edge == 0:
        return (lambda x, z: 0.5 - z), cx, (lambda x, z: 0.5 + x), cz + 1, 1, NORTH
    if edge == 1:
        return (lambda x, z: 0.5 - x), cz, (lambda x, z: 0.5 + z), cx + 1, 1, SOUTH
    if edge == 2:
        return (lambda x, z: z + 0.5), cx, (lambda x, z: 0.5 + x), cz, 0, SOUTH
    return (lambda x, z: x + 0.5), cz, (lambda x, z: 0.5 + z), cx, 0, NORTH


def uv(edge, cell, x, z, layout=None, segment=0):
    """Where a point on a straight band samples the strip. StripTrimGeometry.UV."""
    layout = layout or Layout(GOLDEN_PERIOD)
    depth, origin, local, line, side, band = straight_frame(edge, cell)
    return (band_u(origin, local(x, z), line, side, layout, segment),
            band_v(band, depth(x, z) / DEPTH, layout.bands, layout.layers, layout.layer))


def polygons(shape, rot):
    """Every polygon one straight-family trim draws, as ``(edge, [(x, z), ...])``,
    in the order the C# appends them. Each is convex and listed clockwise seen
    from above."""
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


# ---- polygons cut into a variant's stretches ---------------------------------

def _clip(points, f, keep_above):
    """Sutherland-Hodgman against f(p) >= 0 (or <= 0); f is affine."""
    out = []
    n = len(points)
    for i in range(n):
        p, q = points[i], points[(i + 1) % n]
        fp, fq = f(p), f(q)
        ip = fp >= -1e-9 if keep_above else fp <= 1e-9
        iq = fq >= -1e-9 if keep_above else fq <= 1e-9
        if ip:
            out.append(p)
        if ip != iq and abs(fp - fq) > 1e-12:
            s = fp / (fp - fq)
            cut = (p[0] + (q[0] - p[0]) * s, p[1] + (q[1] - p[1]) * s)
            if 1e-9 < s < 1 - 1e-9:
                out.append(cut)
    return out


def stretches(points, world, layout):
    """A polygon cut where a variant's stretch ends, as (segment, polygon)
    pairs; one pair, segment 0, when the strip holds a single variant.
    ``world(x, z)`` is the point's position along the band, in tiles."""
    if layout.variants <= 1:
        return [(0, points)]
    stretch = layout.period / layout.variants
    values = [world(x, z) / stretch for x, z in points]
    first = math.floor(min(values) + 1e-9)
    last = math.ceil(max(values) - 1e-9) - 1
    out = []
    for k in range(first, last + 1):
        piece = _clip(points, lambda p: world(*p) / stretch - k, True)
        piece = _clip(piece, lambda p: world(*p) / stretch - (k + 1), False)
        if len(piece) >= 3:
            out.append((k, piece))
    return out


# ---- the diagonal ------------------------------------------------------------

def diagonal_frame(rot):
    """The face's tail A, head B, unit along t and inward n, tile-local."""
    a = turn(-0.5, -0.5, rot)
    b = turn(0.5, 0.5, rot)
    t = turn(R2, R2, rot)
    n = turn(R2, -R2, rot)
    return a, b, t, n


def diagonal_band(rot):
    """Which band of the strip a diagonal draws: north with its wall to the
    north-west, south with it south-east, the side band otherwise."""
    return (NORTH, SIDE, SOUTH, SIDE)[rot & 3]


def diagonal_line(rot, cell):
    """(line, side) for a diagonal's offset and variant hash: the face's line,
    constant along a run, and which side of it the band lies."""
    cx, cz = cell
    if rot & 1 == 0:
        return cx - cz, rot >> 1
    return cx + cz + 1, rot >> 1


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1]


def _cross(a, b):
    return a[0] * b[1] - a[1] * b[0]


def _unit(x, z):
    n = math.hypot(x, z)
    return x / n, z / n


class End:
    """How one end of a diagonal is cut.

    ``direction`` is the cut line's unit direction, pointing into the band
    (None: square to the diagonal). ``wedge``, on the outside of a turn into a
    straight, is that straight's (edge, cell) so the diagonal can fill its
    share of the gap in the straight's mapping."""

    def __init__(self, kind="square", direction=None, inner=False, wedge=None):
        self.kind = kind
        self.direction = direction
        self.inner = inner
        self.wedge = wedge

    def key(self):
        if self.direction is None:
            return self.kind
        return f"{self.kind}:{self.direction[0]:+.4f},{self.direction[1]:+.4f}:{int(self.inner)}"


SQUARE = End()


def _corner(cell, x, z):
    """A tile-local corner as whole world coordinates."""
    return (int(round(cell[0] + 0.5 + x)), int(round(cell[1] + 0.5 + z)))


def offered_ends(shape, rot, cell):
    """The band ends a piece offers a diagonal, as (tail, head, along, square
    at tail, square at head, kind, edge). A band runs with its inside on its
    right: tail and head are world corners and along an integer direction."""
    out = []
    if shape == "Diagonal":
        a, b, t, n = diagonal_frame(rot)
        along = (int(round(t[0] / R2)), int(round(t[1] / R2)))
        out.append((_corner(cell, *a), _corner(cell, *b), along, True, True, "Diagonal", None))
        return out
    if shape not in ARMS:
        return out
    arms = ARMS[shape]
    edges = 0
    for arm in arms:
        edges |= 1 << ((rot + arm) & 3)
    for arm in arms:
        edge = (rot + arm) & 3
        tail = _corner(cell, *turn(-0.5, 0.5, edge))
        head = _corner(cell, *turn(0.5, 0.5, edge))
        along = turn(1, 0, edge)
        along = (int(along[0]), int(along[1]))
        tail_square = not edges & (1 << ((edge + 3) & 3))
        head_square = not edges & (1 << ((edge + 1) & 3))
        out.append((tail, head, along, tail_square, head_square, shape, edge))
    return out


def _end(t_in, t_out, n, partner_kind, partner_edge, partner_cell):
    """The cut where a band arriving along ``t_in`` turns to leave along ``t_out``
    (integer directions); ``n`` is this diagonal's inward normal."""
    if t_in == t_out:
        return SQUARE
    ui, uo = _unit(*t_in), _unit(*t_out)
    if _dot(ui, uo) < -1e-9:
        return SQUARE                      # a turn sharper than a right angle
    m = _unit(uo[0] - ui[0], uo[1] - ui[1])
    if _dot(m, n) < 0:
        m = (-m[0], -m[1])
    inner = _cross(ui, uo) < 0
    wedge = None
    if not inner and partner_kind != "Diagonal":
        wedge = (partner_edge, partner_cell)
    return End("mitre", m, inner, wedge)


def diagonal_ends(cell, rot, pieces_at):
    """The two ends of a diagonal at ``cell``, read from the pieces around it.
    ``pieces_at(cell)`` lists (shape, rotation) for the trims standing there.
    Returns (end at the tail A, end at the head B)."""
    a, b, t, n = diagonal_frame(rot)
    tail, head = _corner(cell, *a), _corner(cell, *b)
    along = (int(round(t[0] / R2)), int(round(t[1] / R2)))
    at_tail, at_head = [], []
    for dx in (-1, 0, 1):
        for dz in (-1, 0, 1):
            q = (cell[0] + dx, cell[1] + dz)
            for shape, prot in pieces_at(q):
                for p_tail, p_head, p_along, p_ts, p_hs, kind, edge in offered_ends(shape, prot, q):
                    if p_ts and p_tail == head:
                        at_head.append((p_along, kind, edge, q))
                    if p_hs and p_head == tail:
                        at_tail.append((p_along, kind, edge, q))

    def rank(entry, this_along):
        p_along, kind = entry[0], entry[1]
        if p_along == this_along:
            return 0
        return 1 if kind == "Diagonal" else 2

    end_b = SQUARE
    if at_head:
        best = min(at_head, key=lambda e: rank(e, along))
        end_b = _end(along, best[0], n, best[1], best[2], best[3])
    end_a = SQUARE
    if at_tail:
        best = min(at_tail, key=lambda e: rank(e, along))
        end_a = _end(best[0], along, n, best[1], best[2], best[3])
    return end_a, end_b


def _clockwise(points):
    area = 0.0
    for i in range(len(points)):
        x0, z0 = points[i]
        x1, z1 = points[(i + 1) % len(points)]
        area += x0 * z1 - x1 * z0
    return points if area <= 0 else list(reversed(points))


def diagonal_polygons(rot, end_a=SQUARE, end_b=SQUARE, cell=(0, 0)):
    """What a diagonal draws, as (frame, [(x, z), ...]) in the order the C#
    appends them: its band first, then any wedge. ``frame`` is "diagonal" or,
    for a wedge, the straight's ("edge", edge, cell offset from this one)."""
    a, b, t, n = diagonal_frame(rot)
    da = end_a.direction or n
    db = end_b.direction or n
    sa = DEPTH / _dot(da, n)
    sb = DEPTH / _dot(db, n)
    band = [a, b, (b[0] + sb * db[0], b[1] + sb * db[1]), (a[0] + sa * da[0], a[1] + sa * da[1])]
    out = [("diagonal", band)]
    for point, end, inner_corner in ((a, end_a, band[3]), (b, end_b, band[2])):
        if end.wedge is None:
            continue
        edge, partner = end.wedge
        ns = turn(0.0, -1.0, edge)                       # the straight's inward normal
        square = (point[0] + DEPTH * ns[0], point[1] + DEPTH * ns[1])
        offset = (partner[0] - cell[0], partner[1] - cell[1])
        out.append((("edge", edge, offset), _clockwise([point, square, inner_corner])))
    return out


def diagonal_uv(rot, cell, x, z, layout, segment=0):
    """Where a point on a diagonal's band samples the strip."""
    a, b, t, n = diagonal_frame(rot)
    depth = (x - a[0]) * n[0] + (z - a[1]) * n[1]
    line, side = diagonal_line(rot, cell)
    origin = cell[0] * t[0] + cell[1] * t[1]
    local = (0.5 + x) * t[0] + (0.5 + z) * t[1]
    return (band_u(origin, local, line, side, layout, segment),
            band_v(diagonal_band(rot), depth / DEPTH, layout.bands, layout.layers, layout.layer))


def diagonal_world(rot, cell):
    """A point's position along a diagonal's band, in tiles from the world origin."""
    t = diagonal_frame(rot)[2]
    origin = cell[0] * t[0] + cell[1] * t[1]
    return lambda x, z: origin + (0.5 + x) * t[0] + (0.5 + z) * t[1]


def straight_world(edge, cell):
    _, origin, local, _, _, _ = straight_frame(edge, cell)
    return lambda x, z: origin + local(x, z)


def textured(shape, rot, cell, layout, ends=(SQUARE, SQUARE)):
    """Every polygon a trim prints, cut into its variants' stretches, with the
    UV of each vertex: [(points, uvs), ...], the C#'s order."""
    out = []
    if shape == "Diagonal":
        for frame, points in diagonal_polygons(rot, ends[0], ends[1], cell):
            if frame == "diagonal":
                world = diagonal_world(rot, cell)
                for segment, piece in stretches(points, world, layout):
                    out.append((piece, [diagonal_uv(rot, cell, x, z, layout, segment) for x, z in piece]))
            else:
                _, edge, (ox, oz) = frame
                partner = (cell[0] + ox, cell[1] + oz)
                moved = [(x - ox, z - oz) for x, z in points]
                world = straight_world(edge, partner)
                for segment, piece in stretches(moved, world, layout):
                    out.append(([(x + ox, z + oz) for x, z in piece],
                                [uv(edge, partner, x, z, layout, segment) for x, z in piece]))
        return out
    for edge, points in polygons(shape, rot):
        world = straight_world(edge, cell)
        for segment, piece in stretches(points, world, layout):
            out.append((piece, [uv(edge, cell, x, z, layout, segment) for x, z in piece]))
    return out


def inside(points, x, z):
    """Whether (x, z) lies in a clockwise convex polygon, edges included."""
    n = len(points)
    for i in range(n):
        ax, az = points[i]
        bx, bz = points[(i + 1) % n]
        if (bx - ax) * (z - az) - (bz - az) * (x - ax) > 1e-12:
            return False
    return True


# ---- the golden file ---------------------------------------------------------

#: Scenes whose diagonals the golden file pins, ends read from the neighbours:
#: (name, [(shape, rotation, cell), ...]). Cells sit near (10, 10).
SCENES = (
    ("lone", [("Diagonal", 0, (10, 10))]),
    ("lone-east", [("Diagonal", 1, (10, 10))]),
    ("lone-south", [("Diagonal", 2, (10, 10))]),
    ("lone-west", [("Diagonal", 3, (10, 10))]),
    ("run", [("Diagonal", 0, (10, 10)), ("Diagonal", 0, (11, 11))]),
    # An octagonal room's north-west corner, seen from inside: a west wall, the
    # diagonal, a north wall. Both joins turn on the inside.
    ("octagon-inside", [("Straight", 3, (10, 9)), ("Diagonal", 0, (10, 10)),
                        ("Straight", 0, (11, 10))]),
    # The same corner from outside: both joins turn on the outside.
    ("octagon-outside", [("Straight", 0, (9, 9)), ("Diagonal", 0, (10, 10)),
                         ("Straight", 3, (11, 11))]),
    # A diamond's tip, from inside and from outside.
    ("tip-inside", [("Diagonal", 0, (10, 10)), ("Diagonal", 1, (11, 10))]),
    ("tip-outside", [("Diagonal", 2, (10, 11)), ("Diagonal", 3, (11, 11))]),
    # A corner piece offers the square end of an arm, never its own mitre.
    ("corner-piece", [("Corner", 0, (11, 10)), ("Diagonal", 0, (10, 10))]),
    ("corner-mitred", [("Corner", 3, (11, 10)), ("Diagonal", 0, (10, 10))]),
    # A turn sharper than a right angle stays square.
    ("sharp", [("Diagonal", 0, (10, 10)), ("Straight", 2, (10, 11))]),
)


def scene_pieces(scene):
    table = {}
    for shape, rot, cell in scene:
        table.setdefault(cell, []).append((shape, rot))
    return lambda cell: table.get(cell, [])


def _scaled(values):
    return " ".join(str(int(round(value * SCALE))) for value in values)


def golden_lines(cell=GOLDEN_CELL, period=GOLDEN_PERIOD):
    """The golden file: every straight-family shape and rotation on a two-band
    strip; the straight on a three-band strip and on one with four variants;
    every diagonal of every scene, with its ends read from its neighbours, on
    both; and, on a four-variant strip with a paint overlay, the straight and
    the four lone diagonals from each layer. Each line lists every vertex as
    x z u v, times 4096."""
    lines = []
    plain = Layout(period, 2, 1)
    for shape in SHAPES:
        for rot, name in enumerate(ROTATIONS):
            values = []
            for edge, points in polygons(shape, rot):
                for x, z in points:
                    values += [x, z, *uv(edge, cell, x, z, plain)]
            lines.append(f"{shape} {name} " + _scaled(values))
    three = Layout(period, 3, 1)
    varied = Layout(4.0, 3, 4)
    for label, layout in (("Bands3", three), ("Variants4", varied)):
        for rot, name in enumerate(ROTATIONS):
            values = []
            for points, uvs in textured("Straight", rot, cell, layout):
                for (x, z), (u, v) in zip(points, uvs):
                    values += [x, z, u, v]
            lines.append(f"{label} Straight {name} " + _scaled(values))
    for label, layout in (("Bands3", three), ("Variants4", varied)):
        for name, scene in SCENES:
            pieces_at = scene_pieces(scene)
            for index, (shape, rot, at) in enumerate(scene):
                if shape != "Diagonal":
                    continue
                ends = diagonal_ends(at, rot, pieces_at)
                values = []
                for points, uvs in textured("Diagonal", rot, at, layout, ends):
                    for (x, z), (u, v) in zip(points, uvs):
                        values += [x, z, u, v]
                lines.append(f"{label} Scene {name} {index} {ends[0].kind}{'-in' if ends[0].inner else ''} "
                             f"{ends[1].kind}{'-in' if ends[1].inner else ''} " + _scaled(values))
    for layout in Layout(4.0, 3, 4, 2).each_layer():
        label = f"Overlay{layout.layer}"
        for rot, name in enumerate(ROTATIONS):
            values = []
            for points, uvs in textured("Straight", rot, cell, layout):
                for (x, z), (u, v) in zip(points, uvs):
                    values += [x, z, u, v]
            lines.append(f"{label} Straight {name} " + _scaled(values))
        for name, scene in SCENES[:4]:
            shape, rot, at = scene[0]
            values = []
            for points, uvs in textured(shape, rot, at, layout):
                for (x, z), (u, v) in zip(points, uvs):
                    values += [x, z, u, v]
            lines.append(f"{label} Scene {name} 0 square square " + _scaled(values))
    return lines


def golden_matches(mine, expected):
    """Whether two golden listings agree, every number within one 1/4096th."""
    if len(mine) != len(expected):
        return False
    for a, b in zip(mine, expected):
        wa, wb = a.split(), b.split()
        if len(wa) != len(wb):
            return False
        for x, y in zip(wa, wb):
            if x == y:
                continue
            try:
                if abs(int(x) - int(y)) > 1:
                    return False
            except ValueError:
                return False
    return True


if __name__ == "__main__":
    if "--write-golden" in sys.argv:
        with open(GOLDEN, "w") as out:
            out.write("\n".join(golden_lines()) + "\n")
        print("wrote", os.path.relpath(GOLDEN, os.path.dirname(HERE)))
    else:
        print("\n".join(golden_lines()))
