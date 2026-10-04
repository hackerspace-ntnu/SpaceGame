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
  - "a seated resident stands up for a second to greet or stretch, then sits again"
  - "a seated resident's legs pass through the pot or cushion it sits on"
  - "a seated resident hovers above its seat, or its feet hang above the floor"
  - "a resident on the pillow sits with its lower legs inside the cushion and a foot poking out underneath"
  - "PlayerSeating.OnDestroy() hides inherited member NetworkBehaviour.OnDestroy()"
reads_with: [Residents, InteractionSystem, Multiplayer, PlayerCharacter, Vehicles, ArtPipeline]
updated: 2026-10-04
---

# Seats

Sitting used to have no object: the society sampled a ground height under a sit spot and a body was lifted to it. Now a
[`Seat`](Assets/Game/Scripts/World/Seating/Seat.cs) is the thing sat on, so it can be missing, moved or taken. A resident
whose spot is a sit takes a free seat or does not sit; a player right-clicks a free seat.

## Model

| Idea | Mechanism |
|---|---|
| A seat is scenery | `Seat` on a decoration prefab: no `NetworkObject`, nothing saved, the same bytes on every machine. `Deco_Seat_Clay`/`_Pillow`/`_Wood` (Decorations/Furniture) are `Coll_Deco_Seat_*` of `decorations.blend`, true scale, **not static** (a seat may move), each with `Seat` + `ChairPose` + a trigger on the root; Clay and Wood also a solid `Colliders` child |
| What a seat says | `SitPoint` (hips rest here, +Z = facing), `footDrop` (metres, the seat's own units, from the sit point down to the floor), `pose` (`SeatPose`: how a **resident** sits, below), `standUpDistance`, `moveTolerance` / `turnTolerance` |
| How a resident sits | `Seat.Pose`, stored on each seat prefab. **`Stool`** (all three prefabs, `Deco_Seat_Clay`, `_Wood` and `_Pillow`: each sits 0.56 m up with a solid body under the sitter): `ResidentPresence` raises the animator's `Seated` bool, so the **base layer's chair sit** plays (knees bent, feet down the front) and the spot's loop is **not** held; the body reads `Seated` to `BodyLanguage`. **`Floor`**: the cross-legged loop of the spot's cue (`sitground`), held; **no prefab uses it now** (see Gotchas: it puts the legs of `Sit Ground` inside a 0.56 m seat), keep it for a cushion lying on the ground or delete it. `SeatedBodyFit` puts the model on the sit point for both: a Floor sit is only lifted (`seatHipsAboveSurface`), a Stool sit is put where its hips rest `stoolHipsAboveSurface` above the sit point, lowered by at most `stoolMaxDrop` (the chair clip's hips are 0.08 m above a 0.56 m seat). A player's pose is the chair's (`ChairPose`) whatever `Pose` says |
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
| [`ResidentPresence`](Assets/Game/Scripts/agents/Residents/Body/ResidentPresence.cs) | `seat` NetworkVariable; every machine records the claim on its own copy of the seat, raises `Seated` for a `Stool` seat, and holds the sit loop only on a `Floor` seat |
| [`SeatedBodyFit`](Assets/Game/Scripts/agents/Residents/Body/SeatedBodyFit.cs) | after the animator: moves the model until the hips rest on the seat (`TargetLift`: lifted for a Floor sit, lifted or lowered by up to `stoolMaxDrop` for a Stool sit); visual, local |
| [`PlayerSeating`](Assets/Game/Scripts/Characters/Player/Movement/PlayerSeating.cs) | `NetMsg.SitRequest` (120) / `StandRequest` (121), owner to server on the player's relay |
| [`SeatPlacer`](Assets/Game/Scripts/agents/Residents/Editor/SeatPlacer.cs) | menu `Tools/SpaceGame/Residents/Place Seats At Sit Spots`, also run by `ResidentErrandContentBuilder.Run` |
| `SettlementPlaces.SeatlessSpots` | each seat serves ONE spot; used by Generate's problem list and `SeatTests` |

## Flows

- **Resident sits (server).** `ResidentRoutine.Publish` calls `seating.Sync(society, plannedPlace, goal.HasArrived)`. At a seated, usable place: `Seat.NearestFree` → `TryClaim` → `NpcSeating.Suppress` (agent, motors, kinematic, `RidesAsPassenger`) → `NetworkedTeleport.Move` to `FeetPosition`, `Facing`. `Publish(..., seatId)` follows.
- **Resident stands.** The plan names another place, an override, or the seat moves: body to the spot's stand point, `NpcSeating.Restore`. A death or teardown `Abandon`s where it is. While seated `AtPlace` stays true (the seat is not on the stand point) and a conversation keeps the place.
- **Every machine.** `ResidentPresence.ShowSeat` resolves the id (retried each frame until the chunk is loaded), claims it, sets the `Seated` bool when the seat is a `Stool`, and `CueFor` holds the spot's loop only on a `Floor` seat. No seat = the resident stands.
- **Player sits.** Interact on a free seat → `PlayerSeating.RequestSit` → server: `Seat.Find`, `TryClaim`, writes the id → every machine claims; the owner `NetworkedTeleport.Move`s its capsule feet onto `FeetPosition`, switches `PlayerMovement` off, `CarriedBody.Hold`s the body, `ChairPose` raises the `Seated` bool.
- **Player stands.** Jump or interact (not on the frame they sat), death, or the seat moving: server clears the id; the owner stands at `StandUpPosition`, then gets its weight and movement back.

## Multiplayer

- Server decides both claims; nothing but ids travel. `NetMsg` 120/121 are owner → server on the player's relay and are checked with `Network.MayActFor`.
- A late joiner reads the resident's `seat` and the player's `seatId` with the spawn. A seat in a chunk not yet loaded resolves later.
- Residents' bodies reach clients through their own transform sync; players' through the owner-authoritative `NetworkTransform` and the replicated Animator.
- **Verified 2026-10-03, offline host only:** a whole in-game day in Play after the `AgentController` fix: 16 residents sat at some point, up to 8 at once (evening hearth), **0 of 2,571 seated samples with the NavMeshAgent enabled**, none off its seat, four rendered sitting on the clay pot, the pillow and the cushion. Seated residents gesture with their arms only (Wave Hello, Shrug, Hat Tip, Talk loops: two greeting runs of 7 sitters, no Full one-shot while seated) and a sitter that starts to talk keeps its pose with no blip. **Not run:** any client, `PlayerSeating` in Play, a save/reload (the `settlement-host`/`settlement-client`/`settlement-persist` autotest now logs a `*_BODIES` line with `seat=<id>+` per resident to compare; the player build was not made, see Testing.md).
- **Verified 2026-10-04, edit mode only:** the Seat/SeatedBodyFit/pose changes compiled (domain reload checked: `Seat.pose` present in the loaded assembly) and `SeatTests` (16), `StandPointTests` (8, incl. the stool fit) pass over the bridge; a Raxy rendered on all three seats rests its pelvis on the seat with its feet on the floor (see Gotchas). **Not run:** Play with the chair sit on a real resident (the settle blend), a client, a save/reload.

## Persistence

**Nothing is saved.** Seats are authored scenery, so there is no seat record; occupancy is re-derived: after a load the plan puts a resident back at its spot and it sits again, and a player loaded mid-sit stands where they were. `RidesAsPassenger` and the kinematic hold are runtime state no saver captures.

## Gotchas

- **A module that seats its own body mid-`Update` must stop the controller ticking the motor in that pass.** `ResidentRoutine` runs inside `AgentController.Update`; the controller then called `Motor.Tick`, whose parked-agent recovery (`NavMeshAgentMotor.TryReattachToNavMesh`) switched the freshly disabled agent back on and snapped the body 0.2-0.7 m onto the NavMesh. `AgentController.Update` now returns when `RidesAsPassenger` was raised during module evaluation. The symptom was seated residents with `agent.enabled == true` and a body off its seat.
- **A spot is a NavMesh stand point, the seat is where the body goes.** The seat stands on the spot, but `HasArrived` is measured to the stand point, so `ResidentRoutine.AtPlace` includes `seating.IsSeated`.
- **Two residents at one place:** errand stops share spots. The second finds its seat taken and stands, with no warning. Only "no Seat within reach at all" logs, once per place (`SettlementSociety.ReportOnce`).
- **A resident on a seat gets no full-body one-shots** (`BodyLanguage.SitOn`, set from `ResidentPresence`): its animator reads Standing, so a greeting picked a standing `Curtsey` that stood it up out of the sit for ~1.3 s. And `ResidentPresence.Apply` keeps the same loop at the same place through a change of activity (Sitting to Talking): it used to release and re-hold it, which faded the sit out and back in for a frame or two. Both found in Play 2026-10-03; see [HumanoidAnimation.md](HumanoidAnimation.md) Gotchas.
- **A floor sit lifted onto a pot puts the shins through the pot (found in Play 2026-10-03, fixed 2026-10-04, seen in edit-mode renders).** The sit loops are ground sits (cue `sitground`) and the fit lifts the model to the sit point, so the crossed legs lie at seat height, wider than a 0.64 m pot. `Seat.Pose` picks the sit per prefab: the chair sit of the base layer on a seat with a body under the sitter. Rendered 2026-10-04 (preview-scene renders of a real `Drifter_Raxy 2` on each prefab, front, side and three quarters, the real `Animator` stepped with `Update`, skinned meshes baked): legs hang clear of the pot wall, the stool and the cushion, 0 body vertices inside the solid colliders at the settled pose.
- **The pillow is a 0.56 m pouf, not a floor cushion, so it is a Stool seat too (2026-10-04).** Measured on a Raxy (model scale 1.22): `Sit Cross Leg` sits on top of it well (hips 0.58 to 0.63 m up, fit lifts to 0.68), but `Sit Ground` (the other loop of the `sitground` pool, picked per spot by `BodyLanguage.Hold`) is a squat with the hips 0.83 m up, knees 0.40 and feet 0.13: the fit never lowers a Floor sit, so the lower legs went down inside the cushion and a foot came out through its rope base. The `sitground` cue says "hips at ground level" and `Sit Ground` is not, on this rig; fixing the pool (and `StationTable.md`) was left alone. So the pillow's `pose` is `Stool`; `SeatTests.ASeatPrefabSaysHowItsSitterSits` pins all three.
- **The chair sit's hips are 0.08 m above a 0.56 m seat and the feet 0.11 m above the floor (2026-10-04).** The clip was made for a higher chair: a Raxy's hips joint rests 0.217 m (0.178 at scale 1) above the lowest vertex of its pelvis, so with the hips on `seatHipsAboveSurface` (0.1) the body hovered. `SeatedBodyFit` now lowers a Stool sitter to `stoolHipsAboveSurface` (0.18) above the sit point, never by more than `stoolMaxDrop` (0.1 per metre of scale: the standing pose it blends out of is far higher and would sink the body through the floor for the blend). Measured after: lowest hips-zone vertex 0.563 over a 0.564 sit point, lowest vertex of the body 0.02 above the floor, all three seats. Stepped through the standing-to-seated crossfade in edit mode with the compiled `SeatedBodyFit` (standing, `Seated` raised, 60 x 0.02 s): settled drop 0.081 m, deepest drop 0.094 m, toe bones never below 0.065 m over the floor. **Not seen in Play** at a real frame rate.
- **A stool sitter holds no loop of the spot's, so `ResidentPresence.IsSitting` is true with `heldCue == null`.** The fit used to ask for a held loop as its "is sitting" signal; with the chair sit the signal is the seat and its pose. A sitter on a stool reads posture `Seated` (its `talking` hold picks the `Talk Seated` Full loops), a sitter on a cushion still reads `Standing`; `BodyLanguage.SitOn` keeps both off Full one-shots.
- **The Clay and Wood solid colliders carve the world NavMesh at the next bake**; the world NavMesh was not re-baked.
- **A seat is an `IInteractable` inside a settlement**: `SettlementInteractableTests` allows exactly doors, gates and seats.
- **The player root is not at the soles**: `PlayerSeating.RootFor` moves the capsule's feet onto the seat's feet.
- **`PlayerSeating` is a `NetworkBehaviour`**: on `PlayerCharacterNetworked`, never the base prefab, and its `OnDestroy` must be an override.
- **Re-saving a prefab with `SaveAsPrefabAsset` drops serialized fields the class no longer has** (`description`, `lookSeconds` on four `Deco_*` roots).
- **Changing a seat's name or place in its hierarchy changes its id.** Harmless, since nothing stores one.

## Extending

- **A new seat:** a `Coll_Deco_*` in `decorations.blend`, exported with `_exportlib.export_collections`, a prefab with `Seat` + `ChairPose` + a root trigger (copy a `Deco_Seat_*`), set it as `SpotUse.seatPrefab` or add it to `SeatPlacer.DefaultSeats`.
- **A new sit spot:** a `SettlementSpot` whose `SpotUse` is `seated`, then run the placer; `SeatTests.EverySitSpotOfEveryPrefabHasASeat` fails until it has one.
- **A seat's pose:** set `Seat.pose` on the prefab: `Stool` for anything with a body under the sitter, `Floor` only where crossed legs fit on the seat itself (`SeatTests.ASeatPrefabSaysHowItsSitterSits` pins the three).
- **Not built:** seats a player puts down at runtime (would need a `NetworkObject`, a registered prefab and a `SaveableEntity`).
