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
        [Tooltip("Run from whatever AgentTargeting is currently holding, instead of scanning for a " +
                 "faction relationship.\n\n" +
                 "Turn this on for a peaceful animal. A Fauna creature has no rows in the " +
                 "relationship table, so it is Neutral toward everything and the scan below can " +
                 "never find a threat — it would stand and be shot. What it does have is " +
                 "ProvocationModule, which hands its attacker to AgentTargeting; this reads that.")]
        [SerializeField] private bool fleeFromCurrentTarget;
        [Tooltip("Faction relationship the nearest threat must have. Requires EntityFaction on both " +
                 "entities. Ignored when fleeFromCurrentTarget is on.")]
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

        /// <summary>
        /// Run now, whatever the distance.
        ///
        /// <para>
        /// <c>triggerRadius</c> answers "did something frightening get close", which is the right
        /// question for a creature noticing a predator and the wrong one for a creature that has
        /// just been told to be afraid. A gunshot carries 40 m; this module trips at 22. In that
        /// band Appa acquired the shooter, went to mood Fleeing, and then quietly kept walking his
        /// errand — sometimes toward the gun — because the hysteresis below never flipped. He read
        /// as completely unbothered by being shot at.
        /// </para>
        /// <para>
        /// So whoever decides the creature is frightened says so, and this decides where to run.
        /// Stopping is still <c>safeRadius</c>'s job, so an alarm cannot pin it running forever.
        /// </para>
        /// </summary>
        public void Alarm()
        {
            fleeing = true;
            restoredFlee = false;
        }

        public override string ModuleDescription =>
            "Runs away from the nearest entity with the configured faction relationship when it enters triggerRadius. Stops fleeing once beyond safeRadius.\n\n" +
            "• triggerRadius — threat must enter this range to start fleeing\n" +
            "• safeRadius — entity stops fleeing once threat is this far away\n" +
            "• fleeSpeedMultiplier — movement speed boost while fleeing\n" +
            "• fleeFromCurrentTarget — run from AgentTargeting's target instead of scanning by faction; " +
            "required for peaceful (Fauna) creatures, which are Neutral toward everything\n" +
            "• fleeFromRelationship — faction relationship that identifies a threat (default: Hostile)";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (fleeFromCurrentTarget)
            {
                // AgentTargeting already dropped it if it died, so this needs no viability pass of
                // its own — and taking the target verbatim is the point: the creature runs from
                // whoever provoked it, not from whoever happens to be nearest.
                threat = context.Targeting != null ? context.Targeting.Target : null;
            }
            else
            {
                // Re-resolved on an interval and dropped when it dies. Holding the first threat
                // forever is how a creature ended up fleeing from a corpse for the rest of the
                // session.
                threat = TargetResolution.Refresh(threat, ref retargetTimer, 0.5f, deltaTime,
                                                  selfFaction, fleeFromRelationship, context.Position);
            }

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
                // isRunning, always. It defaults to false, and without it the motor moves at the
                // WALK speed and the animator picks the walk clip -- so a creature running for its
                // life ambled away at 1.6 m/s with its walk cycle playing, which is the same defect
                // as the documented "provoked NPC closes at a walking pace". fleeSpeedMultiplier
                // scales the run; there is no case where fleeing at a walk is the intent.
                return MoveIntent.MoveTo(dest, stopDistance, fleeSpeedMultiplier, isRunning: true);

            return null;
        }

        /// <summary>
        /// Somewhere away from the threat that is actually on the NavMesh.
        ///
        /// <para>
        /// This used to sample one point, straight downwind at the full <c>safeRadius</c>, and
        /// give up if it missed. Missing is common — that point is tens of metres away, and a
        /// cliff, a building or the edge of the baked mesh is enough. Giving up returns
        /// <c>null</c> from <c>Tick</c>, and <c>null</c> means *pass*, so the frame fell through
        /// to whatever chase module sat below and the animal walked calmly at the thing it was
        /// running from.
        /// </para>
        /// <para>
        /// So: fan out. Directly away first, then progressively shorter, then off to the sides,
        /// which is also what a real animal does when the direct line is blocked. Only a creature
        /// genuinely boxed in on every side now fails, and being boxed in is a fact worth
        /// reporting to whatever is deciding whether to keep running.
        /// </para>
        /// </summary>
        private bool TryGetFleeDestination(Vector3 self, Vector3 threatPos, out Vector3 destination)
        {
            Vector3 away = self - threatPos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
            {
                away = Random.insideUnitSphere;
                away.y = 0f;
            }
            away.Normalize();

            // Straight away is best; a wide arc is still better than turning around.
            for (int i = 0; i < FleeArc.Length; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, FleeArc[i], 0f) * away;

                for (int step = 0; step < FleeDistanceSteps; step++)
                {
                    float distance = safeRadius * (1f - step / (float)FleeDistanceSteps);
                    if (distance < 1f)
                        break;

                    if (NavMesh.SamplePosition(self + direction * distance, out NavMeshHit hit,
                                               navMeshSampleDistance, NavMesh.AllAreas))
                    {
                        destination = hit.position;
                        return true;
                    }
                }
            }

            destination = self;
            return false;
        }

        // Tried in order, so the straight line downwind always wins when it is available.
        private static readonly float[] FleeArc = { 0f, -35f, 35f, -70f, 70f, -110f, 110f };
        private const int FleeDistanceSteps = 4;

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
