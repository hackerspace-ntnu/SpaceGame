---
system: Seats
layer: characters
summary: "A Seat is what residents and players sit ON: sit point, foot drop, one occupant; the claim replicates as an id"
paths:
  - Assets/Game/Scripts/World/Seating/
  - Assets/Game/Scripts/Characters/Player/Movement/PlayerSeating.cs
  - Assets/Game/Scripts/agents/Residents/Body/ResidentSeating.cs
  - Assets/Game/Scripts/agents/Residents/Body/SeatedBodyFit.cs
  - Assets/Game/Scripts/agents/Residents/Editor/SeatPlacer.cs
  - Assets/Game/Prefabs/Environment/Decorations/Furniture
  - Assets/Game/Editor/Tests/SeatTests.cs
symptoms:
  - "residents sit in the air, or inside a bench, instead of on something"
  - "a resident stands at its sit spot and never sits, with a clean console"
  - "[Residents] place N is a sit, but there is no Seat within 1.5 m of it"
  - "a seated resident's body is dragged off its seat, or its NavMeshAgent is enabled while it sits"
  - "I right-click a seat and nothing happens, or the prompt says Seat but the press is refused"
  - "a player stays seated after the seat was moved, or stands up inside the seat"
  - "two residents sit on one seat, or a player sits in a resident's lap"
  - "[Seat] 'X' and 'Y' derive the same id"
  - "PlayerSeating.OnDestroy() hides inherited member NetworkBehaviour.OnDestroy()"
reads_with: [Residents, InteractionSystem, Multiplayer, PlayerCharacter, Vehicles, ArtPipeline]
updated: 2026-10-03
---

# Seats

Sitting used to have no object: the society sampled a ground height under a sit spot and a body was lifted to it. Now a
[`Seat`](Assets/Game/Scripts/World/Seating/Seat.cs) is the thing sat on, so it can be missing, moved or taken. A resident
whose spot is a sit takes a free seat or does not sit; a player right-clicks a free seat.

## Model

| Idea | Mechanism |
|---|---|
| A seat is scenery | `Seat` on a decoration prefab: no `NetworkObject`, nothing saved, the same bytes on every machine. `Deco_Seat_Clay`/`_Pillow`/`_Wood` (Decorations/Furniture) are `Coll_Deco_Seat_*` of `decorations.blend`, true scale, **not static** (a seat may move), each with `Seat` + `ChairPose` + a trigger on the root; Clay and Wood also a solid `Colliders` child |
| What a seat says | `SitPoint` (hips rest here, +Z = facing), `footDrop` (metres, the seat's own units, from the sit point down to the floor), `standUpDistance`, `moveTolerance` / `turnTolerance` |
| One occupant | `TryClaim(who)` refuses while somebody else holds it, is idempotent for the holder; `Release(who)` ignores a stranger; a destroyed sitter counts as gone |
| A seat that moves lets go | `ReleaseIfMoved` (from `LateUpdate`): the sit point more than `moveTolerance` from where it was claimed, or turned past `turnTolerance`, raises `Vacated` |
| Identity | `Seat.Id` = [`SceneryId.Of`](Assets/Game/Scripts/World/SceneryId.cs) (FNV-1a of `SaveableEntity.DeriveAuthoredId`: scene + hierarchy path + sibling index; shared with `Pushable`), never 0; a static registry answers `Find(id)`, `NearestFree(point, reach)` (across the ground), `AnyWithin` |
| Residents | a sit spot (`SpotUse.seated`) claims the nearest free seat within `ResidentTuning.seatReach` of the **authored** spot, takes the body onto it, and publishes the id |
| Players | `PlayerSeating` (on `PlayerCharacterNetworked`): owner asks, the server decides, a `NetworkVariable<int>` carries the id |
| Where seats come from | `SeatPlacer` puts a `Seat_<spot>` instance on every **own** sit spot of every prefab (30 of them), at the spot, facing the spot, at true scale; `SpotUse.seatPrefab` picks the model (Shade: pillow), else it cycles Wood, Clay, Pillow |

## Key types

| Type | Role |
|---|---|
| [`Seat`](Assets/Game/Scripts/World/Seating/Seat.cs) | the component above; also `IInteractable` ("RMB: sit"), `IContextualInteractable` (only a player who may sit), `IInteractionMoment` (`None`) |
| [`ResidentSeating`](Assets/Game/Scripts/agents/Residents/Body/ResidentSeating.cs) | server only, owned by `ResidentRoutine`: `Sync(society, wantedPlace, arrived)` sits and stands, `Release` / `Abandon` |
| [`ResidentPresence`](Assets/Game/Scripts/agents/Residents/Body/ResidentPresence.cs) | `seat` NetworkVariable; every machine records the claim on its own copy of the seat and holds the sit loop only with a seat |
| [`SeatedBodyFit`](Assets/Game/Scripts/agents/Residents/Body/SeatedBodyFit.cs) | after the animator: lifts the model until the hips rest on `SitPosition.y`; visual, local |
| [`PlayerSeating`](Assets/Game/Scripts/Characters/Player/Movement/PlayerSeating.cs) | `NetMsg.SitRequest` (120) / `StandRequest` (121), owner to server on the player's relay |
| [`SeatPlacer`](Assets/Game/Scripts/agents/Residents/Editor/SeatPlacer.cs) | menu `Tools/SpaceGame/Residents/Place Seats At Sit Spots`, also run by `ResidentErrandContentBuilder.Run` |
| `SettlementPlaces.SeatlessSpots` | each seat serves ONE spot; used by Generate's problem list and `SeatTests` |

## Flows

- **Resident sits (server).** `ResidentRoutine.Publish` calls `seating.Sync(society, plannedPlace, goal.HasArrived)`. At a seated, usable place: `Seat.NearestFree` → `TryClaim` → `NpcSeating.Suppress` (agent, motors, kinematic, `RidesAsPassenger`) → `NetworkedTeleport.Move` to `FeetPosition`, `Facing`. `Publish(..., seatId)` follows.
- **Resident stands.** The plan names another place, an override, or the seat moves: body to the spot's stand point, `NpcSeating.Restore`. A death or teardown `Abandon`s where it is. While seated `AtPlace` stays true (the seat is not on the stand point) and a conversation keeps the place.
- **Every machine.** `ResidentPresence.ShowSeat` resolves the id (retried each frame until the chunk is loaded), claims it, and `CueFor` holds the spot's loop only with a seat. No seat = the resident stands.
- **Player sits.** Interact on a free seat → `PlayerSeating.RequestSit` → server: `Seat.Find`, `TryClaim`, writes the id → every machine claims; the owner `NetworkedTeleport.Move`s its capsule feet onto `FeetPosition`, switches `PlayerMovement` off, `CarriedBody.Hold`s the body, `ChairPose` raises the `Seated` bool.
- **Player stands.** Jump or interact (not on the frame they sat), death, or the seat moving: server clears the id; the owner stands at `StandUpPosition`, then gets its weight and movement back.

## Multiplayer

- Server decides both claims; nothing but ids travel. `NetMsg` 120/121 are owner → server on the player's relay and are checked with `Network.MayActFor`.
- A late joiner reads the resident's `seat` and the player's `seatId` with the spawn. A seat in a chunk not yet loaded resolves later.
- Residents' bodies reach clients through their own transform sync; players' through the owner-authoritative `NetworkTransform` and the replicated Animator.
- **Verified 2026-10-03, offline only:** residents sat on the seats in a Play run of the settlement (20 seated at once, bodies on their seats, one rendered sitting on a wood chair by the fire) and `SeatTests` pass. **Not run:** any client, `PlayerSeating` in Play, a save/reload, and Play after the `AgentController` fix (the run that found the agent being switched back on predates it).

## Persistence

**Nothing is saved.** Seats are authored scenery, so there is no seat record; occupancy is re-derived: after a load the plan puts a resident back at its spot and it sits again, and a player loaded mid-sit stands where they were. `RidesAsPassenger` and the kinematic hold are runtime state no saver captures.

## Gotchas

- **A module that seats its own body mid-`Update` must stop the controller ticking the motor in that pass.** `ResidentRoutine` runs inside `AgentController.Update`; the controller then called `Motor.Tick`, whose parked-agent recovery (`NavMeshAgentMotor.TryReattachToNavMesh`) switched the freshly disabled agent back on and snapped the body 0.2-0.7 m onto the NavMesh. `AgentController.Update` now returns when `RidesAsPassenger` was raised during module evaluation. The symptom was seated residents with `agent.enabled == true` and a body off its seat.
- **A spot is a NavMesh stand point, the seat is where the body goes.** The seat stands on the spot, but `HasArrived` is measured to the stand point, so `ResidentRoutine.AtPlace` includes `seating.IsSeated`.
- **Two residents at one place:** errand stops share spots. The second finds its seat taken and stands, with no warning. Only "no Seat within reach at all" logs, once per place (`SettlementSociety.ReportOnce`).
- **Seat height is vertical only in the fit.** The sit loops are ground sits (cue `sitground`); the model is lifted to the sit point, so legs cross at seat height.
- **The Clay and Wood solid colliders carve the world NavMesh at the next bake**; the world NavMesh was not re-baked.
- **A seat is an `IInteractable` inside a settlement**: `SettlementInteractableTests` allows exactly doors, gates and seats.
- **The player root is not at the soles**: `PlayerSeating.RootFor` moves the capsule's feet onto the seat's feet.
- **`PlayerSeating` is a `NetworkBehaviour`**: on `PlayerCharacterNetworked`, never the base prefab, and its `OnDestroy` must be an override.
- **Re-saving a prefab with `SaveAsPrefabAsset` drops serialized fields the class no longer has** (`description`, `lookSeconds` on four `Deco_*` roots).
- **Changing a seat's name or place in its hierarchy changes its id.** Harmless, since nothing stores one.

## Extending

- **A new seat:** a `Coll_Deco_*` in `decorations.blend`, exported with `_exportlib.export_collections`, a prefab with `Seat` + `ChairPose` + a root trigger (copy a `Deco_Seat_*`), set it as `SpotUse.seatPrefab` or add it to `SeatPlacer.DefaultSeats`.
- **A new sit spot:** a `SettlementSpot` whose `SpotUse` is `seated`, then run the placer; `SeatTests.EverySitSpotOfEveryPrefabHasASeat` fails until it has one.
- **Not built:** seats a player puts down at runtime (would need a `NetworkObject`, a registered prefab and a `SaveableEntity`), and legs that hang (needs a chair loop cue).
