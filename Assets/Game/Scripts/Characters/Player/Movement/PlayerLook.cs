using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using SpaceGame.Core;
using SpaceGame.World;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Characters
{
    public class PlayerLook : MonoBehaviour
    {
        private PlayerInputManager inputs;
        [Header("References")]
        public GameObject playerCamera;

        [Tooltip("Everything between this player's eye and the world now that the camera sits " +
                 "inside the helmet: the helmet itself, the scarf. Hidden from their own view " +
                 "only — shadows and every other camera keep them. Gear worn at runtime joins " +
                 "this through SetWornHidden rather than being listed here.")]
        [SerializeField] private Renderer[] firstPersonHidden;

        public Transform playerBody;
        public Transform cameraRoot => playerCamera != null ? playerCamera.transform : null;
        private Rigidbody playerRigidbody;

        [Header("Settings")]
        public float sensitivity = 1f;
        public float verticalClamp = 80f;


        private float pitch = 0f;

        /// Yaw accumulated by Update since the last physics step, in degrees. See Update.
        private float pendingYaw;

        private Vector2 lookInput;

        private Camera lookCamera;

        private void Start()
        {
            inputs = GetComponent<PlayerController>().Input;
            playerRigidbody = playerBody.GetComponent<Rigidbody>();

            // Start/OnDestroy, deliberately not OnEnable/OnDisable: mounting disables this
            // component while first person continues through this same camera, and the head must
            // stay hidden there. Remote copies never subscribe — their PlayerLook is disabled from
            // Awake, so Start never runs and their head is left exactly as authored.
            RenderPipelineManager.beginCameraRendering += ApplyFirstPersonVisibility;

            lookCamera = playerCamera != null ? playerCamera.GetComponent<Camera>() : null;

            // Where the prefab put the eye along the body's forward axis, and the frame that rest
            // position is expressed in. Captured here rather than read every frame: this component
            // writes that same component back, so reading it would compound.
            if (playerCamera != null)
            {
                eyeParent = playerCamera.transform.parent;
                baseEyeZ = playerCamera.transform.localPosition.z;
            }

            // The field of view authored on the prefab is the slider's starting point, adopted only
            // while the player has never moved it — otherwise every launch would overwrite what
            // they chose with whatever the prefab happens to say.
            if (lookCamera != null) GameSettings.SeedFieldOfView(lookCamera.fieldOfView);

            GameSettings.Changed += ApplySettings;
            ApplySettings();
        }

        private void OnDestroy()
        {
            GameSettings.Changed -= ApplySettings;
            RenderPipelineManager.beginCameraRendering -= ApplyFirstPersonVisibility;
        }

        /// <summary>
        /// Hide this player's own worn gear from their own eyes, and only their own.
        ///
        /// <para>
        /// Per camera render, not a global toggle: <c>ShadowsOnly</c> written once hides these
        /// from every camera at once, which is right for this player's own view and wrong for
        /// everything else — the mount's orbit camera, the death spectator, the pack's own focus
        /// camera, and any future third person view must all keep them. Deciding at the start of
        /// each camera's render means no view has to remember to switch it back, and they still
        /// cast their shadows in first person because <c>ShadowsOnly</c> keeps the shadow pass —
        /// so a player still reads as wearing their pack from its shadow on the sand.
        /// </para>
        /// </summary>
        /// <summary>
        /// Whether this player's own camera still hides their helmet, scarf and pack.
        ///
        /// <para>
        /// True is the resting state and the whole reason <see cref="firstPersonHidden"/> exists.
        /// It is lifted while the camera is not in the helmet — a ragdolled player watches their own
        /// body from outside it (<c>PlayerRagdoll</c>), and hiding the head from the only camera
        /// looking at it leaves them staring at a headless corpse.
        /// </para>
        ///
        /// <para>
        /// A field rather than something derived from the camera's parent, because the callback
        /// below runs from the render pipeline for every camera every frame and must not be doing
        /// hierarchy work. Whoever lifts it puts it back.
        /// </para>
        /// </summary>
        public void SetFirstPersonHidden(bool hidden) => firstPersonHiddenActive = hidden;

        private bool firstPersonHiddenActive = true;

        /// <summary>
        /// The renderers of gear this body is WEARING, hidden from its own eye alongside
        /// <see cref="firstPersonHidden"/>. Replaces whatever was registered before; null or empty
        /// clears it.
        ///
        /// <para>
        /// Separate from the serialized array because worn gear is instantiated at runtime and does
        /// not hold still: the backpack is built in <c>BackpackController.Awake</c>, and the items
        /// strapped to it are display copies rebuilt every time its contents change, so there is no
        /// set of renderers a prefab field could have named. <c>BackpackController</c> re-registers
        /// on both.
        /// </para>
        /// <para>
        /// <b>Why the pack is hidden rather than posed clear of the eye.</b> It rides the Spine
        /// bone while this camera is bolted to the player root, so a walk cycle's lean rotates it
        /// about a pivot below the eye and throws its top forward through the near plane — and no
        /// worn pose can fix that for gear the player chose the size of, because
        /// <c>PackSurfaceId.LongGoods</c> takes an item 18 cells long — 1.70 m at the current
        /// <c>PackScale.Factor</c>, and 2.43 m at the 1.5 rig this was first written for. The
        /// 2026-09-02 shrink narrows the intrusion; it does not remove it. The pack is inspected in focus
        /// mode, seen by everyone else, and still casts this player's shadow, so hiding it from the
        /// one camera it can only ever obstruct costs nothing.
        /// </para>
        /// <para>
        /// Same contract as the serialized array: these renderers are assumed to want
        /// <see cref="ShadowCastingMode.On"/> everywhere else, because that is what they are given
        /// back — for every other camera, and once for the outgoing set here. A renderer authored
        /// <c>Off</c> would come back on, so do not register one.
        /// </para>
        /// </summary>
        public void SetWornHidden(Renderer[] renderers)
        {
            // The outgoing set gets its shadows back first. A pack that has just left the player's
            // back is theirs to look at from this frame on, and once it is off the register nothing
            // else would ever write ShadowsOnly off it again.
            Apply(wornHidden, ShadowCastingMode.On);

            wornHidden = renderers ?? Array.Empty<Renderer>();
        }

        private Renderer[] wornHidden = Array.Empty<Renderer>();

        private void ApplyFirstPersonVisibility(ScriptableRenderContext context, Camera renderingCamera)
        {
            ShadowCastingMode mode = renderingCamera == lookCamera && firstPersonHiddenActive
                ? ShadowCastingMode.ShadowsOnly
                : ShadowCastingMode.On;

            Apply(firstPersonHidden, mode);
            Apply(wornHidden, mode);
        }

        private static void Apply(Renderer[] renderers, ShadowCastingMode mode)
        {
            if (renderers == null) return;

            foreach (Renderer renderer in renderers)
            {
                if (renderer != null) renderer.shadowCastingMode = mode;
            }
        }

        private void ApplySettings()
        {
            // Only the base changed. The composed value is written every frame by ApplyFieldOfView,
            // which is what keeps a kick from being wiped the next time any setting is touched.
            ApplyFieldOfView();
        }

        // ── Field of view ──────────────────────────────────────────────────────
        //
        // The player's own FieldOfView setting is the base and is never written to. Effects add
        // DEGREES ON TOP of it, so a player who chose 95 keeps 95 as their resting view and still
        // gets the same size of kick as a player who chose 60. Writing an absolute FOV — the
        // obvious way to do this — silently overwrites a preference the pause menu owns, and the
        // player's slider stops matching what they see.

        [Header("Field of view")]
        [Tooltip("Degrees per second the view opens up toward a requested kick.")]
        [SerializeField] private float fovKickInSpeed = 60f;

        [Tooltip("Degrees per second it settles back. Slower than the way in, so speed arrives as " +
                 "a punch and leaves as a glide.")]
        [SerializeField] private float fovKickOutSpeed = 35f;

        private float fovOffsetTarget;
        private float fovOffset;

        /// <summary>
        /// Ask for <paramref name="degrees"/> of extra field of view, eased in and out.
        ///
        /// <para>
        /// Additive and idempotent: callers set a target every frame and set 0 when they are done.
        /// Today the grappling hook drives it from how fast the player is actually travelling,
        /// which is most of what makes a fast swing read as fast — the geometry alone does not,
        /// because nothing in the frame changes size when the whole view moves together.
        /// </para>
        /// </summary>
        public void SetFovOffset(float degrees) => fovOffsetTarget = degrees;

        private void ApplyFieldOfView()
        {
            if (lookCamera == null) return;
            lookCamera.fieldOfView = GameSettings.FieldOfView + fovOffset;
        }

        private void TickFieldOfView()
        {
            if (Mathf.Approximately(fovOffset, fovOffsetTarget)) return;

            float speed = fovOffsetTarget > fovOffset ? fovKickInSpeed : fovKickOutSpeed;
            fovOffset = Mathf.MoveTowards(fovOffset, fovOffsetTarget, speed * Time.deltaTime);

            ApplyFieldOfView();
        }

        // ── Look-down offset ───────────────────────────────────────────────────
        //
        // The eye is authored inside the helmet of a 3 m astronaut, which is fine while the view
        // points at the horizon and useless once it points at the floor: looking straight down the
        // camera sits behind the chest and frames the body it is inside instead of the ground the
        // player wanted to see. So as the pitch goes down the eye slides FORWARD, out past the
        // chest, and the player looks down in front of themselves.
        //
        // Along the body's forward axis, not the view's: forward is the direction "in front of the
        // character" means, and the eye's height belongs to PlayerStance — a second driver on that
        // axis would fight the crouch. It is also the only translation that leaves the local frame
        // this component may write, because pitch is a rotation on this same transform.
        //
        // Local presentation, owner only. Nothing here is published: PlayerLook is disabled from
        // Awake on every remote copy, so Start never runs there and no other machine sees the eye
        // move. It does move the aim ray with it — AimProvider and Interactor both read this
        // transform — which is deliberate. The reticle is drawn at the centre of this camera, so a
        // ray that started anywhere else would stop matching the crosshair; keeping one truth means
        // a shot taken while looking at your boots leaves from in front of the chest rather than
        // from inside it, and still lands exactly where the crosshair sat.

        [Header("Look-down offset")]
        [Tooltip("How far the eye slides forward, in metres, at full downward pitch. 0 turns the " +
                 "effect off. Capped by whatever geometry is in front of the player.")]
        [SerializeField] private float lookDownOffset = 0.4f;

        [Tooltip("Downward pitch, in degrees, where the eye starts to move. At and above this — " +
                 "the horizon and all of the sky — it sits exactly where the prefab put it.")]
        [SerializeField] private float lookDownStartPitch = 25f;

        [Tooltip("Downward pitch, in degrees, at which the eye has moved the whole way. Above " +
                 "verticalClamp this simply never arrives, which is a way of softening the effect.")]
        [SerializeField] private float lookDownFullPitch = 80f;

        [Tooltip("Shape of the slide between those two angles. Eased at both ends so the move has " +
                 "no corner to catch the eye at either.")]
        [SerializeField] private AnimationCurve lookDownEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("How fast the eye catches up to where the pitch says it should be, per second. " +
                 "Higher is tighter; this is what keeps the recovery from a wall smooth.")]
        [SerializeField] private float lookDownResponse = 12f;

        [Tooltip("What the eye refuses to slide through. The ship interior is tight enough that " +
                 "this matters.")]
        [SerializeField] private LayerMask lookDownBlockers = ~0;

        [Tooltip("Radius of the sweep that keeps the eye out of geometry, in metres. Wants to be " +
                 "comfortably larger than the camera's near plane, or a wall arrives through it.")]
        [SerializeField] private float lookDownClearance = 0.2f;

        /// <summary>Colliders in the way of the slide. Reused; see <see cref="ClearedOffset"/>.</summary>
        private static readonly RaycastHit[] ClearanceHits = new RaycastHit[8];

        /// <summary>
        /// Local z the eye rests at, from the prefab. NaN until <see cref="Start"/> has read it,
        /// which is also how a copy that was never started knows not to write one back.
        /// </summary>
        private float baseEyeZ = float.NaN;

        /// <summary>
        /// The transform <see cref="baseEyeZ"/> is measured in. Held so the slide can tell whether
        /// the camera is still on the rig: <c>PlayerRagdoll</c> lifts it off the head on death, and
        /// a local z written under some other parent is not a rest position, it is a shove.
        /// </summary>
        private Transform eyeParent;

        /// <summary>Metres of slide currently applied, smoothed toward the target every frame.</summary>
        private float eyeOffset;

        /// <summary>
        /// Whether there is still an eye, in the frame this component measured, to slide. False on
        /// a copy that never started — every remote — and once something has taken the camera away.
        /// </summary>
        private bool EyeIsOnTheRig =>
            playerCamera != null && !float.IsNaN(baseEyeZ) && playerCamera.transform.parent == eyeParent;

        private void TickLookDownOffset()
        {
            if (!EyeIsOnTheRig) return;

            Transform eye = playerCamera.transform;

            // Swept from where the eye RESTS, not from where it currently is: both the slide and
            // the distance the sweep reports have to be measured from the same origin, or a clamp
            // computed from an already-slid eye feeds back into itself and the view crawls.
            Vector3 local = eye.localPosition;
            Vector3 rest = eyeParent.TransformPoint(new Vector3(local.x, local.y, baseEyeZ));

            float wanted = ClearedOffset(rest, eyeParent.forward, TargetLookDownOffset());

            // Exponential decay rather than Lerp(a, b, k * deltaTime): the naive form makes the
            // catch-up fraction depend on the frame time, so the same slide reads differently at
            // 60 and at 240 fps. This is the form the rest of the project smooths with.
            eyeOffset = Mathf.Lerp(eyeOffset, wanted, 1f - Mathf.Exp(-lookDownResponse * Time.deltaTime));

            ApplyLookDownOffset(eye);
        }

        /// <summary>How far the eye would like to be, before anything solid gets a say.</summary>
        private float TargetLookDownOffset()
        {
            if (lookDownOffset <= 0f) return 0f;

            // Unity's positive pitch looks down, so this is a plain "how far past the start angle".
            float span = lookDownFullPitch - lookDownStartPitch;
            if (span <= 0f) return pitch >= lookDownFullPitch ? lookDownOffset : 0f;

            float t = Mathf.Clamp01((pitch - lookDownStartPitch) / span);
            return lookDownEase.Evaluate(t) * lookDownOffset;
        }

        /// <summary>
        /// The same slide, cut short by whatever it would otherwise have gone through.
        ///
        /// <para>
        /// The player's own colliders are skipped rather than masked out — they sit on the same
        /// layer as plenty of what this must not pass through, so a mask that excluded them would
        /// also excuse a real wall. Same trade <c>PlayerStance.HasHeadroom</c> makes.
        /// </para>
        /// </summary>
        private float ClearedOffset(Vector3 rest, Vector3 direction, float wanted)
        {
            if (wanted <= 0f) return 0f;

            int count = Physics.SphereCastNonAlloc(rest, lookDownClearance, direction,
                                                   ClearanceHits, wanted, lookDownBlockers,
                                                   QueryTriggerInteraction.Ignore);

            float allowed = wanted;

            for (int i = 0; i < count; i++)
            {
                Collider hit = ClearanceHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(transform)) continue;

                // A sweep that starts already overlapping reports distance 0, which is the right
                // answer anyway: an eye with a wall on it does not get to move at all.
                allowed = Mathf.Min(allowed, ClearanceHits[i].distance);
            }

            return allowed;
        }

        /// <summary>
        /// Writes the slide, and only the axis it owns.
        ///
        /// <para>
        /// Read-modify-write rather than a vector built here: <c>PlayerStance</c> drives the same
        /// transform's local <i>height</i> for the crouch, and either component assembling a whole
        /// localPosition would delete the other's axis on its next write.
        /// </para>
        /// </summary>
        private void ApplyLookDownOffset(Transform eye)
        {
            Vector3 local = eye.localPosition;
            local.z = baseEyeZ + eyeOffset;
            eye.localPosition = local;
        }

        /// <summary>
        /// Point the view along a world direction, without fighting the rig.
        ///
        /// Assigning the camera's rotation directly does not work here: pitch is
        /// kept as a float and rewritten from it every Update, so a direct write
        /// survives for exactly one frame. Yaw has the mirror problem in the
        /// other direction — it is banked in <see cref="pendingYaw"/> and spent
        /// on the Rigidbody at the next physics step, so input gathered before a
        /// teleport would be applied after it, turning the player by an amount
        /// that meant something in the place they left.
        ///
        /// Used by portal traversal, which has to hand the player back exactly
        /// the view they had a frame earlier, seen from somewhere else. The body
        /// yaw is the caller's to set; this owns only the pitch.
        /// </summary>
        public void LookAlong(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude < 1e-6f) return;

            worldDirection.Normalize();

            // Unity's positive pitch looks down, so the sign is inverted here.
            pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(worldDirection.y, -1f, 1f)) * Mathf.Rad2Deg,
                                -verticalClamp, verticalClamp);

            ApplyLensRotation();

            pendingYaw = 0f;
        }

        /// <summary>
        /// How far up or down the player is looking, in degrees. Negative is up.
        ///
        /// <para>
        /// Worth exposing because it is the half of the view that nothing else records. Yaw lives on
        /// the body's Rigidbody rotation and is captured with the player's pose; pitch is a private
        /// float on a child camera, so a player who quit looking down a shaft came back staring at
        /// the horizon.
        /// </para>
        /// </summary>
        public float Pitch => pitch;

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// <para>
        /// Safe at any point in the frame: <see cref="Update"/> moves pitch by a delta rather than
        /// recomputing it, so a value written here is the one it carries on from — the same property
        /// <see cref="LookAlong"/> relies on.
        /// </para>
        /// </summary>
        public void RestorePitch(float degrees)
        {
            pitch = Mathf.Clamp(degrees, -verticalClamp, verticalClamp);
            ApplyLensRotation();
        }

        // ── Flying ─────────────────────────────────────────────────────────────
        //
        // A wing owns both halves of the view while it is out: the pitch IS the nose, and the roll
        // is the bank. Handing them over rather than letting the flight write the camera itself
        // keeps one owner for the lens's local rotation — the same rule the crouch and the
        // look-down slide already follow, and for the same reason: a second writer that assembles
        // the whole rotation silently deletes whatever the first one put there.

        /// <summary>
        /// Degrees of view movement per unit of mouse input per second — the rig's own sensitivity
        /// with the player's setting already folded in.
        ///
        /// <para>
        /// Exposed so anything that takes the mouse over can move at the speed the player is used
        /// to instead of inventing its own number. The wingsuit did invent one, and being 40% of
        /// this read as input lag rather than as a design choice: a control that IS the look has to
        /// move like the look.
        /// </para>
        /// </summary>
        public float LookDegreesPerUnit => sensitivity * GameSettings.MouseSensitivity;

        /// <summary>Is a wing driving the view? See <see cref="SetFlying"/>.</summary>
        private bool flying;

        /// <summary>How far the lens is banked, degrees. Owned by the flight while it lasts.</summary>
        private float viewRoll;

        /// <summary>
        /// Hand the view to a wing, or take it back.
        ///
        /// <para>
        /// Taking it back levels the roll but leaves the pitch exactly where the flight left it, so
        /// a landing does not snap the horizon: the player carries on looking where they were
        /// flying. The caller MUST clear it — <c>WingsuitFlight.End</c> is reached from the fold,
        /// the landing and the teardown alike.
        /// </para>
        /// </summary>
        public void SetFlying(bool value)
        {
            flying = value;
            if (!value) viewRoll = 0f;

            ApplyLensRotation();
        }

        /// <summary>
        /// Where the wing is pointing, each frame it is out. Pitch is in this component's own
        /// convention — positive looks DOWN — so a nose-up flight state arrives negative.
        /// </summary>
        public void SetFlightAttitude(float pitchDegrees, float rollDegrees)
        {
            if (!flying) return;

            pitch = Mathf.Clamp(pitchDegrees, -verticalClamp, verticalClamp);
            viewRoll = rollDegrees;
        }

        /// <summary>
        /// The one place the lens's local rotation is written, from the two angles this component
        /// owns. Extracted because there were three copies of the same assignment and the roll had
        /// to appear in all of them or it would blink out on any frame the other two ran.
        /// </summary>
        private void ApplyLensRotation()
        {
            if (playerCamera == null) return;

            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, viewRoll);
        }

        private void OnEnable()
        {
            ApplyCursorLock();
        }

        private void OnDisable()
        {
            ReleaseCursorLock();

            // Put the eye back. Mounting, cutscenes and death all switch this component off while
            // that camera keeps rendering, and a slide left behind is an eye parked in front of a
            // chest with nothing still running to bring it home.
            eyeOffset = 0f;
            if (EyeIsOnTheRig) ApplyLookDownOffset(playerCamera.transform);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && isActiveAndEnabled)
            {
                ApplyCursorLock();
            }
        }

        private void LateUpdate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked || Cursor.visible)
            {
                ApplyCursorLock();
            }
        }
    
        void Update()
        {
            TickFieldOfView();

            lookInput = inputs.LookInput;

            // Under a wing the mouse is the stick, not the look: WingsuitFlight reads the same
            // LookInput, turns it into a commanded nose angle and a rudder, and hands the resulting
            // attitude back through SetFlightAttitude. Neither channel may also be spent here — the
            // pitch would be written twice per frame from two different rules, and the yaw would
            // turn the body out from under a heading the flight model owns.
            if (flying)
            {
                ApplyLensRotation();
                TickLookDownOffset();
                return;
            }

            // The serialized sensitivity is the rig's own scale; the setting is a multiplier on top
            // of it, so tuning the prefab and the player's preference stay independent.
            float scaled = LookDegreesPerUnit;

            // Yaw is banked here and spent in FixedUpdate, because it turns a Rigidbody and a
            // Rigidbody may only be posed on the physics clock. Calling MoveRotation from here span
            // it several times per physics step, and every one of those calls threw away the
            // interpolation that smooths a 50 Hz simulation out over a 240 Hz display -- the camera
            // hangs off this body, so what that actually looked like was the whole view shaking.
            //
            // Banked rather than sampled once per step: LookInput is read per rendered frame, and
            // reading it only in FixedUpdate would drop four mouse movements out of five. Summing
            // the same per-frame terms keeps the total rotation, and the feel, exactly as authored.
            pendingYaw += lookInput.x * scaled * Time.deltaTime;

            // Pitch stays here. It turns the camera, which is a plain transform with no Rigidbody
            // and no interpolation to lose, so it can keep answering at the frame rate.
            float pitchInput = GameSettings.InvertLookY ? -lookInput.y : lookInput.y;
            pitch -= pitchInput * scaled * Time.deltaTime;
            pitch = Mathf.Clamp(pitch, -verticalClamp, verticalClamp);
            ApplyLensRotation();

            // After the pitch is final, so the eye slides to the angle the player is looking at
            // this frame rather than the one they were looking at last.
            TickLookDownOffset();
        }

        private void FixedUpdate()
        {
            if (Mathf.Approximately(pendingYaw, 0f)) return;

            playerRigidbody.MoveRotation(playerRigidbody.rotation * Quaternion.Euler(0f, pendingYaw, 0f));
            pendingYaw = 0f;
        }

        private void ApplyCursorLock()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void ReleaseCursorLock()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
