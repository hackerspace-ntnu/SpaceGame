// Carrying a walking machine through a teleport.
//
// ─────────── why a legged machine cannot simply be moved ───────────
//
// Invariant I4: this class is the single owner of the body's transform. It does not READ the body
// to find out where the machine is — it holds `pathPos`, integrates the commanded velocity into it,
// and writes `body.position` from it every LateUpdate.
//
// So moving the transform of a walker does not move the walker. It moves the transform, and then
// the very next frame writes the old position straight back over it. Every legged thing in this
// game — the ostrich, the crab, the horse, the humanoid, the desert crawler — walked into a portal
// and was returned to where it started within a frame, with no error and nothing in the console.
// The same is true of a respawn, a save restore or a chat teleport landing on one.
//
// The feet are the second half of it. `LegState.Foot` is a CONTRACT with the ground: fixed in world
// space while the leg is planted, and if it moves while planted the machine is skating. A body
// carried through an aperture while its feet stay in the room it left is not a subtle artefact —
// the legs are IK chains reaching for a point that is now a hundred metres behind them.
//
// ─────────── what this does about it ───────────
//
// Rebases both by the transfer, which is the one operation that is right for all of it. A rigid
// change of frame applied uniformly to the path, the footholds, the ground normals and the swing
// arcs leaves the machine standing in exactly the stance it was standing in, at the new place. It
// does not stumble, it does not re-plant, it does not lose its gait phase — the phase is advanced
// by distance travelled, and a teleport is not travel.
//
// Nothing here filters anything, which is the rule the whole locomotion is written to: assignment
// only, at a moment the machine is not stepping.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Teleporting;

namespace SpaceGame.Locomotion
{
    public abstract partial class LeggedLocomotion : ITeleportAware, IGroundProbeExclusions
    {
        /// <summary>
        /// Surfaces this machine's probes must pretend are not there. See
        /// <see cref="IGroundProbeExclusions"/> for why a legged machine needs this and a physics
        /// body does not.
        ///
        /// Held here rather than inside <c>WalkerGround</c> so that it survives the rig being
        /// measured again, and so that it can be written before the rig exists at all — an aperture
        /// can open around a machine on the frame it spawns.
        /// </summary>
        private readonly HashSet<Collider> probeExclusions = new HashSet<Collider>();

        public void ExcludeFromGroundProbes(Collider surface, bool excluded)
        {
            if (surface == null) return;

            if (excluded) probeExclusions.Add(surface);
            else probeExclusions.Remove(surface);
        }

        /// <summary>
        /// Bring the path, the feet and the arms through the move with the body.
        ///
        /// Runs for a followed machine as well as an owning one. A remote copy derives this frame's
        /// motion by measuring how far the body moved, so leaving `lastBodyPos` in the old room
        /// hands the gait a stride of whatever the distance between the two apertures happened to
        /// be — clamped to MaxSpeed, which is not an error but is a full-speed run in place for as
        /// long as the clamp lasts.
        /// </summary>
        public void OnTeleported(in TeleportMove move)
        {
            // Before Initialise there is nothing to rebase and no rig to read: the machine will be
            // measured against wherever it then is.
            if (!ready) return;

            pathPos = move.Point(pathPos);
            lastBodyPos = move.Point(lastBodyPos);

            // The height channel travels as part of the path rather than separately. pathPos.y IS
            // smoothedHeight every frame (Body.cs writes one from the other), so rebasing the point
            // and reading its height back keeps the two agreeing — and leaves the machine primed,
            // so the settle glides onto the new room's ground from where it actually is instead of
            // snapping down from the height it had in the old one.
            smoothedHeight = pathPos.y;

            // Read back rather than rotated: the body's rotation is whoever teleported it to
            // decide, and a player-style upright constraint means the yaw applied is not always the
            // yaw the transfer contains.
            currentYaw = body.eulerAngles.y;

            // Turned, not cleared. Speedy thing goes in, speedy thing comes out — and the gait is
            // paced off the commanded velocity, so zeroing it here would make a machine sprinting
            // through an aperture arrive mid-stride at a standstill.
            commandedWorldVelocity = move.Direction(commandedWorldVelocity);
            velocity = move.Direction(velocity);

            for (int i = 0; i < legs.Count; i++)
            {
                LegState leg = legs[i];

                leg.Foot = move.Point(leg.Foot);
                leg.GroundNormal = move.Direction(leg.GroundNormal);

                // The swing endpoints too, or a leg caught in mid-step lands on the far side by
                // interpolating from a point in the room it left — one foot sweeping the whole
                // distance between the apertures over the remainder of its swing.
                leg.SwingFrom = move.Point(leg.SwingFrom);
                leg.SwingTo = move.Point(leg.SwingTo);
            }

            for (int i = 0; i < arms.Count; i++)
            {
                WalkerArm arm = arms[i];

                arm.Target = move.Point(arm.Target);
                arm.TipDirection = move.Direction(arm.TipDirection);
            }
        }
    }
}
