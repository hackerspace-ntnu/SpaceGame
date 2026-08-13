// Neck, head and tail motion for the robot horse.
//
// `HorseLocomotion` deliberately bobs, sways and rolls the body. This component spends the neck
// against that at the head, adds the head nod a horse locks to its own footfalls, and lets the tail
// trail behind whatever the body just did. It is display only: nothing here feeds back into where
// the hooves go or how fast the machine travels, so it can be tuned, disabled or replaced with no
// risk to the gait.
//
// Runs at 150, after the locomotion at 100, so the body has already been posed for this frame.
//
// ─────────── the one thing that was got wrong on the ostrich and is not repeated here ───────────
//
// THE BOUNCE IS DRIVEN BY THE BODY TRANSFORM, NOT BY `MeasuredVelocity`. `MeasuredVelocity` is the
// PATH velocity, and the path deliberately excludes the bob and the sway -- those are display
// offsets added on top of it and never written back. But the bob is exactly the motion a rider
// feels, so a bounce driven off the path velocity is driven by the one part of the machine's motion
// that has had the ride taken out of it. On the ostrich that measured 5.2 m/s^2 against the
// transform's 11.6, and produced 1.27 degrees of bend: invisible.
//
// Differentiating a transform twice is noisy, which is why the drive goes into a SPRING rather than
// through a smoothing filter -- see HorseRideSpring for why that distinction is the legal one.
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Creatures.Horse
{
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(HorseLocomotion))]
    public class HorseSpineMotion : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Neck bones, base to poll. Auto-found by name (Neck_01, Neck_02, ...) if empty.")]
        [SerializeField] private Transform[] neckBones;
        [Tooltip("Head bone. Auto-found by name if empty.")]
        [SerializeField] private Transform head;
        [Tooltip("Tail bones, root to tip. Auto-found by name (Tail_01, ...) if empty.")]
        [SerializeField] private Transform[] tailBones;

        [Header("Head nod")]
        [Tooltip("Degrees the head nods fore and aft. Driven off the gait clock rather than a timer of " +
                 "its own, so it stays in step with the hooves at any speed and cannot drift out of " +
                 "phase with them.")]
        [SerializeField] private float nodAngle = 6f;
        [Tooltip("Nods per stride cycle. A horse nods once per cycle at a walk and a canter; at a trot " +
                 "the diagonals cancel and the head goes quiet, which is what the run blend does here.")]
        [SerializeField] private float nodCyclesPerStride = 1f;

        [Header("Counter-swing")]
        [Tooltip("Degrees the neck leans into a turn. Taken from how fast the body is ACTUALLY " +
                 "turning rather than from a commanded rate, so it still works when something other " +
                 "than the driver is turning the machine.")]
        [SerializeField] private float turnCounterSwing = 9f;

        [Header("Ride bounce")]
        [SerializeField] private bool enableBounce = true;
        [SerializeField] private HorseRideSpringSettings bounce = new HorseRideSpringSettings
        {
            stiffness = 38f, damping = 0.4f, gain = 26f, maxAngle = 12f, maxDrive = 70f,
        };
        [Tooltip("Body movement in one frame, in metres, past which this is treated as a teleport " +
                 "rather than a stride and the spring is restarted from rest.")]
        [SerializeField] private float teleportDistance = 2f;

        [Header("Bend distribution")]
        [Tooltip("How the bend is shared along the neck. 0 spreads it evenly; higher values move it " +
                 "toward the base, so the whole neck swings rather than the head alone wobbling.")]
        [Range(0f, 3f)]
        [SerializeField] private float bendBaseBias = 1.0f;

        [Header("Tail")]
        [Tooltip("Degrees the tail swings against the body's turn. The tail is a counterweight, so it " +
                 "lags rather than leads.")]
        [SerializeField] private float tailSwing = 14f;
        [Tooltip("Degrees the tail lifts at top speed. A horse carries its tail up when it is moving.")]
        [SerializeField] private float tailLift = 18f;
        [Tooltip("How quickly the tail follows. This one IS allowed a filter: the tail is a passive " +
                 "mass with no phase relationship to the footfalls to preserve.")]
        [SerializeField] private float tailSmooth = 5f;

        private HorseLocomotion locomotion;
        private Transform body;

        private HorseRideSpring spring;
        private Quaternion[] neckRest;
        private Quaternion[] tailRest;
        private Quaternion headRest;
        private float[] bendWeights;

        private Vector3 lastBodyPos;
        private Vector3 lastVelocity;
        private bool havePose;
        private float lastYaw;
        private float smoothedSwing;
        private float smoothedTailYaw;
        private float smoothedTailLift;

        /// The bounce's current bend, in degrees: x fore/aft, y side to side. Read-only, for tests and
        /// debug overlays.
        public Vector2 BounceAngles => spring != null ? spring.Value : Vector2.zero;

        /// The body acceleration the bounce is being driven by, in body axes. Exposed so a test can
        /// prove it is the transform's and not the path's.
        public Vector3 LastDrive { get; private set; }

        private void Awake()
        {
            locomotion = GetComponent<HorseLocomotion>();
            body = transform;
            Resolve();
        }

        /// Finds the chains by name if they were not assigned, and caches the rest pose. Public so a
        /// test or an editor tool can rebuild the cache after re-importing the model.
        public void Resolve()
        {
            // Pick the component wiring up here as well as in Awake: EditMode never fires Awake, and
            // LeggedLocomotion already splits Initialise() out for exactly that reason.
            if (locomotion == null) locomotion = GetComponent<HorseLocomotion>();
            if (body == null) body = transform;

            neckBones = Rediscover(neckBones, "Neck_");
            tailBones = Rediscover(tailBones, "Tail_");

            if (head == null)
            {
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                    if (t.name == "Head") { head = t; break; }
            }

            neckRest = Capture(neckBones);
            tailRest = Capture(tailBones);
            if (head != null) headRest = head.localRotation;

            // Weights sum to 1 so the whole chain delivers exactly the commanded angle however many
            // joints the rig turns out to have. Re-exporting the neck with a different vertebra count
            // therefore changes how the bend is shared and not how far the head moves.
            bendWeights = new float[neckBones.Length];
            float total = 0f;
            for (int i = 0; i < neckBones.Length; i++)
            {
                float t = neckBones.Length > 1 ? i / (float)(neckBones.Length - 1) : 0f;
                bendWeights[i] = Mathf.Pow(1f - t, bendBaseBias) + 0.15f;
                total += bendWeights[i];
            }
            for (int i = 0; i < bendWeights.Length; i++) bendWeights[i] /= Mathf.Max(total, 1e-4f);

            spring = new HorseRideSpring(bounce);
            havePose = false;
        }

        /// A serialized bone cache cannot be trusted when it is absent, empty, or holding a reference
        /// that no longer points at anything -- re-exporting with a different bone count leaves the
        /// array at its old length with every entry dangling, and a length check sails past that and
        /// then throws on the first `localRotation`.
        private Transform[] Rediscover(Transform[] cached, string prefix)
        {
            bool ok = cached != null && cached.Length > 0;
            if (ok)
            {
                foreach (Transform t in cached)
                    if (t == null) { ok = false; break; }
            }
            if (ok) return cached;

            var found = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(prefix)) found.Add(t);
            // Ordinal sort puts Neck_01..Neck_05 in chain order because the names are zero-padded.
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found.ToArray();
        }

        private static Quaternion[] Capture(Transform[] bones)
        {
            var rest = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) rest[i] = bones[i].localRotation;
            return rest;
        }

        private void LateUpdate() => Step(Time.deltaTime);

        /// One frame of spine motion. dt-driven so it can be marched from a test.
        public void Step(float deltaTime)
        {
            if (neckBones == null || neckBones.Length == 0) return;
            float dt = Mathf.Max(deltaTime, 1e-5f);

            // Reset to rest first, so each frame's pose is built against a known one rather than
            // accumulating on top of the last -- which is a corkscrew within a few seconds.
            for (int i = 0; i < neckBones.Length; i++) neckBones[i].localRotation = neckRest[i];
            for (int i = 0; i < tailBones.Length; i++) tailBones[i].localRotation = tailRest[i];
            if (head != null) head.localRotation = headRest;

            LeggedLocomotion.Diagnostics d = locomotion.LastFrame;
            float speed01 = Mathf.Clamp01(Mathf.Abs(d.AchievedSpeed) /
                                          Mathf.Max(locomotion.MaxSpeed, 1e-4f));

            Vector3 drive = MeasureBodyDrive(dt);
            LastDrive = drive;

            ApplyNod(d, speed01);
            ApplyCounterSwing(dt, speed01);
            if (enableBounce) ApplyBounce(dt, drive);
            ApplyTail(dt, speed01);
        }

        /// The body's own acceleration, resolved into its own axes.
        ///
        /// Taken off the TRANSFORM, which carries the bob and the sway, rather than off
        /// `MeasuredVelocity`, which is the path and deliberately does not. See the note at the top of
        /// this file: that choice is the difference between a visible bounce and none.
        private Vector3 MeasureBodyDrive(float dt)
        {
            Vector3 now = body.position;
            if (!havePose)
            {
                lastBodyPos = now;
                lastVelocity = Vector3.zero;
                havePose = true;
                return Vector3.zero;
            }

            Vector3 step = now - lastBodyPos;
            lastBodyPos = now;

            // A spawn snap, a scene migration or a streamed chunk arriving is not a stride. Restart
            // rather than differentiate across the seam, or the spring is slammed to its clamp.
            if (step.magnitude > teleportDistance)
            {
                lastVelocity = Vector3.zero;
                spring.Reset();
                return Vector3.zero;
            }

            Vector3 velocity = step / dt;
            Vector3 world = (velocity - lastVelocity) / dt;
            lastVelocity = velocity;

            return body.InverseTransformDirection(world);
        }

        /// The head nod, locked to the gait clock so it cannot drift out of step with the hooves.
        ///
        /// It fades out toward a trot, which is not a stylistic choice: at a trot the two diagonals
        /// land alternately half a cycle apart and their vertical impulses cancel at the withers, so a
        /// real horse's head goes still. It comes back at a canter, where they do not.
        private void ApplyNod(LeggedLocomotion.Diagnostics d, float speed01)
        {
            float trotQuiet = 1f - Mathf.Clamp01(1f - Mathf.Abs(speed01 - 0.5f) * 4f);
            float nod = Mathf.Sin(2f * Mathf.PI * nodCyclesPerStride * d.Phase)
                        * nodAngle * speed01 * trotQuiet;
            SpreadOverNeck(Quaternion.AngleAxis(nod, Vector3.right));
        }

        /// The neck leans into a turn, the way a horse uses it as a counterweight.
        private void ApplyCounterSwing(float dt, float speed01)
        {
            float yaw = body.eulerAngles.y;
            float measured = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastYaw = yaw;

            float rate = Mathf.Clamp(measured / Mathf.Max(locomotion.MaxYawRate, 1e-4f), -1f, 1f);
            float target = -turnCounterSwing * rate * speed01;
            smoothedSwing = Mathf.Lerp(smoothedSwing, target, 1f - Mathf.Exp(-6f * dt));

            SpreadOverNeck(Quaternion.AngleAxis(smoothedSwing, Vector3.forward));
        }

        /// The ride. The spring is the only thing in this component with memory of its own, and its lag
        /// is the effect rather than a cost.
        private void ApplyBounce(float dt, Vector3 drive)
        {
            spring.Step(dt, drive);
            Vector2 bend = spring.Value;
            SpreadOverNeck(Quaternion.AngleAxis(bend.x, Vector3.right) *
                           Quaternion.AngleAxis(bend.y, Vector3.forward));
        }

        /// The tail: lifts with speed, swings against the turn, and lags both. Nothing here runs at
        /// stride frequency, so unlike the neck it may legitimately be filtered.
        private void ApplyTail(float dt, float speed01)
        {
            if (tailBones.Length == 0) return;

            float yawTarget = -smoothedSwing * (tailSwing / Mathf.Max(turnCounterSwing, 1e-4f));
            float liftTarget = tailLift * speed01;
            float k = 1f - Mathf.Exp(-tailSmooth * dt);
            smoothedTailYaw = Mathf.Lerp(smoothedTailYaw, yawTarget, k);
            smoothedTailLift = Mathf.Lerp(smoothedTailLift, liftTarget, k);

            // Shared evenly down the tail, so it curves rather than hinging at the dock.
            float share = 1f / tailBones.Length;
            Quaternion per = Quaternion.AngleAxis(-smoothedTailLift * share, Vector3.right) *
                             Quaternion.AngleAxis(smoothedTailYaw * share, Vector3.up);
            for (int i = 0; i < tailBones.Length; i++)
                tailBones[i].localRotation = tailBones[i].localRotation * per;
        }

        /// Share one rotation along the neck joints, weighted toward the base, in each joint's own
        /// local space.
        private void SpreadOverNeck(Quaternion rotation)
        {
            for (int i = 0; i < neckBones.Length; i++)
            {
                Quaternion share = Quaternion.Slerp(Quaternion.identity, rotation, bendWeights[i]);
                neckBones[i].localRotation = neckBones[i].localRotation * share;
            }
        }
    }
}
