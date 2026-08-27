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
    }
}
