// The crab walker, as four choices and two claws.
//
// Four to eight legs on ONE component and one rig convention. Nothing here counts legs: `WalkerRig`
// discovers however many the armature has, `LeggedLocomotion` measures each of them and takes its
// cycle distance from the shortest, and `CrabWaveGait` derives its swing count and its minimum
// planted count from the number it is handed at `Bind`. Rebuilding the model with two more legs
// re-tunes the machine with nothing to edit, which is the point of deriving rather than authoring.
//
// What makes this one a crab rather than a walking station:
//
//  1. IT TRAVELS ACROSS ITS OWN NOSE. The legs radiate fore and aft on long coxae, so each foot
//     sweeps its yaw arc along the machine's X -- and X is the direction it covers ground in. A
//     station's legs stick out to the sides and sweep along Z, which is why the same stride model
//     serves both and the same gait pattern cannot.
//  2. THE WAVE RUNS WITH THE TRAVEL. `RippleGait` orders legs rear to front within each side; this
//     machine's sequence marches along X with the fore and aft rows in antiphase. See CrabWaveGait.
//  3. THE CARAPACE HUGS THE GROUND. Nothing rides on a crab, so the shell follows nearly all of the
//     slope under it and tilts far further than a crewed deck may -- the opposite tuning to the
//     crawler's, and it is what keeps the downhill legs inside their reach on a cross-slope.
//  4. THE FOOT IS A PAD. `FlatSole`. Pointed dactyls that pierce rather than pad would want their
//     own style; starting there would be tuning a thing before knowing it walks.
//
// Everything else -- the frame order, the gait clock, foothold resolution, ground probing, the
// support-plane survey, gravity, the IK -- is `LeggedLocomotion` and is not repeated here.
using SpaceGame.Locomotion;
using UnityEngine;

public class CrabLocomotion : LeggedLocomotion
{
    [Header("Crab — gait")]
    [Tooltip("Legs allowed in the air at once. 0 derives it from the leg count, which is what lets " +
             "one component cover four to eight legs: a swing count authored for six leaves an " +
             "eight-legged crab creeping and a four-legged one standing on two feet.")]
    [Range(0, 4)]
    [SerializeField] private int swingLegs;

    [Tooltip("Feet a leg forced to step early must leave behind. -1 derives it as 'everything the " +
             "wave is not already lifting', so the adaptive rule can never take the machine below " +
             "what the clock already guarantees.")]
    [Range(-1, 7)]
    [SerializeField] private int minPlantedLegs = -1;

    [Header("Crab — carapace")]
    [Tooltip("How much of the ground's tilt the shell takes on.\n\nHigh, unlike the crawler's deck: " +
             "nothing is standing on a crab to be tipped off, and a shell held flat over a slope " +
             "puts every hip at one height while the feet are at several, which strands the " +
             "downhill legs. Lying along the hillside is what a crab does and it is also what keeps " +
             "the legs inside their reach.")]
    [Range(0f, 1f)]
    [SerializeField] private float slopeFollow = 0.92f;

    [Tooltip("Hard cap on the shell's tilt. Generous for the same reason: the legs take whatever " +
             "the cap refuses, and on a low splayed machine that is reach it does not have.")]
    [Range(0f, 60f)]
    [SerializeField] private float maxShellTilt = 35f;

    [Tooltip("How far out from the hip a fresh foothold may be placed, as a fraction of the leg's " +
             "reach.\n\nSerialized rather than a constant because it is the one number that trades " +
             "stride against margin, and where it wants to sit depends on the variant: the hull " +
             "creeps a long way while a foothold is committed, so a step arrives further out than " +
             "it was aimed. Measured on the six-legged rig at 0.85 the worst reach was 1.14 in one " +
             "direction of travel and 1.21 along the nose — a foot detached from its own leg.")]
    [Range(0.5f, 0.95f)]
    [SerializeField] private float footholdReach = 0.78f;

    [Header("Crab — claws")]
    [Tooltip("Idle sway of each claw, as a fraction of the arm's own reach. Driven from the gait " +
             "clock rather than a timer of its own, so it cannot drift out of phase with the walk.")]
    [Range(0f, 0.4f)]
    [SerializeField] private float clawSway = 0.12f;

    [Tooltip("Where a raised claw sits, as a fraction of what the arm can span, measured from the " +
             "shoulder along up-and-inboard. A fraction of the LINKAGE rather than a displacement " +
             "added to the rest pose: the rest pose is already most of the way out, so anything " +
             "added to it asks the arm for somewhere it cannot reach.")]
    [Range(0.2f, 0.95f)]
    [SerializeField] private float clawRaise = 0.72f;

    [Tooltip("Seconds the raise takes to come up or go back down.")]
    [SerializeField] private float clawRaiseTime = 0.35f;

    private CrabClawMotion claws;

    protected override IStrideModel CreateStride() => new YawArcStride(YawRange);
    protected override IGaitPattern CreateGait() => new CrabWaveGait(swingLegs, minPlantedLegs);
    protected override IBodyMotion CreateBody() => new LevelDeckBody(slopeFollow, maxShellTilt);
    protected override IFootStyle CreateFeet() => new FlatSole();

    protected override float FootholdReachFraction => footholdReach;

    /// A claw is not a leg and must not be given a leg's travel. The shoulder swings much further
    /// across the body than a coxa ever does, and the elbow folds the claw back over the shell.
    protected override WalkerLimbSolver.Limits BuildArmLimits(in WalkerLimbGeometry g)
    {
        WalkerLimbSolver.Limits limits = base.BuildArmLimits(g);
        limits.Yaw = 80f;
        for (int i = 0; i < limits.Pitch.Length; i++) limits.Pitch[i] = 80f;
        return limits;
    }

    /// True while the claws are up. Read by whatever wants to know the machine is posturing.
    public bool ClawsRaised => claws != null && claws.Raise > 0.5f;

    /// Ask the claws up (or back down). A defensive posture, not a manipulator: there is no target
    /// and nothing to grasp.
    public void RaiseClaws(bool raised)
    {
        EnsureClaws();
        claws.Wanted = raised ? 1f : 0f;
    }

    /// One frame of claw motion, after the body and the legs have been posed.
    ///
    /// Public and dt-driven for exactly the reason `Step` is: a test marches it with no player loop.
    /// `CrabClaws` calls it at execution order 150, after this component's own LateUpdate at 100 --
    /// the arms hang off the body, so posing them before the body has moved reads as the claws
    /// trailing the shell, which is the same fault the frame order exists to prevent for the legs.
    public void PoseClaws(float deltaTime)
    {
        if (!IsReady || ArmCount == 0) return;
        EnsureClaws();
        claws.Pose(Arms, Body, LastFrame.Phase, Effort, Mathf.Max(deltaTime, 1e-5f));
        SolveArms();
    }

    private void EnsureClaws()
    {
        claws ??= new CrabClawMotion(clawSway, clawRaise, clawRaiseTime);
    }
}
