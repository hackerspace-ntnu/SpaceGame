using NUnit.Framework;
using SpaceGame.Characters;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Pins <see cref="PlayerMovement.ShouldEndCarry"/> — when a fling the player did not produce
    /// stops being protected from the air-control lerp.
    ///
    /// <para>
    /// The case that matters is grounded-and-rising. PlayerMovement's ground probe answers
    /// "grounded" for roughly the first 0.6 m of clearance, while a launch climbs about 0.2 m per
    /// physics step, so a body flung off flat ground is still "grounded" for several ticks after
    /// it left. Ending the carry there handed the horizontal half to a lerp whose no-input target
    /// is zero: the victim popped straight up and landed on the spot. A rising body has not landed,
    /// whatever the probe says.
    /// </para>
    /// </summary>
    public class CarryMomentumTests
    {
        private const float MoveSpeed = 6f;

        [Test]
        public void GroundedButStillRising_KeepsTheCarry()
        {
            Assert.IsFalse(PlayerMovement.ShouldEndCarry(grounded: true, rising: true,
                                                         carriedSpeed: 20f, moveSpeed: MoveSpeed),
                "A flung body is still inside the ground probe's ~0.6 m of generosity for several " +
                "ticks after launch. Clearing the carry there deletes the whole horizontal fling.");
        }

        [Test]
        public void GroundedAndNoLongerRising_EndsTheCarry()
        {
            Assert.IsTrue(PlayerMovement.ShouldEndCarry(grounded: true, rising: false,
                                                        carriedSpeed: 20f, moveSpeed: MoveSpeed),
                "A genuine landing must still give the speed back — the rise test is a delay, " +
                "not an escape hatch.");
        }

        [Test]
        public void AirborneAtWalkingPace_EndsTheCarry()
        {
            Assert.IsTrue(PlayerMovement.ShouldEndCarry(grounded: false, rising: false,
                                                        carriedSpeed: 3f, moveSpeed: MoveSpeed),
                "Below a walk there is no fling left to protect.");
        }

        [Test]
        public void AirborneAndFast_KeepsTheCarry()
        {
            Assert.IsFalse(PlayerMovement.ShouldEndCarry(grounded: false, rising: false,
                                                         carriedSpeed: 20f, moveSpeed: MoveSpeed));
        }

        [Test]
        public void RisingButAlreadySlow_EndsTheCarry()
        {
            // Rising does not outrank the walking-pace clause: a body that is climbing but has no
            // horizontal speed worth keeping would otherwise hold the latch for the whole ascent.
            Assert.IsTrue(PlayerMovement.ShouldEndCarry(grounded: true, rising: true,
                                                        carriedSpeed: 2f, moveSpeed: MoveSpeed));
            Assert.IsTrue(PlayerMovement.ShouldEndCarry(grounded: false, rising: true,
                                                        carriedSpeed: 2f, moveSpeed: MoveSpeed));
        }

        [Test]
        public void ExactlyWalkingPace_EndsTheCarry()
        {
            // The boundary the original `carried <= CurrentMoveSpeed` drew, kept unchanged.
            Assert.IsTrue(PlayerMovement.ShouldEndCarry(grounded: false, rising: false,
                                                        carriedSpeed: MoveSpeed, moveSpeed: MoveSpeed));
        }
    }
}
