// Drive for the humanoid robot.
//
// Everything a legged machine's driver does -- the rider channel, the AI channel, the rider-frame
// guard, acceleration, NavMesh path following, ForceStop -- is LeggedDriver, and that includes
// `IRiderControllable`, so a rider steers this through `SteerModule` with no code here at all.
// What is left is the one thing that is this machine's own: what it does when nobody is driving it.
//
// This has to live in Assembly-CSharp rather than in SpaceGame.Creatures.Humanoid, because
// LeggedDriver does: `IRiderControllable` and `IMovementMotor` are declared in the default assembly
// and no asmdef may reference it.
using UnityEngine;

[RequireComponent(typeof(HumanoidLocomotion))]
public class HumanoidDriver : LeggedDriver
{
    [Header("Humanoid")]
    [Tooltip("Throttle the machine settles at when it is walking somewhere on its own. Only " +
             "affects how fast it asks to go; the gait's own duty blend decides whether that comes " +
             "out as a walk or a run.")]
    [Range(0f, 1f)]
    [SerializeField] private float walkThrottle = 0.35f;

    [Tooltip("Walk forward on its own, for looking at the gait without a rider aboard. Debug only.")]
    [SerializeField] private bool autoWalk;

    /// There is deliberately no keyboard fallback: SteerModule is the one input path, and a second
    /// one polling the keyboard directly would drive the machine out from under a rider who is not
    /// touching it.
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
