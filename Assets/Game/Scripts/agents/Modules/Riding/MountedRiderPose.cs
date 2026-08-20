// Sits the rider in the saddle.
//
// Drop this next to a MountModule and whoever climbs on stops standing to attention above the
// seat and starts riding: thighs round the barrel, knees bent, weight forward, hands on the reins,
// giving with the stride. There is no sitting or riding clip anywhere in this project and no
// animator state to put one in, so the pose is built here instead of authored.
//
// Deliberately knows nothing about ostriches. Everything it reacts to is measured off the mount's
// own transform, so it works on the ant, the crawler, the horse and anything else that grows a
// MountModule — and, just as importantly, it does not drag Assembly-CSharp into the
// SpaceGame.Creatures.Ostrich asmdef to ask a locomotion component how fast it thinks it is going.
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Agents
{
    // After Unity evaluates the Animator (that happens in PreLateUpdate, ahead of every
    // LateUpdate) so these writes land on top of the clip rather than under it, and before
    // MountModule's order-1000 camera pass.
    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    public class MountedRiderPose : MonoBehaviour
    {
        [Tooltip("Mount this poses the rider of. Found on this GameObject when empty.")]
        [SerializeField] private MountModule mountModule;

        [Header("Saddle Pose (degrees)")]
        [Tooltip("Hip: X swings the thigh forward (negative), Z abducts it out around the mount's " +
                 "barrel (negative opens the legs). Applied in the RIDER's frame and mirrored for " +
                 "the two sides. Defaults are solved against the ostrich: the knee lands on the " +
                 "barrel's edge rather than inside it, which is the difference between a rider " +
                 "gripping the animal and one standing in it.")]
        [SerializeField] private Vector3 upperLegRotation = new Vector3(-50f, 0f, -26f);
        [Tooltip("Knee, hinged in the THIGH's frame so the bend follows the abducted leg. NEGATIVE " +
                 "closes the knee on this rig — positive kicks the shin up behind like a hamstring " +
                 "curl and lifts the foot above the knee.")]
        [SerializeField] private Vector3 lowerLegRotation = new Vector3(-90f, 0f, 0f);
        [SerializeField] private Vector3 footRotation = new Vector3(25f, 0f, 0f);
        [SerializeField] private Vector3 spineRotation = new Vector3(8f, 0f, 0f);
        [Tooltip("Split from the spine so the rider's back curves over two joints instead of " +
                 "hinging at one.")]
        [SerializeField] private Vector3 chestRotation = new Vector3(6f, 0f, 0f);
        [Tooltip("Shoulder, in the rider's frame. Positive X drops the arm — the arms hang off the " +
                 "already-leaned chest, so a negative swing here puts the hands up around the " +
                 "rider's own head instead of down on the reins.")]
        [SerializeField] private Vector3 upperArmRotation = new Vector3(15f, 0f, 8f);
        [Tooltip("Elbow, hinged in the UPPER ARM's frame so the forearm folds wherever the shoulder " +
                 "has put it.")]
        [SerializeField] private Vector3 lowerArmRotation = new Vector3(35f, 0f, 0f);

        [Header("Motion Response")]
        [SerializeField] private RiderPoseGains gains = RiderPoseGains.Default;
        [Tooltip("How fast the measured motion catches up. Raw frame-to-frame deltas off a legged " +
                 "mount are far too noisy to drive a spine with.")]
        [SerializeField, Min(0.01f)] private float motionSmooth = 9f;
        [Tooltip("Ignore measured speeds above this, in m/s and degrees/s. A chunk load, a scene " +
                 "migration or a leap landing can move the mount a very long way in one frame, and " +
                 "that is not a stride.")]
        [SerializeField, Min(0f)] private float motionRejectThreshold = 60f;

        [Header("Blend")]
        [Tooltip("Seconds to ease into the pose on mounting and out of it on dismounting. Without " +
                 "it the rider snaps between standing and seated on the frame the mount lands.")]
        [SerializeField, Min(0.01f)] private float blendDuration = 0.25f;

        // Rider references are cached here rather than read back off MountModule, because they have
        // to outlive the dismount: the pose blends OUT over blendDuration, and by then MountModule
        // has already cleared everything it knew about the rider.
        private Animator riderAnimator;
        private Transform riderRoot;

        private float weight;
        private float targetWeight;

        private Vector3 lastMountPosition;
        private float lastMountYaw;
        private bool hasLastSample;

        private float smoothedVertical;
        private float smoothedForward;
        private float smoothedTurn;

        /// <summary>Current blend weight, 0 (animator owns the bones) to 1 (fully seated).</summary>
        public float Weight => weight;

        private void Awake()
        {
            if (!mountModule)
                mountModule = GetComponent<MountModule>();

            if (!mountModule)
                Debug.LogWarning($"[MountedRiderPose] No MountModule on {name}; nobody will be posed.", this);
        }

        private void OnEnable()
        {
            if (!mountModule)
                return;

            mountModule.Mounted += HandleMounted;
            mountModule.Dismounted += HandleDismounted;

            // Enabled onto an already-ridden mount — a domain reload, or a module toggled back on.
            if (mountModule.IsMounted)
                HandleMounted(mountModule.MountedPlayerMovement);
        }

        private void OnDisable()
        {
            if (mountModule)
            {
                mountModule.Mounted -= HandleMounted;
                mountModule.Dismounted -= HandleDismounted;
            }

            // Release the bones outright rather than blending: there is no LateUpdate coming to
            // finish the blend with, so a partial pose would be frozen onto the rider for good.
            weight = 0f;
            targetWeight = 0f;
            riderAnimator = null;
            riderRoot = null;
            hasLastSample = false;
        }

        private void HandleMounted(PlayerMovement rider)
        {
            riderRoot = mountModule ? mountModule.MountedPlayerTransform : null;
            riderAnimator = riderRoot ? riderRoot.GetComponentInChildren<Animator>(true) : null;

            // Bones are resolved through the humanoid avatar, so a generic rig has nothing to
            // resolve them against and is left to its animator untouched.
            if (riderAnimator != null && !riderAnimator.isHuman)
            {
                Debug.LogWarning($"[MountedRiderPose] {riderAnimator.name} is not a humanoid rig; " +
                                 "no riding pose will be applied.", this);
                riderAnimator = null;
            }

            targetWeight = riderAnimator != null ? 1f : 0f;
            ResetMotionSamples();
        }

        private void HandleDismounted(PlayerMovement rider)
        {
            // References stay put — LateUpdate keeps posting the decaying pose until it reaches 0.
            targetWeight = 0f;
        }

        private void ResetMotionSamples()
        {
            hasLastSample = false;
            smoothedVertical = 0f;
            smoothedForward = 0f;
            smoothedTurn = 0f;
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            weight = Mathf.MoveTowards(weight, targetWeight, deltaTime / blendDuration);

            if (weight <= 0f)
            {
                if (targetWeight <= 0f)
                {
                    riderAnimator = null;
                    riderRoot = null;
                    hasLastSample = false;
                }
                return;
            }

            if (riderAnimator == null || riderRoot == null)
                return;

            SampleMountMotion(deltaTime);
            ApplyPose(RiderPoseMath.SpineOffset(smoothedVertical, smoothedForward, smoothedTurn, gains));
        }

        /// <summary>
        /// Pose a rider on demand, outside play mode — for an editor preview or a test.
        ///
        /// Exists so a preview drives the real posing code rather than a second copy of it that can
        /// drift out of agreement with this one and quietly certify a pose the game never produces.
        /// </summary>
        public void PreviewPose(Animator animator, Transform root, float blendWeight, Vector3 motion)
        {
            if (animator == null || root == null)
                return;

            riderAnimator = animator;
            riderRoot = root;
            weight = Mathf.Clamp01(blendWeight);
            ApplyPose(motion);
        }

        /// <summary>
        /// Everything the pose reacts to, measured off the mount's transform frame to frame.
        ///
        /// The transform, specifically — not a motor's reported velocity. A legged mount's body
        /// height is written by its locomotion each frame and the bob IS the stride; a velocity
        /// figure has that filtered out of it long before anything else can read it, which leaves
        /// the rider sitting perfectly still on a machine that is visibly bouncing.
        /// </summary>
        private void SampleMountMotion(float deltaTime)
        {
            Vector3 position = transform.position;
            float yaw = transform.eulerAngles.y;

            if (!hasLastSample || deltaTime <= 0f)
            {
                lastMountPosition = position;
                lastMountYaw = yaw;
                hasLastSample = true;
                return;
            }

            Vector3 delta = position - lastMountPosition;
            float vertical = delta.y / deltaTime;
            float forward = Vector3.Dot(delta / deltaTime, transform.forward);
            float turn = Mathf.DeltaAngle(lastMountYaw, yaw) / deltaTime;

            lastMountPosition = position;
            lastMountYaw = yaw;

            // A teleport is not a stride. Drop the whole sample rather than clamping it, so a
            // scene migration doesn't leave the rider leaning at the limit for the smoothing's
            // whole time constant afterwards.
            if (Mathf.Abs(vertical) > motionRejectThreshold ||
                Mathf.Abs(forward) > motionRejectThreshold ||
                Mathf.Abs(turn) > motionRejectThreshold * 10f)
                return;

            float k = 1f - Mathf.Exp(-motionSmooth * deltaTime);
            smoothedVertical = Mathf.Lerp(smoothedVertical, vertical, k);
            smoothedForward = Mathf.Lerp(smoothedForward, forward, k);
            smoothedTurn = Mathf.Lerp(smoothedTurn, turn, k);
        }

        private void ApplyPose(Vector3 motion)
        {
            // Parents before children, always. Each bone's offset is composed onto the world
            // rotation its parent has ALREADY been moved to, so leaning the chest carries the arms
            // with it and the arm offsets then read as "relative to the leaning chest". Applying a
            // parent after its child would undo the child.
            Bone(HumanBodyBones.Spine, spineRotation + motion * 0.6f, false);
            Bone(HumanBodyBones.Chest, chestRotation + motion * 0.4f, false);

            Bone(HumanBodyBones.LeftUpperArm, upperArmRotation, false);
            Bone(HumanBodyBones.RightUpperArm, upperArmRotation, true);
            // Elbows and knees hinge about the limb they are ON, not about the rider's torso. Once
            // the thigh has been swung out around the barrel, "bend down and back" is a rotation in
            // the THIGH's frame; asking for it in the rider's frame swings the shin out sideways
            // instead and leaves the leg held out in a mid-air split.
            BoneLocal(HumanBodyBones.LeftLowerArm, lowerArmRotation, false);
            BoneLocal(HumanBodyBones.RightLowerArm, lowerArmRotation, true);

            Bone(HumanBodyBones.LeftUpperLeg, upperLegRotation, false);
            Bone(HumanBodyBones.RightUpperLeg, upperLegRotation, true);
            BoneLocal(HumanBodyBones.LeftLowerLeg, lowerLegRotation, false);
            BoneLocal(HumanBodyBones.RightLowerLeg, lowerLegRotation, true);
            BoneLocal(HumanBodyBones.LeftFoot, footRotation, false);
            BoneLocal(HumanBodyBones.RightFoot, footRotation, true);
        }

        /// <summary>
        /// Rotate a hinge joint about its OWN parent-relative frame, so the bend follows wherever
        /// the limb has already been swung to.
        ///
        /// X is the hinge axis. Which SIGN closes the joint is a property of the rig, not of this
        /// code — on the Mixamo-derived astronaut it is negative, and a positive knee value kicks
        /// the shin up behind the rider instead of folding it down. Measure before trusting a sign
        /// here. Mirroring negates Y and Z only, exactly as in <see cref="Bone"/>, so a symmetric
        /// pair stays symmetric.
        /// </summary>
        private void BoneLocal(HumanBodyBones bone, Vector3 euler, bool mirror)
        {
            Transform target = riderAnimator.GetBoneTransform(bone);
            if (target == null)
                return;

            if (mirror)
                euler = new Vector3(euler.x, -euler.y, -euler.z);

            Quaternion offset = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(euler), weight);
            target.localRotation = target.localRotation * offset;
        }

        /// <summary>
        /// Rotate one bone by <paramref name="euler"/> degrees about the RIDER's axes, not the
        /// bone's own.
        ///
        /// This is the part that has to be got right. A humanoid avatar hands back the real rig
        /// transforms, and their local axes are whatever the artist left them as — commonly
        /// mirrored between left and right, and rarely aligned with anything meaningful. Writing
        /// local Euler offsets straight onto them gives a pose that is correct on one rig and
        /// lopsided on the next. Conjugating through the rider's rotation means X is always "lean
        /// forward" and Z is always "roll outward" whatever the rig believes, and mirroring is then
        /// just negating the two axes that cross the sagittal plane.
        ///
        /// A multiplicative offset on the animator's output, rather than an absolute local
        /// rotation: a humanoid's rest pose isn't identity and isn't available at runtime, and
        /// capturing a base at mount time would sample whatever frame the idle clip happened to be
        /// on — a slightly different pose every time you got on. An offset needs no base, cannot
        /// drift, lets the idle's breathing show through, and fails towards "standing" rather than
        /// towards garbage.
        /// </summary>
        private void Bone(HumanBodyBones bone, Vector3 euler, bool mirror)
        {
            Transform target = riderAnimator.GetBoneTransform(bone);
            if (target == null)
                return;

            if (mirror)
                euler = new Vector3(euler.x, -euler.y, -euler.z);

            Quaternion riderRotation = riderRoot.rotation;
            Quaternion inRiderFrame = riderRotation * Quaternion.Euler(euler) * Quaternion.Inverse(riderRotation);
            Quaternion blended = Quaternion.Slerp(Quaternion.identity, inRiderFrame, weight);

            target.rotation = blended * target.rotation;
        }
    }
}
