# Sky Tribe, Sky City Population and War-Party Vessels — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Sky tribe lives in the sky city; its war parties fly to a hunted player in a skiff or freighter, land on flat-enough ground (or hover low and drop off), and fight on foot.

**Architecture:** The Sky tribe is a second tribe on the existing faction stack (FactionDefinition, FactionRoster, goodwill ledger, war-party director) — no new faction machinery. Sky people are the sand-nomad characters rebuilt through `NomadPrefabBuilder` with a Sky recipe (faction, roster, cloth palette). The sky city gets its own baked NavMesh added at runtime, a `WorldSiteMarker`, and the existing `SettlementPopulation`/`SettlementAlarm`. The skiff and freighter become NPC-only transport prefabs driven by a server-simulated `VesselPilot`; a war-party template with a `transport` block makes `NpcWorldSim` spawn the party seated in a vessel, which flies to a landing site found by a pure `LandingSiteFinder`, unloads, and leaves.

**Tech Stack:** Unity 6000.3.11f1, C#, NGO (NetworkObject/NetworkTransform, `TrySetParent`), Unity AI Navigation (`NavMeshBuilder`, `NavMesh.AddNavMeshData`), NUnit EditMode.

**Spec:** this plan is its own spec; decisions were made with the user on 2026-09-17 (answers recorded in the table below). Parent systems: [2026-09-16-rosters-and-war-parties-design.md](../specs/2026-09-16-rosters-and-war-parties-design.md), `.claude/skills/spacegame-tribe/SKILL.md`, [Vehicles.md](../../AI/systems/Vehicles.md), [NavMeshSystem.md](../../AI/systems/NavMeshSystem.md), [AgentSystem.md](../../AI/systems/AgentSystem.md).

## Status (2026-09-17)

**Part 1 (Tasks 1–8) is implemented.** Tasks 1–7 are done and play-verified on the host (see
`task-N-report.md`, `fix-t3t4-report.md`, `fix-t4-followup-report.md` and
`fix-skiff-facing-report.md` in this folder) — faction, roster, city NavMesh and population,
vessel flight/landing math, the transport prefabs, and war parties that fly, land or hover, drop
off and return. **Task 8: docs and skill are done** — `docs/AI/systems/SkyTribe.md`,
`docs/Human/the-systems.md`, `.claude/skills/spacegame-vessel/SKILL.md`, and cross-references from
`AgentSystem.md`/`Vehicles.md`/`NavMeshSystem.md`/`TerrainGeneration.md`/`Persistence.md`;
`docs_check.py --index` is clean. **Task 8's play/client verification checklist (host+client vessel
motion and dismount, save/reload mid-flight and after delivery) is still pending** a controller
pass with a connected Unity Editor. Part 2 (Tasks 9–14: NPC worn gear, flight, swarmers) has not
started.

## Decisions (user, 2026-09-17)

| Question | Decision |
|---|---|
| Who flies the vessels | **NPC-only** for now. No player seat, camera or controls. Players can shoot at them. |
| Drop-off | **Land first:** search for ground that is flat *enough* (generous: moderate slope and height variation are fine), touch down, the party walks off. **Fallback:** if no landing site, hover a few metres over open ground and the party drops off onto the NavMesh. |
| Which vessels | **SkySkiff** carries smaller parties, **SkyFreighter** carries larger ones. SkyTug stays scenery. |
| Sky tribe stance | **Neutral tribe with its own goodwill**, like Sand; neutral to Sand. |
| Characters | Reuse the sand-nomad characters (same FBX), recoloured and assigned to the Sky tribe. |

## Facts this plan is built on (verified 2026-09-17)

- `SkyCityFleet.prefab` (nests `SkyCity.prefab` + `SkyFreighter`, `SkySkiff`, `SkyTug` from `Prefabs/Environment/Structures/SkyFleet/`) is placed in `Assets/Game/Scenes/world/persistentScene.unity` at world (3970.32, 228, 1025). Static, no NetworkObject, no saver, no WorldSite, no settlement components. Built by `Editor/Environment/SkyCityBuilder.cs` (root scale 1.3) and `SkyFleetBuilder.cs` (escorts at `HeldAt = Position * SkyCityBuilder.Scale`: Freighter (78,26,-8), Skiff (-80,8,38), Tug (-78,-14,-40)). ~36 m ships; city hull 22 × 116 m, promenades 6.8 m wide.
- **No NavMesh on the city.** `WorldNavMeshBaker` bakes only the 48 chunk scenes into `Assets/Game/Settings/WorldNavMesh.asset`; persistentScene is never scanned. `WorldNavMeshProvider` adds the asset at runtime.
- `SettlementPopulation` spawns via `GameServices.World.Spawn` at `NavMesh.SamplePosition` points in a ring; runs on `Network.Decides`. `ClankerSettlementBuilder` (~472–491) shows the `Configure(...)` wiring.
- `NomadPrefabBuilder` hardcodes `FactionPath` (SandTribe) and bakes `RosterAuthoring.SandRosterPath` hand items; `ApplyClothMaterial` tints `Cloth_*` meshes. Recipes: `Nomad` (staff) + 4 `SandNomads`.
- `NpcPassenger` seats a NetworkObject rider under the mount's NetworkObject with `TrySetParent` and sets `AgentController.RidesAsPassenger`, so a seated rider keeps shooting.
- `NpcWorldSim.Spawn` plans members with `NpcGroupComposition.Resolve`, samples NavMesh per member and stamps `GroupMembership` before network spawn. War parties: `WarPartyDirector.Raise` → `ChooseOrigin` (nearest unobserved `SiteKind.Camp`, else a fallback) → `sim.CreateGroup`.
- Ledger `tribes` list and `GlobalRelationships` live in persistentScene / `ScriptableObjects/Factions/Core/`.

## Global Constraints

- **Multiplayer:** every decision server-side (`Network.Decides`/`Simulates`); vessels are NetworkObjects with server-authoritative NetworkTransform; passengers seat via `TrySetParent` under the vessel's NetworkObject; everything spawned at runtime is registered in the network prefab list. Verify on a client.
- **Persistence:** vessels and passengers are **not** saved individually — the war-party group record rebuilds them. Any new record field is **appended** (old saves read defaults). The sky city population is saved by `SettlementPopulation`'s existing path.
- **Never pop things in or out of a player's view:** vessels spawn and despawn only beyond spawn range, like groups.
- **No code smells:** tunables serialized; one definition per constant; builders own prefabs (no hand-edited YAML); no dead code.
- **Commit only when the user says so.** Stage only this plan's files. Another agent may work on the sky city art — `sky_city.blend`/`.fbx` are not edited by this plan; `SkyCityBuilder.cs`/`SkyFleetBuilder.cs` may be extended, not rewritten.
- **Docs are part of each change** (`docs_check.py --index`), and repeatable procedures get skill updates (user standing request).
- Verification: typecheck `/c/Users/tobia/conda/miniconda3/python tools/typecheck.py --editor`; tests via `SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("(Fixture1|Fixture2)")` + `Temp/headless_tests.txt` (`DONE`, `FAILED=0`); Unity tools `mcp__UnityMCP__*`; never run a menu or tests while `EditorApplication.isPlaying`.

## Default tunables (starting values, all serialized)

| Tunable | Default | Where |
|---|---|---|
| Sky population max / interval | 16 / 45 s | SettlementPopulation on the city |
| Vessel cruise speed / accel / turn rate | 28 m/s / 6 m/s² / 45°/s | VesselPilot |
| Cruise clearance above terrain | 45 m, never below 30 m | VesselPilot |
| Approach distance from quarry | landing ring 40–90 m | LandingSiteFinder settings |
| Landing footprint radius (skiff / freighter) | 12 m / 18 m | vessel prefab |
| Max slope / max height spread across footprint | 22° / 3.5 m | LandingSiteFinder settings |
| Hover drop height | 4 m | VesselPilot |
| Unload interval per passenger | 0.6 s | VesselPilot |
| Skiff capacity | 4 | vessel prefab (seat count) |
| Freighter capacity | 8 | vessel prefab (seat count) |
| Transport-group travel speed while folded | 28 m/s | war-party template transport block |

---

### Task 1: The Sky tribe faction

**Files:** Create `Assets/Game/ScriptableObjects/Factions/Core/SkyTribeFaction.asset` (through an editor menu, not by hand); modify `GlobalRelationships.asset` rows; add Sky to `FactionGoodwillLedger.tribes` on the NpcWorldSim object in persistentScene. Tooling: extend `Editor/Agents/RosterAuthoring.cs` with `AuthorSkyTribeFaction()` (menu `Tools/SpaceGame/Agents/Author Sky Tribe Faction`), idempotent. Test: extend `FactionAssetTests` (or new `SkyTribeAssetTests`).

**Behaviour:**
- `SkyTribeFaction`: `factionName` "Sky Tribe", `defaultStance` Neutral, a sky-blue `hudColor`.
- Relationship rows mirror Sand's non-default rows (read `GlobalRelationships` and copy every row involving Sand onto Sky with the same stance toward the same other faction), excluding Sand↔Sky which is Neutral (no row). Clankers remain hostile via their default stance.
- Ledger `tribes` gets Sky appended (scene edit through `SerializedObject`, persistentScene opened additively only if not already open-and-dirty; see `RosterAuthoring.WithWorldSim`).

**Tests:** asset exists, Neutral default, ID set; for every Sand row there is a matching Sky row with the same stance; no Sand↔Sky row; ledger scene list contains Sky (read the scene via `AssetDatabase`/YAML grep in the test is not allowed — verify through the menu's own post-condition log and a scene read-back step instead).

**Verify:** typecheck; run menu; read `persistentScene.unity` back (ledger `tribes` has two entries); fixtures green.

---

### Task 2: Sky people prefabs and the Sky roster

**Files:** Modify `Editor/Agents/NomadPrefabBuilder.cs` (recipes carry faction, roster, palette and prefab folder); modify `Editor/Agents/RosterAuthoring.cs` (`AuthorSkyRoster()`, menu `Tools/SpaceGame/Agents/Author Sky Tribe Roster`); create prefabs `Assets/Game/Prefabs/Agents/Characters/SkyTribe/SkyNomad_{Umber,Tan,Maroon,StrawHat}.prefab` by menu `Tools/SpaceGame/Agents/Build Sky Nomad NPCs`; roster `ScriptableObjects/Factions/Rosters/SkyTribe.asset` + `SkyTribeHostileLines.asset`. Test: extend `RosterAssetTests` with Sky cases (or `SkyRosterAssetTests`).

**Behaviour:**
- `NomadRecipe` gains `FactionPath`, `RosterPath`, `ClothPalette` (colour set applied by `ApplyClothMaterial`), and the prefab path already exists. Sand recipes keep their current values exactly (building Sand must produce byte-identical candidate lists and faction — assert in the test). Remove the hardcoded `FactionPath` const and the `RosterAuthoring.SandRosterPath` hardcode in `ConfigureRandomWeapon` in favour of recipe fields.
- Sky recipes: same four FBX as the sand nomads, `RandomWeapon = true`, faction SkyTribe, roster SkyTribe, a sky palette (pale blues/whites/brass; distinct from sand at a glance), `ClothWindScale` like the sand nomads (0.35).
- `Build Sky Nomad NPCs` runs the same chain as `Build Sand Nomad NPCs` (network prefab sync, `SaveableWiring.TryWirePrefabs`, `RagdollWiring.WirePrefabs`) but **does not** add a caravan to the scene.
- Sky roster: Scout and Warrior = the four Sky prefabs; **no Rider** role (Sky parties fly); `handItems` = the same seven hand weapons as Sand; hostile lines Sky-flavoured (4 lines); tiers: 0 = 2 Scout; 1 = 3 Warrior + 1 Scout; 2 = 5 Warrior + 2 Scout (fits the freighter, not the skiff). `SkyTribeFaction.roster` back-reference.
- The builder bakes `roster.handItems` into each Sky prefab's `NpcRandomLoadout.candidates`.

**Tests:** Sky roster validates (`RosterValidation.Problems` empty), back-reference, tiers as above, baked candidates equal `handItems` on all four Sky prefabs; every Sky prefab serializes the Sky faction, carries AgentController/EntityFaction/NetworkObject, meets `VisionBaseline`, has savers (count ≥ the Sand counterpart's) and a RagdollRig; Sand prefabs unchanged (faction still Sand, candidates still Sand roster).

**Verify:** menus in order (Author Sky Tribe Faction → Build Sky Nomad NPCs → Author Sky Tribe Roster → Build Sky Nomad NPCs again so candidates bake), network prefab list contains the four Sky prefabs, fixtures green (`RosterAssetTests`, `VisionBaselineTests`, `PrefabPersistenceTests`, `NomadAlertWiringTests`).

---

### Task 3: A NavMesh on the sky city

**Files:** Create `Assets/Game/Scripts/World/Streaming/NavMesh/StaticNavMeshData.cs` (runtime: adds a baked `NavMeshData` at this transform on enable, removes on disable); create `Editor/Environment/SkyCityNavMeshBaker.cs` (menu `World/Streaming/Bake Sky City NavMesh`); asset `Assets/Game/Settings/SkyCityNavMesh.asset`. Wire the component onto the `SkyCityFleet` instance root through `SkyCityBuilder` or a wiring menu (not a hand edit). Test: `SkyCityNavMeshTests`.

**Behaviour:**
- The baker collects sources from the `SkyCityFleet` prefab contents (non-trigger colliders only, same agent type and voxel settings as `WorldNavMesh.asset` so ground NPCs path identically) **in prefab-local space**, builds `NavMeshData`, saves the asset. Local space + the runtime component adding it at the instance's transform means moving the city in the scene needs no re-bake.
- `StaticNavMeshData`: `NavMesh.AddNavMeshData(data, transform.position, transform.rotation)` in `OnEnable`, `NavMesh.RemoveNavMeshData` in `OnDisable`; logs a clear error if the asset is missing. Runs on every machine (clients path nothing, but keeping it symmetric costs nothing and matches `WorldNavMeshProvider`). Follow `WorldNavMeshProvider`'s code style.
- A build check like `WorldNavMeshBuildCheck` is **not** added now; record it as a follow-up in NavMeshSystem.md.

**Tests:** after adding the baked data at the instance transform in EditMode, `NavMesh.SamplePosition` within 2 m of a point on each promenade deck succeeds; the asset exists and is non-empty.

**Verify:** run the bake, commit-ready asset, play check: `NavMesh.SamplePosition` near the promenade at (3970, 228, 1025)-relative points succeeds in play (controller runs this).

---

### Task 4: Populate the sky city

**Files:** Extend `SkyCityBuilder` (or a `SkyCityWiring` editor menu `Tools/Environment/Wire Sky City Settlement`) to put on the `SkyCityFleet` root: `WorldSiteMarker` (SiteKind.Home, name "Sky City"), `SettlementAlarm` and `SettlementPopulation` configured like `ClankerSettlementBuilder` with Sky faction and the four Sky prefabs as inhabitants; if the population component's ring sampling assumes ground level, add a serialized vertical sample distance so decks 30+ m above the root are reachable (do not break Clanker behaviour). Test: `SkyCitySettlementTests`.

**Behaviour:**
- Population spawns on the city's NavMesh (Task 3). Ring radii cover the promenades (city root to hull edges); `countRadius` covers the whole city. Max 16, interval 45 s. The first wave should fill quickly so the city is not empty on arrival — if `SettlementPopulation` has no "fill on start" option, add a serialized `initialWaves` (default 0 so Clankers are unchanged; Sky sets it to fill to max over the first few seconds).
- Residents wander within the city: set the spawned members' wander/errand radius so they stay on the decks (inspect what the Sky prefabs use — `WanderModule`/`NpcTaskModule` — and constrain via NavMesh: a NavMesh island cannot be left without links, so wandering on decks is naturally contained; verify no wander target is sampled off-city).

**Tests:** the scene instance (read through the wiring menu's post-condition) has the three components configured with Sky faction and 4 inhabitants; `initialWaves` default leaves Clanker settlement behaviour unchanged (existing `ClankerSettlementTests` green).

**Verify:** play: within ~10 s of loading the world, Sky people stand and walk on the promenades; they do not fall off; controller probe counts ≥ 8 Sky agents within the city bounds.

---

### Task 5: Vessel flight and landing rules (pure)

**Files:** Create `Assets/Game/Scripts/Vehicles/SkyVessel/LandingSiteFinder.cs`, `VesselFlightMath.cs`, `VesselMission.cs` (pure state machine). Tests: `LandingSiteFinderTests`, `VesselFlightMathTests`, `VesselMissionTests`.

**Interfaces (produce exactly):**
- `LandingSettings` [Serializable] { ringMin 40, ringMax 90, candidatesPerRing 16, rings 3, maxSlopeDegrees 22, maxHeightSpread 3.5, clearanceHeight 25, hoverHeight 4 } + `Default`.
- `interface IGroundProbe { bool TryGround(Vector3 xzPoint, out Vector3 point, out Vector3 normal); bool IsClear(Vector3 center, float radius, float height); bool TryNavMesh(Vector3 point, float maxDistance, out Vector3 onMesh); }` — the Unity implementation (`PhysicsGroundProbe`, raycasts ignoring triggers on the vision occlusion layers, `Physics.CheckCapsule`/`OverlapBox` for clearance, `NavMesh.SamplePosition`) lives in a separate small file; tests use a fake probe (height function + blocked circles + mesh coverage).
- `enum DropMode { Land, Hover }`; `readonly struct DropSite { Vector3 Point; DropMode Mode; Vector3 UnloadPoint; }`.
- `static DropSite? LandingSiteFinder.Find(Vector3 quarry, Vector3 approachFrom, float footprintRadius, in LandingSettings s, IGroundProbe probe)`: candidates on rings around the quarry, preferring the side facing `approachFrom`; a **Land** site needs every footprint sample (centre + 8 on the rim) grounded, slope ≤ max at the centre, height spread ≤ max, clear above, and a NavMesh point within 4 m of the ramp end; otherwise the best **Hover** site is any grounded, clear-above point with NavMesh within 4 m below (unload point = that NavMesh point). Score = |distance − preferred mid-ring| + small penalty for the far side. Returns null only when neither exists.
- `VesselFlightMath`: `CruiseAltitude(float groundBelow, float groundAhead, float clearance, float floor)`, `Step(Vector3 position, Vector3 velocity, Vector3 target, float maxSpeed, float accel, float dt) → (position, velocity)` with arrival slowdown, `TurnToward(Quaternion rotation, Vector3 flatDirection, float degreesPerSecond, float dt)`.
- `VesselMission` states `Cruise → Approach → Descend → Unload → Climb → Depart → Done` with pure transitions given sensor inputs (distance to site, altitude error, passengers remaining, clear timer); `Descend` targets ground for Land, hover height for Hover; `Unload` releases one passenger per `unloadInterval`; `Depart` flies back toward the home site; `Done` when beyond `despawnDistance` of every player (the sim despawns it).

**Tests:** flat ground → Land at a mid-ring point facing the approach; gentle 10° slope with 2 m spread → still Land; rocky field (spread 8 m everywhere) → Hover; ring fully blocked above → null; NavMesh missing at ramp → Hover; the chosen site prefers the approach side; flight step never overshoots and decelerates on arrival; cruise altitude respects clearance over a ridge ahead; mission runs Cruise→…→Done for Land and for Hover, unload releases one per interval, a passenger count of 0 skips Unload.

---

### Task 6: The transport vessels

**Files:** Create `Editor/Vehicles/SkyVesselBuilder.cs` (menu `Tools/SpaceGame/Vehicles/Build Sky Transports`) producing `Assets/Game/Prefabs/Vehicles/Sky/SkySkiffTransport.prefab` and `SkyFreighterTransport.prefab` from the existing `SkySkiff`/`SkyFreighter` models; runtime `Assets/Game/Scripts/Vehicles/SkyVessel/VesselPilot.cs` (NetworkBehaviour), `VesselSeats.cs`, `PhysicsGroundProbe.cs`. Register both prefabs in the network prefab list. Tests: `SkyTransportPrefabTests`.

**Behaviour:**
- Prefab: root NetworkObject + server-authoritative NetworkTransform (interpolated), kinematic Rigidbody, the model's colliders (solid, not triggers), a `Seats` child with N seat markers on the deck (skiff 4, freighter 8), a `Ramp` marker (landing unload end), a `Drop` marker (hover unload point below the hull), footprint radius, `EntityFaction` (Sky) so it is a valid target and shows faction, `HealthComponent` + `NetworkedHealthComponent` (vessels can be shot; on death: passengers dismount where they are, the vessel falls/despawns — keep simple: disable pilot, drop passengers via Hover unload immediately, despawn after 10 s).
- `VesselSeats`: seat an NPC under the vessel's NetworkObject exactly like `NpcPassenger.Seat` (reuse its seating helper or extract a shared `NpcSeating` utility rather than copy it — `NpcPassenger` must keep working; `AgentCarryTests`/rider tests green), set `RidesAsPassenger`, pose seated; `Unseat(i, worldPoint)` unparents, restores the agent, places it on the NavMesh at `worldPoint`.
- `VesselPilot` (server only; clients just interpolate): holds a `VesselMission`, a home point, a quarry transform/position feed, and ticks `VesselFlightMath` on the kinematic body. On `Approach` it calls `LandingSiteFinder.Find` once (again if the site becomes blocked). On `Unload` it unseats passengers to the ramp end (Land) or drop point (Hover, snapped to NavMesh via the probe). Exposes `Begin(Vector3 home, Func<Vector3> quarry, IReadOnlyList<GameObject> passengers)`, `State`, `event Action<GameObject> PassengerUnloaded`, `event Action Finished`.
- Engine presentation (thruster light/sound) is optional; if added, it must run on every machine from replicated state only.

**Tests:** prefab components present (NetworkObject, NetworkTransform with server authority, kinematic RB, seats = capacity, ramp/drop markers, EntityFaction Sky, health); both prefabs in the network prefab list; `VesselSeats` seat/unseat round trip on bare GameObjects in EditMode restores parent and `RidesAsPassenger`.

---

### Task 7: War parties fly in

**Files:** Modify `Scripts/agents/World/NpcGroup.cs` (template `transport` block; record `delivered`), `NpcWorldSim.cs` (transport spawn/fold path), `WarPartyDirector.cs` (origin = tribe home site when the template has a transport), `Editor/Agents/RosterAuthoring.cs` (`WireWorldSim` adds a `sky-war-party` template). Tests: extend `RuntimeGroupTests`, `GroupRecordTests`, `NpcGroupCompositionTests`, `WarPartyDirectorTests`.

**Behaviour:**
- `NpcGroupTemplate.transport` [Serializable] { `GameObject smallVessel`, `GameObject largeVessel`, `float travelSpeed` (folded, 28), `string homeSiteName` ("Sky City") }. Vessel choice: the planned member count fits the small vessel's capacity → small, else large (read capacity from the prefab's `VesselSeats`).
- `NpcGroup` runtime `Transport` (GameObject, not saved) and saved appended `delivered` bool (old saves: false).
- **Folded** transport group not yet delivered: moves at `transport.travelSpeed` (catch-up rules unchanged); altitude is irrelevant while folded.
- **Spawn** (player within spawn radius) of an undelivered transport group: spawn the chosen vessel at `group.Position` raised to cruise altitude, facing the quarry; spawn the planned members (same composition, stamping and seeds as today) and seat them; `VesselPilot.Begin(home, quarry position from the director's lead, passengers)`. Members count as `Live`/`Fighters` from the start (they can be shot and can shoot while seated).
- `PassengerUnloaded`: nothing extra (members are already Live). `Finished` or all seats empty → `delivered = true`; from then the group is an ordinary spawned war party (RefreshQuarryLead/SteerSpawned as today). The vessel keeps flying `Depart` and is despawned by the sim when beyond despawn range of every player (never in view), then `Transport = null`.
- **Fold** of an undelivered transport group (players left): despawn vessel and seated members together (existing despawn path + vessel), keep `delivered = false`; the record's position = vessel position.
- **Fold** of a delivered group: as today; the vessel, if still present and out of range, is despawned.
- **Restore**: undelivered → spawns in a vessel again when approached; delivered → on foot.
- Director: `ChooseOrigin` for a transport template uses the `WorldSiteRegistry` site named `transport.homeSiteName` (any kind) when registered, ignoring the camp search and the observed check (a vessel departing its own city is expected to be seen); falls back to today's logic if the site is missing. Catch-up still applies while folded.
- Scene: `sky-war-party` template (runtimeOnly, bountyHunters, tribe Sky, transport skiff/freighter, home "Sky City"), added by `WireWorldSim`.

**Tests:** vessel choice by capacity (tier 0 and 1 → skiff, tier 2 → freighter with the Task 2 tiers); `delivered` round-trips and old saves read false; a folded transport group advances at transport speed; `ChooseOrigin` returns the home site's position when registered, fallback otherwise; RestoreRecords keeps `delivered`.

---

### Task 8: Docs, skill and verification

**Files:** New `docs/AI/systems/SkyTribe.md` (Model → Key types → Flows → Multiplayer → Persistence → Gotchas → Extending) covering city NavMesh, population, vessels, landing/hover, war-party transport; `docs/Human/the-systems.md` entry (new system); updates to `Vehicles.md` (NPC transports row), `NavMeshSystem.md` (second baked asset, missing build check follow-up), `AgentSystem.md` (pointer only — at its line cap), `Persistence.md` (`delivered`), faction plan status; skills: `spacegame-tribe` (transport option, sky city example), new `.claude/skills/spacegame-vessel/SKILL.md` (how to turn a static model into an NPC transport: builder, seats, pilot, probe, network registration, landing tuning, tests) + CLAUDE.md Skills row. `docs_check.py --index` clean.

**Verification (controller):**
1. Typecheck; full EditMode run compared against known pre-existing failures.
2. Play (host): Sky people on the promenades; provoke Sky to AtWar (shoot a Sky citizen); a skiff launches from the city, flies to you, lands on flat-enough ground or hovers, the party gets off and fights; beat parties until tier 2 → the freighter comes; flee → vessel folds out of sight; save/reload mid-flight → the party comes again in a vessel; after delivery reload → party on foot.
3. Client: the vessel's motion and seated passengers look right on a client; passengers dismount on the client; the war notice appears for the client's own war.

---

## Part 2 — NPC gear, swarmers and wandering sky groups (added 2026-09-17, user request)

**Goal:** NPCs of any tribe can wear body gear through the same gauntlet/torso system the player uses. Worn gear is rare, and a fully stacked NPC is very rare. Sky people wearing a wing pack become **swarmers**: flyers that swarm around the sky city, join war parties to attack the hunted player, and escort wandering sky groups.

**Architecture:** Reuse the player's body-equipment model: `BodySlot`, `EquipKind`, `BodySlotRules`, `WornSeat`/`ForearmSeat`, `WornVisual`, and the worn item's own `Use`/`Present` split. Put it behind an NPC-side controller that the server drives, instead of building a parallel gear system. Rosters gain a weighted, seeded worn-gear loadout, exactly like hand weapons. Flight reuses the existing flight models (`OrnithopterFlightModel` for the wing pack, the wingsuit glide for the wingsuit) through an NPC flight module. Swarming is a new behaviour module layered on that flight.

### Decisions for Part 2

| Question | Decision |
|---|---|
| Who can wear gear | Any tribe NPC (Sand nomads and Sky people), through the tribe's roster. |
| How common | Rare. Most members wear nothing; one piece is uncommon; a full stack (torso + both gauntlets) is very rare. Weights live on the roster and are tunable. |
| What the gear does | The worn item's own behaviour, fired by NPC logic instead of player input. |
| Swarmers | Sky people with a wing pack; their own swarm flight logic; they circle the sky city, join war parties, escort wandering groups. |
| Wandering Sky groups | Small vessels with swarmers around them, or swarmer-only flocks. The sky counterpart of Sand's ostrich/Appa caravans. |

### Task 9: NPCs wear body gear

**Files:**
- Create `Assets/Game/Scripts/Items/Body/NpcBodyEquipment.cs`: an NPC implementation of the existing `IBodyEquipment` seam, server-decided, with replicated slot ids, worn through the same `WornSeat`/`ForearmSeat` and saved through the existing body-equipment saver path or a thin NPC saver.
- Modify `FactionRoster` to add `wornGear` entries (item + weight per slot kind) and a `WornGearChances` block: chance of any torso item, chance of each gauntlet, and a cap on a full stack.
- Extend `NomadPrefabBuilder` so it adds the component to tribe NPC prefabs.
- Add a pure `WornGearDraw` that picks seeded by group seed + member index (like `NpcRandomLoadout`) and applies before network spawn via `GroupMembership`.

**Behaviour:**
- Worn gear appears on the NPC's forearms and back for every machine, and survives a save when the NPC is saved individually.
- Group members are rebuilt from the seed, so they come back with the same gear.
- NPC skeletons use the same humanoid bones the seats need; verify the nomad rig resolves the forearm and spine bones, and extend the bone lookup if it does not.

**Tests:**
- Draw rarity over 10 000 seeded draws matches the configured chances, and a full stack stays at or under its cap.
- The same seed gives the same gear.
- A worn gauntlet on a nomad sits on its forearm bone.
- The slot ids replicate (EditMode wiring test), and a restore round-trips.

### Task 10: NPCs use worn gear

**Files:**
- Create `Assets/Game/Scripts/agents/Modules/Combat/NpcGearUseModule.cs`: a behaviour module that fires worn gauntlets and the torso item through the same server-side use path the player's `UseChannel` drives, so `Present` replicates the same way.
- Create a small per-item `NpcGearUsePolicy` asset (when to fire: range band, cooldown, needs a target or line of sight, hold duration), authored for the gauntlets NPCs are allowed to wear.

**Behaviour:**
- An NPC fires a worn gauntlet when its policy says so, and every machine sees the same presentation a player's use shows.
- The starting set is the gauntlets that make sense against a player: Repulsor Gauntlet (close shove) and Grappling Hook (close distance). Others stay unassigned until they get a policy.
- Items with no NPC policy are never drawn by `WornGearDraw`; roster validation reports them.

**Tests:**
- Policy selection (range band, cooldown).
- A module ticks a worn Repulsor at a target in range and not out of range.
- Roster validation flags worn gear that has no NPC policy.

### Task 11: NPC flight with a wing pack

**Files:**
- Create `Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs`, server-simulated. It deploys a worn wing pack, switches the agent from its NavMesh motor to a flight motor running `OrnithopterFlightModel` with the wing pack's config, steers toward a flight target, and lands (stows and returns to the NavMesh) on command or when grounded near a NavMesh point.
- Create `NpcFlightSaveable` if flight state must survive a save.
- Implement networking through the NPC's existing NetworkTransform.

**Behaviour:**
- A flying NPC keeps its `AgentTargeting`, so it can still acquire and shoot while flying.
- Its body pose uses the flight pose the player uses, if that pose is agent-compatible; otherwise a simple glide pose.
- A crash uses the ornithopter crash damage rule.

**Tests:** in EditMode, pure steering from flight state to a target converges without stalling, deploy and land state transitions are correct, and the motor handover restores the NavMesh agent.

### Task 12: Swarmers

**Files:**
- Add a `Swarmer` value to `RosterRole` (append only).
- Create `Assets/Game/Scripts/agents/Modules/Movement/SwarmModule.cs`: boids-style separation, alignment and cohesion around a swarm anchor, with an orbit radius and altitude band, dive attacks on a target, and pulling out above the target.
- Create `SwarmAnchor` (a transform or position provider: the sky city, a vessel, or a target).
- Sky roster: Swarmer members are Sky people guaranteed to wear a wing pack (the roster role forces the torso item) and to use `NpcFlightModule` + `SwarmModule`.
- Sky city: a swarm of N swarmers circles the city, spawned by a settlement-population-like spawner that places them in the air.

**Behaviour:**
- Swarmers never land in the city unless their flight is interrupted.
- When a hostile target is in sight, a few dive and attack while the rest keep circling.
- Tunables: swarm size, orbit radius, altitude band, separation, dive interval, attackers at once.

**Tests:** pure boids step keeps separation above the minimum, stays within the altitude band, dive scheduling respects the attackers-at-once cap, and the roster forces a wing pack for the Swarmer role.

### Task 13: Swarmers join war parties and escort sky groups

**Files:** Modify the war-party composition and spawn path from Tasks 4 and 7 so members can travel **seated** (in the vessel) or **flying** (swarmers anchored to the vessel), and add Sky war-party tiers with swarmers. Add Sky wandering group templates (vessel + swarmers, or swarmers only) to `NpcWorldSim`, wired by `RosterAuthoring.WireWorldSim`.

**Behaviour:**
- **War parties:** swarmers spawn with the vessel, anchor to it while it flies, re-anchor to the quarry when the vessel reaches its drop site, and attack.
- **Wandering groups:**
  - A small vessel cruises between sky sites (the sky city and other registered sites) with swarmers circling it.
  - Swarmer-only flocks roam the sky.
  - Folded groups move in straight lines at altitude, like Sand caravans.
- Swarmers count as fighters for Defeated.
- Folding despawns vessel and swarmers together, out of view.

**Tests:** composition splits seated and flying members by role, swarmers are counted in fighters, and wandering Sky templates seed, fold, advance and restore.

### Task 14: Part 2 docs and verification

- Docs: `BodyEquipment.md` (NPC wearers), `AgentSystem.md` (pointer), the Sky tribe doc (swarmers, wandering groups), and the `spacegame-tribe` skill (worn gear and swarmers). Add a new skill section on giving an NPC a gear-use policy.
- Play verification, host and client:
  - Rare geared nomads appear.
  - A Repulsor-wearing NPC shoves the player.
  - Swarmers circle the city.
  - A Sky war party arrives with swarmers that attack.
  - A wandering skiff with swarmers passes.
  - Save and reload mid-swarm.
