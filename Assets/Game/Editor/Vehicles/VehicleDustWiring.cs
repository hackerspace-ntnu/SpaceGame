// Puts sand dust on a machine: footfall dust under a legged one's feet (FootfallDust), rolling dust at
// a tracked one's ground contacts (RollingDust). Both fill DustCloudRecipe clouds -- the monowheel's
// lingering cloud -- in one shared sand material. Called by the builder that owns each prefab, so a
// rebuild keeps its dust: StriderCityBuilder (the houses), DesertCrawlerBuilder,
// StriderCrabOutriderBuilder and StriderBargeBuilder. Every Strider city vehicle also gets far dust
// (FarDust): huge, sparse, fading in as the near dust fades out.
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
        public const string FarCloudName = "FX_FarDust";

        /// <summary>Far puffs are this many times the near ones across: the recipe's 2-3.2 m become 8-12.8 m.</summary>
        public const float FarDustSizeMultiplier = 4f;
        /// <summary>And live this many times as long (12-18 s), so a slow march leaves a standing wall.</summary>
        public const float FarDustLifeMultiplier = 1.5f;
        /// <summary>At this fraction of the machine's near dust's peak rate.</summary>
        public const float FarDustRateFraction = 0.12f;
        /// <summary>No far dust past the Strider vehicles' cull (SettlementLodSettings.strider.cullBeyondMetres; SettlementLodPrefabTests pins the two).</summary>
        public const float FarDustCullDistance = 1500f;
        /// <summary>Far puffs are born from the hull's lowest point up to this fraction of its height, so the cloud stands up the legs rather than lying on the sand.</summary>
        public const float FarDustBirthHeightFraction = 0.5f;
        /// <summary>And across this many times the hull's footprint, so from any side some dust stands between the camera and the legs.</summary>
        public const float FarDustFootprintSpread = 2f;
        /// <summary>The birth box leads the hull by this fraction of its length, so a marching vehicle walks into its puffs as they swell: seen head-on, a trail alone lies behind its legs.</summary>
        public const float FarDustLead = 0.5f;
        /// <summary>A far puff's alpha at birth: veils the legs but leaves the hull readable (1 hid the settlement, 2026-10-06).</summary>
        public const float FarDustOpacity = 0.6f;
        /// <summary>Once drag has spent its throw a far puff climbs at this speed (m/s), so the trail behind a marching vehicle stands up into a wall.</summary>
        public const float FarDustRiseSpeed = 0.8f;

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

        /// <summary>The gravity modifier whose pull, against the recipe's drag, settles a far puff at <see cref="FarDustRiseSpeed"/>.</summary>
        public static float FarDustRiseGravity() => FarDustRiseSpeed * DustCloudRecipe.Drag / Physics.gravity.y;

        /// <summary>
        /// Far dust for the machine at <paramref name="root"/>: one huge, sparse cloud on its own child
        /// (<see cref="FarCloudName"/>), born round and ahead of the lower part of the hull, thrown up and
        /// climbing, fading in over the band its near dust fades out. <paramref name="nearPeakRate"/> is that near
        /// dust's peak (puffs/s).
        /// Call after the near dust and after anything that measures renderer bounds.
        /// </summary>
        public static FarDust AddFarDust(GameObject root, float nearPeakRate)
        {
            var band = root.GetComponentInChildren<IDustLodBand>(true);
            if (band == null)
                throw new System.InvalidOperationException($"{root.name} has no near dust; far dust crossfades with it, so add that first.");

            Bounds hull = HullBounds(root);
            float rate = nearPeakRate * FarDustRateFraction;
            int cap = Mathf.CeilToInt(rate * DustCloudRecipe.MaxLife * FarDustLifeMultiplier);
            ParticleSystem cloud = DustCloudRecipe.Cloud(root.transform, FarCloudName, new Vector3(hull.center.x, hull.min.y, hull.center.z),
                                                         Quaternion.LookRotation(Vector3.up, Vector3.forward), SandMaterial(), SandTint, cap, Vector3.zero);
            ParticleSystem.MainModule main = cloud.main;
            main.startSize = new ParticleSystem.MinMaxCurve(DustCloudRecipe.MinSize * FarDustSizeMultiplier,
                                                            DustCloudRecipe.MaxSize * FarDustSizeMultiplier);
            main.startLifetime = new ParticleSystem.MinMaxCurve(DustCloudRecipe.MinLife * FarDustLifeMultiplier,
                                                                DustCloudRecipe.MaxLife * FarDustLifeMultiplier);
            main.gravityModifier = FarDustRiseGravity();
            main.startColor = new Color(SandTint.r, SandTint.g, SandTint.b, FarDustOpacity);
            // The cloud's own axes: z is up (its throw), y is the root's forward, so the box's base is the footprint.
            float birthHeight = hull.size.y * FarDustBirthHeightFraction;
            ParticleSystem.ShapeModule shape = cloud.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(hull.size.x * FarDustFootprintSpread, hull.size.z * FarDustFootprintSpread, birthHeight);
            shape.position = new Vector3(0f, hull.size.z * FarDustLead, birthHeight * 0.5f);

            var dust = cloud.gameObject.AddComponent<FarDust>();
            dust.Configure(cloud, rate, band, FarDustCullDistance);
            return dust;
        }

        /// <summary>The bounds, in the root's own space, of every mesh renderer under it: particles are not hull.</summary>
        private static Bounds HullBounds(GameObject root)
        {
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            Bounds hull = default;
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;

                Bounds world = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = Vector3.Scale(world.extents, new Vector3((corner & 1) == 0 ? -1f : 1f,
                                                                              (corner & 2) == 0 ? -1f : 1f,
                                                                              (corner & 4) == 0 ? -1f : 1f));
                    Vector3 local = toRoot.MultiplyPoint3x4(world.center + offset);
                    if (any) hull.Encapsulate(local);
                    else hull = new Bounds(local, Vector3.zero);
                    any = true;
                }
            }
            if (!any) throw new System.InvalidOperationException($"{root.name} has no mesh renderer to raise far dust round.");
            return hull;
        }
    }
}
