// The tunables of a NavPathFollower, serialized once on each motor that steers its own body along a
// NavMesh route (LeggedDriver, MonowheelMotor). Each motor keeps its own defaults -- how wide a
// corner is rounded depends on how sharply the machine can turn.
using UnityEngine;

namespace SpaceGame.Agents
{
    [System.Serializable]
    public struct NavPathFollowerSettings
    {
        // Floors for Validate. Below these a route is rebuilt every frame, rebuilt on every twitch
        // of the destination, never counts a corner as rounded, or never finds the mesh under a
        // machine resting on it.
        private const float MinRepathInterval = 0.05f;
        private const float MinRepathTolerance = 0.1f;
        private const float MinCornerArriveRadius = 0.1f;
        private const float MinNavMeshSampleDistance = 0.5f;
        private const float MinStillTargetRepathInterval = 0.05f;

        [Tooltip("Seconds between route recalculations while following a NavMesh path.")]
        public float repathInterval;
        [Tooltip("How far a destination must move before the route is rebuilt early.")]
        public float repathTolerance;
        [Tooltip("How close to a path corner counts as rounded. Size this to the machine: too small " +
                 "and one that cannot turn sharply grinds against the corner it is standing on.")]
        public float cornerArriveRadius;
        [Tooltip("How far from the machine and from the destination to search for the NavMesh. A body " +
                 "that rides metres above the ground needs this to clear the ride height.")]
        public float navMeshSampleDistance;
        [Tooltip("Seconds between route rebuilds while the destination has not moved past repathTolerance. " +
                 "Long routes are expensive to rebuild; a route only goes stale when the body is pushed off it.\n\n" +
                 "A body further than cornerArriveRadius from its route falls back to repathInterval. " +
                 "Never shorter than repathInterval.")]
        public float stillTargetRepathInterval;

        /// Rebuilds a route to a still destination as often as to a moving one: what every follower
        /// did before stillTargetRepathInterval existed.
        public NavPathFollowerSettings(float repathInterval, float repathTolerance, float cornerArriveRadius,
                                       float navMeshSampleDistance)
            : this(repathInterval, repathTolerance, cornerArriveRadius, navMeshSampleDistance,
                   stillTargetRepathInterval: repathInterval)
        {
        }

        public NavPathFollowerSettings(float repathInterval, float repathTolerance, float cornerArriveRadius,
                                       float navMeshSampleDistance, float stillTargetRepathInterval)
        {
            this.repathInterval = repathInterval;
            this.repathTolerance = repathTolerance;
            this.cornerArriveRadius = cornerArriveRadius;
            this.navMeshSampleDistance = navMeshSampleDistance;
            this.stillTargetRepathInterval = stillTargetRepathInterval;
        }

        /// Clamp to the floors. For the owning motor's OnValidate.
        public void Validate()
        {
            repathInterval = Mathf.Max(MinRepathInterval, repathInterval);
            repathTolerance = Mathf.Max(MinRepathTolerance, repathTolerance);
            cornerArriveRadius = Mathf.Max(MinCornerArriveRadius, cornerArriveRadius);
            navMeshSampleDistance = Mathf.Max(MinNavMeshSampleDistance, navMeshSampleDistance);
            stillTargetRepathInterval = Mathf.Max(MinStillTargetRepathInterval, repathInterval, stillTargetRepathInterval);
        }
    }
}
