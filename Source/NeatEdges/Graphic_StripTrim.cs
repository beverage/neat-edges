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
    ///     back this class in the new colour, as Graphic_Single does.
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
    /// </summary>
    public class Graphic_StripTrim : Graphic
    {
        internal Material mat;
        internal Material matTint;
        internal float period = 1f;

        internal static readonly Dictionary<(TrimPiece.Kind, int, float), Mesh> Meshes =
            new Dictionary<(TrimPiece.Kind, int, float), Mesh>();

        public override Material MatSingle => mat;

        public override Material MatNorth => mat;

        public override Material MatEast => mat;

        public override Material MatSouth => mat;

        public override Material MatWest => mat;

        /// <summary>Never mirrored: each arm picks its own half of the strip by its edge.</summary>
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
            period = StripTrimGeometry.PeriodOf(strip);

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
            Color32 vertexColor = tinted ? (Color32)color : new Color32(255, 255, 255, 255);
            StripTrimGeometry.Append(subMesh.verts, subMesh.uvs, subMesh.colors, subMesh.tris, piece.shape,
                thing.Rotation, thing.Position, thing.TrueCenter() + DrawOffset(thing.Rotation), vertexColor,
                period);
        }

        /// <summary>
        /// The placement ghost, and any other realtime draw. The base class
        /// would stretch the whole strip over a tile; this draws the shape's
        /// geometry from a mesh built once per shape and rotation, mapped as if
        /// at the map's origin. On a strip that is uniform along its band, which
        /// Neat Edges' own is, that is indistinguishable from the printed trim.
        /// </summary>
        public override void DrawWorker(Vector3 loc, Rot4 rot, ThingDef thingDef, Thing thing, float extraRotation)
        {
            TrimPiece piece = TrimPiece.For(thing?.def ?? thingDef);
            if (piece == null)
            {
                return;
            }
            Quaternion turn = extraRotation == 0f ? Quaternion.identity : Quaternion.AngleAxis(extraRotation, Vector3.up);
            Graphics.DrawMesh(MeshFor(piece.shape, rot), loc + DrawOffset(rot), turn, mat, 0);
        }

        internal Mesh MeshFor(TrimPiece.Kind shape, Rot4 rot)
        {
            var key = (shape, rot.AsInt, period);
            if (!Meshes.TryGetValue(key, out Mesh mesh))
            {
                var verts = new List<Vector3>();
                var uvs = new List<Vector3>();
                var colors = new List<Color32>();
                var tris = new List<int>();
                StripTrimGeometry.Append(verts, uvs, colors, tris, shape, rot, IntVec3.Zero, Vector3.zero,
                    new Color32(255, 255, 255, 255), period);
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
