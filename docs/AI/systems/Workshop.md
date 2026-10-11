---
system: Workshop
layer: world
summary: "The mountain workshop: thirteen rooms turned by hand cogs, a web of tunnels, a hoist that runs itself"
paths:
  - Assets/Game/Scripts/World/Workshop/
  - Assets/Game/Scripts/Vehicles/Stations/StationFooting.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/WorkshopSaveable.cs
  - Assets/Game/Editor/Workshop/
  - Assets/Game/Editor/Tests/WorkshopHoistRoundTests.cs
  - Assets/Game/Editor/Tests/WorkshopSaveableTests.cs
  - Assets/Game/Editor/Tests/WorkshopTurntableTests.cs
  - Assets/Game/Art/Models/_Source~/models/buildings/workshop_export.py
  - Assets/Game/Art/Models/Environment/Structures/Workshop/
  - Assets/Game/Prefabs/Environment/Structures/Workshop/Workshop.prefab
  - Assets/Game/Scenes/Tests/WorkshopTest.unity
symptoms:
  - "the hand cog says: Step up to the hand cog"
  - "somebody else has the grip"
  - "the room does not turn while I hold the mouse button on the hand cog"
  - "I let the button go and the room moved on / did not stay where I left it"
  - "I cannot walk while I hold the hand cog"
  - "the room turned and its entrance faces rock: I am shut in"
  - "I can see out of the caves through a gap over a tunnel mouth"
  - "I can walk through a tunnel wall / the walk check says HOLES IN THE WALLS"
  - "the workshop's gears do not turn, or turn through each other"
  - "the hand cog turns but its pinion, the sleeve gear or the ring on the wall stands still"
  - "the workshop prefab lost my hand edits"
  - "Rebuild Workshop Prefab says READ BACK WRONG"
  - "the export stops with: Blender renamed N exported node(s)"
  - "the Shellbacks in the workshop are all asleep and never get up"
  - "Generate placed 1/4 characters in the workshop"
  - "I cannot fit through a workshop tunnel or doorway"
  - "the rooms cannot be walked / a room can never be entered"
  - "the hoist stands still / is in a different place on another machine / where are the crane levers"
  - "I am not carried by the turning floor / I am swung off a stair that stands in it"
  - "after a re-export the Shellbacks cannot reach their beds (island the settlement cannot walk to)"
  - "an old save loads with the rooms back at the start"
reads_with: [Vehicles, InteractionSystem, Multiplayer, Persistence, Residents, Outposts, NavMeshSystem, ArtPipeline, Seats]
updated: 2026-10-11
---

# Workshop

A base inside a mountain, built in `workshop.blend`, and the maze of turning rooms that runs through it. Thirteen
round rooms turn: the main room (its whole bottom, floor and wall together, the ship's opening its one entrance)
and twelve small clay cave rooms, each one clay pot (wall and ceiling) with one doorway. Every room has a hand cog: a flat metal cog with a grip.
Hold a mouse button on it and the room turns for as long as the button is down; let go and it stays. There are
no stops. Twelve tunnels join the rooms, and most of them fork, so a tunnel is a place several rooms can turn
their entrance to. The engine hall is the end of it. A hoist runs round over the main room by itself, and four
Shellbacks live above.

## Model

| Idea | Mechanism |
|---|---|
| A room turns only while a button is held on its hand cog | `WorkshopWheel`, a `VehicleStation` ([Vehicles.md](Vehicles.md)): RMB (`Interact`) held within `reach` turns it one way, LMB (`SecondaryInteract`) the other, at `degreesPerSecond` (12 main, 28 small) eased over `rampSeconds`; the hold is the claim, and the button coming up (`PlayerInputManager.InteractHeld` / `UseHeld`) releases it. The angle is a float in degrees from the modelled pose, unwrapped |
| The holder stands still, the room goes round | `StationFooting` switches `PlayerMovement` off and pins the player where they took the grip for as long as they hold it; the room's carrier is told to leave them (`ClaimRider`). The cog is static, the floor turns under the feet |
| The hand cog really drives the room | Small rooms: a flat library gear (L) with a grip on its rim, on a shaft carried by an arm of the post in the room's middle; at the shaft's foot a pinion (XS, 10 teeth) bites the sleeve gear on the turning floor (L, 28); a tell-tale cog on the post's top is bitten by the hand cog. Ratios 2.8 and 7.84. Main room: the cog hangs beside the entrance, on its right as you come in from the lane, on a shaft under the gallery; at the shaft's top a pinion (XL, 40) runs inside a 500-tooth ring on the turning wall's top (`MainRoom_Drum_TopRing`, carried over the open entrance by `MainRoom_Drum_EntranceBeam`): 12.5 turns a turn, the same way round (`same_way` in the model's `workshop_wheel`) |
| The web | A room is entered only through the tunnel its entrance faces, and turned only from inside. So from a room you reach every tunnel it has a port on, and from a tunnel every room turned to it right now. Rooms keep their angle, which is the puzzle: the way on is often a room somebody must first leave facing the right way |
| The hoist runs itself | `WorkshopHoistRound`: one round planned from the stops (8 x 8 x 5), the hook table and a list of calls, and `At(seconds)` says where bridge, trolley and hook are at any time. `WorkshopMachinery` asks with the server's clock (`NetworkManager.ServerTime`, or this machine's when alone), so every machine sees the same hoist with nothing on the wire. Bridge and trolley only travel with the hook right up, and the hook stops on what stands under it (`hookLimits`, measured by the export). There are no levers and no other crane |
| Everything seen follows from that | `WorkshopMachinery.Update` copies each room's `WorkshopTurntable.Angle` to its channel (`WorkshopChannel.Room + n`), runs the idle and ambient channels and the hoist's three, and `WorkshopGearTrain.Apply` turns channels into angles and offsets |
| Gears mesh because the model says how | `workshop_export.py` walks the gear graph in Blender (pitch radii, shared shafts, bevel pairs, universal joints) and writes each part's channel and ratio into `workshop_layout.json`. Nothing about a ratio is typed in Unity |
| A room that turns is solid and carries people | Each turntable is a kinematic body with convex `COLT_` colliders (wall, floor, hub); a small room's wall, ceiling and floor turn as one piece (`WheelRoom_NN_Drum_ClayWall`, a pot with the doorway cut in it) inside a still cave shell that is seen only through the doorway and is the roof nobody gets through. A `WalkerPlatformCarrier` carries whoever is in its `CarryVolume` with a turning piece under their feet (`CarryOnlyThoseStandingOnIt`), so the pit stairs standing inside the main room carry nobody; wall pieces are carving `NavMeshObstacle`s |
| The prefab is generated | `WorkshopPrefabBuilder.Rebuild` writes `Workshop.prefab` from the FBX set and the layout, in place (GUID kept). `WorkshopHomeBuilder` adds what makes it a home |

The web, as built (`wheels` and `tunnels` in the layout; rooms `R01`..`R12`, `Main`, and `Final` = the engine hall):

| Tunnel | Joins | Tunnel | Joins |
|---|---|---|---|
| J1 | Main, R01, R02 | K1 | R05, R08, R07 |
| J2 | Main, R02, R05, R03 | K2 | R07, R09, R06 |
| J3 | Main, R05, R08 | K3 | R03, R04 |
| J4 | Main, R08, R11, R10 | K4 | R06, R04, Final |
| J5 | Main, R11, R12 | K5 | R08, R10, R09 |
| L1 | R01, R02 | L2 | R12, R11 |

The main room starts facing the ship lane, every small room facing one of its tunnels (port 0 in the layout).
Alone from the lane every room can be entered, and the shortest way through is Main > R05 > R07 > R06 > engine
hall (`WorkshopWalkCheck`, 2026-10-11: `35 of 35 mouths walk …, 0 leak when turned away. The walls hold`). With
the main room turned away from the lane, the lane stair, the gallery over the wall and a pit stair still lead in.

## Key types

| Type | Role |
|---|---|
| `WorkshopWheel` | the hand cog: the hold as a claim, turning, prediction on the holder's machine, `Restore(degrees)`, the prompt |
| `StationFooting` (Vehicles/Stations) | plain class any standing station can hold: `Begin(player, standBy, reach, onLetGo)`, `Keys`, `InteractHeld`, `UseHeld`, `Hold()` in `LateUpdate`, `End()` |
| `WorkshopTurntable` | follows the angle it is given: `Follow(degrees, instant)`, `Angle`, `IsTurning`; eases at `followDegreesPerSecond` on machines that only hear about it |
| `WorkshopHoistRound` | the hoist's round, serializable, pure: `At(seconds)`, `Seconds`. Tested without a scene |
| `Workshop` | the root, `IPersistentEntity`. Decides nothing |
| `WorkshopMachinery` / `WorkshopGearTrain` | presentation on every machine: `Spinner` (node, axis, ratio, channel), `Slider`, `Cable` |
| `WorkshopSaveable` | key `"workshop"`; null at the authored start |
| `WorkshopLayout` (editor) | the JSON as classes (`Wheel`, `Port`, `Tunnel`, ...); `Channel(name)` throws on a name it does not know, `RoomOf(channel)` reads `Room<n>` |
| `WorkshopPrefabBuilder` / `WorkshopHomeBuilder` / `WorkshopTestSceneBuilder` (editor) | menu **Tools/SpaceGame/Workshop/**: Rebuild Workshop Prefab, Rebuild Workshop Test Scene |
| `WorkshopWalkCheck` (editor) | **Check The Player Can Walk The Puzzle**, with `WorkshopTest` open: per tunnel, turns its rooms to it and bakes a NavMesh for the player's body (0.5 m radius, 3 m tall, no step) to see each wheel reached, then turns them away to see none leak; casts level rays across every tunnel and small room against the colliders (`The walls hold` / `HOLES IN THE WALLS`); then searches the web from the lane, the engine hall counting only if it was walked into. Writes `Temp/workshop_walk_check.txt` |

## Flows

- **Model to game.** `workshop_export.py` in a background Blender over a copy (`WORKSHOP_SRC`; it never saves
  the .blend) → `workshop.fbx` (static chunks), `workshop_moving.fbx` (one node per turning part, plus the
  turntables' collision), `workshop_collision.fbx`, `workshop_layout.json` → **Rebuild Workshop Prefab** → read
  the line it logs (`saved: 48/48 spinning parts, 13 turning rooms, 13 wheels …`) → Rebuild Workshop
  Test Scene → the walk check.
- **A hold.** Button down on the cog → `Interact` / `SecondaryInteract` → near enough? → `RequestClaim`
  (`NetMsg.StationClaim`) → the server mans it → `OnManned` on every machine (the owner's begins
  `StationFooting`) → which way travels as the station's request → `AdvanceOnServer` adds to the angle →
  `StationState` → `ApplyValue` → `Follow`. Button up → `RequestRelease`; the room coasts to a stop.
- **Where a room is in the model.** A `WheelRoom_NN_Pivot_Turn` (or the main room's) empty carries a
  `workshop_wheel` property: id, radius, gear ratio, stance, and `ports` (tunnel → degrees the room must turn to
  face it). The scene's `workshop_net` property lists each tunnel's hub and members. The export copies both.
- **The hoist.** Three nested sliders (bridge > trolley > hook) on `HoistBridge`, `HoistTrolley` and `Crane`, in
  metres, set every frame from `WorkshopHoistRound.At`. Its cables stretch with the hook. To change where it
  goes, edit `calls` on the prefab builder's side (the round's defaults) and rebuild.
- **Who lives there.** `WorkshopHomeBuilder`: a `Dwelling` (4 beds) on the root; under `Home` a `SettlementHeart`
  on the ledge over the lane's mouth, a `SettlementEntrance` at the loft's mouth, four sleeping mats in the loft
  (each a `Deco_SleepingMat_Set`, a `Lie` seat and a `Bunk` spot), a table and three clay stools in the mess,
  three `Workshop` posts on the ledge (`Post_Hoist` and two that watch the big hand cog, `Post_WheelWatch_*`) and three `View` spots. Place the
  prefab with an `Outpost` ([Outposts.md](Outposts.md)), list four `Shellback_Villager`, culture `ShellbackCulture`.

## Multiplayer

On the building's channel: `SettlementNetworking.Wrap` stands a networked wrapper round a building that holds a
`Workshop` (never put a `NetworkObject` in the prefab). The hand cogs are stations on that entity
(`StationClaim` 65 / `StationState` 66, [Vehicles.md](Vehicles.md)): one holder a cog, decided by the server;
the holder's machine turns its own copy at once and takes the server's angle when they let go. The hoist needs
no message: it is a function of the server's clock. `WorkshopOperate` 126 and `WorkshopState` 127 are burned
(the crane levers they carried are gone). With no wrapper above it a workshop runs alone on each machine.
**Not run on a client.** Everything has been run on an offline host only; a client's prediction, a claim that
lands after the button is up, and two players at two cogs are untested.

## Persistence

`WorkshopSaveable` through `SaveablePolicy.EnsureWorldInteractables`: `rooms`, each wheel's id → degrees, and
nothing else (the hoist is wherever the clock says). A save from before the wheels has no `rooms` and loads with
every room as modelled; one from when the cranes were worked by hand still brings its rooms back
(`WorkshopSaveableTests`). A restore lands in place; a client already connected hears a restored room with the
next claim on its cog. **Not run through a save and reload in a game:** `WorkshopTest` is opened directly and
such a scene cannot save ([Scenes.md](Scenes.md)).

## Gotchas

- **Edit the builders, never `Workshop.prefab`.** Rebuild rewrites it. Furniture, posts and beds are rows in `WorkshopHomeBuilder`.
- **Unity axes are (-x, z, -y) of Blender's, and a turn of +a about a Blender axis is -a about the converted one.**
  The layout file is already in Unity axes, in prefab space (origin: the main room's pit-floor centre).
- **Blender renames a new object whose name is taken.** The export renames the originals `_src_…` first and
  stops if any exported node ends in `.001`: a renamed node is a part the layout cannot find, and it stands still.
- **Nobody can be shut in, because the hand cog is inside.** A room left facing away from the tunnel you stand
  in is gone round, or needs a second player. After adding rooms read `All N rooms can be entered`.
- **A tunnel is taller than the hole it meets**, so every tunnel end carries a clay collar (`Net_*_Collar_*`): a
  wall across the tunnel with the doorway in it and a hood on through the dome, room shell, pit wall or the
  engine hall's wall. `leaks.py` (`.agent-staging/workshop/`) casts rays from the walkable places and counts
  those that see a back face or nothing: 4339 before the collars, 7 of 321,000 after (2026-10-11).
- **What is drawn is not what is bumped into.** Collision is only the names `COL_INCLUDE` matches in the export.
  The forked tunnels (`Net_J2_Tube_R05`) did not match `_Tube$` and had no walls at all until 2026-10-11, and
  with none the walk check passed a tunnel to the engine hall that doubled back on itself and was pinched shut.
  The walls check is what catches this; run it after any change to names or tunnels.
- **A new tunnel mouth in the main pit must sit between the wall's ribs** (`Bay_PitCapAndRibs`, one about every
  17 degrees). The five as built are at 125.8, 40.0, -11.4, -45.8 and 228.7 degrees.
- **The player is 3 m tall, half a metre in radius, and steps up nothing.** Tunnels are 3.5 m wide with the
  crown about 4.2 m up; the cave shell's underside (4.69 m round the main pit) stops them going higher.
- **Nothing may ride the main floor between 14.6 and 17.9 m out:** two fixed pit stairs stand in that ring, so the
  main hand cog hangs from the gallery. Its ring gear crosses the entrance 5.1 m up, under the 5.4 m subfloor.
- **`reach` is across the floor** (grip to stance plus 0.9 m, set by the builder); the eye-ray reaches further.
- **The residents' area is the upper levels only:** the ledge round the main room and the cave tiers above it,
  one NavMesh island. A stair post closes a pit stair to the world bake's 0.33 m voxels, so nothing joins the
  island to a turning room or the outside, and **only `Bay_Stair_*` rails are collision** (with the upper stairs'
  rails in, the ledge was cut from every bed). A post on a pit floor would be "an island" to Generate.
- **A scene with no running `DayNightCycle` stands at midnight** and every resident sleeps; the test scene's Sun carries one.
- **Heavy, untuned:** 105 point lamps without shadows, 521 mesh colliders; the clay is a flat colour.
- **Old parts still in the model:** four spinners of the retired drive stand still (the export lists them). The
  retired rooms, key gear, gantry and crane levers are in the .blend's `Cave_Retired`, which is not exported.
- **Verified 2026-10-11, offline host, Play in `WorkshopTest`, the cog taken by script with the mouse buttons
  fed to the Input System:** RMB held turned the main room at 11.5 degrees a second and LMB turned it back; the
  button up let go, the room coasted about 4 degrees and stood still; a small room turned 219 degrees, ceiling
  and all, to its second tunnel; the hoist ran its round by itself. Walk and walls checks as quoted (the walls
  check finds 13,509 rays out with the tunnel colliders switched off); 20 EditMode tests green. **Not run:** a
  real crosshair press on a cog, a player walking the web by hand, riding a room somebody else turns, a
  client, a save written and loaded, a workshop under the world bake.

## Extending

- **Another room:** in the .blend a turning pivot with a `workshop_wheel` property, its drum, post, hand cog, pinion and
  tell-tale (copy a `WheelRoom_NN_*` set), tubes to the tunnels it joins and its id in `workshop_net`. Export,
  rebuild, walk check. Nothing in Unity names a room.
- **Another moving part:** give it a pivot empty in the .blend and gear it to something that already turns; the
  export finds its channel. A part the walk cannot reach is listed in the export log and stays static.
- **Another call for the hoist:** a row in `WorkshopHoistRound.calls`; the round re-plans itself.
- **More people or furniture:** rows in `WorkshopHomeBuilder`, beds in step with `Dwelling.beds`; Rebuild, then
  Rebuild Workshop Test Scene and read its line (`4/4 residents …`).
