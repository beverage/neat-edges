"""Give a gallery card its edge fade, and optionally centre it first.

    python3 card-fade.py SRC OUT --no-crop [--edge 0.6] [--width 0.14]

With --no-crop the image is faded as framed (every card so far: centre it with
card-centre.py first). Without it, the crop is centred on the bedroom block,
found from the green carpet corridors that frame it. The fade is a rounded-rectangle
vignette to black: full brightness inside, easing (smoothstep) down to EDGE at
the border, over WIDTH of the half-size inward; corners run a little darker,
as a vignette does.
"""
import argparse
import subprocess
import numpy as np


def runs(on):
    out, start = [], None
    for i, v in enumerate(on):
        if v and start is None:
            start = i
        if not v and start is not None:
            out.append((start, i - 1))
            start = None
    if start is not None:
        out.append((start, len(on) - 1))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("out")
    ap.add_argument("--edge", type=float, default=0.6, help="brightness left at the very edge (default 0.6)")
    ap.add_argument("--width", type=float, default=0.14, help="fade depth as a share of the half-size (default 0.14)")
    ap.add_argument("--no-crop", action="store_true", help="fade the whole image as given (already framed)")
    args = ap.parse_args()

    w, h = map(int, subprocess.run(["magick", "identify", "-format", "%w %h", args.src],
                                   capture_output=True, text=True, check=True).stdout.split())
    raw = subprocess.run(["magick", args.src, "-alpha", "off", "-depth", "8", "rgb:-"],
                         capture_output=True, check=True).stdout
    img = np.frombuffer(raw, np.uint8).reshape(h, w, 3).astype(float) / 255

    if args.no_crop:
        crop = img
        ch, cw = crop.shape[:2]
        print(f"fading {cw}x{ch} as framed")
    else:
        green = (img[..., 1] > img[..., 0] + 15 / 255) & (img[..., 1] > img[..., 2] + 5 / 255)
        xs = runs(green.mean(axis=0) > 0.8)
        zs = runs(green.mean(axis=1) > 0.8)
        if len(xs) < 2 or len(zs) < 2:
            raise SystemExit(f"could not find the corridors: x {xs}, y {zs}")
        cx = (sum(xs[0]) / 2 + sum(xs[-1]) / 2) / 2
        cy = (sum(zs[0]) / 2 + sum(zs[-1]) / 2) / 2
        half_w = int(min(cx, w - cx))
        half_h = int(min(cy, h - cy))
        x0, y0 = int(round(cx - half_w)), int(round(cy - half_h))
        crop = img[y0:y0 + 2 * half_h, x0:x0 + 2 * half_w]
        ch, cw = crop.shape[:2]
        print(f"block centre ({cx:.1f}, {cy:.1f}) of {w}x{h}; crop {cw}x{ch} at +{x0}+{y0}")

    # Rounded-rectangle distance from the centre, 1.0 on the border midpoints.
    u = np.abs(np.arange(cw) + 0.5 - cw / 2) / (cw / 2)
    v = np.abs(np.arange(ch) + 0.5 - ch / 2) / (ch / 2)
    uu, vv = np.meshgrid(u, v)
    p = 6.0
    r = (uu ** p + vv ** p) ** (1 / p)
    t = np.clip((r - (1 - args.width)) / args.width, 0, 1)
    t = t * t * (3 - 2 * t)
    factor = 1 - (1 - args.edge) * t
    out = np.clip(crop * factor[..., None], 0, 1)

    data = (out * 255 + 0.5).astype(np.uint8)
    subprocess.run(["magick", "-size", f"{cw}x{ch}", "-depth", "8", "rgb:-", args.out],
                   input=data.tobytes(), check=True)
    print(args.out)


if __name__ == "__main__":
    main()
