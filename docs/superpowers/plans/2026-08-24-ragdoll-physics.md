# Ragdoll Physics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bodies go limp when they die and when the repulsor gauntlet's shock wave catches them — living victims tumble and get back up, dead ones stay down until the existing despawn timer takes them.

**Architecture:** One rig-agnostic runtime builder (`RagdollRig`) generates capsules and `CharacterJoint`s by walking any `Animator`'s bone hierarchy, backed by a pure-math helper (`RagdollSkeleton`) that decides which bones matter. Two thin adapters (`AgentRagdoll`, `PlayerRagdoll`) suspend the layers that own each body's transform and hand control back on recovery. Ragdoll runs on every machine; position converges through the authority split the codebase already uses.

**Tech Stack:** Unity 6, Netcode for GameObjects, Assembly-CSharp (no asmdef — the adapters reference `AgentController` and `PlayerMovement`, and an asmdef cannot reference Assembly-CSharp). Tests are NUnit edit-mode in `Assets/Game/Editor/Tests/` (Assembly-CSharp-Editor), where `RepulsorBlastMathTests` already lives.

**Spec:** `docs/superpowers/specs/2026-08-24-ragdoll-physics-design.md`

---

## File Structure

| File | Responsibility |
| --- | --- |
| `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollSkeleton.cs` | **Create.** Pure math: bone selection, capsule sizing, mass split, settle predicate. No scene state. |
| `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs` | **Create.** Builds and owns the physical skeleton. `GoLimp` / `Recover` / root-follow / pose blend. |
| `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollBudget.cs` | **Create.** Caps concurrent limp bodies; freezes the oldest past the cap. |
| `Assets/Game/Scripts/Gameplay/Ragdoll/AgentRagdoll.cs` | **Create.** Agent adapter: suspends `AgentController` / motor / `LeggedLocomotion` / `Animator`, rebases via `ITeleportAware` on recovery. |
| `Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs` | **Create.** Player adapter: suspends `Input` / `PlayerMovement` / `PlayerLook`, moves the camera off the head. |
| `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs` | **Modify.** Add `NetMsg.Knockdown = 82`. |
| `Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs` | **Modify.** Send `Knockdown` alongside `Flung`; ragdoll agents that cannot leap; skip mounted creatures. |
| `Assets/Game/Scripts/Characters/Player/Core/PlayerController.cs` | **Modify.** Replace `// TODO: ragdoll` with the real call. |
| `Assets/Game/Scripts/Characters/Player/Movement/PlayerLook.cs` | **Modify.** Add `SetFirstPersonHidden(bool)` so a ragdolled player is not headless in their own third-person view. |
| `Assets/Game/Scripts/agents/Entity/HealthReactionModule.cs` | **Modify.** Correct the header comment; death ragdoll is `AgentRagdoll`'s own subscription. |
| `Assets/Game/Editor/Tests/RagdollSkeletonTests.cs` | **Create.** Edit-mode tests for the pure math. |

---

## Task 1: `RagdollSkeleton` — the pure math

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollSkeleton.cs`
- Test: `Assets/Game/Editor/Tests/RagdollSkeletonTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using SpaceGame.Gameplay.Ragdoll;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class RagdollSkeletonTests
    {
        [Test]
        public void SelectBones_KeepsHeavyBones_DropsFingers()
        {
            // A spine carrying a third of the mesh, a hand carrying a fortieth.
            float[] weights = { 300f, 250f, 200f, 8f, 6f };
            bool[] keep = RagdollSkeleton.SelectBones(weights, 0.02f);

            Assert.IsTrue(keep[0]);
            Assert.IsTrue(keep[1]);
            Assert.IsTrue(keep[2]);
            Assert.IsFalse(keep[3], "a bone under the weight floor is not worth a joint");
            Assert.IsFalse(keep[4]);
        }

        [Test]
        public void SelectBones_EmptyOrZeroTotal_KeepsNothing()
        {
            Assert.AreEqual(0, RagdollSkeleton.SelectBones(null, 0.02f).Length);
            CollectionAssert.AreEqual(new[] { false, false },
                                      RagdollSkeleton.SelectBones(new[] { 0f, 0f }, 0.02f));
        }

        [Test]
        public void CapsuleSize_UsesBoneLength_AndClampsRadius()
        {
            // A long thin bone: height follows the bone, radius follows the spread.
            Vector2 forearm = RagdollSkeleton.CapsuleSize(0.30f, 0.05f, 0.03f);
            Assert.AreEqual(0.30f, forearm.y, 1e-4f);
            Assert.AreEqual(0.05f, forearm.x, 1e-4f);

            // Radius never exceeds half the height, or the capsule is a sphere that eats its neighbours.
            Vector2 stubby = RagdollSkeleton.CapsuleSize(0.10f, 0.40f, 0.03f);
            Assert.AreEqual(0.05f, stubby.x, 1e-4f);

            // And never collapses to nothing.
            Vector2 tiny = RagdollSkeleton.CapsuleSize(0.20f, 0.001f, 0.03f);
            Assert.AreEqual(0.03f, tiny.x, 1e-4f);
        }

        [Test]
        public void MassFor_SplitsByWeight_AndHasAFloor()
        {
            Assert.AreEqual(30f, RagdollSkeleton.MassFor(300f, 1000f, 100f, 0.5f), 1e-3f);
            Assert.AreEqual(0.5f, RagdollSkeleton.MassFor(1f, 1000f, 100f, 0.5f), 1e-3f,
                            "a near-weightless bone still needs enough mass to be simulated stably");
        }

        [Test]
        public void MassFor_ZeroTotalWeight_FallsBackToTheFloor()
        {
            Assert.AreEqual(0.5f, RagdollSkeleton.MassFor(0f, 0f, 100f, 0.5f), 1e-3f);
        }

        [Test]
        public void IsSettled_NeedsBothSpeedsLow_ForLongEnough()
        {
            // Moving: not settled however long it has been.
            Assert.IsFalse(RagdollSkeleton.IsSettled(2f, 0.1f, 5f, 0.25f, 1f, 0.4f));
            // Spinning on the spot: also not settled.
            Assert.IsFalse(RagdollSkeleton.IsSettled(0.1f, 8f, 5f, 0.25f, 1f, 0.4f));
            // Slow, but not for long enough yet.
            Assert.IsFalse(RagdollSkeleton.IsSettled(0.1f, 0.2f, 0.2f, 0.25f, 1f, 0.4f));
            // Slow, and long enough.
            Assert.IsTrue(RagdollSkeleton.IsSettled(0.1f, 0.2f, 0.5f, 0.25f, 1f, 0.4f));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Unity Test Runner → EditMode → `RagdollSkeletonTests`.
Expected: compile error, `RagdollSkeleton` does not exist.

- [ ] **Step 3: Write the implementation**

`SelectBones`, `CapsuleSize`, `MassFor`, `IsSettled` as exercised above. Every threshold arrives as
a parameter — no defaults, following the rule `RepulsorBlast.FlingVelocity` sets: the serialized
field on the component is the only source of truth.

- [ ] **Step 4: Run the tests to verify they pass**

Expected: 6 passing.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollSkeleton.cs Assets/Game/Editor/Tests/RagdollSkeletonTests.cs
git commit -m "feat: ragdoll skeleton math"
```

---

## Task 2: `RagdollRig` — the physical skeleton

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs`

Public surface the adapters depend on:

```csharp
public bool IsLimp { get; }
public bool IsSettled { get; }
public Transform Hips { get; }
public void GoLimp(Vector3 impulse, bool settled);
public void Recover();
public void Freeze();
```

- [ ] **Step 1: Lazy build.** On the first `GoLimp`, walk the `SkinnedMeshRenderer`s under the root,
  accumulate per-bone vertex weight via `sharedMesh.GetAllBoneWeights()` / `GetBonesPerVertex()`,
  hand the totals to `RagdollSkeleton.SelectBones`, and keep the surviving bones. The hips are the
  common ancestor of the kept set. For each kept bone: `Rigidbody` (mass from
  `RagdollSkeleton.MassFor`), `CapsuleCollider` (size from `RagdollSkeleton.CapsuleSize`, oriented
  down the bone toward its first kept child), and — for every bone that has a kept ancestor — a
  `CharacterJoint` to that ancestor with the serialized swing/twist limits.

- [ ] **Step 2: `GoLimp(impulse, settled)`.** Disable the animator, switch every bone body to
  non-kinematic, apply `impulse` as a `VelocityChange` to the hips, register with `RagdollBudget`.
  `settled: true` skips the impulse and starts the settle timer already expired — the save-restore
  path, so a loaded corpse lies down rather than standing up.

- [ ] **Step 3: Root-follow in `LateUpdate`.** Move the root transform to the hip bone's position,
  then counter-move the hips by the same delta so nothing visually shifts. This is what keeps the
  `NetworkTransform` and the save record pointing at where the body actually is. Record the
  pre-limp pose so recovery can build the `TeleportMove`.

- [ ] **Step 4: Settle detection.** Feed hip linear and angular speed to
  `RagdollSkeleton.IsSettled`. `IsSettled` also returns true once `maxLimpSeconds` has elapsed —
  the `GDC-L1-FEEL-0002` ceiling, so a wedged body never holds a player hostage.

- [ ] **Step 5: `Recover()`.** Snapshot every bone's local rotation, destroy the joints and bodies,
  re-enable the animator, and blend the snapshot into the live animation over
  `recoverBlendSeconds` from `LateUpdate`. Control is handed back by the *adapter* at the start of
  this blend, not the end (`GDC-L1-ANIM-0002`).

- [ ] **Step 6: `Freeze()`.** Destroy joints and bodies, leave bones where they lie. What
  `RagdollBudget` calls on the oldest corpse past the cap.

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs
git commit -m "feat: runtime ragdoll rig builder"
```

---

## Task 3: `RagdollBudget` — the frame-cost ceiling

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollBudget.cs`

- [ ] **Step 1:** Static registry holding live limp rigs in insertion order. `Register(rig)` evicts
  the oldest with `Freeze()` once the count passes the cap; `Unregister(rig)` on recover or
  destroy. Cap read from a serialized field on the rig doing the registering, so it stays tunable
  (`GDC-L1-PERF-0004`).

- [ ] **Step 2: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/RagdollBudget.cs
git commit -m "feat: ragdoll concurrency budget"
```

---

## Task 4: `AgentRagdoll` — the agent adapter

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Ragdoll/AgentRagdoll.cs`

- [ ] **Step 1: Suspend/resume table.**

| Layer | Suspend | Resume |
| --- | --- | --- |
| `AgentController` | `enabled = false` | `enabled = true` |
| `ISelfDrivingMotor` | `SuspendSelfDrive()` | `ResumeSelfDrive()` |
| `LeggedLocomotion` | `enabled = false` | `enabled = true` |
| Root `Collider` | `enabled = false` | `enabled = true` |
| Root `Rigidbody` | `isKinematic = true` | restore what was found |

`LeggedLocomotion` must be switched **off**, not set `ExternallyPosed`. That flag means "someone
else writes the root, but I keep solving the legs", which is exactly wrong for a ragdoll that owns
the bones.

- [ ] **Step 2: Death.** Subscribe to `HealthComponent.OnDeath`. Go limp with an impulse derived
  from the last damage direction. When `health.IsRestoring`, call `GoLimp(Vector3.zero, settled: true)`
  instead — the same rule `HealthReactionModule.ApplyDeadState` already follows, so a loaded corpse
  does not replay the death.

- [ ] **Step 3: Knockdown.** `this.NetOn(NetMsg.Knockdown, ...)` in `OnEnable`, `NetOff` in
  `OnDisable`. Go limp with `arg.P` as the impulse; schedule recovery when settled. Refuse while
  dead (a corpse does not get up) and while mounted (`MountModule` with a rider — a rider parented
  to the seat would be dragged through the ground).

- [ ] **Step 4: Recovery rebase.** Before resuming, raise `ITeleportAware.OnTeleported` on every
  implementor under the root with `new TeleportMove(preLimpPos, preLimpRot, restPos, restRot)`.
  Without this, `LeggedLocomotion` rewrites the body from a `pathPos` it integrated before the fall
  and the creature walks straight back to where it was hit. For a `NavMeshAgent`, warp onto the
  NavMesh at the resting position before re-enabling.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/AgentRagdoll.cs
git commit -m "feat: agent ragdoll adapter"
```

---

## Task 5: `PlayerRagdoll` — the player adapter

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs`
- Modify: `Assets/Game/Scripts/Characters/Player/Movement/PlayerLook.cs`
- Modify: `Assets/Game/Scripts/Characters/Player/Core/PlayerController.cs:227`

- [ ] **Step 1: `PlayerLook.SetFirstPersonHidden(bool)`.** `ApplyFirstPersonVisibility` currently
  forces `ShadowsOnly` on the helmet and scarf for the player's own camera. While ragdolled the
  camera is outside the body, so the flag has to be liftable or the player watches a headless
  corpse.

- [ ] **Step 2: Suspension.** Freeze `Input`, `PlayerMovement` and `PlayerLook` — the same three
  `PlayerController.ApplyDeathFreeze` freezes — plus the animator and the capsule collider.

- [ ] **Step 3: Camera.** On going limp, unparent the camera and lerp it to an over-the-shoulder
  framing of the hips; on recovery, lerp back and reparent. Only on the owning machine — a peer's
  copy ragdolls visually with no camera involvement.

- [ ] **Step 4: Knockdown vs death.** `NetMsg.Knockdown` on the owner's machine goes limp and
  recovers; `PlayerController.OnDeath` goes limp permanently. Control returns at the **start** of
  the recovery blend (`GDC-L1-ANIM-0002`), and `maxLimpSeconds` guarantees it returns at all
  (`GDC-L1-FEEL-0002`). Recovery must refuse while `PlayerController.IsDead` — death outranks it,
  exactly as the existing comment on `isDead` says.

- [ ] **Step 5: Replace the TODO.** `PlayerController.OnDeath`'s `// TODO: ragdoll` becomes the
  real call.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs Assets/Game/Scripts/Characters/Player/
git commit -m "feat: player ragdoll on death and knockdown"
```

---

## Task 6: `NetMsg.Knockdown` and the gauntlet

**Files:**
- Modify: `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs` (after `PortalsShut = 81`)
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs:270-320`

- [ ] **Step 1: The message.**

```csharp
// Server → everyone, on the VICTIM's relay: this body has been knocked down and every machine
// must present it going limp, with P as the impulse (m/s, world space).
//
// Separate from Flung (79) rather than folded into it, because Flung is shared three ways and one
// of them is self-inflicted: GravelBlasterArtifact flings the HOLDER as self-propulsion. A ragdoll
// hung off Flung would knock players down every time they fired their own gravel blaster.
public const ushort Knockdown = 82; // server → everyone, on the VICTIM's relay
```

- [ ] **Step 2: Players.** In `FireBlast`, send `Knockdown` next to the existing `Flung`. Both are
  needed and neither replaces the other: `Flung` carries the body, `Knockdown` carries the pose.

- [ ] **Step 3: Agents.** The `AgentController` branch currently leaps if the motor can and does
  nothing otherwise. It becomes: leap if the creature is **mounted** (a rider must not be dragged),
  otherwise send `Knockdown`. That closes the "deferred by spec" gap in the existing comment — the
  creatures that could not leap now do something.

- [ ] **Step 4: Fix the class doc.** `RepulsorGauntletArtifact`'s header says the blast "ragdolls
  everything in a wide cone". It is now true; the authority paragraph below it needs updating to
  name `Knockdown`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs
git commit -m "feat: gauntlet shock wave ragdolls its victims"
```

---

## Task 7: Prefab wiring and verification

- [ ] **Step 1:** Wire the prefabs with `Tools/SpaceGame/Ragdoll/Wire Prefabs`
  (`Assets/Game/Editor/AssetPipeline/RagdollWiring.cs`), not by hand — most agent prefabs are
  variants whose root is a prefab instance, and editing that YAML directly is how you get a prefab
  Unity cannot open. `RagdollRig` comes along via `[RequireComponent]`.

  A prefab qualifies on **any** of `AgentController`, `LeggedLocomotion` or `HealthComponent`. All
  three are needed: the Ostrich is a rideable mount with no health because it cannot die, and
  `CrabWalker6` / `HumanoidRobot` are walked by `LeggedLocomotion` with no agent brain at all. All
  three have skeletons a blast can fell — dying is only one of the two ways a body ends up down.

- [ ] **Step 2:** Correct the `HealthReactionModule` header comment — it promises a ragdoll trigger
  it never had, and the trigger now lives in `AgentRagdoll` instead.

- [ ] **Step 3: Verify.**
  - Edit-mode: `RagdollSkeletonTests` green.
  - Host + client: blast a crowd — both machines see the tumble, agree where the bodies end up, and
    the caster is not knocked down by their own blast.
  - Kill a creature, save, quit, reload: the corpse is lying in the same spot and does not stand up.
  - Blast a mounted nomad: it leaps, the rider stays seated, neither goes limp.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Prefabs Assets/Game/Scripts/agents/Entity/HealthReactionModule.cs
git commit -m "feat: wire ragdoll onto agent and player prefabs"
```
