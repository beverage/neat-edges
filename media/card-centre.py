#!/usr/bin/env python3
"""Centre a screenshot of a two-way symmetric scene on its mirror axes, for a gallery card.

    python3 card-centre.py SRC OUT

Finds the vertical and the horizontal mirror axis (the axis whose reflection
differs least from the image), crops the largest rectangle centred on their
crossing, and reports both axes. Fade the result with card-fade.py --no-crop.

Check the axes against the building before trusting the crop: plant art leans
upward, so a garden's horizontal axis came out 5 px high, and a scene that is
only symmetric left to right needs the crop by hand on the other axis (the
README records each card's numbers).
"""
import subprocess
import sys

import numpy as np


def load(path):
    w, h = map(int, subprocess.run(["magick", "identify", "-format", "%w %h", path],
                                   capture_output=True, text=True, check=True).stdout.split())
    raw = subprocess.run(["magick", path, "-alpha", "off", "-depth", "8", "rgb:-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(h, w, 3).astype(float)


def axis(a):
    """Best vertical mirror axis of a, in pixel-edge units, and its mean difference."""
    w = a.shape[1]
    best = (np.inf, None)
    for s in range(int(w * 0.7), int(w * 1.3) + 1):
        x = np.arange(max(0, s - w), (s - 1) // 2 + 1)
        xm = s - 1 - x
        keep = xm != x
        if keep.sum() < 50:
            continue
        best = min(best, (np.abs(a[:, x[keep]] - a[:, xm[keep]]).mean(), s / 2))
    return best[1], best[0]


img = load(sys.argv[1])
h, w = img.shape[:2]
cx, dx = axis(img)
cy, dy = axis(img.transpose(1, 0, 2))
print(f"{w}x{h}: mirror x {cx:.1f} (diff {dx:.1f}), y {cy:.1f} (diff {dy:.1f})")
half_w, half_h = int(min(cx, w - cx)), int(min(cy, h - cy))
x0, y0 = int(round(cx - half_w)), int(round(cy - half_h))
print(f"crop {2 * half_w}x{2 * half_h} at +{x0}+{y0}")
subprocess.run(["magick", sys.argv[1], "-crop", f"{2 * half_w}x{2 * half_h}+{x0}+{y0}", "+repage", sys.argv[2]], check=True)
print(sys.argv[2])
