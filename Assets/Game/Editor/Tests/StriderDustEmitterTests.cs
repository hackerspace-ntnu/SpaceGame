using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Crawler;

namespace SpaceGame.EditorTools
{
    /// The two dust emitters the Strider city's machines carry, driven by hand: FootfallDust on the real
    /// RigWalker's legs, RollingDust on a bare contact moved over a slab. Nothing here is networked or
    /// saved; both read only what the machine they sit on already does.
    public class StriderDustEmitterTests
    {
        private const string RigWalkerPath = "Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab";
        private const float Dt = 1f / 60f;

        private GameObject ground;
        private GameObject subject;

        [SetUp]
        public void SetUp()
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -1f, 0f);
            ground.transform.localScale = new Vector3(600f, 2f, 600f);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(subject);
            Object.DestroyImmediate(ground);
        }

        /// The recipe's cloud in a throwaway material: the emitters' logic is under test, not the asset.
        private static ParticleSystem Cloud(Transform parent) =>
            DustCloudRecipe.Cloud(parent, "FX_TestDust", Vector3.zero, Quaternion.LookRotation(Vector3.up),
                                  new Material(Shader.Find(DustCloudRecipe.Shader)), Color.white,
                                  cap: 5000, shapeOffset: Vector3.zero);

        // ── footfall dust ──────────────────────────────────────────────────────

        private (DesertCrawlerLocomotion legs, FootfallDust dust) Walker(int puffs)
        {
            subject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RigWalkerPath));
            subject.transform.position = new Vector3(0f, 30f, 0f);
            Physics.SyncTransforms();
            var legs = subject.GetComponent<DesertCrawlerLocomotion>();
            legs.Initialise();
            legs.SnapToGround();
            Physics.SyncTransforms();
            var dust = subject.AddComponent<FootfallDust>();
            dust.Configure(legs, Cloud(subject.transform), puffs);
            return (legs, dust);
        }

        /// Walks at half speed, presenting at <paramref name="distance"/> after every step; returns (footfalls, puffs thrown).
        private static (int footfalls, int puffs) Walk(DesertCrawlerLocomotion legs, FootfallDust dust, float distance, int frames)
        {
            int footfalls = 0, puffs = 0;
            for (int i = 0; i < frames; i++)
            {
                legs.SetTwist(legs.MaxSpeed * 0.5f, 0f);
                legs.Step(Dt);
                Physics.SyncTransforms();
                footfalls += legs.Footfalls.Count;
                puffs += dust.Present(distance);
            }
            return (footfalls, puffs);
        }

        [Test]
        public void EveryFootThatLands_ThrowsItsPuffs_IntoTheCloud()
        {
            (DesertCrawlerLocomotion legs, FootfallDust dust) = Walker(puffs: 4);
            (int footfalls, int puffs) = Walk(legs, dust, float.NaN, 240);

            Assert.Greater(footfalls, 0, "the walker never put a foot down");
            Assert.AreEqual(footfalls * 4, puffs);
            Assert.AreEqual(puffs, dust.Cloud.particleCount, "the puffs thrown are not in the cloud");
        }

        [Test]
        public void APuff_IsBornAtTheRimOfTheFoot_SizedToIt_AndThrownOutward()
        {
            (DesertCrawlerLocomotion legs, FootfallDust dust) = Walker(puffs: 4);
            for (int i = 0; i < 600 && legs.Footfalls.Count == 0; i++)
            {
                legs.SetTwist(legs.MaxSpeed * 0.5f, 0f);
                legs.Step(Dt);
                Physics.SyncTransforms();
            }
            Assert.Greater(legs.Footfalls.Count, 0, "no foot landed in ten seconds");
            SpaceGame.Locomotion.Footfall f = legs.Footfalls[0];
            dust.Present(float.NaN);

            var particles = new ParticleSystem.Particle[dust.Cloud.particleCount];
            int n = dust.Cloud.GetParticles(particles);
            Assert.GreaterOrEqual(n, 4);
            for (int i = 0; i < 4; i++)
            {
                Vector3 offset = particles[i].position - f.Point;
                Vector3 flat = Vector3.ProjectOnPlane(offset, Vector3.up);
                Assert.AreEqual(f.FootprintRadius, flat.magnitude, f.FootprintRadius * 0.05f + 0.01f, "a puff was not born at the sole's rim");
                Assert.Greater(Vector3.Dot(particles[i].velocity, flat.normalized), 0f, "a puff was thrown inward");
                Assert.Greater(particles[i].velocity.y, 0f, "a puff was thrown into the ground");
                Assert.AreEqual(dust.SizePerFootRadius * f.FootprintRadius * 2f, particles[i].startSize,
                                dust.SizePerFootRadius * f.FootprintRadius * 0.5f, "a puff is not sized to the foot");
            }
        }

        [Test]
        public void FarFromTheCamera_NoFootfallThrowsDust()
        {
            (DesertCrawlerLocomotion legs, FootfallDust dust) = Walker(puffs: 4);
            (int footfalls, int puffs) = Walk(legs, dust, 10000f, 240);
            Assert.Greater(footfalls, 0);
            Assert.AreEqual(0, puffs);
        }

        [Test]
        public void WhileRiding_TheDustThinsForTheCameraDrawingTheFrame_NotForCameraMain()
        {
            // Riding draws through an Untagged orbit camera with the player's own switched off, so
            // Camera.main is null; measured from it, the dust read "no camera" and never thinned.
            (DesertCrawlerLocomotion legs, FootfallDust dust) = Walker(puffs: 4);
            var orbit = new GameObject("OrbitCamera") { tag = "Untagged" };
            orbit.transform.SetParent(subject.transform, false);
            orbit.transform.position = subject.transform.position + new Vector3(0f, 0f, -10000f);
            // Drawn to the screen, as the orbit camera is: URP's UI-overlay blit then logs a size
            // mismatch in edit mode (see ViewCameraTests), which is the harness, not the dust.
            LogAssert.ignoreFailingMessages = true;
            orbit.AddComponent<Camera>().Render();
            Camera main = Camera.main;
            Assume.That(main == null || Vector3.Distance(main.transform.position, subject.transform.position) < dust.LodFar,
                        "the open scene's MainCamera is already far from the walker, so this cannot tell the two apart");

            MethodInfo lateUpdate = typeof(FootfallDust).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            int footfalls = 0;
            for (int i = 0; i < 240; i++)
            {
                legs.SetTwist(legs.MaxSpeed * 0.5f, 0f);
                legs.Step(Dt);
                Physics.SyncTransforms();
                footfalls += legs.Footfalls.Count;
                lateUpdate.Invoke(dust, null);
            }
            int puffs = dust.Cloud.particleCount;

            Assert.Greater(footfalls, 0);
            Assert.AreEqual(0, puffs, "a walker 10 km from the camera drawing the frame still threw dust");
        }

        [Test]
        public void AFootfall_IsThrownOnce_HoweverOftenItIsRead()
        {
            (DesertCrawlerLocomotion legs, FootfallDust dust) = Walker(puffs: 4);
            for (int i = 0; i < 600 && legs.Footfalls.Count == 0; i++)
            {
                legs.SetTwist(legs.MaxSpeed * 0.5f, 0f);
                legs.Step(Dt);
            }
            Assert.Greater(dust.Present(float.NaN), 0);
            Assert.AreEqual(0, dust.Present(float.NaN), "the same footfall threw dust twice");
        }

        // ── rolling dust ───────────────────────────────────────────────────────

        private RollingDust Roller(out Transform contact)
        {
            subject = new GameObject("Roller");
            contact = new GameObject("Contact").transform;
            contact.SetParent(subject.transform, false);
            contact.localPosition = new Vector3(3f, 0f, 0f);
            var dust = subject.AddComponent<RollingDust>();
            dust.Configure(new[] { contact }, new[] { Cloud(contact) }, cruiseSpeed: 4f, peakRate: 3f);
            dust.ResetBaseline();
            return dust;
        }

        private static float Rate(RollingDust dust) => dust.CloudAt(0).emission.rateOverTime.constant;

        private static void Drive(RollingDust dust, Vector3 velocity, float yawRate, float distance, int frames = 120)
        {
            for (int i = 0; i < frames; i++)
            {
                dust.transform.position += velocity * Dt;
                dust.transform.Rotate(0f, yawRate * Dt, 0f);
                Physics.SyncTransforms();
                dust.Present(Dt, distance);
            }
        }

        [Test]
        public void AContactRollingAtCruise_ThrowsTheFullRate()
        {
            RollingDust dust = Roller(out _);
            Drive(dust, Vector3.forward * 4f, 0f, float.NaN);
            Assert.AreEqual(3f, Rate(dust), 0.05f);
        }

        [Test]
        public void AStandingMachine_ThrowsNoDust()
        {
            RollingDust dust = Roller(out _);
            Drive(dust, Vector3.zero, 0f, float.NaN);
            Assert.AreEqual(0f, Rate(dust), 1e-4f);
        }

        [Test]
        public void TurningOnTheSpot_DustsAtTheTrackEnds()
        {
            RollingDust dust = Roller(out _);
            Drive(dust, Vector3.zero, 45f, float.NaN);   // the contact, 3 m out, moves at ~2.4 m/s
            Assert.Greater(Rate(dust), 1f);
        }

        [Test]
        public void AContactOffTheGround_ThrowsNothing()
        {
            RollingDust dust = Roller(out _);
            subject.transform.position = new Vector3(0f, 5f, 0f);
            dust.ResetBaseline();
            Drive(dust, Vector3.forward * 4f, 0f, float.NaN);
            Assert.AreEqual(0f, Rate(dust), 1e-4f);
        }

        [Test]
        public void FarFromTheCamera_TheTracksThrowNothing()
        {
            RollingDust dust = Roller(out _);
            Drive(dust, Vector3.forward * 4f, 0f, 10000f);
            Assert.AreEqual(0f, Rate(dust), 1e-4f);
        }

        [Test]
        public void ASnapIntoPlace_IsNotSpeed()
        {
            RollingDust dust = Roller(out _);
            subject.transform.position += Vector3.forward * 300f;   // a load, a streaming migrate
            Physics.SyncTransforms();
            dust.Present(Dt, float.NaN);
            Assert.AreEqual(0f, Rate(dust), 1e-4f);
        }
    }
}
