using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class NavPathFollowerTests
    {
        private int builds;

        // NUnit reuses one fixture instance for every test in it, so the count must be zeroed here
        // or it carries the builds of whichever tests ran first.
        [SetUp]
        public void ResetBuildCount() => builds = 0;

        private int Corners(Vector3 from, Vector3 to, Vector3[] into)
        {
            builds++;
            into[0] = from; into[1] = new Vector3(0f, 0f, 50f); into[2] = to;
            return 3;
        }

        private NavPathFollower Make() => new NavPathFollower(0.5f, 2f, 6f, Corners);

        [Test]
        public void SteersAtTheFirstRealCorner_NotAtTheTarget()
        {
            Vector3 steer = Make().SteerTarget(Vector3.zero, new Vector3(50f, 0f, 50f), 0.1f);
            Assert.AreEqual(new Vector3(0f, 0f, 50f), steer);
        }

        [Test]
        public void Repaths_OnlyWhenTheTargetMovesOrTheIntervalPasses()
        {
            var f = Make();
            var target = new Vector3(50f, 0f, 50f);
            f.SteerTarget(Vector3.zero, target, 0.1f);
            f.SteerTarget(Vector3.zero, target + Vector3.right, 0.1f);   // moved 1 m < tolerance 2
            Assert.AreEqual(1, builds);
            f.SteerTarget(Vector3.zero, target + Vector3.right * 3f, 0.1f);
            Assert.AreEqual(2, builds, "moved past the tolerance");
            f.SteerTarget(Vector3.zero, target + Vector3.right * 3f, 0.6f);
            Assert.AreEqual(3, builds, "the interval elapsed");
        }

        [Test]
        public void NoPath_SteersStraightAtTheTarget()
        {
            var f = new NavPathFollower(0.5f, 2f, 6f, (a, b, into) => 0);
            var target = new Vector3(10f, 0f, 10f);
            Assert.AreEqual(target, f.SteerTarget(Vector3.zero, target, 0.1f));
            Assert.IsFalse(f.HasPath);
        }

        [Test]
        public void CornerAfter_IsTheOneBeyondTheCurrentCorner()
        {
            var f = Make();
            f.SteerTarget(Vector3.zero, new Vector3(50f, 0f, 50f), 0.1f);
            Assert.IsTrue(f.TryGetCornerAfter(Vector3.zero, out Vector3 after));
            Assert.AreEqual(new Vector3(50f, 0f, 50f), after);
        }
    }
}
