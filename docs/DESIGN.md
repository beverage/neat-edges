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
and one def covers every combination of edges. The all-sides def exists to save
three build orders, not because the combination was otherwise unreachable.

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
| all sides | N E S W | **8** |

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

## 7. Compared with an area-based approach

A reasonable alternative is to let the player designate a **region** and harden
everything inside or around it. It is worth being clear about the trade, because
neither wins outright.

**Where area is better**

- Fewer actions for the common case. "Box in this pond" is one drag rather than
  a lap of the perimeter.
- Nothing to get wrong about rotation.
- The player thinks in areas already — zones, rooms, growing zones, home area.

**Where sided is better**

- **It can express a one-sided boundary.** An area hardens its whole outline; it
  cannot say "crisp against the courtyard, soft against the marsh". A sided
  marker can, because the unit *is* the side.
- **Corners are exact rather than inferred.** An area has to derive its own
  outline and decide what to do at re-entrant corners; a sided model gets them
  from the placed pieces and seals the shared corner points from the two edges
  that actually meet there.
- **It composes with visible trim.** The same marker extension is what Fine
  Establishments' floor borders carry, so a decorated strip hardens the edge it
  hugs. An area has nothing to attach to a piece of art.
- **No terrain slot, no region state.** Markers are ordinary non-edifice things:
  no foundation grid, no scribed area, and removing the mod removes them
  cleanly.

**The honest summary:** area is a better *interface* for the common case; sided
is a better *model* of the thing. The two are not exclusive — an area designator
that places sided markers around its outline would be the best of both, and is
the obvious future work if the per-piece placement proves tedious. What should
not happen is an area mechanism that hardens tiles as its unit, because that
reintroduces exactly the corner and end-of-run artefacts this design exists to
remove, and the project has now learned that lesson four times.
