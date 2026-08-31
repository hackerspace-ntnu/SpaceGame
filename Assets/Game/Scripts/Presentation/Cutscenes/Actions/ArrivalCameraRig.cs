using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The view from a seat during the arrival: look where you like, while the hull shakes.
    ///
    /// <para>
    /// <b>Why it reads the raw action.</b> The cutscene runs with the player's input switched off —
    /// that is what stops them walking out of the chair — and <c>PlayerInputManager</c> zeroes its
    /// look axis in <c>OnDisable</c>, so anything reading <c>LookInput</c> gets a permanently still
    /// camera. Leaving that component enabled instead is worse: jump and dash are delivered as
    /// EVENTS whose handlers fire regardless of <c>PlayerMovement.enabled</c>, so the player could
    /// leap out of the seat mid-descent. <c>MountModule.Camera.cs</c> hit this first and solved it
    /// by going to <c>InputSystem.actions</c> directly; this does the same.
    /// </para>
    ///
    /// <para>
    /// <b>Why it does not use <c>PlayerLook</c>.</b> That component spends its yaw by turning the
    /// player's RIGIDBODY in FixedUpdate. The body here is held at a seat pose that
    /// <c>SeatedRider</c> rewrites every frame, and has been made kinematic; having it also fight
    /// for its own rotation is a conflict with no upside. Pitch and yaw are applied to the camera
    /// alone.
    /// </para>
    ///
    /// <para>
    /// <b>Everything it writes is an OFFSET from the camera's authored pose</b>, which is the head —
    /// about (0, 1.45, 0.16) on the player root, never the identity. Assigning to
    /// <c>localPosition</c> instead of adding to it drops the view to chest height for the whole
    /// descent, and "restoring" by zeroing leaves it there for the rest of the session.
    /// </para>
    ///
    /// <para>
    /// Look and shake are one component and one LateUpdate on purpose. As two components they would
    /// both write the same transform in an order Unity does not define, and the loser's
    /// contribution would vanish on an arbitrary subset of frames.
    /// </para>
    /// </summary>
    // After SeatedRider (100), so the camera's offsets are applied to a body that has already been
    // put in its seat this frame rather than to one still holding last frame's pose.
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public class ArrivalCameraRig : MonoBehaviour
    {
        [Tooltip("Degrees per second of view movement per unit of look input.")]
        [SerializeField] private float lookSensitivity = 180f;

        [Tooltip("How far up and down the view may travel from the seat's forward. Stops a seated " +
                 "player rolling their view past vertical, which reads as a bug rather than a look.")]
        [SerializeField] private float pitchClamp = 75f;

        [Tooltip("How far left and right. Generous — the point is to see the cabin and your crew — " +
                 "but not unlimited, because a strapped-in body cannot turn to look behind itself.")]
        [SerializeField] private float yawClamp = 110f;

        [Tooltip("Peak camera displacement at full shake, in metres.")]
        [SerializeField] private float maxShakeTranslation = 0.14f;

        [Tooltip("Peak camera rotation at full shake, in degrees.")]
        [SerializeField] private float maxShakeRotation = 1.6f;

        [Tooltip("Shake oscillations per second. Higher reads as a rattle, lower as a wallow.")]
        [SerializeField] private float shakeFrequency = 20f;

        private InputAction lookAction;
        private bool forcedLookAction;

        private float yaw;
        private float pitch;

        /// <summary>
        /// The camera's authored pose on the player, captured on the way in and put back on the way
        /// out.
        ///
        /// <para>
        /// It is NOT the identity: the camera sits at roughly (0, 1.45, 0.16) on the player root,
        /// which is the head. A rig that assigns its shake straight to <c>localPosition</c> drops
        /// the view to the pivot — about chest height — for the whole descent, and a rig that
        /// "restores" by zeroing leaves it there permanently, because zero was never where it
        /// started. Both of those were the first version of this file.
        /// </para>
        /// </summary>
        private Vector3 basePosition;
        private Quaternion baseRotation;
        private bool baseCaptured;

        /// <summary>
        /// How hard to shake right now, 0..1. Driven by <see cref="ArrivalCutscene"/>'s beats; the
        /// player's own intensity preference is applied on top, inside <see cref="ShakeMath"/>.
        /// </summary>
        public float ShakeIntensity { get; set; }

        private void OnEnable()
        {
            // Captured before anything is written, so the pose put back on exit is the one the
            // prefab authored rather than one this component invented.
            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
            baseCaptured = true;

            if (InputSystem.actions != null)
                lookAction = InputSystem.actions.FindAction("Look");

            if (lookAction != null && !lookAction.enabled)
            {
                lookAction.Enable();
                forcedLookAction = true;
            }
        }

        private void OnDisable()
        {
            // Only ever undone if we were the ones who turned it on. Disabling an action somebody
            // else enabled is how a player ends up unable to look around after the cutscene ends.
            if (forcedLookAction && lookAction != null)
            {
                lookAction.Disable();
                forcedLookAction = false;
            }

            // Handed back exactly as it was found. Zeroing instead would move the camera off the
            // head and leave it there for the rest of the session — the shake is an offset FROM the
            // authored pose, never a replacement for it.
            if (baseCaptured)
            {
                transform.localPosition = basePosition;
                transform.localRotation = baseRotation;
            }
        }

        private void LateUpdate()
        {
            Vector2 look = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

            float scaled = lookSensitivity * GameSettings.MouseSensitivity * Time.deltaTime;

            yaw = Mathf.Clamp(yaw + look.x * scaled, -yawClamp, yawClamp);

            float pitchInput = GameSettings.InvertLookY ? -look.y : look.y;
            pitch = Mathf.Clamp(pitch - pitchInput * scaled, -pitchClamp, pitchClamp);

            Vector3 offset = ShakeMath.Displacement(ShakeIntensity, GameSettings.CameraShakeIntensity,
                                                    maxShakeTranslation, Time.time, shakeFrequency);

            // Rotational shake is derived from the same displacement so the two stay in phase. A
            // camera whose position and angle rattle independently reads as two separate faults
            // rather than one hull coming apart.
            float rotationScale = maxShakeTranslation > 0f
                ? maxShakeRotation / maxShakeTranslation
                : 0f;

            // Both are offsets FROM the authored head pose, not replacements for it.
            transform.localPosition = basePosition + offset;
            transform.localRotation = baseRotation * Quaternion.Euler(pitch + offset.y * rotationScale,
                                                                      yaw + offset.x * rotationScale,
                                                                      offset.z * rotationScale);
        }
    }
}
