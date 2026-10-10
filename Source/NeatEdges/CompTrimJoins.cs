using System.Linq;
using RimWorld;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Keeps a diagonal's ends in step with the trims around it. A diagonal cuts
    /// its ends by what it finds beside it when it prints, so building or
    /// removing any trim has to print the trims next to it again, or a straight
    /// laid beside a diagonal leaves the old end showing until that section
    /// redraws for some other reason.
    ///
    /// Spawning and despawning already redraw the section a thing stands in
    /// (Thing.DirtyMapMesh), so this matters only where a neighbour sits across
    /// a section's edge: it asks for the adjacent cells too, the call a linked
    /// building makes for its neighbours. Nothing is saved; a thing loaded from
    /// a save builds its comps from its def, so trims built before this existed
    /// get it too.
    /// </summary>
    public class CompTrimJoins : ThingComp
    {
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                Redraw(parent.Map);
            }
        }

        /// <summary>
        /// The map comes from the argument: by the time a comp hears of a
        /// despawn, <c>parent.Map</c> is already null.
        /// </summary>
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            Redraw(map);
        }

        internal void Redraw(Map map)
        {
            map?.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlagDefOf.Things, regenAdjacentCells: true,
                regenAdjacentSections: false);
        }
    }

    /// <summary>
    /// Gives every trim the comp, Neat Edges' own and any other mod's that opted
    /// in with a <see cref="TrimPiece"/>, so opting in stays two lines of XML.
    /// Runs once at startup, after the defs have loaded and before any map.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class TrimJoinsInjection
    {
        internal static int Given;

        static TrimJoinsInjection()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.GetModExtension<TrimPiece>() == null
                    || def.thingClass == null || !typeof(ThingWithComps).IsAssignableFrom(def.thingClass))
                {
                    continue;
                }
                if (def.comps.Any(c => c.compClass == typeof(CompTrimJoins)))
                {
                    continue;
                }
                def.comps.Add(new CompProperties(typeof(CompTrimJoins)));
                Given++;
            }
        }
    }
}
