# Ledge Climbing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Jump in front of something too tall to jump onto climbs it (onto the top, or over it if too thin to stand on); the grappling hook hangs the player below an edge it reeled them to and Jump pulls them up; a separate, removable layer grabs ledges in mid-air while Jump is held.

**Architecture:** A shared `LedgeProbe` (serializable query object) decides *what* is climbable; `LedgeClimber` (owner-side, velocity-steered like `LadderClimber`) performs the climb and owns the ground and rope entry points; `LedgeAirGrab` is a separate component that feeds mid-air grabs into the climber. `PlayerMovement.OnJump` asks the climber first. The grappling hook perches on arrival when an edge is in reach and lets go of the rope through a callback when a climb starts.

**Tech Stack:** Unity 6 C#, Rigidbody physics queries, Netcode for GameObjects (owner-authoritative `NetworkTransform`, nothing new on the wire), `CharacterAction`/`CharacterActions` animation.

**Spec:** [docs/superpowers/specs/2026-10-06-ledge-climbing-design.md](../specs/2026-10-06-ledge-climbing-design.md)

## Global Constraints

- Ground/rope reach `maxReach` = **4.5 m** above the feet; mid-air reach `airReach` = **3.3 m**; `minClimbHeight` = **1.0 m**; `maxVaultThickness` = **1 m**; walkable slope **45°**. All serialized, none literal in logic.
- **Always climb on top if a standing body fits**; vault only when it does not.
- Grapple arrival with no edge in reach keeps **today's** `ReleaseInto(radial, arrived: true)` unchanged.
- **The ledge wins** over the back-gear double tap when a grab is possible on that press.
- Owner-side only. **No new NetMsg, RPC, NetworkVariable or network prefab.** Nothing new saved.
- **No automated tests** in this change — the user tests manually on host and client. Each task ends with a compile check, not a test run.
- **Never commit.** The user commits. Each task ends by listing the files it touched; leave them uncommitted.
- No dead code, no debug logs beyond one-time configuration warnings, no copy-paste (Task 1 exists to stop the ladder's capsule maths being duplicated), no magic numbers.
- Unity compile check = Unity MCP `refresh_unity` then `read_console` (errors only); if MCP is unavailable, ask the user to focus the Editor and report the console.

## Review Focus

1. **Jump next to a ladder** — the ladder must still take hold; the ledge climber must not start inside a ladder volume, and the ladder must not grab a body mid-ledge-climb (Task 3 guards both).
2. **A climb aborted by death, knockdown, a mount or a teleport** — gravity and `SetClimbing(false)` must be restored and the animation stopped; a stuck "climbing" flag freezes the player's horizontal movement forever (Task 3 `LetGo`).
3. **Jump pressed on the grapple rope with no edge in reach** — must do nothing, exactly as today; it must not release the rope (Task 4).
4. **A double tap well away from any wall** — must still deploy the wing pack (Task 5: `TryGrabNow` returns false with no ledge).
5. **Crouched Jump at a wall** — refused (crouched capsule is short, the "standing body fits" test would lie); the press behaves as today (Task 3 `CanStart`).

---

### Task 1: Extract the player's capsule geometry from `LadderClimber`

The ledge probe needs exactly the capsule maths `LadderClimber` has privately (`BodyHeight`, `BodyAt`, `BodyBlockedAt`, `Feet`). Move it into one shared type rather than copying it.

**Files:**
- Create: `Assets/Game/Scripts/Characters/Player/Movement/PlayerBodyShape.cs`
- Modify: `Assets/Game/Scripts/Characters/Player/Movement/LadderClimber.cs` (remove `BodyHeight`, `BodyBlockedAt`, `BodyAt`, `Feet` at the end of the file; use the shape instead)

**Interfaces:**
- Produces: `readonly struct PlayerBodyShape(CapsuleCollider capsule, Rigidbody self)` with `float Height`, `float Radius`, `Vector3 Feet`, `void At(Vector3 feet, out Vector3 low, out Vector3 high)`, `bool BlockedAt(Vector3 feet)`, `bool CastClear(Vector3 feet, Vector3 direction, float distance)`, `bool IsSelf(Collider c)`.

- [ ] **Step 1: Create `PlayerBodyShape.cs`**

```csharp
// The player's body capsule in world space, measured from the authored collider.
//
// The capsule is authored 2 m tall on a transform stretched to 3, and the pivot sits a metre above the
// soles, so every "where are the feet / does the body fit here" question has to go through the
// collider's lossy scale. LadderClimber answered those privately; the ledge climber asks the same
// questions, so the answers live here once.
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// World-space shape of the player's body capsule, and the overlap/sweep queries traversal needs.
    /// Colliders on <c>self</c> (hitboxes, ragdoll capsules riding the same rigidbody) are never
    /// counted as obstacles.
    /// </summary>
    public readonly struct PlayerBodyShape
    {
        private readonly CapsuleCollider capsule;
        private readonly Rigidbody self;

        public PlayerBodyShape(CapsuleCollider capsule, Rigidbody self)
        {
            this.capsule = capsule;
            this.self = self;
        }

        /// <summary>The body's world height: the capsule is authored 2 m on a transform stretched to 3.</summary>
        public float Height => capsule.height * capsule.transform.lossyScale.y;

        public float Radius
        {
            get
            {
                Vector3 scale = capsule.transform.lossyScale;
                return capsule.radius * Mathf.Max(scale.x, scale.z);
            }
        }

        public Vector3 Feet
        {
            get
            {
                Bounds bounds = capsule.bounds;
                return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            }
        }

        /// <summary>The body's capsule end-points, in world space, with its feet at <paramref name="feet"/>.</summary>
        public void At(Vector3 feet, out Vector3 low, out Vector3 high)
        {
            float radius = Radius;
            low = feet + Vector3.up * radius;
            high = feet + Vector3.up * Mathf.Max(radius, Height - radius);
        }

        public bool IsSelf(Collider c) => c.attachedRigidbody != null && c.attachedRigidbody == self;

        /// <summary>Whether the body standing with its feet at <paramref name="feet"/> would be inside something.</summary>
        public bool BlockedAt(Vector3 feet)
        {
            At(feet, out Vector3 low, out Vector3 high);
            foreach (Collider c in Physics.OverlapCapsule(low, high, Radius, Physics.DefaultRaycastLayers,
                                                          QueryTriggerInteraction.Ignore))
                if (!IsSelf(c))
                    return true;
            return false;
        }

        /// <summary>
        /// Whether the body can sweep <paramref name="distance"/> along <paramref name="direction"/> from
        /// <paramref name="feet"/> without meeting anything. Contacts it starts in (distance 0 — the floor
        /// under it, a wall it leans on) are not in its way.
        /// </summary>
        public bool CastClear(Vector3 feet, Vector3 direction, float distance)
        {
            At(feet, out Vector3 low, out Vector3 high);
            foreach (RaycastHit hit in Physics.CapsuleCastAll(low, high, Radius, direction, distance,
                                                             Physics.DefaultRaycastLayers,
                                                             QueryTriggerInteraction.Ignore))
                if (!IsSelf(hit.collider) && hit.distance > 0f)
                    return false;
            return true;
        }
    }
}
```

- [ ] **Step 2: Rewire `LadderClimber` onto it**

In `LadderClimber.cs`:
- Add a property beside the other private members: `private PlayerBodyShape Shape => new PlayerBodyShape(movement.BodyCapsule, body);`
- Replace every `BodyHeight` with `Shape.Height`, every `Feet()` with `Shape.Feet`, every `BodyBlockedAt(x)` with `Shape.BlockedAt(x)`.
- In `BlockedAbove()`, replace `BodyAt(Feet(), out Vector3 low, out Vector3 high, out float radius);` with:

```csharp
            PlayerBodyShape shape = Shape;
            shape.At(shape.Feet, out Vector3 low, out Vector3 high);
            float radius = shape.Radius;
```

  and the loop's `hit.rigidbody != body` with `!shape.IsSelf(hit.collider)`.
- Delete the now-unused `BodyHeight` property and the `BodyBlockedAt`, `BodyAt`, `Feet` methods (and the `/// <summary>The body's world height...` comment that moved to the new file).

- [ ] **Step 3: Compile check**

Run Unity MCP `refresh_unity`, then `read_console` filtered to errors. Expected: no errors. Grep to confirm nothing still calls the removed members:

```bash
grep -n "BodyAt(\|BodyBlockedAt\|Feet()\|BodyHeight" Assets/Game/Scripts/Characters/Player/Movement/LadderClimber.cs
```
Expected: no output.

- [ ] **Step 4: Leave uncommitted.** Files: `PlayerBodyShape.cs` (+ `.meta` Unity generates), `LadderClimber.cs`.

---

### Task 2: `LedgeProbe` — find a climbable ledge

**Files:**
- Create: `Assets/Game/Scripts/Characters/Player/Movement/LedgeProbe.cs`

**Interfaces:**
- Consumes: `PlayerBodyShape` (Task 1).
- Produces:
  - `enum LedgeKind { None, ClimbUp, Vault }`
  - `readonly struct Ledge { LedgeKind Kind; Vector3 Hang; Vector3 Landing; Vector3 Facing; bool Found; static Ledge None; }` — `Hang` = feet at the top of the pull (against the wall, just above the lip); `Landing` = feet at the end (on top, or just past the far edge); `Facing` = flat, into the wall.
  - `[Serializable] sealed class LedgeProbe` with `Ledge Find(in PlayerBodyShape body, Vector3 feet, Vector3 facing, float minHeight, float reach)`.

- [ ] **Step 1: Create `LedgeProbe.cs`**

```csharp
// What counts as a ledge the player can climb, asked from anywhere the player might climb one: standing
// at a wall, hanging on a grapple rope, or passing an edge in mid-air. One query, so all three agree.
//
// Order matters and each step can only say no: a wall in front, a walkable top within reach above the
// feet, room to rise to it, then — standing room on top (climb up) or, failing that, a top thin enough
// to go over (vault). "Climb on top whenever a standing body fits" is a design decision, not an
// accident of the order: see docs/superpowers/specs/2026-10-06-ledge-climbing-design.md.
using System;
using UnityEngine;

namespace SpaceGame.Characters
{
    public enum LedgeKind { None, ClimbUp, Vault }

    /// <summary>A ledge the body can climb, and the two feet positions the climb steers through.</summary>
    public readonly struct Ledge
    {
        public static readonly Ledge None = default;

        public readonly LedgeKind Kind;

        /// <summary>Feet at the top of the pull: against the wall, just above the lip.</summary>
        public readonly Vector3 Hang;

        /// <summary>Feet at the end: standing on top, or just past the far edge of a vault.</summary>
        public readonly Vector3 Landing;

        /// <summary>Flat, unit, into the wall.</summary>
        public readonly Vector3 Facing;

        public Ledge(LedgeKind kind, Vector3 hang, Vector3 landing, Vector3 facing)
        {
            Kind = kind;
            Hang = hang;
            Landing = landing;
            Facing = facing;
        }

        public bool Found => Kind != LedgeKind.None;
    }

    /// <summary>The ledge query. Serialized inside <see cref="LedgeClimber"/> so its distances are tuned in the Inspector.</summary>
    [Serializable]
    public sealed class LedgeProbe
    {
        [Tooltip("How far past the body's surface a wall may be and still be climbed, metres.")]
        [SerializeField, Min(0.05f)] private float wallProbeDistance = 1f;

        [Tooltip("Radius of the spheres the probe sweeps, metres. Small, so a railing's top bar is found.")]
        [SerializeField, Min(0.01f)] private float probeRadius = 0.1f;

        [Tooltip("Steepest top the body may end up standing on, degrees. Steeper is a wall.")]
        [SerializeField, Range(0f, 89f)] private float maxWalkableSlope = 45f;

        [Tooltip("How far past the wall face the lip is looked for, metres. Covers a face that leans back.")]
        [SerializeField, Min(0f)] private float lipInset = 0.3f;

        [Tooltip("Metres above the lip the feet are lifted to before moving over it.")]
        [SerializeField, Min(0f)] private float lipClearance = 0.1f;

        [Tooltip("Metres from the wall face to the body's centre while pulling up. At least the capsule radius.")]
        [SerializeField, Min(0f)] private float standoff = 0.6f;

        [Tooltip("Extra metres past the lip (beyond the capsule radius) where the feet are set down.")]
        [SerializeField, Min(0f)] private float standInset = 0.15f;

        [Tooltip("Thickest top that is vaulted rather than refused, when there is no room to stand on it, metres.")]
        [SerializeField, Min(0.05f)] private float maxVaultThickness = 1f;

        [Tooltip("Metres between samples when walking the top to find its far edge.")]
        [SerializeField, Min(0.02f)] private float vaultStep = 0.1f;

        [Tooltip("How far above or below the lip a sample may find the top and still call it the same top, metres.")]
        [SerializeField, Min(0.01f)] private float topTolerance = 0.3f;

        /// <summary>
        /// The ledge in front of a body with its feet at <paramref name="feet"/>, facing
        /// <paramref name="facing"/>, whose lip is between <paramref name="minHeight"/> and
        /// <paramref name="reach"/> metres above those feet — or <see cref="Ledge.None"/>.
        /// </summary>
        public Ledge Find(in PlayerBodyShape body, Vector3 feet, Vector3 facing, float minHeight, float reach)
        {
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f || reach <= minHeight) return Ledge.None;
            facing.Normalize();

            float radius = body.Radius;

            // 1. A wall in front, swept at a height every climbable ledge has a face at.
            Vector3 wallOrigin = feet + Vector3.up * Mathf.Max(probeRadius, minHeight * 0.5f);
            if (!Sweep(body, wallOrigin, facing, radius + wallProbeDistance, out RaycastHit wall) ||
                IsWalkable(wall.normal))
                return Ledge.None;

            // 2. The lip: straight down just past the face, from the top of the reach. A probe that
            // starts inside something is under an overhang, not over a top.
            Vector3 face = new Vector3(wall.point.x, feet.y, wall.point.z);
            Vector3 lipOrigin = face + facing * lipInset + Vector3.up * (reach + probeRadius);
            if (Overlaps(body, lipOrigin) ||
                !Sweep(body, lipOrigin, Vector3.down, reach - minHeight, out RaycastHit top) ||
                !IsWalkable(top.normal))
                return Ledge.None;

            float lipY = top.point.y;

            // 3. Room to rise to it.
            Vector3 hang = face - facing * standoff;
            hang.y = lipY + lipClearance;
            if (!body.CastClear(feet, Vector3.up, Mathf.Max(0f, hang.y - feet.y)) || body.BlockedAt(hang))
                return Ledge.None;

            Vector3 edge = new Vector3(face.x, lipY, face.z);

            // 4. Standing room on top wins whenever there is any.
            Vector3 stand = edge + facing * (radius + standInset);
            if (TopAt(stand, lipY, out float standY))
            {
                stand.y = standY + lipClearance;
                if (!body.BlockedAt(stand) && body.CastClear(hang, facing, FlatDistance(hang, stand)))
                    return new Ledge(LedgeKind.ClimbUp, hang, stand, facing);
            }

            // 5. Otherwise over, if the top ends within a vault's thickness.
            for (float d = vaultStep; d <= maxVaultThickness; d += vaultStep)
            {
                if (TopAt(edge + facing * d, lipY, out _)) continue;

                Vector3 exit = edge + facing * (d + radius + standInset);
                exit.y = lipY + lipClearance;
                return !body.BlockedAt(exit) && body.CastClear(hang, facing, FlatDistance(hang, exit))
                    ? new Ledge(LedgeKind.Vault, hang, exit, facing)
                    : Ledge.None;
            }

            return Ledge.None;
        }

        private bool IsWalkable(Vector3 normal) => Vector3.Angle(normal, Vector3.up) <= maxWalkableSlope;

        /// <summary>Whether a walkable top is within <see cref="topTolerance"/> of <paramref name="lipY"/> under <paramref name="at"/>.</summary>
        private bool TopAt(Vector3 at, float lipY, out float y)
        {
            Vector3 origin = new Vector3(at.x, lipY + topTolerance, at.z);
            bool hit = Physics.Raycast(origin, Vector3.down, out RaycastHit surface, topTolerance * 2f,
                                       Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                       IsWalkable(surface.normal);
            y = hit ? surface.point.y : lipY;
            return hit;
        }

        /// <summary>The nearest hit along the sweep that is not the body itself and not a contact it starts in.</summary>
        private bool Sweep(in PlayerBodyShape body, Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            bool found = false;
            foreach (RaycastHit hit in Physics.SphereCastAll(origin, probeRadius, direction, distance,
                                                            Physics.DefaultRaycastLayers,
                                                            QueryTriggerInteraction.Ignore))
            {
                if (body.IsSelf(hit.collider) || hit.distance <= 0f) continue;
                if (found && hit.distance >= nearest.distance) continue;
                nearest = hit;
                found = true;
            }
            return found;
        }

        private bool Overlaps(in PlayerBodyShape body, Vector3 at)
        {
            foreach (Collider c in Physics.OverlapSphere(at, probeRadius, Physics.DefaultRaycastLayers,
                                                         QueryTriggerInteraction.Ignore))
                if (!body.IsSelf(c))
                    return true;
            return false;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) =>
            new Vector2(b.x - a.x, b.z - a.z).magnitude;
    }
}
```

- [ ] **Step 2: Compile check** — `refresh_unity`, `read_console` errors only. Expected: none.

- [ ] **Step 3: Leave uncommitted.** File: `LedgeProbe.cs`.

---

### Task 3: `LedgeClimber` — the climb, the ground entry point, and the Jump hand-off

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/Animation/CharacterAction.cs` — append `Grab` to `enum Mark`
- Create: `Assets/Game/Scripts/Characters/Player/Movement/LedgeClimber.cs`
- Modify: `Assets/Game/Scripts/Characters/Player/Movement/Movement.cs` — `Awake`, `OnJump`
- Modify: `Assets/Game/Scripts/Characters/Player/Movement/LadderClimber.cs` — `TryTakeHold` guard
- Modify: `Assets/Game/Editor/Traversal/PlayerTraversalWiring.cs` — menu item
- Prefab: `Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab` (via the menu item)

**Interfaces:**
- Consumes: `PlayerBodyShape` (Task 1), `LedgeProbe`/`Ledge`/`LedgeKind` (Task 2), `PlayerMovement.SetClimbing/IsClimbing/IsGliding/IsBouncing/IsTethered/IsOnGround/CarryMomentum/BodyCapsule`, `PlayerController.IsDead/Input`, `PlayerRagdoll.IsHeldOrDown`, `PlayerStance.IsCrouching`, `Ladder.At(Vector3)`, `CharacterActions.Play/PickVariant/PlaybackSpeed/Stop`, `CharacterAction.SecondsTo/Seconds`, `ITeleportAware`.
- Produces (used by Tasks 4 and 5):
  - `bool TryClimb()` — the Jump entry: rope climb if a rope is offered, else a grounded climb.
  - `bool TryClimbFrom(Vector3 feet, Vector3 facing, float minHeight, float reach)`
  - `void OfferRopeClimb(Vector3 facing, System.Action onClimb)` / `void WithdrawRopeClimb()`
  - `bool RopeLedgeInReach(Vector3 facing)`
  - `bool IsClimbing`, `Vector3 Feet`, `void LetGo()`

- [ ] **Step 1: Append the `Grab` mark**

In `CharacterAction.cs`, `enum Mark`, after `Release`:

```csharp
            /// <summary>The frame a thrown thing leaves the hand.</summary>
            Release,

            /// <summary>The frame the hands take hold of an edge — a ledge climb's pull-up ends here.</summary>
            Grab
```

- [ ] **Step 2: Create `LedgeClimber.cs`**

```csharp
// Climbing a ledge, on the player's own body.
//
// Jump at a wall too tall to jump onto climbs it (GDC-L1-FEEL-0003: the intent, not a button): onto the
// top when a standing body fits there, over it when the top is too thin to stand on. A grapple rope that
// is holding the player offers its own climb (see OfferRopeClimb), and LedgeAirGrab feeds mid-air grabs
// in through TryClimbFrom. What is climbable is LedgeProbe's call, so every way in agrees.
//
// The climb is short and committed (GDC-L1-FEEL-0008): move keys are ignored until it ends, look stays
// free. Its timing is the animation's — the pull-up lasts until the action's Grab mark — so on every
// screen the hands meet the lip when the body does, whatever the height.
//
// Owner only, like LadderClimber. The player's NetworkTransform is owner-authoritative, so peers see the
// climb as the pose moving, and the action rides the network animator. Nothing is saved: a climb lasts
// about a second, and a player loaded mid-climb simply drops or climbs again.
using System;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using SpaceGame.Teleporting;
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Climbs the player onto or over a ledge in front of them. Takes the body off
    /// <see cref="PlayerMovement"/> with <see cref="PlayerMovement.SetClimbing"/> for the duration and
    /// hands it back on every way the climb ends, through <see cref="LetGo"/>.
    /// </summary>
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(Rigidbody), typeof(PlayerMovement))]
    public class LedgeClimber : MonoBehaviour, ITeleportAware
    {
        [Header("Reach, metres above the feet")]
        [Tooltip("Lowest lip Jump climbs from the ground. Anything lower is a plain jump.")]
        [SerializeField, Min(0f)] private float minClimbHeight = 1f;

        [Tooltip("Lowest lip climbed while hanging on a rope. Just above the feet: anything lower is a landing.")]
        [SerializeField, Min(0f)] private float minHangingHeight = 0.2f;

        [Tooltip("Highest lip climbed from the ground or a rope: a jump plus arms' reach.")]
        [SerializeField, Min(0.1f)] private float maxReach = 4.5f;

        [SerializeField] private LedgeProbe probe = new LedgeProbe();

        [Header("Animation")]
        [Tooltip("Played for a climb onto the top. Its Grab mark is where the pull-up ends.")]
        [SerializeField] private CharacterAction climbUpAction;

        [Tooltip("Played for a vault over. Its Grab mark is where the pull-up ends.")]
        [SerializeField] private CharacterAction vaultAction;

        [Tooltip("Pull-up seconds when the action is missing or has no Grab mark.")]
        [SerializeField, Min(0.05f)] private float fallbackPullSeconds = 0.6f;

        [Tooltip("Seconds over the lip when the action is missing or has no Grab mark.")]
        [SerializeField, Min(0.05f)] private float fallbackOverSeconds = 0.35f;

        [Header("Motion")]
        [Tooltip("How hard the body is held on the axis it is not travelling along, per second.")]
        [SerializeField, Min(0f)] private float lineSnap = 12f;

        [Tooltip("Forward speed a vault lets go with, m/s.")]
        [SerializeField, Min(0f)] private float vaultExitSpeed = 4f;

        [Tooltip("Metres from a phase's target that count as there.")]
        [SerializeField, Min(0.001f)] private float arriveTolerance = 0.05f;

        [Tooltip("Slowest progress toward the target, m/s, that is not a stall.")]
        [SerializeField, Min(0f)] private float stallSpeed = 0.25f;

        [Tooltip("Seconds of stalled progress before the climb gives up and hands the body back.")]
        [SerializeField, Min(0.05f)] private float stallTimeout = 0.4f;

        private enum Phase { None, Pull, Over }

        private PlayerMovement movement;
        private PlayerController controller;
        private PlayerStance stance;
        private PlayerRagdoll ragdoll;
        private CharacterActions actions;
        private Rigidbody body;

        private Phase phase;
        private Ledge ledge;
        private float phaseTime;
        private float phaseDuration;
        private float overDuration;
        private float lastDistance;
        private float stallTime;
        private bool gravityBeforeClimb;
        private CharacterAction playing;
        private bool warnedMissingAction;

        private bool ropeOffered;
        private Vector3 ropeFacing;
        private Action onRopeClimb;

        public bool IsClimbing => phase != Phase.None;

        public Vector3 Feet => Shape.Feet;

        private PlayerBodyShape Shape => new PlayerBodyShape(movement.BodyCapsule, body);

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            controller = GetComponent<PlayerController>();
            stance = GetComponent<PlayerStance>();
            ragdoll = GetComponent<PlayerRagdoll>();
            actions = GetComponent<CharacterActions>();
            body = GetComponent<Rigidbody>();
        }

        private void OnDisable()
        {
            LetGo();
            WithdrawRopeClimb();
        }

        /// <summary>A teleport is never a climb: whatever moved the player took them off the ledge.</summary>
        public void OnTeleported(in TeleportMove move) => LetGo();

        // ── Ways in ────────────────────────────────────────────────────────────

        /// <summary>
        /// Jump's question, asked by <see cref="PlayerMovement.OnJump"/> before it jumps. Climbs from the
        /// rope when one is offering, from the ground otherwise. False leaves the press to the jump.
        /// </summary>
        public bool TryClimb()
        {
            if (ropeOffered) return TryClimbFrom(Feet, ropeFacing, minHangingHeight, maxReach);
            if (movement.IsTethered || !movement.IsOnGround) return false;
            return TryClimbFrom(Feet, transform.forward, minClimbHeight, maxReach);
        }

        /// <summary>Climb the ledge described by these limits, if there is one and the body is free to.</summary>
        public bool TryClimbFrom(Vector3 feet, Vector3 facing, float minHeight, float reach)
        {
            if (!CanStart()) return false;

            Ledge found = probe.Find(Shape, feet, facing, minHeight, reach);
            if (!found.Found) return false;

            Begin(found);
            return true;
        }

        /// <summary>
        /// A rope is holding the player and may be climbed off, toward <paramref name="facing"/>.
        /// <paramref name="onClimb"/> is called once when such a climb starts, for the rope to let go.
        /// Call every physics step the rope holds; withdraw when it stops.
        /// </summary>
        public void OfferRopeClimb(Vector3 facing, Action onClimb)
        {
            ropeOffered = true;
            ropeFacing = facing;
            onRopeClimb = onClimb;
        }

        public void WithdrawRopeClimb()
        {
            ropeOffered = false;
            onRopeClimb = null;
        }

        /// <summary>Whether a climb from the rope, toward <paramref name="facing"/>, would find a ledge right now.</summary>
        public bool RopeLedgeInReach(Vector3 facing) =>
            CanStart() && probe.Find(Shape, Feet, facing, minHangingHeight, maxReach).Found;

        private bool CanStart()
        {
            if (!isActiveAndEnabled || phase != Phase.None || !Network.Owns(this)) return false;
            if (movement.BodyCapsule == null || !BodyIsOurs()) return false;
            if (movement.IsClimbing || movement.IsGliding || movement.IsBouncing) return false;

            // A crouched capsule is short, so "a standing body fits on top" would be asked of the wrong body.
            if (stance != null && stance.IsCrouching) return false;

            // The ladder owns Jump inside its volume.
            return Ladder.At(Feet) == null;
        }

        private bool BodyIsOurs() =>
            movement.isActiveAndEnabled && !body.isKinematic &&
            (controller == null || !controller.IsDead) &&
            (ragdoll == null || !ragdoll.IsHeldOrDown);

        // ── The climb ──────────────────────────────────────────────────────────

        private void Begin(Ledge found)
        {
            ledge = found;
            gravityBeforeClimb = body.useGravity;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            movement.SetClimbing(true);

            float pull = fallbackPullSeconds;
            overDuration = fallbackOverSeconds;
            PlayAction(found.Kind == LedgeKind.Vault ? vaultAction : climbUpAction, ref pull, ref overDuration);
            StartPhase(Phase.Pull, pull);

            // Last, so the rope lets go of a body that is already climbing.
            Action letGoOfRope = onRopeClimb;
            WithdrawRopeClimb();
            letGoOfRope?.Invoke();
        }

        private void PlayAction(CharacterAction action, ref float pull, ref float over)
        {
            if (action == null || actions == null)
            {
                WarnMissingAction(action == null ? "no action is assigned" : "the player has no CharacterActions");
                return;
            }

            int variant = actions.PickVariant(action);
            float speed = actions.PlaybackSpeed(action);
            float toGrab = action.SecondsTo(CharacterAction.Mark.Grab, variant, speed);
            float total = action.Seconds(variant, speed);

            if (toGrab > 0f && total > toGrab)
            {
                pull = toGrab;
                over = total - toGrab;
            }
            else
            {
                WarnMissingAction($"'{action.name}' has no Grab mark before its end");
            }

            if (actions.Play(action, null, variant)) playing = action;
        }

        private void WarnMissingAction(string why)
        {
            if (warnedMissingAction) return;
            warnedMissingAction = true;
            Debug.LogWarning($"[LedgeClimber] Climbing on fallback timing because {why}. Assign the climb-up " +
                             "and vault actions on the player prefab's LedgeClimber.", this);
        }

        private void StartPhase(Phase next, float duration)
        {
            phase = next;
            phaseTime = 0f;
            phaseDuration = duration;
            stallTime = 0f;
            lastDistance = float.PositiveInfinity;
        }

        private void FixedUpdate()
        {
            if (phase == Phase.None) return;

            if (!BodyIsOurs())
            {
                LetGo();
                return;
            }

            float dt = Time.fixedDeltaTime;
            phaseTime += dt;

            Vector3 feet = Feet;
            Vector3 target = phase == Phase.Pull ? ledge.Hang : ledge.Landing;
            Vector3 to = target - feet;
            Vector3 flat = new Vector3(to.x, 0f, to.z);

            bool arrived = phase == Phase.Pull ? to.y <= arriveTolerance : flat.magnitude <= arriveTolerance;
            if (arrived)
            {
                if (phase == Phase.Pull) StartPhase(Phase.Over, overDuration);
                else Finish();
                return;
            }

            float distance = to.magnitude;
            stallTime = lastDistance - distance >= stallSpeed * dt ? 0f : stallTime + dt;
            lastDistance = distance;
            if (stallTime > stallTimeout)
            {
                LetGo();
                return;
            }

            // Travel along one axis on the action's clock, hold the other one on its line.
            float remaining = Mathf.Max(phaseDuration - phaseTime, dt);
            body.linearVelocity = phase == Phase.Pull
                ? flat * lineSnap + Vector3.up * (to.y / remaining)
                : flat / remaining + Vector3.up * (to.y * lineSnap);
        }

        private void Finish()
        {
            if (ledge.Kind == LedgeKind.Vault)
            {
                body.linearVelocity = ledge.Facing * vaultExitSpeed;
                // Otherwise air control confiscates the exit inside a fifth of a second.
                movement.CarryMomentum();
            }
            else
            {
                body.linearVelocity = Vector3.zero;
            }

            // Played to its end: nothing to cut short.
            playing = null;
            LetGo();
        }

        /// <summary>Hand the body back. Safe to call when not climbing.</summary>
        public void LetGo()
        {
            if (phase == Phase.None) return;

            phase = Phase.None;
            if (body != null) body.useGravity = gravityBeforeClimb;
            if (movement != null) movement.SetClimbing(false);

            if (playing != null && actions != null) actions.Stop(playing);
            playing = null;
        }
    }
}
```

Before saving, verify the `using` lines against the real namespaces (the ladder file uses `SpaceGame.Core`, `SpaceGame.Gameplay`, `SpaceGame.Teleporting`; check `CharacterActions`/`CharacterAction` and `PlayerRagdoll` namespaces):

```bash
grep -n "^namespace" Assets/Game/Scripts/Presentation/Animation/CharacterActions.cs Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs Assets/Game/Scripts/Gameplay/Traversal/Ladder.cs
```
Fix the `using` block to match.

- [ ] **Step 3: Jump asks the climber first**

In `Movement.cs`, add a field beside `stance`:

```csharp
        private LedgeClimber ledgeClimber;
```

In `Awake()`:

```csharp
            stance = GetComponent<PlayerStance>();
            ledgeClimber = GetComponent<LedgeClimber>();
```

In `OnJump()`, directly after the `if (bouncing || climbing || HaulingBlocksActions) { return; }` block:

```csharp
            // A wall too tall to jump onto is climbed instead. Asked here, before the leg jump, rather
            // than by a second Jump subscriber: two subscribers fire in no defined order, and the jump
            // would leave the ground in the same step the climb took the body.
            if (ledgeClimber != null && ledgeClimber.TryClimb())
            {
                return;
            }
```

- [ ] **Step 4: The ladder must not grab a body mid-ledge-climb**

In `LadderClimber.TryTakeHold`, change the first line:

```csharp
            if (movement.IsGliding || movement.IsTethered || movement.IsClimbing) return;
```

(`IsClimbing` is false whenever the ladder itself is not holding the body here, since `TryTakeHold` only runs with `ladder == null`; it is true only when another climber — the ledge — holds it.)

- [ ] **Step 5: Wire it onto the prefab**

In `PlayerTraversalWiring.cs`, beside `WireHatchCrawler`:

```csharp
        [MenuItem("Tools/SpaceGame/Player/Wire Ledge Climbing")]
        public static void WireLedgeClimbing()
        {
            Ensure<LedgeClimber>();
        }
```

Task 5 adds `Ensure<LedgeAirGrab>();` as a second line once that type exists.

Compile (`refresh_unity`, `read_console`), then run the menu item through Unity MCP `execute_menu_item` with `Tools/SpaceGame/Player/Wire Ledge Climbing`. Expected console: `[LedgeClimber] Added to Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab.` The networked variant inherits it.

- [ ] **Step 6: Compile check** — no errors in `read_console`.

- [ ] **Step 7: Hand to the user for a manual check** (host): Jump at a 2 m crate → stand on it; at a thin 3 m wall → over it; at a 6 m wall → ordinary jump; on flat ground → ordinary jump; next to a ladder → the ladder takes hold. Expect the one-time `[LedgeClimber] Climbing on fallback timing…` warning until Task 6.

- [ ] **Step 8: Leave uncommitted.** Files: `CharacterAction.cs`, `LedgeClimber.cs`, `Movement.cs`, `LadderClimber.cs`, `PlayerTraversalWiring.cs`, `PlayerCharacter.prefab`.

---

### Task 4: Grappling hook — hang below the edge, climb off the rope

**Files:**
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/GrapplingHookArtifact.cs` — fields, `CacheOwner`, `FixedUpdate`, `StopGrapple`, new `LedgeFacing`, new `ClimbOffRope`

**Interfaces:**
- Consumes: `LedgeClimber.OfferRopeClimb(Vector3, Action)`, `WithdrawRopeClimb()`, `RopeLedgeInReach(Vector3)` (Task 3); existing `AnnounceRelease()`, `StopGrapple()`, `ReleaseInto(...)`, `_hitNormal`.
- Produces: nothing new for other tasks.

- [ ] **Step 1: Fields**

Beside `private Vector3 _hitNormal = Vector3.up;` add:

```csharp
        /// <summary>
        /// Reeled to an edge the player can climb: the winch has stopped and the rope holds them just
        /// below the lip until Jump climbs off it. See FixedUpdate.
        /// </summary>
        private bool _perched;
```

Beside `private PlayerMovement _movement;` add:

```csharp
        private LedgeClimber _climber;
```

In the `[Header("Arrival & release")]` block, after `arrivalDistance`:

```csharp
        [Tooltip("How flat the rope must be, 0 to 1, for the climb to face along it. Steeper — a hook " +
                 "almost overhead — faces into the surface the hook is in instead.")]
        [SerializeField, Range(0f, 1f)] private float ledgeFacingMinFlat = 0.3f;
```

- [ ] **Step 2: Cache the climber**

In `CacheOwner()` add:

```csharp
            _climber = owner != null ? owner.GetComponent<LedgeClimber>() : null;
```

- [ ] **Step 3: Offer the climb, perch on arrival**

Replace the body of `FixedUpdate()` from `if (_body == null || _body.isKinematic)` to the end with:

```csharp
            if (_body == null || _body.isKinematic)
            {
                _climber?.WithdrawRopeClimb();
                TickTow();
                return;
            }

            ReclaimRopeFromTow();

            float dt = Time.fixedDeltaTime;

            Vector3 anchor = CurrentAnchor();
            Vector3 toHook = anchor - _body.position;
            float dist = toHook.magnitude;

            if (dist < 0.001f)
            {
                StopGrapple();
                return;
            }

            Vector3 radial = toHook / dist;

            // Swinging or hanging, the rope may be climbed off: Jump asks the climber, which asks this.
            Vector3 facing = LedgeFacing(radial);
            _climber?.OfferRopeClimb(facing, ClimbOffRope);

            if (_winch.Winching && !_perched)
            {
                Winch(radial, dist, dt);
                Ratchet(dist);
            }

            ApplyRopeConstraint(anchor, radial, dist, dt);

            // Hanging below an edge: the rope just holds, and the stall timer has nothing to time.
            if (_perched) return;

            if (_winch.Winching && dist <= arrivalDistance)
            {
                // Reeled to an edge the player can climb: hang there, still, and let Jump take them up.
                // No edge in reach is today's release, boost and all.
                if (_climber != null && _climber.RopeLedgeInReach(facing))
                {
                    _perched = true;
                    _body.linearVelocity = Vector3.zero;
                    return;
                }

                ReleaseInto(radial, arrived: true);
                return;
            }

            if (IsStalled(dist, dt)) ReleaseInto(radial, arrived: false);
```

- [ ] **Step 4: Facing and the release**

Add after `ReleaseInto`:

```csharp
        /// <summary>
        /// Which way a climb off this rope faces: along the rope, flattened — or, with the hook nearly
        /// overhead, into the surface it is buried in. Body forward when neither has a direction.
        /// </summary>
        private Vector3 LedgeFacing(Vector3 radial)
        {
            Vector3 flat = new Vector3(radial.x, 0f, radial.z);
            if (flat.magnitude < ledgeFacingMinFlat) flat = new Vector3(-_hitNormal.x, 0f, -_hitNormal.z);
            return flat.sqrMagnitude > 1e-4f ? flat.normalized : _body.transform.forward;
        }

        /// <summary>
        /// The climber took the player off the rope. Let go with no boost — the climb owns the body now —
        /// announced before the teardown, while owner is still set, as <see cref="ReleaseInto"/> does.
        /// </summary>
        private void ClimbOffRope()
        {
            AnnounceRelease();
            StopGrapple();
        }
```

- [ ] **Step 5: Tear down**

In `StopGrapple()`, after `_tow = null;` add:

```csharp
            _perched = false;
            _climber?.WithdrawRopeClimb();
```

- [ ] **Step 6: Compile check** — no errors.

- [ ] **Step 7: Hand to the user for a manual check** (host, then a client): hook the lip of a roof → reel → hang just below it, still → Jump → climb on top, rope gone on both screens. Hook a mid-cliff face with no top in reach → today's release boost. Swing on a slack rope past an edge, press Jump → climb. Jump on a rope with nothing in reach → nothing happens, rope stays. Quit while hanging, reload → dangling on the rope; Jump climbs.

- [ ] **Step 8: Leave uncommitted.** File: `GrapplingHookArtifact.cs`.

---

### Task 5: `LedgeAirGrab` — the mid-air layer, and the ledge winning the double tap

**Files:**
- Create: `Assets/Game/Scripts/Characters/Player/Movement/LedgeAirGrab.cs`
- Modify: `Assets/Game/Scripts/Items/Body/BodyEquipmentController.cs` — field, lookup, `OnBodyActivate`
- Modify: `Assets/Game/Editor/Traversal/PlayerTraversalWiring.cs` — add `Ensure<LedgeAirGrab>();`
- Prefab: `PlayerCharacter.prefab` (via the menu item)

**Interfaces:**
- Consumes: `LedgeClimber.TryClimbFrom(Vector3, Vector3, float, float)`, `LedgeClimber.Feet` (Task 3); `PlayerMovement.IsOnGround/IsTethered/OnJumped`; `PlayerInputManager.JumpHeld`.
- Produces: `bool LedgeAirGrab.TryGrabNow()`.

- [ ] **Step 1: Create `LedgeAirGrab.cs`**

```csharp
// Catching a ledge in mid-air: hold Jump through a jump or a fall, and the first ledge that comes within
// arms' reach is climbed (GDC-L1-FEEL-0003 — holding forgives the timing a press would demand).
//
// A separate layer on LedgeClimber, on purpose: the wall, obstacle and grapple climbs are the predictable
// core, and this is an add-on. Disable or remove this component and nothing else changes — including the
// back-gear double tap, which only defers to a grab through TryGrabNow when this is here.
using UnityEngine;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Characters
{
    /// <summary>Grabs a ledge in mid-air while Jump is held, through <see cref="LedgeClimber"/>.</summary>
    [DefaultExecutionOrder(160)]
    [RequireComponent(typeof(LedgeClimber), typeof(PlayerMovement))]
    public class LedgeAirGrab : MonoBehaviour
    {
        [Tooltip("Lowest lip grabbed, metres above the feet. Anything lower is a landing.")]
        [SerializeField, Min(0f)] private float minHeight = 0.2f;

        [Tooltip("Highest lip grabbed, metres above the feet: arms' reach, with no jump on top.")]
        [SerializeField, Min(0.1f)] private float airReach = 3.3f;

        [Tooltip("Seconds after a jump leaves the ground before a grab is allowed, so the press that " +
                 "jumped is not also a grab.")]
        [SerializeField, Min(0f)] private float afterJumpDelay = 0.15f;

        private LedgeClimber climber;
        private PlayerMovement movement;
        private PlayerInputManager inputs;
        private float lastJumpTime = float.NegativeInfinity;

        private void Awake()
        {
            climber = GetComponent<LedgeClimber>();
            movement = GetComponent<PlayerMovement>();
        }

        private void Start()
        {
            inputs = GetComponent<PlayerController>().Input;
            movement.OnJumped += OnJumped;
        }

        private void OnDestroy()
        {
            if (movement != null) movement.OnJumped -= OnJumped;
        }

        private void OnJumped() => lastJumpTime = Time.time;

        private void FixedUpdate()
        {
            if (inputs != null && inputs.JumpHeld) TryGrabNow();
        }

        /// <summary>
        /// Grab a ledge now, if the body is airborne and one is in reach. Also asked by the back-gear
        /// double tap, which stands down when this returns true: next to a ledge, the ledge wins.
        /// </summary>
        public bool TryGrabNow()
        {
            if (!isActiveAndEnabled || movement.IsOnGround || movement.IsTethered) return false;
            if (Time.time - lastJumpTime < afterJumpDelay) return false;
            return climber.TryClimbFrom(climber.Feet, transform.forward, minHeight, airReach);
        }
    }
}
```

- [ ] **Step 2: The ledge wins the double tap**

In `BodyEquipmentController.cs`, beside `private PlayerController player;`:

```csharp
        // Optional: the double tap defers to a mid-air ledge grab only when the player has that layer.
        private SpaceGame.Characters.LedgeAirGrab airGrab;
```

(Drop the namespace prefix if the file already has `using SpaceGame.Characters;` — check with `grep -n "^using" Assets/Game/Scripts/Items/Body/BodyEquipmentController.cs`.)

Where `player = GetComponent<PlayerController>();` is assigned, add:

```csharp
            airGrab = GetComponent<SpaceGame.Characters.LedgeAirGrab>();
```

In `OnBodyActivate()`, after `if (body.IsMounted) return;`:

```csharp
            // The second tap of Space next to a ledge means "grab it", not "open the wings".
            if (airGrab != null && airGrab.TryGrabNow()) return;
```

- [ ] **Step 3: Wire it onto the prefab**

In `PlayerTraversalWiring.WireLedgeClimbing`, add `Ensure<LedgeAirGrab>();` after `Ensure<LedgeClimber>();`. Compile, then `execute_menu_item` `Tools/SpaceGame/Player/Wire Ledge Climbing`. Expected: `[LedgeClimber] Already on …` and `[LedgeAirGrab] Added to …`.

- [ ] **Step 4: Compile check** — no errors.

- [ ] **Step 5: Hand to the user for a manual check:** jump toward a 4 m wall holding Space → grabbed and climbed; fall past an edge holding Space → grabbed; with the wing pack worn, double-tap in open air → wings deploy; double-tap jumping at a ledge → climb, no wings. Disable `LedgeAirGrab` on the prefab → ground and rope climbs unchanged, double tap unchanged.

- [ ] **Step 6: Leave uncommitted.** Files: `LedgeAirGrab.cs`, `BodyEquipmentController.cs`, `PlayerTraversalWiring.cs`, `PlayerCharacter.prefab`.

---

### Task 6: Animation clips → actions → prefab

**Blocked on the user** downloading the clips. Do the rest once they are in `Assets/Game/Art/Animations/Humanoid/`.

**Files:**
- Assets (user): `Braced Hang To Crouch.fbx` (or `Freehang Climb.fbx`), `Jump Over.fbx` (or the chosen vault clip), optional `Hanging Idle.fbx`
- Create (menu): two `CharacterAction` assets under `Assets/Game/ScriptableObjects/Animation/Actions/Mixamo/`
- Modify: `PlayerCharacter.prefab` — `LedgeClimber.climbUpAction`, `vaultAction`
- Regenerated: the humanoid controller (Rebuild)

- [ ] **Step 1: Confirm the import.** `LibraryClipImporter` handles `Art/Animations/Humanoid/*.fbx`. Check each clip imported as one named clip (not `mixamo.com` — see AnimationCatalog symptoms) and is Humanoid.

- [ ] **Step 2: Create the actions.** Select the two clips, run `Tools/SpaceGame/Animation/Create Actions From Selected Clips`. Name them `Ledge Climb Up` and `Ledge Vault`. Set each: Slot `Full`, Playback `OneShot`.

- [ ] **Step 3: Mark the grab.** On each action add a `PhaseMark` with mark `Grab` at the normalized time the hands reach the top of the pull (scrub the clip; for "Braced Hang To Crouch" it is where the hips start to come over the edge). Edit through the Inspector or `SerializedObject`, never hand-written YAML.

- [ ] **Step 4: Rebuild.** `Tools/SpaceGame/Animation/Rebuild Humanoid Controller`. Console: no `[HumanoidControllerBuilder]` errors.

- [ ] **Step 5: Assign.** On `PlayerCharacter.prefab` → `LedgeClimber`: `climbUpAction` = `Ledge Climb Up`, `vaultAction` = `Ledge Vault`. Save the prefab.

- [ ] **Step 6: Hand to the user:** the fallback warning is gone; a remote player's climb plays the clip on the other machine, hands arriving at the lip as the body does.

- [ ] **Step 7: Leave uncommitted.** List the FBX files + metas, action assets, controller assets the Rebuild touched, prefab.

---

### Task 7: Documentation

**Files:**
- Create: `docs/AI/systems/LedgeClimbing.md`
- Modify: `docs/AI/systems/PlayerCharacter.md`, `docs/AI/systems/Artifacts.md`, `docs/AI/systems/BodyEquipment.md`, `docs/AI/systems/Ladders.md`, `docs/Human/the-systems.md`
- Regenerated: `docs/AI/INDEX.md`, `docs/AI/ROUTING.md`

- [ ] **Step 1: Write `LedgeClimbing.md`**

Frontmatter (shape copied from `Ladders.md`):

```markdown
---
system: LedgeClimbing
layer: characters
summary: "Jump climbs onto or over a ledge; the grapple hangs below an edge; holding Jump grabs ledges in mid-air"
paths:
  - Assets/Game/Scripts/Characters/Player/Movement/LedgeClimber.cs
  - Assets/Game/Scripts/Characters/Player/Movement/LedgeProbe.cs
  - Assets/Game/Scripts/Characters/Player/Movement/LedgeAirGrab.cs
  - Assets/Game/Scripts/Characters/Player/Movement/PlayerBodyShape.cs
symptoms:
  - "pressing Jump at a wall does an ordinary jump instead of climbing"
  - "the grapple drops me at the top instead of letting me climb up"
  - "the player is stuck unable to walk after a climb"
  - "[LedgeClimber] Climbing on fallback timing"
  - "double-tapping Space next to a wall climbs instead of opening the wings"
reads_with: [PlayerCharacter, Ladders, Artifacts, BodyEquipment, HumanoidAnimation]
updated: 2026-10-06
---
```

Body sections, in order — fill each from the spec and the code as written:
- **Model**: three components (probe / climber / air grab), owner-only, velocity-steered, timing from the `Grab` mark.
- **Key types**: table — `LedgeProbe`/`Ledge`/`LedgeKind`, `LedgeClimber` (every public member from Task 3), `LedgeAirGrab.TryGrabNow`, `PlayerBodyShape`, `CharacterAction.Mark.Grab`.
- **Flows**: Jump on the ground (`PlayerMovement.OnJump` → `TryClimb`); the probe's five steps; Pull → Over → hand back; the grapple perch and `ClimbOffRope`; the mid-air hold; the double tap deferring.
- **Multiplayer**: nothing on the wire; owner `NetworkTransform` + network animator; rope release via `AnnounceRelease`.
- **Persistence**: nothing saved, and why; hanging is the grapple's saved rope.
- **Gotchas**: the explicit `OnJump` call instead of a second subscriber (ordering); `SetClimbing` is shared with the ladder, so `LadderClimber.TryTakeHold` refuses while `IsClimbing` and the climber refuses inside a ladder volume; crouched is refused; `LetGo` is the only exit and restores gravity; the double tap within `afterJumpDelay` of a jump still deploys; append-only `Mark` enum.
- **Extending**: a new rope-like holder offers `OfferRopeClimb`/`WithdrawRopeClimb`; new clips need a `Grab` mark.

- [ ] **Step 2: Update the neighbours**
- `PlayerCharacter.md`: Key types row for `PlayerMovement` — `OnJump` asks `LedgeClimber.TryClimb` first; Gotcha "Three ways to take this body off PlayerMovement" — `SetClimbing` now also used by `LedgeClimber`; add `LedgeClimber`/`LedgeAirGrab` to the "Also on the prefab root" list.
- `Artifacts.md` (grapple): arrival perches below a climbable edge (`_perched`, stall timer off); Jump climbs off the rope via `OfferRopeClimb`; no edge keeps the release boost.
- `BodyEquipment.md`: the double tap first asks `LedgeAirGrab.TryGrabNow`.
- `Ladders.md`: `TryTakeHold` also refuses while another climber holds the body; `LadderClimber` capsule maths now in `PlayerBodyShape`.
- Bump `updated: 2026-10-06` on each.

- [ ] **Step 3: Human doc.** Add under the relevant chapter in `docs/Human/the-systems.md`, next to `### Climbing ladders *(Ladders)*`:

```markdown
### Climbing ledges *(LedgeClimbing)*

Press Space in front of something too tall to jump onto and you climb it — up onto the top if there is
room to stand there, over it if it is only a fence or a thin wall. It reaches about a jump plus an arm's
length, roughly four and a half metres. The grappling hook finishes the same way: reel yourself to a hook
near an edge and you hang just under it until you press Space to pull yourself up. Hold Space while
jumping or falling past an edge and you catch it on the way.
```

- [ ] **Step 4: Regenerate and validate**

```bash
python3 tools/docs_check.py --index
```
Expected: exits 0, no validation errors.

- [ ] **Step 5: Leave uncommitted.** List all doc files touched. Tell the user the full set of files across Tasks 1–7 and suggest committing once their manual host+client check passes.
