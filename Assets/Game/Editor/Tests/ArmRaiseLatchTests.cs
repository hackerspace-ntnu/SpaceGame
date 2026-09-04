// When the gauntlet arm is up: a tap lingers, a hold stays, a release lingers, a strip drops.
using NUnit.Framework;
using SpaceGame.Characters;

namespace SpaceGame.EditorTools
{
    public class ArmRaiseLatchTests
    {
        private const float Linger = 0.6f;

        [Test]
        public void ATapRaisesTheArmForTheLinger()
        {
            var latch = new ArmRaiseLatch(Linger);
            latch.Press(now: 10f, continuous: false);

            Assert.IsTrue(latch.Raised(10f));
            Assert.IsTrue(latch.Raised(10.5f), "still up inside the linger");
            Assert.IsFalse(latch.Raised(10.7f), "down once the linger has run out");
        }

        [Test]
        public void AHeldItemStaysUpPastTheLinger()
        {
            var latch = new ArmRaiseLatch(Linger);
            latch.Press(now: 0f, continuous: true);
            latch.Hold(active: true, now: 1f);

            Assert.IsTrue(latch.Raised(5f), "a beam still burning keeps the arm up");
        }

        [Test]
        public void ReleasingAHoldLingersThenLowers()
        {
            var latch = new ArmRaiseLatch(Linger);
            latch.Press(now: 0f, continuous: true);
            latch.Hold(active: false, now: 3f);

            Assert.IsTrue(latch.Raised(3.3f), "the release lingers like a tap does");
            Assert.IsFalse(latch.Raised(3.7f));
        }

        [Test]
        public void ARemoteReleaseWithNoPressIsHarmless()
        {
            // A peer that joined mid-hold sees only the final tick.
            var latch = new ArmRaiseLatch(Linger);
            latch.Hold(active: false, now: 2f);

            Assert.IsFalse(latch.Raised(2f));
        }

        [Test]
        public void ClearDropsTheArmAtOnce()
        {
            var latch = new ArmRaiseLatch(Linger);
            latch.Press(now: 0f, continuous: true);
            latch.Clear();

            Assert.IsFalse(latch.Raised(0.1f), "a stripped gauntlet leaves nothing to hold the arm up");
        }
    }
}
