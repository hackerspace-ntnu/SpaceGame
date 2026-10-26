using System;
using System.Collections.Generic;
using SpaceGame.Vehicles.DuneFoil;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Grows stylized grass on every loaded terrain, wherever a biome's terrain layer is painted.
    /// Each biome pairs a <see cref="TerrainLayer"/> with a <see cref="GrassFieldProfile"/>, so
    /// painting the dry-grass layer grows dry grass and painting the meadow layer grows meadow.
    /// One field per world is enough: it follows world chunks as they stream in and out, and
    /// re-grows the grass under a brush stroke as soon as the stroke ends.
    ///
    /// The grass is derived entirely from the terrains and a seed, so it is identical on every
    /// client and after every reload without being networked or saved.
    ///
    /// Per camera, from <c>beginCameraRendering</c> (which is what makes the grass show in the
    /// Scene view too): each biome is culled and animated on the GPU by GrassCull.compute, then
    /// drawn with three indirect draws. See <see cref="GrassBiomeStream"/>. The painted ground
    /// itself is coloured to match by <see cref="GrassGroundTint"/>, so fields read from afar.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class GrassField : MonoBehaviour
    {
        private static readonly int WindId = Shader.PropertyToID("_GrassWind");
        private static readonly int BendersId = Shader.PropertyToID("_GrassBenders");
        private static readonly int BenderCountId = Shader.PropertyToID("_GrassBenderCount");
        private static readonly int TimeId = Shader.PropertyToID("_GrassTime");
        private static readonly int CameraPositionId = Shader.PropertyToID("_CameraPosition");
        private static readonly int FrustumPlanesId = Shader.PropertyToID("_FrustumPlanes");
        // The same wind, clock and benders as globals, for SpaceGame/StylizedFoliage (trees, bushes).
        private static readonly int FoliageWindId = Shader.PropertyToID("_FoliageWind");
        private static readonly int FoliageTimeId = Shader.PropertyToID("_FoliageTime");
        private static readonly int FoliageBendersId = Shader.PropertyToID("_FoliageBenders");
        private static readonly int FoliageBenderCountId = Shader.PropertyToID("_FoliageBenderCount");
        private static readonly List<GrassField> Enabled = new List<GrassField>();

        [Serializable]
        private struct Biome
        {
            [Tooltip("The terrain layer this grass grows on. Empty grows it on every terrain, everywhere.")]
            public TerrainLayer terrainLayer;
            public GrassFieldProfile profile;
        }

        [Tooltip("GrassCull.compute: culls and animates the tufts on the GPU.")]
        [SerializeField] private ComputeShader cullShader;

        [Tooltip("Which grass grows on which painted terrain layer.")]
        [SerializeField] private Biome[] biomes = Array.Empty<Biome>();

        [Tooltip("World height below which nothing grows — set it to the water surface.")]
        [SerializeField] private float minimumHeight = -10000f;

        [Tooltip("Changes the scatter without changing its density.")]
        [SerializeField] private int seed = 1;

        [Tooltip("Chunk scatters started per camera per frame across all biomes. They run on " +
                 "worker threads; this only paces how fast new ground fills in.")]
        [SerializeField, Range(1, 64)] private int chunkBuildsPerFrame = 8;

        [Tooltip("Render ticks a chunk survives unseen before its tufts are released.")]
        [SerializeField, Min(1)] private int chunkLifetimeTicks = 600;

        [Header("Wind")]
        [Tooltip("Wind speed (m/s) at which each material's Lean is applied as authored. A " +
                 "WindField in the scene drives the speed; without one the fallback is used.")]
        [SerializeField, Min(0.1f)] private float referenceWindSpeed = 10f;
        [SerializeField, Range(0f, 360f)] private float fallbackBearingFrom = 225f;
        [SerializeField, Min(0f)] private float fallbackWindSpeed = 7f;
        [Tooltip("Upper bound on the lean multiplier, so a gale flattens rather than inverts.")]
        [SerializeField, Min(0f)] private float maxWindScale = 2f;

        private readonly List<GrassBiomeStream> streams = new List<GrassBiomeStream>();
        private readonly List<Rect> changed = new List<Rect>();
        private readonly Vector4[] benders = new Vector4[GrassBender.MaxActive];
        private readonly Plane[] frustum = new Plane[6];
        private readonly Vector4[] frustumPlanes = new Vector4[6];

        private PaintedGround ground;
        private GrassGroundTint tint;
        private CommandBuffer commands;
        private GrassBiomeStream.CullKernels kernels;
        private int maxScattersInFlight;
        private int tick;
        private bool dirty = true;

        // The grass's own clock: level time in play, so it matches the rest of the game, and
        // wall time while authoring, when level time stands still.
        private static float GrassTime => Application.isPlaying ? Time.timeSinceLevelLoad : Time.realtimeSinceStartup;

        private void OnEnable()
        {
            Enabled.Add(this);
            if (Enabled.Count > 1)
                Debug.LogWarning($"[GrassField] '{name}' and '{Enabled[0].name}' are both enabled and both " +
                                 "grow on every loaded terrain, so the grass is doubled. Keep one per world.", this);
            commands = new CommandBuffer { name = "Grass Cull" };
            // Half the cores (at least 4): a 300 m reach is over a thousand chunks per biome, and
            // fewer workers left the far ring unfilled for many seconds and the near chunks
            // waiting in line. Most far chunks are sparse levels that scatter in a fraction of a
            // millisecond, so this no longer crowds the main and render threads.
            maxScattersInFlight = Mathf.Max(4, SystemInfo.processorCount / 2);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            GrassFieldProfile.Edited += OnProfileEdited;
            dirty = true;
        }

        private void OnDisable()
        {
            Enabled.Remove(this);
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            GrassFieldProfile.Edited -= OnProfileEdited;
            Release();
            commands.Release();
            commands = null;
        }

        private void OnValidate() => dirty = true;

        /// <summary>Re-scatter everything, e.g. after a terrain was changed from code.</summary>
        public void Regenerate() => dirty = true;

        private void OnProfileEdited(GrassFieldProfile profile)
        {
            foreach (Biome biome in biomes)
                if (biome.profile == profile)
                    dirty = true;
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView)
                return;
            if ((cam.cullingMask & (1 << gameObject.layer)) == 0)
                return;
            if (dirty)
                Rebuild();
            if (streams.Count == 0)
                return;

            tick++;
            Vector3 eye = cam.transform.position;
            ApplyGroundChanges();
            tint.Update(ground);

            int budget = Mathf.Min(chunkBuildsPerFrame, maxScattersInFlight - PendingScatters());
            bool harvested = false;
            // A different biome goes first each tick, so a busy one cannot take every budget unit.
            for (int i = 0; i < streams.Count; i++)
                harvested |= streams[(tick + i) % streams.Count].Stream(eye, ground, tick, ref budget);

            SetCullGlobals(cam, eye);
            foreach (GrassBiomeStream stream in streams)
                stream.Cull(commands, cullShader, kernels);
            context.ExecuteCommandBuffer(commands);
            commands.Clear();

            foreach (GrassBiomeStream stream in streams)
            {
                stream.Draw(cam, eye, gameObject.layer);
                stream.ReleaseStale(tick, chunkLifetimeTicks);
            }
            RepaintWhileScattering(harvested);
        }

        private void Rebuild()
        {
            Release();
            dirty = false;
            if (cullShader == null || !SystemInfo.supportsComputeShaders)
            {
                Debug.LogError($"[GrassField] '{name}' needs GrassCull.compute in Cull Shader and a GPU with " +
                               "compute shaders. No grass is drawn.", this);
                return;
            }

            kernels = new GrassBiomeStream.CullKernels(cullShader);
            var paints = new List<GroundPaint>();
            var growsAbove = new List<float>();
            var profiles = new List<GrassFieldProfile>();
            for (int i = 0; i < biomes.Length; i++)
            {
                GrassFieldProfile profile = biomes[i].profile;
                if (profile == null || profile.Material == null)
                {
                    Debug.LogError($"[GrassField] Biome {i} on '{name}' has no profile or its profile has " +
                                   "no material. That biome grows nothing.", this);
                    continue;
                }
                streams.Add(new GrassBiomeStream(profile, paints.Count, seed, minimumHeight));
                paints.Add(GroundPaint.Splat(biomes[i].terrainLayer));
                // The scatter fades to nothing at half the profile's full-coverage weight.
                growsAbove.Add(profile.MinimumLayerWeight * 0.5f);
                profiles.Add(profile);
            }
            ground = new PaintedGround(paints, growsAbove);
            tint = new GrassGroundTint(profiles, minimumHeight);
        }

        private void Release()
        {
            foreach (GrassBiomeStream stream in streams)
                stream.Dispose();
            streams.Clear();
            tint?.Dispose();
            tint = null;
            ground?.Dispose();
            ground = null;
        }

        private void ApplyGroundChanges()
        {
            changed.Clear();
            ground.TakeChanges(changed);
            foreach (Rect area in changed)
                foreach (GrassBiomeStream stream in streams)
                    stream.Invalidate(area);
        }

        private int PendingScatters()
        {
            int pending = 0;
            foreach (GrassBiomeStream stream in streams)
                pending += stream.PendingCount;
            return pending;
        }

        /// <summary>What every biome's cull shares for this camera: wind, benders, time, frustum.</summary>
        private void SetCullGlobals(Camera cam, Vector3 eye)
        {
            WindField wind = WindField.Active;
            Vector3 direction = wind != null
                ? wind.Direction
                : Quaternion.Euler(0f, fallbackBearingFrom + 180f, 0f) * Vector3.forward;
            float speed = wind != null ? wind.SpeedAt(eye) : fallbackWindSpeed;
            float scale = Mathf.Min(speed / referenceWindSpeed, maxWindScale);
            var windVector = new Vector4(direction.x, direction.z, scale, 0f);
            commands.SetComputeVectorParam(cullShader, WindId, windVector);
            commands.SetGlobalVector(FoliageWindId, windVector);

            int count = GrassBender.Gather(eye, benders);
            commands.SetComputeVectorArrayParam(cullShader, BendersId, benders);
            commands.SetComputeFloatParam(cullShader, BenderCountId, count);
            commands.SetComputeFloatParam(cullShader, TimeId, GrassTime);
            commands.SetGlobalVectorArray(FoliageBendersId, benders);
            commands.SetGlobalFloat(FoliageBenderCountId, count);
            commands.SetGlobalFloat(FoliageTimeId, GrassTime);
            commands.SetComputeVectorParam(cullShader, CameraPositionId, eye);

            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            for (int i = 0; i < frustum.Length; i++)
                frustumPlanes[i] = new Vector4(frustum[i].normal.x, frustum[i].normal.y, frustum[i].normal.z,
                                               frustum[i].distance);
            commands.SetComputeVectorArrayParam(cullShader, FrustumPlanesId, frustumPlanes);
        }

        // While authoring, the Scene view only redraws when something asks it to; without this,
        // chunks finished on worker threads would not show until the mouse moved.
        private void RepaintWhileScattering(bool harvested)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && (harvested || PendingScatters() > 0))
                UnityEditor.SceneView.RepaintAll();
#endif
        }
    }
}
