using NUnit.Framework;
using SpaceGame.Core.Safety;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The scope guard's patience, exercised by marching its interval forward by hand — this project
    /// has no play-mode tests, and the guard reads its elapsed time from the caller for exactly that
    /// reason.
    ///
    /// The release path itself is not covered here: GameplayMenuScope.Enter refuses without a local
    /// player, and building one takes a three-scene rig (MultiplayerTestPlayerBuilder). What is
    /// covered is the part that decides, which is the part that can be wrong.
    /// </summary>
    public class StuckScopeGuardTests
    {
        [Test]
        public void AnIdleGuardDoesNothingWhenNoScopeIsHeld()
        {
            var guard = new StuckScopeGuard();

            Assert.DoesNotThrow(() => guard.Check(1f));
            Assert.DoesNotThrow(() => guard.Check(60f));
        }

        [Test]
        public void TheGraceIsLongerThanASingleSweep()
        {
            Assert.Greater(StuckScopeGuard.GraceSeconds, SessionGuardRunner.CheckIntervalSeconds,
                           "a grace shorter than one sweep would release on first sighting, which " +
                           "closes menus out from under people mid-animation");
        }

        [Test]
        public void EveryGuardTimeoutOutlastsASweep()
        {
            Assert.Greater(InputRestoreGuard.TimeoutSeconds, SessionGuardRunner.CheckIntervalSeconds);
            Assert.Greater(MountGuard.TimeoutSeconds, SessionGuardRunner.CheckIntervalSeconds);
            Assert.Greater(ViewGuard.TimeoutSeconds, SessionGuardRunner.CheckIntervalSeconds);
        }
    }
}
