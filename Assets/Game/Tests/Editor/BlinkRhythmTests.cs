using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The blink clock over two minutes of frames, including a long hitch. What is pinned is what a
    /// player would notice: eyes that stick shut, eyes that never close all the way, and a band of
    /// drifters blinking in unison.
    /// </summary>
    public class BlinkRhythmTests
    {
        private const float Frame = 1f / 60f;

        [Test]
        public void BlinksCloseFullyReopenPromptlyAndSurviveAHitch()
        {
            var rhythm = new BlinkRhythm();
            var random = new System.Random(7);
            var blinks = new List<(float peak, float shutFor, bool hitched)>();

            float peak = 0f;
            float shutFor = 0f;
            bool hitched = false;
            for (int frame = 0; frame < 120 * 60; frame++)
            {
                // Halfway through, one frame takes five seconds: a load or a breakpoint.
                bool hitch = frame == 60 * 60;
                hitched |= hitch;
                float closure = rhythm.Advance(hitch ? 5f : Frame, random);
                Assert.That(closure, Is.InRange(0f, 1f));

                if (closure > 0f)
                {
                    peak = System.Math.Max(peak, closure);
                    shutFor += Frame;
                }
                else if (shutFor > 0f)
                {
                    blinks.Add((peak, shutFor, hitched));
                    peak = 0f;
                    shutFor = 0f;
                    hitched = false;
                }
            }

            Assert.That(blinks.Count, Is.GreaterThan(10), "two minutes should hold a good many blinks");
            foreach (var (blinkPeak, blinkLength, blinkHitched) in blinks)
            {
                Assert.That(blinkLength, Is.LessThan(1f), "an eye is never left shut");

                // The hitch can land mid-blink and skip its shut beat; that one is excused.
                if (!blinkHitched)
                    Assert.That(blinkPeak, Is.EqualTo(1f), "every blink shuts the eye all the way");
            }
        }

        [Test]
        public void TwoFacesDoNotBlinkInUnison()
        {
            float first = FirstBlink(new System.Random(1));
            float second = FirstBlink(new System.Random(2));

            Assert.That(System.Math.Abs(first - second), Is.GreaterThan(Frame * 3),
                        "a band spawned together must not blink together");
        }

        private static float FirstBlink(System.Random random)
        {
            var rhythm = new BlinkRhythm();
            for (int frame = 0; frame < 30 * 60; frame++)
                if (rhythm.Advance(Frame, random) > 0f)
                    return frame * Frame;

            Assert.Fail("no blink in thirty seconds");
            return 0f;
        }
    }
}
