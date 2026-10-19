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
    ///
    /// Each shape's button is a subclass below, named in the Floors tab's
    /// specialDesignatorClasses (Patches/NeatEdges_Designators.xml), so
    /// vanilla makes the buttons itself whenever it builds that tab's list.
    /// It builds it once at startup, and again whenever anything calls the
    /// category's ResolveReferences: Adaptive Storage - Global Settings does,
    /// on every new game and load, once a player has moved a storage building
    /// to another tab. Buttons added to the list at startup, as these were in
    /// v1.1.0 and v1.1.1, are thrown away by that rebuild and never come back.
    ///
    /// A button holds every trim of its shape, whatever tab the trim's def
    /// names: one button per shape, always on Floors. The def still needs a
    /// designationCategory, because vanilla counts a def without one as not
    /// buildable by the player; Copy finds the trim's designator on any tab.
    /// Vanilla makes a button of its own for every trim too, the trims being
    /// ordinary buildables, and <see cref="Patch_TrimDesignators"/> takes those
    /// off every tab as each is built.
    /// </summary>
    public abstract class Designator_TrimShape : Designator_Dropdown
    {
        /// <summary>The shape every style on the button has.</summary>
        internal readonly TrimPiece.Kind shape;

        internal Designator current;

        /// <summary>
        /// Added to the face style's uiOrder to make the button's Order. The
        /// tab sorts its buttons by Order and keeps list order on a tie, and a
        /// special designator is listed before every button the tab makes from
        /// defs, so on a tie the shape would jump ahead: the floor border's 2060
        /// is the stone tiles' too, and it would split the row of trims from
        /// the tiles. Just past the face's own order, a button sorts after
        /// everything that shares it, where v1.1.0 and v1.1.1 put it by adding
        /// the buttons last, and before anything ordered higher.
        /// </summary>
        internal const float TieBreak = 0.01f;

        /// <summary>
        /// Every buildable def drawn as this shape, Neat Edges' and other
        /// mods', lowest uiOrder first, so another mod's style is one more line
        /// on the shape's menu and adds no button. Vanilla's own button for
        /// each is taken off its tab by <see cref="Patch_TrimDesignators"/>.
        /// </summary>
        protected Designator_TrimShape(TrimPiece.Kind shape)
        {
            this.shape = shape;
            IEnumerable<ThingDef> styles = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.GetModExtension<TrimPiece>()?.shape == shape && d.designationCategory != null)
                .OrderBy(d => d.uiOrder)
                .ThenBy(d => d.defName);
            foreach (ThingDef def in styles)
            {
                Add(new Designator_Build(def));
            }
            current = Elements.FirstOrDefault();
            if (current != null)
            {
                Order = current.Order + TieBreak;
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

    // One per shape, because vanilla makes a special designator from its type
    // alone. Patches/NeatEdges_Designators.xml names each of them, and
    // check-shipped-dll.py requires each by name. No save stores them, so a
    // rename costs only those two files.

    public class Designator_TrimStraight : Designator_TrimShape
    {
        public Designator_TrimStraight() : base(TrimPiece.Kind.Straight) { }
    }

    public class Designator_TrimCorner : Designator_TrimShape
    {
        public Designator_TrimCorner() : base(TrimPiece.Kind.Corner) { }
    }

    public class Designator_TrimInsideCorner : Designator_TrimShape
    {
        public Designator_TrimInsideCorner() : base(TrimPiece.Kind.InsideCorner) { }
    }

    public class Designator_TrimRunner : Designator_TrimShape
    {
        public Designator_TrimRunner() : base(TrimPiece.Kind.Runner) { }
    }

    public class Designator_TrimEndCap : Designator_TrimShape
    {
        public Designator_TrimEndCap() : base(TrimPiece.Kind.EndCap) { }
    }

    public class Designator_TrimFrame : Designator_TrimShape
    {
        public Designator_TrimFrame() : base(TrimPiece.Kind.Frame) { }
    }

    public class Designator_TrimDiagonal : Designator_TrimShape
    {
        public Designator_TrimDiagonal() : base(TrimPiece.Kind.Diagonal) { }
    }
}
