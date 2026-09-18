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

            CaseTranspilerApplied();
            CaseDefsPresent();
            CaseRotationMapping(map);
            CaseSingleEdgeIsThree(map);
            CaseCornerIsFive(map);
            CaseRunnerIsSix(map);
            CaseAllSidesIsEight(map);
            CaseHardeningIsTwoSided(map);
            CaseOutsideCornerSeals(map);
            CaseStackingCombines(map);

            Report.AppendLine($"result: {Passed} passed, {Failed} failed, {Skipped} skipped");
            Log.Message(Report.ToString());
            return Failed == 0;
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

        internal static ThingDef AllSides =>
            DefDatabase<ThingDef>.GetNamedSilentFail("NE_HardEdgeAll");

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
            Check(AllSides != null, "defs.allSides", "NE_HardEdgeAll missing");
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

        internal static void CaseAllSidesIsEight(Map map)
        {
            if (AllSides == null) { Skip("mask.allSides", "def missing"); return; }

            Clear();
            IntVec3 c = Origin(map);
            Place(map, c, AllSides, Rot4.North);

            int mask = Patch_SidedFadeBlock.EdgeMaskAt(c, map);

            Check(mask == 0xFF, "mask.allSides.eight",
                $"got {Show(mask)} ({Bits(mask)}), expected all 8");
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
        /// combination the engine refuses. Four stacked must equal all-sides.
        /// </summary>
        internal static void CaseStackingCombines(Map map)
        {
            if (Single == null) { Skip("mask.stacking", "def missing"); return; }

            Clear();
            IntVec3 c = Origin(map);
            Place(map, c, Single, Rot4.North);
            Place(map, c, Single, Rot4.East);
            Place(map, c, Single, Rot4.South);
            Place(map, c, Single, Rot4.West);

            int mask = Patch_SidedFadeBlock.EdgeMaskAt(c, map);

            Check(mask == 0xFF, "mask.stacking.equalsAllSides",
                $"four stacked got {Show(mask)} ({Bits(mask)}), expected all 8");
            Clear();
        }
    }
}
#endif
