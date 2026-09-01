---
system: EntitySystem
layer: characters
summary: How a GameObject becomes an entity — identity, save opt-in, and following the streaming grid between chunks
paths:
  - Assets/Game/Scripts/agents/Entity/
  - Assets/Game/Scripts/agents/Profiles/
  - Assets/Game/Scripts/Core/Persistence/Runtime/SaveableEntity.cs
  - Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs
  - Assets/Game/Scripts/World/Streaming/Core/SceneTracked.cs
symptoms:
  - "a creature disappears for clients when it walks into another chunk"
  - "console warns No prefab registered for id when loading a world"
  - "a runtime-spawned entity is captured in the save but never comes back"
  - "dead NPCs re-instantiate themselves on every reload"
  - "a generated prefab has an empty motor slot and no error was logged"
  - "an NPC is completely invisible to AI targeting"
  - "EntityProfile_RobotPhil / _DesertRat is referenced but does not exist"
  - "a moving NPC keeps nine chunks loaded around itself"
reads_with: [AgentSystem, Persistence, WorldStreaming, Vehicles]
updated: 2026-09-01
---

# Entity System

How a GameObject becomes a first-class **entity** in SpaceGame: how it is authored or spawned, how it gets a stable identity, and how it follows the streaming grid between chunk scenes.

**Scope:** [Assets/Game/Scripts/agents/Entity/](Assets/Game/Scripts/agents/Entity/), [Assets/Game/Scripts/agents/Profiles/](Assets/Game/Scripts/agents/Profiles/), [Assets/Game/Scripts/agents/Core/EntityTargetRegistry.cs](Assets/Game/Scripts/agents/Core/EntityTargetRegistry.cs), [SceneTracked.cs](Assets/Game/Scripts/World/Streaming/Core/SceneTracked.cs), [IPersistentEntity.cs](Assets/Game/Scripts/Core/Persistence/Format/IPersistentEntity.cs), [SaveableEntity.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveableEntity.cs), [SaveablePolicy.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs), [Assets/Game/Editor/Agents/EntityProfileEditors.cs](Assets/Game/Editor/Agents/EntityProfileEditors.cs)
**Related:** [AgentSystem.md](AgentSystem.md) (behaviour modules, factions, perception) · [Persistence.md](Persistence.md) (record/save format) · [WorldStreaming.md](WorldStreaming.md) (chunk load/unload) · [MountSystem.md](MountSystem.md)

## Model

- There is **no `Entity` base class and no central entity manager**. "Entity" is a claim made by attaching components; three independent axes, each with its own marker.
- **Is it part of the mutable world?** → implements [`IPersistentEntity`](Assets/Game/Scripts/Core/Persistence/Format/IPersistentEntity.cs) (empty marker interface, assembly `SpaceGame.Persistence`, zero refs). Implemented by `AgentController`, `MountModule`, `LeggedLocomotion`, `DuneFoilLocomotion`, `SceneTracked`, `DoorInteraction`, `LeverInteraction`, `RepairWorkstation`, `ShipPartRack`, `Ship`, `SpaceshipManager`, `VolumeTrigger`, `CutsceneAction`, `ScanBeacon`, `RuinSecret`.
- **Does it move between chunks?** → [`SceneTracked`](Assets/Game/Scripts/World/Streaming/Core/SceneTracked.cs), which *also* implements `IPersistentEntity` — the two claims are deliberately one.
- **Does it have a save record?** → [`SaveableEntity`](Assets/Game/Scripts/Core/Persistence/Runtime/SaveableEntity.cs), auto-attached by [`SaveablePolicy`](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs); you rarely add it by hand.
- **Is it targetable by AI?** → [`EntityFaction`](Assets/Game/Scripts/agents/Faction/EntityFaction.cs), which self-registers into [`EntityTargetRegistry`](Assets/Game/Scripts/agents/Core/EntityTargetRegistry.cs) on enable. Factionless ⇒ invisible to all targeting. Details in [AgentSystem.md](AgentSystem.md).
- An AI-driven entity additionally carries the agent stack: `Rigidbody` (kinematic) + `CapsuleCollider` + `NavMeshAgent` + `NavMeshAgentMotor` + [`AgentController`](Assets/Game/Scripts/agents/Controller/AgentController.cs) + `AgentAnimatorDriver` + `HealthComponent` + [`HealthReactionModule`](Assets/Game/Scripts/agents/Entity/HealthReactionModule.cs).

## Profiles

**These are not ScriptableObject assets.** Every `EntityProfile_*` is a data-only `MonoBehaviour` you drop on a prefab, configure, and hit **⚙ Generate** on (button drawn by [EntityProfileEditors.cs](Assets/Game/Editor/Agents/EntityProfileEditors.cs)); the component is then removed. Generation is idempotent (`GetOrAdd` + `SerializedProperty` writes). **Four exist:**

| Profile | Source file | Generates / used by |
| --- | --- | --- |
| `EntityProfile_BaseAgent` | [Profiles/EntityProfile_BaseAgent.cs](Assets/Game/Scripts/agents/Profiles/EntityProfile_BaseAgent.cs) | Base stack + `EntityFaction`, `EntityAudioModule`, `NoiseEmitter`, `EntityInventoryComponent`, `EntityLootTable`. Starting point before hand-adding modules. |
| `EntityProfile_NPC` | [Profiles/EntityProfile_NPC.cs](Assets/Game/Scripts/agents/Profiles/EntityProfile_NPC.cs) | Base stack + `FleeModule`+`WanderModule` (enabled), `WatchModule`/`ApproachModule`/`KeepDistanceModule` (added, disabled), `InteractionFocusModule`, inventory. |
| `EntityProfile_GenericEnemy` | [Profiles/EntityProfile_GenericEnemy.cs](Assets/Game/Scripts/agents/Profiles/EntityProfile_GenericEnemy.cs) | Base-patrol + herd + perception + alerts + melee/ranged/kiting (`RobotHerdAttackStyle`). Has `OnValidate` clamping. |
| `EntityProfile_Vehicle` | [Profiles/EntityProfile_Vehicle.cs](Assets/Game/Scripts/agents/Profiles/EntityProfile_Vehicle.cs) | Base stack + `WanderModule` + `MountModule` + `SteerModule`; sizes the capsule and tunes the `NavMeshAgent`. |

**No prefab, scene or asset in the repo currently references any of the four** (GUID grep over `Prefabs/`, `Scenes/`, `Resources/`) — as designed, since the profile is stripped after Generate. [EntitySystemSetup.cs](Assets/Game/Scripts/agents/Profiles/EntitySystemSetup.cs) is an empty static class holding a comment-only setup guide; **it is itself stale** and still names deleted profiles (`EntityProfile_RobotPhil`, `_RobotCath`, `_DesertRat`, `_MountableAnt`, `_BountyHunter`, `_HostileRobot`).

## Key types

| Type | File | Role |
| --- | --- | --- |
| `IPersistentEntity` | [Core/Persistence/Format/IPersistentEntity.cs](Assets/Game/Scripts/Core/Persistence/Format/IPersistentEntity.cs) | Empty marker: "this object is part of the mutable world". The primary `NeedsSaving` clause. |
| `SceneTracked` | [World/Streaming/Core/SceneTracked.cs](Assets/Game/Scripts/World/Streaming/Core/SceneTracked.cs) | `keepChunksLoaded` + `UnloadPolicy{Pin,Migrate,Despawn}`; `SetKeepChunksLoaded(bool)` re-registers. Self-registers in `OnEnable`. |
| `SaveableEntity` | [Core/Persistence/Runtime/SaveableEntity.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveableEntity.cs) | `prefabId` / `instanceId` / `authored` / `SaveScope`; static `LiveEntities` dictionary; `DeriveAuthoredId`, `EnsureRuntime`, `DisownToExternal`, `MarkBuried`. |
| `SaveablePolicy` | [Core/Persistence/Runtime/SaveablePolicy.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs) | The one opt-in rule: `NeedsSaving` / `Ensure` / `EnsureScene(Scene)` / `EnsureSpawned(GameObject)`. |
| `SaveablePrefabRegistry` | [Core/Persistence/Runtime/SaveablePrefabRegistry.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePrefabRegistry.cs) | `prefabId` (asset GUID) → prefab. Sources: `InventoryItem.itemPrefab`, `Resources/Saveable/`, NGO prefab list (lazy on first miss). |
| `WorldStreamer` | [World/Streaming/Core/WorldStreamer.cs](Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs) | Static `s_trackedEntities`; `UpdateSceneMembership` / `ResolveDesiredScene` / `MoveTracked` / `MigrateObjectRpc`. |
| `EntityTargetRegistry` | [agents/Core/EntityTargetRegistry.cs](Assets/Game/Scripts/agents/Core/EntityTargetRegistry.cs) | Static list of `EntityFaction`; `ResolveNearest(owner, relationship, pos)`. AI targeting only — no persistence link. |
| `Registry<T>` | [Core/Registry/Registry.cs](Assets/Game/Scripts/Core/Registry/Registry.cs) | Generic `IRegistryEntry` store keyed by string `ID`. **Items only** — nothing entity-shaped uses it. Filled by [RegistryLoader](Assets/Game/Scripts/Core/Registry/RegistryLoader.cs) from `Resources/Items`. |
| `EntityInventoryComponent` | [agents/Entity/EntityInventoryComponent.cs](Assets/Game/Scripts/agents/Entity/EntityInventoryComponent.cs) | Same `Inventory` class the player uses, on an NPC. |
| `EntityEquipmentController` | [agents/Entity/EntityEquipmentController.cs](Assets/Game/Scripts/agents/Entity/EntityEquipmentController.cs) | NPC holds/fires the *same* `UsableItem` prefabs as the player; sets `ExternallyAimed`, aims via `UseArg.R`. |
| `EntityLootTable` | [agents/Entity/EntityLootTable.cs](Assets/Game/Scripts/agents/Entity/EntityLootTable.cs) | Death drops: guaranteed inventory contents + rolled `LootEntry` list. |
| `HealthReactionModule` | [agents/Entity/HealthReactionModule.cs](Assets/Game/Scripts/agents/Entity/HealthReactionModule.cs) | Threshold module toggling, hurt/death SFX, despawn after `despawnDelay` via `SetActive(false)`. |

## Flows

**Spawn — authored in a chunk scene**
1. Chunk scene loads; `WorldSaveStore.Hydrate` calls `SaveablePolicy.EnsureScene(scene)`.
2. For every GameObject with no `SaveableEntity` that passes `NeedsSaving`: derive id via `SaveableEntity.DeriveAuthoredId` (hash of `sceneName/siblingIndex:name/...`), then `Ensure` (adds `SaveableEntity`, `TransformSaveable`, and any saver its components imply), then `AdoptAuthoredIdentity(derived)`. Identity **before** savers, always.
3. Editor pass `Tools ▸ Save System` bakes a real GUID into the scene file instead — preferred, because a derived id changes if the object is renamed or reparented.

**Spawn — at runtime**
1. Go through [`WorldService.Spawn`](Assets/Game/Scripts/Core/GameServices/Implementations/WorldService.cs) (server-only; errors loudly on a client and on a prefab with no `NetworkObject`).
2. It calls `SaveablePolicy.EnsureSpawned(instance)` → `NeedsSaving` gate → `Ensure` → warns if the prefab has no stamped `prefabId`.
3. `SaveableEntity` assigns itself a fresh GUID `instanceId` with `authored = false`.
4. `NetworkObject.Spawn`. Restored spawns take the same route from `WorldSaveStore.SpawnEntities`, plus `EnsureRuntime` + `AdoptIdentity(prefabId, instanceId)` + `Restore(state)`.

**Register**
1. `SaveableEntity.Awake` → `Live[instanceId]`, **for the object's whole lifetime, not while enabled** (corpses are disabled, not destroyed).
2. `SceneTracked.OnEnable` → `WorldStreamer.RegisterTracked` (static set, survives streamer respawns).
3. `EntityFaction.OnEnable` → `EntityTargetRegistry.Register`.

**Migrate across a chunk boundary** (server only, in `WorldStreamer.UpdateSceneMembership`, every `updateInterval`)
1. `ResolveDesiredScene`: `Pin` → persistent scene; `Migrate` → the loaded chunk scene under the entity, else stay put; `Despawn` → stay put.
2. Skip if already there or if `transform.parent != null` (children follow their parent; Unity rejects `MoveGameObjectToScene` on a child).
3. `MoveTracked` → `SceneManager.MoveGameObjectToScene` locally, then replicate: dynamically spawned `NetworkObject`s are handled by NGO's `SceneMigrationSynchronization`; **in-scene-placed** ones are announced by hand via `MigrateObjectRpc(networkObjectId, sceneName)` (by *name*, since scene handles are per-process).
4. Client can receive the RPC before it has the object or the scene → parked in `pendingMigrations`, retried by `DrainPendingMigrations`. Every announcement is recorded in `announcedMigrations` and `ReplayMigrationsTo(clientId)` on late join.
5. No `NetworkObject` at all ⇒ one-time `WarnUnreplicatedOnce` and the migration stays local.

**Despawn**
1. `HealthReactionModule.Despawn` → `SetActive(false)`. The object stays in `LiveEntities` and stays saveable.
2. Real removal of an authored object: `WorldSaveStore.RecordDestroyed(entity)` writes a tombstone; `RemoveDestroyed` on the next hydrate calls `MarkBuried()` then despawn+destroy. `IsBuried` stops a same-frame capture re-creating the record.
3. `SaveableEntity.OnDestroy` removes it from `Live` only if the registered instance is still itself.

## Multiplayer

- **Server decides everything structural**: chunk membership, migration, runtime spawns, tombstones. `UpdateSceneMembership` runs server-side only.
- Clients receive migrations as RPCs and apply them by `NetworkObjectId` + scene name; they never initiate one.
- An entity that migrates **must** have a spawned `NetworkObject` (and be registered in the network prefab list if runtime-spawned), or clients destroy their copy when its old chunk unloads while the host keeps and saves it. `Pin` is the escape hatch for purely local props.
- Behaviour authority (who runs the AI, who presents effects) is [AgentSystem.md](AgentSystem.md); item use inside `EntityEquipmentController` follows the `Use()`/`Present()` split.

## Persistence

- **Two ids.** `prefabId` = asset GUID of the source prefab, stamped by `SaveableEntity.OnValidate`, answers "what do I instantiate?". `instanceId` = per-object GUID, answers "which record is mine?".
- **Authored vs runtime is the whole storage split.** Authored objects already exist when the chunk loads, so records are *applied in place*; runtime objects are *re-instantiated* from `prefabId` into the chunk's own scene.
- Records are keyed by **identity, never by scene** — an entity that walked into another chunk still finds its record.
- `SaveScope.External` takes an object out of world capture (players; `NpcWorldSim` caravan members via `DisownToExternal`, which is refused outside play mode).
- Unresolvable `prefabId` ⇒ the record is **kept**, not dropped, and warns `No prefab registered for id`. Format details: [Persistence.md](Persistence.md).

## Gotchas

- **No prefab on disk ships a stamped `prefabId`.** Runtime spawns therefore warn and are captured-but-not-restorable until the prefab is put under `Resources/Saveable/`, registered with NGO, or stamped via `Tools ▸ Save System ▸ Wire Saveable Prefabs`.
- **`EntityProfile_*` are components, not ScriptableObjects,** and there are exactly four. Any doc, comment or memory naming `EntityProfile_RobotPhil`/`_Cath`/`_Ernst`/`_Roberto`/`_DesertRat`/`_MountableAnt`/`_BountyHunter`/`_HostileRobot`/`_RobotHerdPatrol` is describing files that no longer exist.
- **`Core/Registry/` is the item registry.** It has nothing to do with entities; the entity-side lookups are `SaveableEntity.LiveEntities` (persistence) and `EntityTargetRegistry` (targeting). Don't wire an entity into `Registry<T>`.
- **`SetObject(controller, "MotorComponent", …)` is case-sensitive.** `FindProperty` returns null for the wrong casing and `SetObject` swallows it, leaving an empty motor slot on every generated prefab, silently.
- **A `Migrate` entity with no `NetworkObject` desyncs silently for clients** — the warning fires once, per object, and is easy to miss. Prefer `Pin` or add a `NetworkObject`.
- **Migration only moves roots.** A rider parented to a mount migrates with the mount; unparent it mid-migration and it is left in the old scene.
- **Runtime-spawned NPCs must call `SceneTracked.SetKeepChunksLoaded(false)`** (as `NpcWorldSim` does) or every caravan drags nine loaded chunks around with it.
- **`NeedsSaving` inferences all miss this game's machines.** A legged rig is a *kinematic* Rigidbody with no `NavMeshAgent`; the DuneFoil has no Rigidbody on its root. Implement `IPersistentEntity` — do not rely on health/agent/rigidbody heuristics.
- **Death is `SetActive(false)`, not `Destroy`.** Anything treating "in `LiveEntities`" as "alive" is wrong; that mistake previously re-instantiated dead runtime entities on every hydrate.
- **`SaveableEntity` is `[DisallowMultipleComponent]`**, and `DeriveAuthoredId` collides for identically placed objects — it appends a deterministic `#n` rather than randomising.

## Extending

1. Decide what the thing is. Moves between chunks → add `SceneTracked` and pick a policy. Static but stateful (door, lever, beacon) → implement `IPersistentEntity` on its own component instead.
2. If it has AI: run the closest `EntityProfile_*` over the prefab, hit Generate, delete the profile component. Otherwise add the agent stack by hand.
3. Add `EntityFaction` (+ faction asset and relationship table) if anything should target it or it should target anything.
4. If it holds state a saver does not already cover, add an `ISaveable` and a clause in `SaveablePolicy.Ensure` so it is auto-attached — see [Persistence.md](Persistence.md).
5. If it is ever spawned at runtime: give the prefab a `NetworkObject`, register it in the network prefab list, and put it under `Resources/Saveable/` (or reimport so `prefabId` is stamped). Spawn through `WorldService.Spawn`, never raw `Instantiate`.
6. Verify: (a) drive it across a chunk boundary **on a client**, not just the host; (b) save, quit, reload, and confirm its `instanceId` appears in the save JSON at the new position.
