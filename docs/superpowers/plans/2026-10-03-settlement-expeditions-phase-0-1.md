# Settlement Expeditions — Implementation Plan, Phases 0 and 1

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Also load the repo skills `spacegame-agent`, `spacegame-multiplayer` and `spacegame-persistence` before the tasks
> that touch those areas.

**Goal.** Any settlement with an expedition profile keeps one band of its own residents out in the world at all times.
- **Phase 0** lays the groundwork: identity stamps, group-sim hooks, data, sites, the muster spot, the warrior quota
  and the aimed-gun spike.
- **Phase 1** proves the riskiest part end to end: a **Scout** band of 3 musters at the gate, walks out in view, is
  handed off to stand-ins, travels and searches for days, and walks home as the same people. All of it holds across
  host, client, save/reload and chunk unload.

**Architecture.**
- The world-level, server-only `ExpeditionDirector` lives on the `NpcWorldSim` object in `persistentScene`. It owns
  **every settlement's rotation state and every band record**, so bands keep going while their home chunk is unloaded.
- `NpcWorldSim` only moves a band, as a runtime group with `owner = "expedition"`.
- The settlement-local `SettlementExpeditions`, on each `Settlement`, only performs what needs bodies: the muster,
  the walk-out, the hand-off, the walk-in, and applying `Away` to residents.
- Stand-ins are the residents' own prefabs, spawned by `NpcSpawn` and carrying a replicated `ExpeditionMember`.

**Generic by construction.** Nothing in `Scripts/agents/Expeditions/` names a culture or species. A settlement runs
bands when its `SettlementCulture.expeditions` profile is set and the pieces in §"Making another settlement run
bands" are present. Only the Raxy content is authored here. A test pins the genericity rule (Task 1.9).

**Tech stack:** Unity 6000.3, C#, NGO (`NetworkVariable`, `[Rpc]`), Newtonsoft save adapters, NUnit EditMode.

**Spec:** [2026-10-03-settlement-expeditions-design.md](../specs/2026-10-03-settlement-expeditions-design.md)
(revision 4). Read §1, §3, §4, §5.1–5.2, §10.1–10.2 and §14 first. Phases 2+ get their own plans.

---

## Global constraints

- **Commit only when the user says so.** Each task ends with a commit step; skip it unless commits are authorised.
  Stage only the files the task names, because the working tree holds unrelated work. **Never `git stash` or
  `git checkout` to undo anything** (memory: stash lost 457 files).
- **Other sessions edit concurrently.** The activity-contract plan
  ([2026-10-03-activity-contract-animation.md](2026-10-03-activity-contract-animation.md)) touches `ResidentHands`,
  `BodyLanguage` and seating. Re-read a file immediately before editing it; never overwrite from a stale read.
- **Prefabs and scenes:**
  - Before writing a prefab, check it is not open in Prefab Mode, then snapshot and diff after.
  - `Chunk_6_3.unity` holds hand edits and terrain. Save only that scene (`EditorSceneManager.SaveScene`), **never
    `AssetDatabase.SaveAssets()`** while terrain edits may be pending (memory: it flushes them).
  - Never regenerate the settlement.
- **Saves:** formats are append-only. New fields go at the end of `NpcGroup.Record`, `PlannerResident` and the enums
  that serialize as ints (`SiteKind`, `SpotRole`, `PlaceKind`, `Activity` — a replicated byte). Save keys never change.
- **Authority:** decisions go behind `Network.Decides` (true offline, server online). Residents' server logic uses the
  same gate.
- **No magic numbers:** every tunable is serialized on `ExpeditionTuning` (global) or the profile and goal assets, with
  the spec §12 defaults.
- **Randomness:** expedition rolls use `System.Random` seeded from record seeds, never `UnityEngine.Random`.
- **Verification commands:**
  - Type-check: `python3 tools/typecheck.py --editor` → `No errors.` If a new `.cs` file is invisible to it, Unity
    has not imported it yet (memory: compile visibility; rename-the-file workaround).
  - Tests (needs the Editor, from a clean scene): `SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("<Fixture>")`
    via Unity MCP, or `Tools ▸ Tests ▸ Run EditMode Tests (headless)`. Poll `Temp/headless_tests.txt` for `DONE` and
    expect `FAILED=0`.
  - Docs: `python3 tools/docs_check.py --index` → `0 errors`.

## Decisions this plan takes where the spec left room (and two corrections to it)

| Question | Resolution |
|---|---|
| **Where settlement-level state lives** *(corrects spec §1, §14.2)* | In the **director**, keyed by `settlementId`, saved under `expeditions` in `persistentScene`. A `Settlement` component exists only while its chunk is loaded, but "one band always out" must hold while the player is kilometres away. `SettlementExpeditions` keeps no state of its own. |
| **A departure or homecoming while the home chunk is unloaded** *(corrects spec §10.1 "runs whether or not anyone watches")* | It runs **abstractly**: the record changes phase and the residents are marked `Away` (or released) when the chunk next loads. The muster is performed with bodies only when the chunk is loaded. A loaded but unobserved chunk performs it normally. |
| How the director knows a settlement it has never seen loaded | A **baked catalog** (Task 0.6) holds each expedition-running settlement's id, gate pose and **roster snapshot** (key, archetype, roles, bonds, source prefab). It is refreshed live from the scene whenever the chunk loads (deaths, reassignments). |
| `settlementId` | The `SaveableEntity` id of the settlement's own GameObject, added by Task 0.1. It is identity, not scene or position (`Settlement.Seed` moves with the transform). |
| `residentKey` | `"r:" + Resident.index` for authored residents. Phase 4 adds `"n:" + recordId` for newcomers, under the same key type. |
| The spec's "Gathering"/"Muster" spot names | `SpotRole.Gathering` already exists (evening hearth seats). This plan appends **`SpotRole.Assembly`** and **`PlaceKind.Assembly`**. The muster `SpotUse` asset is `Muster.asset`, and Phase 2's plaza will be `CeremonyGround.asset`. Assembly places are **never planned** as stroll or leisure targets. |
| Stand-in followers | `FormationModule` registers in `OnEnable` with the default id `"caravan"`, so it must **not** be live on residents at home. It goes on resident prefabs **disabled**; a stand-in sets its formation id and enables it before the network spawn. |
| Stand-in identity on clients | Scene overrides (name, archetype) do not exist on a freshly spawned prefab. `ExpeditionMember` (a `NetworkBehaviour` on every resident-capable prefab) replicates `displayName`, the archetype index and the kit index, and applies them on every machine. |
| Exact-pose hand-off | `NpcGroup` gains a one-shot, non-serialized `SpawnPoses` list that `NpcWorldSim.Spawn` consumes instead of formation slots. |
| Phase 1 nights | A `Halt` stage: the record stops, and spawned members stand guard in place. Camps are Phase 2. |
| Phase 1 search targets | Seeded waypoints on a ring, sampled on the world NavMesh (always loaded, NavMeshSystem.md), preferring catalog `Ruin`/`ScrapField`/`Landmark` sites when there are any. Phase 1 does not depend on placed sites. |
| Debug tools | **Chat slash commands** (`ChatCommands.Register`), which already route client → server and reply to the sender. No new UI. |
| Harpoon spike | It uses the **existing working `basicgun`** on a temporary Raxy prefab copy. The question is "can a Raxy aim and fire a held weapon", not "does a harpoon exist". All spike assets are deleted afterwards and only the finding is kept. |

## File structure

| File | Responsibility |
|---|---|
| **Create** `Assets/Game/Scripts/agents/Expeditions/ExpeditionTypes.cs` | `ExpeditionRole` (flags), `ExpeditionPhase`, `StageKind`, `ResidentKey` helpers |
| **Create** `.../Expeditions/ExpeditionRecord.cs` | `ExpeditionRecord`, `MemberRecord`, `StageRecord`, `SettlementState`, `RosterEntry` (plain, serializable) |
| **Create** `.../Expeditions/ExpeditionRules.cs` | Pure rules: goal draw, member picks, stage building and advance, travel hours, hand-off test, rotation |
| **Create** `.../Expeditions/ExpeditionDirector.cs` | World-level server MonoBehaviour: rotation, records, group creation and steering, absence queries |
| **Create** `.../Expeditions/SettlementExpeditions.cs` | Settlement-local performer: roster refresh, muster, walk-out, hand-off, walk-in, applying `Away` |
| **Create** `.../Expeditions/ExpeditionMember.cs` | `NetworkBehaviour` on resident prefabs: stand-in identity, replicated name, archetype and kit; expedition mode |
| **Create** `.../Expeditions/Data/ExpeditionProfile.cs`, `ExpeditionGoal.cs`, `ExpeditionKit.cs`, `ExpeditionTuning.cs`, `ExpeditionCatalog.cs` | Data: per-culture profile, goal (slots, stages, weight, kits by role), kit, global tuning (Resources), index of profiles for replication |
| **Create** `.../Expeditions/ExpeditionCommands.cs` | `/exp` chat commands |
| **Create** `Assets/Game/Scripts/Core/Persistence/Adapters/ExpeditionSaveable.cs` | key `expeditions` |
| **Create** `Assets/Game/Scripts/World/Sites/WorldSiteCatalog.cs` | Baked sites and settlements; merged into `WorldSiteRegistry` at startup |
| **Create** `Assets/Game/Editor/Agents/Expeditions/ExpeditionAuthoring.cs` | Menus: stamp settlements, back-fill `sourcePrefab`, prepare resident prefabs, apply the warrior quota |
| **Create** `Assets/Game/Editor/World/WorldSiteCatalogBaker.cs` | Bakes the catalog by opening chunk scenes additively (pattern: `WorldNavMeshBaker`) |
| Modify `Assets/Game/Scripts/agents/World/NpcGroup.cs` | `Owner` + `Record.owner`; non-serialized `PlannedOverride`, `MemberStamp`, `SpawnPoses`, `ReadBack` |
| Modify `.../World/NpcWorldSim.cs` | Restore rule; an expedition branch in `TickVirtual`; spawn poses; member stamp; read-back before `DespawnMembers` |
| Modify `.../World/NpcGroupComposition.cs` | `PlannedOverride` branch first |
| Modify `.../World/WarPartyDirector.cs` | Owner filters in adopt, template lookup and sightings |
| Modify `Assets/Game/Scripts/agents/Residents/Core/Resident.cs` | `sourcePrefab`; `Away` state; guards in `Update` and `Wake`; `DisplayName` from `ExpeditionMember` |
| Modify `.../Residents/Core/ResidentAssignment.cs` | Stamp `sourcePrefab` |
| Modify `.../Residents/Body/ResidentRoutine.cs` | Skip when `IsAway` (tick, settle, backstop) |
| Modify `.../Residents/Core/SettlementSociety.cs` | `away` in `PlannerResidents`; `Assembly` in `KindOf`; public `RefreshRoster()` |
| Modify `.../Residents/Plan/DayPlanner.cs` | `PlannerResident.away` → empty plan; skipped for bookings and pairing like `dead` |
| Modify `.../Residents/Data/ResidentArchetype.cs`, `SettlementCulture.cs`, `ResidentEnums.cs` | `expeditionRoles`; `expeditions` profile; append `Activity.Expedition`, `PlaceKind.Assembly` |
| Modify `.../Residents/Body/ResidentHands*` | Outside hand rule for `Activity.Expedition`: kit weapon in hand, Carry stance |
| Modify `Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Spots/SpotUse.cs` (+ `HouseRoom.cs:122`) | Append `SpotRole.Assembly`; excluded from house rooms |
| Modify `.../Settlement/Core/Settlement.cs` | `SettlementId` getter (from its `SaveableEntity`) |
| Modify `Assets/Game/Scripts/World/Sites/SiteKind.cs` | Append `Outpost`, `AntennaSite` |
| Modify `Assets/Game/Scripts/agents/Residents/Editor/ResidentValidator.cs` | `CheckExpeditions` |
| Tests: `Assets/Game/Editor/Tests/` | `ExpeditionRulesTests`, `RotationTests`, `ExpeditionPersistenceTests`, `ExpeditionGroupTests`, `ExpeditionAssetTests`, `ExpeditionGenericityTests`, `WorldSiteCatalogTests` |
| Tests: `Assets/Game/Scripts/agents/Residents/Editor/Tests/` | `DayPlannerTests` (+ away), `ResidentAssignmentTests` (+ sourcePrefab) |
| Autotest: `AutotestRunner.Settlement.cs` | New modes `expedition-host`, `expedition-client`, `expedition-persist` |
| Docs | Create `docs/AI/systems/Expeditions.md`; entry in `docs/Human/the-systems.md`; gotchas in `AgentSystem.md` and `Residents.md` |

---

# Phase 0 — Groundwork

### Task 0.1: Settlement and resident identity

**Files:** Modify `Settlement.cs`, `Resident.cs`, `ResidentAssignment.cs`. Create `ExpeditionAuthoring.cs` (first two
menus). Test: `ResidentAssignmentTests.cs`.

**Interfaces:**
- `Settlement.SettlementId : string`: its `SaveableEntity` id, or empty with a one-time warning.
- `Resident.sourcePrefab : GameObject` (serialized, appended after `settlement`, `:57`).
- Menu `Tools/SpaceGame/Expeditions/Stamp Settlement Identity`.
- Menu `Tools/SpaceGame/Expeditions/Back-fill Resident Source Prefabs`.

- [ ] **Step 1.** Add `public GameObject sourcePrefab;` to `Resident` after `settlement`, with a tooltip ("the prefab
  this resident was placed from; a stand-in on an expedition is spawned from it").
- [ ] **Step 2.** In `ResidentAssignment.Assign` (`:62–77`), set `resident.sourcePrefab = newcomer.prefab`. The
  `Newcomer` struct already carries the prefab (`Settlement.cs:541`).
- [ ] **Step 3.** Test first: extend `ResidentAssignmentTests` with
  `Assign_StampsSourcePrefab_ForEveryResident`. Run it and see it fail, then pass after Step 2.
- [ ] **Step 4.** Add `Settlement.SettlementId`: `GetComponent<SaveableEntity>()?.InstanceId`. Check the
  `SaveableEntity` accessor name in `SaveableEntity.cs:46`; the entity must be `authored`.
- [ ] **Step 5.** Menu *Stamp Settlement Identity*: for every `Settlement` in every chunk scene of every
  `WorldStreamingConfig`, plus settlement prefabs (`Prefabs/settlements/*`), make sure the GameObject has an authored
  `SaveableEntity` with a stamped id.
  - Use the open-additively loop from `WorldNavMeshBaker` (`:92–144`): skip scenes already open, and save **only**
    scenes it changed.
  - A prefab-placed settlement gets its id on the **scene instance** (instance override), never on the prefab, so two
    placements never share an id.
- [ ] **Step 6.** Menu *Back-fill Resident Source Prefabs*: for each `Resident` under a settlement in an open chunk
  scene, set `sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(resident.gameObject)` (outermost prefab
  root) and record the property modification. Report the count. Expect 70 in `Chunk_6_3`.
- [ ] **Step 7.** Run both menus. Check `Chunk_6_3.unity`: the Settlement GameObject has a `SaveableEntity`, and 70
  `sourcePrefab` overrides. Diff the scene file and confirm nothing else changed.
- [ ] **Step 8.** Type-check and run `ResidentAssignmentTests`.
- [ ] **Step 9 (commit if authorised).** `feat(expeditions): settlement id and resident source prefab`.

### Task 0.2: Group-sim hooks

**Files:** Modify `NpcGroup.cs`, `NpcGroupComposition.cs`, `NpcWorldSim.cs`, `WarPartyDirector.cs`. Tests:
`ExpeditionGroupTests.cs` (new); extend `GroupRecordTests`/`RuntimeGroupTests` where they exist.

**Interfaces** (all on `NpcGroup` unless noted):
- `string Owner` (saved). Owner constants `NpcGroup.OwnerWar = "war"` and `NpcGroup.OwnerExpedition = "expedition"`.
- `[NonSerialized] List<NpcGroupComposition.PlannedMember> PlannedOverride`
- `[NonSerialized] Action<GameObject,int> MemberStamp`
- `[NonSerialized] List<Pose> SpawnPoses`
- `[NonSerialized] Action<GameObject,int> ReadBack`
- `NpcWorldSim.ClaimRuntimeOwner(string owner)`: directors claim the owners whose runtime records must survive restore.

- [ ] **Step 1.** Tests first (`ExpeditionGroupTests`):
  - `Record_OwnerRoundTrips`;
  - `Record_OldSaveWithoutOwner_ReadsEmpty`;
  - `Restore_RuntimeRecordWithClaimedOwner_IsKept`;
  - `Restore_RuntimeRecordWithoutQuarryOrOwner_IsStillSkipped`;
  - `Composition_PlannedOverride_WinsOverTemplate`.
- [ ] **Step 2.** Append `public string owner;` to `Record` after `delivered` (`NpcGroup.cs:332`, dated comment as at
  `:320`). Write it in `ToRecord` (`:353`); read it in `ApplyRecord` (`:374`) as `record.owner ?? string.Empty`.
- [ ] **Step 3.** Set `Owner = OwnerWar` where `WarPartyDirector` raises a party (`:293–320`). In
  `AdoptRestoredParties` (`:195`), `WarPartyTemplateFor` (`NpcWorldSim.cs:314`) and `ReportSighting`'s group filter,
  require `Owner == OwnerWar || (Owner == "" && IsWarParty)`. The second clause keeps old saves working.
- [ ] **Step 4.** In `RestoreRecords` (`NpcWorldSim.cs:1176–1177`), keep a `runtimeOnly` record when
  `claimedOwners.Contains(record.owner)`. Keep the old quarry rule for everything else.
- [ ] **Step 5.** `NpcGroupComposition.Resolve` (`:30`): `if (group.PlannedOverride != null) return new
  List<PlannedMember>(group.PlannedOverride);` as the first branch.
- [ ] **Step 6.** `NpcWorldSim.Spawn` (`:526–598`):
  - when `group.SpawnPoses` holds an entry for a member index, use that pose instead of the formation slot, then
    clear `SpawnPoses`;
  - extend the `beforeSpawn` lambda (`:576`) to also invoke `group.MemberStamp?.Invoke(instance, memberIndex)` after
    `GroupMembership.Stamp`.
- [ ] **Step 7.** `NpcWorldSim.Despawn` (`:771–804`): before `DespawnMembers` (`:801`), call
  `group.ReadBack?.Invoke(member, memberIndex)` for each live member. Use `GroupMembership` for the index.
- [ ] **Step 8.** `TickVirtual` (`:391`): first branch
  `if (group.Owner == NpcGroup.OwnerExpedition) { if (group.HasGoal) group.AdvanceToward(template.travelSpeed, delta); return; }`.
  The director owns the goal; the sim only moves the group.
- [ ] **Step 9.** Run the new tests, `WarPartyDirectorTests` and `WarPartyPersistenceTests` (no war-party regression).
  Type-check.
- [ ] **Step 10 (commit).** `feat(npcworld): group owner, planned members, spawn poses and read-back hooks`.

### Task 0.3: Expedition data and roles

**Files:** Create `ExpeditionTypes.cs` and the `Data/*.cs` files. Modify `ResidentArchetype.cs` (after `:48`) and
`SettlementCulture.cs` (after `:44`). Test: `ExpeditionAssetTests.cs`.

**Interfaces:**
- `[Flags] enum ExpeditionRole { None=0, Warrior=1, Hunter=2, Scout=4, Bearer=8, Builder=16, Healer=32 }`
  (`any` is a slot rule, not a role).
- `ResidentArchetype.expeditionRoles : ExpeditionRole`.
- `SettlementCulture.expeditions : ExpeditionProfile`.
- `ExpeditionProfile` (SO):
  - `goals : ExpeditionGoal[]`
  - `warriorQuotaSmall : Vector2Int` (6–8), `warriorQuotaLarge : int` (12), `largeFromBeds : int` (40)
  - `musterUse : SpotUse`
  - `lines : TextAsset`
- `ExpeditionGoal` (SO):
  - `id`, `displayName`, `weight`
  - `slots : RoleSlot[] { role, min, max, any }`
  - `stages : StageSpec[] { kind, minutesRange, countRange }`
  - `kits : RoleKit[] { role, kit }`
- `ExpeditionKit` (SO): `weapon : InventoryItem` (required), `tool`, `beltItems[]`.
- `ExpeditionTuning` (SO in `Resources/Expeditions/`, the `ResidentTuning.Instance` pattern), with spec §12 defaults:
  - `travelDawn`, `travelDusk`, `departRadius`, `maxHandoffDistance`, `musterMinutes`, `departHour`, `restDays`
  - `minHomeShare`, `observeRadius`, `searchRing`, `handoffObserveRadius`
- `ExpeditionCatalog` (SO in `Resources/Expeditions/`): `profiles[]`. The index into it is replicated (Task 1.4).
- `ExpeditionValidation.Problems(ExpeditionProfile) : List<string>`:
  - every goal has a Warrior slot with `min ≥ 2`;
  - size bounds are 3–10;
  - every kit has a weapon;
  - every role used by a slot has a kit;
  - a muster use is set;
  - the profile is in the catalog.

  It is used by `OnValidate` (warn) and the tests (fail).

- [ ] **Step 1.** Tests first (`ExpeditionAssetTests`):
  - `ShippedProfiles_HaveNoProblems` (iterates `ExpeditionCatalog`);
  - `Validation_FlagsGoalWithOneWarrior`;
  - `Validation_FlagsKitWithoutWeapon`;
  - `Validation_FlagsProfileMissingFromCatalog`.
- [ ] **Step 2.** Write the types and assets' scripts. Use `[CreateAssetMenu(menuName = "SpaceGame/Expeditions/…")]`.
- [ ] **Step 3.** Author the **Raxy content** under `Assets/Game/ScriptableObjects/Expeditions/Nomad/`:
  - `NomadExpeditions.asset` (profile);
  - `Goal_Scout.asset`: Warrior 2–2, Scout 1–2; stages `Travel → Search(3–5) → Halt → Travel → Search(3–5) → Halt →
    ReturnHome`;
  - kits `Kit_Warrior` (spear + signal horn) and `Kit_Scout` (short spear + spyglass), using existing tool items.

  Set `NomadCulture.expeditions`. Add the profile to `ExpeditionCatalog.asset`.
- [ ] **Step 4.** Set `expeditionRoles` on the archetypes per spec §3.2: Guard/TowerGuard/Hunter → Warrior;
  Hunter/Butcher/Herder → Hunter; Scout/Lookout → Scout; Hauler/Drover/Stablehand/WoodCarrier/OreCarrier → Bearer;
  Mechanic/Tinker/Smith → Builder; Healer → Healer.
- [ ] **Step 5.** `ResidentValidator`: add `CheckExpeditions(findings, settlement)` at `:54`. If the culture has a
  profile, report every `ExpeditionValidation` problem plus "no muster spot" (Task 0.4) as **Error** findings.
- [ ] **Step 6.** Run the tests and type-check.
- [ ] **Step 7 (commit).** `feat(expeditions): profile, goal, kit and role data; Raxy scout content`.

### Task 0.4: The muster spot

**Files:** Modify `SpotUse.cs` (`SpotRole`, `:12`), `ResidentEnums.cs` (`PlaceKind`, `:34`), `SettlementSociety.cs`
(`KindOf`, `:516–522`), `HouseRoom.cs` (`:122`), `DayPlanner.cs`. Create `ScriptableObjects/Settlements/Spots/Muster.asset`.
Modify the settlement's main gate prefab.

**Interfaces:**
- `SpotRole.Assembly` (appended), `PlaceKind.Assembly` (appended).
- `SettlementSociety.AssemblyPlace(SpotUse use) : SettlementPlace` (the first usable one), and
  `SettlementSociety.MusterPose(SpotUse) : Pose`.

- [ ] **Step 1.** Test first (`DayPlannerTests`): `AssemblyPlaces_AreNeverPlannedAsStrollOrLeisure`.
- [ ] **Step 2.** Append `Assembly` to both enums. Map `SpotRole.Assembly → PlaceKind.Assembly` in `KindOf`, so it no
  longer falls through to `Stroll`. Make `DayPlanner` skip `PlaceKind.Assembly` in every candidate list.
  `HouseRoom.cs:122` treats it as not-a-room-place.
- [ ] **Step 3.** Create `Muster.asset`: role `Assembly`, `holdCue` = the existing stand or attention cue (reuse; do
  not invent an animation), `seated` false. Set `NomadExpeditions.musterUse` to it.
- [ ] **Step 4.** Find the Raxy settlement's **main gate** prefab: the one carrying the `SettlementEntrance` nearest
  the road out. Read `Chunk_6_3`'s `Generated` hierarchy and the decoration config to confirm which gate prefab is
  used and where.
  - **Prefab Mode check**, then add a child `SettlementSpot` named `MusterSpot` using `Muster.asset`, placed 4–6 m
    inside the gate with +Z pointing **out** through the gate. The departure direction is read from it.
  - Snapshot and diff the prefab.
  - Because the spot lives on the prefab, every settlement using that gate gets a muster spot. Any settlement can get
    one this way.
- [ ] **Step 5.** In the Editor, open `Chunk_6_3` and run the settlement's *Validate* (`SettlementEditor`). Expect
  no "no muster spot" finding, and the muster spot reachable from `WalkableHeart` (`SettlementPlaces.Problems`).
- [ ] **Step 6.** Run the tests and type-check.
- [ ] **Step 7 (commit).** `feat(settlements): Assembly spot role and the gate muster spot`.

### Task 0.5: Warrior quota

**Files:** Modify `ExpeditionAuthoring.cs` (menu), `ExpeditionRules.cs` (pure picker). Test: `ExpeditionRulesTests`.

**Interfaces:**
- `ExpeditionRules.WarriorQuota(ExpeditionProfile, int beds) : int`.
- `ExpeditionRules.PickWarriorRecruits(IReadOnlyList<RecruitCandidate>, int needed, int seed) : List<int>`.
  - Candidates are adults whose `SettlementCulture.SuitsOf(sourcePrefab)` includes an archetype with the Warrior role,
    and whose current archetype has no Warrior role.
  - Lowest `nerve` deficit first, then seeded.
- Menu `Tools/SpaceGame/Expeditions/Apply Warrior Quota (selected settlement)`.

- [ ] **Step 1.** Tests first:
  - `Quota_LargeSettlement_Is12`;
  - `Quota_SmallSettlement_IsWithin6To8`;
  - `Recruits_OnlyFromBodiesThatSuitAWarriorArchetype`;
  - `Recruits_StableForSeed`.
- [ ] **Step 2.** Implement both functions.
- [ ] **Step 3.** The menu reassigns `archetype` on the picked residents to the culture's Guard-like archetype (the
  first Warrior-role archetype their body suits), records the modification, and saves only the scene. Expect 3 changes
  in `Chunk_6_3` (8 Guards + 1 Hunter → 12 warriors). Bodies, names, beds and bonds stay the same.
- [ ] **Step 4.** Re-validate the settlement and check that `PatrolWanted` (`ResidentAssignment.cs:122`) still holds.
- [ ] **Step 5 (commit).** `feat(expeditions): warrior quota; Raxy settlement to 12 warriors`.

### Task 0.6: World site and settlement catalog

**Files:** Create `WorldSiteCatalog.cs` and `WorldSiteCatalogBaker.cs`. Modify `SiteKind.cs` (append `Outpost`,
`AntennaSite`; add both to `WorldSiteMarker.KindColour`, `:117–127`) and `WorldSiteRegistry.cs`. Test:
`WorldSiteCatalogTests.cs`.

**Interfaces:**
- `WorldSiteCatalog` (SO, one per `WorldStreamingConfig`, referenced from it as an appended field):
  - `sites : SiteEntry[] { id, kind, position, radius, name, airborne }`;
  - `settlements : SettlementEntry[] { settlementId, name, position, musterPosition, musterForward, beds, cultureProfileIndex, roster : RosterEntry[] }`;
  - `RosterEntry { residentKey, archetypeIndex, roles, adult, bonds:int[], sourcePrefabGuid }`.
- `WorldSiteRegistry.MergeCatalog(WorldSiteCatalog)`, called once at world start: registers sites so they are known
  before their chunk loads. The markers' own `OnEnable` registration then updates them in place.
- Menu `Tools/SpaceGame/World/Bake Site Catalog` (all worlds).

- [ ] **Step 1.** Tests first:
  - `Catalog_IncludesEveryMarkerInEveryChunkScene` — reads the baked asset against a cheap YAML scan of chunk scenes
    for the `WorldSiteMarker` script GUID `5380307fed0c24ef699d4fe52135d478`;
  - `Catalog_IncludesEverySettlementWithAProfile`;
  - `Merge_RegistersSitesBeforeChunksLoad`.
- [ ] **Step 2.** The baker uses `WorldNavMeshBaker.LoadConfig` semantics, but iterates **every** `WorldStreamingConfig`
  (there are two worlds). It opens each chunk additively, collects markers and settlements, and closes scenes it opened.
  - It writes only the catalog asset, saving that asset with `AssetDatabase.SaveAssetIfDirty(catalog)`, never
    `SaveAssets`.
  - For each settlement with `Culture?.expeditions`, it records the muster pose from `MusterSpot` and the roster from
    its residents.
- [ ] **Step 3.** Call `MergeCatalog` where the world session starts (the same point `WorldSiteRegistry` is first
  used; find it from `WorldSession`). Make `WorldSiteRegistry.Clear()` run on world unload too, because the comment at
  `WorldSiteMarker.cs:96–99` already expects it.
- [ ] **Step 4.** Bake. Inspect the asset: one settlement (`Chunk_6_3`, 70 roster rows, 12 warriors), plus the Sky
  City site.
- [ ] **Step 5 (content, may be done by the user).** Place `WorldSiteMarker`s: `Ruin`/`ScrapField` ×4 as scouting
  targets. The spec's colonies, outposts, camps and antenna sites wait for their phases. Re-bake.
- [ ] **Step 6 (commit).** `feat(world): baked site and settlement catalog`.

### Task 0.7: Spike — can a Raxy aim and fire a held weapon?

**Files:** temporary only, under `Assets/Game/Scenes/Spikes/` and `Assets/Game/Prefabs/Spikes/`. All are deleted at
the end. Output: a findings note appended to the spec's §17 risk row.

- [ ] **Step 1.** Copy `Raxy_Warrior.prefab` → `Spikes/Raxy_Gunner.prefab` (`AssetDatabase.CopyAsset`, new GUID). On
  the copy:
  - add `NpcItemUseModule` (`slotIndex 0`, `maxRange 25`, as on `Nomad_Tan.prefab:1341`);
  - set `EntityEquipmentController.aimHeldItem = true`;
  - put `basicgun` in the inventory's starting slot 0.

  Do **not** run `RaxyToolLoadouts`' *Equip Residents* menu: it forces `aimHeldItem` false (`:111`).
- [ ] **Step 2.** Register the copy (`Sync Network Prefabs`). Make a spike scene with the copy, a target (a
  `ClankerOutrider` or a Fauna dummy made hostile to Drifters for the test), and a flat NavMesh (memory: headless play
  harness, box NavMesh).
- [ ] **Step 3.** Host: does it equip, aim (arm and gun toward the target), fire (projectile, damage), and keep its
  hold pose sane? Record a capture.
- [ ] **Step 4.** Client (a second instance): does the client see the shots (`NetMsg.ItemUsed`) and the aim?
- [ ] **Step 5.** Write the finding (works / partially / fails, plus what broke) into spec §17 "Aimed guns on the
  Raxy rig", and decide whether Phase 2 builds the harpoon or ships spears only first.
- [ ] **Step 6.** Delete the spike prefab and scene, re-run `Sync Network Prefabs`, and confirm the prefab list no
  longer holds it.

---

# Phase 1 — The round trip

### Task 1.1: Records and pure rules

**Files:** Create `ExpeditionRecord.cs` and `ExpeditionRules.cs`. Tests: `ExpeditionRulesTests.cs`, `RotationTests.cs`.

**Interfaces:**
- **`SettlementState`** (saved per settlement): `settlementId`, `roster : RosterEntry[]` (latest snapshot),
  `rotation : int` (bands raised so far, used to seed), `restUntilDay : Dictionary<string,int>`, `lastGoalId`.
- **`ExpeditionRecord`** (saved; the spec §4.2 subset for Phase 1):
  - `id`, `settlementId`, `goalId`, `seed`, `phase`, `stages : StageRecord[]`, `stageIndex`, `stageMinutesLeft`
  - `members : MemberRecord[] { residentKey, archetypeIndex, kitIndex, health01, dead, isLeader }`
  - `departDay`, `groupId`, `handoffPoint : Vector3` — the road point where the band hands off and later walks back in
- **`ExpeditionRules`** (pure, static):
  - `ExpeditionGoal ChooseGoal(IReadOnlyList<ExpeditionGoal>, string lastGoalId, int seed, Func<ExpeditionGoal,bool> possible, float varietyPenalty)`
  - `bool TryPickMembers(ExpeditionGoal, IReadOnlyList<RosterEntry> roster, ISet<string> away, IReadOnlyDictionary<string,int> restUntilDay, int today, int warriorsHomeMin, float minHomeShare, int seed, out List<MemberRecord>)` — fills `min` slots, then `max`; prefers bonds of the first pick; the leader is a random Warrior
  - `StageRecord[] BuildStages(ExpeditionGoal, int seed)` — rolls counts and durations
  - `bool IsTravelHour(double gameMinutes, ExpeditionTuning)`
  - `StageStep Advance(ref ExpeditionRecord, double elapsedGameMinutes, bool arrived)` — returns what the director must do next (`SetTarget`, `Hold`, `Arrive`, `Home`); settles several stage ends in one call (clock jumps)
  - `IReadOnlyList<Vector3> SearchRing(Vector3 centre, int count, int seed, ExpeditionTuning)` — unsampled; the caller samples the NavMesh
  - `bool NextBandDue(ExpeditionPhase current, int bandsOut)` — true when nothing is out, or the only band out has entered `ReturnHome`; never when two are out
  - `bool ShouldHandOff(float distanceFromMuster, bool observed, ExpeditionTuning)` — `(d ≥ departRadius && !observed) || d ≥ maxHandoffDistance`

- [ ] **Step 1.** Tests first:
  - Draw:
    - `ChooseGoal_OnlyAmongPossible`;
    - `ChooseGoal_VarietyPenaltyLowersLastGoal` (10 000 draws);
    - `ChooseGoal_StableForSeed`.
  - Picks:
    - `PickMembers_FillsWarriorMinimum`;
    - `PickMembers_FailsWhenWarriorsHomeWouldDropBelowMinimum`;
    - `PickMembers_SkipsAwayRestingAndChildren`;
    - `PickMembers_PrefersBondsOfFirstPick`;
    - `PickMembers_LeaderIsAWarrior`;
    - `PickMembers_StableForSeed`.
  - Stages and time:
    - `BuildStages_CountsWithinRanges`;
    - `IsTravelHour_DawnToDusk`.
  - Advance:
    - `Advance_SettlesSeveralStagesAfterClockJump`;
    - `Advance_HaltHoldsUntilDawn`.
  - Hand-off:
    - `ShouldHandOff_UnobservedPastDepartRadius`;
    - `ShouldHandOff_ObservedOnlyAtMaxDistance`.
  - Rotation (`RotationTests`):
    - `NextBandDue_WhenNoneOut`;
    - `NextBandDue_WhenOnlyBandEntersReturnHome`;
    - `NextBandDue_NeverWithTwoOut`.
- [ ] **Step 2.** Implement until green. Every tunable is read from `ExpeditionTuning` or the goal asset.
- [ ] **Step 3 (commit).** `feat(expeditions): records and pure rules`.

### Task 1.2: The director and its save

**Files:** Create `ExpeditionDirector.cs` and `ExpeditionSaveable.cs`. Modify `persistentScene.unity`: add both to the
`NpcWorldSim` object, and add the `settlement-expedition` template (`runtimeOnly`, `travelSpeed` 3.5, formation
`Column`, no members — they always come from the record). Test: `ExpeditionPersistenceTests.cs`.

**Interfaces:**
- `ExpeditionDirector : MonoBehaviour`: `[DisallowMultipleComponent]`, `[RequireComponent(typeof(NpcWorldSim))]`,
  static `Instance` with a `ResetStatics` subsystem hook (`WarPartyDirector.cs:19–26` pattern).
- Settlement-facing methods:
  - `static bool IsAway(string settlementId, string residentKey)` (false when there is no instance)
  - `void ReportRoster(string settlementId, RosterEntry[] roster)`
  - `ExpeditionRecord? PendingFor(string settlementId, ExpeditionPhase phase)`
  - `bool BeginHandOff(string expeditionId, IReadOnlyList<Pose> poses)` — spawns stand-ins at the poses (or creates
    the group folded when nobody is near) and marks the members away; the caller hides the residents on the same tick
  - `void CompleteHomecoming(string expeditionId)`
- Debug methods (Task 1.6): `ForceBand`, `AdvanceStage`, `SimulateDays`.
- Save state: `ExpeditionSaveable` (key `expeditions`) → `State { SettlementState[] settlements; ExpeditionRecord[] bands; }`.

**Behaviour:**
- `Awake`: claim the owner on the sim (`ClaimRuntimeOwner(OwnerExpedition)`). Seed `SettlementState`s from the
  catalog's `settlements` for the current world.
- `Update`: `if (!Network.Decides) return;` A `decisionInterval` timer (0.5 s) runs `Step`.
- `Step` reads `DayNightCycle.Main.GameMinutesNow`, then:
  1. **Adopts restored groups** by `owner`. Missing groups are re-created for `Out` records, as war parties are.
  2. **Rotation:** for each settlement where `NextBandDue`, choose a goal and members and create a record in
     `Announced` with `departDay` = next day, then `Departing` at `departHour`.
  3. **Departure, chunk unloaded:** when the settlement is not loaded at `departHour`, perform it abstractly. Members
     are away, and the group is created **folded** at the catalog's road point `musterPosition + musterForward ×
     departRadius`. The phase becomes `Out`.
  4. **Bands out:** advance with `ExpeditionRules.Advance`.
     - Folded: set `group.GoalPosition`/`HasGoal` only during travel hours, and clear `HasGoal` to halt.
     - Spawned: `sim.SteerSpawned(group, target, r)` each time the target changes.
     - Search waypoints are sampled with `NavMesh.SamplePosition` around the ring points, falling back to the next
       point.
  5. **Arrival:** `ReturnHome` arriving at `handoffPoint` → `Returning`.
     - Settlement loaded: `SettlementExpeditions` performs the walk-in (Task 1.5).
     - Not loaded: mark `Home` immediately, release the members, and write `restUntilDay`.
- **Group creation:**
  - `sim.CreateGroup(template, record.id, start)`, then `group.Owner = OwnerExpedition`.
  - `group.PlannedOverride` = the members' source prefabs, from the roster's `sourcePrefabGuid`, resolved through a
    GUID → prefab table in `ExpeditionCatalog`, which the baker fills.
  - `group.MemberStamp` = `ExpeditionMember.Stamp(instance, record, memberIndex)`.
  - `group.ReadBack` writes health and death back into `members[i]`.
- **Saving:** `CaptureState` returns the settlements and the bands. `RestoreState` replaces both, and must not run
  rotation until `SaveManager.OnLoadApplied` has fired (subscribe; `SettlementSociety.cs:94–98` pattern).

- [ ] **Step 1.** Tests first (`ExpeditionPersistenceTests`):
  - `State_RoundTripsThroughSaveSerializer`;
  - `Restore_OutBand_RecreatesGroupWithOwner`;
  - `Restore_DoesNotDoubleRaise_AfterLoad`;
  - `IsAway_TrueOnlyForMembersOfOutOrDepartingBands`;
  - `WarDirector_DoesNotAdoptExpeditionGroups`.
- [ ] **Step 2.** Implement. Keep `Step` small: rotation, departure, advance and arrival are separate private methods.
  Everything decidable is in `ExpeditionRules`.
- [ ] **Step 3.** Wire `persistentScene`: the components and the template. Read `persistentScene.unity` around `:274`
  and `:803` first.
- [ ] **Step 4.** Run the tests and type-check.
- [ ] **Step 5 (commit).** `feat(expeditions): director, rotation and save`.

### Task 1.3: `Away` on residents

**Files:** Modify `Resident.cs`, `ResidentRoutine.cs`, `SettlementSociety.cs`, `DayPlanner.cs`. Tests:
`DayPlannerTests`, plus `ResidentAwayTests` (new, in `Residents/Editor/Tests/`).

**Interfaces:**
- `Resident.IsAway : bool`, `Resident.GoAway()`, `Resident.ComeHome(Vector3 at, Quaternion facing)` — server only.
- `PlannerResident.away` (appended after `dead`, `DayPlanner.cs:24–37`).
- `SettlementSociety.RefreshRoster()` (public; re-gathers residents, rebuilds plans).
- `SettlementSociety.ResidentKey(Resident) : string`.

- [ ] **Step 1.** Tests first:
  - `Plan_AwayResident_IsEmpty`;
  - `Plan_AwayResident_IsSkippedForNightShiftsAndPairing`;
  - `Society_AwayFromDirector_IsPassedToPlanner` (inject a stub absence query; see Step 4).
- [ ] **Step 2.** `GoAway()` reuses `GoOffstage`'s body, but sets `away`. `Resident.Update` (`:163–166`) and `Wake`
  (`:261`) must not bring an away resident onstage. `ComeHome` teleports with `NetworkedTeleport.Move`, then calls
  `ComeOnstage`. `IsAway` is server state; clients see `hidden` only.
- [ ] **Step 3.** `ResidentRoutine.Tick` (`:144`): add `|| resident.IsAway` to the early return. `Settle` (`:236`) and
  `Backstop` (`:241`) also check it, as a second guard.
- [ ] **Step 4.** `SettlementSociety.PlannerResidents` (`:327`) sets
  `away = AbsenceQuery(settlementId, ResidentKey(r))`.
  - `AbsenceQuery` is a static `Func<string,string,bool>` that the director sets in `Awake`, so Residents does not
    reference the director type and the tests can stub it.
  - Rebuild the plans when the director reports a change for this settlement: an event
    `ExpeditionDirector.AbsenceChanged(settlementId)`, which the society subscribes to in `Enable`.
- [ ] **Step 5.** Run the tests and type-check.
- [ ] **Step 6 (commit).** `feat(residents): Away state for residents on an expedition`.

### Task 1.4: Stand-ins (`ExpeditionMember`, expedition mode, outside hands)

**Files:** Create `ExpeditionMember.cs`. Modify `Resident.cs` (`DisplayName`, settlement fallback),
`ResidentHands`/`ResidentHandsRule` (append `Activity.Expedition`), and `ExpeditionAuthoring.cs` (the *Prepare
Resident Prefabs* menu). Modify every prefab listed in any `SettlementCulture.profiles`: today the 18–19
`Characters/Raxy/*` prefabs.

**Interfaces:**
- `ExpeditionMember : NetworkBehaviour`. Its NetworkVariables, all written by the server before the spawn and read
  on every machine in `OnNetworkSpawn`:
  - `FixedString64Bytes residentKey`, `FixedString64Bytes displayName`
  - `short profileIndex`, `short archetypeIndex`, `short kitIndex`
- `static void Stamp(GameObject instance, in ExpeditionRecord record, int memberIndex)` — server, `beforeSpawn`.
  - Sets the fields, the formation id (`record.id`) and `formation.enabled = true`.
  - Sets `Resident.displayName` and `Resident.archetype` (from the culture by index).
  - Puts the kit weapon in inventory slot 0.
- `bool IsStandIn` (`residentKey` not empty).
- `ExpeditionKit Kit` (from the catalog, profile, goal and role).
- **Expedition mode:** when `IsStandIn`, `Resident.Settlement` returns null without falling back to
  `GetComponentInParent`. The routine's early return (`society == null`) then holds. Presence publishes
  `Activity.Expedition`.
- **FixedString null trap** (memory): never `cond ? x : default` with a `FixedString`; assign through explicit
  constructors.

- [ ] **Step 1.** **Audit** the resident stack for null-settlement assumptions:
  - classes: `Resident`, `ResidentRoutine`, `ResidentPresence`, `ResidentHands`, `ResidentCarry`, `ResidentVoice`,
    `ResidentAwareness`, `ResidentSeating`;
  - systems that gather the roster: `HouseVisits`, `Conversations`, `Rumours`.

  List every dereference of `Settlement`/`Society` that is not null-guarded. Fix each with a guard, not a try/catch.
- [ ] **Step 2.** Append `Activity.Expedition` (a byte, at the end, `ResidentEnums.cs:9–31`). In `ResidentHandsRule`,
  `Expedition` holds the kit weapon in **Carry** (spec §6.1, the first row of the outside hand rule). The kit is read
  from `ExpeditionMember.Kit` on every machine, so clients agree without replicating the bag.
- [ ] **Step 3.** *Prepare Resident Prefabs* menu: for every prefab in every culture's `profiles`, ensure an
  `ExpeditionMember` and a **disabled** `FormationModule`.
  - Run the Prefab Mode check first, then snapshot and diff.
  - Then run `Sync Network Prefabs`, `Wire Saveable Prefabs` and `Tools/SpaceGame/Ragdoll/Wire Prefabs`
    (AgentSystem.md gotcha: skipping one leaves a prefab broken).
  - Confirm no `NetworkObject.GlobalObjectIdHash` changed.
- [ ] **Step 4.** Verify how `AgentController` gathers modules (`BehaviourModuleBase`). Enabling a disabled
  `FormationModule` must make it tick. If the controller caches enabled modules only in `Awake`, use the controller's
  own enable path instead; read the code, do not guess.
- [ ] **Step 5.** Play test (the host, offline is fine): spawn one stand-in by hand through a debug command (Task 1.6
  can come first) and check the name, archetype, spear in hand at Carry, and no errors. Then check that a resident at
  home is unchanged: its `FormationModule` is disabled, and its routine and hands are as before.
- [ ] **Step 6.** Type-check and run the resident test suites (no regressions).
- [ ] **Step 7 (commit).** `feat(expeditions): stand-in identity, expedition mode, outside hand rule`.

### Task 1.5: The settlement-local performer

**Files:** Create `SettlementExpeditions.cs`. Add it to settlements: the *Stamp Settlement Identity* menu (Task 0.1)
also adds it wherever the culture has a profile.

**Interfaces:** `SettlementExpeditions : MonoBehaviour`, server-only. It finds its `Settlement`, `Society` and the
muster place. It never saves anything.

**Behaviour:**
- **`Start`** (after the society has started):
  - `director.ReportRoster(id, snapshot)`;
  - apply `Away` to every resident `IsAway` says is out: `GoAway`, with no walk;
  - apply pending homecomings: residents of `Home` records that came back while the chunk was unloaded are simply
    home.
- **Muster**, at `departHour` while loaded:
  - each member gets `SetOverride(OverrideKind.Scripted, musterStand_i, seconds)`, where the stand points are a row in
    front of `MusterSpot` facing out, and `seconds` = `ExpeditionTuning.GameMinutesToSeconds(musterMinutes + walk)`;
  - members show the kit weapon from the muster on (Task 1.4's rule applies to residents in a `Departing` override as
    well);
  - the leader posts one line through `ResidentVoice` (the existing departure key; add it to the culture lines TSV).

  Phase 1 has no speech or crowd; that is Phase 2's ceremony.
- **Walk-out:** after the muster, an override to the road point. Each tick, `ShouldHandOff(distance,
  ObserverCheck.AnyPlayerSees(centroid, tuning.handoffObserveRadius))`. When it is true, call
  `director.BeginHandOff(id, poses)` with each resident's current pose, then `GoAway()` each resident **in the same
  method call**, so the swap is one server tick.
- **Walk-in**, when the director reports `Returning` for a loaded settlement:
  - if the band is spawned, take each stand-in's pose, `ComeHome(pose)` the resident, and despawn the stand-ins
    through `sim.DisbandGroup`, all in one call;
  - if it is folded, `ComeHome` the residents at `handoffPoint` while unobserved (`ObserverCheck`; wait if observed);
  - then a `Scripted` override walks them to the muster spot, and on arrival `director.CompleteHomecoming(id)` clears
    `Away` and sets the rest days.
- **A save mid-muster or mid-walk** (overrides are not saved): on load, the director's record phase decides. A
  `Departing` band whose hand-off did not happen is resolved as already departed (members `Away`, group folded at the
  road point). A `Returning` band is resolved as home.

- [ ] **Step 1.** Pure parts go in `ExpeditionRules` with tests: `MusterRow(Pose muster, int count, float spacing)`
  and `RoadPoint(Pose muster, float departRadius)`.
- [ ] **Step 2.** Implement the performer. Keep the choreography in small methods: `Muster`, `WalkOut`, `HandOff`,
  `WalkIn`.
- [ ] **Step 3.** Play test, host, settlement loaded: `/exp force <settlement> scout`, then advance the clock to the
  departure hour (`DayNightCycle.JumpToHour`). Watch the muster, the walk-out and the hand-off:
  - with the camera on the band: an in-place swap at 400 m — **record whether a pop is visible**;
  - camera away: a swap at 150 m.
- [ ] **Step 4 (commit).** `feat(expeditions): muster, walk-out, hand-off and walk-in`.

### Task 1.6: Debug commands

**Files:** Create `ExpeditionCommands.cs`. The registration pattern is `FaultChatBridge.cs:26–38`
(`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`, `ChatCommands.Register`).

**Interfaces** (server-run, the reply goes to the sender, so they work from a client):

| Command | Does |
|---|---|
| `/exp list` | settlements, bands, phases, stages, members |
| `/exp force <settlement#> <goalId>` | raise a band now; it departs at the next departure hour |
| `/exp depart <band#>` | skip to the muster now |
| `/exp advance <band#> [n]` | end the current stage (n times) |
| `/exp days <band#> <n>` | simulate n days folded (calls `Advance` with n×1440 minutes) |
| `/exp home <band#>` | jump to `ReturnHome` at the hand-off point |
| `/exp record <band#>` | print the record as JSON |

Gate the commands on `GameSettings.DevMode` (`GameSettings.cs:235`), the gate the item browser uses.

- [ ] **Step 1.** Implement the commands with a pure parser and tests (`ExpeditionCommandParseTests`).
- [ ] **Step 2 (commit).** `feat(expeditions): /exp debug commands`.

### Task 1.7: Multiplayer and persistence verification

**Files:** Modify `AutotestRunner.Settlement.cs`. Use `AutotestProbes.TakeSettlementCensus` (Testing.md).

- [ ] **Step 1.** Add the autotest modes:
  - `expedition-host`: force a band → depart → hand-off → `/exp days 2` → home. The census before and after shows no
    duplicates and the same instance ids.
  - `expedition-client`: the same on a client. The client sees the stand-ins with the right names and weapons, and no
    stand-in remains after the walk-in.
  - `expedition-persist`: save with the band `Out` → quit → load. The band is in the same stage, the residents are not
    shown at home, `expeditions` is in the save JSON with its members, there is no second band, and the group's owner
    is `expedition`.
- [ ] **Step 2.** Run the manual checklist from spec §14.3, Phase 1 subset, on **host, a real client and a reload**:
  1. The muster and walk-out are visible on the client. Follow the band: is there a pop at the hand-off? Record a
     capture.
  2. At 2 km out, the names match the residents and everyone carries the kit weapon.
  3. Save mid-trip, quit and load: same stage, nobody duplicated, the save JSON holds `expeditions`.
  4. Kill one stand-in, walk away, come back: the band is one short. Reload: still one short. Homecoming: the
     resident is dead at home. In Phase 1 that is the resident's own `HealthComponent` death applied at homecoming;
     remembrance is Phase 4.
  5. Unload the settlement (walk 2 km away) while the band is due home, then come back: it has returned, and **a new
     band is out**.
  6. Two clients on opposite sides of the band see the same band.
- [ ] **Step 3.** Any failure goes to `docs/AI/DEFECTS.md` if it is not fixed in this phase.

### Task 1.8: Documentation

- [ ] **Step 1.** Create `docs/AI/systems/Expeditions.md` in the standard shape (Model → Key types → Flows →
  Multiplayer → Persistence → Gotchas → Extending), with frontmatter `symptoms:` for anything that cost time (for
  example "a resident on an expedition is standing at home after a reload"). **Extending** documents §"Making another
  settlement run bands" below.
- [ ] **Step 2.** Add an entry to `docs/Human/the-systems.md`: bands leave the settlement, roam for days and come back.
- [ ] **Step 3.** Gotchas:
  - `AgentSystem.md`: `NpcGroup.owner`; the restore rule now keeps claimed runtime owners; the war director filters
    by owner.
  - `Residents.md`: `Away` vs `Offstage`; `Assembly` places are never planned; `FormationModule` is disabled on
    resident prefabs on purpose.
- [ ] **Step 4.** Update the spec where this plan corrected it (the decisions table above): §1, §10.1 and §14.2.
- [ ] **Step 5.** `python3 tools/docs_check.py --index` → `0 errors`.

### Task 1.9: The genericity guard

**Files:** Test `ExpeditionGenericityTests.cs`.
- `ExpeditionCode_NamesNoCultureOrSpecies`: scans `Assets/Game/Scripts/agents/Expeditions/**/*.cs` and the expedition
  edits in `Residents/` for `Raxy`, `Nomad`, `Drifter` and `Astronaut` (case-insensitive). Comments are not exempt.
- `EverySettlementWithAProfile_IsComplete`: from the baked catalog, each such settlement has an id, a muster pose and
  a roster with ≥ quota warriors.

- [ ] **Step 1.** Write the tests and make them pass.
- [ ] **Step 2 (commit).** `test(expeditions): genericity and completeness guards`.

---

## Making another settlement run bands

This is the checklist that goes into `Expeditions.md` → Extending. Nothing in code changes for a new settlement.

1. **It has residents:** its `SettlementConfig.culture` is set. The astronaut settlement has none today
   (`AstronautSettlement.asset:43`), so it cannot run bands until it gets a culture.
2. **The culture has an `ExpeditionProfile`**, listed in `ExpeditionCatalog`, with goals, kits and roles (on its
   archetypes) for that people.
3. **A muster spot:** a `SettlementSpot` using the profile's `musterUse` on its gate prefab, or anywhere under
   `Generated`, with +Z facing out.
4. **Run the menus:**
   - *Stamp Settlement Identity* (adds `SaveableEntity` and `SettlementExpeditions`);
   - *Prepare Resident Prefabs*;
   - *Apply Warrior Quota*;
   - then *Generate* (new settlements get `sourcePrefab` stamped) or *Back-fill Resident Source Prefabs* (existing
     ones).
5. **Bake Site Catalog.**
6. **`ResidentValidator`** reports no expedition errors, and `ExpeditionGenericityTests` is green.

## Exit criteria for Phase 1

- Every test above is green; `typecheck` is clean; `docs_check` reports 0 errors.
- The three autotest modes pass.
- The manual checklist passes on host, a real client and a reload. The hand-off pop is measured and recorded, and is
  either acceptable or handled by the unobserved-only fallback (spec §17).
- One band of the Raxy settlement is out at all times across an hour of play with the `/exp` tools.
