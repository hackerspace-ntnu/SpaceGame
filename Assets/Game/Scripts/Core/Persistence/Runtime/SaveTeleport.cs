using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Places an object at a saved position without the movement systems undoing it.
    ///
    /// Assigning <c>transform.position</c> is not enough for anything the player or an agent drives.
    /// A CharacterController caches its own internal position and writes it back over the transform
    /// on its next Move, so the object snaps home within a frame. A NavMeshAgent does the same from
    /// its own agent position, and additionally has to be told to re-find the mesh — an agent moved
    /// off its current polygon simply stops. A Rigidbody keeps whatever momentum it had, so a body
    /// restored mid-air carries the fall it was in when the save was written.
    ///
    /// Every one of those is a real way a load "worked" and then quietly put the player back where
    /// they started.
    ///
    /// The Rigidbody is the one that had to be learned twice. Physics.autoSyncTransforms is false,
    /// so writing transform.position does not reach the body PhysX simulates: it keeps the pose it
    /// last stepped and restores it, and an interpolated body does it a frame sooner because
    /// interpolation drives the transform from that pose every frame. UnderTerrainGuard resyncs its
    /// bodies by hand for exactly this reason; this path did not, which is why a respawn resolved
    /// the spawn point inside the ship, wrote it, and left the player standing on their own grave.
    /// </summary>
    public static class SaveTeleport
    {
        /// <param name="zeroVelocity">
        /// Whether to stop the body on arrival. True for placement — a body teleported while
        /// carrying the velocity it had at its old position keeps travelling in a direction that no
        /// longer means anything. False when a <see cref="RigidbodySaveable"/> on the same object is
        /// about to restore the real velocity, since the two savers run in component order and
        /// neither may depend on winning that race.
        /// </param>
        public static void Move(GameObject target, Vector3 position, Quaternion rotation, bool zeroVelocity = true)
        {
            if (target == null) return;

            var controller = target.GetComponent<CharacterController>();
            var agent = target.GetComponent<NavMeshAgent>();

            bool controllerWasEnabled = controller != null && controller.enabled;
            if (controllerWasEnabled) controller.enabled = false;

            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                // Warp, not transform assignment: it moves the agent's own position too, which is
                // what the agent actually navigates from.
                agent.Warp(position);
                target.transform.rotation = rotation;
            }
            else
            {
                target.transform.SetPositionAndRotation(position, rotation);
            }

            if (controllerWasEnabled) controller.enabled = true;

            PlaceBodies(target, zeroVelocity);
        }

        /// <summary>
        /// Puts every Rigidbody under <paramref name="target"/> where its transform now is.
        ///
        /// Every body, not just the root's own: moving a transform moves its children, but PhysX
        /// holds each body's pose independently. On a single-body prefab resyncing only the root
        /// costs nothing; on an articulated one — a walker's legs, a rover's bogies and wheels —
        /// it drags the chassis away from parts that stayed behind and pulls the joints apart.
        /// Empty is a normal case: the DuneFoil has no Rigidbody anywhere.
        /// </summary>
        private static void PlaceBodies(GameObject target, bool zeroVelocity)
        {
            foreach (Rigidbody body in target.GetComponentsInChildren<Rigidbody>(true))
            {
                if (body == null) continue;

                // Interpolation is switched off across the write and put back after it. An
                // interpolated body blends the transform from the poses it has already simulated,
                // so leaving it on means the frame after a teleport is spent travelling back toward
                // where the body came from. Restoring the setting matters as much as clearing it —
                // a body left on None never smooths again.
                RigidbodyInterpolation interpolation = body.interpolation;
                if (interpolation != RigidbodyInterpolation.None)
                    body.interpolation = RigidbodyInterpolation.None;

                body.position = body.transform.position;
                body.rotation = body.transform.rotation;

                if (interpolation != RigidbodyInterpolation.None)
                    body.interpolation = interpolation;

                if (zeroVelocity && !body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
        }
    }
}
