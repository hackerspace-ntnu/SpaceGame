using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A camera of its own, spawned rather than borrowed, for the moments a player looks closely at
    /// one thing — a deployed pack, their own body.
    ///
    /// <para>
    /// Precedent is <c>ThirdPersonWalkThroughCutscene</c>, which spawns a <c>CutsceneTempCamera</c>
    /// and switches the player's camera <b>and its AudioListener</b> off for the duration. Moving
    /// the player's own camera instead would leave whatever else reads its transform — the
    /// flashlight's shader globals, the aim ray, <c>PlayerLook</c>'s pitch — pointing at the
    /// ground, and would have to put every one of them back.
    /// </para>
    /// <para>
    /// This class is the mechanics: the handover, the level flight in (and, for cameras that want
    /// it, back out), the cursor parallax, the depth of field, the dismissal. A subclass authors
    /// the SHOT — where the lens sits, which way it faces, how far down it looks, its field of
    /// view and what it focuses on — through the abstract members below.
    /// </para>
    /// </summary>
    public abstract class FocusCamera : MonoBehaviour
    {
        // ── Parallax ─────────────────────────────────────────────────────────
        //
        // Not a control. The cursor is doing something else — it is picking items up — and this
        // rides along with it to give the flat, narrow-lens view some depth. Small enough that a
        // player never has to think about steering it, which is why the numbers are degrees and
        // not a sensitivity.
        //
        // They are added to the shot's YAW and PITCH as numbers, never multiplied onto its
        // rotation. Composing them the other way — target.rotation * Euler(pitch, yaw, 0) — turns
        // the yaw about the camera's OWN up axis, which a pitched shot has already tipped out of
        // vertical, and the horizon comes out rolled by sin(pitch) * yawOffset, proportional to
        // how far off centre the cursor sits. Adding the angles keeps roll at exactly zero.
        private const float MaxYawOffset = 6f;
        private const float MaxPitchOffset = 4f;
        private const float ParallaxSmoothTime = 0.25f;

        // ── Depth of field ───────────────────────────────────────────────────
        private const float Aperture = 2.2f;
        private const float FocalLength = 65f;

        // Seed only, for a shot with nothing to frame yet; with a target, LateUpdate overwrites it.
        private const float FallbackFocusDistance = 2f;

        /// <summary>Where the camera is in its one-way life: Delayed, FlyingIn, Held, FlyingOut.</summary>
        private enum Phase
        {
            /// <summary>Spawned but not rendering — the player still looks through their own camera.</summary>
            Delayed,

            /// <summary>Rendering, on its way from the player's eye to the authored shot.</summary>
            FlyingIn,

            /// <summary>Landed, holding the authored shot. The one phase <see cref="Settled"/> is true in.</summary>
            Held,

            /// <summary>On its way back to the player's eye, to be dismissed when it lands.</summary>
            FlyingOut
        }

        /// <summary>
        /// The focus camera that currently holds the player's eye, or null.
        ///
        /// <para>
        /// Static because the thing being guarded is shared and singular: there is one player
        /// camera on this machine, and exactly one focus camera may have it switched off at a
        /// time. Two can otherwise overlap — a fly-out is still holding the eye when the next
        /// screen opens — and the second would record the first's handiwork as the state to put
        /// back. See <see cref="TakeOverFromPlayerCamera"/>.
        /// </para>
        /// </summary>
        private static FocusCamera holder;

        private Camera cam;
        private Camera playerCamera;
        private AudioListener playerListener;
        private bool playerCameraWasEnabled;
        private bool playerListenerWasEnabled;

        /// <summary>
        /// Whether the player's camera and ears are actually ours. Deliberately not folded into
        /// <see cref="Phase"/>: it is a fact about ownership, not about the flight. A camera with
        /// nobody to take over from — a test scene, a spectator — flies the identical flight and
        /// reaches <see cref="Phase.Held"/> with this still false.
        /// </summary>
        private bool tookOver;

        private VolumeProfile profile;
        private DepthOfField dof;

        private Phase phase = Phase.Delayed;

        /// <summary>The flight now running, so <see cref="FlyOut"/> can stop that one alone.</summary>
        private Coroutine flight;

        private FlightPose flyFrom;
        private float flyElapsed;

        private FlightPose outFrom;
        private float outElapsed;
        private float outSeconds;

        private float yawOffset;
        private float pitchOffset;
        private float yawVelocity;
        private float pitchVelocity;

        /// <summary>
        /// The spawned camera. <see cref="Dismiss"/> disables it at once, but <c>Destroy</c> is
        /// deferred to the end of the frame, so this keeps handing back the disabled camera for the
        /// rest of that frame and reads as null only afterwards.
        /// </summary>
        public Camera Camera => cam;

        /// <summary>
        /// True only while the shot is being HELD: the fly-in delay is over, the flight has landed,
        /// and the pose is the authored one. False throughout the delay — when this camera is not
        /// rendering yet and is still parked at the player's eye — throughout the flight in, and
        /// throughout the flight out.
        /// </summary>
        public bool Settled => phase == Phase.Held;

        // ── The shot, authored by the subclass ───────────────────────────────
        //
        // Every member below is read LIVE and never sampled at spawn: LateUpdate asks for the pose
        // ones each frame, and the fly-in coroutine re-reads its two timings on every frame it
        // waits. A subclass may therefore compute any of them from whatever has moved since.

        /// <summary>False when what the shot frames has gone; the pose then stops updating.</summary>
        protected abstract bool HasTarget { get; }

        /// <summary>Where the lens sits, recomputed every frame.</summary>
        protected abstract Vector3 LensPosition();

        /// <summary>Which way the shot faces, as a compass angle about world up.</summary>
        protected abstract float LensYaw();

        /// <summary>Degrees the lens looks down.</summary>
        protected abstract float PitchDown { get; }

        protected abstract float Fov { get; }

        /// <summary>Distance the depth of field focuses at, recomputed every frame.</summary>
        protected abstract float FocusDistance();

        protected abstract float FlyInSeconds { get; }

        /// <summary>Seconds the player keeps looking through their own camera before the handover.</summary>
        protected virtual float FlyInDelay => 0f;

        // ── Lifecycle ────────────────────────────────────────────────────────

        /// <summary>
        /// Start the shot, flying from <paramref name="fromCamera"/>'s eye. Called once by the
        /// subclass's spawn method AFTER its own shot fields are set — the fallback pose below
        /// reads them.
        /// </summary>
        protected void Begin(Camera fromCamera)
        {
            playerCamera = fromCamera;

            // The pose it flies FROM: the player's eyes. Captured now, before the handover, so the
            // shot starts where the player was actually looking. With no player camera to borrow
            // from — a test scene, a spectator — there is nothing to fly from and it simply starts
            // where it ends.
            flyFrom = playerCamera != null
                ? FlightPose.Of(playerCamera.transform, playerCamera.fieldOfView)
                : new FlightPose(LensPosition(), LensYaw(), PitchDown, Fov);

            cam = gameObject.AddComponent<Camera>();
            cam.fieldOfView = Fov;
            cam.nearClipPlane = playerCamera != null ? playerCamera.nearClipPlane : 0.05f;
            cam.farClipPlane = playerCamera != null ? playerCamera.farClipPlane : 1000f;
            cam.cullingMask = playerCamera != null ? playerCamera.cullingMask : ~0;

            // Held back until the delay is up: two enabled cameras with no depth between them is
            // undefined, and the player is meant to still be looking through their own until then.
            cam.enabled = false;

            transform.SetPositionAndRotation(flyFrom.Position, flyFrom.Rotation);

            BuildDepthOfField();

            flight = StartCoroutine(FlyIn());
        }

        /// <summary>Puts the player's camera and ears back and destroys this. Safe to call twice.</summary>
        public void Dismiss()
        {
            // Ours off first: Destroy is deferred to the end of the frame, so leaving it enabled
            // would put two cameras at the same depth on screen for the rest of this one.
            if (cam != null) cam.enabled = false;

            RestorePlayerCamera();
            DestroyDepthOfField();

            if (this != null && gameObject != null) Destroy(gameObject);
        }

        /// <summary>
        /// Fly back to the player's eye over <paramref name="seconds"/>, then <see cref="Dismiss"/>.
        ///
        /// <para>
        /// The target is the eye's LIVE pose, read every frame: the caller has usually already
        /// handed the controls back, so a player who walks off as the screen closes sees the camera
        /// catch up to them rather than return to where their head used to be. Falls through to an
        /// instant dismiss when there is no eye to fly to or the handover never happened.
        /// </para>
        /// </summary>
        public void FlyOut(float seconds)
        {
            if (playerCamera == null || !tookOver || seconds <= 0f)
            {
                Dismiss();
                return;
            }

            // The flight alone, never StopAllCoroutines: a subclass is this same MonoBehaviour,
            // and its own coroutines are not ours to kill.
            if (flight != null) StopCoroutine(flight);

            outFrom = FlightPose.Of(transform, cam != null ? cam.fieldOfView : Fov);
            outSeconds = seconds;
            outElapsed = 0f;
            phase = Phase.FlyingOut;

            flight = StartCoroutine(FlyOutRoutine());
        }

        private void OnDestroy()
        {
            // Belt and braces. A focus camera destroyed by a scene load rather than by Dismiss
            // would otherwise leave the player looking through a camera that no longer exists.
            RestorePlayerCamera();
            DestroyDepthOfField();
        }

        private void RestorePlayerCamera()
        {
            // Only what was taken. The saved flags default to false, so restoring them before the
            // handover would switch the player's camera OFF.
            if (tookOver)
            {
                if (playerCamera != null) playerCamera.enabled = playerCameraWasEnabled;
                if (playerListener != null) playerListener.enabled = playerListenerWasEnabled;
            }

            tookOver = false;
            playerCamera = null;
            playerListener = null;

            // Only if we are still the one holding it: a hand-off has already pointed `holder` at
            // the incoming camera, and clearing it here would lose that.
            if (holder == this) holder = null;
        }

        private IEnumerator FlyIn()
        {
            for (float wait = 0f; wait < FlyInDelay; wait += Time.unscaledDeltaTime)
                yield return null;

            // Only now does the view actually change hands. Doing it in Begin would black out the
            // delay, since this camera is not rendering yet.
            TakeOverFromPlayerCamera();

            phase = Phase.FlyingIn;
            flyElapsed = 0f;
            cam.enabled = true;

            while (flyElapsed < FlyInSeconds)
            {
                flyElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            // Held whether or not the handover happened: a camera with no player camera to take
            // over from has still arrived at its shot, and Settled has to say so.
            phase = Phase.Held;
        }

        private IEnumerator FlyOutRoutine()
        {
            while (outElapsed < outSeconds)
            {
                outElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Dismiss();
        }

        private void TakeOverFromPlayerCamera()
        {
            if (playerCamera == null) return;

            // Send any focus camera that still holds this player's eye home FIRST.
            //
            // A fly-out hands the eye back only when it LANDS, so for its whole duration the
            // player's camera is still switched off while nothing claims to be using it — and that
            // is a window the player can act in: press I again, or B for the pack. Capturing
            // `playerCamera.enabled` in that window reads `false`, which is the OUTGOING camera's
            // doing rather than the player's state, and this camera would then faithfully restore
            // `false` when it finished — switching the player's own view and ears off for good.
            // The only recovery in the game is mounting and dismounting a vehicle, which happens
            // to re-enable them.
            //
            // Dismissing the holder puts the camera and the listener back before the capture
            // below, so what is read is always the player's real state. It also means there is
            // never a frame with two enabled cameras at the same depth, or two AudioListeners.
            if (holder != null && holder != this) holder.Dismiss();

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

            tookOver = true;
            holder = this;
        }

        private void LateUpdate()
        {
            if (phase == Phase.FlyingOut)
            {
                if (playerCamera == null) { Dismiss(); return; }

                FlightPose eye = FlightPose.Of(playerCamera.transform, playerCamera.fieldOfView);
                Apply(FocusFlight.Blend(outFrom, eye, outSeconds > 0f ? outElapsed / outSeconds : 1f));

                // Focus travels with the lens, or the Bokeh blur stays pinned at the subject's
                // distance while the camera flies home and the player's own head arrives soft.
                SetFocusDistance(Vector3.Distance(transform.position, eye.Position));
                return;
            }

            if (!HasTarget) return;

            UpdateParallax();

            // Roll is not a term anywhere in here. Both angles are absolute — measured about world
            // up and about the horizontal — so whatever the cursor and the flight are doing, the
            // horizon stays level.
            var shot = new FlightPose(LensPosition(), LensYaw() + yawOffset, PitchDown + pitchOffset, Fov);

            Apply(phase == Phase.FlyingIn && FlyInSeconds > 0f
                ? FocusFlight.Blend(flyFrom, shot, flyElapsed / FlyInSeconds)
                : shot);

            SetFocusDistance(FocusDistance());
        }

        /// <summary>
        /// The null check is load-bearing rather than defensive: <see cref="Dismiss"/> destroys the
        /// override, <c>Destroy</c> is deferred, and <c>LateUpdate</c> can still run afterwards in
        /// the same frame.
        /// </summary>
        private void SetFocusDistance(float distance)
        {
            if (dof != null) dof.focusDistance.value = Mathf.Max(0.1f, distance);
        }

        private void Apply(in FlightPose pose)
        {
            transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            if (cam != null) cam.fieldOfView = pose.Fov;
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
        /// A runtime volume holding one <see cref="DepthOfField"/>, focused on the shot's subject.
        ///
        /// <para>
        /// It is put on the pack-item layer and this camera's <c>volumeLayerMask</c> is widened to
        /// include that layer, so the blur belongs to this camera alone. A global volume on the
        /// Default layer would also be picked up by the player's camera — and by every other
        /// camera in the scene — which would soften the world for anyone who happened to be
        /// looking at it while somebody else opened a pack or their body screen.
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
            profile.name = "FocusDof";

            dof = profile.Add<DepthOfField>(overrides: true);
            dof.mode.overrideState = true;
            dof.mode.value = DepthOfFieldMode.Bokeh;
            dof.focusDistance.overrideState = true;
            dof.focusDistance.value = HasTarget ? Mathf.Max(0.1f, FocusDistance()) : FallbackFocusDistance;
            dof.aperture.overrideState = true;
            dof.aperture.value = Aperture;
            dof.focalLength.overrideState = true;
            dof.focalLength.value = FocalLength;

            var volumeGo = new GameObject("FocusVolume") { layer = volumeLayer };
            volumeGo.transform.SetParent(transform, false);

            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = profile;

            UniversalAdditionalCameraData data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();

            data.renderPostProcessing = true;
            data.volumeLayerMask = data.volumeLayerMask | (1 << volumeLayer);
        }

        /// <summary>
        /// <c>VolumeProfile.Add</c> builds the override with <c>ScriptableObject.CreateInstance</c>,
        /// and a profile does not clean its components up when it goes — so the
        /// <see cref="DepthOfField"/> would outlive both and sit in memory until the next
        /// <c>UnloadUnusedAssets</c>, once per focus session.
        /// </summary>
        private void DestroyDepthOfField()
        {
            if (dof != null) Destroy(dof);
            dof = null;

            if (profile != null) Destroy(profile);
            profile = null;
        }
    }
}
