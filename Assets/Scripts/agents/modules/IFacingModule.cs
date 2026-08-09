// Opt-in second channel for modules that want to steer where an agent LOOKS without claiming
// where it MOVES.
//
// Locomotion arbitration is winner-take-all: one module owns the frame and everything below it
// contributes nothing. That forced every combat behaviour to stop the agent in order to aim,
// which is what produced the stop-start shuffle at the edge of a weapon's range. Facing is
// arbitrated separately — highest FacingPriority wins — so AgentRangedCombatModule can hold the
// body on target while ChaseModule still drives the path.
//
// Implement alongside IBehaviourModule on the same component. Return false to pass.
using UnityEngine;

public interface IFacingModule
{
    int FacingPriority { get; }
    bool IsActive { get; }
    bool TryGetFacing(in AgentContext context, out Vector3 facePosition);
}
