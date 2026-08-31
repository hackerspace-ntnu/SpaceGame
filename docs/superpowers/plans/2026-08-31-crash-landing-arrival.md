# Crash Landing Arrival Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a player first enters a newly created world, they wake strapped into a seat aboard the `PlayerShip`, look out through the canopy as it flies a curving descent, and crash — leaving a persistent wreck they climb out of.

**Architecture:** The ship is a real server-spawned `NetworkObject` driven down a closed-form arc by the server and replicated by its existing `ClientNetworkTransform`. Players are parented into `ShipSeat` markers by a new `SeatedRider`, which copies `MountNetworkSync`'s event-channel-plus-state-channel pattern so late joiners are seated correctly. Presentation (letterbox, free look, shake, impact) is a local `Cutscene` under the existing `CutsceneDirector`.

**Tech Stack:** Unity 6000.3.11f1, Unity Netcode for GameObjects, NUnit EditMode tests, Newtonsoft.Json for save state.

**Spec:** [`docs/superpowers/specs/2026-08-31-crash-landing-arrival-design.md`](../specs/2026-08-31-crash-landing-arrival-design.md)

---

## Conventions for this plan

**Type-checking.** There is no `dotnet` on this machine and opening the Editor is slow. Use the script added in Task 0:

```bash
python3 tools/typecheck.py
```

It reuses the response file Unity's own build last generated (defines, ~400 assembly references, langversion) but rebuilds the source list, so files you just created are included. It takes about 4 seconds. Expected output on success: `No errors.`

**Running tests.** EditMode tests need the Editor and cannot run from this shell. Type-check after every task; run the EditMode suite in the Unity Test Runner (`Window > General > Test Runner > EditMode > Run All`) at the checkpoints marked **RUN TESTS**.

**Test location.** `Assets/Game/Editor/Tests/`, namespace `SpaceGame.EditorTools`, no asmdef. This is deliberate and is documented at the top of `NetGunTests.cs`: these tests touch `Assembly-CSharp` types, and an asmdef cannot reference `Assembly-CSharp`.

**Commits.** The repo has a commit hook that false-positives on `$(...)`, backticks and `$((` anywhere in a Bash command. Write commit messages without them. If the hook still fires, tell the user rather than retrying.

**Namespaces.** New gameplay code goes in `SpaceGame.Gameplay.Arrival`. New presentation code goes in `SpaceGame.Presentation`.

---

## File Structure

**Create:**

| File | Responsibility |
| --- | --- |
| `tools/typecheck.py` | Headless type-check (already written; Task 0 only verifies it). |
| `Assets/Game/Scripts/Gameplay/Arrival/Core/ArrivalPath.cs` | Serializable description of one descent. Data only. |
| `Assets/Game/Scripts/Gameplay/Arrival/Core/ArrivalTrajectory.cs` | Pure: normalised time to a pose. |
| `Assets/Game/Scripts/Gameplay/Arrival/Core/SeatOrdering.cs` | Pure: stable seat ordering and wrap-around assignment. |
| `Assets/Game/Scripts/Gameplay/Arrival/Runtime/SeatedRider.cs` | Replicated attach/detach of players into seats. |
| `Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.cs` | Server-only sequence. |
| `Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalSaveable.cs` | The "already arrived" world flag. |
| `Assets/Game/Scripts/Presentation/Cutscenes/Core/ShakeMath.cs` | Pure: capped shake displacement. |
| `Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCameraRig.cs` | Seated free look plus shake, one LateUpdate. |
| `Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCutscene.cs` | The beats. |
| `Assets/Game/Editor/Tests/ArrivalTests.cs` | Tests for the three pure units. |

**Modify:**

| File | Change |
| --- | --- |
| `Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs` | Append ids 90/91. |
| `Assets/Game/Scripts/Core/Settings/GameSettings.cs` | Add `CameraShakeIntensity`. |
| `Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.cs` | Route to arrival before the ordinary spawn. |
| `docs/architecture/Cutscenes.md` | Document the arrival and the replication seam. |

---

## Task 0: Confirm the type-check harness

**Files:**
- Verify: `tools/typecheck.py`

- [ ] **Step 1: Run the type-check on the untouched tree**

```bash
python3 tools/typecheck.py
```

Expected: a line like `Unity 6000.3.11f1 | 744 sources | rsp 200b0aE.dag`, then `No errors.`

If it reports missing Unity paths, the Editor version in `ProjectSettings/ProjectVersion.txt` is not installed in the Hub — stop and tell the user. If it reports errors on an untouched tree, the tree was already broken; stop and tell the user rather than proceeding.

- [ ] **Step 2: Commit the harness**

```bash
git add tools/typecheck.py
git commit -m "tools: add headless Assembly-CSharp type-check"
```

---

## Task 1: `ArrivalPath` — the data

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Arrival/Core/ArrivalPath.cs`

No test of its own: it is a field bag with no behaviour. It is exercised by Task 2's tests.

- [ ] **Step 1: Create the file**

```csharp
using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// One crash descent, as numbers.
    ///
    /// <para>
    /// A serializable struct rather than fields spread across <see cref="ArrivalDirector"/> so the
    /// trajectory can be evaluated — and tested — with no Unity object in existence. Everything
    /// here is authored in the Inspector except <see cref="ImpactPosition"/>, which is measured off
    /// the world at runtime.
    /// </para>
    /// </summary>
    [Serializable]
    public struct ArrivalPath
    {
        [Tooltip("Where the hull ends up. Resolved at runtime from the world's spawn anchor, not authored.")]
        public Vector3 ImpactPosition;

        [Tooltip("Metres above the impact point that the descent begins. Kept inside the band where " +
                 "the desert skybox and volumetric clouds still read correctly — this is a " +
                 "high-atmosphere entry, not true orbit.")]
        public float StartAltitude;

        [Tooltip("How far from the impact point, horizontally, the descent begins. This is a " +
                 "STREAMING budget as much as a staging one: chunks are 500 m and pin under tracked " +
                 "entities, so a descent that crossed the map would drag the streamer through a " +
                 "dozen chunks at speed. A few hundred metres keeps it to two or three.")]
        public float LateralBudget;

        [Tooltip("Compass bearing, in degrees, of the point the descent starts from, measured around " +
                 "the impact point.")]
        public float StartBearing;

        [Tooltip("How far around the impact point the ship swings on the way down. This is what " +
                 "makes the path an arc rather than a straight line — the 'orbit' in the brief. " +
                 "Zero would be a dead-straight dive.")]
        public float SweepDegrees;

        [Tooltip("Peak bank angle, reached at the top of the descent and unwound to zero by impact " +
                 "so the wreck does not land on its side.")]
        public float MaxBankDegrees;

        [Tooltip("Peak nose-down pitch, unwound to zero by impact — the flare.")]
        public float MaxPitchDegrees;

        /// <summary>
        /// The values the arrival ships with. Used by <see cref="ArrivalDirector"/>'s serialized
        /// default so a freshly added component is already flyable, and by the tests as a realistic
        /// starting point rather than a hand-made one that drifts from what the game uses.
        /// </summary>
        public static ArrivalPath Default => new()
        {
            ImpactPosition = Vector3.zero,
            StartAltitude = 2200f,
            LateralBudget = 900f,
            StartBearing = 35f,
            SweepDegrees = 110f,
            MaxBankDegrees = 22f,
            MaxPitchDegrees = 18f,
        };
    }
}
```

- [ ] **Step 2: Type-check**

```bash
python3 tools/typecheck.py
```

Expected: `No errors.`

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Arrival/Core/ArrivalPath.cs
git commit -m "feat: add ArrivalPath, the data for one crash descent"
```

---

## Task 2: `ArrivalTrajectory` — the curve

The shape is a **descending spiral**: the horizontal radius shrinks linearly from `LateralBudget` to zero while the bearing sweeps by `SweepDegrees`, and altitude falls as `1 - t^2`.

Three properties this shape buys, each of which a naive Bezier would not:
- Lateral distance is `LateralBudget * (1 - t)`, so the budget is respected **by construction** rather than by hoping a control point behaves.
- It terminates exactly on `ImpactPosition` at `t = 1`.
- `1 - t^2` falls slowly at first and fastest at the end, which is the ground-rush the beats need. The obvious `(1 - t)^2` does exactly the opposite.

Heading comes from the analytic tangent, so the hull points where it is going.

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Arrival/Core/ArrivalTrajectory.cs`
- Test: `Assets/Game/Editor/Tests/ArrivalTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Game/Editor/Tests/ArrivalTests.cs`:

```csharp
// What the arrival has to keep being true about itself.
//
// The failures worth catching here are the silent ones. A trajectory that overshoots its lateral
// budget still flies and still lands — it just drags the world streamer through chunks nobody
// asked for, which shows up as a stutter on somebody else's machine and never as an error. A
// terminal pose that is a degree off level still looks like a landing in the editor and leaves the
// wreck resting on one wing forever, because the hull is persisted where the trajectory left it.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class ArrivalTrajectoryTests
    {
        /// <summary>Metres of float drift treated as noise rather than as a miss.</summary>
        private const float PositionTolerance = 0.001f;

        /// <summary>Degrees of float drift treated as level.</summary>
        private const float AngleTolerance = 0.01f;

        private static ArrivalPath Path()
        {
            ArrivalPath path = ArrivalPath.Default;
            path.ImpactPosition = new Vector3(120f, 30f, -75f);
            return path;
        }

        [Test]
        public void StartsAtTheConfiguredAltitude()
        {
            ArrivalPath path = Path();

            ArrivalTrajectory.Evaluate(0f, path, out Vector3 position, out Quaternion _);

            Assert.AreEqual(path.ImpactPosition.y + path.StartAltitude, position.y, PositionTolerance);
        }

        [Test]
        public void EndsExactlyOnTheImpactPosition()
        {
            ArrivalPath path = Path();

            ArrivalTrajectory.Evaluate(1f, path, out Vector3 position, out Quaternion _);

            Assert.Less(Vector3.Distance(path.ImpactPosition, position), PositionTolerance,
                        "The wreck is persisted where the trajectory leaves it, so a terminal " +
                        "position that is merely close is a hull buried in, or hovering above, the ground.");
        }

        [Test]
        public void LandsLevel()
        {
            ArrivalPath path = Path();

            ArrivalTrajectory.Evaluate(1f, path, out Vector3 _, out Quaternion rotation);

            Vector3 euler = rotation.eulerAngles;
            float roll = Mathf.DeltaAngle(0f, euler.z);
            float pitch = Mathf.DeltaAngle(0f, euler.x);

            Assert.AreEqual(0f, roll, AngleTolerance, "Bank must unwind to zero or the wreck rests on a wing.");
            Assert.AreEqual(0f, pitch, AngleTolerance, "Pitch must flare to zero or the wreck stands on its nose.");
        }

        [Test]
        public void DescendsMonotonically()
        {
            ArrivalPath path = Path();
            float previous = float.MaxValue;

            for (int i = 0; i <= 200; i++)
            {
                ArrivalTrajectory.Evaluate(i / 200f, path, out Vector3 position, out Quaternion _);

                Assert.Less(position.y, previous + PositionTolerance,
                            $"Altitude rose between samples at t={i / 200f}. The descent must never climb.");
                previous = position.y;
            }
        }

        [Test]
        public void NeverExceedsTheLateralBudget()
        {
            ArrivalPath path = Path();

            for (int i = 0; i <= 200; i++)
            {
                ArrivalTrajectory.Evaluate(i / 200f, path, out Vector3 position, out Quaternion _);

                Vector2 offset = new(position.x - path.ImpactPosition.x, position.z - path.ImpactPosition.z);

                Assert.LessOrEqual(offset.magnitude, path.LateralBudget + PositionTolerance,
                                   "Lateral budget is a world-streaming limit, not a suggestion.");
            }
        }

        [Test]
        public void CurvesRatherThanDivingStraight()
        {
            ArrivalPath path = Path();

            ArrivalTrajectory.Evaluate(0f, path, out Vector3 start, out Quaternion _);
            ArrivalTrajectory.Evaluate(0.5f, path, out Vector3 middle, out Quaternion _);

            Vector2 a = new(start.x, start.z);
            Vector2 b = new(path.ImpactPosition.x, path.ImpactPosition.z);
            Vector2 m = new(middle.x, middle.z);

            // Distance from the midpoint to the straight line joining start and impact. A dead
            // straight dive puts this at zero; the brief explicitly asked for a curve.
            Vector2 line = b - a;
            float deviation = Mathf.Abs(line.x * (a.y - m.y) - (a.x - m.x) * line.y) / line.magnitude;

            Assert.Greater(deviation, 1f, "The path must be an arc, not a straight line.");
        }

        [Test]
        public void ZeroSweepIsStillValid()
        {
            ArrivalPath path = Path();
            path.SweepDegrees = 0f;

            ArrivalTrajectory.Evaluate(1f, path, out Vector3 position, out Quaternion rotation);

            Assert.Less(Vector3.Distance(path.ImpactPosition, position), PositionTolerance);
            Assert.IsFalse(float.IsNaN(rotation.x), "A straight-in approach must not produce a NaN heading.");
        }
    }
}
```

- [ ] **Step 2: Verify the tests fail**

```bash
python3 tools/typecheck.py
```

Expected: FAIL, with errors naming `ArrivalTrajectory` as not found (the test file references a type that does not exist yet). Note the test file lives under `Assets/Game/Editor`, which `typecheck.py` excludes — so this step will actually report `No errors.` and the real failure appears in the Editor's Test Runner as a compile error in the Editor assembly. Confirm there by opening the Test Runner; the EditMode suite will refuse to run until Task 2 Step 3 lands.

- [ ] **Step 3: Write the implementation**

Create `Assets/Game/Scripts/Gameplay/Arrival/Core/ArrivalTrajectory.cs`:

```csharp
using UnityEngine;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Where the ship is, and which way it is pointing, at a given point through its descent.
    ///
    /// <para>
    /// Pure and closed-form — no Unity state, no integration, no time step. That is what lets the
    /// shape be unit-tested at all, and it is also what makes the terminal pose EXACT: the wreck is
    /// persisted wherever the descent leaves it, so an integrator that landed near the impact point
    /// would bury the hull or hover it, permanently, in the save file.
    /// </para>
    ///
    /// <para>
    /// The shape is a descending spiral. Horizontal radius shrinks linearly from the lateral budget
    /// to zero while the bearing sweeps; altitude falls as one-minus-t-squared. Chosen over the
    /// obvious Bezier because the budget is then respected BY CONSTRUCTION rather than by hoping a
    /// control point stays inside it — and the budget is a world-streaming limit, so exceeding it
    /// is a frame-rate problem on somebody else's machine rather than a visible bug on yours.
    /// </para>
    ///
    /// <para>
    /// The altitude curve is one-minus-t-squared and NOT the more obvious one-minus-t, squared.
    /// They look alike and behave oppositely: this one falls slowly at first and fastest at the
    /// end, which is the ground rush the sequence is built around, while the other dumps all its
    /// speed at the top and drifts in.
    /// </para>
    /// </summary>
    public static class ArrivalTrajectory
    {
        /// <summary>
        /// The pose at <paramref name="t"/>, which is clamped to the zero-to-one range so a caller
        /// that overshoots its own timer gets the terminal pose rather than an extrapolated one
        /// somewhere under the terrain.
        /// </summary>
        public static void Evaluate(float t, in ArrivalPath path,
                                    out Vector3 position, out Quaternion rotation)
        {
            t = Mathf.Clamp01(t);

            float radius = path.LateralBudget * (1f - t);
            float bearing = (path.StartBearing + path.SweepDegrees * t) * Mathf.Deg2Rad;

            float sin = Mathf.Sin(bearing);
            float cos = Mathf.Cos(bearing);

            position = new Vector3(
                path.ImpactPosition.x + radius * sin,
                path.ImpactPosition.y + path.StartAltitude * (1f - t * t),
                path.ImpactPosition.z + radius * cos);

            rotation = Quaternion.Euler(
                path.MaxPitchDegrees * (1f - t),
                HeadingDegrees(t, path),
                -path.MaxBankDegrees * (1f - t));
        }

        /// <summary>
        /// The direction of travel, so the hull points where it is going.
        ///
        /// <para>
        /// Differentiated by hand rather than sampled two frames apart, because a finite difference
        /// would make this depend on a step size that the pure form does not have — and because the
        /// obvious sample point, t plus epsilon, does not exist at the end of the descent.
        /// </para>
        /// <para>
        /// The radius reaching zero at impact does NOT produce a singularity: both derivative
        /// components carry the lateral budget as a factor, so the heading there is simply the
        /// bearing the ship came in on, reversed. A zero lateral budget would be a genuine
        /// degenerate case, and is refused by <see cref="ArrivalDirector"/> rather than papered
        /// over here.
        /// </para>
        /// </summary>
        private static float HeadingDegrees(float t, in ArrivalPath path)
        {
            float bearing = (path.StartBearing + path.SweepDegrees * t) * Mathf.Deg2Rad;
            float sweepRate = path.SweepDegrees * Mathf.Deg2Rad;
            float radius = path.LateralBudget * (1f - t);

            // d/dt of the spiral, with radius shrinking at -LateralBudget per unit t.
            float dx = -path.LateralBudget * Mathf.Sin(bearing) + radius * sweepRate * Mathf.Cos(bearing);
            float dz = -path.LateralBudget * Mathf.Cos(bearing) - radius * sweepRate * Mathf.Sin(bearing);

            return Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        }
    }
}
```

- [ ] **Step 4: Type-check**

```bash
python3 tools/typecheck.py
```

Expected: `No errors.`

- [ ] **Step 5: RUN TESTS**

In the Unity Editor: `Window > General > Test Runner > EditMode > Run All`.

Expected: all seven `ArrivalTrajectoryTests` pass.

If `LandsLevel` fails on roll with a value near 360, the test's `Mathf.DeltaAngle` is doing its job and the implementation is producing a tiny negative angle — that is fine and within tolerance. A failure with a value near 22 means the bank term is not being unwound by `(1 - t)`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Arrival/Core/ArrivalTrajectory.cs Assets/Game/Editor/Tests/ArrivalTests.cs
git commit -m "feat: add ArrivalTrajectory, a closed-form descending spiral"
```

---

## Task 3: `SeatOrdering` — who sits where

Ordering is pure integer work, deliberately: `ShipSeat.Order` is a private serialized field with no setter, so a test cannot build a meaningful seat without `SerializedObject` gymnastics. Sorting indices instead keeps the part with the subtle requirement — **stability** — testable with plain ints.

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Arrival/Core/SeatOrdering.cs`
- Modify: `Assets/Game/Editor/Tests/ArrivalTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `Assets/Game/Editor/Tests/ArrivalTests.cs`, inside the `SpaceGame.EditorTools` namespace, after the closing brace of `ArrivalTrajectoryTests`:

```csharp
    public class SeatOrderingTests
    {
        [Test]
        public void SortsByOrder()
        {
            int[] result = SeatOrdering.OrderedIndices(new[] { 30, 10, 20 });

            Assert.AreEqual(new[] { 1, 2, 0 }, result);
        }

        [Test]
        public void TiesKeepHierarchyOrder()
        {
            // Every seat left at the default zero is the common case: somebody added ShipSeat
            // components and never touched the order field. Those must fill top to bottom, which is
            // what the person who never touched the field would expect. An unstable sort satisfies
            // "sorted by order" while filling them arbitrarily.
            int[] result = SeatOrdering.OrderedIndices(new[] { 0, 0, 0, 0 });

            Assert.AreEqual(new[] { 0, 1, 2, 3 }, result);
        }

        [Test]
        public void PartialTiesKeepHierarchyOrderWithinEachGroup()
        {
            int[] result = SeatOrdering.OrderedIndices(new[] { 5, 1, 5, 1 });

            Assert.AreEqual(new[] { 1, 3, 0, 2 }, result);
        }

        [Test]
        public void MorePlayersThanSeatsWraps()
        {
            // A twelve-strong crew in a seven-seat hull is a fair thing to have. Two players
            // briefly sharing a pose push apart on the next physics step; a player handed no seat
            // at all does not recover on its own.
            Assert.AreEqual(0, SeatOrdering.SeatFor(claim: 0, seatCount: 3));
            Assert.AreEqual(2, SeatOrdering.SeatFor(claim: 2, seatCount: 3));
            Assert.AreEqual(0, SeatOrdering.SeatFor(claim: 3, seatCount: 3));
            Assert.AreEqual(1, SeatOrdering.SeatFor(claim: 7, seatCount: 3));
        }

        [Test]
        public void NoSeatsIsRefusedRatherThanDividedByZero()
        {
            Assert.AreEqual(-1, SeatOrdering.SeatFor(claim: 0, seatCount: 0));
        }

        [Test]
        public void EmptyInputIsEmptyOutput()
        {
            Assert.IsEmpty(SeatOrdering.OrderedIndices(new int[0]));
        }
    }
```

Add `using System;` is not needed; the file already has what it needs.

- [ ] **Step 2: Verify the tests fail**

Open the Unity Test Runner. Expected: the Editor assembly fails to compile, naming `SeatOrdering` as not found.

- [ ] **Step 3: Write the implementation**

Create `Assets/Game/Scripts/Gameplay/Arrival/Core/SeatOrdering.cs`:

```csharp
using System.Collections.Generic;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Which seat each arriving player gets.
    ///
    /// <para>
    /// Integers rather than <c>ShipSeat</c> components, so the one part with a subtle requirement —
    /// stability — can be tested without building a GameObject hierarchy. The caller maps the
    /// returned indices back onto its own list.
    /// </para>
    ///
    /// <para>
    /// The rules are lifted from <c>VersusShipSpawner.Seats.cs</c>, which arrived at them first and
    /// for the same reasons. They are re-implemented here rather than shared because that class
    /// resolves seats against a live ship and claims them as a side effect, which is exactly what a
    /// pure ordering helper must not do.
    /// </para>
    /// </summary>
    public static class SeatOrdering
    {
        /// <summary>
        /// Indices into <paramref name="seatOrders"/>, lowest order first, ties keeping their
        /// original position.
        ///
        /// <para>
        /// Insertion sort, not <c>List.Sort</c>. The framework sort is unstable, so seats sharing an
        /// order — which is every seat on a ship nobody has bothered to number — would fill in an
        /// arbitrary sequence that changes between runs. Over a handful of seats the cost is
        /// nothing and the guarantee is the entire point.
        /// </para>
        /// </summary>
        public static int[] OrderedIndices(IReadOnlyList<int> seatOrders)
        {
            int count = seatOrders?.Count ?? 0;
            var indices = new int[count];

            for (int i = 0; i < count; i++) indices[i] = i;

            for (int i = 1; i < count; i++)
            {
                int index = indices[i];
                int order = seatOrders[index];
                int j = i - 1;

                while (j >= 0 && seatOrders[indices[j]] > order)
                {
                    indices[j + 1] = indices[j];
                    j--;
                }

                indices[j + 1] = index;
            }

            return indices;
        }

        /// <summary>
        /// Which of <paramref name="seatCount"/> seats the <paramref name="claim"/>-th arrival
        /// takes, or -1 when there are no seats to take.
        ///
        /// <para>
        /// Wraps rather than refusing. More players than seats is a fair thing to have, and two
        /// bodies briefly sharing a pose push apart on the next physics step — where a player
        /// handed no seat at all is left standing in the sky.
        /// </para>
        /// <para>
        /// The no-seat case returns -1 instead of dividing by zero, and callers are expected to
        /// treat it as the loud failure it is rather than clamping it to seat zero.
        /// </para>
        /// </summary>
        public static int SeatFor(int claim, int seatCount)
        {
            if (seatCount <= 0) return -1;

            return ((claim % seatCount) + seatCount) % seatCount;
        }
    }
}
```

- [ ] **Step 4: Type-check**

```bash
python3 tools/typecheck.py
```

Expected: `No errors.`

- [ ] **Step 5: RUN TESTS**

Expected: all six `SeatOrderingTests` pass, and the seven from Task 2 still pass.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Arrival/Core/SeatOrdering.cs Assets/Game/Editor/Tests/ArrivalTests.cs
git commit -m "feat: add SeatOrdering, stable seat assignment for arrivals"
```

---

## Task 4: `GameSettings.CameraShakeIntensity`

**Files:**
- Modify: `Assets/Game/Scripts/Core/Settings/GameSettings.cs`

Follow the existing property shape in that file exactly: a backing field, lazy load, a setter that writes PlayerPrefs and raises `Changed`.

- [ ] **Step 1: Read the file to find the pattern**

```bash
grep -n "mouseSensitivity\|MouseSensitivity\|SchemaVersion\|private static void Load\|Save(" Assets/Game/Scripts/Core/Settings/GameSettings.cs
```

Match whatever shape `MouseSensitivity` uses — it is the closest analogue (a clamped float with min/max constants).

- [ ] **Step 2: Add the constant and backing field**

Next to `MinSensitivity` / `MaxSensitivity`, add:

```csharp
        /// <summary>
        /// Screen shake is the most over-applied juice technique there is, and the arrival crash
        /// shakes for several seconds with no way to skip it — which is a motion-sickness and
        /// vestibular-accessibility exposure, not a polish dial. Zero is a supported value and must
        /// stay one.
        /// </summary>
        public const float MinCameraShake = 0f;
        public const float MaxCameraShake = 1f;
```

Next to `private static float mouseSensitivity;`, add:

```csharp
        private static float cameraShakeIntensity;
```

- [ ] **Step 3: Add the property**

Immediately after the `MouseSensitivity` property, add:

```csharp
        /// <summary>
        /// How hard camera shake hits, from 0 (off) to 1 (full). Multiplies every shake in the
        /// game; it is not specific to the arrival.
        /// </summary>
        public static float CameraShakeIntensity
        {
            get { Load(); return cameraShakeIntensity; }
            set
            {
                Load();
                float clamped = Mathf.Clamp(value, MinCameraShake, MaxCameraShake);
                if (Mathf.Approximately(clamped, cameraShakeIntensity)) return;

                cameraShakeIntensity = clamped;
                PlayerPrefs.SetFloat(Prefix + nameof(CameraShakeIntensity), clamped);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }
```

If the file's existing properties call something other than `Load()` (for example `EnsureLoaded()`), use that name instead — match the file, do not introduce a second spelling.

- [ ] **Step 4: Seed the default in the loader**

In the method that reads PlayerPrefs into the backing fields, alongside the `mouseSensitivity` line, add:

```csharp
            cameraShakeIntensity = PlayerPrefs.GetFloat(Prefix + nameof(CameraShakeIntensity), 1f);
```

Default 1, so existing players get the full effect and only somebody who turns it down gets less.

- [ ] **Step 5: Type-check**

```bash
python3 tools/typecheck.py
```

Expected: `No errors.`

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Core/Settings/GameSettings.cs
git commit -m "feat: add a camera shake intensity setting, zero included"
```

---

## Task 5: `ShakeMath` — capped shake

**Files:**
- Create: `Assets/Game/Scripts/Presentation/Cutscenes/Core/ShakeMath.cs`
- Modify: `Assets/Game/Editor/Tests/ArrivalTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `Assets/Game/Editor/Tests/ArrivalTests.cs`, inside the namespace:

```csharp
    public class ShakeMathTests
    {
        private const float MaxTranslation = 0.15f;

        [Test]
        public void ZeroSettingsScaleIsExactlyStill()
        {
            // The accessibility guarantee. "Nearly zero" is not off — a player who turned shake off
            // because it makes them ill must get a camera that does not move at all.
            Vector3 offset = ShakeMath.Displacement(intensity: 1f, settingsScale: 0f,
                                                    maxTranslation: MaxTranslation,
                                                    time: 3.7f, frequency: 18f);

            Assert.AreEqual(Vector3.zero, offset);
        }

        [Test]
        public void ZeroIntensityIsExactlyStill()
        {
            Vector3 offset = ShakeMath.Displacement(intensity: 0f, settingsScale: 1f,
                                                    maxTranslation: MaxTranslation,
                                                    time: 3.7f, frequency: 18f);

            Assert.AreEqual(Vector3.zero, offset);
        }

        [Test]
        public void NeverExceedsTheCap()
        {
            for (int i = 0; i <= 500; i++)
            {
                float time = i * 0.037f;

                Vector3 offset = ShakeMath.Displacement(intensity: 1f, settingsScale: 1f,
                                                        maxTranslation: MaxTranslation,
                                                        time: time, frequency: 23f);

                Assert.LessOrEqual(offset.magnitude, MaxTranslation + 0.0001f,
                                   $"Shake exceeded its cap at t={time}. An uncapped shake is an " +
                                   "unreadable frame.");
            }
        }

        [Test]
        public void OutOfRangeIntensityIsClampedRatherThanAmplified()
        {
            Vector3 offset = ShakeMath.Displacement(intensity: 50f, settingsScale: 1f,
                                                    maxTranslation: MaxTranslation,
                                                    time: 1.3f, frequency: 18f);

            Assert.LessOrEqual(offset.magnitude, MaxTranslation + 0.0001f);
        }

        [Test]
        public void IsContinuousInTime()
        {
            // Perlin noise is smooth; a shake built on Random would not be, and would read as a
            // camera glitching rather than a hull shaking.
            Vector3 a = ShakeMath.Displacement(1f, 1f, MaxTranslation, 2.000f, 18f);
            Vector3 b = ShakeMath.Displacement(1f, 1f, MaxTranslation, 2.001f, 18f);

            Assert.Less(Vector3.Distance(a, b), MaxTranslation * 0.25f,
                        "Adjacent samples jumped. That is a glitch, not a shake.");
        }
    }
```

- [ ] **Step 2: Verify the tests fail**

Open the Test Runner. Expected: compile error naming `ShakeMath`.

- [ ] **Step 3: Write the implementation**

Create `Assets/Game/Scripts/Presentation/Cutscenes/Core/ShakeMath.cs`:

```csharp
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// How far to push a camera to sell a shake.
    ///
    /// <para>
    /// Pure, so the two properties that matter can be tested without a scene: the displacement is
    /// CAPPED, and a player who has turned shake off gets exactly zero rather than nearly zero.
    /// Both are easy to get subtly wrong and impossible to notice by eye — an uncapped shake looks
    /// fine until two sources overlap, and a "nearly off" shake still makes a susceptible player
    /// ill.
    /// </para>
    ///
    /// <para>
    /// Perlin rather than Random deliberately. Random gives a new value per sample and reads as the
    /// camera glitching; Perlin is continuous, so the camera moves like a thing with mass.
    /// </para>
    /// </summary>
    public static class ShakeMath
    {
        // Arbitrary but fixed sampling lanes through the noise field. Three different offsets so
        // the axes do not move in lockstep, which would turn a shake into a diagonal slide.
        private const float LaneX = 0f;
        private const float LaneY = 137f;
        private const float LaneZ = 311f;

        /// <summary>
        /// The offset to add to the camera's local position.
        ///
        /// <para>
        /// <paramref name="intensity"/> is the caller's own curve through the event — the arrival
        /// ramps it up and spikes it at impact. <paramref name="settingsScale"/> is the player's
        /// preference and is applied last, so nothing a caller does can shake a camera belonging to
        /// somebody who asked it not to.
        /// </para>
        /// </summary>
        public static Vector3 Displacement(float intensity, float settingsScale,
                                           float maxTranslation, float time, float frequency)
        {
            float scale = Mathf.Clamp01(intensity) * Mathf.Clamp01(settingsScale);

            // Not an optimisation. Perlin noise is 0.5 at its sample origin rather than 0, so the
            // arithmetic below yields a small constant offset at zero intensity — a camera sitting
            // permanently off-centre for a player who turned shake off.
            if (scale <= 0f) return Vector3.zero;

            float t = time * frequency;

            // Perlin returns roughly 0..1 with a mean of 0.5, so it is recentred to roughly -1..1.
            float x = Mathf.PerlinNoise(t, LaneX) * 2f - 1f;
            float y = Mathf.PerlinNoise(t, LaneY) * 2f - 1f;
            float z = Mathf.PerlinNoise(t, LaneZ) * 2f - 1f;

            Vector3 direction = new(x, y, z);

            // Clamped rather than scaled: the three axes are independent, so their combined
            // magnitude can reach the square root of three even though each is within range.
            // Normalising instead would make the shake a constant-radius orbit, which reads as a
            // wobble rather than a rattle.
            return Vector3.ClampMagnitude(direction * (maxTranslation * scale), maxTranslation * scale);
        }
    }
}
```

- [ ] **Step 4: Type-check**

```bash
python3 tools/typecheck.py
```

Expected: `No errors.`

- [ ] **Step 5: RUN TESTS**

Expected: all five `ShakeMathTests` pass, plus the thirteen from Tasks 2 and 3.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Presentation/Cutscenes/Core/ShakeMath.cs Assets/Game/Editor/Tests/ArrivalTests.cs
git commit -m "feat: add ShakeMath, capped and honestly zeroable camera shake"
```

---

## Task 6: `NetMsg` ids for seating

**Files:**
- Modify: `Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs`

The file's own rule, stated at the top: **ids are only ever appended, never reused,** because they travel over the wire between builds. The highest existing id is 89 (`SnareFreed`).

- [ ] **Step 1: Append the ids**

At the end of the class, after the net gun block, add:

```csharp
        // ── Arrival ──
        // Sent on the SHIP's channel: the ship is the thing with seats, it is a spawned
        // NetworkObject with a relay of its own, and it outlives any single player's seating.
        //
        // Two channels here for the reason MountNetworkSync documents at length. This pair is the
        // EVENT, acted on immediately by everyone present. SeatedRider's own NetworkVariable is the
        // STATE, and it exists because NetworkVariable change events never replay: a client that
        // connects while the ship is already falling was not here for the event, and without the
        // state channel it spawns standing on the ground watching its crew drop out of the sky.
        //
        //   Target  the player being seated or released.
        //   A       which seat, as an index into the ship's ordered ShipSeat list.
        public const ushort TakeSeat  = 90; // server → everyone
        public const ushort LeaveSeat = 91; // server → everyone
```

- [ ] **Step 2: Verify uniqueness**

```bash
grep -n "public const ushort" Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs
```

Confirm 90 and 91 appear exactly once each and no other id shares them. `NetMessagingTests` asserts this too.

- [ ] **Step 3: Type-check**

```bash
python3 tools/typecheck.py
```

Expected: `No errors.`

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs
git commit -m "feat: add TakeSeat and LeaveSeat message ids"
```

---

## Task 7: `SeatedRider` — replicated seating

The heart of the feature. Copies `MountNetworkSync`'s two-channel shape.

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Arrival/Runtime/SeatedRider.cs`

- [ ] **Step 1: Write the implementation**

Create `Assets/Game/Scripts/Gameplay/Arrival/Runtime/SeatedRider.cs`:

```csharp
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Puts players in this ship's seats, on every machine, and takes them out again.
    ///
    /// <para>
    /// This is the ATTACH half of <c>MountModule</c> and deliberately nothing else — no steering,
    /// no mount camera, no dismount placement, no collision-ignore bookkeeping. Making
    /// <c>MountModule</c> carry more than one rider was the alternative, and it is entangled with
    /// all of those; every vehicle and creature in the game depends on it, so widening it to serve
    /// one cutscene is how the ostrich, the crawler and the foil sailer break together.
    /// </para>
    ///
    /// <para>
    /// <b>Two channels, answering different questions</b> — the same split
    /// <c>MountNetworkSync</c> documents:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="NetMsg.TakeSeat"/> is the EVENT. Everyone present acts on it at once.</item>
    /// <item><see cref="occupants"/> is the STATE. NetworkVariable change events never replay, so a
    /// client connecting mid-descent has nothing else to go on — the event was sent before it
    /// existed. It also re-asserts every frame, so it repairs the seat whatever went wrong.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public class SeatedRider : NetworkBehaviour
    {
        [Tooltip("How deep a rider sits into the seat point, in the seat point's local space. The " +
                 "player pivot sits roughly a metre above the soles, so seating a body AT the " +
                 "marker leaves it standing with its feet through the floor. Same field, same " +
                 "reason, as MountModule.seatOffset.")]
        [SerializeField] private Vector3 seatOffset = new(0f, -0.9f, 0f);

        /// <summary>
        /// Who is in each seat, by NetworkObjectId; 0 for empty. Index is the seat's position in
        /// the ordered <see cref="ShipSeat"/> list.
        ///
        /// <para>
        /// Server-write, because seating is a server decision and this is the RECORD of it rather
        /// than a second way of making one. Nothing acts on a write to it except a peer bringing
        /// its own copy of the ship into line.
        /// </para>
        /// </summary>
        private readonly NetworkList<ulong> occupants = new();

        /// <summary>Seats in the order <see cref="SeatOrdering"/> put them, resolved once.</summary>
        private readonly List<ShipSeat> seats = new();

        /// <summary>What each seated body's Rigidbody was before we froze it, so release can undo it.</summary>
        private readonly Dictionary<ulong, RiderPhysics> captured = new();

        private bool seatsResolved;

        private readonly struct RiderPhysics
        {
            public readonly bool WasKinematic;
            public readonly bool HadGravity;
            public readonly Transform OriginalParent;

            public RiderPhysics(bool wasKinematic, bool hadGravity, Transform originalParent)
            {
                WasKinematic = wasKinematic;
                HadGravity = hadGravity;
                OriginalParent = originalParent;
            }
        }

        /// <summary>How many seats this ship actually has. Zero means it has no markers at all.</summary>
        public int SeatCount
        {
            get { ResolveSeats(); return seats.Count; }
        }

        public override void OnNetworkSpawn()
        {
            this.NetOn(NetMsg.TakeSeat, OnTakeSeat);
            this.NetOn(NetMsg.LeaveSeat, OnLeaveSeat);

            ResolveSeats();

            // The server fills the list once; clients receive it. Sized up front so an index is
            // always addressable and "empty" is a value rather than a missing element.
            if (IsServer && occupants.Count == 0)
                for (int i = 0; i < seats.Count; i++) occupants.Add(0UL);

            // Late joiner: the events fired before this machine existed, so the state channel is
            // the only account of who is already sitting down.
            ApplyStateChannel();
        }

        public override void OnNetworkDespawn()
        {
            this.NetOff(NetMsg.TakeSeat, OnTakeSeat);
            this.NetOff(NetMsg.LeaveSeat, OnLeaveSeat);
        }

        /// <summary>
        /// Server-only. Seats <paramref name="player"/> in <paramref name="seatIndex"/> and tells
        /// everyone. False when there is no such seat, which the caller must treat as the loud
        /// failure it is.
        /// </summary>
        public bool Seat(GameObject player, int seatIndex)
        {
            if (!IsServer)
            {
                Debug.LogError("[SeatedRider] Seat called on a client. Seating is a server decision.", this);
                return false;
            }

            ResolveSeats();

            if (seatIndex < 0 || seatIndex >= seats.Count)
            {
                Debug.LogError($"[SeatedRider] Seat {seatIndex} does not exist on '{name}' — it has " +
                               $"{seats.Count} seat(s).", this);
                return false;
            }

            if (player == null)
            {
                Debug.LogError("[SeatedRider] Seat called with no player.", this);
                return false;
            }

            occupants[seatIndex] = IdOf(player);

            // Broadcast to All rather than Others so the server runs the same attach path every
            // peer does, instead of a private one that can drift from it.
            this.NetToAll(NetMsg.TakeSeat, new NetArg(a: seatIndex).With(player));
            return true;
        }

        /// <summary>Server-only. Empties every seat and tells everyone.</summary>
        public void ReleaseAll()
        {
            if (!IsServer) return;

            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] == 0UL) continue;

                GameObject player = ResolveById(occupants[i]);
                occupants[i] = 0UL;

                if (player != null)
                    this.NetToAll(NetMsg.LeaveSeat, new NetArg(a: i).With(player));
            }
        }

        /// <summary>Server-only. Empties whichever seat this player is in. For a mid-descent disconnect.</summary>
        public void Release(GameObject player)
        {
            if (!IsServer || player == null) return;

            ulong id = IdOf(player);

            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] != id) continue;

                occupants[i] = 0UL;
                this.NetToAll(NetMsg.LeaveSeat, new NetArg(a: i).With(player));
                return;
            }
        }

        private void OnTakeSeat(in NetArg arg)
        {
            GameObject player = arg.Resolve();
            if (player == null) return;

            Attach(player, arg.A);
        }

        private void OnLeaveSeat(in NetArg arg)
        {
            GameObject player = arg.Resolve();
            if (player == null) return;

            Detach(player);
        }

        /// <summary>
        /// Brings this machine's copy of the ship into line with the state channel. Idempotent, as
        /// every replicated apply in this project is required to be — a machine that missed an
        /// event is corrected by the next pass rather than double-applying.
        /// </summary>
        private void ApplyStateChannel()
        {
            ResolveSeats();

            for (int i = 0; i < occupants.Count && i < seats.Count; i++)
            {
                if (occupants[i] == 0UL) continue;

                GameObject player = ResolveById(occupants[i]);
                if (player == null) continue;

                // Already in the right seat: nothing to do. This is what makes the pass safe to run
                // repeatedly.
                if (player.transform.parent == seats[i].transform) continue;

                Attach(player, i);
            }
        }

        private void Attach(GameObject player, int seatIndex)
        {
            ResolveSeats();

            if (seatIndex < 0 || seatIndex >= seats.Count) return;

            Transform seat = seats[seatIndex].transform;
            ulong id = IdOf(player);

            if (!captured.ContainsKey(id))
            {
                var body = player.GetComponent<Rigidbody>();
                captured[id] = new RiderPhysics(
                    body != null && body.isKinematic,
                    body != null && body.useGravity,
                    player.transform.parent);

                if (body != null)
                {
                    // Kinematic, or the body keeps falling under gravity while the seat it is
                    // parented to flies away underneath it. Every mount in this project does the
                    // same to its rider, for the same reason.
                    body.isKinematic = true;
                    body.useGravity = false;
                }
            }

            player.transform.SetParent(seat, worldPositionStays: false);
            player.transform.localPosition = seatOffset;
            player.transform.localRotation = Quaternion.identity;
        }

        private void Detach(GameObject player)
        {
            ulong id = IdOf(player);

            if (!captured.TryGetValue(id, out RiderPhysics before)) return;
            captured.Remove(id);

            // Kept in world space: the ship has landed and the seat is where the player should
            // physically be. worldPositionStays false here would snap them to wherever the original
            // parent happens to be, which is the scene root at the origin.
            player.transform.SetParent(before.OriginalParent, worldPositionStays: true);

            var body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = before.WasKinematic;
                body.useGravity = before.HadGravity;
            }
        }

        /// <summary>
        /// This ship's seats, ordered. Resolved once and kept, because re-resolving mid-descent
        /// could renumber the seats underneath players already sitting in them.
        /// </summary>
        private void ResolveSeats()
        {
            if (seatsResolved) return;
            seatsResolved = true;

            var found = new List<ShipSeat>();
            GetComponentsInChildren(includeInactive: true, found);

            var orders = new int[found.Count];
            for (int i = 0; i < found.Count; i++) orders[i] = found[i].Order;

            foreach (int index in SeatOrdering.OrderedIndices(orders))
                seats.Add(found[index]);

            if (seats.Count == 0)
                Debug.LogError($"[SeatedRider] '{name}' has no ShipSeat markers, so nobody can be " +
                               "seated in it.", this);
        }

        private void Update()
        {
            // The state channel re-asserts itself, so a seat broken by anything — a missed event, a
            // late spawn, an object that arrived after its own seating message — repairs itself on
            // the next frame rather than staying broken for the rest of the descent.
            if (IsSpawned) ApplyStateChannel();
        }

        private static ulong IdOf(GameObject go)
        {
            var netObj = go != null ? go.GetComponent<NetworkObject>() : null;
            return netObj != null && netObj.IsSpawned ? netObj.NetworkObjectId : 0UL;
        }

        private static GameObject ResolveById(ulong id)
        {
            if (id == 0UL || !Network.IsNetworked) return null;

            var spawned = NetworkManager.Singleton.SpawnManager;
            if (spawned == null) return null;

            return spawned.SpawnedObjects.TryGetValue(id, out NetworkObject obj) && obj != null
                ? obj.gameObject
                : null;
        }
    }
}
```

- [ ] **Step 2: Type-check**

```bash
python3 tools/typecheck.py
```

Expected: `No errors.`

If `NetToAll` or `NetOn` are not found, check the `using SpaceGame.Core;` line — they are extension methods on `Component` declared in `NetMessaging`.

If `NetworkList<ulong>` errors, confirm `using Unity.Netcode;` is present.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Arrival/Runtime/SeatedRider.cs
git commit -m "feat: add SeatedRider, replicated multi-seat attachment"
```

---

## Task 8: `ArrivalCameraRig` — seated free look plus shake

**Files:**
- Create: `Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCameraRig.cs`

Note the design decision this encodes: it reads the raw `InputAction` rather than going through `PlayerInputManager`, exactly as `MountModule.Camera.cs` does.

- [ ] **Step 1: Write the implementation**

Create `Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCameraRig.cs`:

```csharp
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
    /// that is what stops them walking out of the seat — and <c>PlayerInputManager</c> zeroes its
    /// look axis in <c>OnDisable</c>, so anything reading <c>LookInput</c> gets a permanently still
    /// camera. Leaving the input component enabled instead is worse: jump and dash are delivered as
    /// events whose handlers fire regardless of <c>PlayerMovement.enabled</c>, so the player would
    /// be able to leap out of the chair. <c>MountModule.Camera.cs</c> hit this first and solved it
    /// by going to <c>InputSystem.actions</c> directly; this does the same.
    /// </para>
    ///
    /// <para>
    /// <b>Why it does not use <c>PlayerLook</c>.</b> That component spends its yaw by turning the
    /// player's RIGIDBODY in FixedUpdate. The body here is parented into a seat on a moving ship
    /// and has been made kinematic; having it also fight for its own rotation is a conflict with no
    /// upside. Pitch and yaw are applied to the camera alone.
    /// </para>
    ///
    /// <para>
    /// Look and shake are one component and one LateUpdate on purpose. As two components they would
    /// both write the same transform in an order Unity does not define, and the loser's
    /// contribution would vanish on an arbitrary subset of frames.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ArrivalCameraRig : MonoBehaviour
    {
        [Tooltip("Degrees per second of view movement per unit of look input.")]
        [SerializeField] private float lookSensitivity = 180f;

        [Tooltip("How far up and down the view may travel from the seat's forward. Stops a seated " +
                 "player rolling their view past vertical, which reads as a bug rather than a look.")]
        [SerializeField] private float pitchClamp = 75f;

        [Tooltip("How far left and right. Generous — the point is to see the cabin and your crew — " +
                 "but not unlimited, because a seated body cannot actually turn to look behind itself.")]
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
        /// How hard to shake right now, 0..1. Driven by <see cref="ArrivalCutscene"/>'s beats; the
        /// player's own intensity preference is applied on top, inside <see cref="ShakeMath"/>.
        /// </summary>
        public float ShakeIntensity { get; set; }

        private void OnEnable()
        {
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
            // else enabled is how the player ends up unable to look around after the cutscene.
            if (forcedLookAction && lookAction != null)
            {
                lookAction.Disable();
                forcedLookAction = false;
            }

            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
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

            // Rotation shake reuses the same displacement so the two stay in phase — a camera whose
            // position and angle rattle out of step reads as two separate faults.
            float rotationScale = maxShakeTranslation > 0f
                ? maxShakeRotation / maxShakeTranslation
                : 0f;

            transform.localPosition = offset;
            transform.localRotation = Quaternion.Euler(pitch + offset.y * rotationScale,
                                                       yaw + offset.x * rotationScale,
                                                       offset.z * rotationScale);
        }
    }
}
```

- [ ] **Step 2: Type-check**

```bash
python3 tools/typecheck.py
```

Expected: `No errors.`

If `GameSettings.MouseSensitivity` or `GameSettings.InvertLookY` are named differently, correct the call sites — do not add new properties. Confirm with:

```bash
grep -n "public static float MouseSensitivity\|public static bool InvertLookY" Assets/Game/Scripts/Core/Settings/GameSettings.cs
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCameraRig.cs
git commit -m "feat: add ArrivalCameraRig, seated free look with shake"
```

---

## Task 9: `ArrivalCutscene` — the beats

**Files:**
- Create: `Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCutscene.cs`

- [ ] **Step 1: Write the implementation**

Create `Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCutscene.cs`:

```csharp
using System.Collections;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// What the arrival looks like from inside the cabin.
    ///
    /// <para>
    /// Presentation ONLY. It moves no ship and seats no player — the hull is flown by
    /// <c>ArrivalDirector</c> on the server and replicated, and the bodies are put in their chairs
    /// by <c>SeatedRider</c>. This runs on each machine for its own player and would be correct if
    /// deleted mid-flight, which is the property that keeps a cutscene out of networked state.
    /// </para>
    ///
    /// <para>
    /// Durations are fractions of the descent rather than seconds, so retuning the descent length
    /// in the director does not silently desynchronise the beats from the thing they describe.
    /// </para>
    /// </summary>
    public class ArrivalCutscene : Cutscene
    {
        [Tooltip("How long the whole descent takes. Must match ArrivalDirector's descentDuration — " +
                 "the director is the authority and passes its value in via Configure.")]
        [SerializeField] private float descentDuration = 26f;

        [Tooltip("Fade in from black over this long at the start, as the player comes round.")]
        [SerializeField] private float wakeFade = 1.6f;

        [Tooltip("Shake through the descent, sampled by normalised time. Starts near zero, builds " +
                 "through entry buffet, and is at its peak for the ground rush.")]
        [SerializeField] private AnimationCurve shakeOverDescent = new(
            new Keyframe(0f, 0.05f),
            new Keyframe(0.35f, 0.25f),
            new Keyframe(0.75f, 0.55f),
            new Keyframe(1f, 1f));

        [Tooltip("How long the impact itself holds at full shake before the screen goes black.")]
        [SerializeField] private float impactHold = 0.35f;

        [Tooltip("How long the black holds after impact, before the player comes to in the wreck.")]
        [SerializeField] private float blackout = 1.4f;

        private float startTime;

        /// <summary>
        /// Told by the director how long its descent actually is, so the beats and the hull agree
        /// even though they are timed on different objects.
        /// </summary>
        public void Configure(float duration) => descentDuration = Mathf.Max(0.1f, duration);

        public override IEnumerator Play(CutsceneContext ctx)
        {
            Camera cam = ctx.PlayerCamera;
            if (cam == null)
            {
                Debug.LogError("[ArrivalCutscene] No player camera; the arrival cannot be shown.");
                yield break;
            }

            var rig = cam.gameObject.AddComponent<ArrivalCameraRig>();

            // Black first, then fade up: the player is meant to be coming round, and starting on a
            // clear frame shows them a cabin they have not been told they are in yet.
            LetterboxOverlay.Instance.FadeToBlackAsync(0f);
            yield return LetterboxOverlay.Instance.FadeFromBlackAsync(wakeFade);

            startTime = Time.time;

            while (Time.time - startTime < descentDuration)
            {
                float t = Mathf.Clamp01((Time.time - startTime) / descentDuration);
                rig.ShakeIntensity = shakeOverDescent.Evaluate(t);
                yield return null;
            }

            // Impact.
            rig.ShakeIntensity = 1f;
            yield return new WaitForSeconds(impactHold);

            yield return LetterboxOverlay.Instance.FadeToBlackAsync(0.12f);

            rig.ShakeIntensity = 0f;
            yield return new WaitForSeconds(blackout);

            // The rig is removed before the fade up, so the camera is back under the player's own
            // control by the first frame they can see. Destroy rather than disable: its OnDisable
            // is what returns the camera to its neutral local pose, and a disabled component left
            // attached would be found and re-enabled by a second arrival.
            Destroy(rig);

            yield return LetterboxOverlay.Instance.FadeFromBlackAsync(1f);
        }
    }
}
```

- [ ] **Step 2: Type-check**

```bash
python3 tools/typecheck.py
```

Expected: `No errors.`

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCutscene.cs
git commit -m "feat: add ArrivalCutscene, the beats of the descent"
```

---

## Task 10: `ArrivalSaveable` — the "already arrived" flag

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalSaveable.cs`

- [ ] **Step 1: Write the implementation**

Create `Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalSaveable.cs`:

```csharp
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Remembers that this world has already been arrived in.
    ///
    /// <para>
    /// One flag, on one saver, following <c>NpcWorldSaveable</c>'s shape: a subsystem's whole
    /// persisted state in a single key rather than an entity per fact. The wreck itself needs
    /// nothing here — <c>PlayerShip</c> already carries a <c>SaveableEntity</c> with a
    /// <c>TransformSaveable</c>, so its final pose persists on its own.
    /// </para>
    ///
    /// <para>
    /// Put this next to <see cref="ArrivalDirector"/> in the persistent scene, on an object that
    /// also carries a <c>SaveableEntity</c> — that component is what finds savers at all.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(ArrivalDirector))]
    public class ArrivalSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "arrival";

        private ArrivalDirector director;

        private ArrivalDirector Director => director != null ? director : director = GetComponent<ArrivalDirector>();

        public string SaveKey => Key;

        public struct State
        {
            public bool arrived;
        }

        public object CaptureState()
        {
            // A save taken WHILE the descent is running still records "arrived". Replaying a crash
            // landing on somebody who is already standing in the wreck is a far worse outcome than
            // cutting one short for somebody who quit halfway down — and the ship is persisted
            // wherever it had got to, so a resumed descent would start from the sky with a hull
            // already recorded on the ground.
            return new State { arrived = Director.HasArrived || Director.IsRunning };
        }

        public void RestoreState(JObject state)
        {
            // Null is a value, not an absence: it means the save was taken before this saver
            // existed, or with the component at its defaults. Either way the honest reading is
            // "this world has not been arrived in", which is what a brand new world wants.
            bool arrived = state?[nameof(State.arrived)]?.Value<bool>() ?? false;

            Director.RestoreArrived(arrived);
        }
    }
}
```

- [ ] **Step 2: Type-check**

This will fail until Task 11 adds `ArrivalDirector`. That is expected — run it after Task 11 instead, or write Task 11 first and return here. The plan orders them this way because the saver's contract is what the director's public surface has to satisfy.

- [ ] **Step 3: Commit (after Task 11 type-checks clean)**

```bash
git add Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalSaveable.cs
git commit -m "feat: add ArrivalSaveable, the already-arrived world flag"
```

---

## Task 11: `ArrivalDirector` — the server sequence

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.cs`

- [ ] **Step 1: Write the implementation**

Create `Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Flies the crash landing, once per world, on the server.
    ///
    /// <para>
    /// A plain <see cref="MonoBehaviour"/> and deliberately so. It owns no replicated state and
    /// sends no messages of its own: the ship it spawns is networked by <see cref="IWorldService"/>
    /// and replicated by that prefab's own <c>ClientNetworkTransform</c>, and the seating is
    /// <see cref="SeatedRider"/>'s. Being a <c>NetworkBehaviour</c> would buy nothing and cost a
    /// scene-placed <c>NetworkObject</c>, whose id has to survive being authored into a scene to
    /// work at all — the same reasoning <c>VersusShipSpawner</c> records.
    /// </para>
    ///
    /// <para>
    /// Everything it does that matters happens on the server. Its one caller is
    /// <c>NetworkGameManager</c>'s spawn coroutine, which already runs nowhere else.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ArrivalDirector : MonoBehaviour
    {
        public static ArrivalDirector Instance { get; private set; }

        [Tooltip("The ship the crew arrives in. MUST be registered in the network prefab list, or " +
                 "it spawns for the host and for nobody else.")]
        [SerializeField] private GameObject shipPrefab;

        [Tooltip("The cutscene each machine plays for its own player. Presentation only.")]
        [SerializeField] private ArrivalCutscene cutscene;

        [Tooltip("The descent, as numbers. ImpactPosition is overwritten at runtime from the world's " +
                 "spawn anchor.")]
        [SerializeField] private ArrivalPath path = ArrivalPath.Default;

        [Tooltip("How long the descent takes, in seconds. The cutscene is told this value rather " +
                 "than carrying its own, so the beats cannot drift from the hull.")]
        [SerializeField] private float descentDuration = 26f;

        [Tooltip("How long to wait for terrain under the impact point before giving up and " +
                 "spawning everyone normally.")]
        [SerializeField] private float groundResolveTimeout = 20f;

        [Tooltip("Height the ground probe drops from when measuring the impact site.")]
        [SerializeField] private float probeHeight = 600f;

        [Tooltip("How far the hull sits above the measured ground once it has stopped.")]
        [SerializeField] private float wreckGroundClearance = 1.2f;

        private GameObject ship;
        private SeatedRider seating;
        private int claimed;

        /// <summary>True once the crash has finished, or once a save said it already had.</summary>
        public bool HasArrived { get; private set; }

        /// <summary>True while the descent is actually running.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Should the next player to spawn be put in a seat rather than on the ground?
        ///
        /// <para>
        /// False once the crash is done, so everybody who joins afterwards spawns normally — the
        /// arrival is a thing that happened to this world once, not a thing that happens to each
        /// player.
        /// </para>
        /// </summary>
        public bool IsPending => !HasArrived && shipPrefab != null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Restore-only. Called by <see cref="ArrivalSaveable"/>; do not call from gameplay.</summary>
        public void RestoreArrived(bool arrived) => HasArrived = arrived;

        /// <summary>
        /// Puts one client into a seat, starting the descent if this is the first of them.
        ///
        /// <para>
        /// Server-only. <paramref name="impactXZ"/> is the position <c>NetworkGameManager</c>
        /// already resolved and streamed the world around; reusing it rather than resolving a
        /// second one matters for the reason that class documents — a second resolve returns a
        /// different point from the one the terrain was prepared for.
        /// </para>
        /// </summary>
        public IEnumerator SpawnIntoArrival(ulong clientId, Vector3 impactXZ)
        {
            if (!Network.Server)
            {
                Debug.LogError("[Arrival] SpawnIntoArrival called off the server.", this);
                yield break;
            }

            if (!EnsureShip(impactXZ, out bool fatal))
            {
                if (fatal)
                {
                    // Loud, and then the ordinary spawn — a player with no body is worse than a
                    // player who missed a cutscene.
                    HasArrived = true;
                    SpawnManager.Instance.SpawnPlayerForClient(clientId, impactXZ);
                }
                yield break;
            }

            float deadline = Time.time + groundResolveTimeout;
            while (seating.SeatCount == 0)
            {
                if (Time.time >= deadline)
                {
                    Debug.LogError($"[Arrival] '{ship.name}' still reports no seats after " +
                                   $"{groundResolveTimeout}s. Spawning normally.", this);
                    HasArrived = true;
                    SpawnManager.Instance.SpawnPlayerForClient(clientId, impactXZ);
                    yield break;
                }

                yield return null;
            }

            int seatIndex = SeatOrdering.SeatFor(claimed, seating.SeatCount);
            claimed++;

            // Spawned at the seat's current world pose so the body exists somewhere sensible even
            // for the frame before SeatedRider parents it.
            Transform seat = seating.transform;
            SpawnManager.Instance.SpawnPlayerForClient(clientId, seat.position, seat.rotation);

            // The body is created by the spawn above but the NetworkObject it needs is not
            // addressable until the next frame.
            yield return null;

            GameObject player = ResolvePlayer(clientId);
            if (player == null)
            {
                Debug.LogError($"[Arrival] Client {clientId} has no player object after spawning; " +
                               "they cannot be seated.", this);
                yield break;
            }

            seating.Seat(player, seatIndex);

            if (!IsRunning) StartCoroutine(FlyDescent());
        }

        /// <summary>Frees a seat when its occupant disconnects mid-descent.</summary>
        public void ReleaseClient(ulong clientId)
        {
            if (seating == null) return;

            GameObject player = ResolvePlayer(clientId);
            if (player != null) seating.Release(player);
        }

        /// <summary>
        /// Spawns the ship at the top of its arc, once. <paramref name="fatal"/> distinguishes
        /// "not yet, ask again" from "this will never work".
        /// </summary>
        private bool EnsureShip(Vector3 impactXZ, out bool fatal)
        {
            fatal = false;

            if (ship != null) return true;

            if (shipPrefab == null)
            {
                Debug.LogError("[Arrival] No ship prefab assigned — there is nothing to arrive in.", this);
                fatal = true;
                return false;
            }

            if (path.LateralBudget <= 0f)
            {
                Debug.LogError("[Arrival] Lateral budget must be positive; a zero-radius descent has " +
                               "no heading to fly.", this);
                fatal = true;
                return false;
            }

            if (!ShipGrounding.TryResolveGround(new Vector2(impactXZ.x, impactXZ.z), probeHeight,
                                                out float groundY))
            {
                // Not fatal: in a streamed world this means the chunk under the impact point has
                // not loaded, and the only correct response is to wait and ask again.
                return false;
            }

            path.ImpactPosition = new Vector3(impactXZ.x, groundY + wreckGroundClearance, impactXZ.z);

            ArrivalTrajectory.Evaluate(0f, path, out Vector3 start, out Quaternion startRotation);

            ship = GameServices.World.Spawn(shipPrefab, start, startRotation);

            if (ship == null)
            {
                Debug.LogError("[Arrival] Spawning the arrival ship returned nothing. Is it " +
                               "registered in the network prefab list?", this);
                fatal = true;
                return false;
            }

            ship.name = shipPrefab.name + " (Arrival)";

            seating = ship.GetComponent<SeatedRider>();
            if (seating == null)
            {
                Debug.LogError($"[Arrival] '{ship.name}' has no SeatedRider, so nobody can be seated " +
                               "in it.", ship);
                fatal = true;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Walks the hull down the arc. The transform is written on the server alone and reaches
        /// everyone else through the ship prefab's own ClientNetworkTransform.
        /// </summary>
        private IEnumerator FlyDescent()
        {
            IsRunning = true;

            var body = ship.GetComponent<Rigidbody>();
            bool wasKinematic = body != null && body.isKinematic;

            if (body != null)
            {
                // Kinematic for the descent: the arc is authored, and letting physics also have an
                // opinion about where the hull is produces a fight neither side wins.
                body.isKinematic = true;
            }

            PlayLocalCutscene();

            float elapsed = 0f;
            while (elapsed < descentDuration)
            {
                elapsed += Time.deltaTime;

                ArrivalTrajectory.Evaluate(elapsed / descentDuration, path,
                                           out Vector3 position, out Quaternion rotation);

                ship.transform.SetPositionAndRotation(position, rotation);
                yield return null;
            }

            // Land exactly on the authored pose rather than wherever the last frame's dt left it.
            ArrivalTrajectory.Evaluate(1f, path, out Vector3 impact, out Quaternion impactRotation);
            ship.transform.SetPositionAndRotation(impact, impactRotation);

            if (body != null) body.isKinematic = wasKinematic;

            // Held open until the cutscene's own blackout has covered the release, so nobody sees
            // themselves being unparented.
            yield return new WaitForSeconds(1.6f);

            seating.ReleaseAll();

            HasArrived = true;
            IsRunning = false;
        }

        /// <summary>
        /// Starts the presentation on this machine.
        ///
        /// <para>
        /// The server runs this for its own player only. Every other machine starts its own copy
        /// the same way, driven by <c>SeatedRider</c> seating its local player — a cutscene is a
        /// per-machine thing and routing it through the wire would be replicating a camera.
        /// </para>
        /// </summary>
        private void PlayLocalCutscene()
        {
            if (cutscene == null || CutsceneDirector.Instance == null) return;

            cutscene.Configure(descentDuration);
            CutsceneDirector.Instance.Play(cutscene);
        }

        private static GameObject ResolvePlayer(ulong clientId)
        {
            if (!Network.IsNetworked) return null;

            var manager = Unity.Netcode.NetworkManager.Singleton;
            if (manager == null || !manager.ConnectedClients.TryGetValue(clientId, out var client))
                return null;

            return client.PlayerObject != null ? client.PlayerObject.gameObject : null;
        }
    }
}
```

- [ ] **Step 2: Type-check**

```bash
python3 tools/typecheck.py
```

Expected: `No errors.` This also clears Task 10's `ArrivalSaveable`.

If `SpawnManager.SpawnPlayerForClient(ulong, Vector3, Quaternion)` is not found, confirm the overload with:

```bash
grep -n "public void SpawnPlayerForClient" Assets/Game/Scripts/Gameplay/Spawning/SpawnManager.cs
```

- [ ] **Step 3: Commit both**

```bash
git add Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.cs Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalSaveable.cs
git commit -m "feat: add ArrivalDirector, the server-side crash landing sequence"
```

---

## Task 12: Hook the arrival into the spawn flow

**Files:**
- Modify: `Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.cs`

The insertion point is inside `SpawnWhenReady`, immediately before the final `SpawnManager.Instance.SpawnPlayerForClient(clientId, spawnPos);` inside the `if (worldStreamer)` block. Everything the arrival needs has happened by then: the pending scene has loaded, the world has streamed, and `spawnPos` is a validated point on real ground.

It goes **after** the `restoringPlayer` early-return, which is what stops a loaded save from replaying the crash.

- [ ] **Step 1: Add the using**

At the top of the file, alongside the other `SpaceGame` usings, add:

```csharp
using SpaceGame.Gameplay.Arrival;
```

- [ ] **Step 2: Insert the branch**

Find this, near the end of the `if (worldStreamer)` block:

```csharp
                SpawnManager.Instance.SpawnPlayerForClient(clientId, spawnPos);
                yield break;
            }
```

Replace it with:

```csharp
                // A brand new world starts everybody strapped into a ship on its way down, so it
                // takes a different route to a body entirely. Checked here, at the very end, and
                // deliberately AFTER the restoringPlayer branch above returns: a loaded save has
                // already had its arrival, and replaying the crash on somebody standing in the
                // wreck is the one outcome worth engineering against.
                if (ArrivalDirector.Instance != null && ArrivalDirector.Instance.IsPending)
                {
                    yield return ArrivalDirector.Instance.SpawnIntoArrival(clientId, spawnPos);
                    yield break;
                }

                SpawnManager.Instance.SpawnPlayerForClient(clientId, spawnPos);
                yield break;
            }
```

- [ ] **Step 3: Free the seat on disconnect**

Find `OnClientDisconnected` (the method containing `VersusTeamRoster.Release(clientId);`) and add, after that line:

```csharp
            // Frees their seat if they dropped mid-descent, so the hull is not left carrying a
            // body that no longer exists. Harmless once the arrival is over.
            if (ArrivalDirector.Instance != null) ArrivalDirector.Instance.ReleaseClient(clientId);
```

- [ ] **Step 4: Type-check**

```bash
python3 tools/typecheck.py
```

Expected: `No errors.`

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.cs
git commit -m "feat: route new-world spawns through the crash landing arrival"
```

---

## Task 13: Scene and prefab wiring

This task is Editor work and cannot be done from the shell. Each step is a manual action in the Unity Editor.

**Files:**
- Modify: `Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab`
- Modify: `Assets/Game/Scenes/world/persistentScene.unity`

- [ ] **Step 1: Add `SeatedRider` to the ship**

Open `Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab`. On the **root** GameObject (the one carrying the `NetworkObject`), add a `SeatedRider` component. Leave `seatOffset` at its default.

It must be on the same GameObject as the `NetworkObject`; a `NetworkBehaviour` anywhere else will not spawn.

- [ ] **Step 2: Add `ShipSeat` markers**

**The ship seats four, not seven.** An early read of the prefab counted seven seat-ish names and was wrong: the four `Cockpit_Seat_Command*` objects live under `Model/` and are the seat *meshes*, while the sit anchors are four `SeatPoint` transforms whose world positions match those meshes one-to-one. Put `ShipSeat` on the anchors, never the meshes:

| Path | Order | Why |
| --- | --- | --- |
| `Cockpit/SeatPoint` | 0 | Pilot. Front row, best view through `Mesh_CanopyDome`. |
| `Cockpit/PassengerSeat1/SeatPoint` | 1 | Front row alongside the pilot. |
| `Cockpit/PassengerSeat2/SeatPoint` | 2 | Second row. |
| `Cockpit/PassengerSeat3/SeatPoint` | 3 | Third row. |

Numbering them explicitly rather than leaving them all at zero is worth the minute it takes: the front two are the ones looking through the canopy, and filling those first puts the first players to join in front of the windscreen. A fifth player wraps back to seat 0 — that is `SeatOrdering.SeatFor`'s wrap rule doing its job, not a bug.

Verify afterwards:

```bash
python3 - <<'PYEOF'
import pathlib, re
t = pathlib.Path("Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab").read_text(errors="ignore")
meta = pathlib.Path("Assets/Game/Scripts/Gameplay/Versus/Runtime/ShipSeat.cs.meta").read_text()
guid = re.search(r"guid: ([a-f0-9]{32})", meta).group(1)
print("ShipSeat components on PlayerShip:", t.count(guid) - 0)
PYEOF
```

Expected: `4`.

- [ ] **Step 3: Confirm the ship is still registered**

```bash
python3 - <<'PYEOF'
import pathlib, re
guid = re.search(r"guid: ([a-f0-9]{32})",
                 pathlib.Path("Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab.meta").read_text()).group(1)
p = pathlib.Path("Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset")
print("registered:", guid in p.read_text(errors="ignore"))
PYEOF
```

Expected: `registered: True`. If this is False the ship spawns for the host and for nobody else — add it to that list before going further.

- [ ] **Step 4: Create the director object in `persistentScene`**

Open `Assets/Game/Scenes/world/persistentScene.unity`. Create an empty GameObject named `ArrivalDirector` and add:

- `ArrivalDirector`
- `ArrivalSaveable` (added automatically by the `RequireComponent`)
- `SaveableEntity` — **required**, this is what finds savers at all

Then, as a child, create a GameObject named `ArrivalCutscene` and add the `ArrivalCutscene` component.

Wire the `ArrivalDirector`:
- `shipPrefab` → `PlayerShip.prefab`
- `cutscene` → the child `ArrivalCutscene`
- leave `path` at its defaults

- [ ] **Step 5: Save the scene**

Save the scene. If the prefab or scene changes appear not to stick, the AssetDatabase may be read-only in the current session — reimport with `Assets > Reimport` and check the console.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab Assets/Game/Scenes/world/persistentScene.unity
git commit -m "wire: add SeatedRider, ShipSeat markers and the ArrivalDirector"
```

---

## Task 14: Verify on a real client

Per the project's non-negotiables, a feature seen working only on the host is not finished.

- [ ] **Step 1: Host, new world**

Create a brand new world and enter as host. Expected: fade up seated in the cockpit, canopy ahead, curving descent, shake building, impact, blackout, standing up in the wreck.

- [ ] **Step 2: Confirm free look and locked body**

During the descent: the mouse turns the view, clamped. `W`/`A`/`S`/`D`, jump, dash, hotbar keys and the use button all do nothing.

- [ ] **Step 3: Client via MPPM**

Start a second player through Multiplayer Play Mode and join. Expected: the client is seated in a *different* seat, sees the host's body in its own seat, and the hull moves identically on both machines.

If the ship is invisible on the client, it is not registered in the network prefab list. If the client sees the ship but the host's body is standing at the origin, `SeatedRider`'s state channel is not being applied — check `occupants` is populated on the server.

- [ ] **Step 4: Join mid-descent**

Start the host, wait until the descent is a few seconds in, then join with the client. Expected: the client is seated in the moving ship and the descent finishes correctly for it.

- [ ] **Step 5: Save, quit, reload**

After the crash, save and quit. Reload the world. Expected: the wreck is still at the impact site, the players spawn normally on the ground, and **the cutscene does not replay**.

Confirm the flag reached the file:

```bash
python3 - <<'PYEOF'
import json, pathlib, sys
saves = sorted(pathlib.Path.home().glob("**/SpaceGame/**/*.json"))
print("\n".join(str(s) for s in saves[-5:]) if saves else "No save files found; check the save directory.")
PYEOF
```

Then grep the newest save for `arrival`. Expected: a key `arrival` with `arrived: true`.

- [ ] **Step 6: Shake accessibility**

Set `GameSettings.CameraShakeIntensity` to 0 and run the arrival again. Expected: the descent plays with a completely still camera — no residual drift. This is the guarantee `ShakeMathTests.ZeroSettingsScaleIsExactlyStill` asserts, verified end to end.

---

## Task 15: Document it

**Files:**
- Modify: `docs/architecture/Cutscenes.md`

- [ ] **Step 1: Add the arrival to the built-in table**

In the "Built-in cutscenes" table, add:

```markdown
| `ArrivalCutscene` | The crash landing that opens a new world. Seated free look plus shake; the hull is flown by `ArrivalDirector` on the server. |
```

- [ ] **Step 2: Correct the "not replicated" note**

The "Current state" section says cutscenes are not replicated. That is still true of the mechanism and should stay, but it now has a worked exception. Replace the `Not replicated yet` bullet with:

```markdown
- **Not replicated yet.** A cutscene runs only on the client that triggered it. Until it is routed
  through `NetMessaging` (see `spacegame-multiplayer`), don't mutate networked state inside one —
  the mutation would land on one machine only.

  The crash landing arrival is the worked example of the way around this: `ArrivalCutscene` is pure
  presentation and mutates nothing, while everything shared — the hull's motion, who is in which
  seat — lives in `ArrivalDirector` and `SeatedRider` on the server. A cutscene that needs to agree
  across machines should split the same way rather than wait for the director to be networked.
```

- [ ] **Step 3: Add an arrival section**

At the end of the file, before "Files", add:

```markdown
## The crash landing arrival

A new world opens with the crew strapped into `PlayerShip` on its way down. Three pieces:

| Piece | Machine | Job |
| --- | --- | --- |
| `ArrivalDirector` | server | Resolves the impact site, spawns the ship at altitude, seats each arriving player, walks the hull down `ArrivalTrajectory`, releases everyone at the end. |
| `SeatedRider` | everywhere | Parents bodies into `ShipSeat` markers. Event channel plus a re-asserting state channel, so a client joining mid-descent is seated correctly. |
| `ArrivalCutscene` | local | Letterbox, fade, shake beats. Mutates nothing. |

It fires once per world and is recorded by `ArrivalSaveable` under the `arrival` key, so a loaded
save never replays it. The wreck persists on its own — `PlayerShip` already carries a
`SaveableEntity` with a `TransformSaveable`.

`ArrivalCameraRig` reads the `Look` action directly from `InputSystem.actions` rather than through
`PlayerInputManager`, because the cutscene runs with the player's input disabled and that component
zeroes its look axis in `OnDisable`. `MountModule.Camera.cs` does the same thing for the same
reason.

Shake is capped and multiplied by `GameSettings.CameraShakeIntensity`, which reaches zero. The
descent runs for many seconds and cannot be skipped, so that setting is an accessibility
requirement rather than a polish option.
```

- [ ] **Step 4: Commit**

```bash
git add docs/architecture/Cutscenes.md
git commit -m "docs: describe the crash landing arrival and the presentation split"
```

---

## Self-review notes

**Spec coverage.** Every section of the spec maps to a task: architecture → 7/11, descent → 2/11, streaming budget → 1 (`LateralBudget` tooltip) and 2 (`NeverExceedsTheLateralBudget`), impact site → 11 (`EnsureShip`), seating → 3/7/13, presentation → 8/9, shake principle → 4/5, skippability decision → recorded in the spec, nothing to build, persistence → 10, failure handling → 11 and 12, testing → 2/3/5, out-of-scope items → not built.

**Known deviations from the spec, both deliberate:**
1. The spec's `ShipShake` component became `ShakeMath` (pure) plus `ArrivalCameraRig` (one `LateUpdate`). Two components writing one transform in an undefined order is a real bug; the spec was updated to match.
2. The spec's `keepLook` overload on `PlayerController` was dropped entirely once `MountModule.Camera.cs` was found to have solved the same problem without touching shared code. The spec was updated.

**Type consistency.** `ArrivalTrajectory.Evaluate(float, in ArrivalPath, out Vector3, out Quaternion)` is used with that signature in Tasks 2 and 11. `SeatOrdering.OrderedIndices(IReadOnlyList<int>)` and `SeatOrdering.SeatFor(int, int)` are used with those signatures in Tasks 3, 7 and 11. `ShakeMath.Displacement(float, float, float, float, float)` matches between Tasks 5 and 8. `SeatedRider.Seat`, `.Release`, `.ReleaseAll` and `.SeatCount` match between Tasks 7 and 11. `ArrivalDirector.HasArrived`, `.IsRunning`, `.IsPending`, `.RestoreArrived`, `.SpawnIntoArrival` and `.ReleaseClient` match between Tasks 10, 11 and 12.
