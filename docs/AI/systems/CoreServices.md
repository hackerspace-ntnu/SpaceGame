---
system: CoreServices
layer: core
summary: Boot order, the static service/registry locators, player input bindings, and PlayerPrefs-backed settings
paths:
  - Assets/Game/Scripts/Core/GameServices/
  - Assets/Game/Scripts/Core/Input/
  - Assets/Game/Scripts/Core/Registry/
  - Assets/Game/Scripts/Core/Settings/
  - Assets/Game/Scenes/Core/Bootstrap.unity
symptoms:
  - "GameServices.World is null and every spawn or despawn NREs"
  - "I edited the .inputactions asset and the new key binding does nothing"
  - "an item, faction or targeting asset never turns up in Registry<T>.Get"
  - "playing straight from a world scene has no items, no audio, no registries"
  - "my asmdef cannot see PlayerController / GameServices / NetMessaging"
  - "a gameplay hotkey still fires while a menu or the chat box is open"
reads_with: [Multiplayer, Persistence, SceneTransitions, UI]
updated: 2026-09-01
---

# Core Services

The glue layer: boot order, the static service/registry locators, player input, and PlayerPrefs-backed settings.

**Scope:** [Assets/Game/Scripts/Core/GameServices/](Assets/Game/Scripts/Core/GameServices), [Core/Input/](Assets/Game/Scripts/Core/Input), [Core/Registry/](Assets/Game/Scripts/Core/Registry), [Core/Settings/](Assets/Game/Scripts/Core/Settings), [Core/SceneManagement/Core/](Assets/Game/Scripts/Core/SceneManagement/Core), [Assets/Game/Settings/Input/](Assets/Game/Settings/Input), [Bootstrap.unity](Assets/Game/Scenes/Core/Bootstrap.unity), all `.asmdef`s.
**Related:** [Multiplayer.md](Multiplayer.md), [Persistence.md](Persistence.md), [SceneTransitions.md](SceneTransitions.md), [UI.md](UI.md), [audio.md](audio.md)

## Model

- **No DI container, no MonoBehaviour singleton for services.** Cross-system access is via *static* classes: [`GameServices`](Assets/Game/Scripts/Core/GameServices/Core/GameServices.cs) (`.World`, `.ItemDropService`), [`Registry<T>`](Assets/Game/Scripts/Core/Registry/Registry.cs), [`GameSettings`](Assets/Game/Scripts/Core/Settings/GameSettings.cs), [`Game`](Assets/Game/Scripts/Gameplay/Game/State/Game.cs), [`WorldSession`](Assets/Game/Scripts/Core/Persistence/Runtime/WorldSession.cs), [`GameplayMenuScope`](Assets/Game/Scripts/Presentation/UI/Widgets/GameplayMenuScope.cs), [`Network`](Assets/Game/Scripts/Core/Multiplayer/Authority/Network.cs). Statics survive the `LoadSceneMode.Single` between menu and world; MonoBehaviours do not.
- `Instance` singletons exist only for scene-lived coordinators: `SaveManager`, `InteriorManager`, `TransitionRunner`, `NetworkGameManager`, `LobbySession`, `ChatNetwork`. Treat them as nullable.
- Boot is driven by two `[RuntimeInitializeOnLoadMethod]` hooks in [`Bootstrapper`](Assets/Game/Scripts/Core/SceneManagement/Core/Bootstrapper.cs) plus one in `GameSettings` and two in [`NetworkBootstrap`](Assets/Game/Scripts/Core/Multiplayer/Session/NetworkBootstrap.cs) — not by scene wiring.
- Scene 0 is `Bootstrap`, scene 1 is `MainMenu` ([EditorBuildSettings.asset](ProjectSettings/EditorBuildSettings.asset)). Entering play in any other scene bounces through Bootstrap first, then back.
- The `Bootstrapper` GameObject in Bootstrap.unity carries exactly `RegistryLoader` + `GameServiceLoader`; the scene also instances `NetworkManager.prefab` and `AudioManager.prefab` (both `DontDestroyOnLoad` themselves).
- Input is per-player-object, not global: [`PlayerInputManager`](Assets/Game/Scripts/Core/Input/PlayerInputManager.cs) lives on the player prefab and is enabled/disabled to take control away (death, menus, cutscenes).

## Key types

| Type | File | Role |
| --- | --- | --- |
| `Bootstrapper` | [Bootstrapper.cs](Assets/Game/Scripts/Core/SceneManagement/Core/Bootstrapper.cs) | Static; forces scene 0 then loads the intended scene |
| `GameServiceLoader` | [GameServiceLoader.cs](Assets/Game/Scripts/Core/GameServices/Core/GameServiceLoader.cs) | `Awake` → `GameServices.Initialize()` |
| `GameServices` | [GameServices.cs](Assets/Game/Scripts/Core/GameServices/Core/GameServices.cs) | Static locator; re-runs on `Game.OnGameModeChanged` |
| `IWorldService` / `WorldService` | [IWorldService.cs](Assets/Game/Scripts/Core/GameServices/Interfaces/IWorldService.cs), [WorldService.cs](Assets/Game/Scripts/Core/GameServices/Implementations/WorldService.cs) | Spawn/despawn that also handles NGO spawn + `SaveablePolicy.EnsureSpawned` + `SaveManager.NotifyDestroyed` |
| `IItemDropService` / `PlayerDropService` | [IItemDropService.cs](Assets/Game/Scripts/Core/GameServices/Interfaces/IItemDropService.cs), [PlayerDropService.cs](Assets/Game/Scripts/Core/GameServices/Implementations/PlayerDropService.cs) | Drop an `InventoryItem` into the world |
| `Registry<T>` / `IRegistryEntry` | [Registry.cs](Assets/Game/Scripts/Core/Registry/Registry.cs), [IRegistryEntry.cs](Assets/Game/Scripts/Core/Registry/IRegistryEntry.cs) | `string ID` → ScriptableObject; `Get`, `All` |
| `RegistryLoader` | [RegistryLoader.cs](Assets/Game/Scripts/Core/Registry/RegistryLoader.cs) | Loads `Resources/Items`, then `SaveablePrefabRegistry.LoadAll()` |
| `GameSettings` | [GameSettings.cs](Assets/Game/Scripts/Core/Settings/GameSettings.cs) | All player options, PlayerPrefs, `Changed` event |
| `PlayerInputManager` | [PlayerInputManager.cs](Assets/Game/Scripts/Core/Input/PlayerInputManager.cs) | Single source of player input; owns `InputControls` |
| `InputManager` | [InputManager.cs](Assets/Game/Scripts/Core/Input/InputManager.cs) | Legacy stub reading `InputSystem.actions.FindAction("Attack")` — an action that does not exist in this asset |
| `SceneReference` | [SceneReference.cs](Assets/Game/Scripts/Core/SceneManagement/Core/SceneReference.cs) | ScriptableObject wrapping a scene *name* (editor-only `SceneAsset` field) |
| `Game` / `GameMode` | [Game.cs](Assets/Game/Scripts/Gameplay/Game/State/Game.cs) | `Singleplayer` \| `Multiplayer`; drives service reload |
| Root scripts | [BillboardAlongAxis.cs](Assets/Game/Scripts/BillboardAlongAxis.cs), [RocketBoosterController.cs](Assets/Game/Scripts/RocketBoosterController.cs), [RocketBoosterShaderController.cs](Assets/Game/Scripts/RocketBoosterShaderController.cs), [VolumetricExplosionController.cs](Assets/Game/Scripts/VolumetricExplosionController.cs) | Global-namespace VFX/billboard leftovers; not part of core |

## Assemblies

Almost all gameplay code is in the **default `Assembly-CSharp`** (no asmdef). The asmdefs below are leaf/utility islands carved out so EditMode tests can reference them.

| Assembly | .asmdef path | Covers | References |
| --- | --- | --- | --- |
| `SpaceGame.Audio` | [Audio/](Assets/Game/Scripts/Audio/SpaceGame.Audio.asmdef) | SFX ids / catalog | `FMODUnity` |
| `SpaceGame.Persistence` | [Core/Persistence/Format/](Assets/Game/Scripts/Core/Persistence/Format/SpaceGame.Persistence.asmdef) | Save document format | — (precompiled `Newtonsoft.Json.dll`, `overrideReferences`) |
| `SpaceGame.Teleporting` | [Core/Teleporting/](Assets/Game/Scripts/Core/Teleporting/SpaceGame.Teleporting.asmdef) | `ITeleportAware` seam | — |
| `SpaceGame.Locomotion` | [Locomotion/](Assets/Game/Scripts/Locomotion/SpaceGame.Locomotion.asmdef) | Legged locomotion core | Persistence, Teleporting |
| `SpaceGame.Creatures.{Ostrich,Horse,Crab,Humanoid}` | `Creatures/<name>/` | Per-rig gait policies | Locomotion, Teleporting |
| `SpaceGame.Vehicles.Crawler` | [Vehicles/DesertCrawler/](Assets/Game/Scripts/Vehicles/DesertCrawler/SpaceGame.Vehicles.Crawler.asmdef) | Hexapod crawler | Locomotion, Teleporting |
| `SpaceGame.Vehicles.DuneFoil` | [Vehicles/DuneFoil/](Assets/Game/Scripts/Vehicles/DuneFoil/SpaceGame.Vehicles.DuneFoil.asmdef) | Sailer physics | Persistence, Teleporting |
| `SpaceGame.Vehicles.Ornithopter` | [Vehicles/Ornithopter/](Assets/Game/Scripts/Vehicles/Ornithopter/SpaceGame.Vehicles.Ornithopter.asmdef) | Flight model | `FMODUnity`, Audio, Teleporting |
| `SpaceGame.Gear.JumpingRod` | [Gear/JumpingRod/](Assets/Game/Scripts/Gear/JumpingRod/SpaceGame.Gear.JumpingRod.asmdef) | Pogo maths | — |
| `SpaceGame.Minigame.Core` | [Gameplay/Minigame/Core/](Assets/Game/Scripts/Gameplay/Minigame/Core/SpaceGame.Minigame.Core.asmdef) | Match rules | — |
| `SpaceGame.Versus.Core` | [Gameplay/Versus/Core/](Assets/Game/Scripts/Gameplay/Versus/Core/SpaceGame.Versus.Core.asmdef) | Team/ring layout | — |
| `SpaceGame.World.Safety` | [World/Safety/Rules/](Assets/Game/Scripts/World/Safety/Rules/SpaceGame.World.Safety.asmdef) | Safety rules | — |
| `SpaceGame.World.Streaming` | [World/Streaming/Grid/](Assets/Game/Scripts/World/Streaming/Grid/SpaceGame.World.Streaming.asmdef) | Chunk grid maths | — |
| `SpaceGame.Tests.EditMode` | [Tests/EditMode/](Assets/Game/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef) | Editor-only, `autoReferenced: false` | every asmdef above except Audio, + TestRunner |

**Reference rules that bite:**
- An asmdef **cannot reference `Assembly-CSharp`** (Unity forbids it — the default assembly references all asmdefs, never the reverse). So nothing in `SpaceGame.Locomotion`, `SpaceGame.Persistence`, etc. can touch `PlayerController`, `GameServices`, `Registry<T>`, `NetMessaging` … Code that needs both sides belongs in `Assembly-CSharp`, or the shared type must be pushed down into an asmdef (that is why `SpaceGame.Teleporting` exists as a one-interface assembly).
- All asmdefs are `autoReferenced: true`, so `Assembly-CSharp` sees them without extra wiring.
- `Assets/Game/Tests/Editor/` (36 files) has **no** asmdef → it compiles into `Assembly-CSharp-Editor` and *can* see `Assembly-CSharp`. `Assets/Game/Tests/EditMode/` has one and cannot. Put a test where its subject lives.
- `Assets/Game/Editor/` and every `**/Editor/` folder under `Scripts/` also fall into `Assembly-CSharp-Editor`.

## Input

Asset: [InputSystem_Actions.inputactions](Assets/Game/Settings/Input/InputSystem_Actions.inputactions) → generated wrapper class `InputControls` at [InputControls.cs](Assets/Game/Settings/Input/InputControls.cs) (3031 lines, global namespace, `generateWrapperCode: 1` in the `.meta`).

| Map | Actions |
| --- | --- |
| `Player` | Move, Look, Use, Interact, Crouch, Jump, Previous, Next, Sprint, Dash, Vertical, Turn, Backpack |
| `UI` | Navigate, Submit, Cancel, Point, Click, RightClick, MiddleClick, ScrollWheel, TrackedDevice*, Hotkey, Map, Pause, DevInventory, Chat, Hud |
| `Hotbar` | Hotbar1–Hotbar10, Drop, HotbarScroll |

Control schemes: `Keyboard&Mouse`, `Gamepad`, `Touch`, `Joystick`, `XR`.

**The generated file embeds its own copy of the JSON** (`InputActionAsset.FromJson(@"...")` at line 87) and is what binds at runtime — editing the `.inputactions` changes nothing until the asset is reimported and the wrapper regenerated. Because that regeneration rewrites 3000 lines, several actions are instead **built in code** in `PlayerInputManager.EnsureInputs()`: `Aim` (RMB / left trigger), `PackYaw` (wheel), `PackStow1–4` (keys 1–4), `PackRack` (R / north button). Those are enabled explicitly (`SetPackYawEnabled` etc.) because focus mode disables the whole component.

## Flows

1. Play pressed. `GameSettings.ApplyEngineSettings` (BeforeSceneLoad) loads PlayerPrefs, applies quality/vsync/frame cap/window.
2. `Bootstrapper.BeforeSceneLoad`: if scene 0 is not loaded, remember the current build index and `LoadScene(0, Single)`.
3. Bootstrap scene awakes: `RegistryLoader` (items → `SaveablePrefabRegistry.LoadAll()`), `GameServiceLoader` (`GameServices.Initialize()`), `NetworkManager`, `AudioManager`.
4. `Bootstrapper.AfterSceneLoad`: `LoadSceneAsync(targetScene ?? 1, Single)` and `SetActiveScene`. Bootstrap unloads; only statics and `DontDestroyOnLoad` objects survive.
5. `NetworkBootstrap` (AfterSceneLoad, editor only) spawns a NetworkManager if one is missing, and strips orphan scene `NetworkObject`s on every peer.
6. `MainMenuUI` stages the world via `WorldSession.StageNew/StageExisting`, sets `Game.SetMode`, starts host/client, then `NetworkManager.Singleton.SceneManager.LoadScene(gameScene.SceneName, Single)` — never plain `SceneManager` for the world.
7. World scene loads; player prefab spawns with its own `PlayerInputManager`; `SaveManager` hydrates.

## Multiplayer

- `GameServices.LoadServices` fires for both modes deliberately — an empty `Multiplayer` case once left `GameServices.World` null and NREd every despawn.
- `WorldService.Spawn` is **server-only**: on a client it logs an error and destroys the local instance rather than creating a ghost. Route through an RPC.
- `Network.IsNetworked` / `Network.Server` gate the NGO half; the offline path is a plain `Instantiate`.
- `NetworkBootstrap.RemoveOrphanSceneNetworkObjects` prevents "Scene Hash does not exist" style client-sync failures from menu scenes.
- Input, settings and registries are **local**: nothing here replicates. Player name and suit colour are read from `GameSettings` and published by `PlayerIdentity`.

## Persistence

- Settings only: `PlayerPrefs`, all keys prefixed `SpaceGame.Settings.` plus a `Version` key checked against `SchemaVersion` (currently 1).
- Keys: PlayerName, SuitColorIndex, Master/Music/Sfx/Ui/AmbienceVolume, MouseSensitivity, InvertLookY, CameraShakeIntensity, InvertHotbarScroll, DevMode, FieldOfView, QualityLevel, Fullscreen, ResolutionIndex, VSync, FrameRateCap.
- Lazy load on first property read (`EnsureLoaded`); every setter clamps, writes, and raises `GameSettings.Changed`. `Save()` flushes; `ResetToDefaults()` deletes and re-seeds.
- `SeedFieldOfView` / `SeedInvertHotbarScroll` adopt an authored inspector value **only** while the key is absent.
- World/entity state is a different system entirely — see [Persistence.md](Persistence.md).

## Gotchas

- **`CameraShakeIntensity` is missing from `ResetToDefaults`'s key list** ([GameSettings.cs:374](Assets/Game/Scripts/Core/Settings/GameSettings.cs)) — a reset leaves the old shake value in PlayerPrefs.
- **`InputManager` is dead weight**: it looks up an action named `"Attack"`, which does not exist in `InputSystem_Actions`, so `OnUsePressed` never fires. Use `PlayerInputManager`.
- Editing `.inputactions` alone does nothing (see Input). Re-check `InputControls.cs` actually changed before believing a rebind.
- `Registry<T>` is registered by two different mechanisms: `InventoryItem` explicitly in `RegistryLoader`; `FactionDefinition`, `FactionRelationshipTable` and `TargetingProfile` from their own `OnEnable`, i.e. **only once Unity has loaded that asset**. An asset nothing references may never register.
- Registry order matters: items **before** `SaveablePrefabRegistry.LoadAll()`, since half the prefab table is derived from items.
- Playing directly from a world/menu scene skips Bootstrap's registry and audio; `NetworkBootstrap` patches only the NetworkManager and logs a warning that the rest are still absent.
- `PlayerInputManager.OnEnable` can run before its own `Awake` (`PlayerController.Awake` toggles the component), hence `EnsureInputs()` at every entry point. Callbacks are bound once in `BindActions`, never in `OnEnable` — lambdas cannot be unsubscribed, and a death/respawn cycle would double-fire jump.
- `OnDisable` zeroes `MoveInput`/`LookInput`/`CrouchHeld`/`AimHeld` on purpose: axes are only written in `Update`, so a stale vector would outlive death.
- `SceneReference` stores a scene **name**, not a path or index. NGO hashes scene *paths* case-sensitively — see [Multiplayer.md](Multiplayer.md).
- `Bootstrapper.AfterSceneLoad` is `async void` with no error handling; an exception during the target load is swallowed. `ApplyEngineSettings` skips window mode in the editor deliberately (it would fullscreen the Game view every Play).
- Any world-level hotkey must first check `GameplayMenuScope.AcceptsGameplayInput` — the shared, reference-counted gate.

## Extending

**Add a global service**
1. Declare `IFooService` in [Interfaces/](Assets/Game/Scripts/Core/GameServices/Interfaces) and the implementation in [Implementations/](Assets/Game/Scripts/Core/GameServices/Implementations).
2. Add a `public static IFooService Foo { get; set; }` to `GameServices` and assign it in `LoadServices` — in **both** switch cases unless it is genuinely mode-specific.
3. Branch internally on `Network.IsNetworked` / `Network.Server`; do not branch on `Game.Mode` (singleplayer is a host of one).
4. Nothing to register in Bootstrap: `GameServiceLoader` already runs `Initialize()`. If the service needs a scene object, resolve it lazily — Bootstrap unloads.
5. If it holds runtime state, give it a saver (see [Persistence.md](Persistence.md)) or state explicitly that it holds none.

**Add an input binding**
1. Prefer the asset for anything on an existing map: edit [InputSystem_Actions.inputactions](Assets/Game/Settings/Input/InputSystem_Actions.inputactions), then reimport it so [InputControls.cs](Assets/Game/Settings/Input/InputControls.cs) regenerates. Confirm the new action appears in the generated file.
2. Subscribe in `PlayerInputManager.BindActions()` — once, never in `OnEnable` — and expose it as an `event Action` (plus a `…Held` bool if it is a hold, cleared in `OnDisable`).
3. For a one-off button that must survive a regeneration risk, or must stay live while the component is disabled, build the `InputAction` in `EnsureInputs()` with `.WithGroup("Keyboard&Mouse")` / `"Gamepad"` bindings and add an explicit `SetXEnabled(bool)`, following `SetPackRackEnabled`.
4. Disable and dispose it in `OnDisable` / `OnDestroy` alongside the others.
5. Gate the consumer on `GameplayMenuScope.AcceptsGameplayInput`, and if it is player-visible make sure it fires on a client, not just the host.
