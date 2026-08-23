// Finds the best nearby CoverPoint relative to a threat and moves behind it.
// Activates when threat is within threat range. Vacates cover when safe.
// Pair with AgentRangedCombatModule: entity hides, peeks, shoots.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class CoverModule : BehaviourModuleBase
    {
        [Header("Threat")]
        [SerializeField] private Transform threat;
        [Tooltip("Faction relationship the nearest threat must have. Requires EntityFaction on both entities.")]
        [SerializeField] private FactionRelationship threatRelationship = FactionRelationship.Hostile;

        private EntityFaction selfFaction;
        private float retargetTimer;

        private void Awake() => selfFaction = GetComponent<EntityFaction>();

        [Header("Cover Seeking")]
        [SerializeField] private float threatRange = 14f;
        [SerializeField] private float coverSearchRadius = 12f;
        [SerializeField] private float stopDistance = 0.5f;
        [SerializeField] private float speedMultiplier = 1.3f;

        private CoverPoint occupiedCover;
        private bool arrivedAtCover;

        // Set by RestoreCover, consumed by the next OnEnable.
        private bool restoredCover;

        // ── Persisted state ───────────────────────────────────────────────────────
        public CoverPoint OccupiedCover => occupiedCover;
        public bool ArrivedAtCover => arrivedAtCover;
        public float RetargetTimer => retargetTimer;

        private void Reset() => SetPriorityDefault(ModulePriority.Reactive + 1); // 21 — beats plain chase

        private void OnEnable()
        {
            // Without this, every agent pinned behind a rock steps into the open the moment the world
            // reloads — and the reservation it never re-took is free for a second agent to claim.
            if (restoredCover)
            {
                restoredCover = false;
                return;
            }

            VacateCover();
        }

        // Not latched: leaving the world genuinely releases the reservation, and the occupancy count
        // on the point has to come down with it or the cover is lost for the rest of the session.
        private void OnDisable() => VacateCover();

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Re-takes the reservation through <see cref="CoverPoint.TryOccupy"/> rather than assigning
        /// the field, because the occupancy count is not itself persisted: it is rebuilt from
        /// whichever agents get their claim back. That is also what stops two agents from restoring
        /// onto one single-occupant point — the second one's claim is refused here exactly as it
        /// would be during play.
        ///
        /// Returns false when nothing was claimed, which is an ordinary outcome: the point may have
        /// been taken, or <paramref name="point"/> may be null because the save named no cover.
        /// </summary>
        public bool RestoreCover(CoverPoint point, bool arrived, float retarget)
        {
            retargetTimer = Mathf.Max(0f, retarget);
            restoredCover = true;

            if (point == null)
            {
                VacateCover();
                return false;
            }

            // Idempotent: OnLoadComplete can run more than once, and claiming the same point twice
            // would push the occupancy count past maxOccupants and lock everyone else out of it.
            if (occupiedCover == point)
            {
                arrivedAtCover = arrived;
                return true;
            }

            VacateCover();

            if (!point.TryOccupy())
                return false;

            occupiedCover = point;
            arrivedAtCover = arrived;
            return true;
        }

        public override string ModuleDescription =>
            "Finds the nearest available CoverPoint and moves behind it when a threat is within range. Stays in cover until the threat leaves.\n\n" +
            "• threatRange — threat must be within this distance to trigger cover-seeking\n" +
            "• coverSearchRadius — only considers CoverPoints within this radius\n" +
            "• Requires CoverPoint components placed in the scene (behind rocks, crates, walls)\n" +
            "• Pair with AgentRangedCombatModule to shoot from cover";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // Prefer the agent's committed target: taking cover from someone other than whoever is
            // shooting at you is worse than taking no cover at all.
            AgentTargeting targeting = context.Targeting;
            if (targeting != null && targeting.HasTarget)
                threat = targeting.Target;
            else
                threat = TargetResolution.Refresh(threat, ref retargetTimer, 0.5f, deltaTime,
                                                  selfFaction, threatRelationship, context.Position);

            if (!threat)
            {
                VacateCover();
                return null;
            }

            float distToThreat = Vector3.Distance(context.Position, threat.position);
            if (distToThreat > threatRange)
            {
                VacateCover();
                return null;
            }

            // Already claimed a cover point — move to it, then hold once arrived
            if (occupiedCover != null)
            {
                if (!arrivedAtCover)
                {
                    if (Vector3.Distance(context.Position, occupiedCover.Position) <= stopDistance + 0.1f)
                        arrivedAtCover = true;
                    else
                        return MoveIntent.MoveTo(occupiedCover.Position, stopDistance, speedMultiplier);
                }
                return MoveIntent.StopAndFace(threat.position);
            }

            // Find best cover
            CoverPoint best = FindBestCover(context.Position, threat.position);
            if (best == null)
                return null;

            if (best.TryOccupy())
            {
                occupiedCover = best;
                arrivedAtCover = false;
                return MoveIntent.MoveTo(best.Position, stopDistance, speedMultiplier);
            }

            return null;
        }

        private CoverPoint FindBestCover(Vector3 self, Vector3 threatPos)
        {
            return CoverPointRegistry.FindBest(self, threatPos, coverSearchRadius);
        }

        private void VacateCover()
        {
            occupiedCover?.Vacate();
            occupiedCover = null;
            arrivedAtCover = false;
        }

        protected override void OnValidate()
        {
            threatRange = Mathf.Max(0.1f, threatRange);
            coverSearchRadius = Mathf.Max(0.1f, coverSearchRadius);
            stopDistance = Mathf.Max(0.01f, stopDistance);
            speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        }
    }
}
