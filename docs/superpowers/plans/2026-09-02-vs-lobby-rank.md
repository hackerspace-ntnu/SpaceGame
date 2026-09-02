# VS Lobby Rank Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Commits in this repo:** a commit hook blocks `git commit` unless the user asks for a commit in
> that turn. The commit steps below are written out for completeness, but an agent must **not** run
> them unprompted — stage nothing, and tell the user the work is ready to commit instead.

**Goal:** Make the VS lobby's rank of astronauts legible at 8 teams and on small windows, and make
every astronaut stand on the actual sand.

**Architecture:** All new geometry is pure and lives in `SpaceGame.Versus.Core` (no `MonoBehaviour`,
EditMode-testable). The Unity classes under `Presentation/UI/Lobby/Rank/` become thin: they supply a
physics probe and a camera, and apply what the pure classes decide. Teams wrap at 4 per row the way
seats already wrap inside a team; every seat is raycast onto the ground; the camera fits both axes
of the band it owns and lifts its eye when a second row exists; every overlay sizes itself from its
own projected spacing.

**Tech Stack:** Unity (C#), NUnit EditMode tests, TextMeshPro, uGUI.

---

## Spec

[docs/superpowers/specs/2026-09-02-vs-lobby-rank-design.md](../specs/2026-09-02-vs-lobby-rank-design.md)

## File structure

| File | Assembly | Responsibility |
| --- | --- | --- |
| `Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs` | `SpaceGame.Versus.Core` | **modify** — team wrap, stagger, depth, two-axis fit, eye height |
| `Assets/Game/Scripts/Gameplay/Versus/Core/RankGrounding.cs` | `SpaceGame.Versus.Core` | **new** — probe seats onto ground, report height spread |
| `Assets/Game/Scripts/Gameplay/Versus/Core/RankOverlayScale.cs` | `SpaceGame.Versus.Core` | **new** — projected spacing to font size + ladder rung |
| `Assets/Game/Scripts/Gameplay/Versus/Core/VersusRules.cs` | `SpaceGame.Versus.Core` | **modify** — `ShortTeamName` |
| `Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyRankFigures.cs` | Assembly-CSharp | **modify** — seat by world position |
| `Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyPreviewRank.cs` | Assembly-CSharp | **modify** — own the grounding solve, pass scale to overlays |
| `Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyPreviewCamera.cs` | Assembly-CSharp | **modify** — two-axis fit, eye lift, re-aim |
| `Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyTeamPlates.cs` | Assembly-CSharp | **modify** — adaptive label ladder |
| `Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyNameplates.cs` | Assembly-CSharp | **modify** — adaptive size + thinning |
| `Assets/Game/Tests/EditMode/RankLayoutTests.cs` | `SpaceGame.Tests.EditMode` | **modify** — wrap, stagger, fit, eye height |
| `Assets/Game/Tests/EditMode/RankGroundingTests.cs` | `SpaceGame.Tests.EditMode` | **new** |
| `Assets/Game/Tests/EditMode/RankOverlayScaleTests.cs` | `SpaceGame.Tests.EditMode` | **new** |

`Assets/Game/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef` already references
`SpaceGame.Versus.Core` — no asmdef change is needed.

Every new `.cs` file needs a `.meta` sibling; Unity writes it on next focus. Do not hand-write one.

---

## Task 1: Teams wrap into rows

**Files:**
- Modify: `Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs`
- Test: `Assets/Game/Tests/EditMode/RankLayoutTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `public class RankLayoutTests`:

```csharp
        /// <summary>
        /// The whole promise of the wrap: the shapes people actually play must not move a
        /// millimetre. Pinned as literal expected values derived from the pre-change formula
        /// (count * TeamWidth + (count-1) * TeamGap), so a future edit to the wrap cannot quietly
        /// re-space a two-team lobby.
        /// </summary>
        [Test]
        public void FourTeamsOrFewerStandExactlyWhereTheyAlwaysDid()
        {
            for (int teams = RankLayout.MinTeamsForTest; teams <= RankLayout.MaxTeamsPerRow; teams++)
            {
                const int teamSize = 3;

                float pitch = RankLayout.TeamWidth(teamSize) + RankLayout.TeamGap;

                for (int team = 0; team < teams; team++)
                {
                    Vector3 centre = RankLayout.TeamCenter(team, teams, teamSize);
                    float expected = (team - (teams - 1) * 0.5f) * pitch;

                    Assert.AreEqual(expected, centre.x, 0.0001f, $"{teams} teams, team {team}");
                    Assert.AreEqual(0f, centre.z, 0.0001f, "a single row must not be pushed back");
                }

                Assert.AreEqual(teams * RankLayout.TeamWidth(teamSize) + (teams - 1) * RankLayout.TeamGap,
                                RankLayout.TotalWidth(teams, teamSize), 0.0001f);
            }
        }

        [Test]
        public void FiveTeamsWrapToASecondRow()
        {
            Assert.AreEqual(1, RankLayout.TeamRowsFor(RankLayout.MaxTeamsPerRow));
            Assert.AreEqual(2, RankLayout.TeamRowsFor(RankLayout.MaxTeamsPerRow + 1));
        }

        [Test]
        public void TheBackRowStandsBehindTheFrontRow()
        {
            const int teams = 8;
            const int teamSize = 3;

            float front = RankLayout.TeamCenter(0, teams, teamSize).z;
            float back = RankLayout.TeamCenter(RankLayout.MaxTeamsPerRow, teams, teamSize).z;

            Assert.Greater(back, front, "the second row of teams is not behind the first");
        }

        /// <summary>
        /// The stagger is what stops a back team hiding behind a front one. Asserted as a real
        /// clearance in metres between the two blocks, not as the offset that produces it — an
        /// offset that stopped clearing the block would still equal itself.
        /// </summary>
        [Test]
        public void ABackRowTeamStandsInTheGapBetweenTwoFrontRowTeams()
        {
            const int teams = 8;
            const int teamSize = 3;

            float half = RankLayout.TeamWidth(teamSize) * 0.5f;

            for (int back = RankLayout.MaxTeamsPerRow; back < teams; back++)
            {
                float backX = RankLayout.TeamCenter(back, teams, teamSize).x;

                for (int front = 0; front < RankLayout.MaxTeamsPerRow; front++)
                {
                    float frontX = RankLayout.TeamCenter(front, teams, teamSize).x;

                    Assert.Greater(Mathf.Abs(backX - frontX), RankLayout.TeamWidth(teamSize),
                                   $"back team {back} overlaps front team {front} across the line");
                }

                Assert.IsTrue(half > 0f);
            }
        }

        [Test]
        public void WrappingStopsTheRankGettingWider()
        {
            const int teamSize = 3;

            float four = RankLayout.TotalWidth(4, teamSize);
            float eight = RankLayout.TotalWidth(8, teamSize);

            Assert.Less(eight, four * 1.6f, "eight teams should not be far wider than four");
        }

        [Test]
        public void TheWrappedRankIsStillCentredOnTheAnchor()
        {
            const int teams = 8;
            const int teamSize = 3;

            float min = float.MaxValue;
            float max = float.MinValue;

            for (int team = 0; team < teams; team++)
            {
                float x = RankLayout.TeamCenter(team, teams, teamSize).x;
                min = Mathf.Min(min, x);
                max = Mathf.Max(max, x);
            }

            Assert.AreEqual(0f, min + max, 0.0001f, "the wrapped rank drifts off its anchor");
        }

        [Test]
        public void TotalDepthGrowsWhenTeamsWrap()
        {
            const int teamSize = 3;

            Assert.Less(RankLayout.TotalDepth(4, teamSize), RankLayout.TotalDepth(8, teamSize));
        }
```

- [ ] **Step 2: Run the tests and watch them fail**

In the Unity Editor: `Window > General > Test Runner > EditMode > Run All`.

Expected: compile errors — `RankLayout.MinTeamsForTest`, `TeamRowsFor`, `TotalDepth` do not exist,
and `TeamCenter` returns a `Vector3` whose `.z` is always `0`.

- [ ] **Step 3: Add the wrapping constants and helpers**

In `RankLayout.cs`, after the `MaxSeatsPerRow` member, add:

```csharp
        /// <summary>
        /// How many teams stand side by side before the next one goes behind them.
        ///
        /// The same number as <see cref="MaxSeatsPerRow"/>, and for the same reason one level up: a
        /// line of eight teams is 45 m of astronaut, and no camera pull-back frames that legibly.
        /// Four abreast keeps the widest legal rank at roughly the width of a four-team one, which
        /// is the shape the shot was composed around.
        /// </summary>
        public const int MaxTeamsPerRow = 4;

        /// <summary>
        /// Metres of clear sand between one row of teams and the next, measured from the back of the
        /// front row's own seats to the front of the next row's.
        ///
        /// Generous compared with <see cref="RowSpacing"/> because these are whole groups rather
        /// than two ranks of one team: the gap has to read as "those teams are further away", and it
        /// is also the lever the camera's eye lift divides by — a tighter gap needs a higher eye to
        /// see over the front row.
        /// </summary>
        public const float TeamRowSpacing = 6f;

        /// <summary>The smallest legal team count, restated for tests that sweep the range.</summary>
        public const int MinTeamsForTest = 2;

        /// <summary>How many teams stand in a full row.</summary>
        public static int TeamsPerRow(int teams) =>
            teams < MaxTeamsPerRow ? Mathf.Max(1, teams) : MaxTeamsPerRow;

        /// <summary>How many rows of teams there are.</summary>
        public static int TeamRowsFor(int teams) =>
            Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, teams) / (float)MaxTeamsPerRow));

        /// <summary>How deep one team's own block of seats is, front row to back row.</summary>
        public static float TeamDepth(int teamSize) => (RowsFor(teamSize) - 1) * RowSpacing;

        /// <summary>Centre-to-centre distance from one row of teams to the next.</summary>
        public static float TeamRowPitch(int teamSize) => TeamDepth(teamSize) + TeamRowSpacing;
```

- [ ] **Step 4: Rewrite `TeamCenter` to wrap and stagger**

Replace the whole existing `TeamCenter` method with:

```csharp
        /// <summary>
        /// The middle of a team's block, which is where its nameplate hangs and what a player
        /// clicks to join.
        ///
        /// Teams fill a row <see cref="MaxTeamsPerRow"/> wide and then wrap behind, and a partly
        /// filled last row is centred under the ones in front — the same rule
        /// <see cref="SeatPosition"/> already applies to the seats inside a team.
        ///
        /// Rows alternate half a team pitch sideways so a back-row team stands in the
        /// <see cref="TeamGap"/> between two front-row teams rather than directly behind one. The
        /// offset is split as a quarter pitch either way rather than applied wholly to the back row,
        /// so the rank as a whole stays centred on the anchor.
        /// </summary>
        public static Vector3 TeamCenter(int team, int teams, int teamSize)
        {
            int count = Mathf.Max(1, teams);
            int perRow = TeamsPerRow(count);
            int row = team / perRow;
            int column = team % perRow;
            int inThisRow = Mathf.Min(perRow, count - row * perRow);

            // The distance from one team's centre to the next is its own width plus the gap to the
            // next block — NOT plus another seat spacing on top. Adding SeatSpacing here (as an
            // earlier draft did) widens the real gap between the two nearest seats to
            // TeamGap + SeatSpacing, which contradicts the constant's own doc: TeamGap is defined
            // as that seat-to-seat gap, not as a value the layout is free to pad further.
            float pitch = TeamWidth(teamSize) + TeamGap;

            float x = (column - (inThisRow - 1) * 0.5f) * pitch;

            if (TeamRowsFor(count) > 1)
                x += (row % 2 == 0 ? -0.25f : 0.25f) * pitch;

            return new Vector3(x, 0f, row * TeamRowPitch(teamSize));
        }
```

- [ ] **Step 5: Make `SeatPosition` respect the team's row**

In `SeatPosition`, replace the final return statement:

```csharp
            Vector3 centre = TeamCenter(team, teams, teamSize);
            return new Vector3(centre.x + x, 0f, centre.z + row * RowSpacing);
```

- [ ] **Step 6: Rewrite `TotalWidth` and add `TotalDepth`**

Replace `TotalWidth` and add `TotalDepth` after it:

```csharp
        /// <summary>
        /// The whole rank, from the leftmost seat to the rightmost.
        ///
        /// Measured across the team centres rather than derived from a formula, because the stagger
        /// means the widest row is not always the first one. For a single row this reproduces the
        /// old arithmetic exactly: count * TeamWidth + (count - 1) * TeamGap.
        /// </summary>
        public static float TotalWidth(int teams, int teamSize)
        {
            int count = Mathf.Max(1, teams);

            float min = float.MaxValue;
            float max = float.MinValue;

            for (int team = 0; team < count; team++)
            {
                float x = TeamCenter(team, count, teamSize).x;
                min = Mathf.Min(min, x);
                max = Mathf.Max(max, x);
            }

            return max - min + TeamWidth(teamSize);
        }

        /// <summary>
        /// The whole rank front to back: every row of teams, each of them as deep as one team's own
        /// block of seats.
        /// </summary>
        public static float TotalDepth(int teams, int teamSize) =>
            (TeamRowsFor(teams) - 1) * TeamRowPitch(teamSize) + TeamDepth(teamSize);
```

- [ ] **Step 7: Run the tests**

Run EditMode tests. Expected: all `RankLayoutTests` pass, including the pre-existing
`TheRankIsCentredOnTheAnchor`, `APartlyFilledLastRowIsCentredUnderTheOneAboveIt` and
`TheFullestRankStillFitsTheShot`.

If `TheFullestRankStillFitsTheShot` fails, that is expected and correct — it asserts
`width / (halfFrame * 2) == 1 / 1.2` for `TotalWidth(MaxTeams, MaxSeats / MaxTeams)`, which is still
true because it recomputes the distance from the same width. If it fails, the wrap has broken
`TotalWidth`; fix `TotalWidth`, not the test.

- [ ] **Step 8: Commit** *(hook-blocked — do not run; report readiness instead)*

```bash
git add Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs Assets/Game/Tests/EditMode/RankLayoutTests.cs
git commit -m "feat(lobby): wrap VS teams four abreast so the camera stops retreating"
```

---

## Task 2: Two-axis camera fit and eye lift

**Files:**
- Modify: `Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs`
- Test: `Assets/Game/Tests/EditMode/RankLayoutTests.cs`

- [ ] **Step 1: Write the failing tests**

Append inside `RankLayoutTests`:

```csharp
        [Test]
        public void TheTwoAxisFitTakesWhicheverAxisNeedsMoreRoom()
        {
            float wide = RankLayout.CameraDistance(20f, 2f, 90f, 50f, margin: 1.2f);
            float tall = RankLayout.CameraDistance(2f, 20f, 90f, 50f, margin: 1.2f);

            Assert.AreEqual(RankLayout.CameraDistance(20f, 90f, 1.2f), wide, 0.0001f);
            Assert.AreEqual(RankLayout.CameraDistance(20f, 50f, 1.2f), tall, 0.0001f);
        }

        /// <summary>
        /// A shorter band of screen is less room, so it can only ever push the camera further away.
        /// This is the small-window case: the same rank in a frame whose usable height has shrunk.
        /// </summary>
        [Test]
        public void ANarrowerBandNeverPullsTheCameraIn()
        {
            float roomy = RankLayout.CameraDistance(4f, 6f, 90f, 50f, margin: 1.2f);
            float cramped = RankLayout.CameraDistance(4f, 6f, 90f, 25f, margin: 1.2f);

            Assert.GreaterOrEqual(cramped, roomy);
        }

        [Test]
        public void OneRowOfTeamsNeedsNoEyeLift()
        {
            Assert.AreEqual(0f, RankLayout.EyeHeight(4, 3, distance: 12f), 0.0001f);
        }

        /// <summary>
        /// With two rows the eye has to sit above head height or the back row is simply behind the
        /// front one. Today's authored eye is 1.389 m — below a 1.8 m head — which is why this
        /// exists at all.
        /// </summary>
        [Test]
        public void TwoRowsOfTeamsLiftTheEyeAboveHeadHeight()
        {
            float eye = RankLayout.EyeHeight(8, 3, distance: 12.4f);

            Assert.Greater(eye, RankLayout.HeadHeight,
                           "the back row is occluded by the front row at this eye height");
        }

        [Test]
        public void AFurtherCameraNeedsAHigherEyeToSeeOverTheFrontRow()
        {
            float near = RankLayout.EyeHeight(8, 3, distance: 12f);
            float far = RankLayout.EyeHeight(8, 3, distance: 24f);

            Assert.Greater(far, near);
        }
```

- [ ] **Step 2: Run the tests and watch them fail**

Expected: compile errors — no five-argument `CameraDistance`, no `EyeHeight`, no `HeadHeight`.

- [ ] **Step 3: Implement**

Append to `RankLayout`, after the existing `CameraDistance`:

```csharp
        /// <summary>
        /// How tall an astronaut is, in metres. Used to decide how high the eye has to sit to see
        /// over a front-row head, not to size anything.
        /// </summary>
        public const float HeadHeight = 1.8f;

        /// <summary>
        /// How much clear air is wanted between a front-row head and the back-row head behind it,
        /// in metres. Small on purpose: this buys visibility, not separation — the stagger in
        /// <see cref="TeamCenter"/> is what actually keeps the two apart.
        /// </summary>
        public const float BackRowClearance = 0.35f;

        /// <summary>
        /// The distance that holds a rank <paramref name="width"/> metres across and
        /// <paramref name="height"/> metres tall, whichever needs more room.
        ///
        /// Two axes rather than one because the horizontal answer alone is what makes a short or
        /// narrow window frame the rank badly: a camera fitted on width only ignores the fact that
        /// the usable band of screen has got shorter.
        /// </summary>
        public static float CameraDistance(float width, float height, float horizontalFovDegrees,
            float verticalFovDegrees, float margin) =>
            Mathf.Max(CameraDistance(width, horizontalFovDegrees, margin),
                      CameraDistance(height, verticalFovDegrees, margin));

        /// <summary>
        /// How high above the front row's ground the eye must sit for the back row to be visible
        /// over it, in metres. Zero when there is only one row and no lift is needed.
        ///
        /// <para>
        /// From similar triangles: an eye at height <c>h</c> looking past a head at
        /// <see cref="HeadHeight"/>, <paramref name="distance"/> away, clears the ground a further
        /// <c>rowPitch</c> back by <c>(h - HeadHeight) * rowPitch / distance</c>. Setting that to
        /// <see cref="BackRowClearance"/> and solving for <c>h</c> is the line below — which is why
        /// a further camera needs a higher eye, not a lower one.
        /// </para>
        /// </summary>
        public static float EyeHeight(int teams, int teamSize, float distance)
        {
            if (TeamRowsFor(teams) <= 1) return 0f;

            float rowPitch = TeamRowPitch(teamSize);

            if (rowPitch <= 0.01f || distance <= 0.01f) return HeadHeight + BackRowClearance;

            return HeadHeight + BackRowClearance * distance / rowPitch;
        }
```

- [ ] **Step 4: Run the tests**

Expected: PASS.

- [ ] **Step 5: Commit** *(hook-blocked — do not run)*

```bash
git add Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs Assets/Game/Tests/EditMode/RankLayoutTests.cs
git commit -m "feat(lobby): fit the rank on both axes and lift the eye over a second row"
```

---

## Task 3: Ground every seat

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Versus/Core/RankGrounding.cs`
- Test: `Assets/Game/Tests/EditMode/RankGroundingTests.cs`

- [ ] **Step 1: Write the failing test file**

Create `Assets/Game/Tests/EditMode/RankGroundingTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Standing the rank on the sand rather than on the anchor's plane.
    ///
    /// The probe arrives as a delegate so this runs with no scene, no colliders and no physics —
    /// the same trick LobbyJoinRecovery uses to test a service call without the service.
    /// </summary>
    public class RankGroundingTests
    {
        private static Vector3[] Line(int count)
        {
            var seats = new Vector3[count];
            for (int i = 0; i < count; i++) seats[i] = new Vector3(i, 0f, 0f);
            return seats;
        }

        [Test]
        public void EverySeatIsPutOnTheGroundTheProbeFound()
        {
            GroundedRank rank = RankGrounding.Solve(Line(3), fallbackY: 0f,
                                                    (Vector3 seat, out float y) =>
                                                    {
                                                        y = seat.x * 0.5f;
                                                        return true;
                                                    });

            Assert.AreEqual(0f, rank.Positions[0].y, 0.0001f);
            Assert.AreEqual(0.5f, rank.Positions[1].y, 0.0001f);
            Assert.AreEqual(1f, rank.Positions[2].y, 0.0001f);
        }

        [Test]
        public void ASeatWithNoGroundUnderItFallsBackToTheAnchorPlane()
        {
            GroundedRank rank = RankGrounding.Solve(Line(2), fallbackY: 3.196f,
                                                    (Vector3 seat, out float y) =>
                                                    {
                                                        y = 999f;
                                                        return false;
                                                    });

            Assert.AreEqual(3.196f, rank.Positions[0].y, 0.0001f);
            Assert.AreEqual(3.196f, rank.Positions[1].y, 0.0001f);
        }

        [Test]
        public void TheHeightSpreadCoversEverySeat()
        {
            GroundedRank rank = RankGrounding.Solve(Line(3), fallbackY: 0f,
                                                    (Vector3 seat, out float y) =>
                                                    {
                                                        y = seat.x == 1f ? 5f : 1f;
                                                        return true;
                                                    });

            Assert.AreEqual(1f, rank.MinY, 0.0001f);
            Assert.AreEqual(5f, rank.MaxY, 0.0001f);
            Assert.AreEqual(4f, rank.HeightSpread, 0.0001f);
        }

        [Test]
        public void TheSeatsOwnXAndZAreLeftAlone()
        {
            GroundedRank rank = RankGrounding.Solve(new[] { new Vector3(2f, 0f, 7f) }, fallbackY: 0f,
                                                    (Vector3 seat, out float y) => { y = 1f; return true; });

            Assert.AreEqual(2f, rank.Positions[0].x, 0.0001f);
            Assert.AreEqual(7f, rank.Positions[0].z, 0.0001f);
        }

        [Test]
        public void AnEmptyRankReportsNoSpreadRatherThanThrowing()
        {
            GroundedRank rank = RankGrounding.Solve(System.Array.Empty<Vector3>(), fallbackY: 2f,
                                                    (Vector3 seat, out float y) => { y = 0f; return true; });

            Assert.AreEqual(0, rank.Positions.Length);
            Assert.AreEqual(0f, rank.HeightSpread, 0.0001f);
            Assert.AreEqual(2f, rank.MinY, 0.0001f);
            Assert.AreEqual(2f, rank.MaxY, 0.0001f);
        }
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Expected: compile error — `RankGrounding` and `GroundedRank` do not exist.

- [ ] **Step 3: Create `RankGrounding.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>Where the rank actually stands, once the ground has had its say.</summary>
    public readonly struct GroundedRank
    {
        /// <summary>One world position per seat, in the order they were handed in.</summary>
        public readonly Vector3[] Positions;

        /// <summary>The lowest and highest ground the rank stands on.</summary>
        public readonly float MinY;
        public readonly float MaxY;

        public GroundedRank(Vector3[] positions, float minY, float maxY)
        {
            Positions = positions;
            MinY = minY;
            MaxY = maxY;
        }

        /// <summary>How much the ground rises across the rank, in metres. What the camera has to frame.</summary>
        public float HeightSpread => MaxY - MinY;
    }

    /// <summary>
    /// Drops a rank of seats onto the ground.
    ///
    /// <para>
    /// <see cref="RankLayout"/> deliberately keeps every seat flat at local <c>y = 0</c> — it is
    /// pure geometry and knows nothing about a world. That is fine for a six-metre line of four on
    /// an anchor somebody placed on flat sand, and wrong the moment the rank is twenty metres
    /// across and folded into two rows: half of it ends up floating over a dip or buried in a rise.
    /// </para>
    ///
    /// <para>
    /// The probe arrives as a delegate rather than as a <c>Physics.Raycast</c> call, so the rule
    /// ("use the ground if there is any, otherwise the anchor's plane") can be tested without a
    /// scene. The caller supplies the real cast and the layer mask that goes with it.
    /// </para>
    /// </summary>
    public static class RankGrounding
    {
        /// <summary>
        /// Answers what the ground height is under <paramref name="seat"/>. False when there is
        /// nothing under it at all, in which case <paramref name="groundY"/> is not read.
        /// </summary>
        public delegate bool GroundProbe(Vector3 seat, out float groundY);

        /// <summary>
        /// Puts every seat on the ground under it.
        ///
        /// <paramref name="fallbackY"/> is what a seat with no ground beneath it gets — the anchor's
        /// own height, which reproduces exactly what the rank did before it was grounded. A rank
        /// standing in a scene with no colliders therefore looks like it always did rather than
        /// collapsing to zero.
        /// </summary>
        public static GroundedRank Solve(IReadOnlyList<Vector3> worldSeats, float fallbackY, GroundProbe probe)
        {
            int count = worldSeats?.Count ?? 0;

            if (count == 0 || probe == null)
                return new GroundedRank(System.Array.Empty<Vector3>(), fallbackY, fallbackY);

            var positions = new Vector3[count];

            float min = float.MaxValue;
            float max = float.MinValue;

            for (int i = 0; i < count; i++)
            {
                Vector3 seat = worldSeats[i];
                float y = probe(seat, out float hit) ? hit : fallbackY;

                positions[i] = new Vector3(seat.x, y, seat.z);

                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
            }

            return new GroundedRank(positions, min, max);
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Expected: PASS.

- [ ] **Step 5: Commit** *(hook-blocked — do not run)*

```bash
git add Assets/Game/Scripts/Gameplay/Versus/Core/RankGrounding.cs Assets/Game/Tests/EditMode/RankGroundingTests.cs
git commit -m "feat(lobby): solve rank seat heights against the ground"
```

---

## Task 4: The overlay scale ladder

**Files:**
- Create: `Assets/Game/Scripts/Gameplay/Versus/Core/RankOverlayScale.cs`
- Modify: `Assets/Game/Scripts/Gameplay/Versus/Core/VersusRules.cs`
- Test: `Assets/Game/Tests/EditMode/RankOverlayScaleTests.cs`

- [ ] **Step 1: Write the failing test file**

Create `Assets/Game/Tests/EditMode/RankOverlayScaleTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// How a label behaves when the space it is drawn into shrinks.
    ///
    /// The rungs are asserted by what they mean rather than by their numbers: a label that has
    /// plenty of room keeps its authored size, one that has less shrinks, one that cannot shrink any
    /// further gets shorter, and the last rung is still a word — team identity is never left to
    /// colour alone.
    /// </summary>
    public class RankOverlayScaleTests
    {
        private const float Authored = 46f;
        private const float FullWidth = 300f;
        private const float ShortWidth = 150f;
        private const float FloorWidth = 60f;

        private static LabelFit Fit(float available) =>
            RankOverlayScale.Fit(Authored, available, FullWidth, ShortWidth, FloorWidth);

        [Test]
        public void AllTheRoomInTheWorldKeepsTheAuthoredSize()
        {
            LabelFit fit = Fit(FullWidth * 2f);

            Assert.AreEqual(RankLabelRung.Roomy, fit.Rung);
            Assert.AreEqual(Authored, fit.FontSize, 0.0001f);
        }

        [Test]
        public void LessRoomShrinksTheLabelBeforeShorteningIt()
        {
            LabelFit fit = Fit(FullWidth * 0.7f);

            Assert.AreEqual(RankLabelRung.Scaled, fit.Rung);
            Assert.Less(fit.FontSize, Authored);
            Assert.GreaterOrEqual(fit.FontSize, RankOverlayScale.MinFontSize);
        }

        [Test]
        public void TooLittleRoomToShrinkAnyFurtherShortensTheLabel()
        {
            LabelFit fit = Fit(ShortWidth * 0.8f);

            Assert.AreEqual(RankLabelRung.Shortened, fit.Rung);
            Assert.GreaterOrEqual(fit.FontSize, RankOverlayScale.MinFontSize);
        }

        [Test]
        public void TheLastRungIsStillLegibleRatherThanVanishinglySmall()
        {
            LabelFit fit = Fit(1f);

            Assert.AreEqual(RankLabelRung.Floor, fit.Rung);
            Assert.GreaterOrEqual(fit.FontSize, RankOverlayScale.MinFontSize,
                                  "legibility wins over overlap at the bottom of the ladder");
        }

        [Test]
        public void TheLadderNeverGoesBackUpAsRoomRunsOut()
        {
            RankLabelRung previous = RankLabelRung.Roomy;

            for (float available = FullWidth * 2f; available > 0f; available -= 5f)
            {
                RankLabelRung rung = Fit(available).Rung;

                Assert.GreaterOrEqual((int)rung, (int)previous,
                                      $"the ladder climbed back up at {available}px");
                previous = rung;
            }
        }

        [Test]
        public void FontSizeNeverExceedsTheAuthoredSize()
        {
            Assert.LessOrEqual(Fit(FullWidth * 10f).FontSize, Authored);
        }

        [Test]
        public void NamesAreShownForEveryoneWhenTheyFit()
        {
            Assert.AreEqual(RankNameVisibility.All,
                            RankOverlayScale.NamesFor(seatPitchPx: 220f, nameWidthPx: 200f));
        }

        [Test]
        public void NamesThinToYourOwnTeamWhenTheyCrowd()
        {
            Assert.AreEqual(RankNameVisibility.OwnTeamAndHost,
                            RankOverlayScale.NamesFor(seatPitchPx: 120f, nameWidthPx: 200f));
        }

        /// <summary>
        /// You must always be able to find yourself. The bottom rung thins to two labels, it does
        /// not switch names off.
        /// </summary>
        [Test]
        public void TheLastRungStillShowsYouAndTheHost()
        {
            Assert.AreEqual(RankNameVisibility.YouAndHost,
                            RankOverlayScale.NamesFor(seatPitchPx: 10f, nameWidthPx: 200f));
        }

        [Test]
        public void ShortTeamNamesDropThePrefixAndNothingElse()
        {
            Assert.AreEqual("THREE", VersusRules.ShortTeamName(2));
            Assert.AreEqual("ONE", VersusRules.ShortTeamName(0));
        }

        [Test]
        public void AShortTeamNameIsNeverEmpty()
        {
            for (int team = 0; team < VersusRules.MaxTeams; team++)
                Assert.IsNotEmpty(VersusRules.ShortTeamName(team));
        }
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Expected: compile errors — `RankOverlayScale`, `LabelFit`, `RankLabelRung`, `RankNameVisibility` and
`VersusRules.ShortTeamName` do not exist.

- [ ] **Step 3: Add `ShortTeamName` to `VersusRules`**

In `VersusRules.cs`, directly after the existing `TeamName` method:

```csharp
        /// <summary>
        /// A team's name without its "TEAM " prefix, for a plate too small to hold the whole thing.
        ///
        /// Derived from <see cref="TeamName"/> rather than kept as a second array, for the same
        /// reason <see cref="MaxTeams"/> is derived: a parallel list is one somebody has to remember
        /// to keep in step, and nothing here would catch the drift.
        /// </summary>
        public static string ShortTeamName(int team)
        {
            const string prefix = "TEAM ";

            string name = TeamName(team);
            return name.StartsWith(prefix) && name.Length > prefix.Length
                ? name.Substring(prefix.Length)
                : name;
        }
```

- [ ] **Step 4: Create `RankOverlayScale.cs`**

```csharp
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// How much of a label survives at the size it has been left. Ordered: each rung has strictly
    /// less room than the one before it, which is what lets a test assert the ladder only ever
    /// descends.
    /// </summary>
    public enum RankLabelRung
    {
        /// <summary>Plenty of room. The authored size, the whole name.</summary>
        Roomy = 0,

        /// <summary>The whole name, shrunk to fit.</summary>
        Scaled = 1,

        /// <summary>The name without its prefix, plus its occupancy.</summary>
        Shortened = 2,

        /// <summary>The team's number and its occupancy — the least that is still a label.</summary>
        Floor = 3
    }

    /// <summary>Which names are worth drawing at all.</summary>
    public enum RankNameVisibility
    {
        All = 0,
        OwnTeamAndHost = 1,

        /// <summary>Never "none": a player must always be able to find themselves in the rank.</summary>
        YouAndHost = 2
    }

    /// <summary>What a label should be drawn as.</summary>
    public readonly struct LabelFit
    {
        public readonly RankLabelRung Rung;
        public readonly float FontSize;

        public LabelFit(RankLabelRung rung, float fontSize)
        {
            Rung = rung;
            FontSize = fontSize;
        }
    }

    /// <summary>
    /// Sizes the rank's overlays from the room they actually have on screen.
    ///
    /// <para>
    /// The bug this exists to kill: team plates and nameplates were built at a fixed size in canvas
    /// pixels, while the thing they label is measured in metres and moves away from the camera as
    /// teams are added. Past four teams the labels overlapped each other into a smear, and no
    /// amount of camera work fixes a constant.
    /// </para>
    ///
    /// <para>
    /// Pure and free of TextMeshPro: the caller measures its own text and passes the widths in, so
    /// the rule can be tested without a font, a canvas or a camera.
    /// </para>
    /// </summary>
    public static class RankOverlayScale
    {
        /// <summary>
        /// The smallest a label is ever drawn, in canvas pixels on the 1080-high reference canvas.
        ///
        /// Below this the text is present but not readable, which is worse than a shorter label
        /// that is — hence the ladder rather than an unbounded shrink.
        /// </summary>
        public const float MinFontSize = 18f;

        /// <summary>
        /// How much of the room a name needs before the rank stops drawing all of them.
        ///
        /// Under about half, drawing every name produces a smear rather than a list, and the useful
        /// information — which of these is me, which is the host — is the first thing lost in it.
        /// </summary>
        public const float NameThinThreshold = 0.45f;

        /// <summary>
        /// The size a label of <paramref name="widthPx"/> at <paramref name="authoredSize"/> has to
        /// drop to in order to fit <paramref name="availablePx"/>. Never larger than authored: this
        /// shrinks labels, it does not grow them.
        /// </summary>
        public static float SizeFor(float authoredSize, float widthPx, float availablePx)
        {
            if (widthPx <= 0.01f) return authoredSize;

            return Mathf.Min(authoredSize, authoredSize * Mathf.Max(0f, availablePx) / widthPx);
        }

        /// <summary>
        /// Picks the longest version of a label that is still legible in the room available.
        ///
        /// The three widths are the same string measured three ways at
        /// <paramref name="authoredSize"/>: the full name, the short name, and the floor form. The
        /// floor rung is clamped up to <see cref="MinFontSize"/> rather than allowed to shrink
        /// further — at the bottom of the ladder a legible label that slightly overlaps its
        /// neighbour beats an unreadable one that does not.
        /// </summary>
        public static LabelFit Fit(float authoredSize, float availablePx, float fullWidthPx,
            float shortWidthPx, float floorWidthPx)
        {
            float full = SizeFor(authoredSize, fullWidthPx, availablePx);

            if (full >= authoredSize) return new LabelFit(RankLabelRung.Roomy, authoredSize);
            if (full >= MinFontSize) return new LabelFit(RankLabelRung.Scaled, full);

            float shortened = SizeFor(authoredSize, shortWidthPx, availablePx);
            if (shortened >= MinFontSize) return new LabelFit(RankLabelRung.Shortened, shortened);

            float floor = SizeFor(authoredSize, floorWidthPx, availablePx);
            return new LabelFit(RankLabelRung.Floor, Mathf.Max(MinFontSize, floor));
        }

        /// <summary>
        /// Which names to draw, given how far apart two people in the same team stand on screen and
        /// how wide a name is.
        /// </summary>
        public static RankNameVisibility NamesFor(float seatPitchPx, float nameWidthPx)
        {
            if (nameWidthPx <= 0.01f) return RankNameVisibility.All;

            if (seatPitchPx >= nameWidthPx) return RankNameVisibility.All;
            if (seatPitchPx >= nameWidthPx * NameThinThreshold) return RankNameVisibility.OwnTeamAndHost;

            return RankNameVisibility.YouAndHost;
        }
    }
}
```

- [ ] **Step 5: Run the tests**

Expected: PASS.

- [ ] **Step 6: Commit** *(hook-blocked — do not run)*

```bash
git add Assets/Game/Scripts/Gameplay/Versus/Core/RankOverlayScale.cs Assets/Game/Scripts/Gameplay/Versus/Core/VersusRules.cs Assets/Game/Tests/EditMode/RankOverlayScaleTests.cs
git commit -m "feat(lobby): size rank overlays from the room they actually have"
```

---

## Task 5: Seat the figures by world position

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyRankFigures.cs`

No test: this is a one-line change of coordinate space on a class that needs a Resources prefab and
a live scene. Task 6's behaviour is what proves it.

- [ ] **Step 1: Change `Seat` to take a world position**

Replace the `Seat` method's doc comment and body:

```csharp
        /// <summary>
        /// Stands a slot's figure at <paramref name="worldPosition"/> under <paramref name="anchor"/>,
        /// in <paramref name="color"/>. False when no figure could be made — no prefab, or no anchor —
        /// in which case the slot reads as empty rather than throwing.
        ///
        /// A WORLD position, not a local one: seats are laid out flat by RankLayout and then dropped
        /// onto the ground by RankGrounding, and the height that comes back is a world height. Set
        /// as a local position it would be measured from the anchor's own plane and undo the
        /// grounding entirely.
        /// </summary>
        public bool Seat(int slot, Transform anchor, Vector3 worldPosition, int color)
        {
            Ensure(slot, anchor);

            if (figures[slot] == null)
            {
                occupied[slot] = false;
                return false;
            }

            occupied[slot] = true;
            figures[slot].SetActive(true);
            figures[slot].transform.position = worldPosition;

            Recolor(slot, color);
            return true;
        }
```

- [ ] **Step 2: Verify it compiles**

The Unity console must show no errors. `LobbyPreviewRank.Render` still passes a local position at
this point — that is fixed in Task 6 and the two tasks are not separately runnable in the editor.
Compile-clean is the bar here.

- [ ] **Step 3: Commit** *(hook-blocked — do not run)*

```bash
git add Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyRankFigures.cs
git commit -m "refactor(lobby): seat rank figures in world space"
```

---

## Task 6: Wire grounding into the rank

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyPreviewRank.cs`

- [ ] **Step 1: Add the probe constants and cached solve**

In `LobbyPreviewRank`, after the `FallbackProbeDepth` constant, add:

```csharp
        /// <summary>
        /// How far above and below a seat the ground is looked for, in metres. The same probe
        /// LobbyPreviewSetup uses to place the anchor, so a seat lands on exactly the surface the
        /// anchor was dropped onto.
        /// </summary>
        private const float SeatProbeHeight = 30f;
        private const float SeatProbeDepth = 100f;

        /// <summary>
        /// What the seat probe is allowed to hit.
        ///
        /// Not <c>~0</c>: the menu is full of set dressing — the ruin, its rubble, the decorative
        /// astronauts' own props — and a seat that lands on top of a rock reads as a bug rather than
        /// as terrain. The preview astronauts themselves cannot be hit at all (LobbyPreviewSetup
        /// strips every collider off the prefab), so this mask is about the scenery, not about them.
        /// </summary>
        private static readonly int GroundMask = LayerMask.GetMask("Default", "Ground", "Terrain");
```

- [ ] **Step 2: Add the grounding fields**

Beside the existing `localSlot` field:

```csharp
        /// <summary>
        /// Where every seat actually stands, and how much the ground rises across the rank. Solved
        /// when the shape of the rank changes rather than on every poll: the anchor does not move
        /// and neither does the sand, so re-probing twice a second would be 24 raycasts a second to
        /// arrive at the same answer.
        /// </summary>
        private GroundedRank grounded;

        /// <summary>The rank shape <see cref="grounded"/> was solved for.</summary>
        private int groundedTeams = -1;
        private int groundedTeamSize = -1;
        private int groundedHeads = -1;
```

- [ ] **Step 3: Add the solve method**

Add as a private method on `LobbyPreviewRank`:

```csharp
        /// <summary>
        /// Puts every seat of the current rank shape on the ground, if that shape has changed since
        /// the last time. Seats are addressed by index whether or not anybody stands in them — the
        /// same rule RankLayout follows — so an empty seat still has a height and a player who joins
        /// lands on the sand without a re-solve.
        /// </summary>
        private void GroundSeats(int teams, int teamSize, int heads)
        {
            if (teams == groundedTeams && teamSize == groundedTeamSize && heads == groundedHeads) return;

            groundedTeams = teams;
            groundedTeamSize = teamSize;
            groundedHeads = heads;

            if (anchor == null)
            {
                grounded = default;
                return;
            }

            var seats = new Vector3[teams * teamSize];

            for (int team = 0; team < teams; team++)
                for (int seat = 0; seat < teamSize; seat++)
                    seats[team * teamSize + seat] =
                        anchor.TransformPoint(RankLayout.SeatPosition(team, seat, teams, teamSize));

            grounded = RankGrounding.Solve(seats, anchor.position.y, Probe);
        }

        /// <summary>
        /// The real cast behind <see cref="RankGrounding.GroundProbe"/>. Triggers are ignored so a
        /// music zone or a spawn volume over the dunes cannot become the floor.
        /// </summary>
        private static bool Probe(Vector3 seat, out float groundY)
        {
            if (Physics.Raycast(seat + Vector3.up * SeatProbeHeight, Vector3.down,
                                out RaycastHit hit, SeatProbeDepth, GroundMask,
                                QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                return true;
            }

            groundY = 0f;
            return false;
        }

        /// <summary>Where a given team's seat ended up standing, in world space.</summary>
        private Vector3 GroundedSeat(int team, int seat, int teams, int teamSize)
        {
            int index = team * teamSize + seat;

            if (grounded.Positions != null && index >= 0 && index < grounded.Positions.Length)
                return grounded.Positions[index];

            // No solve yet, or a seat outside the shape we solved for — the flat anchor plane is
            // exactly what the rank did before it was ever grounded.
            return anchor != null
                ? anchor.TransformPoint(RankLayout.SeatPosition(team, seat, teams, teamSize))
                : Vector3.zero;
        }
```

- [ ] **Step 4: Use it in `Render`**

In `Render`, replace the seating loop and the `view.Fit` call. The method becomes:

```csharp
        public void Render(RosterSnapshot snapshot)
        {
            localSlot = snapshot.LocalSlot;

            bool versus = snapshot.IsVersus;
            int teams = versus ? Mathf.Max(1, snapshot.TeamCount) : 1;
            int teamSize = versus ? Mathf.Max(1, snapshot.TeamSize) : Mathf.Max(1, snapshot.Names.Length);
            int[] teamsBySlot = versus ? snapshot.Teams : null;

            GroundSeats(teams, teamSize, snapshot.Names.Length);

            for (int slot = 0; slot < snapshot.Names.Length; slot++)
            {
                int team = versus && slot < snapshot.Teams.Length ? snapshot.Teams[slot] : 0;
                int seat = SeatOf(slot, teamsBySlot);

                int color = versus
                    ? snapshot.ColorOfTeam(team)
                    : slot < snapshot.SuitColors.Length ? snapshot.SuitColors[slot] : 0;

                if (figures.Seat(slot, anchor, GroundedSeat(team, seat, teams, teamSize), color))
                    nameplates.Set(slot, snapshot.Names[slot], isHost: slot == snapshot.HostSlot);
            }

            figures.HideFrom(snapshot.Names.Length);

            if (versus)
            {
                plates.Ensure(teams, teamSize);
                plates.Update(snapshot);
            }
            else
            {
                plates.Clear();
            }

            if (figures.IsStanding(localSlot))
                cycler.SetColor(versus ? snapshot.ColorOfTeam(snapshot.LocalTeam) : snapshot.SuitColors[localSlot]);

            view.Fit(anchor, teams, teamSize, grounded.HeightSpread);

            // Re-faced after the fit, not before: facing reads the camera's CURRENT position, and
            // fitting is what just moved it.
            figures.FaceCamera();

            PositionOverlays();
        }
```

- [ ] **Step 5: Ground the team plates too**

`LobbyTeamPlates.Position` places a plate over `anchor.TransformPoint(TeamCenter(...) + up * lift)`,
which is the flat plane again. Add a public accessor on `LobbyPreviewRank` for the grounded height
of a team's centre, and pass it in. Add to `LobbyPreviewRank`:

```csharp
        /// <summary>
        /// The ground height under a team's centre — the seat of its first row, which is where its
        /// plate hangs. Falls back to the anchor's own height, which is where plates hung before the
        /// rank was grounded.
        /// </summary>
        private float GroundOfTeam(int team, int teams, int teamSize)
        {
            if (grounded.Positions == null || grounded.Positions.Length == 0)
                return anchor != null ? anchor.position.y : 0f;

            int index = team * teamSize;

            return index >= 0 && index < grounded.Positions.Length
                ? grounded.Positions[index].y
                : grounded.MinY;
        }
```

and change `PositionOverlays` to hand the plates a height resolver:

```csharp
        private void PositionOverlays()
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            nameplates.Position(camera, figures.Heads, figures.Occupied);
            plates.Position(camera, anchor, GroundOfTeam);

            bool cyclerVisible = figures.IsStanding(localSlot);
            cycler.Position(camera, cyclerVisible,
                            cyclerVisible ? figures.PositionOf(localSlot) - Vector3.up * CyclerDrop : default);
        }
```

- [ ] **Step 6: Update `LobbyTeamPlates.Position` to take the resolver**

In `LobbyTeamPlates.cs`, change the signature and the world point:

```csharp
        /// <summary>
        /// Keeps every plate over its cluster.
        ///
        /// <paramref name="groundOfTeam"/> answers the height the team is actually standing at, so a
        /// plate over a team on a rise hangs over that team rather than at the anchor's height with
        /// its own astronauts above it.
        /// </summary>
        public void Position(Camera camera, Transform anchor, System.Func<int, int, int, float> groundOfTeam)
        {
            if (anchor == null) return;

            for (int team = 0; team < plates.Count; team++)
            {
                Plate plate = plates[team];
                if (plate.Row == null) continue;

                Vector3 flat = anchor.TransformPoint(RankLayout.TeamCenter(team, teamCount, teamSize));
                float groundY = groundOfTeam != null ? groundOfTeam(team, teamCount, teamSize) : flat.y;

                var worldPoint = new Vector3(flat.x, groundY + lift, flat.z);

                plate.Row.gameObject.SetActive(layer.Place(camera, plate.Row, worldPoint));
            }
        }
```

- [ ] **Step 7: Verify in the editor**

Enter Play mode on `MainMenu.unity`, press Host under Versus, set Teams to 8 and Team size to 3.

Expected: eight teams in two rows of four; the back row offset sideways into the front row's gaps;
every astronaut's boots in the sand with no floating and no sinking; the console clean.

- [ ] **Step 8: Commit** *(hook-blocked — do not run)*

```bash
git add Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyPreviewRank.cs Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyTeamPlates.cs
git commit -m "feat(lobby): stand the rank on the ground rather than on the anchor plane"
```

---

## Task 7: Two-axis fit and eye lift in the camera

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyPreviewCamera.cs`

- [ ] **Step 1: Add the band and headroom constants**

After the existing `FitMargin`:

```csharp
        /// <summary>
        /// How much of the frame's height the rank is allowed to fill, measured down from the top.
        ///
        /// <para>
        /// The bottom of the screen is not the rank's: the status line starts
        /// <see cref="MenuEntry.MessageBottom"/> + its own height up from the bottom, and the footer
        /// is below that. The TOP is the rank's, though — a team plate at PlateLift projects above
        /// the page title in the authored shot and always has, and the title is a left-aligned
        /// column while the plates are centred over their teams.
        /// </para>
        ///
        /// <para>
        /// Expressed as pixels of the 1080-high reference canvas and converted against the live
        /// canvas height, because that height is what actually changes on a short window — which is
        /// the whole reason this exists.
        /// </para>
        /// </summary>
        private const float ChromeBottom = MenuEntry.MessageBottom + 44f;

        /// <summary>The reference canvas the menu's chrome is laid out on. Matches its CanvasScaler.</summary>
        private const float ReferenceHeight = 1080f;

        /// <summary>
        /// How far above the tallest head the shot reaches, in metres — the room a team plate needs
        /// at <c>LobbyPreviewRank.PlateLift</c> plus the plate's own height.
        /// </summary>
        private const float Headroom = 2.7f;
```

- [ ] **Step 2: Replace `Fit`**

```csharp
        /// <summary>
        /// Backs the camera off from the authored view so the whole rank fits in frame, and lifts
        /// the eye when a second row of teams has to be seen over the first.
        ///
        /// <para>
        /// Measured from the authored pose and only ever pushed FURTHER back along its own backward
        /// direction, never recomputed from the anchor outright. That is what guarantees a small
        /// rank reproduces the exact composed shot rather than drifting off its axis: when the rank
        /// already fits at the authored distance, the extra distance is zero and the camera sits
        /// exactly where the view put it.
        /// </para>
        ///
        /// <para>
        /// Both axes are fitted. Width alone was what made a narrow or short window frame the rank
        /// badly — a 4:3 window has a narrower horizontal field for the same rank, and a short one
        /// has less usable height under the status line, and neither was being asked about.
        /// </para>
        ///
        /// <para>
        /// The eye lift is the one case where the authored rotation is not kept: a second row of
        /// teams is invisible from an eye below head height, and the authored eye sits 1.39 m above
        /// the anchor. When it applies, the camera is raised and re-aimed at the rank so the lift
        /// does not simply push everyone down the frame. With one row the lift is zero and the
        /// authored pose is reproduced exactly, as before.
        /// </para>
        /// </summary>
        public void Fit(Transform anchor, int teams, int teamSize, float groundSpread)
        {
            // No adopted view means no authored backward direction to push along, so there is
            // nothing safe to fit against — the rank keeps whatever framing the scene already has.
            if (borrowed == null || anchor == null) return;

            Camera camera = Camera.main;
            if (camera == null) return;

            float verticalFov = camera.fieldOfView;
            float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(verticalFov * Mathf.Deg2Rad * 0.5f) * camera.aspect)
                                  * Mathf.Rad2Deg;

            float bandFov = 2f * Mathf.Atan(Mathf.Tan(verticalFov * Mathf.Deg2Rad * 0.5f) * BandFraction())
                            * Mathf.Rad2Deg;

            float width = RankLayout.TotalWidth(teams, teamSize);
            float height = Mathf.Max(0f, groundSpread) + RankLayout.HeadHeight + Headroom;

            float wanted = RankLayout.CameraDistance(width, height, horizontalFov, bandFov, FitMargin);
            float authoredDistance = Vector3.Distance(viewPosition, anchor.position);

            // Never negative: a rank that already fits inside the authored shot must not pull the
            // camera IN, which is the one thing this class promises it never does.
            float extra = Mathf.Max(0f, wanted - authoredDistance);

            Vector3 backward = viewRotation * Vector3.back;
            Vector3 position = viewPosition + backward * extra;

            float distance = authoredDistance + extra;
            float wantedEye = anchor.position.y + RankLayout.EyeHeight(teams, teamSize, distance);
            float lift = Mathf.Max(0f, wantedEye - position.y);

            if (lift <= 0.001f)
            {
                borrowed.SetPositionAndRotation(position, viewRotation);
                return;
            }

            position += Vector3.up * lift;

            // Re-aimed at the rank's own head height so the lift frames the astronauts rather than
            // sliding them out of the bottom of the shot.
            Vector3 target = anchor.position + Vector3.up * RankLayout.HeadHeight;
            Vector3 toTarget = target - position;

            borrowed.SetPositionAndRotation(
                position,
                toTarget.sqrMagnitude < 0.0001f ? viewRotation : Quaternion.LookRotation(toTarget, Vector3.up));
        }

        /// <summary>
        /// How much of the frame's height the rank may use, as a fraction, once the status line and
        /// footer have taken theirs. Computed from the live canvas height so a short window really
        /// does give the rank less room rather than the same fraction of a smaller frame.
        /// </summary>
        private static float BandFraction()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return 1f - ChromeBottom / ReferenceHeight;

            // The menu's CanvasScaler matches WIDTH at 1920x1080, so the canvas is always 1920 wide
            // and its HEIGHT is what moves with the aspect ratio. A short, wide window therefore has
            // a SHORT canvas, and the fixed chrome along its bottom eats a bigger fraction of it.
            float canvasHeight = 1920f * Screen.height / Screen.width;

            return Mathf.Clamp(1f - ChromeBottom / Mathf.Max(1f, canvasHeight), 0.2f, 1f);
        }
```

- [ ] **Step 3: Add the `using` for `MenuEntry`**

`MenuEntry` is in `SpaceGame.Presentation`, the same namespace root. `LobbyPreviewCamera` is in
`SpaceGame.Presentation.Lobbies`, so `MenuEntry` resolves without a new `using`. Confirm the file
compiles; if it does not, add `using SpaceGame.Presentation;`.

- [ ] **Step 4: Verify in the editor**

Play `MainMenu.unity`, host a VS lobby.

- At 2 teams x 2 the shot must look **identical** to before the change.
- At 8 teams x 3 the camera must be visibly closer than the pre-change build and the back row
  visible over the front.
- Resize the Game view to 4:3 and to 21:9 and confirm the rank stays inside the frame and above the
  status line in both.

- [ ] **Step 5: Commit** *(hook-blocked — do not run)*

```bash
git add Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyPreviewCamera.cs
git commit -m "feat(lobby): fit the rank to the band it owns and lift the eye over a second row"
```

---

## Task 8: Adaptive team plates

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyTeamPlates.cs`

- [ ] **Step 1: Add the measured-width and applied-size fields**

Extend the private `Plate` class and add the snapshot the ladder needs:

```csharp
        private sealed class Plate
        {
            public RectTransform Row;
            public TextMeshProUGUI Label;
            public TextMeshProUGUI Shadow;
            public Button Button;

            /// <summary>Where this plate landed on screen last frame, for the collision measure.</summary>
            public Vector2 Screen;
            public bool Visible;

            /// <summary>The size last written to the label, so an unchanged size costs no mesh rebuild.</summary>
            public float AppliedSize = -1f;

            /// <summary>The text last written, for the same reason.</summary>
            public string AppliedText;
        }

        /// <summary>Occupancy per team, kept from the last Update so Position can build "THREE 2/3".</summary>
        private readonly List<int> headsOn = new();

        /// <summary>The team size the occupancy is measured against.</summary>
        private int shownTeamSize;
```

`Build` must now capture the shadow label:

```csharp
            TextMeshProUGUI label = UIBuilder.ShadowedLabel(row, VersusRules.TeamName(team), PlateSize,
                                                            MenuEntry.Title, MenuEntry.Idle, ShadowOffset,
                                                            TextAlignmentOptions.Center,
                                                            out TextMeshProUGUI shadow);
            label.raycastTarget = true;
```

and return `new Plate { Row = row, Label = label, Shadow = shadow, Button = button }`.

- [ ] **Step 2: Record occupancy in `Update`**

At the top of `Update`, before the loop:

```csharp
            shownTeamSize = teamSize;

            headsOn.Clear();
            for (int team = 0; team < plates.Count; team++) headsOn.Add(snapshot.HeadsOn(team));
```

- [ ] **Step 3: Apply the ladder in `Position`**

After the existing placement loop in `Position`, record each plate's screen point, then fit. Replace
the body of `Position` with:

```csharp
        public void Position(Camera camera, Transform anchor, System.Func<int, int, int, float> groundOfTeam)
        {
            if (anchor == null) return;

            for (int team = 0; team < plates.Count; team++)
            {
                Plate plate = plates[team];
                if (plate.Row == null) continue;

                Vector3 flat = anchor.TransformPoint(RankLayout.TeamCenter(team, teamCount, teamSize));
                float groundY = groundOfTeam != null ? groundOfTeam(team, teamCount, teamSize) : flat.y;

                var worldPoint = new Vector3(flat.x, groundY + lift, flat.z);

                plate.Visible = layer.Place(camera, plate.Row, worldPoint);
                plate.Screen = plate.Row.anchoredPosition;
                plate.Row.gameObject.SetActive(plate.Visible);
            }

            ApplyLadder();
        }

        /// <summary>
        /// Shrinks and shortens each plate to the room it has beside its nearest neighbour.
        ///
        /// Room is measured against every OTHER plate rather than against the team next door in the
        /// layout: once teams wrap into two rows the nearest plate on screen is often in the other
        /// row, and a plate sized against its own row would still collide with it. Only plates
        /// within a plate-height vertically are counted — one directly above another does not
        /// compete for horizontal space.
        /// </summary>
        private void ApplyLadder()
        {
            for (int team = 0; team < plates.Count; team++)
            {
                Plate plate = plates[team];
                if (plate.Row == null || !plate.Visible || plate.Label == null) continue;

                float available = float.MaxValue;

                for (int other = 0; other < plates.Count; other++)
                {
                    if (other == team) continue;

                    Plate rival = plates[other];
                    if (rival.Row == null || !rival.Visible) continue;

                    if (Mathf.Abs(rival.Screen.y - plate.Screen.y) > PlateHeight) continue;

                    available = Mathf.Min(available, Mathf.Abs(rival.Screen.x - plate.Screen.x));
                }

                if (available == float.MaxValue) available = PlateWidth;

                string full = VersusRules.TeamName(team);
                string shortened = $"{VersusRules.ShortTeamName(team)} {Occupancy(team)}";
                string floor = $"{team + 1} {Occupancy(team)}";

                LabelFit fit = RankOverlayScale.Fit(PlateSize, available,
                                                    Width(plate.Label, full),
                                                    Width(plate.Label, shortened),
                                                    Width(plate.Label, floor));

                string text = fit.Rung switch
                {
                    RankLabelRung.Shortened => shortened,
                    RankLabelRung.Floor => floor,
                    _ => full
                };

                Write(plate, text, fit.FontSize);
            }
        }

        private string Occupancy(int team) =>
            team < headsOn.Count ? $"{headsOn[team]}/{Mathf.Max(1, shownTeamSize)}" : string.Empty;

        /// <summary>
        /// How wide a string is at the plate's AUTHORED size, whatever size the label happens to be
        /// drawn at right now.
        ///
        /// The size is set, measured and put back rather than scaled arithmetically:
        /// RankOverlayScale scales from the authored size, so measuring at the CURRENT one would
        /// feed last frame's answer back into this frame's and let the size walk down to nothing
        /// over a few seconds.
        /// </summary>
        private static float Width(TextMeshProUGUI label, string text)
        {
            float current = label.fontSize;

            label.fontSize = PlateSize;
            float width = label.GetPreferredValues(text, 0f, 0f).x;
            label.fontSize = current;

            return width;
        }

        /// <summary>
        /// Writes text and size to both copies of the label, and only when either actually changed —
        /// assigning to a TMP label rebuilds its mesh, and this runs every frame for every team.
        /// </summary>
        private static void Write(Plate plate, string text, float size)
        {
            bool sizeChanged = Mathf.Abs(plate.AppliedSize - size) > 0.25f;
            bool textChanged = plate.AppliedText != text;

            if (!sizeChanged && !textChanged) return;

            plate.AppliedSize = size;
            plate.AppliedText = text;

            if (plate.Label != null)
            {
                plate.Label.fontSize = size;
                plate.Label.text = text;
            }

            if (plate.Shadow != null)
            {
                plate.Shadow.fontSize = size;
                plate.Shadow.text = text;
            }
        }
```

- [ ] **Step 4: Verify in the editor**

Host a VS lobby. At 2 teams every plate reads `TEAM ONE` / `TEAM TWO` at full size. Step Teams up to
8 and watch the plates shrink, then shorten to `THREE 2/3`. No plate must ever be blank, and no
plate may overlap another.

- [ ] **Step 5: Commit** *(hook-blocked — do not run)*

```bash
git add Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyTeamPlates.cs
git commit -m "feat(lobby): shrink and shorten team plates to the room they have"
```

---

## Task 9: Adaptive nameplates

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyNameplates.cs`
- Modify: `Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyPreviewRank.cs`

- [ ] **Step 1: Teach the nameplates who is who**

`LobbyNameplates.Set` already takes `isHost`. Add the local slot and the team map so it can thin.
Add these fields:

```csharp
        /// <summary>Which slot is ours, which team each slot is on, and which slot hosts — the three
        /// things the thinning rungs need. Written by Set and by SetContext, never guessed.</summary>
        private readonly List<int> teamOf = new();
        private readonly List<bool> isHost = new();
        private int localSlot = -1;
        private int localTeam = -1;

        /// <summary>The size last written per slot, so an unchanged size costs no mesh rebuild.</summary>
        private readonly List<float> appliedSize = new();
```

Add a context setter:

```csharp
        /// <summary>
        /// Tells the plates which figure is ours and which team each figure is on, so the thinning
        /// rungs can keep the names that matter — yours and the host's — and drop the rest.
        /// </summary>
        public void SetContext(int slot, int team) 
        {
            localSlot = slot;
            localTeam = team;
        }
```

and record the team in `Set`:

```csharp
        public void Set(int slot, string name, bool isHostSlot, int team)
        {
            Ensure(slot);

            SlotLists.Grow(teamOf, slot);
            SlotLists.Grow(isHost, slot);

            teamOf[slot] = team;
            isHost[slot] = isHostSlot;

            labels[slot].text = name;
            shadows[slot].text = name;
            underlines[slot].gameObject.SetActive(isHostSlot);
        }
```

- [ ] **Step 2: Apply the ladder in `Position`**

Replace `Position`:

```csharp
        /// <summary>
        /// Keeps every plate over its head, at the size the space between heads allows.
        ///
        /// A plate whose slot is empty, or whose head is behind the camera, is hidden. So is one the
        /// ladder has thinned away — but never yours and never the host's: you must always be able
        /// to find yourself in the rank.
        /// </summary>
        public void Position(Camera camera, IReadOnlyList<Transform> heads, IReadOnlyList<bool> occupied)
        {
            float pitch = SeatPitchOnScreen(camera, heads, occupied);
            float nameWidth = WidestName();

            RankNameVisibility visibility = RankOverlayScale.NamesFor(pitch, nameWidth);
            float size = Mathf.Max(RankOverlayScale.MinFontSize,
                                   RankOverlayScale.SizeFor(NameSize, nameWidth, pitch));

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;

                bool standing = i < occupied.Count && occupied[i]
                                && i < heads.Count && heads[i] != null;

                bool wanted = standing && Wanted(i, visibility)
                              && layer.Place(camera, rows[i], heads[i].position + Vector3.up * lift);

                rows[i].gameObject.SetActive(wanted);

                if (wanted) Resize(i, size);
            }
        }

        /// <summary>Whether a slot's name survives the current rung.</summary>
        private bool Wanted(int slot, RankNameVisibility visibility)
        {
            if (visibility == RankNameVisibility.All) return true;

            bool mine = slot == localSlot;
            bool host = slot < isHost.Count && isHost[slot];

            if (visibility == RankNameVisibility.YouAndHost) return mine || host;

            bool sameTeam = localTeam >= 0 && slot < teamOf.Count && teamOf[slot] == localTeam;
            return mine || host || sameTeam;
        }

        /// <summary>
        /// How far apart two adjacent heads are on screen, in canvas pixels — the room one name has.
        ///
        /// Measured between real heads rather than derived from RankLayout, because that distance
        /// depends on where the camera ended up, which is the whole thing being adapted to.
        /// </summary>
        private float SeatPitchOnScreen(Camera camera, IReadOnlyList<Transform> heads,
            IReadOnlyList<bool> occupied)
        {
            float nearest = float.MaxValue;

            for (int i = 0; i < heads.Count; i++)
            {
                if (i >= occupied.Count || !occupied[i] || heads[i] == null) continue;

                for (int j = i + 1; j < heads.Count; j++)
                {
                    if (j >= occupied.Count || !occupied[j] || heads[j] == null) continue;

                    Vector3 a = camera.WorldToScreenPoint(heads[i].position);
                    Vector3 b = camera.WorldToScreenPoint(heads[j].position);

                    if (a.z <= 0f || b.z <= 0f) continue;
                    if (Mathf.Abs(a.y - b.y) > RowHeight) continue;

                    nearest = Mathf.Min(nearest, Mathf.Abs(a.x - b.x) * CanvasScale());
                }
            }

            return nearest == float.MaxValue ? RowWidth : nearest;
        }

        /// <summary>
        /// Screen pixels to canvas pixels. The menu's CanvasScaler matches WIDTH at 1920, so one
        /// canvas pixel is Screen.width / 1920 screen pixels — and every size in this file is a
        /// canvas pixel.
        /// </summary>
        private static float CanvasScale() =>
            Screen.width > 0 ? 1920f / Screen.width : 1f;

        /// <summary>The longest name standing, measured at the authored size.</summary>
        private float WidestName()
        {
            float widest = 0f;

            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] == null || string.IsNullOrEmpty(labels[i].text)) continue;

                float current = labels[i].fontSize;
                labels[i].fontSize = NameSize;
                widest = Mathf.Max(widest, labels[i].GetPreferredValues(labels[i].text, 0f, 0f).x);
                labels[i].fontSize = current;
            }

            return widest > 0f ? widest : RowWidth;
        }

        /// <summary>Writes a size to both copies, and only when it actually changed.</summary>
        private void Resize(int slot, float size)
        {
            SlotLists.Grow(appliedSize, slot);

            if (Mathf.Abs(appliedSize[slot] - size) <= 0.25f) return;

            appliedSize[slot] = size;

            if (labels[slot] != null) labels[slot].fontSize = size;
            if (shadows[slot] != null) shadows[slot].fontSize = size;
        }
```

Add `using SpaceGame.Gameplay;` to the file's usings.

- [ ] **Step 3: Update the two call sites in `LobbyPreviewRank`**

In `Render`, pass the team to `Set` and the context before the loop:

```csharp
            nameplates.SetContext(snapshot.LocalSlot, versus ? snapshot.LocalTeam : -1);
```

and:

```csharp
                if (figures.Seat(slot, anchor, GroundedSeat(team, seat, teams, teamSize), color))
                    nameplates.Set(slot, snapshot.Names[slot], slot == snapshot.HostSlot, team);
```

- [ ] **Step 4: Check `SlotLists.Grow` handles `float` and `int`**

`SlotLists.Grow` is generic over `List<T>`; confirm by reading
`Assets/Game/Scripts/Presentation/UI/Lobby/Rank/SlotLists.cs`. If it is not generic, add the
overloads it needs rather than duplicating the growth loop.

- [ ] **Step 5: Verify in the editor**

Host a VS lobby with 2 teams — every name shows at full size. Step up to 8 teams x 3 and confirm the
names shrink, then thin to your own team and the host, and that **your own name never disappears**.

- [ ] **Step 6: Commit** *(hook-blocked — do not run)*

```bash
git add Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyNameplates.cs Assets/Game/Scripts/Presentation/UI/Lobby/Rank/LobbyPreviewRank.cs
git commit -m "feat(lobby): scale and thin nameplates to the room between heads"
```

---

## Task 10: Documentation

**Files:**
- Modify: `docs/AI/systems/GameModes.md`
- Modify: `docs/AI/systems/Lobby.md`

- [ ] **Step 1: Update the `RankLayout` row in `GameModes.md`**

Line 60 currently reads:

```
| `RankLayout` | [Versus/Core/RankLayout.cs](Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs) | Lobby rank geometry: seat spacing, 4-wide wrap, team gap, camera pull-back |
```

Replace with, and add two rows beneath it:

```
| `RankLayout` | [Versus/Core/RankLayout.cs](Assets/Game/Scripts/Gameplay/Versus/Core/RankLayout.cs) | Lobby rank geometry: seat spacing, 4-wide seat wrap, **4-wide team wrap with a half-pitch stagger**, team gap, two-axis camera fit, eye lift |
| `RankGrounding` | [Versus/Core/RankGrounding.cs](Assets/Game/Scripts/Gameplay/Versus/Core/RankGrounding.cs) | Drops flat seats onto the ground through an injected probe; reports the height spread the camera frames |
| `RankOverlayScale` | [Versus/Core/RankOverlayScale.cs](Assets/Game/Scripts/Gameplay/Versus/Core/RankOverlayScale.cs) | Projected spacing → font size + label rung; the floor rung is still a word |
```

- [ ] **Step 2: Add symptoms to `Lobby.md` frontmatter**

Under `symptoms:`, add:

```yaml
  - "astronauts in the lobby float above or sink into the sand"
  - "team names overlap each other with more than four teams"
  - "the rank is tiny or clipped on a small or narrow window"
```

- [ ] **Step 3: Update the `LobbyPreviewRank` row in `Lobby.md`**

Extend the existing row's description to mention that teams wrap four abreast, seats are ground
probed, and overlays scale from their projected spacing.

- [ ] **Step 4: Add three gotchas to `Lobby.md`**

Under `## Gotchas`:

```markdown
- **`RankLayout` returns flat `y = 0` on purpose, and it is not where anyone stands.** The seats are
  pure geometry; `RankGrounding` probes each one onto the sand and `LobbyRankFigures.Seat` takes a
  **world** position. Assigning a seat as a `localPosition` silently re-flattens the whole rank onto
  the anchor's plane — which is what it did before, and why a wide rank floated over dips.
- **The lobby camera's authored eye is 1.389 m above the anchor — below a 1.8 m head.** Any second
  row of anything is invisible from it. `RankLayout.EyeHeight` is what lifts it, and it only applies
  when `TeamRowsFor > 1`, so a one-row rank still reproduces the authored shot exactly.
- **The menu's CanvasScaler matches WIDTH at 1920x1080**, so the canvas is always 1920 wide and its
  *height* moves with the aspect ratio. Anything reasoning about how much vertical room the page has
  must compute `1920 * Screen.height / Screen.width`, not assume 1080.
```

- [ ] **Step 5: Regenerate and validate**

```bash
python3 tools/docs_check.py --index
```

Expected: exits 0. `INDEX.md` and `ROUTING.md` are regenerated — never hand-edit them.

- [ ] **Step 6: Bump `updated:` in both docs to `2026-09-02`**

- [ ] **Step 7: Commit** *(hook-blocked — do not run)*

```bash
git add docs/
git commit -m "docs: rank wrapping, grounding and overlay scaling"
```

---

## Verification

Run before calling this done:

1. **EditMode tests** — `Window > General > Test Runner > EditMode > Run All`. Every test passes,
   including the pre-existing `RankLayoutTests` and `LobbyRankLayoutTests`.
2. **Docs** — `python3 tools/docs_check.py --index` exits 0.
3. **2 teams x 2 looks unchanged.** This is the regression that matters most; the whole design rests
   on it.
4. **8 teams x 3** — two rows, staggered, boots in the sand, no overlapping plates, own name visible.
5. **Aspect sweep** — Game view at 16:9, 4:3 and 21:9. The rank stays framed and clear of the status
   line in all three.
6. **On a client** — launch a second editor instance with `-sgprofile client`, join, and confirm the
   joining player's own team is what dims in the plates and what survives the name thinning. A
   host-only check proves nothing here.

## Out of scope

The 2D chrome on the roster page is **not** changed. At the extremes checked (810 px and 1440 px
canvas heights) the title, status line and footer do not collide — the small-screen failure was the
world camera ignoring the vertical band, which Task 7 fixes. Reflowing the chrome would be a
separate change with its own regression risk across every menu page that shares `MenuEntry`'s
constants.
