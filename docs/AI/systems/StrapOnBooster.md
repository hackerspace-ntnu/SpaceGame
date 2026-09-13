---
system: StrapOnBooster
layer: items
summary: "A five-charge rocket pack clamped to any surface: server publishes the clamp, the body's owner pushes"
paths:
  - Assets/Game/Scripts/Items/Artifacts/StrapOnBooster
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/StrapOnBooster.prefab
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/ClampedStrapOnBooster.prefab
  - Assets/Game/Resources/Items/Artifacts/StrapOnBooster.asset
  - Assets/Game/Art/Models/Items/strap_on_booster.fbx
  - Assets/Game/Art/Models/_Source~/models/gear/strap_on_booster_export.py
  - Assets/Game/Editor/Tests/BoosterClampTests.cs
  - Assets/Game/Editor/Tests/BoosterWiringTests.cs
symptoms:
  - "the strap-on booster does nothing at all when I aim at the ground or a wall"
  - "the booster only works on crates and never on anything else"
  - "the booster clamps to a crate but the crate does not move"
  - "a booster stuck to a creature burns and the creature walks off unaffected"
  - "an NPC or a creature disappears the moment a booster is strapped to it"
  - "a booster stuck to a mounted rider does nothing"
  - "the booster is spent on a press that clamped nothing"
  - "a client's booster clamps to whatever the host was looking at"
  - "the booster hangs in the air instead of riding the thing it is stuck to"
  - "peers see the booster at the prefab pose and never on the target"
  - "the flame points somewhere the booster is not pushing"
  - "the booster's plume is a stub, or fills the screen"
  - "the spent booster fires itself across the desert when the burn ends"
  - "[StrapOnBooster] The clamped booster prefab has no BoosterShell with both markers assigned"
  - "the booster stopped working after the model was re-exported"
reads_with: [Artifacts, Multiplayer, Combat]
updated: 2026-09-12
---

# Strap-on booster

A pack of five rockets you clamp to things and run away from. Each one burns for two seconds along
its own axis as clamped, then lets go and drops. **How you stick it on is the aim** — a booster on the
side of a crate slides the crate, one on the underside flies it, one on a cliff face burns and
moves nothing.

**Scope:** everything under
[Items/Artifacts/StrapOnBooster](Assets/Game/Scripts/Items/Artifacts/StrapOnBooster) — the item, the
clamp, the mount, the boosted body and the shell. The brief is
[Artifacts/StrapOnBooster.md](Artifacts/StrapOnBooster.md); this page wins.

## Model

**Two prefabs, not one.** `StrapOnBooster.prefab` is the pack in the hand and in the sand;
`ClampedStrapOnBooster.prefab` is what exists in the world once one has been stuck to something.
Both are registered network prefabs.

**The clamp takes any surface, and pushing is a separate question.** `BoosterClamp.BodyFor` answers
what the booster rides — a `NetworkObject` first, a `Rigidbody` second, **null for the world itself**
(terrain, a wall, a chunk rock). `BoosterClamp.CanPush` is a *prediction*, not a permission: it says
whether the thing will move, which is what the crosshair promises and what `BoostedBody` spends the
thrust on. Nothing refuses a clamp.

**The burn is a deadline, not a countdown.** `Strap.BurnEndsAt` is a time on the server's clock,
written once. A joiner arriving mid-burn reads the time left; one arriving after it gets the husk.

**The booster is never reparented.** It follows its target by arithmetic —
`target.TransformPoint(local)` every `LateUpdate` — because Netcode refuses a parent that is not a
spawned `NetworkObject`, reverts a reparent on an unspawned one in silence, and replicates no
`localPosition`. The pose is stored LOCAL, so a target that turns carries the booster round with
it.

## Key types

| Type | Does |
| --- | --- |
| `StrapOnBoosterItem` | The pack in the hand. `ToolItem`, `UseAuthority.Server`. Aims, spawns, clamps, and spends a charge only on the press that stuck |
| `BoosterClamp` | Static. `BodyFor` (what it rides), `CanPush` (will it move), `Seat` (how it sits) — the same answers on every machine |
| `BoosterMount` | On the clamped prefab. Owns the replicated `Strap`, the follow, the burn, the husk and the despawn |
| `BoostedBody` | Added at runtime to whatever is being pushed. Applies every booster on that body, on the machine that drives it, and bills the crash |
| `BoosterShell` | The model: markers, armed/spent geometry, the optional jaw, the plume and the smoke |
| `BoosterImpactConfig` | The crash curve, read by `OrnithopterCrash` |

## Flows

**Press.** `OnRequestUse` runs on the holder's machine — the only one whose aim is honest. It puts
the hit point in `P` and the **surface normal** in `R` (as `FromToRotation(forward, normal)`, because
`LookRotation` is degenerate on the commonest clamp of all, straight down onto a top), and names the
body only when there is one. No body means a clamp on the world.

**Clamp.** `Use()` runs on the server. It re-checks the range, seats the booster with
`BoosterClamp.Seat` so the bell points OUT of the surface, spawns `ClampedStrapOnBooster` with its
mounting face on the hit, and calls `BoosterMount.Clamp`. **Every refusal on the way calls
`CancelUse()`**, so a press that clamped nothing costs no charge; reaching the end without one
spends one of the five, and the fifth takes the pack out of the hotbar through the default
`OnMaxUsesReached`. The count is `maxUses` on `UsableItem`, which is the count `ItemState` already
persists under `"uses"`.

**Burn.** Every machine follows the target and draws the flame. `BoostedBody.FixedUpdate` runs at
execution order 200 — after `PlayerMovement`, which *assigns* horizontal velocity and would delete a
push applied before it — and takes one of three branches:

1. `ITowable` → `RequestThrust` with the combined thrust as an **acceleration**, which each machine
   spends in its own terms: against the flight path, against the gait, or off the NavMesh. This is
   also what catches a mounted rider, whose own body is kinematic and parented into a seat.
2. A player on their own feet → a velocity add plus `Movement.CarryMomentum()`, without which air
   control lerps the launch away inside a fifth of a second.
3. Anything else dynamic → `AddForceAtPosition` at the clamp, scaled by the body's own mass, so the
   linear result is mass-independent and an off-centre clamp spins the thing. That is the item.

**Burnout.** The lamp goes out (the spent model replaces the armed one — one booster is one charge,
so there is no fraction to draw), the plume and smoke stop, the clamp detaches, and the husk becomes
a loose body carrying the point velocity it was riding. It lingers 6 s, then the server despawns it.

## Multiplayer

Server-authoritative for the clamp; **owner-authoritative for the push**. The server publishes one
`NetworkVariable<Strap>` and `BoostedBody` asks `Network.Owns(this)` before moving anything, because
a player's `NetworkTransform` is owner-authoritative and a server-written force on it is overwritten
within a tick with nothing in the console.

`Strap.Attached` is the flag that says a clamp has landed; it cannot be inferred from `Target`,
because a world clamp names nobody — a zero `Target` means "the world", not "not yet".

Crash damage goes through `NetDamage.Apply` on the machine that was pushing, and only if it spent
the thrust: a towed machine prices its own arrivals, so `billsImpacts` stays false there.

## Persistence

The charge count, and nothing else. It rides `UsableItem`'s own `"uses"` key in the slot's
`ItemState`, so a half-spent pack comes back half-spent. A burning booster is two seconds of state
and is not saved; `BoostedBody` is runtime-only and holds nothing worth a record.

## Presentation

The exhaust is the jetpack's, on purpose — the same `SpaceGame/Effects/JetFlame` cone and
`SpaceGame/Effects/JetSmoke` puffs. `Exhaust/Plume` is a **mesh**, the shared
`Assets/Game/Art/Models/Generated/JetFlameCone.asset`: the shader reads its own object space as the
flame's frame (base y = 0 radius 1, tip y = 1 radius 0) and holds no measurements, so the transform
carries the size — 0.058 m at the bell mouth, 0.406 m long — and its **+Y must point the way the
exhaust leaves**. No throttle, so `_Throttle` is pinned at 1 through a `MaterialPropertyBlock` and
the renderer is switched off at burnout.

## Gotchas

- **`CanPush` is not permission.** It used to be, and the item then refused every surface that does
  not move — the sand, the walls, the parked hulls, every creature on a `NavMeshAgent`. The press did
  nothing and read as the item being broken. Clamp first; ask what moves second.
- **A world clamp has no target.** Guard `target == null` with `strap.Target != 0` before reading it
  as "what I was riding is gone", or every booster stuck to terrain extinguishes on its first frame.
- **Never name the collider in `NetArg`.** Terrain and chunk scenery are scene objects with no
  network id, so a peer cannot resolve them — a world clamp sends no subject at all.
- **A live `NavMeshAgent` eats forces.** It writes the transform every frame, so the Rigidbody
  underneath it is not what moves the creature. A motor that wants shoving implements `ITowable`.
- **The plume cannot be a billboard.** `JetFlame` is an object-space cone shader; on a particle
  renderer it draws nothing recognisable. `BoosterWiringTests` guards it.
- **The husk must not collide with what it let go of.** It is still inside that body's collider at
  burnout, and a solver handed two overlapping bodies fires one across the desert — `Release` calls
  `Physics.IgnoreCollision` for exactly that frame.
- **`Marker_Muzzle` is load-bearing, and deleting it fails quietly in Blender.** `BoosterShell.IsWired`
  is false without it, so `Use()` logs one error and refuses every clamp — the item looks broken
  rather than mis-modelled. `strap_on_booster_export.py` ships **by collection membership only**, so
  a part left in the Scene Collection is absent from the FBX and the prefab's reference goes silently
  null. After any re-export check `shell.muzzle` — `BoosterWiringTests` does.
- **Colliders are off for the whole burn.** A booster strapped to a crate is part of the crate; a
  live collider there answers the next player's aim ray with the booster instead of the crate.
- **A thruster may never hand a machine a point to be pulled towards.** This asked for a tow 60 m
  out along the thrust, the shape the hook's rope has; every implementor but the ornithopter reads
  that ask as **one step's displacement**. `NavMeshAgentMotor` moved the creature 60 m per physics
  step — 3 km/s, gone before the flame was drawn, and it read as the NPC disappearing on contact
  with nothing in the console. Push with `RequestThrust`.
- **A launched creature is not billed for its landing.** `billsImpacts` is false on the `ITowable`
  branch and a carried creature has no crash curve, so an animal rocketed 95 m up lands unhurt.

## Extending

- **A booster that can be stuck without lighting** would need a record of its own; today it lights on
  attach. **A machine that should be shoveable** implements `ITowable` — never a special case in
  `BoosterClamp` — and answers `RequestThrust` in the units it moves in.
- **Fall damage for a launched creature** belongs to the carry that drops it
  ([CarriedAgent.md](CarriedAgent.md)), not here: a leash on a jetpack pilot asks the same question.
- **Tuning** lives on `BoosterMount` (`burnSeconds`, `thrustAcceleration`, `impact`,
  `impactWindowSeconds`, `spentLingerSeconds`) and on the item (`maxUses`, `range`,
  `serverRangeTolerance`, `showAimHint`). 40 m/s² against this world's 18 of gravity leaves 22 of
  climb: a player leaves the ground at 44 m/s and tops out near 95 m — past the 34 m/s the crash
  curve calls lethal, so the landing is the player's problem. It was 26.5 while the item was tuned
  to stay under it.
