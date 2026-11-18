using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Keeps vanilla's own buttons for the trims off every Architect tab, so
    /// each shape is the one button <see cref="Designator_TrimShape"/> makes,
    /// and keeps those shape buttons on the one tab the player chose.
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
    /// never touched by that half.
    ///
    /// The shape buttons are named in the Floors and the Misc tab's
    /// specialDesignatorClasses (Patches/NeatEdges_Designators.xml), and in
    /// the Trims section Neat Edges adds under Better Architect Menu's
    /// Decorate tab while that mod is loaded
    /// (Defs/Misc/NeatEdges_BetterArchitect.xml), so vanilla makes them on
    /// each, and <see cref="PlaceShapeButtons"/> takes them off every one but
    /// the tab <see cref="HomeFor"/> picks from
    /// <see cref="NeatEdgesSettings.trimsOffFloors"/>. That mod's saved layout
    /// can leave the Trims section on no tab, and loads after the tabs' first
    /// build, so the home is checked again each time the Architect opens
    /// (<see cref="Patch_PlaceTrimsOnOpen"/>).
    ///
    /// Keyed on <see cref="TrimPiece"/>, so another mod's trims leave the tabs
    /// the same way and appear only on their shape's button.
    ///
    /// The method runs once per tab at startup, from a queue of finishing work
    /// that is filled before any static constructor runs, so the patch is
    /// applied from <see cref="NeatEdgesMod"/>, whose constructor runs before
    /// any def loads. It runs again whenever anything calls a tab's
    /// ResolveReferences, as Adaptive Storage - Global Settings does on every
    /// new game and load, and when the player changes the setting
    /// (<see cref="RebuildShapeTabs"/>); the postfix runs inside every one of
    /// those rebuilds. If the patch ever fails to apply, every shape shows on
    /// every tab that names it, with vanilla's dropdown beside ours. Nothing
    /// becomes unbuildable.
    /// </summary>
    public static class Patch_TrimDesignators
    {
        internal const string FloorsTab = "Floors";
        internal const string MiscTab = "Misc";

        /// <summary>
        /// The Trims section under Better Architect Menu's Decorate tab. Its
        /// def carries MayRequire, so it is in the database only while that
        /// mod is loaded.
        /// </summary>
        internal const string TrimsSection = "NE_Trims";

        /// <summary>
        /// Where the buttons go off Floors, first choice first: the Trims
        /// section where it exists, Misc everywhere else.
        /// </summary>
        static readonly string[] AwayTabs = { TrimsSection, MiscTab };

        /// <summary>Every tab that names the shape classes, in rebuild order.</summary>
        static readonly string[] ShapeTabs = { FloorsTab, MiscTab, TrimsSection };

        /// <summary>Whether the postfix is on ResolveDesignators.</summary>
        internal static bool Applied;

        /// <summary>Why it is not, when it is not.</summary>
        internal static string Failure;

        /// <summary>The patched method, so a change of tab can rebuild every holder.</summary>
        internal static MethodInfo Resolve;

        /// <summary>
        /// What the latest rebuild of each tab took out, by its defName: the
        /// trims, the buttons that held them, and the shape buttons that belong
        /// on another tab.
        /// </summary>
        internal static readonly Dictionary<string, Stripped> Last = new Dictionary<string, Stripped>();

        internal struct Stripped
        {
            internal int trims;
            internal int buttons;
            internal int shapes;
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
                Resolve = target;
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
            now.shapes = PlaceShapeButtons(__instance);
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

        // ---- which tab holds the shape buttons ----------------------------

        /// <summary>
        /// The tab the shape buttons leave Floors for, whatever the setting
        /// says: the first of <see cref="AwayTabs"/> that exists and names the
        /// button's class, passing over one that Better Architect Menu nests
        /// as a section and shows on no tab (<see cref="SectionListed"/>):
        /// the Trims section, and Misc, which that mod puts under World. Null
        /// when there is none.
        /// </summary>
        internal static DesignationCategoryDef AwayFor(Type button)
        {
            foreach (string name in AwayTabs)
            {
                DesignationCategoryDef away = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(name);
                if (!Names(away, button) || !SectionListed(away)) continue;
                return away;
            }
            return null;
        }

        /// <summary>
        /// The tab a shape button belongs on: <see cref="AwayFor"/> while the
        /// setting is on, as it is by default; Floors otherwise, and when no
        /// away tab can take it. A tab that lost the patch's entries is passed
        /// over rather than chosen, so the button falls back down the list, to
        /// Floors at the last.
        /// </summary>
        internal static DesignationCategoryDef HomeFor(Type button)
        {
            DesignationCategoryDef away = NeatEdgesMod.Settings != null && NeatEdgesMod.Settings.trimsOffFloors
                ? AwayFor(button) : null;
            return away ?? DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(FloorsTab);
        }

        /// <summary>
        /// Whether the box sends the buttons to the Trims section rather than
        /// to Misc. The setting's label and the update note both read it, so
        /// each names the place the buttons actually go.
        /// </summary>
        internal static bool AwayIsTrimsSection =>
            AwayFor(typeof(Designator_TrimStraight))?.defName == TrimsSection;

        static bool Names(DesignationCategoryDef tab, Type button) =>
            tab?.specialDesignatorClasses != null && tab.specialDesignatorClasses.Contains(button);

        /// <summary>
        /// Takes the shape buttons off every tab but their home, and on the
        /// Misc tab moves them past vanilla's own buttons. Returns how many
        /// it took off.
        ///
        /// A button is taken off only when its home names its class too, so a
        /// Floors tab that lost the patch's entries leaves the buttons where
        /// vanilla made them rather than on no tab at all. Tabs are told apart
        /// by defName, in both branches, so another mod's copy of a tab under
        /// the same name is treated as that tab.
        /// </summary>
        internal static int PlaceShapeButtons(DesignationCategoryDef tab)
        {
            List<Designator> list = tab.AllResolvedDesignators;
            int removed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!(list[i] is Designator_TrimShape button)) continue;
                DesignationCategoryDef home = HomeFor(button.GetType());
                if (home != null && home.defName != tab.defName && Names(home, button.GetType()))
                {
                    list.RemoveAt(i);
                    removed++;
                }
                else if (tab.defName == MiscTab)
                {
                    button.Order += Designator_TrimShape.MiscOffset;
                }
            }
            return removed;
        }

        /// <summary>
        /// Rebuilds every tab that names the shape buttons after the player
        /// moves them. Vanilla rebuilds a tab only when its def resolves, so
        /// without this the buttons would stay where they were until the next
        /// start. The postfix runs inside each rebuild and places the buttons.
        /// Copy keeps the designator it found for each def, which would now be
        /// one from the buttons just thrown away, so that cache is cleared too.
        /// Better Architect Menu rebuilds its own view of the tabs each time
        /// the Architect opens, so a change made while it is closed shows the
        /// next time it opens; what it keeps across openings is which section
        /// each tab had open, and a Trims section left empty is forgotten
        /// there (<see cref="ForgetTrimsSection"/>).
        /// </summary>
        internal static void RebuildShapeTabs()
        {
            if (Resolve == null) return;
            foreach (string name in ShapeTabs)
            {
                DesignationCategoryDef tab = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(name);
                if (tab == null) continue;
                try
                {
                    Resolve.Invoke(tab, null);
                }
                catch (Exception ex)
                {
                    Log.Warning("[NeatEdges] could not rebuild the " + name + " tab after the trim buttons moved: " + ex);
                }
            }
            BuildCopyCommandUtility.ClearCache();

            DesignationCategoryDef section = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail(TrimsSection);
            if (section != null && !section.AllResolvedDesignators.Any(d => d is Designator_TrimShape))
            {
                ForgetTrimsSection(section);
            }
        }

        /// <summary>
        /// Rebuilds the tabs when the shape buttons' home holds none of them,
        /// and says whether it did. Better Architect Menu loads its saved
        /// layout after the tabs' first build, and its edit mode changes the
        /// layout in play, so whether the Trims section shows can change after
        /// the buttons were placed; <see cref="Patch_PlaceTrimsOnOpen"/> calls
        /// this as the Architect opens, before that mod draws. Nothing is
        /// rebuilt when the home already holds them, nor when it does not name
        /// them, which no rebuild would change, nor a second time for a home
        /// a rebuild itself left empty: something takes them off inside every
        /// rebuild, and rebuilding at every opening would not stop it.
        /// </summary>
        internal static bool PlaceIfMoved()
        {
            Type probe = typeof(Designator_TrimStraight);
            DesignationCategoryDef home = HomeFor(probe);
            if (!Names(home, probe)) return false;
            if (HoldsShapes(home))
            {
                emptyAfterRebuild = null;
                return false;
            }
            if (emptyAfterRebuild == home.defName) return false;
            RebuildShapeTabs();
            emptyAfterRebuild = HoldsShapes(home) ? null : home.defName;
            return true;
        }

        static bool HoldsShapes(DesignationCategoryDef tab) =>
            tab.AllResolvedDesignators.Any(d => d is Designator_TrimShape);

        /// <summary>
        /// The home a rebuild from <see cref="PlaceIfMoved"/> left without the
        /// buttons, until it is seen holding them.
        /// </summary>
        static string emptyAfterRebuild;

        // ---- Better Architect Menu's remembered section --------------------

        /// <summary>Better Architect Menu's class that nests a section under one of its tabs.</summary>
        internal const string BetterArchitectNesting = "BetterArchitect.NestedCategoryExtension";

        /// <summary>
        /// The extension by which Better Architect Menu nests a tab as a
        /// section of another, or null when the tab is not nested or that mod
        /// is not loaded. Its class is in that mod's assembly, so it is found
        /// by name, and it leads to that assembly.
        /// </summary>
        internal static DefModExtension Nesting(DesignationCategoryDef tab) =>
            tab?.modExtensions?.FirstOrDefault(e => e?.GetType().FullName == BetterArchitectNesting);

        const string SectionMemoryHolder = "BetterArchitect.ArchitectCategoryTab_DesignationTabOnGUI_Patch";
        const string SectionMemoryField = "selectedCategory";

        /// <summary>
        /// Why the latest look at the memory could not use it, or null when it
        /// could.
        /// </summary>
        internal static string SectionMemoryFailure;

        /// <summary>
        /// Better Architect Menu remembers which section each of its tabs had
        /// open, and goes back to it while the section's def exists, even once
        /// the section holds no buildables and is no longer listed. So after
        /// the buttons leave the Trims section, a player who last had it open
        /// would find Decorate open on an empty panel until they clicked
        /// another section. This forgets the Trims section wherever it is
        /// remembered, and that mod then opens the tab on its first listed
        /// section; every other remembered section stays.
        ///
        /// The memory is a private static dictionary, from main tab to open
        /// section, in that mod's assembly, which the section's own nesting
        /// extension leads to. Entries are matched by defName, since that mod
        /// may hold copies of the defs. Anything unexpected leaves the memory
        /// alone, with the reason in <see cref="SectionMemoryFailure"/> for the
        /// harness: a player sees at worst the empty panel.
        /// </summary>
        internal static void ForgetTrimsSection(DesignationCategoryDef section)
        {
            try
            {
                IDictionary memory = SectionMemory(section);
                if (memory == null) return;
                var keys = new List<object>();
                foreach (DictionaryEntry entry in memory)
                {
                    if ((entry.Value as Def)?.defName == TrimsSection) keys.Add(entry.Key);
                }
                foreach (object key in keys) memory.Remove(key);
            }
            catch (Exception ex)
            {
                SectionMemoryFailure = ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>
        /// Better Architect Menu's remembered sections, or null with the reason
        /// in <see cref="SectionMemoryFailure"/>. Only a dictionary whose
        /// values are defs is taken: one holding anything else would be read
        /// as remembering nothing, and the reset would look like it worked.
        /// </summary>
        internal static IDictionary SectionMemory(DesignationCategoryDef section)
        {
            DefModExtension nesting = Nesting(section);
            Type holder = nesting?.GetType().Assembly.GetType(SectionMemoryHolder);
            FieldInfo field = holder?.GetField(SectionMemoryField, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Type[] types = field != null && field.FieldType.IsGenericType ? field.FieldType.GetGenericArguments() : null;
            bool ofDefs = types != null && types.Length == 2 && typeof(Def).IsAssignableFrom(types[1]);
            IDictionary memory = ofDefs ? field.GetValue(null) as IDictionary : null;
            SectionMemoryFailure = memory != null ? null
                : nesting == null ? "the Trims section has no nesting extension"
                : holder == null ? SectionMemoryHolder + " not found"
                : field == null ? SectionMemoryField + " not found on " + SectionMemoryHolder
                : !ofDefs ? SectionMemoryField + " is a " + field.FieldType.Name + ", not a dictionary of defs"
                : SectionMemoryField + " is not a dictionary";
            return memory;
        }

        // ---- Better Architect Menu's saved layout --------------------------

        const string LayoutRuntime = "BetterArchitect.EditModeRuntime";
        const string LayoutSettings = "BetterArchitect.BetterArchitectSettings";

        /// <summary>
        /// Why the latest look at Better Architect Menu's layout could not use
        /// it, or null when it could.
        /// </summary>
        internal static string LayoutFailure;

        /// <summary>
        /// Whether Better Architect Menu shows a section on a tab the player
        /// can open; any tab it does not nest counts as shown. That mod's edit
        /// mode saves a tab's whole list of sections once the player adds,
        /// removes or drags a section there, and draws only the sections in
        /// it, so a Decorate list saved before Neat Edges 1.2 leaves the Trims
        /// section on no tab, and a World list can leave out Misc; the player
        /// can also hide a whole tab. Asked through that mod's own public
        /// methods (its top-level tabs, each tab's sections, whether a tab is
        /// hidden), so its rules decide.
        ///
        /// Until that mod has loaded its settings, which it does after the
        /// tabs' first build, it would answer from an empty layout and fill
        /// its caches from it, so nothing is asked and the section counts as
        /// shown, as that mod's defaults show it. A version without these
        /// methods has no saved layout and nests by the extension alone, so
        /// the section counts as shown there too, as it does when anything
        /// else goes wrong, with the reason in <see cref="LayoutFailure"/> for
        /// the harness.
        /// </summary>
        internal static bool SectionListed(DesignationCategoryDef section)
        {
            DefModExtension nesting = Nesting(section);
            if (nesting == null) return true;
            try
            {
                FindLayout(nesting.GetType().Assembly);
                LayoutFailure = layoutProblem;
                if (LayoutFailure != null || layoutLoaded.GetValue(null) == null) return true;

                foreach (object tab in (IEnumerable)layoutTabs.Invoke(null, null) ?? Enumerable.Empty<object>())
                {
                    string name = (tab as Def)?.defName;
                    if (name == null || (bool)layoutHidden.Invoke(null, new object[] { name })) continue;
                    var listed = (IEnumerable<string>)layoutSections.Invoke(null, new object[] { name });
                    if (listed != null && listed.Contains(section.defName)) return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LayoutFailure = ex.GetType().Name + ": " + ex.Message;
                return true;
            }
        }

        /// <summary>The assembly the layout members below were looked up in.</summary>
        static Assembly layoutFrom;
        static MethodInfo layoutTabs, layoutSections, layoutHidden;
        static FieldInfo layoutLoaded;
        static string layoutProblem;

        /// <summary>
        /// Looks up Better Architect Menu's layout methods, and the static
        /// field its Mod sets once its settings have loaded, the first time
        /// they are needed from that assembly; the setting's label and the
        /// update note ask every frame they are drawn.
        /// </summary>
        static void FindLayout(Assembly assembly)
        {
            if (assembly == layoutFrom) return;
            Type runtime = assembly.GetType(LayoutRuntime);
            Type settings = assembly.GetType(LayoutSettings);
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public;
            layoutTabs = runtime?.GetMethod("GetParents", flags, null, Type.EmptyTypes, null);
            layoutSections = runtime?.GetMethod("GetChildrenForParent", flags, null, new[] { typeof(string) }, null);
            layoutHidden = settings?.GetMethod("ShouldSkipParentCategory", flags, null, new[] { typeof(string) }, null);
            layoutLoaded = settings?.GetField("mod", flags);
            layoutProblem = layoutTabs == null || !typeof(IEnumerable).IsAssignableFrom(layoutTabs.ReturnType)
                    ? LayoutRuntime + ".GetParents() not found, or not a list"
                : layoutSections == null || !typeof(IEnumerable<string>).IsAssignableFrom(layoutSections.ReturnType)
                    ? LayoutRuntime + ".GetChildrenForParent(string) not found, or not a list of names"
                : layoutHidden == null || layoutHidden.ReturnType != typeof(bool)
                    ? LayoutSettings + ".ShouldSkipParentCategory(string) not found, or not a yes or no"
                : layoutLoaded == null ? LayoutSettings + ".mod not found"
                : null;
            layoutFrom = assembly;
        }
    }

    /// <summary>
    /// Puts the shape buttons on their home as the Architect opens, through
    /// <see cref="Patch_TrimDesignators.PlaceIfMoved"/>, which rebuilds only
    /// when the home holds none of them. Never throws into the window's
    /// opening. Fails closed: unapplied, the buttons stay where the latest
    /// build of the tabs put them.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Architect), nameof(MainTabWindow_Architect.PreOpen))]
    public static class Patch_PlaceTrimsOnOpen
    {
        static void Postfix()
        {
            try
            {
                Patch_TrimDesignators.PlaceIfMoved();
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[NeatEdges] could not place the trim buttons as the Architect opened: " + ex, 0x4E450001);
            }
        }
    }
}
