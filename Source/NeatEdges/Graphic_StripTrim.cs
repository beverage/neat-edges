using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Draws a trim from one strip texture laid out as geometry, instead of one
    /// texture per facing per shape. The def names the strip as its texPath and
    /// its shape with a <see cref="TrimPiece"/>; <see cref="StripTrimGeometry"/>
    /// does the layout. Other mods opt in the same way; TrimPiece's comment has
    /// the XML.
    ///
    /// No Harmony anywhere on this path:
    ///
    ///   - The strip never enters the static texture atlas. The base class's
    ///     TryInsertIntoAtlas is empty and this class keeps it, and
    ///     ThingDef.PostLoad, the only caller, asks the graphic. Out of the
    ///     atlas the strip can repeat along its band, which it could not
    ///     inside one.
    ///   - Paint and stuff colour come through GetColoredVersion, which hands
    ///     back this class in the new colour, as Graphic_Single does. A strip
    ///     with a paint overlay prints its bands in the stuff's colour, read
    ///     off the thing, and the overlay in this graphic's, which is the
    ///     paint when there is one.
    ///   - The placement ghost is built from the def's graphic class
    ///     (GhostUtility.GhostGraphicFor), and blueprints copy the def's
    ///     graphicData class and all, so both arrive here; DrawWorker draws
    ///     the ghost from the same geometry the map prints.
    ///
    /// Built trims print on a white material with their colour in the vertex
    /// colours, as the engine prints every atlased building, so a map section
    /// draws all its trims in one call whatever their stuff or paint. Anything
    /// on another shader, a blueprint for one, keeps its colour on the
    /// material and prints as one call per material instead.
    ///
    /// A diagonal reads the trims around it when it prints, to cut its ends to
    /// meet them, and its ghost reads the map at the cursor. When a trim is
    /// built or removed, <see cref="CompTrimJoins"/> has the trims around it
    /// print again, so a diagonal's ends follow its neighbours.
    /// </summary>
    public class Graphic_StripTrim : Graphic
    {
        internal Material mat;
        internal Material matTint;

        internal static readonly Dictionary<(TrimPiece.Kind, int, float, int, int, int, int, int), Mesh> Meshes =
            new Dictionary<(TrimPiece.Kind, int, float, int, int, int, int, int), Mesh>();

        public override Material MatSingle => mat;

        public override Material MatNorth => mat;

        public override Material MatEast => mat;

        public override Material MatSouth => mat;

        public override Material MatWest => mat;

        /// <summary>Never mirrored: each arm picks its own band of the strip by its edge.</summary>
        public override bool WestFlipped => false;

        public override bool ShouldDrawRotated => false;

        public override Material MatAt(Rot4 rot, Thing thing = null)
        {
            return mat;
        }

        /// <summary>Whether this graphic prints its colour into the vertices.</summary>
        internal bool Tinted => mat != null && mat.shader == ShaderDatabase.Cutout;

        public override void Init(GraphicRequest req)
        {
            data = req.graphicData;
            path = req.path;
            maskPath = req.maskPath;
            color = req.color;
            colorTwo = req.colorTwo;
            drawSize = req.drawSize;

            Texture2D strip = req.texture ?? ContentFinder<Texture2D>.Get(req.path) ?? BaseContent.BadTex;
            if (strip != BaseContent.BadTex)
            {
                // Repeat along the band, so a run carries one strip across every
                // joint; clamp across it, so the band's outer row is the last
                // word at the tile's edge. Set on the shared texture, which only
                // these graphics draw.
                strip.wrapModeU = TextureWrapMode.Repeat;
                strip.wrapModeV = TextureWrapMode.Clamp;
            }

            MaterialRequest request = new MaterialRequest(strip, req.shader, color)
            {
                colorTwo = colorTwo,
                renderQueue = req.renderQueue,
                shaderParameters = req.shaderParameters,
            };
            mat = MaterialPool.MatFrom(request);
            request.color = Color.white;
            request.colorTwo = Color.white;
            matTint = MaterialPool.MatFrom(request);
        }

        public override Graphic GetColoredVersion(Shader newShader, Color newColor, Color newColorTwo)
        {
            return GraphicDatabase.Get<Graphic_StripTrim>(path, newShader, drawSize, newColor, newColorTwo, data, maskPath);
        }

        /// <summary>How the strip is laid out for a piece: its bands, variants and overlay come from the piece.</summary>
        internal StripLayout LayoutFor(TrimPiece piece)
        {
            int bands = piece?.bands ?? 2;
            int layers = piece != null && piece.paintOverlay ? 2 : 1;
            return new StripLayout(StripTrimGeometry.PeriodOf(mat?.mainTexture, bands, layers), bands,
                piece?.variants ?? 1, layers);
        }

        /// <summary>
        /// The colour a thing draws in without its paint: its stuff's, as
        /// Thing.DrawColor gives it before Building.DrawColor puts the paint in
        /// its place. The bands under a paint overlay keep it.
        /// </summary>
        internal static Color StuffColor(Thing thing)
        {
            if (thing.Stuff != null)
            {
                return thing.def.GetColorForStuff(thing.Stuff);
            }
            return thing.def.graphicData?.color ?? Color.white;
        }

        /// <summary>
        /// The built trims standing on a cell, as their shape and rotation.
        /// Blueprints and frames carry no TrimPiece of their own, so they are
        /// never read: a diagonal joins what is built.
        /// </summary>
        internal static IEnumerable<(TrimPiece.Kind shape, int rot)> PiecesAt(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map))
            {
                yield break;
            }
            List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                TrimPiece piece = things[i].def.GetModExtension<TrimPiece>();
                if (piece != null)
                {
                    yield return (piece.shape, things[i].Rotation.AsInt);
                }
            }
        }

        internal static DiagonalEnds EndsFor(TrimPiece piece, Map map, IntVec3 cell, Rot4 rot)
        {
            if (piece?.shape != TrimPiece.Kind.Diagonal)
            {
                return default;
            }
            return StripTrimGeometry.DiagonalEndsAt(cell, rot.AsInt, c => PiecesAt(map, c));
        }

        public override void Print(SectionLayer layer, Thing thing, float extraRotation)
        {
            TrimPiece piece = TrimPiece.For(thing.def);
            if (piece == null)
            {
                Log.ErrorOnce("[NeatEdges] " + thing.def.defName + " draws with Graphic_StripTrim but carries "
                    + "no NeatEdges.TrimPiece naming its shape, so it is not drawn.", thing.def.shortHash ^ 0x5E7A);
                return;
            }
            bool tinted = Tinted;
            LayerSubMesh subMesh = layer.GetSubMesh(tinted ? matTint : mat);
            StripLayout layout = LayoutFor(piece);
            DiagonalEnds ends = EndsFor(piece, thing.Map, thing.Position, thing.Rotation);
            Vector3 origin = thing.TrueCenter() + DrawOffset(thing.Rotation);
            var white = new Color32(255, 255, 255, 255);
            // Paint reaches a paint overlay alone; the bands under it keep the
            // stuff's colour. Both print into the one submesh, the overlay
            // second, so it draws on top at the same altitude.
            Color32 bandColor = !tinted ? white : layout.layers > 1 ? (Color32)StuffColor(thing) : (Color32)color;
            StripTrimGeometry.Append(subMesh.verts, subMesh.uvs, subMesh.colors, subMesh.tris, piece.shape,
                thing.Rotation, thing.Position, origin, bandColor, layout, ends);
            if (layout.layers > 1)
            {
                StripTrimGeometry.Append(subMesh.verts, subMesh.uvs, subMesh.colors, subMesh.tris, piece.shape,
                    thing.Rotation, thing.Position, origin, tinted ? (Color32)color : white, layout.Overlay, ends);
            }
        }

        /// <summary>
        /// The placement ghost, and any other realtime draw. The base class
        /// would stretch the whole strip over a tile; this draws the shape's
        /// geometry from a mesh built once per shape, rotation and, for a
        /// diagonal, the ends the map gives it at the cursor, mapped as if at the
        /// map's origin.
        /// </summary>
        public override void DrawWorker(Vector3 loc, Rot4 rot, ThingDef thingDef, Thing thing, float extraRotation)
        {
            TrimPiece piece = TrimPiece.For(thing?.def ?? thingDef);
            if (piece == null)
            {
                return;
            }
            DiagonalEnds ends = default;
            if (piece.shape == TrimPiece.Kind.Diagonal)
            {
                Map map = thing?.MapHeld ?? Find.CurrentMap;
                ends = EndsFor(piece, map, thing?.PositionHeld ?? loc.ToIntVec3(), rot);
            }
            Quaternion turn = extraRotation == 0f ? Quaternion.identity : Quaternion.AngleAxis(extraRotation, Vector3.up);
            Graphics.DrawMesh(MeshFor(piece.shape, rot, LayoutFor(piece), ends), loc + DrawOffset(rot), turn, mat, 0);
        }

        internal Mesh MeshFor(TrimPiece.Kind shape, Rot4 rot, StripLayout layout, DiagonalEnds ends = default)
        {
            var key = (shape, rot.AsInt, layout.period, layout.bands, layout.variants, layout.layers, ends.a.key, ends.b.key);
            if (!Meshes.TryGetValue(key, out Mesh mesh))
            {
                var verts = new List<Vector3>();
                var uvs = new List<Vector3>();
                var colors = new List<Color32>();
                var tris = new List<int>();
                var white = new Color32(255, 255, 255, 255);
                StripTrimGeometry.Append(verts, uvs, colors, tris, shape, rot, IntVec3.Zero, Vector3.zero,
                    white, layout.Bands, ends);
                if (layout.layers > 1)
                {
                    // A realtime draw has one colour, on the material, so the
                    // overlay goes in the same mesh in it.
                    StripTrimGeometry.Append(verts, uvs, colors, tris, shape, rot, IntVec3.Zero, Vector3.zero,
                        white, layout.Overlay, ends);
                }
                mesh = new Mesh { name = "NeatEdges strip trim " + shape + " " + rot.ToStringHuman() };
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(colors);
                mesh.RecalculateBounds();
                Meshes[key] = mesh;
            }
            return mesh;
        }

        public override string ToString()
        {
            return "StripTrim(path=" + path + ", color=" + color + ")";
        }
    }
}
