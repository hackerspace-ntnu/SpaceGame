# Artifact Multiplayer Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every artifact added on the `movement-and-perspective` branch work for real clients, not just the host — fixing the four high-severity divergences (lasso on players, leash on client-ridden mounts, no late-join state, bystanders eating portals) and the medium races behind them.

**Architecture:** Three shared root causes, three shared fixes. (1) Code asked `Network.Simulates` — "am I the server?" — where the real question is `Network.Owns` — "is this body mine to move?"; the two differ exactly when a client owns something, which is the ordinary case. (2) State that only ever travelled as an event never reaches a machine that was not listening, so a joiner or a loaded world has nothing; one snapshot channel, built on the `ISaveable` capture/restore that already exists, serves all of them. (3) Latches derived from per-machine timers race the messages that should have decided them; each becomes message-driven.

**Tech Stack:** Unity 6, Netcode for GameObjects, the project's `NetMessaging`/`NetChannel`/`NetRelay` layer, `ISaveable`/`SaveManager` persistence, NUnit EditMode tests under `Assets/Game/Editor/Tests/`.

---

## Background you need before task 1

Read these first. They are short and every task below assumes them.

- `Assets/Game/Scripts/Core/Multiplayer/Networking.cs` — `Network.Simulates` vs `Network.Owns`. **`Simulates` is true on the server for everything in the world.** `Owns` is true only on the machine that owns the `NetworkObject`. Both fall back to `true` for anything with no spawned `NetworkObject`, which is how offline and unnetworked props keep working.
- `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs` — `NetArg`, the `NetMsg` id catalog, `NetOn`/`NetOff`/`NetToServer`/`NetToAll`/`NetToOthers`/`NetSendTo`.
- `Assets/Game/Scripts/Items/Core/UsableItem.cs` — `Use()` runs on the authority only; `Present()` runs on every machine; `OnRequestUse(ref NetArg)` runs on the owner before the message leaves.
- `Assets/Game/Scripts/Characters/Player/Movement/FlungBody.cs` — the sanctioned shape for "the server decided, but only the owner may apply it". Every fix in Phase 1 is this shape.
- `.claude/skills/spacegame-multiplayer/SKILL.md` — the authority table and the verification recipe.

**Two house rules that bite in this area:**

1. A player's `NetworkTransform` is **owner-authoritative**. A server-side write to a remote player's body is overwritten within a tick, with no error. This is why half the bugs below are invisible when you host.
2. `PlayerMovement.FixedUpdate` **assigns** horizontal velocity while grounded rather than blending it. Anything that pushes a player before it runs is deleted, not damped. That is why `FlungBody`, `LeashedBody` and `LassoArtifact` all carry `[DefaultExecutionOrder(200)]`, and why anything new that pushes a player must too.

**Message ids.** The catalog currently ends at `Knockdown = 82`. This plan appends `83`, `84`, `85` and never reuses a retired number. Check the tail before allocating:

```bash
grep -n "public const ushort" Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs | tail -3
```

**Running the tests.** All new tests go in `Assets/Game/Editor/Tests/` (namespace `SpaceGame.EditorTools`) because they touch `Assembly-CSharp` types and an asmdef cannot reference `Assembly-CSharp`. Run them from the Unity Editor menu `Tools/Tests/Run EditMode Tests (headless)`, which writes results to `Temp/headless_tests.txt`. It refuses to start in play mode.

---

## File structure

**New files:**

| File | Responsibility |
| --- | --- |
| `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoedBody.cs` | Applies a lasso's pull to a **player** victim, on that player's own machine. Mirror of `LeashedBody`. |
| `Assets/Game/Scripts/Core/Multiplayer/SessionSnapshot.cs` | Hands a joining client the world state that only ever travelled as events — built from the `ISaveable` savers that already exist. |
| `Assets/Game/Editor/Tests/BodyOwnershipTests.cs` | Pins the "`Owns`, not `Simulates`, decides who moves a body" rule. |
| `Assets/Game/Editor/Tests/SessionSnapshotTests.cs` | Pins the snapshot's key whitelist, round trip and empty-state behaviour. |
| `Assets/Game/Editor/Tests/HoldLatchTests.cs` | Pins the grapple winch latch against both message orderings, and the hold timeouts. |

**Modified files:**

| File | Change |
| --- | --- |
| `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs` | Append `Leap = 83`, `GrappleOff = 84`, `LeashSnap = 85` with their contracts. |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs` | `ResolvedHere` asks ownership; `TieTo` takes a knot offset rather than measuring one. |
| `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs` | Length travels rather than being measured per machine; server-authoritative snap; untie addressed relative to an anchor. |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs` | Sends the knot offset and length; local-only ropes to unnetworked props; snap handler. |
| `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoTether.cs` | Ownership gate, kinematic restore, single-binder. |
| `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs` | Players routed to `LassoedBody`; rope length announced; hold timeout; re-announce on join. |
| `Assets/Game/Scripts/Items/Artifacts/Gadgets/BlastPush.cs` | Leaps travel to the mount's owner instead of being applied server-side. |
| `Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs` | Receives `NetMsg.Leap` on the machine that owns the mount. |
| `Assets/Game/Scripts/Items/Artifacts/Gadgets/GrapplingHookArtifact.cs` | Winch latch survives either message ordering; auto-release announced; hold timeout. |
| `Assets/Game/Scripts/Portals/Portal.cs` | Only the traveller's owner shuts the pair; expiry is announced, not locally decided; conform retries. |
| `Assets/Game/Scripts/Portals/PortalPair.cs` | Server-authoritative expiry; snapshot capture/apply seam. |
| `Assets/Game/Scripts/Core/Persistence/Runtime/SaveManager.cs` | Expose capture/restore of a single global key, so the snapshot reuses the savers rather than copying them. |
| `Assets/Game/Scripts/Core/Multiplayer/MultiplayerAutotest.cs` | New `[MPTEST]` reports proving the fixes on a real client. |

---

# Phase 1 — One rule for who moves a body

Four bugs, one cause: code asked "am I the server?" where it should have asked "is this mine to move?".

---

### Task 1: Pin the ownership rule with a test

**Files:**
- Test: `Assets/Game/Editor/Tests/BodyOwnershipTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `Assets/Game/Editor/Tests/BodyOwnershipTests.cs`:

```csharp
// The one rule Phase 1 exists to enforce: a body is moved by the machine that OWNS it, never by
// "the server" as a blanket answer. Offline — which is what an EditMode test is — every machine
// owns everything, so what these tests can pin is that the code ASKS the ownership question at
// all, and that the offline answer stays permissive. The client-side half is proved by the
// two-process run in Phase 6; there is no substitute for it and these tests do not pretend to be one.
using NUnit.Framework;
using SpaceGame.Core;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class BodyOwnershipTests
    {
        private GameObject scratch;

        [SetUp]
        public void SetUp() => scratch = new GameObject("ownership-scratch");

        [TearDown]
        public void TearDown()
        {
            if (scratch != null) Object.DestroyImmediate(scratch);
        }

        [Test]
        public void OfflineOwnsEverything()
        {
            Assert.IsTrue(Network.Owns(scratch.transform),
                          "Offline, every machine owns everything — otherwise nothing moves in single-player.");
        }

        [Test]
        public void LeashObjectEndIsResolvedByItsOwner()
        {
            var target = new GameObject("crate");
            target.AddComponent<Rigidbody>().isKinematic = true;

            var rope = Leash.Create(new Leash.Settings { length = 8f, rope = new LeashRope() });
            rope.TieEndTo(true, target, Vector3.zero);

            Assert.IsTrue(rope.A.ResolvedHere,
                          "An unnetworked object is owned by every machine, so each resolves its own copy.");

            rope.Dispose();
            Object.DestroyImmediate(target);
        }
    }
}
```

- [ ] **Step 2: Run it and watch it fail to compile**

Run `Tools/Tests/Run EditMode Tests (headless)` in the Unity Editor, then read `Temp/headless_tests.txt`.
Expected: compile error — `Leash.Create` exists but `LeashEnd.ResolvedHere` currently reads `!Network.IsNetworked || Network.Server` for the object branch. The test compiles and **passes** offline. That is expected and fine: it is a regression guard, not the proof. Confirm it is green before continuing, so a later refactor cannot break the offline path silently.

- [ ] **Step 3: Commit the guard**

```bash
git add Assets/Game/Editor/Tests/BodyOwnershipTests.cs
git commit -m "test: pin body-ownership rule for leash ends"
```

---

### Task 2: A leash end is resolved by the machine that owns it

The bug: `LeashEnd.ResolvedHere` gives every non-player end to the server. A mount is owned by its **rider** while ridden (`MountNetworkSync` transfers ownership), so the server's pulls are silently overwritten and the rider's machine declines to resolve — nobody holds the rope.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs:218-219`

- [ ] **Step 1: Change the gate**

Replace the `ResolvedHere` property:

```csharp
        /// <summary>
        /// Is this end mine to move?
        ///
        /// <para>
        /// Ownership, not "am I the server". A transform is written by the machine that owns its
        /// NetworkObject and anything another machine writes is overwritten within a tick, silently
        /// — and the owner is NOT always the server. A ridden mount belongs to its rider
        /// (MountNetworkSync hands it over), a player belongs to themselves, and a prop nobody
        /// networked belongs to everyone, which is right: each machine then resolves its own
        /// unshared copy. Asking Network.Server instead left a client-ridden mount with no machine
        /// resolving its end at all — the rope held a host-ridden animal and was inert against a
        /// client-ridden one.
        /// </para>
        /// </summary>
        public bool ResolvedHere =>
            Network.Owns(Body != null ? (Component)Body : Anchor);
```

- [ ] **Step 2: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: `BodyOwnershipTests` and `LeashConstraintTests` PASS. The offline answer is unchanged (`Owns` returns true with no `NetworkObject`), so nothing single-player moves.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs
git commit -m "fix(leash): resolve an end on the machine that owns it, not on the server"
```

---

### Task 3: A blast leap reaches the machine that owns the mount

The bug: `BlastPush.PushAgent` calls `IMountLeapMotor.RequestLeap` directly from the server's `Use()`. `RequestLeap` writes local motor state and replicates nothing, so a mount ridden by a client leaps only on the server's non-authoritative copy and the rider sees nothing. The player branch two lines above already does this correctly with `NetMsg.Flung`; the agent branch is the one that was missed.

**Files:**
- Modify: `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs` (append id 83)
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/BlastPush.cs:164-190`
- Modify: `Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs`

- [ ] **Step 1: Add the message id**

In `NetMessaging.cs`, immediately after `public const ushort Knockdown = 82;`:

```csharp
        // Server → everyone, on the MOUNT's relay: this agent has been thrown and the machine that
        // owns it must run the leap.
        //
        // The agent counterpart of Flung (79), and it exists for the same reason. A mount is owned
        // by its RIDER while ridden (MountNetworkSync hands ownership over so the motion replicates
        // outward from them), so a leap applied on the server writes a transform the rider's next
        // state update overwrites — silently. Broadcast because this layer has no unicast; every
        // machine hears it and only the owner acts (see NavMeshAgentMotor).
        //
        //   P  the leap, as direction × horizontal distance in metres. One field rather than two
        //      because that is exactly what the caller already computed, and a magnitude carries
        //      the distance for free.
        //   A  peak height, in centimetres — NetArg has no float field, the same reason
        //      CraftLaunch sends speeds in centimetres per second.
        //   B  duration, in milliseconds.
        public const ushort Leap = 83; // server → everyone, on the MOUNT's relay
```

- [ ] **Step 2: Send the leap instead of applying it**

In `BlastPush.cs`, replace the body of `PushAgent` below the ragdoll branch:

```csharp
            var leaper = root.GetComponentInChildren<IMountLeapMotor>();
            if (leaper == null) return;

            // A blast with no horizontal component resolves to no direction at all. See the class
            // remarks: this used to leap along a zero vector.
            Vector3 away = Vector3.ProjectOnPlane(velocity, Vector3.up);
            if (away.sqrMagnitude < 1e-6f) return;

            float strength = Mathf.Clamp01(velocity.magnitude / Mathf.Max(referenceSpeed, 0.01f));
            float distance = Mathf.Lerp(leap.MinDistance, leap.MaxDistance, strength);
            float height = Mathf.Lerp(leap.MinHeight, leap.MaxHeight, strength);

            // Sent rather than applied, even though we are the authority and the motor is right
            // here. A ridden mount is owned by its RIDER, and a leap written here would be
            // overwritten by their next state update within a tick. IsLeapAvailable is deliberately
            // NOT checked here either — it is a property of the motor on the machine that will run
            // the leap, and this machine's copy of a client-owned mount is not that machine.
            NetMessaging.NetSendTo(root, NetMsg.Leap, new NetArg
            {
                P = away.normalized * distance,
                A = Mathf.RoundToInt(height * 100f),
                B = Mathf.RoundToInt(leap.Duration * 1000f),
            }, NetTo.All);
```

- [ ] **Step 3: Receive it on the owning machine**

In `NavMeshAgentMotor.cs`, add the handler beside the leap implementation. If the class has no `OnEnable`/`OnDisable`, add them; if it has, add these two lines to the existing bodies:

```csharp
        private void OnEnable() => this.NetOn(NetMsg.Leap, OnLeapRequested);

        private void OnDisable() => this.NetOff(NetMsg.Leap, OnLeapRequested);

        /// <summary>
        /// A blast has thrown this animal. Run it here only if this machine owns it.
        ///
        /// Broadcast on the mount's relay, so every machine receives this and exactly one acts —
        /// the server for a loose creature, the rider's machine for a mount somebody is on. See
        /// NetMsg.Leap, and FlungBody, which is the same shape for a player.
        /// </summary>
        private void OnLeapRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Owns(this)) return;
            if (!IsLeapAvailable) return;

            float distance = arg.P.magnitude;
            if (distance < 1e-3f) return;

            RequestLeap(arg.P / distance, distance, arg.A * 0.01f, arg.B * 0.001f);
        }
```

Add `using SpaceGame.Core;` to the file's usings if it is not already there.

- [ ] **Step 4: Verify it compiles and the guards still pass**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS, including `NetMessagingTests` (which asserts every id is unique and non-zero — a duplicate `83` fails here).

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs \
        Assets/Game/Scripts/Items/Artifacts/Gadgets/BlastPush.cs \
        Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs
git commit -m "fix(blast): route agent leaps to the machine that owns the mount"
```

---

### Task 4: A lasso tether runs where the creature is owned, and cleans up after itself

Two bugs in one component. It is created wherever `Network.Simulates` is true (the server, for everything), so a client-ridden mount is tethered on the wrong machine; and `Bind` flips a kinematic replica to non-kinematic and `Release` never puts it back, which outlives the rope and can be captured by autosave.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoTether.cs:125`, `:138-158`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs:770-774`

- [ ] **Step 1: Restore what Bind changed**

In `LassoTether.cs`, add a field beside `bound`:

```csharp
        /// <summary>Set when this tether — and not something else — made the body dynamic.</summary>
        private bool madeDynamic;
```

Replace the kinematic line in `Bind`:

```csharp
            // Only a body this machine owns. A replica is kinematic on purpose, and flipping one
            // here gave it gravity and collisions it must not have — a corruption that outlived the
            // rope, because Release used not to put it back, and that a quit-time autosave could
            // capture.
            if (body != null && body.isKinematic && Network.Owns(body))
            {
                body.isKinematic = false;
                madeDynamic = true;
            }
```

Add the restore to `Release`, immediately after `bound = false;`:

```csharp
            if (madeDynamic && body != null) body.isKinematic = true;
            madeDynamic = false;
```

- [ ] **Step 2: Ask ownership when deciding where the tether lives**

In `LassoArtifact.cs`, replace `SimulatesTarget`:

```csharp
        /// <summary>
        /// May this machine move what is on the end of the rope?
        ///
        /// Ownership, not "am I the server". A loose creature is owned by the server, a ridden
        /// mount by its rider, and a prop nobody networked by everyone — and in each case that is
        /// the one machine whose writes to the transform survive. Asking Simulates put the tether
        /// on the server for a client-ridden mount, where every write it made was overwritten
        /// within a tick.
        /// </summary>
        private bool OwnsTarget()
        {
            Component target = _targetRb != null ? _targetRb : (Component)_targetTransform;
            return target != null && Network.Owns(target);
        }
```

Update the one call site in `Attach` from `if (SimulatesTarget())` to `if (OwnsTarget())`.

- [ ] **Step 3: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS. `LassoTests` exercises `Bind`/`Release` offline, where `Owns` is true, so behaviour there is unchanged.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoTether.cs \
        Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs
git commit -m "fix(lasso): tether the creature on the machine that owns it, and restore its body"
```

---

### Task 5: Two ropes cannot share one tether

`LassoTether.Ensure` returns the existing component and `Bind` overwrites its anchor, so a second player roping the same creature silently steals the first rope, and the first player's release frees the creature out from under the second.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoTether.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs:594-598`
- Test: `Assets/Game/Editor/Tests/LassoTests.cs` (extend)

- [ ] **Step 1: Write the failing test**

Append to the existing test class in `Assets/Game/Editor/Tests/LassoTests.cs`:

```csharp
        [Test]
        public void ASecondRopeCannotStealABoundTether()
        {
            var creature = new GameObject("creature");
            var first = new GameObject("rope-a").transform;
            var second = new GameObject("rope-b").transform;

            LassoTether tether = LassoTether.Ensure(creature);

            Assert.IsTrue(tether.Bind(first, 6f, new LassoStruggle()), "The first rope takes hold.");
            Assert.IsFalse(tether.Bind(second, 6f, new LassoStruggle()),
                           "A creature already on a rope refuses a second one rather than swapping.");

            // The first rope still owns it, so only the first rope may let it go.
            tether.Release(second);
            Assert.IsTrue(tether.IsBound, "A release from the wrong rope frees nothing.");

            tether.Release(first);
            Assert.IsFalse(tether.IsBound, "The rope that took hold can let go.");

            Object.DestroyImmediate(creature);
            Object.DestroyImmediate(first.gameObject);
            Object.DestroyImmediate(second.gameObject);
        }
```

- [ ] **Step 2: Run it to verify it fails**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: FAIL to compile — `Bind` returns `void`, `Release` takes no argument, and `IsBound` does not exist.

- [ ] **Step 3: Make the tether single-binder**

In `LassoTether.cs`, add beside `bound`:

```csharp
        /// <summary>The rope anchor that took hold. Only that rope may let go again.</summary>
        private Transform binder;

        /// <summary>Whether a rope currently holds this creature.</summary>
        public bool IsBound => bound;
```

Change `Bind`'s signature to `public bool Bind(Transform ropeAnchor, float length, LassoStruggle struggleSettings)` and open its body with:

```csharp
            // One rope at a time. Ensure returns whatever component is already here, so without
            // this a second thrower rebound the SAME tether: the first rope's constraint vanished
            // while its item still drew a rope and still dragged its holder, and whichever thrower
            // released first freed the creature from both.
            if (bound && binder != ropeAnchor) return false;

            binder = ropeAnchor;
```

End the method with `return true;` in place of the bare `bound = true;` — that is, `bound = true; return true;`.

Change `Release` to `public void Release(Transform ropeAnchor)` and open it with:

```csharp
            // Addressed by the rope that took hold, so a release travelling from another thrower's
            // item — or from a stale instance being destroyed — cannot free a creature somebody
            // else is still holding.
            if (!bound || (ropeAnchor != null && ropeAnchor != binder)) return;

            binder = null;
```

Change `OnDestroy` to `private void OnDestroy() => Release(binder);` — the component going away releases whatever holds it, whoever that is.

- [ ] **Step 4: Update the artifact's two call sites**

In `LassoArtifact.cs`, in `Attach`, replace the tether block:

```csharp
            if (OwnsTarget())
            {
                Transform anchor = muzzle != null ? muzzle : owner.transform;
                LassoTether tether = LassoTether.Ensure(root.gameObject);

                // A creature already on somebody else's rope is not catchable. Refusing here rather
                // than drawing a rope that constrains nothing is what stops a second thrower being
                // dragged around by an animal that cannot feel them.
                if (tether == null || !tether.Bind(anchor, _currentRopeLength, struggle))
                {
                    _isLassoed = false;
                    return;
                }

                _tether = tether;
            }
```

In `Release()`, change the tether teardown to pass the anchor back:

```csharp
            // Only ever non-null on the machine that took the creature's legs — see Attach — so
            // this hands navigation back exactly where it was taken away, and only if this rope is
            // still the one holding it.
            if (_tether != null)
            {
                _tether.Release(muzzle != null ? muzzle : owner != null ? owner.transform : null);
                _tether = null;
            }
```

Note the ordering trap: `Release()` is reached from `OnDestroy`, where `owner` may already be gone. The null-conditional chain above is why it is written out rather than inlined.

- [ ] **Step 5: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS, including the new `ASecondRopeCannotStealABoundTether`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoTether.cs \
        Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs \
        Assets/Game/Editor/Tests/LassoTests.cs
git commit -m "fix(lasso): one rope per creature, released only by the rope that took hold"
```

---

### Task 6: Lassoing a player works against clients

The bug: `TryGetLatchTarget` accepts any Rigidbody and the shipped prefab's `hookableLayers` is everything, so players are catchable — but the tether lands on the server, which cannot move an owner-authoritative body, and the victim's machine is never told to do anything. Roping the *host* works, which is why this survived. The fix is the shape `LeashedBody` already proves: the victim's own machine applies the constraint.

**Files:**
- Create: `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoedBody.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs` (`Attach`, `Release`)

- [ ] **Step 1: Write the component**

Create `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoedBody.cs`:

```csharp
// The half of a lasso that only the roped PLAYER's machine can run.
//
// LassoTether is the creature end: it takes an animal's legs off its AI and drives them. A player
// is not that. Their body is owner-authoritative — the server cannot push it, and anything it
// writes is overwritten within a tick, silently — so roping a player has to be applied by the
// player who was roped, on their own machine.
//
// Nothing has to be sent for that to work. The catch is already broadcast to every machine
// (NetMsg.LassoRoped), and both ends of the rope are replicated transforms, so the victim's machine
// has everything it needs to compute its own half. Which is the same trick the two ends of a leash
// use, and the reason PlayerPullShare is a pure function of two masses rather than a number
// somebody sends.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Holds a roped player back, on that player's own machine.
    ///
    /// <para>
    /// Added on demand rather than authored, because any player can be roped at any time and the
    /// alternative is a component every player carries for a case most of them never hit. Same
    /// shape and same reason as <see cref="LeashedBody"/> and <see cref="LassoTether"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    [DefaultExecutionOrder(200)] // after PlayerMovement, which ASSIGNS velocity — see LeashedBody
    public sealed class LassoedBody : MonoBehaviour
    {
        private Rigidbody body;
        private Transform anchor;
        private float ropeLength;
        private float mass = 80f;
        private bool bound;

        /// <summary>Whether a rope currently holds this player.</summary>
        public bool IsBound => bound;

        public static LassoedBody Ensure(GameObject player)
        {
            if (player == null) return null;

            return player.TryGetComponent(out LassoedBody existing)
                ? existing
                : player.AddComponent<LassoedBody>();
        }

        private void Awake() => body = GetComponent<Rigidbody>();

        /// <summary>Take hold. False when somebody else's rope already has this player.</summary>
        public bool Bind(Transform ropeAnchor, float length, float throwerMass)
        {
            // One rope at a time, for the reason LassoTether documents: two ropes sharing one
            // constraint means whichever is released first frees the player from both.
            if (bound && anchor != ropeAnchor) return false;

            anchor = ropeAnchor;
            ropeLength = length;
            mass = throwerMass;
            bound = true;
            return true;
        }

        public void SetRopeLength(float length) => ropeLength = length;

        public void Release(Transform ropeAnchor)
        {
            if (!bound || (ropeAnchor != null && ropeAnchor != anchor)) return;

            bound = false;
            anchor = null;
        }

        private void OnDestroy() => Release(anchor);

        private void FixedUpdate()
        {
            if (!bound || anchor == null || body == null || body.isKinematic) return;

            // Everyone in the session has one of these once a rope has been thrown at them; only
            // the machine that owns the body may move it. Elsewhere this player is a replica whose
            // position is somebody else's to publish.
            if (!Network.Owns(this)) return;

            Vector3 rope = body.position - anchor.position;
            float distance = rope.magnitude;
            if (distance <= ropeLength || distance < 0.001f) return;

            Vector3 radial = rope / distance;

            // The share is COMPUTED, not sent: the thrower's machine runs the mirror of this line
            // against the same two masses and reaches the same split. See LassoArtifact.PlayerPullShare.
            float share = 1f - LassoArtifact.PlayerPullShare(mass);

            // Velocity first, and only ever removed. A rope may take a player's speed away and drag
            // them; it may never give them speed, or a well-timed catch is a launch. This is the
            // same rule LeashEnd.Restrain enforces, and for the same reason.
            float outward = Vector3.Dot(body.linearVelocity, radial);
            if (outward > 0f) body.linearVelocity -= radial * outward;

            // The position error is given back as a POSITION, never as velocity: velocity added to
            // close a gap is still there on the next step, which is how a solver gains energy and
            // how two roped bodies end up slamming together.
            body.position -= radial * ((distance - ropeLength) * share);
        }
    }
}
```

- [ ] **Step 2: Route player targets to it**

In `LassoArtifact.cs`, in `Attach`, replace the tether block written in Task 5 with the two-target-kinds version:

```csharp
            Transform anchor = muzzle != null ? muzzle : owner.transform;

            // A PLAYER is not a creature. Their body is owner-authoritative, so the rope's pull has
            // to be applied by the machine that owns them — which is never this one, unless they
            // happen to be the local player. The component is created on EVERY machine and gates
            // itself, exactly as FlungBody and LeashedBody do, because a catch is announced to
            // everybody and only one of them turns out to own the victim.
            if (root.CompareTag("Player"))
            {
                LassoedBody caught = LassoedBody.Ensure(root.gameObject);

                if (caught == null || !caught.Bind(anchor, _currentRopeLength, AssumedPlayerMass))
                {
                    _isLassoed = false;
                    return;
                }

                _caughtPlayer = caught;
            }
            else if (OwnsTarget())
            {
                LassoTether tether = LassoTether.Ensure(root.gameObject);

                if (tether == null || !tether.Bind(anchor, _currentRopeLength, struggle))
                {
                    _isLassoed = false;
                    return;
                }

                _tether = tether;
            }
```

Add the field beside `_tether`:

```csharp
        /// <summary>Set when the far end is a player rather than a creature. See LassoedBody.</summary>
        private LassoedBody _caughtPlayer;
```

In `Release()`, extend the teardown:

```csharp
            Transform anchor = muzzle != null ? muzzle : owner != null ? owner.transform : null;

            if (_tether != null) { _tether.Release(anchor); _tether = null; }
            if (_caughtPlayer != null) { _caughtPlayer.Release(anchor); _caughtPlayer = null; }
```

In `FixedUpdate`, keep the reeled length in step with both:

```csharp
            if (_reelHeld)
            {
                _currentRopeLength = Mathf.Max(ropeSlack, _currentRopeLength - reelInForce * Time.fixedDeltaTime);
                _tether?.SetRopeLength(_currentRopeLength);
                _caughtPlayer?.SetRopeLength(_currentRopeLength);
            }
```

In `ApplyOwnerPull`, the thrower's own half already reads `_tether.Mass` for the target's weight; give the player case its constant:

```csharp
            float targetMass = _tether != null ? _tether.Mass
                             : _caughtPlayer != null ? AssumedPlayerMass
                             : _targetRb != null ? _targetRb.mass
                             : AssumedPlayerMass;
```

- [ ] **Step 3: Write the test**

Append to `Assets/Game/Editor/Tests/LassoTests.cs`:

```csharp
        [Test]
        public void ARopedPlayerIsHeldByTheirOwnMachine()
        {
            var victim = new GameObject("victim") { tag = "Player" };
            var rb = victim.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.position = new Vector3(20f, 0f, 0f);

            var anchor = new GameObject("thrower").transform;
            anchor.position = Vector3.zero;

            LassoedBody held = LassoedBody.Ensure(victim);
            Assert.IsTrue(held.Bind(anchor, 8f, 80f));

            // Offline this machine owns the body, so the constraint runs — which is the single-
            // player case and the local-player case in a session. The remote case is proved by the
            // two-process run; an EditMode test has no second machine to be wrong on.
            held.SendMessage("FixedUpdate", SendMessageOptions.DontRequireReceiver);

            Assert.Less(rb.position.x, 20f, "A player past the rope's length is pulled back toward it.");
            Assert.Greater(rb.position.x, 8f, "One step gives back a share of the error, never all of it.");

            Object.DestroyImmediate(victim);
            Object.DestroyImmediate(anchor.gameObject);
        }
```

- [ ] **Step 4: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS. If `SendMessage` cannot reach the private `FixedUpdate`, make `FixedUpdate` call a `internal void Step()` and have the test call `Step()` — the same seam `LassoTether.AdvanceStruggle` already provides for its own tests.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoedBody.cs \
        Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs \
        Assets/Game/Editor/Tests/LassoTests.cs
git commit -m "feat(lasso): a roped player is held by their own machine"
```

---

# Phase 2 — Hold streams that never strand a peer

---

### Task 7: The winch latch survives either message ordering

The bug: the trigger release is a relayed message but the dart's bite fires from each machine's **local** flight timer, so an ordinary quick-release shot latches `_winching` differently per machine. The loser draws the rope forever and then silently drops the owner's next throw, because `Present` refuses a second attach while `_isGrappling`.

The fix is not to synchronise the timer. It is to stop the latch depending on arrival order at all: remember the last hold state whenever it arrives, and read it at the bite.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/GrapplingHookArtifact.cs:292-339`, `:375-401`
- Test: `Assets/Game/Editor/Tests/HoldLatchTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `Assets/Game/Editor/Tests/HoldLatchTests.cs`:

```csharp
// The grapple's winch latch, against both orderings of the two things that decide it.
//
// A release is a relayed message and a bite is a local timer, so their order differs per machine by
// network jitter — on the thrower the release lands during flight, on a peer it can land a few
// milliseconds after that peer's own bite. Before this was a latch, the two orderings produced
// different answers, and the disagreement was permanent: the peer that latched _winching wrong
// drew the rope forever and then discarded the thrower's NEXT throw, because Present refuses a
// second attach while a rope is out.
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class HoldLatchTests
    {
        private static WinchLatch Play(params bool[] holdTicks)
        {
            var latch = new WinchLatch();
            foreach (bool active in holdTicks) latch.Observe(active);
            return latch;
        }

        [Test]
        public void ReleaseBeforeBiteStillWinches()
        {
            // The tapped shot: press, release, and only then does the dart arrive. The trigger was
            // never held at the moment of the bite, and the grapple must still reel — otherwise a
            // tap catches and hangs, which is what "it doesn't reel in" was.
            WinchLatch latch = Play(true, false);
            Assert.IsTrue(latch.WinchAtBite, "A release that beat the dart is not a refusal to winch.");
        }

        [Test]
        public void ReleaseAfterBiteStopsTheWinch()
        {
            // The deliberate gesture: hold until the rope goes taut, then let go to trade the climb
            // for a swing.
            WinchLatch latch = Play(true);
            latch.Bite();
            latch.Observe(false);
            Assert.IsFalse(latch.Winching, "Letting go after the bite trades the climb for a swing.");
        }

        [Test]
        public void HoldingThroughTheBiteWinches()
        {
            WinchLatch latch = Play(true);
            latch.Bite();
            Assert.IsTrue(latch.Winching, "A trigger still down at the bite reels in.");
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: FAIL to compile — `WinchLatch` does not exist.

- [ ] **Step 3: Write the latch**

Add to `GrapplingHookArtifact.cs`, as a nested public class at the end of the type (kept pure so the ordering property is provable without a session):

```csharp
        /// <summary>
        /// Whether the winch is running, decided so that it does not matter whether the release
        /// arrived before or after the dart landed.
        ///
        /// <para>
        /// Pure and separate from the artifact because it is the whole of the bug: the bite comes
        /// off a LOCAL flight timer and the release comes off a RELAYED message, so their order
        /// differs from machine to machine by network jitter. A latch that reads the last hold
        /// state at the moment of the bite gives every machine the same answer from the same two
        /// facts, whichever order they turned up in.
        /// </para>
        /// </summary>
        public sealed class WinchLatch
        {
            private bool held = true;
            private bool bitten;

            /// <summary>The winch state to adopt when the dart lands.</summary>
            public bool WinchAtBite => held || !bitten;

            /// <summary>Whether the winch is running right now.</summary>
            public bool Winching { get; private set; }

            /// <summary>A hold tick arrived. <paramref name="active"/> false is the release.</summary>
            public void Observe(bool active)
            {
                held = active;

                // Before the bite there is nothing to winch, so the tick is only remembered. This
                // is what makes a tapped grapple reel: its release always beats the dart.
                if (bitten) Winching = active;
            }

            /// <summary>The dart landed. Catching IS the reel — see Bite.</summary>
            public void Bite()
            {
                bitten = true;
                Winching = held;
            }

            /// <summary>A fresh throw. The trigger is down by definition, since a press started it.</summary>
            public void Reset()
            {
                held = true;
                bitten = false;
                Winching = false;
            }
        }
```

- [ ] **Step 4: Use it in place of the bare bool**

Replace the `_winching` field with `private readonly WinchLatch _winch = new();` and update its four readers and writers:

- In `Present()`, after `if (owner == null) return;` and before `CacheOwner();`, add `_winch.Reset();`
- In `PresentHold`, replace the whole body with:

```csharp
        protected override void PresentHold(NetArg arg, bool active)
        {
            // Observed unconditionally, INCLUDING while the dart is still in the air. That is the
            // fix: this used to return early when !_isGrappling, so a release that beat the dart
            // was thrown away on the machines where it arrived early and honoured on the ones where
            // it arrived late, and the two never reconciled. See WinchLatch.
            _winch.Observe(active);

            _lastHoldTime = Time.time;
        }
```

- In `Bite()`, replace `_winching = true;` with `_winch.Bite();`
- Everywhere else, replace reads of `_winching` with `_winch.Winching` (in `FixedUpdate` and `TickRemoteRope`).

- [ ] **Step 5: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS — all three `HoldLatchTests`, plus the existing `GrappleUseFlowTests` and `GrappleSwingTests`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Gadgets/GrapplingHookArtifact.cs \
        Assets/Game/Editor/Tests/HoldLatchTests.cs
git commit -m "fix(grapple): winch latch no longer depends on release-vs-bite ordering"
```

---

### Task 8: The grapple announces its own release

The bug: the owner's arrival auto-release (`ReleaseInto`) sends nothing. Peers must infer it by watching an interpolated transform pass within `arrivalDistance`, and at winch speed the body can cross that sphere between two network samples — so the peer's rope survives, and the stale `_isGrappling` then discards the owner's next throw.

**Files:**
- Modify: `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs` (append id 84)
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/GrapplingHookArtifact.cs`

- [ ] **Step 1: Add the message id**

After `Leap = 83` in `NetMessaging.cs`:

```csharp
        // What the swinger's rope is doing, when the item's own use messages cannot say it.
        //
        // On the SWINGER's channel, like the lasso's rope verbs and for the same reason: the
        // artifact prefab carries a NetworkObject (dropping the item routes through World.Spawn)
        // but it is never spawned while the item is in a hand, so a send from the item would
        // resolve to a dormant relay and run locally only.
        //
        // Two things need saying that a press cannot. A rope that let GO by itself — the swinger
        // reached the anchor, or the winch stalled — fires inside one physics step and boosts the
        // body straight back out of the arrival sphere, so a peer watching an interpolated
        // transform may have no sample inside it at all and would draw the rope forever. And a rope
        // that is still OUT has to be re-stated for somebody who has just joined, who was not
        // listening when the press went round and would otherwise watch a player arc through the
        // air on nothing.
        //
        //   A  the verb — see GrappleVerb.
        //   P  the anchor point, and R the surface normal, for On. Unused for Off.
        //   Target  what the rope is attached to, for On, when that has a networked identity.
        public const ushort GrappleRope = 84; // owner/server → everyone, on the SWINGER's channel
```

and the verbs, beside `LassoVerb` at the bottom of the file:

```csharp
    /// <summary>What a <see cref="NetMsg.GrappleRope"/> message is saying. Append only.</summary>
    public static class GrappleVerb
    {
        /// <summary>The rope has let go. Stop drawing it.</summary>
        public const int Off = 0;

        /// <summary>
        /// The rope is out, attached where <see cref="NetArg.P"/> says.
        ///
        /// An absolute state rather than an edge — "this rope is attached", not "this rope just
        /// attached" — so re-sending it to a joiner costs everybody else one idempotent no-op.
        /// </summary>
        public const int On = 1;
    }
```

- [ ] **Step 2: Register the handler**

In `GrapplingHookArtifact.cs`, add the channel plumbing. Copy the shape `LassoArtifact.Listen` already uses — the same reasoning applies verbatim:

```csharp
        /// <summary>The transform whose channel we are registered on, so we unregister from it.</summary>
        private Transform _channel;

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);
            Listen(holder != null ? holder.transform : null);
        }

        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);
            Listen(null);
        }

        private void Listen(Transform channel)
        {
            if (_channel == channel) return;

            if (_channel != null) _channel.NetOff(NetMsg.GrappleRope, OnRopeMessage);

            _channel = channel;
            if (_channel != null) _channel.NetOn(NetMsg.GrappleRope, OnRopeMessage);
        }

        /// <summary>
        /// Owner-side: tell everyone the rope has let go on its own.
        ///
        /// Only from the owner, because only the owner's release is real — a peer's copy runs no
        /// swing and has nothing to announce. NetTo.Server rather than All: a client may not
        /// broadcast, and the server relays it onward.
        /// </summary>
        private void AnnounceRelease()
        {
            if (owner == null || !OwnsMovement()) return;

            NetMessaging.NetSendTo(owner, NetMsg.GrappleRope,
                                   new NetArg { A = GrappleVerb.Off }, NetTo.Server);
        }
```

If the class already declares `OnEquipped`/`OnUnequipped`, add the `Listen(...)` calls to the existing bodies rather than adding second copies.

- [ ] **Step 3: Announce from both auto-releases and relay it server-side**

In `ReleaseInto`, call `AnnounceRelease();` immediately before it stops the grapple, so the message goes out while `owner` is still set.

One handler for both directions, branching inside — which is also what makes it idempotent on the host, where a client's request and the broadcast it produces run inline one inside the other:

```csharp
        /// <summary>
        /// The rope changed state somewhere else. Runs on every machine, and twice on the host —
        /// once for the client's request and once for the broadcast it makes — which is why both
        /// branches below must be, and are, safe to run against state that already matches.
        /// </summary>
        private void OnRopeMessage(in NetArg arg, ulong sender)
        {
            // The server is the only machine allowed to broadcast, so it passes the news on before
            // acting on it. `except: sender` keeps the announcing client from presenting twice.
            if (Network.Server && sender != Network.LocalClientId)
                NetMessaging.NetSendTo(owner, NetMsg.GrappleRope, arg, NetTo.Others, except: sender);

            if (arg.A == GrappleVerb.On)
            {
                AdoptRope(arg);
                return;
            }

            StopGrapple();
        }

        /// <summary>
        /// Take up a rope that was already out when this machine started listening — a joiner, or
        /// a peer that missed the press. Idempotent: a machine already drawing this rope keeps it
        /// rather than starting a rival throw, which is the same guard Present uses.
        /// </summary>
        private void AdoptRope(in NetArg arg)
        {
            if (_isGrappling || _isShooting) return;
            if (owner == null) return;

            CacheOwner();

            _hookPoint = arg.P;
            _hitNormal = arg.HasOrientation ? arg.R * Vector3.forward : Vector3.up;

            BindAttach(arg.Resolve());

            // Straight to the bite, skipping the flight: the dart landed before this machine was
            // listening, and replaying its arc would draw a rope flying to somewhere it already is.
            _lastHoldTime = Time.time;
            Bite();
        }
```

`NetToOthers`' `except` parameter is on the extension method, not on `NetSendTo`. If `NetSendTo` has no overload carrying it, add one rather than dropping the argument — sending the relay back to the client that asked would make that client present its own rope twice.

- [ ] **Step 4: Drop the guessing on the peer side**

In `TickRemoteRope`, remove the two inferred auto-releases now that the real one arrives. Replace the block:

```csharp
            Ratchet(dist);

            // The arrival and stall verdicts used to be re-derived here, from an interpolated
            // transform, because the owner's auto-release sent nothing. It does now
            // (NetMsg.GrappleOff), so this draws the rope and no longer guesses at when it ends —
            // which it could not do reliably: the winch crosses the arrival sphere in less than one
            // network sample and the exit boost carries the body straight back out of it.
            return false;
```

- [ ] **Step 5: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS, including `NetMessagingTests` (unique ids) and the existing grapple suites.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs \
        Assets/Game/Scripts/Items/Artifacts/Gadgets/GrapplingHookArtifact.cs
git commit -m "fix(grapple): announce the auto-release instead of making peers guess it"
```

---

### Task 9: A held item that loses its owner puts itself away

The bug: `EquipmentController.OnDisable` ends a hold **locally only** on death or teardown, and explicitly delegates the remote side to the item's own hold timeout. `LaserStaffArtifact` implements that convention (`holdTimeout`, checked in `Update`); the grapple and the lasso do not. A thrower who dies mid-twirl leaves every other machine spinning a loop over their corpse forever — and the stale `_isTwirling` then blocks their next throw after respawn.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/GrapplingHookArtifact.cs`

- [ ] **Step 1: Add the timeout to the lasso**

Add the field beside the other `[Header("Firing")]` entries:

```csharp
        [Tooltip("Seconds of silence after which a wind-up puts itself away. The safety net for a " +
                 "release that never arrived — a dropped packet, or a thrower who died mid-twirl. " +
                 "Must comfortably exceed EquipmentController's hold send interval.")]
        [SerializeField] private float holdTimeout = 0.5f;

        /// <summary>When the last hold tick arrived. Meaningless while not twirling.</summary>
        private float _lastHoldTime;
```

Stamp it in `Present()` (the press opens the stream) and in `PresentHold`:

```csharp
        protected override void PresentHold(NetArg arg, bool active)
        {
            _lastHoldTime = Time.time;

            if (active || !_isTwirling) return;
            ...
```

and in `BeginTwirl`, add `_lastHoldTime = Time.time;`.

Check it in the existing `Update`:

```csharp
        private void Update()
        {
            if (OwnerIsLocal()) ReadReelInput();

            // The safety net. A release is one message, and one message is exactly the kind of
            // thing that goes missing — along with the player who was holding the button.
            // EquipmentController.OnDisable ends a hold locally on death and leaves the remote
            // halves to this, which is the convention LaserStaffArtifact set.
            if (_isTwirling && Time.time - _lastHoldTime > holdTimeout) CancelTwirl();

            TickTwirl(Time.deltaTime);
        }
```

- [ ] **Step 2: Add the timeout to the grapple**

The grapple's `_lastHoldTime` was already added in Task 7's `PresentHold`. Declare it and the tunable:

```csharp
        [Tooltip("Seconds of silence after which a rope in flight or a swing lets go by itself. " +
                 "The safety net for a hold stream that stopped — a dropped packet, or a swinger " +
                 "who disconnected. Must comfortably exceed EquipmentController's send interval.")]
        [SerializeField] private float holdTimeout = 1.5f;

        private float _lastHoldTime;
```

Note the larger default than the lasso's: a grapple's hold stream legitimately runs for the whole of a long swing, and cutting a swinger loose on a single dropped packet is worse than holding a rope half a second too long.

Check it in the existing `Update`, before the flight tick:

```csharp
            // The safety net — see the lasso and the laser staff. Only where the stream is expected
            // to be running: a rope let go deliberately has no ticks to miss.
            if ((_isShooting || _isGrappling) && Time.time - _lastHoldTime > holdTimeout)
            {
                StopGrapple();
                return;
            }
```

Stamp it in `Present()` alongside `_winch.Reset();` so a fresh throw does not immediately time out.

- [ ] **Step 3: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS. Existing grapple and lasso suites drive these items without a clock, so they are unaffected; a failure here means a test is stepping `Update` with a stale `_lastHoldTime`, which is fixed by stamping it in the test's setup rather than by widening the timeout.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs \
        Assets/Game/Scripts/Items/Artifacts/Gadgets/GrapplingHookArtifact.cs
git commit -m "fix(items): grapple and lasso honour the hold-timeout convention"
```

---

# Phase 3 — Portals converge

---

### Task 10: Only the traveller's owner shuts the pair

The bug: `Portal.Traverse` runs `ShutBehind` unconditionally on whichever machine's local sweep fired, while `PortalPair.AnnounceTraversal` sends `PortalsUsed` only when `Network.Owns(traveller)`. The contact pull fires for a *stationary* body pressed against the opening, including an interpolated remote copy this machine does not own — so a bystander leaning near an aperture destroys the shooter's pair locally, nobody travelled, and no reconciling message is ever sent.

**Files:**
- Modify: `Assets/Game/Scripts/Portals/Portal.cs` (in `Traverse`, around `:1281-1293`)

- [ ] **Step 1: Gate the shut on the same question the announce asks**

Replace the `closeOnTraversal` block at the end of `Traverse`:

```csharp
            if (!closeOnTraversal) return;

            // Only the machine that OWNS the traveller may consume the pair, and it is the same
            // question AnnounceTraversal asks — deliberately, because the two must never disagree.
            //
            // Every machine detects crossings from its own physics, but only the owner's detection
            // actually moved a body; everyone else's is cosmetic. A peer's sweep fires for an
            // interpolated replica drifting near the opening — the contact pull needs no motion at
            // all, only proximity — and shutting on that destroyed the shooter's portals on that
            // machine with nobody having travelled and no message sent to put it back. A bystander
            // who could not even see the pair could eat it.
            if (!Network.Owns(traveller)) return;

            // Read before the close: shutting clears the pair's slots, and outside play mode
            // destroys this component outright.
            PortalPair pair = Pair;

            ShutBehind(destination);

            // After the local close, so the offline degradation — the send dispatching straight
            // back into this machine's own handler — meets a pair that is already shut and does
            // nothing twice.
            if (pair != null) pair.AnnounceTraversal(traveller);
```

`AnnounceTraversal` keeps its own `Network.Owns` guard: it is reachable from elsewhere and a guard that is now redundant on this path is cheaper than a guard that is missing on the next one.

- [ ] **Step 2: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS — `SprayedPortalTests` and `PortalGunWiringTests`. Offline `Owns` is true, so single-player traversal is untouched.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Portals/Portal.cs
git commit -m "fix(portals): only the traveller's owner may consume a pair"
```

---

### Task 11: A pair expires everywhere at once

The bug: each machine times the 20 s lifetime from its own `Present` moment, so machines expire the same aperture ~RTT apart. Two consequences: a top-up (`grow`) landing in that window grows the old shape on one machine and opens a fresh one on another, forking the outline for the aperture's whole remaining life; and the pair simply stops existing at different moments for different people.

**Files:**
- Modify: `Assets/Game/Scripts/Portals/PortalPair.cs`

- [ ] **Step 1: Make the server the one that decides expiry**

In `PortalPair.cs`, add to the `Forget` path so an aperture reaching the end of its own timer asks the session to shut the pair rather than shutting only here:

```csharp
        /// <summary>
        /// An aperture told us it is shutting. Let go of the slot, and — if it shut because its
        /// time ran out rather than because somebody closed it — make sure everyone else does too.
        ///
        /// The lifetime is counted from each machine's own Present moment, so without this the same
        /// aperture expires up to a round trip apart on different machines. Small, but not
        /// harmless: a top-up landing inside that window grows the surviving shape on one machine
        /// and opens a clean one on another, and the two outlines never reconcile.
        /// </summary>
        private void Forget(Portal portal)
        {
            bool held = false;

            for (int i = 0; i < portals.Length; i++)
            {
                if (portals[i] != portal) continue;

                portals[i] = null;
                held = true;
            }

            if (!held || !Network.Owns(this)) return;

            // The owner speaks for its own pair, on its own channel. Everyone else — including the
            // server — shuts on the announcement rather than on their own clock, so the pair has
            // one expiry instead of one per machine.
            this.NetToServer(NetMsg.PortalsUsed);
        }
```

- [ ] **Step 2: Stop peers expiring on their own clock**

In `PortalPair.Open`, hand peers a lifetime only for the iris animation, never for the close. Add beside the `SetLifetime` call:

```csharp
            // The owner's copy runs the clock; everybody else's waits to be told. Both still play
            // the iris, because that is cosmetic and wants to start on time — what changes is that
            // only one machine's timer is allowed to actually shut the aperture. See Forget.
            portal.SetLifetime(lifetime, expires: Network.Owns(this));
```

Then in `Portal.cs`, widen `SetLifetime`:

```csharp
        /// <summary>
        /// How long this aperture has, and whether running out actually shuts it.
        ///
        /// <paramref name="expires"/> is false on every machine but the shooter's. They all animate
        /// the iris closing on their own clock, which is what makes the warning read on time, but
        /// only the shooter's machine turns that into a close — and it announces the close, so the
        /// pair goes on every machine at the same moment instead of drifting apart by a round trip.
        /// </summary>
        public void SetLifetime(float seconds, bool expires = true)
        {
            lifetime = seconds;
            remaining = seconds;
            expiresLocally = expires;
        }
```

Declare the field beside `lifetime` and `remaining`:

```csharp
        /// <summary>Whether running out of time actually shuts this copy. See <see cref="SetLifetime"/>.</summary>
        private bool expiresLocally = true;
```

It defaults **true** so a portal placed in a scene by hand, or opened offline, behaves exactly as before — only `PortalPair.Open` ever passes false, and only on a machine that does not own the pair.

Then guard the tick that closes it (find the `Remaining <= 0f` branch in `Portal`'s `Update`) with `if (!expiresLocally) return;` before the `Close()` call, leaving the iris visual running.

- [ ] **Step 3: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS. `SprayedPortalTests` covers lifetime behaviour offline, where `Owns` is true and `expires` defaults true, so nothing single-player changes. `PortalPairSaveable.Reopen` passes a lifetime positionally — confirm it still compiles against the new optional parameter.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/Portals/PortalPair.cs Assets/Game/Scripts/Portals/Portal.cs
git commit -m "fix(portals): one expiry for the pair instead of one per machine"
```

---

### Task 12: An aperture conforms once its wall has streamed in

The bug: `ConformToSurface` and `GatherHostSurfaces` probe local physics at dab-landing time and never run again. A client far from the shooter has that chunk's colliders unloaded, so its copy is unconformed (sitting off the terrain) and has no host surfaces — meaning that when the client later travels there, their own body collides with the wall everyone else walks through.

**Files:**
- Modify: `Assets/Game/Scripts/Portals/Portal.cs`

- [ ] **Step 1: Retry while the answer is still empty**

Add to `Portal`:

```csharp
        /// <summary>
        /// How long an aperture keeps looking for the wall it was cut into.
        ///
        /// Long enough for a chunk to stream in around a player on the far side of the world, short
        /// enough that an aperture genuinely opened on nothing stops probing. A peer that received
        /// the placement while the geometry was unloaded found no surfaces at all and never looked
        /// again — so its copy kept the wall solid, and that player alone walked into a portal
        /// everybody else walked through.
        /// </summary>
        private const float ConformRetrySeconds = 30f;

        private float conformRetryUntil;

        /// <summary>Look for the wall again, for a while, if it was not there the first time.</summary>
        private void TickConformRetry()
        {
            if (Time.time > conformRetryUntil) return;
            if (HasHostSurface) { conformRetryUntil = 0f; return; }

            GatherHostSurfaces();
            ConformToSurface();
        }
```

Arm it wherever the portal is placed — at the end of `Place`, add `conformRetryUntil = Time.time + ConformRetrySeconds;` — and call `TickConformRetry();` from `Portal`'s `Update`.

`HasHostSurface` is whatever the existing field that `GatherHostSurfaces` fills reports as non-empty; expose it as a private bool property over that collection rather than inventing new state.

- [ ] **Step 2: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS. `SprayedPortalTests` places portals against colliders that exist immediately, so the retry never arms in tests.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Portals/Portal.cs
git commit -m "fix(portals): keep looking for the wall while the chunk is still streaming"
```

---

# Phase 4 — Both ends of a leash agree

---

### Task 13: The knot and the length travel, instead of being measured twice

The bug: `LeashEnd.TieTo` measures `LocalOffset` from a world hit point against the anchor's **locally interpolated** pose, and `FinishTie` measures the paid-out `Length` from the A–B distance at each machine's own `Present` moment. On a target moving 8 m/s, a 100 ms relay puts the knot ~1 m apart and the length ~1 m apart between machines — permanently, since both are fixed once tied. Every downstream disagreement (tension, standing stretch, break verdict) follows from these two numbers.

Both are free to send: `P` is already carrying the hit point and can carry a local offset instead, and `A` is unused on this message.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs:120-154`, `:193-250`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs:112-137`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs:149-197`

- [ ] **Step 1: Send the offset rather than the world point**

In `LeashArtifact.OnRequestUse`, replace the tail of the hit branch:

```csharp
            arg = arg.With(root);

            // The knot in the TARGET's local space, not in the world.
            //
            // Measured here because this is the only machine whose copy of a moving target is the
            // one the player actually clicked. A world point re-projected on each machine is
            // measured against that machine's interpolated pose a round trip later, which on
            // anything moving puts the knot on a different part of the animal — and the rope then
            // has a different shape, a different standing stretch and a different break verdict
            // everywhere. Bare geometry has no local space and keeps the world point; B says which.
            arg.P = root.transform.InverseTransformPoint(hit.point);
            arg.B = Hit;
```

The `PinTo` path (`arg.B != Hit`) is unchanged and keeps a world point, which is identical on every machine by definition.

- [ ] **Step 2: Send the length**

In `LeashArtifact.OnRequestUse`, still in the hit branch, add the paid-out length when this click completes a tie:

```csharp
            // Paid out here, once, on the machine that can see both ends now — rather than each
            // machine measuring the gap at its own Present moment and settling on a different
            // rope. Centimetres because NetArg has no float field, the same convention CraftLaunch
            // uses. Zero means "this click starts a rope rather than finishing one", and the
            // receiving side keeps the authored length.
            arg.A = held == null
                ? 0
                : Mathf.RoundToInt(Mathf.Min(Vector3.Distance(held.A.Position, hit.point) + payOutMargin,
                                             maxPaidOutLength) * 100f);
```

Note `arg.A` is free on this message — `UsableItem` uses `A` for the hotbar slot only on the *hold* stream, not on the press. Verify with `grep -n "arg.A" Assets/Game/Scripts/Items/Inventory/Components/EquipmentController.cs` before relying on it; if it is taken, move the length to `arg.R.x` and document the abuse the way `NetMsg.PackMove` documents its own.

- [ ] **Step 3: Take the offset and the length rather than measuring**

In `LeashEnd.cs`, change `TieTo` to accept a local offset:

```csharp
        /// <summary>
        /// Tie this end to a world object at a knot offset in that object's own local space.
        ///
        /// <para>
        /// An OFFSET rather than a world point, deliberately. The point was measured on the
        /// clicking machine and arrives here a round trip later, by which time a moving target has
        /// moved — so re-projecting it against this machine's pose puts the knot somewhere the
        /// player never clicked, and differently on every machine. See LeashArtifact.OnRequestUse.
        /// </para>
        /// </summary>
        public void TieTo(GameObject targetRoot, Vector3 localOffset, Leash rope)
        {
            Release(rope);

            NpcPassenger.UnseatRider(targetRoot);

            var body = targetRoot.GetComponentInParent<Rigidbody>();
            Transform root = body != null ? body.transform : targetRoot.transform;

            Kind = body != null ? LeashEndKind.Object : LeashEndKind.Static;
            Anchor = root;
            Body = body;
            Agent = root.GetComponentInParent<NavMeshAgent>();
            LocalOffset = localOffset;

            Attachable = LeashAttachable.GetOrAdd(root.gameObject);
            Attachable.AddLeash(rope);

            if (IsPlayer) LeashedBody.Ensure(root.gameObject);
        }
```

`RestoreOnto` already passes a local offset and now simply calls `TieTo(root, localOffset, rope)` with no `TransformPoint` round trip — delete the conversion it used to do.

- [ ] **Step 4: Take the length rather than measuring**

In `Leash.cs`, replace `TieHandEndOnto` and `PinHandEndAt` so the length arrives instead of being measured, and delete `PayOutTo`'s distance measurement:

```csharp
        /// <summary>
        /// Move whichever end is in a hand onto a world object — the second click of a tie.
        ///
        /// <paramref name="paidOutLength"/> is the rope's new length, decided once by the clicking
        /// machine and sent. Zero leaves the length alone. It is not measured here: each machine
        /// runs this at its own moment, and measuring gave a rope tied across a moving target a
        /// different length on every machine, for good.
        /// </summary>
        public void TieHandEndOnto(GameObject targetRoot, Vector3 localOffset, float paidOutLength)
        {
            LeashEnd hand = A.IsPlayer ? A : B.IsPlayer ? B : null;
            if (hand == null) return;

            hand.TieTo(targetRoot, localOffset, this);
            FinishTie(paidOutLength);
        }

        public void PinHandEndAt(Vector3 worldPoint, float paidOutLength)
        {
            LeashEnd hand = A.IsPlayer ? A : B.IsPlayer ? B : null;
            if (hand == null) return;

            hand.PinTo(worldPoint, this);
            FinishTie(paidOutLength);
        }

        private void FinishTie(float paidOutLength)
        {
            if (paidOutLength > Length) Length = paidOutLength;
            settings.rope?.Bite();
        }
```

Update `TieEndTo(bool isA, GameObject targetRoot, Vector3 localOffset)` to pass the offset through.

Then widen the artifact's own two helpers to carry the length, since every caller now has one:

```csharp
        private void TieTo(GameObject root, Vector3 localOffset, float paidOutLength)
        {
            if (held == null)
            {
                Hook(leash => leash.TieEndTo(true, root, localOffset));
                return;
            }

            // Tying a rope to the thing already on its other end would be a loop that does nothing.
            if (held.ReferencesObject(root)) return;

            held.TieHandEndOnto(root, localOffset, paidOutLength);
            held = null;
            Sfx.Play(SfxId.InteractLever, held != null ? held.A.Position : root.transform.position);
        }

        private void PinTo(Vector3 point, float paidOutLength)
        {
            if (held == null)
            {
                Hook(leash => leash.PinEndTo(true, point));
                return;
            }

            held.PinHandEndAt(point, paidOutLength);
            held = null;
            Sfx.Play(SfxId.InteractLever, point);
        }
```

Note the ordering trap in `TieTo`: `held` is nulled before the sound plays, so the position it plays at cannot be read from it. The line above resolves that; do not "simplify" it back.

Their call sites in `Present()` become `TieTo(root, arg.P, arg.A * 0.01f)` and `PinTo(arg.P, arg.A * 0.01f)`.

- [ ] **Step 5: Extend the tests**

Append to `Assets/Game/Editor/Tests/LeashConstraintTests.cs`:

```csharp
        [Test]
        public void AKnotIsAnOffsetSoItRidesAMovingAnchor()
        {
            var target = new GameObject("runner");
            target.transform.position = Vector3.zero;

            var rope = Leash.Create(new Leash.Settings { length = 8f, rope = new LeashRope() });
            rope.TieEndTo(true, target, new Vector3(0f, 1f, 0.5f));

            Vector3 before = rope.A.Position;
            target.transform.position = new Vector3(10f, 0f, 0f);

            Assert.AreEqual(before + new Vector3(10f, 0f, 0f), rope.A.Position,
                            "The knot rides the anchor, so every machine holds the same part of it.");

            rope.Dispose();
            Object.DestroyImmediate(target);
        }

        [Test]
        public void APaidOutLengthIsTakenNotMeasured()
        {
            var target = new GameObject("post");

            var rope = Leash.Create(new Leash.Settings { length = 8f, rope = new LeashRope() });
            rope.TieEndTo(true, target, Vector3.zero);
            rope.TieHandEndOnto(target, Vector3.zero, 12.5f);

            Assert.AreEqual(12.5f, rope.Length, 0.001f,
                            "The length the clicking machine decided is the length every machine uses.");

            rope.Dispose();
            Object.DestroyImmediate(target);
        }
```

- [ ] **Step 6: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS, including the existing `LeashConstraintTests` convergence tests and `LeashSaveable` round trips.

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs \
        Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs \
        Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs \
        Assets/Game/Editor/Tests/LeashConstraintTests.cs
git commit -m "fix(leash): the knot and the paid-out length travel instead of being measured twice"
```

---

### Task 14: An untie names a rope that is moving

The bug: an untie is addressed by a bare world point with a 1 m tolerance. On a target moving 8 m/s the rope has moved further than that by the time peers process the message, so `Leash.Nearest` finds nothing and the rope is removed on the clicking machine only — leaving the server constraining a creature nobody can see a rope on.

The fix is the one Task 13 just used: address the point in an anchor's local space, so it rides the thing it is attached to.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs` (`Nearest`)
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs` (`TryAimAtRope`, `UntieAt`)

- [ ] **Step 1: Search only the ropes touching one anchor**

Replace `Leash.Nearest`:

```csharp
        /// <summary>
        /// The rope tied to <paramref name="anchor"/> that passes closest to a point in that
        /// anchor's own local space.
        ///
        /// <para>
        /// Anchored rather than bare-world, because a bare world point names nothing once the thing
        /// it was clicked on starts moving: the click travels for a round trip, and a rope on an
        /// animal running at 8 m/s has left the tolerance by the time a peer looks. Resolved
        /// against the anchor instead, the point rides the animal and every machine finds the same
        /// rope. Narrowing the search to ropes that touch this anchor also removes most of the
        /// ambiguity the old bare-point search had.
        /// </para>
        /// <para>
        /// A null anchor keeps the old behaviour and searches every rope by world point, which is
        /// right for an end pinned to bare geometry — that point is the same on every machine.
        /// </para>
        /// </summary>
        public static Leash Nearest(Transform anchor, Vector3 point, float tolerance)
        {
            Vector3 world = anchor != null ? anchor.TransformPoint(point) : point;

            Leash best = null;
            float nearest = tolerance;

            for (int i = 0; i < LiveLeashes.Count; i++)
            {
                Leash rope = LiveLeashes[i];
                if (rope == null || rope.settings.rope == null) continue;
                if (anchor != null && !rope.Touches(anchor)) continue;

                float distance = rope.settings.rope.DistanceTo(world);
                if (distance > nearest) continue;

                nearest = distance;
                best = rope;
            }

            return best;
        }

        /// <summary>Whether either end of this rope is tied to <paramref name="anchor"/>.</summary>
        public bool Touches(Transform anchor) =>
            anchor != null && (A.Anchor == anchor || B.Anchor == anchor);
```

- [ ] **Step 2: Address the untie**

In `LeashArtifact.TryAimAtRope`, put the anchor in `Target` and the point in its space:

```csharp
            // Named relative to one of the rope's own anchors, so the point rides whatever the rope
            // is tied to — see Leash.Nearest. The nearer end is picked because it is the one the
            // player was looking at, and because a rope's two ends can be on objects moving in
            // different directions.
            Transform anchor = Vector3.SqrMagnitude(point - rope.A.Position) <=
                               Vector3.SqrMagnitude(point - rope.B.Position)
                ? rope.A.Anchor
                : rope.B.Anchor;

            arg = arg.With(anchor != null ? anchor.gameObject : null);
            arg.P = anchor != null ? anchor.InverseTransformPoint(point) : point;
            arg.B = Untie;
            return true;
```

In `Present()`, pass the resolved anchor through:

```csharp
            if (arg.B == Untie)
            {
                GameObject anchorObject = arg.Resolve();
                UntieAt(anchorObject != null ? anchorObject.transform : null, arg.P);
                return;
            }
```

and update `UntieAt`:

```csharp
        private void UntieAt(Transform anchor, Vector3 point)
        {
            Leash rope = Leash.Nearest(anchor, point, untieTolerance);
            if (rope == null) return;

            if (rope == held) held = null;

            rope.Dispose();
            Sfx.Play(SfxId.InteractDrop, rope.A.Position);
        }
```

- [ ] **Step 3: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS. Any existing test calling the two-argument `Leash.Nearest` fails to compile — update it to pass `null` as the anchor, which is the documented bare-geometry behaviour.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs \
        Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs
git commit -m "fix(leash): address an untie relative to an anchor so it survives a moving target"
```

---

### Task 15: A rope breaks everywhere or nowhere

The bug: `HasBroken` is evaluated per machine with no reconciliation. Task 13 removes the largest input divergence, but interpolation lag can still put two machines on opposite sides of `breakStretch` — and the disagreement is permanent: one machine keeps constraining a creature on a rope another machine no longer has, and which nobody can untie because it is not drawn there.

**Files:**
- Modify: `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs` (append id 85)
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs`

- [ ] **Step 1: Add the message id**

After `GrappleOff = 84`:

```csharp
        // Server → everyone: this rope has been pulled apart.
        //
        // On the channel of one of the rope's own ANCHORS, because a Leash is a bare GameObject
        // with no NetworkObject and therefore no relay of its own. Which anchor is in Target, and
        // P names the rope in that anchor's local space — the same addressing an untie uses, and
        // for the same reason: a bare world point stops naming a rope the moment the thing it is
        // tied to moves.
        //
        // The verdict is the server's alone. Every machine can compute the stretch, but they
        // compute it from interpolated endpoints and can land on opposite sides of the threshold —
        // and a rope that broke on one machine and not another is permanent, because the machine
        // that kept it goes on constraining a creature nobody else can see a rope on.
        public const ushort LeashSnap = 85; // server → everyone, on one of the rope's ANCHORS
```

- [ ] **Step 2: Make the verdict the server's, and announce it**

In `Leash.cs`, register the handler on whichever anchor the rope can address, and gate the break:

```csharp
        /// <summary>The anchor whose channel this rope's snap travels on, or null for a local rope.</summary>
        private Transform Channel =>
            NetArg.IdOf(A.Anchor != null ? A.Anchor.gameObject : null) != 0 ? A.Anchor
          : NetArg.IdOf(B.Anchor != null ? B.Anchor.gameObject : null) != 0 ? B.Anchor
          : null;

        private Transform listening;

        /// <summary>
        /// Keep the snap handler on an anchor that can carry it. Re-checked each step rather than
        /// registered once, because an end can be REPLACED — the hand end moving onto an object is
        /// the whole second half of a tie — and because an anchor's NetworkObject may spawn after
        /// the rope was built.
        /// </summary>
        private void RefreshChannel()
        {
            Transform wanted = Channel;
            if (wanted == listening) return;

            if (listening != null) listening.NetOff(NetMsg.LeashSnap, OnSnapAnnounced);

            listening = wanted;
            if (listening != null) listening.NetOn(NetMsg.LeashSnap, OnSnapAnnounced);
        }

        private void OnSnapAnnounced(in NetArg arg, ulong sender)
        {
            GameObject anchorObject = arg.Resolve();
            Transform anchor = anchorObject != null ? anchorObject.transform : null;

            // Addressed the way an untie is, so the same rope is picked on every machine.
            Leash rope = Nearest(anchor, arg.P, SnapTolerance);
            if (rope != null) rope.Snap();
        }

        /// <summary>How near the announced point a rope must pass to be the one that broke.</summary>
        private const float SnapTolerance = 1f;
```

Call `RefreshChannel();` at the top of `FixedUpdate`, and change the break to be decided once:

```csharp
            float stretch = MeasureStretch(out _);

            UpdateTension(stretch);

            // The verdict is the server's, or this machine's when there is nothing to disagree
            // with. A rope with no networked anchor is local to every machine anyway — see the
            // Channel property — so each is entitled to break its own copy.
            if ((Network.Server || !Network.IsNetworked || listening == null) && HasBroken(stretch))
                return;
```

and have `Snap()` announce before it disposes:

```csharp
        public void Snap()
        {
            // Announced before the teardown, while the anchors are still here to address it with.
            // Only from the machine that decided — everyone else reaches Snap FROM the
            // announcement, and a re-broadcast would be a loop.
            if (listening != null && (Network.Server || !Network.IsNetworked))
            {
                Transform anchor = listening;
                NetMessaging.NetSendTo(anchor.gameObject, NetMsg.LeashSnap,
                                       new NetArg { P = anchor.InverseTransformPoint(A.Position) }
                                           .With(anchor.gameObject),
                                       NetTo.All);
            }

            Sfx.Play(SfxId.ImpactMetal, A.Position);
            Dispose();
        }
```

`Dispose` is already idempotent (`disposed`), which is what makes the host running the send and the inline broadcast safe.

Add `OnDisable` cleanup: `if (listening != null) listening.NetOff(NetMsg.LeashSnap, OnSnapAnnounced); listening = null;`

- [ ] **Step 3: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS, including `NetMessagingTests` and `LeashConstraintTests`. Offline, `Network.IsNetworked` is false so every rope still breaks locally, which is what the existing break tests assert.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs \
        Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs
git commit -m "fix(leash): the server decides a break and announces it"
```

---

### Task 16: A rope to an unnetworked prop stays local, as documented

The bug: `OnRequestUse`'s doc says ropes to a prop nobody networked "stay local". The code sends anyway — `NetArg.Target` is 0 and `localTarget` never crosses the wire, so peers `Resolve()` null and fall into `PinTo(arg.P)`. They get a rope anchored to a phantom static point while the clicking machine's rope follows the crate.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs`

- [ ] **Step 1: Give the local case its own verb**

Add beside the other verbs:

```csharp
        private const int Miss = 0;
        private const int Hit = 1;
        private const int Untie = 2;

        /// <summary>
        /// A tie to something with no networked identity — a loose prop nobody opted into.
        ///
        /// Presented only on the machine that clicked. The alternative is what this replaces: the
        /// id is 0 on the wire, so every peer resolved null and pinned the rope to a phantom point
        /// where the prop had been, which is worse than no rope at all — the two machines then
        /// disagree about the rope's shape, its break verdict and its identity for an untie. Such a
        /// prop's physics already differs per machine, so a shared rope to it could never have been
        /// made to agree.
        /// </summary>
        private const int HitLocal = 3;
```

In `OnRequestUse`, choose between them:

```csharp
            arg = arg.With(root);
            arg.P = root.transform.InverseTransformPoint(hit.point);
            arg.B = arg.Target != 0 || !Network.IsNetworked ? Hit : HitLocal;
```

- [ ] **Step 2: Honour it in Present**

```csharp
            // A local tie is the clicking machine's business alone. Every other machine has no
            // identity to resolve and would pin the rope to thin air.
            if (arg.B == HitLocal)
            {
                if (!OwnerIsLocal()) return;

                GameObject local = arg.Resolve();
                if (local != null) TieTo(local, arg.P, arg.A * 0.01f);
                return;
            }
```

Placed above the existing `if (arg.B != Hit) { DropHeld(); return; }`, so a `HitLocal` is not mistaken for a click at nothing.

- [ ] **Step 3: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS. Offline `Network.IsNetworked` is false, so every tie is a plain `Hit` and single-player behaviour is byte-for-byte what it was.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs
git commit -m "fix(leash): a rope to an unnetworked prop stays local instead of pinning phantoms"
```

---

# Phase 5 — Late join and load

Four systems have the same hole: their state exists only because a machine heard an event. A joiner heard nothing, and a loaded world was restored on the host alone. One channel serves all of them, built on the savers that already describe exactly this state.

---

### Task 17: Expose the global savers by key

**Files:**
- Modify: `Assets/Game/Scripts/Core/Persistence/Runtime/SaveManager.cs`

- [ ] **Step 1: Add the two accessors**

Beside `RegisterGlobalSaver`:

```csharp
        /// <summary>
        /// Capture one global saver's state by key, or false when nothing is registered under it.
        ///
        /// <para>
        /// Exposed for <c>SessionSnapshot</c>, which hands a joining client the world state that
        /// only ever travelled as events. That state is exactly what these savers already describe
        /// — a rope tied between two things, an aperture in a wall — so the snapshot reuses them
        /// rather than growing a second description of the same thing that could drift from it.
        /// </para>
        /// </summary>
        public static bool TryCaptureGlobal(string key, out object state)
        {
            state = null;
            if (string.IsNullOrEmpty(key)) return false;

            foreach (ISaveable saver in GlobalSavers)
            {
                if (saver == null || saver.SaveKey != key) continue;

                state = saver.CaptureState();
                return true;
            }

            return false;
        }

        /// <summary>Apply one global saver's state by key. See <see cref="TryCaptureGlobal"/>.</summary>
        public static void RestoreGlobal(string key, JObject state)
        {
            if (string.IsNullOrEmpty(key)) return;

            foreach (ISaveable saver in new List<ISaveable>(GlobalSavers))
            {
                if (saver == null || saver.SaveKey != key) continue;

                try
                {
                    saver.RestoreState(state);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Save] Global saver '{key}' failed to restore: {e}");
                }
            }
        }
```

- [ ] **Step 2: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Core/Persistence/Runtime/SaveManager.cs
git commit -m "feat(save): capture and restore a single global saver by key"
```

---

### Task 18: Hand a joining client the state events never carried

**Files:**
- Create: `Assets/Game/Scripts/Core/Multiplayer/SessionSnapshot.cs`
- Test: `Assets/Game/Editor/Tests/SessionSnapshotTests.cs` (create)
- Modify: `Assets/Game/Prefabs/Systems/NetworkGameManager.prefab` (add the component)

- [ ] **Step 1: Write the failing test**

Create `Assets/Game/Editor/Tests/SessionSnapshotTests.cs`:

```csharp
// What a joining client is owed, and what it is not.
//
// The snapshot is deliberately a WHITELIST rather than the whole save document. A joiner needs the
// world state that only ever travelled as an event and therefore never reached them — ropes,
// apertures. It must never be handed entity records, inventories or player positions: those already
// replicate, and applying a second copy of them on a running client is how you duplicate a creature
// or teleport somebody into last week.
using NUnit.Framework;
using SpaceGame.Core;

namespace SpaceGame.EditorTools
{
    public class SessionSnapshotTests
    {
        [Test]
        public void OnlyEventOnlyStateIsCarried()
        {
            CollectionAssert.AreEquivalent(
                new[] { "leashes", "portals" },
                SessionSnapshot.CarriedKeys,
                "The snapshot carries the systems whose state never replicates, and nothing else.");
        }

        [Test]
        public void EntityAndPlayerStateAreNeverCarried()
        {
            foreach (string forbidden in new[] { "entities", "players", "inventory", "world" })
                CollectionAssert.DoesNotContain(SessionSnapshot.CarriedKeys, forbidden,
                    $"'{forbidden}' already replicates — carrying it would apply it twice.");
        }

        [Test]
        public void AnEmptySnapshotIsNotSent()
        {
            Assert.IsNull(SessionSnapshot.BuildPayload(),
                          "With nothing tied and nothing open there is nothing to send.");
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: FAIL to compile — `SessionSnapshot` does not exist.

- [ ] **Step 3: Write the component**

Create `Assets/Game/Scripts/Core/Multiplayer/SessionSnapshot.cs`:

```csharp
// The state a joining client was never told about.
//
// Most gameplay state replicates on its own: a NetworkVariable has a current value a joiner reads
// at spawn, and a NetworkTransform publishes a pose. But a system whose state exists only because
// every machine ran the same Present() has nothing for a joiner to read — they were not there for
// the event, and events do not replay. Four systems on this branch are in that position, and the
// symptom is the same for all of them: a rope holding nothing, a player teleporting through a solid
// wall, a creature straining against an invisible force.
//
// The obvious fix — a bespoke "tell me your state" round trip per system — is four protocols to
// write and keep in step. The state is already described, once, by the SAVERS: a leash record is
// exactly what a joiner needs to rebuild the rope, because rebuilding ropes from a record is what
// LeashSaveable does. So this sends those records, and applies them through the same RestoreState
// the load path uses.
//
// It cannot ride NetMessaging: NetArg has no string field, and this needs to reach ONE client
// rather than all of them. That is the same pair of reasons ChatNetwork gives, and this follows it
// — a NetworkBehaviour on the object that already exists once per session and spawns before any
// player does.
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Core
{
    /// <summary>
    /// Hands each joining client the world state that only ever travelled as an event.
    ///
    /// <para>
    /// Lives on the <c>NetworkGameManager</c> prefab, beside <see cref="ChatNetwork"/> and for the
    /// same reasons: that object carries a <see cref="NetworkObject"/>, sits in the persistent
    /// scene loaded beneath every gameplay scene, and is spawned on every peer before the first
    /// player is.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class SessionSnapshot : NetworkBehaviour
    {
        /// <summary>
        /// The save keys a joiner is sent, and deliberately a short whitelist.
        ///
        /// <para>
        /// Every one of these describes state that has no other way of reaching a machine that was
        /// not listening when it happened. Nothing that already replicates belongs here: entity
        /// records, inventories and player poses all arrive on their own, and applying a second
        /// copy of them to a running client duplicates creatures and teleports people.
        /// </para>
        /// <para>
        /// A new system joins this list by having a saver and adding its key — which is the whole
        /// point of building it this way rather than four times.
        /// </para>
        /// </summary>
        public static readonly string[] CarriedKeys =
        {
            LeashSaveable.Key,
            PortalPairSaveable.Key,
        };

        /// <summary>
        /// Everything a joiner is owed, as JSON, or null when there is nothing to send.
        ///
        /// <para>
        /// Static and free of any session state so the whitelist and the empty case are provable
        /// without a NetworkManager. A <see cref="StateBag"/> rather than a bespoke struct because
        /// that is what the savers already write into and what <c>RestoreState</c> already reads.
        /// </para>
        /// </summary>
        public static string BuildPayload()
        {
            var bag = new StateBag();
            bool any = false;

            foreach (string key in CarriedKeys)
            {
                if (!SaveManager.TryCaptureGlobal(key, out object state) || state == null) continue;

                bag.Set(key, state);
                any = true;
            }

            // Nothing tied and nothing open. Sending an empty bag would cost a round trip to say so
            // and would make every joiner run a restore that clears state they never had.
            return any ? JsonConvert.SerializeObject(bag, SaveSerializer.Settings) : null;
        }

        /// <summary>Apply a payload built by <see cref="BuildPayload"/>. Safe on an empty string.</summary>
        public static void ApplyPayload(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;

            StateBag bag = JsonConvert.DeserializeObject<StateBag>(payload, SaveSerializer.Settings);
            if (bag == null) return;

            foreach (string key in CarriedKeys)
            {
                if (!bag.TryGetRaw(key, out JObject state)) continue;

                SaveManager.RestoreGlobal(key, state);
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;

            manager.OnClientConnectedCallback += OnClientConnected;
        }

        public override void OnNetworkDespawn()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null) manager.OnClientConnectedCallback -= OnClientConnected;
        }

        /// <summary>
        /// Server-side. Somebody has joined; tell them what they missed.
        ///
        /// <para>
        /// The host's own connection is skipped: it has been here the whole time, and restoring
        /// over its own live state would dispose every rope and re-tie it.
        /// </para>
        /// </summary>
        private void OnClientConnected(ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId) return;

            string payload = BuildPayload();
            if (string.IsNullOrEmpty(payload)) return;

            SnapshotRpc(payload, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        /// <summary>Server → one client. The unicast NetMessaging has no direction for.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void SnapshotRpc(string payload, RpcParams rpcParams = default) =>
            ApplyPayload(payload);
    }
}
```

- [ ] **Step 4: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS — all three `SessionSnapshotTests`. `AnEmptySnapshotIsNotSent` passes because no savers are registered in an EditMode run.

- [ ] **Step 5: Put the component on the prefab**

Add `SessionSnapshot` to `Assets/Game/Prefabs/Systems/NetworkGameManager.prefab`, on the same GameObject as `ChatNetwork`. Then confirm it saved — a prefab edit that the AssetDatabase silently discarded is a known trap in this project:

```bash
grep -c "SessionSnapshot" Assets/Game/Prefabs/Systems/NetworkGameManager.prefab
```

Expected: `1` or more. A `0` means the write was discarded; re-open the prefab and re-apply.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/SessionSnapshot.cs \
        Assets/Game/Editor/Tests/SessionSnapshotTests.cs \
        Assets/Game/Prefabs/Systems/NetworkGameManager.prefab
git commit -m "feat(net): hand joining clients the state that only travelled as events"
```

---

### Task 19: Make the portal saver reachable as a global key

`LeashSaveable` is already a global saver, so Task 18 covers ropes. `PortalPairSaveable` is per-player, so `TryCaptureGlobal("portals", …)` finds nothing and a joiner still gets no portals.

**Files:**
- Modify: `Assets/Game/Scripts/Core/Persistence/Adapters/PortalPairSaveable.cs`

- [ ] **Step 1: Add a session-wide view over the per-player savers**

Add a small global companion inside the same file, registered once:

```csharp
    /// <summary>
    /// Every player's portals, as one record.
    ///
    /// <para>
    /// <see cref="PortalPairSaveable"/> stays per-player — a pair belongs to the shooter, and that
    /// is what keeps two players' portals independent. But a joining client needs everyone's, and
    /// <c>SessionSnapshot</c> reads global savers by key. So this is a view, not a second store: it
    /// captures by walking the live per-player savers and restores by handing each record back to
    /// the shooter it belongs to.
    /// </para>
    /// <para>
    /// Keyed by the shooter's <see cref="SaveRef"/> rather than by client id, for the reason the
    /// whole persistence layer prefers identity: a client id is a property of one connection and
    /// names a different person next session.
    /// </para>
    /// </summary>
    public class SessionPortalsSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "portals";  // shared with PortalPairSaveable — never rename

        public string SaveKey => Key;

        public struct Entry
        {
            public SaveRef shooter;
            public JObject portals;
        }

        public struct State
        {
            public List<Entry> shooters;
        }

        public object CaptureState()
        {
            var entries = new List<Entry>();

            foreach (PortalPairSaveable saver in FindObjectsByType<PortalPairSaveable>(
                         FindObjectsSortMode.None))
            {
                if (saver == null) continue;

                object captured = saver.CaptureState();
                if (captured == null) continue;

                SaveRef shooter = SaveRef.From(saver.gameObject);
                if (!shooter.IsSet) continue;

                entries.Add(new Entry
                {
                    shooter = shooter,
                    portals = JObject.FromObject(captured, SaveSerializer.Serializer),
                });
            }

            return entries.Count == 0 ? null : new State { shooters = entries };
        }

        public void RestoreState(JObject state)
        {
            if (state == null) return;

            List<Entry> entries = state.ToObject<State>(SaveSerializer.Serializer).shooters;
            if (entries == null) return;

            foreach (Entry entry in entries)
            {
                if (!entry.shooter.TryResolve(out GameObject shooter)) continue;
                if (!shooter.TryGetComponent(out PortalPairSaveable saver)) continue;

                saver.RestoreState(entry.portals);
            }
        }

        private void OnEnable() => SaveManager.RegisterGlobalSaver(this);

        private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);
    }
```

- [ ] **Step 2: Put it in the session**

Add `SessionPortalsSaveable` to the same GameObject as `SessionSnapshot` on `Assets/Game/Prefabs/Systems/NetworkGameManager.prefab`, and verify the write landed:

```bash
grep -c "SessionPortalsSaveable" Assets/Game/Prefabs/Systems/NetworkGameManager.prefab
```

Expected: `1` or more.

- [ ] **Step 3: Check for a key collision**

`PortalPairSaveable.Key` and `SessionPortalsSaveable.Key` are both `"portals"`, but they live in different scopes — one in the per-player bag, one in the global bag — so they do not collide. Confirm the save document keeps them apart:

```bash
grep -n "Global\|Players" Assets/Game/Scripts/Core/Persistence/Format/SaveDocument.cs
```

Expected: two distinct bags. If they share one, rename the global view's key to `"portals.session"` and update `SessionSnapshot.CarriedKeys` and `SessionSnapshotTests` to match.

- [ ] **Step 4: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Persistence/Adapters/PortalPairSaveable.cs \
        Assets/Game/Prefabs/Systems/NetworkGameManager.prefab
git commit -m "feat(portals): a session-wide view so joiners and loads get everyone's apertures"
```

---

### Task 20: A rope and a lasso re-announce themselves on join

Grapple and lasso state is small and absolute, and both already have a broadcast verb that is idempotent. They do not need the snapshot — they need to be said again.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs`

- [ ] **Step 1: Re-announce the catch when somebody joins**

In `LassoArtifact.cs`, subscribe on the authority only:

```csharp
        /// <summary>
        /// Somebody joined. If this rope is on a creature, say so again.
        ///
        /// <para>
        /// LassoVerb.Caught is an absolute state rather than an edge — it says "this creature is
        /// roped", not "this creature was just roped" — so re-sending it costs a joiner one
        /// idempotent Attach and costs everybody else a no-op. Without it a joiner sees the creature
        /// struggling under an invisible force with no rope on it, which is precisely the symptom
        /// this whole rework was written to remove.
        /// </para>
        /// <para>
        /// Only from the authority, because a broadcast from a client is refused, and only for a
        /// rope that is actually out.
        /// </para>
        /// </summary>
        private void OnPeerJoined(ulong clientId)
        {
            if (!IsAuthority || !_isLassoed) return;
            if (clientId == Unity.Netcode.NetworkManager.ServerClientId) return;

            Transform root = _targetRb != null ? _targetRb.transform : _targetTransform;
            if (root == null) return;

            SendRope(LassoVerb.Caught, root.gameObject);
        }
```

Hook it in `Listen`, so it lives and dies with the channel registration that is already there:

```csharp
            var manager = Unity.Netcode.NetworkManager.Singleton;

            if (_channel != null)
            {
                _channel.NetOff(NetMsg.LassoRope, OnRopeRequested);
                _channel.NetOff(NetMsg.LassoRoped, OnRopeAnnounced);
                if (manager != null) manager.OnClientConnectedCallback -= OnPeerJoined;
            }

            _channel = channel;
            if (_channel == null) return;

            _channel.NetOn(NetMsg.LassoRope, OnRopeRequested);
            _channel.NetOn(NetMsg.LassoRoped, OnRopeAnnounced);
            if (manager != null) manager.OnClientConnectedCallback += OnPeerJoined;
```

- [ ] **Step 2: Send the rope's length with the catch**

The restore path already diverges here — `TryCompleteRestore` applies the saved length on the authority only, while every other machine recomputes it from the current gap. Carry it instead, in the field that is free:

In `SendRope`, add the length:

```csharp
        /// <summary>Owner-side: tell the session what the rope just did.</summary>
        private void SendRope(int verb, GameObject subject)
        {
            if (owner == null) return;

            // A carries the rope's length in centimetres — NetArg has no float field, the same
            // convention CraftLaunch uses. It has to travel: after a save-restore the authority
            // holds a length the player reeled to, while every other machine would recompute one
            // from wherever the two ends happen to be standing now, and the two never reconcile.
            // Zero means "work it out yourself", which is what a fresh catch sends.
            NetMessaging.NetSendTo(owner, NetMsg.LassoRope,
                new NetArg
                {
                    B = verb,
                    A = _isLassoed ? Mathf.RoundToInt(_currentRopeLength * 100f) : 0,
                }.With(subject), NetTo.Server);
        }
```

and honour it in `ApplyRope`:

```csharp
                case LassoVerb.Caught:
                    GameObject caught = arg.Resolve();
                    if (caught == null) return;

                    Attach(caught.GetComponentInParent<Rigidbody>(), caught.transform);

                    // After Attach, which sets a length of its own from the gap it can see. An
                    // announced length is the better answer where there is one — see SendRope.
                    if (arg.A > 0 && _isLassoed)
                    {
                        _currentRopeLength = arg.A * 0.01f;
                        _tether?.SetRopeLength(_currentRopeLength);
                        _caughtPlayer?.SetRopeLength(_currentRopeLength);
                    }
                    return;
```

`TryCompleteRestore` can now drop its own post-`Attach` length assignment: the `SendRope` it already makes carries it.

- [ ] **Step 3: Re-announce a grapple that is still out**

In `GrapplingHookArtifact.cs`, the same shape on the channel Task 8 gave it:

```csharp
        /// <summary>
        /// Somebody joined. If this rope is still out, say so — see GrappleVerb.On.
        ///
        /// From the authority only: a client may not broadcast, and the server has its own copy of
        /// every equipped item, so it is in a position to answer for a rope any player has out.
        /// </summary>
        private void OnPeerJoined(ulong clientId)
        {
            if (!Network.Server || !_isGrappling || owner == null) return;
            if (clientId == Unity.Netcode.NetworkManager.ServerClientId) return;

            NetMessaging.NetSendTo(owner, NetMsg.GrappleRope, new NetArg
            {
                A = GrappleVerb.On,
                P = _hookPoint,
                R = Quaternion.LookRotation(_hitNormal),
            }.With(_anchorObject), NetTo.All);
        }
```

`_anchorObject` is whatever `BindAttach` already stores as the thing the rope is tied to; if it keeps only a `Transform`, pass its `gameObject`. Hook and unhook it in `Listen` alongside the channel registration, exactly as the lasso does in Step 1 above:

```csharp
            var manager = Unity.Netcode.NetworkManager.Singleton;

            if (_channel != null)
            {
                _channel.NetOff(NetMsg.GrappleRope, OnRopeMessage);
                if (manager != null) manager.OnClientConnectedCallback -= OnPeerJoined;
            }

            _channel = channel;
            if (_channel == null) return;

            _channel.NetOn(NetMsg.GrappleRope, OnRopeMessage);
            if (manager != null) manager.OnClientConnectedCallback += OnPeerJoined;
```

- [ ] **Step 4: Run the tests**

Run `Tools/Tests/Run EditMode Tests (headless)`.
Expected: PASS, including `LassoTests`' restore round trips and `GrappleUseFlowTests`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Gadgets/LassoArtifact.cs \
        Assets/Game/Scripts/Items/Artifacts/Gadgets/GrapplingHookArtifact.cs
git commit -m "fix(ropes): re-announce a live rope on join, and carry the lasso's length"
```

---

# Phase 6 — Proof

Host-only testing proves nothing here: every bug in this plan is invisible when you host. This phase is the only evidence that any of it works.

---

### Task 21: Run the static guards

**Files:**
- Test: all of `Assets/Game/Editor/Tests/`

- [ ] **Step 1: Run the whole EditMode suite**

Run `Tools/Tests/Run EditMode Tests (headless)` from the Unity Editor and read `Temp/headless_tests.txt`.

Expected: 0 failures. The suites that guard this plan specifically:
- `NetMessagingTests` — ids 83/84/85 unique and non-zero, `NetArg` round-trips
- `NetworkPrefabRegistrationTests` — every `NetworkObject` prefab registered, no null entries
- `NetAuthorityAndDamageTests`
- `BodyOwnershipTests`, `HoldLatchTests`, `SessionSnapshotTests` (new)
- `LeashConstraintTests`, `LassoTests`, `SprayedPortalTests`, `GrappleSwingTests`, `GrappleUseFlowTests`

- [ ] **Step 2: Re-sync the network prefab list**

Run `Tools/SpaceGame/Multiplayer/Sync Network Prefabs`, then check nothing ships a zero hash:

```bash
grep -rn "GlobalObjectIdHash: 0$" Assets/Game/Prefabs/Items/Artifacts/ | grep -v InScenePlaced
```

Expected: no output. A script-created `NetworkObject` ships hash `0`, and duplicate zeroes make NGO silently drop all but one prefab.

- [ ] **Step 3: Commit any list changes**

```bash
git add Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset
git commit -m "chore: re-sync network prefab list"
```

---

### Task 22: Prove it on a real client

**Files:**
- Modify: `Assets/Game/Scripts/Core/Multiplayer/MultiplayerAutotest.cs`

- [ ] **Step 1: Report the things this plan fixed**

Extend `MultiplayerAutotest.RunClient` with reports for the state a client should now have. Follow the existing `Report(key, value)` shape exactly — do not invent a second harness.

```csharp
            // Phase 5: a joiner is handed the ropes and apertures it was never told about.
            Report("CLIENT_LEASHES_SEEN", SpaceGame.Items.Leash.All.Count);

            int aperturesSeen = 0;
            foreach (SpaceGame.Portals.Portal portal in
                     Object.FindObjectsByType<SpaceGame.Portals.Portal>(FindObjectsSortMode.None))
                if (portal != null) aperturesSeen++;

            Report("CLIENT_PORTALS_SEEN", aperturesSeen);
```

And the matching host-side counts in `RunHost`, so the two can be compared:

```csharp
            Report("HOST_LEASHES", SpaceGame.Items.Leash.All.Count);
```

- [ ] **Step 2: Build the test player**

Run `Tools/Tests/Build Multiplayer Test Player` from the Unity Editor.

- [ ] **Step 3: Run both processes**

```bash
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode host   -logFile /tmp/mp_host.log &
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode client -logFile /tmp/mp_client.log &
sleep 60
grep '\[MPTEST\]' /tmp/mp_host.log /tmp/mp_client.log
```

Required, all of them:
- `HOST_CLIENTS=2`
- `CLIENT_SPAWNED > 0`
- `CLIENT_PLAYER_OBJECT=True`
- `CLIENT_SUPPRESSED == CLIENT_AUTHORITIES`
- `CLIENT_HEALTH_SEEN == HOST_HEALTH_AFTER`
- `HOST_RELAY_FROM_CLIENT=1`
- `CLIENT_LEASHES_SEEN == HOST_LEASHES` — **new, and the point of Phase 5**

- [ ] **Step 4: Play-test the four high-severity cases by hand**

The autotest cannot press buttons. Launch a build against the editor with its own services profile — without it both sign in as the same anonymous PlayerId and the lobby refuses the second as already a member:

```bash
open "<app>" --args -sgprofile client
```

Then walk these, each of which was broken before this plan and each of which needs a **client**, never the host, to be the actor:

1. **Client lassoes another client.** The victim is held and dragged. (Before: nothing happened unless the victim was the host.)
2. **Client leashes a mount another client is riding.** The rope holds. (Before: inert.)
3. **Client joins while a rope is tied and portals are open.** Both are there. (Before: neither.)
4. **A client stands near another client's portal without going through it.** The pair survives. (Before: it vanished for everyone.)
5. **Client taps the grapple at a wall 40 m away.** It reels in, and their next throw is visible to the host. (Before: the peer's rope hung forever and ate the next throw.)

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/MultiplayerAutotest.cs
git commit -m "test: report client-side rope and aperture counts in the two-process run"
```

---

## What actually happened when this was executed (2026-08-25)

The plan was implemented in full. Four things turned out differently from what is written above;
they are recorded here rather than edited into the tasks, so the reasoning stays checkable.

1. **Task 13's `arg.A` is not free.** `EquipmentController`'s stale-slot guard reads it as the
   hotbar slot on the press message. The paid-out length went into `arg.B` instead, packed above
   the verb in its low byte — the same byte packing `NetMsg.PackMove` uses.

2. **Task 11 as written would have been worse than the bug.** Routing a single aperture's expiry
   through `PortalsUsed`/`PortalsShut` shuts BOTH ends — those two are payload-free precisely
   because a traversal does — so one aperture timing out would have destroyed its partner
   everywhere, against the "an EMPTY barrel always wins" rule `PeekBarrel` exists for. It became two
   things instead: a `RemoteExpiryGrace` on peers so none of them can expire ahead of the shooter,
   and a new `PortalExpired`/`PortalGone` (86/87) pair that names a single barrel.

3. **Task 18's whole premise was wrong, and this is the important one.** The plan reuses the savers
   because they already describe the state — but they address their subjects with a `SaveRef`, and
   **a `SaveRef` does not resolve on a client**. `SaveRefBinder` says so itself and logs a warning
   when anything tries: the entity registry is filled by hydration and the player table by a server
   RPC handler, both server-only. A snapshot built from `SaveRef`s captures correctly, travels
   correctly, and then silently resolves to nothing on the one machine it exists for. `SessionSnapshot`
   now addresses everything by `NetworkObjectId`, which is the one name both machines already agree
   on and is valid for exactly as long as the session it serves. `PortalPairSaveable` also only
   *stages* in `RestoreState` — the aperture is cut in `OnLoadComplete`, from a deferred pass a
   joining client never runs — so it gained `ApplyNow`. Task 17's `SaveManager` accessors are
   unused under this design and were reverted rather than left as dead API.

4. **Task 15 introduced unbounded recursion and it was caught in review, not by a test.**
   `Leash.Snap` broadcast before setting `disposed`, and `NetTo.All` dispatches inline on the host —
   so the handler resolved the same rope by its point and called `Snap` again, for ever. The flag is
   now claimed before the send, with `Teardown` shared by `Snap` and `Dispose`.

**Verification actually achieved.** Both assemblies type-check clean, and the full EditMode suite
was run in the Editor: **1567 passed, 12 failed, 1 skipped.**

All 12 failures are pre-existing and in code this work never touched — the backpack placement suites
(uv `(0.39, 0.27)` against an expected `(0.40, 0.30)`), `LassoTests.RopeStaysSmoothWhileBeingThrown`
(`LassoRope.cs`, unmodified this session), and the mount/wing-pack "rider should have been seated"
group. Verified by checking that no file those tests exercise appears in this change's edit list.

The first run found **24** failures, 12 of them genuinely introduced here. Fixing them turned up
three real defects that type-checking could never have caught, which is the argument for having run
them at all:

- **`WinchLatch.Bite` read `held` instead of `WinchAtBite`** — so a tapped grapple still caught and
  hung, the exact bug the latch was written to remove. `GrappleSwingTests.ATappedGrappleStillReelsIn`,
  which already existed, is what named it.
- **`LassoedBody` never resolved its Rigidbody**, because Unity does not raise `Awake` for a runtime
  `AddComponent`. The constraint silently held nothing. Now resolved lazily.
- **`Leash` destroyed its GameObject with `Destroy`**, which is refused outside play mode — so ropes
  survived teardown in the editor and in every test that disposed one. Now goes through
  `Leash.Remove`, which picks `DestroyImmediate` when not playing.

Two tests were also written wrong and were fixed rather than deleted: the leash length tests called
`TieHandEndOnto` with no end actually in a hand (so it returned early and asserted against untouched
state), and `HoldLatchTests` compared a tap against a bite-with-no-release, which are not the two
orderings the latch exists to distinguish.

One thing was measured and then deliberately left untested: `Leash.All` is filled by `OnEnable`,
which does not run for a runtime `AddComponent` outside play mode, so `SessionSnapshot.BuildPayload`
can only ever see an empty registry in an EditMode test. Asserting that a rope travels would have
pinned the empty case while claiming to pin the populated one. `SessionSnapshotTests` covers the
empty case honestly and says why; the populated path belongs to the two-process run.

**Still outstanding:** Task 22, the two-process run. Nothing here has been observed working on a
real client, which for this change set is the verification that actually counts.

---

## Notes for whoever executes this

**Order matters between phases, not much within them.** Phase 1 changes what `ResolvedHere` and `OwnsTarget` mean, and Phase 4 changes what `TieTo` takes; doing Phase 4 first means writing the offset plumbing twice. Phases 2, 3 and 5 are independent of each other.

**What this plan deliberately does not do.** Each of these is a real review finding, left out with a reason rather than missed.

- **The sprayed portal *outline* for a late joiner.** `SessionSnapshot` carries the dab list through `PortalPairSaveable`, so it should agree — but the dabs are re-conformed against local geometry on arrival, and a joiner whose chunk has not streamed in gets Task 12's retry rather than an exact match. If play-testing shows the outlines differ enough to matter, the next step is to send the conform result rather than re-deriving it, not to send more dabs.
- **A dropped use-relay forking rope state** (`EquipmentController.cs:548`). The stale-slot guard returns before `NetToOthers`, but the owner already ran `Present` — so a client who clicks to tie and immediately scrolls the hotbar can end up with a rope no other machine will ever have. It is a real fork, but the fix is in `EquipmentController` and changes the contract for *every* item, not just the leash: either the owner defers its `Present` until the relay is accepted (which puts a round trip inside the feel of every artifact) or the guard sends a correction. That is its own change, with its own play-testing.
- **Two players tying and untying the same rope in the same instant.** The sender presents immediately and everybody else presents in server-relay order, so the two orderings can fork the per-machine `held` state. Inherent to optimistic presentation; the same trade-off `EquipmentController` makes everywhere. Tasks 13–15 remove the *silent* half of this (a rope that exists on one machine and not another now converges through the snapshot on the next join, and breaks through one verdict), which is the part that mattered.
- **An unnetworked Rigidbody consuming a portal** (`Networking.cs:54-62`). `Network.Owns` answers true on every machine for an object with no spawned `NetworkObject`, so a runtime-built ragdoll piece still passes Task 10's gate and can shut a pair one machine saw it enter. Handlers are idempotent so state converges — a pair can be consumed early, never left inconsistent. Fixing it means deciding whether unnetworked debris should be able to use portals at all, which is a design question, not a netcode one.
- **The cosmetic gadget findings** — ghost blasts at the cooldown edge, the repulsor's remote ammo readout, seam interpolation through a portal — and **the two low-severity backpack findings**. None of them diverge state. They are polish, and they belong in a change where they can be judged on feel.

**If a task's code does not match the file you find.** The working tree has moved since the review — `PackDragController` is now `PackHandController` and `NetMsg.PackDrop` is retired. Read the current file before editing, and prefer the surrounding code's shape over this plan's snippet where the two disagree. The reasoning in each task is the part that matters; the exact lines are a starting point.
