# DEVELOPMENT

Build, test loop, and what lives where.

## Build

```bash
dotnet build Source/NeatEdges/NeatEdges.csproj -c Release
```

Two configurations, `Debug` and `Release`, and **both write the same
`Assemblies/NeatEdges.dll`**. There is no hot-reload rig here — this mod has
almost no UI to iterate on, so the cost of the rig is not repaid. End a session
on Release.

Game references resolve two ways, on install presence rather than
configuration: a machine with RimWorld compiles against the real assemblies (so
go-to-definition decompiles engine bodies), and a machine without one falls back
to `Krafs.Rimworld.Ref`. To reproduce the reference-less path locally, pass
`-p:DisableLocalGameRefs=true`.

Harmony is a compile-time package only — `ExcludeAssets="runtime"` keeps
`0Harmony.dll` out of `Assemblies/`, because Harmony ships as its own mod and
`Assemblies/` must hold exactly our one dll.

## Checks

```bash
python3 devtools/check-invariants.py
```

Static checks — no game, no build, no network. CI runs this same script, so a
green run here is a green run there. It prints every failure it finds, then
exits non-zero.

| Check | Why it exists |
|---|---|
| hot-reload | No `private` members and no auto-properties. A hot-swapped method body runs in a separate assembly and Mono honours only `InternalsVisibleTo`, so a private member throws `FieldAccessException` the first time a swapped body touches it — and both forms compile clean. There is no swap rig here yet (see Build, above); the convention is kept so that adding one stays a tooling change. |
| keys | A key that exists in code but not in the keyed XML renders as its own raw text on screen, and `TryGetTextFromKey` logs nothing. Checks both directions against `Languages/English/Keyed/NeatEdges.xml`: every key used is defined, every key defined is used. |
| bindings | XML names C# types as strings. Rename the type and the XML keeps the old name: the comp never attaches, the designator never appears, the mod loads without error, and nothing happens. Covers `Class=""`, `…Class` elements and bare `<li>` type lists such as `specialDesignatorClasses`. |
| preview | Steam rejects a Workshop preview over 1 MiB and the game does not check, so an oversized one fails mid-publish. Absent is only a note until first publish. |
| patch-root | A patch file whose root is not exactly `<Patch>` has every operation silently discarded. `Patches/NeatEdges_Designators.xml` is the one it guards today. |
| release-contents | Two lists decide what a player installs: `publish-workshop.sh`'s `CONTENT` stages the Workshop copy, and the CI release job copies the GitHub zip. Each looks complete on its own. They drifted: `Patches/` and `Languages/` reached the allowlist and never the zip, so the v1.0.1 zip put no area tools on the Zone tab and showed raw keys for every string the C# shows. Fails if the two differ, or if either line can no longer be found. |
| tracking-ref, internal-vocab, home-path | This repository is public. Tracker identifiers and internal working vocabulary mean nothing to a reader here, and an absolute home path both names the author and pins a script to one machine. All three had already reached `run-harness.sh` and been stripped by hand; nothing stopped them coming back. Use `$HOME` or an env override for paths, and state the *reason* in a comment rather than citing a ticket. |
| harness | The guard is whole-file, never inline — one `#if HARNESS` as the file's first code line, `#endif` as its last, no `#else`. That is what makes a harness build and a shipping build differ by the presence of whole types and by nothing else, so a harness run stays evidence about the assembly that goes out. Also fails on a harness file nobody wrapped, and on a launch flag read from anywhere else. |

```bash
python3 devtools/check-shipped-dll.py [path/to/NeatEdges.dll]
```

The other half of the same rule, on the artifact instead of the source, ported
from the siblings on 2026-09-18. Defaults to `Assemblies/NeatEdges.dll`;
CI runs it three times — against the committed dll before anything is built,
against the freshly built Release dll, and against the dll the release job is
about to zip.

| Check | Why it exists |
|---|---|
| no harness types | `HarnessBoot` and `HarnessDriver` are compiled out of a plain `-c Release` build. Their presence means a Debug or `-p:Harness=true` dll is sitting in the load path — `run-harness.sh` writes that path and sweeps itself afterwards, and this catches the run where the sweep did not happen. |
| no launch flag | `neatedges-harness` is a string literal, and .NET keeps literals in `#US` as **UTF-16** while type names live in `#Strings` as UTF-8. An ASCII grep for the flag therefore finds nothing whether or not it shipped: measured on the harness build, utf8=0 and utf16=2. The check encodes `utf-16-le` explicitly. |
| no "harness" anywhere | The catch-all, and the only check that covers code nobody has written yet — whatever a future harness file is called, it will contain the word. Searched in both encodings, so it also catches the driver's `GameObject` name. The bar is zero, measured, not a threshold. |
| feature surface present | Absence checks pass trivially on an empty or truncated file. This mod is unusually exposed to over-gating, because everything it does is invisible by design: a dll that loads, patches nothing and hardens no edge looks exactly like a map with no markers on it. |

`DebugTools_NeatEdges` is deliberately **not** on the forbidden list. The
siblings forbid every `DebugTools_*` type they have, so the reflex when reading
this is that ours was missed. The constellation's bar for the debug menu is
destructiveness, not reachability, and this toggle destroys nothing, persists
nothing and is undone by pressing it again.

## The dev loop

The game loads `RimWorldMac.app/Mods/NeatEdges`, which is a symlink to this
checkout. **A build reaching disk is not the same as the game running it** — if
that entry is ever a real directory rather than a symlink, it is release-staging
residue and the game is loading stale bits. `run-harness.sh` refuses to run when
the two disagree, for exactly that reason.

Def and texture changes need a full restart. Never hot reload defs: vanilla's
own command and the community mod both corrupt live state.

**The startup line is the first thing to read after a restart:**

```
[NeatEdges] terrain edge patch applied — mask@… store@… (of 2 candidates, …)
```

`NOT APPLIED` means the transpiler could not find its anchors and terrain is
rendering as vanilla. The candidate count is there because an ambiguous anchor
once matched the wrong-but-plausible instruction and produced a patch that
applied perfectly and did nothing.

## Textures

Every texture is generated, never hand-edited:

```bash
python3 devtools/make_edge_art.py
python3 devtools/make_trim_art.py
uv run --with numpy python3 devtools/make_trim_styles.py
python3 devtools/check_trims.py
```

`make_edge_art.py` draws the marker, the area tools and the overlay toggle's
icon. The toggle icon is pixel art on vanilla's 24 px toggle grid, in the values
sampled from that row at 1:1: flat grey 124, a pure-black 1 px outline, 121 on
the shape's right and bottom edges, 2 px of margin, no anti-aliasing. It is
written at 2x so it stays sharp at larger UI scales. Edit the `FRAME` grid, not
the PNG, and resample the game before changing the palette.

`EdgeOne` is only ever the marker's placement ghost and build-menu icon —
nothing is drawn on the map. That is not optional decoration: a fully
transparent texture was tried first and made the marker unplaceable, because
the ghost, the rotation preview and the selected thing all draw from the same
graphic.

`AreaExpand` and `AreaClear` are the paint and clear tools' icons, drawn to sit
with vanilla's area tools on the Zone tab. Those are flat colour with a 2 px
black outline, and every clear tool is its expand tool under one shared slash.
The picture follows Perspective: Paths' own area icon, laid out as that one
measures but drawn fresh: a soft square behind at upper left and a crisp square
in front at lower right. Ours is in two colours, sandy soil behind and cut stone
in front, each with an interior trim just inside its outline, which is the
mod's own theme. It avoids the area's cyan, which is the home area's colour in
those menus, and it is warmer than vanilla's cool shrink-zones grey. The soft
square is drawn with its outline and trim and then blurred as one layer. The
clear tool is the same picture under vanilla's slash, replicated from
measurements: red (152, 27, 32) with a 2 px black outline, 37.1 degrees from
lower left to upper right, about 9 px thick, vertically cut ends, running x 7
to 54 on a 64 px icon. Both are written at 2x, and supersampled so the slash's
diagonal edges come out smooth.

`EdgeOne.png` puts its band in the **north** margin, and `Graphic_Single`
rotation spins it, which is what makes `Rotation` name the hugged edge with no
offset. Change that art and the relationship must be re-measured — the harness
pins it (`mask.rotation.*`) so an inversion fails loudly.

`make_trim_art.py` writes the floor border's eight PNGs under
`Textures/NeatEdges/Trim/`: its strip and a build-menu icon for each of its
seven shapes. The strip, `FloorBorderStrip.png`, is 256 × 192, three bands of
64 rows: the straight border's north band, row for row, its south band, and
the two averaged depth by depth, which a diagonal draws along a wall running
north-west to south-east. `Graphic_StripTrim` lays it out as geometry, a band
along each edge a shape covers, cut at 45° where two meet, so a shape is a set
of edges rather than a texture (see DESIGN §9). The script still draws every
straight-family shape at every facing, as the trims shipped them until
2026-10, but writes none of those drawings to disk: they are the reference the
strip is checked against. The end cap and the frame are drawn by `_frame`, the
corner's joining rules generalised to any set of edges. The diagonal has no
drawing; its icon, like every icon of the other three styles, is laid out from
the strip by `trim_icons.py` and rendered by `strip_render.py`. Icons are
128 px (`trim_icons.ICON`): a build button draws about 75, and 28 icons at 256
would take 2.4 MB of video memory rather than 0.6. Everything is greyscale, so
the stuff tints it and paint recolours it. The script came from Fine
Establishments with the first three trims; `trim_kit.py` is the part of that
mod's texture kit it needs: the canvas, the house greys and the PNG writer,
plus a reader for the PNGs it writes.

`make_trim_styles.py` draws the other three styles: the inlay, the vine and
the pebbles. Each is drawn once, as a height map, an albedo and a coverage
across one band and along its length, then lit three ways, from the band's
outer edge, from its inner side and side-on, with the floor border's ambient
and diffuse so a flat face lands on its 180, and written as one three-band
strip with seven icons. The light runs across the band only (DESIGN §9 has
why). The vine and the pebbles are opaque from the tile's edge, so they cover
the edge as the floor border does. The vine's field runs from the edge to its
dark keyline, with a rounded lip set in from the edge, and its leaves, carved
in relief, reach past both lines: over the field toward the edge, and past the
keyline onto the floor, where only the leaves are opaque and cast their shadow.
Their strips are 1024 wide, four one-tile
variants side by side, and each variant begins and ends alike: the vine's stem
leaves a tile at the height and slope it entered, the one leaf that crosses a
tile's end is the same leaf in every variant, and the pebbles leave grout at
both ends. The baker wraps its lighting and blurs along the band, as the strip
wraps in the game, so that leaf is lit at every joint as it is mid-run. The
vine itself, stem and leaves, is its paint overlay: the baker draws it apart
from the rest, keeping each texel that stands higher than the berries, bakes
it on its own and writes its three bands mirrored below the trim's others,
which makes the strip 1024 × 384 (DESIGN §9). The rest keeps the berries and
every shadow, the vine's included. It needs numpy, so it runs under
`uv run --with numpy`; the rest of the art kit is standard library only.

The geometry exists twice: `StripTrimGeometry.cs`, which the game runs, and
`devtools/strip_trim.py`, which the checks run. Both are pinned to
`devtools/strip_trim_geometry.txt`, every vertex and UV of every shape at every
rotation at one cell. `check_trims.py` fails if the Python drifts from it, and
the harness's `trims.geometry` if the C# does. A deliberate change goes into
both, and then the file is rewritten:

```bash
python3 devtools/strip_trim.py --write-golden
```

`check_trims.py` renders every shape at every rotation from the strip with
that geometry, a texel to a pixel, and compares each with its drawing. The
straight and the runner come out identical. A piece with a mitre may differ in
three 2 × 2 squares at each mitred corner: the outer corner, the groove
crossing and the inner lip corner, where a butt joint and a mitre draw
genuinely different texels. A difference anywhere else fails the run, and so
does the strip no longer being the straight's rows, the runner's rail drifting
off the straight border's, `_frame` no longer redrawing the twelve straight,
corner and runner facings pixel for pixel, or anything in the trim folder but
the four strips and their 28 icons. A strip with variants is measured at its
joints, since any variant may follow any, and fails past `SEAM_TOLERANCE`, 24
of 255, on either measure: how far the variants' first and last columns differ
from one another, and how far a joint steps beyond the largest difference
between neighbouring columns within three of it. A leaf drawn across the joint
passes the second, a stem leaving every tile at another height than it entered
fails it (a deliberately broken copy of the vine measured 255), and the run
prints both. Grey is weighed by alpha, as it is drawn, because the vine leaves
texels transparent past its field and their stored grey means nothing. Both
halves of a strip with a paint overlay are measured, and an empty overlay
fails; the run prints how much of its half the overlay covers. Then it
composes the rendered trims on two
sheets, at the defs' drawSize (exactly one tile), because no trim is ever seen
alone. `dist/_trims.png` has the runs: a corridor of runners, runners meeting
straight borders, east beside west, and a run that stops.
`dist/_trimshapes.png` has the joins: an inside corner around a wall block, a
one-wide path capped at both ends, a runner turning a corner (a corner and an
inside corner stacked on the turning tile), and frames. Each style gets a sheet
of its own, `dist/_trims_<style>.png`: an octagonal room and a diamond, from
inside and from outside, their joins close up, and a corridor of runners. The
sheets cannot show what the game's sampler does where two quads meet; for
that, see the joints scene below. The trims harden the edges their bands run along, and the harness
pins every rotation (`trims.masks.*`).

A `.dds` beside a PNG silently shadows it with no timestamp check, so a
regenerated texture can appear not to change. Faster Game Loading writes them
into the mod folder of any game that runs it, and every other instance that
loads the mod through the same folder then draws its block-compressed copy.
`*.dds` is gitignored; the generator deletes the `.dds` beside each PNG it
writes, and deleting one by hand fixes a texture that refuses to update.

Whether two trims meet cleanly is a question about the game's sampler as much
as the art, so it is checked in a running game, on a layout with every joint
the trims make: a corridor crossing of runners with an end cap on each arm and
an inside corner in each corner, a room edge of straights and corners with an
arm joining it through two inside corners, open ends, a runner giving way to a
straight, and frames. Two captures matter: a close zoom near the art's own
resolution, which shows a one-pixel line for what it is, and the game's
closest normal zoom, where tile boundaries fall between pixels and a sampling
seam would shimmer. Paint the trims a light colour for it; unpainted wood on a
wood floor hides most of what there is to see. The diagonals' joins need
diagonal walls beside them, so they are checked on rooms built with Diagonal
Walls 2 the way players build them: an octagon from inside and from outside, a
diamond, and a room with bevelled corners. Pick the stuff to contrast with the
ground: granite pebbles on granite flagstone show nothing.

## File map

| File | Job |
|---|---|
| `Patch_SidedFadeBlock.cs` | the transpiler, the three inserted calls, and the mask computation |
| `OwnMaskCache.cs` | per-operation memo for the one function that reads the thing grid and the area |
| `BlocksTerrainFade.cs` | the marker extension — `edges` in quarter-turns from `Rotation` |
| `Building_InvisibleEdge.cs` | `Print` no-op, and the gradient selection highlight |
| `Patch_SidedFadeInvalidate.cs` | dirties the terrain mesh on spawn/despawn |
| `Area_HardEdges.cs` | the painted area: its `Set` repaint, get-or-create, and the duplicate merge |
| `Designator_AreaHardEdges.cs` | its paint and clear tools |
| `Patch_AreaMigration.cs` | loads Perspective: Paths' saved areas as ours, and announces it once |
| `PerspectivePathsInterop.cs` | the other direction, while it is installed: hides the area tools and moves our area into its zone |
| `MapComponent_EdgeOverlay.cs` | the overlay drawer, and the area's load-time housekeeping |
| `Patch_EdgeOverlayToggle.cs` | puts it on the bottom-right toggle row |
| `DebugTools_NeatEdges.cs` | the compare toggle |
| `Harness.cs` | the regression cases — see [TESTING.md](TESTING.md) |
| `HarmonyInit.cs` | `PatchAll`, and the startup anchor report |
| `Graphic_StripTrim.cs` | draws a trim from its strip: prints built trims into one submesh with their colour in the vertices, and blueprints and the ghost on their own material; reads a diagonal's neighbours for its ends |
| `StripTrimGeometry.cs` | the bands, mitres and UVs of each shape, the diagonal's four kinds of end, and the variant pick, mirrored by `devtools/strip_trim.py` |
| `TrimPiece.cs` | the extension naming a trim's shape, its strip's band count, variants and paint overlay, read through a blueprint to the def it builds |
| `CompTrimJoins.cs` | reprints the trims around one that is built or removed, so a diagonal across a section's edge takes its new end; given to every trim def at startup |
| `Designator_TrimShape.cs` | one build button per shape: a left click places the style on it, a right click lists the rest; built at startup for every shape of trim in a category |

Outside `Source/`: `Defs/ThingDefs_Buildings/` holds the marker
(`NeatEdges_Edges.xml`) and the trims (`NeatEdges_Trims.xml`),
`Defs/Misc/NeatEdges_TrimShapes.xml` holds each shape's dropdown group,
`Patches/NeatEdges_Designators.xml` puts the two area tools on the Zone tab,
and `Languages/English/Keyed/NeatEdges.xml` holds every string the C# shows a
player. `devtools/` holds the art generators and the renderer they share
(`strip_render.py`, `trim_icons.py`), the trims' geometry model, its golden
file and their check (see Textures), the two static checks and the harness
runner.

## Things that will waste an afternoon

**Placing a thing dirties `Things`, not `Terrain`.** `SectionLayer_Terrain`
declares `relevantChangeTypes = MapMeshFlagDefOf.Terrain`, so without
`Patch_SidedFadeInvalidate` the mask is only read on a map load and the whole
feature looks dead. Any new carrier needs that invalidation too. The painted
area is one: vanilla's area bookkeeping dirties no mesh at all, which is why
`Area_HardEdges` overrides `Set`.

**Never add a scribed field to `Area_HardEdges`.** Its saved node is the base
`Area`'s, an ID and a grid, and that identity with Perspective: Paths' node is
the whole migration. A new field would load at its default on every adopted
area.

**Drag placement is `drawStyleCategory` and nothing else** —
`placingDraggableDimensions` and `DraggableDimensions` do not exist in 1.6. If a
marker will not drag, suspect **Perfect Placement**
(`remi.perfectplacement`), which rebinds hold-left-mouse to rotate the ghost and
has a setting to turn that off. It affects every rotatable building, not ours
specifically.

**`WorkToBuild` must stay above zero.** `Designator_Build.DesignateSingleCell`
branches on `== 0f` into the same path as god mode — direct spawn, no blueprint,
no job — which places instantly and leaves nothing to cancel.

**The transpiler is anchored, not copied.** Re-diff its anchors against the
decompile on a game update; the mask logic is ordinary C# and moves freely.
