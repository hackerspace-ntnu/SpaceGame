# Appa herds and Sand herders — design

Date: 2026-10-04 · Branch: `Feat/create-factions` · Status: decided (the user was not available;
every choice below is a default they may overturn, listed in "Defaults to revisit").

## Request

"Appa must go back into the scene/game. Make some Appa herds wandering around. And have sand
nomads ride and herd Appas." Hard constraint: everything that moves goes through `AgentController`
+ `IBehaviourModule` + `IMovementMotor`; herding is a new module, never a script writing transforms.

## Why no Appa appears today

1. Main's cleanup (8e7ef62a) removed the hand-placed Appa from `persistentScene.unity`.
2. The surviving NpcWorldSim template `wild-appa` starts `startNearSite: AnimalGround` with
   `useStartPosition: 0`. The only `WorldSiteMarker` in the project is the Sky City (a `Home`), so
   `ResolveStart` falls back to the sim's own transform, **(0, 0, 0)**. That is in the authored-empty
   padding (world x 0–1000 has no terrain and no NavMesh). Every task resolves no site and falls back
   to `TryRoamPoint`, which samples the NavMesh within 450 m of the origin, finds nothing and leaves
   the record where it is. The group never moves and no player ever comes within `spawnRadius`.
3. Even spawned, the template is one Appa, and the prefab has no `FormationModule`,
   `NpcTaskModule` or `GoalTravelModule`, so a multi-Appa group could not travel together.

`AppaBuilder` was deleted on main on purpose (efdd3a64); the Appa prefab it wrote is intact (every
script resolves) and is now the source of truth. It is not restored; a small, idempotent authoring
script adds what the herds need on top of the prefab instead.

## Design

### Wild herds (Fauna)

Three seeded NpcWorldSim templates, `appa-herd-dunes`, `appa-herd-plateau`, `appa-herd-south`,
each a lead Appa plus four or five more, at explicit start positions sampled on the world NavMesh
(no `AnimalGround` sites exist). The old `wild-appa` template is **removed**: a save made with it
holds a record stuck at the origin, and the sim skips records whose template no longer exists, so a
new id is what un-sticks an existing save.

Cohesion reuses the caravan machinery rather than `HerdModule`: `NpcWorldSim.Configure` already
re-keys every member's `FormationModule` to its group id and hands the leader the task list. The
herd is a *mob*-shaped formation (3 lanes, wide jitter) behind a lead Appa whose `NpcTaskModule`
grazes (dwell flag `IsGrazing`, which `Appa.controller` has), drinks and drifts. `HerdModule` is
not used: it is keyed by a prefab-baked string with no runtime re-key (every Appa in the world would
be one herd) and it republishes its own slot intents, so a herd's destination creeps.

At a stop, followers stand in the formation's rest ring and amble inside it (`WanderModule` limited
to a short radius instead of 70 m free roam, so a resting herd grazes in place rather than
scattering and being reeled back). Shoot one and the existing Fauna stack applies — flee, or
fight when cornered; `FormationModule.regroupDistance` brings it back afterwards.

### Herders (Sand)

One seeded template, `sand-appa-herders` (tribe `SandTribeFaction`): a **point rider** (leader),
two **drovers** and six head of livestock.

- `NomadAppa.prefab` — Appa variant, born saddled, an `NpcPassenger` seating a Sand nomad
  (`Nomad_StrawHat`), `MountedRiderPose`, `HerdingModule`. The mount is the agent (outrider
  pattern); the rider keeps eyes and gun and shoots from the saddle. The mount stays **Fauna**, like
  every caravan mount (`GroupMembership`: a mount carries, the rider fights).
- `NomadHerdAppa.prefab` — Appa variant serializing **`SandTribeFaction`**. The herd belongs to the
  herders: `GroupMembership.Stamp` enlists a riderless member into the group's tribe on the server
  anyway, and `EntityFaction` is not replicated, so the prefab must already carry the tribe (the
  same rule `RosterValidation` applies to roster members).

`HerdingModule` (new, `Modules/Flocking/`, priority Social + 1 = 16, above `FormationModule`):

- On the member that leads its formation it passes (`null`): the point rider routes with its task
  list and `GoalTravelModule`, and the livestock follow it as a formation.
- On any other herder it picks a station on an arc behind the herd — the drag and flank positions
  of a cattle drive — around the livestock's centroid, opposite the leader's smoothed heading, at
  the herd's radius plus a stand-off. At the station it returns `StopAndFace(herd centre)`.
- If a head strays beyond `strayRadius` from the centroid, the herder nearest it rides to the far
  side of the stray (running when far) — the column reels it in, the herder is seen to fetch it.
- Livestock = formation members without a `HerdingModule`; herders = members with one, minus the
  leader. No livestock left → passes, and the herder falls back to riding in the column.
- All geometry is in a pure static `HerdingMath` (unit-tested); the module only gathers positions.

Drovers are listed after the livestock so their unused column slots are the tail's.

### Faction and goodwill

- Wild Appas: Fauna, unchanged.
- Herders' mounts: Fauna (convention). Riders: Sand (prefab).
- Herded livestock: Sand. Hurting or killing one costs Sand goodwill through the existing
  `ProvocationModule` / `HealthReactionModule` feeds — "you shot their animals" — and the Clankers'
  Hostile default now covers them as it covers the nomads. No new relationship rows.

### Multiplayer

Server-authoritative throughout: `NpcWorldSim` and every module run on the server only
(`AgentController` gates the tick; `HerdingModule` holds no authority check). The two new prefabs
are registered with `NetworkPrefabRegistrar.Sync`; the seated nomad replicates through
`NpcPassenger`'s existing spawn-and-parent path. No new messages.

### Persistence

Groups persist as `npcworld` records (position, goal, task index) keyed by template id; members are
disowned by `NpcSpawn` and rebuilt from the record. `HerdingModule` holds no state worth saving —
stations are recomputed every tick from where the herd is. The rider's stand-down carries through
`CrewSaveable` as on `NomadOstrich`. Expected in the save JSON: one `npcworld` record per new id.

## Implementation

1. `HerdingMath` + tests (TDD), then `HerdingModule`.
2. `AppaHerdAuthoring` (`Tools/Creatures/Author Appa Herds`): adds the herd modules to `Appa.prefab`,
   writes the two variants, syncs network prefabs and save wiring, and writes the four templates
   into `NpcWorldSim` (removing `wild-appa`). Idempotent.
3. Asset tests read the prefabs and the scene back.
4. Docs: AgentSystem.md (Key types, Gotchas), the-systems.md is unchanged (no new system doc —
   this is AgentSystem content), skills' module table.

## Defaults to revisit

- Herd sizes (5–6 wild, 6 herded), positions, 2 drovers per herd, `Nomad_StrawHat` as every herder.
- Livestock is Sand (goodwill cost); the alternative is Fauna with no goodwill cost.
- Herders' mounts are saddled, so a player can take one (the nomad is thrown off, the animal
  becomes the player's — existing caravan behaviour).
- Drovers fetch strays visually; the animals do not yet react to herder pressure (a real
  pressure-driven drive is a second module on the livestock, deferred).
- GDC: emergent herd behaviour from existing rules (`GDC-L1-SYS-0003`), readable roles — the
  point rider leads, drovers ride the flanks, so a player can tell who steers and who guards
  (`GDC-L1-SYS-0006`); mount carries, rider shoots (`GDC-L1-SYS-0005`); a herd drive passing in the
  distance is environmental storytelling about the Sand people's life (`GDC-L1-LEVEL-0007`).
