using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Locomotion;
using SpaceGame.Vehicles.Crawler;

namespace SpaceGame.EditorTools
{
    /// LeggedLocomotion.Footfalls: the feet that came down in the last Step, which is what footfall
    /// dust answers. The real RigWalker on level ground, stepped by hand as SpiderWalkerGroundingTests does.
    public class WalkerFootfallTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab";
        private const float Dt = 1f / 60f;

        private GameObject ground;
        private GameObject walker;
        private DesertCrawlerLocomotion locomotion;

        [SetUp]
        public void SetUp()
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -1f, 0f);
            ground.transform.localScale = new Vector3(600f, 2f, 600f);
            Physics.SyncTransforms();

            walker = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            walker.transform.position = new Vector3(0f, 30f, 0f);
            Physics.SyncTransforms();
            locomotion = walker.GetComponent<DesertCrawlerLocomotion>();
            locomotion.Initialise();
            Assert.IsTrue(locomotion.IsReady, "no leg chains were found on the rig");
            locomotion.SnapToGround();
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(walker);
            Object.DestroyImmediate(ground);
        }

        private void Walk(float speed, int frames, System.Action perStep)
        {
            for (int i = 0; i < frames; i++)
            {
                locomotion.SetTwist(speed, 0f);
                locomotion.Step(Dt);
                Physics.SyncTransforms();
                perStep();
            }
        }

        [Test]
        public void AMachineStandingStill_PutsNoFootDown_OnceSettled()
        {
            // The first frame may open one leg's slice and settle it with a real step.
            Walk(0f, 60, () => { });
            int footfalls = 0;
            Walk(0f, 240, () => footfalls += locomotion.Footfalls.Count);
            Assert.AreEqual(0, footfalls, "a standing machine reported feet landing");
        }

        [Test]
        public void AWalkingMachine_ReportsEveryLegLanding_OnTheGround_OnceEach()
        {
            var landedLegs = new HashSet<int>();
            var lastStep = new Dictionary<int, int>();
            int step = 0;
            Walk(locomotion.MaxSpeed * 0.5f, 600, () =>
            {
                step++;
                for (int i = 0; i < locomotion.Footfalls.Count; i++)
                {
                    Footfall f = locomotion.Footfalls[i];
                    Assert.That(f.Leg, Is.InRange(0, locomotion.LegCount - 1));
                    Assert.AreEqual(0f, f.Point.y, 0.05f, $"leg {f.Leg} landed off the ground at {f.Point}");
                    Assert.Greater(Vector3.Dot(f.Normal, Vector3.up), 0.99f, "level ground reported a tilted normal");
                    Assert.Greater(f.FootprintRadius, 0f, "a footfall with no footprint");
                    Assert.IsFalse(lastStep.TryGetValue(f.Leg, out int prev) && prev == step - 1,
                                   $"leg {f.Leg} was reported landing on two consecutive steps");
                    lastStep[f.Leg] = step;
                    landedLegs.Add(f.Leg);
                }
            });
            Assert.AreEqual(locomotion.LegCount, landedLegs.Count, "not every leg was seen landing in ten seconds of walking");
        }

        [Test]
        public void EveryStep_IsNumbered_SoAReaderSeesEachFootfallOnce()
        {
            int before = locomotion.StepCount;
            Walk(locomotion.MaxSpeed * 0.5f, 3, () => { });
            Assert.AreEqual(before + 3, locomotion.StepCount);
        }
    }
}
