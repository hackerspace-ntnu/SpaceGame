---
system: SatelliteDish
layer: world
summary: "The broken satellite tower prefab: door, catwalk ladder, and a lectern that takes over the dish motors"
paths:
  - Assets/Game/Scripts/Gameplay/DishControl
  - Assets/Game/Scripts/Gameplay/Interaction/Core/ClaimableConsole.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/DishRigSaveable.cs
  - Assets/Game/Prefabs/Environment/Structures/SatelliteTower
  - Assets/Game/Art/Models/Environment/Structures/SatelliteTower
  - Assets/Game/Art/Textures/Environment/SatelliteTower
  - Assets/Game/Art/Materials/Environment/SatelliteTower
  - "Assets/Game/Art/Models/_Source~/models/buildings/satellite_tower_export.py"
symptoms:
  - "the satellite dish turns for the operator but not for the other player"
  - "the dish snaps back to where it was after loading a save"
  - "the dish control lectern says In use and nobody is at it"
  - "steering the dish does nothing for a client although the camera cut to the dish"
  - "the dish swings its hanging beams into the shack roof"
  - "a grapple hooked to the dish stays in the air while the dish turns, on the other player's screen"
  - "the satellite tower's door swings into the room, or about the wrong axis"
reads_with: [InteractionSystem, Terminal, Ladders, ArtPipeline, Multiplayer, Persistence]
updated: 2026-10-05
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

> **Unverified (2026-10-05):** never run in Play mode, on a client, or through save/reload. Only EditMode
> checks (`DishControlTests`, the persistence round trip and wiring sweeps) and edit-time physics probes passed.

**Scope:** [`Gameplay/DishControl/`](Assets/Game/Scripts/Gameplay/DishControl), the claim base
[`ClaimableConsole`](Assets/Game/Scripts/Gameplay/Interaction/Core/ClaimableConsole.cs) (shared with
[Terminal.md](Terminal.md)), the prefab and its art. **Related:** [InteractionSystem.md](InteractionSystem.md)
(the door, the press), [Ladders.md](Ladders.md), [ArtPipeline.md](ArtPipeline.md) (the bake).

## Model

- **Three components, the terminal's split.** [`DishRig`](Assets/Game/Scripts/Gameplay/DishControl/DishRig.cs)
  owns the angles (server-decided, replicated); [`DishConsole`](Assets/Game/Scripts/Gameplay/DishControl/DishConsole.cs)
  owns who is at the controls and forwards their command; [`DishControlSession`](Assets/Game/Scripts/Gameplay/DishControl/DishControlSession.cs)
  is the operator's machine only: camera, readout, raw input.
- **The motor is arithmetic.** [`DishSlew.Step`](Assets/Game/Scripts/Gameplay/DishControl/DishSlew.cs) ramps a
  velocity toward `command × MaxSpeed` at `Acceleration`, and on a limited axis caps it at `sqrt(2·a·d)` so the
  dish brakes ONTO its limit. Tunables are two [`SlewMotor`](Assets/Game/Scripts/Gameplay/DishControl/SlewMotor.cs)s
  on `DishRig` (azimuth 6°/s, 4°/s², wraps; elevation 4°/s, 3°/s², 15-85).
- **Angles are the source rig's.** Azimuth 0-360 about the tower's vertical; elevation in degrees above the
  horizon, rest 40 (the Blender pivot's 50° about its X is `90 - elevation`). Each pivot turns from its imported
  rest pose about a serialized LOCAL axis (`azimuthAxis`, `elevationAxis`), measured and signed when the prefab was
  built — the FBX empties carry the axis conversion, so no axis is assumed.
- **The tower is its own entity.** The prefab root carries a `NetworkObject` (`SynchronizeTransform` and
  `AutoObjectParentSync` off) + `NetRelay`: the channel for the door's `NetLatch`, the dish's variables and the
  console's claim. It is placed in a scene, never spawned; see Gotchas.
- **The door** is a stock [`DoorInteraction`](Assets/Game/Scripts/Gameplay/Interaction/Interactions/DoorInteraction.cs) on
  `SatTower_Int_Door` (an unrotated empty the export adds as the hinge's parent), single leaf: `_leftDoor` =
  `SatTower_Int_DoorHinge`, `swingDegrees` 100, `swingAxis` the hinge's measured local vertical, signed outward.
  A `BoxCollider` on `SatTower_Int_DoorLeaf` swings with it.

## Placement

The main world has one tower, placed 2026-10-05 the way every other authored structure is (the colony,
the outposts, the refinery): a root prefab instance in a chunk scene, saved with the scene.

| Field | Value |
| --- | --- |
| Scene | [`Chunk_6_3.unity`](Assets/Game/Scenes/world/Chunks/Chunk_6_3.unity) (the nomad settlement's chunk), root object `SatelliteTower`, beside the `Terrain_6_3` root and NOT under `Settlement` |
| Pose | (3160, 116.65, 925), yaw 207.7° (front stairs, local +Z, face the landing site) |
| Site | East rim of the plateau the landing site stands on, ~400 m from the ship's landing spot (2974, 571) at bearing 28°; the ground drops ~10 m to a basin 38 m east of the tower. Footprint 85 m north of the settlement's bounds, ~350 m from the Strider city's start (3368, 1199) |
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
| `DishConsole` | `ClaimableConsole`, `IInteractable`, `IInteractionReadout` ("Dish control", "In use"). `Steer(command)` → `SteerServerRpc`, accepted only from `OperatorId`; any operator change zeroes the drive. |
| `ClaimableConsole` | The one-operator claim lifted out of `TerminalConsole`: `NetworkVariable<ulong>` operator, `RequestClaim`, `Release`, disconnect release, `OnOperatorChanged`. |
| `DishControlSession` | `GameplayMenuScope.Enter(freezeTime: false)`, spawns `DishFocusCamera` at `FeedVantage`, shows `DishReadout`, sends the quantised command on change, `Exit` on RMB / Esc / B / death / disable. |
| `DishFocusCamera` / `DishReadout` | A `FocusCamera` that CUTS (fly-in 0) to a fixed vantage; the code-built screen overlay (angles, limits, the commanded direction lit at once). |
| `DishRigSaveable` | Key `dishRig`: `{ azimuth, elevation }`, nothing at rest. |

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
| Camera, readout, input | none | Local to the operator |

## Persistence

| State | Saver | Key |
| --- | --- | --- |
| Dish angles | `DishRigSaveable` → `DishRig.RestoreAngles` (instant, published) | `dishRig` |
| Door open | `DoorSaveable` on `SatTower_Int_Door` | `door` |

Both are auto-attached by `SaveablePolicy` (Wire Saveable Prefabs). The operator claim is not saved.

## Gotchas

- **The world NavMesh does not know about the placed tower until it is re-baked.** It is one author-time bake
  ([NavMeshSystem.md](NavMeshSystem.md)); after the 2026-10-05 placement it was NOT re-baked, so NPCs path
  through the plinth and `WorldNavMeshBuildCheck` fails the next player build. Close every chunk scene, run
  `World/Streaming/Bake World NavMesh`, commit `WorldNavMesh.asset`.
- **A saved world remembers where the tower stood.** The root's `TransformSaveable` writes its pose like any
  authored entity's, so moving the placed instance in `Chunk_6_3` changes new worlds only; an existing save
  restores the old pose onto it. Delete that record (`instanceId 3026bcb1…`) or start a new world after a move.

- **Scene-placed, so a NetworkObject inside the prefab is deliberate — and the scene must be saved.** NGO
  gives an in-scene instance its own hash only when the scene is saved; until then every instance carries the
  prefab's. Two towers in one unsaved scene collide. (`SettlementNetworking.Wrap` avoids this for generated
  buildings by wrapping at generation time; a hand-placed tower has no generator.)
- **A peer resolves only the NetworkObject a grapple hit, not the panel.** `GrapplingHookArtifact.BindAttach`
  therefore looks for the root's own collider at the hook point (`attachProbeRadius`) so the rope rides the turning
  dish on every machine; the owner, who runs the physics, always had the panel.
- **Below ~14° elevation the hanging beams strike the shack roof**; the 15° floor is a collision limit, not taste.
- **The stairs are ramps.** The player has no step offset, so every flight has a `Colliders/Ramp_*` box 3 cm above
  its nosing line (ground flight 32°, the deck dogleg two 34° flights, the door step and the entry stairs); the mesh
  treads stay solid under it for the rails.
- **The floor hatches are 1.17 m pits** nobody without a step offset climbs out of, so each is capped at floor level by a
  `Colliders/HatchCap_n` box, found by a ray grid over `SatTower_Int_FloorHatches` when the prefab is built.
- **The column ladder in the control room is NOT climbable**: it ends at a closed plate under the solid pedestal.
- **`Ladders/CatwalkLadder`** runs roof deck (13.5 m) to catwalk (26.1 m) at -67.5°, climbed from outside the cage; its
  `Exit` is inward at r 9.5 m, inside the 2.5 m clear ring between the azimuth bearing (r 8.1) and the rail.
- **The prefab is rebuilt in place** (`PrefabUtility.LoadPrefabContents`, children replaced, root kept), which is what
  keeps its GUID, `prefabId` and NetworkObject hash, and so the Chunk_6_3 instance, across a re-export.

## Extending

- **Retune the motor** on `DishRig` (`azimuthMotor`, `elevationMotor`) — tests in `DishControlTests` read `DishSlew`.
- **A real motor sound** replaces `AudioCatalog` entry 706 (`DishMotorLoop`); nothing else changes.
- **Re-export** with `satellite_tower_export.py`, then re-check the pivot axes on `DishRig` and `DoorInteraction`
  if the hierarchy changed.
