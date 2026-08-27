using NUnit.Framework;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The seat arithmetic behind the VS rules page and the lobby's host steppers.
    ///
    /// The interesting part is that teams and team size are not independent: their product is
    /// capped, so raising one has to be able to refuse rather than silently overrun the ceiling.
    /// </summary>
    public class VersusRulesTests
    {
        [Test]
        public void DefaultsFitTheCeiling()
        {
            Assert.LessOrEqual(VersusRules.Seats(VersusRules.DefaultTeams, VersusRules.DefaultTeamSize),
                               VersusRules.MaxSeats);
        }

        [Test]
        public void TeamsAreHeldWithinTheirOwnLimits()
        {
            Assert.AreEqual(VersusRules.MinTeams, VersusRules.ClampTeams(0, teamSize: 1));
            Assert.AreEqual(VersusRules.MaxTeams, VersusRules.ClampTeams(99, teamSize: 1));
        }

        [Test]
        public void TeamSizeIsHeldWithinItsOwnLimits()
        {
            Assert.AreEqual(VersusRules.MinTeamSize, VersusRules.ClampTeamSize(0, teams: 2));
            Assert.AreEqual(VersusRules.MaxTeamSize, VersusRules.ClampTeamSize(99, teams: 2));
        }

        /// <summary>The pair is what is capped, not either axis alone.</summary>
        [Test]
        public void TeamsCannotPushTheSeatTotalOverTheCeiling()
        {
            int teams = VersusRules.ClampTeams(VersusRules.MaxTeams, teamSize: VersusRules.MaxTeamSize);

            Assert.LessOrEqual(VersusRules.Seats(teams, VersusRules.MaxTeamSize), VersusRules.MaxSeats);
            Assert.GreaterOrEqual(teams, VersusRules.MinTeams, "clamping must never go below the floor");
        }

        [Test]
        public void TeamSizeCannotPushTheSeatTotalOverTheCeiling()
        {
            int size = VersusRules.ClampTeamSize(VersusRules.MaxTeamSize, teams: VersusRules.MaxTeams);

            Assert.LessOrEqual(VersusRules.Seats(VersusRules.MaxTeams, size), VersusRules.MaxSeats);
            Assert.GreaterOrEqual(size, VersusRules.MinTeamSize);
        }

        /// <summary>
        /// The contract callers actually rely on: run a count through <see cref="VersusRules.ClampTeams"/>,
        /// feed that result into <see cref="VersusRules.ClampTeamSize"/>, and the pair fits — whatever
        /// nonsense went in. Anything that pairs a clamped value with an unclamped one is outside
        /// what these methods promise, which is why they are always called together.
        /// </summary>
        [Test]
        public void ClampingBothAxesInTurnAlwaysYieldsAPairInsideTheCeiling()
        {
            int[] garbage = { -50, -1, 0, 1, 3, 7, 13, 99, 1000 };

            foreach (int rawTeams in garbage)
            {
                foreach (int rawSize in garbage)
                {
                    int teams = VersusRules.ClampTeams(rawTeams, rawSize);
                    int size = VersusRules.ClampTeamSize(rawSize, teams);

                    Assert.LessOrEqual(VersusRules.Seats(teams, size), VersusRules.MaxSeats,
                        $"({rawTeams}, {rawSize}) clamped to ({teams}, {size})");
                    Assert.GreaterOrEqual(teams, VersusRules.MinTeams);
                    Assert.GreaterOrEqual(size, VersusRules.MinTeamSize);
                }
            }
        }

        /// <summary>
        /// An out-of-range count is clamped BEFORE it is used to derive the ceiling, so the ceiling
        /// is computed from a real team count rather than from the nonsense that was passed in.
        /// Pinned by exact value because it is the only observable difference: reverted, this
        /// answers 1, because 24/1000 floors to 0 and the floor drags it to MinTeamSize.
        /// </summary>
        [Test]
        public void AnOutOfRangeTeamCountIsClampedBeforeTheCeilingIsDerived()
        {
            Assert.AreEqual(3, VersusRules.ClampTeamSize(1000, teams: 1000),
                            "1000 teams clamps to MaxTeams = 8 first, so the ceiling is 24 / 8 = 3");
        }

        // ───────────────────────────────────────────── the occupancy guards

        [Test]
        public void TeamSizeMayNotDropBelowTheFullestTeam()
        {
            int[] occupancy = { 3, 1 };

            Assert.IsFalse(VersusRules.CanSetTeamSize(2, occupancy, out string refusal));
            StringAssert.Contains(VersusRules.TeamName(0), refusal,
                "the refusal has to name the team that is in the way");
            Assert.IsTrue(VersusRules.CanSetTeamSize(3, occupancy, out _));
        }

        [Test]
        public void ATeamWithPlayersInItCannotBeRemoved()
        {
            int[] occupancy = { 1, 0, 2 };

            Assert.IsFalse(VersusRules.CanSetTeamCount(2, occupancy, out string refusal));
            StringAssert.Contains(VersusRules.TeamName(2), refusal);
        }

        [Test]
        public void AnEmptyTeamCanBeRemoved()
        {
            int[] threeTeams = { 1, 1, 0 };

            Assert.IsTrue(VersusRules.CanSetTeamCount(2, threeTeams, out _),
                          "the team being dropped has nobody in it");
        }

        [Test]
        public void GrowingIsAlwaysAllowed()
        {
            int[] occupancy = { 2, 2 };

            Assert.IsTrue(VersusRules.CanSetTeamSize(4, occupancy, out _));
            Assert.IsTrue(VersusRules.CanSetTeamCount(4, occupancy, out _));
        }

        /// <summary>
        /// A negative count is not a real request, but this class is the guard — a rulebook that
        /// throws is worse than one that refuses. Its sibling below has always handled this.
        /// </summary>
        [Test]
        public void ANegativeTeamCountIsRefusedRatherThanThrowing()
        {
            int[] occupancy = { 1, 1 };

            Assert.DoesNotThrow(() => VersusRules.CanSetTeamCount(-1, occupancy, out _));
            Assert.IsFalse(VersusRules.CanSetTeamCount(-1, occupancy, out _));
        }

        [Test]
        public void ANegativeTeamSizeIsRefusedRatherThanThrowing()
        {
            int[] occupancy = { 1, 1 };

            Assert.DoesNotThrow(() => VersusRules.CanSetTeamSize(-1, occupancy, out _));
            Assert.IsFalse(VersusRules.CanSetTeamSize(-1, occupancy, out _));
        }

        [Test]
        public void EveryTeamHasAName()
        {
            for (int i = 0; i < VersusRules.MaxTeams; i++)
                Assert.IsNotEmpty(VersusRules.TeamName(i), $"team {i} has no name");
        }

        [Test]
        public void TeamNamesAreDistinct()
        {
            var seen = new System.Collections.Generic.HashSet<string>();

            for (int i = 0; i < VersusRules.MaxTeams; i++)
                Assert.IsTrue(seen.Add(VersusRules.TeamName(i)), $"team {i} reuses a name");
        }
    }
}
