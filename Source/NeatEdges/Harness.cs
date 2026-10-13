// HARNESS only — see the configuration table in NeatEdges.csproj. The
// regression harness and its -neatedges-harness launch flag are dev tooling
// and do not ship: a Release build compiles this file out, so the flag does
// not exist in a player's install at all. devtools/run-harness.sh asks for it
// back with -p:Harness=true on top of Release codegen.
//
// The guard is whole-file, always. Never put an #if HARNESS inside a file that
// ships — a shipping build and a harness build must differ by the presence of
// these types and by nothing else, or a harness run stops saying anything
// about the assembly that goes out. check-invariants.py enforces it.
#if HARNESS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Boots the regression harness when the game is launched with
    /// <c>-neatedges-harness</c> (devtools/run-harness.sh drives the loop:
    /// isolated save-data folder, wait on OUR pid, grep the report).
    ///
    /// Same shape as Apparel Painter's: a flag-gated MonoBehaviour, deliberately
    /// NOT a GameComponent — components are scribed into every save
    /// (Game.ExposeData, LookMode.Deep), and a dev-only feature has no business
    /// in a player's save file. Without the flag, nothing here allocates.
    ///
    /// It SHIPS IN NO CONFIGURATION: this file is behind #if HARNESS, which a
    /// plain Release build does not define, so a player's install carries
    /// neither the suite nor the flag. What used to be argued here — that a
    /// gate is only worth running if it asserts against the literal assembly
    /// players install — is now paid by the guard being whole-file, so the
    /// build under test and the build that goes out differ by the presence of
    /// these types and by nothing else.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class HarnessBoot
    {
        internal const string Arg = "neatedges-harness";

        static HarnessBoot()
        {
            if (!GenCommandLine.CommandLineArgPassed(Arg)) return;

            GameObject driver = new GameObject("NeatEdgesHarnessDriver");
            UnityEngine.Object.DontDestroyOnLoad(driver);
            driver.AddComponent<HarnessDriver>();
        }
    }

    /// <summary>
    /// Waits for the quicktest map to finish loading, runs once, and quits the
    /// process pass or fail — the caller waits on the pid and reads the log,
    /// never a screen.
    /// </summary>
    internal class HarnessDriver : MonoBehaviour
    {
        internal bool started;

        public void Update()
        {
            if (started) return;
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            if (LongEventHandler.AnyEventNowOrWaiting) return;

            started = true;
            Harness.RunAndQuit();
        }
    }

    /// <summary>
    /// The regression harness.
    ///
    /// WHAT IT IS FOR, precisely: every rule bug this mod has had lived in
    /// <see cref="Patch_SidedFadeBlock.EdgeMaskAt"/>, which is a PURE function
    /// from marker layout to an 8-bit mask. Missing flanking diagonals, the
    /// unsealed inside corner, one-sided hardening, the 3/5/6 counts — all of
    /// them are decidable without rendering anything or looking at a screen, and
    /// every one of them cost a full game restart and a screenshot to find.
    ///
    /// WHAT IT IS NOT FOR: whether the edges LOOK right. That stays a
    /// screenshot, because it is a taste question and a harness has no business
    /// having opinions about it.
    ///
    /// Deliberately NOT an offline Python contact sheet, the way Fine
    /// Establishments checks its art. Our output is vanilla's 9-vertex fan mesh,
    /// not our own textures — modelling that offline would produce a sheet that
    /// certifies the model rather than the game, which is the exact trap of
    /// approximating engine math and then trusting the approximation.
    ///
    /// Direction indices throughout are GenAdj.AdjacentCellsAroundBottom:
    /// 0=S 1=SW 2=W 3=NW 4=N 5=NE 6=E 7=SE.
    /// </summary>
    internal static class Harness
    {
        internal static readonly StringBuilder Report = new StringBuilder();
        internal static int Passed;
        internal static int Failed;
        internal static int Skipped;

        internal static readonly List<Thing> Spawned = new List<Thing>();

        internal static void RunAndQuit()
        {
            bool passed = false;
            try
            {
                passed = Run(Find.CurrentMap);
            }
            catch (Exception e)
            {
                // Never let a throw leave the process alive: an automated caller
                // waiting on exit would hang forever.
                Log.Error("[NeatEdges] harness threw: " + e);
            }

            Log.Message("[NeatEdges] harness auto-run: " + (passed ? "PASSED" : "FAILED"));
            Root.Shutdown();
        }

        internal static bool Run(Map map)
        {
            Report.Length = 0;
            Passed = 0;
            Failed = 0;
            Skipped = 0;
            Report.AppendLine("[NeatEdges] regression harness");

            Guard("transpiler", CaseTranspilerApplied);
            Guard("defs", CaseDefsPresent);
            Guard("area.lazy", () => CaseAreaIsLazy(map));   // first: before any case paints
            Guard("mask.rotation", () => CaseRotationMapping(map));
            Guard("mask.single", () => CaseSingleEdgeIsThree(map));
            Guard("mask.corner", () => CaseCornerIsFive(map));
            Guard("mask.runner", () => CaseRunnerIsSix(map));
            Guard("mask.twoSided", () => CaseHardeningIsTwoSided(map));
            Guard("mask.cornerSeal", () => CaseOutsideCornerSeals(map));
            Guard("mask.stacking", () => CaseStackingCombines(map));
            Guard("render.junction", () => CaseJunctionCornerLit(map));
            Guard("render.junctionZone", () => CaseJunctionZoneOnly(map));
            Guard("render.relight", () => CaseRelightPinKept(map));
            Guard("render.taper", () => CaseTaperCornerLit(map));
            Guard("render.loneEndSpike", () => CaseLoneEndSpikeKept(map));
            Guard("render.borderEnd", () => CaseBorderEndLit(map));
            Guard("trims.defs", CaseTrimDefs);
            Guard("trims.masks", () => CaseTrimMasks(map));
            Guard("trims.stack", () => CaseTrimStack(map));
            Guard("trims.render", CaseTrimRender);
            Guard("trims.geometry", CaseStripGeometry);
            Guard("trims.strip", () => CaseStripTrim(map));
            Guard("trims.buttons", CaseTrimButtons);
            Guard("trims.buttons.rebuild", CaseTrimButtonsSurviveRebuild);
            Guard("trims.buildable", () => CaseTrimsBuildable(map));
            Guard("trims.joins", () => CaseTrimJoinsGiven(map));
            Guard("trims.diagonal.ends", () => CaseDiagonalEndsOnMap(map));
            Guard("trims.diagonal.refresh", () => CaseDiagonalRefresh(map));
            Guard("trims.cost", () => CaseTrimCost(map));
            Guard("area.designators", CaseAreaDesignatorsRegistered);
            Guard("overlay.icon", CaseOverlayIcon);
            Guard("area.equalsFourEdges", () => CaseAreaEqualsFourEdges(map));
            Guard("area.clear", () => CaseAreaClearRestores(map));
            Guard("area.clearRemoves", () => CaseAreaClearRemoves(map));
            Guard("area.emptyDropped", () => CaseAreaEmptyDropped(map));
            Guard("area.dirty", () => CaseAreaPaintDirtiesTerrain(map));
            Guard("area.duplicates", () => CaseAreaDuplicatesMerge(map));
            Guard("migration.resolves", CaseMigrationResolvesLegacyClass);
            Guard("migration.realNode", () => CaseMigrationLoadsRealNode(map));
            Guard("migration.roundTrip", CaseMigrationRoundTrips);
            Guard("migration.countsPainted", () => CaseMigrationCountsPainted(map));
            Guard("yield.tools", () => CaseYieldTools(map));
            Guard("yield.handBack", () => CaseYieldHandBack(map));
            Guard("naname", () => CaseNanameDiagonals(map));

            Report.AppendLine($"result: {Passed} passed, {Failed} failed, {Skipped} skipped");
            Log.Message(Report.ToString());
            return Failed == 0;
        }

        /// <summary>
        /// A case that throws is a FAIL carrying its exception, and the suite
        /// goes on. Without this, one throw skipped every later case AND the
        /// report, because the report is only logged at the end: the run said
        /// FAILED and nothing else, and the only evidence was a stack trace
        /// further up the log. (It happened: a passing case built its failure
        /// message eagerly and indexed an array with -1.)
        /// </summary>
        internal static void Guard(string name, Action run)
        {
            try
            {
                run();
            }
            catch (Exception e)
            {
                Failed++;
                Report.AppendLine("  FAIL  " + name + " — threw " + e.GetType().Name + ": " + e.Message);
                Log.Warning("[NeatEdges] harness case " + name + " threw: " + e);
                Clear();
            }
        }

        internal static void Check(bool condition, string name, string detail)
        {
            if (condition)
            {
                Passed++;
                Report.AppendLine("  PASS  " + name);
            }
            else
            {
                Failed++;
                Report.AppendLine("  FAIL  " + name + " — " + detail);
            }
        }

        internal static void Skip(string name, string why)
        {
            Skipped++;
            Report.AppendLine("  SKIP  " + name + " — " + why);
        }

        // ---- fixtures ----------------------------------------------------

        internal static ThingDef Single =>
            DefDatabase<ThingDef>.GetNamedSilentFail("NE_HardEdge");

        /// <summary>Well inside the map, so no case is testing a border.</summary>
        internal static IntVec3 Origin(Map map) =>
            new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);

        internal static void Place(Map map, IntVec3 cell, ThingDef def, Rot4 rot)
        {
            // The trims are stuffable, and MakeThing without a stuff for one
            // logs an error; the markers are not, and DefaultStuffFor answers
            // null for them, which is what MakeThing expects.
            Thing t = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
            t.SetFactionDirect(Faction.OfPlayer);
            Spawned.Add(GenSpawn.Spawn(t, cell, map, rot));
        }

        /// <summary>
        /// Each case owns a clean map. Leaving markers behind would make later
        /// cases depend on earlier ones — and since hardening is two-sided and
        /// seals corners, a stray marker reaches further than its own cell.
        ///
        /// Painted areas go too, and go entirely rather than being cleared, so
        /// every case starts from the state a map nobody painted is in.
        /// </summary>
        internal static void Clear()
        {
            for (int i = Spawned.Count - 1; i >= 0; i--)
            {
                if (Spawned[i] != null && !Spawned[i].Destroyed)
                {
                    Spawned[i].Destroy(DestroyMode.Vanish);
                }
            }
            Spawned.Clear();

            Find.CurrentMap?.areaManager?.AllAreas.RemoveAll(a => a is Area_HardEdges);
            Unpaint(Find.CurrentMap);
        }

        /// <summary>Terrain a case painted, with what was there before.</summary>
        internal static readonly Dictionary<IntVec3, TerrainDef> Painted = new Dictionary<IntVec3, TerrainDef>();

        /// <summary>
        /// Paint a rectangle, remembering each cell's first terrain so
        /// <see cref="Clear"/> can put it back. The render-mask cases read terrain,
        /// so terrain is a fixture like any marker.
        /// </summary>
        internal static void Paint(Map map, CellRect rect, TerrainDef def)
        {
            foreach (IntVec3 c in rect)
            {
                if (!Painted.ContainsKey(c)) Painted[c] = map.terrainGrid.TerrainAt(c);
                map.terrainGrid.SetTerrain(c, def);
            }
        }

        internal static void Unpaint(Map map)
        {
            if (map != null)
            {
                foreach (KeyValuePair<IntVec3, TerrainDef> cell in Painted)
                {
                    map.terrainGrid.SetTerrain(cell.Key, cell.Value);
                }
            }
            Painted.Clear();
        }

        /// <summary>
        /// A floor-covering edifice turns its cell into `Underwall` for the
        /// renderer, which would change what a terrain case is testing.
        /// </summary>
        internal static bool Covered(Map map, CellRect rect)
        {
            foreach (IntVec3 c in rect)
            {
                Thing edifice = c.GetEdifice(map);
                if (edifice != null && edifice.def.coversFloor) return true;
            }
            return false;
        }

        /// <summary>
        /// Every edge mask in the 5x5 around a cell, row by row. Wide enough for
        /// everything one tile can change: its own mask, the tiles across its
        /// edges, and the corners it seals for the diagonals.
        /// </summary>
        internal static int[] Neighbourhood(Map map, IntVec3 c)
        {
            int[] masks = new int[25];
            int i = 0;
            for (int dz = -2; dz <= 2; dz++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    masks[i++] = Patch_SidedFadeBlock.EdgeMaskAt(c + new IntVec3(dx, 0, dz), map);
                }
            }
            return masks;
        }

        internal static int FirstDifference(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return i;
            }
            return -1;
        }

        internal static int Bits(int mask)
        {
            int n = 0;
            for (int i = 0; i < 8; i++) if ((mask & (1 << i)) != 0) n++;
            return n;
        }

        internal static string Show(int mask)
        {
            string[] names = { "S", "SW", "W", "NW", "N", "NE", "E", "SE" };
            List<string> on = new List<string>();
            for (int i = 0; i < 8; i++) if ((mask & (1 << i)) != 0) on.Add(names[i]);
            return on.Count == 0 ? "(none)" : string.Join(",", on.ToArray());
        }

        // ---- cases -------------------------------------------------------

        /// <summary>
        /// The transpiler found its anchors. This is the case that would have
        /// caught the worst bug so far: the neighbour-store anchor matched the
        /// FIRST of two identical stores — the out-of-bounds early-continue —
        /// so the patch applied without error and hardened nothing. No guard
        /// catches that, because every structural check passed.
        /// </summary>
        internal static void CaseTranspilerApplied()
        {
            Check(Patch_SidedFadeBlock.Applied, "transpiler.applied",
                "anchors not found — " + (Patch_SidedFadeBlock.AnchorReport ?? "never ran"));

            Report.AppendLine("        " + (Patch_SidedFadeBlock.AnchorReport ?? "no report"));
        }

        internal static void CaseDefsPresent()
        {
            Check(Single != null, "defs.single", "NE_HardEdge missing");
        }

        /// <summary>
        /// Rotation names the hugged edge, with no offset. Measured from the art
        /// originally (the band sits in the north margin of EdgeOne.png); this
        /// pins it so a texture change cannot silently invert it.
        /// </summary>
        internal static void CaseRotationMapping(Map map)
        {
            if (Single == null) { Skip("mask.rotation", "def missing"); return; }

            Rot4[] rots = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
            int[] expect = { 4, 6, 0, 2 };   // N, E, S, W in adjacency indices

            for (int i = 0; i < 4; i++)
            {
                Clear();
                IntVec3 c = Origin(map);
                Place(map, c, Single, rots[i]);

                int own = Patch_SidedFadeBlock.ComputeOwnMask(c, map);
                Check(own == (1 << expect[i]), "mask.rotation." + rots[i].ToStringHuman(),
                    $"got {Show(own)}, expected {Show(1 << expect[i])}");
            }
            Clear();
        }

        internal static void CaseSingleEdgeIsThree(Map map)
        {
            if (Single == null) { Skip("mask.single", "def missing"); return; }

            Clear();
            IntVec3 c = Origin(map);
            Place(map, c, Single, Rot4.North);

            int mask = Patch_SidedFadeBlock.EdgeMaskAt(c, map);
            int expected = (1 << 3) | (1 << 4) | (1 << 5);   // NW, N, NE

            Check(mask == expected, "mask.single.three",
                $"got {Show(mask)} ({Bits(mask)}), expected {Show(expected)} (3)");
            Clear();
        }

        /// <summary>Two adjacent edges: both cardinals plus the three diagonals they flank.</summary>
        internal static void CaseCornerIsFive(Map map)
        {
            if (Single == null) { Skip("mask.corner", "def missing"); return; }

            Clear();
            IntVec3 c = Origin(map);
            Place(map, c, Single, Rot4.North);
            Place(map, c, Single, Rot4.East);   // rotation-equality: same def, same cell

            int mask = Patch_SidedFadeBlock.EdgeMaskAt(c, map);
            int expected = (1 << 3) | (1 << 4) | (1 << 5) | (1 << 6) | (1 << 7);

            Check(mask == expected, "mask.corner.five",
                $"got {Show(mask)} ({Bits(mask)}), expected {Show(expected)} (5)");
            Clear();
        }

        /// <summary>Two opposite edges: both cardinals and all four diagonals.</summary>
        internal static void CaseRunnerIsSix(Map map)
        {
            if (Single == null) { Skip("mask.runner", "def missing"); return; }

            Clear();
            IntVec3 c = Origin(map);
            Place(map, c, Single, Rot4.North);
            Place(map, c, Single, Rot4.South);

            int mask = Patch_SidedFadeBlock.EdgeMaskAt(c, map);
            int expected = (1 << 0) | (1 << 1) | (1 << 3) | (1 << 4) | (1 << 5) | (1 << 7);

            Check(mask == expected, "mask.runner.six",
                $"got {Show(mask)} ({Bits(mask)}), expected {Show(expected)} (6)");
            Clear();
        }

        /// <summary>
        /// An edge is shared by two cells, not owned by one — so a marker on the
        /// north edge of C must harden the SOUTH edge of the cell above it. This
        /// is what makes it not matter which side you place on.
        /// </summary>
        internal static void CaseHardeningIsTwoSided(Map map)
        {
            if (Single == null) { Skip("mask.twoSided", "def missing"); return; }

            Clear();
            IntVec3 c = Origin(map);
            Place(map, c, Single, Rot4.North);

            IntVec3 above = c + IntVec3.North;
            int mask = Patch_SidedFadeBlock.EdgeMaskAt(above, map);
            int expected = (1 << 0) | (1 << 1) | (1 << 7);   // S, SW, SE

            Check(mask == expected, "mask.twoSided",
                $"cell across the edge got {Show(mask)}, expected {Show(expected)}");
            Clear();
        }

        /// <summary>
        /// The tile beside the marker gets its shared corner sealed and NOTHING
        /// else — no cardinal of its own. That corner is what a lone edge used
        /// to leave open, so the fringe curled around the end of a run.
        /// </summary>
        internal static void CaseOutsideCornerSeals(Map map)
        {
            if (Single == null) { Skip("mask.cornerSeal", "def missing"); return; }

            Clear();
            IntVec3 c = Origin(map);
            Place(map, c, Single, Rot4.North);

            IntVec3 west = c + IntVec3.West;
            int mask = Patch_SidedFadeBlock.EdgeMaskAt(west, map);

            Check(mask == (1 << 5), "mask.cornerSeal",
                $"west neighbour got {Show(mask)}, expected only NE");
            Clear();
        }

        /// <summary>
        /// Rotation-equality is what lets one def cover all fifteen edge
        /// combinations: same def + same rotation + same cell is the only
        /// combination the engine refuses. Four stacked must harden all eight
        /// directions, every one the blend gate can bleed from.
        /// </summary>
        internal static void CaseStackingCombines(Map map)
        {
            if (Single == null) { Skip("mask.stacking", "def missing"); return; }

            Clear();
            IntVec3 c = Origin(map);
            PlaceFourEdges(map, c);

            int mask = Patch_SidedFadeBlock.EdgeMaskAt(c, map);

            Check(mask == 0xFF, "mask.stacking.fourIsEight",
                $"four stacked got {Show(mask)} ({Bits(mask)}), expected all 8");
            Clear();
        }

        /// <summary>A single-edge marker on each side of the cell.</summary>
        internal static void PlaceFourEdges(Map map, IntVec3 c)
        {
            Place(map, c, Single, Rot4.North);
            Place(map, c, Single, Rot4.East);
            Place(map, c, Single, Rot4.South);
            Place(map, c, Single, Rot4.West);
        }

        // ---- the render mask: pins that hold nothing back ----------------

        internal static TerrainDef Terrain(string defName) =>
            DefDatabase<TerrainDef>.GetNamedSilentFail(defName);

        /// <summary>
        /// Two grounds meeting under the end of a floor's hardened edge, the
        /// junction the preview card caught. Steel tile on the two rows above,
        /// lichen-covered soil below on the west and sand on the east, and the
        /// two floor tiles over the junction hardened on their south edges.
        ///
        /// Sand outranks lichen, so sand fades onto the lichen tile from the
        /// east, across an edge nobody hardened. The layout seals that tile's
        /// top-east corner (the floor edge above flanks it), and the render mask
        /// must not pin it: lit, the sand fade meets the sand tile beside it;
        /// pinned, the two grounds met in a hard line under the floor. The floor
        /// tile above keeps its corner pin, because sand reaches that corner only
        /// by the diagonal, and that spike is what its hardened edge is for.
        /// </summary>
        internal static void CaseJunctionCornerLit(Map map)
        {
            if (!PaintJunction(map, "render.junction", out IntVec3 o)) return;
            Place(map, new IntVec3(o.x, 0, o.z + 1), Single, Rot4.South);
            Place(map, new IntVec3(o.x + 1, 0, o.z + 1), Single, Rot4.South);

            IntVec3 ground = o;
            IntVec3 floorAbove = new IntVec3(o.x, 0, o.z + 1);
            const int ne = 1 << 5, se = 1 << 7;

            int layout = Patch_SidedFadeBlock.EdgeMaskAt(ground, map);
            Check((layout & ne) != 0, "render.junction.layoutSeals",
                $"the layout mask should still seal the lichen tile's NE (the premise); got {Show(layout)}");

            int render = Patch_SidedFadeBlock.RenderMaskAt(ground, map);
            Check((render & ne) == 0, "render.junction.groundCornerLit",
                $"the lichen tile's NE is still pinned: render mask {Show(render)}");

            int above = Patch_SidedFadeBlock.RenderMaskAt(floorAbove, map);
            Check((above & se) != 0, "render.junction.floorCornerKept",
                $"the floor tile's SE lost its pin: render mask {Show(above)}");
            Clear();
        }

        /// <summary>
        /// The same junction hardened by the east floor tile alone, as a painted
        /// tile hardens it: its west and south edges, nothing on the west floor
        /// tile. The lichen tile's corner must still be lit. And the west floor
        /// tile keeps its corner pin even though lichen now fades onto it from
        /// below, through an open side, and reaches that corner too: sand sits
        /// on the diagonal, and dropping the pin would let its spike onto the
        /// floor. The first version of the render mask lost exactly this pin,
        /// because it read the diagonal as substituted and so never saw the sand.
        /// </summary>
        internal static void CaseJunctionZoneOnly(Map map)
        {
            if (!PaintJunction(map, "render.junctionZone", out IntVec3 o)) return;
            IntVec3 eastFloor = new IntVec3(o.x + 1, 0, o.z + 1);
            Place(map, eastFloor, Single, Rot4.West);
            Place(map, eastFloor, Single, Rot4.South);

            IntVec3 ground = o;
            IntVec3 westFloor = new IntVec3(o.x, 0, o.z + 1);
            const int ne = 1 << 5, se = 1 << 7;

            int render = Patch_SidedFadeBlock.RenderMaskAt(ground, map);
            Check((render & ne) == 0, "render.junctionZone.groundCornerLit",
                $"the lichen tile's NE is still pinned: render mask {Show(render)}");

            int beside = Patch_SidedFadeBlock.RenderMaskAt(westFloor, map);
            Check((beside & se) != 0, "render.junctionZone.floorCornerKept",
                $"the west floor tile's SE lost its pin, so sand's diagonal spike reaches it: render mask {Show(beside)}");
            Clear();
        }

        /// <summary>
        /// The junction's terrain: steel tile on the two rows above, lichen-covered
        /// soil below on the west and sand on the east, meeting under the seam
        /// between the two floor tiles at <paramref name="o"/> + north and its east
        /// neighbour. False, with a skip recorded, when the case cannot be built.
        /// </summary>
        internal static bool PaintJunction(Map map, string name, out IntVec3 o)
        {
            o = Origin(map);
            TerrainDef floor = Terrain("MetalTile"), lichen = Terrain("MossyTerrain"), sand = Terrain("Sand");
            if (Single == null || floor == null || lichen == null || sand == null)
            {
                Skip(name, "a def is missing");
                return false;
            }
            if (sand.renderPrecedence <= lichen.renderPrecedence)
            {
                Skip(name, "sand no longer outranks lichen, so the fade runs the other way");
                return false;
            }

            Clear();
            CellRect block = CellRect.FromLimits(o.x - 2, o.z - 2, o.x + 3, o.z + 2);
            if (Covered(map, block)) { Skip(name, "an edifice covers the fixture"); return false; }

            Paint(map, CellRect.FromLimits(o.x - 2, o.z + 1, o.x + 3, o.z + 2), floor);
            Paint(map, CellRect.FromLimits(o.x - 2, o.z - 2, o.x, o.z), lichen);
            Paint(map, CellRect.FromLimits(o.x + 1, o.z - 2, o.x + 3, o.z), sand);
            return true;
        }

        /// <summary>
        /// The corner pinning was built for keeps its pin. A steel tile in
        /// soil, hardened on its south edge: soil also lies to its east, through
        /// an open side, and would re-light the south edge's end. The terrain
        /// the hardened edge holds back is the same terrain, so the pin stays.
        /// </summary>
        internal static void CaseRelightPinKept(Map map)
        {
            TerrainDef floor = Terrain("MetalTile"), soil = Terrain("Soil");
            if (Single == null || floor == null || soil == null) { Skip("render.relight", "a def is missing"); return; }

            Clear();
            IntVec3 o = Origin(map);
            CellRect block = CellRect.FromLimits(o.x - 2, o.z - 2, o.x + 2, o.z + 2);
            if (Covered(map, block)) { Skip("render.relight", "an edifice covers the fixture"); return; }

            Paint(map, block, soil);
            Paint(map, CellRect.SingleCell(o), floor);
            Place(map, o, Single, Rot4.South);

            int render = Patch_SidedFadeBlock.RenderMaskAt(o, map);
            int expected = (1 << 0) | (1 << 1) | (1 << 7);   // S, SW, SE
            Check(render == expected, "render.relight.kept",
                $"got {Show(render)}, expected {Show(expected)}");
            Clear();
        }

        /// <summary>
        /// The tile beyond a lone hardened edge's end fades along its open edge
        /// all the way (v1.1.1). Two steel tiles in soil, only the east one
        /// hardened on its south edge: soil fades onto the west tile from below,
        /// across an edge nobody hardened. Until v1.1.1 the west tile's shared
        /// corner kept its pin, so that fade tapered to nothing at the corner and
        /// the first half of the open edge read as hard; the corner is a lone
        /// end, and lit now. The premise is checked first, so a change to corner
        /// sealing reads as that and not as the corner's light.
        /// </summary>
        internal static void CaseTaperCornerLit(Map map)
        {
            TerrainDef floor = Terrain("MetalTile"), soil = Terrain("Soil");
            if (Single == null || floor == null || soil == null) { Skip("render.taper", "a def is missing"); return; }

            Clear();
            IntVec3 o = Origin(map);
            CellRect block = CellRect.FromLimits(o.x - 3, o.z - 2, o.x + 2, o.z + 2);
            if (Covered(map, block)) { Skip("render.taper", "an edifice covers the fixture"); return; }

            Paint(map, block, soil);
            Paint(map, CellRect.FromLimits(o.x - 1, o.z, o.x, o.z), floor);
            Place(map, o, Single, Rot4.South);

            IntVec3 west = o + IntVec3.West;
            int layout = Patch_SidedFadeBlock.EdgeMaskAt(west, map);
            Check(layout == (1 << 7), "render.taper.layoutSeals",
                $"the layout should seal only the west tile's SE (the premise); got {Show(layout)}");
            int render = Patch_SidedFadeBlock.RenderMaskAt(west, map);
            Check(render == 0, "render.taper.lit",
                $"west tile got {Show(render)}, expected nothing pinned");
            Clear();
        }

        /// <summary>
        /// A lone end still keeps its pin against a diagonal spike. The taper's
        /// layout with sand under the hardened tile: sand reaches the west tile's
        /// shared corner only by the diagonal, round the end of the edge that
        /// holds it back, and the soil fading in from below must not take the pin
        /// with it.
        /// </summary>
        internal static void CaseLoneEndSpikeKept(Map map)
        {
            TerrainDef floor = Terrain("MetalTile"), soil = Terrain("Soil"), sand = Terrain("Sand");
            if (Single == null || floor == null || soil == null || sand == null)
            {
                Skip("render.loneEndSpike", "a def is missing");
                return;
            }
            if (sand.renderPrecedence < floor.renderPrecedence || sand.edgeType == TerrainDef.TerrainEdgeType.Hard)
            {
                Skip("render.loneEndSpike", "sand no longer fades onto steel tile, so there is no spike to hold back");
                return;
            }

            Clear();
            IntVec3 o = Origin(map);
            CellRect block = CellRect.FromLimits(o.x - 3, o.z - 2, o.x + 2, o.z + 2);
            if (Covered(map, block)) { Skip("render.loneEndSpike", "an edifice covers the fixture"); return; }

            Paint(map, block, soil);
            Paint(map, CellRect.FromLimits(o.x - 1, o.z, o.x, o.z), floor);
            Paint(map, CellRect.SingleCell(o + IntVec3.South), sand);
            Place(map, o, Single, Rot4.South);

            int render = Patch_SidedFadeBlock.RenderMaskAt(o + IntVec3.West, map);
            Check(render == (1 << 7), "render.loneEndSpike.kept",
                $"west tile got {Show(render)}, expected only SE: sand's spike reaches the corner");
            Clear();
        }

        /// <summary>
        /// The play test's layout, which found the taper: rich soil beside soil
        /// along a straight boundary, and one straight floor border on the soil
        /// tile, hugging the boundary, that stops a tile short. Soil outranks
        /// rich soil, so soil fades onto the rich tile beyond the border's end,
        /// across the edge the border stops short of; that tile's corner at the
        /// border's end is a lone end, and lit.
        /// </summary>
        internal static void CaseBorderEndLit(Map map)
        {
            TerrainDef soil = Terrain("Soil"), rich = Terrain("SoilRich");
            ThingDef border = DefDatabase<ThingDef>.GetNamedSilentFail("NE_FloorBorder");
            if (border == null || soil == null || rich == null) { Skip("render.borderEnd", "a def is missing"); return; }
            if (soil.renderPrecedence <= rich.renderPrecedence)
            {
                Skip("render.borderEnd", "soil no longer outranks rich soil, so the fade runs the other way");
                return;
            }

            Clear();
            IntVec3 o = Origin(map);
            CellRect block = CellRect.FromLimits(o.x - 2, o.z - 2, o.x + 2, o.z + 3);
            if (Covered(map, block)) { Skip("render.borderEnd", "an edifice covers the fixture"); return; }

            Paint(map, block, soil);
            Paint(map, CellRect.FromLimits(o.x - 2, o.z - 2, o.x - 1, o.z + 3), rich);
            Place(map, o, border, Rot4.West);

            IntVec3 beyond = o + IntVec3.North + IntVec3.West;
            int layout = Patch_SidedFadeBlock.EdgeMaskAt(beyond, map);
            Check(layout == (1 << 7), "render.borderEnd.layoutSeals",
                $"the layout should seal only the rich tile's SE, at the border's end (the premise); got {Show(layout)}");
            int render = Patch_SidedFadeBlock.RenderMaskAt(beyond, map);
            Check(render == 0, "render.borderEnd.lit",
                $"the rich tile beyond the border's end got {Show(render)}, expected nothing pinned");
            Clear();
        }

        // ---- the visible trims ------------------------------------------

        /// <summary>
        /// The trim families: each one's defName stem and how many variants its
        /// strip holds. Every family has the seven shapes in
        /// <see cref="FamilyShapes"/>, named by <see cref="TrimSuffixes"/>.
        /// </summary>
        internal static readonly (string stem, int variants)[] TrimFamilies =
        {
            ("NE_FloorBorder", 1), ("NE_InlayBorder", 1), ("NE_VinesBorder", 4), ("NE_PebblesBorder", 4),
        };

        internal static readonly string[] TrimSuffixes =
        {
            "", "Corner", "InsideCorner", "Double", "EndCap", "Frame", "Diagonal",
        };

        /// <summary>
        /// Each suffix's shape. Written out rather than read from the defs, so a
        /// def naming the wrong shape fails here.
        /// </summary>
        internal static readonly TrimPiece.Kind[] FamilyShapes =
        {
            TrimPiece.Kind.Straight, TrimPiece.Kind.Corner, TrimPiece.Kind.InsideCorner,
            TrimPiece.Kind.Runner, TrimPiece.Kind.EndCap, TrimPiece.Kind.Frame, TrimPiece.Kind.Diagonal,
        };

        /// <summary>Each shape's dropdown group, in <see cref="FamilyShapes"/>' order.</summary>
        internal static readonly string[] ShapeGroups =
        {
            "NE_TrimStraights", "NE_TrimCorners", "NE_TrimInsideCorners", "NE_TrimRunners",
            "NE_TrimEndCaps", "NE_TrimFrames", "NE_TrimDiagonals",
        };

        /// <summary>The six shapes of the golden file's first block, which predates the rest.</summary>
        internal static readonly TrimPiece.Kind[] TrimShapes = FamilyShapes.Take(6).ToArray();

        /// <summary>Every trim: its defName, its shape and its family's variants.</summary>
        internal static IEnumerable<(string name, TrimPiece.Kind shape, int variants)> AllTrims()
        {
            foreach ((string stem, int variants) in TrimFamilies)
            {
                for (int s = 0; s < TrimSuffixes.Length; s++)
                {
                    yield return (stem + TrimSuffixes[s], FamilyShapes[s], variants);
                }
            }
        }

        /// <summary>The families whose strip carries a paint overlay: the vine, for its leaves.</summary>
        internal static readonly string[] OverlayFamilies = { "NE_VinesBorder" };

        /// <summary>The floor border's seven, the family the cost case measures.</summary>
        internal static readonly string[] TrimNames =
            TrimSuffixes.Select(s => TrimFamilies[0].stem + s).ToArray();

        /// <summary>
        /// Every trim loads stuffable and paintable, on a three-band strip with
        /// its family's variants and, for the vine alone, a paint overlay, and
        /// every one but the inside corner carries the extension. A trim missing it is decoration and nothing more, and
        /// nothing in game would say so; the inside corner carrying it would
        /// harden a whole tile it only touches. The diagonal carries it with no
        /// edges listed, which hardens its whole tile. Each names its shape, and
        /// has a menu icon of its own: without one the engine takes the
        /// graphic's texture, and the button would show the strip.
        /// </summary>
        internal static void CaseTrimDefs()
        {
            foreach ((string name, TrimPiece.Kind shape, int variants) in AllTrims())
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                bool wantExtension = shape != TrimPiece.Kind.InsideCorner;
                BlocksTerrainFade fade = def?.GetModExtension<BlocksTerrainFade>();
                bool hasExtension = fade != null;
                bool wholeTile = fade != null && (fade.edges == null || fade.edges.Count == 0);
                bool wantWholeTile = shape == TrimPiece.Kind.Diagonal;
                TrimPiece piece = def?.GetModExtension<TrimPiece>();
                bool wantOverlay = OverlayFamilies.Any(family => name.StartsWith(family));
                Texture strip = def?.graphic?.MatSingle?.mainTexture;
                bool iconOwn = def?.uiIcon != null && def.uiIcon != BaseContent.BadTex && def.uiIcon != strip;
                bool ok = def != null && def.MadeFromStuff
                    && def.building != null && def.building.paintable
                    && hasExtension == wantExtension && (!hasExtension || wholeTile == wantWholeTile)
                    && piece != null && piece.shape == shape && piece.bands == 3 && piece.variants == variants
                    && piece.paintOverlay == wantOverlay && iconOwn;
                Check(ok, "trims.defs." + name,
                    def == null ? "missing"
                        : $"stuff {def.MadeFromStuff}, paintable {def.building?.paintable}, "
                          + $"extension {hasExtension} (want {wantExtension}), whole tile {wholeTile} "
                          + $"(want {wantWholeTile}), shape {piece?.shape.ToString() ?? "none"} (want {shape}), "
                          + $"bands {piece?.bands} (want 3), variants {piece?.variants} (want {variants}), "
                          + $"paint overlay {piece?.paintOverlay} (want {wantOverlay}), "
                          + $"icon {def.uiIcon?.name ?? "none"}{(iconOwn ? "" : ", not its own")}");
            }
        }

        /// <summary>
        /// Each trim hardens exactly the edges its art covers, at every
        /// rotation. The expected edges are written out rather than computed
        /// from the production formula, so a wrong offset in either the defs
        /// or the formula fails here instead of agreeing with itself. They are
        /// the edges each shape draws a band along: facing north, the border's
        /// is N; the corner's N and E; the runner's N and S; the end cap's W,
        /// N and E; the frame's all four whatever its rotation; the inside
        /// corner's none, since it covers a corner; the diagonal's all four,
        /// its whole tile, whatever its rotation.
        ///
        /// Adjacency indices: S=0, W=2, N=4, E=6. Rows follow FamilyShapes.
        /// </summary>
        internal static void CaseTrimMasks(Map map)
        {
            Rot4[] rots = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
            int[][] border = { new[] { 4 }, new[] { 6 }, new[] { 0 }, new[] { 2 } };
            int[][] corner = { new[] { 4, 6 }, new[] { 6, 0 }, new[] { 0, 2 }, new[] { 2, 4 } };
            int[][] inside = { new int[0], new int[0], new int[0], new int[0] };
            int[][] runner = { new[] { 4, 0 }, new[] { 6, 2 }, new[] { 0, 4 }, new[] { 2, 6 } };
            int[][] endCap = { new[] { 2, 4, 6 }, new[] { 4, 6, 0 }, new[] { 6, 0, 2 }, new[] { 0, 2, 4 } };
            int[] all = { 0, 2, 4, 6 };
            int[][] frame = { all, all, all, all };
            int[][] diagonal = { all, all, all, all };
            int[][][] expected = { border, corner, inside, runner, endCap, frame, diagonal };

            foreach ((string name, TrimPiece.Kind shape, _) in AllTrims())
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (def == null) { Skip("trims.masks." + name, "def missing"); continue; }
                int t = Array.IndexOf(FamilyShapes, shape);

                for (int r = 0; r < rots.Length; r++)
                {
                    Clear();
                    IntVec3 c = Origin(map);
                    Place(map, c, def, rots[r]);

                    int want = 0;
                    foreach (int dir in expected[t][r]) want |= 1 << dir;
                    int got = Patch_SidedFadeBlock.MarkerMask(c, map);

                    Check(got == want, $"trims.masks.{name}.{rots[r].ToStringHuman()}",
                        $"got {Show(got)}, expected {Show(want)}");
                }
            }
            Clear();
        }

        /// <summary>
        /// A runner turning a corner stacks a corner (its outer rails) and an
        /// inside corner (its inner joint) on one tile, so both must be
        /// placeable there and both must stand. The engine allows it at every
        /// stage because neither is an edifice: blueprint placement refuses
        /// only the same def at the same rotation, and spawning wipes only an
        /// edifice with an edifice. This pins that, and that the inside corner
        /// adds no edge to the tile it shares.
        ///
        /// The cell is found by asking whether a corner alone could be placed
        /// there, so the placement check below cannot fail on the terrain.
        /// </summary>
        internal static void CaseTrimStack(Map map)
        {
            ThingDef corner = DefDatabase<ThingDef>.GetNamedSilentFail("NE_FloorBorderCorner");
            ThingDef inside = DefDatabase<ThingDef>.GetNamedSilentFail("NE_FloorBorderInsideCorner");
            if (corner == null || inside == null)
            {
                Skip("trims.stack", "a def is missing");
                return;
            }

            Clear();
            IntVec3 cell = IntVec3.Invalid;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(Origin(map), 12f, true))
            {
                if (c.InBounds(map) && c.GetThingList(map).Count == 0
                    && GenConstruct.CanPlaceBlueprintAt(corner, c, Rot4.West, map,
                           stuffDef: GenStuff.DefaultStuffFor(corner)).Accepted)
                {
                    cell = c;
                    break;
                }
            }
            if (!cell.IsValid)
            {
                Skip("trims.stack", "no buildable cell near the map centre");
                return;
            }

            Place(map, cell, corner, Rot4.West);
            AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(inside, cell, Rot4.East, map,
                stuffDef: GenStuff.DefaultStuffFor(inside));
            Check(report.Accepted, "trims.stack.placeInsideOverCorner",
                "refused: " + report.Reason);

            Place(map, cell, inside, Rot4.East);
            List<Thing> here = cell.GetThingList(map);
            bool both = here.Any(t => t.def == corner && t.Spawned)
                && here.Any(t => t.def == inside && t.Spawned);
            Check(both, "trims.stack.bothStand",
                "on the cell: " + string.Join(", ", here.Select(t => t.def.defName)));

            int got = Patch_SidedFadeBlock.MarkerMask(cell, map);
            int want = (1 << 2) | (1 << 4);    // the corner facing west: W and N
            Check(got == want, "trims.stack.insideAddsNoEdge",
                $"got {Show(got)}, expected {Show(want)}");
            Clear();
        }

        /// <summary>
        /// Every trim draws from the strip, at exactly one tile, repeating along
        /// its band and clamped across it, and outside the static atlas with no
        /// patch keeping it there. Each is part of what keeps a joint clean, and
        /// losing any one is silent in game:
        ///
        ///   - in the static atlas, a strip could not repeat, and a joint would
        ///     borrow a hairline from whatever texture the packer put beside it;
        ///   - clamped along the band, a run would smear its last texel column
        ///     instead of carrying the strip on;
        ///   - wrapping across, a band's outer row would blend with the other
        ///     half's;
        ///   - drawn larger than its tile, a piece would overlap its neighbour.
        ///
        /// An engine that keeps one wrap mode for every axis cannot repeat a
        /// texture one way and clamp it the other. RimWorld 1.7's UnityStubs is
        /// one: it keeps the axis set last, so the strip is clamped both ways
        /// there. A texture of the case's own, set the way the strip is, finds
        /// out; on such an engine each trim is checked for the clamp across
        /// alone, and trims.render.wrapAlong reports the repeat along as
        /// skipped. The shipped strips draw the same clamped, because the floor
        /// border's and the inlay's are uniform along the band and the vine's
        /// and the pebbles' variants keep their coordinates inside the strip. A
        /// strip that needed the repeat would draw wrong there, and nothing here
        /// would say so.
        ///
        /// Nothing of ours may patch the atlas door any more: the strip never
        /// knocks, and a patch there would run for every texture at startup.
        /// </summary>
        internal static void CaseTrimRender()
        {
            bool atlasDoor = HarmonyLib.Harmony.GetAllPatchedMethods()
                .Where(m => m.Name == nameof(GlobalTextureAtlasManager.TryInsertStatic)
                    && m.DeclaringType == typeof(GlobalTextureAtlasManager))
                .Any(m => HarmonyLib.Harmony.GetPatchInfo(m)?.Owners.Contains(HarmonyInit.Id) == true);
            Check(!atlasDoor, "trims.render.atlasUnpatched",
                "a Neat Edges patch sits on GlobalTextureAtlasManager.TryInsertStatic again");

            var probe = new Texture2D(1, 1);
            probe.wrapModeU = TextureWrapMode.Repeat;
            probe.wrapModeV = TextureWrapMode.Clamp;
            TextureWrapMode probeU = probe.wrapModeU, probeV = probe.wrapModeV;
            UnityEngine.Object.Destroy(probe);
            bool perAxis = probeU == TextureWrapMode.Repeat && probeV == TextureWrapMode.Clamp;
            if (!perAxis)
            {
                Skip("trims.render.wrapAlong", "this engine keeps one wrap mode for every axis, as RimWorld 1.7's "
                    + "UnityStubs does: a texture set to Repeat along and then Clamp across reads back " + probeU
                    + " and " + probeV + ", so no strip can repeat along its band, and each trim below is checked "
                    + "for the clamp across alone");
            }

            foreach ((string name, _, _) in AllTrims())
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (def?.graphic == null) { Skip("trims.render." + name, "def or graphic missing"); continue; }

                var problems = new List<string>();
                if (!(def.graphic is Graphic_StripTrim))
                {
                    problems.Add("draws with " + def.graphic.GetType().Name);
                }
                if (def.graphicData.drawSize != Vector2.one)
                {
                    problems.Add("drawSize " + def.graphicData.drawSize);
                }
                if (!(def.graphic.MatSingle?.mainTexture is Texture2D texture))
                {
                    problems.Add("no texture");
                }
                else
                {
                    bool wrapsWrong = perAxis
                        ? texture.wrapModeU != TextureWrapMode.Repeat || texture.wrapModeV != TextureWrapMode.Clamp
                        : texture.wrapModeV != TextureWrapMode.Clamp;
                    if (wrapsWrong)
                    {
                        problems.Add("wraps " + texture.wrapModeU + " along and " + texture.wrapModeV + " across");
                    }
                    if (GlobalTextureAtlasManager.TryGetStaticTile(def.category.ToAtlasGroup(), texture, out _,
                            ignoreFoundInOtherAtlas: true))
                    {
                        problems.Add("in the static atlas");
                    }
                }
                Check(problems.Count == 0, "trims.render." + name, string.Join("; ", problems));
            }
        }

        /// <summary>
        /// The scenes whose diagonals the golden file pins, each diagonal's ends
        /// read from its neighbours: devtools/strip_trim.py's SCENES, item for
        /// item and in its order.
        /// </summary>
        internal static readonly (string name, (TrimPiece.Kind shape, int rot, int x, int z)[] pieces)[] GeometryScenes =
        {
            ("lone", new[] { (TrimPiece.Kind.Diagonal, 0, 10, 10) }),
            ("lone-east", new[] { (TrimPiece.Kind.Diagonal, 1, 10, 10) }),
            ("lone-south", new[] { (TrimPiece.Kind.Diagonal, 2, 10, 10) }),
            ("lone-west", new[] { (TrimPiece.Kind.Diagonal, 3, 10, 10) }),
            ("run", new[] { (TrimPiece.Kind.Diagonal, 0, 10, 10), (TrimPiece.Kind.Diagonal, 0, 11, 11) }),
            ("octagon-inside", new[]
            {
                (TrimPiece.Kind.Straight, 3, 10, 9), (TrimPiece.Kind.Diagonal, 0, 10, 10),
                (TrimPiece.Kind.Straight, 0, 11, 10),
            }),
            ("octagon-outside", new[]
            {
                (TrimPiece.Kind.Straight, 0, 9, 9), (TrimPiece.Kind.Diagonal, 0, 10, 10),
                (TrimPiece.Kind.Straight, 3, 11, 11),
            }),
            ("tip-inside", new[] { (TrimPiece.Kind.Diagonal, 0, 10, 10), (TrimPiece.Kind.Diagonal, 1, 11, 10) }),
            ("tip-outside", new[] { (TrimPiece.Kind.Diagonal, 2, 10, 11), (TrimPiece.Kind.Diagonal, 3, 11, 11) }),
            ("corner-piece", new[] { (TrimPiece.Kind.Corner, 0, 11, 10), (TrimPiece.Kind.Diagonal, 0, 10, 10) }),
            ("corner-mitred", new[] { (TrimPiece.Kind.Corner, 3, 11, 10), (TrimPiece.Kind.Diagonal, 0, 10, 10) }),
            ("sharp", new[] { (TrimPiece.Kind.Diagonal, 0, 10, 10), (TrimPiece.Kind.Straight, 2, 10, 11) }),
        };

        /// <summary>
        /// The geometry the game draws equals the geometry check_trims.py
        /// renders from, vertex for vertex. Both are pinned to one golden file,
        /// devtools/strip_trim_geometry.txt, as x z u v times 4096: every
        /// straight-family shape at every rotation at cell (3, 5) on a two-band
        /// strip that repeats once a tile; the straight on a three-band strip
        /// and on one of four variants; every diagonal of every scene in
        /// <see cref="GeometryScenes"/>, its ends read from its neighbours, on
        /// both; and, on a four-variant strip with a paint overlay, the
        /// straight and the four lone diagonals sampling each layer. Values
        /// are compared within one 1/4096th, since the diagonals'
        /// are not whole numbers and float and double round them apart.
        /// check_trims.py fails if the Python model drifts from the file; this
        /// fails if the C# does. A deliberate change regenerates the file
        /// (strip_trim.py --write-golden) and changes both.
        /// </summary>
        internal static void CaseStripGeometry()
        {
            ModContentPack pack = LoadedModManager.RunningModsListForReading
                .FirstOrDefault(m => m.assemblies.loadedAssemblies.Contains(typeof(Harness).Assembly));
            string path = pack == null ? null : Path.Combine(pack.RootDir, "devtools", "strip_trim_geometry.txt");
            if (path == null || !File.Exists(path))
            {
                Skip("trims.geometry", "no devtools/strip_trim_geometry.txt beside the mod");
                return;
            }
            string[] golden = File.ReadAllLines(path).Where(line => line.Length > 0).ToArray();
            List<string> mine = GeometryLines();
            int first = Enumerable.Range(0, Math.Max(mine.Count, golden.Length))
                .FirstOrDefault(i => i >= mine.Count || i >= golden.Length || !GoldenLineMatches(mine[i], golden[i]));
            bool same = mine.Count == golden.Length
                && Enumerable.Range(0, mine.Count).All(i => GoldenLineMatches(mine[i], golden[i]));
            Check(same, "trims.geometry",
                same ? $"{mine.Count} lines" : $"line {first + 1}: game \"{(first < mine.Count ? mine[first] : "(none)")}\", "
                    + $"file \"{(first < golden.Length ? golden[first] : "(none)")}\"");
        }

        internal static List<string> GeometryLines()
        {
            string[] rotations = { "North", "East", "South", "West" };
            var cell = new IntVec3(3, 0, 5);
            var mine = new List<string>();
            foreach (TrimPiece.Kind shape in TrimShapes)
            {
                for (int r = 0; r < 4; r++)
                {
                    mine.Add(shape + " " + rotations[r] + " "
                        + GeometryValues(shape, r, cell, new StripLayout(1f, 2, 1), default));
                }
            }
            var layouts = new[] { ("Bands3", new StripLayout(1f, 3, 1)), ("Variants4", new StripLayout(4f, 3, 4)) };
            foreach ((string label, StripLayout layout) in layouts)
            {
                for (int r = 0; r < 4; r++)
                {
                    mine.Add(label + " Straight " + rotations[r] + " "
                        + GeometryValues(TrimPiece.Kind.Straight, r, cell, layout, default));
                }
            }
            foreach ((string label, StripLayout layout) in layouts)
            {
                foreach ((string name, var pieces) in GeometryScenes)
                {
                    IEnumerable<(TrimPiece.Kind, int)> PiecesAt(IntVec3 c) =>
                        pieces.Where(p => p.x == c.x && p.z == c.z).Select(p => (p.shape, p.rot));
                    for (int i = 0; i < pieces.Length; i++)
                    {
                        if (pieces[i].shape != TrimPiece.Kind.Diagonal) continue;
                        var at = new IntVec3(pieces[i].x, 0, pieces[i].z);
                        DiagonalEnds ends = StripTrimGeometry.DiagonalEndsAt(at, pieces[i].rot, PiecesAt);
                        mine.Add($"{label} Scene {name} {i} {EndName(ends.a)} {EndName(ends.b)} "
                            + GeometryValues(TrimPiece.Kind.Diagonal, pieces[i].rot, at, layout, ends));
                    }
                }
            }
            var overlaid = new StripLayout(4f, 3, 4, 2);
            foreach (StripLayout layout in new[] { overlaid.Bands, overlaid.Overlay })
            {
                string label = "Overlay" + layout.layer;
                for (int r = 0; r < 4; r++)
                {
                    mine.Add(label + " Straight " + rotations[r] + " "
                        + GeometryValues(TrimPiece.Kind.Straight, r, cell, layout, default));
                }
                foreach ((string name, var pieces) in GeometryScenes.Take(4))
                {
                    var at = new IntVec3(pieces[0].x, 0, pieces[0].z);
                    mine.Add($"{label} Scene {name} 0 square square "
                        + GeometryValues(pieces[0].shape, pieces[0].rot, at, layout, default));
                }
            }
            return mine;
        }

        internal static string EndName(DiagonalEnd end) =>
            !end.mitre ? "square" : end.inner ? "mitre-in" : "mitre";

        // ---- one button per shape --------------------------------------------

        /// <summary>
        /// Every shape is one button on the Floors tab, holding that shape in
        /// every style in uiOrder, and no trim has a button of its own. The tab
        /// draws the buttons in the shapes' order. Each is a dropdown, so Copy
        /// reaches each trim's own designator (trims.strip checks that per def);
        /// its right-click menu lists the four styles; putting a style on it
        /// changes its label; and every trim names its shape's dropdown group,
        /// whose includeEyeDropperTool keeps Better Architect Menu from
        /// unrolling the button.
        /// </summary>
        internal static void CaseTrimButtons()
        {
            DesignationCategoryDef floors = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Floors");
            List<Designator> buttons = floors?.AllResolvedDesignators ?? new List<Designator>();
            List<string> ownButtons = buttons.OfType<Designator_Build>()
                .Where(d => d.PlacingDef is ThingDef td && td.GetModExtension<TrimPiece>() != null)
                .Select(d => d.PlacingDef.defName).ToList();
            Check(ownButtons.Count == 0, "trims.buttons.noOwnButtons",
                "trims with a button of their own: " + string.Join(", ", ownButtons));

            // The gizmo grid sorts by Order, stably, so this is the tab's order.
            List<Designator_TrimShape> shapeButtons = buttons.OfType<Designator_TrimShape>().ToList();
            List<TrimPiece.Kind> drawn = shapeButtons.OrderBy(b => b.Order).Select(b => b.shape).ToList();
            Check(drawn.SequenceEqual(FamilyShapes), "trims.buttons.order",
                "the Floors tab's trim buttons, in the order it draws them: " + string.Join(", ", drawn));

            // The tab keeps list order on a tie, and the buttons are listed
            // ahead of every button made from defs, so each must sort after the
            // buttons sharing its face style's order, as v1.1.1's did: vanilla's
            // stone tiles share the floor border's 2060, and the floor border
            // jumping ahead of them split the trims' row in two.
            List<Designator> tab = buttons.ToList();
            tab.SortStable((a, b) => a.Order.CompareTo(b.Order));
            int ties = 0;
            var ahead = new List<string>();
            foreach (Designator_TrimShape button in shapeButtons)
            {
                float face = button.current?.Order ?? button.Order;
                List<Designator> sharing = tab.Where(d => !(d is Designator_TrimShape) && d.Order == face).ToList();
                ties += sharing.Count;
                int at = tab.IndexOf(button);
                ahead.AddRange(sharing.Where(d => tab.IndexOf(d) > at).Select(d => $"{button.shape} before {d.Label}"));
            }
            Note("trims.buttons.ties", $"{ties} other buttons share a trim button's face order");
            Check(ahead.Count == 0, "trims.buttons.ties",
                "trim buttons drawn ahead of a button with the same order: " + string.Join(", ", ahead));

            for (int s = 0; s < FamilyShapes.Length; s++)
            {
                TrimPiece.Kind shape = FamilyShapes[s];
                List<string> want = TrimFamilies.Select(f => f.stem + TrimSuffixes[s]).ToList();
                Designator_TrimShape button = shapeButtons.FirstOrDefault(b => b.shape == shape);
                if (button == null)
                {
                    Check(false, "trims.buttons." + shape, "no button on the Floors tab builds the " + shape);
                    continue;
                }
                List<string> got = button.Elements.OfType<Designator_Build>().Select(e => e.PlacingDef.defName).ToList();
                var problems = new List<string>();
                if (!got.SequenceEqual(want)) problems.Add("holds " + string.Join(", ", got));
                int options = button.RightClickFloatMenuOptions.Count();
                if (options != want.Count) problems.Add($"{options} right-click options, want {want.Count}");
                Designator first = button.current;
                Designator last = button.Elements[button.Elements.Count - 1];
                button.Show(last);
                if (button.Label != last.Label) problems.Add($"shows \"{button.Label}\" with {got[got.Count - 1]} on it");
                button.Show(first);
                foreach (string name in want)
                {
                    DesignatorDropdownGroupDef group = DefDatabase<ThingDef>.GetNamedSilentFail(name)?.designatorDropdown;
                    if (group == null || !group.includeEyeDropperTool)
                        problems.Add(name + " names no group that keeps Better Architect Menu from unrolling its button");
                    else if (group.defName != ShapeGroups[s])
                        problems.Add($"{name} names {group.defName}, not its shape's {ShapeGroups[s]}");
                }
                Check(problems.Count == 0, "trims.buttons." + shape, string.Join("; ", problems));
            }
            Check(Designator_TrimShape.ArchitectFilter() != null, "trims.buttons.searchReadable",
                "the Architect tab's search filter could not be read, so search finds only the style on a button");
        }

        /// <summary>
        /// The buttons survive the Floors tab being rebuilt in a game, which
        /// another mod does by calling the category's ResolveReferences:
        /// Adaptive Storage - Global Settings does it on every new game and load
        /// once a player has moved a storage building to another tab. Vanilla
        /// rebuilds the list from the defs and the special designator classes
        /// alone, so in v1.1.0 and v1.1.1, when the buttons were added to the list
        /// from code at startup, every trim left the tab and its search. With no long
        /// event running the rebuild happens inside the call; the case checks
        /// that it did (no instance from before is left) before it reads the
        /// list, or a rebuild that never ran would pass.
        ///
        /// Runs after trims.buttons, and leaves the tab rebuilt.
        /// </summary>
        internal static void CaseTrimButtonsSurviveRebuild()
        {
            DesignationCategoryDef floors = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Floors");
            if (floors == null)
            {
                Check(false, "trims.buttons.rebuild", "no Floors tab");
                return;
            }
            List<Designator> before = floors.AllResolvedDesignators.ToList();
            Dictionary<TrimPiece.Kind, List<string>> held = before.OfType<Designator_TrimShape>()
                .ToDictionary(b => b.shape, b => b.Elements.OfType<Designator_Build>()
                    .Select(e => e.PlacingDef.defName).ToList());

            floors.ResolveReferences();

            List<Designator> after = floors.AllResolvedDesignators;
            int kept = after.Count(before.Contains);
            if (after.Count == 0 || kept > 0)
            {
                Check(false, "trims.buttons.rebuild",
                    $"the tab was not rebuilt ({after.Count} buttons, {kept} of them from before), so this proves nothing");
                return;
            }
            List<Designator_TrimShape> shapeButtons = after.OfType<Designator_TrimShape>().ToList();
            var problems = new List<string>();
            List<TrimPiece.Kind> drawn = shapeButtons.OrderBy(b => b.Order).Select(b => b.shape).ToList();
            if (!drawn.SequenceEqual(FamilyShapes))
                problems.Add("buttons after the rebuild, in drawing order: " + string.Join(", ", drawn));
            foreach (Designator_TrimShape button in shapeButtons)
            {
                List<string> got = button.Elements.OfType<Designator_Build>().Select(e => e.PlacingDef.defName).ToList();
                if (!held.TryGetValue(button.shape, out List<string> was) || !got.SequenceEqual(was))
                    problems.Add($"the {button.shape} button holds {string.Join(", ", got)}");
            }
            Check(problems.Count == 0, "trims.buttons.rebuild", string.Join("; ", problems));
        }

        // ---- the diagonal's joins on a real map -----------------------------

        /// <summary>
        /// Every trim, ours and any other mod's, carries CompTrimJoins from its
        /// def, and a spawned one has the comp: without it a diagonal keeps the
        /// ends it printed with when a neighbour across a section's edge comes or
        /// goes.
        /// </summary>
        internal static void CaseTrimJoinsGiven(Map map)
        {
            List<ThingDef> trims = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.GetModExtension<TrimPiece>() != null).ToList();
            List<string> without = trims.Where(d => !d.comps.Any(c => c.compClass == typeof(CompTrimJoins)))
                .Select(d => d.defName).ToList();
            Check(trims.Count > 0 && without.Count == 0, "trims.joins.given",
                $"{trims.Count} trim defs, {TrimJoinsInjection.Given} given the comp at startup; without it: "
                + string.Join(", ", without));

            ThingDef straight = DefDatabase<ThingDef>.GetNamedSilentFail("NE_FloorBorder");
            if (straight == null)
            {
                Skip("trims.joins.spawned", "no floor border");
                return;
            }
            Clear();
            Place(map, Origin(map), straight, Rot4.North);
            Thing thing = Spawned[Spawned.Count - 1];
            Check(thing.TryGetComp<CompTrimJoins>() != null, "trims.joins.spawned",
                "a spawned floor border has no CompTrimJoins");
            Clear();
        }

        internal static ThingDef SceneDef(TrimPiece.Kind shape)
        {
            switch (shape)
            {
                case TrimPiece.Kind.Straight: return DefDatabase<ThingDef>.GetNamedSilentFail("NE_FloorBorder");
                case TrimPiece.Kind.Corner: return DefDatabase<ThingDef>.GetNamedSilentFail("NE_FloorBorderCorner");
                case TrimPiece.Kind.Diagonal: return DefDatabase<ThingDef>.GetNamedSilentFail("NE_FloorBorderDiagonal");
                default: return null;
            }
        }

        /// <summary>
        /// The ends a diagonal reads off the map are the ends the golden file's
        /// scenes give it. Every scene is built from real things near the map's
        /// centre and each diagonal's ends are read through the thing grid, the
        /// way Print reads them; the same scene read from its own list must
        /// agree, end type, direction, wedge and all. The golden file pins what
        /// those ends draw; this pins that the map gives the same ends.
        /// </summary>
        internal static void CaseDiagonalEndsOnMap(Map map)
        {
            IntVec3 o = Origin(map);
            var shift = new IntVec3(o.x - 10, 0, o.z - 10);
            foreach ((string name, var pieces) in GeometryScenes)
            {
                if (pieces.Any(p => SceneDef(p.shape) == null))
                {
                    Skip("trims.diagonal.ends." + name, "a def is missing");
                    continue;
                }
                Clear();
                foreach (var p in pieces)
                {
                    Place(map, new IntVec3(p.x, 0, p.z) + shift, SceneDef(p.shape), new Rot4(p.rot));
                }
                IEnumerable<(TrimPiece.Kind, int)> Listed(IntVec3 c) =>
                    pieces.Where(p => p.x + shift.x == c.x && p.z + shift.z == c.z).Select(p => (p.shape, p.rot));
                var problems = new List<string>();
                foreach (var p in pieces.Where(p => p.shape == TrimPiece.Kind.Diagonal))
                {
                    IntVec3 at = new IntVec3(p.x, 0, p.z) + shift;
                    DiagonalEnds onMap = Graphic_StripTrim.EndsFor(TrimPiece.For(SceneDef(p.shape)), map, at,
                        new Rot4(p.rot));
                    DiagonalEnds listed = StripTrimGeometry.DiagonalEndsAt(at, p.rot, Listed);
                    if (onMap.a.key != listed.a.key || onMap.b.key != listed.b.key)
                    {
                        problems.Add($"diagonal at ({p.x},{p.z}) reads {EndName(onMap.a)}/{EndName(onMap.b)} "
                            + $"off the map, {EndName(listed.a)}/{EndName(listed.b)} from the scene");
                    }
                }
                Check(problems.Count == 0, "trims.diagonal.ends." + name, string.Join("; ", problems));
            }
            Clear();
        }

        /// <summary>
        /// A straight built beside a diagonal, across a map section's edge from
        /// it, has the diagonal's section print again, so the diagonal's end turns
        /// into the mitre at once; removing the straight does the same. Spawning
        /// alone redraws only the straight's own section, which is what this
        /// asks CompTrimJoins to cover. The diagonal stands in the last column
        /// of one section, facing north, so its head is its tile's north-east
        /// corner; a straight facing north on the first column of the next
        /// section begins there, the octagon's inside join.
        /// </summary>
        internal static void CaseDiagonalRefresh(Map map)
        {
            ThingDef diagonal = SceneDef(TrimPiece.Kind.Diagonal);
            ThingDef straight = SceneDef(TrimPiece.Kind.Straight);
            if (diagonal == null || straight == null)
            {
                Skip("trims.diagonal.refresh", "a def is missing");
                return;
            }
            Clear();
            Section home = map.mapDrawer.SectionAt(Origin(map));
            CellRect rect = home.CellRect;
            var a = new IntVec3(rect.maxX, 0, rect.minZ + 4);
            IntVec3 b = a + IntVec3.East;
            Section next = map.mapDrawer.SectionAt(b);
            if (next == home)
            {
                Skip("trims.diagonal.refresh", "both cells in one section");
                return;
            }
            Place(map, a, diagonal, Rot4.North);
            home.dirtyFlags = 0;
            next.dirtyFlags = 0;

            Place(map, b, straight, Rot4.North);
            Thing added = Spawned[Spawned.Count - 1];
            Check((home.dirtyFlags & MapMeshFlagDefOf.Things) != 0, "trims.diagonal.refresh.onBuild",
                "building the straight left the diagonal's section clean");
            DiagonalEnds ends = Graphic_StripTrim.EndsFor(TrimPiece.For(diagonal), map, a, Rot4.North);
            Check(ends.b.mitre && ends.b.inner && !ends.a.mitre, "trims.diagonal.refresh.mitres",
                $"ends {EndName(ends.a)} and {EndName(ends.b)}, want square and mitre-in");

            home.dirtyFlags = 0;
            added.Destroy(DestroyMode.Vanish);
            Check((home.dirtyFlags & MapMeshFlagDefOf.Things) != 0, "trims.diagonal.refresh.onRemove",
                "removing the straight left the diagonal's section clean");
            ends = Graphic_StripTrim.EndsFor(TrimPiece.For(diagonal), map, a, Rot4.North);
            Check(!ends.b.mitre, "trims.diagonal.refresh.squareAgain",
                $"with the straight gone the head is {EndName(ends.b)}, want square");
            Clear();
        }

        internal static string GeometryValues(TrimPiece.Kind shape, int rot, IntVec3 cell, StripLayout layout,
            DiagonalEnds ends)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector3>();
            var colors = new List<Color32>();
            var tris = new List<int>();
            StripTrimGeometry.Append(verts, uvs, colors, tris, shape, new Rot4(rot), cell, Vector3.zero,
                new Color32(255, 255, 255, 255), layout, ends);
            var values = new List<string>();
            for (int i = 0; i < verts.Count; i++)
            {
                values.Add(Scaled(verts[i].x));
                values.Add(Scaled(verts[i].z));
                values.Add(Scaled(uvs[i].x));
                values.Add(Scaled(uvs[i].y));
            }
            return string.Join(" ", values);
        }

        /// <summary>Two golden lines agree when every word does, numbers within one.</summary>
        internal static bool GoldenLineMatches(string a, string b)
        {
            string[] wa = a.Split(' ');
            string[] wb = b.Split(' ');
            if (wa.Length != wb.Length) return false;
            for (int i = 0; i < wa.Length; i++)
            {
                if (wa[i] == wb[i]) continue;
                if (!long.TryParse(wa[i], out long x) || !long.TryParse(wb[i], out long y) || Math.Abs(x - y) > 1)
                {
                    return false;
                }
            }
            return true;
        }

        internal static string Scaled(float value) =>
            ((long)Math.Round(value * 4096.0)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        // ---- what the trims cost to draw ---------------------------------

        /// <summary>A measurement: printed in the report, counted as nothing.</summary>
        internal static void Note(string name, string detail)
        {
            Report.AppendLine("  INFO  " + name + " — " + detail);
        }

        internal static readonly Rot4[] Rots = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };

        /// <summary>
        /// Measures what the trims cost the renderer, and asserts what the strip
        /// renderer promises: one texture on the map, and ONE draw call per
        /// section for every layout here, whatever the materials, the paint or
        /// whether they are blueprints. Until 2026-10 the same layouts took 21,
        /// 24 and 21. It names no trim graphic class and no patch, so the same
        /// case reads the same numbers off whatever the trims are drawn with,
        /// which is how that before was measured.
        ///
        /// A map section is one mesh per material (MapDrawLayer.GetSubMesh keys
        /// on the Material), and each submesh is one Graphics.DrawMesh a frame,
        /// so the submeshes a section's things layer holds for trim textures are
        /// the trims' draw calls in that section. Two layouts, each inside one
        /// section and unfogged (SectionLayer_Things skips fogged things): every
        /// shape at every rotation, and runs of straights at every rotation.
        /// Each is measured in one stuff, in three stuffs with every fourth
        /// piece painted, and as blueprints.
        /// </summary>
        internal static void CaseTrimCost(Map map)
        {
            List<ThingDef> defs = TrimNames.Select(n => DefDatabase<ThingDef>.GetNamedSilentFail(n)).ToList();
            ThingDef straight = defs[0];
            if (defs.Any(d => d?.graphic == null))
            {
                Skip("trims.cost", "a trim def or its graphic is missing");
                return;
            }

            // Every texture a trim draws on the map, read off the graphics
            // themselves, so the set is right whatever class draws them.
            var drawn = new HashSet<Texture>();
            foreach (ThingDef def in defs)
            {
                foreach (Rot4 rot in Rots)
                {
                    if (def.graphic.MatAt(rot)?.mainTexture is Texture texture) drawn.Add(texture);
                }
            }
            ModContentPack pack = LoadedModManager.RunningModsListForReading
                .FirstOrDefault(m => m.assemblies.loadedAssemblies.Contains(typeof(Harness).Assembly));
            List<Texture2D> folder = pack == null ? new List<Texture2D>()
                : pack.GetContentHolder<Texture2D>().GetAllUnderPath("NeatEdges/Trim/").ToList();
            List<Texture2D> onMap = drawn.OfType<Texture2D>().ToList();
            Note("trims.cost.textures",
                $"{folder.Count} loaded from Trim/, {folder.Sum(TextureBytes):N0} bytes; "
                + $"{onMap.Count} drawn on the map, {onMap.Sum(TextureBytes):N0} bytes ("
                + string.Join(", ", onMap.GroupBy(Describe).Select(g => g.Count() + " × " + g.Key)) + ")");
            Check(onMap.Count == 1, "trims.cost.oneTexture",
                $"{onMap.Count} textures drawn on the map: " + string.Join(", ", onMap.Select(t => t.name)));

            List<System.Reflection.MethodBase> ours = HarmonyLib.Harmony.GetAllPatchedMethods()
                .Where(m => HarmonyLib.Harmony.GetPatchInfo(m)?.Owners.Contains(HarmonyInit.Id) == true)
                .ToList();
            bool atlasDoor = ours.Any(m => m.Name == nameof(GlobalTextureAtlasManager.TryInsertStatic)
                && m.DeclaringType == typeof(GlobalTextureAtlasManager));
            Note("trims.cost.harmony", $"{ours.Count} methods patched by {HarmonyInit.Id}; "
                + "the texture atlas door " + (atlasDoor ? "is" : "is not") + " one of them");

            Section section = map.mapDrawer.SectionAt(Origin(map));
            CellRect rect = section.CellRect;
            var pieces = new List<(ThingDef def, IntVec3 cell, Rot4 rot)>();
            for (int s = 0; s < defs.Count; s++)
            {
                for (int r = 0; r < Rots.Length; r++)
                {
                    pieces.Add((defs[s], new IntVec3(rect.minX + 1 + 2 * s, 0, rect.minZ + 1 + 2 * r), Rots[r]));
                }
            }
            var straights = new List<(ThingDef def, IntVec3 cell, Rot4 rot)>();
            for (int r = 0; r < Rots.Length; r++)
            {
                for (int i = 0; i < 4; i++)
                {
                    straights.Add((straight, new IntVec3(rect.minX + 1 + i, 0, rect.minZ + 10 + r), Rots[r]));
                }
            }

            ThingDef[] stuffs =
            {
                GenStuff.DefaultStuffFor(straight), ThingDefOf.BlocksGranite, ThingDefOf.Steel,
            };
            ColorDef paint = DefDatabase<ColorDef>.AllDefs.FirstOrDefault(c => c.colorType == ColorType.Structure);

            bool measured = true;
            foreach ((string layoutName, var layout) in new[] { ("pieces", pieces), ("straights", straights) })
            {
                foreach (string variant in new[] { "oneStuff", "mixed", "blueprints" })
                {
                    Clear();
                    for (int i = 0; i < layout.Count; i++)
                    {
                        (ThingDef def, IntVec3 cell, Rot4 rot) = layout[i];
                        ThingDef stuff = variant == "mixed" ? stuffs[i % stuffs.Length] : stuffs[0];
                        map.fogGrid.Unfog(cell);
                        if (variant == "blueprints")
                        {
                            Spawned.Add(GenConstruct.PlaceBlueprintForBuild(def, cell, map, rot, Faction.OfPlayer, stuff));
                            continue;
                        }
                        Thing thing = ThingMaker.MakeThing(def, stuff);
                        thing.SetFactionDirect(Faction.OfPlayer);
                        Spawned.Add(GenSpawn.Spawn(thing, cell, map, rot));
                        if (variant == "mixed" && i % 4 == 3 && paint != null)
                        {
                            ((Building)thing).ChangePaint(paint);
                        }
                    }

                    SectionLayer layer = section.GetLayer(typeof(SectionLayer_ThingsGeneral));
                    section.RegenerateSingleLayer(layer);
                    int calls = 0;
                    int verts = 0;
                    foreach (LayerSubMesh sub in layer.subMeshes)
                    {
                        if (!sub.finalized || sub.disabled || sub.verts.Count == 0) continue;
                        if (sub.material == null || !drawn.Contains(sub.material.mainTexture)) continue;
                        calls++;
                        verts += sub.verts.Count;
                    }
                    measured &= calls > 0;
                    Note($"trims.cost.{layoutName}.{variant}",
                        $"{layout.Count} pieces: {calls} draw call(s), {verts} vertices");
                    Check(calls == 1, $"trims.cost.oneCall.{layoutName}.{variant}",
                        $"{calls} draw calls for {layout.Count} pieces");
                }
            }
            Clear();
            Check(measured, "trims.cost.measured",
                "a layout printed no trim submeshes at all, so its numbers above measure nothing");
        }

        // ---- who may build them -------------------------------------------

        /// <summary>
        /// Every ideoligion's members may build every trim. The trims set
        /// canGenerateDefaultDesignator false so the Floors tab shows a button
        /// per shape rather than one per trim, and Ideo.MembersCanBuild reads
        /// that flag as the mark of a building only some ideoligions may raise.
        /// Every pawn carries an ideoligion, Ideology or not, and the one a game
        /// without the DLC makes is not in classic mode, so from v1.1.0 to
        /// v1.1.2 no colonist would build a trim in any game without Ideology,
        /// nor in most games with it. The blueprint placed; the work was refused.
        ///
        /// Asked of every ideoligion in the game that refuses others' buildings:
        /// one outside classic mode with no precept letting its members build
        /// them. A turret's gun, which vanilla marks the same way, is the
        /// control: each such ideoligion must still refuse it, or the case is
        /// reading an ideoligion that refuses nothing. Then a colonist is asked
        /// through GenConstruct.CanConstruct, the call the construction work
        /// givers make, about a floor border blueprint beside them.
        /// </summary>
        internal static void CaseTrimsBuildable(Map map)
        {
            List<ThingDef> trims = AllTrims().Select(t => DefDatabase<ThingDef>.GetNamedSilentFail(t.name))
                .Where(d => d != null).ToList();
            ThingDef control = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_MiniTurret");
            if (trims.Count == 0 || control == null)
            {
                Skip("trims.buildable", trims.Count == 0 ? "no trims loaded" : "Gun_MiniTurret missing, so no control");
                return;
            }

            List<Ideo> all = Find.IdeoManager.IdeosListForReading;
            List<Ideo> strict = all.Where(i => !i.classicMode
                && !i.PreceptsListForReading.Any(p => p.def.willingToConstructOtherIdeoBuildables)).ToList();
            Note("trims.buildable.ideos", $"{all.Count} in the game, {strict.Count} refusing others' buildings: "
                + string.Join(", ", all.Select(i => $"[{all.IndexOf(i)}] {i.name} (classic {i.classicMode}, "
                    + $"made without the DLC {i.createdFromNoExpansionGame}, refuses {strict.Contains(i)})")));
            if (strict.Count == 0)
            {
                Skip("trims.buildable", "no ideoligion here refuses another's building, so nothing would be tested");
                return;
            }

            // Unspawned: MembersCanBuild reads only the def being built and the
            // thing's style precept, and a blueprint and a frame both name their
            // trim through entityDefToBuild.
            var things = new List<Thing>();
            foreach (ThingDef def in trims)
            {
                foreach (ThingDef made in new[] { def.blueprintDef, def.frameDef })
                {
                    if (made != null) things.Add(ThingMaker.MakeThing(made, GenStuff.DefaultStuffFor(made)));
                }
            }
            Thing gun = ThingMaker.MakeThing(control, GenStuff.DefaultStuffFor(control));

            foreach (Ideo ideo in strict)
            {
                int index = all.IndexOf(ideo);
                Check(!ideo.MembersCanBuild(gun), $"trims.buildable.control.ideo{index}",
                    $"{ideo.name} would build {control.defName}, so it refuses nothing and the check below proves nothing");
                List<string> refused = things.Where(t => !ideo.MembersCanBuild(t)).Select(t => t.def.defName).ToList();
                Check(refused.Count == 0, $"trims.buildable.ideo{index}",
                    $"{ideo.name} refuses {refused.Count} of {things.Count}: {string.Join(", ", refused.Take(6))}"
                    + (refused.Count > 6 ? ", ..." : ""));
            }

            Pawn colonist = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p.Ideo != null && strict.Contains(p.Ideo));
            if (colonist == null)
            {
                Skip("trims.buildable.colonist", "no colonist holds an ideoligion that refuses others' buildings");
                return;
            }

            Clear();
            ThingDef border = trims[0];
            ThingDef stuff = GenStuff.DefaultStuffFor(border);
            IntVec3 cell = IntVec3.Invalid;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(colonist.Position, 8f, false))
            {
                if (c.InBounds(map) && c.GetThingList(map).Count == 0
                    && GenConstruct.CanPlaceBlueprintAt(border, c, Rot4.South, map, stuffDef: stuff).Accepted
                    && colonist.CanReach(c, Verse.AI.PathEndMode.Touch, Danger.Deadly))
                {
                    cell = c;
                    break;
                }
            }
            if (!cell.IsValid)
            {
                Skip("trims.buildable.colonist", "no buildable cell the colonist can reach");
                return;
            }

            map.fogGrid.Unfog(cell);
            Thing blueprint = GenConstruct.PlaceBlueprintForBuild(border, cell, map, Rot4.South, Faction.OfPlayer, stuff);
            Spawned.Add(blueprint);
            Verse.AI.JobFailReason.Clear();
            bool can = GenConstruct.CanConstruct(blueprint, colonist, checkSkills: false, forced: true);
            Check(can, "trims.buildable.colonist",
                $"{colonist.LabelShort} of {colonist.Ideo.name} refused a {border.defName} blueprint: "
                + (Verse.AI.JobFailReason.Reason ?? "no reason given"));
            Verse.AI.JobFailReason.Clear();
            Clear();
        }

        // ---- the strip renderer ------------------------------------------

        /// <summary>
        /// Trims drawn from a strip by Graphic_StripTrim. Everything a
        /// per-facing Graphic_Multi gets for free has to hold here with no
        /// Harmony: the ghost and the blueprint keep the class, Copy still finds
        /// a designator, paint and stuff colour reach the vertices, and every
        /// edge samples the half of the strip that keeps the light coming from
        /// the north-west.
        /// </summary>
        internal static void CaseStripTrim(Map map)
        {
            List<ThingDef> strips = AllTrims().Select(t => DefDatabase<ThingDef>.GetNamedSilentFail(t.name))
                .Where(d => d?.graphic is Graphic_StripTrim).ToList();
            if (strips.Count == 0)
            {
                Skip("trims.strip", "no trim draws from a strip");
                return;
            }

            foreach (ThingDef def in strips)
            {
                var graphic = (Graphic_StripTrim)def.graphic;
                TrimPiece piece = TrimPiece.For(def);
                var problems = new List<string>();
                if (piece == null) problems.Add("no TrimPiece");
                if (!graphic.Tinted) problems.Add("prints on " + graphic.mat?.shader?.name + ", so not in one call");

                if (!(def.blueprintDef?.graphic is Graphic_StripTrim blueprint))
                {
                    problems.Add("blueprint draws with " + def.blueprintDef?.graphic?.GetType().Name);
                }
                else
                {
                    // The queue the blueprint def asks for, which vanilla sets to
                    // 2950 and another mod may change; the wall's blueprint is
                    // the vanilla control, drawn by Graphic_Single. Ours must
                    // honour the request as theirs does, whatever the number.
                    int asked = def.blueprintDef.graphicData.renderQueue;
                    int wall = ThingDefOf.Wall.blueprintDef?.graphic?.MatSingle?.renderQueue ?? -1;
                    Note("trims.strip.blueprintQueue",
                        $"ours {blueprint.mat.renderQueue}, def asks {asked}, vanilla wall blueprint {wall}");
                    if (blueprint.mat.renderQueue != asked && blueprint.mat.renderQueue != wall)
                        problems.Add($"blueprint render queue {blueprint.mat.renderQueue}, def asks {asked}, wall's {wall}");
                    if (blueprint.Tinted) problems.Add("blueprint prints its colour into the vertices");
                    if (TrimPiece.For(def.blueprintDef)?.shape != piece?.shape) problems.Add("blueprint resolves no shape");
                }

                Graphic ghost = GhostUtility.GhostGraphicFor(def.graphic, def, new Color(0.5f, 1f, 0.6f, 0.4f));
                if (!(ghost is Graphic_StripTrim ghostStrip))
                {
                    problems.Add("ghost draws with " + ghost?.GetType().Name);
                }
                else
                {
                    if (ghostStrip.mat.shader != ShaderTypeDefOf.EdgeDetect.Shader) problems.Add("ghost shader " + ghostStrip.mat.shader?.name);
                    // A diagonal's band reaches past its cell into the corners of
                    // the two cells beside it, by a band's depth over root two.
                    float reach = piece?.shape == TrimPiece.Kind.Diagonal
                        ? 0.501f + StripTrimGeometry.Depth * (float)StripTrimGeometry.R2
                        : 0.501f;
                    foreach (Rot4 rot in Rots)
                    {
                        Bounds bounds = ghostStrip.MeshFor(piece?.shape ?? TrimPiece.Kind.Straight, rot,
                            ghostStrip.LayoutFor(piece)).bounds;
                        if (bounds.size.x <= 0f || bounds.min.x < -reach || bounds.max.x > reach
                            || bounds.min.z < -reach || bounds.max.z > reach)
                        {
                            problems.Add($"ghost mesh {rot.ToStringHuman()} spans {bounds.min} to {bounds.max}");
                        }
                    }
                }

                Designator_Build copy = BuildCopyCommandUtility.FindAllowedDesignator(def);
                if (copy == null || copy.PlacingDef != def) problems.Add("Copy finds no designator for it");

                Check(problems.Count == 0, "trims.strip." + def.defName, string.Join("; ", problems));
            }

            CaseStripLight();

            ThingDef straight = strips.FirstOrDefault(d => TrimPiece.For(d)?.shape == TrimPiece.Kind.Straight);
            if (straight == null)
            {
                Skip("trims.strip.paint", "no straight draws from a strip");
                return;
            }
            CaseStripPaint(map, straight);

            ThingDef overlaid = strips.FirstOrDefault(d => TrimPiece.For(d)?.shape == TrimPiece.Kind.Straight
                && TrimPiece.For(d).paintOverlay);
            if (overlaid == null)
            {
                Skip("trims.strip.paintOverlay", "no straight carries a paint overlay");
                return;
            }
            CaseStripPaintOverlay(map, overlaid);
        }

        /// <summary>
        /// Each edge samples the right band of the strip, the right way up. The
        /// top band is the band on a north edge, lit lip outermost; the next is
        /// the band on a south edge, shaded lip outermost, its outer edge at its
        /// last row; a three-band strip's third is the side-lit band, outer edge
        /// at its first row, against the south band's. So a north or west band
        /// reads the top band with its outer edge at V 1, and a south or east
        /// band the second with its outer edge where the second band ends. A
        /// strip with a paint overlay halves every band, and its overlay is the
        /// bands mirrored into the bottom half: the overlay's north band has
        /// its outer edge at V 0. Written out per rotation, band count and
        /// layer rather than computed, so a wrong rule fails here instead of
        /// agreeing with itself. Along the band, one tile must span a whole
        /// number of repeats.
        /// </summary>
        internal static void CaseStripLight()
        {
            // bands, layers, layer, rotation: which axis is depth, the outer
            // and inner coordinate on it, and V at each.
            const float third = 1f / 3f;
            const float sixth = 1f / 6f;
            var expected = new (int bands, int layers, int layer, Rot4 rot, bool depthOnZ, float outer, float inner,
                float outerV, float innerV)[]
            {
                (2, 1, 0, Rot4.North, true, 0.5f, 0.25f, 1f, 0.5f),
                (2, 1, 0, Rot4.East, false, 0.5f, 0.25f, 0f, 0.5f),
                (2, 1, 0, Rot4.South, true, -0.5f, -0.25f, 0f, 0.5f),
                (2, 1, 0, Rot4.West, false, -0.5f, -0.25f, 1f, 0.5f),
                (3, 1, 0, Rot4.North, true, 0.5f, 0.25f, 1f, 2 * third),
                (3, 1, 0, Rot4.East, false, 0.5f, 0.25f, third, 2 * third),
                (3, 1, 0, Rot4.South, true, -0.5f, -0.25f, third, 2 * third),
                (3, 1, 0, Rot4.West, false, -0.5f, -0.25f, 1f, 2 * third),
                (3, 2, 0, Rot4.North, true, 0.5f, 0.25f, 1f, 5 * sixth),
                (3, 2, 0, Rot4.East, false, 0.5f, 0.25f, 4 * sixth, 5 * sixth),
                (3, 2, 0, Rot4.South, true, -0.5f, -0.25f, 4 * sixth, 5 * sixth),
                (3, 2, 0, Rot4.West, false, -0.5f, -0.25f, 1f, 5 * sixth),
                (3, 2, 1, Rot4.North, true, 0.5f, 0.25f, 0f, sixth),
                (3, 2, 1, Rot4.East, false, 0.5f, 0.25f, 2 * sixth, sixth),
                (3, 2, 1, Rot4.South, true, -0.5f, -0.25f, 2 * sixth, sixth),
                (3, 2, 1, Rot4.West, false, -0.5f, -0.25f, 0f, sixth),
            };
            const float period = 1f / 64f;
            foreach (var want in expected)
            {
                var verts = new List<Vector3>();
                var uvs = new List<Vector3>();
                var colors = new List<Color32>();
                var tris = new List<int>();
                StripTrimGeometry.Append(verts, uvs, colors, tris, TrimPiece.Kind.Straight, want.rot,
                    new IntVec3(37, 0, 81), Vector3.zero, new Color32(255, 255, 255, 255),
                    new StripLayout(period, want.bands, 1, want.layers, want.layer));
                var problems = new List<string>();
                if (verts.Count != 4 || uvs.Count != 4 || colors.Count != 4 || tris.Count != 6)
                {
                    problems.Add($"{verts.Count} verts, {uvs.Count} uvs, {colors.Count} colours, {tris.Count} indices");
                }
                for (int i = 0; i < verts.Count && i < uvs.Count; i++)
                {
                    float depth = want.depthOnZ ? verts[i].z : verts[i].x;
                    float v = uvs[i].y;
                    if (Mathf.Abs(depth - want.outer) < 1e-4f && Mathf.Abs(v - want.outerV) > 1e-4f)
                        problems.Add($"outer vertex V {v}, want {want.outerV}");
                    else if (Mathf.Abs(depth - want.inner) < 1e-4f && Mathf.Abs(v - want.innerV) > 1e-4f)
                        problems.Add($"inner vertex V {v}, want {want.innerV}");
                    else if (Mathf.Abs(depth - want.outer) >= 1e-4f && Mathf.Abs(depth - want.inner) >= 1e-4f)
                        problems.Add($"vertex at depth {depth}, off the band");
                }
                float span = uvs.Count == 0 ? 0f : uvs.Max(u => u.x) - uvs.Min(u => u.x);
                if (Mathf.Abs(span - 1f / period) > 1e-3f) problems.Add($"one tile spans {span} repeats, want {1f / period}");
                string strip = want.layers == 1 ? $"bands{want.bands}" : $"bands{want.bands}.layer{want.layer}";
                Check(problems.Count == 0, $"trims.strip.light.{strip}.{want.rot.ToStringHuman()}",
                    string.Join("; ", problems));
            }
        }

        /// <summary>
        /// Two straights side by side, one painted: they print into ONE
        /// submesh, the painted one's colour carried in its vertices, and the
        /// submesh's lists stay in step (a vertex without its UV or colour would
        /// skew every trim printed after it in the section). Unpainting gives
        /// the trim back its stuff's colour.
        /// </summary>
        internal static void CaseStripPaint(Map map, ThingDef straight)
        {
            ColorDef paint = DefDatabase<ColorDef>.AllDefs.FirstOrDefault(c => c.colorType == ColorType.Structure);
            if (paint == null)
            {
                Skip("trims.strip.paint", "no structure paint colour loaded");
                return;
            }
            Clear();
            Section section = map.mapDrawer.SectionAt(Origin(map));
            CellRect rect = section.CellRect;
            IntVec3 a = new IntVec3(rect.minX + 2, 0, rect.minZ + 2);
            IntVec3 b = a + IntVec3.East;
            map.fogGrid.Unfog(a);
            map.fogGrid.Unfog(b);
            Place(map, a, straight, Rot4.North);
            Place(map, b, straight, Rot4.North);
            Thing plain = Spawned[Spawned.Count - 2];
            var painted = (Building)Spawned[Spawned.Count - 1];
            painted.ChangePaint(paint);

            SectionLayer layer = section.GetLayer(typeof(SectionLayer_ThingsGeneral));
            section.RegenerateSingleLayer(layer);
            Texture strip = ((Graphic_StripTrim)straight.graphic).mat.mainTexture;
            List<LayerSubMesh> ours = layer.subMeshes
                .Where(s => s.finalized && !s.disabled && s.material?.mainTexture == strip).ToList();
            var colours = new HashSet<Color32>(ours.SelectMany(s => s.colors));
            Color32 plainColour = plain.DrawColor;
            Color32 paintColour = paint.color;
            Check(ours.Count == 1 && colours.Contains(plainColour) && colours.Contains(paintColour),
                "trims.strip.paintSharesOneCall",
                $"{ours.Count} submesh(es); colours {string.Join(", ", colours)}; want {plainColour} and {paintColour}");

            bool inStep = ours.All(s => s.verts.Count == s.uvs.Count && s.verts.Count == s.colors.Count
                && s.tris.Count % 3 == 0 && s.tris.All(t => t < s.verts.Count));
            Check(inStep, "trims.strip.listsInStep",
                string.Join("; ", ours.Select(s => $"{s.verts.Count} verts, {s.uvs.Count} uvs, {s.colors.Count} colours, {s.tris.Count} indices")));

            painted.ChangePaint(null);
            Graphic after = painted.Graphic;
            Check(after is Graphic_StripTrim && after.color == plain.Graphic.color, "trims.strip.unpaintRestores",
                $"{after?.GetType().Name} in {after?.color}, want {plain.Graphic.color}");
            Clear();
        }

        /// <summary>
        /// A trim with a paint overlay prints every polygon twice into its one
        /// submesh: from the strip's top half in its stuff's colour, then from
        /// the bottom half, the overlay, in its paint. Two straights side by
        /// side, one painted: the painted one's overlay alone takes the paint,
        /// and its bands and both layers of the other keep the stuff's colour,
        /// each on its own half of the strip. Unpainting gives the overlay back
        /// the stuff's colour.
        /// </summary>
        internal static void CaseStripPaintOverlay(Map map, ThingDef straight)
        {
            Clear();
            Section section = map.mapDrawer.SectionAt(Origin(map));
            CellRect rect = section.CellRect;
            IntVec3 a = new IntVec3(rect.minX + 2, 0, rect.minZ + 2);
            IntVec3 b = a + IntVec3.East;
            map.fogGrid.Unfog(a);
            map.fogGrid.Unfog(b);
            Place(map, a, straight, Rot4.North);
            Place(map, b, straight, Rot4.North);
            Thing plain = Spawned[Spawned.Count - 2];
            var painted = (Building)Spawned[Spawned.Count - 1];
            Color32 stuffColour = Graphic_StripTrim.StuffColor(plain);
            bool Same(Color32 x, Color32 y) => x.r == y.r && x.g == y.g && x.b == y.b && x.a == y.a;
            ColorDef paint = DefDatabase<ColorDef>.AllDefs
                .FirstOrDefault(c => c.colorType == ColorType.Structure && !Same(c.color, stuffColour));
            if (paint == null)
            {
                Skip("trims.strip.paintOverlay", "no structure paint unlike the stuff's colour");
                Clear();
                return;
            }
            painted.ChangePaint(paint);
            Color32 paintColour = paint.color;

            // One layer of each straight, for the vertex counts.
            var graphic = (Graphic_StripTrim)straight.graphic;
            StripLayout layout = graphic.LayoutFor(TrimPiece.For(straight));
            int LayerVerts(IntVec3 cell)
            {
                var verts = new List<Vector3>();
                StripTrimGeometry.Append(verts, new List<Vector3>(), new List<Color32>(), new List<int>(),
                    TrimPiece.Kind.Straight, Rot4.North, cell, Vector3.zero, new Color32(255, 255, 255, 255), layout);
                return verts.Count;
            }
            int plainVerts = LayerVerts(a);
            int paintedVerts = LayerVerts(b);

            SectionLayer layer = section.GetLayer(typeof(SectionLayer_ThingsGeneral));
            List<LayerSubMesh> Ours()
            {
                section.RegenerateSingleLayer(layer);
                return layer.subMeshes
                    .Where(s => s.finalized && !s.disabled && s.material?.mainTexture == graphic.mat.mainTexture)
                    .ToList();
            }
            List<LayerSubMesh> ours = Ours();
            Check(layout.layers == 2 && ours.Count == 1, "trims.strip.paintOverlay.oneCall",
                $"{layout.layers} layer(s); {ours.Count} submesh(es)");

            // Every vertex as (colour, which half of the strip it samples).
            var printed = ours.SelectMany(s => s.colors.Zip(s.uvs, (c, uv) => (c, overlay: uv.y < 0.5f + 1e-4f))).ToList();
            int paintOverlay = printed.Count(p => Same(p.c, paintColour) && p.overlay);
            int paintBands = printed.Count(p => Same(p.c, paintColour) && !p.overlay);
            int stuffOverlay = printed.Count(p => Same(p.c, stuffColour) && p.overlay);
            int stuffBands = printed.Count(p => Same(p.c, stuffColour) && !p.overlay);
            int other = printed.Count - paintOverlay - paintBands - stuffOverlay - stuffBands;
            Check(paintOverlay == paintedVerts && paintBands == 0 && stuffOverlay == plainVerts
                    && stuffBands == plainVerts + paintedVerts && other == 0,
                "trims.strip.paintOverlay.leavesTakePaint",
                $"paint: {paintOverlay} overlay and {paintBands} band vertices, want {paintedVerts} and 0; "
                + $"stuff: {stuffOverlay} overlay and {stuffBands} band vertices, want {plainVerts} and "
                + $"{plainVerts + paintedVerts}; {other} in neither colour");

            painted.ChangePaint(null);
            List<Color32> after = Ours().SelectMany(s => s.colors).ToList();
            int total = 2 * (plainVerts + paintedVerts);
            Check(after.Count == total && after.All(c => Same(c, stuffColour)), "trims.strip.paintOverlay.unpainted",
                $"{after.Count(c => Same(c, stuffColour))} of {after.Count} vertices in the stuff's colour, want all {total}");
            Clear();
        }

        internal static string Describe(Texture2D texture) =>
            $"{texture.format} {texture.width}x{texture.height}, {texture.mipmapCount} mips";

        /// <summary>
        /// Bytes a texture holds on the GPU, summed over its mip chain. A
        /// format this does not know reads as zero, and the report shows the
        /// format, so an odd total is easy to trace.
        /// </summary>
        internal static long TextureBytes(Texture2D texture)
        {
            long total = 0;
            for (int i = 0; i < texture.mipmapCount; i++)
            {
                long w = Math.Max(1, texture.width >> i);
                long h = Math.Max(1, texture.height >> i);
                long blocks = ((w + 3) / 4) * ((h + 3) / 4);
                switch (texture.format)
                {
                    case TextureFormat.DXT1: total += blocks * 8; break;
                    case TextureFormat.DXT5:
                    case TextureFormat.BC7: total += blocks * 16; break;
                    case TextureFormat.RGBA32:
                    case TextureFormat.ARGB32:
                    case TextureFormat.BGRA32: total += w * h * 4; break;
                    case TextureFormat.RGB24: total += w * h * 3; break;
                    case TextureFormat.Alpha8:
                    case TextureFormat.R8: total += w * h; break;
                }
            }
            return total;
        }

        // ---- the painted area -------------------------------------------

        /// <summary>
        /// Nothing is added to a map nobody painted. An empty area on every map
        /// would be a line in every save, and would cost every map its whole
        /// area list for a player who later removes the mod. Runs before any
        /// case paints.
        /// </summary>
        internal static void CaseAreaIsLazy(Map map)
        {
            Check(Area_HardEdges.On(map) == null, "area.lazy",
                "a map nobody painted already carries a hard-edge area");
        }

        /// <summary>
        /// Both tools are on the Zone tab, with icons, and no longer on the
        /// Floors tab where they began. The patch names the classes as strings,
        /// so a rename leaves the tab without them and logs nothing at all.
        /// </summary>
        internal static void CaseAreaDesignatorsRegistered()
        {
            DesignationCategoryDef zone =
                DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Zone");
            if (zone == null) { Skip("area.designators", "no Zone category"); return; }

            Designator expand = zone.AllResolvedDesignators
                .FirstOrDefault(d => d is Designator_AreaHardEdgesExpand);
            Designator clear = zone.AllResolvedDesignators
                .FirstOrDefault(d => d is Designator_AreaHardEdgesClear);

            Check(expand != null && clear != null, "area.designators.onZoneTab",
                $"expand found: {expand != null}, clear found: {clear != null}");

            DesignationCategoryDef floors =
                DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Floors");
            int onFloors = floors?.AllResolvedDesignators.Count(d => d is Designator_AreaHardEdges) ?? 0;
            Check(onFloors == 0, "area.designators.offFloorsTab",
                $"{onFloors} area tool(s) still registered on the Floors tab");

            bool iconsLoaded = expand != null && clear != null
                && expand.icon != null && expand.icon != BaseContent.BadTex
                && clear.icon != null && clear.icon != BaseContent.BadTex;
            Check(iconsLoaded, "area.designators.icons", "a tool icon did not load");
        }

        /// <summary>
        /// The overlay toggle shows OUR icon, at the size it was drawn for. The
        /// toggle falls back to vanilla's remove-bridge glyph when ours is
        /// missing, so it never disappears, and that fallback is exactly what
        /// would hide a missing texture from anyone looking at the game.
        /// </summary>
        internal static void CaseOverlayIcon()
        {
            Texture2D icon = Patch_EdgeOverlayToggle.Icon;
            bool ours = icon != null && icon.name == "OverlayToggle";
            Check(ours && icon.width == 48 && icon.height == 48, "overlay.icon",
                icon == null ? "no icon at all"
                    : $"got '{icon.name}' at {icon.width}x{icon.height}");
        }

        /// <summary>
        /// A painted tile is four hardened edges and nothing else, so every mask
        /// in the 5x5 around it must match four single-edge markers stacked on
        /// the same tile. One comparison covers the tile, the two-sided edges
        /// and the sealed corners, against the case the marker suite already
        /// pins (mask.stacking.fourIsEight).
        ///
        /// Painted through the real designator, so get-or-create is on the path.
        /// </summary>
        internal static void CaseAreaEqualsFourEdges(Map map)
        {
            if (Single == null) { Skip("area.equalsFourEdges", "def missing"); return; }

            Clear();
            IntVec3 c = Origin(map);
            PlaceFourEdges(map, c);
            int[] marker = Neighbourhood(map, c);
            Clear();

            new Designator_AreaHardEdgesExpand().DesignateSingleCell(c);
            int[] painted = Neighbourhood(map, c);
            bool created = Area_HardEdges.On(map) != null;

            // The detail is built before Check decides, so it must be safe to
            // build on a pass too: diff is -1 then, and indexing with it threw.
            int diff = FirstDifference(marker, painted);
            string detail = !created ? "painting created no area"
                : diff < 0 ? "identical"
                : $"cell {diff} of the 5x5: marker {Show(marker[diff])}, "
                  + $"painted {Show(painted[diff])}";
            Check(created && diff < 0, "area.equalsFourEdges", detail);
            Clear();
        }

        /// <summary>
        /// Clearing puts every mask back, and each tool refuses the tiles the
        /// other one owns, which is what lets a drag across a mixed region touch
        /// only the tiles that need it.
        /// </summary>
        internal static void CaseAreaClearRestores(Map map)
        {
            Clear();
            IntVec3 c = Origin(map);
            Designator_AreaHardEdgesExpand expand = new Designator_AreaHardEdgesExpand();
            Designator_AreaHardEdgesClear clear = new Designator_AreaHardEdgesClear();

            bool clearRefusesUnpainted = !clear.CanDesignateCell(c).Accepted;
            expand.DesignateSingleCell(c);
            bool expandRefusesPainted = !expand.CanDesignateCell(c).Accepted;
            clear.DesignateSingleCell(c);

            int stillHard = Neighbourhood(map, c).Count(m => m != 0);

            // With Perspective: Paths installed both tools refuse every cell,
            // so this would pass without saying anything; yield.tools owns it.
            if (PerspectivePathsInterop.Installed)
            {
                Skip("area.tools.refuseEachOther",
                    "Perspective: Paths is loaded, so both tools refuse every cell (yield.tools)");
            }
            else
            {
                Check(clearRefusesUnpainted && expandRefusesPainted, "area.tools.refuseEachOther",
                    $"clear refused unpainted: {clearRefusesUnpainted}, "
                    + $"expand refused painted: {expandRefusesPainted}");
            }
            Check(stillHard == 0, "area.clearRestores",
                $"{stillHard} of 25 cells still hardened after clearing");
            Clear();
        }

        /// <summary>
        /// Clearing the last painted tile takes the area off the map, so a
        /// player can remove the mod without a saved area of a missing class
        /// costing the map its whole area list. The control is the tile cleared
        /// before it: the area must outlive every tile but the last.
        ///
        /// Through the real clear tool, which is what a player uses.
        /// </summary>
        internal static void CaseAreaClearRemoves(Map map)
        {
            Clear();
            IntVec3 a = Origin(map);
            IntVec3 b = a + new IntVec3(4, 0, 0);
            Designator_AreaHardEdgesExpand expand = new Designator_AreaHardEdgesExpand();
            Designator_AreaHardEdgesClear clear = new Designator_AreaHardEdgesClear();

            expand.DesignateSingleCell(a);
            expand.DesignateSingleCell(b);
            clear.DesignateSingleCell(a);
            bool keptWhilePainted = Area_HardEdges.On(map) != null;
            clear.DesignateSingleCell(b);
            int left = map.areaManager.AllAreas.Count(x => x is Area_HardEdges);

            Check(keptWhilePainted, "area.clearKeepsWhilePainted",
                "clearing one of two painted tiles removed the area");
            Check(left == 0, "area.clearRemoves",
                $"{left} hard-edge area(s) left after clearing every painted tile");
            Clear();
        }

        /// <summary>
        /// Map finalization drops an area that arrives empty: Perspective:
        /// Paths adds an empty zone to every map, and the migration adopts each
        /// one. The control is a painted area beside it, which must stay.
        /// </summary>
        internal static void CaseAreaEmptyDropped(Map map)
        {
            Clear();
            IntVec3 c = Origin(map);
            Area_HardEdges painted = Area_HardEdges.GetOrCreate(map);
            painted[c] = true;
            Area_HardEdges empty = new Area_HardEdges(map.areaManager);
            map.areaManager.AllAreas.Add(empty);

            int removed = Area_HardEdges.RemoveEmpty(map);
            bool emptyGone = !map.areaManager.AllAreas.Contains(empty);
            bool paintedKept = map.areaManager.AllAreas.Contains(painted) && painted[c];

            Check(removed == 1 && emptyGone && paintedKept, "area.emptyDropped",
                $"removed {removed}, empty gone: {emptyGone}, painted kept: {paintedKept}");
            Clear();
        }

        /// <summary>
        /// Painting repaints. Vanilla's area bookkeeping never touches a mesh,
        /// so without the `Set` override a painted tile would look untouched
        /// until something else dirtied its section.
        ///
        /// On a section's east edge, so the next section has to be dirtied too:
        /// two-sided hardening reaches across the boundary. The control paints
        /// the same tile again, which changes nothing and so must dirty nothing.
        /// That is what shows the first assertion is not reading flags something
        /// else left set.
        /// </summary>
        internal static void CaseAreaPaintDirtiesTerrain(Map map)
        {
            Clear();
            IntVec3 o = Origin(map);
            IntVec3 c = new IntVec3(o.x - o.x % Section.Size + Section.Size - 1, 0, o.z);
            IntVec3 across = c + IntVec3.East;
            if (!across.InBounds(map))
            {
                Skip("area.paintDirtiesTerrain", "no section east of the origin");
                return;
            }

            Section own = map.mapDrawer.SectionAt(c);
            Section next = map.mapDrawer.SectionAt(across);
            ulong terrain = (ulong)MapMeshFlagDefOf.Terrain;
            ulong ownBefore = own.dirtyFlags;
            ulong nextBefore = next.dirtyFlags;

            own.dirtyFlags = 0;
            next.dirtyFlags = 0;
            Area_HardEdges area = Area_HardEdges.GetOrCreate(map);
            area[c] = true;
            bool ownDirtied = (own.dirtyFlags & terrain) != 0;
            bool nextDirtied = (next.dirtyFlags & terrain) != 0;

            own.dirtyFlags = 0;
            next.dirtyFlags = 0;
            area[c] = true;
            bool repaintQuiet = own.dirtyFlags == 0 && next.dirtyFlags == 0;

            // Whatever was pending before, plus a terrain pass for the tile the
            // case just painted and is about to clear.
            own.dirtyFlags |= ownBefore | terrain;
            next.dirtyFlags |= nextBefore | terrain;

            Check(ownDirtied && nextDirtied, "area.paintDirtiesTerrain",
                $"own section dirtied: {ownDirtied}, next section dirtied: {nextDirtied}");
            Check(repaintQuiet, "area.repaintIsQuiet",
                "painting an already painted tile dirtied a section");
            Clear();
        }

        /// <summary>
        /// Two areas on one map fold into one holding both sets of tiles. The
        /// second is what a save that ran both mods loads with, and left alone
        /// it keeps hardening tiles the clear tool cannot reach.
        /// </summary>
        internal static void CaseAreaDuplicatesMerge(Map map)
        {
            Clear();
            IntVec3 a = Origin(map);
            IntVec3 b = a + new IntVec3(4, 0, 0);

            Area_HardEdges first = new Area_HardEdges(map.areaManager);
            Area_HardEdges second = new Area_HardEdges(map.areaManager);
            map.areaManager.AllAreas.Add(first);
            map.areaManager.AllAreas.Add(second);
            first[a] = true;
            second[b] = true;

            int removed = Area_HardEdges.MergeDuplicates(map);
            int remaining = map.areaManager.AllAreas.Count(x => x is Area_HardEdges);
            Area_HardEdges kept = Area_HardEdges.On(map);
            bool both = kept != null && kept[a] && kept[b];

            Check(removed == 1 && remaining == 1 && both, "area.duplicatesMerge",
                $"removed {removed}, {remaining} left, both tiles kept: {both}");
            Clear();
        }

        // ---- migration ----------------------------------------------------

        internal const string LegacyClass = "PerspectivePaths.Area_InvertEdges";

        /// <summary>
        /// A hard-edge area node exactly as Perspective: Paths (the continued
        /// release) wrote it into a real 250x250 colony save in September 2026:
        /// 851 painted tiles. Verbatim, line break inside the payload included,
        /// because the node's shape is the claim under test, and a hand-written
        /// node would only prove this mod can read what it writes.
        /// </summary>
        internal const string LegacyAreaFixture = @"<?xml version=""1.0"" encoding=""utf-8""?>
<fixture>
  <areas>
    <li Class=""PerspectivePaths.Area_InvertEdges"">
      <ID>517</ID>
      <innerGrid>
        <trueCount>851</trueCount>
        <mapSizeX>250</mapSizeX>
        <mapSizeZ>250</mapSizeZ>
        <arrDeflate>7dY7EkAwGEVhhh4rorCwLDVLUCpMLhbgxniMwvnaf5KTKEyKAgAAAI8JksqvD/G4mLtT7cetHw+Z/L34pMrGM8s7Px79uPVnJ06c
+Mn4Ij/HVcl/2dn/QaMaNw7qif8s7s3bI8nuLRffn1gunmRvTvydOAAAAHBsBQ==
</arrDeflate>
      </innerGrid>
    </li>
  </areas>
</fixture>";

        /// <summary>
        /// The type lookup answers for the legacy class, and ONLY for it. The
        /// control is a class name nobody defines, which must still come back
        /// null, or every genuinely missing class in a save would load as a
        /// hard-edge area.
        ///
        /// With Perspective: Paths loaded, its own class must win and the
        /// postfix must stand down: the lookup answers with ITS type, and
        /// nothing counts as adopted.
        /// </summary>
        internal static void CaseMigrationResolvesLegacyClass()
        {
            Type installed = GenTypes.GetTypeInAnyAssembly(LegacyClass);
            if (installed != null)
            {
                Patch_AreaMigration.adopted = 0;
                Type answer = BackCompatibility.GetBackCompatibleType(typeof(Area), LegacyClass, null);
                int counted = Patch_AreaMigration.adopted;
                Patch_AreaMigration.adopted = 0;

                Check(answer == installed && counted == 0, "migration.standsDownWhileInstalled",
                    $"got {answer?.FullName ?? "null"}, adopted {counted}");
                return;
            }

            Type legacy = BackCompatibility.GetBackCompatibleType(typeof(Area), LegacyClass, null);
            Type control = BackCompatibility.GetBackCompatibleType(
                typeof(Area), "PerspectivePaths.Area_NoSuchArea", null);
            Patch_AreaMigration.adopted = 0;
            Patch_AreaMigration.pending = false;

            Check(legacy == typeof(Area_HardEdges), "migration.resolvesLegacyClass",
                $"got {legacy?.FullName ?? "null"}");
            Check(control == null, "migration.leavesOtherClassesAlone",
                $"an unrelated missing class resolved to {control?.FullName}");
        }

        /// <summary>
        /// End to end through the engine's own loader: the verbatim node, read
        /// by `Scribe_Collections` exactly as `AreaManager` reads its list. That
        /// is the path a player's save takes: the class attribute, the type
        /// lookup, the parameterless constructor, and the base `ExposeData`
        /// decoding the grid.
        ///
        /// Counts ActiveCells, not TrueCount. The count is saved beside the grid
        /// and read back verbatim, so it would pass on a payload that failed to
        /// decode; enumerating the cells proves the payload came through.
        ///
        /// Then attached to the live map (the fixture is 250x250, as the
        /// quicktest map is) to show an adopted tile hardens like a painted one.
        /// </summary>
        internal static void CaseMigrationLoadsRealNode(Map map)
        {
            if (GenTypes.GetTypeInAnyAssembly(LegacyClass) != null)
            {
                Skip("migration.loadsRealNode", "Perspective: Paths is loaded; its class wins, by design");
                return;
            }

            Clear();
            List<Area> loaded = LoadAreas(LegacyAreaFixture, out string error);
            if (error != null)
            {
                Check(false, "migration.loadsRealNode", "the load threw: " + error);
                return;
            }

            Area_HardEdges area = loaded != null && loaded.Count == 1
                ? loaded[0] as Area_HardEdges
                : null;
            int cells = area != null ? area.ActiveCells.Count() : -1;

            Check(area != null && area.ID == 517 && cells == 851, "migration.loadsRealNode",
                $"loaded {loaded?.Count ?? 0} area(s) as "
                + $"{loaded?.FirstOrDefault()?.GetType().FullName ?? "nothing"}, "
                + $"id {area?.ID}, {cells} cells (expected 517 and 851)");

            if (area == null || cells <= 0) return;
            if (map.Size.x != 250 || map.Size.z != 250)
            {
                Skip("migration.hardensOnMap", "the harness map is not 250x250");
                return;
            }

            area.areaManager = map.areaManager;
            map.areaManager.AllAreas.Add(area);

            IntVec3 tile = area.ActiveCells.First();
            int mask = Patch_SidedFadeBlock.EdgeMaskAt(tile, map);
            Check(mask == 0xFF, "migration.hardensOnMap",
                $"an adopted tile got {Show(mask)}, expected all 8");
            Clear();
        }

        /// <summary>
        /// The save half of a migration: an adopted area writes itself back
        /// under OUR class, with the legacy name gone from the file, and loads
        /// again with every tile it had. After one save the player's file no
        /// longer depends on the adoption at all.
        ///
        /// Through the engine's own saver rather than an autosave, which needs
        /// the game ticking and was not reliable to wait for.
        /// </summary>
        internal static void CaseMigrationRoundTrips()
        {
            if (GenTypes.GetTypeInAnyAssembly(LegacyClass) != null)
            {
                Skip("migration.roundTrips", "Perspective: Paths is loaded; its class wins, by design");
                return;
            }

            List<Area> adopted = LoadAreas(LegacyAreaFixture, out string error);
            Area_HardEdges before = adopted != null && adopted.Count == 1
                ? adopted[0] as Area_HardEdges
                : null;
            if (error != null || before == null)
            {
                Check(false, "migration.savesAsOurs", "the fixture did not load: " + (error ?? "wrong type"));
                return;
            }

            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "neatedges-migration-resaved.xml");
            string written;
            try
            {
                Scribe.saver.InitSaving(path, "fixture");
                Scribe_Collections.Look(ref adopted, "areas", LookMode.Deep);
                Scribe.saver.FinalizeSaving();
                written = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Scribe.ForceStop();
                Check(false, "migration.savesAsOurs", "the save threw: " + e.Message);
                return;
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }

            bool ours = written.Contains("Class=\"NeatEdges.Area_HardEdges\"");
            bool legacyGone = !written.Contains("PerspectivePaths");
            Check(ours && legacyGone, "migration.savesAsOurs",
                $"written under our class: {ours}, legacy name gone: {legacyGone}");

            List<Area> reloaded = LoadAreas(written, out error);
            Area_HardEdges after = reloaded != null && reloaded.Count == 1
                ? reloaded[0] as Area_HardEdges
                : null;
            bool same = after != null && after.ID == before.ID
                && after.ActiveCells.SequenceEqual(before.ActiveCells);
            Check(error == null && same, "migration.roundTrips",
                error ?? $"reloaded {after?.ActiveCells.Count() ?? -1} tiles against "
                         + $"{before.ActiveCells.Count()}, same id: {after?.ID == before.ID}");

            // The announcement counts only areas that came from another mod, so
            // the mark must follow the node, not the class the two now share.
            Check(before.adoptedFromOtherMod && after != null && !after.adoptedFromOtherMod,
                "migration.marksOnlyAdopted",
                $"legacy node marked: {before.adoptedFromOtherMod}, "
                + $"our own node marked: {after?.adoptedFromOtherMod}");
        }

        /// <summary>
        /// The announcement counts adopted areas with painted tiles only.
        /// Perspective: Paths adds an empty zone to every map, so counting every
        /// adoption told a switcher their areas came across on maps they never
        /// painted. The real fixture is the painted one; an adopted area with
        /// nothing on it is the control.
        /// </summary>
        internal static void CaseMigrationCountsPainted(Map map)
        {
            if (GenTypes.GetTypeInAnyAssembly(LegacyClass) != null)
            {
                Skip("migration.countsPaintedOnly", "Perspective: Paths is loaded; nothing is adopted, by design");
                return;
            }

            List<Area> loaded = LoadAreas(LegacyAreaFixture, out string error);
            Area_HardEdges painted = loaded?.FirstOrDefault() as Area_HardEdges;
            if (error != null || painted == null)
            {
                Check(false, "migration.countsPaintedOnly", "the fixture did not load: " + (error ?? "wrong type"));
                return;
            }
            Area_HardEdges empty = new Area_HardEdges(map.areaManager) { adoptedFromOtherMod = true };

            int counted = Patch_AreaMigration.CountPainted(new List<Area> { painted, empty });
            Check(counted == 1, "migration.countsPaintedOnly",
                $"counted {counted} of one painted and one empty adopted area");
        }

        /// <summary>
        /// Loads an `areas` list from XML through the engine's own loader, in
        /// the running game: InitLoading, the deep list, FinalizeLoading. The
        /// path a player's save takes for this node, minus the rest of the
        /// save. Returns null with an error on a throw, and leaves the adoption
        /// counter at zero so no case triggers the post-load announcement.
        /// </summary>
        internal static List<Area> LoadAreas(string xml, out string error)
        {
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "neatedges-migration-fixture.xml");
            List<Area> loaded = null;
            error = null;

            try
            {
                File.WriteAllText(path, xml);
                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loaded, "areas", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception e)
            {
                Scribe.ForceStop();
                error = e.Message;
                loaded = null;
            }
            finally
            {
                Patch_AreaMigration.adopted = 0;
                Patch_AreaMigration.pending = false;
                if (File.Exists(path)) File.Delete(path);
            }

            return loaded;
        }

        // ---- yielding to Perspective: Paths ------------------------------

        /// <summary>
        /// The area tools follow Perspective: Paths: shown and painting
        /// without it, hidden and refusing every cell with it. The marker's
        /// build tool stays in both runs, because its zone has nothing like a
        /// one-sided edge.
        ///
        /// Asserted in both runs, so the default run is the control for the
        /// one with it installed (run-harness.sh --with).
        /// </summary>
        internal static void CaseYieldTools(Map map)
        {
            DesignationCategoryDef zone =
                DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Zone");
            DesignationCategoryDef floors =
                DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Floors");
            if (zone == null || floors == null)
            {
                Skip("yield.tools", "no Zone or no Floors category");
                return;
            }

            Clear();
            IntVec3 c = Origin(map);
            bool installed = PerspectivePathsInterop.Installed;

            List<Designator> tools = zone.AllResolvedDesignators
                .Where(d => d is Designator_AreaHardEdges).ToList();
            int shown = tools.Count(d => d.Visible);
            bool accepts = new Designator_AreaHardEdgesExpand().CanDesignateCell(c).Accepted;
            string detail = $"{tools.Count} tools, {shown} shown, expand accepts a cell: {accepts}";

            if (installed)
            {
                Check(tools.Count == 2 && shown == 0 && !accepts,
                    "yield.toolsHiddenWithPerspectivePaths", detail);
            }
            else
            {
                Check(tools.Count == 2 && shown == 2 && accepts,
                    "yield.toolsShownWithoutPerspectivePaths", detail);
            }

            Designator marker = floors.AllResolvedDesignators
                .FirstOrDefault(d => d is Designator_Build b && b.PlacingDef == Single);
            Check(marker != null && marker.Visible, "yield.markerStays",
                marker == null ? "no build tool for the marker on the Floors tab"
                    : "the marker's build tool is hidden");
            Clear();
        }

        /// <summary>
        /// With Perspective: Paths installed, a hard edge area on the map moves
        /// into its zone and ours goes: every tile arrives, the zone is the one
        /// its own lookup by label finds, and our model stops hardening those
        /// tiles, because its hook does that now.
        ///
        /// Twice: into the zone it made when the map finalized, and into one
        /// this mod has to make, which is the first load after adding it.
        /// Either way its zone is put back as it was afterwards.
        /// </summary>
        internal static void CaseYieldHandBack(Map map)
        {
            if (!PerspectivePathsInterop.Installed)
            {
                Skip("yield.handBack",
                    "Perspective: Paths is not loaded; run-harness.sh --with owlchemist.perspectivepaths");
                return;
            }

            HandBackOnto(map, "yield.handBackIntoItsZone", intoExisting: true);
            HandBackOnto(map, "yield.handBackMakesItsZone", intoExisting: false);
        }

        internal static void HandBackOnto(Map map, string name, bool intoExisting)
        {
            Clear();
            List<Area> areas = map.areaManager.AllAreas;
            Area original = PerspectivePathsInterop.ZoneOn(map);
            if (intoExisting && original == null)
            {
                Check(false, name, "Perspective: Paths made no zone when the map finalized");
                return;
            }
            if (!intoExisting && original != null) areas.Remove(original);

            IntVec3 o = Origin(map);
            IntVec3[] cells = { o, o + IntVec3.East, o + IntVec3.North * 2 };
            Area_HardEdges ours = Area_HardEdges.GetOrCreate(map);
            foreach (IntVec3 c in cells) ours[c] = true;

            PerspectivePathsInterop.handedBack = 0;
            int moved = PerspectivePathsInterop.HandBack(map);
            int counted = PerspectivePathsInterop.handedBack;
            PerspectivePathsInterop.handedBack = 0;

            Area zone = PerspectivePathsInterop.ZoneOn(map);
            bool oursGone = Area_HardEdges.On(map) == null;
            bool arrived = zone != null && cells.All(c => zone[c]);
            bool foundByLabel = zone != null
                && map.areaManager.GetLabeled(PerspectivePathsInterop.ZoneLabel) == zone;
            bool unhardened = cells.All(c => Patch_SidedFadeBlock.EdgeMaskAt(c, map) == 0);
            bool rightZone = intoExisting ? zone == original : zone != null && zone != original;

            Check(moved == cells.Length && counted == 1 && oursGone && arrived && foundByLabel
                    && unhardened && rightZone, name,
                $"moved {moved}, counted {counted}, ours gone: {oursGone}, all arrived: {arrived}, "
                + $"found by its label: {foundByLabel}, no longer hardened here: {unhardened}, "
                + (intoExisting ? "used its zone: " : "made a new zone: ") + rightZone);

            if (zone != null)
            {
                foreach (IntVec3 c in cells) zone[c] = false;
            }
            if (!intoExisting)
            {
                if (zone != null) areas.Remove(zone);
                if (original != null) areas.Add(original);
            }
            Clear();
        }

        // ---- NANAME Floors' diagonal tiles --------------------------------

        /// <summary>
        /// A NANAME tile drawn with a soft mask is drawn crisp where a hard edge
        /// runs along one of its edges, and soft where none does, a hard edge
        /// that only reaches a corner included (<see cref="NanameInterop"/>).
        /// Needs NANAME Floors, and Smooth Terrain for its soft masks, so it
        /// skips on the default list:
        ///
        ///     devtools/run-harness.sh --with oels.nanamefloors --with chaoticenrico.smoothterrain
        ///
        /// Every check builds one tile on sand, split with stony soil in its
        /// south-west half, hardens it one way, regenerates the section's
        /// terrain layer and reads which mask the tile's second quad was drawn
        /// with, off the submesh that holds that quad. Reading the submesh
        /// rather than asking the interop is the point: it is what the patched
        /// method actually built, through NANAME's own drawing, so a missed
        /// anchor or a stale material fails here.
        /// </summary>
        internal static void CaseNanameDiagonals(Map map)
        {
            if (!NanameInterop.Installed)
            {
                Skip("naname", "NANAME Floors is not loaded; run with --with oels.nanamefloors "
                    + "--with chaoticenrico.smoothterrain");
                return;
            }
            Check(NanameInterop.CoverApplied, "naname.coverPatch",
                NanameInterop.CoverReport ?? "the cover transpiler never ran");

            TerrainDef sand = Terrain("Sand"), gravel = Terrain("Gravel"), wood = Terrain("WoodPlankFloor");
            ThingDef diagonal = DefDatabase<ThingDef>.GetNamedSilentFail("NE_FloorBorderDiagonal");
            ThingDef border = DefDatabase<ThingDef>.GetNamedSilentFail("NE_FloorBorder");
            if (Single == null || diagonal == null || sand == null || gravel == null || wood == null)
            {
                Skip("naname.crisp", "a def is missing");
                return;
            }
            TerrainDef soft = NanameBlend("HalfBlend", sand, gravel, Rot4.South);
            TerrainDef softBig = NanameBlend("HalfBlendBig", sand, gravel, Rot4.South);
            TerrainDef hard = NanameBlend("HalfTriangle", sand, gravel, Rot4.South);
            TerrainDef softFloor = NanameBlend("HalfBlend", sand, wood, Rot4.South);
            if (soft == null || softBig == null || softFloor == null)
            {
                Skip("naname.crisp", "no soft mask to test: Smooth Terrain, which ships them, is not loaded");
                return;
            }
            if (hard == null)
            {
                Skip("naname.crisp", "NANAME Floors has no HalfTriangle mask, so there is nothing to draw crisp with");
                return;
            }

            // The first clear 5x5 out from the map's centre, rather than the
            // centre itself: a quicktest map is random, and a rock or a wall
            // there would skip every check below, which reads like coverage
            // in a report that only counts failures.
            Clear();
            IntVec3 o = IntVec3.Invalid;
            CellRect block = default;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(Origin(map), 40f, true))
            {
                CellRect around = CellRect.FromLimits(c.x - 2, c.z - 2, c.x + 2, c.z + 2);
                if (around.InBounds(map) && !Covered(map, around))
                {
                    o = c;
                    block = around;
                    break;
                }
            }
            if (!o.IsValid) { Skip("naname.crisp", "no 5x5 within 40 cells of the centre is free of edifices"); return; }

            // One tile of `def` at o, on sand, hardened by `harden`; returns
            // the materials its second quad was drawn with.
            List<Material> Build(TerrainDef def, Action harden)
            {
                Clear();
                Paint(map, block, sand);
                Paint(map, CellRect.SingleCell(o), def);
                harden();
                return CoverMaterials(map, o);
            }

            void Expect(string name, TerrainDef def, Action harden, string want)
            {
                List<string> masks = Build(def, harden).Select(MaskName).ToList();
                Check(masks.Count > 0 && masks.All(m => m == want), "naname.crisp." + name,
                    masks.Count == 0 ? "no second quad was drawn at the tile"
                        : $"drawn with {string.Join(", ", masks)}, want {want}");
                Clear();
            }

            Expect("untouched", soft, () => { }, "HalfBlend");
            Expect("markerOnTile", soft, () => Place(map, o, Single, Rot4.West), "HalfTriangle");
            Expect("markerAcross", soft, () => Place(map, o + IntVec3.West, Single, Rot4.East), "HalfTriangle");
            Expect("paintedTile", soft, () => Area_HardEdges.GetOrCreate(map)[o] = true, "HalfTriangle");
            Expect("paintedBeside", soft, () => Area_HardEdges.GetOrCreate(map)[o + IntVec3.North] = true, "HalfTriangle");
            Expect("diagonalTrim", soft, () => Place(map, o, diagonal, Rot4.North), "HalfTriangle");
            Expect("halfBlendBig", softBig, () => Place(map, o, Single, Rot4.West), "HalfTriangle");

            // A hard edge that only ends at the tile's corner: the north edge
            // of the tile diagonally south-west, which seals that corner and
            // nothing else. Nothing hard runs along the tile, so it stays soft.
            // The premise is checked first, so a change to corner sealing reads
            // as that and not as the tile's look. Crisping on the sealed corner
            // was the first rule, and in play it turned one tile of a soft
            // Smooth Terrain run into a hard line where a straight floor border
            // merely stopped at its corner: the second check is that layout.
            Build(soft, () => Place(map, o + IntVec3.SouthWest, Single, Rot4.North));
            int cornerOnly = Patch_SidedFadeBlock.EdgeMaskAt(o, map);
            Clear();
            Check(cornerOnly == (1 << 1), "naname.crisp.cornerOnlyPremise",
                $"the layout should seal only the tile's south-west corner; got {Show(cornerOnly)}");
            Expect("cornerOnly", soft, () => Place(map, o + IntVec3.SouthWest, Single, Rot4.North), "HalfBlend");
            if (border == null) Skip("naname.crisp.cornerOnlyBorder", "NE_FloorBorder is missing");
            else Expect("cornerOnlyBorder", soft, () => Place(map, o + IntVec3.SouthWest, border, Rot4.East), "HalfBlend");

            DebugTools_NeatEdges.Suppressed = true;
            try
            {
                Expect("suppressed", soft, () => Place(map, o, Single, Rot4.West), "HalfBlend");
            }
            finally
            {
                DebugTools_NeatEdges.Suppressed = false;
            }

            // A hard mask is NANAME's own business: hardened, it is drawn with
            // NANAME's material, not a copy of it.
            List<Material> hardDrawn = Build(hard, () => Place(map, o, Single, Rot4.West));
            Clear();
            Check(hardDrawn.Count > 0 && hardDrawn.All(m => MaskName(m) == "HalfTriangle" && !m.name.EndsWith("(crisp)")),
                "naname.crisp.hardMaskLeftAlone",
                $"drawn with {string.Join(", ", hardDrawn.Select(m => m.name))}");

            // Two hardened tiles of one def share one crisp material, so the
            // section still draws them in one call.
            IntVec3 other = o + IntVec3.East * 2;
            List<Material> first = Build(soft, () =>
            {
                Paint(map, CellRect.SingleCell(other), soft);
                Place(map, o, Single, Rot4.West);
                Place(map, other, Single, Rot4.West);
            });
            List<Material> second = CoverMaterials(map, other);
            Clear();
            Check(first.Count > 0 && second.Count > 0 && first.Concat(second).Distinct().Count() == 1,
                "naname.crisp.sharedMaterial",
                $"{first.Concat(second).Distinct().Count()} distinct materials for two tiles of one def");

            // A painted diagonal floor keeps its colour and its texture when it
            // turns crisp: the copy changes the mask and nothing else.
            ColorDef paint = DefDatabase<ColorDef>.AllDefs.FirstOrDefault(c => c.colorType == ColorType.Structure);
            if (paint == null)
            {
                Skip("naname.crisp.floorKeepsColour", "no structure paint colour loaded");
            }
            else
            {
                List<Material> before = Build(softFloor, () => map.terrainGrid.SetTerrainColor(o, paint));
                List<Material> after = Build(softFloor, () =>
                {
                    map.terrainGrid.SetTerrainColor(o, paint);
                    Place(map, o, Single, Rot4.West);
                });
                Clear();
                bool kept = before.Count > 0 && after.Count > 0
                    && MaskName(before[0]) == "HalfBlend" && MaskName(after[0]) == "HalfTriangle"
                    && before[0].color == after[0].color && before[0].mainTexture == after[0].mainTexture;
                Check(kept, "naname.crisp.floorKeepsColour",
                    before.Count == 0 || after.Count == 0 ? "no second quad was drawn at the tile"
                        : $"before: {MaskName(before[0])} in {before[0].color}; after: {MaskName(after[0])} in {after[0].color}");
            }
            Clear();
        }

        /// <summary>
        /// NANAME's tile of <paramref name="cover"/> over <paramref name="ground"/>
        /// through <paramref name="mask"/>, its cover in the corner
        /// <paramref name="rot"/> names, made through NANAME's own maker if no
        /// mod has made it yet. Null when NANAME or the mask is missing.
        /// </summary>
        internal static TerrainDef NanameBlend(string mask, TerrainDef ground, TerrainDef cover, Rot4 rot)
        {
            Type maskType = GenTypes.GetTypeInAnyAssembly("NanameFloors.TerrainMask");
            Type blendedType = GenTypes.GetTypeInAnyAssembly("NanameFloors.BlendedTerrainDef");
            Type utilType = GenTypes.GetTypeInAnyAssembly(NanameInterop.UtilClass);
            if (maskType == null || blendedType == null || utilType == null) return null;

            // NANAME's def reads its mask when it loads and throws on a missing one.
            if (ContentFinder<Texture2D>.Get(NanameInterop.MaskFolder + mask, false) == null) return null;

            string name = (string)HarmonyLib.AccessTools.Method(maskType, "GetDefName")
                .Invoke(null, new object[] { mask, ground, cover, rot });
            System.Reflection.MethodInfo find = typeof(DefDatabase<>).MakeGenericType(blendedType).GetMethod("GetNamedSilentFail");
            TerrainDef def = (TerrainDef)find.Invoke(null, new object[] { name });
            if (def == null)
            {
                HarmonyLib.AccessTools.Method(utilType, "MakeBlendedTerrain")
                    .Invoke(null, new[] { Activator.CreateInstance(maskType, mask, ground, cover, rot) });
                def = (TerrainDef)find.Invoke(null, new object[] { name });
            }
            return def;
        }

        /// <summary>
        /// The materials a cell's second quad was drawn with, read after
        /// regenerating its section's terrain layer: every submesh, under one
        /// of NANAME's masks, that holds the four corners of the cell in the
        /// order NANAME adds them.
        /// </summary>
        internal static List<Material> CoverMaterials(Map map, IntVec3 c)
        {
            Section section = map.mapDrawer.SectionAt(c);
            SectionLayer layer = section.GetLayer(typeof(SectionLayer_Terrain));
            section.RegenerateSingleLayer(layer);

            bool At(Vector3 v, int x, int z) => Mathf.Approximately(v.x, x) && Mathf.Approximately(v.z, z);
            List<Material> found = new List<Material>();
            foreach (LayerSubMesh sub in layer.subMeshes)
            {
                if (!sub.finalized || sub.disabled || sub.material == null || MaskName(sub.material) == null) continue;
                List<Vector3> v = sub.verts;
                for (int i = 0; i + 3 < v.Count; i++)
                {
                    if (At(v[i], c.x, c.z) && At(v[i + 1], c.x, c.z + 1)
                        && At(v[i + 2], c.x + 1, c.z + 1) && At(v[i + 3], c.x + 1, c.z))
                    {
                        found.Add(sub.material);
                        break;
                    }
                }
            }
            return found;
        }

        /// <summary>The NANAME mask a material draws through, or null if it draws through none.</summary>
        internal static string MaskName(Material material)
        {
            if (nanameMasks == null)
            {
                nanameMasks = new HashSet<string>(ContentFinder<Texture2D>
                    .GetAllInFolder(NanameInterop.MaskFolder.TrimEnd('/')).Select(t => t.name));
            }
            int id = ShaderPropertyIDs.MaskTex;
            string name = material.HasProperty(id) ? material.GetTexture(id)?.name : null;
            return name != null && nanameMasks.Contains(name) ? name : null;
        }

        internal static HashSet<string> nanameMasks;
    }
}
#endif
