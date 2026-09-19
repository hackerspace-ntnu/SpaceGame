# Faction System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every humanoid agent belongs to a faction; nomads are split into tribes with their own people, mounts, vehicles and gear; each tribe keeps a goodwill toward the player crew that the crew's actions move; a hurt agent turns hostile and alerts its tribe; the Clankers (Red Planet Rampage's robot cowboys) are hostile to everything.

**Design:** [2026-09-07-faction-system-design.md](../specs/2026-09-07-faction-system-design.md). Read it in full first — the vocabulary, the three-layer resolver (§3) and the decisions (§11) are not repeated here. Eight of ten decisions are taken (per-player goodwill; Sand + Mechanics + Sky + Clankers; Clankers hostile to people only; tribes neutral, Sand owns the DuneFoil, Mechanics the crawlers; decay plus amends; worn-pack flight; Outlaws **and** Clanker outriders as hunters; new Clanker prefabs). Q9 and Q10 were answered the same day: tribes live in settlements and move between them, welcome is by goodwill (design §3.10), and the Clanker lore is settled (§3.7). **Only the tribes' real names are open**; assets use the working names and `factionName` is a display string, so nothing blocks. The user's vocabulary is **goodwill meter** (per faction, per player) and **aggression meter** (per agent) — use those words in code and docs.

**Architecture:** Keep `FactionDefinition` / `FactionRelationshipTable` / `EntityFaction` as the identity layer. Add a pure `FactionRelations.Resolve` that layers a per-agent grudge (`ProvocationModule`, exists), a per-faction goodwill ledger (`FactionGoodwillLedger`, new, server-owned, saved) and the authored stance (table row, else a new `defaultStance` on the faction). Wire the existing but uncalled `AlertBroadcaster` onto provocation and rebuild it on `EntityTargetRegistry`. Put tribe composition in a new `FactionRoster` asset the world sim draws from. NPC worn gear is a new `EntityBodyEquipment` reusing the player's `BodySlot`/`WornSeat` pieces. Clankers are a new builder over imported RPR art.

**Tech Stack:** Unity 6000.3.11f1, C#, NGO via `NetMessaging`/`NetArg`, Newtonsoft save adapters, NUnit EditMode, the `blender-model` skill for imported art.

---

## Where this actually is — verified against the repo 2026-09-16

The checkboxes below had drifted from the code, and **the phases were not done in plan order** —
Phase A and Phase 6 ran first and the cheap foundation everything else reads was skipped. Phases 0,
1, 2 and Task 3.0 were done on 2026-09-15/16 and the boxes below now match the repo.

| Phase | State | Evidence |
| --- | --- | --- |
| **A** Mock Clanker settlement | **done** bar the play/client check | `ClankerSettlementBuilder.cs`, `ClankerSettlement.asset`, generated into `Chunk_7_3` |
| **0** Data hygiene | **done 2026-09-15** | Four assets renamed keeping their GUIDs (`HumansFaction`, `SandTribeFaction`, `ClankerFaction`, `OutlawFaction`); Outlaws↔Humans Hostile row added; `PatrolRobot 2` moved off the player's faction; `EntityFactionWiring` gave the six unowned agents one; `FactionAssetTests` |
| **1** `defaultStance` + resolver | **done 2026-09-15** | `FactionDefinition.defaultStance` (+ `debugColor`→`hudColor`); `Get` falls through to §3.2 step 3; Clankers Hostile by default with two `Neutral` animal rows and three redundant rows deleted; `FactionRelations.Resolve` with the goodwill layer stubbed; `FactionRelationsTests` |
| **2** Alerts | **done 2026-09-15** | 2.1 was already done. 2.2 found the alert modules were in the builder's list and on **none** of the five prefabs — nobody had re-run it. Added `NoiseReceiverModule` (Gunshot investigates, Hurt aggros), rebuilt all five, savers via `SaveablePolicy`; `NomadAlertWiringTests` |
| **3** Goodwill + aggression meters | **DONE 2026-09-16** (3.0–3.3), bar the two-client check | `AggressionMath` + 17 tests; `ProvocationModule` is a 0–100 meter with `AggressionSettings`; inputs from damage, alerts, gunshots and the new `MenaceSensor`; `AggressionTelegraphModule` shows the Wary/Drawn bands and replicates them as `AgentAction.Band`; meter persisted. `GoodwillMath` (one-sided hysteresis) and `FactionGoodwillLedger` (on the `NpcWorldSim` object in `persistentScene`, Sand Tribe listed) with the Hit and Kill hooks live and `ResolveGoodwill` no longer a stub; **at war spreads by proximity to same-faction players** — crew in the world, team in versus, one rule. 3.3: `FactionGoodwillSaveable` (PATH C, player prefab) and `FactionGoodwillNetwork` (a targeted Rpc, not a NetMsg — NetMessaging has no unicast) |
| **4** Rosters and tribes (incl. **4.5 traders**) | **sub-project 1 DONE 2026-09-16** (rosters + war parties, code and tests only — carrying the client/host-and-client and save/reload verification debt until Task 11 Step 7 runs against a connected Unity Editor); **sub-project 3's Sky half DONE 2026-09-17** via the separate [2026-09-17-sky-tribe-and-vessels.md](2026-09-17-sky-tribe-and-vessels.md) plan (Tasks 1–8: `SkyTribeFaction`, `Rosters/SkyTribe.asset`, the sky city and its population, and NPC-flown war-party transports — see [SkyTribe.md](../../AI/systems/SkyTribe.md); that plan's own host/client/save-reload checklist is still pending); Mechanics tribe, `TerritoryZone` and 4.5 traders **not started** | `FactionRoster`, `RosterRole`, `RosterValidation`, `RosterDraw`; `Rosters/SandTribe.asset` + `SandTribeHostileLines.asset`; `WarPartyDirector`, `WarBook`, `WarPartyRules`, `NpcGroupComposition`, `GroupMembership`, `WarNotice`; `RosterAuthoring` (`Author Sand Tribe Roster`, `Wire War Party Templates`) chained into `NomadPrefabBuilder`; tests `RosterAssetTests`, `RosterDrawTests`, `WarBookTests`, `WarPartyRulesTests`, `WarPartyDirectorTests`, `WarPartyPersistenceTests`, `WarNoticeTextTests`, `NpcGroupCompositionTests`, `FactionGoodwillLedgerTests` (self-defence exemption) |
| **5** NPC worn gear, Sky flight | **not started** | no `EntityBodyEquipment`, no `NpcFlightModule` |
| **6** Clankers | **6.1 + 6.2 done, 6.3 not started** | Clanker imported with `THIRD_PARTY_NOTICES.md`, `ClankerBuilder.cs`, `Clanker.prefab`, `Clanker.asset` targeting profile, `ClankerSquadPlacer`, settlement garrison + 3 outriders. `Rosters/Clankers.asset` waits on Phase 4. No `ScavengeModule` |
| **7** Legibility | **not started** | no `FactionReadout` |
| **8** Docs | **not started** | no `docs/AI/systems/Factions.md` |

Not in the plan at all, and shipped: the **robot horse** (`RobotHorseBuilder`), which builds the wild
Fauna `RobotHorse` and the Clanker-ridden `ClankerOutrider` the settlement fields. Its rule —
**a mount carries, the rider shoots; no horse can attack anything** — is in
[AgentSystem.md](../../AI/systems/AgentSystem.md) and [Vehicles.md](../../AI/systems/Vehicles.md).

**Suggested next step:** Phase 4 sub-project 1 (rosters and war parties, per the
[2026-09-16 spec](../specs/2026-09-16-rosters-and-war-parties-design.md) and its
[implementation plan](2026-09-16-rosters-and-war-parties.md)) is code-and-tests complete — finish its
Task 11 Steps 6–7 (full EditMode run, then the host-and-client play checklist) once the Unity Editor
bridge is back. The Sky half of sub-project 3 is likewise code-and-docs complete (see above) with
the same host/client/save-reload checklist outstanding. Remaining: the Mechanics tribe, `TerritoryZone`
and 4.5 traders.

**Carrying verification debt.** Five pieces of netcode from 2026-09-15/16 have never been seen on a
real client: `AgentAction.Band`, Appa and Sandloper's `NetworkTransform`, the charged-shot
replication, `MenaceSensor` reading body facing on the server, the goodwill layer deciding
who targets whom, and 3.3's saver and client mirror. Extending
`MultiplayerAutotest.RunClient` with `Report(...)` calls for these is the cheapest way to stop
stacking on an unverified base — [INVARIANTS.md](../../AI/INVARIANTS.md) §1.

**Future, recorded 2026-09-16 at the user's request — plan only, not scheduled:** quest-granted
goodwill. Completing a quest for a tribe is an amends event into `FactionGoodwillLedger`, exactly as
trading is, and needs only a new `GoodwillEvent` when the quest system exists. Also: the user wants to
review what makes NPCs aggressive before per-tribe aggression tuning is designed.

**Also on the list, outside this plan:** enemy pathfinding — see `AI-01` in
[BACKLOG.md](../../BACKLOG.md), which records the three systems the symptom could live in and says
to get one reproducible case before picking one.

## Before you start

- **Branch.** Work is on `Feat/factions` (already checked out). One PR per phase; each phase leaves `main` shippable.
- **Docs to read in full before Task 1:** [`docs/AI/systems/AgentSystem.md`](../../AI/systems/AgentSystem.md), the `spacegame-agent` skill and its `reference.md`, [`docs/AI/INVARIANTS.md`](../../AI/INVARIANTS.md). Phase 3 also needs [`Persistence.md`](../../AI/systems/Persistence.md) + the `spacegame-persistence` skill; Phase 5 needs [`BodyEquipment.md`](../../AI/systems/BodyEquipment.md); Phase 6 needs [`ArtPipeline.md`](../../AI/systems/ArtPipeline.md) + the `blender-model` skill. Phase 2/3 netcode: the `spacegame-multiplayer` skill.
- **Verification commands** (from [`Testing.md`](../../AI/systems/Testing.md)):
  - Type-check: `python3 tools/typecheck.py --editor` → expect `No errors.`
  - Tests: Unity menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then `cat Temp/headless_tests.txt` → expect `FAILED=0` and a final `DONE` line. Queue only from a clean scene.
  - Docs: `python3 tools/docs_check.py --index` after every doc edit.
  - **Client verification is not optional.** Every phase ends with a two-machine check (MPPM clone or the batch-mode autotest). A phase seen working only on the host is not done — [INVARIANTS.md](../../AI/INVARIANTS.md) §1.
- **Every `Nomad*` and robot prefab is builder-owned.** Add components in `NomadPrefabBuilder` / the robot builders, rebuild, and read the prefab back. Hand edits vanish on the next run.
- **Casing.** Reference prefabs as `Assets/Game/Prefabs/Agents/...` (the builders' casing). Never rename the folder.

## Facts this plan is built on

Verified 2026-09-07 by grep; re-check at the task that touches them.

| Fact | Evidence |
| --- | --- |
| `AlertBroadcaster.Broadcast` has **no callers** | `grep -rn '\.Broadcast(' Assets/Game/Scripts/agents` → only `AgentActionRelay` |
| `AlertReceiverModule` sits only on `DeathmatchBot`, `PatrolRobot`, `PatrolRobot 1/2/3` | prefab grep |
| `NoiseReceiverModule.OnNoiseHeard` aggro branch has no relationship check | `Perception/NoiseReceiverModule.cs:111` |
| `PlayerFaction.asset` and `NPCFaction.asset` both carry `factionName: Robots` | asset grep |
| `PatrolRobot 2.prefab` references `PlayerFaction` (`e60e1b5f…`) | prefab grep |
| No `EntityFaction` on `Caravan/BountyHunter`, `creatures/Ostrich`, `creatures/HumanoidRobot`, `creatures/CrabWalker6`, `Vehicles/Ground/DesertCrawler` | prefab grep |
| `NetDamage.Apply` sends `source` in the request (`NetArg.With(source)`) so a client's hit is attributed server-side | `Gameplay/Health/NetDamage.cs` |
| `DuneOrnithopter.prefab` has no `AgentController`; `DesertCrawler.prefab` is a full AI agent (`AgentController`, `WanderModule`, `CrawlerToolModule`) | prefab grep |
| Caravan templates live inline on the `NpcWorldSim` object in `persistentScene.unity`: `Nomad Caravan`, `Sand Nomads`, `Bounty Hunters` (`bountyHunters: 1`), `Appa` | scene grep |
| Highest `NetMsg` id in use: 69 (`AgentActed`) — append after it | `AgentSystem.md` |
| RPR license is BSD-4-Clause with an advertising clause; body is `Assets/Models/Player/YiiHaw.fbx`, clips under `Assets/Animation/Y11-H4W/` | fetched 2026-09-07 |

## File structure

| File | Responsibility |
| --- | --- |
| Modify `Assets/Game/Scripts/agents/Faction/FactionDefinition.cs` | `+ defaultStance`, `+ roster`, `debugColor → hudColor` (keep the serialized name via `[FormerlySerializedAs]`). |
| Modify `Assets/Game/Scripts/agents/Faction/FactionRelationshipTable.cs` | `Get` consults `defaultStance` after the row lookup. |
| Create `Assets/Game/Scripts/agents/Faction/FactionRelations.cs` | Pure static resolver: grudge → goodwill → stance. No `Transform`, no scene. |
| Modify `Assets/Game/Scripts/agents/Faction/EntityFaction.cs` | `GetRelationshipWith` delegates to `FactionRelations`. |
| Create `Assets/Game/Scripts/agents/Faction/FactionRoster.cs` | The tribe composition asset + `RosterRole` enum + `RosterMember`. |
| Create `Assets/Game/Scripts/agents/Faction/FactionGoodwillLedger.cs` | Server-only MonoBehaviour in `persistentScene`; deltas, thresholds, bands, decay, events, replication. |
| Create `Assets/Game/Scripts/agents/Faction/GoodwillBand.cs` | Enum + pure `GoodwillMath` (band from value with hysteresis, delta scaling). |
| Create `Assets/Game/Scripts/Core/Persistence/Adapters/FactionGoodwillSaveable.cs` | Key `"factionGoodwill"`, world scope. |
| Modify `Assets/Game/Scripts/agents/Perception/AlertBroadcaster.cs` | Registry query by `Allied`, no physics, no layer mask. |
| Modify `Assets/Game/Scripts/agents/Perception/AlertReceiverModule.cs` | `ReceiveAlert` → `ProvocationModule.Provoke` when present. |
| Modify `Assets/Game/Scripts/agents/AI/Targeting/ProvocationModule.cs` | The per-agent **aggression meter** (`AddAggression`, inputs, bands, `calmRate`, `attackAt`); broadcast on reaching `attackAt`; report to ledger. `ProvocationSaveable.State` gains `aggression` (appended; key unchanged). |
| Create `Assets/Game/Scripts/agents/AI/Targeting/AggressionMath.cs` | Pure: band from value, gain per input, cooling. Tested without a scene. |
| Modify `Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Config/RobotSettlementRecipe.cs` (the class behind `SettlementConfig.asset`); create `Assets/Game/Scripts/agents/Faction/TerritoryZone.cs` | `owner` faction on a settlement recipe; a zone that raises members' aggression against unwelcome players (design §3.10). |
| Create `Assets/Game/Scripts/agents/Modules/Movement/ScavengeModule.cs` | Clanker loot behaviour: walk to what a beaten enemy dropped and take it (Phase 6 follow-up). |
| Modify `Assets/Game/Scripts/agents/Perception/NoiseReceiverModule.cs` | Relationship check on the aggro branch. |
| Modify `Assets/Game/Scripts/agents/World/NpcGroup.cs`, `NpcWorldSim.cs` | `tribe` on the template, `role` on the member spec, seeded roster draws, `Ensure` on spawn, record fields. |
| Modify `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` | Recipes per tribe: palette, faction, alert modules, worn gear. |
| Create `Assets/Game/Scripts/agents/Entity/EntityBodyEquipment.cs` (+ saver) | NPC worn slots over `BodySlot`/`BodySlotRules`/`WornSeat`. |
| Create `Assets/Game/Scripts/agents/Modules/Combat/NpcGauntletUseModule.cs` | Side-effect module firing a worn gauntlet at the target. |
| Create `Assets/Game/Editor/Agents/ClankerBuilder.cs` | Clanker prefab from the imported YiiHaw FBX. |
| Create `Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs` (+ `NpcFlightSaveable`) | Deploys a worn wing pack, swaps the motor to flight, lands and stows (Phase 5). |
| Create `Assets/Game/Scripts/Presentation/UI/HelmetHUD/FactionReadout.cs` | `IInteractionReadout` for the visor info box. |
| Create `docs/AI/systems/Factions.md` | The new system doc. |
| Create `THIRD_PARTY_NOTICES.md` | RPR BSD-4 text + asset list. |
| Tests under `Assets/Game/Editor/Tests/` | `FactionRelationsTests`, `GoodwillMathTests`, `FactionGoodwillLedgerTests`, `AlertBroadcasterTests`, `FactionAssetTests` (reads the **assets**), `RosterDrawTests`. |

---

## Phase A — Mock Clanker settlement *(user-ordered first step; code done 2026-09-07, scene generation pending)*

The first thing in the world that belongs to a faction. Placeholder buildings and a placeholder garrison; the Clanker bodies replace the garrison in Phase 6, the ownership hook (`TerritoryZone`) arrives in Task 4.4.

- [x] `Assets/Game/Editor/Environment/ClankerSettlementBuilder.cs` — `Tools/SpaceGame/Settlements/Build Mock Clanker Settlement` (and `+ Bake NavMesh`). Writes `Assets/Game/ScriptableObjects/Settlements/ClankerSettlement.asset` (a `RobotSettlementRecipe`: RefineryTower centre, RelayOutpost ring, LatticeOutpost eco hubs, MiningRigDerelict mid ring, boulders, `PatrolRobot`/`1`/`3` garrison, one `CowBotRocket`), finds the flattest 150 m disc 450–800 m from the `SpawnPoint` that clears every `TerrainFeatureSpawner` footprint, places a seeded (`1701`) `RobotSettlementGenerator` under a `ClankerSettlement` root in that chunk scene, generates, verifies the read-back, saves the scene, closes what it opened.
- [x] `Assets/Game/Editor/Tests/ClankerSettlementTests.cs` — the pure site score against known ground shapes; the recipe asset's slots (ignored until the asset exists).
- [x] Docs: `EditorTooling.md` menu row; `TerrainGeneration.md` (the placed generator, the stale-NavMesh gotcha, the mesa-is-invisible-to-raycasts gotcha, the `~0` mask gotcha).
- [x] **Ran it** (2026-09-07): the builder chose `Chunk_7_3` at (3614.8, 109.0, 796), ~800 m south of the lander; 1 refinery, 4 relay outposts, 2 lattice outposts, 1 mining rig, 5 patrol robots, 1 rocket. The pre-existing DuneFoil instance and terrain in that chunk are untouched (override line counts identical before and after). World NavMesh re-baked (511 sources, 48 chunks, 7 s); the refinery's decks carry mesh, so the buildings are in the bake. To commit: `ClankerSettlement.asset` (+meta), `Chunk_7_3.unity`, `WorldNavMesh.asset`, the baker fix below.
- [x] Two things the first run exposed and that are fixed: **(a)** every rock prefab is 100×-scaled and collider-less (three boulders 600–850 m wide hung over the town) — rocks removed from the recipe, `DEFECTS.md` row added; **(b)** the NavMesh baker baked the kinematic robots in as holes under their own feet — `WorldNavMeshBaker.IsBakeable` now skips anything under a `NavMeshAgent`, pinned by `WorldNavMeshBakerSourceTests`, gotcha in `NavMeshSystem.md`.
- [x] `ClankerSettlementTests`: 6/6 pass with the recipe present.
- [ ] Verify in play, host **and client**: walk ~600 m from the lander to the town; the garrison patrols and attacks on sight (they are `RobotFaction`, Hostile to players today); reload, the robots you killed stay dead.
- [ ] Known placeholders, on purpose: no turret ring (no turret prefab exists); `PatrolRobot 2` excluded (it ships on the player faction until Phase 0 fixes it); the rocket may park on a roof (generator raycast gotcha in `TerrainGeneration.md`).

## Phase 0 — Data hygiene (no behaviour change)

Fixes what is wrong on disk so later phases build on true data. Ships alone.

### Task 0.1 — Fix the faction assets

- [x] `PlayerFaction.asset` → `factionName: Humans`; rename file to `HumansFaction.asset` (`AssetDatabase.RenameAsset` or a git `mv` of file + `.meta`; the GUID must survive).
- [x] `NPCFaction.asset` → `factionName: Sand Tribe`; rename to `SandTribeFaction.asset`.
- [x] `RobotFaction.asset` → `factionName: Clankers`; rename to `ClankerFaction.asset`.
- [x] `BountyHunterFaction.asset` → `factionName: Outlaws`; rename to `OutlawFaction.asset`. Add one row: Outlaws↔Humans `Hostile`.
- [x] Update every string path: `NomadPrefabBuilder.FactionPath`, `EntitySystemSetup.cs` comments, the skill's temperament table, `AgentSystem.md`.
- [x] `PatrolRobot 2.prefab` → `ClankerFaction` (via its builder if one exists; grep `Assets/Game/Editor` for `PatrolRobot` first — if none, a one-off `SerializedObject` edit is acceptable and must be recorded in the doc's Gotchas).
- [x] Add `EntityFaction` (+ `EntityFactionSaveable`) to `BountyHunter`, `Ostrich`, `DesertCrawler`, `HumanoidRobot`, `CrabWalker6` through their builders. Factions: BountyHunter → `Outlaws`, Ostrich → `Fauna`, DesertCrawler → `Mechanics` once that faction asset exists in Task 4.3 (until then `SandTribe`, so it is at least targetable), the two robots → `Clankers`.
- [x] **Test** `FactionAssetTests`: loads every asset under `ScriptableObjects/Factions/Core/` and asserts unique, non-"Robots" `factionName`s and non-empty `ID`. Reads the asset, not the class (INVARIANTS: "a serialized field keeps its old value").
- [ ] Verify: play, walk up to a nomad, dev-print `EntityFaction.Faction.factionName` → `Sand Tribe`. Save, reload, same.

## Phase 1 — Default stance and the resolver

### Task 1.1 — `defaultStance`

- [x] `FactionDefinition`: `[SerializeField] FactionRelationship defaultStance = Neutral` with a tooltip that quotes the rule in design §3.2; `[FormerlySerializedAs("debugColor")] hudColor`.
- [x] `FactionRelationshipTable.Get`: after the row miss, apply §3.2 step 3 (either Hostile → Hostile; both Allied → Allied; else Neutral). Keep the index; the fallback runs only on a cache miss.
- [x] Set `defaultStance = Hostile` on `ClankerFaction.asset`. Add two rows: Clankers↔Fauna `Neutral`, Clankers↔Wildlife `Neutral` (people only — a row beats the default). Delete the now-redundant `Player↔Robot Hostile`, `Player↔NPC Neutral` and `Robot↔NPC Neutral` rows **only after** the test below passes with them removed.
- [x] **Tests** `FactionRelationsTests` (pure, `CreateInstance` factions with ids stamped by hand): no rows + Neutral defaults → Neutral; either Hostile default → Hostile; row beats default (Fauna vs Clanker → **Neutral** via the row); same faction → Allied; Fauna vs Sand → Neutral; Humans vs Clanker → Hostile with no row. Plus `FactionAssetTests` reads `GlobalRelationships.asset` and asserts the two Clanker animal rows exist and that no row pairs Clankers with a people faction.

### Task 1.2 — `FactionRelations.Resolve`

- [x] Static `Resolve(EntityFaction self, EntityFaction other)`: grudge (self's `ProvocationModule.IsProvoked && Aggressor.root == other.root` → Hostile) → goodwill (Phase 3; stub returns null now) → table.
- [x] `EntityFaction.GetRelationshipWith` calls it. Nothing else changes; `EntityTargetRegistry`, `AgentTargeting`, `FleeModule`, `AlertBroadcaster` all go through this one method already — grep `GetRelationshipWith\|IsHostileTo\|IsAlliedWith` and confirm no caller bypasses to `table.Get` directly (only `EntityFaction` may).
- [ ] Verify on host **and client**: shoot a Golem (Fauna), it fights back; a Clanker patrol attacks a nomad caravan on sight and the nomads shoot back (Phase 0 gave them a faction, Phase 1 gave Clankers a default); the same patrol walks past a DuneRat herd and Appa without firing. Profile `AgentTargeting.Reevaluate` in the bot arena at 16 solo factions before/after; budget: no measurable change.

## Phase 2 — Alerts that actually fire

**Status 2026-09-07 (pulled forward at the user's request after the first Clanker playtest):** Task 2.1 is done — `AlertBroadcaster` queries the registry for `Allied` receivers, listens to the new `AgentTargeting.TargetAcquired(target, seen)` event and passes on sightings only, `ProvocationModule.Provoke(target, announce)` announces a hit, `AlertReceiverModule.ReceiveAlert` goes through `Provoke(…, announce: false)` so the target sticks and nothing cascades, and `NoiseReceiverModule` skips allied instigators; `AlertChainTests` pins all of it. Task 2.2 is done for the Clanker (`ClankerBuilder`: broadcaster 60 m, receiver, `ProvocationModule`) and written into `NomadPrefabBuilder` (`ConfigureAlerts`: broadcaster 35 m with sightings off, receiver 19); the nomad prefabs pick it up on their next `Build Sand Nomad NPCs` run, which has not been done yet. Also landed ahead of Task 4.4: **`SettlementAlarm`** on the Clanker town root — intruder scan by stance, siren on every machine, defenders rallied on the server — as the first half of the territory rule (design §3.10); the goodwill half waits on Phase 3. And the Clanker sees further: a `Targeting/Clanker.asset` profile at 80 / 100 m and a 150° FOV.

### Task 2.1 — Registry-based broadcaster

- [x] `AlertBroadcaster.Broadcast`: replace `OverlapSphereNonAlloc` + `receiverLayers` with `EntityTargetRegistry.Query(myFaction, Allied, position, alertRadius, buffer)`; skip self; `TryGetComponent<AlertReceiverModule>`. Remove `receiverLayers` and `alliedOnly` (a renamed/removed field is fine here — nothing reads them back). Keep `alertRadius`.
- [x] `AlertReceiverModule.ReceiveAlert`: if `TryGetComponent(out ProvocationModule p)` → `p.Provoke(target)`, else `Targeting.ForceTarget(target)`. Never re-broadcast (design §3.5 cascade cap) — assert it in a test with three agents in a line.
- [x] `ProvocationModule.Provoke`: when `aggressor` changes to a *new* transform, call `GetComponent<AlertBroadcaster>()?.Broadcast(target, target.position)`. Not on every re-assertion frame, not on restore (`RestoreGrudge` passes a flag).
- [x] `NoiseReceiverModule.OnNoiseHeard` aggro branch: resolve `hurtEntity = origin's EntityFaction` (pass it through `Noise.Emit`'s existing `instigator`/`ignore` args or add a `subject` arg — check `Noise.cs` signature first) and require `self.IsAlliedWith(hurtEntity) && !self.IsAlliedWith(instigator)`.
- [x] **Tests** `AlertBroadcasterTests`: allied receiver in radius gets the target; neutral one does not; out-of-radius does not; receiver with `ProvocationModule` ends `IsProvoked`; no cascade.

### Task 2.2 — Put the modules on every tribe member

- [x] `NomadPrefabBuilder`: add `AlertBroadcaster` (radius 30 m), `AlertReceiverModule` (priority `Reactive − 1`, explicit — script-added modules keep priority 0), `AlertResponseSaveable`, `NoiseReceiverModule` (`aggroOn = Hurt`, `investigateOn = Gunshot`) + `NoiseInvestigationSaveable`. Rebuild all five nomads; read the prefab back and assert the components exist (the builder's existing `Verify` pattern).
- [ ] Verify on a client: shoot one sand nomad in a caravan of four; all four turn, the three you did not hit walk to your last-known position then engage; dialog prompt disappears on all four (`DialogInteraction.CanInteract` already reads the grudge). Reload the save mid-fight: still hostile, still converging.

## Phase 3 — The two meters

### Task 3.0 — Aggression meter (per agent)

- [x] `AggressionMath` (pure): `Gain(input, amount, settings)`, `Cool(value, dt, calmRate)`, `BandFor(value)` → `Calm / Wary / Drawn / Grudge` at 40 / 80 / 100. **Tests** `AggressionMathTests`: one full hit reaches 100; a 5 % hit does not; cooling stops at 0; a gunshot at 15 needs seven shots inside the cooling window.
- [x] `ProvocationModule`: `aggression` field, `AddAggression(AggressionInput, magnitude, Transform from)`; reaching `attackAt` calls the existing `Provoke(from)` (unchanged from there: leash, calm-down, re-assert). `HandleDamage` becomes one input among several. Settings live in one serialized `AggressionSettings` struct; `hitGain` is **1200**, calibrated so a hit worth a twelfth of max health still fights at once (the pre-meter behaviour) while a 5 % graze only makes the agent wary. `damageThreshold` stays (a floor under `hitGain`). Also records `LastGunshotFrom`/`HeardGunshotFrom`, which is what `MenaceSensor` reads.
- [x] Inputs wired: `NoiseReceiverModule` → `AddAggression(Gunshot)`; `AlertReceiverModule` → `AddAggression(AllyHurt)` — **but only for a faction that is not already Hostile toward the target**, or a Clanker patrol would hesitate instead of answering its own alarm; `MenaceSensor` (side-effect, 0.25 s sweep) is **brandishing** rather than aiming — see the revised design §3.3 row. It needs `InventoryItem.menacing` **and** a gunshot this agent heard within `brandishWindow`, and it reads the player's BODY facing, because a remote player has no camera on the server.
- [x] Bands drive the telegraph: `Wary` → `WatchModule` target + one `hostileLines` bark; `Drawn` → `IsAiming` + `StopAndFace` + "last warning" bark, `DialogInteraction.CanInteract` refuses (extend `IsFightingWith` to "Drawn or worse"); `Grudge` → the existing fight. Clankers and Outlaws skip `Wary/Drawn` presentation (`attackAt` reached by stance already).
- [x] `ProvocationSaveable.State` gains `aggression` (append; key `"provocation"` untouched); restore sets the value without re-triggering barks.
- [ ] Verify on a client: aim a gun at a nomad from 8 m — after 1.5 s it turns and barks, after ~4 s it draws, keep aiming and it attacks; look away in the wary band and it cools and goes back to work; reload while drawn, it comes back drawn.

### Task 3.1 — Goodwill maths

- [x] `GoodwillBand` enum {`AtWar`, `HostileOnSight`, `Wary`, `Friendly`, `Allied`} and `GoodwillMath`: `BandFor(value, previousBand, thresholds, hysteresis)`, `HitDelta(amount, maxHealth, perHitMin, perHitMax)`, `Decay(value, hours, ratePerHour)`. **Tests** `GoodwillMathTests` including the hysteresis case (−40 enters HostileOnSight, −30 does not leave it, −29 does).

### Task 3.2 — The ledger

- [x] `FactionGoodwillLedger` MonoBehaviour on the `NpcWorldSim` object in `persistentScene.unity` (same lifetime, same server-only pattern; static instance reset via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` — INVARIANTS "statics outlive the world").
- [x] Serialized: default thresholds, hysteresis, per-event deltas (design §3.4 table), decay rate, `crewFaction` (`HumansFaction`). Everything tunable in the Inspector.
- [x] **Per player.** Rows are keyed `(factionId, playerProfileId)`. Find the profile id PATH C in the persistence skill keys player-scoped state by (grep `SaveScope.Player` / the player-scoped saver's key type) and resolve it from an attacker's `EntityFaction` root through the same component. Record the exact type in the Factions doc.
- [x] API: `Report(FactionDefinition victimFaction, EntityFaction attacker, GoodwillEvent kind, float magnitude)` (attacker resolves to a player id or is ignored); `BandFor(FactionDefinition, playerId)`; `event Action<FactionDefinition, playerId, GoodwillBand, GoodwillBand> BandChanged`. Ignores factions with no `roster` and factions whose `defaultStance == Hostile` (design §3.7 Clankers; Outlaws likewise, they are not a tribe).
- [x] Decay: per in-game hour while loaded; on restore, apply `(now − lastChangeTime)` worth of decay once, so an absence counts (design §3.4).
- [x] Hooks: `ProvocationModule.HandleDamage` → `Report(Hit)`; `HealthReactionModule` death path → `Report(Kill)` guarded by `health.IsRestoring`; mount/vehicle kills via the same path (the mount has an `EntityFaction` after Phase 0). Spread to allies/enemies of the victim faction per the table.
- [x] `FactionRelations.Resolve` goodwill step: if `other` is a **player** entity and `self.faction.roster != null` → look up `(self.faction, other's playerId)`: `AtWar/HostileOnSight → Hostile`, `Allied → Allied`, else null. Symmetric for `self` being the player (so the player's own `EntityFaction`-based queries — the visor — agree).
- [x] **Tests** `FactionGoodwillLedgerTests`: a 10 % hit moves −2, a kill −25, cross-faction spread, decay, band events fire once per crossing, Clankers and Outlaws never move, Fauna never move, **two players have independent rows** (player A at −50 is Hostile-on-sight while player B at 0 is Wary against the same tribe).

### Task 3.3 — Persistence and replication

- [x] `FactionGoodwillSaveable` (key `"factionGoodwill"`, **player-scoped, PATH C**, not deferred): `{ factionId → { value, band, lastChangeTime } }` per player. Restored when that player binds, which is before any agent can target them. Follow the skill's PATH C recipe exactly (it survives regardless of which chunks are loaded).
- [x] ~~`NetMsg.FactionGoodwill = 70`~~ **— deliberately not done that way.** 70 is long taken (the catalogue is at 115) and, more importantly, `NetTo` has no unicast: Server/All/Others only. Sending one player their own bands over the channel means broadcasting everybody's to everybody, which is bandwidth spent to leak exactly what design §5 says each client should not have. The multiplayer skill routes "an answer for ONE player" to a NetworkBehaviour with a targeted `[Rpc]`, so `FactionGoodwillNetwork` on the networked player does it — the shape `NetworkedTeleport` already uses. Factions travel as an INDEX into the ledger's tribes list, because `NetArg` has no string field and `string.GetHashCode` is explicitly not stable across machines. Server sends `{ factionId hash, band }` **to the affected player's client** (`NetSendTo`) on `BandChanged`, and replays that player's bands when their player object binds. The host reads the ledger directly.
- [x] Clients keep a read-only mirror of *their own* bands (`FactionGoodwillLedger.BandFor` answers from the mirror when `!Network.Server`). Nothing on a client mutates.
- [ ] Verify with two clients: client A shoots nomads until its band flips; A's visor shows HOSTILE, client B's still shows WARY, the host's dev overlay shows both rows; the nomads chase A and ignore B stood beside them; A quits and rejoins, band restored and the player-scoped JSON has `"factionGoodwill"`.

## Phase 4 — Rosters and tribes *(working names until the real ones arrive)*

> **Redesigned 2026-09-16 — read [2026-09-16-rosters-and-war-parties-design.md](../specs/2026-09-16-rosters-and-war-parties-design.md) before building Tasks 4.1 or 4.2.**
> Phase 4 is split into four sub-projects there. Sub-project 1 (4.1 + 4.2) replaces two passages:
> design §3.4's "every caravan of that tribe routes toward the player" becomes **dedicated war parties**,
> and Task 4.2's template-level `quarry` field becomes a quarry on runtime **groups**. The war-party
> behaviour — the reckoning, self-defence exemption, catch-up and abandon rules — lives only in that
> spec. The task bullets below are kept for history; the new plan supersedes them.

### Task 4.1 — `FactionRoster`

- [ ] Asset + `RosterRole` + `RosterMember { role, prefab, weight }`, fields per design §3.6. Menu `Assets > Create > Factions > Roster`. Validate on `OnValidate`: every member prefab has `AgentController` + `EntityFaction`; every `handItems` entry is `EquipKind.Hand`; every `wornGear` entry is not.
- [ ] `FactionDefinition.roster` back-reference; the roster asserts `roster.faction == this` in `OnValidate` (`GDC-L1-CONTENT-0004`: validate on ingest).
- [ ] Create `Rosters/SandTribe.asset` from what the sand nomads already are: the five prefabs, `NomadOstrich`, Appa-with-saddle, the seven weapon artifacts from `NomadPrefabBuilder.WeaponArtifactPaths` (then delete that array from the builder — the roster owns it), the default `DialogPool`.
- [ ] `RosterDrawTests`: seeded draws are stable; weights respected over 10k draws; a role with no members logs an error and returns null.

### Task 4.2 — World sim draws from the roster

- [ ] `NpcGroupTemplate.tribe`; `NpcGroupMemberSpec.role` (spawn `prefab` when set, else draw). `NpcGroup.Record` gains `tribeId`, `rosterSeed` (append to the struct; old saves read zeros = "use the template's").
- [ ] `NpcWorldSim.Spawn`: after `NpcSpawn.Create`, `EntityFaction.Ensure(member, template.tribe, table)`; hand items / worn gear drawn from the roster and written to `EntityInventoryComponent` / `EntityBodyEquipment` (Phase 5) before `OnEnable` registers it.
- [ ] Set `tribe = SandTribe` on `Nomad Caravan` and `Sand Nomads` templates in `persistentScene.unity`; move the `Bounty Hunters` template to `tribe = Outlaws`.
- [ ] `bountyHunters` templates gain `quarry`: `AnyPlayer` (Outlaws) or `WorstGoodwill` (a tribe at war hunts the player who earned it; reads the ledger). `RefreshLead` picks the target accordingly.
- [ ] Verify: a caravan folds to a record and re-spawns with the same faces and guns; reload mid-journey, same.

### Task 4.3 — Two more tribes and the Outlaws

- [ ] `NomadPrefabBuilder`: `TribeRecipe` (palette materials, faction, roster) wrapping the existing `NomadRecipe`s; build `Nomad_<Tribe>_<Variant>` prefabs for Mechanics and Sky from the same FBX set with the tribe palette (art variants via `blender-model` if the user wants distinct silhouettes — cloth/accessory variations exist in `sand_nogs.blend`). Outlaws reuse the `BountyHunter` body.
- [ ] `MechanicsFaction.asset`, `SkyFaction.asset` (no rows — tribes are neutral to each other and to Humans by default; decided) + rosters. Sand roster `vehicles = [DuneFoil]`; Mechanics `vehicles = [DesertCrawler, RigWalker]` (decided); Sky `wornGear = [Wing Pack]`. `DialogPool` per tribe with lines from **Q10** — until answered, ship the default pool so nothing blocks.
- [ ] Move `DesertCrawler.prefab` to `MechanicsFaction` (Task 0.1 parked it on Sand).
- [ ] One caravan template per tribe; Mechanics use `DesertCrawler` as leader (it is already an agent — give it `FormationModule` leader + `NpcPassenger` seats on the deck via a spike task first: **Spike 4.3a**, half a day, "can a `DesertCrawler` lead a formation and carry two `NpcPassenger`s?" — if no, Mechanics ride ostriches until it can).
- [ ] Tribe home sites: all tribes start at `SiteKind.Camp` until the settlement work adds owned settlements (Task 4.4 gives it the hook).
- [ ] Verify on a client: three caravans of three tribes visible, each visually distinct at 100 m; an Outlaw posse hunts the crew and is neutral to a passing Sand caravan.

### Task 4.4 — Settlement ownership hook (design §3.10)

- [ ] `owner` (`FactionDefinition`, default null = unowned) on `RobotSettlementRecipe` — the class `SettlementConfig.asset` is an instance of, under `Scripts/World/ProceduralGeneration/Settlement/Config/` — and the generator stamps a `TerritoryZone { owner, radius }` on the settlement root it emits. The existing robot settlement → `owner = Clankers`. Read `TerrainGeneration.md` first; settlements are emitted at edit time, so the zone is a scene component, not a runtime spawn.
- [ ] `TerritoryZone`: server-only tick on an interval; `EntityTargetRegistry.All` filtered to player roots inside `radius`; for each player whose goodwill band with `owner` is below `Wary`, call `AddAggression(Trespass, trespassGainPerSecond × interval, player)` on every owner-faction member inside the zone. No physics, no trigger collider.
- [ ] Works on a moving root: put one on the Mechanics' lead crawler prefab so the walking city carries its territory (verifies the "settlement that moves" rule).
- [ ] **Tests** `TerritoryZoneTests` (pure part): welcome bands, radius, and that a Hostile-default owner (Clankers) never needs the zone.
- [ ] Verify on a client: with Sand goodwill at −30, walk into a Sand camp — guards turn, bark, draw, attack; leave, they cool; at +10 the same camp ignores you.

### Task 4.5 — Traders: the peaceful half of the goodwill loop

**Why it belongs in this plan rather than beside it.** Goodwill is currently a meter with only one
direction of travel: everything that moves it is something you did wrong. Trading is what makes it
worth *earning* — a tribe that likes you sells you things, a tribe that does not turns you away —
and without it the Mechanics' whole identity ("the tribe that would trade for salvage, which ties
them to the game's spine") is a sentence in the design and nothing in the game.

**The system already exists and is unused.** `Gameplay/Trading/` has `TraderInteraction` (asks
through the existing `DialogInteraction` question popup rather than being a second `IInteractable`),
`TraderProfile` + `TradeOffer` (stock as an asset, cloned per trader so one buyer does not empty
every trader sharing it), `TradeUI`, `TraderSaveable` and `TradingTests`, and it has a `NetMsg` id.
**No prefab or scene references any of it** — a grep for `TraderInteraction` finds only C#. So this
task is content and wiring, not a new system, and the first step is to confirm that by putting one
trader in front of a player.

- [ ] **Spike first (half a day):** add `TraderInteraction` + a `TraderProfile` to one sand nomad by
      hand, walk up to them, buy something, reload. Answers whether the unused code actually works
      before anything is built on it. If it does not, fix it here and stop — everything below assumes
      a working trade.
- [ ] `RosterRole.Trader` in the Phase 4.1 roster, and a `TraderProfile` per tribe on the roster
      asset. Sand sells water, rope, mounts and saddles; Mechanics buy salvage and sell tools and
      vehicle parts (the hull modules that tie them to the ship); Sky sell wing packs and scanners.
      Outlaws and Clankers get none — you do not haggle with either.
- [ ] `NpcWorldSim` draws one Trader per caravan and per camp from the roster, the same way it draws
      every other role. A tribe's trader is not a separate spawn path.
- [ ] **Goodwill gates the trade, and that is the loop closing** (design §3.4 bands):
      `AtWar`/`HostileOnSight` refuse outright (`TraderInteraction` declines with a line);
      `Wary` trades at a markup; `Friendly` at list price; `Allied` unlocks the roster's
      `alliedOnlyOffers`. Read through `FactionGoodwillLedger.BandFor(faction, playerId)` — **per
      player**, so a crew-mate who has not been shooting nomads can still buy for you. That asymmetry
      is worth playtesting deliberately; it is either the most interesting thing here or an annoyance.
- [ ] **Trading is an amends event.** A completed trade reports a small positive delta to the ledger
      (design §3.4's "decay plus amends"), so the way back from a bad reputation is to do business
      rather than to wait. Cap it per in-game day or it is a grind that buys forgiveness.
- [ ] **Multiplayer**: the trade itself is server-decided and already has a `NetMsg` id — check that
      `TradeUI` opens for the asking player only and that stock decrements are authoritative, or two
      players buy the same last item. Verify with two clients trading with one trader at once.
- [ ] **Tests** `TraderGoodwillTests` (pure where possible): each band's price multiplier and refusal;
      allied-only offers hidden below Allied; two players with different bands get different prices
      from the same trader; a trade reports amends once and is capped.
- [ ] Verify on a client: buy from a Sand trader at `Friendly`; shoot a nomad until `HostileOnSight`;
      the same trader refuses you and the one in the next camp does too, while your crew-mate still
      trades normally. Reload — stock and goodwill both survive.

**Design check before building the price bands:** `ECON` and `MON` in the constitution, plus
`GDC-L1-SYS-0008` (sources, sinks and flows) — a trader who buys anything at any volume is an
infinite sink and the fastest way to flatten an economy.

## Phase 5 — NPC worn gear and Sky flight *(Option B decided: the nomad wears and deploys the pack)*

### Task 5.1 — `EntityBodyEquipment`

- [ ] Three `BodySlot`s, `BodySlotRules` for what fits, `WornSeat.Apply` on the humanoid bones, `WornVisual` form `Worn`. `TryWear(InventoryItem)`, `Remove(BodySlot)`. On death → `EntityLootTable` gets the worn items too.
- [ ] `EntityBodyEquipmentSaveable` (key `"npcWorn"`, item GUID per slot). Wire.
- [ ] `NpcGauntletUseModule` (side-effect, `ClaimsMovement = false`, fires the worn item's `Use` on the entity's channel like `NpcItemUseModule` does for the hand) — only for gauntlets whose `InventoryItem` opts in via a new `bool npcUsable` on the asset.
- [ ] Verify on a client: a Sky nomad walks with a wing pack on its back; kill it; the wing pack is on the ground and you can wear it. Reload before killing: still worn.

### Task 5.2 — Spike: the motor swap

- [ ] **Spike 5.2a** (budget: one day, throwaway branch): on a sand nomad, at runtime, disable `NavMeshAgent` + `NavMeshAgentMotor`, add an `OrnithopterFlightMotor` to the same body, feed it `MoveIntent`s from `AirWanderModule`, then land (`NavMesh.SamplePosition` below, `NavMeshAgent.Warp`, re-enable) — on a **client** as well as the host. Questions the spike must answer in writing: does `OrnithopterFlightMotor` run without a seated rider; does the swap leave exactly one transform writer per phase (INVARIANTS "something else already owns that transform"); does `NetAuthority.simulationDrivers` need the flight motor listed; does the body replicate cleanly through the swap. If the answer to any is a hard no, **stop and report** — the mounted-aircraft alternative (design §3.8) needs a sign-off before it replaces this.

### Task 5.3 — `NpcFlightModule` and the Sky tribe

- [ ] `NpcFlightModule` (Override priority, `ClaimsMovement = true` only while airborne): triggers — a `Fly` task, or provoked with the aggressor inside `takeOffDistance`, or a goodwill-Hostile player in sight. Deploy: `WornVisual` → spread form, motor swap per the spike, `NavMeshAgent` off. Fly: `GoalTravelModule` / `AirWanderModule` drive the flight motor; `FormationModule` gets a `flying` shape. Land: a `Land` task step or arrival → sample, descend, `Warp`, swap back, fold. Serialized: `takeOffDistance`, `cruiseHeight`, `landingSampleDistance`.
- [ ] Replication: `AgentAction.Deploy` / `Stow` through `AgentActionRelay` (presentation only). Late joiner: the worn pack's replicated/saved form decides, never a missed message.
- [ ] `NpcFlightSaveable` (key `"npcFlight"`): `flying`, plus the transform the existing `TransformSaveable` already stores. On restore with `flying = true`, come back airborne with the pack deployed and the flight motor active.
- [ ] `NomadPrefabBuilder` Sky recipe: `EntityBodyEquipment` with the Wing Pack pre-worn, `NpcFlightModule`, `FleeModule` **off** (Sky flees upward, not along the ground), `AirWanderModule`, both saveables. Sky roster tasks include `Fly` between sites and `Land` at them.
- [ ] Verify on a client: a wing of three takes off from a camp, crosses to another site in formation, lands, walks; shoot one in the air, it falls, the pack is on the corpse, you wear it and fly; reload mid-flight, they are still flying; a late joiner sees deployed packs.

## Phase 6 — Clankers *(decided: new prefabs alongside, patrol robots retired in a later PR)*

**Status 2026-09-07:** Tasks 6.1 and 6.2 are built — `Assets/ThirdParty/RedPlanetRampage/` (body + nine clips + licence, from commit `25835ac0` of a sparse clone at `../Red-Planet-Rampage`), `THIRD_PARTY_NOTICES.md`, `ClankerBuilder`, `ClankerPrefabTests`, and `ClankerSettlementBuilder` now garrisons the town with `Clanker.prefab`. `Build Clanker Prefab` ran to completion (body scaled 0.332 → 3.20 m, stride 4.89 m/s; verified: animator on the rig root, palette materials, network hash, save id, ragdoll wired) — after parking the editor behind `SyncMenu`'s modal dialog for ten minutes; the builder calls `Sync` now. The town was rebuilt with five Clankers as its garrison and the NavMesh re-baked (511 sources; all five stand on mesh, no holes). `ClankerPrefabTests`: 8 of 8 pass (the first run's one failure was a test bug reading the controller through the prefab reference; fixed). **Playtest 1 (user, same day):** "animation didn't work" — root cause: the second build's delete-and-recreate of the controller lost every state (gotcha in `EditorTooling.md`); the builder now rebuilds it in place and asserts the states off disk. Also per the playtest: the built-in pistol ray is gone; the Clanker now rolls a real `basicgun`/`GravelBlaster` at spawn (`NpcRandomLoadout` + `EntityEquipmentController` on `DEF-hand.R` + `NpcItemUseModule`) and drops it on death (`EntityLootTable`). `ClankerSquadPlacer` puts three Clankers ~75 m from the spawn point (`Tools > SpaceGame > Agents > Place Clanker Squad Near Spawn`) so the robots can be met without the hike. After the fix the full sequence (build → place squad → bake) was run twice in a fresh editor and the controller kept its states each time; an edit-mode drive of the prefab's Animator at SpeedY 4.5 plays the walk clip at 0.92 weight and rotates the foot bone 25°, so the rig animates. The one corruption seen after the in-place fix (17:01) coincided with a corrupted `Library/Artifacts` entry the same editor session later died on, and could not be reproduced afterwards; the read-back assert in `BuildController` is what stands guard now. **Robot horse (same day, user request):** the user's `horse1.blend` became `_Source~/models/creatures/robot_horse/robot_horse.blend` through `robot_horse_rig.py` (rigid per-island binding replacing the broken auto-weights, hooves onto the cannon bones, IK gone), `robot_horse_anim.py` (Idle/Walk/Run/TurnL/TurnR, in place, axis-probed) and `robot_horse_export.py`; `RobotHorseBuilder` builds `RobotHorse.prefab` (wild, Fauna, full wildlife stack + saddle stack, born saddled via the new `SaddleSocket.startSaddled`) and `Robots/ClankerOutrider.prefab` (Clanker faction, patrol/alerts/charge, a Clanker seated by `NpcPassenger`; the Clanker's controller gained an `IsSeated` → crouch state because `MountedRiderPose` needs a Humanoid). Mounted gallop 13.9 m/s, derived from the run clip's stride × 1.8 scale × 1.4 playback. The town recipe gained `outriderPrefabs`/`outriderTotal` (2) and the squad placer drops a saddled stray horse 16 m from the squad. Tests: `RobotHorsePrefabTests` (6), a seated-flag case in `NpcPassengerTests`, an outrider check in `ClankerSettlementTests`. **Settlement population (same night, user request):** `SettlementPopulation` on the town root refills the Clankers to a cap of 14 one wave (≤2) a minute, Clankers 3:1 outriders, spawned via `GameServices.World.Spawn` in the patrol ring away from players, holding while the alarm is raised; pure `SettlementPopulationLogic` + 4 tests. **Playtest 2 (2026-09-08, user):** "Clankers aren't wandering, don't see players unless right next to them, must feel like a threat; more spawns; take-down should be hard; posses that roam; more artifacts." Found in the log: the spawner's `World.Spawn` refused 29× as "called on a client" (`Network.Simulates` says yes on clients for scene objects → new `Network.Decides`), and by reading: `PerceptionModule` aimed its sight line at the target's origin = the ground (`AimPointOf` now aims at the body; `PerceptionAimTests`). Then: Clankers carry a `FormationModule` and every placer names bands (settlement groups, the spawn posse with a 120 m-roaming leader, spawner waves); sight 110/140 m, FOV 170, health 160, cooldown 0.9, patrol 35 m with 2–6 s waits; seven guns (the nomads' list) and a carried artifact from ten-or-nothing in a second bag slot; town garrison 4–5 bands of 3–4, three outriders, cap 26 with three every 45 s. **Open:** play verification on host and a client (rider seat height on the crouching Clanker is a first estimate, `RobotHorseBuilder.ClankerSeatDrop`; the standing-still report could not be reproduced from the code — if it persists on the host, the next thing to check is `NavMeshAgentMotor` parking at Awake). **Option for later:** RPR's own textures exist (`Assets/Textures/Player/YiiHaw_{Albedo,Normal,Metalness,Base_Normal}.png` in the clone) if the palette look is not wanted — they were not copied because RPR's materials need its dither shader.

### Task 6.1 — Import

- [ ] Clone RPR at a pinned commit; copy `YiiHaw.fbx` and the `Y11-H4W` clips into the `.blend` source library via the `blender-model` skill (the ArtPipeline doc forbids a bare FBX in `Assets/`); apply the project palette; export through the model's own script; confirm `avatar.isHuman` (or set up as Generic with `handBoneNameHints` if the rig is not humanoid — check first).
- [ ] `THIRD_PARTY_NOTICES.md` with the full BSD-4 text, the copyright line, the asset list and the advertising acknowledgement; add the acknowledgement to the in-game credits screen (`UI.md` says menus are built in C# — find the credits page or add one line to the main menu footer).
- [ ] Animator: map `rig.001_Walk/idle/back/SideStep*` onto the `SpeedX`/`SpeedY` blend contract; borrow `Hurt`/`Die`/shoot from the existing robot controller or author two clips.

### Task 6.2 — `ClankerBuilder`

- [ ] Copy `NomadPrefabBuilder`'s shape. Stack: `NavMeshAgent` + `NavMeshAgentMotor` (speed = run), `AgentController`, `EntityFaction` = Clankers, `HealthComponent` + `HealthReactionModule` + `EntityLootTable`, `EntityInventoryComponent` + `EntityEquipmentController` + `NpcItemUseModule` with the robot pistol, `PatrolModule` fallback, `ChaseModule`, `SearchModule`, `PerceptionModule` (set `occlusionLayers`), `AlertBroadcaster` + `AlertReceiverModule`, `NoiseReceiverModule`, `AgentGroundConform`, `SceneTracked`, all saveables, `NetworkObject` + `NetworkedHealthComponent` + `NetAuthority`; `Sync Network Prefabs`. No `ProvocationModule` warning telegraph — Clankers do not warn.
- [ ] `Rosters/Clankers.asset`: roles `Patrol` and `Outrider` (same body, the outrider with a longer `PerceptionModule` range and a `SearchModule`); `vehicles`: `CowBotRocket` as dressing; a hostile `DialogPool` of barks (they talk; they do not converse — `DialogInteraction` absent).
- [ ] `SettlementConfig.robotPrefabs` → Clanker prefabs; two caravan templates: a Clanker patrol and a Clanker outrider posse with `bountyHunters = true`, `quarry = AnyPlayer` (decided: Clankers hunt too).
- [ ] Verify on a client: a Clanker patrol attacks the crew and a nomad caravan on sight and ignores a Golem; the outriders track the crew across a chunk boundary; two Clankers alert each other; kill one, loot its gun, reload, it stays dead and the gun stays looted.
- [ ] Follow-up PR (after client verification): delete `PatrolRobot`, `PatrolRobot 1..3`, `DeathmatchBot` or repoint `MatchManager.deathmatchBotPrefab` to the Clanker; remove their network-prefab entries; update `AgentSystem.md`'s prefab list.

### Task 6.3 — Clankers take what you carried (lore: they hunt artifacts)

- [ ] `ScavengeModule` (Ambient priority) from the agent skill's worked example, plus the missing NPC pickup path it names: on arrival, `EntityInventoryComponent.TryAddItem` and despawn the world item through the netcode path, authority-gated inside `Tick`. Only while `AgentTargeting` has no target.
- [ ] Trigger: the Clanker's `HealthReactionModule`/kill event or `EntityLootTable` drop of something it was fighting — it walks to the drop. Also ambient: any loose `ScanClass.Item` within `searchRadius` (they are looking for artifacts).
- [ ] A Clanker that has picked up a usable item holds it (`EntityEquipmentController` re-equips on `OnSlotChanged` already) and it drops again when the Clanker dies — the loot loop closes.
- [ ] Verify on a client: die to a Clanker, it walks over and takes your dropped artifact, kill it later and the artifact drops; reload in between, it still holds it.

## Phase 7 — Legibility

- [ ] `FactionReadout : IInteractionReadout` on tribe/Clanker prefabs (via builders): label = faction name, value = resolved relationship to the *viewer's* `EntityFaction`, colour = `hudColor`. Shows in the visor info box; verify it reads `SAND TRIBE · WARY` then `· HOSTILE` after a shot.
- [ ] Threshold bark: `FactionGoodwillLedger.BandChanged` → nearest live member of that faction in 40 m of any crew member says a `hostileLines` entry through `ChatterModule` (it already ticks on every machine as an `IPresentationModule`) and the visor warning banner shows one line for 4 s.
- [ ] The pre-attack telegraph is the aggression meter's `Wary` and `Drawn` bands (Task 3.0); confirm here that a one-shot kill from full calm still gets at least the `Drawn` pose for one frame before the fight, and that Clankers and Outlaws skip it entirely.
- [ ] Verify on a client that the bark is heard once, by everyone, and the banner appears on the client that crossed the threshold.

## Phase 8 — Documentation and closure

- [ ] `docs/AI/systems/Factions.md`: frontmatter (`layer: characters`, `paths: [Assets/Game/Scripts/agents/Faction/, Assets/Game/ScriptableObjects/Factions/]`, `reads_with: [AgentSystem, Persistence, Multiplayer]`, symptoms from every bug hit in Phases 0–7 phrased as what was *seen*), sections Model → Key types → Flows → Multiplayer → Persistence → Gotchas → Extending ("add a tribe", "add a Clanker variant").
- [ ] `AgentSystem.md`: move the `EntityFaction`/table rows to point at `Factions.md`; delete the "WildlifeFaction is already Hostile" and "NPCFaction" wording that Phase 0 made untrue; add the alert-through-`Provoke` gotcha.
- [ ] `spacegame-agent` skill: temperament table → faction names from Phase 0, `defaultStance` row, "peaceful = zero rows **and** Neutral default".
- [ ] `docs/Human/the-systems.md` entry for Factions; `04-creatures-and-people.md` "Factions, and who attacks whom" rewritten for tribes and goodwill; `GLOSSARY.md` entries: tribe, goodwill, stance, grudge, roster, Clanker.
- [ ] `python3 tools/docs_check.py --index` clean.
- [ ] Record in `DEFECTS.md` anything found and deliberately left (e.g. the prefab folder casing).

## Verification matrix (every phase, before its PR)

| Check | How |
| --- | --- |
| Type-check | `python3 tools/typecheck.py --editor` → `No errors.` |
| EditMode suite | headless run, `FAILED=0` |
| Host + **client** | MPPM clone or autotest; the specific scenario listed at the end of each task |
| Save round-trip | quit/reload; grep the world JSON for the saver key (`"faction"`, `"provocation"`, `"factionGoodwill"`, `"npcWorn"`) |
| Prefab read-back | builder `Verify()` asserts the components it added exist on disk |
| Docs | `docs_check.py --index` |

## Order and dependencies

```
Phase A (mock town) ─► Phase 6 (Clankers) ──────────────────────────┐
Phase 0 ─► Phase 1 ─► Phase 2 ─► Phase 3 ─► Phase 4 ─► Phase 5 ───┴─► Phase 7 ─► Phase 8
```

Phase A and Phase 6 are the user's chosen first thread: the town, then the Clankers in it. They touch settlement tooling and a new prefab, not the faction code, so Phase 0–3 can run alongside them on a second branch.

Phases 0–3 need no answers and are pure improvement over today (Clankers hostile to all, alerts working, goodwill measured but only surfaced through targeting). Phases 4 and 6 can run in parallel on two branches once §11 is answered. Phase 7 needs both.
