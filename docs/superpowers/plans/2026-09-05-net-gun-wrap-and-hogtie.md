# Net Gun Wrap and Hogtie Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A net that touches a body cinches around it with the cloth solver, freezes, binds to the victim's ragdoll bones, and holds the victim limp until they struggle free — and a second player may tie a downed captive with a leash for up to two minutes.

**Architecture:** The cinch is a **constraint**, not a post-pass: a shrinking radial target relaxed inside `SnareLattice`'s existing Gauss-Seidel loop beside strands, shear and bend. Inextensible strands mean the shrinking circumference is absorbed as folds, so the net wraps without anything ever being projected onto a cylinder. When the cinch ends the solver stops for good and each node is bound to its nearest ragdoll bone. `SnaredBody` / `SnareTether` stop being movement constraints and become holds on the existing ragdoll. Escape is one rate-limited struggle accumulator feeding the existing `SnareIntegrity` drain, tuned twice — once for the net, once for the hogtie.

**Tech Stack:** Unity 6000.3, C# 9, Netcode for GameObjects, NUnit EditMode tests, existing `NetMessaging` / `NetChannel` / `NetArg` layer.

**Spec:** [docs/superpowers/specs/2026-09-05-net-gun-wrap-and-hogtie-design.md](../specs/2026-09-05-net-gun-wrap-and-hogtie-design.md)

> **Verification command for every task:** `python3 tools/typecheck.py --editor`
> Exit code 0 means the runtime and editor assemblies both compile. This works while the Unity
> Editor is open and holding the lock. There is no headless test runner on this machine (no
> licence), so EditMode tests are run from the Editor's Test Runner window; the type-check is what
> proves the test code itself compiles.

> **Commits are gated.** A hook in this repo blocks `git commit` unless the user asks for a commit
> in that turn. Run the `git add` in each commit step, then ask the user to authorise the commit
> rather than retrying. The same hook fires a false positive on any `$(...)` command substitution —
> avoid it in shell steps.

> **Tests live in `Assets/Game/Editor/Tests/`, not beside the code.** An asmdef cannot reference
> `Assembly-CSharp`, which is where every type here lives. `NetGunTests.cs` already documents this.

---

## File Structure

| File | Responsibility |
|---|---|
| `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareCinch.cs` | **new.** The radius schedule and the per-node radial correction. Pure static geometry, no Unity objects. |
| `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareBinding.cs` | **new.** Nearest-bone skinning: capture offsets at freeze, resolve world positions per frame. |
| `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggleMeter.cs` | **new.** The rate-limited struggle accumulator. Pure, no input, no Unity. |
| `Assets/Game/Scripts/Items/Artifacts/NetGun/SnarePhase.cs` | **new.** The five-phase enum. |
| `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareLattice.cs` | **modify.** Cinch constraint inside `Step`'s loop; `Freeze()`; `Frozen`. |
| `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareCatch.cs` | **modify.** Phase machine; cinch and bind; drop `DragTowardCaptives`; struggle into the drain. |
| `Assets/Game/Scripts/Items/Artifacts/NetGun/SnaredBody.cs` | **modify.** Was a constraint, becomes a hold plus the local struggle input. |
| `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareTether.cs` | **modify.** Was a speed cap, becomes a hold. Mount fallback. |
| `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggle.cs` | **modify.** Five dead tunables out, struggle tunables in. |
| `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareReceiver.cs` | **modify.** Handle `NetMsg.SnareStruggle`. |
| `Assets/Game/Scripts/Items/Artifacts/NetGun/Hogtie.cs` | **new.** The tie: its own integrity pool, its own hold, its own release. |
| `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs` | **modify.** `BoneTransforms` accessor; `BudgetExempt`. |
| `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollBudget.cs` | **modify.** Skip exempt rigs when evicting. |
| `Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs` | **modify.** `HoldDown` / `ReleaseHold`. |
| `Assets/Game/Scripts/Gameplay/Ragdoll/AgentRagdoll.cs` | **modify.** `HoldDown` / `ReleaseHold`. |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs` | **modify.** The `Tie` verb. |
| `Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs` | **modify.** `SnareStruggle = 98`, `Hogtied = 99`, `HogtieFreed = 100`. |
| `Assets/Game/Editor/Tests/NetGunTests.cs` | **modify.** Cinch, freeze, binding, struggle, budget, mount fallback. |
| `Assets/Game/Editor/Tests/HogtieTests.cs` | **new.** The tie's pool, its refusal to apply to a standing target, and the untie. |

`SnareCinch`, `SnareBinding` and `SnareStruggleMeter` are separate files because each is pure and
independently testable, and because `SnareLattice.cs` is already 50 KB. The folder's existing
boundary is one file per concern (`SnareDrape` clamps, `SnareMesh` draws, `SnareIntegrity` counts);
these three inherit it.

---

## Task 1: `SnareCinch` — the radius schedule and the radial correction

Pure geometry. No Unity objects, no lattice, no scene — which is what lets the next task wire it in
with the maths already proven.

**Files:**
- Create: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareCinch.cs`
- Test: `Assets/Game/Editor/Tests/NetGunTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside the `NetGunTests` class in `Assets/Game/Editor/Tests/NetGunTests.cs`:

```csharp
        // ── SnareCinch ────────────────────────────────────────────────────────

        [Test]
        public void CinchRadius_EasesFromStartToTarget()
        {
            Assert.AreEqual(3f, SnareCinch.RadiusAt(3f, 0.4f, 0f, 0.7f), 1e-4f,
                            "At t=0 the target is the radius the net already had.");
            Assert.AreEqual(0.4f, SnareCinch.RadiusAt(3f, 0.4f, 0.7f, 0.7f), 1e-4f,
                            "At t=duration the target is the authored cinch radius.");

            float mid = SnareCinch.RadiusAt(3f, 0.4f, 0.35f, 0.7f);
            Assert.Less(mid, 3f);
            Assert.Greater(mid, 0.4f);
        }

        [Test]
        public void CinchRadius_IsMonotonic()
        {
            float previous = float.MaxValue;

            for (int i = 0; i <= 20; i++)
            {
                float radius = SnareCinch.RadiusAt(3f, 0.4f, i / 20f * 0.7f, 0.7f);
                Assert.LessOrEqual(radius, previous + 1e-5f,
                                   "A cinch that widens at any point pumps energy into the cloth.");
                previous = radius;
            }
        }

        [Test]
        public void CinchRadius_ClampsPastTheEnd()
        {
            Assert.AreEqual(0.4f, SnareCinch.RadiusAt(3f, 0.4f, 5f, 0.7f), 1e-4f,
                            "Past the window the target holds, it does not keep shrinking.");
        }

        [Test]
        public void CinchCorrection_PullsInwardOnlyWhenOutsideTheRadius()
        {
            var axis = new SnareCinch.Axis(Vector3.zero, Vector3.up);

            Vector3 outside = SnareCinch.Correction(new Vector3(2f, 1f, 0f), axis, 1f, 1f);
            Assert.Less(outside.x, 0f, "A node outside the radius is pulled toward the axis.");
            Assert.AreEqual(0f, outside.y, 1e-5f, "The pull is radial — it must not move a node along the axis.");

            Vector3 inside = SnareCinch.Correction(new Vector3(0.5f, 1f, 0f), axis, 1f, 1f);
            Assert.AreEqual(Vector3.zero, inside,
                            "A node already inside the radius is left alone. A two-sided cinch " +
                            "would inflate the net into a tube, which is the capsule the design refuses.");
        }

        [Test]
        public void CinchCorrection_ScalesWithStiffness()
        {
            var axis = new SnareCinch.Axis(Vector3.zero, Vector3.up);

            Vector3 full = SnareCinch.Correction(new Vector3(2f, 0f, 0f), axis, 1f, 1f);
            Vector3 half = SnareCinch.Correction(new Vector3(2f, 0f, 0f), axis, 1f, 0.5f);

            Assert.AreEqual(full.magnitude * 0.5f, half.magnitude, 1e-5f);
        }

        [Test]
        public void CinchCorrection_IgnoresANodeOnTheAxis()
        {
            var axis = new SnareCinch.Axis(Vector3.zero, Vector3.up);

            Assert.AreEqual(Vector3.zero, SnareCinch.Correction(new Vector3(0f, 3f, 0f), axis, 1f, 1f),
                            "A node exactly on the axis has no radial direction. Normalising a " +
                            "zero vector is a NaN that spreads through the whole lattice in one pass.");
        }

        [Test]
        public void CinchAxis_IsSampledUpright()
        {
            var axis = new SnareCinch.Axis(new Vector3(5f, 2f, 5f), new Vector3(0f, 3f, 0f));

            Assert.AreEqual(Vector3.up, axis.Direction,
                            "A non-unit up must be normalised, or the radius is measured in the " +
                            "wrong units and the cinch overshoots.");
        }
```

- [ ] **Step 2: Run the type-check to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `The name 'SnareCinch' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

Create `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareCinch.cs`:

```csharp
// The shrinking ring a net closes around what it caught.
//
// Pure static geometry, deliberately: this is the one part of the wrap whose behaviour can be
// wrong in a way that looks like a tuning problem. A cinch that widens for one substep, or that
// pushes outward on a node already inside the ring, pumps energy into a cloth solver and reads as
// "the net is jittery" rather than as an ordering mistake — exactly the failure mode the Laplacian
// bend pass produced before it became a constraint. Kept out of SnareLattice so it can be proven
// with arithmetic and no scene.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where the net's cord is being pulled to while it closes around a body, and how hard.
    ///
    /// <para>
    /// <b>This is a target field, never a shape.</b> Nothing here moves a node onto a cylinder.
    /// <see cref="Correction"/> returns a pull toward a radius, which <see cref="SnareLattice"/>
    /// relaxes inside its own constraint loop alongside the strands, the shear and the bend. The
    /// strands are inextensible, so the cord's length has nowhere to go as the ring closes except
    /// into folds — the buckling, the slack between limbs and the hanging hem are the solver's
    /// answer, not an authored pose.
    /// </para>
    /// <para>
    /// This distinction is the whole reason the feature works. The version that projected cord
    /// straight onto a capsule was rejected on sight, and the note explaining why is one sentence
    /// long: projecting cord onto a capsule draws a capsule.
    /// </para>
    /// </summary>
    public static class SnareCinch
    {
        /// <summary>The line the net closes around: a point on the victim, and which way is up.</summary>
        public readonly struct Axis
        {
            public readonly Vector3 Origin;

            /// <summary>Unit. Normalised on construction so callers may hand over any up vector.</summary>
            public readonly Vector3 Direction;

            public Axis(Vector3 origin, Vector3 up)
            {
                Origin = origin;
                Direction = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
            }
        }

        /// <summary>
        /// The target radius this far into the cinch.
        ///
        /// <para>
        /// SmoothStep rather than a straight ramp, for the reason
        /// <see cref="SnareLattice.AdvanceUnfurl"/> gives about the unfurl: the ends are what read.
        /// A linear close starts and stops with a visible corner; eased ends look like a net
        /// gathering and then holding.
        /// </para>
        /// <para>
        /// Monotonic by construction, and that is load-bearing rather than tidy. A target that
        /// widened even for one substep would hand the cloth outward corrections it then has to
        /// take back, which is energy the solver did not have — and a net that visibly breathes.
        /// </para>
        /// </summary>
        public static float RadiusAt(float startRadius, float targetRadius, float elapsed, float duration)
        {
            if (duration <= 0f) return targetRadius;

            float t = Mathf.Clamp01(elapsed / duration);
            return Mathf.Lerp(startRadius, targetRadius, Mathf.SmoothStep(0f, 1f, t));
        }

        /// <summary>
        /// How far one node has to move to reach the ring, scaled by this pass's stiffness.
        ///
        /// <para>
        /// <b>One-sided.</b> A node already inside the radius is returned unchanged. Pushing it
        /// back out would make this an inflation as well as a contraction, and the net would settle
        /// into an even tube standing off the body — which is a drawn capsule by another route.
        /// What the design wants is cord gathered in and then left to fold wherever the body and
        /// its own constraints put it.
        /// </para>
        /// <para>
        /// <b>Radial only.</b> The component along the axis is removed before the pull is measured,
        /// so a cinch never slides cord up or down the body. Without that a net closing on a
        /// standing figure walks its own hem up to the waist.
        /// </para>
        /// </summary>
        public static Vector3 Correction(Vector3 node, Axis axis, float radius, float stiffness)
        {
            if (stiffness <= 0f) return Vector3.zero;

            Vector3 offset = node - axis.Origin;
            Vector3 radial = offset - axis.Direction * Vector3.Dot(offset, axis.Direction);

            float distance = radial.magnitude;

            // A node sitting exactly on the axis has no radial direction to be pulled along.
            // Normalising it is a NaN, and one NaN reaches every node in the lattice within a
            // single constraint pass.
            if (distance <= 1e-5f || distance <= radius) return Vector3.zero;

            return radial * (-(distance - radius) / distance * stiffness);
        }
    }
}
```

- [ ] **Step 4: Run the type-check, then the tests**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

Then in the Unity Editor: **Window → General → Test Runner → EditMode**, run `NetGunTests`.
Expected: the seven new `Cinch*` tests pass, and all pre-existing tests still pass.

- [ ] **Step 5: Stage the change**

```bash
git add Assets/Game/Scripts/Items/Artifacts/NetGun/SnareCinch.cs Assets/Game/Editor/Tests/NetGunTests.cs
```

Then ask the user to authorise the commit: `feat: SnareCinch — the shrinking ring a net closes around a body`

---

## Task 2: The cinch inside the constraint loop, and `Freeze`

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareLattice.cs`
- Test: `Assets/Game/Editor/Tests/NetGunTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `NetGunTests`:

```csharp
        // ── The cinch, wired into the solver ──────────────────────────────────

        /// <summary>
        /// A lattice deployed flat and settled, ready to be cinched. Shared by the tests below so
        /// they measure the cinch and not the deploy.
        /// </summary>
        private static SnareLattice CinchableLattice()
        {
            var lattice = new SnareLattice();
            lattice.Deploy(Vector3.zero, Vector3.forward, HalfWidth);

            for (int i = 0; i < SettleSteps; i++) lattice.Step(Substep);

            return lattice;
        }

        [Test]
        public void Cinch_DrawsTheNetInTowardTheAxis()
        {
            SnareLattice lattice = CinchableLattice();
            float before = lattice.WorldBounds().extents.x;

            lattice.BeginCinch(new SnareCinch.Axis(Vector3.zero, Vector3.up), 0.5f, 0.7f);
            for (int i = 0; i < 90; i++) lattice.Step(Substep);

            Assert.Less(lattice.WorldBounds().extents.x, before * 0.6f,
                        "The net is meant to close around the body, not sit where it landed.");
        }

        [Test]
        public void Cinch_KeepsItsCordLength()
        {
            SnareLattice lattice = CinchableLattice();
            float before = TotalStrandLength(lattice);

            lattice.BeginCinch(new SnareCinch.Axis(Vector3.zero, Vector3.up), 0.5f, 0.7f);
            for (int i = 0; i < 90; i++) lattice.Step(Substep);

            float after = TotalStrandLength(lattice);

            // This is the test that separates a wrap from a shrink-wrap. If the cinch were allowed
            // to shorten the cord, the net would end up as a smooth tube the size of the body —
            // the capsule the design refuses. Inextensible strands mean the length must survive and
            // come out as folds instead.
            Assert.AreEqual(before, after, before * 0.06f,
                            "Cord length must survive the cinch. Length that vanishes is a net " +
                            "that shrink-wrapped instead of folding.");
        }

        /// <summary>Summed length of every strand segment. Translation-invariant on purpose.</summary>
        private static float TotalStrandLength(SnareLattice lattice)
        {
            int side = lattice.Resolution;
            float total = 0f;

            for (int row = 0; row < side; row++)
            {
                for (int col = 0; col < side; col++)
                {
                    if (col + 1 < side)
                        total += Vector3.Distance(lattice.NodeAt(row, col), lattice.NodeAt(row, col + 1));
                    if (row + 1 < side)
                        total += Vector3.Distance(lattice.NodeAt(row, col), lattice.NodeAt(row + 1, col));
                }
            }

            return total;
        }

        [Test]
        public void Cinch_SettlesInsteadOfVibrating()
        {
            SnareLattice lattice = CinchableLattice();

            lattice.BeginCinch(new SnareCinch.Axis(Vector3.zero, Vector3.up), 0.5f, 0.7f);
            for (int i = 0; i < 270; i++) lattice.Step(Substep);

            Vector3 sample = lattice.NodeAt(0, 0);
            lattice.Step(Substep);

            // The number is the one the shear/bend sweep landed on: 0.0003 m of per-substep motion
            // at rest, against 0.0142 m for the full-stiffness case. A cinch applied AFTER the
            // constraint loop instead of inside it lands nearer the second number, which is how
            // this test tells the two apart.
            Assert.Less(Vector3.Distance(sample, lattice.NodeAt(0, 0)), 0.001f,
                        "A cinch relaxed outside the constraint loop leaves every substep ending " +
                        "off-constraint for the next one to yank back — a permanent vibration.");
        }

        [Test]
        public void Freeze_StopsTheSolverAndKeepsTheShape()
        {
            SnareLattice lattice = CinchableLattice();

            lattice.BeginCinch(new SnareCinch.Axis(Vector3.zero, Vector3.up), 0.5f, 0.7f);
            for (int i = 0; i < 90; i++) lattice.Step(Substep);

            Vector3 before = lattice.NodeAt(2, 2);
            lattice.Freeze();

            Assert.IsTrue(lattice.Frozen);

            for (int i = 0; i < 90; i++) lattice.Simulate(Substep);

            Assert.AreEqual(before, lattice.NodeAt(2, 2),
                            "A frozen lattice keeps the shape it froze with. This is the whole " +
                            "saving: a bound net costs nothing per frame.");
        }

        [Test]
        public void Freeze_SurvivesGravity()
        {
            SnareLattice lattice = CinchableLattice();
            lattice.Freeze();

            Vector3 before = lattice.NodeAt(4, 4);
            for (int i = 0; i < 300; i++) lattice.Simulate(Substep);

            Assert.AreEqual(before, lattice.NodeAt(4, 4),
                            "Freeze has to stop the integrator too, not only the constraints — a " +
                            "frozen net that still falls is a net that sinks through the floor.");
        }
```

- [ ] **Step 2: Run the type-check to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `'SnareLattice' does not contain a definition for 'BeginCinch'`.

- [ ] **Step 3: Add the fields and the tunables**

In `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareLattice.cs`, add beside the other
`[SerializeField]` stiffness tunables (near `iterations`, line ~61):

```csharp
        [Tooltip("How hard the net is drawn onto what it caught, 0-1.\n\n" +
                 "Relaxed inside the same loop as the strands and the bend, and divided across " +
                 "the passes by PerPass like every other soft family — so this number means the " +
                 "same thing at any iteration count.\n\n" +
                 "Too low and the net drifts off a body instead of gripping it. Too high and it " +
                 "beats the strands, which is a smooth tube rather than a folded net.")]
        [SerializeField, Range(0f, 1f)] private float cinchStiffness = 0.22f;
```

Add beside the other private solver state (near `shearPerPass`, line ~190):

```csharp
        /// <summary>The ring being closed, or null while the net is flying or has missed.</summary>
        private SnareCinch.Axis? cinchAxis;
        private float cinchStartRadius;
        private float cinchTargetRadius;
        private float cinchDuration;
        private float cinchElapsed;
        private float cinchPerPass;

        /// <summary>Set by <see cref="Freeze"/>. A frozen lattice is finished, permanently.</summary>
        private bool frozen;
```

- [ ] **Step 4: Add the public seams**

Add beside `GripGround` (near line ~479):

```csharp
        /// <summary>Has this net stopped solving for good? See <see cref="Freeze"/>.</summary>
        public bool Frozen => frozen;

        /// <summary>
        /// Start closing the net around a line through what it caught.
        ///
        /// <para>
        /// The start radius is measured here, once, from the lattice's own current spread — not
        /// authored. A net that caught a body at the edge of its bloom and one that caught it dead
        /// centre are different sizes at contact, and a fixed start radius would make the first
        /// jump inward on its first substep.
        /// </para>
        /// <para>
        /// <b>The axis is sampled once and never tracked.</b> The body is about to topple, and a
        /// target frame that tumbled with it would sweep the cinch ring — and every node it holds —
        /// through the ground.
        /// </para>
        /// </summary>
        public void BeginCinch(SnareCinch.Axis axis, float targetRadius, float duration)
        {
            if (pos == null || frozen) return;

            cinchAxis = axis;
            cinchTargetRadius = Mathf.Max(targetRadius, 0.01f);
            cinchDuration = Mathf.Max(duration, 0.01f);
            cinchElapsed = 0f;
            cinchStartRadius = MeanRadiusAbout(axis);
        }

        /// <summary>
        /// Stop solving, permanently, keeping the shape exactly as it is.
        ///
        /// <para>
        /// Both <c>pos</c> and <c>prev</c> are pinned, not just <c>pos</c>. Leaving <c>prev</c>
        /// behind hands every node its own displacement as stored velocity the moment anything
        /// reads it again — the same defect <see cref="SnareDrape"/> shipped with, where a
        /// position-only clamp injected speed on every substep of contact.
        /// </para>
        /// </summary>
        public void Freeze()
        {
            if (pos == null || frozen) return;

            frozen = true;
            cinchAxis = null;

            for (int i = 0; i < pos.Length; i++) prev[i] = pos[i];
        }

        /// <summary>Mean distance of the nodes from a line, for sizing the cinch that follows.</summary>
        private float MeanRadiusAbout(SnareCinch.Axis axis)
        {
            if (pos == null || pos.Length == 0) return 0f;

            float total = 0f;

            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 offset = pos[i] - axis.Origin;
                total += (offset - axis.Direction * Vector3.Dot(offset, axis.Direction)).magnitude;
            }

            return total / pos.Length;
        }
```

- [ ] **Step 5: Relax the cinch inside the loop**

In `SnareLattice.Step`, replace the body from the `shearPerPass` assignment through the end of the
`for` loop with:

```csharp
            shearPerPass = PerPass(shearStiffness);
            bendPerPass = PerPass(bendStiffness);
            cinchPerPass = PerPass(cinchStiffness);

            AdvanceCinch(step);

            // Alternating direction, because this is Gauss-Seidel: a pass carries tension from the
            // corner it starts at across the whole lattice, so running every pass the same way
            // leaves the far corner lagging.
            //
            // Shear is relaxed inside this loop rather than after it, because the two constraints
            // genuinely fight: pulling a stretched strand back in racks its cell further over, and
            // capping the diagonal stretches the strands again. Interleaving lets them converge on
            // each other; running the diagonals once at the end just leaves the strands holding
            // the whole residual.
            //
            // The cinch joins them for exactly the same reason and it is not optional. Applied
            // after the loop it would be a filter, moving nodes off their rest lengths by
            // construction and leaving every substep ending off-constraint — which is what the old
            // Laplacian bend pass did, and what the net's shivering actually was.
            for (int pass = 0; pass < iterations; pass++)
            {
                ConstrainStrands(forward: (pass & 1) == 0);
                ConstrainShear();
                ConstrainBend();
                ConstrainCinch();
            }
```

Add the guard at the very top of `Step`, immediately after the existing `pos == null` throw:

```csharp
            // A frozen net is finished. Not merely "does not move" — nothing integrates either, so
            // gravity cannot walk it through the floor over the minutes a hogtie can last.
            if (frozen) return;
```

Add the same guard at the top of `Simulate`, after its own `pos == null` check:

```csharp
            if (frozen) return;
```

- [ ] **Step 6: Add the two private methods**

Add beside `ConstrainBend`:

```csharp
        /// <summary>Move the cinch window on. Separate from the constraint so the ring closes once
        /// per substep rather than once per pass — the radius must not move underneath the loop
        /// that is converging on it.</summary>
        private void AdvanceCinch(float step)
        {
            if (cinchAxis == null) return;

            cinchElapsed += step;
        }

        /// <summary>
        /// Draw every node that is outside the ring back onto it.
        ///
        /// <para>
        /// Weighted by inverse mass like every other constraint here, which matters more than it
        /// looks: the rim nodes are heavy (<see cref="rimMassMultiplier"/>), so they answer this
        /// pull less than the mesh does. That is the behaviour that produces a hem hanging below a
        /// gathered body rather than a drawstring bag.
        /// </para>
        /// </summary>
        private void ConstrainCinch()
        {
            if (cinchAxis == null || cinchPerPass <= 0f) return;

            SnareCinch.Axis axis = cinchAxis.Value;
            float radius = SnareCinch.RadiusAt(cinchStartRadius, cinchTargetRadius,
                                               cinchElapsed, cinchDuration);

            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 correction = SnareCinch.Correction(pos[i], axis, radius, cinchPerPass);
                if (correction == Vector3.zero) continue;

                pos[i] += correction * Mathf.Min(inverseMass[i], 1f);
            }
        }
```

- [ ] **Step 7: Run the type-check, then the tests**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

Test Runner → EditMode → `NetGunTests`.
Expected: the six new tests pass. **All 28 pre-existing tests must still pass** — if
`Cinch_SettlesInsteadOfVibrating` fails, the cinch is being applied outside the loop or
`cinchPerPass` is not going through `PerPass`.

- [ ] **Step 8: Stage the change**

```bash
git add Assets/Game/Scripts/Items/Artifacts/NetGun/SnareLattice.cs Assets/Game/Editor/Tests/NetGunTests.cs
```

Ask the user to authorise: `feat: relax the cinch inside the solver loop, and freeze a landed net`

---

## Task 3: `RagdollRig.BoneTransforms` and `SnareBinding`

**Files:**
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs`
- Create: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareBinding.cs`
- Test: `Assets/Game/Editor/Tests/NetGunTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `NetGunTests`:

```csharp
        // ── SnareBinding ──────────────────────────────────────────────────────

        [Test]
        public void Binding_HoldsNodesStillWhenTheBonesDoNotMove()
        {
            var bone = new GameObject("Bone").transform;
            bone.position = new Vector3(1f, 0f, 0f);

            var nodes = new[] { new Vector3(1.1f, 0f, 0f), new Vector3(0.9f, 0f, 0f) };

            var binding = new SnareBinding();
            binding.Capture(nodes, new[] { bone });

            var resolved = new Vector3[nodes.Length];
            binding.Resolve(resolved);

            Assert.AreEqual(nodes[0].x, resolved[0].x, 1e-4f);
            Assert.AreEqual(nodes[1].x, resolved[1].x, 1e-4f);

            Object.DestroyImmediate(bone.gameObject);
        }

        [Test]
        public void Binding_CarriesNodesWithTheirBone()
        {
            var bone = new GameObject("Bone").transform;
            bone.position = Vector3.zero;

            var nodes = new[] { new Vector3(0f, 0.2f, 0f) };

            var binding = new SnareBinding();
            binding.Capture(nodes, new[] { bone });

            bone.position = new Vector3(10f, 0f, 0f);

            var resolved = new Vector3[1];
            binding.Resolve(resolved);

            Assert.AreEqual(new Vector3(10f, 0.2f, 0f), resolved[0]);

            Object.DestroyImmediate(bone.gameObject);
        }

        [Test]
        public void Binding_RotatesNodesWithTheirBone()
        {
            var bone = new GameObject("Bone").transform;

            var nodes = new[] { new Vector3(1f, 0f, 0f) };

            var binding = new SnareBinding();
            binding.Capture(nodes, new[] { bone });

            bone.rotation = Quaternion.Euler(0f, 90f, 0f);

            var resolved = new Vector3[1];
            binding.Resolve(resolved);

            Assert.AreEqual(0f, resolved[0].x, 1e-4f);
            Assert.AreEqual(-1f, resolved[0].z, 1e-4f,
                            "A node bound to a limb has to turn with it, or the net stays flat " +
                            "while the body folds up inside it.");

            Object.DestroyImmediate(bone.gameObject);
        }

        [Test]
        public void Binding_PicksTheNearestBonePerNode()
        {
            var near = new GameObject("Near").transform;
            near.position = Vector3.zero;
            var far = new GameObject("Far").transform;
            far.position = new Vector3(10f, 0f, 0f);

            var nodes = new[] { new Vector3(0.1f, 0f, 0f), new Vector3(9.9f, 0f, 0f) };

            var binding = new SnareBinding();
            binding.Capture(nodes, new[] { near, far });

            near.position = new Vector3(0f, 5f, 0f);

            var resolved = new Vector3[2];
            binding.Resolve(resolved);

            Assert.AreEqual(5f, resolved[0].y, 1e-4f, "The near node follows the bone it was nearest.");
            Assert.AreEqual(0f, resolved[1].y, 1e-4f, "The far node must not have moved with it.");

            Object.DestroyImmediate(near.gameObject);
            Object.DestroyImmediate(far.gameObject);
        }

        [Test]
        public void Binding_SurvivesADestroyedBone()
        {
            var bone = new GameObject("Bone").transform;

            var nodes = new[] { new Vector3(0f, 1f, 0f) };

            var binding = new SnareBinding();
            binding.Capture(nodes, new[] { bone });

            Object.DestroyImmediate(bone.gameObject);

            var resolved = new Vector3[1];
            Assert.DoesNotThrow(() => binding.Resolve(resolved),
                                "A netted creature can be despawned by streaming while its net is " +
                                "still drawn. That must not throw once per node per frame.");
        }

        [Test]
        public void Binding_ReportsWhetherItBound()
        {
            var binding = new SnareBinding();
            Assert.IsFalse(binding.IsBound, "Nothing captured yet.");

            binding.Capture(new[] { Vector3.zero }, new Transform[0]);
            Assert.IsFalse(binding.IsBound,
                           "A rig with no bones cannot be bound to. HumanoidRobot and the golems " +
                           "are rigid-part rigs and a builder that finds nothing finds it SILENTLY.");
        }
```

- [ ] **Step 2: Run the type-check to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `The name 'SnareBinding' does not exist in the current context`.

- [ ] **Step 3: Expose the rig's bones**

In `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs`, add beside `Hips` (near line ~198):

```csharp
        /// <summary>
        /// The simulated bones, for something that needs to ride the body without being part of it.
        ///
        /// <para>
        /// A fresh array rather than the live list, and transforms rather than the <c>Bone</c>
        /// records: a caller that could reach the Rigidbodies could add force to a ragdoll it does
        /// not own, and the one caller this exists for — a net binding its cord to a captive — has
        /// no business doing that.
        /// </para>
        /// </summary>
        public Transform[] BoneTransforms()
        {
            var found = new Transform[bones.Count];
            for (int i = 0; i < bones.Count; i++) found[i] = bones[i].Transform;
            return found;
        }
```

- [ ] **Step 4: Write `SnareBinding`**

Create `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareBinding.cs`:

```csharp
// What a net's cord is nailed to once it has stopped solving.
//
// The saving this buys is the whole point of the wrap: a bound net costs one matrix multiply per
// node per frame against a solver that no longer runs at all, where the draped net it replaces ran
// ninety substeps a second for its full thirty-second life, three of them at a time per gun.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Frozen net cord, pinned to the bones of the body it closed around.
    ///
    /// <para>
    /// <b>One bone per node, not a weighted skin.</b> A ragdoll here has on the order of fifteen
    /// bones and the cord is already folded against the body by the time this captures, so a
    /// blended skin would spend its budget smoothing a seam nobody can see through a net. Nearest
    /// bone at freeze time, offset stored in that bone's local space, and the seam between two
    /// limbs is held together by the cord's own drawn geometry.
    /// </para>
    /// <para>
    /// Bones are held as plain transforms rather than as the rig's own records, so nothing here can
    /// push a ragdoll it does not own. Every one is null-checked on resolve: a netted creature can
    /// be despawned by world streaming while a peer is still drawing the net that caught it, and
    /// that must not throw once per node per frame.
    /// </para>
    /// </summary>
    public class SnareBinding
    {
        private Transform[] bones;
        private int[] boneOf;
        private Vector3[] localOffset;

        /// <summary>Did the capture find anything to bind to? False for a rig with no skeleton.</summary>
        public bool IsBound => bones != null && bones.Length > 0 && boneOf != null;

        /// <summary>
        /// Nail each node to whichever bone is closest to it right now.
        ///
        /// <para>
        /// Called once, at the end of the cinch. Refuses a rig with no bones rather than binding to
        /// nothing: <c>Golem</c>, <c>CrabWalker6</c> and <c>HumanoidRobot</c> are rigid-part rigs
        /// with zero SkinnedMeshRenderers, and a rig whose build found no skeleton reports it
        /// SILENTLY — the prefab looks correctly wired and the net simply hangs in mid-air.
        /// <see cref="IsBound"/> is how the caller finds out in time to fall back.
        /// </para>
        /// </summary>
        public void Capture(Vector3[] nodes, Transform[] rigBones)
        {
            if (nodes == null || rigBones == null || rigBones.Length == 0)
            {
                bones = null;
                boneOf = null;
                localOffset = null;
                return;
            }

            bones = rigBones;
            boneOf = new int[nodes.Length];
            localOffset = new Vector3[nodes.Length];

            for (int i = 0; i < nodes.Length; i++)
            {
                int nearest = -1;
                float best = float.MaxValue;

                for (int b = 0; b < bones.Length; b++)
                {
                    if (bones[b] == null) continue;

                    float distance = (bones[b].position - nodes[i]).sqrMagnitude;
                    if (distance >= best) continue;

                    best = distance;
                    nearest = b;
                }

                boneOf[i] = nearest;
                localOffset[i] = nearest < 0
                    ? nodes[i]
                    : bones[nearest].InverseTransformPoint(nodes[i]);
            }
        }

        /// <summary>Where every node is this frame. Fills the caller's array rather than allocating.</summary>
        public void Resolve(Vector3[] into)
        {
            if (into == null || !IsBound) return;

            int count = Mathf.Min(into.Length, boneOf.Length);

            for (int i = 0; i < count; i++)
            {
                int bone = boneOf[i];

                // Unity-null, not C#-null: a destroyed transform compares equal to null through the
                // engine's own operator and would otherwise throw a MissingReferenceException here.
                if (bone < 0 || bones[bone] == null)
                {
                    into[i] = localOffset[i];
                    continue;
                }

                into[i] = bones[bone].TransformPoint(localOffset[i]);
            }
        }
    }
}
```

- [ ] **Step 5: Run the type-check, then the tests**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

Test Runner → EditMode → `NetGunTests`. Expected: the six `Binding_*` tests pass.

- [ ] **Step 6: Stage the change**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs Assets/Game/Scripts/Items/Artifacts/NetGun/SnareBinding.cs Assets/Game/Editor/Tests/NetGunTests.cs
```

Ask the user to authorise: `feat: bind frozen net cord to a captive's ragdoll bones`

---

## Task 4: The indefinite ragdoll hold, and the budget exemption

This is the task that fixes defect 1 from the spec — `RagdollBudget` standing a captive up.

**Files:**
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs`
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollBudget.cs`
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs`
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/AgentRagdoll.cs`
- Test: `Assets/Game/Editor/Tests/NetGunTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `NetGunTests`:

```csharp
        // ── The ragdoll hold ──────────────────────────────────────────────────

        [Test]
        public void Budget_NeverEvictsAHeldBody()
        {
            // A netted player must not be stood back up because a firefight elsewhere in the world
            // filled the ragdoll budget. Their limpness is a gameplay state with an owner, not a
            // corpse lying around waiting to be reclaimed.
            var held = new GameObject("Held").AddComponent<RagdollRig>();
            held.BudgetExempt = true;

            RagdollBudget.Register(held, cap: 1);

            var filler = new GameObject("Filler").AddComponent<RagdollRig>();
            RagdollBudget.Register(filler, cap: 1);

            Assert.IsTrue(RagdollBudget.IsLive(held),
                          "The exempt rig was evicted. A net that frees its captive when an " +
                          "unrelated creature dies is a bug nobody will reproduce on purpose.");

            RagdollBudget.Unregister(held);
            RagdollBudget.Unregister(filler);
            Object.DestroyImmediate(held.gameObject);
            Object.DestroyImmediate(filler.gameObject);
        }

        [Test]
        public void Budget_StillEvictsOrdinaryBodies()
        {
            var first = new GameObject("First").AddComponent<RagdollRig>();
            RagdollBudget.Register(first, cap: 1);

            var second = new GameObject("Second").AddComponent<RagdollRig>();
            RagdollBudget.Register(second, cap: 1);

            Assert.IsFalse(RagdollBudget.IsLive(first),
                           "Exempting held bodies must not have exempted everything.");

            RagdollBudget.Unregister(first);
            RagdollBudget.Unregister(second);
            Object.DestroyImmediate(first.gameObject);
            Object.DestroyImmediate(second.gameObject);
        }

        [Test]
        public void Hold_IsNotEndedByTheSettleCeiling()
        {
            // RagdollRig.maxLimpSeconds is 4, and IsSettled goes true there whether the body agrees
            // or not. That is correct for a knockdown and must NOT end a hold: a captive is up when
            // the pool runs out, which can be thirty seconds or two minutes later. Settling means
            // the bodies sleep, which is the look we want — it does not mean standing up.
            //
            // Pinned by reading the source, because the distinction lives in a control-flow guard
            // with no runtime state to assert on — the same technique LeashConstraintTests uses to
            // pin the absence of a SetTethered call.
            string source = System.IO.File.ReadAllText(
                "Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs");

            int guard = source.IndexOf("if (held) return;", System.StringComparison.Ordinal);
            int recovery = source.IndexOf("if (Time.time < downUntil", System.StringComparison.Ordinal);

            Assert.Greater(guard, -1, "PlayerRagdoll.Update lost its held guard.");
            Assert.Less(guard, recovery,
                        "The held guard has to come BEFORE the settle-and-timer recovery, or a " +
                        "held captive stands up four seconds into a two-minute tie.");
        }
```

- [ ] **Step 2: Run the type-check to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `'RagdollRig' does not contain a definition for 'BudgetExempt'`.

- [ ] **Step 3: Add the exemption flag to the rig**

In `RagdollRig.cs`, beside `Drives` (near line ~195):

```csharp
        /// <summary>
        /// Is this body limp because a gameplay system is HOLDING it there?
        ///
        /// <para>
        /// A corpse and a captive are both limp and the budget cannot otherwise tell them apart —
        /// so a firefight across the valley filling the budget would freeze a netted player, and
        /// <c>PlayerRagdoll.Update</c> restores control on <c>!IsLimp</c>, which stands them
        /// straight back up. The net is still drawn around them and still holding, and nothing is
        /// logged. Set for the duration of the hold and cleared on release.
        /// </para>
        /// </summary>
        public bool BudgetExempt { get; set; }
```

- [ ] **Step 4: Honour it in the budget**

In `RagdollBudget.cs`, in `OldestEvictable`, replace the loop body:

```csharp
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i] == null || live[i] == exclude) continue;

                // A held body is somebody's captive, not a corpse. Freezing it would hand control
                // back to a player a net is still holding — see RagdollRig.BudgetExempt.
                if (live[i].BudgetExempt) continue;

                if (live[i].IsSettled) return i;
                if (fallback < 0) fallback = i;
            }
```

And add beside `LiveCount`:

```csharp
        /// <summary>Is this rig still holding a place? For tests and diagnostics.</summary>
        public static bool IsLive(RagdollRig rig) => rig != null && live.Contains(rig);
```

Note the `while (live.Count > cap)` loop in `Register` already returns when `OldestEvictable`
answers -1, so a budget full of exempt bodies runs over cap rather than spinning. That is the
correct trade and the existing comment on `OldestEvictable` already states it.

- [ ] **Step 5: Add the hold to `PlayerRagdoll`**

In `PlayerRagdoll.cs`, add a field beside `downUntil`:

```csharp
        /// <summary>Held limp by something with its own end condition — a net, a tie. See HoldDown.</summary>
        private bool held;
```

Add the public pair after `OnKnockdown`:

```csharp
        /// <summary>
        /// Go limp and STAY limp until told otherwise.
        ///
        /// <para>
        /// Distinct from <see cref="OnKnockdown"/>, which recovers on its own timer, because the
        /// end of this one is not known when it starts: a captive is up when they have struggled
        /// out, and how long that takes is decided on the server against a pool being drained by
        /// their own inputs. Nothing about the duration can travel, so nothing tries to.
        /// </para>
        /// <para>
        /// Runs on every machine. It is called from <c>SnaredBody.Bind</c>, which every machine
        /// reaches through <c>NetMsg.Snared</c> — the same "both ends are replicated, so each
        /// machine computes its own half" argument in SnaredBody's own header. The Drives split
        /// then decides which half of the ragdoll plumbing this machine gets, exactly as for a
        /// knockdown.
        /// </para>
        /// </summary>
        public void HoldDown()
        {
            if (dead || held) return;

            held = true;

            Suspend();
            rig.BudgetExempt = true;
            rig.GoLimp(CarriedVelocity, settled: false, drives: Drives);
        }

        /// <summary>Let a held player stand up. Safe to call when nothing is holding them.</summary>
        public void ReleaseHold()
        {
            if (!held) return;

            held = false;
            rig.BudgetExempt = false;

            // Not Restore() directly: Update owns the recovery, and it has to check IsSettled first
            // or a player released mid-tumble snaps upright out of a roll. Clearing downUntil is
            // what lets that check run on the very next frame.
            downUntil = 0f;
        }
```

And guard the recovery in `Update` — insert immediately after the `dead` check:

```csharp
            // Still being held. The pool has not run out, so there is nothing to recover from yet.
            if (held) return;
```

- [ ] **Step 6: Add the same pair to `AgentRagdoll`**

In `AgentRagdoll.cs`, mirroring the shape above and using this file's own suspend/restore names,
add beside `CanBeKnockedDown`:

```csharp
        /// <summary>Held limp by a net or a tie, with no timer of its own. See PlayerRagdoll.HoldDown.</summary>
        private bool held;

        /// <summary>Is something holding this creature down right now?</summary>
        public bool IsHeld => held;

        /// <summary>
        /// Go limp and stay limp. The creature counterpart of
        /// <c>PlayerRagdoll.HoldDown</c>, and the same reasoning applies to all of it.
        ///
        /// <para>
        /// Refuses a ridden mount, and that refusal has to be visible to the caller rather than
        /// silent: <see cref="CanBeKnockedDown"/> is false while somebody is aboard, so a net on a
        /// mounted nomad would otherwise be a no-op with a clean console. The caller falls back to
        /// hobbling the animal instead — see <c>SnareTether.Bind</c>.
        /// </para>
        /// </summary>
        public bool HoldDown()
        {
            if (held) return true;
            if (!CanBeKnockedDown) return false;

            held = true;

            Suspend();
            rig.BudgetExempt = true;
            rig.GoLimp(Vector3.zero, settled: false, drives: Drives);
            return true;
        }

        /// <summary>Let a held creature get up.</summary>
        public void ReleaseHold()
        {
            if (!held) return;

            held = false;
            rig.BudgetExempt = false;
            downUntil = 0f;
        }
```

Add the same early return in `AgentRagdoll.Update`, after its own death check:

```csharp
            if (held) return;
```

> If `AgentRagdoll`'s private members are named differently from `PlayerRagdoll`'s (`Suspend`,
> `Drives`, `downUntil`, `rig`), use this file's own names — read the file before editing. The
> shape is what matters, not the identifiers.

- [ ] **Step 7: Run the type-check, then the tests**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

Test Runner → EditMode → `NetGunTests`. Expected: both `Budget_*` tests pass.

- [ ] **Step 8: Stage the change**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/ Assets/Game/Editor/Tests/NetGunTests.cs
```

Ask the user to authorise: `feat: indefinite ragdoll hold, exempt from the budget`

---

## Task 5: `SnareStruggleMeter` — the rate-limited accumulator

Pure. No input, no Unity types, no network. That is what makes the saturation provable.

**Files:**
- Create: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggleMeter.cs`
- Test: `Assets/Game/Editor/Tests/NetGunTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `NetGunTests`:

```csharp
        // ── The struggle meter ────────────────────────────────────────────────

        /// <summary>Feed the meter <paramref name="hz"/> inputs a second for a second, then read it.</summary>
        private static float StruggleAfterOneSecondAt(float hz)
        {
            var meter = new SnareStruggleMeter(maxUsefulRate: 2.5f, decaySeconds: 1.2f);

            int steps = 600;
            float step = 1f / steps;
            float sinceInput = 0f;

            for (int i = 0; i < steps; i++)
            {
                sinceInput += step;

                if (sinceInput >= 1f / hz)
                {
                    meter.Push();
                    sinceInput = 0f;
                }

                meter.Advance(step);
            }

            return meter.Level;
        }

        [Test]
        public void Struggle_Saturates()
        {
            float atCap = StruggleAfterOneSecondAt(2.5f);
            float spamming = StruggleAfterOneSecondAt(20f);

            Assert.AreEqual(atCap, spamming, 0.02f,
                            "Mashing twenty times a second must be worth exactly what mashing " +
                            "2.5 times a second is worth. This is the anti-macro property and the " +
                            "accessibility property at the same time — they are one property.");
        }

        [Test]
        public void Struggle_RewardsGettingUpToTheCap()
        {
            Assert.Less(StruggleAfterOneSecondAt(0.5f), StruggleAfterOneSecondAt(2.5f),
                        "Below the cap, struggling harder has to do more — or there is no input.");
        }

        [Test]
        public void Struggle_IsBoundedToOne()
        {
            var meter = new SnareStruggleMeter(maxUsefulRate: 2.5f, decaySeconds: 1.2f);

            for (int i = 0; i < 500; i++)
            {
                meter.Push();
                meter.Advance(1f / 60f);
            }

            Assert.LessOrEqual(meter.Level, 1f,
                               "The level feeds a mass multiplier. Unbounded, it is an instant escape.");
        }

        [Test]
        public void Struggle_DecaysWhenTheVictimStops()
        {
            var meter = new SnareStruggleMeter(maxUsefulRate: 2.5f, decaySeconds: 1.2f);

            for (int i = 0; i < 20; i++)
            {
                meter.Push();
                meter.Advance(0.4f);
            }

            float fighting = meter.Level;

            for (int i = 0; i < 120; i++) meter.Advance(1f / 60f);

            Assert.Less(meter.Level, fighting * 0.5f,
                        "A captive who stops fighting has to stop draining the net, or the pool " +
                        "empties on the strength of a struggle that ended ten seconds ago.");
        }

        [Test]
        public void Struggle_RejectsInputInsideTheCooldown()
        {
            var meter = new SnareStruggleMeter(maxUsefulRate: 2.5f, decaySeconds: 1.2f);

            Assert.IsTrue(meter.Push(), "The first input always counts.");
            Assert.IsFalse(meter.Push(),
                           "A second input in the same instant is discarded. This is what throttles " +
                           "the message send as well as the meter.");

            meter.Advance(0.5f);
            Assert.IsTrue(meter.Push(), "Past the cooldown it counts again.");
        }
```

- [ ] **Step 2: Run the type-check to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `The name 'SnareStruggleMeter' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

Create `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggleMeter.cs`:

```csharp
// How hard something caught in a net is fighting, on a scale that cannot be cheated by pressing
// faster.
//
// The cap is not a balance tweak, it is the design. A struggle that rewards raw input rate is a
// mechanic that excludes anyone who cannot spam a key and rewards anyone who binds an autofire
// macro — the same players, penalised and rewarded for the same property of their hardware rather
// than of their play (GDC-L1-UX-0006). Saturating the meter removes both at once.
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A 0..1 measure of how hard a captive is fighting, saturating at a fixed input rate.
    ///
    /// <para>
    /// Pure: it holds no input, no transform and no network. What counts as a struggle input is the
    /// caller's business — <see cref="SnaredBody"/> counts a Jump press and a Move direction
    /// reversal alike — and this only decides whether one is worth anything and what it adds up to.
    /// </para>
    /// <para>
    /// <b>The cooldown throttles the wire as well as the meter.</b> <see cref="Push"/> answering
    /// false is the caller's signal not to send, so a netted player emits at most
    /// <c>maxUsefulRate</c> messages a second however fast they hammer the key.
    /// </para>
    /// </summary>
    public class SnareStruggleMeter
    {
        private readonly float cooldown;
        private readonly float decaySeconds;

        private float level;
        private float sinceAccepted;

        /// <summary>0 when the captive is still, 1 when they are fighting as hard as counts.</summary>
        public float Level => level;

        /// <param name="maxUsefulRate">
        /// Inputs per second beyond which nothing more is gained. 2.5 is the shipped value: fast
        /// enough to read as a struggle, slow enough that a player can hold it indefinitely without
        /// hurting their hands.
        /// </param>
        /// <param name="decaySeconds">
        /// How long the level takes to fall away once the captive stops. Without it, one burst of
        /// struggling drains the net for the rest of its life.
        /// </param>
        public SnareStruggleMeter(float maxUsefulRate, float decaySeconds)
        {
            cooldown = 1f / Mathf.Max(maxUsefulRate, 0.01f);
            this.decaySeconds = Mathf.Max(decaySeconds, 0.01f);

            // Born past the cooldown so the very first input counts. Starting at zero would eat it,
            // and the one input a player notices being ignored is the first.
            sinceAccepted = cooldown;
        }

        /// <summary>
        /// Offer one struggle input.
        /// </summary>
        /// <returns>
        /// True if it counted. False means it landed inside the cooldown and was discarded — the
        /// caller should not send it either.
        /// </returns>
        public bool Push()
        {
            if (sinceAccepted < cooldown) return false;

            sinceAccepted = 0f;

            // One accepted input is worth one cooldown's worth of level, so a captive holding the
            // cap exactly sits at 1 and one holding half the cap sits at about a half. That
            // proportionality is what makes the multiplier in SnareCatch a number a designer can
            // reason about rather than a curve they have to sample.
            level = Mathf.Min(1f, level + cooldown / decaySeconds);
            return true;
        }

        /// <summary>Let time pass. Drives the cooldown and the decay.</summary>
        public void Advance(float delta)
        {
            sinceAccepted += delta;
            level = Mathf.Max(0f, level - delta / decaySeconds);
        }
    }
}
```

- [ ] **Step 4: Run the type-check, then the tests**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

Test Runner → EditMode → `NetGunTests`. Expected: the five `Struggle_*` tests pass.

- [ ] **Step 5: Stage the change**

```bash
git add Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggleMeter.cs Assets/Game/Editor/Tests/NetGunTests.cs
```

Ask the user to authorise: `feat: rate-limited struggle meter — spamming buys nothing`

---

## Task 6: `SnareStruggle` pruned, `SnaredBody` and `SnareTether` become holds

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggle.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnaredBody.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareTether.cs`
- Test: `Assets/Game/Editor/Tests/NetGunTests.cs`

- [ ] **Step 1: Delete the dead tunables**

Replace the whole body of `SnareStruggle.cs`'s class with:

```csharp
    [System.Serializable]
    public class SnareStruggle
    {
        [Tooltip("Seconds an ordinary single captive is held before the net rots away, if they " +
                 "never struggle.\n\n" +
                 "It means exactly that: one captive of SnareIntegrity.ReferenceLoad mass is held " +
                 "for this long. Three of them share the same pool and get a third of it each, " +
                 "which is what stops a wide net being strictly better than a careful shot.")]
        [SerializeField, Min(0.01f)] private float holdSeconds = 30f;

        [Tooltip("Struggle inputs per second past which nothing more is gained.\n\n" +
                 "The cap is the design, not a balance knob. Above it, a struggle rewards input " +
                 "rate — which excludes anyone who cannot spam a key and rewards anyone who binds " +
                 "an autofire macro (GDC-L1-UX-0006).")]
        [SerializeField, Min(0.1f)] private float maxUsefulStruggleRate = 2.5f;

        [Tooltip("Seconds a struggle takes to fade once the captive stops fighting.")]
        [SerializeField, Min(0.05f)] private float struggleDecaySeconds = 1.2f;

        [Tooltip("Extra load a captive struggling flat out puts on the net, as a multiple of one " +
                 "ordinary captive.\n\n" +
                 "At 2 a fully struggling captive presents three captives' worth, so a 30 s net " +
                 "gives out in 10 s. This is the number that sets how long an escape takes.")]
        [SerializeField, Min(0f)] private float struggleMultiplier = 2f;

        public float HoldSeconds => Mathf.Max(holdSeconds, 0.01f);
        public float MaxUsefulStruggleRate => Mathf.Max(maxUsefulStruggleRate, 0.1f);
        public float StruggleDecaySeconds => Mathf.Max(struggleDecaySeconds, 0.05f);
        public float StruggleMultiplier => Mathf.Max(struggleMultiplier, 0f);
    }
```

Update the class summary above it, replacing the sentence about designer numbers with:

```csharp
    /// <summary>
    /// How long a net holds, and how much a captive's own struggling shortens that.
    ///
    /// <para>
    /// Lives here rather than on <see cref="SnareTether"/> for the reason
    /// <see cref="LassoStruggle"/> gives: the tether is added at runtime and never authored, and
    /// serialized fields on a component nobody can select in the Inspector are constants wearing a
    /// costume. These are the numbers a designer actually moves, so they are serialized on the gun
    /// prefab and handed to each captive as the net lands.
    /// </para>
    /// <para>
    /// The shuffle radius, hobble speed, thrash and drag that used to live here are gone with the
    /// behaviour they described. Every one of them positioned a captive who was still on its feet,
    /// and a captive is now on the floor.
    /// </para>
    /// </summary>
```

- [ ] **Step 2: Run the type-check to find every caller**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL, listing each use of the five removed properties. Those are the call sites the next
steps remove — work the list, do not guess at it.

- [ ] **Step 3: Rewrite `SnaredBody` as a hold**

Replace the members of `SnaredBody` from `private Rigidbody body;` to the end of the class with:

```csharp
        private PlayerRagdoll ragdoll;
        private SnareStruggleMeter meter;
        private Transform anchor;
        private SnareStruggle settings;
        private bool bound;

        /// <summary>Which way the move stick was last pointing, for spotting a reversal.</summary>
        private Vector2 lastMove;

        public bool IsBound => bound;

        /// <summary>0..1, how hard this player is fighting. Read by the sender below.</summary>
        public float StruggleLevel => meter?.Level ?? 0f;

        public static SnaredBody Ensure(GameObject player)
        {
            if (player == null) return null;

            return player.TryGetComponent(out SnaredBody existing)
                ? existing
                : player.AddComponent<SnaredBody>();
        }

        /// <summary>
        /// The ragdoll this puts down, resolved on demand.
        ///
        /// Not trusted from <see cref="Awake"/> alone: this component is added at runtime, and
        /// Unity does not raise Awake for an AddComponent outside play mode — so in an EditMode
        /// test the field is still null when the hold first runs, and the net silently holds
        /// nothing. The same trap <see cref="LassoedBody"/> documents.
        /// </summary>
        private PlayerRagdoll Ragdoll =>
            ragdoll != null ? ragdoll : ragdoll = GetComponent<PlayerRagdoll>();

        /// <summary>
        /// Take hold: put this player on the ground and keep them there.
        ///
        /// <para>
        /// Runs on EVERY machine, which is the whole reason nothing extra is sent. The catch is
        /// already broadcast, so each machine reaches this through <c>NetMsg.Snared</c> and starts
        /// its own copy of the ragdoll — the Drives split inside <see cref="RagdollRig"/> then
        /// decides which of them is driving it and which is watching.
        /// </para>
        /// <para>
        /// Note what is NOT gated on <see cref="Network.Owns"/> any more. The old constraint wrote
        /// a position and had to be owner-only; going limp is presentation on every machine and
        /// owner-authoritative only in the sense the ragdoll already handles. The struggle INPUT
        /// below is still owner-only, because that is the part only one machine can know.
        /// </para>
        /// </summary>
        public bool Bind(Transform netAnchor, SnareStruggle struggleSettings)
        {
            if (bound && anchor != netAnchor) return false;

            anchor = netAnchor;
            settings = struggleSettings ?? new SnareStruggle();
            meter = new SnareStruggleMeter(settings.MaxUsefulStruggleRate,
                                           settings.StruggleDecaySeconds);
            bound = true;

            if (Ragdoll != null) Ragdoll.HoldDown();
            return true;
        }

        public void Release(Transform netAnchor)
        {
            if (!bound || (netAnchor != null && netAnchor != anchor)) return;

            bound = false;
            anchor = null;
            meter = null;

            // Only if nothing ELSE is still holding them. A net can tear out from under a body
            // that is also hogtied, and standing that player up would undo a tie somebody spent a
            // rope on — see Hogtie, which holds its own pool for exactly this reason.
            if (Ragdoll != null && GetComponent<Hogtie>() == null) Ragdoll.ReleaseHold();
        }

        private void OnDisable()
        {
            if (bound) Release(anchor);
        }

        /// <summary>
        /// Advance one step without a frame. The seam the EditMode tests use.
        ///
        /// <para>
        /// Struggle input is read here and only on the machine that owns this player, because that
        /// is the only machine with their input. Reading it anywhere else is the defect the leash's
        /// deleted <c>dropAction</c> shipped with — an <c>InputActionReference</c> read on every
        /// copy of a thing, so the local key pressed every remote player's version of it.
        /// </para>
        /// <para>
        /// <b>Two input channels, and either one counts.</b> A Jump press or a Move direction
        /// reversal. Neither is required, which is what lets a player who cannot use one use the
        /// other, and the meter's own cap means neither can be spammed for advantage.
        /// </para>
        /// </summary>
        public void Step(float delta, bool jumpPressed, Vector2 move)
        {
            if (!bound || meter == null) return;

            meter.Advance(delta);

            if (!Network.Owns(this)) return;

            bool reversed = Vector2.Dot(move, lastMove) < 0f && move.sqrMagnitude > 0.25f;
            if (move.sqrMagnitude > 0.25f) lastMove = move;

            if (!jumpPressed && !reversed) return;

            // Push answering false means the input landed inside the cooldown. Not sending it is
            // the point: the cap throttles the wire as well as the meter, so this is at most
            // MaxUsefulStruggleRate tiny messages a second per netted player.
            if (!meter.Push()) return;

            NetMessaging.NetSendTo(gameObject, NetMsg.SnareStruggle,
                                   new NetArg(a: 0).With(gameObject), NetTo.Server);
        }
```

> `Rigidbody`, `ShuffleRadius` and the whole radial correction go. `FixedUpdate` is replaced in
> Task 7, where the input actions are wired; leave the file without one for now — the type-check
> proves it compiles and Task 7's test proves it runs.

Update the file header, replacing the paragraph about holding a player back:

```csharp
// The half of a net that only the netted PLAYER's machine can run.
//
// SnareTether is the creature end. A player is not that: their body is owner-authoritative, so a
// net thrown at a player has to be applied by that player, on their own machine.
//
// What that means changed with the wrap. This used to be a constraint that wrote a position every
// tick to hold a standing player inside a radius; it is now a hold that puts them on the ground and
// a reader for the one thing only this machine knows — whether they are struggling. Going limp
// needs no ownership gate at all, because the ragdoll already has one.
```

- [ ] **Step 4: Rewrite `SnareTether` as a hold**

In `SnareTether.Bind`, replace `CapSpeed();` with:

```csharp
            // A ridden mount refuses to be knocked down (AgentRagdoll.CanBeKnockedDown is false
            // while somebody is aboard), and a net that silently did nothing to a mounted nomad
            // would be a no-op with a clean console. Fall back to the hobble that used to be the
            // only behaviour — the rider is unseated above, so this is an animal being slowed, not
            // a vehicle being frozen.
            ragdoll = GetComponentInParent<AgentRagdoll>();
            hobbled = ragdoll == null || !ragdoll.HoldDown();

            if (hobbled) CapSpeed();
```

Add the two fields beside `navAgent`:

```csharp
        private AgentRagdoll ragdoll;

        /// <summary>True when this creature could not be put down and is being slowed instead.</summary>
        private bool hobbled;
```

In `SnareTether.Release`, before whatever it already does to restore the speed, add:

```csharp
            if (ragdoll != null && !hobbled) ragdoll.ReleaseHold();
```

and leave the existing speed restore guarded by `if (hobbled)`.

Delete `StrugglePull` and the thrash that computes it, along with any `Update`/`FixedUpdate` that
existed only to apply the constraint. Keep `Mass` — `SnareCatch.StrugglingMass` still reads it.

- [ ] **Step 5: Write the fallback test**

Append inside `NetGunTests`:

```csharp
        [Test]
        public void Tether_HobblesWhatItCannotKnockDown()
        {
            // A ridden mount is the case: AgentRagdoll refuses a knockdown while a rider is aboard,
            // and the net must slow the animal instead of doing nothing at all.
            var creature = new GameObject("Creature");
            var agent = creature.AddComponent<NavMeshAgent>();
            agent.speed = 6f;

            var tether = SnareTether.Ensure(creature);
            tether.Bind(new GameObject("Net").transform, new SnareStruggle());

            Assert.Less(agent.speed, 6f,
                        "With no AgentRagdoll to hold it down, the creature has to be hobbled — " +
                        "otherwise netting a mounted nomad is a silent no-op.");

            Object.DestroyImmediate(creature);
        }
```

- [ ] **Step 6: Run the type-check, then the tests**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0. If it still names `ShuffleRadius`, `HobbleSpeed`, `ThrashFrequency`,
`ThrashShare` or `DragInfluence`, a call site was missed — Task 8 removes the last one
(`SnareCatch.DragTowardCaptives`), so that one failure is expected until then and may be left.

Test Runner → EditMode → `NetGunTests`. Expected: `Tether_HobblesWhatItCannotKnockDown` passes.

- [ ] **Step 7: Stage the change**

```bash
git add Assets/Game/Scripts/Items/Artifacts/NetGun/SnareStruggle.cs Assets/Game/Scripts/Items/Artifacts/NetGun/SnaredBody.cs Assets/Game/Scripts/Items/Artifacts/NetGun/SnareTether.cs Assets/Game/Editor/Tests/NetGunTests.cs
```

Ask the user to authorise: `feat: a netted body goes limp instead of being held on its feet`

---

## Task 7: The struggle message, the input wiring, and the drain

**Files:**
- Modify: `Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnaredBody.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareReceiver.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareCatch.cs`

- [ ] **Step 1: Add the message id**

In `NetMsg.cs`, after `SnareFreed = 89`, extend the existing comment block for the snare family and
add:

```csharp
        //   SnareStruggle  victim's owner → server, on the VICTIM's relay. Target = the victim.
        //                  Sent at most SnareStruggle.MaxUsefulStruggleRate times a second, because
        //                  the meter's own cooldown decides whether an input is worth sending. It
        //                  carries no magnitude: the server holds the meter that turns a count of
        //                  inputs into a load, so a client cannot report a struggle harder than the
        //                  cap allows (GDC-L1-MP-0004).
        public const ushort SnareStruggle = 98; // owner → server, on the VICTIM's relay
```

> 98 is the next free id — 97 (`ArrivalLaunched`) is currently the highest. Confirm with
> `grep -oE 'ushort [A-Za-z]+ *= *9[0-9]' Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs`
> before writing it, and take the next free number if anything has landed since.

- [ ] **Step 2: Give `SnaredBody` its input**

Add to `SnaredBody`, replacing the `FixedUpdate` removed in Task 6:

```csharp
        private PlayerInput input;

        private void Awake() => input = GetComponentInParent<PlayerInput>();

        private void Update()
        {
            if (!bound) return;

            bool jump = false;
            Vector2 move = Vector2.zero;

            // Read through the player's own PlayerInput rather than an InputActionReference on this
            // component. A reference read here would fire for every copy of this component on the
            // machine, which on a peer is every OTHER player who is also netted.
            if (input != null && Network.Owns(this))
            {
                jump = input.actions["Jump"].WasPressedThisFrame();
                move = input.actions["Move"].ReadValue<Vector2>();
            }

            Step(Time.deltaTime, jump, move);
        }
```

Add `using UnityEngine.InputSystem;` to the file's usings.

> Read `PlayerMovement` or `PlayerController` first and use whatever this project already uses to
> reach the local player's actions. If those components expose the jump/move values directly,
> prefer reading them off that component over touching `PlayerInput` here — the codebase's existing
> route always wins over a new one.

- [ ] **Step 3: Handle it on the receiver**

In `SnareReceiver`, register the handler in `OnEnable` beside the existing two:

```csharp
            this.NetOn(NetMsg.SnareStruggle, OnSnareStruggle);
```

and unregister it in `OnDisable`:

```csharp
            this.NetOff(NetMsg.SnareStruggle, OnSnareStruggle);
```

Add the handler beside `OnSnared`:

```csharp
        /// <summary>
        /// A captive reports that it is fighting. Server only — the peers do not decide.
        ///
        /// <para>
        /// The message carries no magnitude, only the fact of one input. What that is worth is
        /// decided here against a meter the server owns, so a client that fabricates a hundred of
        /// these a second gains nothing the cap does not already allow.
        /// </para>
        /// </summary>
        private void OnSnareStruggle(in NetArg arg, ulong sender)
        {
            if (!Decides) return;

            GameObject victim = arg.Resolve();
            if (victim == null) return;

            foreach (Tracked tracked in live.Values)
            {
                if (tracked.Net == null) continue;
                if (!tracked.Net.Captives.Contains(victim)) continue;

                tracked.Net.PushStruggle(victim);
                return;
            }
        }
```

- [ ] **Step 4: Feed it into the drain**

In `SnareCatch`, add beside the `captives` list:

```csharp
        /// <summary>
        /// One meter per captive, on the authority only.
        ///
        /// Keyed by the captive rather than held on it, because the meter that DECIDES is the
        /// server's — the copy on the victim's own SnaredBody exists to throttle their sending, and
        /// two machines counting the same inputs into two meters is exactly the split the design
        /// wants: theirs bounds the wire, this one bounds the escape.
        /// </summary>
        private readonly Dictionary<GameObject, SnareStruggleMeter> struggling =
            new Dictionary<GameObject, SnareStruggleMeter>();
```

Add the public seam beside `Capture`:

```csharp
        /// <summary>One accepted struggle input from a captive. Authority only.</summary>
        public void PushStruggle(GameObject captive)
        {
            if (captive == null || !captives.Contains(captive)) return;

            if (!struggling.TryGetValue(captive, out SnareStruggleMeter meter))
            {
                meter = new SnareStruggleMeter(struggle.MaxUsefulStruggleRate,
                                               struggle.StruggleDecaySeconds);
                struggling[captive] = meter;
            }

            meter.Push();
        }
```

Replace `StrugglingMass` with:

```csharp
        /// <summary>
        /// What the net is carrying, in kilogrammes, including how hard it is being fought.
        ///
        /// <para>
        /// A struggling captive simply presents more mass, which is why nothing about
        /// <see cref="SnareIntegrity.Drain"/> had to change: it already turns summed mass into a
        /// multiple of one ordinary captive.
        /// </para>
        /// <para>
        /// Creatures need none of this. Their <see cref="SnareTether.Mass"/> already scales with
        /// what they weigh, and that IS their struggle — a heavier animal has always torn out
        /// faster. Only a player, whose mass here is a flat constant, needs the meter.
        /// </para>
        /// </summary>
        private float StrugglingMass()
        {
            float total = 0f;

            foreach (GameObject captive in captives)
            {
                if (captive == null) continue;

                if (captive.TryGetComponent(out SnareTether tether))
                {
                    total += tether.Mass;
                    continue;
                }

                float level = struggling.TryGetValue(captive, out SnareStruggleMeter meter)
                    ? meter.Level
                    : 0f;

                total += SnareIntegrity.ReferenceLoad * (1f + struggle.StruggleMultiplier * level);
            }

            return total;
        }
```

And advance the meters — in `SnareCatch.Advance`, immediately before `integrity.Drain(...)`:

```csharp
            foreach (SnareStruggleMeter meter in struggling.Values) meter.Advance(delta);
```

Clear them in `ReleaseAll`, after `captives.Clear();`:

```csharp
            struggling.Clear();
```

- [ ] **Step 5: Run the type-check**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

- [ ] **Step 6: Stage the change**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs Assets/Game/Scripts/Items/Artifacts/NetGun/
```

Ask the user to authorise: `feat: struggling out of a net, capped and server-decided`

---

## Task 8: The phase machine in `SnareCatch`

This is where the parts meet — and the spec records that the previous round's defects 1, 3 and 4 all
lived exactly here, in the file that had no tests at all. It gets tests.

**Files:**
- Create: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnarePhase.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/NetGun/SnareCatch.cs`
- Test: `Assets/Game/Editor/Tests/NetGunTests.cs`

- [ ] **Step 1: Write the phase enum**

Create `Assets/Game/Scripts/Items/Artifacts/NetGun/SnarePhase.cs`:

```csharp
namespace SpaceGame.Items
{
    /// <summary>
    /// Where one net is in its life.
    ///
    /// <para>
    /// An enum rather than the three booleans this replaces. <see cref="SnareCatch"/> used to infer
    /// its state from <c>landed</c>, <c>landedElapsed</c> and <c>rotElapsed &gt;= 0f</c>, which
    /// answers two states unambiguously and five not at all — "landed and cinching" and "landed and
    /// bound" are the same three flags.
    /// </para>
    /// </summary>
    public enum SnarePhase
    {
        /// <summary>Carried along the closed-form arc, solver alive.</summary>
        Flight,

        /// <summary>Closing around a body, solver alive plus the cinch constraint.</summary>
        Cinching,

        /// <summary>Closed and frozen, cord riding the captive's bones. Solver off.</summary>
        Bound,

        /// <summary>Landed on nothing and frozen where it lay. Solver off.</summary>
        Fallen,

        /// <summary>Given out, dissolving.</summary>
        Tearing,
    }
}
```

- [ ] **Step 2: Write the failing tests**

Append inside `NetGunTests`:

```csharp
        // ── The phase machine ─────────────────────────────────────────────────

        /// <summary>A net in flight, driven by Advance rather than by a play session.</summary>
        private static SnareCatch FiredNet(out GameObject shooter)
        {
            shooter = new GameObject("Shooter");

            var net = new GameObject("Net").AddComponent<SnareCatch>();
            net.Begin(netId: 1, origin: new Vector3(0f, 2f, 0f), aim: Vector3.forward,
                      halfWidth: HalfWidth, cord: 0.028f, source: new SnareLattice(),
                      struggleSettings: new SnareStruggle(), authority: true, firedBy: shooter);

            return net;
        }

        [Test]
        public void Net_StartsInFlight()
        {
            SnareCatch net = FiredNet(out GameObject shooter);

            Assert.AreEqual(SnarePhase.Flight, net.Phase);

            Object.DestroyImmediate(net.gameObject);
            Object.DestroyImmediate(shooter);
        }

        [Test]
        public void Net_ThatMissesFallsAndStopsSolving()
        {
            SnareCatch net = FiredNet(out GameObject shooter);

            net.LandOnGround(groundY: 0f);
            Assert.AreEqual(SnarePhase.Fallen, net.Phase);

            for (int i = 0; i < 60; i++) net.Advance(Substep);

            Assert.IsTrue(net.LatticeForTest.Frozen,
                          "A net that caught nothing must stop solving. The saving is the point.");

            Object.DestroyImmediate(net.gameObject);
            Object.DestroyImmediate(shooter);
        }

        [Test]
        public void Net_ThatHitsABodyCinchesThenBinds()
        {
            SnareCatch net = FiredNet(out GameObject shooter);

            var victim = new GameObject("Victim");
            victim.tag = "Player";

            net.LandOnBody(victim, contact: Vector3.zero, up: Vector3.up);
            Assert.AreEqual(SnarePhase.Cinching, net.Phase);
            Assert.IsFalse(net.LatticeForTest.Frozen, "The solver has to stay alive through the cinch.");

            for (int i = 0; i < 120; i++) net.Advance(Substep);

            Assert.AreEqual(SnarePhase.Bound, net.Phase,
                            "The cinch is a WINDOW. A net that never leaves it never stops solving.");
            Assert.IsTrue(net.LatticeForTest.Frozen);

            Object.DestroyImmediate(victim);
            Object.DestroyImmediate(net.gameObject);
            Object.DestroyImmediate(shooter);
        }

        [Test]
        public void Net_TearsWhateverPhaseItIsIn()
        {
            SnareCatch net = FiredNet(out GameObject shooter);

            net.LandOnGround(groundY: 0f);
            net.Tear();

            Assert.AreEqual(SnarePhase.Tearing, net.Phase);

            Object.DestroyImmediate(net.gameObject);
            Object.DestroyImmediate(shooter);
        }

        [Test]
        public void Net_StillExpiresOnItsOwnWithNoAuthority()
        {
            // The failsafe: a peer's net never drains and waits to be told it has torn, and that
            // message never arrives if the shooter despawns with nets live — nothing can be sent at
            // that moment, by anyone. Each net has to know its own worst case.
            SnareCatch net = FiredNet(out GameObject shooter);
            net.LandOnGround(groundY: 0f);

            for (int i = 0; i < 90 * 120; i++) net.Advance(Substep);

            Assert.AreEqual(SnarePhase.Tearing, net.Phase);

            Object.DestroyImmediate(net.gameObject);
            Object.DestroyImmediate(shooter);
        }
```

- [ ] **Step 3: Run the type-check to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `'SnareCatch' does not contain a definition for 'Phase'`.

- [ ] **Step 4: Replace the flags with the phase**

In `SnareCatch`, delete the `landed` and `rotElapsed`-as-state fields' role as the state and add:

```csharp
        [Tooltip("Seconds the net takes to close around what it hit.")]
        private const float CinchSeconds = 0.7f;

        /// <summary>Metres from the victim's centre line the cord is drawn to.</summary>
        private const float CinchRadius = 0.45f;

        private SnarePhase phase = SnarePhase.Flight;
        private float cinchElapsed;
        private readonly SnareBinding binding = new SnareBinding();
        private Vector3[] boundNodes;

        /// <summary>Where this net is in its life.</summary>
        public SnarePhase Phase => phase;

        /// <summary>The lattice, for the tests that assert it has stopped. Nothing in the game reads it.</summary>
        public SnareLattice LatticeForTest => lattice;
```

Keep `HasLanded` as a derived property so `SnareReceiver` is unchanged:

```csharp
        /// <summary>Has the net finished flying? True for every phase past Flight.</summary>
        public bool HasLanded => phase != SnarePhase.Flight;
```

- [ ] **Step 5: Split `Land` into the two outcomes**

Replace `Land()` with:

```csharp
        /// <summary>
        /// The net reached the ground without touching anybody. Freeze it where it lies.
        ///
        /// <para>
        /// Public because <see cref="Advance"/> is the tests' only seam and the two landings have
        /// to be reachable from there. Nothing else calls them.
        /// </para>
        /// </summary>
        public void LandOnGround(float groundY)
        {
            if (phase != SnarePhase.Flight) return;

            phase = SnarePhase.Fallen;
            landedElapsed = 0f;
            groundHeight = groundY;

            // One last drape, so the shape it freezes with is the shape the ground gave it rather
            // than the shape it had a substep before touching.
            drape.Resolve(lattice, proxies, groundHeight);
            lattice.GripGround(groundHeight);
            lattice.Freeze();
        }

        /// <summary>
        /// The net touched a body. Start closing around it.
        ///
        /// <para>
        /// <b>The axis is sampled here, once.</b> The body is about to topple — that is the whole
        /// point — and a cinch frame that tumbled with it would sweep the ring, and every node it
        /// holds, through the ground.
        /// </para>
        /// </summary>
        public void LandOnBody(GameObject body, Vector3 contact, Vector3 up)
        {
            if (phase != SnarePhase.Flight) return;

            phase = SnarePhase.Cinching;
            landedElapsed = 0f;
            cinchElapsed = 0f;
            groundHeight = SampleGround(contact);

            lattice.BeginCinch(new SnareCinch.Axis(contact, up), CinchRadius, CinchSeconds);
        }

        /// <summary>
        /// The cinch window is over: stop solving and nail the cord to the captive's bones.
        ///
        /// <para>
        /// Falls back to <see cref="SnarePhase.Fallen"/> when there is nothing to bind to. A rig
        /// with no skeleton reports it SILENTLY — <c>Golem</c>, <c>CrabWalker6</c> and
        /// <c>HumanoidRobot</c> have zero SkinnedMeshRenderers — and a net that bound to nothing
        /// would hang in mid-air where the creature used to be.
        /// </para>
        /// </summary>
        private void Bind()
        {
            lattice.Freeze();

            GameObject captive = captives.Count > 0 ? captives[0] : null;
            RagdollRig rig = captive != null ? captive.GetComponentInParent<RagdollRig>() : null;

            if (rig != null && rig.HasSkeleton)
            {
                binding.Capture(lattice.Positions, rig.BoneTransforms());
                boundNodes = new Vector3[lattice.Positions.Length];
            }

            phase = binding.IsBound ? SnarePhase.Bound : SnarePhase.Fallen;
        }
```

- [ ] **Step 6: Rewrite `Advance` around the phases**

Replace `SnareCatch.Advance` with:

```csharp
        public void Advance(float delta)
        {
            if (lattice == null) return;

            switch (phase)
            {
                case SnarePhase.Flight:
                    CarryAlongFlight(delta);
                    RefreshProxies();
                    lattice.Simulate(delta);
                    drape.Resolve(lattice, proxies, groundHeight);
                    lattice.GripGround(groundHeight);
                    break;

                case SnarePhase.Cinching:
                    landedElapsed += delta;
                    cinchElapsed += delta;
                    RefreshProxies();
                    lattice.Simulate(delta);
                    drape.Resolve(lattice, proxies, groundHeight);

                    // The cinch is a window and it has to close. A net left solving is the cost
                    // this whole change exists to remove.
                    if (cinchElapsed >= CinchSeconds) Bind();
                    break;

                case SnarePhase.Bound:
                    landedElapsed += delta;
                    binding.Resolve(boundNodes);
                    break;

                case SnarePhase.Fallen:
                    landedElapsed += delta;
                    break;

                case SnarePhase.Tearing:
                    rotElapsed += delta;
                    if (rotElapsed >= RotSeconds) Destroy(gameObject);
                    return;
            }

            Redraw();

            lifeElapsed += delta;

            // The failsafe, and it runs on every machine rather than only the authority.
            //
            // A peer's net never drains — it waits to be told it has torn — so if that message
            // never arrives the net holds its captives forever. It does not arrive when the shooter
            // despawns with nets live: the announcement goes out on the SHOOTER's relay, and a
            // player being destroyed has no relay left to send from. Nothing can be sent at that
            // moment, by anyone, so the only honest answer is for each net to know its own worst
            // case and stop by itself.
            if (lifeElapsed >= maxLifeSeconds) Tear();

            if (!authoritative) return;

            foreach (SnareStruggleMeter meter in struggling.Values) meter.Advance(delta);

            integrity.Drain(StrugglingMass(), delta);
            if (integrity.IsSpent) Tear();
        }
```

Update `Tear` to set the phase:

```csharp
        public void Tear()
        {
            if (phase == SnarePhase.Tearing) return;

            ReleaseAll();
            phase = SnarePhase.Tearing;
            rotElapsed = 0f;
        }
```

Delete `DragTowardCaptives` and its call — it read `struggle.DragInfluence`, which no longer
exists, and it moved a net that is now nailed to bones.

Have `Redraw` read the bound positions when there are any:

```csharp
            // A bound net's nodes come from the bones, not the solver. Everything downstream —
            // the mesh, the winding, the origin correction — is unchanged.
            Vector3[] nodes = phase == SnarePhase.Bound && boundNodes != null
                ? boundNodes
                : lattice.Positions;
```

> `SnareMesh.Build` currently takes the lattice. Give it an overload taking `Vector3[]` plus the
> resolution, and have the existing entry point call it — do not duplicate the ribbon-winding code,
> which the previous round's notes single out as the part that fails silently when it is wrong.

- [ ] **Step 7: Route the flight's impact to the right landing**

In `CarryAlongFlight`, where `TryFindImpact` currently leads to `Land()`, replace with:

```csharp
                // The sphere cast MUST skip the shooter: the net is born inside the player, so an
                // unfiltered cast lands every shot at their own feet.
                GameObject struck = ImpactBody(hit);

                bool catchable = struck != null && (struck.CompareTag("Player") ||
                                                    struck.GetComponentInParent<AgentController>() != null);

                if (catchable) LandOnBody(struck, point, struck.transform.up);
                else LandOnGround(SampleGround(point));
```

with the helper beside `TryFindImpact`:

```csharp
        /// <summary>The body a cast hit, resolved through its Rigidbody so a limb answers as its owner.</summary>
        private static GameObject ImpactBody(RaycastHit hit) =>
            hit.collider == null ? null
            : hit.collider.attachedRigidbody != null ? hit.collider.attachedRigidbody.gameObject
            : hit.collider.gameObject;
```

- [ ] **Step 8: Run the type-check, then the tests**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

Test Runner → EditMode → `NetGunTests`. Expected: the five `Net_*` tests pass, and every earlier
test in this plan still passes.

- [ ] **Step 9: Stage the change**

```bash
git add Assets/Game/Scripts/Items/Artifacts/NetGun/
```

Ask the user to authorise: `feat: net phase machine — flight, cinch, bind, fall, tear`

---

## Task 9: The hogtie

**Files:**
- Create: `Assets/Game/Scripts/Items/Artifacts/NetGun/Hogtie.cs`
- Modify: `Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs`
- Test: `Assets/Game/Editor/Tests/HogtieTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Game/Editor/Tests/HogtieTests.cs`:

```csharp
// What a tie has to keep being true about itself.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp. Same reason as NetGunTests.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class HogtieTests
    {
        private const float Step = 1f / 60f;

        private static Hogtie Tied(GameObject victim)
        {
            Hogtie tie = Hogtie.Ensure(victim);
            tie.Bind(new Hogtie.Settings());
            return tie;
        }

        [Test]
        public void Tie_LastsTwoMinutesUntouched()
        {
            var victim = new GameObject("Victim");
            Hogtie tie = Tied(victim);

            for (int i = 0; i < 60 * 110; i++) tie.Advance(Step);
            Assert.IsTrue(tie.IsBound, "At 110 s an untouched tie is still holding.");

            for (int i = 0; i < 60 * 20; i++) tie.Advance(Step);
            Assert.IsFalse(tie.IsBound, "At 130 s it has given out. The ceiling is two minutes.");

            Object.DestroyImmediate(victim);
        }

        [Test]
        public void Tie_YieldsFasterToAStruggle()
        {
            var victim = new GameObject("Victim");
            Hogtie tie = Tied(victim);

            float elapsed = 0f;

            while (tie.IsBound && elapsed < 130f)
            {
                tie.PushStruggle();
                for (int i = 0; i < 24; i++)
                {
                    tie.Advance(Step);
                    elapsed += Step;
                }
            }

            // Two minutes with no input at all is the thing PlayerRagdoll's own header calls out:
            // a state the player did not ask for has to be priced by bounding it. Struggling
            // perfectly has to buy a real fraction of that back.
            Assert.Less(elapsed, 70f, "Struggling flat out must beat the idle timer substantially.");
            Assert.Greater(elapsed, 25f, "But a tie has to be meaningfully stronger than a net.");

            Object.DestroyImmediate(victim);
        }

        [Test]
        public void Tie_RefusesAStandingTarget()
        {
            var standing = new GameObject("Standing");

            Assert.IsFalse(Hogtie.CanTie(standing),
                           "You cannot tie somebody who is on their feet. The tie is what you do " +
                           "to a body a net already put down.");

            Object.DestroyImmediate(standing);
        }

        [Test]
        public void Tie_SurvivesTheNetTearingOutFromUnderIt()
        {
            var victim = new GameObject("Victim");
            Hogtie tie = Tied(victim);

            SnaredBody snared = SnaredBody.Ensure(victim);
            snared.Bind(new GameObject("Net").transform, new SnareStruggle());
            snared.Release(null);

            Assert.IsTrue(tie.IsBound,
                          "A net rotting away must not undo a tie somebody spent a rope on. The " +
                          "two pools are independent — that is what 'so he cannot get up' means.");

            Object.DestroyImmediate(victim);
        }

        [Test]
        public void Untie_ReleasesImmediately()
        {
            var victim = new GameObject("Victim");
            Hogtie tie = Tied(victim);

            tie.Untie();

            Assert.IsFalse(tie.IsBound);

            Object.DestroyImmediate(victim);
        }
    }
}
```

- [ ] **Step 2: Run the type-check to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `The name 'Hogtie' does not exist in the current context`.

- [ ] **Step 3: Add the two message ids**

In `NetMsg.cs`, after `SnareStruggle = 98`:

```csharp
        // A body tied with a leash. Hogtied says who and for how long a pool; HogtieFreed ends it,
        // whether that was the timer, a struggle or somebody cutting them loose.
        //
        //   Hogtied      server → everyone, on the VICTIM's relay. Target = the victim.
        //   HogtieFreed  server → everyone, on the VICTIM's relay. Target = the victim.
        public const ushort Hogtied     = 99;  // server → everyone, on the VICTIM's relay
        public const ushort HogtieFreed = 100; // server → everyone, on the VICTIM's relay
```

- [ ] **Step 4: Write `Hogtie`**

Create `Assets/Game/Scripts/Items/Artifacts/NetGun/Hogtie.cs`:

```csharp
// A body tied with a leash so it cannot get up.
//
// In the NetGun folder rather than the Leash one because of what it IS rather than what applies it.
// The leash is the verb; this is the same family of state as SnaredBody — a hold on a ragdoll with
// its own pool, drained by the same struggle. It reuses SnareIntegrity and SnareStruggleMeter
// outright, which is the whole reason a two-minute tie costs a small file rather than a system.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Items
{
    /// <summary>
    /// Rope around a downed body, holding it down past the net that put it there.
    ///
    /// <para>
    /// <b>Its pool is its own.</b> A net and a tie drain separately and either may end without the
    /// other — a net that rots out from under a tied body leaves it tied, which is the whole point
    /// of tying somebody. <see cref="SnaredBody.Release"/> checks for this component before letting
    /// a player up for exactly that reason.
    /// </para>
    /// <para>
    /// <b>It can always be escaped.</b> Two minutes of no input is the state
    /// <c>PlayerRagdoll</c>'s own header exists to rule out: a state the player did not ask for has
    /// to be priced by bounding it. A tie nobody could break would also be a griefing tool that the
    /// design, not the moderation, has to answer for (GDC-L1-MP-0002). So the ceiling is the
    /// backstop, a struggle is always worth something, and a third party can cut them loose.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class Hogtie : MonoBehaviour
    {
        /// <summary>What a tie is worth. Serialized on the leash prefab and handed over on the tie.</summary>
        [System.Serializable]
        public class Settings
        {
            [Tooltip("Seconds a tie holds a body that never struggles. The ceiling.")]
            [SerializeField, Min(1f)] private float holdSeconds = 120f;

            [Tooltip("Struggle inputs per second past which nothing more is gained. Matches the " +
                     "net's cap — one rate for both, so a player learns it once.")]
            [SerializeField, Min(0.1f)] private float maxUsefulStruggleRate = 2.5f;

            [Tooltip("Seconds a struggle takes to fade once the captive stops.")]
            [SerializeField, Min(0.05f)] private float struggleDecaySeconds = 1.2f;

            [Tooltip("Extra load a captive struggling flat out puts on the rope, as a multiple of " +
                     "the idle drain.\n\n" +
                     "At 1.67 a perfect struggle gets out in about 45 s against the 120 s ceiling. " +
                     "Lower than the net's multiplier on purpose: a tie is meant to be the " +
                     "stronger of the two.")]
            [SerializeField, Min(0f)] private float struggleMultiplier = 1.67f;

            public float HoldSeconds => Mathf.Max(holdSeconds, 1f);
            public float MaxUsefulStruggleRate => Mathf.Max(maxUsefulStruggleRate, 0.1f);
            public float StruggleDecaySeconds => Mathf.Max(struggleDecaySeconds, 0.05f);
            public float StruggleMultiplier => Mathf.Max(struggleMultiplier, 0f);
        }

        private readonly SnareIntegrity integrity = new SnareIntegrity();

        private Settings settings;
        private SnareStruggleMeter meter;
        private bool bound;

        public bool IsBound => bound;

        /// <summary>0 when the rope is about to give, 1 when it is fresh. For the HUD.</summary>
        public float HoldFraction => integrity.Fraction;

        public static Hogtie Ensure(GameObject victim)
        {
            if (victim == null) return null;

            return victim.TryGetComponent(out Hogtie existing)
                ? existing
                : victim.AddComponent<Hogtie>();
        }

        /// <summary>
        /// May this body be tied at all?
        ///
        /// <para>
        /// Only one that is already down. Tying a running figure is not a thing the animation, the
        /// fiction or the balance supports — the tie is what a second player does to a body a net
        /// has already put on the ground, and refusing it here is what keeps the leash from
        /// becoming a second net gun with no flight and no ammunition.
        /// </para>
        /// </summary>
        public static bool CanTie(GameObject victim)
        {
            if (victim == null) return false;
            if (victim.TryGetComponent(out Hogtie existing) && existing.IsBound) return false;

            if (victim.TryGetComponent(out PlayerRagdoll player)) return player.IsHeldOrDown;
            if (victim.TryGetComponent(out AgentRagdoll agent)) return agent.IsHeld;

            return false;
        }

        /// <summary>Take hold. Runs on every machine, from NetMsg.Hogtied.</summary>
        public void Bind(Settings tieSettings)
        {
            settings = tieSettings ?? new Settings();
            meter = new SnareStruggleMeter(settings.MaxUsefulStruggleRate,
                                           settings.StruggleDecaySeconds);

            integrity.Reset(settings.HoldSeconds);
            bound = true;

            if (TryGetComponent(out PlayerRagdoll player)) player.HoldDown();
            else if (TryGetComponent(out AgentRagdoll agent)) agent.HoldDown();
        }

        /// <summary>One accepted struggle input.</summary>
        public void PushStruggle() => meter?.Push();

        /// <summary>Cut loose by somebody else, or by the pool running out.</summary>
        public void Untie()
        {
            if (!bound) return;

            bound = false;

            // Only if the NET is not also still holding them. The two are independent and either
            // may outlast the other.
            bool stillNetted = TryGetComponent(out SnaredBody snared) && snared.IsBound;

            if (!stillNetted)
            {
                if (TryGetComponent(out PlayerRagdoll player)) player.ReleaseHold();
                else if (TryGetComponent(out AgentRagdoll agent)) agent.ReleaseHold();
            }

            Destroy(this);
        }

        /// <summary>Advance one step without a frame. The seam the EditMode tests use.</summary>
        public void Advance(float delta)
        {
            if (!bound) return;

            meter.Advance(delta);

            // The same trick as the net: a struggling captive simply presents more load, so
            // SnareIntegrity needs no knowledge of ties at all. ReferenceLoad is the unit of "one
            // ordinary captive", so an idle tie presents exactly one and drains at 1/HoldSeconds.
            float load = SnareIntegrity.ReferenceLoad *
                         (1f + settings.StruggleMultiplier * meter.Level);

            integrity.Drain(load, delta);

            if (integrity.IsSpent) Untie();
        }

        private void Update()
        {
            // Authority only decides when it ENDS; every machine holds the body. A peer's tie waits
            // to be told, exactly as a peer's net does.
            if (!Network.Simulates(this)) return;

            Advance(Time.deltaTime);
        }

        private void OnDisable()
        {
            if (bound) Untie();
        }
    }
}
```

> `Hogtie.Advance` drains at `1/HoldSeconds` per second for an idle tie because
> `SnareIntegrity.Drain` divides the load by `ReferenceLoad` — so `ReferenceLoad` in gives a rate of
> 1. `IdleRotShare` (0.25) is below that and never binds here, which is correct: a tie is never
> lighter than one captive's worth.

- [ ] **Step 5: Expose `IsHeldOrDown` on `PlayerRagdoll`**

In `PlayerRagdoll.cs`, beside `HoldDown`:

```csharp
        /// <summary>
        /// Is this player on the ground right now, by any route — a net, a tie, or a blast?
        ///
        /// The question <see cref="Hogtie.CanTie"/> asks. Deliberately broader than
        /// <c>held</c>: a player knocked flat by a repulsor blast is just as tieable as a netted
        /// one, and refusing that would make the two feel like unrelated systems.
        /// </summary>
        public bool IsHeldOrDown => held || (rig != null && rig.IsLimp);
```

- [ ] **Step 6: Add the `Tie` verb to the leash**

In `LeashArtifact.cs`, add the verb constant beside `HitLocal`:

```csharp
        /// <summary>
        /// A click on a body a net has already put down: spend the rope tying them.
        ///
        /// A fifth verb rather than a second meaning for Hit, because the two do genuinely
        /// different things to genuinely different targets, and a control that means two things
        /// depending on the state of what it is pointed at is how a control stops being
        /// predictable — the same argument the empty-handed untie above already makes.
        /// </summary>
        private const int Tie = 4;
```

In `OnRequestUse`, after the self-roping guard and before the knot is measured:

```csharp
            // A downed body is tied, not roped. Checked before the ordinary tie-a-rope path,
            // because a body IS a valid rope anchor and would otherwise swallow the click.
            if (held == null && Hogtie.CanTie(root))
            {
                arg = arg.With(root);
                arg.B = Encode(Tie, 0f);
                return;
            }
```

In `Present`, add the case:

```csharp
                case Tie:
                {
                    GameObject victim = arg.Resolve();
                    if (victim == null) break;

                    Hogtie.Ensure(victim).Bind(tieSettings);

                    // The rope is around them now, so it is not in the player's hands.
                    //
                    // Through the depletion event rather than by reaching into the inventory: this
                    // is the established path an item uses to spend itself, and EquipmentController
                    // already answers it by removing the selected slot and unequipping. Anything
                    // that removed the slot directly would skip the unequip and leave a destroyed
                    // item in the hand.
                    Deplete();
                    break;
                }
```

Add the serialized settings beside the other leash tunables:

```csharp
        [Header("Tying")]
        [Tooltip("What a tie is worth when this leash is spent on a downed body.")]
        [SerializeField] private Hogtie.Settings tieSettings = new Hogtie.Settings();
```

> **The give-back is this task's one genuinely unverified piece of plumbing.** Spending the item is
> settled — `UsableItem.OnItemDepleted` is the event, and `EquipmentController.ItemDepleted` already
> answers it with `inventory.TryRemoveItem(inventory.SelectedSlotIndex)` and `Unequip()`. Returning
> the leash when the tie ends is not: it needs the world's existing item-drop spawn, which the
> `spacegame-artifact` skill documents and which **must be read before writing this step** rather
> than guessed at. Whatever that path is, it is the one to call from `Hogtie.Untie` — a runtime-
> spawned item needs a registered network prefab id or it vanishes for clients and on reload, and
> `LeashArtifact.DropHeld` is not it (that disposes a rope object, not the hotbar item).
>
> If reading that path shows the drop is more than a couple of lines, stop and split it into its own
> task rather than inflating this one.

- [ ] **Step 7: Run the type-check, then the tests**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

Test Runner → EditMode → `HogtieTests`. Expected: all five pass.

- [ ] **Step 8: Stage the change**

```bash
git add Assets/Game/Scripts/Items/Artifacts/NetGun/Hogtie.cs Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs Assets/Game/Editor/Tests/HogtieTests.cs Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs
```

Ask the user to authorise: `feat: tie a downed body with a leash, for up to two minutes`

---

## Task 10: Verify on a real client, then document

Nothing above proves the feature works — the type-check proves it compiles and the EditMode tests
prove the arithmetic. The project's own non-negotiables require a client and a reload.

**Files:**
- Modify: `docs/AI/systems/Artifacts.md`
- Modify: `docs/AI/INVARIANTS.md`
- Modify: `docs/Human/the-systems.md`

- [ ] **Step 1: Play it on a host**

Enter play mode, fire a net at an NPC. Confirm: the net blooms in flight, gathers over ~0.7 s into
folds rather than a smooth tube, the creature goes down, and the cord moves with its limbs.

- [ ] **Step 2: Play it on an actual client**

Launch a second instance via MPPM. From the CLIENT, fire a net at the host's player.

Confirm on **both** screens: the net cinches, the victim goes limp, the cord rides their bones. Then
struggle on the victim's machine and confirm they get up **on both screens** at the same moment.

> A feature that has only been seen working on the host is not finished. `SnaredBody`'s own file
> header exists because the lasso shipped a constraint that ran on the server, wrote a position
> fifty times a second that nobody saw, and did nothing whatsoever to a client — while looking
> perfect to whoever was hosting.

- [ ] **Step 3: Confirm the struggle cap is real**

Hold Jump down with key-repeat, then tap it about twice a second. Time both escapes. They must come
out within a fraction of a second of each other. If mashing is faster, `Push` is being called
outside the meter or the message is being sent without checking its return.

- [ ] **Step 4: Confirm nothing was persisted**

Net a creature, then quicksave and reload. Confirm the creature is standing, unnetted, and that
`snare` and `hogtie` appear nowhere in the save JSON.

> This is the design working, not a bug. A quit-time autosave that captured a limp player reloads a
> world where they cannot move with nothing in the log to say why.

- [ ] **Step 5: Update the docs**

In `docs/AI/systems/Artifacts.md`, rewrite the net gun's rows to describe cinch-and-bind, and
**delete what this change made untrue** — every sentence about the drape holding a body by hem
weight and friction, and about a captive shuffling within a radius. Add to that doc's `## Gotchas`:

```markdown
- **A net cinches by CONSTRAINT, never by projection.** `SnareCinch` supplies a shrinking radial
  target that `SnareLattice` relaxes inside its own Gauss-Seidel loop. Applying it after the loop
  instead makes it a filter that ends every substep off-constraint — a permanent vibration at 90 Hz,
  which reads as a tuning problem and is not one. Projecting cord onto the body directly draws a
  capsule; that version was tried and rejected.
- **`RagdollBudget` will stand a captive up.** A held body is limp for gameplay reasons and the
  budget cannot tell it from a corpse, so enough deaths elsewhere in the world silently free a
  netted player. `RagdollRig.BudgetExempt` is set for the duration of every hold.
- **A ridden mount refuses a knockdown.** `AgentRagdoll.CanBeKnockedDown` is false while somebody is
  aboard, so `SnareTether.Bind` falls back to the speed cap. Without that fallback, netting a
  mounted nomad is a no-op with a clean console.
- **A rig with no skeleton binds nothing, silently.** `Golem`, `CrabWalker6` and `HumanoidRobot` are
  rigid-part rigs. `SnareBinding.IsBound` is how `SnareCatch.Bind` finds out in time to fall back to
  `Fallen` instead of leaving a net hanging in mid-air.
```

Add a `symptoms:` frontmatter entry to that doc for each of the three that would cost real time,
phrased as what you would *see*:

```yaml
  - a netted player stands back up on their own while the net is still drawn around them
  - a net thrown at a mounted rider does nothing at all and the console is clean
  - the net wraps into a smooth tube instead of folding around the body
```

Add to `docs/AI/INVARIANTS.md` — it holds for the lattice, the leash rope and the ragdoll alike:

```markdown
- **A soft correction applied outside a position-based solver's constraint loop is a filter, not a
  constraint.** It moves nodes off their rest lengths by construction, so every substep ends
  off-constraint for the next one to yank back. At 90 Hz that is a permanent vibration rather than a
  small residual. If a rule is something the cord obeys, it is relaxed inside the loop with
  everything else.
```

Add a plain-language paragraph to `docs/Human/the-systems.md` under the net gun, and update the
Human chapters only if this counts as a change in the *shape* of the system — it does: being netted
is now being put on the ground and fighting your way up, which is a new way to play.

- [ ] **Step 6: Regenerate and validate the docs**

```bash
python3 tools/docs_check.py --index
```

Expected: exit code 0. `INDEX.md` and `ROUTING.md` are generated from frontmatter — do not
hand-edit them.

- [ ] **Step 7: Stage the change**

```bash
git add docs/
```

Ask the user to authorise: `docs: net gun wrap, hogtie, and the four gotchas they cost`

---

## Notes for whoever executes this

**Two numbers in here are assumptions, not requirements.** They are flagged in §4.1 of the spec and
they are single fields:

1. `SnareStruggle.struggleMultiplier = 2` — a 10 s perfect-struggle escape from a 30 s net. The
   user specified the tie's two minutes and not this.
2. The tie consuming the leash item. If it should be free and leave the leash in hand, Task 9 Step 6
   loses its `ConsumeRope()` call and `Hogtie.Untie` loses the drop.

Raise both after the first playtest rather than tuning them blind.

**The one thing most likely to go wrong** is the cinch reading as a shrink-wrap.
`Cinch_KeepsItsCordLength` is the test that catches it, and if it fails the cause is almost always
`cinchStiffness` beating the strands — lower it before touching anything else. The bend stiffness
has a documented cliff nearby (0.016 drapes, 0.030 slides off, 0.050 never folds), so sweep the
cinch against the same four metrics rather than adjusting by eye.

**Do not add a `SnareCling`-style helper that gathers cord onto the capsule** if the folds look
wrong. That has been tried and rejected, and the note is one sentence: projecting cord onto a
capsule draws a capsule.
