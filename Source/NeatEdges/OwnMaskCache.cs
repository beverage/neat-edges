using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Memoises per-cell own-masks for the duration of one operation.
    ///
    /// WHY THIS EXISTS
    ///
    /// `EdgeMaskAt` costs up to 24 own-mask lookups per cell — 8 building the
    /// base mask (four cardinals, each asking both sides of its edge) and 16
    /// sealing corners (four diagonals, each asking two neighbours). Almost all
    /// of it is the SAME few cells: a cell's own mask is recomputed four times
    /// inside `BaseMaskAt` alone, and every neighbour is recomputed again by
    /// each of the up-to-eight cells that touch it.
    ///
    /// And the empty case is the worst case — every short-circuit in that path
    /// fires only when something IS hardened, so terrain with no markers near
    /// it pays full price.
    ///
    /// Measured in calls, over a 250x250 map:
    ///
    ///     section regen (17x17)   ~6,900  ->    361   (19x)
    ///     overlay rebuild (whole) ~1.5M   -> 62,500   (24x)
    ///
    /// The overlay is why this matters rather than being a tidy-up:
    /// `CellBoolDrawer.RegenerateMesh` walks the entire map calling the getter
    /// per cell, and it is marked dirty on every marker spawned — so dragging a
    /// run of them paid 1.5M lookups per frame.
    ///
    /// LIFETIME
    ///
    /// Deliberately per-operation, never long-lived. A cache that outlived its
    /// operation would need invalidating whenever a marker moved, and a stale
    /// edge mask is invisible until someone notices a fringe that should not be
    /// there. Building one costs a single array allocation; getting
    /// invalidation subtly wrong costs a bug that only shows up in play.
    /// </summary>
    internal sealed class OwnMaskCache
    {
        /// <summary>Not yet computed. Zero is a legitimate mask.</summary>
        internal const int Unset = -1;

        internal readonly Map map;
        internal readonly CellRect rect;
        internal readonly int[] values;

        internal OwnMaskCache(Map map, CellRect rect)
        {
            this.map = map;
            this.rect = rect;

            values = new int[rect.Width * rect.Height];
            for (int i = 0; i < values.Length; i++) values[i] = Unset;
        }

        /// <summary>
        /// Cells outside the covered rect fall through to a direct computation
        /// rather than failing, so a caller may size the rect to whatever it
        /// expects to touch without having to be exactly right.
        /// </summary>
        internal int MaskAt(IntVec3 cell)
        {
            if (!rect.Contains(cell))
            {
                return Patch_SidedFadeBlock.ComputeOwnMask(cell, map);
            }

            int i = (cell.z - rect.minZ) * rect.Width + (cell.x - rect.minX);

            int cached = values[i];
            if (cached != Unset) return cached;

            int computed = Patch_SidedFadeBlock.ComputeOwnMask(cell, map);
            values[i] = computed;
            return computed;
        }
    }
}
