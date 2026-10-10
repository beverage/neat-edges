#!/usr/bin/env python3
"""Renders trims from a strip the way the game lays them out, for menu icons
and contact sheets: polygons with their UVs (from ``strip_trim.py``), sampled
at the nearest texel, composited in order, supersampled. Imported, not run;
standard library only, like the rest of the art kit.

The strip is ``(width, height, rows)`` as ``trim_kit.read_rgba_png`` returns
it: greyscale RGBA rows, top to bottom. Output is the same shape, so a result
can be written with ``trim_kit.write_rgba_png`` or pasted onto a sheet.
"""

import math


def sample(strip, u, v):
    """(grey, alpha) of the texel a UV lands in: U repeats, V clamps."""
    width, height, rows = strip
    tx = int(math.floor((u % 1.0) * width)) % width
    ty = min(height - 1, max(0, int(math.floor((1.0 - v) * height))))
    row = rows[ty]
    return row[4 * tx], row[4 * tx + 3]


def _affine(points, uvs):
    """UV as an affine function of position, from a polygon's first three
    vertices (a band's UV is affine across it)."""
    (ax, az), (bx, bz), (cx, cz) = points[0], points[1], points[2]
    (ua, va), (ub, vb), (uc, vc) = uvs[0], uvs[1], uvs[2]
    det = (bx - ax) * (cz - az) - (cx - ax) * (bz - az)
    if abs(det) < 1e-12:
        return None

    def at(x, z):
        px, pz = x - ax, z - az
        s = (px * (cz - az) - pz * (cx - ax)) / det
        t = ((bx - ax) * pz - (bz - az) * px) / det
        return ua + s * (ub - ua) + t * (uc - ua), va + s * (vb - va) + t * (vc - va)
    return at


def _inside(points, x, z):
    n = len(points)
    for i in range(n):
        ax, az = points[i]
        bx, bz = points[(i + 1) % n]
        if (bx - ax) * (z - az) - (bz - az) * (x - ax) > 1e-12:
            return False
    return True


class Image:
    """A greyscale image with alpha, drawn supersampled."""

    def __init__(self, width, height, window, supersample=2):
        self.width, self.height = width, height
        self.window = window
        self.ss = supersample
        self.sw, self.sh = width * supersample, height * supersample
        self.value = [0.0] * (self.sw * self.sh)
        self.alpha = [0.0] * (self.sw * self.sh)

    def _to_world(self, i, j):
        x0, z0, x1, z1 = self.window
        return (x0 + (i + 0.5) / self.sw * (x1 - x0),
                z1 - (j + 0.5) / self.sh * (z1 - z0))

    def fill(self, grey):
        """Paint the whole image one opaque grey: the ground a scene stands on."""
        self.value = [float(grey)] * (self.sw * self.sh)
        self.alpha = [1.0] * (self.sw * self.sh)

    def fill_polygon(self, points, grey):
        """Paint a clockwise convex polygon one opaque grey: a room's floor."""
        x0, z0, x1, z1 = self.window
        xs = [p[0] for p in points]
        zs = [p[1] for p in points]
        i0 = max(0, int(math.floor((min(xs) - x0) / (x1 - x0) * self.sw)))
        i1 = min(self.sw, int(math.ceil((max(xs) - x0) / (x1 - x0) * self.sw)))
        j0 = max(0, int(math.floor((z1 - max(zs)) / (z1 - z0) * self.sh)))
        j1 = min(self.sh, int(math.ceil((z1 - min(zs)) / (z1 - z0) * self.sh)))
        for j in range(j0, j1):
            for i in range(i0, i1):
                if _inside(points, *self._to_world(i, j)):
                    k = j * self.sw + i
                    self.value[k] = float(grey)
                    self.alpha[k] = 1.0

    def draw(self, points, uvs, strip):
        """Composite one polygon, sampled from the strip, over what is there."""
        at = _affine(points, uvs)
        if at is None:
            return
        x0, z0, x1, z1 = self.window
        xs = [p[0] for p in points]
        zs = [p[1] for p in points]
        i0 = max(0, int(math.floor((min(xs) - x0) / (x1 - x0) * self.sw)) - 1)
        i1 = min(self.sw, int(math.ceil((max(xs) - x0) / (x1 - x0) * self.sw)) + 1)
        j0 = max(0, int(math.floor((z1 - max(zs)) / (z1 - z0) * self.sh)) - 1)
        j1 = min(self.sh, int(math.ceil((z1 - min(zs)) / (z1 - z0) * self.sh)) + 1)
        for j in range(j0, j1):
            for i in range(i0, i1):
                x, z = self._to_world(i, j)
                if not _inside(points, x, z):
                    continue
                grey, a = sample(strip, *at(x, z))
                if a == 0:
                    continue
                src = a / 255.0
                k = j * self.sw + i
                dst = self.alpha[k]
                out = src + dst * (1.0 - src)
                self.value[k] = (grey * src + self.value[k] * dst * (1.0 - src)) / out
                self.alpha[k] = out

    def rows(self):
        """Averaged down to final size, weighted by alpha so edges keep their grey."""
        out = []
        ss = self.ss
        for y in range(self.height):
            row = bytearray()
            for x in range(self.width):
                weighted = total = 0.0
                for dy in range(ss):
                    base = (y * ss + dy) * self.sw + x * ss
                    for dx in range(ss):
                        a = self.alpha[base + dx]
                        weighted += self.value[base + dx] * a
                        total += a
                if total <= 0.0:
                    row += b"\x00\x00\x00\x00"
                else:
                    grey = max(0, min(255, int(weighted / total + 0.5)))
                    row += bytes((grey, grey, grey, max(0, min(255, int(total / (ss * ss) * 255 + 0.5)))))
            out.append(bytes(row))
        return out
