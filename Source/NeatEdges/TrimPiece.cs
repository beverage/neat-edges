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
    /// Other mods name this class, its field and the six values in their XML, so
    /// all three are permanent once published: add, never rename.
    ///
    /// The shape is never derived from <see cref="BlocksTerrainFade.edges"/>.
    /// The two answer different questions, and the inside corner carries the
    /// one and not the other on purpose: it draws a corner of its tile and
    /// hardens no edge.
    /// </summary>
    public class TrimPiece : DefModExtension
    {
        /// <summary>
        /// The six shapes, each a set of bands along its tile's edges, turned
        /// with the thing's <c>Rotation</c>. Facing north: Straight covers the
        /// north edge; Corner north and east; Runner north and south; EndCap
        /// west, north and east; Frame all four. InsideCorner is the square
        /// between the north and east bands of the two neighbouring tiles, which
        /// a line turning around an inside corner otherwise leaves open.
        /// </summary>
        public enum Kind
        {
            Straight,
            Corner,
            InsideCorner,
            Runner,
            EndCap,
            Frame,
        }

        public Kind shape;

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
    }
}
