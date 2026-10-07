using HarmonyLib;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Applies the mod's patches once at game start. None replaces a method:
    ///
    ///   <see cref="Patch_SidedFadeBlock"/>       transpiler on SectionLayer_Terrain.Regenerate;
    ///                                            passes the method through untouched if any
    ///                                            anchor is missing
    ///   <see cref="Patch_SidedFadeInvalidate"/>  repaints terrain when a marker spawns or goes
    ///   <see cref="Patch_EdgeOverlayToggle"/>    the bottom-right overlay toggle
    ///   <see cref="Patch_AreaMigration"/>        answers a save's type lookup for Perspective:
    ///                                            Paths' area, and only when nothing else did
    ///
    /// One more goes in earlier, from <see cref="NeatEdgesMod"/>'s constructor:
    /// <see cref="Patch_TrimAtlas"/>, which keeps the trims' textures out of the
    /// static atlas and has to be in place before the first def's graphic is
    /// built, long before this runs.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        internal const string Id = "MrBeverage.NeatEdges";

        static HarmonyInit()
        {
            new Harmony(Id).PatchAll();

            // One line, every start. The transpiler's worst failure produced no
            // error and no warning — it anchored onto a store that exists twice
            // and hardened nothing. Printing what it resolved, and how many
            // candidates each anchor saw, turns "it silently did nothing" into
            // something a log can be grepped for.
            Log.Message("[NeatEdges] terrain edge patch "
                + (Patch_SidedFadeBlock.Applied ? "applied" : "NOT APPLIED")
                + " — " + (Patch_SidedFadeBlock.AnchorReport ?? "transpiler never ran")
                + "; trims " + (Patch_TrimAtlas.applied ? "kept out of" : "NOT kept out of")
                + " the texture atlas");
        }
    }
}
