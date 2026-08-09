# Leaderboard & Loading Screen — Design

Date: 2026-08-06

Two features for the minigame//startup flow:
1. A per-player leaderboard, live during a match (hold Tab) and pinned open on the result screen.
2. A loading screen that stays up until the world is genuinely ready, removing the first-seconds
   stutter.

---

## 1. Leaderboard

### Problem
`MatchManager` only counts kills **per team** (`killsByTeam`), because that is all the win
conditions need. A leaderboard needs per-*entity* kills and deaths, plus a display name, plus a
way for clients to see the same numbers the server computed.

### Data
New `MatchScoreEntry : INetworkSerializable` (`Assets/Scripts/Minigame/MatchScoreEntry.cs`):

| Field | Notes |
|---|---|
| `Name` | `FixedString32Bytes` — "You" resolves client-side, so this is "Player 2" / "Bot 07" |
| `Kills` / `Deaths` | per entity |
| `Team` | for team-coloured rows in Team Deathmatch |
| `ClientId` | `NoClient` (`ulong.MaxValue`) for bots; lets each peer find its own row |

`MatchManager` gains `killsByEntity`, `deathsByEntity`, `nameByEntity`, keyed on `GameObject` to
match the existing `entityTeam` style.

### Replication
Server-authoritative, broadcast on change. Scores change only on a death, so an RPC carrying the
whole table is cheaper and far simpler than a `NetworkList` with per-element deltas.

- `MatchManager.Scores` — `static IReadOnlyList<MatchScoreEntry>`, mirroring how `LocalTeamIndex`
  is already exposed to UI on every peer.
- `MatchManager.OnScoresChanged` — `static event Action`, so the UI refreshes without polling.
- Reset in `OnNetworkSpawn` alongside `LocalTeamIndex`.

### UI
`MatchLeaderboardUI` (`Assets/Scripts/UI/Pages/MatchLeaderboardUI.cs`), built at runtime in the
same style as `MatchResultUI` — the arena scene carries no UI GameObjects, so a scene-authored
panel would have to be duplicated per arena.

- Visible while **Tab is held**, or while **pinned**.
- `SetPinned(true)` is called by `MatchResultUI` when the match ends.
- Rows sorted kills desc → deaths asc → name. The local player's row is highlighted.
- Rows are pooled and reused; the table is rebuilt only when it is visible and the scores changed.

`MatchResultUI`'s generated layout moves from "everything stacked in the centre" to headline
top-anchored / button bottom-anchored, leaving the middle free for the table. That reads fine with
or without a leaderboard present, and avoids fragile centre-offset arithmetic.

---

## 2. Loading screen

### What the lag actually is
`NetworkGameManager.SpawnPlayerWhenReady` already waits for `WorldStreamer.IsReady` and preloads
chunks around the spawn point before spawning the player. The remaining first-seconds cost is the
NavMesh bake plus first-frame shader/pipeline warmup, which happen *after* the scene load
completes and while the player already has control.

So covering the scene load alone would not remove the stutter — the screen has to stay up until
the world reports ready.

### Gate
`LoadingScreenUI` (`Assets/Scripts/UI/Pages/LoadingScreenUI.cs`), `DontDestroyOnLoad`, sorting
order above everything. `Show()` is called before the scene load starts; it then waits for, in
order:

1. the named scene to be loaded and active (skipped when no name is given),
2. the local player object to exist (skipped when not networked),
3. `WorldStreamer.InitialChunksLoaded`, if a `WorldStreamer` exists at all,
4. a few rendered frames, to absorb the first-frame shader compile hitch.

A **timeout** (default 30 s) dismisses it regardless, so a streaming failure degrades to "you can
play" rather than "the game is stuck on a loading screen". Timing out logs a warning naming the
stage it was waiting on.

The status line names the current stage rather than showing a fake progress bar — none of these
stages report meaningful progress, and an honest label beats a lying bar.

### Hook-up
`MainMenuUI.StartSinglePlayer()` and `MainMenuUI.LaunchMinigame()` show it before `StartHost()`,
passing the scene they expect to end up in (`gameScene` and `minigameScene` respectively).

---

## Implementation notes

Both assemblies type-check clean. **Not play-tested.**

### Files
| File | Change |
|---|---|
| `Minigame/MatchScoreEntry.cs` | new — one leaderboard row, `INetworkSerializable` |
| `Minigame/MatchManager.cs` | per-entity kills/deaths/names, `Scores`, `OnScoresChanged`, `BroadcastScoresRpc` |
| `UI/Pages/MatchLeaderboardUI.cs` | new — Tab-held / pinnable table |
| `UI/Pages/MatchResultUI.cs` | pins the leaderboard on match end; layout moved to top/bottom anchors |
| `UI/Pages/LoadingScreenUI.cs` | new — the readiness gate |
| `UI/Pages/MainMenuUI.cs` | shows the loading screen on both entry points |

### Decisions worth remembering
- **Kills are only credited across teams.** Friendly fire and suicides score for nobody on either
  the team tally or the personal one, so the leaderboard can't be farmed by shooting allies.
- **Names are fixed at spawn**, and "You" is resolved per-peer by comparing `ClientId`. One table
  is therefore correct for everyone, and labels don't reshuffle mid-match.
- **Score broadcasts are suppressed during initial spawn** and published once when the roster is
  complete — otherwise a 16-bot match opens with 16 RPCs carrying near-identical tables.
- **The leaderboard canvas sorts *above* the result screen (1100 vs 1000)** because the result
  screen's panel is a full-screen backdrop that would otherwise hide the table completely.
- **The leaderboard canvas has no `GraphicRaycaster`.** It is display-only, so clicks fall through
  to the result screen's button underneath.
- **Terrain streaming gets its own 15 s budget, and timing out means "carry on".** By that point
  the scene and player are both ready, so the game is playable; a streamer that never reports in
  should cost a rough few seconds, not a 30 s black screen.

### Unverified by the offline type-check
Netcode's RPC **source generator** does not run outside the editor, so it is the one thing the
offline compile cannot validate. `MatchScoreEntry[]` was checked by hand against
`FastBufferWriter.WriteValue<T>(T[], ForNetworkSerializable) where T : INetworkSerializable`,
which is the overload the generator emits — so it should be fine, but a first-domain-reload error
would most likely come from `BroadcastScoresRpc`.

## Out of scope
- Persistent/cross-session stats. There is no backend, and nothing asked for it.
- Assists, streaks, damage totals. Kills and deaths only.
- A loading screen for the multiplayer lobby path (`StartMultiPlayer` → lobby scene); that route
  loads a menu scene, not the world.
