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
  - "a menu choice drops me back on the main menu instead of opening the page it names"
  - "the world list only shows two saves before it has to be scrolled"
  - "a menu list is far shorter on an ultrawide monitor than on a 16:9 one"
  - "the UI is a different size on different screens, or the versus lobby's names are too small on an ultrawide"
  - "text or a panel is tiny on a 4K monitor and normal at 1080p"
  - "damage numbers and nameplates never appear for anyone, no errors — their Canvas is disabled"
  - "the map hologram in the ship shows the world with me off in a corner of it"
reads_with: [Lobby, Inventory, Persistence, audio]
updated: 2026-09-05
---

# UI

Every screen in the game — the main-menu page stack, the in-game HUD, the full-screen overlays that open over gameplay, and the world-anchored labels — all built in C# at runtime, no UI art assets.

**Scope:** [Assets/Game/Scripts/Presentation/UI/](Assets/Game/Scripts/Presentation/UI) (71 files) + [Menu Button.controller](Assets/Game/Art/Animations/UI/Buttons/Menu%20Button.controller) + [GameSettings.cs](Assets/Game/Scripts/Core/Settings/GameSettings.cs)
**Related:** [Lobby.md](Lobby.md) (lobby netcode), [Inventory.md](Inventory.md), [Persistence.md](Persistence.md), [audio.md](audio.md)

## Model

- **Two families.** *Menu-scene pages* subclass [MenuScreen.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuScreen.cs): a static `Open()` builds a component, assigns fields, then calls `Present()`, which disables every **other** `Canvas` in the screen's own scene — never one from another scene — and builds its own, so the 3D menu set stays visible behind the words. `Close()` re-enables them; `HandOff()` doesn't (used before a scene load, safe only because everything hidden dies with that load). Both mark the page closed and switch it off before `Destroy`, because Destroy is a frame away and a page opened in the meantime would otherwise find and draw over the corpse — which is why `Open` asks `MenuScreen.Existing<T>()` for "already open", never `FindFirstObjectByType` directly. *In-game overlays* are `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` singletons on a `DontDestroyOnLoad` GameObject that build their canvas lazily on first open — gameplay spans a persistent scene + streamed chunks + an additive arena, so nothing UI-shaped is authored into a scene.
- **One owner of cursor/input/time:** [GameplayMenuScope.cs](Assets/Game/Scripts/Presentation/UI/Widgets/GameplayMenuScope.cs). Reference-counted (`Enter(owner)` / `Exit(owner)`), because two screens can be open at once. First one in calls `PlayerController.EnterCutsceneMode(hideHud)` (input+look+movement off, camera still renders), disables `SpectatorCamera`, and frees the cursor; last one out restores. `Enter` returns **false** when there is no local player — that is how a gameplay overlay refuses to open over the main menu.
- **Time freezes only in a solo session** and only if some current owner asked for it (`freezers` set, separate from `owners`): chat enters with `freezeTime: false`, backpack focus with `hideHud: false` too. All open/close animations run on `Time.unscaledDeltaTime`.
- **Gameplay hotkeys gate on `GameplayMenuScope.AcceptsGameplayInput`** — local player exists AND its `PlayerInput` is enabled. This is the one shared check ([Flashlight.cs](Assets/Game/Scripts/Characters/Player/Equipment/Flashlight.cs), [HelmetOverlayVisibility.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetOverlayVisibility.cs), [MapHologramTerrain.cs](Assets/Game/Scripts/Presentation/UI/Map/MapHologramTerrain.cs), [SeatedRider.cs](Assets/Game/Scripts/Gameplay/Arrival/Runtime/SeatedRider.cs)). The keys that *open* menus cannot use it — the pause menu and chat each own a private `InputControls` with only the `UI` map live, because `PlayerInputManager` disables the player's whole asset while a menu holds the scope.
- **`GameplayMenuScope.FindLocalPlayer()` is the project's only answer to "which player is this peer driving"** — never a `"Player"` tag search (every player carries that tag). `FindLocalPlayer(this)` walks the parent chain instead, for per-player HUD components; it resolves during `OnNetworkSpawn`, where the session-wide lookup still returns null. Hits are cached, misses never are.
- **HUD data sources are events, not polling:** `HealthComponent` events, `HealthComponent.AnyDamaged` + `NetMsg.Damaged`, `PlayerIdentity.All`, `ChatLog.Added`, `Interactor` + `InteractionPromptResolver`, `IPlayerInventory`, `GameSettings.Changed`, `MatchManager`, `EntityTargetRegistry`/`MapService`.
- **Sorting-order ladder:** WorldOverlay `-1` · PlayerHUD `0` · world prompts `50` · MenuScreen/MinigameConfig `900` · MatchResult `1000` · MatchLeaderboard `1100` · Chat `1500` · PauseMenu `2000` · Trade `2050` · BodyInventory `2060` · DevInventory `2100` · LoadingScreen `5000`.
- **Nothing imports art.** [UITheme.cs](Assets/Game/Scripts/Presentation/UI/Theme/UITheme.cs) draws rounded rects/discs/chevrons into textures at runtime and 9-slices them; [HotbarStyle.cs](Assets/Game/Scripts/Presentation/UI/HUD/HotbarStyle.cs) does the same for item tiles, in the visor's palette. Three design languages: **menu navy over the live 3D set** ([MenuEntry.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuEntry.cs)), **near-black panel + blue accent** (`UITheme`, for screens over gameplay), and **the visor** (`VisorStyle` — light projected on helmet glass; blue is the language, warm is the alarm, see [Visor.md](Visor.md)).

## Key types

| Type | File | Role |
| --- | --- | --- |
| `GameplayMenuScope` | [Widgets/GameplayMenuScope.cs](Assets/Game/Scripts/Presentation/UI/Widgets/GameplayMenuScope.cs) | Ref-counted cursor/input/timescale handover; `AcceptsGameplayInput`; local-player resolver. |
| `MenuScreen` | [Widgets/MenuScreen.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuScreen.cs) | Base for menu-scene pages: canvas swap + shared skeleton (`Title`/`Column`/`Entry`/`PinnedRow`). |
| `MenuEntry` | [Widgets/MenuEntry.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuEntry.cs) | Menu palette, type scale, column/horizon layout constants; clones the menu button prefab (falls back to a plain built button). |
| `MenuLock` | [Widgets/MenuLock.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuLock.cs) | The only sanctioned way to disable a menu control: `CanvasGroup` alpha + `interactable`, never `Button.interactable` alone. |
| `UIScale` | [Widgets/UIScale.cs](Assets/Game/Scripts/Presentation/UI/Widgets/UIScale.cs) | The project's one canvas-scaling rule: 1920x1080, `Expand`. Also answers canvas size / scale factor as pure functions, so geometry can be reasoned about before a canvas exists. |
| `UIBuilder` | [Widgets/UIBuilder.cs](Assets/Game/Scripts/Presentation/UI/Widgets/UIBuilder.cs) | uGUI primitives (`Rect`, `Fill`, `Label`, `Clickable`, `HitArea`, `Column`, `PinnedTop/Bottom`, `EnsureEventSystem`). |
| `UITheme` | [Theme/UITheme.cs](Assets/Game/Scripts/Presentation/UI/Theme/UITheme.cs) | Panel-screen palette, type scale, runtime-generated sprites cached per radius. |
| `HotbarStyle` | [HUD/HotbarStyle.cs](Assets/Game/Scripts/Presentation/UI/HUD/HotbarStyle.cs) | Item-tile geometry and the refusal shake. Its **colours now delegate to `VisorStyle`** — the warm expedition palette is gone. |
| `VisorStyle` | [Theme/VisorStyle.cs](Assets/Game/Scripts/Presentation/UI/Theme/VisorStyle.cs) | The helmet visor's palette, type ramp, motion constants and generated sprites. [Visor.md](Visor.md). |
| `VisorGauge` | [HelmetHUD/VisorGauge.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorGauge.cs) | One visor readout bound to an `IVisorGaugeSource`; alarm states change shape and word, not only colour. |
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
| `WorldSelectUI` | [Pages/WorldSelectUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/WorldSelectUI.cs) | The only place a world is chosen: list / name-new / confirm-delete, for both SP and lobby destinations. The list is stretched across the whole content band (`MenuEntry.ContentTop` → the status line); **New world**, **Delete** and **Start** are all footer actions. |
| `VersusRulesUI` | [Pages/VersusRulesUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/VersusRulesUI.cs) | Teams and team size before a VS lobby; stages into statics the lobby reads. |
| `MinigameConfigUI` | [Pages/MinigameConfigUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/MinigameConfigUI.cs) | Pre-match gamemode/bots/win condition → `MatchSettings`. Predates `MenuScreen`, keeps its own canvas swap. |
| `LobbyUI` | [Lobby/LobbyUI.cs](Assets/Game/Scripts/Presentation/UI/Lobby/LobbyUI.cs) | Lobby screen: swaps Join ↔ Roster pages, owns host/join/start/leave. Netcode in [Lobby.md](Lobby.md). |
| `LobbyRoute` | [Lobby/LobbyRoute.cs](Assets/Game/Scripts/Presentation/UI/Lobby/LobbyRoute.cs) | Which door was taken (story/VS × host/join); carried explicitly, never inferred. |
| Lobby **Join** | [Lobby/Join/](Assets/Game/Scripts/Presentation/UI/Lobby/Join) | `LobbyJoinPage` widgets · `LobbyJoinFlow` actions · `LobbyJoinLayout` geometry · `LobbyBrowser`+`LobbyBrowserRow` reconciled session list · `LobbyAutoRefresh` 1 s budget · `LobbyBusyScope`/`LobbyBusyState` per-region lock table. |
| Lobby **Rank** | [Lobby/Rank/](Assets/Game/Scripts/Presentation/UI/Lobby/Rank) | `LobbyPreviewRank` astronauts standing in the real menu scene · `LobbyRankFigures` one figure per slot (hidden, never destroyed) · `LobbyNameplates` · `LobbyTeamPlates` clickable team headers · `LobbySuitCycler` · `LobbyPreviewCamera` borrows the menu camera · `LobbyOverlayLayer` the screen rect they project onto · `SlotLists` parallel per-slot lists. |
| Lobby **Roster** | [Lobby/Roster/](Assets/Game/Scripts/Presentation/UI/Lobby/Roster) | `LobbyRosterView` live page (polls 2×/s) · `LobbyRosterFlow` actions · `LobbySessionStrip` code/copy/privacy · `LobbyTeamRulesStrip` host steppers. |
| `PauseMenuUI` | [Pages/PauseMenuUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/PauseMenuUI.cs) | In-game pause on **M** (not Escape): Audio/Video/Controls/Players/Dev tabs + Resume / Main Menu / Quit, each destructive footer button arm-then-confirm. |
| `ChatUI` | [Pages/ChatUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/ChatUI.cs) | Fading log bottom-left + input box on **T**; enters the scope without freezing time. Renders through `<noparse>` on top of `ChatText.Sanitize`. |
| `DevInventoryUI` | [Pages/DevInventoryUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/DevInventoryUI.cs) | Artifact browser on **O** while `GameSettings.DevMode`; hotbar edits go through `IPlayerInventory`, so they replicate. |
| `TradeUI` | [Pages/TradeUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/TradeUI.cs) | Trader offers vs. your bag, one click per swap; opened by `TraderInteraction`. |
| `LoadingScreenUI` | [Pages/LoadingScreenUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/LoadingScreenUI.cs) | Covers scene load **and** streamer readiness/NavMesh/warmup; carries a fallback camera; logs (never lifts) after a 30 s stall. |
| `MatchResultUI` / `MatchLeaderboardUI` | [Pages/MatchResultUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/MatchResultUI.cs), [Pages/MatchLeaderboardUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/MatchLeaderboardUI.cs) | Win/loss screen and Tab-held scoreboard; `MatchManager.Ensure()`s both on every peer. |
| `PlayerListView` | [Widgets/PlayerListView.cs](Assets/Game/Scripts/Presentation/UI/Widgets/PlayerListView.cs) | Pause menu's Players tab: name, you/host tags, RTT; rows pooled, ping refreshed once a second. |
| `CrosshairUI` | [HUD/CrosshairUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/CrosshairUI.cs) | Crosshair + aim hint. **Its hover half has never run** — `playerInteractor` is unassigned on the prefab and `Update` returns on line 1. |
| `PlayerHints` | [HUD/PlayerHints.cs](Assets/Game/Scripts/Presentation/UI/HUD/PlayerHints.cs) | **Now a static adapter over `SystemMessages`**, not a canvas of its own — same `Show(id, text[, seconds])` / `Hide(id)` API, so every caller is unchanged. Hints post at `Notice` and are drawn by `VisorMessageStack`. See [Visor.md](Visor.md). |
| `SeatPromptUI` | [HUD/SeatPromptUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/SeatPromptUI.cs) | WHEN the arrival's "Q — exit the ship" hint shows: 3 s after the cutscene ends, 10 s backstop from the seat becoming leavable. **Polled** (`SeatedRider.LocalPlayerMayLeave` + `CutsceneDirector.IsPlaying`), never event-driven — it lives on a HUD that is disabled at exactly the moments the arrival announces things, so an event subscriber missed them and the hint never showed. Draws via `PlayerHints`; `SeatedRider` decides whether the key does anything. |
| `DeathScreenUI` | [HUD/DeathScreenUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/DeathScreenUI.cs) | Death overlay; binds in `OnEnable` and reads current `IsDead`, not just the event. |
| `InventoryUI` / `InventorySlotUI` | [HUD/InventoryUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/InventoryUI.cs), [HUD/InventorySlotUI.cs](Assets/Game/Scripts/Presentation/UI/HUD/InventorySlotUI.cs) | Four-slot hotbar, built in code (`Slot.prefab` is dead). Clicks are handed to `PackHandController`; the bar never draws a held-item stand-in. |
| `HelmetHUDController` | [HelmetHUD/HelmetHUDController.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetHUDController.cs) | The visor root. Builds the `Vitals` / `Annotations` sublayers and spawns the modules; the player's health gauge lives here now. See [Visor.md](Visor.md). |
| `VisorReticle` | [HelmetHUD/VisorReticle.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorReticle.cs) | Corner marks around whatever the `Interactor` is hovering, plus a look-at info box beside them from `InteractionPromptResolver` — "What am I looking at, what will the buttons do". Absorbed `InteractionPromptUI`; the crosshair third of the design's `VisorReticle` is not built yet and still lives on `CrosshairUI`. |
| `HelmetDangerVignette` | [HelmetHUD/HelmetDangerVignette.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetDangerVignette.cs) | Two curved arcs that grow per hit and decay; driven only by `HitSide`/`HitBoth`. |
| `HelmetOverlayVisibility` | [HelmetHUD/HelmetOverlayVisibility.cs](Assets/Game/Scripts/Presentation/UI/HelmetHUD/HelmetOverlayVisibility.cs) | **H** cycles Full → Vitals only → Off. Three states because health is on the visor now and a plain toggle could hide it. Lives on the canvas root because it deactivates its own target. |
| `DamageNumbers` | [World/DamageNumbers.cs](Assets/Game/Scripts/Presentation/UI/World/DamageNumbers.cs) | Floating `-25` for the local player's own hits, pooled and stacked; needs **both** sources (below). |
| `PlayerNameplates` | [World/PlayerNameplates.cs](Assets/Game/Scripts/Presentation/UI/World/PlayerNameplates.cs) | Names over other players from `PlayerIdentity.All`; distance fade + occlusion ray with a near clearance. |
| `NpcDialogPopupUI` | [Dialog/NpcDialogPopupUI.cs](Assets/Game/Scripts/Presentation/UI/Dialog/NpcDialogPopupUI.cs) | Scene-authored singleton speech popup: typewriter, hold, optional yes/no choice. |
| `MapHologramTerrain` | [Map/MapHologramTerrain.cs](Assets/Game/Scripts/Presentation/UI/Map/MapHologramTerrain.cs) | The 3D map: one mesh per revealed chunk from `Resources/MapMeshes/`. Two modes: `projectorAnchor` null = the personal map, camera-anchored, toggled by the Map key; `projectorAnchor` assigned = pinned upright over that transform (the `HoloProjector` prefab, switched by its own `HoloProjectorInteraction` — set `toggleActionName` empty there so the Map key does not also flip it). Both charts centre on the player (`centerOnPlayer`, Gotchas) and show `viewRadius` chunks either side of them, scaled to fit `footprint`. |
| `MapService` / `MapPOI` / `MapMarkerType` | [Map/](Assets/Game/Scripts/Presentation/UI/Map) | Marker registry + revealed-chunk set; `MapPOI` self-registers a persistent static marker. Nothing currently reads it — the helmet visor's nav-marker layer was removed; there is no 2D map screen. |

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

- **The map hologram is centred on the player, and getting that wrong is silent.** `centerOnPlayer` translates the terrain so the player's own position sits over the emitter, and `viewRadius` then sets the zoom — the ship's projector charts 7 chunks (3500 m) into a 0.9 m plate, near enough the whole 4000 x 3000 m world that it still reads as a world chart. It was authored the other way, a fixed world-centred chart with the player wherever the ship had crashed, which is the one question a map table exists to answer (`GDC-L1-LEVEL-0002`, `GDC-L1-UX-0003`). Two things beyond the flag decide whether it looks centred. The visible chunk window comes from `ChunkGrid.WindowAround`, not from the player's own chunk plus a radius — see [WorldStreaming](WorldStreaming.md) Gotchas. And `mapRadius` fades the terrain out in a disc around the player's world XZ: it is set past the window's half-diagonal on the projector so it does nothing, and pulling it inside that (leaving room for `mapEdgeFalloff` plus the shader's own noise fuzz, which pushes the edge out by up to a third of that falloff again) is the knob that turns the chart into a round plate with no chunk-edge raggedness.
- **Never tint a label to show selection.** `Menu Button.controller` drives the label's `m_fontColor` and the root scale on every state change, so a tint survives one frame. Say it on a different object (`LobbySuitCycler` puts the swatch name in its own object; `InventorySlotUI` lifts and rings the selected slot instead of brightening it).
- **Never use `Button.interactable` alone to disable a menu entry.** That controller's `Disabled` clip is **empty**, so the row freezes in whatever colour/scale it was in and, with raycasts off, never gets the pointer-exit. Use [MenuLock](Assets/Game/Scripts/Presentation/UI/Widgets/MenuLock.cs). `CanvasGroup` alphas multiply, so "dim all, keep one lit" needs per-control groups.
- **A closed page is still findable for the rest of the frame.** `Close()` hands the GameObject to `Destroy`, which Unity does not act on until end of frame, so `FindFirstObjectByType` keeps returning it — non-null — until then. `MenuChoiceUI.Pick` closes and routes onward in one breath, so Story ▸ Multiplayer (a `MenuChoiceUI` opened from a `MenuChoiceUI`) found the page it had just closed, took it for "already open", built nothing, and left the player on the main menu. Every `Open` guard goes through `MenuScreen.Existing<T>()`, which also asks whether the page it found is closing; [LobbyMenuWiringTests](Assets/Game/Editor/Tests/LobbyMenuWiringTests.cs) fails any page that reaches for the raw find instead.
- **`Present()`, not `Awake()`.** `AddComponent` runs `Awake` before the caller's next statement, so a `MenuScreen` that built itself there would build before its arguments were assigned. `WorldOverlay.Create()` calls `Build()` explicitly for the mirror-image reason: `AddComponent` raises no `Awake` outside play mode.
- **Bind in `OnEnable`, and read current state — not only the event.** A player HUD is deactivated by `PlayerController.Awake` and only returns in `EnablePlayer`; a save-restored death is announced inside that window, so a `Start`-based subscriber misses the only announcement there ever was.
- **`FindLocalPlayer()` returns null legitimately** for a frame or more: NGO publishes the local player object *after* `OnNetworkSpawn`. Never cache a miss. Never tag-search for `"Player"` — every player carries the tag.
- **The key that opens a menu cannot live on the player's `InputControls`** — the scope disables that asset. Give the screen its own instance with only the `UI` map enabled, and `Dispose()` it in `OnDestroy`.
- **`OnDestroy` must `GameplayMenuScope.Exit(this)`**, or a screen destroyed while paused leaves the game frozen with nothing able to thaw it.
- **`UIBuilder.EnsureEventSystem()` before any clickable overlay in a gameplay scene**, or the panel renders and nothing responds.
- **A screen must never hide a canvas from another scene.** `MenuScreen.HideOtherCanvases` (and `MinigameConfigUI`'s copy) once disabled every enabled canvas in the game; `HandOff()` then launched into gameplay without restoring, on the reasoning that the menu scene dies with the load. `WorldOverlay`, `ChatUI` and the other `DontDestroyOnLoad` surfaces don't — they entered every play session with their Canvas silently disabled, so damage numbers and nameplates never rendered while their components kept running. Hiding is now scoped to `canvas.gameObject.scene == gameObject.scene`; [MenuScreenTests](Assets/Game/Editor/Tests/MenuScreenTests.cs) pins it.
- **Never put a `LayoutElement` and a `LayoutGroup` on the same rect** — equal layout priority, the row silently inflates. Only the outer column is a layout group; row contents are anchored.
- **A 9-sliced sprite whose border exceeds the rect draws its corners over each other** — `UITheme.Rounded(radius)` is cached per radius for this reason; a pill wants half its own height.
- **[UIScale](Assets/Game/Scripts/Presentation/UI/Widgets/UIScale.cs) is the only thing that may configure a `CanvasScaler`.** Every canvas in the game — authored or built at runtime — is `ScaleWithScreenSize` at 1920x1080 with `ScreenMatchMode.Expand`, so the canvas is never smaller than the reference on either axis and an authored layout always fits. **At 16:9 and every wider aspect the canvas is exactly 1080 tall**, which is what makes an ultrawide show the same UI as a laptop; only a window narrower than 16:9 grows the canvas, and it grows it taller. The project previously held four rules at once (authored canvases matched width, thirteen runtime ones matched 0.5, three left the match at Unity's default, two had no scaler) and no single screen looked wrong — screens drawn together simply disagreed about how big a pixel was. [UIScalingTests](Assets/Game/Editor/Tests/UIScalingTests.cs) fails any file that sets the scaler properties itself.
- **A menu page's vertical budget is 308 reference pixels, and is now the same at every aspect 16:9 or wider.** Everything clickable sits between `MenuEntry.MessageBottom + 44` (the top of the status line, 212) and `MenuEntry.ContentTop` (560 down), the rest being title, sky and footer. It used to *shrink* on a wide screen — the old `matchWidthOrHeight 0.5` gave a 21:9 monitor a canvas only ~943 px tall while every offset stayed put, dropping the band to ~190 px. `Expand` removed that. A scrolling list in the band should still be **stretched between anchors, never given a height** (that is how it grows on a taller-than-16:9 canvas), and should not share the band with a pinned row — `WorldSelectUI` moved its **New world** action into the footer for that reason.
- **`MenuEntry.Horizon` and `ContentTop` are properties, not constants, and resolve against the live canvas.** The skyline is put where it is by a camera with fixed pitch and fixed vertical FOV, so it sits at a fixed *fraction* of the frame; the content offset is a fixed number of pixels from the top. Those agree at one aspect only. Both stretch together above 1080, so on the 5:4 canvas (1536 tall) content lands at 796 and the horizon at 768 rather than content at 560 and the skyline 50 px below it. Use `ContentTopFor(canvasHeight)` / `HorizonFor(canvasHeight)` in a test — reading the property there asserts against the editor's game-view size.
- **Menu colour rules:** entries are dark navy and only read against ground, so everything clickable belongs below `MenuEntry.Horizon`. `Horizon` is conservative and is *not* "above this is sky" — anything drawn higher must carry its own contrast (white, or the nameplates' white-over-navy shadow trick).
- **No glyph spinners**: `◀`/`▶`/braille/box-drawing are absent from LiberationSans and render as nothing. Use `MenuBusy`.
- **`MenuStepper` and `MenuStatusLine` invert the obvious:** a stepper does not move until a caller calls `SetValue`, and a `Polled` status write is refused while a `Warn` stands (a 2 Hz redraw would otherwise erase the failure before it could be read).
- **`CrosshairUI`'s hover dimming is a dead code path, deliberately** — do not "fix" it by wiring `playerInteractor` without owning the look change. The visor work is where that look change gets owned: the reticle moves into `VisorReticle` (see [Visor.md](Visor.md)) and `CrosshairUI` goes with it.

## Extending

**A new menu-scene page**
1. Subclass [MenuScreen](Assets/Game/Scripts/Presentation/UI/Widgets/MenuScreen.cs); add a static `Open(MainMenuUI menu, …)` that guards `Existing<T>()` for "already open", `AddComponent`s onto a new GameObject, assigns fields, then calls `Present(menu)` **last**.
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
