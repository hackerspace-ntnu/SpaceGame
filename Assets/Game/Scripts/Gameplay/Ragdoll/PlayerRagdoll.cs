using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Teleporting;
using UnityEngine;

namespace SpaceGame.Gameplay.Ragdoll
{
    /// <summary>
    /// Takes a player's body away from them when they die or are caught by a shock wave, and gives
    /// it back.
    ///
    /// <para>
    /// Taking control from a player is the thing this codebase otherwise refuses to do, so it is
    /// worth being explicit about which rules apply. <c>GDC-L1-FEEL-0002</c> draws the line between
    /// latency — the game being slow to HEAR you, always a defect — and commitment, the game taking
    /// time to carry out what it already heard. A knockdown is neither: it is a state the player did
    /// not ask for, and the only honest way to price it is to bound it. Hence
    /// <c>KnockdownTuning.settleGraceSeconds</c>: a player wedged against a rock never settles, and
    /// without a ceiling would never stand up. <c>GDC-L1-ANIM-0002</c> supplies the other half —
    /// control comes back at the START of the recovery blend, not the end, so the player is already
    /// driving while their body finishes standing up.
    /// </para>
    ///
    /// <para>
    /// The caster of a repulsor blast is never a victim of it: <c>RepulsorGauntletArtifact.FireBlast</c>
    /// excludes its own holder's root, so the repulsor-jump and its recoil are untouched by any of
    /// this.
    /// </para>
    ///
    /// <para>
    /// Death and knockdown share the ragdoll and nothing else. Death is permanent limpness and
    /// <c>PlayerController</c> keeps owning its freeze, its cursor and its death screen — the flag
    /// on <c>PlayerController.isDead</c> says death outranks every other control owner, and this is
    /// one of them. A knockdown recovers on its own.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    public class PlayerRagdoll : RagdollController
    {
        [Tooltip("Upward speed handed to a dying player's body, m/s. Just enough that they fold " +
                 "over their own feet rather than sinking through them.")]
        [SerializeField] private float deathLift = 1.5f;

        [Header("Camera")]
        [Tooltip("Where the camera sits relative to the fallen body while limp — back, up and to " +
                 "the side, in the body's own frame.\n\n" +
                 "The camera has to leave the head. It normally lives inside the helmet, and a " +
                 "first-person view bolted to a tumbling skull is unusable in the literal sense: " +
                 "the player cannot tell what happened to them, which is the one thing a knockdown " +
                 "has to communicate.")]
        [SerializeField] private Vector3 downedCameraOffset = new Vector3(0.6f, 1.8f, -2.4f);

        [Tooltip("How fast the camera chases that spot, per second. Low enough to smooth the " +
                 "tumble out; a camera rigidly attached to a ragdoll is the tumbling skull again " +
                 "at a longer focal length (GDC-L1-FEEL-0006 — dose the camera motion).")]
        [SerializeField] private float downedCameraLerp = 6f;

        private PlayerController controller;
        private PlayerMovement movement;
        private PlayerLook look;
        private Rigidbody body;
        private Collider bodyCollider;

        private bool bodyWasKinematic;
        private RigidbodyInterpolation bodyInterpolation;
        private bool movementWasEnabled;
        private bool lookWasEnabled;
        private bool inputWasEnabled;
        private bool colliderWasEnabled;

        private Transform cameraTransform;
        private Transform cameraParent;
        private Vector3 cameraLocalPosition;
        private Quaternion cameraLocalRotation;

        /// <summary>The heading the camera watches from, fixed when the player goes down.</summary>
        private Quaternion downedCameraFrame;

        protected override void Awake()
        {
            base.Awake();
            controller = GetComponent<PlayerController>();
            movement = GetComponent<PlayerMovement>();
            look = GetComponent<PlayerLook>();
            body = GetComponent<Rigidbody>();
            bodyCollider = GetComponent<Collider>();

            // The player gets up looking where they were looking: the view hangs off this body,
            // and turning it to face wherever the ragdoll landed moved their camera for them.
            rig.KeepsFacingOnRecover = true;

            if (health == null)
                Debug.LogWarning($"{name}: PlayerRagdoll needs a HealthComponent to know when this " +
                                 "player dies.", this);
        }

        /// <summary>
        /// A player's body is OWNER-authoritative — the rule FlungBody follows, and the reason a
        /// server-side push on one is overwritten within a tick. So the machine that drives the
        /// ragdoll is the one that owns the player, not the server.
        /// </summary>
        protected override bool Drives => Network.Owns(this);

        /// <summary>
        /// Is this player's body already somebody else's to move?
        ///
        /// <para>
        /// Asked of <see cref="CarriedBody"/> rather than of a mount, and that is the whole reason
        /// this answer is trustworthy. There is no single "am I mounted" flag on a player: a rider
        /// is normally parented into the saddle, so <c>GetComponentInParent&lt;MountModule&gt;()</c>
        /// would usually find it — but <c>MountModule.ParentRiderToMount</c> has a documented
        /// fallback that seats a rider WITHOUT parenting when netcode refuses the reparent, and a
        /// hierarchy check misses exactly that case. Both riding systems in this project register
        /// their claim here instead (<c>MountModule</c> for the saddle,
        /// <c>SeatedRider</c> for a ship's chair), on every path, parented or not.
        /// </para>
        /// <para>
        /// <b>Rigidly, not merely held, and the difference is a desync.</b>
        /// <c>UnderTerrainGuard</c> claims bodies through the same record while the ground under
        /// them loads — but the guard runs OWNER-ONLY, so that claim exists on the victim's machine
        /// and nowhere else. Refusing on it would have the server accept a capture and announce it,
        /// every peer put the body limp, and the victim's own machine alone refuse — and a player's
        /// transform is owner-authoritative, so the victim's answer is the one that wins. Limp on
        /// every other screen, walking around on their own, during ordinary chunk streaming. The
        /// seat and the saddle are replicated and produce the same answer everywhere, which is what
        /// makes them safe to refuse on; see <see cref="CarriedBody.IsCarriedRigidly"/>.
        /// </para>
        /// </summary>
        private bool IsCarried => CarriedBody.IsCarriedRigidly(gameObject);

        /// <summary>
        /// A seat or a saddle refuses knockdowns and holds alike — see <see cref="IsCarried"/> for
        /// why that is asked of <c>CarriedBody</c>, and rigidly.
        /// </summary>
        protected override bool RefusesToGoDown => IsCarried;

        /// <summary>
        /// How fast this player was already moving. Unlike a creature's, this one really is on the
        /// rigidbody — but only until <see cref="SuspendLayers"/> makes it kinematic, so every caller
        /// reads it first.
        /// </summary>
        protected override Vector3 CarriedVelocity =>
            body != null && !body.isKinematic ? body.linearVelocity : Vector3.zero;

        /// <summary>Just enough that a dying player folds over their own feet.</summary>
        protected override Vector3 DeathImpulse() => Vector3.up * deathLift;

        /// <summary>
        /// Death outranks standing up. PlayerController's isDead is the authority on whether this
        /// player has control, and a knockdown that landed on the same frame as the killing blow
        /// must not stand the corpse back up.
        /// </summary>
        protected override bool ControlsLocked => controller != null && controller.IsDead;

        /// <summary>
        /// Take the body without laying it down: input, look and movement stop, the body is pinned
        /// where it stands, and the skeleton keeps the pose it was in.
        ///
        /// <para>
        /// The freeze's hold, and the one hold in this game that must not go through the ragdoll.
        /// A body frozen solid reads as a statue only while it keeps the pose it was caught in, and
        /// <see cref="RagdollController.HoldDown"/> would replace that with a heap on the sand and
        /// move the camera out of the helmet to watch it. Everything else about the claim is
        /// identical — the same claim set, the same release, the same refusal for a body a seat is
        /// already carrying — so a captive who is netted AND frozen is held once and stands up once.
        /// </para>
        /// <para>
        /// The collider stays on, unlike a limp hold: a statue is something the world can still
        /// bump into, and switching it off would let bodies walk through the player standing there.
        /// </para>
        /// </summary>
        /// <returns>
        /// True once the body is held. False means the hold did not take — the player is dead, or
        /// something else is already carrying them — and the caller must not treat them as held.
        /// </returns>
        public bool HoldStanding(object holder) => HoldStandingClaim(holder);

        private void LateUpdate()
        {
            if (!rig.IsLimp || cameraTransform == null || rig.Hips == null) return;

            // The offset is applied in a FIXED frame, not the hips'. Hanging it off the pelvis
            // means it inherits the tumble, and a camera that rolls with the body is the tumbling
            // first-person view this exists to escape — just further away.
            Vector3 target = rig.Hips.position + downedCameraFrame * downedCameraOffset;
            Vector3 lookAt = rig.Hips.position;

            float t = 1f - Mathf.Exp(-downedCameraLerp * Time.deltaTime);
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, target, t);

            Vector3 toBody = lookAt - cameraTransform.position;
            if (toBody.sqrMagnitude > 1e-4f)
                cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation,
                                                            Quaternion.LookRotation(toBody), t);
        }

        // ── Handing the body over and back ────────────────────────────────────

        /// <param name="standing">
        /// Keep the body on its feet: the collider stays on and the camera stays in the helmet,
        /// because nothing is going to go limp under it. See <see cref="HoldStanding"/>.
        /// </param>
        protected override void OnEnable()
        {
            base.OnEnable();
            this.NetOn(NetMsg.GetUpRequest, OnGetUpRequest);
            this.NetOn(NetMsg.GotUp, OnGotUp);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            this.NetOff(NetMsg.GetUpRequest, OnGetUpRequest);
            this.NetOff(NetMsg.GotUp, OnGotUp);
        }

        /// <summary>
        /// Jump, pressed while knocked down, on the machine that owns this player: ask to get up.
        ///
        /// <para>
        /// A request rather than getting up on the spot, because every machine runs its own copy
        /// of the ragdoll: an owner that stood up alone would walk off while everyone else still
        /// watched the body lie there until its own timer ran out. The server answers with
        /// <see cref="NetMsg.GotUp"/> to all of them.
        /// </para>
        /// </summary>
        private void RequestGetUp()
        {
            if (!CanGetUpEarly) return;

            NetMessaging.NetSendTo(gameObject, NetMsg.GetUpRequest, default, NetTo.Server);
        }

        /// <summary>
        /// Server: a player asked to get up. Only the player may ask for their own body, and only
        /// a body this machine also has down is worth announcing.
        /// </summary>
        private void OnGetUpRequest(in NetArg arg, ulong sender)
        {
            if (!Network.MayActFor(NetChannel.RootOf(this), sender)) return;
            if (!CanGetUpEarly) return;

            NetMessaging.NetSendTo(gameObject, NetMsg.GotUp, default, NetTo.All);
        }

        /// <summary>Every machine: the server let this player up.</summary>
        private void OnGotUp(in NetArg arg, ulong sender) => GetUpNow();

        protected override void SuspendLayers(bool standing)
        {
            // Recorded rather than assumed. A player can go limp while already frozen by something
            // else — mounted, mid-cutscene, or dead — and restoring a blanket "enabled" would hand
            // control back to a body that was never supposed to have it.
            colliderWasEnabled = bodyCollider != null && bodyCollider.enabled;
            if (bodyCollider != null && !standing) bodyCollider.enabled = false;

            if (body != null)
            {
                bodyWasKinematic = body.isKinematic;
                body.isKinematic = true;

                // Interpolation would write a lagged root over the one the rig sets, dragging
                // every bone with it.
                bodyInterpolation = body.interpolation;
                body.interpolation = RigidbodyInterpolation.None;
            }

            if (!Drives) return;

            movementWasEnabled = movement != null && movement.enabled;
            lookWasEnabled = look != null && look.enabled;
            inputWasEnabled = controller != null && controller.Input != null
                              && controller.Input.enabled;

            if (movement != null) movement.enabled = false;
            if (look != null) look.enabled = false;

            // Killed at the source, not merely by disabling PlayerMovement: jump and dash arrive as
            // input EVENTS that PlayerMovement subscribes to in Start and never unsubscribes, so a
            // disabled component still leaves a limp player able to jump. PlayerController's own
            // death freeze documents the same trap.
            if (controller != null && controller.Input != null) controller.Input.enabled = false;

            // Jump gets a knocked-down player up — its own action, switched on only now that the
            // input it lives beside has been switched off (see PlayerInputManager.OnGetUpPressed).
            // Not for a standing hold: a frozen or netted body is held, not knocked down.
            if (!standing && controller != null && controller.Input != null)
            {
                controller.Input.OnGetUpPressed += RequestGetUp;
                controller.Input.SetGetUpEnabled(true);
            }

            // Only a body that is about to go limp needs the camera out of its skull. A held-
            // standing body keeps the pose it had, so the helmet stays where the eye already is —
            // and detaching it there would leave an unparented camera looking at the inside of a
            // head this component had just un-hidden.
            if (!standing) DetachCamera();
        }

        protected override void RestoreLayers(in TeleportMove move)
        {
            // Only ever switched back ON. PlayerController owns the death freeze and re-asserts it
            // from several places, so writing a recorded "false" back over one of those would be
            // this component quietly taking control away on a frame it was not asked to.
            if (bodyCollider != null && colliderWasEnabled) bodyCollider.enabled = true;
            if (body != null) body.isKinematic = bodyWasKinematic;
            if (body != null) body.interpolation = bodyInterpolation;

            if (!Drives) return;

            AttachCamera();

            // Before the input comes back, so the get-up action is off again by the time Jump is
            // listening for real. Safe after a standing hold, which never switched it on.
            if (controller != null && controller.Input != null)
            {
                controller.Input.SetGetUpEnabled(false);
                controller.Input.OnGetUpPressed -= RequestGetUp;
            }

            if (movement != null && movementWasEnabled) movement.enabled = true;
            if (look != null && lookWasEnabled) look.enabled = true;
            if (controller != null && controller.Input != null && inputWasEnabled)
                controller.Input.enabled = true;
        }

        /// <summary>
        /// Move the camera out of the helmet and show the player their own body.
        ///
        /// <para>
        /// The head has to be un-hidden along with it. PlayerLook permanently draws this player's
        /// helmet and scarf as shadows-only for their OWN camera, because in first person those
        /// sit between the eye and the world — and it does so from a render callback subscribed in
        /// Start, which keeps running while the component is disabled. Left alone, a player looking
        /// at their own knocked-down body would find it headless.
        /// </para>
        /// </summary>
        private void DetachCamera()
        {
            if (look == null || look.playerCamera == null) return;

            cameraTransform = look.playerCamera.transform;
            cameraParent = cameraTransform.parent;
            downedCameraFrame = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            cameraLocalPosition = cameraTransform.localPosition;
            cameraLocalRotation = cameraTransform.localRotation;

            cameraTransform.SetParent(null, true);
            look.SetFirstPersonHidden(false);
        }

        private void AttachCamera()
        {
            if (cameraTransform == null) return;

            cameraTransform.SetParent(cameraParent, false);
            cameraTransform.localPosition = cameraLocalPosition;
            cameraTransform.localRotation = cameraLocalRotation;
            cameraTransform = null;

            if (look != null) look.SetFirstPersonHidden(true);
        }
    }
}
