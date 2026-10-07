#!/usr/bin/env python3
"""A looping before/after GIF from two screenshots taken with one camera.

    python3 transition-gif.py BEFORE.png AFTER.png OUT.gif [--hold 1.5] [--fade 0.6] [--fps 20]

The loop holds BEFORE, fades to AFTER, holds AFTER, and fades back.

The two captures only have to share a camera, not a crop. The script finds
the offset at which the unchanged ground matches pixel for pixel, crops both
to their overlap, and refuses to go on if the ground does not match: a zoom
change, a moved camera or a change of light would make the whole frame swim
during the fade, which reads as an effect of the mod. The aligned pair is
written beside the GIF (OUT-before.png, OUT-after.png) for compositing.

Needs numpy, ImageMagick (magick), ffmpeg and gifsicle. ffmpeg does the
palette and the dithering; gifsicle then sets every frame's delay exactly,
because ffmpeg's GIF timing rounds a 50 ms frame to an uneven mix of 40 and
80, and drops the extra frame the concat list has to end with.
"""
import argparse
import os
import shutil
import subprocess
import sys
import tempfile

import numpy as np


def load(path):
    w, h = subprocess.run(["magick", "identify", "-format", "%w %h", path + "[0]"],
                          capture_output=True, text=True, check=True).stdout.split()
    raw = subprocess.run(["magick", path + "[0]", "-alpha", "off", "-depth", "8", "rgb:-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(int(h), int(w), 3)


def overlap(a_shape, b_shape, dx, dy):
    """The shared rectangle, in A's coordinates, when B[y + dy, x + dx] is A[y, x]."""
    y0, y1 = max(0, -dy), min(a_shape[0], b_shape[0] - dy)
    x0, x1 = max(0, -dx), min(a_shape[1], b_shape[1] - dx)
    return y0, y1, x0, x1


def median_diff(a, b, dx, dy, step):
    y0, y1, x0, x1 = overlap(a.shape, b.shape, dx, dy)
    if y1 - y0 < 16 or x1 - x0 < 16:
        return np.inf
    pa = a[y0:y1:step, x0:x1:step]
    pb = b[y0 + dy:y1 + dy:step, x0 + dx:x1 + dx:step]
    # The median, not the mean: what changed between the shots is the point
    # of the comparison, and it only has to cover less than half the frame.
    return float(np.median(np.abs(pa - pb)))


def register(a, b, reach_x, reach_y):
    ga = a.mean(axis=2)
    gb = b.mean(axis=2)
    coarse = min(((median_diff(ga, gb, dx, dy, 3), dx, dy)
                  for dy in range(-reach_y, reach_y + 1)
                  for dx in range(-reach_x, reach_x + 1)), key=lambda t: t[0])
    fine = min(((median_diff(ga, gb, dx, dy, 1), dx, dy)
                for dy in range(coarse[2] - 2, coarse[2] + 3)
                for dx in range(coarse[1] - 2, coarse[1] + 3)), key=lambda t: t[0])
    return fine


def write_ppm(path, image):
    with open(path, "wb") as f:
        f.write(b"P6\n%d %d\n255\n" % (image.shape[1], image.shape[0]))
        f.write(np.ascontiguousarray(image).tobytes())


def write_png(path, image):
    subprocess.run(["magick", "-size", f"{image.shape[1]}x{image.shape[0]}", "-depth", "8", "rgb:-", path],
                   input=np.ascontiguousarray(image).tobytes(), check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("before")
    parser.add_argument("after")
    parser.add_argument("out")
    parser.add_argument("--hold", type=float, default=1.5, help="seconds on each state (default 1.5)")
    parser.add_argument("--fade", type=float, default=0.6, help="seconds per fade (default 0.6)")
    parser.add_argument("--fps", type=int, default=20, help="frame rate of the fades (default 20)")
    parser.add_argument("--reach", type=int, nargs=2, default=(48, 16), metavar=("X", "Y"),
                        help="largest crop offset to search, in pixels (default 48 16)")
    args = parser.parse_args()
    for tool in ("magick", "ffmpeg", "gifsicle"):
        if not shutil.which(tool):
            sys.exit(f"{tool} is not installed (brew install {'imagemagick' if tool == 'magick' else tool})")

    before = load(args.before)
    after = load(args.after)
    score, dx, dy = register(before.astype(np.float32), after.astype(np.float32), *args.reach)
    y0, y1, x0, x1 = overlap(before.shape, after.shape, dx, dy)
    a = before[y0:y1, x0:x1]
    b = after[y0 + dy:y1 + dy, x0 + dx:x1 + dx]
    same = float((np.abs(a.astype(np.int16) - b.astype(np.int16)).max(axis=2) == 0).mean())
    print(f"aligned: after is offset {dx:+d},{dy:+d} px from before; overlap {a.shape[1]}x{a.shape[0]}; "
          f"{same:.0%} of pixels identical, median difference {score:.1f}")
    if score > 2.0 or same < 0.5:
        sys.exit("the captures do not share a camera: the unchanged ground does not match at any offset "
                 "(a different zoom, a moved camera or a change of light). Recapture both from one view.")

    stem = os.path.splitext(args.out)[0]
    write_png(stem + "-before.png", a)
    write_png(stem + "-after.png", b)

    fa = a.astype(np.float32)
    fb = b.astype(np.float32)
    steps = max(1, round(args.fade * args.fps))

    def blend(t):
        t = t * t * (3 - 2 * t)  # smoothstep: no jolt at either end of the fade
        return np.clip(fa + (fb - fa) * t + 0.5, 0, 255).astype(np.uint8)

    ramp = [(i + 1) / (steps + 1) for i in range(steps)]
    frames = [(a, args.hold)]
    frames += [(blend(t), 1 / args.fps) for t in ramp]
    frames += [(b, args.hold)]
    frames += [(blend(1 - t), 1 / args.fps) for t in ramp]

    work = tempfile.mkdtemp(prefix="transition-gif-")
    try:
        lines = ["ffconcat version 1.0"]
        for i, (image, seconds) in enumerate(frames):
            name = f"f{i:03d}.ppm"
            write_ppm(os.path.join(work, name), image)
            lines += [f"file '{name}'", f"duration {seconds:.3f}"]
        # The concat demuxer only honours the last duration when a file follows it.
        lines.append(f"file 'f{len(frames) - 1:03d}.ppm'")
        listing = os.path.join(work, "frames.ffconcat")
        with open(listing, "w") as f:
            f.write("\n".join(lines) + "\n")
        # One palette for every frame, so the ground keeps its colours through
        # the fade, and each frame re-dithered only where it changed, so the
        # untouched ground does not crawl.
        graph = ("split[s0][s1];[s0]palettegen=stats_mode=full[p];"
                 "[s1][p]paletteuse=dither=sierra2_4a:diff_mode=rectangle")
        encoded = os.path.join(work, "encoded.gif")
        subprocess.run(["ffmpeg", "-y", "-v", "error", "-f", "concat", "-safe", "0", "-i", listing,
                        "-vf", graph, "-fps_mode", "vfr", "-loop", "0", encoded], check=True)
        # Selecting frames 0..n-1 by number, each with its own delay, sets the
        # timing exactly and leaves out the concat list's repeated last frame.
        retime = ["gifsicle", "-O3", "--loopcount=forever", encoded]
        for i, (_, seconds) in enumerate(frames):
            retime += ["-d", str(round(seconds * 100)), f"#{i}"]
        subprocess.run(retime + ["-o", args.out], check=True)
    finally:
        shutil.rmtree(work, ignore_errors=True)

    seconds = 2 * args.hold + 2 * steps / args.fps
    print(f"wrote {args.out}: {len(frames)} frames, {seconds:.1f} s loop, {os.path.getsize(args.out) / 1024:.0f} KB")
    print(f"aligned pair: {stem}-before.png, {stem}-after.png")


if __name__ == "__main__":
    main()
