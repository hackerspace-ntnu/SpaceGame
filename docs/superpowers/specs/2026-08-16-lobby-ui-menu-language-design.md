# Lobby UI in the main menu's language

**Date:** 2026-08-16
**Status:** approved, implementing

## The problem

Every menu in the game is now drawn in one language: bold text over the live 3D menu scene,
entries cloned from the menu's button prefab so they carry its hover animation and its two FMOD
sounds, text typed on a rule rather than in a box. `MenuScreen` + `MenuEntry` write that language
down; `WorldSelectUI` speaks it.

The lobby does not. It is a separate scene with its own camera and a flat background, four tabs
(Browse / Create / Join by code / Direct), dark `UITheme` panels, chips and boxed input fields —
half of which `LobbyModeUI` switches off at runtime depending on which route the player took.

Two facts found while surveying, both of which shape the design:

1. **The tabbed screen has never shipped.** `LobbyMenu.unity` on disk still holds the *old*
   hand-authored menu (`MainMenuCanvas`, `CreateLobbyPanel`, `LobbyListPanel`,
   `JoinPrivateLobbyPanel`, `WarningPanel`). None of `LobbyMenuBuilder`'s output
   (`LobbyMenuCanvas`, `Tabs`, `BrowseBody`, `CreateBody`, `StatusStrip`) is present, and the
   scene is not dirty in git. The 713-line builder has never been run. What a player sees today is
   the seven-panel screen that builder was written to replace.

2. **The password prompt was unreachable.** `JoinLobbyByPasswordPanel` was built inactive, and the
   only code that touched it was `LobbyListSystem.closeJoinPrivateLobbyScreen()` →
   `SetActive(false)`. Nothing anywhere called `SetActive(true)`. Underneath that,
   `JoinByCodeAsync` reported a password failure through the same `Failed` string as every other
   error, so nothing *could* tell "needs a password" from "no such lobby". Passwords have since
   been dropped from the design outright, so this is resolved by deletion rather than repair.

## Decisions

| Question | Decision |
|---|---|
| Where does the lobby live? | Retire `LobbyMenu.unity`. The lobby becomes a `MenuScreen` over the main menu. |
| Host arrival | Auto-create the session on arrival, named after the world. No create form. |
| Privacy | A public/private toggle on the roster page, applied via `UpdateLobbyAsync`. |
| Passwords | Removed entirely. |
| Direct connect | Dropped from the menu. |

Privacy is a post-creation control because the session exists the moment the page opens, so there is
no earlier moment to ask — and the roster is the only screen where the host is idle long enough to
care.

**Passwords are gone.** Private means delisted from the browser and nothing more; the session stays
reachable by its code, which is already a secret you have to be told. A password on top of that
guarded nothing the code did not already guard and cost a whole page to collect. Removing it also
removes the reason `JoinOutcome` existed — with no "needs a password" answer, a join is a bool
again.

## The flow

No scene loads until the game itself starts. Every step happens over the live main-menu scene.

```
MAIN MENU
└─ Multiplayer ──► MULTIPLAYER
                   ├─ Host a game ─► WORLDS ─► LOBBY (roster, session already created)
                   ├─ Join a game ─► JOIN A GAME ─┬─ pick a row ─► LOBBY (roster)
                   │                              └─ code ──────► LOBBY (roster)
                   └─ Back
```

Four tabs collapse to two pages. The host never sees the join side and the joiner never sees a
create form — not because a component switches halves off, but because they are different pages
reached by different routes.

## The screens

Type scale is `MenuEntry`'s: 110pt titles, 64pt actions, 52pt rows, 30pt captions.

```
JOIN A GAME                          DUNE VALLEY

  Enter a code   Open sessions          Code ABC123  Copy   Share the code…
  ABC123______   Kari's game     2/4
  Join           Bjørn  playing  3/4    Ferdinand    host   Private session  off
                                        Kari                Listed in the browser…

  Refresh  Back                         Start game   Leave
```

- **Join page.** Code typed on a rule, then `Join`, on the left. The browser on the right: one row
  per open session, occupancy right-aligned the way `WorldSelectUI` right-aligns a timestamp,
  `playing` marking sessions already in progress. Clicking a row joins it directly — joining is
  reversible, so it needs no select-then-confirm the way deleting a world does. A message line
  above the footer carries errors.
- **Roster page.** Host sees the world name as the title, the code with a `Copy` action and the
  player list on the left; status and the `Private session` toggle on the right; `Start game` and
  `Leave` along the bottom. A joiner sees the lobby's name, the roster, `Waiting for the host to
  start.` and `Leave`. While the create is in flight the page says so; if it fails the message
  lands here with `Leave` still available.

The privacy toggle carries a line saying what it actually does — "Hidden from the browser. Anyone
with the code can still join." — because "private" reads like "nobody can get in", and the code
still working is the entire point of it.

## Components

### New

**`LobbyUI : MenuScreen`** — `Assets/Game/Scripts/Presentation/UI/Pages/LobbyUI.cs`

Navigation between the two pages, the join page itself, and every `LobbySession` call. Which page it
opens on is read from `WorldSession.IsActive`, preserving the existing rule that *the staged world
is* the host/join difference — `MainMenuUI.HostMultiplayer` stages one, `JoinMultiplayer` clears it,
and a second flag beside it could only ever disagree.

**`LobbyRosterView`** — `Assets/Game/Scripts/Presentation/UI/Widgets/LobbyRosterView.cs`

Builds the roster page and re-renders it from a `Lobby` on every session change (twice a second).
Separated because it is the only *live* page — the join page is a static form — and because it
touches no service, so it can be exercised without one. It is also the only thing that writes the
privacy label, so what the toggle reads is always the lobby's own flag rather than the last thing
the host clicked, which is what matters when an update fails.

**`MenuField`** — `Assets/Game/Scripts/Presentation/UI/Widgets/MenuField.cs`

Text on a rule, this menu's substitute for an input box, plus the right-aligned trailing value used
for occupancy and toggle state.

### Changed

- **`MainMenuUI`** — `EnterLobby()` opens `LobbyUI` instead of loading a scene; the `lobbyScene`
  field goes. `EnterWorld()` and the minigame path are untouched.
- **`MultiplayerChoiceUI`** — becomes a `MenuScreen` subclass, dropping the ~90 lines of canvas
  creation, canvas hiding and restoring it duplicates from it. Words and routes unchanged.
- **`LobbySession`** — `CreateAsync(name, isPrivate)` and `JoinByCodeAsync(code)` lose their
  password parameters; joins stay `Task<bool>`. New `SetPrivacyAsync(bool isPrivate)`.
- **`LobbySessionOptions`** — `BuildPrivacyOptions(bool)`, beside the existing pure builders. Sets
  `IsPrivate` and nothing else: `UpdateLobbyOptions` reads a null `Password` as "leave it alone",
  which is exactly right when there is never one to leave.
- **`LobbyMenuWiringTests`** — the cases pinning `LobbySystem` / `LobbyListSystem` method names
  exist because `LobbyMenu.unity` bound controls to them by string. With the scene gone the
  bindings are compiled and the compiler pins them; those cases go. `MainMenuUI`'s cases stay —
  `MainMenu.unity` still binds those by name.
- **`DirectConnectController`** — serialized fields of type `LobbySystem` and `LobbyWarningSystem`
  are replaced with a plain scene name and `Debug.LogWarning`. Without this it does not compile
  once those classes are deleted. It remains in the codebase, compiling and unreferenced.

### Deleted

Verified by script GUID: this cluster appears in exactly two asset files, `LobbyMenu.unity` and
`LobbyElement.prefab`, both of which go with it.

```
Assets/Game/Scenes/Menus/LobbyMenu.unity
Assets/Game/Scenes/References/LobbyMenu.asset      (+ its EditorBuildSettings entry)
Assets/Game/Editor/Multiplayer/LobbyMenuBuilder.cs
Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/LobbySystem.cs
Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/LobbyListSystem.cs
Assets/Game/Scripts/Presentation/UI/LobbyMenu/Core/LobbyModeUI.cs
Assets/Game/Scripts/Presentation/UI/LobbyMenu/Widgets/LobbyWarningSystem.cs
Assets/Game/Scripts/Presentation/UI/LobbyMenu/Widgets/LobbyUIManager.cs
Assets/Game/Scripts/Presentation/UI/LobbyMenu/Widgets/OpenCloseUIElement.cs
Assets/Game/Scripts/Presentation/UI/LobbyMenu/Widgets/LobbyElementController.cs
Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/JoinLobbyByCodeController.cs
Assets/Game/Scripts/Presentation/UI/LobbyMenu/Join/JoinLobbyByPasswordButton.cs
Assets/Game/Prefabs/UI/LobbyMenu/LobbyElement.prefab
Assets/Game/Prefabs/UI/LobbyMenu/PlayerDisplayElement.prefab
```

## Where the content sits

Every page in this language draws text straight over the menu's 3D set, with nothing behind it. That
makes position a legibility problem, not a taste one.

MainMenu.unity's camera sits at zero pitch, and a camera with no pitch puts the horizon exactly
across the middle of what it sees — so the bottom half of the screen is ground and the top half is
sky. Entries are drawn in `MenuEntry.Idle`, a dark navy that disappears against bright sky and reads
cleanly against sand. So **every clickable thing belongs below the middle of the screen**. The
menu's own `ButtonRow` arrives at the same place from the other direction, by anchoring at 0.5 and
growing downward.

The constants live on `MenuEntry` (`Horizon`, `TitleTop`, `ContentTop`, `ColumnX`, `ColumnWidth`,
`FooterBottom`, `MessageBottom`) so the screens agree with each other rather than each picking its
own inset. Titles are the deliberate exception: they are white, white reads against sky, and a
110pt title below the horizon would eat the space the content needs.

Two pages could not be a single stack once confined to the bottom half — roughly 630px of content
into 540px of screen — so they are two columns:

- **Join** — code and its action on the left, the session browser on the right, one footer under both.
- **Roster** — code and the player list on the left, status and the host's privacy controls on the
  right, Start/Leave along the bottom. The list is sized to show a full lobby without scrolling,
  which is four rows, because `LobbySession.MaxPlayers` is 4.

`MinigameConfigUI` is not in this language but sits over the same set, so its column is confined to
the same band.

## Error handling

- Create fails → message on the roster page, `Leave` still available. `LobbySession` already shuts
  the transport down on this path.
- Join fails → message on the join page, from `Failed` where there is a specific reason and a
  generic line only where there is not.
- Host closes the lobby → `LobbySession`'s poll already raises `Failed` and forgets the lobby;
  a joiner falls back to the join page carrying that reason rather than a generic one.
- Privacy update fails → the message is *sticky* on the roster's status line. That page redraws
  twice a second, so an ordinary status would be replaced before it could be read — and the line
  that replaced it would say everything was fine.
- Services unavailable → `EnsureReadyAsync` fails and the message lands on whichever page is up.
  Direct connect is no longer the escape hatch it was; noted as a consequence of dropping it.

## Testing

- `LobbySessionTests` — `BuildCreateOptions` never sets a password and delists a private lobby;
  `BuildPrivacyOptions` sets `IsPrivate` both ways and never sends a password.
- `LobbyMenuWiringTests` — reduced to what is still resolved by string, i.e. `MainMenuUI`'s
  scene-bound entry points.
- Compile + full EditMode suite headless.

## Open items

- Dropping direct connect removes the only route that works when Relay or Lobby are unavailable.
  `DirectConnectController` stays in the tree so restoring it later is a UI entry, not a rewrite.
- `MinigameConfigUI` still carries its own copy of what `MenuScreen` now does. Out of scope here;
  an obvious follow-up.

## Coordination

Another session was editing `MainMenuUI.cs`, `WorldSelectUI.cs`, `MenuScreen.cs`, `MenuEntry.cs`
and the lobby scripts concurrently with this survey — `MainMenuUI.cs` changed mid-read. Re-read
those files before editing. Separately, the tree does not compile as found:
`Assets/Game/Editor/Menus/WorldSelectBuilder.cs:203-204` binds `WorldSelectUI.LoadSelected` and
`DeleteSelected`, neither of which exists on the rewritten `WorldSelectUI`.
