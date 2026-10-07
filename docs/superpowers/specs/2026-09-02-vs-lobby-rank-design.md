# VS lobby rank: many teams, small screens, real ground

**Date:** 2026-09-02
**Status:** approved
**Area:** `Presentation/UI/Lobby/Rank/`, `Gameplay/Versus/Core/`
**Docs governed by:** [Lobby.md](../../AI/systems/Lobby.md), [GameModes.md](../../AI/systems/GameModes.md)

## Problem

The VS lobby draws its roster as a rank of astronauts standing in `MainMenu.unity`. Three
independent defects make it unusable past a handful of teams or on a small window.

### 1. The camera pays for every team in astronaut pixels

`LobbyPreviewCamera.Fit` frames the rank by retreating along the authored view's backward axis
until `RankLayout.TotalWidth(teams, teamSize)` fits the horizontal FOV. Teams stand in a single
line, so width grows linearly with team count and the figures shrink to match.

At 8 teams x 3 on a 1366x768 window:

| | value |
| --- | --- |
| rank width | 45.6 m (22.4 m of it empty `TeamGap` sand) |
| camera distance | 26.7 m |
| astronaut height | 45 px |
| between team centres | 152 px |
| between adjacent people | 36 px |

### 2. The overlays never shrink

`LobbyTeamPlates` is built at a fixed `PlateWidth 520` / `PlateSize 46`, and `LobbyNameplates` at
`RowWidth 600` / `NameSize 40`, in canvas pixels. Nothing reads the projected spacing. Team plates
therefore collide from **4 teams up** (448 px of pitch under a 520 px plate) and names collide far
earlier.

### 3. Nobody is standing on the ground

`RankLayout.SeatPosition` always returns local `y = 0` and `LobbyRankFigures.Seat` assigns it as
`localPosition`, so every figure stands on the **anchor's plane**, not on the sand. At 2 teams the
6 m line is short enough for the dune to be flat under it. At 8 teams it is a 45.6 m line and
figures already float and sink today.

### Aggravating factor: aspect, not resolution

`Fit` derives horizontal FOV from `camera.aspect`. A 16:10 or 4:3 window has a narrower hFOV, so
the same rank pushes the camera roughly 33% further back than 16:9 — then renders it into fewer
pixels. Only the horizontal axis is considered at all; the vertical band the rank actually owns
(between the title block and the status line) is never accounted for.

## Decisions taken

| Question | Decision |
| --- | --- |
| Show all teams, or page / demote them? | **Show all teams.** Camera and text scale with the team count. |
| How does the rank arrange itself? | **Teams wrap at 4 per row**, mirroring `MaxSeatsPerRow`. 2-4 teams are unchanged. |
| Where does the rank stand? | **Keep the authored anchor.** The code copes with whatever ground is there. |
| Colour-only team identity at the smallest size? | **No.** The last rung of the label ladder is still a word plus a count. |

Rejected: paging the rank (loses "see the whole lobby", adds an interaction to a page that has
none) and demoting the rank to a 2D team bar (loses the astronauts, which are the point of the
page).

## Design

### Geometry: teams wrap

`RankLayout` gains team-level wrapping, the same rule it already applies to seats inside a team:

- `MaxTeamsPerRow = 4`.
- Team row `r` is offset in local `+Z` by `TeamRowSpacing` (~6 m), behind the row in front.
- Odd rows are offset laterally by **half a team pitch**, so no back-row team is ever directly
  behind a front-row one. Where a team is no wider than the 3.2 m `TeamGap` — teams of three or
  fewer — it lands in the gap with no lateral overlap at all; wider teams still overlap in
  silhouette, which is what a crowd behind a crowd looks like.
- `TeamCenter` returns a `Vector3` carrying that `z` and stagger; `SeatPosition` adds the seat's own
  in-team row on top.
- New `TotalDepth(teams, teamSize)` beside `TotalWidth`.

Measured out of the built `RankLayout`, on a 1366x768 window:

| shape | width | camera | figure | team pitch |
| --- | --- | --- | --- | --- |
| 8 x 3, before | 45.6 m | 26.7 m | 45 px | 152 px |
| 8 x 3, after | 24.3 m | 14.2 m | **84 px** | **286 px** |
| 4 x 6 (one row, unchanged) | 27.0 m | 15.8 m | 76 px | 318 px |
| 2 x 2 (authored shot) | 6.1 m | no pull-back | 336 px | 868 px |

A 1.87x gain in figure size at 8 teams. Note the stagger costs half a pitch of extra width, so the
wrapped rank is 24.3 m rather than the 21.2 m a naive two-row estimate gives — and 4 x 6, which
stays in one row, is now the widest shape the rules allow rather than 8 x 3.

**`teams <= 4` must produce positions identical to today.** This is a pinned regression test, not
an aspiration.

**Every team sits on one shared half-pitch lattice**, re-centred once. Centring each row on itself
and then staggering it looks equivalent and is not: with five teams the two corrections cancel and
the lone back team lands exactly behind a front one. The cost is that a short back row fills slots
from the left rather than being centred under the row in front.

### Grounding: every seat probed onto the sand

`RankLayout.SeatPosition` stays pure and keeps returning flat local `y = 0`. A new pure
`RankGrounding.Solve(seats, probe)` takes the local seats and a probe delegate and returns their
grounded positions plus the measured bounds.

The Unity side supplies the probe: cast from `seat + up * 30 m` down `100 m` against a ground layer
mask with `QueryTriggerInteraction.Ignore` — the same probe
`LobbyPreviewSetup.EnsureAnchor` already uses to place the anchor. A miss falls back to the anchor
plane, which is today's behaviour, so a scene with no ground colliders degrades rather than breaks.

Feasibility is already established: the saved anchor sits at `y = 3.196` against a camera view at
`y = 4.585`, a 1.389 m gap rather than the 1.6 m no-hit fallback. **The menu ground has real
colliders.**

The layer mask exists to avoid the ruin and set dressing, **not** other astronauts —
`LobbyPreviewSetup` strips every collider from the preview prefab, so figures cannot be hit.

Re-solved only when the seat set changes (team shape or roster length), not on every 2 Hz poll.

This also fixes the story lobby's single line, which has the same flat-plane bug.

### Camera: fit the band the rank owns, in both axes

`LobbyPreviewCamera.Fit` currently takes `(anchor, teams, teamSize)` and fits width alone. It will
instead take the **measured bounds** from `RankGrounding` and the **screen band** the rank owns:

- Horizontal: full frame width, as today.
- Vertical: the frame minus the bottom chrome (`MenuEntry.MessageBottom` plus the status line's
  own height). The **top** is not reserved: a team plate at `PlateLift` projects above the page
  title in the authored shot and always has — the title is a left-aligned column and the plates are
  centred over their teams. Measured against the live canvas height, which is
  `1920 * Screen.height / Screen.width` because the scaler matches width. This is what makes short
  and narrow windows work.
- The needed distance is the larger of the two axes' requirements.
- **Eye lift.** When more than one team row exists the eye must clear head height, or the back row
  is entirely occluded. Today's eye is 1.389 m above the anchor — *below* a 1.8 m head. At the
  measured 14.2 m with a 6 m row gap, the eye goes to 2.63 m, clearing a front-row head by 0.35 m.
  A lift also re-aims the camera at the rank, so it frames the astronauts rather than sliding them
  out of the bottom of the shot; with one row the lift is zero and the authored pose is exact.
- The existing promise is kept: the camera is only ever pushed **further back** than the authored
  pose, never pulled in.

### Overlays: one scale, one ladder

New pure `RankOverlayScale` converts pixels-per-metre at an overlay's own depth (the back row is
further away, so it scales separately) into a font size and a ladder rung.

Team plates:

| Rung | Shows |
| --- | --- |
| Roomy | `TEAM THREE` at authored size |
| Scaled | `TEAM THREE`, font scaled to the pitch |
| Shortened | `THREE 2/3` |
| Floor | `3 · 2/3` |

Names: full name -> your own team and the host -> you and the host. **Never none** — a player must
always be able to find themselves in the rank.

The floor is still a word and a count. Team identity is never carried by colour alone
(`GDC-L1-UX-0003`, `GDC-L1-UX-0006`).

## Where the code goes

Pure, in `Gameplay/Versus/Core/` (asmdef `SpaceGame.Versus.Core`, tested from
`Assets/Game/Tests/EditMode/`):

| Class | Status |
| --- | --- |
| `RankLayout` | extend — team wrap, stagger, `TotalDepth`, fit maths incl. eye lift |
| `RankGrounding` | new — `Solve(seats, probe)` -> grounded positions + bounds |
| `RankOverlayScale` | new — projected spacing -> font size + ladder rung |

Unity-side, in `Presentation/UI/Lobby/Rank/` (Assembly-CSharp, tested from
`Assets/Game/Editor/Tests/`):

| Class | Change |
| --- | --- |
| `LobbyPreviewRank` | supplies the physics probe; caches the grounding solve |
| `LobbyRankFigures` | `Seat` takes a **world** position instead of a local one |
| `LobbyPreviewCamera` | `Fit` takes measured bounds + the owned screen band; applies eye lift |
| `LobbyTeamPlates` | sizes and relabels from `RankOverlayScale` |
| `LobbyNameplates` | sizes and thins from `RankOverlayScale` |

## Non-negotiables

**Multiplayer.** Presentation only — no RPCs, no new network state, nothing added to the prefab
list. The layout is deterministic from `(teams, teamSize, slot)` off `RosterSnapshot`, which every
peer already computes identically, so peers agree by construction. Still verified on a real client,
because a client's `LocalTeam` differs and that drives the name ladder and the plate dimming.

**Persistence.** Nothing here is saved. Lobby state lives on the service and dies with the session
(see `Lobby.md`). Stated explicitly rather than skipped.

**Code smells.** Tunables are documented `const`s in the pure layout classes, matching the
convention `RankLayout` already sets (a static geometry class has no Inspector to serialize into).
The ground probe reuses the existing `EnsureAnchor` idiom rather than inventing a second one. No
class gains a second responsibility.

## Tests

EditMode (`Assets/Game/Tests/EditMode/RankLayoutTests.cs` and new files):

- `teams <= 4` produce positions identical to the pre-change layout (regression pin).
- 5-8 teams wrap to two rows; the back row is staggered by half a team pitch.
- No two teams overlap in projection at any legal `(teams, teamSize)` the rules allow.
- `TotalDepth` grows with rows; `TotalWidth` stops growing past `MaxTeamsPerRow`.
- `RankGrounding` falls back to the anchor plane when the probe misses, and reports bounds spanning
  every grounded seat.
- `RankOverlayScale` rungs are monotonic in available pitch, and the floor rung still carries a
  word.
- The fit never pulls the camera in past the authored pose; it satisfies both axes; it reserves the
  chrome band; a narrower aspect never produces a *smaller* distance than a wider one.

Editor (`Assets/Game/Editor/Tests/`), for anything touching uGUI types.

Manual: host a VS lobby at 2x2 (must look unchanged), 4x6, and 8x3, on a 16:9 and a 4:3 window,
then join from a second instance with `-sgprofile client` and confirm the client's own team reads
correctly.

## Documentation

- `docs/AI/systems/GameModes.md` — the `RankLayout` row (currently "seat spacing, 4-wide wrap, team
  gap, camera pull-back").
- `docs/AI/systems/Lobby.md` — the `LobbyPreviewRank` row and `Gotchas`; add `symptoms:` entries for
  *"astronauts float above or sink into the sand"* and *"team names overlap with more than four
  teams"*.
- Regenerate: `python3 tools/docs_check.py --index`.
- No Human-doc change: the shape of the system does not move.
