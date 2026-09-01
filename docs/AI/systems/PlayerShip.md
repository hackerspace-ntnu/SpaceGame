---
system: PlayerShip
layer: vehicles
summary: The script-generated lander: walkable hover hull, 4 seats, and the one-time crash-landing arrival.
paths:
  - Assets/Game/Scripts/Gameplay/Arrival/
  - Assets/Game/Editor/Vehicles/PlayerShipBuilder.cs
  - Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab
  - Assets/Game/Scripts/Gameplay/Versus/Runtime/
symptoms:
  - "the crash-landing intro never plays and nobody spawns, with one error in the console"
  - "a component I added to PlayerShip.prefab by hand disappeared after a rebuild"
  - "the cabin shakes or the screen comes apart during the descent"
  - "the ship levels out just before it lands instead of crashing into the ground"
  - "the wreck is left standing on its nose, or the camera is inside the terrain at the impact"
  - "my feet are buried through the deck when seated in a chair"
  - "I fall through the ship's floor or cannot climb the boarding stair"
  - "the ship lands buried in terrain or strands the crew in the air"
  - "the arrival cutscene plays for the host only"
  - "the salvage sockets forget what has been taken after a reload"
reads_with: [Vehicles, Cutscenes, Multiplayer, Persistence]
updated: 2026-09-01
---

# PlayerShip

The crashed lander: a script-generated, walkable, drivable 60-tonne hover vehicle that also flies the one-time crash-landing that opens a world.

**Scope:** [Assets/Game/Scripts/Gameplay/Arrival/](Assets/Game/Scripts/Gameplay/Arrival/), [Assets/Game/Editor/Vehicles/PlayerShipBuilder.cs](Assets/Game/Editor/Vehicles/PlayerShipBuilder.cs), [PlayerShip.prefab](Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab), [Assets/Game/Scripts/Spaceship/](Assets/Game/Scripts/Spaceship/)
**Related:** [MountSystem.md](MountSystem.md) · [Vehicles.md](Vehicles.md) · [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md)

> `Assets/Game/Scripts/Spaceship/` is **not** this ship. `SpaceshipManager` + `SpaceshipLaunchInteract` (idle/flight/crash state machine, booster lights, `NetLatch` launch button) belong to [CowBotRocket.prefab](Assets/Game/Prefabs/agents/Vehicles/Spacecraft/CowBotRocket.prefab) — a scenery rocket. PlayerShip carries none of it and has no take-off.

## Model

- Source is the **user's hand-built** [ship_lander_blockout.blend](Assets/Game/Art/Models/_Source~/models/vehicles/ship_lander_blockout.blend). Tooling opens it **read-only** — never edit or regenerate it.
- [player_ship_export.py](Assets/Game/Art/Models/_Source~/models/vehicles/player_ship_export.py) exports two FBXs in one run: [player_ship.fbx](Assets/Game/Art/Models/Vehicles/PlayerShip/player_ship.fbx) (visual) and [player_ship_collision.fbx](Assets/Game/Art/Models/Vehicles/PlayerShip/player_ship_collision.fbx) (baked convex hulls). One run, so axis/scale flags cannot drift apart.
- The export drops `Ref_ExampleHull` (the Tripo reference shell), renames role meshes **in memory** (`Mesh_BoardingStair`, `Mesh_SillPlatform`, …) and localises palette-linked materials.
- Nose arrives along **-X**; `ResolveModelYaw` turns it onto +Z, then the origin is seated under the hull centre at ground level (stair meshes excluded from that measure).
- [PlayerShipBuilder.Build()](Assets/Game/Editor/Vehicles/PlayerShipBuilder.cs) (menu **Tools ▸ Vehicles ▸ Build PlayerShip Prefab**) generates the entire prefab: pivots, colliders, seats, cockpit, sockets, netcode and savers. **Authored = the .blend only. Everything in the prefab is generated.**
- `VerifyParts` / `VerifyOrientation` / `VerifyCollisionCoverage` / `Verify()` abort the build loudly on a renamed or uncovered mesh.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `ArrivalDirector` | [Runtime/ArrivalDirector.cs](Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.cs) | Server-only singleton; spawns hulls, seats crew, gates the launch, walks hulls down |
| `ArrivalDirector` (versus half) | [Runtime/ArrivalDirector.Versus.cs](Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.Versus.cs) | Builds the whole per-team formation at once |
| `ArrivalPath` / `ArrivalTrajectory` | [Core/ArrivalPath.cs](Assets/Game/Scripts/Gameplay/Arrival/Core/ArrivalPath.cs), [Core/ArrivalTrajectory.cs](Assets/Game/Scripts/Gameplay/Arrival/Core/ArrivalTrajectory.cs) | Pure closed-form descending spiral; `Evaluate` is the dive, `EvaluateSettle` the crash that follows it, `RestRotation` the pose the wreck keeps. No integration |
| `ArrivalFormation` | [Core/ArrivalFormation.cs](Assets/Game/Scripts/Gameplay/Arrival/Core/ArrivalFormation.cs) | Per-team arc: mirrored sweep, staggered budget/altitude, `BearingForLandingYaw` |
| `SeatOrdering` | [Core/SeatOrdering.cs](Assets/Game/Scripts/Gameplay/Arrival/Core/SeatOrdering.cs) | Stable insertion sort of seat order; wrapping `SeatFor` |
| `ArrivalFlight` | [Runtime/ArrivalFlight.cs](Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalFlight.cs) | Server-only record: hull + seating + arc + claim count |
| `SeatedRider` | [Runtime/SeatedRider.cs](Assets/Game/Scripts/Gameplay/Arrival/Runtime/SeatedRider.cs) | `NetworkBehaviour` on the hull; holds riders in seats on every machine |
| `ArrivalSaveable` | [Runtime/ArrivalSaveable.cs](Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalSaveable.cs) | One flag, key `"arrival"` |
| `ArrivalCutscene` | [ArrivalCutscene.cs](Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCutscene.cs) | Per-machine presentation only: fade, shake curve, impact hold, blackout |
| `ShipSeat` | [Versus/Runtime/ShipSeat.cs](Assets/Game/Scripts/Gameplay/Versus/Runtime/ShipSeat.cs) | Seat marker component + `Order` |
| `ShipGrounding` | [Versus/Runtime/ShipGrounding.cs](Assets/Game/Scripts/Gameplay/Versus/Runtime/ShipGrounding.cs) | Heightmap first, raycast fallback; `false` means "not yet", not "never" |
| `VersusShipSpawner` | [Versus/Runtime/VersusShipSpawner.cs](Assets/Game/Scripts/Gameplay/Versus/Runtime/VersusShipSpawner.cs) | Owns arena layout, team ship prefab, livery, `EnsureShipAt` / `TryLandingPose` |
| `VehicleDeploymentController` | [Systems/VehicleDeploymentController.cs](Assets/Game/Scripts/Vehicles/Systems/VehicleDeploymentController.cs) | Closes every hatch on mount, reopens on dismount |
| `ArticulatedPart` / `ShipPartSocket` / `ShipPartRack` | [Vehicles/Parts/](Assets/Game/Scripts/Vehicles/Parts/) | Moving panels; 11 salvage sockets as one saved/replicated bitmask |

## Flows

**Arrival (story world)** — entry is [NetworkGameManager.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.cs) `:291`:

1. NGM resolves the spawn anchor from a `SpawnPoint`, streams chunks around it, resolves `spawnPos` **once**, and hands that same point in as the impact site. A restored save returns before this — a loaded world never re-crashes.
2. `SpawnIntoArrival` waits (≤ `seatResolveTimeout` 20 s) for `ShipGrounding` to measure ground; `fatal` (no prefab, zero lateral budget) short-circuits to `SpawnNormally`, which sets `HasArrived = true`.
3. Ship is spawned via `GameServices.World.Spawn` at `ArrivalTrajectory.Evaluate(0)` — top of the arc, `StartAltitude` 2200 m, `LateralBudget` 900 m.
4. Each client gets a body spawned **at the hull**, then one frame later `SeatedRider.Seat(player, seatIndex)`.
5. `FlyFormation` holds the launch until every connected client is seated (≤ `crewGatherTimeout` 12 s), then launches all hulls on the same frame.
6. `FlyDescent` calls `QuietHull` (kinematic, interpolation off, disables `AgentController` / `HoverRigidbodyMotor` / `UnderTerrainGuard`), measures `TouchdownLift`, teleports the transform each frame for `descentDuration` 26 s, then snaps to the exact `t=1` pose — which is **contact, nose-down**, not a landing.
7. The crash: the hull is held at that attitude for `settleHold` 0.2 s, then `Settle` drops it off its nose onto its belly over `settleDuration` 1.4 s, ending on `EvaluateSettle(1)` — exactly the impact point, yaw only. `SetDown` grounds it, then `RestoreHull` and `ParkHull`.
8. After `releaseDelay` 1.6 s the server sets `releasable = true`; crew stand up on **Escape** at their own pace. `strandedSeatTimeout` 180 s force-empties the seats as a backstop.

**Deploy switch** — every sliding leaf is an `ArticulatedPartInteraction` wired to the *whole* side assembly: 4 telescoping leaves + `BoardingStair` + `SillPlatform`. One press runs the lot; mixed states resolve toward "close everything". The stair and platform are authored **deployed** and re-based so stowed is the closed pose. An invisible 32° ramp collider does the carrying (the player capsule has no step offset and cannot climb 0.7 m treads). The aft entrance is the second assembly: the `back_door` ramp droops 40° into a walkable ramp, and two leaves of three telescoping panels each (`BayDoorLeaf_Port1..3` / `BayDoorLeaf_Stbd1..3`) part sideways out of the doorway behind it, elevator-fashion. All seven carry a switch and all seven drive the set, so one press opens the whole aft end. Taking the helm closes all thirteen parts (`closeOnMount`), dismounting reopens them.

**Bay doors** — the ramp is a tail-gate; raised, it only leans across the hole from outside, so the aperture itself was never sealed. The two leaves are **built, not modelled** (`BuildBayDoors`), because nothing in the .blend names the hole — it is the absence between wall slabs. Everything about them is measured off the ship's own collision at build time, which is why they are built *after* `BuildStructuralCollision`, next to the sill platform's threshold: a grid of rays swept forward through the doorway plane (the ramp excluded — closed, it leans across the whole opening), the longest clear run per row taken as that row's span, and rows under `BayDoorMinSpan` discarded so the seams between abutting slabs stay out of the answer. The bulkhead's inboard face is *walked*, not raycast — a ray reports the face it enters, never the one it leaves, and the wall here is several baked hulls deep. Each panel is a scaled primitive cube (its `BoxCollider` comes along scaled, which is exactly what a closed door wants), so the arch frames rectangular panels and no attempt is made to follow the curve. Panels are overhung at the sides and head but **not at the foot** — the floor seals that, and the deck slab stands 0.02 m under the sill, so an overhang there starts the pocket `BoxCast` inside a collider and it measures zero.

**Why three panels a side.** There is no pocket to hide a solid leaf in. Measured, the wall beside the ~3.75 m aperture is clear for 1.4–1.8 m at chest height but only **0.77 m** at its worst — the hull skin (`Plane.001/002`) curving up at the sill, and `Cube.059/075` overhead — and a full-height leaf is bound by its worst band. Two 2 m leaves would part to about a third of the doorway and read as doors jammed halfway. Three panels a side put the retracted stack at 0.67 m, which fits 0.77 m with 0.22 m to spare and opens the doorway fully; opening slides every panel onto the outermost one's slot, which tucks a width further, so they arrive staggered — the four-leaf side door's cascade at the aft end. If a remodelled bulkhead ever cannot take the stack, the **build fails** and names the measured pocket rather than shipping a doorway too narrow to walk through.

**Seating (4 seats)** — four `Cockpit_Seat_Command*` chairs. The front-left is the **helm**: nothing sits on it; the directly-interactable root `MountModule` answers for the whole fuselage. The other three get their own `MountModule` + `MountNetworkSync` behind a **trigger** `BoxCollider` (`SeatVolumePadding` 0.25) — a trigger is the one thing `Interactor` will not resolve upward. Separately, four `ArrivalSeat1..4` markers under `ArrivalSeats` are what `SeatedRider` uses; poses are measured off each chair's **cushion mesh** (`SeatedPivotAboveCushion` 0.55 over a pivot that already sits `PlayerPivotHeight` 1 m above the soles).

**The descent is committed** — it does not flare. The dive angle is measured off the arc and capped
at `MaxPitchDegrees` (70°), and the late arc is steeper than that cap, so the ship **hits the ground
at exactly the cap** and `MaxPitchDegrees` is therefore the impact attitude, not just a sanity
limit. Two consequences the old flare hid:

- `TouchdownLift` holds the hull above the impact point at `t=1` by the difference between its belly
  depth pitched and its belly depth level (`ShipHull.BellyDropAt`), so the part of it that reaches
  the ground is the **nose** and not the cockpit the crew are sitting in. The settle takes the lift
  back out, which is what makes the hull drop as it rotates.
- The pose the world keeps is `EvaluateSettle(1)`, not the descent's last frame. Everything
  downstream — `ShipGrounding`, `ShipHull.BellyDrop`, the saved wreck — still assumes a hull that
  differs from its prefab by **yaw alone**, and the settle is what guarantees that.

## Multiplayer

- `ArrivalDirector` is a plain `MonoBehaviour`, exists on every machine, but only **acts** on the server (`Network.Server` guards). It owns no replicated state.
- Hull motion is server-written and reaches peers through the prefab's own `ClientNetworkTransform`.
- `SeatedRider` is the `NetworkBehaviour`. Two channels: `NetMsg.TakeSeat`/`LeaveSeat` (event) and a `NetworkList<ulong> occupants` + `NetworkVariable<bool> releasable` (state, for late joiners and self-repair each frame).
- **Riders are never reparented.** The player `NetworkTransform` is owner-authoritative and world-space, so `HoldSeats()` in `LateUpdate` writes only bodies this machine owns. LateUpdate is required: the descent coroutine resumes before LateUpdate, so `Update` placement lags a frame and reads as the cabin shaking.
- The cutscene is started by the static `SeatedRider.LocalPlayerSeated` event, **not** from the descent coroutine — that runs on the server only and would play for the host alone.
- Versus: one team-coloured ship per team via `VersusShipSpawner.EnsureShipAt`; `ShipTeamAccent` + `ShipAccentRecolor` put the swatch on the wire. All landing sites are measured before any hull spawns — all-or-nothing.

## Persistence

| What | Where |
| --- | --- |
| "This world has been arrived in" | `ArrivalSaveable`, key `arrival`. Captured `true` even mid-descent — a resumed crash is worse than a cut-short one |
| Wreck pose | The prefab's own `SaveableEntity` + `TransformSaveable` |
| Hover motor state | `MotorStateSaveable` |
| Door / stair / platform / bay-door poses | `ArticulatedPartsSaveable` — keyed by hierarchy path, so the two bay doors needed no wiring. A save written before they existed has no entry for them and leaves them as the prefab authored them (shut), which is the right default |
| Salvage progress | `ShipPartRack` bitmask (socket order = **name sort**, so re-exports keep bit meaning) |
| Not saved | Seat occupancy — the descent is over by the time any save is legitimate |

## Gotchas

- **`PlayerShipBuilder` rewrites the prefab wholesale.** Any component, marker or Inspector tweak added to `PlayerShip.prefab` by hand is destroyed by the next build, silently. Every fix belongs in the builder. This is documented in the builder itself at `BuildArrivalSeats`, and `Verify()` exists precisely because the losses are invisible (a ship with no `SeatedRider` flies its descent with nobody aboard).
- **Collision is a baked convex decomposition, not a per-mesh rule.** All hulls live on one `COL_Hulls` holder as convex `MeshCollider`s; `CollectHulls` asserts every baked hull shares one transform. The canopy dome deliberately gets *no* collider (a 3 m character's head sits inside the glass). `COLLISION_SKIP` in the export script and `NoStructuralCollider` in the builder must stay reconciled — `VerifyCollisionCoverage` fails the build if they drift.
- **Spawn-point dependency.** No `SpawnPoint` in the scene ⇒ `TryGetSpawnAnchor` fails and NGM refuses to spawn anyone; the arrival never runs and the console shows one error. The impact site *is* the resolved spawn position, and it must never be re-resolved (a second resolve returns a different scattered point from the one the terrain was streamed around).
- **`ShipGrounding` returning false means "wait"**, not "give up" — in a streamed world it means the chunk has not loaded. Treating it as fatal buries or strands hulls.
- **The settle is the only thing that levels the ship.** Shortening `settleDuration` to zero, or
  returning early from `Settle`, leaves the wreck standing on its nose at `MaxPitchDegrees` —
  permanently, because that is the pose the save keeps and the pose the landing was measured
  against. Retune the *impact attitude* with `MaxPitchDegrees` instead; the settle then plays out
  whatever angle it is given.
- **`ArrivalCutscene` is told the settle window, not just the descent.** The hull keeps moving after
  first contact, so `Configure(descentDuration, settleHold + settleDuration)` is what stops the
  screen fading to black halfway through the crash.
- **Never leave the hull's own drivers live during a descent.** `QuietHull` is the fix for the "screen coming apart" bug: `HoverRigidbodyMotor`, `AgentController` and `UnderTerrainGuard` all write the same Rigidbody the descent is teleporting.
- **Seat markers are not chair transforms.** FBX chairs arrive ~150× scaled with baked exporter yaw reading 180 whichever way they point; poses must be measured from geometry. A marker on the deck buries the feet a metre through it.
- `seatOffset` on `SeatedRider` is meant to stay zero — markers are already the answer.
- Registration: `PlayerShip.prefab` must be in the network prefab list (`NetworkPrefabRegistrar.Sync` runs at build) and its `GlobalObjectIdHash` must be non-zero — a script-built `NetworkObject` ships 0, which is why the builder re-imports and force-reserialises after saving.

## Extending

**Change the ship geometry**
1. Edit [ship_lander_blockout.blend](Assets/Game/Art/Models/_Source~/models/vehicles/ship_lander_blockout.blend) by hand in Blender. Keep the names in `RequiredParts` and `PartNames` intact.
2. `blender --background --python player_ship_export.py` — writes both FBXs.
3. Run **Tools ▸ Vehicles ▸ Build PlayerShip Prefab**. Read the console: `VerifyParts`, `VerifyCollisionCoverage` and `Verify()` fail loudly rather than shipping a hull with holes.
4. Run [PlayerShipTests.cs](Assets/Game/Editor/Tests/PlayerShipTests.cs) (`MainDeckAisleIsWalkable`, `CollisionIsAllConvex`, `EachChairOffersItsOwnSeat`, `AftDoorwaySealsShutAndClearsOpen`).
5. Verify on an actual client and by reloading a save.

**Add a new ship station (e.g. a fifth seat, a console)**
1. Model it in the .blend under a prefix the export already stamps (`Cockpit_*` → fitting collider; `Part_*` → salvage socket).
2. Add the measurement + component wiring to `PlayerShipBuilder` — a new `Build…` method called from `Build()`, never a hand edit to the prefab.
3. If it is a seat: emit a `ShipSeat` marker under `ArrivalSeats` and a passenger `MountModule` behind a trigger volume (`BuildPassengerSeat`); measure the pose with `MeasureSeat`, not the chair transform.
4. Extend `Verify()` with the count so a future rebuild that drops it fails instead of shipping quietly.
5. If it holds runtime state, add a saver in `BuildRootComponents` and confirm the key appears in the save JSON.
