// What a resident's hand should hold for what it is doing. Pure, so the rule can be tested as a table.
//
// A resident draws its tool for work and puts it on the belt for everything else, so that a body that
// is only standing, sitting or talking has a free hand to fidget and gesture with (BodyLanguage never
// plays a body-language clip over a held item). The tool stays in the hand where it cannot be stowed:
// an item nothing draws on the belt would vanish from the body, which reads as a bug rather than as
// "put away". Travel is free-handed too: a hold pose keys the spine and chest, so a resident walking with a
// tool in its hand walks hunched; it draws the tool when it reaches the place where the work is done.
using SpaceGame.Presentation;

namespace SpaceGame.Agents.Residents
{
    /// <summary>What the hand holds.</summary>
    public enum HandContents : byte
    {
        Empty,
        /// <summary>The resident's own item (<c>Resident.HeldItem</c>): its archetype's <c>heldItem</c>, or a stand-in's kit weapon.</summary>
        Tool,
        /// <summary>The thing an errand or trip is carrying (a bucket, a crate, a prop's item).</summary>
        Carried,
    }

    public static class ResidentHandsRule
    {
        /// <summary>
        /// Work, a patrol (armed duty) and a trip's quarry are done WITH the tool, and so is a chore once the resident is at the
        /// spot it works at (<paramref name="atPlace"/>); between spots it walks with free hands. Nothing known yet
        /// (<see cref="Activity.None"/>, before the first publish) keeps the authored default, the tool in the hand. Everything
        /// else — walking between places, sitting, a stroll, the hearth, a conversation, sleeping, sheltering, an amble, a
        /// ladder — is done with free hands. A body with a band (<see cref="Activity.Expedition"/>: a stand-in on the road, or a
        /// resident mustering, walking out or walking in) carries its kit's weapon, the first row of the outside hand rule
        /// (spec §6.1); the weapon's own stance holds it (a spear: Carry).
        /// </summary>
        public static bool NeedsTool(Activity activity, bool atPlace) => activity switch
        {
            Activity.None or Activity.Work or Activity.Patrol or Activity.Trip or Activity.Stalking or Activity.Expedition => true,
            Activity.Chore => atPlace,
            _ => false,
        };

        /// <summary>
        /// Whether the loop a station holds is done with empty hands: its clips say so (<see cref="CharacterCue.BareHands"/>: wiping,
        /// rummaging), or it is a gesture cue (<see cref="CharacterCue.NeedsFreeHands"/>: explaining at a stall) whose loops
        /// <see cref="BodyLanguage"/> never plays over an arm the held item occupies. With a tool in the hand the resident would
        /// stand there doing nothing.
        /// </summary>
        public static bool EmptyHandsFor(CharacterCue station) => station != null && (station.BareHands || station.NeedsFreeHands);

        /// <summary>
        /// Hands on a cart's handles hold nothing else. Otherwise a carried errand item wins the hand, then the tool, when the
        /// activity needs it or when it has nowhere to hang (<paramref name="toolStowable"/> false), else nothing.
        /// </summary>
        public static HandContents Wanted(Activity activity, bool atPlace, bool carrying, bool toolStowable, bool pushing = false)
        {
            if (pushing) return HandContents.Empty;
            if (carrying) return HandContents.Carried;
            return NeedsTool(activity, atPlace) || !toolStowable ? HandContents.Tool : HandContents.Empty;
        }
    }
}
