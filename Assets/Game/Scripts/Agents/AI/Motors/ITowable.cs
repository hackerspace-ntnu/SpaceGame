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

        /// <summary>
        /// Ask to be driven along <paramref name="acceleration"/> — metres per second squared, in
        /// world space — for one physics step.
        ///
        /// <para>
        /// The other half of the channel, and a different question from <see cref="RequestTow"/>.
        /// A rope knows where it wants this machine to BE and has worked the distance out from the
        /// rope's own physics; a motor bolted to the machine knows only how hard it pushes, and
        /// how far that gets the body in one step is the machine's business — a craft resolves it
        /// against its flight path, a walker against its gait, a creature on a NavMesh against
        /// whether the push has taken it off the mesh at all.
        /// </para>
        /// <para>
        /// Kept apart rather than folded into the anchor, because the two asks cannot be spelled
        /// as one vector. A thruster handing a rope-shaped ask has to invent a distance, and an
        /// implementor that reads that distance literally moves the body by it: a booster asking
        /// to be pulled towards a point 60 m out along its own axis moved every NavMesh creature
        /// it was strapped to 60 m per physics step, which read as the animal vanishing.
        /// </para>
        /// <para>
        /// Asked every step for as long as the thrust lasts, exactly as a tow is, so a push that
        /// stops being asked for stops. Returns false when this machine will not take it at all.
        /// </para>
        /// </summary>
        bool RequestThrust(Vector3 acceleration);
    }
}
