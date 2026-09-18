# AGENTS.md

A RimWorld 1.6 mod that stops terrain fading across a tile boundary. Two
placeable markers, one Harmony assembly, two generated placeholder textures.
It ships no floors and no terrain of its own — it changes how *other* people's
floors meet what they touch.

| Doc | Contents |
|---|---|
| [README.md](README.md) | what the mod does, for players |
| [docs/DESIGN.md](docs/DESIGN.md) | the blend gate, the three mechanisms, why sided rather than area |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | build, dev loop, file map |
| [docs/TESTING.md](docs/TESTING.md) | the harness, and what it deliberately does not cover |

## Verify your work

```bash
dotnet build Source/NeatEdges/NeatEdges.csproj -c Release
```

`xmllint --noout` on every changed def. Textures are **generated, never
hand-edited** — edit `devtools/make_edge_art.py` and re-run it.

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
reads terrain defs and the foundation grid, nothing else. This mod replaces
that method and gives the cell a say, driven by the marked things standing on
it.

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

## Rules that validate fine and fail later

**`Patch_SidedFadeBlock` is a COPIED METHOD BODY.** It is vanilla's
`Regenerate` verbatim plus the lines marked `SIDED`. Re-diff it against the
decompile on every game update. It fails safe — a missing reflection handle
logs once and returns control to vanilla — but it cannot detect vanilla
*changing* around it.

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
the bottom-right row is the sole way to find one. It shares `EdgeMaskAt` with
the renderer on purpose — what is highlighted and what is hardened cannot drift.

**Map components are scribed by type.** `MapComponent_EdgeOverlay` is compiled
unconditionally; one that existed only under a build symbol would make every
save taken with it log a missing-type error without it.

## Scope rules

**`NE_` prefixes defNames; textures live under `Textures/NeatEdges/`.** Both
namespaces are global across every loaded mod.

**No floors, no terrain, no art beyond the two ghosts.** This mod works with
anyone's flooring; shipping its own would put it in competition with the
mods it exists to serve.

**Other mods opt in by extension, never by name.** `BlocksTerrainFade` is
extension-keyed so a third party's overlay can adopt the behaviour without this
mod knowing it exists. Fine Establishments' borders do exactly that, behind
`MayRequire` so they stay inert for players without this installed.

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
every rotatable building, ours and Fine Establishments' floor borders alike. It
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
