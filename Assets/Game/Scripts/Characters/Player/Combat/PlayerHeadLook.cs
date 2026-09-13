// Where a player's head is pointed, on the bones, on every machine.
//
// Before this, looking around moved the CAMERA and nothing else. On your own machine that is
// invisible — you are behind the camera — but to everybody else you were a mannequin staring
// dead ahead while your view swept the cabin, and during the arrival, where four people sit in
// one cockpit watching each other for half a minute, that is the whole read of the scene
// (GDC-L1-ANIM-0003: animation is the channel state is communicated on).
//
// ── Why it is not the aim rig ──
// PlayerAimRig answers "what is the upper body doing because of the thing in the hand". A head
// turns whether or not anything is held, and it turns for a reason the hand knows nothing about.
// What IS shared is reused rather than rebuilt: the angles ride PlayerViewNetwork, the same
// channel that already carries pitch and lights remote torches, and they land on the same
// AimPivot the aim rig solves its hand IK against — so a seated player's weapon now points where
// their head does instead of down the body's forward.
//
// ── Why the bones are written in LateUpdate rather than through the IK pass ──
// The player's Upper Body avatar mask deliberately excludes the head (PlayerUpperBodySetup: an
// Upper Body layer at weight 1 would flatten the head of every death and damage clip on the Base
// Layer), so there is no masked layer here to hang a head goal on, and OnAnimatorIK only arrives
// for layers with their IK Pass ticked — a flag that is invisible in code and silently switchable
// in the controller. A world-space rotation laid on top of the evaluated pose needs neither, and
// composes with whatever the Base Layer was already doing to the head.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Turns the head and neck to where this player is looking, and keeps that answer in one place
    /// for the camera, the wire and the rig to share.
    ///
    /// <para>
    /// Runs on EVERY machine, like <see cref="PlayerStance"/> and <see cref="PlayerAimRig"/> and
    /// for the same reason: <c>PlayerController.DisablePlayer</c> switches input, movement and look
    /// off on every remote copy, so a component that only ran for the owner would leave every other
    /// player's head locked forward. The owner decides; everyone else replays what
    /// <see cref="PlayerViewNetwork"/> hands them.
    /// </para>
    ///
    /// <para>
    /// Nothing here is saved. The head angle is a fact about this frame's input, not state — a
    /// player who quits looking over their shoulder comes back facing wherever their body faces,
    /// and their PITCH, which is the half worth keeping, is already persisted by
    /// <see cref="PlayerLook.RestorePitch"/>.
    /// </para>
    /// </summary>
    // The LAST thing to touch these bones. MountedRiderPose writes the rider's spine and chest at
    // 900, and the neck and head hang off those — a head posed before it would be dragged off its
    // aim by its own parent a moment later. Nothing writes the neck or the head itself, so this is
    // the single writer for both, as the transform-ownership rule requires.
    [DefaultExecutionOrder(950)]
    [DisallowMultipleComponent]
    public class PlayerHeadLook : MonoBehaviour
    {
        /// <summary>Below this the look is treated as straight ahead and the bones are left alone.</summary>
        private const float StillDegrees = 0.01f;

        [Header("Neck limits")]
        [Tooltip("Degrees the head may turn either side of the body. A NECK limit, not a camera " +
                 "limit: past roughly this the skin shears at the collar and the helmet reads as " +
                 "having come loose. Only spent while seated — on foot the body carries the turn.")]
        [SerializeField] private float yawClamp = 80f;

        [Tooltip("Degrees of chin-to-chest. This is what makes looking down read as bowing the head.")]
        [SerializeField] private float lookDownClamp = 60f;

        [Tooltip("Degrees of chin-up. More than down, which is the way a real neck is built.")]
        [SerializeField] private float lookUpClamp = 70f;

        [Tooltip("How much of the turn the NECK takes; the head takes the rest. All of it on the " +
                 "head alone reads as a doll's head swivelling on a fixed body.")]
        [Range(0f, 1f)]
        [SerializeField] private float neckShare = 0.45f;

        private Animator animator;
        private PlayerLook look;
        private PlayerViewNetwork view;

        private Transform neckBone;
        private Transform headBone;
        private bool bonesResolved;

        private float yaw;
        private float pitch;

        /// <summary>
        /// Which half of the look the neck is carrying. Set by whoever has taken the body's ability
        /// to turn — and put back by them, which is why it is a property and not a latch.
        ///
        /// <para>
        /// Changing it clears the yaw: a player who stands up out of a seat with their head turned
        /// 80° would otherwise keep it there, because <see cref="HeadAimMode.Free"/> stops WRITING
        /// yaw rather than winding the old value back.
        /// </para>
        /// </summary>
        public HeadAimMode Mode
        {
            get => mode;
            set
            {
                if (mode == value) return;
                mode = value;
                yaw = 0f;
            }
        }

        private HeadAimMode mode = HeadAimMode.Free;

        /// <summary>Head yaw relative to the body, in degrees. Zero on foot, by construction.</summary>
        public float Yaw => yaw;

        /// <summary>Head pitch, in degrees. Positive is down, as everywhere else in the player.</summary>
        public float Pitch => pitch;

        /// <summary>
        /// The look as a rotation in the body's frame, for anything that has to point the same way
        /// the head does — the seated camera, above all. Sharing this instead of integrating a
        /// second copy is what guarantees the view cannot drift off the head it rides.
        /// </summary>
        public Quaternion LookRotation => HeadAim.Local(yaw, pitch);

        private HeadAim.Limits Limits => new(yawClamp, lookDownClamp, lookUpClamp);

        private void Awake()
        {
            look = GetComponent<PlayerLook>();
            view = GetComponent<PlayerViewNetwork>();

            // Included-inactive, for the reason PlayerAimRig gives: on a remote copy
            // PlayerController has already switched parts of this character off, and the Animator
            // is still the one whose bones have to move.
            animator = GetComponentInChildren<Animator>(true);
        }

        /// <summary>
        /// Move the look by a mouse delta, already scaled to degrees by whoever read the input.
        ///
        /// <para>
        /// Pushed in rather than read here on purpose. A seated view is one component reading one
        /// input action and writing one camera in one LateUpdate — see
        /// <see cref="SpaceGame.Presentation.ArrivalCameraRig"/> — and a second reader of the same
        /// action would double every mouse movement while the two clamps quietly disagreed.
        /// </para>
        /// </summary>
        public void AddLook(float yawDegrees, float pitchDegrees)
        {
            HeadAim.Limits limits = Limits;

            yaw = HeadAim.Yaw(yaw + yawDegrees, mode, limits);
            pitch = HeadAim.Pitch(pitch + pitchDegrees, limits);
        }

        // Update rather than LateUpdate for the on-foot case, and ordered after PlayerLook: the
        // pitch published to everyone else this frame is then the one the player is actually
        // looking along, not last frame's. A seated look arrives later than this (the camera rig
        // runs in LateUpdate) and so reaches the wire one frame behind — invisible at any frame
        // rate, and the alternative is a second component racing the camera for the input.
        private void Update()
        {
            if (!Network.Owns(this)) return;
            if (mode != HeadAimMode.Free) return;

            HeadAim.Limits limits = Limits;

            yaw = HeadAim.Yaw(yaw, mode, limits);
            pitch = HeadAim.Pitch(look != null ? look.Pitch : pitch, limits);
        }

        private void LateUpdate()
        {
            // A remote copy has no input and no PlayerLook running; its whole answer arrives over
            // the wire already clamped by the owner, so it is replayed rather than re-decided. That
            // also means the MODE is an owner-side concept only — what travels is the result.
            if (!Network.Owns(this) && view != null)
            {
                yaw = view.HeadYaw;
                pitch = view.HeadPitch;
            }

            ApplyToBones();
        }

        /// <summary>
        /// Lay the look on top of the pose the Animator has already evaluated this frame.
        ///
        /// <para>
        /// Nothing is restored on the way out, unlike a camera rig: the Animator rewrites both
        /// bones from the clip every frame, so a disabled head look is back on the animated pose on
        /// the next one without anybody having to remember the old value.
        /// </para>
        /// <para>
        /// That is also why a switched-off Animator means switched-off head look. `RagdollRig`
        /// disables it and hands every bone to physics; a rotation written on top of a ragdoll's
        /// head is not laid over a pose, it is a second driver fighting a joint, and the corpse's
        /// head would twitch back to the player's last look angle every frame.
        /// </para>
        /// </summary>
        private void ApplyToBones()
        {
            if (animator == null || !animator.enabled) return;
            if (!ResolveBones()) return;
            if (Mathf.Abs(yaw) < StillDegrees && Mathf.Abs(pitch) < StillDegrees) return;

            Quaternion delta = HeadAim.Delta(yaw, pitch, transform.up, transform.right);

            // Neck first and head second, and the head's rotation read back in between: the head is
            // a child, so by the time it is written it already carries the neck's share and only
            // needs the remainder. Reading it before moving the neck would apply the neck's share
            // twice.
            if (neckBone != null)
            {
                neckBone.rotation = HeadAim.Share(delta, neckShare) * neckBone.rotation;
                headBone.rotation = HeadAim.Share(delta, 1f - neckShare) * headBone.rotation;
                return;
            }

            headBone.rotation = delta * headBone.rotation;
        }

        /// <summary>
        /// Find the two bones once, and say so loudly if the rig has no head.
        ///
        /// <para>
        /// Loud because every other symptom of this is silence: the angles keep updating, the
        /// camera keeps turning, the wire keeps carrying the yaw, and the only thing missing is the
        /// one thing this component exists to do. <c>GetBoneTransform</c> answers null on a model
        /// whose avatar has stopped being humanoid — which a re-export can do without a single
        /// console line (see the ArtPipeline doc's avatar gotcha).
        /// </para>
        /// </summary>
        private bool ResolveBones()
        {
            if (bonesResolved) return headBone != null;

            bonesResolved = true;

            if (animator == null || !animator.isHuman)
            {
                Debug.LogError($"PlayerHeadLook on '{name}': no humanoid Animator, so the head " +
                               "cannot be aimed. The view will still turn and other players will " +
                               "see a character staring straight ahead.", this);
                return false;
            }

            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);

            if (headBone == null)
            {
                Debug.LogError($"PlayerHeadLook on '{name}': the avatar has no Head bone mapped.",
                               this);
                return false;
            }

            return true;
        }
    }
}
