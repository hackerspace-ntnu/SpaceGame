// Locomotion for the robot ostrich: the component, its tunables, and the order of a frame.
//
// The leg IK is not reimplemented here. WalkerRig measures the rig and WalkerLegSolver solves one
// leg, and both are leg-count agnostic, so this component is only the parts a biped does
// differently from the six-legged station:
//
//  1. STRIDE COMES FROM THE HIP, NOT THE COXA. SpiderWalkerLocomotion sizes its stride from the
//     coxa's yaw arc, which works because the station's legs stick out sideways. An ostrich's foot
//     sits almost directly under its hip -- the rest foot radius is a few centimetres against a
//     metre of leg -- so a yaw arc would give it a stride of nearly nothing. The stride is the
//     chord the foot sweeps when the HIP PITCHES, and the coxa is left to do the job it is
//     actually needed for: holding a planted foot still while the body turns.
//  2. THERE IS NO STATIC STABILITY TO PRESERVE. The station keeps three feet down and is safe at
//     any moment you stop the clock. A biped is never statically stable, spends half its cycle on
//     one foot and, at speed, has moments with no feet down at all. So there is no minimum-planted
//     rule here; a leg swings when its slice opens and that is all.
//  3. THE BODY IS NOT HELD LEVEL. The station levels its deck because people stand on it. An
//     ostrich's body bobs, sways over the stance foot and pitches toward horizontal as it speeds
//     up, and that motion is most of what reads as an ostrich rather than a table walking.
//  4. THE FOOT IS A JOINT, NOT A PAD. The station sets its soles flat and leaves them there. A
//     bird rolls onto its toe to push off, carries the foot toed-up through the swing, and reaches
//     toe-first for the landing.
//
// Balance is procedural, not physical: the body follows curves driven by the gait clock rather
// than solving a real centre-of-mass problem. A real balance solve fights the rider for control
// and falls over when it loses; this reads correct and stays steerable.
//
// This is a kinematic layer. OstrichDriver hands it a twist; nothing else writes the body's
// transform.
//
// ─────────── where everything lives ───────────
//
// The class is split across four more files, by the question each one answers. They are partials
// of one component, so the serialized fields below are the ONLY ones -- keeping them all here is
// deliberate, since scattering them across partials leaves their inspector order up to the
// compiler and their [Header] groups interleaved.
//
//   .Rig.cs   what the machine IS       -- discovery, measurement, stride and cadence, LegState
//   .Gait.cs  WHEN and WHERE a foot goes -- the clock, swing arcs, footholds, load transfer
//   .Body.cs  where the BODY goes        -- height, bob, lean, attitude, gravity
//   .Ik.cs    posing the legs onto it    -- the solver call, foot articulation, gizmos
using UnityEngine;

[DefaultExecutionOrder(100)]
public partial class OstrichLocomotion : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("Armature holding the Coxa_*/Hip_*/Knee_*/Ankle_* bones. Auto-found if empty.")]
    [SerializeField] private Transform armatureRoot;
    [Tooltip("Transform that gets moved, yawed and bobbed. Defaults to this object.")]
    [SerializeField] private Transform body;

    [Header("Joint travel (degrees either side of rest)")]
    [Tooltip("Coxa azimuth. This is NOT what sets the stride on a biped -- it is what lets a " +
             "planted foot stay planted while the body turns, so it only needs to cover a turn.")]
    [SerializeField] private float yawRange = 45f;
    [Tooltip("Hip pitch. THIS is the stride: the foot sweeps fore and aft on it, so widening it " +
             "lengthens the step and raises top speed.")]
    [SerializeField] private float hipRange = 55f;
    [Tooltip("Generous: the hock is the joint that folds up to clear the ground, and a knee that " +
             "runs onto its stop reports the whole leg unreachable even when the foot is fine.")]
    [SerializeField] private float kneeRange = 90f;
    [SerializeField] private float ankleRange = 60f;
    [Tooltip("Sole roll, for meeting slopes the bird is walking ACROSS.")]
    [SerializeField] private float rollRange = 25f;

    [Header("Gait")]
    [Tooltip("Fraction of the cycle a foot is airborne at a walk. Below 0.5 both feet are down " +
             "for part of the cycle, which is what makes it a walk.")]
    [Range(0.30f, 0.49f)]
    [SerializeField] private float walkSwingDuty = 0.44f;
    [Tooltip("Same at a run. Above 0.5 the swings overlap and the bird is briefly airborne with " +
             "no feet down -- the flight phase that makes a run read as a run.")]
    [Range(0.51f, 0.75f)]
    [SerializeField] private float runSwingDuty = 0.62f;
    [Tooltip("Seconds one foot spends in the air, at full speed.")]
    [SerializeField] private float stepDuration = 0.30f;
    [Tooltip("Foot lift on level ground, as a fraction of leg reach. Ostriches pick their feet " +
             "up high, so this is generous compared to the walking station.")]
    [SerializeField] private float stepClearance = 0.16f;
    [Tooltip("Extra height held over the tallest obstacle found under a swing. Kept small: every " +
             "centimetre of arc is reach spent going up rather than forward, and a leg that runs " +
             "out of reach mid-swing visibly straightens.")]
    [SerializeField] private float obstacleClearance = 0.12f;

    [Header("Foot")]
    [Tooltip("Degrees the sole rolls onto its toe at the ends of a stance: heel up to push off, " +
             "toe down to reach for the landing, flat through the middle where the weight is.")]
    [SerializeField] private float toeOffAngle = 18f;
    [Tooltip("Degrees the toes lift through mid-swing, to clear the ground the foot is crossing. " +
             "Too much and the bird high-steps like a dressage horse.")]
    [SerializeField] private float swingToeAngle = 12f;

    [Header("Body motion")]
    [Tooltip("Ride height above the planted feet. Measured from the rest pose on Awake.")]
    [SerializeField] private bool autoCalibrateRideHeight = true;
    [SerializeField] private float rideHeight;
    [Tooltip("Hip height while walking, as a fraction of its height in the model's rest pose.\n\n" +
             "This is the single most important number here. The legs are modelled almost " +
             "straight, which leaves no length spare to swing fore and aft with -- a straight leg " +
             "can only reach the ground directly beneath it. Standing the bird down slightly bends " +
             "the hock and buys back the reach the stride is made of. Lower is a longer stride and " +
             "a more crouched, sprinting look; 1 stands it up and it can barely step at all.")]
    [Range(0.70f, 1.0f)]
    [SerializeField] private float hipHeightFraction = 0.86f;
    [Tooltip("Vertical bob, as a fraction of ride height. Runs at twice the stride frequency: " +
             "the body dips onto each footfall and rises through mid-stance.")]
    [SerializeField] private float bobAmount = 0.055f;
    [Tooltip("How far the body leans over the foot that is carrying it, as a fraction of the " +
             "stance width. Small, but it is what stops the walk reading as a shopping trolley.")]
    [SerializeField] private float swayAmount = 0.55f;
    [Tooltip("Degrees the body pitches toward horizontal at top speed.")]
    [SerializeField] private float runPitch = 16f;
    [Tooltip("Degrees the body rolls into a turn at top speed.")]
    [SerializeField] private float turnRoll = 8f;
    [SerializeField] private float heightSmooth = 12f;
    [SerializeField] private float attitudeSmooth = 8f;

    [Header("Steering")]
    [Tooltip("Fastest the bird may turn. A biped pivots about its own feet, so this is authored " +
             "rather than derived -- there is no long outboard leg to be dragged.")]
    [SerializeField] private float maxYawRate = 120f;

    [Header("Gravity")]
    [Tooltip("Downward acceleration applied when nothing is carrying the bird. Authored rather " +
             "than taken from Physics.gravity: this is a kinematic machine and the number is a " +
             "look, not a simulation.")]
    [SerializeField] private float fallGravity = 20f;
    [SerializeField] private float maxFallSpeed = 40f;
    [Tooltip("How far above the ground beneath it a planted foot may sit and still count as " +
             "holding the bird up. Anything higher is a foot stranded in the air -- which is what " +
             "a bird spawned before its terrain has is standing on.")]
    [SerializeField] private float footGroundTolerance = 0.25f;
    [Tooltip("How far above its resting height the body must be before gravity takes over, as a " +
             "fraction of leg reach.\n\nThis is what keeps a run's flight phase from being read " +
             "as a fall: for the fraction of a second both feet are off the ground the body is " +
             "still at ride height, nowhere near this. It also has to clear the tallest thing a " +
             "leg can step up onto, or the bird falls off its own footholds.")]
    [Range(0.1f, 2f)]
    [SerializeField] private float fallThresholdFraction = 0.5f;

    [Header("Ground")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float rayStartAbove = 3f;
    [SerializeField] private float rayLength = 60f;
    [SerializeField] private bool snapToGroundOnStart = true;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private float commandedSpeed;
    private float commandedYawRate;
    private bool ready;
    private Diagnostics diagnostics;

    // ─────────── what the driver sees ───────────

    public bool IsReady => ready;
    public int LegCount => legs.Count;
    public Vector3 MeasuredVelocity => velocity;

    /// True while nothing is carrying the bird and gravity owns its height.
    public bool IsFalling { get; private set; }

    /// Height the body rides above its planted feet, measured from the rest pose.
    public float RideHeight => rideHeight;

    /// Fastest the legs can carry the body, at the run duty. Derived from stride and cadence.
    public float MaxSpeed { get; private set; }
    public float MaxYawRate => maxYawRate;

    public Diagnostics LastFrame => diagnostics;

    public struct Diagnostics
    {
        public float AchievedSpeed;
        public int StanceLegs;
        public int SwingingLegs;
        public int UnreachableLegs;
        public float WorstReachFraction;
        public float Phase;
        public float Duty;
        /// True while no foot is on the ground. Expected at a run, a bug at a walk.
        public bool Airborne;
    }

    public bool TryGetFoot(int index, out Vector3 foot, out bool swinging)
    {
        if (index < 0 || index >= legs.Count) { foot = default; swinging = false; return false; }
        foot = legs[index].Foot;
        swinging = legs[index].Swinging;
        return true;
    }

    /// What the driver calls each frame. Speed is world units/second along the body's forward,
    /// yaw rate is degrees/second; both are clamped to what the legs can deliver.
    public void SetTwist(float speed, float yawRate)
    {
        commandedSpeed = Mathf.Clamp(speed, -MaxSpeed, MaxSpeed);
        commandedYawRate = Mathf.Clamp(yawRate, -maxYawRate, maxYawRate);
    }

    // ─────────── quantities the whole machine is paced by ───────────

    /// Ground speed of the hardest-worked foot, which is what cadence has to be paced by.
    private float Pace =>
        Mathf.Abs(commandedSpeed) + Mathf.Abs(commandedYawRate) * Mathf.Deg2Rad * maxFootRadius;

    /// How far into "running" the current speed is, 0..1. Drives duty, pitch and bob together so
    /// the whole machine changes character at once instead of in pieces.
    private float RunBlend => MaxSpeed <= 1e-4f
        ? 0f
        : Mathf.Clamp01(Mathf.Abs(commandedSpeed) / MaxSpeed);

    private float CurrentDuty => Mathf.Lerp(walkSwingDuty, runSwingDuty, RunBlend);

    /// Fraction of top speed the bird is actually working at. Scales the bob and the lean, so a
    /// standing bird is still rather than idling up and down on the spot.
    private float Effort => Mathf.Clamp01(Pace / Mathf.Max(MaxSpeed, 1e-4f));

    // ─────────── the frame ───────────

    private void Awake() => Initialise();

    private void Start()
    {
        if (snapToGroundOnStart) SnapToGround();
    }

    private void LateUpdate() => Step(Time.deltaTime);

    /// One frame of locomotion. Public and dt-driven so a test can march it forward without a
    /// running player loop; nothing in here reads Time directly.
    ///
    /// The body is posed BEFORE the gait picks its footholds, and the order is the whole point.
    /// The gait aims every step from the hips and from where the rest footholds have got to, both
    /// of which are read off the body's transform. Posing afterwards left both a frame out of date,
    /// so every foothold was committed to where the bird had been rather than where it is -- ten
    /// centimetres at a run, and a whole frame of yaw through a turn. It reads exactly as what it
    /// is: the feet trailing the body.
    public void Step(float deltaTime)
    {
        if (!ready) return;
        float dt = Mathf.Max(deltaTime, 1e-5f);

        MoveBody(dt);
        PoseBody(dt);
        UpdateGait(dt);
        SolveLegs();

        TrackVelocity(dt);
        diagnostics.Phase = gait.Phase;
        diagnostics.Duty = CurrentDuty;
    }
}
