using System;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Keeps the trims' textures out of the game's static texture atlas and
    /// clamps their edges, so each trim draws its own art right to the edge of
    /// its tile and nothing else.
    ///
    /// The atlas packs small building textures edge to edge with no gutter
    /// between them (StaticTextureAtlas.CalcRectsForAtlasNew advances by each
    /// texture's width and nothing more), then builds mipmaps and compresses
    /// over the whole sheet. So the outermost texels of an atlased quad are
    /// filtered against whichever texture the packer put beside it. A building
    /// whose art stops short of its edges never shows it. A trim's band runs to
    /// the edge of its texture by design, so every joint picked up a hairline
    /// of the neighbouring texture's colour, often green or teal, which shifted
    /// as the camera moved. Drawing each trim 4% oversize, as the trims did
    /// until 2026-10-06, only moved that line 2% into the next tile, where it
    /// sat on top of the neighbour's band.
    ///
    /// Out of the atlas, a trim samples only its own texture, and Clamp makes
    /// its outermost texel the last word at its edge. Two pieces that meet at a
    /// tile boundary then each end on their own outer column, which the art
    /// generator draws identically wherever two pieces continue one another.
    /// So the trims draw at exactly one tile, and a joint is the drawing
    /// carrying on.
    ///
    /// The cost is that trims stop sharing the atlas's material: a map section
    /// draws them in one batch per texture and colour instead of one in all.
    ///
    /// Applied from <see cref="NeatEdgesMod"/>'s constructor rather than with
    /// the other patches, which go in from a static constructor, by which time
    /// every def has already offered its textures to the atlas. A prefix at the
    /// door rather than an edit of the build queue, because Faster Game Loading
    /// can defer graphics and bake the atlas long after startup. Fails closed:
    /// if it does not apply, the trims are atlased as they always were and the
    /// hairlines come back, which the harness's trims.atlas cases catch.
    /// </summary>
    public static class Patch_TrimAtlas
    {
        /// <summary>The trims' texture folder, as the defs name it under Textures/.</summary>
        internal const string TexturePrefix = "NeatEdges/Trim/";

        internal static ModContentPack content;
        internal static bool applied;

        internal static void Apply(ModContentPack pack)
        {
            content = pack;
            try
            {
                new Harmony(HarmonyInit.Id).Patch(
                    AccessTools.Method(typeof(GlobalTextureAtlasManager), nameof(GlobalTextureAtlasManager.TryInsertStatic)),
                    prefix: new HarmonyMethod(typeof(Patch_TrimAtlas), nameof(Prefix)));
                applied = true;
            }
            catch (Exception e)
            {
                Log.Warning("[NeatEdges] the trims stay in the texture atlas, so their joints may show hairlines: " + e);
            }
        }

        /// <summary>
        /// True for exactly the textures this mod loaded from its trim folder:
        /// looked up by the name the loader gave the texture, then compared by
        /// identity, so another mod's texture of the same name is left alone.
        /// </summary>
        internal static bool IsTrimTexture(Texture2D texture)
        {
            return texture != null && content != null
                && content.GetContentHolder<Texture2D>().Get(TexturePrefix + texture.name) == texture;
        }

        public static bool Prefix(Texture2D texture, ref bool __result)
        {
            if (!IsTrimTexture(texture))
            {
                return true;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            __result = false;
            return false;
        }
    }
}
