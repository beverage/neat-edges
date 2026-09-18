using HarmonyLib;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Applies the mod's patches once at game start.
    ///
    /// Three of the four are additive and degrade to vanilla if they throw: the
    /// overlay toggle, the mesh invalidation on spawn/despawn, and (in Fine
    /// Establishments' build) the mouseover line. The fourth,
    /// <see cref="Patch_SidedFadeBlock"/>, is a REPLACEMENT body for
    /// `SectionLayer_Terrain.Regenerate` — it carries its own fail-safe, and
    /// returns control to vanilla rather than throwing if the mesh API it needs
    /// is not where it expects.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            new Harmony("MrBeverage.NeatEdges").PatchAll();

            // One line, every start. The transpiler's worst failure produced no
            // error and no warning — it anchored onto a store that exists twice
            // and hardened nothing. Printing what it resolved, and how many
            // candidates each anchor saw, turns "it silently did nothing" into
            // something a log can be grepped for.
            Log.Message("[NeatEdges] terrain edge patch "
                + (Patch_SidedFadeBlock.Applied ? "applied" : "NOT APPLIED")
                + " — " + (Patch_SidedFadeBlock.AnchorReport ?? "transpiler never ran"));
        }
    }
}
