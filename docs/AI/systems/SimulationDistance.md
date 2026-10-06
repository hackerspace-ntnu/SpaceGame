---
system: SimulationDistance
layer: characters
summary: NPCs and animals far from every player freeze in place; cells, exemptions and the dormant parking reason
paths:
  - Assets/Game/Scripts/agents/Simulation/SimulationRules.cs
  - Assets/Game/Scripts/agents/Simulation/DistanceDormant.cs
  - Assets/Game/Scripts/agents/Simulation/SimulationRange.cs
  - Assets/Game/Editor/Agents/DistanceDormancyWiring.cs
  - Assets/Game/Editor/Tests/SimulationRulesTests.cs
  - Assets/Game/Editor/Tests/AgentParkingTests.cs
  - Assets/Game/Editor/Tests/DistanceDormantTests.cs
  - Assets/Game/Editor/Tests/SimulationRangeTests.cs
  - Assets/Game/Editor/Tests/DistanceDormancyPrefabTests.cs
symptoms:
  - "NPCs and creatures fight each other far away where no player is"
  - "an NPC far away stands frozen in place and does nothing"
  - "a villager stands still until I walk within about 250 m"
  - "a creature I shot from very far away does not react"
  - "the sky city or a Strider house stopped moving in the distance"
  - "a pack hunting me stops chasing when I run far enough"
  - "Failed to create agent because it is not close enough to the NavMesh logged once per sleeping NPC when Play Mode stops"
  - "my mount stops dead when I ride it into a cave"
  - "a corpse jitters or stands up after I kill a far-off NPC"
  - "Failed to create agent because it is not close enough to the NavMesh when I fly near the sky city"
reads_with: [AgentSystem, EntitySystem, WorldStreaming, Residents]
updated: 2026-10-06
---

# Simulation Distance

People and animals far from every player are parked where they stand: still visible, doing nothing. Machines and travelling groups never sleep. Design: [the spec](docs/superpowers/specs/2026-10-06-simulation-distance-design.md).

**Scope:** [agents/Simulation/](Assets/Game/Scripts/agents/Simulation/), [DistanceDormancyWiring.cs](Assets/Game/Editor/Agents/DistanceDormancyWiring.cs), [IAirborneCarrier.cs](Assets/Game/Scripts/agents/entity/IAirborneCarrier.cs)
**Related:** [AgentSystem.md](AgentSystem.md) (`Offstage`, modules) · [EntitySystem.md](EntitySystem.md) · [WorldStreaming.md](WorldStreaming.md) (`ChunkGrid`) · [Residents.md](Residents.md)

## Model

- **Cells.** `ChunkGrid` maths with the streaming origin and a serialized `cellSize` (125 m). Pure, unbounded, never loaded or unloaded. Horizontal (XZ) distances, like `NpcWorldSim.NearestPlayerDistance`.
- **Wake / sleep.** A cell wakes when its rectangle comes within `NpcWorldSim.SpawnRadius` of any wake source and sleeps once all of it is beyond `DespawnRadius`; between the two an awake cell stays awake (hysteresis). Radii are read from `NpcWorldSim`, never copied. The world scene has 250 / 360 (code default for despawn is 350).
- **Wake sources.** Every body from `SessionPlayers.Collect`, at its real position. A player inside an interior counts **twice**: where they stand (so a mount ridden in with them keeps simulating) and at the visit's `ReturnPosition` (`InteriorManager`, so the settlement stays awake while its houses are visited). Interiors load at world origin.
- **Subjects (opt-in).** Only agents carrying `DistanceDormant`. Vehicles, the sky fleet, Strider houses and barges never carry it. Prefab folders (case-insensitive) that must carry it: `/prefabs/agents/characters/`, `/robots/`, `/creatures/` and `/caravan/` (caravan prefabs are variants of in-scope people and animals and inherit the marker). `Prefabs/Spikes/` is ignored.
- **Dormant agent.** `AgentController` parked: no module ticks, motor `ForceStop` + `SuspendSelfDrive`, `AgentTargeting` and `PerceptionModule` skip `Update`, animator idles. Body, collider, faction registration, health and held target untouched.
- **Exemptions.** A subject in a sleeping cell still simulates when:

| Exemption | Why |
| --- | --- |
| `GroupMembership.Group != null` (caravan, war party, Strider city, expedition band, sky wing) | `NpcWorldSim` already folds groups at these distances; war parties stage beyond `spawnRadius` and march in; the city's crabs trail ~90 m behind the lead house |
| Seated in an `IAirborneCarrier` (`AirborneSeat.IsSeatedAloft`) | A pilot frozen mid-flight hangs in the air; flying pilots are withheld from the save |
| `NpcFlightModule` `InFlight \|\| IsAirborne \|\| OnSortie` | A resident off the ground waits `fallDeploySeconds` before deploying; a sortie removes itself on its own timer (`sortieLifetime`) |
| `AgentTargeting.Target` is a player (found by walking up parents, so a seated player counts) | Events aimed at players still happen; a pack chasing a player who outruns 360 m keeps coming. Only NPC-vs-NPC fights far away freeze |
| Hurt within `woundedWakeSeconds` (30 s) | A sniped NPC fights back or flees. Its alert does **not** wake allies in sleeping cells |

**Left as it is** (neither woken nor put to sleep) while `AgentController.enabled` is false or `RidesAsPassenger` is true; `SimulationRange.Tick` skips the subject:

| Held by | Why |
| --- | --- |
| Controller disabled: death (`HealthReactionModule.ApplyDeadState`, `disableAgentOnDeath`) or a ragdoll (`AgentRagdoll.SuspendLayers`) | Waking a dormant body would call `ResumeSelfDrive` and switch its `NavMeshAgent` back on under the ragdoll. Corpses, `Remains` and `LootAwaitingGround` run on their own components anyway |
| `RidesAsPassenger` (seated by `NpcSeating`: sky-city deck residents, a Clanker on its outrider horse) | `NpcSeating.Suppress` records only the behaviours that are enabled, so a resident dormant at deck departure has its `NavMeshAgent` unrecorded; waking it mid-voyage would resume it off the mesh under the moving hull |

A passenger therefore keeps the state it had when seated, for the whole ride.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `SimulationRules`, `SimulationCells`, `DormancyInputs` | [SimulationRules.cs](Assets/Game/Scripts/agents/Simulation/SimulationRules.cs) | Pure maths: `CellOf` (unclamped), `DistanceToCell`, `Advance` (next awake set), `ShouldSleep`. No scene access |
| `SimulationRange` | [SimulationRange.cs](Assets/Game/Scripts/agents/Simulation/SimulationRange.cs) | On the NpcWorldSim GameObject in `persistentScene.unity` (`[RequireComponent(typeof(NpcWorldSim))]`). Server-or-offline. Public `InteriorReturnPosition` (`Func<Transform, Vector3?>`, the visit's door or null; a test seam, public because `Assembly-CSharp-Editor` has no `InternalsVisibleTo` into `Assembly-CSharp`); public `Tick(players, now)`; `origin` read once in `Start` from `WorldStreamer.Config.worldOrigin` (zero with no streamer) |
| `DistanceDormant` | [DistanceDormant.cs](Assets/Game/Scripts/agents/Simulation/DistanceDormant.cs) | Opt-in marker. Static registry `All`; stamps last-hurt time from `HealthComponent.OnDamage`; exposes exemption inputs |
| `AgentController.Dormant` / `IsParked` | [AgentController.cs](Assets/Game/Scripts/agents/controller/AgentController.cs) | `IsParked = Offstage \|\| Dormant`. Motor parks when it first becomes true, unparks only when both clear. `Simulate` returns early on it; `AgentTargeting` and `PerceptionModule` read it |
| `DistanceDormancyWiring` | [DistanceDormancyWiring.cs](Assets/Game/Editor/Agents/DistanceDormancyWiring.cs) | `Ensure(root)`, `WirePrefab(path)`, `PrefabPathsInOrder()` (bases before variants). Menu `Tools/SpaceGame/Agents/Wire Distance Dormancy` |
| `IAirborneCarrier` / `AirborneSeat` | [IAirborneCarrier.cs](Assets/Game/Scripts/agents/entity/IAirborneCarrier.cs) | `AirborneSeat.IsSeatedAloft`, shared with `EntityLootTable` |

`NpcFlightModule.IsAirborne` was added for the flying exemption.

## Flows

Server, every `tickInterval` (0.5 s, serialized), allocation-free:
1. Collect wake sources via `SessionPlayers.Collect`: each player's position, plus its `InteriorReturnPosition` when inside an interior.
2. `SimulationRules.Advance(awake, sources, cells, scratch)` updates the awake cell set.
3. For each registered `DistanceDormant`: skip it while its controller is disabled or it `RidesAsPassenger`. Otherwise cell = floor((position - origin) / cellSize), **unclamped**, and `Dormant = ShouldSleep(subject.Read(cellAwake, players, now), woundedWakeSeconds)`, i.e. cell asleep and no exemption.

## Multiplayer

Server-only decision, nothing replicated, no prefab to register (no runtime spawns). Clients run only `IPresentationModule`s on agents already, so a dormant agent is a body whose replicated transform stops changing. `SimulationRange` does nothing on a client.

## Persistence

Holds no state worth persisting; re-derived on the first tick after a load. The last-hurt time is not saved, so a load wakes nobody. Save JSON gains no keys.

## Gotchas

- **Never use `ChunkGrid.ToCoord` for cells.** It clamps to the world edge and would put an off-grid agent (the arena, 16.5 km east) into the corner cell. Use `SimulationRules.CellOf`.
- **`Offstage` and `Dormant` are two reasons.** Anything asking "is it acting?" reads `IsParked`, never `Offstage` alone. Each writer owns its flag and neither can release the other's park.
- **A machine must never carry `DistanceDormant`**; `DistanceDormancyPrefabTests` guards the folders. A new agent prefab outside the four folders is never put to sleep, silently.
- **`NavMeshAgentMotor`'s suspend is one latch, not a count.** `SuspendSelfDrive`/`ResumeSelfDrive` are shared by dormancy, the ragdoll, the lasso and the seating, and the motor does not count holders: a second suspend is a no-op and the first resume releases everybody. That is why `Tick` leaves `Dormant` untouched on a disabled controller or a passenger: unparking there would switch the `NavMeshAgent` on under somebody else's hold (a corpse that jitters or stands up; "not close enough to the NavMesh" under the sky city). The converse is not guarded: a dormant body that recovers from a knockdown gets `ResumeSelfDrive` from `AgentRagdoll.RestoreLayers` while still `Dormant`, which is harmless because `Simulate` returns early on `IsParked`.
- **An interior's wake source must not replace the player's own.** The player stands at world-origin coordinates inside; counting only the door froze a mount ridden in with them (`SteerModule` never ticks on a parked agent).
- **Group members are exempt on purpose**: war parties stage beyond `spawnRadius` and must still act.
- **Only the marker itself switched off releases its agent.** `OnDisable` unparks only when `enabled` is false. A body being destroyed or deactivated runs `OnDisable` with `enabled` still true and stays parked: unparking re-enables its `NavMeshAgent`, and on a Play Mode stop Netcode destroys the sleepers (`ModeChanged` → `ShutdownInternal` → `DespawnAndDestroyNetworkObjects`) after the NavMesh is gone, logging one "not close enough to the NavMesh" each. A reactivated body re-registers and the next `SimulationRange` tick re-derives `Dormant`.
- **Test registry leak.** `DistanceDormant.All` is static and EditMode `DestroyImmediate` does not run `OnDisable`. Reset it (reflection on the private `ResetStatics`) in SetUp and TearDown or leftovers throw `MissingReferenceException` in the next fixture.
- **Batch prefab wiring.** When running `WirePrefab` over many prefabs from a script, do about 15 per editor call; the shared editor ran out of memory on large single calls.
- **Rebuilds keep the marker only through the builders.** ClankerStack, NomadPrefabBuilder, SculptCharacterBuilder, RobotHorseBuilder, StriderCrabOutriderBuilder, StriderElderBuilder, AppaHerdAuthoring and DuneRatPennedBuilder call `DistanceDormancyWiring.Ensure(root)`.

## Extending

- **A new player-facing event** (raptor pack, hunting party) stays live through an existing exemption, never a new switch: spawn it as an `NpcWorldSim` group (members carry `GroupMembership`), or give its members a player target (`AgentTargeting.ForceTarget`, `ProvocationModule.Provoke`) as they set off.
- **A new NPC or animal prefab** goes in one of the four subject folders and its builder calls `DistanceDormancyWiring.Ensure`.
- **A new exemption** is a field on `DormancyInputs` plus a clause in `ShouldSleep`, filled in `DistanceDormant`; add a case to `SimulationRulesTests`.
