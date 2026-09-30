# Strider Monowheels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Monowheels that players and NPCs both drive through one physics motor; 8 Strider scouts ride them around the walking city with 2 out sweeping at a time; Strider war parties become monowheel convoys whose doubles carry a driver and 3 gunners.

**Architecture:** Drive maths in a pure static class (`MonowheelDrive`), wrapped by a thin `MonowheelMotor` Rigidbody shell that implements the existing `IMovementMotor` + `IRiderControllable` contract, so `SteerModule` (players) and `AgentController` (NPC `MoveIntent`s) drive the same code. NPC path-following reuses `WalkerPath`/`WalkerSteering` through a `NavPathFollower` extracted from `LeggedDriver`. Prefabs nest the parallel session's `Monowheel_<Variant>` art prefabs (ring spin + particles are theirs) and add a Body-only lean. Scouting is a server-side `ScoutRota` on the city's lead house; gunners are `VesselSeats` filled by `MountedGunners`.

**Tech Stack:** Unity 6000.3.11f1, C#, Netcode for GameObjects (`ClientNetworkTransform` + `NetAuthority` + `MountNetworkSync`), NUnit EditMode.

**Spec:** [docs/superpowers/specs/2026-09-24-strider-monowheels-design.md](../specs/2026-09-24-strider-monowheels-design.md). **Runs after** [2026-09-24-striders-walking-city.md](2026-09-24-striders-walking-city.md) (needs its Strider faction, nomads, roster, `StriderCityBuilder`, `CrewShift`, `strider-city` template) **and after** the parallel session's [2026-09-24-monowheel-presentation.md](2026-09-24-monowheel-presentation.md) (needs its art prefabs `Assets/Game/Prefabs/Vehicles/Monowheel/Monowheel_<Runner|Hauler|Patched|Double|DoubleWide>.prefab` with `MonowheelPresentation`).

## Global Constraints

- Everything in the walking-city plan's Global Constraints applies (commit only when authorised, builder-owned prefabs, `Sync(out _, out _)` never `SyncMenu()`, explicit module priorities, no `SetFormation("")`, `LeggedDriver` speed clamp, appended save fields, no magic numbers, baked faction, verification commands, Unity bridge).
- **Never spin a ring and never touch** `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation*.cs`, `MonowheelWheel.cs`, `Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs` or anything under `Assets/Game/Prefabs/Vehicles/Monowheel/` — the parallel session owns them. Our prefabs **nest** `Monowheel_<Variant>.prefab` as a child named `Body`.
- **Never yaw the `Body` child.** `MonowheelPresentation` reads speed along the root/Body forward (+Z); the art's forward is the driving direction. Lean is a **roll on `Body` only**, never on the physics root.
- **Assembly boundary:** before creating a file under `Assets/Game/Scripts/Vehicles/Monowheel/`, check for an `.asmdef` there. If one exists (the parallel session may add one), put this plan's Assembly-CSharp-dependent files (they use `IMovementMotor`, `AgentController`, `NpcSeating`) in `Assets/Game/Scripts/Vehicles/MonowheelDriving/` instead, and use that folder everywhere this plan says `Vehicles/Monowheel/`. Record which in the ledger.
- Physics mount netcode is the project stack: `NetworkObject` → `ClientNetworkTransform` → `NetRelay` → `NetAuthority` (`freezePhysicsOnRemote = true`) → `NetworkedHealthComponent`; player ownership handoff is `MountNetworkSync`'s (only when a `SteerModule` is present).
- Speeds (spec §2.1, tuned in Task 1): singles `topSpeed` 20 m/s, doubles 16 m/s; NPC cruise ≤ 16 m/s and **below every follower's `topSpeed`**.
- Tests: pure maths in `Assets/Game/Editor/Tests/` (EditMode, `SpaceGame.EditorTools` namespace), same style as `FormationMathTests`.

## Review Focus

1. **A player mounts a monowheel an NPC is riding** — the NPC rider is evicted (`MountModule.VacateSeatForPlayer`) and the player drives; the NPC channel must not fight the rider on the same frame (Task 4 `RiderFrame_SkipsTheMoveIntent`).
2. **An NPC driver at 16 m/s approaches a sharp corner or its stop point** — it must slow early, not overshoot (Task 3 `CornerAhead_LowersTheWantedSpeed`, `StoppingDistance_BrakesInTime`).
3. **A double is destroyed or folds with gunners aboard** — gunners despawn first, none stranded at the scene root (Task 5 `Gunners_AreDespawnedBeforeTheMount` + Task 11 play check).
4. **A scout pair's sweep target is off the NavMesh / unreachable** — the rota times the sweep out and sends them home (Task 7 `ASweepThatRunsTooLong_EndsAndReturnsHome`).
5. **Only one scout alive, or none** — the rota sends what it has and never throws (Task 7 `FewerThanTwoLiving_SendsWhatThereIs`).

---

### Task 1: Spike — does a physics monowheel drive? (throwaway)

**Files:** none kept (`Assets/Scratch/`, deleted at the end).

- [ ] **Step 1:** In a scratch scene with NavMesh-baked uneven ground (or on a copy of a world chunk in `Assets/Scratch/` — never the user's scenes), build a throwaway root: non-kinematic `Rigidbody` (mass 400, drag 0, angular drag 5, `FreezeRotationX|Z`, interpolate), a `SphereCollider` sized to the ring (radius ≈ 1.95 m, centre at hub height) plus a box for the chassis, and a nested `Monowheel_Runner.prefab` as `Body` (if the parallel session's prefab does not exist yet, nest the raw `desert_monowheel_runner.fbx`). Drive it by a throwaway script that sets `linearVelocity` along a heading and `MoveRotation` yaw.
- [ ] **Step 2:** Answer in writing: at 20 m/s on the terrain does it stay grounded over bumps (vertical velocity, airtime), does the sphere collider snag on terrain seams, what `Rigidbody` settings keep it from flipping; can it climb the steepest NavMesh slope at speed; at what corner angle does 16 m/s overshoot a 6 m arrive radius.
- [ ] **Step 3:** Record the numbers you would put in `MonowheelDriveSettings` (Task 3) and the collider/Rigidbody settings (Task 6) under "Spike findings" at the end of this plan; delete `Assets/Scratch/`; reopen `Bootstrap`. If the vehicle cannot be kept on the ground at 16 m/s with any reasonable settings, **stop and report**.

---

### Task 2: `NavPathFollower` — extract LeggedDriver's path following

**Files:**
- Create: `Assets/Game/Scripts/agents/AI/Motors/NavPathFollower.cs`
- Modify: `Assets/Game/Scripts/agents/AI/Motors/LeggedDriver.cs` (`SteerAlongPath` :408-428, `TryBuildPath` :483-498, the path fields :55-125)
- Test: `Assets/Game/Editor/Tests/NavPathFollowerTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed class NavPathFollower
{
    public NavPathFollower(float repathInterval, float repathTolerance, float cornerArriveRadius,
                           CornerSource corners);
    public delegate int CornerSource(Vector3 from, Vector3 to, Vector3[] into);  // returns corner count, 0 = no path
    public Vector3 SteerTarget(Vector3 position, Vector3 target, float deltaTime);  // next corner, or target when pathless
    public bool TryGetCornerAfter(Vector3 position, out Vector3 corner);            // the corner after the current one (for slowing)
    public bool HasPath { get; }
    public void Clear();
    public static CornerSource NavMeshCorners(float sampleDistance);               // the real NavMesh.CalculatePath source
}
```

- [ ] **Step 1: Write the failing tests** (a fake `CornerSource` counts calls and returns a fixed polyline):

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class NavPathFollowerTests
    {
        private int builds;
        private int Corners(Vector3 from, Vector3 to, Vector3[] into)
        {
            builds++;
            into[0] = from; into[1] = new Vector3(0f, 0f, 50f); into[2] = to;
            return 3;
        }

        private NavPathFollower Make() => new NavPathFollower(0.5f, 2f, 6f, Corners);

        [Test]
        public void SteersAtTheFirstRealCorner_NotAtTheTarget()
        {
            Vector3 steer = Make().SteerTarget(Vector3.zero, new Vector3(50f, 0f, 50f), 0.1f);
            Assert.AreEqual(new Vector3(0f, 0f, 50f), steer);
        }

        [Test]
        public void Repaths_OnlyWhenTheTargetMovesOrTheIntervalPasses()
        {
            var f = Make();
            var target = new Vector3(50f, 0f, 50f);
            f.SteerTarget(Vector3.zero, target, 0.1f);
            f.SteerTarget(Vector3.zero, target + Vector3.right, 0.1f);   // moved 1 m < tolerance 2
            Assert.AreEqual(1, builds);
            f.SteerTarget(Vector3.zero, target + Vector3.right * 3f, 0.1f);
            Assert.AreEqual(2, builds, "moved past the tolerance");
            f.SteerTarget(Vector3.zero, target + Vector3.right * 3f, 0.6f);
            Assert.AreEqual(3, builds, "the interval elapsed");
        }

        [Test]
        public void NoPath_SteersStraightAtTheTarget()
        {
            var f = new NavPathFollower(0.5f, 2f, 6f, (a, b, into) => 0);
            var target = new Vector3(10f, 0f, 10f);
            Assert.AreEqual(target, f.SteerTarget(Vector3.zero, target, 0.1f));
            Assert.IsFalse(f.HasPath);
        }

        [Test]
        public void CornerAfter_IsTheOneBeyondTheCurrentCorner()
        {
            var f = Make();
            f.SteerTarget(Vector3.zero, new Vector3(50f, 0f, 50f), 0.1f);
            Assert.IsTrue(f.TryGetCornerAfter(Vector3.zero, out Vector3 after));
            Assert.AreEqual(new Vector3(50f, 0f, 50f), after);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** (type-check: `NavPathFollower` missing).
- [ ] **Step 3: Implement.** `NavPathFollower` owns a `WalkerPath`, a `Vector3[64]` corner buffer, `pathTarget`, `repathTimer`, `hasPath`; `SteerTarget` is `LeggedDriver.SteerAlongPath`'s body minus `SteerTowards`/`ApplyClimbDetour` (return the steer point instead). `TryGetCornerAfter` needs `WalkerPath` to expose the next-after-current corner: add `public bool TryGetCornerAfterCurrent(out Vector3 corner)` to `WalkerPath` (`Assets/Game/Scripts/Locomotion/Steering/WalkerPath.cs`) and a test for it in `Assets/Game/Tests/EditMode/WalkerPathTests.cs` beside the existing ones. `NavMeshCorners(sampleDistance)` is `LeggedDriver.TryBuildPath`'s NavMesh body (sample both ends, `NavMesh.CalculatePath`, reject `PathInvalid`, `GetCornersNonAlloc`, `< 2` → 0), holding its own `NavMeshPath` created lazily on first call (never in a field initializer — native constructor). Then make `LeggedDriver` use it: construct in `Awake` from its existing serialized fields; `SteerAlongPath` becomes `SteerTowards(ApplyClimbDetour(follower.SteerTarget(transform.position, target, deltaTime), deltaTime));`; delete the now-unused private fields and `TryBuildPath`.
- [ ] **Step 4: Run** `NavPathFollowerTests` (4), `WalkerPathTests` (all), and the legged regression fixtures (`SpiderWalkerGroundingTests`, `OstrichSteeringTests`, `CrabLocomotionTests`, `LateralTravelTests`) → green.
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 3: `MonowheelDrive` — the pure drive model

**Files:**
- Create: `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelDrive.cs` (see the assembly-boundary constraint)
- Test: `Assets/Game/Editor/Tests/MonowheelDriveTests.cs`

**Interfaces:**
- Produces:
```csharp
[System.Serializable] public struct MonowheelDriveSettings
{
    public float topSpeed, reverseSpeed, acceleration, braking, coastDrag;
    public float turnRate, turnRateAtTop, lateralGrip;
    public float maxLean, leanPerLateralAccel;
    public float cornerSlowAngle, cornerMinSpeedFraction;
}
public static class MonowheelDrive
{
    public static float NextSpeed(float speed, float throttle, float deltaTime, in MonowheelDriveSettings s);
    public static float TurnRate(float speed, in MonowheelDriveSettings s);
    public static float NextHeading(float headingDeg, float steer, float speed, float deltaTime, in MonowheelDriveSettings s);
    public static Vector3 GripVelocity(Vector3 velocity, Vector3 forward, float speed, float deltaTime, in MonowheelDriveSettings s);
    public static float Lean(float speed, float yawRateDegPerSec, in MonowheelDriveSettings s);
    public static float WantedSpeed(float remainingDistance, float turnAheadDeg, float speedMultiplier, in MonowheelDriveSettings s);
    public static (float throttle, float steer) NpcInput(float headingDeg, float speed, Vector3 position, Vector3 steerAt, float wantedSpeed);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.EditorTools
{
    public class MonowheelDriveTests
    {
        private static MonowheelDriveSettings S => new MonowheelDriveSettings
        {
            topSpeed = 20f, reverseSpeed = 4f, acceleration = 6f, braking = 12f, coastDrag = 2f,
            turnRate = 90f, turnRateAtTop = 25f, lateralGrip = 4f,
            maxLean = 25f, leanPerLateralAccel = 2.5f,
            cornerSlowAngle = 90f, cornerMinSpeedFraction = 0.3f,
        };

        [Test] public void Throttle_AcceleratesTowardTopSpeed_AndNoFurther()
        {
            Assert.AreEqual(6f, MonowheelDrive.NextSpeed(0f, 1f, 1f, S), 1e-4f);
            Assert.AreEqual(20f, MonowheelDrive.NextSpeed(19f, 1f, 1f, S), 1e-4f);
        }

        [Test] public void NoThrottle_Coasts_BrakeStopsFaster()
        {
            Assert.AreEqual(8f, MonowheelDrive.NextSpeed(10f, 0f, 1f, S), 1e-4f);
            Assert.AreEqual(0f, MonowheelDrive.NextSpeed(10f, -1f, 1f, S), 1e-4f);
        }

        [Test] public void Reverse_IsSlow() =>
            Assert.AreEqual(-4f, MonowheelDrive.NextSpeed(0f, -1f, 10f, S), 1e-4f);

        [Test] public void TurningIsSlowerAtSpeed()
        {
            Assert.AreEqual(90f, MonowheelDrive.TurnRate(0f, S), 1e-4f);
            Assert.AreEqual(25f, MonowheelDrive.TurnRate(20f, S), 1e-4f);
            Assert.Greater(MonowheelDrive.TurnRate(5f, S), MonowheelDrive.TurnRate(15f, S));
        }

        [Test] public void Heading_FollowsSteer_ReversedWhenRollingBackwards()
        {
            Assert.AreEqual(9f, MonowheelDrive.NextHeading(0f, 1f, 0f, 0.1f, S), 1e-3f);
            Assert.Less(MonowheelDrive.NextHeading(0f, 1f, -2f, 0.1f, S), 0f);
        }

        [Test] public void Grip_BleedsSideways_KeepsVerticalAndForward()
        {
            Vector3 v = MonowheelDrive.GripVelocity(new Vector3(5f, -3f, 10f), Vector3.forward, 10f, 1f, S);
            Assert.AreEqual(10f, v.z, 1e-3f);
            Assert.AreEqual(-3f, v.y, 1e-3f, "gravity is the Rigidbody's");
            Assert.Less(Mathf.Abs(v.x), 5f * 0.05f, "e^-4 of the slide is left after a second");
        }

        [Test] public void Lean_IsIntoTheTurn_AndCapped()
        {
            float right = MonowheelDrive.Lean(10f, 30f, S);
            Assert.Greater(right, 0f);
            Assert.AreEqual(-right, MonowheelDrive.Lean(10f, -30f, S), 1e-4f);
            Assert.AreEqual(25f, MonowheelDrive.Lean(20f, 180f, S), 1e-4f);
        }

        [Test] public void CornerAhead_LowersTheWantedSpeed()
        {
            float straight = MonowheelDrive.WantedSpeed(500f, 0f, 1f, S);
            float hairpin = MonowheelDrive.WantedSpeed(500f, 90f, 1f, S);
            Assert.AreEqual(20f, straight, 1e-4f);
            Assert.AreEqual(6f, hairpin, 1e-4f, "cornerMinSpeedFraction of top speed");
        }

        [Test] public void StoppingDistance_BrakesInTime()
        {
            // v^2 = 2 a d: with 12 m/s^2 braking, 6 m left allows 12 m/s.
            Assert.AreEqual(12f, MonowheelDrive.WantedSpeed(6f, 0f, 1f, S), 1e-3f);
            Assert.AreEqual(0f, MonowheelDrive.WantedSpeed(0f, 0f, 1f, S), 1e-4f);
        }

        [Test] public void NpcInput_SteersTowardTheTarget_ThrottlesToTheWantedSpeed()
        {
            var (throttle, steer) = MonowheelDrive.NpcInput(0f, 5f, Vector3.zero, new Vector3(10f, 0f, 10f), 15f);
            Assert.Greater(steer, 0f, "target is to the right of +Z");
            Assert.AreEqual(1f, throttle, 1e-4f);
            var (brake, _) = MonowheelDrive.NpcInput(0f, 15f, Vector3.zero, new Vector3(0f, 0f, 10f), 5f);
            Assert.Less(brake, 0f, "over the wanted speed: brake");
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Implement**

```csharp
// The monowheel's handling as pure numbers: speed from throttle, heading from steer, how much of a
// slide the sand gives back, how far the chassis leans into a turn, and what speed an NPC driver
// should want with a corner or a stop ahead. MonowheelMotor applies these to the Rigidbody;
// MonowheelLean applies the lean to the art. Nothing here touches a scene, so it is tested alone.
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    [System.Serializable]
    public struct MonowheelDriveSettings
    {
        [Tooltip("Forward speed at full throttle, m/s.")] public float topSpeed;
        [Tooltip("Top speed backing up, m/s.")] public float reverseSpeed;
        [Tooltip("m/s^2 under throttle.")] public float acceleration;
        [Tooltip("m/s^2 under brake.")] public float braking;
        [Tooltip("m/s^2 lost rolling with no input.")] public float coastDrag;
        [Tooltip("Heading change at a crawl, deg/s.")] public float turnRate;
        [Tooltip("Heading change at top speed, deg/s.")] public float turnRateAtTop;
        [Tooltip("How fast sideways slide is bled off, 1/s. Low = more slide on sand.")] public float lateralGrip;
        [Tooltip("Largest chassis roll into a turn, degrees.")] public float maxLean;
        [Tooltip("Degrees of roll per m/s^2 of lateral acceleration.")] public float leanPerLateralAccel;
        [Tooltip("Turn ahead (degrees) at which an NPC driver is down to its slowest corner speed.")] public float cornerSlowAngle;
        [Tooltip("Slowest corner speed, as a fraction of top speed.")] public float cornerMinSpeedFraction;
    }

    public static class MonowheelDrive
    {
        public static float NextSpeed(float speed, float throttle, float deltaTime, in MonowheelDriveSettings s)
        {
            throttle = Mathf.Clamp(throttle, -1f, 1f);
            if (throttle > 0f) return Mathf.MoveTowards(speed, s.topSpeed * throttle, s.acceleration * deltaTime);
            if (throttle < 0f)
                return speed > 0f
                    ? Mathf.MoveTowards(speed, 0f, s.braking * deltaTime)
                    : Mathf.MoveTowards(speed, -s.reverseSpeed * -throttle, s.acceleration * deltaTime);
            return Mathf.MoveTowards(speed, 0f, s.coastDrag * deltaTime);
        }

        public static float TurnRate(float speed, in MonowheelDriveSettings s) =>
            Mathf.Lerp(s.turnRate, s.turnRateAtTop, Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(0.01f, s.topSpeed)));

        public static float NextHeading(float headingDeg, float steer, float speed, float deltaTime, in MonowheelDriveSettings s)
        {
            float direction = speed < 0f ? -1f : 1f;
            return headingDeg + Mathf.Clamp(steer, -1f, 1f) * TurnRate(speed, s) * deltaTime * direction;
        }

        public static Vector3 GripVelocity(Vector3 velocity, Vector3 forward, float speed, float deltaTime, in MonowheelDriveSettings s)
        {
            forward.y = 0f;
            forward.Normalize();
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 lateral = flat - forward * Vector3.Dot(flat, forward);
            lateral *= Mathf.Exp(-s.lateralGrip * deltaTime);
            Vector3 result = forward * speed + lateral;
            result.y = velocity.y;
            return result;
        }

        public static float Lean(float speed, float yawRateDegPerSec, in MonowheelDriveSettings s)
        {
            float lateralAccel = speed * yawRateDegPerSec * Mathf.Deg2Rad;
            return Mathf.Clamp(lateralAccel * s.leanPerLateralAccel, -s.maxLean, s.maxLean);
        }

        public static float WantedSpeed(float remainingDistance, float turnAheadDeg, float speedMultiplier, in MonowheelDriveSettings s)
        {
            float cornerFactor = Mathf.Lerp(1f, s.cornerMinSpeedFraction,
                                            Mathf.Clamp01(Mathf.Abs(turnAheadDeg) / Mathf.Max(1f, s.cornerSlowAngle)));
            float cruise = s.topSpeed * Mathf.Clamp01(speedMultiplier <= 0f ? 1f : speedMultiplier) * cornerFactor;
            float stopping = Mathf.Sqrt(2f * s.braking * Mathf.Max(0f, remainingDistance));
            return Mathf.Min(cruise, stopping);
        }

        /// <summary>Throttle (-1..1) and steer (-1..1) for an NPC driver heading for steerAt.</summary>
        public static (float throttle, float steer) NpcInput(float headingDeg, float speed, Vector3 position,
                                                             Vector3 steerAt, float wantedSpeed)
        {
            Vector3 to = steerAt - position;
            to.y = 0f;
            float bearing = to.sqrMagnitude > 1e-4f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : headingDeg;
            float error = Mathf.DeltaAngle(headingDeg, bearing);
            float steer = SpaceGame.Locomotion.WalkerSteering.Turn(error);
            float throttle = speed < wantedSpeed - SpeedBand ? 1f : speed > wantedSpeed + SpeedBand ? -1f : 0f;
            return (throttle, steer);
        }

        /// <summary>m/s either side of the wanted speed where an NPC neither throttles nor brakes.</summary>
        private const float SpeedBand = 0.5f;
    }
}
```

Check `WalkerSteering.Turn`'s sign convention against `NpcInput_SteersTowardTheTarget` (positive error = target to the right when heading is measured clockwise from +Z, as `Atan2(x, z)` gives); if `Turn` expects the opposite sign, negate here and say so in a comment. If `SpaceGame.Locomotion` is not referenceable from the assembly this file lands in, add the reference or move the file per the Global Constraints.

- [ ] **Step 4: Run** `MonowheelDriveTests` → 10 pass. Replace the test's `S` values with Task 1's spike numbers only if a test's arithmetic is updated with them.
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 4: `MonowheelMotor` and `MonowheelLean`

**Files:**
- Create: `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelMotor.cs`, `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelLean.cs`
- Test: `Assets/Game/Editor/Tests/MonowheelMotorTests.cs`

**Interfaces:**
- Consumes: `MonowheelDrive`, `MonowheelDriveSettings` (Task 3); `NavPathFollower` (Task 2); `IMovementMotor`, `IRiderControllable`, `RiderInput`, `MoveIntent`, `AgentIntentType`, `ITeleportAware` (existing, `Assets/Game/Scripts/agents/AI/Motors/`).
- Produces: `MonowheelMotor : MonoBehaviour, IMovementMotor, IRiderControllable, ITeleportAware` (`[RequireComponent(typeof(Rigidbody))]`, `[DefaultExecutionOrder(-100)]` like `RigidbodyMotor`); public `float Speed`, `MonowheelDriveSettings Settings`. `MonowheelLean : MonoBehaviour` with `[SerializeField] Transform body` and `MonowheelDriveSettings` read from the sibling motor's `Settings` (so both use one set of numbers).

- [ ] **Step 1: Write the failing tests** — what EditMode can prove without physics stepping:

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.EditorTools
{
    public class MonowheelMotorTests
    {
        private GameObject go;
        private MonowheelMotor motor;

        [SetUp] public void SetUp()
        {
            go = new GameObject("Monowheel");
            go.AddComponent<Rigidbody>();
            motor = go.AddComponent<MonowheelMotor>();
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(go);

        [Test] public void IsAMotorBothChannelsCanDrive()
        {
            Assert.IsInstanceOf<IMovementMotor>(motor);
            Assert.IsInstanceOf<IRiderControllable>(motor);
        }

        [Test] public void RiderFrame_SkipsTheMoveIntent()
        {
            motor.ApplyRiderInput(new RiderInput(new Vector2(0f, 1f), 0f, false), 0.02f);
            motor.Tick(MoveIntent.MoveTo(new Vector3(100f, 0f, 0f)), 0.02f);
            Assert.IsNull(motor.CurrentDestination, "the rider owns this frame; the AI's destination is ignored");
        }

        [Test] public void AnAiFrame_TakesTheDestination()
        {
            motor.Tick(MoveIntent.MoveTo(new Vector3(100f, 0f, 0f), 5f), 0.02f);
            Assert.AreEqual(new Vector3(100f, 0f, 0f), motor.CurrentDestination);
            Assert.IsFalse(motor.HasReachedDestination);
        }

        [Test] public void IdleIntent_ClearsTheDestination()
        {
            motor.Tick(MoveIntent.MoveTo(new Vector3(100f, 0f, 0f), 5f), 0.02f);
            motor.Tick(MoveIntent.Idle(), 0.02f);
            Assert.IsNull(motor.CurrentDestination);
            Assert.IsTrue(motor.HasReachedDestination);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Implement `MonowheelMotor`.** Mirror `RigidbodyMotor`'s shape (read it first: `Assets/Game/Scripts/agents/AI/Motors/RigidbodyMotor.cs`) — the rider latch (`ApplyRiderInput` stores `pendingRiderInput`, stamps `riderDriveFrame = Time.frameCount`), `Tick` returning early on the rider frame, `FixedUpdate` consuming inputs on the physics clock, yaw only through `body.MoveRotation`, never `transform.rotation`. State: `speed` (own field, not read back from `body.linearVelocity` — friction drains it between steps, same reason as `RigidbodyMotor`), `headingDeg`, `destination` (`Vector3?`), `stopDistance`, `speedMultiplier`, a `NavPathFollower` built in `Awake` with `NavPathFollower.NavMeshCorners(navMeshSampleDistance)`. Serialized: `MonowheelDriveSettings settings` (defaults from Task 1's findings), `repathInterval = 0.5f`, `repathTolerance = 3f`, `cornerArriveRadius = 8f`, `navMeshSampleDistance = 20f`, `defaultStopDistance = 6f`. Per `FixedUpdate`:

```csharp
            float throttle, steer;
            if (riderFrameConsumed) { throttle = rider.Move.y; steer = rider.Move.x; }
            else if (destination.HasValue)
            {
                Vector3 pos = body.position;
                Vector3 steerAt = follower.SteerTarget(pos, destination.Value, dt);
                float turnAhead = follower.TryGetCornerAfter(pos, out Vector3 after)
                    ? Vector3.Angle(Flat(steerAt - pos), Flat(after - steerAt)) : 0f;
                float remaining = Flat(destination.Value - pos).magnitude - stopDistance;
                float wanted = MonowheelDrive.WantedSpeed(remaining, turnAhead, speedMultiplier, settings);
                (throttle, steer) = MonowheelDrive.NpcInput(headingDeg, speed, pos, steerAt, wanted);
            }
            else { throttle = speed > 0f ? -1f : 0f; steer = 0f; }

            speed = MonowheelDrive.NextSpeed(speed, throttle, dt, settings);
            headingDeg = MonowheelDrive.NextHeading(headingDeg, steer, speed, dt, settings);
            body.MoveRotation(Quaternion.Euler(0f, headingDeg, 0f));
            Vector3 forward = Quaternion.Euler(0f, headingDeg, 0f) * Vector3.forward;
            body.linearVelocity = MonowheelDrive.GripVelocity(body.linearVelocity, forward, speed, dt, settings);
```

`Velocity => body.linearVelocity`; `TopSpeed => settings.topSpeed`; `IsImmobile => false`; `HasReachedDestination => !destination.HasValue || Flat(destination.Value - transform.position).magnitude <= stopDistance`; `StopAndFacePosition` → clear destination, brake, set `headingDeg` toward the face point at `TurnRate` (no strafe — it is a wheel); `ForceStop` → `speed = 0`, clear destination, zero horizontal velocity; `NudgeDestination`/`SuggestDestination` like `RigidbodyMotor`. `ITeleportAware.OnTeleported` rebases `destination` and clears the follower. `Awake` reads `headingDeg` from `transform.eulerAngles.y`. Every constant above that is not a serialized field gets a named `const` with a why.

**Implement `MonowheelLean`** (presentation, runs on every machine — must NOT be listed as a `NetAuthority` simulation driver, so it keeps running on clients; confirm how `NetAuthority` discovers drivers (`SimulationDrivers.Discover`) and make sure a plain presentation `MonoBehaviour` is not picked up, or implement `IExternallyPosed` as a no-op if discovery would disable it): each `LateUpdate`, measure yaw rate and flat speed from the root transform's change since last frame (skip the frame — reset — when the implied speed exceeds `maxPlausibleSpeed`, a serialized 50 m/s, matching `MonowheelPresentation`'s snap rule), compute `MonowheelDrive.Lean(speed, yawRate, motor.Settings)`, smooth it (`leanFollow`, serialized), and set `body.localRotation = Quaternion.Euler(0f, 0f, -lean)` (roll only; verify the sign in play so it leans INTO the turn and record it in a comment). Never yaw or pitch `body`.

- [ ] **Step 4: Run** `MonowheelMotorTests` (4) and `MonowheelDriveTests` → green.
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 5: `MountedGunners` — gunners for the doubles

**Files:**
- Create: `Assets/Game/Scripts/Vehicles/Monowheel/MountedGunners.cs`
- Modify: `Assets/Game/Scripts/agents/World/GroupMembership.cs` (`StampRider` gains a seat index)
- Test: `Assets/Game/Editor/Tests/MountedGunnersTests.cs`, append to `GroupMembershipTests.cs`

**Interfaces:**
- Consumes: `VesselSeats` (`Capacity`, `Seat(int, GameObject)`, `OccupantAt`), `NpcSpawn.Create(prefab, pos, rot, context, beforeSpawn, seated: true)`, `GroupMembership.StampRider`.
- Produces: `MountedGunners : MonoBehaviour` with `[SerializeField] GameObject gunnerPrefab`, `[SerializeField] bool spawnOnStart = true`; `public IReadOnlyList<GameObject> Gunners`; `public void SpawnGunners()` (authority only); `public void DespawnGunners()`. `GroupMembership.StampRider(GameObject mount, GameObject rider, int seat = 0)`.

- [ ] **Step 1: Read first:** `NpcPassenger.SpawnRider`, its `OnDestroy` (despawns an owned rider), and `GroupMembership.StampRider` — mirror them; do not invent a new lifecycle.
- [ ] **Step 2: Write the failing tests**

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class MountedGunnersTests
    {
        [Test]
        public void EachGunnerSeat_GetsADistinctMemberIndex_SoLoadoutsDiffer()
        {
            var group = new NpcGroup { Id = "convoy" };
            var mount = new GameObject("Double");
            var a = new GameObject("A"); var b = new GameObject("B");
            try
            {
                GroupMembership.Stamp(mount, group, 3, null);
                GroupMembership.StampRider(mount, a, 1);
                GroupMembership.StampRider(mount, b, 2);
                Assert.AreNotEqual(a.GetComponent<GroupMembership>().MemberIndex,
                                   b.GetComponent<GroupMembership>().MemberIndex);
            }
            finally { Object.DestroyImmediate(mount); Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [Test]
        public void TheDriverStamp_IsUnchanged()
        {
            var group = new NpcGroup { Id = "convoy" };
            var mount = new GameObject("Double"); var driver = new GameObject("Driver");
            try
            {
                GroupMembership.Stamp(mount, group, 3, null);
                GroupMembership.StampRider(mount, driver);
                Assert.AreEqual(3 + GroupMembership.RiderIndexOffset, driver.GetComponent<GroupMembership>().MemberIndex);
            }
            finally { Object.DestroyImmediate(mount); Object.DestroyImmediate(driver); }
        }
    }
}
```

(Adjust the expected driver index to what `StampRider` computes today — the test pins today's behaviour for seat 0; read it before writing the assertion. `Stamp` with a null tribe on a health-less mount is safe after walking-city Task 3: it is not a fighter, so `Enlist` does not run.)

- [ ] **Step 3: Run to verify it fails.**
- [ ] **Step 4: Implement.** `StampRider(mount, rider, seat = 0)`: seat 0 keeps today's index; seat N adds `N * GunnerIndexStride` (a named const, e.g. 100, with a why: keeps gunner indices apart from the driver's and from each other for the seeded loadout roll). `MountedGunners`:
  - `Start` → if `spawnOnStart && Network.Simulates(this)` → `SpawnGunners()`.
  - `SpawnGunners()`: for each seat `i` in `VesselSeats` without an occupant: `NpcSpawn.Create(gunnerPrefab, seatPos, rot, this, g => GroupMembership.StampRider(gameObject, g, i + 1), seated: true)`; then `seats.Seat(i, g)` (after the network spawn — same rule as the city crew); keep the list. A failed seat → `Debug.LogError` naming the mount and seat, and despawn that gunner.
  - `OnDestroy` (server): `DespawnGunners()` — each living gunner despawned via its `NetworkObject` when spawned, else `Destroy` — **before** the mount goes, exactly as `NpcPassenger` handles its rider. Confirm by reading `NpcPassenger.OnDestroy` that this runs before the netcode despawn of the parent; if it does not, use the same hook `VesselPilot.OnNetworkDespawn` uses (a `NetworkBehaviour` override) and say which in the report.
- [ ] **Step 5: Run** `MountedGunnersTests`, `GroupMembershipTests`, `GroupRecordTests` → green.
- [ ] **Step 6: Commit** (only if authorised).

---

### Task 6: `StriderMonowheelBuilder` — the five prefabs

**Files:**
- Create: `Assets/Game/Editor/Vehicles/StriderMonowheelBuilder.cs`
- Test: `Assets/Game/Editor/Tests/StriderMonowheelPrefabTests.cs`

**Interfaces:**
- Consumes: `MonowheelMotor`, `MonowheelLean`, `MountedGunners`; `NomadPrefabBuilder.StriderNomads`; `RosterAuthoring.StriderFactionPath`, `GlobalRelationshipsPath`; `AgentNetworkWiring.Ensure`; `SerializedFields`; the art prefabs `Assets/Game/Prefabs/Vehicles/Monowheel/Monowheel_<Variant>.prefab`.
- Produces: `StriderMonowheelBuilder.PrefabPath(string variant)` → `Assets/Game/Prefabs/Agents/Characters/Striders/StriderMonowheel_<Variant>.prefab`; `StriderMonowheelBuilder.Singles = { "Runner", "Hauler", "Patched" }`, `Doubles = { "Double", "DoubleWide" }`; `public const float SingleTopSpeed = 20f, DoubleTopSpeed = 16f, CruiseSpeed = 14f` (Task 1 numbers).

- [ ] **Step 1: Write the failing tests** — for every variant (a `[TestCaseSource]` over Singles+Doubles):
  - prefab exists; root has non-kinematic `Rigidbody` with gravity; `MonowheelMotor` is `AgentController`'s motor; `MonowheelLean.body` is the `Body` child; `Body` is a nested instance of the matching `Monowheel_<Variant>.prefab` (`PrefabUtility.GetCorrespondingObjectFromSource`) with **identity local rotation** (never yawed);
  - `MountModule` + `SteerModule` + `MountNetworkSync` (player can drive) and `NpcPassenger` whose `riderPrefab` is a `StriderNomad_*` (faction baked) and `seatPoint` is the art's `Socket_Rider_*`;
  - `EntityFaction` = Striders; `HealthComponent`; `FormationModule` (priority Social, empty id); `GoalTravelModule` (priority Fallback+1);
  - `NetworkObject` with non-zero hash; `ClientNetworkTransform`; `NetAuthority` with `freezePhysicsOnRemote`; `SaveableEntity` with a prefab id; no attack module on the mount;
  - singles: no `VesselSeats`; doubles: `VesselSeats` capacity 3, `MountedGunners` with a `StriderNomad_*` gunner prefab, and seat 0 at `Socket_Passenger_Double*`;
  - `MonowheelMotor.Settings.topSpeed` = `SingleTopSpeed` / `DoubleTopSpeed`.
- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Implement.** For each variant: `new GameObject("StriderMonowheel_<V>")`; nest `PrefabUtility.InstantiatePrefab(artPrefab)` as child `Body` (keep connected — do not unpack; never touch its contents); colliders from Task 1 (a ring `SphereCollider` + chassis box, sized from the art's renderer bounds, named consts for margins); `Rigidbody` (Task 1 mass/drag/constraints/interpolation); `MonowheelMotor` (settings; `topSpeed` per single/double); `MonowheelLean` (`body` = `Body`); `AgentController` (`MotorComponent` = the motor); `HealthComponent` + `HealthReactionModule`; `EntityFaction` (Striders + table); `FormationModule` (Social, empty id; `holdSlotAtRest` true, `slotTolerance` 4, `regroupDistance` 150 — named consts: **nothing parks** (user 2026-09-24) — a scout at home keeps its place in the column beside the city, marching or stopped, using the walking-city plan's `holdSlotAtRest`) + `GoalTravelModule` (Fallback+1); `MountModule` (`seatPoint` = the art's `Socket_Rider_*` found by name under `Body`, `mountableByDirectInteraction = true`, `defaultPerspective` third person, `followMountPitch = false`) + `SteerModule` + `MountNetworkSync`; `NpcPassenger` (`riderPrefab` = `StriderNomads[0]` prefab, `seatPoint` = the same socket, `spawnOnStart = true`); doubles: seat markers — seat 0 at `Socket_Passenger_Double*`, seats 1–2 at the centres of the two side-seat cushions found by name prefix (`Mesh_SideSeatCushion`, exactly two expected, else `Debug.LogError` and skip the double) — `ChairPose` + `VesselSeats` (3 seats) + `MountedGunners` (`gunnerPrefab` = `StriderNomads[1]` prefab); `SceneTracked` (Migrate); `AgentNetworkWiring.Ensure(root)`; `NetAuthority.freezePhysicsOnRemote = true`; `SaveableEntity`. Save with `SaveAsPrefabAsset`, destroy the temp root. After all five: `NetworkPrefabRegistrar.Sync(out _, out _)`, `SaveableWiring.TryWirePrefabs()` (LogError on false), then a `Verify()` like `RobotHorseBuilder`'s. Menu: `Tools/SpaceGame/Vehicles/Build Strider Monowheels`. If an art prefab is missing, `Debug.LogError("... run the monowheel presentation builder first")` and skip that variant.
- [ ] **Step 4: Run the builder**; read back; run `StriderMonowheelPrefabTests` → green.
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 7: `ScoutRota` — two out at a time

**Files:**
- Create: `Assets/Game/Scripts/agents/Faction/ScoutRotaLogic.cs`, `Assets/Game/Scripts/agents/Faction/ScoutRota.cs`
- Modify: `Assets/Game/Editor/Vehicles/StriderCityBuilder.cs` (add `ScoutRota` to the habitat variant)
- Test: `Assets/Game/Editor/Tests/ScoutRotaLogicTests.cs`

**Interfaces:**
- Produces: `ScoutRotaLogic.PickNext(IReadOnlyList<ScoutRecord> scouts, int wantOut) -> List<int>` (indices of the scouts home longest, living, not fighting, up to `wantOut` minus those already out); `ScoutRotaLogic.SweepPoints(Vector3 centre, float radius, int points, float startAngleDeg) -> Vector3[]`; `ScoutRotaLogic.SweepOver(float elapsed, float timeout, bool reachedLastPoint) -> bool`; `struct ScoutRecord { bool Alive, Out, Fighting; float HomeSince; }`. `ScoutRota : MonoBehaviour` (server) on the lead house.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class ScoutRotaLogicTests
    {
        private static ScoutRecord Home(float since) => new ScoutRecord { Alive = true, HomeSince = since };

        [Test] public void Sends_TheTwoHomeLongest()
        {
            var scouts = new List<ScoutRecord> { Home(30f), Home(5f), Home(50f), Home(10f) };
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, ScoutRotaLogic.PickNext(scouts, 2));
        }

        [Test] public void KeepsExactlyTwoOut()
        {
            var scouts = new List<ScoutRecord> { new ScoutRecord { Alive = true, Out = true }, Home(5f), Home(50f) };
            CollectionAssert.AreEqual(new[] { 2 }, ScoutRotaLogic.PickNext(scouts, 2));
        }

        [Test] public void SkipsTheDead_AndThoseFighting()
        {
            var scouts = new List<ScoutRecord>
            {
                new ScoutRecord { Alive = false, HomeSince = 99f },
                new ScoutRecord { Alive = true, Fighting = true, HomeSince = 98f },
                Home(1f), Home(2f),
            };
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, ScoutRotaLogic.PickNext(scouts, 2));
        }

        [Test] public void FewerThanTwoLiving_SendsWhatThereIs()
        {
            CollectionAssert.AreEqual(new[] { 0 }, ScoutRotaLogic.PickNext(new List<ScoutRecord> { Home(1f) }, 2));
            CollectionAssert.IsEmpty(ScoutRotaLogic.PickNext(new List<ScoutRecord>(), 2));
        }

        [Test] public void SweepPoints_AreOnTheRing()
        {
            Vector3[] pts = ScoutRotaLogic.SweepPoints(new Vector3(100f, 0f, 100f), 600f, 6, 0f);
            Assert.AreEqual(6, pts.Length);
            foreach (Vector3 p in pts)
                Assert.AreEqual(600f, Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(100f, 0f, 100f)), 0.01f);
        }

        [Test] public void ASweepThatRunsTooLong_EndsAndReturnsHome()
        {
            Assert.IsFalse(ScoutRotaLogic.SweepOver(100f, 300f, false));
            Assert.IsTrue(ScoutRotaLogic.SweepOver(301f, 300f, false));
            Assert.IsTrue(ScoutRotaLogic.SweepOver(10f, 300f, true));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Implement.** `ScoutRotaLogic` is pure (the four functions above; `PickNext` sorts candidates by `HomeSince` ascending — the smallest timestamp has been home longest — and returns up to `wantOut − countOut`). `ScoutRota` (server, `Network.Simulates`): acts only on the house that is the formation leader (`FormationModule.LeaderOf(id) == own formation`, same rule as `CrewShift`'s gate); every `rotaInterval` (serialized, 1 s) it collects the group's scouts — members of the same formation carrying a `MonowheelMotor` — builds `ScoutRecord`s (alive from `HealthComponent`, fighting from the rider's `AgentTargeting.HasTarget` via `NpcPassenger.Rider`), and for each picked scout: `FormationModule.active = false` (the inherited module switch — confirm the field/property name in `BehaviourModuleBase`), then walks it through `SweepPoints(leaderPosition, sweepRadius, sweepPoints, randomStart)` by setting `AgentGoal` to each point in turn (`goal.Set(point, sweepArriveRadius)`; next point on `HasArrived`). When `SweepOver(...)`: `goal.Clear()`, `active = true` (formation's regroup brings it back), stamp `HomeSince = Time.time`. Serialized: `scoutsOut = 2`, `sweepRadius = 600f`, `sweepPoints = 6`, `sweepArriveRadius = 25f`, `sweepTimeout = 300f`, `rotaInterval = 1f`. Nothing saved (spec §3: after a reload all are home). Add `ScoutRota` to the habitat variant in `StriderCityBuilder.AddBrain` (every house has it; only the leader acts).
- [ ] **Step 4: Run** `ScoutRotaLogicTests` (6), `StriderHabitatWalkerTests` (still green; add one assertion that the habitat carries `ScoutRota`).
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 8: Scouts in the city

**Files:**
- Modify: `Assets/Game/Editor/Agents/RosterAuthoring.cs` (`WireStriderCity`)
- Modify: `Assets/Game/Editor/Tests/StriderCityTemplateTests.cs`

- [ ] **Step 1: Extend the test** — the city template also holds 8 scout monowheels (fixed prefabs from the three singles, total count 8, not crew, listed after the houses and crawlers); formation shape unchanged.
- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Implement** — in `WireStriderCity`, after the crawlers: `AddMember(members, Load<GameObject>(StriderMonowheelBuilder.PrefabPath(v)), RosterRole.Scout, count, leader: false, crew: false)` for the three singles with counts 3, 3, 2 (named const `CityScouts = 8`, split in order). Keep the crab `Rider` members as they are (the crabs stay as the flank escort). The city's live leader speed (2.7 m/s) is far below the monowheels' top speed, so they keep formation.
- [ ] **Step 4: Run** `WireStriderCity` in the editor; `StriderCityTemplateTests` → green.
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 9: War parties become convoys

**Files:**
- Modify: `Assets/Game/Editor/Agents/RosterAuthoring.cs` (`AuthorStriderRoster`, the Strider war-party template's `travelSpeed`)
- Modify: `Assets/Game/Editor/Tests/StriderRosterAssetTests.cs` (`Riders_AreTheCrabOutrider` → monowheels; tiers)

- [ ] **Step 1: Update the tests:** Rider members are exactly the five `StriderMonowheel_*` prefabs, singles weight 3, doubles weight 1; tiers `0: (Rider, 2)`, `1: (Rider, 3)`, `2: (Rider, 5)`; the crab outrider is no longer in the roster; `RosterValidation.Problems` empty (Rider members need `NpcPassenger` — the monowheels have one).
- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Implement** — replace the crab `Rider` member with `MemberWeighted(RosterRole.Rider, prefab, weight)` for each monowheel (add a `weight` overload of the existing `Member` helper, not a copy); tiers as above; in `WireWorldSim` set the `strider-war-party` template's `travelSpeed` to `StriderMonowheelBuilder.CruiseSpeed` (folded speed matches the convoy).
- [ ] **Step 4: Run** `AuthorStriderRoster` then `WireWorldSim` in the editor; `StriderRosterAssetTests`, `WarPartyDirectorTests`, `NpcGroupCompositionTests` → green.
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 10: Documentation

**Files:** `docs/AI/systems/Vehicles.md` (monowheel row, `MonowheelMotor`/`MonowheelLean`/`MountedGunners`, the shared `NavPathFollower`, Gotchas: never yaw `Body`, presentation belongs to `MonowheelPresentation`, ownership handoff), `docs/AI/systems/Striders.md` (scouts, `ScoutRota`, convoys), `docs/AI/systems/Locomotion.md` (`LeggedDriver` now uses `NavPathFollower`), `docs/Human/the-systems.md` (a line in the Striders entry about scouts and convoys), `docs/superpowers/plans/2026-09-07-faction-system.md` (Phase 4 row), `.claude/skills/spacegame-vessel/SKILL.md` or `spacegame-agent` (a "fast physics mount" pointer, whichever covers mounts).

- [ ] **Step 1:** Update each doc in its own shape; add `symptoms:` for anything that cost time; delete what became untrue (e.g. "crab outriders are the Strider war-party mount").
- [ ] **Step 2:** `python3 tools/docs_check.py --index` → 0 errors.
- [ ] **Step 3: Commit** (only if authorised).

---

### Task 11: Verification — play, host **and** client, reload, profile

- [ ] **Step 1:** Type-check; full EditMode suite `FAILED=0` (or only proven pre-existing failures, named).
- [ ] **Step 2: Player drive (host).** Mount a riderless monowheel (kill a scout's rider or spawn one): throttle to top speed, lean into turns, slide on sand, stop, reverse, dismount. The rings spin (theirs) and the chassis leans (ours) together.
- [ ] **Step 3: Scouts (host).** At the city: 8 monowheels in formation; after `rotaInterval` two leave, sweep ~600 m out and back, rejoin, the next two go. Kill one scout's rider mid-sweep — the rota keeps two out.
- [ ] **Step 4: Convoy.** Push the Striders to `AtWar`; a tier-2 party arrives as a monowheel convoy at ~14 m/s, doubles with 3 gunners firing from their seats; riders dismount to fight. Kill a double: gunners fall, none left hanging at the scene root.
- [ ] **Step 5: Client.** Repeat Steps 2–4 watching from a client: smooth monowheel motion, rings and lean visible, gunners seated; a client player drives a monowheel (ownership handoff) and the host sees it.
- [ ] **Step 6: Reload.** Save while scouts are out → reload → all scouts home, rota restarts. Park a player-driven monowheel, save, reload → it is where it was parked; grep the save JSON for it.
- [ ] **Step 7: Profile** beside the city (~50 agents) and beside a tier-2 convoy; record in `Striders.md` Gotchas; lower scout count / double weight if over budget.
- [ ] **Step 8:** Update the faction plan status row and report results, including anything unverified.

---

## Spike findings

*(Filled in by Task 1.)*
