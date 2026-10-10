using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// One build button for a shape of trim, that shape in every style on its
    /// right-click menu. A left click opens the material menu for the style
    /// on the button, as any stuffable building's button does; a right click
    /// lists the styles, and choosing one puts it on the button and opens its
    /// material menu. A new style, Neat Edges' or another mod's, adds a line to
    /// each shape's menu and no buttons.
    ///
    /// A <see cref="Designator_Dropdown"/>, for Copy: a placed trim's Copy
    /// command finds its designator by looking inside dropdowns
    /// (BuildCopyCommandUtility.FindAllowedDesignator), so each style's own
    /// Designator_Build stays reachable with no patch. Vanilla's dropdown
    /// opens its list on a left click and never calls the chosen element's
    /// ProcessInput, where the material menu lives, so a stuffable building
    /// in one is locked to its default material; this one forwards the click
    /// instead.
    ///
    /// Better Architect Menu unrolls every dropdown on its Floors tab unless a
    /// member's DesignatorDropdownGroupDef sets includeEyeDropperTool, which
    /// vanilla reads only for terrain. Each shape's defs name such a group
    /// (Defs/Misc/NeatEdges_TrimShapes.xml), so the shape stays one button
    /// there too.
    ///
    /// Architect search matches a button's label only, so while a search is
    /// running the button takes the label of the first style it matches, and
    /// a click places that style. The filter is read from the Architect tab by
    /// reflection; if that field ever moves, search simply finds the style on
    /// the button, as it does for vanilla's dropdowns.
    /// </summary>
    public class Designator_TrimShape : Designator_Dropdown
    {
        /// <summary>The shape every style on the button has.</summary>
        internal readonly TrimPiece.Kind shape;

        internal Designator current;

        public Designator_TrimShape(TrimPiece.Kind shape, IEnumerable<ThingDef> styles)
        {
            this.shape = shape;
            foreach (ThingDef def in styles)
            {
                Add(new Designator_Build(def));
            }
            current = Elements.FirstOrDefault();
            if (current != null)
            {
                Order = current.Order;
            }
        }

        public override string Label => (SearchMatch() ?? current)?.Label ?? base.Label;

        public override string Desc =>
            (current?.Desc ?? base.Desc) + "\n\n" + "NeatEdges.TrimShapeStyles".Translate();

        public override void ProcessInput(Event ev)
        {
            Designator match = SearchMatch();
            if (match != null)
            {
                Show(match);
            }
            current?.ProcessInput(ev);
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                foreach (Designator style in Elements)
                {
                    if (!style.Visible)
                    {
                        continue;
                    }
                    Designator chosen = style;
                    yield return new FloatMenuOption(chosen.LabelCap.ToString().TrimEnd('.'), delegate
                    {
                        Show(chosen);
                        chosen.ProcessInput(Event.current);
                    }, (Texture2D)chosen.icon, chosen.IconDrawColor);
                }
            }
        }

        /// <summary>Put a style on the button: its icon, label and description.</summary>
        internal void Show(Designator style)
        {
            current = style;
            SetActiveDesignator(style);
        }

        // ---- the Architect's search ------------------------------------------

        internal static FieldInfo searchWidget;
        internal static bool searchLookedUp;

        /// <summary>The Architect tab's search filter, or null when it cannot be read.</summary>
        internal static QuickSearchFilter ArchitectFilter()
        {
            try
            {
                if (!searchLookedUp)
                {
                    searchLookedUp = true;
                    searchWidget = typeof(MainTabWindow_Architect).GetField("quickSearchWidget",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }
                if (searchWidget == null || !(MainButtonDefOf.Architect?.TabWindow is MainTabWindow_Architect tab))
                {
                    return null;
                }
                return (searchWidget.GetValue(tab) as QuickSearchWidget)?.filter;
            }
            catch (Exception)
            {
                searchWidget = null;
                return null;
            }
        }

        /// <summary>
        /// While the Architect's search is running and does not match the style
        /// on the button, the first style it does match.
        /// </summary>
        internal Designator SearchMatch()
        {
            QuickSearchFilter filter = ArchitectFilter();
            if (filter == null || !filter.Active || current == null || filter.Matches(current.Label))
            {
                return null;
            }
            return Elements.FirstOrDefault(style => style.Visible && filter.Matches(style.Label));
        }
    }

    /// <summary>
    /// Builds a button for every shape of trim with no buttons of its own
    /// (canGenerateDefaultDesignator false), holding that shape in every style
    /// in the category, lowest uiOrder first, and adds it to the category.
    /// Neat Edges' seven come this way, and another mod's trims that opt in the
    /// same way join them: its straight is one more line on the straight's
    /// menu.
    ///
    /// Runs as a static constructor, after the categories resolved their own
    /// designators (that is queued while defs resolve, and static constructors
    /// are queued after) and before any map.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class TrimShapeButtons
    {
        internal static readonly List<Designator_TrimShape> All = new List<Designator_TrimShape>();

        static TrimShapeButtons()
        {
            var shapes = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.GetModExtension<TrimPiece>() != null && d.designationCategory != null
                    && !d.canGenerateDefaultDesignator)
                .GroupBy(d => (d.designationCategory, d.GetModExtension<TrimPiece>().shape))
                .OrderBy(g => g.Key.shape)
                .ThenBy(g => g.Key.designationCategory.defName);
            foreach (var shape in shapes)
            {
                var button = new Designator_TrimShape(shape.Key.shape,
                    shape.OrderBy(d => d.uiOrder).ThenBy(d => d.defName));
                shape.Key.designationCategory.AllResolvedDesignators.Add(button);
                All.Add(button);
            }
        }
    }
}
