---
system: PlayerShip
layer: vehicles
summary: The script-generated lander: walkable hover hull, 4 seats, the entry burn, and the crash-landing arrival.
paths:
  - Assets/Game/Scripts/Gameplay/Arrival/
  - Assets/Game/Art/Shaders/Effects/EntryPlasma.shader
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
  - "I board the ship from outside by looking at the cockpit through the canopy"
  - "pressing E anywhere on the hull puts me in the pilot's chair"
  - "I fall through the ship's floor or cannot climb the boarding stair"
  - "the ship lands buried in terrain or strands the crew in the air"
  - "the ship finishes its dive in mid-air and then sinks slowly for minutes"
  - "the arrival log says the ship landed cleanly but it is visibly hanging in the sky"
  - "the arrival cutscene plays for the host only"
  - "the arrival cutscene runs on a different clock on the host and the client"
  - "I can see the ship hit the ground before the screen goes black"
  - "the salvage sockets forget what has been taken after a reload"
  - "the crew stand up out of the wreck able to walk on air, never falling again"
  - "the gear wall's headroom check names the gear wall itself as the thing overhead"
  - "the arrival logs that the heightmap and the colliders disagree about the ground under the ship"
  - "the ship lands a couple of metres in the air when an NPC or a mount is standing near the impact site"
  - "the ship falls the whole way down with no sign of atmospheric heating"
  - "the entry fire is drawn over the inside of the cabin as well as out of the window"
  - "the entry burn is still blazing when the screen fades to black at the impact"
  - "a parked ship, or a wreck loaded from a save, is sitting inside a ball of orange fire"
  - "there is a hard oval seam in the air around the burning ship where the effect stops"
reads_with: [Vehicles, Cutscenes, Multiplayer, Persistence]
updated: 2026-09-02
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
- The nose's arrival axis is **measured, not assumed** — `ResolveModelYaw` compares the canopy against the back door and yaws only if it must. On the current export it measures **0°** (the nose already faces +Z; the old "-X, yawed 90°" note is stale). The origin is then seated under the hull centre at ground level, stair meshes excluded from that measure.
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
| `ArrivalCutscene` | [ArrivalCutscene.cs](Assets/Game/Scripts/Presentation/Cutscenes/Actions/ArrivalCutscene.cs) | Per-machine presentation only: black on seating, held until the launch, then shake curve, fade finishing **at** contact, blackout |
| `EntryBurn` + `EntryPlasma` | [EntryBurn.cs](Assets/Game/Scripts/Gameplay/Arrival/Presentation/EntryBurn.cs), [EntryBurnCurve.cs](Assets/Game/Scripts/Gameplay/Arrival/Core/EntryBurnCurve.cs), [EntryPlasma.shader](Assets/Game/Art/Shaders/Effects/EntryPlasma.shader) | The atmospheric burn, on the HULL: an ellipsoid plasma shell (additive, `Cull Front`, `ZTest LEqual`, `Queue Transparent-100`) plus a cabin glow lamp, both switched off `SeatedRider.SecondsSinceLaunch`. The curve is the pure envelope and the one shared `Flicker`. Presentation only |
| `ShipSeat` | [Versus/Runtime/ShipSeat.cs](Assets/Game/Scripts/Gameplay/Versus/Runtime/ShipSeat.cs) | Seat marker component + `Order` |
| `ShipGrounding` | [Versus/Runtime/ShipGrounding.cs](Assets/Game/Scripts/Gameplay/Versus/Runtime/ShipGrounding.cs) | Two sources, for two different questions. `TryResolveGround` (heightmap first, raycast fallback) **plans**; `TryResolveCollisionGround` / `TryMeasureLandingAgainstCollision` **verify**, against colliders, ignoring the hull. `false` means "not yet", not "never" |
| `VersusShipSpawner` | [Versus/Runtime/VersusShipSpawner.cs](Assets/Game/Scripts/Gameplay/Versus/Runtime/VersusShipSpawner.cs) | Owns arena layout, team ship prefab, livery, `EnsureShipAt` / `TryLandingPose` |
| `VehicleDeploymentController` | [Systems/VehicleDeploymentController.cs](Assets/Game/Scripts/Vehicles/Systems/VehicleDeploymentController.cs) | Closes every hatch on mount, reopens on dismount |
| `ArticulatedPart` / `ShipPartSocket` / `ShipPartRack` | [Vehicles/Parts/](Assets/Game/Scripts/Vehicles/Parts/) | Moving panels; 11 salvage sockets as one saved/replicated bitmask |

## Flows

**Arrival (story world)** — entry is [NetworkGameManager.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.cs) `:291`:

1. NGM resolves the spawn anchor from a `SpawnPoint`, streams chunks around it, resolves `spawnPos` **once**, and hands that same point in as the impact site. A restored save returns before this — a loaded world never re-crashes.
2. `SpawnIntoArrival` waits (≤ `seatResolveTimeout` 20 s) for `ShipGrounding` to measure ground; `fatal` (no prefab, zero lateral budget) short-circuits to `SpawnNormally`, which sets `HasArrived = true`.
3. Ship is spawned via `GameServices.World.Spawn` at `ArrivalTrajectory.Evaluate(0)` — top of the arc, `StartAltitude` 2200 m, `LateralBudget` 900 m.
4. Each client gets a body spawned **at the hull**, then one frame later `SeatedRider.Seat(player, seatIndex)`.
5. `FlyFormation` holds the launch until every connected client is seated (≤ `crewGatherTimeout` 12 s), then, on the same frame, calls `SeatedRider.AnnounceLaunch` on each hull and launches them all. That announcement is the instant every machine's presentation is timed from.
6. `FlyDescent` calls `QuietHull` (kinematic, interpolation off, disables `AgentController` / `HoverRigidbodyMotor` / `UnderTerrainGuard`), measures `TouchdownLift`, teleports the transform each frame for `descentDuration` 26 s, then snaps to the exact `t=1` pose — which is **contact, nose-down**, not a landing.
7. The crash: the hull is held at that attitude for `settleHold` 0.2 s, then `Settle` drops it off its nose onto its belly over `settleDuration` 1.4 s, ending on `EvaluateSettle(1)` — exactly the impact point, yaw only. `SetDown` grounds it **against collision** (heightmap only as a fallback), then `RestoreHull` and `ParkHull`.
8. After `releaseDelay` 1.6 s the server sets `releasable = true`; crew stand up on **Escape** at their own pace. `strandedSeatTimeout` 180 s force-empties the seats as a backstop.

**Atmospheric entry** — `EntryBurn` sits on the SHIP rather than the camera and derives everything from `SeatedRider.SecondsSinceLaunch` (a replicated instant on the server's clock) and the local `ArrivalDirector.DescentDuration`, so **nothing about the fire is on the wire** and in versus every team's hull burns on every screen with no second code path. Two layers off one number: an ellipsoid **plasma shell** enclosing the hull, drawn on its BACK faces — the mesh only says where on screen the burn might be, each pixel colours itself from its direction on that shell in the ship's OBJECT space, which pins the hot cap to the nose and streams the wake aft however the head turns — and one **cabin glow** lamp forward of the crew under the canopy, so it reads as light that came in through the glass. `EntryBurnCurve.Flicker` is computed once on the CPU and handed to both, so the cabin light is in phase with the fire outside; sampled separately they read as two unrelated faults. **It is visible only through the window with no mask and no stencil**: the shell draws with an ordinary `ZTest LEqual` against the opaque pass's depth — the cabin walls are opaque and two metres away so they reject a shell twenty metres behind them, the canopy is transparent with `ZWrite` off (`MakeCanopyGlass`) and writes no depth so the burn survives exactly across the glass, and the same test silhouettes the ship against its own plasma from outside. Retuning: shader defaults *and* `PlayerShipBuilder.EntryPlasmaMaterial()` (colour/shape), `EntryBurnCurve.Default` (timing), `EntryShell*` (size).

**Deploy switch** — every sliding leaf is an `ArticulatedPartInteraction` wired to the *whole* side assembly: 4 telescoping leaves + `BoardingStair` + `SillPlatform`. One press runs the lot; mixed states resolve toward "close everything". The stair and platform are authored **deployed** and re-based so stowed is the closed pose. An invisible 32° ramp collider does the carrying (the player capsule has no step offset and cannot climb 0.7 m treads). The aft entrance is the second assembly: the `back_door` ramp droops 40° into a walkable ramp, and two leaves of three telescoping panels each (`BayDoorLeaf_Port1..3` / `BayDoorLeaf_Stbd1..3`) part sideways out of the doorway behind it, elevator-fashion. All seven carry a switch and all seven drive the set, so one press opens the whole aft end. Taking the helm closes all thirteen parts (`closeOnMount`), dismounting reopens them.

**Bay doors** — the ramp is a tail-gate; raised, it only leans across the hole from outside, so the aperture itself was never sealed. The two leaves are **built, not modelled** (`BuildBayDoors`), because nothing in the .blend names the hole — it is the absence between wall slabs. Everything about them is measured off the ship's own collision at build time, which is why they are built *after* `BuildStructuralCollision`, next to the sill platform's threshold: a grid of rays swept forward through the doorway plane (the ramp excluded — closed, it leans across the whole opening), the longest clear run per row taken as that row's span, and rows under `BayDoorMinSpan` discarded so the seams between abutting slabs stay out of the answer. The bulkhead's inboard face is *walked*, not raycast — a ray reports the face it enters, never the one it leaves, and the wall here is several baked hulls deep. Each panel is a scaled primitive cube (its `BoxCollider` comes along scaled, which is exactly what a closed door wants), so the arch frames rectangular panels and no attempt is made to follow the curve. Panels are overhung at the sides and head but **not at the foot** — the floor seals that, and the deck slab stands 0.02 m under the sill, so an overhang there starts the pocket `BoxCast` inside a collider and it measures zero.

**Why three panels a side.** There is no pocket to hide a solid leaf in. Measured, the wall beside the ~3.75 m aperture is clear for 1.4–1.8 m at chest height but only **0.77 m** at its worst — the hull skin (`Plane.001/002`) curving up at the sill, and `Cube.059/075` overhead — and a full-height leaf is bound by its worst band. Two 2 m leaves would part to about a third of the doorway and read as doors jammed halfway. Three panels a side put the retracted stack at 0.67 m, which fits 0.77 m with 0.22 m to spare and opens the doorway fully; opening slides every panel onto the outermost one's slot, which tucks a width further, so they arrive staggered — the four-leaf side door's cascade at the aft end. If a remodelled bulkhead ever cannot take the stack, the **build fails** and names the measured pocket rather than shipping a doorway too narrow to walk through.

**Seating (4 seats)** — four `Cockpit_Seat_Command*` chairs, each boarded from a **trigger** `BoxCollider` wrapping the chair (`AddSeatVolume`, `SeatVolumePadding` 0.25) — a trigger is the one thing `Interactor` will not resolve upward, so the volume is reached before the chair's own mesh and is see-through from everywhere else. The front-left is the **helm**: its volume (`Cockpit/HelmSeat`) carries a `MountStation` wired to the **root** `MountModule`, and that module has `mountableByDirectInteraction = false` so no other collider on the hull offers a seat. The other three carry their own `MountModule` + `MountNetworkSync` + `ChairPose` (`BuildPassengerSeat`). Nothing new is on the wire: `MountStation.Interact` routes through the root `MountNetworkSync.RequestMount`, the same server-decided path `MountModule.Interact` took, and no `MountNetworkSync` moved so the positional `MountIndex` of all four seats is unchanged. Separately, four `ArrivalSeat1..4` markers under `ArrivalSeats` are what `SeatedRider` uses; poses are measured off each chair's **cushion mesh** (`SeatedPivotAboveCushion` 0.55 over a pivot that already sits `PlayerPivotHeight` 1 m above the soles).

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
- The cutscene is started by the static `SeatedRider.LocalPlayerSeated` event, **not** from the descent coroutine — that runs on the server only and would play for the host alone. It only goes to black there; its *timed* beats are released by `SeatedRider.LocalCrewLaunched`, raised from `NetMsg.ArrivalLaunched` (server → everyone, on the ship's relay) and, for a late joiner who missed it, from the replicated `launchedAt` read when they are seated.
- Seated players' heads are on the wire too: `PlayerHeadLook` turns the head/neck bones and `PlayerViewNetwork` carries the yaw beside the pitch, so a crewmate looking round the cabin looks round it on every machine. See [PlayerCharacter](PlayerCharacter.md).
- Versus: one team-coloured ship per team via `VersusShipSpawner.EnsureShipAt`; `ShipTeamAccent` + `ShipAccentRecolor` put the swatch on the wire. All landing sites are measured before any hull spawns — all-or-nothing.

## Persistence

| What | Where |
| --- | --- |
| "This world has been arrived in" | `ArrivalSaveable`, key `arrival`. Captured `true` even mid-descent — a resumed crash is worse than a cut-short one |
| Wreck pose | The prefab's own `SaveableEntity` + `TransformSaveable` |
| Hover motor state | `MotorStateSaveable` |
| Door / stair / platform / bay-door poses | `ArticulatedPartsSaveable` — keyed by hierarchy path, so the two bay doors needed no wiring. A save written before they existed has no entry for them and leaves them as the prefab authored them (shut), which is the right default |
| Salvage progress | `ShipPartRack` bitmask (socket order = **name sort**, so re-exports keep bit meaning) |
| The entry burn | **Nothing.** A loaded world never re-crashes, and a restored hull reports `SecondsSinceLaunch` of -1, which is already "dark" |
| Rider at the helm | One `MountSaveable` (rider `SaveRef` only), on the **root** — `SaveablePolicy.Ensure` runs on the prefab root alone, so the three passenger `MountModule`s have never had one. Unchanged by the helm's station, which holds no state and does not move the module the saver reads. Arrival seat occupancy is **not** saved: the descent is over by the time any save is legitimate |

## Gotchas

- **`PlayerShipBuilder` rewrites the prefab wholesale.** Any component, marker or Inspector tweak added to `PlayerShip.prefab` by hand is destroyed by the next build, silently. Every fix belongs in the builder. This is documented in the builder itself at `BuildArrivalSeats`, and `Verify()` exists precisely because the losses are invisible (a ship with no `SeatedRider` flies its descent with nobody aboard).
- **Collision is a baked convex decomposition, not a per-mesh rule.** All hulls live on one `COL_Hulls` holder as convex `MeshCollider`s; `CollectHulls` asserts every baked hull shares one transform. The canopy dome deliberately gets *no* structural collider (a 3 m character's head sits inside the glass) — and therefore gets an interaction-only one: `CanopyBlocker`, a **trigger box** over the dome's own bounds carrying [`InteractionBlocker`](Assets/Game/Scripts/Gameplay/Interaction/Core/InteractionBlocker.cs), built by `BlockReachThroughCanopy`. Without it "no collider" also meant "nothing in the way", and the cockpit chairs' own boarding triggers were the first thing an outside ray met: the ship was boardable from the air over its nose, out to the player's whole 20 m reach. The dome's **bounds** and not its mesh — a convex hull of the glass is thinnest at its aft rim and the chairs sit low and aft inside it, which left 129 of the 282 approaches open. `Verify()` fails the build if it is missing or is not a trigger box; `PlayerShip_NoChairIsBoardedFromOutsideTheHull` and `PlayerShip_EveryChairIsBoardedFromWhereItPutsYouDown` are the two halves of it (blocked from outside, never from the deck the chair puts you down on). `COLLISION_SKIP` in the export script and `NoStructuralCollider` in the builder must stay reconciled — `VerifyCollisionCoverage` fails the build if they drift. **So fit anything that stands inside the hull against the bake, not against the meshes you can see**: the skin `Plane.001` curves up off the deck and its hulls fill the curve, making the outboard half-metre of floor solid for ~0.36 m with nothing drawn there. The gear wall stood inside that for a day at `WallRibClearance` 0.70 (now **1.00**) and no probe caught it, because they all ask about its placement face and the face starts 0.572 m up — see `PlayerShip_InventoryWallStopsShortOfTheOverhead` and [Backpack](Backpack.md). Same trap again on 2026-09-01: `WallDepth` was `TRAY_D` x scale, a hand edit to the `.blend` made the *surround* the deepest part, and the fitting sat 0.29 m too far outboard. It is now the fitting's **measured** back reach (0.6856), which `inventory_wall_scale.py` prints; `WallGridCentreHeight` is 2.1465. The room caps the wall's drawn size at 1.065x — it is drawn 1.06x, leaving 0.282 m of rib clearance.
- **The crew exist unheld for one frame, 2200 m up.** `SeatIntoFlight` spawns the body at the hull's own position and only seats it after `yield return null`, because a `NetworkObject` is not addressable by id until the next frame. For that frame the player is an unheld, free-falling body over chunks the streamer has not reached — which is a state other systems react to. `UnderTerrainGuard` parks it (see [WorldStreaming](WorldStreaming.md) gotchas): that used to be captured by `SeatedRider` as the player's normal physics and handed back on standing up, which is how the crew ended up walking on air. Anything new that reads or writes the player's physics must be safe in that window.
- **Spawn-point dependency.** No `SpawnPoint` in the scene ⇒ `TryGetSpawnAnchor` fails and NGM refuses to spawn anyone; the arrival never runs and the console shows one error. The impact site *is* the resolved spawn position, and it must never be re-resolved (a second resolve returns a different scattered point from the one the terrain was streamed around).
- **`ShipGrounding` returning false means "wait"**, not "give up" — in a streamed world it means the chunk has not loaded. Treating it as fatal buries or strands hulls.
- **A check must not share its input with what it checks.** Every arrival height came from `TryResolveGround`, `SetDown` included, so the only check agreed with itself and logged a clean landing (`-0.50 m off the ground`) for a hull hanging in the sky: plan against the heightmap, **verify against collision** (`TryMeasureLandingAgainstCollision`, which logs an error naming any discrepancy past `landingTolerance` — if it appears, the arc, the versus landing pose and the spawn points all believe the other answer). Second form of the same bug: the gear wall's headroom probe excluded the wall by `hit.transform`, which under this hull's one Rigidbody is the ship's ROOT, so it never matched and measured the wall against its own collider. `hit.collider.transform`.
- **The world's surface is its STATIC collision — a Rigidbody of any kind is a body standing on it.** `ShipGrounding.IsWorldSurface` excludes every collider with an `attachedRigidbody`, and must never be narrowed back to the non-kinematic ones the way `WalkerGround.IsLooseBody` does: a walker is *meant* to stand on a kinematic body (a mount's deck), a hull deciding where the ground is is not — and **almost nothing here that stands on the ground is dynamic**, since agents, mounts and a rider held by `CarriedBody` are all kinematic. Measured: an arrival landed beside a **nomad**, the probe took the NPC's collider 2.81 m above the terrain as the ground, and `SetDown` lifted the hull onto its head — where the wreck is then persisted. The tell is `WarnIfHeightmapDisagrees` firing with collision reading *higher* than the heightmap by about the height of a creature. Covered by `IgnoresAKinematicBodyStandingUnderTheHull`.
- **A hull left off its arc does not stay put — it sinks for minutes.** `linearDamping: 1` plus
  `HoverRigidbodyMotor.restWhenParked` means an unflown hull parks itself, gravity comes on, and
  drag pins it to ~10 m/s: from `StartAltitude`, three to four minutes of visible drift, which is
  what "it floats down" means when someone reports it. `GroundWhatWasBuilt` is the invariant, and
  **every** exit that gives up must call it — one enforced at some exits is not an invariant.
- **`ShipHull` skips colliders that are not in the physics scene.** `Collider.bounds` is maintained
  by physics; one physics has never seen reports a zero-SIZE box at the **world origin**, which
  `Encapsulate` drags the hull's bounds down to y=0. `PlayerShip` carries eleven (the salvage
  `Part_*` colliders are authored disabled), so an unguarded `BellyDrop` on a hull at 106 m returns
  106. Tested on the box, not the `enabled` flag: a prefab asset is in no physics scene either, and
  an `activeInHierarchy` test would discard the prefab measurement the arc is planned from.
- **The fallback ground ray reaches below the probe, not a fixed length from it.** It was
  `probeHeight` (600) less a constant 500 — only ground above y=100, in a world whose surface sits
  at ~100-120 m. Reach is now anchored under the origin.
- **The settle is the only thing that levels the ship.** Shortening `settleDuration` to zero, or
  returning early from `Settle`, leaves the wreck standing on its nose at `MaxPitchDegrees` —
  permanently, because that is the pose the save keeps and the pose the landing was measured
  against. Retune the *impact attitude* with `MaxPitchDegrees` instead; the settle then plays out
  whatever angle it is given.
- **`ArrivalCutscene` is told the settle window, not just the descent.** `Configure(descentDuration, settleHold + settleDuration)` is what keeps the screen black for the whole crash. The black is complete **at** first contact, not after the topple — the fade starts `impactFade` early and finishes on the impact frame, and the settle plays out unseen. The director is the authority for both numbers, so retiming needs nothing in the cutscene; shortening the *settle* to save black time leaves the wreck on its nose (above).
- **The launch is announced, not inferred.** `FlyFormation` calls `SeatedRider.AnnounceLaunch` per hull the frame the gate opens: `NetMsg.ArrivalLaunched` plus a replicated `launchedAt` on the server clock, and every machine times its presentation from that one instant. Timed from `LocalPlayerSeated` instead — which is what it used to do — the host started up to `crewGatherTimeout` (12 s) ahead of a client still streaming chunks, and every beat after that landed at a different time on every screen. Seating still starts the cutscene; it holds on black until the announcement.
- **Never leave the hull's own drivers live during a descent.** `QuietHull` is the fix for the "screen coming apart" bug: `HoverRigidbodyMotor`, `AgentController` and `UnderTerrainGuard` all write the same Rigidbody the descent is teleporting.
- **A solid collider on this hull answers with whatever `IInteractable` is above it**, which is why `mountableByDirectInteraction` is **off** on the root: with it on, all 140-odd wall/floor/hull slabs, the boarding stair, the salvage sockets and the three passenger chairs' own meshes resolved up to the helm, and pressing E anywhere on the ship — inside or out — seated the presser in the pilot's chair. `Verify()` and `PlayerShip_NoHullColliderBoardsTheHelm` both fail if it comes back on. It is legitimately on only for an export with **no** `Cockpit_Seat_Command*` chairs: no chair means no station, and a hull boarded from anywhere beats one nobody can board. **Do not "fix" this by moving the `MountModule` onto the chair** — on this ship that module *is* the vehicle. `SteerModule`, `VehicleDeploymentController`, `SeatedRider`'s `GetComponent<MountModule>`, `ArticulatedPartInteraction`'s `GetComponentInParent` door lock, `SaveablePolicy`'s `MountSaveable`, the module's own `GetComponent<IMovementMotor>` / `GetComponent<Rigidbody>` / `RiderCollisionIgnore` over its colliders and the chase camera's yaw off its `transform.rotation` all stop finding it, silently, and the ship still appears to mount.
- **Seat markers are not chair transforms.** FBX chairs arrive ~150× scaled with baked exporter yaw reading 180 whichever way they point; poses must be measured from geometry. A marker on the deck buries the feet a metre through it, and `seatOffset` on `SeatedRider` is meant to stay zero — the markers are already the answer.
- **The entry burn must be OUT before the ground rush, and its shell must be invisible to physics.** `EntryBurnCurve.Default` extinguishes at 0.70 of the descent, about eight seconds clear of the fade to black — asserted (`EntryBurnTests.IsOutBeforeTheGroundRush`) because the last third belongs to the ground coming up and to `shakeOverDescent` peaking on it, and a window still blown out orange hides the beat the whole descent is building toward. Separately, the shell is a `CreatePrimitive` sphere and its `SphereCollider` is destroyed in the builder: left on, a 35 x 16 x 69 m collider answers every interaction ray and every spawn probe, and since `ShipHull` measures the hull **from its colliders**, `TouchdownLift` and `SetDown` would plan against the fire and park the wreck tens of metres up (`ThePlasmaShellIsInvisibleToPhysics`).
- **The shell and its lamp are saved DISABLED, and the plasma material is rewritten every build.** They are switched rather than dimmed to zero — a URP light at zero intensity is still a light the renderer sorts (`CabinAlert`'s lesson) — and a shell left enabled puts every parked ship and every loaded wreck inside a ball of orange; `Verify()` and `TheEntryBurnIsDarkUntilTheDescent` both fail on it. The material is restated unconditionally by `EntryPlasmaMaterial()` because a `.mat` freezes the shader defaults it was born with, so **retune in the builder, never in the Inspector**; and it is a material asset rather than `Shader.Find` because an unreferenced shader is stripped from a player build and the burn then works in the editor and nowhere else (see [Environment](Environment.md)). One more silent one: without `_EdgeFade` the shell draws its own silhouette — a hard elliptical seam in the air around the ship — but widen it past ~0.3 and the limb, where the external drama lives, goes with it.
- Registration: `PlayerShip.prefab` must be in the network prefab list (`NetworkPrefabRegistrar.Sync` runs at build) and its `GlobalObjectIdHash` must be non-zero — a script-built `NetworkObject` ships 0, which is why the builder re-imports and force-reserialises after saving.

## Extending

**Change the ship geometry**
1. Edit [ship_lander_blockout.blend](Assets/Game/Art/Models/_Source~/models/vehicles/ship_lander_blockout.blend) by hand in Blender. Keep the names in `RequiredParts` and `PartNames` intact.
2. `blender --background --python player_ship_export.py` — writes both FBXs.
3. Run **Tools ▸ Vehicles ▸ Build PlayerShip Prefab**. Read the console: `VerifyParts`, `VerifyCollisionCoverage` and `Verify()` fail loudly rather than shipping a hull with holes.
4. Run [PlayerShipTests.cs](Assets/Game/Editor/Tests/PlayerShipTests.cs) (`MainDeckAisleIsWalkable`, `CollisionIsAllConvex`, `EachChairOffersItsOwnSeat`, `NoHullColliderBoardsTheHelm`, `AftDoorwaySealsShutAndClearsOpen`, `InventoryWallStopsShortOfTheOverhead` — the aft room's own dimensions are what the gear wall is cut to fit, see [Backpack](Backpack.md)).
5. Verify on an actual client and by reloading a save.

**Add a new ship station (e.g. a fifth seat, a console)**
1. Model it in the .blend under a prefix the export already stamps (`Cockpit_*` → fitting collider; `Part_*` → salvage socket).
2. Add the measurement + component wiring to `PlayerShipBuilder` — a new `Build…` method called from `Build()`, never a hand edit to the prefab.
3. If it is a seat: emit a `ShipSeat` marker under `ArrivalSeats` and a passenger `MountModule` behind a trigger volume (`BuildPassengerSeat` + `AddSeatVolume`); measure the pose with `MeasureSeat`, not the chair transform. **Never** make it directly interactable off a solid collider — the hull is not, and one seat that is puts every collider above it back in the mount business.
4. Extend `Verify()` with the count so a future rebuild that drops it fails instead of shipping quietly.
5. If it holds runtime state, add a saver in `BuildRootComponents` and confirm the key appears in the save JSON.
