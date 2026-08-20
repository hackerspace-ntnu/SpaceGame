using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// The climb limit's arithmetic.
    ///
    /// The reason this class exists at all is the tension between the two things it has to get right
    /// at once: a legged machine must NOT walk up a hillside, and it MUST still step onto a rock.
    /// A limit written against the local surface normal gets the first and loses the second, so most
    /// of what is pinned down here is the second -- the ledge, the boulder, the cliff whose average
    /// grade is diluted by the flat ground in front of it.
    ///
    /// Distances and heights are in metres, and the machine these numbers were chosen around has a
    /// 1.5 m step-up height and probes 3 m ahead in four steps of 0.75 m.
    public class WalkerClimbTests
    {
        private const float MaxAngle = 35f;
        private const float Taper = 5f;
        private const float StepUp = 1.5f;
        private const float Run = 3f;
        private const int Probes = 4;

        /// A profile whose height at distance d is `height(d)`.
        private static WalkerClimb.Sample[] Profile(System.Func<float, float> height)
        {
            var samples = new WalkerClimb.Sample[Probes];
            for (int i = 0; i < Probes; i++)
            {
                float d = Run * (i + 1) / Probes;
                samples[i] = new WalkerClimb.Sample { Distance = d, Height = height(d), Found = true };
            }
            return samples;
        }

        private static float Scale(WalkerClimb.Sample[] samples) =>
            WalkerClimb.TravelScale(0f, samples, samples.Length, MaxAngle, Taper, StepUp);

        /// Ground at a constant grade, which is the case every other one is a departure from.
        private static WalkerClimb.Sample[] Slope(float degrees) =>
            Profile(d => d * Mathf.Tan(degrees * Mathf.Deg2Rad));

        [Test]
        public void LevelGround_TravelsFreely()
        {
            Assert.AreEqual(1f, Scale(Profile(_ => 0f)), 1e-4f);
        }

        [Test]
        public void GentleSlope_TravelsFreely()
        {
            Assert.AreEqual(1f, Scale(Slope(15f)), 1e-4f);
        }

        [Test]
        public void SlopePastTheLimit_IsRefused()
        {
            Assert.AreEqual(0f, Scale(Slope(50f)), 1e-4f);
        }

        /// The band below the limit, which is what keeps the machine from meeting a hillside as if
        /// it were glass. Inside it there is still travel, and less of it the steeper the ground.
        [Test]
        public void ApproachingTheLimit_TapersRatherThanStops()
        {
            float gentler = Scale(Slope(32f));
            float steeper = Scale(Slope(34f));

            Assert.Greater(gentler, 0f);
            Assert.Less(gentler, 1f);
            Assert.Less(steeper, gentler);
        }

        /// Downhill is never gated. This is one of the three things that keep the gate from
        /// latching: a machine refused in the direction it faces has to be able to back off, and
        /// backing off is downhill by construction.
        [Test]
        public void SteepGroundDownhill_IsNeverRefused()
        {
            Assert.AreEqual(1f, Scale(Slope(-60f)), 1e-4f);
        }

        /// The case a limit written against the surface normal gets wrong. A boulder well under the
        /// leg's own lift is a step, not a wall, and a legged machine's whole point is that it
        /// steps onto it.
        [Test]
        public void BoulderShorterThanTheLegsLift_IsStillSteppedOnto()
        {
            // Flat ground with a 0.4 m block sitting in the middle of the run.
            var samples = Profile(d => d > 1f && d < 2f ? 0.4f : 0f);

            Assert.AreEqual(1f, Scale(samples), 1e-4f);
        }

        /// A ledge steeper than the limit but lower than the machine can step up: still a step.
        [Test]
        public void LedgeUnderTheLegsLift_IsStillSteppedUp()
        {
            // Rises 0.9 m within one 0.75 m segment -- 50 degrees locally -- then stays there. The
            // sustained grade over the whole run is atan(0.9 / 3) = 17 degrees.
            var samples = Profile(d => d >= 1.5f ? 0.9f : 0f);

            Assert.AreEqual(1f, Scale(samples), 1e-4f);
        }

        /// The case the sustained grade alone misses, and the reason there is a second test at all.
        /// Flat ground for most of the run with a wall at the end of it: the average is dragged
        /// under the limit by the approach, and the machine would climb a cliff.
        [Test]
        public void CliffAtTheEndOfAFlatApproach_IsRefused()
        {
            // 2 m rise inside the last 0.75 m segment: 69 degrees locally, and past the step-up height.
            // Over the whole 3 m run the grade is only atan(2 / 3) = 34 degrees, which the
            // sustained test would wave through.
            var samples = Profile(d => d >= 2.25f ? 2f : 0f);

            // The sustained grade on its own lets a quarter of the travel through, which is a
            // machine creeping up a cliff rather than one refusing it. That is the gap the wall
            // test closes, and this pins the gap open so the test cannot quietly stop testing it.
            Assert.Greater(WalkerClimb.GradeScale(33.7f, MaxAngle, Taper), 0f);
            Assert.AreEqual(0f, Scale(samples), 1e-4f);
        }

        /// Missing samples are unloaded terrain, not a void. Refusing on them would park every
        /// machine at a chunk border.
        [Test]
        public void NoGroundFound_RefusesNothing()
        {
            var samples = new WalkerClimb.Sample[Probes];
            for (int i = 0; i < Probes; i++)
                samples[i] = new WalkerClimb.Sample { Distance = Run * (i + 1) / Probes, Found = false };

            Assert.AreEqual(1f, Scale(samples), 1e-4f);
        }

        /// A hole in the middle of the run must not widen the segment across it: the wall on the
        /// far side would be averaged out into a ramp and walked up.
        [Test]
        public void HoleInTheRun_DoesNotFlattenAWallBehindIt()
        {
            var samples = Profile(d => d >= 2.25f ? 3f : 0f);
            samples[1].Found = false;                       // the sample at 1.5 m went missing
            samples[2].Found = false;                       // and so did the one at 2.25 m

            Assert.AreEqual(0f, Scale(samples), 1e-4f);
        }

        /// Sustained grade is measured over the part of the run that could actually be seen, not
        /// against a distance the probe never reached.
        [Test]
        public void RunThatFallsOffALoadedChunk_IsJudgedOnWhatItSaw()
        {
            var samples = Slope(50f);
            samples[2].Found = false;
            samples[3].Found = false;

            Assert.AreEqual(0f, Scale(samples), 1e-4f);
        }

        [Test]
        public void NoSamplesAtAll_RefusesNothing()
        {
            Assert.AreEqual(1f, WalkerClimb.TravelScale(0f, null, 0, MaxAngle, Taper, StepUp), 1e-4f);
        }

        /// A limit of 89 degrees or more is how a machine opts out entirely, and the scale has to be
        /// exactly 1 there rather than merely close to it.
        [Test]
        public void GradeScale_AtTheExtremes()
        {
            Assert.AreEqual(1f, WalkerClimb.GradeScale(0f, MaxAngle, Taper), 1e-4f);
            Assert.AreEqual(0f, WalkerClimb.GradeScale(MaxAngle, MaxAngle, Taper), 1e-4f);
            Assert.AreEqual(0f, WalkerClimb.GradeScale(89f, MaxAngle, Taper), 1e-4f);
            Assert.AreEqual(1f, WalkerClimb.GradeScale(40f, 89f, Taper), 1e-4f);
        }
    }
}
