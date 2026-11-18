using HarmonyLib;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// The mod's settings, and the one patch that cannot wait for the rest. A
    /// Mod's constructor runs before any def loads, and
    /// <see cref="Patch_TrimDesignators"/> has to be in place before the game
    /// first builds each Architect tab's buttons, which it does from finishing
    /// work queued ahead of every static constructor. The settings are read
    /// here for the same reason: that first build already places the shape
    /// buttons on the tab the player chose. Everything else is patched later,
    /// from <see cref="HarmonyInit"/>.
    /// </summary>
    public class NeatEdgesMod : Mod
    {
        internal static NeatEdgesSettings Settings;

        public NeatEdgesMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<NeatEdgesSettings>();
            Patch_TrimDesignators.Apply(new Harmony(HarmonyInit.Id));
        }

        public override string SettingsCategory() => Content.Name;

        /// <summary>
        /// One checkbox, ticked by default, worded for where the buttons go:
        /// the Trims section while Better Architect Menu is loaded and shows
        /// it, and the Misc tab otherwise
        /// (<see cref="Patch_TrimDesignators.AwayIsTrimsSection"/>). A change
        /// rebuilds the tabs at once, in a running game too, so the buttons
        /// move without a restart; the game writes the file when the window
        /// closes.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            bool offFloors = Settings.trimsOffFloors;
            if (Patch_TrimDesignators.AwayIsTrimsSection)
            {
                listing.CheckboxLabeled("NeatEdges.SettingTrimsOnDecorate".Translate(), ref offFloors,
                    "NeatEdges.SettingTrimsOnDecorateDesc".Translate());
            }
            else
            {
                listing.CheckboxLabeled("NeatEdges.SettingTrimsOnMisc".Translate(), ref offFloors,
                    "NeatEdges.SettingTrimsOnMiscDesc".Translate());
            }
            listing.End();
            if (offFloors != Settings.trimsOffFloors)
            {
                Settings.trimsOffFloors = offFloors;
                Patch_TrimDesignators.RebuildShapeTabs();
            }
        }
    }
}
