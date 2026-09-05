// Mounted look input, perspective toggle, and third/first-person camera placement.
// Lives on MountModule so every mount (steered or not) gets the camera system.
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Core;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    public partial class MountModule
    {
        private void ResolveCameraInputActions()
        {
            if (InputSystem.actions == null)
                return;
            lookAction = InputSystem.actions.FindAction(lookActionName);
        }

        private void EnsureLookActionEnabled()
        {
            if (lookAction == null || lookAction.enabled)
                return;
            lookAction.Enable();
            forcedLookActionEnabled = true;
        }

        private void HandleLookInput(float deltaTime)
        {
            Vector2 lookInput = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

            // The serialized sensitivity is this mount's own scale and the setting is a multiplier
            // on top, matching PlayerLook exactly. Mounted look used to read neither the player's
            // sensitivity preference nor their invert-Y — so every option in the menu silently
            // stopped applying the moment you climbed on something.
            float scaled = lookSensitivity * GameSettings.MouseSensitivity * deltaTime;
            float pitchInput = GameSettings.InvertLookY ? -lookInput.y : lookInput.y;

            bool firstPerson = activePerspective == CameraPerspective.FirstPerson;

            cameraYawOffset = MountLookMath.WrapAngle(cameraYawOffset + lookInput.x * scaled);
            if (firstPerson)
                cameraYawOffset = MountLookMath.ClampYaw(cameraYawOffset, firstPersonYawClamp);
            mountedPitch = Mathf.Clamp(mountedPitch - pitchInput * scaled, -lookPitchClamp, lookPitchClamp);
            orbitPitch = Mathf.Clamp(orbitPitch - pitchInput * scaled, orbitPitchMin, orbitPitchMax);

            // Any look input counts, not just yaw. Testing the x axis alone meant that tilting the
            // camera up at the ostrich's head while parked out on its flank left the yaw timer
            // running, and the side view you were holding crept out from under you mid-look.
            if (lookInput.sqrMagnitude > 0.0001f)
                timeSinceLastLookInput = 0f;
            else
                timeSinceLastLookInput += deltaTime;

            // The drift home is the ORBIT's behaviour and stays there. A third-person camera has a
            // neutral worth returning to — behind the vehicle — and a flank view that never came
            // back would strand the camera out to one side for the rest of the ride. A head has no
            // such neutral: it points where the person is looking, and turning it back on its own
            // is the view overriding the player rather than serving them. A passenger watching the
            // dunes go past would have the window slide out from under them every three seconds.
            if (!firstPerson)
                cameraYawOffset = MountLookMath.StepRecentre(cameraYawOffset, timeSinceLastLookInput,
                                                             cameraAutoAlignDelay, cameraAutoAlignSpeed,
                                                             deltaTime);
            orbitPitch = MountLookMath.StepRecentre(orbitPitch, timeSinceLastLookInput,
                                                    cameraAutoAlignDelay, cameraAutoAlignSpeed,
                                                    deltaTime);

            // Yaw as well as pitch. Without the Y term the look stick's whole horizontal axis was
            // read, accumulated into cameraYawOffset and then dropped on the floor in first person
            // — the camera answered half the control, so a seated rider could look up and down and
            // not left or right.
            if (mountedFirstPersonCameraRoot)
                mountedFirstPersonCameraRoot.localRotation =
                    Quaternion.Euler(mountedPitch, cameraYawOffset, 0f);
        }

        /// <summary>
        /// Put this machine's view into <paramref name="perspective"/>.
        ///
        /// <para>
        /// Tracked on every machine, because <c>activePerspective</c> is what LateUpdate reads and
        /// what a later toggle flips — but ACTED ON only for the local rider. Everything below the
        /// guard is one person's view of the world: their cameras, their audio listener, and a
        /// static shader flag with exactly one correct writer per process. Running it for somebody
        /// else's rider is what spawned a second third-person camera on every peer in the session
        /// the moment one player climbed into a saddle.
        /// </para>
        /// </summary>
        private void ApplyPerspective(CameraPerspective perspective)
        {
            activePerspective = perspective;
            bool firstPerson = activePerspective == CameraPerspective.FirstPerson;

            // The rider's head takes care of itself: PlayerLook hides it per camera render, only
            // for the camera that IS this rider's first-person view. The orbit camera below is a
            // different camera, so switching to it shows the head with no toggle here.
            if (!RiderIsLocal)
                return;

            if (firstPerson)
            {
                SetThirdPersonCameraEnabled(false);
                SetFirstPersonCameraEnabled(true);
                SetMountedVisorEnabled(true);
            }
            else
            {
                SetFirstPersonCameraEnabled(false);
                SetThirdPersonCameraEnabled(true);
                SetMountedVisorEnabled(false);
            }

            PublishRiderAimView(firstPerson);
        }

        /// <summary>
        /// Tell the rider's aim which view they are looking through, and that this machine is
        /// around them.
        ///
        /// <para>
        /// Every aimed item in the game measures from <see cref="AimProvider"/>, and left alone it
        /// measures from the rider's own eye — which mounting has just parented under the seat
        /// marker wearing the seat's rotation. On the ornithopter that seat is rotated ninety
        /// degrees, because a prone pilot faces the floor, so an unattended aim pointed straight
        /// into the sand under the craft while the pilot watched the horizon through the orbit
        /// camera. Third person hands the aim that orbit camera to converge through; first person
        /// hands it nothing but the hull, because there the eye already is the view.
        /// </para>
        /// <para>
        /// Both halves are named after the machine rather than the perspective: whichever camera
        /// the rider is on, a raycast still crosses the fuselage they are strapped inside, and
        /// <c>Physics.IgnoreCollision</c> does nothing to a query.
        /// </para>
        /// </summary>
        private void PublishRiderAimView(bool firstPerson)
        {
            if (mountedAimProvider == null)
                return;

            mountedAimProvider.SetExternalView(firstPerson ? null : runtimeThirdPersonCamera,
                                               transform);
        }

        /// <summary>
        /// Hand this machine's view back to the player's own head at the end of a dismount.
        ///
        /// <para>
        /// The exact mirror of the local half of <see cref="ApplyPerspective"/>, and gated the same
        /// way. On a machine that is only WATCHING somebody dismount there is no mount camera to
        /// switch off, the first-person camera being re-enabled belongs to a remote player whose
        /// camera <c>PlayerController.DisablePlayer</c> deliberately switched off, and the visor
        /// flag is a global with one correct writer.
        /// </para>
        /// </summary>
        private void RestoreLocalViewAfterDismount()
        {
            if (!RiderIsLocal)
                return;

            SetThirdPersonCameraEnabled(false);
            SetFirstPersonCameraEnabled(true);
            SetMountedVisorEnabled(true);

            if (mountedAimProvider != null)
                mountedAimProvider.ClearExternalView();
        }

        private void InitializeMountedViewState()
        {
            float yaw = transform.rotation.eulerAngles.y;
            cameraYaw = yaw;
            cameraYawOffset = 0f;
            timeSinceLastLookInput = 0f;
            mountedPitch = defaultMountedPitch;
            // Zero, not defaultMountedPitch. That value is the first-person head's resting tilt; the
            // orbit's neutral is "wherever thirdPersonOffset already puts the camera", so starting
            // the orbit at 0 keeps the authored framing exactly as authored.
            orbitPitch = 0f;
        }

        private void SetFirstPersonCameraEnabled(bool enabledState)
        {
            if (mountedFirstPersonCamera == null)
                return;
            mountedFirstPersonCamera.enabled = enabledState;
            AudioListener listener = mountedFirstPersonCamera.GetComponent<AudioListener>();
            if (listener)
                listener.enabled = enabledState;
        }

        private void SetThirdPersonCameraEnabled(bool enabledState)
        {
            if (enabledState)
                EnsureRuntimeThirdPersonCamera();

            if (runtimeThirdPersonCamera == null)
                return;

            runtimeThirdPersonCamera.enabled = enabledState;
            AudioListener listener = runtimeThirdPersonCamera.GetComponent<AudioListener>();
            if (listener)
                listener.enabled = enabledState;
        }

        // Default third-person camera prefab loaded from Resources/ when no per-vehicle prefab
        // is wired. Authored with the right URP settings (post-processing on, volume mask, etc.)
        // so every mountable gets sane visuals out of the box.
        // Resources.Load paths are relative to the Resources folder, so this has to
        // track the prefab's subfolder.
        private const string DefaultThirdPersonCameraResourcePath = "Cameras/Mount Third Person Camera";
        private static Camera s_defaultThirdPersonCameraPrefab;

        private static Camera ResolveDefaultThirdPersonCameraPrefab()
        {
            if (s_defaultThirdPersonCameraPrefab != null)
                return s_defaultThirdPersonCameraPrefab;
            s_defaultThirdPersonCameraPrefab = Resources.Load<Camera>(DefaultThirdPersonCameraResourcePath);
            return s_defaultThirdPersonCameraPrefab;
        }

        // Spawn the third-person camera. Prefers thirdPersonCameraPrefab (per-vehicle override),
        // then the project default loaded from Resources/, then a clone of Camera.main, then a
        // bare Camera as last resort.
        //
        // Deliberately spawned unparented. LateUpdate below writes the camera's world pose outright,
        // so parenting it to the mount applies the vehicle's motion twice — once through the parent
        // transform, once through the recomputed target — and the residual smoothing between them
        // shows up as judder (measured: 48% frame-to-frame variance parented vs 2.6% free).
        // Lifetime is handled explicitly by ReleaseRuntimeThirdPersonCamera, not by the hierarchy.
        private void EnsureRuntimeThirdPersonCamera()
        {
            if (runtimeThirdPersonCamera != null)
                return;

            Camera prefabToUse = thirdPersonCameraPrefab != null
                ? thirdPersonCameraPrefab
                : ResolveDefaultThirdPersonCameraPrefab();

            GameObject cameraObject;
            if (prefabToUse != null)
            {
                cameraObject = Object.Instantiate(prefabToUse.gameObject);
                cameraObject.name = $"{name}_MountThirdPersonCamera";
                cameraObject.tag = "Untagged";
                runtimeThirdPersonCamera = cameraObject.GetComponent<Camera>();
                runtimeThirdPersonCamera.targetTexture = null;
            }
            else if (Camera.main != null)
            {
                cameraObject = Object.Instantiate(Camera.main.gameObject);
                cameraObject.name = $"{name}_MountThirdPersonCamera";
                cameraObject.tag = "Untagged";
                foreach (Transform child in cameraObject.transform)
                    Object.Destroy(child.gameObject);
                runtimeThirdPersonCamera = cameraObject.GetComponent<Camera>();
                runtimeThirdPersonCamera.targetTexture = null;
            }
            else
            {
                cameraObject = new GameObject($"{name}_MountThirdPersonCamera");
                runtimeThirdPersonCamera = cameraObject.AddComponent<Camera>();
            }

            if (!cameraObject.GetComponent<AudioListener>())
                cameraObject.AddComponent<AudioListener>();

            // This object is RUNTIME state and must never reach a scene file.
            //
            // It is spawned unparented (see above) into whatever scene is currently open, so in the
            // editor it is an ordinary root object that Ctrl+S writes to disk like any other. Four
            // of them once shipped inside persistentScene that way: enabled, depth -1, display 0,
            // tying with the real Main Camera, so which camera the game rendered through was
            // arbitrary -- and each carried an AudioListener and a StudioListener besides.
            //
            // DontSaveInEditor rather than DontSave: the latter also means "do not destroy on
            // scene load", which for a camera would trade a save-file leak for a cross-scene one.
            cameraObject.hideFlags = HideFlags.DontSaveInEditor;

            // Without the parent to start it in the right place, the first frame must snap rather
            // than lerp — otherwise the camera swoops in from wherever the prefab was authored.
            thirdPersonCameraNeedsSnap = true;
        }

        private void SetMountedVisorEnabled(bool enabledState)
        {
            GlassDistortionRenderFeature.RuntimeEnabled = enabledState;
        }

        private Vector3 GetThirdPersonCameraOffset()
        {
            float resolved = thirdPersonDistance > 0.01f ? thirdPersonDistance : Mathf.Max(0.1f, Mathf.Abs(thirdPersonOffset.z));
            float signedDistance = thirdPersonOffset.z > 0f ? resolved : -resolved;
            return new Vector3(thirdPersonOffset.x, thirdPersonOffset.y, signedDistance);
        }

        private void LateUpdate()
        {
            // Nothing in here exists on a machine whose player is not the rider: the camera is
            // never spawned there, and cameraYaw only feeds that camera.
            if (!IsMounted || !RiderIsLocal)
                return;

            // Light smoothing on the orbit yaw. This is a comfort filter, not the shake fix — the
            // shake came from the motor driving the Rigidbody on the render loop, which made the
            // interpolated pose advance unevenly per frame; that is fixed in the motor's FixedUpdate.
            // Keep this responsive: over-damping here reads as the camera lagging the vehicle.
            float targetYaw = transform.rotation.eulerAngles.y + cameraYawOffset;
            if (thirdPersonCameraNeedsSnap)
                cameraYaw = targetYaw;
            else
                cameraYaw = Mathf.LerpAngle(cameraYaw, targetYaw, 1f - Mathf.Exp(-thirdPersonYawLerp * Time.deltaTime));

            if (activePerspective != CameraPerspective.ThirdPerson || runtimeThirdPersonCamera == null)
                return;

            Transform pivot = thirdPersonPivot
                ? thirdPersonPivot
                : (mountedPlayer ? mountedPlayer : activeSeatPoint);
            if (pivot == null)
                pivot = transform;

            // Position: behind+above the pivot, rotated around by cameraYaw (rider's look-stick yaw).
            //
            // followMountPitch swaps the yaw-only orbit for one built on the mount's full attitude,
            // with the rider's look yaw applied on top of it. A ground vehicle wants the yaw-only
            // form: the horizon stays level however the hull tilts over rough terrain. A flying
            // machine needs the full form, or the camera holds a level horizon through a dive and the
            // pilot sees the ground rise instead of feeling the nose drop.
            Quaternion yawRot = followMountPitch
                ? transform.rotation * Quaternion.Euler(0f, cameraYawOffset, 0f)
                : Quaternion.Euler(0f, cameraYaw, 0f);

            // Elevation. The boom pivots about the rider on the rider's own X axis, so pushing the
            // mouse up swings the camera down and back and it looks up at the mount; pulling down
            // lifts it into a high view. Without this the third-person camera ignored the vertical
            // axis outright — mounted, in the default perspective, you could only look left and
            // right, which is half a look control.
            //
            // Rotating the OFFSET rather than the camera keeps the boom length fixed, so elevation
            // never dollies the mount towards or away from you.
            Quaternion orbitRot = yawRot * Quaternion.Euler(orbitPitch, 0f, 0f);
            Vector3 targetPosition = pivot.position + orbitRot * GetThirdPersonCameraOffset();
            Transform camTransform = runtimeThirdPersonCamera.transform;

            // Aim: look at a point ahead of the pivot at pivot height. Because the camera is
            // above the pivot, LookRotation naturally tilts down — exactly enough to frame both
            // the vehicle and the ground ahead. thirdPersonLookAhead controls how far down.
            Vector3 targetAimPoint = pivot.position + yawRot * (Vector3.forward * thirdPersonLookAhead);

            if (thirdPersonCameraNeedsSnap)
            {
                camTransform.position = targetPosition;
                smoothedAimPoint = targetAimPoint;
                thirdPersonCameraNeedsSnap = false;
            }
            else
            {
                // Exponential decay rather than Lerp(a, b, k * deltaTime): the naive form makes the
                // smoothing strength scale with frame time, so on an uncapped framerate the camera
                // catches up by a different fraction every frame and shimmers behind the vehicle.
                float follow = 1f - Mathf.Exp(-thirdPersonFollowLerp * Time.deltaTime);
                camTransform.position = Vector3.Lerp(camTransform.position, targetPosition, follow);

                // The aim point gets its own, slower filter. Deriving rotation straight from the raw
                // target meant the camera both chased a moving point and pivoted toward it, so the
                // small position error left by the follow lerp was re-expressed as angular jitter at
                // the end of a long boom. Filtering the point the camera looks at breaks that coupling.
                float aim = 1f - Mathf.Exp(-thirdPersonAimLerp * Time.deltaTime);
                smoothedAimPoint = Vector3.Lerp(smoothedAimPoint, targetAimPoint, aim);
            }

            Vector3 aimDir = smoothedAimPoint - camTransform.position;
            if (aimDir.sqrMagnitude > 1e-4f)
                // World up, deliberately, even when followMountPitch is on: the camera follows the
                // mount's pitch but never its ROLL. Rolling the view with a banking aircraft is a
                // reliable way to make people motion-sick, and holding the horizon level is what makes
                // the wings visibly bank against it.
                camTransform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
        }
    }
}
