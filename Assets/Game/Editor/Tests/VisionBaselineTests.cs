// Every agent sees at least as far, as wide and for as long as VisionBaseline says. The failure this
// guards: nomads ran on component defaults nobody had chosen -- a 110 degree cone, 35 m, five
// seconds of memory -- and noticed a player only when he stood right in front of them.
//
// A creature that should see less has to say so in VisionBaselineWiring.Exempt, with its reason.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class VisionBaselineTests
    {
        [Test]
        public void EveryAgentPrefabMeetsTheBaseline()
        {
            var shortfalls = new List<string>();
            foreach (string path in VisionBaselineWiring.AgentPrefabPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (VisionBaselineWiring.IsExempt(prefab)) continue;
                VisionBaselineWiring.Raise(prefab, shortfalls, apply: false);
            }

            Assert.IsEmpty(shortfalls, "Run Tools/SpaceGame/Agents/Wire Vision Baseline:\n" +
                                       string.Join("\n", shortfalls));
        }

        [Test]
        public void EveryTargetingProfileMeetsTheBaseline()
        {
            var shortfalls = new List<string>();
            foreach (TargetingProfile profile in VisionBaselineWiring.TargetingProfiles())
                VisionBaselineWiring.Raise(profile, shortfalls, apply: false);

            Assert.IsEmpty(shortfalls, string.Join("\n", shortfalls));
        }

        [Test]
        public void ComponentDefaultsAreTheBaseline()
        {
            var go = new GameObject("defaults");
            TargetingProfile profile = ScriptableObject.CreateInstance<TargetingProfile>();
            try
            {
                var perception = new SerializedObject(go.AddComponent<PerceptionModule>());
                Assert.AreEqual(VisionBaseline.MinFieldOfView, perception.FindProperty("fieldOfViewAngle").floatValue);
                Assert.AreEqual(VisionBaseline.MinMemory, perception.FindProperty("memoryDuration").floatValue);

                var targeting = new SerializedObject(go.AddComponent<AgentTargeting>());
                Assert.AreEqual(VisionBaseline.MinAcquisitionRange, targeting.FindProperty("acquisitionRange").floatValue);
                Assert.AreEqual(VisionBaseline.MinLoseRange, targeting.FindProperty("loseRange").floatValue);

                Assert.AreEqual(VisionBaseline.MinAcquisitionRange, profile.acquisitionRange);
                Assert.AreEqual(VisionBaseline.MinLoseRange, profile.loseRange);
                Assert.AreEqual(VisionBaseline.MinMemory, profile.memoryDuration);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
