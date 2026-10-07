# Agent Restructure Implementation Plan

> **For agentic workers:** one task = one subagent. Read this file's *Global constraints* and
> *Working protocol* first, then only your task and the maps it names. Track steps with `- [ ]`.
> Progress, handoffs and verification debt go in
> [2026-10-02-agent-restructure-progress.md](2026-10-02-agent-restructure-progress.md).

**Goal:** Replace the agent module stack (priority integers, `enabled` toggling, frame-stamp side
channels) with one server-side `Brain` per agent built from a shared `BehaviourProfile` asset —
senses → memory → mind → tiered goals on four body channels — without losing a feature, phase by
phase, each phase shipping.

**Spec:** [docs/superpowers/specs/2026-10-02-agent-restructure-design.md](../specs/2026-10-02-agent-restructure-design.md).
Read §0–§4 and §10–§13 before any Phase-1+ task. The *why* is there; this plan is the *how* and
records where the code forced a different answer (§ "Plan decisions").

**Code maps (2026-10-02, read-only survey; re-verify line numbers, code moves):**
`/private/tmp/claude-501/-Users-ferdinandfremming-Documents-hackerspace-spillgruppen-SpaceGame/e2db7b85-2e25-4976-aa3a-4dfb3a94412a/scratchpad/maps/`
— `core.md` (controller loop, modules, targeting, motors, frame order), `prefabs_savers.md` (all 55
agent prefabs × components, savers, migration mechanics), `callsites_tests.md` (§5.1 table, 60 test
files, test infra), `phase0.md` (bug root causes + fixes, deletion recipes),
`residents_groups_mounts_net.md` (residents, NpcWorldSim, mounts/seats, netcode on agents).

**Tech stack:** Unity 6000.3.11f1, C#, NGO (`NetworkBehaviour`, `NetworkVariable`, `[Rpc]`,
`NetMessaging`/`AgentActionRelay`), Newtonsoft save adapters, NUnit EditMode.

---

## Global constraints

- **No commits.** A user-policy hook blocks `git commit`; the user has not asked for commits. Leave
  changes in the working tree. **Never** `git stash`, `git checkout -- <file>`, `git restore` or
  `git reset` — the tree holds the user's unrelated uncommitted work (memory: a stash round-trip
  lost 457 files). Undo your own edits by editing them back.
- **The Bash hook** also blocks any command containing `$`, backticks, `for` loops, heredocs with
  braces, or the substring `commit`. Use literal paths, or `Write` a `.py` to the scratchpad and run
  `python3 /abs/path.py`.
- **Other Claude sessions edit this repo concurrently.** A compile error in a file you did not touch
  is probably theirs — read the paths before "fixing" anything. Re-read a file right before editing
  it if your earlier read is more than a few minutes old.
- **Prefab writes:** inside the same Unity command that saves, check
  `PrefabStageUtility.GetCurrentPrefabStage()` for that path and **skip** a prefab open in Prefab
  Mode (report it as pending). `cp -p` the prefab to the scratchpad first and diff after.
- **Server decides.** Brain code runs only where `Network.Decides` (server, or offline). Offline
  `Network.Server` is false and `Network.Decides` is true. Never gate the brain on ownership.
- **Determinism.** Every random draw in brain code uses a per-agent `System.Random` seeded from
  identity (`SaveableEntity.InstanceId` hash or `Resident` seed), never `UnityEngine.Random`.
- **No allocations per tick** in AgentTicker, Brain.Tick, senses, selection. No LINQ, no closures,
  no `string` building in hot paths. Reuse buffers.
- **Statics** reset in `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`
  (domain reload may be off).
- **Save formats are append-only** within a key; never rename an existing key. Restore writes state
  directly — never through events — so no reaction, alert or ledger report fires on load.
- **CLAUDE.md non-negotiables** apply to every task: multiplayer (host *and* client), persistence
  (reload + read the JSON), no code smells (no dead code, no debug logs, no magic numbers —
  serialize tunables, no silent `catch`), and **docs in the same change** (governing doc updated,
  untrue text deleted, Gotchas/symptoms added, `updated:` bumped,
  `python3 tools/docs_check.py --index` → 0 errors).
- **Tests** live in `Assets/Game/Editor/Tests/` (Assembly-CSharp-Editor). The
  `Tests/EditMode` asmdef cannot see Assembly-CSharp. EditMode tests must not depend on
  `Time.time` advancing.
- **Old and new never drive one prefab at once.** A prefab is either on the old module stack or on a
  profile. `AgentValidator` refuses the mix from Phase 1.

## Working protocol (every subagent)

1. Read: this file's top sections, your task, the maps it names, the governing system doc(s)
   (`grep <path> docs/AI/ROUTING.md`), `docs/AI/INVARIANTS.md` once, and the relevant
   `.claude/skills/spacegame-*/SKILL.md` (agent / multiplayer / persistence).
2. Read the progress file's last handoff for your task, if any.
3. Implement. Type-check after every meaningful step:
   `python3 tools/typecheck.py --editor` → `No errors.` (errors in files you did not touch: see
   concurrency note; do not edit other sessions' files).
4. Tests: run the fixtures your task names. Preferred route — Unity MCP `Unity_RunCommand`:
   `SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("<Fixture>");` then poll
   `Temp/headless_tests.txt` for `DONE` (delete it first; run twice if results look stale; a run
   that finishes in seconds was truncated). Gate on `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll`
   being newer than your `.cs` before trusting a run. **Never** call `AssetDatabase.Refresh` /
   `ImportAsset(ForceUpdate)` / `RequestScriptCompilation` from a command — the bridge wedges for
   30 min. If the bridge is unresponsive once, stop using it and record the debt.
   Fallback without the bridge: AppleScript menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)`
   (open the menu bar item first; see memory `project_headless_verification`).
5. Play-mode / client / reload verification: do what the environment allows (bridge play harness:
   SessionState flag + `[InitializeOnLoadMethod]`, `Time.captureDeltaTime = 1f/60f`, box NavMesh).
   Anything you could not verify on a real client or across a real reload goes into the progress
   file's **Verification debt** table, verbatim — never claim it.
6. Docs: update the governing doc(s) per CLAUDE.md, run `python3 tools/docs_check.py --index`.
7. **Context budget:** if your context approaches ~120k tokens, stop at a compiling point, write a
   handoff (what is done, what is next, files touched, open questions) under your task in the
   progress file, and return `HANDOFF: <task id>`. A fresh agent continues.
8. Return ≤200 words: done / not done, files touched, tests run + results, verification debt added.

---

## Plan decisions (where the code or the maps overrode the spec)

| # | Topic | Decision | Why |
|---|---|---|---|
| D1 | Agent count | **55** agent prefabs (53 + variants `Caravan/BountyHunter`, `Caravan/NomadOstrich`); **51** after the 4 PatrolRobots go. Phase 7 enumerates 51 (plus any added meanwhile). | prefabs_savers §0 |
| D2 | Brain tick | Brain ticks in **`AgentTicker`** gated on `Network.Decides` and `controller.isActiveAndEnabled`, *not* inside `AgentController.Update` (that loop is owner-gated: a player-ridden mount is client-owned, so a brain inside it would die on the server). Exec order −60 (before AgentTargeting −50). | core F1 |
| D3 | Coexistence adapters | The spec's "side-effect sense module" is unnecessary: senses run in the Brain via AgentTicker, so MountModule suppression cannot starve them. Adapters inside the old loop are only **`BrainLegsModule`** (movement; reports the brain's `LegsRequest` as `MoveIntent`; *is* suppressed while a player rides — correct, the Rider drives) and **`BrainGazeModule`** (`IFacingModule`). | core F3/F4 |
| D4 | Threat mirror | Add `AgentTargeting.Pin(Transform)` / `Unpin()` (pinned target is not re-scored away) instead of re-forcing every frame. Brain writes Threat → Pin, Memory last-seen/age → `RestoreMemory`-style setters, so NpcWorldSim's lead reads keep working until Phase 6. | core F10, callsites #16-17 |
| D5 | Grudge in hostility | `FactionRelations.HoldsGrudgeAgainst` goes through a new `IGrudgeHolder` interface (implemented by `ProvocationModule` and by the Brain's Mind) in Phase 1; dialogue refusal / "busy?" reads go through a static `AgentHostility` facade. | callsites #1,2,5,8,39 |
| D6 | Old-save migration | **Continuous legacy read, not a one-shot version bump.** Add `ILegacyStateReader` to the persistence core: on restore, a key with no saver on the entity is offered to any saver that lists it in `LegacyKeys`. `BrainSaveable` claims `provocation` and `resident`. Works for every prefab whenever it migrates (P1–P7) and for saves made mid-migration; no `CurrentVersion` change. A `provocation` record with an aggressor and no `aggression` field restores as **Max** (grudge). | prefabs_savers §5.1–5.2 (a V2→V3 migration would run once and miss prefabs that migrate later) |
| D7 | Body state persistence | Body state (`Seated/Ridden/Carried/…`) is **derived at runtime**, never saved, so cross-entity deferred-load order (rider brain vs mount) does not matter. | prefabs_savers §5.1 |
| D8 | `LegsRequest`/`Pace` | Defined in **Phase 1** (the brain must emit them); `BrainLegsModule` converts to `MoveIntent`. Phase 2 makes motors consume `LegsRequest` natively and deletes `MoveIntent`. | |
| D9 | Phase-1 test creature | **RobotHorse** (Grazer path): rideable (proves brain keeps sensing while ridden), has Flee + Provocation + hearing + Wander (proves suspend/resume and the Legs channel). | spec §14.17 |
| D10 | Phase 3 scope | All **Grazers** (Appa, RobotHorse, Sandloper, Ostrich, NomadOstrich) + **Wanderers** (RigWalker, DesertCrawler). NomadOstrich's `NpcTaskModule`/`FormationModule` stay old-stack until Phase 6 → NomadOstrich migrates in **Phase 6**, not 3. | NpcTask only on NomadOstrich |
| D11 | Projectile "merge" | Nothing to merge: delete `AgentProjectile` + `AgentBullet.prefab` (both network lists); **keep `TurretProjectile`** (live via `RocketLauncherTurret`). | phase0 #16 |
| D12 | Extra P0 deletions | Also delete (orphaned by §6): `HerdStateSaveable` (+ its component on `[SaveSystem]` in `persistentScene.unity`), `agents/Weapons/` (AgentWeaponDefinition/FireProfile/AimProfile + WPN_RobotPistol/FIRE_PistolBurst/AIM_GruntPoor assets), `EntityAudioModule`, `SuperSword.prefab` (verify PatrolRobot-only first), `MoveIntent.OverrideFacingDirection`, `PerceptionModule.CanSee` (→ `IsVisible`), `SettlementSociety.needSpeakers`, `ResidentTuning.TryGetTrip`. | "remove everything that can be removed" |
| D13 | Not deleted without the user | `Creatures/Horse/*` (live rig work per memory), `Drifter_RaxyMauve 1` / `Drifter_RaxySage 1` (stale copies, still network-registered), the 7 unpacked AgentControllers in `Tests/FerdinandWorld/.../FerdinandChunk_3_1`. Listed under *Questions for the user* in the progress file; they are migrated like any other agent if the user does not answer. | |
| D14 | "Unchanged" modules | `MountModule`, `SteerModule`, `CrawlerToolModule` derive `BehaviourModuleBase`. They become plain components (Rider leg source / tool component) in **Phase 2**, so Phase 7's "no `IBehaviourModule`" gate can pass. No-brain agents (PlayerShip, DuneOrnithopter) still need `AgentController` to host the leg stack. | callsites D2, prefabs §0 |
| D15 | Pre-existing prefab defects | Fix in P0: duplicate components on BountyHunter (2× AgentGoal/AgentGroundConform/RagdollRig/AgentRagdoll), NomadOstrich (2× SceneTracked/RagdollRig/AgentRagdoll), Clanker (2× NpcRandomLoadout); stale `hitGain: 300` on Nomad + Astronaut (code default and all others 1200 — confirm intent, default to 1200); DuneRat `ChaseModule.priority 0` → 20. | prefabs §1 |
| D16 | Restored test coverage | `VisionBaselineTests` and `RosterAssetTests` were deleted in 7db435b7 but the spec/skills cite them — the AgentValidator test (P1) re-covers VisionBaseline; RosterAssetTests is restored from git history in P0 if it still compiles, else rewritten. | callsites C |
| D17 | Brain code home | `Assets/Game/Scripts/agents/Brain/` (Assembly-CSharp; asmdefs cannot reference Assembly-CSharp). Profiles: `Assets/Game/ScriptableObjects/Agents/Profiles/`. | |
| D18 | Conditions/effects | Fixed vocabulary = `enum ConditionKind` + typed params in a `[Serializable] struct Condition`; same for `EffectKind`. Escape hatch = a `CustomCondition`/`CustomEffect` ScriptableObject sub-asset. No `[SerializeReference]`. | spec §2 Authoring |

---

## Target file structure (new)

```
Assets/Game/Scripts/agents/Brain/
  Core/      AgentTicker.cs, Brain.cs, BrainContext.cs, Situation.cs, EventLog.cs, BrainFault.cs,
             BrainRandom.cs, BodyState.cs, Channels.cs (Channel flags + BodyChannels output)
  Body/      LegsRequest.cs (LegsKind, Pace, LegsRequest), LegControl.cs (P2: source stack),
             OffMeshTransit.cs (P2), IMotorState.cs (P2)
  Profile/   BehaviourProfile.cs, SenseDefinition.cs, ReactionDefinition.cs, Condition.cs,
             Effect.cs, Consideration.cs, CustomCondition.cs, CustomEffect.cs
  Memory/    Memory.cs, Fact.cs (FactSource, StimulusKind, StimulusSlot), PlayerSnapshot.cs,
             SightLine.cs
  Mind/      Mind.cs, Anger.cs (wraps AggressionMath), Fear.cs, Threat.cs, Personality.cs,
             IGrudgeHolder.cs, AgentHostility.cs
  Senses/    Sight.cs, Hearing.cs (+ INoiseListener), Touch.cs, Menace.cs, Awareness.cs (P5)
  Goals/     GoalDefinition.cs (GoalTier, GoalStatus, ExitReason, GoalInstance), GoalSelector.cs,
             SuspendStack.cs, Wander.cs, FollowDirective.cs, Investigate.cs, Flee.cs, Sleep.cs,
             Warn.cs (P3), Fight.cs (P4), Participate.cs (P5), FollowPlan.cs (P5), Task.cs (P6)
  Plans/     Method.cs, Step.cs, StepRunner.cs (P5)
  Skills/    IAttack.cs, MeleeAttack.cs, ProjectileAttack.cs, HeldItemAttack.cs, SpellAttack.cs,
             Speak.cs, Gesture.cs, UseItem.cs, Defend.cs (P4/P5)
  Social/    Encounter.cs (P5), GroupBrain.cs, Directive.cs, EngagementCoordinator.cs (P4/P6)
  Presence/  AgentPresence.cs (NetworkBehaviour: stance, focus, activity)
  Adapters/  BrainLegsModule.cs, BrainGazeModule.cs (deleted in P7)
  Persistence/ BrainSaveable.cs, AgentControllerSaveable.cs (P7: folds AgentPacingSaveable)
Assets/Game/Editor/Agents/  AgentValidator.cs, AgentControllerEditor.cs (goal table),
                            BehaviourProfileEditor.cs (summary + "+ Add" from TypeCache)
Assets/Game/Editor/Tests/   Brain*Tests.cs, AgentValidatorTests.cs, AgentBenchmark (P0)
```

---

## Phase 0 — bugs, deletions, benchmark, baselines

Each bug fix ships with its EditMode test and doc update. Tasks 0.1–0.4 touch disjoint files and
may run in parallel; 0.5 (deletions) runs after 0.1–0.4 finish (it edits `AgentController`,
`SaveablePolicy`, motors); 0.6–0.7 after 0.5. Map: `phase0.md` (full recipe per item).

### Task 0.1 — Search, caravan re-roll, autoBraking
Files: `Modules/Movement/SearchModule.cs`, `AI/Targeting/AgentTargeting.cs`,
`Tasks/NpcTaskModule.cs`, `World/NpcWorldSim.cs`, `AI/Motors/NavMeshAgentMotor.cs`.
- [ ] **Search (phase0 A1):** `AgentTargeting.LostCount` incremented in `ClearTarget` when a target
  was held; `SearchModule` tracks `handledLoss` instead of `hadTarget`; start a search when the count
  changed, no target, and a last-known position exists. Remove the now-meaningless `hadTarget`
  plumbing from `SearchSaveable` (keep the key; ignore the field on read).
- [ ] Test `SearchModuleTests.StartsSearchAfterTargetLost` (fails before, passes after). Replace the
  no-op Golem row in `PrefabPersistenceTests.Golem_ResumesTheSearchItWasOn` with Clanker.
- [ ] **Caravan (A2):** `NpcTaskModule.ResumeTask(tasks, index, travelling, dwellRemaining, lastSiteId)`;
  `NpcWorldSim.Configure` calls it after `SetGoal` instead of `SetTasks`; `Despawn` reads phase,
  dwell and lastSiteId back into the group record (append fields to the record if missing).
  Delete `SetTasks` if it has no other caller.
- [ ] Test `NpcTaskResumeTests` (resume travelling keeps index + goal; resume dwelling finishes the
  dwell).
- [ ] **autoBraking (A6):** delete the forced `agent.autoBraking = false` in `NavMeshAgentMotor.Awake`
  and fix the comment that depends on it. Set `m_AutoBraking: 0` on the three ridden NavMesh mounts
  (Appa, RobotHorse, Sandloper) **only if** a play check shows braking toward the rider carrot;
  record the check (or the debt).
- [ ] Docs: AgentSystem.md (Search row), Errands/NPC world docs for the caravan fix, NavMeshSystem.md.

### Task 0.2 — Corpses on clients, melee blockable
Files: `Gameplay/Health/HealthComponent.cs`, `NetworkedHealthComponent.cs`,
`agents/Entity/HealthReactionModule.cs`, `Gameplay/Ragdoll/AgentRagdoll.cs`,
`HidePartsOnDeath.cs`, `Modules/Combat/CloseCombatModule.cs`.
- [ ] **Corpses (A3):** `HealthComponent.IsReplicating` true only inside
  `RestoreHealth(value, replicated: true)`; `NetworkedHealthComponent.ApplyHealth` passes
  `replicated: true` from `OnValueChanged` (the `OnNetworkSpawn` snapshot stays a plain restore so a
  late joiner hides old corpses). `HealthReactionModule.HandleDeath`: replicated → present death
  (anim trigger + SFX only) and run the normal despawn timer; save-restore → immediate as today. Apply
  the same distinction in `AgentRagdoll` (~:177) and `HidePartsOnDeath` (~:52).
- [ ] Test: replicated death keeps the GO active for `despawnDelay`; save-restore death deactivates
  at once; neither fires loot/ledger/noise.
- [ ] **Melee (A4):** `CloseCombatModule.LandBlow` passes `DamageKind.Melee`; skip the knockback
  when the returned defense is not `None`.
- [ ] Test: victim with `MeleeDefense` (blockChance 1, facing) takes no damage, `LastDefense == Blocked`,
  is not knocked.
- [ ] Docs: EntitySystem.md (HealthReaction despawn), Combat.md; symptom "NPC corpses vanish
  instantly on clients".
- [ ] Verification debt (expected): kill an NPC on host → corpse stays on client for `despawnDelay`.

### Task 0.3 — Goodwill in the settlement alarm, grudge reload, gossip
Files: `Faction/FactionRelations.cs`, `Core/EntityTargetRegistry.cs`, `Faction/SettlementAlarm.cs`,
`Core/Persistence/Adapters/ProvocationSaveable.cs`, `Residents/Core/SettlementSociety.cs`,
`Residents/Mind/Gossip.cs`.
- [ ] **Alarm (A5):** `FactionRelations.Resolve(FactionDefinition owner, FactionRelationshipTable,
  EntityFaction other)` applying goodwill (shared band logic with `ResolveGoodwill`) then the table;
  the definition-overload `EntityTargetRegistry.Query` uses it. Fix the stale header comment. Add a
  Gotcha: the ledger is server-only, so a client's siren can still answer by table.
- [ ] Test in the goodwill tests: Allied band overrides a Hostile table row for the definition query.
- [ ] **Grudge reload (new, prefabs_savers §5.2):** `ProvocationSaveable.RestoreState`: aggressor
  set + `aggression` field absent → restore as `AggressionMath.Max` (grudge), not 0. Test with a
  raw old-shape JObject.
- [ ] **Gossip (A7):** in `SettlementSociety.Tick`, capture `ended = builtDay` before
  `RebuildPlans()` and call `EndDay(ended)` only when `ended == Day - 1` (natural day change; loads,
  time jumps and the initial build must not fire). `EndDay` runs `Gossip.SpreadAtBedtime` and
  `SpreadAtHearth`. Expose the `ended == Day-1` rule as a pure static and test it.
- [ ] Tests (MindTests style): bedtime passes a first-hand deed to kin (heard, favor moved); hearth
  passes between two friends who both had a Hearth segment.
- [ ] Docs: ResidentReputation.md (Flows: gossip at day end), DEFECTS.md row for gossip deleted
  (with Task 0.5's DawnDeaths removal — coordinate via the progress file).

### Task 0.4 — Allocation-free module loop + benchmark harness
Files: `Controller/AgentController.cs` (RunModule, ApplyFacingOverride only),
`Core/Diagnostics/Fault.cs`, new `Assets/Game/Editor/Agents/AgentBenchmark.cs` +
a runtime bootstrap under `Assets/Game/Scripts/agents/Diagnostics/`.
- [ ] `Fault`: a non-allocating entry (`TryEnter(owner, site)` + `Report(owner, site, e)`, or
  `Run<TState>` with a static lambda and a ref struct state) and build the key string only when
  quarantine bookkeeping actually needs it (count > 0, or on report). Keep `Fault.Run(Action)` for
  cold callers.
- [ ] `AgentController.RunModule` / `ApplyFacingOverride` use it — zero closures per tick.
- [ ] Test: `GC.GetAllocatedBytesForCurrentThread()` around 1000 `RunModule` calls with a stub
  module → 0 bytes. Keep `AgentModuleBarrierTests` green.
- [ ] **Benchmark:** menu `Tools/Agents/Run Agent Benchmark` → SessionState flag → enters play mode
  in an empty scene, builds a box NavMesh (one `NavMeshBuildSource`, shape Box), spawns 200 agents
  cycling through a fixed prefab mix (Raxy_poor, Nomad_Tan, Clanker, Vrescal, RobotHorse, Appa) and 4
  scripted fake players (moving `SessionPlayers`-visible transforms), `Time.captureDeltaTime = 1/60`,
  warms 300 frames, records 600 frames of `ProfilerRecorder` main-thread script update time
  (`BehaviourUpdate` marker) + `GC.Alloc` count/bytes, writes `Temp/agent_benchmark.txt`
  (median/p95 ms, GC bytes/frame), exits play mode. Unfocused-editor frame time is meaningless —
  measure the markers, not frame ms.
- [ ] Run it; record the baseline in the progress file's **Benchmark** table.
- [ ] Docs: Diagnostics.md (benchmark), AgentSystem.md (no per-tick allocation; Fault entry).

### Task 0.5 — Deletions (§6 + D11/D12) and prefab defect fixes (D15)
Runs after 0.1–0.4. Recipe per item in `phase0.md` Task B table (#1–#22) — follow it exactly.
- [ ] Code deletions: FlyingRigidbodyMotor, RigidbodyMotor, `SteerModule.EnsureRuntimeMovementPath`
  (+ field, helper, 2 calls), HorseDriver (**not** `Creatures/Horse/*`), `IMovementMotor.NudgeDestination`
  /`SuggestDestination` (6 impls + 2 test fakes), `MoveIntent.FacingDirection` +
  `OverrideFacingDirection`, `FightOrFlightModule.SetRoaringFlag` + field,
  `EntityEquipmentController.autoUse*` + `EntityEquipmentSaveable.autoUseTimer` (keep
  `TryUseForward`), `PerceptionModule` memory + `NotifySpotted` + spot fields + `CanSee` (→ `IsVisible`)
  + `AgentStateSaveable` memory restore, `ResidentMemory` death knowledge + `Gossip.DawnDeaths`,
  `SettlementSociety.FindNeeds/HasNeed/needSpeakers`, `Resident.ClearOverride`, `DayPlan.Next`,
  `TripKindRow.siteKinds/maxRadius/armed` + `TryGetTrip`, `AgentRangedCombatModule` (+ AgentTargeting
  range loop, CombatCadence ranged block, SaveablePolicy row, ranged tests), `AgentProjectile`,
  `HerdModule` + `HerdMemberSaveable` + `HerdStateSaveable`, `BasePatrolModule` + `BasePatrolSaveable`,
  `HealthReactionSaveable` (+ SaveablePolicy row; all thresholds empty), `agents/Weapons/*`,
  `EntityAudioModule`. Keep `AgentAction.Ranged`'s wire number (mark the enum value obsolete-free:
  delete it only if numbering stays stable).
- [ ] Asset deletions (via the Editor so GUID references are seen; check Prefab Mode first): the 4
  `Robots/PatrolRobot*.prefab`, `AgentBullet.prefab`, `SuperSword.prefab` (after a GUID scan proves
  PatrolRobot-only), WPN_RobotPistol / FIRE_PistolBurst / AIM_GruntPoor; remove their entries from
  **both** `Assets/DefaultNetworkPrefabs.asset` and
  `Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset`; remove the PatrolRobot
  instance from `Chunk_7_0.unity` and the `HerdStateSaveable` component from `[SaveSystem]` in
  `persistentScene.unity` (open scene → delete → save; re-saving a chunk invalidates the baked world
  NavMesh: re-bake with `WorldNavMeshBaker.Bake(WorldNavMeshBaker.LoadConfig())`). Remove the
  `HealthReactionSaveable` component from all agent prefabs (saver before module order).
- [ ] D15 prefab fixes: remove duplicate components (BountyHunter, NomadOstrich, Clanker); set
  DuneRat Chase priority 20; Nomad/Astronaut `hitGain` → 1200 (note as a decision in the progress
  file).
- [ ] Tests: delete PatrolRobot rows/const in `PrefabPersistenceTests`, `LibraryExporter` skip rows,
  ranged tests in `AgentActionBroadcastTests`, MindTests death lines; restore `RosterAssetTests` from
  git history (`git show 7db435b7^:<path>`) if it compiles, else rewrite (D16). Full EditMode suite
  green except the documented standing failures (`Time.time == 0` mount tests, Lasso, LaserStaff).
- [ ] Docs: every row in `phase0.md` "Docs to edit for §6" (AgentSystem, Combat — fix its
  frontmatter path since `agents/Weapons/` vanishes, MountSystem, Vehicles, Locomotion,
  SceneTransitions, LeashSystem, Multiplayer, audio docs, ResidentReputation, the agent and
  multiplayer skills + reference.md). DEFECTS.md: remove the gossip row and the `PatrolRobot 2` row.

### Task 0.6 — Residents baseline run
- [ ] Play-mode harness in `Chunk_6_3` (or the settlement test scene the Residents doc names):
  `captureDeltaTime = 1/60`, time-scale the day clock to cover one full in-game day, sample every
  resident at each plan segment boundary: plan activity, `ResidentRoutine.Current`, position vs plan
  target distance, errands started/finished, conversations, gossip at day end. Write
  `Temp/residents_baseline.csv` and a summary into the progress file (**Residents baseline**).
- [ ] If play mode cannot be driven, record the debt and write the harness anyway so Phase 5 can run
  it before and after.

### Task 0.7 — Phase-0 review gate
- [ ] Independent review subagent: `/code-review`-style pass over the Phase-0 diff (correctness +
  smells + docs). Fix findings. Update the progress file; Phase 0 done when every bug has a passing
  test and either a host+client check or a recorded debt row.

---

## Phase 1 — foundations + coexistence (one test creature: RobotHorse)

Sequential tasks (shared new files). Map: `core.md` §14, `callsites_tests.md` A.
**The Phase-1 API contract is authoritative for names, signatures, enum values and the RobotHorse
profile:** `scratchpad/phase1_contract.md` (namespace `SpaceGame.Agents.Brains`; Anger extracted
from `ProvocationModule` into a plain class that the module wraps; `GoodwillHitReporter` takes over
goodwill Hit reporting from profiled agents; `LegsRequest.SpeedScale` (P1 only) keeps directive
speeds; budget estimate ≈0.85 ms at 200 agents). Where it and this plan differ, the contract wins
for Phase 1 and the deviation is recorded in the progress file.

### Task 1.1 — Core types, ticker, fault barrier, event log
- [ ] `Channel` (`[Flags] Legs=1, Gaze=2, Hands=4, Voice=8`), `BodyChannels` (per-tick output:
  `LegsRequest Legs`, `GazeTarget` (Transform or point, priority-free), `HandsCommand`,
  `VoiceLine`), `LegsRequest` + `LegsKind {Hold, GoTo, Drive}` + `Pace {Stroll, Walk, Run, Sprint}`
  (spec §4.2), `BodyState` enum (spec §4.1; derived each tick from existing flags:
  `RidesAsPassenger`, `MountModule.IsMounted`, motor `IsCarried`, ragdoll, `StatusReceiver.Suppressed`,
  ArrivalDirector quiet, `HealthComponent.IsDead`).
- [ ] `Situation` (readonly struct: time, dt, self pose, body state, memory/mind read-only views,
  directive, personality), `GoalStatus {Running, Succeeded, Failed}`, `ExitReason`,
  `EventLog` (fixed 32-entry ring of `(time, kind, a, b)` value structs — no strings until
  displayed), `BrainRandom` (seeded `System.Random` wrapper), `BrainFault` (per-goal quarantine,
  allocation-free, logs once via `Fault.Report`).
- [ ] `AgentTicker` (singleton MonoBehaviour created on demand, `[DefaultExecutionOrder(-60)]`,
  `DontDestroyOnLoad`): registry of brains; staggered buckets — think at 10 Hz split across frames,
  active goal `Tick` every frame, senses on their own budgeted interval; skips controllers that are
  disabled or `Offstage` (Dormant tier = Offstage for now); runs only when `Network.Decides`;
  statics reset on SubsystemRegistration. Importance floor hook (`IsImportant`) returns true for now —
  Dormant promotion logic lands with the tier work in 1.6.
- [ ] Tests: ticker stagger distributes N brains evenly; disabled controller not ticked; zero
  allocations over 1000 ticks of 50 stub brains.

### Task 1.2 — Profile, conditions, goals, selection, suspend/resume
- [ ] `BehaviourProfile` (ScriptableObject): `SenseDefinition[] senses`, `ReactionDefinition[]
  reactions`, `GoalDefinition[] goals`, all **sub-assets**; definitions immutable at runtime.
  `Condition` struct + `ConditionKind` (the spec §4.6 list: `HasThreat, ThreatWithin, FearAbove,
  AngerBand, Relationship, BondedTo, FactSource, BodyState, InPlanSegment, EscapeExists,
  HoldsItem, HasDirective, NoiseHeardWithin, TimeSince, Random`) with `Evaluate(in Situation)` and
  `Describe(in Situation)` (the "why not" text, built only for the inspector). `CustomCondition`
  SO escape hatch. `Consideration` (curve over a `ConditionKind`-style input) for desire.
- [ ] `GoalDefinition`/`GoalInstance` exactly as spec §4.5 (Tier, Needs, MinDwell, Presents, Gates,
  Desire, Create; Enter/Suspend/Resume/Exit/Interruptible/Tick/Capture/Restore).
- [ ] `GoalSelector` (pure C#): gates → tier → desire (Social/Routine only) → hysteresis (`MinDwell`,
  `Interruptible`, higher tier preempts); winner claims `Needs`; remaining channels to the best
  compatible goal; records first failed gate per loser. `SuspendStack` (≤3, timeout, `Resume`
  re-check → continue or Exit with reason). Flap detector (> N switches in T s → log fault event).
- [ ] Tests (pure EditMode, stub goals): tier precedence; desire only within Social/Routine;
  MinDwell holds; non-interruptible holds unless higher tier; channel split (Gaze+Voice goal runs
  beside a Legs goal); suspend → resume → continue; resume re-check fails → exit; stack cap + timeout;
  flap detector; "why not" names the first failed gate.

### Task 1.3 — Memory, Mind, senses, shared services
- [ ] `PlayerSnapshot` (10 Hz table from `SessionPlayers`: position, flat forward, held item +
  menacing, sprinting, profile id, capsule radius; zero-alloc). `SightLine` (non-allocating LOS:
  `RaycastNonAlloc`, one layer policy; move `PerceptionModule.SolidGeometryLayers` here and point
  `ObserverCheck`, `PhysicsGroundProbe`, `PerceptionModule` at it).
- [ ] `Memory`/`Fact` per spec §4.3 (Source Seen/Heard/Told/Hit, NoticedAt reaction delay,
  Confidence fuzz+decay, LastSeen, DamageTaken, GrudgeUntil, StimulusSlot[] by StimulusKind);
  first-hand-only announcement rule; long-term per-player memory slot for residents (filled in P5).
- [ ] `Mind`: `Anger` (AggressionMath, unchanged maths: bands, jostle ladder, settle, `Announces`),
  `Fear` (same maths, own settings), `Threat` (AgentTargeting's scoring as a function of Memory;
  grudge pins), `Personality` (nerve, temper, speed-drift seed; rolled per instance from identity).
  `IGrudgeHolder` implemented by Mind **and** by `ProvocationModule`; `FactionRelations` uses the
  interface (D5). `AgentHostility` facade (`IsFightingWith`, `IsHostileBand`, `IsBusy`) used by
  `DialogInteraction`, `SettlementAlarm`, `Resident` defender detection — answering from the old
  components or the brain, whichever the agent has.
- [ ] Senses: `Sight` (today's rules: FOV, body *or* head LOS, sandstorm factor, triggers ignored,
  TargetingProfile ranges folded in), `Hearing` (`INoiseListener` interface added to the `Noise`
  registry; `NoiseReceiverModule` implements it too), `Touch` (JostleSensor's position-delta rule,
  reusing its statics), `Menace` (MenaceSensor's shot-then-aim rule).
- [ ] Reactions: `ReactionDefinition` row (trigger · conditions · effects) + `EffectKind` (anger
  ±/→band, fear ±/→value, pin attacker, alert allies (first-hand only, `callForHelp` leash +
  interval), legs hold, join ally (Told fact pinned)); the six rows of spec §4.6 table expressible.
  Goodwill stays the existing consequence component (not a reaction).
- [ ] Tests: Memory source/decay/reaction delay; Told facts never announced (cascade cap); Anger
  parity with `ProvocationTests`' meter cases; Fear maths; Sight against `PerceptionLineOfSightTests`
  geometry; Hearing gets `Noise.Emit`; reactions fire the right effects; restore fires none.

### Task 1.4 — Brain assembly, goals (first four), adapters, Presence
- [ ] `Brain` (plain C#, owned by `AgentController` when `profile != null`): senses → memory →
  reactions → mind → selector → active goals → `BodyChannels`. `AgentController` gains
  `[SerializeField] BehaviourProfile profile`, `Brain Brain`, registers with `AgentTicker` on enable.
- [ ] Goals: `Wander` (radius 0 = idle; port WanderModule incl. failed-pick backoff), `FollowDirective`
  (reads `AgentGoal` as the directive: hold/face/speed/reason/siteId), `Investigate` (fuzzy position
  then sweep), `Flee` (away policy; shelter/home policies stubbed with a clear `NotSupported` gate
  until P3/P5), `Sleep` (until disturbed; port DormantModule wake rules).
- [ ] Free-Gaze fill: glance/look-around/watch behaviour when no goal claims Gaze (ports
  WatchModule + IdleLookAroundModule rules).
- [ ] Adapters (D3): `BrainLegsModule : BehaviourModuleBase` (priority 98 so it outranks
  old modules on a profiled prefab; returns null when the brain holds no Legs claim; converts
  `LegsRequest` → `MoveIntent`, Pace → `IsRunning`/`SpeedMultiplier` via one mapping table) and
  `BrainGazeModule : IFacingModule` (FacingPriority 99 — strictly above the legs adapter, or
  `FacingApplies` never lets Gaze override). Brain output is read only where the server
  owns the body; a client-owned (ridden) body ignores it.
- [ ] Threat mirror (D4): `AgentTargeting.Pin/Unpin`; brain pins Threat, writes last-known + age.
- [ ] `AgentPresence : NetworkBehaviour`: `NetworkVariable` stance (from winning goals' `Presents`),
  focus (`NetworkObjectId`), activity id; ticks chatter per machine and telegraph presentation on
  every machine. `DialogInteraction` refuses per player via focus (through `AgentHostility`).
- [ ] Tests: brain on a stub agent picks Wander, suspends it for Flee on a Hit reaction, resumes it;
  Legs adapter conversion; Gaze fill yields to a Gaze-claiming goal.

### Task 1.5 — BrainSaveable, legacy read, AgentValidator, goal table
- [ ] Persistence core: `ILegacyStateReader { string[] LegacyKeys; void RestoreLegacy(string key, JObject state); }`
  in `SaveableEntity.Restore` (offer keys with no saver on the entity; document in Persistence.md).
- [ ] `BrainSaveable` (key `brain`, `IDeferredSaveable`, LoadOrder after `MountSaveable` (Early) —
  same-entity only, D7): memory (entities as `SaveRef`, **pending until the player binds**, idempotent
  across `PlayerBound` passes, consume on resolve), mind, active + suspended goals with `Capture`,
  directive. Restore writes state directly. Claims legacy `provocation` (D6: missing `aggression` with
  aggressor → Max) and `resident` (drop `knownDeaths`). `SaveablePolicy`: `AgentController` with a
  profile → `BrainSaveable`; **refuse** old savers on profiled prefabs.
- [ ] `AgentValidator` (editor): checklist with Fix per row (spec §7): body kind → motor, animator
  driver, ground conform, health, faction, NetworkObject + NetAuthority + NetRelay +
  `AgentPresence`, network-prefab registration, savers via `SaveablePolicy`; refuses profile + old
  movement modules; no attack skill with `Fight`; no `Speak` with `Warn`; `occlusionLayers` Nothing;
  VisionBaseline minima (D16). Skips prefabs open in Prefab Mode. `AgentValidatorTests` runs it over
  every agent prefab (floor: sweep finds ≥ 40) — migrated prefabs must pass all rows, unmigrated
  ones only the body/netcode/saver rows.
- [ ] Inspector: `AgentControllerEditor` — profile summary (generated from gates/rows), play-mode goal
  table (tier, channels, state/step, desire, first failed gate per loser, suspended stack, body state,
  leg source), mind/memory view, event log; scene label toggle. `BehaviourProfileEditor` with
  `+ Add` from `TypeCache`.
- [ ] Tests: BrainSaveable round-trip (memory, mind, suspended goal); legacy `provocation` old-shape
  and new-shape; legacy `resident`; pending player fact resolves on a later pass; restore fires no
  reaction; validator refuses the mix.

### Task 1.6 — RobotHorse on a profile (Phase-1 proof) + Dormant tier
- [ ] `Grazer` profile v0 (`Assets/Game/ScriptableObjects/Agents/Profiles/Grazer.asset`): Sight,
  Hearing, Touch; reactions Hit → fear (RobotHorse has no fight today), Gunshot → fear; goals Flee
  (values from RobotHorse's FleeModule: trigger 25, safe 60), FollowDirective, Investigate? (only if
  RobotHorse has NoiseReceiver investigate today — keep behaviour identical), Wander (values from the
  prefab), Sleep no.
- [ ] Migrate `RobotHorse.prefab`: add profile + `AgentPresence` + `BrainSaveable` +
  `BrainLegsModule` + `BrainGazeModule`; remove FleeModule, NoiseReceiverModule, ProvocationModule
  (if its job is now Mind), WanderModule, PerceptionModule and their savers. Keep Mount/Steer/seat
  components. Validator green.
- [ ] Dormant tier: brain asleep when `Offstage` or beyond a serialized distance from every player
  **and** not important (group member, has directive, in Combat/Alert, vessel rider); woken by
  noise/plan; promotion through the `NetworkedTeleport`/`SaveTeleport` seam when unobserved.
- [ ] Verify (spec §13 P1 "done when"): host — wanders, flees a gunshot, resumes wandering; ridden by
  a **client** — the server brain still senses and its fear shows in Presence (stance) while the rider
  drives the legs; save/quit/load mid-flee → `brain` key in the JSON, flee resumes or exits cleanly.
  Benchmark re-run; numbers into the progress file.
- [ ] **Gate (spec §14.17):** if channels or suspend/resume do not hold on RobotHorse, stop and
  report — the spec gets revised before Phase 2.
- [ ] Docs: new `docs/AI/systems/AgentBrain.md` (Model → Key types → Flows → Multiplayer →
  Persistence → Gotchas → Extending; symptoms frontmatter), entry in `docs/Human/the-systems.md`,
  AgentSystem.md coexistence section, Persistence.md (`ILegacyStateReader`), skill
  `spacegame-agent` (restated rule, spec §14.15).

### Task 1.7 — Phase-1 review gate (independent reviewer subagent; fix findings).

---

## Phase 2 — Body: leg control and motors

### Task 2.1 — LegsRequest native, Pace, request shaping
- [ ] `IMotor` (narrowed `IMovementMotor`): `Tick(in LegsRequest, float)`, `Velocity`, `TopSpeed`,
  `IsImmobile`, `HasReachedDestination`, `ForceStop`, `CurrentDestination` (only if a caller
  remains). Each motor gets a gait table `Pace → speed`; NavMesh: repath throttle scaled by distance
  with a minimum interval, arrival braking, turn rate in °/s; legged drivers honour `Pace` (no more
  `SpeedMultiplier` clamp to 1).
- [ ] Old modules keep working through one shim in `AgentController` (`MoveIntent → LegsRequest`),
  so unmigrated prefabs behave identically. Update `LassoTests`/`CryoFreezeTests` fakes.
- [ ] Tests: pace mapping per motor; repath throttle; legged pace distinguishes wander from chase.

### Task 2.2 — Leg control stack, body state owner, OffMeshTransit, IMotorState
- [ ] `LegControl` stack on `AgentController`: exactly one source drives the legs — Puppet
  (ArrivalDirector; replaces toggling `AgentController.enabled`), Carrier (motor carry), Seat
  (`NpcSeating`/`NpcPassenger`; replaces `RidesAsPassenger` switching movement off), Rider
  (`SteerModule`; replaces the `riderDriveFrame`/`riderFrame` stamps in NavMesh/Hover/Ornithopter/
  Legged motors), Brain. The brain never stops for any of them.
- [ ] `BodyState` gets one owner with transitions in one place; `AgentRagdoll`, `ArrivalDirector.QuietHull`,
  status suppression and `CarriedBody` write through it (fixes the `AgentRagdoll` race and the
  `isKinematic` bypass).
- [ ] `MountModule`, `SteerModule`, `CrawlerToolModule` stop deriving `BehaviourModuleBase` (D14):
  Steer is the Rider source; Mount's module suppression becomes "Rider source active"; Crawler tool
  a plain component. Keep `allowAISelfMovementWhenMounted` semantics (passenger seats / conjurer).
- [ ] `OffMeshTransit` helper (leap, mounted jump, carry arcs) keeping `IsLeaping/IsCarried/
  GroundOffset/NavSurfaceY` readable for `AgentGroundConform`.
- [ ] `IMotorState` per motor replaces `MotorStateSaveable`'s type switch (key `motor` unchanged;
  same JSON blocks). Covers CrabWalker6, HumanoidRobot, RoverNoHierarchy too.
- [ ] Tests: source arbitration (Puppet > Carrier > Seat/Rider > Brain); rider drive without frame
  stamps (`RiderTurnChannelTests`, `RiderLookaheadTests`, mount tests stay green); motor state
  round-trip per motor.
- [ ] Verify: every agent moves (spot-check one per motor kind), ridden mount on a client, arrival
  hull, lasso/rocket carry, ragdoll recover; reload a ridden mount. Docs: Locomotion.md,
  MountSystem.md, Vehicles.md, AgentBrain.md, Arrival docs.

### Task 2.3 — Phase-2 review gate.

---

## Phase 3 — Grazers + Wanderers (Appa first)

### Task 3.1 — Warn goal, Fight-lite for Appa, shelter/home flee policies
- [ ] `Warn` (Wary: faces + barks via Presence; Drawn: stands + aim gesture; Gaze Voice (+Legs when
  Drawn)) — ports `AggressionTelegraphModule` incl. its server→client band broadcast through
  generalised `AgentActionRelay` (one presentation channel: attacks, roar, wake, war cry, death).
- [ ] `Fight` minimal FSM sufficient for Appa's cornered/enraged melee (approach, wind-up, strike,
  recover) using a `MeleeAttack` skill ported from `CloseCombatModule` (DamageKind.Melee, blockable).
  Full Fight lands in Phase 4 — design the FSM so Phase 4 extends rather than replaces it.
- [ ] Appa's FightOrFlight as profile data: reactions Hit ≥ `enrageDamage` (60) → fear 0, anger
  Grudge; cornered (`EscapeExists` false within `corneredDistance` 13.5) → Fight gate; flee
  trigger 22 / safe 48.
- [ ] `PettableModule` reads mood from `AgentPresence` stance (fixes always-Calm on clients) and the
  pet request is server-handled regardless of who owns the body.

### Task 3.2 — Migrate Appa, Sandloper, Ostrich, RigWalker, DesertCrawler; finish RobotHorse
- [ ] Grazer profile complete; Wanderer profile (RigWalker, DesertCrawler; `CrawlerToolModule` kept
  as a plain component). Migrate each prefab (validator green, old modules + savers removed). Values
  read **from the prefabs** (prefabs_savers §4 Grazer/Wanderer tables).
- [ ] Scenarios (spec §12): Appa flees a gunshot; fights when cornered; frightened while ridden by a
  client (Presence stance visible on the client, rider keeps control); pet works on a client;
  save/load mid-flee and mid-rage. Benchmark re-run.
- [ ] Docs: AgentBrain.md (profiles table), MountSystem.md, Saddles.md if touched; tests rewritten
  per callsites B (ProvocationTests Appa/Golem rows as applicable).

### Task 3.3 — Phase-3 review gate.

---

## Phase 4 — Fight, skills, coordinator; Predator, Robots, Conjurer, ArmedTribe

### Task 4.1 — Fight complete + skills + EngagementCoordinator
- [ ] `Fight` FSM (approach, position, wind-up, strike/fire, recover); runs seated with Legs
  discarded (outrider). Skills: `MeleeAttack`, `ProjectileAttack`, `HeldItemAttack` (from
  `NpcItemUseModule`: aim via Gaze, `TryUseForward`, reload lull), `SpellAttack` (from
  `ConjurerCastModule`: charge, lightning, Idle while casting), `Defend` (from `MeleeDefense.Decide`;
  a defended blow does not knock back), `KeepDistance` positioning folded into Fight's position state.
- [ ] `EngagementCoordinator` per target: shooter tokens, melee ring slots; those without a token
  reposition. Tests: ≤ N shooters; ring slots distinct; token release on death/loss.
- [ ] Rewrite tests per callsites B: MeleeDefenseTests, CharacterActionRuleTests,
  MeleeMovesetRuleTests, AgentActionBroadcastTests (melee), HumanoidWiringAssetTests rows;
  `CharacterActionWiring` writes skill fields instead of module fields.

### Task 4.2 — Profiles + migration: Predator, RobotSoldier, RobotRider, Conjurer
- [ ] Predator (DuneRat, Vrescal: faction-hostile; Golem: peaceful-until-provoked — model as a
  profile flag or a separate `PredatorProvoked` profile; decide by which keeps one-asset-per-type
  cleaner and record it), RobotSoldier (Clanker: Search → Investigate after target loss, formation
  stays old until P6 — so **Clanker's FormationModule stays**: migrate Clanker in Phase 6 if the
  validator refuses the mix; otherwise give FormationModule a temporary Directive-writer role. Decide
  and record), RobotRider (ClankerOutrider has **no attack module today** — keep it attack-less
  unless the user decides otherwise; seated behaviour unchanged, spec §15 Q5), Conjurer (Sleep +
  SpellAttack; no PerceptionModule today → Sight sense with its 28 m ranges).
- [ ] Scenarios: Clanker squad keeps ≤ N shooters and reloads; search after losing a target;
  outrider seated behaviour unchanged (mount brain chases to `ChaseStopDistance`, rider aims);
  conjurer wakes and casts on a client (wake + roar presented on every machine); host+client+reload.

### Task 4.3 — ArmedTribe (Nomad, Nomad_*×4, SkyNomad_*×4, Astronaut, BountyHunter)
- [ ] ArmedTribe profile (held items via HeldItemAttack, fists fallback; alert radius 35, no
  announce-sightings; BountyHunter's targeting override 120/170/8 becomes personality/instance data
  or a second profile — record the choice). Formation members (BountyHunter) as in 4.2.
- [ ] Scenarios: armed nomad positions and fires; melee blockable both ways; war party on a client.
- [ ] Docs: Combat.md, AgentBrain.md, WeaponSystem.md if touched, SkyTribe.md.

### Task 4.4 — Phase-4 review gate.

---

## Phase 5 — People: Villager, Drifter, residents

Requires the Task 0.6 baseline. Map: `residents_groups_mounts_net.md` §1.

### Task 5.1 — Step plans, Speak/Gesture/UseItem, Encounters, Participate
- [ ] `Method`/`Step`/`StepRunner`: primitives `GoTo · Face · Pick · Drop · Hold(cue, s) · Say ·
  Wait · UseSkill`, precondition re-checked at step start; suspend puts a held prop at a valid rest
  (or keeps it in Hands) and saves the step index. Promote `ChoreDefinition`/`ErrandRunner`/`ErrandStop`
  onto it (ErrandStop already maps; add Say/UseSkill/preconditions/step index).
- [ ] `Encounter` (participants, roles, turn state, break condition) for Talk (player or resident
  pair), Trade, Follow, Pet, Lead-by-rope; move `SettlementSociety.Conversations` onto it;
  `Participate` goal (Gaze Voice). `InteractionFocusModule` callers (DialogInteraction,
  Conversations, ResidentVoice) move to Participate.
- [ ] Skills `Speak` (ResidentVoice + ChatterModule lines; per-machine chatter stays in Presence),
  `Gesture` (`CharacterActions`), `UseItem`.

### Task 5.2 — FollowPlan, Awareness, kin reactions, resident memory
- [ ] `FollowPlan` (read-only `plan.At(now)` — exists at `DayPlan.cs:41`; tolerates arriving late;
  Legs Hands) ports `ResidentRoutine.Evaluate`; `Awareness` sense ports `ResidentAwareness`
  (`PlayerRead`); kin/defender reactions port `Resident.cs:313-331`; long-term per-player memory
  (ResidentMemory acquaintances/favor/deeds) lives in Memory, saved in `brain` (legacy `resident`
  read already in place). `Resident` becomes **data-only identity** (keeps the 27 scene instances'
  overrides in Chunk_6_3 intact — verify by diffing the scene's override count before/after).
- [ ] `ResidentStackBuilder` shrinks to "ensure body + assign profile"; `ResidentsWindow`/
  `ResidentInspector` read the goal table.

### Task 5.3 — Villager + Drifter profiles; migrate 20 Raxy + 5 Drifters
- [ ] Villager (fists, `callForHelp` 1 s; 18 carry `Resident`), Drifter (fists, no call for help).
  `SculptCharacterBuilder` → "ensure body + assign profile" (no more string-named modules).
- [ ] Scenarios: timid villager shelters, bonded one joins; a Raxy's call for help reaches allies
  within the leash and stops at the next camp; chore interrupted by a fight resumes with its prop;
  settlement day matches the Task 0.6 baseline (re-run the harness, compare CSVs); host+client+reload.
- [ ] Tests: MindTests module-coupled parts, AlertChainTests rewritten on the brain; ErrandTests,
  PropErrandTests against step plans.
- [ ] Docs: Residents.md, Errands.md, ResidentReputation.md, AgentBrain.md, Human docs chapter on
  settlements if the *shape* changed.

### Task 5.4 — Phase-5 review gate.

---

## Phase 6 — GroupBrain, Task goal, seats; NomadOstrich, formations

### Task 6.1 — GroupBrain + directives
- [ ] `GroupBrain` (plain C#) owned by `NpcGroup` (caravan, war party), `Companions` (pairs), herds,
  squads: directives `GoTo`, `FollowLeader`, `Hold`, `Board(seat)` with tier + `AllowReflexes`;
  members obey at Directive tier; steering cohesion replaces `FormationModule` column logic.
  `NpcWorldSim` writes directives instead of `AgentGoal`/`NpcTaskModule`/`FormationModule`; lead
  reads (RefreshLead/RefreshQuarryLead) query Memory instead of AgentTargeting. `SettlementPopulation`,
  `RobotSettlementGenerator` (scene migration of placed Clankers' `formationId`/`isLeader`/
  `patrolRadius` in Chunk_7_5 — read prefabs_savers §3) and `WarPartyDirector` (war cry via relay)
  move.
- [ ] `Task` goal (kind of place + dwell; ordered places = patrol) replaces `NpcTaskModule` and
  `PatrolModule`; keeps the Phase-0 caravan resume fix.

### Task 6.2 — Seats as a leg source; migrate NomadOstrich, Clanker/BountyHunter formations
- [ ] Seats via LegControl Seat source; NPC rider behaviour unchanged (spec §15 Q5);
  `PassengerSeat.ForgetIgnored` becomes a Sight-sense exemption; `NpcSeating` no longer flips
  `RidesAsPassenger`. `NpcSpeechTokens` reads Task goal state.
- [ ] Migrate NomadOstrich and anything left on FormationModule/NpcTaskModule/PatrolModule.
- [ ] Scenarios: caravans keep their task across spawn/fold; war party hunts and settles; vessels
  (sky tribe) board/land/unload; outrider on host+client; reload mid-caravan.
- [ ] Docs: SkyTribe.md, Vehicles.md, NPC world doc, AgentBrain.md; tests RuntimeGroupTests,
  WarParty*, PassengerSeatTests, NpcPassengerTests rewritten where module-coupled.

### Task 6.3 — Phase-6 review gate.

---

## Phase 7 — delete the old stack

### Task 7.1 — Deletion + no-brain agents
- [ ] PlayerShip, DuneOrnithopter: `AgentController` without profile (brainless) hosting the leg
  stack; explicitly marked brainless in the validator.
- [ ] Delete `IBehaviourModule`, `BehaviourModuleBase`, `ModulePriority`, `IFacingModule`,
  `IPresentationModule`, `AgentContext`, `MoveIntent`, every old module, `AgentTargeting`'s scoring
  (Threat owns it; delete the component if nothing reads it), `TargetingProfile` (folded into Sight),
  `ProvocationModule`, `AlertBroadcaster`, `AlertReceiverModule`, `NoiseReceiverModule` (registry
  keeps `INoiseListener`), `PerceptionModule` (`VisionBaseline` stays as Sight defaults), `GoalTravelModule`,
  `AgentGoal` (once nothing writes it), `HealthReactionModule` slimmed to death consequences
  (corpse timer on every machine, `Despawning`, Hurt/Death noises), `BehaviourModuleEditor`, the
  adapters, and the 15 savers in spec §6 (`AgentPacingSaveable` folds into `AgentControllerSaveable`
  — keep the key `agentPacing` to avoid a migration). Legacy keys stay readable via
  `ILegacyStateReader`.
- [ ] Gate: `grep -r IBehaviourModule Assets` → nothing; `AgentValidatorTests` enumerates all 51 (+
  any new) agent prefabs, each on a profile or explicitly brainless; full EditMode suite green
  (standing failures excepted); benchmark ≤ 2 ms/frame at 200 agents.

### Task 7.2 — Docs rewrite
- [ ] AgentSystem.md (or fold into AgentBrain.md and delete), Residents.md, Errands.md,
  MountSystem.md, INVARIANTS.md (restated rule: behaviour is profile data over shared goals; no
  per-creature code paths), the `spacegame-agent` skill + reference.md, `spacegame-tribe` skill,
  Human docs chapters whose shape changed, the-systems.md. `docs_check.py --index` green.

### Task 7.3 — Final review gate + full verification sweep
- [ ] Independent review of the whole diff; host+client+reload scenario sweep across one agent per
  profile; close or explicitly hand over every Verification-debt row.

Phase 8 (NpcWorldSim split, one schedule format, Virtual tier) is a separate spec — out of scope.
