using System.Collections.Generic;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Marks a def whose presence forces hard terrain edges — no fade fringe
    /// painted in, and none painted out.
    ///
    /// Named for the BEHAVIOUR rather than for a carrier, because there are
    /// two:
    ///
    ///   - **Whole-tile** — the hard-edge pad, a foundation TerrainDef. All four
    ///     edges harden at once, which the engine already implements: the blend
    ///     gate's foundation clause is tested on both cells of every pair.
    ///   - **Sided** — the border trim, where the marked thing's `Rotation`
    ///     names WHICH edge. This is the reason the family exists.
    ///     An edge is a sided thing; a tile answer is only an approximation of
    ///     it, and the project has now learned that four times.
    ///
    /// Extension-keyed rather than defName-keyed so the behaviour is opt-in per
    /// def, and another mod's terrain or overlay can adopt it without us knowing
    /// about that mod. Nothing reads a field here — presence is the whole signal
    /// — but it stays a class rather than a tag so it can grow one later.
    ///
    /// Renaming this class is save-safe, unlike a `thingClass` rename:
    /// modExtensions are rebuilt from XML at every def load and are never
    /// scribed, so no save carries the old name. (It was `HardEdgeFoundation`
    /// until 2026-08-25, when the pad stopped being the only carrier.)
    ///
    /// See Patch_FoundationUnderFloor for what the marker buys on the pad, and
    /// why vanilla refuses to place one without it.
    /// </summary>
    public class BlocksTerrainFade : DefModExtension
    {
        /// <summary>
        /// Which edges this thing hardens, as quarter-turns clockwise FROM its
        /// own <c>Rotation</c> — so the values are the same whichever way the
        /// piece is turned, and one def covers all four facings.
        ///
        ///     straight strip   0        the edge it faces
        ///     double band      0, 2     both opposite edges
        ///     L corner         0, 1     two adjacent edges
        ///
        /// Null or empty means all four, which is what a rotationless carrier
        /// (the pad) implies. The pad does not actually read this — its veto is
        /// the engine's own foundation clause — but leaving the field
        /// unambiguous keeps the two members describable in one vocabulary.
        /// </summary>
        public List<int> edges;
    }
}
