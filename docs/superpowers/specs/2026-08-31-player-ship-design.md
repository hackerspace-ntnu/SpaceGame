# PlayerShip — design

2026-08-31. A second drivable ship in the ShipRV mould, built from the user's hand-modelled
lander (`Assets/Game/Art/Models/_Source~/models/vehicles/ship_lander_blockout.blend`,
**never edited** — it is the user's hand-built file; see `project_lander_blockout` memory).
Everything below is generated *from* that model: a new export script writes the FBX, a new
editor builder assembles the prefab, and the runtime behaviour composes from the existing
vehicle components (`ArticulatedPart`, `ArticulatedPartInteraction`,
`VehicleDeploymentController`, `MountModule`/`SteerModule`, `HoverRigidbodyMotor`) — per the
no-copy-paste rule, no new per-feature door/sync scripts.

## Deliverables

1. `player_ship_export.py` (beside the other vehicle sources) — opens the blockout .blend
   read-only, renames the role meshes in memory, drops `Ref_ExampleHull`, exports
   `Assets/Game/Art/Models/Vehicles/PlayerShip/player_ship.fbx`. Never saves the .blend.
2. `Assets/Game/Editor/Vehicles/PlayerShipBuilder.cs` — **Tools ▸ Vehicles ▸ Build PlayerShip
   Prefab** → `Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab`. Everything
   measured from the meshes at build time, ShipRVBuilder-style; `VerifyParts` refuses to build
   on a renamed mesh.
3. Network prefab registration, saveable wiring, `GlobalObjectIdHash` stamping, and a scene
   instance in `Ferdinand_Test_world` — all run headlessly in batchmode.
4. EditMode tests: persistence round trip (a door left open survives save/load) and builder
   wiring assertions.

## The moving parts (all `ArticulatedPart`, networked by `ArticulatedPartInteraction`)

| Part | Model meshes | Motion |
|---|---|---|
| Back door | `back_door` + 3 `back_door_support` ribs, one pivot | **Slide down** along its own 34°-tilted plane until the aft doorway is clear. Distance serialized; verified against ground contact in play mode. |
| Sliding door | `sliding_door_1..4`, one pivot each | Telescope: each leaf slides along the shared down-forward hull diagonal onto the lowest leaf's position — leaf 1 travels ~3 slots, leaf 4 a half slot, same speed, so they arrive staggered and **collect at the (forward-lower) side** of the opening. Authored pose = closed. |
| Boarding stair ("ladder") | `Cube.129` (stepped ramp to the ground) + `Cube.119` foot | Authored pose = deployed. The builder re-bases the pivot so *stowed* (tucked into the below-deck void) is the closed pose and the authored position is `openDistance` away. Carries an invisible ≤32° ramp collider so the Rigidbody capsule (no step offset) can actually climb it. |
| Sill platform | `Cube.043` | Same re-basing: slides out from under the sliding-door sill when the door opens. |

One `ArticulatedPartInteraction` on the top sliding leaf drives **leaves + stair + platform**
(the multi-part switch pattern), so opening the sliding door deploys the ladder and platform in
the same press; one on the back-door pivot drives itself. `VehicleDeploymentController.closeOnMount`
lists every part, so taking the controls seals the ship. No new NetMsg ids: `PartToggle`/`PartState`
already carry door state per switch index, and `ArticulatedPartsSaveable` persists every part by
hierarchy path.

## Hull, collision, cockpit

- The model has a real hand-built interior (140+ slabs), so instead of hand-boxing rooms the
  builder adds a convex `MeshCollider` per structural mesh above a small size threshold (the
  slabs are boxes/lofts, so convex hulls are exact); door/stair/platform pivots carry their own
  `BoxCollider`s, RV-style. Invisible ramp wedges cover the two interior step blocks
  (~0.8 m risers the player capsule cannot climb).
- Cockpit in the nose under the canopy dome: primitive fallback steering wheel (the model has
  no yoke) + `MountStation`, seat/dismount points measured off the forward deck plate,
  `mountableByDirectInteraction` off.
- Root component set mirrors the shipped ShipRV root exactly (minus `AgentGoal`/`AgentGoalSaveable`
  — no NPC drives this ship — and minus `AudioLoop`, since the audio catalog has no slot for it
  yet): Rigidbody, `AgentController`, `MountModule`, `SteerModule`, `VehicleDeploymentController`,
  `HoverRigidbodyMotor` (mass and probe footprint scaled to the 30 m hull), `NetworkObject`,
  `ClientNetworkTransform`, `NetRelay`, `NetAuthority`, `SaveableEntity`, `TransformSaveable`,
  `MotorStateSaveable`, `ArticulatedPartsSaveable`, `MountNetworkSync`, `UnderTerrainGuard`,
  `SandstormShelter`.

## Multiplayer / persistence (the non-negotiables)

- Root `NetworkObject`; registered via `NetworkPrefabRegistrar.Sync`; hash stamped with the
  ImportAsset + ForceReserializeAssets recipe (script-built prefabs otherwise ship hash 0).
- Doors ride the existing server-decides/broadcast flow in `ArticulatedPartInteraction`,
  including the late-joiner ask. Client-side proof via the two-process multiplayer test build.
- `Wire Saveable Prefabs` stamps the prefabId; `ArticulatedPartsSaveable` +
  `TransformSaveable`/`MotorStateSaveable` cover the state; round trip proven by a
  `PersistenceProbe` EditMode test and a play-mode F5/F9 pass.

## Assumptions (flagged, all cheap to change)

- "Ladder" = the stepped `Cube.129` stair the model already has under the side door.
- The back door buries its lower half in the sand when opened at rest; travel distance is a
  serialized tunable if the look or the ground contact misbehaves.
- PlayerShip is placed in `Ferdinand_Test_world` only (next to ShipRV) until the user decides
  it belongs in `persistentScene`.
