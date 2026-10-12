using HarmonyLib;
using RimWorld;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// Lets every ideoligion's members build the trims.
    ///
    /// The trims set canGenerateDefaultDesignator false, so that the Floors
    /// tab shows one button per shape (<see cref="Designator_TrimShape"/>)
    /// instead of one per trim. Ideology reads the same flag as the mark of a
    /// building only some ideoligions may raise, an altar or a ritual seat:
    /// `Ideo.MembersCanBuild` passes a def that sets it true and nothing else,
    /// unless the ideoligion is in classic mode, lists the def through a meme
    /// or a style, holds a precept for it, or holds a precept that lets its
    /// members build others' buildings. A trim meets none of those, and
    /// `GenConstruct.CanConstruct` asks the question of every blueprint and
    /// frame before any work starts.
    ///
    /// It is not only a question for Ideology players. Every pawn carries an
    /// ideoligion; the one a game without the DLC makes for each faction is
    /// not in classic mode and has no precept that would pass. So from v1.1.0,
    /// which set the flag, to v1.1.2, no colonist built a trim in any game
    /// without Ideology, nor in most games with it: the blueprint placed, and
    /// the work was refused, silently without the DLC and with "Only ... can
    /// build" when some ideoligion in the world held the permissive precept.
    ///
    /// Keyed on <see cref="TrimPiece"/>, the extension that puts a def on a
    /// shape's button, so another mod's trims that opt in the same way are
    /// covered too. A blueprint's or frame's def carries no extensions, hence
    /// entityDefToBuild. Only ever turns a refusal into a yes, and only for a
    /// trim, so an ideoligion's real buildings keep their gate.
    ///
    /// Rejected: putting the flag back, which brings back 28 buttons; and
    /// listing the trims on every structure meme, which would add a second,
    /// ideoligion-made button for each to the tab.
    /// </summary>
    [HarmonyPatch(typeof(Ideo), nameof(Ideo.MembersCanBuild))]
    public static class Patch_TrimsBuildableByAll
    {
        public static void Postfix(Thing thing, ref bool __result)
        {
            if (__result || thing?.def == null) return;
            BuildableDef def = thing.def.entityDefToBuild ?? thing.def;
            if (def.GetModExtension<TrimPiece>() != null) __result = true;
        }
    }
}
