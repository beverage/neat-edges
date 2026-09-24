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

1. at the first `TerrainAt(item)` — compute the cell's mask once into a fresh local
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

**The area is created on first paint**, not with every map. An empty area on
every map would be one more node in every save, and a load error on every map
of a player who later removes the mod, including maps where they never used it.

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
before removing it, and what happens otherwise is worse than losing one area
(measured 2026-09-23 on a real colony save). The unresolvable node loads as a
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

Two loose ends, both handled at map finalization:

- **A map can load with two areas** — one adopted, one painted here — if a save
  ever ran with both mods and the player used both tools. `AreaManager` relinks
  areas on load but never prunes them by type, and everything here takes the
  first one found, so the second would keep hardening tiles the clear tool
  could not reach. They are merged, through the indexer so the pathfinder and
  the terrain mesh hear about every tile.
- **The player is told once**, after the load. The tools are on the Zone tab,
  where Perspective: Paths kept its own, but without the message the only
  evidence the migration ran is that nothing broke.

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

A border strip, its corner and a double-rail runner: 1×1 non-edifice buildings
at a floor-covering altitude that dress a cell without owning it, so they
coexist with any terrain and with furniture. Stuffable and paintable. Each
carries `BlocksTerrainFade` for the edges its art covers, so a decorated strip
is also a hard edge, by the same two-sided, corner-sealing rules as the
invisible marker.

They began in Fine Establishments and moved here on 2026-09-24, because what
makes a trim more than decoration is this mod's mechanism. The art came with
them: `devtools/make_trim_art.py` is that mod's drawing code, and it writes the
same PNGs byte for byte. Their defNames took this mod's `NE_` prefix on the
way; the saves that held the old names were patched once by a script in Fine
Establishments' devtools, since a compatibility shim is for players of a
published mod and that one was never published.

- **The border hugs one edge and rotates to pick which.** A centred linked band
  was built first and rejected in play: a link mask can never say which side
  its wall is on, so an edge-hugging piece has to rotate instead of link.
- **The corner is one rotatable def with four explicit textures.** The piece is
  chiral (a mirrored east would duplicate the south corner), and owning both
  bands in one image is what lets them join cleanly: two separate strips never
  can, since neither texture knows the other.
- **The runner authors its own west facing.** The engine mirrors east for a
  missing west, which flips the absolute lighting across both rails at once:
  fine for a one-rail strip (a flipped right-edge strip *is* a left-edge
  strip), wrong for the piece that owns the pair. `check_trims.py` re-derives
  whether the mirror is still dangerous rather than trusting a comment.
- **Two straights at different rotations stack on one cell**, by the same
  rotation-equality rule as the markers (§3). The runner exists for the
  ergonomics, a corridor's two rails in one drag, and so one image owns both
  rails. It costs double, so the convenient route is not also the cheap one.
- **All three draw at 4% overdraw** (`drawSize 1.04`, square): at shared quad
  edges the sampler resolves the two edge-texel columns differently as the
  camera moves, and the overlap kills the shimmer.
