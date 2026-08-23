// One instantaneous move, described as the rigid transform it was.
//
// A teleport is not "a new position". It is a change of FRAME, and that distinction is the whole
// reason this type exists. Anything that only knew its new position has to guess how to bring its
// own world-space state along; anything handed the transfer matrix rebases every point, direction
// and rotation it holds with one multiply and cannot get it wrong.
//
// So this carries both: the two poses, for anything that wants to know how far it went, and the
// transfer, for everything that has to come with it.
//
// It lives in its own tiny assembly with no references but UnityEngine, so that every layer of the
// game can implement <see cref="ITeleportAware"/> — the locomotion assembly cannot reference
// Assembly-CSharp, and a seam only half the game can reach is not a seam.
using UnityEngine;

namespace SpaceGame.Teleporting
{
    public readonly struct TeleportMove
    {
        /// <summary>Where the object was, immediately before the move.</summary>
        public readonly Vector3 From;
        public readonly Quaternion FromRotation;

        /// <summary>Where it is now.</summary>
        public readonly Vector3 To;
        public readonly Quaternion ToRotation;

        /// <summary>
        /// The rigid transform taking the old frame to the new one.
        ///
        /// Derived from the two poses rather than supplied, so it always describes the move that
        /// ACTUALLY happened rather than the one somebody intended. That matters at the one place
        /// the two differ: a player going through a portal takes the aperture's yaw but is kept
        /// upright, so the portal's own transfer contains a roll the body never received. A
        /// listener rebasing world state by that number would be wrong by exactly the roll.
        /// </summary>
        public readonly Matrix4x4 Transfer;

        public TeleportMove(Vector3 from, Quaternion fromRotation, Vector3 to, Quaternion toRotation)
        {
            From = from;
            FromRotation = fromRotation;
            To = to;
            ToRotation = toRotation;

            Transfer = Matrix4x4.TRS(to, toRotation, Vector3.one) *
                       Matrix4x4.TRS(from, fromRotation, Vector3.one).inverse;
        }

        /// <summary>Carry a world POINT through the move — a foothold, a target, a cached contact.</summary>
        public Vector3 Point(Vector3 world) => Transfer.MultiplyPoint3x4(world);

        /// <summary>
        /// Carry a world DIRECTION through the move — a velocity, a ground normal, an aim.
        ///
        /// Turned, never translated. Rotating the vector rather than reapplying its magnitude along
        /// some new axis is what preserves a diagonal: otherwise every teleport straightens out
        /// whatever was moving through it.
        /// </summary>
        public Vector3 Direction(Vector3 world) => Transfer.MultiplyVector(world);

        /// <summary>Carry a world ROTATION through the move — a stored facing, a held item's pose.</summary>
        public Quaternion Rotation(Quaternion world) => Transfer.rotation * world;

        /// <summary>How far the object jumped. Useful for deciding whether to smooth or to cut.</summary>
        public float Distance => Vector3.Distance(From, To);
    }
}
