// Contract for all drag-and-drop behaviour modules on entity prefabs.
// Return null from Tick() to pass movement control to the next module; return a MoveIntent to claim this frame.
// Modules where ClaimsMovement == false (e.g. EntityCombatModule, RangedAttackModule) are always ticked
// as side-effects even after a movement winner is found — they must always return null from Tick().
public interface IBehaviourModule
{
    int Priority { get; }
    bool IsActive { get; }
    // False for pure side-effect modules (attack, audio, etc.) that never return a MoveIntent.
    // AgentController ticks all side-effect modules every frame regardless of movement winner.
    bool ClaimsMovement { get; }
    MoveIntent? Tick(in AgentContext context, float deltaTime);
}
