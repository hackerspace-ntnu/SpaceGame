using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Finishes a <see cref="SaveTeleport"/> placement that could not be completed because the
    /// NavMesh was not there yet.
    ///
    /// <b>Why this is not an edge case.</b> Placement runs from <c>WorldStreamer.OnChunkLoaded</c>,
    /// and a chunk that has just finished loading very often does not have its NavMesh available in
    /// the same frame. <c>NavMeshAgent.Warp</c> is guarded on <c>isOnNavMesh</c>, so the guard was
    /// simply false and the code fell through to a plain transform write — which moves the
    /// GameObject but not the agent's own internal position. The agent then either stands still or
    /// walks back toward where it thinks it is, and nothing is logged. That is a restored creature
    /// silently reverting to its authored position, which is the exact failure the save system was
    /// built to stop.
    ///
    /// So the warp is retried for a bounded window instead. The component adds itself, keeps trying
    /// every frame, and removes itself the instant it succeeds — so in the common case where the
    /// mesh arrives a frame or two later, this costs two frames of a null check.
    ///
    /// It gives up after <see cref="TimeoutSeconds"/> and says so. An agent whose saved position has
    /// no NavMesh under it at all is a real problem (the chunk's mesh was never baked, or the object
    /// was saved somewhere it cannot stand), and a warning naming it is worth more than a creature
    /// that quietly does not move.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeferredNavMeshWarp : MonoBehaviour
    {
        /// <summary>How long to keep trying. Long enough for a chunk's mesh, short enough to notice.</summary>
        public const float TimeoutSeconds = 10f;

        /// <summary>
        /// How far from the saved position to accept a mesh point.
        ///
        /// Not zero: a baked mesh rarely passes exactly through the point an object was standing on,
        /// because the agent's saved position is its transform and the mesh sits at the agent's own
        /// offset. Not large either — snapping an entity across a canyon to "somewhere navigable"
        /// would be a worse answer than leaving it where it was saved.
        /// </summary>
        public const float SampleRadius = 4f;

        private NavMeshAgent agent;
        private Vector3 position;
        private Quaternion rotation;
        private float deadline;

        /// <summary>
        /// Queues a warp to run as soon as the agent has a mesh under it.
        ///
        /// Idempotent per object: a second call replaces the pending target rather than stacking a
        /// second component, because the newest placement is the one that is true.
        /// </summary>
        public static void Schedule(NavMeshAgent agent, Vector3 position, Quaternion rotation)
        {
            if (agent == null) return;

            var pending = agent.gameObject.GetComponent<DeferredNavMeshWarp>();
            if (pending == null) pending = agent.gameObject.AddComponent<DeferredNavMeshWarp>();

            pending.agent = agent;
            pending.position = position;
            pending.rotation = rotation;
            pending.deadline = Time.time + TimeoutSeconds;
            pending.enabled = true;
        }

        private void LateUpdate()
        {
            // The agent was destroyed, or something disabled it. Nothing left to finish.
            if (agent == null)
            {
                Destroy(this);
                return;
            }

            if (agent.enabled && agent.isOnNavMesh && TryWarp())
            {
                Destroy(this);
                return;
            }

            if (Time.time < deadline) return;

            Debug.LogWarning(
                $"[Save] '{name}' was restored to {position} but no NavMesh appeared under it within " +
                $"{TimeoutSeconds:0}s, so its agent is still at its pre-load position. The chunk's " +
                "NavMesh may not be baked, or the object was saved somewhere it cannot stand.", this);

            Destroy(this);
        }

        /// <summary>
        /// Warps to the saved position, or to the nearest mesh point within
        /// <see cref="SampleRadius"/> of it.
        ///
        /// The sample matters: <c>isOnNavMesh</c> being true says the agent is on a mesh where it
        /// currently stands, not that the saved position is on one. Warping to a point off the mesh
        /// puts the agent into the same stuck state this class is here to resolve.
        /// </summary>
        private bool TryWarp()
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, SampleRadius, NavMesh.AllAreas))
            {
                if (!agent.Warp(hit.position)) return false;
                transform.rotation = rotation;
                return true;
            }

            // No mesh near the saved position yet. Keep waiting rather than warping somewhere wrong.
            return false;
        }
    }
}
