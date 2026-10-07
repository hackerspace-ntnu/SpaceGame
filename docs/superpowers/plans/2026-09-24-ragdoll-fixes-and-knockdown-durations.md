# Ragdoll Fixes and Event-Driven Knockdown Durations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop ragdolls from mangling the mesh, jittering on the ground and outstaying their welcome, then put every ragdoll behind one controller whose down-time is computed from the event that caused it (fall 1 s, hit 1–2 s scaled by damage / health left / knockback, death until despawn).

**Architecture:** Phase A fixes `RagdollRig` itself (joints, velocity, root follow, recovery, settling, budget), one defect per task, each with an EditMode test. Phase B extracts the duplicated logic of `AgentRagdoll`/`PlayerRagdoll` into an abstract `RagdollController`, adds a pure `KnockdownPolicy` that turns a `KnockdownEvent` into seconds, and routes every knockdown source (blasts, hits, falls) through a server-side `RagdollController.Knock`. The duration keeps travelling inside `NetMsg.Knockdown` so every machine stands up together.

**Tech Stack:** Unity 6 (PhysX `Rigidbody`/`CharacterJoint`), Netcode for GameObjects through the project's `NetMessaging`/`NetMsg` layer, NUnit EditMode tests in `Assets/Game/Editor/Tests`, run through Unity MCP `run_tests`.

**Spec:** This plan's own "Decisions" section below (from the 2026-09-24 conversation) plus the defect map in the same conversation. Governing doc: [docs/AI/systems/Combat.md](../../AI/systems/Combat.md) (ragdoll rows, Flows step 7, Gotchas).

## Decisions (agreed with the user, 2026-09-24)

| Event | Down-time |
| --- | --- |
| Fall that deals fall damage | flat `fallSeconds` = **1 s** |
| Hit (damage) | **0 s below a severity threshold**, otherwise 1 → 2 s, scaled by *damage dealt*, *how little health is left after the hit* and *the knockback of the event* |
| Blast (gauntlet, singularity) | same formula as a hit, but never less than 1 s |
| Death | until despawn (creature) / until respawn (player) — unchanged |
| Net | **flat 3 s, no struggle** — **out of scope here, see Phase C** |

- Applies to **players and creatures**. Falls only reach players (only `PlayerMovement` computes fall damage).
- A body that just stood up is immune to *hit* knockdowns for `reknockImmunitySeconds` (default 1 s, 0 disables) so automatic fire cannot stun-lock. Blasts and falls ignore the immunity.
- Once the down-time has run out, the body stands up when it has come to rest, or after `settleGraceSeconds` more, whichever is first. The absolute 4 s ceiling is gone for knockdowns (it stays as the corpse sleep timer).

## Global Constraints

- CLAUDE.md non-negotiables: works on host **and** client; survives save/quit/load; no dead code, no commented-out code, no debug logs, no magic numbers (serialize tunables), no silent `catch`, names say what things are.
- Every behaviour change updates `docs/AI/systems/Combat.md` in the **same commit**, deletes what the change made untrue, bumps `updated:`, then `python3 tools/docs_check.py --index` must pass. `INDEX.md`/`ROUTING.md` are generated — never hand-edit.
- The working tree has many unrelated uncommitted changes on `Feat/create-factions`. **Stage only the files each task names** (`git add <paths>`), never `git add -A` / `git add .`.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.
- Tests: EditMode, namespace `SpaceGame.EditorTools`, folder `Assets/Game/Editor/Tests`. Run through Unity MCP `run_tests` (mode `EditMode`, filter by class name). A test run holds the assembly-reload lock — do not edit scripts while one is running.
- Existing suites that must stay green after every task: `RagdollSkeletonTests`, `HogtieTests`, `NetGunTests`, `BodyAttachmentTests`, `SingularityTests`, `LeashConstraintTests`.
- Match the file's comment style: XML doc comments that explain *why*, prose not bullet lists.

## Review Focus

1. **A client watching a knocked-down creature or player** — expect the body to lie in the same place and roughly the same orientation as on the host, not an upright pelvis with the limbs hanging off it, and not flung twice as far. (Manual, Task B5.)
2. **Save/quit/load with a corpse lying on a slope** — expect it to reload lying where it lay, not standing, not thrown again, not under the terrain. (Manual, Task B5.)
3. **Automatic fire into one creature** — expect small hits never to knock it down and a knocked-down creature not to be re-knocked the instant it stands. (Test in Task B1: `Immune_BlocksHitsJustAfterStandingUp_ButNotBlasts`, `Seconds_SmallHitAtFullHealth_DoesNotKnockDown`.)
4. **A knockdown on a player sitting in a seat or saddle** — expect it to be refused, not a limp body dragged through the ground. (Test in Task B2: `Knockdown_RefusedWhileRiding`.)
5. **Gear destroyed between two knockdowns** (a gauntlet stripped from a bone that held a body) — expect the second knockdown to rebuild joints without throwing. (Test in Task A2: `GoLimp_AfterAJointedBoneIsDestroyed_DoesNotThrow`.)

## File Structure

| File | Change | Responsibility |
| --- | --- | --- |
| `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs` | Modify | Physical skeleton: build, go limp, follow, recover, freeze |
| `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollSkeleton.cs` | Modify | Pure math: add `JointAxes`, `ClampMassRatios` |
| `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollBudget.cs` | Modify | Evict corpses only |
| `Assets/Game/Scripts/Gameplay/Ragdoll/KnockdownPolicy.cs` | Create | Pure: `RagdollCause`, `KnockdownEvent`, `KnockdownTuning`, `KnockdownPolicy` |
| `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollController.cs` | Create | Abstract base: when to go limp, how long, holds, stand-up, knock entry points |
| `Assets/Game/Scripts/Gameplay/Ragdoll/AgentRagdoll.cs` | Modify | Shrinks to creature-specific layers (brain, motor, legs, rider) |
| `Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs` | Modify | Shrinks to player-specific layers (input, look, camera, standing hold) |
| `Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs` | Modify | `Knockdown` gains `B = cause`; new `KnockdownRequest = 116` |
| `Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs` | Modify | Knock through `RagdollController.Knock`; drop `downedSeconds` |
| `Assets/Game/Scripts/Items/Artifacts/BottledSingularity/SingularityWell.cs` | Modify | Same |
| `Assets/Game/Scripts/Characters/Player/Movement/Movement.cs` | Modify | Request a fall knockdown when fall damage lands |
| `Assets/Game/Editor/Tests/RagdollRigTests.cs` | Create | Scene-free rig tests (joints, velocity, follow, recovery, settling) |
| `Assets/Game/Editor/Tests/RagdollSkeletonTests.cs` | Modify | Tests for the new pure functions |
| `Assets/Game/Editor/Tests/KnockdownPolicyTests.cs` | Create | Duration and stand-up rules |
| `docs/AI/systems/Combat.md` | Modify | Every task |

---

## Phase A — Rig defects

### Task A0: Test fixture for a scene-free rig

**Files:**
- Create: `Assets/Game/Editor/Tests/RagdollRigTests.cs`

**Interfaces:**
- Produces: `RagdollRigTests` fixture with `NewHumanoidRig(out RagdollRig rig)`, `NewSplitRig(out RagdollRig rig)`, `Bone(root, name)`, `StepPhysics(int steps)`, `Invoke(object target, string method, params object[] args)`. Later Phase A tasks add tests to this class.

- [ ] **Step 1: Write the fixture and a smoke test**

```csharp
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpaceGame.Gameplay.Ragdoll;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The ragdoll's physical behaviour, measured on hard-surface rigs built in code: empty bones
    /// with cubes parented on as leaves, which is exactly the shape RagdollRig's structural rule
    /// selects. Physics runs in Script mode so each test decides how many steps happen.
    /// </summary>
    public class RagdollRigTests
    {
        private const float Step = 0.02f;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private SimulationMode originalSimulationMode;
        private Vector3 originalGravity;

        [SetUp]
        public void SetUp()
        {
            originalSimulationMode = Physics.simulationMode;
            originalGravity = Physics.gravity;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();

            Physics.simulationMode = originalSimulationMode;
            Physics.gravity = originalGravity;
        }

        private static void StepPhysics(int steps)
        {
            for (int i = 0; i < steps; i++) Physics.Simulate(Step);
        }

        /// <summary>
        /// Calls a method by name, searching base classes too: reflection never returns a base
        /// class's PRIVATE members from a derived type, and RagdollController's private handlers
        /// (Phase B) are reached through AgentRagdoll.
        /// </summary>
        private static object Invoke(object target, string method, params object[] args)
        {
            MethodInfo info = null;
            for (System.Type type = target.GetType(); type != null && info == null; type = type.BaseType)
                info = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic |
                                              BindingFlags.Public | BindingFlags.DeclaredOnly);

            Assert.IsNotNull(info, $"{target.GetType().Name}.{method} no longer exists — the test " +
                                   "would silently measure nothing.");
            return info.Invoke(target, args);
        }

        /// <summary>An empty bone with one collider-less cube hanging under it.</summary>
        private static Transform Bone(Transform parent, string name, Vector3 localPosition,
                                      Vector3 partSize, Vector3 partOffset)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;

            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.name = name + "_Mesh";
            part.transform.SetParent(bone, false);
            part.transform.localPosition = partOffset;
            part.transform.localScale = partSize;

            return bone;
        }

        private RagdollRig Rig(GameObject root)
        {
            RagdollRig rig = root.AddComponent<RagdollRig>();
            Invoke(rig, "Awake");
            return rig;
        }

        /// <summary>Hips → spine; hips → thigh → shin, both sides. Everything under the hips.</summary>
        private GameObject NewHumanoidRig(out RagdollRig rig)
        {
            var root = new GameObject("Humanoid");
            spawned.Add(root);

            Transform hips = Bone(root.transform, "Hips", new Vector3(0f, 1f, 0f),
                                  new Vector3(0.4f, 0.3f, 0.3f), Vector3.zero);
            Bone(hips, "Spine", new Vector3(0f, 0.2f, 0f),
                 new Vector3(0.4f, 0.5f, 0.25f), new Vector3(0f, 0.25f, 0f));

            foreach (float side in new[] { -1f, 1f })
            {
                Transform thigh = Bone(hips, side < 0 ? "ThighL" : "ThighR",
                                       new Vector3(0.15f * side, -0.1f, 0f),
                                       new Vector3(0.15f, 0.4f, 0.15f), new Vector3(0f, -0.2f, 0f));
                Bone(thigh, side < 0 ? "ShinL" : "ShinR", new Vector3(0f, -0.4f, 0f),
                     new Vector3(0.12f, 0.4f, 0.12f), new Vector3(0f, -0.2f, 0f));
            }

            rig = Rig(root);
            return root;
        }

        /// <summary>
        /// A body and a leg that are SIBLINGS under an empty pelvis — the ostrich's shape. The pelvis
        /// carries nothing, so the body becomes the ragdoll's root bone and the leg is jointed to it
        /// without being its child in the hierarchy.
        /// </summary>
        private GameObject NewSplitRig(out RagdollRig rig, out Transform body, out Transform leg)
        {
            var root = new GameObject("Split");
            spawned.Add(root);

            var pelvis = new GameObject("Pelvis").transform;
            pelvis.SetParent(root.transform, false);
            pelvis.localPosition = new Vector3(0f, 1f, 0f);

            body = Bone(pelvis, "Body", Vector3.zero, new Vector3(0.6f, 0.4f, 0.8f), Vector3.zero);
            leg = Bone(pelvis, "Leg", new Vector3(0f, -0.2f, 0f),
                       new Vector3(0.12f, 0.6f, 0.12f), new Vector3(0f, -0.3f, 0f));

            rig = Rig(root);
            return root;
        }

        private static Transform Find(GameObject root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;

            Assert.Fail($"No '{name}' under {root.name}.");
            return null;
        }

        [Test]
        public void Fixture_BuildsAJointedSkeleton()
        {
            NewHumanoidRig(out RagdollRig rig);

            rig.GoLimp(Vector3.zero);

            Assert.AreEqual(6, rig.BoneCount, "hips, spine, two thighs, two shins");
            Assert.AreEqual(5, rig.JointCount, "every bone but the hips is jointed to its parent");
        }
    }
}
```

- [ ] **Step 2: Run it**

Run: Unity MCP `run_tests` (mode `EditMode`, filter `RagdollRigTests`).
Expected: PASS. If `BoneCount` differs, print `rig.Measure`/`CandidateCount` and fix the fixture (not the rig) — the weight floor is 0.015 of total bulk and every cube above clears it.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Editor/Tests/RagdollRigTests.cs Assets/Game/Editor/Tests/RagdollRigTests.cs.meta
git commit -m "test(ragdoll): scene-free rig fixture"
```

---

### Task A1: The budget evicts corpses only, and recovery survives a frozen rig

Defect: `RagdollBudget` may `Freeze` a *living* knocked-down body. `Freeze` sets `IsLimp = false` without re-enabling the Animator, the adapter's rescue calls `Recover()`, which returns early on `!IsLimp` — the creature gets its brain back while stuck in the ragdoll pose forever.

**Files:**
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollBudget.cs` (`Judge`, `OldestEvictable`)
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs` (new `IsCorpse`, `Freeze`, `Recover`)
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/AgentRagdoll.cs`, `PlayerRagdoll.cs` (`OnDeath`/`OnRevive` set `IsCorpse`)
- Modify: `Assets/Game/Editor/Tests/HogtieTests.cs`, `NetGunTests.cs` — only calls of `RagdollBudget.Judge` (signature change)
- Test: `Assets/Game/Editor/Tests/RagdollRigTests.cs`

**Interfaces:**
- Produces: `RagdollRig.IsCorpse { get; set; }`; `RagdollBudget.Judge(bool excluded, bool exempt, bool corpse, bool settled)`.

- [ ] **Step 1: Write the failing tests**

Add to `RagdollRigTests`:

```csharp
        [Test]
        public void Judge_NeverEvictsALivingBody()
        {
            Assert.AreEqual(RagdollBudget.Verdict.Skip,
                RagdollBudget.Judge(excluded: false, exempt: false, corpse: false, settled: true),
                "a knocked-down body that is still alive stands up within seconds — freezing it " +
                "leaves it standing in its ragdoll pose with its brain switched back on");
            Assert.AreEqual(RagdollBudget.Verdict.Take,
                RagdollBudget.Judge(excluded: false, exempt: false, corpse: true, settled: true));
            Assert.AreEqual(RagdollBudget.Verdict.Consider,
                RagdollBudget.Judge(excluded: false, exempt: false, corpse: true, settled: false));
        }

        [Test]
        public void Recover_AfterFreeze_GivesTheAnimatorBack()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            Animator animator = root.AddComponent<Animator>();
            Invoke(rig, "Awake");   // re-resolve the animator now that it exists

            rig.GoLimp(Vector3.zero);
            Assert.IsFalse(animator.enabled);

            rig.Freeze();
            rig.Recover();

            Assert.IsTrue(animator.enabled,
                "Freeze took the body off physics; nothing else will ever switch the animator on");
        }
```

- [ ] **Step 2: Run to verify they fail**

Run: `run_tests` EditMode filter `RagdollRigTests`.
Expected: compile error (`Judge` has no `corpse` parameter), then after Step 3's signature change only, `Recover_AfterFreeze_GivesTheAnimatorBack` fails on the animator assertion.

- [ ] **Step 3: Implement**

`RagdollBudget.cs` — replace `Judge` and its call:

```csharp
        public static Verdict Judge(bool excluded, bool exempt, bool corpse, bool settled)
        {
            // A living body is never a candidate. A knockdown ends by itself within seconds, and
            // freezing one mid-knockdown left it standing in its ragdoll pose with its brain back
            // on — the budget exists to reclaim corpses, which are the bodies that never end.
            if (excluded || exempt || !corpse) return Verdict.Skip;

            return settled ? Verdict.Take : Verdict.Consider;
        }
```

and in `OldestEvictable`:

```csharp
                switch (Judge(live[i] == exclude, live[i].BudgetExempt, live[i].IsCorpse,
                              live[i].IsSettled))
```

Update the `Judge` XML doc: add "corpse" to the facts it combines, and state that living bodies are skipped. Update any `RagdollBudget.Judge(` call in `HogtieTests.cs` / `NetGunTests.cs` (grep `Judge(`) to pass `corpse: true` so they keep testing what they tested.

`RagdollRig.cs` — add under `BudgetExempt`:

```csharp
        /// <summary>
        /// Is this body dead? Set by the adapter on death and cleared on revive.
        ///
        /// <para>
        /// Only corpses may be evicted by <see cref="RagdollBudget"/>. A living body is limp for a
        /// few seconds and then stands up; freezing one in between left it frozen in its ragdoll
        /// pose while its brain came back on.
        /// </para>
        /// </summary>
        public bool IsCorpse { get; set; }

        /// <summary>Did <see cref="Freeze"/> take this body off physics while it was limp?</summary>
        private bool frozen;
```

In `Freeze()` after `IsLimp = false;` add `frozen = true;`.

Replace the first line of `Recover()`:

```csharp
            if (frozen) return Thaw();
            if (!IsLimp) return new TeleportMove(transform.position, transform.rotation,
                                                 transform.position, transform.rotation);
```

and add below `Recover`:

```csharp
        /// <summary>
        /// Give a frozen body back to its animation. The bones stay where they were frozen and the
        /// root does not move: there is no body left to measure, and a corpse revived out of the
        /// budget is the only way here.
        /// </summary>
        private TeleportMove Thaw()
        {
            frozen = false;
            if (animator != null) animator.enabled = true;

            return new TeleportMove(transform.position, transform.rotation,
                                    transform.position, transform.rotation);
        }
```

In `GoLimp` set `frozen = false;` right after `if (!built) Build();`.

`AgentRagdoll.OnDeath` and `PlayerRagdoll.OnDeath`: add `rig.IsCorpse = true;` next to `rig.BudgetExempt = false;`. Their `OnRevive`: add `rig.IsCorpse = false;`.

- [ ] **Step 4: Run tests**

Run: `run_tests` EditMode filters `RagdollRigTests`, `HogtieTests`, `NetGunTests`.
Expected: all PASS.

- [ ] **Step 5: Docs**

`docs/AI/systems/Combat.md`: in the `RagdollBudget` key-types row write "Static per-process cap; freezes the oldest settled **corpse** (living bodies are never evicted)". Replace the gotcha "A ragdoll frozen out from under you" with: "`RagdollBudget` only evicts corpses (`RagdollRig.IsCorpse`). It used to freeze living knocked-down bodies, and `Recover` returned early on the frozen rig, so the creature got its brain back while stuck in its ragdoll pose. `Recover` on a frozen rig now thaws it (`Thaw`)." Add frontmatter symptom: `"a creature stands up frozen in its ragdoll pose and slides around"`. Bump `updated:`. Run `python3 tools/docs_check.py --index`; expect exit 0.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollBudget.cs Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs Assets/Game/Scripts/Gameplay/Ragdoll/AgentRagdoll.cs Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs Assets/Game/Editor/Tests/RagdollRigTests.cs Assets/Game/Editor/Tests/HogtieTests.cs Assets/Game/Editor/Tests/NetGunTests.cs docs/AI/systems/Combat.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "fix(ragdoll): budget evicts corpses only; recover thaws a frozen rig"
```

---

### Task A2: Joint axes follow the bone, and joints are rebuilt from the pose at each knockdown

Defects: `BuildJoint` never sets `axis`/`swingAxis`, so the twist limit lies across the bone and the swing limits allow ±45° twist about it — spines, necks and forearms twist until the skin collapses. And joints are built once on the first limp, so every later knockdown enforces limits relative to that first pose and snaps the body the moment it goes limp.

**Files:**
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollSkeleton.cs` (add `JointAxes`)
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs` (`Bone.Parent`, `Build`, `BuildJoint`, new `RebuildJoints`, `LongAxis` → `LocalBoneDirection`)
- Test: `RagdollSkeletonTests.cs`, `RagdollRigTests.cs`

**Interfaces:**
- Produces: `RagdollSkeleton.JointAxes(Vector3 alongBone, out Vector3 twist, out Vector3 swing)`; `RagdollRig.Bone.Parent` (`Rigidbody`, null for the root bone).

- [ ] **Step 1: Write the failing tests**

`RagdollSkeletonTests.cs`:

```csharp
        [Test]
        public void JointAxes_TwistRunsDownTheBone_SwingIsPerpendicular()
        {
            foreach (Vector3 along in new[] { Vector3.down, new Vector3(0.3f, 0.9f, 0.1f), Vector3.forward })
            {
                RagdollSkeleton.JointAxes(along, out Vector3 twist, out Vector3 swing);

                Assert.Less(Vector3.Angle(twist, along), 0.01f, "twist must be the bone's own length");
                Assert.AreEqual(0f, Vector3.Dot(twist, swing), 1e-4f, "swing must be perpendicular");
                Assert.AreEqual(1f, swing.magnitude, 1e-4f);
            }
        }

        [Test]
        public void JointAxes_ZeroLengthBone_FallsBackToAValidPair()
        {
            RagdollSkeleton.JointAxes(Vector3.zero, out Vector3 twist, out Vector3 swing);

            Assert.AreEqual(1f, twist.magnitude, 1e-4f);
            Assert.AreEqual(0f, Vector3.Dot(twist, swing), 1e-4f);
        }
```

`RagdollRigTests.cs`:

```csharp
        [Test]
        public void Joints_TwistAboutTheirOwnBone()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            rig.GoLimp(Vector3.zero);

            var thigh = Find(root, "ThighL").GetComponent<CharacterJoint>();
            Vector3 towardShin = thigh.transform.InverseTransformDirection(
                Find(root, "ShinL").position - thigh.transform.position);

            Assert.Less(Vector3.Angle(thigh.axis, towardShin), 1f,
                "the twist axis lies across the thigh, so its ±45° swing limit is a twist limit");
        }

        [Test]
        public void SecondKnockdown_LimitsAreRelativeToTheNewPose()
        {
            Physics.gravity = Vector3.zero;
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            Transform thigh = Find(root, "ThighL");

            rig.GoLimp(Vector3.zero, settled: true);
            rig.Recover();

            // Well past the 45° swing limit measured from the first pose.
            Quaternion posed = Quaternion.Euler(80f, 0f, 0f);
            thigh.localRotation = posed;

            rig.GoLimp(Vector3.zero);
            StepPhysics(20);

            Assert.Less(Quaternion.Angle(thigh.localRotation, posed), 3f,
                "the joint still measures its limits from the first knockdown's pose and snapped the leg");
        }

        [Test]
        public void GoLimp_AfterAJointedBoneIsDestroyed_DoesNotThrow()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            rig.GoLimp(Vector3.zero, settled: true);
            rig.Recover();

            Object.DestroyImmediate(Find(root, "ThighL").gameObject);

            Assert.DoesNotThrow(() => rig.GoLimp(Vector3.zero));
            Assert.AreEqual(3, rig.JointCount, "spine, and the right thigh and shin");
        }
```

- [ ] **Step 2: Run to verify they fail**

Run: `run_tests` EditMode filters `RagdollSkeletonTests`, `RagdollRigTests`.
Expected: compile error on `JointAxes`; after adding a stub, `Joints_TwistAboutTheirOwnBone` and `SecondKnockdown_LimitsAreRelativeToTheNewPose` FAIL.

- [ ] **Step 3: Implement**

`RagdollSkeleton.cs`, add:

```csharp
        /// <summary>
        /// The two axes a <c>CharacterJoint</c> needs, in the bone's local space: twist down the
        /// bone, swing perpendicular to it.
        ///
        /// <para>
        /// Left at Unity's defaults the joint twists about local X and swings about local Y, and on
        /// a rig whose bones run along Y — Mixamo, and most Blender exports — that puts the tight
        /// twist limit across the limb and the loose swing limits around it. Every spine and neck
        /// joint could then corkscrew forty-five degrees each way, which a skinned surface shows as
        /// a limb collapsing into itself.
        /// </para>
        /// </summary>
        /// <param name="alongBone">Direction from this joint toward the next one, bone-local.</param>
        public static void JointAxes(Vector3 alongBone, out Vector3 twist, out Vector3 swing)
        {
            twist = alongBone.sqrMagnitude > 1e-8f ? alongBone.normalized : Vector3.right;

            // The basis vector least parallel to the twist gives the best-conditioned perpendicular.
            float x = Mathf.Abs(twist.x), y = Mathf.Abs(twist.y), z = Mathf.Abs(twist.z);
            Vector3 seed = x <= y && x <= z ? Vector3.right : y <= z ? Vector3.up : Vector3.forward;

            swing = Vector3.ProjectOnPlane(seed, twist).normalized;
        }
```

`RagdollRig.cs`:

1. In `class Bone` add:

```csharp
            /// <summary>The body this bone is jointed to. Null for the root bone.</summary>
            public Rigidbody Parent;
```

2. In `Build()`, replace the joint creation inside the loop:

```csharp
                if (parent != null && bodies.TryGetValue(parent, out Rigidbody parentBody))
                    made.Parent = parentBody;
```

(`joints` is no longer filled in `Build`.)

3. Add after `ApplySelfCollision`:

```csharp
        /// <summary>
        /// Throw away the joints and wire fresh ones from the pose the body is in right now.
        ///
        /// <para>
        /// A joint measures its limits from the pose it was created in. Built once, on the first
        /// knockdown, every later knockdown was judged against whatever the creature happened to be
        /// doing that first time — and a body already past a limit from there was snapped back on
        /// the first physics step. Rebuilding costs a couple of dozen component adds per knockdown.
        /// </para>
        ///
        /// <para>
        /// DestroyImmediate, not Destroy: this runs on the frame the body goes dynamic, and a
        /// deferred destroy would leave two joints on one bone for that frame's physics step.
        /// </para>
        /// </summary>
        private void RebuildJoints()
        {
            foreach (Joint joint in joints)
                if (joint != null) DestroyImmediate(joint);
            joints.Clear();

            foreach (Bone bone in bones)
            {
                if (bone == bones[0]) continue;

                // A parent destroyed with worn gear leaves the branch hanging off the root bone —
                // the same fallback Build uses for a branch with no simulated ancestor.
                Rigidbody parent = bone.Parent != null ? bone.Parent : bones[0].Body;
                joints.Add(BuildJoint(bone, parent));
            }
        }
```

4. In `GoLimp`, inside `if (!IsLimp)`, call `RebuildJoints();` immediately before the `foreach (Bone bone in bones)` that switches bodies dynamic.

5. Replace `BuildJoint`:

```csharp
        private Joint BuildJoint(Bone bone, Rigidbody parent)
        {
            var joint = bone.Transform.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parent;
            joint.enablePreprocessing = false;

            RagdollSkeleton.JointAxes(LocalBoneDirection(bone.Transform, BoneTransforms()),
                                      out Vector3 twist, out Vector3 swing);
            joint.axis = twist;
            joint.swingAxis = swing;

            joint.swing1Limit = new SoftJointLimit { limit = swingLimit };
            joint.swing2Limit = new SoftJointLimit { limit = swingLimit };
            joint.lowTwistLimit = new SoftJointLimit { limit = -twistLimit };
            joint.highTwistLimit = new SoftJointLimit { limit = twistLimit };

            return joint;
        }
```

6. Replace `LongAxis` with a direction helper and a thin axis wrapper (the capsule code keeps calling `LongAxis`; `SegmentLength`/`CreateCollider` take `List<Transform>`, so accept `IList<Transform>` in both new helpers and change `LongAxis`'s parameter type to match):

```csharp
        /// <summary>
        /// Which way the bone runs, in its own local space: toward its first simulated child, else
        /// its first child, else onward from its parent (a hand, a head, a foot).
        /// </summary>
        private static Vector3 LocalBoneDirection(Transform bone, IList<Transform> simulated)
        {
            for (int i = 0; i < bone.childCount; i++)
            {
                Transform child = bone.GetChild(i);
                if (simulated.Contains(child))
                    return bone.InverseTransformDirection(child.position - bone.position);
            }

            if (bone.childCount > 0)
                return bone.InverseTransformDirection(bone.GetChild(0).position - bone.position);

            return bone.parent != null
                ? bone.InverseTransformDirection(bone.position - bone.parent.position)
                : Vector3.up;
        }

        /// <summary>The bone's local axis pointing down its own segment, and which way along it.</summary>
        private static int LongAxis(Transform bone, IList<Transform> simulated, out float sign)
        {
            Vector3 local = LocalBoneDirection(bone, simulated);
            int axis = 0;
            if (Mathf.Abs(local.y) > Mathf.Abs(local[axis])) axis = 1;
            if (Mathf.Abs(local.z) > Mathf.Abs(local[axis])) axis = 2;

            sign = local[axis] < 0f ? -1f : 1f;
            return axis;
        }
```

Add `using System.Collections.Generic;` is already present. Update the class-level remark "Once built it is kept and switched kinematic" to say joints are rebuilt on every knockdown while bodies and colliders are kept.

- [ ] **Step 4: Run tests**

Run: `run_tests` EditMode filters `RagdollSkeletonTests`, `RagdollRigTests`, `HogtieTests`, `NetGunTests`.
Expected: PASS.

- [ ] **Step 5: Docs**

Combat.md: `RagdollRig` key-types row → "Builds bodies on first limp and **rebuilds joints on every limp**; …". Add gotcha: "**Joint axes must run down the bone.** `CharacterJoint` defaults to twist about local X; on Y-along-bone rigs that put the ±25° twist limit across the limb and let every spine/neck joint corkscrew ±45°, collapsing the skin. `RagdollSkeleton.JointAxes` sets them. Joints are rebuilt at each knockdown because limits are measured from the creation pose." Symptom: `"the ragdoll's neck, spine or arms twist until the mesh collapses"`. Bump `updated:`, run the validator.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollSkeleton.cs Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs Assets/Game/Editor/Tests/RagdollSkeletonTests.cs Assets/Game/Editor/Tests/RagdollRigTests.cs docs/AI/systems/Combat.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "fix(ragdoll): joint axes follow the bone; joints rebuilt per knockdown"
```

---

### Task A3: Every bone gets the velocity, and joints are projected

Defect: `GoLimp` zeroes every bone, then hands the whole impulse (up to 48 m/s) to the hips alone. The limbs start at rest and the joints cannot catch up in one step — they separate and the skin stretches. `enableProjection` is off, so nothing pulls them back.

**Files:**
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs` (`GoLimp`, `BuildJoint`, two new serialized fields)
- Test: `RagdollRigTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
        [Test]
        public void GoLimp_HandsTheImpulseToEveryBone()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var impulse = new Vector3(12f, 3f, 0f);

            rig.GoLimp(impulse);

            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
                Assert.Less(Vector3.Distance(body.linearVelocity, impulse), 1e-3f,
                    $"{body.name} started at rest while the hips flew — the joints tear");
        }

        [Test]
        public void HardBlast_JointsStayConnected()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            rig.GoLimp(new Vector3(48f, 10f, 0f));
            StepPhysics(30);

            foreach (CharacterJoint joint in root.GetComponentsInChildren<CharacterJoint>())
            {
                Vector3 mine = joint.transform.TransformPoint(joint.anchor);
                Vector3 theirs = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
                Assert.Less(Vector3.Distance(mine, theirs), 0.05f, $"{joint.name} came apart");
                Assert.IsTrue(joint.enableProjection);
            }
        }
```

- [ ] **Step 2: Run to verify they fail**

Run: `run_tests` EditMode filter `RagdollRigTests`. Expected: both FAIL.

- [ ] **Step 3: Implement**

Serialized fields under `[Header("Joints")]`:

```csharp
        [Tooltip("How far a joint may come apart before PhysX snaps it back, metres. The last line " +
                 "against a stretched mesh when a blast is stronger than the solver can resolve.")]
        [SerializeField] private float projectionDistance = 0.05f;

        [Tooltip("How far past its limit a joint may bend before PhysX snaps it back, degrees.")]
        [SerializeField, Range(1f, 45f)] private float projectionAngle = 10f;
```

In `BuildJoint`, after `enablePreprocessing`:

```csharp
            joint.enableProjection = true;
            joint.projectionDistance = projectionDistance;
            joint.projectionAngle = projectionAngle;
```

In `GoLimp`, replace the two velocity-zeroing lines inside the bone loop with:

```csharp
                    // The whole body starts at the same speed. Giving it all to the hips left every
                    // limb at rest for the solver to accelerate in one step, which it cannot do
                    // without pulling the joints apart — the stretched mesh after every blast.
                    bone.Body.linearVelocity = settled ? Vector3.zero : impulse;
                    bone.Body.angularVelocity = Vector3.zero;
```

Delete the old `else if (Drives && impulse != Vector3.zero) bones[0].Body.AddForce(...)` branch. On a watching machine (`!Drives`) the hips are pinned kinematic, so they skip this loop already; change the loop condition so a watcher's dynamic limbs get **zero**, not the impulse (the impulse already arrives in the replicated root):

```csharp
                    bone.Body.linearVelocity = settled || !Drives ? Vector3.zero : impulse;
```

Keep the explanatory comment about watchers next to it. A second knockdown on an already-limp body must still add its impulse: after the `if (!IsLimp) { … }` block add

```csharp
            else if (Drives && !settled)
            {
                foreach (Bone bone in bones)
                    if (!bone.Body.isKinematic) bone.Body.AddForce(impulse, ForceMode.VelocityChange);
            }
```

- [ ] **Step 4: Run tests**

Run: `run_tests` EditMode filters `RagdollRigTests`, `HogtieTests`, `NetGunTests`, `SingularityTests`. Expected: PASS.

- [ ] **Step 5: Docs**

Combat.md Flows step 7: "…`rig.GoLimp(impulse)`, which hands the impulse to **every** bone (projected `CharacterJoint`s)". Gotcha: "**Never hand the impulse to one bone.** A 48 m/s velocity on the hips with every limb at rest tears the joints apart in one step; every bone starts at the same speed, and joint projection catches the rest." Symptom: `"after a blast the ragdoll's limbs stretch away from the body"`. Validator.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs Assets/Game/Editor/Tests/RagdollRigTests.cs docs/AI/systems/Combat.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "fix(ragdoll): impulse to every bone, projected joints"
```

---

### Task A4: Clamp neighbouring bone masses to a stable ratio

Defect: `minBoneMass` is a flat 0.6 kg floor, so a hand or foot on a 20–30 kg segment sits at 40–50 : 1, well past the ~10 : 1 PhysX stability limit the code itself documents.

**Files:**
- Modify: `RagdollSkeleton.cs` (add `ClampMassRatios`), `RagdollRig.cs` (serialized `maxJointMassRatio`, apply in `Build`)
- Test: `RagdollSkeletonTests.cs`

**Interfaces:**
- Produces: `RagdollSkeleton.ClampMassRatios(float[] masses, int[] parents, float maxRatio) → float[]` (parents precede children; `-1` = root).

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void ClampMassRatios_RaisesLightChildren_DownTheWholeChain()
        {
            // torso 30 → forearm 1 → hand 0.6, and a thigh of 12 straight off the torso.
            float[] masses = { 30f, 1f, 0.6f, 12f };
            int[] parents = { -1, 0, 1, 0 };

            float[] clamped = RagdollSkeleton.ClampMassRatios(masses, parents, 8f);

            Assert.AreEqual(30f, clamped[0], 1e-4f, "the root is never changed");
            Assert.AreEqual(3.75f, clamped[1], 1e-4f, "30 / 8");
            Assert.AreEqual(1f, masses[1], 1e-4f, "the input is not modified");
            Assert.AreEqual(0.6f, clamped[2], 1e-4f, "0.6 already clears its CLAMPED parent's 3.75 / 8");
            Assert.AreEqual(12f, clamped[3], 1e-4f, "already within the ratio");

            // A hand far too light for its clamped forearm is raised against the clamped value.
            float[] chain = RagdollSkeleton.ClampMassRatios(new[] { 64f, 1f, 0.1f }, new[] { -1, 0, 1 }, 8f);
            Assert.AreEqual(8f, chain[1], 1e-4f);
            Assert.AreEqual(1f, chain[2], 1e-4f, "judged against the raised forearm, not the original 1 kg");
        }
```

- [ ] **Step 2: Run to verify it fails** — `run_tests` filter `RagdollSkeletonTests`; expected compile error.

- [ ] **Step 3: Implement**

`RagdollSkeleton.cs`:

```csharp
        /// <summary>
        /// Raise every body that is too light for the body it is jointed to.
        ///
        /// <para>
        /// A joint between bodies more than about ten to one apart is the classic ragdoll
        /// explosion, and a flat per-bone floor does not prevent it: 0.6 kg is fine on a forearm and
        /// fifty to one on a torso. Walked root-first so a raised parent raises its children in turn.
        /// Children only — lowering a parent would take mass out of the body.
        /// </para>
        /// </summary>
        public static float[] ClampMassRatios(float[] masses, int[] parents, float maxRatio)
        {
            if (masses == null || parents == null) return System.Array.Empty<float>();

            var clamped = (float[])masses.Clone();
            float ratio = Mathf.Max(maxRatio, 1f);

            for (int i = 0; i < clamped.Length; i++)
            {
                int parent = parents[i];
                if (parent >= 0 && parent < i)
                    clamped[i] = Mathf.Max(clamped[i], clamped[parent] / ratio);
            }

            return clamped;
        }
```

`RagdollRig.cs`, serialized under `minBoneMass`:

```csharp
        [Tooltip("Heaviest a body may be relative to a body jointed to it. Light children are raised " +
                 "to meet it. Around ten to one is where PhysX joint chains start to explode.")]
        [SerializeField, Range(2f, 12f)] private float maxJointMassRatio = 8f;
```

At the end of `Build()`:

```csharp
            var masses = new float[bones.Count];
            var parents = new int[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                masses[i] = bones[i].Body.mass;
                parents[i] = bones[i].Parent != null
                    ? bones.FindIndex(b => b.Body == bones[i].Parent)
                    : (i == 0 ? -1 : 0);
            }

            float[] balanced = RagdollSkeleton.ClampMassRatios(masses, parents, maxJointMassRatio);
            for (int i = 0; i < bones.Count; i++) bones[i].Body.mass = balanced[i];
```

(`bones` is ordered shallowest-first, so every parent index is lower than its child's.)

- [ ] **Step 4: Run tests** — `RagdollSkeletonTests`, `RagdollRigTests`: PASS.

- [ ] **Step 5: Docs** — Combat.md, next to the `minBoneMass` wording in the "branches meet at" gotcha, add: "`maxJointMassRatio` (8) raises any body lighter than 1/8 of the body it is jointed to." Validator.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollSkeleton.cs Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs Assets/Game/Editor/Tests/RagdollSkeletonTests.cs docs/AI/systems/Combat.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "fix(ragdoll): clamp jointed mass ratios"
```

---

### Task A5: The root follows the body from physics, without teleporting the bones

Defects: `FollowHips` runs in `LateUpdate`, moves the root (which drags every bone with it) and puts back only the hips — from the *interpolated* transform, so PhysX teleports the hips backwards every frame; any branch not under the hips is shifted by the root's move; the player's interpolated root rigidbody adds a second writer. On watching machines the hips never rotate, so a client sees an upright pelvis with the body hanging off it.

New rule: while limp, **in `FixedUpdate`**, the root takes the hips' **physics** pose (position, and rotation via the hips-to-root offset captured at limp time), then every simulated bone's transform is re-seated to its own body pose. The watcher pins the hips to the root's position **and** rotation. The adapters switch the root rigidbody's interpolation off while limp.

**Files:**
- Modify: `RagdollRig.cs` (`FixedUpdate`, `FollowHips`, `PinHipsToRoot`, `LateUpdate`, `PlaceRootUnderHips`, new `hipsToRoot`)
- Modify: `AgentRagdoll.cs`, `PlayerRagdoll.cs` (`Suspend`/`Restore` save and restore `body.interpolation`)
- Test: `RagdollRigTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
        [Test]
        public void FollowHips_MovesTheRoot_WithoutMovingAnyBone()
        {
            Physics.gravity = new Vector3(0f, -18f, 0f);
            GameObject root = NewSplitRig(out RagdollRig rig, out Transform body, out Transform leg);

            rig.GoLimp(new Vector3(5f, 0f, 0f));
            StepPhysics(10);

            Rigidbody legBody = leg.GetComponent<Rigidbody>();
            Vector3 legBefore = legBody.position;

            Invoke(rig, "FollowHips");
            Physics.SyncTransforms();

            Assert.Less(Vector3.Distance(root.transform.position, body.GetComponent<Rigidbody>().position),
                        1e-3f, "the root is not where the body is");
            Assert.Less(Vector3.Distance(legBody.position, legBefore), 1e-3f,
                "moving the root teleported a bone that is not a child of the hips");
        }

        [Test]
        public void Watcher_PinsHipsRotationToo()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            Transform hips = Find(root, "Hips");

            rig.GoLimp(Vector3.zero, drives: false);
            root.transform.rotation = Quaternion.Euler(0f, 0f, 90f);   // the wire says: lying on its side

            Invoke(rig, "PinHipsToRoot");
            StepPhysics(1);

            Assert.Less(Quaternion.Angle(hips.rotation, root.transform.rotation), 1f,
                "a watcher's pelvis stays upright while the owner's body lies down");
        }
```

(`hipsToRoot` is identity on this fixture because the hips are unrotated under the root — the test pins that simple case; the offset is exercised by `PlaceRootUnderHips` on real rigs in Task B5.)

- [ ] **Step 2: Run to verify they fail** — `RagdollRigTests`: both FAIL.

- [ ] **Step 3: Implement**

`RagdollRig.cs`:

Field next to `standingHipHeight`:

```csharp
        /// <summary>
        /// The root's rotation expressed in the hips' frame, taken the moment the body went limp.
        /// While limp the root is kept at <c>hips × hipsToRoot</c>, so the replicated root carries
        /// the body's orientation to every watcher and they can reconstruct the pelvis from it.
        /// </summary>
        private Quaternion hipsToRoot = Quaternion.identity;
```

In `GoLimp`'s `if (!IsLimp)` block, after recording `PreLimpRotation`:

```csharp
                if (Hips != null) hipsToRoot = Quaternion.Inverse(Hips.rotation) * transform.rotation;
```

Replace `FixedUpdate`:

```csharp
        private void FixedUpdate()
        {
            if (!IsLimp || (sleepWhenSettled && IsSettled)) return;

            if (Drives) FollowHips();
            else PinHipsToRoot();
        }
```

Replace `PinHipsToRoot` body (keep its doc comment, extend it to mention rotation):

```csharp
        private void PinHipsToRoot()
        {
            if (Hips == null || bones.Count == 0 || bones[0].Body == null) return;

            bones[0].Body.MovePosition(transform.position);
            bones[0].Body.MoveRotation(transform.rotation * Quaternion.Inverse(hipsToRoot));
        }
```

Replace `FollowHips` (rewrite its doc comment: physics poses, not interpolated transforms; every bone re-seated, not just the hips; runs in FixedUpdate so the write lands before the solver reads it):

```csharp
        private void FollowHips()
        {
            if (Hips == null || bones.Count == 0 || bones[0].Body == null) return;

            Rigidbody hips = bones[0].Body;
            transform.SetPositionAndRotation(hips.position, hips.rotation * hipsToRoot);

            // Moving the root moved every transform under it. Put each simulated bone back where
            // its body actually is — parents first, which is the order bones are kept in — so the
            // sync into PhysX before the next step is a no-op instead of a teleport.
            foreach (Bone bone in bones)
                if (bone.Body != null)
                    bone.Transform.SetPositionAndRotation(bone.Body.position, bone.Body.rotation);
        }
```

Replace the limp branch of `LateUpdate` (the follow moved to FixedUpdate):

```csharp
            if (IsLimp)
            {
                limpSeconds += Time.deltaTime;

                bool slow = FastestLinearSpeed <= settleLinearSpeed
                            && FastestAngularSpeed <= settleAngularSpeed;
                slowSeconds = slow ? slowSeconds + Time.deltaTime : 0f;

                if (sleepWhenSettled && IsSettled) SleepBones();
                return;
            }
```

In `PlaceRootUnderHips`, replace the facing computation:

```csharp
            Vector3 facing = Vector3.ProjectOnPlane(hipRotation * hipsToRoot * Vector3.forward, Vector3.up);
            if (facing.sqrMagnitude < 1e-4f)
                facing = Vector3.ProjectOnPlane(hipRotation * hipsToRoot * Vector3.up, Vector3.up);
```

and after setting the root, re-seat every bone rather than only the hips (same loop as in `FollowHips`, reading `bone.Transform` world poses captured before the root moved). Capture them first:

```csharp
            var worldPoses = new (Vector3, Quaternion)[bones.Count];
            for (int i = 0; i < bones.Count; i++)
                worldPoses[i] = (bones[i].Transform.position, bones[i].Transform.rotation);

            transform.position = grounded;
            if (facing.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);

            for (int i = 0; i < bones.Count; i++)
                bones[i].Transform.SetPositionAndRotation(worldPoses[i].Item1, worldPoses[i].Item2);
```

(delete the old two-line `Hips.position = …; Hips.rotation = …;` restore.)

`AgentRagdoll.cs` and `PlayerRagdoll.cs`: add `private RigidbodyInterpolation bodyInterpolation;`. In `Suspend`, inside `if (body != null)`: `bodyInterpolation = body.interpolation; body.interpolation = RigidbodyInterpolation.None;` with a one-line comment ("interpolation would write a lagged root over the one the rig sets, dragging every bone with it"). In `Restore`: `if (body != null) body.interpolation = bodyInterpolation;` next to the kinematic restore.

- [ ] **Step 4: Run tests** — `RagdollRigTests`, `HogtieTests`, `NetGunTests`: PASS.

- [ ] **Step 5: Docs**

Combat.md Flows step 7: "…either drags the root after the hips (`Drives`, **in `FixedUpdate`, from the hips' physics pose; the root also takes the body's orientation**) or pins the hips' position **and rotation** to the replicated root." Persistence row "Ragdoll pose": add "the root carries the body's orientation too, so a reloaded corpse starts tilted the way it lay". Replace the parts of the FollowHips story that describe LateUpdate. Gotcha: "**Never write a limp bone from its interpolated transform.** The old LateUpdate follow wrote `Hips.position` back from the interpolated pose — a teleport into the past every frame, which is jitter and a body that never settles. Moving the root also moves every bone under it; re-seat all of them, not just the hips." Symptoms: `"a knocked-down body keeps twitching and never comes to rest"`, `"on a client a corpse's pelvis stands upright with the body hanging off it"`. Validator.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs Assets/Game/Scripts/Gameplay/Ragdoll/AgentRagdoll.cs Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs Assets/Game/Editor/Tests/RagdollRigTests.cs docs/AI/systems/Combat.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "fix(ragdoll): root follows the body from physics; watchers pin rotation"
```

---

### Task A6: Recovery restores the whole pre-knockdown pose, and leaves no interpolated bodies behind

Defects: recovery blends `localRotation` only, toward "whatever is there now" — a bone no animator writes blends from A to A and stays in the ragdoll pose forever; `localPosition` (joint stretch) is never restored. After recovery every bone keeps a kinematic, *interpolated* rigidbody that fights the Animator for the transform.

**Files:**
- Modify: `RagdollRig.cs` (`Bone`, `GoLimp`, `Recover`, new `Update`, `WriteRecoveryTarget`, `BlendRecovery(float)`, `LateUpdate`)
- Test: `RagdollRigTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void Recovery_ReturnsEveryBoneToItsPose_EvenWithNoAnimator()
        {
            Physics.gravity = new Vector3(0f, -18f, 0f);
            GameObject root = NewHumanoidRig(out RagdollRig rig);

            var before = new Dictionary<Transform, (Vector3, Quaternion)>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
                before[t] = (t.localPosition, t.localRotation);

            rig.GoLimp(new Vector3(8f, 4f, 0f));
            StepPhysics(40);
            rig.Recover();

            for (int frame = 0; frame < 20; frame++)
            {
                Invoke(rig, "WriteRecoveryTarget");
                Invoke(rig, "BlendRecovery", 0.05f);
            }

            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
            {
                Transform t = body.transform;
                if (t == root.transform) continue;

                Assert.Less(Vector3.Distance(t.localPosition, before[t].Item1), 1e-3f, $"{t.name} stayed stretched");
                Assert.Less(Quaternion.Angle(t.localRotation, before[t].Item2), 0.5f, $"{t.name} stayed bent");
                Assert.AreEqual(RigidbodyInterpolation.None, body.interpolation,
                    $"{t.name} keeps an interpolated body that fights the animator");
            }
        }
```

- [ ] **Step 2: Run to verify it fails** — `RagdollRigTests`: FAIL (missing methods, then pose assertions).

- [ ] **Step 3: Implement**

`class Bone`: replace `RecoverFrom` with

```csharp
            /// <summary>The bone's local pose the moment the body went limp — what recovery returns to.</summary>
            public Vector3 RestPosition;
            public Quaternion RestRotation;

            /// <summary>Where the bone lay when the body got up — the blend's start.</summary>
            public Vector3 RecoverFromPosition;
            public Quaternion RecoverFrom;
```

`BuildBone`: initialise `RestPosition = bone.localPosition, RestRotation = bone.localRotation` instead of `RecoverFrom`.

`GoLimp`, inside `if (!IsLimp)` before `RebuildJoints()`:

```csharp
                foreach (Bone bone in bones)
                {
                    bone.RestPosition = bone.Transform.localPosition;
                    bone.RestRotation = bone.Transform.localRotation;
                    bone.Body.interpolation = RigidbodyInterpolation.Interpolate;
                }
```

Remove `body.interpolation = RigidbodyInterpolation.Interpolate;` from `BuildBone` (bodies are born kinematic and non-interpolated).

`Recover`, in the snapshot loop:

```csharp
                bone.RecoverFrom = bone.Transform.localRotation;
                bone.RecoverFromPosition = bone.Transform.localPosition;
                bone.Body.isKinematic = true;

                // Off while the animator owns the bone. An interpolated kinematic body writes its
                // own lagged pose over the animator's every frame — a smeared, trailing skeleton on
                // anything that has ever been knocked down.
                bone.Body.interpolation = RigidbodyInterpolation.None;
```

Add, above `LateUpdate`:

```csharp
        /// <summary>
        /// Before the animator runs: put every simulated bone back to its pre-knockdown pose, so
        /// the blend's target is that pose wherever nothing animates the bone, and the animator's
        /// pose wherever something does. Rewritten every frame because the blend writes the bone
        /// too — without this, a bone no animator touches would blend from its ragdoll pose to
        /// itself and never move.
        /// </summary>
        private void Update()
        {
            if (!IsLimp && blendRemaining > 0f) WriteRecoveryTarget();
        }

        private void WriteRecoveryTarget()
        {
            foreach (Bone bone in bones)
                if (bone.Transform != null)
                    bone.Transform.SetLocalPositionAndRotation(bone.RestPosition, bone.RestRotation);
        }
```

`LateUpdate`: `if (blendRemaining > 0f) BlendRecovery(Time.deltaTime);`

`BlendRecovery(float deltaTime)`: replace `Time.deltaTime` with the parameter and blend both channels:

```csharp
                bone.Transform.localRotation =
                    Quaternion.Slerp(bone.RecoverFrom, bone.Transform.localRotation, eased);
                bone.Transform.localPosition =
                    Vector3.Lerp(bone.RecoverFromPosition, bone.Transform.localPosition, eased);
```

Update the `BlendRecovery` doc comment: positions blend too, and the target is written each frame by `Update`.

- [ ] **Step 4: Run tests** — `RagdollRigTests`, `HogtieTests`, `NetGunTests`, `BodyAttachmentTests`: PASS.

- [ ] **Step 5: Docs** — Combat.md `RagdollRig` row: "`Recover` blends every simulated bone back to its pre-knockdown pose (position and rotation)". Gotcha: "**Recovery must restore what the animator does not write.** Humanoid avatars write no bone translations and hard-surface/procedural rigs write no bone rotations; the old blend only eased rotation toward whatever was already there, so those channels stayed in the ragdoll pose forever. Bodies go non-interpolated when kinematic." Symptoms: `"a creature gets up with its limbs still bent or stretched"`, `"a body that was knocked down once looks smeared when it moves afterwards"`. Validator.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs Assets/Game/Editor/Tests/RagdollRigTests.cs docs/AI/systems/Combat.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "fix(ragdoll): recovery restores the whole pose; no interpolated kinematic bones"
```

---

### Task A7: A landed body comes to rest and sleeps

Defect the user sees: bodies on the ground keep jittering. Tasks A2–A5 remove the causes; this task adds the per-body sleep threshold and a drop test that guards the result.

**Files:**
- Modify: `RagdollRig.cs` (serialized `boneSleepThreshold`, applied in `BuildBone`)
- Test: `RagdollRigTests.cs`

- [ ] **Step 1: Write the test**

```csharp
        [Test]
        public void DroppedBody_ComesToRest_AndSleeps()
        {
            Physics.gravity = new Vector3(0f, -18f, 0f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(ground);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);

            GameObject root = NewHumanoidRig(out RagdollRig rig);
            rig.GoLimp(new Vector3(2f, 1f, 0f));

            StepPhysics(100);   // two seconds to land and stop

            float fastest = 0f;
            for (int i = 0; i < 50; i++)   // the next second must be still
            {
                StepPhysics(1);
                foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
                    if (!body.isKinematic) fastest = Mathf.Max(fastest, body.linearVelocity.magnitude);
            }

            Assert.Less(fastest, 0.05f, "a body on flat ground is still jittering after two seconds");

            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
                if (!body.isKinematic) Assert.IsTrue(body.IsSleeping(), $"{body.name} never went to sleep");
        }
```

- [ ] **Step 2: Run** — `RagdollRigTests`. If it already passes after A2–A6, still do Step 3 (the sleep threshold is what makes the last assertion reliable on real rigs with many contacts); if it fails, Step 3 is the fix.

- [ ] **Step 3: Implement**

```csharp
        [Tooltip("Energy per kg under which PhysX puts a bone to sleep. Unity's default suits loose " +
                 "props; a jointed chain keeps trading tiny corrections above it and never sleeps, " +
                 "which is the shiver of a body lying on flat ground.")]
        [SerializeField] private float boneSleepThreshold = 0.05f;
```

In `BuildBone` after `solverIterations`: `body.sleepThreshold = boneSleepThreshold;`

- [ ] **Step 4: Run tests** — `RagdollRigTests`: PASS. If `fastest` is still above 0.05, raise `angularDamping` default to 1.0 before touching anything else, and record the measured value in the commit message.

- [ ] **Step 5: Docs** — Combat.md: add `boneSleepThreshold` to the ragdoll tuning mention; gotcha line: "A jointed chain under Unity's default sleep threshold never sleeps — set per body." Validator.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs Assets/Game/Editor/Tests/RagdollRigTests.cs docs/AI/systems/Combat.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "fix(ragdoll): landed bodies come to rest and sleep"
```

---

## Phase B — One controller, event-driven durations

### Task B1: `KnockdownPolicy` — events in, seconds out

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Ragdoll/KnockdownPolicy.cs`
- Create: `Assets/Game/Editor/Tests/KnockdownPolicyTests.cs`

**Interfaces:**
- Produces:
  - `enum RagdollCause : byte { Hit = 1, Blast = 2, Fall = 3 }`
  - `readonly struct KnockdownEvent(RagdollCause cause, float damageFraction, float healthLeftFraction, float knockbackSpeed)`
  - `[Serializable] sealed class KnockdownTuning` with public fields `fallSeconds, minSeconds, maxSeconds, threshold, fullSeverity, damageWeight, lowHealthWeight, knockbackWeight, knockbackReferenceSpeed, settleGraceSeconds, reknockImmunitySeconds`
  - `static class KnockdownPolicy`: `float Severity(in KnockdownEvent, KnockdownTuning)`, `float Seconds(in KnockdownEvent, KnockdownTuning)`, `bool Immune(RagdollCause, float secondsSinceStoodUp, KnockdownTuning)`, `bool ShouldStandUp(float now, float standAt, bool atRest, float graceSeconds)`

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.EditorTools
{
    /// <summary>How long a body stays down, as a function of what put it there.</summary>
    public class KnockdownPolicyTests
    {
        private static KnockdownTuning Tuning() => new KnockdownTuning();

        [Test]
        public void Seconds_Fall_IsFlat()
        {
            var fall = new KnockdownEvent(RagdollCause.Fall, 0.5f, 0.1f, 30f);
            Assert.AreEqual(1f, KnockdownPolicy.Seconds(fall, Tuning()), 1e-4f);
        }

        [Test]
        public void Seconds_SmallHitAtFullHealth_DoesNotKnockDown()
        {
            // An assault-rifle round: 5% of health, 95% left, no knockback.
            var bullet = new KnockdownEvent(RagdollCause.Hit, 0.05f, 0.95f, 0f);
            Assert.AreEqual(0f, KnockdownPolicy.Seconds(bullet, Tuning()));
        }

        [Test]
        public void Seconds_Hit_GrowsWithDamage_LowHealth_AndKnockback()
        {
            KnockdownTuning t = Tuning();
            float moderate = KnockdownPolicy.Seconds(new KnockdownEvent(RagdollCause.Hit, 0.3f, 0.7f, 0f), t);
            float nearlyDead = KnockdownPolicy.Seconds(new KnockdownEvent(RagdollCause.Hit, 0.3f, 0.1f, 0f), t);
            float shoved = KnockdownPolicy.Seconds(new KnockdownEvent(RagdollCause.Hit, 0.3f, 0.7f, 15f), t);

            Assert.GreaterOrEqual(moderate, t.minSeconds);
            Assert.Greater(nearlyDead, moderate, "less health left must keep you down longer");
            Assert.Greater(shoved, moderate, "more knockback must keep you down longer");
        }

        [Test]
        public void Seconds_NeverExceedTheMaximum()
        {
            var brutal = new KnockdownEvent(RagdollCause.Hit, 0.9f, 0.01f, 80f);
            Assert.AreEqual(Tuning().maxSeconds, KnockdownPolicy.Seconds(brutal, Tuning()), 1e-4f);
        }

        [Test]
        public void Seconds_Blast_AlwaysKnocksDown_EvenWithNoDamage()
        {
            // The gauntlet deals no damage; its whole price is the knockdown.
            var puff = new KnockdownEvent(RagdollCause.Blast, 0f, 1f, 2f);
            Assert.AreEqual(Tuning().minSeconds, KnockdownPolicy.Seconds(puff, Tuning()), 1e-4f);
        }

        [Test]
        public void Immune_BlocksHitsJustAfterStandingUp_ButNotBlasts()
        {
            KnockdownTuning t = Tuning();
            Assert.IsTrue(KnockdownPolicy.Immune(RagdollCause.Hit, 0.2f, t));
            Assert.IsFalse(KnockdownPolicy.Immune(RagdollCause.Hit, t.reknockImmunitySeconds + 0.01f, t));
            Assert.IsFalse(KnockdownPolicy.Immune(RagdollCause.Blast, 0.2f, t));
            Assert.IsFalse(KnockdownPolicy.Immune(RagdollCause.Fall, 0.2f, t));
        }

        [Test]
        public void ShouldStandUp_WaitsForTheTime_ThenForRest_ThenForTheGrace()
        {
            Assert.IsFalse(KnockdownPolicy.ShouldStandUp(now: 0.9f, standAt: 1f, atRest: true, graceSeconds: 1.5f),
                           "not before the down-time is up, even at rest");
            Assert.IsTrue(KnockdownPolicy.ShouldStandUp(1.0f, 1f, atRest: true, graceSeconds: 1.5f));
            Assert.IsFalse(KnockdownPolicy.ShouldStandUp(2.0f, 1f, atRest: false, graceSeconds: 1.5f),
                           "still tumbling, inside the grace");
            Assert.IsTrue(KnockdownPolicy.ShouldStandUp(2.5f, 1f, atRest: false, graceSeconds: 1.5f),
                          "wedged against a rock: the grace ends it");
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `run_tests` filter `KnockdownPolicyTests`: compile error.

- [ ] **Step 3: Implement** `KnockdownPolicy.cs`:

```csharp
using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Ragdoll
{
    /// <summary>What put a body on the ground. Travels in <c>NetMsg.Knockdown</c>'s <c>B</c>.</summary>
    public enum RagdollCause : byte
    {
        /// <summary>Damage. Knocks down only past a severity threshold.</summary>
        Hit = 1,

        /// <summary>A shock wave — the gauntlet, the singularity. Always knocks down.</summary>
        Blast = 2,

        /// <summary>A landing hard enough to deal fall damage.</summary>
        Fall = 3,
    }

    /// <summary>The facts a knockdown is priced from, gathered by the machine that decides it.</summary>
    public readonly struct KnockdownEvent
    {
        public readonly RagdollCause Cause;

        /// <summary>Damage dealt as a share of max health, 0..1.</summary>
        public readonly float DamageFraction;

        /// <summary>Health left AFTER the hit as a share of max health, 0..1. 1 for a body with no health.</summary>
        public readonly float HealthLeftFraction;

        /// <summary>Speed of the impulse the event carries, m/s. 0 for a plain hit.</summary>
        public readonly float KnockbackSpeed;

        public KnockdownEvent(RagdollCause cause, float damageFraction, float healthLeftFraction,
                              float knockbackSpeed)
        {
            Cause = cause;
            DamageFraction = damageFraction;
            HealthLeftFraction = healthLeftFraction;
            KnockbackSpeed = knockbackSpeed;
        }
    }

    /// <summary>
    /// The knobs, one set per body prefab so a boss and a rat can price the same blast differently.
    /// Field initialisers are the agreed defaults (2026-09-24): fall 1 s, hits 1–2 s.
    /// </summary>
    [Serializable]
    public sealed class KnockdownTuning
    {
        [Tooltip("Seconds down after a landing that dealt fall damage.")]
        public float fallSeconds = 1f;

        [Tooltip("Seconds down for the lightest hit or blast that knocks down at all.")]
        public float minSeconds = 1f;

        [Tooltip("Seconds down for the hardest.")]
        public float maxSeconds = 2f;

        [Tooltip("Severity a HIT must reach to knock down. Below it the body flinches and keeps " +
                 "going — the reason automatic fire does not stun-lock. Blasts ignore it.")]
        public float threshold = 0.35f;

        [Tooltip("Severity at which a knockdown reaches maxSeconds.")]
        public float fullSeverity = 1f;

        [Tooltip("Severity per share of max health dealt by the hit.")]
        public float damageWeight = 1f;

        [Tooltip("Severity per share of max health MISSING after the hit. A wounded body goes down " +
                 "easier and stays down longer.")]
        public float lowHealthWeight = 0.5f;

        [Tooltip("Severity for knockback at the reference speed.")]
        public float knockbackWeight = 1f;

        [Tooltip("Knockback speed, m/s, that counts as full knockback.")]
        public float knockbackReferenceSpeed = 20f;

        [Tooltip("Once the down-time is up, how much longer to wait for a still-tumbling body to " +
                 "come to rest before standing it up anyway, seconds. The ceiling that keeps a body " +
                 "wedged against a rock from never getting up (GDC-L1-FEEL-0002).")]
        public float settleGraceSeconds = 1.5f;

        [Tooltip("Seconds after standing up during which HITS cannot knock the body down again. " +
                 "0 disables. Blasts and falls ignore it.")]
        public float reknockImmunitySeconds = 1f;
    }

    /// <summary>
    /// How long a body stays down, decided from what put it there — pure, so the numbers can be
    /// tested and tuned without a scene.
    ///
    /// <para>
    /// Down-time is committed resolution in the GDC-L1-FEEL-0008 sense: the player is heard at once
    /// (the body reacts the frame the event lands) and the world then takes a bounded, legible time
    /// to hand control back. Bounded both ways — a floor so a knockdown reads as one
    /// (GDC-L1-FEEL-0007), a ceiling so it never becomes lost control (GDC-L1-FEEL-0002).
    /// </para>
    /// </summary>
    public static class KnockdownPolicy
    {
        public static float Severity(in KnockdownEvent e, KnockdownTuning t)
        {
            float knockback = Mathf.Clamp01(e.KnockbackSpeed / Mathf.Max(t.knockbackReferenceSpeed, 0.01f));

            return t.damageWeight * Mathf.Clamp01(e.DamageFraction)
                   + t.lowHealthWeight * (1f - Mathf.Clamp01(e.HealthLeftFraction))
                   + t.knockbackWeight * knockback;
        }

        /// <summary>Seconds down, or 0 for an event that does not knock down.</summary>
        public static float Seconds(in KnockdownEvent e, KnockdownTuning t)
        {
            if (e.Cause == RagdollCause.Fall) return t.fallSeconds;

            float severity = Severity(e, t);
            if (e.Cause == RagdollCause.Hit && severity < t.threshold) return 0f;

            float full = Mathf.Max(t.fullSeverity, t.threshold + 1e-3f);
            return Mathf.Lerp(t.minSeconds, t.maxSeconds, Mathf.InverseLerp(t.threshold, full, severity));
        }

        public static bool Immune(RagdollCause cause, float secondsSinceStoodUp, KnockdownTuning t) =>
            cause == RagdollCause.Hit && secondsSinceStoodUp < t.reknockImmunitySeconds;

        public static bool ShouldStandUp(float now, float standAt, bool atRest, float graceSeconds) =>
            now >= standAt && (atRest || now >= standAt + graceSeconds);
    }
}
```

- [ ] **Step 4: Run tests** — `KnockdownPolicyTests`: PASS. (Check the arithmetic of `Seconds_Hit_GrowsWithDamage…`: moderate = 0.3 + 0.15 = 0.45 → 1.15 s; nearlyDead = 0.3 + 0.45 = 0.75 → 1.62 s; shoved = 0.45 + 0.75 = 1.2 → capped 2 s. Brutal ≥ 1 → 2 s. Puff blast = 0.1 → below threshold → InverseLerp clamps to 0 → 1 s.)

- [ ] **Step 5: Commit** (no behaviour change yet — docs come with B2)

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/KnockdownPolicy.cs Assets/Game/Scripts/Gameplay/Ragdoll/KnockdownPolicy.cs.meta Assets/Game/Editor/Tests/KnockdownPolicyTests.cs Assets/Game/Editor/Tests/KnockdownPolicyTests.cs.meta
git commit -m "feat(ragdoll): knockdown policy turns events into down-time"
```

---

### Task B2: `RagdollController` — the shared base of `AgentRagdoll` and `PlayerRagdoll`

The two adapters duplicate the holder set, `dead` flag, `downUntil`, the `Update` stand-up logic, `OnKnockdown`, `HoldDown`, `ReleaseHold`, `IsHeldOrDown` and the `OnDeath`/`OnRevive` bookkeeping. This task moves all of it into an abstract base and switches stand-up timing to `KnockdownPolicy.ShouldStandUp`. `Knockdown` now reads the duration from `A` (ms) and the cause from `B`; the adapters' `downedSeconds` fields are deleted (a knockdown always carries its duration from Task B3 on; until then a missing `A` falls back to `knockdown.minSeconds`).

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollController.cs`
- Modify: `AgentRagdoll.cs`, `PlayerRagdoll.cs`
- Modify: `RagdollRig.cs` (add `IsAtRest`)
- Test: `RagdollRigTests.cs` (controller tests live with the rig fixture), existing `HogtieTests`, `NetGunTests`

**Interfaces:**
- Consumes: `KnockdownPolicy`, `KnockdownTuning`, `RagdollCause` (B1); `RagdollRig.IsCorpse` (A1).
- Produces (all `public` on `RagdollController`): `bool IsHeld`, `bool IsHeldOrDown`, `bool CanBeKnockedDown`, `bool HoldDown(object holder)`, `void ReleaseHold(object holder)`, `KnockdownTuning Tuning`. Protected hooks: `abstract bool Drives`, `abstract Vector3 CarriedVelocity`, `abstract bool RefusesToGoDown`, `abstract Vector3 DeathImpulse()`, `abstract void SuspendLayers(bool standing)`, `abstract void RestoreLayers(in TeleportMove move)`, `virtual bool ControlsLocked`. `RagdollRig.IsAtRest` (velocity-only settle, no timeout).
- `Awake` is `protected virtual` in the base and `protected override` in each adapter (the tests' `Woken<T>` helper reflects `Awake` on the concrete type — a `private` base `Awake` would not be found).

- [ ] **Step 1: Write the failing tests** (add to `RagdollRigTests`)

```csharp
        private static void Knock(Component body, float seconds, RagdollCause cause = RagdollCause.Blast)
        {
            var arg = new SpaceGame.Core.NetArg { P = Vector3.zero, A = Mathf.RoundToInt(seconds * 1000f), B = (int)cause };
            Invoke(body, "OnKnockdown", arg, 0UL);
        }

        [Test]
        public void Controller_StandsUpOnlyAfterTheEventsDuration()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var ragdoll = root.AddComponent<AgentRagdoll>();
            Invoke(ragdoll, "Awake");

            Knock(ragdoll, 2f);
            Assert.IsTrue(rig.IsLimp);

            Invoke(ragdoll, "TickStandUp", Time.time + 1.9f);
            Assert.IsTrue(rig.IsLimp, "stood up before the 2 s the event asked for");

            Invoke(ragdoll, "TickStandUp", Time.time + 2f + ragdoll.Tuning.settleGraceSeconds + 0.01f);
            Assert.IsFalse(rig.IsLimp, "never stood up");
        }

        [Test]
        public void Knockdown_RefusedWhileRiding()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var ragdoll = root.AddComponent<RidingTestRagdoll>();
            Invoke(ragdoll, "Awake");

            Knock(ragdoll, 1f);

            Assert.IsFalse(rig.IsLimp, "a rider went limp in the saddle and will be dragged through the ground");
        }

        /// <summary>A controller that answers "someone is carrying me" — the saddle/seat case.</summary>
        private sealed class RidingTestRagdoll : AgentRagdoll
        {
            protected override bool RefusesToGoDown => true;
        }
```

(`AgentRagdoll` must therefore not be `sealed`, and `RefusesToGoDown` must be `protected virtual`/`override`-able — see Step 3. `TickStandUp(float now)` is the time-injected body of `Update`.)

- [ ] **Step 2: Run to verify they fail** — `RagdollRigTests`: compile errors.

- [ ] **Step 3: Implement**

`RagdollRig.cs`, next to `IsSettled`:

```csharp
        /// <summary>
        /// Has the body actually stopped moving — the velocity half of <see cref="IsSettled"/> with no
        /// timeout. The controller supplies its own ceiling (KnockdownTuning.settleGraceSeconds).
        /// </summary>
        public bool IsAtRest =>
            !IsLimp || RagdollSkeleton.IsSettled(FastestLinearSpeed, FastestAngularSpeed, slowSeconds,
                                                 settleLinearSpeed, settleAngularSpeed, settleSeconds);
```

Update `maxLimpSeconds`'s tooltip: it now only bounds how long a *corpse* keeps simulating before it is put to sleep; knockdown timing belongs to `KnockdownTuning`.

`RagdollController.cs`:

```csharp
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.Teleporting;
using UnityEngine;

namespace SpaceGame.Gameplay.Ragdoll
{
    /// <summary>
    /// Decides WHEN a body goes limp and for how long, for any body — creature or player.
    ///
    /// <para>
    /// Everything here used to exist twice, once in <see cref="AgentRagdoll"/> and once in
    /// <see cref="PlayerRagdoll"/>, and the two copies had started to drift (only one refused a
    /// knockdown on a carried body). What differs between them is WHICH layers own the transform
    /// and must be switched off — a creature's brain, motor and legs; a player's input, look and
    /// camera — and that is all the subclasses supply.
    /// </para>
    ///
    /// <para>
    /// Down-time comes from the event, not from the body: <see cref="KnockdownPolicy"/> prices it on
    /// the deciding machine and <c>NetMsg.Knockdown</c> carries it to everyone, so every machine
    /// stands the body up together. Death has no down-time: a corpse stays limp until it despawns.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    public abstract class RagdollController : MonoBehaviour
    {
        [SerializeField] private KnockdownTuning knockdown = new KnockdownTuning();

        protected RagdollRig rig;
        protected HealthComponent health;

        private readonly HashSet<object> holders = new HashSet<object>();
        private bool suspended;
        private bool dead;
        private float standAt;
        private float stoodUpAt = float.NegativeInfinity;

        public KnockdownTuning Tuning => knockdown;

        /// <summary>Is something holding this body down right now?</summary>
        public bool IsHeld => holders.Count > 0;

        /// <summary>
        /// On the ground by any route — a hold or a knockdown. Deliberately broader than
        /// <see cref="IsHeld"/>: a body knocked flat is just as tieable as a netted one.
        /// </summary>
        public bool IsHeldOrDown => IsHeld || (rig != null && rig.IsLimp);

        /// <summary>Can this body be put on the ground right now?</summary>
        public bool CanBeKnockedDown => isActiveAndEnabled && !RefusesToGoDown;

        protected bool IsDead => dead;
        protected bool IsSuspended => suspended;

        // ── What the subclass supplies ────────────────────────────────────────

        /// <summary>Does THIS machine decide where the body ends up? See <see cref="RagdollRig.Drives"/>.</summary>
        protected abstract bool Drives { get; }

        /// <summary>How fast the body was already moving. Read BEFORE suspending.</summary>
        protected abstract Vector3 CarriedVelocity { get; }

        /// <summary>Is the body somebody else's to move — a rider in a saddle or a seat?</summary>
        protected abstract bool RefusesToGoDown { get; }

        /// <summary>The velocity a killing blow hands the body.</summary>
        protected abstract Vector3 DeathImpulse();

        /// <summary>Stop every layer that writes the transform or the bones. Called once per suspension.</summary>
        protected abstract void SuspendLayers(bool standing);

        /// <summary>Hand the body back to those layers, at the place it came to rest.</summary>
        protected abstract void RestoreLayers(in TeleportMove move);

        /// <summary>Something outside this component that forbids standing up — a player's death screen.</summary>
        protected virtual bool ControlsLocked => false;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        protected virtual void Awake()
        {
            rig = GetComponent<RagdollRig>();
            health = GetComponent<HealthComponent>();
        }

        protected virtual void OnEnable()
        {
            this.NetOn(NetMsg.Knockdown, OnKnockdown);
            if (health != null) health.OnDeath += OnDeath;
            if (health != null) health.OnRevive += OnRevive;
        }

        protected virtual void OnDisable()
        {
            this.NetOff(NetMsg.Knockdown, OnKnockdown);
            if (health != null) health.OnDeath -= OnDeath;
            if (health != null) health.OnRevive -= OnRevive;
        }

        private void Update() => TickStandUp(Time.time);

        /// <summary>The stand-up decision, with the clock passed in so it can be tested.</summary>
        private void TickStandUp(float now)
        {
            if (!suspended || dead || ControlsLocked || IsHeld) return;

            // The budget took the body off physics (it only does that to corpses, so this is the
            // belt to its braces). Nothing will come to rest or say so — take the body back now.
            if (!rig.IsLimp)
            {
                Restore();
                return;
            }

            if (KnockdownPolicy.ShouldStandUp(now, standAt, rig.IsAtRest, knockdown.settleGraceSeconds))
                Restore();
        }

        // ── What starts it ────────────────────────────────────────────────────

        private void OnDeath()
        {
            dead = true;

            // Death ends every hold at once; the corpse stays limp but stops being a captive the
            // budget may not reclaim.
            holders.Clear();
            rig.BudgetExempt = false;
            rig.IsCorpse = true;

            // A save being loaded, or a remote death arriving through RestoreHealth: lie down where
            // it is, without being thrown again.
            bool restoring = health != null && health.IsRestoring;
            Vector3 carried = restoring ? Vector3.zero : CarriedVelocity;

            Suspend(standing: false);
            rig.GoLimp(restoring ? Vector3.zero : DeathImpulse() + carried,
                       settled: restoring, drives: Drives);
        }

        private void OnRevive()
        {
            dead = false;
            holders.Clear();
            rig.BudgetExempt = false;
            rig.IsCorpse = false;

            if (rig.IsLimp) Restore();
        }

        /// <summary>
        /// Every machine: go limp for <c>A</c> ms, thrown at <c>P</c>. <c>B</c> is the
        /// <see cref="RagdollCause"/>, for diagnostics and immunity bookkeeping.
        /// </summary>
        private void OnKnockdown(in NetArg arg, ulong sender)
        {
            if (dead || RefusesToGoDown) return;

            Vector3 carried = CarriedVelocity;

            Suspend(standing: false);
            rig.GoLimp(arg.P + carried, settled: false, drives: Drives);

            float seconds = arg.A > 0 ? arg.A / 1000f : knockdown.minSeconds;
            standAt = Mathf.Max(standAt, Time.time + seconds);
        }

        // ── Holds ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Go limp and stay limp until every holder lets go. True once the body is limp and held;
        /// false means the hold did not take and the caller must not treat the body as held.
        /// </summary>
        public bool HoldDown(object holder)
        {
            if (holder == null || dead) return false;

            if (IsHeld)
            {
                holders.Add(holder);
                return true;
            }

            if (!CanBeKnockedDown) return false;

            holders.Add(holder);
            Vector3 carried = CarriedVelocity;

            Suspend(standing: false);
            rig.BudgetExempt = true;
            rig.GoLimp(carried, settled: false, drives: Drives);

            if (rig.IsLimp) return true;

            holders.Remove(holder);
            rig.BudgetExempt = false;
            Restore();
            return false;
        }

        /// <summary>Claim the body WITHOUT laying it down. See <c>PlayerRagdoll.HoldStanding</c>.</summary>
        protected bool HoldStandingClaim(object holder)
        {
            if (holder == null || dead) return false;

            if (IsHeld)
            {
                holders.Add(holder);
                return true;
            }

            if (RefusesToGoDown) return false;

            holders.Add(holder);
            Suspend(standing: true);
            return true;
        }

        /// <summary>Give up one claim. The body gets up once the LAST claim is given up.</summary>
        public void ReleaseHold(object holder)
        {
            if (holder == null || !holders.Remove(holder)) return;
            if (IsHeld) return;

            rig.BudgetExempt = false;

            // Update owns the recovery, so a body released mid-tumble does not snap upright.
            standAt = 0f;
        }

        // ── Handing the body over and back ────────────────────────────────────

        private void Suspend(bool standing)
        {
            if (suspended) return;
            suspended = true;
            SuspendLayers(standing);
        }

        private void Restore()
        {
            if (!suspended) return;
            suspended = false;
            standAt = 0f;
            stoodUpAt = Time.time;

            TeleportMove move = rig.Recover();
            RestoreLayers(move);
        }
    }
}
```

`stoodUpAt` is read by Task B3; if the implementer finds the compiler warns it is unused in B2, add B3's `KnockHere` in the same session rather than suppressing the warning.

`AgentRagdoll.cs` — rewrite as a subclass that keeps only creature layers. Keep the class-level doc comment. Remove: `health`, `rig`, `holders`, `IsHeld`, `IsHeldOrDown`, `dead`, `downUntil`, `downedSeconds`, `suspended`, `OnEnable`/`OnDisable`, `OnDeath`, `OnRevive`, `OnKnockdown`, `HoldDown`, `ReleaseHold`, `Update`, `CanBeKnockedDown`. The class becomes:

```csharp
    public class AgentRagdoll : RagdollController
    {
        [Tooltip(/* keep the existing deathImpulse tooltip */)]
        [SerializeField] private float deathImpulse = 2.5f;

        [Tooltip(/* keep the existing deathImpulseLift tooltip */)]
        [SerializeField, Range(0f, 1f)] private float deathImpulseLift = 0.35f;

        private AgentController agentController;
        private LeggedLocomotion locomotion;
        private NavMeshAgent navAgent;
        private Rigidbody body;
        private Collider bodyCollider;
        private MountModule mount;
        private NpcPassenger passenger;

        private bool bodyWasKinematic;
        private RigidbodyInterpolation bodyInterpolation;
        private bool controllerWasEnabled;
        private bool locomotionWasEnabled;
        private bool colliderWasEnabled;

        // keep the SelfDrivingMotor property and its doc comment verbatim

        protected override void Awake()
        {
            base.Awake();
            agentController = GetComponent<AgentController>();
            locomotion = GetComponentInChildren<LeggedLocomotion>(true);
            navAgent = GetComponentInChildren<NavMeshAgent>(true);
            body = GetComponent<Rigidbody>();
            bodyCollider = GetComponent<Collider>();
            mount = GetComponentInChildren<MountModule>(true);
            passenger = GetComponentInChildren<NpcPassenger>(true);
        }

        protected override bool Drives => Network.Simulates(this);

        protected override Vector3 CarriedVelocity =>
            agentController != null && agentController.Motor != null
                ? agentController.Motor.Velocity
                : Vector3.zero;

        // keep the HasRider doc comment on this property
        protected override bool RefusesToGoDown => (mount != null && mount.IsMounted)
                                                   || (passenger != null && passenger.HasRider);

        protected override Vector3 DeathImpulse() { /* body of the existing DeathImpulse, unchanged */ }

        protected override void SuspendLayers(bool standing) { /* body of the existing Suspend minus the `suspended` guard, plus the interpolation save from Task A5 */ }

        protected override void RestoreLayers(in TeleportMove move) { /* body of the existing Restore from "if (bodyCollider != null && colliderWasEnabled)" onward, using `move` */ }

        // keep RaiseTeleported verbatim
    }
```

(The `/* … */` markers above point at code that already exists in the file and moves unchanged; copy it, do not paraphrase it.) Because `RefusesToGoDown` is overridden, change its modifier in `AgentRagdoll` to `protected override bool RefusesToGoDown` (not sealed) so the test's `RidingTestRagdoll` can override again.

`HoldDown`'s doc paragraph about `SnareTether.Bind` falling back to capping NavMeshAgent speed moves to the base `HoldDown` doc.

`PlayerRagdoll.cs` — the same reduction. Keep: camera fields/`LateUpdate`/`DetachCamera`/`AttachCamera`, `deathLift`, `HoldStanding` (now `public bool HoldStanding(object holder) => HoldStandingClaim(holder);` with its existing doc comment), `IsCarried`. Overrides:

```csharp
        protected override bool Drives => Network.Owns(this);
        protected override Vector3 CarriedVelocity =>
            body != null && !body.isKinematic ? body.linearVelocity : Vector3.zero;
        protected override bool RefusesToGoDown => CarriedBody.IsCarriedRigidly(gameObject);
        protected override Vector3 DeathImpulse() => Vector3.up * deathLift;
        protected override bool ControlsLocked => controller != null && controller.IsDead;
        protected override void SuspendLayers(bool standing) { /* existing Suspend body minus the guard */ }
        protected override void RestoreLayers(in TeleportMove move) { /* existing Restore body minus the guard and the rig.Recover() call */ }
```

Note the one behaviour change: a player seated or in a saddle now refuses *knockdowns* too, not only holds. Record it in the docs step. `PlayerRagdoll.Awake` keeps its missing-HealthComponent warning after `base.Awake()`.

`HealthComponent` lives in `SpaceGame.Core`? — check its namespace with `grep -n namespace Assets/Game/Scripts/Gameplay/Health/HealthComponent.cs` and add the matching `using` to `RagdollController.cs`.

Callers keep compiling: `BlastPush` (`AgentRagdoll.CanBeKnockedDown`), `Hogtie` (`IsHeldOrDown`), `InputRestoreGuard` (`IsHeldOrDown`), `SnaredBody`/`SnareTether`/`BodyHold` (`HoldDown`, `HoldStanding`, `ReleaseHold`) — all now inherited public members.

- [ ] **Step 4: Run tests** — `RagdollRigTests`, `KnockdownPolicyTests`, `HogtieTests`, `NetGunTests`, `BodyAttachmentTests`, `SingularityTests`, `LeashConstraintTests`: PASS. `NetGunTests.cs:3421` greps source for `"public bool HoldDown(object holder)"` in a specific file — point it at `RagdollController.cs` if it fails and keep what it asserts.

- [ ] **Step 5: Docs**

Combat.md: key-types row `AgentRagdoll` / `PlayerRagdoll` → two rows: `RagdollController` ("abstract base: when to go limp, for how long (`KnockdownTuning`), holds, stand-up") and `AgentRagdoll` / `PlayerRagdoll` ("which layers own the transform and how to hand them back"). Flows step 7 names `RagdollController`. Rewrite the "frozen out from under you" gotcha to point at `RagdollController.TickStandUp`. Add a "Knockdown duration" subsection under Flows: the table from this plan's Decisions section, "priced on the deciding machine, carried in `NetMsg.Knockdown` (`A` = ms, `B` = cause)". Gotcha: "**Seated or saddled players refuse knockdowns** (`RefusesToGoDown`), not only holds." Update `.claude/skills/spacegame-tribe/SKILL.md` or `spacegame-agent/SKILL.md` only if they name `AgentRagdoll` fields that were removed (grep `downedSeconds`). Bump `updated:`, validator.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/ Assets/Game/Editor/Tests/RagdollRigTests.cs Assets/Game/Editor/Tests/NetGunTests.cs docs/AI/systems/Combat.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "refactor(ragdoll): one RagdollController behind both adapters"
```

---

### Task B3: Blasts and hits are priced by the policy on the server

**Files:**
- Modify: `RagdollController.cs` (static `Knock`, `KnockHere`, `OnDamaged`, serialized `hitImpulse`)
- Modify: `NetMsg.cs` (document `B` on `Knockdown`)
- Modify: `RepulsorGauntletArtifact.cs`, `SingularityWell.cs` (`Knock` bodies; delete `downedSeconds`)
- Test: `RagdollRigTests.cs`

**Interfaces:**
- Produces: `public static void RagdollController.Knock(GameObject victim, RagdollCause cause, Vector3 impulse, float damageFraction = 0f)` — no-op off the deciding machine.

- [ ] **Step 1: Write the failing tests**

```csharp
        [Test]
        public void SmallHit_DoesNotKnockDown_BigHitDoes()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var health = root.AddComponent<HealthComponent>();
            Invoke(health, "Awake");
            var ragdoll = root.AddComponent<AgentRagdoll>();
            Invoke(ragdoll, "Awake");
            Invoke(ragdoll, "OnEnable");

            health.Damage(Mathf.RoundToInt(health.GetMaxHealth * 0.05f), null);
            Assert.IsFalse(rig.IsLimp, "a 5% hit knocked the body down — automatic fire will stun-lock");

            health.Damage(Mathf.RoundToInt(health.GetMaxHealth * 0.4f), null);
            Assert.IsTrue(rig.IsLimp, "a 40% hit on a wounded body did not knock it down");
        }
```

(Offline in EditMode, `Network.Decides` is true and `NetSendTo(…, NetTo.All)` dispatches locally, so the full server path runs. If `HealthComponent` has no `Awake`, drop that `Invoke` line; check with `grep -n "void Awake" Assets/Game/Scripts/Gameplay/Health/HealthComponent.cs`. If the local `NetChannel` needs a relay component to dispatch offline, follow how `HogtieTests`/`NetGunTests` set one up — grep them for `NetChannel`.)

- [ ] **Step 2: Run to verify it fails** — the big-hit assertion FAILS.

- [ ] **Step 3: Implement**

`RagdollController.cs`:

```csharp
        [Tooltip("Speed a knockdown-worthy HIT throws the body at, m/s, away from the attacker. The " +
                 "hit carries no knockback of its own, so this is the fall's shape, not its price.")]
        [SerializeField] private float hitImpulse = 3f;
```

Subscribe in `OnEnable`/`OnDisable`: `health.OnDamage += OnDamaged;` / `-=`.

```csharp
        /// <summary>
        /// The deciding machine only: price a knockdown and tell every machine. The single door every
        /// knockdown source goes through — a blast, a hit, a fall.
        /// </summary>
        public static void Knock(GameObject victim, RagdollCause cause, Vector3 impulse,
                                 float damageFraction = 0f)
        {
            if (!Network.Decides || victim == null) return;

            RagdollController ragdoll = victim.GetComponentInParent<RagdollController>();
            if (ragdoll != null) ragdoll.KnockHere(cause, impulse, damageFraction);
        }

        private void KnockHere(RagdollCause cause, Vector3 impulse, float damageFraction)
        {
            if (dead || !CanBeKnockedDown) return;
            if (!rig.IsLimp && KnockdownPolicy.Immune(cause, Time.time - stoodUpAt, knockdown)) return;

            float healthLeft = health != null && health.GetMaxHealth > 0
                ? (float)health.GetHealth / health.GetMaxHealth
                : 1f;

            float seconds = KnockdownPolicy.Seconds(
                new KnockdownEvent(cause, damageFraction, healthLeft, impulse.magnitude), knockdown);
            if (seconds <= 0f) return;

            NetMessaging.NetSendTo(gameObject, NetMsg.Knockdown, new NetArg
            {
                P = impulse,
                A = Mathf.RoundToInt(seconds * 1000f),
                B = (int)cause,
            }, NetTo.All);
        }

        /// <summary>
        /// Damage lands only where it is decided (the server, or offline), so this runs there and
        /// nowhere else. A killing blow is death's business, and a save restoring health is not a hit.
        /// </summary>
        private void OnDamaged(int amount)
        {
            if (!Network.Decides || health == null || health.IsRestoring) return;
            if (health.GetHealth <= 0 || health.GetMaxHealth <= 0) return;

            Transform source = health.LastDamageSource;
            Vector3 away = source != null
                ? Vector3.ProjectOnPlane(transform.position - source.position, Vector3.up).normalized
                : -transform.forward;

            Knock(gameObject, RagdollCause.Hit, away * hitImpulse, (float)amount / health.GetMaxHealth);
        }
```

Note: the knockback term for a plain hit reads `impulse.magnitude` = `hitImpulse` (3 m/s → 0.15 severity at defaults). That is intentional: it is the hit's own small shove. Adjust `Seconds_*` expectations nowhere — B1 tests the policy with explicit events.

`RepulsorGauntletArtifact.cs`: delete the `downedSeconds` field and its tooltip; replace the body of `Knock`:

```csharp
        private void Knock(GameObject victim, Vector3 fling) =>
            RagdollController.Knock(victim, RagdollCause.Blast, fling, (float)blastDamage / 100f);
```

— **check first** what `blastDamage` is relative to (it is an absolute amount; if the victim's max health is needed, pass `0f` instead and let `OnDamaged` price the damage separately; the two knockdowns merge by `Mathf.Max` in `OnKnockdown`). Prefer `0f` here. Update the class doc and the `Knock` doc comment ("duration priced by the victim's `KnockdownTuning`"). `SingularityWell.cs`: same change, `downedSeconds` deleted, its `[Tooltip]` text about "so every machine agrees when it ends" moves into the `Knock` doc.

`NetMsg.cs` `Knockdown` comment: add "`A` = down-time in ms, priced by `KnockdownPolicy` on the deciding machine; `B` = `RagdollCause`. Send through `RagdollController.Knock`, never directly."

The Unity serializer drops the deleted fields from prefabs on next save — no migration needed. Grep prefabs for `downedSeconds` and note in the commit message which prefabs carried overridden values (those tunings are now per-victim in `KnockdownTuning`).

- [ ] **Step 4: Run tests** — all suites listed in Global Constraints plus `RagdollRigTests`, `KnockdownPolicyTests`: PASS.

- [ ] **Step 5: Docs** — Combat.md: Flows "Knockdown duration" subsection: "Every source calls `RagdollController.Knock(victim, cause, impulse)` on the deciding machine; hits are priced from `HealthComponent.OnDamage` there." Extending section: "To make a new effect knock bodies down, call `RagdollController.Knock` — do not send `NetMsg.Knockdown` yourself." Gotcha: "A hit that is also a blast produces two knockdowns; `OnKnockdown` keeps the later stand-up time." Update `Artifacts.md` if it documents the gauntlet's/singularity's `downedSeconds` (grep). Validator.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollController.cs Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs Assets/Game/Scripts/Items/Artifacts/BottledSingularity/SingularityWell.cs Assets/Game/Editor/Tests/RagdollRigTests.cs docs/AI/systems/Combat.md docs/AI/systems/Artifacts.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "feat(ragdoll): hits and blasts priced by the knockdown policy on the server"
```

---

### Task B4: A damaging fall knocks the player down for a second

A fall is measured by its owner (`PlayerMovement.HandleFallDamage`), but only the server may broadcast. The owner sends `NetMsg.KnockdownRequest` to the server; the server checks the sender speaks for the body and runs `Knock(Fall)`.

**Files:**
- Modify: `NetMsg.cs` (add `KnockdownRequest = 116` — confirm 116 is unused: `grep -n "= 116" Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs` must print nothing)
- Modify: `RagdollController.cs` (`RequestFallKnockdown`, `OnKnockdownRequest`)
- Modify: `Assets/Game/Scripts/Characters/Player/Movement/Movement.cs` (`ApplyFallDamage`)
- Test: `RagdollRigTests.cs`

**Interfaces:**
- Produces: `public static void RagdollController.RequestFallKnockdown(Component body)`.

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void FallRequest_KnocksDownForTheFallTime()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var ragdoll = root.AddComponent<AgentRagdoll>();
            Invoke(ragdoll, "Awake");
            Invoke(ragdoll, "OnEnable");

            RagdollController.RequestFallKnockdown(ragdoll);

            Assert.IsTrue(rig.IsLimp);
            Invoke(ragdoll, "TickStandUp", Time.time + ragdoll.Tuning.fallSeconds - 0.05f);
            Assert.IsTrue(rig.IsLimp, "stood up before the fall's second was up");
        }
```

- [ ] **Step 2: Run to verify it fails** — compile error.

- [ ] **Step 3: Implement**

`NetMsg.cs`, after `Emote`:

```csharp
        // Owner → server, on the VICTIM's relay: "my own landing was hard enough to knock me down".
        // A = RagdollCause. The server checks Network.MayActFor and prices it through
        // RagdollController.Knock, which broadcasts Knockdown (82). A client cannot broadcast, and a
        // fall is only ever measured by the machine that owns the body — hence the round trip.
        public const ushort KnockdownRequest = 116; // owner → server, on the VICTIM's relay
```

`RagdollController.cs` — subscribe in `OnEnable`/`OnDisable`: `this.NetOn(NetMsg.KnockdownRequest, OnKnockdownRequest);` / `NetOff`.

```csharp
        /// <summary>
        /// The owner's half of a fall knockdown: ask the server. Offline this dispatches locally
        /// and lands in <see cref="OnKnockdownRequest"/> at once.
        /// </summary>
        public static void RequestFallKnockdown(Component body)
        {
            if (body == null) return;

            NetMessaging.NetSendTo(body.gameObject, NetMsg.KnockdownRequest,
                                   new NetArg { A = (int)RagdollCause.Fall }, NetTo.Server);
        }

        private void OnKnockdownRequest(in NetArg arg, ulong sender)
        {
            // Only falls may be requested: a client naming any other cause is asking to knock a body
            // down on its own authority.
            if ((RagdollCause)arg.A != RagdollCause.Fall) return;
            if (!Network.MayActFor(gameObject, sender)) return;

            Knock(gameObject, RagdollCause.Fall, Vector3.zero);
        }
```

`Movement.cs` `ApplyFallDamage`: after `NetDamage.Apply(...)` add

```csharp
                // The landing itself knocks the player flat for a moment, on top of whatever the
                // damage prices as a hit — the two merge on the longer stand-up.
                SpaceGame.Gameplay.Ragdoll.RagdollController.RequestFallKnockdown(this);
```

(Use a `using SpaceGame.Gameplay.Ragdoll;` at the top instead of the qualified name if the file has no conflicting names.)

- [ ] **Step 4: Run tests** — `RagdollRigTests` and the Global Constraints suites: PASS.

- [ ] **Step 5: Docs** — Combat.md "Knockdown duration": the fall row names `NetMsg.KnockdownRequest` and `Network.MayActFor`. `docs/AI/systems/Multiplayer.md`: if it keeps a message table, add `KnockdownRequest (116)`. `PlayerCharacter.md` (grep `HandleFallDamage`): one line saying a damaging landing also requests a knockdown. Validator.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs Assets/Game/Scripts/Gameplay/Ragdoll/RagdollController.cs Assets/Game/Scripts/Characters/Player/Movement/Movement.cs Assets/Game/Editor/Tests/RagdollRigTests.cs docs/AI/systems/Combat.md docs/AI/systems/Multiplayer.md docs/AI/systems/PlayerCharacter.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "feat(ragdoll): damaging falls knock the player down"
```

---

### Task B5: Verify on host, client and reload

No code unless something fails. CLAUDE.md: a feature seen only on the host is not finished; state must survive save/quit/load.

- [ ] **Step 1: Audit the wired prefabs** — run the `Diagnose Wired Prefabs` menu item (`RagdollWiring`) through Unity MCP `execute_menu_item`; every rig should report joints > 0 and `unfiltered: 0`. Record any rig whose bone count dropped compared to before this plan.
- [ ] **Step 2: Host + client session** (MPPM or a build + editor, per the `spacegame-multiplayer` skill). On each machine, watch a Clanker, a Nomad, the ostrich and another player:
  - gauntlet blast → ~1–2 s down, limbs attached, body still on the ground within ~1.5 s, same place and orientation on both screens (Review Focus 1);
  - rifle fire into a creature → small hits never knock down; a big hit does; no re-knock within 1 s of standing (Review Focus 3);
  - jump from a height that deals fall damage → 1 s down, visible to the other player;
  - blast a player sitting in a sky vessel seat → refused (Review Focus 4);
  - equip/strip the gauntlet between two knockdowns → no exception (Review Focus 5);
  - kill 13+ creatures in one fight → no living creature ever frozen in a ragdoll pose.
- [ ] **Step 3: Save/quit/load** with one corpse on a slope and one knocked-down body mid-fall. Reload: the corpse lies where it lay (not standing, not thrown, not under terrain); open the save JSON and confirm the corpse's `TransformSaveable` rotation is the tilted one (Review Focus 2). A body that was merely knocked down reloads standing — knockdowns hold no state worth persisting (the down-time is a few seconds and the body's position is already saved by its transform); say so in Combat.md's Persistence table.
- [ ] **Step 4: Record the results** in Combat.md (`updated:`, remove any gotcha this plan made untrue), run `python3 tools/docs_check.py --index`, commit docs only:

```bash
git add docs/AI/systems/Combat.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "docs(ragdoll): verified on host, client and reload"
```

---

## Phase C — Nets as a flat 3 s knockdown (separate plan)

The user chose "flat 3 s, no struggle" for nets. The net gun is its own subsystem (~5 000 lines across `SnareCatch`, `SnareStruggle*`, `SnaredBody`, `SnareTether`, `SnareReceiver`, plus 4 400 lines of `NetGunTests`), governed by `docs/AI/systems/Artifacts.md` and shared with `Hogtie` through `HoldDown`. Removing the struggle is a design change to that system, not a ragdoll fix, so it gets its own plan once Phase B has landed — written against the then-current `RagdollController`, where it will add `RagdollCause.Net` with `netSeconds = 3` to `KnockdownTuning` and replace the snare's `HoldDown` with `RagdollController.Knock(victim, RagdollCause.Net, …)`. That plan also decides what happens to the net mesh and tether after the 3 s, and whether `BudgetExempt` can then be retired (A1 made it redundant for everything except holds; after nets, only `Hogtie` holds remain).
