using Verse;

namespace NeatEdges
{
    /// <summary>
    /// The player's choices, saved by the game in its Config folder under the
    /// mod's folder name and <see cref="NeatEdgesMod"/>: where the trims'
    /// shape buttons go, and which update note the player has seen.
    /// </summary>
    public class NeatEdgesSettings : ModSettings
    {
        /// <summary>
        /// On, the default since 1.2.0, moves the shape buttons off the Floors
        /// tab and leaves the hard edge marker there: to a Trims section on
        /// the Decorate tab while Better Architect Menu is loaded and shows
        /// it, and to the end of vanilla's Misc tab, which vanilla fills far
        /// less, otherwise. Off keeps them on Floors, where every earlier
        /// version put them; <see cref="WhatsNewNote"/> tells an updating
        /// player about the move once, and offers to put them back.
        /// <see cref="Patch_TrimDesignators.HomeFor"/> reads it in every build
        /// of any of those tabs. Written even at its default, so a player who
        /// kept the move keeps it if a later version changes the default.
        /// </summary>
        internal bool trimsOffFloors = true;

        /// <summary>
        /// The id of the last update note the player closed, or null before
        /// the first. <see cref="WhatsNewNote"/> shows a note whose id differs.
        /// </summary>
        internal string whatsNewSeen;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref trimsOffFloors, "trimsOffFloors", true, forceSave: true);
            Scribe_Values.Look(ref whatsNewSeen, "whatsNewSeen");
        }
    }
}
