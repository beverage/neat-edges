# DESIGN

Why Neat Edges took this shape, and what it does to the engine to get there.

Written to be readable by someone who has not seen the code — including anyone
weighing it against a different approach to the same problem.

---

## 1. The problem: the receiving cell gets no say

RimWorld does not draw terrain cell by cell. `SectionLayer_Terrain.Regenerate`
draws each cell's own quad, and then, for each of its eight neighbours, decides
whether to paint that **neighbour's** material into this cell as a nine-vertex
fan. That fan is the soft fringe you see wherever two terrains meet.

The decision is one condition:

```csharp
if (!neighbour.Equals(self)
    && neighbour.def.edgeType != TerrainEdgeType.Hard
    && terrainGrid.FoundationAt(self) == null
    && terrainGrid.FoundationAt(c) == null
    && neighbour.def.renderPrecedence >= self.def.renderPrecedence)
```

Everything in it is a property of a **terrain def** or of the **foundation
grid**. Nothing a player places on the cell is consulted. That is the whole
reason this mod needs code: there is no XML in the game that lets a tile say
"do not bleed into me".

The fan's geometry matters later, so it is worth stating now. Nine vertices —
eight on the rim, one at the centre, always transparent — and eight triangles
meeting in the middle:

```
NW ---- N ---- NE       A CARDINAL match lights three rim verts:
 |  \    |    /  |      itself and both flanking diagonals.
 |   \   |   /   |
 W ------C------ E      A DIAGONAL match lights one.
 |   /   |   \   |
 |  /    |    \  |      C is always clear, so every fan fades
SW ---- S ---- SE       from full alpha at the rim to nothing.
```

## 2. Why none of vanilla's levers work

Three fields look like they should solve this. None does.

| Lever | Scope | Why it fails |
|---|---|---|
| `edgeType Hard` | per-def | **Outbound only.** It stops *your* terrain bleeding out; it does nothing about what bleeds in. Vanilla `Concrete` is `Hard` and still gets soil smeared across it. |
| `renderPrecedence` | per-def, relative | A neighbour bleeds in when `neighbour >= self`. Note `>=` — **a tie bleeds.** Water sits at 394–399 and land at up to 370, so a floor has to out-rank the entire ladder, and 400 is the ceiling. It is an arms race, not a rule. |
| `isFoundation` | **per-cell, absolute** | Works — it is what makes bridges look clean — but it is whole-tile, and it drags the bridge vocabulary with it. |

The foundation route was built, shipped and then cut. It worked, but
`isFoundation` is not a label you can borrow: removal is only via the "remove
bridge" order, the foundation slot is exclusive so it can never share a cell
with an actual bridge, `GetAffordances` starts reporting the pad's list instead
of the floor's, and **three separate Harmony patches** were needed purely to
undo vanilla's assumption that a foundation *is* a bridge.

## 3. The decision: an edge is sided

The rejected design and the shipped one differ on one question — *what is the
unit of the thing being described?*

**An edge is a boundary between two cells.** It has a side. Any mechanism whose
unit is the tile, or the region, is approximating it, and the approximation
shows up exactly where boundaries are interesting: at corners, at the ends of
runs, and wherever a player wants one side crisp and the other not.

So the marker is a **placed, rotatable thing**, and its rotation names the edge
it hardens. Everything else follows from that choice.

### Rotation-stacking gives all fifteen combinations from one def

RimWorld refuses a placement only when def, rotation **and** cell all match. A
different rotation passes at every stage — placement, spawn and construction
alike. So four `NE_HardEdge` markers at four rotations legally share one cell,
and one def covers every combination of edges.

An all-sides def shipped alongside it briefly, to save three build orders on a
whole tile. It was removed before release once the painted area (§7) did the
same job with no build orders at all; no save ever kept one.

## 4. What the code actually does

Three mechanisms. They are not interchangeable, and each exists because the
others cannot do its job.

### Substitution — removes a direction as a *source*

For a hardened direction, the neighbour's terrain is swapped for `Underwall`
before the gate sees it. `Underwall` is `edgeType Hard` at `renderPrecedence 0`,
so it fails the gate **and** matches nothing later. This is not a trick; it is
precisely what vanilla already does for a wall that covers its floor, applied
per direction instead of per cell.

Every other slot keeps the value vanilla computed, which is what preserves the
neighbouring edges' shared wedges.

### Corner sealing — closes a corner a neighbour hardened

Where terrain fills a quadrant, the floor cell diagonally inside the corner
touches it only at a **diagonal**. It has no exposed cardinal edge, so no marker
is ever placed on it, and nothing masks its corner spike. The two hardened edges
that close that corner belong to other cells.

So for each diagonal, we ask the two cardinal neighbours it sits between whether
either has hardened the edge running along that corner.

### Vertex pinning — forces a corner dark whatever lit it

Substitution removes a direction as a source. It **cannot clear a vertex**,
because a cardinal match lights three rim verts — so an open neighbouring
cardinal re-lights the very corner a hardened edge just sealed. That was the
bug where a lone edge left both flanking tiles fringed around the end of the
run.

Pinning happens after the mask loop and forces sealed corner vertices dark
regardless of what lit them. Cardinals are untouched, so a neighbouring tile
keeps its own fade — it simply stops wrapping around the corner.

### A pin that holds nothing back is dropped

"Regardless of what lit them" was too broad. `EdgeMaskAt` decides which corners
a layout seals from the markers alone, and a pin darkens that vertex for every
fade drawn on the cell — including a fade that never crossed a hardened edge.
The preview card caught it: lichen and sand meeting under the end of a floor's
hardened bottom edge. Sand outranks lichen (350 against 315), so its fade lands
on the lichen tile from the east, across an edge nobody hardened. The floor's
edge flanks that tile's top corner and the floor's side seam closes it, so the
corner was pinned, the sand fade was cut off at the top, and the two grounds
met in a hard line just under the floor. Trims alone did it, and so did the
painted area alone.

So the renderer consumes `RenderMaskAt`: the layout mask, less every pin that
holds nothing back. It replays the patched renderer's view of the eight
neighbours — vanilla's gate for which terrains fade onto the cell, the
substituted array for which corners each one lights — and drops a pin only when
every fade reaching that corner:

- comes in through an **open side whose terrain is its own** (a fade that reaches
  the corner only by the diagonal keeps its pin: that spike is what corner
  sealing is for);
- finds **no open side at the corner that is a seam inside the cell's own
  surface**, where a lit corner beside a neighbour pinned clean would cut the
  fade off in a straight line (the taper beside a lone edge keeps its pin: its
  open side there is the same floor). An open side of the fade's own terrain,
  or of any terrain different from the cell's, is already a boundary, and a lit
  corner cuts nothing new there;
- and finds **no hardened edge at the corner holding that same terrain back**
  (the re-lit corner above, where the hardened edge holds back the soil that
  re-lights it, keeps its pin).

The replay reads the cardinals substituted and each diagonal as built. The
renderer substitutes every direction whose bit is in the mask it is handed,
diagonals included, so dropping a pin also un-substitutes its diagonal; the
first version read the diagonal substituted, never saw the sand sitting there,
and let its spike onto a carpet tile beside a painted area.

Anything mixed keeps its pin: a crisp edge wins a tie. `EdgeMaskAt` itself is
unchanged and stays a pure function of the marker layout, which is what the
harness's mask cases assert; the render step reads terrain, and its cases paint
terrain to read.

### Hardening is two-sided

An edge is shared, so a marker on the north edge of a cell hardens the south
edge of the cell above it. One marker therefore tidies **six** tiles: the one it
is on, the one across the edge, and the four meeting at the edge's two
endpoints.

This is what makes it not matter which side of a boundary you place on, and it
is what closes the outside corners when boxing in a patch of terrain.

The counts fall out of the edge list rather than being hard-coded:

| Piece | Edges | Directions hardened |
|---|---|---|
| single | W | SW, W, NW — **3** |
| corner (two adjacent) | N + W | SW, W, NW, N, NE — **5** |
| runner (two opposite) | N + S | all but E and W — **6** |
| all four (stacked, or a painted tile) | N E S W | **8** |

## 5. Why a transpiler

The renderer began as a Harmony prefix returning `false` — vanilla's
`Regenerate` copied out and modified. That works alone and is hostile in
company, and it broke a real mod: **Dub's Paint Shop transpiles the same
method**, rewriting each `GetMaterialFor` call into a paint-aware lookup. A
prefix returning false skips the original, and a transpiler only edits the
original — so every painted floor in the colony silently lost its colour.

Transpilers compose. Harmony chains them, each seeing the previous one's output,
and our anchors are disjoint from theirs. Both survive in either order.

Three insertions, all direct calls:

1. at the first `TerrainAt(item)` — compute the cell's render mask once into a fresh local
2. before the neighbour store — substitute `Underwall` for a hardened direction
3. at the colour ternary — force sealed corner vertices dark

New locals are appended by `DeclareLocal`, which matters: Dub's transpiler emits
a hardcoded `ldloc.S 6`, and renumbering would silently corrupt it.

**Anchors are the fragile part, and one already bit.** The neighbour store
happens twice in that method — once on an out-of-bounds early-continue, once for
real — and matching the first produced a patch that applied cleanly, threw
nothing, and hardened nothing anywhere a player would look. Anchors are now
positioned relative to the `GetEdifice` call that sits between them. Failure is
all-or-nothing: any missing anchor logs once and passes the instructions
through, giving vanilla rendering rather than half-applied rendering.

## 6. Cost

`EdgeMaskAt` costs up to 24 own-mask lookups per cell — eight for the base mask
(four cardinals, each asking both sides of its edge) and sixteen for corner
sealing. **The empty case is the worst case**: every short-circuit fires only
when something *is* hardened, so terrain with no markers near it pays full
price.

Almost all of it is the same few cells, so `OwnMaskCache` memoises the one
function that touches the thing grid, per operation:

| | uncached | cached |
|---|---|---|
| section regen (17×17) | ~6,900 | 361 |
| overlay rebuild (250×250) | ~1.5M | 62,500 |

The overlay's own predicate was later narrowed to "does this cell carry a
marker", which is one lookup per cell and needs no cache at all.

`RenderMaskAt` adds work only on a cell with a sealed corner, which is a cell
beside a hardened edge: one pass over the eight neighbours' terrain, built the
way the renderer builds it, into per-thread scratch arrays. A cell with no
sealed corner returns the layout mask untouched.

The painted area adds one grid read per own-mask lookup. The area itself is
found once per regeneration, in the cache's constructor, because finding it is a
scan of the map's area list; a map nobody painted has none, and pays nothing.

## 7. The painted area: an area interface on the sided model

Area and sided are not rival answers. They answer different questions, and
neither wins outright.

**Where area is better**

- Fewer actions for the common case. "Box in this pond" is one drag rather than
  a lap of the perimeter.
- Nothing to get wrong about rotation.
- The player thinks in areas already — zones, rooms, growing zones, home area.
- It costs nothing. No build order, no pawn, no thing on the map: a RimWorld
  `Area` is scribed, drag-painted and overlaid by the engine.

**Where sided is better**

- **It can express a one-sided boundary.** An area hardens every edge of every
  tile it covers; it cannot say "crisp against the courtyard, soft against the
  marsh". A sided marker can, because the unit *is* the side.
- **It composes with visible trim.** The same marker extension is what the
  trims carry (§9), so a decorated strip hardens the edge it hugs. An area has
  nothing to attach to a piece of art.

**So the mod ships both, and they share one model.** A painted tile enters the
mask as own-mask bits, exactly as four stacked markers do, and everything
downstream is the sided model unchanged: two-sided edges, corner sealing,
pinning. The harness pins that equivalence directly, comparing the 5×5 of edge
masks around a painted tile with the 5×5 around four stacked single-edge
markers, cell by cell.

That is why this is not the thing an earlier version of this section warned
against. The warning was about an area whose unit *replaces* the model — skip
the fan on the painted tile and stop there, which is how Perspective: Paths
works (read from its hook, not observed on screen). That protects only the
receiving side: the painted tile's own terrain still fades **out** onto every
unpainted neighbour that ranks at or below it, including a spike into each tile
diagonally outside the region's corners, because nothing seals a corner that
belongs to an unpainted tile.

A painted tile here hardens both sides of each of its edges and seals those
corners, by the same rules the markers are tested against.

**The area's unit is the tile, and that is a real limit, not an oversight.**
Painting two adjacent tiles of different floors hardens the edge between them
too. Where only one side of a boundary should be crisp, the answer is still a
marker.

**Considered and not built: an area tool that places markers around its
outline.** It would put build orders and things on the map for what is a
rendering preference, and it cannot load another mod's saved areas, which are
sets of tiles (§8).

**The area is created on first paint**, not with every map, **and removed when
its last tile is cleared.** A saved area whose class is missing does not cost
one load error: it takes the map's whole area list with it (§8 has the
measured case, Perspective: Paths' own). So an empty area on every map would
cost every map its home and allowed areas for a player who later removes the
mod, including maps where they never painted, and an area kept after its last
tile was cleared would do the same to the maps where they did. Clearing every
painted tile is therefore the player's way to make a map safe to remove this
mod from, and map finalization drops any area that loads empty, which is what
an adopted Perspective: Paths zone usually is: that mod adds an empty zone to
every map.

The tools sit on the **Zone** tab with vanilla's areas, which is also where
Perspective: Paths keeps its own, so a player switching over finds them where
the old ones were. They began on the Floors tab beside the marker, as one
feature with two interfaces; the move is for the switch. Neither sets an
`Order`. No vanilla tool on the Zone tab does, so the grid's stable sort keeps
list order and these land after vanilla's areas, where its tools sat.

## 8. Taking over Perspective: Paths saves

Perspective: Paths stores its per-tile override as an `Area` subclass,
`PerspectivePaths.Area_InvertEdges`. Its original and its continued release
both write that one class name. Its own FAQ tells players to clear their areas
before removing it, but clearing does not help: a `Map.FinalizeInit` postfix
adds its zone to every map that lacks one, painted or not, and the zone does
not override `Mutable`, so Manage Areas never lists it and `AreaManager.Remove`
refuses it. Every map it was ever loaded on keeps the class in its save, and
what that does on removal is worse than losing one area (measured 2026-09-23 on
a real colony save with a painted zone; the empty-zone case tested 2026-10-07
on a minimal list, where a never-painted zone loaded without the mod left the
map with no areas, `Map.FinalizeLoading` failed at the first reader of the home
area, and the game's root update threw every frame after. The same save with
this mod installed instead loaded clean). The unresolvable node loads as a
null list element; `AreaManager.UpdateAllAreasLinks` dereferences every element
while loading, throws on the null, and the map's **whole** area manager fails to
load. Home, allowed and roof areas all go with it. Anything that reads the area
manager then fails in turn: on a 234-mod list, Hospitality's map component
(whose saved state names the home area) and Vehicle Framework's pathing system
(whose pathfinder constructor looks up road areas) both failed to build, and
every thing on the map threw while spawning until RimWorld stopped logging.

**Its saved node is a plain `Area`** — an ID and a grid, nothing else — and so
is `Area_HardEdges`'s, which is why this mod must never add a scribed field to
it. So the whole migration is answering one type lookup differently.

**The lookup is `BackCompatibility.GetBackCompatibleType`**, and a postfix on it
is the entire mechanism. `ScribeExtractor.SaveableFromNode` sends *every*
deep-saved object's class name through that method, not only the missing ones,
and the method ends in a plain type lookup; a postfix sees the final answer on
every branch. It acts only when that answer is null, so while Perspective: Paths
is installed its own class resolves first and nothing here fires. What happens
then is the last part of this section.

Called once per deep-saved object on every load, so the null check comes first.

**Rejected: declaring `PerspectivePaths.Area_InvertEdges` ourselves**, the usual
continued-mod trick. It squats another author's namespace, and it collides with
the real class whenever both mods are loaded unless it sits behind a conditional
load folder, which then owns all of this mod's folder resolution.

**The names we answer for are a table, not a comparison.** If this mechanism
ever moves into another mod, a save carrying `NeatEdges.Area_HardEdges` needs
exactly the same treatment, and that should be one more row.

Three loose ends, all handled at map finalization:

- **A map can load with two areas** — one adopted, one painted here — if a save
  ever ran with both mods and the player used both tools. `AreaManager` relinks
  areas on load but never prunes them by type, and everything here takes the
  first one found, so the second would keep hardening tiles the clear tool
  could not reach. They are merged, through the indexer so the pathfinder and
  the terrain mesh hear about every tile.
- **An adopted zone is usually empty.** Perspective: Paths adds its zone to
  every map, painted or not, so a switcher's save carries one per map. Empty
  ones are dropped (§7: an empty area must never reach a save).
- **The player is told once**, after the load, and only about areas that had
  painted tiles. The tools are on the Zone tab, where Perspective: Paths kept
  its own, but without the message the only evidence the migration ran is that
  nothing broke. Counting every adoption told a switcher their areas came
  across on maps they never painted. An adopted area is told apart from one
  this mod saved, which shares its class, by a runtime mark the type lookup
  leaves for the constructor the scribe calls next; it is counted before the
  merge can fold it away, and the message waits for the load to finish so
  every map is counted.

**The visible difference is disclosed, not hidden.** An adopted tile is a
painted tile, so it also stops fading out onto its neighbours and its region's
corners close (§7). Edges around adopted areas come out a little crisper than
Perspective: Paths drew them.

### When both are installed

**The two patches compose in either load order.** Perspective: Paths also
transpiles `Regenerate`, and its transpiler always runs before ours: its
designator reads a static field when the game builds the Architect menu during
def resolution, which runs its static constructor, and its `PatchAll` with it,
before any mod's startup constructors. Our anchors still resolve, with the vert
anchor exactly five instructions later, which is its insertion. That was
measured in both orders on a minimal list, and on a 234-mod list with Dub's
Paint Shop's edit in place as well.

**Left alone, a player would get two whole-tile tools**, one on each tab,
drawing by different rules. This mod yields instead. A player who has
Perspective: Paths installed chose it for exactly this job, so while its class
resolves:

- **our two area tools are hidden**, and refuse every cell, because architect
  search activates a hidden tool when it is the only match;
- **an area of ours already on a map moves into its zone** when the map
  finalizes, and ours is removed. That covers a player who painted here before
  adding it, and the round trip this mod creates: remove it, its zones become
  ours, add it back. Its zone is found by type, or made through its own
  constructor and label. That label is what its finalization postfix looks up
  next, so it caches the zone made here instead of adding an empty one. If that
  constructor ever changes, our area stays where it is and keeps drawing;
- **the marker, the trims and the overlay stay.** Its zone cannot say "crisp on
  this side only", and without the overlay nobody can find a marker.

With the migration above, whichever mod is installed owns the painted tiles,
and Perspective: Paths owns them when both are. No tile is lost in either
direction. What changes is the rule they draw by: its zone stops terrain fading
into a tile, but not out of it.

The clean-room line is deliberate. Perspective: Paths carries no licence, on
its files, its Workshop pages or its repository, so nothing here is taken from
its code. What it contributed is the idea that an `Area` is the right store for
whole-tile hardening, and the README credits it for that.

## 9. The visible trims

A border strip, its corner, the inside corner, a double-rail runner, an end cap,
a frame and a diagonal: 1×1 non-edifice buildings at a floor-covering altitude
that dress a cell without owning it, so they coexist with any terrain, with
furniture and with each other. Stuffable and paintable. Each but the inside
corner carries `BlocksTerrainFade` for the edges its bands run along, and the
diagonal for its whole tile, so a decorated strip is also a hard edge, by the
same two-sided, corner-sealing rules as the invisible marker. Since v1.1.0 the
seven shapes come in four styles, each drawn from its own strip: the floor
border, the inlay, the vine and the pebbles.

They began in Fine Establishments and moved here on 2026-09-24, because what
makes a trim more than decoration is this mod's mechanism. The art came with
them: `devtools/make_trim_art.py` is that mod's drawing code. Until 2026-10 it
wrote a texture for every shape and facing, the same PNGs byte for byte; now it
writes one strip, and the drawings stay as the reference the strip is checked
against. Their defNames took this mod's `NE_` prefix on the way; the saves that
held the old names were patched once by a script in Fine Establishments'
devtools, since a compatibility shim is for players of a published mod and
that one was never published.

- **The border hugs one edge and rotates to pick which.** A centred linked band
  was built first and rejected in play: a link mask can never say which side
  its wall is on, so an edge-hugging piece has to rotate instead of link.
- **The corner is one rotatable def owning both bands.** Two separate strips
  cannot join cleanly, since neither knows the other is there; one piece meets
  its two bands in a mitre on the diagonal.
- **Two straights at different rotations stack on one cell**, by the same
  rotation-equality rule as the markers (§3). The runner exists for the
  ergonomics, a corridor's two rails in one drag, and so one piece owns both
  rails. It costs double, so the convenient route is not also the cheap one.
- **Every trim draws from its family's strip, laid out as geometry** (since
  2026-10, `Graphic_StripTrim`). `FloorBorderStrip.png` is the straight's band
  at 256 × 192, in three bands of 64 rows: the band on a north edge, the band
  on a south edge, and the band lit from the side that diagonals draw (below).
  A shape is the set of edges it draws a band along, an arm
  each — the straight one, the corner two adjacent, the runner two opposite, the
  end cap three, the frame four — and each arm is a quad a quarter tile deep, cut
  at 45° where a neighbouring arm is present. The inside corner is the band
  square at one corner, split on its diagonal. Along a band, U comes from world
  position, so a run of trims is one strip carried across every joint; each
  grid line and side starts it at its own offset, invisible on ours, whose band
  does not change along its length, but a strip with a pattern along it would
  otherwise repeat in step on every parallel run. Altitude follows the engine's
  `PrintPlane`, which raises a plane's north edge 0.01 above its south.
- **The strip's bands carry the light.** North and west arms take the first
  band, south and east the second, so light falls from the north-west on every
  shape at every rotation with nothing drawn per facing. A diagonal along a wall
  running south-west to north-east sees the light from one side of its band, as
  a straight does, and takes the first or second; one along a wall running
  north-west to south-east sees it side-on, and neither is right, so the strip
  carries a third band lit evenly across. The floor border's third band is its
  other two averaged depth by depth. A two-band strip still loads (`bands`
  defaults to 2), and its diagonals draw the first band there. The per-facing
  textures had to earn that one file at a time: the engine mirrors east for a
  missing west, which flips the light and puts the lit lip on the shaded side.
  The runner and the end cap authored their west from the start. The straight
  was left to the mirror, on the belief that a flipped right-edge strip *is* a
  left-edge strip, which is true of its shape and not of its light: on
  2026-10-06 its lip visibly stepped wherever a west straight met a corner, and
  it authored its west too, which made 21 textures. The strip has no facing to
  get wrong.
- **The strip never enters the static atlas, and nothing patches it out.**
  RimWorld packs small building textures into a static atlas edge to edge,
  with no gutter, and builds mipmaps and compresses the sheet as a whole, so
  the outermost texels of an atlased quad are filtered against whichever
  texture sits beside it there. A trim's bands run to the edge by design, so
  every joint carried a hairline of a neighbour's colour, often green or teal,
  that changed as the camera moved. A 4% overdraw (`drawSize 1.04`) only moved
  the line onto the neighbouring piece, where it drew on top. From 2026-10-06,
  `Patch_TrimAtlas`, a prefix on `GlobalTextureAtlasManager.TryInsertStatic`
  applied from the mod class's constructor, kept the per-facing textures out
  and clamped them. `Graphic_StripTrim` keeps the base
  `Graphic.TryInsertIntoAtlas`, which is empty, so the strip never asks to go
  in, and the patch and the mod class went with the per-facing textures. The
  strip repeats along its band and is clamped across it, so a band's outer row
  never blends with the other half's.
- **A map section draws all its trims of one style in one call.** A section
  draws one mesh per material (`MapDrawLayer.GetSubMesh` keys on it). Built
  trims print on one white material per strip with their stuff or paint colour
  in the vertex colours, the trick the engine uses for every atlased building,
  so wood, stone, steel and paint share one mesh; a section holding all four
  styles draws four. Blueprints and the placement ghost print on their own
  material, which carries their shader and colour. Measured on 24 pieces, every
  shape at every rotation in one section: 21 draw calls with the per-facing
  textures, 24 in mixed stuff and paint, and 1 on the strip either way. What
  goes up is geometry: mitred arms draw more vertices, 216 for those 24 pieces
  against 96.
- **The geometry exists twice and is pinned.** `StripTrimGeometry` in C# draws
  it, and `devtools/strip_trim.py` renders it for `check_trims.py`. Both must
  reproduce `devtools/strip_trim_geometry.txt`, every vertex and UV of every
  shape at every rotation at one cell, chosen so every value is a whole number
  of 1/4096 and float and double agree exactly.
- **A mitre is the only change from the drawn art.** `check_trims.py` renders
  every shape from the strip, a texel to a pixel, and compares it with the
  drawing it replaced. The straight and the runner are identical. At each
  mitred corner three squares of 2×2 texels differ: the outer corner, where a
  lit lip and a shaded one now meet on the diagonal instead of one overlapping
  the other; the groove crossing, which the drawings darkened by laying both
  grooves over it; and the inner lip corner, which they left as plain fill.
  Anything else differing fails the check.
- **The inside corner fills the notch strips cannot.** A strip runs its own
  tile's full length, so where a line turns around an inside corner (a wall
  jutting into the room) the strips on the two tiles beside the corner meet
  only at a point, and a band's width of square in the tile between them is
  left open. The inside corner is that square, split on its diagonal, each half
  the band of the strip it continues, so their keylines, grooves and inner lips
  run on and join. It hardens nothing: it covers a corner, not an edge, and
  corner sealing already closes that corner from the two strips beside it. Its
  rotation names the corner, as the corner piece's does.
- **A runner turning a corner is two pieces on one tile**: a corner for the
  outer rails and an inside corner for the inner joint. Neither is an edifice,
  so the engine lets them share the tile at every stage: blueprint placement
  refuses only the same def at the same rotation, and spawning wipes only an
  edifice with an edifice. The harness pins it (`trims.stack.*`).
- **The end cap and the frame are arms like the rest**, three and four. Their
  reference drawings come from `_frame`, the corner's joining rules generalised
  to any set of edges: one fill union, keylines that stop at a crossing band's
  inner edge, grooves that run into each other and stop, bevels lit by their
  final side. Two adjacent edges through `_frame` are the corner's drawing pixel
  for pixel, and one edge or two opposite are the straight's and the runner's;
  the check fails if that stops being true, which is what keeps a new shape a
  member of the family rather than a lookalike. The end cap's rotation names its
  closed end. The frame never rotates and needs nothing to stop it: four arms
  draw the same whatever rotation code gives it, each taking the half for the
  edge it lands on.
- **A trim's shape is read through blueprints.** The engine generates a
  blueprint def for each buildable def and copies the graphic, class included,
  but not the extensions, so `TrimPiece.For` looks through `entityDefToBuild` to
  the def being built. The ghost (`GhostUtility.GhostGraphicFor`) keeps the
  class too, on the EdgeDetect shader. Every trim names a `uiIconPath`: without
  one the build button takes the graphic's texture, which is now the strip.
- **Price follows the bands**, the runner's rule: the end cap costs three
  strips and the frame four, and the inside corner, a band's width square, a
  fraction of one. The diagonal's band is a tile's diagonal long, so it costs
  half again a straight, 3 against 2.
- **The diagonal follows a diagonal wall, on the wall's own tile.** Diagonal
  Walls 2, the wall mod it was built against, makes each piece a full-tile wall
  whose art fills half the tile, with its face on the tile's diagonal, and lets
  a non-edifice be built under it. The trim stands there, with its band along
  the face in the open half, from the face outward. Laid along the wall's
  centre line it would be half covered and read half as wide as the straights
  beside it, so its rotation names the half the wall fills (facing north, the
  north-west half), and the player turns it until the band shows. U runs along
  the diagonal at the straight's texel density. The band reaches past its own
  tile into the corners of the two floor tiles beside it, because consecutive
  diagonal tiles touch only at a corner, and a band clipped to its tile would
  leave a notch at every step.
- **One diagonal does all the joining.** No fixed end serves every join: a run
  of diagonals needs ends cut square to the diagonal, a turn into a straight a
  cut on the turn's bisector, and both happen at the same corner point. So a
  diagonal reads what ends at its two corners when it prints and picks one of
  four ends at each. Nothing there, or the next diagonal of a run: square. A
  straight's band ending there on the same side: a mitre on the bisector. On
  the inside of the turn the two bands overlap and the diagonal draws over the
  straight's square end, 0.02 higher so it wins against the 0.01 a plane's
  north edge already rises; on the outside the two square ends leave a wedge,
  and the diagonal fills it in the straight's own mapping, so the straight's
  lines run on to the bisector. Another diagonal turning there, as at a
  diamond's tip: a mitre on the axis. A turn sharper than a right angle stays
  square. Straights never read their neighbours, so they and their corner
  pieces draw exactly as before. The one compromise: where two joined pieces
  differ in material or paint, the outside wedge takes the diagonal's colour,
  a sliver past the straight's end.
- **Reading neighbours costs a comp.** Building or removing a thing dirties
  only its own section (`Thing.DirtyMapMesh`), so a straight built beside a
  diagonal across a section's edge would leave the diagonal's old end showing.
  `CompTrimJoins` makes the call linked buildings make, `MapMeshDirty` on the
  `Things` layer with `regenAdjacentCells`, on spawn and on despawn, where it
  takes the map from its argument because `parent.Map` is already null. Neat Edges adds the comp at startup to every def carrying
  `TrimPiece`, other mods' included, so opting in stays two lines of XML, and
  a thing in an older save builds its comps from its def when it loads. The
  placing ghost has no thing to ask, so it reads the map at its cell, and a
  join shows while placing.
- **The diagonal hardens its whole tile**, as the painted area does. On the
  usual build, two staggered rows of diagonal wall, the outer row covers no
  floor, so its ground creeps into the room at every joint, and hardening the
  room-side row removes it. Behind vanilla walls it changes nothing. Its band
  lies on no single edge, so no list of edges would describe it.
- **Variants break the beat.** One tile of an irregular pattern, a vine or
  pebbles, repeated every tile reads as a stamp. A strip with `variants` holds
  that many patterns side by side, and every stretch of band one pattern long
  draws one, picked by a hash of its line, side and stretch: random to look at,
  the same on every load and machine. Any variant may follow any other, so each
  begins and ends alike: the vine's stem returns to the height it entered at,
  and one leaf, the same in every variant, crosses each joint, so the garland
  runs on through it instead of thinning to bare stem once a tile.
  `check_trims.py` checks that the ends match and that no joint steps. A piece's polygons are
  cut where a stretch ends, since a diagonal, √2 long, can hold the end of one
  pattern and the start of the next. With one variant a line starts its strip
  at one of 16 phases instead, so parallel runs and a runner's two rails never
  line up.
- **The styles.** The inlay is one fine line set in from the edge; the vine, a
  laurel-like vine carved in relief on a field that runs from the tile's edge
  to the floor border's dark keyline, a rounded lip set in from the edge, the
  leaves reaching past both lines, in four variants; the pebbles, river stones
  in grout, stone only, in four variants, for paths outdoors. A trim cannot know what lies across its
  edge, a wall, another floor or more of the same floor, so each reads right
  against all three. Each is drawn once, as a height map, an albedo and a
  coverage, and lit the three ways the bands need: from the band's outer edge,
  from its inner side and side-on, with the floor border's ambient and diffuse
  so a flat face lands on its value of 180. Light runs across the band and
  never along it: one band serves north and west edges, which see the
  north-west light from opposite ends, so a bump lit from along the band would
  be lit from the wrong end on half of them.
- **A border covers its edge.** The vine and the pebbles are opaque from the
  tile's very edge, as the floor border is, so the line where the floor meets
  what lies past it is under the trim. The first vine grew over open floor
  from a three-texel fillet, and in game it read as an ornament beside the edge
  rather than a border; the pebbles' grout began a texel in and left a line of
  floor showing. The vine's lines were then set in from both sides and its
  leaves grown past them, so the leaves are as much the border's outline as
  the lines: on the floor's side, past the keyline, only the leaves are
  opaque. Over its field the ornament casts its shadow onto the field, the
  baker's relief shadow, offset away from each band's light; past the field,
  onto the floor. The inlay is set into the floor by design.
- **Paint colours the vine, not its border** (decided 2026-10-10, the leaves
  first and the stem the same night; `TrimPiece.paintOverlay`). The vine is
  meant for natural themes, ground leading into a wooden floor, and painted
  whole it read as a green kerb: the paint took the lip and the field with the
  leaves. With the leaves alone painted, the wooden stem ran through the green
  as a brown line; the stem joined them. A strip with a paint
  overlay holds its bands in its top half and the same layout, mirrored, in its
  bottom half, and a trim prints every polygon twice into its one submesh: from
  the top half in the stuff's colour (`Graphic_StripTrim.StuffColor`, what
  `Thing.DrawColor` gives before `Building.DrawColor` puts the paint in its
  place), then from the bottom half in the graphic's colour, which is the paint
  when there is one. The second print lands on top at the same altitude, and
  the section still draws in one call. Mirrored rather than stacked in order,
  so the halves meet inner edge to inner edge: a mipmap blending rows across
  the middle blends a band with its own copy, never with an outer edge. The
  vine's baker splits its relief: the vine, stem and leaves, goes to the
  overlay wherever it stands higher than the berries, which is where it showed
  carved as one, so unpainted the two halves draw the old vine; the berries and
  the vine's shadows stay in the bands, in the material's colour. Unpainted is the material's colour rather than green, so an
  unpainted vine is one colour like every other stuff-built thing, and green
  costs a dye a tile like any paint. The cost: the vine's strip doubles to
  1024 × 384, taking the trims' video memory from 1.27 MB to 1.53 MB, and a
  vine piece prints twice the vertices.
- **Each shape is one button, its styles on right click** (`Designator_TrimShape`).
  Four styles of seven shapes would be 28 buttons on the Floors tab. The trims
  carry `canGenerateDefaultDesignator false`, and at startup each shape in each
  category becomes a dropdown subclass holding that shape in every style: a
  left click goes to the style on the button, material menu and all, which
  vanilla's dropdown never calls; a right click lists the styles. The first
  build had one button per style with the shapes on right click, which made
  four buttons; on 2026-10-10 it turned round to seven, because a player
  looks for a shape in the menu and styles will outnumber shapes, so another
  mod's style lands on the right-click menus and adds no buttons. Being a
  dropdown keeps Copy working, since
  `BuildCopyCommandUtility.FindAllowedDesignator` looks inside dropdowns.
  Better Architect Menu unrolls every dropdown on its Floors tab unless a
  member's group sets `includeEyeDropperTool`, which vanilla reads only for
  terrain, so each shape's abstract def names a group with it set. Search
  matches a button's label only, so while a search runs the button shows the
  first style it matches, read from the Architect tab by reflection.
- **New defNames are checked against hash slots.** Saves store rock, ore and
  deep resources by a def's slot, and every buildable def adds itself and its
  generated Blueprint and Frame, which sort ahead of every resource, so a new
  trim can take a resource's slot (AGENTS.md has the gate). The pebble and vine
  families' defNames say Pebbles and Vines because the singular names failed
  it: `Frame_NE_PebbleBorderEndCap` took Jade's slot on a 229-mod list, and
  `Blueprint_NE_VineBorderInsideCorner` hashed two below MineableGold.
- **Other mods can draw trims the same way.** A def opts in with the graphic
  class `NeatEdges.Graphic_StripTrim`, a strip of the same layout as its
  texture, and a `NeatEdges.TrimPiece` naming the shape. Those are a public
  contract from v1.1.0: the class name, the `shape` values, `Diagonal`
  included, the `bands`, `variants` and `paintOverlay` fields, the strip layout
  with its mirrored overlay, and the shape
  button for defs with `canGenerateDefaultDesignator false` stay as they are.
  An older Neat Edges does not know a newer shape value, and an enum that fails
  to parse keeps its default, `Straight`, with an error, so a def using one
  should say which version it needs. The harness pins each part as the mod's
  own trims use it (`trims.render.*`, `trims.geometry`, `trims.strip.*`,
  `trims.buttons.*`, `trims.joins.*`).
