// Defines WHERE an agent patrols — not how it walks.
//
//   RadiusBased  — picks random NavMesh destinations within a radius of a base point.
//                  The agent wanders freely inside the area; locomotion speed is untouched.
//
//   PatrolPoints — cycles through a list of Transform waypoints in sequence, ping-pong,
//                  or random order. Waits at each point before moving on.
//
// Neither mode controls movement speed or style. Both let higher-priority modules
// (combat, chase, etc.) interrupt at any time.
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents
{
    public enum PatrolMode { RadiusBased, PatrolPoints }

    public class PatrolModule : BehaviourModuleBase
    {
        [Header("Mode")]
        [SerializeField] private PatrolMode mode = PatrolMode.RadiusBased;

        [Header("Radius Based")]
        [Tooltip("Center of the patrol area. Uses the agent's spawn position if left empty.")]
        [SerializeField] private Transform radiusCenter;
        [Tooltip("Furthest distance from the center that destinations can be picked.")]
        [SerializeField] private float patrolRadius = 15f;
        [Tooltip("Destinations closer than this to the agent are discarded and re-sampled.")]
        [SerializeField] private float minMoveDistance = 3f;

        [Header("Patrol Points")]
        [SerializeField] private Transform[] patrolPoints;
        [SerializeField] private PatrolSelectionMode selectionMode = PatrolSelectionMode.SequentialLoop;

        [Header("Timing")]
        [SerializeField] private float minWaitTime = 1f;
        [SerializeField] private float maxWaitTime = 3f;

        [Header("Arrival")]
        [Tooltip("How close to the destination counts as arrived.")]
        [SerializeField] private float stopDistance = 0.5f;

        [Tooltip("While pausing between patrol points, claim the frame and stand still (a guard holding " +
                 "a post). Uncheck to yield the pause to lower-priority modules instead — required when " +
                 "something below this module should run during the wait, e.g. HuntModule on arena bots.")]
        [SerializeField] private bool holdPositionWhileWaiting = true;

        private Vector3 spawnAnchor;
        private bool hasSpawnAnchor;
        private Vector3? destination;
        private float waitTimer;
        private int waypointIndex;
        private int waypointDirection = 1;

        /// <summary>
        /// Set by a restore, consumed by the next <see cref="OnEnable"/>.
        ///
        /// <see cref="OnEnable"/> zeroes the whole route, and hydration does not guarantee it runs
        /// before the restore — a chunk that streams back in re-enables this module *after* the
        /// record has been applied. Without the latch the restore is silently undone and the guard
        /// starts again at waypoint 0, which is the behaviour the saver exists to remove.
        /// </summary>
        private bool restoredThisEnable;

        private void Reset() => SetPriorityDefault(ModulePriority.Fallback);

        private void OnEnable()
        {
            if (restoredThisEnable)
            {
                restoredThisEnable = false;
                return;
            }

            destination = null;
            waitTimer = 0f;
            waypointIndex = 0;
            waypointDirection = 1;
        }

        // ─────────── Patrol progress, for the save system ───────────
        // Exposed so an agent resumes its route instead of restarting it. Where a patroller is in its
        // circuit is exactly the kind of state a player notices coming back wrong: a guard that always
        // stands at waypoint 0 after every reload reads as a route that does not work.

        public int WaypointIndex => waypointIndex;

        /// <summary>+1 or -1 on a ping-pong route; unused on a looping one.</summary>
        public int WaypointDirection => waypointDirection;

        public bool HasDestination => destination.HasValue;

        /// <summary>Meaningless unless <see cref="HasDestination"/>.</summary>
        public Vector3 Destination => destination ?? Vector3.zero;

        /// <summary>Seconds left of the pause at the current post.</summary>
        public float WaitTimer => waitTimer;

        /// <summary>
        /// Whether the patrol circle's centre has been decided yet.
        ///
        /// This is the territory. In <see cref="PatrolMode.RadiusBased"/> with no explicit
        /// <c>radiusCenter</c>, the centre is latched once from wherever the agent woke up — so if a
        /// reload lets it latch a second time, the whole patrol area moves to wherever the guard
        /// happened to be standing when the game was saved, and moves again on the next save.
        /// </summary>
        public bool HasSpawnAnchor => hasSpawnAnchor;

        public Vector3 SpawnAnchor => spawnAnchor;

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Puts an agent back where it was in its circuit. Clamped rather than trusted: the route is
        /// authored data and may have had waypoints removed since the save was written.
        /// </summary>
        public void RestorePatrolProgress(int index, int direction)
        {
            waypointIndex = patrolPoints == null || patrolPoints.Length == 0
                ? 0
                : Mathf.Clamp(index, 0, patrolPoints.Length - 1);

            waypointDirection = direction < 0 ? -1 : 1;
            restoredThisEnable = true;
        }

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestorePatrolLeg(bool hasDestination, Vector3 target, float wait)
        {
            destination = hasDestination ? target : (Vector3?)null;
            waitTimer = Mathf.Max(0f, wait);
            restoredThisEnable = true;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Hands back the patrol centre the agent had before the save, and marks it latched so
        /// <see cref="EnsureAnchor"/> never re-derives it from the current position.
        /// </summary>
        public void RestoreSpawnAnchor(bool hasAnchor, Vector3 anchor)
        {
            if (!hasAnchor) return;

            spawnAnchor = anchor;
            hasSpawnAnchor = true;
            restoredThisEnable = true;
        }

        public override string ModuleDescription =>
            "Defines where the agent patrols. Does not control locomotion speed or style.\n\n" +
            "RadiusBased — roams to random NavMesh points within a radius of the base point.\n" +
            "  • radiusCenter — center of the area (uses spawn position if empty)\n" +
            "  • patrolRadius — maximum distance from center for picked destinations\n" +
            "  • minMoveDistance — re-samples if destination is too close to the agent\n\n" +
            "PatrolPoints — visits assigned Transforms in a defined order.\n" +
            "  • patrolPoints — list of waypoints to visit\n" +
            "  • selectionMode — SequentialLoop, PingPong, or Random\n\n" +
            "Shared:\n" +
            "  • minWaitTime / maxWaitTime — pause at each point before picking the next\n" +
            "  • stopDistance — how close counts as arrived";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            EnsureAnchor();

            if (waitTimer > 0f)
            {
                waitTimer -= deltaTime;
                return holdPositionWhileWaiting ? MoveIntent.Idle() : (MoveIntent?)null;
            }

            if (!destination.HasValue)
            {
                destination = mode == PatrolMode.RadiusBased
                    ? PickRadiusDestination(context.Position)
                    : PickWaypointDestination();
            }

            if (!destination.HasValue)
                return null;

            if (context.HasReachedDestination ||
                Vector3.Distance(context.Position, destination.Value) <= stopDistance)
            {
                destination = null;
                waitTimer = Random.Range(minWaitTime, maxWaitTime);
                return holdPositionWhileWaiting ? MoveIntent.Idle() : (MoveIntent?)null;
            }

            return MoveIntent.MoveTo(destination.Value, stopDistance);
        }

        private Vector3? PickRadiusDestination(Vector3 currentPos)
        {
            Vector3 center = radiusCenter ? radiusCenter.position : spawnAnchor;
            float sampleRadius = Mathf.Max(2f, patrolRadius * 0.15f);

            for (int i = 0; i < 12; i++)
            {
                Vector2 circle = Random.insideUnitCircle * patrolRadius;
                Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                    continue;

                if (Vector3.Distance(currentPos, hit.position) < minMoveDistance)
                    continue;

                Vector3 offset = hit.position - center;
                offset.y = 0f;
                if (offset.sqrMagnitude > patrolRadius * patrolRadius)
                    continue;

                return hit.position;
            }

            return null;
        }

        private Vector3? PickWaypointDestination()
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
                return null;

            int index = AdvanceWaypointIndex();
            Transform point = patrolPoints[index];
            if (!point)
                return null;

            if (NavMesh.SamplePosition(point.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                return hit.position;

            return null;
        }

        private int AdvanceWaypointIndex()
        {
            if (selectionMode == PatrolSelectionMode.Random)
                return Random.Range(0, patrolPoints.Length);

            int current = Mathf.Clamp(waypointIndex, 0, patrolPoints.Length - 1);

            if (selectionMode == PatrolSelectionMode.SequentialLoop)
            {
                waypointIndex = (waypointIndex + 1) % patrolPoints.Length;
                return current;
            }

            // PingPong
            if (patrolPoints.Length == 1)
                return 0;

            waypointIndex += waypointDirection;
            if (waypointIndex >= patrolPoints.Length)
            {
                waypointIndex = patrolPoints.Length - 2;
                waypointDirection = -1;
            }
            else if (waypointIndex < 0)
            {
                waypointIndex = 1;
                waypointDirection = 1;
            }

            return current;
        }

        private void EnsureAnchor()
        {
            if (!hasSpawnAnchor)
            {
                spawnAnchor = transform.position;
                hasSpawnAnchor = true;
            }
        }

        protected override void OnValidate()
        {
            patrolRadius     = Mathf.Max(0.5f, patrolRadius);
            minMoveDistance  = Mathf.Clamp(minMoveDistance, 0.1f, patrolRadius);
            minWaitTime      = Mathf.Max(0f, minWaitTime);
            maxWaitTime      = Mathf.Max(minWaitTime, maxWaitTime);
            stopDistance     = Mathf.Max(0.01f, stopDistance);
        }
    }
}
