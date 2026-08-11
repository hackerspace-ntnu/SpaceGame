// One locomotion core, many robots.
//
// Two legged machines existed before this and neither shared its locomotion with the other, though
// both were built on the same library underneath. They had independently reimplemented the leg
// state, the discovery, the gait loop, the foothold search, the swing lift and the gizmos -- and
// the copies had DRIFTED, which is what made this worth doing rather than merely tidy. One had a
// foothold clamp that lifted feet off the ground; one posed its body a frame late; one had no
// gravity at all and hung in the sky when spawned above unloaded terrain.
//
// So: the base owns the order of a frame and every piece of arithmetic that is the same on every
// legged machine. What genuinely differs is four things, and they are interfaces -- how far a leg
// can step (IStrideModel), when each leg swings (IGaitPattern), where the body goes (IBodyMotion),
// what the foot does (IFootStyle). A robot is one small file that assembles four of them.
//
// This is a KINEMATIC layer, and invariant I4: it is the single owner of the body's transform. A
// driver hands it a twist and reads back achieved motion. Rigidbodies stay kinematic with gravity
// off.
//
// ─────────── where everything lives ───────────
//
// The class is split across four more files, by the question each one answers. They are partials of
// one component, so the serialized fields below are the ONLY ones -- keeping them all here is
// deliberate, since scattering them across partials leaves their inspector order up to the compiler
// and their [Header] groups interleaved.
//
//   .Rig.cs   what the machine IS        -- discovery, measurement, stride and cadence
//   .Gait.cs  WHEN and WHERE a foot goes -- the clock, swing arcs, footholds, load transfer
//   .Body.cs  where the BODY goes        -- height, gravity, falling, landing
//   .Ik.cs    posing the legs onto it    -- the solver call, foot articulation, gizmos
using UnityEngine;

namespace SpaceGame.Locomotion
{
    [DefaultExecutionOrder(100)]
    public abstract partial class LeggedLocomotion : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Armature holding the limb chains. Auto-found if empty.")]
        [SerializeField] private Transform armatureRoot;
        [Tooltip("Transform that gets moved, yawed and posed. Defaults to this object.")]
        [SerializeField] private Transform body;

        [Header("Joint travel (degrees either side of rest)")]
        [Tooltip("Coxa azimuth. On a splayed-leg machine this sets the stride; on a biped it is " +
                 "what lets a planted foot stay planted while the body turns.")]
        [SerializeField] private float yawRange = 40f;
        [Tooltip("First pitch joint, against the body.")]
        [SerializeField] private float hipRange = 45f;
        [Tooltip("Second pitch joint. Generous: this is the joint that folds up to clear the " +
                 "ground, and one that runs onto its stop reports the whole leg unreachable even " +
                 "when the foot is fine.")]
        [SerializeField] private float kneeRange = 60f;
        [Tooltip("Last pitch joint, and every joint past it on a longer limb.")]
        [SerializeField] private float ankleRange = 45f;
        [Tooltip("Sole roll, for meeting slopes the leg is walking ACROSS.")]
        [SerializeField] private float rollRange = 30f;

        [Header("Gait")]
        [Tooltip("Seconds one foot spends in the air, at full speed.")]
        [SerializeField] private float stepDuration = 0.45f;
        [Tooltip("Foot lift on level ground, as a fraction of leg reach. The swing also probes the " +
                 "ground it is crossing and lifts further to clear anything in the way.")]
        [SerializeField] private float stepClearance = 0.08f;
        [Tooltip("Extra height held over the tallest obstacle found under a swing. Every " +
                 "centimetre of arc is reach spent going up rather than forward.")]
        [SerializeField] private float obstacleClearance = 1.5f;

        [Header("Body")]
        [Tooltip("Measure ride height from the rest pose on Awake. The rig's origin often sits at " +
                 "foot level, so a hand-guessed value here will launch the machine on frame one.")]
        [SerializeField] private bool autoCalibrateRideHeight = true;
        [Tooltip("Ride height above the planted feet. Overwritten when auto-calibrating.")]
        [SerializeField] private float rideHeight;
        [Tooltip("Smoothing on the GROUND only. Nothing that moves at stride frequency may go " +
                 "through this: a filter costs amplitude and phase at the frequency it passes.")]
        [SerializeField] private float heightSmooth = 5f;

        [Header("Gravity")]
        [Tooltip("Downward acceleration applied when nothing is carrying the machine. Authored " +
                 "rather than taken from Physics.gravity: this is a kinematic machine and the " +
                 "number is a look, not a simulation.")]
        [SerializeField] private float fallGravity = 20f;
        [SerializeField] private float maxFallSpeed = 40f;
        [Tooltip("How far above the ground beneath it a planted foot may sit and still count as " +
                 "holding the machine up. Anything higher is a foot stranded in the air -- which " +
                 "is what a machine spawned before its terrain has under it.")]
        [SerializeField] private float footGroundTolerance = 0.25f;
        [Tooltip("How far above its resting height the body must be before gravity takes over, as " +
                 "a fraction of leg reach.\n\nThis is what keeps a run's flight phase from being " +
                 "read as a fall. It also has to clear the tallest thing a leg can step up onto, " +
                 "or the machine falls off its own footholds.")]
        [Range(0.1f, 2f)]
        [SerializeField] private float fallThresholdFraction = 0.5f;

        [Header("Ground")]
        [Tooltip("Layers treated as ground. The machine's OWN colliders are always ignored " +
                 "regardless of this mask, so leaving it as Everything is safe.")]
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float rayStartAbove = 3f;
        [SerializeField] private float rayLength = 60f;
        [Tooltip("On Start, drop the machine onto the ground so the legs begin within reach.")]
        [SerializeField] private bool snapToGroundOnStart = true;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        private float commandedSpeed;
        private float commandedYawRate;
        private bool ready;
        private Diagnostics diagnostics;

        // ─────────── what a robot's own file supplies ───────────

        protected abstract IStrideModel CreateStride();
        protected abstract IGaitPattern CreateGait();
        protected abstract IBodyMotion CreateBody();
        protected abstract IFootStyle CreateFeet();

        /// Yaw travel, for a stride model that is sized from the coxa's arc.
        protected float YawRange => yawRange;

        /// Joint travel for one limb. Override for a limb whose joints do not all share the four
        /// ranges above -- the base reuses `ankleRange` for everything past the third pitch joint.
        protected virtual WalkerLimbSolver.Limits BuildLimits(in WalkerLimbGeometry g)
        {
            var limits = new WalkerLimbSolver.Limits
            {
                Yaw = yawRange,
                Roll = rollRange,
                Pitch = new float[Mathf.Max(1, g.PitchCount)],
            };
            for (int i = 0; i < limits.Pitch.Length; i++)
            {
                limits.Pitch[i] = i == 0 ? hipRange
                                : i == 1 ? kneeRange
                                : ankleRange;
            }
            return limits;
        }

        /// Fastest the machine may turn.
        ///
        /// Derived by default: the outermost foot travels furthest per degree of turn, so it is
        /// what bounds a pivot. A biped pivots about its own feet and has no long outboard leg to
        /// be dragged, so it overrides this with an authored number.
        protected virtual float DeriveMaxYawRate()
            => maxFootRadius > 1e-3f ? MaxSpeed / maxFootRadius * Mathf.Rad2Deg : 0f;

        // ─────────── what the driver sees ───────────

        public bool IsReady => ready;
        public int LegCount => legs.Count;
        /// Smoothed world velocity, as achieved rather than as commanded.
        public Vector3 MeasuredVelocity => velocity;

        /// True while nothing is carrying the machine and gravity owns its height.
        public bool IsFalling { get; private set; }

        /// Height the body rides above its planted feet, measured from the rest pose.
        public float RideHeight => rideHeight;

        /// Fastest the legs can carry the body. Derived from stride and cadence, not authored:
        /// asking for more than this would require the feet to slide.
        public float MaxSpeed { get; private set; }
        public float MaxYawRate { get; private set; }

        public Diagnostics LastFrame => diagnostics;

        /// Last frame's gait state. A legged machine is hard to reason about from the outside; this
        /// is what tells you whether a stall is the speed clamp or the gait failing to step.
        public struct Diagnostics
        {
            public float AchievedSpeed;
            public int StanceLegs;
            public int SwingingLegs;
            /// Legs whose foot the linkage could not reach. Should be zero on reasonable ground.
            public int UnreachableLegs;
            /// Largest reach fraction across the legs. Above 1 means a foot is visibly detached.
            public float WorstReachFraction;
            public float Phase;
            public float Duty;
            /// True while no foot is on the ground. Expected at a run, a bug at a walk.
            public bool Airborne;
        }

        /// World contact point of leg `index`, and whether it is mid-swing. For debug overlays and
        /// tests that need to prove planted feet do not slide.
        public bool TryGetFoot(int index, out Vector3 foot, out bool swinging)
        {
            if (index < 0 || index >= legs.Count) { foot = default; swinging = false; return false; }
            foot = legs[index].Foot;
            swinging = legs[index].Swinging;
            return true;
        }

        /// What one leg was measured as. Per leg, not averaged: a machine with shorter front legs
        /// gets a stride that fits each pair rather than neither.
        public bool TryGetMeasurement(int index, out LegMeasurement measurement)
        {
            if (index < 0 || index >= legs.Count) { measurement = default; return false; }
            measurement = legs[index].Measure;
            return true;
        }

        /// What the driver calls each frame. Speed is world units/second along the body's forward,
        /// yaw rate is degrees/second; both are clamped to what the legs can deliver.
        ///
        /// Invariant I1: this clamps, it never zeroes. The clock advances by distance travelled, so
        /// forcing speed to 0 stops the gait, no phase slice ever reopens, and nothing can un-stick
        /// the machine. That latch cost a session once already.
        public void SetTwist(float speed, float yawRate)
        {
            commandedSpeed = Mathf.Clamp(speed, -MaxSpeed, MaxSpeed);
            commandedYawRate = Mathf.Clamp(yawRate, -MaxYawRate, MaxYawRate);
        }

        // ─────────── quantities the whole machine is paced by ───────────

        /// Ground speed of the hardest-worked foot. A pivot moves the outer legs even when the
        /// body's centre is still, so cadence has to be paced by this rather than by speed alone.
        protected float Pace =>
            Mathf.Abs(commandedSpeed) + Mathf.Abs(commandedYawRate) * Mathf.Deg2Rad * maxFootRadius;

        /// How far into "running" the current speed is, 0..1. Drives duty, pitch and bob together
        /// so the whole machine changes character at once instead of in pieces.
        protected float RunBlend => MaxSpeed <= 1e-4f
            ? 0f
            : Mathf.Clamp01(Mathf.Abs(commandedSpeed) / MaxSpeed);

        protected float CurrentDuty => gaitPattern.Duty(RunBlend);

        /// Fraction of top speed actually being worked at. Scales the bob and the lean, so a
        /// standing machine is still rather than idling up and down on the spot.
        protected float Effort => Mathf.Clamp01(Pace / Mathf.Max(MaxSpeed, 1e-4f));

        protected float CommandedSpeed => commandedSpeed;
        protected float CommandedYawRate => commandedYawRate;
        protected Transform Body => body;

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
        /// THE BODY IS POSED BEFORE THE GAIT PICKS ITS FOOTHOLDS, and the order is the whole point.
        /// The gait aims every step from the hips and from where the rest footholds have got to,
        /// both of which are read off the body's transform. Posing afterwards leaves both a frame
        /// out of date, so every foothold is committed to where the machine HAD BEEN rather than
        /// where it is -- ten centimetres at a run, and a whole frame of yaw through a turn. It
        /// reads exactly as what it is: the feet trailing the body.
        ///
        /// Reading a hip before SolveLegs is safe because the hip sits ON the coxa's yaw axis, so
        /// this frame's yaw has not moved it.
        public void Step(float deltaTime)
        {
            if (!ready) return;
            float dt = Mathf.Max(deltaTime, 1e-5f);

            AdvancePath(dt);
            PoseBody(dt);
            UpdateGait(dt);
            SolveLegs();

            TrackVelocity(dt);
            diagnostics.Phase = gait.Phase;
            diagnostics.Duty = CurrentDuty;
        }
    }
}
