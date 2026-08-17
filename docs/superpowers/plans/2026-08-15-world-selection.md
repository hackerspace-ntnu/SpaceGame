# World Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the player start a New World or load a saved world by name, from one screen that serves both singleplayer and multiplayer, with the lobby host's choice deciding the session's world.

**Architecture:** The save system already supports arbitrary named slots; nothing calls it with anything but `"autosave"`/`"quicksave"`. This adds a `WorldSession` static that replaces `SaveManager.PendingLoad`/`StageLoad`/`ClearStagedLoad` as the single answer to "which world are we in", two header fields carrying the world's name and config GUID, and a `WorldSelectUI` screen. Save-slot defaulting moves from a hardcoded constant to the active world, which makes autosave/quit-save/quicksave per-world for free.

**Tech Stack:** Unity 6, C#, Netcode for GameObjects 2.9.1, Newtonsoft.Json, Unity Test Framework (EditMode), TextMeshPro + uGUI.

---

## Assembly constraint (read before starting)

This is the single most important structural fact in this plan, and getting it wrong costs a rewrite.

- `Assets/Game/Scripts/Core/Persistence/Format/` is its own assembly, `SpaceGame.Persistence`, with **`"references": []`** — it depends on nothing and **cannot see** `Assembly-CSharp`.
- `Assets/Game/Scripts/Core/Persistence/Runtime/` (`SaveManager`, `SaveHotkeys`) has no asmdef, so it is in **`Assembly-CSharp`**.
- `WorldStreamingConfig` is in **`SpaceGame.World.Streaming`**.
- `Assets/Game/Editor/Tests/` has no asmdef, so it is in **`Assembly-CSharp-Editor`**, which can see all of the above.

Therefore the world logic is split in two:

| Type | Assembly | Why |
|---|---|---|
| `WorldIdentity` (pure rules + validation) | `SpaceGame.Persistence` | No Unity/config deps, so EditMode tests exercise it directly. This is where the hard-requirement tests live. |
| `WorldSession` (runtime static) | `Assembly-CSharp` | Needs `SaveDocument` staging and `WorldStreamingConfig` lookup. |

Do **not** add a reference from `SpaceGame.Persistence` to anything. If a task seems to need one, the logic belongs in `WorldSession`, not `WorldIdentity`.

---

## File Structure

**Create:**
- `Assets/Game/Scripts/Core/Persistence/Format/WorldIdentity.cs` — pure world-id rules and config-guard validation.
- `Assets/Game/Scripts/Core/Persistence/Runtime/WorldSession.cs` — the runtime static: which world is active, staging the document.
- `Assets/Game/Scripts/Presentation/UI/Pages/WorldSelectUI.cs` — the New World / Load World screen.
- `Assets/Game/Editor/Tests/WorldIdentityTests.cs` — pure guard/sanitisation tests.
- `Assets/Game/Editor/Tests/WorldSaveRoundTripTests.cs` — the hard-requirement round-trip, isolation and per-world-quicksave tests.

**Modify:**
- `Assets/Game/Scripts/Core/Persistence/Format/SaveDocument.cs:47-60` — two `SaveHeader` fields.
- `Assets/Game/Scripts/World/Streaming/Core/WorldStreamingConfig.cs` — serialized `configId` GUID field.
- `Assets/Game/Scripts/Core/Persistence/Runtime/SaveManager.cs` — remove `PendingLoad`/`StageLoad`/`ClearStagedLoad`; default slot to active world; stamp header.
- `Assets/Game/Scripts/Core/Persistence/Runtime/SaveHotkeys.cs:52,58` — quicksave/quickload target the active world.
- `Assets/Game/Scripts/Presentation/UI/Pages/MainMenuUI.cs` — route through world select; host via `SessionLauncher`.

---

### Task 1: `WorldIdentity` — pure world rules

**Files:**
- Create: `Assets/Game/Scripts/Core/Persistence/Format/WorldIdentity.cs`
- Test: `Assets/Game/Editor/Tests/WorldIdentityTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Game/Editor/Tests/WorldIdentityTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTests
{
    public class WorldIdentityTests
    {
        [Test]
        public void IdFor_StripsPathSeparators()
        {
            Assert.AreEqual("evil", WorldIdentity.IdFor("../../evil"));
        }

        [Test]
        public void IdFor_EmptyNameFallsBackToSave()
        {
            Assert.AreEqual("save", WorldIdentity.IdFor("   "));
        }

        [Test]
        public void Accepts_MatchingConfigId()
        {
            var header = new SaveHeader { WorldConfigId = "abc123" };
            Assert.IsTrue(WorldIdentity.AcceptsConfig(header, "abc123", out string error));
            Assert.IsNull(error);
        }

        [Test]
        public void Rejects_MismatchedConfigId()
        {
            var header = new SaveHeader { WorldConfigId = "abc123" };
            Assert.IsFalse(WorldIdentity.AcceptsConfig(header, "different", out string error));
            Assert.IsNotNull(error);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Accepts_LegacySaveWithNoConfigId()
        {
            // A file written before world selection existed. It belongs to the only world
            // that existed at the time, so it must still load.
            var header = new SaveHeader { WorldConfigId = "" };
            Assert.IsTrue(WorldIdentity.AcceptsConfig(header, "abc123", out string error));
            Assert.IsNull(error);
        }

        [Test]
        public void DisplayNameFor_PrefersHeaderWorldName()
        {
            var header = new SaveHeader { WorldName = "My Desert Run" };
            Assert.AreEqual("My Desert Run", WorldIdentity.DisplayNameFor(header, "my desert run"));
        }

        [Test]
        public void DisplayNameFor_FallsBackToSlotIdOnLegacySave()
        {
            var header = new SaveHeader { WorldName = "" };
            Assert.AreEqual("autosave", WorldIdentity.DisplayNameFor(header, "autosave"));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

In Unity: **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run Selected** on `WorldIdentityTests`.
Expected: compile error, `WorldIdentity` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Game/Scripts/Core/Persistence/Format/WorldIdentity.cs`:

```csharp
namespace SpaceGame.Persistence
{
    /// <summary>
    /// The rules that decide what a world is called and whether a save belongs to the world
    /// being entered.
    ///
    /// Deliberately pure and in the Format assembly: this is the logic a wrong answer corrupts
    /// a session with, so it has to be reachable by a test that does not open Unity. It knows
    /// nothing about ScriptableObjects — the caller resolves a config to its id and passes the
    /// string in.
    /// </summary>
    public static class WorldIdentity
    {
        /// <summary>
        /// The file name a world is stored under, derived from what the player typed.
        ///
        /// Shares <see cref="SaveSlots.Sanitize"/> rather than repeating it, so there is exactly
        /// one answer to "can a typed world name escape the save directory".
        /// </summary>
        public static string IdFor(string displayName) => SaveSlots.Sanitize(displayName);

        /// <summary>
        /// Whether a save may be loaded into the world identified by <paramref name="configId"/>.
        ///
        /// A mismatch is refused rather than repaired. The chunk deltas in a save are keyed by
        /// scene name and grid coordinate, so hydrating world 2's save onto world 1's grid does
        /// not fail loudly — it scatters objects into whatever scenes happen to share those keys.
        ///
        /// An empty id on the save is accepted: files written before world selection existed
        /// carry no id and belong to the only world there was.
        /// </summary>
        public static bool AcceptsConfig(SaveHeader header, string configId, out string error)
        {
            string saved = header?.WorldConfigId;

            if (string.IsNullOrEmpty(saved) || saved == configId)
            {
                error = null;
                return true;
            }

            error = "This save belongs to a different world and cannot be loaded here.";
            return false;
        }

        /// <summary>What to show in a world list, falling back to the file name for legacy saves.</summary>
        public static string DisplayNameFor(SaveHeader header, string slotId) =>
            string.IsNullOrEmpty(header?.WorldName) ? slotId : header.WorldName;
    }
}
```

- [ ] **Step 4: Add the two header fields**

Modify `Assets/Game/Scripts/Core/Persistence/Format/SaveDocument.cs`, inside `SaveHeader` after the `slotLabel` line:

```csharp
        [JsonProperty("slotLabel")] public string SlotLabel = string.Empty;

        /// <summary>The world's player-facing name. Empty on saves written before world selection.</summary>
        [JsonProperty("worldName")] public string WorldName = string.Empty;

        /// <summary>
        /// The GUID of the WorldStreamingConfig this world's chunk deltas were recorded against.
        /// Empty on legacy saves, which are taken to belong to the main world. Checked on load by
        /// <see cref="WorldIdentity.AcceptsConfig"/>.
        /// </summary>
        [JsonProperty("worldConfigId")] public string WorldConfigId = string.Empty;
```

Do **not** bump `SaveDocument.CurrentVersion`. Both fields default to empty, which is exactly how an old file deserialises, so no migration is needed and `SaveMigrator`'s ladder stays empty.

- [ ] **Step 5: Run the tests to verify they pass**

Run EditMode `WorldIdentityTests`.
Expected: 6/6 PASS.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Core/Persistence/Format/WorldIdentity.cs \
        Assets/Game/Scripts/Core/Persistence/Format/WorldIdentity.cs.meta \
        Assets/Game/Scripts/Core/Persistence/Format/SaveDocument.cs \
        Assets/Game/Editor/Tests/WorldIdentityTests.cs \
        Assets/Game/Editor/Tests/WorldIdentityTests.cs.meta
git commit -m "feat(save): world identity rules and header fields"
```

---

### Task 2: `configId` on `WorldStreamingConfig`

**Files:**
- Modify: `Assets/Game/Scripts/World/Streaming/Core/WorldStreamingConfig.cs`

The runtime must know a config's GUID without touching `AssetDatabase`, which does not exist in a build. The GUID is therefore stamped into a serialized field at author time by `OnValidate`, exactly as `SaveableEntity` already stamps its `prefabId`.

- [ ] **Step 1: Add the field and the editor-time stamp**

Add to `WorldStreamingConfig`, after the `streamLookaheadSeconds` field:

```csharp
        [Header("Identity")]
        [Tooltip("Stable id for this world, stamped from the asset GUID. A save records the id of " +
                 "the world it was made in, and refuses to load into a different one — its chunk " +
                 "deltas are keyed by scene name and would otherwise scatter into the wrong scenes. " +
                 "Do not edit by hand.")]
        [SerializeField] private string configId = string.Empty;

        /// <summary>This world's stable id. Empty only on a config that has never been saved in-editor.</summary>
        public string ConfigId => configId;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // The GUID, not the asset name: names change and a renamed world must not orphan
            // every save made in it.
            string path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(path)) return;

            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid) || guid == configId) return;

            configId = guid;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
```

- [ ] **Step 2: Stamp the two existing configs**

In Unity, select each of these so `OnValidate` runs, then **File ▸ Save Project**:
- `Assets/Game/Settings/WorldStreamingConfig.asset`
- `Assets/Game/Settings/FerdinandWorldStreamingConfig.asset`

- [ ] **Step 3: Verify the ids landed and differ**

Run:
```bash
grep -A1 "configId" Assets/Game/Settings/WorldStreamingConfig.asset Assets/Game/Settings/FerdinandWorldStreamingConfig.asset
```
Expected: a non-empty 32-character hex string in each, and the two are **different**. If either is empty the asset was not re-saved — repeat step 2.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/World/Streaming/Core/WorldStreamingConfig.cs \
        Assets/Game/Settings/WorldStreamingConfig.asset \
        Assets/Game/Settings/FerdinandWorldStreamingConfig.asset
git commit -m "feat(world): stable config id stamped from asset GUID"
```

---

### Task 3: `WorldSession` — the runtime static

**Files:**
- Create: `Assets/Game/Scripts/Core/Persistence/Runtime/WorldSession.cs`

- [ ] **Step 1: Write the implementation**

Create `Assets/Game/Scripts/Core/Persistence/Runtime/WorldSession.cs`:

```csharp
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Which world this session is playing, and the document it starts from.
    ///
    /// Replaces SaveManager's PendingLoad/StageLoad/ClearStagedLoad. Those expressed "new game" as
    /// the ABSENCE of a staged document, so every entry point had to independently know that
    /// convention — and one that forgot silently turned New Game into Continue. Here the choice is
    /// a value: <see cref="IsNew"/> is true or a document is staged, never ambiguous.
    ///
    /// Static for the same reason PendingLoad was: nothing survives the scene load between the menu
    /// and the world. The menu's SaveManager dies with the menu scene and the world's does not exist
    /// yet, so the choice cannot live on either.
    /// </summary>
    public static class WorldSession
    {
        /// <summary>The sanitized slot id — the save's file name.</summary>
        public static string WorldId { get; private set; }

        /// <summary>What the player typed, shown in menus.</summary>
        public static string DisplayName { get; private set; }

        /// <summary>The WorldStreamingConfig GUID this world belongs to.</summary>
        public static string WorldConfigId { get; private set; }

        /// <summary>True when this world has no save behind it yet.</summary>
        public static bool IsNew { get; private set; }

        /// <summary>False before any world has been chosen — a world scene opened directly in the editor.</summary>
        public static bool IsActive => !string.IsNullOrEmpty(WorldId);

        private static SaveDocument staged;

        /// <summary>Begins a world with no save behind it.</summary>
        public static void StageNew(string displayName, WorldStreamingConfig config)
        {
            WorldId = WorldIdentity.IdFor(displayName);
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? WorldId : displayName.Trim();
            WorldConfigId = config != null ? config.ConfigId : string.Empty;
            IsNew = true;
            staged = null;
        }

        /// <summary>
        /// Reads a world's save and stages it for the next world load. Stages nothing on failure,
        /// so a refused load leaves the previous choice untouched rather than half-applied.
        /// </summary>
        public static bool StageExisting(string worldId, WorldStreamingConfig config, out string error)
        {
            var slots = new SaveSlots(SaveManager.DefaultRoot);
            SaveFileStore.ReadResult result = SaveFileStore.Read(slots.PathFor(worldId));

            switch (result.Outcome)
            {
                case SaveFileStore.ReadOutcome.RecoveredFromBackup:
                    Debug.LogWarning($"[Save] {result.Error}");
                    goto case SaveFileStore.ReadOutcome.Ok;

                case SaveFileStore.ReadOutcome.Ok:
                    break;

                case SaveFileStore.ReadOutcome.Missing:
                    error = $"There is no world called '{worldId}'.";
                    return false;

                default:
                    error = result.Error ?? $"World '{worldId}' could not be read.";
                    return false;
            }

            SaveDocument document = result.Document.Normalized();
            string configId = config != null ? config.ConfigId : string.Empty;

            if (!WorldIdentity.AcceptsConfig(document.Header, configId, out error))
                return false;

            WorldId = WorldIdentity.IdFor(worldId);
            DisplayName = WorldIdentity.DisplayNameFor(document.Header, worldId);
            WorldConfigId = document.Header.WorldConfigId;
            IsNew = false;
            staged = document;

            error = null;
            return true;
        }

        /// <summary>
        /// Takes the staged document, leaving the world active but the document consumed.
        ///
        /// Consumed rather than merely read so a second world load in the same process cannot
        /// silently re-apply a document the player already moved on from. Null for a new world.
        /// </summary>
        public static SaveDocument Consume()
        {
            SaveDocument document = staged;
            staged = null;
            return document;
        }

        /// <summary>Forgets the world entirely, for a return to the main menu.</summary>
        public static void Clear()
        {
            WorldId = null;
            DisplayName = null;
            WorldConfigId = null;
            IsNew = false;
            staged = null;
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

In Unity, let the domain reload finish and check the Console.
Expected: no compile errors. `WorldSession` is in `Assembly-CSharp` so it may reference both `SpaceGame.Persistence` and `SpaceGame.World.Streaming`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Core/Persistence/Runtime/WorldSession.cs \
        Assets/Game/Scripts/Core/Persistence/Runtime/WorldSession.cs.meta
git commit -m "feat(save): WorldSession replaces the staged-document convention"
```

---

### Task 4: Point `SaveManager` at the active world

**Files:**
- Modify: `Assets/Game/Scripts/Core/Persistence/Runtime/SaveManager.cs`

- [ ] **Step 1: Delete `PendingLoad` and consume from `WorldSession`**

Remove the `PendingLoad` property (lines 41-48) along with its doc comment. In `Awake`, replace:

```csharp
            SaveDocument document = PendingLoad;
            PendingLoad = null;
```

with:

```csharp
            SaveDocument document = WorldSession.Consume();
```

- [ ] **Step 2: Delete `StageLoad` and `ClearStagedLoad`**

Remove both methods entirely (lines 475-510), including the `/// <summary>` blocks. `WorldSession.StageExisting` replaces the first; the second has no replacement because "new world" is now a value rather than the absence of one.

- [ ] **Step 3: Add the active-world slot default**

Add near `DefaultRoot`:

```csharp
        /// <summary>
        /// The slot a save with no explicit target goes to: the active world.
        ///
        /// Falls back to the autosave slot only when no world has been chosen — a world scene
        /// opened directly in the editor, with no menu run behind it. That fallback is what keeps
        /// the existing editor workflow working; in normal play a world is always active.
        /// </summary>
        private static string DefaultSlotId =>
            WorldSession.IsActive ? WorldSession.WorldId : SaveSlots.AutoSaveSlotId;
```

- [ ] **Step 4: Route the three automatic saves through it**

In `Update` (line 192), replace `Save(SaveSlots.AutoSaveSlotId, "Autosave");` with:

```csharp
            Save(DefaultSlotId, "Autosave");
```

In `OnApplicationQuit` (line 202), replace `Save(SaveSlots.AutoSaveSlotId, "Autosave", synchronous: true);` with:

```csharp
            Save(DefaultSlotId, "Autosave", synchronous: true);
```

Replace `QuickSave()` (line 356) with:

```csharp
        /// <summary>
        /// Writes to the active world.
        ///
        /// Not to a separate quicksave file: with more than one world a global quicksave slot is
        /// silently cross-world — F5 in one world and F9 in another would load the wrong session.
        /// </summary>
        public bool QuickSave() => Save(DefaultSlotId, "Quicksave");
```

- [ ] **Step 5: Stamp the world onto the header**

In `BuildDocument`, extend the `SaveHeader` initialiser:

```csharp
                Header = new SaveHeader
                {
                    Version = SaveDocument.CurrentVersion,
                    SavedAtUtc = DateTime.UtcNow,
                    PlaytimeSeconds = TotalPlaytime,
                    GameVersion = Application.version,
                    SlotLabel = string.IsNullOrEmpty(label) ? slotId : label,
                    WorldName = WorldSession.IsActive ? WorldSession.DisplayName : slotId,
                    WorldConfigId = WorldSession.WorldConfigId ?? string.Empty,
                },
```

- [ ] **Step 6: Verify no stale references remain**

Run:
```bash
grep -rn "StageLoad\|PendingLoad\|ClearStagedLoad" Assets/Game/Scripts Assets/Game/Editor --include="*.cs"
```
Expected: only hits in `SaveHotkeys.cs` and `MainMenuUI.cs`, both fixed in Tasks 5 and 6. Zero hits in `SaveManager.cs`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Scripts/Core/Persistence/Runtime/SaveManager.cs
git commit -m "feat(save): saves default to the active world"
```

---

### Task 5: Per-world quicksave hotkeys

**Files:**
- Modify: `Assets/Game/Scripts/Core/Persistence/Runtime/SaveHotkeys.cs:42-76`

- [ ] **Step 1: Replace both methods**

```csharp
        public void QuickSave()
        {
            SaveManager manager = SaveManager.Instance;

            if (manager == null)
            {
                Debug.LogWarning("[Save] Quicksave pressed with no SaveManager in the scene.");
                return;
            }

            // Writes to whichever world is active — see SaveManager.QuickSave.
            if (manager.QuickSave())
                Debug.Log($"[Save] Quicksaved '{WorldSession.DisplayName ?? "world"}'.");
        }

        public void QuickLoad()
        {
            if (!WorldSession.IsActive)
            {
                Debug.LogWarning("[Save] Quickload pressed with no active world.");
                return;
            }

            // Restages THIS world rather than a global quicksave slot, so quickloading cannot
            // pull a different world's session into the one being played.
            if (!WorldSession.StageExisting(WorldSession.WorldId, ActiveConfig(), out string error))
            {
                Debug.LogWarning($"[Save] Quickload failed: {error}");
                return;
            }

            string target = string.IsNullOrEmpty(worldSceneName)
                ? SceneManager.GetActiveScene().name
                : worldSceneName;

            Debug.Log($"[Save] Quickloading into '{target}'.");

            // Through Netcode's scene manager when hosted, so clients follow. A plain
            // SceneManager.LoadScene here would leave every client in the old world.
            if (Network.Server)
                Unity.Netcode.NetworkManager.Singleton.SceneManager.LoadScene(target, LoadSceneMode.Single);
            else
                SceneManager.LoadScene(target, LoadSceneMode.Single);
        }

        /// <summary>
        /// The config of the world being played, so a quickload validates against the same world
        /// it is reloading. Found from the live streamer rather than a serialized reference, which
        /// would be one more thing to wire per world scene and to get wrong.
        /// </summary>
        private static WorldStreamingConfig ActiveConfig()
        {
            var streamer = Object.FindFirstObjectByType<WorldStreamer>();
            return streamer != null ? streamer.Config : null;
        }
```

- [ ] **Step 2: Add the usings**

At the top of the file, add:

```csharp
using SpaceGame.World;
```

- [ ] **Step 3: Expose `Config` on `WorldStreamer`**

`WorldStreamer.config` is a private `[SerializeField]`. Add next to it in `Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs`:

```csharp
        /// <summary>The world this streamer is running, for code that needs the world's identity.</summary>
        public WorldStreamingConfig Config => config;
```

- [ ] **Step 4: Verify it compiles**

Check the Unity Console after the domain reload.
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Persistence/Runtime/SaveHotkeys.cs \
        Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs
git commit -m "feat(save): quicksave and quickload follow the active world"
```

---

### Task 6: Route `MainMenuUI` through world select

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Pages/MainMenuUI.cs`

- [ ] **Step 1: Replace the entry points**

Replace `StartSinglePlayer`, `ContinueGame`, `LoadGame` and `EnterWorld` with:

```csharp
    [SerializeField] private WorldSelectUI worldSelect;
    [SerializeField] private WorldStreamingConfig worldConfig;

    /// <summary>Opens the world list; entering a world is WorldSelectUI's job.</summary>
    public void StartSinglePlayer() => worldSelect.Open(WorldSelectUI.Destination.Singleplayer);

    /// <summary>Opens the world list, then the lobby — the host picks the world before anyone joins.</summary>
    public void StartMultiPlayer() => worldSelect.Open(WorldSelectUI.Destination.Lobby);

    /// <summary>
    /// Resumes the most recently played world without going through the list. Safe to wire to a
    /// button that is always visible: with nothing to load it says so and stays on the menu.
    /// </summary>
    public void ContinueGame()
    {
        var slots = new SaveSlots(SaveManager.DefaultRoot);

        if (!slots.TryGetMostRecent(out SaveSlotInfo slot))
        {
            Debug.LogWarning("[Save] Continue pressed with no readable save on disk.");
            return;
        }

        if (!WorldSession.StageExisting(slot.SlotId, worldConfig, out string error))
        {
            Debug.LogError($"[Save] Could not load '{slot.SlotId}': {error}");
            return;
        }

        EnterWorld();
    }

    /// <summary>
    /// Does the three things every route into the world does, in the order they must happen.
    /// Public so WorldSelectUI can finish the job it started.
    /// </summary>
    public void EnterWorld()
    {
        // Up before the load starts, and held until terrain streaming and the NavMesh bake have
        // finished — those run after the scene reports loaded and are what makes the first few
        // seconds stutter.
        LoadingScreenUI.ShowUntilReady(gameScene.SceneName);

        // Through SessionLauncher rather than a bare StartHost(). UnityTransport keeps whatever
        // it was last configured with, so hosting after a Relay attempt in the same process would
        // otherwise host on stale Relay data.
        SessionLauncher.SessionResult result = SessionLauncher.HostDirect();

        if (!result.Success)
        {
            Debug.LogError($"[Net] Could not start the session: {result.Error}");
            LoadingScreenUI.Dismiss();
            return;
        }

        NetworkManager.Singleton.SceneManager.LoadScene(gameScene.SceneName, LoadSceneMode.Single);
    }

    /// <summary>Loads the lobby, with the host's world already chosen.</summary>
    public void EnterLobby() => SceneManager.LoadScene(lobbyScene.SceneName, LoadSceneMode.Single);
```

`LaunchMinigame` keeps its own `StartHost()` — the minigame is a throwaway arena with no world
behind it, and routing it through the world select would be wrong.

- [ ] **Step 2: Verify `SessionLauncher.HostDirect` is reachable**

Run:
```bash
grep -n "public static SessionResult HostDirect" Assets/Game/Scripts/Core/Multiplayer/SessionLauncher.cs
```
Expected: one hit, `HostDirect(ushort port = DefaultDirectPort)`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Presentation/UI/Pages/MainMenuUI.cs
git commit -m "feat(menu): route world entry through world select and SessionLauncher"
```

---

### Task 7: `WorldSelectUI`

**Files:**
- Create: `Assets/Game/Scripts/Presentation/UI/Pages/WorldSelectUI.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The New World / Load World screen, and the only place a world is chosen.
    ///
    /// One screen serves both singleplayer and multiplayer because the choice is the same in each
    /// — the difference is only where the player goes afterwards. Picking here rather than inside
    /// the lobby also keeps LobbyMenu.unity untouched, whose buttons are bound to method names by
    /// string and pinned by LobbyMenuWiringTests.
    /// </summary>
    public class WorldSelectUI : MonoBehaviour
    {
        public enum Destination { Singleplayer, Lobby }

        [SerializeField] private GameObject panel;
        [SerializeField] private MainMenuUI mainMenu;
        [SerializeField] private WorldStreamingConfig worldConfig;

        [Header("World list")]
        [Tooltip("Parent the rows are instantiated under.")]
        [SerializeField] private Transform listContent;
        [Tooltip("A row prefab whose root carries a Button and a TMP_Text child.")]
        [SerializeField] private GameObject rowPrefab;

        [Header("New world")]
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private Button createButton;

        [Header("Feedback")]
        [SerializeField] private TMP_Text message;

        private Destination destination;
        private readonly List<GameObject> rows = new();
        private string selectedWorldId;

        private void Awake()
        {
            if (panel != null) panel.SetActive(false);
            if (createButton != null) createButton.onClick.AddListener(CreateWorld);
        }

        public void Open(Destination target)
        {
            destination = target;
            selectedWorldId = null;
            SetMessage(string.Empty);

            if (panel != null) panel.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
        }

        /// <summary>Rebuilds the world list from disk.</summary>
        public void Refresh()
        {
            foreach (GameObject row in rows) Destroy(row);
            rows.Clear();

            if (listContent == null || rowPrefab == null) return;

            var slots = new SaveSlots(SaveManager.DefaultRoot);

            foreach (SaveSlotInfo slot in slots.List())
            {
                GameObject row = Instantiate(rowPrefab, listContent);
                rows.Add(row);

                var label = row.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    // Unreadable files are listed rather than hidden: a world the player can see
                    // and delete beats one that silently is not there.
                    label.text = slot.Unreadable
                        ? $"{slot.SlotId}  (unreadable)"
                        : $"{WorldIdentity.DisplayNameFor(slot.Header, slot.SlotId)}" +
                          $"  —  {slot.SavedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
                }

                string id = slot.SlotId;
                var button = row.GetComponent<Button>();
                if (button != null) button.onClick.AddListener(() => Select(id));
            }
        }

        /// <summary>Remembers which row is chosen. Loading is a separate, deliberate press.</summary>
        public void Select(string worldId)
        {
            selectedWorldId = worldId;
            SetMessage($"Selected '{worldId}'.");
        }

        /// <summary>Starts a world with no save behind it.</summary>
        public void CreateWorld()
        {
            string typed = nameInput != null ? nameInput.text : string.Empty;

            if (string.IsNullOrWhiteSpace(typed))
            {
                SetMessage("Type a name for the new world.");
                return;
            }

            // Refusing rather than silently overwriting: the two are one keystroke apart and only
            // one of them is recoverable.
            var slots = new SaveSlots(SaveManager.DefaultRoot);
            if (slots.Exists(WorldIdentity.IdFor(typed)))
            {
                SetMessage($"A world called '{typed}' already exists.");
                return;
            }

            WorldSession.StageNew(typed, worldConfig);
            Enter();
        }

        /// <summary>Loads the selected world.</summary>
        public void LoadSelected()
        {
            if (string.IsNullOrEmpty(selectedWorldId))
            {
                SetMessage("Pick a world first.");
                return;
            }

            if (!WorldSession.StageExisting(selectedWorldId, worldConfig, out string error))
            {
                SetMessage(error);
                return;
            }

            Enter();
        }

        /// <summary>Deletes the selected world's file.</summary>
        public void DeleteSelected()
        {
            if (string.IsNullOrEmpty(selectedWorldId))
            {
                SetMessage("Pick a world first.");
                return;
            }

            new SaveSlots(SaveManager.DefaultRoot).Delete(selectedWorldId);
            SetMessage($"Deleted '{selectedWorldId}'.");
            selectedWorldId = null;
            Refresh();
        }

        /// <summary>
        /// Hands off to wherever this screen was opened for. The world is already staged by the
        /// time this runs, so both destinations are a plain scene change.
        /// </summary>
        private void Enter()
        {
            Close();

            if (destination == Destination.Singleplayer) mainMenu.EnterWorld();
            else mainMenu.EnterLobby();
        }

        private void SetMessage(string text)
        {
            if (message != null) message.text = text;
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Check the Unity Console.
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Presentation/UI/Pages/WorldSelectUI.cs \
        Assets/Game/Scripts/Presentation/UI/Pages/WorldSelectUI.cs.meta
git commit -m "feat(menu): world select screen"
```

---

### Task 8: The hard-requirement round-trip tests

**Files:**
- Create: `Assets/Game/Editor/Tests/WorldSaveRoundTripTests.cs`

These prove saving, loading and new-world work against the real `SaveFileStore` and a temp
directory. They deliberately exercise the file layer end to end rather than mocking it — the
whole point of `SaveSlots` taking its root by injection.

- [ ] **Step 1: Write the tests**

```csharp
using System.IO;
using NUnit.Framework;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// The save/load contract, exercised against real files in a temp directory.
    ///
    /// These cover the three things world selection has to get right: a world round-trips, two
    /// worlds never touch each other's state, and a new world starts empty.
    /// </summary>
    public class WorldSaveRoundTripTests
    {
        private string root;
        private SaveSlots slots;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "SpaceGameWorldTests", Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            slots = new SaveSlots(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        /// <summary>Builds a document carrying one identifiable piece of world state.</summary>
        private static SaveDocument DocumentFor(string worldName, string configId, string marker)
        {
            var document = new SaveDocument
            {
                Header = new SaveHeader
                {
                    WorldName = worldName,
                    WorldConfigId = configId,
                    SlotLabel = worldName,
                },
            }.Normalized();

            document.World.Global.Set("testMarker", new { value = marker });
            return document;
        }

        private static string MarkerIn(SaveDocument document)
        {
            Assert.IsTrue(document.World.Global.TryGetRaw("testMarker", out var payload),
                          "The document carries no test marker.");
            return payload["value"].ToString();
        }

        [Test]
        public void SaveThenLoad_RoundTripsWorldState()
        {
            SaveFileStore.Write(slots.PathFor("Alpha"), DocumentFor("Alpha", "config-1", "alpha-state"), true);

            SaveFileStore.ReadResult result = SaveFileStore.Read(slots.PathFor("Alpha"));

            Assert.AreEqual(SaveFileStore.ReadOutcome.Ok, result.Outcome);
            Assert.AreEqual("alpha-state", MarkerIn(result.Document.Normalized()));
            Assert.AreEqual("Alpha", result.Document.Header.WorldName);
            Assert.AreEqual("config-1", result.Document.Header.WorldConfigId);
        }

        [Test]
        public void TwoWorlds_DoNotShareState()
        {
            SaveFileStore.Write(slots.PathFor("Alpha"), DocumentFor("Alpha", "config-1", "alpha-state"), true);
            SaveFileStore.Write(slots.PathFor("Beta"), DocumentFor("Beta", "config-1", "beta-state"), true);

            Assert.AreEqual("alpha-state", MarkerIn(SaveFileStore.Read(slots.PathFor("Alpha")).Document.Normalized()));
            Assert.AreEqual("beta-state", MarkerIn(SaveFileStore.Read(slots.PathFor("Beta")).Document.Normalized()));

            Assert.AreEqual(2, slots.List().Count, "Each world must be its own file.");
        }

        [Test]
        public void NewWorld_OverAnExistingNameIsRefusedByExists()
        {
            SaveFileStore.Write(slots.PathFor("Alpha"), DocumentFor("Alpha", "config-1", "alpha-state"), true);

            // WorldSelectUI checks Exists before staging a new world, so an accidental overwrite
            // of a real save is impossible.
            Assert.IsTrue(slots.Exists(WorldIdentity.IdFor("Alpha")));
            Assert.IsFalse(slots.Exists(WorldIdentity.IdFor("Gamma")));
        }

        [Test]
        public void ConfigGuard_RefusesASaveFromAnotherWorld()
        {
            SaveDocument foreign = DocumentFor("Alpha", "config-OTHER", "alpha-state");

            Assert.IsFalse(WorldIdentity.AcceptsConfig(foreign.Header, "config-1", out string error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void LegacySave_WithNoWorldFieldsStillLoads()
        {
            // A file written before world selection existed: no worldName, no worldConfigId.
            var legacy = new SaveDocument { Header = new SaveHeader { SlotLabel = "autosave" } }.Normalized();
            legacy.World.Global.Set("testMarker", new { value = "legacy-state" });

            SaveFileStore.Write(slots.PathFor("autosave"), legacy, true);
            SaveFileStore.ReadResult result = SaveFileStore.Read(slots.PathFor("autosave"));

            Assert.AreEqual(SaveFileStore.ReadOutcome.Ok, result.Outcome);
            Assert.AreEqual("legacy-state", MarkerIn(result.Document.Normalized()));
            Assert.IsTrue(WorldIdentity.AcceptsConfig(result.Document.Header, "config-1", out _),
                          "A legacy save belongs to the only world that existed when it was written.");
        }

        [Test]
        public void WorldName_CannotEscapeTheSaveRoot()
        {
            string id = WorldIdentity.IdFor("../../etc/passwd");
            string path = slots.PathFor(id);

            Assert.AreEqual(Path.GetFullPath(root), Path.GetFullPath(Path.GetDirectoryName(path)));
        }

        [Test]
        public void ListedWorlds_ShowTheirDisplayName()
        {
            SaveFileStore.Write(slots.PathFor("my desert run"),
                                DocumentFor("My Desert Run", "config-1", "state"), true);

            SaveSlotInfo slot = slots.List()[0];

            Assert.AreEqual("My Desert Run", WorldIdentity.DisplayNameFor(slot.Header, slot.SlotId));
        }
    }
}
```

- [ ] **Step 2: Run the tests**

Run EditMode `WorldSaveRoundTripTests`.
Expected: 7/7 PASS.

If `SaveFileStore.Write(path, document, pretty)` does not match that overload, check its
signature with:
```bash
grep -n "public static void Write" Assets/Game/Scripts/Core/Persistence/Format/SaveFileStore.cs
```
and adjust the call, not the assertion.

- [ ] **Step 3: Run the whole EditMode suite for regressions**

Run all EditMode tests. The pre-existing save fixtures — `SaveFileStoreTests`,
`SaveSerializationTests`, `SaveSlotTests`, `SaveMigrationTests`,
`SavePayloadCompatibilityTests` — must all still pass, proving the two new header fields did
not break the format.

Expected: no new failures.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Editor/Tests/WorldSaveRoundTripTests.cs \
        Assets/Game/Editor/Tests/WorldSaveRoundTripTests.cs.meta
git commit -m "test(save): world round-trip, isolation and config guard"
```

---

### Task 9: Scene wiring

**Files:**
- Modify: `Assets/Game/Scenes/Core/MainMenu.unity`

Code alone does not make the screen appear. This is the step that satisfies "implement the UI
changes".

- [ ] **Step 1: Build the panel**

In `MainMenu.unity`, under the existing Canvas:

1. Create an empty child `WorldSelectPanel`, inactive by default.
2. Under it add: a `ScrollView` (its `Content` is the list parent), a `TMP_InputField`
   (`NameInput`), and four `Button`s — `CreateButton`, `LoadButton`, `DeleteButton`,
   `BackButton` — plus a `TMP_Text` (`MessageText`).
3. Create a row prefab at `Assets/Game/Prefabs/UI/WorldRow.prefab`: a `Button` whose root has a
   `TMP_Text` child. Save it as a prefab and leave it out of the scene.

- [ ] **Step 2: Add and wire the component**

Add `WorldSelectUI` to `WorldSelectPanel`'s parent (the Canvas or a `MainMenu` object), then
set in the Inspector:

| Field | Value |
|---|---|
| `panel` | `WorldSelectPanel` |
| `mainMenu` | the object carrying `MainMenuUI` |
| `worldConfig` | `Assets/Game/Settings/WorldStreamingConfig.asset` |
| `listContent` | the ScrollView's `Content` |
| `rowPrefab` | `Assets/Game/Prefabs/UI/WorldRow.prefab` |
| `nameInput` | `NameInput` |
| `createButton` | `CreateButton` |
| `message` | `MessageText` |

- [ ] **Step 3: Wire the buttons**

`CreateButton` is wired in code by `Awake`. Wire the rest via their `onClick` in the Inspector:

| Button | Target | Method |
|---|---|---|
| `LoadButton` | `WorldSelectUI` | `LoadSelected` |
| `DeleteButton` | `WorldSelectUI` | `DeleteSelected` |
| `BackButton` | `WorldSelectUI` | `Close` |

- [ ] **Step 4: Wire `MainMenuUI`'s new fields**

On the `MainMenuUI` object set `worldSelect` → the `WorldSelectUI` component, and `worldConfig`
→ `Assets/Game/Settings/WorldStreamingConfig.asset`.

The existing Singleplayer and Multiplayer buttons keep pointing at `StartSinglePlayer` and
`StartMultiPlayer`; both now open the panel rather than entering the world.

- [ ] **Step 5: Save and commit**

```bash
git add Assets/Game/Scenes/Core/MainMenu.unity \
        Assets/Game/Prefabs/UI/WorldRow.prefab \
        Assets/Game/Prefabs/UI/WorldRow.prefab.meta
git commit -m "feat(menu): wire the world select panel into MainMenu"
```

---

### Task 10: Play-mode verification

Neither a compile nor an EditMode pass proves the feature works. This task is the evidence.

- [ ] **Step 1: New world**

Play from `Bootstrap`. Singleplayer → type "Alpha" → Create.
Expected: the world loads, and the Console shows a `[Save] Wrote 'Alpha'` line within the
autosave interval or on quit.

- [ ] **Step 2: The world file exists and is named**

```bash
ls ~/Library/Application\ Support/*/SpaceGame/Saves/
```
Expected: `Alpha.json`. Confirm its header:
```bash
grep -o '"worldName":"[^"]*"' ~/Library/Application\ Support/*/SpaceGame/Saves/Alpha.json
```
Expected: `"worldName":"Alpha"`.

- [ ] **Step 3: State round-trips**

In Alpha, move somewhere identifiable and press **F5**. Quit to menu, Singleplayer → select
Alpha → Load.
Expected: the player is where F5 was pressed, not at the default spawn.

- [ ] **Step 4: Two worlds stay separate**

Create a second world "Beta", move somewhere clearly different, F5, quit to menu, load Alpha.
Expected: Alpha's position, not Beta's. Both `Alpha.json` and `Beta.json` on disk. **This is
the case the old global quicksave slot got wrong.**

- [ ] **Step 5: Multiplayer host carries the world**

Multiplayer → select Alpha → the lobby opens → Create Lobby → Start Game.
Expected: the host spawns in Alpha's saved state.

- [ ] **Step 6: Record the outcome**

Write what actually happened — including anything that failed — into the plan or the commit
message. Do not report a step as passing without having seen it.

---

## Actual results (2026-08-15)

Implemented and verified against the live editor (Unity 6000.3.11f1) over the MCP bridge.
**53 assertions, 0 failures**, run against real compiled code and real files.

**Compile:** clean. Zero errors. Two of my own mistakes were caught and fixed during the run —
`SessionResult` is a sibling type in `SpaceGame.Core`, not nested in `SessionLauncher`; and a
leftover `return frame;` in a method changed to `void`.

**Logic — 12/12** (`WorldIdentity`, header fields): name sanitisation strips path separators and
falls back to `save`; the config guard accepts matching ids, refuses mismatches with a reason,
and accepts legacy empty ids; display names prefer the header and fall back to the file name;
both new header fields default to empty and `CurrentVersion` is still 1.

**File layer — 20/20** (real `SaveFileStore`, real files under `Application.temporaryCachePath`):
round trip carries `worldName`/`worldConfigId` through disk and into the JSON text; two worlds
keep separate state in separate files; an existing name is detected before overwrite; a legacy
document with no world fields loads and is accepted; a `../../etc/passwd` name cannot leave the
save root; the list shows the display name; delete removes one world and leaves the others.

**Scene wiring — 21/21**: all eight `WorldSelectUI` serialized fields resolved; both new
`MainMenuUI` fields resolved and the config carries a stamped id; the three sidebar buttons each
bind to exactly one persistent listener resolving to the right method on the right target; the
panel starts hidden; the row prefab carries the Button and TMP label the code expects.

**Deadlock check:** the `WorldSelectUI` component sits on the canvas root with the panel as its
child, so it stays enabled while the panel is hidden and `Open()` can bring it back. Had the
component been on the panel itself, hiding it once would have made the screen unreopenable.

**Method surface — compile-proven.** All 16 methods bound from scenes by string name
(`StartSinglePlayer`, `StartMultiPlayer`, `ContinueGame`, `EnterWorld`, `EnterLobby`,
`StartMinigame`, `LaunchMinigame`, `OpenSettings`, `QuitGame`, `Open`, `Close`, `Refresh`,
`Select`, `CreateWorld`, `LoadSelected`, `DeleteSelected`) were bound to typed delegates so the
compiler — not a reflection lookup — proves each exists with the right signature.

**`MainMenuUI.LoadGame(string)` was removed.** Confirmed unreferenced first: no C# caller and no
UnityEvent binding in `MainMenu.unity`. `WorldSelectUI.LoadSelected` replaces it.

**Known gap:** `ContinueGame` has no button in `MainMenu.unity`. It is public and works, but is
unreachable from the UI until a button is added — pre-existing, not introduced here.

**Config ids:** `WorldStreamingConfig` → `303234dac994845669b13bb8047213be`,
`FerdinandWorldStreamingConfig` → `9ac62146af2dd49d8b061b281b31bd84`. Distinct, stamped from
each asset's own GUID.

**Not run — needs a human at the keyboard.** Task 10's play-mode steps below (entering a world,
F5/F9 across two worlds, the multiplayer host path) require a focused editor and a Play session.
The EditMode runner also could not be launched over the bridge: `EditorApplication.delayCall`
does not tick while the editor is unfocused, so `HeadlessTestRunner.RunEditModeDeferred` never
fired and `Temp/headless_tests.txt` was never written. The two NUnit fixtures
(`WorldIdentityTests`, `WorldSaveRoundTripTests`) are committed and will run in the Test Runner;
their assertions are the same ones proven above through the bridge.

## Verification Summary

| Requirement | Proven by |
|---|---|
| New world works | Task 8 `NewWorld_OverAnExistingNameIsRefusedByExists`; Task 10 step 1 |
| Saving works | Task 8 `SaveThenLoad_RoundTripsWorldState`; Task 10 steps 2-3 |
| Loading works | Task 8 `SaveThenLoad_RoundTripsWorldState`, `ListedWorlds_ShowTheirDisplayName`; Task 10 step 3 |
| Worlds stay separate | Task 8 `TwoWorlds_DoNotShareState`; Task 10 step 4 |
| Wrong-world guard | Task 8 `ConfigGuard_RefusesASaveFromAnotherWorld`; Task 1 `Rejects_MismatchedConfigId` |
| Old saves still load | Task 8 `LegacySave_WithNoWorldFieldsStillLoads`; Task 1 `Accepts_LegacySaveWithNoConfigId` |
| No path escape | Task 8 `WorldName_CannotEscapeTheSaveRoot`; Task 1 `IdFor_StripsPathSeparators` |
| Multiplayer host picks the world | Task 10 step 5 |
