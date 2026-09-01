# Glossary

The project's vocabulary: every noun you are likely to meet in a task, a commit message, a doc or a
code comment, mapped to what it actually is and which system doc owns it. This is a disambiguation
layer, not a tutorial — look a word up, follow the link, then read that doc. Several words in this
codebase mean three different things depending on the sentence (**relay**, **entity**, **profile**,
**rig**, **anchor**, **Registry**, **session**, **`Present()`**); those rows say so explicitly, and
they are the ones worth reading before you start grepping.

| Term | What it is | Doc |
| --- | --- | --- |
| **agent** | A creature, NPC, enemy or turret: a prefab plus a stack of `IBehaviourModule` components arbitrated by priority | [AgentSystem](systems/AgentSystem.md) |
| **AgentController** | The one component that ticks an agent's modules, arbitrates them, and drives motor + animator | [AgentSystem](systems/AgentSystem.md) |
| **AgentGoal** | The single owner of "where does this agent go"; `GoalTravelModule` walks there | [AgentSystem](systems/AgentSystem.md) |
| **AgentTargeting** | The single owner of "who does this agent fight"; auto-added in `AgentController.Awake` | [AgentSystem](systems/AgentSystem.md) |
| **AimPivot** | Runtime child built by `PlayerViewNetwork`; the only place to hang things other players must see | [PlayerCharacter](systems/PlayerCharacter.md) |
| **anchor** | Streaming: a transform chunks stay loaded around, server-only. Not `InteriorAnchor`, not a storm/sky clock anchor | [WorldStreaming](systems/WorldStreaming.md) |
| **arena** | `MinigameArena.unity`, loaded additively ~16.5 km east of the grid for bot deathmatch — currently an empty scene | [GameModes](systems/GameModes.md) |
| **arrival** | The one-time crash landing that opens a story world; a server-flown hull plus a local-only cutscene | [PlayerShip](systems/PlayerShip.md) |
| **artifact** | A player-held usable item occupying a hotbar slot and firing on the shared `Player/Use` action | [Artifacts](systems/Artifacts.md) |
| **authored** | A saved object that already exists in a scene file, so its record is a *delta*; the opposite of runtime-spawned | [Persistence](systems/Persistence.md) |
| **autotest** | The two-process batch-mode run (`-sgmode host\|client\|persist`) printing `[MPTEST] key=value` | [Testing](systems/Testing.md) |
| **backpack** | The physical inventory: a deployable rig whose seven gridded faces hold real item instances | [Backpack](systems/Backpack.md) |
| **builder** | A `*Builder.cs` editor script that rewrites a prefab wholesale — hand edits to its output die on the next run | [EditorTooling](systems/EditorTooling.md) |
| **caravan** | An `NpcGroup` record lerping along a line in `NpcWorldSim`, becoming real prefabs only near a player | [AgentSystem](systems/AgentSystem.md) |
| **chunk** | One 500x500 m additive scene of the streamed world; 48 in the main world, addressed by scene *name* | [WorldStreaming](systems/WorldStreaming.md) |
| **crawler** | Six-legged walker. `DesertCrawler` is the AI-driven habitat; `RigWalker` is the piloted mount version | [Vehicles](systems/Vehicles.md) |
| **cutscene** | A coroutine `MonoBehaviour` (no Timeline, no Cinemachine), local to one machine, run by `CutsceneDirector` | [Cutscenes](systems/Cutscenes.md) |
| **DefaultNetworkPrefabs.asset** | The live network prefab list under `ScriptableObjects/Networking/`; the copy at the repo root is stale and unused | [Multiplayer](systems/Multiplayer.md) |
| **deferred pass** | `IDeferredSaveable.OnLoadComplete`, re-run per player bind and per late chunk — must be idempotent | [Persistence](systems/Persistence.md) |
| **DuneFoil** (the **foil**) | The sand sailer: no mount at all, a walkable deck with claimable stations and a wheel that turns the foil | [Vehicles](systems/Vehicles.md) |
| **EffectItem** | `UsableItem` subclass for a timed change to the holder's own body; `Use()` is sealed, override `ApplyEffect()` | [Artifacts](systems/Artifacts.md) |
| **entity** | Not a base class: three independent marker components — `IPersistentEntity`, `SceneTracked`, `EntityFaction` | [EntitySystem](systems/EntitySystem.md) |
| **EntityFaction** | The AI-targetability marker; without it a thing is invisible to every targeting module, silently | [AgentSystem](systems/AgentSystem.md) |
| **EntityProfile_\*** | Data-only authoring *MonoBehaviours* with a Generate button (not ScriptableObjects); exactly four exist | [EntitySystem](systems/EntitySystem.md) |
| **EntityTargetRegistry** | Static registry of `EntityFaction`s for AI targeting — nothing to do with `Registry<T>` or persistence | [EntitySystem](systems/EntitySystem.md) |
| **EquipmentController** | Hand sockets plus the only place an item use is triggered across the network | [Inventory](systems/Inventory.md) |
| **expedition rig** | The backpack prefab itself (`ExpeditionRig.prefab`), built from FBX by `ExpeditionRigWiring` | [Backpack](systems/Backpack.md) |
| **faction** | A `FactionDefinition` asset; relationships live in the one `GlobalRelationships.asset` table | [AgentSystem](systems/AgentSystem.md) |
| **Ferdinand world** | The second, 4x2-chunk world; reachable only by opening its scene in the editor, no menu route | [WorldStreaming](systems/WorldStreaming.md) |
| **flashlight** | The player torch in three layers: URP spot, global "long-throw" shader uniforms, screen-space beam mesh | [Flashlight](systems/Flashlight.md) |
| **FogVolume** | A scene-authored body of air; the 8 nearest are marched in one pass. Holds no runtime state, saves nothing | [Environment](systems/Environment.md) |
| **gait** | `IGaitPattern`: per-leg phase offsets and duty. Bind it *before* deriving speeds or the machine never steps | [Locomotion](systems/Locomotion.md) |
| **GameplayMenuScope** | The ref-counted owner of cursor, input and timescale while a panel is open; `AcceptsGameplayInput` is the hotkey gate | [UI](systems/UI.md) |
| **GameServices** | The static service locator (`.World`, `.ItemDropService`); statics survive the menu→world load, MonoBehaviours do not | [CoreServices](systems/CoreServices.md) |
| **GlobalObjectIdHash** | How NGO identifies a prefab on the wire; a script-built `NetworkObject` ships `0` and gets silently dropped | [Multiplayer](systems/Multiplayer.md) |
| **GlobalRelationships.asset** | The single `FactionRelationshipTable`; a pair with no row is `Neutral`, so adding one turns a faction hostile | [AgentSystem](systems/AgentSystem.md) |
| **hold pose** | The masked upper-body animator layer a held item puts the character in; auto-added by `OnEquipped` | [Artifacts](systems/Artifacts.md) |
| **hold stream** | The 15 Hz `OnRequestHold`/`Hold`/`PresentHold` tick loop of an `IsContinuous` item; the final tick is always "stop" | [Artifacts](systems/Artifacts.md) |
| **hotbar** | The player's four server-authoritative inventory slots; overflow goes to the backpack | [Inventory](systems/Inventory.md) |
| **hydrate / dehydrate** | Moving a scene's saved state into live objects and back out, driven by chunk and interior load/unload events | [Persistence](systems/Persistence.md) |
| **IExternallyPosed** | Marker saying "I move the body *and* solve limbs" — `NetAuthority` follows such drivers instead of disabling them | [Locomotion](systems/Locomotion.md) |
| **IInteractable** | `CanInteract()` + `Interact(Interactor)`; needs a collider, and one per collider only | [Interaction](systems/InteractionSystem.md) |
| **instanceId** | Per-object GUID answering "which save record is mine"; distinct from `prefabId` | [Persistence](systems/Persistence.md) |
| **interior** | An additive scene (cave, building) loaded beside the streamed exterior, refcounted by `InteriorManager` | [SceneTransitions](systems/SceneTransitions.md) |
| **InteriorAnchor** | A spawn/exit marker inside an interior scene, keyed by `(scene name, id)`. Not a streaming anchor | [SceneTransitions](systems/SceneTransitions.md) |
| **InventoryItem** | The ScriptableObject under `Resources/Items`; its `ID` is the asset GUID and the save/registry key | [Inventory](systems/Inventory.md) |
| **IPersistentEntity** | Empty marker interface meaning "I am mutable world" — the declared save opt-in for kinematic rigs and rootless vehicles | [Persistence](systems/Persistence.md) |
| **ItemGrip** | Per-prefab hand, grip point, offsets, `holdSize` and `packSize`. `holdSize` is metres, not a multiplier | [Inventory](systems/Inventory.md) |
| **ItemState** | The per-hotbar-slot string bag where an item's instance state lives; the instance itself is destroyed on unequip | [Artifacts](systems/Artifacts.md) |
| **ITeleportAware** | The seam every instant move announces, carrying a rigid `Transfer` matrix so listeners rebase world-space state | [SceneTransitions](systems/SceneTransitions.md) |
| **lander** | `PlayerShip`: the script-generated, walkable, 4-seat hover hull that also flies the crash landing | [PlayerShip](systems/PlayerShip.md) |
| **lasso** | A throwable loop artifact with its own Verlet rope and twirl-charge hold stream — **not** the leash | [Artifacts](systems/Artifacts.md) |
| **leash** | A rope tied between any two things; each machine resolves only the ends it owns. **Not** the lasso | [LeashSystem](systems/LeashSystem.md) |
| **LeggedDriver** | The Assembly-CSharp motor that turns rider input and AI intent into a twist for a `LeggedLocomotion` | [Locomotion](systems/Locomotion.md) |
| **LeggedLocomotion** | The kinematic base class owning procedural walking; it is the *sole* owner of the body transform | [Locomotion](systems/Locomotion.md) |
| **LobbySession** | App-lifetime owner of UGS lobby state; `Instance` creates one on touch, use `Existing` to merely ask | [Lobby](systems/Lobby.md) |
| **MatchManager** | Server orchestrator for all three arena gamemodes: bots, factions, kills, win check, leaderboard | [GameModes](systems/GameModes.md) |
| **motor** | An `IMovementMotor`: the single owner of "how does this agent move" (NavMesh, rigidbody, hover, flight, legs) | [AgentSystem](systems/AgentSystem.md) |
| **mount** | Taking a machine over — seat, camera and controls transfer to you. Contrast **station** | [Vehicles](systems/Vehicles.md) |
| **MountModule** | One seat. A hull with four seats carries four of them, addressed positionally by `MountIndex` | [Vehicles](systems/Vehicles.md) |
| **MountStation** | A cockpit control that mounts you, so the hull itself can set `mountableByDirectInteraction = false` | [Vehicles](systems/Vehicles.md) |
| **MoveIntent** | What a behaviour module returns. `null` passes to the next module; `Idle()` claims the frame and starves the rest | [AgentSystem](systems/AgentSystem.md) |
| **NetArg** | The fixed message payload `Target,A,B,P,R`. `A` is reserved for the hotbar slot on item messages | [Multiplayer](systems/Multiplayer.md) |
| **NetAuthority** | Disables simulation drivers and freezes the Rigidbody on remote copies; idempotent, stops at the `NetworkObject` boundary | [Multiplayer](systems/Multiplayer.md) |
| **NetChannel** | The per-entity handler table. Sibling components share one channel — number them with `IndexOf<T>` | [Multiplayer](systems/Multiplayer.md) |
| **NetDamage** | `NetDamage.Apply` — the only sanctioned way to hurt anything; never call `HealthComponent.Damage` | [Combat](systems/Combat.md) |
| **NetLatch** | One networked bit for an open/closed fixture (door, lever, launch button), with late-joiner ask and restore | [Interaction](systems/InteractionSystem.md) |
| **NetMsg** | The append-only message id catalog; ids 3 and 30 are burned and must never be reused | [Multiplayer](systems/Multiplayer.md) |
| **NetRelay** | *This project's* wire: three RPCs on a `NetworkObject`. Nothing to do with Unity Relay — see **relay** | [Multiplayer](systems/Multiplayer.md) |
| **Network** | The authority facade: ask `Network.Simulates(c)` (may I decide?) or `Network.Owns(c)` (mine to drive?), never `IsServer` | [Multiplayer](systems/Multiplayer.md) |
| **NpcPassenger** | NPC riders: the *mount* is the agent and the rider is switched-off cargo. Shares no code with `MountModule` | [Vehicles](systems/Vehicles.md) |
| **NpcWorldSim** | Server-only simulation of virtual NPC groups that spawn into real prefabs near a player and fold back beyond it | [AgentSystem](systems/AgentSystem.md) |
| **ornithopter** | The 10 m flapping-wing aircraft, carried folded as the wing pack and flown on a point-mass energy model | [Ornithopter](systems/Ornithopter.md) |
| **PackSurface** | One flat face of the backpack (`SURF_` empty): id + size in metres, uv↔world. Ids are persisted bytes — append only | [Backpack](systems/Backpack.md) |
| **palette** | The one shared material library (`palette.blend`, 54 materials); nothing in a `.blend` defines a local material | [ArtPipeline](systems/ArtPipeline.md) |
| **persistentScene** | The root gameplay scene loaded `Single`; everything else in-game is additive on top of it | [Scenes](systems/Scenes.md) |
| **PlayerCharacterNetworked** | The prefab actually spawned for a player — a *variant* of `PlayerCharacter`, which is where gameplay components live | [PlayerCharacter](systems/PlayerCharacter.md) |
| **PlayerIdentity** | On the player prefab: name + suit colour are owner-write, team is server-write; the source for rosters and nameplates | [Multiplayer](systems/Multiplayer.md) |
| **portal** | A sprayable one-way-pair aperture you walk through; not a `NetworkObject` — placement replicates as a message | [Portals](systems/Portals.md) |
| **prefabId** | Asset GUID answering "what do I instantiate"; no prefab on disk ships one until the wiring tool stamps it | [Persistence](systems/Persistence.md) |
| **`Present()`** | Two unrelated things: the cosmetic every-machine half of an item use, and `MenuScreen.Present()`, which builds a menu page | [Artifacts](systems/Artifacts.md) |
| **profile** | 5 senses: `TargetingProfile`, `EntityProfile_*`, a save `PlayerProfile` GUID, a UGS `SessionProfile`, a `SandstormProfile` | [EntitySystem](systems/EntitySystem.md) |
| **ragdoll** | A skeleton *derived* at runtime from mesh vertex weights — no `CharacterJoint` is authored anywhere on disk | [Combat](systems/Combat.md) |
| **Registry&lt;T&gt;** | The **item** registry (`Resources/Items`), keyed by string ID. Never wire an entity into it | [CoreServices](systems/CoreServices.md) |
| **relay** | 4 senses: Unity Relay (transport), `NetRelay` (our wire), `AgentActionRelay`/`InteractorRelay`, `Unity.Relay.Editor` (AI sidecar) | [Multiplayer](systems/Multiplayer.md) |
| **rig** | Five senses: the expedition rig (backpack), a `WalkerRig` (legged limbs), the wing rig, `PlayerAimRig`, or a Blender armature | [Backpack](systems/Backpack.md) |
| **RigWalker** | The *piloted* six-legged walker prefab (mount + steer + deck carrier); not the same prefab as `DesertCrawler` | [Vehicles](systems/Vehicles.md) |
| **sandstorm** | A ~30-byte record on a shared clock; position, intensity, visuals and damage are all derived, never replicated per frame | [Environment](systems/Environment.md) |
| **SaveableEntity** | The component carrying `prefabId` / `instanceId` / `authored` / `SaveScope`; auto-attached, rarely added by hand | [Persistence](systems/Persistence.md) |
| **SaveablePolicy** | The single opt-in rule (`NeedsSaving`) plus the auto-wiring that attaches savers to a prefab or scene object | [Persistence](systems/Persistence.md) |
| **SaveRef** | A cross-object reference `{kind: player\|entity, id}`; resolve it in `OnLoadComplete`, never in `RestoreState` | [Persistence](systems/Persistence.md) |
| **SaveScope.External** | Takes an object out of world capture (players, caravan members) so two systems cannot both own its record | [Persistence](systems/Persistence.md) |
| **SaveTeleport** | The only correct instant move: disables `CharacterController`, warps the agent, resyncs bodies, raises `ITeleportAware` | [SceneTransitions](systems/SceneTransitions.md) |
| **SceneTracked** | Opt-in for a moving entity: `Pin` / `Migrate` / `Despawn` between chunk scenes, plus `keepChunksLoaded` | [WorldStreaming](systems/WorldStreaming.md) |
| **SceneTransition** | The door/threshold orchestrator: trigger → destination (SO) + effects (SO[]), run on a `DontDestroyOnLoad` host | [SceneTransitions](systems/SceneTransitions.md) |
| **session** | 4 senses: `WorldSession` (which world), `LobbySession` (UGS), `VersusSession` (the match), the NGO session `SessionLauncher` starts | [Multiplayer](systems/Multiplayer.md) |
| **settlement** | A seeded, tile-generated town emitted at edit time into the scene. Not a **site** | [ProceduralGeneration](systems/TerrainGeneration.md) |
| **SfxId** | The 71-value sound vocabulary an `AudioCatalog` maps to FMOD events; new events cannot be authored, the `.fspro` is lost | [Audio](systems/audio.md) |
| **ShipRV** | The hover RV vehicle prefab — and the carrier of the world's only `SpawnPoint`, so losing it hangs every spawn | [Vehicles](systems/Vehicles.md) |
| **site** | A hand-placed `WorldSiteMarker` publishing a `WorldSite` record NPCs navigate by. Not a **settlement** | [ProceduralGeneration](systems/TerrainGeneration.md) |
| **`_Source~`** | `Art/Models/_Source~/`: the Unity-invisible Blender library. No `.meta`, no GUIDs, nothing there is referenceable | [ArtPipeline](systems/ArtPipeline.md) |
| **SpawnPoint** | The scene marker `SpawnManager` resolves a player spawn anchor from; absent, nobody spawns and one error is logged | [GameModes](systems/GameModes.md) |
| **StateBag** | `key -> JObject` inside a save record; one saver owns one key, which is why adding a saver needs no migration | [Persistence](systems/Persistence.md) |
| **station** | Keeping your body and camera while claiming one control on a walkable deck (`VehicleStation`). Contrast **mount** | [Vehicles](systems/Vehicles.md) |
| **stow** | Backpack: putting an item onto a pack face (or reshouldering the pack). Vehicles: retracting a deployed part | [Backpack](systems/Backpack.md) |
| **suit** | The player's colour swatch: one synced index recolouring seven materials matched by *name*, shared with ship livery | [PlayerCharacter](systems/PlayerCharacter.md) |
| **TargetingProfile** | A ScriptableObject overriding every inline `AgentTargeting` field; `MatchManager` swaps it in for arena bots | [AgentSystem](systems/AgentSystem.md) |
| **TerrainFeature** | An edit-time marching-cubes addition on top of authored terrain. Only two types remain: `Mesa = 2`, `Cliff = 4` | [ProceduralGeneration](systems/TerrainGeneration.md) |
| **thopter** | Shorthand for the ornithopter | [Ornithopter](systems/Ornithopter.md) |
| **ToolItem** | `UsableItem` subclass for an aimed, instant or world-changing use; adds `aimProvider` | [Artifacts](systems/Artifacts.md) |
| **UnderTerrainGuard** | Owner-side failsafe holding a body still while its ground is still streaming in | [WorldStreaming](systems/WorldStreaming.md) |
| **UsableItem** | The base class for everything held; owns the `Use()` / `Present()` split, so every item is networked by default | [Artifacts](systems/Artifacts.md) |
| **UseAuthority** | `Server` \| `Owner` — which machine runs `Use()`. `Owner` is for effects on the holder's own (owner-authoritative) body | [Artifacts](systems/Artifacts.md) |
| **Versus** (VS) | Team PvP in the streamed world; 2–8 teams x 1–12, one team ship each, no scoring and no win condition | [GameModes](systems/GameModes.md) |
| **VolumeTrigger** | Walk-in trigger that fires **server-only**; its click-to-fire sibling `InteractableTrigger` is deliberately ungated | [Interaction](systems/InteractionSystem.md) |
| **WalkerPlatformCarrier** | Re-applies a transform-driven hull's per-frame delta to bodies standing on it — transform hulls impart no friction | [Vehicles](systems/Vehicles.md) |
| **wing pack** | The folded ornithopter as a worn item; launching spawns the craft and seats the pilot as its owner | [Ornithopter](systems/Ornithopter.md) |
| **WorldSaveStore** | The chunk-aware half of the save system: `instanceId -> EntityRecord`, hydrated and dehydrated per scene | [Persistence](systems/Persistence.md) |
| **WorldSession** | The static carrying which world is staged, its config GUID and the staged document across the menu→world load | [Persistence](systems/Persistence.md) |
| **WorldStreamer** | The one component that loads and unloads chunk scenes, server-only, through a single sequential op queue | [WorldStreaming](systems/WorldStreaming.md) |
