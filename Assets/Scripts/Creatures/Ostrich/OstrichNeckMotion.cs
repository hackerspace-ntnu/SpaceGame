// Autonomous neck life: looking around, and bouncing while it is ridden.
//
// This LAYERS ON TOP of OstrichSpineMotion rather than replacing it. That component (order 150)
// already owns the gait-locked peck, the turn counter-swing, the look-toward-travel and the head
// stabilisation, and it is tuned; it resets the chain to rest at the top of its own frame and then
// applies its layers. Running afterwards at order 160 and only ADDING rotation means the two
// compose instead of fighting over the same bones, and this one can be switched off on its own.
//
// Nothing here is steerable. The bird looks where it wants to.
//
// The neck is 11 vertebrae now, not the 5 it used to be, so rotation is spread with a weight curve
// rather than shared out evenly -- an even share across 11 joints bends like a hose. Large sweeps
// are paid for near the base, fine gaze near the head.
using UnityEngine;

namespace SpaceGame.Creatures.Ostrich
{
    [DefaultExecutionOrder(160)]
    [RequireComponent(typeof(OstrichLocomotion))]
    public class OstrichNeckMotion : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Neck bones, base to head. Auto-found by name (Neck_01, Neck_02, ...) if empty.")]
        [SerializeField] private Transform[] neckBones;
        [Tooltip("Head bone. Auto-found by name if empty.")]
        [SerializeField] private Transform head;
        [Tooltip("Jaw bone, if the model has one. Auto-found by name; optional.")]
        [SerializeField] private Transform jaw;

        [Header("Looking around")]
        [SerializeField] private bool enableGaze = true;
        [SerializeField] private OstrichNeckGazeSettings gaze = new OstrichNeckGazeSettings
        {
            yawRange = 62f, pitchRange = 20f, minHold = 0.7f, maxHold = 2.6f, slewRate = 240f,
            startleChance = 0.15f, startleYaw = 120f, travelSuppression = 0.65f,
        };
        [Tooltip("Seed for the look-around pattern. Two birds with different seeds do not scan in " +
                 "lockstep, which is what stops a flock looking like a chorus line.")]
        [SerializeField] private int gazeSeed = 0;
        [Tooltip("Use the instance id as the seed instead, so every bird differs without hand-editing.")]
        [SerializeField] private bool seedFromInstance = true;

        [Header("Ride bounce")]
        [SerializeField] private bool enableBounce = true;
        [SerializeField] private OstrichNeckSpringSettings bounce = new OstrichNeckSpringSettings
        {
            stiffness = 42f, damping = 0.35f, gain = 32f, maxAngle = 14f, maxDrive = 70f,
        };
        [Tooltip("Body movement in one frame, in metres, past which this is treated as a teleport " +
                 "rather than a stride and the bounce is restarted from rest.")]
        [SerializeField] private float teleportDistance = 2f;

        [Header("Bend distribution")]
        [Tooltip("How gaze is shared along the neck. 0 spreads it evenly; higher values move the bend " +
                 "toward the head, which is where a bird actually aims from.")]
        [Range(0f, 3f)] [SerializeField] private float gazeHeadBias = 1.4f;
        [Tooltip("How the bounce is shared. Higher values move it toward the base, so the whole neck " +
                 "sways rather than the head alone wobbling.")]
        [Range(0f, 3f)] [SerializeField] private float bounceBaseBias = 1.1f;

        [Header("Jaw")]
        [SerializeField] private bool enableJaw = true;
        [Tooltip("How far the beak opens, in degrees.")]
        [SerializeField] private float jawOpenAngle = 16f;
        [Tooltip("Axis the jaw opens about, in its own local space. Negative X opens the beak on this " +
                 "model; Unity's FBX import can remap bone axes, so this is a field rather than a " +
                 "constant.")]
        [SerializeField] private Vector3 jawOpenAxis = new Vector3(-1f, 0f, 0f);
        [SerializeField] private float minJawInterval = 3.5f;
        [SerializeField] private float maxJawInterval = 11f;
        [Tooltip("How long the beak stays open.")]
        [SerializeField] private float jawOpenDuration = 0.45f;

        private OstrichLocomotion locomotion;
        private OstrichSpineMotion spine;
        private Transform body;

        private OstrichNeckGaze gazeState;
        private OstrichNeckSpring spring;

        private Quaternion[] neckRest;
        private Quaternion headRest, jawRest;
        private float[] gazeWeights, bounceWeights;

        private Vector3 lastVelocity;
        private Vector3 lastBodyPos;
        private bool haveLastPos;
        private float jawTimer, jawHold;
        private System.Random jawRng;

        /// Where the bird is currently looking, in degrees from neutral. Read-only; nothing outside
        /// drives it.
        public Vector2 GazeAngles => gazeState != null ? gazeState.Current : Vector2.zero;
        public Vector2 BounceAngles => spring != null ? spring.Value : Vector2.zero;

        private void Awake()
        {
            locomotion = GetComponent<OstrichLocomotion>();
            spine = GetComponent<OstrichSpineMotion>();
            body = transform;
            Resolve();
        }

        /// Finds the chain by name if it was not assigned, and caches the rest pose. Public so a test
        /// or an editor tool can rebuild the cache after re-importing the model.
        public void Resolve()
        {
            // Pick up the component wiring here as well as in Awake, so this can be driven from an
            // EditMode test where Awake never fires.
            if (locomotion == null) locomotion = GetComponent<OstrichLocomotion>();
            if (spine == null) spine = GetComponent<OstrichSpineMotion>();
            if (body == null) body = transform;

            // Rediscover when the cache is empty OR when any entry has gone null. Re-exporting the
            // model with a different bone count leaves the serialized array at its old length with
            // every reference dangling, and a length check alone would sail straight past that.
            if (NeedsRediscovery(neckBones))
            {
                var found = new System.Collections.Generic.List<Transform>();
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Neck_")) found.Add(t);
                // Ordinal sort puts Neck_01..Neck_11 in chain order because the names are zero-padded.
                found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                neckBones = found.ToArray();
            }

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (head == null && t.name == "Head") head = t;
                if (jaw == null && t.name == "Jaw") jaw = t;
            }

            int n = neckBones != null ? neckBones.Length : 0;
            neckRest = new Quaternion[n];
            for (int i = 0; i < n; i++) neckRest[i] = neckBones[i].localRotation;
            if (head != null) headRest = head.localRotation;
            if (jaw != null) jawRest = jaw.localRotation;

            gazeWeights = BuildWeights(n, gazeHeadBias, towardHead: true);
            bounceWeights = BuildWeights(n, bounceBaseBias, towardHead: false);

            int seed = seedFromInstance ? GetInstanceID() : gazeSeed;
            gazeState = new OstrichNeckGaze(gaze, seed);
            spring = new OstrichNeckSpring(bounce);
            jawRng = new System.Random(seed ^ 0x5bf03635);
            jawTimer = NextJawInterval();
            jawHold = 0f;
            lastVelocity = Vector3.zero;
            haveLastPos = false;
        }

        /// True when a serialized bone cache cannot be trusted: absent, empty, or holding a reference
        /// that no longer points at anything.
        private static bool NeedsRediscovery(Transform[] bones)
        {
            if (bones == null || bones.Length == 0) return true;
            foreach (Transform t in bones)
                if (t == null) return true;
            return false;
        }

        /// Share of the total bend each joint carries, normalised to sum to 1.
        ///
        /// Normalising matters: each joint is rotated about a WORLD axis and children inherit their
        /// parents' rotation, so the angles add up the chain. Weights that did not sum to 1 would put
        /// the head somewhere other than the angle that was asked for.
        private static float[] BuildWeights(int n, float bias, bool towardHead)
        {
            var w = new float[n];
            if (n == 0) return w;
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 1f : i / (float)(n - 1);      // 0 at base, 1 at head
                float x = towardHead ? t : 1f - t;
                w[i] = Mathf.Pow(0.15f + x, Mathf.Max(bias, 0f));
                total += w[i];
            }
            for (int i = 0; i < n; i++) w[i] /= total;
            return w;
        }

        private void LateUpdate() => Step(Time.deltaTime);

        /// One frame of neck motion. dt-driven so it can be marched from a test.
        public void Step(float deltaTime)
        {
            if (neckBones == null || neckBones.Length == 0 || head == null) return;
            float dt = Mathf.Max(deltaTime, 1e-5f);

            // OstrichSpineMotion resets the chain to rest before applying its own layers, so normally
            // this component simply adds on top. With it absent or disabled nothing would reset, and
            // these contributions would accumulate into a corkscrew -- so do the reset here instead.
            if (spine == null || !spine.enabled)
            {
                for (int i = 0; i < neckBones.Length; i++) neckBones[i].localRotation = neckRest[i];
                head.localRotation = headRest;
            }

            float speed01 = Mathf.Clamp01(Mathf.Abs(locomotion.LastFrame.AchievedSpeed) /
                                          Mathf.Max(locomotion.MaxSpeed, 1e-4f));

            if (enableGaze) ApplyGaze(dt, speed01);
            if (enableBounce) ApplyBounce(dt);
            if (enableJaw) ApplyJaw(dt);
        }

        /// The look-around. Yaw about the body's up, pitch about its right.
        private void ApplyGaze(float dt, float speed01)
        {
            gazeState.Step(dt, speed01);
            Vector2 g = gazeState.Current;
            if (Mathf.Abs(g.x) < 1e-4f && Mathf.Abs(g.y) < 1e-4f) return;

            for (int i = 0; i < neckBones.Length; i++)
            {
                float w = gazeWeights[i];
                Quaternion yaw = Quaternion.AngleAxis(g.x * w, body.up);
                Quaternion pitch = Quaternion.AngleAxis(g.y * w, body.right);
                neckBones[i].rotation = yaw * pitch * neckBones[i].rotation;
            }
        }

        /// The bounce, driven by how the body is actually moving in the world.
        ///
        /// Measured from the BODY TRANSFORM, not from locomotion.MeasuredVelocity. The latter is the
        /// path velocity and deliberately excludes the bob and sway that OstrichLocomotion adds on top
        /// as display offsets -- but the bob is exactly the motion a rider feels, and it is what the
        /// neck is supposed to be reacting to. Measured on the real rig the transform carries roughly
        /// 11 m/s² of vertical acceleration at half speed against 5 from the path velocity, and driving
        /// off the path velocity produced a bounce of about one degree: invisible.
        private void ApplyBounce(float dt)
        {
            Vector3 pos = body.position;
            if (!haveLastPos)
            {
                lastBodyPos = pos;
                lastVelocity = Vector3.zero;
                haveLastPos = true;
                return;                       // no acceleration can be formed from a single sample
            }

            // A jump this big in one frame is not a stride. The bird is snapped to the ground on
            // spawn, and WorldStreaming migrates entities between scenes, so treat it as a
            // discontinuity: forget the history and let the spring restart from rest rather than
            // differentiating across the seam.
            if ((pos - lastBodyPos).sqrMagnitude > teleportDistance * teleportDistance)
            {
                lastBodyPos = pos;
                lastVelocity = Vector3.zero;
                spring.Reset();
                return;
            }

            Vector3 v = (pos - lastBodyPos) / dt;
            Vector3 accelWorld = (v - lastVelocity) / dt;
            lastBodyPos = pos;
            lastVelocity = v;

            // Into body axes: x fore/aft, y vertical, z lateral. Raw -- never pre-smoothed.
            Vector3 drive = new Vector3(
                Vector3.Dot(accelWorld, body.forward),
                Vector3.Dot(accelWorld, body.up),
                Vector3.Dot(accelWorld, body.right));

            spring.Step(dt, drive);
            Vector2 b = spring.Value;
            if (Mathf.Abs(b.x) < 1e-4f && Mathf.Abs(b.y) < 1e-4f) return;

            for (int i = 0; i < neckBones.Length; i++)
            {
                float w = bounceWeights[i];
                Quaternion pitch = Quaternion.AngleAxis(b.x * w, body.right);
                Quaternion roll = Quaternion.AngleAxis(b.y * w, body.forward);
                neckBones[i].rotation = pitch * roll * neckBones[i].rotation;
            }
        }

        /// An occasional beak open. Nothing else touches the jaw, so it is reset here rather than by
        /// OstrichSpineMotion, or the opening would accumulate frame on frame.
        private void ApplyJaw(float dt)
        {
            if (jaw == null) return;
            jaw.localRotation = jawRest;

            if (jawHold > 0f)
            {
                jawHold -= dt;
            }
            else
            {
                jawTimer -= dt;
                if (jawTimer <= 0f)
                {
                    jawHold = jawOpenDuration;
                    jawTimer = NextJawInterval();
                }
            }

            if (jawHold <= 0f) return;

            // Open and shut on one smooth arc so the beak does not pop at either end.
            float t = 1f - Mathf.Clamp01(jawHold / Mathf.Max(jawOpenDuration, 1e-4f));
            float open = Mathf.Sin(t * Mathf.PI);
            Vector3 axis = jawOpenAxis.sqrMagnitude < 1e-6f ? Vector3.right : jawOpenAxis.normalized;
            jaw.localRotation = jawRest * Quaternion.AngleAxis(jawOpenAngle * open, axis);
        }

        private float NextJawInterval()
        {
            float lo = Mathf.Min(minJawInterval, maxJawInterval);
            float hi = Mathf.Max(minJawInterval, maxJawInterval);
            return lo + (float)jawRng.NextDouble() * (hi - lo);
        }
    }
}
