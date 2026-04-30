// Forces the entity to stop and face a target for a set duration.
// Call FocusOn() from dialogue, cutscene, or interaction systems.
// Highest priority when active — overrides all movement.
using UnityEngine;

public class InteractionFocusModule : BehaviourModuleBase
{
    private Transform focusTarget;
    private float focusTimer;

    public bool IsFocused => focusTimer > 0f && focusTarget;

    // Call from dialogue/interaction systems.
    public void FocusOn(Transform target, float duration)
    {
        if (!target)
            return;
        focusTarget = target;
        focusTimer = Mathf.Max(focusTimer, duration);
    }

    public void ClearFocus()
    {
        focusTimer = 0f;
        focusTarget = null;
    }

    private void Reset() => SetPriorityDefault(ModulePriority.Scripted);
    private void OnEnable() => ClearFocus();

    public override string ModuleDescription =>
        "Stops and faces a target for a set duration. Triggered externally by calling FocusOn(target, duration). Highest priority — overrides all other movement while active.\n\n" +
        "• Call FocusOn(transform, seconds) from the dialog or interaction system\n" +
        "• Automatically releases control when the timer expires";

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (focusTimer <= 0f || !focusTarget)
        {
            focusTarget = null;
            focusTimer = 0f;
            return null;
        }

        focusTimer -= deltaTime;
        return MoveIntent.StopAndFace(focusTarget.position);
    }
}
