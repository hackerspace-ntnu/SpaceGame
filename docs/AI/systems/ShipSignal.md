---
system: ShipSignal
layer: items
summary: "The working transmitter's intercepted call: the nearest fixed, friendly settlement, charted and walked to"
paths:
  - Assets/Game/Scripts/Gameplay/Signal/
  - Assets/Game/Scripts/Core/Persistence/Adapters/ShipSignalSaveable.cs
  - Assets/Game/Scripts/Gameplay/Objectives/Steps/FitTransmitterStep.cs
  - Assets/Game/Scripts/Gameplay/Objectives/Steps/AnswerSignalStep.cs
  - Assets/Game/ScriptableObjects/Objectives/Steps/07_FitTransmitter.asset
  - Assets/Game/ScriptableObjects/Objectives/Steps/08_AnswerSignal.asset
  - Assets/Game/Editor/Tests/ShipSignalTests.cs
symptoms:
  - "the transmitter is fitted but the COMMS tab never appears"
  - "the COMMS page says SCANNING BANDS forever"
  - "the signal leads to the Clanker town, a Strider city or the Sky City"
  - "the signal leads to the settlement the ship landed next to"
  - "the signal's destination changed after a reload, or differs between host and client"
  - "the objective is stuck on Answer the signal with no waypoint"
  - "No baked site catalog on the world's streaming config, so the transmitter has no towns to hear"
  - "Unity says the type ShipSignal could not be found but tools/typecheck.py passes"
reads_with: [ShipTransmitterFire, Terminal, Objectives, PlayerShip, Expeditions, AgentSystem]
updated: 2026-10-06
---

# Ship signal

The moment a working long-range transmitter is in the lander's socket, the ship hears a voice: a
looped call on the open band from the nearest settlement that will have the crew. The terminal's
COMMS page prints it with a bearing and range, the map table charts it, and the objective
"Answer the signal" walks the crew there. Before the transmitter works the terminal is deaf: only
the hull drawing is alive ([Terminal.md](Terminal.md)).

**Scope:** [`Gameplay/Signal/`](Assets/Game/Scripts/Gameplay/Signal) (the choice, its state, its map
marker), its saver, and the two objective steps. The broken unit and its fire are
[ShipTransmitterFire.md](ShipTransmitterFire.md); the baked town list is [Expeditions.md](Expeditions.md)'s
catalog; the `wandering` faction flag is [AgentSystem.md](AgentSystem.md)'s.

## Model

- **"Working" is the rack's fitted mask.** `ShipSignal.TransmitterWorking` / `ShipTelemetry.TransmitterOnline`
  ask whether a `ShipPartKind.Transmitter` socket is in `ShipPartRack.InstalledMask`. The burnt-out unit the
  hull lands with is the separate broken mask, so it never counts.
- **The rule** ([`SignalDestinationRule`](Assets/Game/Scripts/Gameplay/Signal/SignalDestinationRule.cs), pure):
  of the world's baked towns (`WorldSiteCatalog.towns`), the one nearest the hull's position, measured flat to
  the town's CENTRE, that (1) has a faction, (2) whose faction is not `FactionDefinition.wandering` (Striders,
  Sky Tribe), (3) that `GlobalRelationships` does not call hostile to the crew's faction (Humans; rows, then
  default stance, so the Clankers), and (4) whose centre is at least `minimumDistance` (300 m, on the prefab)
  from the hull. Ties go to the lower id, ordinal.
- **Chosen once, on the server, from where the hull actually lies**, never from a spawn point: the first
  frame the server sees a working transmitter after `ArrivalDirector.CrewHasLanded`. Then saved and
  replicated, so it never changes for that world.
- **The state is the place, not just the id** ([`SignalDestination`](Assets/Game/Scripts/Gameplay/Signal/SignalDestination.cs)):
  received flag, town id, origin (faction name), position, radius. A re-bake of the catalog never moves a
  destination a world already has. `Nowhere` = heard, nothing qualified.
- **Which towns exist** is the bake: every `Settlement` (faction = its `SettlementPopulation.Owner`, else the
  most common `EntityFaction` of its character prefabs) and every stand-alone `SettlementPopulation`.
  Radius = `Settlement.GeneratedExtent`, or the population's `CountRadius`. Mobile cities are not in chunk
  scenes and are not baked; the `wandering` flag keeps a future placed one out too.

**What it picks (main world, 2026-10-06 bake):** towns are the nomad `Settlement` (Drifters, (2999, 618),
extent 221 m), `astronauts_settlement`, the Mars colony (Humans, (2786, 534), 139 m), `outpost (1)` (Drifters,
(2381, 896), 16 m) and `ClankerSettlement` (Clankers, hostile, (3615, 796)). From the crash site at
(2610, 880) the outpost is 230 m off (skipped), and the signal leads to **the Mars colony, 388 m at
bearing 153° (SE)**; the nomads are 469 m. The ship lands within 60 m of the spawn, which cannot change
that. (From the old crash site, (2974, 571), it led to `outpost (1)`, 676 m at 299°.)

## Key types

| Type | Role |
| --- | --- |
| `ShipSignal` | `NetworkBehaviour` on the hull ROOT beside `ShipPartRack`. Serialized `crewFaction`, `relationships`, `minimumDistance`, `transcript`, `mapLabel`. Server `Listen()`; `Restore`; `Destination`, `Transcript`, `Changed`; charts a map POI on every machine |
| `SignalDestination` | `INetworkSerializable` value: `Received`, `HasDestination`, `Id`, `Origin`, `Position`, `Radius`; `To(...)`, `Nowhere` |
| `SignalDestinationRule` | `TryChoose`, `Welcomes`, `FlatDistance`, `Bearing` (clockwise from +Z) |
| `ShipSignalSaveable` | Key `shipSignal`: `{ received, id, origin, x, y, z, radius }`; null until heard. Hand-placed on the prefab |
| `FitTransmitterStep` | `fit-transmitter`: status by socket state and fire phase; waypoint the socket while broken, the transmitter on the dish (the one tower marker), then loose or home; two remarks (tower, hook); met by a working one fitted. See [SatelliteDish.md](SatelliteDish.md) |
| `AnswerSignalStep` | `answer-signal`: begins once `Received`; waypoint + beacon on the destination; met when a crew member is within `Radius + arrivalMargin` (15 m); met at once for `Nowhere` |

## Flows

1. **Fit.** The working `DishTransmitter` goes into `Part_Transmitter_A` (bit 11) → rack mask replicates.
2. **Every machine, at once:** the terminal's static lifts, the COMMS tab appears (derived from the mask).
3. **Server, same frame:** `ShipSignal.Update` → `Listen` → `TryChoose` over the streamer config's catalog →
   `Set` → `NetworkVariable` → every peer `Adopt` → `Changed`; each machine registers POI `signal:<id>`
   (`MapMarkerType.Quest`, label `SIGNAL`, always visible) with `MapService`, which both the map table and
   the personal map draw. COMMS shows the transcript, origin, bearing and range from the hull.
4. **Objective:** `fit-transmitter` is met → `answer-signal` begins (signal already received) → briefing,
   visor waypoint, beacon → met on arrival → `repair-ship`.

## Multiplayer

| Path | Carrier | Authority |
| --- | --- | --- |
| Transmitter working | `ShipPartRack` masks | Server (install path) |
| Destination | `ShipSignal`'s `NetworkVariable<SignalDestination>` under the hull's NetworkObject; a joiner reads it in `OnNetworkSpawn` | Server chooses; peers adopt |
| Terminal pages, COMMS text | none, derived from the two above | — |
| Map marker | none, registered locally on each machine from the replicated value | — |
| Step progress | `ObjectiveNetwork` | Server; arrival judged from replicated player positions |

## Persistence

| State | Saver | Key |
| --- | --- | --- |
| Signal heard + destination | `ShipSignalSaveable` on the hull root (collected by its `SaveableEntity`) | `shipSignal` |
| Map marker | rebuilt from the above; `MapSaveable` may also keep the POI under the same id | `map` |
| Step | `ObjectiveSaveable` (step ids `fit-transmitter`, `answer-signal`) | `objectives` |

**Old saves:** no `shipSignal` record → unheard; a world with a working transmitter already fitted chooses on
the first server frame after load, from where its hull lies. A save at `repair-ship` resolves past both new
steps and stays there (by id; no regression), but its COMMS page and map marker still work. A save at
`first-module` or `first-artifact` walks into the new steps; `fit-transmitter` is met at once if the
transmitter is already in.

## Gotchas

- **A new `.cs` may be left out of Unity's compile while `tools/typecheck.py` passes.** On 2026-10-06
  `ShipSignal.cs` and `ShipSignalSaveable.cs` were missing from `CompilationPipeline.GetAssemblies()`'s
  `sourceFiles` (other new files beside them were in), so Unity failed with `ShipSignal` not found and blocked
  every session. Force-reimport did not help; `AssetDatabase.MoveAsset` to a temporary name and back did. Check
  `sourceFiles` before blaming the code.
- **Distance is to the town's centre, not its edge.** Measured to the edge, the moved crash site leaves NO
  town past 300 m (colony edge 249 m, nomad edge 248 m) and the signal leads nowhere.
- **The choice is final per world.** Retuning `minimumDistance` or re-baking changes worlds that have not
  heard the signal yet, never one that has: delete the `shipSignal` record to re-choose.
- **No catalog is not "nowhere".** Without a baked catalog `Listen` logs an error once and keeps retrying, so
  the step waits instead of saving a dead signal. Run **Tools ▸ SpaceGame ▸ World ▸ Bake Site Catalog**.
- **The transcript must not name a people or a place**: which town answers is decided per world, so the call
  is generic and the COMMS page adds the origin line from the faction.
- **A town is only as known as its bake.** A settlement placed or moved in a chunk scene is invisible to the
  signal until the catalog is re-baked.

## Extending

- **Tune** `minimumDistance`, the transcript and the map label on `PlayerShip.prefab`'s `ShipSignal`;
  `arrivalMargin` and the words on `08_AnswerSignal.asset`.
- **Another faction kept off the signal:** set `wandering` on its `FactionDefinition`, or make it hostile to
  Humans in `GlobalRelationships`.
- **Mark more towns on the map** (other friendly fixed settlements in range): iterate the catalog in
  `ShipSignal.Chart` with `SignalDestinationRule.Welcomes`, `MapMarkerType.Friendly`. Not built.
- **Verify** on a client (COMMS text, tab and marker on the second machine) and after a reload
  (`shipSignal` in the save JSON, same destination).
