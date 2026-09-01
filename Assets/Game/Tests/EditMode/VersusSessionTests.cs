using NUnit.Framework;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The handful of values that have to cross the load into the world scene.
    ///
    /// A static, like MatchSettings and WorldSession before it, because the menu that knows them is
    /// destroyed by the load that needs them. The tests here are mostly about the clearing: a
    /// static that outlives a match is exactly how the next one starts wearing the last one's
    /// colours.
    /// </summary>
    public class VersusSessionTests
    {
        [SetUp]
        public void Reset() => VersusSession.Clear();

        [TearDown]
        public void Clean() => VersusSession.Clear();

        [Test]
        public void StartsInactive()
        {
            Assert.IsFalse(VersusSession.IsActive);
        }

        [Test]
        public void BeginRecordsTheMatch()
        {
            VersusSession.Begin(teamCount: 3, teamSize: 2, localTeam: 1, teamColors: new[] { 4, 9, 1 });

            Assert.IsTrue(VersusSession.IsActive);
            Assert.AreEqual(3, VersusSession.TeamCount);
            Assert.AreEqual(2, VersusSession.TeamSize);
            Assert.AreEqual(1, VersusSession.LocalTeam);
            Assert.AreEqual(9, VersusSession.ColorOf(1));
        }

        /// <summary>
        /// Every field, not just the obvious ones. The colours matter most: the class doc's own
        /// warning is that a session left standing is how the next match starts wearing the last
        /// one's colours, and a Clear that missed the array would leave exactly that behind while
        /// every other field read as empty.
        /// </summary>
        [Test]
        public void ClearForgetsEverything()
        {
            VersusSession.Begin(3, 2, 1, new[] { 4, 9, 1 });
            VersusSession.Clear();

            Assert.IsFalse(VersusSession.IsActive);
            Assert.AreEqual(0, VersusSession.TeamCount);
            Assert.AreEqual(0, VersusSession.TeamSize);
            Assert.AreEqual(-1, VersusSession.LocalTeam);

            // Not "returns 0 because the team is unknown" — after a Clear there are no teams at
            // all, so every index is unknown and the fallback is the only answer left.
            Assert.AreEqual(0, VersusSession.ColorOf(1), "a cleared session still knows a colour");
        }

        /// <summary>
        /// The caller keeps its array. Lobby UI holds a live colour table and repaints it in place
        /// as teams recolour, so a session that aliased it would quietly follow edits made after
        /// the match was already staged — with no assignment through this class to point at.
        /// </summary>
        [Test]
        public void TheColoursAreCopiedRatherThanBorrowed()
        {
            int[] caller = { 4, 9 };

            VersusSession.Begin(2, 2, 0, caller);
            caller[0] = 11;

            Assert.AreEqual(4, VersusSession.ColorOf(0),
                            "the session followed a change the caller made after Begin");
        }

        /// <summary>
        /// A team index from a peer on a build with more teams must not throw on the way to a
        /// suit colour. Falling back is what keeps a mismatched build looking wrong rather than
        /// crashing.
        /// </summary>
        [Test]
        public void AnUnknownTeamHasAColourRatherThanAnException()
        {
            VersusSession.Begin(2, 2, 0, new[] { 4, 9 });

            Assert.DoesNotThrow(() => VersusSession.ColorOf(7));
            Assert.GreaterOrEqual(VersusSession.ColorOf(7), 0);
            Assert.GreaterOrEqual(VersusSession.ColorOf(-1), 0);
        }
    }
}
