using NUnit.Framework;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    /// <summary>
    /// How a label behaves as the space it is drawn into shrinks.
    ///
    /// The rungs are asserted by what they mean rather than by their numbers: a label with plenty of
    /// room keeps its authored size, one with less shrinks, one that cannot shrink any further gets
    /// shorter, and the last rung is still a word — team identity is never left to colour alone.
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

        /// <summary>
        /// The threshold is derived rather than picked: the full name stops being an option exactly
        /// when the room falls below what it needs at <see cref="RankOverlayScale.MinFontSize"/>.
        /// A guessed number here passed while still reading "Scaled", because a name that shrinks to
        /// 18.4pt has not run out of room at all.
        /// </summary>
        [Test]
        public void TooLittleRoomToShrinkAnyFurtherShortensTheLabel()
        {
            float tooTightForTheFullName = FullWidth * RankOverlayScale.MinFontSize / Authored;

            LabelFit fit = Fit(tooTightForTheFullName - 5f);

            Assert.AreEqual(RankLabelRung.Shortened, fit.Rung);
            Assert.GreaterOrEqual(fit.FontSize, RankOverlayScale.MinFontSize);
        }

        /// <summary>
        /// At the bottom of the ladder legibility wins over overlap: a label nobody can read is not
        /// a smaller label, it is a missing one.
        /// </summary>
        [Test]
        public void TheLastRungIsStillLegibleRatherThanVanishinglySmall()
        {
            LabelFit fit = Fit(1f);

            Assert.AreEqual(RankLabelRung.Floor, fit.Rung);
            Assert.GreaterOrEqual(fit.FontSize, RankOverlayScale.MinFontSize);
        }

        [Test]
        public void TheLadderNeverClimbsBackUpAsRoomRunsOut()
        {
            RankLabelRung previous = RankLabelRung.Roomy;

            for (float available = FullWidth * 2f; available > 0f; available -= 5f)
            {
                RankLabelRung rung = Fit(available).Rung;

                Assert.GreaterOrEqual((int)rung, (int)previous, "the ladder climbed back up");
                previous = rung;
            }
        }

        [Test]
        public void FontSizeNeverExceedsTheAuthoredSize()
        {
            Assert.LessOrEqual(Fit(FullWidth * 10f).FontSize, Authored);
        }

        [Test]
        public void AZeroWidthLabelIsNotDividedBy()
        {
            Assert.AreEqual(Authored, RankOverlayScale.SizeFor(Authored, widthPx: 0f, availablePx: 10f),
                            0.0001f);
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
        /// You must always be able to find yourself. The bottom rung thins to two labels; it does
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
            Assert.AreEqual("1", VersusRules.ShortTeamName(0));
            Assert.AreEqual("3", VersusRules.ShortTeamName(2));
        }

        [Test]
        public void AShortTeamNameIsNeverEmpty()
        {
            for (int team = 0; team < VersusRules.MaxTeams; team++)
                Assert.IsNotEmpty(VersusRules.ShortTeamName(team));

            Assert.IsNotEmpty(VersusRules.ShortTeamName(VersusRules.MaxTeams + 5));
        }
    }
}
