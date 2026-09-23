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
    /// Registered in Architect → Floors by Patches/NeatEdges_Designators.xml,
    /// beside the marker rather than on the Zone tab with vanilla's areas. The
    /// marker and the area are one feature with two interfaces, and a player
    /// who finds one should find the other next to it.
    /// </summary>
    public abstract class Designator_AreaHardEdges : Designator_Cells
    {
        internal DesignateMode mode;

        public override bool DragDrawMeasurements => true;

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.Areas;

        public Designator_AreaHardEdges(DesignateMode mode)
        {
            this.mode = mode;
            useMouseIcon = true;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
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

    /// <summary>
    /// `Order` places both tools straight after the marker (uiOrder 2080).
    /// Special designators default to 0 and sort to the front of the tab,
    /// among Cancel and Remove floor, where nobody looking at the marker would
    /// see them.
    /// </summary>
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
            Order = 2082f;
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
            Order = 2083f;
        }
    }
}
