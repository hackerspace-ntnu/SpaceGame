# The Crucible Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A two-player puzzle room where both players leash the same power cell, thread their ropes through long slots in the pit rim, and fly the cell through a maze of chimneys to a socket — with the lava below as the only fail state, and a plain floor instead of lava when only one player is present.

**Architecture:** A `LeashRail` is an authored segment whose bend point is a closed-form solve, so the rope's shape stays identical on every machine with nothing on the wire. Rails are not a parallel system: a rail-bound waypoint is an ordinary `LeashWrap` that recomputes its position each step instead of staying frozen. The carrier is a server-owned networked rigidbody, so both ropes' carrier-ends resolve on one machine under the leash's existing ownership rule.

**Tech Stack:** Unity 6000.3, C# 9, Netcode for GameObjects (`NetworkVariable`, server-authoritative `NetworkTransform`), the project's `ISaveable`/`SaveableEntity` persistence, NUnit EditMode tests.

**Spec:** [docs/superpowers/specs/2026-09-05-crucible-puzzle-design.md](../specs/2026-09-05-crucible-puzzle-design.md)

**Blocked on:** [2026-09-05-leash-rope-collision.md](2026-09-05-leash-rope-collision.md) must land first. Task 2 here edits `LeashPath` and `LeashWrap`, which that plan creates.

> **Verification command for every task:** `python3 tools/typecheck.py --editor` (exit 0 = both assemblies compile).
> **Commits are gated:** a hook blocks `git commit` unless the user asks for one in that turn. Stage, then ask.

---

## File Structure

| File | Responsibility |
|---|---|
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashRail.cs` | **new.** The authored slot: its segment, its closed-form bend, its junction links, and a live registry so the path can find one without searching the scene. |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashWrap.cs` | **modify.** Gains an optional `Rail`. |
| `Assets/Game/Scripts/Items/Artifacts/Leash/LeashPath.cs` | **modify.** A rail-bound wrap slides instead of freezing, and hands over at junctions. |
| `Assets/Game/Scripts/Gameplay/Crucible/CrucibleCarrier.cs` | **new.** The power cell. Server-owned; knows how to be destroyed and re-cradled. |
| `Assets/Game/Scripts/Gameplay/Crucible/CruciblePit.cs` | **new.** The hazard surface, its kill trigger, and the player-count swap. |
| `Assets/Game/Scripts/Gameplay/Crucible/CrucibleSocket.cs` | **new.** The goal trigger. |
| `Assets/Game/Scripts/Gameplay/Crucible/CrucibleRoom.cs` | **new.** The solved flag, the vault, the saver. |
| `Assets/Game/Editor/Tests/CrucibleTests.cs` | **new.** Rail geometry, junction transfer, hazard rule. |

`Gameplay/Crucible/` is its own folder rather than lodged in an existing one: it is a room, not a subsystem of anything already here, and four small files that change together belong together.

---

## Task 0: Prototype the ownership question before building anything

The riskiest assumption in the design. Do this first; if it fails, the rest of the plan changes shape.

- [ ] **Step 1: Build the throwaway scene**

A flat plane, two `LeashRail`s twelve metres apart (hand-place two empties per rail for now — `LeashRail` arrives in Task 1, so for this step use two cubes as improvised rim walls with a gap, and rely on Task 0 measuring *latency*, not rails). One crate with a `NetworkObject`, a server-authoritative `NetworkTransform`, a dynamic `Rigidbody` and a `LeashAttachable`. Spawned by the server.

- [ ] **Step 2: Run host + a real client**

Both players leash the crate, walk apart and try to hold it at a marked height for ten seconds.

- [ ] **Step 3: Judge it, and write down the answer**

Expected: the crate is controllable and the delay is felt but not fought. **Not acceptable:** the crate oscillating, or a player's correction arriving so late they overshoot every time.

If it is unplayable, stop and re-plan. The fallback is client-side prediction of the carrier from local rim motion reconciled against the server, which is a substantially larger piece of work and must be costed before any geometry or art is made.

- [ ] **Step 4: Record the verdict in the spec**

Append a short `## Prototype result` section to `docs/superpowers/specs/2026-09-05-crucible-puzzle-design.md` saying what happened and at what ping. Stage it and ask the user to authorise the commit.

---

## Task 1: `LeashRail` and its closed-form bend, test-first

**Files:**
- Create: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashRail.cs`
- Create: `Assets/Game/Editor/Tests/CrucibleTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// The rail's geometry, checked without a scene.
//
// The bend point is the one piece of this room that MUST agree between two machines to the bit:
// nothing about a rope's shape is ever sent, so both machines solve it independently from the same
// replicated positions. A closed form has no iteration count to disagree about, and the first test
// here is what proves the closed form is actually the minimum rather than merely plausible.
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class CrucibleTests
    {
        private static float RopeLength(Vector3 bend, Vector3 from, Vector3 to) =>
            Vector3.Distance(from, bend) + Vector3.Distance(bend, to);

        /// <summary>
        /// Brute force over the segment, to a finer resolution than the answer needs. If the closed
        /// form is not within a hair of the best sample, it is not the minimum.
        /// </summary>
        private static float BestSampled(Vector3 a, Vector3 b, Vector3 from, Vector3 to)
        {
            float best = float.MaxValue;

            for (int i = 0; i <= 20000; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / 20000f);
                best = Mathf.Min(best, RopeLength(p, from, to));
            }

            return best;
        }

        [Test]
        public void ClosestBend_IsTheShortestRopeOverTheRail()
        {
            Random.InitState(20260905);

            for (int trial = 0; trial < 200; trial++)
            {
                Vector3 a = Random.insideUnitSphere * 10f;
                Vector3 b = a + Random.onUnitSphere * Random.Range(2f, 20f);
                Vector3 from = Random.insideUnitSphere * 15f;
                Vector3 to = Random.insideUnitSphere * 15f;

                Vector3 bend = LeashRail.ClosestBend(a, b, from, to);

                Assert.AreEqual(BestSampled(a, b, from, to), RopeLength(bend, from, to), 0.005f,
                                $"trial {trial}");
            }
        }

        [Test]
        public void ClosestBend_ClampsToTheSegment()
        {
            Vector3 a = new(0f, 0f, 0f);
            Vector3 b = new(10f, 0f, 0f);

            // Both points far off the near end: the best bend is the end itself.
            Vector3 bend = LeashRail.ClosestBend(a, b, new Vector3(-20f, 3f, 0f), new Vector3(-20f, -3f, 0f));

            Assert.AreEqual(a, bend);
        }

        [Test]
        public void ClosestBend_WithBothPointsOnTheRailsOwnLine_FallsBackToTheMidpoint()
        {
            Vector3 a = new(0f, 0f, 0f);
            Vector3 b = new(10f, 0f, 0f);

            Vector3 bend = LeashRail.ClosestBend(a, b, new Vector3(2f, 0f, 0f), new Vector3(6f, 0f, 0f));

            Assert.AreEqual(4f, bend.x, 0.0001f);
        }

        /// <summary>
        /// The whole control scheme in one assertion: back away from your rail and the far end of
        /// the rope is drawn toward it, because the rope you spend outside is rope the inside no
        /// longer has.
        /// </summary>
        [Test]
        public void WalkingAwayFromTheRail_SpendsMoreRope()
        {
            Vector3 a = new(-5f, 0f, 0f);
            Vector3 b = new(5f, 0f, 0f);
            Vector3 cell = new(0f, -6f, 0f);

            Vector3 near = new(0f, 0f, 2f);
            Vector3 far = new(0f, 0f, 8f);

            float spentNear = RopeLength(LeashRail.ClosestBend(a, b, near, cell), near, cell);
            float spentFar = RopeLength(LeashRail.ClosestBend(a, b, far, cell), far, cell);

            Assert.Greater(spentFar, spentNear);
        }
    }
}
```

- [ ] **Step 2: Run the type-check to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `error CS0246: The type or namespace name 'LeashRail' could not be found`.

- [ ] **Step 3: Write `LeashRail`**

```csharp
// A slot cut through a wall, that a rope rides inside and slides along.
//
// A rail is not a pin. The rope's bend is wherever on the slot makes the whole rope shortest, so it
// MOVES as either end moves — which is what gives one player two continuous axes while walking:
// along the rail sweeps the far end sideways, away from the rail spends rope and draws it in.
//
// The bend is solved in closed form, and that is not an optimisation. Nothing about a rope's shape
// is ever sent between machines; both solve it from the same replicated positions and have to agree
// without talking. A closed form has no iteration count to disagree about and no raycast to land
// differently on two machines.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>One slot. Rope rides it, slides along it, and hands over to a connected one at its ends.</summary>
    public class LeashRail : MonoBehaviour
    {
        [Tooltip("The two ends of the slot. Leave empty to use this transform for both.")]
        [SerializeField] private Transform from;
        [SerializeField] private Transform to;

        [Tooltip("Rails whose mouths this one hands a rope over to. Set both ways.")]
        [SerializeField] private LeashRail[] connections = System.Array.Empty<LeashRail>();

        [Tooltip("How close a bend must come before this rail takes it, in metres.")]
        [SerializeField] private float captureRadius = 0.6f;

        [Tooltip("How close two rails' mouths must be to hand a rope over, in metres.")]
        [SerializeField] private float junctionRadius = 0.5f;

        public Vector3 A => from != null ? from.position : transform.position;

        public Vector3 B => to != null ? to.position : transform.position;

        public float CaptureRadius => captureRadius;

        /// <summary>
        /// The point on a rail that makes the total rope shortest.
        ///
        /// <para>
        /// Written along the rail's own axis, each end reduces to a distance <c>t</c> along it and a
        /// perpendicular height <c>h</c>, which turns the problem into the flat reflection one:
        /// <c>t* = (t_from·h_to + t_to·h_from) / (h_from + h_to)</c>. Exact, one expression, no loop.
        /// </para>
        /// <para>
        /// Both ends lying on the rail's own line gives <c>h_from + h_to == 0</c> and no unique
        /// answer — every point between them is equally short. The midpoint is chosen because it is
        /// stable: any other tie-break makes the bend jump when the degenerate case is entered and
        /// left, which reads as the rope snagging on nothing.
        /// </para>
        /// </summary>
        public static Vector3 ClosestBend(Vector3 a, Vector3 b, Vector3 from, Vector3 to)
        {
            Vector3 axis = b - a;
            float span = axis.magnitude;
            if (span < 0.0001f) return a;

            Vector3 direction = axis / span;

            float tFrom = Vector3.Dot(from - a, direction);
            float tTo = Vector3.Dot(to - a, direction);

            float hFrom = Vector3.Distance(from, a + direction * tFrom);
            float hTo = Vector3.Distance(to, a + direction * tTo);

            float total = hFrom + hTo;

            float t = total > 0.0001f
                ? (tFrom * hTo + tTo * hFrom) / total
                : (tFrom + tTo) * 0.5f;

            return a + direction * Mathf.Clamp(t, 0f, span);
        }

        /// <summary>Where a rope running between these two points bends on this rail.</summary>
        public Vector3 BendFor(Vector3 from, Vector3 to) => ClosestBend(A, B, from, to);

        /// <summary>
        /// The rail this bend should hand over to, or null.
        ///
        /// <para>
        /// Only ever asked of a bend that has already run out of rail. Walking on past the end of
        /// one slot is the entire input for changing slots — there is no key, because the moment a
        /// player needs to change rails is the moment their partner is holding the cell alone, and
        /// that is the worst possible time to ask someone to stop and aim at something.
        /// </para>
        /// </summary>
        public LeashRail HandOverAt(Vector3 bend)
        {
            for (int i = 0; i < connections.Length; i++)
            {
                LeashRail next = connections[i];
                if (next == null || next == this) continue;

                if (Vector3.Distance(bend, next.A) <= junctionRadius) return next;
                if (Vector3.Distance(bend, next.B) <= junctionRadius) return next;
            }

            return null;
        }

        /// <summary>Whether a bend has slid all the way to one of this rail's mouths.</summary>
        public bool AtEnd(Vector3 bend)
        {
            const float Epsilon = 0.02f;
            return Vector3.Distance(bend, A) < Epsilon || Vector3.Distance(bend, B) < Epsilon;
        }

        // ── Live registry ──────────────────────────────────────────────────────

        private static readonly List<LeashRail> LiveRails = new();

        private void OnEnable() => LiveRails.Add(this);

        private void OnDisable() => LiveRails.Remove(this);

        /// <summary>
        /// The rail that should take a bend at this point, or null.
        ///
        /// <para>
        /// A registry rather than a scene search or a trigger volume: this is asked once per wrap per
        /// physics step, and it must answer identically on two machines, which a physics query
        /// against streamed-in colliders does not reliably do.
        /// </para>
        /// </summary>
        public static LeashRail Capturing(Vector3 point)
        {
            LeashRail best = null;
            float nearest = float.MaxValue;

            for (int i = 0; i < LiveRails.Count; i++)
            {
                LeashRail rail = LiveRails[i];
                if (rail == null) continue;

                float distance = Vector3.Distance(point, rail.BendFor(point, point));
                if (distance > rail.captureRadius || distance >= nearest) continue;

                nearest = distance;
                best = rail;
            }

            return best;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(A, B);
            Gizmos.DrawWireSphere(A, 0.15f);
            Gizmos.DrawWireSphere(B, 0.15f);
        }
    }
}
```

> Note on `Capturing`: `BendFor(point, point)` with both arguments equal reduces to the closest point
> on the segment, which is exactly the distance test wanted — no second projection helper needed.

- [ ] **Step 4: Type-check, then run the tests**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0. Then run `CrucibleTests` in the Editor's Test Runner.
Expected: all four pass. `ClosestBend_IsTheShortestRopeOverTheRail` is the one that matters — if it
fails, the closed form is wrong and nothing downstream is worth building.

- [ ] **Step 5: Stage**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/LeashRail.cs \
        Assets/Game/Editor/Tests/CrucibleTests.cs
```

Ask the user to authorise: `feat(leash): LeashRail — a slot the rope slides along`

---

## Task 2: Rail-bound wraps in `LeashPath`

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashWrap.cs`
- Modify: `Assets/Game/Scripts/Items/Artifacts/Leash/LeashPath.cs`

- [ ] **Step 1: Give `LeashWrap` a rail**

Add the field and a second constructor:

```csharp
        /// <summary>
        /// The slot this bend is riding, or null for an ordinary bend on a surface.
        ///
        /// <para>
        /// A rail-bound wrap is not frozen where it was made: its position is re-solved every step,
        /// which is what makes a rail a rail rather than a pin.
        /// </para>
        /// </summary>
        public readonly LeashRail Rail;
```

Extend both constructors so the existing three-argument one passes `null` for `Rail`, and add:

```csharp
        public LeashWrap(Vector3 position, Vector3 normal, Collider surface, LeashRail rail)
        {
            Position = position;
            Normal = normal;
            Surface = surface;
            Rail = rail;
        }
```

- [ ] **Step 2: Do not drop a rail-bound wrap for having no collider**

In `LeashPath.DropDead`, change the condition:

```csharp
            for (int i = wraps.Count - 1; i >= 0; i--)
                if (wraps[i].Rail == null && wraps[i].Surface == null) wraps.RemoveAt(i);
```

- [ ] **Step 3: Slide and hand over, at the end of `Step`**

In `LeashPath.Step`, after `Wrap(...)`:

```csharp
            SlideRails(endA, endB);
```

And add the method:

```csharp
        /// <summary>
        /// Re-solve every rail-bound bend, and hand a bend that has run out of rail to the next one.
        ///
        /// <para>
        /// Runs after wrapping, so a bend the probe has just made against the rim wall is picked up
        /// by the slot cut through that wall in the same step rather than a frame later — a bend that
        /// spends one step frozen on the wall and the next sliding on the rail visibly jumps.
        /// </para>
        /// </summary>
        private void SlideRails(Vector3 endA, Vector3 endB)
        {
            for (int i = 0; i < wraps.Count; i++)
            {
                LeashWrap wrap = wraps[i];

                LeashRail rail = wrap.Rail != null ? wrap.Rail : LeashRail.Capturing(wrap.Position);
                if (rail == null) continue;

                Vector3 before = i == 0 ? endA : wraps[i - 1].Position;
                Vector3 after = i == wraps.Count - 1 ? endB : wraps[i + 1].Position;

                Vector3 bend = rail.BendFor(before, after);

                // Slid off the end: if a connected slot's mouth is here, keep walking onto it.
                if (rail.AtEnd(bend))
                {
                    LeashRail next = rail.HandOverAt(bend);
                    if (next != null)
                    {
                        rail = next;
                        bend = rail.BendFor(before, after);
                    }
                }

                wraps[i] = new LeashWrap(bend, wrap.Normal, wrap.Surface, rail);
            }
        }
```

- [ ] **Step 4: Type-check**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0.

- [ ] **Step 5: Verify by hand in the Editor**

Two cubes with a gap between them, a `LeashRail` empty pair spanning the gap, a crate on the far
side. Tie a rope through the gap and walk along the wall.
Expected: the rope's bend slides along the slot as you walk, and the crate sweeps sideways. Walk away
from the wall and the crate is drawn toward the slot.

- [ ] **Step 6: Stage**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Leash/LeashWrap.cs \
        Assets/Game/Scripts/Items/Artifacts/Leash/LeashPath.cs
```

Ask the user to authorise: `feat(leash): rail-bound wraps slide and hand over at junctions`

---

## Task 3: The carrier and its cradle

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Crucible/CrucibleCarrier.cs`
- Create: `Assets/Game/Prefabs/Gameplay/Crucible/CruciblePowerCell.prefab`

- [ ] **Step 1: Write `CrucibleCarrier`**

```csharp
// The thing you are carrying: a power cell, and deliberately a cheap one.
//
// It is destroyed by the lava constantly, so it must be something a fresh copy of can roll into the
// cradle without anyone raising an eyebrow. That is why the reward is behind a socket rather than
// being this object: an ancient relic that respawns after falling in lava is a worse story than a
// spare fuse.
using SpaceGame.Core;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// One power cell. Server-owned, so both ropes' cell-ends resolve on one machine under the
    /// leash's existing "each machine resolves the ends it owns" rule — no new ownership concept.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CrucibleCarrier : NetworkBehaviour
    {
        [Tooltip("Speed below which the cell counts as settled, for seating it in the socket.")]
        [SerializeField] private float settledSpeed = 0.35f;

        private Rigidbody body;

        private void Awake() => body = GetComponent<Rigidbody>();

        public bool Settled => body != null && body.linearVelocity.magnitude < settledSpeed;

        /// <summary>Put this cell back in its cradle, at rest. Server only.</summary>
        public void Recradle(Vector3 at)
        {
            if (!IsServer) return;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            // transform.position does not move a body here — it is undone within the frame by the
            // physics step. MovePosition is the only write that survives.
            body.position = at;
            body.MovePosition(at);
        }
    }
}
```

- [ ] **Step 2: Build the prefab**

Create `Assets/Game/Prefabs/Gameplay/Crucible/CruciblePowerCell.prefab`:
a cube roughly 0.4 m, with `Rigidbody` (mass 12, interpolate, not kinematic), a `BoxCollider`,
`NetworkObject`, `NetworkTransform` set to **server** authority, `LeashAttachable`, and
`CrucibleCarrier`.

- [ ] **Step 3: Register it as a network prefab**

Add `CruciblePowerCell` to the project's network prefab list. **This is not optional and its absence
does not fail on the host** — an unregistered runtime-spawned prefab works perfectly in single player
and breaks only for clients, silently.

- [ ] **Step 4: Verify the registration landed**

Run: `grep -rn 'CruciblePowerCell' Assets/ --include="*.asset" --include="*.prefab" | head`
Expected: a hit in the network prefabs list asset, not only in the prefab itself.

- [ ] **Step 5: Type-check and stage**

Run: `python3 tools/typecheck.py --editor`

```bash
git add Assets/Game/Scripts/Gameplay/Crucible/ Assets/Game/Prefabs/Gameplay/Crucible/
```

Ask the user to authorise: `feat(crucible): the power cell`

---

## Task 4: The pit — hazard, kill trigger, and the single-player swap

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Crucible/CruciblePit.cs`
- Test: `Assets/Game/Editor/Tests/CrucibleTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `CrucibleTests`:

```csharp
        [Test]
        public void TheFloorIsLava_OnlyWhenThereIsSomeoneToCoordinateWith()
        {
            Assert.IsFalse(CruciblePit.HazardFor(0));
            Assert.IsFalse(CruciblePit.HazardFor(1));
            Assert.IsTrue(CruciblePit.HazardFor(2));
            Assert.IsTrue(CruciblePit.HazardFor(4));
        }
```

- [ ] **Step 2: Run the type-check to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `CruciblePit` not found.

- [ ] **Step 3: Write `CruciblePit`**

```csharp
// The floor of the room, and the only way to fail in it.
//
// Alone, it is a floor. The cell can be set down, so the puzzle becomes sequential — park it, walk
// round, re-rig, pull again — and the room turns from a test of nerve into a test of planning. That
// is not a difficulty setting; it is a different game in the same geometry, and it is the reason the
// swap is worth having rather than just gating the room behind two players.
//
// A second player joining floods the room. That is the intended drama: it teaches what the room is
// in one shot, and it is much better theatre than a sign.
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>The hazard surface under the maze.</summary>
    public class CruciblePit : NetworkBehaviour
    {
        [Tooltip("Shown when the pit is lava.")]
        [SerializeField] private GameObject lavaVisuals;

        [Tooltip("Shown when the pit is a plain floor.")]
        [SerializeField] private GameObject floorVisuals;

        [Tooltip("Where a destroyed cell comes back.")]
        [SerializeField] private Transform cradle;

        private readonly NetworkVariable<bool> hazard = new(false);

        /// <summary>
        /// Lava needs someone to coordinate with. A solo player cannot hold a cell up with one rope,
        /// so a lava floor alone is not a hard room, it is an impossible one.
        /// </summary>
        public static bool HazardFor(int players) => players >= 2;

        public bool HazardActive => hazard.Value;

        public override void OnNetworkSpawn()
        {
            hazard.OnValueChanged += (_, now) => ShowHazard(now);
            ShowHazard(hazard.Value);

            if (!IsServer) return;

            NetworkManager.OnClientConnectedCallback += _ => Recount();
            NetworkManager.OnClientDisconnectCallback += _ => Recount();
            Recount();
        }

        private void Recount()
        {
            if (!IsServer) return;
            hazard.Value = HazardFor(NetworkManager.ConnectedClientsIds.Count);
        }

        private void ShowHazard(bool active)
        {
            if (lavaVisuals != null) lavaVisuals.SetActive(active);
            if (floorVisuals != null) floorVisuals.SetActive(!active);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || !hazard.Value || cradle == null) return;

            CrucibleCarrier carrier = other.GetComponentInParent<CrucibleCarrier>();
            if (carrier == null) return;

            carrier.Recradle(cradle.position);
        }
    }
}
```

- [ ] **Step 4: Type-check, run the test**

Run: `python3 tools/typecheck.py --editor`
Expected: exit code 0. Then run `CrucibleTests`.
Expected: PASS.

- [ ] **Step 5: Stage**

```bash
git add Assets/Game/Scripts/Gameplay/Crucible/CruciblePit.cs \
        Assets/Game/Editor/Tests/CrucibleTests.cs
```

Ask the user to authorise: `feat(crucible): the pit, and the floor that is only lava with company`

---

## Task 5: The socket, the room, and the save

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Crucible/CrucibleSocket.cs`
- Create: `Assets/Game/Scripts/Gameplay/Crucible/CrucibleRoom.cs`

- [ ] **Step 1: Write `CrucibleSocket`**

```csharp
// The far end. A cell that arrives here and stops is a cell that has made it.
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>The receptacle the cell has to be seated in.</summary>
    public class CrucibleSocket : NetworkBehaviour
    {
        [SerializeField] private CrucibleRoom room;

        [Tooltip("Seconds the cell must sit still in here before it counts as seated.")]
        [SerializeField] private float settleSeconds = 0.5f;

        private CrucibleCarrier resting;
        private float restingSince;

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;

            CrucibleCarrier carrier = other.GetComponentInParent<CrucibleCarrier>();
            if (carrier == null) return;

            resting = carrier;
            restingSince = Time.time;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;
            if (other.GetComponentInParent<CrucibleCarrier>() != resting) return;

            resting = null;
        }

        private void Update()
        {
            if (!IsServer || resting == null || room == null) return;

            // Settled as well as present: a cell swinging through the socket on a rope has not been
            // seated in it, and without this the room is solved by flinging the cell past the hole.
            if (!resting.Settled)
            {
                restingSince = Time.time;
                return;
            }

            if (Time.time - restingSince < settleSeconds) return;

            room.Solve();
            resting = null;
        }
    }
}
```

- [ ] **Step 2: Write `CrucibleRoom` with its saver**

```csharp
// Whether this room has been beaten, and the vault that answers for it.
//
// One flag, and the vault's state is DERIVED from it rather than saved beside it — two records of
// one fact are two records that can disagree, and the one that disagrees is always the one the
// player is looking at.
using Newtonsoft.Json.Linq;
using SpaceGame.Persistence;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>The Crucible's one piece of durable state.</summary>
    public class CrucibleRoom : NetworkBehaviour, ISaveable
    {
        [Tooltip("Moved aside when the room is solved.")]
        [SerializeField] private GameObject vaultDoor;

        private readonly NetworkVariable<bool> solved = new(false);

        public bool Solved => solved.Value;

        public override void OnNetworkSpawn()
        {
            solved.OnValueChanged += (_, now) => ShowVault(now);
            ShowVault(solved.Value);
        }

        /// <summary>Server only. Idempotent — the socket may report a seating more than once.</summary>
        public void Solve()
        {
            if (!IsServer || solved.Value) return;
            solved.Value = true;
        }

        private void ShowVault(bool open)
        {
            if (vaultDoor != null) vaultDoor.SetActive(!open);
        }

        // ── Persistence ────────────────────────────────────────────────────────

        public string SaveKey => "crucible";

        public object CaptureState() => solved.Value ? new JObject { ["solved"] = true } : null;

        /// <summary>
        /// A null state means the room was at its defaults when the save was taken, which is a value
        /// and not an absence: it must put the room back to unsolved rather than leave whatever this
        /// session happens to be holding.
        /// </summary>
        public void RestoreState(object state)
        {
            bool wasSolved = state is JObject o && o.Value<bool>("solved");

            if (IsServer) solved.Value = wasSolved;
            ShowVault(wasSolved);
        }
    }
}
```

> Check `ISaveable`'s exact `RestoreState` signature in
> `Assets/Game/Scripts/Core/Persistence/Format/ISaveable.cs` and match it; adjust the parameter type
> if it differs.

- [ ] **Step 3: The cell is deliberately not saved**

Do **not** add a saver to `CrucibleCarrier`. On load, an unsolved room spawns a fresh cell in the
cradle. This sidesteps the runtime-spawned-entity trap entirely: no prefab id to resolve, nothing to
duplicate on reload, nothing to go missing. If a cell must be spawned on load, do it from
`CrucibleRoom.RestoreState` on the server when `wasSolved` is false.

- [ ] **Step 4: Type-check and stage**

Run: `python3 tools/typecheck.py --editor`

```bash
git add Assets/Game/Scripts/Gameplay/Crucible/CrucibleSocket.cs \
        Assets/Game/Scripts/Gameplay/Crucible/CrucibleRoom.cs
```

Ask the user to authorise: `feat(crucible): socket, room and its saved solve`

---

## Task 6: Grey-box the room

No art. Wall heights and chimney widths are the tuning surface and they will change every playtest.

- [ ] **Step 1: Build the shell from primitives**

A rectangular pit 24 m × 16 m. Rim walkway at `y = 0`. Hazard surface at `y = −9`. Rim wall inner
face carrying the rails.

- [ ] **Step 2: Lay the rails**

Six to eight `LeashRail`s in the rim's inner face at `y = −0.4`, each 4–7 m long, with mouths meeting
within `junctionRadius` where a route should be possible and *not* meeting where it should not.
**Wire `connections` in both directions** — a one-way link is a rope that goes onto a rail and can
never come off it.

- [ ] **Step 3: Build three chimneys**

Vertical shafts through a rock slab, 2.5 m, 2.0 m and 1.6 m wide, tightest last, immediately before
the socket. Everything between them is connective tissue: walls whose tops sit **above** `y = −0.4`,
so the cell can never be lifted over them.

- [ ] **Step 4: Place the cradle and the socket**

Cradle at one short end, reachable from the rim so a reset costs seconds and not a trek. Socket at
the far end, past the tightest chimney.

- [ ] **Step 5: Add one low wall**

A single wall whose top sits **below** `y = −0.4`, as a shortcut for players who are good. This is
the one place to prove the wall-height rule works in both directions.

- [ ] **Step 6: Set the wrap mask**

Every piece of this room must be on a layer that is in the leash's `wrapLayers`, and the cell must
**not** be.

---

## Task 7: Verify it, on two machines and across a reload

Neither of these is optional. Both are project non-negotiables.

- [ ] **Step 1: Host and a real client**

Fly the cell end to end.
Expected: both screens draw the same rope shape and the same cell position throughout.

- [ ] **Step 2: Drop it in**

Expected: the cell is destroyed and a fresh one is in the cradle, on both machines.

- [ ] **Step 3: Solo**

Disconnect the client.
Expected: the lava becomes a floor, the cell can be set down, and the room is completable alone.

- [ ] **Step 4: Reconnect mid-attempt**

Expected: the lava floods back in while the cell is in the air.

- [ ] **Step 5: Save, quit, reload — unsolved**

Expected: room unsolved, vault shut, a fresh cell in the cradle, no duplicates.

- [ ] **Step 6: Solve, save, quit, reload**

Expected: vault still open. Confirm `"crucible"` actually appears in the save JSON — a saver whose
key is absent from the file has failed silently, which is how persistence fails here.

---

## Task 8: Documentation

- [ ] **Step 1: Write `docs/AI/systems/CruciblePuzzle.md`**

Use the house shape: frontmatter (`system`, `layer`, `summary`, `paths`, `symptoms`, `reads_with`,
`updated`) then **Model → Key types → Flows → Multiplayer → Persistence → Gotchas → Extending**.

`symptoms:` should be things someone would actually see:
`"the cell sits at a different height on the host and the client"`,
`"a rope goes onto a rail and can never come off it"`,
`"the lava never appears no matter how many people are in the room"`,
`"the vault is shut again after reloading a save"`,
`"the cell can be lifted straight over the maze"`.

`Gotchas` must carry: rails' `connections` are one-way unless set both ways; a wall shorter than rail
height is a shortcut and that is deliberate; the cell must not be in `wrapLayers`; the cell is
intentionally not persisted; `CruciblePit.HazardFor` counts connected clients, so a listen-host alone
is one.

- [ ] **Step 2: Add a plain-language entry to `docs/Human/the-systems.md`**

The validator fails without one for a new system.

- [ ] **Step 3: Note the rail in `LeashSystem.md`**

Add `LeashRail` to its Key types and a line under `Extending` — a rail is a leash concept that the
Crucible merely uses, and someone reading the leash doc needs to know slots exist.

- [ ] **Step 4: Regenerate and validate**

Run: `python3 tools/docs_check.py --index`
Expected: exit code 0.

- [ ] **Step 5: Stage**

```bash
git add docs/AI/systems/CruciblePuzzle.md docs/AI/systems/LeashSystem.md \
        docs/Human/the-systems.md docs/AI/INDEX.md docs/AI/ROUTING.md
```

Ask the user to authorise: `docs: the Crucible`

---

## Self-review notes

- **Spec coverage.** Rails and the closed-form bend → Task 1. Rails as preferred wrap points, not a
  parallel system → Task 2. Junction hand-over → Tasks 1, 2. Server-owned carrier → Tasks 0, 3.
  Network prefab registration → Task 3. Hazard swap on player count → Task 4. Socket and solve →
  Task 5. Persistence, and the deliberate non-persistence of the cell → Task 5. The wall-height rule
  → Task 6. Two-machine and reload verification → Task 7. Docs → Task 8.
- **Deliberate simplification against the spec.** The spec reserved `NetMsg` 98 and 99. Neither is
  needed: `solved` and `hazard` are `NetworkVariable`s, and a cell reset is a server-side rigidbody
  write on a server-authoritative transform. **Do not add the messages** — every message added to
  this codebase is a handler someone has to keep installed, and these would carry nothing a
  `NetworkVariable` does not already carry.
- **Names are consistent throughout:** `LeashRail.ClosestBend/BendFor/HandOverAt/AtEnd/Capturing/
  CaptureRadius`, `LeashWrap.Rail`, `LeashPath.SlideRails`, `CrucibleCarrier.Recradle/Settled`,
  `CruciblePit.HazardFor/HazardActive`, `CrucibleRoom.Solve/Solved`, save key `"crucible"`.
- **Open question carried from the spec, not resolved here:** whether the Crucible is its own
  interior scene or a set-piece in an existing one. Task 6 builds it as a self-contained prefab
  either way, so the answer can arrive late without rework.
