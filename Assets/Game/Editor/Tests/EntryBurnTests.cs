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
using UnityEngine;
using SpaceGame.Gameplay.Arrival;

namespace SpaceGame.EditorTools
{
    public class EntryBurnTests
    {
        private const float Tolerance = 0.0001f;

        /// <summary>A descent stepped at sixty frames a second, which is what the component sees.</summary>
        private const float FrameSeconds = 1f / 60f;

        /// <summary>The descent the arrival currently flies, in seconds.</summary>
        private const float DescentSeconds = 18.2f;

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

        // ─────────────────────────────────────────────
        //  The component, frame by frame
        // ─────────────────────────────────────────────
        //
        // The envelope above is a shape and was always covered. The STATE MACHINE that reads it was
        // not, and that is where the whole effect was being lost: EntryBurn latches itself off for
        // good once the burn is over, and the "is it over?" question used to be answered from the
        // intensity it had just computed. The ignition ramp is a smoothstep, so it climbs through
        // the sliver between zero and `cutoff` over about a fifth of a second — the first frame of
        // which looked exactly like the end of a burn. The sheath latched off before it had ever
        // drawn a frame, and the whole descent came down cold with a clean console.

        /// <summary>
        /// The regression test for that. Stepped at a real frame rate through a real descent, the
        /// sheath must actually reach full strength.
        /// </summary>
        [Test]
        public void LightsUpAcrossTheDescentInsteadOfLatchingOffDuringIgnition()
        {
            EntryBurn burn = NewBurn();

            try
            {
                float peak = 0f;

                for (float t = 0f; t <= DescentSeconds; t += FrameSeconds)
                {
                    burn.Advance(t, DescentSeconds, t);
                    peak = Mathf.Max(peak, burn.Burn);
                }

                Assert.AreEqual(1f, peak, Tolerance,
                    "The sheath never reached full strength over a whole descent. It latched itself " +
                    "off inside the ignition ramp, which is what 'no flames appear, nothing' looks " +
                    "like from the cabin — see EntryBurn.alight.");
            }
            finally
            {
                Cleanup(burn);
            }
        }

        /// <summary>
        /// The other half of the latch, and the reason it exists: once the fire is out the component
        /// stops working for the rest of the session rather than evaluating a curve that can only
        /// return zero. A wreck stands in the world for a long time.
        /// </summary>
        [Test]
        public void StaysOutOnceTheBurnHasFinished()
        {
            EntryBurn burn = NewBurn();

            try
            {
                for (float t = 0f; t <= DescentSeconds; t += FrameSeconds)
                    burn.Advance(t, DescentSeconds, t);

                Assert.AreEqual(0f, burn.Burn, Tolerance, "The burn is still alight at the ground.");

                // Asked again about the middle of the arc, where the curve says "full". A hull that
                // has already burned is past it and must not relight.
                burn.Advance(DescentSeconds * 0.3f, DescentSeconds, 0f);

                Assert.AreEqual(0f, burn.Burn, Tolerance,
                    "The sheath relit after the descent was over.");
            }
            finally
            {
                Cleanup(burn);
            }
        }

        /// <summary>
        /// A parked ship and a wreck loaded from a save both report a negative
        /// <c>SecondsSinceLaunch</c>, and neither is on fire. Nothing about that may latch the
        /// component off either — the same hull can be handed a real descent moments later.
        /// </summary>
        [Test]
        public void IsDarkBeforeLaunchAndStillAbleToBurnAfterwards()
        {
            EntryBurn burn = NewBurn();

            try
            {
                for (int frame = 0; frame < 120; frame++)
                    burn.Advance(-1f, DescentSeconds, frame * FrameSeconds);

                Assert.AreEqual(0f, burn.Burn, Tolerance, "An unlaunched hull is on fire.");

                burn.Advance(DescentSeconds * 0.3f, DescentSeconds, 0f);

                Assert.AreEqual(1f, burn.Burn, Tolerance,
                    "The hull could not light after sitting unlaunched, so a ship that waited for " +
                    "its crew arrives cold.");
            }
            finally
            {
                Cleanup(burn);
            }
        }

        /// <summary>
        /// A bare component with no shell and no lamp: EditMode never runs Awake, so the
        /// MaterialPropertyBlock is not built and the two renderer references are null — which
        /// <see cref="EntryBurn.Advance"/> guards, because a hull can legitimately be built without
        /// either. What is being asserted here is the sequencing, and that needs neither.
        /// </summary>
        private static EntryBurn NewBurn() =>
            new GameObject("EntryBurnProbe").AddComponent<EntryBurn>();

        private static void Cleanup(EntryBurn burn)
        {
            if (burn != null) Object.DestroyImmediate(burn.gameObject);
        }
    }
}
