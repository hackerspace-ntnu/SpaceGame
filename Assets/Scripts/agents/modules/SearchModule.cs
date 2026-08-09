// Activates when the agent loses its target. Moves to the last known position, searches
// briefly, then deactivates — passing control back to lower-priority modules.
// Sits between ChaseModule (Reactive=20) and WanderModule (Fallback=0).
//
// Reads AgentTargeting for both the "target lost" edge and the position to search, so it
// investigates the entity the agent was actually fighting rather than whichever one this
// module would have resolved for itself.
using UnityEngine;
using FMODUnity;

public class SearchModule : BehaviourModuleBase
{
    [Header("Search")]
    [SerializeField] private float searchDuration = 4f;
    [SerializeField] private float stopDistance = 0.6f;
    [SerializeField] private float speedMultiplier = 1.1f;

    [Header("Audio")]
    [SerializeField] private EventReference searchSound;

    private bool isSearching;
    private float searchTimer;
    private Vector3 searchPosition;
    private bool hadTarget; // tracks edge: target just became lost

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive - 1); // 19 — just below Chase

    private void OnEnable()
    {
        isSearching = false;
        searchTimer = 0f;
        hadTarget = false;
    }

    public override string ModuleDescription =>
        "When the agent loses its target, moves to the last known position and searches for a short time before giving up.\n\n" +
        "Reads AgentTargeting for the lost-target edge and the last known position.\n\n" +
        "• searchDuration — how many seconds to search before returning to idle\n" +
        "• Automatically deactivates when a target is reacquired";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        AgentTargeting targeting = context.Targeting;
        if (targeting == null)
            return null;

        bool hasTarget = targeting.HasTarget;

        // Detect falling edge: had target last frame, lost it this frame.
        if (hadTarget && !hasTarget && !isSearching && targeting.HasLastKnownPosition)
        {
            searchPosition = targeting.LastKnownPosition;
            isSearching = true;
            searchTimer = searchDuration;

            if (!searchSound.IsNull)
                RuntimeManager.PlayOneShot(searchSound, transform.position);
        }

        hadTarget = hasTarget;

        if (!isSearching)
            return null;

        // Abort if chase reacquired.
        if (hasTarget)
        {
            isSearching = false;
            return null;
        }

        searchTimer -= deltaTime;
        if (searchTimer <= 0f)
        {
            isSearching = false;
            return null;
        }

        return MoveIntent.MoveTo(searchPosition, stopDistance, speedMultiplier);
    }

    protected override void OnValidate()
    {
        searchDuration = Mathf.Max(0.1f, searchDuration);
        stopDistance = Mathf.Max(0.01f, stopDistance);
        speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
    }
}
