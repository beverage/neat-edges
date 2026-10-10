#!/usr/bin/env python3
"""The trim styles beyond the floor border: inlay, vine and pebbles.

Each style is drawn once, as a height map, an albedo and a coverage across one
band and along its length, then lit three ways: from the band's outer edge (the
band on a north edge), from its inner side (on a south edge) and side-on (a
diagonal whose wall lies north-east or south-west). The three are written as
one three-band strip (strip_trim.py has the layout), with seven menu icons
laid out from it by trim_icons.py.

LIGHT RUNS ACROSS THE BAND ONLY. One band serves north and west edges, and the
light comes from the north-west, so on a north edge it falls from the band's
left end and on a west edge from its right. A bump lit from along the band
would be lit from the wrong end on half the edges; lit across, it is right on
all of them.

A BORDER COVERS THE EDGE. The vine and the pebbles are opaque from the tile's
very edge, as the floor border is, so the line where a floor meets whatever
lies past it is under the trim, and the trim reads as a hard border rather
than an ornament beside one. The vine's lines are set in from both sides and
its leaves reach past them, so on the floor's side its outline is leaves. The
inlay is the exception by design: it is set into the floor, which shows on
either side of it.

VARIANTS. The vine and the pebbles are four one-tile variants side by side,
and the renderer gives every tile of band one of them by a hash, so neither
repeats on a beat. Every variant starts and ends alike: the vine's stem leaves
a tile at the height and slope it entered it, and the one leaf that crosses a
tile's end is the same leaf in every variant; the pebbles leave grout at both
ends.

PAINT ON THE VINE. The vine itself, its stem and leaves, is drawn apart from
the rest and written below it as a paint overlay (TrimPiece.paintOverlay): the
same three bands, mirrored into the strip's bottom half. The game prints the
overlay over the bands in the paint colour and the bands in the stuff's, so
green paint on a wooden vine is a green vine on a wooden border. The berries
stay with the bands, and the vine goes to the overlay wherever it stands higher
than them, which is where it showed when everything was carved as one, so
unpainted the two layers draw the vine as it was; the vine's shadows stay with
the bands.

Needs numpy, so it runs apart from make_trim_art.py:

    uv run --with numpy python3 devtools/make_trim_styles.py
    python3 devtools/check_trims.py
"""

import math
import os

import numpy as np

import trim_icons
from make_trim_art import OUT_DIR
from trim_kit import TILE, write_rgba_png

ROWS = TILE // 4                 # a band: a quarter tile
SS = 4                           # supersampling while drawing
BANDS = 3
AMBIENT, DIFFUSE = 0.35, 0.503   # a flat face lands on 180, the floor border's LIGHT
LIGHTS = {                       # band-local light: along, inward, up
    "north": (0.0, -0.5, 0.707),   # from the outer edge
    "south": (0.0, 0.5, 0.707),    # from the inner side
    "side": (0.0, 0.0, 0.707),     # side-on
}
SHADOW_SHIFT = {"north": 1.6, "south": -1.6, "side": 0.0}


# ---- drawing kit ----------------------------------------------------------

def grid(width):
    u = (np.arange(width * SS) + 0.5) / SS
    d = (np.arange(ROWS * SS) + 0.5) / SS
    return np.meshgrid(u, d)


def cover(x, lo, hi, soft=0.5):
    """Antialiased coverage of lo <= x <= hi."""
    return np.clip(np.minimum(x - lo, hi - x) / soft + 0.5, 0.0, 1.0)


def pdiff(a, b, period):
    return (a - b + period / 2.0) % period - period / 2.0


def plateau(d, lo, hi, height, bevel):
    """A raised strip from lo to hi across the band, with sloped sides."""
    return np.clip(np.minimum(d - lo, hi - d) / bevel, 0.0, 1.0) * height


# ---- the styles -------------------------------------------------------------

def inlay(u, d, width):
    """One fine metal line set in from the edge, the floor showing either side:
    a crisp line in gold or silver on wood reads as fitted work."""
    lo, hi = 10.0, 18.0
    metal = cover(d, lo, hi)
    height = plateau(d, lo, hi, 1.3, 1.0)
    gap = np.maximum(cover(d, lo - 1.3, lo), cover(d, hi, hi + 1.3))
    albedo = metal * 1.0 + gap * 0.3
    alpha = np.maximum(metal, gap * 0.85)
    return dict(height=height, albedo=albedo, alpha=alpha, shine=0.5, shadow=0.0)


def _leaf(u, d, width, u0, d0, angle, length, breadth):
    """A lens-shaped leaf from (u0, d0) along angle (radians, in u-d space)."""
    eu, ed = math.cos(angle), math.sin(angle)
    du = pdiff(u, u0, width)
    dd = d - d0
    p = du * eu + dd * ed
    q = -du * ed + dd * eu
    t = np.clip(p / length, 0, 1)
    half = (breadth / 2) * np.sin(np.pi * t) ** 0.75
    inside = (p >= 0) & (p <= length)
    coverage = np.where(inside, cover(np.abs(q), -10, half, soft=0.45), 0.0)
    rel = np.where(half > 0.05, np.abs(q) / np.maximum(half, 0.05), 1.0)
    height = 1.5 * np.clip(1 - rel * rel, 0, 1) * np.sin(np.pi * t) ** 0.5
    rib = cover(np.abs(q), -10, 0.45) * inside * (t < 0.92)
    return coverage, height - 0.45 * rib, rib


#: The vine's band, in texels across it from the tile's edge. The field is
#: opaque from the tile's edge to VINE_FIELD_END, so the band covers the edge
#: as the floor border does. A rounded lip and the floor border's dark keyline
#: are set in from either side, and the leaves reach past both, out to
#: VINE_REACH: past the field's end only the leaves are opaque, so the leaves
#: draw as much of the border's outline as the lines do.
VINE_LIP = (4.8, 1.3)            # a rounded bead, set in from the edge: centre and radius
VINE_KEYLINE = (26.5, 29.0)      # the dark inner line, set in from the leaves' reach
VINE_FIELD_END = 29.0            # opaque from the tile's edge to here
VINE_REACH = (0.5, 34.8)         # how far across the band a leaf may reach
VINE_STEM = 17.5                 # the stem's centre line, midway across the leaves' reach
FIELD_ALBEDO = 0.6               # the ground, darker than the vine so it reads in dark stuffs
KEYLINE_ALBEDO = 68 / 180        # the floor border's keyline, 68 where its fill is 180


def bead(d, centre, radius, height):
    """A rounded moulding along the band, so each side of it takes the light."""
    return height * np.sqrt(np.clip(1 - ((d - centre) / radius) ** 2, 0, 1))


def _reach(length, breadth, angle):
    """How far along the band a leaf reaches past its base."""
    t = np.linspace(0.0, 1.0, 65)
    half = (breadth / 2) * np.sin(np.pi * t) ** 0.75
    a = abs(angle)
    return float(np.max(t * length * math.cos(a) + half * math.sin(a)))


#: Texels at each end of a tile that only the shared leaf and the stem may
#: touch, shadows included, so every variant begins and ends alike.
VINE_END_CLEAR = 4.5
VINE_SPACING = 16.0              # the mean distance between leaves along the stem
#: Every leaf has a second one opposite it. At one in four, a quarter tile of
#: vine covered 0.43-0.61 of the leaves' reach, so some stretches read thin;
#: paired, the least full covers 0.63, past the fullest before.
VINE_PAIR_CHANCE = 1.0
VINE_PAIR_SCALE = (0.85, 0.9)    # the second leaf's length and breadth, against the first


class _Relief:
    """Raised parts carved into one surface: each texel shows the highest."""

    def __init__(self, like):
        self.height = np.zeros_like(like)
        self.coverage = np.zeros_like(like)
        self.albedo = np.ones_like(like)

    def add(self, h, c, a):
        self.height = np.maximum(self.height, h * (c > 0))
        self.coverage = np.maximum(self.coverage, c)
        self.albedo = np.minimum(self.albedo, a)


def _vine_tile(u, d, width, rng):
    """One tile of vine: a stem that enters and leaves at the same height and
    slope, c(u) = c0 + sum a_k sin^2(pi k u / W), and leaves alternating along
    it at an even spacing, reaching past both lines. One leaf crosses the
    tile's end, the same in every variant; no other comes near either end.

    Returns the berries and the vine, its stem and leaves, apart, each
    (height, albedo, coverage); the vine's coverage is kept only where it
    stands higher than the berries, so the two drawn one over the other show
    what one carving would."""
    c0 = VINE_STEM
    coeffs = [(k, rng.uniform(-0.5, 0.5)) for k in (1, 2, 3)]
    stem = c0 + sum(a * np.sin(np.pi * k * u / width) ** 2 for k, a in coeffs)

    def stem_at(x):
        return c0 + sum(a * math.sin(math.pi * k * x / width) ** 2 for k, a in coeffs)

    berries, leaves = _Relief(u), _Relief(u)
    stem_cover = cover(np.abs(d - stem), -10, 2.5)
    leaves.add(2.0 * np.sqrt(np.clip(1 - ((d - stem) / 2.8) ** 2, 0, 1)) * stem_cover, stem_cover,
               np.ones_like(u))

    def leaf(x, angle, length, breadth):
        """A leaf at x, shortened if it would reach into the tile's end."""
        room = width - VINE_END_CLEAR - x
        while _reach(length, breadth, angle) > room and length > 10.0:
            length -= 0.5
        if _reach(length, breadth, angle) <= room:
            c, h, rib = _leaf(u, d, width, x, stem_at(x), angle, length, breadth)
            leaves.add(h, c, 1 - 0.2 * rib)

    # The leaf across the joint: its base ends every variant and its tip,
    # wrapped by _leaf's periodic distance, begins every variant, so any
    # variant may follow any and the garland runs on through the joint
    # instead of thinning to bare stem there, a beat once a tile. The stem is
    # at c0 there in every variant, so the leaf is too.
    seam_x, seam_side = width - 8.0, 1
    c, h, rib = _leaf(u, d, width, seam_x, c0, seam_side * 0.6, 23.5, 13.5)
    leaves.add(h, c, 1 - 0.2 * rib)
    if VINE_PAIR_CHANCE >= 1.0:     # paired like every other leaf, or each joint reads thin
        c, h, rib = _leaf(u, d, width, seam_x + 2, c0, -seam_side * 0.54,
                          23.5 * VINE_PAIR_SCALE[0], 13.5 * VINE_PAIR_SCALE[1])
        leaves.add(h, c, 1 - 0.2 * rib)

    # The rest, spaced evenly from x0 up to it with a little jitter. An odd
    # count keeps the sides alternating through the joint.
    x0 = rng.uniform(VINE_END_CLEAR, 7.0)
    n = 2 * int(round(((seam_x - x0) / VINE_SPACING - 1) / 2)) + 1
    step = (seam_x - x0) / n
    for i in range(n):
        side = seam_side * (-1) ** (n - i)
        x = max(VINE_END_CLEAR, x0 + i * step + rng.uniform(-1.5, 1.5))
        angle = side * rng.uniform(0.55, 0.66)
        length = rng.uniform(22.0, 25.0)
        breadth = rng.uniform(12.5, 14.5)
        leaf(x, angle, length, breadth)
        if rng.random() < VINE_PAIR_CHANCE:
            leaf(x + 2, -angle * 0.9, length * VINE_PAIR_SCALE[0], breadth * VINE_PAIR_SCALE[1])
        if rng.random() < 0.3 and 2 * VINE_END_CLEAR <= x <= width - 14:   # berries in the axil
            for _ in range(int(rng.integers(2, 4))):
                bu = x + rng.uniform(0, 3)
                bd = stem_at(bu) - side * rng.uniform(3.0, 4.6)
                rr = np.sqrt(pdiff(u, bu, width) ** 2 + (d - bd) ** 2) / 1.8
                bc = cover(rr, -10, 1.0, soft=0.15)
                berries.add(1.6 * np.sqrt(np.clip(1 - rr * rr, 0, 1)), bc, np.where(bc > 0, 0.9, 1.0))
    on_top = leaves.height >= berries.height
    return ((berries.height, berries.albedo, berries.coverage),
            (leaves.height, leaves.albedo, leaves.coverage * on_top))


def vine(u, d, width, variants=4, seed=11):
    """A vine carved in relief on a solid band: a hard border like the floor
    border, opaque from the tile's edge, a rounded lip and the same dark
    keyline set in from either side, and leaves reaching past both lines, so
    they draw the border's outline as much as the lines do. The vine casts a
    soft shadow onto the field, and past the field's end onto the floor.

    The vine, its stem and leaves, comes back apart, as the drawing's
    ``overlay``; the band, the berries and every shadow, the vine's included,
    are the rest."""
    rng = np.random.default_rng(seed)
    berry_h, berry_c, vine_h, vine_c = (np.zeros_like(u) for _ in range(4))
    berry_a, vine_a = np.ones_like(u), np.ones_like(u)
    for j in range(variants):
        cols = slice(j * TILE * SS, (j + 1) * TILE * SS)
        berries, painted = _vine_tile(u[:, cols] - j * TILE, d[:, cols], TILE, rng)
        berry_h[:, cols], berry_a[:, cols], berry_c[:, cols] = berries
        vine_h[:, cols], vine_a[:, cols], vine_c[:, cols] = painted
    reach = cover(d, *VINE_REACH)
    berry_c, vine_c = berry_c * reach, vine_c * reach
    whole = np.maximum(berry_c, vine_c)
    field = cover(d, -10.0, VINE_FIELD_END)
    lips = bead(d, *VINE_LIP, 1.2)
    keyline = cover(d, *VINE_KEYLINE)
    ground = np.where(lips > 0, 0.95, FIELD_ALBEDO) * (1 - keyline) + KEYLINE_ALBEDO * keyline
    overlay = dict(height=vine_h * reach, albedo=vine_a, alpha=vine_c, shine=0.0, shadow=0.0)
    return dict(height=np.maximum(berry_h * reach, lips), albedo=ground * (1 - berry_c) + berry_a * berry_c,
                alpha=np.maximum(field, berry_c), shine=0.0,
                shadow=0.45, caster=np.maximum(field, whole),
                relief=berry_c, relief_caster=whole, relief_shadow=0.5,
                overlay=overlay)


def _stones(u, d, rng, lo_u, hi_u):
    height = np.zeros_like(u)
    albedo = np.full_like(u, 0.62)
    # Clear of both ends by a texel and more, so every variant meets the next
    # in grout.
    x = lo_u + rng.uniform(1.5, 3.0)
    while True:
        s = rng.uniform(12.5, 23.0)
        if x + s > hi_u - 1.8:
            s = hi_u - 1.8 - x
            if s < 8:
                break
        stack = s < 15.5 and rng.random() < 0.6
        centres = ([(x + s / 2 + rng.uniform(-1, 1), 8.8 + rng.uniform(-1, 1), s / 2, 6.3),
                    (x + s / 2 + rng.uniform(-1, 1), 23.0 + rng.uniform(-1, 1), s / 2, 6.5)]
                   if stack else
                   [(x + s / 2, 16 + rng.uniform(-1.8, 1.8), s / 2, min(13.2, s / 2 * rng.uniform(0.95, 1.25)))])
        for cu, cd, rx, ry in centres:
            r = (np.abs((u - cu) / rx) ** 2.4 + np.abs((d - cd) / ry) ** 2.4) ** (1 / 2.4)
            h = 3.0 * np.sqrt(np.clip(1 - r ** 2.2, 0, 1))
            tone = rng.uniform(0.8, 1.12)
            win = h > height
            height = np.where(win, h, height)
            albedo = np.where(win & (r < 1), tone * (0.93 + 0.07 * (1 - r)), albedo)
        x += s + rng.uniform(1.6, 3.0)
        if x > hi_u - 9:
            break
    return height, albedo


def pebbles(u, d, width, variants=4, seed=5):
    """River pebbles set in a band of grout, for paths: hard-wearing, and each
    tile its own stones."""
    rng = np.random.default_rng(seed)
    # Grout from the tile's very edge: starting it a texel in left a line of
    # floor showing between the border and whatever lay across the edge.
    band = cover(d, -10.0, 31.0)
    height = np.zeros_like(u)
    albedo = np.full_like(u, 0.62)
    for j in range(variants):
        cols = slice(j * TILE * SS, (j + 1) * TILE * SS)
        h, a = _stones(u[:, cols] - j * TILE, d[:, cols], rng, 0.0, TILE)
        height[:, cols], albedo[:, cols] = h, a
    return dict(height=height * band, albedo=albedo, alpha=band, shine=0.0, shadow=0.35)


# ---- lighting and the strip ---------------------------------------------------

def box_blur(a, r, axis):
    """Along the band (axis 1) the strip repeats, so the blur wraps there and
    a seam sees the same neighbours as the middle of a run; across it, edge."""
    if r < 1:
        return a
    k = int(r)
    c = np.cumsum(np.pad(a, [(k + 1, k) if i == axis else (0, 0) for i in range(a.ndim)],
                         mode="wrap" if axis == 1 else "edge"),
                  axis=axis)
    hi = np.take(c, range(2 * k + 1, c.shape[axis]), axis=axis)
    lo = np.take(c, range(0, c.shape[axis] - 2 * k - 1), axis=axis)
    return (hi - lo) / (2 * k + 1)


def bake(s, width):
    """A style's drawing lit three ways: {band: (grey 0-1, alpha 0-1)}, each
    ROWS x width, row 0 at the outer edge.

    Shadows fall from ``caster`` (the drop shadow, onto whatever lies past the
    band) and ``relief_caster`` (onto the band itself) when the drawing names
    them, else from what it draws: the vine's band carries the shadows of the
    leaves its overlay draws."""
    height, albedo, alpha = s["height"], s["albedo"], np.clip(s["alpha"], 0, 1)
    # Central differences that wrap along the band, as the strip does: a
    # one-sided difference at the strip's two ends would light a leaf crossing
    # a variant's end differently there than at every other joint.
    gu = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) / 2 * SS
    gd = np.gradient(height, axis=0) * SS
    norm = np.sqrt(gu * gu + gd * gd + 1)
    out = {}
    for band, (lu, ld, lz) in LIGHTS.items():
        lit = (-gu * lu - gd * ld + lz) / norm
        grey = albedo * (AMBIENT + DIFFUSE * np.clip(lit, 0, None))
        if s.get("shine"):
            light = np.array([lu, ld, lz]) / np.linalg.norm([lu, ld, lz])
            facing = (-gu * light[0] - gd * light[1] + light[2]) / norm
            grey = grey + s["shine"] * np.clip(facing, 0, 1) ** 24
        if s.get("relief_shadow"):
            # A raised ornament on an opaque band casts its shadow onto the
            # band itself, away from the light, where the drop shadow below
            # (which falls only where the band is not) cannot reach.
            n = int(round(SHADOW_SHIFT[band] * SS))
            cast = np.roll(s.get("relief_caster", s["relief"]), n, axis=0)
            if n > 0:
                cast[:n] = 0
            elif n < 0:
                cast[n:] = 0
            cast = box_blur(box_blur(cast, 1.0 * SS, 0), 1.0 * SS, 1)
            grey = grey * (1 - s["relief_shadow"] * cast * (1 - s["relief"]))
        total = alpha
        premul = grey * alpha
        if s.get("shadow"):
            n = int(round(SHADOW_SHIFT[band] * SS))
            mask = np.roll(np.clip(s.get("caster", alpha), 0, 1), n, axis=0)
            if n > 0:
                mask[:n] = 0
            elif n < 0:
                mask[n:] = 0
            mask = box_blur(box_blur(mask, 1.2 * SS, 0), 1.2 * SS, 1)
            total = alpha + s["shadow"] * mask * (1 - alpha)
        premul = premul.reshape(ROWS, SS, width, SS).mean(axis=(1, 3))
        total = total.reshape(ROWS, SS, width, SS).mean(axis=(1, 3))
        grey = np.where(total > 1e-4, premul / np.maximum(total, 1e-4), 0.0)
        out[band] = (np.clip(grey, 0, 1), np.clip(total, 0, 1))
    return out


def strip_rows(baked):
    """The strip's RGBA rows: north band outer edge first, south band outer
    edge last, side band outer edge first."""
    def rows_of(grey, alpha, reverse):
        order = range(ROWS - 1, -1, -1) if reverse else range(ROWS)
        out = []
        for r in order:
            g = np.round(grey[r] * 255).astype(np.uint8)
            a = np.round(alpha[r] * 255).astype(np.uint8)
            out.append(np.stack([g, g, g, a], axis=1).tobytes())
        return out
    return (rows_of(*baked["north"], False) + rows_of(*baked["south"], True)
            + rows_of(*baked["side"], False))


#: Each family: its texture stem, how it draws, and its variants.
FAMILIES = (
    ("InlayBorder", inlay, 1),
    ("VineBorder", vine, 4),
    ("PebbleBorder", pebbles, 4),
)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for stem, style, variants in FAMILIES:
        width = TILE * variants
        drawing = style(*grid(width), width)
        rows = strip_rows(bake(drawing, width))
        layers = 1
        if "overlay" in drawing:
            # The overlay's bands, mirrored into the bottom half: the
            # renderer reads them so (StripTrimGeometry.BandV).
            rows += strip_rows(bake(drawing["overlay"], width))[::-1]
            layers = 2
        write_rgba_png(os.path.join(OUT_DIR, f"{stem}Strip.png"), width, layers * BANDS * ROWS, rows)
        strip = (width, layers * BANDS * ROWS, rows)
        layout = trim_icons.layout_of(strip, BANDS, variants, layers)
        for shape, suffix in trim_icons.SUFFIX.items():
            write_rgba_png(os.path.join(OUT_DIR, f"{stem}{suffix}_MenuIcon.png"), trim_icons.ICON,
                           trim_icons.ICON, trim_icons.render(shape, strip, layout))


if __name__ == "__main__":
    main()
