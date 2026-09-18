using LudeonTK;
using RimWorld;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Turn the hardening off without removing anything, so the same camera can
    /// shoot both states.
    ///
    /// THIS SHIPS, and that is a considered call rather than an oversight. The
    /// constellation's bar for the debug menu is DESTRUCTIVENESS, not
    /// reachability — Shift Change ships five `[TweakValue]` fields on the same
    /// reasoning, because they are how a player gets walked through diagnosing a
    /// report. This toggle destroys nothing, persists nothing, and is undone by
    /// pressing it again. "Is this mod doing anything, and what?" is a question
    /// a player genuinely asks, and this is the only way to answer it, since a
    /// hard edge is invisible by design.
    ///
    /// It is deliberately NOT the old Fine Establishments version. That one
    /// postfixed `TerrainGrid.FoundationAt` and was gated behind SCENES, because
    /// the pad's mechanism lived inside the engine's own gate and the patch
    /// carried a per-cell cost we did not want shipped. Ours is our own code, so
    /// suppression is a single early return.
    ///
    /// WHAT IT DOES NOT SUPPRESS, on purpose: the overlay. That reads
    /// `ComputeOwnMask` — which markers are standing on a cell — while this
    /// short-circuits `MaskForCell`, the mask the renderer consumes. So the two
    /// compose: you can shoot the "before" with every edge still highlighted,
    /// which is exactly the frame that makes a comparison legible.
    /// </summary>
    public static class DebugTools_NeatEdges
    {
        /// <summary>
        /// Read once per cell during section regeneration, before anything else
        /// in <see cref="Patch_SidedFadeBlock.MaskForCell"/>. A static bool read
        /// is not worth gating behind a build symbol.
        /// </summary>
        public static bool Suppressed;

        [DebugAction("Neat Edges", "Toggle hard edges (compare)",
            actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void ToggleSuppression()
        {
            Suppressed = !Suppressed;

            // Every cached section mesh was built under the old answer.
            Find.CurrentMap?.mapDrawer
                ?.WholeMapChanged((ulong)MapMeshFlagDefOf.Terrain);

            Messages.Message(
                Suppressed
                    ? "Hard edges suppressed — rendering only, nothing removed."
                    : "Hard edges active.",
                MessageTypeDefOf.SilentInput, historical: false);
        }
    }
}
