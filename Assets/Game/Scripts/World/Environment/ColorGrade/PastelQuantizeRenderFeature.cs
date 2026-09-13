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
    /// Watercolour and pen, in that order. Two things that are not colour run immediately
    /// before the snap, both by moving the colour in Oklab so that what they draw still
    /// lands on a palette entry: <see cref="InkShape"/> draws lines along lightness and
    /// depth edges, and <see cref="NoiseShape"/> adds paper tooth, dither or spatter when it
    /// is asked to. They belong in this pass rather than in passes of their own precisely
    /// because the snap is what would otherwise erase them — anything composited after it is
    /// off-palette by construction. The palette itself comes from
    /// <see cref="PastelPalette"/> rather than a serialized array so the PC and mobile
    /// renderers cannot drift into showing different looks.
    /// </para>
    /// </summary>
    public class PastelQuantizeRenderFeature : ScriptableRendererFeature
    {
        /// <summary>
        /// Must equal MAX_PALETTE in PastelQuantize.shader. Public because a
        /// <see cref="PaletteShape"/> has to be validated against it before it is pushed
        /// — a shape that overruns this would otherwise build a palette whose tail is
        /// silently ignored. A material's vector-array size freezes the first time it is
        /// set, so the upload is always padded to the full length and <c>_PaletteCount</c>
        /// carries the real count; upload fewer and the size is locked short for the
        /// material's lifetime.
        /// </summary>
        public const int MaxPaletteSize = 256;

        [System.Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            public Material material;

            [Tooltip("0 = the frame with grain but no snap, 1 = fully quantized.")]
            [Range(0f, 1f)] public float blend = 1f;

            /// <summary>
            /// The pen lines drawn along edges, before the snap. Serialized, unlike
            /// <see cref="paletteShape"/>: the palette is kept off the renderer assets so
            /// PC and mobile cannot drift into different colours, but an amount of an
            /// effect has to be dragged while the game runs to be judged at all, exactly as
            /// <c>blend</c> above it is. The cost is that these are now two values —
            /// <b>set them on both renderers</b>.
            /// </summary>
            public InkShape ink = InkShape.Default;

            /// <summary>Optional noise before the snap. <see cref="NoiseKind.None"/> by
            /// default: it is a dial to reach for, not part of the committed look.</summary>
            public NoiseShape noise = NoiseShape.Default;

            /// <summary>
            /// The lattice the palette is built from. <see cref="System.NonSerialized"/>
            /// on purpose: keeping it off the renderer assets is what stops the PC and
            /// Mobile renderers drifting into different palettes, and what keeps the
            /// Look Lab an exploration tool rather than a second place a look can ship
            /// from. Only a code edit and the editor-only live bridge write it, and a
            /// domain reload restores this default.
            /// </summary>
            [System.NonSerialized] public PaletteShape paletteShape = PaletteShape.Default;
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

            pass.EnsurePalette();
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
            private static readonly int InkAmountId = Shader.PropertyToID("_InkAmount");
            private static readonly int InkLumaThresholdId =
                Shader.PropertyToID("_InkLumaThreshold");
            private static readonly int InkDepthThresholdId =
                Shader.PropertyToID("_InkDepthThreshold");
            private static readonly int InkSoftnessId = Shader.PropertyToID("_InkSoftness");
            private static readonly int InkWidthId = Shader.PropertyToID("_InkWidth");
            private static readonly int InkTintId = Shader.PropertyToID("_InkTint");
            private static readonly int InkColorId = Shader.PropertyToID("_InkColor");
            private static readonly int NoiseKindId = Shader.PropertyToID("_NoiseKind");
            private static readonly int NoiseAmountId = Shader.PropertyToID("_NoiseAmount");
            private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
            private static readonly int NoiseDensityId = Shader.PropertyToID("_NoiseDensity");
            private readonly Settings settings;
            private readonly Vector4[] paletteLinear = new Vector4[MaxPaletteSize];
            private readonly Vector4[] paletteOklab = new Vector4[MaxPaletteSize];
            private int paletteCount;
            private PaletteShape builtShape;
            private bool built;

            public PastelQuantizePass(Settings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;

                // The pass reads the camera colour and writes a replacement for it, so it
                // cannot run against the back buffer: that handle carries no descriptor to
                // size the destination from. Declaring the requirement makes URP keep an
                // intermediate colour texture alive instead of resolving post-processing
                // straight to the back buffer.
                requiresIntermediateTexture = true;

                // Without this the depth texture is never produced for this camera and
                // SampleSceneDepth returns 0 everywhere, so the ink's silhouettes vanish
                // while its lightness edges keep working — silently, which reads as a
                // tuning problem rather than as a missing input. UseAllGlobalTextures below
                // is the other half: this asks for the texture, that grants a pass this
                // late in the frame access to it.
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            /// <summary>
            /// Rebuilds the uploaded palette when the shape has changed since the last
            /// build, and does nothing at all when it has not.
            ///
            /// <para>
            /// The palette used to be built once in the constructor, which meant a
            /// palette parameter arriving over the Look Lab bridge changed a field that
            /// nothing ever read again — no error, no effect. Scalar parameters like
            /// <c>blend</c> hid the problem because they are pushed to the material every
            /// frame regardless.
            /// </para>
            /// </summary>
            public void EnsurePalette()
            {
                if (built && builtShape.Equals(settings.paletteShape))
                {
                    return;
                }

                PaletteShape shape = settings.paletteShape;
                if (!shape.Validate(MaxPaletteSize, out string error))
                {
                    Debug.LogError($"[PastelQuantize] Cannot build the palette: {error} " +
                                   "Falling back to the committed shape.");
                    shape = PaletteShape.Default;
                    settings.paletteShape = shape;
                }

                Color[] palette = PastelPalette.Build(shape);
                paletteCount = palette.Length;
                for (int i = 0; i < paletteCount; i++)
                {
                    Color linear = palette[i].linear;
                    paletteLinear[i] = linear;
                    paletteOklab[i] = PastelPalette.LinearToOklab(linear);
                }

                builtShape = shape;
                built = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                TextureHandle source = resourceData.activeColorTexture;
                if (!source.IsValid() || resourceData.isActiveTargetBackBuffer)
                {
                    return;
                }

                Material material = settings.material;
                material.SetVectorArray(PaletteLinearId, paletteLinear);
                material.SetVectorArray(PaletteOklabId, paletteOklab);
                material.SetInteger(PaletteCountId, paletteCount);
                material.SetFloat(BlendId, settings.blend);
                material.SetFloat(InkAmountId, settings.ink.amount);
                material.SetFloat(InkLumaThresholdId, settings.ink.lumaThreshold);
                material.SetFloat(InkDepthThresholdId, settings.ink.depthThreshold);
                material.SetFloat(InkSoftnessId, settings.ink.softness);
                material.SetFloat(InkWidthId, settings.ink.width);
                material.SetFloat(InkTintId, settings.ink.tint);
                // Converted here rather than per pixel: the pen is one colour for the whole
                // frame, and the shader works in Oklab throughout.
                material.SetVector(InkColorId,
                    PastelPalette.LinearToOklab(settings.ink.color.linear));
                material.SetFloat(NoiseKindId, (int)settings.noise.kind);
                material.SetFloat(NoiseAmountId, settings.noise.amount);
                material.SetFloat(NoiseScaleId, settings.noise.scale);
                material.SetFloat(NoiseDensityId, settings.noise.density);
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
                    // The ink's silhouettes sample _CameraDepthTexture, which by this late
                    // pass event is a global rather than anything this pass declared. See
                    // ConfigureInput above: both are needed, and missing either loses the
                    // silhouettes with no error.
                    builder.UseAllGlobalTextures(true);
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
