// Lowest-priority fallback: roams randomly using NavMesh sampling.
// Add this to any entity that should move around when nothing else claims the frame.
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents
{
    public class WanderModule : BehaviourModuleBase
    {
        [Header("Wander")]
        [SerializeField] private bool limitWanderRadius = true;
        [SerializeField] private float wanderRadius = 10f;
        [Tooltip("Candidate range when limitWanderRadius is off. NavMesh is sampled within this distance.")]
        [SerializeField] private float freeRoamRadius = 50f;
        [SerializeField] private float sampleDistance = 6f;
        [SerializeField] private float minDestinationDistance = 1.5f;
        [SerializeField] private int maxSampleAttempts = 10;

        [Header("Wait")]
        [SerializeField] private float minWaitTime = 0.5f;
        [SerializeField] private float maxWaitTime = 2f;

        [Header("Movement")]
        [SerializeField] private float stopDistance = 0.2f;
        [SerializeField] private float speedMultiplier = 1f;

        private bool hasDestination;
        private Vector3 currentDestination;
        private float waitTimer;
        /// <summary>
        /// Set by a restore, consumed by the next <see cref="OnEnable"/>. Hydration does not
        /// guarantee <see cref="OnEnable"/> runs before the restore — a chunk streaming back in
        /// re-enables the module afterwards — and without the latch the restored walk is thrown away
        /// and the creature re-rolls a destination it was already halfway to.
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

            hasDestination = false;
            waitTimer = 0f;
        }

        // ─────────── For the save system ───────────

        public bool HasDestination => hasDestination;

        /// <summary>Meaningless unless <see cref="HasDestination"/>.</summary>
        public Vector3 CurrentDestination => currentDestination;

        /// <summary>Seconds left of the idle pause after reaching a point.</summary>
        public float WaitTimer => waitTimer;

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreWander(bool destinationSet, Vector3 destination, float wait)
        {
            hasDestination = destinationSet;
            currentDestination = destination;
            waitTimer = Mathf.Max(0f, wait);
            restoredThisEnable = true;
        }

        public override string ModuleDescription =>
            "Roams randomly across the NavMesh. No extra components required.\n\n" +
            "• limitWanderRadius — uncheck to roam freely across the whole NavMesh\n" +
            "• wanderRadius — how far from current position to pick destinations (only when limited)\n" +
            "• freeRoamRadius — candidate range when not limited; sample radius scales with it\n" +
            "• sampleDistance — NavMesh.SamplePosition search radius\n" +
            "• minDestinationDistance — ignore destinations closer than this\n" +
            "• maxSampleAttempts — tries per destination before giving up\n" +
            "• minWaitTime / maxWaitTime — idle pause range after reaching each point\n" +
            "• stopDistance — how close counts as 'reached'\n" +
            "• speedMultiplier — movement speed scale while wandering";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (hasDestination && context.HasReachedDestination)
            {
                hasDestination = false;
                waitTimer = Random.Range(minWaitTime, maxWaitTime);
            }

            if (waitTimer > 0f)
            {
                waitTimer -= deltaTime;
                return null;
            }

            if (!hasDestination)
            {
                if (!TryPickDestination(context.Position, out currentDestination))
                    return null;
                hasDestination = true;
            }

            return MoveIntent.MoveTo(currentDestination, stopDistance, speedMultiplier);
        }

        private bool TryPickDestination(Vector3 origin, out Vector3 destination)
        {
            float radius = limitWanderRadius ? wanderRadius : freeRoamRadius;
            float sample = limitWanderRadius ? sampleDistance : Mathf.Max(sampleDistance, radius * 0.2f);

            for (int i = 0; i < maxSampleAttempts; i++)
            {
                Vector3 candidate = origin + Random.insideUnitSphere * radius;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sample, NavMesh.AllAreas))
                    continue;

                if (Vector3.Distance(origin, hit.position) < minDestinationDistance)
                    continue;

                destination = hit.position;
                return true;
            }

            destination = origin;
            return false;
        }

        protected override void OnValidate()
        {
            if (limitWanderRadius)
                wanderRadius = Mathf.Max(0.1f, wanderRadius);
            freeRoamRadius = Mathf.Max(wanderRadius, freeRoamRadius);
            sampleDistance = Mathf.Max(0.5f, sampleDistance);
            minDestinationDistance = Mathf.Max(0.1f, minDestinationDistance);
            maxSampleAttempts = Mathf.Max(1, maxSampleAttempts);
            minWaitTime = Mathf.Max(0f, minWaitTime);
            maxWaitTime = Mathf.Max(minWaitTime, maxWaitTime);
            stopDistance = Mathf.Max(0.01f, stopDistance);
            speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        }
    }
}
