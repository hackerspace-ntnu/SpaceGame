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
