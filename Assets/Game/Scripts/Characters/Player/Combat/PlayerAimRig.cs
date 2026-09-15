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
        // Public: HoldAnimator drives the same parameter and layer on an NPC that wears this
        // controller without a rig, so the names live in one place.
        public const string HoldStyleParameter = "HoldStyle";

        /// <summary>
        /// Bool selecting the mirrored copy of whichever hold state <see cref="HoldStyleParameter"/>
        /// names. Every hold clip is right-handed — the one-handed pose puts the RIGHT hand forward
        /// and leaves the left at the hip — so a pose struck for something on the LEFT arm has to
        /// be the mirror of it or the wrong arm comes up.
        /// </summary>
        private const string HoldMirrorParameter = "HoldMirror";

        /// <summary>Name of the masked layer this component owns outright.</summary>
        public const string UpperBodyLayer = "Upper Body";

        /// <summary>
        /// Name of the second masked layer, which covers the LEFT arm alone.
        ///
        /// <para>
        /// It exists because the layer above answers exactly one ask. A player wearing a working
        /// device on each forearm — a lit torch and a powered scanner — makes two, and the right
        /// one used to win outright: the left arm hung at the side with its lamp lighting the
        /// ground, which is the device's own on/off state reported wrongly (GDC-L1-ANIM-0003).
        /// This layer carries the left arm's pose for that case and only that case; see
        /// <see cref="LeftArmStyle"/>.
        /// </para>
        /// </summary>
        public const string WornLeftLayer = "Worn Left";

        /// <summary>Int parameter the Worn Left layer's Any State transitions compare against.</summary>
        private const string WornLeftStyleParameter = "WornLeftStyle";

        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Hold")]
        [Tooltip("Seconds for the upper-body pose to fade in when an item is equipped and out " +
                 "when it is put away. 0 snaps.")]
        [SerializeField] private float holdBlendTime = 0.18f;

        [Header("Gauntlet raise")]
        [Tooltip("Seconds a one-shot gesture keeps the Upper Body layer up after it starts. Set from the clip length by whoever plays it; this is only the fallback.")]
        [SerializeField] private float defaultGestureSeconds = 2.5f;

        [Tooltip("Seconds for a gauntlet arm to come up when its item fires, and to drop after.")]
        [SerializeField] private float raiseBlendTime = 0.12f;

        [Tooltip("Look pitch, in degrees, at which the raised arm reaches the Up and Down clips " +
                 "of its blend tree. The clips are authored at roughly +49 and -27 degrees of " +
                 "forearm elevation; this is the look pitch that maps onto them.")]
        [SerializeField, Min(1f)] private float raisePitchRange = 45f;

        private PlayerController controller;
        private PlayerViewNetwork view;

        /// <summary>Int the Upper Body layer's raise states are entered on: 0 none, 1 left, 2 right, 3 both.</summary>
        // Held true while a one-shot gesture plays, so the Upper Body layer's states can gate on
        // it and an AnyState transition cannot evict the gesture mid-play.
        private const string GesturingParameter = "Gesturing";

        private const string ArmRaiseParameter = "ArmRaise";

        /// <summary>Float the raise states blend on: the look pitch in degrees, up positive.</summary>
        private const string AimPitchParameter = "AimPitch";

        private int upperBodyLayerIndex = -1;
        private int wornLeftLayerIndex = -1;
        private int holdStyleHash;
        private int holdMirrorHash;
        private int wornLeftStyleHash;
        private int armRaiseHash;
        private int aimPitchHash;

        private ItemGrip.HoldStyle heldStyle = ItemGrip.HoldStyle.None;

        // One per arm, because the pose is not symmetric: a device on the left forearm needs the
        // mirror of the pose one on the right needs, and a player may wear both.
        private ItemGrip.HoldStyle wornRight = ItemGrip.HoldStyle.None;
        private ItemGrip.HoldStyle wornLeft = ItemGrip.HoldStyle.None;
        private float holdT;

        // One raise per arm: the decision, and its blend.
        private bool raiseLeft;
        private bool raiseRight;
        private int gesturingHash;

        // Counts down while a gesture plays. Holds the masked layer up so the gesture is visible
        // with EMPTY hands, which is the whole point: you pet an animal with a free hand, and the
        // layer is otherwise only raised by holding an item or by a gauntlet firing.
        private float gestureTimer;

        // Which arm the running gesture plays on, when the gesture said. Null means the hold
        // pose's own rule (PoseMirrored). Lives only as long as gestureTimer does.
        private bool? gestureMirror;

        private float raiseLeftT;
        private float raiseRightT;

        // The Worn Left layer's weight, eased through the same ramp the hold pose uses so the
        // second arm comes up at the same speed as the first rather than snapping beside it.
        private float wornLeftT;

        /// <summary>
        /// What is in the hand right now, or <see cref="ItemGrip.HoldStyle.None"/> for empty.
        /// The value <see cref="HoldAnimator"/> last pushed in.
        /// </summary>
        public ItemGrip.HoldStyle HeldStyle => heldStyle;

        /// <summary>
        /// The pose the body is actually in: what is in the hand, or — with empty hands — whatever
        /// a switched-on worn gauntlet asked for.
        ///
        /// <para>
        /// A held item wins, and it wins for free rather than through a rule: something in the
        /// hand is a better answer to "what are the arms doing" than a device on the wrist, and
        /// both hands are on it anyway. Between two working gauntlets the right one takes THIS
        /// layer — there is one pose here and it faces one way — and the left one is answered on
        /// its own layer instead; see <see cref="LeftArmStyle"/>.
        /// </para>
        /// </summary>
        public ItemGrip.HoldStyle PoseStyle =>
            heldStyle != ItemGrip.HoldStyle.None ? heldStyle :
            wornRight != ItemGrip.HoldStyle.None ? wornRight : wornLeft;

        /// <summary>
        /// Whether that pose is played mirrored — true only when the LEFT arm is the one that
        /// asked for it.
        ///
        /// <para>
        /// Every hold clip is right-handed. Measured off the assets: the one-handed pose
        /// (<c>HumanM@Gun_Aim01</c>) puts the right hand 0.19 up and 0.19 forward of the body
        /// centre and leaves the left one at the hip, so a gauntlet on the left forearm played
        /// unmirrored raises the empty arm — the pose comes on, and it comes on for the wrong arm,
        /// which is worse than no pose because it looks deliberate.
        /// </para>
        /// <para>
        /// A held item is never mirrored. The off hand grips an item without the body turning
        /// round it, and mirroring the pose for it would swap which shoulder every two-handed item
        /// is braced against.
        /// </para>
        /// </summary>
        public bool PoseMirrored =>
            heldStyle == ItemGrip.HoldStyle.None &&
            wornRight == ItemGrip.HoldStyle.None &&
            wornLeft != ItemGrip.HoldStyle.None;

        /// <summary>
        /// What the LEFT arm's own layer plays — the left device's pose, and only while the main
        /// layer is already busy with the right one.
        ///
        /// <para>
        /// A left device alone is not this case: it takes the main layer mirrored, which poses the
        /// chest with it and reads better than an arm moving on its own. This exists for the arm
        /// the main layer cannot reach — one pose there, two working devices — and answering it on
        /// a left-arm mask is the only way both can be up at once.
        /// </para>
        /// <para>
        /// A held item stops it, exactly as it stops the worn pose on the main layer: both hands
        /// are on the item, and pulling one off it for a wrist device would break the grip the
        /// held pose exists to show.
        /// </para>
        /// </summary>
        public ItemGrip.HoldStyle LeftArmStyle =>
            heldStyle == ItemGrip.HoldStyle.None &&
            wornRight != ItemGrip.HoldStyle.None &&
            wornLeft != ItemGrip.HoldStyle.None
                ? wornLeft
                : ItemGrip.HoldStyle.None;

        /// <summary>
        /// Whether the masked layer should be carrying a pose at all this frame — the thing the
        /// layer WEIGHT is eased towards.
        ///
        /// <para>
        /// It reads <see cref="PoseStyle"/> and not <c>heldStyle</c>, and that is the whole of
        /// ANIM-01: a lit torch used to write its style into the animator while the weight stayed
        /// at zero, so the state machine dutifully entered the pose on a layer nobody could see.
        /// Every symptom of that is a silence — no error, the right parameter, the right state,
        /// and an arm hanging at the player's side.
        /// </para>
        /// </summary>
        public bool Posing =>
            (PoseStyle != ItemGrip.HoldStyle.None || gestureTimer > 0f)
            && !Relaxed
            && (controller == null || !controller.IsDead);

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            view = GetComponent<PlayerViewNetwork>();

            // Added here rather than authored on the prefab, exactly as PlayerViewNetwork adds the
            // head look: this component decides WHICH arm is posed, so it is the one that must be
            // sure something is pointing it — on remote copies too, and without a prefab edit that a
            // re-export or a rebuilt player could lose.
            if (GetComponent<PlayerArmAim>() == null) gameObject.AddComponent<PlayerArmAim>();

            // Included-inactive: on a remote copy PlayerController.Awake has already switched
            // parts of this character off, and the Animator is still the one we must drive.
            if (animator == null) animator = GetComponentInChildren<Animator>(true);

            holdStyleHash = Animator.StringToHash(HoldStyleParameter);
            holdMirrorHash = Animator.StringToHash(HoldMirrorParameter);
            wornLeftStyleHash = Animator.StringToHash(WornLeftStyleParameter);
            gesturingHash = Animator.StringToHash(GesturingParameter);
            armRaiseHash = Animator.StringToHash(ArmRaiseParameter);
            aimPitchHash = Animator.StringToHash(AimPitchParameter);

            if (animator == null) return;

            upperBodyLayerIndex = animator.GetLayerIndex(UpperBodyLayer);
            wornLeftLayerIndex = animator.GetLayerIndex(WornLeftLayer);

            // Loud, because everything else about this component will look like it is working:
            // the blend runs, the parameters are written, and nothing appears on screen.
            if (upperBodyLayerIndex < 0)
                Debug.LogError($"PlayerAimRig on '{name}': the Animator has no '{UpperBodyLayer}' " +
                               "layer. Run Tools/SpaceGame/Player/Build Upper Body Layer.", this);

            // Equally loud and for the same reason, but on its own line: a rig with the main layer
            // and without this one works for everything except the one case this layer exists for,
            // and that case is two worn devices at once — easy to miss and impossible to diagnose
            // from what is on screen.
            if (wornLeftLayerIndex < 0)
                Debug.LogError($"PlayerAimRig on '{name}': the Animator has no '{WornLeftLayer}' " +
                               "layer, so a left-arm device cannot pose while the right arm is " +
                               "posing. Run Tools/SpaceGame/Player/Build Upper Body Layer.", this);
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
        /// <summary>
        /// Play a one-shot on the masked Upper Body layer, and hold the layer up while it runs.
        ///
        /// <para>
        /// Routed through this component rather than set on the Animator directly because this
        /// component owns the layer weight - it writes it every frame from holdT, so a trigger
        /// fired from outside plays a clip on a layer weighted 0 and nothing appears.
        /// </para>
        /// </summary>
        public void PlayGesture(string trigger, float seconds = 0f) => PlayGesture(trigger, seconds, null);

        /// <summary>
        /// As above, on a named arm. An aimed gesture (the wrist blade's stab, the puncher's
        /// punch) belongs to the forearm its device is worn on, which is not what
        /// <see cref="PoseMirrored"/> says when a second device is worn on the other arm; the
        /// device knows its arm (<c>UsableItem.WornOn</c>) and says so here. The mirror bool is
        /// written now, on the frame the trigger is raised — the Any State transition reads it on
        /// that frame — and held for the gesture's length over the per-frame rewrite in Update.
        /// </summary>
        public void PlayGesture(string trigger, float seconds, ItemGrip.Hand? arm)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (string.IsNullOrEmpty(trigger)) return;

            gestureTimer = Mathf.Max(gestureTimer, seconds > 0f ? seconds : defaultGestureSeconds);
            gestureMirror = arm.HasValue ? arm.Value == ItemGrip.Hand.Left : (bool?)null;
            animator.SetBool(holdMirrorHash, gestureMirror ?? PoseMirrored);
            animator.SetBool(gesturingHash, true);
            animator.SetTrigger(trigger);
        }

        public void RaiseArm(ItemGrip.Hand hand, bool raised)
        {
            if (hand == ItemGrip.Hand.Left) raiseLeft = raised;
            else raiseRight = raised;
        }

        /// <summary>
        /// Bring the body into a hold pose because a working gauntlet on <paramref name="arm"/>
        /// wants that arm up, or let it drop.
        ///
        /// <para>
        /// <see cref="ItemGrip.HoldStyle.None"/> releases it, for that arm alone — a player with a
        /// device on each wrist switches them off one at a time. Which arm is asking decides
        /// whether the pose plays mirrored; see <see cref="PoseMirrored"/>.
        /// </para>
        /// <para>
        /// Two artifacts ask for it today and both for the same reason: a forearm device is only
        /// usable with the forearm up. <see cref="SpaceGame.Items.FlashlightGauntletArtifact"/>
        /// calls it off <c>Flashlight.Switched</c> so the beam leaves along a raised arm, and
        /// <see cref="SpaceGame.Items.ItemScannerArtifact"/> calls it while the set is powered so
        /// the wearer can read the screen. Both call on EVERY machine — the flashlight through the
        /// lamp's own event, the scanner through <c>Present</c> — so a peer sees the same posture
        /// with nothing extra on the wire, and the raised arm reads the device's on/off state from
        /// across a room (GDC-L1-ANIM-0003).
        /// </para>
        /// <para>
        /// This reuses the pose the body already takes for a HELD item rather than a pose of its
        /// own. The gauntlet is on the forearm, so what it needs is exactly what holding something
        /// needs: the forearm up and forward, pitching with the look. A bespoke set of clips was
        /// built for the torch first and thrown away — this is the same shape for none of the
        /// assets.
        /// </para>
        /// </summary>
        public void SetWornStyle(ItemGrip.Hand arm, ItemGrip.HoldStyle style)
        {
            if (arm == ItemGrip.Hand.Left) wornLeft = style;
            else wornRight = style;
        }

        /// <summary>
        /// Is <paramref name="arm"/> being held up by the device worn ON it, right now?
        ///
        /// <para>
        /// The one question <see cref="PlayerArmAim"/> asks before it swings that arm at the
        /// crosshair, and it is asked here rather than answered by the device because every reason
        /// the arm might not be the device's to move is already known here and not there: something
        /// in the hands outranks a wrist device, the gear screen stands the body down, and a corpse
        /// keeps whatever pose it died in.
        /// </para>
        /// <para>
        /// True for BOTH arms when both wear a working device — that is what the Worn Left layer
        /// exists for, and a torch on the left wrist has exactly as much business pointing where the
        /// player looks as one on the right.
        /// </para>
        /// </summary>
        public bool WornArmPosed(ItemGrip.Hand arm)
        {
            if (heldStyle != ItemGrip.HoldStyle.None) return false;
            if (Relaxed || (controller != null && controller.IsDead)) return false;

            return (arm == ItemGrip.Hand.Left ? wornLeft : wornRight) != ItemGrip.HoldStyle.None;
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
            if (gestureTimer > 0f)
                gestureTimer -= deltaTime;

            // The pose comes off entirely while dead, whatever is in the hand. The death clip runs
            // on the Base Layer, and an Upper Body layer left at weight 1 would override its arms
            // and leave the corpse holding its rifle out in front of it. That rule lives in Posing.
            holdT = PoseBlend.Ease(holdT, Posing ? 1f : 0f, holdBlendTime, deltaTime);

            // A raised gauntlet arm, dead or not: the corpse rule above applies to it too.
            bool alive = !Relaxed && (controller == null || !controller.IsDead);
            raiseLeftT = PoseBlend.Ease(raiseLeftT, raiseLeft && alive ? 1f : 0f, raiseBlendTime, deltaTime);
            raiseRightT = PoseBlend.Ease(raiseRightT, raiseRight && alive ? 1f : 0f, raiseBlendTime, deltaTime);

            // The left arm's own layer yields to that arm's firing raise, which plays on the main
            // layer and would otherwise be overwritten by this one the whole time a device on that
            // wrist is switched on — the gauntlet would fire with no gesture at all.
            bool leftArmPosing = LeftArmStyle != ItemGrip.HoldStyle.None && alive && !raiseLeft;
            wornLeftT = PoseBlend.Ease(wornLeftT, leftArmPosing ? 1f : 0f, holdBlendTime, deltaTime);
        }

        private void WriteAnimator()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (upperBodyLayerIndex < 0) return;

            // A raised arm needs the layer up even when nothing is held and the hold pose is off.
            animator.SetLayerWeight(upperBodyLayerIndex, Mathf.Max(holdT, Mathf.Max(raiseLeftT, raiseRightT)));

            // A worn gauntlet's style is written into the same parameter the hand uses, so a worn
            // pose and a held item cannot both be on: there is one pose and one state machine.
            // The mirror is a second parameter rather than more values of the first, so every hold
            // style gets a left-armed twin without the enum growing a mirrored half.
            animator.SetInteger(holdStyleHash, (int)PoseStyle);
            if (gestureTimer <= 0f) gestureMirror = null;
            animator.SetBool(holdMirrorHash, gestureMirror ?? PoseMirrored);
            animator.SetBool(gesturingHash, gestureTimer > 0f);

            // The raise is a state on the same layer — three pointing clips blended on the look
            // pitch — not an IK goal: the layer sits in Empty whenever the hands are empty, which
            // is the ordinary case for a player wearing gauntlets, and IK set on an empty layer
            // moves nothing. The clips give it something to play. Pitch comes off AimPivot, which
            // is the owner's live pitch here and their replicated pitch on every other machine.
            animator.SetInteger(armRaiseHash, (raiseLeft ? 1 : 0) | (raiseRight ? 2 : 0));
            animator.SetFloat(aimPitchHash, LookPitch());

            // The second arm, on the mask that is only that arm. Written whatever the main layer
            // is doing: the two never describe the same limb at once, because LeftArmStyle is None
            // unless the main layer has already been claimed by the right arm.
            if (wornLeftLayerIndex < 0) return;

            animator.SetLayerWeight(wornLeftLayerIndex, wornLeftT);
            animator.SetInteger(wornLeftStyleHash, (int)LeftArmStyle);
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
            wornLeftT = 0f;
            wornLeft = ItemGrip.HoldStyle.None;
            wornRight = ItemGrip.HoldStyle.None;
            heldStyle = ItemGrip.HoldStyle.None;
            WriteAnimator();
        }
    }
}
