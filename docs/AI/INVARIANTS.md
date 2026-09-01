# Invariants

Rules that hold in **every** subsystem here — read once, before touching anything. Each is recorded in three or more of the `## Gotchas`
sections under [systems/](systems/), which carry the detail; this file carries only what repeats. Almost nothing here throws when it breaks.

## Verify on a client, never only on the host

**The rule.** A feature is unfinished until it has been seen working on a second machine. Host-only observation is not evidence of anything.

**Why.** Single-player is a host of one (`MainMenuUI.EnterWorld` → `SessionLauncher.HostLocal` → `StartHost`), so `Network.IsNetworked` is true
solo and every netcode path *appears* to run. The server also instantiates prefabs directly and never consults the network prefab list.

**How it fails.** A perfect host and blind clients, clean console: the object exists for you and nobody else, and damage lands twice.

**Where.** [Multiplayer](systems/Multiplayer.md) · [Testing](systems/Testing.md) · [Combat](systems/Combat.md) · [Artifacts](systems/Artifacts.md)

## Server decides, every machine presents

**The rule.** State changes run only where `Network.Simulates(this)` is true; visuals and sound run everywhere. Aim, seeds and anything else
derived from input travel **in the message** — never recompute them on the receiver.

**Why.** `Camera.main` on the server is the *host's* camera, and `NetAuthority` switches off remote drivers, so a suppressed driver never runs
the code that spawns its own effect. Gate on `Simulates`, not `IsServer` — but on an unspawned held item `Simulates` is true everywhere, so
there ask about the owner.

**How it fails.** Every client's shot and every NPC's barrel follow the host's head; a remote turret fires with no muzzle flash.

**Where.** [Multiplayer](systems/Multiplayer.md) · [Artifacts](systems/Artifacts.md) · [Combat](systems/Combat.md) · [AgentSystem](systems/AgentSystem.md)

## Register exactly what the server spawns — and nothing else

**The rule.** Anything handed to `GameServices.World.Spawn`, including *every* `InventoryItem.itemPrefab`, needs a root `NetworkObject` and an
entry in the live prefab list. Projectiles and equipped visuals must **not** be registered.

**Why.** NGO replicates a spawn by `GlobalObjectIdHash`; a script-built `NetworkObject` ships `0`, and duplicate zeros make NGO drop all but
one silently. The list stores GUIDs so a name grep lies, and the root `Assets/DefaultNetworkPrefabs.asset` regenerates itself and is not read.

**How it fails.** Under-registered: `[WorldService] Prefab 'X' has no NetworkObject`, or a host-only object. Over-registered: damage per
player, or a `NetworkObject` that cannot parent to a hand bone.

**Where.** [Multiplayer](systems/Multiplayer.md) · [Combat](systems/Combat.md) · [GameModes](systems/GameModes.md) · [DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset)

## Something else already owns that transform

**The rule.** Never write a transform to move a body — use `SaveTeleport.Move` (world objects) or `NetworkedTeleport.Move` (players, always
*owner*-gated), and let exactly one component write a given transform per update phase, switching the others off explicitly.

**Why.** An interpolating Rigidbody restores its position within the frame, `LeggedLocomotion` overwrites from `pathPos` next frame, a
`NavMeshAgent` writes every enabled frame, and the player's `NetworkTransform` is owner-authoritative so a server write to a remote player is
undone within a tick. `m_AutoSyncTransforms` is `0` project-wide, so teleport-then-raycast reads stale physics. Never add a `LateUpdate` to a
subclass either — it hides the base's.

**How it fails.** The object snaps back with no error; a server respawn works for the host alone; the walker stops walking or glitches.

**Where.** [PlayerCharacter](systems/PlayerCharacter.md) · [Locomotion](systems/Locomotion.md) · [Persistence](systems/Persistence.md) · [PlayerShip](systems/PlayerShip.md)

## Identity is a contract: bake it, then never rename it

**The rule.** Address a saved or replicated thing by a baked id — the wiring tool's GUID, a stamped `prefabId`, a `SaveKey`, a `NetMsg` id —
never by hierarchy position or scene name. Anything that reached the wire, a save file or a serialized reference is permanent: append only.

**Why.** The fallback `DeriveAuthoredId` is FNV-1a over scene + hierarchy path + sibling index, so a rename or re-parent changes it;
`TransitionId` hashes the same way; anchors and chunk deltas key on scene *name*. Retired `NetMsg` ids still travel between builds,
`TerrainFeatureType` ints and `PackSurfaceId` sit in saves, suit materials match by name, and `[SerializeReference]` stores types by name.

**How it fails.** Nothing throws: the record is orphaned and the object returns at its authored pose with prefab defaults, the wrong door
opens, a slot loads empty. Restore a deleted asset by GUID.

**Where.** [Persistence](systems/Persistence.md) · [EntitySystem](systems/EntitySystem.md) · [SceneTransitions](systems/SceneTransitions.md) · [Inventory](systems/Inventory.md)

## Edit the builder, never the asset it writes

**The rule.** Prefabs, scene subtrees, materials and `.blend` files owned by a generator are rewritten wholesale on the next run. Every fix
belongs in the script.

**Why.** `*Builder` scripts under `Assets/Game/Editor/` rebuild from scratch with no merge and no warning; FBX sub-asset materials regenerate
on reimport; a `.blend` carries hand edits that exist nowhere else (compare object *scales*, not names, to spot one).

**How it fails.** Hand-added components vanish — `GolemBuilder` lost the Golem's `SaveableEntity`; `PlayerShip` lost its `SeatedRider` and flew
its descent empty. A renamed `[SerializeField]` breaks a builder as quietly, unless it goes through [SerializedFields](Assets/Game/Editor/Support/SerializedFields.cs).

**Where.** [EditorTooling](systems/EditorTooling.md) · [PlayerShip](systems/PlayerShip.md) · [Artifacts](systems/Artifacts.md) · [ArtPipeline](systems/ArtPipeline.md)

## Assume nothing throws — read the write back

**The rule.** After any editor-side write (prefab save, asset edit, prefab registration, FBX re-export, render-feature install, NavMesh bake),
re-read it off disk and assert. `Verify()` is the established pattern.

**Why.** The AssetDatabase goes read-only in some sessions and discards saves silently; URP keeps a parallel `m_RendererFeatureMap`, so a list
append yields a feature that never runs; `avatar.isHuman` flips to `false` on a re-export; an unreadable `MeshCollider` mesh drops out of the
bake; an MPPM clone reads assets fine and imports nothing.

**How it fails.** A builder reports success having written nothing; the character stands still with a clean console; the feature never runs.

**Where.** [EditorTooling](systems/EditorTooling.md) · [ArtPipeline](systems/ArtPipeline.md) · [Environment](systems/Environment.md) · [NavMeshSystem](systems/NavMeshSystem.md)

## Every handler fires more than once, and one of those times is a load

**The rule.** Handlers must be idempotent and re-entrancy-safe — act only when the new state differs — and anything reacting to a state
*change* must ask whether it is being restored (`HealthComponent.IsRestoring`).

**Why.** On the host a request handler answering with a broadcast re-enters `Dispatch` inline; `OnLoadComplete` fires per player bind and per
late chunk; `NetAuthority.Refresh` races `Start` against `OnNetworkSpawn`; `Present()` runs twice; `Dismount` is re-entrant. And a restore
writes real state the reaction cannot tell apart from the real event.

**How it fails.** State applied twice; `InvalidOperationException: Collection was modified`; a ran-once flag that left every client simulating
its own copy; loot re-dropped and the death animation replayed on each load.

**Where.** [Multiplayer](systems/Multiplayer.md) · [Persistence](systems/Persistence.md) · [Combat](systems/Combat.md) · [Vehicles](systems/Vehicles.md)

## Ground probes reject loose bodies, your own hierarchy, and missing ground

**The rule.** Every downward probe skips non-kinematic Rigidbodies and anything under its own transform, and treats a miss as *not yet*, never
as a height to guess.

**Why.** A raycast sees a passenger as geometry: the deck rises, the carrier lifts the rider, the probe finds them higher, and the machine
climbs its own passenger. `Physics.IgnoreCollision` does nothing to a raycast, so pass-through surfaces must call
`IGroundProbeExclusions.ExcludeFromGroundProbes`. A miss in a streamed world means the chunk is not loaded.

**How it fails.** A vehicle climbs into the sky; a rope tied to a flank rides onto the animal's back; a walker stops at a hole it may cross.

**Where.** [Locomotion](systems/Locomotion.md) · [Vehicles](systems/Vehicles.md) · [Portals](systems/Portals.md) · [WalkerGround.cs](Assets/Game/Scripts/Locomotion/Ground/WalkerGround.cs)

## Statics outlive the world, the session and play mode

**The rule.** Every static in gameplay code needs an explicit reset from `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`, and must
never be assumed cleared by a return to the menu.

**Why.** Statics survive a world unload and a trip back to the main menu inside one process, and survive play-mode exit wherever domain reload
is off. Enter-play-mode options are enabled here, so never reason from a fresh domain.

**How it fails.** Match N+1 starts on match N's spawn ring; a leaked chunk activation queue; a storm clock carrying yesterday's world in.

**Where.** [Environment](systems/Environment.md) · [SceneTransitions](systems/SceneTransitions.md) · [WorldStreaming](systems/WorldStreaming.md) · [GameModes](systems/GameModes.md)

## Bind in `OnEnable`, and read the state as it is now

**The rule.** Subscribe in `OnEnable`, unsubscribe in `OnDisable`, and on binding read the current value instead of waiting for the next change.
Never bind a lambda nothing can remove.

**Why.** `NetworkVariable.OnValueChanged` never replays for a late joiner; a save-restored death is announced while the HUD is deactivated;
`FindLocalPlayer()` legitimately returns null for a frame. `AddComponent` runs `Awake` before the caller's next statement in play mode and none
at all outside it — so initialise explicitly (`Present()`, not `Awake()`) and resolve siblings lazily.

**How it fails.** Correct for everyone but the late joiner; a HUD that never shows the only announcement there was; a bag of nulls in a test.

**Where.** [UI](systems/UI.md) · [Multiplayer](systems/Multiplayer.md) · [PlayerCharacter](systems/PlayerCharacter.md) · [Testing](systems/Testing.md)

## Path casing is load-bearing

**The rule.** Treat every asset path as case-sensitive and never change a folder's case casually; where casing has already drifted, fix the
*reference*, not the folder.

**Why.** macOS and `core.ignorecase` hide drift from `git status`, but NGO hashes scene *paths* case-sensitively off each machine's disk,
`AssetDatabase.LoadAssetAtPath` is case-sensitive, and `FindProperty`-style lookups swallow the wrong casing and leave the field empty.

**How it fails.** A client join dies with `Scene Hash N does not exist in the HashToBuildIndex table`; a NavMesh bake silently skips every
chunk; a generated prefab ships an empty motor slot. The config asset says `Scenes/World/Chunks`; disk says `Scenes/world/Chunks`.

**Where.** [Scenes](systems/Scenes.md) · [WorldStreaming](systems/WorldStreaming.md) · [Multiplayer](systems/Multiplayer.md) · [EditorTooling](systems/EditorTooling.md)
