using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// One biome of a <see cref="GrassField"/>, drawn GPU-driven out to a long distance:
    ///
    /// <list type="number">
    /// <item>A <see cref="ChunkScatterStream{T}"/> keeps the chunks around the cameras scattered
    /// on worker threads.</item>
    /// <item>The field thins with distance (<see cref="GrassFieldProfile.DensityAt"/>), so a
    /// chunk is stored at the density its distance needs: whole near the camera, a quarter, a
    /// sixteenth or a sixty-fourth farther out. Every cell draws its own numbers, so each level
    /// is an exact subset of the denser ones and nothing pops when a chunk swaps.</item>
    /// <item>Each camera, GrassCull.compute tests every pooled tuft, thins and widens it by
    /// distance, animates the ones it keeps and sorts them into near, mid, far and distant lists,
    /// which four indirect draws draw. The distant tuft is a camera-facing row of flat spikes, cheap
    /// enough to carry the field to the horizon.</item>
    /// </list>
    /// </summary>
    public sealed class GrassBiomeStream : IDisposable
    {
        private const int LodCount = 4;
        private const int ArgsPerDraw = 5;
        private const int VisibleStride = sizeof(float) * 16;

        // Storage levels: each keeps a quarter of the tufts of the one before.
        private const int LevelCount = 4;
        private const float LevelThinning = 4f;

        // A chunk is stored at the density it will need this many chunks closer than it is, so a
        // walking or riding camera finds it already dense instead of watching it fill in.
        private const float LevelLookAheadChunks = 2f;

        // A detail level's list holds every tuft inside its radius, widened by the per-tuft
        // jitter of the switch distance, plus headroom.
        private const float SwitchJitter = 1.1f;
        private const float ListHeadroom = 1.3f;
        private const int MinListCapacity = 64;

        // A zero-length morph or canopy change would be a division by zero.
        private const float MinMorphLength = 0.01f;

        private static readonly int CountsId = Shader.PropertyToID("_Counts");
        private static readonly int ArgsId = Shader.PropertyToID("_Args");
        private static readonly int[] ListIds =
        {
            Shader.PropertyToID("_Near"), Shader.PropertyToID("_Mid"), Shader.PropertyToID("_Far"),
            Shader.PropertyToID("_Distant"),
        };
        private static readonly int[] CapacityIds =
        {
            Shader.PropertyToID("_NearCapacity"), Shader.PropertyToID("_MidCapacity"),
            Shader.PropertyToID("_FarCapacity"), Shader.PropertyToID("_DistantCapacity"),
        };
        private static readonly int MidDistanceId = Shader.PropertyToID("_MidDistance");
        private static readonly int FarDistanceId = Shader.PropertyToID("_FarDistance");
        private static readonly int DistantDistanceId = Shader.PropertyToID("_DistantDistance");
        private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
        private static readonly int DrawDistanceId = Shader.PropertyToID("_DrawDistance");
        private static readonly int FullDensityDistanceId = Shader.PropertyToID("_FullDensityDistance");
        private static readonly int DensityFalloffId = Shader.PropertyToID("_DensityFalloff");
        private static readonly int ThinningFadeId = Shader.PropertyToID("_ThinningFade");
        private static readonly int LodMorphId = Shader.PropertyToID("_LodMorph");
        private static readonly int CanopyId = Shader.PropertyToID("_GrassCanopy");
        private static readonly int MaxWidthBoostId = Shader.PropertyToID("_MaxWidthBoost");
        private static readonly int VisibleId = Shader.PropertyToID("_GrassVisible");

        // The biome's material settings the compute pass animates with.
        private static readonly int[] MaterialFloatIds =
        {
            Shader.PropertyToID("_WindStrength"), Shader.PropertyToID("_WaveLength"),
            Shader.PropertyToID("_WaveSpeed"), Shader.PropertyToID("_WindFrequency"),
            Shader.PropertyToID("_WindSpeed"), Shader.PropertyToID("_FlutterStrength"),
            Shader.PropertyToID("_FlutterFrequency"), Shader.PropertyToID("_BendStrength"), Shader.PropertyToID("_PatchScale"),
            Shader.PropertyToID("_VariationScale"), Shader.PropertyToID("_TrampleStrength"),
            Shader.PropertyToID("_SurgeStrength"), Shader.PropertyToID("_SurgePeriod"), Shader.PropertyToID("_SurgeLength"),
        };

        /// <summary>The kernels of GrassCull.compute, looked up once per field.</summary>
        public readonly struct CullKernels
        {
            public readonly int Reset;
            public readonly int Cull;
            public readonly int WriteArgs;
            public readonly int Trample;

            public CullKernels(ComputeShader shader)
            {
                Reset = shader.FindKernel("Reset");
                Cull = shader.FindKernel("Cull");
                WriteArgs = shader.FindKernel("WriteArgs");
                Trample = shader.FindKernel("Trample");
            }
        }

        private readonly GrassFieldProfile profile;
        private readonly GrassScatter.Settings settings;
        private readonly Mesh[] tufts;
        private readonly ChunkScatterStream<GrassInstance> chunks;
        private readonly MaterialPropertyBlock[] lodProperties = new MaterialPropertyBlock[LodCount];
        private readonly GraphicsBuffer[] lists = new GraphicsBuffer[LodCount];
        private readonly int[] listCapacity = new int[LodCount];

        private GraphicsBuffer counts;
        private GraphicsBuffer args;

        /// <param name="slot">The biome's slot in the field's <see cref="PaintedGround"/>.</param>
        /// <param name="minimumHeight">World height below which nothing grows, unless the profile grows under water.</param>
        /// <param name="densityScale">The field's scale on the profile's tufts per square metre.</param>
        /// <param name="contactClearing">The fraction of a plant's base kept clear of grass.</param>
        /// <param name="contactReach">How far beyond a plant's base, in metres, it shades and shelters the grass.</param>
        public GrassBiomeStream(GrassFieldProfile profile, int slot, int fieldSeed, float minimumHeight,
                                float densityScale, float contactClearing, float contactReach)
        {
            this.profile = profile;
            settings = new GrassScatter.Settings(profile, densityScale, contactClearing, contactReach);
            int seed = unchecked(fieldSeed * 397 + slot);
            tufts = new[]
            {
                GrassTuftMesh.BuildNear(profile), GrassTuftMesh.BuildMid(profile), GrassTuftMesh.BuildFar(profile),
                GrassTuftMesh.BuildDistant(profile),
            };
            float lookAhead = LevelLookAheadChunks * settings.ChunkSize;
            var capacities = new int[LevelCount];
            for (int level = 0; level < LevelCount; level++)
                capacities[level] = GrassScatter.CapacityFor(settings, LevelDensity(level));
            GrassScatter.Settings copy = settings;
            float lowest = profile.GrowsUnderwater ? float.NegativeInfinity : minimumHeight;
            chunks = new ChunkScatterStream<GrassInstance>(slot, settings.ChunkSize, profile.DrawDistance, profile.EdgeWarp,
                capacities, distance => LevelFor(profile.DensityAt(Mathf.Max(0f, distance - lookAhead))),
                (coord, level, ground, capacity) =>
                    GrassScatter.Build(coord, copy, ground, lowest, seed, LevelDensity(level), capacity));
        }

        /// <summary>Chunks still being scattered on worker threads.</summary>
        public int PendingCount => chunks.PendingCount;

        /// <summary>Keeps the chunks around <paramref name="eye"/> scattered. True when a finished one was taken in.</summary>
        public bool Stream(Vector3 eye, PaintedGround ground, int tick, ref int budget) =>
            chunks.Stream(eye, ground, tick, ref budget);

        /// <summary>Records this biome's cull for one camera. False when it has nothing to draw.</summary>
        public bool Cull(CommandBuffer commands, ComputeShader shader, CullKernels kernels)
        {
            if (!chunks.HasResidents)
                return false;
            EnsureLists();

            commands.SetComputeBufferParam(shader, kernels.Reset, CountsId, counts);
            commands.DispatchCompute(shader, kernels.Reset, 1, 1, 1);

            commands.SetComputeBufferParam(shader, kernels.Cull, CountsId, counts);
            for (int lod = 0; lod < LodCount; lod++)
            {
                commands.SetComputeBufferParam(shader, kernels.Cull, ListIds[lod], lists[lod]);
                commands.SetComputeIntParam(shader, CapacityIds[lod], listCapacity[lod]);
            }
            commands.SetComputeFloatParam(shader, MidDistanceId, profile.MidTuftDistance);
            commands.SetComputeFloatParam(shader, FarDistanceId, profile.FarTuftDistance);
            commands.SetComputeFloatParam(shader, DistantDistanceId, profile.DistantTuftDistance);
            commands.SetComputeFloatParam(shader, FadeStartId, profile.FadeStart);
            commands.SetComputeFloatParam(shader, DrawDistanceId, profile.DrawDistance);
            commands.SetComputeFloatParam(shader, FullDensityDistanceId, profile.FullDensityDistance);
            commands.SetComputeFloatParam(shader, DensityFalloffId, profile.DensityFalloff);
            commands.SetComputeFloatParam(shader, ThinningFadeId, profile.ThinningFade);
            commands.SetComputeFloatParam(shader, MaxWidthBoostId, profile.MaxWidthBoost);
            foreach (int id in MaterialFloatIds)
                commands.SetComputeFloatParam(shader, id, profile.Material.GetFloat(id));
            chunks.CullPools(commands, shader, kernels.Cull);

            commands.SetComputeBufferParam(shader, kernels.WriteArgs, CountsId, counts);
            commands.SetComputeBufferParam(shader, kernels.WriteArgs, ArgsId, args);
            commands.DispatchCompute(shader, kernels.WriteArgs, 1, 1, 1);
            return true;
        }

        /// <summary>Queues the four detail levels for <paramref name="cam"/>; call after <see cref="Cull"/>.</summary>
        public void Draw(Camera cam, Vector3 eye, int layer)
        {
            if (!chunks.HasResidents)
                return;
            // The compute pass does the real culling; these bounds only need to hold the field.
            float extent = 2f * (profile.DrawDistance + profile.HeightRange.y);
            var parameters = new RenderParams(profile.Material)
            {
                camera = cam,
                layer = layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = new Bounds(eye, Vector3.one * extent),
            };
            float canopyLength = Mathf.Max(profile.CanopyColorEnd - profile.CanopyColorStart, MinMorphLength);
            var canopy = new Vector4(profile.CanopyColorStart, 1f / canopyLength, 0f, 0f);
            for (int lod = 0; lod < LodCount; lod++)
            {
                lodProperties[lod].SetVector(LodMorphId, LodMorph(lod));
                lodProperties[lod].SetVector(CanopyId, canopy);
                parameters.matProps = lodProperties[lod];
                Graphics.RenderMeshIndirect(parameters, tufts[lod], args, 1, lod);
            }
        }

        public void ReleaseStale(int tick, int lifetimeTicks) => chunks.ReleaseStale(tick, lifetimeTicks);

        /// <summary>Drops the grass over <paramref name="area"/>, finished or not, so it is scattered again.</summary>
        public void Invalidate(Rect area) => chunks.Invalidate(area);

        public void Dispose()
        {
            chunks.Dispose();
            counts?.Release();
            args?.Release();
            for (int lod = 0; lod < LodCount; lod++)
            {
                lists[lod]?.Release();
                DestroyMesh(tufts[lod]);
            }
        }

        /// <summary>The per-camera lists and draw arguments, sized from the profile's reach.</summary>
        private void EnsureLists()
        {
            if (counts != null)
                return;
            float[] from =
            {
                0f, profile.MidTuftDistance / SwitchJitter, profile.FarTuftDistance / SwitchJitter,
                profile.DistantTuftDistance / SwitchJitter,
            };
            float[] to =
            {
                profile.MidTuftDistance * SwitchJitter, profile.FarTuftDistance * SwitchJitter,
                profile.DistantTuftDistance * SwitchJitter, profile.DrawDistance,
            };
            var drawArgs = new uint[LodCount * ArgsPerDraw];
            for (int lod = 0; lod < LodCount; lod++)
            {
                listCapacity[lod] = Mathf.Max(MinListCapacity,
                    Mathf.CeilToInt(TuftsBetween(from[lod], to[lod]) * ListHeadroom));
                lists[lod] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, listCapacity[lod], VisibleStride);
                lodProperties[lod] = new MaterialPropertyBlock();
                lodProperties[lod].SetBuffer(VisibleId, lists[lod]);
                drawArgs[lod * ArgsPerDraw] = tufts[lod].GetIndexCount(0);
            }
            counts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, LodCount, sizeof(uint));
            args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw,
                                      drawArgs.Length, sizeof(uint));
            args.SetData(drawArgs);
        }

        /// <summary>
        /// The most tufts that can survive distance thinning in a ring around the camera, fully
        /// painted and looking every way at once.
        /// </summary>
        private float TuftsBetween(float inner, float outer)
        {
            const int rings = 64;
            float density = settings.MaxTuftsPerChunk / (settings.ChunkSize * settings.ChunkSize);
            outer = Mathf.Min(outer, profile.DrawDistance);
            float step = Mathf.Max(0f, outer - inner) / rings;
            float total = 0f;
            for (int i = 0; i < rings; i++)
            {
                float radius = inner + (i + 0.5f) * step;
                total += 2f * Mathf.PI * radius * step * density * profile.DensityAt(radius);
            }
            return total;
        }

        /// <summary>
        /// <c>_LodMorph</c> for one detail level: where its tufts start morphing into the next
        /// level (in GrassLodRange metres), one over the length of that morph, and whether the next
        /// level is the eye-facing distant row of spikes. The last level never morphs.
        /// </summary>
        private Vector4 LodMorph(int lod)
        {
            if (lod == LodCount - 1)
                return Vector4.zero;
            float end = lod == 0 ? profile.MidTuftDistance
                      : lod == 1 ? profile.FarTuftDistance
                      : profile.DistantTuftDistance;
            float length = Mathf.Max(end * profile.LodMorphBand, MinMorphLength);
            return new Vector4(end - length, 1f / length, lod == LodCount - 2 ? 1f : 0f, 0f);
        }

        private static float LevelDensity(int level) => Mathf.Pow(LevelThinning, -level);

        /// <summary>The sparsest level that still holds <paramref name="density"/> of the tufts.</summary>
        private static int LevelFor(float density)
        {
            int level = 0;
            while (level < LevelCount - 1 && LevelDensity(level + 1) >= density)
                level++;
            return level;
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(mesh);
            else
                UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
