using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Locomotion;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.EditorTools
{
    /// Every machine in the Strider city kicks up the monowheel's sand: footfall dust under the legged
    /// ones, rolling dust at the barges' tracks. Read off the built prefabs, and the caps checked against
    /// what each machine's legs actually do at top speed -- the overdraw budget is only a budget if the
    /// rate it was sized for is the real one.
    public class StriderDustPrefabTests
    {
        private const float Dt = 1f / 60f;

        public static IEnumerable<TestCaseData> LeggedMachines()
        {
            yield return new TestCaseData(StriderCityBuilder.HabitatPath, StriderCityBuilder.PuffsPerFootfall,
                                          StriderCityBuilder.PeakFootfallsPerSecond);
            yield return new TestCaseData(DesertCrawlerBuilder.PrefabPath, DesertCrawlerBuilder.PuffsPerFootfall,
                                          DesertCrawlerBuilder.PeakFootfallsPerSecond);
            yield return new TestCaseData(StriderCrabOutriderBuilder.PrefabPath, StriderCrabOutriderBuilder.PuffsPerFootfall,
                                          StriderCrabOutriderBuilder.PeakFootfallsPerSecond);
        }

        /// Every vehicle in the city and, for the legged ones, its near dust's peak (puffs/s); NaN: read off the prefab.
        public static IEnumerable<TestCaseData> CityVehicles()
        {
            yield return new TestCaseData(StriderCityBuilder.HabitatPath, StriderCityBuilder.PeakFootfallsPerSecond * StriderCityBuilder.PuffsPerFootfall);
            yield return new TestCaseData(DesertCrawlerBuilder.PrefabPath, DesertCrawlerBuilder.PeakFootfallsPerSecond * DesertCrawlerBuilder.PuffsPerFootfall);
            yield return new TestCaseData(StriderCrabOutriderBuilder.PrefabPath,
                                          StriderCrabOutriderBuilder.PeakFootfallsPerSecond * StriderCrabOutriderBuilder.PuffsPerFootfall);
            foreach ((string _, string variant) in StriderBargeBuilder.Barges)
                yield return new TestCaseData(StriderBargeBuilder.PrefabPath(variant), float.NaN);
            foreach (string variant in StriderMonowheelBuilder.Singles.Concat(StriderMonowheelBuilder.Doubles))
                yield return new TestCaseData(StriderMonowheelBuilder.PrefabPath(variant), float.NaN);
        }

        private static float NearPeak(GameObject prefab, float leggedPeak)
        {
            if (!float.IsNaN(leggedPeak)) return leggedPeak;
            if (prefab.TryGetComponent(out RollingDust rolling)) return rolling.ContactCount * rolling.RateAtFullSpeed;
            return prefab.GetComponentInChildren<MonowheelPresentation>(true).DustAtFullSpeed;
        }

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"{path} is missing");
            return prefab;
        }

        private static void AssertSharedSand(ParticleSystem cloud)
        {
            Material material = cloud.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Assert.IsNotNull(material, $"{cloud.name} has no material and would draw nothing");
            Assert.AreEqual(VehicleDustWiring.SandMaterialPath, AssetDatabase.GetAssetPath(material),
                            $"{cloud.name} does not use the shared sand material");
            Assert.AreEqual(ParticleSystemSimulationSpace.World, cloud.main.simulationSpace,
                            $"{cloud.name} would drag its cloud along with the machine");
        }

        [TestCaseSource(nameof(LeggedMachines))]
        public void ALeggedMachine_ThrowsFootfallDust_FromItsOwnLegs(string path, int puffs, float peak)
        {
            GameObject prefab = Load(path);
            var dust = prefab.GetComponent<FootfallDust>();
            Assert.IsNotNull(dust, $"{prefab.name} has no FootfallDust");
            Assert.AreSame(prefab.GetComponent<LeggedLocomotion>(), dust.Locomotion, "the dust follows someone else's legs");
            Assert.IsNotNull(dust.Cloud, "no cloud to throw the dust into");
            Assert.IsTrue(dust.Cloud.transform.IsChildOf(prefab.transform));
            Assert.AreEqual(puffs, dust.PuffsPerFootfall);
            Assert.AreEqual(VehicleDustWiring.FootfallCap(peak, puffs), dust.Cloud.main.maxParticles,
                            "the cloud's cap is not the one its peak footfall rate needs");
            AssertSharedSand(dust.Cloud);
        }

        [TestCaseSource(nameof(CityVehicles))]
        public void ACityVehicle_ThrowsFarDust_InItsNearDustsBand(string path, float leggedPeak)
        {
            GameObject prefab = Load(path);
            FarDust far = prefab.GetComponentInChildren<FarDust>(true);
            Assert.IsNotNull(far, $"{prefab.name} has no far dust: rebuild it");
            Assert.AreEqual(VehicleDustWiring.FarCloudName, far.name);
            Assert.AreSame(prefab.transform, far.transform.parent);
            Assert.AreSame(far.GetComponent<ParticleSystem>(), far.Cloud);

            IDustLodBand band = prefab.GetComponentInChildren<IDustLodBand>(true);
            Assert.AreEqual(band.LodNear, far.FadeNear, "fades in where the near dust starts fading out");
            Assert.AreEqual(band.LodFar, far.FadeFar);
            Assert.AreEqual(NearPeak(prefab, leggedPeak) * VehicleDustWiring.FarDustRateFraction, far.RateAtFullSpeed, 1e-4f);
            Assert.AreEqual(StriderCityBuilder.CityLeaderSpeed, far.FullSpeed, 1e-4f, "full while marching with the city");
            Assert.AreEqual(VehicleDustWiring.FarDustCullDistance, far.CullDistance);
            Assert.AreEqual(DustCloudRecipe.MinSize * VehicleDustWiring.FarDustSizeMultiplier, far.Cloud.main.startSize.constantMin, 1e-4f);
            AssertSharedSand(far.Cloud);
        }

        /// The cap was sized for `peak` feet landing a second. Walk the real machine flat out, then
        /// turn it on the spot as fast as it turns, and count: more footfalls than that and the cloud
        /// is starved of puffs at speed.
        [TestCaseSource(nameof(LeggedMachines))]
        public void ALeggedMachine_NeverLandsMoreFeet_ThanItsCloudWasSizedFor(string path, int puffs, float peak)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject machine = null;
            try
            {
                ground.transform.position = new Vector3(0f, -1f, 0f);
                ground.transform.localScale = new Vector3(2000f, 2f, 2000f);
                machine = Object.Instantiate(Load(path));
                machine.transform.position = new Vector3(-600f, 30f, 0f);
                Physics.SyncTransforms();
                var legs = machine.GetComponent<LeggedLocomotion>();
                legs.Initialise();
                legs.SnapToGround();
                Physics.SyncTransforms();

                float Rate(float speed, float yawRate, int frames)
                {
                    int footfalls = 0;
                    for (int i = 0; i < frames; i++)
                    {
                        legs.SetTwist(speed, yawRate);
                        legs.Step(Dt);
                        Physics.SyncTransforms();
                        footfalls += legs.Footfalls.Count;
                    }
                    return footfalls / (frames * Dt);
                }

                Rate(0f, 0f, 60);   // settle
                float walking = Rate(legs.MaxSpeed, 0f, 600);
                float turning = Rate(0f, legs.MaxYawRate, 600);
                Assert.Greater(walking, 0f, $"{machine.name} never put a foot down");
                Assert.LessOrEqual(Mathf.Max(walking, turning), peak,
                                   $"{machine.name} lands {walking:F2} feet/s walking and {turning:F2} turning; its cloud was sized for {peak}");
            }
            finally
            {
                Object.DestroyImmediate(machine);
                Object.DestroyImmediate(ground);
            }
        }

        [TestCase("StriderDuneBarge", 8)]
        [TestCase("StriderDuneBargeLookout", 8)]
        [TestCase("StriderDuneBargeCompact", 4)]
        public void ABarge_ThrowsRollingDust_AtEveryTrackContact(string variant, int contacts)
        {
            GameObject prefab = Load(StriderBargeBuilder.PrefabPath(variant));
            var dust = prefab.GetComponent<RollingDust>();
            Assert.IsNotNull(dust, $"{variant} has no RollingDust");
            Transform markers = prefab.transform.Find(StriderBargeBuilder.TrackContactsName);
            Assert.IsNotNull(markers, $"{variant} has no {StriderBargeBuilder.TrackContactsName}");
            Assert.AreEqual(contacts, markers.childCount);
            Assert.AreEqual(contacts, dust.ContactCount, "not every track contact throws dust");

            int cap = DustCloudRecipe.CapFor(StriderBargeBuilder.TrackDustPerContact);
            for (int i = 0; i < dust.ContactCount; i++)
            {
                Assert.AreSame(markers, dust.Contact(i).parent, $"contact {i} is not one of the track markers");
                Assert.AreSame(dust.Contact(i), dust.CloudAt(i).transform.parent, $"contact {i}'s cloud is not at that contact");
                Assert.AreEqual(cap, dust.CloudAt(i).main.maxParticles);
                AssertSharedSand(dust.CloudAt(i));
            }
            Assert.AreEqual(StriderBargeBuilder.TrackDustPerContact, dust.RateAtFullSpeed);
        }
    }
}
