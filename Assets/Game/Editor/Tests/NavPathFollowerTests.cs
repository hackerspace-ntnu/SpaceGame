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

        // interval 0.5 s, tolerance 2 m, corners rounded at 6 m, still-target interval 5 s.
        private NavPathFollower MakeWithStillInterval() => new NavPathFollower(0.5f, 2f, 6f, Corners, 5f);

        private static readonly Vector3 StillTarget = new Vector3(50f, 0f, 50f);

        private static void Run(NavPathFollower f, Vector3 position, Vector3 target, float seconds)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += 0.1f) f.SteerTarget(position, target, 0.1f);
        }

        [Test]
        public void AStillTargetIsRepathedOnTheLongInterval()
        {
            Run(MakeWithStillInterval(), Vector3.zero, StillTarget, 10f);
            Assert.LessOrEqual(builds, 3, "20 at the 0.5 s interval; the route to a still target has not changed");
        }

        [Test]
        public void TheFourArgumentFollowerStillRepathsOnTheShortInterval()
        {
            Run(Make(), Vector3.zero, StillTarget, 10f);
            Assert.GreaterOrEqual(builds, 15, "MonowheelMotor and TrackedHullMotor keep today's behaviour");
        }

        [Test]
        public void ATargetThatMovesRepathsOnTheSameFrame_EvenOnTheLongInterval()
        {
            var f = MakeWithStillInterval();
            Run(f, Vector3.zero, StillTarget, 1f);   // past the first interval: the long one is armed
            int before = builds;
            f.SteerTarget(Vector3.zero, StillTarget + Vector3.right * 3f, 0.1f);
            Assert.AreEqual(before + 1, builds);
        }

        [Test]
        public void RequestRepath_RebuildsOnTheNextSteer()
        {
            var f = MakeWithStillInterval();
            Run(f, Vector3.zero, StillTarget, 1f);
            int before = builds;
            f.RequestRepath();
            f.SteerTarget(Vector3.zero, StillTarget, 0.1f);
            Assert.AreEqual(before + 1, builds);
        }

        [Test]
        public void ABodyPushedOffItsRouteRecoversOnTheShortInterval()
        {
            var f = MakeWithStillInterval();
            Run(f, Vector3.zero, StillTarget, 1f);
            int before = builds;

            // 20 m sideways off the first leg (0,0,0)→(0,0,50): far outside the 6 m corner radius.
            Run(f, new Vector3(20f, 0f, 10f), StillTarget, 1f);
            Assert.Greater(builds, before, "a knocked-off body must not wait out the 5 s still-target interval");
        }

        [Test]
        public void ABodyStillOnItsRouteKeepsTheLongInterval()
        {
            var f = MakeWithStillInterval();
            Run(f, Vector3.zero, StillTarget, 1f);
            int before = builds;
            Run(f, new Vector3(3f, 0f, 10f), StillTarget, 3f);   // 3 m off the leg, inside the 6 m radius
            Assert.AreEqual(before, builds);
        }

        [Test]
        public void NoRouteKeepsTheShortInterval_SoANewlyBakedChunkIsPickedUp()
        {
            int tries = 0;
            var f = new NavPathFollower(0.5f, 2f, 6f, (a, b, into) => { tries++; return 0; }, 5f);
            Run(f, Vector3.zero, StillTarget, 10f);
            Assert.GreaterOrEqual(tries, 15);
        }

        [Test]
        public void Settings_TheFourArgumentConstructorRepathsAStillTargetAtTheNormalRate()
        {
            var settings = new NavPathFollowerSettings(0.5f, 2f, 6f, 20f);
            Assert.AreEqual(0.5f, settings.stillTargetRepathInterval);
        }

        [Test]
        public void Settings_ValidateNeverLetsTheStillIntervalUndercutTheNormalOne()
        {
            var settings = new NavPathFollowerSettings(1f, 2f, 6f, 20f, stillTargetRepathInterval: 0f);
            settings.Validate();
            Assert.AreEqual(1f, settings.stillTargetRepathInterval);
        }

        [Test]
        public void TheRigWalkerWaitsLongerForAStillTargetThanForAMovingOne()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab");
            Assert.IsNotNull(prefab, "RigWalker.prefab moved");
            var driver = prefab.GetComponentInChildren<LeggedDriver>(true);
            Assert.IsNotNull(driver);

            var so = new UnityEditor.SerializedObject(driver);
            float interval = so.FindProperty("route.repathInterval").floatValue;
            float still = so.FindProperty("route.stillTargetRepathInterval").floatValue;
            Assert.Greater(still, interval);
        }

        [Test]
        public void FlatDistanceFromLeg_MeasuresAcrossTheLegIgnoringHeight()
        {
            var path = new SpaceGame.Locomotion.WalkerPath();
            path.Set(new[] { Vector3.zero, new Vector3(0f, 0f, 50f) }, 2);
            Assert.AreEqual(20f, path.FlatDistanceFromLeg(new Vector3(20f, 8f, 10f)), 1e-4f);
            Assert.AreEqual(0f, path.FlatDistanceFromLeg(new Vector3(0f, 30f, 25f)), 1e-4f);
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
