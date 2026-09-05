# Agent Ground Conform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop every `NavMeshAgent`-driven agent levitating above the terrain, and tilt its body to the slope it is standing on, so it reads as walking on the ground rather than skating over it.

**Architecture:** The baked world NavMesh sits a *measured* median of 0.257 m above the terrain (min −0.262, p95 +0.480, max +0.600, n=1384). `NavMeshAgentMotor` puts the transform exactly on that mesh and nothing ever conforms it. A new `AgentGroundConform` component probes the real ground each `LateUpdate` with the existing `WalkerGround` sampler and produces two outputs from that one probe: a vertical correction fed into `NavMeshAgent.baseOffset` (on the authoritative machine only — it reaches everyone else through the replicated transform), and a slope tilt written to the agent's visual root (on every machine — it is pure presentation). All the arithmetic lives in a pure, physics-free `AgentGrounding` solver in `SpaceGame.Locomotion` so the EditMode suite can test it; the MonoBehaviour is thin glue.

**Tech Stack:** Unity 6000.3.11f1, C#, `UnityEngine.AI.NavMeshAgent`, existing `SpaceGame.Locomotion` helpers (`WalkerGround`, `WalkerSurface`, `WalkerSupportPlane`), NUnit EditMode tests, Unity Netcode for GameObjects.

---

## Before you start

- **Branch.** Current branch is `body-gear-system`. Branch off it before the first commit: `git checkout -b agent-ground-conform`.
- **Commit hook.** This repo has a hook that blocks `git commit`. If a commit step fails with "Committing is blocked by user policy", tell the user and let them allow it — do not work around it.
- **Do not read the whole doc corpus.** The governing docs for this change are [`docs/AI/systems/AgentSystem.md`](../../AI/systems/AgentSystem.md), [`docs/AI/systems/NavMeshSystem.md`](../../AI/systems/NavMeshSystem.md) and [`docs/AI/systems/Locomotion.md`](../../AI/systems/Locomotion.md). Read those three, in full, before Task 1.
- **Verification commands** (from [`docs/AI/systems/Testing.md`](../../AI/systems/Testing.md) — there is **no** `unity -batchmode -runTests` path in this repo, do not invent one):
  - Type-check: `python3 tools/typecheck.py --editor` → expect `No errors.`
  - Tests: Unity menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then `cat Temp/headless_tests.txt` → expect `FAILED=0` and a final `DONE` line. Absence of the file means still running; poll for it.
  - **Queue a headless run only from a clean scene.** A dirty scene makes the runner open a modal save dialog that blocks the entire editor with nothing logged.

## Measurements this plan is built on

Taken in a live play session on 2026-09-05 with `Unity_RunCommand`. Do not re-derive them; do sanity-check them at Task 9.

| Fact | Value |
| --- | --- |
| `navMeshY − terrainY`, 1384 samples over 6 terrains | mean +0.264, min −0.262, p25 +0.199, median +0.257, p75 +0.321, p95 +0.480, max +0.600 |
| `transform.position.y − navMeshY` for every live agent | exactly `NavMeshAgent.baseOffset` — nothing conforms |
| Sole-to-pivot in the prefabs' rest pose | −0.107 … +0.014 across all 10 prefabs (Nomad/BountyHunter exactly 0.000). **Every prefab is authored soles-at-pivot**, so `soleOffset` defaults to 0 |
| Prefabs with a `NavMeshAgent` | 10: `Caravan/BountyHunter`, `Characters/Nomad`, `creatures/DuneRat`, `creatures/Golem`, `creatures/Vrescal`, `Robots/DeathmatchBot`, `Robots/PatrolRobot`, `Robots/PatrolRobot 1`, `Robots/PatrolRobot 2`, `Robots/PatrolRobot 3`. All 10 have `NavMeshAgentMotor` and `AgentAnimatorDriver`; all have `applyRootMotion = false` |
| Clips that rotate the agent's top visual node | **Golem** (`Bone_Root`) and **DuneRat** (`Arm_DuneRat`) only. The other eight have nothing driving that node |
| Legged walkers (Ostrich, DesertCrawler, DuneFoil) | already conform via `LeggedLocomotion`; **out of scope** |

## File structure

| File | Responsibility |
| --- | --- |
| Create `Assets/Game/Scripts/Locomotion/Ground/AgentGrounding.cs` | Pure solver: height offset, slope tilt, smoothing, and the animated-vs-static baseline rule. No physics, no `Transform`, fully unit-testable. Assembly `SpaceGame.Locomotion`. |
| Create `Assets/Game/Tests/EditMode/AgentGroundingTests.cs` | Unit tests for the solver. Assembly `SpaceGame.Tests.EditMode` (already references `SpaceGame.Locomotion`). |
| Modify `Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs` | Compose `baseOffset` from three terms instead of two writers fighting; expose `GroundOffset`, `NavSurfaceY`, `IsLeaping`. |
| Create `Assets/Game/Scripts/agents/Animation/AgentGroundConform.cs` | The MonoBehaviour glue: resolve refs, probe with `WalkerGround`, call the solver, write the two outputs. Assembly-CSharp, namespace `SpaceGame.Agents`. |
| Create `Assets/Game/Editor/Agents/AgentGroundConformWiring.cs` | Idempotent `Ensure(root)` + a `Tools ▸ SpaceGame ▸ Agents ▸ Wire Ground Conform` menu item that adds the component to all 10 prefabs. |
| Modify `Assets/Game/Editor/Creatures/{GolemBuilder,DuneRatBuilder,VrescalBuilder}.cs`, `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` | Call `Ensure` so a rebuild does not silently drop the component. |
| Create `Assets/Game/Editor/Tests/AgentGroundConformTests.cs` | Integration test on real colliders with a real prefab, mirroring `SpiderWalkerGroundingTests`. Assembly-CSharp-Editor. |
| Modify `Assets/Game/Scenes/world/Chunks/Chunk_7_5.unity`, `Assets/Game/Scenes/Tests/FerdinandWorld/Chunks/FerdinandChunk_3_1.unity` | Delete four stray `m_BaseOffset: 0.1` overrides. |
| Modify `docs/AI/systems/{AgentSystem,NavMeshSystem,Locomotion}.md` | Document the behaviour, the gotchas and the symptoms. |

---

### Task 1: The pure grounding solver

**Files:**
- Create: `Assets/Game/Scripts/Locomotion/Ground/AgentGrounding.cs`
- Test: `Assets/Game/Tests/EditMode/AgentGroundingTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Game/Tests/EditMode/AgentGroundingTests.cs`:

```csharp
// The arithmetic that puts an agent's body on the ground, with no scene, no colliders and no
// physics -- the same separation SpiderWalkerGroundingTests relies on at the other end, where the
// whole assembled machine is checked against real geometry.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Locomotion;

namespace SpaceGame.Tests
{
    public class AgentGroundingTests
    {
        private static AgentGroundingSettings Settings() => new AgentGroundingSettings
        {
            SoleOffset = 0f,
            MaxCorrection = 1f,
            HeightFollowSpeed = 12f,
            SlopeFollow = 1f,
            MaxTiltDegrees = 30f,
            TiltFollowSpeed = 8f,
        };

        /// The measured case: the NavMesh put the body 0.26 m above the sand.
        [Test]
        public void TheBodyIsPutOnTheGroundTheProbeFound()
        {
            var g = new AgentGrounding(Quaternion.identity);

            g.Step(grounded: true, navSurfaceY: 100.26f, groundY: 100f, localGroundNormal: Vector3.up,
                   currentBodyRotation: Quaternion.identity, Settings(), 1f / 60f);

            Assert.AreEqual(-0.26f, g.HeightOffset, 0.0001f);
        }

        /// <summary>
        /// The first frame snaps. Easing in from zero would drop every agent a quarter of a metre
        /// in front of the player on the frame it spawns, and do it again after every stream-in,
        /// respawn and save restore.
        /// </summary>
        [Test]
        public void TheFirstFrameSnapsRatherThanEasingIn()
        {
            var g = new AgentGrounding(Quaternion.identity);

            g.Step(true, 100.26f, 100f, Vector3.up, Quaternion.identity, Settings(), 0.0001f);

            Assert.AreEqual(-0.26f, g.HeightOffset, 0.0001f);
        }

        /// <summary>
        /// A probe that finds something absurd -- the roof of a cave, a collider streaming in
        /// underneath -- must not be able to teleport a body. It is clamped, not trusted.
        /// </summary>
        [Test]
        public void TheCorrectionIsCappedBothWays()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            s.MaxCorrection = 0.5f;

            g.Step(true, 100f, 90f, Vector3.up, Quaternion.identity, s, 1f);
            Assert.AreEqual(-0.5f, g.HeightOffset, 0.0001f, "a floor 10 m down must not drop the body 10 m");

            var up = new AgentGrounding(Quaternion.identity);
            up.Step(true, 100f, 110f, Vector3.up, Quaternion.identity, s, 1f);
            Assert.AreEqual(0.5f, up.HeightOffset, 0.0001f, "a ceiling read as floor must not launch the body");
        }

        /// <summary>
        /// Off the ground -- mid-leap, over a ledge, a hole in the collision -- holding the last
        /// correction would hang the body at the height of ground it is no longer above. The
        /// honest answer with no probe is "wherever navigation put me".
        /// </summary>
        [Test]
        public void WithNoGroundTheCorrectionDecaysBackToZero()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            g.Step(true, 100.26f, 100f, Vector3.up, Quaternion.identity, s, 1f);
            Assert.AreEqual(-0.26f, g.HeightOffset, 0.0001f);

            for (int i = 0; i < 120; i++)
                g.Step(false, 100.26f, 0f, Vector3.up, g.BodyRotation, s, 1f / 60f);

            Assert.AreEqual(0f, g.HeightOffset, 0.001f);
        }

        [Test]
        public void TheTiltFollowsTheSurfaceNormal()
        {
            var g = new AgentGrounding(Quaternion.identity);
            Vector3 normal = Quaternion.Euler(0f, 0f, 20f) * Vector3.up;

            g.Step(true, 100f, 100f, normal, Quaternion.identity, Settings(), 1f);

            float angle = Quaternion.Angle(Quaternion.identity, g.BodyRotation);
            Assert.AreEqual(20f, angle, 0.5f);
        }

        [Test]
        public void TheTiltIsCappedHoweverSteepTheGroundIs()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            s.MaxTiltDegrees = 15f;
            Vector3 normal = Quaternion.Euler(0f, 0f, 50f) * Vector3.up;

            g.Step(true, 100f, 100f, normal, Quaternion.identity, s, 1f);

            Assert.AreEqual(15f, Quaternion.Angle(Quaternion.identity, g.BodyRotation), 0.5f);
        }

        /// <summary>
        /// The compounding trap. On the Nomad, PatrolRobot and Vrescal nothing animates the node
        /// the tilt is written to, so the value read back next frame is the tilt itself. Multiply
        /// the tilt in again and the body spins.
        /// </summary>
        [Test]
        public void ANodeNothingElseDrivesDoesNotAccumulateTilt()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            s.TiltFollowSpeed = 1000f;
            Vector3 normal = Quaternion.Euler(0f, 0f, 20f) * Vector3.up;

            // Exactly what the component does: write the result to the transform, and read that
            // same transform back on the next frame because nothing else touched it.
            Quaternion onTheTransform = Quaternion.identity;
            for (int i = 0; i < 100; i++)
            {
                g.Step(true, 100f, 100f, normal, onTheTransform, s, 1f / 60f);
                onTheTransform = g.BodyRotation;
            }

            Assert.AreEqual(20f, Quaternion.Angle(Quaternion.identity, onTheTransform), 0.5f);
        }

        /// <summary>
        /// The opposite case, and it wants the opposite treatment. The Golem's clips carry a
        /// rotation curve for Bone_Root and DuneRat's for Arm_DuneRat, so the Animator rewrites
        /// the node before every LateUpdate. Tilting from the rest pose there would erase the
        /// animation; the tilt has to ride on top of it.
        /// </summary>
        [Test]
        public void AnAnimatedNodeKeepsItsAnimationUnderTheTilt()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            s.TiltFollowSpeed = 1000f;
            Vector3 normal = Quaternion.Euler(0f, 0f, 20f) * Vector3.up;

            Quaternion animated = Quaternion.identity;
            for (int i = 0; i < 100; i++)
            {
                // A clip swinging the root bone 10 degrees back and forth about Y.
                animated = Quaternion.Euler(0f, Mathf.Sin(i * 0.3f) * 10f, 0f);
                g.Step(true, 100f, 100f, normal, animated, s, 1f / 60f);
            }

            Assert.AreEqual(0f, Quaternion.Angle(g.BodyRotation, g.LastTilt * animated), 0.5f,
                            "the tilt must be composed onto the animated pose, not replace it");
        }

        /// <summary>
        /// Reset is what OnEnable calls. A creature comes back from a respawn, a chunk stream and
        /// a save restore, and each time it must snap to the ground it is standing on now rather
        /// than easing over from wherever it last stood.
        /// </summary>
        [Test]
        public void ResetMakesTheNextFrameSnapAgain()
        {
            var g = new AgentGrounding(Quaternion.identity);
            var s = Settings();
            g.Step(true, 100.26f, 100f, Vector3.up, Quaternion.identity, s, 1f);

            g.Reset();
            Assert.AreEqual(0f, g.HeightOffset, 0.0001f);

            g.Step(true, 50.4f, 50f, Vector3.up, Quaternion.identity, s, 0.0001f);
            Assert.AreEqual(-0.4f, g.HeightOffset, 0.0001f);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Unity menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then:

```bash
cat Temp/headless_tests.txt
```

Expected: the run does not even reach the tests — `python3 tools/typecheck.py` reports `AgentGrounding` / `AgentGroundingSettings` are undefined. That is the failure.

- [ ] **Step 3: Write the solver**

Create `Assets/Game/Scripts/Locomotion/Ground/AgentGrounding.cs`:

```csharp
// Putting a NavMesh agent's body on the ground rather than on the NavMesh.
//
// The two are not the same surface and never were. The world mesh is baked at voxelSize 0.3333 and
// Recast puts each polygon at the TOP of the voxel column it came from, so the mesh floats: a
// median of 0.257 m above the terrain across 1384 samples, from 0.262 m below to 0.600 m above.
// NavMeshAgentMotor sets the transform to exactly what navigation says, so every agent inherited
// that error verbatim -- which is why they were all hovering, and why they hovered by DIFFERENT
// amounts depending on where they stood.
//
// A constant baseOffset cannot fix that. Subtracting the median leaves the middle half of the
// world within 6 cm, but still floats 22 cm at p95 and buries the body to the shins at the worst
// sample. The error is terrain-dependent, so the correction has to be measured per frame.
//
// This class is the arithmetic only. It never touches a Transform or a collider, so the whole of
// it is testable without a scene; the raycasting lives in WalkerGround and the wiring in
// AgentGroundConform.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// Per-agent tuning for <see cref="AgentGrounding"/>. A struct so a MonoBehaviour can serialize
    /// the fields and hand them over each frame without allocating.
    public struct AgentGroundingSettings
    {
        /// Distance from the body's pivot to its soles. Every agent prefab in this project is
        /// authored with the soles at the pivot (measured: -0.107 m to +0.014 m), so this is 0
        /// unless a particular model says otherwise.
        public float SoleOffset;

        /// Cap on the correction in metres, either way. The largest error measured in the world is
        /// 0.60 m; anything past this cap is a probe that found something it should not have -- a
        /// cave roof, a collider streaming in -- and clamping is what stops it teleporting a body.
        public float MaxCorrection;

        /// First-order follow rate for the height, in 1/seconds.
        public float HeightFollowSpeed;

        /// How much of the ground's tilt the body takes on. 0 stands bolt upright and reads as a
        /// cardboard cut-out on a hillside; 1 lies the body flat on the slope, which is right for a
        /// quadruped and wrong for anything that walks on two legs.
        public float SlopeFollow;

        /// Cap on the tilt in degrees. The world bakes walkable ground up to 60 degrees, and a
        /// biped leaned over that far has fallen over.
        public float MaxTiltDegrees;

        /// First-order follow rate for the tilt, in 1/seconds.
        public float TiltFollowSpeed;
    }

    /// <summary>
    /// Per-frame solve for where an agent's body should sit and how it should lean. Stateful across
    /// frames (both outputs are smoothed), so one instance per agent.
    /// </summary>
    public sealed class AgentGrounding
    {
        private readonly Quaternion restBodyRotation;

        private float heightOffset;
        private Quaternion tilt = Quaternion.identity;
        private bool primed;

        private Quaternion lastWritten;
        private bool hasWritten;

        public AgentGrounding(Quaternion restBodyRotation)
        {
            this.restBodyRotation = restBodyRotation;
            BodyRotation = restBodyRotation;
        }

        /// Metres to add to wherever navigation put the body. Feeds NavMeshAgent.baseOffset.
        public float HeightOffset => heightOffset;

        /// Local rotation to write on the body's visual root.
        public Quaternion BodyRotation { get; private set; }

        /// The smoothed slope tilt alone, without the pose it was composed onto. Exposed for tests
        /// and gizmos; gameplay wants <see cref="BodyRotation"/>.
        public Quaternion LastTilt => tilt;

        /// The visual root's rotation as the prefab authored it, for a caller putting the body back
        /// the way it found it.
        public Quaternion RestBodyRotation => restBodyRotation;

        /// <summary>
        /// Forget the smoothed state so the next <see cref="Step"/> snaps. Called from OnEnable: a
        /// creature is re-enabled by respawn, chunk streaming and save restores, and each of those
        /// puts it somewhere new. Easing across from the old correction would show it sliding into
        /// place.
        /// </summary>
        public void Reset()
        {
            heightOffset = 0f;
            tilt = Quaternion.identity;
            primed = false;
            hasWritten = false;
            BodyRotation = restBodyRotation;
        }

        /// <param name="grounded">Whether the probe found anything at all. False decays both
        /// outputs to neutral rather than holding a correction for ground that is not there.</param>
        /// <param name="navSurfaceY">World Y of the NavMesh polygon under the body, with every
        /// offset already stripped off it.</param>
        /// <param name="groundY">World Y of the real surface the probe found. Ignored when
        /// <paramref name="grounded"/> is false.</param>
        /// <param name="localGroundNormal">The surface normal, in the AGENT's local space, so the
        /// tilt turns with the body instead of being pinned to world axes.</param>
        /// <param name="currentBodyRotation">Whatever is on the visual root's localRotation right
        /// now, read before this write. See <see cref="Baseline"/>.</param>
        public void Step(bool grounded, float navSurfaceY, float groundY, Vector3 localGroundNormal,
                         Quaternion currentBodyRotation, in AgentGroundingSettings settings, float dt)
        {
            float targetOffset;
            Quaternion targetTilt;

            if (grounded)
            {
                float cap = Mathf.Max(0f, settings.MaxCorrection);
                targetOffset = Mathf.Clamp(groundY + settings.SoleOffset - navSurfaceY, -cap, cap);

                // Reuses the walkers' tilt solve rather than repeating it: same clamp, same
                // fraction-of-the-slope tunable, already tested.
                var plane = new WalkerSupportPlane
                {
                    Normal = localGroundNormal.sqrMagnitude > 1e-8f
                        ? localGroundNormal.normalized
                        : Vector3.up,
                    Height = 0f,
                    Valid = true,
                };
                targetTilt = plane.Tilt(settings.SlopeFollow, settings.MaxTiltDegrees);
            }
            else
            {
                targetOffset = 0f;
                targetTilt = Quaternion.identity;
            }

            if (primed)
            {
                heightOffset = Mathf.Lerp(heightOffset, targetOffset,
                                          Rate(settings.HeightFollowSpeed, dt));
                tilt = Quaternion.Slerp(tilt, targetTilt, Rate(settings.TiltFollowSpeed, dt));
            }
            else
            {
                heightOffset = targetOffset;
                tilt = targetTilt;
                primed = true;
            }

            BodyRotation = tilt * Baseline(currentBodyRotation);
            lastWritten = BodyRotation;
            hasWritten = true;
        }

        /// The project's standard frame-rate-independent first-order follow.
        private static float Rate(float speed, float dt)
            => dt > 0f ? 1f - Mathf.Exp(-Mathf.Max(0f, speed) * dt) : 1f;

        /// <summary>
        /// What to tilt FROM, and the one genuinely subtle thing in this class.
        ///
        /// <para>
        /// The node the tilt lands on is animated on some rigs and not on others. The Golem's clips
        /// carry a rotation curve for <c>Bone_Root</c> and the DuneRat's for <c>Arm_DuneRat</c>; the
        /// Nomad's <c>Model</c>, the PatrolRobots' <c>Armature</c> and the Vrescal's <c>vrescal</c>
        /// have nothing driving them at all. The two cases want opposite treatment, and getting it
        /// wrong fails loudly in both directions: tilt from the rest pose on an animated node and
        /// the tilt erases the animation; tilt from the read-back value on a node nothing drives and
        /// last frame's tilt is multiplied in again, every frame, until the body is spinning.
        /// </para>
        /// <para>
        /// A serialized flag per prefab would answer it, and would be silently wrong the first time
        /// someone re-exports a rig with a root curve it did not have before. Read it off the
        /// transform instead. If the rotation still holds exactly what was written last frame,
        /// nothing else touched it and the baseline is the rest pose. If it changed, the Animator
        /// wrote it, and that is the baseline. A clip that momentarily lands on exactly the value
        /// written costs one frame of rest-pose baseline and corrects itself on the next.
        /// </para>
        /// </summary>
        private Quaternion Baseline(Quaternion current)
        {
            if (!hasWritten) return current;

            // abs() because q and -q are the same rotation. 0.99999 is about half a degree.
            return Mathf.Abs(Quaternion.Dot(current, lastWritten)) > 0.99999f
                ? restBodyRotation
                : current;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
python3 tools/typecheck.py --editor
```
Expected: `No errors.`

Then Unity menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)`:
```bash
cat Temp/headless_tests.txt
```
Expected: `FAILED=0`, and `DONE` on the last line.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Locomotion/Ground/AgentGrounding.cs Assets/Game/Scripts/Locomotion/Ground/AgentGrounding.cs.meta Assets/Game/Tests/EditMode/AgentGroundingTests.cs Assets/Game/Tests/EditMode/AgentGroundingTests.cs.meta
git commit -m "feat: AgentGrounding solver for agent ground conforming"
```

---

### Task 2: Let NavMeshAgentMotor compose its baseOffset

`baseOffset` currently has one writer (the mounted-jump arc). The conform needs a second term, and two components writing the same field means the later one erases the other's contribution. Make the motor the single writer and give it three terms to sum.

**Files:**
- Modify: `Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs`

- [ ] **Step 1: Add the composed offset and the two accessors the conform needs**

In `NavMeshAgentMotor`, find the mounted-jump fields (they sit just after the `[Header("Mounted Jump")]` block's serialized fields, alongside `jumpElapsed` and `jumpCooldownTimer`) and add these members next to them:

```csharp
        // baseOffset has more than one thing to say now, so nobody writes it directly any more.
        //
        // The jump arc was its only author until ground conforming arrived, and two components
        // assigning the same field do not add up -- whichever ran later in the frame silently
        // erased the other. So each contribution is kept separately and summed in one place.
        //
        // groundOffset is the NavMesh-to-real-ground correction, pushed in by AgentGroundConform.
        // jumpArc is the mounted jump. defaultBaseOffset is whatever the prefab was authored with.
        private float groundOffset;
        private float jumpArc;

        /// <summary>
        /// Vertical correction between the NavMesh polygon this agent stands on and the ground
        /// underneath it. Written by <c>AgentGroundConform</c>; 0 when nothing is conforming.
        /// </summary>
        public float GroundOffset
        {
            get => groundOffset;
            set
            {
                groundOffset = value;
                ApplyBaseOffset();
            }
        }

        /// <summary>
        /// World Y of the NavMesh polygon under this agent, with every offset stripped back off.
        ///
        /// <para>
        /// This is the number a ground conform has to correct against, and it cannot be recovered
        /// from <c>transform.position</c> alone: that already carries the prefab's own base offset
        /// and, mid-jump, the arc as well, so subtracting only the ground term would leave the
        /// conform fighting the jump. The fallback covers an agent that has been parked off the
        /// mesh, where the transform is the only position there is.
        /// </para>
        /// </summary>
        public float NavSurfaceY => agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh
            ? agent.nextPosition.y - agent.baseOffset
            : transform.position.y - groundOffset;

        /// <summary>Whether a mounted leap is in flight, driving the transform by hand.</summary>
        public bool IsLeaping => isLeaping;

        private void ApplyBaseOffset()
        {
            if (agent) agent.baseOffset = defaultBaseOffset + groundOffset + jumpArc;
        }
```

- [ ] **Step 2: Route the jump through the same sum**

Replace the body of `UpdateMountedJump` (currently the method that writes `agent.baseOffset = defaultBaseOffset + arc * Mathf.Max(0.01f, mountedJumpHeight);`) with:

```csharp
        private void UpdateMountedJump(float deltaTime)
        {
            if (!agent)
            {
                return;
            }

            jumpCooldownTimer = Mathf.Max(0f, jumpCooldownTimer - deltaTime);
            if (jumpElapsed < 0f)
            {
                return;
            }

            jumpElapsed += deltaTime;
            float t = Mathf.Clamp01(jumpElapsed / Mathf.Max(0.01f, mountedJumpDuration));
            jumpArc = Mathf.Sin(t * Mathf.PI) * Mathf.Max(0.01f, mountedJumpHeight);

            if (t >= 1f)
            {
                jumpElapsed = -1f;
                jumpArc = 0f;
            }

            ApplyBaseOffset();
        }
```

- [ ] **Step 3: Clear both terms when the motor goes away**

In `OnDisable`, replace the `agent.baseOffset = defaultBaseOffset;` line so both contributions are dropped rather than left stale on a disabled motor:

```csharp
            if (agent)
            {
                groundOffset = 0f;
                jumpArc = 0f;
                agent.baseOffset = defaultBaseOffset;
                agent.updateRotation = defaultUpdateRotation;
            }
```

- [ ] **Step 4: Type-check**

```bash
python3 tools/typecheck.py --editor
```
Expected: `No errors.`

- [ ] **Step 5: Run the full EditMode suite to prove nothing regressed**

Unity menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)`:
```bash
cat Temp/headless_tests.txt
```
Expected: `FAILED=0`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs
git commit -m "refactor: NavMeshAgentMotor composes baseOffset from ground and jump terms"
```

---

### Task 3: The AgentGroundConform component

**Files:**
- Create: `Assets/Game/Scripts/agents/Animation/AgentGroundConform.cs`

- [ ] **Step 1: Write the component**

Create `Assets/Game/Scripts/agents/Animation/AgentGroundConform.cs`:

```csharp
// Sitting an agent on the ground instead of on the NavMesh, and leaning it into the slope.
//
// One probe, two answers, and they are delivered to different places for a reason.
//
// HEIGHT goes to NavMeshAgent.baseOffset, which is the one vertical knob the agent will not fight:
// writing transform.position while updatePosition is on drags the agent's own internal position
// with it. It is applied only where the motor is running -- NetAuthority switches the motor and
// the NavMeshAgent off on every remote copy -- and reaches the other machines the way every other
// bit of the agent's position does, through the replicated transform. Correcting it locally as
// well would apply it twice.
//
// TILT goes to the body's visual root, on EVERY machine, because it is presentation: no rotation
// of a child transform is replicated, and recomputing it locally costs one probe and keeps a
// watching client's creature leaning the same way the host's does.
//
// Why tilt matters at all: height alone gets the soles touching, and on the dunes a rigidly
// vertical body with both feet at one height still reads as pasted on. Leaning into the slope is
// what makes it look like standing on it.
using SpaceGame.Locomotion;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents
{
    /// <summary>
    /// Conforms a NavMesh-driven agent to the real ground under it. Add alongside
    /// <see cref="NavMeshAgentMotor"/>; wired onto every agent prefab by
    /// <c>Tools ▸ SpaceGame ▸ Agents ▸ Wire Ground Conform</c>.
    /// </summary>
    // After the agent's other LateUpdates, so AgentAnimatorDriver has already had its say about the
    // pose this frame. The Animator itself always runs before any LateUpdate, so the animated
    // rotation this reads is the finished one.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(NavMeshAgent))]
    public class AgentGroundConform : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Left empty, resolved in Initialise from the NavMeshAgent on this object.")]
        [SerializeField] private NavMeshAgent agent;

        [Tooltip("Left empty, resolved in Initialise. Without a motor the height correction is " +
                 "skipped and only the slope tilt runs.")]
        [SerializeField] private NavMeshAgentMotor motor;

        [Tooltip("The child transform the slope tilt is written to. Left empty, resolved to the " +
                 "direct child the skin hangs from. Never the agent root: its yaw belongs to " +
                 "navigation, and tilting it would tilt the collider and the NavMeshAgent with it.")]
        [SerializeField] private Transform bodyRoot;

        [Header("Ground probe")]
        [Tooltip("Layers counted as ground. Anything under its own physics is rejected whatever " +
                 "its layer, so a player standing next to the agent is never read as floor.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far above the body each ray starts, so ground higher than the pivot is still " +
                 "found. Sized from the worst measured NavMesh error (0.60 m) with a little over. " +
                 "Keep it small: a ray starts above the body and looks down, so an overhang inside " +
                 "this band is read as the ground the body should be standing on.")]
        [SerializeField, Min(0f)] private float probeStartHeight = 0.6f;

        [Tooltip("How far down each ray looks from its start height.")]
        [SerializeField, Min(0.5f)] private float probeDistance = 8f;

        [Tooltip("Radius of the probe ring. 0 uses the NavMeshAgent's own radius, which is what " +
                 "the body's footprint is defined as everywhere else.")]
        [SerializeField, Min(0f)] private float footprintRadius = 0f;

        [Header("Height")]
        [Tooltip("Distance from this prefab's pivot to its soles. Every agent prefab in this " +
                 "project is authored soles-at-pivot, so this is 0 unless the model says otherwise.")]
        [SerializeField] private float soleOffset = 0f;

        [Tooltip("Cap on the correction, metres, either way. The largest NavMesh error measured in " +
                 "the world is 0.60 m; the cap is what stops a bad probe teleporting the body.")]
        [SerializeField, Min(0f)] private float maxCorrection = 1f;

        [Tooltip("How fast the height follows the ground, 1/seconds. High enough to keep up with a " +
                 "running creature, low enough that a ray catching a rock edge does not step it.")]
        [SerializeField, Min(0f)] private float heightFollowSpeed = 12f;

        [Header("Slope")]
        [SerializeField] private bool alignToSlope = true;

        [Tooltip("How much of the ground's tilt the body takes on. 1 lies flat on the hillside, " +
                 "which suits a quadruped; a biped stays closer to upright.")]
        [SerializeField, Range(0f, 1f)] private float slopeFollow = 0.8f;

        [Tooltip("Cap on the lean in degrees. The world bakes walkable ground up to 60 degrees and " +
                 "a body leaned that far has fallen over.")]
        [SerializeField, Min(0f)] private float maxTiltDegrees = 30f;

        [SerializeField, Min(0f)] private float tiltFollowSpeed = 8f;

        private WalkerGround ground;
        private AgentGrounding grounding;
        private bool initialised;

        private float FootprintRadius => footprintRadius > 0f
            ? footprintRadius
            : agent != null ? Mathf.Max(0.1f, agent.radius) : 0.5f;

        private AgentGroundingSettings Settings => new AgentGroundingSettings
        {
            SoleOffset = soleOffset,
            MaxCorrection = maxCorrection,
            HeightFollowSpeed = heightFollowSpeed,
            SlopeFollow = alignToSlope ? slopeFollow : 0f,
            MaxTiltDegrees = maxTiltDegrees,
            TiltFollowSpeed = tiltFollowSpeed,
        };

        /// <summary>
        /// Resolve references and build the sampler. Public and separate from Awake so an EditMode
        /// test can assemble the whole thing without a running player loop, the way
        /// <c>DesertCrawlerLocomotion.Initialise</c> does.
        /// </summary>
        public void Initialise()
        {
            if (initialised) return;
            initialised = true;

            if (!agent) agent = GetComponent<NavMeshAgent>();
            if (!motor) motor = GetComponent<NavMeshAgentMotor>();
            if (!bodyRoot) bodyRoot = ResolveBodyRoot();

            if (!bodyRoot && alignToSlope)
            {
                Debug.LogWarning(
                    $"{name}: no visual root found to lean, so this agent will be put on the " +
                    "ground but will not follow the slope. Assign bodyRoot on the prefab.", this);
            }

            ground = new WalkerGround(transform, groundMask, probeStartHeight, probeDistance);
            grounding = new AgentGrounding(bodyRoot ? bodyRoot.localRotation : Quaternion.identity);
        }

        private void Awake() => Initialise();

        private void OnEnable()
        {
            Initialise();

            // Respawn, chunk stream-in, save restore: each puts the body somewhere new, and easing
            // across from the last correction would show it sliding into place.
            grounding.Reset();
        }

        private void OnDisable()
        {
            if (motor) motor.GroundOffset = 0f;

            // Back to the authored pose, not to the last lean we wrote. On the rigs where nothing
            // animates this node -- the Nomad, the PatrolRobots, the Vrescal -- leaving the lean
            // behind would freeze a dead or streamed-out body at whatever angle the last hillside
            // it stood on happened to be.
            if (bodyRoot && grounding != null) bodyRoot.localRotation = grounding.RestBodyRotation;
        }

        private void LateUpdate() => Conform(Time.deltaTime);

        /// <summary>
        /// One frame of conforming. Public and dt-driven so it can be stepped from a test.
        /// </summary>
        public void Conform(float deltaTime)
        {
            Initialise();

            bool grounded = ground.TrySurface(transform.position, FootprintRadius,
                                              float.NegativeInfinity, out WalkerSurface surface);

            // A leap runs with updatePosition off and the body driven along an arc by hand. The
            // ground under a body mid-arc is not the ground it is standing on, and conforming to it
            // would flatten the leap.
            if (motor != null && motor.IsLeaping) grounded = false;

            // NetAuthority disables the motor and the NavMeshAgent on every remote copy. There the
            // height already arrived inside the replicated transform, and correcting it again here
            // would apply it twice.
            bool drivesHeight = motor != null && motor.isActiveAndEnabled;

            Vector3 localNormal = grounded
                ? transform.InverseTransformDirection(surface.Normal)
                : Vector3.up;

            grounding.Step(grounded,
                           drivesHeight ? motor.NavSurfaceY : transform.position.y,
                           grounded ? surface.Point.y : 0f,
                           localNormal,
                           bodyRoot ? bodyRoot.localRotation : Quaternion.identity,
                           Settings,
                           deltaTime);

            if (drivesHeight) motor.GroundOffset = grounding.HeightOffset;
            if (bodyRoot && alignToSlope) bodyRoot.localRotation = grounding.BodyRotation;
        }

        /// <summary>
        /// The direct child of this agent that the skin actually hangs from.
        ///
        /// <para>
        /// Not the agent root, whose yaw navigation owns and whose collider must stay upright, and
        /// not an arbitrary renderer: a SkinnedMeshRenderer deforms to its BONES, so tilting the
        /// object holding the renderer moves nothing. Walking up from the root bone to the child of
        /// this agent lands on <c>Model</c> for the Nomad and BountyHunter, <c>Armature</c> for the
        /// PatrolRobots and DeathmatchBot, <c>Arm_DuneRat</c>, <c>Bone_Root</c> and <c>vrescal</c>
        /// for the three creatures.
        /// </para>
        /// </summary>
        private Transform ResolveBodyRoot()
        {
            var skin = GetComponentInChildren<SkinnedMeshRenderer>(true);
            Transform node = skin != null ? skin.rootBone : null;

            if (node == null)
            {
                var animator = GetComponentInChildren<Animator>(true);
                if (animator != null && animator.transform != transform) node = animator.transform;
            }

            while (node != null && node.parent != null && node.parent != transform)
                node = node.parent;

            return node != null && node != transform ? node : null;
        }

        private void OnValidate()
        {
            probeDistance = Mathf.Max(probeStartHeight + 0.5f, probeDistance);
        }
    }
}
```

- [ ] **Step 2: Type-check**

```bash
python3 tools/typecheck.py --editor
```
Expected: `No errors.`

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/agents/Animation/AgentGroundConform.cs Assets/Game/Scripts/agents/Animation/AgentGroundConform.cs.meta
git commit -m "feat: AgentGroundConform puts NavMesh agents on the real ground"
```

---

### Task 4: Integration test on real geometry

The unit tests pin the arithmetic. This one exists because the faults it catches are properties of the assembled agent: the wrong node resolved as the body root, a probe that finds the agent's own collider, and a closed loop that does not settle.

**Files:**
- Create: `Assets/Game/Editor/Tests/AgentGroundConformTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Game/Editor/Tests/AgentGroundConformTests.cs`:

```csharp
// The assembled agent, on real colliders.
//
// Runs in EditMode rather than PlayMode because everything needed is available without a running
// player loop -- AgentGroundConform.Initialise and .Conform are public and dt-driven for exactly
// this reason, and Unity's raycasts work against colliders in an edit-mode scene once the
// transforms are synced. Same shape as SpiderWalkerGroundingTests.
//
// The loop in each test does the NavMeshAgent's job by hand: the agent is what turns baseOffset
// into a transform position, and without it the conform would measure its own output and run away.
// Closing the loop here is also the point -- it proves the correction SETTLES rather than
// oscillating.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class AgentGroundConformTests
    {
        private const string GolemPath = "Assets/Game/Prefabs/agents/creatures/Golem.prefab";
        private const string NomadPath = "Assets/Game/Prefabs/agents/Characters/Nomad.prefab";

        /// The measured median error: the NavMesh floats a quarter of a metre over the sand.
        private const float NavMeshError = 0.257f;

        private GameObject world;
        private GameObject agent;

        [SetUp]
        public void SetUp()
        {
            world = new GameObject("TestWorld");
            Slab(new Vector3(0f, -1f, 0f), new Vector3(400f, 2f, 400f), Quaternion.identity);
            Slab(new Vector3(60f, 3.5f, 0f), new Vector3(60f, 2f, 200f), Quaternion.Euler(0f, 0f, -15f));
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (agent != null) Object.DestroyImmediate(agent);
            if (world != null) Object.DestroyImmediate(world);
        }

        private void Slab(Vector3 centre, Vector3 size, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(world.transform);
            go.transform.SetPositionAndRotation(centre, rotation);
            go.transform.localScale = size;
        }

        private AgentGroundConform Spawn(string prefabPath, Vector3 navMeshPosition)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, "prefab missing: " + prefabPath);

            agent = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            agent.transform.position = navMeshPosition;

            var conform = agent.GetComponent<AgentGroundConform>();
            Assert.IsNotNull(conform, prefabPath + " has no AgentGroundConform. " +
                                      "Run Tools > SpaceGame > Agents > Wire Ground Conform.");
            conform.Initialise();
            Physics.SyncTransforms();
            return conform;
        }

        /// <summary>Steps the conform and does the NavMeshAgent's job of applying baseOffset.</summary>
        private void Settle(AgentGroundConform conform, Vector3 navMeshPosition, int frames = 120)
        {
            var navAgent = agent.GetComponent<NavMeshAgent>();
            for (int i = 0; i < frames; i++)
            {
                conform.Conform(1f / 60f);
                agent.transform.position = navMeshPosition + Vector3.up * navAgent.baseOffset;
                Physics.SyncTransforms();
            }
        }

        [Test]
        public void TheBodyEndsUpOnTheSlabRatherThanAboveIt()
        {
            var navMeshPosition = new Vector3(0f, 0f + NavMeshError, 0f);   // slab top is y = 0
            var conform = Spawn(GolemPath, navMeshPosition);

            Settle(conform, navMeshPosition);

            Assert.AreEqual(0f, agent.transform.position.y, 0.02f,
                            "the Golem should be standing on the slab, not floating over it");
        }

        [Test]
        public void AHumanoidIsGroundedToo()
        {
            var navMeshPosition = new Vector3(0f, 0f + NavMeshError, 5f);
            var conform = Spawn(NomadPath, navMeshPosition);

            Settle(conform, navMeshPosition);

            Assert.AreEqual(0f, agent.transform.position.y, 0.02f);
        }

        /// <summary>
        /// The node the lean is written to, found the way the component finds it.
        ///
        /// Its authored rotation is NOT identity on every rig -- a Blender bone root usually
        /// carries a -90 degree X -- so every assertion here measures the lean as a change from
        /// the rest pose, never as an angle against world up.
        /// </summary>
        private static Transform BodyRoot(GameObject root)
        {
            var skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Transform node = skin != null ? skin.rootBone : null;
            while (node != null && node.parent != null && node.parent != root.transform)
                node = node.parent;
            Assert.IsNotNull(node, "no visual root resolved under " + root.name);
            return node;
        }

        /// <summary>
        /// A body that ignores the slope stands bolt upright on a hillside and reads as pasted on.
        /// The ramp is 15 degrees and slopeFollow is 0.8, so the body should lean about 12.
        /// </summary>
        [Test]
        public void TheBodyLeansIntoTheSlope()
        {
            // Top surface of the ramp slab at x = 60, allowing for its 15-degree tilt.
            var navMeshPosition = new Vector3(60f, 4.5f + NavMeshError, 0f);
            var conform = Spawn(GolemPath, navMeshPosition);

            Transform body = BodyRoot(agent);
            Quaternion rest = body.localRotation;

            Settle(conform, navMeshPosition);

            float lean = Quaternion.Angle(rest, body.localRotation);
            Assert.Greater(lean, 6f, "the body did not follow the slope at all");
            Assert.Less(lean, 18f, "the body over-leaned past the slope it is standing on");
        }

        [Test]
        public void OnFlatGroundTheBodyDoesNotLean()
        {
            var navMeshPosition = new Vector3(0f, NavMeshError, 10f);
            var conform = Spawn(GolemPath, navMeshPosition);

            Transform body = BodyRoot(agent);
            Quaternion rest = body.localRotation;

            Settle(conform, navMeshPosition);

            Assert.AreEqual(0f, Quaternion.Angle(rest, body.localRotation), 1f,
                            "a body on a flat slab must keep its authored pose");
        }

        /// <summary>
        /// The compounding failure, caught end to end. On the Nomad nothing animates the node the
        /// lean is written to, so a naive implementation reads its own output back and multiplies
        /// the lean in again every frame until the body is spinning. Ten seconds of frames is long
        /// enough that any accumulation is unmissable.
        /// </summary>
        [Test]
        public void TheTiltDoesNotAccumulateOnANodeNothingElseDrives()
        {
            var navMeshPosition = new Vector3(60f, 4.5f + NavMeshError, 6f);
            var conform = Spawn(NomadPath, navMeshPosition);

            Transform body = agent.transform.Find("Model");
            Assert.IsNotNull(body, "the Nomad's visual root is the Model child");
            Quaternion rest = body.localRotation;

            Settle(conform, navMeshPosition, frames: 600);

            Assert.Less(Quaternion.Angle(rest, body.localRotation), 18f,
                        "the lean accumulated instead of settling");
        }

        /// <summary>
        /// Over a hole the probe finds nothing, and holding the last correction would hang the body
        /// at the height of ground it is no longer above.
        /// </summary>
        [Test]
        public void WithNoGroundBelowItTheBodyReturnsToWhereNavigationPutIt()
        {
            var navMeshPosition = new Vector3(0f, NavMeshError, 0f);
            var conform = Spawn(GolemPath, navMeshPosition);
            Settle(conform, navMeshPosition);

            // Step off the world entirely.
            var overNothing = new Vector3(5000f, 50f, 5000f);
            Settle(conform, overNothing, frames: 240);

            Assert.AreEqual(overNothing.y, agent.transform.position.y, 0.02f);
        }
    }
}
```

- [ ] **Step 2: Run the fixture and verify it fails**

Run via `Unity_RunCommand` (or the Test Runner window):

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("AgentGroundConformTests");
```

Then poll:
```bash
cat Temp/headless_tests.txt
```
Expected: every test fails on `Golem.prefab has no AgentGroundConform. Run Tools > SpaceGame > Agents > Wire Ground Conform.` — the component is written but no prefab carries it yet. That is Task 5.

- [ ] **Step 3: Commit the test**

```bash
git add Assets/Game/Editor/Tests/AgentGroundConformTests.cs Assets/Game/Editor/Tests/AgentGroundConformTests.cs.meta
git commit -m "test: integration coverage for agent ground conforming"
```

---

### Task 5: Wire the component onto every agent prefab

Hand-adding it would not survive: the `*Builder` scripts overwrite their prefabs wholesale with no warning, which is exactly how `GolemBuilder` lost the Golem's `SaveableEntity`. So the wiring is a shared, idempotent function that both a menu item and each builder call.

**Files:**
- Create: `Assets/Game/Editor/Agents/AgentGroundConformWiring.cs`
- Modify: `Assets/Game/Editor/Creatures/GolemBuilder.cs`, `Assets/Game/Editor/Creatures/DuneRatBuilder.cs`, `Assets/Game/Editor/Creatures/VrescalBuilder.cs`, `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs`

- [ ] **Step 1: Write the wiring tool**

Create `Assets/Game/Editor/Agents/AgentGroundConformWiring.cs`:

```csharp
// Getting AgentGroundConform onto every agent that needs it, and keeping it there.
//
// Four of the ten agent prefabs are generated by a builder that overwrites the prefab wholesale, so
// a component added by hand disappears the next time somebody rebuilds -- which is how the Golem
// lost its SaveableEntity. Ensure() is therefore called from BOTH ends: from the menu item here,
// which fixes the six prefabs nobody generates, and from inside each builder.
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public static class AgentGroundConformWiring
    {
        private const string AgentPrefabRoot = "Assets/Game/Prefabs/agents";

        /// <summary>
        /// Adds <see cref="AgentGroundConform"/> to <paramref name="root"/> if it drives a
        /// NavMeshAgent and does not have one yet. Returns whether anything changed, so a caller
        /// can skip a pointless prefab save.
        /// </summary>
        public static bool Ensure(GameObject root)
        {
            if (root == null) return false;
            if (root.GetComponent<NavMeshAgent>() == null) return false;
            if (root.GetComponent<AgentGroundConform>() != null) return false;

            root.AddComponent<AgentGroundConform>();
            return true;
        }

        [MenuItem("Tools/SpaceGame/Agents/Wire Ground Conform")]
        public static void WireAll()
        {
            int changed = 0, seen = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { AgentPrefabRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponent<NavMeshAgent>() == null) continue;

                seen++;
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!Ensure(contents)) continue;

                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);

                    // A read-only AssetDatabase discards a prefab save and says nothing at all.
                    if (!saved)
                    {
                        Debug.LogError($"[AgentGroundConformWiring] Could not save {path}. " +
                                       "The AssetDatabase refused the write.");
                        continue;
                    }

                    changed++;
                    Debug.Log($"[AgentGroundConformWiring] Added AgentGroundConform to {path}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[AgentGroundConformWiring] {changed} of {seen} agent prefabs updated.");
        }
    }
}
```

- [ ] **Step 2: Call it from the four builders**

Insert this line immediately **before** the `PrefabUtility.SaveAsPrefabAsset(root, PrefabPath…)` call in each builder:

```csharp
            // Every component this prefab needs must be added HERE. A rebuild overwrites the asset
            // wholesale, so anything added by hand in the Inspector is silently gone.
            AgentGroundConformWiring.Ensure(root);
```

| File | Save call currently at |
| --- | --- |
| `Assets/Game/Editor/Creatures/GolemBuilder.cs` | line 503 |
| `Assets/Game/Editor/Creatures/DuneRatBuilder.cs` | line 561 |
| `Assets/Game/Editor/Creatures/VrescalBuilder.cs` | line 548 |
| `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` | line 195 |

All four builders are already in namespace `SpaceGame.EditorTools`, the same namespace as `AgentGroundConformWiring`, so **no `using` directive is needed in any of them**.

- [ ] **Step 3: Run the wiring**

Unity menu `Tools ▸ SpaceGame ▸ Agents ▸ Wire Ground Conform`.

Expected console output: ten `Added AgentGroundConform to …` lines, then `10 of 10 agent prefabs updated.` Run it a second time and expect `0 of 10 agent prefabs updated.` — it must be idempotent.

- [ ] **Step 4: Run the integration fixture and verify it now passes**

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("AgentGroundConformTests");
```
```bash
cat Temp/headless_tests.txt
```
Expected: `FAILED=0`.

- [ ] **Step 5: Run the whole suite**

Unity menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)`:
```bash
cat Temp/headless_tests.txt
```
Expected: `FAILED=0`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Editor/Agents/AgentGroundConformWiring.cs Assets/Game/Editor/Agents/AgentGroundConformWiring.cs.meta Assets/Game/Editor/Creatures/GolemBuilder.cs Assets/Game/Editor/Creatures/DuneRatBuilder.cs Assets/Game/Editor/Creatures/VrescalBuilder.cs Assets/Game/Editor/Agents/NomadPrefabBuilder.cs Assets/Game/Prefabs/agents
git commit -m "feat: wire AgentGroundConform onto every agent prefab"
```

---

### Task 6: Delete the stray baseOffset overrides

Four hand-placed agents carry `m_BaseOffset: 0.1`, pushing them 10 cm *further* into the air on top of the NavMesh error. Nothing sets it deliberately — every prefab is 0.

**Files:**
- Modify: `Assets/Game/Scenes/world/Chunks/Chunk_7_5.unity:8521`
- Modify: `Assets/Game/Scenes/Tests/FerdinandWorld/Chunks/FerdinandChunk_3_1.unity:15050`, `:16335`, `:27869`

- [ ] **Step 1: Confirm exactly four sites**

```bash
grep -rn --include='*.prefab' --include='*.unity' "m_BaseOffset" Assets | grep -v "m_BaseOffset: 0$"
```
Expected: exactly the four lines above and nothing else.

- [ ] **Step 2: Set them to zero**

Open each scene in the Unity Editor, select the NPC, and set the `NavMeshAgent` component's `Base Offset` field to `0`. Save the scene.

Do **not** hand-edit the YAML: these are scene files with a live Editor attached, and an edit made underneath it is invisible to the Editor and gets clobbered on the next save.

- [ ] **Step 3: Verify none remain**

```bash
grep -rn --include='*.prefab' --include='*.unity' "m_BaseOffset" Assets | grep -v "m_BaseOffset: 0$"
```
Expected: no output.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Game/Scenes/world/Chunks/Chunk_7_5.unity" "Assets/Game/Scenes/Tests/FerdinandWorld/Chunks/FerdinandChunk_3_1.unity"
git commit -m "fix: clear stray NavMeshAgent baseOffset overrides on four placed NPCs"
```

---

### Task 7: Verify it in the running game

The unit and integration tests prove the arithmetic and the assembly. Neither proves what the player sees.

**Files:** none — verification only.

- [ ] **Step 1: Measure the residual in play**

Enter play mode in `Assets/Game/Scenes/world/persistentScene.unity`, walk to where creatures are, and run this through `Unity_RunCommand`:

```csharp
using System.Text;
using UnityEngine;
using UnityEngine.AI;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var sb = new StringBuilder();
        foreach (var a in Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
        {
            var t = a.transform;
            float groundY = float.NaN;
            var hits = Physics.RaycastAll(t.position + Vector3.up * 3f, Vector3.down, 12f, ~0,
                                          QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(t)) continue;
                groundY = h.point.y;
                break;
            }
            sb.AppendLine(string.Format("{0,-22} pivot-ground={1,7:F3}  baseOffset={2,7:F3}",
                                        a.name, t.position.y - groundY, a.baseOffset));
        }
        result.Log(sb.ToString());
    }
}
```

Expected: `pivot-ground` within about ±0.03 m of zero for every agent standing on ground (it was +0.158 to +0.603 before), and `baseOffset` roughly the negative of the old error, around −0.15 to −0.60.

- [ ] **Step 2: Watch a creature walk across a dune**

Follow one on foot. Expect: feet stay in contact, the body leans into the slope on the way up and out of it on the way down, and there is no visible bobbing or jitter on flat sand. If it jitters, lower `heightFollowSpeed`; if it lags behind a run, raise it.

- [ ] **Step 3: Confirm the mounted jump still arcs**

Mount the Ostrich or a rideable agent and jump. Expect a clean arc. This is the regression the `baseOffset` composition in Task 2 exists to prevent — if the jump is flattened, `ApplyBaseOffset` is not summing `jumpArc`.

- [ ] **Step 4: Verify on an actual client**

```
# menu: Tools ▸ Tests ▸ Build Multiplayer Test Player
open "../Build/MPTest/SpaceGameMP.app" --args -sgprofile client
```

Host in the Editor, join with that build, and look at the same creature on both machines. Expect: grounded on both, leaning the same way on both. A creature grounded on the host but floating on the client means the height is not reaching the client through the replicated transform; a creature grounded on both but upright on the client means `AgentGroundConform` is being disabled on the remote copy — check that it is not being picked up by `SimulationDrivers.Discover`, which should only match `AgentController`, `IMovementMotor` and `NavMeshAgent`.

- [ ] **Step 5: Verify it survives save / quit / load**

Save, quit to the menu, load the world back. Expect creatures grounded immediately on the first frame they appear, with no visible settle. `AgentGrounding.Reset` in `OnEnable` plus the snap-on-first-frame rule is what buys that.

**There is deliberately nothing to persist.** Both outputs are re-derived from a probe within one frame of the component being enabled, and `Reset` makes that first frame snap rather than ease, so a saved value would only ever be a stale copy of something already recomputed. Do not add a saver.

---

### Task 8: Documentation

Every change to behaviour updates its doc in the same commit.

**Files:**
- Modify: `docs/AI/systems/AgentSystem.md`
- Modify: `docs/AI/systems/NavMeshSystem.md`
- Modify: `docs/AI/systems/Locomotion.md`

- [ ] **Step 1: AgentSystem.md**

Add to the `symptoms:` frontmatter list:

```yaml
  - "every creature and NPC hovers a hand's width above the sand"
  - "an NPC stands bolt upright on a dune instead of leaning into it"
```

Add a row to the Key types table, next to `AgentAnimatorDriver`:

```markdown
| `AgentGroundConform` | [Animation/AgentGroundConform.cs](Assets/Game/Scripts/agents/Animation/AgentGroundConform.cs) | Probes the real ground each `LateUpdate` and produces two outputs from one probe: a height correction into `NavMeshAgentMotor.GroundOffset` (authority only), and a slope lean on the body's visual root (every machine) |
```

Add to `## Gotchas`:

```markdown
- **Agents stand on the NavMesh, and the NavMesh is not the ground.** The world bake floats a
  median of 0.257 m above the terrain (max 0.600) — see [NavMeshSystem.md](NavMeshSystem.md).
  `AgentGroundConform` corrects it per frame. A constant `baseOffset` cannot: the error is
  terrain-dependent, so a single number leaves the body buried at one end of the range and floating
  at the other. Every agent prefab's `baseOffset` is 0 and should stay that way.
- **`baseOffset` has three authors now** — the prefab's own value, the ground correction and the
  mounted-jump arc — summed in `NavMeshAgentMotor.ApplyBaseOffset`. Assign `agent.baseOffset`
  directly from anywhere else and whichever writer runs later in the frame silently erases the rest.
- **The node the slope lean is written to is animated on the Golem and the DuneRat and on nothing
  else.** `AgentGrounding.Baseline` works out which case it is by reading the transform back rather
  than from a per-prefab flag. Replace it with a flag and you get one of two silent failures: the
  lean erases the root-bone animation, or it multiplies into itself every frame and the body spins.
```

- [ ] **Step 2: NavMeshSystem.md**

Add to `symptoms:`:

```yaml
  - "every agent hovers a few centimetres to half a metre above the ground"
```

Add to `## Gotchas`:

```markdown
- **The baked mesh sits above the ground, and by a varying amount.** Measured over 1384 samples on
  six terrains: mean +0.264 m, median +0.257, p25 +0.199, p75 +0.321, p95 +0.480, max +0.600, min
  −0.262. Recast places each polygon at the top of the voxel column it came from, so the error
  scales with `voxelSize` (0.3333 here) and is inherent to the bake, not a fault in it. Halving
  `voxelSize` roughly halves it while quadrupling bake time and asset size — not worth it.
  `AgentGroundConform` corrects it at runtime instead; see [AgentSystem.md](AgentSystem.md).
```

- [ ] **Step 3: Locomotion.md**

Add a row to the Key types table:

```markdown
| `AgentGrounding` / `AgentGroundingSettings` | [Ground/AgentGrounding.cs](Assets/Game/Scripts/Locomotion/Ground/AgentGrounding.cs) | Pure per-frame solve for a NavMesh agent's height correction and slope lean. No physics; reuses `WalkerSupportPlane.Tilt` for the lean |
```

- [ ] **Step 4: Bump `updated:` and regenerate**

Set `updated: 2026-09-05` in the frontmatter of all three docs, then:

```bash
python3 tools/docs_check.py --index
```
Expected: regenerates `INDEX.md` and `ROUTING.md`, then reports validation passed. **Never hand-edit those two files.**

- [ ] **Step 5: Commit**

```bash
git add docs/AI
git commit -m "docs: agent ground conforming and the NavMesh height error"
```

---

### Task 9: Final verification

- [ ] **Step 1: Type-check everything**

```bash
python3 tools/typecheck.py --editor
```
Expected: `No errors.`

- [ ] **Step 2: Full EditMode suite**

Unity menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)` from a clean scene:
```bash
cat Temp/headless_tests.txt
```
Expected: `FAILED=0` and `DONE`.

- [ ] **Step 3: Confirm the working tree is what you meant to change**

```bash
git status
git log --oneline main..HEAD
```
Expected: seven commits, and no unexpected modified scene or prefab files.

- [ ] **Step 4: Report honestly**

State which of Task 7's five in-game checks actually ran and what the measured `pivot-ground` residuals were. A step that was skipped — no second machine to hand, say — is reported as skipped, not as passed.
