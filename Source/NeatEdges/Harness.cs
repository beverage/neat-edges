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
            Guard("area.designators", CaseAreaDesignatorsRegistered);
            Guard("area.equalsFourEdges", () => CaseAreaEqualsFourEdges(map));
            Guard("area.clear", () => CaseAreaClearRestores(map));
            Guard("area.dirty", () => CaseAreaPaintDirtiesTerrain(map));
            Guard("area.duplicates", () => CaseAreaDuplicatesMerge(map));
            Guard("migration.resolves", CaseMigrationResolvesLegacyClass);
            Guard("migration.realNode", () => CaseMigrationLoadsRealNode(map));
            Guard("migration.roundTrip", CaseMigrationRoundTrips);

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
            Thing t = ThingMaker.MakeThing(def);
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

        // ---- the painted area -------------------------------------------

        /// <summary>
        /// Nothing is added to a map nobody painted. An empty area on every map
        /// would be a line in every save, and a load error on every map of a
        /// player who later removes the mod. Runs before any case paints.
        /// </summary>
        internal static void CaseAreaIsLazy(Map map)
        {
            Check(Area_HardEdges.On(map) == null, "area.lazy",
                "a map nobody painted already carries a hard-edge area");
        }

        /// <summary>
        /// Both tools are on the Floors tab, with icons. The patch names the
        /// classes as strings, so a rename leaves the tab without them and logs
        /// nothing at all.
        /// </summary>
        internal static void CaseAreaDesignatorsRegistered()
        {
            DesignationCategoryDef floors =
                DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Floors");
            if (floors == null) { Skip("area.designators", "no Floors category"); return; }

            Designator expand = floors.AllResolvedDesignators
                .FirstOrDefault(d => d is Designator_AreaHardEdgesExpand);
            Designator clear = floors.AllResolvedDesignators
                .FirstOrDefault(d => d is Designator_AreaHardEdgesClear);

            Check(expand != null && clear != null, "area.designators.onFloorsTab",
                $"expand found: {expand != null}, clear found: {clear != null}");

            bool iconsLoaded = expand != null && clear != null
                && expand.icon != null && expand.icon != BaseContent.BadTex
                && clear.icon != null && clear.icon != BaseContent.BadTex;
            Check(iconsLoaded, "area.designators.icons", "a tool icon did not load");
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

            Check(clearRefusesUnpainted && expandRefusesPainted, "area.tools.refuseEachOther",
                $"clear refused unpainted: {clearRefusesUnpainted}, "
                + $"expand refused painted: {expandRefusesPainted}");
            Check(stillHard == 0, "area.clearRestores",
                $"{stillHard} of 25 cells still hardened after clearing");
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
        /// With Perspective: Paths loaded, its own class wins and the postfix is
        /// inert by design, so there is nothing to assert.
        /// </summary>
        internal static void CaseMigrationResolvesLegacyClass()
        {
            if (GenTypes.GetTypeInAnyAssembly(LegacyClass) != null)
            {
                Skip("migration.resolves", "Perspective: Paths is loaded; its class wins, by design");
                return;
            }

            Type legacy = BackCompatibility.GetBackCompatibleType(typeof(Area), LegacyClass, null);
            Type control = BackCompatibility.GetBackCompatibleType(
                typeof(Area), "PerspectivePaths.Area_NoSuchArea", null);
            Patch_AreaMigration.adopted = 0;

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
                if (File.Exists(path)) File.Delete(path);
            }

            return loaded;
        }
    }
}
#endif
