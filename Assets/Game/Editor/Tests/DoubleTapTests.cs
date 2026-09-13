// The double-Space gesture that deploys whatever is worn on the back.
//
// Pinned as a pure class because the two things that matter about it — the window is fixed, and
// a hit consumes both presses — are exactly the properties that quietly break when the detection
// is folded into an input callback that reads Time.time.
//
// In Editor/ because DoubleTap lives in Assembly-CSharp, which an asmdef'd test cannot reference.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class DoubleTapTests
    {
        private const float Window = 0.3f;

        [Test]
        public void TwoPressesInsideTheWindowFire()
        {
            var tap = new DoubleTap(Window);

            Assert.IsFalse(tap.Press(0f), "the first press is not yet a gesture");
            Assert.IsTrue(tap.Press(0.2f), "a second press inside the window should fire");
        }

        [Test]
        public void TwoPressesOutsideTheWindowDoNotFire()
        {
            var tap = new DoubleTap(Window);

            tap.Press(0f);
            Assert.IsFalse(tap.Press(0.31f), "a second press just past the window must not fire");
        }

        [Test]
        public void TheWindowEdgeCounts()
        {
            var tap = new DoubleTap(Window);

            tap.Press(1f);
            Assert.IsTrue(tap.Press(1.3f), "a press exactly on the window edge should still fire");
        }

        [Test]
        public void AHitConsumesBothPresses()
        {
            var tap = new DoubleTap(Window);

            tap.Press(0f);
            Assert.IsTrue(tap.Press(0.1f));
            Assert.IsFalse(tap.Press(0.2f), "the third press of a triple must start a new count, not fire again");
            Assert.IsTrue(tap.Press(0.3f), "…and the fourth pairs with the third");
        }

        [Test]
        public void ALatePressStartsANewCount()
        {
            var tap = new DoubleTap(Window);

            tap.Press(0f);
            Assert.IsFalse(tap.Press(1f), "too late to pair with the first");
            Assert.IsTrue(tap.Press(1.25f), "but it starts a count of its own");
        }
    }
}
