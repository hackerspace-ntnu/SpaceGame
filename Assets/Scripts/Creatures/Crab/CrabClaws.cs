// Ticks the claws, after the machine has finished walking.
//
// It exists only to own the frame. `SolveArms()` is protected on `LeggedLocomotion` -- an arm's
// target is the robot's business and nobody else's -- so the claw pose has to be driven from
// `CrabLocomotion`, and `CrabLocomotion` cannot tick it itself: its base already owns `LateUpdate`,
// and a second `LateUpdate` on the subclass HIDES the base's rather than adding to it, which would
// silently stop the machine walking at all.
//
// Order 150: after the locomotion at 100. The arms hang off the shell, so posing them before the
// shell has moved leaves them a frame behind it -- the same fault the frame order in
// `LeggedLocomotion.Step` exists to prevent for the legs, arriving from the other end.
//
// This is the shape `OstrichSpineMotion` uses for the same reason, and for the same reason it is
// tiny: everything worth testing is in the plain class underneath it.
using UnityEngine;

[DefaultExecutionOrder(150)]
[RequireComponent(typeof(CrabLocomotion))]
public class CrabClaws : MonoBehaviour
{
    [Tooltip("Hold the claws up from the moment the machine spawns. For a display piece; the " +
             "raise is normally asked for by whatever decided the crab is threatened.")]
    [SerializeField] private bool raisedOnStart;

    private CrabLocomotion crab;

    private void Awake()
    {
        crab = GetComponent<CrabLocomotion>();
        if (crab != null) crab.RaiseClaws(raisedOnStart);
    }

    /// Bring the claws up, or put them back down.
    public void Raise(bool raised)
    {
        if (crab != null) crab.RaiseClaws(raised);
    }

    private void LateUpdate()
    {
        if (crab != null) crab.PoseClaws(Time.deltaTime);
    }
}
