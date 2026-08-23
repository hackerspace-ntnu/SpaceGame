// "Pretend that surface is not there when you decide where you may walk."
//
// A legged machine does not push against the world the way a physics body does. It ASKS the world
// where it may put a foot and whether the ground ahead is climbable, by casting rays, and then it
// writes its own position from the answer. That difference is invisible until something makes a
// solid surface passable, at which point the two kinds of mover diverge completely:
//
//   • a Rigidbody walks through a wall the instant Physics.IgnoreCollision is called on the pair.
//   • a legged machine does not, because IgnoreCollision has NO effect on a raycast. Every probe
//     still finds the wall, the climb gate reads it as a cliff, and the machine stops at the rim.
//
// A portal aperture is exactly that: a hole cut into a wall whose collision is switched off for
// whoever is standing in it. Without this interface a portal is a door that only physics bodies can
// use, and every walking creature in the game halts against the picture.
//
// Declared in this assembly rather than in the portal code for the same reason IExternallyPosed is:
// the locomotion assembly cannot reference Assembly-CSharp, and "some of the world is not really
// there for me" is not a portal idea. Portals are merely the component that happens to know which
// surfaces those are.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public interface IGroundProbeExclusions
    {
        /// <summary>
        /// Add or remove a surface from the set this machine's ground and climb probes ignore.
        ///
        /// Must be safe to call before the rig is measured — an aperture can open around a machine
        /// that has not finished starting up — and must be idempotent, since a wall shared by two
        /// apertures is excluded and restored by each of them independently.
        /// </summary>
        void ExcludeFromGroundProbes(Collider surface, bool excluded);
    }
}
