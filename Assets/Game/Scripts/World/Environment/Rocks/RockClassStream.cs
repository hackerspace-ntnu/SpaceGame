using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// One size class of one rock profile, drawn GPU-driven:
    ///
    /// <list type="number">
    /// <item>A <see cref="ChunkScatterStream{T}"/> keeps its chunks scattered around the cameras
    /// (and, in play, around every player, for <see cref="RockColliders"/>). One storage level:
    /// rocks thin out with distance by size, not by rank.</item>
    /// <item>Each camera, RockCull.compute tests every pooled rock against the draw distance, its
    /// size on screen and the frustum widened by the shadow it casts, picks its Mesh LOD and
    /// appends it to the segment of its model and LOD.</item>
    /// <item>One indirect draw per model per LOD the model has. A model's LODs are index ranges of
    /// one mesh (importer Mesh LOD), so a draw only changes which indices it starts from.</item>
    /// </list>
    /// </summary>
    public sealed class RockClassStream : IDisposable
    {
        public const int LodCount = 3;
        private const int ArgsPerDraw = 5;
        private const int VisibleStride = sizeof(float) * 12;
        private const int ResetGroupSize = 64;   // [numthreads] of Reset and WriteArgs

        // A segment holds every rock of its model the reach can hold, plus headroom.
        private const float SegmentHeadroom = 1.3f;
        private const int MinSegmentCapacity = 64;

        // Rocks shrink away over this share of the cull size before they are dropped.
        private const float ShrinkBand = 0.6f;

        private static readonly int CountsId = Shader.PropertyToID("_Counts");
        private static readonly int ArgsId = Shader.PropertyToID("_Args");
        private static readonly int SegmentsId = Shader.PropertyToID("_Segments");
        private static readonly int SegmentCountId = Shader.PropertyToID("_SegmentCount");
        private static readonly int VariantLodsId = Shader.PropertyToID("_VariantLods");
        private static readonly int VisibleId = Shader.PropertyToID("_Visible");
        private static readonly int DrawDistanceId = Shader.PropertyToID("_DrawDistance");
        private static readonly int CullSizeId = Shader.PropertyToID("_CullSize");
        private static readonly int Lod1SizeId = Shader.PropertyToID("_Lod1Size");
        private static readonly int Lod2SizeId = Shader.PropertyToID("_Lod2Size");
        private static readonly int ShrinkBandId = Shader.PropertyToID("_ShrinkBand");
        private static readonly int FadeBandId = Shader.PropertyToID("_FadeBand");
        private static readonly int ShadowLengthId = Shader.PropertyToID("_ShadowLength");
        private static readonly int RockVisibleId = Shader.PropertyToID("_RockVisible");
        private static readonly int SegmentOffsetId = Shader.PropertyToID("_SegmentOffset");

        /// <summary>The kernels of RockCull.compute, looked up once per field.</summary>
        public readonly struct CullKernels
        {
            public readonly int Reset;
            public readonly int Cull;
            public readonly int WriteArgs;

            public CullKernels(ComputeShader shader)
            {
                Reset = shader.FindKernel("Reset");
                Cull = shader.FindKernel("Cull");
                WriteArgs = shader.FindKernel("WriteArgs");
            }
        }

        private readonly RockSizeClass sizeClass;
        private readonly RockVariant[] variants;
        private readonly int[] variantLods;
        private readonly ChunkScatterStream<RockInstance> chunks;
        private readonly int segmentCount;
        private readonly int segmentCapacity;
        private readonly MaterialPropertyBlock[] segmentProperties;

        private GraphicsBuffer visible;
        private GraphicsBuffer counts;
        private GraphicsBuffer segments;
        private GraphicsBuffer lodCounts;
        private GraphicsBuffer args;

        /// <param name="variants">The class's usable variants: each has a mesh and a material.</param>
        /// <param name="slot">The profile's slot in the field's <see cref="PaintedGround"/>.</param>
        /// <param name="seed">This class's scatter seed.</param>
        /// <param name="patchSeed">The profile's patch seed, shared by its classes.</param>
        public RockClassStream(RockFieldProfile profile, RockSizeClass sizeClass, RockVariant[] variants, int slot,
                               int seed, int patchSeed, float minimumHeight)
        {
            this.sizeClass = sizeClass;
            this.variants = variants;
            var meshes = new Mesh[variants.Length];
            var weights = new float[variants.Length];
            variantLods = new int[variants.Length];
            for (int i = 0; i < variants.Length; i++)
            {
                meshes[i] = variants[i].mesh;
                weights[i] = variants[i].weight;
                variantLods[i] = Mathf.Clamp(variants[i].mesh.lodCount, 1, LodCount);
            }
            var settings = new RockScatter.Settings(profile, sizeClass, meshes, weights, patchSeed);
            int capacity = ScatterGrid.CapacityFor(settings.MaxRocksPerChunk, settings.Chance);
            chunks = new ChunkScatterStream<RockInstance>(slot, sizeClass.ChunkSize, sizeClass.DrawDistance, profile.EdgeWarp,
                new[] { capacity }, distance => 0,
                (coord, level, ground, slotCapacity) => RockScatter.Build(coord, settings, ground, minimumHeight, seed, slotCapacity));

            segmentCount = variants.Length * LodCount;
            float reach = sizeClass.DrawDistance;
            float rocksInReach = Mathf.PI * reach * reach / (sizeClass.Spacing * sizeClass.Spacing) * sizeClass.Chance;
            segmentCapacity = Mathf.Max(MinSegmentCapacity, Mathf.CeilToInt(rocksInReach * SegmentHeadroom));
            segmentProperties = new MaterialPropertyBlock[segmentCount];
        }

        public bool Collides => sizeClass.Collides;

        /// <summary>Chunks still being scattered on worker threads.</summary>
        public int PendingCount => chunks.PendingCount;

        /// <summary>The mesh a stored rock's <see cref="RockInstance.Variant"/> draws.</summary>
        public Mesh MeshOf(int variant) => variants[variant].mesh;

        /// <summary>Keeps the chunks around <paramref name="eye"/> scattered. True when a finished one was taken in.</summary>
        public bool Stream(Vector3 eye, PaintedGround ground, int tick, ref int budget) =>
            chunks.Stream(eye, ground, tick, ref budget);

        /// <inheritdoc cref="ChunkScatterStream{T}.ChunksNear"/>
        public void ChunksNear(Vector3 point, float radius, List<KeyValuePair<Vector2Int, RockInstance[]>> into) =>
            chunks.ChunksNear(point, radius, into);

        /// <summary>Records this class's cull for one camera; the camera's settings are set by the field. False when it has nothing to draw.</summary>
        public bool Cull(CommandBuffer commands, ComputeShader shader, CullKernels kernels, float shadowLength)
        {
            if (!chunks.HasResidents)
                return false;
            EnsureBuffers();

            int segmentGroups = (segmentCount + ResetGroupSize - 1) / ResetGroupSize;
            commands.SetComputeIntParam(shader, SegmentCountId, segmentCount);
            commands.SetComputeBufferParam(shader, kernels.Reset, CountsId, counts);
            commands.DispatchCompute(shader, kernels.Reset, segmentGroups, 1, 1);

            commands.SetComputeBufferParam(shader, kernels.Cull, CountsId, counts);
            commands.SetComputeBufferParam(shader, kernels.Cull, VisibleId, visible);
            commands.SetComputeBufferParam(shader, kernels.Cull, SegmentsId, segments);
            commands.SetComputeBufferParam(shader, kernels.Cull, VariantLodsId, lodCounts);
            commands.SetComputeFloatParam(shader, DrawDistanceId, sizeClass.DrawDistance);
            commands.SetComputeFloatParam(shader, CullSizeId, sizeClass.CullScreenSize);
            commands.SetComputeFloatParam(shader, Lod1SizeId, sizeClass.Lod1ScreenSize);
            commands.SetComputeFloatParam(shader, Lod2SizeId, sizeClass.Lod2ScreenSize);
            commands.SetComputeFloatParam(shader, ShrinkBandId, ShrinkBand);
            commands.SetComputeFloatParam(shader, FadeBandId, sizeClass.FadeBand);
            commands.SetComputeFloatParam(shader, ShadowLengthId, sizeClass.CastShadows ? shadowLength : 0f);
            chunks.CullPools(commands, shader, kernels.Cull);

            commands.SetComputeBufferParam(shader, kernels.WriteArgs, CountsId, counts);
            commands.SetComputeBufferParam(shader, kernels.WriteArgs, SegmentsId, segments);
            commands.SetComputeBufferParam(shader, kernels.WriteArgs, ArgsId, args);
            commands.DispatchCompute(shader, kernels.WriteArgs, segmentGroups, 1, 1);
            return true;
        }

        /// <summary>Queues one draw per model per LOD for <paramref name="cam"/>; call after <see cref="Cull"/>.</summary>
        public void Draw(Camera cam, Vector3 eye, int layer)
        {
            if (!chunks.HasResidents || args == null)
                return;
            // The compute pass does the real culling; these bounds only need to hold the class.
            float extent = 2f * (sizeClass.DrawDistance + sizeClass.WidthRange.y);
            for (int variant = 0; variant < variants.Length; variant++)
            {
                var parameters = new RenderParams(variants[variant].material)
                {
                    camera = cam,
                    layer = layer,
                    shadowCastingMode = sizeClass.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    worldBounds = new Bounds(eye, Vector3.one * extent),
                };
                for (int lod = 0; lod < variantLods[variant]; lod++)
                {
                    int segment = variant * LodCount + lod;
                    parameters.matProps = segmentProperties[segment];
                    Graphics.RenderMeshIndirect(parameters, variants[variant].mesh, args, 1, segment);
                }
            }
        }

        public void ReleaseStale(int tick, int lifetimeTicks) => chunks.ReleaseStale(tick, lifetimeTicks);

        /// <summary>Drops the rocks over <paramref name="area"/>, finished or not, so they are scattered again.</summary>
        public void Invalidate(Rect area) => chunks.Invalidate(area);

        public void Dispose()
        {
            chunks.Dispose();
            visible?.Release();
            counts?.Release();
            segments?.Release();
            lodCounts?.Release();
            args?.Release();
        }

        /// <summary>The visible list, its segments and the draw arguments, made on first use.</summary>
        private void EnsureBuffers()
        {
            if (args != null)
                return;
            var ranges = new uint[segmentCount * 2];
            var drawArgs = new uint[segmentCount * ArgsPerDraw];
            for (int variant = 0; variant < variants.Length; variant++)
            {
                // Rock meshes have one submesh; its LOD ranges index the whole index buffer.
                Mesh mesh = variants[variant].mesh;
                for (int lod = 0; lod < LodCount; lod++)
                {
                    int segment = variant * LodCount + lod;
                    ranges[segment * 2] = (uint)(segment * segmentCapacity);
                    ranges[segment * 2 + 1] = (uint)segmentCapacity;
                    MeshLodRange range = mesh.GetLod(0, Mathf.Min(lod, variantLods[variant] - 1));
                    drawArgs[segment * ArgsPerDraw] = range.indexCount;
                    drawArgs[segment * ArgsPerDraw + 2] = range.indexStart;
                    drawArgs[segment * ArgsPerDraw + 3] = (uint)mesh.GetSubMesh(0).baseVertex;
                }
            }

            visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, segmentCount * segmentCapacity, VisibleStride);
            counts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, segmentCount, sizeof(uint));
            segments = new GraphicsBuffer(GraphicsBuffer.Target.Structured, segmentCount, sizeof(uint) * 2);
            segments.SetData(ranges);
            lodCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, variants.Length, sizeof(uint));
            lodCounts.SetData(variantLods);
            args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw,
                                      drawArgs.Length, sizeof(uint));
            args.SetData(drawArgs);
            for (int segment = 0; segment < segmentCount; segment++)
            {
                segmentProperties[segment] = new MaterialPropertyBlock();
                segmentProperties[segment].SetBuffer(RockVisibleId, visible);
                segmentProperties[segment].SetInteger(SegmentOffsetId, segment * segmentCapacity);
            }
        }
    }
}
