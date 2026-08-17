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
            var body = target.GetComponent<Rigidbody>();

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

            if (zeroVelocity && body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }
}
