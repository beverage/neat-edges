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

All three are generated, never hand-edited:

```bash
python3 devtools/make_edge_art.py
```

`EdgeOne` is only ever the marker's placement ghost and build-menu icon —
nothing is drawn on the map. That is not optional decoration: a fully
transparent texture was tried first and made the marker unplaceable, because
the ghost, the rotation preview and the selected thing all draw from the same
graphic. `AreaExpand` and `AreaClear` are the paint and clear tools' icons: a
ring (a tile hard on every side) with a plus or a minus.

`EdgeOne.png` puts its band in the **north** margin, and `Graphic_Single`
rotation spins it, which is what makes `Rotation` name the hugged edge with no
offset. Change that art and the relationship must be re-measured — the harness
pins it (`mask.rotation.*`) so an inversion fails loudly.

A `.dds` beside a PNG silently shadows it with no timestamp check, so a
regenerated texture can appear not to change. `*.dds` is gitignored; delete one
if a texture refuses to update.

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
| `MapComponent_EdgeOverlay.cs` | the overlay drawer, and the area's load-time housekeeping |
| `Patch_EdgeOverlayToggle.cs` | puts it on the bottom-right toggle row |
| `DebugTools_NeatEdges.cs` | the compare toggle |
| `Harness.cs` | the regression cases — see [TESTING.md](TESTING.md) |
| `HarmonyInit.cs` | `PatchAll`, and the startup anchor report |

Outside `Source/`: `Patches/NeatEdges_Designators.xml` puts the two tools on
the Floors tab, and `Languages/English/Keyed/NeatEdges.xml` holds every string
the C# shows a player.

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
