---
name: spacegame-persistence
description: Use when something must survive save/quit/load in SpaceGame — state resets to prefab defaults after loading a world, a creature or vehicle reappears at its authored position, a runtime-spawned object is missing on load with "No prefab registered for id", an entity duplicates on every reload, a saver's key is absent from the save JSON, Newtonsoft stack-overflows on a Vector3 or Quaternion, a loaded player cannot move, or save support is being added to a new component, prefab, spawned entity, or player-scoped value.
---

# SpaceGame Persistence

> **Design check:** when deciding *what* the game should remember, or how saving is surfaced to the
> player, read the `ARCH`, `UX` and `PROG` principles in
> `docs/game-development-constitution/INDEX.md` and cite their IDs.

Persistence in this project fails **silently**: nothing throws, no test goes red, and the player's
session is simply gone. The core principle is that a saved object is addressed by **identity, never
by scene** (`WorldStreamer` migrates entities between chunks, so scene membership is where a thing
is right now, not what it is), and each `ISaveable` owns exactly one key inside its entity's
`StateBag`, which nothing else may write.

Code lives in `Assets/Game/Scripts/Core/Persistence/` (`Format/` = asmdef `SpaceGame.Persistence`,
zero references; `Runtime/`, `Adapters/`, `Editor/` = Assembly-CSharp).
Full system reference: `docs/AI/systems/Persistence.md`.
Record shapes, adapter catalog, JSON rules, migrations: `reference.md` beside this file.

## When to Use

Adding a new component whose state must outlive a reload; adding a new prefab that is spawned during
play; making a player-scoped value persist; session-wide flags and timers; or diagnosing any of the
symptoms in the description.

## Decision Guide — which path does this thing need

```
Who OWNS this state?
├─ The PLAYER (inventory, backpack contents, suit colour, per-profile progress)
│     → PATH C · player-scoped, keyed by profile
├─ NOBODY — it is session-wide (timer, world flags, quest state)
│     → PATH D · global saver
├─ Another SYSTEM that recreates the object itself every session
│     (BackpackController builds one pack per player in Awake; NpcWorldSim rebuilds
│      caravan members from one group record)
│     → the object must NOT get a world record of its own: SaveScope.External on the
│       prefab, or SaveableEntity.DisownToExternal() at runtime. Its state belongs to
│       the owning system's saver — usually PATH C or PATH D. Giving it its own record
│       instead produces a second, competing copy AND a duplicate object on every load.
└─ The WORLD (creature, vehicle, mount, prop, dropped item)
      │
      ├─ Does the object EXIST on load without help?
      │    authored in a chunk/persistent scene → yes, the scene file recreates it
      │    spawned during play                  → no, it must be re-instantiated
      │
      ├─ authored  → PATH A · write a saver, that is all
      └─ spawned   → PATH B · saver + be resolvable by SaveablePrefabRegistry

Does restoring the state need something that does not exist yet — another object (a
rider, a target, an owner), ground to stand on, a chunk still streaming in, or a
NetworkObject not yet spawned?
  → additionally implement IDeferredSaveable and do the work in OnLoadComplete.
    Never resolve a SaveRef in RestoreState.
```

**Before any path: check whether a saver already owns this.** Grep `Persistence/Adapters/` for the
component, and read the table below. Extending an existing saver's `State` struct with a new public
field is the cheapest correct answer and needs **no migration** — old files lack the field and read
back as its default (`MissingMemberHandling.Ignore`; see `reference.md`).

## PATH A — component state on a world object

1. Confirm the object is opted in: `SaveablePolicy.NeedsSaving` says yes for `IPersistentEntity`,
   `HealthComponent`, `PickupableItem`, `NavMeshAgent`, or a **non-kinematic** `Rigidbody`. A new
   locomotion base or vehicle root matching none of those must implement the empty marker
   `SpaceGame.Persistence.IPersistentEntity` (add `SpaceGame.Persistence` to its asmdef references).
2. Write the saver — see the example below. Put it in `Persistence/Adapters/`, namespace
   `SpaceGame.Core.Persistence`, not in the feature's own asmdef (that drags in Newtonsoft).
3. Auto-attach it: add a clause to `SaveablePolicy.Ensure` keyed off the component that implies it.
4. Run `Tools ▸ Save System ▸ Wire Saveable Prefabs`, then verify.

## PATH B — an entity spawned at runtime

1. Do PATH A first.
2. Spawn through `GameServices.World.Spawn(prefab, pos, rot)` (server-only) — it calls
   `SaveablePolicy.EnsureSpawned`. Destroy through `GameServices.World.Despawn`.
3. Make `prefabId` resolvable, or the record is captured and silently dropped on load with
   `[Save] No prefab registered for id '...'`. Satisfy **one** of: it is an `InventoryItem.itemPrefab`;
   it is a registered NetworkManager prefab carrying a `SaveableEntity`; it sits under a
   `Resources/Saveable/` folder. **Move the prefab, never make a variant** — `OnValidate` stamps
   `prefabId` from the asset GUID, so a variant stamps its own and disagrees with every record
   already written. Registering with NetworkManager is
   `Tools/SpaceGame/Multiplayer/Sync Network Prefabs` (see `spacegame-multiplayer`).
4. Runtime spawn sites that know a better key call `SaveableEntity.EnsureRuntime(obj, item.ID)`.

## PATH C — player-scoped state

The player is `SaveScope.External`: the world store steps over it and `PlayerSaveService` owns the
record, keyed by `PlayerProfile.LocalId`. Put the saver on **`PlayerCharacter.prefab`** — the
networked player is a *variant* of it, so grepping `PlayerCharacterNetworked.prefab` for a script
GUID finds nothing that is nevertheless there.

Chunk streaming never touches this record: it is captured by `PlayerSaveService.CaptureAll` on every
save and by `Unbind` on disconnect, so player-scoped state survives regardless of which chunks were
loaded or where anything was standing. That makes PATH C the right home for anything a per-player
controller owns — including an object it detached into the world — provided the restore that
re-places that object is deferred to `OnLoadComplete`, by which time the ground exists.

Savers collected for the player stop at any nested `SaveableEntity`, and `SaveablePolicy` skips
anything carrying `PlayerSaveBinder` or `PlayerSaveSync`, so a player-owned child never gets a
second record by accident — unless someone adds a `SaveableEntity` to it.

## PATH D — global state

`ISaveable` + `SaveManager.RegisterGlobalSaver(this)` in `OnEnable` /
`UnregisterGlobalSaver(this)` in `OnDisable`. Registration order does not matter — a load stages
global payloads and applies each when its saver appears. Pattern: `GameStateSaveable.cs`.

## Complete example — a new saver with a cross-object reference

```csharp
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>Persists which creature holds a grudge against whom.</summary>
    [RequireComponent(typeof(ProvocationModule))]
    public class ProvocationSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "provocation";           // written into save files — never rename

        private ProvocationModule module;

        // Lazy-resolved, NOT cached in Awake: EditMode tests never run Awake, so a saver that
        // caches there cannot be round-trip tested by PersistenceProbe.
        private ProvocationModule Module =>
            module != null ? module : module = GetComponent<ProvocationModule>();

        public string SaveKey => Key;

        public struct State                                // a plain struct, never the component
        {
            public SaveRef aggressor;
        }

        private SaveRef pending;

        // null stores nothing — the right answer for a component at its defaults.
        public object CaptureState() => Module == null || !Module.IsProvoked
            ? null
            : new State { aggressor = SaveRef.From(Module.Aggressor) };

        public void RestoreState(JObject state)
        {
            // Through SaveSerializer.Serializer, always. It carries the Vector3/Quaternion/Color
            // converters; reading a Unity struct without them recurses through its own
            // `normalized`/`eulerAngles` properties into a StackOverflowException.
            pending = state == null
                ? SaveRef.None
                : state.ToObject<State>(SaveSerializer.Serializer).aggressor;
        }

        public void OnLoadComplete()                       // runs MANY times — must be idempotent
        {
            if (Module == null || !pending.IsSet) return;

            // Kept on failure: the aggressor may be a player who has not rejoined yet, and this
            // pass fires again on every PlayerBound and every late chunk hydrate.
            if (!pending.TryResolve(out GameObject aggressor)) return;

            pending = SaveRef.None;                        // consumed only on success
            Module.Provoke(aggressor.transform);
        }
    }
}
```

Then in `SaveablePolicy.Ensure`:

```csharp
if (go.GetComponent<ProvocationModule>() != null && go.GetComponent<ProvocationSaveable>() == null)
{
    go.AddComponent<ProvocationSaveable>();
    parts.Add(nameof(ProvocationSaveable));
}
```

## Existing savers — reuse before writing

| Saver | Key | Auto-added when the object has |
|---|---|---|
| `TransformSaveable` | `transform` | always |
| `RigidbodySaveable` | `rigidbody` | non-kinematic `Rigidbody` (velocity only — never `isKinematic`) |
| `HealthSaveable` | `health` | `HealthComponent` (health 0 **is** the dead state; no `alive` field) |
| `MountSaveable` | `mount` | `MountModule` (deferred; remounts via `MountNetworkSync.ServerMount`) |
| `AgentStateSaveable` | `agent` | `AgentTargeting` (deferred; target, memory, patrol index) |
| `EntityInventorySaveable` | `entityInventory` | `EntityInventoryComponent` |
| `ArticulatedPartsSaveable` | `parts` | any `ArticulatedPart` below it (keyed by hierarchy path) |
| `DuneFoilSaveable` | `dunefoil` | `SailRig` |
| `OrnithopterSaveable` | `ornithopter` | `OrnithopterFlightMotor` (deferred; relaunches in-flight craft) |
| `PlayerInventorySaveable` / `BackpackSaveable` | `inventory` / `backpack` | player prefab (PATH C) |
| `NpcWorldSaveable` | `npcworld` | `NpcWorldSim` — one record per group, not per member |
| `GameStateSaveable` | `gameState` | registered by hand (PATH D) |

## Verification Recipe

A persistence change that has not been round-tripped does not work. Do all of these.

1. **Wire and validate.** `Tools ▸ Save System ▸ Wire Saveable Prefabs` (and `Wire Saveable Scene
   Objects` / `Wire Saveable Chunk Scenes` if scenes changed), then `Tools ▸ Save System ▸ Validate
   Save Wiring` — zero errors.
2. **EditMode fixpoint test.** Three lines in `Assets/Game/Editor/Tests/PrefabPersistenceTests.cs`:
   ```csharp
   [Test]
   public void Golem_StaysWounded() =>
       PersistenceProbe.For("Assets/Game/Prefabs/agents/creatures/Golem.prefab")
           .Mutate(go => go.GetComponent<HealthComponent>().Damage(7))
           .AssertSurvivesRoundTrip();
   ```
   It captures, serializes to **real JSON text**, restores onto a **fresh** instance and re-captures;
   `Mutate` must put the object into a state a *player* could put it in, or the test passes
   vacuously. `AssertWiredCorrectly()` is the structural counterpart. `Excluding<T>()` is only for
   savers that genuinely cannot run without `Awake` or a runtime registry
   (`EntityInventorySaveable` needs the item registry) — never to silence a failing round trip.
   A saver holding a `SaveRef` round-trips as `none` unless the test installs an `ISaveRefBinder`
   into `SaveRefBinding.Active` (pattern: `Assets/Game/Tests/EditMode/SaveRefTests.cs`).
   Run headlessly: `HeadlessTestRunner.RunEditModeDeferred("PrefabPersistenceTests")`, then read
   `Temp/headless_tests.txt`. The two project-wide sweeps
   (`EveryWorldEntityPrefabIsWiredForSaving`, `EveryWiredPrefabHasTheSaversItsComponentsImply`)
   cover every new prefab automatically and go red until the wiring tool has been run.
3. **Play-mode round trip.** Main menu → New/Load World (a world scene opened *directly* in the
   editor has no `WorldSession`, so every save is refused with `Save ignored: no world is active`).
   Change the thing → `F5` quicksave → `F9` quickload → the change must be there. Then quit the app
   entirely and re-enter through Load World. (`SaveManager` and `SaveHotkeys` are placed only in
   `Assets/Game/Scenes/world/persistentScene.unity`; a world scene without them saves nothing.)
4. **Read the file.** `~/Library/Application Support/Hackerspace NTNU/SpaceGame/Saves/<World>.json`.
   The saver's key must appear under the expected entity — see `reference.md` for an inspection
   script. `keys=[]` means wired but saving nothing.
5. **Reload twice and count entity records.** Duplication only appears on the second cycle.
6. **Read the console on load.** `No prefab registered for id` and `share instance id` are both
   silent data loss.
7. **Host + client** if the thing is networked — an unregistered network prefab fails *only* on
   clients.

## Common Mistakes

| Mistake | Symptom | Fix |
|---|---|---|
| Reading a payload by probing `JObject` tokens | `StackOverflowException` inside Newtonsoft on `Vector3.normalized` / `Quaternion.eulerAngles` | `state.ToObject<State>(SaveSerializer.Serializer)` |
| Restoring pose with `transform.position` | Object snaps back within a frame (`Physics.autoSyncTransforms` is off; the player body is interpolated) | Don't — the `EntityRecord` pose is applied for you via `SaveTeleport.Move`, which warps `NavMeshAgent`s, cycles `CharacterController`s and writes every child `Rigidbody.position` |
| Persisting `isKinematic` | Loaded player cannot walk, jump or fall while `PlayerLook` still works | Never save engine-owned flags. The quit-time `Autosave` captures the body *after* netcode teardown made it kinematic; `RigidbodySaveable.CaptureState` returns null for a kinematic body for exactly this reason |
| Resolving a `SaveRef` in `RestoreState` | Rider never re-seated; second player's mount permanently empty | Resolve in `IDeferredSaveable.OnLoadComplete`; consume only on success |
| Treating `OnLoadComplete` as once-only | State re-applied over a world that moved on, or a late chunk never restored | Idempotent. Consume immediately for self-contained state (flight, patrol); consume on success for references |
| Opting in by sniffing for a non-kinematic `Rigidbody` | Every mount, walker and vehicle absent from the save file | Implement `IPersistentEntity` — legged rigs are kinematic and `DuneFoil` has no root `Rigidbody` |
| `Instantiate` / `Destroy` for world objects | Restored object piles up a duplicate per reload; looted authored crate refills itself | `GameServices.World.Spawn` / `.Despawn` (the latter calls `SaveManager.NotifyDestroyed`, which tombstones authored objects) |
| Renaming a `SaveKey`, or renaming/re-parenting an unwired scene object | Every record under the old spelling orphaned, silently | Keys are permanent. Bake identities with the wiring tool rather than relying on the derived hierarchy-path fallback |
| Assuming a saver on a child is captured by the parent | State lands in the child's own record | Collection stops at any nested `SaveableEntity` |
| Giving a system-owned object its own `SaveableEntity` | Two competing copies of the same state, and a lifeless duplicate object standing beside the real one on every load | `SaveScope.External` on the prefab, or `SaveableEntity.DisownToExternal()` at runtime; store the state on the owning system's saver |
| Adding a saver with `AddComponent` at runtime | New saver never captured — the entity cached its saver list on first use | `entity.InvalidateSavers()` after the add (see `PlayerSaveService.EnsureMomentumSaver`) |
| Restoring 0 HP without guarding | Loot re-dropped and death reaction replayed on every load | `HealthComponent.IsRestoring` — checked by `HealthReactionModule` and `EntityLootTable` |

## Related

- `spacegame-multiplayer` — authority, RPCs, `NetMessaging`, network prefab registration. This skill
  touches netcode only at the seam: saving is server-only (`Network.Server`), restored objects go
  through `SaveNetworking.SpawnIfNetworked`, and player placement goes through `PlayerSaveService`
  because the player transform is owner-authoritative.
- `docs/AI/systems/Persistence.md` — the source-verified system reference. Narrative version for humans: `docs/Human/08-saving-and-continuity.md`.
- `reference.md` — record format, JSON rules, migrations, file map, save-file inspection script.
