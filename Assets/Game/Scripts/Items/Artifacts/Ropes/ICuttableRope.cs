using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A rope that a blade, a beam or anything else travelling in a straight line can part.
    ///
    /// <para>
    /// There are three rope systems in the game and no two of them break the same way: a
    /// <see cref="Leash"/> announces its own snap on an anchor's channel, a lasso publishes a verb
    /// on the thrower's, and a grappling hook's release is the SWINGER's to run because their
    /// movement is theirs. None of that is worth unifying — each is right for its own rope — so
    /// what is shared here is only the two questions a cutter actually asks: where is this rope,
    /// and part it. Everything about how the news travels stays behind <see cref="Cut"/>.
    /// </para>
    /// <para>
    /// A rope registers with <see cref="CuttableRopes"/> for as long as this machine holds a live
    /// copy of the thing that owns it — a leash for as long as the component is enabled, an item
    /// for as long as it is in a hand — and NOT for as long as the rope is out. A rope that is
    /// coiled, in flight or otherwise not there simply appends nothing and the cutter skips it.
    /// That keeps registration on a lifecycle seam each of these files already has, where it cannot
    /// leak, rather than on a state edge in the middle of a throw, where it can.
    /// </para>
    /// </summary>
    public interface ICuttableRope
    {
        /// <summary>
        /// Append this rope's shape in world space, in order, from one end to the other.
        ///
        /// <para>
        /// Two points for a straight span, more for one that bends — a leash appends its wraps, so
        /// a rope round a pillar is cut where it is drawn rather than along the chord it would take
        /// if the pillar were not there. Append NOTHING when the rope is not out; the caller reads
        /// fewer than two points as "no rope" and never as an error.
        /// </para>
        /// </summary>
        void AppendSpan(List<Vector3> into);

        /// <summary>
        /// Part the rope, and tell whoever else needs to know by whatever means this rope already
        /// uses.
        ///
        /// <para>
        /// Called on the SERVER only — <see cref="CuttableRopes.CutAlong"/> is the sole caller and
        /// is gated there — so an implementation may take the authority for granted, exactly as the
        /// damage travelling with the same beam does.
        /// </para>
        /// </summary>
        void Cut();
    }
}
