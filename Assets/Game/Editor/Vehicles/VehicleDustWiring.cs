// Puts sand dust on a machine: footfall dust under a legged one's feet (FootfallDust), rolling dust at
// a tracked one's ground contacts (RollingDust). Both fill DustCloudRecipe clouds -- the monowheel's
// lingering cloud -- in one shared sand material. Called by the builder that owns each prefab, so a
// rebuild keeps its dust: StriderCityBuilder (the houses), DesertCrawlerBuilder,
// StriderCrabOutriderBuilder and StriderBargeBuilder.
//
// The caps are where the overdraw budget lives (GDC-L1-TECH-0002): a cloud holds exactly what its
// peak rate keeps alive for the recipe's longest life, and not a puff more.
using SpaceGame.Locomotion;
using SpaceGame.Vehicles;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class VehicleDustWiring
    {
        /// <summary>The desert's dust colour: the monowheel's, so every machine's cloud is the same sand.</summary>
        public static readonly Color SandTint = new Color(0.78f, 0.66f, 0.47f, 1f);
        /// <summary>The soft fade (m) where a puff meets the sand, as the monowheel's dust.</summary>
        public const float SandSoftFade = 1.2f;
        /// <summary>The one material every machine's sand dust shares.</summary>
        public const string SandMaterialPath = "Assets/Game/Art/Materials/Vehicles/SandDust.mat";

        public const string FootfallCloudName = "FX_FootfallDust";
        public const string TrackCloudName = "FX_TrackDust";

        /// <summary>Track dust is thrown up and back off the ground run, as the monowheel's off its paddles.</summary>
        private static readonly Quaternion TrackDustAim = Quaternion.LookRotation(new Vector3(0f, 0.8f, -0.6f));
        /// <summary>Born just off the sand, along the cloud's own axes, as the monowheel's.</summary>
        private static readonly Vector3 TrackDustBirthOffset = new Vector3(0f, 0.4f, 0f);

        public static Material SandMaterial() =>
            DustCloudRecipe.Material(SandMaterialPath, SandTint, SandSoftFade);

        /// <summary>The cap for a footfall cloud: the machine's fastest footfall rate, in puffs, kept alive for a full life.</summary>
        public static int FootfallCap(float peakFootfallsPerSecond, int puffsPerFootfall) =>
            DustCloudRecipe.CapFor(peakFootfallsPerSecond * puffsPerFootfall);

        /// <summary>
        /// Footfall dust for the legged machine at <paramref name="root"/>: one cloud, filled by a
        /// FootfallDust watching its LeggedLocomotion. <paramref name="peakFootfallsPerSecond"/> is how
        /// many feet land per second at the machine's top speed (measured, and pinned by StriderDustPrefabTests).
        /// </summary>
        public static FootfallDust AddFootfallDust(GameObject root, int puffsPerFootfall, float peakFootfallsPerSecond)
        {
            var legs = root.GetComponent<LeggedLocomotion>();
            if (legs == null)
                throw new System.InvalidOperationException($"{root.name} has no LeggedLocomotion; footfall dust has no feet to follow.");

            ParticleSystem cloud = DustCloudRecipe.Cloud(root.transform, FootfallCloudName, Vector3.zero,
                                                         Quaternion.LookRotation(Vector3.up), SandMaterial(), SandTint,
                                                         FootfallCap(peakFootfallsPerSecond, puffsPerFootfall), Vector3.zero);
            var dust = root.AddComponent<FootfallDust>();
            dust.Configure(legs, cloud, puffsPerFootfall);
            return dust;
        }

        /// <summary>
        /// Rolling dust at each of <paramref name="contacts"/> (ground-level markers under
        /// <paramref name="root"/>): one cloud per contact, parented to it, driven by a RollingDust that
        /// peaks at <paramref name="peakRatePerContact"/> puffs/s at <paramref name="cruiseSpeed"/>.
        /// </summary>
        public static RollingDust AddRollingDust(GameObject root, Transform[] contacts, float cruiseSpeed, float peakRatePerContact)
        {
            Material sand = SandMaterial();
            int cap = DustCloudRecipe.CapFor(peakRatePerContact);
            var clouds = new ParticleSystem[contacts.Length];
            for (int i = 0; i < contacts.Length; i++)
                clouds[i] = DustCloudRecipe.Cloud(contacts[i], TrackCloudName, Vector3.zero, TrackDustAim, sand, SandTint,
                                                  cap, TrackDustBirthOffset);
            var dust = root.AddComponent<RollingDust>();
            dust.Configure(contacts, clouds, cruiseSpeed, peakRatePerContact);
            return dust;
        }
    }
}
