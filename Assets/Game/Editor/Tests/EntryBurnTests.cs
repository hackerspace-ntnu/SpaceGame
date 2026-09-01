// The atmospheric entry burn, as arithmetic.
//
// The envelope is the whole design of this effect: WHEN the hull is on fire decides whether the
// descent reads as an entry followed by a crash, or as one undifferentiated orange smear. Every
// way it can go wrong is silent — a burn still at full strength behind the fade to black hides the
// ground rush, a burn that never lights leaves a cold descent with a clean console, and an
// inverted curve lights the cabin up during the crash it was written to stay out of. None of them
// throw, and all of them are a shape, so all of them can be asserted with no play mode, no shader
// and no ship.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Gameplay.Arrival;

namespace SpaceGame.EditorTools
{
    public class EntryBurnTests
    {
        private const float Tolerance = 0.0001f;

        /// <summary>The tuning the arrival actually flies.</summary>
        private static EntryBurnCurve Curve => EntryBurnCurve.Default;

        [Test]
        public void IsDarkAtTheTopOfTheArcAndAtTheGround()
        {
            Assert.AreEqual(0f, Curve.Intensity(0f), Tolerance,
                "Launching straight into a full burn reads as an effect being switched on rather " +
                "than as the ship entering something.");
            Assert.AreEqual(0f, Curve.Intensity(1f), Tolerance);
        }

        [Test]
        public void ReachesFullStrengthAcrossTheMiddleOfTheDescent()
        {
            Assert.AreEqual(1f, Curve.Intensity(0.3f), Tolerance);
        }

        /// <summary>
        /// The contract the whole envelope exists for. <c>ArrivalCutscene</c> starts its fade to
        /// black <c>impactFade</c> before contact and the last stretch of the descent is the ground
        /// rush — the beat the shake curve is built to peak on. A sheath still alight through it
        /// blows the window out to white orange over exactly the thing the descent is building
        /// toward, and the two effects then compete instead of handing over.
        /// </summary>
        [Test]
        public void IsOutBeforeTheGroundRush()
        {
            Assert.AreEqual(0f, Curve.Intensity(0.75f), Tolerance);
            Assert.AreEqual(0f, Curve.Intensity(0.95f), Tolerance,
                "The last seconds of the arc belong to the ground coming up.");
        }

        [Test]
        public void RisesAndFallsWithoutEverExceedingFull()
        {
            float previous = 0f;
            for (float t = 0f; t <= Curve.Full; t += 0.01f)
            {
                float now = Curve.Intensity(t);
                Assert.GreaterOrEqual(now + Tolerance, previous, $"The burn dips while igniting, at {t}.");
                previous = now;
            }

            previous = 1f;
            for (float t = Curve.Fade; t <= 1f; t += 0.01f)
            {
                float now = Curve.Intensity(t);
                Assert.LessOrEqual(now - Tolerance, previous, $"The burn flares up while dying, at {t}.");
                previous = now;
            }

            for (float t = 0f; t <= 1f; t += 0.01f)
            {
                float now = Curve.Intensity(t);
                Assert.GreaterOrEqual(now, 0f, $"Negative burn at {t}.");
                Assert.LessOrEqual(now, 1f, $"Burn over full at {t}.");
            }
        }

        /// <summary>
        /// A late joiner is seated into a descent already under way, and one seated after the
        /// landing asks about a progress past 1. Extrapolating off the end of the curve there gives
        /// a negative intensity or, worse, a second ignition in a hull that is already wrecked.
        /// </summary>
        [Test]
        public void ClampsOutsideTheDescentRatherThanExtrapolating()
        {
            Assert.AreEqual(0f, Curve.Intensity(-3f), Tolerance);
            Assert.AreEqual(0f, Curve.Intensity(40f), Tolerance);
        }

        /// <summary>
        /// The four points are Inspector fields and nothing stops somebody dragging them out of
        /// order. The worst that may do is shorten the burn; it must never invert it and light the
        /// cabin up during the crash the fire is timed to stay out of.
        /// </summary>
        [Test]
        public void SurvivesAnOutOfOrderCurve()
        {
            var scrambled = new EntryBurnCurve { Ignite = 0.8f, Full = 0.2f, Fade = 0.1f, Extinguish = 0.05f };

            for (float t = 0f; t <= 1f; t += 0.01f)
            {
                float now = scrambled.Intensity(t);
                Assert.GreaterOrEqual(now, 0f, $"Negative burn at {t} on a scrambled curve.");
                Assert.LessOrEqual(now, 1f, $"Burn over full at {t} on a scrambled curve.");
            }
        }

        /// <summary>
        /// Points collapsed onto each other are a zero-width edge, and Mathf.SmoothStep answers 0
        /// for one. Taken literally that makes a burn told to reach full strength instantly vanish
        /// instead — the opposite of what was asked for.
        /// </summary>
        [Test]
        public void HoldsFullThroughACollapsedEdgeRatherThanGoingDark()
        {
            var instant = new EntryBurnCurve { Ignite = 0.2f, Full = 0.2f, Fade = 0.6f, Extinguish = 0.6f };

            Assert.AreEqual(1f, instant.Intensity(0.4f), Tolerance);
            Assert.AreEqual(0f, instant.Intensity(0.1f), Tolerance);
            Assert.AreEqual(0f, instant.Intensity(0.7f), Tolerance);
        }

        /// <summary>
        /// The flicker is a shimmer, never a strobe. A crew member sits inside this light for the
        /// better part of twenty seconds with no way to look away from it, so the bound is the
        /// accessibility half of the effect (GDC-L1-UX-0006) and not a tuning preference.
        /// </summary>
        [Test]
        public void FlickerStaysWithinItsDepth()
        {
            const float depth = 0.18f;

            for (float t = 0f; t < 30f; t += 0.005f)
            {
                float f = EntryBurnCurve.Flicker(t, 2.7f, depth);
                Assert.GreaterOrEqual(f, 1f - depth - Tolerance, $"Flicker dipped past its depth at {t}.");
                Assert.LessOrEqual(f, 1f + depth + Tolerance, $"Flicker spiked past its depth at {t}.");
            }
        }

        /// <summary>
        /// Two frequencies beating against each other rather than one, because a single sine is a
        /// pulse and a pulse reads as machinery rather than as fire. The test for that is that the
        /// signal does not repeat on the period of its own base frequency.
        /// </summary>
        [Test]
        public void FlickerIsNotASinglePulse()
        {
            const float hz = 2.7f;
            float period = 1f / hz;

            Assert.AreNotEqual(EntryBurnCurve.Flicker(0.13f, hz, 0.18f),
                               EntryBurnCurve.Flicker(0.13f + period, hz, 0.18f),
                               "The flicker repeats every base period, so it is one sine and reads " +
                               "as a blinking lamp.");
        }

        /// <summary>Depth zero is steady light, not darkness — the toggle-off case.</summary>
        [Test]
        public void ZeroDepthIsSteady()
        {
            Assert.AreEqual(1f, EntryBurnCurve.Flicker(4.2f, 2.7f, 0f), Tolerance);
        }
    }
}
