---
system: Multiplayer
layer: core
summary: Unity NGO wrapped in one NetMsg/NetArg message channel, an authority facade and one session launcher
paths:
  - Assets/Game/Scripts/Core/Multiplayer/
  - Assets/Game/Prefabs/Systems/NetworkManager.prefab
  - Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset
  - Assets/Game/Scripts/Gameplay/Health/NetDamage.cs
  - Assets/Game/Editor/Multiplayer/NetworkPrefabRegistrar.cs
symptoms:
  - "it works when I host but the client sees nothing happen"
  - "an object I spawn at runtime is invisible to clients, or logs 'has no NetworkObject'"
  - "my [Rpc] method never runs on the other machine"
  - "a joining client fails with 'Scene Hash N does not exist in the HashToBuildIndex table'"
  - "the server teleports a player and it snaps back within a frame"
  - "'Failed to bind UDP socket' or a 409 'already a member of the lobby' when launching two instances"
  - "the game is laggy with five or six players but fine with two"
  - "Could not start a local session on port N / another program may be using it"
  - "a client joining a game in progress throws NullReferenceException in NetworkObject.Serialize / WriteSceneSynchronizationData"
reads_with: [Lobby, Persistence, Testing, CoreServices]
updated: 2026-09-25
---

# Multiplayer / Netcode core

Unity Netcode for GameObjects wrapped in one generic message channel, one authority facade and one session launcher — so a feature is written once and degrades to single-player instead of throwing.

**Scope:** `Assets/Game/Scripts/Core/Multiplayer/` (Messaging, Authority, Session, Joining, Players, Chat, Autotest, Lobby) + [NetworkManager.prefab](Assets/Game/Prefabs/Systems/NetworkManager.prefab).
**Related:** [Lobby.md](Lobby.md) · [Persistence.md](Persistence.md) · [Inventory.md](Inventory.md) · [spacegame-multiplayer SKILL.md](.claude/skills/spacegame-multiplayer/SKILL.md)

## Model

- **Singleplayer is a host of one** (`MainMenuUI.EnterWorld` → `SessionLauncher.HostLocal` → `StartHost`), so `Network.IsNetworked` is true in solo play and all netcode validation fires there.
- **One channel, not one sync class per feature.** Per-feature `XNetworkSync` classes are gone: register a handler for a `NetMsg` id, send a `NetArg`, transport is somebody else's problem.
- **Every send degrades to a local dispatch** when there is no relay, no spawn or no session — the pre-netcode behaviour. Nothing throws.
- **"Entity" = the `NetworkObject` root** (`transform.root` if none), so a handler on a nested weapon and a message addressed to the player body meet in the same `NetChannel`.
- **Two authority questions, never `IsServer` directly:** `Network.Simulates(c)` (may I decide?) and `Network.Owns(c)` (mine to drive from input?). Both true offline and for un-networked objects — refusing there would freeze chunk props and interiors.
- **NGO replicates a spawn by `GlobalObjectIdHash`;** the server never consults the prefab list, so an unregistered prefab is a working host and blind clients.
- Session-wide netcode (chat, sky anchor, join snapshot, spawn flow) rides the **NetworkGameManager prefab** in `persistentScene`: one `NetworkObject`, spawned on every peer before any player object.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `NetMessaging` | [NetMessaging.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMessaging.cs) | Extension API: `NetOn`/`NetOff`, `NetToServer`/`NetToAll`/`NetToOthers`, `NetSendTo` |
| `NetArg` | [NetArg.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetArg.cs) | Fixed payload `Target,A,B,P,R`; `.With(go)` also keeps an **unserialized** local ref so `Resolve()` works offline; `HasOrientation` |
| `NetMsg` | [NetMsg.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs) | Id catalog (47 ids, highest 97). Append only; 3 (Equip) and 30 (LaunchCraft) burned |
| `NetChannel` | [NetChannel.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetChannel.cs) | Per-entity handler table, plain MonoBehaviour added on demand; re-entrant `Dispatch` off a static buffer pool; `IndexOf<T>` numbers sibling components; `WarnUnrelayed` |
| `NetRelay` | [NetRelay.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetRelay.cs) | The wire: `ToServerRpc`/`ToAllRpc`/`ToOthersRpc`. Requires a `NetworkObject` |
| `NetTo`/`NetTarget`/`NetHandler` | [NetTo.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetTo.cs) | Directions, `Self` sentinel, `void (in NetArg, ulong sender)` |
| `Vocabulary/*` | [AgentAction.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/Vocabulary/AgentAction.cs) | Constants some ids put in `A`/`B` (`GrappleVerb`, `LassoVerb`, `SceneEffectPhase`) |
| `Network` | [Network.cs](Assets/Game/Scripts/Core/Multiplayer/Authority/Network.cs) | `IsNetworked`, `Server`, `Client`, `LocalClientId`, `Simulates`, `Owns`, `Execute` |
| `NetAuthority` | [NetAuthority.cs](Assets/Game/Scripts/Core/Multiplayer/Authority/NetAuthority.cs) | Disables simulation drivers + freezes the Rigidbody on remote copies; `IsSimulatedHere`; adopts spawn pose; idempotent `Refresh` on Start/spawn/ownership change |
| `SimulationDrivers` | [SimulationDrivers.cs](Assets/Game/Scripts/Core/Multiplayer/Authority/SimulationDrivers.cs) | What counts as a driver (`AgentController`, `IMovementMotor`, `NavMeshAgent`); `BelongsTo` stops the sweep at the `NetworkObject` boundary |
| `NetworkedTeleport` | [NetworkedTeleport.cs](Assets/Game/Scripts/Core/Multiplayer/Authority/NetworkedTeleport.cs) | `Move(go,pos,rot)`: the owner performs it, server RPCs the owner; falls through to `SaveTeleport` |
| `ClientNetworkTransform`/`ClientNetworkAnimator` | [ClientNetworkTransform.cs](Assets/Game/Scripts/Core/Multiplayer/Authority/ClientNetworkTransform.cs) | Owner-authoritative overrides for client-driven bodies |
| `SessionLauncher` (+`.Relay`, `.Direct`) | [SessionLauncher.cs](Assets/Game/Scripts/Core/Multiplayer/Session/SessionLauncher.cs) | The only place a session starts; never throws, returns `SessionResult`. `HostLocal` (solo), `HostRelayAsync`/`JoinRelayAsync` (players), `HostDirect`/`JoinDirect` (**test only**) |
| `NetworkBootstrap` | [NetworkBootstrap.cs](Assets/Game/Scripts/Core/Multiplayer/Session/NetworkBootstrap.cs) | Editor-only NetworkManager backfill; strips inert scene `NetworkObject`s; `LogRegisteredPrefabCount` |
| `SessionProfile`/`CommandLineArgs` | [SessionProfile.cs](Assets/Game/Scripts/Core/Multiplayer/Session/SessionProfile.cs) | Which UGS profile this process signs in under (`-sgprofile`, MPPM `-editor-mode -name`, ParrelSync clone) |
| `SessionExit`/`SessionWatchdog`/`DisconnectHook`/`SessionEndedScreen` | [SessionExit.cs](Assets/Game/Scripts/Core/Multiplayer/Session/SessionExit.cs) | One teardown path to `MainMenu`; watchdog notices a lost host from inside the world; hook re-attaches to whichever NetworkManager is live |
| `SceneEventHook` | [SceneEventHook.cs](Assets/Game/Scripts/Core/Multiplayer/Session/SceneEventHook.cs) | `DisconnectHook` for `NetworkSceneManager.OnSceneEvent`. A scene manager does not exist until a session starts and is **replaced** on every start after, so anything listening across a whole play session — [NetworkLoadingScreen](Assets/Game/Scripts/Presentation/UI/Pages/NetworkLoadingScreen.cs) — polls this instead of subscribing once |
| `NetworkGameManager` (+`.Profiles`, `.Versus`) | [NetworkGameManager.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.cs) | Server-side per-client spawn flow; profile + team reporting RPCs |
| `SessionSnapshot`/`SnapshotCapture`/`SnapshotRestore`/`SnapshotPayload` | [SessionSnapshot.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/SessionSnapshot.cs) | Hands a joiner event-only state (ropes, portals) by `NetworkObjectId`, retried 30 s |
| `SpawnSyncGuard` | [SpawnSyncGuard.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/SpawnSyncGuard.cs) | Server-side sweep, once a second, of NGO's `SpawnedObjects`/`SpawnedObjectsList` for entries whose GameObject is destroyed. Bootstrapped from a static like `SessionWatchdog`; logs each removal as an error |
| `SkyNetwork`/`SkyAnchor` | [SkyNetwork.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/SkyNetwork.cs) | Replicates the day/night *anchor* only; time of day is a pure function of a shared clock |
| `PlayerIdentity`/`PlayerRoster` | [PlayerIdentity.cs](Assets/Game/Scripts/Core/Multiplayer/Players/PlayerIdentity.cs) | On the player prefab: name + suit colour **owner-write**, team **server-write**; roster rows + ping |
| `ChatNetwork`/`ChatLog`/`ChatCommands`/`ChatBuiltinCommands`/`ChatText`/`ChatMessage` | [ChatNetwork.cs](Assets/Game/Scripts/Core/Multiplayer/Chat/ChatNetwork.cs) | Own 3 RPCs (NetArg has no string, NetTo has no unicast); token-bucket throttle; static log survives scene loads. Commands are an open table: `/tp`, `/help` here, `/wave` `/cheer` `/shrug` `/flex` from [PlayerEmoteCommands](Assets/Game/Scripts/Characters/Player/PlayerEmoteCommands.cs) |
| `MultiplayerAutotest`/`AutotestRunner.*`/`AutotestProbes` | [MultiplayerAutotest.cs](Assets/Game/Scripts/Core/Multiplayer/Autotest/MultiplayerAutotest.cs) | `-sgmode host\|client\|persist`; prints `[MPTEST] key=value` |
| `NetworkPrefabRegistrar` | [NetworkPrefabRegistrar.cs](Assets/Game/Editor/Multiplayer/NetworkPrefabRegistrar.cs) | `Tools/SpaceGame/Multiplayer/Sync Network Prefabs` |
| Lobby (`LobbySession`, `LobbyJoinRecovery`, `LobbyTeams`, …) | [Lobby/](Assets/Game/Scripts/Core/Multiplayer/Lobby/) | Namespace `SpaceGame.Core.Lobbies` — see [Lobby.md](Lobby.md) |

Part of the contract but outside the folder: [NetDamage.cs](Assets/Game/Scripts/Gameplay/Health/NetDamage.cs), [NetworkedHealthComponent.cs](Assets/Game/Scripts/Gameplay/Health/NetworkedHealthComponent.cs), [NetLatch.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/NetLatch.cs), [PlayerInventoryNetwork.cs](Assets/Game/Scripts/Items/Inventory/Components/PlayerInventoryNetwork.cs), [NetworkPlayerController.cs](Assets/Game/Scripts/Characters/Player/Core/NetworkPlayerController.cs), [SpawnManager.cs](Assets/Game/Scripts/Gameplay/Game/Spawning/SpawnManager.cs).

## Flows

1. **Message round trip.** Owner `Present()`s locally, then `NetToServer(id, arg.With(subject))` → `ToServerRpc` → server `Dispatch` → handler guards `Network.Simulates(this)`, re-checks preconditions, mutates, `NetToOthers(id2, arg, except: sender)` → peers `Present()`. On the host that broadcast re-enters `Dispatch` inline (`SendTo.ClientsAndHost`).
2. **Starting a session.** `EnsureServicesAsync` (UGS init + anon sign-in under `SessionProfile`) → `HostRelayAsync` (allocation → `dtls` endpoint → `SetRelayServerData` → `StartHost`) or `JoinRelayAsync` → `WaitForClientConnectedAsync` (15 s; `StartClient()`'s bool is *not* success).
3. **A client gets a body.** `NetworkGameManager.OnNetworkSpawn` (every peer) adopts the versus session from the lobby and sends `ReportProfileServerRpc` (+ team). Server then runs `SpawnWhenReady` per client: yield a frame (avoid `SceneEventInProgress`) → await the pending additive scene's `OnLoadEventCompleted` → versus-ship route, or wait for `WorldStreamer.IsReady` and a `SpawnPoint` (15 s) → `TryGetSpawnAnchor` → `WaitForProfile` (5 s) → a saved position overrides the anchor **before** the preload → `PreloadChunksAroundPositions` → resolve the spawn point **once** → `ArrivalDirector.SpawnIntoArrival` or `SpawnManager.SpawnPlayerForClient` (`SpawnAsPlayerObject`).
4. **Joiner catch-up.** Server `SnapshotCapture.Build()` → JSON RPC to that client → `SnapshotRestore` retries each entry per frame for 30 s until the named `NetworkObject`s exist locally.

## Multiplayer

| Concern | Rule |
| --- | --- |
| Decide / mutate | Server only, gated on `Network.Simulates(this)` — not `IsServer`: an un-networked entity has no wire and must still act |
| Drive from input | `Network.Owns(this)` — covers host, offline, and a mount handed to its rider |
| Act on a body a message NAMES | `Network.MayActFor(subject, sender)` — the server's counterpart of `Owns`. Nothing ties `NetArg.Target` to the sender id, so a client may name any body in the session; every handler that acts on one asks this first (`SnareReceiver`, `VehicleStation`, `SeatedRider`). In a live session a subject with no **spawned** `NetworkObject` is **refused**; offline and a `ServerClientId` sender are waved through |
| Broadcast | Server only; `NetRelay.RequireServer` warns and drops a client's `All`/`Others` |
| Player transform | **Owner-authoritative.** A server write to a remote player is overwritten within a tick, silently ⇒ `NetworkedTeleport.Move`, owner-gated failsafes |
| Item use | `Use()` on the authority, `Present()` everywhere; `NetMsg.UseItem`/`ItemUsed`, hold stream `UseItemHold`/`ItemUseHeld` (B=1 continue, 0 stop; P/R carry the aim **ray**, not the hit point). A hold identical to the last one sent goes out only every `UseChannel.HoldKeepAliveInterval` (0.2 s) — the local `PlayHold`/`TryHold` still run every tick; artifacts with a `holdTimeout` must keep it above that |
| Continuous values | Compare before sending. `VehicleStation` announces and renews only on a change past `ValueDeadband`, plus a `KeepAliveInterval` (1 s) repair; `PlayerMovement` writes its animator floats through `DampedAnimatorFloat` so a resting player's `ClientNetworkAnimator` has nothing to send |
| Damage | `NetDamage.Apply`; `NetMsg.Damage` → server on the target's relay; `NetMsg.Damaged` broadcast on the victim's relay |
| Remote copies | `NetAuthority` suppresses drivers, so anything a suppressed driver would have drawn must be broadcast explicitly |
| Late joiners | `NetworkVariable.OnValueChanged` never replays — read the value in `OnNetworkSpawn`; event-only state goes in `SessionSnapshot` |
| Config | `TickRate 30`, `ConnectionApproval 0` (**off**, deliberately), `EnableSceneManagement 1`, `PlayerPrefab: {fileID: 0}` (**null** — `SpawnManager` spawns the real one), one list `DefaultNetworkPrefabs.asset` guid `c9ad996e…`, UTP port `7782`, `LoadSceneTimeOut 120` |
| Transform settings | Every `NetworkTransform`/`ClientNetworkTransform` in the project (players, 16 agents and vehicles, `SyncedPlayer`, `CowBotRocket`) ships `UseUnreliableDeltas 1`, `UseHalfFloatPrecision 1`, `UseQuaternionSynchronization 1` + `Compression 1`, `PositionThreshold 0.02`, `RotAngleThreshold 0.5`. NGO's defaults (reliable, full float, three Euler floats, 1 mm) had six interpolating Rigidbodies never going idle on the reliable channel. A new networked prefab must match; `AgentBullet` is the one deliberate exception |

## Persistence

The layer saves nothing itself; it *carries* persistence. `ReportProfileServerRpc` tells the server which save profile a client plays so the world streams around that player's saved position (`TryGetSavedSpawn`); binding and validation belong to [PlayerSaveSync.cs](Assets/Game/Scripts/Core/Persistence/Runtime/PlayerSaveSync.cs). `SnapshotCapture` deliberately does **not** reuse savers: a `SaveRef` does not resolve on a client, so the join snapshot addresses everything by `NetworkObjectId` (session lifetime) while the save file keeps `SaveRef`s (restart lifetime). See [Persistence.md](Persistence.md).

## Gotchas
- **`NetworkAnimator` replays layer STATES and WEIGHTS, not just parameters.** Its owner's `CheckForStateChange` sends every layer's weight, every crossfade started from code and every state a layer arrives in, and applies them on watchers and late joiners with `SetLayerWeight`/`CrossFade`/`Play`. So a body that has one must have ONE writer: an action also started locally on a watcher plays twice, the replayed crossfade restarting it. The player's is owner-authoritative ([`ClientNetworkAnimator`](Assets/Game/Scripts/Core/Multiplayer/Authority/ClientNetworkAnimator.cs)); the rule is `IsServerAuthoritative() ? IsServer : IsOwner`, never `HasAuthority`. Its parameter table is baked into the prefab and only regenerated by its editor-only `OnValidate` — see [HumanoidAnimation.md](HumanoidAnimation.md).
- **A message names a body, but nothing on the wire says the sender is entitled to it.** `NetArg.Target` is a `NetworkObjectId` a client chooses; the sender id is separate. A server handler that acts on `arg.Resolve()` without `Network.MayActFor` lets one player leave another's seat, claim another's station or drain a net holding another's captive — none of which the host can reproduce, because the host is always waved through as `ServerClientId`.
- **The tag, the `Rigidbody` and the `NetworkObject` of a player must be on ONE GameObject.** Physics queries return the Rigidbody's object, `NetArg.Resolve` returns the NetworkObject's, and a tag check asks a third. Separate them and the object recorded locally is not the object announced, so the handler acts on the wrong body **on clients only and in silence** — the host still holds `NetArg.localTarget`, which never leaves the machine, so it is right by accident. `NetGunWiringTests.ThePlayerTagRigidbodyAndNetworkObjectShareOneGameObject` pins it.
- **Unregistered network prefab = perfect host, blind clients.** Run `Sync Network Prefabs`. The live list is `Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset`; the root `Assets/DefaultNetworkPrefabs.asset` regenerates itself and is **not** what NetworkManager loads. A script-created `NetworkObject` ships `GlobalObjectIdHash: 0`, and duplicate 0s make NGO drop all but one prefab silently.
- **`[Rpc]` on a non-`NetworkBehaviour` is silently inert** — ILPP only rewrites NetworkBehaviours.
- **Sibling components share one channel.** A ship with four seats or a hull with two doors gets every message on all of them; number them with `NetChannel.IndexOf<T>` into `NetArg.A`. That index is *positional* and does not survive reordering prefab children between builds.
- **Handlers must be idempotent and re-entrancy-safe** — on the host a request handler that answers with a broadcast re-enters `Dispatch` inline, applying state twice. Act only when it differs.
- **`NetArg.Target` carries an unserialized local ref via `.With()`.** Rebuilding the struct field-by-field inside a handler drops it and breaks the offline path.
- **`R == default` is not a rotation** — use `HasOrientation`; falling back to `Camera.main` on the server aims down the *host's* crosshair.
- **`NetAuthority.Refresh` races `Start` vs `OnNetworkSpawn`,** hence idempotent: a ran-once flag left every client simulating its own copy. It stops at the `NetworkObject` boundary (so a mount does not switch off its rider) and skips `IExternallyPosed` drivers (legged/flight rigs both move the body *and* solve the limbs).
- **Spawn pose:** clients instantiate at the *prefab* pose then write the transform, which a Rigidbody undoes within the frame — and on an owner-authoritative body that wrong pose is *published as truth*. Fixed by `NetAuthority.AdoptSpawnPose` / `NetworkPlayerController`.
- **`OnClientConnectedCallback` already fired** for everyone who joined via the lobby before `NetworkGameManager` existed, so `OnNetworkSpawn` also sweeps `ConnectedClientsIds`; `handledClients` is pruned on disconnect because NGO reuses the lowest free client id.
- **One destroyed `NetworkObject` left in the spawn table breaks every later join.** Synchronising a joiner walks `SpawnedObjectsList` and calls `NetworkObject.Serialize` on every entry, with no error handling: the first entry that throws aborts the whole scene-event message and the joiner never receives the world. A destroyed-but-still-spawned entry throws on its own transform — `MissingReferenceException` in the editor, `NullReferenceException` in a player, which is why it shows up only in a build. NGO leaves such entries behind on two of its own quiet paths (`NetworkObject.OnDestroy` with no NetworkManager to ask; `NetworkSpawnManager.OnDespawnObject` handed an object that already reads as null). Two things now hold the line: the forked netcode package skips an object it cannot serialize, rewinds the writer, corrects the object count and logs which object it was (`SPACEGAME PATCH` in [SceneEventData.cs](Packages/com.unity.netcode.gameobjects/Runtime/SceneManagement/SceneEventData.cs), see [PATCHES.md](Packages/PATCHES.md)) — so a joiner loses one object instead of the world; and `SpawnSyncGuard` removes dead entries from the table and names the `NetworkObjectId`. The other way `Serialize` can throw is an object spawned with a null `NetworkManagerOwner` — internal to NGO and unreachable from here, but NGO logs `NetworkManagerOwner should not be null!` when it happens, so check the host log for that line if a join still fails with the guard silent.
- **`SceneEventInProgress`**: `NetworkSceneManager` has one global busy flag — wait on `OnLoadEventCompleted`, never on raw `Scene.isLoaded`.
- **`Scene Hash N does not exist in the HashToBuildIndex table`**: NGO hashes scene *paths* case-sensitively off each machine's disk; git folder-casing drift is invisible under `core.ignorecase`.
- **Two instances on one machine share PlayerPrefs**, so anonymous auth reuses the PlayerId and the lobby returns 409 — launch the second with `-sgprofile client`.
- **`Failed to bind UDP socket`**: the editor leaks the native socket per Play session. [PlayModeTransportTeardown.cs](Assets/Game/Editor/Multiplayer/PlayModeTransportTeardown.cs) mitigates it; otherwise bump the port. Never route singleplayer through `HostDirect` — it calls `SetConnectionData` and overrides the port.
- **Netcode is an embedded, patched package.** It lives in `Packages/com.unity.netcode.gameobjects`, not the package cache, because `MigrateNetworkObjectsIntoScenes` dereferenced a destroyed `NetworkObject` and took the frame's whole migration batch down with it. Every change is marked `SPACEGAME PATCH`; read [Packages/PATCHES.md](Packages/PATCHES.md) before upgrading, and see [WorldStreaming.md](WorldStreaming.md) for what the bug did.
- **`NetworkManager.ConnectedClientsList` is server-only** — build rosters from spawned `PlayerIdentity` objects (`PlayerRoster.Build`).
- `cond ? item.ID : default` for a `NetworkList<FixedString64Bytes>` collapses to `string` and NREs — write `default(FixedString64Bytes)`.

## Extending

1. **Pick the mechanism.** Runtime prefab ⇒ `NetworkObject` + prefab list + server-side `GameServices.World.Spawn`. Late-joiner value ⇒ `NetworkVariable` read in `OnNetworkSpawn`. One-off event ⇒ this channel. Open/closed fixture ⇒ `NetLatch`. Damage ⇒ `NetDamage.Apply`. Placing an owner-authoritative body ⇒ `NetworkedTeleport.Move`. String or unicast ⇒ your own `NetworkBehaviour` (see `ChatNetwork`). Derivable from replicated state ⇒ do nothing.
2. **Append ids** to [NetMsg.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs): read the tail first, never reuse a retired number, comment direction, channel and what `A`/`B`/`P`/`R` mean.
3. **Subscribe** `this.NetOn(NetMsg.Foo, OnFoo)` in `OnEnable` and `NetOff` in `OnDisable` — every `NetOn` needs its pair.
4. **Split the code** along flow 1: local `Present()` → `NetToServer` → server handler guarded by `Network.Simulates(this)` → `NetToOthers(except: sender)`. `Present()` stays idempotent and cosmetic-only.
5. **If the entity carries several of the same component**, put `NetChannel.IndexOf<T>(this)` in `NetArg.A` and drop messages that are not yours.
6. **Add `NetAuthority`** to anything that simulates itself, and `NetworkedHealthComponent` beside any `HealthComponent`.
7. **Register prefabs** (`Sync Network Prefabs`), then re-save every scene holding an instance if you added a `NetworkObject`.
8. **Verify.** EditMode guards ([NetMessagingTests.cs](Assets/Game/Editor/Tests/NetMessagingTests.cs), [NetworkPrefabRegistrationTests.cs](Assets/Game/Editor/Tests/NetworkPrefabRegistrationTests.cs), [NetAuthorityAndDamageTests.cs](Assets/Game/Editor/Tests/NetAuthorityAndDamageTests.cs)), then a real two-process run — `Tools/Tests/Build Multiplayer Test Player`, `-sgmode host` / `-sgmode client` — adding a `Report(...)` to [AutotestRunner.Client.cs](Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Client.cs). Host-only testing proves nothing.
