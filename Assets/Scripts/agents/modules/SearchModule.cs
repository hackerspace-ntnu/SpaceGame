// Activates when ChaseModule loses its target. Moves to the last known position,
// searches briefly, then deactivates — passing control back to lower-priority modules.
// Sits between ChaseModule (Reactive=20) and WanderModule (Fallback=0).
// PerceptionModule is optional: if present, uses its LastKnownPosition; otherwise uses
// the target's last seen transform position cached when chase was active.
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

    private ChaseModule chaseModule;
    private PerceptionModule perception;

    private bool isSearching;
    private float searchTimer;
    private Vector3 searchPosition;
    private bool hadTarget; // tracks edge: target just became lost

    private void Reset() => SetPriorityDefault(ModulePriority.Reactive - 1); // 19 — just below Chase
    private void Awake()
    {
        chaseModule = GetComponent<ChaseModule>();
        perception = GetComponent<PerceptionModule>();

        if (chaseModule == null)
            Debug.LogWarning($"{name}: SearchModule requires a ChaseModule on the same GameObject.", this);
    }

    private void OnEnable()
    {
        isSearching = false;
        searchTimer = 0f;
        hadTarget = false;
    }

    public override string ModuleDescription =>
        "When ChaseModule loses its target, moves to the last known position and searches for a short time before giving up.\n\n" +
        "Requires: ChaseModule on the same entity. PerceptionModule is optional — if present, its LastKnownPosition is preferred.\n\n" +
        "• searchDuration — how many seconds to search before returning to idle\n" +
        "• Automatically deactivates when ChaseModule reacquires a target";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (chaseModule == null)
            return null;

        bool hasTarget = chaseModule.HasTarget;

        // Detect falling edge: had target last frame, lost it this frame.
        if (hadTarget && !hasTarget && !isSearching)
        {
            bool gotPosition = false;

            if (perception != null && perception.HasLastKnownPosition)
            {
                searchPosition = perception.LastKnownPosition;
                gotPosition = true;
            }
            else if (chaseModule.LastKnownPosition.HasValue)
            {
                searchPosition = chaseModule.LastKnownPosition.Value;
                gotPosition = true;
            }

            if (gotPosition)
            {
                isSearching = true;
                searchTimer = searchDuration;

                if (!searchSound.IsNull)
                    RuntimeManager.PlayOneShot(searchSound, transform.position);
            }
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
