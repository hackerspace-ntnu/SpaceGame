using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Core;
using SpaceGame.Characters;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The view from a seat during the arrival: look where you like, while the hull shakes.
    ///
    /// <para>
    /// <b>The look turns the PLAYER'S HEAD, and the camera rides it.</b> This component reads the
    /// mouse and hands the movement to <see cref="PlayerHeadLook"/>, which is the one thing that
    /// clamps it and the one thing that turns the bones; the camera is then posed from the very
    /// same rotation. It is not two aims kept in step — there is one angle pair, used twice, so
    /// the view cannot drift off the head it is riding. The previous version rotated the camera
    /// alone, which is invisible to you and left everyone else in the cabin looking at a crewmate
    /// staring rigidly ahead through the entire descent.
    /// </para>
    ///
    /// <para>
    /// <b>Why the camera is posed rather than parented to the head bone.</b> Riding the bone
    /// literally would also inherit every wobble the seated clip puts through the neck, and a
    /// first-person camera that inherits animation noise is the classic way to make people ill —
    /// the same dosing argument as GDC-L1-FEEL-0006, applied to a source the player cannot turn
    /// off. Sharing the rotation and keeping the authored eye position gives the identical read
    /// (the head goes exactly where the view goes) with none of that.
    /// </para>
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
    /// for its own rotation is a conflict with no upside — and it is exactly why the neck has to
    /// carry the horizontal look while seated.
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
    // put in its seat this frame rather than to one still holding last frame's pose. Before
    // PlayerHeadLook (950), so the look handed over below is on the bones the same frame it is on
    // the camera.
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public class ArrivalCameraRig : MonoBehaviour
    {
        [Tooltip("Degrees per second of view movement per unit of look input.")]
        [SerializeField] private float lookSensitivity = 180f;

        [Tooltip("Peak camera displacement at full shake, in metres.")]
        [SerializeField] private float maxShakeTranslation = 0.14f;

        [Tooltip("Peak camera rotation at full shake, in degrees.")]
        [SerializeField] private float maxShakeRotation = 1.6f;

        [Tooltip("Shake oscillations per second. Higher reads as a rattle, lower as a wallow.")]
        [SerializeField] private float shakeFrequency = 20f;

        private InputAction lookAction;
        private bool forcedLookAction;

        /// <summary>
        /// Who actually holds the look. Resolved from the parents because this component is added
        /// to the CAMERA, and the head look lives on the player root with everything else that has
        /// to keep running on a remote copy.
        /// </summary>
        private PlayerHeadLook headLook;

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

        /// <summary>True once the rig has bound its lifetime to the seat instead of the cutscene.</summary>
        private bool releaseWithSeat;

        /// <summary>
        /// Keeps the rig alive past its cutscene, as the landed rider's look, until this machine's
        /// player stands up.
        ///
        /// <para>
        /// The rig IS the in-chair look — it feeds <see cref="PlayerHeadLook"/>'s clamped neck and
        /// poses the camera from the same rotation — and the chair outlives the cutscene: the crew
        /// sit blurred in the wreck and look around before choosing to get up. <c>PlayerLook</c>
        /// cannot take over here because it spends yaw rotating the player's BODY, which is the
        /// wrong thing for someone strapped into a seat (and <c>SeatedRider</c> suspends it for
        /// exactly that reason). Standing up is the moment the body becomes the player's own
        /// again, so that is the moment this destroys itself and hands everything back.
        /// </para>
        /// </summary>
        public void ReleaseWithSeat()
        {
            if (releaseWithSeat) return;

            releaseWithSeat = true;
            SpaceGame.Gameplay.Arrival.SeatedRider.LocalPlayerReleased += OnSeatReleased;
        }

        private void OnSeatReleased() => Destroy(this);

        private void OnDestroy()
        {
            if (releaseWithSeat)
                SpaceGame.Gameplay.Arrival.SeatedRider.LocalPlayerReleased -= OnSeatReleased;
        }

        private void OnEnable()
        {
            // Captured before anything is written, so the pose put back on exit is the one the
            // prefab authored rather than one this component invented.
            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
            baseCaptured = true;

            headLook = GetComponentInParent<PlayerHeadLook>();

            // Declared, not assumed. Nothing else can work out that this player has been strapped
            // into a chair, and a head left in Free mode would answer zero yaw for the whole
            // descent — the view would turn and the character would not.
            if (headLook != null) headLook.Mode = HeadAimMode.Seated;
            else
                // Loud, because the visible symptom is a view that will not turn at all: the look
                // is the head's now, and without a head there is nothing to look with.
                Debug.LogError("[ArrivalCameraRig] No PlayerHeadLook above this camera, so the " +
                               "seated look has nothing to turn. PlayerViewNetwork adds one to " +
                               "every player — this camera is not on one.", this);

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

            // Handed back, so the head unwinds to centre as the player stands up and their body
            // starts carrying the yaw again. Left in Seated mode the neck would keep whatever angle
            // the descent ended on for the rest of the session.
            if (headLook != null) headLook.Mode = HeadAimMode.Free;

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
            float pitchInput = GameSettings.InvertLookY ? -look.y : look.y;

            // Handed over rather than integrated here. The head look owns the angles, the clamps
            // and the mode; this component owns the input and the camera. Two integrators would be
            // two clamps, and the head and the view would disagree at exactly the extremes where a
            // player is most likely to notice.
            Quaternion aim = Quaternion.identity;

            if (headLook != null)
            {
                headLook.AddLook(look.x * scaled, -pitchInput * scaled);
                aim = headLook.LookRotation;
            }

            Vector3 offset = ShakeMath.Displacement(ShakeIntensity, GameSettings.CameraShakeIntensity,
                                                    maxShakeTranslation, Time.time, shakeFrequency);

            // Rotational shake is derived from the same displacement so the two stay in phase. A
            // camera whose position and angle rattle independently reads as two separate faults
            // rather than one hull coming apart.
            float rotationScale = maxShakeTranslation > 0f
                ? maxShakeRotation / maxShakeTranslation
                : 0f;

            Quaternion rattle = Quaternion.Euler(offset.y * rotationScale,
                                                 offset.x * rotationScale,
                                                 offset.z * rotationScale);

            // Both are offsets FROM the authored head pose, not replacements for it. The shake is
            // applied INSIDE the aim rather than added to its Euler angles, so it stays a rattle of
            // the view along whatever direction the head is pointing.
            transform.localPosition = basePosition + offset;
            transform.localRotation = baseRotation * aim * rattle;
        }
    }
}
