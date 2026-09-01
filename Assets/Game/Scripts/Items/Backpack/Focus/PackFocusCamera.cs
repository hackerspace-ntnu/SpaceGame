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
    /// The pose is authored, not orbited: on the rig's centre axis, 3.69 m out on the side of it
    /// AWAY from the player, 2.25 m up, pitched 38&#176; down, at FOV 40, looking back down the
    /// player&#8594;pack line. The distance was 1.9 m until the 2026-08-25 board deepening pushed
    /// the leading edge from 0.94 m to 1.50 m out from the rig root and left the near cells
    /// 0.40 m from the lens, cropped out of the bottom of the frame; the lens moved back by
    /// exactly the growth — 0.56 m — so the near edge sits the 0.96 m from the lens it always
    /// did. The 38&#176; pitch aimed the camera at the rig's base by construction while
    /// atan(1.5 / 1.9) was 38.3&#176;; from 2.46 m it centred a half-step short of the base, on
    /// the mat itself, which is where the items are. FOV 40 is narrower than
    /// gameplay FOV on purpose: a narrow lens flattens perspective, which is what makes comparing
    /// two items' true sizes honest and makes a placement easy to judge.
    /// </para>
    /// <para>
    /// <b>2.46 and 1.5 became 3.69 and 2.25 with the 2026-09-01 enlargement, and the framing did
    /// not move.</b> Both are offsets from the rig's own origin, so multiplying them by
    /// <see cref="PackScale.Factor"/> — exactly what the rig, the faces and the gear were
    /// multiplied by — makes the shot a similarity transform of the one before it: the same solid
    /// angle, the same pitch, the same field of view, every item filling the same fraction of the
    /// frame. Leaving the lens where it was would have put it a third of the way inside a mat that
    /// is now 3.12 m deep. What DOES change, and is the point, is that the pack now fills that
    /// frame with 1.5x the linear detail relative to the screen it is drawn on.
    /// </para>
    /// </summary>
    public sealed class PackFocusCamera : MonoBehaviour
    {
        // ── The authored pose ────────────────────────────────────────────────
        //
        // The shot is ON the player→pack axis, facing the pack square — from the FAR side of the
        // rig, looking back down that axis. The mat unfolds away from the player's feet, so a lens
        // on the player's own side of the pack frames the closed harness back and none of the
        // items; crossing over is what puts the mat, and everything laid out on it, toward the
        // camera.
        //
        // The player's body is consequently IN the shot, standing beyond the rig at
        // BackpackController.deployDistance + DistanceOut (6.6 m) from the lens, small in a 40°
        // lens and mostly behind the raised board. It reads as the owner kneeling over their pack
        // rather than as an obstruction, and nothing about the framing depends on the two
        // distances any more — the lens can no longer end up between the player and the pack.
        //
        // Both distances are offsets from the RIG's origin, so they take PackScale.Factor with the
        // rig. The angles do not, and must not: a similarity transform leaves every angle alone,
        // which is exactly why the enlarged pack frames identically from here.
        private static readonly float DistanceOut = PackScale.Apply(2.46f);
        private static readonly float HeightUp = PackScale.Apply(1.5f);
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
        //
        // They are added to the shot's YAW and PITCH as numbers, never multiplied onto its
        // rotation. Composing them the other way — target.rotation * Euler(pitch, yaw, 0) — turns
        // the yaw about the camera's OWN up axis, which a 38° pitch has already tipped out of
        // vertical, and the horizon comes out rolled by sin(PitchDown) * yawOffset: up to 3.7°,
        // proportional to how far off centre the cursor sits, so the whole view sat very slightly
        // crooked whenever the mouse was not dead centre. Adding the angles keeps roll at exactly
        // zero at every cursor position.
        private const float MaxYawOffset = 6f;
        private const float MaxPitchOffset = 4f;
        private const float ParallaxSmoothTime = 0.25f;

        // ── Depth of field ───────────────────────────────────────────────────
        private const float Aperture = 2.2f;
        private const float FocalLength = 65f;

        private Transform rig;

        // Where the LENS looks: down the player→pack line, from beyond the pack back toward the
        // player. The reverse of what the caller hands over, reversed once on the way in.
        private Vector3 lensForward = Vector3.forward;

        private Camera cam;
        private Camera playerCamera;
        private AudioListener playerListener;
        private bool playerCameraWasEnabled;
        private bool playerListenerWasEnabled;

        private Volume volume;
        private VolumeProfile profile;
        private DepthOfField dof;

        // The flight is interpolated as position + yaw + pitch, not as a pose: Quaternion.Slerp
        // takes the geodesic between two rotations, and between the player's eyeline and a shot
        // 180° round the other side of the pack that path rolls through 19° at the halfway point —
        // the camera cartwheels on its way over. Lerping the two angles keeps it level throughout.
        private Vector3 flyFromPosition;
        private float flyFromYaw;
        private float flyFromPitch;
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
        /// <param name="viewDirection">The player→rig line, flattened. The camera sits DistanceOut
        /// PAST the rig along it and looks back down it, so the mat faces the lens square-on and
        /// the player stands beyond the pack. Frozen at spawn so the shot cannot swing while the
        /// player's body drifts.</param>
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

            // Reversed here, once: the caller measures player→pack, the lens looks pack→player.
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            lensForward = flat.sqrMagnitude > 1e-6f ? -flat.normalized : Vector3.forward;

            playerCamera = fromCamera;

            // The pose it flies FROM: the player's eyes. Captured now, before the handover, so the
            // shot starts where the player was actually looking. With no player camera to borrow
            // from — a test scene, a spectator — there is nothing to fly from and it simply starts
            // where it ends.
            if (playerCamera != null)
            {
                Vector3 eye = playerCamera.transform.rotation.eulerAngles;
                flyFromPosition = playerCamera.transform.position;
                flyFromPitch = eye.x;
                flyFromYaw = eye.y;
                flyFromFov = playerCamera.fieldOfView;
            }
            else
            {
                flyFromPosition = LensPosition();
                flyFromPitch = PitchDown;
                flyFromYaw = LensYaw();
                flyFromFov = Fov;
            }

            cam = gameObject.AddComponent<Camera>();
            cam.fieldOfView = Fov;
            cam.nearClipPlane = playerCamera != null ? playerCamera.nearClipPlane : 0.05f;
            cam.farClipPlane = playerCamera != null ? playerCamera.farClipPlane : 1000f;
            cam.cullingMask = playerCamera != null ? playerCamera.cullingMask : ~0;

            // Held back until the delay is up: two enabled cameras with no depth between them is
            // undefined, and the player is meant to still be looking through their own for the
            // first 0.15 s.
            cam.enabled = false;

            transform.SetPositionAndRotation(flyFromPosition, Quaternion.Euler(flyFromPitch, flyFromYaw, 0f));

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

            Vector3 targetPosition = LensPosition();

            UpdateParallax();

            // Roll is not a term anywhere in here. Both angles are absolute — measured about world
            // up and about the horizontal — so whatever the cursor and the flight are doing, the
            // horizon stays level.
            float aimPitch = PitchDown + pitchOffset;
            float aimYaw = LensYaw() + yawOffset;

            if (flying)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(flyElapsed / FlyInSeconds));
                transform.SetPositionAndRotation(
                    Vector3.Lerp(flyFromPosition, targetPosition, t),
                    Quaternion.Euler(Mathf.LerpAngle(flyFromPitch, aimPitch, t),
                                     Mathf.LerpAngle(flyFromYaw, aimYaw, t),
                                     0f));
                if (cam != null) cam.fieldOfView = Mathf.Lerp(flyFromFov, Fov, t);
            }
            else
            {
                transform.SetPositionAndRotation(targetPosition, Quaternion.Euler(aimPitch, aimYaw, 0f));
                if (cam != null) cam.fieldOfView = Fov;
            }

            if (dof != null)
                dof.focusDistance.value = Mathf.Max(0.1f, Vector3.Distance(transform.position, rig.position));
        }

        /// <summary>
        /// Where the lens sits, recomputed every frame off the rig's live position.
        ///
        /// Live rather than sampled once, because the rig is still travelling along its deploy arc
        /// when the camera spawns. Tracking it means the shot converges on the landing pose instead
        /// of framing the patch of sand the pack was over when the key was pressed.
        /// </summary>
        private Vector3 LensPosition() =>
            rig.position - lensForward * DistanceOut + Vector3.up * HeightUp;

        /// <summary>
        /// Which way the shot faces, as a compass angle about world up.
        ///
        /// Frozen with <see cref="lensForward"/> at spawn, so unlike the position this does not
        /// move — but it is a number rather than a rotation on purpose: the pitch and the cursor
        /// parallax are added to it, and a rotation composed instead of added is what puts roll in
        /// a shot that should be level.
        /// </summary>
        private float LensYaw() => Quaternion.LookRotation(lensForward, Vector3.up).eulerAngles.y;

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
