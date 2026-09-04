// What the upper body is doing because of the thing in the player's hand.
//
// Two jobs, and they are one concern rather than two: keeping a held item up where it can be seen,
// and bringing a gauntlet arm up in front of the eye while it fires. Both are expressed through the
// same masked Upper Body layer, and separating them would mean two components writing one weight.
//
// Runs on EVERY machine, like PlayerStance and for the same reason: PlayerController.DisablePlayer
// switches the movement and input components off on remote copies, so a rig that only ran for the
// owner would leave every other player's arms hanging at their sides. Both of its inputs — the held
// style and the gauntlet raise — are pushed in from components that already run everywhere.
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Characters
{
    [DisallowMultipleComponent]
    public class PlayerAimRig : MonoBehaviour
    {
        /// <summary>Int parameter the Upper Body layer's Any State transitions compare against.</summary>
        private const string HoldStyleParameter = "HoldStyle";

        /// <summary>Name of the masked layer this component owns outright.</summary>
        private const string UpperBodyLayer = "Upper Body";

        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Hold")]
        [Tooltip("Seconds for the upper-body pose to fade in when an item is equipped and out " +
                 "when it is put away. 0 snaps.")]
        [SerializeField] private float holdBlendTime = 0.18f;

        [Header("Gauntlet raise")]
        [Tooltip("Seconds for a gauntlet arm to come up when its item fires, and to drop after.")]
        [SerializeField] private float raiseBlendTime = 0.12f;

        [Tooltip("Look pitch, in degrees, at which the raised arm reaches the Up and Down clips " +
                 "of its blend tree. The clips are authored at roughly +49 and -27 degrees of " +
                 "forearm elevation; this is the look pitch that maps onto them.")]
        [SerializeField, Min(1f)] private float raisePitchRange = 45f;

        private PlayerController controller;
        private PlayerViewNetwork view;

        /// <summary>Int the Upper Body layer's raise states are entered on: 0 none, 1 left, 2 right, 3 both.</summary>
        private const string ArmRaiseParameter = "ArmRaise";

        /// <summary>Float the raise states blend on: the look pitch in degrees, up positive.</summary>
        private const string AimPitchParameter = "AimPitch";

        private int upperBodyLayerIndex = -1;
        private int holdStyleHash;
        private int armRaiseHash;
        private int aimPitchHash;

        private ItemGrip.HoldStyle heldStyle = ItemGrip.HoldStyle.None;
        private ItemGrip.HoldStyle torchStyle = ItemGrip.HoldStyle.None;
        private float holdT;

        // One raise per arm: the decision, and its blend.
        private bool raiseLeft;
        private bool raiseRight;
        private float raiseLeftT;
        private float raiseRightT;

        /// <summary>
        /// What is in the hand right now, or <see cref="ItemGrip.HoldStyle.None"/> for empty.
        /// The value <see cref="HoldAnimator"/> last pushed in.
        /// </summary>
        public ItemGrip.HoldStyle HeldStyle => heldStyle;

        /// <summary>
        /// The pose the body is actually in: what is in the hand, or — with empty hands — whatever
        /// a lit torch asked for.
        ///
        /// <para>
        /// A held item wins, and it wins for free rather than through a rule: something in the
        /// hand is a better answer to "what are the arms doing" than a lamp on the wrist, and both
        /// hands are on it anyway.
        /// </para>
        /// </summary>
        private ItemGrip.HoldStyle EffectiveStyle =>
            heldStyle != ItemGrip.HoldStyle.None ? heldStyle : torchStyle;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            view = GetComponent<PlayerViewNetwork>();

            // Included-inactive: on a remote copy PlayerController.Awake has already switched
            // parts of this character off, and the Animator is still the one we must drive.
            if (animator == null) animator = GetComponentInChildren<Animator>(true);

            holdStyleHash = Animator.StringToHash(HoldStyleParameter);
            armRaiseHash = Animator.StringToHash(ArmRaiseParameter);
            aimPitchHash = Animator.StringToHash(AimPitchParameter);

            if (animator == null) return;

            upperBodyLayerIndex = animator.GetLayerIndex(UpperBodyLayer);

            // Loud, because everything else about this component will look like it is working:
            // the blend runs, the parameters are written, and nothing appears on screen.
            if (upperBodyLayerIndex < 0)
                Debug.LogError($"PlayerAimRig on '{name}': the Animator has no '{UpperBodyLayer}' " +
                               "layer. Run Tools/SpaceGame/Player/Build Upper Body Layer.", this);
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
        /// Bring one forearm up in front of the eye, or let it drop. Called by
        /// <c>BodyEquipmentController</c> while the gauntlet on that arm is firing — on every
        /// machine, since the use is presented on every machine — so a peer sees the same arm come
        /// up that the wearer does.
        /// </summary>
        public void RaiseArm(ItemGrip.Hand hand, bool raised)
        {
            if (hand == ItemGrip.Hand.Left) raiseLeft = raised;
            else raiseRight = raised;
        }

        /// <summary>
        /// Bring the body into a hold pose because a lit torch wants the arm up, or let it drop.
        ///
        /// <para>
        /// <see cref="ItemGrip.HoldStyle.None"/> releases it. Called by
        /// <see cref="SpaceGame.Items.FlashlightGauntletArtifact"/> off <c>Flashlight.Switched</c>,
        /// so it follows the lamp on every machine — the wearer switching it, a peer being told by
        /// <c>netTorch</c>, or a save restore — and a peer sees the same posture with nothing extra
        /// on the wire.
        /// </para>
        /// <para>
        /// This reuses the pose the body already takes for a HELD item rather than a pose of its
        /// own. The gauntlet is on the forearm and the beam leaves along it, so what the torch
        /// needs is exactly what holding something needs: the forearm up and forward, pitching
        /// with the look. A bespoke set of clips was built for it first and thrown away — this is
        /// the same shape for none of the assets.
        /// </para>
        /// </summary>
        public void SetTorchStyle(ItemGrip.HoldStyle style)
        {
            torchStyle = style;
        }

        private void Update()
        {
            Blend(Time.deltaTime);
            WriteAnimator();
        }

        /// <summary>
        /// Stand the body down to a plain idle, whatever it is holding.
        ///
        /// <para>
        /// Set while the gear screen is open. That screen is a camera flown round to look AT the
        /// character, so what it shows has to be the character rather than a pose left over from
        /// what they happened to be doing when they pressed I — arms out around a rifle, an arm up
        /// mid-gauntlet, a torch held forward. The gear is the subject; the astronaut is the stand
        /// it sits on, and it should be still.
        /// </para>
        /// <para>
        /// Blends off through the same ease as everything else here rather than snapping, and is
        /// deliberately NOT the same thing as the death rule below: a corpse must never come back
        /// to a pose, whereas this hands it straight back on exit.
        /// </para>
        /// </summary>
        public bool Relaxed { get; set; }

        private void Blend(float deltaTime)
        {
            // The pose comes off entirely while dead, whatever is in the hand. The death clip runs
            // on the Base Layer, and an Upper Body layer left at weight 1 would override its arms
            // and leave the corpse holding its rifle out in front of it.
            bool posed = heldStyle != ItemGrip.HoldStyle.None
                         && !Relaxed
                         && (controller == null || !controller.IsDead);

            holdT = PoseBlend.Ease(holdT, posed ? 1f : 0f, holdBlendTime, deltaTime);

            // A raised gauntlet arm, dead or not: the corpse rule above applies to it too.
            bool alive = !Relaxed && (controller == null || !controller.IsDead);
            raiseLeftT = PoseBlend.Ease(raiseLeftT, raiseLeft && alive ? 1f : 0f, raiseBlendTime, deltaTime);
            raiseRightT = PoseBlend.Ease(raiseRightT, raiseRight && alive ? 1f : 0f, raiseBlendTime, deltaTime);
        }

        private void WriteAnimator()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (upperBodyLayerIndex < 0) return;

            // A raised arm needs the layer up even when nothing is held and the hold pose is off.
            animator.SetLayerWeight(upperBodyLayerIndex, Mathf.Max(holdT, Mathf.Max(raiseLeftT, raiseRightT)));

            // The lit torch's style is written into the same parameter the hand uses, so a torch
            // pose and a held item cannot both be on: there is one pose and one state machine.
            animator.SetInteger(holdStyleHash, (int)EffectiveStyle);

            // The raise is a state on the same layer — three pointing clips blended on the look
            // pitch — not an IK goal: the layer sits in Empty whenever the hands are empty, which
            // is the ordinary case for a player wearing gauntlets, and IK set on an empty layer
            // moves nothing. The clips give it something to play. Pitch comes off AimPivot, which
            // is the owner's live pitch here and their replicated pitch on every other machine.
            animator.SetInteger(armRaiseHash, (raiseLeft ? 1 : 0) | (raiseRight ? 2 : 0));
            animator.SetFloat(aimPitchHash, LookPitch());
        }

        /// <summary>The look pitch in degrees, up positive, clamped to the blend tree's range.</summary>
        private float LookPitch()
        {
            Transform eye = view != null ? view.AimPivot : null;
            if (eye == null) return 0f;

            float pitch = Mathf.Asin(Mathf.Clamp(eye.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            return Mathf.Clamp(pitch, -raisePitchRange, raisePitchRange);
        }

        /// <summary>
        /// Put the arm down on the way out.
        ///
        /// Mirrors PlayerStance.OnDisable: a component switched off mid-pose leaves a layer weight
        /// on a rig with nothing still running to clear it.
        /// </summary>
        private void OnDisable()
        {
            holdT = 0f;
            raiseLeft = false;
            raiseRight = false;
            raiseLeftT = 0f;
            raiseRightT = 0f;
            torchStyle = ItemGrip.HoldStyle.None;
            heldStyle = ItemGrip.HoldStyle.None;
            WriteAnimator();
        }
    }
}
