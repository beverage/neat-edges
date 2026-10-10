using System.Collections.Generic;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Names the shape of a trim that <see cref="Graphic_StripTrim"/> draws. The
    /// graphic class lays the def's strip texture out as geometry for that shape,
    /// so a def opts in with two lines of XML and no art per facing:
    ///
    ///     <graphicData>
    ///       <texPath>MyMod/Trims/MyStrip</texPath>
    ///       <graphicClass>NeatEdges.Graphic_StripTrim</graphicClass>
    ///     </graphicData>
    ///     <modExtensions>
    ///       <li Class="NeatEdges.TrimPiece"><shape>Straight</shape></li>
    ///     </modExtensions>
    ///
    /// Other mods name this class, its fields and the shape values in their XML,
    /// so all of them are permanent once published: add, never rename.
    ///
    /// The shape is never derived from <see cref="BlocksTerrainFade.edges"/>.
    /// The two answer different questions, and the inside corner carries the
    /// one and not the other on purpose: it draws a corner of its tile and
    /// hardens no edge.
    /// </summary>
    public class TrimPiece : DefModExtension
    {
        /// <summary>
        /// The shapes. The first six are bands along their tile's edges, turned
        /// with the thing's <c>Rotation</c>. Facing north: Straight covers the
        /// north edge; Corner north and east; Runner north and south; EndCap
        /// west, north and east; Frame all four. InsideCorner is the square
        /// between the north and east bands of the two neighbouring tiles, which
        /// a line turning around an inside corner otherwise leaves open.
        ///
        /// Diagonal follows a diagonal wall: it stands on the wall's cell with
        /// its band along the wall's face in the open half. Facing north the wall
        /// fills the cell's north-west half; each rotation turns that clockwise.
        /// It reads what ends at its two corners and cuts each end to meet it,
        /// so a run of diagonals joins square and a turn into a straight or
        /// another diagonal joins in a mitre. Straights never read their
        /// neighbours.
        /// </summary>
        public enum Kind
        {
            Straight,
            Corner,
            InsideCorner,
            Runner,
            EndCap,
            Frame,
            Diagonal,
        }

        public Kind shape;

        /// <summary>
        /// How many bands the strip holds, top to bottom: 2, the band on a north
        /// edge (outer edge first) and the band on a south edge (outer edge
        /// last); or 3, which adds below them the band a diagonal draws when its
        /// wall lies north-east or south-west, lit side-on, outer edge first.
        /// A two-band strip draws those diagonals from its north band. Every
        /// band is the same height, and that height is a quarter tile.
        /// </summary>
        public int bands = 2;

        /// <summary>
        /// How many variants sit side by side along the strip. With one, the
        /// strip repeats along its band, each line of trims starting it at its
        /// own offset. With several, the strip's width is split into that many
        /// equal stretches and every stretch of band draws one, picked by a hash
        /// of where it lies, so an irregular pattern never repeats on a beat.
        /// Each variant must begin and end alike for the joints to vanish.
        /// </summary>
        public int variants = 1;

        /// <summary>
        /// Whether the strip carries a paint overlay, drawn over its bands. The
        /// bands then fill the strip's top half, and its bottom half holds the
        /// overlay: the same layout again, mirrored, so the strip's last row is
        /// the overlay's north band at its outer edge. Paint colours the overlay
        /// alone and the bands keep the stuff's colour; unpainted, both take the
        /// stuff's. The vine keeps its leaves and stem there, so green paint
        /// greens the vine and leaves the border wood or stone.
        /// </summary>
        public bool paintOverlay;

        /// <summary>
        /// The piece a thing of this def draws. A blueprint's def is the one the
        /// engine generated for it, which copies the graphic but not the
        /// extensions, so this looks through it to the def it builds.
        /// </summary>
        internal static TrimPiece For(ThingDef def)
        {
            if (def == null)
            {
                return null;
            }
            ThingDef built = def.entityDefToBuild as ThingDef ?? def;
            return built.GetModExtension<TrimPiece>();
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (bands != 2 && bands != 3)
            {
                yield return $"NeatEdges.TrimPiece bands is {bands}; a strip holds 2 bands or 3";
            }
            if (variants < 1)
            {
                yield return $"NeatEdges.TrimPiece variants is {variants}; it must be at least 1";
            }
        }
    }
}
