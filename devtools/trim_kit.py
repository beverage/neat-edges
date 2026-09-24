#!/usr/bin/env python3
"""The drawing kit the trim art is built from: a supersampled greyscale canvas
and a dependency-free PNG writer. Imported, not run.

Ported from Fine Establishments' ``texture_kit.py`` when the trims moved here,
cut down to what they use. The pixel maths is unchanged, so this regenerates
the trim textures byte for byte. What was left behind is the two-tone mask
machinery: the trims are single-material, tinted whole by their stuff, so
there is no mask to write.

Why grey: every trim is stuffable. RimWorld multiplies the texture by the
material's colour at draw time, so a texture's job is light and shadow, not
colour. A mid-grey band reads as oak built from wood and as brushed steel
built from steel.

Coordinates are pixels on the finished image, (0, 0) at the top-left, y
increasing downwards. One map tile is ``TILE`` pixels.
"""

import os
import struct
import zlib

#: The mod's root directory. This file lives in ``<mod>/devtools/``.
MOD_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

#: One map tile, in authored pixels.
TILE = 256

# The house greys, in draw order from darkest to brightest.
DARK = 68       # deep recesses: the keyline beside a band
SHADE = 105     # surfaces turned away from the light; grooves
LIGHT = 180     # horizontal top surfaces, which catch the most light
HILITE = 215    # the lit lip of a bevel

#: Every image is drawn at this multiple of its final size and averaged back
#: down, which keeps diagonal edges from looking like staircases.
SUPERSAMPLE = 2


class Canvas:
    """A greyscale image with transparency, drawn at supersampled size.

    Two parallel byte buffers hold the grey ``value`` and the ``alpha`` of each
    pixel. Drawing composites onto whatever is already there, so later shapes
    paint over earlier ones like stacked brush strokes. Public methods take
    coordinates in FINAL pixels; the supersampling is internal.
    """

    def __init__(self, size):
        """``size`` is a side length for a square, or a (width, height) pair."""
        if isinstance(size, (tuple, list)):
            self.final_width, self.final_height = size
        else:
            self.final_width = self.final_height = size
        self.width = self.final_width * SUPERSAMPLE
        self.height = self.final_height * SUPERSAMPLE
        self.value = bytearray(self.width * self.height)
        self.alpha = bytearray(self.width * self.height)

    def _blend(self, index, value, alpha):
        """Paint one pixel over the existing one ("source-over" compositing)."""
        src_a = alpha / 255.0
        dst_a = self.alpha[index] / 255.0
        out_a = src_a + dst_a * (1.0 - src_a)
        if out_a > 0.0001:
            self.value[index] = int(
                (value * src_a + self.value[index] * dst_a * (1.0 - src_a)) / out_a
            )
            self.alpha[index] = int(out_a * 255.0 + 0.5)

    def rect(self, x0, y0, x1, y1, value, alpha=255):
        """Fill an axis-aligned rectangle."""
        left, top = int(x0 * SUPERSAMPLE), int(y0 * SUPERSAMPLE)
        right, bottom = int(x1 * SUPERSAMPLE), int(y1 * SUPERSAMPLE)
        left, right = max(0, left), min(self.width, right)
        top, bottom = max(0, top), min(self.height, bottom)
        shaded = max(0, min(255, int(value)))
        for y in range(top, bottom):
            row = y * self.width
            for x in range(left, right):
                self._blend(row + x, shaded, alpha)

    def paste(self, source, x, y, size=None):
        """Composite another canvas onto this one, optionally rescaled.

        ``size`` is the width the source should occupy here, in final pixels;
        it keeps the source's aspect ratio. Used to lay pieces out as a run on
        a contact sheet.
        """
        size = size or source.final_width
        step = source.width / (size * SUPERSAMPLE)
        dest_height = int(size * source.final_height / source.final_width)

        for dy in range(dest_height * SUPERSAMPLE):
            src_top = dy * step
            src_bottom = min(source.height, max(src_top + step, src_top + 1))
            out_y = int(y * SUPERSAMPLE) + dy
            if not 0 <= out_y < self.height:
                continue
            for dx in range(size * SUPERSAMPLE):
                src_left = dx * step
                src_right = min(source.width, max(src_left + step, src_left + 1))

                # Box-average the source footprint, weighting colour by alpha
                # so transparent pixels don't darken the result.
                weighted_value = total_alpha = samples = 0
                for sy in range(int(src_top), int(src_bottom)):
                    row = sy * source.width
                    for sx in range(int(src_left), int(src_right)):
                        a = source.alpha[row + sx]
                        weighted_value += source.value[row + sx] * a
                        total_alpha += a
                        samples += 1
                if not samples or not total_alpha:
                    continue

                out_x = int(x * SUPERSAMPLE) + dx
                if 0 <= out_x < self.width:
                    self._blend(out_y * self.width + out_x,
                                int(weighted_value / total_alpha),
                                int(total_alpha / samples))

    def _resolve_pixels(self):
        """Average the supersampled buffer down to final-size RGBA rows.

        Weighted by alpha, so the transparent pixels around a shape don't drag
        its edge toward black: the classic dark halo.
        """
        rows = []
        for y in range(self.final_height):
            row = bytearray()
            for x in range(self.final_width):
                weighted_value = total_alpha = 0
                for sub_y in range(SUPERSAMPLE):
                    start = (y * SUPERSAMPLE + sub_y) * self.width + x * SUPERSAMPLE
                    for sub_x in range(SUPERSAMPLE):
                        a = self.alpha[start + sub_x]
                        weighted_value += self.value[start + sub_x] * a
                        total_alpha += a
                if total_alpha == 0:
                    row += b"\x00\x00\x00\x00"
                else:
                    grey = int(weighted_value / total_alpha)
                    row += bytes((grey, grey, grey,
                                  int(total_alpha / (SUPERSAMPLE * SUPERSAMPLE))))
            rows.append(bytes(row))
        return rows

    def save_png(self, path):
        """Write the drawn art as an 8-bit RGBA PNG."""
        write_rgba_png(path, self.final_width, self.final_height,
                       self._resolve_pixels())


def drop_stale_dds(png_path):
    """Delete the ``.dds`` beside a PNG we have just rewritten.

    RimWorld ignores a PNG outright when a ``.dds`` sits next to it, with no
    timestamp comparison, and caching tools write those ``.dds`` files beside
    mod textures. After any regeneration the cache is stale and the game keeps
    drawing the old texture, silently. So the generator owns the
    invalidation: write the PNG, drop the cache.
    """
    stale = os.path.splitext(png_path)[0] + ".dds"
    if os.path.exists(stale):
        os.remove(stale)


def write_rgba_png(path, width, height, rows):
    """Write finished RGBA rows as an 8-bit PNG, standard library only.

    Also drops any stale ``.dds`` beside the file, without which the game never
    sees anything written here.
    """

    def chunk(kind, data):
        return (struct.pack(">I", len(data)) + kind + data
                + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF))

    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    pixels = b"".join(b"\x00" + row for row in rows)

    with open(path, "wb") as png:
        png.write(b"\x89PNG\r\n\x1a\n")
        png.write(chunk(b"IHDR", header))
        png.write(chunk(b"IDAT", zlib.compress(pixels, 9)))
        png.write(chunk(b"IEND", b""))
    drop_stale_dds(path)
    print("wrote", os.path.relpath(path, MOD_ROOT))
