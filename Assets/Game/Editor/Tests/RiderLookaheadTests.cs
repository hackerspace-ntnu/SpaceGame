// The rider's look-ahead on the NavMesh motor: a fast mount aims further ahead than a slow one,
// and never closer than the authored minimum. The failure this guards is the robot horse that
// stopped dead every chunk-load hitch at a gallop (Vehicles.md gotcha).
using NUnit.Framework;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class RiderLookaheadTests
    {
        [Test]
        public void AGallopLooksHalfASecondAhead()
        {
            Assert.AreEqual(6.95f, NavMeshAgentMotor.RiderLookahead(13.9f, 2f, 0.5f), 1e-3f);
        }

        [Test]
        public void AWalkNeverAimsCloserThanTheMinimum()
        {
            Assert.AreEqual(2f, NavMeshAgentMotor.RiderLookahead(2.85f, 2f, 0.5f), 1e-3f);
            Assert.AreEqual(2f, NavMeshAgentMotor.RiderLookahead(0f, 2f, 0.5f), 1e-3f, "standing still");
            Assert.AreEqual(2f, NavMeshAgentMotor.RiderLookahead(-5f, 2f, 0.5f), 1e-3f, "a negative speed is not a look-behind");
        }
    }
}
