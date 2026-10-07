# Leash as a Force Contest — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the leash from a distance limit that may only remove velocity into a two-way force contest — the stronger end tows the weaker, mass decides how slowly, multiple ropes sum, and nothing breaks except by a deliberate resist.

**Architecture:** Each end gains a `PullStrength` of `mass × topSpeed`, derived from values already on the prefab so every machine computes the same number with nothing sent. `ShareOf` (inverse mass) is left alone — it answers *resistance*, which is already correct. `LeashEnd.Restrain`, the clamp that forbids a rope from ever adding speed, is deleted and replaced at the same call site by a `TowCap` of `netPull / mass`. Stretch-breaking is deleted outright and replaced by resist strain accumulated from movement input.

**Tech Stack:** Unity 6000.3.11f1, C#, NUnit EditMode tests in `Assembly-CSharp-Editor`, Netcode for GameObjects.

**Design spec:** [2026-09-04-leash-physics-rework-design.md](../specs/2026-09-04-leash-physics-rework-design.md)

---

## Before you start: how to verify anything in this repo

**There is no `unity -batchmode -runTests` path in this project. Do not invent one** — it exits 0 without a licence and tells you nothing. Two real tools:

**Compile check** (fast, works from the shell, use it constantly):
```bash
python3 tools/typecheck.py --editor
```
Expected on success: `Unity 6000.3.11f1`, then `Assembly-CSharp: no errors.` and `Assembly-CSharp-Editor: no errors.`, exit 0. `--editor` is required for every task in this plan, because `LeashConstraintTests` lives in `Assembly-CSharp-Editor`.

**Run the tests** (needs a live Editor — via the Unity window or MCP):
- Menu: `Tools ▸ Tests ▸ Run EditMode Tests (headless)`
- Or externally: `SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("LeashConstraintTests")`
- Then read the verdict:
```bash
cat Temp/headless_tests.txt
```
`HeadlessTestRunner` **deletes that file before starting**, so absence means "still running" and the literal token `DONE` at the end means finished. Poll for it; never assume.

**Two known traps in this suite:** `Awake` and `Start` do **not** run on an `AddComponent`'d MonoBehaviour in EditMode, and `Time.time` starts at 0 outside play mode. Every test in this plan is written against pure static functions for exactly that reason.

---

## File structure

| File | Responsibility | Change |
| --- | --- | --- |
| `Assets/Game/Scripts/agents/AI/Motors/IMovementMotor.cs` | Mover contract | Add `TopSpeed` |
| `Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs` | Creature mover | Implement `TopSpeed` |
| `Assets/Game/Scripts/agents/AI/Motors/RigidbodyMotor.cs` | Ground vehicle mover | Implement `TopSpeed` |
| `Assets/Game/Scripts/agents/AI/Motors/HoverRigidbodyMotor.cs` | Hover mover (the lander) | Implement `TopSpeed` |
| `Assets/Game/Scripts/agents/AI/Motors/FlyingRigidbodyMotor.cs` | Flying mover | Implement `TopSpeed` |
| `Assets/Game/Scripts/agents/AI/Motors/LeggedDriver.cs` | Legged mover | Implement `TopSpeed` off `LeggedLocomotion.MaxSpeed` |
| `Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.cs` | Aircraft mover, already `ITowable` | Implement `TopSpeed` |
| `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs` | The rope, the constraint | `PullOf`, `TowCap`; delete `HasBroken`; per-body resolve |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs` | One knot | `TopSpeed`, `PullStrength`; delete `Restrain`; `ITowable` branch |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashedBody.cs` | Player half | Resist strain; drop the kinematic early-out |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs` | The equipped tool | Terrain test; retire break tunables, add resist tunables |
| `Assets/Game/Editor/Tests/LeashConstraintTests.cs` | Pure-maths proof | New contest tests; retire `Restrain` tests |
| `docs/AI/systems/LeashSystem.md` | Governing doc | Update per CLAUDE.md |

**Naming note, read before Task 3:** `LeashEnd` already has a **method** called `Pull(toward, arrestSpeed, correctionDistance)`. The new strength property is therefore called **`PullStrength`**, never `Pull`. Do not rename the method.

---

### Task 1: `TopSpeed` on every mover

Purely additive — no behaviour changes. This is the number `PullStrength` multiplies by.

**Files:**
- Modify: `Assets/Game/Scripts/agents/AI/Motors/IMovementMotor.cs`
- Modify: `Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs`
- Modify: `Assets/Game/Scripts/agents/AI/Motors/RigidbodyMotor.cs`
- Modify: `Assets/Game/Scripts/agents/AI/Motors/HoverRigidbodyMotor.cs`
- Modify: `Assets/Game/Scripts/agents/AI/Motors/FlyingRigidbodyMotor.cs`
- Modify: `Assets/Game/Scripts/agents/AI/Motors/LeggedDriver.cs`
- Modify: `Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.cs`

- [ ] **Step 1: Add `TopSpeed` to the interface**

In `IMovementMotor.cs`, inside `interface IMovementMotor`, after `Vector3 Velocity { get; }`:

```csharp
        /// <summary>
        /// The best speed this mover can sustain under its own power, in m/s. Used as the
        /// "how hard can it haul" half of a rope's pull strength, so it must be a STABLE figure
        /// off the prefab and never the current speed — both machines resolving a rope derive
        /// their pull from it independently and have to agree without a message.
        /// </summary>
        float TopSpeed { get; }
```

Adding it to the interface is deliberate: the compiler then lists every mover that still owes an answer, rather than letting one silently report zero and be unable to pull.

- [ ] **Step 2: Implement it on the five simple movers**

`NavMeshAgentMotor.cs` — add near the existing `Velocity` property. `defaultSpeed` is assigned in `Awake` (line ~134), and **`Awake` does not run in EditMode**, so the fallback is load-bearing:

```csharp
        public float TopSpeed => defaultSpeed > 0.01f ? defaultSpeed
                               : agent != null ? agent.speed : 0f;
```

`RigidbodyMotor.cs`, `HoverRigidbodyMotor.cs`, `FlyingRigidbodyMotor.cs` — each already has a private `[SerializeField] private float maxSpeed`. Add to each:

```csharp
        public float TopSpeed => maxSpeed;
```

`LeggedDriver.cs` — `LeggedLocomotion.MaxSpeed` is already public, and the driver already holds the locomotion:

```csharp
        public float TopSpeed => locomotion != null ? locomotion.MaxSpeed : 0f;
```

- [ ] **Step 3: Implement it on the ornithopter**

`OrnithopterFlightMotor.cs`. Its config exposes `FullAuthoritySpeed` (`OrnithopterFlightConfig.cs:112`, default 12):

```csharp
        public float TopSpeed => config != null ? config.FullAuthoritySpeed : 0f;
```

Confirm the field name resolves against whatever the motor calls its config reference before committing — if the motor holds no config, use the flight model's cruise figure instead. This number only scales tow strength; it does not affect flight.

- [ ] **Step 4: Compile**

Run: `python3 tools/typecheck.py --editor`
Expected: `Assembly-CSharp: no errors.` and `Assembly-CSharp-Editor: no errors.`, exit 0.

If any mover is reported as not implementing `TopSpeed`, implement it there rather than removing it from the interface — that error is the interface doing its job.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/agents/AI/Motors
git commit -m "feat: expose TopSpeed on every movement motor"
```

---

### Task 2: `PullOf` and `TowCap` — the pure maths

Both are `public static` and pure, matching `ShareOf` / `ArrestSpeed` / `CorrectionDistance`, so they are provable with no scene. **Write the tests first.**

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs`
- Test: `Assets/Game/Editor/Tests/LeashConstraintTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `class LeashConstraintTests` in `Assets/Game/Editor/Tests/LeashConstraintTests.cs`:

```csharp
        [Test]
        public void PullIsMassTimesTopSpeedAndAStaticAnchorHasNone()
        {
            // A heavy slow thing and a light fast thing can be evenly matched. That is the whole
            // point of multiplying rather than picking one of them.
            Assert.That(Leash.PullOf(80f, 6f), Is.EqualTo(480f).Within(1e-3f));
            Assert.That(Leash.PullOf(120f, 9f), Is.EqualTo(1080f).Within(1e-3f));

            // A wall resists infinitely but tows NOTHING. Returning zero here rather than
            // evaluating Infinity * 0 is what keeps a NaN out of the clamp downstream.
            Assert.That(Leash.PullOf(Mathf.Infinity, 6f), Is.Zero);

            // A crate has no engine and no legs.
            Assert.That(Leash.PullOf(400f, 0f), Is.Zero);
        }

        [Test]
        public void TowCapOnlyClampsTheEndThatIsLosing()
        {
            // Out-pulled by 600 with 80 kg to shift: dragged, but at a finite speed.
            Assert.That(Leash.TowCap(600f, 80f), Is.EqualTo(7.5f).Within(1e-3f));

            // Twice the mass, half the speed. This is "heavy stuff is moved slowly".
            Assert.That(Leash.TowCap(600f, 160f), Is.EqualTo(3.75f).Within(1e-3f));

            // Winning, or evenly matched: NOT clamped. Two passive crates roped together both
            // score zero, and a clamp of zero would freeze the rope instead of closing it.
            Assert.That(Leash.TowCap(-600f, 80f), Is.EqualTo(Mathf.Infinity));
            Assert.That(Leash.TowCap(0f, 80f), Is.EqualTo(Mathf.Infinity));

            // An immovable end is never towed however hard it is pulled.
            Assert.That(Leash.TowCap(600f, Mathf.Infinity), Is.Zero);
        }

        [Test]
        public void ShareStillAnswersResistanceRatherThanStrength()
        {
            // Pull and mass answer different questions and must not be conflated. A wall has NO
            // pull, so sharing by pull would hand a player roped to one a share of zero and the
            // rope would stop restraining them. Sharing by mass keeps it at 1.
            Assert.That(Leash.PullOf(Mathf.Infinity, 0f), Is.Zero);
            Assert.That(Leash.ShareOf(80f, Mathf.Infinity), Is.EqualTo(1f).Within(1e-4f));
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run `Tools ▸ Tests ▸ Run EditMode Tests (headless)` in the Editor, then:
```bash
cat Temp/headless_tests.txt
```
Expected: compilation fails outright with `'Leash' does not contain a definition for 'PullOf'`. A compile error rather than a red test is the correct failure here — confirm it with `python3 tools/typecheck.py --editor` if the Editor is not open.

- [ ] **Step 3: Implement both**

In `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs`, immediately after `ShareOf`:

```csharp
        /// <summary>
        /// How hard this end can HAUL — as distinct from how hard it is to move, which is mass.
        ///
        /// <para>
        /// Mass times top speed, because both come off the prefab: the two machines resolving the
        /// two ends derive the same number independently, which is what lets a contest be decided
        /// with nothing on the wire. Nothing in this game publishes a force — every mover is
        /// authored in velocity — so this is the closest honest stand-in for one.
        /// </para>
        /// <para>
        /// A static anchor scores ZERO rather than infinity: it resists everything and tows
        /// nothing. Returning early also keeps <c>Infinity * 0f</c> — a NaN that would poison
        /// every clamp downstream — from ever being evaluated.
        /// </para>
        /// </summary>
        public static float PullOf(float mass, float topSpeed)
        {
            if (float.IsInfinity(mass) || mass <= 0f) return 0f;
            if (topSpeed <= 0f) return 0f;

            return mass * topSpeed;
        }

        /// <summary>
        /// The fastest this end may be dragged, in m/s — force over mass, and the only place
        /// <see cref="PullOf"/> is consulted.
        ///
        /// <para>
        /// This replaces <c>LeashEnd.Restrain</c>, which capped every pull at the speed the body
        /// already had and so made it impossible for a rope to move anything that was standing
        /// still. The cap applies only to the end that is being OUT-PULLED: an end that is winning,
        /// or evenly matched, is not being towed and needs no ceiling. That exemption is what keeps
        /// two passive bodies — two crates, both scoring zero — closing normally rather than
        /// freezing at a cap of nothing.
        /// </para>
        /// </summary>
        public static float TowCap(float netPull, float mass)
        {
            if (netPull <= 0f) return Mathf.Infinity;
            if (float.IsInfinity(mass)) return 0f;

            return netPull / Mathf.Max(0.01f, mass);
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run the EditMode tests as in Step 2, then:
```bash
cat Temp/headless_tests.txt
```
Expected: `FAILED=0`, and the file ends with `DONE`. The three new tests appear in the pass count along with the existing suite.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs Assets/Game/Editor/Tests/LeashConstraintTests.cs
git commit -m "feat: add PullOf and TowCap to the leash constraint"
```

---

### Task 3: `PullStrength` on a rope end

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs`

- [ ] **Step 1: Add the top-speed resolution and the strength**

In `LeashEnd.cs`, directly after the existing `Mass` property:

```csharp
        /// <summary>
        /// The best speed the thing on this end can manage under its own power, in m/s.
        ///
        /// <para>
        /// Resolved in order of specificity, because a ridden walker carries both a legged
        /// locomotion and a driver and the locomotion is the one that owns the figure. A crate
        /// answers zero and therefore tows nothing, which is correct: it has no engine.
        /// </para>
        /// </summary>
        public float TopSpeed
        {
            get
            {
                if (Anchor == null) return 0f;

                var motor = Anchor.GetComponentInParent<IMovementMotor>();
                if (motor != null) return Mathf.Max(0f, motor.TopSpeed);

                var movement = Anchor.GetComponentInParent<PlayerMovement>();
                if (movement != null) return Mathf.Max(0f, movement.SprintSpeed);

                return 0f;
            }
        }

        /// <summary>How hard this end can haul. See <see cref="Leash.PullOf"/>.</summary>
        public float PullStrength => Leash.PullOf(Mass, TopSpeed);
```

- [ ] **Step 2: Expose the player's sprint speed**

`Movement.cs:21` has `[SerializeField] private float sprintSpeed = 9f`, with no public read — it is
consumed only at line 190. Add a read beside the existing `HorizontalSpeed` property (line ~101):

```csharp
        /// <summary>
        /// Top speed under the player's own power, in m/s. Read by the leash as the "how hard can
        /// it haul" half of a pull strength — so it is the authored ceiling, not the current speed.
        /// </summary>
        public float SprintSpeed => sprintSpeed;
```

Getter only. Do not rename the field and do not add a setter.

- [ ] **Step 3: Compile**

Run: `python3 tools/typecheck.py --editor`
Expected: `Assembly-CSharp: no errors.` and `Assembly-CSharp-Editor: no errors.`, exit 0.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs Assets/Game/Scripts/Characters/Player/Movement/Movement.cs
git commit -m "feat: give a leash end a pull strength"
```

---

### Task 4: Delete `Restrain`, apply `TowCap`

The rule change. After this task a rope can tow.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs`
- Test: `Assets/Game/Editor/Tests/LeashConstraintTests.cs`

- [ ] **Step 1: Update the test harness to model the tow cap**

In `LeashConstraintTests.cs`, replace the body of the `Settle` helper's constraint block so it matches what `ResolveEnd` will now do. Replace these lines:

```csharp
                    velA += Leash.ArrestSpeed(separation, shareA, MaxSpeed);
                    posA += Leash.CorrectionDistance(stretch, shareA, Correction, MaxStep);

                    velB -= Leash.ArrestSpeed(separation, shareB, MaxSpeed);
                    posB -= Leash.CorrectionDistance(stretch, shareB, Correction, MaxStep);
```

with:

```csharp
                    float capA = Leash.TowCap(pullB - pullA, massA);
                    float capB = Leash.TowCap(pullA - pullB, massB);

                    velA += Leash.ArrestSpeed(separation, shareA, MaxSpeed);
                    posA += Mathf.Min(Leash.CorrectionDistance(stretch, shareA, Correction, MaxStep),
                                      capA * Dt);

                    velB -= Leash.ArrestSpeed(separation, shareB, MaxSpeed);
                    posB -= Mathf.Min(Leash.CorrectionDistance(stretch, shareB, Correction, MaxStep),
                                      capB * Dt);
```

and extend the signature so callers can supply strengths, defaulting to zero so every existing test keeps its current meaning:

```csharp
        private static (float gap, float swing) Settle(
            float massA, float massB, float length, float startGap,
            float driveA = 0f, int steps = 600,
            float pullA = 0f, float pullB = 0f)
```

- [ ] **Step 2: Write the failing contest tests**

Append inside `class LeashConstraintTests`:

```csharp
        [Test]
        public void AStrongerEndDragsAStandingPlayerAlong()
        {
            // The case Restrain forbade outright: a player standing still, roped to something that
            // walks away. They contribute no separating velocity of their own, so the arrest term
            // does nothing and the position correction is the only thing that can move them --
            // which Restrain then clamped back to the speed they already had, i.e. zero.
            (float gap, _) = Settle(massA: 80f, massB: 120f, length: 8f, startGap: 8f,
                                    driveA: 0f, steps: 400,
                                    pullA: Leash.PullOf(80f, 6f),      // player, 480
                                    pullB: Leash.PullOf(120f, 9f));    // ostrich, 1080

            // The rope stays taut, which it can only do if the player came along.
            Assert.That(gap, Is.EqualTo(8f).Within(0.6f));
        }

        [Test]
        public void AHeavierEndIsDraggedMoreSlowlyByTheSamePull()
        {
            // Same contest, twice the mass on the losing end. Force over mass, so half the speed.
            float netPull = 600f;

            Assert.That(Leash.TowCap(netPull, 80f),
                        Is.EqualTo(2f * Leash.TowCap(netPull, 160f)).Within(1e-3f));
        }

        [Test]
        public void TwoPassiveBodiesStillCloseTheirRope()
        {
            // Both score zero pull, so both caps must be uncapped rather than zero. A cap of zero
            // here would freeze two roped crates apart forever.
            (float gap, float swing) = Settle(massA: 400f, massB: 400f, length: 8f, startGap: 12f,
                                              pullA: 0f, pullB: 0f);

            Assert.That(gap, Is.EqualTo(8f).Within(0.02f));
            Assert.That(swing, Is.LessThan(0.01f), "The rope must settle, not ring.");
        }
```

- [ ] **Step 3: Delete the two tests that pin the old rule**

In `LeashConstraintTests.cs`, delete `ALeashCanNeverMakeAPlayerFaster` (line ~151) and `ALeashStillTakesSpeedAwayFromAPlayerRunningIntoIt` (line ~173). The first asserts exactly the behaviour this task removes; the second is subsumed by `ARopeHoldsAPlayerWhoKeepsWalkingIntoIt`, which stays and still passes because `ArrestSpeed` is untouched.

**Keep `NothingInTheLeashReachesForTheGrapplingHooksSwingSteering` (line ~187).** It asserts that no leash source file contains the string `SetTethered(`, and that guard is still correct and still wanted: this design never claims the body and adds no winch, so a player still has no way to pull *themselves* anywhere.

- [ ] **Step 4: Run the tests to verify the new ones fail**

Run `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then:
```bash
cat Temp/headless_tests.txt
```
Expected: `AStrongerEndDragsAStandingPlayerAlong` **FAILS** — the gap opens far past 8 m because nothing tows the player yet. The other two new tests pass already (they exercise only the pure functions from Task 2).

- [ ] **Step 5: Delete `Restrain` and apply the cap**

In `LeashEnd.cs`, delete the entire `Restrain` method and its doc comment. Then change `Pull`'s signature and its two mutable branches. Replace:

```csharp
        public void Pull(Vector3 toward, float arrestSpeed, float correctionDistance)
        {
            if (Body == null) return;
            if (arrestSpeed <= 0f && correctionDistance <= 0f) return;

            Vector3 step = toward * correctionDistance;
```

with:

```csharp
        public void Pull(Vector3 toward, float arrestSpeed, float correctionDistance, float towCap)
        {
            if (Body == null) return;
            if (arrestSpeed <= 0f && correctionDistance <= 0f) return;

            // Force over mass, applied as the ceiling on how far this end may be carried in one
            // step. This is what replaces Restrain: the rope may now ADD speed to the end that is
            // losing the contest, but only as fast as the winner's spare pull can shift its mass.
            correctionDistance = Mathf.Min(correctionDistance, towCap * Time.fixedDeltaTime);

            Vector3 step = toward * correctionDistance;
```

Then in the player branch, replace:

```csharp
                Body.linearVelocity = Restrain(Body.linearVelocity, toward, arrestSpeed);
                Body.position += step;
```

with:

```csharp
                // No clamp on the result any more. A rope may now tow a player who is standing
                // still, which is the whole feature -- and it is safe without Restrain because
                // there is still no winch anywhere in this system, so a player has no way to pull
                // THEMSELVES along a rope. Something else must do the dragging.
                Body.linearVelocity += toward * arrestSpeed;
                Body.position += step;
```

- [ ] **Step 6: Pass the cap in from `ResolveEnd`**

In `Leash.cs`, in `ResolveEnd`, replace the final `self.Pull(...)` call:

```csharp
            self.Pull(toward,
                      ArrestSpeed(separation, share, settings.maxCorrectionSpeed),
                      CorrectionDistance(stretch, share, settings.correction, settings.maxCorrectionStep));
```

with:

```csharp
            float netPull = other.PullStrength - self.PullStrength;

            self.Pull(toward,
                      ArrestSpeed(separation, share, settings.maxCorrectionSpeed),
                      CorrectionDistance(stretch, share, settings.correction, settings.maxCorrectionStep),
                      TowCap(netPull, self.Mass));
```

- [ ] **Step 7: Run the tests to verify they pass**

Run the EditMode tests, then:
```bash
cat Temp/headless_tests.txt
```
Expected: `FAILED=0`, ending in `DONE`.

- [ ] **Step 8: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash Assets/Game/Editor/Tests/LeashConstraintTests.cs
git commit -m "feat: let a leash tow the end that loses the pull contest"
```

---

### Task 5: Delete stretch-breaking

A tow holds a permanent overstretch by design, so any stretch threshold that survives hauling a crate is one that never fires. Resist replaces it in Task 6.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs`

- [ ] **Step 1: Delete the break test**

In `Leash.cs`, delete the whole `HasBroken` method and its doc comment, and delete the `private float overstretchedSince` field it uses. In `FixedUpdate`, delete these lines together with the `mineToDecide` comment block above them:

```csharp
            bool mineToDecide = !Network.IsNetworked || Network.Server || listening == null;

            if (mineToDecide && HasBroken(stretch)) return;
```

`Snap()` itself stays exactly as it is — including the `disposed`-before-send ordering, which is load-bearing against re-entrancy on the host. Task 6 gives it a new caller.

- [ ] **Step 2: Remove the retired tunables**

In `Leash.cs`, delete `breakStretch` and `breakTime` from `struct Settings`.

In `LeashArtifact.cs`, delete the `breakStretch` and `breakTime` serialized fields (lines ~57 and ~60), delete their two lines from the `RopeSettings` property (lines ~412-413), and delete them from the hardcoded fallback inside `TryResolveSettings` (line ~443).

- [ ] **Step 3: Compile**

Run: `python3 tools/typecheck.py --editor`
Expected: no errors, exit 0. If `stretch` is now unused in `FixedUpdate`, keep the `MeasureStretch` call — `UpdateTension` still consumes it.

- [ ] **Step 4: Run the tests**

Run the EditMode tests, then `cat Temp/headless_tests.txt`.
Expected: `FAILED=0`. No existing test covers `HasBroken`, so nothing should go red here.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash
git commit -m "feat: a leash under load no longer snaps on stretch"
```

---

### Task 6: Resist — fight your way out

Strain builds while your movement input points away from the knot, scaled by the captor's pull, and decays when you stop.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashedBody.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs`
- Test: `Assets/Game/Editor/Tests/LeashConstraintTests.cs`

- [ ] **Step 1: Write the failing test**

Append inside `class LeashConstraintTests`:

```csharp
        [Test]
        public void ResistBuildsWhilePullingAwayAndDecaysWhenYouStop()
        {
            const float Decay = 0.5f;

            // Straight away from the knot, against an evenly-matched captor.
            float strain = Leash.ResistStrain(0f, away: 1f, resistSeconds: 2f, dt: 1f, decay: Decay);
            Assert.That(strain, Is.EqualTo(0.5f).Within(1e-3f));

            // Sideways earns nothing: it is the component ALONG the rope that counts.
            Assert.That(Leash.ResistStrain(0f, away: 0f, resistSeconds: 2f, dt: 1f, decay: Decay),
                        Is.Zero);

            // Standing still gives it back.
            Assert.That(Leash.ResistStrain(0.5f, away: 0f, resistSeconds: 2f, dt: 1f, decay: Decay),
                        Is.EqualTo(0f).Within(1e-3f));

            // It never runs negative, so a long rest does not bank credit against the next rope.
            Assert.That(Leash.ResistStrain(0.1f, away: 0f, resistSeconds: 2f, dt: 5f, decay: Decay),
                        Is.Zero);

            // And it is capped, so one very long step cannot overshoot past the snap point.
            Assert.That(Leash.ResistStrain(0.9f, away: 1f, resistSeconds: 2f, dt: 10f, decay: Decay),
                        Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void TearingFreeOfSomethingStrongerTakesLonger()
        {
            // resistSeconds scales with the captor's pull, so a ship holds you longer than a player
            // does. Two seconds against an equal, proportionally more against a ship.
            Assert.That(Leash.ResistSeconds(theirPull: 480f, myPull: 480f, baseSeconds: 2f),
                        Is.EqualTo(2f).Within(1e-3f));

            Assert.That(Leash.ResistSeconds(theirPull: 1920f, myPull: 480f, baseSeconds: 2f),
                        Is.EqualTo(8f).Within(1e-3f));

            // Tearing free of something weaker than you is quick, but never instant.
            Assert.That(Leash.ResistSeconds(theirPull: 0f, myPull: 480f, baseSeconds: 2f),
                        Is.GreaterThan(0f));
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run the EditMode tests, then `cat Temp/headless_tests.txt`.
Expected: compile failure, `'Leash' does not contain a definition for 'ResistStrain'`. Confirm with `python3 tools/typecheck.py --editor` if no Editor is open.

- [ ] **Step 3: Implement the two pure functions**

In `Leash.cs`, after `TowCap`:

```csharp
        /// <summary>
        /// How long a struggle against <paramref name="theirPull"/> should take, in seconds.
        ///
        /// <para>
        /// A ratio rather than a difference, so it stays sane at both ends of the scale: tearing
        /// free of the lander is proportionally harder than tearing free of another player, and
        /// tearing free of a crate is quick without ever being instant.
        /// </para>
        /// </summary>
        public static float ResistSeconds(float theirPull, float myPull, float baseSeconds)
        {
            float mine = Mathf.Max(1f, myPull);
            float ratio = Mathf.Max(0.1f, theirPull / mine);

            return Mathf.Max(0.1f, baseSeconds * ratio);
        }

        /// <summary>
        /// One step of a struggle. <paramref name="away"/> is how squarely the body is pulling
        /// against the rope — the dot of its move input with the direction away from the knot,
        /// so leaning sideways earns nothing.
        ///
        /// <para>
        /// Pure, and takes its own delta, because <c>Time.time</c> starts at zero outside play
        /// mode and an EditMode test that read the clock would be measuring the Editor's frame.
        /// Clamped at both ends: never negative, so resting banks no credit against the next rope,
        /// and never past 1, so a long step cannot overshoot the snap.
        /// </para>
        /// </summary>
        public static float ResistStrain(float strain, float away, float resistSeconds,
                                         float dt, float decay)
        {
            float gain = Mathf.Max(0f, away) / Mathf.Max(0.1f, resistSeconds);

            strain += gain > 0f ? gain * dt : -decay * dt;

            return Mathf.Clamp01(strain);
        }
```

- [ ] **Step 4: Add the tunables**

In `Leash.cs`, add to `struct Settings`:

```csharp
            /// <summary>Seconds of squarely-away struggle to tear free of an equally strong end.</summary>
            public float resistSeconds;

            /// <summary>Strain given back per second when not struggling.</summary>
            public float strainDecay;
```

In `LeashArtifact.cs`, beside the other tunables (where `breakStretch` used to be):

```csharp
        [Header("Resist")]
        [Tooltip("Seconds of pulling squarely away to tear free of an end as strong as you are. " +
                 "Scales with the other end's pull, so a ship holds you far longer than a player.")]
        [SerializeField, Min(0.1f)] private float resistSeconds = 2f;

        [Tooltip("Strain given back per second when you stop pulling away.")]
        [SerializeField, Min(0f)] private float strainDecay = 0.5f;
```

Add both to the `RopeSettings` property and to the hardcoded fallback in `TryResolveSettings`:

```csharp
            resistSeconds = 2f, strainDecay = 0.5f,
```

- [ ] **Step 5: Accumulate strain on the player end**

In `LeashedBody.cs`, add the field and drive it from the existing `FixedUpdate` loop, after `rope.ResolveEnd(mine, rope.Opposite(mine))`:

```csharp
            LeashEnd other = rope.Opposite(mine);

            Vector3 knotToMe = body.position - other.Position;
            if (knotToMe.sqrMagnitude > 1e-4f)
            {
                Vector3 away = knotToMe.normalized;
                Vector3 wish = movement != null ? movement.WishDirection : Vector3.zero;

                float strain = rope.StrainOn(mine);
                float seconds = Leash.ResistSeconds(other.PullStrength, mine.PullStrength,
                                                    rope.ResistBaseSeconds);

                rope.SetStrainOn(mine,
                    Leash.ResistStrain(strain, Vector3.Dot(wish, away), seconds,
                                       Time.fixedDeltaTime, rope.StrainDecay));

                if (rope.StrainOn(mine) >= 1f) rope.Snap();
            }
```

Cache `movement` in `Awake` alongside `body`:

```csharp
        private PlayerMovement movement;

        private void Awake()
        {
            body = GetComponentInChildren<Rigidbody>();
            movement = GetComponentInChildren<PlayerMovement>();
        }
```

`PlayerMovement` has no `WishDirection` today. Add one, returning the **same** world-space direction
`FixedUpdate` already builds at line 255 (`transform.right * moveInput.x + transform.forward * moveInput.y`),
so the struggle reads the player's actual intent rather than a second copy of it:

```csharp
        /// <summary>
        /// Where the player is asking to go, in world space, normalised — zero when they are not
        /// asking. The same vector the move solve builds; exposed so the leash can tell a struggle
        /// against the rope from a stroll along it.
        /// </summary>
        public Vector3 WishDirection
        {
            get
            {
                Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
                return move.sqrMagnitude > 1e-4f ? move.normalized : Vector3.zero;
            }
        }
```

Read `moveInput` (the field assigned at line 219), never `PlayerInputManager` — an item that reads
input directly reads *local* input on every copy of itself on the machine, which is the trap that
got the leash's old `dropAction` deleted.

- [ ] **Step 6: Add the per-end strain store to `Leash`**

In `Leash.cs`, add beside the other per-rope state, plus the settings reads `LeashedBody` uses:

```csharp
        private float strainA, strainB;

        /// <summary>How far through tearing free the given end is, 0 to 1.</summary>
        public float StrainOn(LeashEnd end) => end == A ? strainA : strainB;

        /// <summary>Record a struggle step. Owner-local: strain is never sent, only the snap is.</summary>
        public void SetStrainOn(LeashEnd end, float value)
        {
            if (end == A) strainA = value; else strainB = value;
        }

        /// <summary>The authored base figure. Named to avoid colliding with the static
        /// <see cref="ResistSeconds"/>, which scales this by the captor's pull.</summary>
        public float ResistBaseSeconds => settings.resistSeconds;

        public float StrainDecay => settings.strainDecay;
```

**A property and a method may not share a name in one C# type**, so the settings read is
`ResistBaseSeconds` and the pure scaling function stays `ResistSeconds(theirPull, myPull, baseSeconds)`.

- [ ] **Step 7: Let the resisting owner announce the snap**

In `Leash.cs`, `Snap()` currently sends only when `!Network.IsNetworked || Network.Server`. Breaking is now decided by the resisting end's owner, because strain comes from local movement input. Replace that condition:

```csharp
            if (listening != null && (!Network.IsNetworked || Network.Server))
```

with:

```csharp
            // Whoever DECIDED announces it. That used to be the server, because the verdict was
            // stretch and every machine could measure it; resist is accumulated from movement
            // input, which only the struggling player's own machine has. Peers reach Snap from
            // the announcement and must not re-broadcast, which the disposed flag above ensures.
            if (listening != null && (!Network.IsNetworked || Network.Owns(listening)
                                      || Network.Server))
```

`Network.Owns` takes a **`Component`** (`Network.cs:54`), and `listening` is a `Transform` — so it
is passed directly, with no `.gameObject`. `LeashEnd.ResolvedHere` (line 248) is the reference use.

- [ ] **Step 8: Run the tests to verify they pass**

Run the EditMode tests, then `cat Temp/headless_tests.txt`.
Expected: `FAILED=0`, ending in `DONE`.

- [ ] **Step 9: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash Assets/Game/Scripts/Characters/Player/Movement/Movement.cs Assets/Game/Editor/Tests/LeashConstraintTests.cs
git commit -m "feat: fight your way out of a leash by pulling away from it"
```

---

### Task 7: Terrain drops the rope

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs`
- Test: `Assets/Game/Editor/Tests/LeashConstraintTests.cs`

- [ ] **Step 1: Write the failing test**

Append inside `class LeashConstraintTests`:

```csharp
        [Test]
        public void TerrainIsNotSomethingARopeCanBeTiedTo()
        {
            var go = new GameObject("terrain probe");
            try
            {
                // A TerrainCollider is the exact thing chunk ground uses, so the type IS the test.
                // A layer mask would have to be kept in step with the streaming config, which
                // already has a documented casing drift defect.
                Assert.That(LeashArtifact.IsTieable(go.AddComponent<TerrainCollider>()), Is.False);

                var box = new GameObject("crate");
                try
                {
                    Assert.That(LeashArtifact.IsTieable(box.AddComponent<BoxCollider>()), Is.True);
                }
                finally { Object.DestroyImmediate(box); }

                // Nothing aimed at is not tieable either, and must not throw.
                Assert.That(LeashArtifact.IsTieable(null), Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run the EditMode tests, then `cat Temp/headless_tests.txt`.
Expected: compile failure, `'LeashArtifact' does not contain a definition for 'IsTieable'`.

- [ ] **Step 3: Implement the test and call it**

In `LeashArtifact.cs`, add beside the other statics:

```csharp
        /// <summary>
        /// Whether a rope may be tied to what the aim ray hit.
        ///
        /// <para>
        /// Terrain is the one refusal: a rope pinned to open ground is a fence post, and the item
        /// is for moving things. Rocks, walls and structures still anchor — only the ground itself
        /// is excluded, and <c>TerrainCollider</c> identifies it exactly, with nothing to keep in
        /// step and no layer to configure.
        /// </para>
        /// </summary>
        public static bool IsTieable(Collider hit) => hit != null && hit is not TerrainCollider;
```

Then call it in `OnRequestUse`. `arg.B` is already seeded with `Encode(Miss, 0f)` at line 160, and
every early `return` in the method leaves it that way — so a bare `return` **is** the drop, and no
new verb code and no wire change is needed. Add one line directly after the existing null-collider
guard at line 175:

```csharp
            if (!aimed || hit.collider == null) return;

            // Terrain is the one refusal. A bare return leaves arg.B on the Miss seeded at the top
            // of this method, which already means "drop what you are holding".
            if (!IsTieable(hit.collider)) return;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run the EditMode tests, then `cat Temp/headless_tests.txt`.
Expected: `FAILED=0`, ending in `DONE`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs Assets/Game/Editor/Tests/LeashConstraintTests.cs
git commit -m "feat: aiming a leash at terrain drops it instead of anchoring"
```

---

### Task 8: Multiple ropes sum on one body

Today each rope resolves independently, so two ropes on one body each cancel the **full** relative speed — removing it twice. Gathering per body fixes that and is what makes "three players out-pull one ship" true.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs`
- Test: `Assets/Game/Editor/Tests/LeashConstraintTests.cs`

- [ ] **Step 1: Write the failing test**

Append inside `class LeashConstraintTests`:

```csharp
        [Test]
        public void PullsFromSeveralRopesAddUp()
        {
            // Three players hauling one ship out-pull one player hauling it the other way.
            float player = Leash.PullOf(80f, 6f);

            Assert.That(Leash.CombinedPull(new[] { player, player, player }),
                        Is.EqualTo(3f * player).Within(1e-3f));

            // And an empty rope set pulls nothing rather than throwing.
            Assert.That(Leash.CombinedPull(new float[0]), Is.Zero);
        }

        [Test]
        public void OpposingRopesOnOneBodyCancel()
        {
            // Two players roping one crate in opposite directions deadlock it. The signs come from
            // the direction each rope pulls, so no rule is needed for "which side is the crate on".
            float player = Leash.PullOf(80f, 6f);

            Assert.That(Leash.CombinedPull(new[] { player, -player }), Is.Zero.Within(1e-3f));

            // A third player joining one side breaks the deadlock.
            Assert.That(Leash.CombinedPull(new[] { player, -player, player }),
                        Is.EqualTo(player).Within(1e-3f));
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run the EditMode tests, then `cat Temp/headless_tests.txt`.
Expected: compile failure, `'Leash' does not contain a definition for 'CombinedPull'`.

- [ ] **Step 3: Implement the sum**

In `Leash.cs`, after `TowCap`:

```csharp
        /// <summary>
        /// The net pull on one body from every rope tied to it, signed along each rope's own
        /// direction. Several crews hauling the same hull add; two hauling it apart cancel.
        ///
        /// <para>
        /// Summing per BODY rather than resolving each rope alone is also a correctness fix: two
        /// ropes on one body each cancelling the full relative speed removes it twice, which is
        /// the same double-counting <c>share</c> exists to prevent within a single rope.
        /// </para>
        /// </summary>
        public static float CombinedPull(IReadOnlyList<float> signedPulls)
        {
            if (signedPulls == null) return 0f;

            float total = 0f;
            for (int i = 0; i < signedPulls.Count; i++) total += signedPulls[i];

            return total;
        }
```

- [ ] **Step 4: Use it in `ResolveEnd`**

In `ResolveEnd`, the `netPull` added in Task 4 currently counts only this rope. Gather the other ropes on this body through `LeashAttachable` — the public API that has had no consumers until now — and project each onto this rope's direction so opposing ropes cancel:

```csharp
            float netPull = other.PullStrength - self.PullStrength;

            if (self.Attachable != null && self.Attachable.Leashes.Count > 1)
            {
                var contributions = new List<float>(self.Attachable.Leashes.Count);

                foreach (Leash rope in self.Attachable.Leashes)
                {
                    if (rope == null || rope == this) continue;

                    LeashEnd mine = rope.A.Anchor == self.Anchor ? rope.A : rope.B;
                    LeashEnd theirs = rope.Opposite(mine);
                    if (!theirs.IsAlive) continue;

                    Vector3 pullDirection = (theirs.Position - mine.Position).normalized;

                    contributions.Add((theirs.PullStrength - mine.PullStrength)
                                      * Vector3.Dot(pullDirection, toward));
                }

                netPull += CombinedPull(contributions);
            }
```

Allocating a `List` per end per step is not acceptable at 50 Hz — hoist it to a reusable `private static readonly List<float>` scratch buffer on `Leash` and `Clear()` it at the top of the block. **Do not share one buffer across nested calls**: `NetChannel` re-entrancy has bitten this codebase before, and `Snap` can re-enter inline on the host.

- [ ] **Step 5: Run the tests to verify they pass**

Run the EditMode tests, then `cat Temp/headless_tests.txt`.
Expected: `FAILED=0`, ending in `DONE`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs Assets/Game/Editor/Tests/LeashConstraintTests.cs
git commit -m "feat: several leashes on one body add their pull"
```

---

### Task 9: Tow vehicles and mounted players

Closes the two silent failures: `LeashedBody` bails on kinematic bodies, so a mounted player does nothing; and the foil and crawler classify as `Static` because neither has a dynamic body on its root.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashedBody.cs`

- [ ] **Step 1: Add an `ITowable` branch to the end**

In `LeashEnd.cs`, cache the interface in `TieTo` beside the existing `Agent` lookup:

```csharp
            Towable = root.GetComponentInParent<ITowable>();
```

with the property:

```csharp
        /// <summary>
        /// The machine this end is tied to, when it is one that would rather be ASKED than pushed.
        ///
        /// <para>
        /// A mounted rider's body is kinematic and parented into a seat, so writing velocity to it
        /// achieves nothing whatsoever — exactly what a hook fired from an ornithopter's cradle
        /// used to achieve. The vehicle owns what a pull costs it; the rope owns where the far end
        /// is tied. Anything that cannot usefully be hauled simply does not implement the
        /// interface, and a rope tied to it hangs slack, which is the honest answer.
        /// </para>
        /// </summary>
        public ITowable Towable { get; private set; }
```

Then take that branch first in `Pull`, before the kinematic branch:

```csharp
            if (Towable != null)
            {
                // Ask, do not push. RequestTow returning false means the tow is over -- arrived,
                // out of energy, or no longer under way -- and the rope should stop asking.
                if (!Towable.RequestTow(Position + step)) Towable = null;
                return;
            }
```

`ITowable.RequestTow` takes the anchor to be pulled toward, so pass the corrected position rather than the raw far end — that keeps the cap and the share from Task 4 applying to vehicles too.

- [ ] **Step 2: Route a mounted player through their vehicle**

In `LeashedBody.cs`, the early-out currently reads:

```csharp
            if (body == null || body.isKinematic || !Network.Owns(this)) return;
```

A mounted rider's body is kinematic, so this is why roping one does nothing. Replace it:

```csharp
            // A seated rider's body is kinematic and parented into the seat, so there is nothing
            // here to push. The rope is not inert in that case -- the end resolves against the
            // machine underneath instead, through its ITowable branch in LeashEnd.Pull.
            if (body == null || !Network.Owns(this)) return;
            if (body.isKinematic && GetComponentInParent<ITowable>() == null) return;
```

- [ ] **Step 3: Compile**

Run: `python3 tools/typecheck.py --editor`
Expected: no errors, exit 0. Add `using SpaceGame.Agents;` to both files if `ITowable` does not resolve — it lives in that namespace.

- [ ] **Step 4: Run the tests**

Run the EditMode tests, then `cat Temp/headless_tests.txt`.
Expected: `FAILED=0`. No test covers this path; the compile and the manual check below are the gate.

- [ ] **Step 5: Verify by hand in the Editor**

This cannot be covered by an EditMode test — there is no play-mode suite in this project. In the Editor:

1. Enter play mode, spawn a leash gauntlet, rope a dropped crate, and walk away. The crate follows and you are visibly slowed.
2. Rope a creature and let it flee. **You are dragged** — the behaviour `Restrain` used to forbid.
3. Hold a movement input squarely away from the rope. It parts after roughly `resistSeconds`.
4. Aim at open terrain. The held rope drops rather than pinning.
5. Aim at a rock face. It still anchors.

Then verify on an actual client, not just the host — a feature only ever seen working on the host is not finished. Build and run `open "<app>" --args -sgprofile client` against the Editor as host; without `-sgprofile` both sign in as the same anonymous PlayerId and the lobby 409s.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash
git commit -m "feat: a leash tows vehicles and mounted players through ITowable"
```

---

### Task 10: Documentation

Per `CLAUDE.md`, every change to behaviour updates its doc in the same commit, and a doc describing code that no longer exists is worse than no doc.

**Files:**
- Modify: `docs/AI/systems/LeashSystem.md`
- Modify: `docs/Human/05-what-you-carry.md`

- [ ] **Step 1: Rewrite the affected sections of the system doc**

In `docs/AI/systems/LeashSystem.md`:

- Replace the `### The constraint, as `ResolveEnd` applies it` code block with the four-line version from the spec (`pull`, `share`, `netPull`, `towCap`).
- In `## Gotchas`, **delete** the bullet beginning "**It must never be a grappling hook.**" — its `Restrain` claim is now false. Replace it with why the boundary still holds:

```markdown
- **A rope may now tow, but never launch.** `Restrain` is gone, so the constraint can add speed to
  the end losing the pull contest — that is the feature. What keeps it from being a second
  grappling hook is structural rather than a clamp: there is no winch anywhere in this system, so a
  player cannot pull *themselves* along a rope. Something else has to do the dragging.
  `LeashConstraintTests` still pins the absence of a `SetTethered` call at the source.
- **Nothing breaks under load.** A tow holds a permanent overstretch by design, so a stretch
  threshold that survives hauling a crate is one that never fires. The only exit is `ResistStrain`,
  accumulated from movement input pointing away from the knot — which is why the break verdict now
  belongs to the resisting end's **owner** rather than to the server.
- **`PullOf` returns zero for an infinite mass.** A static anchor resists everything and tows
  nothing, and the early return also keeps `Infinity * 0f` from producing a `NaN` that would poison
  every clamp downstream.
```

- Update the `| player |` and `| kinematic + NavMeshAgent |` rows of the `LeashEnd.Pull` table, and add a row for the new first branch:

```markdown
| `ITowable` | `RequestTow(corrected position)`. Asked, not pushed — a seated rider's body is kinematic and parented, so writing velocity to it does nothing. Returning false ends the tow |
```

- Add to `## Persistence`: resist strain is deliberately transient and not saved.
- Add these `symptoms:` entries to the frontmatter, phrased as what you would *see*:

```yaml
  - "a rope tied to a vehicle or a mounted player does nothing at all"
  - "the rope snaps every time I try to drag something heavy"
  - "two ropes on one object hold it far more firmly than one does"
```

- Bump `updated:` to `2026-09-05`.

- [ ] **Step 2: Update the player-facing doc**

In `docs/Human/05-what-you-carry.md`, the `## The leash` section, rewrite the two paragraphs that are now false — the one beginning "**It must never be a grappling hook.**" and the bullet "**A creature on a leash strains against it.**" — to describe the contest, the tow, and resist. Keep the register of the surrounding prose: plain language, no type names.

- [ ] **Step 3: Regenerate and validate the doc index**

```bash
python3 tools/docs_check.py --index
```
Expected: exit 0. `INDEX.md` and `ROUTING.md` are generated from frontmatter — never hand-edit them.

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "docs: the leash is a force contest"
```

---

## Out of scope, and why

- **Aiming while mounted is broken** (`docs/AI/DEFECTS.md`): items hook straight down rather than where they are pointed while riding. "A ship tows something" is unreachable from a cockpit until that is fixed. It is a defect in a shared aiming path, not leash work, and fixing it here would hide it from every other item that has it.
- **No creature is told it is leashed.** Resist gives a creature a way *out* of a rope, not an opinion about being on one. Unchanged from today.
- **No tow speed cap and no fall-damage exemption**, by explicit decision — being dragged into a dune at vehicle speed is meant to hurt. If playtests hate it, the cheapest lever is a cap on players only, in `TowCap`.
