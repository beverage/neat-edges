using System.Collections.Generic;
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
    ///
    /// One more is applied earlier, from <see cref="NeatEdgesMod"/>'s constructor,
    /// because the game builds each Architect tab's buttons before any static
    /// constructor runs: <see cref="Patch_TrimDesignators"/>, a postfix that
    /// takes vanilla's buttons for the trims off every tab, leaving each shape's
    /// one. This class reports on it.
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

            // Only on failure: the postfix either applies or does not, and
            // failing costs duplicate buttons, never a trim that cannot be built.
            if (!Patch_TrimDesignators.Applied)
            {
                Log.Warning("[NeatEdges] trim button patch NOT APPLIED, so every trim shape shows a second button — "
                    + (Patch_TrimDesignators.Failure ?? "the patch never ran"));
            }

            WarnTrimsMarkedIdeoligionBuildings();
        }

        /// <summary>
        /// A trim that sets canGenerateDefaultDesignator false, as Neat Edges'
        /// own did through v1.1.3 and the README then asked other mods to, is an
        /// ideoligion building to Ideology: Ideo.MembersCanBuild refuses it to
        /// every ideoligion outside classic mode without a precept for it, and
        /// the one a game without the DLC gives every pawn is not in classic
        /// mode. It still joins its shape's button; nothing here makes it
        /// buildable, so the log names it.
        /// </summary>
        internal static void WarnTrimsMarkedIdeoligionBuildings()
        {
            var named = new List<string>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!def.canGenerateDefaultDesignator && def.GetModExtension<TrimPiece>() != null)
                {
                    named.Add(def.defName + (def.modContentPack != null ? " (" + def.modContentPack.Name + ")" : ""));
                }
            }
            if (named.Count == 0) return;
            Log.Warning("[NeatEdges] these trims set canGenerateDefaultDesignator to false, which Ideology reads "
                + "as a building only some ideoligions may raise, so most colonists will refuse to build them: "
                + string.Join(", ", named) + ". Remove the field: Neat Edges keeps a trim's own button off the tab "
                + "by itself, and puts the trim on its shape's button.");
        }
    }
}
