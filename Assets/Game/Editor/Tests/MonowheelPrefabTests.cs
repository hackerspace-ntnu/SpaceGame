// Assets/Game/Editor/Tests/MonowheelPrefabTests.cs
//
// Read-back on the five built art prefabs (spec §8): the right number of measured wheels, and
// three capped, world-space, textured systems each. Run after Tools ▸ Vehicles ▸ Build Monowheel
// Presentation — a missing prefab is a failure, because shipping without it is the bug.
using NUnit.Framework;
using SpaceGame.Vehicles.Monowheel;
using UnityEditor;
using UnityEngine;
using B = SpaceGame.EditorTools.MonowheelPresentationBuilder;

namespace SpaceGame.EditorTools
{
    public class MonowheelPrefabTests
    {
        private static MonowheelPresentation Load(string variant)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(B.PrefabPath(variant));
            Assert.IsNotNull(prefab, $"{B.PrefabPath(variant)} is not built");
            var p = prefab.GetComponent<MonowheelPresentation>();
            Assert.IsNotNull(p, $"{variant} has no MonowheelPresentation");
            return p;
        }

        [Test]
        public void EveryVariantHasItsMeasuredWheels()
        {
            foreach (var (variant, _, rings) in B.Variants)
            {
                MonowheelPresentation p = Load(variant);
                Assert.AreEqual(rings, p.Wheels.Count, variant);
                foreach (MonowheelWheel w in p.Wheels)
                {
                    Assert.IsNotNull(w.ringBone, variant);
                    Assert.AreEqual(1f, w.localAxle.magnitude, 1e-3f, $"{variant} axle");
                    Assert.That(w.paddleRadius, Is.InRange(1.8f, 2.1f), $"{variant} radius");
                }
            }
        }

        [Test]
        public void EveryWheelHasThreeCappedWorldSpaceSystemsWithMaterials()
        {
            foreach (var (variant, _, _) in B.Variants)
                foreach (MonowheelWheel w in Load(variant).Wheels)
                {
                    Check(w.spray, B.SprayCap, variant);
                    Check(w.dust, B.DustCap, variant);
                    Check(w.smoke, B.SmokeCap, variant);
                }
        }

        [Test]
        public void DustHangsInLargeCloudsForTenSecondsOrMore()
        {
            foreach (var (variant, _, _) in B.Variants)
                foreach (MonowheelWheel w in Load(variant).Wheels)
                {
                    Assert.GreaterOrEqual(w.dust.main.startLifetime.constantMin, 10f, variant);
                    Assert.GreaterOrEqual(w.dust.main.startSize.constantMin, 2.5f, variant);
                    Assert.GreaterOrEqual(w.dust.main.maxParticles, 112, $"{variant}: the cap would cut a long-lived cloud short");
                }
        }

        [Test]
        public void DoublesSpinOnTwoDifferentCamberedAxles()
        {
            foreach (string variant in new[] { "Double", "DoubleWide" })
            {
                MonowheelPresentation p = Load(variant);
                Transform root = p.transform;
                Vector3 a = root.InverseTransformDirection(p.Wheels[0].ringBone.TransformDirection(p.Wheels[0].localAxle));
                Vector3 b = root.InverseTransformDirection(p.Wheels[1].ringBone.TransformDirection(p.Wheels[1].localAxle));
                Assert.Less(Mathf.Abs(Vector3.Dot(a, b)), 0.9999f, $"{variant}: both rings on one axle — camber lost");
            }
        }

        private static void Check(ParticleSystem ps, int cap, string variant)
        {
            Assert.IsNotNull(ps, variant);
            Assert.AreEqual(cap, ps.main.maxParticles, $"{variant} {ps.name} cap");
            Assert.AreEqual(ParticleSystemSimulationSpace.World, ps.main.simulationSpace, $"{variant} {ps.name}");
            Assert.IsNotNull(ps.GetComponent<ParticleSystemRenderer>().sharedMaterial, $"{variant} {ps.name} material");
        }
    }
}
