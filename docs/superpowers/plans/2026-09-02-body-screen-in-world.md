# Body Screen in the World — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the F screen from a six-tile panel into a camera step-out in the live world — the real character seen from the front, ghost silhouettes on empty gauntlet/back sites, the carried item previewed where it will sit, the three hotbar tiles along the bottom.

**Architecture:** A spawned `BodyFocusCamera` (sharing a new `FocusCamera` base with the pack's camera) frames the player's chest from the front; a `BodyFocusSession` component on the player prefab owns three `BodySite`s whose ghosts are stripped display copies seated through the same `EquipItemSocket` / `WornFit` path as real worn gear and tinted with the pack's `PackDragTint` shader; `BodyInventoryUI` stays the conductor of click-to-carry and draws chips/captions on `WorldOverlay`. Slots, rules, wire and save are untouched.

**Tech Stack:** Unity 6000.3 (URP, Netcode for GameObjects, TextMeshPro, Input System), NUnit EditMode tests in `Assets/Game/Editor/Tests`, Blender 5.1 headless for the two placeholder meshes.

**Spec:** [2026-09-02-body-screen-in-world-design.md](../specs/2026-09-02-body-screen-in-world-design.md)

---

## Before you start — how this repo is verified

**Commits.** A hook blocks `git commit` unless the user asked for a commit *in that turn*. At each commit step, ask the user ("Task N is done and verified — commit?") and run the commit only when they say yes. Never work around the hook.

**Never put a `$` in a Bash command** — the same hook fires on any `$` (`$f`, `"$X"`, `$(...)`). Use Python one-liners or files for loops and variables. Zsh also expands `--include=*.cs`; quote it.

**Type-check without the editor (~2 min):**
```bash
mkdir -p /private/tmp/claude-501/bodyscreen && cp /Users/ferdinandfremming/.claude/projects/-Users-ferdinandfremming-Documents-hackerspace-spillgruppen-SpaceGame/memory/headless_check.py /private/tmp/claude-501/bodyscreen/check.py
python3 /private/tmp/claude-501/bodyscreen/check.py rebuild runtime editor
```
Expected: both passes end with `0 error(s)`. A `CS0246` naming a type you can grep on disk means the source list is stale — the script rebuilds it, so re-run once before believing it.

**Run EditMode tests.** The editor must be open and idle (not in play mode). Delete stale results first, click the menu (AppleScript works even when the editor is unfocused — open the menu, then the item), then poll:
```bash
rm -f Temp/headless_tests.txt
osascript -e 'tell application "System Events" to tell process "Unity"' -e 'click menu bar item "Tools" of menu bar 1' -e 'delay 0.5' -e 'click menu item "Run EditMode Tests (headless)" of menu 1 of menu item "Tests" of menu 1 of menu bar item "Tools" of menu bar 1' -e 'end tell'
python3 -c "import time,os; [time.sleep(3) for _ in range(60) if not os.path.exists('Temp/headless_tests.txt') or 'DONE' not in open('Temp/headless_tests.txt').read()]; print(open('Temp/headless_tests.txt').read()[-1500:])"
```
Expected: a `PASSED=<n> FAILED=<m>` line and `DONE`. A run that returns in seconds with a tiny count was cut short by a domain reload — re-run. Standing failures that are **not** yours: the `Time.time == 0` mount/wingpack/passenger tests, the lasso rope test, and the older Backpack snap-drift tests (see `docs/AI/DEFECTS.md`).

Before any test run, make sure the editor has compiled your files: `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` must be newer than the `.cs` you wrote (`python3 -c "import os;print(os.path.getmtime('Library/ScriptAssemblies/Assembly-CSharp-Editor.dll') > os.path.getmtime('<your file>'))"`). If it is not, focus the editor or click **Assets ▸ Refresh** the same AppleScript way.

**Seeing the result.** Play verification is over the unity-mcp bridge (`Unity_Camera_Capture` / screenshots) or by the user playing. **Read the screenshots** — the pack's whole overlay once passed two static reviews while its shader was never scheduled.

---

## File structure

| File | Responsibility |
| --- | --- |
| `Assets/Game/Scripts/Presentation/Cameras/FocusFlight.cs` *(new)* | `FlightPose` + the pure roll-free blend the flights use |
| `Assets/Game/Scripts/Presentation/Cameras/FocusCamera.cs` *(new)* | Abstract spawned focus camera: handover, fly in/out, parallax, DOF, dismiss |
| `Assets/Game/Scripts/Items/Backpack/Focus/PackFocusCamera.cs` *(rewrite)* | `: FocusCamera` — only the pack's shot |
| `Assets/Game/Scripts/Items/Equipped/DisplayCopy.cs` *(new)* | Staged instantiate + `Strip` (moved from `BackpackItemVisual`) |
| `Assets/Game/Scripts/Items/Equipped/TintMaterials.cs` *(new)* | `PackDragTint` material builder (moved from `PackHandVisuals`) |
| `Assets/Game/Scripts/Items/Equipped/OutlineShell.cs` *(new)* | Outline shell tracer (moved from `PackHandVisuals`) |
| `Assets/Game/Scripts/Items/Equipped/BackSeat.cs` *(new)* | `WornFit` seating (moved from `BodyEquipmentController`) |
| `Assets/Game/Scripts/Items/Equipped/ForearmSeat.cs` *(new)* | `GauntletFit` seating on the forearm bone (moved from `BodyEquipmentController`) |
| `Assets/Game/Scripts/Items/Body/BodyEquipmentController.cs` *(edit)* | `BackBone`, `WornInstance`, wear-flex + sound |
| `Assets/Game/Scripts/Items/Body/Focus/BodySiteState.cs` *(new)* | `SiteState` enum + pure resolver |
| `Assets/Game/Scripts/Items/Body/Focus/LensStandoff.cs` *(new)* | Pure wall pull-in arithmetic |
| `Assets/Game/Scripts/Items/Body/Focus/BodyFocusCamera.cs` *(new)* | `: FocusCamera` — the chest-front shot |
| `Assets/Game/Scripts/Items/Body/Focus/BodySite.cs` *(new)* | One site: ghosts, states, hide/restore, canvas rect, animations; `BodySite.Palette` |
| `Assets/Game/Scripts/Items/Body/Focus/BodyFocusSession.cs` *(new)* | On the player prefab: tunables, camera, sites, hover/click events |
| `Assets/Game/Scripts/Presentation/UI/Pages/BodyInventoryUI.cs` *(rewrite)* | Conductor: carry over sites + tiles, chips, captions, chrome |
| `Assets/Game/Art/Models/_Source~/models/gear/ghost_gauntlet_export.py` *(new)* | Ships `Coll_GauntletBase_Plain` as the empty-arm placeholder — no new model |
| `Assets/Game/Art/Models/_Source~/models/gear/ghost_mount_frame.py` + `_export.py` + `_BUILD.md` *(new)* | Placeholder back mount frame |
| `Assets/Game/Editor/Items/GearGhostBuilder.cs` *(new)* | Builds `GhostGauntlet.prefab`, `GhostBack.prefab`; adds `BodyFocusSession` to `PlayerCharacter.prefab` |
| `Assets/Game/Editor/Tests/FocusFlightTests.cs`, `DisplayCopyTests.cs`, `BackSeatTests.cs`, `ForearmSeatTests.cs`, `BodySiteStateTests.cs`, `LensStandoffTests.cs` *(new)* | Tests |

Every `.cs` you add under `Assets/` gets a `.meta` from Unity on import — do not author them by hand. Nothing here needs a fixed GUID: the one prefab wiring (Task 10) is done by an editor script.

---

## Task 1: Extract `FocusCamera` from `PackFocusCamera`

Pure refactor plus one pure helper. The pack must behave identically afterwards.

**Files:**
- Create: `Assets/Game/Scripts/Presentation/Cameras/FocusFlight.cs`
- Create: `Assets/Game/Scripts/Presentation/Cameras/FocusCamera.cs`
- Rewrite: `Assets/Game/Scripts/Items/Backpack/Focus/PackFocusCamera.cs`
- Test: `Assets/Game/Editor/Tests/FocusFlightTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/Game/Editor/Tests/FocusFlightTests.cs
// The one flight every focus camera takes. Angles are blended as numbers about world axes, never
// as a rotation, because a slerp between an eyeline and a shot round the far side of a pack rolls
// through 19° at the halfway point — the camera cartwheels. This pins that the horizon stays level.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class FocusFlightTests
    {
        private static readonly FlightPose From = new(new Vector3(0f, 1.7f, 0f), 350f, 10f, 70f);
        private static readonly FlightPose To = new(new Vector3(2f, 1.5f, 3f), 10f, 38f, 40f);

        [Test]
        public void AtZeroIsTheFromPose()
        {
            FlightPose p = FocusFlight.Blend(From, To, 0f);
            Assert.AreEqual(From.Position, p.Position);
            Assert.AreEqual(From.Yaw, p.Yaw, 1e-4f);
            Assert.AreEqual(From.Pitch, p.Pitch, 1e-4f);
            Assert.AreEqual(From.Fov, p.Fov, 1e-4f);
        }

        [Test]
        public void AtOneIsTheTargetPose()
        {
            FlightPose p = FocusFlight.Blend(From, To, 1f);
            Assert.AreEqual(To.Position, p.Position);
            Assert.AreEqual(To.Yaw, p.Yaw, 1e-4f);
            Assert.AreEqual(To.Pitch, p.Pitch, 1e-4f);
            Assert.AreEqual(To.Fov, p.Fov, 1e-4f);
        }

        [Test]
        public void YawWrapsTheShortWayRound()
        {
            // 350° to 10° is a 20° turn through north, not a 340° turn the other way.
            FlightPose p = FocusFlight.Blend(From, To, 0.5f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, p.Yaw), 1e-3f);
        }

        [Test]
        public void RollIsZeroAllTheWay()
        {
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float roll = FocusFlight.Blend(From, To, t).Rotation.eulerAngles.z;
                Assert.AreEqual(0f, Mathf.DeltaAngle(0f, roll), 1e-3f, "roll at t=" + t);
            }
        }

        [Test]
        public void OfReadsATransformAsYawAndPitch()
        {
            var go = new GameObject("Eye");
            try
            {
                go.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(25f, 200f, 0f));
                FlightPose p = FlightPose.Of(go.transform, 60f);
                Assert.AreEqual(new Vector3(1f, 2f, 3f), p.Position);
                Assert.AreEqual(200f, p.Yaw, 1e-3f);
                Assert.AreEqual(25f, p.Pitch, 1e-3f);
                Assert.AreEqual(60f, p.Fov);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
```

- [ ] **Step 2: Run the type-check to see it fail**

Run: `python3 /private/tmp/claude-501/bodyscreen/check.py rebuild runtime editor`
Expected: the editor pass fails with `CS0246: The type or namespace name 'FlightPose' could not be found`.

- [ ] **Step 3: Write `FocusFlight.cs`**

```csharp
// Assets/Game/Scripts/Presentation/Cameras/FocusFlight.cs
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A camera pose the way a focus flight interpolates it: a position, two ABSOLUTE angles —
    /// yaw about world up, pitch about the horizontal — and a field of view. Never a rotation.
    /// </summary>
    public readonly struct FlightPose
    {
        public readonly Vector3 Position;
        public readonly float Yaw;
        public readonly float Pitch;
        public readonly float Fov;

        public FlightPose(Vector3 position, float yaw, float pitch, float fov)
        {
            Position = position;
            Yaw = yaw;
            Pitch = pitch;
            Fov = fov;
        }

        /// <summary>The pose a transform is in now, read as yaw and pitch about world axes.</summary>
        public static FlightPose Of(Transform t, float fov)
        {
            Vector3 euler = t.rotation.eulerAngles;
            return new FlightPose(t.position, euler.y, euler.x, fov);
        }

        /// <summary>The rotation this pose means. Roll is not a term in it, by construction.</summary>
        public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);
    }

    /// <summary>
    /// The flight every focus camera takes between two poses.
    ///
    /// <para>
    /// Interpolated as position + yaw + pitch, not as a pose: <c>Quaternion.Slerp</c> takes the
    /// geodesic between two rotations, and between a player's eyeline and a shot 180° round the
    /// other side of a pack that path rolls through 19° at the halfway point — the camera
    /// cartwheels on its way over. Blending the two angles keeps it level throughout, and
    /// <see cref="Mathf.LerpAngle"/> takes the short way round.
    /// </para>
    /// </summary>
    public static class FocusFlight
    {
        public static FlightPose Blend(in FlightPose from, in FlightPose to, float t)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            return new FlightPose(
                Vector3.Lerp(from.Position, to.Position, k),
                Mathf.LerpAngle(from.Yaw, to.Yaw, k),
                Mathf.LerpAngle(from.Pitch, to.Pitch, k),
                Mathf.Lerp(from.Fov, to.Fov, k));
        }
    }
}
```

- [ ] **Step 4: Write `FocusCamera.cs`**

This is `PackFocusCamera`'s mechanics with the pack's numbers removed and a fly-out added. Two deliberate differences from the original, both bug fixes: the player camera is only *restored* if it was actually taken over (the original restored `playerCameraWasEnabled`, which defaults to `false`, so a `Dismiss` inside the 0.15 s delay would have switched the player's camera off); and `Settled` also covers the fly-out.

```csharp
// Assets/Game/Scripts/Presentation/Cameras/FocusCamera.cs
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

        private Camera cam;
        private Camera playerCamera;
        private AudioListener playerListener;
        private bool playerCameraWasEnabled;
        private bool playerListenerWasEnabled;
        private bool tookOver;

        private Volume volume;
        private VolumeProfile profile;
        private DepthOfField dof;

        private FlightPose flyFrom;
        private float flyElapsed;
        private bool flying;

        private FlightPose outFrom;
        private float outElapsed;
        private float outSeconds;
        private bool flyingOut;

        private float yawOffset;
        private float pitchOffset;
        private float yawVelocity;
        private float pitchVelocity;

        /// <summary>The spawned camera. Null once <see cref="Dismiss"/> has run.</summary>
        public Camera Camera => cam;

        /// <summary>True once no flight is in progress and the pose is the authored one.</summary>
        public bool Settled => !flying && !flyingOut;

        // ── The shot, authored by the subclass ───────────────────────────────

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

            StartCoroutine(FlyIn());
        }

        /// <summary>Puts the player's camera and ears back and destroys this. Safe to call twice.</summary>
        public void Dismiss()
        {
            RestorePlayerCamera();

            if (profile != null) Destroy(profile);
            profile = null;

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

            StopAllCoroutines();
            flying = false;

            outFrom = FlightPose.Of(transform, cam != null ? cam.fieldOfView : Fov);
            outSeconds = seconds;
            outElapsed = 0f;
            flyingOut = true;

            StartCoroutine(FlyOutRoutine());
        }

        private void OnDestroy()
        {
            // Belt and braces. A focus camera destroyed by a scene load rather than by Dismiss
            // would otherwise leave the player looking through a camera that no longer exists.
            RestorePlayerCamera();
            if (profile != null) Destroy(profile);
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
        }

        private IEnumerator FlyIn()
        {
            for (float wait = 0f; wait < FlyInDelay; wait += Time.unscaledDeltaTime)
                yield return null;

            // Only now does the view actually change hands. Doing it in Begin would black out the
            // delay, since this camera is not rendering yet.
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
        }

        private void LateUpdate()
        {
            if (flyingOut)
            {
                if (playerCamera == null) { Dismiss(); return; }

                FlightPose eye = FlightPose.Of(playerCamera.transform, playerCamera.fieldOfView);
                Apply(FocusFlight.Blend(outFrom, eye, outSeconds > 0f ? outElapsed / outSeconds : 1f));
                return;
            }

            if (!HasTarget) return;

            UpdateParallax();

            // Roll is not a term anywhere in here. Both angles are absolute — measured about world
            // up and about the horizontal — so whatever the cursor and the flight are doing, the
            // horizon stays level.
            var shot = new FlightPose(LensPosition(), LensYaw() + yawOffset, PitchDown + pitchOffset, Fov);

            Apply(flying && FlyInSeconds > 0f
                ? FocusFlight.Blend(flyFrom, shot, flyElapsed / FlyInSeconds)
                : shot);

            if (dof != null) dof.focusDistance.value = Mathf.Max(0.1f, FocusDistance());
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
            dof.focusDistance.value = HasTarget ? Mathf.Max(0.1f, FocusDistance()) : 2f;
            dof.aperture.overrideState = true;
            dof.aperture.value = Aperture;
            dof.focalLength.overrideState = true;
            dof.focalLength.value = FocalLength;

            var volumeGo = new GameObject("FocusVolume") { layer = volumeLayer };
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
```

- [ ] **Step 5: Rewrite `PackFocusCamera.cs` as a subclass**

Replace the whole file. The class comment about the authored pose (the `2.46 m` / `38°` / `FOV 40` reasoning and the `PackScale.Factor` similarity-transform argument) is kept verbatim from the current file's `<summary>` — copy it in above the class; only the mechanics paragraphs (precedent, parallax, flight) go, because they now live on the base.

```csharp
// Assets/Game/Scripts/Items/Backpack/Focus/PackFocusCamera.cs
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// The view used while rummaging in a deployed pack.
    ///
    /// [KEEP: paste the existing "The pose is authored, not orbited ..." paragraph and the
    ///  "Both distances ride PackScale.Factor ..." paragraph from the current file here, unchanged.]
    /// </summary>
    public sealed class PackFocusCamera : FocusCamera
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
        // rather than as an obstruction.
        //
        // Both distances are offsets from the RIG's origin, so they take PackScale.Factor with the
        // rig. The angles do not, and must not: a similarity transform leaves every angle alone,
        // which is exactly why the enlarged pack frames identically from here.
        private static readonly float DistanceOut = PackScale.Apply(2.46f);
        private static readonly float HeightUp = PackScale.Apply(1.5f);
        private const float Pitch = 38f;
        private const float FieldOfView = 40f;

        // ── The arrival ──────────────────────────────────────────────────────
        //
        // The pack's own arc is 0.9 s (BackpackController.deploySeconds). Starting 0.15 s in and
        // taking 0.9 s means the camera settles a breath after the rig does. It deliberately does
        // NOT wait for the unfold to finish: this is an interaction performed hundreds of times a
        // session, and 1.4 s of nothing at the front of it is the difference between a pocket and
        // a cutscene.
        private const float Delay = 0.15f;
        private const float Seconds = 0.9f;

        private Transform rig;

        // Where the LENS looks: down the player→pack line, from beyond the pack back toward the
        // player. The reverse of what the caller hands over, reversed once on the way in.
        private Vector3 lensForward = Vector3.forward;

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

            focus.rig = rig;

            // Reversed here, once: the caller measures player→pack, the lens looks pack→player.
            var flat = new Vector3(viewDirection.x, 0f, viewDirection.z);
            focus.lensForward = flat.sqrMagnitude > 1e-6f ? -flat.normalized : Vector3.forward;

            focus.Begin(playerCamera);
            return focus;
        }

        protected override bool HasTarget => rig != null;

        /// <summary>
        /// Live rather than sampled once, because the rig is still travelling along its deploy arc
        /// when the camera spawns. Tracking it means the shot converges on the landing pose instead
        /// of framing the patch of sand the pack was over when the key was pressed.
        /// </summary>
        protected override Vector3 LensPosition() =>
            rig.position - lensForward * DistanceOut + Vector3.up * HeightUp;

        /// <summary>Frozen with <see cref="lensForward"/> at spawn, so unlike the position this does not move.</summary>
        protected override float LensYaw() => Quaternion.LookRotation(lensForward, Vector3.up).eulerAngles.y;

        protected override float PitchDown => Pitch;
        protected override float Fov => FieldOfView;
        protected override float FlyInDelay => Delay;
        protected override float FlyInSeconds => Seconds;
        protected override float FocusDistance() => Vector3.Distance(transform.position, rig.position);
    }
}
```

- [ ] **Step 6: Type-check and run the tests**

Run: `python3 /private/tmp/claude-501/bodyscreen/check.py rebuild runtime editor`
Expected: `0 error(s)` on both passes. `PackFocusSession` still compiles unchanged — it only calls `PackFocusCamera.Spawn`, `.Dismiss()`, `.Camera` and `.Settled`.

Run the EditMode suite (recipe above). Expected: `FocusFlightTests` — 5 pass; `PASSED=` up by 5, nothing new under `FAILED`.

- [ ] **Step 7: Verify the pack in play**

With the editor playing (host of one): press **B**. Expected: the camera flies out over the pack exactly as before (settles a breath after the rig lands, mat facing the lens, level horizon), cursor parallax nudges the shot, the world behind is softly blurred. Press **Esc**: the pack reshoulders and the player's own view is back with sound. Also open and close the pack **twice** in a row to prove the restore.

- [ ] **Step 8: Commit** (ask the user first)

```bash
git add Assets/Game/Scripts/Presentation/Cameras Assets/Game/Scripts/Items/Backpack/Focus/PackFocusCamera.cs Assets/Game/Editor/Tests/FocusFlightTests.cs
git commit -m "refactor: extract FocusCamera base from PackFocusCamera, add fly-out"
```

---

## Task 2: Extract `DisplayCopy` from `BackpackItemVisual`

**Files:**
- Create: `Assets/Game/Scripts/Items/Equipped/DisplayCopy.cs`
- Modify: `Assets/Game/Scripts/Items/Backpack/BackpackItemVisual.cs` (the stage block in `Build`; delete `Strip` and `DestroyAll`)
- Modify: `Assets/Game/Scripts/Items/Backpack/Holders/HolderBuilder.cs:83` (and the `<see cref>` at line 11)
- Test: `Assets/Game/Editor/Tests/DisplayCopyTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/Game/Editor/Tests/DisplayCopyTests.cs
// A display copy is scenery: it must not be able to tick, collide, own a network identity or run
// a script, and it must keep the prefab's hierarchy so a grip point can be found on it by path.
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class DisplayCopyTests
    {
        private class Ticker : MonoBehaviour { }

        private GameObject prefab;
        private GameObject parent;
        private GameObject copy;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("Item");
            prefab.AddComponent<Rigidbody>();
            prefab.AddComponent<BoxCollider>();
            prefab.AddComponent<Ticker>();
            prefab.AddComponent<NetworkObject>();

            var body = new GameObject("Body");
            body.transform.SetParent(prefab.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            body.AddComponent<MeshRenderer>();

            var grip = new GameObject("Grip");
            grip.transform.SetParent(body.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0.1f, 0f);

            parent = new GameObject("Parent");
            parent.transform.position = new Vector3(5f, 0f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (copy != null) Object.DestroyImmediate(copy);
            if (prefab != null) Object.DestroyImmediate(prefab);
            if (parent != null) Object.DestroyImmediate(parent);
        }

        [Test]
        public void StripsEverythingThatCouldRun()
        {
            copy = DisplayCopy.Make(prefab, parent.transform);

            Assert.IsNotNull(copy);
            Assert.AreEqual(0, copy.GetComponentsInChildren<Rigidbody>(true).Length);
            Assert.AreEqual(0, copy.GetComponentsInChildren<Collider>(true).Length);
            Assert.AreEqual(0, copy.GetComponentsInChildren<MonoBehaviour>(true).Length,
                "no script may survive — NetworkObject and the Ticker are both MonoBehaviours");
        }

        [Test]
        public void KeepsTheHierarchyAndTheRenderers()
        {
            copy = DisplayCopy.Make(prefab, parent.transform);

            Transform grip = copy.transform.Find("Body/Grip");
            Assert.IsNotNull(grip, "Strip removes components, never GameObjects");
            Assert.AreEqual(new Vector3(0f, 0.1f, 0f), grip.localPosition);
            Assert.AreEqual(1, copy.GetComponentsInChildren<MeshRenderer>(true).Length);
        }

        [Test]
        public void SitsUnderTheParentAtIdentity()
        {
            copy = DisplayCopy.Make(prefab, parent.transform);

            Assert.AreEqual(parent.transform, copy.transform.parent);
            Assert.AreEqual(Vector3.zero, copy.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, copy.transform.localRotation);
            Assert.AreEqual(Vector3.one, copy.transform.localScale);
        }

        [Test]
        public void LeavesNoStageBehind()
        {
            copy = DisplayCopy.Make(prefab, parent.transform);
            Assert.IsNull(GameObject.Find("DisplayCopyStage"));
        }
    }
}
```

- [ ] **Step 2: Type-check to see it fail**

Run: `python3 /private/tmp/claude-501/bodyscreen/check.py rebuild runtime editor`
Expected: editor pass fails with `CS0103: The name 'DisplayCopy' does not exist`.

- [ ] **Step 3: Write `DisplayCopy.cs`**

`Strip` and `DestroyAll` move here **verbatim** from `BackpackItemVisual.cs` (lines 170–235 of the current file), including their comments; only `Make` is new.

```csharp
// Assets/Game/Scripts/Items/Equipped/DisplayCopy.cs
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Turns an item prefab into an inert display copy: something that can be looked at and
    /// nothing else. The pack's mat, the ship's gear wall and the body screen's ghosts all show
    /// items this way.
    ///
    /// <para>
    /// A display copy is not an item: it holds no state and must never run gameplay code. So
    /// everything that could tick, collide, animate, make noise or own a network identity is taken
    /// off it before it gets a chance to run — and it has to be taken off BEFORE the copy is ever
    /// active, because <c>Instantiate</c> runs <c>Awake</c> synchronously. A copy born under a
    /// deactivated stage is never <c>activeInHierarchy</c>, so no <c>Awake</c> runs at all and
    /// <c>DestroyImmediate</c> takes the components off clean.
    /// </para>
    /// </summary>
    public static class DisplayCopy
    {
        /// <summary>
        /// A stripped copy of <paramref name="prefab"/> under <paramref name="parent"/>, at the
        /// identity local pose and unit scale. The caller seats and scales it.
        /// </summary>
        public static GameObject Make(GameObject prefab, Transform parent)
        {
            if (prefab == null) return null;

            var stage = new GameObject("DisplayCopyStage");
            stage.SetActive(false);

            GameObject copy = Object.Instantiate(prefab, stage.transform);
            Strip(copy);

            Transform t = copy.transform;
            t.SetParent(parent, false);

            // Normalise: the prefab's own root pose is about to be replaced by whoever seats the
            // copy, and a zero on one scale axis would make the inverse transform inside
            // ItemBounds.Measure non-finite.
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            Object.DestroyImmediate(stage);
            return copy;
        }

        /// <summary>
        /// Take everything that could tick, collide, animate, make noise or own a network identity
        /// off a copy, leaving pure scenery.
        ///
        /// <para>
        /// Public because <c>HolderBuilder</c> needs exactly this and a second stripper is the
        /// wrong answer: this one is hard-won, and the ways it can be got wrong are all silent.
        /// Order matters. MonoBehaviours go first because a <c>[RequireComponent]</c> on a script
        /// blocks removal of the Rigidbody or Collider it names. ParticleSystemRenderer goes with
        /// its ParticleSystem for the same reason — the renderer requires the system, and a
        /// particle renderer with nothing feeding it draws nothing anyway.
        /// </para>
        /// <para>
        /// Only ever call it on a copy under a <b>deactivated</b> parent. Instantiate runs Awake
        /// synchronously, so a copy born active has already registered itself before the first
        /// component comes off.
        /// </para>
        /// </summary>
        public static void Strip(GameObject copy)
        {
            if (copy == null) return;

            // NetworkBehaviours before the plain pass, because NetworkObject is itself a
            // MonoBehaviour and every NetworkBehaviour on the item requires it. The retry loop
            // below does get there eventually, but only after Unity has logged a refusal for each
            // one — ten warnings per pack refresh, which buries anything real. A stowed copy is
            // scenery; it has no business owning a network identity either way.
            DestroyAll<Unity.Netcode.NetworkBehaviour>(copy);

            DestroyAll<MonoBehaviour>(copy);
            DestroyAll<ParticleSystemRenderer>(copy);
            DestroyAll<ParticleSystem>(copy);

            // Line and trail renderers usually run in WORLD space, which means they ignore their
            // own transform: the copy gets scaled and seated and the rope stays exactly where the
            // original prefab drew it. On the grappling hook and the lasso that measured as a
            // 1 x 1 x 2 m item stuck at the pack's origin. They are also meaningless on a stowed
            // copy — a coil of rope in a pack is not mid-throw.
            DestroyAll<LineRenderer>(copy);
            DestroyAll<TrailRenderer>(copy);

            DestroyAll<Rigidbody>(copy);
            DestroyAll<Collider>(copy);
            DestroyAll<Animator>(copy);
            DestroyAll<AudioSource>(copy);
        }

        // Unity refuses to remove a component while another one on the same object declares it as
        // a requirement, and only logs rather than throwing — so a single pass silently leaves
        // whichever half of a [RequireComponent] pair it happened to reach first. Repeating until
        // the count stops falling clears the dependents and then what they were holding.
        private static void DestroyAll<T>(GameObject root) where T : Component
        {
            int previous = int.MaxValue;

            for (int pass = 0; pass < 8; pass++)
            {
                T[] found = root.GetComponentsInChildren<T>(true);

                int alive = 0;
                foreach (T component in found)
                    if (component != null) alive++;   // missing scripts come back as null entries

                if (alive == 0 || alive >= previous) return;
                previous = alive;

                foreach (T component in found)
                    if (component != null) Object.DestroyImmediate(component);
            }
        }
    }
}
```

(`Strip` and `DestroyAll` are the current `BackpackItemVisual` methods verbatim; diff them against the originals before deleting those.)

- [ ] **Step 4: Point `BackpackItemVisual.Build` and `HolderBuilder` at it**

In `BackpackItemVisual.Build`, replace this block:

```csharp
            var stage = new GameObject("BackpackItemStage");
            stage.SetActive(false);

            GameObject copy = Object.Instantiate(itemPrefab, stage.transform);
            Strip(copy);

            Transform t = copy.transform;

            // Normalise before measuring: ...
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
```

with:

```csharp
            GameObject copy = DisplayCopy.Make(itemPrefab, surface.transform);
            Transform t = copy.transform;
```

Then delete the later line `t.SetParent(anchor, false);` (the copy is already under the surface) and the final `Object.DestroyImmediate(stage);`. Keep `Transform anchor = surface.transform;` — `surfaceScale` and `SetLayer` still read it. Delete `Strip` and `DestroyAll` from `BackpackItemVisual` entirely (the "Instantiate runs Awake synchronously" comment goes with them — it now lives on `DisplayCopy`).

In `HolderBuilder.cs`, change `BackpackItemVisual.Strip(holder);` to `DisplayCopy.Strip(holder);` and the `<see cref="BackpackItemVisual.Strip"/>` at line 11 to `<see cref="DisplayCopy.Strip"/>`.

Confirm nothing else references the old name:
```bash
grep -rn "BackpackItemVisual.Strip" Assets/Game
```
Expected: no output.

- [ ] **Step 5: Type-check, run tests, verify the pack's mat**

Run the type-check (expected `0 error(s)`), then the EditMode suite (expected `DisplayCopyTests` 4 pass, the Backpack tests unchanged from their standing baseline).

In play: open the pack with **B**. Expected: every stowed item is drawn on the mat at the size and place it was before; picking one up and putting it down still works. (`Build` measures the copy after `Make` reparented it — `ItemBounds.Measure` walks `activeSelf` inside the item only, so the result is unchanged.)

- [ ] **Step 6: Commit** (ask first)

```bash
git add Assets/Game/Scripts/Items/Equipped/DisplayCopy.cs Assets/Game/Scripts/Items/Backpack/BackpackItemVisual.cs Assets/Game/Scripts/Items/Backpack/Holders/HolderBuilder.cs Assets/Game/Editor/Tests/DisplayCopyTests.cs
git commit -m "refactor: extract DisplayCopy (staged instantiate + Strip) from BackpackItemVisual"
```

---

## Task 3: Extract `TintMaterials` and `OutlineShell` from `PackHandVisuals`

Pure refactor. The hover rim and the refusal flash on the pack must look identical afterwards.

**Files:**
- Create: `Assets/Game/Scripts/Items/Equipped/TintMaterials.cs`
- Create: `Assets/Game/Scripts/Items/Equipped/OutlineShell.cs`
- Modify: `Assets/Game/Scripts/Items/Backpack/Focus/PackHandVisuals.cs`

- [ ] **Step 1: Write `TintMaterials.cs`**

```csharp
// Assets/Game/Scripts/Items/Equipped/TintMaterials.cs
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Items
{
    /// <summary>
    /// Materials on <c>SpaceGame/PackDragTint</c>: the one shader that draws a flat tinted body
    /// and/or an inflated outline round a mesh. The pack's hover rim and refusal flash, and the body
    /// screen's ghosts and previews, are all built here so they are one visual language.
    ///
    /// <para>
    /// The shader's two passes carry explicit <c>LightMode</c> tags — URP silently skips a
    /// multi-pass shader whose passes have none, which is why the pack's whole overlay once
    /// rendered nothing. Do not add a pass.
    /// </para>
    /// </summary>
    public static class TintMaterials
    {
        public const string ShaderName = "SpaceGame/PackDragTint";

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int BodyOnId = Shader.PropertyToID("_BodyOn");
        private static readonly int OutlineOnId = Shader.PropertyToID("_OutlineOn");
        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        /// <summary>
        /// Outline only, depth-tested normally, drawn on a shell that traces the real item so the
        /// ITEM lights up — no floating UI box. <paramref name="width"/> is a placeholder: every
        /// shell build sets a real one from the item's own size.
        /// </summary>
        public static Material Rim(string name, Color colour, float width)
        {
            Material material = New(name);
            material.SetFloat(BodyOnId, 0f);
            material.SetFloat(OutlineOnId, 1f);
            material.SetColor(OutlineColorId, colour);
            material.SetFloat(OutlineWidthId, width);
            Blend(material, queue: 2001);
            return material;
        }

        /// <summary>
        /// A see-through body with an outline: what a ghost is made of. Alpha-blended, no depth
        /// write, in the transparent queue so the world behind it still shows.
        /// </summary>
        public static Material Translucent(string name, Color body, Color outline, float width)
        {
            Material material = New(name);
            material.SetFloat(BodyOnId, 1f);
            material.SetFloat(OutlineOnId, 1f);
            material.SetColor(ColorId, body);
            material.SetColor(OutlineColorId, outline);
            material.SetFloat(OutlineWidthId, width);
            Blend(material, queue: 3000);
            return material;
        }

        public static void SetBody(Material material, Color body) => material.SetColor(ColorId, body);

        public static void SetOutline(Material material, Color outline) => material.SetColor(OutlineColorId, outline);

        public static void SetOutlineWidth(Material material, float width) => material.SetFloat(OutlineWidthId, width);

        private static Material New(string name)
        {
            Shader shader = Shader.Find(ShaderName);

            // Same fallback shape HelmetDangerVignette uses, so a missing project shader keeps
            // the session alive rather than null-reffing it. It is a keep-running fallback, not a
            // visual one: URP/Unlit knows nothing of the outline pass, so a rim renders as plain
            // colour instead of a rim.
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            return new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
        }

        private static void Blend(Material material, int queue)
        {
            material.SetFloat(ZTestId, (float)CompareFunction.LessEqual);
            material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat(ZWriteId, 0f);
            material.renderQueue = queue;
        }
    }
}
```

- [ ] **Step 2: Write `OutlineShell.cs`**

`Build`, `Clear`, `MeshOf` and the width arithmetic move here from `PackHandVisuals` with their comments (the "half a technique" paragraph about appending a material to the item's own renderers, and the "one constant cannot serve a 0.16 m leash and a 1.35 m staff" paragraph). The only edits: the width is written through `TintMaterials.SetOutlineWidth`, and the width constants become public so callers can seed a rim material.

```csharp
// Assets/Game/Scripts/Items/Equipped/OutlineShell.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Items
{
    /// <summary>
    /// Traces a visual with throwaway renderers carrying one outline material, so the whole
    /// silhouette lights up. [MOVE the existing BuildShell doc comment here.]
    /// </summary>
    public static class OutlineShell
    {
        /// <summary>Marks the shell objects, so a shell is never built round a shell.</summary>
        public const string ShellName = "PackOutlineShell";

        // [MOVE the OutlineFraction / Min / Max comment here.]
        private const float OutlineFraction = 0.020f;
        public static readonly float MinOutlineWidth = PackScale.Apply(0.0015f);
        public static readonly float MaxOutlineWidth = PackScale.Apply(0.010f);

        public static void Build(GameObject visual, Material outline, float weight, List<GameObject> parts)
        {
            Clear(parts);
            if (visual == null) return;

            TintMaterials.SetOutlineWidth(outline, WidthFor(visual, weight));

            foreach (Renderer source in visual.GetComponentsInChildren<Renderer>(true))
            {
                // Never shell our own shells: Unity's Destroy is deferred, so the parts cleared a
                // moment ago are still hanging on these renderers for the rest of the frame.
                if (source == null || source.gameObject.name == ShellName) continue;

                Mesh mesh = MeshOf(source);
                if (mesh == null || mesh.subMeshCount <= 0) continue;

                var part = new GameObject(ShellName) { hideFlags = HideFlags.HideAndDontSave };
                part.transform.SetParent(source.transform, false);
                part.layer = source.gameObject.layer;

                var materials = new Material[mesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = outline;

                Renderer shell;

                if (source is SkinnedMeshRenderer skinned)
                {
                    // A skinned mesh's vertices mean nothing without its bones, so the shell has
                    // to be skinned too and share them.
                    var copy = part.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = mesh;
                    copy.bones = skinned.bones;
                    copy.rootBone = skinned.rootBone;
                    copy.localBounds = skinned.localBounds;
                    shell = copy;
                }
                else
                {
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    shell = part.AddComponent<MeshRenderer>();
                }

                shell.sharedMaterials = materials;
                shell.shadowCastingMode = ShadowCastingMode.Off;
                shell.receiveShadows = false;

                parts.Add(part);
            }
        }

        public static void Clear(List<GameObject> parts)
        {
            foreach (GameObject part in parts)
                if (part != null) Object.Destroy(part);

            parts.Clear();
        }

        /// <summary>How thick a line to draw round this particular visual, in world metres. [MOVE comment.]</summary>
        public static float WidthFor(GameObject visual, float weight)
        {
            float span = 0f;
            bool any = false;
            Bounds bounds = default;

            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.gameObject.name == ShellName) continue;

                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (any)
            {
                Vector3 size = bounds.size;
                span = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            }

            return Mathf.Clamp(span * OutlineFraction * weight, MinOutlineWidth, MaxOutlineWidth);
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
```

- [ ] **Step 3: Rewire `PackHandVisuals`**

In `PackHandVisuals.cs`:
1. Delete the constants `ShaderName`, `ShellName`, `OutlineFraction`, `MinOutlineWidth`, `MaxOutlineWidth` and all nine `Shader.PropertyToID` fields.
2. Replace the constructor body's material lines with:
   ```csharp
            deniedMaterial = TintMaterials.Rim("PackDeniedRim", DeniedRim, OutlineShell.MinOutlineWidth);
            hoverMaterial = TintMaterials.Rim("PackHover", HoverRim, OutlineShell.MinOutlineWidth);
   ```
   and delete the `Shader shader = Shader.Find(...)` / fallback lines and the trailing `ConfigureRim(...)` calls (keep the explanatory comments about "no carry material at all").
3. Delete the private methods `Build`, `ConfigureRim`, `BuildShell`, `ClearShell`, `MeshOf`, `OutlineWidthFor`.
4. Replace every call: `BuildShell(` → `OutlineShell.Build(`, `ClearShell(` → `OutlineShell.Clear(` (five call sites: `SetHovered`, `SetCarryDenied` ×2, `EndCarry`, `Dispose` ×2).
5. Remove `using UnityEngine.Rendering;` only if nothing else in the file uses it.

Check:
```bash
grep -n "BuildShell\|ClearShell\|ConfigureRim\|OutlineWidthFor\|PropertyToID" Assets/Game/Scripts/Items/Backpack/Focus/PackHandVisuals.cs
```
Expected: no output.

- [ ] **Step 4: Type-check and verify the pack's rims in play**

Run the type-check (expected `0 error(s)`). In play, open the pack: hovering a placed item rims it in the warm hover colour over its whole silhouette; lifting an item and holding it over a spot where it cannot go shows the red rim flash. Both exactly as before.

- [ ] **Step 5: Commit** (ask first)

```bash
git add Assets/Game/Scripts/Items/Equipped/TintMaterials.cs Assets/Game/Scripts/Items/Equipped/OutlineShell.cs Assets/Game/Scripts/Items/Backpack/Focus/PackHandVisuals.cs
git commit -m "refactor: extract TintMaterials and OutlineShell from PackHandVisuals"
```

---

## Task 4: Seating seams — `BackSeat`, `ForearmSeat`, controller read-only seams

> **Revised 2026-09-03.** When this plan was written, gauntlets were seated in the hand's grip
> frame by `EquipItemSocket`, so the plan added `SeatCopy`/`Mirror` there to seat a stripped ghost
> the same way. A concurrent session has since rebuilt all six gauntlets on a shared
> `components/props/gauntlet_base.blend` and replaced that seating with `GauntletFit` +
> `BodyEquipmentController.WearOnForearm`, which parents the model to the **LowerArm bone** and
> aligns it to the elbow→wrist line. So no ghost is ever seated in the hand frame:
> `EquipItemSocket.SeatCopy`/`Mirror` are **not built**, and the forearm seating is extracted
> instead, exactly as the back seating is.

**Files:**
- Create: `Assets/Game/Scripts/Items/Equipped/BackSeat.cs`
- Create: `Assets/Game/Scripts/Items/Equipped/ForearmSeat.cs`
- Modify: `Assets/Game/Scripts/Items/Body/BodyEquipmentController.cs` (`WearOnBack`, `WearOnForearm` call the seats; add the read-only seams)
- Test: `Assets/Game/Editor/Tests/BackSeatTests.cs`, `Assets/Game/Editor/Tests/ForearmSeatTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Editor/Tests/BackSeatTests.cs
// The one copy of the WornFit seating arithmetic, shared by the real worn item and the body
// screen's ghost of it — so both land in the same place at the same size.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class BackSeatTests
    {
        private GameObject bone;
        private GameObject instance;

        [SetUp]
        public void SetUp()
        {
            bone = new GameObject("Spine");
            bone.transform.position = new Vector3(1f, 1.2f, 0f);

            instance = new GameObject("Pack");
            var body = new GameObject("Body");
            body.transform.SetParent(instance.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            body.AddComponent<MeshRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(bone);
        }

        private WornFit Fit(Vector3 position, Vector3 euler, float size)
        {
            var fit = instance.AddComponent<WornFit>();
            var so = new SerializedObject(fit);
            so.FindProperty("localPosition").vector3Value = position;
            so.FindProperty("localEuler").vector3Value = euler;
            so.FindProperty("size").floatValue = size;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fit;
        }

        [Test]
        public void SeatsAtTheFitsPoseAndSize()
        {
            WornFit fit = Fit(new Vector3(0f, 0.05f, -0.22f), new Vector3(0f, 90f, 0f), 0.5f);

            BackSeat.Apply(instance, bone.transform, fit);

            Assert.AreEqual(bone.transform, instance.transform.parent);
            Assert.AreEqual(0.5f, instance.transform.localScale.x, 1e-4f, "a 1 m cube drawn at 0.5 m");
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0f, 0.05f, -0.22f), instance.transform.localPosition), 1e-5f);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), instance.transform.localRotation), 1e-3f);
        }

        [Test]
        public void ZeroSizeKeepsTheAuthoredScale()
        {
            instance.transform.localScale = new Vector3(2f, 2f, 2f);
            WornFit fit = Fit(Vector3.zero, Vector3.zero, 0f);

            BackSeat.Apply(instance, bone.transform, fit);

            Assert.AreEqual(2f, instance.transform.localScale.x, 1e-5f);
        }

        [Test]
        public void NoFitIsTheBoneItself()
        {
            BackSeat.Apply(instance, bone.transform, null);

            Assert.AreEqual(bone.transform, instance.transform.parent);
            Assert.AreEqual(Vector3.zero, instance.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, instance.transform.localRotation);
        }
    }
}
```

```csharp
// Assets/Game/Editor/Tests/ForearmSeatTests.cs
// The one copy of the gauntlet seating arithmetic, shared by the real worn gauntlet and the body
// screen's ghost of it. The model is aligned to the elbow→wrist line, its dorsal face turned to
// the back of the arm, and the LEFT arm gets a negative X scale rather than a mirrored model —
// the base's hinges are on one flank, and a plain rotation would put them on the wrong one.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class ForearmSeatTests
    {
        private GameObject forearm;
        private GameObject hand;
        private GameObject instance;

        // A right forearm laid along world +X with the hand 0.4 m out, thumb side up.
        private static readonly Vector3 Elbow = new(0f, 1.2f, 0f);
        private static readonly Vector3 Wrist = new(0.4f, 1.2f, 0f);
        private static readonly Quaternion Grip = Quaternion.identity;   // grip up = world up = thumb side

        [SetUp]
        public void SetUp()
        {
            forearm = new GameObject("LowerArm");
            forearm.transform.position = Elbow;

            hand = new GameObject("Hand");
            hand.transform.position = Wrist;

            instance = new GameObject("Gauntlet");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(hand);
            Object.DestroyImmediate(forearm);
        }

        private GauntletFit Fit(float cuff, float length, float wristGap, float roll)
        {
            var fit = instance.AddComponent<GauntletFit>();
            var so = new SerializedObject(fit);
            so.FindProperty("cuffScale").floatValue = cuff;
            so.FindProperty("lengthScale").floatValue = length;
            so.FindProperty("wristGap").floatValue = wristGap;
            so.FindProperty("rollDegrees").floatValue = roll;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fit;
        }

        [Test]
        public void SitsAWristGapBackFromTheHandAlongTheArm()
        {
            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1f, 1f, 0.02f, 0f));

            // toHand is world +X, so the origin backs off the wrist toward the elbow by the gap.
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0.38f, 1.2f, 0f), instance.transform.position), 1e-4f);
            Assert.AreEqual(forearm.transform, instance.transform.parent);
        }

        [Test]
        public void PointsItsOwnForwardDownTheArm()
        {
            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1f, 1f, 0.02f, 0f));

            Assert.AreEqual(0f, Vector3.Angle(Vector3.right, instance.transform.forward), 1e-2f,
                "the model's +Z is the elbow→wrist line");
        }

        [Test]
        public void TheLeftArmIsMirroredOnX()
        {
            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: true, Fit(1f, 1f, 0.02f, 0f));
            Assert.Less(instance.transform.localScale.x, 0f, "the left arm mirrors rather than rotating");

            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1f, 1f, 0.02f, 0f));
            Assert.Greater(instance.transform.localScale.x, 0f);
        }

        [Test]
        public void WidthAndLengthScaleSeparately()
        {
            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1.5f, 2f, 0.02f, 0f));

            Vector3 scale = instance.transform.localScale;
            Assert.AreEqual(1.5f, scale.x, 1e-4f, "across the arm");
            Assert.AreEqual(1.5f, scale.y, 1e-4f, "across the arm");
            Assert.AreEqual(2f, scale.z, 1e-4f, "along it");
        }

        [Test]
        public void TheDorsalFaceIsTurnedOppositeWaysOnTheTwoArms()
        {
            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1f, 1f, 0.02f, 0f));
            Vector3 rightUp = instance.transform.up;

            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: true, Fit(1f, 1f, 0.02f, 0f));

            Assert.AreEqual(0f, Vector3.Distance(-rightUp, instance.transform.up), 1e-3f,
                "with the same thumb side, the two arms' backs face opposite ways");
        }

        [Test]
        public void ADegenerateArmStillGetsAUsablePose()
        {
            // Hand exactly on the elbow: toHand is undefined and the cross product collapses.
            hand.transform.position = Elbow;

            ForearmSeat.Apply(instance, forearm.transform, hand.transform, Grip, left: false, Fit(1f, 1f, 0.02f, 0f));

            Assert.IsFalse(float.IsNaN(instance.transform.rotation.x), "no NaN pose from a zero-length arm");
        }
    }
}
```

- [ ] **Step 2: Type-check to see them fail**

Run: `python3 /private/tmp/claude-501/bodyscreen/check.py rebuild runtime editor`
Expected: `CS0103: The name 'BackSeat' does not exist` and `CS0103: The name 'ForearmSeat' does not exist`.

- [ ] **Step 3: Write `BackSeat.cs`**

The arithmetic is `BodyEquipmentController.WearOnBack`'s, moved unchanged.

```csharp
// Assets/Game/Scripts/Items/Equipped/BackSeat.cs
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Seat something on the spine. There is no anatomy to derive a frame from on a back, so the
    /// pose is the prefab's <see cref="WornFit"/>, or the bone itself without one. The real worn
    /// item and the body screen's ghost of it both come through here, which is what makes a ghost
    /// sit exactly where the item will.
    /// </summary>
    public static class BackSeat
    {
        public static void Apply(GameObject instance, Transform bone, WornFit fit)
        {
            Transform t = instance.transform;
            t.SetParent(bone, false);

            if (fit != null && fit.Size > 0f)
            {
                Bounds bounds = ItemBounds.Measure(instance, null);
                float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                float boneScale = Mathf.Max(0.0001f, bone.lossyScale.x);
                if (longest > 0f) t.localScale = Vector3.one * (fit.Size / (longest * boneScale));
            }

            t.localPosition = fit != null ? fit.LocalPosition : Vector3.zero;
            t.localRotation = fit != null ? fit.LocalRotation : Quaternion.identity;
        }
    }
}
```

- [ ] **Step 4: Write `ForearmSeat.cs`**

The arithmetic is `BodyEquipmentController.WearOnForearm`'s, moved unchanged — read that method first and carry its comments across. The only change is that the instance is passed in already created (the controller instantiates it; the body screen makes a `DisplayCopy` of it), so this parents and poses rather than instantiating.

```csharp
// Assets/Game/Scripts/Items/Equipped/ForearmSeat.cs
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Strap a gauntlet to a forearm: aligned to the elbow→wrist line, its dorsal face turned to
    /// the back of the arm, at the scale its <see cref="GauntletFit"/> asks for. The real worn
    /// gauntlet and the body screen's ghost of it both come through here, so a ghost sits exactly
    /// where the device will.
    ///
    /// <para>
    /// [CARRY OVER from WearOnForearm: why the hand's grip frame is the wrong seat for a gauntlet,
    /// and why the LEFT arm gets a negative X scale rather than a mirrored model.]
    /// </para>
    /// </summary>
    public static class ForearmSeat
    {
        /// <param name="instance">Already created; parented and posed here.</param>
        /// <param name="forearm">The LowerArm bone. The gauntlet follows every pose from here on.</param>
        /// <param name="hand">The hand bone, for the wrist end of the arm.</param>
        /// <param name="gripRotation">The hand's grip frame rotation — used only for its thumb side.</param>
        /// <param name="left">The left arm mirrors on X.</param>
        public static void Apply(GameObject instance, Transform forearm, Transform hand,
                                 Quaternion gripRotation, bool left, GauntletFit fit)
        {
            if (instance == null || forearm == null || hand == null || fit == null) return;

            Vector3 toHand = (hand.position - forearm.position).normalized;
            Vector3 thumbSide = gripRotation * Vector3.up;
            Vector3 dorsal = left ? Vector3.Cross(toHand, thumbSide) : Vector3.Cross(thumbSide, toHand);
            if (dorsal.sqrMagnitude < 1e-4f) dorsal = Vector3.up;

            Transform t = instance.transform;
            t.SetParent(forearm, false);

            t.rotation = Quaternion.LookRotation(toHand, dorsal) * Quaternion.AngleAxis(fit.RollDegrees, Vector3.forward);
            t.position = hand.position - toHand * fit.WristGap;

            // Across the arm on X and Y, along it on Z (the model's arm axis). Not uniform: the
            // cuff is twice as long as wide, the forearm is not — see GauntletFit.
            float boneScale = Mathf.Max(0.0001f, forearm.lossyScale.x);
            float across = fit.CuffScale / boneScale;
            float along = fit.LengthScale / boneScale;
            t.localScale = new Vector3(left ? -across : across, across, along);
        }
    }
}
```

**Watch the degenerate case:** `(hand.position - forearm.position).normalized` is the zero vector when the two coincide, and `Quaternion.LookRotation(Vector3.zero, ...)` logs an error and yields an identity-ish rotation. `ADegenerateArmStillGetsAUsablePose` pins that it must not produce NaN — add a guard if the test shows one is needed, and say so in your report rather than changing the test.

- [ ] **Step 5: Point the controller at the two seats and add the seams**

In `BodyEquipmentController`, `WearOnBack` becomes:

```csharp
        private GameObject WearOnBack(Worn entry, GameObject prefab)
        {
            if (entry.Bone == null) return null;

            GameObject instance = Instantiate(prefab, entry.Bone);
            EquipItemSocket.Sanitize(instance);
            BackSeat.Apply(instance, entry.Bone, instance.GetComponent<WornFit>());

            return instance;
        }
```

and `WearOnForearm` keeps its `left` derivation and its doc comment but delegates the pose:

```csharp
        private GameObject WearOnForearm(Worn entry, GameObject prefab, GauntletFit fit)
        {
            if (entry.Bone == null || entry.Socket == null) return null;

            GameObject instance = Instantiate(prefab, entry.Bone);
            EquipItemSocket.Sanitize(instance);

            ForearmSeat.Apply(instance, entry.Bone, entry.Socket.Socket, entry.Socket.GripRotation,
                              entry.Slot == BodySlot.LeftGauntlet, fit);

            return instance;
        }
```

Move the paragraphs of `WearOnForearm`'s doc comment that explain the *seating* onto `ForearmSeat`; leave on the controller only what is about wearing (instantiate, sanitize, who owns the instance).

Add, next to `WornIn`:

```csharp
        /// <summary>The bone a back item hangs from, once <c>Start</c> has resolved it. The body screen seats its ghosts on it.</summary>
        public Transform BackBone => worn[(int)BodySlot.Back].Bone;

        /// <summary>The forearm bone a gauntlet is strapped to, or null for the back slot.</summary>
        public Transform ForearmBone(BodySlot slot) =>
            slot == BodySlot.Back ? null : worn[(int)slot].Bone;

        /// <summary>The hand socket a gauntlet's seating reads its thumb side from, or null for the back slot.</summary>
        public EquipItemSocket HandSocket(BodySlot slot) =>
            slot == BodySlot.Back ? null : worn[(int)slot].Socket;

        /// <summary>The instance worn in a slot, or null. The body screen hides and outlines it; it never moves it.</summary>
        public GameObject WornInstance(BodySlot slot) => worn[(int)slot].Instance;
```

- [ ] **Step 6: Type-check, run tests, verify in play**

Type-check: `0 errors`. EditMode: `BackSeatTests` 3 pass, `ForearmSeatTests` 6 pass; `WornPoseTests`, `GearMovesTests` unchanged.

In play: the wing pack sits on the back exactly where it did (it is `startingBody[0]`); wear each of the six gauntlets on each arm through the F screen and confirm every one sits exactly where it did before this task — same place, same size, hinges on the same flank, and the left arm still mirrored. This is a pure refactor: any visible difference is a bug in the extraction.

- [ ] **Step 7: Commit** (ask first)

```bash
git add Assets/Game/Scripts/Items/Equipped/BackSeat.cs Assets/Game/Scripts/Items/Equipped/ForearmSeat.cs Assets/Game/Scripts/Items/Body/BodyEquipmentController.cs Assets/Game/Editor/Tests/BackSeatTests.cs Assets/Game/Editor/Tests/ForearmSeatTests.cs
git commit -m "refactor: BackSeat and ForearmSeat so ghosts seat exactly like worn gear"
```

---

## Task 5: `BodySiteState` — the pure resolver

**Files:**
- Create: `Assets/Game/Scripts/Items/Body/Focus/BodySiteState.cs`
- Test: `Assets/Game/Editor/Tests/BodySiteStateTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/Game/Editor/Tests/BodySiteStateTests.cs
// What a site on the body shows, for every combination of what is worn there and what the cursor
// carries. Legality comes from GearMoves, the same table the server uses and the tiles predict
// with, so a site can never light amber for a move the server would refuse.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class BodySiteStateTests
    {
        private static readonly GearRef Hot0 = GearRef.Hotbar(0);
        private static readonly GearRef Left = GearRef.Body(BodySlot.LeftGauntlet);

        [Test]
        public void NothingCarriedShowsWhatIsThere()
        {
            Assert.AreEqual(SiteState.Empty, BodySiteState.Resolve(BodySlot.LeftGauntlet, null, GearRef.None, null, hovered: false));
            Assert.AreEqual(SiteState.Worn, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, GearRef.None, null, hovered: true));
        }

        [Test]
        public void ALegalCarryOverAnEmptySiteIsAPreview()
        {
            Assert.AreEqual(SiteState.Preview, BodySiteState.Resolve(BodySlot.RightGauntlet, null, Hot0, EquipKind.Gauntlet, hovered: false));
            Assert.AreEqual(SiteState.Preview, BodySiteState.Resolve(BodySlot.Back, null, Hot0, EquipKind.Back, hovered: true));
        }

        [Test]
        public void ALegalCarryOverAFilledSiteIsASwap()
        {
            Assert.AreEqual(SiteState.SwapOutline, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, Hot0, EquipKind.Gauntlet, hovered: false));
        }

        [Test]
        public void AnIllegalCarryIsOnlyRefusedWhileHovered()
        {
            Assert.AreEqual(SiteState.Refused, BodySiteState.Resolve(BodySlot.Back, null, Hot0, EquipKind.Gauntlet, hovered: true));
            Assert.AreEqual(SiteState.Empty, BodySiteState.Resolve(BodySlot.Back, null, Hot0, EquipKind.Gauntlet, hovered: false));
            Assert.AreEqual(SiteState.Refused, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, Hot0, EquipKind.Hand, hovered: true));
            Assert.AreEqual(SiteState.Worn, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, Hot0, EquipKind.Hand, hovered: false));
        }

        [Test]
        public void TheOriginOfTheCarryIsReserved()
        {
            Assert.AreEqual(SiteState.Reserved, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, Left, EquipKind.Gauntlet, hovered: false));
            Assert.AreEqual(SiteState.Reserved, BodySiteState.Resolve(BodySlot.LeftGauntlet, EquipKind.Gauntlet, Left, EquipKind.Gauntlet, hovered: true));
        }

        [Test]
        public void AGauntletCarriedFromOneArmPreviewsOnTheOther()
        {
            Assert.AreEqual(SiteState.Preview, BodySiteState.Resolve(BodySlot.RightGauntlet, null, Left, EquipKind.Gauntlet, hovered: false));
        }
    }
}
```

- [ ] **Step 2: Type-check to see it fail**

Expected: `CS0246: The type or namespace name 'SiteState' could not be found`.

- [ ] **Step 3: Write `BodySiteState.cs`**

```csharp
// Assets/Game/Scripts/Items/Body/Focus/BodySiteState.cs
namespace SpaceGame.Items
{
    /// <summary>What a site on the body is showing. See <see cref="BodySiteState.Resolve"/>.</summary>
    public enum SiteState
    {
        /// <summary>Nothing worn: the faint generic placeholder.</summary>
        Empty,
        /// <summary>The real worn item, as it is.</summary>
        Worn,
        /// <summary>Carrying something that fits an empty site: a translucent copy of it, seated.</summary>
        Preview,
        /// <summary>Carrying something that fits a filled site: an amber outline on what is worn — a swap.</summary>
        SwapOutline,
        /// <summary>Hovering with something that cannot go here.</summary>
        Refused,
        /// <summary>This site is where the carried item came from.</summary>
        Reserved,
        /// <summary>A legal click was sent and the server has not answered yet. Set by the site, never resolved.</summary>
        Committing,
    }

    /// <summary>
    /// The pure mapping from "what is worn here" and "what the cursor carries" to what the site
    /// shows. <see cref="GearMoves.Resolve"/> is its only source of legality — the same table the
    /// server decides with and the hotbar tiles predict with — so the three never disagree.
    /// </summary>
    public static class BodySiteState
    {
        public static SiteState Resolve(BodySlot slot, EquipKind? wornKind, GearRef carried, EquipKind? carriedKind, bool hovered)
        {
            GearRef here = GearRef.Body(slot);

            if (carried.IsNone) return wornKind == null ? SiteState.Empty : SiteState.Worn;
            if (carried == here) return SiteState.Reserved;

            MoveResult verdict = GearMoves.Resolve(carried, carriedKind, here, wornKind, mounted: false);
            if (verdict.Allowed) return wornKind == null ? SiteState.Preview : SiteState.SwapOutline;

            // Illegal targets stay quiet until the cursor asks: a screen full of red is a screen
            // that tells the player nothing about where the item CAN go.
            if (hovered) return SiteState.Refused;
            return wornKind == null ? SiteState.Empty : SiteState.Worn;
        }
    }
}
```

- [ ] **Step 4: Type-check and run the tests**

Expected: `0 error(s)`; `BodySiteStateTests` 6 pass.

- [ ] **Step 5: Commit** (ask first)

```bash
git add Assets/Game/Scripts/Items/Body/Focus/BodySiteState.cs Assets/Game/Editor/Tests/BodySiteStateTests.cs
git commit -m "feat: BodySiteState — pure resolver for what a body site shows"
```

---

## Task 6: `LensStandoff` and `BodyFocusCamera`

**Files:**
- Create: `Assets/Game/Scripts/Items/Body/Focus/LensStandoff.cs`
- Create: `Assets/Game/Scripts/Items/Body/Focus/BodyFocusCamera.cs`
- Test: `Assets/Game/Editor/Tests/LensStandoffTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/Game/Editor/Tests/LensStandoffTests.cs
// How far in front of the chest the body screen's lens may sit when a wall is in the way.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class LensStandoffTests
    {
        [Test]
        public void NoBlockerIsTheFullDistance()
        {
            Assert.AreEqual(2.3f, LensStandoff.Resolve(2.3f, float.PositiveInfinity, 0.25f, 0.9f), 1e-5f);
        }

        [Test]
        public void ABlockerPullsTheLensInByItsRadius()
        {
            Assert.AreEqual(1.25f, LensStandoff.Resolve(2.3f, 1.5f, 0.25f, 0.9f), 1e-5f);
        }

        [Test]
        public void ABlockerBeyondTheShotChangesNothing()
        {
            Assert.AreEqual(2.3f, LensStandoff.Resolve(2.3f, 4f, 0.25f, 0.9f), 1e-5f);
        }

        [Test]
        public void NeverNearerThanTheFloor()
        {
            Assert.AreEqual(0.9f, LensStandoff.Resolve(2.3f, 0.6f, 0.25f, 0.9f), 1e-5f);
            Assert.AreEqual(0.9f, LensStandoff.Resolve(2.3f, 0f, 0.25f, 0.9f), 1e-5f);
        }
    }
}
```

- [ ] **Step 2: Type-check to see it fail**

Expected: `CS0103: The name 'LensStandoff' does not exist`.

- [ ] **Step 3: Write `LensStandoff.cs`**

```csharp
// Assets/Game/Scripts/Items/Body/Focus/LensStandoff.cs
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How far in front of its subject a focus lens may sit when something solid is in the way.
    /// Pure, so the arithmetic is testable without a physics scene.
    /// </summary>
    public static class LensStandoff
    {
        /// <param name="wanted">The authored distance.</param>
        /// <param name="hit">Distance to the nearest blocker along the shot, or +infinity for none.</param>
        /// <param name="radius">The probe's radius — the lens stops this far short of the blocker.</param>
        /// <param name="floor">Nearest the lens is ever allowed; the crop gets tight rather than the lens going through the body.</param>
        public static float Resolve(float wanted, float hit, float radius, float floor)
        {
            float allowed = float.IsPositiveInfinity(hit) ? wanted : hit - radius;
            return Mathf.Max(floor, Mathf.Min(wanted, allowed));
        }
    }
}
```

- [ ] **Step 4: Write `BodyFocusCamera.cs`**

```csharp
// Assets/Game/Scripts/Items/Body/Focus/BodyFocusCamera.cs
using System;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// The view used while arranging gear on your own body: in front of the chest, level, narrow.
    ///
    /// <para>
    /// The shot is on the body's flattened FORWARD, looking back at it — the camera goes to where
    /// the player faces; the body never turns. Framed from the thighs up (the look target is the
    /// chest bone), so the two forearms and the shoulders, which are the targets, are large. FOV 40
    /// is the pack's narrow lens: flat perspective, honest sizes.
    /// </para>
    /// <para>
    /// If the player is facing a wall, a spherecast from the chest along the shot pulls the lens in
    /// to the wall, never nearer than <see cref="Shot.MinLensDistance"/>. The player's own
    /// colliders are always ignored — the probe starts inside them.
    /// </para>
    /// </summary>
    public sealed class BodyFocusCamera : FocusCamera
    {
        /// <summary>The authored shot. Serialized on <see cref="BodyFocusSession"/> so it is tuned in the Inspector, not in code.</summary>
        [Serializable]
        public struct Shot
        {
            [Tooltip("Metres in front of the look target, along the body's flattened forward.")]
            public float LensDistance;

            [Tooltip("Metres the lens sits above the look target.")]
            public float LensRise;

            [Tooltip("Degrees the lens looks down. Small — the horizon should stay honest.")]
            public float PitchDown;

            public float FieldOfView;

            [Tooltip("Seconds the camera takes to fly from the eye to the shot.")]
            public float FlyInSeconds;

            [Tooltip("Nearest the lens is allowed when a wall pulls it in.")]
            public float MinLensDistance;

            [Tooltip("Radius of the wall probe.")]
            public float PullInRadius;

            [Tooltip("What can pull the lens in. The player's own colliders are always ignored.")]
            public LayerMask Blockers;

            public static Shot Default => new()
            {
                LensDistance = 2.3f,
                LensRise = 0.10f,
                PitchDown = 4f,
                FieldOfView = 40f,
                FlyInSeconds = 0.4f,
                MinLensDistance = 0.9f,
                PullInRadius = 0.25f,
                Blockers = ~0,
            };
        }

        private Transform target;
        private Transform ignore;
        private Vector3 forward = Vector3.forward;
        private Shot shot;

        private readonly RaycastHit[] hits = new RaycastHit[8];

        /// <param name="lookTarget">What the lens looks at — the chest bone. Tracked live.</param>
        /// <param name="bodyForward">The body's forward; flattened here and frozen for the session.</param>
        /// <param name="ignoreRoot">The player's root: nothing under it can pull the lens in.</param>
        /// <param name="playerCamera">Switched off, with its AudioListener, for the duration.</param>
        public static BodyFocusCamera Spawn(Transform lookTarget, Vector3 bodyForward, Transform ignoreRoot, in Shot shot, Camera playerCamera)
        {
            if (lookTarget == null) return null;

            var go = new GameObject("BodyFocusCamera");
            var focus = go.AddComponent<BodyFocusCamera>();

            focus.target = lookTarget;
            focus.ignore = ignoreRoot;
            focus.shot = shot;

            var flat = new Vector3(bodyForward.x, 0f, bodyForward.z);
            focus.forward = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;

            focus.Begin(playerCamera);
            return focus;
        }

        protected override bool HasTarget => target != null;
        protected override float PitchDown => shot.PitchDown;
        protected override float Fov => shot.FieldOfView;
        protected override float FlyInSeconds => shot.FlyInSeconds;

        /// <summary>Looking back down the forward the lens sits on.</summary>
        protected override float LensYaw() => Quaternion.LookRotation(-forward, Vector3.up).eulerAngles.y;

        protected override float FocusDistance() => Vector3.Distance(transform.position, target.position);

        protected override Vector3 LensPosition()
        {
            Vector3 origin = target.position;
            float distance = LensStandoff.Resolve(shot.LensDistance, NearestBlocker(origin), shot.PullInRadius, shot.MinLensDistance);
            return origin + forward * distance + Vector3.up * shot.LensRise;
        }

        /// <summary>Distance to the nearest thing along the shot that is not the player, or +infinity.</summary>
        private float NearestBlocker(Vector3 origin)
        {
            int count = Physics.SphereCastNonAlloc(origin, shot.PullInRadius, forward, hits,
                                                   shot.LensDistance + shot.PullInRadius, shot.Blockers,
                                                   QueryTriggerInteraction.Ignore);

            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Transform hit = hits[i].transform;
                if (ignore != null && (hit == ignore || hit.IsChildOf(ignore))) continue;
                nearest = Mathf.Min(nearest, hits[i].distance);
            }

            return nearest;
        }
    }
}
```

- [ ] **Step 5: Type-check and run the tests**

Expected: `0 error(s)`; `LensStandoffTests` 4 pass.

- [ ] **Step 6: Commit** (ask first)

```bash
git add Assets/Game/Scripts/Items/Body/Focus/LensStandoff.cs Assets/Game/Scripts/Items/Body/Focus/BodyFocusCamera.cs Assets/Game/Editor/Tests/LensStandoffTests.cs
git commit -m "feat: BodyFocusCamera — chest-front focus shot with wall pull-in"
```

---

## Task 7: `BodySite` — one site's ghosts, states and hit rect

No unit test: this is a display class (edit-mode `AddComponent` raises no `Awake`, and nothing here has logic that is not already pinned by `BodySiteStateTests`, `BackSeatTests` and the socket tests). It is verified by screenshot in Task 11.

**Files:**
- Create: `Assets/Game/Scripts/Items/Body/Focus/BodySite.cs`

- [ ] **Step 1: Write `BodySite.cs`**

```csharp
// Assets/Game/Scripts/Items/Body/Focus/BodySite.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// One place on the body where worn gear lives, as the body screen shows it: a gauntlet site
    /// on each forearm and the back.
    ///
    /// <para>
    /// A site is anchored to <b>the same transform the worn item uses</b> — a second
    /// <see cref="EquipItemSocket"/> on the hand bone for a gauntlet, the spine bone with the
    /// prefab's <see cref="WornFit"/> for the back — so a ghost sits exactly where the real thing
    /// will. Its ghosts are <see cref="DisplayCopy"/> copies (no scripts, no physics) drawn with
    /// one translucent <see cref="TintMaterials"/> material each.
    /// </para>
    /// <para>
    /// What it shows is decided elsewhere (<see cref="BodySiteState.Resolve"/>) and handed in
    /// through <see cref="Apply"/>. This class only knows how each state LOOKS. Everything it
    /// creates is local to this machine and dies with <see cref="Dispose"/>.
    /// </para>
    /// </summary>
    public sealed class BodySite
    {
        // ── The look of each state. The alphas are what make a ghost a ghost. ──
        private static readonly Color PlaceholderBody = WithAlpha(UITheme.Accent, 0.22f);
        private static readonly Color PlaceholderHover = WithAlpha(UITheme.Accent, 0.35f);
        private static readonly Color PlaceholderOutline = WithAlpha(UITheme.Accent, 0.7f);
        private static readonly Color PreviewBody = WithAlpha(HotbarStyle.Amber, 0.55f);
        private static readonly Color PreviewHover = WithAlpha(HotbarStyle.Amber, 0.8f);
        private static readonly Color PreviewOutline = WithAlpha(HotbarStyle.Amber, 0.9f);
        private static readonly Color CommitBody = WithAlpha(HotbarStyle.Amber, 0.9f);
        private static readonly Color RefusedBody = WithAlpha(UITheme.Danger, 0.45f);
        private static readonly Color ReservedBody = WithAlpha(UITheme.Muted, 0.30f);

        // ── Feel ──
        private const float PopSeconds = 0.15f;
        private const float PopScale = 1.06f;
        private const float ShakeSeconds = 0.25f;
        private const float ShakeMetres = 0.006f;
        private const float ShakeFrequency = 55f;

        /// <summary>
        /// One site's three rim materials.
        ///
        /// <para>
        /// <b>Per site, never shared between them.</b> <see cref="OutlineShell.Build"/> writes
        /// <c>_OutlineWidth</c> onto the material it is handed — the width is per visual, computed
        /// from that visual's own size — so two sites tracing shells from ONE material would fight
        /// over it and both shells would render at whichever width was written last. Three tiny
        /// materials per site is the price of each site's outline being its own.
        /// </para>
        /// </summary>
        public sealed class Palette : IDisposable
        {
            public readonly Material SwapRim = TintMaterials.Rim("BodySwapRim", HotbarStyle.Amber, OutlineShell.MinOutlineWidth);
            public readonly Material HoverRim = TintMaterials.Rim("BodyHoverRim", new Color(1f, 0.92f, 0.6f, 1f), OutlineShell.MinOutlineWidth);
            public readonly Material RefusedRim = TintMaterials.Rim("BodyRefusedRim", new Color(1f, 0.42f, 0.36f, 1f), OutlineShell.MinOutlineWidth);

            public void Dispose()
            {
                UnityEngine.Object.Destroy(SwapRim);
                UnityEngine.Object.Destroy(HoverRim);
                UnityEngine.Object.Destroy(RefusedRim);
            }
        }

        /// <summary>A ghost copy and what it needs to be shaken and popped back to rest.</summary>
        private sealed class Ghost
        {
            public GameObject Go;
            public Material Tint;
            public Vector3 RestPosition;
            public Vector3 RestScale;
            public GameObject Of;   // previews only: the prefab this is a copy of
        }

        public BodySlot Slot { get; }
        public SiteState State { get; private set; }

        private readonly BodyEquipmentController body;
        private readonly EquipItemSocket socket;   // gauntlets: the hand, for its thumb side; null for the back
        private readonly Transform forearm;        // gauntlets: the bone the device is strapped to; null for the back
        private readonly Transform backBone;       // the back; null for gauntlets
        private readonly GameObject placeholderPrefab;

        /// <summary>This site's own rim materials — see <see cref="Palette"/> for why they are not shared.</summary>
        private readonly Palette palette;

        private Ghost placeholder;
        private Ghost preview;
        private readonly List<GameObject> shell = new();

        private GameObject hiddenWorn;
        private readonly List<Renderer> hidden = new();

        private InventoryItem lastCarried;
        private bool hovered;
        private float popUntil;
        private float shakeUntil;
        private bool animating;

        public BodySite(BodySlot slot, BodyEquipmentController body, EquipItemSocket socket, Transform forearm,
                        Transform backBone, GameObject placeholderPrefab)
        {
            Slot = slot;
            this.body = body;
            this.socket = socket;
            this.forearm = forearm;
            this.backBone = backBone;
            this.placeholderPrefab = placeholderPrefab;
            palette = new Palette();
        }

        /// <summary>Is there anywhere to seat a ghost? False on a rig with no such bone.</summary>
        public bool HasAnchor => Slot == BodySlot.Back ? backBone != null : (forearm != null && socket != null);

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>Show <paramref name="state"/>. <paramref name="carried"/> is what the cursor holds, for previews.</summary>
        public void Apply(SiteState state, InventoryItem carried, bool isHovered)
        {
            State = state;
            lastCarried = carried;
            hovered = isHovered;

            GameObject worn = body.WornInstance(Slot);
            bool reserve = state == SiteState.Reserved;

            SetWornHidden(worn, reserve);

            bool showPreview = state is SiteState.Preview or SiteState.Committing;
            bool showPlaceholder = state is SiteState.Empty or SiteState.Reserved
                                   || (state == SiteState.Refused && worn == null);

            if (showPreview) EnsurePreview(carried);
            else DestroyGhost(ref preview);

            if (showPlaceholder) EnsurePlaceholder();
            else if (placeholder != null) placeholder.Go.SetActive(false);

            OutlineShell.Clear(shell);
            if (worn != null && !reserve)
            {
                Material rim = state switch
                {
                    SiteState.SwapOutline => palette.SwapRim,
                    SiteState.Committing => palette.SwapRim,
                    SiteState.Refused => palette.RefusedRim,
                    SiteState.Worn when hovered => palette.HoverRim,
                    _ => null,
                };
                if (rim != null) OutlineShell.Build(worn, rim, hovered ? 1.3f : 1f, shell);
            }

            Recolour();
        }

        /// <summary>A legal click was sent. Brighten and pop until the answer redraws us.</summary>
        public void Commit()
        {
            State = SiteState.Committing;
            popUntil = Time.unscaledTime + PopSeconds;
            animating = true;
            Recolour();
        }

        /// <summary>A refused click: a red flick and a shake, then back to whatever we were showing.</summary>
        public void Refuse()
        {
            shakeUntil = Time.unscaledTime + ShakeSeconds;
            animating = true;

            GameObject worn = body.WornInstance(Slot);
            if (worn != null && State != SiteState.Reserved)
                OutlineShell.Build(worn, palette.RefusedRim, 1.3f, shell);

            Recolour();
        }

        /// <summary>Drive the pop and the shake. Call once a frame while the screen is up.</summary>
        public void Tick()
        {
            if (!animating) return;

            float now = Time.unscaledTime;
            Ghost ghost = preview ?? (placeholder != null && placeholder.Go.activeSelf ? placeholder : null);

            bool shaking = now < shakeUntil;
            bool popping = now < popUntil;

            if (ghost != null)
            {
                Transform t = ghost.Go.transform;

                // Along the ghost's own X, in world metres: the parent is a bone whose scale is not 1.
                float parentScale = Mathf.Max(1e-4f, t.parent != null ? t.parent.lossyScale.x : 1f);
                float jitter = shaking
                    ? Mathf.Sin(now * ShakeFrequency) * ShakeMetres * ((shakeUntil - now) / ShakeSeconds) / parentScale
                    : 0f;
                t.localPosition = ghost.RestPosition + new Vector3(jitter, 0f, 0f);

                float pop = popping ? Mathf.Lerp(PopScale, 1f, 1f - (popUntil - now) / PopSeconds) : 1f;
                t.localScale = ghost.RestScale * pop;
            }

            if (!shaking && !popping)
            {
                animating = false;
                // The flash is over: draw the state we were in before the refusal again.
                if (State != SiteState.Committing) Apply(State, lastCarried, hovered);
            }
            else
            {
                Recolour();
            }
        }

        // ── Screen space ──────────────────────────────────────────────────────

        /// <summary>
        /// Where this site is on the overlay, in canvas pixels: the projected box of whatever it is
        /// currently showing, padded. False when nothing is showing or it is behind the lens.
        ///
        /// <para>
        /// The hit test is done here, in screen space, on purpose. Three sites do not justify
        /// colliders, and a trigger anywhere near the player's hierarchy or on a gameplay layer is a
        /// thing the movement probes, the scanner and other players' rays can hit.
        /// </para>
        /// </summary>
        public bool TryCanvasRect(WorldOverlay overlay, float padding, out Rect rect)
        {
            rect = default;
            if (overlay == null) return false;

            GameObject visual = preview != null ? preview.Go
                : placeholder != null && placeholder.Go.activeSelf ? placeholder.Go
                : body.WornInstance(Slot);
            if (visual == null) return false;

            bool any = false;
            Vector2 min = Vector2.positiveInfinity;
            Vector2 max = Vector2.negativeInfinity;

            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled || renderer.gameObject.name == OutlineShell.ShellName) continue;

                Bounds b = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                             (i & 2) == 0 ? b.min.y : b.max.y,
                                             (i & 4) == 0 ? b.min.z : b.max.z);
                    if (!overlay.Project(corner, out Vector2 p)) return false;
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                    any = true;
                }
            }

            if (!any) return false;
            rect = Rect.MinMaxRect(min.x - padding, min.y - padding, max.x + padding, max.y + padding);
            return true;
        }

        /// <summary>The world point sounds for this site play from.</summary>
        public Vector3 AnchorPosition => Slot == BodySlot.Back
            ? (backBone != null ? backBone.position : Vector3.zero)
            : (forearm != null ? forearm.position : Vector3.zero);

        // ── Teardown ──────────────────────────────────────────────────────────

        /// <summary>Destroy every ghost, un-hide the worn item, clear the shells. Safe to call twice.</summary>
        public void Dispose()
        {
            RestoreWorn();
            OutlineShell.Clear(shell);
            DestroyGhost(ref placeholder);
            DestroyGhost(ref preview);
            palette.Dispose();
        }

        // ── Ghosts ────────────────────────────────────────────────────────────

        private void EnsurePlaceholder()
        {
            if (placeholder == null)
            {
                placeholder = MakeGhost(placeholderPrefab, "BodyGhost_" + Slot, PlaceholderBody, PlaceholderOutline);
                if (placeholder == null) return;
            }

            placeholder.Go.SetActive(true);
        }

        private void EnsurePreview(InventoryItem carried)
        {
            GameObject prefab = carried != null ? carried.itemPrefab : null;
            if (prefab == null) { DestroyGhost(ref preview); return; }

            if (preview != null && preview.Of == prefab) return;

            DestroyGhost(ref preview);
            preview = MakeGhost(prefab, "BodyPreview_" + Slot, PreviewBody, PreviewOutline);
            if (preview != null) preview.Of = prefab;
        }

        /// <summary>
        /// A stripped copy of <paramref name="prefab"/>, seated the way the real item is worn and
        /// repainted with one translucent material.
        /// </summary>
        private Ghost MakeGhost(GameObject prefab, string name, Color bodyColour, Color outlineColour)
        {
            if (prefab == null || !HasAnchor) return null;

            Transform anchor = Slot == BodySlot.Back ? backBone : forearm;
            GameObject copy = DisplayCopy.Make(prefab, anchor);
            if (copy == null) return null;
            copy.name = name;

            if (Slot == BodySlot.Back) BackSeat.Apply(copy, backBone, prefab.GetComponent<WornFit>());
            else ForearmSeat.Apply(copy, forearm, socket.Socket, socket.GripRotation,
                                   Slot == BodySlot.LeftGauntlet, prefab.GetComponent<GauntletFit>());

            Material tint = TintMaterials.Translucent(name, bodyColour, outlineColour, OutlineShell.WidthFor(copy, 1f));

            foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < materials.Length; i++) materials[i] = tint;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return new Ghost
            {
                Go = copy,
                Tint = tint,
                RestPosition = copy.transform.localPosition,
                RestScale = copy.transform.localScale,
            };
        }

        private static void DestroyGhost(ref Ghost ghost)
        {
            if (ghost == null) return;
            if (ghost.Go != null) UnityEngine.Object.Destroy(ghost.Go);
            if (ghost.Tint != null) UnityEngine.Object.Destroy(ghost.Tint);
            ghost = null;
        }

        private void Recolour()
        {
            bool flashing = Time.unscaledTime < shakeUntil;

            if (placeholder != null && placeholder.Go.activeSelf)
            {
                Color colour = flashing || State == SiteState.Refused ? RefusedBody
                    : State == SiteState.Reserved ? ReservedBody
                    : hovered ? PlaceholderHover
                    : PlaceholderBody;
                TintMaterials.SetBody(placeholder.Tint, colour);
            }

            if (preview != null)
            {
                Color colour = flashing ? RefusedBody
                    : State == SiteState.Committing ? CommitBody
                    : hovered ? PreviewHover
                    : PreviewBody;
                TintMaterials.SetBody(preview.Tint, colour);
            }
        }

        // ── The worn item ─────────────────────────────────────────────────────

        /// <summary>
        /// Hide the worn item while it is being carried, by switching its renderers off. Local only
        /// — peers keep seeing it where it is — and restored on every exit path, because the pack
        /// outlived its hand once and hid an item for good.
        /// </summary>
        private void SetWornHidden(GameObject worn, bool hide)
        {
            if (hiddenWorn != null && (hiddenWorn != worn || !hide)) RestoreWorn();
            if (!hide || worn == null || hiddenWorn == worn) return;

            hiddenWorn = worn;
            foreach (Renderer renderer in worn.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled || renderer.gameObject.name == OutlineShell.ShellName) continue;
                renderer.enabled = false;
                hidden.Add(renderer);
            }
        }

        private void RestoreWorn()
        {
            foreach (Renderer renderer in hidden)
                if (renderer != null) renderer.enabled = true;   // destroyed with a slot change: nothing to restore

            hidden.Clear();
            hiddenWorn = null;
        }

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);
    }
}
```

- [ ] **Step 2: Type-check**

Run: `python3 /private/tmp/claude-501/bodyscreen/check.py rebuild runtime editor`
Expected: `0 error(s)`. (`UITheme` and `HotbarStyle` are in `SpaceGame.Presentation`, hence the `using`.)

- [ ] **Step 3: Commit** (ask first)

```bash
git add Assets/Game/Scripts/Items/Body/Focus/BodySite.cs
git commit -m "feat: BodySite — ghosts, previews and hit rects for one body slot"
```

---

## Task 8: `BodyFocusSession` — the world side of the screen

**Files:**
- Create: `Assets/Game/Scripts/Items/Body/Focus/BodyFocusSession.cs`

- [ ] **Step 1: Write `BodyFocusSession.cs`**

```csharp
// Assets/Game/Scripts/Items/Body/Focus/BodyFocusSession.cs
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// Everything the body screen does in the WORLD: the spawned camera in front of the player and
    /// the three <see cref="BodySite"/>s on their body. <see cref="Presentation.BodyInventoryUI"/>
    /// is the conductor — it owns the carry and the hotbar tiles, and it tells this what the cursor
    /// holds; this tells it what the cursor is over and when a site was clicked.
    ///
    /// <para>
    /// Lives on the player prefab (wired by <c>GearGhostBuilder</c>) because the shot and the ghost
    /// prefabs are things to tune in the Inspector, not constants. Like <see cref="PackFocusSession"/>:
    /// nothing pauses, every exit is instant, and every exit path — F, Esc, death, the component
    /// being disabled — comes through <see cref="Exit"/>.
    /// </para>
    /// <para>
    /// Nothing here is sent to anyone. The camera, the ghosts, the hit rects and the hidden
    /// renderers are local; peers see the player standing still, as they already did.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BodyFocusSession : MonoBehaviour
    {
        [Header("The shot")]
        [SerializeField] private BodyFocusCamera.Shot shot = BodyFocusCamera.Shot.Default;

        [Tooltip("Seconds the camera takes to fly back to the eye when the screen closes.")]
        [SerializeField, Min(0f)] private float flyOutSeconds = 0.25f;

        [Header("Look target")]
        [Tooltip("The bone the lens looks at — framing from the thighs up.")]
        [SerializeField] private HumanBodyBones chestBone = HumanBodyBones.Chest;

        [Tooltip("Substring hints for a non-humanoid rig.")]
        [SerializeField] private string[] chestBoneNameHints = { "Chest", "Spine" };

        [Tooltip("With no chest bone at all: this far above the player's origin. The origin is about a metre above the soles.")]
        [SerializeField] private float fallbackLookHeight = 0.4f;

        [Header("Ghosts")]
        [Tooltip("What an empty gauntlet site shows: a plain cuff. Seated by its ItemGrip like a real bracer.")]
        [SerializeField] private GameObject gauntletPlaceholder;

        [Tooltip("What an empty back site shows: a mount frame rising past the shoulders. Seated by its WornFit.")]
        [SerializeField] private GameObject backPlaceholder;

        [Header("Feel")]
        [Tooltip("Seconds a sent move stays lit before we assume the server refused it.")]
        [SerializeField, Min(0.1f)] private float commitTimeoutSeconds = 1f;

        [Tooltip("Canvas pixels of slack round a site's projected box for the cursor.")]
        [SerializeField, Min(0f)] private float hitPaddingPx = 12f;

        /// <summary>The session on screen, if any. At most one, on one machine.</summary>
        public static BodyFocusSession Active { get; private set; }

        public bool IsOpen => Active == this;

        /// <summary>The site under the cursor changed; null is nothing.</summary>
        public event Action<BodySlot?> HoverChanged;

        /// <summary>Left click on a site.</summary>
        public event Action<BodySlot> SiteClicked;

        /// <summary>Left click on the world, over no site and no UI.</summary>
        public event Action NothingClicked;

        private PlayerController player;
        private IBodyEquipment slots;
        private BodyEquipmentController worn;

        private BodyFocusCamera focusCamera;
        private Transform lookAnchor;
        private Camera previousEyeOverride;

        private readonly BodySite[] sites = new BodySite[GearRef.BodySlotCount];

        private BodySlot? hovered;
        private GearRef carried = GearRef.None;
        private InventoryItem carriedItem;

        private int committing = -1;
        private float commitDeadline;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            slots = GetComponent<IBodyEquipment>();
            worn = GetComponent<BodyEquipmentController>();
        }

        private void OnDisable() => Exit();

        // ── Entering and leaving ─────────────────────────────────────────────

        /// <summary>Takes the world. False when it cannot — another session is up, or this rig has no body slots.</summary>
        public bool Enter()
        {
            if (IsOpen) return true;
            if (Active != null || worn == null || slots == null) return false;

            Camera eye = player != null ? player.PlayerCamera : null;
            focusCamera = BodyFocusCamera.Spawn(ResolveLookTarget(), transform.forward, transform, shot, eye);
            if (focusCamera == null) return false;

            Active = this;

            // Every anchor comes from the controller's own seams rather than being re-derived
            // here: a ghost has to sit exactly where the real thing will, and the controller is
            // what decides that. It resolved these bones in Start.
            foreach (BodySlot slot in new[] { BodySlot.LeftGauntlet, BodySlot.RightGauntlet })
                sites[(int)slot] = new BodySite(slot, worn, worn.HandSocket(slot), worn.ForearmBone(slot),
                                                null, gauntletPlaceholder);

            sites[(int)BodySlot.Back] = new BodySite(BodySlot.Back, worn, null, null, worn.BackBone,
                                                     backPlaceholder);

            // Labels project through the lens that is actually rendering — ours, for the duration.
            WorldOverlay overlay = WorldOverlay.Create();
            previousEyeOverride = overlay.EyeOverride;
            overlay.EyeOverride = focusCamera.Camera;

            slots.OnBodySlotChanged += OnSlotChanged;

            hovered = null;
            carried = GearRef.None;
            carriedItem = null;
            committing = -1;

            ApplyAll();
            return true;
        }

        /// <summary>Hands the world back. Safe to call when there is no session, and safe to call twice.</summary>
        public void Exit()
        {
            if (!IsOpen) return;

            Active = null;

            slots.OnBodySlotChanged -= OnSlotChanged;

            foreach (BodySite site in sites) site?.Dispose();
            Array.Clear(sites, 0, sites.Length);

            WorldOverlay overlay = WorldOverlay.Instance;
            if (overlay != null && focusCamera != null && overlay.EyeOverride == focusCamera.Camera)
                overlay.EyeOverride = previousEyeOverride;

            if (focusCamera != null) focusCamera.FlyOut(flyOutSeconds);
            focusCamera = null;

            if (lookAnchor != null) Destroy(lookAnchor.gameObject);
            lookAnchor = null;

            hovered = null;
        }

        // ── What the UI tells us ─────────────────────────────────────────────

        /// <summary>The cursor now holds <paramref name="item"/> from <paramref name="from"/> (or nothing). Every site re-resolves.</summary>
        public void SetCarry(GearRef from, InventoryItem item)
        {
            carried = from;
            carriedItem = item;
            ApplyAll();
        }

        /// <summary>A legal move to <paramref name="slot"/> was sent. The site stays lit until the answer or the timeout.</summary>
        public void Commit(BodySlot slot)
        {
            committing = (int)slot;
            commitDeadline = Time.unscaledTime + commitTimeoutSeconds;
            sites[(int)slot]?.Commit();
        }

        /// <summary>A click on a site the carried item cannot go to.</summary>
        public void Refuse(BodySlot slot)
        {
            sites[(int)slot]?.Refuse();
            Sfx.Play2D(SfxId.UiError);
        }

        /// <summary>Where a site is on the overlay, for the chips and captions. False when it is not showing.</summary>
        public bool TryCanvasRect(BodySlot slot, out Rect rect)
        {
            rect = default;
            BodySite site = IsOpen ? sites[(int)slot] : null;
            return site != null && site.TryCanvasRect(WorldOverlay.Instance, hitPaddingPx, out rect);
        }

        public SiteState StateOf(BodySlot slot) =>
            IsOpen && sites[(int)slot] != null ? sites[(int)slot].State : SiteState.Empty;

        // ── Per frame ────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (!IsOpen) return;

            if (player != null && player.IsDead) { Exit(); return; }

            // The server never answered — a lost race, or a refusal, which announces nothing.
            if (committing >= 0 && Time.unscaledTime > commitDeadline)
            {
                int slot = committing;
                committing = -1;
                ApplyAll();
                sites[slot]?.Refuse();
            }

            UpdateHover();

            foreach (BodySite site in sites) site?.Tick();

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PointerOverUi())
            {
                if (hovered.HasValue) SiteClicked?.Invoke(hovered.Value);
                else NothingClicked?.Invoke();
            }
        }

        private void UpdateHover()
        {
            BodySlot? now = null;

            WorldOverlay overlay = WorldOverlay.Instance;
            if (Mouse.current != null && overlay != null && !PointerOverUi()
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay.Layer, Mouse.current.position.ReadValue(), null, out Vector2 cursor))
            {
                float best = float.PositiveInfinity;
                for (int i = 0; i < sites.Length; i++)
                {
                    if (sites[i] == null || !sites[i].TryCanvasRect(overlay, hitPaddingPx, out Rect rect) || !rect.Contains(cursor)) continue;

                    // Nearest centre wins where two boxes overlap.
                    float d = (rect.center - cursor).sqrMagnitude;
                    if (d < best) { best = d; now = (BodySlot)i; }
                }
            }

            if (now == hovered) return;

            hovered = now;
            ApplyAll();
            HoverChanged?.Invoke(hovered);
        }

        private void OnSlotChanged(BodySlot slot, InventorySlot contents)
        {
            // The answer landed — for this site or any other; either way the world moved.
            if ((int)slot == committing) committing = -1;
            ApplyAll();
        }

        private void ApplyAll()
        {
            if (!IsOpen) return;

            EquipKind? carriedKind = carriedItem != null ? carriedItem.equipKind : null;

            for (int i = 0; i < sites.Length; i++)
            {
                if (sites[i] == null || i == committing) continue;

                var slot = (BodySlot)i;
                bool isHovered = hovered == slot;
                SiteState state = BodySiteState.Resolve(slot, KindIn(slot), carried, carriedKind, isHovered);
                sites[i].Apply(state, carriedItem, isHovered);
            }
        }

        private EquipKind? KindIn(BodySlot slot)
        {
            InventorySlot contents = slots.GetSlot(slot);
            return contents == null || contents.IsEmpty ? null : contents.Item.equipKind;
        }

        private static bool PointerOverUi() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private Transform ResolveLookTarget()
        {
            var animator = GetComponentInChildren<Animator>(true);
            Transform chest = BoneResolver.Resolve(animator, transform, chestBone, chestBoneNameHints);
            if (chest != null) return chest;

            if (lookAnchor == null)
            {
                lookAnchor = new GameObject("BodyFocusLookAnchor").transform;
                lookAnchor.SetParent(transform, false);
                lookAnchor.localPosition = Vector3.up * fallbackLookHeight;
            }

            return lookAnchor;
        }
    }
}
```

- [ ] **Step 2: Type-check**

Expected: `0 error(s)`. (`Sfx`/`SfxId` are `SpaceGame.Audio`; `PlayerController` is `SpaceGame.Characters`; `WorldOverlay`, `UITheme` are `SpaceGame.Presentation`.)

- [ ] **Step 3: Commit** (ask first)

```bash
git add Assets/Game/Scripts/Items/Body/Focus/BodyFocusSession.cs
git commit -m "feat: BodyFocusSession — camera and sites for the body screen"
```

---

## Task 9: The two placeholders

> **Revised 2026-09-03.** This task originally built a `ghost_gauntlet` model in the old arm-cuff
> family's frame. A concurrent session has since rebuilt every gauntlet on
> `components/props/gauntlet_base.blend`, whose **`Coll_GauntletBase_Plain`** variation is already
> exactly what the empty-gauntlet placeholder should be: the bracer with no device on it, authored
> at true suit scale against the skinned forearm. So the gauntlet placeholder is **an export of
> that existing variation** — no new model, no new `.blend`. Only the back placeholder is built.

**Files:**
- Create: `Assets/Game/Art/Models/_Source~/models/gear/ghost_gauntlet_export.py`
- Create: `Assets/Game/Art/Models/_Source~/models/gear/ghost_mount_frame.py`, `ghost_mount_frame_export.py`, `ghost_mount_frame_BUILD.md`
- Produces: `.../models/gear/ghost_mount_frame.blend`, `Assets/Game/Art/Models/Items/ghost_gauntlet.fbx`, `Assets/Game/Art/Models/Items/ghost_mount_frame.fbx`
- Regenerates: `Assets/Game/Art/Models/_Source~/LIBRARY.md`, `library_index.json`

Build with the `blender-model` skill's rules: `start` refuses to overwrite an existing `.blend`, the
`.blend` is the source of truth, and **a generator is never re-run over its own output**.

- [ ] **Step 1: Write `ghost_gauntlet_export.py`**

`_exportlib.export`'s `keep` parameter exists for exactly this — shipping named objects out of a
COMPONENT file — and `_gauntlet.base_object_names(variant)` already names them.

```python
"""Ship the PLAIN gauntlet base to Unity as the body screen's empty-arm placeholder.

The body screen shows a faint ghost on a gauntlet site with nothing worn on it.
That ghost should be the bracer every real gauntlet is built on, with no device
on top — which is `Coll_GauntletBase_Plain`, already in
`components/props/gauntlet_base.blend`. So there is no ghost model: this exports
that variation under its own name.

Exported from the component file rather than a model file, so `keep` names the
objects (see `_exportlib.export`). Re-running only ever reads the .blend.

    blender --background --python models/gear/ghost_gauntlet_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

from _exportlib import export, unity_path  # noqa: E402
from _gauntlet import BASE, base_object_names  # noqa: E402

DST = unity_path("Items", "ghost_gauntlet.fbx")


def main():
    export(BASE, DST, keep_armature=False, keep=base_object_names("Plain"))


main()
```

- [ ] **Step 2: Write `ghost_mount_frame.py`**

```python
"""Ghost mount frame — the body screen's placeholder for an EMPTY back slot.

Two uprights and a crossbar: a rack for something big, and deliberately NOT a
pack — every player already wears the expedition rig on their back, so a pack
silhouette peeking over the shoulders would read as a second backpack. The
frame is built to be seen PAST THE SHOULDERS, which is the only part of the
back the body screen's front view shows: the crossbar rises above the
shoulder line and the uprights drop behind the shoulders.

## Frame and origin

Origin at the bottom centre of the uprights. Width along X, height along +Z
(up), depth along Y. `_exportlib` maps Blender Z onto Unity Y and −Y onto +Z,
so in Unity the frame stands up along the spine bone's Y with the crossbar
across the shoulders and its thin dimension along the bone's Z (out of the
back) — the same frame the wing pack's `WornFit` at localEuler (0, 0, 0) uses.
If a screenshot shows the frame edge-on, the fix is `localEuler` on the
prefab's WornFit in `GearGhostBuilder`, not this file.

## Scale

Authored 1:1. The prefab's `WornFit.size` = WIDTH keeps it 1:1 on the spine.

    blender --background --python models/gear/ghost_mount_frame.py -- --out models/gear/ghost_mount_frame.blend
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)

from _buildlib import *  # noqa: E402,F403

MATS = ["Mat_Paint_Hull_Bleached"]

WIDTH = 0.90        # crossbar span, shoulder to shoulder with room — WornFit.size
HEIGHT = 0.55       # uprights, from the spine bone up past the shoulder line
BAR = 0.05          # square section of every member
UPRIGHT_X = 0.36    # half the distance between the uprights


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_GhostMountFrame")

    p = Part(mats)
    for x in (-UPRIGHT_X, UPRIGHT_X):
        p.box((x, 0.0, HEIGHT / 2.0), (BAR, BAR, HEIGHT), 0)
    p.box((0.0, 0.0, HEIGHT - BAR / 2.0), (WIDTH, BAR, BAR), 0)
    # A lower rail, so an empty frame reads as a rack and not two sticks.
    p.box((0.0, 0.0, HEIGHT * 0.45), (UPRIGHT_X * 2.0, BAR * 0.6, BAR * 0.6), 0)
    p.finish("Mesh_GhostMountFrame", coll)

    report()
    save(out)


if __name__ == "__main__":
    main()
```

- [ ] **Step 3: Write `ghost_mount_frame_export.py`**

```python
"""Ship the ghost mount frame to Unity. Reads the .blend, never writes it.

    blender --background --python models/gear/ghost_mount_frame_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "ghost_mount_frame.blend")
DST = unity_path("Items", "ghost_mount_frame.fbx")


def main():
    export(SRC, DST, keep_armature=False)


main()
```

- [ ] **Step 4: Build the frame and run both exports**

Run each from `Assets/Game/Art/Models/_Source~`:
```bash
cd Assets/Game/Art/Models/_Source~ && /Applications/Blender.app/Contents/MacOS/Blender --background --python models/gear/ghost_mount_frame.py -- --out models/gear/ghost_mount_frame.blend 2>&1 | tail -6
cd Assets/Game/Art/Models/_Source~ && /Applications/Blender.app/Contents/MacOS/Blender --background --python models/gear/ghost_mount_frame_export.py 2>&1 | tail -3
cd Assets/Game/Art/Models/_Source~ && /Applications/Blender.app/Contents/MacOS/Blender --background --python models/gear/ghost_gauntlet_export.py 2>&1 | tail -3
```
Expected: `Wrote .../ghost_mount_frame.blend` with a `Mesh_GhostMountFrame` dims line of about
`(0.90, 0.05, 0.55)`; two FBX files under `Assets/Game/Art/Models/Items/`. `ghost_gauntlet.fbx`
should carry the Plain variation's meshes only — no `_Mount` or `_Rail` objects, no hardpoint deck.
Verify by listing what actually landed in the FBX (a short Blender script that imports it and prints
object names, or `_exportlib`'s own output).

If `start` refuses because `ghost_mount_frame.blend` already exists, you are re-running a generator
over a built file — **stop**, do not delete it, and edit the `.blend` instead.

Render a check of the frame and **look at it** (`_preview.py` in the library root renders a `.blend`
to PNG; read the PNG). Expected: a П-shaped frame with a lower rail, standing up its own +Z.

- [ ] **Step 5: Write the build record**

`ghost_mount_frame_BUILD.md`:
```markdown
# Ghost Mount Frame — build record

`models/gear/ghost_mount_frame.blend` → `Assets/Game/Art/Models/Items/ghost_mount_frame.fbx`
→ `Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostBack.prefab` (built by `GearGhostBuilder`).

The body screen's placeholder for an empty back site. A rack, not a pack: every player already
wears the expedition rig, so a pack silhouette over the shoulders would read as a second backpack.
Built to be seen PAST THE SHOULDERS — the front view's only sight of the back.

| Part | Dimensions |
|---|---|
| `Mesh_GhostMountFrame` | 0.90 wide x 0.55 tall x 0.05 deep; two uprights 0.72 m apart, a crossbar, a lower rail |

- Origin at the bottom centre; up is +Z in Blender → +Y in Unity, so `WornFit.localEuler (0,0,0)`
  stands it along the spine. Edge-on in a screenshot → fix `localEuler` in `GearGhostBuilder`.
- `WornFit.size` 0.9 keeps it 1:1; `localPosition (0, 0.05, -0.22)` is the wing pack's seat.
- Generated 2026-09-03; no hand edits.

The **gauntlet** placeholder has no build record of its own: it is `Coll_GauntletBase_Plain` from
`components/props/gauntlet_base.blend`, shipped by `ghost_gauntlet_export.py`. See that component's
own `_BUILD.md`.
```

- [ ] **Step 6: Regenerate the library index**

```bash
python3 .claude/skills/blender-model/scripts/index_library.py --models-dir "Assets/Game/Art/Models/_Source~"
```
Expected: `LIBRARY.md` and `library_index.json` list `models/gear/ghost_mount_frame.blend`. (The
gauntlet ghost adds no library entry — it ships an existing component.)

- [ ] **Step 7: Commit** (ask first)

```bash
git add Assets/Game/Art/Models/_Source~/models/gear/ghost_gauntlet_export.py Assets/Game/Art/Models/_Source~/models/gear/ghost_mount_frame.py Assets/Game/Art/Models/_Source~/models/gear/ghost_mount_frame_export.py Assets/Game/Art/Models/_Source~/models/gear/ghost_mount_frame_BUILD.md Assets/Game/Art/Models/_Source~/models/gear/ghost_mount_frame.blend Assets/Game/Art/Models/Items/ghost_gauntlet.fbx Assets/Game/Art/Models/Items/ghost_mount_frame.fbx Assets/Game/Art/Models/_Source~/LIBRARY.md Assets/Game/Art/Models/_Source~/library_index.json
git commit -m "art: gauntlet-base and mount-frame placeholders for the body screen"
```
(The `.fbx.meta` files appear once the editor imports them — add them in the next commit if they
were not there yet.)

---

## Task 10: `GearGhostBuilder` — the two prefabs and the session on the player

**Files:**
- Create: `Assets/Game/Editor/Items/GearGhostBuilder.cs`
- Produces: `Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostGauntlet.prefab`, `GhostBack.prefab`
- Modifies (by running): `Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab`

- [ ] **Step 1: Write `GearGhostBuilder.cs`**

```csharp
// Assets/Game/Editor/Items/GearGhostBuilder.cs
// Builds the body screen's two placeholder prefabs from their FBX and wires them, with a
// BodyFocusSession, onto the base player prefab. Re-runnable: it overwrites the two ghost prefabs
// and only ADDS to the player prefab (an existing session keeps its tuned numbers).
using System.IO;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public static class GearGhostBuilder
    {
        private const string CuffModelPath = "Assets/Game/Art/Models/Items/ghost_gauntlet.fbx";
        private const string FrameModelPath = "Assets/Game/Art/Models/Items/ghost_mount_frame.fbx";
        private const string GauntletPrefabPath = "Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostGauntlet.prefab";
        private const string BackPrefabPath = "Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostBack.prefab";
        private const string PlayerPrefabPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        // The gauntlet ghost is the PLAIN gauntlet base, authored at true suit scale against the
        // skinned forearm — so it wears GauntletFit's own defaults, exactly as every real gauntlet
        // built on that base does. A number typed here instead would be a second source of truth
        // for where a gauntlet sits.

        /// <summary>The wing pack's seat, so the frame stands where the one back item will.</summary>
        private static readonly Vector3 BackLocalPosition = new(0f, 0.05f, -0.22f);
        private static readonly Vector3 BackLocalEuler = Vector3.zero;
        private const float BackSize = 0.9f;

        [MenuItem("Tools/SpaceGame/Items/Build Gear Ghosts")]
        public static void BuildAll()
        {
            GameObject gauntlet = BuildGauntlet();
            GameObject back = BuildBack();
            if (gauntlet == null || back == null) return;

            WireSession(gauntlet, back);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GearGhosts] Built both ghost prefabs and wired BodyFocusSession on PlayerCharacter.prefab.");
        }

        private static GameObject BuildGauntlet()
        {
            GameObject model = Load(CuffModelPath);
            if (model == null) return null;

            var root = new GameObject("GhostGauntlet");
            Nest(model, root.transform);

            GauntletFit fit = root.AddComponent<GauntletFit>();
            var so = new SerializedObject(fit);
            SerializedFields.SetFloat(so, "cuffScale", GauntletFit.DefaultCuffScale);
            SerializedFields.SetFloat(so, "lengthScale", GauntletFit.DefaultLengthScale);
            SerializedFields.SetFloat(so, "wristGap", GauntletFit.DefaultWristGap);
            so.ApplyModifiedPropertiesWithoutUndo();

            return Save(root, GauntletPrefabPath);
        }

        private static GameObject BuildBack()
        {
            GameObject model = Load(FrameModelPath);
            if (model == null) return null;

            var root = new GameObject("GhostBack");
            Nest(model, root.transform);

            WornFit fit = root.AddComponent<WornFit>();
            var so = new SerializedObject(fit);
            SerializedFields.SetVector3(so, "localPosition", BackLocalPosition);
            SerializedFields.SetVector3(so, "localEuler", BackLocalEuler);
            SerializedFields.SetFloat(so, "size", BackSize);
            so.ApplyModifiedPropertiesWithoutUndo();

            return Save(root, BackPrefabPath);
        }

        /// <summary>
        /// Add a <see cref="BodyFocusSession"/> to the BASE player prefab — savers and controllers
        /// live there, network components on the variant — and point it at the two ghosts.
        /// </summary>
        private static void WireSession(GameObject gauntlet, GameObject back)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var session = root.GetComponent<BodyFocusSession>();
                if (session == null) session = root.AddComponent<BodyFocusSession>();

                var so = new SerializedObject(session);
                SerializedFields.Set(so, "gauntletPlaceholder", gauntlet);
                SerializedFields.Set(so, "backPlaceholder", back);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject Load(string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
                Debug.LogError($"[GearGhosts] No model at {path}. Run the matching _export.py in _Source~/models/gear first.");
            return model;
        }

        /// <summary>Nest the FBX and unpack it, so a model reimport cannot silently rearrange the prefab.</summary>
        private static void Nest(GameObject model, Transform parent)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.transform.SetParent(parent, false);
            instance.name = "Model";
        }

        private static GameObject Save(GameObject root, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            if (saved == null) Debug.LogError($"[GearGhosts] Saving {path} failed.");
            return saved;
        }
    }
}
```

- [ ] **Step 2: Type-check, then run the menu item**

Type-check: `0 error(s)`. Make sure the editor has imported the two FBX files and compiled (focus it or **Assets ▸ Refresh**). Then click **Tools ▸ SpaceGame ▸ Items ▸ Build Gear Ghosts** (AppleScript: open `Tools`, then `SpaceGame`, then `Items`, then the item).

Expected: the console logs `[GearGhosts] Built both ghost prefabs ...`; `git status` shows the two new prefabs (+ metas) and a modified `PlayerCharacter.prefab`.

Verify the wiring on disk:
```bash
grep -c "gauntletPlaceholder\|backPlaceholder" Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab
grep -n "cuffScale\|lengthScale\|wristGap" Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostGauntlet.prefab
grep -n "localPosition: {x: 0, y: 0.05\|size: 0.9" Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostBack.prefab
```
Expected: `2`; `cuffScale: 1`, `lengthScale: 1`, `wristGap: 0.02`; the back fit lines present. If the player prefab did not change, the editor's AssetDatabase is read-only (an MPPM clone) — see `docs/AI/DEFECTS.md` / the `AssetDatabase Read-Only` note and run it in the main editor.

- [ ] **Step 3: Commit** (ask first)

```bash
git add Assets/Game/Editor/Items/GearGhostBuilder.cs Assets/Game/Prefabs/Items/Equipment/Ghosts Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab Assets/Game/Art/Models/Items/ghost_gauntlet.fbx.meta Assets/Game/Art/Models/Items/ghost_mount_frame.fbx.meta
git commit -m "feat: GearGhostBuilder — ghost prefabs and BodyFocusSession on the player"
```

---

## Task 11: Rewrite `BodyInventoryUI` over sites and tiles

**Files:**
- Rewrite: `Assets/Game/Scripts/Presentation/UI/Pages/BodyInventoryUI.cs`

- [ ] **Step 1: Replace the file**

```csharp
// Assets/Game/Scripts/Presentation/UI/Pages/BodyInventoryUI.cs
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SpaceGame.Characters;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The body screen: your own character, seen from the front in the live world, with the three
    /// worn-gear sites on the body and the hand hotbar's three tiles along the bottom. Opened with F.
    ///
    /// <para>
    /// <b>Click to carry.</b> Click a filled site or tile and its icon follows the cursor; click
    /// another site or tile to put it there, swapping if that one is full. While something is
    /// carried every site it can go to lights: a translucent copy of the item seated where it will
    /// sit on an empty site, an amber outline on a filled one. Hovering a site it cannot go to
    /// tints it red; clicking there shakes it. Nothing moves locally: a legal click sends one
    /// request and the slot-change events that come back redraw everything. The same gesture the
    /// backpack's hand uses, on the same button, so the two screens are one language.
    /// </para>
    /// <para>
    /// This class is the conductor. The world — the camera, the ghosts, what the cursor is over —
    /// is <see cref="BodyFocusSession"/> on the player; this owns the carry, the tiles, and the
    /// chips and captions drawn on <see cref="WorldOverlay"/>. A gameplay overlay in the
    /// <see cref="DevInventoryUI"/> mould: a singleton that lives across scene loads, builds its
    /// canvas lazily, and takes input, look and the cursor through <see cref="GameplayMenuScope"/>
    /// — without stopping the clock, and without the HUD, whose hotbar would otherwise be drawn
    /// twice. No panel and no backdrop: the world is the backdrop.
    /// </para>
    /// </summary>
    public class BodyInventoryUI : MonoBehaviour
    {
        private const float OpenSeconds = 0.14f;
        private const float HotbarFromBottom = 96f;
        private const float CaptionGap = 12f;
        private const float ChipGap = 26f;
        private const float ChipHeight = 30f;
        private const float KeyChipWidth = 44f;
        private const float BackChipWidth = 116f;
        private const float ChipFontSize = 18f;

        private static BodyInventoryUI instance;

        public static bool IsOpen => instance != null && instance.open;

        private InputControls inputs;

        private bool open;
        private bool built;
        private float visibility;

        private CanvasGroup group;
        private RectTransform carryRoot;
        private Image carryIcon;

        private readonly List<Tile> tiles = new();

        private PlayerController player;
        private IPlayerInventory hotbar;
        private IBodyEquipment body;
        private BodyFocusSession session;

        /// <summary>What the cursor is carrying, or none.</summary>
        private GearRef carried = GearRef.None;

        /// <summary>The tile under the cursor, or none.</summary>
        private GearRef hoveredTile = GearRef.None;

        /// <summary>The site under the cursor, or null.</summary>
        private BodySlot? hoveredSite;

        private readonly Chip[] chips = new Chip[GearRef.BodySlotCount];
        private TextMeshProUGUI caption;

        private sealed class Tile
        {
            public GearRef Slot;
            public GearTile View;
        }

        /// <summary>A key label pinned beside a site: Q, E, SPACE ×2.</summary>
        private sealed class Chip
        {
            public RectTransform Rect;
            public float HalfWidth;
        }

        // ------------------------------------------------------------------ bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject("BodyInventory");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BodyInventoryUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            // Its own copy of the UI map, because the player's own input asset is switched off
            // for as long as this screen holds the scope — see GameplayMenuScope.
            inputs = new InputControls();
            inputs.UI.BodyInventory.performed += _ => Toggle();
            inputs.UI.Cancel.performed += _ => { if (open) Close(); };
            inputs.UI.Enable();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;

            Unsubscribe();

            if (inputs != null)
            {
                inputs.UI.Disable();
                inputs.Dispose();
            }

            GameplayMenuScope.Exit(this);
        }

        // ---------------------------------------------------------------------- input

        public void Toggle()
        {
            // F is a letter before it is a shortcut, so a focused field owns it.
            if (IsTypingInField()) return;

            if (open) Close();
            else Open();
        }

        private static bool IsTypingInField()
        {
            GameObject selected = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject
                : null;

            return selected != null
                   && selected.TryGetComponent(out TMP_InputField field)
                   && field.isFocused;
        }

        public void Open()
        {
            if (open) return;

            player = GameplayMenuScope.FindLocalPlayer();
            if (player == null) return;

            hotbar = player.PlayerInventory;
            body = player.GetComponent<IBodyEquipment>();
            session = player.GetComponent<BodyFocusSession>();
            if (hotbar == null || body == null || session == null) return;

            // A rider's hands are on the controls, and nothing on the body may move mid-flight.
            if (body.IsMounted) return;

            if (!GameplayMenuScope.Enter(this, freezeTime: false, hideHud: true)) return;

            if (!session.Enter())
            {
                GameplayMenuScope.Exit(this);
                return;
            }

            open = true;
            carried = GearRef.None;
            hoveredTile = GearRef.None;
            hoveredSite = null;

            UIBuilder.EnsureEventSystem();
            if (!built) Build();
            EnsureChips();

            group.gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;

            hotbar.OnSlotChanged += OnHotbarChanged;
            hotbar.OnSlotSelected += OnHotbarSelected;
            body.OnBodySlotChanged += OnBodyChanged;
            session.HoverChanged += OnSiteHover;
            session.SiteClicked += OnSiteClicked;
            session.NothingClicked += OnNothingClicked;

            Refresh();
        }

        public void Close()
        {
            if (!open) return;

            open = false;
            carried = GearRef.None;
            hoveredTile = GearRef.None;
            hoveredSite = null;

            group.blocksRaycasts = false;
            group.interactable = false;
            if (carryRoot != null) carryRoot.gameObject.SetActive(false);
            ShowChips(false);

            Unsubscribe();

            // Idempotent on the session's side too, and safe when it has already exited itself.
            if (session != null) session.Exit();

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            GameplayMenuScope.Exit(this);
        }

        private void Unsubscribe()
        {
            if (hotbar != null)
            {
                hotbar.OnSlotChanged -= OnHotbarChanged;
                hotbar.OnSlotSelected -= OnHotbarSelected;
            }

            if (body != null) body.OnBodySlotChanged -= OnBodyChanged;

            if (session != null)
            {
                session.HoverChanged -= OnSiteHover;
                session.SiteClicked -= OnSiteClicked;
                session.NothingClicked -= OnNothingClicked;
            }
        }

        private void OnHotbarChanged(int index, InventorySlot slot) => OnAnySlotChanged();
        private void OnHotbarSelected(InventorySlot slot) => Refresh();
        private void OnBodyChanged(BodySlot slot, InventorySlot contents) => OnAnySlotChanged();

        /// <summary>
        /// The world moved something. Whatever was being carried may no longer be where it was,
        /// so the carry is dropped rather than left pointing at a slot that now holds something
        /// else — the same reason the pack's hand lets go on any change it did not make.
        /// </summary>
        private void OnAnySlotChanged()
        {
            carried = GearRef.None;
            Refresh();
        }

        private void Update()
        {
            if (!built) return;

            float target = open ? 1f : 0f;
            if (!Mathf.Approximately(visibility, target))
            {
                visibility = Mathf.MoveTowards(visibility, target, Time.unscaledDeltaTime / OpenSeconds);
                group.alpha = visibility * visibility * (3f - 2f * visibility);

                if (visibility <= 0f) group.gameObject.SetActive(false);
            }

            if (!open) return;

            // The session can end without us: death, the player being despawned, the component
            // being disabled. It tears down its own half — camera, ghosts, hidden renderers — and
            // deliberately calls nothing outward while doing it, because those are teardown paths
            // where re-entrancy is the thing that bites. So the screen asks instead. Without this
            // the UI would sit open holding GameplayMenuScope over a world with no focus camera,
            // and the player would be left with a cursor and no controls.
            if (session == null || !session.IsOpen) { Close(); return; }

            if (!carried.IsNone && carryRoot != null && Mouse.current != null)
                carryRoot.position = Mouse.current.position.ReadValue();

            PlaceChips();
        }

        // ------------------------------------------------------------------- actions

        private void OnTileClicked(Tile tile) => OnSlotClicked(tile.Slot, refused: tile.View.Shake);

        private void OnSiteClicked(BodySlot slot) => OnSlotClicked(GearRef.Body(slot), refused: () => session.Refuse(slot));

        /// <summary>
        /// One click, wherever it landed. Pick up, put back, or ask the server to move — the site
        /// and the tile differ only in how they show a refusal.
        /// </summary>
        private void OnSlotClicked(GearRef slot, System.Action refused)
        {
            if (!open) return;

            if (carried.IsNone)
            {
                if (KindAt(slot) == null) return;

                carried = slot;
                Refresh();
                return;
            }

            if (slot == carried)
            {
                carried = GearRef.None;
                Refresh();
                return;
            }

            MoveResult verdict = Predict(slot);

            if (!verdict.Allowed)
            {
                refused();
                return;
            }

            // The request is the whole action. The icon goes back to its origin now; the answer
            // arrives as slot-change events and redraws every tile and site. A site stays lit as
            // "committing" until then, so the click is acknowledged before the round trip.
            body.RequestMove(carried, slot);
            if (slot.IsBody) session.Commit(slot.Slot);
            carried = GearRef.None;
            Refresh();
        }

        /// <summary>A click on the world with something in hand puts it back where it came from.</summary>
        private void OnNothingClicked()
        {
            if (!open || carried.IsNone) return;

            carried = GearRef.None;
            Refresh();
        }

        private void OnTileHover(Tile tile, bool over)
        {
            hoveredTile = over ? tile.Slot : hoveredTile == tile.Slot ? GearRef.None : hoveredTile;
            Refresh();
        }

        private void OnSiteHover(BodySlot? slot)
        {
            hoveredSite = slot;
            RefreshCaption();
        }

        private MoveResult Predict(GearRef target) =>
            GearMoves.Resolve(carried, KindAt(carried), target, KindAt(target), mounted: false);

        private InventoryItem ItemAt(GearRef slot)
        {
            if (slot.IsNone) return null;

            InventorySlot contents = slot.IsBody ? body.GetSlot(slot.Slot) : hotbar.GetSlot(slot.Index);
            return contents == null || contents.IsEmpty ? null : contents.Item;
        }

        private EquipKind? KindAt(GearRef slot)
        {
            InventoryItem item = ItemAt(slot);
            return item != null ? item.equipKind : null;
        }

        private void Refresh()
        {
            if (!built || hotbar == null || body == null) return;

            foreach (Tile tile in tiles)
            {
                InventoryItem item = ItemAt(tile.Slot);
                bool isCarried = tile.Slot == carried;
                bool isHovered = tile.Slot == hoveredTile;
                bool selected = tile.Slot.Index == hotbar.SelectedSlotIndex;

                bool dropTarget = false, refused = false;
                if (!carried.IsNone && isHovered && !isCarried)
                {
                    MoveResult verdict = Predict(tile.Slot);
                    dropTarget = verdict.Allowed;
                    refused = !verdict.Allowed;
                }

                bool worn = item != null && !BodySlotRules.HandEquips(item.equipKind);

                tile.View.Refresh(item, selected, isHovered, dropTarget, refused, isReserved: isCarried, isWorn: worn);
            }

            InventoryItem carriedItem = ItemAt(carried);
            bool carrying = carriedItem != null;

            if (carryRoot != null) carryRoot.gameObject.SetActive(carrying);
            if (carryIcon != null)
            {
                carryIcon.sprite = carrying ? carriedItem.icon : null;
                carryIcon.enabled = carrying && carriedItem.icon != null;
            }

            if (session != null && open) session.SetCarry(carried, carriedItem);

            RefreshCaption();
        }

        // ---------------------------------------------------------------- captions

        /// <summary>
        /// The one line of text beside the hovered site: what is there and its key, or what a
        /// click would do. Refusals get no text — the red tint and the shake are the answer.
        /// </summary>
        private void RefreshCaption()
        {
            if (caption == null) return;

            string text = string.Empty;

            if (open && hoveredSite.HasValue)
            {
                BodySlot slot = hoveredSite.Value;
                InventoryItem here = ItemAt(GearRef.Body(slot));
                InventoryItem carriedItem = ItemAt(carried);

                switch (session.StateOf(slot))
                {
                    case SiteState.Empty:
                        text = SlotName(slot) + "  ·  " + KeyOf(slot);
                        break;
                    case SiteState.Worn:
                    case SiteState.Reserved:
                        if (here != null) text = here.itemName + "  ·  " + KeyOf(slot);
                        break;
                    case SiteState.Preview:
                    case SiteState.Committing:
                        if (carriedItem != null) text = carriedItem.itemName + "  ·  " + KeyOf(slot);
                        break;
                    case SiteState.SwapOutline:
                        if (here != null && carriedItem != null) text = "Swap  ·  " + here.itemName + " ↔ " + carriedItem.itemName;
                        break;
                }
            }

            caption.text = text;
            caption.enabled = !string.IsNullOrEmpty(text);
        }

        private static string SlotName(BodySlot slot) => slot switch
        {
            BodySlot.LeftGauntlet => "Left gauntlet",
            BodySlot.RightGauntlet => "Right gauntlet",
            _ => "Back",
        };

        private static string KeyOf(BodySlot slot) => slot switch
        {
            BodySlot.LeftGauntlet => "Q",
            BodySlot.RightGauntlet => "E",
            _ => "SPACE ×2",
        };

        // --------------------------------------------------------------------- build

        private void Build()
        {
            built = true;

            var canvasGo = new GameObject("BodyInventoryCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2060; // above the HUD and the trade screen, below the dev browser

            UIScale.Configure(canvasGo.GetComponent<CanvasScaler>());

            group = canvasGo.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            var root = (RectTransform)canvasGo.transform;

            BuildHeader(root);
            BuildHotbar(root);
            BuildCarry(root);

            visibility = 0f;
            group.gameObject.SetActive(false);
        }

        private void BuildHeader(RectTransform host)
        {
            var header = UIBuilder.Rect("Header", host);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 72f);

            var titleRect = UIBuilder.LeftColumn(UIBuilder.Rect("Title", header), 34f, 400f);
            UIBuilder.Label(titleRect, "BODY GEAR", UITheme.HeadingSize, UITheme.Bright, TextAlignmentOptions.Left, FontStyles.Bold);

            var hintRect = UIBuilder.RightColumn(UIBuilder.Rect("Hint", header), 34f, 420f);
            UIBuilder.Label(hintRect, "click to pick up  ·  click to place  ·  F closes", UITheme.CaptionSize,
                UITheme.Faint, TextAlignmentOptions.Right);
        }

        /// <summary>The three hand slots along the bottom — the HUD's own tiles, since the HUD is hidden.</summary>
        private void BuildHotbar(RectTransform host)
        {
            const float slot = HotbarStyle.SlotWidth;
            const float gap = HotbarStyle.SlotSpacing;

            int size = hotbar != null ? hotbar.GetInventorySize() : 3;
            float row = -(slot + gap) * (size - 1) * 0.5f;

            for (int i = 0; i < size; i++)
                AddTile(host, GearRef.Hotbar(i), $"Slot {i + 1}", (i + 1).ToString(), new Vector2(row + i * (slot + gap), HotbarFromBottom));

            var captionRect = UIBuilder.Rect("Hands caption", host);
            captionRect.anchorMin = new Vector2(0.5f, 0f);
            captionRect.anchorMax = new Vector2(0.5f, 0f);
            captionRect.pivot = new Vector2(0.5f, 1f);
            captionRect.sizeDelta = new Vector2(HotbarStyle.SlotWidth * 2.2f, 24f);
            captionRect.anchoredPosition = new Vector2(0f, HotbarFromBottom - HotbarStyle.SlotHeight * 0.5f - CaptionGap);
            UIBuilder.Label(captionRect, "Hands  ·  1 – " + size, UITheme.CaptionSize, UITheme.Muted, TextAlignmentOptions.Center);
        }

        private void AddTile(RectTransform host, GearRef slot, string name, string key, Vector2 at)
        {
            GearTile view = GearTile.Build(host, name, key);

            RectTransform rect = view.Rect;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = at;

            var element = view.GetComponent<LayoutElement>();
            if (element != null) element.ignoreLayout = true;

            var tile = new Tile { Slot = slot, View = view };
            view.Clicked += _ => OnTileClicked(tile);
            view.HoverChanged += (_, over) => OnTileHover(tile, over);
            tiles.Add(tile);
        }

        /// <summary>The icon that follows the cursor while something is carried. Never a raycast target.</summary>
        private void BuildCarry(RectTransform root)
        {
            carryRoot = UIBuilder.Rect("Carry", root);
            carryRoot.anchorMin = Vector2.zero;
            carryRoot.anchorMax = Vector2.zero;
            carryRoot.pivot = new Vector2(0.5f, 0.5f);
            carryRoot.sizeDelta = new Vector2(HotbarStyle.SlotWidth - HotbarStyle.IconInset * 2f,
                                              HotbarStyle.SlotHeight - HotbarStyle.IconInset * 2f);

            carryIcon = UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Icon", carryRoot)), null, Color.white);
            carryIcon.preserveAspect = true;
            carryIcon.raycastTarget = false;
            carryIcon.enabled = false;

            carryRoot.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------------- chips

        /// <summary>
        /// The key chips and the caption live on <see cref="WorldOverlay"/>, not on this canvas:
        /// they track world points, and that layer is the one thing in the UI built to do that.
        /// </summary>
        private void EnsureChips()
        {
            if (caption != null) return;

            WorldOverlay overlay = WorldOverlay.Create();

            for (int i = 0; i < chips.Length; i++)
            {
                var slot = (BodySlot)i;
                float width = slot == BodySlot.Back ? BackChipWidth : KeyChipWidth;

                RectTransform rect = UIBuilder.Rect("BodyChip " + slot, overlay.Layer);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(width, ChipHeight);
                UIBuilder.Sprite(rect, UITheme.ChipSprite, UITheme.Panel);

                TextMeshProUGUI text = WorldOverlay.CreateLabel(rect, "Key", ChipFontSize, width);
                text.text = KeyOf(slot);
                text.color = UITheme.Bright;
                text.fontStyle = FontStyles.Bold;

                chips[i] = new Chip { Rect = rect, HalfWidth = width * 0.5f };
                rect.gameObject.SetActive(false);
            }

            caption = WorldOverlay.CreateLabel(overlay.Layer, "BodyCaption", UITheme.CaptionSize, 460f);
            caption.color = UITheme.Muted;
            caption.enabled = false;
        }

        private void ShowChips(bool shown)
        {
            foreach (Chip chip in chips)
                if (chip != null && chip.Rect != null) chip.Rect.gameObject.SetActive(shown);

            if (caption != null && !shown) caption.enabled = false;
        }

        /// <summary>
        /// Pin each chip beside its site: outward from the body's centre for the arms — the
        /// player's LEFT arm is on the RIGHT of the screen — and above the crest for the back. The
        /// caption hangs off the hovered site's chip.
        /// </summary>
        private void PlaceChips()
        {
            if (session == null || caption == null) return;

            WorldOverlay overlay = WorldOverlay.Instance;
            float screenCentreX = overlay != null ? overlay.Layer.rect.center.x : 0f;

            for (int i = 0; i < chips.Length; i++)
            {
                Chip chip = chips[i];
                if (chip == null || chip.Rect == null) continue;

                var slot = (BodySlot)i;
                bool shown = session.TryCanvasRect(slot, out Rect r);
                chip.Rect.gameObject.SetActive(shown);
                if (!shown) continue;

                Vector2 at;
                if (slot == BodySlot.Back)
                    at = new Vector2(r.center.x, r.yMax + ChipGap + ChipHeight * 0.5f);
                else if (r.center.x < screenCentreX)
                    at = new Vector2(r.xMin - ChipGap - chip.HalfWidth, r.center.y);
                else
                    at = new Vector2(r.xMax + ChipGap + chip.HalfWidth, r.center.y);

                chip.Rect.anchoredPosition = at;

                if (hoveredSite == slot && caption.enabled)
                {
                    float y = slot == BodySlot.Back ? at.y + ChipHeight * 0.5f + 16f : at.y - ChipHeight * 0.5f - 16f;
                    caption.rectTransform.anchoredPosition = new Vector2(at.x, y);
                }
            }
        }
    }
}
```

- [ ] **Step 2: Type-check**

Run: `python3 /private/tmp/claude-501/bodyscreen/check.py rebuild runtime editor`
Expected: `0 error(s)`. (`UITheme.HeadingSize` is the existing `public const int HeadingSize = 30`.)

- [ ] **Step 3: Verify in play — read the screenshots**

Host of one, standing on open sand with the wing pack worn (it is a starting item) and nothing on the arms:
1. Press **F**. Expected: the view flies from your eyes to a spot ~2.3 m in front of you in ~0.4 s and settles level, your whole upper body in frame; the world carries on behind. Two faint blue translucent cuffs on your forearms; a faint blue frame rising past your shoulders is **not** there because the wing pack is worn — instead the wing pack's spars show past the shoulders; chips `Q`, `E` beside the arms (Q on the screen's right — your left) and `SPACE ×2` above the crest; three hotbar tiles along the bottom, the selected one lifted and amber, the selected item visibly in your hand; `BODY GEAR` top-left. **Take a screenshot and look at it.** If the cuffs do not show, check `TintMaterials.Translucent` landed the material on the copy's renderers and that the shader was found (a magenta cuff = shader missing).
2. Move the cursor over a cuff: it brightens; the caption reads `Left gauntlet  ·  Q` (or Right · E).
3. Click the item scanner in a hotbar tile (`startingBody` puts it on the body — if it is on an arm already, click that arm instead): it is carried; the origin tile hatches (or the origin cuff area goes grey with the worn scanner hidden); **both** forearms show an amber translucent scanner seated where it would sit — over the empty arm as a preview; the pack crest shows nothing new (wrong kind). Hover the crest: it tints red; click it: shake + error sound; the crest returns to normal after a quarter second.
4. Click the empty arm: the preview brightens and pops; the real scanner appears on that arm within a frame or two (host), the preview vanishes, the equip sound plays and the arm flexes (after Task 12). The chip/caption follow.
5. Click the scanner on the arm again to carry it, then click the **other** arm (a swap or a move); then click a tile to put it back in the hotbar.
6. Press **F** (or Esc): the camera flies back to your eyes in ~0.25 s; controls return; the cuffs, chips and caption are gone. Open and close **twice** in a row, and once while carrying, to prove nothing leaks (no `BodyGhost_*` objects left under the player: check the hierarchy).
7. Face a wall and press F: the lens stops short of the wall (tighter crop) instead of going through it.

Fix what the screenshots show before moving on — the two likely knobs are `BodyFocusCamera.Shot` numbers (on the player prefab) and `GearGhostBuilder`'s `BackLocalEuler` / `BackLocalPosition` if the mount frame is edge-on or below the shoulders (rebuild with the menu item after changing them).

- [ ] **Step 4: Commit** (ask first)

```bash
git add Assets/Game/Scripts/Presentation/UI/Pages/BodyInventoryUI.cs Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab
git commit -m "feat: body screen over the real character — sites, ghost previews, chips"
```

---

## Task 12: Wear feedback — the arm flexes and a clank plays on every machine

**Files:**
- Modify: `Assets/Game/Scripts/Items/Body/BodyEquipmentController.cs`

- [ ] **Step 1: Add the post-adopt flag and the celebration**

Add `using SpaceGame.Audio;` to the file's usings. Add a field next to `listening`:

```csharp
        /// <summary>
        /// True once <c>Start</c> has worn whatever the slots already held. A slot change after
        /// that is a player putting something on and is celebrated; the initial adopt — a loaded
        /// save, a late joiner's copy of a body — is not, or every spawn would clank three times.
        /// </summary>
        private bool adopted;
```

At the end of `Start()`, after the `for` loop that calls `OnSlotChanged` for every slot and before the input subscriptions, add:

```csharp
            adopted = true;
```

In `Wear(...)`, immediately after `entry.Item = item;`, add:

```csharp
            if (adopted) Celebrate(entry);
```

Add the method below `Wear`:

```csharp
        /// <summary>
        /// Something was just put on. The equip sound at the item, and — for a gauntlet — the same
        /// arm raise a Q or E press gives, through the existing latch. Runs on every machine,
        /// because it is driven by the replicated slot change and not by a message of its own: a
        /// peer sees the flex the wearer does.
        /// </summary>
        private void Celebrate(Worn entry)
        {
            if (entry.Instance != null) Sfx.Play(SfxId.WeaponEquip, entry.Instance.transform);
            entry.Raise?.Press(Time.time, continuous: false);
        }
```

- [ ] **Step 2: Type-check and verify in play**

Type-check: `0 error(s)`.

In play: open F, move a gauntlet from a tile to an arm. Expected: as the real bracer appears, a clank plays from the arm and that forearm comes up in front of the eye for about 0.6 s, then drops. Load a save with gear worn: **no** clank and no flex on load. On a real client: the host sees the client's arm flex when the client wears a gauntlet, and vice versa.

- [ ] **Step 3: Commit** (ask first)

```bash
git add Assets/Game/Scripts/Items/Body/BodyEquipmentController.cs
git commit -m "feat: newly worn gear clanks and flexes the arm, on every machine"
```

---

## Task 13: Documentation, regression and the final verification

**Files:**
- Modify: `docs/AI/systems/BodyEquipment.md`
- Modify: `docs/AI/systems/Backpack.md`
- Modify: `docs/AI/systems/Inventory.md`
- Modify: `docs/AI/systems/Environment.md` (one stale line reference — see Step 2)
- Modify: `docs/Human/the-systems.md`
- Regenerate: `docs/AI/INDEX.md`, `docs/AI/ROUTING.md` (never by hand)

- [ ] **Step 1: Update `BodyEquipment.md`**

Frontmatter: add to `paths:`
```yaml
  - Assets/Game/Scripts/Items/Body/Focus
  - Assets/Game/Prefabs/Items/Equipment/Ghosts
  - Assets/Game/Editor/Items/GearGhostBuilder.cs
  - "Assets/Game/Art/Models/_Source~/models/gear/ghost_gauntlet (from gauntlet_base.blend)"
  - "Assets/Game/Art/Models/_Source~/models/gear/ghost_mount_frame.blend"
```
add to `symptoms:`
```yaml
  - "the F screen shows my character but no ghost cuff on the empty arm"
  - "the ghost on the arm is magenta"
  - "a site on the body screen stays lit amber after I clicked it"
  - "the back placeholder is edge-on, or sits below the shoulders"
  - "an item I picked up on the body screen stayed invisible after I closed it"
  - "Q or E chips sit on the wrong arm"
  - "gear clanks and both arms fly up when I load a save"
  - "the body screen is open but I have a cursor, no controls and no camera"
```
set `updated: <today>`; add `Cutscenes` is not needed — leave `reads_with` and add `Backpack` to it (the focus camera and the tint materials are shared).

Body text:
- **Model**, replace the bullet "The hotbar is three slots ... The F screen shows six tiles ..." with:
  > **The hotbar is three slots** (`inventorySize: 3` on the networked player). **The F screen is a step-out into the world**: a spawned `BodyFocusCamera` frames the player's chest from the front (thighs up, FOV 40, ~2.3 m, level), three `BodySite`s sit on the body at the same transforms the worn items use, and the three hotbar tiles run along the bottom. An empty site shows a faint generic ghost (a cuff; a mount frame past the shoulders — never a pack, the player already wears one); while carrying, every legal site shows a translucent copy of the carried item seated where it will sit (empty) or an amber outline (swap). Design: [2026-09-02-body-screen-in-world-design.md](../../superpowers/specs/2026-09-02-body-screen-in-world-design.md).
- **Key types**: replace the `BodyInventoryUI` row's role with "The F screen's conductor: click-to-carry over three `BodySite`s and three `GearTile`s; chips and captions on `WorldOverlay`" and add rows:

  | Type | File | Role |
  | --- | --- | --- |
  | `BodyFocusSession` | [Body/Focus/BodyFocusSession.cs](Assets/Game/Scripts/Items/Body/Focus/BodyFocusSession.cs) | On `PlayerCharacter.prefab`: the shot's tunables, the ghost prefabs; owns the camera and the sites; hover/click events; commit timeout |
  | `BodyFocusCamera` | [Body/Focus/BodyFocusCamera.cs](Assets/Game/Scripts/Items/Body/Focus/BodyFocusCamera.cs) | `: FocusCamera` (Backpack.md). Chest-front shot; `LensStandoff` wall pull-in |
  | `BodySite` / `BodySite.Palette` | [Body/Focus/BodySite.cs](Assets/Game/Scripts/Items/Body/Focus/BodySite.cs) | One site: placeholder + preview ghosts (`DisplayCopy` + `TintMaterials`), worn-renderer hide/restore, outline shells, screen-space hit rect, pop/shake |
  | `BodySiteState` / `SiteState` | [Body/Focus/BodySiteState.cs](Assets/Game/Scripts/Items/Body/Focus/BodySiteState.cs) | Pure: what a site shows, with `GearMoves` as its only legality source |
  | `GearGhostBuilder` | [Editor/Items/GearGhostBuilder.cs](Assets/Game/Editor/Items/GearGhostBuilder.cs) | `Tools/SpaceGame/Items/Build Gear Ghosts`: the two ghost prefabs; adds `BodyFocusSession` to the player |

- **Flows → Move**: replace with:
  > **Move** — F opens `BodyInventoryUI` (own `InputControls` UI map; `GameplayMenuScope.Enter(freezeTime: false, hideHud: true)`; refuses with no local player, while mounted, or while typing), which calls `BodyFocusSession.Enter()`: the camera flies from the eye to the front of the chest (0.4 s), `WorldOverlay.EyeOverride` is set to it, three sites are built. Click a filled tile or site → carried (local); `SetCarry` re-resolves every site through `BodySiteState`. Click a legal target → `IBodyEquipment.RequestMove(from, to)` → `MoveServerRpc` → `GearMoves.Resolve` on the server → both lists written → slot events redraw tiles and sites. A clicked site shows `Committing` until its slot event or a 1 s timeout (then a refusal shake). Click a red site → shake + `UiError`. Click the world → the carry returns. Close → `Exit()` disposes ghosts, restores hidden renderers, camera flies back to the eye (0.25 s).
- **Multiplayer**: add a bullet: "The body screen sends nothing new. Camera, ghosts, previews and chips are local. The wear-flex and equip sound are derived from the replicated slot change on every machine (`BodyEquipmentController.Celebrate`), suppressed during the initial adopt."
- **Persistence**: add: "The body screen holds no state — it is a view over the two saved lists."
- **Gotchas**, add:
  - **Hit-testing on the body screen is screen-space rects, not colliders** — the projected bounds of what each site shows, padded 12 px. A trigger near the player's hierarchy or on a gameplay layer is a thing the movement probes, the scanner and other players' rays can hit.
  - **A ghost is seated by the PREFAB's fit component, never by anything on the copy.** `DisplayCopy.Strip` takes every MonoBehaviour off, so `BodySite` reads the `GauntletFit` / `WornFit` from the prefab and hands it to `ForearmSeat` / `BackSeat`. A gauntlet prefab with no `GauntletFit` cannot be previewed at all — the same rule `BodyEquipmentController` already enforces when wearing one.
  - **`ItemBounds.Measure` on a display copy now inverts the surface's world matrix.** A copy is
    parented to its live surface before it is measured (`DisplayCopy.Make`), so a `PackSurface`
    authored at a near-zero lossy scale would make that inverse degenerate and return garbage
    bounds. Not a shipped path — an authoring error — but it is why the `surfaceScale < 1e-6f`
    guard two lines further down exists.
  - **An outline material may not be shared between two shells.** `OutlineShell.Build` writes
    `_OutlineWidth` onto the material it is given, computed from the traced visual's own size, so
    two visuals tracing from one material both end up at whichever width was written last. Each
    `BodySite` therefore owns its three rim materials. The pack is safe for the same reason: hover
    and denied are separate materials.
  - **`OutlineShell`'s clamp bounds are `PackScale.Apply(...)` of a `const`.** That is the only
    reason moving them to another type could not change a single rim's width — a type initializer
    running at a different moment over a mutable factor would silently retune every outline in the
    game, and invisibly, since `Build` overwrites the seed before the first draw. If
    `PackScale.Factor` is ever made configurable, this is the thing that breaks.
  - **Hidden renderers are restored on every exit path.** `BodySite.Dispose` runs from `Exit`, `OnDisable` and death; the carried item's worn instance is hidden by `renderer.enabled`, locally only.
  - **`session.Commit` must be called BEFORE `RequestMove`, not after.** On a host the move RPC
    runs synchronously — NGO picks `LocalSendRpcTarget` when `IsServer`, which handles the message
    in the same call, and the `NetworkList` write raises `OnListChanged` synchronously from there.
    So with the natural-looking order the server's answer arrives and clears the pending commit
    *before* the commit is set: the site then sits showing its preview ghost over the real item for
    the whole timeout and ends with a refusal shake, at a player whose equip in fact succeeded.
    Commit-first is correct on both host (the answer clears it on its way past) and client (it stays
    lit across the round trip).
  - **The F screen refuses to open while any other gameplay menu holds the controls.**
    `PackFocusSession` guards the same way. Without it, F over a deployed pack takes
    `GameplayMenuScope` a second time — so it is not handed back until both owners exit — and spawns
    a second focus camera, and two enabled cameras at one depth render in no defined order.
  - **`Committing` is a promise with a deadline.** A refused server move announces nothing, so the site reverts and shakes after `commitTimeoutSeconds` (1 s).
  - **The back placeholder must not look like a pack.** Every player wears the expedition rig. The mount frame rises past the shoulders; if it shows edge-on or too low, fix `GearGhostBuilder.BackLocalEuler` / `BackLocalPosition` and rebuild, never the model.
  - **The chips are placed by screen side, not handedness** — the camera faces the player, so the LEFT arm is on the screen's RIGHT.
  - **A save restore reaches `Wear` AFTER `Start`, so a post-`Start` flag is not enough.**
    `BodyEquipmentSaveable.RestoreState` waits on `PlayerSaveSync`'s claim RPC, so it lands a frame
    later on the host and a full round trip on a client — after the adopt loop has finished. Worse,
    on a *peer's* machine the restore is indistinguishable from any other replicated `NetworkList`
    delta: there is no local trace of the load at all. `BodyEquipmentController` therefore gates the
    wear feedback on `adopted` **and** a short serialized settle window (`wearSettleSeconds`), which
    is a heuristic and is documented as one. An exact gate would have to put "this write was a
    restore" on the wire. Anything else that reacts to a slot change inherits this problem.
  - **Tests**: add `BodySiteStateTests`, `LensStandoffTests`, `BackSeatTests`, `ForearmSeatTests`, `DisplayCopyTests`, `FocusFlightTests` to the list.
- **Extending → Add a body slot**: append "…and give it a `BodySite` in `BodyFocusSession.Enter` with an anchor and a placeholder."
- **Two specific lines are now false** (found by the Task 4 review): around lines 66 and 74 the doc
  attributes the seating arithmetic to `WearOnForearm` and never names `ForearmSeat` / `BackSeat`.
  The controller still *wears*; the two seats now decide *where*. Re-grep both line numbers before
  editing — sibling sessions have been editing this file all day.
- **`BodySite` deliberately has no `AnchorPosition`.** The plan specified one; Task 7 dropped it
  because nothing called it, and Task 12's equip sound plays from the worn instance's own transform
  inside `BodyEquipmentController`. If a later feature wants a per-site world point, add it then —
  do not document one that does not exist.

- [ ] **Step 2: Update `Backpack.md`**

Frontmatter `paths:` add `- Assets/Game/Scripts/Presentation/Cameras` and `- Assets/Game/Scripts/Items/Equipped/DisplayCopy.cs`, `- Assets/Game/Scripts/Items/Equipped/TintMaterials.cs`, `- Assets/Game/Scripts/Items/Equipped/OutlineShell.cs`; bump `updated:`; add `BodyEquipment` to `reads_with`.

Key types: change the `PackFocusSession` / `PackFocusCamera` row's role to start "B enters focus mode. `PackFocusCamera : FocusCamera` ([Presentation/Cameras](Assets/Game/Scripts/Presentation/Cameras)) — the base owns the handover, the roll-free flight in/out, parallax and DOF; the pack authors only the shot: own camera 2.46 m past the rig, ..." (keep the rest). Add rows:

  | `FocusCamera` / `FocusFlight` | [Presentation/Cameras/](Assets/Game/Scripts/Presentation/Cameras) | Spawned focus camera base shared with the body screen; `FlightPose` blended as position + yaw + pitch, never slerped |
  | `DisplayCopy` | [Equipped/DisplayCopy.cs](Assets/Game/Scripts/Items/Equipped/DisplayCopy.cs) | Staged instantiate + `Strip`: the inert copy the mat, the wall, the holders and the body screen all draw |
  | `TintMaterials` / `OutlineShell` | [Equipped/](Assets/Game/Scripts/Items/Equipped) | `PackDragTint` materials (rim, translucent) and the outline shell tracer; `PackHandVisuals` and `BodySite` share them |

Gotchas: add these four —
- **Only one focus camera may hold the player's eye, and `FocusCamera` enforces it with a static
  `holder`.** A fly-out hands the eye back only when it LANDS, so for its whole duration the
  player's camera is off while nothing claims to be using it — and the player can act in that
  window (F again, or B). A second camera starting there would capture `enabled == false` as "the
  player's state" and faithfully restore it on exit, switching their own view and ears off for
  good; the only accidental recovery in the game is mounting and dismounting a vehicle. Taking over
  therefore dismisses the incumbent first. This is also what stops two enabled cameras sharing a
  depth, and two AudioListeners.
- **`FocusCamera` restores the player camera only if it took it over.** The old `PackFocusCamera.Dismiss` wrote the saved flag unconditionally, so an Esc inside the 0.15 s pre-handover delay left *no* enabled camera at all — a black screen. `tookOver` is the gate; `Settled` is likewise false until the handover, or a caller gating on it draws one frame at the player's eye.
- **`FlyOut` must not `StopAllCoroutines`** — a subclass is the same MonoBehaviour instance, so the flight is tracked by handle.
- **A runtime `VolumeProfile` does not clean up its own components.** `Dismiss` destroys the `DepthOfField` as well as the profile and nulls the field, because `Destroy(gameObject)` is deferred and `LateUpdate` can still run — and write to it — in the same frame.

Also fix the stale reference in **Environment.md** (see Step 2): it names `PackFocusCamera.cs:191` as one of two "stale `1000f` far-clip fallbacks"; that line is now in `FocusCamera.cs` and `PackFocusCamera.cs` has no `1000f` at all. Point it at the new file and re-check the line number.

**Note the layering wrinkle** in Backpack.md's Gotchas as a known, accepted one: `SpaceGame.Presentation.FocusCamera` reaches into `SpaceGame.Items.BackpackItemVisual.ItemLayer` for its volume layer, so the body screen's blur volume also sits on the layer named `PackItem`. It compiles because neither folder has an asmdef — the day either gets one, this is the thing that breaks (an asmdef here cannot reference Assembly-CSharp).

- [ ] **Step 2a: Fix three stale references the extractions left behind**

Each was found by a review of Tasks 2 and 3; none is optional, because each names a symbol that no
longer exists.

1. `docs/AI/systems/Backpack.md` (around line 153) refers to `BackpackItemVisual.Strip`. That method
   is now `DisplayCopy.Strip` — see the Backpack.md row work in Step 2.
2. `Assets/Game/Art/Shaders/Backpack/PackDragTint.shader` (around line 33) says "PackHandVisuals
   still scales it per item — see `OutlineWidthFor`". That symbol is now `OutlineShell.WidthFor`,
   and the class is no longer `PackHandVisuals`. Fix the sentence. (A shader comment, but it is the
   thing an agent reads first when the outline width looks wrong.)
3. `Assets/Game/Scripts/Items/Core/ItemBounds.cs` — `Measure`'s doc justifies `ActiveUnder` with
   "The backpack measures a display copy while it is still parented to a deactivated staging
   object." **That is now false**, and the obvious repair is also wrong: `HolderBuilder` does not
   call `Measure` either. The real reason `ActiveUnder` is still load-bearing is that most callers
   measure **prefab assets**, whose `activeInHierarchy` is always false — so an
   `activeInHierarchy` test would measure every one of them as zero. Rewrite the paragraph to say
   that, and keep the conclusion.

- [ ] **Step 2b: Fix the stale reference in `Environment.md`**

`docs/AI/systems/Environment.md` (around line 134) names `PackFocusCamera.cs:191` as one of two places carrying a stale `1000f` far-clip fallback. After Task 1 that line lives in `Assets/Game/Scripts/Presentation/Cameras/FocusCamera.cs`, and `PackFocusCamera.cs` has no `1000f` in it. Re-grep for the real line (`grep -n "1000f" Assets/Game/Scripts/Presentation/Cameras/FocusCamera.cs`) and correct both the file and the number. Bump `updated:`.

- [ ] **Step 3: Update `Inventory.md`**

Add a row for the two seats: `BackSeat` / `ForearmSeat` ([Equipped/](Assets/Game/Scripts/Items/Equipped)) — "Where a worn item sits: the spine from `WornFit`, the forearm from `GauntletFit`. Extracted from `BodyEquipmentController` so the body screen's ghosts seat through exactly the same arithmetic as the real thing." Bump `updated:`.

- [ ] **Step 4: Update `docs/Human/the-systems.md`**

Replace the sentence "Press F for the body screen, which lays the six slots out like a body seen from the front: back on top, an arm either side, the three hotbar slots along the bottom; click an item to pick it up and click a slot to put it there." with:

> Press F for the body screen: the camera steps out in front of you and you see your own character, with a faint ghost of a cuff on each empty forearm and a mount frame over the shoulders where the back item goes, and the three hand slots along the bottom. Pick something up and every place it can go lights up — a see-through copy of it sitting exactly where it will sit — then click there. Nothing pauses while you do it.

- [ ] **Step 5: Validate the docs**

```bash
python3 tools/docs_check.py --index
```
Expected: `INDEX.md` and `ROUTING.md` regenerated; validation passes with no errors (every new path routed, every symptom listed).

- [ ] **Step 6: Full regression**

1. EditMode suite: `PASSED=` ≥ baseline + 27 (5 flight, 4 display copy, 4 mirror, 3 back seat, 6 site state, 4 standoff, … ) and no `FAILED` entry naming a new fixture or a Backpack/Body test that passed before.
2. Pack focus (Task 1 §7) still correct after everything.
3. **The fly-out window** (this is a regression check for a real bug, not a nicety): press **F**,
   close it, and press **F** again immediately — inside the 0.25 s fly-out. Then do the same with
   **F** followed by **B**. After each, close everything and confirm the player can still see and
   hear: a black screen with only the HUD, or silence, means the hand-off in `FocusCamera` has
   regressed. Repeat the F-F case five times in a row.
4. **A real client** (ParrelSync/MPPM clone or a second machine): join a host; as the client press F — camera, ghosts, chips; wear a gauntlet from a tile; the **host** sees the client's bracer appear and the arm flex; the host wears one and the client sees it; both close cleanly. Then swap the two arms as the client while the host watches.
5. **Save → quit → load** with a gauntlet on each arm and the wing pack: after loading, F shows all three sites `Worn` with no ghosts; no clank on load.
6. Death while the screen is open (host: let a creature hit you, or `Debug` the health): the screen closes, the camera returns, no `BodyGhost_*` objects remain under the player.
7. Open the screen with the pack DEPLOYED nearby (B, walk off, F): clicking the pack's items through the body screen does nothing (sites only hit their own rects).

- [ ] **Step 7: Commit** (ask first)

```bash
git add docs/AI/systems/BodyEquipment.md docs/AI/systems/Backpack.md docs/AI/systems/Inventory.md docs/Human/the-systems.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "docs: body screen in the world — BodyEquipment, Backpack, Inventory, Human systems"
```

---

## Out of scope (do not build these)

Inspect stance, head look-at the lens, orbiting, hotbar keys while the screen is open, a dedicated equip `SfxId`, per-kind placeholders — see spec §11.

