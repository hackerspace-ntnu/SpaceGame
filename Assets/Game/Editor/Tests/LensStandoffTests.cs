// How far in front of the chest the body screen's lens may sit when a wall is in the way.
using NUnit.Framework;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class LensStandoffTests
    {
        [Test]
        public void NoBlockerIsTheFullDistance()
        {
            Assert.AreEqual(2.3f, LensStandoff.Resolve(2.3f, float.PositiveInfinity, 0.25f, 0.9f), 1e-5f);
        }

        [Test]
        public void ABlockerPullsTheLensInByItsRadius()
        {
            Assert.AreEqual(1.25f, LensStandoff.Resolve(2.3f, 1.5f, 0.25f, 0.9f), 1e-5f);
        }

        [Test]
        public void ABlockerBeyondTheShotChangesNothing()
        {
            Assert.AreEqual(2.3f, LensStandoff.Resolve(2.3f, 4f, 0.25f, 0.9f), 1e-5f);
        }

        [Test]
        public void NeverNearerThanTheFloor()
        {
            Assert.AreEqual(0.9f, LensStandoff.Resolve(2.3f, 0.6f, 0.25f, 0.9f), 1e-5f);
            Assert.AreEqual(0.9f, LensStandoff.Resolve(2.3f, 0f, 0.25f, 0.9f), 1e-5f);
        }
    }
}
