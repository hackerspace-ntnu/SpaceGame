using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Posterises the finished frame: every pixel snaps to its nearest neighbour in a
    /// small pastel palette, giving flat colour fields with clean edges. Runs after
    /// post-processing so it matches against tonemapped LDR colour, and matches in
    /// Oklab so "nearest" follows perceived colour rather than RGB distance.
    /// </summary>
    public class PastelQuantizeRenderFeature : ScriptableRendererFeature
    {
        private const int MaxPaletteSize = 128;

        [System.Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            public Material material;

            [Tooltip("0 = untouched frame, 1 = fully quantized.")]
            [Range(0f, 1f)] public float blend = 1f;

            [Tooltip("Ordered dither on lightness before matching; hides banding in slow gradients at the cost of flatness.")]
            [Range(0f, 0.2f)] public float ditherStrength = 0f;

            [Tooltip("Every pixel snaps to the nearest of these (sRGB). 128 max; extras are ignored.")]
            public Color[] palette = PastelPalette.Default();
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

            if (settings.material == null || settings.palette == null || settings.palette.Length == 0)
            {
                return;
            }

            renderer.EnqueuePass(pass);
        }

        private class PastelQuantizePass : ScriptableRenderPass
        {
            private const string k_PassName = "PastelQuantize";

            private readonly Settings settings;
            private readonly Vector4[] paletteLinear = new Vector4[MaxPaletteSize];
            private readonly Vector4[] paletteOklab = new Vector4[MaxPaletteSize];
            private Color[] uploadedPalette;

            public PastelQuantizePass(Settings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;
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
                RebuildPaletteIfChanged();

                material.SetVectorArray("_PaletteLinear", paletteLinear);
                material.SetVectorArray("_PaletteOklab", paletteOklab);
                material.SetInteger("_PaletteCount", Mathf.Min(settings.palette.Length, MaxPaletteSize));
                material.SetFloat("_Blend", settings.blend);
                material.SetFloat("_DitherStrength", settings.ditherStrength);

                var destDesc = renderGraph.GetTextureDesc(source);
                destDesc.name = "_PastelQuantizeTemp";
                destDesc.clearBuffer = false;
                destDesc.depthBufferBits = 0;
                TextureHandle destination = renderGraph.CreateTexture(destDesc);

                var blitParams = new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0);
                renderGraph.AddBlitPass(blitParams, passName: k_PassName);

                resourceData.cameraColor = destination;
            }

            // Inspector edits reach here because URP calls Create() on validate, which
            // builds a fresh pass; the reference check only skips the per-frame rebuild.
            private void RebuildPaletteIfChanged()
            {
                if (ReferenceEquals(uploadedPalette, settings.palette))
                {
                    return;
                }

                int count = Mathf.Min(settings.palette.Length, MaxPaletteSize);
                for (int i = 0; i < count; i++)
                {
                    Color linear = settings.palette[i].linear;
                    paletteLinear[i] = linear;
                    paletteOklab[i] = PastelPalette.LinearToOklab(linear);
                }

                uploadedPalette = settings.palette;
            }
        }
    }
}
