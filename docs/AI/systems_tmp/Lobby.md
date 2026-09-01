# Lobby

The multiplayer lobby is two halves with one seam between them: a **session** that owns lobby
state for as long as the application runs, and a **screen** that is disposable view over it. The
session never touches `UnityEngine.UI`; the screen never calls the Lobby service.

The namespaces are `…Lobbies`, plural, while the folders are `Lobby/`. Not a typo: a namespace
whose last segment is `Lobby` shadows the SDK's `Unity.Services.Lobbies.Models.Lobby` type from
inside it — C# finds the enclosing namespace's member before it consults any `using` — and every
reader in the Core folder takes a `Lobby`.

## Core — `Assets/Game/Scripts/Core/Multiplayer/Lobby/` (`SpaceGame.Core.Lobbies`)

| File | Concern |
| --- | --- |
| `LobbySession.cs` | The one owner of lobby state: `Current`, `State`, `Changed`/`Failed`, lifecycle, leaving, disconnects. `DontDestroyOnLoad`, created on first use. |
| `LobbySession.Hosting.cs` | Create, start the game, and the two host-only live controls (privacy, VS team rules). |
| `LobbySession.Joining.cs` | Join by id or code, then connect to the Relay server the lobby advertises; rolls the membership back if Relay fails. |
| `LobbySession.Browsing.cs` | The browser's query, and the one place its one-per-second rate budget is spent. |
| `LobbyHeartbeat.cs` / `LobbyPoll.cs` | The two timers: keep a hosted lobby listed; refresh the lobby this peer is in. Each is one request at a time. |
| `LobbyPlayerPublisher.cs` | Suit colour, team and team colour, each on its own `DebouncedPublish<T>` clock. |
| `LobbyJoinRecovery.cs` | The 409 "already a member" sweep-and-retry, with the service calls as delegates. |
| `LobbyServiceErrors.cs` | Turns what the service threw into a readable line; recognises the SDK's own null-on-error path. |
| `Data/LobbyKeys.cs` | Every key in lobby and player data, and what its visibility has to be. |
| `Data/LobbyOptions.cs` | The option objects handed to the service. Pure. |
| `Data/LobbyRoster.cs` / `Data/LobbyTeams.cs` | Readers over a `Lobby`: who is here / how the VS teams are shaped. Pure; meet in `LobbyRoster.Snapshot`. |
| `Data/TeamColorOpinion.cs` | The `"swatch:stampMs"` codec, and why a team's colour is an opinion per player. |
| `Data/RosterSnapshot.cs` | What a view reads. SDK-free on purpose so views can be tested without UGS. |
| `Data/VersusSetup.cs` | The team shape a VS lobby is created with, already clamped. |

Rules that hold across the folder:

- **Nothing throws across the boundary.** Failures arrive as `Failed(string)` with a message fit
  to show a player.
- **One operation at a time** (`TryBegin`) guards only the calls that allocate — create, join,
  start. Privacy, team rules and the publishers deliberately bypass it.
- **The pure readers take a `Lobby`, never the service.** The single thing that needs the
  authentication service — the local player's slot — is passed in.

## Presentation — `Assets/Game/Scripts/Presentation/UI/Lobby/` (`SpaceGame.Presentation.Lobbies`)

```
LobbyUI.cs            MenuScreen: swaps the two pages, wires their actions to the session, renders
LobbyRoute.cs         host/join × story/VS, carried explicitly (never inferred from a staged world)
Join/
  LobbyJoinFlow.cs    signs in, auto-refreshes, turns a press into a join; attempt generations
  LobbyJoinPage.cs    the widgets: code column, footer, per-region locking
  LobbyBrowser.cs     the session list, reconciled row by row every second
  LobbyBrowserRow.cs  one session: name, "Joining…"/occupancy slot, pips, PLAYING
  LobbyAutoRefresh.cs the refresh clock with back-off (pure)
  LobbyBusyScope.cs / LobbyBusyState.cs   what each wait switches off (pure table)
  LobbyJoinLayout.cs  the page's geometry, shared with the layout tests
Roster/
  LobbyRosterFlow.cs      the in-lobby actions against the session (copy, privacy, colours, teams)
  LobbyRosterView.cs      the live page; redraws from a RosterSnapshot
  LobbySessionStrip.cs    code / Copy / Private, along the top
  LobbyTeamRulesStrip.cs  the host's Teams / Team size steppers (VS only)
Rank/
  LobbyPreviewRank.cs    conducts the rank (MonoBehaviour: the overlays need LateUpdate)
  LobbyRankFigures.cs    the astronaut figures: instantiate, seat, recolour, face the camera
  LobbyPreviewCamera.cs  borrows the menu camera for the lobby's shot and fits it to the rank
  LobbyOverlayLayer.cs   the rect the overlays live in, and world-point → UI placement
  LobbyNameplates.cs / LobbyTeamPlates.cs / LobbySuitCycler.cs   the three overlays
```

Shared widgets this work pulled out of the lobby, in `Presentation/UI/Widgets/`:

- `MenuStatusLine` — transient / sticky / polled / animated-wait semantics for a page's status line.
- `MenuLock` — how a control goes quiet (a `CanvasGroup`, never the Button's Disabled state — see
  the class doc for the animator trap).
- `UIBuilder.Row`, `UIBuilder.PinnedTop` / `PinnedBottom`, `UIBuilder.ShadowedLabel`.

## Verifying a change

- EditMode guards: `LobbyOptionsTests`, `VersusLobbyDataTests`, `LobbyJoinRecoveryTests`,
  `LobbyServiceErrorsTests`, `LobbyRosterViewTests`, `LobbyRankLayoutTests`, `LobbyLayoutTests`,
  `LobbyAutoRefreshTests`, `MenuBusyTests`, `LobbyRouteTests`, `SessionExitTests`.
- Anything that talks to the service needs two machines — see the `spacegame-multiplayer` skill.
