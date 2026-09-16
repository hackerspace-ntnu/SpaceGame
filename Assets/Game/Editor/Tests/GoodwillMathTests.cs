// The goodwill meter's arithmetic (design §3.4).
//
// The hysteresis cases are the reason this file exists. A band boundary without slack is not a
// subtle bug: a player sitting on exactly −40 makes every nomad in the camp flip between offering
// to talk and opening fire, several times a second, and it reads as the game being broken rather
// than as a tribe making up its mind. It is also invisible in a diff and trivial to assert here.
using NUnit.Framework;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class GoodwillMathTests
    {
        private static GoodwillThresholds T => GoodwillThresholds.Default;

        private static GoodwillBand Raw(float v) => GoodwillMath.RawBandFor(v, T);
        private static GoodwillBand From(GoodwillBand previous, float v) =>
            GoodwillMath.BandFor(v, previous, T);

        // ── Where the bands sit ────────────────────────────────────────────────────

        [Test]
        public void TheBandsSitWhereTheDesignSaysTheyDo()
        {
            Assert.AreEqual(GoodwillBand.AtWar, Raw(-100f));
            Assert.AreEqual(GoodwillBand.AtWar, Raw(-80f), "at or below -80");
            Assert.AreEqual(GoodwillBand.HostileOnSight, Raw(-79f));
            Assert.AreEqual(GoodwillBand.HostileOnSight, Raw(-40f), "at or below -40");
            Assert.AreEqual(GoodwillBand.Wary, Raw(-39f));
            Assert.AreEqual(GoodwillBand.Wary, Raw(0f), "everybody starts here");
            Assert.AreEqual(GoodwillBand.Wary, Raw(19f));
            Assert.AreEqual(GoodwillBand.Friendly, Raw(20f), "at or above +20");
            Assert.AreEqual(GoodwillBand.Friendly, Raw(59f));
            Assert.AreEqual(GoodwillBand.Allied, Raw(60f), "at or above +60");
            Assert.AreEqual(GoodwillBand.Allied, Raw(100f));
        }

        [Test]
        public void TheGapBetweenHostileAndWaryBelongsToWary()
        {
            // The design's table labels Wary as "-20 ... +20" and Hostile-on-sight as "<= -40",
            // which leaves -39..-21 unnamed. It is Wary: the stance is unchanged there, which is
            // exactly what Wary means.
            Assert.AreEqual(GoodwillBand.Wary, Raw(-25f));
        }

        // ── Hysteresis: the case in the plan ───────────────────────────────────────

        [Test]
        public void MinusFortyEntersHostileAndItTakesMinusTwentyNineToLeave()
        {
            Assert.AreEqual(GoodwillBand.HostileOnSight, From(GoodwillBand.Wary, -40f),
                            "-40 enters");
            Assert.AreEqual(GoodwillBand.HostileOnSight, From(GoodwillBand.HostileOnSight, -30f),
                            "-30 does not leave it");
            Assert.AreEqual(GoodwillBand.Wary, From(GoodwillBand.HostileOnSight, -29f),
                            "-29 does");
        }

        [Test]
        public void TheSlackWorksInBothDirections()
        {
            // Entering Friendly costs +20; falling out of it costs a drop to below +10.
            Assert.AreEqual(GoodwillBand.Friendly, From(GoodwillBand.Wary, 20f));
            Assert.AreEqual(GoodwillBand.Friendly, From(GoodwillBand.Friendly, 11f),
                            "a friend who slips a little is still a friend");
            Assert.AreEqual(GoodwillBand.Wary, From(GoodwillBand.Friendly, 9f));
        }

        [Test]
        public void SittingExactlyOnABoundaryDoesNotFlicker()
        {
            GoodwillBand band = GoodwillBand.Wary;

            // The value the player is hovering at, jittering by a hair either side.
            foreach (float v in new[] { -40f, -39.9f, -40.1f, -40f, -39.5f, -40f })
                band = GoodwillMath.BandFor(v, band, T);

            Assert.AreEqual(GoodwillBand.HostileOnSight, band,
                "once entered, small jitter around the boundary must not bounce the band");
        }

        /// <summary>
        /// A player who was Friendly and then murders somebody drops several bands at once. They
        /// must land where the value actually is, not one band down from where they were — which
        /// is why the slack is applied to the band being LEFT rather than to each threshold.
        /// </summary>
        [Test]
        public void ABigSwingLandsWhereItActuallyIsRatherThanOneBandAway()
        {
            Assert.AreEqual(GoodwillBand.AtWar, From(GoodwillBand.Friendly, -85f));
            Assert.AreEqual(GoodwillBand.Allied, From(GoodwillBand.AtWar, 90f));
        }

        [Test]
        public void ABandIsAlwaysItsOwnAnswer()
        {
            foreach (GoodwillBand b in System.Enum.GetValues(typeof(GoodwillBand)))
                Assert.AreEqual(b, GoodwillMath.BandFor(ValueInside(b), b, T), b.ToString());
        }

        private static float ValueInside(GoodwillBand band) => band switch
        {
            GoodwillBand.AtWar => -90f,
            GoodwillBand.HostileOnSight => -60f,
            GoodwillBand.Wary => 0f,
            GoodwillBand.Friendly => 40f,
            _ => 80f,
        };

        // ── What a hit costs ───────────────────────────────────────────────────────

        [Test]
        public void AHitCostsMoreTheNearerItComesToKilling()
        {
            float graze = GoodwillMath.HitDelta(5f, 100f, 2f, 10f);
            float solid = GoodwillMath.HitDelta(50f, 100f, 2f, 10f);
            float nearlyFatal = GoodwillMath.HitDelta(100f, 100f, 2f, 10f);

            Assert.Less(graze, 0f, "any landed hit costs something");
            Assert.Less(solid, graze, "a real hit costs more than a graze");
            Assert.Less(nearlyFatal, solid);
            Assert.AreEqual(-10f, nearlyFatal, 1e-3f, "a hit that would kill outright costs the max");
        }

        [Test]
        public void EveryLandedHitCostsAtLeastTheFloor()
        {
            // The floor is what stops a player whittling a camp down for free with a weak weapon.
            Assert.AreEqual(-2f, GoodwillMath.HitDelta(0.001f, 100f, 2f, 10f), 1e-2f);
        }

        [Test]
        public void AHitThatDidNotLandCostsNothing()
        {
            Assert.AreEqual(0f, GoodwillMath.HitDelta(0f, 100f, 2f, 10f), 1e-4f);
            Assert.AreEqual(0f, GoodwillMath.HitDelta(-5f, 100f, 2f, 10f), 1e-4f);
        }

        [Test]
        public void OverkillIsNotWorseThanAKill()
        {
            Assert.AreEqual(GoodwillMath.HitDelta(100f, 100f, 2f, 10f),
                            GoodwillMath.HitDelta(500f, 100f, 2f, 10f), 1e-4f);
        }

        // ── Decay ──────────────────────────────────────────────────────────────────

        [Test]
        public void TimeDriftsTowardZeroFromBothSides()
        {
            Assert.AreEqual(-40f, GoodwillMath.Decay(-50f, 5f, 2f), 1e-3f, "a grudge fades");
            Assert.AreEqual(40f, GoodwillMath.Decay(50f, 5f, 2f), 1e-3f,
                            "and so does a favour nobody has renewed");
        }

        [Test]
        public void DecayNeverCrossesZero()
        {
            Assert.AreEqual(0f, GoodwillMath.Decay(-5f, 100f, 2f), 1e-4f);
            Assert.AreEqual(0f, GoodwillMath.Decay(5f, 100f, 2f), 1e-4f,
                "sitting still must not turn an old friendship into an enmity — that is not " +
                "something time does");
        }

        [Test]
        public void NoTimePassingChangesNothing()
        {
            Assert.AreEqual(-50f, GoodwillMath.Decay(-50f, 0f, 2f), 1e-4f);
            Assert.AreEqual(-50f, GoodwillMath.Decay(-50f, 5f, 0f), 1e-4f, "a tribe that never forgets");
        }

        /// <summary>
        /// The brake on design §3.4's positive feedback loop: damage makes them hostile, which makes
        /// the player defend themselves, which is more damage. An absence has to be able to undo a
        /// war, or one accidental shot ends with a tribe permanently at war with one person.
        /// </summary>
        [Test]
        public void AWarCanBeWaitedOut()
        {
            float v = -90f;
            Assert.AreEqual(GoodwillBand.AtWar, Raw(v));

            v = GoodwillMath.Decay(v, 45f, 2f);   // 45 in-game hours away

            Assert.AreEqual(GoodwillBand.Wary, GoodwillMath.BandFor(v, GoodwillBand.AtWar, T),
                            "long enough away and the tribe has moved on");
        }

        // ── Clamping and the two questions callers ask ─────────────────────────────

        [Test]
        public void TheMeterCannotLeaveItsRange()
        {
            Assert.AreEqual(GoodwillMath.Min, GoodwillMath.Apply(-95f, -50f), 1e-4f);
            Assert.AreEqual(GoodwillMath.Max, GoodwillMath.Apply(95f, 50f), 1e-4f);
        }

        [Test]
        public void HostileAndHuntingMeanWhatTheySay()
        {
            Assert.IsTrue(GoodwillMath.IsHostile(GoodwillBand.AtWar));
            Assert.IsTrue(GoodwillMath.IsHostile(GoodwillBand.HostileOnSight));
            Assert.IsFalse(GoodwillMath.IsHostile(GoodwillBand.Wary));
            Assert.IsFalse(GoodwillMath.IsHostile(GoodwillBand.Friendly));
            Assert.IsFalse(GoodwillMath.IsHostile(GoodwillBand.Allied));

            Assert.IsTrue(GoodwillMath.IsHunting(GoodwillBand.AtWar));
            Assert.IsFalse(GoodwillMath.IsHunting(GoodwillBand.HostileOnSight),
                           "hostile on sight is not the same as coming to find you");
        }
    }
}
