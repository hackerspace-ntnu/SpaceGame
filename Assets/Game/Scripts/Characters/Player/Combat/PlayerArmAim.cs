// Where a worn forearm device is POINTED, on the bones, on every machine.
//
// The pose layer answers "is this arm up", and that was the whole answer until now: a lit torch
// took the held-item pose, and the pose is one fixed shape, so the beam left along whatever
// direction the clip happened to put the forearm in. Looking somewhere and lighting somewhere else
// is the device reporting its own state wrongly (GDC-L1-ANIM-0003) — you cannot tell a torch
// pointing past a thing from a torch that is off, and the fix costs no input latency, which is what
// ANIM-0002 asks of anything laid on top of a player-controlled pose.
//
// ── Why bones rather than more clips ──
// A clip per direction is what the aim rig's firing raise does (three Point clips blended on
// AimPitch) and it is coarse by construction: three poses cover the pitch and nothing covers the
// small yaw a seated player's look carries. A world-space rotation laid over the evaluated pose
// points the device AT something instead, and composes with whatever the pose layer was playing.
//
// ── Why LateUpdate, and why it is not PlayerAimRig ──
// Same two reasons PlayerHeadLook gives. The Upper Body mask and the IK Pass flag are both
// invisible from code, and a rotation written after the Animator has evaluated needs neither.
// PlayerAimRig owns layer WEIGHTS and Animator parameters; this owns two bones. Keeping them apart
// is what stops one component being the only thing that knows how an arm works.
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Swings a posed forearm so the device strapped to it points where the player is looking.
    ///
    /// <para>
    /// Runs on EVERY machine, like <see cref="PlayerHeadLook"/> and <see cref="PlayerAimRig"/> and
    /// for the same reason: <c>PlayerController.DisablePlayer</c> switches input and movement off on
    /// remote copies, so a rig that only ran for the owner would leave every other player's torch
    /// lighting their own boots. The aim is read from <see cref="PlayerViewNetwork.AimPivot"/> —
    /// this player's live look on their own machine and their replicated one everywhere else — so
    /// nothing new goes on the wire.
    /// </para>
    /// <para>
    /// Nothing here is saved. Where an arm points is a fact about this frame's look, and the look
    /// itself is already persisted by <see cref="PlayerLook.RestorePitch"/>.
    /// </para>
    /// </summary>
    // After MountedRiderPose (900), which writes the rider's upper and lower arms: an arm aimed
    // before it would be dragged off its aim a moment later. Beside PlayerHeadLook at 950 — the two
    // are independent, they share no bone.
    [DefaultExecutionOrder(950)]
    [DisallowMultipleComponent]
    public class PlayerArmAim : MonoBehaviour
    {
        [Header("Aim")]
        [Tooltip("Metres out along the look the arm converges on. The device and the crosshair " +
                 "agree exactly at this distance and drift apart either side of it, so this is the " +
                 "range the torch is expected to be USED at — roughly a cave wall, not the horizon.")]
        [SerializeField, Min(0.5f)] private float convergeRange = 25f;

        [Tooltip("Degrees the arm may be swung off the pose the clip put it in. A shoulder limit: " +
                 "past this the arm goes through the chest, so the aim stops following instead.")]
        [SerializeField, Min(0f)] private float maxSwing = 75f;

        [Tooltip("How much of the swing the SHOULDER takes; the elbow takes the rest. All of it on " +
                 "the elbow reads as a broken wrist, all of it on the shoulder as a stiff salute.")]
        [Range(0f, 1f)]
        [SerializeField] private float shoulderShare = 0.55f;

        [Tooltip("Extra elbow passes. The device is not AT the elbow, so bending the elbow moves it " +
                 "as well as turning it and one pass lands slightly short; two close the gap.")]
        [SerializeField, Range(1, 4)] private int elbowPasses = 2;

        [Tooltip("Seconds the aim fades in when a device starts asking for it, and out when it " +
                 "stops. Matches the pose blend: the arm must not snap to the crosshair while the " +
                 "pose it is laid on is still coming up.")]
        [SerializeField, Min(0f)] private float aimBlendTime = 0.18f;

        /// <summary>Below this weight the bones are left exactly as the Animator evaluated them.</summary>
        private const float StillWeight = 0.001f;

        private sealed class Arm
        {
            public Transform Pointer;      // the device's own transform; its forward is what is aimed
            public Transform Upper;
            public Transform Lower;
            public bool Resolved;
            public float Weight;
        }

        private readonly Arm[] arms = { new(), new() };   // indexed by ItemGrip.Hand

        private Animator animator;
        private PlayerAimRig rig;
        private PlayerViewNetwork view;

        private void Awake()
        {
            rig = GetComponent<PlayerAimRig>();
            view = GetComponent<PlayerViewNetwork>();

            // Included-inactive, for the reason PlayerHeadLook gives: on a remote copy
            // PlayerController has already switched parts of this character off, and the Animator is
            // still the one whose bones have to move.
            animator = GetComponentInChildren<Animator>(true);
        }

        /// <summary>
        /// Aim <paramref name="arm"/> along <paramref name="pointer"/>'s forward from now on.
        ///
        /// <para>
        /// Called by a worn device that is only useful pointed somewhere — the flashlight gauntlet,
        /// whose lamp IS the pointer. Whether the arm is actually aimed this frame is not the
        /// device's call: it is aimed while <see cref="PlayerAimRig"/> is carrying that arm's worn
        /// pose, so switching the torch off, picking something up, dying or opening the gear screen
        /// all put the arm back on its clip without the device having to hear about any of them.
        /// </para>
        /// </summary>
        public void SetPointer(ItemGrip.Hand arm, Transform pointer)
        {
            arms[(int)arm].Pointer = pointer;
        }

        /// <summary>
        /// Stop aiming <paramref name="arm"/>, if <paramref name="pointer"/> is still the one it is
        /// aimed along.
        ///
        /// <para>
        /// Matched rather than unconditional: two gauntlets swapped on one arm in a frame would
        /// otherwise let the outgoing one's teardown clear the incoming one's pointer, and the arm
        /// would stop aiming with nothing on screen to say why.
        /// </para>
        /// </summary>
        public void ClearPointer(ItemGrip.Hand arm, Transform pointer)
        {
            Arm entry = arms[(int)arm];
            if (entry.Pointer == pointer) entry.Pointer = null;
        }

        private void LateUpdate()
        {
            // A switched-off Animator means switched-off aim, exactly as it does for the head look:
            // RagdollRig hands every bone to physics, and a rotation written on top of a ragdoll is
            // a second driver fighting a joint rather than a pose being decorated.
            if (animator == null || !animator.enabled) return;

            Aim(ItemGrip.Hand.Right);
            Aim(ItemGrip.Hand.Left);
        }

        private void Aim(ItemGrip.Hand hand)
        {
            Arm arm = arms[(int)hand];

            bool wanted = arm.Pointer != null && rig != null && rig.WornArmPosed(hand);
            arm.Weight = PoseBlend.Ease(arm.Weight, wanted ? 1f : 0f, aimBlendTime, Time.deltaTime);

            if (arm.Weight <= StillWeight || arm.Pointer == null) return;
            if (!Resolve(hand, arm)) return;

            Transform eye = view != null ? view.AimPivot : null;
            if (eye == null) return;

            Vector3 target = ArmAim.Convergence(eye.position, eye.forward, convergeRange);

            ArmAim.Point(arm.Upper, arm.Lower, arm.Pointer, target,
                         shoulderShare, maxSwing, arm.Weight, elbowPasses);
        }

        /// <summary>
        /// Find the two bones once, and say so loudly if the rig has no arm.
        ///
        /// <para>
        /// Loud for the reason the head look's bone lookup is: every other symptom of
        /// a missing bone here is a silence — the device is worn, the pose plays, the lamp is lit,
        /// and the only thing not happening is the aim. <c>GetBoneTransform</c> answers null on a
        /// model whose avatar has stopped being humanoid, which a re-export can do without a single
        /// console line.
        /// </para>
        /// </summary>
        private bool Resolve(ItemGrip.Hand hand, Arm arm)
        {
            if (arm.Resolved) return arm.Lower != null;

            arm.Resolved = true;

            if (!animator.isHuman)
            {
                Debug.LogError($"PlayerArmAim on '{name}': no humanoid Animator, so a worn device " +
                               "cannot be pointed. It will light wherever its clip leaves the arm.",
                               this);
                return false;
            }

            arm.Upper = animator.GetBoneTransform(hand == ItemGrip.Hand.Left
                ? HumanBodyBones.LeftUpperArm
                : HumanBodyBones.RightUpperArm);
            arm.Lower = animator.GetBoneTransform(hand == ItemGrip.Hand.Left
                ? HumanBodyBones.LeftLowerArm
                : HumanBodyBones.RightLowerArm);

            if (arm.Lower == null || arm.Upper == null)
            {
                Debug.LogError($"PlayerArmAim on '{name}': the avatar has no {hand} arm bones " +
                               "mapped, so a device worn there cannot be pointed.", this);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Drop the aim on the way out.
        ///
        /// Mirrors the aim rig standing its own blends down: nothing has to be RESTORED — the
        /// Animator rewrites both bones from the clip on the next frame — but a weight left at 1
        /// would have the arm snap to the crosshair the moment this is switched on again.
        ///
        /// The pointers are kept. They are registered once when the device is worn, and a component
        /// switched off and on again has nothing left to tell it what the arms are carrying.
        /// </summary>
        private void OnDisable()
        {
            foreach (Arm arm in arms)
                arm.Weight = 0f;
        }
    }
}
