// Rope channel for motors. Parallels IRiderControllable — that one carries what the rider is
// asking for, this one carries what a rope tied to the machine is doing to it.
//
// The grappling hook moves a PLAYER by writing their Rigidbody's velocity outright, which is the
// right thing to do to a capsule and the wrong thing to do to a machine that integrates its own
// flight. A mounted player's body is kinematic and parented into a seat: it is not the thing that
// is moving, so pulling on it achieves nothing whatsoever — which is exactly what a hook fired
// from an ornithopter's cradle used to achieve.
//
// So the rope asks instead of pushing. The vehicle owns what a pull costs and what its airframe
// will take; the hook owns where the far end is tied. Neither needs to know how the other works.
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// Something a rope can be tied to and haul along: a vehicle the player is riding, rather than
    /// the player themselves.
    ///
    /// <para>
    /// Implemented by machines that can usefully be pulled. Anything that cannot — a horse, a
    /// chair, the ship's gunner seat — simply does not implement it, and a rope thrown from it
    /// hangs slack. That is the honest answer rather than a special case, and it means adding a
    /// towable vehicle later costs one interface and no edits to the hook.
    /// </para>
    /// </summary>
    public interface ITowable
    {
        /// <summary>Where a rope tied to this thing pulls from, in world space.</summary>
        Vector3 TowAttachPoint { get; }

        /// <summary>
        /// Ask to be pulled towards <paramref name="anchor"/> for one physics step.
        ///
        /// <para>
        /// Asked every step for as long as the rope is out, and a tow that stops being asked for
        /// stops. That is deliberate: a hook that is dropped, an item that is unequipped and a
        /// pilot who dies all end the tow without anyone having to remember to say so.
        /// </para>
        /// <para>
        /// Returns false when the tow is over and the rope should be let go — arrived, out of
        /// energy, or no longer under way. The caller drops the rope on false rather than asking
        /// again.
        /// </para>
        /// </summary>
        bool RequestTow(Vector3 anchor);
    }
}
