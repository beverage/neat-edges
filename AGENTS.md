# AGENTS.md

A RimWorld 1.6 mod that stops terrain fading across a tile boundary. An
invisible marker, a drag-painted area and visible trims, four styles of seven
shapes each; one Harmony assembly; generated textures. It ships no floors and no terrain of its own — it
changes how *other* people's floors meet what they touch.

| Doc | Contents |
|---|---|
| [README.md](README.md) | what the mod does, for players |
| [docs/DESIGN.md](docs/DESIGN.md) | the blend gate, the three mechanisms, why sided rather than area, the painted area and its migration, the trims |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | build, dev loop, file map |
| [docs/TESTING.md](docs/TESTING.md) | the harness, and what it deliberately does not cover |

## Verify your work

```bash
dotnet build Source/NeatEdges/NeatEdges.csproj -c Release
```

`xmllint --noout` on every changed def. Textures are **generated, never
hand-edited** — edit the generator and re-run it: `devtools/make_edge_art.py`
for the marker ghost and the area tools, `devtools/make_trim_art.py` for the
floor border and `devtools/make_trim_styles.py` (it needs numpy) for the other
three styles. After any trim art change, run `devtools/check_trims.py`: no trim
is ever seen alone, and the sheets compose runs the way the engine draws them.

After any change under `Defs/`, run the constellation's hash gate against the
last committed version (`hash-preflight.py mod <this mod> --previous <a
checkout of it>`): a new or renamed def, and the Blueprint and Frame names the
engine generates from a buildable one, can push a def that saves store by slot
number, which silently breaks existing saves. It has caught one here: the
pebble and vine families are `NE_PebblesBorder` and `NE_VinesBorder` because the
singular names put a frame on Jade's slot and a blueprint two slots below
MineableGold's. The comments above each family say so; never rename them back.

```bash
python3 devtools/check-invariants.py
python3 devtools/check-shipped-dll.py
devtools/run-harness.sh
```

**The harness and `-neatedges-harness` ship in no configuration.** They sit
behind `#if HARNESS`, which every configuration except a plain `-c Release`
defines; `run-harness.sh` asks for them with `-p:Harness=true` and sweeps
`Assemblies/` back to the shipping dll when it finishes. Dev tooling is not a
player's to carry.

**`HARNESS` is a whole-file guard, always.** Never write `#if HARNESS` inside a
file that ships: the point is that a harness build and a shipping build differ
by the presence of whole types and by nothing else, so no shipping code path
changes shape between the build under test and the build that goes out.
`check-invariants.py` fails on an inline guard, on an unwrapped harness file,
and on a launch flag read anywhere else.

Those two scripts are the two halves of the same rule and neither subsumes the
other. `check-invariants.py` asserts the shape of the SOURCE, which is where
the mistake gets made; `check-shipped-dll.py` asserts the outcome on the
ARTIFACT, which is what a player receives. Run the second one after anything
that writes `Assemblies/` — a harness run whose sweep did not happen leaves a
harness dll sitting in the load path, and it is a build away from being the one
you commit. It also checks the reverse failure: over-gate the mod and the
absence checks all pass on a dll that does nothing, which is invisible in game
because a working hard edge looks like a map with no markers on it.

The harness does not cover rendering. Every claim about what the map LOOKS like
was checked by placing markers in a running game and looking at the result. Do
not claim a rendering change works without saying how it was checked.

## What this mod actually does

`SectionLayer_Terrain.Regenerate` paints a neighbouring terrain's fade into a
cell as a 9-vertex fan, and **the receiving cell never gets a say** — the gate
reads terrain defs and the foundation grid, nothing else. This mod transpiles
that method and gives the cell a say, driven by the markers standing on it and
by the painted area.

Three concepts, and they are not interchangeable:

| | What it does |
|---|---|
| **Substitution** | swaps a direction's terrain for `Underwall` (Hard, precedence 0) so it stops being a *source* of fade |
| **Corner sealing** | a diagonal whose corner point a neighbour's hardened edge already closes |
| **Vertex pinning** | forces a corner vertex dark *after* the mask loop, regardless of what lit it |

Pinning exists because substitution cannot clear a vertex: a cardinal match
lights three rim verts — itself and both flanking diagonals — so an open
neighbouring cardinal re-lights the very corner a hardened edge just sealed.
That was the "a lone edge leaves both flanking tiles fringed" bug. Both
mechanisms are needed; neither replaces the other.

**The renderer consumes `RenderMaskAt`, not `EdgeMaskAt`.** A pin darkens its
vertex for every fade on the cell, so a layout pin also cut fades that never
crossed a hardened edge: two grounds meeting under a floor's hardened edge met
in a hard line there. `RenderMaskAt` drops a pin only when every fade reaching
that corner comes through an open side of its own terrain, no open side there
is a seam inside the cell's own surface, and no hardened edge there holds the
fade's terrain back (DESIGN §4). It replays diagonals unsubstituted, because
dropping a pin un-substitutes its diagonal in the renderer too.
**Keep `EdgeMaskAt` a pure function of the marker layout** — the harness's mask
cases assert exactly that — and put anything that reads terrain in the render
step, whose cases paint terrain fixtures.

## Rules that validate fine and fail later

**`Patch_SidedFadeBlock` is a TRANSPILER with three anchors, not a copied
body.** A copy was tried first and silently stripped every Dub's Paint Shop
colour, because a prefix that skips the original also skips every other mod's
transpiler. Anchors fail safe — any one missing logs once and passes the method
through untouched — but they cannot tell a right instruction from a plausible
wrong one, which is why the startup log line reports each anchor's position and
candidate count. Re-check them on every game update.

**The patch deliberately covers `SectionLayer_Watergen` too.** That class
subclasses `SectionLayer_Terrain` and inherits `Regenerate`, so patching here
catches both, and the body dispatches through the virtual `GetMaterialFor`.
Water depth masks on the same edges for free. Do not "fix" this by narrowing
the patch to the base class.

**Placing a thing dirties `Things`, not `Terrain`.** Without
`Patch_SidedFadeInvalidate` the mask is only ever read on a map load, and the
feature looks completely dead. Any new carrier needs that invalidation too.

**`WorkToBuild` must stay above zero.** `Designator_Build.DesignateSingleCell`
branches on `== 0f` into the same path as god mode — direct ThingMaker/GenSpawn,
no blueprint, no frame, no job — which places instantly and leaves **nothing to
cancel**. Free of materials is the goal; free of work is a trapdoor.

**The markers draw nothing on the map, and that is C#, not the texture.**
`Building_InvisibleEdge` no-ops `Print`. A fully transparent texture was tried
first and makes the marker unplaceable: ghost, rotation preview and selected
thing all draw from the same graphic. `drawerType None` is not the answer
either — no vanilla building uses it, and it would suppress the ghost as well.

**`edges` is in quarter-turns from the thing's own `Rotation`**, so one entry
covers all four facings. The band in `EdgeOne.png` sits in the north margin, so
`Rotation` names the hugged edge with no offset — measured by decoding the PNG,
not assumed. Change the art and that relationship must be re-measured.

**The overlay must ship.** A marker leaves no trace anywhere in the vanilla UI:
the terrain readout names the floor, the build menu shows the floor, and the
only way to remove one is to deconstruct a thing you cannot see. The toggle on
the bottom-right row is the sole way to find one. It asks which markers stand
on each cell, not the derived mask: one marker tidies six tiles, and asking the
derived mask turned overlapping runs into blobs that located nothing. The
painted area shows on the same toggle through its own drawer, in the same
colour, and is deliberately not counted in the marker predicate, or every
painted tile would be tinted twice.

**Map components are scribed by type.** `MapComponent_EdgeOverlay` is compiled
unconditionally; one that existed only under a build symbol would make every
save taken with it log a missing-type error without it.

## The painted area

**A painted tile is four hardened edges to the mask, and nothing more.** It
enters `ComputeOwnMask` as the four cardinal bits, and everything after that is
the sided model unchanged. Keep it there. An area that short-circuits the model
instead (skip the fan on the painted tile) protects only the receiving side,
which is Perspective: Paths' behaviour and the thing this design exists to beat.
The harness pins the equivalence cell by cell against four stacked single-edge
markers.

**There is no all-sides marker, on purpose.** One shipped briefly before
release and was removed once the area covered whole tiles for free; no save
ever kept one. Do not bring it back: it duplicates the area at the cost of a
build order per tile.

**`Area_HardEdges` saves exactly the base `Area` node, and must keep doing so.**
An ID and a grid is also exactly what Perspective: Paths writes, and that
identity IS the migration. A scribed field added here loads at its default on
every adopted area.

**Its class name is permanent.** `NeatEdges.Area_HardEdges` is written into
every map that has one. A rename needs a row in `Patch_AreaMigration.Adopted`,
the same table that answers for Perspective: Paths.

**Painting has to dirty the terrain mesh itself.** Vanilla's area bookkeeping
refreshes the area overlay, the pathfinder and the region, and never a mesh,
so the `Set` override is what makes a painted tile repaint. `Area.Clear()` and
`Area.Invert()` bypass `Set`; nothing calls them on this area, because it is
not player-deletable and so never appears in the manage-areas dialog.

**The migration answers only a null lookup.** While Perspective: Paths is
installed its class resolves first and the postfix never fires. Never declare a
type in another mod's namespace to catch its saves: it collides the moment both
are loaded.

**While Perspective: Paths is installed, whole-tile painting is its job.**
`PerspectivePathsInterop.Installed` (its area class resolves, the same name the
migration answers for) hides the two area tools and makes them refuse every
cell, because architect search activates a hidden tool when it is the only
match. At map finalization any area of ours moves into its zone, found by type
or made through its own constructor and label, and ours is removed. So
whichever mod is installed owns the painted tiles, and it owns them when both
are. The marker, the trims and the overlay stay: its zone cannot say one side.
Its patch on `Regenerate` lands before ours in every load order, and our vert
anchor sits five instructions later for it; see DESIGN §8.

**The area is created on first paint, never with the map**, and a map that
loads with two is merged down to one at finalization. Everything resolves THE
area as the first one found, so a second would keep hardening tiles the clear
tool cannot reach.

**An empty area must never reach a save.** Clearing the last painted tile
removes the area (`Area_HardEdges.Discard`, from the `Set` override), and map
finalization drops any that loads empty, which every map adopted from
Perspective: Paths does, since it adds an empty zone to each. The reason is
removal: a saved area whose class is missing costs the map its whole area
list (DESIGN §8), so clearing every tile is how a player makes a map safe to
remove this mod from. Keep both paths, and keep anything that holds the area
across a clear tolerant of it leaving the list.

## Scope rules

**`NE_` prefixes defNames; textures live under `Textures/NeatEdges/`.** Both
namespaces are global across every loaded mod.

**No floors, no terrain.** This mod works with anyone's flooring; shipping its
own would put it in competition with the mods it exists to serve. Its art is
the marker's ghost, the two tool icons, the overlay toggle's icon, and the
trims' four strips and their 28 menu icons, all generated. The ghost and the
strips are greyscale, so the trims take their stuff's colour and paint like any
building. The icons copy the vanilla
icons they sit among instead (see DEVELOPMENT, Textures): the toggle is pixel
art matching the toggle row, and the area tools match the Zone tab's area
tools, vanilla's clear slash included. The tools are stone and soil, never the
area's cyan: that blue is the home area's in those menus.

**The trims' `edges` are the arms their shape draws.** Rotation names the edge
the band hugs, with the offsets in the defs (border `[0]`, corner `[0,1]`,
runner `[0,2]`, end cap `[3,0,1]`, frame all four), the same offsets
`StripTrimGeometry` draws arms on. The harness pins every rotation
(`trims.masks.*`). Change one without the other and a trim hardens an edge it
does not cover, or draws one it does not harden.

**The diagonal hardens its whole tile, and lists no `edges` on purpose.** A
diagonal wall is usually built as two staggered rows; the outer row covers no
floor, and its ground creeps into the room at every joint until the room-side
row is hardened whole. Its band is not on any one edge, so no list of edges
would describe it anyway. The harness pins it (`trims.defs.*`, `trims.masks.*`).

**The inside corner carries no `BlocksTerrainFade`, and must not.** It covers a
corner of its tile, not an edge, and the strips beside it already seal that
corner. The extension with an empty list means all four edges, so "adding it
with nothing in it" would harden a whole tile the piece only touches; the
harness pins the absence (`trims.defs.*`).

**The corner names follow tile games, not carpentry, and stay.** "Corner" and
"inside corner" name corners of the floor being edged, as autotiled maps do:
the corner piece sits on the floor's outer corner, and the inside corner on
its inner corner, where the floor wraps around a wall jutting into the room or
meets itself at a crossing of runners. Carpentry calls that jutting wall an
outside corner, which is a reason to explain the name, not to change it:
`InsideCorner` is a `TrimPiece.Kind` the README publishes for other mods'
trims, so renaming it breaks their defs (DESIGN §9).

**Every trim draws from its family's strip, and its geometry exists twice.**
`Graphic_StripTrim` lays a strip from `Textures/NeatEdges/Trim/` out as
geometry: a band along each edge the shape covers, cut at 45 degrees where two
meet, or along a diagonal wall's face, mapped to world position along the band.
The geometry is `StripTrimGeometry` in C# and `devtools/strip_trim.py` in
Python, which `check_trims.py` renders from, and both are pinned to
`devtools/strip_trim_geometry.txt`: every straight-family shape at every
rotation, the straight on a three-band and a four-variant strip, every
diagonal in a set of neighbour arrangements (a run, an octagon's corner and a
diamond's tip from inside and out, a corner piece, a turn too sharp to mitre),
and the straight and the lone diagonals from each half of a strip with a paint
overlay.
The harness's `trims.geometry` fails if the C# drifts from it and
`check_trims.py` if the Python does. Change both, then regenerate the file with
`strip_trim.py --write-golden`; never regenerate it to make one side pass.

**The strip's bands carry the light.** Top to bottom: the band on a north edge,
lit lip outermost; the band on a south edge, shaded lip outermost; and the band
lit from the side. West arms take the first and east arms the second, so the
light stays north-west on every shape at every rotation with nothing drawn per
facing; a mirrored facing lit from the wrong side, the bug the old per-facing
art had to author its way around, cannot happen. A diagonal whose wall fills its
tile's north-west or south-east half takes the first or second band; one along
a wall running north-west to south-east meets the light side-on and takes the
third. The floor border's third band is its other two averaged depth by depth;
the other styles light their height maps from the side. A strip with two bands
still loads, and its diagonals draw the north band there. The floor border's
strip is 256 wide although four columns hold the band: at four, its small mips
compressed to different colours and the straight drew up to 8 levels off.

**The drawings in `make_trim_art.py` are the reference, not shipped art.** The
floor border's strip is cut from the straight's rows, and `check_trims.py`
renders every shape at every rotation from the strip and compares it with its
drawing pixel for pixel. The only difference it allows is at each mitre: three
squares of 2×2 texels, the outer corner, the groove crossing and the inner lip
corner. So a change to a drawing is a change to what players see. `_frame` draws
the end cap and the frame by the corner's rules, and must keep redrawing the
straight, corner and runner pixel for pixel; the check fails if it stops. The
diagonal and the other three styles have no drawings: `make_trim_styles.py`
draws each style once as a height map and lights it three ways, and the check
holds their strips to their seams instead (below).

**The strip never enters the static atlas, and nothing patches the atlas.** The
class keeps the base `TryInsertIntoAtlas`, which is empty. Inside the atlas the
strip could not repeat along its band, and its edge texels would be filtered
against whatever the packer put beside it, the joint hairline a Harmony patch
existed to stop until 2026-10. Built trims print on one white material with
their colour in the vertices, as the engine prints every atlased building, so a
map section draws all its trims in one call; anything on another shader, a
blueprint or the ghost, keeps its colour on its material. Keep `drawSize` at one
tile. The harness pins it (`trims.render.*`, `trims.cost.*`); whether a joint
LOOKS clean is still a close-zoom capture in a running game.

**A trim's shape is read through blueprints.** The engine's generated blueprint
def copies the graphic, class included, but not the extensions, so
`TrimPiece.For` looks through `entityDefToBuild` to the def being built. And
every trim def names a `uiIconPath`: without one the engine takes the graphic's
texture for the build button, which would be the strip.

**A variant is picked by position, never at random, and its ends must match.**
The vine's and the pebbles' strips hold four one-tile variants side by side;
every stretch of band draws one, picked by `VariantPick` from a hash of its
line, side and stretch, so a tile draws the same variant on every load and every
machine. Changing the hash reshuffles every vine and pebble border already
built, which is an art change; the golden file pins it. Any variant may follow
any other, so each must begin and end alike, and the vine's one leaf across a
joint is the same leaf in every variant. `check_trims.py` measures both halves
of that, past `SEAM_TOLERANCE` failing: how far the variants' first and last
columns differ from each other, and how far a joint steps beyond the
differences between neighbouring columns beside it. The second is the one that
catches a stem leaving every tile lower than it entered, which leaves the ends
alike; a deliberately broken strip measured 255. Grey is weighed by alpha, as
it is drawn: the vine leaves texels transparent past its field. The 16-phase offset that keeps
parallel runs out of step applies only to a strip with one variant.

**The vine and the pebbles are opaque from the tile's very edge.** A border
covers the line where its floor meets what lies past it; a trim that leaves that
line showing reads as an ornament beside the edge, not a border, which is how
the first vine read in game. The inlay is set into the floor on purpose.

**Paint reaches the vine alone, stem and leaves, through a paint overlay.**
`TrimPiece.paintOverlay` doubles the strip: the bands in its top half and the
same layout in its bottom half, the overlay, which `Print` draws over them in
the one submesh, the bands in `StuffColor(thing)` and the overlay in the
graphic's colour. Three things hold it together. The bottom half is MIRRORED,
not stacked in order (`BandV` returns `1 - v` for it, and `make_trim_styles.py`
writes its rows reversed), so the halves meet inner edge to inner edge and a
mip never blends a band's edge with an unlike row. The bands' colour comes from
`StuffColor`, never `color`: a painted thing's graphic is the painted version,
so its `color` is the paint. And the overlay prints after the bands at the same
altitude, which is what puts it on top in a shared submesh. `MeshFor` builds
both halves into the ghost's one mesh, in the material's colour. The harness
pins it (`trims.strip.paintOverlay.*`, `trims.strip.light.bands3.layer*`, the
golden file's Overlay lines), and `check_trims.py` fails an empty overlay.

**A diagonal reads its neighbours, and nothing else does.** When its section
prints, a diagonal looks up the trims that end at its two corners
(`StripTrimGeometry.DiagonalEndsAt`) and cuts each end to meet them: square
along a run, a mitre where the line turns, with the wedge on the outside of a
turn into a straight filled in that straight's own mapping. Only built trims
count, since a blueprint's def carries no `TrimPiece`. Building or removing a
thing dirties only its own section, so a neighbour across a section's edge would
keep its old end; `CompTrimJoins`, which `TrimJoinsInjection` gives every def
with a `TrimPiece` at startup, other mods' included, reprints the cells around
it. Keep it even though a diagonal looks right inside one section:
`trims.diagonal.refresh` builds across a section edge on purpose. The placing
ghost has no thing, so it reads the map at its own cell.

**Each shape is one button, built at startup.** `TrimShapeButtons` groups the
defs that carry a `TrimPiece`, a designation category and
`canGenerateDefaultDesignator false` by category and shape, and adds a
`Designator_TrimShape` holding that shape in every style, lowest `uiOrder`
first, to the category. Buttons go by shape rather than by style (changed
2026-10-10) because shapes are what a player looks for in the menu and styles
will outnumber them; another mod's style then adds no buttons. It is a
`Designator_Dropdown`, so a placed trim's Copy still finds that trim's own
`Designator_Build`, but a left click goes to the current style's
`ProcessInput`, where the material menu lives; vanilla's dropdown never calls
it and would lock a stuffable trim to its default material. Every trim names
its shape's dropdown group, set on the shape's abstract def, with
`includeEyeDropperTool`, the one flag that stops Better Architect Menu
unrolling a dropdown on its Floors tab; vanilla reads it for terrain only. The
Architect's search filter is read by reflection and fails soft to the style on
the button (`trims.buttons.searchReadable` reports which).

**Other mods opt in by extension, never by name.** `BlocksTerrainFade` is
extension-keyed so a third party's overlay can adopt the behaviour without this
mod knowing it exists, behind `MayRequire` so it stays inert for players
without this installed. The trims began that way, in Fine Establishments,
before they moved here.

**Compatibility shims are for published mods only.** The adoption of
Perspective: Paths' saved area is a shim because that mod is published. The
trims were renamed from Fine Establishments' prefix when they moved, and that
unpublished mod's saves were patched once by a script in its own devtools
rather than taught to this one.

**Every Harmony patch fails closed to vanilla.**

## Not our bug: drag-placement and Perfect Placement

**If a marker will not drag out in a run, check the mod list before the def.**

Drag placement in 1.6 is governed by `drawStyleCategory` and nothing else —
`placingDraggableDimensions` and `DraggableDimensions` do not exist anywhere in
the assembly. Our defs use `Defenses`, which inherits `Default1D` (Line,
AngledLine, EmptyRectangle, EmptyOval); `DesignatorManager.Select` then sets
`selectedStyle = styles[0]`, and the only path to no-drag is a null or empty
category. So if the XML names a real category, dragging is not the def's fault.

**Perfect Placement** (`remi.perfectplacement`) rebinds hold-left-mouse to
rotate the ghost — "hold left mouse to pin an object, then move the mouse to
rotate", by design. That consumes the drag, so drag-placement disappears for
every rotatable building, the markers and the floor borders alike. It
has a setting to turn the mouse-hold rotation off (confirmed 2026-09-04).

The diagnostic lesson is worth more than the fact. The symptom was reported as
"cannot drag", and several reads went into draw styles, def inheritance and
cross-reference errors on that description. What actually identified it in one
step was **what happened INSTEAD** — the piece rotated by one step. Nothing in
vanilla rotates on drag (`Designator_Place` binds rotation to the
`Designator_RotateRight`/`Left` keybinds only, and sets a drag sustain sound, so
it expects dragging to work). When placement misbehaves, get the wrong
behaviour described, not just the missing one.

## Commits

One line, no body: an emoji, then an imperative subject.

`✨` feature, `🐛` fix, `💄` art, `♻️` refactor, `🔥` removal, `🔧` tooling,
`📝` docs, `✅` tests, `⬆️` dependency bump. Pick by the nature of the change,
not the files touched.
