# Discarding the Brain (Phase 1), keeping Phase 0

**Nothing has been deleted yet. This is a plan, not a record.** Everything (Phase 0 and Phase 1) is
uncommitted in the working tree, together with unrelated settlement/ladder/terrain work. The
assistant cannot commit (hook). **Recommended first two steps, both yours:**

1. Commit Phase 0 (and nothing else) so it is safe. Do not use `git checkout`/`stash` on the
   tree; use the hand edits below.
2. Copy `Assets/Game/Scripts/agents/Brain/`, the other delete-list files and the 22 edited files of
   section 4 to a folder outside the repo (see Decision 5) before deleting anything.

## 1. Summary

Delete the whole `agents/Brain/` folder (56 files), its editor tools, its 19 test files, `Grazer.asset`,
`GoodwillHitReporter`, the RobotHorse brain-check harness and two docs. Then undo about 22 small
Phase-1 hooks in original-stack files: 15 of them are pure Phase 1 and can come back from git HEAD
as is; 7 mix Phase 0 and Phase 1 and must be edited by hand. Put `RobotHorse.prefab` back from a
backup (not from git). Everything under section 2 stays; none of it depends on the brain.

## 2. KEEP (Phase 0 gains)

Checked: the grep for brain names finds no Phase-0-only file referencing the brain. The files marked
(mixed) also carry Phase-1 hooks and appear again in section 4.

| Gain | Files (under `Assets/Game/` unless noted) |
| --- | --- |
| 0.1 Search starts after a target is lost; caravans keep their destination across fold/unfold; autoBraking no longer forced off | `Scripts/agents/AI/Targeting/AgentTargeting.cs` (mixed: `LostCount`, `RestoreMemory`), `agents/Modules/Movement/SearchModule.cs`, `Core/Persistence/Adapters/{SearchSaveable,AgentStateSaveable}.cs`, `agents/Tasks/NpcTaskModule.cs`, `agents/World/NpcWorldSim.cs`, `agents/AI/Motors/NavMeshAgentMotor.cs` (also holds another session's off-mesh work), tests `SearchModuleTests`, `NpcTaskResumeTests`, `NavMeshAgentMotorBrakingTests` |
| 0.2 Corpses show on clients; NPC melee can be blocked | `Gameplay/Health/{HealthComponent,NetworkedHealthComponent}.cs`, `agents/Entity/{HealthReactionModule,HidePartsOnDeath}.cs`, `Gameplay/Ragdoll/AgentRagdoll.cs`, `agents/Modules/Combat/CloseCombatModule.cs`, tests `ReplicatedDeathTests`, `NpcMeleeBlockTests` |
| 0.3 Allied players are no intruders in a settlement; old-save grudges survive load; gossip runs at day end | `agents/Faction/{FactionRelations (mixed),SettlementAlarm}.cs`, `agents/Core/EntityTargetRegistry.cs`, `Core/Persistence/Adapters/ProvocationSaveable.cs` (mixed), `agents/Residents/Core/SettlementSociety.cs`, `agents/Residents/Mind/Gossip.cs`, tests `FactionGoodwillLedgerTests`, `ProvocationTests`, `MindTests` |
| 0.4 Module loop allocates nothing; benchmark harness | `Core/Diagnostics/Fault.cs`, `agents/Controller/AgentController.cs` (mixed: only `RunModule`, `ApplyFacingOverride`, `FacingSite`), `agents/Diagnostics/{AgentBenchmark*,BenchmarkChannel,BenchmarkPlayer}.cs`, `Editor/Agents/AgentBenchmark.cs`, tests `AgentModuleBarrierTests`, `FaultBarrierTests` |
| 0.5 About 3.5k lines of dead code gone (herd, ranged, patrol robots, rigidbody motors, HorseDriver, EntityAudioModule, resident leftovers); saver table trimmed; prefab fixes (BountyHunter, NomadOstrich duplicates, DuneRat chase priority, Nomad/Astronaut `hitGain` 1200) | All deletions and edits in handoffs 0.5, 0.5_S2, 0.5_S3b, 0.5_S4, 0.5_finish, incl. `Core/Persistence/Runtime/SaveablePolicy.cs` (mixed), `Settings/WorldNavMesh.asset`, both DefaultNetworkPrefabs lists, `RosterAssetTests.cs` |
| 0.6 Residents baseline harness | `Editor/Agents/{PlayModeHarness,ResidentsBaseline}.cs`, `agents/Diagnostics/{HarnessRun,ResidentsBaselineRun,ResidentsBaselineSettings,ResidentBaselineTrack,ResidentBaselineCounts}.cs`, `docs/AI/systems/ResidentsBaseline.md`. `HarnessRun` gained `MoveToEmptyStage`/`BuildNavMeshSlab` in 1.6; `AgentBenchmarkRun` now calls them, so keep both |
| 0.7 / 0.8 review fixes: `Fault.Report` logs when owner is gone; Allied players not counted as town population; a destroyed target is searched for; resumed caravan leader knows its destination name | `Fault.cs`, `agents/Faction/SettlementPopulation.cs`, `AgentTargeting.cs`, `NpcTaskModule.cs`, comment edits in `NoiseEmitter.cs`, `PatrolSaveable.cs`, `UnderTerrainGuard.cs`, `RagdollRig.cs` |
| Docs for all of the above | `docs/AI/systems/{Combat,EntitySystem,HumanoidAnimation,NavMeshSystem,ResidentReputation,Residents,Diagnostics,Vehicles,Locomotion,SceneTransitions,LeashSystem,Multiplayer,audio,audio-prefab-inventory,SkyTribe}.md`, `docs/AI/DEFECTS.md`, `docs/Human/{04,08}*.md`, skills `spacegame-agent`, `spacegame-multiplayer`, `spacegame-tribe` (all brain-free) |

## 3. DELETE outright (each file together with its `.meta`)

| What | Paths |
| --- | --- |
| Runtime brain (56 `.cs`) | `Assets/Game/Scripts/agents/Brain/` and `Assets/Game/Scripts/agents/Brain.meta` |
| Empty staging skeletons | `Assets/Game/Scripts/agents/Brain~/`, `Assets/Game/Editor/Agents/Brain~/`, `Assets/Game/Editor/Tests/Brain~/` (0 files in each) |
| Brain editor code | `Assets/Game/Editor/Agents/Brain/` (`AgentValidator`, `AgentControllerEditor`, `BehaviourProfileEditor`, `BrainReadout`, `GrazerProfileBuilder`) and `Assets/Game/Editor/Agents/Brain.meta` |
| RobotHorse brain-check harness | `Assets/Game/Editor/Agents/RobotHorseBrainCheck.cs`, `Assets/Game/Scripts/agents/Diagnostics/{RobotHorseBrainCheckRun,RobotHorseCheckSettings}.cs` |
| Profile asset | `Assets/Game/ScriptableObjects/Agents/` (only `Profiles/Grazer.asset` inside) and `Assets/Game/ScriptableObjects/Agents.meta` |
| Goodwill reporter | `Assets/Game/Scripts/agents/Faction/GoodwillHitReporter.cs` (only the brain and the old `ProvocationModule` rewrite use it; HEAD's module reports goodwill inline) |
| Phase-1 tests, all in `Assets/Game/Editor/Tests/` (19) | `AgentHostilityTests`, `AgentTickerTests`, `AgentValidatorTests` (holds `KnownFailures`), `AngerTests`, `BrainAssemblyTests`, `BrainCoreTests`, `BrainProfileTests`, `BrainReadoutTests`, `BrainSaveableTests`, `BrainSenseTests`, `BrainTestDoubles`, `BrainTestRig`, `ConditionTests`, `GoalSelectorTests`, `MemoryTests`, `ReactionTests`, `StubCustomCondition`, `StubGoalDefinition`, `ThreatTests` |
| Docs | `docs/AI/systems/AgentBrain.md`, `docs/AI/systems/AgentBrainTooling.md`; the two entries "The new creature brain" and "Building a creature and watching it think" in `docs/Human/the-systems.md` (lines ~149-160) |
| Generated | `docs/AI/INDEX.md`, `docs/AI/ROUTING.md`: never hand-edit; regenerate with `docs_check --index` (section 7) |

Not deleted, on purpose: `docs/superpowers/plans/2026-10-02-agent-restructure*.md` (untracked plan and
progress record) and the committed spec in `docs/superpowers/specs/` (Decision 4).

No scene, prefab or asset other than RobotHorse and Grazer references a brain script GUID (GUID scan of
all 73 Brain/editor/Grazer GUIDs over `Assets/`). `DefaultNetworkPrefabs` needs no change: RobotHorse's
path and GUID never changed.

## 4. UNDO small edits in original-stack files

### 4a. Pure Phase 1: take the HEAD version
Each file below has only Phase-1 hunks (verified with `git diff`); its HEAD text compiles against the
Phase-0 tree (checked: no HEAD reference to a Phase-0-deleted class, and `MoveIntent.MoveTo` callers use
the named `isRunning:`). Re-run `git diff <file>` right before restoring, in case another session touched it.

| File (under `Assets/Game/Scripts/`) | What the Phase-1 hunk was |
| --- | --- |
| `agents/Modules/Movement/WanderModule.cs`, `agents/Modules/Movement/FleeModule.cs` | Call `WanderGoal.TrySample/SampleRadius` and `FleeGoal.AwayFrom/TryFindDestination`, which live in `Brain/`. **Compile breaks if Brain/ goes first.** HEAD has the original inline loops |
| `agents/AI/Targeting/ProvocationModule.cs` | Rewritten as a host over `Anger`, implements `IGrudgeHolder`, 3-arg `HeardGunshotFrom`. HEAD is the original. Loses only two cosmetic Phase-0 comment fixes (a "herd", `AgentRangedCombatModule` in the header) |
| `agents/Perception/AlertBroadcaster.cs` | Static `Broadcast(...)` extraction plus `Brain.ReceiveAlert` branch |
| `agents/Audio/Noise.cs`, `agents/Audio/NoiseReceiverModule.cs` | Registry retyped to `INoiseListener`; `NoiseTypeMasks` extracted. `NoiseEmitter.cs` is Phase 0: keep |
| `agents/Perception/JostleSensor.cs` | `AgentRadius`/`PlayerRadius` made public statics for `TouchSense` |
| `agents/Residents/Body/ObserverCheck.cs`, `Vehicles/SkyVessel/PhysicsGroundProbe.cs` | `SightLine.SolidGeometryLayers` instead of `PerceptionModule.SolidGeometryLayers` (and a `using`) |
| `agents/Residents/Mind/PlayerRead.cs`, `agents/Residents/Mind/ResidentAwareness.cs`, `agents/Residents/Speech/ResidentVoice.cs` | Calls routed through `AgentHostility` / `IGrudgeHolder` |
| `Gameplay/Interaction/Interactions/DialogInteraction.cs` | `IsFightingWith` delegated to `AgentHostility` plus `IsBusyWith`. HEAD has the full original body |
| `Core/Persistence/Format/ISaveable.cs`, `Core/Persistence/Runtime/SaveableEntity.cs` | `ILegacyStateReader` interface and `RestoreLegacyKeys`/`HasSaverFor` |

### 4b. Mixed Phase 0 + Phase 1: edit by hand (no git copy of the pre-Phase-1 state)

| File | Revert (Phase 1) | Keep (Phase 0, same file) |
| --- | --- | --- |
| `agents/Controller/AgentController.cs` | `using SpaceGame.Agents.Brains`; `[Header("Brain")] profile` field; `Profile` and `Brain` properties; the `if (profile != null) { Brain = new Brain(...); AgentTicker.Register }` block at the end of `Awake`; the whole `OnDestroy` | `RunModule`/`ApplyFacingOverride` with `Fault.TryEnter`/`Report` and `FacingSite` (alloc-free loop); herd field/publish removal; the new motor error text; `RefreshModules`/`RefreshMotor` removed |
| `agents/AI/Targeting/AgentTargeting.cs` | `using ...Brains`; `IsPinned`, `IsBrainDriven`, `brain` field; `BindBrain`, `Pin`, `Unpin`, `PublishMemory`; the `if (IsBrainDriven) { brain.TellThreat }` head of `ForceTarget`; `IsBrainDriven \|\|` in `Update`; `&& !IsPinned` (x2) and `\|\| IsPinned`; `IsPinned = false;` in `ClearTarget`; **`RecomputeEffectiveRanges`**: inline `WidenedAcquisitionRange/WidenedLoseRange/LongestWeaponRange` back into one method | `LostCount` and its `++` in `ClearTarget`; `RestoreMemory(target, targetWasHeld, ...)`; `ReferenceEquals` + `vanished` handling; `perception.IsVisible` instead of `CanSee`; the Phase-0 deletion of the ranged-module loop (do **not** restore it from HEAD) |
| `agents/Perception/PerceptionModule.cs` | `using ...Brains`; `SolidGeometryLayers => SightLine...`; `SightLine.SolidGeometryLayerNames` in the Awake warning; `AimPointOf`, head point and `CanSightReach`/`HasLineOfSightFrom` calling `SightLine.Unobstructed/HeadPoint/AimPoint`. Re-inline from `git show HEAD:` (lines ~79-91, 182, 190-260): `FallbackOcclusionLayerNames`, `solidGeometryLayers`, `colliderBuffer`, `AimPointOf`, `HeadPointOf`, `IsUnobstructed` (the RaycastAll version) | Memory, `CanSee`, `NotifySpotted`, spot fields, `noiseEmitter` stay deleted; `IsVisible` and `HasLineOfSight` stay; header and comment text |
| `Core/Persistence/Runtime/SaveablePolicy.cs` | `using ...Brains`; `&& !IsProfiled(go)` on the AgentState clause; the `BrainSaveable` add block at the top of `EnsureAgentMind`; `&& !RefusedOnProfiled(...)` on 8 clauses (Provocation, Search, Alert, Noise, Flee, Wander, Pursuit, CombatCadence); the block `RetiredOnProfile` / `IsProfiled` / `RetiredOnProfiled` / `RefusedOnProfiled` | `AgentProjectile` row removal; BasePatrol, Herd, HealthReaction clauses gone; CombatCadence without ranged; Motor clause = three motors; comment rewrites |
| `Core/Persistence/Adapters/ProvocationSaveable.cs` | Make `Read(JObject)` private (nothing else calls it once BrainSaveable is gone) and drop the "Shared with BrainSaveable" sentence | `PredatesTheMeter` rule: old record with an aggressor and no `aggression` key restores as `AggressionMath.Max`; the `State.aggression` doc |
| `agents/Faction/FactionRelations.cs` | `using ...Brains`; `HoldsGrudgeAgainst` body back to the HEAD text (`ProvocationModule` check on `aggressor.root == other.transform.root`) | `Resolve(definition, table, other)`, the one-direction `ResolveGoodwill(tribe, player)` refactor, header |
| `agents/Residents/Core/Resident.cs` | `using ...Brains`; `AgentHostility.Raise(Provocation, ...)` back to `Provocation.Raise(...)` | `ClearOverride` removal |

### 4c. Docs and skills (remove only the Phase-1 text; Phase-0 text in the same file stays)

| File | Remove |
| --- | --- |
| `docs/AI/systems/AgentSystem.md` | Line ~90 "Coexistence with the brain" bullet. In rows: `AgentTargeting` (~112: "static `WidenedAcquisitionRange`... shared with the brain's `SightSense`" and the `Pin/Unpin/BindBrain` sentence); `ProvocationModule` (~115: "The rules live in `Brains.Anger`... implements `IGrudgeHolder`... through `AgentHostility`" and the `JostleSensor` "shared with the brain's `TouchSense`" clause); Faction (~116: `AgentHostility`/`IGrudgeHolder` path and "Fed by `GoodwillHitReporter`"; say the module reports it); `PerceptionModule` (~128: `SightLine` sentence and the `sight.baseline` validator sentence); `AlertBroadcaster` (~129: static `Broadcast` and "An ally with no receiver but a profiled..."); `Noise` (~130: `INoiseListener`, `NoiseTypeMasks`); gotcha `ForceTarget` self-check (~183: replace "`Anger.Attack` guards this" by the old module's guard). Also reword the Phase-0 text "AgentValidator (P1) to re-cover" (Perception row) to "not re-covered" |
| `docs/AI/systems/Persistence.md` | `ILegacyStateReader` key-type row (~70); the "Agent mind" row's `Brain` saver clause (~84); the version-bump-migration gotcha row (~140) and the `ILegacyStateReader` clause in Extending 9 (~183); the symptom "a creature migrated to the brain forgot its grudge..." (~31) |
| `docs/AI/systems/MountSystem.md` | Section "A profiled mount's brain while ridden" (~56-60). Keep the Phase-0 motor rows and `updated:` |
| `.claude/skills/spacegame-persistence/SKILL.md` | `BrainSaveable` row (~194); "not on a profiled agent" in the `AgentStateSaveable` row (~193); the `ILegacyStateReader` common-mistake row (~265) |
| `.claude/skills/spacegame-agent/SKILL.md` | Line ~129: "the AgentValidator test (agent restructure, phase 1) is to" (written in 0.5; reword to "nothing enforces this") |

Then bump `updated:` on each edited doc (section 7 validates).

## 5. RobotHorse.prefab

`Assets/Game/Prefabs/agents/creatures/RobotHorse.prefab` currently runs on the Grazer profile.

- **Restore from the backup `scratchpad/RobotHorse.prefab.bak16`** (full path:
  `/private/tmp/claude-501/-Users-ferdinandfremming-Documents-hackerspace-spillgruppen-SpaceGame/e2db7b85-2e25-4976-aa3a-4dfb3a94412a/scratchpad/RobotHorse.prefab.bak16`).
  Copy it somewhere permanent now (the scratchpad is session-temporary).
- **Do not use git HEAD for this prefab.** HEAD still carries `HealthReactionSaveable`, which Phase 0
  deleted; it would come back as a missing script. (Compared: HEAD vs backup differ only by that one
  component; backup vs the current file differ only by the migration.)
- Verified by parsing both files: the current prefab differs from the backup in exactly these ways: 10
  old components gone (Perception, NoiseReceiver, Provocation, Wander, Flee, AgentState, ProvocationSaver,
  FleeSaver, NoiseInvestigationSaver, WanderSaver), 5 added (BrainLegsModule, BrainGazeModule,
  AgentPresence, BrainSaveable, GoodwillHitReporter) and `AgentController.profile` set. The file's
  mtime is later (3 Oct 09:54) than the 1.6 migration, but the content shows no other edit. If you
  hand-edited the horse since, those edits are not in the backup: diff first.
- Close Prefab Mode on it before copying, and let Unity reimport.
- Nothing else in assets was touched by the migration: `DefaultNetworkPrefabs` unchanged (1.6 handoff);
  `KnownFailures` lives in `AgentValidatorTests`, which is deleted. `Grazer.asset` is section 3.

## 6. Decisions for you (recommended default first)

| # | Question | Default |
| --- | --- | --- |
| 1 | Keep the validator's brain-free rows (body/net/save) as a small tool? | **Delete it.** It is 595 lines interleaved with brain rows and `SaveablePolicy.IsProfiled`. Keep its 12 findings instead as `DEFECTS.md` rows: no `AgentGroundConform` on Appa, Sandloper, LightningConjurer; no `HealthComponent` on Ostrich, NomadOstrich, DesertCrawler, RigWalker; no `EntityFaction` on RigWalker; PlayerShip and DuneOrnithopter lack health and faction |
| 2 | Keep `SightLine` as a shared line-of-sight helper? | **No, revert to PerceptionModule's inline code.** It needs `AgentTicker.BeginFrame` and the whole Brain namespace to stay coherent. Cost: re-inlining ~100 lines (section 4b) |
| 3 | Keep the `Anger` extraction from `ProvocationModule`? | **No, restore HEAD's module.** ProvocationTests (17) and AggressionMathTests ran green on it before Phase 1; the Phase-0 grudge rule lives in `ProvocationSaveable`, not the module |
| 4 | Keep the plan, progress file and spec in `docs/superpowers/`? | **Keep**, add one line at the top of the progress file saying Phase 1 was discarded on this date |
| 5 | Back up Brain/ first? | **Yes.** Copy into e.g. `/Users/ferdinandfremming/Documents/hackerspace/spillgruppen/brain-phase1-backup/` (outside the repo): the delete list of section 3, the current copies of the 22 files of section 4, and `RobotHorse.prefab` (migrated). Without it only this working tree holds the 7.7k lines |

## 7. After deleting: four checks

1. `python3 tools/typecheck.py --editor`: both assemblies must report no errors.
2. `python3 tools/docs_check.py --index`: regenerates INDEX/ROUTING, then 0 errors and 0 warnings.
3. EditMode fixtures (filter regex): `SearchModuleTests|NpcTaskResumeTests|NavMeshAgentMotorBrakingTests|ReplicatedDeathTests|NpcMeleeBlockTests|FaultBarrierTests|AgentModuleBarrierTests|FactionGoodwillLedgerTests|FactionRelationsTests|ProvocationTests|AggressionMathTests|MindTests|AlertChainTests|HostileDialogTests|PerceptionLineOfSightTests|PerceptionAimTests|AgentActionBroadcastTests|EntityPersistenceTests|PrefabPersistenceTests|NetworkPrefabRegistrationTests|RosterAssetTests|NpcWorldSimTests`; baseline before Phase 1 was 3263 run, 69 failed (known set: NetGun, Hogtie, Lasso, PackSize, ...).
4. Leftover grep over `Assets docs .claude tools`: `Agents.Brains|BehaviourProfile|AgentPresence|BrainSaveable|BrainLegs|BrainGaze|AgentHostility|IGrudgeHolder|GoodwillHitReporter|INoiseListener|SightLine|PlayerSnapshot|ILegacyStateReader|WanderGoal|FleeGoal|AgentTicker|BindBrain|PublishMemory|IsProfiled`: expect hits only in `docs/superpowers/`.

## 8. Not determinable from the tree

- Whether the mixed files contain Phase-1 edits beyond the hunks listed: the lists come from `git diff`
  plus the handoffs, but with no commit in between, "Phase 1" is inferred, not recorded.
- Docs hunks in section 4c are located by phrase and approximate line number; those files also carry
  other sessions' edits (git shows 30 docs modified).
- No play, client or reload run exists for either the brain or the post-discard tree.
- Do not touch: the settlement, decoration, ladder, terrain and NavMesh-link work (for example
  `NavMeshAgentMotor.Links.cs`, `Settlement*`, `Ladder*`, `NavLinkAreas.cs`, `ProjectSettings/NavMeshAreas.asset`).
