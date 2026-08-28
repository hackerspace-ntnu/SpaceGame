// Drive for the lightning conjurer.
//
// Everything a legged machine's driver does -- the rider channel, the AI channel, the rider-frame
// guard, acceleration, NavMesh path following, ForceStop -- is LeggedDriver. What is left here is
// the one thing that is this machine's own: what it does when nobody is driving it.
//
// For the conjurer that answer is STAND STILL, which is also its whole first impression. It waits,
// inert, until a player comes inside AgentTargeting's acquisition range; ChaseModule then starts
// feeding this driver a destination and the legs carry it there. So unlike the ostrich and the
// humanoid there is no autoWalk convenience here by default -- a machine that strolls off on its
// own while nothing has provoked it is the one behaviour this creature must not have. The debug
// flag is still available for looking at the gait in isolation.
//
// This has to live in Assembly-CSharp rather than in SpaceGame.Creatures.Conjurer, because
// LeggedDriver does: IRiderControllable and IMovementMotor are declared in the default assembly
// and no asmdef may reference it.
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Creatures.Conjurer;

namespace SpaceGame.Creatures
{
    [RequireComponent(typeof(ConjurerLocomotion))]
    public class ConjurerDriver : LeggedDriver
    {
        [Header("Conjurer")]
        [Tooltip("Throttle the machine settles at when it is walking somewhere on its own. Only " +
                 "affects how fast it asks to go; the gait's own duty blend decides whether that " +
                 "comes out as a walk or a run.")]
        [Range(0f, 1f)]
        [SerializeField] private float walkThrottle = 0.5f;

        [Tooltip("Walk forward on its own, for looking at the gait without an AgentController or a " +
                 "rider. Debug only -- leave OFF, or the creature will not stand still when idle.")]
        [SerializeField] private bool autoWalk;

        /// There is deliberately no keyboard fallback: SteerModule is the one input path, and a
        /// second one polling the keyboard directly would drive the machine out from under a rider
        /// who is not touching it.
        protected override void Idle()
        {
            if (!autoWalk)
            {
                base.Idle();
                return;
            }

            forward = walkThrottle;
            turn = 0f;
        }
    }
}
