using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Posterises the finished frame: every pixel snaps to its nearest neighbour in the
    /// pastel palette, giving flat colour fields. Runs after post-processing so it
    /// matches against tonemapped LDR colour, and matches in Oklab so "nearest" follows
    /// perceived colour rather than RGB distance.
    ///
    /// <para>
    /// Colour is the whole effect — there are no contours, no grain and no grade. The
    /// palette comes from <see cref="PastelPalette"/> rather than a serialized array so
    /// the PC and mobile renderers cannot drift into showing different looks.
    /// </para>
    /// </summary>
    public class PastelQuantizeRenderFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// Must equal MAX_PALETTE in PastelQuantize.shader. A material's vector-array
        /// size freezes the first time it is set, so the upload is always padded to the
        /// full length and <c>_PaletteCount</c> carries the real count; upload fewer and
        /// the size is locked short for the material's lifetime.
        /// </summary>
        private const int MaxPaletteSize = 256;

        [System.Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            public Material material;

            [Tooltip("0 = untouched frame, 1 = fully quantized.")]
            [Range(0f, 1f)] public float blend = 1f;
        }

        public Settings settings = new Settings();
        private PastelQuantizePass pass;

        public override void Create()
        {
            pass = new PastelQuantizePass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Game cameras only: the scene view and previews stay readable for editing.
            if (renderingData.cameraData.cameraType != CameraType.Game)
            {
                return;
            }

            if (settings.material == null || settings.blend <= 0f)
            {
                return;
            }

            renderer.EnqueuePass(pass);
        }

        private class PastelQuantizePass : ScriptableRenderPass
        {
            private const string k_PassName = "PastelQuantize";
            private static readonly Vector4 ScaleBias = new Vector4(1f, 1f, 0f, 0f);
            private static readonly int PaletteLinearId = Shader.PropertyToID("_PaletteLinear");
            private static readonly int PaletteOklabId = Shader.PropertyToID("_PaletteOklab");
            private static readonly int PaletteCountId = Shader.PropertyToID("_PaletteCount");
            private static readonly int BlendId = Shader.PropertyToID("_Blend");

            private readonly Settings settings;
            private readonly Vector4[] paletteLinear = new Vector4[MaxPaletteSize];
            private readonly Vector4[] paletteOklab = new Vector4[MaxPaletteSize];
            private readonly int paletteCount;

            public PastelQuantizePass(Settings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;

                Color[] palette = PastelPalette.Default();
                if (palette.Length > MaxPaletteSize)
                {
                    Debug.LogError($"[PastelQuantize] PastelPalette has {palette.Length} colours but the " +
                                   $"shader holds {MaxPaletteSize}; the rest are ignored. Raise MAX_PALETTE " +
                                   "in PastelQuantize.shader and MaxPaletteSize here together.");
                }

                paletteCount = Mathf.Min(palette.Length, MaxPaletteSize);
                for (int i = 0; i < paletteCount; i++)
                {
                    Color linear = palette[i].linear;
                    paletteLinear[i] = linear;
                    paletteOklab[i] = PastelPalette.LinearToOklab(linear);
                }
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                TextureHandle source = resourceData.activeColorTexture;
                if (!source.IsValid())
                {
                    return;
                }

                Material material = settings.material;
                material.SetVectorArray(PaletteLinearId, paletteLinear);
                material.SetVectorArray(PaletteOklabId, paletteOklab);
                material.SetInteger(PaletteCountId, paletteCount);
                material.SetFloat(BlendId, settings.blend);

                var destDesc = renderGraph.GetTextureDesc(source);
                destDesc.name = "_PastelQuantizeTemp";
                destDesc.clearBuffer = false;
                destDesc.depthBufferBits = 0;
                TextureHandle destination = renderGraph.CreateTexture(destDesc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(k_PassName, out PassData passData))
                {
                    passData.material = material;
                    passData.source = source;

                    builder.UseTexture(source);
                    builder.SetRenderAttachment(destination, 0);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, data.source, ScaleBias, data.material, 0));
                }

                resourceData.cameraColor = destination;
            }

            private class PassData
            {
                public Material material;
                public TextureHandle source;
            }
        }
    }
}
