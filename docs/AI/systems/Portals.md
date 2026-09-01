---
system: Portals
layer: world
summary: Sprayable one-way-pair apertures you walk through, replicated as messages rather than networked entities
paths:
  - Assets/Game/Scripts/Portals/
  - Assets/Game/Art/Shaders/Portal/
symptoms:
  - "walking into a portal does nothing and no trigger ever fires"
  - "the player comes out of a ceiling portal lying down in mid-air"
  - "exit momentum is confiscated a fraction of a second after coming through"
  - "one player walks into a wall where everyone else walks through a portal"
  - "an NPC or legged machine touches a portal but never goes through"
  - "a portal stays open forever with no partner after someone traverses"
  - "half the player is uncoloured while standing in the aperture"
  - "a rider arrives twice the aperture separation past the exit"
reads_with: [SceneTransitions, Artifacts, Combat]
updated: 2026-09-01
---

# Portals

Sprayable one-way-pair apertures: a plane you walk through, replicated as messages rather than as networked entities.
**Scope:** [`Assets/Game/Scripts/Portals/`](Assets/Game/Scripts/Portals) (9 files), [`Assets/Game/Art/Shaders/Portal/`](Assets/Game/Art/Shaders/Portal).
**Related:** [SceneTransitions.md](SceneTransitions.md) (`SaveTeleport`, `ITeleportAware`) · [Artifacts.md](Artifacts.md) · [WeaponSystem.md](WeaponSystem.md)

## Model

- A portal is a **door, not a window**. The old `PortalRenderer` (second camera → RenderTexture) is deleted: the surface is a stylised swirl ([`PortalSurface.shader`](Assets/Game/Art/Shaders/Portal/PortalSurface.shader)), so nothing here has to agree with a rendered view — `TransferFrom` answers to physics alone.
- **A portal is not a `NetworkObject`.** Placement replicates as a message and every machine builds its own copy — same pattern as the grappling rope. That is why portals work offline, on a host, and on a peer with no prefab registration.
- Coordinate convention: the normal is **local +Z / `transform.forward`**, pointing *out of the wall*. The back (−Z) is masonry. Every crossing gate is front-to-back only. **The Portal root must stay at unit scale** — `TransferFrom` composes `localToWorldMatrix` with a partner's `worldToLocalMatrix`, so root scale would resize the traveller.
- The opening is a **distance field**, not a rectangle: `PortalStencil` is an inscribed ellipse until paint lands, then the smooth union of up to `MaxDabs = 24` circles. The shader evaluates the identical field ([`PortalStencil.hlsl`](Assets/Game/Art/Shaders/Portal/PortalStencil.hlsl), `Smoothing = 0.35` must match `PORTAL_SMOOTH`). A lobe you can see is a lobe you can walk through.
- The door is **swept explicitly once per frame** from `LateUpdate`, never via trigger callbacks.
- **Navigation is left ignorant on purpose.** Nothing paths through a portal; things are *carried* through when they touch one.

## Key types

| Type | File | Role |
|---|---|---|
| `Portal` | [Portal.cs](Assets/Game/Scripts/Portals/Portal.cs) | One aperture. `[ExecuteAlways]`. Static `All`, `Link(a,b)`, `Crossing(...)`. Owns the sweep, the crossing step, the iris and the materials. |
| `PortalTraveller` | [PortalTraveller.cs](Assets/Game/Scripts/Portals/PortalTraveller.cs) | Added on demand to anything that reaches an aperture. Wall pass-through, girth measurement, the ghost clone, the move itself. |
| `PlayerPortalTraveller` | [PlayerPortalTraveller.cs](Assets/Game/Scripts/Portals/PlayerPortalTraveller.cs) | Overrides `ApplyTraversal`: `CarryMomentum`, upright yaw-only body, pitch via `PlayerLook.LookAlong`. |
| `PortalPair` | [PortalPair.cs](Assets/Game/Scripts/Portals/PortalPair.cs) | The two barrels **for one player**, on the player (not the gun — the gun prefab is rebuilt on every hotbar change). Owns the barrel cursor and the spray session; handles `NetMsg.PortalsUsed/PortalsShut/PortalExpired/PortalGone`. |
| `PortalStencil` / `PortalDab` | [PortalStencil.cs](Assets/Game/Scripts/Portals/PortalStencil.cs) | Pure C#, no MonoBehaviour. `Contains`, `Fits`, `Bounds`, `ClampInside`, `WriteShaderData`. Testable without a scene. |
| `PortalPlacement` / `NonPortalable` | [PortalPlacement.cs](Assets/Game/Scripts/Portals/PortalPlacement.cs) | Static `Fit(hit, size, viewForward, mask, …) → Result`. Shooter-only; the result travels verbatim. |
| `PortalGunItem` | [PortalGunItem.cs](Assets/Game/Scripts/Portals/PortalGunItem.cs) | A `ToolItem` on the ordinary Use/Present + hold-stream split. Owner authority. |
| `PortalJet` | [PortalJet.cs](Assets/Game/Scripts/Portals/PortalJet.cs) | Static ballistic `Sample` / `Trace` (20 chords). Particles must match its speed + gravity. |
| `PortalSplat` | [PortalSplat.cs](Assets/Game/Scripts/Portals/PortalSplat.cs) | One quad of exhaust paint, ~0.5 s. Seed is `SeedFor(point)` (cm-quantised hash), so every machine draws the same splash. |

## Flows

1. **Shot.** `OnRequestUse` (owner) picks the barrel via `PortalPair.ChooseSprayBarrel(aimPoint, growMargin, out grow)` — starting on your own paint tops that aperture up, anywhere else takes `PeekBarrel()` (an **empty** barrel always wins over plain alternation). `CommitBarrel` fires at the press, not at the landing.
2. **Jet.** `PortalJet.Trace` follows the parabola; `flightTime` delays the landing. `PortalPlacement.Fit` probes outward for the surface's edges and slides the opening wholly onto the wall, or fizzles.
3. **Paint.** `Present`/`PresentHold` on **every** machine: `PortalPair.LayDab` → `Portal.AddStroke` (interpolated arithmetically, never by probing) → `Portal.ConformToSurface`. **The shape never goes on the wire** — each tick carries one point and the owner's affordability verdict; every machine replays the same gesture.
4. **Sweep.** `Portal.LateUpdate` → `PublishSurfaceState` (also outside play mode) → `TickConformRetry` → `AdvanceTraversal` → `SweepVolume` + `StepCrossings`.
5. **Crossing (two modes).** `Crossed`: `previous.Side > 0 && current <= 0`, travel > `minimumCrossingSpeed`, and the **segment/plane intersection** inside the aperture (margin 0.25). `Touching` (the contact pull): still in front, has stopped closing, within `HalfDepthAlong(normal) + PullReach(0.25)` — this is what carries NavMeshAgents and legged machines, which never drive their centre past a wall.
6. **Traverse.** `transfer = destination.TransferFrom(this)` (`localToWorld * Rotate(0,180,0) * from.worldToLocal`); a pull composes an entry offset first; `ExitPointFor` clamps into the destination's own outline; all corrections are **composed into the matrix**, never applied after. Then `Release` → `PortalTraveller.Traverse` → `destination.Adopt` (immediately) → `ShutBehind` (far end first) → `pair.AnnounceTraversal`.
7. **Move.** Owner only: capture velocity → `ApplyTraversal` → `SaveTeleport.Move(go, pos, rot, zeroVelocity: false)` → `body.linearVelocity = transfer.MultiplyVector(velocity)` (rotated, not re-aimed — that is what preserves a diagonal) → `PlaceAgent` → `WarnIfStuck`. `ITeleportAware` is raised by `SaveTeleport`, not here.
8. **Projectiles.** `Portal.Crossing(from, to, out entry, out transfer)` — a static segment test over `All`, front-to-back, nearest wins. Consumed by [`Projectile.CrossPortal`](Assets/Game/Scripts/Weapons/Projectiles/Projectile.cs).

## Multiplayer

- **`Network.Owns(...)` is the only authority gate, asked twice.** `PortalTraveller.Traverse` wraps the whole move in it (player bodies are owner-authoritative; a server-side move is overwritten within a tick). `Portal.Traverse` gates `closeOnTraversal` on it — a peer's sweep fires for an interpolated replica and the contact pull needs no motion at all, so without the gate a bystander who could not even see the pair could eat it.
- Non-owners still run everything cosmetic: sweep, clone, slice, `Traversed`.
- `PortalPair` replicates over `NetMessaging`: owner → `NetMsg.PortalsUsed`/`PortalExpired` to the server; server → `NetMsg.PortalsShut`/`PortalGone` to everyone else. All handlers are idempotent because the host runs the request and its own broadcast inline.
- `SetLifetime(seconds, grace)`: peers get `RemoteExpiryGrace = 0.5 s` so a peer is **never** the first to drop an aperture — only the shooter may decide a spray is a top-up.
- Registration: portals need **no** network prefab entry. The `PortalGunItem` prefab does, like any other item; the `Portal` prefab it instantiates does not.

## Persistence

N/A for the gun's apertures — a portal's lifetime is 20 s and nothing writes one to a save. What is persistence-shaped: `linked` is a **serialized** field so a hand-authored pair survives load (an auto-property is not serialized, and an unlinked portal looks identical while being a wall you walk into); `Colour` is cached in a field because the material is a per-portal instance released in `OnDisable`; materials carry `HideFlags.HideAndDontSave` and are restored on disable, so a scene-placed portal leaves no instanced material in the saved scene. Travellers are transient and hold no saved state.

## Gotchas

- **Trigger callbacks never worked here.** The volume is a `BoxCollider` on a *child*; Unity delivers trigger messages only to the collider's own GameObject and its attached Rigidbody's, so traversal silently did nothing in every scene, forever. `travellerVolume` is authoring/gizmo only and is disabled in play mode. Never reintroduce `OnTriggerEnter`.
- **`Physics.IgnoreCollision` does nothing to raycasts.** `SetWallIgnored` must *also* call `IGroundProbeExclusions.ExcludeFromGroundProbes`, or legged and probing movers read the far side as a cliff and stop at the rim.
- **Composite entities are one traveller.** `PortalTraveller.Carrier` walks up from `transform.parent` (never `GetComponentInParent` on itself — it never terminates) and returns the outermost. A rider that traverses under its own name *and* is carried arrives twice the aperture separation past the exit. Non-parented carriers check `traveller.InPortal` instead — see [`WalkerPlatformCarrier`](Assets/Game/Scripts/Vehicles/Systems/WalkerPlatformCarrier.cs).
- **`agent.Warp` fails silently and `agent.isOnNavMesh` is the wrong gate** — a refused warp leaves the agent on the mesh it never left. Check the return value; `PlaceAgent` falls back to `NavMesh.SamplePosition` within 8 m. `WarnIfStuck` (1.5 m tolerance vs the *intended* exit) exists because this failure is otherwise clean-consoled and invisible.
- **`CarryMomentum` before the move, not after.** `PlayerMovement` lerps horizontal velocity 30 % toward a walk every FixedUpdate; without the flag a 40 m/s exit is confiscated in ~0.2 s.
- **A ceiling portal composes a 180° roll.** Hand it to a walking capsule and the player lies down in mid-air. `PlayerPortalTraveller` gives the body yaw only and sends pitch through `PlayerLook.LookAlong` (setting the camera transform is overwritten from the stored float next frame).
- **`Portal.Crossing` must run *before* a projectile's collision cast**, and only once per frame — two facing apertures would otherwise recurse without bound.
- **Never move a portal's transform laterally to follow paint.** `ConformToSurface` moves along the normal only; lateral origin is what `TransferFrom` is built on, and shifting it drags the exit out from under whoever is mid-crossing.
- `ConformToSurface` and `GatherHostSurfaces` must reject `attachedRigidbody != null` and `CharacterController` — otherwise a body standing in front of the paint reads as a bulge and shoves the aperture metres off the wall.
- **`Fits` is checked before tracking, not before teleporting.** Being tracked already disables the wall, so an oversized creature would otherwise walk into the masonry. Refusal is the whole remedy.
- `ShutBehind` closes the **far** end first — `Close()` unpairs as it goes, so the near end first orphans the partner open forever, which looks exactly like a working portal.
- The clone is built **renderer by renderer**, never `Instantiate` + strip: instantiating the player produces a second live networked player (the save system logged the duplicate reassigning an entity id), and the strip cannot finish because Unity refuses to remove `[RequireComponent]` dependencies. Particle/trail/line renderers are deliberately skipped. Skinned copies need `updateWhenOffscreen = true`.
- `SliceClone` seeds its `MaterialPropertyBlock` from the source renderer's block — a blank block strips the suit colour off the half of the player standing in the other room. Slicing only affects materials exposing `_SliceNormal` ([`PortalSliceable.shader`](Assets/Game/Art/Shaders/Portal/PortalSliceable.shader)).
- `TickConformRetry` (30 s) exists because a peer that received placement mid-stream found no host surfaces and never looked again — that player alone walked into a portal everybody else walked through.
- `Clock` is `Time.realtimeSinceStartup` outside play mode; `Time.time` is 0 there, which pinned `_Open` at 0 and clipped the whole aperture away in the Scene view.
- Minor smells: `BodyColourId` and `RimColourId` are both `PropertyToID("_Colour")`; the XML doc above `Fits` actually describes `MightTravel`; the static `Collider[] Sweep` is 64 wide, shared by `SweepVolume` and `GatherHostSurfaces`, and silently drops the overflow. `hostSurface` (serialized, singular) is only the seed — reason about pass-through from `HostSurfaces`.
- `AdvanceTraversal()` and `PortalTraveller.Clone` are public for tests only, not a supported runtime API.

## Extending

1. **A scene-placed pair:** two `Portal` GameObjects at unit root scale, `Link` them in the inspector (`linked` on both), set each `hostSurface`, and uncheck `closeOnTraversal` and leave `lifetime = 0` so they stay open.
2. **Make a surface refuse portals:** add [`NonPortalable`](Assets/Game/Scripts/Portals/PortalPlacement.cs) to it, or keep its layer out of the gun's `surfaceMask`.
3. **A new thing that can go through:** nothing to do — `PortalTraveller.For` adopts anything with a `Rigidbody`, `CharacterController` or `NavMeshAgent`. If its colliders are an unhandled type, extend `ShapeOf`; an unmeasured collider makes the object read as *smaller* than it is and squeeze through holes it should not.
4. **Bring extra world-space state through:** implement `ITeleportAware` (see [SceneTransitions.md](SceneTransitions.md)), not a `PortalTraveller` subclass. Subclass only to change the *move itself*.
5. **A new transform-driven projectile:** call `Portal.Crossing` before your collision cast, trace from the segment it returns, and cross at most once per frame.
6. **Tune the shape:** change `PortalStencil` constants **and** the matching constants in `PortalStencil.hlsl` in the same commit — the picture and the physics read one field, and a disagreement is the failure the class is shaped to prevent.
