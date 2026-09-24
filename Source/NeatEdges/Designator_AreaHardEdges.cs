using RimWorld;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Paint and clear for <see cref="Area_HardEdges"/>, on vanilla's own
    /// area-designator pattern (the snow-clear pair is the closest template):
    /// the Areas draw style, so a drag paints a filled rectangle, and the area
    /// overlay while the tool is held.
    ///
    /// Registered in Architect → Zone by Patches/NeatEdges_Designators.xml,
    /// with vanilla's areas, where Perspective: Paths keeps its own tools. A
    /// player switching over finds these where the old ones were. They sat on
    /// the Floors tab beside the marker at first, and moved for the switch.
    /// Neither sets an Order: no vanilla tool on the Zone tab does, so the
    /// grid's stable sort keeps list order and these follow vanilla's areas.
    ///
    /// Both step aside while Perspective: Paths is installed, whose zone does
    /// whole-tile painting then (<see cref="PerspectivePathsInterop"/>). The
    /// marker does not: its zone has nothing like a one-sided edge.
    /// </summary>
    public abstract class Designator_AreaHardEdges : Designator_Cells
    {
        internal DesignateMode mode;

        public override bool DragDrawMeasurements => true;

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.Areas;

        /// <summary>
        /// Hidden while Perspective: Paths is installed. The architect grid
        /// skips any gizmo that is not visible.
        /// </summary>
        public override bool Visible => !PerspectivePathsInterop.Installed;

        public Designator_AreaHardEdges(DesignateMode mode)
        {
            this.mode = mode;
            useMouseIcon = true;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            // Hidden is not enough on its own: architect search activates the
            // only tool matching a query whether or not that tool is visible.
            if (PerspectivePathsInterop.Installed) return false;
            if (!c.InBounds(Map)) return false;

            Area_HardEdges area = Area_HardEdges.On(Map);
            bool painted = area != null && area[c];

            return mode == DesignateMode.Add ? !painted : painted;
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            if (mode == DesignateMode.Add)
            {
                Area_HardEdges.GetOrCreate(Map)[c] = true;
                return;
            }

            Area_HardEdges area = Area_HardEdges.On(Map);
            if (area != null) area[c] = false;
        }

        public override void SelectedUpdate()
        {
            GenUI.RenderMouseoverBracket();
            Area_HardEdges.On(Map)?.MarkForDraw();
        }
    }

    public class Designator_AreaHardEdgesExpand : Designator_AreaHardEdges
    {
        public Designator_AreaHardEdgesExpand() : base(DesignateMode.Add)
        {
            defaultLabel = "NeatEdges.DesignatorExpand".Translate();
            defaultDesc = "NeatEdges.DesignatorExpandDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("NeatEdges/AreaExpand");
            soundDragSustain = SoundDefOf.Designate_DragAreaAdd;
            soundDragChanged = SoundDefOf.Designate_DragZone_Changed;
            soundSucceeded = SoundDefOf.Designate_ZoneAdd;
        }
    }

    public class Designator_AreaHardEdgesClear : Designator_AreaHardEdges
    {
        public Designator_AreaHardEdgesClear() : base(DesignateMode.Remove)
        {
            defaultLabel = "NeatEdges.DesignatorClear".Translate();
            defaultDesc = "NeatEdges.DesignatorClearDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("NeatEdges/AreaClear");
            soundDragSustain = SoundDefOf.Designate_DragAreaDelete;
            soundDragChanged = null;
            soundSucceeded = SoundDefOf.Designate_ZoneDelete;
        }
    }
}
