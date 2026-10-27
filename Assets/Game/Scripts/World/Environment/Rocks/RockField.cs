using System;
using System.Collections.Generic;
using SpaceGame.Agents;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Scatters rocks on every loaded terrain wherever a profile's detail layer is painted with
    /// the terrain's Paint Details brush. Unlike grass, the paint is a detail layer, so painting
    /// rocks never changes the ground texture. Each biome pairs the detail prototype prefab (what
    /// the brush palette shows) with a <see cref="RockFieldProfile"/>, whose size classes decide
    /// which models grow, how big, and how far they are drawn.
    ///
    /// The rocks are derived entirely from the terrains and a seed, so they are identical on
    /// every client and after every reload without being networked or saved. In play, the rocks
    /// of colliding size classes near any player get colliders (<see cref="RockColliders"/>).
    ///
    /// Per camera, from <c>beginCameraRendering</c>: every size class is culled on the GPU by
    /// RockCull.compute and drawn with one indirect draw per model per Mesh LOD. See
    /// <see cref="RockClassStream"/>. The terrains need Detail Distance 0, or Unity draws its own
    /// copies of the prototype prefabs too: <c>Tools/World/Rocks/Add Rock Paint To Selected Terrains</c>
    /// sets that up.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class RockField : MonoBehaviour
    {
        private static readonly int CameraPositionId = Shader.PropertyToID("_CameraPosition");
        private static readonly int FrustumPlanesId = Shader.PropertyToID("_FrustumPlanes");
        private static readonly int ScreenScaleId = Shader.PropertyToID("_ScreenScale");
        private static readonly int ShadowDirectionId = Shader.PropertyToID("_ShadowDirection");
        private static readonly int ArrivalSecondsId = Shader.PropertyToID("_ArrivalSeconds");
        private static readonly List<RockField> Enabled = new List<RockField>();

        // Shadows of rocks just off screen can fall on screen; the cull keeps a rock whose shadow,
        // up to this many of its radii long, reaches the frustum (a low sun is capped here).
        private const float MaxShadowLengthInRadii = 6f;

        [Serializable]
        public struct Biome
        {
            [Tooltip("The prefab of the terrain detail prototype this profile grows on — the entry " +
                     "the Paint Details palette shows. Unity must not draw it: Detail Distance 0.")]
            public GameObject paintPrototype;
            public RockFieldProfile profile;
        }

        [Tooltip("RockCull.compute: culls the rocks and picks their LODs on the GPU.")]
        [SerializeField] private ComputeShader cullShader;

        [Tooltip("Which rocks grow on which painted terrain detail layer.")]
        [SerializeField] private Biome[] biomes = Array.Empty<Biome>();

        [Tooltip("World height below which no rock grows — set it to the water surface.")]
        [SerializeField] private float minimumHeight = -10000f;

        [Tooltip("Changes the scatter without changing its density.")]
        [SerializeField] private int seed = 1;

        [Tooltip("Chunk scatters started per camera per frame across all size classes. They run " +
                 "on worker threads; this only paces how fast new ground fills in.")]
        [SerializeField, Range(1, 64)] private int chunkBuildsPerFrame = 8;

        [Tooltip("Render ticks a chunk survives unseen before its rocks are released.")]
        [SerializeField, Min(1)] private int chunkLifetimeTicks = 600;

        [Tooltip("The light whose shadows the rocks cast. Empty uses the scene's sun.")]
        [SerializeField] private Light sun;

        [Tooltip("Rocks of a chunk that finishes scattering while in view grow out of the ground " +
                 "over this long, instead of popping in together (after loading, a teleport or a " +
                 "brush stroke).")]
        [SerializeField, Min(0.01f)] private float arrivalSeconds = 0.6f;

        [Header("Colliders (play only)")]
        [Tooltip("Rocks of colliding size classes this close to a player get a collider.")]
        [SerializeField, Min(1f)] private float colliderRadius = 40f;

        [Tooltip("The physics layer of the rock colliders; the ground layers players walk on.")]
        [SerializeField] private int colliderLayer;

        [Tooltip("Seconds between collider updates.")]
        [SerializeField, Min(0.02f)] private float colliderInterval = 0.25f;

        private readonly List<RockClassStream> streams = new List<RockClassStream>();
        private readonly List<Rect> changed = new List<Rect>();
        private readonly List<Transform> players = new List<Transform>();
        private readonly List<Terrain> loaded = new List<Terrain>();
        private readonly HashSet<Terrain> warned = new HashSet<Terrain>();
        private readonly Plane[] frustum = new Plane[6];
        private readonly Vector4[] frustumPlanes = new Vector4[6];

        private PaintedGround ground;
        private RockColliders colliders;
        private CommandBuffer commands;
        private RockClassStream.CullKernels kernels;
        private int maxScattersInFlight;
        private int tick;
        private float nextColliderSync;
        private bool dirty = true;

        /// <summary>The biomes, for the editor's paint setup.</summary>
        public IReadOnlyList<Biome> Biomes => biomes;

        private void OnEnable()
        {
            Enabled.Add(this);
            if (Enabled.Count > 1)
                Debug.LogWarning($"[RockField] '{name}' and '{Enabled[0].name}' are both enabled and both " +
                                 "grow on every loaded terrain, so the rocks are doubled. Keep one per world.", this);
            commands = new CommandBuffer { name = "Rock Cull" };
            // A quarter of the cores, as for grass: more crowds the main and render threads.
            maxScattersInFlight = Mathf.Max(2, SystemInfo.processorCount / 4);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RockFieldProfile.Edited += OnProfileEdited;
            dirty = true;
        }

        private void OnDisable()
        {
            Enabled.Remove(this);
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RockFieldProfile.Edited -= OnProfileEdited;
            Release();
            commands.Release();
            commands = null;
        }

        private void OnValidate() => dirty = true;

        /// <summary>Re-scatter everything, e.g. after a terrain was changed from code.</summary>
        public void Regenerate() => dirty = true;

        private void OnProfileEdited(RockFieldProfile profile)
        {
            foreach (Biome biome in biomes)
                if (biome.profile == profile)
                    dirty = true;
        }

        private void Update()
        {
            if (!Application.isPlaying || streams.Count == 0 || Time.time < nextColliderSync)
                return;
            nextColliderSync = Time.time + colliderInterval;
            ApplyGroundChanges();
            SessionPlayers.Collect(players);
            // Every player is an eye too, so the host has rocks to collide with around players
            // its own camera is nowhere near.
            int budget = chunkBuildsPerFrame;
            foreach (Transform player in players)
                foreach (RockClassStream stream in streams)
                    if (stream.Collides)
                        stream.Stream(player.position, ground, tick, ref budget);
            colliders.Sync(players, streams, colliderRadius);
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

            int budget = Mathf.Min(chunkBuildsPerFrame, maxScattersInFlight - PendingScatters());
            bool harvested = false;
            // A different class goes first each tick, so a busy one cannot take every budget unit.
            for (int i = 0; i < streams.Count; i++)
                harvested |= streams[(tick + i) % streams.Count].Stream(eye, ground, tick, ref budget);

            float shadowLength = SetCullGlobals(cam, eye);
            foreach (RockClassStream stream in streams)
                stream.Cull(commands, cullShader, kernels, shadowLength);
            context.ExecuteCommandBuffer(commands);
            commands.Clear();

            foreach (RockClassStream stream in streams)
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
                Debug.LogError($"[RockField] '{name}' needs RockCull.compute in Cull Shader and a GPU with " +
                               "compute shaders. No rocks are drawn.", this);
                return;
            }

            kernels = new RockClassStream.CullKernels(cullShader);
            var paints = new List<GroundPaint>();
            for (int i = 0; i < biomes.Length; i++)
            {
                Biome biome = biomes[i];
                if (biome.profile == null || biome.paintPrototype == null)
                {
                    Debug.LogError($"[RockField] Biome {i} on '{name}' has no profile or no paint prototype. " +
                                   "It grows nothing.", this);
                    continue;
                }
                int slot = paints.Count;
                paints.Add(GroundPaint.Detail(biome.paintPrototype));
                int patchSeed = unchecked(seed * 397 + slot);
                RockSizeClass[] classes = biome.profile.SizeClasses;
                for (int c = 0; c < classes.Length; c++)
                {
                    RockVariant[] variants = UsableVariants(biome.profile, classes[c]);
                    if (variants.Length > 0)
                        streams.Add(new RockClassStream(biome.profile, classes[c], variants, slot,
                                                        unchecked(patchSeed * 31 + c), patchSeed, minimumHeight));
                }
            }
            ground = new PaintedGround(paints);
            if (Application.isPlaying)
                colliders = new RockColliders(gameObject, colliderLayer);
        }

        /// <summary>The class's variants that can be drawn; the others are reported and left out.</summary>
        private RockVariant[] UsableVariants(RockFieldProfile profile, RockSizeClass sizeClass)
        {
            var usable = new List<RockVariant>();
            foreach (RockVariant variant in sizeClass.Variants)
            {
                if (variant.mesh != null && variant.mesh.subMeshCount == 1 && variant.material != null && variant.weight > 0f)
                    usable.Add(variant);
                else
                    Debug.LogError($"[RockField] A variant of '{sizeClass.Name}' in '{profile.name}' has no mesh, a mesh " +
                                   "with more than one submesh, no material or no weight. It is never placed.", profile);
            }
            return usable.ToArray();
        }

        private void Release()
        {
            foreach (RockClassStream stream in streams)
                stream.Dispose();
            streams.Clear();
            ground?.Dispose();
            ground = null;
            colliders?.Dispose();
            colliders = null;
        }

        private void ApplyGroundChanges()
        {
            changed.Clear();
            ground.TakeChanges(changed);
            foreach (Rect area in changed)
                foreach (RockClassStream stream in streams)
                    stream.Invalidate(area);

            loaded.Clear();
            ground.TakeRepainted(loaded);
            foreach (Terrain terrain in loaded)
                WarnIfUnityDrawsPaint(terrain);
        }

        /// <summary>
        /// The paint prototypes are real detail prototypes, so a terrain with a Detail Distance
        /// draws Unity's own copies of them on top of the rocks.
        /// </summary>
        private void WarnIfUnityDrawsPaint(Terrain terrain)
        {
            if (terrain == null || terrain.detailObjectDistance <= 0f || !warned.Add(terrain))
                return;
            foreach (int index in ground.PaintIndices(terrain))
            {
                if (index < 0)
                    continue;
                Debug.LogWarning($"[RockField] Terrain '{terrain.name}' carries rock paint but its Detail Distance " +
                                 $"is {terrain.detailObjectDistance}, so Unity draws its own copies of the paint " +
                                 "prototypes as well. Set it to 0 (Tools/World/Rocks/Add Rock Paint To Selected Terrains).", terrain);
                return;
            }
        }

        private int PendingScatters()
        {
            int pending = 0;
            foreach (RockClassStream stream in streams)
                pending += stream.PendingCount;
            return pending;
        }

        /// <summary>What every class's cull shares for this camera. Returns the shadow length in radii.</summary>
        private float SetCullGlobals(Camera cam, Vector3 eye)
        {
            commands.SetComputeVectorParam(cullShader, CameraPositionId, eye);
            commands.SetComputeFloatParam(cullShader, ArrivalSecondsId, arrivalSeconds);
            // A rock's height on screen as a share of the screen, per metre of size per metre of
            // distance — the measure LODGroup thresholds use, with the project's LOD bias.
            float screenScale = 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * QualitySettings.lodBias;
            commands.SetComputeFloatParam(cullShader, ScreenScaleId, screenScale);

            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            for (int i = 0; i < frustum.Length; i++)
                frustumPlanes[i] = new Vector4(frustum[i].normal.x, frustum[i].normal.y, frustum[i].normal.z,
                                               frustum[i].distance);
            commands.SetComputeVectorArrayParam(cullShader, FrustumPlanesId, frustumPlanes);

            Light light = sun != null ? sun : RenderSettings.sun;
            if (light == null || light.shadows == LightShadows.None)
            {
                commands.SetComputeVectorParam(cullShader, ShadowDirectionId, Vector4.zero);
                return 0f;
            }
            Vector3 direction = light.transform.forward;
            commands.SetComputeVectorParam(cullShader, ShadowDirectionId, direction);
            // A shadow runs about height / tan(elevation) along the ground; the cull walks it in 3D.
            float elevationSin = Mathf.Max(0.01f, -direction.y);
            return Mathf.Min(MaxShadowLengthInRadii, 2f / elevationSin);
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
