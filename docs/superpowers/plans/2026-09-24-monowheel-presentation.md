# Monowheel Presentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the five monowheel models spin their wheels, throw sand, leave a 5 s dust cloud and smoke from their hubs whenever their root moves, as five art prefabs the Strider gameplay prefabs will later wrap.

**Architecture:**
- A pure static math class (speed estimate, spin step, rates, distance LOD, plane fit) is unit-tested in EditMode.
- A `MonowheelPresentation` MonoBehaviour applies that math every frame to measured wheel data and three world-space particle systems per wheel. It reads only the root transform, so it is identical on every machine.
- An editor builder measures each FBX's wheels from geometry, creates the particle systems and materials, saves the prefabs, and self-checks them in a preview scene.

**Tech Stack:** Unity 6000.3 (URP), C#, Shuriken `ParticleSystem`, NUnit EditMode tests, the existing `SpaceGame/Effects/JetSmoke` shader.

**Spec:** [docs/superpowers/specs/2026-09-24-monowheel-presentation-design.md](../specs/2026-09-24-monowheel-presentation-design.md). It is a sub-part of [Strider monowheels](../specs/2026-09-24-strider-monowheels-design.md) §2.1 "Presentation".

## Global Constraints

- Dust cloud lifetime **4.5–5 s**. Spray **0.5–0.8 s**. Hub smoke **2.5–3.5 s**.
- Per-wheel particle caps: **spray 60, dust 90, smoke 40** (190 per wheel).
- Starting rates per wheel: **spray 0→150/s, dust 0→18/s, smoke 2 idle→12/s** by speed fraction.
- Distance LOD: **full to 60 m, linear to 0 at 150 m**. Wheel spin is never LOD'd.
- All three layers use `simulationSpace = World`, `cullingMode = AlwaysSimulate`.
- Materials use the existing shader `SpaceGame/Effects/JetSmoke`. No new shader.
- Ground probe against `groundLayers` (default `Default` + `Ground`), ignoring the vehicle's own colliders.
- No network messages, no networked state, nothing saved (spec §6 and §7).
- Prefabs go at `Assets/Game/Prefabs/Vehicles/Monowheel/Monowheel_<Variant>.prefab`, for variants `Runner, Hauler, Patched, Double, DoubleWide`, built from `Assets/Game/Art/Models/Vehicles/Monowheel/desert_monowheel_<runner|hauler|patched|double|double_wide>.fbx`.
- Forward is Unity **+Z**. `_exportlib` maps the models' Blender −Y to +Z, so there is no yaw fix, unlike `DuneFoilBuilder`.
- Repo rules (`CLAUDE.md`):
  - No magic numbers at runtime; tunables are `[SerializeField]`.
  - No silent `catch`, no debug logs left in.
  - Every behaviour change updates its doc in the same change.
- **Commits only when the user asks.** Each task's "Commit" step is gated on that.
- **Another session (`spacegame-f7`) is building vehicle movement.** `MonowheelPresentation` owns the ring spin, and the motor must not spin rings. Before creating files in `Assets/Game/Scripts/Vehicles/Monowheel/`, check that none of the same names already exist from that session.

## Review Focus

1. **The vehicle is teleported, or placed by a load or a streaming snap.** Expect no burst of spray or dust and no wild spin on that frame. Pinned by `StepSpeed_TeleportKeepsPreviousSpeed` (Task 1). The component keeps the previous speed and moves its baseline to the new position (Task 2).
2. **The vehicle reverses.** Expect the wheels to spin backwards, and the dust to still come off at the same rate (by |speed|). Pinned by `SpinDegrees_IsSignedBySpeed` and `SpeedFraction_UsesMagnitude` (Task 1).
3. **The vehicle sits still.** Expect no spray and no dust, but a trickle of hub smoke. Pinned by `Rate_AtRestIsIdle` (Task 1) and the builder self-check's still phase (Task 3).
4. **No camera** (a dedicated server, a loading screen). Expect no exception, with LOD treated as near. Pinned by `LodFactor_NoCameraIsFull` (Task 1): the component passes `float.NaN` for "no camera".
5. **A ring mesh whose vertices are nearly collinear or degenerate.** The plane fit must not return NaN or zero, and the builder must fail loudly. Pinned by `PlaneNormal_DegenerateThrows` (Task 1).

---

## File Structure

| File | Responsibility |
| --- | --- |
| Create `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentationMath.cs` | Pure arithmetic: speed smoothing/teleport, spin step, speed fraction, rate, LOD factor, ring plane normal. |
| Create `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelWheel.cs` | Serializable per-wheel data measured by the builder (bone, signed axle, radius, contact and hub points, its three systems). |
| Create `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs` | The per-frame component: speed → spin, ground probe, emission rates, LOD. `Present(float dt)` is public so the builder can drive it in a preview scene. |
| Create `Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs` | `Tools/Vehicles/Build Monowheel Presentation`: materials, per-variant measurement, particle systems, prefab save, self-check. |
| Create `Assets/Game/Editor/Tests/MonowheelPresentationMathTests.cs` | Unit tests for the math class. |
| Create `Assets/Game/Editor/Tests/MonowheelPrefabTests.cs` | Read-back tests on the five built prefabs. |
| Create `docs/AI/systems/Monowheel.md` | System doc. |
| Modify `docs/Human/the-systems.md` | Plain-language entry. |
| Modify `docs/superpowers/specs/2026-09-24-monowheel-presentation-design.md` §5 | Menu path to the repo convention `Tools/Vehicles/…`. |

The folder `Assets/Game/Scripts/Vehicles/Monowheel/` has **no asmdef**, so it compiles into `Assembly-CSharp`, which the editor tests in `Assets/Game/Editor/Tests/` (Assembly-CSharp-Editor) can see. The Strider spec places `MonowheelMotor` in this same folder later.

---

### Task 1: `MonowheelPresentationMath` + tests

**Files:**
- Create: `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentationMath.cs`
- Test: `Assets/Game/Editor/Tests/MonowheelPresentationMathTests.cs`

**Interfaces:**
- Produces (namespace `SpaceGame.Vehicles.Monowheel`, `public static class MonowheelPresentationMath`):
  - `float StepSpeed(float smoothed, Vector3 from, Vector3 to, Vector3 forward, float dt, float smoothing, float maxPlausibleSpeed, out bool teleported)`
  - `float SpinDegrees(float speed, float radius, float dt)`
  - `float SpeedFraction(float speed, float fullSpeed)`
  - `float Rate(float idle, float max, float fraction)`
  - `float LodFactor(float distance, float near, float far)`: `NaN` distance means no camera and returns 1
  - `Vector3 PlaneNormal(IReadOnlyList<Vector3> points)`: throws `ArgumentException` on fewer than 3 points or a degenerate spread

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Editor/Tests/MonowheelPresentationMathTests.cs
//
// The monowheel's presentation arithmetic (spec §2, §4). Pure, because every interesting case —
// a teleport, a reverse, a standstill, no camera — is a single frame that a MonoBehaviour test
// would need a scene, a camera and a physics step to reach.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Vehicles.Monowheel;
using UnityEngine;
using M = SpaceGame.Vehicles.Monowheel.MonowheelPresentationMath;

namespace SpaceGame.EditorTools
{
    public class MonowheelPresentationMathTests
    {
        private const float Dt = 1f / 60f;

        [Test]
        public void StepSpeed_UnsmoothedIsForwardDistanceOverTime()
        {
            float s = M.StepSpeed(0f, Vector3.zero, new Vector3(0f, 0f, 0.2f), Vector3.forward,
                                  Dt, 0f, 50f, out bool teleported);
            Assert.AreEqual(12f, s, 1e-3f);
            Assert.IsFalse(teleported);
        }

        [Test]
        public void StepSpeed_IgnoresSidewaysMotion()
        {
            float s = M.StepSpeed(0f, Vector3.zero, new Vector3(0.2f, 0f, 0f), Vector3.forward,
                                  Dt, 0f, 50f, out _);
            Assert.AreEqual(0f, s, 1e-4f);
        }

        [Test]
        public void StepSpeed_SmoothingMovesPartWayTowardRaw()
        {
            float s = M.StepSpeed(0f, Vector3.zero, new Vector3(0f, 0f, 0.2f), Vector3.forward,
                                  Dt, 0.15f, 50f, out _);
            Assert.Greater(s, 0f);
            Assert.Less(s, 12f);
        }

        [Test]
        public void StepSpeed_TeleportKeepsPreviousSpeed()
        {
            float s = M.StepSpeed(3f, Vector3.zero, new Vector3(0f, 0f, 400f), Vector3.forward,
                                  Dt, 0.15f, 50f, out bool teleported);
            Assert.IsTrue(teleported);
            Assert.AreEqual(3f, s, 1e-5f, "a snap must not read as 24 km/s");
        }

        [Test]
        public void StepSpeed_AHitchAtTopSpeedIsStillDriving()
        {
            // 5 m in a 250 ms hitch is 20 m/s — a single's top speed, not a teleport. A fixed
            // distance threshold would have called this a snap (Strider session's catch).
            M.StepSpeed(20f, Vector3.zero, new Vector3(0f, 0f, 5f), Vector3.forward,
                        0.25f, 0.15f, 50f, out bool teleported);
            Assert.IsFalse(teleported);
        }

        [Test]
        public void StepSpeed_ZeroDtIsNoChange()
        {
            Assert.AreEqual(4f, M.StepSpeed(4f, Vector3.zero, Vector3.forward, Vector3.forward,
                                            0f, 0.15f, 50f, out _));
        }

        [Test]
        public void SpinDegrees_IsSpeedOverRadius()
        {
            // 1.965 m paddle radius at 19.65 m/s is 10 rad/s.
            Assert.AreEqual(10f * Mathf.Rad2Deg * Dt, M.SpinDegrees(19.65f, 1.965f, Dt), 1e-3f);
        }

        [Test]
        public void SpinDegrees_IsSignedBySpeed()
        {
            Assert.Less(M.SpinDegrees(-5f, 1.965f, Dt), 0f);
        }

        [Test]
        public void SpinDegrees_ZeroRadiusIsZero()
        {
            Assert.AreEqual(0f, M.SpinDegrees(10f, 0f, Dt));
        }

        [Test]
        public void SpeedFraction_UsesMagnitudeAndClamps()
        {
            Assert.AreEqual(0.5f, M.SpeedFraction(-10f, 20f), 1e-5f);
            Assert.AreEqual(1f, M.SpeedFraction(50f, 20f));
            Assert.AreEqual(0f, M.SpeedFraction(5f, 0f));
        }

        [Test]
        public void Rate_AtRestIsIdle()
        {
            Assert.AreEqual(2f, M.Rate(2f, 12f, 0f));
            Assert.AreEqual(12f, M.Rate(2f, 12f, 1f));
        }

        [Test]
        public void LodFactor_FullNearZeroFarLinearBetween()
        {
            Assert.AreEqual(1f, M.LodFactor(10f, 60f, 150f));
            Assert.AreEqual(0.5f, M.LodFactor(105f, 60f, 150f), 1e-5f);
            Assert.AreEqual(0f, M.LodFactor(500f, 60f, 150f));
        }

        [Test]
        public void LodFactor_NoCameraIsFull()
        {
            Assert.AreEqual(1f, M.LodFactor(float.NaN, 60f, 150f));
        }

        [Test]
        public void PlaneNormal_OfATiltedRingIsItsAxle()
        {
            // A ring of radius 1.7 in the YZ plane, cambered 20 degrees about Z.
            Quaternion camber = Quaternion.AngleAxis(20f, Vector3.forward);
            var pts = new List<Vector3>();
            for (int i = 0; i < 72; i++)
            {
                float a = i * Mathf.PI * 2f / 72f;
                pts.Add(camber * new Vector3(0f, Mathf.Cos(a) * 1.7f, Mathf.Sin(a) * 1.7f));
            }
            Vector3 n = M.PlaneNormal(pts);
            Assert.Greater(Mathf.Abs(Vector3.Dot(n, camber * Vector3.right)), 0.999f);
            Assert.AreEqual(1f, n.magnitude, 1e-4f);
        }

        [Test]
        public void PlaneNormal_DegenerateThrows()
        {
            var line = new List<Vector3> { Vector3.zero, Vector3.up, Vector3.up * 2f, Vector3.up * 3f };
            Assert.Throws<ArgumentException>(() => M.PlaneNormal(line));
            Assert.Throws<ArgumentException>(() => M.PlaneNormal(new List<Vector3> { Vector3.zero }));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail.** Compile check: `python tools/typecheck.py --editor`. Expected: errors like `The type or namespace name 'Monowheel' does not exist in the namespace 'SpaceGame.Vehicles'`.

- [ ] **Step 3: Write the implementation**

```csharp
// Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentationMath.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    /// <summary>
    /// The arithmetic behind a monowheel's presentation (spec §2 and §4): how fast it is going
    /// judged from its own transform, how far each ring turns for that, how much sand and smoke that
    /// is worth, how much of it the camera's distance allows, and which way a ring's axle points.
    /// Pure, so every one-frame edge case — a teleport, a reverse, no camera — is a unit test.
    /// </summary>
    public static class MonowheelPresentationMath
    {
        /// <summary>
        /// Smoothed signed ground speed along <paramref name="forward"/>. A step implying more than
        /// <paramref name="maxPlausibleSpeed"/> is a snap (a save restore, a streaming migrate, a
        /// NetworkTransform correction), not motion: the previous speed is kept and
        /// <paramref name="teleported"/> says so. Judged by implied SPEED, not distance, so a long
        /// frame hitch at top speed still reads as driving.
        /// </summary>
        public static float StepSpeed(float smoothed, Vector3 from, Vector3 to, Vector3 forward,
                                      float dt, float smoothing, float maxPlausibleSpeed,
                                      out bool teleported)
        {
            teleported = false;
            if (dt <= 0f) return smoothed;

            Vector3 step = to - from;
            if (step.magnitude > maxPlausibleSpeed * dt)
            {
                teleported = true;
                return smoothed;
            }

            float raw = Vector3.Dot(step, forward.normalized) / dt;
            if (smoothing <= 0f) return raw;
            return Mathf.Lerp(smoothed, raw, 1f - Mathf.Exp(-dt / smoothing));
        }

        /// <summary>Degrees a ring of <paramref name="radius"/> turns in <paramref name="dt"/> rolling at <paramref name="speed"/>.</summary>
        public static float SpinDegrees(float speed, float radius, float dt) =>
            radius <= 0f ? 0f : speed / radius * dt * Mathf.Rad2Deg;

        /// <summary>|speed| as a fraction of <paramref name="fullSpeed"/>, clamped to [0, 1]. Reversing throws sand too.</summary>
        public static float SpeedFraction(float speed, float fullSpeed) =>
            fullSpeed <= 0f ? 0f : Mathf.Clamp01(Mathf.Abs(speed) / fullSpeed);

        /// <summary>Emission rate between <paramref name="idle"/> (standing) and <paramref name="max"/> (full speed).</summary>
        public static float Rate(float idle, float max, float fraction) => Mathf.Lerp(idle, max, fraction);

        /// <summary>
        /// 1 up to <paramref name="near"/>, 0 from <paramref name="far"/>, linear between. A NaN
        /// distance means there is no camera to be far from (a server, a loading screen): full.
        /// </summary>
        public static float LodFactor(float distance, float near, float far)
        {
            if (float.IsNaN(distance) || distance <= near) return 1f;
            if (distance >= far) return 0f;
            return 1f - (distance - near) / (far - near);
        }

        /// <summary>
        /// Unit normal of the best-fit plane through <paramref name="points"/> — for a ring mesh,
        /// its axle. The smallest-variance direction of the points' covariance, found by power
        /// iteration on (trace·I − C). Throws if the points do not span a plane.
        /// </summary>
        public static Vector3 PlaneNormal(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count < 3)
                throw new ArgumentException("A plane needs at least three points.", nameof(points));

            Vector3 c = Vector3.zero;
            foreach (Vector3 p in points) c += p;
            c /= points.Count;

            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (Vector3 p in points)
            {
                Vector3 d = p - c;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
                yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }

            float trace = xx + yy + zz;
            // Two of the three variances must be real for the points to be a plane, not a line.
            float[] v = { xx, yy, zz };
            Array.Sort(v);
            if (trace <= 1e-9f || v[1] <= trace * 1e-4f)
                throw new ArgumentException("The points are collinear or coincident; no plane.", nameof(points));

            // Power iteration on B = trace·I − C: B's largest eigenvector is C's smallest.
            Vector3 n = new Vector3(0.577f, 0.577f, 0.577f);
            for (int i = 0; i < 64; i++)
            {
                Vector3 b = new Vector3(
                    (trace - xx) * n.x - xy * n.y - xz * n.z,
                    -xy * n.x + (trace - yy) * n.y - yz * n.z,
                    -xz * n.x - yz * n.y + (trace - zz) * n.z);
                n = b.normalized;
            }
            return n;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass.**
  - Compile: `python tools/typecheck.py --editor` should report no errors.
  - Run: Unity Test Runner, EditMode, filter `MonowheelPresentationMathTests`. That's either MCP `run_tests` (mode EditMode, test filter `SpaceGame.EditorTools.MonowheelPresentationMathTests`) or the Test Runner window.
  - Expected: all 14 pass.
  - Before running tests, check for dirty scenes and ask the user to save (see the Unity MCP workflow memory: a test run can drop unsaved scene edits).

- [ ] **Step 5: Commit** (only if the user has asked for commits)

```bash
git add Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentationMath.cs* Assets/Game/Editor/Tests/MonowheelPresentationMathTests.cs*
git commit -m "feat(monowheel): presentation math — speed, spin, rates, LOD, ring axle"
```

---

### Task 2: `MonowheelWheel` + `MonowheelPresentation`

**Files:**
- Create: `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelWheel.cs`
- Create: `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs`

**Interfaces:**
- Consumes: `MonowheelPresentationMath` (Task 1).
- Produces:
  - `[Serializable] public sealed class MonowheelWheel` with public fields `Transform ringBone; Vector3 localAxle; float paddleRadius; Vector3 localContact; Vector3 localHub; ParticleSystem spray; ParticleSystem dust; ParticleSystem smoke;`
  - `public sealed class MonowheelPresentation : MonoBehaviour`, with:
    - `public void Configure(MonowheelWheel[] wheels)` (builder only)
    - `public IReadOnlyList<MonowheelWheel> Wheels { get; }`
    - `public float Speed { get; }`
    - `public void Present(float dt)` and `public void Present(float dt, float cameraDistance)` (NaN = no camera)
    - `public void ResetBaseline()`
    - serialized tunables, listed in the code below

The component has no pure logic of its own to unit-test: every decision is a Task 1 function. Its behaviour is exercised end to end by the builder self-check (Task 3) and the prefab tests (Task 4).

- [ ] **Step 1: Write `MonowheelWheel.cs`**

```csharp
// Assets/Game/Scripts/Vehicles/Monowheel/MonowheelWheel.cs
using System;
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    /// <summary>
    /// One wheel of a monowheel, as MEASURED by MonowheelPresentationBuilder from the ring's
    /// geometry — never assumed, because FBX import re-orients bones and the doubles are cambered.
    /// Points are in the vehicle root's local space (the ring bone spins, so it cannot hold them).
    /// </summary>
    [Serializable]
    public sealed class MonowheelWheel
    {
        [Tooltip("The Bone_Ring* transform that spins. Its paddles and mounts ride under it.")]
        public Transform ringBone;

        [Tooltip("Axle in the ring bone's local space, signed so a positive turn rolls the vehicle forward.")]
        public Vector3 localAxle = Vector3.right;

        [Tooltip("Metres from hub to paddle tip: the radius the wheel rolls on.")]
        public float paddleRadius = 1.965f;

        [Tooltip("Where the paddles meet the ground, in root space. Spray and dust are born here.")]
        public Vector3 localContact;

        [Tooltip("The hub, in root space. Smoke rises from here.")]
        public Vector3 localHub;

        public ParticleSystem spray;
        public ParticleSystem dust;
        public ParticleSystem smoke;
    }
}
```

- [ ] **Step 2: Write `MonowheelPresentation.cs`**

```csharp
// Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    /// <summary>
    /// Spins a monowheel's rings and makes them throw sand, lay a lingering dust cloud and smoke
    /// from their hubs — from nothing but how the root actually moves (spec §2). It runs on every
    /// machine and reads only the transform it already sees, so a client watching a replicated
    /// monowheel, a player driving one and an NPC driving one all present the same way, with no
    /// message and no saved state. The Strider MonowheelMotor's "presentation" is this component.
    ///
    /// <para>The sand is feedback on a real event — the paddles biting the ground
    /// (GDC-L1-FEEL-0004) — so it follows actual speed and actual ground contact. The dust is the
    /// expensive part, so every wheel is capped and the effect fades with camera distance
    /// (GDC-L1-TECH-0002, GDC-L1-PERF-0004).</para>
    /// </summary>
    public sealed class MonowheelPresentation : MonoBehaviour
    {
        [Tooltip("Measured by MonowheelPresentationBuilder. One entry per ring.")]
        [SerializeField] private MonowheelWheel[] wheels = new MonowheelWheel[0];

        [Header("Speed")]
        [Tooltip("Ground speed (m/s) at which sand and smoke reach their maximum.")]
        [SerializeField] private float fullSpeed = 20f;
        [Tooltip("Seconds over which speed is smoothed, so a jittery replicated transform does not flicker the effects.")]
        [SerializeField] private float speedSmoothing = 0.15f;
        [Tooltip("A frame's move implying more than this speed (m/s) is a snap, not motion. About twice the fastest monowheel's top speed.")]
        [SerializeField] private float maxPlausibleSpeed = 50f;

        [Header("Ground")]
        [Tooltip("What counts as ground for the paddles to throw.")]
        [SerializeField] private LayerMask groundLayers = 1 << 0;   // Builder sets Default + Ground.
        [Tooltip("How far below the contact point (m) ground still counts as touching.")]
        [SerializeField] private float groundProbe = 0.6f;

        [Header("Emission per wheel (particles/s)")]
        [SerializeField] private float sprayAtFullSpeed = 150f;
        [SerializeField] private float dustAtFullSpeed = 18f;
        [SerializeField] private float smokeIdle = 2f;
        [SerializeField] private float smokeAtFullSpeed = 12f;

        [Header("Distance LOD")]
        [Tooltip("Full effects up to this camera distance (m).")]
        [SerializeField] private float lodNear = 60f;
        [Tooltip("No sand, dust or smoke beyond this camera distance (m). Spin never stops.")]
        [SerializeField] private float lodFar = 150f;

        private readonly RaycastHit[] hits = new RaycastHit[8];
        private Vector3 lastPosition;
        private float speed;

        public IReadOnlyList<MonowheelWheel> Wheels => wheels;
        public float Speed => speed;

        /// <summary>Builder only: install the measured wheels.</summary>
        public void Configure(MonowheelWheel[] measured)
        {
            wheels = measured;
            groundLayers = LayerMask.GetMask("Default", "Ground");
        }

        /// <summary>Forget the last position — after a spawn, a load or any snap into place.</summary>
        public void ResetBaseline()
        {
            lastPosition = transform.position;
            speed = 0f;
        }

        private void OnEnable() => ResetBaseline();

        private void Update() => Present(Time.deltaTime);

        /// <summary>One frame of presentation, LOD'd against the main camera.</summary>
        public void Present(float dt) => Present(dt, CameraDistance(transform.position));

        /// <summary>
        /// One frame at a given camera distance (<c>float.NaN</c> = no camera, full effect). The
        /// builder's self-check passes NaN: in the editor, Camera.main may be a scene camera far
        /// away, and the LOD would silence the very effects the check is looking for.
        /// </summary>
        public void Present(float dt, float cameraDistance)
        {
            if (dt <= 0f) return;

            Vector3 position = transform.position;
            speed = MonowheelPresentationMath.StepSpeed(speed, lastPosition, position, transform.forward,
                                                        dt, speedSmoothing, maxPlausibleSpeed, out _);
            lastPosition = position;

            float fraction = MonowheelPresentationMath.SpeedFraction(speed, fullSpeed);
            float lod = MonowheelPresentationMath.LodFactor(cameraDistance, lodNear, lodFar);

            foreach (MonowheelWheel wheel in wheels)
            {
                wheel.ringBone.Rotate(wheel.localAxle,
                                      MonowheelPresentationMath.SpinDegrees(speed, wheel.paddleRadius, dt),
                                      Space.Self);

                bool grounded = Touching(transform.TransformPoint(wheel.localContact));
                float ground = grounded ? lod : 0f;
                SetRate(wheel.spray, MonowheelPresentationMath.Rate(0f, sprayAtFullSpeed, fraction) * ground);
                SetRate(wheel.dust, MonowheelPresentationMath.Rate(0f, dustAtFullSpeed, fraction) * ground);
                SetRate(wheel.smoke, MonowheelPresentationMath.Rate(smokeIdle, smokeAtFullSpeed, fraction) * lod);
            }
        }

        private float CameraDistance(Vector3 position)
        {
            Camera cam = Camera.main;
            return cam == null ? float.NaN : Vector3.Distance(cam.transform.position, position);
        }

        // Probed through the scene's OWN physics scene, so the builder's preview-scene check and a
        // live world use the same code path. The vehicle's own colliders (the Strider prefab adds
        // them) are skipped rather than excluded by layer, so no layer has to be reserved for it.
        private bool Touching(Vector3 contact)
        {
            Vector3 up = transform.up;
            int n = gameObject.scene.GetPhysicsScene().Raycast(contact + up * groundProbe, -up, hits,
                                                               groundProbe * 2f, groundLayers,
                                                               QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!hits[i].collider.transform.IsChildOf(transform)) return true;
            return false;
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = rate;
        }
    }
}
```

- [ ] **Step 3: Compile.** Run `python tools/typecheck.py`. Expected: `Assembly-CSharp: no errors.`

- [ ] **Step 4: Commit** (only if the user has asked for commits)

```bash
git add Assets/Game/Scripts/Vehicles/Monowheel/MonowheelWheel.cs* Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs*
git commit -m "feat(monowheel): MonowheelPresentation — spin, ground-probed sand, hub smoke, LOD"
```

---

### Task 3: `MonowheelPresentationBuilder` (materials, measurement, systems, prefabs, self-check)

**Files:**
- Create: `Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs`

**Interfaces:**
- Consumes:
  - `MonowheelPresentationMath.PlaneNormal` (Task 1)
  - `MonowheelWheel` and `MonowheelPresentation.Configure/Present/ResetBaseline/Wheels` (Task 2)
- Produces:
  - Menu item `Tools/Vehicles/Build Monowheel Presentation`
  - `public static void BuildAll()`
  - `public static readonly (string variant, string fbx, int rings)[] Variants`, which Task 4's tests read
  - Prefabs at `Assets/Game/Prefabs/Vehicles/Monowheel/Monowheel_<variant>.prefab`
  - Materials `Assets/Game/Art/Materials/Vehicles/MonowheelDust.mat` and `MonowheelHubSmoke.mat`

- [ ] **Step 1: Write the builder**

```csharp
// Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs
//
// Builds the five monowheel ART prefabs (spec §5): the model, MonowheelPresentation with its wheels
// measured from geometry, and three world-space particle systems per wheel. The Strider gameplay
// prefabs (StriderMonowheelBuilder, Strider spec §2.3) wrap these; they never rebuild the effects.
//
// Everything geometric is measured, because the FBX importer re-orients bones and the doubles are
// cambered 7° and 20°: the axle is the ring mesh's plane normal, the radius the paddles' reach.
//
// Re-run from: Tools ▸ Vehicles ▸ Build Monowheel Presentation
using System.Collections.Generic;
using SpaceGame.Vehicles.Monowheel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public static class MonowheelPresentationBuilder
    {
        public static readonly (string variant, string fbx, int rings)[] Variants =
        {
            ("Runner", "desert_monowheel_runner", 1),
            ("Hauler", "desert_monowheel_hauler", 1),
            ("Patched", "desert_monowheel_patched", 1),
            ("Double", "desert_monowheel_double", 2),
            ("DoubleWide", "desert_monowheel_double_wide", 2),
        };

        public const string ModelFolder = "Assets/Game/Art/Models/Vehicles/Monowheel";
        public const string PrefabFolder = "Assets/Game/Prefabs/Vehicles/Monowheel";
        private const string MaterialFolder = "Assets/Game/Art/Materials/Vehicles";
        private const string DustMaterialPath = MaterialFolder + "/MonowheelDust.mat";
        private const string SmokeMaterialPath = MaterialFolder + "/MonowheelHubSmoke.mat";
        private const string SmokeShader = "SpaceGame/Effects/JetSmoke";

        // The build recipe for the three layers (spec §3). Runtime rates live on the component.
        private static readonly Color SandTint = new Color(0.78f, 0.66f, 0.47f, 1f);
        private static readonly Color SmokeTint = new Color(0.36f, 0.33f, 0.30f, 1f);
        public const int SprayCap = 60, DustCap = 90, SmokeCap = 40;

        public static string PrefabPath(string variant) => $"{PrefabFolder}/Monowheel_{variant}.prefab";

        [MenuItem("Tools/Vehicles/Build Monowheel Presentation")]
        public static void BuildAll()
        {
            Material dust = SmokeMaterial(DustMaterialPath, SandTint);
            Material smoke = SmokeMaterial(SmokeMaterialPath, SmokeTint);
            foreach (var (variant, fbx, rings) in Variants)
            {
                string path = Build(variant, $"{ModelFolder}/{fbx}.fbx", rings, dust, smoke);
                SelfCheck(path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Monowheel] Built and verified {Variants.Length} presentation prefabs in {PrefabFolder}.");
        }

        // ── materials ───────────────────────────────────────────────────────────

        // A script-created ParticleSystem with no material draws NOTHING, silently (Jetpack gotcha),
        // so both are created here and a missing shader is an error, not an empty field.
        private static Material SmokeMaterial(string path, Color tint)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find(SmokeShader);
                if (shader == null)
                    throw new System.InvalidOperationException($"Shader '{SmokeShader}' not found — the monowheel dust would draw nothing.");
                System.IO.Directory.CreateDirectory(MaterialFolder);
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_Color", tint);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ── one prefab ──────────────────────────────────────────────────────────

        private static string Build(string variant, string fbxPath, int ringCount, Material dust, Material smoke)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) throw new System.IO.FileNotFoundException("Monowheel model missing", fbxPath);

            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                root.name = $"Monowheel_{variant}";
                var measured = new List<MonowheelWheel>();
                foreach (Transform bone in FindRingBones(root.transform))
                {
                    MonowheelWheel wheel = Measure(root.transform, bone);
                    string side = bone.name.Substring("Bone_Ring".Length);   // "", "L" or "R"
                    wheel.spray = Spray(root.transform, $"FX_Spray{side}", wheel.localContact);
                    wheel.dust = Dust(root.transform, $"FX_Dust{side}", wheel.localContact, dust);
                    wheel.smoke = Smoke(root.transform, $"FX_HubSmoke{side}", wheel.localHub, smoke);
                    Renderer(wheel.spray, dust);
                    measured.Add(wheel);
                }
                if (measured.Count != ringCount)
                    throw new System.InvalidOperationException($"{variant}: found {measured.Count} ring bone(s), expected {ringCount}.");

                root.AddComponent<MonowheelPresentation>().Configure(measured.ToArray());

                System.IO.Directory.CreateDirectory(PrefabFolder);
                string path = PrefabPath(variant);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return path;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static IEnumerable<Transform> FindRingBones(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Bone_Ring" || t.name == "Bone_RingL" || t.name == "Bone_RingR")
                    yield return t;
        }

        // The ring is the Mesh_Ring* directly under the bone; the paddles and mounts are under it.
        private static MonowheelWheel Measure(Transform root, Transform bone)
        {
            MeshFilter ring = null;
            foreach (Transform child in bone)
                if (child.name.StartsWith("Mesh_Ring") && child.TryGetComponent(out MeshFilter mf)) ring = mf;
            if (ring == null) throw new System.InvalidOperationException($"No Mesh_Ring* under {bone.name}.");

            var bandPoints = new List<Vector3>();
            foreach (Vector3 v in ring.sharedMesh.vertices) bandPoints.Add(ring.transform.TransformPoint(v));
            Vector3 axle = MonowheelPresentationMath.PlaneNormal(bandPoints);
            Vector3 hub = Vector3.zero;
            foreach (Vector3 p in bandPoints) hub += p;
            hub /= bandPoints.Count;

            // The rolling radius is the paddles' reach, measured perpendicular to the axle.
            float radius = 0f;
            foreach (MeshFilter part in ring.GetComponentsInChildren<MeshFilter>(true))
                foreach (Vector3 v in part.sharedMesh.vertices)
                {
                    Vector3 d = part.transform.TransformPoint(v) - hub;
                    radius = Mathf.Max(radius, (d - axle * Vector3.Dot(d, axle)).magnitude);
                }

            // The lowest point of the (possibly cambered) wheel: down, projected into the ring's plane.
            Vector3 down = -root.up;
            Vector3 inPlaneDown = (down - axle * Vector3.Dot(down, axle)).normalized;
            Vector3 contact = hub + inPlaneDown * radius;

            // Sign the axle so a positive turn moves the bottom of the wheel BACKWARDS — that is
            // rolling forward. Decided numerically, so Unity's handedness is never guessed at.
            Vector3 r = contact - hub;
            Vector3 moved = Quaternion.AngleAxis(1f, axle) * r - r;
            if (Vector3.Dot(moved, root.forward) > 0f) axle = -axle;

            return new MonowheelWheel
            {
                ringBone = bone,
                localAxle = bone.InverseTransformDirection(axle).normalized,
                paddleRadius = radius,
                localContact = root.InverseTransformPoint(contact),
                localHub = root.InverseTransformPoint(hub),
            };
        }

        // ── the three layers (spec §3) ──────────────────────────────────────────

        private static ParticleSystem NewSystem(Transform root, string name, Vector3 localPos,
                                                Quaternion localRot, int cap, float minLife, float maxLife)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = cap;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;   // MonowheelPresentation drives it
            return ps;
        }

        private static ParticleSystem Spray(Transform root, string name, Vector3 contact)
        {
            // Thrown up and back off the paddles.
            Quaternion aim = Quaternion.LookRotation(new Vector3(0f, 0.75f, -0.66f));
            ParticleSystem ps = NewSystem(root, name, contact, aim, SprayCap, 0.5f, 0.8f);
            ParticleSystem.MainModule main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new Color(SandTint.r, SandTint.g, SandTint.b, 0.9f);
            main.gravityModifier = 1f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.25f;
            FadeOut(ps, 0.9f);
            return ps;
        }

        private static ParticleSystem Dust(Transform root, string name, Vector3 contact, Material mat)
        {
            ParticleSystem ps = NewSystem(root, name, contact, Quaternion.identity, DustCap, 4.5f, 5f);
            ParticleSystem.MainModule main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startColor = new Color(SandTint.r, SandTint.g, SandTint.b, 0.45f);
            main.gravityModifier = -0.02f;   // hangs, drifting up a little
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.8f, 0.2f, 0.8f);
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 2.6f));
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 0.6f;
            FadeOut(ps, 0.45f);
            Renderer(ps, mat);
            return ps;
        }

        private static ParticleSystem Smoke(Transform root, string name, Vector3 hub, Material mat)
        {
            ParticleSystem ps = NewSystem(root, name, hub, Quaternion.LookRotation(Vector3.up), SmokeCap, 2.5f, 3.5f);
            ParticleSystem.MainModule main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startColor = new Color(SmokeTint.r, SmokeTint.g, SmokeTint.b, 0.5f);
            main.gravityModifier = -0.05f;   // rises
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 3f));
            FadeOut(ps, 0.5f);
            Renderer(ps, mat);
            return ps;
        }

        private static void FadeOut(ParticleSystem ps, float startAlpha)
        {
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(startAlpha * 0.6f, 0.5f),
                              new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        private static void Renderer(ParticleSystem ps, Material mat)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ── self-check (spec §5) ────────────────────────────────────────────────

        // Drives each built prefab over a ground plane in a PREVIEW scene: standing still must
        // smoke but throw nothing; moving at speed must spin every ring (about its own hub) and
        // emit all three layers. Fails loudly — the jetpack once shipped smoke that never drew.
        private static void SelfCheck(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var presentation = go.GetComponent<MonowheelPresentation>();
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.layer = LayerMask.NameToLayer("Ground");
                float lowest = float.MaxValue;
                foreach (MonowheelWheel w in presentation.Wheels)
                    lowest = Mathf.Min(lowest, go.transform.TransformPoint(w.localContact).y);
                ground.transform.position = new Vector3(0f, lowest - 0.5f + 0.05f, 0f);
                ground.transform.localScale = new Vector3(40f, 1f, 400f);
                Physics.SyncTransforms();

                const float dt = 1f / 60f;
                presentation.ResetBaseline();
                Step(presentation, 60, dt, Vector3.zero);   // 1 s: the 2/s idle smoke must show
                foreach (MonowheelWheel w in presentation.Wheels)
                {
                    Require(w.spray.particleCount == 0, prefabPath, "throws sand while standing still");
                    Require(w.smoke.particleCount > 0, prefabPath, "has no idle hub smoke");
                }

                var hubsBefore = new List<Vector3>();
                var rotBefore = new List<Quaternion>();
                foreach (MonowheelWheel w in presentation.Wheels)
                {
                    hubsBefore.Add(RingCentre(w));
                    rotBefore.Add(w.ringBone.localRotation);
                }
                Step(presentation, 60, dt, go.transform.forward * (18f * dt));   // 18 m/s for 1 s
                for (int i = 0; i < presentation.Wheels.Count; i++)
                {
                    MonowheelWheel w = presentation.Wheels[i];
                    Require(Quaternion.Angle(rotBefore[i], w.ringBone.localRotation) > 1f, prefabPath, $"{w.ringBone.name} did not spin");
                    Vector3 drift = RingCentre(w) - hubsBefore[i] - go.transform.forward * 18f;
                    Require(drift.magnitude < 0.02f, prefabPath, $"{w.ringBone.name} orbits instead of spinning ({drift.magnitude:0.000} m)");
                    Require(w.spray.particleCount > 0 && w.dust.particleCount > 0, prefabPath, $"{w.ringBone.name} threw no sand at speed");
                    Require(w.spray.GetComponent<ParticleSystemRenderer>().sharedMaterial != null, prefabPath, "spray has no material");
                }
                Debug.Log($"[Monowheel] Verified {prefab.name}: {presentation.Wheels.Count} wheel(s) spin in place and throw sand.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void Step(MonowheelPresentation p, int frames, float dt, Vector3 perFrame)
        {
            for (int f = 0; f < frames; f++)
            {
                p.transform.position += perFrame;
                Physics.SyncTransforms();
                p.Present(dt, float.NaN);   // full effect: no editor camera may LOD it away
                foreach (MonowheelWheel w in p.Wheels)
                {
                    w.spray.Simulate(dt, false, false, false);
                    w.dust.Simulate(dt, false, false, false);
                    w.smoke.Simulate(dt, false, false, false);
                }
            }
        }

        private static Vector3 RingCentre(MonowheelWheel w)
        {
            MeshFilter ring = null;
            foreach (Transform child in w.ringBone)
                if (child.name.StartsWith("Mesh_Ring") && child.TryGetComponent(out MeshFilter mf)) ring = mf;
            Vector3 sum = Vector3.zero;
            Vector3[] verts = ring.sharedMesh.vertices;
            foreach (Vector3 v in verts) sum += ring.transform.TransformPoint(v);
            return sum / verts.Length;
        }

        private static void Require(bool ok, string prefab, string what)
        {
            if (!ok) throw new System.InvalidOperationException($"[Monowheel] {prefab}: {what}.");
        }
    }
}
```

- [ ] **Step 2: Compile.** Run `python tools/typecheck.py --editor`. Expected: no errors in Assembly-CSharp or Assembly-CSharp-Editor.

- [ ] **Step 3: Run the builder.**
  - Unity MCP `execute_code`: `UnityEditor.EditorApplication.ExecuteMenuItem("Tools/Vehicles/Build Monowheel Presentation");`
  - Or have the user click **Tools ▸ Vehicles ▸ Build Monowheel Presentation**.
  - Expected console output: five `[Monowheel] Verified Monowheel_<Variant>` lines, then `Built and verified 5 presentation prefabs`, with no errors.
  - If a `Require` throws, fix the cause (usually measurement) and re-run. Do not loosen the check.

- [ ] **Step 4: Commit** (only if the user has asked for commits)

```bash
git add Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs* Assets/Game/Prefabs/Vehicles/Monowheel Assets/Game/Prefabs/Vehicles/Monowheel.meta Assets/Game/Art/Materials/Vehicles/MonowheelDust.mat* Assets/Game/Art/Materials/Vehicles/MonowheelHubSmoke.mat*
git commit -m "feat(monowheel): presentation builder — measured wheels, sand/dust/smoke, five art prefabs"
```

---

### Task 4: Prefab read-back tests

**Files:**
- Test: `Assets/Game/Editor/Tests/MonowheelPrefabTests.cs`

**Interfaces:**
- Consumes:
  - `MonowheelPresentationBuilder.Variants`, `PrefabPath(string)`, `SprayCap/DustCap/SmokeCap` (Task 3)
  - `MonowheelPresentation.Wheels` (Task 2)

- [ ] **Step 1: Write the tests**

```csharp
// Assets/Game/Editor/Tests/MonowheelPrefabTests.cs
//
// Read-back on the five built art prefabs (spec §8): the right number of measured wheels, and
// three capped, world-space, textured systems each. Run after Tools ▸ Vehicles ▸ Build Monowheel
// Presentation — a missing prefab is a failure, because shipping without it is the bug.
using NUnit.Framework;
using SpaceGame.Vehicles.Monowheel;
using UnityEditor;
using UnityEngine;
using B = SpaceGame.EditorTools.MonowheelPresentationBuilder;

namespace SpaceGame.EditorTools
{
    public class MonowheelPrefabTests
    {
        private static MonowheelPresentation Load(string variant)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(B.PrefabPath(variant));
            Assert.IsNotNull(prefab, $"{B.PrefabPath(variant)} is not built");
            var p = prefab.GetComponent<MonowheelPresentation>();
            Assert.IsNotNull(p, $"{variant} has no MonowheelPresentation");
            return p;
        }

        [Test]
        public void EveryVariantHasItsMeasuredWheels()
        {
            foreach (var (variant, _, rings) in B.Variants)
            {
                MonowheelPresentation p = Load(variant);
                Assert.AreEqual(rings, p.Wheels.Count, variant);
                foreach (MonowheelWheel w in p.Wheels)
                {
                    Assert.IsNotNull(w.ringBone, variant);
                    Assert.AreEqual(1f, w.localAxle.magnitude, 1e-3f, $"{variant} axle");
                    Assert.That(w.paddleRadius, Is.InRange(1.8f, 2.1f), $"{variant} radius");
                }
            }
        }

        [Test]
        public void EveryWheelHasThreeCappedWorldSpaceSystemsWithMaterials()
        {
            foreach (var (variant, _, _) in B.Variants)
                foreach (MonowheelWheel w in Load(variant).Wheels)
                {
                    Check(w.spray, B.SprayCap, variant);
                    Check(w.dust, B.DustCap, variant);
                    Check(w.smoke, B.SmokeCap, variant);
                }
        }

        [Test]
        public void DustLingersFiveSeconds()
        {
            foreach (var (variant, _, _) in B.Variants)
                foreach (MonowheelWheel w in Load(variant).Wheels)
                {
                    Assert.AreEqual(4.5f, w.dust.main.startLifetime.constantMin, 1e-3f, variant);
                    Assert.AreEqual(5f, w.dust.main.startLifetime.constantMax, 1e-3f, variant);
                }
        }

        [Test]
        public void DoublesSpinOnTwoDifferentCamberedAxles()
        {
            foreach (string variant in new[] { "Double", "DoubleWide" })
            {
                MonowheelPresentation p = Load(variant);
                Transform root = p.transform;
                Vector3 a = root.InverseTransformDirection(p.Wheels[0].ringBone.TransformDirection(p.Wheels[0].localAxle));
                Vector3 b = root.InverseTransformDirection(p.Wheels[1].ringBone.TransformDirection(p.Wheels[1].localAxle));
                Assert.Less(Mathf.Abs(Vector3.Dot(a, b)), 0.9999f, $"{variant}: both rings on one axle — camber lost");
            }
        }

        private static void Check(ParticleSystem ps, int cap, string variant)
        {
            Assert.IsNotNull(ps, variant);
            Assert.AreEqual(cap, ps.main.maxParticles, $"{variant} {ps.name} cap");
            Assert.AreEqual(ParticleSystemSimulationSpace.World, ps.main.simulationSpace, $"{variant} {ps.name}");
            Assert.IsNotNull(ps.GetComponent<ParticleSystemRenderer>().sharedMaterial, $"{variant} {ps.name} material");
        }
    }
}
```

- [ ] **Step 2: Run the tests.** EditMode filter `SpaceGame.EditorTools.MonowheelPrefabTests`. Expected: 4 pass, because Task 3 built the prefabs. If they fail, fix the builder, not the test.

- [ ] **Step 3: Commit** (only if the user has asked for commits)

```bash
git add Assets/Game/Editor/Tests/MonowheelPrefabTests.cs*
git commit -m "test(monowheel): read-back on the five presentation prefabs"
```

---

### Task 5: Documentation

**Files:**
- Create: `docs/AI/systems/Monowheel.md`
- Modify: `docs/Human/the-systems.md` (append an entry)
- Modify: `docs/superpowers/specs/2026-09-24-monowheel-presentation-design.md` §5 (menu path)
- Regenerate: `docs/AI/INDEX.md`, `docs/AI/ROUTING.md` (via the tool; never hand-edit)

- [ ] **Step 1: Read the rules and a sibling doc's shape.** Read `docs/AI/CONTRIBUTING.md` and the frontmatter of `docs/AI/systems/Jetpack.md`: the frontmatter keys (`system`, `layer`, `summary`, `paths`, `symptoms`, `reads_with`, `updated`) and the section order **Model → Key types → Flows → Multiplayer → Persistence → Gotchas → Extending**.

- [ ] **Step 2: Write `docs/AI/systems/Monowheel.md`** with:
  - **Frontmatter:**
    - `system: Monowheel`
    - `layer: vehicles`
    - `summary: "Monowheel presentation: rings spin at ground speed, paddles throw sand into a 5 s dust cloud, hubs smoke — from the root's motion alone."`
    - `paths:` `Assets/Game/Scripts/Vehicles/Monowheel/`, `Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs`, `Assets/Game/Prefabs/Vehicles/Monowheel/`, `Assets/Game/Art/Models/Vehicles/Monowheel/`
    - `symptoms:` "the monowheel's wheels wobble or orbit instead of spinning", "the wheels spin backwards", "no sand comes off the wheels however fast it goes", "a burst of dust appears when a monowheel is loaded or teleported"
    - `reads_with: [Vehicles, Jetpack, AgentSystem]`
    - `updated: 2026-09-24`
  - **Model:** the spec §2 summary. It reads only the transform, it's the Strider motor's presentation, and it uses art prefabs.
  - **Key types:** `MonowheelPresentationMath`, `MonowheelWheel`, `MonowheelPresentation`, `MonowheelPresentationBuilder`.
  - **Flows:** build (measure, systems, save, self-check), then per frame (speed, spin, probe, rates, LOD).
  - **Multiplayer:** local on every machine, no messages.
  - **Persistence:** no state worth persisting, and why.
  - **Gotchas:**
    - The axle is measured, never assumed.
    - A teleport must not read as speed.
    - A particle system without a material draws nothing, silently.
    - The ground probe uses the object's own `PhysicsScene`, so preview-scene checks work.
    - Spin never LODs.
  - **Extending:** adding a layer means a builder recipe, a `MonowheelWheel` field, a rate in `Present`, a read-back assert, and doc rows. Adding a variant means one `Variants` line.
  - **Budget table:** the caps and LOD distances, with the profiler numbers from Task 6.

- [ ] **Step 3: Add the human entry.** Append a short plain-language paragraph under the vehicles part of `docs/Human/the-systems.md`: "Monowheels — the Striders' rim-driven machines. Their wheels spin with the ground they cover, the paddles throw sand that hangs as a dust cloud for five seconds, and smoke curls up out of each hub. It is presentation only, so it looks the same to everyone in a multiplayer game."

- [ ] **Step 4: Fix the spec's menu path.** In spec §5, change `SpaceGame/Vehicles/Build Monowheel Presentation` to `Tools/Vehicles/Build Monowheel Presentation` (the repo convention, e.g. `DuneFoilBuilder`).

- [ ] **Step 5: Regenerate and validate.** Run `python tools/docs_check.py --index`. Expected: INDEX and ROUTING are regenerated, and validation passes.

- [ ] **Step 6: Commit** (only if the user has asked for commits)

```bash
git add docs/AI/systems/Monowheel.md docs/Human/the-systems.md docs/AI/INDEX.md docs/AI/ROUTING.md docs/superpowers/specs/2026-09-24-monowheel-presentation-design.md
git commit -m "docs(monowheel): system doc, human entry, routing"
```

---

### Task 6: Play verification and profile

**Files:**
- Modify: `docs/AI/systems/Monowheel.md` (budget numbers)

- [ ] **Step 1: Look.** In a test scene on sand, drag or animate `Monowheel_Runner` and `Monowheel_DoubleWide` forward at about 15 m/s. Confirm:
  - The wheels spin forward, and the DoubleWide's cambered wheels spin on their tilted axles.
  - Spray comes off the paddles.
  - The dust cloud hangs where it was kicked up and is gone by about 5 s.
  - Hub smoke rises, with a trickle when stopped.
  - Nothing appears in mid-air after lifting the vehicle off the ground.
  - Effects thin past 60 m and are gone at 150 m.

- [ ] **Step 2: Client check.** Host plus client, with a monowheel moved on the host through a replicated transform (any temporary `NetworkTransform` setup; the Strider prefab brings the real one). The client must see the same spin and dust. No messages are expected.

- [ ] **Step 3: Profile.** Six `Monowheel_DoubleWide` moving at full speed within 60 m. Record from the Profiler:
  - particle count
  - `ParticleSystem.Update` time
  - GPU transparent pass time
  - frame time

  Enter them in the Monowheel.md budget table. If the frame is over budget, lower the caps and LOD distances, which are tunables, and re-run the builder. Do not remove the effect.

- [ ] **Step 4: Update the doc and regenerate.** Run `python tools/docs_check.py --index`.

- [ ] **Step 5: Commit** (only if the user has asked for commits)

```bash
git add docs/AI/systems/Monowheel.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "docs(monowheel): measured particle budget"
```
