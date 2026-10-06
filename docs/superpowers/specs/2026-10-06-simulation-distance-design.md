# Simulation distance for NPCs and animals — design

Date: 2026-10-06 · Status: approved in conversation, awaiting spec review

## Intent

NPCs and animals far from every player stop doing anything, so tribes, Clanker towns and wildlife
no longer fight each other where nobody can see. Today every agent in the 3×3 loaded chunks around a
player (500 m chunks, `loadRadius` 1 — up to ~1 km away) runs its full stack; AgentSystem.md already
records the cost ("one save lost 13 Clankers in three minutes").

What the user asked for, verbatim decisions:

- Chunk based, on the current architecture.
- Only NPCs and animals. Machines keep moving — the sky settlement must always drift in the distance.
- Same distances as the lightweight `NpcWorldSim` records: wake at `spawnRadius` (250 m), sleep
  beyond `despawnRadius` (360 m in the world scene).
- Out of range = **frozen in place**, still visible ("just make the npcs stop doing stuff").
- Cells finer than the 500 m streaming chunk: a simulation grid on the same `ChunkGrid` maths.
- A sleeping NPC that is **hurt wakes up** for a while.
- Server decides from the union of all players; nothing replicated; nothing saved.

Success: a Clanker town ~600 m from the player loses nobody; walking toward it, it is fully alive
by 250 m; a long-range shot wakes its target; the sky city and Strider city keep moving; clients see
the same still bodies with a clean console; a save/reload adds nothing to the JSON and re-derives
every sleeper on the first tick.

## Model

- **Simulation cells.** The `ChunkGrid` maths with the streaming config's origin and a cell size
  `cellSize` (125 m, serialized), so four cells span one streaming chunk per axis. Cells are pure,
  unbounded geometry (no grid dimensions, no clamping); nothing is loaded or unloaded.
- **Awake cells.** A cell wakes when its rectangle comes within `wakeRadius` of any wake source and
  sleeps once every point of it is beyond `sleepRadius`. Per-cell hysteresis: a cell that is awake
  stays awake until it leaves `sleepRadius`. `wakeRadius`/`sleepRadius` are read from
  `NpcWorldSim.SpawnRadius`/`DespawnRadius` — never copied — so the two distance rules cannot drift.
  Distances are horizontal (XZ), like `NpcWorldSim.NearestPlayerDistance`.
- **Wake sources.** Every player body from `SessionPlayers.Collect`. A player inside an interior
  (`InteriorManager.TryGetVisit`) counts at the visit's return position, so a settlement stays awake
  while its houses are visited (interiors load at world origin).
- **Subjects.** Only agents carrying the opt-in marker `DistanceDormant`. Vehicles, the sky fleet,
  Strider houses and barges never carry it, so they can never be frozen.
- **Dormant agent** = `AgentController` parked for distance: no module ticks, motor `ForceStop` +
  `SuspendSelfDrive`, `AgentTargeting`/`PerceptionModule` skip their `Update`, animator settles to
  idle. Body, collider, faction registration, health and held target are untouched.

## Exemptions — a subject in a sleeping cell still simulates when

| Exemption | Why |
| --- | --- |
| It carries `GroupMembership` (caravan, war party, Strider city, expedition band, sky wing) | `NpcWorldSim` already folds these at the same distances, and some must act just outside 250 m: war parties stage beyond `spawnRadius` and march in; the city's crabs trail ~90 m behind its lead house |
| It is seated in an `IAirborneCarrier` | A pilot frozen mid-flight hangs in the air; flying pilots are withheld from the save (`SaveScopeHold` / `WorldSaveStore.Withhold`) |
| Its `NpcFlightModule` is in flight or falling toward deploying its craft | A Sky resident off the ground waits `fallDeploySeconds` before deploying; frozen mid-fall it never would |
| Its `AgentTargeting.Target` is a player (the target or one of its parents is a collected wake source) | Events aimed at players must still happen (user, 2026-10-06: "a pack of raptors attacking… hunting parties, wild life on attack can still happen"). A pack chasing a player who outruns 360 m keeps coming; only NPC-vs-NPC fights far away freeze |
| Its `NpcFlightModule.OnSortie` is true | A sortie lands far from players and removes itself on its own timer (`sortieLifetime`); frozen, the timer never runs and the resident is stranded |
| It is dead (`HealthComponent.Alive == false`) | Corpses, `Remains` and `LootAwaitingGround` run on their own components and must not be touched |
| It took damage within `woundedWakeSeconds` (30 s, serialized) | A sniped NPC fights back or flees instead of standing as a dummy. Its alert does **not** wake allies in sleeping cells — they stay asleep unless hurt themselves |

Other passengers (a Clanker on its outrider horse, a Sky resident parked on the flagship's deck while
under way) follow their own cell — they stand where their carrier stands, so they sleep and wake with
it.

## Components

| Unit | Kind | Responsibility |
| --- | --- | --- |
| `SimulationRules` | static, pure | Cell rectangle distance; next awake-cell set from previous set + source positions + radii; `ShouldSleep(cellAwake, exemptions)`. No Unity scene access — EditMode-testable like `WarPartyRules` |
| `SimulationRange` | MonoBehaviour, persistent scene beside `WorldStreamer` | Server-or-offline (`!Network.IsNetworked \|\| Network.Server`). Every `tickInterval` (0.5 s, serialized) collects wake sources, advances the awake set, walks the registered subjects and writes `AgentController.Dormant`. Reads `cellSize` from its own serialized field, origin from `WorldStreamingConfig`, radii from `NpcWorldSim` |
| `DistanceDormant` | MonoBehaviour marker on the agent root | Self-registers in a static set on enable (cleared via `RuntimeInitializeOnLoadMethod(SubsystemRegistration)`); listens to `HealthComponent.OnDamage` to stamp the last-hurt time; exposes the exemption inputs |
| `AgentController.Dormant` | property | Second parking reason beside `Offstage` (below) |
| `DistanceDormancyWiring` | editor static | `Ensure(root)` adds the marker; called by the agent builders and by the wiring menu item |

### `AgentController`: two reasons, one parked state

`Offstage` stays owned by the residents' routine. A new `Dormant` property is owned by
`SimulationRange`. Both setters go through one private refresh of `IsParked = offstage || dormant`:
the motor parks when `IsParked` first becomes true and unparks only when both are clear, so neither
writer can release the other's park. `Simulate` returns early on `IsParked`; `AgentTargeting` and
`PerceptionModule` read `IsParked` instead of `Offstage`. Neither writes `enabled`; neither is saved
or replicated. The watcher path (`!simulating`) is unchanged.

## Flow (server, every tick)

1. `SessionPlayers.Collect` → positions, replacing an interior occupant's position with its return
   position.
2. `SimulationRules.Advance(previousAwake, positions, cellGrid, wakeRadius, sleepRadius)` → awake set.
3. For each `DistanceDormant`: cell = floor((position − origin) / cellSize), **unclamped** — never
   `ChunkGrid.ToCoord`, which clamps to the edge (WorldStreaming Gotchas) and would put the arena
   16.5 km east into the world's corner cell. Cells are unbounded, so an off-grid agent obeys the same
   distances. `Dormant = !awake && !exempt`.

The loop allocates nothing per tick (reused lists/sets), and the walk is over subjects only.

## Prefab classification

- `Tools/SpaceGame/Agents/Wire Distance Dormancy` — idempotent pass: every prefab with an
  `AgentController` under `Prefabs/Agents/Characters/`, `Prefabs/Agents/Robots/`,
  `Prefabs/Agents/creatures/` gets `DistanceDormant`; anything under `Prefabs/Agents/Vehicles/` or
  `Prefabs/Environment/` must not have it (removed if present). Variants inherit from their base where
  the base is in scope. `Prefabs/Spikes/` is ignored.
- Every agent builder that writes a prefab in those folders calls `DistanceDormancyWiring.Ensure`, so
  a rebuild cannot drop the marker (the "my hand-added component disappeared after someone rebuilt
  the prefab" trap). `NomadPrefabBuilder`, `RosterAuthoring` and `NpcWorldSim` carry another session's
  uncommitted Task 9 edits: build on them, never over them, and message that session first.
- `DistanceDormancyPrefabTests` walks the folders and fails on a person/animal without the marker or a
  vehicle/structure with one.

## Multiplayer

Server-only decision; clients already run only `IPresentationModule`s on agents, so a dormant agent is
just a body whose replicated transform stops changing. Nothing new is sent and no prefab needs
registering (no runtime spawns). Verify on a real client with the two-process autotest.

## Persistence

No state worth persisting: dormancy is a reading of where players stand, re-derived on the first tick
after a load (the same argument as `Offstage` and `simulating`). The last-hurt time is not saved — a
load wakes nobody by it. Save JSON must show no new keys.

## Testing

EditMode:
- `SimulationRules`: cell distance at edges and corners; wake at < 250, stay awake 250–360, sleep
  beyond 360; multiple sources union; interior return position used.
- Exemptions, each in isolation.
- `AgentController`: `Offstage` and `Dormant` overlapping in every order never unpark while either holds.
- Prefab folder test.

Play Mode (after a heads-up to the other sessions sharing the editor — no Play Mode without telling):
- Clanker town ~600 m away: dormant, no deaths over several minutes; walk in → awake by 250 m.
- Shot from ~400 m → target wakes, stays awake 30 s after the last hit.
- Sky city flies its route with residents dormant; Strider city marches; sky wing pilots fly.
- Two-process client: same still bodies, clean console.
- Save, quit, reload: no new JSON keys; sleepers re-derived.

## Docs

New `docs/AI/systems/SimulationDistance.md` (frontmatter, symptoms such as "NPCs fight each other far
away where nobody is"), an entry in `docs/Human/the-systems.md`, AgentSystem.md's `Offstage`
paragraph and Gotchas updated for `Dormant`/`IsParked`, then `python3 tools/docs_check.py --index`.

## Extending — a new player-facing event

A random event (a raptor pack sent at a player, a hunting party) stays live by one of the existing
exemptions, never by a new switch: spawn it as an `NpcWorldSim` group (members carry
`GroupMembership`), or hand its members a player target (`AgentTargeting.ForceTarget` /
`ProvocationModule.Provoke`) as they set off. SimulationDistance.md's `Extending` says so.

## Out of scope

- Throttled or reduced-rate simulation of distant agents.
- Hiding, despawning or LOD-swapping distant agents.
- Any change to `NpcWorldSim` folding or `WorldStreamer` loading.
