# Multiplayer Lobby Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One clean multiplayer screen for browsing, creating and joining lobbies, backed by a session that survives into the game world so players can join a running session at any time.

**Architecture:** Extract the Unity Gaming Services state machine out of the 667-line `LobbySystem` into a persistent `LobbySession` singleton that outlives scene loads, leaving `LobbySystem` as a thin scene-side controller that keeps every string-bound `UnityEvent` method name. Drop the lobby lock at game start and publish a `GameState` lobby key instead, so late joiners find the session and Netcode's scene synchronisation pulls them into the running world. Rebuild `LobbyMenu.unity` as one four-tab screen in the existing `UITheme` language.

**Tech Stack:** Unity 6, Netcode for GameObjects 2.9.1, `com.unity.services.multiplayer` 2.1.3 (Relay + Lobby), TextMeshPro, uGUI, NUnit EditMode tests via `HeadlessTestRunner`, `unity-mcp` bridge for scene edits.

---

## Background the engineer needs

**Read the spec first:** `docs/superpowers/specs/2026-08-14-multiplayer-lobby-redesign-design.md`.

**Five project facts that will bite you if you don't know them:**

1. **`UnityEvent` resolves targets by method NAME, at runtime, with no compile-time link.** `LobbyMenu.unity` binds buttons to `LobbySystem` methods this way. Renaming one turns its button into a silent no-op — no exception, no console entry. Every public method listed in Task 4 must keep its exact name and signature, including the deliberately lowercase ones.

2. **Netcode spawns by prefab hash and the server never consults the prefab list.** An unregistered prefab gives you a host that works perfectly and clients that spawn nothing. Solo playtesting cannot find it. If spawning breaks, run `Tools/SpaceGame/Multiplayer/Sync Network Prefabs`.

3. **`NetworkManager` lives only in the Bootstrap scene** and survives via its own `DontDestroyOnLoad`. Pressing Play in `LobbyMenu` directly leaves `NetworkManager.Singleton` null; `NetworkBootstrap` backfills it in-editor only.

4. **Tests live in `Assets/Game/Editor/Tests/`, namespace `SpaceGame.Tests`.** That is an `Editor/` folder, not an asmdef, which is precisely why these tests can reference `Assembly-CSharp` types like `LobbySystem`. Do not move them into `Assets/Game/Tests/EditMode/` — that has an asmdef and cannot see `Assembly-CSharp`.

5. **Running the test suite has two traps that both look like "my fix did nothing".** Delete `Temp/headless_tests.txt` from Bash before every run, and run the suite twice, trusting the second — a run started right after a script edit races the compile and exercises the old assembly.

**How to run the tests (used by every task below):**

```bash
rm -f Temp/headless_tests.txt
```

Then via the MCP bridge, `Unity_RunCommand` with:

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditMode("SpaceGame.Tests.LobbySessionTests");
```

Then poll from Bash:

```bash
until [ -f Temp/headless_tests.txt ] && grep -q DONE Temp/headless_tests.txt; do sleep 2; done; cat Temp/headless_tests.txt
```

`groupNames` is a regex. Omit the argument to run everything.

**Do not put `System.IO`, `System.Reflection`, or `EditorApplication.ExecuteMenuItem` in an MCP command snippet** — the bridge rejects all three with a useless `Object reference not set`. Project code doing file IO is fine; it is only your snippet that is restricted. Test *files* compiled by Unity may use reflection freely.

**Baseline before you start:** the suite sits at roughly `PASSED=501 FAILED=19`. All 19 failures are pre-existing `OrnithopterRigWiringTests` / `OrnithopterWingAnimatorTests` looking for prefabs at pre-restructure paths. Re-measure at Task 0 rather than trusting that number. "Still exactly N pre-existing failures" is the green signal.

---

## File Structure

**Create:**

| Path | Responsibility |
|---|---|
| `Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs` | The only owner of lobby state. Persistent singleton. Create/query/join/leave/begin-game, heartbeat, poll. No `UnityEngine.UI` dependency. |
| `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/DirectConnectController.cs` | The Direct tab's behaviour. Wraps `SessionLauncher.HostDirect` / `JoinDirectAsync`. |
| `Assets/Game/Editor/Tests/LobbySessionTests.cs` | Pure-logic tests for the session's option builders and state helpers. |
| `Assets/Game/Editor/Tests/LobbyMenuWiringTests.cs` | Pins the string-bound method names that `UnityEvent` cannot check at compile time. |

**Modify:**

| Path | Change |
|---|---|
| `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/LobbySystem.cs` | 667 → ~160 lines. Delegates to `LobbySession`, renders the view. Keeps all string-bound names. |
| `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/LobbyListSystem.cs` | Replace child-index walks with serialized fields; replace the scene-name guard with a destroyed-object guard; drop the duplicate text writes in `listNewLobby`. |
| `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Widgets/LobbyElementController.cs` | Real occupancy and a waiting/in-game state label. |
| `Assets/Game/Scenes/Menus/LobbyMenu.unity` | Rebuilt as one four-tab screen. |

**Delete:**

| Path | Why |
|---|---|
| `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Flow/StartingGameManager.cs` (+ `.meta`) | Dead. Duplicates `StartLobbyGame`, hardcodes `LoadScene("Tommy test scene")`. |
| `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/Entity.cs` (+ `.meta`) | Dead health stub, unrelated to lobbies. |
| `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/DirectConnectPanel.cs` (+ `.meta`) | Replaced by `DirectConnectController` in the scene. This is the self-installing IMGUI popup. |

---

## Task 0: Establish the baseline

**Files:** none — measurement only.

- [ ] **Step 1: Confirm the Editor is not in Play mode**

A human in Play mode makes every EditMode run fail silently — the Test Runner throws `This cannot be used during play mode` and never writes the results file, which looks exactly like a hang. This Editor is shared; do not force Play mode off.

Via MCP `Unity_RunCommand`:

```csharp
UnityEngine.Debug.Log($"isPlaying={UnityEditor.EditorApplication.isPlaying}");
```

Expected: `isPlaying=False`. If `True`, stop and report it rather than waiting.

- [ ] **Step 2: Record the pre-change test baseline**

```bash
rm -f Temp/headless_tests.txt
```

MCP `Unity_RunCommand`:

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditMode();
```

```bash
until [ -f Temp/headless_tests.txt ] && grep -q DONE Temp/headless_tests.txt; do sleep 3; done; head -1 Temp/headless_tests.txt
```

Expected: a line like `PASSED=501 FAILED=19 SKIPPED=0 INCONCLUSIVE=0`. **Write the exact numbers down.** Every later task compares against them.

- [ ] **Step 3: Repeat the run once**

Same three commands again. The second run is the trustworthy one. If the two disagree, trust the second and record that.

---

## Task 1: Delete the three dead files

Doing this first means the compiler tells you immediately if anything actually referenced them.

**Files:**
- Delete: `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Flow/StartingGameManager.cs` and `.meta`
- Delete: `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/Entity.cs` and `.meta`
- Delete: `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/DirectConnectPanel.cs` and `.meta`

- [ ] **Step 1: Prove nothing references them**

```bash
grep -rn "StartingGameManager\|DirectConnectPanel" Assets --include=*.cs --include=*.unity --include=*.prefab --include=*.asset
```

Expected: only the files' own declarations, plus `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/DirectConnectPanel.cs` matching itself. No `.unity` or `.prefab` hits.

For `Entity`, grep for the component reference by GUID rather than by name — `Entity` is far too common a word to grep textually:

```bash
grep -m1 guid Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/Entity.cs.meta
```

Take the GUID from that output and search for it:

```bash
grep -rln "<paste-the-guid-here>" Assets --include=*.unity --include=*.prefab --include=*.asset
```

Expected: no output. If a scene or prefab does reference it, stop — remove the component through the Editor first, then come back.

- [ ] **Step 2: Delete the files**

```bash
rm -f Assets/Game/Scripts/Presentation/UI/LobbyMenu/Flow/StartingGameManager.cs \
      Assets/Game/Scripts/Presentation/UI/LobbyMenu/Flow/StartingGameManager.cs.meta \
      Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/Entity.cs \
      Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/Entity.cs.meta \
      Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/DirectConnectPanel.cs \
      Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/DirectConnectPanel.cs.meta
rmdir Assets/Game/Scripts/Presentation/UI/LobbyMenu/Flow 2>/dev/null
rm -f Assets/Game/Scripts/Presentation/UI/LobbyMenu/Flow.meta
```

- [ ] **Step 3: Confirm the project still compiles**

MCP `Unity_RunCommand`:

```csharp
UnityEditor.AssetDatabase.Refresh();
UnityEngine.Debug.Log("refreshed");
```

Then read the console:

MCP `Unity_GetConsoleLogs` — expected: no `CS____` compile errors mentioning `StartingGameManager`, `Entity` or `DirectConnectPanel`.

Note: pre-existing Netcode *warnings* in the log make the MCP call report failure even when the code ran fine. Read the returned log before believing a failure.

- [ ] **Step 4: Commit**

```bash
git add -A Assets/Game/Scripts/Presentation/UI/LobbyMenu
git commit -m "chore: remove dead lobby code (StartingGameManager, Entity, DirectConnectPanel)"
```

---

## Task 2: `LobbySession` option builders (TDD)

The session's two genuinely unit-testable pieces are the option builders. They are also where the two bugs that matter live: the relay code must be written at lobby *creation* rather than by a follow-up update, and `BeginGame` must never set `IsLocked`. Both are one-liners that would be easy to reintroduce, so they get pinned by tests first.

**Files:**
- Create: `Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs`
- Test: `Assets/Game/Editor/Tests/LobbySessionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Game/Editor/Tests/LobbySessionTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Core;
using Unity.Services.Lobbies.Models;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The parts of <see cref="LobbySession"/> that hold without a live Lobby service.
    ///
    /// Creating and joining need Unity Gaming Services and two machines, and are covered by
    /// playing the game. The option objects handed to those calls do not — and every bug that
    /// made the lobby unusable lived in one of them.
    /// </summary>
    public class LobbySessionTests
    {
        [Test]
        public void CreateOptions_CarriesTheRelayCodeAtCreation()
        {
            // Written at creation rather than by a follow-up UpdateLobbyAsync. A client that
            // polled in the gap between the two saw a lobby with no join code and read straight
            // past the missing key.
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, null, "RELAY99", "Ferdinand");

            Assert.IsTrue(options.Data.ContainsKey(LobbySession.KeyRelayJoinCode));
            Assert.AreEqual("RELAY99", options.Data[LobbySession.KeyRelayJoinCode].Value);
        }

        [Test]
        public void CreateOptions_StartsInTheWaitingState()
        {
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, null, "RELAY99", "Ferdinand");

            Assert.AreEqual(LobbySession.StateWaiting, options.Data[LobbySession.KeyGameState].Value);
        }

        [Test]
        public void CreateOptions_PublishesGameStateToNonMembers()
        {
            // The lobby browser shows "waiting" or "in game" on rows the player has not joined,
            // so this key has to be visible to non-members. Member visibility would render every
            // row blank.
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, null, "RELAY99", "Ferdinand");

            Assert.AreEqual(DataObject.VisibilityOptions.Public,
                options.Data[LobbySession.KeyGameState].Visibility);
        }

        [Test]
        public void CreateOptions_CarriesThePlayerName()
        {
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(false, null, "RELAY99", "Ferdinand");

            Assert.AreEqual("Ferdinand", options.Player.Data[LobbySession.KeyPlayerName].Value);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void CreateOptions_TreatsABlankPasswordAsNoPassword(string blank)
        {
            // Lobby rejects an empty-string password outright rather than ignoring it, so a
            // private lobby created with the password field untouched failed to create at all.
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(true, blank, "RELAY99", "Ferdinand");

            Assert.IsNull(options.Password);
        }

        [Test]
        public void CreateOptions_KeepsARealPassword()
        {
            CreateLobbyOptions options = LobbySession.BuildCreateOptions(true, "hunter2", "RELAY99", "Ferdinand");

            Assert.AreEqual("hunter2", options.Password);
        }

        [Test]
        public void BeginGameOptions_NeverLockTheLobby()
        {
            // THE regression test for this feature. Locking the lobby at game start is what made
            // it impossible to join a session already in progress: a locked lobby refuses every
            // join, and the host is usually playing alone when the first friend tries.
            UpdateLobbyOptions options = LobbySession.BuildBeginGameOptions();

            Assert.IsTrue(options.IsLocked == null || options.IsLocked == false,
                "Locking the lobby at start makes late join impossible.");
        }

        [Test]
        public void BeginGameOptions_MarkTheLobbyInGame()
        {
            UpdateLobbyOptions options = LobbySession.BuildBeginGameOptions();

            Assert.AreEqual(LobbySession.StateInGame, options.Data[LobbySession.KeyGameState].Value);
        }

        [Test]
        public void Occupancy_CountsTakenSlotsNotFreeOnes()
        {
            Assert.AreEqual("3/4", LobbySession.DescribeOccupancy(4, 1));
            Assert.AreEqual("0/4", LobbySession.DescribeOccupancy(4, 4));
            Assert.AreEqual("4/4", LobbySession.DescribeOccupancy(4, 0));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
rm -f Temp/headless_tests.txt
```

MCP `Unity_RunCommand`:

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditMode("SpaceGame.Tests.LobbySessionTests");
```

```bash
until [ -f Temp/headless_tests.txt ] && grep -q DONE Temp/headless_tests.txt; do sleep 3; done; cat Temp/headless_tests.txt
```

Expected: the run does not even reach the tests — the console carries a compile error `CS0246: The type or namespace name 'LobbySession' could not be found`. That is the correct failure for this step. Confirm it with MCP `Unity_GetConsoleLogs`.

- [ ] **Step 3: Write the minimal implementation**

Create `Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs` with only what the tests need. The full session lands in Task 3.

```csharp
using System.Collections.Generic;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace SpaceGame.Core
{
    /// <summary>Where a session is in its life. The view renders from this, nothing else.</summary>
    public enum LobbyState { Idle, InLobby, InGame }

    public partial class LobbySession
    {
        /// <summary>Relay join code, so a member can reach the server the host allocated.</summary>
        public const string KeyRelayJoinCode = "RelayJoinCode";

        public const string KeyPlayerName = "PlayerName";

        /// <summary>Whether the host is still in the lobby or already playing. See <see cref="StateInGame"/>.</summary>
        public const string KeyGameState = "GameState";

        public const string StateWaiting = "waiting";
        public const string StateInGame = "in-game";

        /// <summary>
        /// The options a lobby is created with.
        ///
        /// The relay code goes in here rather than into a follow-up UpdateLobbyAsync: a client
        /// polling in the gap between the two saw a lobby with no join code and read straight
        /// past the missing key.
        /// </summary>
        public static CreateLobbyOptions BuildCreateOptions(bool isPrivate, string password,
            string relayJoinCode, string playerName)
        {
            var options = new CreateLobbyOptions
            {
                IsPrivate = isPrivate,
                Player = BuildPlayer(playerName),
                Data = new Dictionary<string, DataObject>
                {
                    { KeyRelayJoinCode, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) },

                    // Public, not Member: the browser labels rows the player has not joined.
                    { KeyGameState, new DataObject(DataObject.VisibilityOptions.Public, StateWaiting) }
                }
            };

            // Lobby rejects an empty-string password rather than ignoring it, so a private lobby
            // created with the field untouched failed to create at all.
            if (isPrivate) options.Password = NullIfBlank(password);

            return options;
        }

        /// <summary>
        /// The options that mark a lobby as playing.
        ///
        /// Deliberately does NOT set IsLocked. Locking here is what made joining a session in
        /// progress impossible — and the host is usually alone when the first friend tries.
        /// </summary>
        public static UpdateLobbyOptions BuildBeginGameOptions() => new()
        {
            Data = new Dictionary<string, DataObject>
            {
                { KeyGameState, new DataObject(DataObject.VisibilityOptions.Public, StateInGame) }
            }
        };

        /// <summary>"3/4" — taken slots over total. Lobby reports FREE slots, which reads inverted.</summary>
        public static string DescribeOccupancy(int maxPlayers, int availableSlots) =>
            $"{maxPlayers - availableSlots}/{maxPlayers}";

        public static Player BuildPlayer(string playerName) => new()
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { KeyPlayerName, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) }
            }
        };

        private static string NullIfBlank(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
```

Note `partial` — Task 3 adds the MonoBehaviour half in the same file. Keeping the pure statics separable is what lets them be tested without a live service.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
rm -f Temp/headless_tests.txt
```

MCP `Unity_RunCommand`:

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditMode("SpaceGame.Tests.LobbySessionTests");
```

```bash
until [ -f Temp/headless_tests.txt ] && grep -q DONE Temp/headless_tests.txt; do sleep 3; done; cat Temp/headless_tests.txt
```

Expected: `PASSED=11 FAILED=0`. Run it a second time and trust that result — the first run may have raced the compile.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs \
        Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs.meta \
        Assets/Game/Editor/Tests/LobbySessionTests.cs \
        Assets/Game/Editor/Tests/LobbySessionTests.cs.meta
git commit -m "feat: add LobbySession option builders with late-join semantics"
```

---

## Task 3: `LobbySession` — the persistent service

Everything here is lifted from `LobbySystem`, not invented. Read `LobbySystem.cs` alongside this task: the heartbeat interval, the poll interval, the in-flight guards and the `busy` guard all carry the reasoning for their values in comments, and those comments move with the code.

**Files:**
- Modify: `Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs`

- [ ] **Step 1: Add the MonoBehaviour half**

Append to `Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs`, and change the class declaration to derive from `MonoBehaviour`:

```csharp
public partial class LobbySession : MonoBehaviour
```

Add these `using` lines at the top of the file:

```csharp
using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.SceneManagement;
```

Then the instance half:

```csharp
        public const int MaxPlayers = 4;

        /// <summary>Lobby delists a lobby not heartbeated inside 30s. 15s leaves room for a hiccup.</summary>
        private const float HeartbeatInterval = 15f;

        /// <summary>Lobby's GET rate limit is one call per second per lobby; 2s stays clear of it.</summary>
        private const float PollInterval = 2f;

        private static LobbySession instance;

        /// <summary>
        /// The session, created on first use.
        ///
        /// Not placed in a scene: it has to outlive every scene, including the one that would
        /// have held it. Created lazily rather than from Bootstrap so that entering LobbyMenu
        /// directly in the editor works — the same reason NetworkBootstrap backfills its manager.
        /// </summary>
        public static LobbySession Instance
        {
            get
            {
                if (instance != null) return instance;

                var host = new GameObject(nameof(LobbySession));
                instance = host.AddComponent<LobbySession>();
                DontDestroyOnLoad(host);
                return instance;
            }
        }

        public LobbyState State { get; private set; } = LobbyState.Idle;

        /// <summary>The lobby this peer is in, or null. Refreshed by the poll.</summary>
        public Lobby Current { get; private set; }

        public bool IsHost =>
            Current != null
            && AuthenticationService.Instance.IsSignedIn
            && Current.HostId == AuthenticationService.Instance.PlayerId;

        /// <summary>Raised whenever the roster, the code or the state moved. The view redraws from this.</summary>
        public event Action Changed;

        /// <summary>A message fit to show a player. Never an exception across this boundary.</summary>
        public event Action<string> Failed;

        private float heartbeatTimer;
        private float pollTimer;

        // Update() fires these on a timer, and a slow request would otherwise be reissued every
        // frame until it returned, tripping the rate limiter and burying the real response.
        private bool heartbeatInFlight;
        private bool pollInFlight;

        /// <summary>One operation at a time. Double-clicking Create allocated two Relay servers.</summary>
        private bool busy;

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;

            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        }

        private void Update()
        {
            Heartbeat();
            Poll();
        }
```

- [ ] **Step 2: Add the public API**

Append to the same class:

```csharp
        /// <summary>Signed in and ready for Relay/Lobby calls. Safe to await repeatedly.</summary>
        public async Task<bool> EnsureReadyAsync()
        {
            SessionResult services = await SessionLauncher.EnsureServicesAsync();
            if (!services.Success) Failed?.Invoke(services.Error);
            return services.Success;
        }

        /// <summary>
        /// Allocates a Relay server, then advertises it as a lobby.
        ///
        /// Relay first. If it fails there is no lobby to clean up — the reverse order left an
        /// orphan lobby advertised to everyone with a join code that led nowhere.
        /// </summary>
        public async Task<bool> CreateAsync(string lobbyName, bool isPrivate, string password)
        {
            if (!TryBegin()) return false;

            try
            {
                if (!await EnsureReadyAsync()) return false;

                SessionResult host = await SessionLauncher.HostRelayAsync(MaxPlayers);
                if (!host.Success) { Failed?.Invoke(host.Error); return false; }

                string name = string.IsNullOrWhiteSpace(lobbyName) ? $"{PlayerName}'s game" : lobbyName;

                Current = await LobbyService.Instance.CreateLobbyAsync(name, MaxPlayers,
                    BuildCreateOptions(isPrivate, password, host.JoinCode, PlayerName));

                State = LobbyState.InLobby;
                Changed?.Invoke();

                Debug.Log($"[LobbySession] Hosting '{Current.Name}' code={Current.LobbyCode} relay={host.JoinCode}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, "Could not create the lobby."));
                SessionLauncher.Shutdown();
                return false;
            }
            finally { busy = false; }
        }

        /// <summary>Public lobbies with room, newest first. Private ones are reachable only by code.</summary>
        public async Task<List<Lobby>> QueryAsync()
        {
            try
            {
                QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count = 25,
                    Filters = new List<QueryFilter>
                    {
                        new(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
                    },
                    Order = new List<QueryOrder> { new(false, QueryOrder.FieldOptions.Created) }
                });

                return response.Results;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, "Could not fetch the lobby list."));
                return new List<Lobby>();
            }
        }

        public Task<bool> JoinByIdAsync(string lobbyId) => JoinAsync(
            () => LobbyService.Instance.JoinLobbyByIdAsync(lobbyId,
                new JoinLobbyByIdOptions { Player = BuildPlayer(PlayerName) }),
            "Could not join that lobby.");

        public Task<bool> JoinByCodeAsync(string lobbyCode, string password = null)
        {
            string code = SessionLauncher.NormalizeJoinCode(lobbyCode);

            if (string.IsNullOrEmpty(code))
            {
                Failed?.Invoke("Enter a lobby code first.");
                return Task.FromResult(false);
            }

            return JoinAsync(
                () => LobbyService.Instance.JoinLobbyByCodeAsync(code, new JoinLobbyByCodeOptions
                {
                    Player = BuildPlayer(PlayerName),
                    Password = NullIfBlank(password)
                }),
                "Could not join with that code.");
        }

        public async Task LeaveAsync()
        {
            string lobbyId = Current?.Id;

            // Local state first, so the UI responds even if the service call hangs.
            Forget();
            SessionLauncher.Shutdown();

            await RemoveSelfQuietly(lobbyId);
        }

        /// <summary>
        /// Marks the lobby as playing and moves everyone into the world.
        ///
        /// The lobby is NOT locked and the heartbeat keeps running, so the lobby stays listed and
        /// anyone joining later is synchronised into the running world by Netcode.
        /// </summary>
        public async Task<bool> BeginGameAsync(string sceneName)
        {
            if (!TryBegin()) return false;

            try
            {
                if (Current == null) { Failed?.Invoke("You are not in a lobby."); return false; }
                if (!IsHost) { Failed?.Invoke("Only the host can start the game!"); return false; }

                NetworkManager manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsServer)
                {
                    Failed?.Invoke("The host is not running a server. Try recreating the lobby.");
                    return false;
                }

                Current = await LobbyService.Instance.UpdateLobbyAsync(Current.Id, BuildBeginGameOptions());
                State = LobbyState.InGame;
                Changed?.Invoke();

                Debug.Log($"[LobbySession] Starting '{sceneName}' for {manager.ConnectedClientsIds.Count} client(s). " +
                          "Lobby stays open for late joiners.");

                manager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, "Could not start the game."));
                return false;
            }
            finally { busy = false; }
        }

        /// <summary>The names to show in the roster, in lobby order.</summary>
        public static string[] PlayerNames(Lobby lobby)
        {
            if (lobby?.Players == null) return Array.Empty<string>();

            var names = new string[lobby.Players.Count];

            for (int i = 0; i < lobby.Players.Count; i++)
            {
                Player p = lobby.Players[i];

                // Defensive on both the dictionary and the entry: a player written by an older
                // build, or one still mid-join, may not carry the name key, and an unguarded
                // indexer threw KeyNotFoundException every poll and killed the whole roster.
                names[i] = p?.Data != null && p.Data.TryGetValue(KeyPlayerName, out PlayerDataObject value)
                    ? value.Value
                    : "Player";
            }

            return names;
        }

        /// <summary>True when this lobby's host is already playing, so a joiner skips the lobby screen.</summary>
        public static bool IsPlaying(Lobby lobby) =>
            lobby?.Data != null
            && lobby.Data.TryGetValue(KeyGameState, out DataObject state)
            && state.Value == StateInGame;
```

- [ ] **Step 3: Add the private half**

Append:

```csharp
        /// <summary>The name shown to other players. One identity, shared with PlayerIdentity in-game.</summary>
        private static string PlayerName => GameSettings.PlayerName;

        /// <summary>
        /// Joins, then connects to the Relay server the lobby advertises.
        ///
        /// Lobby membership is rolled back if the Relay connection fails. Otherwise a failed
        /// connection leaves a ghost occupying a slot in a lobby it is not in, which is how a
        /// four-player lobby ends up refusing a third player.
        /// </summary>
        private async Task<bool> JoinAsync(Func<Task<Lobby>> join, string failureHeadline)
        {
            if (!TryBegin()) return false;

            try
            {
                if (!await EnsureReadyAsync()) return false;

                Lobby lobby = await join();
                if (lobby == null) { Failed?.Invoke("The lobby service returned nothing."); return false; }

                if (!lobby.Data.TryGetValue(KeyRelayJoinCode, out DataObject relayCode)
                    || string.IsNullOrEmpty(relayCode.Value))
                {
                    await RemoveSelfQuietly(lobby.Id);
                    Failed?.Invoke("That lobby has no Relay server attached. The host may still be setting it up.");
                    return false;
                }

                SessionResult connected = await SessionLauncher.JoinRelayAsync(relayCode.Value);
                if (!connected.Success)
                {
                    await RemoveSelfQuietly(lobby.Id);
                    Failed?.Invoke(connected.Error);
                    return false;
                }

                Current = lobby;
                State = IsPlaying(lobby) ? LobbyState.InGame : LobbyState.InLobby;
                Changed?.Invoke();

                Debug.Log($"[LobbySession] Joined '{lobby.Name}' ({State}).");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Failed?.Invoke(Describe(e, failureHeadline));
                return false;
            }
            finally { busy = false; }
        }

        private async void Heartbeat()
        {
            if (Current == null || !IsHost || heartbeatInFlight) return;

            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer > 0f) return;

            heartbeatTimer = HeartbeatInterval;
            heartbeatInFlight = true;

            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(Current.Id);
            }
            catch (Exception e)
            {
                // Not surfaced: a missed heartbeat only delists the lobby from search, and the
                // session keeps working. A popup here would fire repeatedly over a flaky
                // connection and bury messages that actually need acting on.
                Debug.LogWarning($"[LobbySession] Heartbeat failed: {e.Message}");
            }
            finally { heartbeatInFlight = false; }
        }

        private async void Poll()
        {
            if (Current == null || pollInFlight) return;

            pollTimer -= Time.deltaTime;
            if (pollTimer > 0f) return;

            pollTimer = PollInterval;
            pollInFlight = true;

            try
            {
                Lobby lobby = await LobbyService.Instance.GetLobbyAsync(Current.Id);

                // Leaving nulls Current while this request is in flight; writing the stale result
                // back would resurrect a lobby we have already left.
                if (Current == null) return;

                Current = lobby;
                Changed?.Invoke();
            }
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound)
            {
                Failed?.Invoke("The host closed the lobby.");
                Forget();
                SessionLauncher.Shutdown();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbySession] Poll failed: {e.Message}");
            }
            finally { pollInFlight = false; }
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.IsHost) return;
            if (clientId != manager.LocalClientId) return;

            string reason = manager.DisconnectReason;
            Debug.Log($"[LobbySession] Disconnected. Reason: '{reason}'");

            // Any disconnect strands us, not just a clean host shutdown. Matching on the exact
            // reason string left the player in a session that no longer existed.
            Failed?.Invoke(string.IsNullOrEmpty(reason) ? "Lost connection to the host." : reason);
            Forget();
        }

        private void Forget()
        {
            Current = null;
            State = LobbyState.Idle;
            Changed?.Invoke();
        }

        /// <summary>Best-effort removal. Never reports — callers are already handling a failure.</summary>
        private static async Task RemoveSelfQuietly(string lobbyId)
        {
            try
            {
                if (!string.IsNullOrEmpty(lobbyId) && AuthenticationService.Instance.IsSignedIn)
                    await LobbyService.Instance.RemovePlayerAsync(lobbyId, AuthenticationService.Instance.PlayerId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbySession] Could not remove self from {lobbyId}: {e.Message}");
            }
        }

        private bool TryBegin()
        {
            if (busy)
            {
                Debug.Log("[LobbySession] Ignoring input — an operation is already running.");
                return false;
            }

            busy = true;
            return true;
        }

        private static string Describe(Exception e, string headline) =>
            e is LobbyServiceException lobbyException
                ? $"{headline}\n({lobbyException.Reason}: {lobbyException.Message})"
                : $"{headline}\n({e.GetType().Name}: {e.Message})";
```

- [ ] **Step 4: Verify it compiles and the Task 2 tests still pass**

```bash
rm -f Temp/headless_tests.txt
```

MCP `Unity_RunCommand`:

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditMode("SpaceGame.Tests.LobbySessionTests");
```

```bash
until [ -f Temp/headless_tests.txt ] && grep -q DONE Temp/headless_tests.txt; do sleep 3; done; cat Temp/headless_tests.txt
```

Expected: `PASSED=11 FAILED=0`, unchanged from Task 2. Run twice.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs
git commit -m "feat: LobbySession owns lobby state and survives scene loads"
```

---

## Task 4: Narrow `LobbySystem` onto the session

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/LobbySystem.cs`
- Test: `Assets/Game/Editor/Tests/LobbyMenuWiringTests.cs`

- [ ] **Step 1: Write the failing wiring test**

`UnityEvent` binds these by name with no compile-time link, so nothing else can catch their removal. Create `Assets/Game/Editor/Tests/LobbyMenuWiringTests.cs`:

```csharp
using System.Reflection;
using NUnit.Framework;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Pins the method names LobbyMenu.unity binds its buttons to.
    ///
    /// UnityEvent resolves targets by string at runtime and silently drops any it cannot find —
    /// no exception, no console entry — so renaming one of these turns its button into a dead
    /// control that nothing anywhere reports. The compiler cannot see the binding; this can.
    ///
    /// The lowercase names are deliberate. They are what the scene already contains.
    /// </summary>
    public class LobbyMenuWiringTests
    {
        private const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;

        [TestCase("createLobbyWithGivenOptions")]
        [TestCase("listLobbies")]
        [TestCase("JoinLobbyById")]
        [TestCase("JoinLobbyByCode")]
        [TestCase("JoinLobbyByPassword")]
        [TestCase("LeaveLobby")]
        [TestCase("StartLobbyGame")]
        public void LobbySystem_KeepsItsSceneBoundMethods(string methodName)
        {
            Assert.IsNotNull(typeof(LobbySystem).GetMethod(methodName, Public),
                $"LobbyMenu.unity binds a button to LobbySystem.{methodName} by name. " +
                "Removing or renaming it makes that button silently do nothing.");
        }

        [TestCase("closeJoinPrivateLobbyScreen")]
        [TestCase("changeStateOfPasswordInputFieldCreateLobby")]
        public void LobbyListSystem_KeepsItsSceneBoundMethods(string methodName)
        {
            Assert.IsNotNull(typeof(LobbyListSystem).GetMethod(methodName, Public),
                $"LobbyMenu.unity binds a control to LobbyListSystem.{methodName} by name.");
        }

        [Test]
        public void LobbySystem_ExposesTheGameSceneNameTheDirectPathAlsoUses()
        {
            Assert.IsNotNull(typeof(LobbySystem).GetProperty("GameSceneName", Public),
                "DirectConnectController reads the game scene from here so the two paths cannot drift.");
        }
    }
}
```

- [ ] **Step 2: Run it to verify it passes against the current file**

```bash
rm -f Temp/headless_tests.txt
```

MCP `Unity_RunCommand`:

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditMode("SpaceGame.Tests.LobbyMenuWiringTests");
```

```bash
until [ -f Temp/headless_tests.txt ] && grep -q DONE Temp/headless_tests.txt; do sleep 3; done; cat Temp/headless_tests.txt
```

Expected: `PASSED=10 FAILED=0`. This test passes *before* the rewrite on purpose — it is a guard rail for Step 3, not a red-green cycle. If it fails now, the method list above is wrong; fix the list against the real file before rewriting anything.

- [ ] **Step 3: Replace the body of `LobbySystem.cs`**

Overwrite `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/LobbySystem.cs` entirely:

```csharp
using System.Collections.Generic;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Presentation;

/// <summary>
/// The lobby menu's controller: it decides WHEN to act and what the screen shows. What actually
/// happens lives in <see cref="LobbySession"/>, which outlives this object and this scene.
///
/// Deliberately in the global namespace with these exact method names — LobbyMenu.unity binds its
/// buttons to them by string through UnityEvent, which resolves at runtime with no compile-time
/// link. Renaming any of them turns its button into a silent no-op with nothing logged.
/// LobbyMenuWiringTests pins them.
///
/// A MonoBehaviour, not a NetworkBehaviour. NetworkBehaviour requires a NetworkObject, and this
/// sits in a scene loaded by plain SceneManager rather than Netcode's — so starting a host made
/// Netcode try to spawn an in-scene object joining clients had no matching copy of, which is a
/// synchronisation failure rather than a warning.
/// </summary>
public class LobbySystem : MonoBehaviour
{
    [SerializeField] private SceneReference gameScene;

    /// <summary>Kept for the scene's own inspector references. The session owns the real limit.</summary>
    public static int maxPlayers = LobbySession.MaxPlayers;

    /// <summary>
    /// The scene the lobby hands off to. Exposed so the Relay-free direct path loads the same one
    /// rather than keeping a second copy of the name that can drift.
    /// </summary>
    public string GameSceneName => gameScene != null ? gameScene.SceneName : null;

    private LobbyListSystem lobbyList;
    private LobbyWarningSystem warningSystem;
    private LobbySession session;

    /// <summary>
    /// The code last tried, so a password prompt can retry the same lobby. A private lobby is
    /// excluded from query results, so it is reachable only by code — there is no listed entry to
    /// click and no id to look up.
    /// </summary>
    private string lastAttemptedJoinCode;

    private void Awake()
    {
        // Before any await. Resolved after `await` these were null whenever initialisation failed
        // — and every error path here reports through warningSystem, so the one situation that
        // most needed a message on screen instead threw inside an async void and vanished.
        lobbyList = GetComponent<LobbyListSystem>();
        warningSystem = GetComponent<LobbyWarningSystem>();
    }

    private async void Start()
    {
        session = LobbySession.Instance;
        session.Changed += Render;
        session.Failed += Warn;

        Render();

        if (!await session.EnsureReadyAsync()) return;

        NetworkBootstrap.LogRegisteredPrefabCount();
        listLobbies();
    }

    private void OnDestroy()
    {
        if (session == null) return;

        session.Changed -= Render;
        session.Failed -= Warn;
    }

    // ───────────────────────────────────────────────
    //  Bound by NAME from LobbyMenu.unity. Do not rename.
    // ───────────────────────────────────────────────

    public async void createLobbyWithGivenOptions() =>
        await session.CreateAsync(
            lobbyList.getLobbyNameInputText(),
            lobbyList.getLobbyPrivate(),
            lobbyList.getLobbyPasswordInputText());

    public async void listLobbies()
    {
        List<Lobby> lobbies = await session.QueryAsync();

        if (lobbyList == null) return;

        lobbyList.clearPrevList();
        foreach (Lobby lobby in lobbies)
            lobbyList.listNewLobby(lobby);
    }

    public async void JoinLobbyById(string id)
    {
        if (await session.JoinByIdAsync(id)) EnterIfPlaying();
    }

    public async void JoinLobbyByCode(string lobbyCode)
    {
        lastAttemptedJoinCode = SessionLauncher.NormalizeJoinCode(lobbyCode);
        if (await session.JoinByCodeAsync(lastAttemptedJoinCode)) EnterIfPlaying();
    }

    /// <summary>Retries the last code with a password, for a lobby that turned out to be protected.</summary>
    public async void JoinLobbyByPassword(string lobbyPassword)
    {
        if (string.IsNullOrWhiteSpace(lobbyPassword)) { Warn("Enter the lobby password first."); return; }
        if (string.IsNullOrEmpty(lastAttemptedJoinCode)) { Warn("Enter the lobby code first, then the password."); return; }

        if (await session.JoinByCodeAsync(lastAttemptedJoinCode, lobbyPassword)) EnterIfPlaying();
    }

    public async void LeaveLobby()
    {
        await session.LeaveAsync();
        listLobbies();
    }

    public async void StartLobbyGame()
    {
        string scene = GameSceneName;
        if (string.IsNullOrEmpty(scene)) { Warn("No game scene is configured on this lobby."); return; }

        // Up before the load starts and held until terrain streaming and the NavMesh bake finish
        // — those run after the scene reports loaded and are what makes the first seconds stutter.
        LoadingScreenUI.ShowUntilReady(scene);

        if (!await session.BeginGameAsync(scene)) LoadingScreenUI.Dismiss();
    }

    // ───────────────────────────────────────────────
    //  View
    // ───────────────────────────────────────────────

    /// <summary>
    /// Redraws the screen from the session. Called on every session change rather than written to
    /// piecemeal at each call site, so there is one description of what the screen shows.
    /// </summary>
    private void Render()
    {
        if (lobbyList == null || session == null) return;

        if (session.Current == null)
        {
            lobbyList.hideLobbyScreen();
            lobbyList.setStartGameButtonState(false);
            return;
        }

        lobbyList.openLobbyScreen(session.Current.Name, session.Current.LobbyCode);
        lobbyList.setStartGameButtonState(session.IsHost);
        lobbyList.showPlayerElements(LobbySession.PlayerNames(session.Current));
    }

    /// <summary>
    /// A joiner whose host is already playing skips the lobby screen entirely. Netcode's scene
    /// synchronisation pulls them into the running world; this only puts something on screen
    /// while that happens.
    /// </summary>
    private void EnterIfPlaying()
    {
        if (session.State != LobbyState.InGame) return;

        string scene = GameSceneName;
        if (!string.IsNullOrEmpty(scene))
            LoadingScreenUI.ShowUntilReady(scene, "Joining");
    }

    private void Warn(string message)
    {
        Debug.LogWarning($"[Lobby] {message}");

        // Still useful without the panel wired: this class is reachable from scenes with no
        // warning strip, and losing the message entirely is how the original failed silently.
        if (warningSystem != null) warningSystem.warn(message);
    }
}
```

- [ ] **Step 4: Run the wiring tests and the session tests**

```bash
rm -f Temp/headless_tests.txt
```

MCP `Unity_RunCommand`:

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditMode("SpaceGame.Tests.Lobby.*");
```

```bash
until [ -f Temp/headless_tests.txt ] && grep -q DONE Temp/headless_tests.txt; do sleep 3; done; cat Temp/headless_tests.txt
```

Expected: `PASSED=21 FAILED=0` (11 session + 10 wiring). Run twice.

- [ ] **Step 5: Confirm the line count actually came down**

```bash
wc -l Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/LobbySystem.cs
```

Expected: roughly 160, down from 667. If it is still over 250, logic that belongs in the session stayed behind.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/LobbySystem.cs \
        Assets/Game/Editor/Tests/LobbyMenuWiringTests.cs \
        Assets/Game/Editor/Tests/LobbyMenuWiringTests.cs.meta
git commit -m "refactor: LobbySystem delegates to LobbySession, 667 -> 160 lines"
```

---

## Task 5: Harden the pre-July view scripts

These files stay — they are only being made safe for a session that now outlives them.

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/LobbyListSystem.cs`
- Modify: `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Widgets/LobbyElementController.cs`

- [ ] **Step 1: Replace the child-index walk in `openLobbyScreen`**

In `LobbyListSystem.cs`, add two serialized fields beside the existing `lobbyScreen` field:

```csharp
    [SerializeField]
    [Tooltip("The lobby name shown on the in-lobby screen.")]
    private TextMeshProUGUI lobbyScreenTitle;

    [SerializeField]
    [Tooltip("The 'Code: ABC123' line on the in-lobby screen.")]
    private TextMeshProUGUI lobbyScreenCode;
```

Then replace the whole `openLobbyScreen` method:

```csharp
    /// <summary>
    /// Shows the in-lobby screen.
    ///
    /// The title and code are serialized rather than reached through lobbyScreen.GetChild(0) and
    /// GetChild(1). That chain ran on every lobby poll, so reordering the screen's children in the
    /// inspector — a thing anyone editing this menu will do — turned it into an exception twice a
    /// second. FindPlayerListContainer below already documents the same fault.
    /// </summary>
    public void openLobbyScreen(string lobbyName, string lobbyCode)
    {
        if (lobbyScreen == null) return;

        lobbyScreen.SetActive(true);

        if (lobbyScreenTitle != null) lobbyScreenTitle.text = lobbyName;
        if (lobbyScreenCode != null) lobbyScreenCode.text = "Code: " + lobbyCode;
    }
```

- [ ] **Step 2: Replace the scene-name guards with destroyed-object guards**

The existing guard is `if (SceneManager.GetActiveScene().name != "LobbyMenu") return;` on
`showPlayerElements` and `setStartGameButtonState`. It answers the right question the wrong way:
it hardcodes a scene name, and it does not protect the four methods that were never guarded.

Asking whether the serialized reference is still alive is both more direct and more robust —
Unity's overloaded `==` reports a destroyed object as null, so this covers the scene unloading for
any reason, not just the one named scene.

In `showPlayerElements`, delete:

```csharp
        if(SceneManager.GetActiveScene().name != "LobbyMenu")
        {
            return;
        }
```

and replace with:

```csharp
        // The session outlives this scene, so it can still push a roster after the canvas has
        // been destroyed. A destroyed GameObject compares equal to null, which is what makes this
        // both the liveness check and the null check.
        if (lobbyScreen == null || playerDisplayElement == null) return;
```

In `setStartGameButtonState`, delete the same two lines and replace with:

```csharp
        if (startGameButton == null) return;
```

Add the same guard as the first line of the four previously unguarded methods:

```csharp
    public void hideLobbyScreen()
    {
        if (lobbyScreen == null) return;
        lobbyScreen.SetActive(false);
    }

    public void clearPrevList()
    {
        if (lobbyElementContainer == null) return;

        foreach (Transform t in lobbyElementContainer.transform)
            Destroy(t.gameObject);
    }
```

Note `clearPrevList` also changes from `GetComponentInChildren<Transform>()` to `.transform`.
`GetComponentInChildren<Transform>()` returns the container's *own* transform, and iterating a
Transform yields its children — so the old line happened to work, by accident, through a method
that reads as if it does something else.

- [ ] **Step 3: Drop the duplicate text writes in `listNewLobby`**

The current method sets the same two labels twice — once through `LobbyElementController`'s
setters and again by walking `GetChild(0)` and `GetChild(1)`. Replace it:

```csharp
    public void listNewLobby(Lobby lobby)
    {
        if (lobbyElementContainer == null || lobbyElement == null) return;

        GameObject newLobbyElement = Instantiate(lobbyElement, lobbyElementContainer.transform, false);

        // Through the controller only. The previous version also wrote the same two labels by
        // child index, so the row had two sources of truth that could disagree.
        LobbyElementController controller = newLobbyElement.GetComponent<LobbyElementController>();
        controller.setlobbyName(lobby.Name);
        controller.setLobbyId(lobby.Id);
        controller.setOccupancy(lobby.MaxPlayers, lobby.AvailableSlots);
        controller.setPlaying(SpaceGame.Core.LobbySession.IsPlaying(lobby));
    }
```

- [ ] **Step 4: Give `LobbyElementController` real occupancy and a state label**

In `LobbyElementController.cs`, replace `setMaxPlayers` with `setOccupancy`, add `setPlaying`, and
put the unused `lobbyCodeUI` field to work as the state label. Replace the fields and those methods:

```csharp
  [SerializeField]
  private TextMeshProUGUI lobbyNameUI;

  [SerializeField]
  private TextMeshProUGUI maxPlayersUI;

  [SerializeField]
  [Tooltip("Shows 'waiting' or 'in game' for this row.")]
  private TextMeshProUGUI lobbyStateUI;

  public void setlobbyName(string newLobbyName){
    lobbyName = newLobbyName;
    if (lobbyNameUI != null) lobbyNameUI.text = newLobbyName;
  }

  /// <summary>
  /// "3/4" — taken over total. Lobby reports FREE slots, and the previous version hardcoded the
  /// taken count to 0, so every row in the browser claimed to be empty.
  /// </summary>
  public void setOccupancy(int newMaxPlayers, int availableSlots) {
    maxPlayers = newMaxPlayers;
    if (maxPlayersUI != null)
      maxPlayersUI.text = SpaceGame.Core.LobbySession.DescribeOccupancy(newMaxPlayers, availableSlots);
  }

  public void setPlaying(bool playing) {
    if (lobbyStateUI != null) lobbyStateUI.text = playing ? "in game" : "waiting";
  }
```

Delete the now-unused `lobbyIdUI` and `lobbyCodeUI` fields and the `getLobbyName` /
`getMaxPlayers` accessors — nothing calls them.

- [ ] **Step 5: Verify the whole suite is back to baseline**

```bash
rm -f Temp/headless_tests.txt
```

MCP `Unity_RunCommand`:

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditMode();
```

```bash
until [ -f Temp/headless_tests.txt ] && grep -q DONE Temp/headless_tests.txt; do sleep 3; done; head -1 Temp/headless_tests.txt
```

Expected: `FAILED=` the same pre-existing count recorded in Task 0, and `PASSED=` the Task 0 number
plus 21. Run twice.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Presentation/UI/LobbyMenu
git commit -m "fix: harden lobby view against a session that outlives its scene"
```

---

## Task 6: `DirectConnectController`

Replaces the deleted IMGUI popup with a tab in the screen.

**Files:**
- Create: `Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/DirectConnectController.cs`

- [ ] **Step 1: Write the file**

```csharp
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Host or join by IP, with no Unity Gaming Services involved.
    ///
    /// This exists because every other way into a game depends on Relay and Lobby, and those can
    /// be unavailable for reasons no amount of code quality prevents: services not enabled on the
    /// dashboard, an expired org seat, a campus network blocking UDP to Relay, an outage. When
    /// that happens the Relay path cannot even report accurately — a Relay misconfiguration
    /// surfaces as a connection that hangs, not as an error. Two people on the same network can
    /// always use this instead.
    ///
    /// The scene binds these methods by name, like the rest of this menu.
    /// </summary>
    public class DirectConnectController : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI localAddressLabel;
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private LobbyWarningSystem warningSystem;
        [SerializeField] private LobbySystem lobby;
        [SerializeField] private ushort port = SessionLauncher.DefaultDirectPort;

        private void OnEnable()
        {
            if (localAddressLabel != null)
                localAddressLabel.text = $"{SessionLauncher.GetLocalIPv4()}:{port}";
        }

        public void CopyLocalAddress() =>
            GUIUtility.systemCopyBuffer = $"{SessionLauncher.GetLocalIPv4()}:{port}";

        public void HostDirect()
        {
            SessionResult result = SessionLauncher.HostDirect(port);
            if (!result.Success) { Warn(result.Error); return; }

            string scene = lobby != null ? lobby.GameSceneName : null;
            if (string.IsNullOrEmpty(scene)) { Warn("Hosting, but no game scene is configured to load."); return; }

            // Straight into the game rather than waiting in a menu. Clients that connect later are
            // synchronised into whatever scene the host is in, and NetworkGameManager spawns a
            // player for each as it connects — so there is no window in which joining is too late.
            LoadingScreenUI.ShowUntilReady(scene);
            NetworkManager.Singleton.SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }

        public async void JoinDirect()
        {
            string address = addressInput != null ? addressInput.text : null;

            SessionResult result = await SessionLauncher.JoinDirectAsync(address, port);
            if (!result.Success) { Warn(result.Error); return; }

            string scene = lobby != null ? lobby.GameSceneName : null;
            if (!string.IsNullOrEmpty(scene))
                LoadingScreenUI.ShowUntilReady(scene, "Joining");
        }

        private void Warn(string message)
        {
            Debug.LogWarning($"[DirectConnect] {message}");
            if (warningSystem != null) warningSystem.warn(message);
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

MCP `Unity_RunCommand`:

```csharp
UnityEditor.AssetDatabase.Refresh();
UnityEngine.Debug.Log("refreshed");
```

Then MCP `Unity_GetConsoleLogs`. Expected: no `CS____` errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/DirectConnectController.cs \
        Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/DirectConnectController.cs.meta
git commit -m "feat: direct connect as a screen tab instead of a self-installing IMGUI panel"
```

---

## Task 7: Rebuild `LobbyMenu.unity`

**Revised during execution.** The original plan drove the rebuild through one-off MCP calls. That
was replaced by a re-runnable editor script, for two reasons: the repo already builds its complex
assets this way (`DesertCrawlerBuilder`, `HorseBuilder`, `OrnithopterBuilder`, `WingPackBuilder`),
and a scene assembled by a pile of individual tool calls cannot be reviewed, diffed, or re-run after
someone edits the scene by hand.

**Files:**
- Create: `Assets/Game/Editor/Multiplayer/LobbyMenuBuilder.cs`
- Modify: `Assets/Game/Scenes/Menus/LobbyMenu.unity` (as the builder's output)

Run from `Tools ▸ SpaceGame ▸ Multiplayer ▸ Rebuild Lobby Menu`. The builder refuses to run in Play
mode, because a scene built during play mode is discarded when play mode ends.

**Target hierarchy:**

```
LobbyMenuCanvas  (Canvas ScreenSpaceOverlay, CanvasScaler 1920x1080 match 0.5, GraphicRaycaster)
├── Header
│   ├── Title            "MULTIPLAYER"   TitleSize, Bright
│   └── BackButton       -> MainMenuUI scene load
├── Tabs
│   ├── BrowseTab   CreateTab   CodeTab   DirectTab      (Buttons)
├── Bodies
│   ├── BrowseBody   (OpenCloseUIElement, isOpenByDefault = true)
│   │   ├── Scroll / Viewport / GridContent      <- lobbyElementContainer
│   │   └── RefreshButton                        -> LobbySystem.listLobbies
│   ├── CreateBody   (OpenCloseUIElement)
│   │   ├── LobbyNameInput                       <- lobbyNameInputField
│   │   ├── IsPrivateInputToggle                 <- lobbyPrivateToggle
│   │   ├── LobbyPassword                        <- lobbyPasswordObject
│   │   │   └── LobbyPasswordInput               <- passwordInputField
│   │   └── CreateLobbyButton                    -> createLobbyWithGivenOptions
│   ├── CodeBody     (OpenCloseUIElement)
│   │   ├── JoinCodeInput
│   │   ├── JoinByCodeButton                     -> JoinLobbyByCode
│   │   └── JoinLobbyByPasswordPanel             <- joinPrivateLobbyPanel
│   │       ├── LobbyPasswordInput
│   │       └── JoinPrivateLobbyButton           -> JoinLobbyByPassword
│   └── DirectBody   (OpenCloseUIElement, DirectConnectController)
│       ├── LocalAddressLabel                    <- localAddressLabel
│       ├── CopyButton                           -> CopyLocalAddress
│       ├── AddressInput                         <- addressInput
│       ├── HostButton                           -> HostDirect
│       └── JoinButton                           -> JoinDirect
├── LobbyScreen  (inactive by default)           <- lobbyScreen
│   ├── LobbyScreenTitle                         <- lobbyScreenTitle
│   ├── LobbyScreenCode                          <- lobbyScreenCode
│   ├── PlayerList                               (found BY NAME — do not rename)
│   ├── StartGameButton                          <- startGameButton, -> StartLobbyGame
│   └── LeaveLobbyButton                         -> LeaveLobby
├── StatusStrip
│   └── WarningText                              <- warningPanelErrorMessage
└── LobbyManager  (LobbySystem, LobbyListSystem, LobbyWarningSystem)
    UIManager     (LobbyUIManager)
```

**Two names are load-bearing and must match exactly:** `PlayerList` (found by name in
`LobbyListSystem.FindPlayerListContainer`) and `JoinLobbyByPasswordPanel` (the fallback lookup in
`ResolveJoinPrivateLobbyPanel`).

- [ ] **Step 1: Back up the scene before touching it**

```bash
cp "Assets/Game/Scenes/Menus/LobbyMenu.unity" /private/tmp/claude-501/-Users-ferdinandfremming-Documents-hackerspace-spillgruppen-SpaceGame/b2a6ddd2-10a5-444a-9de8-b84408c231f0/scratchpad/LobbyMenu.unity.bak
```

- [ ] **Step 2: Open the scene in the Editor**

MCP `Unity_ManageAsset` or `Unity_RunCommand`:

```csharp
UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
    "Assets/Game/Scenes/Menus/LobbyMenu.unity",
    UnityEditor.SceneManagement.OpenSceneMode.Single);
UnityEngine.Debug.Log("opened");
```

- [ ] **Step 3: Record what the old canvas was wired to, then delete it**

Before deleting, dump the current serialized state so nothing is lost:

MCP `Unity_ManageGameObject` with `action: get_components`, `target: LobbyManager`,
`include_non_public_serialized: true`. Save the output — it holds the `SceneReference gameScene`
value that must be re-applied to the rebuilt `LobbyManager`.

Then delete the old visual root (keep `LobbyManager`, `UIManager`, `EventSystem`, `Main Camera`,
`Directional Light`):

MCP `Unity_ManageGameObject` with `action: delete`, `target: MainMenuCanvas`.

- [ ] **Step 4: Build the new hierarchy**

Create each object with MCP `Unity_ManageGameObject` `action: create`, following the target
hierarchy above, parenting with the `parent` argument. Use the `UITheme` colours for every
`Image` and `TextMeshProUGUI`:

| Role | Colour | Size |
|---|---|---|
| Screen title | `UITheme.Bright` | `UITheme.TitleSize` (64) |
| Tab label | `UITheme.Muted`, selected `UITheme.Accent` | `UITheme.TabSize` (26) |
| Panel background | `UITheme.Panel`, sprite `UITheme.PanelSprite` | — |
| Row background | `UITheme.PanelRaised`, sprite `UITheme.ChipSprite` | — |
| Row label | `UITheme.Bright` | `UITheme.LabelSize` (22) |
| Row secondary | `UITheme.Faint` | `UITheme.CaptionSize` (17) |
| Error text | `UITheme.Danger` | `UITheme.CaptionSize` (17) |

- [ ] **Step 5: Wire the serialized fields**

MCP `Unity_ManageGameObject` with `action: set_component_property`. Scene-object references use the
dict form, e.g. for `LobbyListSystem.lobbyScreen`:

```json
{
  "action": "set_component_property",
  "target": "LobbyManager",
  "component_name": "LobbyListSystem",
  "component_properties": {
    "LobbyListSystem": {
      "lobbyScreen":       {"find": "LobbyScreen", "method": "by_name"},
      "lobbyScreenTitle":  {"find": "LobbyScreenTitle", "component": "TextMeshProUGUI"},
      "lobbyScreenCode":   {"find": "LobbyScreenCode", "component": "TextMeshProUGUI"},
      "lobbyElementContainer": {"find": "GridContent", "method": "by_name"},
      "startGameButton":   {"find": "StartGameButton", "method": "by_name"},
      "joinPrivateLobbyPanel": {"find": "JoinLobbyByPasswordPanel", "method": "by_name"}
    }
  }
}
```

Repeat for `lobbyNameInputField`, `lobbyPrivateToggle`, `passwordInputField`,
`lobbyPasswordObject`, `playerDisplayElement` (the prefab, by asset path
`Assets/Game/Prefabs/UI/LobbyMenu/PlayerDisplayElement.prefab`), `lobbyElement`
(`Assets/Game/Prefabs/UI/LobbyMenu/LobbyElement.prefab`), then `LobbyWarningSystem.warningPanel` /
`warningPanelErrorMessage`, then `DirectConnectController`'s five fields, then re-apply
`LobbySystem.gameScene` from the value dumped in Step 3.

- [ ] **Step 6: Wire the buttons**

Each tab body carries an `OpenCloseUIElement` whose `openButtons` is its own tab and whose
`closeButtons` are the other three. `LobbyUIManager` reads these on Start and attaches the
listeners, so no per-button UnityEvent is needed for tab switching.

The action buttons DO need `onClick` entries pointing at the named methods:
`CreateLobbyButton → LobbySystem.createLobbyWithGivenOptions`,
`RefreshButton → LobbySystem.listLobbies`,
`JoinByCodeButton → LobbySystem.JoinLobbyByCode`,
`JoinPrivateLobbyButton → LobbySystem.JoinLobbyByPassword`,
`StartGameButton → LobbySystem.StartLobbyGame`,
`LeaveLobbyButton → LobbySystem.LeaveLobby`,
`CopyButton → DirectConnectController.CopyLocalAddress`,
`HostButton → DirectConnectController.HostDirect`,
`JoinButton → DirectConnectController.JoinDirect`.

Also set `LobbyElement.prefab`'s join button to `LobbyElementController.attemptJoin` if it is not
already, and set `LobbyElementController.lobbyStateUI` on that prefab to its state label.

- [ ] **Step 7: Save the scene**

MCP `Unity_RunCommand`:

```csharp
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
UnityEngine.Debug.Log("saved");
```

- [ ] **Step 8: Verify no field was left unassigned**

MCP `Unity_ManageGameObject` `action: get_components`, `target: LobbyManager`,
`include_non_public_serialized: true`.

Expected: every serialized field on `LobbySystem`, `LobbyListSystem` and `LobbyWarningSystem` has a
non-zero `fileID`. Any `{fileID: 0}` is an unwired field and a button that will silently do nothing.
Do the same for `DirectConnectController` on `DirectBody`.

- [ ] **Step 9: Commit**

```bash
git add "Assets/Game/Scenes/Menus/LobbyMenu.unity" Assets/Game/Prefabs/UI/LobbyMenu
git commit -m "feat: rebuild LobbyMenu as one four-tab screen"
```

---

## Task 8: Play-mode verification

Unit tests cannot prove late join. Two peers can.

**Files:** none — verification only.

- [ ] **Step 1: Confirm the network prefab list is complete**

```bash
rm -f Temp/headless_tests.txt
```

MCP `Unity_RunCommand`:

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditMode("SpaceGame.Tests.NetworkPrefabRegistrationTests");
```

```bash
until [ -f Temp/headless_tests.txt ] && grep -q DONE Temp/headless_tests.txt; do sleep 3; done; cat Temp/headless_tests.txt
```

Expected: `FAILED=0`. If it fails, run `Tools/SpaceGame/Multiplayer/Sync Network Prefabs` before
going further — an incomplete list gives a host that works and clients that spawn nothing, which
would be misread as a lobby bug.

- [ ] **Step 2: Host alone from the editor**

Enter Play mode from `Bootstrap`. Main Menu → Multiplayer → Create tab → name it → Create → Start.

Expected in the console:
- `[NetworkBootstrap] N network prefabs registered.` with N > 1
- `[LobbySession] Hosting '<name>' code=<CODE> relay=<RELAY>`
- `[LobbySession] Starting '<scene>' for 1 client(s). Lobby stays open for late joiners.`

Expected on screen: the world loads and your player spawns. **Write down the lobby code.**

- [ ] **Step 3: Prove the lobby is still listed 60 seconds later**

This is the heartbeat test. A lobby not heartbeated inside 30s is delisted, and before this change
the heartbeat died with the menu scene.

Wait at least 60 seconds in the running game, then from a second instance (a build, or a second
editor via `Unity_RunCommand` in a cloned project) open Multiplayer → Browse.

Expected: the lobby appears, labelled `1/4` and `in game`.

If it does not appear, the session is not persisting — check that `LobbySession.Instance` created
its GameObject with `DontDestroyOnLoad` and that `IsHost` is still true after the scene load.

- [ ] **Step 4: Join the running world**

Click Join on that row.

Expected on the joiner: the loading screen shows "Joining", then the running world appears with the
host's player already in it.

Expected on the host's console:
- `[NGM DEBUG] OnClientConnected(1) called, already handled=False`
- `[NGM DEBUG] SpawnWhenReady(1) started`

Expected on both screens: two players, each able to see the other move.

- [ ] **Step 5: Confirm there is exactly one popup-free screen**

Reopen Multiplayer from the main menu.

Expected: one screen with four tabs. No IMGUI box in the lower left. Pressing F9 does nothing —
that was `DirectConnectPanel`'s toggle and the class no longer exists.

- [ ] **Step 6: Exercise the Direct tab**

Direct tab → note the address → Host. Expected: the world loads. From the second instance,
Direct tab → type that address → Join. Expected: connected, player spawns.

- [ ] **Step 7: Run the whole suite one final time**

```bash
rm -f Temp/headless_tests.txt
```

MCP `Unity_RunCommand`:

```csharp
SpaceGame.EditorTools.HeadlessTestRunner.RunEditMode();
```

```bash
until [ -f Temp/headless_tests.txt ] && grep -q DONE Temp/headless_tests.txt; do sleep 3; done; cat Temp/headless_tests.txt
```

Expected: `FAILED=` exactly the pre-existing count from Task 0. `PASSED=` the Task 0 number plus 21.
Run twice and trust the second.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "test: verify late join into a running session"
```

---

## Definition of done

- Opening Multiplayer shows one screen with four tabs and no IMGUI overlay.
- A host can create a lobby, start, and play alone.
- That lobby is still listed more than 60 seconds later, labelled `in game`.
- A second player can join it from the browser and spawns into the running world.
- Direct connect works host-and-join without any Unity service.
- `LobbySystem.cs` is under 200 lines and contains no `LobbyService` call.
- `grep -rn "IsLocked" Assets/Game/Scripts` returns nothing.
- The EditMode suite shows the Task 0 failure count and 21 more passing tests.
