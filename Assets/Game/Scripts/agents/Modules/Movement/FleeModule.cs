// Runs away from the nearest hostile-faction entity within triggerRadius.
// Deactivates itself once the threat is beyond safeRadius (hysteresis).
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents
{
    public class FleeModule : BehaviourModuleBase
    {
        [Header("Threat")]
        [SerializeField] private Transform threat;
        [Tooltip("Faction relationship the nearest threat must have. Requires EntityFaction on both entities.")]
        [SerializeField] private FactionRelationship fleeFromRelationship = FactionRelationship.Hostile;

        [Header("Ranges")]
        [SerializeField] private float triggerRadius = 6f;
        [SerializeField] private float safeRadius = 10f;

        [Header("Movement")]
        [SerializeField] private float fleeSpeedMultiplier = 1.4f;
        [SerializeField] private float stopDistance = 0.2f;
        [SerializeField] private float navMeshSampleDistance = 4f;

        private bool fleeing;
        private EntityFaction selfFaction;
        private float retargetTimer;

        // Set by RestoreFlee, consumed by the next OnEnable.
        private bool restoredFlee;

        // ── Persisted state ───────────────────────────────────────────────────────
        public bool IsFleeing => fleeing;

        /// <summary>Who is being run from. Re-resolved on an interval, so this is a starting point.</summary>
        public Transform Threat => threat;

        public float RetargetTimer => retargetTimer;

        private void Awake() => selfFaction = GetComponent<EntityFaction>();
        private void Reset() => SetPriorityDefault(ModulePriority.Override);

        private void OnEnable()
        {
            // The flee flag is hysteresis, not a derived value — see RestoreFlee.
            if (restoredFlee)
            {
                restoredFlee = false;
                return;
            }

            fleeing = false;
            retargetTimer = 0f;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// <paramref name="wasFleeing"/> cannot be recomputed from the world, which is the whole
        /// point of persisting it. Fleeing starts inside <c>triggerRadius</c> and stops outside
        /// <c>safeRadius</c>, so for any threat sitting between the two the flag is the only thing
        /// that says which side of the hysteresis the creature is on. A DuneRat that reloads calm
        /// with the player 8 m away — past the 6 m trigger, inside the 10 m safe radius — stands
        /// there and waits to be shot.
        /// </summary>
        public void RestoreFlee(bool wasFleeing, float retarget)
        {
            fleeing = wasFleeing;
            retargetTimer = Mathf.Max(0f, retarget);
            restoredFlee = true;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Separate from <see cref="RestoreFlee"/> because the threat is a cross-object reference and
        /// arrives later, once the save system can resolve it. Losing it is survivable — the next
        /// <c>Refresh</c> finds a threat of its own — but the restored flag would then be applied to
        /// whoever that turns out to be rather than to the creature's actual pursuer.
        /// </summary>
        public void RestoreThreat(Transform restoredThreat)
        {
            if (restoredThreat != null)
                threat = restoredThreat;
        }

        public override string ModuleDescription =>
            "Runs away from the nearest entity with the configured faction relationship when it enters triggerRadius. Stops fleeing once beyond safeRadius.\n\n" +
            "• triggerRadius — threat must enter this range to start fleeing\n" +
            "• safeRadius — entity stops fleeing once threat is this far away\n" +
            "• fleeSpeedMultiplier — movement speed boost while fleeing\n" +
            "• fleeFromRelationship — faction relationship that identifies a threat (default: Hostile)";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // Re-resolved on an interval and dropped when it dies. Holding the first threat forever
            // is how a creature ended up fleeing from a corpse for the rest of the session.
            threat = TargetResolution.Refresh(threat, ref retargetTimer, 0.5f, deltaTime,
                                              selfFaction, fleeFromRelationship, context.Position);
            if (!threat)
            {
                fleeing = false;
                return null;
            }

            float distance = Vector3.Distance(context.Position, threat.position);

            if (!fleeing && distance <= triggerRadius)
                fleeing = true;
            else if (fleeing && distance > safeRadius)
                fleeing = false;

            if (!fleeing)
                return null;

            if (TryGetFleeDestination(context.Position, threat.position, out Vector3 dest))
                return MoveIntent.MoveTo(dest, stopDistance, fleeSpeedMultiplier);

            return null;
        }

        private bool TryGetFleeDestination(Vector3 self, Vector3 threatPos, out Vector3 destination)
        {
            Vector3 away = self - threatPos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
            {
                away = Random.insideUnitSphere;
                away.y = 0f;
            }

            Vector3 candidate = self + away.normalized * safeRadius;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
            {
                destination = hit.position;
                return true;
            }

            destination = self;
            return false;
        }

        protected override void OnValidate()
        {
            triggerRadius = Mathf.Max(0.1f, triggerRadius);
            safeRadius = Mathf.Max(triggerRadius, safeRadius);
            fleeSpeedMultiplier = Mathf.Max(0.01f, fleeSpeedMultiplier);
            stopDistance = Mathf.Max(0.01f, stopDistance);
            navMeshSampleDistance = Mathf.Max(0.5f, navMeshSampleDistance);
        }
    }
}
