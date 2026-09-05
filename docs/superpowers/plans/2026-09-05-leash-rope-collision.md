# Leash Rope Collision Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a leash rope bend around static world geometry instead of passing through it, so that rope spent on a wrap is rope no longer available to the far end.

**Architecture:** A rope stops being the chord `A→B` and becomes a polyline `A → w₀ → … → wₙ → B`. A new `LeashPath` owns the waypoint list and the insert/remove step; `Leash` measures stretch along the polyline and pulls each end toward its *adjacent* waypoint rather than toward the far end. Casting is injected as a delegate so the geometry is testable in EditMode with no physics scene. `LeashGround`'s downward height probe — which faked this for the renderer only — is deleted.

**Tech Stack:** Unity 6000.3, C# 9, `Physics.SphereCastNonAlloc`, NUnit EditMode tests, Netcode for GameObjects (unchanged by this work).

**Spec:** [docs/superpowers/specs/2026-09-05-leash-rope-collision-design.md](../specs/2026-09-05-leash-rope-collision-design.md)

> **Verification command for every task:** `python3 tools/typecheck.py --editor`
> Exit code 0 means the runtime and editor assemblies both compile. This works while the Unity
> Editor is open and holding the lock. There is no headless test runner on this machine (no licence),
> so EditMode tests are run from the Editor's Test Runner window; the type-check is what proves the
> test code itself compiles.

> **Commits are gated.** A hook in this repo blocks `git commit` unless the user asks for a commit in
> that turn. Run the `git add` in each commit step, then ask the user to authorise the commit rather
> than retrying.

---

## File Structure

| File | Responsibility |
|---|---|
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashWrap.cs` | **new.** One waypoint: position, normal, collider. Pure data. |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashPath.cs` | **new.** The polyline: the waypoint list, the pure geometry (`PolylineLength`, `TryMake`, `DirectionFrom`), the insert/remove step, and `WorldCast` — the one place this feature touches `Physics`. |
| `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs` | **modify.** `Settings` gains four tunables; `FixedUpdate` steps the path; `MeasureStretch` measures the polyline; `ResolveEnd` takes its direction from the path; `LateUpdate` hands the rope its points. |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashRope.cs` | **modify.** `Draw` takes a polyline. Sag is shared out per segment. `FloorUnder`/`TiedBetween` deleted. |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashGround.cs` | **delete.** Its whole job was faking rope-world contact for the renderer. |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs` | **modify.** Author the four tunables; pass them into `RopeSettings`. |
| `Assets/Game/Editor/Tests/LeashConstraintTests.cs` | **modify.** Geometry tests plus the regression that an unwrapped rope is unchanged. |

Splitting `LeashPath` out of `Leash` follows the boundary the folder already keeps: `Leash` is the constraint, `LeashRope` is the drawing, `LeashGround` was the only file allowed to touch `Physics`. `LeashPath` inherits that last role.

---

## Task 1: `LeashWrap` — the waypoint

**Files:**
- Create: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashWrap.cs`

- [ ] **Step 1: Write the file**

```csharp
// One place a rope bends around the world.
//
// A readonly struct rather than a class: a path holds a handful of these, rebuilds them constantly,
// and nothing ever needs to alias one. Kept in its own file rather than nested in LeashPath so that
// LeashRail, which produces a wrap of its own, does not have to reach inside another type.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>One waypoint on a rope's path: where it bends, and what it is bending around.</summary>
    public readonly struct LeashWrap
    {
        /// <summary>The bend point, already pushed clear of the surface by the path's clearance.</summary>
        public readonly Vector3 Position;

        /// <summary>The surface normal at the contact, before that clearance was applied.</summary>
        public readonly Vector3 Normal;

        /// <summary>
        /// What the rope is bending around.
        ///
        /// <para>
        /// Held so the path can drop a wrap whose collider has been destroyed — a rope bent around
        /// a crate that has since been picked up is bent around nothing, and would otherwise hold
        /// its corner in mid-air until something else disturbed it.
        /// </para>
        /// </summary>
        public readonly Collider Surface;

        public LeashWrap(Vector3 position, Vector3 normal, Collider surface)
        {
            Position = position;
            Normal = normal;
            Surface = surface;
        }
    }
}
```

- [ ] **Step 2: Type-check**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0, no errors.

- [ ] **Step 3: Stage**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/LeashWrap.cs
```

Then ask the user to authorise: `feat(leash): LeashWrap, one waypoint on a rope's path`

---

## Task 2: `LeashPath` geometry — the pure half, test-first

**Files:**
- Create: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashPath.cs`
- Test: `Assets/Game/Editor/Tests/LeashConstraintTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `public class LeashConstraintTests`:

```csharp
        // ── Path geometry ──────────────────────────────────────────────────────

        [Test]
        public void PolylineLength_WithNoWraps_IsTheStraightDistance()
        {
            var points = new[] { new Vector3(0f, 0f, 0f), new Vector3(3f, 4f, 0f) };

            Assert.AreEqual(5f, LeashPath.PolylineLength(points), 0.0001f);
        }

        [Test]
        public void PolylineLength_SumsEverySegment()
        {
            var points = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(3f, 0f, 0f),
                new Vector3(3f, 4f, 0f),
            };

            Assert.AreEqual(7f, LeashPath.PolylineLength(points), 0.0001f);
        }

        /// <summary>
        /// The winch. Rope spent going round a corner is rope the far end does not have, so a bend
        /// makes the SAME two endpoints measure longer — which is what pulls the far end in.
        /// </summary>
        [Test]
        public void ABend_MakesTheSameEndpointsMeasureLonger()
        {
            var straight = new[] { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f) };
            var bent = new[] { new Vector3(0f, 0f, 0f), new Vector3(5f, 3f, 0f), new Vector3(10f, 0f, 0f) };

            Assert.Greater(LeashPath.PolylineLength(bent), LeashPath.PolylineLength(straight));
        }

        [Test]
        public void TryMake_OffsetsTheContactAlongTheNormal()
        {
            var tuning = new LeashPath.Tuning { clearance = 0.1f, maxWraps = 8 };

            bool made = LeashPath.TryMake(
                contact: new Vector3(5f, 0f, 0f), normal: Vector3.up, surface: null,
                from: Vector3.zero, to: new Vector3(10f, 0f, 0f), tuning, out LeashWrap wrap);

            Assert.IsTrue(made);
            Assert.AreEqual(new Vector3(5f, 0.1f, 0f), wrap.Position);
        }

        /// <summary>
        /// The degenerate case: a contact that lands on top of a point it is meant to bend between
        /// produces a zero-length segment, and a wall full of them fills the list in one step.
        /// </summary>
        [Test]
        public void TryMake_RefusesAWrapSittingOnItsOwnNeighbour()
        {
            var tuning = new LeashPath.Tuning { clearance = 0.1f, maxWraps = 8 };

            bool made = LeashPath.TryMake(
                contact: new Vector3(0.05f, 0f, 0f), normal: Vector3.up, surface: null,
                from: Vector3.zero, to: new Vector3(10f, 0f, 0f), tuning, out _);

            Assert.IsFalse(made);
        }

        [Test]
        public void DirectionFrom_WithNoWraps_PointsAtTheFarEnd()
        {
            var path = new LeashPath();

            Vector3 fromA = path.DirectionFrom(true, Vector3.zero, new Vector3(0f, 0f, 10f));

            Assert.AreEqual(Vector3.forward, fromA);
        }
```

- [ ] **Step 2: Run the type-check to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `error CS0246: The type or namespace name 'LeashPath' could not be found`.

- [ ] **Step 3: Write `LeashPath`**

```csharp
// The shape of a rope, once it is allowed to touch the world.
//
// A leash used to be the chord between its two knots: it measured the straight line, pulled along
// the straight line, and passed through anything in between. LeashGround papered over the visible
// half of that by DRAWING the rope draped on the ground while the constraint still measured the
// line underneath — so the rope looked like it lay on the hill and behaved like it went through it.
//
// This is the honest version. The rope is a polyline; the constraint measures it; rope spent going
// round a corner is rope the far end no longer has. That last sentence is the whole feature: it is
// what lets someone reel a load in by walking away from the corner their rope is bent around,
// without a winch existing anywhere in the system.
//
// Casting is injected rather than called directly so the geometry can be tested without a physics
// scene. WorldCast, at the bottom, is the only part of this file that knows Physics exists — the
// same split LeashGround used to hold.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Is the straight line between two points blocked? Reports the first contact along it.
    /// </summary>
    public delegate bool LeashCast(Vector3 from, Vector3 to,
                                   out Vector3 point, out Vector3 normal, out Collider surface);

    /// <summary>A rope's path: its two ends, and every place it bends between them.</summary>
    public class LeashPath
    {
        /// <summary>What the wrap step is allowed to do. Authored on the leash artifact.</summary>
        public struct Tuning
        {
            /// <summary>How far off a surface a bend point sits, in metres.</summary>
            public float clearance;

            /// <summary>Ceiling on bends in one rope. Past it the rope stops wrapping.</summary>
            public int maxWraps;
        }

        private readonly List<LeashWrap> wraps = new();
        private readonly List<Vector3> points = new();

        public IReadOnlyList<LeashWrap> Wraps => wraps;

        public int Count => wraps.Count;

        public void Clear() => wraps.Clear();

        // ── Pure geometry ──────────────────────────────────────────────────────

        /// <summary>Total length along a polyline. Zero or one point measures nothing.</summary>
        public static float PolylineLength(IReadOnlyList<Vector3> pts)
        {
            if (pts == null || pts.Count < 2) return 0f;

            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);

            return total;
        }

        /// <summary>
        /// A candidate bend, or nothing.
        ///
        /// <para>
        /// Refused when the offset point would sit within two clearances of one of the points it is
        /// meant to bend between. That is the degenerate case, and it is not rare: a rope lying along
        /// a flat wall contacts it everywhere, and without this the list fills with waypoints a
        /// millimetre apart in a single step and the rope's length collapses.
        /// </para>
        /// </summary>
        public static bool TryMake(Vector3 contact, Vector3 normal, Collider surface,
                                   Vector3 from, Vector3 to, in Tuning tuning, out LeashWrap wrap)
        {
            wrap = default;

            Vector3 position = contact + normal * tuning.clearance;
            float minSegment = Mathf.Max(0.002f, tuning.clearance * 2f);

            if (Vector3.Distance(position, from) < minSegment) return false;
            if (Vector3.Distance(position, to) < minSegment) return false;

            wrap = new LeashWrap(position, normal, surface);
            return true;
        }

        // ── The path ───────────────────────────────────────────────────────────

        /// <summary>
        /// A → every wrap → B, in a buffer this path owns.
        ///
        /// <para>
        /// The returned list is overwritten by the next call. Every caller here consumes it
        /// immediately, and handing out a fresh array 50 times a second per rope is garbage for
        /// nothing.
        /// </para>
        /// </summary>
        public IReadOnlyList<Vector3> PointsBetween(Vector3 endA, Vector3 endB)
        {
            points.Clear();
            points.Add(endA);
            for (int i = 0; i < wraps.Count; i++) points.Add(wraps[i].Position);
            points.Add(endB);

            return points;
        }

        /// <summary>How much rope this path uses to get from one end to the other.</summary>
        public float TotalLength(Vector3 endA, Vector3 endB) =>
            PolylineLength(PointsBetween(endA, endB));

        /// <summary>
        /// Unit vector from one end toward the point the rope actually pulls it at — its nearest
        /// bend, or the far end when the rope is straight.
        ///
        /// <para>
        /// This is the difference between a wrap that means something and a wrap that is decoration.
        /// Pull an end toward the FAR end and a rope bent ninety degrees round a pillar still drags
        /// its load straight through the pillar; pull it toward the bend and the load comes round
        /// the corner.
        /// </para>
        /// </summary>
        public Vector3 DirectionFrom(bool isA, Vector3 endA, Vector3 endB)
        {
            Vector3 self = isA ? endA : endB;

            Vector3 neighbour = wraps.Count == 0
                ? (isA ? endB : endA)
                : (isA ? wraps[0].Position : wraps[wraps.Count - 1].Position);

            Vector3 delta = neighbour - self;
            float distance = delta.magnitude;

            return distance > 0.0001f ? delta / distance : Vector3.forward;
        }

        /// <summary>
        /// Bring the path up to date: drop dead wraps, unwrap what no longer bends, then wrap what
        /// now does.
        ///
        /// <para>
        /// Unwrap runs BEFORE wrap. The other order re-tests a waypoint inserted this same step
        /// against a neighbour it has not been introduced to yet, and removes it again immediately.
        /// </para>
        /// </summary>
        public void Step(Vector3 endA, Vector3 endB, LeashCast cast, in Tuning tuning)
        {
            if (cast == null) return;

            DropDead();
            Unwrap(endA, endB, cast);
            Wrap(endA, endB, cast, tuning);
        }

        /// <summary>A rope bent around a crate that has since been carried off is bent around nothing.</summary>
        private void DropDead()
        {
            for (int i = wraps.Count - 1; i >= 0; i--)
                if (wraps[i].Surface == null) wraps.RemoveAt(i);
        }

        /// <summary>
        /// A wrap dies when its two neighbours can see each other.
        ///
        /// <para>
        /// The textbook test is whether the turn at the waypoint has reversed sign, which is well
        /// behaved for a rope in a plane and unreliable against arbitrary 3D meshes — the turn
        /// direction at a bend over a curved surface is ambiguous, and a waypoint that cannot decide
        /// sticks forever. Line of sight is one cast, is obviously correct, and cannot get stuck.
        /// </para>
        /// </summary>
        private void Unwrap(Vector3 endA, Vector3 endB, LeashCast cast)
        {
            int i = 0;

            while (i < wraps.Count)
            {
                Vector3 before = i == 0 ? endA : wraps[i - 1].Position;
                Vector3 after = i == wraps.Count - 1 ? endB : wraps[i + 1].Position;

                if (cast(before, after, out _, out _, out _))
                {
                    i++;
                    continue;
                }

                wraps.RemoveAt(i);

                // Removing this one may have freed the one before it, which has already been passed.
                if (i > 0) i--;
            }
        }

        /// <summary>
        /// At most one new bend per end per step, and never past the cap.
        ///
        /// <para>
        /// One per end per step rather than looping until clear: a rope dragged hard into a corner
        /// can otherwise insert its whole budget in a single frame, and every one of those bends is
        /// measured against endpoint positions that a physics step has not yet reacted to.
        /// </para>
        /// </summary>
        private void Wrap(Vector3 endA, Vector3 endB, LeashCast cast, in Tuning tuning)
        {
            if (wraps.Count >= tuning.maxWraps) return;

            Vector3 neighbourOfA = wraps.Count == 0 ? endB : wraps[0].Position;

            if (cast(endA, neighbourOfA, out Vector3 point, out Vector3 normal, out Collider surface)
                && TryMake(point, normal, surface, endA, neighbourOfA, tuning, out LeashWrap madeAtA))
                wraps.Insert(0, madeAtA);

            if (wraps.Count >= tuning.maxWraps) return;

            Vector3 neighbourOfB = wraps.Count == 0 ? endA : wraps[wraps.Count - 1].Position;

            if (cast(endB, neighbourOfB, out point, out normal, out surface)
                && TryMake(point, normal, surface, endB, neighbourOfB, tuning, out LeashWrap madeAtB))
                wraps.Add(madeAtB);
        }
    }
}
```

- [ ] **Step 4: Type-check**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

- [ ] **Step 5: Run the new tests**

In the Unity Editor: Window → General → Test Runner → EditMode → run `LeashConstraintTests`.
Expected: all six new tests pass, and every pre-existing test in the file still passes.

- [ ] **Step 6: Stage**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/LeashPath.cs \
        Assets/Game/Editor/Tests/LeashConstraintTests.cs
```

Ask the user to authorise: `feat(leash): LeashPath — a rope is a polyline, with tests`

---

## Task 3: `WorldCast` — the one place this touches Physics

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashPath.cs`

- [ ] **Step 1: Add `WorldCast` inside the `SpaceGame.Items` namespace, after the `LeashPath` class**

```csharp
    /// <summary>
    /// The real world, as a <see cref="LeashCast"/>. Owns its buffer, so a per-step query allocates
    /// nothing.
    ///
    /// <para>
    /// Two rules here are inherited wholesale from the LeashGround probe this replaces, because
    /// both were learned the hard way and neither is obvious. A rope must not catch on the things it
    /// is TIED to, or a rope knotted to a creature's flank instantly wraps around the creature. And
    /// a NonAlloc buffer that fills up discards hits arbitrarily — including, sometimes, the wall
    /// you care about — so it is grown and re-cast rather than trusted when full.
    /// </para>
    /// <para>
    /// The mask must exclude dynamic bodies. Every machine derives a rope's shape independently from
    /// replicated endpoints and nothing about that shape is ever sent; two machines agree about
    /// where a wall is and do not agree about where a rolling barrel was forty milliseconds ago.
    /// </para>
    /// </summary>
    public sealed class LeashWorldCast
    {
        private const int MaxBuffer = 128;

        private readonly LayerMask mask;
        private readonly float radius;

        private RaycastHit[] buffer = new RaycastHit[16];

        private Transform endA;
        private Transform endB;

        public LeashWorldCast(LayerMask mask, float radius)
        {
            this.mask = mask;
            this.radius = Mathf.Max(0.005f, radius);
        }

        /// <summary>The two things this rope is tied to. Their own surfaces are not obstacles for it.</summary>
        public void Ignoring(Transform a, Transform b)
        {
            endA = a;
            endB = b;
        }

        public bool Cast(Vector3 from, Vector3 to,
                         out Vector3 point, out Vector3 normal, out Collider surface)
        {
            point = default;
            normal = default;
            surface = null;

            Vector3 delta = to - from;
            float distance = delta.magnitude;

            // A segment no longer than the probe itself has nothing to report: the sphere starts
            // already covering the far end.
            if (distance <= radius * 2f) return false;

            Vector3 direction = delta / distance;
            Vector3 origin = from + direction * radius;
            float reach = distance - radius * 2f;

            int count = Physics.SphereCastNonAlloc(origin, radius, direction, buffer, reach, mask,
                                                   QueryTriggerInteraction.Ignore);

            while (count == buffer.Length && buffer.Length < MaxBuffer)
            {
                buffer = new RaycastHit[buffer.Length * 2];
                count = Physics.SphereCastNonAlloc(origin, radius, direction, buffer, reach, mask,
                                                   QueryTriggerInteraction.Ignore);
            }

            float nearest = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = buffer[i];

                if (hit.collider == null) continue;
                if (hit.distance >= nearest) continue;
                if (Under(hit.collider.transform, endA) || Under(hit.collider.transform, endB)) continue;

                // A sweep that starts already overlapping reports distance 0 and a zero normal.
                // There is no bend to be had from that, and offsetting along a zero normal would put
                // the waypoint inside the surface.
                if (hit.normal.sqrMagnitude < 0.5f) continue;

                nearest = hit.distance;
                point = hit.point;
                normal = hit.normal;
                surface = hit.collider;
            }

            return surface != null;
        }

        private static bool Under(Transform candidate, Transform root) =>
            root != null && candidate != null && candidate.IsChildOf(root);
    }
```

- [ ] **Step 2: Type-check**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

- [ ] **Step 3: Stage**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/LeashPath.cs
```

Ask the user to authorise: `feat(leash): LeashWorldCast, the rope's probe into the world`

---

## Task 4: Tunables on `LeashArtifact` and `Leash.Settings`

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs`

- [ ] **Step 1: Add the four fields to `Leash.Settings`**

In `Leash.cs`, inside `public struct Settings`, after `public LeashRope rope;`:

```csharp
            /// <summary>
            /// What a rope may bend around. MUST be static geometry only — see LeashWorldCast for
            /// why a dynamic collider in this mask desynchronises the rope between machines.
            /// </summary>
            public LayerMask wrapLayers;

            /// <summary>Probe radius, in metres. Roughly the rope's own thickness.</summary>
            public float wrapRadius;

            /// <summary>How far off a surface a bend sits, in metres.</summary>
            public float wrapClearance;

            /// <summary>Ceiling on bends in one rope.</summary>
            public int maxWrapPoints;
```

- [ ] **Step 2: Find where `LeashArtifact` builds `RopeSettings`**

Run: `grep -n 'wrapLayers\|leashableLayers\|new Leash.Settings\|Settings {' Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs`

- [ ] **Step 3: Add the authored fields to `LeashArtifact`**

Beside the existing rope tunables:

```csharp
        [Header("Collision")]
        [Tooltip("What the rope may bend around. Static geometry only — a dynamic collider here " +
                 "makes the rope's shape disagree between machines.")]
        [SerializeField] private LayerMask wrapLayers = ~0;

        [Tooltip("Probe radius in metres. About the rope's own thickness.")]
        [SerializeField] private float wrapRadius = 0.05f;

        [Tooltip("How far off a surface a bend sits, in metres.")]
        [SerializeField] private float wrapClearance = 0.06f;

        [Tooltip("Ceiling on bends in one rope. Past it the rope stops wrapping.")]
        [SerializeField] private int maxWrapPoints = 8;
```

- [ ] **Step 4: Carry them into the settings the artifact hands out**

Wherever the artifact fills a `Leash.Settings` (the method found in Step 2), add:

```csharp
                wrapLayers = wrapLayers,
                wrapRadius = wrapRadius,
                wrapClearance = wrapClearance,
                maxWrapPoints = maxWrapPoints,
```

- [ ] **Step 5: Type-check**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

- [ ] **Step 6: Stage**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs \
        Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs
```

Ask the user to authorise: `feat(leash): authored wrap tunables`

---

## Task 5: `Leash` measures and pulls along the path

This is the task that changes behaviour. Everything before it was inert.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs`
- Test: `Assets/Game/Editor/Tests/LeashConstraintTests.cs`

- [ ] **Step 1: Write the failing regression test**

The test that protects every rope already in the game: with no wraps, the new direction and
separation arithmetic must be identical to the old.

```csharp
        /// <summary>
        /// The generalisation must be exactly that. With no bend, each end's own contribution to the
        /// rope lengthening sums to the relative-velocity term it replaces — if this drifts, every
        /// rope in the shipped game changes feel and nothing says so.
        /// </summary>
        [Test]
        public void SeparationRate_WithNoWraps_MatchesRelativeVelocity()
        {
            var path = new LeashPath();

            Vector3 endA = new(0f, 0f, 0f);
            Vector3 endB = new(0f, 0f, 10f);

            Vector3 velocityA = new(1f, 0f, -2f);
            Vector3 velocityB = new(0f, 3f, 5f);

            Vector3 towardA = path.DirectionFrom(true, endA, endB);
            Vector3 towardB = path.DirectionFrom(false, endA, endB);

            float split = Vector3.Dot(velocityA, -towardA) + Vector3.Dot(velocityB, -towardB);
            float relative = Vector3.Dot(velocityA - velocityB, -towardA);

            Assert.AreEqual(relative, split, 0.0001f);
        }
```

- [ ] **Step 2: Run the type-check to confirm the test compiles, then run it**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0. Then run `LeashConstraintTests` in the Test Runner.
Expected: PASS — this test pins existing behaviour and should pass against Task 2's `LeashPath`
before any of `Leash.cs` is touched. If it fails, `DirectionFrom` is wrong and Task 2 must be fixed
before continuing.

- [ ] **Step 3: Add the path to `Leash`**

In `Leash.cs`, beside `private Settings settings;`:

```csharp
        /// <summary>
        /// The rope's shape. Derived here on every machine from the two replicated endpoints and
        /// never sent — see LeashPath.
        /// </summary>
        private readonly LeashPath path = new();

        private LeashWorldCast probe;

        private LeashPath.Tuning wrapTuning;
```

In `Awake` (after `settings` is available — if `settings` is assigned in `Create`, put this at the
end of `Create` instead, right before the rope is returned):

```csharp
            probe = new LeashWorldCast(settings.wrapLayers, settings.wrapRadius);
            wrapTuning = new LeashPath.Tuning
            {
                clearance = settings.wrapClearance,
                maxWraps = Mathf.Max(0, settings.maxWrapPoints),
            };
```

- [ ] **Step 4: Step the path in `FixedUpdate`**

In `Leash.FixedUpdate`, immediately after `RefreshChannel();` and before `float stretch = …`:

```csharp
            // Re-stated every step rather than once at tie time: an end can be REPLACED — the hand
            // end moving onto an object is the second half of every tie — and a probe still
            // excluding the previous one would let the rope wrap around what it is now tied to.
            probe.Ignoring(A.Anchor, B.Anchor);
            path.Step(A.Position, B.Position, probe.Cast, wrapTuning);
```

- [ ] **Step 5: Measure the polyline**

Replace `MeasureStretch` entirely:

```csharp
        /// <summary>
        /// Metres of rope owed. Zero or less means slack.
        ///
        /// <para>
        /// Measured along the PATH, not between the knots. That one word is the winch: a rope bent
        /// round a corner measures longer for the same two endpoints, so walking away from the
        /// corner draws the far end in — with no winch anywhere in this system, and without a player
        /// being able to shorten their own free segment by moving.
        /// </para>
        /// </summary>
        private float MeasureStretch() => path.TotalLength(A.Position, B.Position) - Length;
```

Then fix the two callers. In `FixedUpdate`:

```csharp
            float stretch = MeasureStretch();
```

- [ ] **Step 6: Pull along the path in `ResolveEnd`**

Replace the body of `ResolveEnd` from `float stretch = …` down to the `self.Pull(…)` call:

```csharp
            float stretch = MeasureStretch();
            if (stretch <= 0f) return;

            float share = ShareOf(self.Mass, other.CanMove ? other.Mass : Mathf.Infinity);

            // Each end pulls toward its own nearest BEND, not toward the far end. Pull toward the far
            // end and a rope wrapped ninety degrees round a pillar still drags its load through the
            // pillar — the wrap becomes decoration.
            Vector3 toward = path.DirectionFrom(self == A, A.Position, B.Position);
            Vector3 otherToward = path.DirectionFrom(other == A, A.Position, B.Position);

            // Each end's own contribution to the rope getting longer. With a bend in it the two ends
            // no longer move along one shared axis, so a single relative-velocity term is measuring
            // the wrong thing. With no bend, otherToward is exactly -toward and this reduces to the
            // relative velocity it replaces — pinned by SeparationRate_WithNoWraps_MatchesRelativeVelocity.
            float separation = Vector3.Dot(self.Velocity, -toward)
                             + Vector3.Dot(other.Velocity, -otherToward);

            self.Pull(toward,
                      ArrestSpeed(separation, share, settings.maxCorrectionSpeed),
                      CorrectionDistance(stretch, share, settings.correction, settings.maxCorrectionStep),
                      TowCap(NetPullOn(self, other, toward), self.Mass));
```

- [ ] **Step 7: Type-check**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0. `LateUpdate` still calls the old `Draw` signature and still compiles — Task 6
changes it.

- [ ] **Step 8: Verify in the Editor**

Enter play mode, equip the Leash, tie one end to a crate and the other to a wall corner, and walk
the crate around the corner.
Expected: the rope visibly bends at the corner and the crate follows the corner round rather than
being dragged into the wall.

- [ ] **Step 9: Stage**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs \
        Assets/Game/Editor/Tests/LeashConstraintTests.cs
```

Ask the user to authorise: `feat(leash): the constraint measures and pulls along the rope's path`

---

## Task 6: `LeashRope` draws the polyline; delete `LeashGround`

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashRope.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs`
- Delete: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashGround.cs`

- [ ] **Step 1: Replace `Draw`**

Replace the whole `public void Draw(Vector3 a, Vector3 b, float length, float tension01)` method with:

```csharp
        /// <summary>
        /// Lay the rope along its path.
        ///
        /// <paramref name="tension01"/> is zero for a slack rope and one for a rope at full stretch;
        /// it drives the thinning and the shiver.
        ///
        /// <para>
        /// Points are emitted evenly along the path's LENGTH rather than evenly per segment, so a
        /// short bend round a corner does not get the same share of the line renderer's budget as a
        /// twenty-metre span.
        /// </para>
        /// </summary>
        public void Draw(IReadOnlyList<Vector3> path, float length, float tension01)
        {
            if (line == null || path == null || path.Count < 2) return;

            int count = Mathf.Max(2, segments);
            if (line.positionCount != count) line.positionCount = count;
            if (drawn.Length != count) drawn = new Vector3[count];
            drawnCount = count;

            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);

            if (total < 0.01f)
            {
                for (int i = 0; i < count; i++)
                {
                    line.SetPosition(i, path[0]);
                    drawn[i] = path[0];
                }
                return;
            }

            float tension = Mathf.Clamp01(tension01);

            float thickness = width * (1f - tensionThinning * tension);
            line.startWidth = thickness;
            line.endWidth = thickness;

            // Per metre of rope, so a longer rope lays down more braid rather than stretching what
            // it has.
            line.textureScale = new Vector2(Mathf.Max(0.01f, total * braidsPerMetre), 1f);

            // Spare rope is shared out in proportion to each span, so a rope pinned round a corner
            // droops in both halves rather than dumping all its slack into one of them.
            float slack = Mathf.Max(0f, length - total);

            Displacement wobble = WobbleFor(total, tension);

            int segment = 0;
            float consumed = 0f;
            float segmentSpan = Vector3.Distance(path[0], path[1]);

            for (int i = 0; i < count; i++)
            {
                float travel = total * i / (count - 1);

                while (segment < path.Count - 2 && travel > consumed + segmentSpan)
                {
                    consumed += segmentSpan;
                    segment++;
                    segmentSpan = Vector3.Distance(path[segment], path[segment + 1]);
                }

                Vector3 from = path[segment];
                Vector3 to = path[segment + 1];

                float t = segmentSpan > 0.0001f
                    ? Mathf.Clamp01((travel - consumed) / segmentSpan)
                    : 0f;

                Vector3 chord = Vector3.Lerp(from, to, t);

                // Pinned at every point on the path, not just at the two knots. A bend is a place the
                // rope touches the world, and a rope that sags away from what it is resting on has
                // not understood what resting means.
                float envelope = 4f * t * (1f - t);

                float share = total > 0.0001f ? segmentSpan / total : 0f;
                float sag = Mathf.Min(maxSag, SagDepth(segmentSpan, segmentSpan + slack * share));

                Vector3 p = chord;
                p.y -= envelope * sag;

                if (wobble.Amplitude > 0.0001f)
                {
                    Vector3 axis = to - from;
                    Vector3 forward = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.forward;

                    Vector3 right = Vector3.Cross(forward, Vector3.up);
                    right = right.sqrMagnitude < 1e-4f ? Vector3.right : right.normalized;
                    Vector3 up = Vector3.Cross(right, forward);

                    float amp = wobble.Amplitude * envelope;
                    float phase = Time.time * wobble.Speed;
                    float along = travel / total;

                    // Two axes, deliberately out of step in both frequency and speed, so the motion
                    // turns over in space. One axis alone is a flat ripple, and a flat ripple is
                    // invisible from half of all viewing angles.
                    p += right * (Mathf.Sin(along * Mathf.PI * 2f * wobble.Waves - phase) * amp);
                    p += up * (Mathf.Sin(along * Mathf.PI * 2f * wobble.Waves * 0.63f - phase * 1.31f) * amp * 0.7f);
                }

                line.SetPosition(i, p);
                drawn[i] = p;
            }
        }
```

- [ ] **Step 2: Delete the height probe and its plumbing from `LeashRope`**

Delete the `FloorUnder` method and the `TiedBetween` method, and delete the `LeashGround` field and
every reference to it in `LeashRope.cs` (including in `Build` and `CopyFrom`).

Run: `grep -n 'LeashGround\|FloorUnder\|TiedBetween' Assets/Game/Scripts/Items/Artifacts/Leash/LeashRope.cs`
Expected: no output.

- [ ] **Step 3: Add the `using` for the list type**

At the top of `LeashRope.cs`, ensure:

```csharp
using System.Collections.Generic;
```

- [ ] **Step 4: Update the caller in `Leash.LateUpdate`**

Replace the body of `LateUpdate` after the null guard:

```csharp
            settings.rope.Draw(path.PointsBetween(A.Position, B.Position), Length, Tension01);
```

Delete the `settings.rope.TiedBetween(A.Anchor, B.Anchor);` line above it — the probe it fed is gone,
and the wrap step is told what to ignore in `FixedUpdate` instead.

- [ ] **Step 5: Delete `LeashGround`**

```bash
git rm Assets/Game/Scripts/Items/Artifacts/Leash/LeashGround.cs \
       Assets/Game/Scripts/Items/Artifacts/Leash/LeashGround.cs.meta
```

- [ ] **Step 6: Confirm nothing else referenced it**

Run: `grep -rn 'LeashGround' Assets/ --include="*.cs"`
Expected: no output.

- [ ] **Step 7: Type-check**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

- [ ] **Step 8: Verify in the Editor**

Play mode. Tie a rope between two points across a hillside.
Expected: the rope lies on the hill because it is bending on it, not because it is being drawn to.
Walk one end away and the rope pulls tight over the crest rather than sinking through it.

- [ ] **Step 9: Stage**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/LeashRope.cs \
        Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs
```

Ask the user to authorise: `feat(leash): draw the rope along its path; delete the height probe`

---

## Task 7: Set the wrap mask on the Leash prefab

**Files:**
- Modify: `Assets/Game/Prefabs/Items/Artifacts/Gadgets/Leash.prefab`

- [ ] **Step 1: List the project's layers**

Run: `grep -n 'm_TagManager\|  - ' ProjectSettings/TagManager.asset | head -40`

- [ ] **Step 2: Set `wrapLayers` in the Inspector**

Open `Assets/Game/Prefabs/Items/Artifacts/Gadgets/Leash.prefab`, find the `LeashArtifact` component,
and set **Wrap Layers** to the static-world layers only — terrain, default/environment geometry.
**Uncheck Player, and uncheck every layer that loose props, vehicles, creatures or items live on.**
A dynamic collider in this mask makes two machines disagree about the rope's shape.

- [ ] **Step 3: Verify the prefab saved**

Run: `grep -n 'wrapLayers' -A 1 Assets/Game/Prefabs/Items/Artifacts/Gadgets/Leash.prefab`
Expected: a non-zero `m_Bits` value. If the file is unchanged, the Editor discarded the save — see
the AssetDatabase read-only trap in the project's notes.

- [ ] **Step 4: Stage**

```bash
git add Assets/Game/Prefabs/Items/Artifacts/Gadgets/Leash.prefab
```

Ask the user to authorise: `chore(leash): wrap mask on the leash prefab`

---

## Task 8: Playtest the ropes that already exist

This work changes shipped content. It is not done until the existing ropes have been looked at.

- [ ] **Step 1: Lasso**

Throw the lasso at a creature and let it hitch into a leash. Lead the creature around a rock.
Expected: the rope catches on the rock and the creature comes round it. Not expected: the rope
pinging, the creature jittering, or the rope inserting bends faster than it removes them.

- [ ] **Step 2: Towing**

Tie a rope to a crate and haul it across broken ground.
Expected: it catches where the ground rises. This is the intended feel change. Judge whether it is
too grabby; `wrapClearance` and `wrapRadius` are the two knobs.

- [ ] **Step 3: Two machines**

Host plus a real client. Tie a rope around a pillar on the host and confirm the client draws the
same bend. Then do it from the client.
Expected: identical shape on both screens. A divergence here means a dynamic collider is in
`wrapLayers`.

- [ ] **Step 4: Save and reload**

Tie a rope around a pillar, save, quit, reload.
Expected: the rope is still there and still bent — though possibly round the other side of the
pillar, which is by design and is documented.

---

## Task 9: Documentation

**Files:**
- Modify: `docs/AI/systems/LeashSystem.md`

- [ ] **Step 1: Update the doc**

- `paths:` — no change (same folder).
- `symptoms:` — add: `"a rope pulls straight through a wall or a hillside"`,
  `"a rope's shape is different on the host and the client"`,
  `"a rope fills up with bends and its length collapses"`.
- `## Model` — replace "The constraint is a distance limit" bullet's neighbours with the polyline
  model; state that rope spent on a bend is rope the far end does not have, and that this is a winch
  made of walking with no winch in the code.
- `## Key types` — add `LeashPath`, `LeashWrap`, `LeashWorldCast`. **Delete the `LeashGround` row.**
- `### The constraint, as ResolveEnd applies it` — the direction is now the path's, not `aToB`.
- `## Multiplayer` — add that wrapping is derived per machine from replicated endpoints and that
  `wrapLayers` must be static-only.
- `## Persistence` — add that wrap points are re-derived on load and may come back on the other side
  of an obstacle.
- `## Gotchas` — **delete** the two `LeashGround` gotchas ("Ground probe starts above the chord" and
  "The ground clamp runs after the sway"); they describe a file that no longer exists, and a doc
  describing code that is gone is worse than no doc. Add: unwrap-before-wrap ordering; static-only
  masking; that `ResolveEnd` reverting to `aToB` silently turns every wrap into decoration; that a
  slack rope can still dip slightly below a surface between two bends, bounded by `maxSag`.
- Bump `updated:` to the landing date.

- [ ] **Step 2: Regenerate and validate**

Run: `python3 tools/docs_check.py --index`
Expected: exit code 0. `INDEX.md` and `ROUTING.md` are generated — never hand-edit them.

- [ ] **Step 3: Stage**

```bash
git add docs/AI/systems/LeashSystem.md docs/AI/INDEX.md docs/AI/ROUTING.md
```

Ask the user to authorise: `docs: rope collision in LeashSystem`

---

## Self-review notes

- **Spec coverage.** Polyline model → Tasks 2, 5. Wrap/unwrap algorithm and ordering → Task 2.
  Force along the first segment → Task 5. Caps and insert refusal → Task 2. `LeashGround` subsumed →
  Task 6. Tunables → Task 4. Static-only determinism → Tasks 3, 7. Persistence (nothing saved, wraps
  re-derived) → no code change needed; documented in Task 9 and checked in Task 8 Step 4.
  Performance (owned buffers, `NonAlloc`, grow-and-recast) → Task 3. Tests → Tasks 2, 5.
  Feel change to shipped content → Task 8.
- **Deliberate simplification against the spec.** The spec described free-length accounting per end.
  It is not needed: because the constraint measures the whole polyline, a bend already makes the
  same two endpoints measure longer, and the reeling-in behaviour falls out. No `FreeLengthAt` is
  implemented, and none should be added.
- **Names are consistent throughout:** `LeashPath`, `LeashWrap`, `LeashWorldCast`, `LeashCast`,
  `LeashPath.Tuning { clearance, maxWraps }`, `PolylineLength`, `TryMake`, `PointsBetween`,
  `TotalLength`, `DirectionFrom`, `Step`, `Ignoring`, `Cast`.
- **Not covered here, by design:** rope-vs-rope, rope-vs-dynamic-body, friction at a bend, and any
  change to resist/break. All listed out of scope in the spec.
