using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Where the astronauts stand.
    ///
    /// These assert relationships, not metres — the same discipline LobbyLayoutTests uses on the
    /// join page, and for the same reason: the numbers are worked out on paper and there is no way
    /// to look at the result from here, so what has to be pinned is that teams stay apart, seats
    /// stay in their team, and the whole rank stays inside the shot.
    /// </summary>
    public class RankLayoutTests
    {
        [Test]
        public void SeatsInOneTeamAreCloserThanTheGapBetweenTeams()
        {
            Assert.Less(RankLayout.SeatSpacing, RankLayout.TeamGap,
                        "the clusters would read as one line");
        }

        [Test]
        public void ASmallTeamStandsInOneRow()
        {
            Assert.AreEqual(1, RankLayout.RowsFor(RankLayout.MaxSeatsPerRow));
        }

        [Test]
        public void ALargeTeamWrapsToASecondRow()
        {
            Assert.AreEqual(2, RankLayout.RowsFor(RankLayout.MaxSeatsPerRow + 1));
        }

        [Test]
        public void EverySeatOfATeamIsPlacedSomewhereDifferent()
        {
            const int teams = 3;
            const int teamSize = 5;

            var seen = new System.Collections.Generic.HashSet<Vector3>();

            for (int team = 0; team < teams; team++)
                for (int seat = 0; seat < teamSize; seat++)
                    Assert.IsTrue(seen.Add(RankLayout.SeatPosition(team, seat, teams, teamSize)),
                                  $"team {team} seat {seat} stands inside someone else");
        }

        /// <summary>
        /// The gap is the whole point of the grouping, so it is asserted directly: the nearest two
        /// seats across a team boundary must be further apart than two seats inside one team.
        /// </summary>
        [Test]
        public void TeamsAreSeparatedByMoreThanTheirOwnSeatSpacing()
        {
            const int teams = 2;
            const int teamSize = 3;

            float insideTeam = Vector3.Distance(RankLayout.SeatPosition(0, 0, teams, teamSize),
                                                RankLayout.SeatPosition(0, 1, teams, teamSize));

            float acrossTeams = Vector3.Distance(RankLayout.SeatPosition(0, teamSize - 1, teams, teamSize),
                                                 RankLayout.SeatPosition(1, 0, teams, teamSize));

            Assert.Greater(acrossTeams, insideTeam);
        }

        [Test]
        public void ATeamCentreSitsBetweenItsOwnSeats()
        {
            const int teams = 2;
            const int teamSize = 4;

            float centre = RankLayout.TeamCenter(0, teams, teamSize).x;
            float first = RankLayout.SeatPosition(0, 0, teams, teamSize).x;
            float last = RankLayout.SeatPosition(0, teamSize - 1, teams, teamSize).x;

            Assert.GreaterOrEqual(centre, Mathf.Min(first, last));
            Assert.LessOrEqual(centre, Mathf.Max(first, last));
        }

        [Test]
        public void TheRankIsCentredOnTheAnchor()
        {
            const int teams = 4;
            const int teamSize = 3;

            float left = RankLayout.TeamCenter(0, teams, teamSize).x;
            float right = RankLayout.TeamCenter(teams - 1, teams, teamSize).x;

            Assert.AreEqual(0f, left + right, 0.001f, "the rank drifts off its anchor");
        }

        [Test]
        public void AWiderRankNeedsTheCameraFurtherBack()
        {
            float near = RankLayout.CameraDistance(RankLayout.TotalWidth(2, 2), 60f, margin: 1.2f);
            float far = RankLayout.CameraDistance(RankLayout.TotalWidth(6, 4), 60f, margin: 1.2f);

            Assert.Greater(far, near);
        }

        /// <summary>
        /// The widest rank the rules allow still gets framed with air to spare.
        ///
        /// Asserted against the margin's EFFECT rather than by re-deriving the distance formula:
        /// multiplying the returned distance back by the same tangent only ever restates
        /// <c>width * margin / 2</c>, which is true for any width and would go on passing if the
        /// margin stopped being applied at all.
        /// </summary>
        [Test]
        public void TheFullestRankStillFitsTheShot()
        {
            float width = RankLayout.TotalWidth(VersusRules.MaxTeams,
                                                VersusRules.MaxSeats / VersusRules.MaxTeams);

            float snug = RankLayout.CameraDistance(width, 60f, margin: 1f);
            float roomy = RankLayout.CameraDistance(width, 60f, margin: 1.2f);

            Assert.Greater(roomy, snug, "the margin is not being applied");

            // At the snug distance the rank exactly fills the frame, so the roomy one has to
            // leave real air: the rank's half-width must fall short of the half-frame.
            float halfFrame = roomy * Mathf.Tan(60f * 0.5f * Mathf.Deg2Rad);

            Assert.Less(width * 0.5f, halfFrame, "the widest rank is clipped");
            Assert.AreEqual(1f / 1.2f, width / (halfFrame * 2f), 0.001f,
                            "the rank should fill 1/margin of the frame");
        }

        /// <summary>
        /// A team of five is four and one, and the one stands in the middle rather than hanging off
        /// the left edge where a naive row-major layout would put it.
        /// </summary>
        [Test]
        public void APartlyFilledLastRowIsCentredUnderTheOneAboveIt()
        {
            const int teams = 1;
            const int teamSize = 5;

            float front = 0f;
            for (int seat = 0; seat < RankLayout.MaxSeatsPerRow; seat++)
                front += RankLayout.SeatPosition(0, seat, teams, teamSize).x;

            front /= RankLayout.MaxSeatsPerRow;

            Vector3 lonely = RankLayout.SeatPosition(0, RankLayout.MaxSeatsPerRow, teams, teamSize);

            Assert.AreEqual(front, lonely.x, 0.001f, "the last row is not centred");
            Assert.Greater(lonely.z, 0f, "the last row should stand behind the first");
        }

        /// <summary>
        /// The whole promise of the team wrap: the shapes people actually play must not move a
        /// millimetre. Pinned against the pre-change formula rather than against recorded output, so
        /// a later edit to the wrap cannot quietly re-space a two-team lobby.
        /// </summary>
        [Test]
        public void FourTeamsOrFewerStandExactlyWhereTheyAlwaysDid()
        {
            const int teamSize = 3;

            for (int teams = VersusRules.MinTeams; teams <= RankLayout.MaxTeamsPerRow; teams++)
            {
                float pitch = RankLayout.TeamWidth(teamSize) + RankLayout.TeamGap;

                for (int team = 0; team < teams; team++)
                {
                    Vector3 centre = RankLayout.TeamCenter(team, teams, teamSize);

                    Assert.AreEqual((team - (teams - 1) * 0.5f) * pitch, centre.x, 0.0001f,
                                    "a team of a one-row rank has moved sideways");
                    Assert.AreEqual(0f, centre.z, 0.0001f, "a single row must not be pushed back");
                }

                Assert.AreEqual(teams * RankLayout.TeamWidth(teamSize) + (teams - 1) * RankLayout.TeamGap,
                                RankLayout.TotalWidth(teams, teamSize), 0.0001f,
                                "a one-row rank no longer measures what it used to");
            }
        }

        [Test]
        public void TeamsWrapOnceThereAreMoreThanARowsWorth()
        {
            Assert.AreEqual(1, RankLayout.TeamRowsFor(RankLayout.MaxTeamsPerRow));
            Assert.AreEqual(2, RankLayout.TeamRowsFor(RankLayout.MaxTeamsPerRow + 1));
        }

        [Test]
        public void TheSecondRowOfTeamsStandsBehindTheFirst()
        {
            const int teams = 8;
            const int teamSize = 3;

            Assert.Greater(RankLayout.TeamCenter(RankLayout.MaxTeamsPerRow, teams, teamSize).z,
                           RankLayout.TeamCenter(0, teams, teamSize).z,
                           "the second row of teams is not behind the first");
        }

        /// <summary>
        /// The stagger is what stops a back team hiding behind a front one, and it holds for every
        /// legal shape rather than only the one this test used to check: no back-row team is ever
        /// aligned with a front-row team, and the offset is exactly half a pitch.
        /// </summary>
        [Test]
        public void ABackRowTeamIsNeverLinedUpBehindAFrontRowTeam()
        {
            for (int teams = RankLayout.MaxTeamsPerRow + 1; teams <= VersusRules.MaxTeams; teams++)
            {
                int teamSize = VersusRules.ClampTeamSize(VersusRules.MaxTeamSize, teams);
                float pitch = RankLayout.TeamWidth(teamSize) + RankLayout.TeamGap;

                for (int back = RankLayout.MaxTeamsPerRow; back < teams; back++)
                {
                    float backX = RankLayout.TeamCenter(back, teams, teamSize).x;

                    for (int front = 0; front < RankLayout.MaxTeamsPerRow; front++)
                    {
                        float gap = Mathf.Abs(backX - RankLayout.TeamCenter(front, teams, teamSize).x);

                        Assert.GreaterOrEqual(gap, pitch * 0.5f - 0.001f,
                                              "a back-row team is lined up behind a front-row one");
                    }
                }
            }
        }

        /// <summary>
        /// Where the teams are narrow enough for it — three or fewer, so a team is no wider than the
        /// gap between two — the stagger drops a back team cleanly into that gap with no lateral
        /// overlap at all. This is the 8x3 shape, which is the one that made the wrap necessary.
        /// </summary>
        [Test]
        public void ANarrowBackRowTeamClearsTheFrontRowEntirely()
        {
            const int teams = 8;
            const int teamSize = 3;

            for (int back = RankLayout.MaxTeamsPerRow; back < teams; back++)
            {
                float backX = RankLayout.TeamCenter(back, teams, teamSize).x;

                for (int front = 0; front < RankLayout.MaxTeamsPerRow; front++)
                    Assert.Greater(Mathf.Abs(backX - RankLayout.TeamCenter(front, teams, teamSize).x),
                                   RankLayout.TeamWidth(teamSize),
                                   "a back-row team stands across a front-row one");
            }
        }

        [Test]
        public void WrappingStopsTheRankGettingMuchWider()
        {
            const int teamSize = 3;

            Assert.Less(RankLayout.TotalWidth(8, teamSize), RankLayout.TotalWidth(4, teamSize) * 1.6f,
                        "eight teams should not be far wider than four");
        }

        /// <summary>
        /// Every legal team count, not just the ones whose rows happen to be equally full. Five, six
        /// and seven teams have a short back row, and an earlier draft of the stagger left those
        /// sitting up to half a pitch to one side of the shot — invisible at eight teams, which was
        /// the only count this test used to cover.
        /// </summary>
        [Test]
        public void EveryTeamCountLeavesTheRankCentredOnTheAnchor()
        {
            for (int teams = VersusRules.MinTeams; teams <= VersusRules.MaxTeams; teams++)
            {
                int teamSize = VersusRules.ClampTeamSize(VersusRules.MaxTeamSize, teams);

                float min = float.MaxValue;
                float max = float.MinValue;

                for (int team = 0; team < teams; team++)
                {
                    float x = RankLayout.TeamCenter(team, teams, teamSize).x;
                    min = Mathf.Min(min, x);
                    max = Mathf.Max(max, x);
                }

                Assert.AreEqual(0f, min + max, 0.0001f, "the rank drifts off its anchor");
            }
        }

        [Test]
        public void TotalDepthGrowsWhenTeamsWrap()
        {
            const int teamSize = 3;

            Assert.Less(RankLayout.TotalDepth(4, teamSize), RankLayout.TotalDepth(8, teamSize));
        }

        /// <summary>
        /// Every seat of every legal lobby shape stands somewhere of its own, wrap and stagger
        /// included. This is what catches an off-by-one in the row arithmetic across the whole
        /// range, rather than at the one shape a test happened to pick.
        /// </summary>
        [Test]
        public void NoTwoPeopleShareASeatInAnyLegalLobby()
        {
            for (int teams = VersusRules.MinTeams; teams <= VersusRules.MaxTeams; teams++)
            {
                int teamSize = VersusRules.ClampTeamSize(VersusRules.MaxTeamSize, teams);
                var seen = new System.Collections.Generic.HashSet<Vector3>();

                for (int team = 0; team < teams; team++)
                    for (int seat = 0; seat < teamSize; seat++)
                        Assert.IsTrue(seen.Add(RankLayout.SeatPosition(team, seat, teams, teamSize)),
                                      "two people were placed in the same seat");
            }
        }

        [Test]
        public void TheTwoAxisFitTakesWhicheverAxisNeedsMoreRoom()
        {
            float wide = RankLayout.CameraDistance(20f, 2f, 90f, 50f, margin: 1.2f);
            float tall = RankLayout.CameraDistance(2f, 20f, 90f, 50f, margin: 1.2f);

            Assert.AreEqual(RankLayout.CameraDistance(20f, 90f, 1.2f), wide, 0.0001f);
            Assert.AreEqual(RankLayout.CameraDistance(20f, 50f, 1.2f), tall, 0.0001f);
        }

        /// <summary>
        /// The small-window case: the same rank in a frame whose usable height has shrunk has less
        /// room, so it can only ever push the camera further away.
        /// </summary>
        [Test]
        public void AShorterBandOfScreenNeverPullsTheCameraIn()
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
        /// With two rows the eye has to clear head height or the back row is simply behind the front
        /// one. The lobby's authored eye is 1.389 m — below a 1.8 m head — which is why this exists.
        /// </summary>
        [Test]
        public void TwoRowsOfTeamsLiftTheEyeAboveHeadHeight()
        {
            Assert.Greater(RankLayout.EyeHeight(8, 3, distance: 12.4f), RankLayout.HeadHeight,
                           "the back row is occluded at this eye height");
        }

        [Test]
        public void AFurtherCameraNeedsAHigherEyeToSeeOverTheFrontRow()
        {
            Assert.Greater(RankLayout.EyeHeight(8, 3, distance: 24f),
                           RankLayout.EyeHeight(8, 3, distance: 12f));
        }

        [Test]
        public void ABackRowsPlateHangsHigherThanAFrontRowsPlate()
        {
            Assert.Greater(RankLayout.PlateLift(team: 4, teams: 8),
                           RankLayout.PlateLift(team: 0, teams: 8),
                           "on screen, height is what says which row a plate belongs to");
        }

        [Test]
        public void ASingleRowOfTeamsKeepsTheFrontPlateLift()
        {
            Assert.AreEqual(RankLayout.PlateLiftFront, RankLayout.MaxPlateLift(4), 0.0001f,
                            "a one-row rank must reproduce the authored shot's plate height");
        }

        /// <summary>
        /// The defect this whole arrangement exists to kill, asserted at maximum capacity: with
        /// eight teams up, a front-row plate and the staggered back-row plate behind it must be
        /// clearly apart in the frame. From a near-level eye with one shared lift they projected
        /// about a quarter of a degree apart — the smear in the 8-team capture.
        ///
        /// The camera here is the fitted one: distance from <see cref="RankLayout.CameraDistance"/>
        /// at the project's 60°/16:9 field of view, eye from <see cref="RankLayout.EyeHeight"/>,
        /// aimed at head height the way <c>LobbyPreviewCamera.Fit</c> aims it.
        /// </summary>
        [Test]
        public void AtMaximumCapacityTheTwoPlateRowsAreClearlyApartInTheFrame()
        {
            const int teams = 8;
            const int teamSize = 3; // 8 x 3 = the 24-seat ceiling

            float width = RankLayout.TotalWidth(teams, teamSize);
            float height = RankLayout.MaxPlateLift(teams) + 2f;
            float distance = RankLayout.CameraDistance(width, height, 91.5f, 60f, margin: 1.2f);
            float eye = RankLayout.EyeHeight(teams, teamSize, distance);

            // Elevation of each plate as seen from the eye, in degrees below level. The back row
            // stands a full team-row pitch further from the camera.
            float frontDepth = distance;
            float backDepth = distance + RankLayout.TeamRowPitch(teamSize);

            float front = Mathf.Rad2Deg * Mathf.Atan2(RankLayout.PlateLift(0, teams) - eye, frontDepth);
            float back = Mathf.Rad2Deg * Mathf.Atan2(RankLayout.PlateLift(4, teams) - eye, backDepth);

            Assert.Greater(back - front, 3f,
                           "the two rows of team plates land in the same band of screen");
        }
    }
}
