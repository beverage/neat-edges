using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Lays a trim's strip out as geometry: pure functions from a shape, a
    /// rotation and a cell to vertices, UVs and triangles. Map printing and the
    /// placement ghost both build from here, so the two cannot drift.
    ///
    /// Every shape is bands along its tile's edges, each band an "arm": a quad
    /// from the edge a quarter tile into the tile, cut at 45 degrees where a
    /// neighbouring edge carries an arm too, so two arms meet in a mitre. The
    /// inside corner is the band square at one corner, split on its diagonal.
    ///
    /// A band is sampled across from the strip by its depth into the tile, and
    /// along by its WORLD position, so two tiles in a row share one coordinate at
    /// the joint between them and the strip carries on across it. The strip's
    /// top half is the band as it sits on a north edge and its bottom half the
    /// band on a south edge; west arms take the top half and east arms the
    /// bottom. That keeps the art's light absolute, lit from the north-west, as
    /// it is in every hand-drawn facing.
    ///
    /// Coordinates here are relative to the tile's centre, x east and z north,
    /// a tile spanning -0.5 to 0.5, already turned to the world.
    /// </summary>
    internal static class StripTrimGeometry
    {
        /// <summary>
        /// How far a band's quad reaches into its tile. Each half of the strip
        /// spans this depth, which fixes the strip's density: 64 rows to a
        /// quarter tile is 256 to a tile, the density of the hand-drawn art.
        /// </summary>
        internal const float Depth = 0.25f;

        /// <summary>The altitude PrintPlane gives a plane's north edge over its south.</summary>
        internal const float TopBias = 0.01f;

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
        /// The length of band one repeat of the strip covers, in tiles, chosen
        /// so its texels come out square: Neat Edges' own 256 x 128 strip, at
        /// 64 rows to a quarter tile, repeats once a tile.
        /// </summary>
        internal static float PeriodOf(Texture texture)
        {
            if (texture == null || texture.height < 2)
            {
                return 1f;
            }
            return Depth * texture.width / (texture.height / 2f);
        }

        /// <summary>
        /// Appends one trim. <paramref name="origin"/> is what the vertices are
        /// offset from: the thing's true centre on the map, or zero for a cached
        /// mesh. <paramref name="cell"/> is the cell the UVs are mapped from.
        /// The four lists stay in step, one UV and one colour per vertex,
        /// because a section's submesh is shared by everything printed with the
        /// same material.
        /// </summary>
        internal static void Append(List<Vector3> verts, List<Vector3> uvs, List<Color32> colors,
            List<int> tris, TrimPiece.Kind shape, Rot4 rot, IntVec3 cell, Vector3 origin, Color32 color,
            float period)
        {
            int r = rot.AsInt;
            const float h = 0.5f;
            const float d = Depth;

            if (shape == TrimPiece.Kind.InsideCorner)
            {
                // Facing north it fills the north-east band square. The half
                // against the east side continues the north band of the tile
                // to the east; the half against the north side continues the
                // east band of the tile to the north.
                int north = r;
                int east = (r + 1) & 3;
                Polygon(verts, uvs, colors, tris, north, cell, origin, color, period,
                    Turn(h, h, r), Turn(h, h - d, r), Turn(h - d, h - d, r));
                Polygon(verts, uvs, colors, tris, east, cell, origin, color, period,
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
                Polygon(verts, uvs, colors, tris, edge, cell, origin, color, period,
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

        /// <summary>
        /// One convex polygon, fan-triangulated, sampled as a band on
        /// <paramref name="edge"/> (0 north, 1 east, 2 south, 3 west).
        /// </summary>
        internal static void Polygon(List<Vector3> verts, List<Vector3> uvs, List<Color32> colors,
            List<int> tris, int edge, IntVec3 cell, Vector3 origin, Color32 color, float period,
            params Vector2[] points)
        {
            int start = verts.Count;
            foreach (Vector2 p in points)
            {
                verts.Add(origin + new Vector3(p.x, TopBias * (p.y + 0.5f), p.y));
                uvs.Add(UV(edge, cell, p.x, p.y, period));
                colors.Add(color);
            }
            for (int i = 1; i < points.Length - 1; i++)
            {
                tris.Add(start);
                tris.Add(start + i);
                tris.Add(start + i + 1);
            }
        }

        /// <summary>
        /// Where a point samples the strip. Across, by its depth from the band's
        /// outer edge: the top half for north and west bands, the bottom half
        /// for south and east, each half spanning <see cref="Depth"/>. Along, by
        /// its world coordinate on the band's axis, plus an offset that differs
        /// between the lines on either side of a tile and between a line's two
        /// sides, so a runner's two rails and two parallel runs do not start
        /// their strips in step. A whole number of repeats is taken off so the
        /// value stays small enough to keep its precision.
        /// </summary>
        internal static Vector3 UV(int edge, IntVec3 cell, float x, float z, float period)
        {
            float depth;
            double along;
            int origin;
            int line;
            int side;
            switch (edge)
            {
                case 0: depth = 0.5f - z; origin = cell.x; along = x; line = cell.z + 1; side = 1; break;
                case 1: depth = 0.5f - x; origin = cell.z; along = z; line = cell.x + 1; side = 1; break;
                case 2: depth = z + 0.5f; origin = cell.x; along = x; line = cell.z; side = 0; break;
                default: depth = x + 0.5f; origin = cell.z; along = z; line = cell.x; side = 0; break;
            }
            float half = depth / Depth * 0.5f;
            float v = edge == 0 || edge == 3 ? 1f - half : half;

            double offset = ((7 * line + 3 * side) & 15) * (double)period / 16.0;
            double start = origin + offset;
            double u = (start + 0.5 + along) / period - Math.Floor(start / period);
            return new Vector3((float)u, v, 0f);
        }
    }
}
