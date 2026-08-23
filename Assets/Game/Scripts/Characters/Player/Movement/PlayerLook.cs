using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
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
        public Transform playerHead; 
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
        private SkinnedMeshRenderer headRenderer;

        private Camera lookCamera;

        private void Start()
        {
            inputs = GetComponent<PlayerController>().Input;
            playerRigidbody = playerBody.GetComponent<Rigidbody>();

            // Hide the player head mesh to prevent clipping with the camera
            headRenderer = playerHead.GetComponent<SkinnedMeshRenderer>();
            headRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

            lookCamera = playerCamera != null ? playerCamera.GetComponent<Camera>() : null;

            // The field of view authored on the prefab is the slider's starting point, adopted only
            // while the player has never moved it — otherwise every launch would overwrite what
            // they chose with whatever the prefab happens to say.
            if (lookCamera != null) GameSettings.SeedFieldOfView(lookCamera.fieldOfView);

            GameSettings.Changed += ApplySettings;
            ApplySettings();
        }

        private void OnDestroy() => GameSettings.Changed -= ApplySettings;

        private void ApplySettings()
        {
            if (lookCamera != null)
                lookCamera.fieldOfView = GameSettings.FieldOfView;
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

        public void SetHeadVisible(bool visible)
        {
            if (!headRenderer) return;
            headRenderer.shadowCastingMode = visible
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
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
            lookInput = inputs.LookInput;

            // The serialized sensitivity is the rig's own scale; the setting is a multiplier on top
            // of it, so tuning the prefab and the player's preference stay independent.
            float scaled = sensitivity * GameSettings.MouseSensitivity;

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
