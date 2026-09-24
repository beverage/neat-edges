using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// What this mod does while Perspective: Paths is INSTALLED: it leaves
    /// whole-tile painting to that mod's zone.
    ///
    /// A player who has it installed chose it for exactly that feature, so a
    /// second whole-tile tool here would only compete with it. The marker and
    /// the trims stay, because they harden one side of a boundary, which its
    /// zone cannot express. The overlay stays with them: it is the only way to
    /// find a marker.
    ///
    /// Two things follow while it is installed:
    ///
    ///   - The two area tools are hidden and refuse every cell
    ///     (<see cref="Designator_AreaHardEdges"/>).
    ///   - A hard edge area already on a map is moved into its zone when the
    ///     map finalizes, and ours is removed (<see cref="HandBack"/>). That
    ///     happens when a player painted here before adding it, and on the
    ///     round trip this mod creates itself: remove it, its zones load as
    ///     ours (<see cref="Patch_AreaMigration"/>), add it back.
    ///
    /// With the migration, whichever mod is installed owns the painted tiles,
    /// and Perspective: Paths owns them when both are. No tile is lost in either
    /// direction. What changes is the rule they draw by: its zone stops terrain
    /// fading into a tile, but not the tile fading out.
    ///
    /// "Installed" means its area class resolves: the class the migration
    /// answers for, and the only name a save of its zone depends on. Both
    /// Workshop releases of it ship that class.
    ///
    /// Its zone is reached by name and reflection only. This assembly holds no
    /// reference to that mod, which a player without it does not have.
    /// </summary>
    public static class PerspectivePathsInterop
    {
        /// <summary>The class its zone saves under.</summary>
        internal const string AreaClass = "PerspectivePaths.Area_InvertEdges";

        /// <summary>
        /// The label it makes its zone with, and looks it up by when each map
        /// finalizes. A zone made here under this label is the one it then
        /// caches as its own, instead of adding a second, empty one.
        /// </summary>
        internal const string ZoneLabel = "InvertEdges";

        internal static Type areaType;
        internal static bool resolved;

        /// <summary>Areas handed over since the last announcement.</summary>
        internal static int handedBack;

        /// <summary>Its zone's type, or null when it is not installed.</summary>
        internal static Type AreaType
        {
            get
            {
                if (!resolved)
                {
                    areaType = GenTypes.GetTypeInAnyAssembly(AreaClass);
                    resolved = true;
                }
                return areaType;
            }
        }

        /// <summary>True while Perspective: Paths is installed.</summary>
        public static bool Installed => AreaType != null;

        /// <summary>
        /// Moves this map's painted tiles into its zone and removes our area.
        /// Returns how many tiles moved, or -1 if its zone could not be made,
        /// in which case our area stays and keeps drawing by our rule.
        ///
        /// Called at map finalization, after duplicates are merged, so there
        /// is at most one area to move. That is also before its own
        /// finalization postfix, which looks its zone up by label and makes an
        /// empty one if none is there, and before the first mesh build, which
        /// the engine defers until the load finishes. So nothing needs
        /// repainting, and it adopts the zone made here.
        ///
        /// Cells go in through its zone's indexer, so the pathfinder and the
        /// region hear about each one, as they do for any painted area. Our
        /// area is removed around `AreaManager.Remove`, which refuses an area
        /// that is not player-deletable, as <see cref="Area_HardEdges.MergeDuplicates"/>
        /// does.
        /// </summary>
        internal static int HandBack(Map map)
        {
            if (!Installed) return 0;

            Area_HardEdges ours = Area_HardEdges.On(map);
            if (ours == null) return 0;

            List<IntVec3> cells = ours.ActiveCells.ToList();
            if (cells.Count > 0)
            {
                Area zone = ZoneOn(map) ?? CreateZone(map);
                if (zone == null) return -1;

                for (int i = 0; i < cells.Count; i++)
                {
                    zone[cells[i]] = true;
                }
                handedBack++;
            }

            map.areaManager.AllAreas.Remove(ours);
            return cells.Count;
        }

        /// <summary>Its zone on this map, or null if there is none yet.</summary>
        internal static Area ZoneOn(Map map)
        {
            List<Area> areas = map?.areaManager?.AllAreas;
            if (areas == null || AreaType == null) return null;

            for (int i = 0; i < areas.Count; i++)
            {
                if (areas[i].GetType() == AreaType) return areas[i];
            }
            return null;
        }

        /// <summary>
        /// Makes its zone the way it makes it itself: its (AreaManager, string)
        /// constructor with its label, added to the map's list. Null, with a
        /// warning, if that constructor has changed.
        /// </summary>
        internal static Area CreateZone(Map map)
        {
            try
            {
                Area zone = (Area)Activator.CreateInstance(AreaType, map.areaManager, ZoneLabel);
                map.areaManager.AllAreas.Add(zone);
                return zone;
            }
            catch (Exception e)
            {
                Log.Warning("[NeatEdges] could not make Perspective: Paths' zone to move this "
                    + "map's hard edge area into, so the area stays with Neat Edges and keeps "
                    + "drawing. " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Tells the player once, after the load, where their painted tiles
        /// went and which tools edit them now. Deferred for the same reason as
        /// <see cref="Patch_AreaMigration.AnnounceAdopted"/>.
        /// </summary>
        internal static void AnnounceHandedBack()
        {
            if (handedBack == 0) return;

            int count = handedBack;
            handedBack = 0;

            Log.Message("[NeatEdges] moved " + count + " hard-edge area(s) into Perspective: "
                + "Paths' zone; it is installed, and owns whole-tile hard edges while it is.");

            LongEventHandler.ExecuteWhenFinished(() => Messages.Message(
                "NeatEdges.HandedBackAreas".Translate(),
                MessageTypeDefOf.NeutralEvent, historical: false));
        }
    }
}
