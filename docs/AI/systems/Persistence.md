---
system: Persistence
layer: core
summary: Identity-keyed, streaming-aware save system; one JSON document per world built from ISaveable payloads
paths:
  - Assets/Game/Scripts/Core/Persistence/Runtime/
  - Assets/Game/Scripts/Core/Persistence/Adapters/
  - Assets/Game/Scripts/Core/Persistence/Format/
  - Assets/Game/Scripts/Core/Persistence/Editor/
symptoms:
  - "state resets to prefab defaults after I save, quit and load the world"
  - "my saver's key is nowhere in the save JSON"
  - "an entity duplicates every time I reload the world"
  - "'[Save] No prefab registered for id …' and the object never comes back"
  - "the loaded player cannot walk, or an object snaps back after I restore its position"
  - "a creature or vehicle reappears at its authored position instead of where I left it"
reads_with: [EntitySystem, SceneTransitions, Vehicles, Multiplayer]
updated: 2026-09-01
---

# Persistence / Save-Load

Identity-keyed, streaming-aware save system: one JSON document per world, assembled from per-component `ISaveable` payloads on the server.

**Scope:** [Assets/Game/Scripts/Core/Persistence/](Assets/Game/Scripts/Core/Persistence/) — `Format/` (asmdef `SpaceGame.Persistence`, zero refs, Newtonsoft only), `Runtime/`, `Adapters/` (61 savers), `Editor/` (Assembly-CSharp).
**Related:** [.claude/skills/spacegame-persistence/SKILL.md](.claude/skills/spacegame-persistence/SKILL.md) (recipes) + [reference.md](.claude/skills/spacegame-persistence/reference.md) (record shapes) · [EntitySystem.md](EntitySystem.md) · [InteriorScenes.md](InteriorScenes.md) · [MountSystem.md](MountSystem.md) · [Lobby.md](Lobby.md)

## Model

- **Identity, never scene.** `WorldRecord.Entities` is a flat `instanceId -> EntityRecord` map. `EntityRecord.Scene` is *routing only* (which scene load re-spawns a runtime object), re-stamped on every capture, because `WorldStreamer` migrates entities between chunks.
- **Three populations.** World objects → [WorldSaveStore](Assets/Game/Scripts/Core/Persistence/Runtime/WorldSaveStore.cs) keyed by instance id; players → [PlayerSaveService](Assets/Game/Scripts/Core/Persistence/Runtime/PlayerSaveService.cs) keyed by profile GUID (`SaveScope.External`, world store steps over them); session-wide → global savers on [SaveManager](Assets/Game/Scripts/Core/Persistence/Runtime/SaveManager.cs).
- **The store's invariant:** an object's state is EITHER live in a loaded scene OR in the record — never neither, never both drifting. Maintained by hooking `WorldStreamer` / `InteriorManager` load+unload.
- **One saver owns one key** inside its entity's `StateBag`; nothing else writes it. That is why adding/removing a saver or a field needs no migration.
- **Pose lives on the record**, not in a saver — a runtime object needs its position *before* it exists.
- **Opt-in is by component, not by list** ([SaveablePolicy.NeedsSaving](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs#L67)); savers are auto-attached by `SaveablePolicy.Ensure`.
- Authored (in a scene file) = record is a **delta**, restored in place, removed only by a tombstone. Runtime = record is a **recipe**, re-instantiated from `prefabId`.

## Key types

| Type | File | Role |
|---|---|---|
| `SaveManager` | [Runtime/SaveManager.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveManager.cs) | Front door. Owns both stores, autosave timer, quit save, global savers, deferred passes, streaming subscriptions |
| `WorldSaveStore` | [Runtime/WorldSaveStore.cs](Assets/Game/Scripts/Core/Persistence/Runtime/WorldSaveStore.cs) | `Hydrate`/`Dehydrate` per scene; `Compact`; `RecordDestroyed`; holds unresolvable records in `unresolved` |
| `PlayerSaveService` | [Runtime/PlayerSaveService.cs](Assets/Game/Scripts/Core/Persistence/Runtime/PlayerSaveService.cs) | profile → `PlayerRecord`; `Bind`/`Unbind`/`CaptureAll`; raises `PlayerBound` |
| `SaveableEntity` | [Runtime/SaveableEntity.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveableEntity.cs) | `prefabId`/`instanceId`/`authored`/`SaveScope`; `LiveEntities`; `Capture`/`Restore`/`NotifyLoadComplete` |
| `SaveablePolicy` | [Runtime/SaveablePolicy.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs) | `NeedsSaving` / `Ensure` / `EnsureSpawned` / `EnsureScene` — the single opt-in + auto-wiring rule |
| `SaveablePrefabRegistry` | [Runtime/SaveablePrefabRegistry.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePrefabRegistry.cs) | `prefabId` → prefab from 3 sources: `InventoryItem.itemPrefab`, `Resources/Saveable/`, NetworkManager prefab list (lazy, on first miss) |
| `SaveTeleport` | [Runtime/SaveTeleport.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveTeleport.cs) | The *only* correct placement: disables `CharacterController`, `NavMeshAgent.Warp` (return value checked), moves child rigidbodies, raises `ITeleportAware` |
| `DeferredNavMeshWarp` | [Runtime/DeferredNavMeshWarp.cs](Assets/Game/Scripts/Core/Persistence/Runtime/DeferredNavMeshWarp.cs) | Retries a refused warp until the chunk's NavMesh exists (10 s, 4 m sample radius) |
| `SaveNetworking` | [Runtime/SaveNetworking.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveNetworking.cs) | `SpawnIfNetworked` (checks the prefab table itself), `DespawnAndDestroy`, play/edit-mode `Destroy` |
| `WorldSession` | [Runtime/WorldSession.cs](Assets/Game/Scripts/Core/Persistence/Runtime/WorldSession.cs) | Static: which world, its config GUID, `IsNew`, the staged document (`Consume()` once) |
| `PlayerSaveSync` / `PlayerSaveBinder` / `PlayerProfile` | [Runtime/](Assets/Game/Scripts/Core/Persistence/Runtime/) | Owner claims its profile over RPC / offline binding / per-instance GUID in PlayerPrefs |
| `SaveHotkeys` | [Runtime/SaveHotkeys.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveHotkeys.cs) | F5 quicksave, F9 quickload (reloads the scene through Netcode's SceneManager so clients follow) |
| `ISaveable` / `IDeferredSaveable` | [Format/ISaveable.cs](Assets/Game/Scripts/Core/Persistence/Format/ISaveable.cs) | `SaveKey`/`CaptureState`/`RestoreState`; deferred adds `OnLoadComplete` + `LoadOrder` (`Early -100` / `Default 0` / `Late 100`) |
| `IPersistentEntity` | [Format/IPersistentEntity.cs](Assets/Game/Scripts/Core/Persistence/Format/IPersistentEntity.cs) | Empty marker = "I am mutable world" — the declared opt-in for kinematic rigs and rootless vehicles |
| `SaveRef` / `ISaveRefBinder` | [Format/SaveRef.cs](Assets/Game/Scripts/Core/Persistence/Format/SaveRef.cs) | `{kind:player|entity, id}`; live half is [SaveRefBinder.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveRefBinder.cs) installed on `SaveRefBinding.Active` |
| `SaveDocument`/`SaveHeader`/`PlayerRecord`/`WorldRecord`/`EntityRecord`/`SceneKey` | [Format/SaveDocument.cs](Assets/Game/Scripts/Core/Persistence/Format/SaveDocument.cs) | The file shape |
| `StateBag` / `SaveSerializer` / `UnityJsonConverters` | [Format/](Assets/Game/Scripts/Core/Persistence/Format/) | `key -> JObject`; the one `JsonSerializer`; Vector2/3/Vector2Int/Quaternion/Color converters |
| `SaveFileStore` / `SaveSlots` / `WorldIdentity` | [Format/](Assets/Game/Scripts/Core/Persistence/Format/) | Atomic `.tmp`→`File.Replace`→`.bak` write, `.bak` fallback read; slot listing; world naming + config guard |
| `SaveMigrator` + `Migrations/V1GlobalEntities` | [Format/SaveMigrator.cs](Assets/Game/Scripts/Core/Persistence/Format/SaveMigrator.cs) | Version ladder; v1 (per-scene records) → v2 (flat) |

### Adapters (61) — [Adapters/](Assets/Game/Scripts/Core/Persistence/Adapters/), namespace `SpaceGame.Core.Persistence`. ᴰ = also `IDeferredSaveable`. Keys are permanent — renaming one orphans every record under the old spelling.

| Category | Savers (key) |
|---|---|
| Pose & motion | `Transform`(transform) `Rigidbody`(rigidbody, velocity only) `MotorState`(motor) `LeggedGait`(gait) `ArticulatedParts`(parts, keyed by hierarchy path) |
| Vitals & kit | `Health`(health, 0 HP *is* dead) `HealthReaction` `EntityFaction` `EntityEquipment`ᴰ `EntityInventory` |
| Agent mind | `AgentState`ᴰ(agent) `Provocation`ᴰ `Search` `Alert` `NoiseInvestigation` `Flee`ᴰ `Cover`ᴰ `Pursuit`ᴰ `CombatCadence`ᴰ |
| Agent routine | `Patrol` `BasePatrol` `Wander` `AirWander` `WanderBehaviour` `NpcTask` `AgentGoal` `AgentPacing` `HerdMember` `Formation` `NpcWorld`(one record per caravan group) |
| Vehicles & turrets | `Mount`ᴰ `DuneFoil` `Ornithopter`ᴰ `Ship` `ShipParts` `ShipAccent` `Spaceship` `Turret`ᴰ `WeaponMount` |
| World interactables | `Door` `Lever` `RepairWorkstation` `Trader` `VolumeTrigger` `RuinSecret` `ScanBeacon` `CutsceneAction`(stops `playOnce` replaying) |
| Player-scoped (on `PlayerCharacter.prefab`) | `PlayerInventory`ᴰ(inventory) `Backpack`ᴰ `SuitColor` `PlayerLook` `Flashlight` `Effects` `InteriorVisit`ᴰ `PortalPair`ᴰ `Health` |
| Global (`RegisterGlobalSaver`) | `GameState`(gameState) `DayNight`(sky) `Sandstorm`(weather) `Map`(map) `HerdState`(herds) `Leash`(leashes)ᴰ |

## Flows

**Load** (`SaveManager.Awake`, line ~133):
1. `WorldSession.Consume()` → `new WorldSaveStore(doc.World)` + `new PlayerSaveService(doc.Players)`; install `SaveRefBinding.Active`.
2. `RestoreGlobals` stages every global payload; a saver registering later is served (once) in `RegisterGlobalSaver`.
3. Subscribe `WorldStreamer.OnChunkLoaded/WillUnload/Unloaded` and the three identical `InteriorManager` events.
4. `Start`: `Hydrate(SceneKey.Persistent, this scene)` by hand — no streaming event ever fires for it, and every Pin'd `SceneTracked` entity lives there. Then `SaveNewWorld()` writes the file immediately.
5. Per scene `Hydrate`: `EnsureScene` (wire unwired) → `RemoveDestroyed` → `RestoreAuthored` (`SaveTeleport.Move` then `entity.Restore`) → `SpawnEntities` (Instantiate → `MoveGameObjectToScene` → `EnsureSpawned` → `EnsureRuntime` → `AdoptIdentity` → `Restore` → `SpawnIfNetworked`) → `OnSceneHydrated`.
6. Player: `PlayerSaveSync` (owner) → `ClaimProfileServerRpc` → `PlayerSaveService.Bind` → place, restore, `NotifyLoadComplete`, then raise `PlayerBound` → `SaveManager.RunWorldDeferredPass()`.

**Save** (`SaveManager.Save`): `BuildDocument` → `playerService.CaptureAll()` + `worldStore.DehydrateLoaded()` + persistent scene + `Compact()` + `CaptureGlobals`. Serialize on the main thread, `Task.Run` the write (synchronous for quit/exit, which *waits out* an in-flight write rather than standing down). Guards: `WouldDowngradeFormat`, `WouldDiscardAllPlayers`. Triggers: 300 s timer (retries in ≤15 s after a refusal), `OnApplicationQuit`, `SaveManager.SaveOnExit()` (menu return), F5, `SaveNewWorld`.

**World switch:** menu calls `WorldSession.StageNew(name, config)` or `StageExisting(worldId, config, out error)` (reads the file, checks `WorldIdentity.AcceptsConfig`), then loads the world scene. `WorldSession.Clear()` on return to menu. Quickload restages the *same* world and reloads via `NetworkManager.SceneManager.LoadScene(Single)`.

**Deferred pass** runs: once per world load, again on **every** `PlayerBound`, and again per scene hydrated after the first pass (`HandleSceneHydrated`). `OnLoadComplete` must be idempotent.

## Multiplayer

- **Server-only.** Every hydrate/dehydrate/save handler early-returns on `Network.IsNetworked && !Network.Server`. Singleplayer is a host of one, so the host path is the only path that ever writes.
- Clients get world state through normal replication; a client F5 is refused with an explanation, and a client F9 is refused because reloading the scene would drop it out of the session.
- Restored objects go through `SaveNetworking.SpawnIfNetworked`, which checks `NetworkConfig.Prefabs.NetworkPrefabOverrideLinks` for `PrefabIdHash` **itself** — NGO does not throw for an unregistered server-side dynamic spawn, the *client* silently fails to construct it.
- Player pose is owner-authoritative, so placement goes through `PlayerSaveService.Bind` (skipped for the host, already placed), never a server teleport.
- `SaveablePrefabRegistry` folds in the NetworkManager prefab list lazily on the first cache miss — scanning at load time races NetworkManager's `Awake`.

## Persistence — on-disk format

`~/Library/Application Support/Hackerspace NTNU/SpaceGame/Saves/<sanitized world>.json` (`Application.persistentDataPath/Saves`), plus `.bak`, transiently `.tmp`.

```
header   version(2) savedAtUtc playtimeSeconds gameVersion slotLabel worldName worldConfigId
players[] profileId displayName position rotation state:StateBag
world    global:StateBag · entities:{instanceId: EntityRecord} · destroyed:[instanceId]
EntityRecord  prefabId instanceId scene authored position rotation scale hasPose hasScale state:StateBag
StateBag      { entries: { saverKey: <payload object> } }
SceneKey      "persistent" | "chunk:<x>,<y>" | "scene:<Name>"
```

`SaveSerializer.Serializer` is the only serializer: `IgnoreSerializableAttribute` (public **fields**), `MissingMemberHandling.Ignore`, `ObjectCreationHandling.Replace`, `TypeNameHandling.None`, `NullValueHandling.Ignore`, plus the Unity struct converters (zero quaternion reads back as `identity`). `slotLabel` tells you which trigger wrote the file (world name = `SaveOnExit`; `Autosave` = timer or quit; `Quicksave` = F5).

## Gotchas

| Trap | Silent symptom | Correct move |
|---|---|---|
| Reading a payload by probing `JObject` tokens | `StackOverflowException` in `Vector3.normalized` | `state.ToObject<State>(SaveSerializer.Serializer)` |
| `CaptureState` returning a bare list/int/string | Key dropped (error logged, capture survives) — see [StateBag.Set](Assets/Game/Scripts/Core/Persistence/Format/StateBag.cs#L44) | Wrap in a public-field struct |
| Ignoring the `state == null` branch of `RestoreState` | Stale value re-applied after a save that stored nothing | null means "restore defaults"; clear pending refs too |
| Resolving a `SaveRef` in `RestoreState` | Rider never re-seated; second player's mount empty forever | Resolve in `OnLoadComplete`, consume only on success |
| Treating `OnLoadComplete` as once-only | State re-applied over a world that moved on | Idempotent — it fires per player bind and per late chunk |
| Restoring pose with `transform.position` | Object snaps back within a frame | The record's pose is applied for you via `SaveTeleport.Move` |
| Persisting `isKinematic` | Loaded player cannot walk (quit-time autosave captures the body after netcode teardown) | Never save engine-owned flags; `RigidbodySaveable` returns null for a kinematic body |
| Opting in via non-kinematic `Rigidbody` | Every mount/walker/vehicle absent from the file (legged rigs are kinematic; DuneFoil has no root body) | Implement `IPersistentEntity` |
| `Instantiate`/`Destroy` for world objects | Duplicate per reload; looted authored crate refills | `GameServices.World.Spawn`/`.Despawn` ([WorldService.cs](Assets/Game/Scripts/Core/GameServices/Implementations/WorldService.cs)) |
| `EnsureRuntime` on an authored scene object | Would duplicate on every load — now refused with a warning | Spawn a fresh instance |
| Giving a system-owned object its own `SaveableEntity` | Two competing copies **and** a lifeless duplicate object | `SaveScope.External` on the prefab, or `DisownToExternal()` |
| `AddComponent`-ing a saver at runtime | Never captured — the saver list is cached on first `Savers()` | `entity.InvalidateSavers()` |
| Renaming/re-parenting an *unwired* scene object | Derived id (`DeriveAuthoredId`: scene + hierarchy path + sibling index, FNV-1a) changes → record orphaned | Bake real GUIDs with the wiring tool |
| Prefab-instance `instanceId` assigned + `SetDirty` | Value equals the prefab's, Unity records no override, nothing hits the scene file | `RecordAsPrefabOverrides` via `SerializedObject`; grep with `grep -A1 "propertyPath: instanceId"` |
| Making a prefab **variant** instead of moving it | `OnValidate` stamps a new `prefabId` from the variant's GUID; disagrees with every existing record | Move the prefab |
| Unresolvable `prefabId` | `[Save] No prefab registered for id …` — record is **kept** in `unresolved`, not dropped | Register in NetworkManager, or `Resources/Saveable/`, or run the wiring tool |
| Playing a world scene opened directly in the editor | `Save ignored: no world is active` — no `WorldSession`, so nothing saves | Enter via the main menu |
| Saver on a child under a nested `SaveableEntity` | State lands in the child's record | Collection stops at any nested `SaveableEntity` |
| A saver caching its component in `Awake` | EditMode round-trip tests cannot exercise it | Lazy `GetComponent` property |
| Restoring 0 HP unguarded | Loot re-dropped, death reaction replayed each load | Check `HealthComponent.IsRestoring` |

## Extending

1. **Opt in.** `SaveablePolicy.NeedsSaving` already says yes for `IPersistentEntity`, `HealthComponent`, `PickupableItem`, `NavMeshAgent`, non-kinematic `Rigidbody` (and no for the `Transient` blacklist and anything with `PlayerSaveBinder`/`PlayerSaveSync`). A new vehicle/locomotion root matching none of those implements `SpaceGame.Persistence.IPersistentEntity`.
2. **Write the saver** in `Adapters/`, namespace `SpaceGame.Core.Persistence` (not the feature asmdef — that drags in Newtonsoft): `const string Key`, a plain public-field `State` struct, `CaptureState()` returning null at defaults, `RestoreState` via `SaveSerializer.Serializer`, lazy component lookup.
3. **Auto-attach it**: add a clause to `SaveablePolicy.Ensure` (or the `EnsureAgent*` / `EnsureWorldInteractables` helpers) keyed off the component that implies it.
4. **Cross-object state**: hold a `SaveRef`, add `IDeferredSaveable`, resolve in `OnLoadComplete`, consume on success only; use `LoadOrder = Early` if others read your result.
5. **Runtime-spawned**: spawn via `GameServices.World.Spawn` / despawn via `.Despawn`; make `prefabId` resolvable by one of the three registry routes; spawn sites with a better key call `SaveableEntity.EnsureRuntime(obj, item.ID)`.
6. **Player-scoped**: put the saver on `PlayerCharacter.prefab` (the networked player is a *variant*, so GUID-grepping `PlayerCharacterNetworked.prefab` finds nothing). **Session-wide**: `RegisterGlobalSaver` in `OnEnable` / `UnregisterGlobalSaver` in `OnDisable` — order does not matter.
7. **Wire and validate**: `Tools ▸ Save System ▸` *Wire Saveable Prefabs* / *Wire Saveable Scene Objects* / *Wire Saveable Chunk Scenes* / *Validate Save Wiring* / *Report Unsaved State* ([Editor/](Assets/Game/Scripts/Core/Persistence/Editor/)). Idempotent; never run the wiring ones in Play mode.
8. **Prove it**: EditMode `PersistenceProbe.For(prefab).Mutate(…).AssertSurvivesRoundTrip()` in [PrefabPersistenceTests.cs](Assets/Game/Editor/Tests/PrefabPersistenceTests.cs) (two project-wide sweeps cover new prefabs automatically); then play-mode F5/F9, then quit and re-enter via Load World; read the JSON (`keys=[]` means wired and saving nothing); reload **twice** and count entity records — duplication only shows on the second cycle. Format-only tests live in [Assets/Game/Tests/EditMode/](Assets/Game/Tests/EditMode/) and [Assets/Game/Tests/Editor/](Assets/Game/Tests/Editor/).
9. **Migrations only for document-shape changes**: adding/removing a saver or a field needs none. Otherwise bump `SaveDocument.CurrentVersion` and add an `ISaveMigration` operating on the raw `JObject` in the same commit; a file from a newer build is refused on both read and write.
