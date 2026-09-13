// Tests for the signed scalar the inflator nozzle and the resizer remote both pump.
//
// This is the arithmetic two items share, so it is also the seam where they can silently stop
// agreeing. Everything below is pure static maths and touches no scene at all — which is the point:
// the half of the contract worth pinning is the half that has no Unity in it, and that turns out to
// be the half a second item would break.
//
// WHAT IS NOT HERE, AND WHY. CanResize's Rigidbody cases — a kinematic body refused, a dynamic one
// accepted — need a live StatusReceiver, which subscribes to NetMessaging in OnEnable. That is a
// body in a session, not a fixture, and it belongs in the two-process run the multiplayer skill
// describes. Only the null case is proved here.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    public class InflationScalarTests
    {
        /// <summary>What the inflator nozzle is tuned to: the whole range, in three seconds.</summary>
        private const float NozzleToward = 1f;
        private const float NozzleSeconds = 3f;

        /// <summary>
        /// And the resizer remote: a fraction of the range, in five. The two numbers ARE the trade
        /// that makes the second item a choice rather than a strictly-better copy of the first
        /// (GDC-L1-SYS-0005), so they are written here as the items' own values and asserted
        /// against each other below.
        /// </summary>
        private const float RemoteToward = 0.6f;
        private const float RemoteSeconds = 5f;

        [Test]
        public void PumpedReachesTheLimitInTheSecondsItIsGiven()
        {
            float scalar = Pump(0f, NozzleToward, NozzleSeconds, NozzleSeconds);

            Assert.AreEqual(NozzleToward, scalar, 1e-4f);
        }

        [Test]
        public void PumpedStopsAtTheLimitAndDoesNotRunPastIt()
        {
            // Three times as long as it takes to fill. MoveTowards is what stops it, and the day
            // somebody replaces it with an integration this is the test that notices.
            float scalar = Pump(0f, NozzleToward, NozzleSeconds, NozzleSeconds * 3f);

            Assert.AreEqual(NozzleToward, scalar, 1e-4f);
        }

        [Test]
        public void PumpingTheOtherWayIsTheSameCodeWithASign()
        {
            float up = Pump(0f, NozzleToward, NozzleSeconds, NozzleSeconds * 0.5f);
            float down = Pump(0f, -NozzleToward, NozzleSeconds, NozzleSeconds * 0.5f);

            Assert.AreEqual(up, -down, 1e-4f,
                "Shrinking must be the same arithmetic as growing with the sign flipped. If these " +
                "diverge, the property has been built twice.");
        }

        [Test]
        public void ARemoteCannotDriveABodyPastItsOwnSignalStrength()
        {
            // The whole anti-griefing bound of the ranged item: it reaches further than the nozzle
            // and in exchange it stops well short of the ends of the range, so nothing it touches
            // is ever buoyant or a speck. Held for a minute, it still stops here.
            float scalar = Pump(0f, RemoteToward, RemoteSeconds, 60f);

            Assert.AreEqual(RemoteToward, scalar, 1e-4f);
            Assert.Less(RemoteToward, NozzleToward,
                "The ranged item must not reach as far up the range as the arm's-length one.");
        }

        [Test]
        public void ARemoteCanPullBackABodyTheNozzleInflated()
        {
            // The two items compose because they share the property rather than each owning one.
            // A body the nozzle drove to +1 is a body the remote can drag back down, and it does it
            // by asking for a NEGATIVE limit — not by knowing anything about nozzles.
            float inflated = Pump(0f, NozzleToward, NozzleSeconds, NozzleSeconds);
            float pulled = Pump(inflated, -RemoteToward, RemoteSeconds, RemoteSeconds);

            Assert.Less(pulled, inflated);
        }

        [Test]
        public void ProgressReadsZeroForABodyDrivenTheOtherWay()
        {
            // An honest answer for a needle and for a threshold alike: this handset has made no
            // progress on that body. A negative share here would read as "nearly there" on a gauge
            // and would arm the nozzle's pop on a body somebody else is shrinking.
            Assert.AreEqual(0f, InflationScalar.Progress(-0.5f, RemoteToward), 1e-4f);
        }

        [Test]
        public void ProgressIsAShareOfTheAskersOwnRange()
        {
            // Half of the remote's 0.6 reads as half on the remote's gauge — not as 0.3 of the
            // shared range. Two items with different reaches must not read each other's numbers.
            Assert.AreEqual(0.5f, InflationScalar.Progress(RemoteToward * 0.5f, RemoteToward), 1e-4f);
        }

        [Test]
        public void ProgressIsZeroRatherThanInfiniteForAnItemThatPumpsNowhere()
        {
            Assert.AreEqual(0f, InflationScalar.Progress(0.5f, 0f), 1e-4f);
        }

        [Test]
        public void NothingResolvesAgainstABodyThatIsNotThere()
        {
            // Every caller traces a ray that may have hit open sky, so null is the ordinary case
            // rather than an error, on all three of these.
            Assert.AreEqual(0f, InflationScalar.Presented(null), 1e-4f);
            Assert.IsFalse(InflationScalar.CanResize(null));
        }

        /// <summary>
        /// Run <paramref name="seconds"/> of pumping through the real function in 15 Hz steps —
        /// the hold stream's own rate — rather than as one big call, because that is how the game
        /// calls it and because a rate expressed per second must survive being chopped up.
        /// </summary>
        private static float Pump(float from, float toward, float secondsToFull, float seconds)
        {
            const float Step = 1f / 15f;

            float scalar = from;
            for (float t = 0f; t < seconds; t += Step)
                scalar = InflationScalar.Pumped(scalar, toward, secondsToFull,
                                                Mathf.Min(Step, seconds - t));
            return scalar;
        }
    }
}
