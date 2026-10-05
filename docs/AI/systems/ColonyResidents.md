---
system: ColonyResidents
layer: characters
summary: "The colony's people, their airlock crossings, beds and posts, its rovers and exterior stations"
paths:
  - Assets/Game/Scripts/World/Colony/AirlockPassage.cs
  - Assets/Game/Scripts/World/Colony/AirlockTransit.cs
  - Assets/Game/Scripts/Gameplay/Traversal/INavLinkGate.cs
  - Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.Links.cs
  - Assets/Game/Scripts/Vehicles/Rover/Patrol/
  - Assets/Game/Prefabs/Vehicles/ColonyRover.prefab
  - Assets/Game/Prefabs/Environment/Decorations/Astronaut/Exterior/
  - Assets/Game/Editor/World/ColonyAirlockPassageAuthoring.cs
  - Assets/Game/Editor/World/ColonyStandSnapper.cs
  - Assets/Game/Scripts/agents/Residents/Editor/ResidentErrandContentBuilder.Colony.cs
  - Assets/Game/ScriptableObjects/Residents/ColonyCulture.asset
  - Assets/Game/ScriptableObjects/Residents/Archetypes/Colony/
  - Assets/Game/ScriptableObjects/Residents/Lines/ColonyLines.txt
  - Assets/Game/Scripts/agents/Residents/Editor/Tests/ColonyContentTests.cs
  - Assets/Game/Editor/Tests/AirlockTransitTests.cs
  - Assets/Game/Editor/Tests/RoverRouteTests.cs
symptoms:
  - "a colonist stands on a building's roof and never comes down"
  - "a colonist waits at an airlock and nothing opens, or the hatch shuts on one in the doorway"
  - "colonists never go through an airlock, every interior place says unusable or an island the settlement cannot walk to"
  - "a colony rover stays parked and the log says it found no drivable loop"
  - "the colony's resident census reads walkable heart (0, 0, 0) after a reload"
  - "a colony's walkable heart is on a roof, so every place indoors is unreachable"
  - "a colonist sleeps standing beside its bunk, or pops onto the top bunk while I watch"
  - "colonists never sit at the dining nooks"
reads_with: [ColonyInterior, Residents, Errands, Seats, NavMeshSystem, Multiplayer, Persistence]
updated: 2026-10-04
---

# Colony residents

Fourteen astronauts live, work and sleep in the Mars colony ([ColonyInterior.md](ColonyInterior.md)) and go out on EVA; three rovers drive loops round it. Nothing here is colony-only code in the resident pipeline: the colony is a `Settlement` with a culture ([Residents.md](Residents.md)) and differs in data and in three generic features it is the first to use: a gated off-mesh link (an airlock), in-scene beds and building-local ambles. Exterior rovers are a separate, stateless thing.

## Model
| Piece | What it is |
|---|---|
| Passage | `AirlockPassage` (child `Passage` of both airlock prefabs, with its `OutsideStand` / `InsideStand`): the off-mesh link a colonist crosses by, and its gate. Every building is its own NavMesh island and the shut hatch leaves are baked as walls, so this link is the only way between outside and a room (see [Colonists crossing an airlock](#colonists-crossing-an-airlock)) |
| Colonists | 14 residents of one `Settlement` (`AstronautSettlement.asset` + `ColonyCulture.asset`): ten archetypes, a post for each on an `AstroDeco_*` prop, a bed in a bunk each, lines in `ColonyLines.txt` (see [Colonists](#colonists)) |
| Exterior | `Prefabs/Environment/Decorations/Astronaut/Exterior/`: `AstroExt_RoverService` (jack stands, parts table, diagnostic cart, work station), `AstroExt_SensorMast` (a `Deco_Solar_Pole` x1.8 and a console), `AstroExt_SolarArray` (`Deco_Solar_Array` x1.6), `AstroExt_DrillSite` (`Deco_DrillRig` x1.5 and the core rack): nested props, composed in the Editor from existing art (no new Blender model), listed in `AstronautSettlement.asset.decorations`. Two carry a `SettlementHeart` |
| Rovers | `Prefabs/Vehicles/ColonyRover.prefab`: `AstroDeco_DisplayRover`'s renderers at 0.5 scale (4.2 x 3.3 x 6.2 m), its six wheels each under a spin pivot, a kinematic `Rigidbody`, a box collider and a `RoverPatrol`. Three in the settlement's decorations (see [Rovers](#rovers)) |

## Key types
| Type | Role |
|---|---|
| [`AirlockPassage`](Assets/Game/Scripts/World/Colony/AirlockPassage.cs) | The link and its gate (`INavLinkGate`, `IAirlockPort`): registers `NavMesh.AddLink` with retries as a ladder does (owner = itself), walks colonists through the hatches by the waypoints below, works the real hatches through `AirlockChamber.ServerOperate`, hops an unwatched colonist whose turn never comes. Server only |
| [`AirlockTransit`](Assets/Game/Scripts/World/Colony/AirlockTransit.cs) | The crossing's decisions, pure and unit-tested (`AirlockTransitTests`): who crosses together, which stage, what each traveller does |
| [`INavLinkGate`](Assets/Game/Scripts/Gameplay/Traversal/INavLinkGate.cs) / `NavLinkGates` | The fourth link kind beside ladder, plain and jump: `NavMeshAgentMotor` hands a traveller standing at a link whose owner is a gate to `Direct` each tick (wait, walk to a point, done). `NavLinkGates` lists the live gates so the planner can charge their `TransitSeconds` |
| [`RoverPatrol`](Assets/Game/Scripts/Vehicles/Rover/Patrol/RoverPatrol.cs) / `RoverRoute` | A rover's loop and where it is on it, both pure functions of the shared clock (below) |

## Flows
### Colonists crossing an airlock
A colonist's path runs over the passage's link like any other. At the link's start the motor stops it and asks the gate each tick (`AirlockTransit`, one group at a time, the first traveller waits `groupWindowSeconds` for company):
1. **Admit**: the near hatch is requested open through `ServerOperate` (the same rules as a click: a running cycle or an occupied doorway refuses it and the request is repeated). 2. **Board**: the group walks to the near doorway and the chamber's middle (waypoints 0 and 1, at the room's floor height, from the zone centres). 3. **Seal**: the near hatch is shut. 4. **Cycle**: the far hatch is requested (the chamber vents or pressurises, mist and beacon as for a player). 5. **Unload**: the group walks through the far doorway and lands on the link's far end (waypoints 2 and 3). 6. **Settle**: whichever hatch is still open is shut, then the next group may start.
Every stage is decided from the state the airlock reports, never from memory of what was asked, so a player who shuts a hatch mid-crossing only sends the stage back a step (Board back to Admit, Unload back to Cycle); a hatch is never shut on someone standing in its doorway because the cycle's own interlock refuses (`PresenceZone` counts colonists). A colonist that has not stepped in after `hopAfterSeconds` and that no player can see hops across (`NetworkedTeleport.Move`); while anyone sees it, it keeps waiting. `TravelMinutes` charges each crossing `AirlockPassage.transitSeconds` (20 s) at walking speed, and the link's `costModifier` (4) keeps paths out of buildings for no reason.

### Rovers
`RoverPatrol` has no state: where a rover is is a function. Its loop is built once, on every machine, from the settlement's seed and the rover's `SceneryId`: control points proposed on a ring `ringMargin` (8-60 m) beyond the outermost building (`SettlementPlaces.BuildingBounds`), each kept only where the terrain is under 18 degrees, nothing but terrain stands within 4 m, and the way from the last point is clear (a ray down for terrain, a sphere for props, every 6 m along the leg), joined by a Catmull-Rom spline (`RoverRoute`, arc-length parameterised). Where it is on the loop is `StormClock.Shared` (server time online, game time offline) times `cruiseSpeed` (2.5 m/s), plus a seeded phase and direction; its pose comes from four ground probes under the hull, moved by a kinematic body in `FixedUpdate`, wheels spun in `Update`. A loop that cannot be built is retried every 5 s for a minute (a neighbour chunk may still be streaming) and then logs once and stays parked.

### Colonists
The colony is a resident settlement exactly as the nomads' is ([Residents.md](Residents.md)): the same `Settlement` + `SettlementConfig` + `SettlementCulture`, planner, routine and errands. It differs only in data and in three generic features of the shared system that it is the first to use: gated links (above), in-scene beds and building-local ambles.
- **Culture and archetypes.** `ColonyCulture.asset`: names, `ColonyLines.txt`, ten archetypes in `Archetypes/Colony/` (Engineer: `LifeSupport`; Operator: `Terminal`; Geologist: `RockAnalysis` + the `CarrySamples` chore; RoverTech: `RoverBay`, indoors and out; Botanist: `Garden` + the nomads' `Water` chore; Chef: `Galley`; Medic: `MedBay`; Surveyor: out on `Survey` trips; EvaGuard: `Patrol` duty; Crew: roams), the `Astronaut` prefab mapped to all of them, `freeTimeReachMinutes` 6 and `ambleUse` `Wander`. Colonists work with empty hands (the astronaut's hand is 1.7 times a human's and tool grips were only fitted to the Raxy), so every colony use holds a bare-handed loop (`wipe`, `operate`, `rummage`, `tend`); the galley and the med bay are their own uses (`Galley`, `MedBay`) because the nomad `Kitchen` and `Infirmary` hold loops that need a ladle or a mortar.
- **Spots are on the props.** `ResidentErrandContentBuilder.Colony` puts `SettlementSpot`s on 27 `AstroDeco_*` props (a job post in front of the prop's box, 0.9 m off it, two on a wide one; a stand-up spot at every `Seat`; two bed spots per bunk), so all seven buildings inherit them. A building also gets a `Dwelling` whose beds are its bed spots, a `Hall` (`SettlementEntrance`) at the room-side stand of its first airlock (where an idle resident waits; there is no door to go through to bed), and a `Wander` spot at the middle of every room, hub and tube.
- **Beds.** A bunk's two berths are `Seat`s with pose `Lie` (the sleeper's root on the mattress, the `sleep` loop held) and a bed spot each (use `Bunk`, `sleeps`): `PlaceKind.Bed`. Each resident of a dwelling takes one in roster order; `DayPlanner` sleeps it there (`PlannerResident.bedPlusOne`), nobody is ever planned onto a bed awake, and a resident whose bed is unusable falls back to going indoors at its hall. Nobody goes offstage in the colony. The top berth is 3.6 m up: the sleeper is put there only while no player can see it (`ResidentSeating`, `elevatedSeatRise`).
- **Local ambles.** An amble in a building that has `Wander` spots walks between that building's seats and wander spots, never to a point in the street, so a stroll never crosses an airlock. The planner keeps free time within `freeTimeReachMinutes` of where the resident is (the airlock's transit minutes make another building a deliberate trip).
- **Outside.** Rover bay work, the sensor mast's console, the solar array, the drill site and its core rack are the same posts and chore stops on exterior props; Surveyors go on `Survey` trips on the trip ring (`bored` look-about loop, no prop); EVA guards walk the perimeter ring. Every exit and return goes through a passage.
- **Not authored.** No sensor, solar or drill model of its own (the exterior stations reuse the nomad `Deco_Solar_*` and `Deco_DrillRig` art), no tool grips for the astronaut hand, no treadmill, window or projector circles (their spots do not exist), no `CleanPanels` chore.

## Multiplayer
- **A colonist's crossing is server-side and rides the airlock's own wire.** The gate, the planner and the routine run where the agent is simulated; the hatches, mist and beacon reach every client through `AirlockState` (125) on the settlement wrapper, a colonist's body through its transform sync, the lying pose through `ResidentPresence` (activity, held place, seat id). Nothing new is sent. A player clicking a hatch mid-crossing gets the ordinary refusals.
- **Rovers are scenery on every machine.** No `NetworkObject`, no message, no registration: two machines place a rover alike because they evaluate the same function of the same clock against the same chunk's colliders. A machine whose neighbouring chunk had not streamed in when the loop was built may draw a slightly different one (visual only).

## Persistence
| State | Where |
|---|---|
| Colonists | The existing `ResidentSaveable` / `AgentGoalSaveable` / `EntityEquipmentSaveable`. Plans, bed claims, gate state and a seat claim are re-derived. **A colonist saved mid-crossing loads inside the chamber's island**: the routine's settle (`CutOff`: on the mesh with no path to its place) puts it at its place while no player can see it. Regenerating the colony makes new people (their identity derives from scene path), so older records are orphaned |
| Rovers | **Nothing.** A rover resumes the loop wherever the clock says it is, which no player can tell from one that never stopped |

**Verified 2026-10-04, host only, in a live Play session (the colony generated and baked, Chunk_5_3):** 14 residents at their posts, on ambles and on a trip; a resident sent from outside to inside `Colony_Large` cycled the airlock (outer hatch vented and opened, walked to the chamber, outer shut, inner opened, walked in, both shut) and went on; a group of four was boarding another airlock for one cycle at the same time; all 12 links were valid. Two colony baseline runs (`ResidentsBaseline` pointed at `Chunk_5_3`) were stopped by someone leaving play mode after 2-3 game hours: 0 errors, 14 residents, 209 of 234 places usable, sleepers at their beds (Sleep segments reached 75-79 %), ambles and errands running; **a baseline day is not a fair test of a spread-out settlement at 600 s/day** (a crossing and a 150 m walk are hours of game time there; at 1200-2400 s plans fill out). Three rovers were seen driving their loops, nose first, at 2.5 m/s. **Not run:** a full colony day, the nomad baseline for regression, a client, a save/reload.

## Gotchas
- **A script-built off-mesh link added before the mesh exists attaches to nothing, and a `NavMeshLink` component is the same.** `AirlockPassage` registers its link with `NavMesh.AddLink` in a retry coroutine (as `Ladder` does) and is the link's owner, which is how the motor knows the gate. In edit mode nothing registers it (no `OnEnable`), so `SettlementNavMeshAudit` reports the whole interior "unreachable" until the tool run calls `AirlockPassage.TryAttachLink` on each passage with the world mesh on, as `ColonyStandSnapper` does.
- **The airlock prefabs save their `Passage` and every instance of them in an open scene is refreshed, dropping the instance's position.** `ColonyAirlockPassageAuthoring` measures in one preview scene (buildings 500 m apart: they are all authored around the origin, and a ray down one hits the next), writes, then verifies in a second, new one.
- **A prefab-instance Settlement loses what Generate sets on it unless it is recorded as an override.** `walkableHeart` (a private field) was zero after the first scene reload, so `RefreshStands` never measured a stand and the planner had nothing. `SettlementEditor.Generate` now calls `PrefabUtility.RecordPrefabInstancePropertyModifications` on the settlement.
- **Generate's own guess for the walkable heart is a roof.** It takes the biggest group of places that can walk to one another, over the throwaway mesh, which has no airlock links: the biggest is a building's roof or interior. A prop that stands on open ground carries a `SettlementHeart` marker (the rover service and the sensor mast do) and Generate takes that. Without one the colony's places are all "an island the settlement cannot walk to".
- **A building's bounds must skip particle systems.** `SettlementPlaces.BuildingBounds` (the perimeter ring, the rover loop) read every renderer: an airlock's mist system has bounds at the world origin until it has run, which made the colony a kilometre across, the guards' ring 1,400 m wide and every rover's loop impossible.
- **Characters are only placed where the heart can walk to.** `Generate` placed residents anywhere on the throwaway mesh, roofs included, and a colonist put on a roof stands there forever (its place is directly below, so a flat distance calls it "arrived"). Generate now rejects a spawn point with no path from the heart, and the routine's settle puts a body that is on the mesh but cut off from its place back at it while no player sees (`ResidentRoutine.CutOff`).
- **Decorations only keep a point radius clear.** Generate keeps `decorationSpacing / 2` from a solid, whatever the prop's size, so a 12 m rover pad can land on a stair ramp and cut the airlock off. `AstronautSettlement.asset` sets `decorationSpacing` 24 (12 m clear round every decoration) and `outskirts` 40.
- **A prop's "front" is only the room side for wall-backed props.** Free-standing furniture (table, chairs, benches) is turned any way by the planner, so a stand 0.9 m "in front" can land in the wall or under the table; the baked NavMesh keeps 0.5 m off every solid and the rooms are packed. `ColonyStandSnapper` moves each spot that is off the mesh or cut off to the nearest point on rings round what it looks at (about 56 moved per run, written into the building prefabs): 190 of 215 places are usable; what is left are window benches and a table nook walled in by furniture. Re-run it after re-laying a building.
- **A `Lie` seat's `footDrop` is 0** (the body's root rests on the mattress), and a Lie seat is not offered to a player (`Seat.CanInteract`).
- **Rover loop probes read terrain, not "ground".** A ray that hits a rock first is an obstacle, not a surface; the first terrain collider under the point is the ground. The prefab keeps its own serialized `ringMargin` and `alternatives`: changing the script's defaults does not change `ColonyRover.prefab`.

## Extending
- **A new colonist role:** a `ResidentArchetype` in `Archetypes/Colony/` with a post (a Work `SpotUse`, a bare-handed `holdCue` registered in `StationAuthoring.Spots`) and spots on a prop (`ColonyJobs` in `ResidentErrandContentBuilder.Colony`), added to `ColonyCulture`'s archetypes and to `ColonyLines.txt`. Run **Tools/SpaceGame/Colony/Author Colony Residents**, Generate, **Snap Spots To NavMesh**, Generate + Bake World NavMesh, then **Tools/SpaceGame/Residents/Write Station Table** (`StationTableTests` fails until the table is current).
- **Re-laying a building or moving an airlock:** run **Tools/SpaceGame/Colony/Place Airlock Passages** (it measures all 13 and fails loudly if one disagrees with the prefab), then Generate + Bake World NavMesh and **Snap Spots To NavMesh**.
