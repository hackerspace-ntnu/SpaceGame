// The one client-side component that turns storm records into something you can see.
//
// It owns all three layers so their handover is decided in one place rather than negotiated
// between them: silhouettes for every storm in the world, the fullscreen fog for whichever storm
// the camera is actually standing in, and the near detail on top of that.
//
// Nothing here is authoritative. Delete this component and the storms still exist, still hurt and
// still blind the AI — you just cannot see them. That separation is what lets a dedicated server
// run the same scene without a renderer.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Weather
{
    [DefaultExecutionOrder(80)] // after gameplay has moved the camera for this frame
    [DisallowMultipleComponent]
    public class SandstormVisuals : MonoBehaviour
    {
        /// <summary>
        /// Storm density at the camera this frame. The render feature reads it to decide whether
        /// the fullscreen pass is worth enqueueing at all — in clear air the storm costs nothing.
        /// </summary>
        public static float CameraDensity { get; private set; }

        // Statics outlive both scene loads and — with domain reload turned off — play sessions.
        // A stale value here would let the fullscreen fog run in a scene that has no weather in it
        // at all, such as the main menu. SubsystemRegistration runs before the first scene loads.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => CameraDensity = 0f;

        [Header("Layers")]
        [Tooltip("Material using SpaceGame/SandstormWall. Draws the storm's silhouette from " +
                 "outside; without it the storm is invisible until you are inside it.")]
        [SerializeField] private Material wallMaterial;

        [Tooltip("Draw the far silhouette. Turning this off is the second step of a low quality " +
                 "tier, after the near detail.")]
        [SerializeField] private bool drawSilhouettes = true;

        [Tooltip("Spawn the profile's near-detail prefab at the camera. First thing to turn off " +
                 "on a weak machine.")]
        [SerializeField] private bool drawNearDetail = true;

        [Header("Camera")]
        [Tooltip("Leave empty to follow the main camera and re-find it when the player respawns.")]
        [SerializeField] private Camera targetCamera;

        private static readonly int CenterId = Shader.PropertyToID("_SandstormCenter");
        private static readonly int ShapeAId = Shader.PropertyToID("_SandstormShapeA");
        private static readonly int ShapeBId = Shader.PropertyToID("_SandstormShapeB");
        private static readonly int ColorId = Shader.PropertyToID("_SandstormColor");
        private static readonly int ParamsId = Shader.PropertyToID("_SandstormParams");
        private static readonly int LookId = Shader.PropertyToID("_SandstormLook");
        private static readonly int InteriorId = Shader.PropertyToID("_SandstormInterior");
        private static readonly int GrainId = Shader.PropertyToID("_SandstormGrain");
        private static readonly int SkyLightId = Shader.PropertyToID("_SandstormSkyLight");

        // Reused so evaluating the sky costs no allocation per frame.
        private static readonly Vector3[] SkyDirections = { Vector3.up };
        private static readonly Color[] SkySamples = new Color[1];

        private readonly Dictionary<int, SandstormWall> walls = new Dictionary<int, SandstormWall>();
        private readonly List<int> retired = new List<int>();

        private SandstormVfx nearDetail;
        private SandstormProfile nearDetailSource;

        private void OnDisable()
        {
            // Everything this component owns is scratch. Leaving a silhouette behind after the
            // renderer is switched off would be a storm nobody can end.
            foreach (SandstormWall wall in walls.Values)
            {
                if (wall != null)
                    Destroy(wall.gameObject);
            }

            walls.Clear();
            ClearNearDetail();
            CameraDensity = 0f;
            Shader.SetGlobalVector(CenterId, Vector4.zero);
        }

        /// <summary>
        /// Hands the shaders the sky's own radiance.
        /// <para>
        /// Both render paths used to read this themselves with <c>SampleSH</c>, and neither can:
        /// the fullscreen fog is a procedural blit with no per-draw SH constants at all, and the
        /// silhouette's renderer has light probes switched off. It came back zero in both, which
        /// left the sand lit by nothing but a 0.35 fudge of the sun colour. From OUTSIDE a storm
        /// that only made it a little flat — the sun march still lights the front of it. From
        /// INSIDE, where the sun march is occluded in every direction, it was the only light
        /// there was, and the storm interior rendered as a near-black screen.
        /// </para>
        /// <para>
        /// Public and static because the editor's storm preview draws a shell without this
        /// component ever running; <see cref="SandstormWall.Apply"/> calls it too.
        /// </para>
        /// </summary>
        public static void PushSkyLight()
        {
            RenderSettings.ambientProbe.Evaluate(SkyDirections, SkySamples);

            // Evaluate returns linear radiance, which is what a linear project's shaders want.
            Color sky = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? SkySamples[0]
                : SkySamples[0].gamma;

            Shader.SetGlobalVector(SkyLightId, new Vector4(
                Mathf.Max(0f, sky.r), Mathf.Max(0f, sky.g), Mathf.Max(0f, sky.b), 0f));
        }

        private void LateUpdate()
        {
            // Before the early-outs: the silhouettes are lit by this too, and they are drawn for
            // every storm in the world, not just the one the camera is standing in.
            PushSkyLight();

            Camera view = ResolveCamera();
            SandstormManager manager = SandstormManager.Instance;
            if (view == null || manager == null)
            {
                CameraDensity = 0f;
                Shader.SetGlobalVector(CenterId, Vector4.zero);
                return;
            }

            Vector3 eye = view.transform.position;
            bool inStorm = Sandstorms.TrySample(eye, out StormSample sample);
            CameraDensity = inStorm ? sample.Density : 0f;

            UpdateSilhouettes(manager.Resolved, eye);
            UpdateFog(inStorm, sample);
            UpdateNearDetail(view, inStorm, sample);
        }

        // ── Layer 1 ───────────────────────────────────────────────────────────────

        private void UpdateSilhouettes(IReadOnlyList<ResolvedStorm> storms, Vector3 eye)
        {
            if (!drawSilhouettes || wallMaterial == null)
            {
                if (walls.Count > 0)
                    RetireAllWalls();
                return;
            }

            for (int i = 0; i < storms.Count; i++)
            {
                ResolvedStorm storm = storms[i];
                if (!walls.TryGetValue(storm.Id, out SandstormWall wall) || wall == null)
                {
                    // Parented so the silhouettes belong to a scene and die with it, rather than
                    // being loose objects nobody owns. The wall divides out this transform's
                    // scale, so parenting cannot resize a storm.
                    wall = SandstormWall.Create($"Sandstorm Silhouette {storm.Id}", wallMaterial, false);
                    wall.transform.SetParent(transform, worldPositionStays: true);
                    walls[storm.Id] = wall;
                }

                wall.Apply(storm.Profile, storm.Footprint, storm.Intensity, storm.DensityAt(eye));
            }

            // A storm that has blown itself out leaves its silhouette behind unless someone
            // collects it. Two passes rather than mutating the dictionary while enumerating it.
            retired.Clear();
            foreach (int id in walls.Keys)
            {
                if (!Contains(storms, id))
                    retired.Add(id);
            }

            for (int i = 0; i < retired.Count; i++)
                RetireWall(retired[i]);
        }

        private static bool Contains(IReadOnlyList<ResolvedStorm> storms, int id)
        {
            for (int i = 0; i < storms.Count; i++)
            {
                if (storms[i].Id == id)
                    return true;
            }

            return false;
        }

        private void RetireWall(int id)
        {
            if (walls.TryGetValue(id, out SandstormWall wall) && wall != null)
                Destroy(wall.gameObject);

            walls.Remove(id);
        }

        private void RetireAllWalls()
        {
            retired.Clear();
            retired.AddRange(walls.Keys);
            for (int i = 0; i < retired.Count; i++)
                RetireWall(retired[i]);
        }

        // ── Layer 2 ───────────────────────────────────────────────────────────────

        // Only the strongest storm at the camera drives the fullscreen fog. Two storms deep enough
        // to overlap around one head is not a case worth doubling the shader's cost for, and the
        // silhouettes still show both.
        private void UpdateFog(bool inStorm, in StormSample sample)
        {
            if (!inStorm)
            {
                Shader.SetGlobalVector(CenterId, Vector4.zero);
                return;
            }

            StormFootprint shape = sample.Footprint;
            SandstormProfile profile = sample.Profile;

            Shader.SetGlobalVector(CenterId, new Vector4(shape.Center.x, shape.BaseY, shape.Center.y, 1f));
            Shader.SetGlobalVector(ShapeAId, new Vector4(shape.Radius, Mathf.Max(0.01f, shape.EdgeFeather),
                                                         shape.Height, Mathf.Max(0.01f, shape.HeightFeather)));
            Shader.SetGlobalVector(ShapeBId, new Vector4(shape.Kind == StormShapeKind.Wall ? 1f : 0f,
                                                         shape.LateralExtent, shape.Heading.x, shape.Heading.y));

            // Alpha carries the lifecycle intensity, so the shader multiplies shape by it exactly
            // the way ResolvedStorm.DensityAt does on the CPU.
            Color fog = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? profile.fogColor.linear
                : profile.fogColor;
            Shader.SetGlobalVector(ColorId, new Vector4(fog.r, fog.g, fog.b, sample.Intensity));

            Shader.SetGlobalVector(ParamsId, new Vector4(
                profile.visibilityAtFullIntensity / Mathf.Max(0.01f, profile.fogDensityScale),
                Mathf.Max(1f, profile.noiseScale),
                profile.noiseSpeed,
                profile.distortionStrength));

            Shader.SetGlobalVector(LookId, new Vector4(profile.erosion, profile.forwardScatter,
                                                       profile.ambient, profile.windStretch));
            Shader.SetGlobalVector(InteriorId, new Vector4(profile.interiorClearRadius,
                                                           profile.maxFogOpacity, 0f, 0f));
            Shader.SetGlobalFloat(GrainId, profile.grainStrength);
        }

        // ── Layer 3 ───────────────────────────────────────────────────────────────

        private void UpdateNearDetail(Camera view, bool inStorm, in StormSample sample)
        {
            bool wanted = drawNearDetail && inStorm &&
                          sample.Profile.nearDetailPrefab != null &&
                          sample.Density >= sample.Profile.nearDetailThreshold;

            if (!wanted)
            {
                ClearNearDetail();
                return;
            }

            if (nearDetailSource != sample.Profile)
            {
                ClearNearDetail();

                GameObject instance = Instantiate(sample.Profile.nearDetailPrefab, view.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                nearDetail = instance.GetComponent<SandstormVfx>();
                if (nearDetail == null)
                    nearDetail = instance.AddComponent<SandstormVfx>();

                nearDetailSource = sample.Profile;
            }
            else if (nearDetail != null && nearDetail.transform.parent != view.transform)
            {
                // The camera is replaced when the player respawns; follow it rather than leaving
                // the sand blowing around wherever the corpse fell.
                nearDetail.transform.SetParent(view.transform, worldPositionStays: false);
                nearDetail.transform.localPosition = Vector3.zero;
            }

            if (nearDetail != null)
            {
                nearDetail.Drive(sample.Density,
                                 new Vector3(sample.WindDirection.x, 0f, sample.WindDirection.y));
            }
        }

        private void ClearNearDetail()
        {
            if (nearDetail != null)
                Destroy(nearDetail.gameObject);

            nearDetail = null;
            nearDetailSource = null;
        }

        // Re-resolved rather than cached once: the player prefab is respawned on death, taking its
        // camera with it, and a camera captured at Awake would leave the storm rendering from the
        // position of a corpse.
        private Camera ResolveCamera()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            return targetCamera;
        }
    }
}
