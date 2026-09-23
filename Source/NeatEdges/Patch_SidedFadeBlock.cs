using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Lets a placed thing harden SPECIFIC edges of its own cell.
    ///
    /// WHY A TRANSPILER AND NOT A REPLACEMENT BODY
    ///
    /// This was a prefix returning false — vanilla's `Regenerate` copied out and
    /// modified. That is hostile to every other mod on the same method, and it
    /// broke one in play: **Dub's Paint Shop transpiles `Regenerate`**, rewriting
    /// each `callvirt GetMaterialFor` into a paint-aware lookup. A prefix that
    /// returns false skips the original, and a transpiler only edits the
    /// original — so every painted floor in the colony silently lost its colour.
    ///
    /// Transpilers compose: Harmony chains them, each seeing the previous one's
    /// output. Our anchors and theirs are disjoint (they match
    /// `callvirt GetMaterialFor`; we match the neighbour store and the colour
    /// ternary), so both survive in either order.
    ///
    /// Their transpiler emits a hardcoded `ldloc.S 6` for the cell. New locals
    /// declared here are APPENDED by `DeclareLocal`, so index 6 keeps its
    /// meaning — do not renumber or reorder existing locals.
    ///
    /// WHAT IS INSERTED
    ///
    ///   1. at the first `TerrainAt(item)` — compute this cell's edge mask once
    ///      and stash it in a fresh local
    ///   2. at the neighbour store `array[i] = cellTerrain2` — swap a hardened
    ///      direction's terrain for `Underwall`
    ///   3. at the colour ternary's `array2[l]` read — force sealed corner
    ///      vertices dark
    ///
    /// (2) is the substitution vanilla already performs for walls: `Underwall`
    /// is Hard at precedence 0, so that direction fails the gate AND matches
    /// nothing in the mask loop, while every other slot keeps the value vanilla
    /// computed — which is what preserves neighbouring edges' shared wedges.
    ///
    /// (3) exists because substitution removes a SOURCE but cannot clear a
    /// VERTEX: a cardinal match lights three rim verts, so an open neighbouring
    /// cardinal re-lights the very corner a hardened edge just sealed.
    ///
    /// FAILURE IS ALL-OR-NOTHING. If any anchor is missing — a game update moved
    /// the ground under us — the instructions pass through untouched and we log
    /// once. Vanilla rendering, not half-applied rendering.
    /// </summary>
    [HarmonyPatch(typeof(SectionLayer_Terrain), nameof(SectionLayer_Terrain.Regenerate))]
    public static class Patch_SidedFadeBlock
    {
        /// <summary>Cardinal slots in GenAdj.AdjacentCellsAroundBottom: S, W, N, E.</summary>
        internal const int AllCardinals = (1 << 0) | (1 << 2) | (1 << 4) | (1 << 6);

        /// <summary>Odd bits — the diagonals, which are the pinnable vertices.</summary>
        internal const int AllDiagonals = 0xAA;

        internal static readonly AccessTools.FieldRef<MapDrawLayer, Map> MapOf =
            AccessTools.FieldRefAccess<MapDrawLayer, Map>("map");

        internal static readonly AccessTools.FieldRef<SectionLayer, Section> SectionOf =
            AccessTools.FieldRefAccess<SectionLayer, Section>("section");

        /// <summary>
        /// Own-mask memo for the section currently regenerating. Set by the
        /// prefix, cleared by the finalizer, read by the inserted calls.
        ///
        /// ThreadStatic because the lifetime is "one call to Regenerate" and
        /// nothing here should assume the engine keeps mesh regeneration on one
        /// thread forever. Null is always safe — the mask computes directly.
        /// </summary>
        [System.ThreadStatic]
        internal static OwnMaskCache sectionCache;

        internal static bool warned;

        /// <summary>
        /// What the transpiler resolved, for the harness and the log to assert
        /// on. Null until patching runs.
        ///
        /// This exists because of a failure that produced NO error and NO
        /// warning: the neighbour-store anchor matched the FIRST of two
        /// identical stores — the out-of-bounds early-continue — so the patch
        /// applied perfectly and hardened nothing anywhere a player would look.
        /// Guards cannot catch that; an anchor that matches a plausible wrong
        /// instruction passes every structural test. Reporting how many
        /// candidates each anchor saw is what makes the ambiguity visible.
        /// </summary>
        public static string AnchorReport => anchorReport;

        /// <summary>True only if all three insertions were made.</summary>
        public static bool Applied => applied;

        // Backing fields rather than an auto-property with a restricted
        // setter: an auto-property's compiler-generated backing field is
        // always private, whatever the property declares, and this codebase
        // is internal-by-default. Read through the two properties above;
        // write through these.
        internal static string anchorReport;
        internal static bool applied;

        // ------------------------------------------------------------------
        // Cache lifetime. This prefix does NOT skip the original — it returns
        // void deliberately, so it can never block another mod's patch the way
        // the old prefix-false did.
        // ------------------------------------------------------------------

        public static void Prefix(SectionLayer_Terrain __instance)
        {
            Map map = MapOf(__instance);
            Section section = SectionOf(__instance);
            if (map == null || section == null) return;

            // Expanded by 1: sealing a corner reads the neighbours just outside
            // the rect. Cells beyond it fall through to a direct compute, so the
            // bound is an optimisation, not a correctness claim.
            sectionCache = new OwnMaskCache(map, section.CellRect.ExpandedBy(1));
        }

        /// <summary>
        /// Release the memo. A postfix rather than a finalizer on purpose: a
        /// finalizer makes Harmony wrap the method in try/finally, changing
        /// exception semantics for every OTHER mod patching it too. If
        /// regeneration throws, the stale memo is simply overwritten by the next
        /// call — harmless, and not worth altering the method's shape for.
        /// </summary>
        public static void Postfix()
        {
            sectionCache = null;
        }

        // ------------------------------------------------------------------
        // The three inserted calls.
        // ------------------------------------------------------------------

        /// <summary>
        /// (1) This cell's full edge mask, computed once per cell.
        ///
        /// The suppression check is first and deliberately cheap — a zero mask
        /// means no substitution and no pinning, so the terrain renders exactly
        /// as vanilla while every marker stays where it is. See
        /// <see cref="DebugTools_NeatEdges"/>.
        /// </summary>
        public static int MaskForCell(IntVec3 cell, SectionLayer_Terrain layer)
        {
            if (DebugTools_NeatEdges.Suppressed) return 0;

            Map map = MapOf(layer);
            return map == null ? 0 : EdgeMaskAt(cell, map, sectionCache);
        }

        /// <summary>
        /// (2) Swap a hardened direction's neighbour for `Underwall`, exactly as
        /// vanilla does for a wall that covers its floor.
        /// </summary>
        public static CellTerrain Substitute(CellTerrain neighbour, int dir, int mask)
        {
            if ((mask & (1 << dir)) != 0)
            {
                neighbour.def = TerrainDefOf.Underwall;
            }
            return neighbour;
        }

        /// <summary>
        /// (3) A rim vertex stays lit only if its corner is not sealed. Cardinals
        /// are untouched, so a neighbouring tile keeps its own fade — it simply
        /// stops wrapping around the corner.
        /// </summary>
        public static bool AllowVert(bool lit, int vert, int mask)
        {
            if (!lit) return false;
            return (mask & AllDiagonals & (1 << vert)) == 0;
        }

        // ------------------------------------------------------------------
        // Transpiler
        // ------------------------------------------------------------------

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            List<CodeInstruction> code = instructions.ToList();

            MethodInfo terrainAt = AccessTools.Method(
                typeof(TerrainGrid), nameof(TerrainGrid.TerrainAt),
                new[] { typeof(IntVec3) });

            // (1) The first TerrainAt(item) in the cell loop. `item` is loaded
            //     immediately before it, which is how we learn its local.
            int atTerrainAt = code.FindIndex(c => c.Calls(terrainAt));

            // (2) The store into the CellTerrain[] neighbour array — but NOT the
            //     first one. Vanilla stores into that array twice:
            //
            //         if (!c.InBounds(map)) { array[i] = cellTerrain; continue; }
            //         ...
            //         array[i] = cellTerrain2;      // <- this one
            //
            //     Taking the first match anchored the substitution onto the
            //     out-of-bounds early-continue path, which only runs at the map
            //     border — so the patch applied cleanly and did nothing anywhere
            //     a player would look. `GetEdifice` sits between the two and is
            //     called exactly once, which disambiguates them.
            MethodInfo getEdifice = AccessTools.Method(
                typeof(GridsUtility), nameof(GridsUtility.GetEdifice),
                new[] { typeof(IntVec3), typeof(Map) });

            int atEdifice = code.FindIndex(c => c.Calls(getEdifice));

            int atStore = atEdifice >= 0 && atEdifice + 1 < code.Count
                ? code.FindIndex(atEdifice + 1, c =>
                    (c.opcode == OpCodes.Stelem || c.opcode == OpCodes.Stobj)
                    && c.operand is System.Type t && t == typeof(CellTerrain))
                : -1;

            // (3) The colour ternary's read of array2 — a byte-array load, and
            //     the first one after the neighbour store.
            int atVertRead = atStore >= 0 && atStore + 1 < code.Count
                ? code.FindIndex(atStore + 1, c => c.opcode == OpCodes.Ldelem_U1)
                : -1;

            bool ok = atTerrainAt > 0 && atStore > 2 && atVertRead > 0;

            // The two locals we must read back: `item` (the cell) sits just
            // before TerrainAt; the neighbour index sits two slots before the
            // store, under the value being stored.
            int itemLocal = ok ? LocalIndex(code[atTerrainAt - 1]) : -1;
            int dirLocal = ok ? LocalIndex(code[atStore - 2]) : -1;
            int vertLocal = ok ? LocalIndex(code[atVertRead - 1]) : -1;

            // How many stores looked like candidates. More than one means the
            // anchor is ambiguous, which is how the out-of-bounds early-continue
            // path got patched instead of the real one.
            int storeCandidates = code.Count(c =>
                (c.opcode == OpCodes.Stelem || c.opcode == OpCodes.Stobj)
                && c.operand is System.Type st && st == typeof(CellTerrain));

            anchorReport = string.Format(
                "mask@{0} store@{1} (of {2} candidates, after GetEdifice@{3}) "
                + "vert@{4} | locals: item={5} dir={6} vert={7}",
                atTerrainAt, atStore, storeCandidates, atEdifice, atVertRead,
                itemLocal, dirLocal, vertLocal);

            if (!ok || itemLocal < 0 || dirLocal < 0 || vertLocal < 0)
            {
                applied = false;
                if (!warned)
                {
                    warned = true;
                    Log.Warning("[NeatEdges] Could not find the SectionLayer_Terrain."
                        + "Regenerate anchors; hard edges are disabled and terrain "
                        + "renders exactly as vanilla. This usually means a game "
                        + "update changed the method. " + AnchorReport);
                }
                return code;
            }

            applied = true;

            LocalBuilder mask = il.DeclareLocal(typeof(int));

            List<CodeInstruction> result = new List<CodeInstruction>(code.Count + 12);

            for (int i = 0; i < code.Count; i++)
            {
                // (3) …ldloc array2; ldloc l; ldelem.u1  ->  + AllowVert(l, mask)
                if (i == atVertRead)
                {
                    result.Add(code[i]);
                    result.Add(new CodeInstruction(OpCodes.Ldloc, vertLocal));
                    result.Add(new CodeInstruction(OpCodes.Ldloc, mask.LocalIndex));
                    result.Add(new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(Patch_SidedFadeBlock),
                            nameof(AllowVert))));
                    continue;
                }

                // (2) …ldloc array; ldloc i; ldloc cellTerrain2; [stelem]
                //     -> insert before the store, transforming the value.
                if (i == atStore)
                {
                    result.Add(new CodeInstruction(OpCodes.Ldloc, dirLocal));
                    result.Add(new CodeInstruction(OpCodes.Ldloc, mask.LocalIndex));
                    result.Add(new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(Patch_SidedFadeBlock),
                            nameof(Substitute))));
                    result.Add(code[i]);
                    continue;
                }

                result.Add(code[i]);

                // (1) …ldloc item; [callvirt TerrainAt]  -> stash the mask after
                //     the call site's argument is known to be live.
                if (i == atTerrainAt)
                {
                    result.Add(new CodeInstruction(OpCodes.Ldloc, itemLocal));
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(Patch_SidedFadeBlock),
                            nameof(MaskForCell))));
                    result.Add(new CodeInstruction(OpCodes.Stloc, mask.LocalIndex));
                }
            }

            return result;
        }

        /// <summary>
        /// The local slot a load instruction reads, or -1 if it is not one.
        /// Roslyn emits the short forms freely, so all of them have to be read.
        /// </summary>
        internal static int LocalIndex(CodeInstruction instruction)
        {
            OpCode op = instruction.opcode;

            if (op == OpCodes.Ldloc_0) return 0;
            if (op == OpCodes.Ldloc_1) return 1;
            if (op == OpCodes.Ldloc_2) return 2;
            if (op == OpCodes.Ldloc_3) return 3;

            if (op == OpCodes.Ldloc || op == OpCodes.Ldloc_S || op == OpCodes.Ldloca
                || op == OpCodes.Ldloca_S)
            {
                if (instruction.operand is LocalBuilder lb) return lb.LocalIndex;
                if (instruction.operand is int n) return n;
                if (instruction.operand is byte b) return b;
            }

            return -1;
        }

        // ------------------------------------------------------------------
        // Mask computation — plain C#, unchanged by the move to a transpiler.
        // ------------------------------------------------------------------

        /// <summary>
        /// The full mask for a cell: hardened cardinals (from either side of each
        /// edge), the diagonals those flank, and any diagonal whose corner a
        /// neighbour has already sealed.
        /// </summary>
        internal static int EdgeMaskAt(IntVec3 cell, Map map, OwnMaskCache cache = null)
        {
            int mask = BaseMaskAt(cell, map, cache);

            for (int k = 0; k < 8; k += 2)
            {
                if ((mask & (1 << k)) == 0) continue;
                mask |= 1 << ((k + 7) % 8);
                mask |= 1 << ((k + 1) % 8);
            }

            for (int d = 1; d < 8; d += 2)
            {
                if ((mask & (1 << d)) != 0) continue;

                int p = (d + 7) % 8;
                int q = (d + 1) % 8;

                IntVec3 pc = cell + GenAdj.AdjacentCellsAroundBottom[p];
                if (pc.InBounds(map) && EdgeHardened(pc, q, map, cache))
                {
                    mask |= 1 << d;
                    continue;
                }

                IntVec3 qc = cell + GenAdj.AdjacentCellsAroundBottom[q];
                if (qc.InBounds(map) && EdgeHardened(qc, p, map, cache))
                {
                    mask |= 1 << d;
                }
            }

            return mask;
        }

        /// <summary>
        /// Is the boundary on <paramref name="dir"/> side of this cell hardened,
        /// from EITHER side? An edge is shared by two cells, not owned by one —
        /// which is what makes it not matter which tile you place the marker on,
        /// and what seals the outside corners of a boxed-in terrain patch.
        /// </summary>
        internal static bool EdgeHardened(IntVec3 cell, int dir, Map map,
            OwnMaskCache cache)
        {
            if ((OwnMaskAt(cell, map, cache) & (1 << dir)) != 0) return true;

            IntVec3 across = cell + GenAdj.AdjacentCellsAroundBottom[dir];
            if (!across.InBounds(map)) return false;

            return (OwnMaskAt(across, map, cache) & (1 << ((dir + 4) % 8))) != 0;
        }

        internal static int OwnMaskAt(IntVec3 cell, Map map, OwnMaskCache cache)
        {
            return cache != null ? cache.MaskAt(cell) : ComputeOwnMask(cell, map);
        }

        internal static int BaseMaskAt(IntVec3 cell, Map map, OwnMaskCache cache = null)
        {
            int mask = 0;
            for (int k = 0; k < 8; k += 2)
            {
                if (EdgeHardened(cell, k, map, cache)) mask |= 1 << k;
            }
            return mask;
        }

        /// <summary>
        /// Cardinals hardened by this cell alone: the markers standing on it,
        /// plus all four if it is in the painted area. No neighbour awareness at
        /// all, which is what stops the recursion. The only function that reads
        /// the thing grid or the area, and the only one
        /// <see cref="OwnMaskCache"/> memoises.
        ///
        /// This overload resolves the area itself, which is a scan of the map's
        /// area list: fine for a one-off question, wrong in a per-cell loop.
        /// Loops hold the area, as the cache does.
        /// </summary>
        internal static int ComputeOwnMask(IntVec3 cell, Map map)
        {
            return ComputeOwnMask(cell, map, Area_HardEdges.On(map));
        }

        /// <summary>
        /// A painted tile is four hardened edges, and deliberately nothing more:
        /// it enters the model as own-mask bits, so two-sided hardening, corner
        /// sealing and pinning apply to the area's outline by the same rules
        /// the markers are tested against.
        /// </summary>
        internal static int ComputeOwnMask(IntVec3 cell, Map map, Area_HardEdges area)
        {
            int mask = MarkerMask(cell, map);
            if (area != null && area[cell]) mask |= AllCardinals;
            return mask;
        }

        /// <summary>
        /// Cardinals hardened by the markers standing on this cell, and by
        /// nothing else. The overlay asks this rather than the own mask: it
        /// answers "where is the thing I would click", and the painted area has
        /// its own overlay.
        /// </summary>
        internal static int MarkerMask(IntVec3 cell, Map map)
        {
            List<Thing> things = cell.GetThingList(map);
            int mask = 0;

            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                BlocksTerrainFade ext = thing.def?.GetModExtension<BlocksTerrainFade>();
                if (ext == null) continue;

                if (ext.edges.NullOrEmpty())
                {
                    mask |= AllCardinals;
                    continue;
                }

                int rot = thing.Rotation.AsInt;
                for (int e = 0; e < ext.edges.Count; e++)
                {
                    // Rot4 is N=0,E=1,S=2,W=3; the adjacency array is
                    // S=0,SW=1,W=2,NW=3,N=4,NE=5,E=6,SE=7. Hence *2 + 4.
                    mask |= 1 << (((rot + ext.edges[e]) * 2 + 4) % 8);
                }
            }

            return mask;
        }
    }
}
