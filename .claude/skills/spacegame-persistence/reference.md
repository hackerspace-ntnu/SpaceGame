# Persistence Reference

Companion to `SKILL.md`. Everything here was read out of
`Assets/Game/Scripts/Core/Persistence/` — check the source before trusting a detail that matters.

## File map

```
Format/    (asmdef SpaceGame.Persistence — references: [], precompiled: Newtonsoft.Json.dll)
  ISaveable.cs           ISaveable, IDeferredSaveable
  IPersistentEntity.cs   the empty opt-in marker
  StateBag.cs            saverKey -> JObject; Set/TryGet<T>/TryGetRaw/Has/Remove/MergeFrom
  SaveDocument.cs        SaveDocument, SaveHeader, PlayerRecord, WorldRecord, EntityRecord, SceneKey
  SaveRef.cs             SaveRef, ISaveRefBinder, SaveRefBinding
  SaveSerializer.cs      the ONE JsonSerializer; ToJson / FromJson / TryReadHeader
  UnityJsonConverters.cs Vector2/Vector3/Vector2Int/Quaternion/Color converters
  SaveMigrator.cs        the version ladder; ISaveMigration
  Migrations/V1GlobalEntities.cs
  SaveFileStore.cs       atomic write via .tmp + File.Replace -> .bak; read falls back to .bak
  SaveSlots.cs           slot ids, Sanitize
  WorldIdentity.cs       world naming + AcceptsConfig guard

Runtime/   (Assembly-CSharp, namespace SpaceGame.Core.Persistence)
  SaveManager.cs         front door; autosave timer, quit save, global savers, deferred passes
  WorldSaveStore.cs      Hydrate/Dehydrate per scene; the identity-keyed record
  SaveableEntity.cs      prefabId/instanceId/authored/scope; SaveScope; LiveEntities
  SaveablePolicy.cs      NeedsSaving / Ensure / EnsureSpawned / EnsureScene
  SaveablePrefabRegistry.cs   prefabId -> prefab, from three sources
  SaveTeleport.cs        the only correct way to place a saved object
  SaveNetworking.cs      SpawnIfNetworked / DespawnAndDestroy / Destroy
  PlayerSaveService.cs   profile -> PlayerRecord; Bind/Unbind/CaptureAll; PlayerBound event
  PlayerSaveSync.cs      NetworkBehaviour: owner claims its profile on spawn
  PlayerSaveBinder.cs    offline/edit-time player binding (steps aside when Network.IsNetworked)
  PlayerProfile.cs       LocalId — per-instance GUID in PlayerPrefs
  SaveRefBinder.cs       live half of SaveRef
  WorldSession.cs        which world is being played + the staged document
  SaveHotkeys.cs         F5 quicksave / F9 quickload

Adapters/  (Assembly-CSharp) — the 13 ISaveable implementations
Editor/    SaveableWiring.cs (3 menu items), SaveWiringValidator.cs (1 menu item)

Tests
  Assets/Game/Editor/Tests/PersistenceProbe.cs        the harness
  Assets/Game/Editor/Tests/PrefabPersistenceTests.cs  project-wide sweeps + per-prefab tests
  Assets/Game/Editor/Tests/EntityPersistenceTests.cs
  Assets/Game/Editor/Tests/KinematicBodyRestoreTests.cs
  Assets/Game/Editor/Tests/DeathOnLoadTests.cs
  Assets/Game/Editor/Tests/WorldSaveRoundTripTests.cs
  Assets/Game/Editor/Tests/HeadlessTestRunner.cs      -> Temp/headless_tests.txt
  Assets/Game/Tests/EditMode/                         format-only tests (asmdef, no Unity scene)
  Assets/Game/Tests/Editor/                           WorldSaveStoreTests, SaveableEntityTests, codecs
```

## Record format

```
SaveDocument                                   header.version == SaveDocument.CurrentVersion (2)
├── header    version, savedAtUtc, playtimeSeconds, gameVersion, slotLabel,
│             worldName, worldConfigId          ← WorldStreamingConfig GUID; a mismatch refuses the load
├── players[] profileId, displayName, position, rotation, state: StateBag
└── world
    ├── global      StateBag                    ← PATH D savers
    ├── entities    { instanceId: EntityRecord } ← FLAT, not per scene
    └── destroyed   [ instanceId ]              ← tombstones for authored objects only

EntityRecord   prefabId, instanceId, scene, authored, position, rotation, scale, hasPose,
               state: StateBag  ({ saverKey: payload })
```

`EntityRecord.Scene` is **routing** (`"persistent"`, `"chunk:7,5"`, `"scene:Name"` via `SceneKey`),
re-stamped on every capture — it decides which scene load spawns a runtime object. It is never the
address of the record: v1 filed records per scene, and every creature that wandered into another
chunk came back at its authored position. `V1GlobalEntities` lifts those files to v2.

## Lifecycle / load order

```
SaveManager.Awake     WorldSession.Consume() -> new WorldSaveStore(doc.World) + PlayerSaveService(doc.Players)
                      installs SaveRefBinding.Active = new SaveRefBinder(...)
                      RestoreGlobals(doc)  (stages payloads for savers not yet registered)
                      subscribes WorldStreamer.OnChunkLoaded / OnChunkWillUnload / OnChunkUnloaded
SaveManager.Start     worldStore.Hydrate("persistent", this scene)   ← no streaming event fires for it
                      SaveNewWorld()  (writes the file immediately so a new world exists on disk)

per chunk load        Hydrate(sceneKey, scene):
                        1 SaveablePolicy.EnsureScene(scene)   ← wires anything unwired, derived identity
                        2 RemoveDestroyed(authored)
                        3 RestoreAuthored  → SaveTeleport.Move + entity.Restore(record.State)
                        4 SpawnEntities    → Instantiate, MoveGameObjectToScene, EnsureSpawned,
                                             EnsureRuntime, AdoptIdentity, Restore, SpawnIfNetworked
                        5 OnSceneHydrated  → deferred pass for that scene if the world pass already ran
per chunk unload      Dehydrate(sceneKey, scene) BEFORE the scene goes away, then ForgetLoaded

player spawns         PlayerSaveSync (owner) -> ClaimProfileServerRpc -> PlayerSaveService.Bind
                        SaveTeleport.Move (skipped for the host, already placed)
                        entity.Restore(record.State); entity.NotifyLoadComplete()
                        THEN raises PlayerBound  → SaveManager.RunWorldDeferredPass()

save                  playerService.CaptureAll() + worldStore.DehydrateLoaded() + persistent scene
                      + CaptureGlobals; serialize on the main thread, write on a background task
```

`OnLoadComplete` therefore fires: once per world pass, again on **every** `PlayerBound`, and again
for every scene hydrated after the first pass.

## JSON rules (`SaveSerializer`)

Non-negotiable — `SaveSerializer.Serializer` is the only serializer the system uses.

| Setting | Consequence |
|---|---|
| `Vector2/Vector3/Vector2Int/Quaternion/Color` converters | Without them Newtonsoft walks `Vector3.normalized` → `Vector3.normalized` → … until the stack ends. Any payload holding a Unity struct **must** be read with `state.ToObject<T>(SaveSerializer.Serializer)` |
| `DefaultContractResolver { IgnoreSerializableAttribute = true }` | Public **fields** are serialized. Payload DTOs are plain public-field structs |
| `MissingMemberHandling.Ignore` | A field this build no longer knows about is skipped — this is what let `isKinematic` be dropped from `RigidbodySaveable.State` with no migration |
| `ObjectCreationHandling.Replace` | A list in a DTO is replaced, not appended to its initializer |
| `TypeNameHandling.None` | No `$type` in files; `StateBag` defers typing to the reader |
| `NullValueHandling.Ignore` | `CaptureState()` returning null removes the key entirely (`StateBag.Set`) |
| `QuaternionConverter` | An all-zero quaternion reads back as `identity` — a zero rotation makes an object vanish from the renderer |

## Adding a field, and when a migration is needed

- **Adding or removing a saver, or adding/removing a field inside one payload: no migration.**
  Old files simply lack the key (`StateBag.TryGet` returns false) or carry an extra one
  (`MissingMemberHandling.Ignore`). This is the entire point of per-saver keys.
- **Bump `SaveDocument.CurrentVersion` and add an `ISaveMigration` in the same commit** only when a
  change to the *document shape* cannot be absorbed by a saver reading defensively.
- A migration operates on the raw `JObject`, never on the DTOs (the DTOs only describe the new shape,
  so a DTO-based migration silently sees new names on old data). Register it in
  `SaveMigrator.Migrations`; `FromVersion` produces `FromVersion + 1`.
- A file from a **newer** build is refused, not guessed at — loading it would half-populate a world
  that then gets saved back over the good file.

## SaveableEntity API worth knowing

```csharp
SaveableEntity.EnsureRuntime(go, prefabId)   // attach identity at a runtime spawn site
entity.AdoptIdentity(prefabId, instanceId)   // take over a record after being re-instantiated
entity.AdoptAuthoredIdentity(derivedId)      // for objects wired at runtime by EnsureScene
entity.DisownToExternal()                    // one-way: "another system owns my record"
entity.InvalidateSavers()                    // REQUIRED after AddComponent-ing a saver at runtime;
                                             // the saver list is cached on first Savers() call
SaveableEntity.LiveEntities                  // instanceId -> entity, for the object's whole LIFETIME
                                             // (Awake/OnDestroy, not OnEnable/OnDisable)
SaveManager.NotifyDestroyed(go)              // tombstone an authored object (via World.Despawn)
```

## SaveRef

```csharp
SaveRef.From(gameObject) / SaveRef.From(component)   // walks UP to the owning identity
ref.IsSet, ref.TryResolve(out GameObject target)
SaveRef.None
```

`{ kind: "player", id: <profileId> }` or `{ kind: "entity", id: <instanceId> }`. `TryDescribe`
checks players **first** — a player also carries a `SaveableEntity`, and describing it as an entity
files the ref under an instance id no load ever recreates. An `External`-scope object that is not a
bound player describes as nothing, on purpose. Resolution includes **disabled** objects: death here
is `SetActive(false)`, and `SaveableEntity` registers in `Awake`/`OnDestroy` rather than
`OnEnable`/`OnDisable` so corpses stay resolvable and are not re-instantiated as missing.

## Identity

| | `prefabId` | `instanceId` | On load |
|---|---|---|---|
| Authored (placed in a scene at edit time) | source prefab GUID | baked into the scene file by `OnValidate` | already present; the record is a delta |
| Runtime (spawned during play) | prefab GUID or item registry ID | GUID at spawn | instantiated from `prefabId` into `record.Scene` |
| External (`SaveScope.External`) | — | — | world store skips it; another system owns the record |

Unwired scene objects get a **derived** identity from `SaveableEntity.DeriveAuthoredId` — scene name
plus hierarchy path with sibling indices, FNV-1a hashed, prefixed `auto`. Stable across sessions,
orphaned by a rename or re-parent. Run the wiring tool to bake a real GUID instead.

Prefab-instance trap: assigning `instanceId` on a prefab **instance** and calling `SetDirty` leaves
the value equal to the prefab's, so Unity records no override and writes nothing to the scene file —
the identity is regenerated differently every time the scene opens.
`SaveableEntity.RecordAsPrefabOverrides` writes through `SerializedObject` to force real overrides.
Because overrides are stored as `propertyPath: instanceId` / `value: <guid>`, grepping a scene for
`instanceId: <hex>` finds **zero** even when the data is there — use `grep -A1 "propertyPath: instanceId"`.

## Inspecting a save file

```bash
python3 - <<'EOF'
import json, glob, os
root = os.path.expanduser("~/Library/Application Support/Hackerspace NTNU/SpaceGame/Saves")
f = max(glob.glob(root + "/*.json"), key=os.path.getmtime)
d = json.load(open(f))
print(os.path.basename(f), "v", d["header"]["version"], d["header"].get("slotLabel"))
for p in d.get("players", []):
    print("  player", p["profileId"][:8], "keys=", sorted((p.get("state") or {}).get("entries", {}).keys()))
for eid, r in d["world"]["entities"].items():
    kind = "authored" if r.get("authored") else "runtime "
    keys = sorted((r.get("state") or {}).get("entries", {}).keys())
    print(f"  {eid[:8]} {kind} {r.get('scene',''):16s} keys={keys}")
print("  destroyed:", len(d["world"].get("destroyed", [])))
EOF
```

`keys=[]` on an entity means it is wired and saving nothing. A record count that grows across two
identical reloads means something re-spawns instead of adopting its recorded identity.

`slotLabel` matters when diagnosing: a file labelled with the world name came from a live
`SaveOnExit`; one labelled `Autosave` came from the timer or from `OnApplicationQuit`, which runs
*after* netcode teardown has already made bodies kinematic.

## Menu items

- `Tools ▸ Save System ▸ Wire Saveable Prefabs`
- `Tools ▸ Save System ▸ Wire Saveable Scene Objects`
- `Tools ▸ Save System ▸ Wire Saveable Chunk Scenes`
- `Tools ▸ Save System ▸ Validate Save Wiring`

All idempotent; the wiring ones must not be run in Play mode (prefab edits are discarded).
