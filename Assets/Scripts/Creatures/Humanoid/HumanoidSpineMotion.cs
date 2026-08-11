// Torso, neck and head motion for the humanoid robot.
//
// A walking person's shoulders and hips turn in OPPOSITE directions. The pelvis rotates toward the
// swinging leg and the chest rotates back against it, and the arms ride on the chest -- which is
// why an arm swing with a rigid torso reads as a mannequin being carried along rather than as
// something walking. LeggedLocomotion owns the pelvis (invariant I4: it is the single owner of the
// body's transform), so what is left for this component is everything above it.
//
// Modelled on OstrichSpineMotion, and split out for the same reason: it is DISPLAY ONLY. Nothing
// here feeds back into where the feet go or how fast the machine travels, so it can be tuned,
// disabled or replaced with no risk to the gait.
//
// ─────────── what the counter-rotation is measured against ───────────
//
// The pelvis does not actually turn -- the base drives it in a straight line -- so there is no
// measured pelvic yaw to invert. What there IS is the gait clock, and the leg's fore-aft position
// is a function of it. The chest turns opposite the LEG on each side, which is the same thing as
// turning WITH the arm on that side, and both statements fall out of one expression.
//
// Everything counter-rotational is driven off `LastFrame.Phase` and the legs' own offsets, exactly
// as the arm swing is, so the two cannot come apart. Nothing at stride frequency is filtered; the
// only smoothed terms here are the speed envelope and the head's look direction, neither of which
// moves at the rate the machine steps.
//
// Runs at 150, after HumanoidLocomotion (100) and HumanoidArmSwing (120): the body is posed and the
// arms are solved before the chest they hang off is turned.
using UnityEngine;
using SpaceGame.Locomotion;

namespace SpaceGame.Creatures.Humanoid
{
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(HumanoidLocomotion))]
    public class HumanoidSpineMotion : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Torso bone. Auto-found by name (Chest) if empty.")]
        [SerializeField] private Transform chest;
        [Tooltip("Neck bones, base to head. Auto-found by name (Neck_01, Neck_02, ...) if empty.")]
        [SerializeField] private Transform[] neckBones;
        [Tooltip("Head bone. Auto-found by name if empty.")]
        [SerializeField] private Transform head;

        [Header("Torso")]
        [Tooltip("Degrees the chest counter-rotates against the pelvis at top speed. SMALL. Past about " +
                 "12 the machine reads as wringing itself out rather than walking.")]
        [Range(0f, 20f)]
        [SerializeField] private float counterRotation = 7f;
        [Tooltip("Degrees the chest rolls toward the loaded foot, at twice the stride frequency. This " +
                 "is the shoulder drop over the stance leg.")]
        [Range(0f, 12f)]
        [SerializeField] private float shoulderDrop = 2.5f;
        [Tooltip("Degrees the chest leans forward at top speed, on top of whatever the body motion is " +
                 "already doing. A person leans from the waist, not the ankles.")]
        [Range(0f, 20f)]
        [SerializeField] private float leanIntoRun = 5f;

        [Header("Neck and head")]
        [Tooltip("How much of the chest's counter-rotation the neck undoes, so the head keeps facing " +
                 "where the machine is going instead of scanning left and right every stride.")]
        [Range(0f, 1f)]
        [SerializeField] private float headStabilisation = 0.85f;
        [Tooltip("Head turns toward travel. 0 keeps it locked forward.")]
        [Range(0f, 1f)]
        [SerializeField] private float lookAheadWeight = 0.5f;
        [SerializeField] private float lookSmooth = 6f;
        [SerializeField] private float maxLookAngle = 60f;

        [Header("Envelope")]
        [Tooltip("How quickly the whole spine motion grows and fades with speed. An envelope, not a " +
                 "filter on anything that moves at stride frequency.")]
        [SerializeField] private float effortSmooth = 4f;

        private HumanoidLocomotion locomotion;
        private Quaternion chestRest;
        private Quaternion[] neckRest;
        private Quaternion headRest;
        private float leadPhase;          // the left leg's slice of the cycle
        private float smoothedEffort;
        private float smoothedLook;
        private bool resolved;

        private void Awake()
        {
            locomotion = GetComponent<HumanoidLocomotion>();
            Resolve();
        }

        /// Finds the spine by name if it was not assigned, and caches the rest pose. Public so a test
        /// or an editor tool can rebuild the cache after re-importing the model.
        public void Resolve()
        {
            if (locomotion == null) locomotion = GetComponent<HumanoidLocomotion>();

            if (chest == null)
            {
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                    if (t.name == "Chest") { chest = t; break; }
            }

            // Rediscover when the cache is empty OR when any entry has gone null. Re-exporting the
            // model with a different bone count leaves the serialized array at its old length with
            // every reference dangling, and a length check alone would sail past that and then throw
            // on the first localRotation.
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

            chestRest = chest != null ? chest.localRotation : Quaternion.identity;
            neckRest = new Quaternion[neckBones.Length];
            for (int i = 0; i < neckBones.Length; i++) neckRest[i] = neckBones[i].localRotation;
            if (head != null) headRest = head.localRotation;

            // The leg on the machine's own left, and its slice of the cycle. Taken from where the feet
            // are rather than from an index, so a rig re-exported in a different order still turns the
            // torso the right way.
            leadPhase = 0f;
            if (locomotion != null && locomotion.IsReady)
            {
                int left = 0;
                float leftMost = float.MaxValue;
                for (int i = 0; i < locomotion.LegCount; i++)
                {
                    float x = locomotion.LegHomeLocal(i).x;
                    if (x < leftMost) { leftMost = x; left = i; }
                }
                leadPhase = locomotion.LegPhaseOffset(left);
            }

            resolved = chest != null;
        }

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
            if (!resolved) { Resolve(); if (!resolved) return; }
            float dt = Mathf.Max(deltaTime, 1e-5f);

            // Reset to rest first, so each frame's rotation is computed against a known pose rather
            // than accumulating on top of the last one.
            chest.localRotation = chestRest;
            for (int i = 0; i < neckBones.Length; i++) neckBones[i].localRotation = neckRest[i];
            if (head != null) head.localRotation = headRest;

            HumanoidLocomotion.Diagnostics d = locomotion.LastFrame;
            float target = Mathf.Clamp01(Mathf.Abs(d.AchievedSpeed) /
                                         Mathf.Max(locomotion.MaxSpeed, 1e-4f));
            smoothedEffort = Mathf.Lerp(smoothedEffort, target, 1f - Mathf.Exp(-effortSmooth * dt));

            // How far forward the LEFT leg is, from the clock. -cos so it is rearmost when that leg
            // lifts. The chest turns against it: left leg forward, left shoulder back.
            float p = Mathf.Repeat(d.Phase - leadPhase, 1f);
            float legFore = -Mathf.Cos(2f * Mathf.PI * p);

            // Twice the stride frequency, because both stance legs take their turn under the same
            // shoulder over one cycle.
            float drop = Mathf.Sin(4f * Mathf.PI * p) * shoulderDrop * smoothedEffort;

            float yaw = -legFore * counterRotation * smoothedEffort;
            float lean = leanIntoRun * d.Duty * smoothedEffort;

            chest.localRotation = chestRest *
                Quaternion.Euler(lean, yaw, drop);

            ApplyHeadStabilisation(yaw);
            ApplyLook(dt);
        }

        /// The neck spends most of the chest's turn back again, so the head keeps facing forward while
        /// the shoulders swing under it. Exactly the trick the ostrich's neck plays with the bob, one
        /// axis over.
        private void ApplyHeadStabilisation(float chestYaw)
        {
            int n = neckBones.Length;
            if (n == 0) return;

            float share = -chestYaw * headStabilisation / n;
            for (int i = 0; i < n; i++)
                neckBones[i].localRotation = neckBones[i].localRotation * Quaternion.Euler(0f, share, 0f);
        }

        /// Head turns toward travel.
        private void ApplyLook(float dt)
        {
            if (head == null) return;

            Vector3 v = locomotion.MeasuredVelocity;
            v.y = 0f;
            float target = 0f;
            if (v.sqrMagnitude > 1e-4f)
                target = Mathf.Clamp(Vector3.SignedAngle(transform.forward, v.normalized, Vector3.up),
                                     -maxLookAngle, maxLookAngle) * lookAheadWeight;

            smoothedLook = Mathf.Lerp(smoothedLook, target, 1f - Mathf.Exp(-lookSmooth * dt));
            head.rotation = Quaternion.AngleAxis(smoothedLook, Vector3.up) * head.rotation;
        }

        /// Degrees the chest is currently turned against the pelvis. For the tests: what has to be
        /// proved is that it is opposite the leg, not merely non-zero.
        public float ChestYaw
        {
            get
            {
                if (!resolved) return 0f;
                return Mathf.DeltaAngle(
                    0f, (Quaternion.Inverse(chestRest) * chest.localRotation).eulerAngles.y);
            }
        }
    }
}
