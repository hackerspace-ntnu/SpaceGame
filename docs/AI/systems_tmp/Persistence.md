# Making Something Persistent

How to make an object survive save/load in SpaceGame. Written for both humans and Claude agents.

**The rule this system exists to enforce: nothing that moves or changes may be lost.** Every agent,
every vehicle, every mount, every dropped item, every mutable state. If a player can change it, a
reload must show the change.

**The failure mode you are guarding against is silent.** A missing saver throws nothing, logs
nothing and breaks no test. The game runs perfectly and the player's session is simply gone. That is
why this document is prescriptive and why the validator (`Tools ▸ Save System ▸ Validate Save
Wiring`) exists — there is no compiler for asset wiring.

---

## 1. The 30-second version

| You have | You do |
|---|---|
| A new agent, creature, mount or vehicle prefab | Nothing. It is opted in automatically — see §2. Then run `Tools ▸ Save System ▸ Wire Saveable Prefabs` to bake a stable identity. Tests: also nothing, see §9. |
| A new component whose state must survive | Implement `ISaveable` on it. §4. |
| State that needs another entity (a rider, a target) | `ISaveable` + `SaveRef` + `IDeferredSaveable`. §5. |
| A prefab spawned at runtime, not an inventory item | Put it under a `Resources/Saveable` folder. §6. |
| A new locomotion base class or vehicle root | Implement `IPersistentEntity`. §2. |
| Session-wide state belonging to no object | `ISaveable` + `SaveManager.RegisterGlobalSaver`. §7. |

Then, always: **§10, the verification checklist.** A persistence change you have not round-tripped is
a persistence change that does not work.

---

## 2. Opting an object in

An object is saved when it has a `SaveableEntity`. You almost never add one by hand — two mechanisms
add it for you, and both consult the *same* rule, `SaveablePolicy.NeedsSaving`:

- **`Tools ▸ Save System ▸ Wire Saveable Prefabs`** (edit time) — bakes a GUID identity into the
  prefab or scene file. This is the good path: a baked identity survives the object being renamed or
  re-parented.
- **`WorldSaveStore.Hydrate` → `SaveablePolicy.EnsureScene`** (runtime, every scene load) — wires
  anything that qualified but was never wired, with an identity **derived from its scene name and
  hierarchy path**. Stable across sessions, *not* across scene edits: rename the object and its
  record is orphaned.

So the runtime pass means forgetting costs nothing today and costs you a record the day someone
renames the object. Run the editor tool.

### What `NeedsSaving` says yes to

```
NOT saved   any component in the Transient blacklist  (AgentProjectile, TurretProjectile,
                                                        Projectile, RocketLauncherTurret)
NOT saved   the player  (PlayerSaveBinder / PlayerSaveSync own it — see §8)

saved       IPersistentEntity        ← the marker: "I am part of the mutable world"
saved       HealthComponent          ← has damage to remember
saved       PickupableItem           ← a dropped item
saved       NavMeshAgent             ← a wanderer
saved       non-kinematic Rigidbody  ← physics can move it
```

**`IPersistentEntity` is the one you extend.** It is an empty marker interface living in
`SpaceGame.Persistence` — an assembly with zero references, so implementing it couples you to
nothing. It is already implemented by:

| Implementor | Covers |
|---|---|
| `AgentController` | every agent and every AI-capable vehicle |
| `MountModule` | every mount |
| `SceneTracked` | anything explicitly declared a world entity |
| `LeggedLocomotion` | every legged machine, through inheritance |
| `DuneFoilLocomotion` | the sailer, which has no other qualifying component |

> **Why an interface and not a list of type names.** The blacklist above matches by *name* because
> `SaveablePolicy` cannot reference the weapon and item assemblies. Name matching cannot see base
> classes: `OstrichLocomotion` does not match `"LeggedLocomotion"`, so a name list would need every
> subclass spelled out and would break silently on the first rename — and this repo has already lost
> data to exactly that class of bug. An interface is inherited, compile-checked, and survives
> renaming.

If you write a new locomotion base or a vehicle root that has no `AgentController`, implement
`IPersistentEntity` on it. If your asmdef is separate, add `SpaceGame.Persistence` to its
`references`. That is the whole cost.

### The three populations

`SaveableEntity` carries two identities and one scope, and the combination decides how the object
comes back.

| | `prefabId` | `instanceId` | On load |
|---|---|---|---|
| **Authored** — placed in a scene at edit time | source prefab GUID | baked into the scene file | Already there. Its record is a **delta** applied on top. Re-creating it would duplicate it. |
| **Runtime** — spawned during play | the prefab it came from | assigned at spawn | **Instantiated** from `prefabId` into its recorded scene, then restored. |
| **External** — `SaveScope.External` | — | — | The world store *skips it entirely*. Another system owns the record. Only the player uses this. |

Getting `SaveScope` wrong on a player-like object gives every load a lifeless duplicate of it
standing beside the real one.

---

## 3. Where records live, and why it is keyed the way it is

```
SaveDocument
├── header    version, playtime, world name, world config GUID
├── players   keyed by PROFILE id      → PlayerSaveService
└── world                             → WorldSaveStore
    ├── global     state belonging to no object
    ├── entities   Dictionary<instanceId, EntityRecord>   ← flat, NOT per-scene
    └── destroyed  List<instanceId>                       ← tombstones for authored objects
```

**Records are keyed by identity, never by scene.** This is load-bearing and was learned the hard
way. `WorldStreamer.UpdateSceneMembership` moves every `SceneTracked` entity into whichever chunk it
has wandered into, so the scene an object is in is a property of *where it is right now*, not of the
object. When records were filed per scene, a creature captured in the chunk it walked to was looked
up in that chunk on load — while the scene file had put it back where it was authored. The lookup
missed and every creature in the world reappeared at its starting position. `EntityRecord.Scene`
still exists, as *routing* (which scene load spawns a runtime object), re-stamped on every capture.

The store keeps one invariant:

> An object's state is **either** live in a loaded scene **or** recorded in the store — never
> neither, and never both where the two could drift.

It maintains that by hooking chunk streaming: `Dehydrate` captures a chunk before it unloads,
`Hydrate` puts the record back after it loads. The persistent scene gets no streaming events, so
`SaveManager` hydrates and dehydrates it by hand.

---

## 4. Writing a saver

One component, one `ISaveable`, one key, and nothing else may write to that key. That is what lets
savers be added and reshaped without a format migration.

```csharp
[RequireComponent(typeof(SailRig))]
public class SailTrimSaveable : MonoBehaviour, ISaveable
{
    public string SaveKey => "sail";          // stable forever; see the warning below

    private SailRig rig;
    private SailRig Rig => rig != null ? rig : rig = GetComponent<SailRig>();

    // A plain struct, not the component. Serialize the state, never the object.
    public struct State
    {
        public float mainSheet;
        public float mastCant;
    }

    public object CaptureState() => Rig == null
        ? null                                 // null stores nothing — correct for "at defaults"
        : new State { mainSheet = Rig.MainSheet, mastCant = Rig.MastCant };

    public void RestoreState(JObject state)
    {
        if (Rig == null || state == null) return;

        // Read defensively, field by field. `state` may come from an older build that never
        // wrote half of these, and anything it does not mention must be left alone.
        if (state["mainSheet"] is { Type: JTokenType.Float } sheet)
            Rig.SetMainSheet(sheet.Value<float>());
    }
}
```

Rules that are not negotiable:

1. **`SaveKey` is written into save files.** Renaming it orphans every record stored under the old
   spelling. Pick a short lower-case noun and never change it.
2. **Read `JObject` defensively.** Never assume a field is present or of the type you expect. A
   missing field is the normal way an old save meets new code, and it must not throw — a throwing
   saver is caught and logged, but its state is lost.
3. **Do not restore the pose.** `WorldSaveStore` stores position/rotation/scale on the record itself
   and applies it *before* your `RestoreState`, via `SaveTeleport` — which knows to `Warp` a
   `NavMeshAgent`, cycle a `CharacterController`, and zero a `Rigidbody`. Assigning
   `transform.position` yourself will be silently undone within a frame by whatever drives the
   object.
4. **Never touch another entity's state.** If you need one, you need §5.
5. **Capture must be pure.** It runs mid-frame during a save; do not spawn, destroy or mutate.
6. **Deserialize through `SaveSerializer.Serializer`.** `state.ToObject<State>(SaveSerializer.Serializer)`
   is the house pattern, and it is not optional for anything holding a `Vector3`, `Quaternion`,
   `Color` or `SaveRef`: the Unity converters live on that serializer, and a `Vector3` read without
   them recurses through its own properties into a stack overflow. It also gives you graceful
   handling of missing fields for free, which is most of rule 2.

### Which savers already exist

Reuse before writing. `SaveablePolicy.Ensure` adds the first four automatically:

| Saver | Key | Owns | Added automatically when the object has |
|---|---|---|---|
| `TransformSaveable` | `transform` | pose (the record's pose is authoritative; this is belt-and-braces) | always |
| `RigidbodySaveable` | `rigidbody` | linear + angular velocity | a non-kinematic `Rigidbody` |
| `HealthSaveable` | `health` | current HP — **which is also how death persists**, see below | `HealthComponent` |
| `MountSaveable` | `mount` | who was riding — deferred remount | `MountModule` |
| `AgentStateSaveable` | `agent` | combat target, last-known position, aggro, patrol progress | `AgentTargeting` |
| `EntityInventorySaveable` | `entityInventory` | an NPC's slots (and so what it drops) | `EntityInventoryComponent` |
| `ArticulatedPartsSaveable` | `parts` | every hatch, ramp and canopy, keyed by path | any `ArticulatedPart` below it |
| `DuneFoilSaveable` | `dunefoil` | sail sheet, cant and hoist; mooring | `SailRig` |
| `OrnithopterSaveable` | `ornithopter` | whether it was airborne, and how fast | `OrnithopterFlightMotor` |
| `PlayerInventorySaveable` | `inventory` | the player's inventory and hotbar | (player prefab) |
| `BackpackSaveable` | `backpack` | a deployed backpack's contents | (backpack prefab) |
| `GameStateSaveable` | — | global, registered by hand | — |

**Death is not a separate flag.** `Alive` means `currentHealth > 0`, so a corpse is a record with
health 0 and nothing more is needed — a second flag could only ever disagree with the first.
`HealthComponent.RestoreHealth` announces `OnDeath` whenever the restored value is lethal, and sets
`IsRestoring` while it does. Listeners that merely *observe* death ignore the flag; listeners that
*act* on it must check it. Two do: `HealthReactionModule` (which would replay the death sound and
restart the despawn timer) and `EntityLootTable` (which would drop the loot table again, every load).

A saver placed on a **child** of the entity is collected too — but collection stops at any nested
`SaveableEntity`. That cut-off is why a player carrying a backpack does not save the backpack's
contents twice.

---

## 5. Referring to another entity (riders, targets)

A record cannot hold a `Transform`. Use `SaveRef`, which serializes *which* thing rather than the
thing:

```csharp
SaveRef.From(transform)      // → { kind: "player", id: "<profileId>" }
                             //   or { kind: "entity", id: "<instanceId>" }
someRef.TryResolve(out GameObject target)
```

**Reading and writing a `SaveRef` goes through `SaveSerializer.Serializer`** like any other payload —
in practice that means `state.ToObject<State>(SaveSerializer.Serializer)`, never hand-probing tokens.
See rule 6 in §4.

**Resolve in the deferred pass, never in `RestoreState`.** When a mount is restored the rider does
not exist yet — Netcode spawns players at a time this system does not control, and other chunks may
still be streaming in. Implement `IDeferredSaveable` and do the work in `OnLoadComplete`, which runs
once the world is hydrated and players are bound:

```csharp
public class MountSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
{
    public string SaveKey => "mount";

    private SaveRef pendingRider;                 // stashed by RestoreState

    public void RestoreState(JObject state) { /* read pendingRider, apply nothing yet */ }

    public void OnLoadComplete()                  // rider exists by now
    {
        if (!pendingRider.TryResolve(out GameObject rider)) return;
        mount.TryMount(rider.GetComponent<Interactor>(), null);
    }
}
```

**`OnLoadComplete` runs more than once, deliberately.** It fires once for the world at load, again
each time a player binds, and again for any scene that hydrates afterwards. In multiplayer players
arrive one at a time, so a single pass would resolve the first player's mount and permanently give up
on the second's. That gives every deferred saver one decision to make:

- **Consume on the first pass** when the state does not depend on another party — a flight to resume,
  a patrol index. Re-applying it later would overwrite a world that has moved on.
- **Consume only on success** when it names someone who may still be arriving — a rider. Keep the ref
  and let the next pass try again.

Either way the saver must be safe to call twice. If you find yourself wanting a guaranteed ordering
between two deferred savers, you want a guarantee the system does not give — make the operation
idempotent instead.

---

## 6. Objects spawned at runtime

Spawn through `GameServices.World.Spawn` and you are mostly done: it runs
`SaveablePolicy.EnsureSpawned`, which applies **the same opt-in rule a scene load applies** — so a
vehicle deployed at runtime gets its identity and its savers exactly as an authored one does. Skip
that service and the object is saved only as well as its prefab happened to be authored.

```csharp
GameObject obj = GameServices.World.Spawn(prefab, position, rotation);   // server-only, see §8

// Only when the spawn site knows a better id than the prefab's own — a dropped item, whose record
// is keyed by the ITEM's registry ID rather than by the pickup prefab's GUID.
SaveableEntity.EnsureRuntime(obj, item.ID);
```

### Being resolvable on load

A runtime object is **instantiated from `prefabId` on load**, so the store has to turn that id back
into a prefab. `SaveablePrefabRegistry` fills itself from three sources, and you almost certainly
already satisfy one:

1. **Every `InventoryItem`'s `itemPrefab`**, under the item's own registry ID. Dropped items need no
   work at all.
2. **Every registered network prefab** that carries a `SaveableEntity`, under that entity's
   `prefabId`. This is not a shortcut — a world object spawnable during play *must* be a registered
   network prefab or it exists on the host alone (§8), so "the server can spawn it" and "the save
   system can rebuild it" describe the same population.
3. **Every prefab under a `Resources/Saveable` folder.** The fallback for something spawnable but not
   networked.

Miss all three and the object is captured perfectly and silently discarded on load, with one console
warning: `No prefab registered for id '...'`. That is exactly what happened to the ornithopter the
wing pack deploys — its record was in every save file and nothing could rebuild it.

> **Moving a prefab into `Resources/Saveable`: move it, never make a variant.** `OnValidate` stamps
> `prefabId` from the asset path, so a variant stamps the *variant's* GUID while the spawner still
> instantiates the base — and the registry key then disagrees with every record already written.
> `AssetDatabase.MoveAsset` keeps the GUID.

### Destroying things

- **Runtime object** — just destroy it. It stops being captured and drops out of the record.
- **Authored object** — the scene file re-creates it on every load, so it needs an explicit
  tombstone. Route the destruction through `GameServices.World.Despawn`, which calls
  `SaveManager.NotifyDestroyed` before the object is gone. Skip this and a looted crate refills
  itself every time the chunk streams back in.

---

## 7. State that belongs to no object

Timers, world flags, quest progress:

```csharp
private void OnEnable()  => SaveManager.RegisterGlobalSaver(this);
private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);
```

Registration order does not matter. A load stages global payloads and applies each one when its
saver registers, so a saver that wakes up after `SaveManager` is served identically to one that woke
up before.

---

## 8. Netcode and authority

**The server owns all world state.** The game is always hosted — singleplayer runs as a host — so
every save and load path is guarded by `Network.Server`. A client that saved would write out its own
replicated approximation of a world it does not own.

| Do | Not |
|---|---|
| `GameServices.World.Spawn` / `.Despawn` | `Instantiate` / `Destroy` for networked world objects |
| Let `SaveNetworking.SpawnIfNetworked` spawn restored objects | Call `NetworkObject.Spawn` yourself in a saver |
| Guard save/load work with `Network.Server` | Assume a client can restore anything |

Two traps specific to this project:

- **The player transform is owner-authoritative.** A server-side teleport of a player is overwritten
  by the owner's next update. Player placement goes through `PlayerSaveService`, not through the
  world store.
- **Every restored networked prefab must be registered in `NetworkManager`'s prefab list.** An
  unregistered prefab fails **only on clients**, so solo playtesting will never find it. Run
  `Sync Network Prefabs` and see `docs/architecture/` on network prefab tiers.

---

## 9. Testing a prefab

**Adding a prefab usually needs no test.** Two sweeps in
`Assets/Game/Editor/Tests/PrefabPersistenceTests.cs` already cover every prefab in the project the
moment it exists:

| Test | Fails when |
|---|---|
| `EveryWorldEntityPrefabIsWiredForSaving` | a prefab qualifies as a world entity but has no `SaveableEntity` |
| `EveryWiredPrefabHasTheSaversItsComponentsImply` | a wired prefab gained a `MountModule` (or similar) and nobody re-ran the wiring tool |

Neither takes a list of prefabs, and neither takes a list of savers — the first discovers subjects
through `SaveablePolicy.NeedsSaving`, and the second derives its expectations by running the real
`SaveablePolicy.Ensure` on a throwaway copy and asking what it had to add. Add a saver to the policy
and every prefab starts being checked for it with no test edited.

**Write a per-prefab test when the prefab has state a sweep cannot know about** — a rig to trim, a
hatch to open, a fuel level. `PersistenceProbe` makes that three lines:

```csharp
[Test]
public void DuneFoil_KeepsItsRigTrimmed() =>
    PersistenceProbe.For("Assets/Game/Prefabs/Agents/Vehicles/Ground/DuneFoil.prefab")
        .Mutate(go => go.GetComponent<SailRig>().MainSail.SetSheet(0.35f))
        .AssertSurvivesRoundTrip();
```

`Mutate` puts the instance into a state a *player* could put it in. `AssertSurvivesRoundTrip` then
captures it, serializes to **real JSON text**, restores onto a **fresh instance**, captures again, and
requires the two to agree — naming the offending save key when they don't. Both of those details
matter: text is where the Unity converters get exercised, and a fresh instance is what stops a saver
whose `RestoreState` is empty from passing.

The other assertion is structural:

```csharp
[Test]
public void Ostrich_IsWiredForSaving() =>
    PersistenceProbe.For(".../Ostrich.prefab").AssertWiredCorrectly();
```

**`Excluding<T>()` is for savers that genuinely cannot run outside play mode** — ones that depend on
state built in `Awake`, or on a registry only filled at runtime (`EntityInventorySaveable` needs the
item registry). Excluding a saver because its round trip *fails* is hiding the bug this harness exists
to find.

> **EditMode does not run `Awake`.** That is why every saver here lazy-resolves its component
> (`x != null ? x : x = GetComponent<T>()`). Follow that pattern in a new saver and it is testable;
> cache the component in `Awake` instead and it is not.

---

## 10. Verification checklist

Do not report a persistence change as working without this. "It compiles" and "the save file grew"
are not evidence.

1. **Round-trip in play mode.** New world → change the thing (ride the mount, wound the creature,
   drop the item, trim the sail) → return to the main menu → re-enter the world. The change must be
   there.
2. **Read the file.** It is JSON on purpose.
   ```bash
   python3 - <<'EOF'
   import json, glob, os
   root = os.path.expanduser("~/Library/Application Support/Hackerspace NTNU/SpaceGame/Saves")
   f = max(glob.glob(root + "/*.json"), key=os.path.getmtime)
   d = json.load(open(f)); print(os.path.basename(f), "v", d["header"]["version"])
   for eid, r in d["world"]["entities"].items():
       print(f"  {eid[:8]} {'authored' if r['authored'] else 'runtime '} "
             f"{r['scene']:16s} keys={sorted((r.get('state') or {}).keys())}")
   EOF
   ```
   Your saver's key must appear under the entity you expect. An entity with `keys=[]` is wired but
   saving nothing.
3. **Save twice, reload twice, count.** Duplication only shows on the second cycle. A record count
   that grows across identical reloads means something re-spawns instead of adopting its recorded
   identity.
4. **Run `Tools ▸ Save System ▸ Validate Save Wiring`.** Zero errors.
5. **Check the console on load.** `No prefab registered for id` and `share instance id` are both
   silent data loss.
6. **Test with a second peer** if the thing is networked. Host + client, because unregistered
   network prefabs fail only on the client.

---

## 11. Traps this project has actually hit

Each of these cost a debugging session. They are listed because none of them look like bugs.

| Trap | What happens |
|---|---|
| **Prefab-instance overrides** | Assigning `instanceId` on a prefab instance and calling `SetDirty` leaves the value matching the prefab's, so Unity records no override and writes **nothing** to the scene file. Identity is regenerated differently on every scene open and no record ever matches. `SaveableEntity.RecordAsPrefabOverrides` writes through `SerializedObject` to force real overrides. |
| **Grepping for identities** | Overrides are stored as `propertyPath: instanceId` / `value: <guid>`, so `grep "instanceId: <hex>"` finds **zero** even when the data is there. Use `grep -A1 "propertyPath: instanceId"`. |
| **Restoring 0 HP re-runs death** | `HealthComponent.RestoreHealth` fires `OnDeath` when the assignment crosses zero. Without a restore-aware guard, `HealthReactionModule` re-runs the whole death reaction on every load and **re-drops the loot table**. |
| **Death is `SetActive(false)`** | A dead agent is a disabled GameObject, not a destroyed one. Anything keyed on "is enabled" mistakes a corpse for an absence. `SaveableEntity` registers in `Awake`/`OnDestroy`, not `OnEnable`/`OnDisable`, for exactly this reason. |
| **Ternary null into `FixedString`** | `cond ? item.ID : default` types the whole expression as `string` and converts the result; an empty slot converts `null` and throws inside `Unity.Collections`. Guarding the condition does nothing. Type the empty branch `default(FixedString64Bytes)`. |
| **Newtonsoft and Unity types** | `Vector3` / `Quaternion` need the explicit converters in `SaveSerializer` or serialization stack-overflows on their recursive properties. |
| **A saver on a nested `SaveableEntity`** | Collection stops at nested entities. State under one is captured by *that* entity's record, not the parent's — deliberate, but surprising if you expected the parent to own it. |
| **`Instantiate` puts objects in the active scene** | A restored runtime object left in the active scene survives its chunk unloading and piles up a duplicate on every reload. `WorldSaveStore` moves it into the chunk's own scene. |
| **`.gitattributes` eating binary assets** | "Unknown error occurred while loading \<asset\>" means git corrupted the file, not that Unity is broken. Never add `*.asset text`. |

---

## 12. Reference

**Code** — `Assets/Game/Scripts/Core/Persistence/`

```
Format/     ISaveable, IPersistentEntity, SaveDocument, SaveRef, StateBag,
            SaveSerializer, SaveFileStore, SaveSlots, SaveMigrator, WorldIdentity
Runtime/    SaveManager, WorldSaveStore, PlayerSaveService, SaveableEntity,
            SaveablePolicy, SaveablePrefabRegistry, SaveTeleport, SaveNetworking,
            WorldSession, PlayerSaveBinder, PlayerSaveSync, SaveHotkeys
Adapters/   TransformSaveable, RigidbodySaveable, HealthSaveable, MountSaveable,
            AgentStateSaveable, EntityInventorySaveable, PlayerInventorySaveable,
            BackpackSaveable, GameStateSaveable
Editor/     SaveableWiring, SaveWiringValidator
```

**Menu items**

- `Tools ▸ Save System ▸ Wire Saveable Prefabs` — bakes identities and adds savers. Idempotent;
  re-run after adding prefabs.
- `Tools ▸ Save System ▸ Wire Saveable Scene Objects` / `… Wire Saveable Chunk Scenes`
- `Tools ▸ Save System ▸ Validate Save Wiring` — reports what would fail silently.

**Format changes.** Bump `SaveDocument.CurrentVersion` only when a change cannot be absorbed by a
saver reading defensively, and add the matching `ISaveMigration` in the same commit. Adding or
removing a saver needs **no** migration — that is the point of per-saver keys.

**Related docs** — `docs/architecture/AgentSystem.md`, `MountSystem.md`, `EntitySystem.md`,
`Vehicles.md`, `Inventory.md`, and the design record in
`docs/superpowers/specs/2026-08-17-entity-persistence-design.md`.
