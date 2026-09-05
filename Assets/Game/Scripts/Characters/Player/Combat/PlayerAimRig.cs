// What the upper body is doing because of the thing in the player's hand.
//
// Two jobs, and they are one concern rather than two: keeping the held item up where it can be
// seen, and bringing it to the eye when the player aims. Both are expressed through the same
// masked Upper Body layer, and separating them would mean two components writing one layer weight.
//
// Runs on EVERY machine, like PlayerStance and for the same reason: PlayerController.DisablePlayer
// switches the movement and input components off on remote copies, so a rig that only ran for the
// owner would leave every other player's arms hanging at their sides. On the owner the decision
// comes from input; everywhere else it is read back out of PlayerViewNetwork.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Characters
{
    [DisallowMultipleComponent]
    public class PlayerAimRig : MonoBehaviour
    {
        /// <summary>Int parameter the Upper Body layer's Any State transitions compare against.</summary>
        private const string HoldStyleParameter = "HoldStyle";

        /// <summary>Bool the aim states are entered on. Already existed for NPCs.</summary>
        private const string AimingParameter = "IsAiming";

        /// <summary>Name of the masked layer this component owns outright.</summary>
        private const string UpperBodyLayer = "Upper Body";

        /// <summary>
        /// Bool held true while a one-shot gesture is playing on the Upper Body layer.
        ///
        /// The layer's Any State transitions are driven by <see cref="HoldStyleParameter"/> and are
        /// continuously true — with empty hands "HoldStyle Equals 0" sends it to Empty on every
        /// frame — so a gesture entered by a trigger is thrown straight back out again. Every one
        /// of those transitions also requires this to be false.
        /// </summary>
        private const string GesturingParameter = "Gesturing";

        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Hold")]
        [Tooltip("Seconds for the upper-body pose to fade in when an item is equipped and out " +
                 "when it is put away. 0 snaps.")]
        [SerializeField] private float holdBlendTime = 0.18f;

        [Header("Gesture")]
        [Tooltip("Seconds a one-shot gesture keeps the Upper Body layer up after it starts. " +
                 "Set from the clip length by whoever plays it; this is only the fallback.")]
        [SerializeField] private float defaultGestureSeconds = 2.5f;

        [Header("Aim")]
        [Tooltip("Seconds to bring the item all the way to the eye and back down.")]
        [SerializeField] private float aimBlendTime = 0.15f;

        [Tooltip("Where the hand is pulled to while aiming, in the EYE's frame, in metres. " +
                 "+Z is where you are looking, +X is right, +Y is up. Roughly a third of a metre " +
                 "forward and a little right and down puts the item under the crosshair without " +
                 "the fist covering it.")]
        [SerializeField] private Vector3 aimHandOffset = new Vector3(0.06f, -0.05f, 0.34f);

        [Tooltip("Where the elbow is pushed, in the BODY's frame, in metres. Out and down, or " +
                 "the solver is free to fold the arm the wrong way when you look up.")]
        [SerializeField] private Vector3 elbowPush = new Vector3(0.30f, -0.28f, -0.05f);

        private PlayerController controller;
        private PlayerViewNetwork view;
        private PlayerInputManager inputs;
        private EquipmentController equipment;

        private int upperBodyLayerIndex = -1;
        private int holdStyleHash;
        private int aimingHash;
        private int gesturingHash;

        // Counts down while a gesture plays. Holds the masked layer up so the gesture is visible
        // with empty hands, which is the whole point: you pet an animal with a free hand, and the
        // layer is otherwise only raised by holding an item.
        private float gestureTimer;

        private ItemGrip.HoldStyle heldStyle = ItemGrip.HoldStyle.None;
        private float holdT;
        private float aimT;
        private bool aiming;

        /// <summary>Aiming right now, on any machine — the owner's decision or their replica of it.</summary>
        public bool IsAiming => aiming;

        /// <summary>How far into the aim, 0 to 1. Read by movement and look sensitivity.</summary>
        public float AimBlend => aimT;

        /// <summary>
        /// What is in the hand right now, or <see cref="ItemGrip.HoldStyle.None"/> for empty.
        /// The value <see cref="HoldAnimator"/> last pushed in.
        /// </summary>
        public ItemGrip.HoldStyle HeldStyle => heldStyle;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            view = GetComponent<PlayerViewNetwork>();
            equipment = GetComponent<EquipmentController>();

            // Included-inactive: on a remote copy PlayerController.Awake has already switched
            // parts of this character off, and the Animator is still the one we must drive.
            if (animator == null) animator = GetComponentInChildren<Animator>(true);

            holdStyleHash = Animator.StringToHash(HoldStyleParameter);
            aimingHash = Animator.StringToHash(AimingParameter);
            gesturingHash = Animator.StringToHash(GesturingParameter);

            if (animator == null) return;

            upperBodyLayerIndex = animator.GetLayerIndex(UpperBodyLayer);

            if (upperBodyLayerIndex < 0)
            {
                // Loud, because everything else about this component will look like it is working:
                // the blend runs, the parameters are written, and nothing appears on screen.
                Debug.LogError($"PlayerAimRig on '{name}': the Animator has no '{UpperBodyLayer}' " +
                               "layer. Run Tools/SpaceGame/Player/Build Upper Body Layer.", this);
                return;
            }

            var relay = animator.gameObject.GetComponent<AimIkRelay>();
            if (relay == null) relay = animator.gameObject.AddComponent<AimIkRelay>();
            relay.Bind(this);
        }

        /// <summary>
        /// Called by <see cref="HoldAnimator"/> when an item is picked up or put away.
        /// <see cref="ItemGrip.HoldStyle.None"/> means empty-handed.
        /// </summary>
        public void SetHeldStyle(ItemGrip.HoldStyle style)
        {
            heldStyle = style;
        }

        /// <summary>
        /// Play a one-shot on the masked Upper Body layer, and hold the layer up while it runs.
        ///
        /// <para>
        /// Routed through this component rather than set on the Animator directly because this
        /// component owns the layer weight — it writes it every frame from <c>holdT</c>, so a
        /// trigger fired from outside plays a clip on a layer weighted 0 and nothing appears.
        /// </para>
        /// </summary>
        public void PlayGesture(string trigger, float seconds = 0f)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (string.IsNullOrEmpty(trigger)) return;

            gestureTimer = Mathf.Max(gestureTimer, seconds > 0f ? seconds : defaultGestureSeconds);
            animator.SetBool(gesturingHash, true);
            animator.SetTrigger(trigger);
        }

        private void Update()
        {
            if (gestureTimer > 0f)
                gestureTimer -= Time.deltaTime;

            DecideAiming();
            Blend(Time.deltaTime);
            WriteAnimator();
        }

        /// <summary>
        /// Owner decides, everyone else copies.
        ///
        /// <para>
        /// The owner's test is the same "is this player actually driving their own body" question
        /// PlayerStance.DecideStance asks, so death, mounts, cutscenes and menus all lower the
        /// weapon without any of them having to know this component exists.
        /// </para>
        /// </summary>
        private void DecideAiming()
        {
            if (!Network.Owns(this))
            {
                aiming = view != null && view.Aiming;
                return;
            }

            if (inputs == null && controller != null) inputs = controller.Input;

            bool driving = inputs != null && inputs.enabled
                           && controller != null && !controller.IsDead && !controller.InCutsceneMode;

            aiming = driving && inputs.AimHeld && heldStyle != ItemGrip.HoldStyle.None;

            if (view != null) view.PublishAiming(aiming);
        }

        private void Blend(float deltaTime)
        {
            // The pose comes off entirely while dead, whatever is in the hand. The death clip runs
            // on the Base Layer, and an Upper Body layer left at weight 1 would override its arms
            // and leave the corpse holding its rifle out in front of it.
            bool posed = (heldStyle != ItemGrip.HoldStyle.None || gestureTimer > 0f)
                         && (controller == null || !controller.IsDead);

            holdT = AimPose.Ease(holdT, posed ? 1f : 0f, holdBlendTime, deltaTime);
            aimT = AimPose.Ease(aimT, aiming ? 1f : 0f, aimBlendTime, deltaTime);

            // Aim can never exceed the pose it is layered on. Without this an item equipped while
            // the aim button is already down snaps the hand to the eye before the arm has come up.
            aimT = Mathf.Min(aimT, holdT);
        }

        private void WriteAnimator()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (upperBodyLayerIndex < 0) return;

            animator.SetLayerWeight(upperBodyLayerIndex, holdT);
            animator.SetInteger(holdStyleHash, (int)heldStyle);
            animator.SetBool(aimingHash, aiming);
            animator.SetBool(gesturingHash, gestureTimer > 0f);
        }

        /// <summary>
        /// The IK pass, forwarded from <see cref="AimIkRelay"/>.
        ///
        /// <para>
        /// The target is anchored on <see cref="PlayerViewNetwork.AimPivot"/> rather than on the
        /// camera, and that is the whole reason remote players aim correctly: the pivot carries the
        /// owner's live pitch on their own machine and their replicated pitch on everybody else's,
        /// while the camera exists only for the local player and is switched off on every remote
        /// copy. Reading the camera here would have aimed every other player's weapon at whatever
        /// the local player happened to be looking at.
        /// </para>
        /// </summary>
        public void ApplyIk(int layerIndex)
        {
            if (layerIndex != upperBodyLayerIndex) return;
            if (animator == null || aimT <= 0.001f) return;

            Transform eye = view != null ? view.AimPivot : null;
            if (eye == null) return;

            Vector3 goal = AimPose.HandGoal(eye.position, eye.rotation, aimHandOffset);

            Quaternion grip = equipment != null
                ? equipment.MainHandGripLocalRotation
                : Quaternion.identity;

            Quaternion goalRotation = AimPose.HandRotationForItem(eye.rotation, grip);

            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, aimT);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, aimT);
            animator.SetIKPosition(AvatarIKGoal.RightHand, goal);
            animator.SetIKRotation(AvatarIKGoal.RightHand, goalRotation);

            Transform shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (shoulder == null) return;

            animator.SetIKHintPosition(AvatarIKHint.RightElbow,
                AimPose.ElbowHint(shoulder.position, goal, transform.rotation, elbowPush));
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, aimT);
        }

        /// <summary>
        /// Put the arm down on the way out.
        ///
        /// Mirrors PlayerStance.OnDisable: a component switched off mid-aim leaves a layer weight
        /// and an IK goal on a rig with nothing still running to clear them.
        /// </summary>
        private void OnDisable()
        {
            aiming = false;
            holdT = 0f;
            aimT = 0f;
            heldStyle = ItemGrip.HoldStyle.None;
            WriteAnimator();
        }
    }
}
