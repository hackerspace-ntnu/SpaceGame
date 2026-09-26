---
system: SkyTribe
layer: characters
summary: The Sky Tribe end to end — faction, roster, city population and NPC-flown war-party vessels
paths:
  - Assets/Game/Scripts/Vehicles/SkyVessel/
  - Assets/Game/Scripts/World/Streaming/NavMesh/StaticNavMeshData.cs
  - Assets/Game/Scripts/agents/World/NpcGroup.cs
  - Assets/Game/Prefabs/Vehicles/Sky/
  - Assets/Game/Prefabs/Agents/Characters/SkyTribe/
  - Assets/Game/ScriptableObjects/Factions/Rosters/SkyTribe.asset
  - Assets/Game/ScriptableObjects/Factions/Core/SkyTribeFaction.asset
symptoms:
  - "a sky war party is raised and given up on the next moment when I am far from the Sky City"
  - "every reload of the world adds another sky city's worth of nomads"
  - "sky nomads walk to a railing and stand there staring up at a roof"
  - "an NPC spawned on the sky city has no NavMesh under it"
  - "a sky transport flies stern-first, its cockpit house trailing instead of leading"
  - "the second passenger off a hovering sky vessel is dropped in mid-air, on the first one's head"
  - "NPCs stand frozen where a despawned sky transport was, never walking again"
  - "a sky transport starts unloading while it is still sliding across its landing site"
  - "a sky war party comes home with its riders still seated and nobody ever gets off"
  - "two sky hulls park on top of each other outside the city"
  - "the passengers of a shot-down sky transport appear by a landing site hundreds of metres away"
  - "the Sky roster only ever has four people even though a fifth recipe exists"
  - "right after loading, a sky transport turns straight around, flies home and comes back about 20 s later"
  - "Failed to create agent because it is not close enough to the NavMesh logged twice every time a Sky war party spawns"
  - "after a quickload an empty sky transport hull stays parked at the city and no party owns it"
reads_with: [AgentSystem, Vehicles, NavMeshSystem, Persistence, TerrainGeneration]
updated: 2026-09-17
---

# Sky Tribe

The second tribe on the existing faction stack (no new faction machinery), plus the two things unique to it: a city that stands in the air with its own NavMesh, and war parties that travel by NPC-flown vessel instead of on foot. This doc is where faction/roster, city and vessels meet; [AgentSystem.md](AgentSystem.md) and [Vehicles.md](Vehicles.md) own the generic mechanisms (factions, `NpcWorldSim`, mounting) this tribe reuses rather than replaces.

## Model

- **Faction.** `SkyTribeFaction.asset` — Neutral `defaultStance`, Neutral to Sand by design (no row in `GlobalRelationships.asset`), registered in `FactionGoodwillLedger.tribes` on `NpcWorldSim`. Authored by `RosterAuthoring.AuthorSkyTribeFaction` (`Tools/SpaceGame/Agents/Author Sky Tribe Faction`), which mirrors onto Sky any row Sand has (none today — the rule is generic, not a special case for "Sand has none").
- **People.** The Sand-nomad body, recoloured: `SkyNomad_{Umber,Tan,Maroon,StrawHat}.prefab` under `Prefabs/Agents/Characters/SkyTribe/`, built by `NomadPrefabBuilder.SkyNomads` (`Build Sky Nomad NPCs`). Cloth is `SkyNomadCloth_*` — pale sky blue (0.58, 0.72, 0.86), cloud-white scarf accent (0.90, 0.91, 0.88) — under its own `MaterialPrefix`, distinct from Sand's `NomadCloth_*`, so the two tribes' builds never recolour each other's meshes even though they share mesh names. Each recipe also sets `WanderReachableOnly`, so a resident's `WanderModule` never targets an unreachable island of the city mesh (Gotchas).
- **Roster.** `Rosters/SkyTribe.asset`: the four prefabs as both Scout and Warrior (8 members), **no Rider role** — a Sky party flies, so it has no mount to leave behind. Hand items are Sand's own seven; `hostileLines` is `SkyTribeHostileLines.asset` (8 lines). War-party tiers: 0 = 2 Scouts, 1 = 3 Warriors + 1 Scout, 2 = 5 Warriors + 2 Scouts. A fifth recipe, `NomadPrefabBuilder.SkySoldier` (`Build Sky Soldier NPC`, its own `sky_soldier.fbx` and orange `SkySoldierCloth` palette), already exists but its prefab has not been built — `AuthorSkyRoster` silently skips any `SkyTribePeople` recipe whose `PrefabPath` does not resolve, so today's roster and city population are the four nomads only.
- **The city.** `SkyCityFleet.prefab` (art finished — never edit `sky_city.blend`/`.fbx`, or the scale/position `SkyCityBuilder`/`SkyFleetBuilder` set) stands in `persistentScene.unity`, static, no `NetworkObject`. It carries its own baked NavMesh ([StaticNavMeshData](Assets/Game/Scripts/World/Streaming/NavMesh/StaticNavMeshData.cs) + `SkyCityNavMesh.asset`, baked in prefab space by SkyCityNavMeshBaker so it moves with the fleet) and a `WorldSiteMarker` (`Home`, "Sky City", **`airborne = true`**) — full NavMesh mechanics: [NavMeshSystem.md](NavMeshSystem.md); airborne-site search rules: [TerrainGeneration.md](TerrainGeneration.md).
- **Vessels.** `SkySkiffTransport` (4 seats) and `SkyFreighterTransport` (8 seats), NPC-only — no player seat, camera or controls, but players can shoot at them. Built from the escort FBX models (the *static* `SkyFleetBuilder` escorts stay scenery) by SkyVesselBuilder (`Tools/SpaceGame/Vehicles/Build Sky Transports`); server-flown by [VesselPilot](Assets/Game/Scripts/Vehicles/SkyVessel/VesselPilot.cs) — full component API: [Vehicles.md](Vehicles.md).

## Key types

| Type | File | Role |
|---|---|---|
| `RosterAuthoring.AuthorSkyTribeFaction` / `AuthorSkyRoster` | RosterAuthoring.cs | Author the faction/ledger row and the roster; see Flows for build order |
| `NomadPrefabBuilder.SkyNomads` / `SkySoldier` / `SkyTribePeople` | NomadPrefabBuilder.cs | Recipes (faction, roster, `ClothPalette`, `WanderReachableOnly`); `SkyTribePeople` is what the roster author and the city population both read |
| `SkyCitySettlementWiring` | SkyCitySettlementWiring.cs | `Tools/Environment/Wire Sky City Settlement` — puts `WorldSiteMarker`, `SettlementAlarm`, `SettlementPopulation` and a `PromenadeAnchor` child on the fleet root; `Verify` is its post-condition, read by `SkyCitySettlementTests` |
| `SettlementPopulation` (Sky options) | [SettlementPopulation.cs](Assets/Game/Scripts/agents/Faction/SettlementPopulation.cs) | Generic type in [AgentSystem.md](AgentSystem.md); the city sets `inhabitants` = the four `SkyNomad_*`, cap 16 / 45 s / 4 per wave, ring 0–90 m, `initialWaves` 4 (1 s apart), `reachableFrom` = `PromenadeAnchor`, `keepGroundChunksLoaded` |
| `NpcGroupTransport` | [NpcGroup.cs](Assets/Game/Scripts/agents/World/NpcGroup.cs) | `smallVessel`/`largeVessel`, `travelSpeed` (28), `homeSiteName` (`WorldSite.SkyCityName`), `dockSpacing` (45 m); `VesselFor(riders)`, `IsDelivered`, `ShouldRelaunch`, `DockPoint(home, slot, spacing)` — rings of 6/12/18… slots, each ≥`spacing` from every other |
| `VesselPilot` / `VesselSeats` / `VesselMission` / `LandingSiteFinder` / `NpcSeating` | [SkyVessel/](Assets/Game/Scripts/Vehicles/SkyVessel/) | Full API, tunables and gotchas in [Vehicles.md](Vehicles.md) — this doc covers only how the Sky war party drives them |
| `WorldSite.SkyCityName` | [WorldSite.cs](Assets/Game/Scripts/World/Sites/WorldSite.cs) | The runtime constant (`"Sky City"`) both the settlement wiring and the war-party template's `homeSiteName` read, so they can never disagree |

## Flows

**Author + build order** (idempotent; re-run after any recipe change): `Author Sky Tribe Faction` → `Build Sky Nomad NPCs` (pass 1, no roster yet) → `Author Sky Tribe Roster` (needs the prefabs' baked faction) → `Build Sky Nomad NPCs` (pass 2, bakes `roster.handItems`) → `Build Sky Fleet Prefabs` (rebakes the city NavMesh, re-runs `Wire Sky City Settlement`) → `Build Sky Transports` → `Wire War Party Templates` (`WireWorldSim` adds/updates `sky-war-party`: transport = skiff/freighter, `travelSpeed` = the slower of the two prefabs' `VesselPilot.cruiseSpeed`, `homeSiteName` = `WorldSite.SkyCityName`).

**City population** — server/offline only. Every `spawnInterval` (45 s, faster during the 4 `initialWaves`), `SettlementPopulation` counts Sky residents inside `countRadius` and, below the cap, samples the ring and keeps only points `NavMeshReach.CanWalk` reaches from `PromenadeAnchor` (see [NavMeshSystem.md](NavMeshSystem.md) for how much of the mesh that actually is). Because the fleet lives in `persistentScene` rather than a chunk scene, `keepGroundChunksLoaded` registers the fleet root as a tracked transform, holds it `SuspendAnchor`ed until the streamer's own initial chunks load (so it never competes with the session's own spawn-area preload), then resumes it and waits for the chunks under `countRadius` before starting the spawn clock — skip this and a reload spawns a second population beside the one the save restores (measured: 16 → 24).

**War-party transport** (`sky-war-party`, driven by `NpcWorldSim`/`WarPartyDirector`):
1. **Raise.** `WarPartyDirector.ChooseOrigin` returns `WorldSiteRegistry.TryFindByName("Sky City")` (any kind, airborne, observed or not) instead of the usual nearest-camp search.
2. **Travel, folded.** The record moves at `NpcGroupTransport.travelSpeed` (28 m/s) and catches up like any party — never abandoned for distance while still flying (Gotchas).
3. **Board.** Inside `spawnRadius`, `NpcWorldSim.Spawn` counts riders with `NpcGroupComposition.Rides` and picks `VesselFor(count)` (tier 0/1 → skiff, tier 2 → freighter). `BoardTransport` first tries `TryTakeParkedHull` — an empty, undamaged, `Done` hull of that prefab within `dockReuseRadius` (150 m) — else spawns fresh at `FirstFreeDock`, `RiseToCruise`d before the network spawn so nobody watches it climb out of the ground, nose toward `QuarryPoint`. Riders spawn under the hull and seat exactly as on foot, except that each is made `seated` (`NpcSpawn.Create(..., seated: true)`): its `NavMeshAgent` is already off when it wakes at cruise height (Gotchas).
4. **Launch.** `pilot.Begin(dock, () => QuarryPoint(group), riders, despawnRadius)` — dock is `NpcGroupTransport.DockPoint`, `despawnRadius` is `NpcWorldSim.despawnRadius` (350 m), never a prefab literal. The pilot then runs `Cruise → Approach → Descend → Unload → Climb → Depart → Done` (landing/hover math: [Vehicles.md](Vehicles.md)). It chooses no landing site until `WorldStreamer.IsGroundLoadedAround(quarry, landing.ringMax + footprintRadius)` — until then it keeps flying at the quarry (Gotchas).
5. **Delivered?** Polled every tick, not event-driven (`Finished` never fires for a mid-run despawn): `NpcGroupTransport.IsDelivered(vesselExists, wrecked, aboard)` — true once nobody is seated, the hull is wrecked, or it is gone. **`Done` alone is not delivery** — a run with no landing site anywhere flies home still carrying the party. While undelivered and `Done`, `TransportParkedFor` accumulates; once it reaches `transportRetryDelay` (15 s), `ShouldRelaunch` re-`Begin`s the same hull from the dock. Once delivered, the group is an ordinary spawned party and the empty hull despawns once every player is beyond `despawnRadius`.
6. **Fold while aboard.** The vessel despawns **first** (`OnNetworkDespawn` unseats riders onto the NavMesh below), then the members — never the other order, or Netcode lifts seated children to the scene root with nothing to release them ([Vehicles.md](Vehicles.md) Gotchas). `Delivered` stays false; the group flies on from the vessel's last position.
7. **Release/restore.** A released or wiped-out group is not removed while `Transport` is still out (`ReleaseGroup`/`DisbandWhenFolded` wait for it). Restore: undelivered spawns in a vessel again; delivered walks. With no usable vessel prefab, the sim logs an error and spawns the party on foot (`Delivered = true`).

## Multiplayer

Every decision is server-side: `NpcWorldSim`/`WarPartyDirector` (`Network.Decides`), `VesselPilot.Update` (`Network.Simulates`). Vessels are `NetworkObject`s with a server-authority `NetworkTransform`; clients interpolate. Riders seat via `NpcSeating.Attach`/`TrySetParent` under the hull's `NetworkObject`, replicated the same way `NpcPassenger` seats a caravan mount — a seated rider keeps its own `AgentTargeting`/`PerceptionModule` and fires from the saddle. Both transport prefabs are registered network prefabs (`NetworkPrefabRegistrar.Sync`).

## Persistence

Faction, roster and prefabs are authored assets, not save state. City residents are ordinary `SettlementPopulation` spawns (`GameServices.World.Spawn`), saved once migrated into the chunk scene under the city — hence `keepGroundChunksLoaded` above; see [Persistence.md](Persistence.md) and the "settlement outside the chunk scenes" gotcha in [AgentSystem.md](AgentSystem.md). **Vessels and their seated riders are never saved individually** — only the war-party group record is, and `NpcGroup.Record.delivered` (appended 2026-09-17; an older save reads `false`) decides what comes back: an undelivered party spawns in a fresh vessel and tries again, a delivered one is on foot.

## Gotchas

- **A `Home` site is not a ground destination by default.** The city sits 228 m up with no ground link; its marker's `airborne` flag keeps every kind-based ground search (`NpcTaskModule`/`NpcTaskPlanner`/`WarPartyDirector`'s camp search) from sending an unrelated ground NPC there. Only `TryFindByName("Sky City")` (what the transport flow uses) or `includeAirborne: true` finds it — [TerrainGeneration.md](TerrainGeneration.md).
- **Most of the city's NavMesh is unreachable islands** (roofs, gas-bag tops, ladder-only decks, escort decks — ~120 welded islands, only ~3 300 m² of promenade actually connected). Both the population's spawn ring and each resident's `WanderModule` filter through `NavMeshReach.CanWalk`, or NPCs stand on a roof they can never leave, or a wander pick lands a partial path from a railing (`onlyReachableDestinations` + a back-off timer on a failed pick — [NavMeshSystem.md](NavMeshSystem.md)).
- **A war party never gives up on distance while it is still flying.** Ordinary parties abandon beyond `maxPursuitDistance`, but a Sky party's home site can be 1.5+ km from a moving quarry — `WarPartyRules.ShouldAbandon(..., inFlight: sim.IsInFlight(party))` exempts it. Tried the other way (`Track` before the abandon check) and still lost a party raised 1.6 km out on the very next step; the exemption is deliberate, not a shortcut.
- **A vessel waits for the ground before it looks for a landing site.** A party restored by a load is spawned in the world's first frame, before a single chunk scene has streamed in, and often already inside `approachDistance` — so its pilot went `Approach` and ran `LandingSiteFinder.Find` over empty space the very next frame, found no site, aborted and flew home; the group folded out of view and came back ~20 s later (measured 2026-09-17: hull `NEW → Cruise → Approach → Depart` inside frames 7971–7973, all 9 chunk samples round the quarry unloaded). `VesselPilot.MaintainSite` now skips choosing and re-checking while `IsGroundLoadedAround` is false; `LandingSiteFinder` stays pure and never asks about streaming. No `WorldStreamer` (a test scene) means nothing to wait for.
- **A rider is spawned with its `NavMeshAgent` already off, not switched off in `beforeSpawn`.** Unity logs "Failed to create agent because it is not close enough to the NavMesh" from *inside* `Instantiate` when an enabled agent wakes off the mesh — measured: the warning lands between the lines before and after `Instantiate`, and `NavMeshAgentMotor.Awake` has already set `enabled = false` by the time it returns. So `beforeSpawn` is too late; `NpcSpawn.Create(..., seated: true)` instantiates under an inactive cradle, disables the agents, then releases the NPC to the scene root. `NpcSeating.Suppress` does not record an agent it finds off, so unseating does not hand it back directly — the re-enabled `NavMeshAgentMotor`'s reattach (`ReattachInterval` 0.5 s) puts it on the NavMesh, exactly as it did when `Awake` switched it off.
- **A shot-down (wrecked) vessel drops its passengers straight down, never at a chosen site** — the fallback used to teleport them to a landing site the vessel had already picked, sometimes 100+ m away and into another vessel's footprint.
- **A hull must despawn its seated riders before it despawns itself**, or Netcode lifts them to the scene root with `RidesAsPassenger` still on and nothing ever releases them. `VesselPilot.OnNetworkDespawn` (server) unseats everyone first; `NpcWorldSim.DespawnTransport` always runs before `DespawnMembers`.
- **Two parked hulls never overlap.** `NpcGroupTransport.DockPoint` rings dock slots `dockSpacing` (45 m, ≥2× the freighter's footprint) apart, and a new arrival reuses a parked, undamaged, empty hull of the right prefab before spawning another — otherwise every returning party leaves a hull idling at the same dock point.
- **A transport's bow is not the model's fixed −Z.** The escort FBXs disagree with each other on which end is which (mesh names like `SternGear`/`Prow` are inherited from an unrelated part library and do not track it); `SkyVesselBuilder.Transport.ModelYawCorrection` is measured per vessel from the raw FBX's own geometry (both 0 today), not assumed. Seats/`Ramp`/`Drop` need no separate fix — they are read in or computed from the model's own (rotated) space.
- **The Sky roster silently has fewer people than the code lists** whenever a `SkyTribePeople` recipe's prefab does not exist yet (`SkySoldier` today) — `AuthorSkyRoster` filters missing prefabs rather than failing, so nothing breaks, but the roster, the tiers' role counts and the city population all read four people, not five, until `Build Sky Soldier NPC` runs.
- **Never rewrite `SkyCityBuilder`/`SkyFleetBuilder` wholesale** — the sky city art is finished; extend them (as the NavMesh bake and settlement wiring do, chained onto the end of `Build`) and never edit `sky_city.blend`/`.fbx`.
- **Chain a builder to its end, never to `SyncMenu()`.** Any menu that must run to completion in one pass (`Build Sky Nomad NPCs`, `Build Sky Transports`) calls `NetworkPrefabRegistrar.Sync(out _, out _)`, not `SyncMenu()` — the latter opens a modal "OK" dialog that parks the rest of the chain (savers, ragdolls) until a human clicks it.

## Extending

- **A new roster role or person:** add a `NomadRecipe` to `NomadPrefabBuilder` (its own `ClothPalette.MaterialPrefix` — reusing one recolours the wrong tribe's cloth), build its prefab, append it where `SkyTribePeople` is read, then re-run `Author Sky Tribe Roster`.
- **A new tribe with its own city and vessels:** follow the [spacegame-tribe](.claude/skills/spacegame-tribe/SKILL.md) skill end to end (§9 covers a home settlement) and the [spacegame-vessel](.claude/skills/spacegame-vessel/SKILL.md) skill for a new NPC transport; give the template an `NpcGroupTransport` pointing at the new vessels and a `homeSiteName` naming the new site.
- **Another structure outside the chunk scenes that needs walking NPCs:** the NavMesh bake pattern in [NavMeshSystem.md](NavMeshSystem.md) Extending, plus the three `SettlementPopulation` options (`reachableFrom`, `keepGroundChunksLoaded`, `initialWaves`) covered here and in [AgentSystem.md](AgentSystem.md).
- **Tuning flight/landing/capacity:** every number here is serialized on `VesselPilot`, `NpcGroupTransport` or `NpcWorldSim` — see [Vehicles.md](Vehicles.md) for the full tunable list.
