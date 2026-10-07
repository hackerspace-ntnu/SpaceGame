using NUnit.Framework;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The rule that keeps two teams from wearing the same suit.
    ///
    /// The swatch count arrives as an argument rather than being read from SuitPalette: the palette
    /// lives in Assembly-CSharp, which an assembly definition cannot reference, and passing the
    /// count in is what keeps this testable at all.
    /// </summary>
    public class TeamColorRulesTests
    {
        private const int Swatches = 14;

        [Test]
        public void SteppingForwardSkipsASwatchAnotherTeamWears()
        {
            int[] taken = { 4 };

            Assert.AreEqual(5, TeamColorRules.Step(3, 1, Swatches, taken));
        }

        [Test]
        public void SteppingBackwardSkipsToo()
        {
            int[] taken = { 4 };

            Assert.AreEqual(3, TeamColorRules.Step(5, -1, Swatches, taken));
        }

        [Test]
        public void SteppingWrapsAroundThePalette()
        {
            Assert.AreEqual(0, TeamColorRules.Step(Swatches - 1, 1, Swatches, new int[0]));
            Assert.AreEqual(Swatches - 1, TeamColorRules.Step(0, -1, Swatches, new int[0]));
        }

        [Test]
        public void SteppingSkipsARunOfTakenSwatches()
        {
            int[] taken = { 4, 5, 6 };

            Assert.AreEqual(7, TeamColorRules.Step(3, 1, Swatches, taken));
        }

        /// <summary>
        /// With every other swatch spoken for there is nowhere to go, and the answer has to be the
        /// colour already worn rather than a hang or a duplicate.
        /// </summary>
        [Test]
        public void SteppingWithNowhereToGoStaysPut()
        {
            var taken = new int[Swatches - 1];
            for (int i = 0; i < taken.Length; i++) taken[i] = i + 1;

            Assert.AreEqual(0, TeamColorRules.Step(0, 1, Swatches, taken));
        }

        /// <summary>
        /// A team's colour can arrive from a peer whose build has a bigger palette, so an index
        /// this build has never heard of is reachable rather than theoretical. Whatever comes back
        /// has to be a swatch this build can actually paint — including on the give-up path, where
        /// the answer is the colour already worn.
        /// </summary>
        [Test]
        public void AnOutOfRangeCurrentComesBackInsideThePalette()
        {
            var everythingTaken = new int[Swatches];
            for (int i = 0; i < everythingTaken.Length; i++) everythingTaken[i] = i;

            foreach (int wild in new[] { -5, -1, Swatches, 20, 400 })
            {
                int stepped = TeamColorRules.Step(wild, 1, Swatches, everythingTaken);

                Assert.GreaterOrEqual(stepped, 0, $"stepping from {wild} left the palette");
                Assert.Less(stepped, Swatches, $"stepping from {wild} left the palette");
            }
        }

        [Test]
        public void DefaultColorsAreAllDistinct()
        {
            int[] colors = TeamColorRules.DefaultColors(VersusRules.MaxTeams, Swatches);

            Assert.AreEqual(VersusRules.MaxTeams, colors.Length);

            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (int color in colors)
                Assert.IsTrue(seen.Add(color), "two teams start on the same swatch");
        }

        [Test]
        public void DefaultColorsAreInsideThePalette()
        {
            foreach (int color in TeamColorRules.DefaultColors(VersusRules.MaxTeams, Swatches))
            {
                Assert.GreaterOrEqual(color, 0);
                Assert.Less(color, Swatches);
            }
        }

        /// <summary>
        /// More teams than swatches cannot be made distinct, and the answer is a valid index for
        /// every team rather than an exception on a screen the host is looking at.
        /// </summary>
        [Test]
        public void DefaultColorsSurviveAPaletteSmallerThanTheTeamCount()
        {
            int[] colors = TeamColorRules.DefaultColors(6, swatchCount: 3);

            Assert.AreEqual(6, colors.Length);
            foreach (int color in colors)
            {
                Assert.GreaterOrEqual(color, 0);
                Assert.Less(color, 3);
            }
        }
    }
}
