using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Loads another mod's saved hard-edge areas as ours, once that mod is gone.
    ///
    /// Perspective: Paths stores its per-tile override as an `Area` subclass,
    /// `PerspectivePaths.Area_InvertEdges`, and the original and the continued
    /// release both write that one class name. Remove the mod with nothing to
    /// adopt it and every map that carries one loses its WHOLE area list, home
    /// area included (DESIGN §8). That is every map it was ever loaded on: it
    /// adds its zone to each map at finalization, painted or not, and the zone
    /// is not player-deletable, so clearing its tiles, as its own FAQ advises,
    /// leaves the zone in the save.
    ///
    /// Its saved node is a plain `Area`: an ID and a grid, nothing else. So is
    /// <see cref="Area_HardEdges"/>'s. Answering the type lookup with our class
    /// is therefore the whole migration. No converter, no field mapping.
    ///
    /// WHY HERE. `ScribeExtractor.SaveableFromNode` resolves EVERY deep-saved
    /// object's class through this method, not only the missing ones, and the
    /// method ends in a plain `GenTypes` lookup. A postfix sees the final answer
    /// on every branch, including the one the engine takes when the save matches
    /// the current environment exactly.
    ///
    /// It acts only on a null result. With Perspective: Paths still installed
    /// its own class resolves first and nothing here fires. That direction,
    /// both mods installed, is <see cref="PerspectivePathsInterop"/>'s: this
    /// mod leaves whole-tile painting to its zone then.
    ///
    /// Rejected: declaring a `PerspectivePaths.Area_InvertEdges` of our own, the
    /// usual continued-mod trick. It squats another author's namespace, and it
    /// collides with the real class whenever both mods are loaded, unless it is
    /// gated behind a conditional load folder, which then owns all of this mod's
    /// folder resolution.
    ///
    /// Called once per deep-saved object on every load, so the first check is
    /// the cheap one.
    /// </summary>
    [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.GetBackCompatibleType))]
    public static class Patch_AreaMigration
    {
        /// <summary>
        /// Saved class names we answer for. A table rather than one comparison,
        /// because it will not stay one: if this mechanism ever moves into
        /// another mod, a save carrying `NeatEdges.Area_HardEdges` needs exactly
        /// this treatment, and that should be one more row.
        /// </summary>
        internal static readonly Dictionary<string, Type> Adopted = new Dictionary<string, Type>
        {
            { PerspectivePathsInterop.AreaClass, typeof(Area_HardEdges) },
        };

        /// <summary>
        /// How many nodes were answered since the last announcement. Counted here
        /// rather than in the area, because resolving the type is the one step
        /// that knows the node came from somewhere else.
        /// </summary>
        internal static int adopted;

        /// <summary>
        /// Set when a node is answered, and taken by the area the scribe builds
        /// next (<see cref="Area_HardEdges"/>'s parameterless constructor):
        /// `ScribeExtractor.SaveableFromNode` resolves the type and then
        /// instantiates it, with nothing between. That is how an adopted area is
        /// told apart from one this mod saved, which share a class. Cleared at
        /// every map finalization, so a lookup with no construction after it
        /// (the harness makes those) cannot mark a later area.
        /// </summary>
        internal static bool pending;

        /// <summary>Adopted areas with painted tiles, since the last announcement.</summary>
        internal static int adoptedPainted;

        internal static bool announceQueued;

        public static void Postfix(string providedClassName, ref Type __result)
        {
            if (__result != null || providedClassName == null) return;
            if (!Adopted.TryGetValue(providedClassName, out Type type)) return;

            __result = type;
            adopted++;
            pending = true;
        }

        /// <summary>
        /// Counts this map's adopted areas that have painted tiles. Called at
        /// map finalization before the merge and the empty-area drop, which
        /// would lose the mark or the area.
        ///
        /// Only painted ones count, because Perspective: Paths adds an empty
        /// zone to every map: counting every adoption would tell a player their
        /// areas came across on maps where they never painted, and those empty
        /// areas are dropped a moment later anyway.
        /// </summary>
        internal static void CountPainted(Map map)
        {
            pending = false;
            List<Area> areas = map?.areaManager?.AllAreas;
            adoptedPainted += CountPainted(areas);

            // Each area is counted once: a later finalization of the same map
            // must not announce it again.
            if (areas == null) return;
            foreach (Area area in areas)
            {
                if (area is Area_HardEdges hard) hard.adoptedFromOtherMod = false;
            }
        }

        internal static int CountPainted(IEnumerable<Area> areas)
        {
            int count = 0;
            if (areas == null) return count;
            foreach (Area area in areas)
            {
                if (area is Area_HardEdges hard && hard.adoptedFromOtherMod && hard.TrueCount > 0) count++;
            }
            return count;
        }

        /// <summary>
        /// Tells the player once, after the load, that the areas they painted
        /// came across. The tools are on the Zone tab, where Perspective: Paths
        /// kept its own, but without this the only evidence the migration ran
        /// is that nothing broke. Silent when nothing painted came across.
        ///
        /// Deferred until the loading event finishes, so every map has been
        /// counted first, and because a message plays a sound.
        /// </summary>
        internal static void AnnounceAdopted()
        {
            adopted = 0;
            if (adoptedPainted == 0 || announceQueued) return;

            announceQueued = true;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                int count = adoptedPainted;
                adoptedPainted = 0;
                announceQueued = false;

                Log.Message("[NeatEdges] adopted " + count + " painted hard-edge area(s) saved by "
                    + "another mod; they load as this mod's painted area.");
                Messages.Message("NeatEdges.AdoptedAreas".Translate(),
                    MessageTypeDefOf.NeutralEvent, historical: false);
            });
        }
    }
}
