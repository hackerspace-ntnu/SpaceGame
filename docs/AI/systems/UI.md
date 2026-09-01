---
system: UI
layer: presentation
summary: Menus, HUD, full-screen overlays and world-anchored labels, all built in C# at runtime, no UI art
paths:
  - Assets/Game/Scripts/Presentation/UI/
  - Assets/Game/Scripts/Core/Settings/GameSettings.cs
  - Assets/Game/Prefabs/UI/HUD/PlayerHUD.prefab
  - Assets/Game/Art/Animations/UI/Buttons/Menu Button.controller
symptoms:
  - "the game stays frozen after I close the menu and nothing can unfreeze it"
  - "a menu swallows my movement keys, or a hotkey does nothing while a panel is open"
  - "a disabled menu row stays stuck in its hover colour and never resets"
  - "the panel draws fine but no button responds to clicks"
  - "a HUD element stays blank until something happens to it"
  - "the tint I set to mark the selected entry disappears after one frame"
  - "my arrow or spinner glyph renders as nothing"
  - "a row silently inflates and blows out the layout"
  - "the death screen does not appear for a player who died before loading"
reads_with: [Lobby, Inventory, Persistence, audio]
updated: 2026-09-01
---

# UI

Every screen in the game — the main-menu page stack, the in-game HUD, the full-screen overlays that open over gameplay, and the world-anchored labels — all built in C# at runtime, no UI art assets.

**Scope:** [Assets/Game/Scripts/Presentation/UI/](Assets/Game/Scripts/Presentation/UI) (71 files) + [Menu Button.controller](Assets/Game/Art/Animations/UI/Buttons/Menu%20Button.controller) + [GameSettings.cs](Assets/Game/Scripts/Core/Settings/GameSettings.cs)
**Related:** [Lobby.md](Lobby.md) (lobby netcode), [Inventory.md](Inventory.md), [Persistence.md](Persistence.md), [audio.md](audio.md)

## Model

- **Two families.** *Menu-scene pages* subclass [MenuScreen.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuScreen.cs): a static `Open()` builds a component, assigns fields, then calls `Present()`, which disables every **other** `Canvas` in the scene and builds its own — so the 3D menu set stays visible behind the words. `Close()` re-enables them; `HandOff()` doesn't (used before a scene load). *In-game overlays* are `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` singletons on a `DontDestroyOnLoad` GameObject that build their canvas lazily on first open — gameplay spans a persistent scene + streamed chunks + an additive arena, so nothing UI-shaped is authored into a scene.
- **One owner of cursor/input/time:** [GameplayMenuScope.cs](Assets/Game/Scripts/Presentation/UI/Widgets/GameplayMenuScope.cs). Reference-counted (`Enter(owner)` / `Exit(owner)`), because two screens can be open at once. First one in calls `PlayerController.EnterCutsceneMode(hideHud)` (input+look+movement off, camera still renders), disables `SpectatorCamera`, and frees the cursor; last one out restores. `Enter` returns **false** when there is no local player — that is how a gameplay overlay refuses to open over the main menu.
- **Time freezes only in a solo session** and only if some current owner asked for it (`freezers` set, separate from `owners`): chat enters with `freezeTime: false`, backpack focus with `hideHud: false` too. All open/close animations run on `Time.unscaledDeltaTime`.
- **Gameplay hotkeys gate on `GameplayMenuScope.AcceptsGameplayInput`** — local player exists AND its `PlayerInput` is enabled. This is the one shared check ([Flashlight.cs](Assets/Game/Scripts/Characters/Player/Equipment/Flashlight.cs), [HelmetOverlayVisibility.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetOverlayVisibility.cs), [MapHologramTerrain.cs](Assets/Game/Scripts/Presentation/UI/Map/MapHologramTerrain.cs), [SeatedRider.cs](Assets/Game/Scripts/Gameplay/Arrival/Runtime/SeatedRider.cs)). The keys that *open* menus cannot use it — the pause menu and chat each own a private `InputControls` with only the `UI` map live, because `PlayerInputManager` disables the player's whole asset while a menu holds the scope.
- **`GameplayMenuScope.FindLocalPlayer()` is the project's only answer to "which player is this peer driving"** — never a `"Player"` tag search (every player carries that tag). `FindLocalPlayer(this)` walks the parent chain instead, for per-player HUD components; it resolves during `OnNetworkSpawn`, where the session-wide lookup still returns null. Hits are cached, misses never are.
- **HUD data sources are events, not polling:** `HealthComponent` events, `HealthComponent.AnyDamaged` + `NetMsg.Damaged`, `PlayerIdentity.All`, `ChatLog.Added`, `Interactor` + `InteractionPromptResolver`, `IPlayerInventory`, `GameSettings.Changed`, `MatchManager`, `EntityTargetRegistry`/`MapService`.
- **Sorting-order ladder:** WorldOverlay `-1` · PlayerHUD `0` · world prompts `50` · MenuScreen/MinigameConfig `900` · MatchResult `1000` · MatchLeaderboard `1100` · Chat `1500` · PauseMenu `2000` · Trade `2050` · DevInventory `2100` · LoadingScreen `5000`.
- **Nothing imports art.** [UITheme.cs](Assets/Game/Scripts/Presentation/UI/Theme/UITheme.cs) draws rounded rects/discs/chevrons into textures at runtime and 9-slices them; [HotbarStyle.cs](Assets/Game/Scripts/Presentation/UI/HUD/HotbarStyle.cs) does the same in the world's warm palette. Two design languages: **menu navy over the live 3D set** ([MenuEntry.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuEntry.cs)) and **near-black panel + blue accent** (`UITheme`, for screens over gameplay).

## Key types

| Type | File | Role |
| --- | --- | --- |
| `GameplayMenuScope` | [Widgets/GameplayMenuScope.cs](Assets/Game/Scripts/Presentation/UI/Widgets/GameplayMenuScope.cs) | Ref-counted cursor/input/timescale handover; `AcceptsGameplayInput`; local-player resolver. |
| `MenuScreen` | [Widgets/MenuScreen.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuScreen.cs) | Base for menu-scene pages: canvas swap + shared skeleton (`Title`/`Column`/`Entry`/`PinnedRow`). |
| `MenuEntry` | [Widgets/MenuEntry.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuEntry.cs) | Menu palette, type scale, column/horizon layout constants; clones the menu button prefab (falls back to a plain built button). |
| `MenuLock` | [Widgets/MenuLock.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuLock.cs) | The only sanctioned way to disable a menu control: `CanvasGroup` alpha + `interactable`, never `Button.interactable` alone. |
| `UIBuilder` | [Widgets/UIBuilder.cs](Assets/Game/Scripts/Presentation/UI/Widgets/UIBuilder.cs) | uGUI primitives (`Rect`, `Fill`, `Label`, `Clickable`, `HitArea`, `Column`, `PinnedTop/Bottom`, `EnsureEventSystem`). |
| `UITheme` | [Theme/UITheme.cs](Assets/Game/Scripts/Presentation/UI/Theme/UITheme.cs) | Panel-screen palette, type scale, runtime-generated sprites cached per radius. |
| `HotbarStyle` | [HUD/HotbarStyle.cs](Assets/Game/Scripts/Presentation/UI/HUD/HotbarStyle.cs) | The HUD's warm palette, lifted from the model library's `PALETTE.md`. |
| `SettingsWidgets` | [Widgets/SettingsWidgets.cs](Assets/Game/Scripts/Presentation/UI/Widgets/SettingsWidgets.cs) | Slider / switch / cycler / text row builders; each returns a `Row` with `Refresh()` that re-reads its source. |
| `MenuStatusLine` | [Widgets/MenuStatusLine.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuStatusLine.cs) | The bottom line of a menu page. `Say` transient, `Warn` sticky, `Polled` refused while a warning stands. |
| `MenuBusy` | [Widgets/MenuBusy.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuBusy.cs) | In-flight feedback: a sweeping rule, or animated trailing dots. No glyph spinners (font has none). |
| `MenuField` / `MenuFieldRule` | [Widgets/MenuField.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuField.cs), [MenuFieldRule.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuFieldRule.cs) | Text typed on an underline (the menu draws no boxes); the rule carries idle/hover/focus state. |
| `MenuStepper` | [Widgets/MenuStepper.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuStepper.cs) | `− 3 +` row. **Reports, never decides** — shows only what `SetValue` last told it, so a caller can refuse. |
| `HoverTint` | [Widgets/HoverTint.cs](Assets/Game/Scripts/Presentation/UI/Widgets/HoverTint.cs) | Tints a *visible* graphic on hover when the Button's own target is an invisible full-rect hit area. |
| `UIButton` | [Buttons/UIButton.cs](Assets/Game/Scripts/Presentation/UI/Buttons/UIButton.cs) | Scene-authored button: drives the `State` int on `Menu Button.controller` and plays `SfxId.UiHover`/`UiPress` via `Sfx.Play2D`. |
| `WorldOverlay` | [World/WorldOverlay.cs](Assets/Game/Scripts/Presentation/UI/World/WorldOverlay.cs) | The `DontDestroyOnLoad` screen-space layer world-anchored labels project onto (survives chunk streaming). |
| `GameSettings` | [Core/Settings/GameSettings.cs](Assets/Game/Scripts/Core/Settings/GameSettings.cs) | Static PlayerPrefs-backed store + `Changed` event; lazily loaded, `SchemaVersion` re-seeds defaults. |

## Pages & widgets

| Screen/Widget | File | Purpose |
| --- | --- | --- |
| `MainMenuUI` | [Pages/MainMenuUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/MainMenuUI.cs) | Front menu in `MainMenu.unity`; owns `gameScene`, `worldConfig` and the menu button prefab lent to every page it opens. Methods bound **by name** from the scene. |
| `MenuChoiceUI` | [Pages/MenuChoiceUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/MenuChoiceUI.cs) | One question, 2–3 answers + Back (story/VS, host/join). |
| `WorldSelectUI` | [Pages/WorldSelectUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/WorldSelectUI.cs) | The only place a world is chosen: list / name-new / confirm-delete, for both SP and lobby destinations. |
| `VersusRulesUI` | [Pages/VersusRulesUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/VersusRulesUI.cs) | Teams and team size before a VS lobby; stages into statics the lobby reads. |
| `MinigameConfigUI` | [Pages/MinigameConfigUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/MinigameConfigUI.cs) | Pre-match gamemode/bots/win condition → `MatchSettings`. Predates `MenuScreen`, keeps its own canvas swap. |
| `LobbyUI` | [Lobby/LobbyUI.cs](Assets/Game/Scripts/Presentation/UI/Lobby/LobbyUI.cs) | Lobby screen: swaps Join ↔ Roster pages, owns host/join/start/leave. Netcode in [Lobby.md](Lobby.md). |
| `LobbyRoute` | [Lobby/LobbyRoute.cs](Assets/Game/Scripts/Presentation/UI/Lobby/LobbyRoute.cs) | Which door was taken (story/VS × host/join); carried explicitly, never inferred. |
| Lobby **Join** | [Lobby/Join/](Assets/Game/Scripts/Presentation/UI/Lobby/Join) | `LobbyJoinPage` widgets · `LobbyJoinFlow` actions · `LobbyJoinLayout` geometry · `LobbyBrowser`+`LobbyBrowserRow` reconciled session list · `LobbyAutoRefresh` 1 s budget · `LobbyBusyScope`/`LobbyBusyState` per-region lock table. |
| Lobby **Rank** | [Lobby/Rank/](Assets/Game/Scripts/Presentation/UI/Lobby/Rank) | `LobbyPreviewRank` astronauts standing in the real menu scene · `LobbyRankFigures` one figure per slot (hidden, never destroyed) · `LobbyNameplates` · `LobbyTeamPlates` clickable team headers · `LobbySuitCycler` · `LobbyPreviewCamera` borrows the menu camera · `LobbyOverlayLayer` the screen rect they project onto · `SlotLists` parallel per-slot lists. |
| Lobby **Roster** | [Lobby/Roster/](Assets/Game/Scripts/Presentation/UI/Lobby/Roster) | `LobbyRosterView` live page (polls 2×/s) · `LobbyRosterFlow` actions · `LobbySessionStrip` code/copy/privacy · `LobbyTeamRulesStrip` host steppers. |
| `PauseMenuUI` | [Pages/PauseMenuUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/PauseMenuUI.cs) | In-game pause on **M** (not Escape): Audio/Video/Controls/Players/Dev tabs + Resume / Main Menu / Quit, each destructive footer button arm-then-confirm. |
| `ChatUI` | [Pages/ChatUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/ChatUI.cs) | Fading log bottom-left + input box on **T**; enters the scope without freezing time. Renders through `<noparse>` on top of `ChatText.Sanitize`. |
| `DevInventoryUI` | [Pages/DevInventoryUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/DevInventoryUI.cs) | Artifact browser on **I** while `GameSettings.DevMode`; hotbar edits go through `IPlayerInventory`, so they replicate. |
| `TradeUI` | [Pages/TradeUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/TradeUI.cs) | Trader offers vs. your bag, one click per swap; opened by `TraderInteraction`. |
| `LoadingScreenUI` | [Pages/LoadingScreenUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/LoadingScreenUI.cs) | Covers scene load **and** streamer readiness/NavMesh/warmup; carries a fallback camera; logs (never lifts) after a 30 s stall. |
| `MatchResultUI` / `MatchLeaderboardUI` | [Pages/MatchResultUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/MatchResultUI.cs), [Pages/MatchLeaderboardUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/MatchLeaderboardUI.cs) | Win/loss screen and Tab-held scoreboard; `MatchManager.Ensure()`s both on every peer. |
| `PlayerListView` | [Widgets/PlayerListView.cs](Assets/Game/Scripts/Presentation/UI/Widgets/PlayerListView.cs) | Pause menu's Players tab: name, you/host tags, RTT; rows pooled, ping refreshed once a second. |
| `HealthUI` | [HUD/HealthUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/HealthUI.cs) | Bar + numbers from a serialized `HealthComponent`'s damage/heal/death/revive events. |
| `CrosshairUI` | [HUD/CrosshairUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/CrosshairUI.cs) | Crosshair + aim hint. **Its hover half has never run** — `playerInteractor` is unassigned on the prefab and `Update` returns on line 1. |
| `InteractionPromptUI` | [HUD/InteractionPromptUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/InteractionPromptUI.cs) | "What am I looking at, what will the buttons do", from `InteractionPromptResolver`; finds the `Interactor` when unwired. |
| `SeatPromptUI` | [HUD/SeatPromptUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/SeatPromptUI.cs) | Timed "ESCAPE to leave the seat" after the crash landing. Draws only; `SeatedRider` decides. |
| `DeathScreenUI` | [HUD/DeathScreenUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/DeathScreenUI.cs) | Death overlay; binds in `OnEnable` and reads current `IsDead`, not just the event. |
| `InventoryUI` / `InventorySlotUI` | [HUD/InventoryUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/InventoryUI.cs), [HUD/InventorySlotUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/InventorySlotUI.cs) | Four-slot hotbar, built in code (`Slot.prefab` is dead). Clicks are handed to `PackHandController`; the bar never draws a held-item stand-in. |
| `HelmetHUDController` | [HelmetHUD/HelmetHUDController.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetHUDController.cs) | Spawns and feeds the visor overlay; resolves health off the player it hangs under. |
| `HelmetNavMarkers` / `HelmetMarkerFactory` | [HelmetHUD/HelmetNavMarkers.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetNavMarkers.cs), [HelmetMarkerFactory.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetMarkerFactory.cs) | AR markers from `EntityTargetRegistry` (faction-coloured) + `MapService` POIs; on-screen ring or edge-clamped arrow. Factory builds each marker's hierarchy. |
| `HelmetDangerVignette` | [HelmetHUD/HelmetDangerVignette.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetDangerVignette.cs) | Two curved arcs that grow per hit and decay; driven only by `HitSide`/`HitBoth`. |
| `HelmetOverlayVisibility` | [HelmetHUD/HelmetOverlayVisibility.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetOverlayVisibility.cs) | **H** toggles the helmet layer only; lives on the canvas root because it deactivates its own target. |
| `DamageNumbers` | [World/DamageNumbers.cs](Assets/Game/Scripts/Presentation/UI/World/DamageNumbers.cs) | Floating `-25` for the local player's own hits, pooled and stacked; needs **both** sources (below). |
| `PlayerNameplates` | [World/PlayerNameplates.cs](Assets/Game/Scripts/Presentation/UI/World/PlayerNameplates.cs) | Names over other players from `PlayerIdentity.All`; distance fade + occlusion ray with a near clearance. |
| `RepairProgressUI` | [World/RepairProgressUI.cs](Assets/Game/Scripts/Presentation/UI/World/RepairProgressUI.cs) | World-space gauge reading `RepairWorkstation`'s replicated progress. |
| `NpcDialogPopupUI` | [Dialog/NpcDialogPopupUI.cs](Assets/Game/Scripts/Presentation/UI/Dialog/NpcDialogPopupUI.cs) | Scene-authored singleton speech popup: typewriter, hold, optional yes/no choice. |
| `MapHologramTerrain` | [Map/MapHologramTerrain.cs](Assets/Game/Scripts/Presentation/UI/Map/MapHologramTerrain.cs) | The 3D map: one mesh per revealed chunk from `Resources/MapMeshes/`, floating beside the player. The only map piece actually placed in a scene. |
| `MapService` / `MapPOI` / `MapMarkerType` | [Map/](Assets/Game/Scripts/Presentation/UI/Map) | Marker registry + revealed-chunk set; `MapPOI` self-registers a persistent static marker. `MapService` is read by the helmet markers; there is no 2D map screen. |

## Flows

**Opening a menu-scene page** (main menu → world select): 1. scene button calls a `MainMenuUI` method **by name**; 2. the page's static `Open()` guards "already open", `AddComponent`s itself, assigns its own fields; 3. calls `Present(menu)` — `EnsureEventSystem`, free cursor, disable all other canvases, create a 1920×1080 overlay canvas, `Build()`.
**Opening a gameplay overlay** (pause menu): 1. `inputs.UI.Pause` fires from the screen's own `InputControls`; 2. `Toggle()` bails if a `TMP_InputField` is focused, and peels `DevInventoryUI` off first if it is on top; 3. `GameplayMenuScope.Enter(this)` — false (no local player) means no world to pause, so nothing opens; 4. build the canvas on first open, then ease `visibility` on unscaled time.
**Closing:** `Close()` → `GameplayMenuScope.Exit(this)`. Last owner out thaws time, `ExitCutsceneMode()`, re-enables the spectator camera, and re-locks the cursor **unless the player is dead**. `Abandon()` (scene load, disconnect) drops all claims, thaws, and forgets the cached local player.
**HUD update:** `PlayerController.EnablePlayer()` (`OnNetworkSpawn`) activates `PlayerHUD.prefab` for the owner only → each component binds in `OnEnable` and resolves its player via `FindLocalPlayer(this)` → subsequent redraws are event-driven; `WorldOverlay` projects world points every `LateUpdate` through `Camera.main`, re-read each frame so it follows the player onto a mount.

## Multiplayer

- **Nameplates** read `PlayerIdentity.All` — already replicated, nothing new on the wire; `PlayerIdentity` supplies a `Player N` stand-in until a name arrives.
- **Damage numbers** need two sources: `HealthComponent.AnyDamaged` (hits this machine resolved) **and** `NetMsg.Damaged` (hits the server resolved for a client, since `Weapon.Use()` runs on the authority only).
- **`PlayerListView`** reads the NGO roster plus per-connection RTT; **`MatchResultUI`/`MatchLeaderboardUI`** are `Ensure()`d by `MatchManager` on every peer because the arena scene carries no UI.
- **Lobby roster/rank** render `LobbySession` state (see [Lobby.md](Lobby.md)); the UI never calls the Lobby service directly.
- **`GameplayMenuScope` never freezes time in a session with other players** — `IsSoloSession()` gates it. Chat, trade and the pause menu therefore all keep the world running for everyone else.

## Persistence

- [GameSettings.cs](Assets/Game/Scripts/Core/Settings/GameSettings.cs) — PlayerPrefs under `SpaceGame.Settings.`: player name, suit colour index, five volume buses, sensitivity, camera shake, invert look/hotbar scroll, dev mode, FOV, quality, fullscreen, resolution, frame cap. `SchemaVersion` re-seeds when a default changes. Consumers subscribe to `Changed`.
- Nothing else in UI persists. Open pages, chat log, HUD toggles and the map's revealed set are session-only (the chat log is cleared by `ChatNetwork.OnDestroy`, deliberately *not* by a scene change, so walking into an interior does not empty it).

## Gotchas

- **Never tint a label to show selection.** `Menu Button.controller` drives the label's `m_fontColor` and the root scale on every state change, so a tint survives one frame. Say it on a different object (`LobbySuitCycler` puts the swatch name in its own object; `InventorySlotUI` lifts and rings the selected slot instead of brightening it).
- **Never use `Button.interactable` alone to disable a menu entry.** That controller's `Disabled` clip is **empty**, so the row freezes in whatever colour/scale it was in and, with raycasts off, never gets the pointer-exit. Use [MenuLock](Assets/Game/Scripts/Presentation/UI/Widgets/MenuLock.cs). `CanvasGroup` alphas multiply, so "dim all, keep one lit" needs per-control groups.
- **`Present()`, not `Awake()`.** `AddComponent` runs `Awake` before the caller's next statement, so a `MenuScreen` that built itself there would build before its arguments were assigned. `WorldOverlay.Create()` calls `Build()` explicitly for the mirror-image reason: `AddComponent` raises no `Awake` outside play mode.
- **Bind in `OnEnable`, and read current state — not only the event.** A player HUD is deactivated by `PlayerController.Awake` and only returns in `EnablePlayer`; a save-restored death is announced inside that window, so a `Start`-based subscriber misses the only announcement there ever was.
- **`FindLocalPlayer()` returns null legitimately** for a frame or more: NGO publishes the local player object *after* `OnNetworkSpawn`. Never cache a miss. Never tag-search for `"Player"` — every player carries the tag.
- **The key that opens a menu cannot live on the player's `InputControls`** — the scope disables that asset. Give the screen its own instance with only the `UI` map enabled, and `Dispose()` it in `OnDestroy`.
- **`OnDestroy` must `GameplayMenuScope.Exit(this)`**, or a screen destroyed while paused leaves the game frozen with nothing able to thaw it.
- **`UIBuilder.EnsureEventSystem()` before any clickable overlay in a gameplay scene**, or the panel renders and nothing responds.
- **Never put a `LayoutElement` and a `LayoutGroup` on the same rect** — equal layout priority, the row silently inflates. Only the outer column is a layout group; row contents are anchored.
- **A 9-sliced sprite whose border exceeds the rect draws its corners over each other** — `UITheme.Rounded(radius)` is cached per radius for this reason; a pill wants half its own height.
- **Menu colour rules:** entries are dark navy and only read against ground, so everything clickable belongs below `MenuEntry.Horizon` (540). `Horizon` is conservative and is *not* "above this is sky" — anything drawn higher must carry its own contrast (white, or the nameplates' white-over-navy shadow trick).
- **No glyph spinners**: `◀`/`▶`/braille/box-drawing are absent from LiberationSans and render as nothing. Use `MenuBusy`.
- **`MenuStepper` and `MenuStatusLine` invert the obvious:** a stepper does not move until a caller calls `SetValue`, and a `Polled` status write is refused while a `Warn` stands (a 2 Hz redraw would otherwise erase the failure before it could be read).
- **`CrosshairUI`'s hover dimming is dead code path, deliberately** — do not "fix" it by wiring `playerInteractor` without owning the look change.

## Extending

**A new menu-scene page**
1. Subclass [MenuScreen](Assets/Game/Scripts/Presentation/UI/Widgets/MenuScreen.cs); add a static `Open(MainMenuUI menu, …)` that guards `FindFirstObjectByType` for "already open", `AddComponent`s onto a new GameObject, assigns fields, then calls `Present(menu)` **last**.
2. Implement `Build()` using only `Title()`, `Column()`, `Entry()`, `PinnedRow()` — no private copies of the anchors or the palette. Keep clickable rows below `MenuEntry.Horizon`.
3. Route Back to `Close()`, and anything that loads a scene to `HandOff()`.
4. Lock in-flight controls with `MenuLock`; report through a `MenuStatusLine` and `MenuBusy`. Never tint a label to mark state.

**A new HUD widget**
1. Decide the family: per-player readout → a component under `PlayerHUD.prefab`, resolving its player with `GameplayMenuScope.FindLocalPlayer(this)`; world-anchored label → parent into `WorldOverlay.Instance.Layer`; full-screen overlay → the `RuntimeInitializeOnLoadMethod` + `DontDestroyOnLoad` + lazy-build singleton pattern.
2. Bind in `OnEnable`, unbind in `OnDisable`, and render current state immediately rather than waiting for the next event.
3. Draw with `UIBuilder` + `HotbarStyle` (in-world) or `UITheme` (panels). No PNGs, no new prefab of nested Images.
4. If it opens over gameplay: `GameplayMenuScope.Enter(this[, freezeTime][, hideHud])`, `Exit` on close **and** in `OnDestroy`, `UIBuilder.EnsureEventSystem()`, pick a sorting order from the ladder, and animate on unscaled time.
5. If it reacts to a hotkey while the player is standing in the world, gate it on `GameplayMenuScope.AcceptsGameplayInput`.
6. Multiplayer: if it shows another player's state, source it from something already replicated (`PlayerIdentity`, `NetMsg.*`) — never from the acting machine's local call. Persistence: only `GameSettings` values survive a quit; say so explicitly if the widget holds none.
