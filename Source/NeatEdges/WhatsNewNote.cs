using HarmonyLib;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// A note shown once at the main menu after an update that moves
    /// something a player would otherwise go looking for. 1.2.0 is the first:
    /// the trims' shape buttons leave the Floors tab by default.
    ///
    /// The note's id is <see cref="Current"/>, and the player's settings keep
    /// the id of the last note they closed, so each note shows once per
    /// player, whatever the mod's version string. A release with something to
    /// say gives the note a new id and new text keys; a release without keeps
    /// the id, and nobody sees anything.
    ///
    /// It opens when the main menu's interface is built
    /// (<see cref="Patch_WhatsNewAtMenu"/>), and not from a static
    /// constructor: those run inside the data load, before the main menu has a
    /// window stack. A start that leaves the main menu at once, a quick test or
    /// a start straight into a save, loses it to the scene change, which
    /// throws the window away without closing it; it stays due for the next
    /// visit to the main menu, because it is marked seen when it closes, not
    /// when it opens. Nothing here reads a launch flag.
    /// </summary>
    public static class WhatsNewNote
    {
        /// <summary>The current note's id.</summary>
        internal const string Current = "1.2.0";

        /// <summary>Whether the note opened in this run.</summary>
        internal static bool OpenedThisRun;

        /// <summary>Whether the player has not yet closed the current note.</summary>
        internal static bool Due =>
            NeatEdgesMod.Settings != null && NeatEdgesMod.Settings.whatsNewSeen != Current;

        /// <summary>
        /// Opens the note if it is due. 1.2.0's is about the buttons leaving
        /// Floors, so a player who has already put them back is told nothing,
        /// and the note is marked seen.
        /// </summary>
        internal static void ShowIfDue()
        {
            if (!Due) return;
            WindowStack stack = Find.WindowStack;
            if (stack == null || stack.IsOpen<Dialog_WhatsNew>()) return;
            if (!NeatEdgesMod.Settings.trimsOffFloors)
            {
                MarkSeen();
                return;
            }
            stack.Add(new Dialog_WhatsNew());
        }

        /// <summary>Records the current note as seen, in the settings file.</summary>
        internal static void MarkSeen()
        {
            if (NeatEdgesMod.Settings == null) return;
            NeatEdgesMod.Settings.whatsNewSeen = Current;
            LoadedModManager.GetMod<NeatEdgesMod>()?.WriteSettings();
        }

        /// <summary>
        /// The note's undo: the shape buttons back on the Floors tab, at once,
        /// as unticking the box in the mod settings does.
        /// </summary>
        internal static void PutBackOnFloors()
        {
            if (NeatEdgesMod.Settings == null) return;
            NeatEdgesMod.Settings.trimsOffFloors = false;
            Patch_TrimDesignators.RebuildShapeTabs();
        }

        /// <summary>
        /// The note's text for where the buttons are now: the Trims section
        /// while Better Architect Menu is loaded and shows it, the Misc tab
        /// otherwise. Each names the mod setting by its own label, and both
        /// the note and the settings window ask
        /// <see cref="Patch_TrimDesignators.AwayIsTrimsSection"/>, so the two
        /// never disagree.
        /// </summary>
        internal static string Body() =>
            Patch_TrimDesignators.AwayIsTrimsSection
                ? "NeatEdges.WhatsNew_1_2_0_Decorate".Translate("NeatEdges.SettingTrimsOnDecorate".Translate())
                : "NeatEdges.WhatsNew_1_2_0_Misc".Translate("NeatEdges.SettingTrimsOnMisc".Translate());
    }

    /// <summary>
    /// Opens <see cref="WhatsNewNote"/> where vanilla opens its own
    /// version-update dialog: as the main menu's interface is built, after
    /// every def and translation has loaded, and again on each return to the
    /// main menu. Fails closed: unapplied, no note shows.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.Init))]
    public static class Patch_WhatsNewAtMenu
    {
        static void Postfix() => WhatsNewNote.ShowIfDue();
    }

    /// <summary>The window <see cref="WhatsNewNote"/> opens.</summary>
    public class Dialog_WhatsNew : Window
    {
        const float ButtonHeight = 38f;
        const float Gap = 10f;

        Vector2 scroll;

        public Dialog_WhatsNew()
        {
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(620f, 380f);

        public override void PostOpen()
        {
            base.PostOpen();
            WhatsNewNote.OpenedThisRun = true;
        }

        public override void PostClose()
        {
            base.PostClose();
            WhatsNewNote.MarkSeen();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Rect title = new Rect(inRect.x, inRect.y, inRect.width, 42f);
            Widgets.Label(title, "NeatEdges.WhatsNew_1_2_0_Title".Translate());
            Text.Font = GameFont.Small;

            // Scrolls only when a translation outgrows the window, so the
            // paragraph naming the setting is never cut off under the buttons.
            Rect body = new Rect(inRect.x, title.yMax + Gap, inRect.width, inRect.height - title.height - ButtonHeight - 2f * Gap);
            Widgets.LabelScrollable(body, WhatsNewNote.Body(), ref scroll,
                dontConsumeScrollEventsIfNoScrollbar: true, takeScrollbarSpaceEvenIfNoScrollbar: false);

            float width = (inRect.width - Gap) / 2f;
            Rect keep = new Rect(inRect.x, inRect.yMax - ButtonHeight, width, ButtonHeight);
            if (Widgets.ButtonText(keep, "NeatEdges.WhatsNewKeep".Translate()))
            {
                Close();
            }
            Rect back = new Rect(keep.xMax + Gap, keep.y, width, ButtonHeight);
            if (Widgets.ButtonText(back, "NeatEdges.WhatsNewBackToFloors".Translate()))
            {
                WhatsNewNote.PutBackOnFloors();
                Close();
            }
        }
    }
}
