# Handoff — Save/Load + World Selection (2026-08-15)

Branch: `remove-dead-assets-and-create-new-prefabs`
**Nothing is committed.** A hook blocks the agent from running `git commit`; the user commits.

---

## 1. What was asked

> "Improve the save/load system. Start either a new world or select a saved world — think
> Minecraft. All worlds must run in singleplayer or multiplayer. Whoever creates the lobby
> defines whether it is a new world or a saved one (of theirs). Fully implement, review, refactor,
> minimum lines of code, best architecture."

Then, across follow-ups: fix the port-7777 regression, fix the inventory restore crash, and
**"everything should be saved"** (full world persistence, not just the player).

---

## 2. Design decisions (settled with the user — do not re-litigate)

| # | Decision | Chosen |
|---|---|---|
| 1 | World identity | **Slot id IS the world.** One flat file per world: `Saves/<name>.json`. |
| 2 | When the host picks | **Before the lobby opens.** One screen serves SP + MP. |
| 3 | How the choice travels | **One `WorldSession` static**, replacing `PendingLoad`/`StageLoad`/`ClearStagedLoad`. |
| 4 | New World means | One world type + a **`WorldConfigId` guard** in the header. Terrain is authored, there is no seed. |
| 5 | Save scope | **Everything mutable** — creatures, vehicles, mounts, dropped items. |

Specs: `docs/superpowers/specs/2026-08-15-world-selection-design.md`
Plan (with recorded results): `docs/superpowers/plans/2026-08-15-world-selection.md`

---

## 3. Architecture as built

**Assembly constraint — the thing that shapes everything.** `SpaceGame.Persistence` (the Format
folder) declares `"references": []`. It cannot see `Assembly-CSharp` or
`SpaceGame.World.Streaming`. So:

- `WorldIdentity` (Format assembly) — pure rules: name sanitisation, the config guard, display
  names. Unit-testable without Unity.
- `WorldSession` (Assembly-CSharp) — the runtime static: which world is active, the staged
  `SaveDocument`, the `WorldStreamingConfig`.

If a change seems to need Format → config, it belongs in `WorldSession`, not `WorldIdentity`.

**Files created**
```
Assets/Game/Scripts/Core/Persistence/Format/WorldIdentity.cs
Assets/Game/Scripts/Core/Persistence/Runtime/WorldSession.cs
Assets/Game/Scripts/Core/Persistence/Editor/SaveableWiring.cs      <- opts world objects in
Assets/Game/Scripts/Presentation/UI/Pages/WorldSelectUI.cs
Assets/Game/Editor/Menus/WorldSelectBuilder.cs                     <- builds the UI panel
Assets/Game/Editor/Tests/WorldIdentityTests.cs
Assets/Game/Editor/Tests/WorldSaveRoundTripTests.cs
Assets/Game/Prefabs/UI/WorldRow.prefab                             <- generated
```

**Files modified (this work only)**
```
Core/Persistence/Format/SaveDocument.cs        + SaveHeader.WorldName, .WorldConfigId (still v1)
Core/Persistence/Runtime/SaveManager.cs        DefaultSlotId, SaveNewWorld(), SaveOnExit(),
                                               removed PendingLoad/StageLoad/ClearStagedLoad
Core/Persistence/Runtime/SaveHotkeys.cs        F5/F9 are per-world now
Core/Persistence/Runtime/SaveableEntity.cs     RecordAsPrefabOverrides()  <- critical, see §5
Core/Multiplayer/SessionLauncher.cs            + HostLocal()
Presentation/UI/Pages/MainMenuUI.cs            routes through world select; uses HostLocal
Presentation/UI/Pages/PauseMenuUI.cs           SaveOnExit() + WorldSession.Clear() before shutdown
Items/Inventory/Components/PlayerInventoryNetwork.cs   3 FixedString null fixes
World/Streaming/Core/WorldStreamer.cs          + public Config property
World/Streaming/Core/WorldStreamingConfig.cs   + configId stamped from asset GUID
Assets/Game/Settings/{World,FerdinandWorld}StreamingConfig.asset   configId values
Assets/Game/Scenes/Core/MainMenu.unity         world-select panel (built by the tool)
Assets/Game/Scenes/world/persistentScene.unity + Chunks/Chunk_7_0.unity   entity identities
~30 prefabs under Assets/Game/Prefabs/         SaveableEntity + savers
```

Note: `HeadlessTestRunner.cs`, `LoadingScreenUI.cs`, `Networking.cs`, `VrescalBuilder.cs` and the
`Net*` test files were **already modified on the branch before this work started** — not ours.

---

## 4. Bugs found and fixed along the way

1. **New world never written to disk.** `StageNew` only set memory state; the file appeared on the
   first save, and the only triggers were a 300 s autosave and `OnApplicationQuit`. Fixed with
   `SaveManager.SaveNewWorld()` from `Start()`.
2. **Returning to the main menu discarded the session.** `PauseMenuUI` does `Shutdown()` +
   `LoadScene("MainMenu")`, which is not `OnApplicationQuit`. Fixed with `SaveManager.SaveOnExit()`
   (synchronous — the scene load destroys the stores it reads from).
3. **Port regression (mine).** Routing singleplayer through `SessionLauncher.HostDirect()` forced
   port **7777** via `SetConnectionData`, overriding the prefab's deliberate **7781** and defeating
   the project's leaked-socket workaround. Fixed with `HostLocal()`, which re-feeds the transport its
   OWN `ConnectionData` — still resetting Relay mode, since `SetConnectionData` internally calls
   `SetProtocol(ProtocolType.UnityTransport)` (UnityTransport.cs:889).
   `HostDirect(7777)` is correct ONLY for the Direct-connect tab and `MultiplayerAutotest`.
4. **Inventory restore crash.** `cond ? item.ID : default` — both arms are `string`, so C# types the
   ternary as string and converts the RESULT; an empty slot converted `default(string)` = null, and
   `FixedString64Bytes` throws an NRE on null. **Guarding the condition does nothing.** Fixed in 3
   places by typing the empty branch `default(FixedString64Bytes)`. Any save with one empty hotbar
   slot failed to load. Written up in memory as `project_fixedstring_null_trap`.

---

## 5. THE CRITICAL MECHANISM (read before touching identities)

`SaveableEntity.OnValidate` stamps `prefabId` / `instanceId` / `authored`. On a **prefab instance**
the plain field assignment + `SetDirty` was **not enough**: the value matched the prefab's, Unity
recorded no override, and nothing was written into the scene file — so identities were regenerated,
differently, on every scene open, and no saved record could ever be matched back.

Fixed by `RecordAsPrefabOverrides()`, which writes the fields through `SerializedObject` so Unity
registers real overrides.

**Verification gotcha that cost time:** overrides are stored as
```yaml
- target: {fileID: ..., guid: ...}
  propertyPath: instanceId
  value: <guid>
```
so grepping for `instanceId: <hex>` finds **zero** even when the data is present. Use:
```bash
grep -A1 "propertyPath: instanceId" Assets/Game/Scenes/world/persistentScene.unity
```
Confirmed on disk: **persistentScene has 6 unique instanceIds, Chunk_7_0 has 1**, all `authored: 1`.

---

## 6. Verified (real runs, not assertions)

All via the unity-mcp bridge against the live editor. **53 + 4 + 20 + 21 assertions, 0 failures.**

- **Logic 12/12** — sanitisation, config guard (accept match / refuse mismatch with reason / accept
  legacy empty), display-name fallback, header fields default empty, `CurrentVersion == 1`.
- **File layer 20/20** — round trip through real `SaveFileStore` and real files; `worldName` +
  `worldConfigId` reach the JSON text; two worlds keep separate state in separate files; existing
  name detected before overwrite; legacy save loads; `../../etc/passwd` cannot escape the save root;
  delete removes one and leaves the rest.
- **Scene wiring 21/21** — all 8 `WorldSelectUI` fields resolved, both `MainMenuUI` fields resolved,
  3 buttons bound to the right methods, panel starts hidden, row prefab correct.
- **FixedString 4/4** — reproduced the original NRE with the old code shape, confirmed the new shape
  returns an empty string. Before/after proof.
- **Compile: clean**, zero errors.

**User-confirmed working:** save and load now work; inventory restores; world selection flow is good.

---

## 6b. Session 2 (same day) — mount teardown + three of the open items

### The reported bug: returning to the menu while mounted

```
Cannot set the parent of the GameObject 'PlayerCharacterNetworked(Clone)' while activating or
deactivating the parent GameObject 'Ostrich'.
  MountModule:UnparentRider → MountModule:Dismount → MountModule:OnDisable
```

**Root cause.** `PauseMenuUI.ReturnToMenu` calls `NetworkManager.Shutdown()`, and NGO *deliberately*
skips deparenting children while shutting down (`NetworkSpawnManager.OnDespawnObject`, the
`!ShutdownInProgress` guard at line 1585). So the rider is still parented to the mount and no longer
spawned. `LoadScene("MainMenu", Single)` then tears the world scene down, Unity deactivates the
Ostrich, `MountModule.OnDisable` fires, sees `IsMounted`, and runs the full `Dismount()`. Its
networked detach is unavailable (`IsSpawned` is false), so it falls through to a raw
`Transform.SetParent(null, true)` — which Unity refuses while the parent is mid-activation-change.

**Fix.** `MountModule.OnDisable` now branches on `gameObject.activeInHierarchy` and calls a new
`AbandonRider()` instead of `Dismount()` when the mount is going down. `AbandonRider` forgets the
rider and touches nothing in the hierarchy.

**Measured, not assumed** (probes run against the live editor):

| Case | `activeInHierarchy` in OnDisable | reparent |
|---|---|---|
| component `enabled = false` | **true** | succeeds |
| `gameObject.SetActive(false)` | **false** | refused, exact error reproduced |
| scene unload | **false** (`scene.isLoaded` false too) | refused |

So the flag is exactly the condition Unity guards on. Then verified in **play mode** with a
throwaway harness against the real `MountModule`:

- mount deactivated → `IsMounted=False`, rider left parented, **no error logged**
- module switched off → `IsMounted=False`, rider reparented to root — a real dismount still happens

`Assets/Game/Editor/Tests/MountTeardownTests.cs` pins the platform behaviour the fix reads.
It cannot exercise `MountModule.OnDisable` itself: **Unity does not deliver OnEnable/OnDisable to a
plain MonoBehaviour in edit mode** (only with `[ExecuteAlways]`), which is why the first attempt at
an edit-mode reproduction showed nothing at all.

### Also done this session

- **7e closed.** `Agents/Creatures/MountableAnt.prefab` (4 missing scripts — the pre-`MountModule`
  trio) deleted. Its root GameObject fileID is *identical* to the live
  `Agents/Vehicles/Mounts/MountableAnt.prefab`, so both `DefaultNetworkPrefabs` assets were
  **repointed** by GUID rather than emptied — which also fixes the live ant never having been
  registered as a network prefab at all.
- **7f code done.** `MainMenuUI` gained a `continueButton` field and a `Start()` that hides it when
  no save exists, plus `Assets/Game/Editor/Menus/ContinueButtonBuilder.cs`
  (Tools ▸ SpaceGame ▸ Menus ▸ Rebuild Continue Button). **The builder has not been run** — see below.
- **`HeadlessTestRunner` now works in a background editor.** It scheduled runs on
  `EditorApplication.delayCall`, which is only drained by the editor's interactive tick — so with
  the Unity window unfocused (the normal state when driving it from a terminal) a scheduled run
  never started, and that is indistinguishable from one still going. It now pumps
  `EditorApplication.update`. **This is why §6's EditMode tests were never run.**

### unity-mcp bridge limits found this session (add to `project_headless_verification`)

- `File.Delete` / `File.Exists` in a RunCommand are refused as "User interactions are not supported"
  — same bucket as `Directory.Delete`. The refusal names no API, so it reads like a bridge fault.
- Anything touching `EditorApplication.isPlaying`, or `ExecuteMenuItem("Edit/Play")`, is refused.
  **Workaround that works:** set a `SessionState` flag from a RunCommand, and have an
  `[InitializeOnLoadMethod]` in a *project* file enter play mode on the far side of the reload.
  Pump it on `EditorApplication.update`, not `delayCall`.
- `AssetDatabase.Refresh()` in a RunCommand **can deadlock the bridge for its full 1800 s timeout**
  — the domain reload it triggers has to unload the assembly the command is running in. It worked
  three times and then wedged; it never recovered in this session.
- Unity does **not** auto-refresh while unfocused. After editing files headlessly, nothing compiles
  until someone brings the editor to the foreground.

---

## 6c. Session 3 — entities did not persist. Format v2.

**Reported:** new world → walk → note where creatures are → exit → re-enter. The player's position
restores; every other entity is back where it started.

**Root cause, proven against the user's own save files.** `WorldStreamer.UpdateSceneMembership`
moves every `SceneTracked` entity into whichever chunk it has wandered into. v1 filed a world record
under the scene the object was CAPTURED in, and `RestoreAuthored` looked it up in that scene on load
— but the scene file had put the creature back in the scene it was AUTHORED in. The lookup missed,
the record was dropped with a warning, and the creature stood at its authored position.

The evidence, from `~/Library/Application Support/.../Saves/zombies.json`:

| | |
|---|---|
| authored records under `chunk:7,5` | **4** |
| authored records under `persistent` | 1 |
| of those instanceIds actually authored in `persistentScene.unity` | **all 5** |

The player was unaffected because `PlayerSaveService` keys by profile, not by scene — which is
exactly why the symptom looked like "the player works, nothing else does".

**The fix: identity is the key, the scene is a field.** `WorldRecord` is now one flat
`Dictionary<instanceId, EntityRecord>` plus a global `Destroyed` list; `SceneRecord` is gone.
`EntityRecord` gained `Scene` (routing, re-stamped on every capture), `Authored`, a pose, and
`HasPose`. Format version 2 with a `V1GlobalEntities` migration, so existing worlds keep everything.

`RestoreAuthored` now iterates the objects PRESENT in a scene and looks up their records, rather
than the reverse. A record whose scene is not loaded waits quietly instead of being reported missing.

**Also: the editor wiring step is no longer required.** The rule moved out of the editor tool into
`SaveablePolicy` (runtime), and `WorldSaveStore.Hydrate` applies it as each scene loads, so an object
nobody wired still persists — with an identity derived from scene + hierarchy path
(`SaveableEntity.DeriveAuthoredId`). Running `Wire Saveable Prefabs` is now an optimisation: it bakes
a GUID that survives renaming and re-parenting.

**Verified** — 812 EditMode tests pass, 0 fail (the 1 skip is a pre-existing deliberate `Ignore`).
Plus two live checks through the bridge: the v1→v2 migration on a real payload, and a creature
captured in `chunk:7,5` restoring correctly when `persistent` hydrates.

**Files:** `Format/SaveDocument.cs`, `Format/Migrations/V1GlobalEntities.cs` (new),
`Format/SaveMigrator.cs`, `Runtime/WorldSaveStore.cs` (rewritten), `Runtime/SaveablePolicy.cs` (new),
`Runtime/SaveableEntity.cs`, `Runtime/SaveManager.cs`, `Editor/SaveableWiring.cs`, and the four save
test fixtures.

### Still open from this session
- **Runtime items triplicate.** `zombies.json` holds 12 runtime records for 4 dropped items — three
  copies each, DIFFERENT instanceIds, IDENTICAL positions. So the live world really had 12 objects;
  something spawns a fresh copy per session rather than adopting the recorded identity. The id-keyed
  dictionary bounds the file but does not explain the duplication. Reproduce by dropping an item,
  reloading twice, and counting.
- One authored entity (`bbc860ca66ef…`, in persistentScene) captures at position (0,0,0) in every
  save. Worth finding out which object that is — it is wired but appears to sit at the world origin.

---

## 7. OPEN — pick up here

### 7a. Verify a live save now records world entities
Everything in §5 is wired, but **no Play-mode save has been taken since**. Do this first:

1. Play → new world → damage a Golem, move a vehicle, drop an item → pause → return to menu.
2. Inspect the file:
```bash
python3 - <<'EOF'
import json, glob, os
root = os.path.expanduser("~/Library/Application Support/Hackerspace NTNU/SpaceGame/Saves")
f = max(glob.glob(root + "/*.json"), key=os.path.getmtime)
d = json.load(open(f)); print(os.path.basename(f))
for name, rec in (d.get("world", {}).get("scenes") or {}).items():
    print(f"  [{name}] entities={len(rec.get('entities') or [])} "
          f"authored={len(rec.get('authored') or [])} "
          f"destroyed={len(rec.get('destroyedAuthored') or [])}")
EOF
```
**Expect `authored` > 0 for `persistentScene`.** Before this work it was `WORLD.SCENES: 0`.
3. Reload the world and confirm the Golem is still wounded and the vehicle still moved.

**Session 2 pinned down the number you should see.** The on-disk precondition is verified: 30 prefabs
carry `SaveableEntity`, `persistentScene` holds 24 prefab instances of which **exactly 6** are of
wired prefabs — 2× DuneRat, ShipRV, Vrescal, Golem, and PlayerCharacter — and the scene carries
exactly 6 `instanceId` overrides, all `authored: 1`, no duplicates. `Chunk_7_0` carries 1 and no
other chunk carries any.

So **expect `authored` = 6 for `persistentScene`** (5 once the player is excluded by
`SaveScope.External`), not some larger number. A low count here is not missing wiring — the world
simply has few authored saveables placed. Anything *below* 5 is the real signal that capture is
broken. Damage the Golem and move the ShipRV specifically; those two are known to be wired.

### 7b. NavMesh staleness — **DONE (session 3)**
Only `Chunk_7_0` was stale. Re-baked: 130 sources from 48 chunk scenes in 8.2 s,
`WorldNavMeshStaleness.Check()` now reports up to date. One pre-existing warning worth a look:
`'GameObject' in Chunk_6_5 has no baked mesh`.

### 7c. Network prefab registration — **DONE (session 3)**
`Sync Network Prefabs` run: 40 networked prefabs, 1 added (`Agents/Vehicles/Mounts/MountableAnt`),
and `NetworkPrefabRegistrationTests` now passes.

**Do not hand-edit `DefaultNetworkPrefabs.asset`.** Session 2 repointed an entry by GUID because the
dead prefab and the live one share a root fileID — but the live ant is a prefab VARIANT, whose root
fileID belongs to its BASE, so the reference resolved to null and the list grew a null entry. Let
the tool write these; it loads through `AssetDatabase` and gets it right.

Still worth running **Tools ▸ Save System ▸ Validate Save Wiring** (`SaveWiringValidator`).

### 7d. `Resources/Saveable` is still just a README
Dropped **inventory items** are already covered — `SaveablePrefabRegistry` auto-registers every
`InventoryItem` by asset GUID. But any *non-item* runtime-spawned saveable needs its prefab under a
`Resources/Saveable` folder. Add them there if such objects appear.

### 7e. Broken asset blocking one prefab — **DONE (session 2)**
Deleted, and both prefab lists repointed to the live ant. Nothing to do.

### 7f. Smaller loose ends
- `ContinueGame()` had **no button** — **DONE**. Builder written in session 2 and run in session 3;
  `MainMenu.unity` now carries a Continue button above Singleplayer, bound to `ContinueGame`, with
  `MainMenuUI.continueButton` assigned so it hides itself when there is no save.
- `MainMenuUI.LoadGame(string)` was deleted (verified unreferenced — no C# caller, no scene binding).
- `WorldRow.prefab` uses flat white placeholder Images; the list will look rough until styled.
- The user reported "white outlines" in the loading screen — investigated, **not from this work**:
  they are pre-existing sprite-less white Images (`icon` on inventory `Slot(Clone)` ×3, `BarTrack`).
  The world-select canvas is not even loaded in the world scene.

---

## 8. Tooling notes that will save you an hour

**Re-runnable menu items created here**
- `Tools ▸ Save System ▸ Wire Saveable Prefabs` — components chosen from what an object HAS, so new
  prefabs are covered by re-running rather than by remembering a list.
- `Tools ▸ Save System ▸ Wire Saveable Scene Objects` / `… Wire Saveable Chunk Scenes`
- `Tools ▸ SpaceGame ▸ Menus ▸ Rebuild World Select` — idempotent; strips its own `WorldSelect` root
  first.

**unity-mcp bridge limits hit repeatedly** (also in memory `project_headless_verification`)
- No Newtonsoft reference → `StateBag.Set/TryGet/TryGetRaw` are unusable in a RunCommand.
- `System.Reflection` NREs at execution; `HashSet<T>` fails to compile (use `List<T>`).
- `Path.GetTempPath()`, `Directory.Delete(path, true)` and sometimes `AssetDatabase.LoadAssetAtPath`
  are refused as "User interactions are not supported". Use `Application.temporaryCachePath`.
- **Instead of reflection, take a typed delegate** — `Action[] m = { menu.StartSinglePlayer, … }`
  makes the compiler prove a scene-bound method exists.
- The EditMode Test Runner **cannot be launched while the editor is unfocused**
  (`EditorApplication.delayCall` never ticks), so `WorldIdentityTests` / `WorldSaveRoundTripTests`
  are committed but **have not been run in the Test Runner**. Their assertions were proven
  equivalently through the bridge.

---

## 9. Memory written this session
- `project_world_selection` — the feature and its load-bearing decisions
- `project_fixedstring_null_trap` — the ternary/null NRE
- `project_netcode_editor_gotchas` — appended: the port-bump workaround is defeated by any
  `SetConnectionData` call
- `project_headless_verification` — appended: the four new bridge limits above
