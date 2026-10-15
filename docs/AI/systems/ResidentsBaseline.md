---
system: ResidentsBaseline
layer: pipeline
summary: "One recorded in-game day of a real settlement, per resident and plan segment, to hold residents rewrites to"
paths:
  - Assets/Game/Editor/Agents/ResidentsBaseline.cs
  - Assets/Game/Scripts/agents/Diagnostics/ResidentsBaselineRun.cs
  - Assets/Game/Scripts/agents/Diagnostics/ResidentsBaselineSettings.cs
  - Assets/Game/Scripts/agents/Diagnostics/ResidentBaselineTrack.cs
  - Assets/Game/Scripts/agents/Diagnostics/ResidentBaselineCounts.cs
symptoms:
  - "how do I check a change to the residents still gives the same settlement day"
  - "Temp/residents_baseline_summary.txt says FAILED or PARTIAL"
  - "the residents baseline observer moved hundreds of metres by the end"
  - "the residents baseline says conversations opened: 0"
reads_with: [Residents, Errands, Diagnostics, WorldStreaming]
updated: 2026-10-02
---

# Residents baseline

## Model
A play-mode harness that plays **one in-game day** of the `Chunk_6_3` settlement offline and writes what every resident
did, per plan segment, so the agent restructure (Phase 5) — or any rewrite of [Residents](Residents.md) — is compared
against the day the old code produced. Run it before and after the change with the **same settings**, diff the CSVs.

| Output | What |
|---|---|
| `Temp/residents_baseline.csv` | one row per resident per segment boundary (columns below — the comparison contract) |
| `Temp/residents_baseline_summary.txt` | totals and the outcome; last line `DONE` (`status: OK`, `PARTIAL — <why>` with the counts so far, or `FAILED — <why>` before sampling began). `Run` deletes both files first, so no file = still running |

## Key types
| Type | Role |
|---|---|
| `ResidentsBaseline` (Editor) | menu `Tools/Agents/Run Residents Baseline`, `Run()` / `Run(settings)`; a `PlayModeHarness` with no scene staging ([Diagnostics.md](Diagnostics.md)) |
| `ResidentsBaselineSettings` | every tunable: world scene, settlement chunk, step, `dayLengthSeconds` 600, `startDay` 1, `startHour` 5, `sampleSeconds` 0.25, `reachedWithin` 3 m, observer offset, seeded deed, timeouts, wall-clock cap 90 min, output paths |
| `ResidentsBaselineRun` | the run (Flows); counts errors logged after the world scene loads |
| `ResidentBaselineTrack` | one resident: tracks its current segment, writes rows; **`ReadRoutine` is the only reader of the routine** |
| `ResidentBaselineCounts` | rows per boundary, segments closed/reached per activity |

## Flows
1. Bootstrap settles (`HarnessRun.WaitForBootstrap`); the world scene `persistentScene` is loaded Single unless it was
   the scene played. **Edit-time scenes are never touched** — unsaved edits survive, play mode restores them.
2. A streaming anchor at the chunk's centre (`WorldStreamingConfig.chunks`); wait for a `Settlement` in that scene whose
   `Society` has residents.
3. Observer: the offline `PlayerCharacter`, once the ground under its spawn is loaded, is moved by `SaveTeleport` to
   `WalkableHeart + observerOffset` snapped to the NavMesh, suit oxygen off. Resident pairs only talk with a player in
   earshot (`ObserverCheck`), so without it `conversations opened` is 0.
4. `captureDeltaTime = 1/60`; `cycleDuration = dayLengthSeconds`; `AnchorTo(startDay, startHour)` — which rebuilds every
   plan. Seeded deed: the first resident with family holds a first-hand `Hit` on itself by profile
   `residents-baseline-observer` (no player has it), so midnight gossip has something to spread and no attitude to a real
   player changes.
5. Every `sampleSeconds` each track reads its segment (`society.PlanFor(r).At(now)`), distances, presence, counters; rows
   are appended to the CSV as they happen. At 24 game hours it cuts open segments, writes the summary, and the editor
   leaves play mode. Stopped early (play mode ended, a throw, the wall-clock cap), it keeps the rows and says PARTIAL.

**CSV columns — never change what a column means; only append:**

| Column | Meaning |
|---|---|
| `game_minute`, `day`, `clock` | `society.NowMinutes` (absolute), `society.Day`, hh:mm |
| `boundary` | `start` · `arrive` (clock crossed the segment's `arrive`) · `leave` (plan moved on) · `replan` (segment replaced before its `leave` by a rebuild) · `cutoff` (run ended mid-segment) · `dayend` (one sample after midnight, after the society's `EndDay` gossip) |
| `resident`, `name`, `role` | `Resident.index`, `DisplayName`, `RoleName` |
| `plan_day`, `segment`, `plan_activity`, `plan_place`, `place_kind` | the `DayPlan` holding the segment (yesterday's before today's first departure), index in it, activity, place index, `SettlementPlace.Kind` |
| `routine_activity` | activity of the segment the routine follows (`ResidentRoutine.Current`; after the restructure, FollowPlan); empty = none |
| `presence_activity`, `held_place` | `ResidentPresence.Activity`; the routine's `HeldPlace` (-1 walking) |
| `dist_m`, `min_dist_m`, `reached` | distance now to the place's stand point; minimum since `arrive`; on closing rows 1 when that minimum ≤ `reachedWithin` |
| `offstage`, `dead`, `band` | `IsOffstage`, `IsDead`, `ProvocationModule.Band` |
| `errands_started`, `errands_finished`, `errand_stops` | cumulative, observed: an errand segment (Chore/Patrol/Amble) is *started* with ≥ 1 stop arrival (`AtPlace` false→true after `arrive`), *finished* if it still showed that errand at `leave`; stops = arrivals |
| `carries`, `lines_said` | presence prop 0→non-zero changes; `ResidentVoice.LinesSaid` |
| `deeds_known`, `deeds_heard` | `ResidentMemory.Deeds` count; how many were heard second-hand |
| `conversations_opened` | `SettlementSociety.ConversationsOpened`, settlement-wide, cumulative |
| `x`, `z` | world position |

## Multiplayer
Offline only (no session): the run measures the deciding machine's behaviour. What clients see is not covered.

## Persistence
None. No `WorldSession` is active offline, so nothing is saved; the seeded deed lives only in the run.

## Gotchas
- **Compare only equal settings.** Walking times are game minutes, so `dayLengthSeconds` changes the plan itself, and
  `startDay` seeds it. A changed default makes every earlier CSV incomparable.
- **Check `places … (usable N)` in the summary first.** Over a world NavMesh that does not match the settlement, every spot
  is "an island", the planner is left with doors and the day is `Break` at the door — 100 % reached, nothing measured
  ([Residents.md](Residents.md) Gotchas).
- **The observer is parked by `UnderTerrainGuard` until its spawn ground loads, and a park restores the parked position**:
  a teleport during the park is undone (first run: "moved 452 m by the end"). The run waits for `IsGroundLoadedAround`.
- **A run stopped by play mode ending has no `cutoff` rows** (the residents are already destroyed); its summary uses the
  values last sampled. Stop it from the editor's Play button / `Edit ▸ Play Mode ▸ Play` — the bridge cannot (above).
- **Entering play mode kills anybody else's EditMode test run**, as the benchmark does — check before starting.
- **Speed depends on what the residents do**: 30 fps unfocused while they stood at their doors, ~11 fps once they walked
  to work — a 600 s day is 36,000 frames, ~55 min. Raise `maxWallClockMinutes` rather than the step if it comes back PARTIAL.
- **Another session's script edit does not stop the run** (the editor recompiles after play), but the bridge then refuses
  every command with `COMPILATION_IN_PROGRESS` until play mode ends — a running baseline cannot be stopped over the bridge.

## Extending
- Another settlement: `settlementScene` (and `worldScene` for another world) in the settings.
- New column: append it to `CsvHeader` and `WriteRow` together; document it here.
- When the routine moves (Phase 5): re-point `ResidentBaselineTrack.ReadRoutine` (and `Conversations.Opened` if talks
  move to `Encounter`); keep the columns.
