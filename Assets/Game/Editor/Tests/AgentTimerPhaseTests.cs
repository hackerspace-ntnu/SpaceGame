// A crowd enabled on the same frame must not re-score, re-sample or re-scan on the same frame.
// Every timer used to start at 0, so a city of 40 agents spawned together paid its whole scan
// cost in one frame every interval. The phase is random (the AgentController.speedVariationPhase
// precedent); the tests seed Random so they are deterministic.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.Tests
{
    public class AgentTimerPhaseTests
    {
        private const int Crowd = 20;
        private const int Seed = 1234;

        private readonly List<Object> made = new();
        private Random.State savedRandom;

        [SetUp]
        public void SetUp()
        {
            savedRandom = Random.state;
            Random.InitState(Seed);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            Random.state = savedRandom;
        }

        // OnEnable does not run for AddComponent in edit mode, so it is called the way the engine would.
        private static void Enable(Component component) =>
            component.GetType().GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                     .Invoke(component, null);

        private static float Read(Component component, string field) =>
            (float)component.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                            .GetValue(component);

        private T Make<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            made.Add(go);
            return go.AddComponent<T>();
        }

        private TargetingProfile Profile()
        {
            var profile = ScriptableObject.CreateInstance<TargetingProfile>();
            made.Add(profile);
            return profile;
        }

        private static void AssertSpreadWithin(List<float> phases, float interval, string what)
        {
            var distinct = new HashSet<float>(phases);
            Assert.Greater(distinct.Count, 1, $"every {what} starts in step, so the crowd pays it on one frame");
            foreach (float phase in phases)
                Assert.That(phase, Is.InRange(0f, interval), $"a {what} phase is within one interval");
        }

        [Test]
        public void TargetingReevaluationsAreOutOfStep()
        {
            TargetingProfile profile = Profile();
            var reevaluate = new List<float>();
            var storm = new List<float>();
            for (int i = 0; i < Crowd; i++)
            {
                var targeting = Make<AgentTargeting>();
                targeting.ApplyProfile(profile);
                Enable(targeting);
                reevaluate.Add(Read(targeting, "reevaluateTimer"));
                storm.Add(Read(targeting, "stormSampleTimer"));
            }

            AssertSpreadWithin(reevaluate, profile.reevaluateInterval, "re-evaluation");
            AssertSpreadWithin(storm, 1f, "storm sample");
        }

        [Test]
        public void AProfileSwapReevaluatesOutOfStepToo()
        {
            TargetingProfile profile = Profile();
            var phases = new List<float>();
            for (int i = 0; i < Crowd; i++)
            {
                var targeting = Make<AgentTargeting>();
                targeting.ApplyProfile(profile);
                phases.Add(Read(targeting, "reevaluateTimer"));
            }

            AssertSpreadWithin(phases, profile.reevaluateInterval, "re-evaluation after ApplyProfile");
        }

        [Test]
        public void AForcedTargetIsHeldForAFullInterval()
        {
            TargetingProfile profile = Profile();
            var targeting = Make<AgentTargeting>();
            targeting.ApplyProfile(profile);
            Enable(targeting);

            var enemy = new GameObject("enemy");
            made.Add(enemy);
            targeting.ForceTarget(enemy.transform);

            Assert.AreEqual(profile.reevaluateInterval, Read(targeting, "reevaluateTimer"),
                "an ally's alert is not overruled by the next phased re-score");
        }

        [Test]
        public void MenaceScansAreOutOfStep()
        {
            var phases = new List<float>();
            float interval = 0f;
            for (int i = 0; i < Crowd; i++)
            {
                var sensor = Make<MenaceSensor>();
                Enable(sensor);
                phases.Add(Read(sensor, "scanTimer"));
                interval = Read(sensor, "scanInterval");
            }

            AssertSpreadWithin(phases, interval, "menace scan");
        }
    }
}
