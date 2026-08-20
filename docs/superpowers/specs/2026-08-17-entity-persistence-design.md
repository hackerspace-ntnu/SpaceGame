# Entity Persistence — Audit and Design (2026-08-17)

**Goal.** Nothing that moves or changes may be lost across a save. Every agent, every vehicle, every
mount, every drop, every mutable state. Mount a player on the Ostrich and reload — they are still
mounted. Leave a Golem mid-fight and reload — it is still fighting.

**Companion document.** `docs/architecture/Persistence.md` is the how-to. This is the audit that
motivated it and the record of what was decided.

---

## 1. What was already true

The save architecture built over sessions 1–3 (see `docs/superpowers/HANDOFF-2026-08-15-save-load.md`)
is sound and does not need replacing:

- `WorldSaveStore` keyed by identity with the scene as a routing field, hooked to chunk streaming.
- `SaveablePolicy` as a single runtime rule, so an unwired object still persists.
- `SaveableEntity` with baked-vs-derived identity and prefab-override handling.
- `SaveTeleport` handling `NavMeshAgent`, `CharacterController` and `Rigidbody` correctly.
- `SaveablePrefabRegistry` auto-covering every inventory item.
- Server-only authority throughout.

The player persists correctly. Nothing else reliably does. The reason is not in the mechanism.

---

## 2. Audit: the live population

Measured from prefab YAML — components on the **root** GameObject, which is what
`SaveablePolicy.NeedsSaving` is evaluated against — cross-referenced with the scenes that instance
them.

### Agents

| Prefab | In scene | `SaveableEntity` | Qualifies? | Verdict |
|---|---|---|---|---|
| `Agents/Characters/Nomad` | persistentScene | ✅ | Health, NavMeshAgent, SceneTracked | pose + health only |
| `Agents/Creatures/DuneRat` | persistentScene | ✅ | Health, NavMeshAgent, SceneTracked | pose + health only |
| `Agents/Creatures/Golem` | persistentScene | ✅ | Health, NavMeshAgent, SceneTracked | pose + health only |
| `Agents/Creatures/Vrescal` | persistentScene | ❌ | Health, NavMeshAgent, SceneTracked | **derived id only** — fragile |
| `Agents/Creatures/Ostrich` | persistentScene, Ferdinand_Test_world | ❌ | **nothing** — kinematic RB, no Health, no NavMeshAgent | **never captured** |
| `Agents/Creatures/HorseRobot` | — | ❌ | **nothing** | would never be captured |
| `Agents/Robots/PatrolRobot` | Chunk_7_0 | ✅ | Health, NavMeshAgent, SceneTracked | pose + health only |
| `Agents/Robots/DeathmatchBot` | — (spawned) | ✅ | Health, NavMeshAgent, SceneTracked | arena-only, out of scope |
| `Agents/Creatures/CrabWalker6` | — | ❌ | nothing | unused art prefab |
| `Agents/Creatures/HumanoidRobot` | — | ❌ | nothing | unused art prefab |

### Vehicles and mounts

| Prefab | In scene | `SaveableEntity` | Qualifies? | Verdict |
|---|---|---|---|---|
| `Vehicles/Spacecraft/ShipRV` | persistentScene | ✅ | dynamic RB | pose + velocity; deployment & shell lost |
| `Vehicles/Aircraft/DuneOrnithopter` | — (item-spawned) | ✅ | dynamic RB | pose + velocity; flight energy lost |
| `Vehicles/Ground/DuneFoil` | persistentScene, 2 tests | ❌ | **nothing at all** — no RB on root | **never captured** |
| `Vehicles/Ground/DesertCrawler` | persistentScene, Ferdinand_Test_world | ❌ | **nothing** — kinematic RB | **never captured** |
| `Vehicles/Ground/RigWalker` | persistentScene, Ferdinand_Test_world | ❌ | **nothing** — no RB on root | **never captured** |
| `Vehicles/Rover` | — | ✅ | dynamic RB | pose + velocity |
| `Vehicles/Spacecraft/CowBotRocket` | test scene only | ❌ | nothing | test scene, out of scope |

### Drops and items

| Path | Status |
|---|---|
| Player drops (`PlayerDropService`) | ✅ correct — `EnsureRuntime` stamps the item ID, registry resolves it |
| Agent loot (`EntityLootTable` → same service) | ✅ spawns correctly, but **re-drops on every load** (§3.5) |
| Backpack contents | ✅ `BackpackSaveable` |
| Non-item runtime spawns | ❌ `Resources/Saveable` holds only `RocketSpawn` |

### State never captured by anything

Mount link (rider ↔ mount ↔ seat ↔ camera) · AI target and aggro · patrol progress · NPC inventory
and equipped weapon · alive/dead · sail trim and mast cant · mooring · foil and rudder · ornithopter
flight energy · vehicle deployment and shell variant · `SceneTracked` migration state.

---

## 3. Root causes

### 3.1 The opt-in test cannot see what moves

`NeedsSaving` asks for `HealthComponent`, `PickupableItem`, `NavMeshAgent`, or a **non-kinematic**
`Rigidbody`. Every legged machine is a **kinematic** `Rigidbody` driven by `LeggedLocomotion` — the
body is a collider, not a motor. `DuneFoil` has no `Rigidbody` on its root at all. They fail every
clause, so the Ostrich the player rides is not restored badly; it is never looked at.

This is the primary bug and it explains the whole reported symptom.

### 3.2 The format cannot express a reference between two saved things

A mount record must say "profile `abc` was riding me". A Golem record must say "I was fighting entity
`def`". `StateBag` can hold any JSON, but there is no way to name another entity and no way to turn a
name back into a `Transform`. Mount and combat persistence are therefore not "unimplemented" — they
are inexpressible.

### 3.3 The deferred pass is wired for players only

`IDeferredSaveable.OnLoadComplete` is called from exactly one line —
`PlayerSaveService.Bind:88`. World entities never receive it. This is precisely the hook that a
remount needs, because when a mount is restored the rider does not exist: Netcode spawns players at a
time the save system does not control.

### 3.4 Identity is scoped to *enabled*, not to *lifetime*

`SaveableEntity` registers in `OnEnable` and unregisters in `OnDisable`. But `HealthReactionModule`
kills by `gameObject.SetActive(false)`, so **a corpse is absent from the live registry**. Two
consequences:

- `WorldSaveStore.SpawnEntities` dedupes against that registry, so a dead *runtime* entity is
  re-instantiated on every hydrate. This is the most likely explanation for the unresolved
  "runtime items triplicate" item in the handoff — to be confirmed by counting across two reloads.
- Any `SaveRef` pointing at a corpse cannot resolve.

### 3.5 Restoring 0 HP re-runs the entire death reaction

`HealthComponent.RestoreHealth` fires `OnDeath` when the assignment crosses zero — deliberately, so
clients stay in step. But `HealthReactionModule.HandleDeath` then re-runs on every load: it re-emits
death noise, re-disables the agent, and **`EntityLootTable` drops the loot table again**. Kill one
DuneRat, reload five times, get five sets of loot. Death needs to be explicit state, not inferred
from a health value.

### 3.6 Mounts and vehicles are not `SceneTracked`

`Ostrich`, `HorseRobot`, `DuneFoil`, `DesertCrawler`, `RigWalker`, `DuneOrnithopter` and `ShipRV` all
lack `SceneTracked`. They neither migrate between chunk scenes nor keep chunks loaded around
themselves. Any one of them placed in or driven into a chunk scene is destroyed when that chunk
unloads — and `Dehydrate` is driven per scene, so scene membership is a persistence concern, not only
a streaming one.

---

## 4. Design

Five pieces. Each is small; the ordering matters because later pieces depend on earlier ones.

### 4.1 `IPersistentEntity` — opting in by interface

An empty marker interface in `SpaceGame.Persistence`, an assembly with `"references": []`, so
implementing it couples the implementor to nothing.

```csharp
namespace SpaceGame.Persistence
{
    /// <summary>Marks a component whose GameObject is part of the mutable world.</summary>
    public interface IPersistentEntity { }
}
```

`NeedsSaving` gains one clause: `go.GetComponent<IPersistentEntity>() != null → reasons.Add("entity")`.
The `Transient` blacklist is still tested first, so projectiles remain excluded.

Implementors:

| Component | Assembly | Covers |
|---|---|---|
| `AgentController` | Assembly-CSharp | Ostrich, Vrescal, HorseRobot, Nomad, PatrolRobot, DeathmatchBot, DesertCrawler, RigWalker, DuneOrnithopter, ShipRV |
| `MountModule` | Assembly-CSharp | every mount |
| `SceneTracked` | Assembly-CSharp | anything explicitly declared a world entity |
| `LeggedLocomotion` | `SpaceGame.Locomotion` | every legged machine, by inheritance |
| `DuneFoilLocomotion` | `SpaceGame.Vehicles.DuneFoil` | the sailer, which has no other qualifying component |

The last two need `SpaceGame.Persistence` added to their asmdef `references`.

**Why an interface rather than extending the name-matched list.** The existing `Transient` set matches
`c.GetType().Name` because `SaveablePolicy` cannot reference the item and weapon assemblies. Name
matching cannot see base classes — `OstrichLocomotion` does not match `"LeggedLocomotion"` — so a
name list needs every subclass spelled out and breaks silently on rename. This repo has already lost
data to rename fragility (`SerializeReference`, `UnityEvent`). An interface is inherited and
compile-checked.

**Rejected: a hand-authored `PersistentEntity` marker component on each prefab.** Explicit, but "every
agent needs persistency" then depends on somebody remembering, on every prefab, forever — the exact
failure the runtime policy was built to prevent.

### 4.2 `SaveRef` — a serializable cross-entity reference

```csharp
public struct SaveRef
{
    public string Kind;   // "player" | "entity"
    public string Id;     // profile id, or SaveableEntity.InstanceId

    public static SaveRef From(Component c);
    public static SaveRef From(GameObject go);
    public bool IsSet { get; }
    public bool TryResolve(out GameObject target);
}
```

Lives in `SpaceGame.Persistence` next to `ISaveable`; resolution needs the runtime registries, so
`SaveRefResolver` (Runtime) supplies them through a static hook set by `SaveManager`. Entities resolve
through `SaveableEntity.LiveEntities`, players through `PlayerSaveService`'s bindings.

Two referent kinds because the two populations are keyed differently — profile vs instance — and that
distinction is already load-bearing everywhere else in the system.

### 4.3 A world deferred pass

`SaveManager.RunWorldDeferredPass` walks `SaveableEntity.LiveEntities` and calls
`NotifyLoadComplete()` on each world-scoped entity. It is driven from three moments, and needs all
three:

| Trigger | Why |
|---|---|
| `PlayerSaveService.PlayerBound` (new event) | The real precondition is "a player exists to be referenced". Keying off `NotifyLoadApplied` alone would miss a client joining a saved world for the first time, who has no record to restore. |
| `NotifyLoadApplied` | Backstop for a load in which no player binds. |
| `WorldSaveStore.OnSceneHydrated` | Chunks stream in for the whole session. Without this, a mount in a chunk that loaded a minute in holds a rider reference nothing ever resolves. |

**The pass runs repeatedly, and savers must be safe to call twice.** In multiplayer players arrive
one at a time, so a single pass would seat the first player's mount and permanently give up on the
second's. Two policies follow from that: state that depends on nobody (a flight to resume, a patrol
index) is consumed on the first pass, and a reference to someone who may still be arriving (a rider)
is consumed only on success.

### 4.4 Identity for the object's lifetime

`SaveableEntity` registration moves from `OnEnable`/`OnDisable` to `Awake`/`OnDestroy`. A corpse keeps
its identity, `SpawnEntities` stops duplicating disabled runtime entities, and `SaveRef` can resolve
to something dead. Duplicate-id detection stays where it is.

### 4.5 Savers

| Saver | Key | Owns | Notes |
|---|---|---|---|
| `MountSaveable` *(new)* | `mount` | rider `SaveRef` | `IDeferredSaveable`; remounts through `MountNetworkSync.ServerMount` |
| `AgentStateSaveable` *(new)* | `agent` | target and last-attacker `SaveRef`s, `LastKnownPosition`, `TimeSinceSeen`, patrol progress | `IDeferredSaveable` — refs resolve there |
| `EntityInventorySaveable` *(new)* | `entityInventory` | NPC slot item IDs, positionally | mirrors `PlayerInventorySaveable`'s rules |
| `ArticulatedPartsSaveable` *(new)* | `parts` | every hatch/ramp/canopy below the entity, keyed by hierarchy path | one saver for all parts — see below |
| `DuneFoilSaveable` *(new)* | `dunefoil` | per-sail sheet, cant, hoist; mooring | |
| `OrnithopterSaveable` *(new)* | `ornithopter` | airborne flag + airspeed | `IDeferredSaveable`; resumes through `Launch` |

All of them live in `Adapters/` (Assembly-CSharp), which already references every gameplay assembly
automatically. Putting the vehicle savers in the vehicles' own assemblies was considered and rejected:
it would drag Newtonsoft into `SpaceGame.Vehicles.*` for no gain, since Assembly-CSharp can see those
assemblies but not the reverse.

`SaveablePolicy.Ensure` adds every one of them from the component that implies it — `MountModule`,
`AgentTargeting`, `EntityInventoryComponent`, `ArticulatedPart`, `SailRig`, `OrnithopterFlightMotor` —
by the same have-a-component rule it already uses for `HealthSaveable`.

**Three things in this table are absent on purpose.**

- **No `alive` flag.** `Alive` is `currentHealth > 0`, so health 0 already *is* the dead state and a
  second field could only ever disagree with it. What was actually broken is that
  `RestoreHealth` only announced `OnDeath` when the value *crossed* zero, which silently missed an
  entity already at 0 and one at negative health from overkill — both of which came back standing up.
  It now announces whenever the restored value is lethal, and sets `IsRestoring` while it does.
- **No seat or camera perspective on `MountSaveable`.** Every call site passes the mount's single
  `seatPoint`, and `ApplyPerspective` is called from exactly one place with `defaultPerspective`.
  Neither can differ from the prefab's value, so storing them would be storing a constant.
- **No `VehicleFitSaveable`.** `VehicleDeploymentController` and `ShellVariantSwitcher` are both
  *derived*: the first reacts to `Mounted`/`Dismounted`, the second to whether any `ArticulatedPart`
  is open. Restoring the mount and the parts restores both for free. Persisting them as well would be
  a second source of truth that can contradict the first.

**Why `ArticulatedPartsSaveable` is one saver rather than one per part.** A saver owns a key within
its entity's bag, so a component-per-part design needs every part to invent a distinct key, and two
that collide silently overwrite each other. Keyed by hierarchy path rather than by index, because an
index into `GetComponentsInChildren` is whatever Unity's traversal produces — re-parenting one panel
would reassign every part's saved state.

### 4.6 Prefab and scene wiring

- Add `SceneTracked` to `Ostrich`, `HorseRobot`, `DuneFoil`, `DesertCrawler`, `RigWalker`,
  `DuneOrnithopter`, `ShipRV`. **`Pin`** for mounts (they follow the player and must outlive chunk
  unloads); **`Migrate`** for free-roaming vehicles.
- Run `Wire Saveable Prefabs`, then re-save `persistentScene` and `Ferdinand_Test_world` so the new
  identities land as prefab-instance overrides (§11 of the guide).
- Extend `SaveWiringValidator`: **error** on any prefab with `IPersistentEntity` and no
  `SaveableEntity`; **error** on a networked saveable missing from the network prefab list.

---

### 4.7 Runtime-spawned vehicles (found by playtest, same day)

Reported: fly the wing pack, quit, reload — right coordinates, no wings, no velocity. Four defects,
three of them generic.

| # | Defect | Fix |
|---|---|---|
| 1 | **The craft could not be rebuilt.** `DuneOrnithopter` is neither an `InventoryItem.itemPrefab` (the *pack* is the item) nor under `Resources/Saveable`, so `SaveablePrefabRegistry` could not resolve its `prefabId`. Its record was in every save file; the load printed one warning and dropped it. | `SaveablePrefabRegistry` also registers **every registered network prefab carrying a `SaveableEntity`**, scanned lazily on the first miss so `NetworkManager`'s Awake order cannot matter. A spawnable world object must already be a network prefab, so this is the same rule stated once rather than a new list to maintain. |
| 2 | **Runtime spawns never met the policy.** `SaveablePolicy.Ensure` ran only from `EnsureScene`, i.e. only on objects a scene load brought in. A spawned craft was saved only as well as its prefab was authored — pose and velocity, never its rider. | New `SaveablePolicy.EnsureSpawned`, called from `WorldService.Spawn` and from `WorldSaveStore.SpawnEntities`. In the restore path it runs **before** `Restore`, because a saver added afterwards is handed nothing. |
| 3 | **The player's momentum was never saved, and was actively zeroed.** `PlayerRecord` has no velocity field, the player is `SaveScope.External` so the policy skips it, and `SaveTeleport.Move` defaults to `zeroVelocity: true`. | `PlayerSaveService.Bind` wires a `RigidbodySaveable` onto any player with a dynamic body. No format change — the existing saver already round-trips through the player's own state bag, and it restores *after* the teleport that would otherwise wipe it. |
| 4 | **`PlayerBound` fired too early** (introduced in §4.3 the same day). Raised before the record was applied, so the deferred pass re-seated the rider and `SaveTeleport` then dragged them back out of the seat. | Moved to the end of `Bind`, still firing for players with no record. Pinned by `Bind_AnnouncesThePlayerOnlyAfterRestoringIt`. |

**Plus one ownership repair.** `WingPackItem` spawns the craft and tears it down on landing, but a load
rebuilds the craft through the save system, which knows nothing about wing packs — leaving the pack
believing it was stowed while its owner was airborne: folded pack rendering in the pilot's hand, a
second craft deployable, and nothing subscribed to `Landed`, so touching down left the ornithopter
standing in the sand forever.

`WingPackItem.AdoptCraft` fixes it, driven from `MountModule.Mounted` (via `OrnithopterSaveable`)
rather than from the pack's own equip. The pack is equipped while the player's inventory is restored,
which is strictly *before* the deferred pass that seats the rider — so at the moment it is equipped
there is nothing yet to adopt. Driving it off the event that actually reports a rider is also
order-independent between the two savers on the craft, which run in component order.

---

## 5. Scope decisions

Settled with the user; do not re-litigate.

| # | Question | Decision |
|---|---|---|
| 1 | Save while mounted | **Still seated.** Deferred remount after players spawn. |
| 2 | Agent mid-fight | **Full combat state** — target, last-known position, aggro — with refs resolved deferred. |
| 3 | Dead agents | **Stay dead, loot not re-dropped.** Explicit `alive` flag; death reaction suppressed on restore. |

Out of scope: arena/minigame entities (`MatchManager` owns its own lifecycle), projectiles
(`Transient` by design), test-scene-only prefabs (`CowBotRocket`), unused art prefabs
(`CrabWalker6`, `HumanoidRobot`) — the interface covers the last two automatically if they are ever
placed.

---

## 6. Format impact

**No version bump, no migration.** Every change is a new saver key or a new field inside an existing
key, and savers read defensively — that is the property per-saver keys exist to provide. An old save
loading against this code finds no `mount` key and no `agent` key and leaves those components at
their prefab defaults, which is the correct behaviour for a save written before the state existed.

`SaveDocument.CurrentVersion` stays at 2.

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| Deferred remount fights NGO reparenting | `MountModule.TryMount` is the same path the live game uses; the pass runs after players are spawned, which is the precondition it already requires. Verify host + client. |
| A `SaveRef` target legitimately no longer exists | `TryResolve` returns false and the saver leaves state alone. A missing rider means "not mounted", which is a valid world. |
| Restoring an agent's target re-triggers combat audio/alerts on load | Restore the target through `ForceTarget`, which sets state without re-running perception's alert broadcast. |
| Adding `SceneTracked` changes streaming behaviour | `Pin` for mounts is the documented policy for player-attached entities; measure chunk load counts before and after. |
| `Awake` registration changes ordering | `Awake` runs before any `OnEnable` that could consult the registry; duplicate detection is unchanged. |

---

## 8. Verification plan

1. **EditMode tests** — `SaveRef` round trip and resolution; `NeedsSaving` returns true for a
   kinematic-`Rigidbody` object with `IPersistentEntity`; `HealthSaveable` alive/dead round trip;
   loot suppressed on a restore-driven `OnDeath`.
2. **Play-mode round trip** — mount the Ostrich, wound the Golem, sail the DuneFoil, drop an item,
   kill a DuneRat → menu → re-enter. All five must hold.
3. **File inspection** — `mount`, `agent`, `health.alive` present on the right entities.
4. **Two reloads** — entity record count stable, loot not duplicated.
5. **`Validate Save Wiring`** — zero errors.
6. **Host + client** — restored mount and rider correct on the client.
