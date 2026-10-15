# Agent Restructure — progress, handoffs, verification debt

Plan: [2026-10-02-agent-restructure.md](2026-10-02-agent-restructure.md). Append-only per task;
newest handoff at the bottom of its task section.

> **2026-10-03: Phase 1 (the brain) was discarded** per [2026-10-02-brain-discard-plan.md](2026-10-02-brain-discard-plan.md). Phase 0 stands; everything below about Brain/Phase 1 is history.

## Status

| Task | State | Notes |
|---|---|---|
| 0.1 Search / caravan / autoBraking | done, EditMode-verified | 0.v: all new fixtures green; `Clanker_ResumesTheSearchItWasOn` retargeted to ClankerOutrider (test bug). Play/client/reload in debt |
| 0.2 Corpses / melee | done, EditMode-verified | ReplicatedDeathTests 8/8, NpcMeleeBlockTests 3/3. Client corpse + NPC-vs-NPC block in debt |
| 0.3 Alarm goodwill / grudge reload / gossip | done, EditMode-verified | New tests in FactionGoodwillLedger/Provocation/Mind green. Play/client/reload in debt |
| 0.4 Alloc-free loop / benchmark | done, verified | 0.v: allocation probe switched to `Is.Not.AllocatingGCMemory()` (old probe read 0 always); benchmark harness ran first time, baseline recorded |
| 0.5 Deletions / prefab defects | done, EditMode-verified | 0.5_verify: Editor compiled + reloaded clean; filtered suite 3263 / 3192 passed / 69 failed — 0 new failures from 0.5 (1 new failure is another session's untracked `Cues/ladder.asset`); RosterAssetTests 5/5, MindTests knownDeaths-load test green; 0 references to the 15 deleted script GUIDs. Play/client/reload in debt |
| 0.6 Residents baseline | partial | Harness `Tools/Agents/Run Residents Baseline` built + run 05:00–10:49 of day 1 only (stopped to free the Editor for the settlement session). Full-day run owed after the settlement overhaul settles |
| 0.7 Phase-0 review | done | No bugs on reviewed paths; `Fault.Report` silent drop fixed; stale comments fixed. Follow-ups: SettlementPopulation counts Allied players (regression from 0.3 → fix in 0.8); destroyed target never starts a search (undocumented); resumed travelling leader shows empty destination name |
| 0.8 Phase-0 follow-ups | done, EditMode-verified | SettlementPopulation counts non-players only (Player tag); destroyed held target now counts as lost and is searched (matches RestoreMemory); ResumeTask restores destination name from site id. tests_1: the 3 new tests green, FaultBarrier 10/10, AgentModuleBarrier 5/5, SearchModule 7/7, NpcTaskResume 7/7, FactionGoodwillLedger 23/23. Benchmark re-run not done (needs play) |
| 1.1 Core types / AgentTicker / fault barrier / event log | done, EditMode-verified | tests_1: AgentTickerTests 14/14, BrainCoreTests 14/14 (first run). Nothing registers a brain yet, so no play/client/reload behaviour to verify |
| 1.2 Profile / conditions / GoalSelector / SuspendStack | done, EditMode-verified | tests_1: GoalSelectorTests 18/18, ConditionTests 18/18, BrainProfileTests 6/7 (`AssetDatabase.IsSubAsset` false for a freshly added sub-asset → assertion replaced by GetAssetPath + main-asset checks, same claim). tests_2: BrainProfileTests 7/7, GoalSelectorTests 18/18 |
| 1.3 Mind / senses / reactions / shared services | done, EditMode-verified | Part A green in tests_1. tests_2 (Part B): BrainSenseTests 11/11, ReactionTests 8/8, ConditionTests 18/18 (after a rig fix: `HoldsItem_ReadsThePlayerSnapshot` added the hand after the player's first snapshot, which caches it on first sight — `BrainTestRig.Player` now carries an empty EquipmentController like PlayerCharacter), PerceptionLineOfSight 3/3, PerceptionAim 3/3, AlertChain 8/8, AgentSystemResidentFeature 25/25. Play/client in debt |
| 1.4 Brain / P1 goals / adapters / mirror / Presence | done, EditMode-verified | tests_2: BrainAssemblyTests 29/29, AgentHostilityTests 8/8, ReactionTests 8/8, HostileDialog 5/5, Provocation 17/17, AgentAuthority 21/21, Wander 1/1, PassengerSeat 22/22, NpcPassenger 23/23, SearchModule 7/7, EntityPersistence 21/21, PrefabPersistence 15/15, Threat 18/18. No prefab carries a profile yet → host+client+reload behaviour in debt (1.6) |
| 1.5 Brain persistence + validator + inspectors | done, EditMode-verified | Part A BrainSaveable; Part B AgentValidator (+KnownFailures list for 12 unmigrated body rows), AgentControllerEditor goal table, BehaviourProfileEditor, BrainReadout, docs AgentBrainTooling.md. BrainSaveableTests all green (the 8 failures were a test-rig bug: stale `player` across tests, fixed in TearDown). AgentValidatorTests 17/17, BrainReadoutTests 6/6, Provocation/EntityPersistence green. Unrelated reds: PrefabPersistenceTests.EveryWired.. + NetworkPrefabRegistrationTests (LeashEnd.cs 'Failed to load' log), SaveWiringOnDiskTests (Chunk_6_3 NomadAnimalKeep prefabId stamp, settlement). Inspector UI never looked at in a live Editor |
| 1.6 RobotHorse on `Grazer` (Phase-1 proof) | done except Dormant tier + client proofs; gate holds | Profile built by `GrazerProfileBuilder`; prefab migrated through the Editor. EditMode 105/105 (AgentValidator, BrainAssembly, BrainSaveable, BrainProfile, PrefabPersistence, NetworkPrefabRegistration, EntityPersistence). Play check `Temp/robothorse_check.txt` offline: wanders; gunshot by neutral shooter = fear only (parity); rider seated, `BrainLegsModule` suppressed, brain still hears third-party shots and picks Flee while ridden, body does not move; 60 HP hit = grudge + Flee (Wander Suspended); entity saved mid-flee has `brain` and none of provocation/flee/wander/noise/agent; reload fires 0 reactions, Flee exits cleanly (shooter has no SaveRef), Wander resumes. NOT done: Dormant tier (not in the dispatch), hostile-shooter ForceThreat path (no faction is Hostile to Fauna), filtered full suite (Editor was in use) |

## Questions for the user (defaults applied until answered)

| # | Question | Default |
|---|---|---|
| Q1 | Delete `Creatures/Horse/*` (0 asset refs; only `HorseDriver` used it)? | Keep |
| Q2 | Delete stale copies `Drifter_RaxyMauve 1`, `Drifter_RaxySage 1` (network-registered, unused)? | Keep; migrate as Villager |
| Q3 | Migrate or delete the 7 unpacked AgentControllers in `Tests/FerdinandWorld/.../FerdinandChunk_3_1`? | Migrate |
| Q4 | Nomad/Astronaut `hitGain` 300 vs everyone else 1200 — intended? | Set 1200 — **applied default** in 0.5 (Nomad + Astronaut 300 → 1200; BountyHunter inherits). Revert those two prefab values if 300 was intended |
| Q5 | ClankerOutrider has no attack module (the seated rider cannot attack) — intended? | Keep as is (spec §15 Q5: unchanged) |
| Q6 | Freeze the baseline settlement (no Generate on Chunk_6_3) until Phase 5, so the residents baseline stays comparable? | Ask the settlement session; re-run the baseline when its overhaul settles |

## Benchmark (200 agents, 4 fake players; script update ms median / p95; GC bytes/frame)

| When | median | p95 | GC/frame |
|---|---|---|---|
| baseline (P0, before deletions) — 2026-10-02, 0.v | 68.90 ms `BehaviourUpdate` (+2.72 Late, +0.25 Fixed) | 130.13 ms (Late 2.86, Fixed 0.32) | 58,383 B median / 216,873 B p95 (866 allocs median). No-agent floor: 0.05 ms, 42,791 B, 925 allocs |

| after P0 deletions — 2026-10-02, 0.5_verify | 62.63 ms `BehaviourUpdate` (+2.46 Late, +0.26 Fixed) | 119.60 ms (Late 2.71, Fixed 0.32) | 52,071 B median / 160,302 B p95 (804 allocs median). Floor: 0.05 ms, 42,641 B, 922 allocs. 141 live at the end |

| after 1.6 (33 RobotHorses on the brain) — 2026-10-03 | 65.47 ms `BehaviourUpdate` (+2.47 Late, +0.25 Fixed) | 93.68 ms (Late 2.61, Fixed 0.31) | 36,706 B median / 149,256 B p95 (675 allocs median). Floor: 0.05 ms, 56,829 B, 1228 allocs. 147 live at the end |

Run notes: 200 spawned (Raxy_poor 34, Nomad_Tan 34, Clanker/Vrescal/RobotHorse/Appa 33 each), **143 still had an enabled AgentController at the end** (mixed hostile factions fight), 4 fake players, 120/300/600 frames at captureDeltaTime 1/60, unfocused editor. GC columns include editor overhead; compare against the floor. Compare later runs only at a similar end-of-run live count.

## Residents baseline

**PARTIAL** — run 2026-10-02 23:13 (copies: `scratchpad/residents_baseline.csv` + `_summary.txt`). dayLength 1200 s, 1/60 step, day 1 from 05:00, stopped at 10:49 (~17.5k frames). Harness default is now **600 s** — the next (full) run is not comparable to this partial. Column contract: `docs/AI/systems/ResidentsBaseline.md`.

| Measure | Result |
|---|---|
| Residents | 70, all Calm, 0 errors logged |
| Places | 275, 223 usable |
| CSV rows | 273 (start 70, arrive 123, leave 80) |
| Movement | 56/70 moved > 5 m (median span 83 m, max 252 m) |
| Segments reached ≤ 3 m | 77/80 (96.3%); ≤ 1 m 71/80 |
| Errands | started 1, finished 1 |
| Carries / props moved | 12 / 10 |
| Conversations opened | 0 (lines said ~6) — check in the full run before reading as a regression |
| Gossip | midnight not reached |

Content moved under the run (settlement overhaul by another session: Chunk_6_3 saved 22:37, WorldNavMesh rebaked 22:42) — a baseline taken mid-overhaul is not a stable Phase-5 reference.

## Verification debt (could not be verified here — needs a real client / reload / play run)

| Task | What | Why not verified |
|---|---|---|
| 0.1 | Clanker walks to the last-known position after losing line of sight | needs play mode (host); not run |
| 0.1 | Search driven on the server shows on a client | needs a real client |
| 0.1 | Reload with a saved target that no longer resolves → agent searches the last-known position; `search`/`agent` keys in the save JSON | needs a real save/quit/load |
| 0.1 | Old save containing `search.hadTarget` loads without error | needs a pre-change save reloaded |
| 0.1 | Caravan spawned mid-journey keeps its destination; dwelling caravan finishes its dwell; fold/unfold keeps TaskIndex/LastSiteId/DwellRemaining | needs play mode (spawnRadius in/out) + reload of NpcWorldSaveable JSON |
| 0.1 | autoBraking removal: arrivals stop without overshoot; ridden Appa/RobotHorse/Sandloper do not brake toward the rider carrot (else set `m_AutoBraking: 0` on those prefabs) | needs play mode with a ridden mount, host + client |
| 0.2 | Client corpse: host kills an NPC → client corpse stays (falls, anim + sound) for `despawnDelay`, no second loot pile, ledger charged once | needs a real client |
| 0.2 | Late joiner: NPC died before join → corpse hidden at once | needs a real client |
| 0.2 | Client ragdoll: replicated death goes limp `settled:false`, hips pinned to replicated root, no drift | needs a real client |
| 0.2 | NPC-vs-NPC melee: a Raxy/Nomad hit by another NPC sometimes Blocks/Dodges and is not shoved | needs play mode; skipped by 0.v (another session was running play-mode tests in the shared Editor) |
| 0.3 | Settlement alarm with an Allied player: silent on host, no rally; client siren may still sound (documented) | needs play mode + a client |
| 0.3 | Old-shape grudge: load a real save with a provocation record lacking `aggression`; creature hostile after bind; re-saved JSON has `aggression: 100` | needs a real reload of a user save |
| 0.3 | Gossip at day end across a natural midnight; heard deeds survive reload (ResidentSaveable) | needs a long play run + reload |
| 0.4 | Barrier autotest `HOST_FAULTS_CONTAINED` still green (Fault/AgentController loop change) | needs the multiplayer autotest run |
| 0.5 | Old saves holding deleted keys/fields load clean: the keys of the deleted HerdMember/HerdState/BasePatrol/HealthReaction savers, `motor` body/fly blocks, perception memory in `agent`, `autoUseTimer`, `combatCadence` ranged arrays (only `knownDeaths` is covered, by MindTests) | needs a pre-0.5 save reloaded |
| 0.5 | BountyHunter / NomadOstrich after removing variant-added duplicates (AgentRagdoll, AgentGroundConform, AgentGoal, RagdollRig, SceneTracked): spawn, die (ragdoll), ride/conform, save/reload — host + client | needs play mode + a client + reload |
| 0.5 | DuneRat Chase priority 0 → 20 actually chases; Nomad/Astronaut/BountyHunter `hitGain` 1200 feels right (Q4) | needs play mode |
| 0.5 | Chunk_7_0 NavMesh only restamped (robot sat under a NavMeshAgent, excluded from the bake), not rebaked; a player build guard / world NavMesh check should still pass | needs a build or a streamed play run through Chunk_7_0 |
| 0.5 | Patrol save round-trip has no prefab test since PatrolRobot went (move to ClankerOutrider); health thresholds are no longer persisted (every prefab's list is empty) | coverage gap, not a run |
| 0.5 | Leftovers, harmless: orphan `autoUse*` YAML on ~10 prefabs, orphan keys in `ResidentTuning.asset`, unused `SfxId.EntityFootstep` (serialized enum), stale showcase data (`docs/library/library.json` + `blurbs.md` still list PatrolRobot/DeathmatchBot until the next `Export Library Site Data`) | cleanup, not a run |
| 0.5 | Docs still cite builders deleted before 0.5 (efdd3a64): SkyTribe.md Model/Flows (`RosterAuthoring`, build-order menus), EntitySystem.md builder list, AgentSystem.md Appa/RobotHorse/Clanker builders | dedicated docs pass |
| 0.v | Full EditMode suite has 73 non-0.1–0.4 failures (see 0.v handoff); NGO package `BuildTests.BasicBuildTest` blocks an unfiltered run on a Save-Scene modal | pre-existing / other sessions' in-flight work; triage in 0.7 |
| 0.6 | Full-day residents baseline (`SpaceGame.EditorTools.ResidentsBaseline.Run();`, ~60 min, poll `Temp/residents_baseline_summary.txt` for DONE) | Editor needed by the settlement session; content still changing |
| 0.6 | AgentBenchmark re-run after its `PlayModeHarness` refactor | not re-run (type-checks only) |
| 0.8 | Benchmark re-run through PlayModeHarness | needs play mode; not in 0.8 dispatch |
| 1.4 | AgentPresence spawned for real: OnNetworkSpawn write-through, client `StanceChanged`, late-join replay; SleepPresentation on a late joiner | needs a profiled prefab (1.6 RobotHorse, P4 sleeper) + host + client |
| 1.4 | Live Wander/Flee after the WanderGoal/FleeGoal helper extraction (WanderModuleTests 1/1 is thin coverage) | needs play mode |
| 1.6 | Client proofs: `AgentPresence` stance `Frightened`/`Fleeing` seen on a real client and a late joiner; a client-owned rider with the server brain hearing its surroundings; `BrainLegsModule` yielding to a client `SteerModule` | needs host + client |
| 1.6 | Pending player fact binds on `PlayerBound` with a real player identity (the check's shooter has no SaveableEntity, so Flee exited on reload); whole-world save file / F9 / quit-reload; reload twice; an old save with a `provocation` grudge on RobotHorse | needs play + real save |
| 1.6 | `ForceThreat` row (shot by a Hostile faction) never exercised live: no faction asset resolves Hostile against Fauna | needs a hostile test faction |
| 1.6 | Filtered EditMode suite after the migration (`^(SpaceGame\.|JumpingRodHop|BodyFeet)`, last 3454/3377/75) | Editor in use by another session; request was discarded twice |
| 1.6 | Dormant tier (`BrainTier`, plan 1.6 bullet 3, contract 2.3) not built | not in the dispatch |

## Handoffs

### 0.1 — Search, caravan re-roll, autoBraking (handoff: scratchpad/handoffs/0.1.md)

Decisions: `AgentTargeting.LostCount` (bumped on a held target's loss) replaces SearchModule's `hadTarget` edge; `RestoreMemory` takes an explicit `targetWasHeld` so an unresolvable saved target still starts a search; `SearchSaveable` dropped `hadTarget` (key unchanged, old field ignored). Caravans: `NpcTaskModule.SetTasks` deleted, `ResumeTask(...)` + `NpcWorldSim.ReadTaskBack` carry task index/site/dwell across fold/unfold; director-steered leaders untouched. `NavMeshAgentMotor` no longer forces `autoBraking = false` (mount prefabs NOT changed — conditional on a play check). Files: see handoff. Tests: SearchModuleTests (6), NpcTaskResumeTests (6), NavMeshAgentMotorBrakingTests (2) — all green in 0.v.

### 0.2 — Corpses on clients, NPC melee blockable (handoff: scratchpad/handoffs/0.2.md)

Decisions: `HealthComponent.IsReplicating` marks a death that arrives by replication (`RestoreHealth(value, replicated: true)`); `HealthReactionModule` presents it (anim + sound, despawn delay) instead of hiding the corpse; spawn snapshot stays a plain restore (late joiner hides old corpses). `AgentRagdoll`/`HidePartsOnDeath` treat replicated deaths as live. `CloseCombatModule.LandBlow` passes `DamageKind.Melee` and skips the knock when defended (GDC-L1-ANIM-0003). Open follow-up: `PlayerRagdoll.OnDeath` has the same `IsRestoring` shape (left alone). Tests: ReplicatedDeathTests (8), NpcMeleeBlockTests (3) — green.

### 0.3 — Goodwill in the settlement alarm, grudge reload, gossip (handoff: scratchpad/handoffs/0.3.md)

Decisions: `FactionRelations.Resolve(owner, table, other)` (goodwill → table, no grudge layer) used by `EntityTargetRegistry`'s definition query; client siren still answers by table (documented). `ProvocationSaveable`: aggressor set + no `aggression` key → restores as Max (grudge). `SettlementSociety` runs bedtime/hearth gossip only on a natural day change (`EndsADay`); dawn gossip still uncalled (DEFECTS row narrowed; 0.5 deletes it). Tests: FactionGoodwillLedgerTests +1, ProvocationTests +2, MindTests +3 (7 cases) — green.

### 0.4 — Allocation-free module loop + benchmark harness (handoff: scratchpad/handoffs/0.4.md)

Decisions: `Fault.TryEnter`/`Report` (caller-owned try/catch, key string built only for owners with a quarantined site); `AgentController.RunModule`/`ApplyFacingOverride` use it. Benchmark: `Tools/Agents/Run Agent Benchmark` / `AgentBenchmark.Run()`, `ProfilerRecorder` markers, Bootstrapper settle, box NavMesh, 4 unkillable fake players (invisible to `SessionPlayers` — documented). Tests: AgentModuleBarrierTests +2, FaultBarrierTests +2 — green after 0.v's probe fix.

### 0.v — Phase 0 (0.1–0.4) verification (handoff: scratchpad/handoffs/0.v.md)

Typecheck clean. Project EditMode suite (filter `^(SpaceGame\.|JumpingRodHop|BodyFeet)`): 3248 tests, 3170 passed, 75 failed → 2 caused by 0.1/0.4 tests, both test bugs, fixed and re-run green; the other 73 are outside 0.1–0.4 (NetGun 31, Hogtie 16, CharacterActionsPlayback 4, Lasso 4, PackSize 3, Ladder 3 [another session in flight], AgentCarry 2, LaserStaff 2, SaveWiringOnDisk 2, PrefabPersistence prefabId on Astronaut, ArmAim, LocalPlayerResolution, Singularity, Terminal, WorldItem). Benchmark ran first time without harness changes; numbers above. **AgentSystem.md is at its 150-line budget** (0.1 and 0.3 both merged gotchas to fit) — any later task adding to it must trim or split the doc first.

### 0.5 — Deletions (§6 + D11/D12) and prefab defects (D15/D16) (handoffs: scratchpad/handoffs/0.5.md, 0.5_S2.md, 0.5_S3b.md, 0.5_S4.md, 0.5_finish.md, 0.5_docs.md, 0.5_verify.md)

Deleted (+ .meta): `RigidbodyMotor`, `FlyingRigidbodyMotor`, `HorseDriver`, `AgentRangedCombatModule`, `agents/Weapons/*` (`AgentProjectile`, `AgentWeaponDefinition`, `AgentFireProfile`, `AgentAimProfile`), `HerdModule`, `HerdMemberSaveable`, `HerdStateSaveable`, `BasePatrolModule`, `BasePatrolSaveable`, `HealthReactionSaveable`, `EntityAudioModule`; assets PatrolRobot×4, AgentBullet, SuperSword, WPN/FIRE/AIM; dead API (`IMovementMotor.Nudge/SuggestDestination`, `MoveIntent.FacingDirection`, `SteerModule` runtime motor fallback, `FightOrFlight` roaring flag, `EntityEquipmentController.autoUse`, `PerceptionModule` memory/`CanSee`/`NotifySpotted`, `AgentController.RefreshModules/RefreshMotor`, residents' known deaths / dawn gossip / needs / `ClearOverride` / `DayPlan.Next` / `ResidentTuning` trip kinds). Full per-file lists: 0.5_S2/S3b/S4 and 0.5_finish "Files touched". Prefab fixes: BountyHunter + NomadOstrich variant-added duplicates removed by text (they are `[DisallowMultipleComponent]`, so `RevertAddedComponent` silently does nothing), DuneRat Chase priority 20, Nomad/Astronaut `hitGain` 1200 (Q4 default). **Clanker's two `NpcRandomLoadout`s are intentional** (slot 0 = equipped weapon, slot 1 = loot with null entries as the chance of nothing) — map §1 was wrong. Decisions: `AgentAction.Ranged = 1` kept as retired (ids append-only); no save migrations (unknown keys/fields are ignored; MindTests proves it for `knownDeaths`); health thresholds no longer persisted; RosterAssetTests rewritten against literal asset paths (Sand only). Verify: typecheck clean (1100 / 385 sources); suite 3263 / 3192 / 69 — vs baseline 3248 / 3170 / 75, 0 new failures from 0.5, 8 tests gone with their subjects, 23 new tests all green; benchmark "after P0 deletions" above. 0.5_verify also fixed stale docs (`RefreshModules` in AgentSystem.md + agent reference, the deleted-builder sentence in AgentSystem.md, the tribe skill's "ignore the menu" note) and `WarPartyDirector`'s missing-template error, which named a deleted menu.

### 0.6 — Residents baseline (handoff: scratchpad/handoffs/0.6.md)

Harness: `PlayModeHarness` (editor, extracted from AgentBenchmark) + `HarnessRun` (runtime) shared by both harnesses; `ResidentsBaseline` enters play with whatever is open, loads `persistentScene`, parks an offline observer at the settlement heart, anchors the clock to day 1 05:00, seeds one first-hand deed so midnight gossip has content. Errand counts are observed (AtPlace transitions), so the CSV stays comparable in Phase 5. Conversations got an `Opened` counter. Unexplained save of Chunk_6_3 + 4 TerrainData at 22:15 reported to the settlement session.

### 0.7 — Phase-0 review (handoff: scratchpad/handoffs/0.7.md)

Reviewed all Phase-0 diffs read-only, then fixed: `Fault.Report` logs (without counting) an exception from a module that destroyed its own owner; stale comments in 7 files. Follow-ups → 0.8.

### tests_1 — test run for 0.7/0.8/1.1/1.2 (+1.3 Part A) (handoff: scratchpad/handoffs/tests_1.md)

Named fixtures: 266 run, 265 passed (1.3 Part A fixtures all green: Provocation 17, AggressionMath 15, Anger 18, Memory 13, Threat 18, AgentHostility 5, FactionRelations 11, HostileDialog 5, Mind 41). Filtered suite `^(SpaceGame\.|JumpingRodHop|BodyFeet)`: 3392 / 3322 passed / 68 failed vs 0.5_verify 3263 / 3192 / 69 — 1 new failure (BrainProfileTests above), 2 fixed (PrefabPersistence prefabId, SaveWiringOnDisk stamped id — not ours), 0 tests vanished, 129 new tests. Results: scratchpad/xml_t1/.

### tests_2 — test run for 1.2 re-run, 1.3 Part B, 1.4 A+B (handoff: scratchpad/handoffs/tests_2.md)

Named run (28 fixtures, compiled 01:12): 366 / 365 — the one failure was a test-rig bug (`ConditionTests.HoldsItem_ReadsThePlayerSnapshot`: hand added after the snapshot's first sight); fixed in `BrainTestRig.Player`, green in the suite. Filtered suite (compiled 01:17): 3454 / 3377 passed / 75 failed vs tests_1 3392 / 3322 / 68 — 0 new failures from 1.2–1.4; BrainProfileTests now green; the 8 new failures are all `BrainSaveableTests` (Task 1.5, in flight when the suite compiled: `Bind()` line 118 reads a destroyed Transform); 1 test gone (the HoldsItem case 1.3 removed from `Unsupported_FalseWithPhaseReason`). Results: scratchpad/xml_t2/.
