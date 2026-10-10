using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// How a strip is laid out: the band length its whole width covers, in
    /// tiles; how many bands it holds (2, or 3 with the side-lit band); how
    /// many variants sit along it (1 for a plain repeating strip); and how many
    /// layers of those bands it stacks (2 when it carries a paint overlay, see
    /// <see cref="TrimPiece.paintOverlay"/>), with the layer this samples.
    /// </summary>
    internal readonly struct StripLayout
    {
        internal readonly float period;
        internal readonly int bands;
        internal readonly int variants;
        internal readonly int layers;
        internal readonly int layer;

        internal StripLayout(float period, int bands = 2, int variants = 1, int layers = 1, int layer = 0)
        {
            this.period = period;
            this.bands = bands < 3 ? 2 : 3;
            this.variants = Math.Max(1, variants);
            this.layers = layers < 2 ? 1 : 2;
            this.layer = this.layers == 2 && layer == 1 ? 1 : 0;
        }

        /// <summary>The same strip, sampling its paint overlay.</summary>
        internal StripLayout Overlay => new StripLayout(period, bands, variants, layers, 1);

        /// <summary>The same strip, sampling its bands.</summary>
        internal StripLayout Bands => new StripLayout(period, bands, variants, layers, 0);
    }

    /// <summary>How one end of a diagonal is cut, read from what meets it there.</summary>
    internal struct DiagonalEnd
    {
        /// <summary>False: square to the diagonal, a plain end or a run's joint.</summary>
        internal bool mitre;

        /// <summary>The cut's unit direction, pointing into the band.</summary>
        internal double dirX;
        internal double dirZ;

        /// <summary>On the inside of the turn, where the two bands overlap.</summary>
        internal bool inner;

        /// <summary>
        /// On the outside of a turn into a straight, the gap the two square ends
        /// would leave is this diagonal's to fill, in the straight's mapping.
        /// </summary>
        internal bool wedge;
        internal int wedgeEdge;
        internal IntVec3 wedgeOffset;

        /// <summary>Everything above packed small, so meshes can be cached by it.</summary>
        internal int key;
    }

    internal struct DiagonalEnds
    {
        internal DiagonalEnd a;
        internal DiagonalEnd b;
    }

    /// <summary>
    /// Lays a trim's strip out as geometry: pure functions from a shape, a
    /// rotation and a cell to vertices, UVs and triangles. Map printing and the
    /// placement ghost both build from here, so the two cannot drift.
    ///
    /// Every shape but the diagonal is bands along its tile's edges, each band
    /// an "arm": a quad from the edge a quarter tile into the tile, cut at 45
    /// degrees where a neighbouring edge carries an arm too, so two arms meet in
    /// a mitre. The inside corner is the band square at one corner, split on
    /// its diagonal.
    ///
    /// A band is sampled across from the strip by its depth into the tile, and
    /// along by its WORLD position, so two tiles in a row share one coordinate at
    /// the joint between them and the strip carries on across it. The strip's
    /// top band is the band as it sits on a north edge and the next the band on
    /// a south edge; west arms take the north band and east arms the south. That
    /// keeps the art's light absolute, lit from the north-west, as it is in
    /// every hand-drawn facing. A three-band strip adds the side-lit band below.
    /// A strip with a paint overlay holds its bands in its top half and the
    /// overlay, the same bands mirrored, in its bottom half; a trim prints the
    /// same polygons twice, once sampling each.
    ///
    /// The diagonal stands on a diagonal wall's cell, its band along the wall's
    /// face in the open half, reaching past the cell into the corners of the two
    /// cells beside it. Each end reads what else ends at that corner (see
    /// <see cref="DiagonalEndsAt"/>): a run's next diagonal shares a square
    /// joint, and a straight band or a diagonal turning there meets it in a
    /// mitre on the bisector. On the inside of a turn the two overlap and the
    /// diagonal prints <see cref="DiagonalLift"/> higher, so its mitre wins; on
    /// the outside it fills the wedge the two square ends would leave, drawing
    /// the straight's share in the straight's own mapping.
    ///
    /// devtools/strip_trim.py mirrors all of this, and both are held to
    /// devtools/strip_trim_geometry.txt (the harness's trims.geometry case).
    ///
    /// Coordinates here are relative to the tile's centre, x east and z north,
    /// a tile spanning -0.5 to 0.5, already turned to the world.
    /// </summary>
    internal static class StripTrimGeometry
    {
        /// <summary>
        /// How far a band's quad reaches into its tile. Each band of the strip
        /// spans this depth, which fixes the strip's density: 64 rows to a
        /// quarter tile is 256 to a tile, the density of the hand-drawn art.
        /// </summary>
        internal const float Depth = 0.25f;

        /// <summary>The altitude PrintPlane gives a plane's north edge over its south.</summary>
        internal const float TopBias = 0.01f;

        /// <summary>
        /// How much higher a diagonal prints than the trims around it. A straight
        /// in the row south of it prints <see cref="TopBias"/> higher at any
        /// point, by PrintPlane's rule, so the lift has to clear that; it stays
        /// well under the gap to the next altitude layer.
        /// </summary>
        internal const float DiagonalLift = 0.02f;

        internal const int North = 0;
        internal const int South = 1;
        internal const int Side = 2;

        internal static readonly double R2 = Math.Sqrt(0.5);

        internal static readonly int[] StraightArms = { 0 };
        internal static readonly int[] RunnerArms = { 0, 2 };
        internal static readonly int[] CornerArms = { 0, 1 };
        internal static readonly int[] EndCapArms = { 3, 0, 1 };
        internal static readonly int[] FrameArms = { 0, 1, 2, 3 };

        /// <summary>
        /// The edges a shape draws bands along, in quarter-turns clockwise from
        /// the thing's Rotation: the same convention, and for these five the
        /// same values, as <see cref="BlocksTerrainFade.edges"/>.
        /// </summary>
        internal static int[] Arms(TrimPiece.Kind shape)
        {
            switch (shape)
            {
                case TrimPiece.Kind.Straight: return StraightArms;
                case TrimPiece.Kind.Runner: return RunnerArms;
                case TrimPiece.Kind.Corner: return CornerArms;
                case TrimPiece.Kind.EndCap: return EndCapArms;
                case TrimPiece.Kind.Frame: return FrameArms;
                default: return Array.Empty<int>();
            }
        }

        /// <summary>
        /// The band length the strip's whole width covers, in tiles, chosen so
        /// its texels come out square: Neat Edges' own 256-wide strip, at 64
        /// rows to a band, repeats once a tile, and a strip of four 256-wide
        /// variants covers four. A paint overlay stacks the bands twice, so
        /// the strip holds twice the rows for the same band height.
        /// </summary>
        internal static float PeriodOf(Texture texture, int bands = 2, int layers = 1)
        {
            int rows = bands * Math.Max(1, layers);
            if (texture == null || texture.height < rows)
            {
                return 1f;
            }
            return Depth * texture.width / (texture.height / (float)rows);
        }

        // ---- sampling the strip ---------------------------------------------

        /// <summary>
        /// V for a point <paramref name="fraction"/> of the way through a band's
        /// depth, 0 at its outer edge. A side band asked of a two-band strip
        /// reads the north band. With a paint overlay the bands fill the top
        /// half and the overlay is the same bands mirrored into the bottom half,
        /// so where the halves meet a band's inner edge meets its own copy's,
        /// and the strip's last row is the overlay's north outer edge: every
        /// band meets one like it, which keeps a mipmap from blending unlike
        /// rows across the middle.
        /// </summary>
        internal static double BandV(int band, double fraction, int bands, int layers = 1, int layer = 0)
        {
            double h = 1.0 / (bands * Math.Max(1, layers));
            if (band == Side && bands < 3)
            {
                band = North;
            }
            double v;
            if (band == North)
            {
                v = 1.0 - fraction * h;
            }
            else if (band == South)
            {
                v = 1.0 - 2.0 * h + fraction * h;
            }
            else
            {
                v = 1.0 - 2.0 * h - fraction * h;
            }
            return layer == 1 ? 1.0 - v : v;
        }

        /// <summary>Which variant a stretch of band draws: a 32-bit hash, the same in Python.</summary>
        internal static int VariantPick(int line, int side, long segment, int variants)
        {
            unchecked
            {
                uint h = (uint)line * 0x9E3779B1u;
                h ^= (uint)side * 0x85EBCA77u;
                h ^= (uint)segment * 0xC2B2AE3Du;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (int)(h % (uint)variants);
            }
        }

        /// <summary>
        /// U for a point <paramref name="local"/> along a band from
        /// <paramref name="origin"/>, both in tiles. One variant: the strip
        /// repeats along the band, every line and side starting at one of 16
        /// offsets, so parallel runs and a runner's two rails do not start in
        /// step; a whole number of repeats is taken off so the value keeps its
        /// precision. Several: the point lies in stretch
        /// <paramref name="segment"/>, one variant long, which draws the variant
        /// the hash picks.
        /// </summary>
        internal static double BandU(double origin, double local, int line, int side, StripLayout layout, long segment)
        {
            double period = layout.period;
            if (layout.variants <= 1)
            {
                double offset = ((7 * line + 3 * side) & 15) * period / 16.0;
                double start = origin + offset;
                return (start + local) / period - Math.Floor(start / period);
            }
            double stretch = period / layout.variants;
            int pick = VariantPick(line, side, segment, layout.variants);
            return (pick + (origin + local) / stretch - segment) / layout.variants;
        }

        /// <summary>
        /// For a band on <paramref name="edge"/> of <paramref name="cell"/>: its
        /// origin along the band, the line and side it lies on, and which band of
        /// the strip it draws. Local positions run from the cell's west or south
        /// edge.
        /// </summary>
        internal static void StraightFrame(int edge, IntVec3 cell, out int origin, out int line, out int side, out int band)
        {
            switch (edge)
            {
                case 0: origin = cell.x; line = cell.z + 1; side = 1; band = North; break;
                case 1: origin = cell.z; line = cell.x + 1; side = 1; band = South; break;
                case 2: origin = cell.x; line = cell.z; side = 0; band = South; break;
                default: origin = cell.z; line = cell.x; side = 0; band = North; break;
            }
        }

        internal static double StraightDepth(int edge, double x, double z)
        {
            switch (edge)
            {
                case 0: return 0.5 - z;
                case 1: return 0.5 - x;
                case 2: return z + 0.5;
                default: return x + 0.5;
            }
        }

        internal static double StraightLocal(int edge, double x, double z) =>
            edge == 0 || edge == 2 ? 0.5 + x : 0.5 + z;

        /// <summary>Where a point on a straight band samples the strip.</summary>
        internal static Vector3 UV(int edge, IntVec3 cell, float x, float z, StripLayout layout, long segment = 0)
        {
            StraightFrame(edge, cell, out int origin, out int line, out int side, out int band);
            double u = BandU(origin, StraightLocal(edge, x, z), line, side, layout, segment);
            double v = BandV(band, StraightDepth(edge, x, z) / Depth, layout.bands, layout.layers, layout.layer);
            return new Vector3((float)u, (float)v, 0f);
        }

        // ---- the straight family ----------------------------------------------

        /// <summary>
        /// Appends one trim. <paramref name="origin"/> is what the vertices are
        /// offset from: the thing's true centre on the map, or zero for a cached
        /// mesh. <paramref name="cell"/> is the cell the UVs are mapped from, and
        /// <paramref name="ends"/> how a diagonal's ends are cut. The four lists
        /// stay in step, one UV and one colour per vertex, because a section's
        /// submesh is shared by everything printed with the same material.
        /// </summary>
        internal static void Append(List<Vector3> verts, List<Vector3> uvs, List<Color32> colors,
            List<int> tris, TrimPiece.Kind shape, Rot4 rot, IntVec3 cell, Vector3 origin, Color32 color,
            StripLayout layout, DiagonalEnds ends = default)
        {
            int r = rot.AsInt;
            const float h = 0.5f;
            const float d = Depth;

            if (shape == TrimPiece.Kind.Diagonal)
            {
                AppendDiagonal(verts, uvs, colors, tris, r, cell, origin, color, layout, ends);
                return;
            }

            if (shape == TrimPiece.Kind.InsideCorner)
            {
                // Facing north it fills the north-east band square. The half
                // against the east side continues the north band of the tile
                // to the east; the half against the north side continues the
                // east band of the tile to the north.
                int north = r;
                int east = (r + 1) & 3;
                Polygon(verts, uvs, colors, tris, north, cell, origin, color, layout,
                    Turn(h, h, r), Turn(h, h - d, r), Turn(h - d, h - d, r));
                Polygon(verts, uvs, colors, tris, east, cell, origin, color, layout,
                    Turn(h, h, r), Turn(h - d, h - d, r), Turn(h - d, h, r));
                return;
            }

            int[] arms = Arms(shape);
            int edges = 0;
            foreach (int arm in arms)
            {
                edges |= 1 << ((r + arm) & 3);
            }
            foreach (int arm in arms)
            {
                int edge = (r + arm) & 3;
                // The arm on the north edge, cut back on either side where the
                // neighbouring edge has an arm of its own, then turned to its
                // edge. Listed clockwise seen from above, as PrintPlane winds.
                float cutWest = (edges & (1 << ((edge + 3) & 3))) != 0 ? d : 0f;
                float cutEast = (edges & (1 << ((edge + 1) & 3))) != 0 ? d : 0f;
                Polygon(verts, uvs, colors, tris, edge, cell, origin, color, layout,
                    Turn(-h, h, edge), Turn(h, h, edge),
                    Turn(h - cutEast, h - d, edge), Turn(-h + cutWest, h - d, edge));
            }
        }

        /// <summary>A point on the north side of the tile, turned clockwise by quarter turns.</summary>
        internal static Vector2 Turn(float x, float z, int quarterTurns)
        {
            for (int i = 0; i < (quarterTurns & 3); i++)
            {
                float was = x;
                x = z;
                z = -was;
            }
            return new Vector2(x, z);
        }

        internal static void TurnD(ref double x, ref double z, int quarterTurns)
        {
            for (int i = 0; i < (quarterTurns & 3); i++)
            {
                double was = x;
                x = z;
                z = -was;
            }
        }

        /// <summary>
        /// One convex polygon, sampled as a band on <paramref name="edge"/> (0
        /// north, 1 east, 2 south, 3 west) and cut where a variant's stretch
        /// ends.
        /// </summary>
        internal static void Polygon(List<Vector3> verts, List<Vector3> uvs, List<Color32> colors,
            List<int> tris, int edge, IntVec3 cell, Vector3 origin, Color32 color, StripLayout layout,
            params Vector2[] points)
        {
            StraightFrame(edge, cell, out int bandOrigin, out _, out _, out _);
            foreach ((long segment, List<Vector2> piece) in Stretches(new List<Vector2>(points),
                         p => bandOrigin + StraightLocal(edge, p.x, p.y), layout))
            {
                Emit(verts, uvs, colors, tris, origin, color, piece, 0f,
                    p => UV(edge, cell, p.x, p.y, layout, segment));
            }
        }

        /// <summary>Writes one convex polygon, fan-triangulated, listed clockwise seen from above.</summary>
        internal static void Emit(List<Vector3> verts, List<Vector3> uvs, List<Color32> colors, List<int> tris,
            Vector3 origin, Color32 color, List<Vector2> points, float lift, Func<Vector2, Vector3> uvOf)
        {
            int start = verts.Count;
            foreach (Vector2 p in points)
            {
                verts.Add(origin + new Vector3(p.x, TopBias * (p.y + 0.5f) + lift, p.y));
                uvs.Add(uvOf(p));
                colors.Add(color);
            }
            for (int i = 1; i < points.Count - 1; i++)
            {
                tris.Add(start);
                tris.Add(start + i);
                tris.Add(start + i + 1);
            }
        }

        // ---- cutting at a variant's stretch -------------------------------------

        /// <summary>Sutherland-Hodgman against f(p) &gt;= 0, or &lt;= 0; f is affine.</summary>
        internal static List<Vector2> Clip(List<Vector2> points, Func<Vector2, double> f, bool keepAbove)
        {
            var output = new List<Vector2>(points.Count + 2);
            int n = points.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = points[i];
                Vector2 q = points[(i + 1) % n];
                double fp = f(p);
                double fq = f(q);
                bool ip = keepAbove ? fp >= -1e-9 : fp <= 1e-9;
                bool iq = keepAbove ? fq >= -1e-9 : fq <= 1e-9;
                if (ip)
                {
                    output.Add(p);
                }
                if (ip != iq && Math.Abs(fp - fq) > 1e-12)
                {
                    double s = fp / (fp - fq);
                    if (s > 1e-9 && s < 1 - 1e-9)
                    {
                        output.Add(new Vector2((float)(p.x + (q.x - p.x) * s), (float)(p.y + (q.y - p.y) * s)));
                    }
                }
            }
            return output;
        }

        /// <summary>
        /// A polygon cut where a variant's stretch ends, as (segment, polygon)
        /// pairs; one pair, segment 0, when the strip holds a single variant.
        /// <paramref name="world"/> is a point's position along the band.
        /// </summary>
        internal static List<(long, List<Vector2>)> Stretches(List<Vector2> points, Func<Vector2, double> world,
            StripLayout layout)
        {
            var output = new List<(long, List<Vector2>)>(1);
            if (layout.variants <= 1)
            {
                output.Add((0L, points));
                return output;
            }
            double stretch = layout.period / (double)layout.variants;
            double lo = double.MaxValue;
            double hi = double.MinValue;
            foreach (Vector2 p in points)
            {
                double value = world(p) / stretch;
                lo = Math.Min(lo, value);
                hi = Math.Max(hi, value);
            }
            long first = (long)Math.Floor(lo + 1e-9);
            long last = (long)Math.Ceiling(hi - 1e-9) - 1;
            for (long k = first; k <= last; k++)
            {
                long index = k;
                List<Vector2> piece = Clip(points, p => world(p) / stretch - index, true);
                piece = Clip(piece, p => world(p) / stretch - (index + 1), false);
                if (piece.Count >= 3)
                {
                    output.Add((k, piece));
                }
            }
            return output;
        }

        // ---- the diagonal -------------------------------------------------------

        /// <summary>The face's tail A, head B, unit along t and inward n, tile-local.</summary>
        internal static void DiagonalFrame(int rot, out double ax, out double az, out double bx, out double bz,
            out double tx, out double tz, out double nx, out double nz)
        {
            ax = -0.5; az = -0.5; TurnD(ref ax, ref az, rot);
            bx = 0.5; bz = 0.5; TurnD(ref bx, ref bz, rot);
            tx = R2; tz = R2; TurnD(ref tx, ref tz, rot);
            nx = R2; nz = -R2; TurnD(ref nx, ref nz, rot);
        }

        /// <summary>
        /// Which band of the strip a diagonal draws: north with its wall to the
        /// north-west, south with it south-east, the side band otherwise.
        /// </summary>
        internal static int DiagonalBand(int rot)
        {
            switch (rot & 3)
            {
                case 0: return North;
                case 2: return South;
                default: return Side;
            }
        }

        /// <summary>
        /// The line a diagonal's face lies on, constant along a run, and which
        /// side of it the band lies: for its offset and its variant hash.
        /// </summary>
        internal static void DiagonalLine(int rot, IntVec3 cell, out int line, out int side)
        {
            line = (rot & 1) == 0 ? cell.x - cell.z : cell.x + cell.z + 1;
            side = (rot & 3) >> 1;
        }

        internal static double DiagonalWorld(int rot, IntVec3 cell, double x, double z)
        {
            DiagonalFrame(rot, out _, out _, out _, out _, out double tx, out double tz, out _, out _);
            double origin = cell.x * tx + cell.z * tz;
            return origin + ((0.5 + x) * tx + (0.5 + z) * tz);
        }

        /// <summary>Where a point on a diagonal's band samples the strip.</summary>
        internal static Vector3 DiagonalUV(int rot, IntVec3 cell, double x, double z, StripLayout layout, long segment)
        {
            DiagonalFrame(rot, out double ax, out double az, out _, out _, out double tx, out double tz,
                out double nx, out double nz);
            double depth = (x - ax) * nx + (z - az) * nz;
            DiagonalLine(rot, cell, out int line, out int side);
            double origin = cell.x * tx + cell.z * tz;
            double local = (0.5 + x) * tx + (0.5 + z) * tz;
            double u = BandU(origin, local, line, side, layout, segment);
            double v = BandV(DiagonalBand(rot), depth / Depth, layout.bands, layout.layers, layout.layer);
            return new Vector3((float)u, (float)v, 0f);
        }

        /// <summary>
        /// A band end a piece offers a diagonal. A band runs with its inside on
        /// its right, so tail and head are world corners and along an integer
        /// direction; an end is square unless the piece mitres it into another
        /// of its own arms.
        /// </summary>
        internal struct OfferedEnd
        {
            internal IntVec2 tail;
            internal IntVec2 head;
            internal IntVec2 along;
            internal bool tailSquare;
            internal bool headSquare;
            internal bool diagonal;
            internal int edge;
        }

        internal static IntVec2 Corner(IntVec3 cell, double x, double z) =>
            new IntVec2((int)Math.Round(cell.x + 0.5 + x), (int)Math.Round(cell.z + 0.5 + z));

        internal static void OfferedEnds(TrimPiece.Kind shape, int rot, IntVec3 cell, List<OfferedEnd> output)
        {
            if (shape == TrimPiece.Kind.Diagonal)
            {
                DiagonalFrame(rot, out double ax, out double az, out double bx, out double bz, out _, out _, out _, out _);
                int ux = 1;
                int uz = 1;
                for (int i = 0; i < (rot & 3); i++)
                {
                    int was = ux;
                    ux = uz;
                    uz = -was;
                }
                output.Add(new OfferedEnd
                {
                    tail = Corner(cell, ax, az),
                    head = Corner(cell, bx, bz),
                    along = new IntVec2(ux, uz),
                    tailSquare = true,
                    headSquare = true,
                    diagonal = true,
                    edge = -1,
                });
                return;
            }
            int[] arms = Arms(shape);
            int edges = 0;
            foreach (int arm in arms)
            {
                edges |= 1 << ((rot + arm) & 3);
            }
            foreach (int arm in arms)
            {
                int edge = (rot + arm) & 3;
                Vector2 tail = Turn(-0.5f, 0.5f, edge);
                Vector2 head = Turn(0.5f, 0.5f, edge);
                Vector2 along = Turn(1f, 0f, edge);
                output.Add(new OfferedEnd
                {
                    tail = Corner(cell, tail.x, tail.y),
                    head = Corner(cell, head.x, head.y),
                    along = new IntVec2((int)Math.Round(along.x), (int)Math.Round(along.y)),
                    tailSquare = (edges & (1 << ((edge + 3) & 3))) == 0,
                    headSquare = (edges & (1 << ((edge + 1) & 3))) == 0,
                    diagonal = false,
                    edge = edge,
                });
            }
        }

        /// <summary>
        /// The two ends of a diagonal at <paramref name="cell"/>, read from the
        /// trims standing around it. A neighbour joins an end when its band
        /// carries on from it with its inside on the same side: an end whose
        /// tail is this diagonal's head, or whose head is its tail. The next
        /// diagonal of a run is preferred, then a diagonal turning, then a
        /// straight; ties go to the first found, cells west to east and, within a
        /// column, south to north.
        /// </summary>
        internal static DiagonalEnds DiagonalEndsAt(IntVec3 cell, int rot,
            Func<IntVec3, IEnumerable<(TrimPiece.Kind shape, int rot)>> piecesAt)
        {
            DiagonalFrame(rot, out double ax, out double az, out double bx, out double bz, out _, out _,
                out double nx, out double nz);
            IntVec2 tail = Corner(cell, ax, az);
            IntVec2 head = Corner(cell, bx, bz);
            int ux = 1;
            int uz = 1;
            for (int i = 0; i < (rot & 3); i++)
            {
                int was = ux;
                ux = uz;
                uz = -was;
            }
            var along = new IntVec2(ux, uz);

            bool haveHead = false, haveTail = false;
            int headRank = int.MaxValue, tailRank = int.MaxValue;
            OfferedEnd headPartner = default, tailPartner = default;
            IntVec3 headCell = IntVec3.Invalid, tailCell = IntVec3.Invalid;
            var offered = new List<OfferedEnd>(4);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var q = new IntVec3(cell.x + dx, 0, cell.z + dz);
                    foreach ((TrimPiece.Kind shape, int pieceRot) in piecesAt(q))
                    {
                        offered.Clear();
                        OfferedEnds(shape, pieceRot, q, offered);
                        foreach (OfferedEnd end in offered)
                        {
                            int rank = end.along == along ? 0 : end.diagonal ? 1 : 2;
                            if (end.tailSquare && end.tail == head && rank < headRank)
                            {
                                haveHead = true;
                                headRank = rank;
                                headPartner = end;
                                headCell = q;
                            }
                            if (end.headSquare && end.head == tail && rank < tailRank)
                            {
                                haveTail = true;
                                tailRank = rank;
                                tailPartner = end;
                                tailCell = q;
                            }
                        }
                    }
                }
            }
            var ends = new DiagonalEnds();
            if (haveTail)
            {
                ends.a = MakeEnd(tailPartner.along, along, nx, nz, tailPartner.diagonal, tailPartner.edge,
                    tailCell - cell);
            }
            if (haveHead)
            {
                ends.b = MakeEnd(along, headPartner.along, nx, nz, headPartner.diagonal, headPartner.edge,
                    headCell - cell);
            }
            return ends;
        }

        /// <summary>
        /// The cut where a band arriving along <paramref name="tIn"/> turns to
        /// leave along <paramref name="tOut"/>. A run carries straight on and
        /// stays square, and so does a turn sharper than a right angle; anything
        /// else is a mitre on the bisector.
        /// </summary>
        internal static DiagonalEnd MakeEnd(IntVec2 tIn, IntVec2 tOut, double nx, double nz, bool partnerDiagonal,
            int partnerEdge, IntVec3 partnerOffset)
        {
            var end = new DiagonalEnd();
            if (tIn == tOut)
            {
                return end;
            }
            double li = Math.Sqrt(tIn.x * tIn.x + tIn.z * tIn.z);
            double lo = Math.Sqrt(tOut.x * tOut.x + tOut.z * tOut.z);
            double uix = tIn.x / li, uiz = tIn.z / li;
            double uox = tOut.x / lo, uoz = tOut.z / lo;
            if (uix * uox + uiz * uoz < -1e-9)
            {
                return end;
            }
            double mx = uox - uix, mz = uoz - uiz;
            double ml = Math.Sqrt(mx * mx + mz * mz);
            mx /= ml;
            mz /= ml;
            if (mx * nx + mz * nz < 0)
            {
                mx = -mx;
                mz = -mz;
            }
            end.mitre = true;
            end.dirX = mx;
            end.dirZ = mz;
            end.inner = uix * uoz - uiz * uox < 0;
            if (!end.inner && !partnerDiagonal)
            {
                end.wedge = true;
                end.wedgeEdge = partnerEdge;
                end.wedgeOffset = partnerOffset;
            }
            end.key = 1
                | (end.inner ? 2 : 0)
                | ((tIn.x + 1) << 2) | ((tIn.z + 1) << 4) | ((tOut.x + 1) << 6) | ((tOut.z + 1) << 8)
                | (end.wedge ? 1 << 10 : 0) | ((end.wedge ? end.wedgeEdge : 0) << 11)
                | ((partnerOffset.x + 1) << 13) | ((partnerOffset.z + 1) << 15);
            return end;
        }

        /// <summary>
        /// A diagonal: its band, cut at each end as <paramref name="ends"/> says
        /// and at every variant's stretch, then the wedge of any outside turn
        /// into a straight, end A's before end B's.
        /// </summary>
        internal static void AppendDiagonal(List<Vector3> verts, List<Vector3> uvs, List<Color32> colors,
            List<int> tris, int rot, IntVec3 cell, Vector3 origin, Color32 color, StripLayout layout,
            DiagonalEnds ends)
        {
            DiagonalFrame(rot, out double ax, out double az, out double bx, out double bz, out _, out _,
                out double nx, out double nz);
            double dax = ends.a.mitre ? ends.a.dirX : nx, daz = ends.a.mitre ? ends.a.dirZ : nz;
            double dbx = ends.b.mitre ? ends.b.dirX : nx, dbz = ends.b.mitre ? ends.b.dirZ : nz;
            double sa = Depth / (dax * nx + daz * nz);
            double sb = Depth / (dbx * nx + dbz * nz);
            var innerA = new Vector2((float)(ax + sa * dax), (float)(az + sa * daz));
            var innerB = new Vector2((float)(bx + sb * dbx), (float)(bz + sb * dbz));
            var a = new Vector2((float)ax, (float)az);
            var b = new Vector2((float)bx, (float)bz);
            var band = new List<Vector2> { a, b, innerB, innerA };

            foreach ((long segment, List<Vector2> piece) in Stretches(band,
                         p => DiagonalWorld(rot, cell, p.x, p.y), layout))
            {
                Emit(verts, uvs, colors, tris, origin, color, piece, DiagonalLift,
                    p => DiagonalUV(rot, cell, p.x, p.y, layout, segment));
            }

            Wedge(verts, uvs, colors, tris, cell, origin, color, layout, a, innerA, ends.a);
            Wedge(verts, uvs, colors, tris, cell, origin, color, layout, b, innerB, ends.b);
        }

        /// <summary>
        /// The straight's share of an outside turn's gap: from its square end to
        /// the mitre, in its own mapping, so its lines run on to the bisector.
        /// Drawn in the diagonal's colour, the one compromise of the scheme.
        /// </summary>
        internal static void Wedge(List<Vector3> verts, List<Vector3> uvs, List<Color32> colors, List<int> tris,
            IntVec3 cell, Vector3 origin, Color32 color, StripLayout layout, Vector2 point, Vector2 innerCorner,
            DiagonalEnd end)
        {
            if (!end.wedge)
            {
                return;
            }
            int edge = end.wedgeEdge;
            Vector2 ns = Turn(0f, -1f, edge);
            var square = new Vector2(point.x + Depth * ns.x, point.y + Depth * ns.y);
            List<Vector2> triangle = Clockwise(new List<Vector2> { point, square, innerCorner });
            IntVec3 partner = cell + end.wedgeOffset;
            float ox = end.wedgeOffset.x;
            float oz = end.wedgeOffset.z;
            var moved = new List<Vector2>(3);
            foreach (Vector2 p in triangle)
            {
                moved.Add(new Vector2(p.x - ox, p.y - oz));
            }
            StraightFrame(edge, partner, out int bandOrigin, out _, out _, out _);
            foreach ((long segment, List<Vector2> piece) in Stretches(moved,
                         p => bandOrigin + StraightLocal(edge, p.x, p.y), layout))
            {
                var back = new List<Vector2>(piece.Count);
                foreach (Vector2 p in piece)
                {
                    back.Add(new Vector2(p.x + ox, p.y + oz));
                }
                Emit(verts, uvs, colors, tris, origin, color, back, DiagonalLift,
                    p => UV(edge, partner, p.x - ox, p.y - oz, layout, segment));
            }
        }

        internal static List<Vector2> Clockwise(List<Vector2> points)
        {
            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 p = points[i];
                Vector2 q = points[(i + 1) % points.Count];
                area += (double)p.x * q.y - (double)q.x * p.y;
            }
            if (area > 0)
            {
                points.Reverse();
            }
            return points;
        }
    }
}
