using HarmonyLib;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Exists for the one patch that cannot wait for the rest. A Mod's
    /// constructor runs before any def loads, and <see cref="Patch_TrimDesignators"/>
    /// has to be in place before the game first builds each Architect tab's
    /// buttons, which it does from finishing work queued ahead of every static
    /// constructor. Everything else is patched later, from <see cref="HarmonyInit"/>.
    /// It has no settings, so the mod settings list does not show it.
    /// </summary>
    public class NeatEdgesMod : Mod
    {
        public NeatEdgesMod(ModContentPack content) : base(content)
        {
            Patch_TrimDesignators.Apply(new Harmony(HarmonyInit.Id));
        }
    }
}
