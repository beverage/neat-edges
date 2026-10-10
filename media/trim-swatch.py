#!/usr/bin/env python3
"""The trim swatch cards, drawn from the strips the mod ships rather than
captured in game.

    uv run --with numpy --with pillow python3 trim-swatch.py

Each piece is laid out by devtools/strip_trim.py, the Python mirror of the C#
geometry that check_trims.py holds to the golden file, and rendered by
devtools/strip_render.py. Each layer is then tinted as the game tints it: the
strip's grey times the material's colour, and a paint overlay's grey times the
paint's. Lighting is left out, so this is the game at full daylight. The
colours are vanilla's own, from the Core defs.

Writes cards/card-trims.png (a corner of each style over every piece in the
plain border) and cards/card-silver-vines.png (the vine in silver, unpainted
and in three golds).
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
MOD_ROOT = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(MOD_ROOT, "devtools"))
import strip_render  # noqa: E402
import strip_trim  # noqa: E402
import trim_icons  # noqa: E402
import trim_kit  # noqa: E402

TEXTURES = os.path.join(MOD_ROOT, "Textures", "NeatEdges", "Trim")
# Shift Change's copy of the house title font, beside this repo in the constellation.
FONT = os.environ.get("RIMWORD_FONT",
                      os.path.join(HERE, "..", "..", "shift-change", "media", "RimWordFont.ttf"))

# Stuff colours (stuffProps.color) and paint colours (ColorDef).
WOOD = (133, 97, 67)            # WoodLog
SLATE = (70, 70, 70)            # BlocksSlate
GOLD = (255, 235, 122)          # Gold; no paint carries it, a colour picker does
SILVER = (180, 173, 150)        # Silver
GREEN = (89, 105, 62)           # Structure_Green
ORANGE_PASTEL = (191, 137, 33)  # Structure_OrangePastel, the vanilla paint nearest gold
MUSTARD = (163, 131, 49)        # Structure_Mustard

PAGE = (31, 36, 42)             # the section banners' ground, #1f242a
INK = (245, 240, 231)           # the house title colour
DIM = (172, 168, 160)           # a label's second line
FLOOR = (136, 131, 122)         # light enough for slate to read, dark enough for gold

PX = 256                        # texels per tile in every strip

# The font's capital O is the star from the game's logo, and its lower case is
# the same capitals with a plain O, so every label is written in lower case.


def strip_of(stem, variants=1, layers=1):
    strip = trim_kit.read_rgba_png(os.path.join(TEXTURES, stem + "Strip.png"))
    return strip, trim_icons.layout_of(strip, 3, variants, layers)


def layer_arrays(pieces_of, strip, layout, window, size):
    """One RGBA array per layer the strip holds, bands first."""
    out = []
    for each in layout.each_layer():
        image = strip_render.Image(size[0], size[1], window)
        for points, uvs in pieces_of(each):
            image.draw(points, uvs, strip)
        out.append(np.frombuffer(b"".join(image.rows()), dtype=np.uint8).reshape(size[1], size[0], 4))
    return out


def tinted(layers, tints, ground=FLOOR):
    """Each layer's grey times its tint, laid over the ground in order."""
    h, w = layers[0].shape[:2]
    rgb = np.empty((h, w, 3), dtype=np.float64)
    rgb[:] = ground
    for layer, tint in zip(layers, tints):
        grey = layer[..., 0:1].astype(np.float64) / 255.0
        alpha = layer[..., 3:4].astype(np.float64) / 255.0
        rgb = grey * np.array(tint, dtype=np.float64) * alpha + rgb * (1.0 - alpha)
    return Image.fromarray(np.clip(rgb + 0.5, 0, 255).astype(np.uint8), "RGB")


def scene(pieces):
    """Pieces placed as (shape, rotation, cell), each cell spanning [x, x+1] x [z, z+1]."""
    def pieces_of(layout):
        for shape, rot, cell in pieces:
            for points, uvs in strip_trim.textured(shape, rot, cell, layout):
                yield [(cell[0] + 0.5 + x, cell[1] + 0.5 + z) for x, z in points], uvs
    return pieces_of


# A corner piece facing west (its bands on the west and north edges) at the top
# left, cropped to three quarters of a tile so a style's detail shows, and the
# straights beside it carrying its runs out of the frame.
CORNER = scene([("Corner", 3, (0, 1)), ("Straight", 0, (1, 1)), ("Straight", 3, (0, 0))])
CROP = 0.75
CORNER_WINDOW = (0.0, 2.0 - CROP, CROP, 2.0)
CORNER_SIZE = (int(round(CROP * PX)), int(round(CROP * PX)))


def corner_card(stem, variants, layers, tints):
    strip, layout = strip_of(stem, variants, layers)
    return tinted(layer_arrays(CORNER, strip, layout, CORNER_WINDOW, CORNER_SIZE), tints)


def style_cards():
    return [
        (corner_card("FloorBorder", 1, 1, (WOOD,)), ("floor border", "wood")),
        (corner_card("PebbleBorder", 4, 1, (SLATE,)), ("pebble border", "slate")),
        (corner_card("InlayBorder", 1, 1, (GOLD,)), ("inlay border", "gold")),
        (corner_card("VineBorder", 4, 2, (WOOD, GREEN)), ("vine border", "wood, painted green")),
    ]


def piece_cards():
    """Every shape in the plain border, in wood, framed as its build button's icon is."""
    strip, layout = strip_of("FloorBorder")
    cards = []
    for shape, text in (("Straight", "straight"), ("Corner", "corner"), ("InsideCorner", "inside corner"),
                        ("Runner", "runner"), ("EndCap", "end cap"), ("Frame", "frame"),
                        ("Diagonal", "diagonal")):
        window, _ = trim_icons.pieces_for(shape, layout)

        def pieces_of(each, shape=shape):
            return trim_icons.pieces_for(shape, each)[1]
        cards.append((tinted(layer_arrays(pieces_of, strip, layout, window, (PX, PX)), (WOOD,)), (text,)))
    return cards


def font(size):
    return ImageFont.truetype(FONT, size)


def text_height(face):
    return ImageDraw.Draw(Image.new("RGB", (1, 1))).textbbox((0, 0), "LINE", font=face)[3]


def row_height(card, lines, label_gap, line_gap):
    return card + label_gap + sum(text_height(f) for f, _ in lines) + line_gap * (len(lines) - 1)


def draw_row(out, top, cards, card, gap, lines, label_gap, line_gap):
    """Cards centred across the image, each over its label lines."""
    draw = ImageDraw.Draw(out)
    span = card * len(cards) + gap * (len(cards) - 1)
    left = (out.width - span) // 2
    for k, (image, texts) in enumerate(cards):
        x = left + k * (card + gap)
        out.paste(image.resize((card, card), Image.LANCZOS), (x, top))
        y = top + card + label_gap
        for (face, colour), text in zip(lines, texts):
            box = draw.textbbox((0, 0), text, font=face)
            draw.text((x + card / 2 - (box[2] - box[0]) / 2 - box[0], y), text, font=face, fill=colour)
            y += text_height(face) + line_gap


def trims_card(path, width=1600, margin=48, between=56):
    """The four styles over the seven pieces, both rows the same span."""
    span = width - 2 * margin
    gap1, gap2 = 48, 28
    card1 = (span - 3 * gap1) // 4
    card2 = (span - 6 * gap2) // 7
    lines1 = ((font(28), INK), (font(21), DIM))
    lines2 = ((font(22), INK),)
    first = row_height(card1, lines1, 16, 8)
    out = Image.new("RGB", (width, margin + first + between + row_height(card2, lines2, 14, 0) + margin), PAGE)
    draw_row(out, margin, style_cards(), card1, gap1, lines1, 16, 8)
    draw_row(out, margin + first + between, piece_cards(), card2, gap2, lines2, 14, 0)
    out.save(path)
    return out.size


def silver_vines_card(path, card=300, gap=40, margin=40):
    """The vine in silver: unpainted, in gold's own colour, and in the two
    vanilla paints nearest gold."""
    cards = [
        (corner_card("VineBorder", 4, 2, (SILVER, SILVER)), ("silver", "unpainted")),
        (corner_card("VineBorder", 4, 2, (SILVER, GOLD)), ("silver", "painted gold")),
        (corner_card("VineBorder", 4, 2, (SILVER, ORANGE_PASTEL)), ("silver", "painted pastel orange")),
        (corner_card("VineBorder", 4, 2, (SILVER, MUSTARD)), ("silver", "painted mustard")),
    ]
    lines = ((font(28), INK), (font(21), DIM))
    width = 2 * margin + len(cards) * card + (len(cards) - 1) * gap
    out = Image.new("RGB", (width, 2 * margin + row_height(card, lines, 16, 8)), PAGE)
    draw_row(out, margin, cards, card, gap, lines, 16, 8)
    out.save(path)
    return out.size


if __name__ == "__main__":
    cards = os.path.join(HERE, "cards")
    for name, make in (("card-trims.png", trims_card), ("card-silver-vines.png", silver_vines_card)):
        w, h = make(os.path.join(cards, name))
        print(f"cards/{name}: {w}x{h}")
