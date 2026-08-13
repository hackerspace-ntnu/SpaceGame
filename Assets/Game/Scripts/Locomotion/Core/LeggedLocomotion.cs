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
using System.Collections.Generic;
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

        [Header("Climb")]
        [Tooltip("Steepest sustained grade the machine will walk UP, in degrees. Kept below the " +
                 "NavMesh's own bake-time slope limit on purpose: an AI routed over ground its " +
                 "legs then refuse grinds against a hill the path says is fine.\n\nDownhill is " +
                 "never limited, and turning is never limited, so a machine stopped by this can " +
                 "always back off or turn away.")]
        [Range(0f, 89f)]
        [SerializeField] private float maxClimbAngle = 35f;
        [Tooltip("Band below the limit over which travel falls off to nothing. Without it the " +
                 "machine meets a hillside as if it were glass; with it, it slows into it.")]
        [Range(0f, 20f)]
        [SerializeField] private float climbTaperAngle = 5f;
        [Tooltip("How far ahead the grade is measured, as a multiple of leg reach. Long enough " +
                 "that a boulder does not read as a cliff, short enough that the answer is about " +
                 "the ground the machine is next putting feet on.")]
        [Range(0.5f, 6f)]
        [SerializeField] private float climbProbeRun = 2f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        /// The commanded twist's linear half, as a PLANAR VELOCITY IN BODY SPACE: x to the
        /// machine's right, y along its nose. Not a scalar, because travel is not always along the
        /// nose -- a crab goes sideways with its body square to the direction of motion, and a
        /// scalar plus a heading cannot say that.
        private Vector2 commandedVelocity;

        /// What the machine is actually being carried by this frame: the command above with the
        /// climb gate applied. EVERYTHING downstream reads this rather than the raw request.
        ///
        /// The two are separate fields because a single decision has to reach three readers that
        /// must agree -- the path integrates travel, the gait clock is advanced by it, and the
        /// foothold clamp aims a swing's worth of it ahead. Scaling one and not the others leaves a
        /// clock turning while the body stands still, so the feet drift against travel that never
        /// happens. That is a thrash, not a stall.
        private Vector2 travelVelocity;

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

        /// Joint travel for one ARM. Split from `BuildLimits` because a shoulder and a hip have
        /// nothing in common but a name: a hip that could reach where a shoulder reaches would
        /// fold the machine up. Defaults to the leg's ranges so a rig that has never thought about
        /// it still solves.
        protected virtual WalkerLimbSolver.Limits BuildArmLimits(in WalkerLimbGeometry g)
            => BuildLimits(g);

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

        /// Limbs that are NOT walked on. Empty on every machine whose rig has no `Arm_` root, which
        /// is both of the ones shipping.
        public int ArmCount => arms.Count;

        /// The arms, for whatever drives them. Exposed rather than driven here on purpose: the base
        /// owns the gait and the body, and an arm's target is neither -- it comes from the robot's
        /// own component, which calls `SolveArms()` once it has written the targets.
        public IReadOnlyList<WalkerArm> Arms => arms;

        /// Smoothed world velocity, as achieved rather than as commanded.
        public Vector3 MeasuredVelocity => velocity;

        /// True while nothing is carrying the machine and gravity owns its height.
        public bool IsFalling { get; private set; }

        /// True on any frame the ground ahead was too steep to walk up and travel was cut short
        /// because of it. What a driver watches to know it should be going another way.
        ///
        /// Only ever true while something is ASKING the machine to move. A machine standing at the
        /// foot of a cliff with no orders is not blocked, it is parked.
        public bool ClimbBlocked { get; private set; }

        /// How much of the last commanded travel survived the climb gate, 0..1.
        public float ClimbScale { get; private set; } = 1f;

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

        /// What the driver calls each frame, for a machine that travels along its nose. Speed is
        /// world units/second forward, yaw rate is degrees/second.
        public void SetTwist(float speed, float yawRate)
            => SetTwist(new Vector2(0f, speed), yawRate);

        /// The full twist: a planar velocity in the BODY'S OWN frame -- x to the right, y along the
        /// nose -- plus a yaw rate in degrees/second. Both are clamped to what the legs can deliver.
        ///
        /// The lateral channel exists because "how fast" and "which way" are two different
        /// questions and only one of them is the heading. A crab holds its body square to the
        /// direction it is going; a mech sidesteps without turning. Expressing that as a speed down
        /// the nose is not possible, and expressing it by yawing the body first is a different
        /// machine.
        ///
        /// Invariant I1: this clamps, it never zeroes, and it clamps the velocity's MAGNITUDE so
        /// the direction survives. The clock advances by distance travelled, so a lateral channel
        /// clamped independently to 0 would stop the gait dead on a purely sideways command, no
        /// phase slice would ever reopen, and nothing could un-stick the machine. That latch cost a
        /// session once already, from the other end.
        public void SetTwist(Vector2 velocityLocal, float yawRate)
        {
            float max = MaxSpeed;
            float sq = velocityLocal.sqrMagnitude;
            // Scaled rather than component-clamped: clamping x and y separately turns a diagonal
            // command into a different direction, and a box clamp lets a machine travel faster on
            // the diagonal than its legs can carry it.
            if (sq > max * max && sq > 1e-12f) velocityLocal *= max / Mathf.Sqrt(sq);

            commandedVelocity = velocityLocal;

            // Travel starts out as the whole command, and the climb gate takes from it at the top
            // of the next frame. Seeded here rather than left at zero so the machine's state
            // between an order and the frame that acts on it is "about to travel at this", not
            // "stopped" -- a reader that catches it in that window, a test included, should see the
            // order it just gave rather than a gate verdict that has not been reached yet.
            travelVelocity = velocityLocal;

            commandedYawRate = Mathf.Clamp(yawRate, -MaxYawRate, MaxYawRate);
        }

        // ─────────── quantities the whole machine is paced by ───────────

        /// Ground speed of the hardest-worked foot. A pivot moves the outer legs even when the
        /// body's centre is still, so cadence has to be paced by this rather than by speed alone.
        ///
        /// Taken from the velocity's MAGNITUDE, so a machine crabbing sideways is paced exactly as
        /// one walking forwards at the same ground speed. Reading the forward component alone would
        /// leave a purely lateral command with a pace of zero, which stops the clock (I1).
        protected float Pace =>
            travelVelocity.magnitude + Mathf.Abs(commandedYawRate) * Mathf.Deg2Rad * maxFootRadius;

        /// How far into "running" the current speed is, 0..1. Drives duty, pitch and bob together
        /// so the whole machine changes character at once instead of in pieces.
        protected float RunBlend => MaxSpeed <= 1e-4f
            ? 0f
            : Mathf.Clamp01(travelVelocity.magnitude / MaxSpeed);

        protected float CurrentDuty => gaitPattern.Duty(RunBlend);

        /// Fraction of top speed actually being worked at. Scales the bob and the lean, so a
        /// standing machine is still rather than idling up and down on the spot.
        protected float Effort => Mathf.Clamp01(Pace / Mathf.Max(MaxSpeed, 1e-4f));

        /// The planar velocity the machine is travelling at in body space: x right, y forward.
        /// Post-gate, so a machine refused by a hillside leans and bobs like the standing machine
        /// it is rather than like one walking into a wall.
        protected Vector2 CommandedVelocity => travelVelocity;

        /// The forward component only. Kept for the body motions, which lean and pitch against the
        /// nose; anything that cares how fast the machine is actually going wants `Pace` or
        /// `CommandedVelocity.magnitude` instead, both of which count sideways travel.
        protected float CommandedSpeed => travelVelocity.y;
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
