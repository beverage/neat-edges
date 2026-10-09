# TESTING

```bash
devtools/run-harness.sh              # alongside whatever is already running
devtools/run-harness.sh --full       # your own mod list, copied
devtools/run-harness.sh --exclusive  # refuse if any RimWorld is up
devtools/run-harness.sh --with owlchemist.perspectivepaths  # plus one mod
```

Builds Release with the harness compiled in (`-p:Harness=true`), launches an
isolated instance with `-quicktest -neatedges-harness`, waits for it to run
every case and quit itself, prints the report, and exits non-zero if a case
failed. On the way out it rebuilds plain Release, so `Assemblies/` holds the
shipping dll again.

**Alongside is the default here**, unlike the other children — several mods in
this constellation get worked on at once, and refusing to start beside a running
instance makes the gate unrunnable most of the day. `--exclusive` is the right
mode for a final pre-release run.

**A game running beside the harness keeps the dll it loaded.** `Mods/NeatEdges`
is a symlink to this checkout, so that game loaded `Assemblies/NeatEdges.dll`
from here. Neither build writes that file: both compile into `dist/build/`, and
each result is copied in as `NeatEdges.dll.new` and renamed over the target. A
rename swaps the directory entry and nothing else, so a running game keeps the
file it opened and the next one to start loads the new one. On the way out the
script runs `check-shipped-dll.py` and lists `Assemblies/`, and warns if that
directory holds anything but the one shipping dll.

A bare `dotnet build` promises none of that, so check for a running game before
building straight into `Assemblies/`. On macOS with the .NET 9 SDK it happens to
leave a running game alone, because MSBuild's copy unlinks the old file and
clones in the new one (measured 2026-09-23). Where the runtime cannot clone, as
on a volume without clone support, it truncates the existing file and writes
into it.

## The focus stall, and why a run no longer needs a click

An unfocused instance used to stall on the loading screen: around 49 log lines,
near 0% CPU, never recovering. The cause was RimWorld's own `runInBackground`
preference, which defaults to off in a fresh save-data folder. The script now
seeds it on, and that is measured, not assumed: with focus held on another app
throughout, 3 of 3 seeded runs passed and 3 of 3 unseeded runs stalled. The
script's header has the full account, including what was ruled out first.

The bail-out stays. The script gives up at `STARTUP_GRACE` (120s) once the log
has also gone quiet, rather than burning the full timeout, and it tells "never
reached RimWorld's own startup" apart from "started but the harness never ran",
because one message for both once misdirected an entire session.

## What the cases cover

Every rule bug this mod has had lived in `EdgeMaskAt`, which is a pure function
from marker layout to an 8-bit mask. That is decidable without rendering
anything, and each of these cost a game restart and a screenshot to find the
first time.

The one exception lives in `RenderMaskAt`, the mask the renderer consumes: the
layout mask less any corner pin that holds nothing back (DESIGN §4). It reads
terrain, so its cases paint terrain as a fixture, and `Clear()` puts the old
terrain back.

| Case | Pins |
|---|---|
| `transpiler.applied` | anchors found — and prints the report, so an ambiguous anchor is visible |
| `defs.single` | the marker exists |
| `mask.rotation.*` | `Rotation` names the hugged edge, no offset — guards the art relationship |
| `mask.single.three` | one edge hardens 3 directions |
| `mask.corner.five` | two adjacent harden 5 |
| `mask.runner.six` | two opposite harden 6 |
| `mask.twoSided` | the cell *across* the edge hardens too |
| `mask.cornerSeal` | the neighbour gets its shared corner and nothing else |
| `mask.stacking.fourIsEight` | four stacked singles harden all 8 directions |
| `render.junction.layoutSeals` | the premise: under a floor's hardened edge, the layout mask seals the lichen tile's corner where sand meets it |
| `render.junction.groundCornerLit` | the render mask leaves that corner lit, so sand fades onto the lichen tile right up to the floor, as it does beside it |
| `render.junction.floorCornerKept` | the floor tile above keeps its corner pin: sand reaches it only by the diagonal |
| `render.junctionZone.groundCornerLit` | the same junction hardened only by the east floor tile, as a painted tile hardens it: the lichen tile's corner is lit although its north side is open onto floor |
| `render.junctionZone.floorCornerKept` | the west floor tile keeps its corner pin although lichen now reaches that corner through its open south side: sand sits on the diagonal. The first version lost this pin |
| `render.relight.kept` | a steel tile in soil, hardened on one edge, keeps both pins: the soil re-lighting the corners is the soil the edge holds back |
| `render.taper.kept` | the neighbour of a lone hardened edge keeps its shared corner pin: its open side there is the same floor, so a lit corner would cut a seam inside one surface |
| `trims.defs.*` | each trim loads stuffable and paintable, names its shape, has a build-menu icon of its own, and every one but the inside corner carries the extension; without it a trim is decoration, and nothing in game says so. The inside corner must NOT carry it: it covers a corner, not an edge, and the extension with an empty list would harden all four edges. Without its own `uiIconPath` the engine takes the graphic's texture for the button, which would be the strip |
| `trims.masks.*` | each of the six trims hardens exactly the edges its bands run along, at all four rotations, the inside corner none. The expected edges are written out by hand rather than computed by the production formula, so a wrong offset in the defs or the formula fails instead of agreeing with itself |
| `trims.stack.*` | a runner's turning tile works: an inside corner can be placed over a corner on the same tile, both stand once spawned, and the tile hardens only the corner's two edges |
| `trims.render.atlasUnpatched` | nothing of ours patches `GlobalTextureAtlasManager.TryInsertStatic`. The strip never asks to enter the atlas, so a patch there would only cost a call for every texture at startup |
| `trims.render.*` | each trim draws with `Graphic_StripTrim` at exactly one tile, from a texture that repeats along its band, is clamped across it, and is absent from every static atlas. Each property is part of a clean joint and each is lost silently: atlased, the strip could not repeat and a joint would borrow a hairline from the neighbouring texture; clamped along, a run smears the strip's last column; wrapped across, a band's outer row blends with the other half's; oversized, a piece draws over its neighbour |
| `trims.geometry` | the C# geometry reproduces `devtools/strip_trim_geometry.txt`, every vertex and UV of every shape at every rotation. `check_trims.py` holds the Python model to the same file, so the shapes it renders and compares with their drawings are the shapes the game draws |
| `trims.strip.*` | per trim: it prints on the white tinted material; its blueprint keeps the class, honours the render queue its def asks for (vanilla's 2950, or whatever another mod sets), keeps its colour on the material and resolves its shape through `entityDefToBuild`; the ghost keeps the class, on the EdgeDetect shader, with a mesh inside its tile; Copy finds a designator for it. `trims.strip.light.*` pins which half of the strip each edge samples and which way up, written out per rotation. `paintSharesOneCall`, `listsInStep` and `unpaintRestores`: a painted and an unpainted straight print into one submesh carrying both colours, its vertex, UV and colour lists stay in step, and unpainting gives the stuff colour back |
| `trims.cost.*` | `oneTexture`: the trims draw one texture on the map. `oneCall.*`: one draw call per section for every shape at every rotation and for runs of straights, each in one stuff, in mixed stuff and paint, and as blueprints. It also prints what it measured, as INFO lines: texture formats and bytes, the methods Neat Edges patches, and calls and vertices per layout. It names no trim graphic class, so the same case measured the per-facing trims before them: 21, 24 and 21 calls on the 24-piece layout |
| `area.lazy` | a map nobody painted carries no area; runs before anything paints |
| `area.designators.*` | both tools are on the Zone tab, with icons that loaded |
| `overlay.icon` | the overlay toggle shows our icon at 48 px, not the vanilla glyph it falls back to when ours is missing |
| `area.equalsFourEdges` | the 5×5 of masks around a painted tile equals the 5×5 around four stacked single-edge markers, cell by cell |
| `area.tools.refuseEachOther` | paint refuses painted tiles, clear refuses unpainted ones |
| `area.clearRestores` | clearing puts all 25 masks back to zero |
| `area.clearRemoves` | clearing the last painted tile through the clear tool takes the area off the map, so no saved area outlives the mod; `area.clearKeepsWhilePainted` is the control, clearing one of two tiles |
| `area.emptyDropped` | the finalization tidy drops an empty area (what an adopted Perspective: Paths zone usually is) and keeps a painted one beside it |
| `area.paintDirtiesTerrain` | painting dirties the terrain mesh in its own section and the next one across the boundary |
| `area.repaintIsQuiet` | the control: painting a painted tile dirties nothing |
| `area.duplicatesMerge` | two areas on one map fold into one holding both sets of tiles |
| `migration.resolvesLegacyClass` | the type lookup answers `PerspectivePaths.Area_InvertEdges` with ours |
| `migration.leavesOtherClassesAlone` | the control: an unrelated missing class still resolves to nothing |
| `migration.loadsRealNode` | a node copied verbatim from a real save loads through the engine's own loader, as our type, with all 851 tiles decoded |
| `migration.hardensOnMap` | an adopted tile hardens all eight directions |
| `migration.savesAsOurs` | an adopted area saves back under this mod's class, with the legacy name gone from the file |
| `migration.roundTrips` | and loads again with the same ID and every tile |
| `migration.marksOnlyAdopted` | the legacy node's area is marked as adopted, and the same area reloaded from this mod's own class is not |
| `migration.countsPaintedOnly` | the one-time message counts adopted areas with painted tiles only: the real fixture counts, an empty adopted area (what Perspective: Paths leaves on every map) does not |
| `migration.standsDownWhileInstalled` | with Perspective: Paths loaded, the lookup answers with its class and nothing counts as adopted |
| `yield.toolsShownWithoutPerspectivePaths` | without it, both area tools show and paint |
| `yield.toolsHiddenWithPerspectivePaths` | with it, both are hidden and refuse every cell; the two runs are each other's control |
| `yield.markerStays` | the marker's build tool shows in both runs |
| `yield.handBackIntoItsZone` | with it, our area moves into the zone it made when the map finalized: every tile arrives, ours is gone, and our model stops hardening those tiles |
| `yield.handBackMakesItsZone` | the same when it has no zone yet: one is made through its own constructor, and it is the one its lookup by label finds |

Each case clears its fixtures first, painted areas and painted terrain included. With two-sided
hardening and corner sealing, a stray marker reaches beyond its own cell, so
leaked state would make later cases depend on earlier ones.

**A case that throws is a FAIL, and the suite goes on.** The report is only
logged at the end, so before that guard one throw cost every later case and the
whole report, and the run said FAILED with nothing else. It happened: a passing
case built its failure message before `Check` decided, and indexed an array
with -1.

`migration.loadsRealNode` counts decoded tiles rather than reading `TrueCount`.
The count is saved beside the grid and read back verbatim, so it would pass on a
payload that failed to decode.

**Perspective: Paths takes two runs.** The migration can only be tested without
it and the hand-back only with it, so the default run skips `yield.handBack`,
and a run with it skips the migration's load cases and asserts
`migration.standsDownWhileInstalled` instead:

```bash
devtools/run-harness.sh
devtools/run-harness.sh --with owlchemist.perspectivepaths
```

`--with` adds a mod to the minimal list. Its Workshop copy has to be
subscribed, with Steam running, for the game to find it. `--full` covers the
same ground when your own list has it.

## What it deliberately does not cover

**Whether the edges look right.** That is a taste question and stays a
screenshot. Use the compare toggle (debug menu → Neat Edges → Toggle hard edges)
to shoot both states from one camera position; it suppresses the rendering while
leaving the overlay showing where every marker is.

**How the trims look where they meet.** The harness pins what makes a joint
clean, but whether one is clean is a screenshot: a close zoom near the art's
own resolution, and the game's closest normal zoom, where tile boundaries fall
between pixels and a sampling seam would show (see DEVELOPMENT, Textures).

**An offline contact sheet of the hardening.** The trims have one,
`check_trims.py`, because their strip and geometry are ours, and the model it
renders from is held to the C# by a golden file. The hardening is not: its
output is vanilla's nine-vertex fan mesh, so drawing it offline would mean
reimplementing `Regenerate`, and the sheet would then certify the model rather
than the game. That is the trap of approximating engine maths and then
trusting the approximation.

**The IL anchors being semantically right.** The harness asserts the transpiler
*applied* and prints what it resolved, which would have made the wrong-store bug
obvious, but it cannot tell a correct anchor from a plausible wrong one. That
gap is why the anchor report includes the candidate count.

## Migration: the real-save acceptance

The harness proves a saved node loads as ours and saves back as ours. It cannot
prove a player's save does, because that takes the save's own mod list, and the
Workshop text promises exactly that.

**Done on 2026-09-23**, headlessly, on a real 234-mod colony save carrying one
Perspective: Paths area of 851 tiles. RimWorld loads `Saves/autostart.rws` at
startup when dev mode is on, so an isolated `-savedatafolder` holding a copy of
the save, the player's whole `Config/` (mod settings matter: without them every
mod runs at defaults and the load is not the player's), and a mod list minus
Perspective: Paths loads the colony with nothing driving the window. Launch to
loaded colony took 151 seconds. The log carried `[NeatEdges] adopted 1
hard-edge area(s)` and no `Could not find class PerspectivePaths`.

**The control**, the same save with neither mod, is why the migration matters:
the unresolvable area loads as a null, the engine's area manager throws on it
while linking its list, and **the map comes up with no areas at all**: home,
allowed and roof areas lost with it. On that mod list it then broke two other
mods' map components that read the area manager, and every thing on the map
threw while spawning until RimWorld stopped logging.

**The hand-back went through a real load too** (2026-09-24), because the order
inside map finalization only exists there: merge, then our hand-back, then
Perspective: Paths' own postfix looking its zone up by label. A quick-test
colony's autosave, with one of our areas injected (the harness fixture's 851
tiles), was loaded with Perspective: Paths added. The log carried
`[NeatEdges] moved 1 hard-edge area(s) into Perspective: Paths' zone` and no
errors. The next autosave held no area of ours and exactly one of its zones,
with all 851 tiles: made through its constructor, then adopted by its own
lookup rather than joined by a second, empty one.

To repeat the migration by hand instead, on a save with areas painted with
Perspective: Paths:

1. Load it with Perspective: Paths still installed, and save.
2. Remove Perspective: Paths from the mod list and restart.
3. Load the save. Check:
   - the log carries `[NeatEdges] adopted N hard-edge area(s)` and no
     `Could not find class PerspectivePaths`;
   - the message about the areas appears once, after the load;
   - the overlay toggle shows the old painted tiles, and they render hardened;
   - the Zone tab's clear tool removes them.

Perspective: Paths exists as two Workshop items, the original and a
continuation, and both write the same class name. The harness fixture came from
the continuation. The original's node shape is read from its source, not from a
save it wrote.

## Interop

`--full` is the only list that exercises the mods this one shares a method with.
**Dub's Paint Shop** transpiles the same `SectionLayer_Terrain.Regenerate`;
paint disappearing from every floor in the colony is the signature of a
composition failure, and is what happened when this was a prefix returning
false. There is no automated case for it yet — checking a painted floor still
has its colour after a `--full` run is the manual step, and it belongs in the
harness.

**Perspective: Paths** transpiles the same method and always applies first
(DESIGN §8). The transpiler report shows it: with it loaded, the vert anchor
reads five higher than without (496 against 491 on the minimal list), which is
its insertion.
