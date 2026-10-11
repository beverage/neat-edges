using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// What this mod does while NANAME Floors is installed: a hard edge also
    /// stops a soft diagonal tile from bleeding.
    ///
    /// NANAME Floors puts two terrains in one tile, split along a mask, as a
    /// terrain def of its own. Players build them as diagonal floors, and
    /// Smooth Terrain makes one at every corner it rounds, out of the natural
    /// ground on either side. The split is the tile's own terrain, drawn as a
    /// second quad over the first with a material of its own, so none of the
    /// fade-blocking in <see cref="Patch_SidedFadeBlock"/> reaches it: a
    /// diagonal trim laid on the tile, or a painted tile beside it, stops every
    /// fade around the tile and leaves the split exactly as soft as before.
    /// Smooth Terrain's two masks, HalfBlend and HalfBlendBig, are soft right
    /// across the tile, so the gradient shows on both sides of a trim's band
    /// and runs up to the hard edges beside it.
    ///
    /// So wherever a hard edge runs along such a tile, the second quad is drawn
    /// with NANAME's own hard half-tile mask instead. "Runs along" is one of
    /// the tile's four edges hardened, from either side: a marker or a trim on
    /// the tile or across one of its edges, the painted area on it or beside
    /// it, or a diagonal trim on it, which hardens all four. A hard edge that
    /// only ends at one of its corners does not count. Corner sealing sets that
    /// corner's bit in the layout mask, but nothing hard runs along the tile,
    /// and crisping it there turned one tile of Smooth Terrain's soft run into
    /// a hard line beside a border that merely stopped at its corner. The split
    /// keeps its orientation and turns crisp on the tile's true diagonal, which
    /// is where a diagonal trim's band and a diagonal wall's face run, so each
    /// terrain fills exactly half the tile (HalfBlend centres about a fifth of
    /// a tile off that line, toward its corner). Nothing is squared off: a
    /// diagonal stays a diagonal. A tile no hard edge touches keeps its soft
    /// split, and so does every tile while the compare toggle suppresses
    /// hardening.
    ///
    /// Only the draw changes. The crisp material is a copy of the one NANAME
    /// picked for the quad, its colour and pollution included, with the mask
    /// swapped; it is made once per material and shared by every tile drawn
    /// with it, so a section still draws each in one call. No def is made,
    /// nothing is saved and the terrain grid is never written, so removing
    /// either mod leaves nothing behind.
    ///
    /// Its method is reached by name only. This assembly holds no reference to
    /// NANAME Floors, which a player without it does not have.
    /// </summary>
    public static class NanameInterop
    {
        /// <summary>The class and method that draw a NANAME tile's second quad.</summary>
        internal const string UtilClass = "NanameFloors.BlendedTerrainUtil";
        internal const string CoverMethodName = "GenerateCover";

        /// <summary>Where every mod's NANAME masks live, NANAME's own and Smooth Terrain's alike.</summary>
        internal const string MaskFolder = "NanameFloors/TerrainMasks/";

        /// <summary>
        /// Each soft mask, by texture name, and the hard mask its split is
        /// drawn with where a hard edge touches it. Both of Smooth Terrain's
        /// are a diagonal half, so both map to NANAME's own diagonal half,
        /// which is exact: corner to corner, a texel of antialiasing wide. A
        /// mask not listed here is drawn as it is, hard or not, so a shape
        /// another mod adds keeps its own look rather than becoming a half.
        /// </summary>
        internal static readonly Dictionary<string, string> CrispMasks = new Dictionary<string, string>
        {
            { "HalfBlend", "HalfTriangle" },
            { "HalfBlendBig", "HalfTriangle" },
        };

        internal static MethodInfo coverMethod;
        internal static bool resolved;

        /// <summary>NANAME's method that draws a tile's second quad, or null when it is not installed.</summary>
        internal static MethodInfo CoverMethod
        {
            get
            {
                if (!resolved)
                {
                    coverMethod = AccessTools.Method(GenTypes.GetTypeInAnyAssembly(UtilClass), CoverMethodName);
                    resolved = true;
                }
                return coverMethod;
            }
        }

        /// <summary>True while NANAME Floors is installed.</summary>
        public static bool Installed => CoverMethod != null;

        /// <summary>
        /// What the cover patch resolved, for the startup line and the harness
        /// to assert on. Null when NANAME Floors is not installed.
        /// </summary>
        public static string CoverReport => coverReport;

        /// <summary>True only if the cover transpiler made its one insertion.</summary>
        public static bool CoverApplied => coverApplied;

        internal static string coverReport;
        internal static bool coverApplied;

        /// <summary>
        /// Patches NANAME's cover method when it is installed, and only then:
        /// the patch is applied by hand rather than by attribute, because
        /// PatchAll would try to resolve a class a player without NANAME does
        /// not have. A method whose signature has moved is left alone, and
        /// the report says so.
        /// </summary>
        internal static void Patch(Harmony harmony)
        {
            MethodInfo method = CoverMethod;
            if (method == null) return;

            ParameterInfo[] parameters = method.GetParameters();
            bool shaped = method.IsStatic && parameters.Length >= 3
                && parameters[0].ParameterType == typeof(SectionLayer)
                && parameters[2].ParameterType == typeof(IntVec3);
            if (!shaped)
            {
                coverApplied = false;
                coverReport = "its signature changed: ("
                    + string.Join(", ", parameters.Select(p => p.ParameterType.Name)) + ")";
                return;
            }

            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(NanameInterop), nameof(CoverTranspiler)));
        }

        /// <summary>
        /// One insertion, just before the cover method asks for its submesh:
        /// the material it chose goes through <see cref="CoverMaterial"/> on
        /// its way there. Nothing of NANAME's is removed or reordered, so its
        /// own choice of material stands everywhere this one passes it
        /// through, and another mod's transpiler on the same method still
        /// finds every instruction it looked for.
        ///
        /// The anchor is the call to GetSubMesh, and it must be the only one.
        /// Anything else, the method passes through untouched and the report
        /// says what was found: soft tiles stay soft, as they would without
        /// this mod.
        /// </summary>
        public static IEnumerable<CodeInstruction> CoverTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.ToList();
            MethodInfo getSubMesh = AccessTools.Method(typeof(MapDrawLayer), nameof(MapDrawLayer.GetSubMesh),
                new[] { typeof(Material) });

            List<int> anchors = new List<int>();
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(getSubMesh)) anchors.Add(i);
            }

            if (anchors.Count != 1)
            {
                coverApplied = false;
                coverReport = $"GetSubMesh called {anchors.Count} times, want 1";
                return code;
            }

            // On the stack at the anchor: the layer, then the material. Push
            // the layer and the cell, and the call leaves a material in the
            // material's place. A branch that targeted the call now lands on
            // the first inserted instruction, so no path skips the swap.
            int at = anchors[0];
            CodeInstruction first = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code[at]);
            code.InsertRange(at, new[]
            {
                first,
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NanameInterop), nameof(CoverMaterial))),
            });
            coverApplied = true;
            coverReport = $"GetSubMesh@{at} of {code.Count - 3}";
            return code;
        }

        /// <summary>
        /// The material a NANAME tile's second quad is drawn with: NANAME's
        /// own choice, or its crisp copy where a hard edge runs along one of
        /// the tile's edges. Only the mask's edge bits count; its corner bits,
        /// set by sealing, do not.
        ///
        /// Only on the layers this mod hardens, the terrain layer and the
        /// water layer that inherits its Regenerate. NANAME also draws covers
        /// for As Above So Below's views of a level below, and those are left
        /// as NANAME draws them, since nothing here hardens them.
        ///
        /// Inside a section's regeneration the edge mask reads the section's
        /// memo, the same one <see cref="Patch_SidedFadeBlock"/> built for it;
        /// anywhere else, or on another map, it computes directly.
        /// </summary>
        public static Material CoverMaterial(Material material, SectionLayer layer, IntVec3 cell)
        {
            if (material == null || DebugTools_NeatEdges.Suppressed) return material;
            if (!(layer is SectionLayer_Terrain)) return material;

            Map map = Patch_SidedFadeBlock.MapOf(layer);
            if (map == null || !cell.InBounds(map)) return material;

            OwnMaskCache cache = Patch_SidedFadeBlock.sectionCache;
            if (cache != null && cache.map != map) cache = null;

            int edges = Patch_SidedFadeBlock.EdgeMaskAt(cell, map, cache) & Patch_SidedFadeBlock.AllCardinals;
            return edges == 0 ? material : Crisp(material);
        }

        /// <summary>Crisp copies, by the material they copy. A material with nothing to swap maps to itself.</summary>
        internal static readonly Dictionary<Material, Material> crispFor = new Dictionary<Material, Material>();

        internal static bool warnedMissingMask;

        /// <summary>
        /// The crisp copy of a cover material, made the first time it is
        /// asked for. NANAME keeps one material per terrain, mask, colour and
        /// pollution, so this keeps one copy of each, and every tile drawn
        /// with a material is drawn with the same copy.
        /// </summary>
        internal static Material Crisp(Material soft)
        {
            lock (crispFor)
            {
                if (!crispFor.TryGetValue(soft, out Material crisp))
                {
                    crisp = MakeCrisp(soft);
                    crispFor[soft] = crisp;
                }
                return crisp;
            }
        }

        /// <summary>
        /// A copy of the material with its mask swapped for the hard one
        /// <see cref="CrispMasks"/> names. The copy keeps everything else:
        /// shader, texture, colour, render queue, and the noise texture a
        /// rough terrain's edge is drawn through.
        ///
        /// The hard mask is the texture NANAME's own diagonal floors use, and
        /// it is given the wrap and mip bias NANAME gives every mask it draws
        /// with, read off the soft mask, which NANAME set up the same way.
        /// </summary>
        internal static Material MakeCrisp(Material soft)
        {
            int id = ShaderPropertyIDs.MaskTex;
            if (!soft.HasProperty(id)) return soft;

            Texture mask = soft.GetTexture(id);
            if (mask == null || !CrispMasks.TryGetValue(mask.name, out string hardName)) return soft;

            Texture2D hard = ContentFinder<Texture2D>.Get(MaskFolder + hardName, false);
            if (hard == null)
            {
                if (!warnedMissingMask)
                {
                    warnedMissingMask = true;
                    Log.Warning($"[NeatEdges] NANAME Floors has no {hardName} mask, so its soft diagonal "
                        + "tiles stay soft beside hard edges.");
                }
                return soft;
            }
            hard.wrapMode = mask.wrapMode;
            hard.mipMapBias = mask.mipMapBias;

            Material crisp = new Material(soft) { name = soft.name + " (crisp)" };
            crisp.SetTexture(id, hard);
            return crisp;
        }
    }
}
