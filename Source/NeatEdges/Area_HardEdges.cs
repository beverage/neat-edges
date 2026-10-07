using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// The painted hard-edge area: whole tiles, drag-painted from Architect →
    /// Zone, free and instant.
    ///
    /// A painted tile is exactly four hardened edges to the mask, as if a
    /// single-edge marker stood on each side — it contributes all four
    /// cardinals to <see cref="Patch_SidedFadeBlock.ComputeOwnMask(IntVec3, Map, Area_HardEdges)"/>
    /// and everything after that is the sided model unchanged. So hardening is
    /// two-sided, the area's outside corners are sealed for the tiles around it,
    /// and a re-entrant corner closes itself, all by rules the markers already
    /// pass. What an area still cannot say is "crisp on this side only"; that is
    /// what the markers are for.
    ///
    /// WHY AN AREA. It is the cheapest per-cell player state in the game: the
    /// engine scribes it, drag-paints it and draws its overlay, and a painted
    /// tile costs no build order, no pawn and no thing on the map. The markers
    /// stay for the sided case and for trim pieces that carry the extension.
    ///
    /// THE SAVED SHAPE IS THE BASE CLASS'S, AND MUST STAY SO. `ExposeData` is
    /// not overridden: an area saves its ID and its grid, nothing else, and that
    /// is exactly the node Perspective: Paths writes for its own area. That
    /// identity is what lets <see cref="Patch_AreaMigration"/> load their saved
    /// areas as this type without a converter. Add a scribed field here and
    /// every adopted area loads with that field at its default.
    ///
    /// The class name is permanent once a save carries it: the scribe writes
    /// `Class="NeatEdges.Area_HardEdges"` into every map that has one.
    /// </summary>
    public class Area_HardEdges : Area
    {
        /// <summary>
        /// Loaded from another mod's saved node (<see cref="Patch_AreaMigration"/>).
        /// Runtime only, and must stay so: a scribed field here would break the
        /// shared saved shape the migration depends on.
        /// </summary>
        internal bool adoptedFromOtherMod;

        /// <summary>For the scribe, which instantiates through it on load.</summary>
        public Area_HardEdges()
        {
            if (Patch_AreaMigration.pending)
            {
                adoptedFromOtherMod = true;
                Patch_AreaMigration.pending = false;
            }
        }

        public Area_HardEdges(AreaManager areaManager) : base(areaManager)
        {
        }

        public override string Label => "NeatEdges.AreaLabel".Translate();

        /// <summary>The overlay's colour, so both halves of the feature read as one.</summary>
        public override Color Color => MapComponent_EdgeOverlay.OverlayColor;

        /// <summary>Below vanilla's fixed areas. Only list order, and this area is never listed.</summary>
        public override int ListPriority => 4000;

        public override string GetUniqueLoadID()
        {
            return "Area_" + ID + "_HardEdges";
        }

        /// <summary>
        /// Painting has to repaint the terrain, and vanilla's `MarkDirty` does not.
        ///
        /// It refreshes the area's own overlay, the pathfinder's copy and the
        /// region, and never touches a mesh — no vanilla area changes how the
        /// ground is drawn. Without this a painted tile looks untouched until
        /// something else happens to dirty its section, which is the same "the
        /// feature looks dead" failure the markers have
        /// <see cref="Patch_SidedFadeInvalidate"/> for.
        ///
        /// `regenAdjacentCells` because hardening is two-sided: the tile across
        /// each edge changes, and so do the diagonal neighbours whose corners it
        /// seals, and any of them may sit in the next section.
        ///
        /// Clearing the last painted tile removes the area (<see cref="Discard"/>).
        /// </summary>
        protected override void Set(IntVec3 c, bool val)
        {
            if (this[c] == val) return;

            base.Set(c, val);

            Map.mapDrawer?.MapMeshDirty(c, (ulong)MapMeshFlagDefOf.Terrain,
                regenAdjacentCells: true, regenAdjacentSections: false);

            if (!val && TrueCount == 0) Discard();
        }

        /// <summary>
        /// Takes this area off its map, so a map with nothing painted saves no
        /// trace of this mod's area.
        ///
        /// WHY IT MATTERS: a saved area whose class is missing takes the map's
        /// WHOLE area list down with it. The engine loads the list as one, its
        /// links pass throws on the null an unresolvable class leaves, and the
        /// map ends up with no areas at all: home, allowed and roof gone. Then
        /// map loading itself fails at the first reader of the home area, and
        /// the game's root update throws every frame (DESIGN §8 has the tested
        /// Perspective: Paths cases). So a player removing this mod must
        /// be able to leave no `Area_HardEdges` behind, and clearing every
        /// painted tile is how they do it. The area keeps its manager reference,
        /// so a caller still holding it, such as a drag in progress, stays safe.
        ///
        /// Removed around `AreaManager.Remove`, which refuses an area that is
        /// not player-deletable, as <see cref="MergeDuplicates"/> does.
        /// </summary>
        internal void Discard()
        {
            areaManager?.AllAreas.Remove(this);
        }

        /// <summary>
        /// Removes every empty hard-edge area on the map. Returns how many.
        ///
        /// Clearing removes an area as it empties, but one can still arrive
        /// empty: Perspective: Paths adds an empty zone to every map, and the
        /// migration adopts each one as ours. Run at map finalization, after
        /// duplicates are merged.
        /// </summary>
        internal static int RemoveEmpty(Map map)
        {
            List<Area> areas = map?.areaManager?.AllAreas;
            if (areas == null) return 0;

            return areas.RemoveAll(a => a is Area_HardEdges hard && hard.TrueCount == 0);
        }

        /// <summary>
        /// This map's area, or null if nobody has painted one. A linear scan of
        /// the map's areas (`AreaManager.Get`), so resolve it once per operation
        /// and never per cell.
        /// </summary>
        internal static Area_HardEdges On(Map map)
        {
            return map?.areaManager?.Get<Area_HardEdges>();
        }

        /// <summary>
        /// Created on first paint, not with the map, and removed again when the
        /// last tile is cleared. An empty area on every map would be one more
        /// thing in every save, and would cost every map its whole area list if
        /// the player later removes this mod (<see cref="Discard"/>).
        /// </summary>
        internal static Area_HardEdges GetOrCreate(Map map)
        {
            Area_HardEdges area = On(map);
            if (area != null) return area;

            area = new Area_HardEdges(map.areaManager);
            map.areaManager.AllAreas.Add(area);
            return area;
        }

        /// <summary>
        /// Folds any second area into the first. Returns how many were removed.
        ///
        /// A map can load with two: one adopted from Perspective: Paths and one
        /// painted here, if a save ever ran with both mods and the player used
        /// both tools. `AreaManager` relinks areas on load but never prunes them
        /// by type, and everything here resolves THE area as the first one found,
        /// so a second would keep hardening tiles the clear tool could not reach.
        ///
        /// Cells move through the indexer rather than the raw grid, so the
        /// pathfinder's copy and the terrain mesh hear about every one of them.
        /// Removal goes around `AreaManager.Remove`, which refuses any area that
        /// is not player-deletable; the pathfinder prunes an area it no longer
        /// finds in the list on its next update.
        /// </summary>
        internal static int MergeDuplicates(Map map)
        {
            List<Area> areas = map?.areaManager?.AllAreas;
            if (areas == null) return 0;

            Area_HardEdges keep = null;
            int removed = 0;

            for (int i = 0; i < areas.Count; i++)
            {
                if (!(areas[i] is Area_HardEdges extra)) continue;

                if (keep == null)
                {
                    keep = extra;
                    continue;
                }

                foreach (IntVec3 c in extra.ActiveCells)
                {
                    keep[c] = true;
                }

                areas.RemoveAt(i);
                i--;
                removed++;
            }

            return removed;
        }
    }
}
