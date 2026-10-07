#!/bin/bash
# Mint Neat Edges' Steam-page section banners from shift-change's banner
# template (same CSS, embedded RimWordFont, gradient), then export with the
# sibling's headless-Chrome + trim recipe. Apparel Painter's banner-export.sh,
# with this mod's titles and a throwaway Chrome profile, so a Chrome that is
# already open is never touched.
set -eu

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# The constellation checks its repos out side by side; SHIFT_CHANGE_REPO
# overrides that assumption.
SHIFT_CHANGE="${SHIFT_CHANGE_REPO:-$REPO/../shift-change}"
TEMPLATE="$SHIFT_CHANGE/media/cards/_export/banner-what-it-does.html"
EXPORT="$REPO/media/cards/_export"
OUT="$REPO/media/cards"
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
PANEL="#1f242a"
PROFILE="$(mktemp -d)"

mkdir -p "$EXPORT"

python3 - "$TEMPLATE" "$EXPORT" <<'EOF'
import sys
template_path, export_dir = sys.argv[1], sys.argv[2]
src = open(template_path).read()
banners = {
    "banner-what-it-does": "What it does",
    "banner-compatibility": "Compatibility",
    "banner-recommended-with": "Recommended with",
    "banner-source": "Source",
}
needle = '<span class="t">What it does</span>'
assert needle in src, "template body changed"
for name, title in banners.items():
    out = src.replace(needle, f'<span class="t">{title}</span>')
    open(f"{export_dir}/{name}.html", "w").write(out)
    print("wrote", name)
EOF

for page in "$EXPORT"/banner-*.html
do
  name=$(basename "$page" .html)
  # Chrome 154 writes the screenshot and then never exits (2026-10-07, with
  # and without --timeout), so wait for the file and end that process here.
  rm -f "$EXPORT/$name.raw.png"
  "$CHROME" --headless --disable-gpu --hide-scrollbars \
            --user-data-dir="$PROFILE" --no-first-run --no-default-browser-check \
            --disable-extensions --disable-sync \
            --force-device-scale-factor=2 \
            --default-background-color=00000000 \
            --window-size=640,4000 \
            --screenshot="$EXPORT/$name.raw.png" \
            "file://$page" 2>/dev/null &
  chrome=$!
  for _ in $(seq 60)
  do
    [ -s "$EXPORT/$name.raw.png" ] && break
    sleep 0.5
  done
  sleep 1
  kill "$chrome" 2>/dev/null || true
  wait "$chrome" 2>/dev/null || true
  [ -s "$EXPORT/$name.raw.png" ] || { echo "no screenshot for $name" >&2; exit 1; }
  magick "$EXPORT/$name.raw.png" -trim +repage \
         -background "$PANEL" -flatten "$OUT/$name.png"
  rm "$EXPORT/$name.raw.png"
  echo "exported $name.png"
done
rm -rf "$PROFILE"
