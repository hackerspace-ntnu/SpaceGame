// Finds the best nearby CoverPoint relative to a threat and moves behind it.
// Activates when threat is within threat range. Vacates cover when safe.
// Pair with RangedAttackModule: entity hides, peeks, shoots.
using UnityEngine;

public class CoverModule : BehaviourModuleBase
{
    [Header("Threat")]
    [SerializeField] private Transform threat;
    [SerializeField] private string threatTag = "Player";
    [SerializeField] private FactionRelationship threatRelationship = FactionRelationship.Hostile;

    [Header("Cover Seeking")]
    [SerializeField] private float threatRange = 14f;
    [SerializeField] private float coverSearchRadius = 12f;
    [SerializeField] private float stopDistance = 0.5f;
    [SerializeField] private float speedMultiplier = 1.3f;

    private CoverPoint occupiedCover;
    private bool arrivedAtCover;

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive + 1); // 21 — beats plain chase

    private void OnEnable() => VacateCover();
    private void OnDisable() => VacateCover();

    public override string ModuleDescription =>
        "Finds the nearest available CoverPoint and moves behind it when a threat is within range. Stays in cover until the threat leaves.\n\n" +
        "• threatRange — threat must be within this distance to trigger cover-seeking\n" +
        "• coverSearchRadius — only considers CoverPoints within this radius\n" +
        "• Requires CoverPoint components placed in the scene (behind rocks, crates, walls)\n" +
        "• Pair with RangedAttackModule to shoot from cover";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        TryResolveThreat();
        if (!threat)
            return null;

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

    private void TryResolveThreat()
    {
        if (threat)
            return;
        Transform candidate = EntityTargetRegistry.Resolve(threatTag, transform.position);
        if (candidate && EntityFaction.IsValidTarget(transform, candidate, threatRelationship))
            threat = candidate;
    }

    protected override void OnValidate()
    {
        threatRange = Mathf.Max(0.1f, threatRange);
        coverSearchRadius = Mathf.Max(0.1f, coverSearchRadius);
        stopDistance = Mathf.Max(0.01f, stopDistance);
        speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
    }
}
