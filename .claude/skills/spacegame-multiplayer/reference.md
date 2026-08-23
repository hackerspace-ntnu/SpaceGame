# NetMessaging contract, NetMsg catalog, and network prefab rules

All paths are relative to the repository root. Every symbol below exists in the repo as written.

## Where things live

| File | Role |
|---|---|
| `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs` | `NetArg`, `NetHandler`, `NetMsg`, `NetTo`, `NetTarget`, the extension API |
| `Assets/Game/Scripts/Core/Multiplayer/NetChannel.cs` | Per-entity handler table. Plain MonoBehaviour, `[AddComponentMenu("")]`, added on demand |
| `Assets/Game/Scripts/Core/Multiplayer/NetRelay.cs` | The wire. `[RequireComponent(typeof(NetworkObject))]`, three RPCs |
| `Assets/Game/Scripts/Core/Multiplayer/NetAuthority.cs` | Switches off local simulation drivers on machines that do not own the entity |
| `Assets/Game/Scripts/Core/Multiplayer/Networking.cs` | `static class Network` — `IsNetworked`, `Server`, `Client`, `LocalClientId`, `Simulates`, `Owns`, `Execute` |
| `Assets/Game/Scripts/Core/Multiplayer/NetworkedTeleport.cs` | Authoritative placement of an owner-driven body |
| `Assets/Game/Scripts/Core/Multiplayer/ClientNetworkTransform.cs` / `ClientNetworkAnimator.cs` | `OnIsServerAuthoritative() => false` |
| `Assets/Game/Scripts/Core/Multiplayer/NetworkBootstrap.cs` | Backfills the NetworkManager in-editor, strips inert scene NetworkObjects, `LogRegisteredPrefabCount()` |
| `Assets/Game/Scripts/Core/Multiplayer/NetworkGameManager.cs` | Per-client spawn flow (`SpawnWhenReady`), scene waits, saved-spawn restore |
| `Assets/Game/Scripts/Core/Multiplayer/SessionLauncher.cs` | `HostRelayAsync`, `JoinRelayAsync`, `HostDirect`, `HostLocal`, `JoinDirectAsync`, `WaitForClientConnectedAsync`, `ProfileArg` |
| `Assets/Game/Scripts/Core/Multiplayer/LobbySession.cs`, `LobbySessionOptions.cs` | Lobby lifecycle, `JoinWithConflictRecoveryAsync` |
| `Assets/Game/Scripts/Core/Multiplayer/Chat/ChatNetwork.cs` | The one system that cannot ride NetMessaging |
| `Assets/Game/Scripts/Core/Multiplayer/MultiplayerAutotest.cs` | Two-process client-side test harness |
| `Assets/Game/Editor/Multiplayer/NetworkPrefabRegistrar.cs` | `Tools/SpaceGame/Multiplayer/Sync Network Prefabs`, plus `Audit()` |
| `Assets/Game/Editor/Tests/NetworkPrefabRegistrationTests.cs` | EditMode guard for the prefab list |
| `Assets/Game/Editor/Tests/NetMessagingTests.cs` | Id uniqueness, `NetArg` serialization, offline dispatch |
| `Assets/Game/Editor/Tests/MultiplayerTestPlayerBuilder.cs` | `Tools/Tests/Build Multiplayer Test Player` |
| `Assets/Game/Prefabs/Systems/NetworkManager.prefab` | The only NetworkManager. `PlayerPrefab: {fileID: 0}` — deliberately null |
| `Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset` | The live prefab list, guid `c9ad996ef06854049834f7c1c8f95ea3` |

## NetArg

```csharp
public struct NetArg : INetworkSerializable
{
    public ulong Target;   // a NetworkObjectId; 0 means none
    public int A;
    public int B;
    public Vector3 P;
    public Quaternion R;
    private GameObject localTarget;   // NOT serialized — see below

    public NetArg(ulong target = 0, int a = 0, int b = 0);
    public NetArg With(GameObject go);        // returns a copy; assign or chain it
    public NetArg With(Component component);
    public readonly GameObject Resolve();     // localTarget, else SpawnedObjects[Target], else null
    public readonly bool HasOrientation { get; }  // R is a real quaternion, not all-zero
    public static ulong IdOf(GameObject go);
}
```

- **`localTarget` is deliberately absent from `NetworkSerialize`.** An id exists only for a *spawned*
  `NetworkObject`, so offline `Target` is always 0. `.With(go)` also stashes the raw reference,
  which survives the local-dispatch path — without it every subject-carrying message would work
  online and break in single-player.
- **`HasOrientation`** distinguishes "the sender told me where they were aiming" from "nobody filled
  this in". The alternative is a peer falling back on its own camera, which on the server means
  firing along the host's crosshair.
- `IdOf` uses `GetComponentInParent<NetworkObject>()` and requires `IsSpawned`.
- A `NetArg` is a struct: `.With()` returns a modified copy. `new NetArg { A = 1 }.With(x)` is the
  idiom; `arg.With(x);` on its own line does nothing useful.

## The API (extension methods on `Component`)

```csharp
void NetOn (this Component self, ushort id, NetHandler handler);
void NetOff(this Component self, ushort id, NetHandler handler);
void NetToServer(this Component self, ushort id, NetArg arg = default);
void NetToAll   (this Component self, ushort id, NetArg arg = default);
void NetToOthers(this Component self, ushort id, NetArg arg = default, ulong except = NetTarget.Self);
static void NetMessaging.NetSendTo(GameObject target, ushort id, NetArg arg, NetTo to = NetTo.Server);

public delegate void NetHandler(in NetArg arg, ulong sender);
public enum NetTo { Server, All, Others }
public static class NetTarget { public const ulong Self = ulong.MaxValue; }
```

`NetOn` creates the channel (`NetChannel.GetOrAdd`); `NetOff` only *finds* one, so calling it during
teardown is safe and never sprouts a component on a dying object.

### Send semantics

| Situation | `NetToServer` | `NetToAll` / `NetToOthers` |
|---|---|---|
| Client, relay spawned | RPC to the server | Refused with a warning — only the server may broadcast |
| Host / server | Runs `Deliver` inline, no round trip | RPC to `SendTo.ClientsAndHost`; `Others` filters the excluded client on arrival |
| Offline, or no `NetRelay`, or not yet spawned | Dispatched locally; `NetChannel.WarnUnrelayed` logs once per entity when a session exists | `Others` is a no-op by definition (silent); `All` dispatches locally |

There is **no unicast.** The house idiom is broadcast-then-filter: by `Network.Owns` for a
player-targeted message (`NetMsg.RopeTug`), or by an index for a per-group one (`LatchState`).
`NetworkedTeleport` and `ChatNetwork` use their own `[Rpc(SendTo.SpecifiedInParams)]` where a real
unicast is unavoidable.

### Re-entrancy

`NetChannel.Dispatch` copies its handler list into a buffer rented from a **static
`Stack<List<NetHandler>>`** and returns it in a `finally`. This is not defensive coding: a request
handler that answers with a broadcast re-enters `Dispatch` on the *same channel*, inline, on the
host — and it also fires offline, where `Send` falls through to a local dispatch. A single reusable
instance buffer threw `InvalidOperationException: Collection was modified` out of an RPC on every
item use. A handler that throws is logged (`[Net] handler for message N on '<name>' threw:`) and the
remaining handlers still run.

## NetMsg catalog

**Read the catalog from the source, never from a copy here.** It grows most weeks, and a stale
copy is how an id gets allocated twice:

```
grep -n "public const ushort" Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs
```

Every id carries a one-line comment in that file giving its direction, its channel and what each
`NetArg` field means. Append after the current highest.

Ids are appended only; a reused number routes a message to the wrong handler across builds.
Two numbers are **burned** and must never be reused: `3` (was `Equip` — the hotbar selection
already replicates) and `30` (was `LaunchCraft` — a wing-pack launch is just a server-authoritative
item use). `NetMessagingTests.MessageIdsAreUnique` and `ZeroIsNotUsed` enforce the rest.

Verb vocabulary shared by `PartToggle`/`PartState` and `LatchSet`/`LatchState`:
`-1` ask, `0` off animated, `1` on animated, `2` off instantly, `3` on instantly. "Instantly" is the
late-joiner answer — a door opened before you arrived is already open, not swinging in your face.

**Which channel to address** is the design decision, not an implementation detail. Put the message
on the entity that owns the contested state: damage on the victim, mounting on the mount, trade on
the trader, a station claim on the vehicle. That is what stops two players both taking the last
water cell.

## Network prefab rules

Netcode replicates a spawn by `GlobalObjectIdHash`. The server instantiates its own copy and never
consults the list, so an unregistered prefab is a **host that works and clients that see nothing**.

| Tier | Rule | Members | Why |
|---|---|---|---|
| 1 | **MUST** be a root `NetworkObject` **and** in the prefab list | Anything `IWorldService.Spawn` can be handed — including **every `InventoryItem.itemPrefab`**, `RocketSpawn`, `ornithopterPrefab`, the networked player prefab | Dropping a hotbar slot runs `EquipmentController.OnItemDropped` → `GameServices.ItemDropService.DropItem` → `PlayerDropService` → `GameServices.World.Spawn`, so an item never thought of as a world object still needs one |
| 2 | **MUST NOT** be networked: projectiles | `projectile`, `RocketProjectile`, `BallLightningProjectile`, `AgentProjectile` | Every machine instantiates its own bullet; only the authority's applies damage, flagged by `Weapon.ShotDealsDamage`. `AgentBullet.prefab` carries netcode that nothing uses — it lies about the design |
| 3 | **MUST NOT** be networked: equipped visuals | `GrapplingHookGun`, `CixinGunFinal`, `SuperSword`, `magazine_cixin`, `Line` | `EquipItemSocket.Equip` does a plain `Object.Instantiate(prefab, …, socket)` onto a bone, rebuilt locally from the replicated hotbar. A `NetworkObject` cannot parent to a plain transform anyway |

Additional rules:

- **Nested `NetworkObject`s are a warning, not an error**: `has child NetworkObject(s) but they will
  not be spawned across the network`. A `NetworkBehaviour` on a *plain* child of a `NetworkObject`
  is fully supported and is usually what is wanted. Strip a child's `NetRelay` **before** its
  `NetworkObject`, or `[RequireComponent]` refuses the removal (and only logs).
- **Script-created `NetworkObject`s ship `GlobalObjectIdHash: 0`.** `PrefabUtility.LoadPrefabContents`
  + `AddComponent<NetworkObject>` + `SaveAsPrefabAsset` hits it too — the component is added inside a
  preview scene where `OnValidate` cannot mint a real id. Duplicates on 0 make NGO register only the
  first and silently resolve every other spawn to it. Always grep the saved YAML.
- **Adding a `NetworkObject` to a prefab invalidates every scene holding an instance** until that
  scene is opened in edit mode and re-saved, with
  `PrefabUtility.RecordPrefabInstancePropertyModifications` — `MarkSceneDirty` + `SaveScene` writes
  nothing for a prefab instance otherwise, and returns `true` regardless. Find the scenes with
  `AssetDatabase.GetDependencies(scenePath, false)`, never by grepping YAML for a script GUID.
- Adding a `NetworkBehaviour` to an *existing* `NetworkObject` needs no scene re-save.
- `GlobalObjectIdHash` is plain XXH32 (seed 0) over
  `GlobalObjectId_V1-1-<prefabGuid>-<fileIdOfTheNetworkObjectComponent>-0`, so it can be recomputed
  without an editor. Validate any implementation against a known-good prefab first.
- Deleting a prefab nulls serialized references with no error and leaves null entries in the list,
  which makes NGO refuse to start. `RegisteredPrefabs_AllStillExist` guards it.
  `NetworkPrefabsList.PrefabList` is `IReadOnlyList` — removing an entry needs
  `SerializedObject`/`FindProperty("List")`; `.Add()` works for appending.

## Which player prefab is real

`NetworkConfig.PlayerPrefab` is **null**. `SpawnManager` spawns from its own serialized
`networkPlayerPrefab`, which is
`Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab` — a **variant**.
`PlayerCharacter.prefab` has no `NetworkObject` (local rig only) and `SyncedPlayer.prefab` still
exists; putting sync components on the wrong one silently does nothing. A variant's inherited
components do not appear in its own YAML, so grepping GUIDs across prefabs under-reports.

## NetAuthority

```csharp
public bool IsSimulatedHere => Network.Owns(this);          // ownership, NOT server-ness
[SerializeField] private List<Behaviour> simulationDrivers; // empty = auto-discovered
[SerializeField] private bool freezePhysicsOnRemote = true;
public static List<Behaviour> Discover(GameObject root);    // AgentController, IMovementMotor, NavMeshAgent
```

- `Refresh()` runs from `Start`, `OnNetworkSpawn`, `OnGainedOwnership`, `OnLostOwnership`, and
  always calls `Restore()` first. It **must** be idempotent: `Start` and `OnNetworkSpawn` race — on
  an instantiate-then-spawn, `Start` runs while the object is unspawned and therefore looks
  unowned-and-therefore-ours — and a ran-once flag left every client simulating its own copy.
- `Discover` matches by interface and base type, never by name, and `BelongsTo` stops at the
  `NetworkObject` boundary. A rider is parented *into* its mount, so crossing that boundary would
  switch off the player sitting on it and hand a remote player's controls to the wrong machine on
  dismount.
- **Trade-off:** disabling an `IMovementMotor` on a remote copy freezes procedurally animated legs,
  because the motor both moves the body and solves the legs. Fix per prefab by removing locomotion
  from that entity's `simulationDrivers` list.

## Why chat cannot ride NetMessaging

Two structural reasons, both worth remembering before anyone "simplifies" `ChatNetwork` onto
`NetRelay`: `NetArg` has no string field, and widening it puts ~128 bytes on every damage, mount and
item message in the game; and `NetTo` has no unicast, while a command's answer ("no player called
Bob") belongs to the asker alone. `ChatNetwork` therefore has its own three RPCs and lives on
`NetworkGameManager.prefab` — already a `NetworkObject`, placed in `persistentScene` (loaded beneath
every gameplay scene including the additive arena), spawned before any player exists.

Chat's own traps: TMP markup containment needs **both** the `<noparse>` wrap and
`ChatText.Sanitize`; and a char limit is not a byte limit — the message crosses as
`FixedString512Bytes`, and 180 characters of a 3-byte script throws inside `Unity.Collections`
rather than truncating.

## Session and lobby facts

- The only `NetworkManager` lives in `Bootstrap` (build index 0) and survives via its own
  `DontDestroyOnLoad`. Playing any other scene directly leaves `Singleton` null;
  `NetworkBootstrap` backfills it in-editor with a warning that the other Bootstrap systems (item
  registry, audio) are still absent.
- Relay: build `RelayServerData` from the allocation's **endpoint list**
  (`allocation.ToRelayServerData("dtls")`), never the legacy `allocation.RelayServer` +
  `isSecure: false`, which hangs instead of erroring.
- `StartClient()` returning true only means the attempt was dispatched. Await
  `SessionLauncher.WaitForClientConnectedAsync`.
- `OwnerClientId` on a **scene-placed** `NetworkObject` is always the server. Spawning "the owner"
  spawns only the host — lobby clients connected before the object existed, so their
  `OnClientConnectedCallback` already fired. Iterate `ConnectedClientsIds` too (see
  `NetworkGameManager.OnNetworkSpawn` and `ChatNetwork.OnNetworkSpawn`).
- Clients do not stream their own chunks: `WorldStreamer.Update` early-returns on clients, and chunk
  scenes arrive as NGO scene events applied asynchronously. A client's player can therefore exist
  before its ground does — which is why `UnderTerrainGuard` gates on `Network.Owns`.
- `NetworkSceneManager` has one **global** `m_IsSceneEventActive` flag, cleared only when Netcode's
  own completion chain finishes — well after `Scene.isLoaded` reports true. Wait on
  `OnLoadEventCompleted`/`OnUnloadEventCompleted`, or treat `SceneEventInProgress` as "retry me".
