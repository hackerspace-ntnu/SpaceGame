// Contract for all drag-and-drop behaviour modules on entity prefabs.
//
// Tick() return values:
//   null            — pass. Movement control falls through to the next module.
//   MoveIntent.Idle — claim the frame and hold position. Everything below this module is
//                     starved for as long as it keeps returning Idle, so only use it when
//                     standing still IS the behaviour (a guard at a post, a committed
//                     attack animation). A module that is merely between actions must
//                     return null instead.
//   MoveTo / StopAndFace — claim the frame and drive the motor.
//
// Modules where ClaimsMovement == false (pure side-effect modules) are always ticked
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
