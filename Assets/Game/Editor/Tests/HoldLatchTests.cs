// The grapple's winch latch, against both orderings of the two things that decide it.
//
// A release is a RELAYED message and a bite is a LOCAL timer, so their order differs per machine by
// network jitter plus the interpolation lag on a remote muzzle — a window of tens of milliseconds
// around the bite, which an ordinary quick-release shot lands squarely inside. Before this was a
// latch, the two orderings produced different answers, and the disagreement was permanent: the peer
// that latched the winch wrong drew the rope forever and then discarded the thrower's NEXT throw,
// because Present refuses a second attach while a rope is already out.
//
// Pinned here rather than judged in play mode because the property is pure: given the same two
// facts in either order, the same answer.
//
// In Editor/ rather than beside the asmdef'd EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class HoldLatchTests
    {
        private static GrapplingHookArtifact.WinchLatch Play(params bool[] holdTicks)
        {
            var latch = new GrapplingHookArtifact.WinchLatch();
            foreach (bool active in holdTicks) latch.Observe(active);
            return latch;
        }

        [Test]
        public void ReleaseBeforeBiteStillWinches()
        {
            // The tapped shot: press, release, and only then does the dart arrive. The trigger was
            // never down at the moment of the bite, and the grapple must still reel — otherwise a
            // tap catches and then hangs, which is exactly what "it doesn't reel in" was.
            GrapplingHookArtifact.WinchLatch latch = Play(true, false);

            Assert.IsTrue(latch.WinchAtBite, "a release that beat the dart was read as a refusal to winch");

            latch.Bite();
            Assert.IsTrue(latch.Winching, "the tapped grapple caught and then hung");
        }

        [Test]
        public void ReleaseAfterBiteStopsTheWinch()
        {
            // The deliberate gesture: hold until the rope goes taut, then let go to trade the climb
            // for a swing.
            GrapplingHookArtifact.WinchLatch latch = Play(true);
            latch.Bite();
            latch.Observe(false);

            Assert.IsFalse(latch.Winching, "letting go after the bite did not trade the climb for a swing");
        }

        [Test]
        public void HoldingThroughTheBiteWinches()
        {
            GrapplingHookArtifact.WinchLatch latch = Play(true);
            latch.Bite();

            Assert.IsTrue(latch.Winching, "a trigger still down at the bite did not reel in");
        }

        [Test]
        public void ATapAndADeliberateSwingAreDifferentGestures()
        {
            // The two orderings the latch has to tell apart, and the reason it cannot simply read
            // "is the trigger down" at the moment of the bite.
            //
            // A TAP releases while the dart is still flying: the trigger is up when the rope
            // catches, and the player still expects to be reeled in. A SWING holds until the rope
            // goes taut and lets go afterwards, which is the deliberate gesture for trading the
            // climb for an arc. Identical inputs, opposite meanings, told apart only by order.
            var tap = new GrapplingHookArtifact.WinchLatch();
            tap.Observe(true);
            tap.Observe(false);
            tap.Bite();

            var swing = new GrapplingHookArtifact.WinchLatch();
            swing.Observe(true);
            swing.Bite();
            swing.Observe(false);

            Assert.IsTrue(tap.Winching, "a tapped grapple did not reel in");
            Assert.IsFalse(swing.Winching, "letting go after the catch did not trade the climb for a swing");
        }

        [Test]
        public void AFreshThrowForgetsTheLastOne()
        {
            // Reset is called by Present on every new attach. Without it, a rope released by letting
            // go would start the next throw already not-winching.
            GrapplingHookArtifact.WinchLatch latch = Play(true);
            latch.Bite();
            latch.Observe(false);

            latch.Reset();
            latch.Bite();

            Assert.IsTrue(latch.Winching, "a fresh throw inherited the previous rope's released trigger");
        }
    }
}
