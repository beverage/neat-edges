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
    /// release both write that one class name. Remove the mod today and every
    /// map that carries one logs "Could not find class" on load and loses the
    /// area; its own FAQ tells players to clear all areas before removing it.
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

        public static void Postfix(string providedClassName, ref Type __result)
        {
            if (__result != null || providedClassName == null) return;
            if (!Adopted.TryGetValue(providedClassName, out Type type)) return;

            __result = type;
            adopted++;
        }

        /// <summary>
        /// Tells the player once, after the load, that the areas came across.
        /// The tools are on the Zone tab, where Perspective: Paths kept its
        /// own, but without this the only evidence the migration ran is that
        /// nothing broke.
        ///
        /// Deferred, because map finalization can run inside the loading event,
        /// and a message plays a sound.
        /// </summary>
        internal static void AnnounceAdopted()
        {
            if (adopted == 0) return;

            int count = adopted;
            adopted = 0;

            Log.Message("[NeatEdges] adopted " + count + " hard-edge area(s) saved by "
                + "another mod; they load as this mod's painted area.");

            LongEventHandler.ExecuteWhenFinished(() => Messages.Message(
                "NeatEdges.AdoptedAreas".Translate(),
                MessageTypeDefOf.NeutralEvent, historical: false));
        }
    }
}
