# SpaceGame cleanup — removal list, verified bugs, and what changed

Produced by an audit of the whole project: 602 `.cs` files (~105k LOC), 2,869 GUID-bearing assets,
273 scenes, 185 prefabs. Ten domain auditors plus a scripted reference index; every claim below
carries its evidence.

> ## STATUS: four removal groups have now been EXECUTED
>
> **1,475 files deleted (702 assets + 773 `.meta`), `Assets/` 973 MB → 781 MB (−192 MB).**
> Verified afterwards by recovering all 773 deleted `.meta` GUIDs from git and confirming **none
> appears anywhere in the current tree** — zero dangling references.
>
> | group | result |
> |---|---|
> | 192 orphan chunk scenes | deleted, and all 192 entries purged from `EditorBuildSettings` (263 → 69). 48 configured chunks intact. |
> | vendor content | 231 files, 134.6 MB. LightRays2D gone entirely. **FirstGearGames and TextMesh Pro deliberately kept** — see below. |
> | 2D map system | `MapUI`, `MapPOIController`, `MapMarker`, `MapTextures`, `MapTileBaker`, `MapHolographic.shader`, and all 240 `MapTiles` PNGs. |
> | Legacy prefabs + MasterScene + duplicates | 12 Legacy prefabs, `MasterScene.unity` + 9 terrain assets (17.7 MB), 10 reorg duplicates, 7 empty folders pruned. |
>
> **Two corrections found while executing — both changed the plan:**
> - **`FirstGearGames` was NOT deleted.** It reported 36 of 38 files unreachable, but it is
>   SmoothCameraShaker — a *code* package whose C# is referenced by **type name** from
>   `Scripts/Gameplay/Health/DamageFeedback.cs`, which GUID scanning cannot see. Deleting it would
>   have broken the build.
> - **`NpcBrain.cs` and `WanderBehaviour.cs` were NOT deleted.** `Chunk_7_5.unity` (a live,
>   configured chunk) and `FerdinandChunk_3_1.unity` carry three NPCs as **scene-native**
>   components — `m_PrefabInstance: {fileID: 0}`, not prefab instances. Removing the scripts would
>   leave Missing Script components in a shipping world chunk. `EnemyBrain.cs` and `IAgentBrain.cs`
>   are now at 0 references and are safe to remove; the other two need those three NPCs re-authored
>   onto a live agent prefab first.
>
> Everything else in the list below is still **a candidate awaiting your approval**.
> Code changes remain limited to verified bugs and dead code, at **net −17 code lines**.

## Files in this folder

| file | contents |
|---|---|
| `all_removals.tsv` | 128 removal candidates with category, confidence, evidence, risk |
| `all_bugs.md` | 39 reported bugs, each with a concrete failure scenario |
| `all_refactors.md` | 80 refactor proposals, each with a net line count |
| `all_moves.tsv` | 50 proposed file moves |
| `orphan_chunks.txt` | the 192 orphan chunk scene paths, ready to feed a delete script |
| `unreachable_assets.tsv` | every `Assets/Game` asset unreachable from any scene, with sizes |
| `cs_unreachable.tsv` | C# files unreachable from shipping roots |

---

# 1. READ THIS BEFORE DELETING ANYTHING

### `MinigameArena.unity` is binary-serialized and opaque to every text tool
`ProjectSettings/EditorSettings.asset` sets `m_SerializationMode: 2` (Force Text), but this 14.75 MB
scene predates that and was never re-saved, so it is still binary. I recovered the script types it
uses by string extraction — `MatchManager`, `SpawnPoint`, `TerrainFeatureSpawner`,
`TerrainGenManager` — but **its serialized object references cannot be read**. Any minigame-adjacent
asset must be confirmed in the Unity inspector before deletion.

Re-saving that scene in Unity is free and makes it diffable, mergeable, and visible to tooling.
Do that first; several verdicts get firmer afterwards.

### The project could not be compiled during this work
The Unity Editor holds the project lock (PID 26889), so batch mode was unavailable, and the MCP
editor bridge did not respond. Baseline was established from build artifacts instead:
`Library/ScriptAssemblies/Assembly-CSharp.dll` (10:21) is newer than every source file except your
in-flight `MountedRiderPose.cs` (10:23), and Unity only writes that DLL on a successful compile —
so **the project compiled clean at 10:21**. The 3,017 `error CS` lines in `~/Library/Logs/Unity/
Editor.log` are historical; the final block is offset one line from the current file and the fields
it complains about now exist.

Every edit I made was verified statically instead: brace balance under both `UNITY_EDITOR` and
player-build preprocessor states, case-exact asset-path resolution, serialized-property-name
matching, and a check that no removed symbol is still referenced. **Please still let Unity
recompile and run the EditMode suite before trusting the changes.**

### Two files were modified by another session while I worked
`Assets/Game/Scripts/agents/Modules/Riding/MountedRiderPose.cs` and
`Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/DirectConnectPanel.cs` (plus
`lattice_outpost.blend`, `SampleSceneProfile.asset`, and a design doc) show as modified but were
**not** touched by me. Don't attribute those to this cleanup.

---

# 2. Repo integrity: fix this first

## 2.1 Git never recorded 9 folder renames — 932 tracked paths

`core.ignorecase=true`, so renaming a directory's case on macOS left git's index holding the old
spelling. `git status` is clean, which is why this is invisible.

| git records | actually on disk | tracked files |
|---|---|---|
| `Assets/Game/Scenes/world` | `Scenes/World` | 583 |
| `Assets/Game/Scripts/agents` | `Scripts/Agents` | 192 |
| `Art/Models/Environment/nature` | `.../Nature` | 49 |
| `Art/Models/Vehicles/rover` | `.../Rover` | 44 |
| `Assets/Game/Prefabs/agents` | `Prefabs/Agents` | 20 |
| `Art/Shaders/caves` | `Shaders/Caves` | 18 |
| `Prefabs/VisualEffects/cutsceneExamples` | `.../CutsceneExamples` | 10 |
| `Prefabs/VisualEffects/lines` | `.../Lines` | 4 |
| `Prefabs/Legacy/hostiles` | `.../Hostiles` | 2 |

**Why it matters.** Clone on a case-sensitive filesystem (Linux CI, a Linux/WSL teammate) and git
checks out what the index says. `EditorBuildSettings.asset` references
`Assets/Game/Scenes/World/persistentScene.unity` and 248 `Scenes/World/Chunks/...` entries with a
capital W — none of which would exist. `Prefabs/agents` is worse: git tracks **both** spellings as
real paths (20 files lowercase, 47 uppercase), so a Linux checkout produces two sibling folders and
splits the prefabs. And `Assets/Game/Prefabs/Agents.meta` is on disk but **untracked** while
`Prefabs/agents.meta` is tracked, so a clean clone gets a different folder GUID.

**Fix** — one rename-only commit, two-step per directory because the filesystem is case-insensitive:
```
git mv Assets/Game/Scenes/world Assets/Game/Scenes/world-tmp
git mv Assets/Game/Scenes/world-tmp Assets/Game/Scenes/World
```
…repeated for the other eight. Then `git config core.ignorecase false` so it stops recurring.
I did not run these — 932 path changes is your call, and it should land as its own commit.

## 2.2 `.gitattributes` terrain exceptions point at pre-restructure paths

Lines 20–23 carve binary exceptions for `Assets/Terrain/ChunkData/*.asset`,
`Assets/TerrainData_*.asset` and `Assets/*Terrain.asset`. **All three match nothing** — the real
location is `Assets/Game/Terrain/ChunkData/`. So the generic `*.asset text eol=lf` rule (line 12)
wins on 24 binary terrain heightmaps, and git may rewrite line endings inside them.
`git check-attr text eol binary -- Assets/Game/Terrain/ChunkData/TerrainData_4_3.asset` reports
`text: set`, `eol: lf` on a file whose first bytes are raw binary.

**Fix**: retarget to `Assets/Game/Terrain/ChunkData/*.asset binary`, then
`git add --renormalize .` and check whether history already corrupted any heightmap.

---

# 3. The removal list

Sizes are on-disk bytes. "Refs" is GUID references across every scene, prefab, `.asset`,
`.mat`, `.controller`, `.anim` and `ProjectSettings` file.

## 3.1 Biggest wins

### A. 192 orphan chunk scenes — 42.8 MB — highest confidence in the whole audit

`Assets/Game/Scenes/World/Chunks/` holds 240 chunk scenes. `WorldStreamingConfig.asset` declares
`gridDimensions 8x6` and lists exactly 48. The other **192 are outside the configured world**:

- all 192 are in `EditorBuildSettings`, so **they ship in every build**
- each contains 1 GameObject, 1 Terrain, 0 MeshRenderers, ~6.3 KB
- each references a `TerrainData` GUID that **does not exist in the project** — 192 distinct
  dangling references. They render nothing and cannot.
- by contrast all 36 surviving `TerrainData` assets are referenced by configured chunks, with
  zero dangling refs

They are leftovers from shrinking the world from 20×12 to 8×6. Paths in `orphan_chunks.txt`.

Keep the 12 configured-but-empty chunks (`Chunk_0_0`–`Chunk_1_5`, byte-identical 3,539-byte empty
scenes) — they are inside the live grid and are content still to be authored.

### B. Vendor packages — ~128 MB unreachable

Transitive reachability from all of `Assets/Game/`:

| package | total | used | **unreachable** |
|---|---|---|---|
| **Kevin Iglesias** | 106.3 MB / 186 files | 8 files, 5.5 MB | **178 files, 100.8 MB** |
| **Bruhassets** | 61.3 MB / 22 files | 11 files, 36.9 MB | **11 files, 24.4 MB** |
| **Sci-Fi RTS pack** | 4.1 MB / 25 | 18 files | 7 files, 3.3 MB |
| **LightRays2D** | 7 files | **0** | **all 7 — package entirely unused** |
| Same Gev Dudios | 9.3 MB / 30 | 21 files | 9 files, 0.1 MB (URP variants + demo) |
| Same Gev Dudios 1 | 0.2 MB / 4 | 4 | 0 |
| TextMesh Pro | 4.0 MB / 37 | 4 | 33 — **do not trim**, TMP loads its own Resources by name |

Kevin Iglesias is the single biggest item in the repo. Only these 8 files are used, all through
`Art/Animations/Player/AstronautArmature.controller`: `AssultRifleIdle.fbx`,
`assultRifleShooting.fbx`, `Damage.fbx`, `Death.fbx`, `HumanM@Gun_Aim01.fbx`,
`HumanM@Gun_Aim02.fbx`, `HumanM@ThrowBoomerang01_R.fbx`, `HumanM@ThrowSpear01_R.fbx`.
Dead: two ~10 MB `.blend` sources, two ~3.5 MB `.zip` archives, the demo scene, 10 soldier
prefabs, the entire Female animation set, and both character `Models/*.fbx`.

### C. The 2D map system + its 240 tiles

Two map implementations exist; the 3D one is live and the 2D one is dead.

| file | loc | code refs | guid refs | verdict |
|---|---|---|---|---|
| `MapHologramTerrain.cs` | 1346 | 1 | 1 | **LIVE** — the only map component in a scene |
| `MapService.cs` | 174 | 7 | 2 | **LIVE** |
| `MapMarkerType.cs` | 26 | 7 | 0 | **LIVE** |
| `MapPOI.cs` | 81 | 0 | 1 | **LIVE** |
| `MapUI.cs` | 602 | 0 | 0 | dead |
| `MapPOIController.cs` | 271 | 0 | 0 | dead |
| `MapTextures.cs` | 173 | 1 | 0 | dead — only referrer is `MapUI.cs` |
| `MapMarker.cs` | 58 | 0 | 0 | dead |

`Resources/MapTiles/` — 240 PNGs, 1.9 MB:
- loaded **only** by `MapUI.cs:560`, `Resources.Load<Sprite>($"MapTiles/Tile_{x}_{y}")`
- extent `Tile_0_0`..`Tile_19_11` — the same abandoned 20×12 grid as (A)
- **all 240 are byte-identical** (531 bytes) — placeholder output carrying no information
- under `Resources/`, so all 240 ship in every build regardless of references

`Resources/MapMeshes/` — 36 assets, 23 MB — **KEEP.** Loaded by string at
`MapHologramTerrain.cs:789`; refcount 0 is a scanner blind spot, not death. In fact 12 more are
*missing*: the config needs 48 (x=0–7) and only x=2–7 were baked, so the western quarter of the
map hologram is a permanent hole that logs 12 warnings every startup. Re-run
**Tools ▸ World Streaming ▸ Bake Map Meshes**.

### D. `Prefabs/Legacy/` — all 12 prefabs unreachable

Transitive closure from every live scene, all test scenes and all 240 chunk scenes reaches none of:
`Legacy/Hostiles/enemy.prefab`, `Legacy/NPC/{NPC, NPC_Mechanic, NPC_Prospector, NPC_Settler,
NPC_SkittishScavenger}.prefab`, `Legacy/Robots/{Cath, Ernst, Phil, Roberto}.prefab`,
`Legacy/Templates/Template.prefab`, `Legacy/Vehicles/Ship.prefab`. ~582 KB.

**Sequencing matters.** These prefabs are the only thing keeping the legacy brain layer alive
(`NpcBrain.cs`, `EnemyBrain.cs`, `IAgentBrain.cs`, `WanderBehaviour.cs` — ~695 LOC, and both brain
files open with `// OBSOLETE: Replaced by the modular behaviour system`). Delete the scripts first
and you leave Missing Script components in live streaming chunks — `Chunk_7_5`, `Chunk_18_10` and
`Tests/FerdinandWorld/FerdinandChunk_3_1` reference them. Remove prefabs and scripts together,
then drop `AgentController`'s `legacyBrain` field, its `IAgentBrain` resolve loop and a second
`GetComponentsInChildren` pass (~13 LOC), and the `TryGetComponent(out NpcBrain)` branch in
`DialogInteraction.cs:557`.

### E. `MasterScene.unity` + its terrain — superseded by the chunk streaming world

`Assets/Game/Scenes/World/MasterScene.unity` plus
`Assets/Game/Terrain/TerrainData/MasterScene Terrain/` (10 binary assets, ~19 MB) plus the 14
prefabs MasterScene is the sole referencer of. This is the pre-streaming monolithic world.

## 3.2 Duplicates left behind by folder reorganisations

Each row is a live copy and a 0-reference twin.

| dead (0 refs) | live copy |
|---|---|
| `Prefabs/VisualEffects/Lighting/Sun.prefab` | `Prefabs/Environment/Sky/Sun.prefab` (4 refs) |
| `Prefabs/VisualEffects/Lighting/Global Volume.prefab` | `Prefabs/Environment/Sky/Global Volume.prefab` (5) |
| `Prefabs/Environment/Wind.prefab` | `Prefabs/Environment/Sky/Wind.prefab` (3) |
| `Prefabs/VisualEffects/Debug/Cube.prefab` | `Prefabs/Items/Debug/Cube.prefab` (6) |
| `Assets/DefaultNetworkPrefabs.asset` *(at the `Assets/` root)* | `ScriptableObjects/Networking/DefaultNetworkPrefabs.asset` (1) |
| `ScriptableObjects/Weapons/BallLightningWeapon.asset` | `Resources/Items/Artifacts/BallLightningWeapon.asset` (2) |
| `Art/Materials/Untitled/New Material.mat` | `Art/Materials/Settlement/New Material.mat` (1) |
| `Prefabs/Agents/Vehicles/Spacecraft/Ship.prefab` **and** `Prefabs/Legacy/Vehicles/Ship.prefab` | `Prefabs/Vehicles/Ship.prefab` (7) |
| `Art/Models/Vehicles/Legacy/walker_station.blend` | `.../Outpost/robot_rig/walker_station.blend` (1) |
| `Prefabs/Environment/Structures/MarsSettlement/Drone_truck.prefab` | `Prefabs/Agents/Vehicles/Robotic/Drone_truck.prefab` (2) |

Both copies dead: `MountableAnt.prefab` (in `Agents/Creatures/` **and** `Agents/Vehicles/Mounts/`);
`ship_model.blend` (in `Vehicles/Legacy/` **and** `Vehicles/RV/`).

Also `ScriptableObjects/Networking/DefaultNetworkPrefabs Unused.asset` — named "Unused", and is.

### Duplicate *item definitions* — a correctness risk, not just clutter

`Lasso.asset`, `Leash.asset`, `GrapplingHook.asset`, `RuinScanner.asset`, `RocketArtifact.asset`
and `AntiGravityFlask.asset` each exist in **both** `Resources/Items/Artifacts/` and
`ScriptableObjects/Items/`. `RegistryLoader.cs:19` does `Resources.LoadAll<InventoryItem>("Items")`,
so only the `Resources/` copies enter the registry — but `ExpeditionBackpack.prefab`'s 10 starting
items point at the `ScriptableObjects/` copies, which are therefore unresolvable. See bug 4.3.

## 3.3 Dead C# (22 files unreachable from shipping roots)

`cs_unreachable.tsv` has the machine-readable list. Highlights:

- the Map cluster above (1,104 LOC)
- `Agents/Modules/Cover/` — `CoverModule.cs` (114) + `CoverPoint.cs` (86); the whole cover feature,
  and CoverPoint's only referrer is CoverModule
- `Cave/Decoration/PrebuiltDecorationRules.cs` (180)
- `Items/Artifacts/RuinScanner/RuinSecret.cs` (171)
- `World/Caves/AlgaePulse.cs` (136)
- `Weapons/Firearms/EnergyRifle.cs` (118)
- `Agents/Profiles/EntitySystemSetup.cs` (114)
- `Agents/Entity/EntityEquipmentController.cs` (102)
- `Gameplay/Interaction/Interactions/LeverInteraction.cs` (69)
- `Characters/Player/Combat/LightningSpawner.cs` (66)
- `Agents/Modules/Facing/FacePlayerModule.cs` (46)
- `Presentation/UI/LobbyMenu/Flow/StartingGameManager.cs` (44)
- `Agents/Modules/Combat/WeaponSelector.cs` (31)
- `Presentation/UI/LobbyMenu/Core/Entity.cs` (28) — a class named `Entity` in a UI folder
- `Editor/AssetPipeline/MeshReadablePostprocessor.cs` (21)
- **`Items/Equipped/weapon.cs` — 5 lines, declares no type, lowercase filename.** Pure stub.
- `Presentation/Audio/AudioTestThingy.cs` (21) — reachable only from `Markus Music Test Scene`

### Accretion in `Scripts/Agents/Modules/` — four generations of the same ideas

The agents auditor's headline: this domain is the best-written code in the project (dense accurate
comments, real hysteresis on every range check, one stray `Debug.Log` in 6,900 lines, zero
commented-out blocks) and its only problem is that nothing was ever removed. Targeting went
registry-per-module → `AgentTargeting`; group movement `FlockingModule` → `HerdModule`; patrol
`WanderBehaviour` → `BasePatrolModule` → `PatrolModule`; weapon-holding has **three** unfinished
answers (`WeaponSelector`, `WeaponMount`, `EntityEquipmentController`) and none is on a prefab;
facing went `MoveIntent.FacingDirection` → `MoveIntent.FacePosition` → `IFacingModule` and all
three survive.

The single best refactor there: delete the `FlockingModule` cascade — the module (108 LOC), plus
`AgentController`'s per-frame 30 m `OverlapSphere` neighbour scan and its three buffers, plus
`AgentContext`'s three `NearbyAgent*` fields. **~141 LOC gone**, and three live `PatrolRobot`
prefabs stop paying a broadphase query and 32 interface `GetComponent`s every frame for data
nothing reads.

## 3.4 False positives — do NOT delete these despite 0 references

This is the most important safety section.

| asset | why it looks dead | why it is alive |
|---|---|---|
| `Settings/Input/InputControls.cs` (2954 loc) | 0 refs | generated Input System class declared `class @InputControls`; the `@` defeats name matching. Used by 6 files. |
| `Prefabs/Agents/Creatures/{HorseRobot, HumanoidRobot, CrabWalker4/6/8}.prefab` | 0 GUID refs | regenerated by `Editor/Creatures/{HorseBuilder, CrabWalkerBuilder}.cs` and loaded by literal path in 4 EditMode/Editor tests |
| `Prefabs/Agents/Robots/DeathmatchBot.prefab` | 0 refs | the only asset that can fill `MatchManager.deathmatchBotPrefab`. Content awaiting wiring — the orphan status is the *symptom* of bug 4.1, not the cause. |
| `Resources/MapMeshes/*` (36, 23 MB) | 0 refs, in `orphans.tsv` | loaded by string at `MapHologramTerrain.cs:789` |
| `HoverGroundSensor.cs` | 0 GUID refs | a `[Serializable]` field inside `HoverRigidbodyMotor`, not a component |
| `MountModule.Camera.cs`, `MountModule.Mounting.cs`, `SteerModule.Camera.cs`, `SteerModule.Input.cs` | 0 GUID refs | `partial` halves; the GUID lives on `MountModule.cs` / `SteerModule.cs` |
| `MountLookMath.cs`, `RiderPoseMath.cs` | 0 GUID refs | covered by `Editor/Tests/MountLookMathTests.cs` and `RiderPoseMathTests.cs` |
| the 7 `Shader.Find` shaders | 0 refs | looked up by name at runtime — see bug 4.2, they need to be *added* to Always Included Shaders, not deleted |
| `ThirdParty/TextMesh Pro/**` | 33 files unreachable | TMP loads its own `Resources/` by name |

## 3.5 Repo-level and small items

- `Archive/` — 16 MB, 13 tracked files, not gitignored. `BlenderSnapshots/` is 12 manual `.blend`
  backups named `PRENECK`/`PRETAIL`/`PRECOLLECTOR`/`PREDIGGER`/`PREFOOTFIX`. Superseded by git.
- 11 `.blend1` files across `Art/Models/` — Blender's own auto-backups, should be gitignored.
- `fmod_editor.log` — a stray log tracked at the repo root.
- `docs/architecture/CutsceneExamples.md.meta` — a Unity `.meta` for a file outside `Assets/`.
  Unity never made it and never reads it.
- `SpaceGame.sln` **and** `SpaceGame.slnx` — two solution formats for one project.
- `.claude/skills/blender-model/scripts/__pycache__/` — committed Python bytecode.
- `.gitignore` has a rule for `Assets/Scenes/Chunks/`, a path that no longer exists.
- `Settings/Mobile_RPAsset.asset` + `Settings/Mobile_Renderer.asset` — a mobile render pipeline
  in a desktop game, 0 refs.
- `Art/Materials/Untitled/` — a folder literally named Untitled holding 4 `New Material*.mat`.
  Three are referenced, but only by the dead/test Rover cluster; the fourth has 0 refs.
- `Packages/manifest.json` — `com.unity.modules.{tilemap, vr, xr, video, vectorgraphics}`,
  `com.unity.timeline`, `com.unity.ai.assistant` (a `-pre` build) and `com.unity.collab-proxy`
  all appear unused. Package removal is cheap to try and easy to revert.
- 6 `Prefabs/Cutscenes/Example_*.prefab` + 5 `Prefabs/VisualEffects/CutsceneExamples/*` —
  documentation examples matching `docs/architecture/CutsceneExamples.md`. Keep or delete as a set
  with that doc.
- 11 `Prefabs/Environment/Structures/MarsSettlement/*` drone/prop prefabs, 0 refs.
- `Prefabs/Characters/Player/Player.prefab` — the known dead stub (real one is
  `PlayerCharacter.prefab`, 7 refs).
- `Prefabs/Vehicles/Rover.prefab` (155 KB) + `RoverNoHierarchy.prefab` (179 KB, test-scene-only)
  + `Scripts/Vehicles/Rover/` + `Art/Models/Vehicles/Rover/`. Two of the reported bugs (4.6, 4.7)
  live in Rover code; if the subsystem goes, both are moot.

## 3.6 Test scenes ship in every build

`EditorBuildSettings` enables five personal scratch scenes **ahead of** `persistentScene`:
`Blocking test`, `Aleksander test scene`, `Tommy test scene`, `Emil test scene`,
`Marius test scene`. They are in every player build. Setting `enabled: 0` keeps them openable in
the editor while dropping them from builds — that alone is worth doing regardless of deletion.

Also `Scenes/Tests/CaveTest.unity` is byte-identical to an empty default scene, and
`Scenes/Utility/SpriteRenderScene.unity` has 0 refs.

---

# 4. Verified bugs

39 are documented in `all_bugs.md` with full failure scenarios. The ones I fixed are in §5.
The most serious ones I did **not** fix, because they need Unity or a design decision:

## 4.1 The minigame is entirely unwired
`MatchManager`'s script GUID `4e26c1883c39a42b09ab1d3accec3e16` appears in **no scene and no
prefab** — the only hit in the whole repo is its own `.cs.meta`. Yet `SpawnManager.cs:203` and
`MatchResultUI.cs:63` both call `FindFirstObjectByType<MatchManager>()`, and the class doc says
"in MinigameArena.unity". So: pick the minigame from the main menu and the arena loads with no
match logic, no teams, no bots and no win condition. `MatchManager.cs:226`'s
"No DeathmatchBot prefab assigned" error cannot even print, because the component does not exist.

This explains the 0-reference status of the whole `Gameplay/Minigame/` subtree, `DeathmatchBot.prefab`,
and the 16 Solo + 4 Team faction assets. **None of those should be deleted** — they are content
waiting on one scene edit: add a `MatchManager` to `MinigameArena.unity` and assign
`deathmatchBotPrefab`, `teamFactions`, `soloFactions`, `relationshipTable` and
`arenaTargetingProfile`.

*Caveat*: that scene is binary, so I could not read its object list directly; the GUID scan is the
evidence. Confirm in the inspector.

## 4.2 Seven runtime shaders are stripped from player builds
Every shader obtained via `Shader.Find` is referenced by no material, scene or prefab, and none is
in `m_AlwaysIncludedShaders` — so Unity's stripper drops them and every `Shader.Find` returns null
in a build. `MapHologramTerrain` logs "Shader 'Hologram/Terrain' not found." and the map renders
nothing; the helmet HUD falls back to `UI/Default` and loses its hologram look. Works perfectly in
the editor, which is why it went unnoticed.

Fix is zero code: add these to **Project Settings ▸ Graphics ▸ Always Included Shaders** —
`Art/Shaders/UI/Map/{HologramTerrain, HologramBeam, HologramSolid}.shader` and
`Art/Shaders/UI/HelmetHUD/{HelmetHUDHolographicUI, HelmetHUDHologramSolid,
HelmetHUDDangerVignette}.shader`. (Not `MapHolographic.shader` — that one is on the removal list.)

## 4.3 `InventoryItem.ID` is never serialized → item registry throws in player builds
`InventoryItem.cs:21` declares `public string ID { get; set; }` — an auto-property, which Unity
never serializes — and its only writer is an `#if UNITY_EDITOR` `OnValidate`. So in a player build
every item's `ID` is null. `RegistryLoader.LoadItems` then calls
`Registry<InventoryItem>.Register(item)` → `entries[value.ID] = value`, and
`Dictionary<string,T>` throws `ArgumentNullException` on a null key. `RegistryLoader.Awake` dies on
the first item, so `SaveablePrefabRegistry.LoadAll()` never runs either — the whole item and save
system is unregistered. Editor play mode hides it because `OnValidate` already ran.

Fix: back it with a field — `[SerializeField] private string id; public string ID { get => id;
set => id = value; }` — then re-save the 20 item assets so the value is written. I left this to you
because re-saving those assets needs Unity.

## 4.4 The player's whole starting kit vanishes on load
`ExpeditionBackpack.prefab`'s 10 starting items reference `ScriptableObjects/Items/` assets, which
are not under `Resources/`, so `RegistryLoader` never registers them. Save, reload, and
`BackpackSaveCodec.RestoreCompartment` logs "item '<guid>' is not in the registry" ten times and
leaves every slot empty. Fix: repoint the prefab at the identical `Resources/Items/Artifacts/`
assets and drop the duplicates (§3.2).

Related: `Resources/Items/Artifacts/Leash.asset` and `RocketTurret.asset` still carry the default
`itemName = "NewItem"`, so both display as "NewItem" in the inventory — and `Leash.asset`'s icon
GUID is the *Lasso's* sprite.

## 4.5 `Resources/Saveable/` did not exist — save system silently lost every spawned object
`SaveablePrefabRegistry.ResourcesFolder` is `"Saveable"` and the registry fills itself from
`Resources.LoadAll<GameObject>("Saveable")`. No such folder existed anywhere, so the registry was
permanently empty and every dropped item or spawned vehicle in a save was silently dropped on load.
**I created the folder** with a README; you still need to put the `Saveable`-carrying prefabs in it.
`Scripts/Core/Persistence/Editor/SaveWiringValidator.cs` already scans that path and will list them.

## 4.6 `PlayerDropService` RPC is inert — clients spawn ghost items
`[Rpc(SendTo.Server)]` is declared on a plain C# class, not a `NetworkBehaviour`. Netcode's IL
post-processor only rewrites `NetworkBehaviour` subclasses (confirmed against this project's own
`NetworkBehaviourILPP.cs`), so the attribute does nothing and the method runs locally on the client.
It then calls `NetworkObject.Spawn()`, which throws on a client. Net effect on a non-host client:
the item leaves your inventory, a local-only ghost prefab appears, and no peer sees it. Invisible
in singleplayer because that runs as host.

## 4.7 Rover: `moveSpeed` is a no-op and "backup" drives forward
Two independent `moveSpeed` fields; `RoverMovementController.SetTargetDirection` normalises away
the magnitude, so the value designers set on `RoverController` governs only the wheel animation —
wheels under-rotate ~40% against actual motion. And the obstacle-avoidance backup state commands a
reversed direction, but the movement controller always translates along `transform.forward` with a
non-negative speed, so the rover grinds into the obstacle for `backupTime` instead of reversing.
Both are moot if the Rover subsystem is removed.

## 4.8 `WorldStreamer.PreloadChunksAroundPosition` clamps off-world positions
The singular overload uses `config.WorldToChunkCoord`, which **clamps** out-of-grid positions to the
nearest edge chunk. Its plural sibling deliberately uses `TryGetStreamingCoord` and skips off-world
positions, with a comment explaining that the minigame arena must not be clamped into a real edge
chunk. The singular overload never got the guard, and `PlayerController.cs:58` and
`InteriorManager.cs:263` both use it. Fix is the refactor that deletes the duplication:
`PreloadChunksAroundPosition(p, cb) => PreloadChunksAroundPositions(new[]{p}, cb)` — **−34/+2 lines**.

## 4.9 `IconGenerator` leaves your scene camera rendering to a destroyed texture
Assign the scene's Main Camera, preview a prefab, close the window without clicking Generate:
`OnDisable` destroys the RenderTexture but never clears `camera.targetTexture`, so the Game view
goes black with nothing logged. The same code also silently overwrote that camera's `clearFlags`,
`backgroundColor`, `allowHDR`, `renderPostProcessing`, position and rotation, and restores none of
it. Minimum fix is 2 lines in `OnDisable`.

## 4.10 Ground-probe fix applied to only 2 of 5 downward probes
`WalkerGround.cs:102-106` documents the failure exactly ("the machine climbed into the sky at a
steady rate for as long as somebody stood under the probe") and fixes it by rejecting hits whose
`attachedRigidbody` is non-kinematic. `FoilLift.cs:361` has the same guard. These three do not:
`RoverBogieIK.cs:349`, `RoverController.cs:189,193`, `DuneRiderController.cs:273`.
`RoverBogieIK` is the strongest case — the same file sets its own wheel bodies non-kinematic
(lines 152, 161), so a wheel inside `groundMask` can be selected as ground. Needs per-site layer
confirmation before each is called a live bug; I did not change them.

---

# 5. What I actually changed

**Net −17 code lines** (48 code lines added, 65 removed) across 26 files, plus 34 comment lines
recording why each bug existed — matching the dense "why" comment style already in this codebase.

### Three player-build-breaking `CS1022` errors
`namespace X {` sat inside `#if UNITY_EDITOR` while the closing `}` was outside. With
`UNITY_EDITOR` undefined the opening brace and the `namespace` line vanish and the closing brace
remains, so **the player build could not compile at all**:
- `Scripts/Core/SceneManagement/Core/SceneReference.cs`
- `Scripts/Core/SceneManagement/Interiors/InteriorScene.cs` (also dropped a verified-unused `using SpaceGame.World;`)
- `Scripts/World/ProceduralGeneration/Settlement/Core/SettlementBuilder.cs` — **found by my own
  preprocessor-aware brace checker, not by any agent**

I wrote `braces.py` to evaluate every `.cs` file's brace balance under both preprocessor states; it
now reports zero real imbalances. (`Tests/Editor/SavePayloadCompatibilityTests.cs` still shows −4 in
*both* states — that is my checker failing to span multi-line verbatim JSON strings, not a defect.)

Also removed the same redundant guard from `Editor/Agents/BehaviourModuleEditor.cs` and
`EntityProfileEditors.cs` — harmless today because `Assets/Game/Editor` has no asmdef and so always
compiles with `UNITY_EDITOR`, but it made both files unsafe to relocate.

### 15 broken hardcoded asset paths across 10 files
9 were miscased (resolve on macOS, fail on Linux/CI); 6 pointed at directories that **do not exist
in any casing** — `Prefabs/agents/vehicle/` and `Prefabs/items/`, which meant 12 ornithopter tests
failed unconditionally and `OrnithopterBuilder`/`WingPackBuilder` wrote duplicate orphan prefabs
next to the real ones while logging success. Corrected to the on-disk spelling in
`CrabWalkerBuilder`, `HorseBuilder`, `HorseRigWiringTests`, `OrnithopterRigWiringTests`,
`OrnithopterWingAnimatorTests`, `OrnithopterBuilder`, `WingPackBuilder`, `DuneFoilBuilder`,
`HorseLocomotionTests`, `HumanoidArmSwingTests`, `HumanoidLocomotionTests`, `OstrichLocomotionTests`.
`paths.py` verifies all remaining literals resolve case-exactly.

### `EditorBuildSettings`: two entries pointing at deleted scenes
`Interiors/TestInterior.unity` and `Interiors/InsideRuin.unity` were both `enabled: 1` with no file
on disk. Removing them also fixed `Bootstrapper.cs:29`, whose
`targetScene = 1; // Default to main menu` pointed at `TestInterior` (index 1) while `MainMenu` sat
at index 2 — the "default to main menu" fallback was loading a missing scene. `MainMenu` is now
index 1 and the existing code is correct as written. −6 lines.

### Agents shooting through walls
`PerceptionModule.HasLineOfSightFrom` decided line-of-sight from an **unsorted**
`Physics.RaycastAll`, returning a verdict on the first non-self element. With the player on layer 0
— inside every agent's occlusion mask — a wall and the player both hit, and whenever the player came
back first the agent acquired and fired straight through the wall. Intermittent because the order
is not stable, which is why it read as "the robots sometimes shoot through walls". Now finds the
nearest blocker and asks whether *that* is the target.

### DuneFoil climb braking silently disabled
`FoilLift.cs` passed `float.NegativeInfinity` as the lookahead's ceiling meaning "accept anything".
But `ceiling` is an **upper** bound, so every finite hit cleared it and was diverted into the
lowest-above-ceiling bucket — the lookahead returned the **lowest** surface ahead. A rock or floor
slab under a dune face read as a downhill grade and the climb braking switched itself off. The
sibling call uses `+Infinity` for the same meaning. One-token fix.

### Dead code and dead guards
- `Weapon.cs:120` was `if (Time.time >= nextFireTime && !IsReadyToFire)` where
  `IsReadyToFire => Time.time >= nextFireTime` — literally `X && !X`, always false, running in
  `Update` on every weapon. Deleted it and the `OnFireRateReady` event it guarded (0 subscribers,
  verified).
- `AntiGravityPotion.cs:29` compared `useSound.Guid == null` on `FMOD.GUID`, a **struct** — lifted
  to `GUID?` and always false. The guard never fired and its `return` was a no-op. Deleted.
- `LightningSpell.cs:18` collapsed a missed aim raycast to `Vector3.zero` via `?? Vector3.zero`, so
  casting at open sky struck the world origin. Now returns early on a miss.
- `EntityProfileEditors.cs` looked up `"motorComponent"` while `AgentController` declares
  `MotorComponent`. `FindProperty` is case-sensitive and the helper swallows a null property, so the
  Generate button silently never wired the motor. I wrote `props.py` to check every
  serialized-name string against every declared field: this was the only case-only mismatch, and
  there are now zero.

### Two NGO teardown leaks
`NetworkedHealthComponent.OnDestroy` returned early before `base.OnDestroy()`, and
`PlayerInventoryNetwork.OnDestroy` never called it. NGO's `NetworkBehaviour.OnDestroy` is what
disposes `NetworkVariable` fields (including `PlayerInventoryNetwork`'s `NetworkList`, backed by a
native container) and deregisters the behaviour from its `NetworkObject`. Verified against this
project's own `NetworkBehaviour.cs:1679-1713` in `Library/PackageCache`.

I checked all 16 such overrides and deliberately left 12 alone: NGO's `OnNetworkSpawn` and
`OnNetworkDespawn` are empty virtuals `{ }`, so adding base calls there would be noise, not a fix.

### Menu buttons dead when you skip Bootstrap
`UIButton` dereferenced `AudioManager.Instance` unguarded. `AudioManager` exists only on
`Bootstrap.unity` and survives by `DontDestroyOnLoad`, so pressing Play directly in `MainMenu.unity`
threw an NRE that aborted the handler *before* `SetState(Highlighted)` — the visible symptom was
"buttons don't highlight", which points nowhere near audio. Two `?.` — net 0 lines.

---

# 6. What I did not do, and why

- **No deletions.** As asked. `all_removals.tsv` is the list.
- **No bulk file moves.** 50 are proposed in `all_moves.tsv`. Moving an asset with its `.meta`
  preserves the GUID and is safe in principle, but with the Editor mid-import and no way to
  recompile I was not willing to move ~900 files unverified. The casing normalisation (§2.1) should
  land first anyway, since it decides which spelling is authoritative.
- **Only one new folder.** `Resources/Saveable/`, because code requires it (4.5). I did not create
  speculative art category folders — empty directories are not tracked by git and Unity would just
  generate `.meta` clutter. The concrete hierarchy proposal is in `all_moves.tsv`; it is worth doing
  as a deliberate pass once casing is fixed.
- **The crosscut duplication audit did not complete.** It hit the subagent session limit, as did 86
  of 97 adversarial verifiers. So most `all_removals.tsv` rows carry the auditor's own evidence but
  **not** an independent second opinion. The items in §3.1–§3.4 are the ones I re-verified myself
  with scripted checks; treat the rest as strong leads rather than settled.

## What to do next (updated after the deletions)

Steps 4, 5 (partly) and 6 of the original plan are **done**. Remaining, in priority order:

### Blocking — do first
1. **Let Unity recompile and run the EditMode suite.** 1,475 files were deleted and 26 edited
   without a compile. Everything was checked statically, but that is not a substitute.
   Watch specifically for Missing Script / missing-reference warnings on `Chunk_7_5.unity`.

### Player-build correctness — the game is currently broken in a build, not in the editor
2. **Six shaders are stripped from every player build.** All six are obtained only through
   `Shader.Find`, are referenced by no material/scene/prefab, and none is in
   `m_AlwaysIncludedShaders` (verified: the 7 existing entries are all Unity built-ins). Add via
   Project Settings ▸ Graphics ▸ Always Included Shaders — **zero code**:
   `Art/Shaders/UI/Map/{HologramTerrain, HologramBeam, HologramSolid}.shader`,
   `Art/Shaders/UI/HelmetHUD/{HelmetHUDHolographicUI, HelmetHUDHologramSolid,
   HelmetHUDDangerVignette}.shader`.
   (`Standard`, `UI/Default`, `Unlit/Color` and the two URP names are Unity built-ins — fine.)
3. **`InventoryItem.ID` is an auto-property with no serialized backing** (confirmed: no
   `[field: SerializeField]`, no backing field). In a player build every ID is null, and
   `Registry.Register` does `entries[value.ID] = value` → `ArgumentNullException` on the first of
   13 item assets, killing `RegistryLoader.Awake` and with it the whole item/save system.
   Fix, then re-save the 13 assets so the value is written.
4. **The player's starting kit is unresolvable** — `ExpeditionBackpack.prefab`'s 10 items point at
   `ScriptableObjects/Items/`, which is not a Resources folder, so they never enter the registry.
   Repoint at the identical `Resources/Items/Artifacts/` assets. Also `Leash.asset` and
   `RocketTurret.asset` still read `itemName: NewItem`, and `Leash.asset` carries the Lasso's icon.
5. **`Resources/Saveable/` is empty.** I created the folder (code requires it); it still needs the
   `Saveable`-carrying prefabs. `SaveWiringValidator.cs` will list them.

### Repo hygiene — cheap, and blocks anyone on Linux/CI
6. **Normalise the 932 path-casing divergences** (§2.1) in one rename-only commit. Until then a
   Linux checkout cannot find `Scenes/World/` at all.
7. **Fix `.gitattributes`** (§2.2) — the three terrain binary globs match nothing, so 24 binary
   heightmaps are being treated as LF-normalised text.
8. **Disable the 14 test scenes still enabled in builds**: the 5 personal scratch scenes,
   `Ferdinand_Test_world`, and its 8 `FerdinandChunk_*`. `enabled: 0` keeps them editor-openable.
9. **Re-save `MinigameArena.unity` as text** — it is the last binary scene and the only asset whose
   references no tool can audit.

### Content gaps the audit exposed
10. **Re-bake the map hologram meshes.** Only x=2–7 of the 48-chunk grid exists, so the western
    quarter is a hole that logs 12 warnings per startup. Tools ▸ World Streaming ▸ Bake Map Meshes.
11. **Wire the minigame** (§4.1) — add a `MatchManager` to the arena scene. Until then the whole
    `Gameplay/Minigame/` subtree, `DeathmatchBot.prefab` and 20 faction assets are dead weight that
    must *not* be deleted.
12. **Re-author `Chunk_7_5`'s three NPCs** onto a live agent prefab. They are scene-native
    `NpcBrain`/`WanderBehaviour` components and are the only thing keeping those two obsolete
    scripts alive. Afterwards `NpcBrain.cs`, `WanderBehaviour.cs`, `EnemyBrain.cs` and
    `IAgentBrain.cs` (~695 LOC) all go, plus `AgentController`'s legacy-brain fallback (~13 LOC).

### Remaining dead code — 19 files, ~1,500 LOC once the false positive is excluded
`cs_unreachable.tsv` is regenerated. `InputControls.cs` (2,954 loc) is the known false positive —
**keep it**. `MapPOI.cs` (81 loc) became dead *as a result of* deleting the orphan chunks: its only
reference was `Chunk_17_10.unity`. Safe to remove now, along with `EnemyBrain.cs` and
`IAgentBrain.cs` (now 0 refs). The rest: `PrebuiltDecorationRules` 180, `RuinSecret` 171,
`AlgaePulse` 136, `EnergyRifle` 118, `EntitySystemSetup` 114, `CoverModule` 114 + `CoverPoint` 86
(the whole cover feature), `EntityEquipmentController` 102, `LeverInteraction` 69,
`LightningSpawner` 66, `FacePlayerModule` 46, `StartingGameManager` 44, `WeaponSelector` 31,
`LobbyMenu/Core/Entity` 28, `MeshReadablePostprocessor` 21, and `Items/Equipped/weapon.cs` (5 lines,
declares no type).

### Highest-value refactors still on the table (all net-negative)
- **The `FlockingModule` cascade** — module (108 LOC) + `AgentController`'s per-frame 30 m
  `OverlapSphere` and three buffers + `AgentContext`'s three `NearbyAgent*` fields. **−141 LOC**,
  and three live `PatrolRobot` prefabs stop paying a broadphase query plus 32 interface
  `GetComponent`s every frame for data nothing reads.
- **`WorldStreamer.PreloadChunksAroundPosition`** → delegate to the guarded plural overload.
  **−34/+2**, and it fixes the off-world clamping bug (§4.8).
- **`IMovementMotor.NudgeDestination`/`SuggestDestination`** — declared, implemented six times,
  called never.
- 80 more in `all_refactors.md`, each with a stated net line count.

### Leftovers worth a sweep
`Archive/` (16 MB of `PRENECK`/`PRETAIL` .blend backups, superseded by git), 11 `.blend1` Blender
auto-backups, `fmod_editor.log` at the repo root, `.claude/skills/.../__pycache__/`,
`docs/architecture/CutsceneExamples.md.meta`, the duplicate `SpaceGame.slnx`, `Settings/Mobile_*`
(a mobile render pipeline in a desktop game), and the unused `Packages/manifest.json` modules.
