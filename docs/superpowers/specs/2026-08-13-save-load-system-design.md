# Save / Load System — Design

Date: 2026-08-13

## Problem

The game has no persistence. Nothing survives a session: player position, health,
hotbar contents, backpack contents, dropped items, the game timer. A player who quits
starts over at the spawn point with the prefab's starting items.

Two properties of this project make a naive "serialize the scene" approach wrong:

1. **The world is streamed.** `WorldStreamer` loads and unloads 256×256 m chunk scenes
   around the player. At any moment most of the world is not in memory, so "walk the
   scene graph and write it out" captures a fraction of the world and silently loses the
   rest.
2. **The game runs on Netcode for GameObjects,** always as a host — even singleplayer
   (`SingleplayerInitializer` calls `StartHost`). State lives on the server and
   replicates out. Saving must therefore be a server-authority operation, and loading
   must restore server state and let the existing `NetworkVariable`/`NetworkList`
   replication carry it to clients.

## Approach

The standard, well-trodden Unity pattern — a **`SaveableEntity` + `ISaveable` component
registry serialized to JSON** — is the right base. It is what Unity's own saving samples,
the widely used RPG-course saving system, and most shipped indie titles use. It is
data-driven, needs no reflection over MonoBehaviour internals, and survives refactors
because every participant names its own key and owns its own payload struct.

On top of that base this design adds the one thing the standard pattern lacks: a
**world state store** that reconciles persistence with chunk streaming.

### The central invariant

> An object's state is **either** live in a loaded scene **or** recorded in the store —
> never neither, never both stale.

`WorldSaveStore` holds an in-memory `SceneRecord` per chunk. It hooks chunk streaming:

- **chunk loaded** → *hydrate*: apply recorded state to authored objects in that scene,
  delete authored objects recorded as destroyed, instantiate recorded runtime objects
  into that scene.
- **chunk about to unload** → *dehydrate*: walk the scene, capture every `SaveableEntity`
  into its record, then let the unload proceed.
- **save** → dehydrate every currently-loaded chunk in place (without unloading), then
  serialize the whole store.

This makes streaming and saving the same mechanism. A crate the player moved in chunk
(3,2) keeps its position when that chunk unloads and reloads mid-session, and that is
exactly the state that gets written to disk.

### Rejected alternatives

- **Unity `JsonUtility` + one big blob.** No dictionary support, no polymorphism, no
  null discrimination, and no way to add a field without hand-writing migration. Rejected.
- **Binary `BinaryFormatter`.** Obsolete and a remote-code-execution vector; Microsoft has
  removed it in .NET 9. Rejected.
- **ScriptableObject-as-savefile.** Does not work in builds. Rejected.
- **Netcode `NetworkVariable` snapshotting.** Only covers spawned NetworkObjects in loaded
  scenes — precisely the fraction of the world that streaming leaves in memory. Rejected.

`Newtonsoft.Json` (`com.unity.nuget.newtonsoft-json` 3.2.2) is already resolved in this
project as a transitive dependency of Netcode and the multiplayer tools. It will be
promoted to an explicit entry in `Packages/manifest.json` so it cannot vanish if those
packages change.

## Architecture

### Assemblies

```
Assets/Game/Scripts/Core/Persistence/
  Format/     SpaceGame.Persistence.asmdef  — pure C#, no game types, fully unit-tested
  Runtime/    Assembly-CSharp — SaveManager, WorldSaveStore, SaveableEntity, registries
  Adapters/   Assembly-CSharp — one ISaveable per persisted subsystem
  Editor/     Editor tooling — prefab stamping, save-folder inspection
```

The split matters: `Format/` has no dependency on any game type, so
`SpaceGame.Tests.EditMode` can reference it and test file IO, versioning and the JSON
converters as ordinary C#. `Assembly-CSharp` automatically references auto-referenced
asmdefs, so game components can implement `ISaveable` freely. (The reverse — an asmdef
referencing `Assembly-CSharp` — is impossible, which is why the game-facing half stays
outside an asmdef and its tests live in an `Editor/` folder.)

### Data model

```
SaveDocument
├── header   SaveHeader   version, savedAtUtc, playtimeSeconds, gameVersion, slotLabel
├── players  List<PlayerRecord>
│              profileId, position, rotation, state: StateBag
└── world    WorldRecord
           ├── global   StateBag                        // game timer, flags
           └── scenes   Dictionary<string, SceneRecord>  // key: "chunk:3,2" | "scene:AlgeaCave" | "persistent"

SceneRecord
├── entities          List<EntityRecord>          // runtime-spawned objects
├── authored          Dictionary<string, StateBag> // authored objects, keyed by instanceId
└── destroyedAuthored List<string>                 // authored objects the player removed

EntityRecord   prefabId, instanceId, position, rotation, state: StateBag
StateBag       Dictionary<string, JObject>         // saverKey → that saver's own payload
```

Every payload is namespaced under the key its `ISaveable` declares (`"health"`,
`"inventory"`, `"backpack"`). Adding a saver adds a key; removing one leaves an ignored
key. Neither breaks an existing file.

### Identity

Two independent identities, mirroring how `InventoryItem` already derives a stable `ID`
from its asset GUID in `OnValidate`:

- **`prefabId`** — the asset GUID of the prefab, stamped into `SaveableEntity` by
  `OnValidate` when the component sits on a prefab asset. Answers "what do I instantiate
  to bring this back?"
- **`instanceId`** — a GUID identifying *this* object. Assigned at author time for
  scene-placed objects (and de-duplicated on copy/paste), at spawn time for runtime
  objects. Answers "which saved record is this object's?"

`SaveablePrefabRegistry` resolves `prefabId → GameObject` from two sources, loaded by the
existing `RegistryLoader`:

1. every `InventoryItem` in the item registry contributes its `itemPrefab` — so dropped
   items persist without touching a single item prefab;
2. any prefab under `Resources/Saveable/` contributes under its own asset GUID.

### Ownership: `SaveScope`

Not every saveable object belongs to the world. `SaveableEntity` carries a `SaveScope`:

- **`World`** (default) — captured and restored by `WorldSaveStore`, with the scene it
  stands in.
- **`External`** — another system owns the record. `WorldSaveStore` sees it and passes over.

Players are `External`. This is not a refinement — without it the world store captures the
player (which lives in the persistent scene and carries a `SaveableEntity` like anything
else) as an ordinary world entity, and the next load instantiates a lifeless copy from the
player prefab beside the one Netcode spawned. The first live probe of the running game
found exactly that, and it is what `WorldSaveStoreTests.Dehydrate_IgnoresEntitiesOwnedBy‑
AnotherSystem` now guards.

### Participation

```csharp
public interface ISaveable
{
    string SaveKey { get; }     // stable, namespaced, e.g. "health"
    object CaptureState();      // any Newtonsoft-serializable payload
    void RestoreState(JObject state);
}
```

`SaveableEntity` gathers the `ISaveable` components on its own GameObject and children
(excluding nested `SaveableEntity` subtrees, so a deployed backpack does not get captured
twice).

Shipped adapters:

| Adapter | Key | Covers |
|---|---|---|
| `HealthSaveable` | `health` | any `HealthComponent` — player, agents, destructibles |
| `PlayerInventorySaveable` | `inventory` | hotbar slot item IDs + selected slot |
| `BackpackSaveable` | `backpack` | both compartments of `BackpackContainer` |
| `RigidbodySaveable` | `rigidbody` | velocity, angular velocity, kinematic flag |
| `TransformSaveable` | `transform` | local scale + moved authored objects |
| `GameStateSaveable` | `gameState` | `GameManager.GameTimer` |

Position and rotation of runtime entities live on `EntityRecord` itself rather than in a
saver, because the store needs them before the object exists in order to instantiate it.

### Codecs: format apart from wiring

The two savers with real logic — hotbar and backpack — keep it in static codecs
(`InventorySaveCodec`, `BackpackSaveCodec`) that take an `IPlayerInventory` or a
`BackpackContainer` and no GameObject. The MonoBehaviour only resolves the target and
delegates.

This is not decoration. **MonoBehaviour `Awake` does not run outside play mode**, so
`PlayerInventoryComponent` hands out a null inventory and `BackpackController` never builds
a pack in an EditMode test — logic reachable only through those components is logic no test
can reach. It follows the `HotbarNavigation` precedent already in this repo.

### Guarding the silent failure

The format's tolerance — a payload whose shape no longer matches is reported *absent*, so
the saver keeps its defaults — is what lets savers be reshaped without migrations. The cost
is that renaming a field looks exactly like "this saver is new": no compile error, no
exception, no warning, and health quietly comes back full.

`SavePayloadCompatibilityTests` freezes a literal sample of every saver's payload, every
save key, and a whole v1 document. Renaming a field fails that fixture, which is the only
place the failure can be made loud. The fixture's own doc comment says what to do when it
fails: **do not edit the literal to match the new shape** — restore the name, or add a
migration and keep the old literal as a second case.

`Tools ▸ Save System ▸ Validate Save Wiring` covers the faults no test can see because they
are asset wiring: a `Resources/Saveable` prefab without a `SaveableEntity` or a prefab id,
an item whose stamped ID has drifted from its asset GUID, a saveable networked prefab
missing from Netcode's registration, duplicate instance ids in open scenes, two savers
sharing a key.

### Orchestration

- **`SaveManager`** (persistent scene, singleton, server-only). `Save(slot)`,
  `Load(slot)`, `QuickSave`, autosave on an interval and on application quit. Capture
  runs on the main thread; the byte-level write is handed to a background `Task`.
- **`PlayerSaveService`** (server-only). Owns `profileId → PlayerRecord`. `PlayerProfile`
  stores a per-machine GUID in `PlayerPrefs`.
- **`SaveBootstrap`** (persistent scene). If `SaveManager.PendingLoad` is set, it feeds
  the world record into `WorldSaveStore` and the player records into `PlayerSaveService`
  *before* `NetworkGameManager` spawns anyone.
- **`PlayerSaveSync`** (networked player prefab, `NetworkBehaviour`). The owner reports its
  `profileId` to the server on spawn; the server applies the matching record and
  registers the player so a later save captures back into it.
- **`PlayerSaveBinder`** (player prefab, plain `MonoBehaviour`). Covers the players no
  network spawn produced: one placed in a scene at edit time — how the project's scenes are
  usually entered from the editor — and the offline `PlayerCharacter` prefab, which has no
  `NetworkObject` for a `NetworkBehaviour` to work with. Both are still the local player and
  still need their state saved. The first live probe of the running game came back with an
  empty player list for precisely this reason.

### Load flow

```
MainMenuUI.ContinueGame(slot)
  → SaveManager.PendingLoad = read(slot)          // read + migrate before any scene work
  → StartHost + LoadScene(persistentScene)
  → SaveBootstrap seeds WorldSaveStore + PlayerSaveService
  → NetworkGameManager asks PlayerSaveService for the host's saved position
      • found     → preload chunks around it, spawn there
      • not found → existing SpawnPoint path, unchanged
  → chunks stream in; WorldSaveStore hydrates each one as it lands
  → PlayerSaveSync applies health / inventory / backpack
```

Remote clients spawn at the spawn point and are then teleported and restored when their
`profileId` reaches the server. Connection approval stays off, so the lobby and Relay
flows are untouched.

### Durability

`SaveFileStore` never writes a save file in place:

1. serialize to `<slot>.json.tmp`, flush and `FileStream.Flush(true)` to force to disk;
2. `File.Replace(tmp, live, backup)` — atomic on every platform Unity targets;
3. on read, a corrupt or truncated live file falls back to the `.bak`.

A crash or a pulled plug mid-write therefore costs at most the newest save, never the
existing one.

### Versioning

`SaveHeader.version` is an integer. `SaveMigrator` runs a chain of
`ISaveMigration { int FromVersion; void Apply(JObject) }` steps over the raw JSON until it
reaches the current version. A file newer than the running build is refused with a clear
message rather than half-loaded.

## Testing

EditMode tests, run headlessly through `HeadlessTestRunner`.

Pure (`SpaceGame.Tests.EditMode` → `SpaceGame.Persistence`):

- round-trip of every DTO, including empty and maximal documents;
- `Vector3` / `Quaternion` / `Vector2Int` converters (Newtonsoft otherwise recurses through
  `Vector3.normalized`);
- atomic write: interrupted write leaves the previous file intact; corrupt live file reads
  through to `.bak`; missing file returns "no save" rather than throwing;
- migration chain applies in order, is a no-op at current version, and refuses a future
  version;
- slot enumeration and metadata ordering.

Game-facing (`Assets/Game/Tests/Editor/`):

- `SaveableEntity` gathers child savers but stops at a nested `SaveableEntity`;
- `WorldSaveStore` dehydrate → hydrate round-trips a chunk's entities;
- an entity recorded as destroyed is not re-created on hydrate;
- `PlayerInventorySaveable` restores slot-for-slot, including holes and unknown item IDs;
- `BackpackSaveable` restores both compartments independently.

## Scope

**In:** the persistence core, chunk-aware world store, player position / health /
inventory / backpack, dropped world items, deployed backpacks, game timer, save slots,
autosave, atomic writes, versioned migration, main-menu Continue and quicksave keys.

**Out (deliberately):** save-game thumbnails; cloud saves; per-client save files on a
dedicated server; mass-stamping `SaveableEntity` onto the 240 authored chunk scenes. The
authored-object path is implemented and tested, but only objects that actually need it get
the component — nothing in the game currently mutates authored world props.
