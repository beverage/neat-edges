using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Highlights every cell carrying a hard edge. Toggled from the bottom-right
    /// overlay row — see <see cref="Patch_EdgeOverlayToggle"/>.
    ///
    /// This is not a convenience. A hard edge draws nothing on the map by
    /// design, and nothing in the vanilla UI reports it: the terrain readout
    /// names the floor, the build menu shows the floor, and the only way to
    /// remove one is to deconstruct a thing you cannot see. Without this the
    /// mod would ship a feature the player can place and then never find again.
    ///
    /// Compiled unconditionally: map components are scribed by type
    /// (`Map.ExposeComponents` uses LookMode.Deep), so one that existed only
    /// under a build symbol would make every save taken with it log a
    /// missing-type error without it.
    /// </summary>
    public class MapComponent_EdgeOverlay : MapComponent
    {
        /// <summary>
        /// Global rather than per-map: the toggle follows the player between
        /// maps, matching how vanilla's overlay toggles behave.
        /// </summary>
        public static bool ShowOverlay;

        internal static readonly Color OverlayColor = new Color(0.25f, 0.80f, 1f);

        internal CellBoolDrawer drawer;
        internal bool subscribed;

        public MapComponent_EdgeOverlay(Map map) : base(map)
        {
        }

        internal CellBoolDrawer Drawer => drawer ?? (drawer = new CellBoolDrawer(
            HasBlocker,
            () => OverlayColor,
            _ => Color.white,
            map.Size.x, map.Size.z));

        /// <summary>
        /// Highlights cells that CARRY an edge component — not every cell one
        /// affects.
        ///
        /// `EdgeMaskAt` was the obvious predicate and the wrong one. Since
        /// hardening became two-sided, a single marker tidies six tiles, and
        /// overlapping runs merged into large blobs that told the player nothing
        /// about where anything actually was (observed in play). The question
        /// this overlay answers is "where is the thing I would click", so the
        /// own mask — the markers standing on this cell — is what it must ask.
        ///
        /// It also drops the cost by the same 24x the cache was buying, and
        /// without the cache: one thing-grid lookup per cell, no neighbours.
        ///
        /// Markers only. The painted area is drawn by its own drawer (see
        /// <see cref="MapComponentUpdate"/>), in the same colour; counting it
        /// here as well would tint every painted tile twice.
        /// </summary>
        internal bool HasBlocker(int index)
        {
            return Patch_SidedFadeBlock.MarkerMask(
                map.cellIndices.IndexToCell(index), map) != 0;
        }

        /// <summary>Mark the overlay stale; it rebuilds on the next draw.</summary>
        internal void Invalidate() => drawer?.SetDirty();

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            // Load-time housekeeping for the painted area, here because this is
            // the one hook that runs per map once every area has loaded. All of
            // it is idempotent, so running again on a later FinalizeInit is
            // harmless. Adopted areas with painted tiles are counted first, for
            // the announcement, before the merge can fold one away; the merge
            // goes before the hand-back, so a hand-back to Perspective: Paths
            // has at most one area to move, and an area left empty after it,
            // or adopted empty, is dropped before it can reach a save.
            Patch_AreaMigration.CountPainted(map);
            Area_HardEdges.MergeDuplicates(map);
            Area_HardEdges.RemoveEmpty(map);
            Patch_AreaMigration.AnnounceAdopted();
            PerspectivePathsInterop.HandBack(map);
            PerspectivePathsInterop.AnnounceHandedBack();

            // The drawer caches its mesh until told otherwise, so an edge placed
            // or removed while the overlay is up would not show.
            //
            // Guarded because FinalizeInit runs again on load, and subscribing
            // twice would rebuild the mesh twice per change.
            if (subscribed) return;

            // Spawn and despawn are the only events that change the answer.
            // Terrain changes used to matter, when the predicate was the derived
            // mask and a neighbour's edge could seal a corner here; asking only
            // about markers standing on the cell makes that irrelevant.
            map.events.ThingSpawned += OnThingChanged;
            map.events.ThingDespawned += OnThingChanged;
            subscribed = true;
        }

        internal void OnThingChanged(Thing thing)
        {
            if (thing?.def?.GetModExtension<BlocksTerrainFade>() == null) return;
            Invalidate();
        }

        /// <summary>
        /// One toggle shows both halves: the markers through this drawer, and
        /// the painted area through the area's own, which vanilla otherwise
        /// only draws while its designator is held.
        /// </summary>
        public override void MapComponentUpdate()
        {
            if (!ShowOverlay) return;
            Drawer.MarkForDraw();
            Drawer.CellBoolDrawerUpdate();
            Area_HardEdges.On(map)?.MarkForDraw();
        }
    }
}
