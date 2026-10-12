---
system: SatelliteDish
layer: world
summary: "The broken satellite tower prefab: door, catwalk ladder, and a lectern that takes over the dish motors"
paths:
  - Assets/Game/Scripts/Gameplay/DishControl
  - Assets/Game/Scripts/Gameplay/Interaction/Core/ClaimableConsole.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/DishRigSaveable.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/DishConsoleSaveable.cs
  - Assets/Game/Editor/Tests/DishTowerQuestTests.cs
  - Assets/Game/Prefabs/Environment/Structures/SatelliteTower
  - Assets/Game/Art/Models/Environment/Structures/SatelliteTower
  - Assets/Game/Art/Textures/Environment/SatelliteTower
  - Assets/Game/Art/Materials/Environment/SatelliteTower
  - "Assets/Game/Art/Models/_Source~/models/buildings/satellite_tower_export.py"
  - "Assets/Game/Art/Models/_Source~/models/gear/dish_transmitter_export.py"
  - Assets/Game/Prefabs/Items/ShipParts/DishTransmitter.prefab
  - Assets/Game/Resources/Items/ShipParts/DishTransmitter.asset
symptoms:
  - "the dish control lectern says Dish drives unresponsive"
  - "the dish points straight up in a new world"
  - "the satellite dish turns for the operator but not for the other player"
  - "the dish snaps back to where it was after loading a save"
  - "the dish control lectern says In use and nobody is at it"
  - "steering the dish does nothing for a client although the camera cut to the dish"
  - "the dish swings its hanging beams into the shack roof"
  - "a grapple hooked to the dish stays in the air while the dish turns, on the other player's screen"
  - "the satellite tower's door swings into the room, or about the wrong axis"
  - "the grappling hook is back on the catwalk board after I took it and reloaded"
  - "the transmitter is back on the dish after I took it and reloaded"
  - "the catwalk board and the dish's transmitter cradle share or swap their gear after a reload"
  - "the transmitter cradle on the dish will not take anything I hold"
  - "the transmitter on the dish can be taken from the catwalk, or through the feed cabin"
  - "I am right next to the transmitter and the crosshair offers no take"
reads_with: [InteractionSystem, Terminal, ShipSignal, Objectives, Ladders, ArtPipeline, Multiplayer, Persistence]
updated: 2026-10-06
---

# Satellite dish

The broken satellite tower as a finished, scene-placeable prefab: a walkable control room behind a
hinged steel door, stairs and decks, a caged ladder to the pedestal catwalk, three shanties, a 54 m dish
with one panel missing (the hole is open; its neighbours hook a grapple), and a lectern in the control room
that **takes over the dish**. Right-click the lectern and the camera cuts to a feed framing the dish;
WASD / the left stick slews azimuth (left/right, full turn) and elevation (up/down, 15°-85°) at a
slow, spooling motor; RMB, Esc or the pad's B leaves. No generator places it: the one tower in the
world is a hand-placed prefab instance in a chunk scene (see Placement).

**Size (the 1.8x rework, 2026-10-06):** about 55 x 56 x 77 m; plinth ±20 m, with shanties, debris and the fallen
panel out to ~29 m. Levels: plinth deck 5.4 m, control-room floor 4.59 m (7.15 m headroom), roof deck 13.5 m,
pedestal catwalk 26.1 m; azimuth pivot 28.8 m, elevation pivot ~48.2 m. The ground stairs and the door face the
prefab's +Z (Blender -Y). Shanties: `PlinthLeanTo` on the ground, `WaterTank` (trestle, 13.5 m platform, plank
bridge), `DeckHut` on props off the roof deck, and the scrap shack with its annex and porch.

> **Unverified (2026-10-06):** never run in Play mode by an agent, on a client, or through save/reload. The 1.8x
> rebuild passed edit-time probes only (floors, ramps, dish drop tests, ladder volume, console ray); the user plays it.

**Scope:** [`Gameplay/DishControl/`](Assets/Game/Scripts/Gameplay/DishControl), the claim base
[`ClaimableConsole`](Assets/Game/Scripts/Gameplay/Interaction/Core/ClaimableConsole.cs) (shared with
[Terminal.md](Terminal.md)), the prefab and its art. **Related:** [InteractionSystem.md](InteractionSystem.md)
(the door, the press), [Ladders.md](Ladders.md), [ArtPipeline.md](ArtPipeline.md) (the bake).

## Model

- **Three components, the terminal's split.** [`DishRig`](Assets/Game/Scripts/Gameplay/DishControl/DishRig.cs) owns the angles (server-decided, replicated); [`DishConsole`](Assets/Game/Scripts/Gameplay/DishControl/DishConsole.cs) owns who is at the controls and forwards their command; [`DishControlSession`](Assets/Game/Scripts/Gameplay/DishControl/DishControlSession.cs) is the operator's machine only: camera, readout, raw input.
- **A new world's dish points nearly straight up** (`DishRig.startElevation` 85, the motor's top; azimuth at
  rest). The transmitter rides the elevation pivot: at 85 its device centre is (0, 73.37, 1.05) in prefab space,
  73 m above the ground, 47 m over the catwalk and 1 m off the tower axis, out of any grapple's reach; at 40
  (the model's rest) (0, 66.26, 18.94); at 15, (0, 57.17, 24.92). Turning the dish down is the puzzle.
- **The drives are dead until the hook leaves its board.** `DishConsole` reads `hookBoard` (`CatwalkGearBoard`)
  for `hookItem` (`Resources/Items/Artifacts/GrapplingHook`); on the first server frame it is gone the drives latch
  awake for good (putting it back does not relock). Locked, the crosshair says "Dish drives unresponsive", a press
  plays `InteractDenied` on the presser's machine, and the server drops any steer. The latch is a replicated
  `NetworkVariable<bool>`, saved by `DishConsoleSaveable` (key `dishConsole`, hand-placed on the lectern).
- **The motor is arithmetic.** [`DishSlew.Step`](Assets/Game/Scripts/Gameplay/DishControl/DishSlew.cs) ramps a velocity toward `command × MaxSpeed` at `Acceleration`, and on a limited axis caps it at `sqrt(2·a·d)` so the dish brakes ONTO its limit. Tunables are two [`SlewMotor`](Assets/Game/Scripts/Gameplay/DishControl/SlewMotor.cs)s on `DishRig` (azimuth 6°/s, 4°/s², wraps; elevation 4°/s, 3°/s², 15-85).
- **Angles are the source rig's.** Azimuth 0-360 about the tower's vertical; elevation in degrees above the horizon, rest 40 (the Blender pivot's 50° about its X is `90 - elevation`). Each pivot turns from its imported rest pose about a serialized LOCAL axis (`azimuthAxis`, `elevationAxis`), measured and signed when the prefab was built — the FBX empties carry the axis conversion, so no axis is assumed.
- **The tower is its own entity.** The prefab root carries a `NetworkObject` (`SynchronizeTransform` and `AutoObjectParentSync` off) + `NetRelay`: the channel for the door's `NetLatch`, the dish's variables and the console's claim. It is placed in a scene, never spawned; see Gotchas.
- **The door** is a stock [`DoorInteraction`](Assets/Game/Scripts/Gameplay/Interaction/Interactions/DoorInteraction.cs) on `SatTower_Int_Door` (an unrotated empty the export adds as the hinge's parent), single leaf: `_leftDoor` = `SatTower_Int_DoorHinge`, `swingDegrees` 100, `swingAxis` the hinge's measured local vertical, signed outward. A `BoxCollider` on `SatTower_Int_DoorLeaf` swings with it.
- **Two gear walls ride the tower's entity** (2026-10-06). Both are stock [`WallInventory`](Assets/Game/Scripts/Items/Wall/WallInventory.cs)s ([Backpack.md](Backpack.md)) with no `SaveableEntity` of their own, the pattern `PlayerShip` uses for its gear wall. No new code: the only change they needed is `WallInventorySaveable.recordName` (see Persistence).
  - **`CatwalkGearBoard`**: a nested `AstroDeco_SmallGearBoard` instance (the smallest wall: 6 x 10 cells, drawn 1.34 x 2.12 m), turned landscape and hung on the azimuth bearing drum beside the catwalk ladder's exit. Centre (-1.59, 27.72, 7.99) in prefab space, bearing -11.25 deg (between two drum ribs; the exit is on the -22.5 deg rib, 2.2 m away), 0.95-2.29 m above the 26.1 m deck, back 7 mm off the rib tips at r 8.10. It stocks ONE `GrapplingHook` through its own `startingMainItems`. Its `SaveableEntity`, `TransformSaveable` and `PersistentFixture` are removed as prefab overrides.
  - **`TransmitterCradle`**: under `SatTower_Rig_Elevation`, saddled on the TOP of the feed horn, the cylinder (radius 1.98 m, centre (0, 64.69, 20.68), along the dish axis) that sticks 1.6 m out of the feed cabin's dish-facing face; the cabin is the box at the dish's focus, held out on the four feed legs. Moved there 2026-10-06 at the user's request (first placed on the cube's +X side). At rest (azimuth 0, elevation 40) the device centre is (0, 66.12, 18.92) in prefab space, world (3151.2, 182.8, 908.3) in `Chunk_6_3`: 66.1 m above the ground, 40.0 m above the catwalk, on the horn's upper side, centred left-right, plug end toward the cube. Since the 1.5x resize (2026-10-06) the cradle fills the horn: its cable gland ends 0.03 m short of the cabin face and its plate's outer end overhangs the horn tip by ~0.04 m (the cable run behind the receptacle was shortened to 0.45x so the 2.07 m cradle became 1.77 m on a 1.75 m horn), so a further resize has nowhere to go along the horn. It turns and tilts with the dish. Model `dish_transmitter_cradle.fbx`: plate, clamp straps, the receptacle the plug seats in, a short cable run into a gland, and two saddle bands that wrap down the horn's curve (built for its 1.975 m radius). One reserved face, `SURF_CradleSocket` (`PackSurface.acceptsOnly` = `DishTransmitter`, 11 x 5 cells, at plate height 0.0375); `displayScale` 1.323 = 1.32 / (packSize 0.95 x `PackScale.Factor` 1.05), so the copy is drawn at the device's true 1.32 m. The item's own `packSize` cannot simply follow the model: `ShipPartsTests.TheHaulLadder_KeepsTrueSizeOrder` caps the shortest module at the next rung (the Belly Motor's 0.95), so the seated size is bought with `displayScale` and the hand size with `holdSize` 1.26; `startingMainItems` = the one `DishTransmitter`. A solid `BoxCollider` over the device volume is what the wall aim hits once the cradle is empty. **Close range only:** its `WallInventory.takeReach` ([`WallTakeReach`](Assets/Game/Scripts/Items/Wall/WallTakeReach.cs)) is `maxDistance` 4 m with `needsLineOfSight`, so it is taken only by someone who has grappled out to it, never from the catwalk or through the cabin. The board keeps the default (no rule).
- **The dish transmitter is the mission object, and there is one.** It is stocked only on this cradle and is in no loot table. It is a `ShipPartItem` of the eighth kind, `ShipPartKind.Transmitter` ([PlayerShip.md](PlayerShip.md)), so the existing install path fits it into the lander's `Part_Transmitter_A` socket (a wall cradle in the aft room, once its burnt-out unit is put out and taken off: [ShipTransmitterFire.md](ShipTransmitterFire.md)). Model `models/gear/dish_transmitter.blend` (`Coll_DishTransmitter`, 1.32 x 0.54 x 0.54 m since 2026-10-06, when the device, its burnt-out twin and both cradles were scaled 1.5x at the user's request — all but the dish cradle's saddle bands and edge shims, which stay fitted to the 1.975 m horn, a salvaged signal core: an olive power block with a torn-off side cover over exposed circuit boards, a folded antenna and stub dish on its roof, stencil and warning stripes and a rusted patch plate; in front of it an amber-glowing heater coil (`Mat_Emissive_Amber`, the one focal glow) seen through the slots of a vented steel heat chamber in a cage with a carry handle; radial cooling fins; a seven-pin connector in a locking collar. No glass or crystal, by the user's direction; `Coll_DishTransmitterCradle` beside it), exported by `dish_transmitter_export.py`.
- **Why there** (`GDC-L1-LEVEL-0004`, `GDC-L1-UX-0004`): the hook hangs where the climb ends, in sight of the feed it is needed for, so the space states the problem and the tool together. The cradle takes only its own device, so the put-back verb cannot be misread. Both are contextual; the user's play-test of the grapple route is the evidence that counts.

## Placement

The main world has one tower, placed 2026-10-05 the way every other authored structure is (the colony,
the outposts, the refinery): a root prefab instance in a chunk scene, saved with the scene.

| Field | Value |
| --- | --- |
| Scene | [`Chunk_6_3.unity`](Assets/Game/Scenes/world/Chunks/Chunk_6_3.unity) (the nomad settlement's chunk), root object `SatelliteTower`, beside the `Terrain_6_3` root and NOT under `Settlement` |
| Pose | (3160, 116.65, 925), yaw 207.7° (front stairs, local +Z, face the landing site) |
| Site | East rim of the plateau north of the nomad settlement, ~550 m from the ship's landing spot (the `SpawnPoint`, (2610, 880) since 2026-10-06) at bearing 85° — in clear line of sight from eye height there; the ground drops ~10 m to a basin 38 m east of the tower. Footprint 85 m north of the settlement's bounds, ~350 m from the Strider city's start (3368, 1199) |
| Ground | Terrain under the plinth edge 116.71-116.94: the plinth is sunk 0.06-0.29 m, nowhere above the ground |
| Identity | `SaveableEntity` `authored: 1`, `instanceId 3026bcb13a084bc28eeef81bd2b22fa8`, NGO `GlobalObjectIdHash 1322249500`, all prefab overrides in the scene file |

No hill within ~600 m of the landing site has a walkable approach: the rises there are hex-terraced
plates and mesas with 4-10 m cliff edges, and the player has no step offset. The only walkable raised
plate (~(3238, 1110), +4 m) sits inside the Strider city's start footprint, so the tower stands on the
plateau rim instead. To move it, edit the instance's transform in `Chunk_6_3` and save the scene. The
identity is the baked `instanceId`, not the pose, so it survives the move — but its `TransformSaveable`
record does too: a save written before the move puts the tower back where it was (see Gotchas).

## Key types

| Type | Role |
| --- | --- |
| `DishRig` | `NetworkBehaviour`, `IPersistentEntity`. `NetworkVariable<float>` azimuth/elevation; server steps `DishSlew` from `Drive(command)`; peers follow at `MaxSpeed × followCatchUp`; `RestoreAngles`, `IsAtRest`; motor hum via `LoopingEmitter` (`SfxId.DishMotorLoop`, a stand-in on `ElectricHum`). |
| `DishConsole` | `ClaimableConsole`, `IInteractable`, `IInteractionReadout` ("Dish control", "In use", "Dish drives unresponsive"). `DrivesUnlocked`, `BoardHoldsHook`, `TransmitterCradle` (read by `FitTransmitterStep`); static `Live`. `Steer(command)` → `SteerServerRpc`, accepted only from `OperatorId`; any operator change zeroes the drive. |
| `ClaimableConsole` | The one-operator claim lifted out of `TerminalConsole`: `NetworkVariable<ulong>` operator, `RequestClaim`, `Release`, disconnect release, `OnOperatorChanged`. |
| `DishControlSession` | `GameplayMenuScope.Enter(freezeTime: false)`, spawns `DishFocusCamera` at `FeedVantage`, shows `DishReadout`, sends the quantised command on change, `Exit` on RMB / Esc / B / death / disable. |
| `DishFocusCamera` / `DishReadout` | A `FocusCamera` that CUTS (fly-in 0) to a fixed vantage; the code-built screen overlay (angles, limits, the commanded direction lit at once). |
| `DishRigSaveable` | Key `dishRig`: `{ azimuth, elevation }`, nothing at rest. |
| `WallInventory` x 2 | `CatwalkGearBoard` (the hook) and `TransmitterCradle` (the transmitter); take and put-back are the gear wall's own crosshair verbs (`WallAimController`). |
| `ShipPartItem` (`DishTransmitter`) | The carried transmitter; `Use` aimed at the lander's empty transmitter socket fits it. |

## Flows

**Take control.** RMB on `DishConsole`'s box → `DishControlSession.Enter` (camera cut, readout) →
`RequestClaim`. **Steer:** each frame the session reads WASD/arrows/left stick (dead zone, rounded to
`commandSteps`), repaints the readout, and on a change calls `Steer` → server → `DishRig.Drive`. The server's
`Update` steps both axes and writes the variables on change; every peer's `Update` follows them.
**Leave:** zero command, `Release`, camera home, scope released.

## Multiplayer

| Path | Carrier | Authority |
| --- | --- | --- |
| Operator | `ClaimableConsole`'s `NetworkVariable<ulong>`; `ClaimServerRpc` / `ReleaseServerRpc` via `InteractorRelay` | Server; released on disconnect |
| Command | `SteerServerRpc(Vector2)`, sent on change; `RpcParams.Receive.SenderClientId` must equal the operator | Server |
| Angles | `NetworkVariable<float>` × 2, written on change; a joiner snaps to them in `OnNetworkSpawn` | Server; not predicted (the motor's spool-up outlasts a round trip; the readout acknowledges the press at once) |
| Door | `NetLatch` on the tower's channel | Server |
| Board and cradle contents | Each wall's `WallInventoryNetwork` (`NetworkList`), NetworkBehaviours on the tower's own `NetworkObject`; `NetMsg.WallTake`/`WallStow` carry `WallIndex` (cradle 0, board 1) | Server; a joiner adopts the list on spawn |
| Camera, readout, input | none | Local to the operator |

## Persistence

| State | Saver | Key |
| --- | --- | --- |
| Dish angles | `DishRigSaveable` → `DishRig.RestoreAngles` (instant, published); none = the start pose (85) | `dishRig` |
| Drives awake | `DishConsoleSaveable` → `DishConsole.RestoreUnlocked`; none = locked, re-derived from the board | `dishConsole` |
| Door open | `DoorSaveable` on `SatTower_Int_Door` | `door` |
| Catwalk board contents | `WallInventorySaveable` on `CatwalkGearBoard` | `wallInventory` |
| Cradle contents | `WallInventorySaveable` on `TransmitterCradle`, `recordName` `transmitterCradle` | `wallInventory.transmitterCradle` |

The first two are auto-attached by `SaveablePolicy` (Wire Saveable Prefabs); the wall savers are on the prefab by hand. The
operator claim is not saved. Both walls are authored WITH starting gear, so once emptied each writes an empty `placements` list
and the next load does not lay the hook or the transmitter on again; an item put back is saved where it was put. A carried
transmitter persists like any held item (the player's inventory, or a world-item record if it was dropped).

## Gotchas

- **The world NavMesh does not know about the placed tower until it is re-baked.** It is one author-time bake ([NavMeshSystem.md](NavMeshSystem.md)); after the 2026-10-05 placement it was NOT re-baked, so NPCs path through the plinth and `WorldNavMeshBuildCheck` fails the next player build. Close every chunk scene, run `World/Streaming/Bake World NavMesh`, commit `WorldNavMesh.asset`.
- **A saved world remembers where the tower stood.** The root's `TransformSaveable` writes its pose like any authored entity's, so moving the placed instance in `Chunk_6_3` changes new worlds only; an existing save restores the old pose onto it. Delete that record (`instanceId 3026bcb1…`) or start a new world after a move.
- **Scene-placed, so a NetworkObject inside the prefab is deliberate — and the scene must be saved.** NGO gives an in-scene instance its own hash only when the scene is saved; two towers in one unsaved scene collide.
- **A peer resolves only the NetworkObject a grapple hit, not the panel.** `GrapplingHookArtifact.BindAttach` therefore looks for the root's own collider at the hook point (`attachProbeRadius`) so the rope rides the turning dish on every machine; the owner, who runs the physics, always had the panel.
- **Below ~14° elevation the hanging beams strike the shack roof**; the 15° floor is a collision limit, not taste.
- **Stairs are ramps; `PlayerMovement` has no slope limit** (it writes horizontal velocity; the colony ramps are 19.9°). `Colliders/Ramp_*` sit 3 cm over each nosing line: ground 32.5°, deck dogleg 34°+34°, entry 30.4°, door step 25.8° to the threshold's edge 1.3 m out (a ramp to the sill left it 0.13 m proud). 20 `Ramp_Lip_n` wedge 0.12-0.3 m steps on floor flood-filled from the walking levels (unfiltered, the grid wedged furniture tops and the lean-to roof: 75).
- **A thin panel on a moving body is a floor a discrete-collision player falls through**, so each dish panel has a `Proxy` box on its own PCA plane, 0.35 m behind the face, 85% wide (dish-axis boxes are metres thick on the ~30° outer rings). The grapple hole (~22 m², by Panel_54/55) passes a 0.4 m sphere; Panel_54 hooks on its Proxy.
- **The floor hatches are 1.17 m pits** nobody without a step offset climbs out of, so each is capped at floor level by a `Colliders/HatchCap_n` box (5), found by a ray grid over `SatTower_Int_FloorHatches` when the prefab is built.
- **The lectern is lit by `LecternGlow`**, 13.6 m straight ahead of the door, in front of the plotter board.
- **The column ladder in the control room is NOT climbable**: it ends at a closed plate under the solid pedestal.
- **`Ladders/CatwalkLadder`** runs roof deck (13.5 m) to catwalk (26.1 m) at -67.5°, climbed from outside the cage; its `Exit` is inward at r 9.5 m, inside the 2.5 m clear ring between the azimuth bearing (r 8.1) and the rail.
- **The door frame must fill the wall's opening.** After the 1.8x rework the opening was ±1.30 m and the jambs ±1.04 m, a 0.26 m slot each side; the library jambs and lintel were widened to ±1.295 m (clear 1.72 m kept). The user's own `radar_peak.blend` still has the gap, so a re-sync from it brings it back.
- **Two walls on one entity need two save keys.** `SaveableEntity` keys savers by `SaveKey` alone. With both walls on `wallInventory`, the later saver wins the capture and BOTH restore from it: the board's hook record lands on the cradle, whose reserved face refuses it, and the other way round. The cradle's `recordName` gives it `wallInventory.transmitterCradle`; the board keeps the plain key. `SaveableWiring` strips a second saver on a key the entity already owns, so a missing `recordName` also loses the cradle's saver the next time anyone runs Wire Saveable Prefabs. Never rename a `recordName`.
- **The nested board must not keep `PersistentFixture`.** `EnsureScene` walks every transform under a scene root and gives an `IPersistentEntity` its own `SaveableEntity`, which would then collect the board's saver into a second record.
- **The board stays under the yoke.** Above y 28.8 the rotating `HeadYoke` sweeps out to r 8.94, and the drum under it is only 1.5 m tall (y 27.2-28.7). Portrait (2.12 m) the board would clear the yoke by 0.18 m; landscape clears it by 0.41 m. The dish itself never comes below y 29.8 within r 10.8 m at any elevation from 15 to 85.
- **The feed horn's sides do not stop a ray from outside.** A ray cast down onto the horn's top passes its side faces and hits geometry inside, so measure the horn from its mesh (cap normals give the axis), never by raycasting onto it. The cradle's pose came from the mesh fit. The two-way sight check still blocks eyes under the horn (9/145 clear, the rest at the rim).
- **A sight line from inside the cabin passes.** Raycasts never hit a mesh collider's back faces, so a one-way cast from an eye inside (or behind) the feed cabin leaves through its wall unseen. `WallTakeReach` casts both ways; an edit-time sample of 400 eyes at 3.5 m cleared 153/154 in front of the face and 0/180 behind it. Probes in a preview scene must use that scene's `PhysicsScene`: `Physics.*` queries the main scene and sees nothing there.
- **The client and server ask from different points.** The crosshair asks from the camera, the server from the taker's body point nearest the device, which is never further away, so an honest take is never refused. A peer's camera is not something the server has.
- **The cradle sits under a 100x-scaled pivot.** `TransmitterCradle` carries local scale 0.01 so its lossy scale is 1 and its `BoxCollider` and `PackSurface` are in metres; `PackSurface.ToLocal` divides lossy scale out either way.
- **The prefab is rebuilt in place** (`PrefabUtility.LoadPrefabContents`, children replaced, root kept), which is what keeps its GUID, `prefabId` and NetworkObject hash, and so the Chunk_6_3 instance, across a re-export.

## Extending

- **An old save** keeps a turned dish's angles; an UNTOUCHED dish has no record, so it comes back pointing up.
  With no `dishConsole` record the drives start dead and wake on the first server frame if the board's saved
  contents no longer hold the hook. The quest marker and remarks are [Objectives.md](Objectives.md)'s.
- **Retune the motor** on `DishRig` (`azimuthMotor`, `elevationMotor`) — tests in `DishControlTests` read `DishSlew`.
- **A real motor sound** replaces `AudioCatalog` entry 706 (`DishMotorLoop`); nothing else changes.
- **Re-bake only what changed:** `satellite_tower_export.py -- Interior` unwraps every atlas but bakes only the named ones; the rest keep their PNGs, which still match because Smart UV Project is deterministic on unchanged geometry. A full bake beside a running Unity can run the machine out of commit (it did at 2.9 GB free, 2026-10-06).
- **The mission** reads the cradle's `WallInventory.Layout` (empty = taken) and the lander's `ShipPartRack` (`IsInstalled` on the transmitter socket's index, 11; `Changed` fires on every machine). Nothing mission-specific is in the tower.
- **Re-export** with `satellite_tower_export.py`, then re-check the pivot axes on `DishRig` and `DoorInteraction` if the hierarchy changed.
