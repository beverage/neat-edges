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
    ///   <see cref="NanameInterop"/>              transpiler on NANAME Floors' cover method,
    ///                                            applied by hand and only while it is
    ///                                            installed; passes the method through
    ///                                            untouched if its anchor is missing
    ///   <see cref="Patch_TrimsBuildableByAll"/>  lets every ideoligion build the trims,
    ///                                            which Ideology otherwise reads as
    ///                                            ideoligion buildings
    ///
    /// The trims need no patch to draw: <see cref="Graphic_StripTrim"/> never
    /// offers its strip to the static atlas. (Until 2026-10 a patch applied from
    /// a Mod constructor before any def loaded kept the per-facing trim textures
    /// out of it.)
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        internal const string Id = "MrBeverage.NeatEdges";

        static HarmonyInit()
        {
            Harmony harmony = new Harmony(Id);
            harmony.PatchAll();
            NanameInterop.Patch(harmony);

            // One line, every start. The transpiler's worst failure produced no
            // error and no warning — it anchored onto a store that exists twice
            // and hardened nothing. Printing what it resolved, and how many
            // candidates each anchor saw, turns "it silently did nothing" into
            // something a log can be grepped for.
            Log.Message("[NeatEdges] terrain edge patch "
                + (Patch_SidedFadeBlock.Applied ? "applied" : "NOT APPLIED")
                + " — " + (Patch_SidedFadeBlock.AnchorReport ?? "transpiler never ran"));

            // And one more while NANAME Floors is installed, for the same
            // reason: a missed anchor leaves its soft tiles soft, which looks
            // exactly like the mod not being there.
            if (NanameInterop.Installed)
            {
                Log.Message("[NeatEdges] NANAME Floors diagonal tile patch "
                    + (NanameInterop.CoverApplied ? "applied" : "NOT APPLIED, so soft diagonal tiles stay soft")
                    + " — " + (NanameInterop.CoverReport ?? "transpiler never ran"));
            }
        }
    }
}
