using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceGame.Items
{
    /// <summary>
    /// The view used while rummaging in a deployed pack: a camera of its own, spawned rather than
    /// borrowed.
    ///
    /// <para>
    /// Precedent is <c>ThirdPersonWalkThroughCutscene</c>, which spawns a <c>CutsceneTempCamera</c>
    /// and switches the player's camera <b>and its AudioListener</b> off for the duration. Moving
    /// the player's own camera instead would leave whatever else reads its transform — the
    /// flashlight's shader globals, the aim ray, <c>PlayerLook</c>'s pitch — pointing at the
    /// ground, and would have to put every one of them back.
    /// </para>
    /// <para>
    /// The pose is authored, not orbited: on the rig's centre axis, 1.9 m back, 1.5 m up, pitched
    /// 38&#176; down, at FOV 40. That triangle is not arbitrary — atan(1.5 / 1.9) is 38.3&#176;, so
    /// the pitch aims the camera at the rig's base by construction. FOV 40 is narrower than
    /// gameplay FOV on purpose: a narrow lens flattens perspective, which is what makes comparing
    /// two items' true sizes honest and makes a placement easy to judge.
    /// </para>
    /// </summary>
    public sealed class PackFocusCamera : MonoBehaviour
    {
        // ── The authored pose ────────────────────────────────────────────────
        //
        // The shot is ON the player→pack axis, facing the pack square. The player is standing on
        // that same axis, so the arrangement only works while the camera lands IN FRONT of their
        // body: the pack is deployed BackpackController.deployDistance (2.4 m) out, the camera
        // sits DistanceOut back from it, and the difference — 0.5 m — is where the lens ends up,
        // ahead of the player at head height with their body behind the near plane. Grow
        // DistanceOut past deployDistance and the player is back between the lens and the pack,
        // filling the frame.
        private const float DistanceOut = 1.9f;
        private const float HeightUp = 1.5f;
        private const float PitchDown = 38f;
        private const float Fov = 40f;

        // ── The arrival ──────────────────────────────────────────────────────
        //
        // The pack's own arc is 0.9 s (BackpackController.deploySeconds). Starting 0.15 s in and
        // taking 0.9 s means the camera settles a breath after the rig does. It deliberately does
        // NOT wait for the unfold to finish: this is an interaction performed hundreds of times a
        // session, and 1.4 s of nothing at the front of it is the difference between a pocket and
        // a cutscene.
        private const float FlyInDelay = 0.15f;
        private const float FlyInSeconds = 0.9f;

        // ── Parallax ─────────────────────────────────────────────────────────
        //
        // Not a control. The cursor is doing something else — it is picking items up — and this
        // rides along with it to give the flat, narrow-lens view some depth. Small enough that a
        // player never has to think about steering it, which is why the numbers are degrees and
        // not a sensitivity.
        private const float MaxYawOffset = 6f;
        private const float MaxPitchOffset = 4f;
        private const float ParallaxSmoothTime = 0.25f;

        // ── Depth of field ───────────────────────────────────────────────────
        private const float Aperture = 2.2f;
        private const float FocalLength = 65f;

        private Transform rig;
        private Vector3 viewDirection = Vector3.forward;

        private Camera cam;
        private Camera playerCamera;
        private AudioListener playerListener;
        private bool playerCameraWasEnabled;
        private bool playerListenerWasEnabled;

        private Volume volume;
        private VolumeProfile profile;
        private DepthOfField dof;

        private Pose flyFrom;
        private float flyFromFov = 60f;
        private float flyElapsed;
        private bool flying;

        private float yawOffset;
        private float pitchOffset;
        private float yawVelocity;
        private float pitchVelocity;

        /// <summary>The spawned camera. Null once <see cref="Dismiss"/> has run.</summary>
        public Camera Camera => cam;

        /// <summary>True once the fly-in has finished and the pose is the authored one.</summary>
        public bool Settled => !flying;

        /// <summary>
        /// Puts a focus camera on <paramref name="rig"/>.
        /// </summary>
        /// <param name="rig">The deployed pack. Tracked live rather than sampled once, because it
        /// is still mid-arc when this is called and lands under the camera as it arrives.</param>
        /// <param name="viewDirection">The player→rig line, flattened. The camera sits back along
        /// it, between the player and the pack, facing the pack square-on. Frozen at spawn so
        /// the shot cannot swing while the player's body drifts.</param>
        /// <param name="playerCamera">Switched off, with its AudioListener, for the duration.</param>
        public static PackFocusCamera Spawn(Transform rig, Vector3 viewDirection, Camera playerCamera)
        {
            if (rig == null) return null;

            var go = new GameObject("PackFocusCamera");
            var focus = go.AddComponent<PackFocusCamera>();
            focus.Begin(rig, viewDirection, playerCamera);
            return focus;
        }

        private void Begin(Transform target, Vector3 direction, Camera fromCamera)
        {
            rig = target;

            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            viewDirection = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;

            playerCamera = fromCamera;

            // The pose it flies FROM: the player's eyes. Captured now, before the handover, so the
            // shot starts where the player was actually looking. With no player camera to borrow
            // from — a test scene, a spectator — there is nothing to fly from and it simply starts
            // where it ends.
            flyFrom = playerCamera != null
                ? new Pose(playerCamera.transform.position, playerCamera.transform.rotation)
                : TargetPose();
            flyFromFov = playerCamera != null ? playerCamera.fieldOfView : Fov;

            cam = gameObject.AddComponent<Camera>();
            cam.fieldOfView = Fov;
            cam.nearClipPlane = playerCamera != null ? playerCamera.nearClipPlane : 0.05f;
            cam.farClipPlane = playerCamera != null ? playerCamera.farClipPlane : 1000f;
            cam.cullingMask = playerCamera != null ? playerCamera.cullingMask : ~0;

            // Held back until the delay is up: two enabled cameras with no depth between them is
            // undefined, and the player is meant to still be looking through their own for the
            // first 0.15 s.
            cam.enabled = false;

            transform.SetPositionAndRotation(flyFrom.position, flyFrom.rotation);

            BuildDepthOfField();

            StartCoroutine(FlyIn());
        }

        /// <summary>Puts the player's camera and ears back. Safe to call twice.</summary>
        public void Dismiss()
        {
            if (playerCamera != null) playerCamera.enabled = playerCameraWasEnabled;
            if (playerListener != null) playerListener.enabled = playerListenerWasEnabled;

            playerCamera = null;
            playerListener = null;

            if (profile != null) Destroy(profile);
            profile = null;

            if (this != null && gameObject != null) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // Belt and braces. A focus camera destroyed by a scene load rather than by Dismiss
            // would otherwise leave the player looking through a camera that no longer exists.
            if (playerCamera != null) playerCamera.enabled = playerCameraWasEnabled;
            if (playerListener != null) playerListener.enabled = playerListenerWasEnabled;
            if (profile != null) Destroy(profile);
        }

        private IEnumerator FlyIn()
        {
            for (float wait = 0f; wait < FlyInDelay; wait += Time.unscaledDeltaTime)
                yield return null;

            // Only now does the view actually change hands. Doing it in Begin would black out the
            // first 0.15 s, since this camera is not rendering yet.
            TakeOverFromPlayerCamera();

            flying = true;
            flyElapsed = 0f;
            cam.enabled = true;

            while (flyElapsed < FlyInSeconds)
            {
                flyElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            flying = false;
        }

        private void TakeOverFromPlayerCamera()
        {
            if (playerCamera == null) return;

            playerCameraWasEnabled = playerCamera.enabled;
            playerCamera.enabled = false;

            playerListener = playerCamera.GetComponent<AudioListener>();
            if (playerListener != null)
            {
                playerListenerWasEnabled = playerListener.enabled;
                playerListener.enabled = false;
            }

            // Ours only once theirs is off — two live listeners is a warning per frame and
            // undefined panning.
            if (GetComponent<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        }

        private void LateUpdate()
        {
            if (rig == null) return;

            Pose target = TargetPose();

            UpdateParallax();

            Quaternion aimed = target.rotation * Quaternion.Euler(pitchOffset, yawOffset, 0f);

            if (flying)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(flyElapsed / FlyInSeconds));
                transform.SetPositionAndRotation(
                    Vector3.Lerp(flyFrom.position, target.position, t),
                    Quaternion.Slerp(flyFrom.rotation, aimed, t));
                if (cam != null) cam.fieldOfView = Mathf.Lerp(flyFromFov, Fov, t);
            }
            else
            {
                transform.SetPositionAndRotation(target.position, aimed);
                if (cam != null) cam.fieldOfView = Fov;
            }

            if (dof != null)
                dof.focusDistance.value = Mathf.Max(0.1f, Vector3.Distance(transform.position, rig.position));
        }

        /// <summary>
        /// The authored shot, recomputed every frame off the rig's live position.
        ///
        /// Live rather than sampled once, because the rig is still travelling along its deploy arc
        /// when the camera spawns. Tracking it means the shot converges on the landing pose instead
        /// of framing the patch of sand the pack was over when the key was pressed.
        /// </summary>
        private Pose TargetPose()
        {
            Vector3 position = rig.position - viewDirection * DistanceOut + Vector3.up * HeightUp;
            float yaw = Quaternion.LookRotation(viewDirection, Vector3.up).eulerAngles.y;

            return new Pose(position, Quaternion.Euler(PitchDown, yaw, 0f));
        }

        /// <summary>
        /// Cursor offset from screen centre, damped into a small rotation.
        ///
        /// <see cref="Mathf.SmoothDamp"/> is a critically damped spring, which is the response
        /// asked for — no overshoot, no ringing, and it cannot be outrun by a fast mouse. Driven on
        /// unscaled time because focus mode never stops the clock but something else might.
        /// </summary>
        private void UpdateParallax()
        {
            Vector2 cursor = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

            float halfWidth = Mathf.Max(1f, Screen.width * 0.5f);
            float halfHeight = Mathf.Max(1f, Screen.height * 0.5f);

            float wantYaw = Mathf.Clamp((cursor.x - halfWidth) / halfWidth, -1f, 1f) * MaxYawOffset;
            float wantPitch = -Mathf.Clamp((cursor.y - halfHeight) / halfHeight, -1f, 1f) * MaxPitchOffset;

            float dt = Time.unscaledDeltaTime;
            yawOffset = Mathf.SmoothDamp(yawOffset, wantYaw, ref yawVelocity, ParallaxSmoothTime, Mathf.Infinity, dt);
            pitchOffset = Mathf.SmoothDamp(pitchOffset, wantPitch, ref pitchVelocity, ParallaxSmoothTime, Mathf.Infinity, dt);
        }

        /// <summary>
        /// A runtime volume holding one <see cref="DepthOfField"/>, focused on the rig.
        ///
        /// <para>
        /// It is put on the pack-item layer and this camera's <c>volumeLayerMask</c> is widened to
        /// include that layer, so the blur belongs to this camera alone. A global volume on the
        /// Default layer would also be picked up by the player's camera — and by every other
        /// camera in the scene — which would soften the world for anyone who happened to be
        /// looking at it while somebody else opened a pack.
        /// </para>
        /// <para>
        /// The profile is a <c>ScriptableObject</c> created with no asset behind it, so it is
        /// destroyed explicitly in <see cref="Dismiss"/>; Unity does not collect those with their
        /// GameObject.
        /// </para>
        /// </summary>
        private void BuildDepthOfField()
        {
            int volumeLayer = BackpackItemVisual.ItemLayer;
            if (volumeLayer < 0) return;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "PackFocusDof";

            dof = profile.Add<DepthOfField>(overrides: true);
            dof.mode.overrideState = true;
            dof.mode.value = DepthOfFieldMode.Bokeh;
            dof.focusDistance.overrideState = true;
            dof.focusDistance.value = DistanceOut;
            dof.aperture.overrideState = true;
            dof.aperture.value = Aperture;
            dof.focalLength.overrideState = true;
            dof.focalLength.value = FocalLength;

            var volumeGo = new GameObject("PackFocusVolume") { layer = volumeLayer };
            volumeGo.transform.SetParent(transform, false);

            volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = profile;

            UniversalAdditionalCameraData data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();

            data.renderPostProcessing = true;
            data.volumeLayerMask = data.volumeLayerMask | (1 << volumeLayer);
        }
    }
}
