using NUnit.Framework;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// A fire lasts five seconds and then it is out.
    ///
    /// <para>
    /// Both sources of fire announce <see cref="StatusKind.Burning"/> continuously — the cone
    /// fifteen times a second while the trigger is held, every patch of ground fire for as long as
    /// a body stands in it — so the duration is only real if the announcements after the first are
    /// refused. That refusal is invisible from the outside: the fire looks identical either way and
    /// only outlasts its five seconds when something is holding it there.
    /// </para>
    /// </summary>
    public class BurningStatusTests
    {
        /// <summary>
        /// The defect this exists for: a creature standing in a burning patch never stopped
        /// burning, because every patch tick pushed the expiry back out to five seconds.
        /// </summary>
        [Test]
        public void ARunningFireIsNotExtended()
        {
            var burning = new BurningStatus();

            Assert.IsTrue(burning.CanApply(null, running: false),
                          "A body that is not alight refused to catch fire at all.");

            Assert.IsFalse(burning.CanApply(null, running: true),
                           "A fire already burning accepted a refresh. Held flame now extends the " +
                           "burn indefinitely instead of the five seconds the status is worth.");
        }

        /// <summary>
        /// And the half of it that is easy to leave out: with no cooldown the body relights on the
        /// very frame its own fire expired, which is the same endless burn one frame at a time.
        /// </summary>
        [Test]
        public void AFireDoesNotRelightTheMomentItGoesOut()
        {
            var burning = new BurningStatus();

            burning.OnCleared(null);

            Assert.IsFalse(burning.CanApply(null, running: false),
                           "A body caught fire again immediately after burning out. The reignite " +
                           "delay is not being served.");
        }
    }
}
