# Minigame Deathmatch Core + Team Deathmatch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `MinigameArena.unity` playable as a Team Deathmatch match — allied and enemy `DeathmatchBot`s, a player with a weapon and faction, a `MatchManager` that spawns everyone, tracks kills/lives, detects the win condition, respawns or eliminates on death, and shows a win/loss result screen. Team sizes and win condition are hardcoded for this plan (no config UI yet — that's a follow-up plan covering FFA/Battle Royale + the config screen, per `docs/superpowers/specs/2026-08-04-minigame-deathmatch-design.md`).

**Architecture:** Reuses the existing modular entity AI system (`IBehaviourModule`, `EntityFaction`/`EntityTargetRegistry`, `AgentWeaponDefinition`/`AgentProjectile`) unchanged. New work: 4 team `FactionDefinition` assets + relationship entries, a `DeathmatchBot` prefab variant of `PatrolRobot` with combat enabled, a new player-side networked fire component reusing `AgentWeaponDefinition`/`AgentProjectile`, and one new server-authoritative `MatchManager` (`NetworkBehaviour`) that owns spawning, respawn/elimination, and win detection. Pure-logic pieces (spawn-point shuffling/team assignment, win-condition evaluation) are extracted into plain C# classes with EditMode tests; Unity-authoring steps (prefabs, ScriptableObject assets, scene placement) are verified manually in the Editor/Play Mode, matching this project's existing testing convention (no automated tests exist anywhere in the codebase today).

**Tech Stack:** Unity 6, Netcode for GameObjects, existing `agents/` AI framework, uGUI. Unity Editor operations are performed via the `unity-mcp` MCP tools (`Unity_ManageAsset`, `Unity_ManageGameObject`, `Unity_CreateScript`, `Unity_ApplyTextEdits`, `Unity_RunCommand`), not hand-written YAML.

---

## Section A: EditMode test infrastructure

### Task A1: Create the EditMode test assembly

**Files:**
- Create: `Assets/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef`
- Create: `Assets/Tests/EditMode/.gitkeep` (placeholder so the folder exists before Task B has a test file)

No asmdef exists anywhere in `Assets/Scripts` today — all game code compiles into the implicit default assembly (`Assembly-CSharp`).

**Important (this was originally specified wrong and cost a debugging cycle):** an assembly definition **cannot** reference the predefined `Assembly-CSharp`. The dependency only runs the other way — predefined assemblies automatically reference every `autoReferenced` asmdef, never the reverse. Listing `"Assembly-CSharp"` in an asmdef's `references` is silently ignored: the test assembly still compiles, but without visibility of any game type, producing a wall of `CS0103: The name 'X' does not exist in the current context`.

The code under test must therefore live in its own assembly. Pure-logic classes go in `Assets/Scripts/Minigame/Core/` under `SpaceGame.Minigame.Core` (`autoReferenced: true`, no references of its own), which the test assembly references by name and which `Assembly-CSharp` keeps seeing automatically — so `MatchManager` needs no changes. Only dependency-free types can move there; anything touching Netcode or the rest of `Assembly-CSharp` (e.g. `MatchManager`) must stay put.

- [ ] **Step 1: Create the folder and asmdef via Unity MCP**

Use `mcp__unity-mcp__Unity_ManageAsset` with `Action: "CreateFolder"`, `Path: "Assets/Tests"`, then again with `Path: "Assets/Tests/EditMode"`.

Then create the asmdef file directly (asmdef is just JSON text, not a scripted asset type):

```json
{
    "name": "SpaceGame.Tests.EditMode",
    "rootNamespace": "",
    "references": [
        "SpaceGame.Minigame.Core",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": true,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Write this via `mcp__unity-mcp__Unity_ManageAsset` with `Action: "Create"`, `Path: "Assets/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef"`, `AssetType: "TextAsset"`, and the JSON above as the file content (if the tool's `Create` action does not support raw text content for arbitrary files, instead use `mcp__unity-mcp__Unity_RunCommand` to write the file directly via `System.IO.File.WriteAllText` to the absolute project path, then `AssetDatabase.Refresh()`).

- [ ] **Step 2: Verify Unity compiles the new assembly with no errors**

Use `mcp__unity-mcp__Unity_GetConsoleLogs` (or the equivalent console-reading tool) after the asset import. Expected: no compile errors related to the new asmdef. If NUnit/TestRunner references are unresolved, confirm the Unity Test Framework package (`com.unity.test-framework`) is present in `Packages/manifest.json` — it ships with every Unity project template by default, so this should already be satisfied.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/
git commit -m "test: add EditMode test assembly for gameplay logic"
```

---

## Section B: Faction assets

### Task B1: Add a public setter to `EntityFaction`

**Files:**
- Modify: `Assets/Scripts/agents/faction/EntityFaction.cs`

`EntityFaction.faction` currently has no way to be set except via the Inspector. `MatchManager` needs to assign factions at runtime to instantiated bots/players before their `OnEnable` self-registers them into `EntityTargetRegistry`.

- [ ] **Step 1: Read the current file to confirm exact line numbers before editing**

Use `mcp__unity-mcp__Unity_FindInFile` or `Read` on `Assets/Scripts/agents/faction/EntityFaction.cs` to confirm the field declaration is still:
```csharp
[SerializeField] private FactionDefinition faction;
[SerializeField] private FactionRelationshipTable relationshipTable;

public FactionDefinition Faction => faction;
```

- [ ] **Step 2: Add `SetFaction` via `Unity_ApplyTextEdits`**

Insert immediately after `public FactionDefinition Faction => faction;`:

```csharp

    // Assigns faction at runtime, before OnEnable registers this entity into
    // EntityTargetRegistry. Used by MatchManager when spawning match entities
    // (bots and players) whose faction depends on chosen team/gamemode, not
    // on what's serialized in the prefab.
    public void SetFaction(FactionDefinition newFaction, FactionRelationshipTable table = null)
    {
        faction = newFaction;
        if (table != null)
            relationshipTable = table;
    }
```

- [ ] **Step 3: Verify compile**

Check `mcp__unity-mcp__Unity_GetConsoleLogs` for compile errors after the edit. Expected: none.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/agents/faction/EntityFaction.cs
git commit -m "feat: allow runtime faction assignment on EntityFaction"
```

### Task B2: Author the 4 team faction assets

**Files:**
- Create: `Assets/Prefabs/agents/factions/TeamRed.asset`
- Create: `Assets/Prefabs/agents/factions/TeamBlue.asset`
- Create: `Assets/Prefabs/agents/factions/TeamGreen.asset`
- Create: `Assets/Prefabs/agents/factions/TeamYellow.asset`

Existing faction assets (`PlayerFaction.asset`, `NPCFaction.asset`, `RobotFaction.asset`) live in `Assets/Prefabs/agents/factions/` — follow the same location and naming convention.

- [ ] **Step 1: Create each asset via Unity MCP**

For each of the 4 team names, call `mcp__unity-mcp__Unity_ManageAsset` with `Action: "Create"`, `Path: "Assets/Prefabs/agents/factions/Team<Name>.asset"`, `AssetType: "FactionDefinition"`, `Properties: {"factionName": "Team<Name>", "debugColor": [<r>,<g>,<b>,1]}`. Use these colors:
  - TeamRed: `[1, 0.2, 0.2, 1]`
  - TeamBlue: `[0.2, 0.4, 1, 1]`
  - TeamGreen: `[0.2, 1, 0.3, 1]`
  - TeamYellow: `[1, 0.9, 0.2, 1]`

- [ ] **Step 2: Verify all 4 assets exist**

Use `mcp__unity-mcp__Unity_ManageAsset` with `Action: "Search"`, `Path: "Assets/Prefabs/agents/factions/"`, `SearchPattern: "Team*.asset"`. Expected: 4 results.

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/agents/factions/Team*.asset*
git commit -m "feat: add 4 team faction assets for team deathmatch"
```

### Task B3: Add cross-team Hostile relationships to `GlobalRelationships`

**Files:**
- Modify: `Assets/Prefabs/agents/factions/GlobalRelationships.asset`

`FactionRelationshipTable.Get()` treats same-faction pairs as `Allied` automatically (see `Assets/Scripts/agents/faction/FactionRelationshipTable.cs`, `if (a == b) return Allied`), so only the 6 cross-team pairs need explicit `Hostile` entries. This is a data-only asset edit — no new mutation API needed, since `Unity_RunCommand` can call the editor-time serialization APIs directly.

- [ ] **Step 1: Write and run a one-off editor command to append the 6 pairs**

Use `mcp__unity-mcp__Unity_RunCommand` with code that loads the 4 team faction assets and the `GlobalRelationships` asset, and appends `FactionPairRelationship` entries for all 6 unique pairs (Red-Blue, Red-Green, Red-Yellow, Blue-Green, Blue-Yellow, Green-Yellow) with `relationship: Hostile`, using reflection to append to the private `relationships` list (or, simpler: add a temporary public `AddPair` method to `FactionRelationshipTable` for this one edit — but since Task B1 already established runtime mutation is acceptable for `EntityFaction`, prefer adding a small internal editor-only helper instead of reflection):

```csharp
using UnityEngine;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var table = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(
            "Assets/Prefabs/agents/factions/GlobalRelationships.asset");
        var red = AssetDatabase.LoadAssetAtPath<FactionDefinition>("Assets/Prefabs/agents/factions/TeamRed.asset");
        var blue = AssetDatabase.LoadAssetAtPath<FactionDefinition>("Assets/Prefabs/agents/factions/TeamBlue.asset");
        var green = AssetDatabase.LoadAssetAtPath<FactionDefinition>("Assets/Prefabs/agents/factions/TeamGreen.asset");
        var yellow = AssetDatabase.LoadAssetAtPath<FactionDefinition>("Assets/Prefabs/agents/factions/TeamYellow.asset");

        if (table == null || red == null || blue == null || green == null || yellow == null)
        {
            result.LogError("Missing one or more assets — check paths from Task B2.");
            return;
        }

        result.RegisterObjectModification(table);

        var so = new SerializedObject(table);
        var list = so.FindProperty("relationships");

        void AddHostilePair(FactionDefinition a, FactionDefinition b)
        {
            int idx = list.arraySize;
            list.InsertArrayElementAtIndex(idx);
            var elem = list.GetArrayElementAtIndex(idx);
            elem.FindPropertyRelative("factionA").objectReferenceValue = a;
            elem.FindPropertyRelative("factionB").objectReferenceValue = b;
            elem.FindPropertyRelative("relationship").enumValueIndex = (int)FactionRelationship.Hostile;
        }

        AddHostilePair(red, blue);
        AddHostilePair(red, green);
        AddHostilePair(red, yellow);
        AddHostilePair(blue, green);
        AddHostilePair(blue, yellow);
        AddHostilePair(green, yellow);

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();

        result.Log("Added 6 cross-team Hostile relationship pairs to GlobalRelationships.");
    }
}
```

- [ ] **Step 2: Verify the asset file contains 6 new entries**

Read `Assets/Prefabs/agents/factions/GlobalRelationships.asset` and confirm the `relationships` list now has 6 entries referencing the 4 team faction GUIDs with `relationship: 2` (Hostile is index 2 in the `FactionRelationship` enum: `Neutral=0, Allied=1, Hostile=2`).

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/agents/factions/GlobalRelationships.asset
git commit -m "feat: mark all 4 team factions mutually hostile in GlobalRelationships"
```

---

## Section C: DeathmatchBot prefab

### Task C1: Duplicate `PatrolRobot` into `DeathmatchBot`

**Files:**
- Create: `Assets/Prefabs/agents/prefab/RobotPatrol/DeathmatchBot.prefab`

- [ ] **Step 1: Duplicate the source prefab via Unity MCP**

Use `mcp__unity-mcp__Unity_ManageAsset` with `Action: "Duplicate"`, `Path: "Assets/Prefabs/agents/prefab/RobotPatrol/PatrolRobot.prefab"`, `Destination: "Assets/Prefabs/agents/prefab/RobotPatrol/DeathmatchBot.prefab"`.

- [ ] **Step 2: Verify the duplicate exists and inspect its components**

Use `mcp__unity-mcp__Unity_ManageAsset` with `Action: "GetComponents"`, `Path: "Assets/Prefabs/agents/prefab/RobotPatrol/DeathmatchBot.prefab"`. Confirm it lists `AgentRangedCombatModule`, `WanderModule`, `PatrolModule`, `HerdModule`, `ChaseModule`, `CloseCombatModule`, `HealthComponent`, `EntityFaction`, matching the source prefab.

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/agents/prefab/RobotPatrol/DeathmatchBot.prefab*
git commit -m "feat: duplicate PatrolRobot as base for DeathmatchBot"
```

### Task C2: Reconfigure `DeathmatchBot` modules for arena combat

**Files:**
- Modify: `Assets/Prefabs/agents/prefab/RobotPatrol/DeathmatchBot.prefab`

Per the design spec §4: enable `AgentRangedCombatModule` (disabled by default on `PatrolRobot`), disable `WanderModule`/`PatrolModule`/`HerdModule` (irrelevant/counterproductive in a small arena — wandering pulls bots away from combat, patrol/herd assume open-world routes). `ChaseModule` and `CloseCombatModule` stay enabled as-is.

- [ ] **Step 1: Open the prefab for editing and toggle module `m_Enabled` via Unity MCP**

Use `mcp__unity-mcp__Unity_ManageGameObject` with `action: "set_component_property"` on the prefab's root GameObject (open it via `mcp__unity-mcp__Unity_ManageAsset` `Action: "GetInfo"` first if needed to get the correct target path/instance), setting:
- `AgentRangedCombatModule` → `enabled: true`
- `WanderModule` → `enabled: false`
- `PatrolModule` → `enabled: false`
- `HerdModule` → `enabled: false`

Note `PatrolModule` and `HerdModule` are already disabled by default on `PatrolRobot` per the research (`disabled by default, priority 14/15`) — this step is a no-op for those two but is included to make the intended state explicit and not depend on the source prefab's current defaults.

- [ ] **Step 2: Verify via `GetComponents`**

Use `mcp__unity-mcp__Unity_ManageAsset` with `Action: "GetComponents"`, `Path: "Assets/Prefabs/agents/prefab/RobotPatrol/DeathmatchBot.prefab"` and confirm the `enabled` state of each listed module matches Step 1.

- [ ] **Step 3: Manual verification**

Drag `DeathmatchBot.prefab` into a scratch/empty area of `MinigameArena.unity` in the Editor (not saved), enter Play Mode, and confirm no console errors fire from the enabled `AgentRangedCombatModule` (e.g. missing `weapon`/`fireProfile`/`aimProfile` references — `PatrolRobot`'s existing assignments for these fields should already be present and carry over from the duplicate). Remove the scratch instance after checking; do not save the scene at this point (spawning happens via `MatchManager` in Section F, not via manual scene placement).

- [ ] **Step 4: Commit**

```bash
git add Assets/Prefabs/agents/prefab/RobotPatrol/DeathmatchBot.prefab
git commit -m "feat: enable ranged combat and disable wander/patrol/herd on DeathmatchBot"
```

---

## Section D: Player weapon + faction

### Task D1: Add `EntityFaction` to the player prefab

**Files:**
- Modify: `Assets/Prefabs/Player/PlayerCharacterNetworked.prefab`

- [ ] **Step 1: Add the component via Unity MCP**

Use `mcp__unity-mcp__Unity_ManageGameObject` with `action: "add_component"`, `target` set to the prefab's root, `component_name: "EntityFaction"`. Leave `faction`/`relationshipTable` unassigned in the Inspector — `MatchManager` assigns `faction` at spawn time via `SetFaction` (Task B1), and `relationshipTable` should point at the same `GlobalRelationships` asset used everywhere else, set once here as a default:

Use `mcp__unity-mcp__Unity_ManageGameObject` with `action: "set_component_property"`, `component_properties: {"EntityFaction": {"relationshipTable": "Assets/Prefabs/agents/factions/GlobalRelationships.asset"}}`.

- [ ] **Step 2: Verify via `GetComponents`**

Confirm `EntityFaction` is now listed on `Assets/Prefabs/Player/PlayerCharacterNetworked.prefab` with `relationshipTable` assigned and `faction` empty.

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/Player/PlayerCharacterNetworked.prefab
git commit -m "feat: add EntityFaction to player prefab for match targeting"
```

### Task D2: Create `PlayerRangedCombat` networked fire component

**Files:**
- Create: `Assets/Scripts/Player/PlayerRangedCombat.cs`

Standalone component, not routed through the `UsableItem`/`EquipmentController` inventory system — that system (`Weapon : UsableItem`) is a separate, non-networked, non-faction-aware hitscan/magazine framework built for general item use, not deathmatch combat. Reusing `AgentWeaponDefinition`/`AgentProjectile` (as the design spec calls for) keeps damage/friendly-fire consistent with bots and gets `AgentProjectile`'s existing ally-damage-prevention for free. Server-authoritative: the client-side fire input triggers an RPC, the server instantiates and inits the projectile (matching how `NetworkedHealthComponent` keeps the server as the source of truth for health).

- [ ] **Step 1: Create the script via Unity MCP**

```csharp
using Unity.Netcode;
using UnityEngine;

// Player-side equivalent of AgentRangedCombatModule's FireOne(), reusing the same
// AgentWeaponDefinition/AgentProjectile pair bots use so damage and friendly-fire
// behavior (AgentProjectile.IsAlliedWith check) are identical for players and bots
// in a mixed deathmatch. Server-authoritative: firing is requested via RPC and the
// projectile is spawned only on the server, matching NetworkedHealthComponent's
// server-owns-truth pattern elsewhere on this prefab.
[RequireComponent(typeof(NetworkObject))]
public class PlayerRangedCombat : NetworkBehaviour
{
    [SerializeField] private AgentWeaponDefinition weapon;
    [SerializeField] private AimProvider aimProvider;
    [SerializeField] private Transform muzzle;
    [SerializeField] private float fireCooldown = 0.3f;

    private float nextFireTime;

    public void TryFire()
    {
        if (!IsOwner) return;
        if (Time.time < nextFireTime) return;
        if (weapon == null || aimProvider == null || muzzle == null) return;

        nextFireTime = Time.time + fireCooldown;

        Vector3 aimDirection = aimProvider.GetAimRay().direction;
        FireServerRpc(muzzle.position, aimDirection);
    }

    [Rpc(SendTo.Server)]
    private void FireServerRpc(Vector3 spawnPosition, Vector3 aimDirection)
    {
        if (weapon == null || weapon.projectilePrefab == null) return;

        GameObject projectile = Instantiate(weapon.projectilePrefab, spawnPosition, Quaternion.LookRotation(aimDirection));

        AgentProjectile agentProjectile = projectile.GetComponent<AgentProjectile>();
        if (agentProjectile != null)
            agentProjectile.Init(weapon.damagePerHit, null, gameObject);

        Rigidbody rb = projectile.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = aimDirection * weapon.projectileSpeed;

        NetworkObject projectileNetObj = projectile.GetComponent<NetworkObject>();
        if (projectileNetObj != null)
            projectileNetObj.Spawn();
    }
}
```

Use `mcp__unity-mcp__Unity_CreateScript` with `Path: "Assets/Scripts/Player/PlayerRangedCombat.cs"`, `ScriptType: "MonoBehaviour"`, and the contents above.

- [ ] **Step 2: Verify compile**

Check `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: no errors. If `AgentProjectile`'s prefab has no `NetworkObject` component, the `projectileNetObj.Spawn()` call will silently no-op via the null check — confirmed acceptable for now since Task D4 verifies actual networked firing manually and will surface this if it's a real problem.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Player/PlayerRangedCombat.cs
git commit -m "feat: add server-authoritative player ranged combat component"
```

### Task D3: Check whether the robot projectile prefab has a `NetworkObject`, add one if missing

**Files:**
- Modify (conditionally): the projectile prefab referenced by `Assets/Prefabs/agents/weapons/WPN_RobotPistol.asset` (`projectilePrefab` field, guid `fc20b127768f544edb547eded0b3b078` — resolve the actual asset path via search first)

Bots fire this projectile today without any networking at all (single-player-style `Instantiate` in `AgentRangedCombatModule.FireOne`, called locally on whichever peer's AI tick runs it) — it may have never needed a `NetworkObject`. For the player's server-authoritative fire path in Task D2 to actually replicate the projectile to all clients, the prefab needs one.

- [ ] **Step 1: Resolve the projectile prefab's asset path**

Use `mcp__unity-mcp__Unity_ManageAsset` with `Action: "Search"`, `Path: "Assets/"`, `SearchPattern: "*.prefab"`, and cross-reference the guid `fc20b127768f544edb547eded0b3b078` (search result metadata should include guids; alternatively use `mcp__unity-mcp__Unity_FindProjectAssets` with a query like "robot pistol projectile bullet").

- [ ] **Step 2: Inspect its components**

Use `mcp__unity-mcp__Unity_ManageAsset` `Action: "GetComponents"` on the resolved path. Check for `NetworkObject`.

- [ ] **Step 3: If missing, add `NetworkObject` and `NetworkTransform`**

Use `mcp__unity-mcp__Unity_ManageGameObject` `action: "add_component"` with `component_name: "Unity.Netcode.NetworkObject"`, then again with `component_name: "Unity.Netcode.Components.NetworkTransform"` (matching the pattern already used on `PlayerCharacterNetworked.prefab`, which has both). If a `NetworkObject` is already present, skip this step and note it in the commit message as a no-op check.

- [ ] **Step 4: Register the prefab in the `NetworkManager`'s network prefabs list if newly added**

If Step 3 added a `NetworkObject`, the prefab must be registered so clients know how to instantiate it on spawn. Open `Assets/Prefabs/General/NetworkManager.prefab` (already modified in this session's git status — check current uncommitted state first with `git diff --stat Assets/Prefabs/General/NetworkManager.prefab` before touching it, to avoid clobbering unrelated in-progress changes) and add the projectile prefab to `NetworkConfig.Prefabs` via `mcp__unity-mcp__Unity_ManageGameObject` `set_component_property` on the `NetworkManager` component, or via `mcp__unity-mcp__Unity_RunCommand` calling `NetworkManager.NetworkConfig.Prefabs.Add(...)` at edit time and saving.

- [ ] **Step 5: Manual verification**

Enter Play Mode as host, manually trigger a bot's ranged attack (e.g. via existing debug means, or defer full verification to Task F's end-to-end match test), confirm no `NetworkObject`-related console errors.

- [ ] **Step 6: Commit (only if Step 3 made changes)**

```bash
git add -A
git commit -m "feat: make robot projectile prefab network-spawnable"
```

If no changes were needed, skip the commit and note in the task tracker that the projectile was already networked.

### Task D4: Wire `PlayerRangedCombat` into the player prefab and fire input

**Files:**
- Modify: `Assets/Prefabs/Player/PlayerCharacterNetworked.prefab`
- Modify: `Assets/Scripts/Player/PlayerController.cs:64-71` (`EnablePlayer`)

Reuses the existing `AimProvider` (already referenced in the research as present under `Player/`) and wires firing to `PlayerInputManager.OnUsePressed` — the same input event `EquipmentController.OnUse` already listens to for inventory items. Both listeners firing on `OnUsePressed` is acceptable: the deathmatch weapon is not going through the inventory/equip system, so there's no double-fire conflict, just two independent systems both reacting to "use pressed" (one manipulates equipped items, the other always attempts a match-weapon shot). This is intentionally simple for this plan's hardcoded-TDM scope; if this proves confusing in practice, a follow-up can gate it behind a "combat mode" flag — noted here rather than solved now, per YAGNI.

- [ ] **Step 1: Add `PlayerRangedCombat` component to the prefab**

Use `mcp__unity-mcp__Unity_ManageGameObject` `action: "add_component"`, `component_name: "PlayerRangedCombat"`, target the prefab root.

- [ ] **Step 2: Assign `weapon` and `aimProvider` fields**

Use `mcp__unity-mcp__Unity_ManageGameObject` `action: "set_component_property"`:
```json
{
  "PlayerRangedCombat": {
    "weapon": "Assets/Prefabs/agents/weapons/WPN_RobotPistol.asset",
    "aimProvider": {"find": "PlayerCharacterNetworked", "component": "AimProvider"}
  }
}
```
If `AimProvider` is not already present on the player prefab (confirm via `GetComponents` first — the earlier research noted it exists under `Player/AimProvider.cs` but did not confirm it's attached to this specific prefab), add it first with `action: "add_component"`, `component_name: "AimProvider"`, and assign its `playerCamera` field to the prefab's existing camera child object.

- [ ] **Step 3: Create and assign a muzzle transform**

Use `mcp__unity-mcp__Unity_ManageGameObject` `action: "create"` with `name: "Muzzle"`, `parent` set to the player prefab's camera transform (found via `GetComponents`/hierarchy inspection in Step 2), `position: [0, 0, 0.5]` (half a meter in front of the camera, matching `muzzleForwardOffset` conventions used on `AgentRangedCombatModule`). Then assign it: `component_properties: {"PlayerRangedCombat": {"muzzle": {"find": "Muzzle", "method": "by_name"}}}`.

- [ ] **Step 4: Wire fire input in `PlayerController.EnablePlayer`**

Read current state of `Assets/Scripts/Player/PlayerController.cs` to confirm line numbers, then use `mcp__unity-mcp__Unity_ApplyTextEdits` to change:

```csharp
    public void EnablePlayer()
    {
        Input.enabled = true;
        playerCamera.gameObject.SetActive(true);
        playerHUD.gameObject.SetActive(true);
        playerMovement.enabled = true;
        playerLook.enabled = true;
        damageFeedback.enabled = true;

        playerHealth.OnDeath += OnDeath;
    }
```
to:
```csharp
    public void EnablePlayer()
    {
        Input.enabled = true;
        playerCamera.gameObject.SetActive(true);
        playerHUD.gameObject.SetActive(true);
        playerMovement.enabled = true;
        playerLook.enabled = true;
        damageFeedback.enabled = true;

        playerHealth.OnDeath += OnDeath;

        var rangedCombat = GetComponent<PlayerRangedCombat>();
        if (rangedCombat != null)
            Input.OnUsePressed += rangedCombat.TryFire;
    }
```

And mirror the unsubscribe in `DisablePlayer()`:
```csharp
    public void DisablePlayer()
    {
        Input.enabled = false;
        playerCamera.gameObject.SetActive(false);
        playerHUD.gameObject.SetActive(false);
        playerMovement.enabled = false;
        playerLook.enabled = false;
        damageFeedback.enabled = false;

        playerHealth.OnDeath -= OnDeath;

        var rangedCombat = GetComponent<PlayerRangedCombat>();
        if (rangedCombat != null)
            Input.OnUsePressed -= rangedCombat.TryFire;
    }
```

- [ ] **Step 5: Verify compile**

Check `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: no errors.

- [ ] **Step 6: Manual verification**

Enter Play Mode (single-player host flow via `StartMinigame` — or a scratch scene with the player prefab placed directly if the full flow isn't wired yet at this point in the plan), press the "Use" input binding, confirm a projectile spawns from the muzzle position along the camera's forward direction with no console errors. Full hit-registration-against-a-bot verification happens in Task F's end-to-end test once bots exist in the scene.

- [ ] **Step 7: Commit**

```bash
git add Assets/Prefabs/Player/PlayerCharacterNetworked.prefab Assets/Scripts/Player/PlayerController.cs
git commit -m "feat: wire player ranged combat to fire input"
```

---

## Section E: Match logic (pure C#, EditMode-tested)

### Task E1: `TeamAssignment` — split shuffled spawn points into team blocks

**Files:**
- Create: `Assets/Scripts/Minigame/TeamAssignment.cs`
- Test: `Assets/Tests/EditMode/TeamAssignmentTests.cs`

Pure logic, no `MonoBehaviour`/Unity API dependency beyond `Vector3` — extracted so it's independently testable without a running scene, per Task A1's EditMode setup. `MatchManager` (Task F) calls this with real spawn point positions.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class TeamAssignmentTests
{
    [Test]
    public void SplitEvenly_TwoTeams_DividesPositionsInHalf()
    {
        var positions = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0),
            new Vector3(2, 0, 0), new Vector3(3, 0, 0),
        };

        var teams = TeamAssignment.SplitEvenly(positions, teamCount: 2);

        Assert.AreEqual(2, teams.Count);
        Assert.AreEqual(2, teams[0].Count);
        Assert.AreEqual(2, teams[1].Count);
    }

    [Test]
    public void SplitEvenly_UsesEveryPositionExactlyOnce()
    {
        var positions = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(2, 0, 0),
            new Vector3(3, 0, 0), new Vector3(4, 0, 0), new Vector3(5, 0, 0),
        };

        var teams = TeamAssignment.SplitEvenly(positions, teamCount: 2);

        var all = new List<Vector3>();
        foreach (var team in teams) all.AddRange(team);

        Assert.AreEqual(positions.Count, all.Count);
        foreach (var p in positions)
            Assert.Contains(p, all);
    }

    [Test]
    public void SplitEvenly_OddCount_DistributesRemainderToEarlierTeams()
    {
        var positions = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(2, 0, 0),
        };

        var teams = TeamAssignment.SplitEvenly(positions, teamCount: 2);

        Assert.AreEqual(2, teams[0].Count);
        Assert.AreEqual(1, teams[1].Count);
    }

    [Test]
    public void SplitEvenly_FewerPositionsThanTeams_SomeTeamsGetEmptyList()
    {
        var positions = new List<Vector3> { new Vector3(0, 0, 0) };

        var teams = TeamAssignment.SplitEvenly(positions, teamCount: 3);

        Assert.AreEqual(3, teams.Count);
        int nonEmpty = 0;
        foreach (var t in teams) if (t.Count > 0) nonEmpty++;
        Assert.AreEqual(1, nonEmpty);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Use `mcp__unity-mcp__Unity_RunCommand` to invoke `UnityEditor.TestTools.TestRunner.Api` for EditMode tests filtered to `TeamAssignmentTests` (or trigger via the Editor's Test Runner window if the MCP tooling exposes it more directly — check `mcp__unity-mcp__Unity_RunCommand`'s capability to drive `TestRunnerApi` first). Expected: FAIL / compile error, `TeamAssignment` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Collections.Generic;
using UnityEngine;

// Splits a shuffled list of spawn positions into `teamCount` roughly-even blocks.
// Caller is responsible for shuffling — this only divides, so results are
// deterministic given the same input order (kept separate from randomness for
// testability).
public static class TeamAssignment
{
    public static List<List<Vector3>> SplitEvenly(IReadOnlyList<Vector3> positions, int teamCount)
    {
        var teams = new List<List<Vector3>>(teamCount);
        for (int i = 0; i < teamCount; i++)
            teams.Add(new List<Vector3>());

        for (int i = 0; i < positions.Count; i++)
            teams[i % teamCount].Add(positions[i]);

        return teams;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Re-run the EditMode test filter from Step 2. Expected: PASS, all 4 tests green.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Minigame/TeamAssignment.cs Assets/Tests/EditMode/TeamAssignmentTests.cs
git commit -m "feat: add TeamAssignment spawn-splitting logic with tests"
```

### Task E2: `MatchWinEvaluator` — pure win-condition logic for the 3 TDM variants

**Files:**
- Create: `Assets/Scripts/Minigame/MatchWinEvaluator.cs`
- Test: `Assets/Tests/EditMode/MatchWinEvaluatorTests.cs`

Encodes the win-condition rules from the design spec §6 (Kill Target / Lives Per Player / Last Team Standing) as pure functions over simple state, independent of `HealthComponent`/Netcode so they're fully unit-testable.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;

public class MatchWinEvaluatorTests
{
    [Test]
    public void KillTarget_NoTeamReachedTarget_ReturnsNoWinner()
    {
        var kills = new System.Collections.Generic.Dictionary<int, int> { { 0, 3 }, { 1, 4 } };
        int? winner = MatchWinEvaluator.EvaluateKillTarget(kills, target: 5);
        Assert.IsNull(winner);
    }

    [Test]
    public void KillTarget_TeamReachedTarget_ReturnsThatTeam()
    {
        var kills = new System.Collections.Generic.Dictionary<int, int> { { 0, 3 }, { 1, 5 } };
        int? winner = MatchWinEvaluator.EvaluateKillTarget(kills, target: 5);
        Assert.AreEqual(1, winner);
    }

    [Test]
    public void KillTarget_BothTeamsReachedTarget_ReturnsHigherScoringTeam()
    {
        var kills = new System.Collections.Generic.Dictionary<int, int> { { 0, 6 }, { 1, 5 } };
        int? winner = MatchWinEvaluator.EvaluateKillTarget(kills, target: 5);
        Assert.AreEqual(0, winner);
    }

    [Test]
    public void LivesRemaining_OneTeamAtZero_ReturnsOtherTeam()
    {
        var lives = new System.Collections.Generic.Dictionary<int, int> { { 0, 0 }, { 1, 3 } };
        int? winner = MatchWinEvaluator.EvaluateLivesExhausted(lives);
        Assert.AreEqual(1, winner);
    }

    [Test]
    public void LivesRemaining_BothTeamsHaveLives_ReturnsNoWinner()
    {
        var lives = new System.Collections.Generic.Dictionary<int, int> { { 0, 1 }, { 1, 2 } };
        int? winner = MatchWinEvaluator.EvaluateLivesExhausted(lives);
        Assert.IsNull(winner);
    }

    [Test]
    public void LivesRemaining_AllTeamsAtZero_ReturnsNull_NoWinnerDraw()
    {
        var lives = new System.Collections.Generic.Dictionary<int, int> { { 0, 0 }, { 1, 0 } };
        int? winner = MatchWinEvaluator.EvaluateLivesExhausted(lives);
        Assert.IsNull(winner);
    }

    [Test]
    public void LastStanding_OneTeamHasLivingMembers_ReturnsThatTeam()
    {
        var livingCounts = new System.Collections.Generic.Dictionary<int, int> { { 0, 0 }, { 1, 2 } };
        int? winner = MatchWinEvaluator.EvaluateLastStanding(livingCounts);
        Assert.AreEqual(1, winner);
    }

    [Test]
    public void LastStanding_MultipleTeamsHaveLivingMembers_ReturnsNoWinner()
    {
        var livingCounts = new System.Collections.Generic.Dictionary<int, int> { { 0, 1 }, { 1, 2 } };
        int? winner = MatchWinEvaluator.EvaluateLastStanding(livingCounts);
        Assert.IsNull(winner);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run the EditMode test filter for `MatchWinEvaluatorTests`. Expected: FAIL, `MatchWinEvaluator` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Collections.Generic;

// Pure win-condition evaluation for Team Deathmatch's 3 configurable variants
// (design spec §6). Takes plain per-team-index dictionaries so MatchManager can
// feed it real tracked state without this class depending on Netcode/HealthComponent.
// Returns the winning team index, or null if the match should continue.
public static class MatchWinEvaluator
{
    public static int? EvaluateKillTarget(IReadOnlyDictionary<int, int> killsByTeam, int target)
    {
        int? best = null;
        int bestKills = -1;
        foreach (var pair in killsByTeam)
        {
            if (pair.Value < target) continue;
            if (pair.Value > bestKills)
            {
                bestKills = pair.Value;
                best = pair.Key;
            }
        }
        return best;
    }

    // A team is eliminated when its shared life pool hits 0. Winner is the last
    // team with lives remaining. If every team is simultaneously at 0 (edge case:
    // final exchange kills the last member of both teams at once), it's a draw —
    // returns null rather than an arbitrary team.
    public static int? EvaluateLivesExhausted(IReadOnlyDictionary<int, int> livesByTeam)
    {
        int teamsWithLives = 0;
        int? candidate = null;
        foreach (var pair in livesByTeam)
        {
            if (pair.Value > 0)
            {
                teamsWithLives++;
                candidate = pair.Key;
            }
        }
        return teamsWithLives == 1 ? candidate : null;
    }

    public static int? EvaluateLastStanding(IReadOnlyDictionary<int, int> livingCountByTeam)
    {
        int teamsAlive = 0;
        int? candidate = null;
        foreach (var pair in livingCountByTeam)
        {
            if (pair.Value > 0)
            {
                teamsAlive++;
                candidate = pair.Key;
            }
        }
        return teamsAlive == 1 ? candidate : null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Re-run the EditMode test filter. Expected: PASS, all 8 tests green.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Minigame/MatchWinEvaluator.cs Assets/Tests/EditMode/MatchWinEvaluatorTests.cs
git commit -m "feat: add MatchWinEvaluator win-condition logic with tests"
```

---

## Section F: `MatchManager`

### Task F1: Create `MatchSettings` (hardcoded for this plan)

**Files:**
- Create: `Assets/Scripts/Minigame/MatchSettings.cs`

Per the design spec §1, `MatchSettings` is the static config object the (not-yet-built) config UI will populate. This plan hardcodes its values via Inspector-settable defaults on `MatchManager` directly instead of routing through a config screen — but the type is introduced now, matching the field shape the design spec calls for, so the follow-up plan only has to add a UI that writes to it rather than inventing it fresh.

- [ ] **Step 1: Create the script via Unity MCP**

```csharp
// Host-side match configuration, set before StartHost() and read once by
// MatchManager after the arena scene loads (mirrors the existing
// NetworkGameManager.PendingSceneNameToWaitFor static-field pattern used by
// the minigame scene-load flow). Not networked — the host is always the one
// who configures and starts the match.
//
// This plan (core + Team Deathmatch) sets these via MatchManager's own
// Inspector-exposed defaults rather than a config screen; the values still
// flow through this class so a future config-UI plan only adds a writer,
// not a new settings type.
public enum GameMode { TeamDeathmatch, FreeForAll, BattleRoyale }
public enum WinCondition { KillTarget, LivesPerPlayer, LastStanding }

public static class MatchSettings
{
    public static GameMode Mode = GameMode.TeamDeathmatch;
    public static int AllyCount = 3;
    public static int EnemyCount = 4;
    public static WinCondition Condition = WinCondition.LastStanding;
    public static int KillTargetCount = 10;
    public static int LivesPerPlayerCount = 3;
}
```

Use `mcp__unity-mcp__Unity_CreateScript` with `Path: "Assets/Scripts/Minigame/MatchSettings.cs"`.

- [ ] **Step 2: Verify compile**

Check console logs for errors. Expected: none.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Minigame/MatchSettings.cs
git commit -m "feat: add MatchSettings config type (hardcoded defaults for TDM plan)"
```

### Task F2: Create `MatchManager` — spawning

**Files:**
- Create: `Assets/Scripts/Minigame/MatchManager.cs`

Server-authoritative `NetworkBehaviour`, placed in `MinigameArena.unity` (Task G). Handles bot instantiation, faction assignment, and spawn-point collection using `TeamAssignment` (Task E1). Player faction assignment and win/respawn logic are added in Tasks F3-F4 to keep this task reviewable.

- [ ] **Step 1: Create the script via Unity MCP**

```csharp
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

// Server-authoritative match orchestrator for the minigame arena. Owns bot
// spawning, faction assignment, and (in later tasks) win detection and
// respawn/elimination. Lives in MinigameArena.unity, separate from
// SpawnManager/SpawnPoint (Assets/Scripts/Game/) which remain untouched —
// this does its own independent spawn-point collection for match entities
// per design spec §3.
public class MatchManager : NetworkBehaviour
{
    [Header("Team factions (index 0 = allies, index 1 = enemies)")]
    [SerializeField] private FactionDefinition allyFaction;
    [SerializeField] private FactionDefinition enemyFaction;
    [SerializeField] private FactionRelationshipTable relationshipTable;

    [Header("Bots")]
    [SerializeField] private GameObject deathmatchBotPrefab;

    private readonly List<GameObject> spawnedBots = new();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        SpawnBots();
    }

    private List<Vector3> CollectSpawnPositions()
    {
        var points = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var positions = new List<Vector3>(points.Length);
        foreach (var p in points)
            positions.Add(p.GetSpawnPoint());

        // Fisher-Yates shuffle so team-block assignment (TeamAssignment.SplitEvenly)
        // doesn't correlate with scene placement order.
        for (int i = positions.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (positions[i], positions[j]) = (positions[j], positions[i]);
        }

        return positions;
    }

    private void SpawnBots()
    {
        var positions = CollectSpawnPositions();
        var teams = TeamAssignment.SplitEvenly(positions, teamCount: 2);

        SpawnTeam(teams[0], allyFaction, MatchSettings.AllyCount);
        SpawnTeam(teams[1], enemyFaction, MatchSettings.EnemyCount);
    }

    private void SpawnTeam(List<Vector3> teamPositions, FactionDefinition faction, int count)
    {
        int spawnCount = Mathf.Min(count, teamPositions.Count);
        for (int i = 0; i < spawnCount; i++)
        {
            GameObject bot = Instantiate(deathmatchBotPrefab, teamPositions[i], Quaternion.identity);

            var entityFaction = bot.GetComponent<EntityFaction>();
            if (entityFaction != null)
                entityFaction.SetFaction(faction, relationshipTable);

            var netObj = bot.GetComponent<NetworkObject>();
            if (netObj != null)
                netObj.Spawn();

            spawnedBots.Add(bot);
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Check console logs. Expected: no errors. If `DeathmatchBot.prefab` lacks a `NetworkObject` (it's a duplicate of `PatrolRobot`, which per the research already has `SceneTracked` but was not confirmed to have `NetworkObject`), this is caught in Step 3's manual test, not here — compile succeeds either way since `netObj` is null-checked.

- [ ] **Step 3: Manual verification (placeholder scene)**

This step is superseded by the full end-to-end check in Task G — skip standalone verification here since `MatchManager` needs to be placed in a scene with real `SpawnPoint`s and a real host session to test meaningfully, which Task G sets up.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Minigame/MatchManager.cs
git commit -m "feat: add MatchManager with team-based bot spawning"
```

### Task F3: `MatchManager` — track kills/lives, wire respawn and elimination

**Files:**
- Modify: `Assets/Scripts/Minigame/MatchManager.cs`

Extends `SpawnTeam` to subscribe to each entity's `HealthComponent.OnDeath`, tracks per-team kill count and per-entity lives, and implements the respawn-vs-permanent-elimination branch per win condition (design spec §6). Player entities are included via a new `RegisterPlayer` method called once the player prefab is faction-assigned (wired in Task F4).

- [ ] **Step 1: Read current file to confirm exact content before editing**

Confirm `Assets/Scripts/Minigame/MatchManager.cs` matches Task F2's Step 1 output.

- [ ] **Step 2: Apply the extension via `Unity_ApplyTextEdits`**

Replace the full file contents with:

```csharp
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

// Server-authoritative match orchestrator for the minigame arena. Owns bot
// spawning, faction assignment, kill/lives tracking, and win detection. Lives
// in MinigameArena.unity, separate from SpawnManager/SpawnPoint
// (Assets/Scripts/Game/), which remain untouched — this does its own
// independent spawn-point collection for match entities per design spec §3.
public class MatchManager : NetworkBehaviour
{
    private const int AllyTeam = 0;
    private const int EnemyTeam = 1;

    [Header("Team factions (index 0 = allies, index 1 = enemies)")]
    [SerializeField] private FactionDefinition allyFaction;
    [SerializeField] private FactionDefinition enemyFaction;
    [SerializeField] private FactionRelationshipTable relationshipTable;

    [Header("Bots")]
    [SerializeField] private GameObject deathmatchBotPrefab;

    [Header("Respawn")]
    [SerializeField] private float respawnDelay = 3f;

    private readonly List<GameObject> spawnedEntities = new();
    private readonly Dictionary<GameObject, int> entityTeam = new();
    private readonly Dictionary<int, int> killsByTeam = new() { { AllyTeam, 0 }, { EnemyTeam, 0 } };
    private readonly Dictionary<int, int> livesByEntity = new();
    private readonly Dictionary<int, int> livesByTeam = new() { { AllyTeam, 0 }, { EnemyTeam, 0 } };

    private List<Vector3> allSpawnPositions;
    private bool matchEnded;

    public event System.Action<int> OnMatchEnded; // winning team index, or -1 for draw

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        allSpawnPositions = CollectSpawnPositions();
        SpawnBots();
    }

    private List<Vector3> CollectSpawnPositions()
    {
        var points = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var positions = new List<Vector3>(points.Length);
        foreach (var p in points)
            positions.Add(p.GetSpawnPoint());

        for (int i = positions.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (positions[i], positions[j]) = (positions[j], positions[i]);
        }

        return positions;
    }

    private void SpawnBots()
    {
        var teams = TeamAssignment.SplitEvenly(allSpawnPositions, teamCount: 2);

        SpawnTeam(teams[0], AllyTeam, allyFaction, MatchSettings.AllyCount);
        SpawnTeam(teams[1], EnemyTeam, enemyFaction, MatchSettings.EnemyCount);
    }

    private void SpawnTeam(List<Vector3> teamPositions, int teamIndex, FactionDefinition faction, int count)
    {
        int spawnCount = Mathf.Min(count, teamPositions.Count);
        for (int i = 0; i < spawnCount; i++)
        {
            GameObject bot = Instantiate(deathmatchBotPrefab, teamPositions[i], Quaternion.identity);
            RegisterEntity(bot, teamIndex, faction);

            var netObj = bot.GetComponent<NetworkObject>();
            if (netObj != null)
                netObj.Spawn();
        }
    }

    // Called for the bots spawned above, and externally by whatever assigns the
    // player(s) to a team (Task F4) once their EntityFaction is set.
    public void RegisterEntity(GameObject entity, int teamIndex, FactionDefinition faction)
    {
        var entityFaction = entity.GetComponent<EntityFaction>();
        if (entityFaction != null)
            entityFaction.SetFaction(faction, relationshipTable);

        spawnedEntities.Add(entity);
        entityTeam[entity] = teamIndex;
        livesByEntity[entity.GetInstanceID()] = MatchSettings.LivesPerPlayerCount;
        livesByTeam[teamIndex] = livesByTeam.GetValueOrDefault(teamIndex) + MatchSettings.LivesPerPlayerCount;

        var health = entity.GetComponent<HealthComponent>();
        if (health != null)
            health.OnDeath += () => HandleDeath(entity, teamIndex);
    }

    private void HandleDeath(GameObject entity, int teamIndex)
    {
        if (matchEnded) return;

        int killerTeam = teamIndex == AllyTeam ? EnemyTeam : AllyTeam;
        killsByTeam[killerTeam] = killsByTeam.GetValueOrDefault(killerTeam) + 1;

        bool shouldRespawn = MatchSettings.Condition != WinCondition.LastStanding;

        if (shouldRespawn)
        {
            int entityId = entity.GetInstanceID();
            livesByEntity[entityId] = livesByEntity.GetValueOrDefault(entityId) - 1;

            if (MatchSettings.Condition == WinCondition.LivesPerPlayer)
                livesByTeam[teamIndex] = Mathf.Max(0, livesByTeam.GetValueOrDefault(teamIndex) - 1);

            bool outOfLives = MatchSettings.Condition == WinCondition.LivesPerPlayer && livesByEntity[entityId] <= 0;

            if (!outOfLives)
                Invoke(nameof(NoOpRespawnScheduler), 0f); // see RespawnEntity below for the real scheduling call
        }

        CheckWinCondition();
    }

    // Kept as a distinct method (rather than inlining Invoke's target) so respawn
    // scheduling can pass the specific entity through — Invoke only supports
    // parameterless methods, so entity-specific respawn uses a coroutine instead.
    private void NoOpRespawnScheduler() { }

    private System.Collections.IEnumerator RespawnEntityAfterDelay(GameObject entity, int teamIndex)
    {
        yield return new WaitForSeconds(respawnDelay);
        if (entity == null) yield break;

        var teams = TeamAssignment.SplitEvenly(allSpawnPositions, teamCount: 2);
        var teamPositions = teams[teamIndex];
        Vector3 respawnPos = teamPositions.Count > 0
            ? teamPositions[Random.Range(0, teamPositions.Count)]
            : entity.transform.position;

        entity.transform.position = respawnPos;
        entity.SetActive(true);

        var health = entity.GetComponent<HealthComponent>();
        if (health != null)
            health.Heal(health.GetMaxHealth);

        var agentController = entity.GetComponent<AgentController>();
        if (agentController != null)
            agentController.enabled = true;
    }

    private void CheckWinCondition()
    {
        if (matchEnded) return;

        int? winner = MatchSettings.Condition switch
        {
            WinCondition.KillTarget => MatchWinEvaluator.EvaluateKillTarget(killsByTeam, MatchSettings.KillTargetCount),
            WinCondition.LivesPerPlayer => MatchWinEvaluator.EvaluateLivesExhausted(livesByTeam),
            WinCondition.LastStanding => MatchWinEvaluator.EvaluateLastStanding(CountLivingByTeam()),
            _ => null,
        };

        if (winner.HasValue)
            EndMatch(winner.Value);
    }

    private Dictionary<int, int> CountLivingByTeam()
    {
        var counts = new Dictionary<int, int> { { AllyTeam, 0 }, { EnemyTeam, 0 } };
        foreach (var entity in spawnedEntities)
        {
            if (entity == null || !entity.activeInHierarchy) continue;
            var health = entity.GetComponent<HealthComponent>();
            if (health != null && !health.Alive) continue;
            counts[entityTeam[entity]] = counts.GetValueOrDefault(entityTeam[entity]) + 1;
        }
        return counts;
    }

    private void EndMatch(int winningTeam)
    {
        matchEnded = true;
        OnMatchEnded?.Invoke(winningTeam);
        BroadcastMatchEndedClientRpc(winningTeam);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void BroadcastMatchEndedClientRpc(int winningTeam)
    {
        OnMatchEnded?.Invoke(winningTeam);
    }
}
```

**Correction before proceeding:** the `NoOpRespawnScheduler`/`Invoke` placeholder above is dead code left over from an intermediate draft — replace the body of `HandleDeath`'s respawn branch to call the coroutine directly instead. Apply this fix as part of Step 2, not as a separate step:

```csharp
            if (!outOfLives)
                StartCoroutine(RespawnEntityAfterDelay(entity, teamIndex));
```

replacing the `if (!outOfLives) Invoke(nameof(NoOpRespawnScheduler), 0f);` line, and delete the `NoOpRespawnScheduler` method entirely. The final `HandleDeath` method reads:

```csharp
    private void HandleDeath(GameObject entity, int teamIndex)
    {
        if (matchEnded) return;

        int killerTeam = teamIndex == AllyTeam ? EnemyTeam : AllyTeam;
        killsByTeam[killerTeam] = killsByTeam.GetValueOrDefault(killerTeam) + 1;

        bool shouldRespawn = MatchSettings.Condition != WinCondition.LastStanding;

        if (shouldRespawn)
        {
            int entityId = entity.GetInstanceID();
            livesByEntity[entityId] = livesByEntity.GetValueOrDefault(entityId) - 1;

            if (MatchSettings.Condition == WinCondition.LivesPerPlayer)
                livesByTeam[teamIndex] = Mathf.Max(0, livesByTeam.GetValueOrDefault(teamIndex) - 1);

            bool outOfLives = MatchSettings.Condition == WinCondition.LivesPerPlayer && livesByEntity[entityId] <= 0;

            if (!outOfLives)
                StartCoroutine(RespawnEntityAfterDelay(entity, teamIndex));
        }

        CheckWinCondition();
    }
```

- [ ] **Step 3: Verify compile**

Check console logs. Expected: no errors, no unused-method warnings for `NoOpRespawnScheduler` (it should no longer exist in the file).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Minigame/MatchManager.cs
git commit -m "feat: add kill/lives tracking, respawn, and win detection to MatchManager"
```

### Task F4: Assign player faction and register with `MatchManager` on spawn

**Files:**
- Modify: `Assets/Scripts/Game/SpawnManager.cs:102-110` (`SpawnPlayerForClient`)

`SpawnManager.SpawnPlayerForClient` is the single place every player instantiation already flows through (both `RequestSpawnServerRpc` and the initial `NetworkGameManager.SpawnWhenReady` call use it). Hooking team assignment here means no changes are needed to `NetworkGameManager` or the scene-load flow at all. This hardcodes every human player onto the ally team for this plan (multi-human team-select is out of scope per the design spec's "Open items" — deferred to the follow-up plan alongside the config UI).

- [ ] **Step 1: Read current state to confirm line numbers**

Confirm `Assets/Scripts/Game/SpawnManager.cs` lines 102-110 still read:

```csharp
    public void SpawnPlayerForClient(ulong clientId)
    {

        Vector3 spawnPosition = GetSpawnPoint();
        GameObject playerObj = Instantiate(networkPlayerPrefab, spawnPosition, Quaternion.identity);

        // Spawn it specifically as the Player Object for that ID
        playerObj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }
```

- [ ] **Step 2: Apply the edit via `Unity_ApplyTextEdits`**

```csharp
    public void SpawnPlayerForClient(ulong clientId)
    {

        Vector3 spawnPosition = GetSpawnPoint();
        GameObject playerObj = Instantiate(networkPlayerPrefab, spawnPosition, Quaternion.identity);

        // Spawn it specifically as the Player Object for that ID
        playerObj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);

        // Every human player joins the ally team for this plan — multi-human
        // team-select is deferred to the follow-up config-UI plan. Only runs
        // when a MatchManager is present in the loaded scene (i.e. the
        // minigame flow), so the main game's plain spawn path is unaffected.
        var matchManager = FindFirstObjectByType<MatchManager>();
        if (matchManager != null)
            matchManager.RegisterPlayerEntity(playerObj);
    }
```

- [ ] **Step 3: Add `RegisterPlayerEntity` to `MatchManager`**

`RegisterEntity` (Task F3) takes a `teamIndex` and `FactionDefinition` directly — add a thin player-specific wrapper so `SpawnManager` doesn't need to know `MatchManager`'s internal team-index constants or which faction fields are ally vs enemy:

```csharp
    // Thin wrapper so callers outside MatchManager (SpawnManager) don't need to
    // know the AllyTeam/EnemyTeam constants or which serialized faction field is
    // which — every human player joins the ally team for this plan.
    public void RegisterPlayerEntity(GameObject player)
    {
        RegisterEntity(player, AllyTeam, allyFaction);
    }
```

Insert this in `MatchManager.cs` immediately after the existing `RegisterEntity` method, via `Unity_ApplyTextEdits`.

- [ ] **Step 4: Verify compile**

Check console logs. Expected: no errors.

- [ ] **Step 5: Manual verification**

Deferred to Task G's end-to-end test — team assignment for a spawned player can only be meaningfully verified once `MatchManager` is actually placed in `MinigameArena.unity`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Game/SpawnManager.cs Assets/Scripts/Minigame/MatchManager.cs
git commit -m "feat: assign spawned players to the ally team via MatchManager"
```

---

## Section G: Scene wiring + result screen + spectator camera

### Task G1: Place ~16 `SpawnPoint`s in `MinigameArena.unity`

**Files:**
- Modify: `Assets/Scenes/Minigames/MinigameArena.unity`

Per design spec §3: untagged, evenly-spread `SpawnPoint`s (existing component, unmodified) so `MatchManager` has enough positions to place up to 4 allies + 4 enemies (this plan's hardcoded max) with headroom for the follow-up plan's larger FFA/BR counts.

- [ ] **Step 1: Open the scene and inspect existing content**

Use `mcp__unity-mcp__Unity_ManageGameObject` `action: "find"`, `search_term: "SpawnPoint"`, `find_all: true` with the scene open, to see what spawn points (if any) already exist per the prior minigame spec's "at least one SpawnPoint" requirement.

- [ ] **Step 2: Create 16 `SpawnPoint` GameObjects in two clusters**

Use `mcp__unity-mcp__Unity_ManageGameObject` `action: "create"` 16 times, `name: "SpawnPoint_Ally_<n>"` (×8) and `name: "SpawnPoint_Enemy_<n>"` (×8), `components_to_add: ["SpawnPoint"]`. Position the 8 "Ally" points clustered at one side of the arena's existing terrain footprint and the 8 "Enemy" points at the opposite side, with enough internal spread (e.g. 5-8m between points within a cluster) that `SpawnPoint`'s own radius-based placement (`spawnRadius`) doesn't overlap adjacent spawn points. Determine actual world-space coordinates by first reading the arena's terrain bounds via `mcp__unity-mcp__Unity_SceneView_Capture2DScene` or `Unity_ManageGameObject find_all` on the copied terrain objects, since exact placement depends on where the 4-chunk terrain copy (from the prior spec) actually ended up — do not guess fixed coordinates without checking the scene first.

Naming with "Ally"/"Enemy" suffixes is for scene-authoring legibility only — `MatchManager` treats all `SpawnPoint`s as one undifferentiated pool per design spec §3 and re-derives the split via shuffling, it does not read these names.

- [ ] **Step 3: Verify count and rough distribution**

Use `mcp__unity-mcp__Unity_ManageGameObject` `action: "find"`, `search_term: "SpawnPoint"`, `find_all: true`. Expected: 16 results (plus any pre-existing one from the prior spec — if one already exists at the arena's center, leave it in place, it's harmless since `MatchManager` shuffles the full pool regardless of count).

- [ ] **Step 4: Save the scene**

Use `mcp__unity-mcp__Unity_ManageAsset` or the appropriate scene-save MCP call to persist changes to `MinigameArena.unity`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scenes/Minigames/MinigameArena.unity
git commit -m "feat: add 16 spawn points to minigame arena for team deathmatch"
```

### Task G2: Place `MatchManager` in the scene and wire references

**Files:**
- Modify: `Assets/Scenes/Minigames/MinigameArena.unity`

- [ ] **Step 1: Create the `MatchManager` GameObject**

Use `mcp__unity-mcp__Unity_ManageGameObject` `action: "create"`, `name: "MatchManager"`, `components_to_add: ["MatchManager", "NetworkObject"]`. A `NetworkObject` is required since `MatchManager : NetworkBehaviour` and needs to spawn on the network for `OnNetworkSpawn` to fire — place it directly in the scene (not instantiated at runtime) so it's picked up by Netcode's in-scene network object handling the same way other persistent managers are.

- [ ] **Step 2: Assign serialized fields**

Use `mcp__unity-mcp__Unity_ManageGameObject` `action: "set_component_property"`:
```json
{
  "MatchManager": {
    "allyFaction": "Assets/Prefabs/agents/factions/TeamRed.asset",
    "enemyFaction": "Assets/Prefabs/agents/factions/TeamBlue.asset",
    "relationshipTable": "Assets/Prefabs/agents/factions/GlobalRelationships.asset",
    "deathmatchBotPrefab": "Assets/Prefabs/agents/prefab/RobotPatrol/DeathmatchBot.prefab"
  }
}
```

- [ ] **Step 3: Verify via `get_components`**

Confirm all 4 fields are assigned and non-null.

- [ ] **Step 4: Save the scene and commit**

```bash
git add Assets/Scenes/Minigames/MinigameArena.unity
git commit -m "feat: place and wire MatchManager in minigame arena"
```

### Task G3: Result screen UI

**Files:**
- Create: `Assets/Scripts/UI/Pages/MatchResultUI.cs`
- Modify: `Assets/Scenes/Minigames/MinigameArena.unity` (add the Canvas)

Minimal per design spec §6/§7: outcome message + "Return to Menu" button, following `MainMenuUI`'s flat-MonoBehaviour uGUI convention (confirmed as the project's only UI pattern — no page framework exists).

- [ ] **Step 1: Create the script via Unity MCP**

```csharp
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Minimal win/loss screen shown when MatchManager broadcasts match end.
// Follows MainMenuUI's flat-MonoBehaviour-with-Inspector-wired-elements
// convention — no page framework exists in this project to hook into.
public class MatchResultUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Text resultText;
    [SerializeField] private Button returnToMenuButton;
    [SerializeField] private SceneReference mainMenuScene;

    // Set on the local player's EntityFaction team index by whatever assigned
    // it (SpawnManager.SpawnPlayerForClient -> MatchManager.RegisterPlayerEntity,
    // always AllyTeam == 0 for this plan).
    private const int LocalPlayerTeam = 0;

    private void Awake()
    {
        panel.SetActive(false);
        returnToMenuButton.onClick.AddListener(ReturnToMenu);
    }

    private void Start()
    {
        var matchManager = FindFirstObjectByType<MatchManager>();
        if (matchManager != null)
            matchManager.OnMatchEnded += ShowResult;
    }

    private void ShowResult(int winningTeam)
    {
        panel.SetActive(true);
        resultText.text = winningTeam == LocalPlayerTeam ? "Victory!" : "Defeat";
    }

    private void ReturnToMenu()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(mainMenuScene.SceneName, LoadSceneMode.Single);
    }
}
```

Use `mcp__unity-mcp__Unity_CreateScript` with `Path: "Assets/Scripts/UI/Pages/MatchResultUI.cs"`.

- [ ] **Step 2: Verify compile**

Check console logs. Expected: no errors.

- [ ] **Step 3: Build the Canvas in the scene**

Use `mcp__unity-mcp__Unity_ManageGameObject` to create: a root `Canvas` GameObject (`components_to_add: ["Canvas", "CanvasScaler", "GraphicRaycaster"]`) named `MatchResultCanvas`, a child `Panel` GameObject (`Image` component, initially inactive) containing a child `Text` (`Text` component, "Result" placeholder) and a child `Button` (`Button` + `Text` "Return to Menu"). Then add `MatchResultUI` to the `MatchResultCanvas` root and wire `panel`/`resultText`/`returnToMenuButton`/`mainMenuScene` via `set_component_property`, using the existing `MainMenuUI`'s `mainMenuScene`/`SceneReference` asset (confirm the exact asset path used for the main menu scene reference in `MainMenuUI`'s own Inspector assignment before wiring, since the design spec's `MainMenuUI.cs` reads `gameScene`/`lobbyScene`/`minigameScene` fields but the main-menu-scene-itself reference for a *return* trip needs its own `SceneReference` asset lookup — check `Assets/Scenes/References/` for an existing one, e.g. a `MainMenu.asset`, before assuming one needs to be created).

- [ ] **Step 4: Manual verification**

Enter Play Mode via the full `StartMinigame` flow (or a scratch trigger calling `MatchManager`'s `EndMatch` directly via a temporary debug call, since a full 4v4 fight to completion is slow to trigger manually every iteration — remove any temporary debug trigger before committing). Confirm the panel appears with the correct Victory/Defeat text and the button returns to the main menu without console errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/Pages/MatchResultUI.cs Assets/Scenes/Minigames/MinigameArena.unity
git commit -m "feat: add match result screen"
```

### Task G4: Spectator camera for eliminated players

**Files:**
- Create: `Assets/Scripts/Player/SpectatorCamera.cs`
- Modify: `Assets/Scripts/Player/PlayerController.cs` (`OnDeath`)

Per design spec §7: free-fly camera activates locally on elimination (Lives Per Player exhausted, or Last Standing despawn), replaced by the result screen at match end. Only relevant when respawns are possible for others but not this entity — for this plan's hardcoded ally-team-only humans, this mainly matters in Lives Per Player mode; Last Standing has no respawns for anyone so elimination = the entity that just died is immediately the local player being eliminated, same trigger point.

- [ ] **Step 1: Create the spectator camera script**

```csharp
using UnityEngine;

// Free-fly camera activated on the local client when its player is eliminated
// (out of lives, or despawned in a no-respawn win condition) while the match
// continues for others. Deactivated and destroyed once MatchResultUI shows the
// final outcome (design spec §7) — MatchResultUI's panel simply renders on top,
// no explicit handoff needed since this only controls camera movement.
public class SpectatorCamera : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float lookSensitivity = 2f;

    private float yaw;
    private float pitch;

    private void Start()
    {
        yaw = transform.eulerAngles.y;
        pitch = transform.eulerAngles.x;
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        yaw += Input.GetAxis("Mouse X") * lookSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -89f, 89f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        if (Input.GetKey(KeyCode.Space)) move.y += 1f;
        if (Input.GetKey(KeyCode.LeftControl)) move.y -= 1f;

        transform.position += transform.TransformDirection(move) * (moveSpeed * Time.deltaTime);
    }

    private void OnDestroy()
    {
        Cursor.lockState = CursorLockMode.None;
    }
}
```

Use `mcp__unity-mcp__Unity_CreateScript` with `Path: "Assets/Scripts/Player/SpectatorCamera.cs"`.

Note: uses the legacy `Input` class (`Input.GetAxis`) rather than `PlayerInputManager`'s `InputControls` asset, since spectating is a distinct, simple, temporary mode not worth extending the main input action map for — consistent with YAGNI for this plan's scope. If this reads oddly next to the rest of the input-driven codebase during review, that's an acceptable, explicitly-scoped tradeoff, not an oversight.

- [ ] **Step 2: Wire activation into `PlayerController.OnDeath`**

Read current state of `Assets/Scripts/Player/PlayerController.cs` (`OnDeath`, currently):
```csharp
    private void OnDeath()
    {
        OnPlayerDeath?.Invoke();
        playerMovement.enabled = false;
        playerLook.enabled = false;

        // TODO: ragdoll
    }
```

Apply via `Unity_ApplyTextEdits`:
```csharp
    private void OnDeath()
    {
        OnPlayerDeath?.Invoke();
        playerMovement.enabled = false;
        playerLook.enabled = false;

        // Only spectate if this death doesn't end the match outright (MatchManager
        // decides that via win-condition evaluation) — spawn a free-fly camera at
        // the player's last position so eliminated-but-match-ongoing players (Lives
        // Per Player mode) aren't stuck staring at a frozen screen until the match
        // result arrives. Harmless no-op outside the minigame (no MatchManager in
        // the main game scene).
        var matchManager = FindFirstObjectByType<MatchManager>();
        if (matchManager != null)
        {
            var spectatorGo = new GameObject("SpectatorCamera");
            spectatorGo.transform.position = PlayerCameraTransform.position;
            spectatorGo.transform.rotation = PlayerCameraTransform.rotation;
            spectatorGo.AddComponent<Camera>();
            spectatorGo.AddComponent<SpectatorCamera>();
            playerCameraField().SetActive(false);
        }

        // TODO: ragdoll
    }

    private GameObject playerCameraField() => GetComponent<PlayerController>() != null ? gameObject : null;
```

**Correction before proceeding:** the `playerCameraField()` helper above is malformed (it doesn't actually reference the private `playerCamera` field and was a mistaken placeholder) — `playerCamera` is already a private serialized field on this same class, so it's directly accessible. Replace that last line and remove the bogus helper entirely. The final `OnDeath` reads:

```csharp
    private void OnDeath()
    {
        OnPlayerDeath?.Invoke();
        playerMovement.enabled = false;
        playerLook.enabled = false;

        // Only spectate if this death doesn't end the match outright (MatchManager
        // decides that via win-condition evaluation) — spawn a free-fly camera at
        // the player's last position so eliminated-but-match-ongoing players (Lives
        // Per Player mode) aren't stuck staring at a frozen screen until the match
        // result arrives. Harmless no-op outside the minigame (no MatchManager in
        // the main game scene).
        var matchManager = FindFirstObjectByType<MatchManager>();
        if (matchManager != null)
        {
            var spectatorGo = new GameObject("SpectatorCamera");
            spectatorGo.transform.position = PlayerCameraTransform.position;
            spectatorGo.transform.rotation = PlayerCameraTransform.rotation;
            spectatorGo.AddComponent<Camera>();
            spectatorGo.AddComponent<SpectatorCamera>();
            playerCamera.SetActive(false);
        }

        // TODO: ragdoll
    }
```

- [ ] **Step 3: Verify compile**

Check console logs. Expected: no errors.

- [ ] **Step 4: Manual verification**

In a Lives Per Player match, let the local player die without exhausting lives first (confirm respawn happens, no spectator camera), then exhaust lives (confirm spectator camera activates, free-fly works, cursor unlocks correctly on scene exit).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Player/SpectatorCamera.cs Assets/Scripts/Player/PlayerController.cs
git commit -m "feat: add free-fly spectator camera for eliminated players"
```

### Task G5: End-to-end manual verification

**Files:** none (verification only)

- [ ] **Step 1: Full match — Last Standing**

Set `MatchManager`'s Inspector defaults (or `MatchSettings` static defaults) to `WinCondition.LastStanding`, `AllyCount = 3`, `EnemyCount = 4`. Launch via the main menu's existing "Minigame" button (`MainMenuUI.StartMinigame`). Confirm: 3 allied bots + 4 enemy bots spawn at distinct positions, the player spawns with the ally faction, allied bots do not fire on the player or each other, enemy bots engage the player and allied bots, killed entities despawn (no respawn), the match ends when one side has no living members, and the result screen shows the correct outcome.

- [ ] **Step 2: Full match — Kill Target**

Repeat with `WinCondition.KillTarget`, `KillTargetCount = 5`. Confirm killed entities respawn after `respawnDelay`, kill counts accumulate per team, and the match ends the instant either team's kill count reaches 5.

- [ ] **Step 3: Full match — Lives Per Player**

Repeat with `WinCondition.LivesPerPlayer`, `LivesPerPlayerCount = 2`. Confirm entities respawn until their individual lives run out, then stay eliminated (bot: despawned; player: spectator camera activates), and the match ends when one team's combined lives hit 0.

- [ ] **Step 4: Record any deviations**

If any of the 3 runs above surface a bug, fix it in the relevant task's file directly (not a new task) and re-commit with a `fix:` prefix, then re-run that specific scenario to confirm.
