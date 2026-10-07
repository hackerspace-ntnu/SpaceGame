// The aggression meter's arithmetic (design §3.3), which is where all of its interesting cases
// are: what one hit is worth against what a graze is worth, and whether a noise the player makes
// repeatedly can stack up faster than the agent forgives it.
//
// Pure, because accumulation over time is what matters here and a MonoBehaviour is a bad place to
// test seven shots inside a cooling window.
using NUnit.Framework;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class AggressionMathTests
    {
        private static AggressionSettings Settings => AggressionSettings.Default;

        private static float AddOnce(float value, AggressionInput input, float magnitude) =>
            AggressionMath.Apply(value, AggressionMath.Gain(input, magnitude, Settings));

        // ── What a hit is worth ────────────────────────────────────────────────────

        /// <summary>
        /// The calibration that matters most, because it is the behaviour this component had before
        /// it had a meter at all: a real hit is a fight, immediately. ProvocationTests has always
        /// called 9 damage on a 100 HP creature a real hit, and it still is.
        /// </summary>
        [Test]
        public void ARealHitIsStillAnInstantFight()
        {
            Assert.AreEqual(AggressionBand.Grudge,
                            AggressionMath.BandFor(AddOnce(0f, AggressionInput.Hit, 0.09f)),
                            "9 damage on a 100 HP creature");
            Assert.AreEqual(AggressionMath.Max, AddOnce(0f, AggressionInput.Hit, 1f), 1e-3f,
                            "and so, obviously, is a killing blow");
        }

        [Test]
        public void AFivePercentGrazeDoesNotStartAFight()
        {
            float after = AddOnce(0f, AggressionInput.Hit, 0.05f);

            Assert.Less(after, AggressionMath.Max, "a graze is not a declaration of war");
            Assert.AreEqual(AggressionBand.Wary, AggressionMath.BandFor(after),
                            "though it is certainly worth looking up about");
        }

        [Test]
        public void AOnePercentScratchIsNotEvenWorthLookingUpAbout()
        {
            Assert.AreEqual(AggressionBand.Calm,
                            AggressionMath.BandFor(AddOnce(0f, AggressionInput.Hit, 0.01f)));
        }

        [Test]
        public void TwoGrazesAddUpToAFight()
        {
            float v = 0f;
            for (int i = 0; i < 2; i++) v = AddOnce(v, AggressionInput.Hit, 0.05f);

            Assert.AreEqual(AggressionBand.Grudge, AggressionMath.BandFor(v),
                            "chip damage accumulating is the whole point of a meter — you cannot " +
                            "whittle somebody down for free just because no single shot was solid");
        }

        // ── Cooling ────────────────────────────────────────────────────────────────

        [Test]
        public void CoolingStopsAtZeroAndNeverGoesNegative()
        {
            float v = AggressionMath.Cool(25f, 10f, Settings.calmRate);

            Assert.AreEqual(0f, v, 1e-4f);
            Assert.AreEqual(0f, AggressionMath.Cool(v, 100f, Settings.calmRate), 1e-4f,
                            "a negative meter would be a bank of forgiveness to spend later");
        }

        [Test]
        public void CoolingIsProportionalToTime()
        {
            Assert.AreEqual(90f, AggressionMath.Cool(100f, 1f, 10f), 1e-4f);
            Assert.AreEqual(50f, AggressionMath.Cool(100f, 5f, 10f), 1e-4f);
        }

        [Test]
        public void ZeroCalmRateMeansTheAgentNeverForgives()
        {
            Assert.AreEqual(70f, AggressionMath.Cool(70f, 30f, 0f), 1e-4f);
        }

        // ── Accumulation against forgiveness ───────────────────────────────────────

        /// <summary>
        /// The case the meter exists for. A gunshot is 15 points, so seven of them inside the
        /// cooling window start a fight and six do not — firing near a camp is a thing you can do
        /// a few times and then cannot.
        /// </summary>
        [Test]
        public void SevenGunshotsInsideTheCoolingWindowStartAFight()
        {
            float six = 0f;
            for (int i = 0; i < 6; i++) six = AddOnce(six, AggressionInput.Gunshot, 1f);
            Assert.AreNotEqual(AggressionBand.Grudge, AggressionMath.BandFor(six),
                               "six shots is a warning");
            Assert.AreEqual(AggressionBand.Drawn, AggressionMath.BandFor(six));

            float seven = AddOnce(six, AggressionInput.Gunshot, 1f);
            Assert.AreEqual(AggressionBand.Grudge, AggressionMath.BandFor(seven), "the seventh is a fight");
        }

        [Test]
        public void ShotsSpacedOutNeverAddUp()
        {
            float v = 0f;
            for (int i = 0; i < 20; i++)
            {
                v = AddOnce(v, AggressionInput.Gunshot, 1f);
                v = AggressionMath.Cool(v, 2f, Settings.calmRate);   // 20 points shed per shot
            }

            Assert.AreEqual(AggressionBand.Calm, AggressionMath.BandFor(v),
                            "hunting near a camp all afternoon must not eventually provoke it");
        }

        [Test]
        public void TwoHurtAlliesAreAFight()
        {
            float v = AddOnce(0f, AggressionInput.AllyHurt, 1f);
            Assert.AreEqual(AggressionBand.Wary, AggressionMath.BandFor(v));

            v = AddOnce(v, AggressionInput.AllyHurt, 1f);
            Assert.AreEqual(AggressionBand.Drawn, AggressionMath.BandFor(v));
        }

        [Test]
        public void AimingAtSomebodyForFiveSecondsIsAFight()
        {
            // 20 points a second, so the meter is full in five — long enough to be a decision and
            // short enough that the player connects the posture to what they are doing.
            Assert.AreEqual(AggressionBand.Grudge,
                            AggressionMath.BandFor(AddOnce(0f, AggressionInput.Menace, 5f)));
            Assert.AreEqual(AggressionBand.Wary,
                            AggressionMath.BandFor(AddOnce(0f, AggressionInput.Menace, 2f)),
                            "two seconds is exactly the wary threshold — the boundary is inclusive");
        }

        // ── Clamping and degenerate inputs ─────────────────────────────────────────

        [Test]
        public void TheMeterIsAPercentageNotATally()
        {
            float v = AddOnce(0f, AggressionInput.Hit, 5f);   // five times its own health
            Assert.AreEqual(AggressionMath.Max, v, 1e-4f,
                            "overkill must not bank aggression that survives a full calm-down");
        }

        [Test]
        public void NothingHappenedIsWorthNothing()
        {
            Assert.AreEqual(0f, AggressionMath.Gain(AggressionInput.Hit, 0f, Settings), 1e-4f);
            Assert.AreEqual(0f, AggressionMath.Gain(AggressionInput.Gunshot, -3f, Settings), 1e-4f,
                            "a negative magnitude must not be forgiveness by another route");
        }

        // ── Bands ──────────────────────────────────────────────────────────────────

        [Test]
        public void TheBandsSitWhereTheDesignSaysTheyDo()
        {
            Assert.AreEqual(AggressionBand.Calm, AggressionMath.BandFor(0f));
            Assert.AreEqual(AggressionBand.Calm, AggressionMath.BandFor(39.9f));
            Assert.AreEqual(AggressionBand.Wary, AggressionMath.BandFor(40f));
            Assert.AreEqual(AggressionBand.Wary, AggressionMath.BandFor(79.9f));
            Assert.AreEqual(AggressionBand.Drawn, AggressionMath.BandFor(80f));
            Assert.AreEqual(AggressionBand.Drawn, AggressionMath.BandFor(99.9f));
            Assert.AreEqual(AggressionBand.Grudge, AggressionMath.BandFor(100f));
        }

        /// <summary>
        /// An agent that snaps early still shows all three bands on the way up. Scaling the
        /// thresholds against its own `attackAt` is what stops a short-tempered creature sitting
        /// calm until it is two points from attacking — which would be the binary version again.
        /// </summary>
        [Test]
        public void AShortTemperedAgentStillTelegraphs()
        {
            const float attackAt = 60f;

            Assert.AreEqual(AggressionBand.Calm, AggressionMath.BandFor(20f, attackAt));
            Assert.AreEqual(AggressionBand.Wary, AggressionMath.BandFor(30f, attackAt));
            Assert.AreEqual(AggressionBand.Drawn, AggressionMath.BandFor(50f, attackAt));
            Assert.AreEqual(AggressionBand.Grudge, AggressionMath.BandFor(60f, attackAt));
        }
    }
}
