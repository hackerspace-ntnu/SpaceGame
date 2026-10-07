// Swallowed whole: inside a singularity, and not where you were.
//
// The sixth condition, and the only one that does nothing to the body it is on. The other five
// change what a body can do; this one is a FLAG and a CLOCK, and the moving is done elsewhere —
// SingularityWell hands the body to InteriorManager, which is the one thing in the project entitled
// to move a body between scenes. Two things then need to know, and neither can ask the well:
//
//   • SingularityVoidGuard, which measures "in the void with nobody holding you" and puts a
//     stranded player back. This flag is the "somebody is holding you" half of that question, and
//     it is the half that has to survive the well being destroyed — which is exactly the case the
//     guard exists for, so the flag carries its own expiry rather than being cleared by the well.
//   • Anything that reacts to conditions at all: StatusReactionModule, the HUD, a future system
//     that wants to know why a creature stopped answering. That is the whole reason the status
//     system exists rather than five artifacts each growing their own flag.
//
// IT DOES NOT SUPPRESS. A swallowed body walks around — the void is a room, not a holding cell —
// so this is deliberately not Frozen under another name. Nothing here takes input, takes the body
// over, or hides anything.
//
// IT IS NOT CONTAINMENT. Captivity.Capture serialises a body to a record and despawns it, which is
// right for a captive that must survive a save and a rejoin inside a carried item, and wrong for
// something seconds long: a despawn-and-rebuild hands the body a fresh identity, and loses it
// outright if the session ends mid-hold.
using System;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// Inside a singularity: somewhere else, for as long as the thing that ate you says so.
    /// </summary>
    [Serializable]
    public sealed class SwallowedStatus : StatusBehaviour
    {
        /// <summary>
        /// The bottled singularity's hold, which is the only thing that applies this today. The
        /// caller names its own duration; this is what the condition says it is worth on its own.
        /// </summary>
        private const float DefaultDuration = 6f;

        public SwallowedStatus() : base(DefaultDuration) { }

        public override StatusKind Kind => StatusKind.Swallowed;

        /// <summary>
        /// Never extended by a second source. Two wells whose holds overlap must not add up to a
        /// body held twice as long — and more sharply than for any other kind, a refresh would send
        /// a body that is already in the void into it a second time, which <c>InteriorManager</c>
        /// reads as a re-entry and answers by overwriting the return position with a point inside
        /// the void itself. That body would then be let out into the room it was already standing
        /// in, with no way back to the desert at all.
        /// </summary>
        public override bool CanApply(StatusReceiver body, bool running) => !running;
    }
}
