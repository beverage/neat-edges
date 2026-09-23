using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Puts the hard-edge overlay on the bottom-right toggle row, beside
    /// vanilla's roof, fertility and terrain-affordance overlays. It shows the
    /// markers and the painted area together.
    ///
    /// This SHIPS, deliberately. It started as a debug action, which was wrong
    /// for the obvious reason: players do not run with dev mode on, and a marker
    /// draws nothing on the map. "Where are my hard edges" is a question the
    /// player who placed them has to be able to answer, which is the test for a
    /// diagnostic that belongs in a shipped build.
    ///
    /// `showTerrainAffordanceOverlay` is the closest vanilla precedent, so the
    /// toggle sits in the same row and behaves the same way.
    ///
    /// Known limitation: the flag is a plain static, not a scribed `PlaySettings`
    /// field, so it resets each session rather than persisting like vanilla's
    /// toggles. Matching vanilla would mean patching PlaySettings.ExposeData.
    /// </summary>
    [HarmonyPatch(typeof(PlaySettings), "DoPlaySettingsGlobalControls")]
    [StaticConstructorOnStartup]   // static Texture2D: silences the startup check's "probably needs"
    public static class Patch_EdgeOverlayToggle
    {
        // TODO(art): placeholder. Vanilla's remove-bridge glyph is the only
        // stock icon that reads as "foundation"; a purpose-made one should
        // land before release.
        internal static Texture2D icon;

        internal static Texture2D Icon => icon
            ?? (icon = ContentFinder<Texture2D>.Get(
                "UI/Designators/RemoveBridge", reportFailure: false));

        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView || row == null) return;
            if (Current.ProgramState != ProgramState.Playing) return;
            if (Icon == null) return;

            row.ToggleableIcon(
                ref MapComponent_EdgeOverlay.ShowOverlay,
                Icon,
                "NeatEdges.OverlayToggle".Translate(),
                SoundDefOf.Mouseover_ButtonToggle);
        }
    }
}
