// The far dust: a huge, sparse cloud that fades in exactly as a vehicle's near dust fades out, billows
// while the vehicle moves and settles when it parks. Driven by hand; nothing here is networked or saved.
using System;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Monowheel;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools
{
    public class FarDustTests
    {
        private const float Near = 80f, Far = 200f, Cull = 1500f;
        private const float Dt = 1f / 60f;

        private sealed class Band : IDustLodBand
        {
            public float LodNear => Near;
            public float LodFar => Far;
        }

        private GameObject subject;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(subject);

        [Test]
        public void TheNearAndFarDust_AlwaysSumToOne_InsideTheCull()
        {
            for (float d = 0f; d < Cull; d += 5f)
            {
                float near = MonowheelPresentationMath.LodFactor(d, Near, Far);
                float far = FarDust.Fade(d, Near, Far, Cull);
                Assert.AreEqual(1f, near + far, 1e-5f, $"at {d} m");
                Assert.IsFalse(near > 0.99f && far > 0.99f, $"both full at {d} m");
            }
        }

        [Test]
        public void TheFarDust_IsOffNear_HalfwayInTheBand_FullBeyond_AndGonePastTheCull()
        {
            Assert.AreEqual(0f, FarDust.Fade(40f, Near, Far, Cull));
            Assert.AreEqual(0.5f, FarDust.Fade(140f, Near, Far, Cull), 1e-5f);
            Assert.AreEqual(1f, FarDust.Fade(900f, Near, Far, Cull));
            Assert.AreEqual(0f, FarDust.Fade(Cull, Near, Far, Cull));
            Assert.AreEqual(0f, FarDust.Fade(float.NaN, Near, Far, Cull), "no camera: the near dust is full, so the far is off");
        }

        private FarDust Rig()
        {
            subject = new GameObject("Vehicle");
            ParticleSystem cloud = DustCloudRecipe.Cloud(subject.transform, "FX_TestFarDust", Vector3.zero, Quaternion.LookRotation(Vector3.up),
                                                         new Material(Shader.Find(DustCloudRecipe.Shader)), Color.white,
                                                         cap: 500, shapeOffset: Vector3.zero);
            var dust = cloud.gameObject.AddComponent<FarDust>();
            dust.Configure(cloud, cruiseSpeed: 2.7f, peakRate: 4f, nearBand: new Band(), cull: Cull);
            dust.ResetBaseline();
            return dust;
        }

        private static float Drive(FarDust dust, float speed, float distance, int frames)
        {
            float rate = 0f;
            for (int i = 0; i < frames; i++)
            {
                dust.transform.position += Vector3.forward * (speed * Dt);
                rate = dust.Present(Dt, distance);
            }
            return rate;
        }

        [Test]
        public void AMarchingVehicle_BillowsAtRange_AndAParkedOneSettles()
        {
            FarDust dust = Rig();
            Assert.Greater(Drive(dust, 2.7f, 300f, 120), 3.6f, "marching at the city's pace, 300 m away");
            Assert.Less(Drive(dust, 0f, 300f, 120), 0.1f, "parked");
            Assert.AreEqual(0f, Drive(dust, 2.7f, 50f, 60), "close up the near dust has it");
        }

        [Test]
        public void AddFarDust_BuildsAHugeSparseCloud_InTheNearDustsOwnBand()
        {
            // An unscaled root, as every builder's is, with a 10 x 4 x 6 m hull under it.
            subject = new GameObject("Machine");
            GameObject hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.transform.SetParent(subject.transform, false);
            hull.transform.localScale = new Vector3(10f, 4f, 6f);
            var contact = new GameObject("Contact");
            contact.transform.SetParent(subject.transform, false);
            RollingDust rolling = VehicleDustWiring.AddRollingDust(subject, new[] { contact.transform }, 4f, 3f);

            FarDust far = VehicleDustWiring.AddFarDust(subject, nearPeakRate: 6f, cruiseSpeed: 2.7f);

            Assert.AreEqual(VehicleDustWiring.FarCloudName, far.name);
            Assert.AreSame(subject.transform, far.transform.parent);
            Assert.AreSame(far.GetComponent<ParticleSystem>(), far.Cloud);
            Assert.AreEqual(rolling.LodNear, far.FadeNear);
            Assert.AreEqual(rolling.LodFar, far.FadeFar);
            Assert.AreEqual(6f * VehicleDustWiring.FarDustRateFraction, far.RateAtFullSpeed, 1e-5f);
            Assert.AreEqual(2.7f, far.FullSpeed, 1e-5f);
            Assert.AreEqual(VehicleDustWiring.FarDustCullDistance, far.CullDistance);

            ParticleSystem.MainModule main = far.Cloud.main;
            Assert.AreEqual(DustCloudRecipe.MinSize * VehicleDustWiring.FarDustSizeMultiplier, main.startSize.constantMin, 1e-4f);
            Assert.AreEqual(DustCloudRecipe.MaxSize * VehicleDustWiring.FarDustSizeMultiplier, main.startSize.constantMax, 1e-4f);
            Assert.AreEqual(DustCloudRecipe.MinLife * VehicleDustWiring.FarDustLifeMultiplier, main.startLifetime.constantMin, 1e-4f);
            Assert.AreEqual(DustCloudRecipe.MaxLife * VehicleDustWiring.FarDustLifeMultiplier, main.startLifetime.constantMax, 1e-4f);
            Assert.AreEqual(Mathf.CeilToInt(far.RateAtFullSpeed * DustCloudRecipe.MaxLife * VehicleDustWiring.FarDustLifeMultiplier),
                            main.maxParticles);
            Assert.AreEqual(ParticleSystemSimulationSpace.World, main.simulationSpace);
            ParticleSystem.ShapeModule shape = far.Cloud.shape;
            Assert.AreEqual(ParticleSystemShapeType.Box, shape.shapeType);
            Vector3 birthMin = far.transform.TransformPoint(shape.position - shape.scale * 0.5f);
            Vector3 birthMax = far.transform.TransformPoint(shape.position + shape.scale * 0.5f);
            Bounds birth = new Bounds(birthMin, Vector3.zero);
            birth.Encapsulate(birthMax);
            float spread = VehicleDustWiring.FarDustFootprintSpread;
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(10f * spread, 4f * VehicleDustWiring.FarDustBirthHeightFraction, 6f * spread), birth.size), 1e-3f,
                            "born round the hull's footprint and up its lower part, not on the sand alone");
            Assert.AreEqual(0f, birth.center.x, 1e-3f, "centred across the hull");
            Assert.AreEqual(6f * VehicleDustWiring.FarDustLead, birth.center.z, 1e-3f,
                            "leading it: a trail alone lies behind the legs of a vehicle seen head-on");
            Assert.AreEqual(VehicleDustWiring.FarDustOpacity, main.startColor.color.a, 1e-5f);
            Assert.AreEqual(-2f, birth.min.y, 1e-3f, "from the hull's lowest point");
            Assert.AreEqual(VehicleDustWiring.SandMaterialPath,
                            UnityEditor.AssetDatabase.GetAssetPath(far.Cloud.GetComponent<ParticleSystemRenderer>().sharedMaterial));
        }

        [Test]
        public void AFarPuff_ClimbsAtTheRiseSpeed_OnceItsThrowIsSpent()
        {
            subject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var contact = new GameObject("Contact");
            contact.transform.SetParent(subject.transform, false);
            VehicleDustWiring.AddRollingDust(subject, new[] { contact.transform }, 4f, 3f);
            FarDust far = VehicleDustWiring.AddFarDust(subject, nearPeakRate: 6f, cruiseSpeed: 2.7f);

            far.Cloud.Emit(1);
            far.Cloud.Simulate(6f, true, false, false);
            var puffs = new ParticleSystem.Particle[1];
            Assert.AreEqual(1, far.Cloud.GetParticles(puffs));
            Assert.AreEqual(VehicleDustWiring.FarDustRiseSpeed, puffs[0].totalVelocity.y, 0.15f * VehicleDustWiring.FarDustRiseSpeed,
                            "a far cloud stands up into a column; one that hangs where it was thrown lies on the sand as a band");
        }

        [Test]
        public void AddFarDust_RefusesAMachineWithNoNearDust()
        {
            subject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Assert.Throws<InvalidOperationException>(() => VehicleDustWiring.AddFarDust(subject, 6f, 2.7f));
        }
    }
}
