using HarmonyLib;
using RimWorld;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Repaints the terrain when a fade-blocking thing is placed or removed.
    ///
    /// Without this the feature looks completely dead, which is exactly how it
    /// first presented. `SectionLayer_Terrain` declares
    /// `relevantChangeTypes = MapMeshFlagDefOf.Terrain`, but placing a BUILDING
    /// dirties Things — so the section's terrain mesh is never rebuilt and the
    /// edge mask is never re-read. The mask only took effect on a map load,
    /// when every section regenerates anyway. Place a border mid-session and
    /// nothing happened at all.
    ///
    /// `regenAdjacentCells` matters as much here as it does on the pad: this
    /// cell's own fringe changes, and so does what its neighbours draw toward
    /// it, because the gate reads both sides of every pair.
    ///
    /// DeSpawn is a PREFIX because the position has to be read while the thing
    /// still has one — `Thing.DeSpawn` clears the map reference on its way out,
    /// and a postfix would be reading a corpse. (The same ordering trap the
    /// furniture rules record for comps: PostDeSpawn runs after Thing.DeSpawn,
    /// with Map already null.)
    ///
    /// Spawning during map load is harmless: MapMeshDirty returns immediately
    /// unless ProgramState is Playing, and the full regenerate that follows the
    /// load picks the mask up regardless.
    /// </summary>
    public static class Patch_SidedFadeInvalidate
    {
        internal static void Dirty(Thing thing, Map map)
        {
            if (map == null || thing?.def == null) return;
            if (thing.def.GetModExtension<BlocksTerrainFade>() == null) return;

            map.mapDrawer?.MapMeshDirty(thing.Position,
                (ulong)MapMeshFlagDefOf.Terrain,
                regenAdjacentCells: true, regenAdjacentSections: false);
        }

        [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
        public static class OnSpawn
        {
            public static void Postfix(Thing __instance, Map map)
            {
                Dirty(__instance, map);
            }
        }

        [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
        public static class OnDeSpawn
        {
            public static void Prefix(Thing __instance)
            {
                if (!__instance.Spawned) return;
                Dirty(__instance, __instance.Map);
            }
        }
    }
}
