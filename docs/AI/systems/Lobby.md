---
system: Lobby
layer: presentation
summary: Unity Lobby session state plus the disposable lobby screen: hosting, joining, roster and team rules
paths:
  - Assets/Game/Scripts/Core/Multiplayer/Lobby/
  - Assets/Game/Scripts/Presentation/UI/Lobby/
  - Assets/Game/Scripts/Presentation/UI/Widgets/
symptoms:
  - "joining fails with 409 'player is already a member of the lobby'"
  - "a failed join throws a bare NullReferenceException instead of a readable service error"
  - "the lobby browser stops refreshing, or hammers the service past its rate limit"
  - "a hosted lobby stays listed after the host has left"
  - "the lobby is joined but the Relay connection fails and the player is stranded"
  - "the roster shows the wrong team, team colour or suit for a player"
  - "astronauts in the lobby float above the sand or stand sunk into it"
  - "team names or player names overlap each other with more than four teams"
  - "the menu's decorative astronauts stand in front of the roster once the lobby has several teams"
  - "the two rows of team plates land in the same band of screen and smear together"
  - "the lobby rank is tiny, clipped or badly framed on a small or narrow window"
  - "the versus lobby's player names come out too small on an ultrawide and too large on a narrow window"
  - "the compiler cannot resolve Unity's Lobby type inside this folder"
  - "a lobby control looks enabled but does nothing while a request is in flight"
reads_with: [UI, GameModes, Multiplayer]
updated: 2026-09-02
---

# Lobby

Unity Gaming Services Lobby wrapped in one app-lifetime `LobbySession` plus a disposable menu page, with the Relay join code as the single seam between "who is in the lobby" and "who is in the netcode session".

**Scope:** `Assets/Game/Scripts/Core/Multiplayer/Lobby/` (+ `Data/`), `Assets/Game/Scripts/Presentation/UI/Lobby/` (`Join/`, `Roster/`, `Rank/`), [SessionLauncher.cs](Assets/Game/Scripts/Core/Multiplayer/Session/SessionLauncher.cs).
**Related:** [Multiplayer.md](Multiplayer.md) · [UI.md](UI.md) · [GameModes.md](GameModes.md)

## Model

- **Two halves, one seam.** [LobbySession.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/LobbySession.cs) owns lobby state for the whole application run (`DontDestroyOnLoad`, created lazily by `Instance`); [LobbyUI.cs](Assets/Game/Scripts/Presentation/UI/Lobby/LobbyUI.cs) is a page over the main menu that is thrown away. The session never touches `UnityEngine.UI`; the screen never calls `LobbyService`.
- **Lobby membership ≠ netcode session.** UGS owns the roster, the code, the listing and the per-player data. NGO + Relay own the actual connection ([Multiplayer.md](Multiplayer.md)). The only bridge is `LobbyKeys.RelayJoinCode` in lobby data: join the lobby, read the code, `SessionLauncher.JoinRelayAsync`. If Relay fails the membership is rolled back.
- **The session outlives the menu on purpose.** A lobby unheartbeated for 30 s is delisted, so a session tied to the menu scene could never be joined once the host started playing — which is what "start now, let friends in later" needs.
- **Nothing throws across the boundary.** Failures arrive as `Failed(string)` with player-readable text; `SessionLauncher` answers with `SessionResult`. Views render from `LobbyState` (`Idle`/`InLobby`/`InGame`) and nothing else.
- **The pure readers never see the service.** `LobbyRoster`, `LobbyTeams`, `LobbyOptions`, `TeamColorOpinion` take a `Lobby` and are static; the one thing needing `AuthenticationService` — the local player's slot — is passed in. `RosterSnapshot` is SDK-free so every view can be tested without UGS.
- **There is no ready check.** The host presses Start; the lobby is *not* locked and the heartbeat keeps running, so a late joiner is pulled into the running world by NGO scene synchronisation.
- **Namespaces are `…Lobbies` (plural) while the folders are `Lobby/`** — a namespace ending in `Lobby` shadows `Unity.Services.Lobbies.Models.Lobby` from inside it.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `LobbySession` | [LobbySession.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/LobbySession.cs) | State + lifecycle: `Current`, `State`, `IsHost`, `LocalSlot`, `LocalTeam`, `Changed`/`Failed`, `LeaveAsync`, `LeaveInBackground`, `CurrentSnapshot()`. `Instance` creates on touch; **`Existing`** answers without conjuring one |
| `LobbySession.Hosting` | [LobbySession.Hosting.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/LobbySession.Hosting.cs) | `CreateAsync`, `BeginGameAsync`, and the host-only live controls `SetPrivacyAsync` / `SetTeamRulesAsync` |
| `LobbySession.Joining` | [LobbySession.Joining.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/LobbySession.Joining.cs) | `JoinByIdAsync` / `JoinByCodeAsync`, then Relay, with membership rollback |
| `LobbySession.Browsing` | [LobbySession.Browsing.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/LobbySession.Browsing.cs) | `QueryAsync` (25 rows, free slots > 0, newest first) and the shared 1.1 s query spacing |
| `LobbyHeartbeat` / `LobbyPoll` | [LobbyHeartbeat.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/LobbyHeartbeat.cs) | Keep a hosted lobby listed (15 s); refresh the joined lobby (2 s). One request in flight each |
| `LobbyPlayerPublisher` / `DebouncedPublish<T>` | [LobbyPlayerPublisher.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/LobbyPlayerPublisher.cs) | Suit colour, team, team colour — each on its own 0.75 s debounce clock |
| `LobbyJoinRecovery` | [LobbyJoinRecovery.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/LobbyJoinRecovery.cs) | The 409 sweep-and-retry-once; service calls arrive as delegates so it is testable |
| `LobbyServiceErrors` | [LobbyServiceErrors.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/LobbyServiceErrors.cs) | `Describe(e, headline)` and `IsSdkErrorPathFailure` — see Gotchas |
| `LobbyKeys` / `LobbyData` | [LobbyKeys.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/LobbyKeys.cs) | Every key and its required visibility; non-throwing, invariant-culture readers |
| `LobbyOptions` | [LobbyOptions.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/LobbyOptions.cs) | Every option object handed to the service. Pure and static |
| `LobbyRoster` / `LobbyTeams` | [LobbyRoster.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/LobbyRoster.cs) | Names, suits, slots, `IsPlaying` / VS rules, teams, occupancy, team colours. They meet in `LobbyRoster.Snapshot` |
| `RosterSnapshot` | [RosterSnapshot.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/RosterSnapshot.cs) | What every view reads. SDK-free, index-guarded, arrays never null |
| `TeamColorOpinion` / `VersusSetup` | [TeamColorOpinion.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/TeamColorOpinion.cs) | `"swatch:stampMs"` codec; the already-clamped team shape a VS lobby is created with |
| `LobbyUI` / `LobbyRoute` | [LobbyRoute.cs](Assets/Game/Scripts/Presentation/UI/Lobby/LobbyRoute.cs) | `MenuScreen` swapping two pages; host/join × story/VS carried explicitly from [MainMenuUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/MainMenuUI.cs) |
| `LobbyJoinFlow` | [LobbyJoinFlow.cs](Assets/Game/Scripts/Presentation/UI/Lobby/Join/LobbyJoinFlow.cs) | Sign in, auto-refresh, join; cancellation is an **attempt generation counter**, not a token |
| `LobbyJoinPage` / `LobbyBrowser` / `LobbyBrowserRow` | [LobbyBrowser.cs](Assets/Game/Scripts/Presentation/UI/Lobby/Join/LobbyBrowser.cs) | Code column + footer; the list, reconciled row-by-row by lobby id |
| `LobbyAutoRefresh` / `LobbyBusyScope` / `LobbyBusyState` | [LobbyAutoRefresh.cs](Assets/Game/Scripts/Presentation/UI/Lobby/Join/LobbyAutoRefresh.cs) | 1 s cadence measured from completion, doubling back-off to 15 s; a table of what each wait locks |
| `LobbyRosterFlow` / `LobbyRosterView` | [LobbyRosterFlow.cs](Assets/Game/Scripts/Presentation/UI/Lobby/Roster/LobbyRosterFlow.cs) | In-lobby actions (copy, privacy, colour, team, rules) and the only live page |
| `LobbySessionStrip` / `LobbyTeamRulesStrip` | [LobbySessionStrip.cs](Assets/Game/Scripts/Presentation/UI/Lobby/Roster/LobbySessionStrip.cs) | One top band: Code / Copy / Private on the left, and the host's Teams / Team size steppers (VS only) at the right end, drawn at the strip's own caption scale in white via `MenuStepper.Skin` |
| `LobbyPreviewRank` (+ `LobbyRankFigures`, `LobbyPreviewCamera`, `LobbySetDressing`, `LobbyOverlayLayer`, `LobbyNameplates`, `LobbyTeamPlates`, `LobbySuitCycler`) | [LobbyPreviewRank.cs](Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyPreviewRank.cs) | The roster *is* a rank of astronauts standing in `MainMenu.unity` on the authored `LobbyPreviewAnchor`, with UI overlays tracking world points in `LateUpdate`. Teams wrap four abreast, every seat is probed onto the ground, each overlay sizes itself from its own projected spacing, team plates zoom under the pointer (`HoverScale`) and hang higher per row of teams, and the menu's decorative astronauts are hidden while the rank is up. Colour control splits by mode: a story lobby shows the cycler under your own figure; a VS lobby hides it and steps the team colour from chevrons hugging the LOCAL team plate's name, whose text colour is the readout. Names split the same way: a VS lobby hides the over-head nameplates entirely and lists each team's members vertically in small lowercase on its plate — below a front-row plate, stacked above a back-row one |

Shared widgets: [MenuStatusLine.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuStatusLine.cs) (transient / sticky / animated waits), [MenuLock.cs](Assets/Game/Scripts/Presentation/UI/Widgets/MenuLock.cs) (locks via `CanvasGroup`, never the Button's Disabled state).

## Flows

1. **Create (host).** `MainMenuUI` → `LobbyUI.Open(route)` → roster page shown busy *first* → `EnsureReadyAsync` (UGS init + anon sign-in under `SessionProfile`) → `SessionLauncher.HostRelayAsync` → `LobbyService.CreateLobbyAsync` with the relay code, `GameState=waiting` and `Mode` already in `Data` → `Adopt(InLobby)`. Relay is allocated **before** the lobby so a failure leaves no orphan listing.
2. **Join.** `LobbyJoinFlow` locks the page for the attempt → `EnsureReadyAsync` → `LobbyJoinRecovery.JoinAsync` (join; on 409 release every joined lobby and retry once) → read `RelayJoinCode` → `SessionLauncher.JoinRelayAsync` → `WaitForClientConnectedAsync` → `Adopt(InGame` if `GameState=in-game`, else `InLobby)`. Any failure after the join calls `RemovePlayerAsync` first.
3. **Roster update.** `LobbyPoll` fetches every 2 s → `OnPolled` (dropped if `Current` went null meanwhile) → `Adopt` → `Changed` → `LobbyUI.Render` → `LobbyRosterFlow.Render` → `SetSession` + `Render(CurrentSnapshot(), IsHost, hostTitle)` → the rank restands and the overlays follow.
4. **Local change.** Colour chevron (story: the cycler; VS: the local team plate's arrows) or team press → view repaints locally *now* → `PublishSuitColor` / `PublishTeam` / `PublishTeamColor` → debounced `UpdatePlayerAsync` → everyone else sees it on their next poll.
5. **Start.** Host presses Start → `LoadingScreenUI.ShowUntilReady` → `BeginGameAsync`: requires host + a running server, sets `GameState=in-game` (**no lock**), then `NetworkManager.SceneManager.LoadScene`. On failure the loading screen is dismissed and the lobby survives.
6. **Leave.** `LeaveAsync` → `Forget()` and `SessionLauncher.Shutdown()` locally first, then best-effort `RemovePlayerAsync`. A joiner returns to the browser; a host also clears `WorldSession` and staged VS rules and closes the screen. In-world exits go through `LeaveInBackground` ([SessionExit.cs](Assets/Game/Scripts/Core/Multiplayer/Session/SessionExit.cs)); a disconnect is caught by `DisconnectHook` → `Fail` + `Forget` + quiet removal.

## Multiplayer

| Concern | Owner |
| --- | --- |
| Roster, codes, listing, membership | **UGS Lobby.** Every mutation is a service call; there is no NGO traffic in this folder at all |
| Lobby data (`Mode`, `TeamCount`, `TeamSize`, privacy, `GameState`) | **Host only** — `UpdateLobbyAsync` is host-restricted, hence `RequireHost` |
| Player data (name, suit, team, team colour) | **Each member writes their own** via `UpdatePlayerAsync`. Team colour is therefore an *opinion*, tagged with the team it was cast FOR (`"swatch:stampMs:team"`) and resolved identically on every peer as the highest stamp among that team's votes (ties → earliest lobby order, strict `>`). The tag is what makes switching teams keep the destination's colour instead of importing yours — an untagged legacy vote falls back to the voter's current team |
| Visibility | `GameState` and `Mode` are **Public** (the browser reads them before joining); everything else is Member |
| Transport / connection | `SessionLauncher` + NGO. `IsHost` is `Current.HostId == AuthenticationService.PlayerId` — the lobby's host, not NGO's |
| Lobby → match | [NetworkGameManager.Versus.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.Versus.cs) derives `VersusSession` from `LobbySession.Existing?.Current` on **every** peer before anything spawns, so host and clients cannot disagree; the client then reports its team over its own RPC |
| Concurrency | `TryBegin` allows one *allocating* operation (create, join, start) at a time. Privacy, team rules and the publishers deliberately bypass it |

## Persistence

Nothing here is saved. Lobby state lives on the service and dies with the session — that is why membership is handed back on the way out and swept by `LobbyJoinRecovery` on the way in. Two adjacent values *are* persisted, elsewhere: `GameSettings.PlayerName` / `SuitColorIndex` (install preferences, written by the story cycler, shared with `PlayerIdentity`), and the world a story host staged (`WorldSession`, cleared when the host leaves). A VS team colour is deliberately **not** stored in `GameSettings` — it belongs to the match. See [Persistence.md](Persistence.md).

## Gotchas

- **409 ghost memberships are yours from last run.** Anonymous auth hands back the *same* PlayerId every launch, and membership is only released by pressing Leave — a crash, a Relay timeout or a killed process leaves your id sitting in a lobby, and the next join is refused with `LobbyConflict`. [LobbyJoinRecovery.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/LobbyJoinRecovery.cs) releases *every* joined lobby and retries **once**. Do not lean on the SDK's own recovery: joining by id it gives up unless `GetJoinedLobbies` returns exactly one lobby, and then joins *that* one instead of the one you asked for.
- **Two editor instances on one machine share PlayerPrefs**, so both sign in as the same player and the second is 409'd — launch the clone with `-sgprofile client` ([SessionProfile.cs](Assets/Game/Scripts/Core/Multiplayer/Session/SessionProfile.cs)).
- **A refused request arrives as a bare `NullReferenceException`, not a `LobbyServiceException`.** `WrappedLobbyService.TryCatchRequest` dereferences `he.ActualError.Code`, and `ActualError` is null whenever the service answers an HTTP error with an unparseable body — which its rate limiter does. The status code is destroyed by the same dereference. `LobbyServiceErrors.IsSdkErrorPathFailure` matches `NullReferenceException` **plus a `Unity.Services.Lobbies` stack frame**, so a genuine null bug in our own code is still reported as one.
- **Rate limits are the design constraint.** Query 1/s (auto-refresh at 1 s measured from *completion*, plus a shared 1.1 s floor in `LobbySession` because the Refresh button is a second caller the browser cannot see); `GetLobby` 1/s per lobby (poll at 2 s); `UpdatePlayer` 5 per 5 s (hence `DebouncedPublish`); heartbeat 15 s against a 30 s delist window.
- **`QueryAsync` returns `null` for failure and an empty list for "nothing open".** Collapsing them empties the browser on every hiccup; callers keep the last known list on `null`.
- **`LobbySession.Instance` creates on touch.** Anything merely *asking* whether a lobby exists must use `Existing`, or singleplayer conjures a `DontDestroyOnLoad` session that outlives every scene.
- **`TryBegin` returns false silently.** That is why the join page locks itself for the whole attempt — a second click otherwise painted "Could not join" over a join that was still succeeding.
- **A cancelled join that succeeds must be handed back.** `LobbyJoinFlow.Abandon` calls `LeaveAsync`, or the player occupies a slot in a lobby nothing is showing — and becomes their own next 409.
- **Private means delisted, not locked**, and `BeginGame()` deliberately sets no `IsLocked`: locking made joining a session in progress impossible, and the host is usually alone when the first friend tries.
- **A VS host allocates Relay for `VersusRules.MaxSeats`, not `VersusSetup.Seats`.** Relay's allocation size is fixed at creation and the live steppers can grow a team afterwards; the *lobby's* advertised max follows the rules so "3/8" in the browser is trustworthy.
- **An absent `Mode` key reads as story** (`LobbyTeams.IsVersus`), which is why mode and team rules are stamped in `CreateLobbyOptions` rather than in a follow-up update — a poll landing in the gap would flash a VS lobby into the story browser. The relay code is in there for the same reason.
- **Out-of-range team indices are folded by modulus, not clamped to 0** (`LobbyTeams.FoldTeam`), so a peer on a build with more teams does not pile everyone onto team one.
- **Unsubscribe.** The session outlives the screen and will raise `Changed`/`Failed` at a destroyed page; `LobbyUI.OnDestroy` and `LobbyJoinFlow.Dispose` are what stop a poll driving thrown-away rects.
- **`RankLayout` returns a flat local `y = 0` on purpose, and it is not where anybody stands.** The
  seats are pure geometry; `RankGrounding` probes each one onto the sand and `LobbyRankFigures.Seat`
  takes a **world** position. Assigning a seat as a `localPosition` silently re-flattens the whole
  rank back onto the anchor's plane — which is what it used to do, and why a wide rank floated over
  dips and sank into rises.
- **The lobby camera's authored eye is 1.389 m above the anchor — below a 1.8 m head.** Any second
  row of anything is invisible from it. `RankLayout.EyeHeight` is what lifts it — it holds a 16°
  down-angle (`MultiRowDownAngle`) rather than a clearance, so the rows separate on screen — and
  only when `TeamRowsFor > 1`, so a one-row rank still reproduces the authored shot exactly.
- **Team plates hang higher per row of teams** (`RankLayout.PlateLift`), because from a near-level
  eye with one shared lift the front and back rows' plates projected fractions of a degree apart
  and smeared. Vertical position is what says which row a plate belongs to.
- **The rank's overlays measure in CANVAS pixels, never screen pixels.**
  [`LobbyOverlayLayer.TryToCanvas`](Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyOverlayLayer.cs)
  is the one conversion out of world space, and it hands back the same units every font size and row
  width in these files is written in. `LobbyNameplates` used to project to screen pixels and convert
  with `1920 / Screen.width` — the answer for a scaler matching WIDTH, which is not the rule the
  lobby's canvas follows — so names came out about 15% too small on a 21:9 monitor and too large on a
  narrow window, and `LobbyPreviewCamera` made the mirror-image error deciding how much of the frame
  the rank could use. `LobbyTeamPlates` never had the bug because it already measured in canvas
  space. See [UI.md](UI.md) for the scaling rule itself.
- **The menu's decorative astronauts are hidden by name prefix at scene ROOT only.**
  `LobbySetDressing` matches `AstronautArmature*` root objects; the rank's own figures contain an
  `AstronautArmature` node *inside* their hierarchy, so a deep search would hide the roster itself.
  Renaming or re-parenting the set dressing in `MainMenu.unity` silently puts it back in the shot.
- **Team names are numeric** — `VersusRules.TeamName` generates `"TEAM 3"`, so the plate ladder's
  full, shortened and floor rungs all agree on the same digit. There is no name array to extend.
- **Teams sit on one shared half-pitch lattice, re-centred once.** Centring each row on itself and
  then staggering it is *not* equivalent: at five teams the two corrections cancel and the lone back
  team lands exactly behind a front one. Change the lattice, not the per-row centring.
- **The menu's CanvasScaler matches WIDTH at 1920x1080**, so the canvas is always 1920 wide and its
  *height* moves with the aspect ratio. Anything reasoning about how much vertical room the page has
  must compute `1920 * Screen.height / Screen.width` — never assume 1080.
- The rank's chevrons are ASCII: LiberationSans SDF has no ◀/▶ and TMP silently substitutes empty boxes.
- **The roster page's session name sits in the LOWER-RIGHT corner**, level with the footer's
  actions, in navy (`MenuEntry.Idle`) because down there it is over sand. Its rect is the right
  half of the column: right-aligned so a long name grows leftwards, but capped so it can never
  reach the Start/Leave buttons that own the row's left end.

## Extending

1. **New lobby-data field.** Add the key to [LobbyKeys.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/LobbyKeys.cs) and decide its visibility — **Public only if the browser reads it before joining**, else Member. Stamp it in `LobbyOptions.Create` (never in a follow-up update) and add an `UpdateLobbyOptions` factory beside `TeamRules` if it is live-tunable.
2. **Read it purely.** Add a static reader over `Lobby` in `LobbyRoster` / `LobbyTeams` using `LobbyData.Text`/`Int` (never an indexer — a peer mid-join or on an older build has no key), give it a fallback, and surface it on [RosterSnapshot.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/RosterSnapshot.cs) so views stay SDK-free.
3. **Per-player instead?** If any member must be able to write it, it has to be player data (`UpdatePlayerAsync`) — `UpdateLobbyAsync` is host-only. Derive the shared value from the members, stamped, like [TeamColorOpinion.cs](Assets/Game/Scripts/Core/Multiplayer/Lobby/Data/TeamColorOpinion.cs).
4. **Publish it** through a new `DebouncedPublish<T>` in `LobbyPlayerPublisher` plus a `PublishX` passthrough on `LobbySession` — do **not** route it through `TryBegin`.
5. **Host-only control:** go through `RequireHost` + `UpdateLobbyAsync`, and refuse (via `Fail`) rather than silently displacing anyone, the way `SetTeamRulesAsync` checks `VersusRules` against live occupancy.
6. **New page or screen:** a flow class (service calls, no rects) + a view class (rects, reads a `RosterSnapshot`, no service). Register the page in `LobbyUI.NewPage` so the outgoing one is disposed and deactivated before `Destroy`, wire waits through `LobbyBusyScope`/`LobbyBusyState` and `MenuStatusLine`, and lock with `MenuLock`.
7. **Guard it.** EditMode tests: [LobbyOptionsTests.cs](Assets/Game/Editor/Tests/LobbyOptionsTests.cs), [VersusLobbyDataTests.cs](Assets/Game/Editor/Tests/VersusLobbyDataTests.cs), [LobbyJoinRecoveryTests.cs](Assets/Game/Editor/Tests/LobbyJoinRecoveryTests.cs), [LobbyServiceErrorsTests.cs](Assets/Game/Editor/Tests/LobbyServiceErrorsTests.cs), [LobbyRosterViewTests.cs](Assets/Game/Editor/Tests/LobbyRosterViewTests.cs), [LobbyAutoRefreshTests.cs](Assets/Game/Editor/Tests/LobbyAutoRefreshTests.cs), [LobbyRouteTests.cs](Assets/Game/Editor/Tests/LobbyRouteTests.cs), [LobbyMenuWiringTests.cs](Assets/Game/Editor/Tests/LobbyMenuWiringTests.cs).
8. **Then prove it on two machines** — anything touching the service cannot be proven by a host alone. See the [spacegame-multiplayer skill](.claude/skills/spacegame-multiplayer/SKILL.md).
