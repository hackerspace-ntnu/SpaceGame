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

        [Tooltip("Mouse sensitivity is multiplied by this while aiming, eased over the aim blend. " +
                 "Below 1 makes fine aim possible; 1 disables the effect.")]
        [SerializeField, Range(0.1f, 1f)] private float aimSensitivity = 0.5f;

        private float pitch = 0f;

        /// Yaw accumulated by Update since the last physics step, in degrees. See Update.
        private float pendingYaw;

        private Vector2 lookInput;

        private Camera lookCamera;

        private PlayerAimRig aimRig;

        private void Start()
        {
            inputs = GetComponent<PlayerController>().Input;
            aimRig = GetComponent<PlayerAimRig>();
            playerRigidbody = playerBody.GetComponent<Rigidbody>();

            // Start/OnDestroy, deliberately not OnEnable/OnDisable: mounting disables this
            // component while first person continues through this same camera, and the head must
            // stay hidden there. Remote copies never subscribe — their PlayerLook is disabled from
            // Awake, so Start never runs and their head is left exactly as authored.
            RenderPipelineManager.beginCameraRendering += ApplyFirstPersonVisibility;

            lookCamera = playerCamera != null ? playerCamera.GetComponent<Camera>() : null;

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
        /// <c>PackSurfaceId.LongGoods</c> takes an item 2.43 m long. The pack is inspected in focus
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

            if (playerCamera != null)
                playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

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

            if (playerCamera != null)
                playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void OnEnable()
        {
            ApplyCursorLock();
        }

        private void OnDisable()
        {
            ReleaseCursorLock();
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

            // The serialized sensitivity is the rig's own scale; the setting is a multiplier on top
            // of it, so tuning the prefab and the player's preference stay independent.
            // Eased on the aim blend rather than switched on the button, so the sensitivity change
            // arrives with the weapon rather than a fifth of a second before it. Never written back
            // to GameSettings — that is the player's own preference and must survive aiming.
            float aimScale = aimRig != null
                ? Mathf.Lerp(1f, aimSensitivity, aimRig.AimBlend)
                : 1f;

            float scaled = sensitivity * GameSettings.MouseSensitivity * aimScale;

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
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
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
