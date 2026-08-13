// Neck, head and body-follow motion for the robot ostrich.
//
// The one thing that makes a walking bird read as a bird rather than a machine on two legs is
// that its BODY bobs and its HEAD does not. Birds hold their heads still in world space and let
// the neck absorb everything underneath -- it is why a walking chicken looks like it is on rails.
// OstrichLocomotion deliberately bobs, sways and pitches the body; this component spends the neck
// undoing that at the head.
//
// Split out from the locomotion because it is display only. Nothing here feeds back into where
// the feet go or how fast the bird travels, so it can be tuned, disabled or replaced without any
// risk to the gait.
//
// Runs after OstrichLocomotion (order 100), which has already posed the body for this frame.
using UnityEngine;
using SpaceGame.Locomotion;

namespace SpaceGame.Creatures.Ostrich
{
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(OstrichLocomotion))]
    public class OstrichSpineMotion : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Neck bones, base to head. Auto-found by name (Neck_01, Neck_02, ...) if empty.")]
        [SerializeField] private Transform[] neckBones;
        [Tooltip("Head bone. Auto-found by name if empty.")]
        [SerializeField] private Transform head;

        [Header("Head stabilisation")]
        [Tooltip("How much of the body's bob the neck cancels. 1 holds the head perfectly still, " +
                 "which reads as uncanny; a little residual motion looks alive.")]
        [Range(0f, 1f)]
        [SerializeField] private float stabilisation = 0.8f;
        [Tooltip("How quickly the head returns to its held position. Too high and it snaps; too low " +
                 "and the neck lags behind the body like rubber.")]
        [SerializeField] private float stabiliseSmooth = 14f;
        [Tooltip("Largest correction the neck may make, in local units. Stops the neck folding " +
                 "through the body if the locomotion does something extreme.")]
        [SerializeField] private float maxCorrection = 0.35f;

        [Header("Stride bob")]
        [Tooltip("The forward/back head pecking that a walking bird does, in degrees. Driven off the " +
                 "gait clock so it stays in step with the feet at any speed.")]
        [SerializeField] private float peckAngle = 5f;

        [Header("Look")]
        [Tooltip("Head turns to face where the bird is going. 0 keeps it locked forward.")]
        [SerializeField] private float lookAheadWeight = 0.6f;
        [SerializeField] private float lookSmooth = 6f;
        [SerializeField] private float maxLookAngle = 55f;

        [Header("Counter-swing")]
        [Tooltip("Degrees the neck swings against a turn, to counterbalance it.")]
        [SerializeField] private float turnCounterSwing = 10f;

        private OstrichLocomotion locomotion;
        private Transform body;

        private Vector3 heldHeadWorld;      // where the head is being held, in world space
        private bool held;
        private float smoothedLook;
        private float smoothedSwing;
        private float lastYaw;
        private Quaternion[] neckRest;
        private Quaternion headRest;

        private void Awake()
        {
            locomotion = GetComponent<OstrichLocomotion>();
            body = transform;
            Resolve();
        }

        /// Finds the neck chain by name if it was not assigned. Public so a test or an editor tool can
        /// rebuild the cache after re-importing the model.
        public void Resolve()
        {
            // Pick up the component wiring here as well as in Awake. EditMode never fires Awake, and
            // OstrichLocomotion already splits Initialise() out for exactly that reason; without this
            // a test that resolves and then steps NREs on `locomotion`.
            if (locomotion == null) locomotion = GetComponent<OstrichLocomotion>();
            if (body == null) body = transform;

            // Rediscover when the cache is empty OR when any entry has gone null. Re-exporting the
            // model with a different bone count leaves the serialized array at its old length with
            // every reference dangling, and a length check alone would sail straight past that and
            // then throw on the first localRotation.
            if (NeedsRediscovery(neckBones))
            {
                var found = new System.Collections.Generic.List<Transform>();
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Neck_")) found.Add(t);
                found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                neckBones = found.ToArray();
            }

            if (head == null)
            {
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                    if (t.name == "Head") { head = t; break; }
            }

            neckRest = new Quaternion[neckBones.Length];
            for (int i = 0; i < neckBones.Length; i++)
                neckRest[i] = neckBones[i].localRotation;
            if (head != null) headRest = head.localRotation;

            held = false;
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

        private void LateUpdate() => Step(Time.deltaTime);

        /// One frame of spine motion. dt-driven so it can be marched from a test.
        public void Step(float deltaTime)
        {
            if (neckBones == null || neckBones.Length == 0 || head == null) return;
            float dt = Mathf.Max(deltaTime, 1e-5f);

            // Reset to rest first, so each frame's correction is computed against a known pose rather
            // than accumulating on top of the last one.
            for (int i = 0; i < neckBones.Length; i++) neckBones[i].localRotation = neckRest[i];
            head.localRotation = headRest;

            OstrichLocomotion.Diagnostics d = locomotion.LastFrame;
            float speed01 = Mathf.Clamp01(Mathf.Abs(d.AchievedSpeed) /
                                          Mathf.Max(locomotion.MaxSpeed, 1e-4f));

            ApplyCounterSwing(dt, speed01);
            ApplyPeck(d, speed01);
            ApplyLook(dt);
            Stabilise(dt, speed01);
        }

        /// The neck leans away from a turn, the way a bird uses it as a counterweight.
        ///
        /// Taken from how fast the body is ACTUALLY turning rather than from a commanded rate, so it
        /// still works when something other than the driver is turning the bird -- a mount, a
        /// cutscene, or a shove.
        private void ApplyCounterSwing(float dt, float speed01)
        {
            float yaw = body.eulerAngles.y;
            float measured = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastYaw = yaw;

            float yawRate = Mathf.Clamp(measured / Mathf.Max(locomotion.MaxYawRate, 1e-4f), -1f, 1f);
            float target = -turnCounterSwing * yawRate * speed01;
            smoothedSwing = Mathf.Lerp(smoothedSwing, target, 1f - Mathf.Exp(-6f * dt));

            SpreadOverNeck(Quaternion.AngleAxis(smoothedSwing, Vector3.forward));
        }

        /// Fore-and-aft head pecking, locked to the gait clock so it stays in step with the feet.
        private void ApplyPeck(OstrichLocomotion.Diagnostics d, float speed01)
        {
            float peck = Mathf.Sin(2f * Mathf.PI * 2f * d.Phase) * peckAngle * speed01;
            SpreadOverNeck(Quaternion.AngleAxis(peck, Vector3.right));
        }

        /// Head turns toward travel.
        private void ApplyLook(float dt)
        {
            Vector3 v = locomotion.MeasuredVelocity;
            v.y = 0f;
            float target = 0f;
            if (v.sqrMagnitude > 1e-4f)
                target = Mathf.Clamp(Vector3.SignedAngle(body.forward, v.normalized, Vector3.up),
                                     -maxLookAngle, maxLookAngle) * lookAheadWeight;

            smoothedLook = Mathf.Lerp(smoothedLook, target, 1f - Mathf.Exp(-lookSmooth * dt));
            head.rotation = Quaternion.AngleAxis(smoothedLook, Vector3.up) * head.rotation;
        }

        /// Hold the head where it was in world space, and pay for it out of the neck.
        ///
        /// Only the vertical component is cancelled. Cancelling the horizontal too would peg the head
        /// in space while the body walks out from under it, which looks like a bug rather than a bird.
        private void Stabilise(float dt, float speed01)
        {
            Vector3 now = head.position;

            if (!held)
            {
                heldHeadWorld = now;
                held = true;
                return;
            }

            // The held position tracks the body horizontally and lags it vertically -- that lag IS
            // the stabilisation.
            heldHeadWorld.x = now.x;
            heldHeadWorld.z = now.z;
            heldHeadWorld.y = Mathf.Lerp(heldHeadWorld.y, now.y, 1f - Mathf.Exp(-stabiliseSmooth * dt));

            float correction = (heldHeadWorld.y - now.y) * stabilisation * speed01;
            correction = Mathf.Clamp(correction, -maxCorrection, maxCorrection);
            if (Mathf.Abs(correction) < 1e-5f) return;

            // Spend the correction as a small rotation at each neck joint rather than translating the
            // head, so the neck stays a neck and no joint comes apart.
            int n = neckBones.Length;
            for (int i = 0; i < n; i++)
            {
                Transform bone = neckBones[i];
                Vector3 toHead = head.position - bone.position;
                if (toHead.sqrMagnitude < 1e-8f) continue;

                // angle that lifts the head by its share of the correction, about this joint
                float share = correction / n;
                float radius = toHead.magnitude;
                float angle = Mathf.Asin(Mathf.Clamp(share / radius, -0.99f, 0.99f)) * Mathf.Rad2Deg;

                Vector3 axis = Vector3.Cross(toHead.normalized, Vector3.up);
                if (axis.sqrMagnitude < 1e-8f) continue;
                bone.rotation = Quaternion.AngleAxis(-angle, axis.normalized) * bone.rotation;
            }
        }

        /// Share one rotation evenly across the neck joints, in each joint's own local space.
        private void SpreadOverNeck(Quaternion rotation)
        {
            int n = neckBones.Length;
            if (n == 0) return;
            Quaternion share = Quaternion.Slerp(Quaternion.identity, rotation, 1f / n);
            for (int i = 0; i < n; i++)
                neckBones[i].localRotation = neckBones[i].localRotation * share;
        }
    }
}
