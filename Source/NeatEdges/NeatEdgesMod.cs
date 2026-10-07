using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Exists for the one patch that cannot wait for the rest. A Mod's
    /// constructor runs before any def loads, and <see cref="Patch_TrimAtlas"/>
    /// has to be in place before the first trim's graphic offers its textures
    /// to the atlas. Everything else is patched later, from
    /// <see cref="HarmonyInit"/>. It has no settings, so the mod settings list
    /// does not show it.
    /// </summary>
    public class NeatEdgesMod : Mod
    {
        public NeatEdgesMod(ModContentPack content) : base(content)
        {
            Patch_TrimAtlas.Apply(content);
        }
    }
}
