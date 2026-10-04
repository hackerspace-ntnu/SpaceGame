# Settlement Expeditions, Phases 0–1 — progress

Plan: [2026-10-03-settlement-expeditions-phase-0-1.md](2026-10-03-settlement-expeditions-phase-0-1.md).
Spec: [2026-10-03-settlement-expeditions-design.md](../specs/2026-10-03-settlement-expeditions-design.md).
Nothing is committed; the user reviews and commits.

## How this run works

- One subagent per task writes code and tests; the orchestrator reviews each diff.
- **Editor access is batched.** On 2026-10-03 22:45 the Editor was in Play mode and one bridge call
  hung for 30 min, so agents do file-level work only (code, tests, `typecheck.py --editor`) and list
  their Editor steps (menus to run, scene/prefab writes, Editor-only tests). The orchestrator runs
  those steps when the Editor is idle, and records them below.
- Pure tests run without the Editor through a scratchpad runner that compiles like
  `tools/typecheck.py --editor` and executes the NUnit fixture under Unity's .NET. Tests that touch a
  native Unity object need the Editor's `HeadlessTestRunner`.

## Status

| Task | Status | Verified (how) | Deviations | Open |
|---|---|---|---|---|
| 0.1 Settlement and resident identity | code done, reviewed; Editor steps pending | typecheck --editor clean; `ResidentAssignmentTests` (incl. new `Assign_StampsSourcePrefab_ForEveryResident`) need the Editor; docs_check 0 errors | `SaveableEntity` scene-stamping branch of `OnValidate` extracted unchanged into public editor-only `StampSceneIdentity()` (reuse, not copy); no `SaveableEntity` added to the settlement PREFAB asset (`astronauts_settlement.prefab`) — added on the scene instance only; settlement entity is **`SaveScope.External`** (orchestrator decision: state lives in the director; World scope would restore the settlement pose and capture child savers); an already-open dirty chunk scene is edited but NOT saved (reported); chunk scene paths resolved case-insensitively (config says `Scenes/World`, disk `Scenes/world`); Residents.md + Persistence.md gotchas added | settlement-id save/reload + client run belong to 1.7 |
| 0.2 Group-sim hooks | code done, reviewed; Editor tests pending | typecheck --editor clean; puretest: `ExpeditionGroupTests` 5/7 (2 JSON tests need the Editor: Newtonsoft strong-name load), `NpcWorldSimTests` 6/6, `WarPartyRulesTests` 12/13 (pre-existing `TrailFix_StaysWithinFuzz` uses UnityEngine.Random) | `PlannedMember` is top-level in `SpaceGame.Agents`; war predicate is `NpcGroup.IsOwnedByWar = IsWarParty && (Owner == "" or "war")` (plan's form would adopt a released party with an empty quarry); `ReportSighting` skips `IsDirected` groups (its filter EXCLUDES war parties — plan's literal form would invert it); `WarPartyTemplateFor` unchanged (searches templates; the expedition template must not be `bountyHunters`); folded band speed is `FoldedSpeed()`; `WarPartyDirectorTests` gains one owner assertion; second fixture `ExpeditionGroupSimTests` (Editor) | No read-back for members DESTROYED while spawned (chunk unload) and a non-war group is never `WipedOut` → director must treat "stamped but never read back" as destroyed and prune `PlannedOverride` (spec §17 inherited bug); no read-back on `DisbandGroup` — the director reads members itself at the homecoming swap; hooks are null on a restored band until the director adopts it; posed spawns are still NavMesh-sampled (may lift a resident ~0.26 m — watch in 1.5's pop test) |
| 0.3 Expedition data and roles | code done, reviewed; Editor steps pending | typecheck --editor clean; puretest `Validation_FlagsGoalWithOneWarrior` PASS; 3 tests need the Editor | `ExpeditionValidation` in its own file `Data/ExpeditionValidation.cs`; extra checks (one role per slot, min ≤ max, last stage ReturnHome, no empty kit row); tuning adds `varietyPenalty` 0.5, `decisionInterval` 0.5 s; clock fields in HOURS (departHour 7.5, dawn 6, dusk 19); musterMinutes 30, observeRadius 150, handoffObserveRadius 400, searchRing 150–300 m; content authored by menu `Tools/SpaceGame/Expeditions/Author Expedition Content` (`ExpeditionContentMenu.cs`), not hand-written YAML; no "short spear" exists → Warrior `Tool_Spear_Bone` + belt `Tool_SignalHorn`, Scout `Tool_Spear_Stone` + tool `Tool_Spyglass` | `ShippedProfiles_HaveNoProblems` fails on "musterUse not set" until 0.4; `ExpeditionCatalog.Instance` caches an empty catalog if asked before the asset exists (until domain reload); git tracks both `Scripts/Agents/` and `Scripts/agents/` (pre-existing casing drift) — new files land under `agents/` |
| 0.4 Muster spot | code done, reviewed (one revision); Editor steps pending | typecheck clean; puretest `SettlementMusterTests` 9/9; `DayPlannerTests` + `ExpeditionAssetTests` need the Editor; docs_check: 1 error in another session's `HumanoidAnimation.md` (over budget), none ours | No gate exists, so the spot is placed by rule (see Log). Rule: the road out is the lane whose trail reaches farthest; the spot stands `musterInset` (5 m, new tunable) back along the street from where its PAVING ends, facing along the last 5 m of paving. `SettlementMuster` (pure `TryChoose`/`TraceStreets`, `Find`, `TryPlace`), placed in `Settlement.Populate` (Generate) and by menu `Tools/SpaceGame/Expeditions/Place Muster Spots` (existing settlements; traces `Generated/Streets` pieces classified by prefab via `SettlementStreetStyle.PavedPieces()/SurfacePieces()`); `SettlementSociety.TryMusterPose` (Try-form, not `MusterPose`) + `AssemblyPlace`; `KindOf` public; `HouseRoom` refuses Assembly spots; validator: no-spot + unreachable errors; `ExpeditionValidation` flags a musterUse whose role isn't Assembly; `Muster.asset` hold cue `bored` (no stand/attention cue exists; `listen` is a counter lean); touched `SettlementSpot.SetUse`, `SettlementLayoutResult.lanes`, `SettlementStreetLayout`, `SettlementStreetStyle`; docs: Residents.md, SettlementStreets.md | Expected Chunk_6_3 pose about (2997.2, 114.3, 535.6), facing about 200 deg (the slab row runs at 200 deg). Validator may false-error right after a plain Generate (baked NavMesh stale). Places hold one resident: the performer fans the band out with `MusterRow`. |
| 0.5 Warrior quota | code done, reviewed; user APPROVED the picks; Editor steps pending | typecheck --editor clean; puretest `ExpeditionRulesTests` 9/9 | Recruits become the settlement's **Guard** (no post, standing duty), not "the first Warrior archetype the body suits" (that is TowerGuard here: 3 recruits on one tower post); `ResidentAssignment.UsableArchetypes` made public (reuse); small-settlement quota = equal bed bands across 6–8 (0–13→6, 14–26→7, 27–39→8, ≥40→12); "lowest nerve deficit first" read as highest nerve first + seeded tiebreak; seed = `LineTable.IdOf(SettlementId)`; extra report-only **Preview Warrior Quota** menu | A world saved BEFORE Apply keeps the recruits' old Miner/Scout tools (`ResidentCarry` fills only an empty bag) — Residents.md gotcha in 1.8; recruits keep their old coworker bonds |
| 0.6 Site and settlement catalog | code done, reviewed; Editor steps pending | typecheck --editor clean; puretest `WorldSiteCatalogTests` 3/5 (2 scene-scan tests need the Editor) + `WorldSiteRegistryTests` 7/7 | `RosterEntry` in its own file (`agents/Expeditions/RosterEntry.cs`); `SettlementEntry.hasMuster` flag added (no invented pose); `adult` always true (nothing marks a child yet); `bonds` = `Resident.CloseTo()` indices (family + friends); merge/clear run in `WorldStreamer.Awake`/`OnDestroy` (owns the active config); new shared helper `Editor/World/WorldChunkScenes.cs`; `WorldSiteMarker.SiteId` now calls `EnsureId()`; catalogs at `Assets/Game/Settings/<Config>Sites.asset` | **No `WorldSiteMarker` exists in any chunk scene** (the only one is on `SkyCityFleet.prefab`, placed nowhere since 8e7ef62a) → the plan's "plus the Sky City site" won't hold, and `TryFindByName("Sky City")` finds nothing today (pre-existing); `ExpeditionAuthoring.ChunkScenePaths` duplicates `WorldChunkScenes` → dedupe after 0.5; docs owed: TerrainGeneration.md (catalog, MergeCatalog, 10 kinds, Clear on unload, bake menu, 2 gotchas), WorldStreaming.md (`siteCatalog`) |
| 0.7 Harpoon spike | todo | | | stop-and-ask: report before deleting |
| 1.1 Records and pure rules | code done, reviewed | typecheck --editor clean; puretest `ExpeditionRulesTests` + `RotationTests` 32/33 (`Records_RoundTripThroughSaveSerializer` needs the Editor: Newtonsoft) | Records are classes (`StageRecord` a struct), so `Advance(band, now, elapsed, arrived, tuning)` takes the class, not `ref`; added `waypointsDone`; `StageStep` adds `Continue`; `NextBandDue(IReadOnlyList<ExpeditionRecord>)` over core `(bandsUnderway, onlyBandHeadingHome)` (the plan's signature can't see ReturnHome); `Announced` counts as underway; pure cores beside every asset overload (`GoalOption`, slot-based picks); `MusterRow`/`RoadPoint` pulled forward from 1.5; `musterSpacing` 1.2 m tunable; rolls salted via `RosterDraw.Hash`; clock = `DayNightCycle.GameMinutesNow` (minutes since day-0 midnight; minute-of-day = mod 1440) | Decided for 1.2: `restUntilDay = homeDay + restDays + 1` (spec: back MORE than restDays ago); append `Vector3 target` to the record (travel destination / search-ring centre, stable across reload); the roster snapshot must drop the dead (no `dead` flag); a Halt begun in daytime waits for the next dawn (early camp), a Phase 2 tuning concern; `/exp days` stalls at the first Travel unless the director moves the folded group / reports arrival |
| 1.2 Director and save | code done, reviewed (one fix); Editor steps pending | typecheck --editor clean; puretest `ExpeditionDirectorRulesTests` 9/9 + rules/rotation green; 8 `ExpeditionPersistenceTests` need the Editor | Deaths come from each stand-in's `HealthComponent.OnDeath` (hooked in `MemberStamp`), NOT "stamped but never read back" (a streaming destroy must not kill a resident); dead removed from `PlannedOverride`; all dead → Lost; `MemberStamp` applies the record's health before spawn (folds no longer heal) and maps plan index ↔ member index; `IsAway` = members of Out/Returning bands (+ dead of unretired finished bands); Departing-before-hand-off is NOT away (test renamed `IsAway_TrueOnlyForMembersOfBandsPastTheHandOff`); finished records kept only while they hold dead (performer applies, then `Retire`); restore resolves Departing→Out (folded at handoffPoint), Returning→Home; **post-load hold released on `WorldSaveStore.OnSceneHydrated(Persistent)`, not `SaveManager.OnLoadApplied`** (that never fires when no player has a record — orchestrator-requested fix); band id = group id = `exp:<settlementId>:<rotation>`; Travel destination = seeded catalog site of `travelSiteKinds` within `travelReach` (800–1600 m) else a seeded ring point, saved in `target`; new tunables `travelReach`, `travelSiteKinds`, `arriveRadius` 15 m, `destinationSampleDistance` 25 m; `NpcWorldSim` gains `DespawnRadius`, `FindTemplate`, `SpawnNow` (stand-ins the same tick); wiring menu `Wire Expedition Director` lives in `ExpeditionAuthoring.cs` (reuses its scene-edit helpers) | Observer between `DespawnRadius` (360 m) and `maxHandoffDistance` (400 m) → group stays folded and residents vanish in view (measure in 1.5); a spawned band with an unreachable Travel target can stall (no time limit); finished bands with dead accumulate until the settlement loads; `ReportRoster` doesn't re-plan an existing group; `SettlementSociety` also waits on `OnLoadApplied` (1.3 checks) |
| 1.3 Away on residents | code done, reviewed; Editor tests pending | typecheck --editor clean; all new tests need the Editor (SO setup): `DayPlannerTests` (2 new), `ResidentAwayTests` (7) | **Away is a kind of offstage** (`GoAway` = `routine.LetGo()` + `GoOffstage`; `IsOffstage` true while away), so every existing offstage exclusion covers it; `ComeOnstage` is a no-op while away (`ComeHome` is the only way back, via `NetworkedTeleport.Move`); new `ResidentRoutine.LetGo()` (errand prop lands, seat + cart released); `Gossip.Witness`/`IsListening` and `SettlementSociety.EnsureCompanions` skip away (they read residents regardless of offstage); `PlannerResident.Home => !dead && !away`; hooks `SettlementSociety.AbsenceQuery` / `OnAbsenceChanged` / static `ResidentKey`, bridged in the director's Awake/OnDestroy; **`RefreshRoster()` NOT added** (nothing in Phase 0–1 calls it; a naive re-gather would shift spot indices — Phase 4 adds it with an append-only rule); `Society_AwayFromDirector_IsPassedToPlanner` lives in `ResidentAwayTests`; nothing new is saved (director is the source of truth) | Until 1.5's performer runs `GoAway` on `Start`, an away resident in a loaded chunk stands visible and idle at the hand-off point after a load; `ResidentAwayTests.cs` was missing from Bee's rsp at report time (rename if still missing); docs owed (Residents.md rows + 2 gotchas, Errands.md `LetGo`) |
| 1.4 Stand-ins | code done, reviewed; Editor steps pending (prefab menu, play test) | typecheck --editor clean; pure parts pass; `ExpeditionMemberTests` (5), `ExpeditionAssetTests`, `ResidentHandsRuleTests` + Residents suites need the Editor | `Stamp(instance, record, memberIndex, profile, residentName)` (record holds neither); NetworkVariables (`residentKey`, `displayName`, `profileIndex`, **`goalIndex` added**, `archetypeIndex`, `kitIndex`) written in the server's `OnNetworkSpawn` (NGO warns on writes before spawn; values still ride the spawn payload), server keeps a plain pre-spawn copy; new back-link `ExpeditionProfile.culture` (clients need profile → culture), validated both ways; `RosterEntry.displayName` appended (name source while the chunk is unloaded); `Resident.HeldItem` = kit weapon for a stand-in; hand rule: `Expedition` needs the tool (spear stance `Staff` = Carry); whole kit replaces the bag on every machine before `ResidentCarry.Start`; audit found no unguarded Settlement/Society dereference reachable by a stand-in (only fix: `Settlement` short-circuit); prefab menu `Tools/SpaceGame/Expeditions/Prepare Resident Prefabs` in new `StandInPrefabMenu.cs`; none of the 18 profile prefabs is a variant | Stand-ins keep the prefab's stock chatter/look-around modules and temperament (QuietStockModules/ApplyDerivedTuning need a Society) — watch in play test; stand-ins can't speak (voice table comes from the Society) → Phase 2; `Wire Saveable Prefabs` will add a trivial `FormationSaveable` to all 70 home residents (harmless, module disabled); kits > 4 items truncated with a warning |
| 1.5 Settlement performer | code done, reviewed (two fixes); Editor + play steps pending | typecheck --editor clean; puretest 76/77 (+ new `EntityPersistenceTests.InventoryCapture_SavesALentSlotEmpty_AndKeepsEveryPosition`); play untested | Start pass runs in the first `Update` once every resident's presence is network-spawned (a `GoAway` before spawn would hide on the host only); muster starts whenever a band is Departing (so `/exp depart` works); **walk-out target = `maxHandoffDistance + arriveRadius` along the road** (a watched band must reach the 400 m swap); new tunable `walkLimitMinutes` 240 (a stuck member can't hold a band); director `AdoptGroups` skips a Returning band whose settlement is performed (else duplicate stand-ins after the swap); kit at home: `ExpeditionMember.CarryKit`/`PutKitAway`, `Resident.IsWithBand`, routine publishes `Expedition`; leader speaks at the walk-out start (new `depart.*` rows in `NomadLines.txt`, topic Farewell); no facing during muster (Scripted overrides have none); deaths applied as `RestoreHealth(0)` on a hidden body; one roster-row builder `RosterEntry.Of` (baker reuses it); **lent kit weapon never saved** (`ILentSlots` on `ExpeditionMember`, `EntityInventorySaveable` writes lent slots empty); one `ObserverCheck.ChestHeight` (was 3 copies) | A member whose resident dies at home before the hand-off still gets a stand-in; presence published before network spawn never reaches clients (pre-existing gap, worked around); no muster place → residents go Away where they stand (warning) |
| 1.6 Debug commands | code done, reviewed; follow-up running (DepartNow/SendHome in the director) | typecheck --editor clean; puretest `ExpeditionCommandParseTests` 26/26, rules/rotation green | Ran in parallel with 1.3 (plan allows 1.6 before 1.4; no shared files); targets are 1-based numbers from `/exp list` or exact ids; gates DevMode → `Network.Decides` → director; long replies split into ≤180-char chat lines (`ChatNetwork.Notify` per line); new pure `ExpeditionRules.StageEndsUntil` | `ForceBand` ignores the two-band bound by design (debug); `/help` text is already near the 180-char notify cut (pre-existing) |
| 1.7 MP + persistence verification | autotest code done, reviewed; NOT RUN (needs the Editor steps + a player build); manual checklist NOT RUN | typecheck --editor clean | Modes live in a new partial `AutotestRunner.Expeditions.cs` + `AutotestProbes.Expeditions.cs` (the runner's convention is one partial per mode), not in `AutotestRunner.Settlement.cs`; time driven by clock jumps (to departHour, over the muster, past `walkLimitMinutes` only if a walk stalls); the host player is moved to force unobserved hand-off / spawned stand-ins / fold / walk-in swap; `AutotestProbes.FindSettlement` fixed to skip a culture-less settlement (the astronaut colony in the same 3x3 could null-ref existing settlement modes); Testing.md documents the commands | The test build may lack chunks around (4..6, 1..4) the walk-in point needs (`[WorldStreamer] Failed to load` noise; add cells to `MultiplayerTestPlayerBuilder.ChunkGridCells` if it matters); launch: `-sgmode expedition-host|expedition-client|expedition-persist`, pass = `*_EXP_PASS=True` + host/client stand-in lines equal |
| 1.8 Documentation | done (docs); spec updated to revision 5 by the orchestrator | `docs_check --index`: 77 docs, 2 errors, both pre-existing in `ColonyInterior.md` (a missing `ShelfStockSaveable.cs` path + a broken link) — none in ours | New `docs/AI/systems/Expeditions.md` (9 symptoms, tunables table, flows, ~20 gotchas, Extending checklist with the real menus); `docs/Human/the-systems.md` entry + one line in `04-creatures-and-people.md`; rows/gotchas in Residents, AgentSystem, Errands, HandTools, Multiplayer, Persistence (saver count 59), TerrainGeneration, WorldStreaming; the tooling symptom ("new .cs type could not be found") is only a gotcha in Expeditions.md — Testing.md is owned by 1.7 | Spec rev 5: §0.2 (no markers), §1 (director owns settlement state; performer only while loaded), §3.3 (recruits → Guard), §4.3 (deaths from OnDeath; folds don't heal), §10.1 (abstract when unloaded; rule-placed muster spot), §12 (Phase 1 tunables), §14.2 (saved/not saved; hydrate hold), §17 (pop vs despawn radius). SkyTribe.md/AgentSystem.md describe a live Sky City that no scene places (stale, not ours) |
| 1.9 Genericity guard | code done, reviewed | typecheck --editor clean; puretest `ExpeditionCode_NamesNoCultureOrSpecies` PASS (no violations; detection proven by inverting the exemption); `EverySettlementWithAProfile_IsComplete` needs the Editor and fails until the catalog is baked (intended) | Scans all of `Scripts/agents/Expeditions/**` and `Editor/Agents/Expeditions/**` (exempt: `ExpeditionContentMenu.cs`, which authors culture content), plus an explicit list of expedition files elsewhere; in Residents/ only the lines naming an expedition member are scanned (whole files name cultures on purpose); paths resolved case-insensitively and a missing listed file fails; completeness test also requires ≥1 settlement and adult warriors ≥ quota | Expedition test fixtures are not scanned (they may load the shipped culture's assets) |

## Editor runbook (what is still to run, in order)

**Status 2026-10-04 14:40:** steps 1–8 DONE (see Log; step 4's warrior menus are now *Preview/Apply Role
Quotas*, and the Scout quota was applied too); step 10 host part DONE (twice); world NavMesh re-baked;
step 11 build running. Open: step 9 (spike), step 10 on a real client (pop), step 11 runs, step 12. Run with the Editor idle:
not in Play mode, no prefab open in Prefab Mode for the prefab steps, `Chunk_6_3` not dirty with your own work.

1. **Compile.** Assets ▸ Refresh, wait for the domain reload, then run `scratchpad/rspcheck.py` (or check
   `Library/Bee/artifacts/*.dag/Assembly-CSharp*.rsp`): every new `.cs` must be listed. A file missing after a
   compile is the "dropped from the rsp" trap: rename the file (and its class) and refresh. At the time of
   writing these were not yet imported: `ExpeditionMember.cs`, `SettlementExpeditions.cs`,
   `StandInPrefabMenu.cs`, `ExpeditionMemberTests.cs`, `ExpeditionGenericityTests.cs`,
   `ResidentAwayTests.cs`, `AutotestRunner.Expeditions.cs`, `AutotestProbes.Expeditions.cs`.
2. `Tools/SpaceGame/Expeditions/Author Expedition Content` **again** (sets `NomadExpeditions.culture`, added
   after the first run). Expect the read-back log and a `culture:` line in `NomadExpeditions.asset`.
3. `Tools/SpaceGame/Expeditions/Stamp Settlement Identity` **again** (now also adds `SettlementExpeditions`).
   Expect 1 changed (`Chunk_6_3`); a third run changes 0. Diff: one MonoBehaviour + its component entry.
4. Open `Chunk_6_3` (additive is fine), then:
   - `Tools/SpaceGame/Expeditions/Back-fill Resident Source Prefabs` → expect 70/70, 70 `sourcePrefab` overrides.
   - `Tools/SpaceGame/Expeditions/Place Muster Spots` → expect `MusterSpot` at ≈ (2997.2, 114.3, 535.6),
     facing ≈ 200°, as the last child of `Generated`; a second run changes nothing. Screenshot the spot.
   - Select the Settlement, `Tools/SpaceGame/Expeditions/Preview Warrior Quota (selected settlement)` → expect
     quota 12, warriors 9, needed 3: #10 Rasha, #47, #56 → Guard; patrol 8 → 11 (wanted 10). **Approved by the
     user; if the preview lists anyone else, stop and ask.** Then `Apply Warrior Quota (selected settlement)`;
     diff = 3 archetype references to Guard (`fe7e4476…`).
   - `ResidentValidator.Report(<Settlement>)`: no "no muster spot", no "muster spot … unusable".
5. `Tools/SpaceGame/Expeditions/Prepare Resident Prefabs` (skips any prefab open in Prefab Mode — close it
   first) → 18 changed, GlobalObjectIdHash unchanged on every one; then `Tools/SpaceGame/Multiplayer/Sync
   Network Prefabs`, `Tools/Save System/Wire Saveable Prefabs`, `Tools/SpaceGame/Ragdoll/Wire Prefabs`.
6. `Tools/SpaceGame/Expeditions/Wire Expedition Director` → `persistentScene` gains `ExpeditionDirector`,
   `ExpeditionSaveable` and the `settlement-expedition` template; second run: "Already wired".
7. `Tools/SpaceGame/World/Bake Site Catalog` → `Assets/Game/Settings/WorldStreamingConfigSites.asset`
   (0 sites, 1 settlement, 70 roster rows, 12 warriors, hasMuster) and `FerdinandWorldStreamingConfigSites.asset`
   (0/0); `ExpeditionCatalog.prefabs` filled. **Re-saving chunk scenes stales the world NavMesh bake**: run
   `WorldNavMeshBaker.Bake` before any player build.
8. EditMode tests (expect FAILED=0 apart from known standing failures): `ExpeditionAssetTests`,
   `ExpeditionGroupTests`, `ExpeditionGroupSimTests`, `ExpeditionRulesTests`, `RotationTests`,
   `ExpeditionDirectorRulesTests`, `ExpeditionPersistenceTests`, `ExpeditionMemberTests`,
   `ExpeditionCommandParseTests`, `ExpeditionGenericityTests`, `WorldSiteCatalogTests`,
   `WorldSiteRegistryTests`, `SettlementMusterTests`, `DayPlannerTests`, `ResidentAwayTests`,
   `ResidentAssignmentTests`, `ResidentHandsRuleTests`, `EntityPersistenceTests`, `SpeechTests`,
   `MindTests`, `ErrandTests`, `HouseVisitTests`, `StationCueTests`, `StationTableTests` (run
   `Tools/SpaceGame/Residents/Write Station Table` first: the Muster use adds a row), `RuntimeGroupTests`,
   `GroupRecordTests`, `NpcWorldSimTests`, `WarPartyDirectorTests`, `WarPartyPersistenceTests`,
   `WarPartyRulesTests`, `WorldSaveStoreTests`, `SaveWiringOnDiskTests`, `PrefabPersistenceTests`,
   `NetworkPrefabRegistrationTests`; then `Tools ▸ Save System ▸ Validate Save Wiring` → 0 errors.
9. **Task 0.7 spike** (not started): copy a Raxy warrior prefab to `Prefabs/Spikes/Raxy_Gunner.prefab`,
   add `NpcItemUseModule` (slot 0, maxRange 25), `aimHeldItem = true`, `basicgun` in slot 0; register; spike
   scene with a hostile target on a box NavMesh; host then client capture. **Stop and report before deleting.**
10. **Play, host** (DevMode on): `/exp list`, `/exp force 1 scout`, `/exp depart <band>`; watch the muster,
    walk-out and hand-off (camera on the band within 360 m → in-place swap at ~400 m; away → swap at ~150 m);
    check stand-in names, spears at Carry, formation; `/exp days`, `/exp home`, walk-in. **Then the same on a
    real client and report whether the swap pops (stop-and-ask).**
11. **Autotests**: `Tools/Tests/Build Multiplayer Test Player`, then `-sgmode expedition-host`,
    `expedition-client`, `expedition-persist` (Testing.md); pass = `*_EXP_PASS=True` on all three.
12. Manual checklist (plan Task 1.7 step 2) on host, client and reload.

## Log

- **0.4 decision (user, 2026-10-03):** the Raxy settlement has no gate, wall or gap (only terrace
  walls; four lanes leave the plaza). The user: "settlements are dynamic, no one looks the same — put
  it somewhere you think is suitable". So the muster spot is **placed by a rule, not on a gate
  prefab**: at the outer end of the street lane that reaches farthest from the centre, ~5 m inside its
  end, +Z pointing outward along the lane. One rule serves `Settlement.Generate` (new settlements) and
  an idempotent authoring menu (existing ones, e.g. `Chunk_6_3`, without regenerating). In
  `Chunk_6_3` that is expected to be the south lane (world ≈ 2995.7, 114.3, 535.8, bearing ≈ 182°).
- **0.5 approval (user, 2026-10-03):** reassign #10 Rasha (Miner), #47 (Miner) and #56 (Scout), all
  on body `Raxy_equipped`, to **Guard**. Condition: run Preview first; if it lists anyone else, stop
  and ask again.
- Orchestrator dedupe: `ExpeditionAuthoring.ChunkScenePaths` now uses `WorldChunkScenes` (0.6). Bed
  counting (was in `Settlement.Populate`, `ExpeditionAuthoring`, `WorldSiteCatalogBaker`) is now one
  `Settlement.Beds` property.
- **Unity's compile list dropped two new files** (the known trap): `ExpeditionRecord.cs` (renamed to
  `ExpeditionRecords.cs`) and `ExpeditionContentAuthoring.cs` (file + class renamed to
  `ExpeditionContentMenu`). `scratchpad/rspcheck.py` lists any new `.cs` missing from Bee's rsp.
- **Bridges:** `unity-mcp` (Unity AI relay) hung 30 min twice and is dead for this session;
  `unityMCP` (MCP for Unity) works — `execute_code`, `refresh_unity`, `manage_scene`.
- **2026-10-04 00:4x, Editor batch (part 1):** `Author Expedition Content` ran (16 archetypes got
  roles, `NomadCulture.expeditions`, new assets incl. `Muster.asset`; 15 archetypes also gained
  `pushesCart: 0` — another session's new field being serialized). `Stamp Settlement Identity` ran:
  `Chunk_5_3` + `Chunk_6_3` saved, ids `be7fcc75…` / `1c3973d9…`, scope External.
- **`Chunk_6_3` was regenerated by hand at 22:52 on 2026-10-03** (Editor.log: `[DesertSettlement]
  Generated 30/30 buildings … 70/70 characters` from an Inspector click, then the scene saved), i.e.
  before any expedition write. Against HEAD this re-keyed the whole `Generated` subtree: all 83
  resident `SaveableEntity` ids and 94 `GlobalObjectIdHash`es differ, content is otherwise equal
  (861 street pieces, same 55 seeds). **Any save made before 22:52 no longer matches those
  residents.** Our own write on top of it: one `SaveableEntity` on the Settlement (verified by a
  document-level diff: 1 added MonoBehaviour, the GameObject's component list).
- **2026-10-04 ~02:00, fix round after the docs pass:** `ResidentPresence` now flushes its server copy into its NetworkVariables on spawn (the performer's wait-for-spawn removed); the hand-off observe radius is capped at the sim's despawn radius (`EffectiveObserveRadius`, `ExpeditionDirector.HandOffObserveRadius`); a dead member whose resident is missing keeps its band record while the BAKED roster lists the key (warns), retires otherwise (`MayRetire`); dead field `ExpeditionProfile.lines` removed. Unfixed defects recorded in DEFECTS.md (stand-ins' stock behaviour, a member dead at home still departs, spawned band stalls on an unreachable target, Sky City marker placed nowhere).
- **2026-10-04 12:2x–12:4x, Editor batch (part 2), runbook steps 1–4, 6, 7 DONE:** all new files compiled;
  `Author Expedition Content` re-run (`NomadExpeditions.culture` set); `Stamp Settlement Identity` re-run
  (`SettlementExpeditions` added to `Chunk_6_3`); back-fill 70/70; muster spot placed at (2997.16, 114.51,
  535.55) facing 199.8° — **it lies in `Chunk_5_3`** (x < 3000, the settlement straddles the chunk border), so
  the menu and the validator's reachability check need `Chunk_5_3` open too (with only `Chunk_6_3` open the
  ray finds no ground and placement fails); warrior quota previewed (exactly #47, #56, #10 → Guard, as
  approved) and applied, second preview needs 0; `ResidentValidator`: only a pre-existing Info; director
  wired into `persistentScene` (2 components + template, second run no-op); site catalog baked (1 settlement,
  70 rows, 12 warriors, hasMuster; `ExpeditionCatalog.prefabs` 18). Every scene write diffed against a
  snapshot taken at 12:27: only the expected documents changed; `Chunk_5_3` untouched.
- **Step 5 (prefabs) deliberately WAITING (user, 2026-10-04):** the "Raxy costumes" session is writing Raxy
  prefabs (incl. `Drifter_RaxyClassic`, one of the 18) and the three sweeps would touch its new prefabs. Run
  step 5 only once that session is finished.
- **Archetype moves onto the prefab (user, 2026-10-04):** the clothing session will define a resident's
  archetype on its prefab instead of per resident on the settlement. Expeditions finish on the current model
  and adapt AFTER that change lands: (1) the warrior quota (now a scene override `Resident.archetype = Guard`
  on #10/#47/#56) likely becomes a swap to a Guard job prefab; (2) `ExpeditionMember.Stamp` copying the
  archetype by index; (3) `RosterEntry.archetypeIndex` in the roster/catalog.
- **2026-10-04 13:0x, step 5 done** (after the clothing session finished): `Prepare Resident Prefabs` 18/18
  (+ExpeditionMember +disabled FormationModule; −24 lines = orphaned serialized data for fields no longer in
  code, e.g. `BeltCarrier.anchors`, dropped by any save), Sync Network Prefabs (262, 0 added), Wire Saveable
  Prefabs (FormationSaveable on the 18 only), Ragdoll Wire (nothing to do). Only those 18 files changed.
  `Drifter_rax_young`'s root `GlobalObjectIdHash` went 3590691163 → 3650592022 (the committed value was
  stale; NGO normalised it on re-import; nothing references the old value).
- **Clothing session's fixed roles (2026-10-04):** a prefab whose `Resident.archetype` is set keeps that role
  everywhere. Our three recruits are on `Raxy_equipped` (no fixed role), so their Guard override stands; the
  recruit menu now explicitly skips fixed-role bodies (`ResidentAssignment.RoleOf`).
- **Step 8, EditMode:** the Test Runner's full run never completed (other sessions' recompiles killed it), so
  34 fixtures ran in-domain by reflection (386/392) and the 5 `LogAssert` fixtures through
  `HeadlessTestRunner` (6/6 and 50/50). Remaining failure: `SaveWiringOnDiskTests.ScenePrefabIdOverridesNameTheirSourcePrefab`
  — 30 nested colony prefabs in `Chunk_5_3`, which another session rewrote between 00:4x and 12:27; not ours.
  **Real bug fixed:** `ExpeditionRules.TryPickMembers`' home-share check failed under Mono (12/15 compared at
  double precision is just below 0.8f) — the quotient is now cast to float.
- **Step 10, host play test (disposable session, 13:45):** the rotation raised a band at world start;
  `DepartNow` → muster with kit drawn (bone/stone spears) → walk-out → hand-off at ~150 m unobserved with
  stand-ins spawned **in place** (two identical poses, one 4 cm lower from the NavMesh snap) carrying the
  residents' names, archetypes and spears → fold when the player left (health read back) →
  `SimulateDays(2)` ran the whole trip → folded walk-in at the hand-off point → homecoming, rest days written.
  **Two defects found:** (1) a spawned band never travels: a 1.1 km Travel leg leaves the leader's
  NavMeshAgent `pathPending` forever (a 60 m leg walks fine) → fix: short legs for spawned bands;
  (2) the settlement has ONE Scout, so after one trip no band can form for 3 days → user decision: role
  quotas (more Scouts, picks shown before Apply) AND a slot fallback so any adult can fill a scout place and
  act as the scout (kit + recorded acting role). Both in progress.
- **2026-10-04 14:0x, fixes from the play test (agent + orchestrator):** spawned bands walk legs of
  `spawnedLegLength` (120 m; `ExpeditionRules.LegPoint`), spawned leaders stop at `leaderStopRadius` (5 m);
  `RoleSlot.fillFromAnyAdult` (Goal_Scout's Scout slot) + `MemberRecord.role` (the acting role); generic
  `ExpeditionProfile.roleQuotas` (Scout: small 1–2, large 3) and the menus are now **Preview/Apply Role
  Quotas** (one planner, warriors first); **root cause of the stall fixed in the shared motor**
  (`NavMeshAgentMotor.ApplyMoveIntent` no longer re-requests while `pathPending`; user approved).
- **Scout quota applied (user approved #43 Salvager → Scout, #64 Water Runner → Scout):** preview matched the
  prediction exactly; `Chunk_6_3` diff = 2 archetype references; catalog re-baked (3 Scouts).
- **Host play test, round 2 (disposable, 14:24):** band of 4 (2 warriors, 2 scouts incl. #43) → with the
  player following ~60 m behind, no swap at 150 m; **watched in-place swap at ~400 m**, stand-ins kept
  walking; **spawned travel works** (~200 m in 80 s over several legs); fold → `SimulateDays(3)` → home →
  **next band raised at once** (with Scout #64). Motor fix verified directly: a spawned leader steered
  1.2 km in one leg got a 9-corner path and walked (before: `hasPath=False`, velocity 0).
  Not measurable from the host: whether the swap *looks* like a pop (animation restart, nameplate) — that is
  the client check, still open.
- **World NavMesh re-baked 14:38** (1951 sources; the chunk saves had staled it), via the `World ▸ Streaming
  ▸ Bake World NavMesh` menu — bridge calls running the bake were killed by other sessions' recompiles.
- **2026-10-04 15:2x, step 11 DONE — all three autotests PASS** on a fresh `Build Multiplayer Test Player`
  (99 s; the Editor was restarted first):
  - `expedition-host` PASS (370 s): rotation-raised band of 4 (2 warriors, 2 scouts) → muster with kit 4/4 →
    hand-off by the walk-out at 394.9 m → 4 away → 4 stand-ins named + armed, none seen twice → fold →
    2 simulated days → walk-in (folded; `WALKIN_STAND_INS_SEEN=0`) → retired; 70 → 70 residents, same ids,
    0 duplicates, 0 stand-ins left.
  - `expedition-client` PASS (same run, `HOST_CLIENTS=2`): the client saw the same 4 stand-ins with the same
    names and spears (`STAND_IN_LINE` equal as a set), none twice, none left after the swap, 70 → 70 same ids.
    Note: `CLIENT_EXP_HOMECOMING_SEEN=0 still with the band` — at that probe the client still showed the
    members carrying their kit (likely timing; not a failure condition). Watch in the manual client run.
  - `expedition-persist` PASS (260 s): saved Out at stage 1 (Search) → loaded Out, stage 1; `expeditions` in
    the save with members; group owner `expedition`; no second band; 4/4 away and hidden; ids same, 0 dupes.
- **Still open:** step 9 (harpoon spike; prefab `Prefabs/Spikes/Raxy_Gunner` made and registered, not yet
  run — needs the bridge, which has 0 connected instances since the Editor restart); the hand-off *pop* judged
  by eye on a client (stop point); step 12 manual items not covered by the autotests (kill a stand-in and
  reload; unload the settlement while a band is due; two clients on opposite sides).
- **2026-10-04 16:xx, "regenerate and it works" (user request):** Generate now seats the expedition-quota
  roles first (`ExpeditionRules.PlanMoveIn`, quotas from `ExpeditionRules.Quotas`; the summary prints
  `quotas: Warrior n/12, Scout n/3`; `ResidentValidator.CheckQuotas` errors on a shortfall). New config
  `ScriptableObjects/Settlements/RaxySettlement.asset` (menu `Tools/SpaceGame/Residents/Author Raxy Settlement
  Config`): a copy of NomadSettlement with every Raxy character prefab (75; quota bodies at chance 1, other
  fixed-role prefabs 1 × 0.9, profile-dealt prefabs 2 × 0.6; ~86 copies expected for 70 beds).
  `Prepare Resident Prefabs` now also covers configs' characters (57 more prepared + the three sweeps).
  **Found and fixed: 52 of the clothing session's new Raxy prefabs carried `Drifter_Raxy`'s stale
  `GlobalObjectIdHash` (2323023342) on disk** (copies never re-validated) — NGO would have dropped all but one
  on clients; re-saved so the file holds each prefab's own hash (no duplicates left among Raxy characters).
  **Verified on a throwaway copy** (copied TerrainData 5_3/6_3, fresh DesertSettlement at Chunk_6_3's pose with
  RaxySettlement): 70/70 in 70 beds, Warrior 18/12, Scout 3/3, muster spot placed by Generate, every resident
  has an archetype and a source prefab, 58 distinct prefabs; a second Generate gave the identical sequence.
  Copy deleted; real TerrainData_6_3 untouched; Chunk_6_3 unchanged apart from the approved Scout apply.
  `Chunk_6_3` still uses NomadSettlement (switching it is the user's call).
- **State at hand-back (2026-10-04 ~02:05):** all code and docs written and reviewed; `typecheck --editor` clean; 109 pure tests pass, the 4 failing ones need the Editor (Newtonsoft / Resources); `docs_check --index`: 2 errors, both pre-existing in another session's ColonyInterior.md. Both Unity bridges are unusable (`unity-mcp` hung twice for 30 min; `unityMCP` sessions disconnect mid-command) and another session is waiting on the Editor, so the Editor runbook above (steps 1–12) is NOT done beyond the two menus noted. Not started: Task 0.7 spike. Nothing verified in play, on a client or through a reload.
