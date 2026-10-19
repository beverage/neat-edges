using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Keeps vanilla's own buttons for the trims off every Architect tab, so
    /// each shape is the one button <see cref="Designator_TrimShape"/> makes.
    ///
    /// The trims are ordinary buildables: canGenerateDefaultDesignator is left
    /// true, because Ideology reads false as the mark of a building only some
    /// ideoligions may raise (v1.1.0 to v1.1.2 shipped trims no colonist would
    /// build without the DLC, nor in most games with it). With the flag true,
    /// DesignationCategoryDef.ResolveDesignators makes a button for every trim,
    /// one dropdown per designatorDropdown group, and this postfix takes them
    /// out again: a lone Designator_Build of a trim, and every trim inside a
    /// plain Designator_Dropdown, the dropdown going too once it is empty. Only
    /// those exact types, the two vanilla makes there, so the shape buttons
    /// (a Designator_Dropdown subclass) and other mods' special designators are
    /// never touched.
    ///
    /// Keyed on <see cref="TrimPiece"/>, so another mod's trims leave the tabs
    /// the same way and appear only on their shape's button.
    ///
    /// The method runs once per tab at startup, from a queue of finishing work
    /// that is filled before any static constructor runs, so the patch is
    /// applied from <see cref="NeatEdgesMod"/>, whose constructor runs before
    /// any def loads. It runs again whenever anything calls a tab's
    /// ResolveReferences, as Adaptive Storage - Global Settings does on every
    /// new game and load, and the postfix runs inside every one of those
    /// rebuilds. If the patch ever fails to apply, every shape shows twice:
    /// vanilla's dropdown beside ours. Nothing becomes unbuildable.
    /// </summary>
    public static class Patch_TrimDesignators
    {
        /// <summary>Whether the postfix is on ResolveDesignators.</summary>
        internal static bool Applied;

        /// <summary>Why it is not, when it is not.</summary>
        internal static string Failure;

        /// <summary>
        /// What the latest rebuild of each tab took out, by its defName: the
        /// trims, and the buttons that held them.
        /// </summary>
        internal static readonly Dictionary<string, Stripped> Last = new Dictionary<string, Stripped>();

        internal struct Stripped
        {
            internal int trims;
            internal int buttons;
            internal int rebuilds;
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(DesignationCategoryDef), "ResolveDesignators");
                if (target == null)
                {
                    Failure = "DesignationCategoryDef.ResolveDesignators not found";
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(Patch_TrimDesignators), nameof(Postfix)));
                Applied = true;
            }
            catch (Exception ex)
            {
                Failure = ex.GetType().Name + ": " + ex.Message;
            }
        }

        public static void Postfix(DesignationCategoryDef __instance)
        {
            if (__instance == null) return;
            Last.TryGetValue(__instance.defName, out Stripped before);
            Stripped now = Strip(__instance.AllResolvedDesignators);
            now.rebuilds = before.rebuilds + 1;
            Last[__instance.defName] = now;
        }

        internal static Stripped Strip(List<Designator> list)
        {
            var result = new Stripped();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Designator designator = list[i];
                if (designator.GetType() == typeof(Designator_Build))
                {
                    if (!IsTrim(designator)) continue;
                    list.RemoveAt(i);
                    result.trims++;
                    result.buttons++;
                }
                else if (designator.GetType() == typeof(Designator_Dropdown))
                {
                    var dropdown = (Designator_Dropdown)designator;
                    int removed = dropdown.Elements.RemoveAll(IsTrim);
                    if (removed == 0) continue;
                    result.trims += removed;
                    result.buttons++;
                    if (dropdown.Elements.Count == 0)
                    {
                        list.RemoveAt(i);
                    }
                    else
                    {
                        // The face may have been a trim: show what is left.
                        dropdown.SetActiveDesignator(dropdown.Elements[0], explicitySet: false);
                    }
                }
            }
            return result;
        }

        internal static bool IsTrim(Designator designator) =>
            designator is Designator_Build build && build.PlacingDef is ThingDef def
            && def.GetModExtension<TrimPiece>() != null;
    }
}
