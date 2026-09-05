---
system: Vehicles
layer: vehicles
summary: Mounting (seat + camera takeover) and stations (walkable deck, claimed controls) for every machine.
paths:
  - Assets/Game/Scripts/Vehicles/
  - Assets/Game/Scripts/agents/Modules/Riding/
  - Assets/Game/Scripts/agents/AI/Motors/
  - Assets/Game/Prefabs/agents/Vehicles/
symptoms:
  - "I right-click the vehicle and nothing happens, or every hull collider mounts me"
  - "the rider floats above the saddle on every machine but the host's"
  - "one press seated the player in all four ship chairs at once"
  - "right-clicking anywhere on the hull puts me in the pilot's chair"
  - "I board the ship from outside by looking at its cockpit through the glass"
  - "the vehicle drives fine for the host but a client steers a body that snaps back"
  - "the third-person camera on the mount jitters or doubles the vehicle's motion"
  - "the player standing on the deck is read as ground and the walker climbs into the sky"
  - "after loading a save the rider is standing next to the mount instead of in the seat"
  - "the respawn button is unclickable after dismounting a dead rider"
  - "a player who has been carried walks and steers but never falls again"
reads_with: [Ornithopter, PlayerShip, AgentSystem, Persistence]
updated: 2026-09-03
---

# Vehicles & Mounts

Two ways to operate a machine: **mounting** (you take the vehicle over — seat, camera, controls) and **stations** (you keep your body and camera and claim one control on a walkable deck).

**Scope:** `Assets/Game/Scripts/Vehicles/` (Appearance, DesertCrawler, Drivers, DuneFoil, Ornithopter, Parts, Rover, Stations, Systems, Tools) + `Assets/Game/Scripts/agents/Modules/Riding/` + `Assets/Game/Scripts/agents/AI/Motors/`.
**Related:** [Ornithopter.md](Ornithopter.md) · [PlayerShip.md](PlayerShip.md) · [MountSystem.md](MountSystem.md) · [AgentSystem.md](AgentSystem.md) · [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md)

## Model

- **Mount stack = 3 pieces**: [`MountModule`](Assets/Game/Scripts/agents/Modules/Riding/MountModule.cs) (lifecycle + camera) + [`SteerModule`](Assets/Game/Scripts/agents/Modules/Riding/SteerModule.cs) (input) + a motor implementing [`IRiderControllable`](Assets/Game/Scripts/agents/AI/Motors/IRiderControllable.cs). Both modules are `IBehaviourModule`s ticked by [`AgentController`](Assets/Game/Scripts/agents/Controller/AgentController.cs).
- **Station stack** ([`VehicleStation`](Assets/Game/Scripts/Vehicles/Stations/VehicleStation.cs)) is the opposite trade: no camera takeover, no seat, one claimable control per crew member. Used by the DuneFoil, whose "controls are the deck".
- **The rider is parented into the seat**, made kinematic (gravity + interpolation off), and its colliders are pairwise `IgnoreCollision`'d against the mount — never disabled, so it stays shootable/ropeable.
- **One `MountModule` = one seat.** A hull may carry several (PlayerShip has 4); every network message is addressed by `MountNetworkSync.MountIndex` (positional).
- **Mount ownership follows the rider** so their local input moves the vehicle and replicates outward. Station vehicles stay authority-simulated unless a station sets `TakesVehicleOwnership`.
- **NPC riders are a different model**: [`NpcPassenger`](Assets/Game/Scripts/agents/Modules/Riding/NpcPassenger.cs) — the *mount* is the agent, the rider is switched-off cargo. It shares no code with `MountModule`, only [`ISeatOccupant`](Assets/Game/Scripts/agents/Modules/Riding/ISeatOccupant.cs) (evict) and `RiderCollisionIgnore`/`MountedRiderPose`.
- **Only the local rider gets a view.** `MountModule.RiderIsLocal` (`Network.Owns(rider)`) gates cameras, audio listener, look input, visor flag and the rider's own control scripts. Everything else runs on every peer.

## Key types

| Type | File | Role |
|---|---|---|
| `MountModule` | [MountModule.cs](Assets/Game/Scripts/agents/Modules/Riding/MountModule.cs) · [.Mounting](Assets/Game/Scripts/agents/Modules/Riding/MountModule.Mounting.cs) · [.Camera](Assets/Game/Scripts/agents/Modules/Riding/MountModule.Camera.cs) | Seat lifecycle, `IInteractable`, AI+root-motion suppression, mounted FP/TP camera and look, `IPersistentEntity`. `[DefaultExecutionOrder(1000)]`. |
| `SteerModule` | [SteerModule.cs](Assets/Game/Scripts/agents/Modules/Riding/SteerModule.cs) · [.Input](Assets/Game/Scripts/agents/Modules/Riding/SteerModule.Input.cs) · [.Camera](Assets/Game/Scripts/agents/Modules/Riding/SteerModule.Camera.cs) | Reads Move/Jump/Sprint/Vertical/Turn, smooths, calls `motor.ApplyRiderInput`, claims the frame with `MoveIntent.Idle()`. Tap = jump, hold ≥ `leapHoldTime` = leap. Cosmetic lean. |
| `MountNetworkSync` | [MountNetworkSync.cs](Assets/Game/Scripts/agents/Modules/Riding/MountNetworkSync.cs) | Server-decided seating, ownership transfer, `NetworkVariable<ulong> seatedRider` for late joiners, dismount position on the wire. |
| `RiderInput` / `IRiderControllable` | [IRiderControllable.cs](Assets/Game/Scripts/agents/AI/Motors/IRiderControllable.cs) | `Move` (x=yaw, y=throttle), `Vertical`, `Turn` (separate yaw axis for strafing rigs), `IsRunning`. |
| `IMountJumpMotor` / `IMountLeapMotor` | [IMountJumpMotor.cs](Assets/Game/Scripts/agents/AI/Motors/IMountJumpMotor.cs) | Optional motor extensions. A motor that omits them just ignores the button. |
| Motors | [Motors/](Assets/Game/Scripts/agents/AI/Motors/) | `RigidbodyMotor`, `NavMeshAgentMotor`, `FlyingRigidbodyMotor`, `HoverRigidbodyMotor`, `OrnithopterFlightMotor`, `LeggedDriver` (+ `OstrichDriver`, `DesertCrawlerDriver`, `HorseDriver`, `CrabDriver`, `HumanoidDriver`). |
| `MountedRiderPose` / `ChairPose` | [MountedRiderPose.cs](Assets/Game/Scripts/agents/Modules/Riding/MountedRiderPose.cs) · [ChairPose.cs](Assets/Game/Scripts/agents/Modules/Riding/ChairPose.cs) | Saddle pose is *built* (no riding clip exists) at exec order 900, with speed/bounce/turn response ([`RiderPoseMath`](Assets/Game/Scripts/agents/Modules/Riding/RiderPoseMath.cs)); a chair just sets the animator's `Seated` bool. |
| `RiderCollisionIgnore` | [RiderCollisionIgnore.cs](Assets/Game/Scripts/agents/Modules/Riding/RiderCollisionIgnore.cs) | Apply/Restore/Forget of rider↔mount collider pairs. Shared by `MountModule` and `NpcPassenger`. |
| `RiderTeardownBeacon` | [RiderTeardownBeacon.cs](Assets/Game/Scripts/agents/Modules/Riding/RiderTeardownBeacon.cs) | The only way to know a rider is mid-destruction (`rider == null` is still false in `OnDestroy`). |
| `MountLookMath` | [MountLookMath.cs](Assets/Game/Scripts/agents/Modules/Riding/MountLookMath.cs) | `WrapAngle` / `ClampYaw` / `StepRecentre` — pure, tested. |
| `MountStation` | [MountStation.cs](Assets/Game/Scripts/Vehicles/Stations/MountStation.cs) | Cockpit control that calls `RequestMount` directly, so `mountableByDirectInteraction = false` can close the hull. |
| `VehicleStation` | [VehicleStation.cs](Assets/Game/Scripts/Vehicles/Stations/VehicleStation.cs) | Claim protocol (`NetMsg.StationClaim` 65 / `StationState` 66, addressed to the *vehicle*): server owns the claim table and the control's absolute value; the occupant drives locally and ignores its own echo. Both directions are change-gated: a request or announcement goes out at `PublishInterval` (0.1 s) only when it moved past `ValueDeadband`, and otherwise every `KeepAliveInterval` (1 s). |
| `DeckBoarding` | [DeckBoarding.cs](Assets/Game/Scripts/Vehicles/Stations/DeckBoarding.cs) | Look at the hull, right-click, get placed on the deck. Deliberately *not* a mount. |
| `WalkerPlatformCarrier` | [WalkerPlatformCarrier.cs](Assets/Game/Scripts/Vehicles/Systems/WalkerPlatformCarrier.cs) | Transform-driven hulls impart no friction; this re-applies the hull's per-frame delta (incl. rotation about the pivot) to bodies in the carry volume. Exec order 200, `ITeleportAware`. |
| Moving parts | [ArticulatedPart.cs](Assets/Game/Scripts/Vehicles/Parts/ArticulatedPart.cs) · [ArticulatedPartInteraction.cs](Assets/Game/Scripts/Vehicles/Parts/ArticulatedPartInteraction.cs) · [ShellVariantSwitcher.cs](Assets/Game/Scripts/Vehicles/Parts/ShellVariantSwitcher.cs) · [VehicleDeploymentController.cs](Assets/Game/Scripts/Vehicles/Systems/VehicleDeploymentController.cs) | Rotate/slide about own origin (put it on the hinge pivot); a switch that toggles several; hull mesh swap while any panel is off closed; deploy/stow off `Mounted`/`Dismounted`. |
| `MountSaveable` | [MountSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/MountSaveable.cs) | Stores *only* the rider `SaveRef`; deferred, `LoadOrder = Early`. |

## Vehicle catalogue

| Vehicle | Files | Notes |
|---|---|---|
| Ostrich (rideable creature) | [Ostrich.prefab](Assets/Game/Prefabs/agents/creatures/Ostrich.prefab), [OstrichDriver.cs](Assets/Game/Scripts/Creatures/Drivers/OstrichDriver.cs) | `MountModule` + `SteerModule` + `MountedRiderPose`; direct-interaction mount; legged motor. |
| RigWalker | [RigWalker.prefab](Assets/Game/Prefabs/agents/Vehicles/Ground/RigWalker.prefab), [DesertCrawlerLocomotion.cs](Assets/Game/Scripts/Vehicles/DesertCrawler/DesertCrawlerLocomotion.cs), [DesertCrawlerDriver.cs](Assets/Game/Scripts/Vehicles/Drivers/DesertCrawlerDriver.cs) | Piloted six-legged walker: `MountStation` + `MountModule` + `SteerModule` + `WalkerPlatformCarrier`. |
| DesertCrawler | [DesertCrawler.prefab](Assets/Game/Prefabs/agents/Vehicles/Ground/DesertCrawler.prefab), [Tools/](Assets/Game/Scripts/Vehicles/Tools/) | Same legs, **no mount** — AI-driven walking station with dig/claw/collector rig (`CrawlerToolModule` is a side-effect module, `ClaimsMovement = false`). |
| DuneFoil (sand sailer) | [DuneFoil.prefab](Assets/Game/Prefabs/agents/Vehicles/Ground/DuneFoil.prefab), [DuneFoil/](Assets/Game/Scripts/Vehicles/DuneFoil/), [Stations/DuneFoil*.cs](Assets/Game/Scripts/Vehicles/Stations/) | **No mount at all.** `DeckBoarding` + `DuneFoilHelm` + 4 × `DuneFoilRiggingStation` + `DuneFoilMooring` (holds station while the deck is empty) + `BoardingRamp` + HUD. Transform-driven, so `IPersistentEntity` + `ITeleportAware` + carrier. |
| DuneOrnithopter | [DuneOrnithopter.prefab](Assets/Game/Prefabs/agents/Vehicles/Aircraft/DuneOrnithopter.prefab), [Ornithopter/](Assets/Game/Scripts/Vehicles/Ornithopter/) | See [Ornithopter.md](Ornithopter.md). Mounted; spawned by [`WingPackItem`](Assets/Game/Scripts/Items/Equipped/WingPackItem.cs); wants `followMountPitch = true`. |
| ShipRV | [ShipRV.prefab](Assets/Game/Prefabs/agents/Vehicles/Spacecraft/ShipRV.prefab), [ShipRVBuilder.cs](Assets/Game/Editor/Vehicles/ShipRVBuilder.cs) | `HoverRigidbodyMotor`, `MountStation`, 8 `ArticulatedPart`s, `ShellVariantSwitcher`, `VehicleDeploymentController`. Rebuilt wholesale by the builder. |
| PlayerShip (lander) | [PlayerShip.prefab](Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab), [PlayerShipBuilder.cs](Assets/Game/Editor/Vehicles/PlayerShipBuilder.cs) | See [PlayerShip.md](PlayerShip.md). **4 `MountModule`s** (helm on the root + 3 `ChairPose` chairs), one `SteerModule`, `ShipPartRack`/`ShipPartSocket`, `ShipTeamAccent`, `CabinAlert`. Root `mountableByDirectInteraction = false`; the helm is boarded from a `MountStation` on a trigger volume at the pilot's chair, the passengers from trigger volumes of their own. |
| Rover | [Rover.prefab](Assets/Game/Prefabs/Vehicles/Rover.prefab), [Rover/](Assets/Game/Scripts/Vehicles/Rover/) | Not rideable — autonomous explorer with bogie IK. Test scene only; decisions gated on `Network.Simulates`. |
| DuneRider | [DuneRiderController.cs](Assets/Game/Scripts/agents/Controller/DuneRiderController.cs) | Self-contained rigidbody mount driver (its own input, no `SteerModule`). **On no prefab today.** |

## Flows

**Mount** (`MountStation.Interact` or `MountModule.Interact`)
1. `MountNetworkSync.RequestMount` → `NetMsg.Mount` to server (`NetArg.A = MountIndex`, Target = rider).
2. Server `SeatOnServer`: `CanMount` → `TryMount` → `mountObject.ChangeOwnership(riderOwner)` → `NetToOthers(NetMsg.Mounted)`.
3. `TryMount` (runs on **every** peer): `VacateSeatForPlayer` (`ISeatOccupant`) → cache rider refs → `RiderTeardownBeacon.Arm` → subscribe rider death → disable rider movement/look/interactor (*recording* prior state) → rider Rigidbody kinematic, no gravity, no interpolation → parent → suppress modules + motor `ForceStop` + freeze own rotation + root motion off → `RiderCollisionIgnore.Apply` → init view → `ApplyPerspective`.
4. Local rider only: FP camera off / TP camera spawned **unparented** from `thirdPersonCameraPrefab` → `Resources/Cameras/Mount Third Person Camera` → clone of `Camera.main` → bare `Camera`; visor render feature toggled with perspective.

**Drive** (per frame, local rider only)
1. `SteerModule.Update` → read + `SmoothDamp` Move/Vertical/Turn; `hasSteeringOverride` = max axis ≥ `steeringOverrideThreshold`.
2. `SteerModule.Tick` → `motor.ApplyRiderInput(input, dt)`, returns `MoveIntent.Idle()` to claim the frame; returns `null` when the rider lets off, so AI modules run iff `allowAISelfMovementWhenMounted`.
3. Motor stamps `Time.frameCount` inside `ApplyRiderInput` and skips the `MoveIntent` branch that frame.
4. `MountModule.LateUpdate` (order 1000) writes the TP camera's world pose: yaw orbit (or full attitude when `followMountPitch`), `orbitPitch` boom, exponential-decay follow with a *separate, slower* filter on the aim point; `Vector3.up` always, never roll.

**Dismount** (Esc, `SteerModule`-less chairs included — the key is read by `MountModule`)
1. `RequestDismount` → `NetMsg.Dismount` → server checks `IsDismountAllowed(sender, server, riderOwner)` → `ApplyDismount` → ownership back to server.
2. `DismountInternal`: re-entrancy guard → beacon check → `UnparentRider` (`TryRemoveParent`, else `SetParent(null)`) → position = override ?? `dismountPoint` ?? `transform.right * fallbackDismountDistance`, yaw only → `ApplyDismountPose` writes **transform *and* Rigidbody** → restore rigidbody/components/constraints/collisions/root-motion/modules/view → `Dismounted` → destroy TP camera → clear refs.
3. `AnnounceDismount` (server, off `MountModule.Dismounted`) broadcasts `NetMsg.Dismounted` with the actual position in `NetArg.P` (`B = 1`), covering the dismounts nobody requested (death, landing, unequip, teardown).

**Teardown** — `AbandonRider` instead of `Dismount` when reparenting is illegal: mount deactivating (`OnDisable` with `!gameObject.activeInHierarchy`) or rider mid-destruction (beacon). It destroys the TP camera, `Forget()`s collision pairs and clears refs; the rider stays parented.

## Multiplayer

- **Mounting is server-decided, presentation is local.** Every peer replays `TryMount`/`Dismount` so the rider is visibly seated; `RiderIsLocal` gates cameras, audio listener, input actions, visor flag and the rider's component restore.
- **Two channels**: the event (`NetMsg.Mount`/`Mounted`/`Dismount`/`Dismounted`) for everyone present, and the state (`seatedRider` `NetworkVariable`, server-write, polled each frame) for late joiners. `ReconcileSeat` **only seats** — emptying is the event's job, or a peer would throw a rider off in the window before the variable arrives.
- **Every message carries `NetArg.A = MountIndex`** (positional over the entity's `MountNetworkSync`es). Unaddressed, one press mounted a player in all four PlayerShip chairs. Same trick: `VehicleStation.StationIndex`, `ArticulatedPartInteraction`.
- **Ownership**: mount `NetworkObject` → rider's client on seating, → server on dismount. Without it the rider steers a body they don't own and the server's `NetworkTransform` overwrites it every tick.
- `VehicleStation` and `ArticulatedPartInteraction` are plain `MonoBehaviour`s on purpose (a `NetworkBehaviour` with no `NetworkObject` above it is a Netcode error); with no relay every send falls through to a local dispatch.
- `NpcPassenger` seats only on the authority; netcode replicates the spawn *and* the parenting, and `NetAuthority` switches the watching copies' brains off. Nothing to send.

## Persistence

- [`SaveablePolicy`](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs) auto-adds `MountSaveable` to anything with a `MountModule` — `MountModule` is `IPersistentEntity` precisely because a mount can have no Rigidbody, no NavMeshAgent and no health and would otherwise be invisible to the save system.
- **Only the rider `SaveRef` is stored.** Seat, offset and perspective come from the prefab; the rider's pose comes from the seat (parenting), so saving a world position would be a second, competing answer.
- Restore is **deferred** (`IDeferredSaveable`, `LoadOrder = Early`) and re-runs as each player binds — players arrive one at a time, so an unresolved ref is kept, not dropped. It refuses a corpse, and goes through `MountNetworkSync.ServerMount` so the restore replicates and ownership moves.
- `DuneFoilLocomotion` is its own `IPersistentEntity` (no Rigidbody, no `AgentController` — nothing else would have found it). The ornithopter has `OrnithopterSaveable`, which reads the seat `MountSaveable` restored first.

## Gotchas

- **Nothing may remember a carried body's physics flags privately.** [`CarriedBody`](Assets/Game/Scripts/agents/Modules/Riding/CarriedBody.cs) is the one record of `isKinematic` / `useGravity` / `interpolation`: captured on the FIRST claim, handed back on the LAST release. Every system that touches those three on a body somebody else might take goes through it — `MountModule`, `SeatedRider`, and `UnderTerrainGuard`'s park (`SuspendGravity`, a weight-only claim that leaves the body dynamic). A private capture banks whatever transient state the body happened to be in and makes it permanent; both times this has shipped, the symptom was a player who could walk but had no gravity. `Hold` and `SuspendGravity` are two ways to claim, `Release` is the only way to give back, and a holder asking whether a body is carried must use `IsHeldByOther` so it does not find itself.
- **Never `SetParent` a spawned `NetworkObject` to a bare marker.** `ParentRiderToMount` parents to the mount's `NetworkObject` and folds the seat marker's offset into local space by hand; `NpcPassenger.SeatPoseIn` does the same fold separately. Getting the fold wrong floats the rider above the saddle on every machine but one.
- **`OnDisable` must not dismount when the hierarchy is going down.** `gameObject.activeInHierarchy == false` is the exact condition Unity guards on (covers both `SetActive(false)` and scene unload; `NetworkManager.Shutdown` skips deparenting, so the rider is still parented and no longer spawned).
- **A destroyed rider cannot be detected by a null check.** Inside `OnDestroy`, `rider == null` is still false. Use `RiderTeardownBeacon.CanReparent`.
- **`Dismount` is re-entrant.** `Dismounted` fires *before* refs are cleared and listeners routinely dismount in response (`WingPackItem`). The `dismounting` flag is the guard; six+ call sites reach it.
- **Restore only what you took.** `RestoreRiderComponentsAfterDismount` re-enables `PlayerMovement`/`PlayerLook`/`Interactor` only if they were on at mount time, and never for a dead rider — `PlayerLook.LateUpdate` re-locks *this* machine's cursor every frame, which is how the respawn button became unclickable.
- **Rider is kinematic with interpolation off** while seated; `ApplyDismountPose` writes the Rigidbody as well as the transform because `Physics.autoSyncTransforms` is off project-wide, so a transform-only write leaves the mount's tilt on the player for a frame.
- **Never disable the rider's colliders** to stop it shoving the mount — that removes it from every raycast/overlap/interaction probe. Use `RiderCollisionIgnore` (pairwise), and skip pairs where the mount collider is a child of the rider.
- **Ground probes must reject non-kinematic bodies.** [`WalkerGround.IsLooseBody`](Assets/Game/Scripts/Locomotion/Ground/WalkerGround.cs) and `FoilLift`'s probe both do: otherwise a player standing mid-deck is read as ground and the machine climbs into the sky. A mounted rider is kinematic *and* parented, so it is already excluded.
- **The TP camera is spawned unparented** — parenting applies the vehicle's motion twice (measured 48% vs 2.6% frame-to-frame variance). Its lifetime is explicit; `AbandonRider` destroys it or you leak a camera + a second `AudioListener` for the session.
- `ReleaseRuntimeThirdPersonCamera` uses `DestroyImmediate` outside play mode — plain `Destroy` is an editor error and fails EditMode tests that mount anything.
- **Transform-driven hulls need `WalkerPlatformCarrier`**, and it must poll the carry volume rather than use `OnTrigger*` (messages only reach the collider's own GameObject and its `attachedRigidbody`).
- **On a big hull, `mountableByDirectInteraction` must be off.** `Interactor` resolves a solid collider by walking UP the hierarchy, so a root `MountModule` left directly interactable turns every wall, floor and hull slab into a mount point — right-clicking anywhere on a PlayerShip put the presser in the pilot's chair. Boarding then comes from a `MountStation` on a **trigger** collider (the one thing `Interactor` will not resolve upward), which calls `TryMount`/`RequestMount` directly and so keeps working with the flag off. Do **not** answer this by moving the `MountModule` onto the seat: on a vehicle the module is the vehicle, and `GetComponent<IMovementMotor>`, `GetComponent<Rigidbody>`, `RiderCollisionIgnore` over its own colliders, the chase camera's yaw, `VehicleDeploymentController`, `ArticulatedPartInteraction`'s `GetComponentInParent` mount lock and `SaveablePolicy`'s `MountSaveable` all silently stop finding it.
- **A boarding volume behind a hole in the collision is boarded from outside.** The complement of the rule above, and the half it does not cover: `Interactor` stops at *solid* colliders and passes through everything else, so a boarding trigger is exposed wherever the hull is drawn without collision. The PlayerShip's canopy dome deliberately carries none (a convex hull of the glass fills the cockpit and brains a three-metre pilot), and its four chairs' volumes were therefore the first thing an outside ray met — 282 approaches in `PlayerShip_NoChairIsBoardedFromOutsideTheHull` boarded a chair through the glass, out to the player's whole 20 m reach. The fix is an [`InteractionBlocker`](Assets/Game/Scripts/Gameplay/Interaction/Core/InteractionBlocker.cs) over the canopy: a **trigger box** that stops the ray and nothing else. It must **enclose** what it protects rather than merely stand in front of it — a ray starting inside a collider is not reported as hitting it, which is what still lets the pilot standing under the canopy reach their chair (`PlayerShip_EveryChairIsBoardedFromWhereItPutsYouDown`), and a shape that hugged the glass instead left the cockpit open over the dome's aft rim. See [InteractionSystem](InteractionSystem.md).
- **Station/mount/part indices are positional** and do not survive reordering a prefab's children between builds.
- Mounted look reads `GameSettings.MouseSensitivity` and `InvertLookY`; `lookSensitivity` defaults to 20 to match `PlayerLook`.

## Extending — add a new rideable vehicle

1. Root GameObject: `Rigidbody` + `Collider` (or a legged locomotion component), `AgentController`, a motor implementing `IMovementMotor` **and** `IRiderControllable` (optionally `IMountJumpMotor`/`IMountLeapMotor`).
2. Add `MountModule` + `SteerModule`. `SteerModule.EnsureRuntimeMovementPath` will add `RigidbodyMotor` + `AgentController` if missing — add them explicitly so the fields are tunable.
3. Seat: assign `seatPoint` and push `seatOffset` **down** by roughly the rider's leg length (the player's origin is ~1 m below its own head, at the feet). Assign `dismountPoint`. Add `MountedRiderPose` (saddle) or `ChairPose` (seat with a `Seated` animator state).
4. Large hull? Set `mountableByDirectInteraction = false` and add a [`MountStation`](Assets/Game/Scripts/Vehicles/Stations/MountStation.cs) on the cockpit control, else every hull collider becomes a mount point.
5. Add `MountNetworkSync` on the same GameObject as the vehicle's `NetworkObject`, and register the prefab in the network prefab list if it is spawned at runtime.
6. Flies? Set `SteerModule.verticalActionName` and `MountModule.followMountPitch = true`. Strafes rather than turns with the stick? Set `turnActionName`.
7. Walkable deck on a transform-driven hull? Add `WalkerPlatformCarrier` and a carry-volume trigger collider.
8. Moving parts: `ArticulatedPart` on each hinge pivot, `ArticulatedPartInteraction` for player-operated ones, `VehicleDeploymentController` to drive them off the mount events, `ShellVariantSwitcher` if the hull ships a cut-out mesh.
9. Persistence is automatic (`SaveablePolicy` adds `MountSaveable`). Verify the rider reappears in the seat after quit/load, and verify mounting **on a client**, not just the host.
