# Media

Store-page media for Neat Edges, and how each piece was made. None of it
belongs in the upload, and there is no publish script yet to keep it out: the
in-game uploader sends the mod folder as it stands, so the first publish needs
an allowlist that leaves `media/` behind. Commands run from this folder.

| File | What it is |
|---|---|
| `hard-edges.gif` | the store card: soft edges, a fade to hard, and back |
| `hard-edges-before.png`, `hard-edges-after.png` | its two frames at 640×360 |
| `hard-edges-pair.png` | the two frames side by side |
| `cards/card-*.png` | gallery cards: bedrooms, church, lab, garden, bar, anima garden, purple bedroom, and the two trim swatches |
| `captures/` | every source the above were made from |

## hard-edges.gif

The media sandbox's "Neat Edges preview" stage (soft, then hard), captured
through the bridge at its 8×5 cell frame: steel tile in sand on one half,
marine fine carpet in lichen-covered soil on the other, with wood trims painted
cream down the carpet's outer side and along its bottom, and the hard-edge area
over the tile. Noon light. The sources are `captures/stage-soft.png` and
`captures/stage-hard.png`.

    python3 stage-card.py hard-edges captures/stage-soft.png captures/stage-hard.png

Each frame keeps the capture's bottom 4.5 rows (16:9 at eight cells across),
is authored at 1280×720 under the house title (88 pt, a 180 px black-to-clear
fade) and exported at 640×360; the loop is `transition-gif.py` on the two.
Output lands in `build/`. The title font is Shift Change's copy,
`../../shift-change/media/RimWordFont.ttf`.

## Gallery cards

Each card is centred on its building and faded at the edges: a rounded
vignette easing to 60% brightness over the outer 14% of the half-size.

    python3 card-centre.py captures/<scene>.png <scene>-crop.png
    python3 card-fade.py <scene>-crop.png cards/card-<scene>.png --no-crop

`card-centre.py` finds the mirror axes; check them against the building, and
crop by hand where they disagree. Each card's numbers:

| Card | Source | Centred on | Crop | Size |
|---|---|---|---|---|
| bedrooms | bridge capture of cells 106..132 × 138..158 at root size 13 (65 px per cell) | the four-bedroom block, found from the green carpet corridors | `card-fade.py` without `--no-crop` | 1130×910 |
| church | screenshot, 693×864 | the walls' outer outline, x 51..644, y 43..811 (the mirror axis agrees, 347.5) | 690×854 at +2+0 | 690×854 |
| lab | screenshot, 724×726 | the walls' outer outline, x 72..656, y 73..657 (mirror axis 364.0) | 720×722 at +4+4 | 720×722 |
| garden | screenshot, 901×802 | across: the rose bed, x 453.5; down: the beds' frames, y 238..566 (the mirror axis read 396.5, pulled up by the plant art) | 894×800 at +7+2 | 894×800 |
| bar | screenshot from the main save, 944×651 | across: the mirror axis, x 471.5; down: kept as framed | 942×651 at +0+0 | 942×651 |
| anima-garden | screenshot, 929×923 | the walls' outer outline, x 112..818, y 110..816 (centre 465.5, 463.5), framed so the four outer braziers sit in the corners (the mirror axis read low, pulled by the grass and the flames) | 719×721 at +106+103 | 719×721 |
| purple-bedroom | screenshot, 533×501, zoomed out far enough that the bed's owner label is not drawn | the purple carpet corridors, x 81..111 and 423..453, y 80..110 and 391..421 (the mirror axes agree, 267.5 and 251.0) | 531×500 at +2+1 | 531×500 |

The church, lab, garden and bar screenshots are about 42 px per cell; the
bedroom capture is 65, the anima garden 26 and the purple bedroom 31. The
church, lab and garden are in the media sandbox: slate walls and paving; the
garden's beds in limestone trims with corn, potatoes, strawberries and roses;
the rooms north and south of it in marine fine carpet with silver trims and
silver columns. So are the anima garden and the purple bedroom. The anima
garden is an octagon of totemic boards round an anima tree and its grass, each
quadrant ringed inside and out in one trim style (gold inlay, vine painted
pastel green, slate pebbles, wood painted dark mauve), on granite flagstone.
The purple bedroom and its corridors are deep purple fine carpet edged in
inlay.

## The trim swatches

`cards/card-trims.png` (a corner of each style over every piece of the plain
border) and `cards/card-silver-vines.png` (the vine in silver, unpainted and in
three golds) are drawn rather than captured:

    uv run --with numpy --with pillow python3 trim-swatch.py

It lays the shipped strips out through `../devtools/strip_trim.py`, the
geometry `check_trims.py` holds to the golden file, and tints each layer as the
game does: the strip's grey times the material's stuff colour, and the vine's
overlay times the paint's. The colours are vanilla's: wood, slate blocks, gold
and silver, and the green, pastel orange and mustard paints. There is no
lighting, so this is the game at full daylight, on a flat floor. The labels are
lower case because the font's capital O is the star from the game's logo, and
its lower case sets the same capitals with a plain O. From a checkout outside
the constellation, set `RIMWORD_FONT` to the font. The trims card is also on
the Workshop page; the silver vines card is not in the gallery.

## transition-gif.py

Makes a looping GIF from a before/after pair: a hold on each state with a
fade between, 1.5 s holds and 0.6 s fades at 20 fps by default. The two shots
must share a camera: it aligns them by the ground they have in common, refuses
a pair whose unchanged ground does not match at any offset (a zoom or lighting
change would make the whole frame swim during the fade), and also writes the
aligned pair as `<name>-before.png` and `<name>-after.png`.
`--hold`, `--fade` and `--fps` set the timing. Needs numpy, ImageMagick,
ffmpeg and gifsicle.

## About/Preview.png

The store card's GIF bytes under the PNG name, unchanged:

    cp hard-edges.gif ../About/Preview.png

The Workshop page plays it as the animated preview. Steam's own client UI
shows it as a still or not at all, which is acceptable: the Workshop page is
the one that matters. Steam rejects a preview over 1 MiB, and this one is
488 KB; `devtools/check-invariants.py` fails the file past the limit.
Regenerate the GIF and copy it again whenever the store card changes.

## steam-description.bbcode

The Workshop page. Its images are the four section banners and the trims card,
linked from the repo's `main`, so `media/` has to be pushed before the page
shows them; the store card is the item's preview, and the other cards go in
the item's gallery.
Check it before every upload (an unclosed tag turns the rest of the page into
raw text):

    python3 ../devtools/bbcode-preview.py --inline steam-description.bbcode

The Flooring Floors comparison in it was checked against that mod's own defs
(`ThingsDefs.xml`: one border, `KS_StoneBorderDark`, Stony only, a single
rotatable straight) and its border art (a corner is two strips overlapping).

## Section banners

`cards/banner-*.png`, one per section of the Workshop page, from Shift
Change's banner template so all three mods' pages share a style:

    ./banner-export.sh

It renders each title through headless Chrome on a throwaway profile, then
trims and flattens onto `#1f242a`. Chrome 154 writes each screenshot and never
exits, so the script waits for the file and ends that render itself. The
titles are the `banners` table in the script.
