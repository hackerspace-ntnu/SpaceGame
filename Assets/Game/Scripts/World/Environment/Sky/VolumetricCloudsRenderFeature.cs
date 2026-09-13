// Draws the cloud layer, if the scene has one.
//
// Deliberately a separate feature from the fog rather than another pass inside it. The two look
// alike but they are not the same problem: the clouds are one body at a fixed altitude, marched over
// tens of kilometres with long strides and no local lights, while the fog is up to eight volumes an
// arm's length away marched at a quarter of a billow with lamps inside them. Merging them would mean
// one shader carrying both sets of compromises and neither getting the right step length.
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SpaceGame.World
{
    public enum CloudQuality
    {
        /// <summary>Sixteen steps at quarter resolution. Soft and cheap; the shapes still read.</summary>
        Low = 0,

        /// <summary>Thirty-two steps at half resolution. The tier the look was tuned for.</summary>
        Medium = 1,

        /// <summary>Sixty-four steps at half resolution.</summary>
        High = 2,

        /// <summary>Ninety-six steps at full resolution. For screenshots, not for play.</summary>
        Ultra = 3,
    }

    public class VolumetricCloudsRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("Must use the SpaceGame/VolumetricClouds shader.")]
            public Material material;

            [Tooltip("Where the clouds land in the frame. After the skybox, so they are composited " +
                     "over its gradient, and before the fog's own event at BeforeRenderingTransparents, " +
                     "so a fog bank on the ground is drawn over the sky rather than under it.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

            [Tooltip("Starting quality. A settings menu can change it at runtime through " +
                     "VolumetricCloudsRenderFeature.Quality.")]
            public CloudQuality quality = CloudQuality.Medium;

            [Tooltip("How far the ray marches, in metres. A horizon ray crosses an enormous span of " +
                     "the layer, and past this the clouds are a thin haze that the sky colour " +
                     "swallows anyway.")]
            [Min(1000f)] public float maxDistance = 90000f;
        }

        /// <summary>Live quality tier. Settable from a graphics options menu.</summary>
        public static CloudQuality Quality { get; set; } = CloudQuality.Medium;

        /// <summary>
        /// Format of the reduced-resolution march target. Needs alpha for the same reason the fog's
        /// does: the march writes colour in rgb and coverage in alpha, and URP's 32-bit HDR camera
        /// format has no alpha to inherit.
        /// </summary>
        public static GraphicsFormat CloudFormat => GraphicsFormat.R16G16B16A16_SFloat;

        public static float StepsFor(CloudQuality quality) => quality switch
        {
            CloudQuality.Low => 16f,
            CloudQuality.High => 64f,
            CloudQuality.Ultra => 96f,
            _ => 32f,
        };

        public static float LightStepsFor(CloudQuality quality) => quality switch
        {
            CloudQuality.Low => 2f,
            CloudQuality.High => 5f,
            CloudQuality.Ultra => 6f,
            _ => 3f,
        };

        public static float ResolutionFor(CloudQuality quality) => quality switch
        {
            CloudQuality.Low => 0.25f,
            CloudQuality.Ultra => 1f,
            _ => 0.5f,
        };

        public Settings settings = new Settings();

        private CloudPass pass;

        public override void Create()
        {
            Quality = settings.quality;
            pass = new CloudPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.material == null)
                return;

            CameraType type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView)
                return;

            CloudLayer layer = CloudLayer.Active;
            if (layer == null)
                return;

            // In the editor a scene view renders without play mode, so LateUpdate is not guaranteed
            // to have run before the first frame that needs these values.
            layer.Push();

            pass.renderPassEvent = settings.renderPassEvent;
            renderer.EnqueuePass(pass);
        }

        private class CloudPass : ScriptableRenderPass
        {
            private static readonly int StepsId = Shader.PropertyToID("_CloudSteps");
            private static readonly int LightStepsId = Shader.PropertyToID("_CloudLightSteps");
            private static readonly int MaxDistanceId = Shader.PropertyToID("_CloudMaxDistance");
            private static readonly int CloudTexId = Shader.PropertyToID("_CloudTex");
            private static readonly int TexelSizeId = Shader.PropertyToID("_CloudTexelSize");
            private static readonly int DepthTexId = Shader.PropertyToID("_CameraDepthTexture");
            private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

            private const int ShaderPassMarch = 0;
            private const int ShaderPassComposite = 1;

            private readonly Settings settings;

            public CloudPass(Settings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;
            }

            private class PassData
            {
                public Material material;
                public TextureHandle source;
                public TextureHandle depth;
                public TextureHandle clouds;
                public int shaderPass;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                TextureHandle source = resources.activeColorTexture;
                TextureHandle depth = resources.cameraDepthTexture;

                if (!source.IsValid())
                    return;

                if (!depth.IsValid())
                {
                    // Without scene depth the clouds cannot tell sky from a mountain and would be
                    // drawn over the terrain. Better to say why than to look broken.
                    Debug.LogWarning("[Clouds] No camera depth texture. Enable Depth Texture on the " +
                                     "URP asset or the cloud layer cannot be rendered.");
                    return;
                }

                Material material = settings.material;
                material.SetFloat(StepsId, StepsFor(Quality));
                material.SetFloat(LightStepsId, LightStepsFor(Quality));
                material.SetFloat(MaxDistanceId, settings.maxDistance);

                TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
                float scale = ResolutionFor(Quality);

                int cloudWidth = Mathf.Max(1, Mathf.RoundToInt(sourceDesc.width * scale));
                int cloudHeight = Mathf.Max(1, Mathf.RoundToInt(sourceDesc.height * scale));

                TextureDesc cloudDesc = sourceDesc;
                cloudDesc.name = "_VolumetricClouds";
                cloudDesc.width = cloudWidth;
                cloudDesc.height = cloudHeight;
                cloudDesc.depthBufferBits = 0;
                cloudDesc.clearBuffer = false;
                cloudDesc.format = CloudFormat;

                // Both passes need it: the march to jitter once per texel it writes, the composite
                // to find the texel centres it filters between.
                material.SetVector(TexelSizeId, new Vector4(1f / cloudWidth, 1f / cloudHeight,
                                                            cloudWidth, cloudHeight));

                TextureHandle clouds = renderGraph.CreateTexture(cloudDesc);

                TextureDesc outputDesc = sourceDesc;
                outputDesc.name = "_VolumetricCloudsComposite";
                outputDesc.depthBufferBits = 0;
                outputDesc.clearBuffer = false;
                TextureHandle output = renderGraph.CreateTexture(outputDesc);

                AddFullscreenPass(renderGraph, "VolumetricCloudMarch", material, ShaderPassMarch,
                                  source, depth, clouds, TextureHandle.nullHandle);
                AddFullscreenPass(renderGraph, "VolumetricCloudComposite", material, ShaderPassComposite,
                                  source, depth, output, clouds);

                resources.cameraColor = output;
            }

            private static void AddFullscreenPass(RenderGraph renderGraph, string passName, Material material,
                                                  int shaderPass, TextureHandle source, TextureHandle depth,
                                                  TextureHandle target, TextureHandle clouds)
            {
                using IRasterRenderGraphBuilder builder =
                    renderGraph.AddRasterRenderPass(passName, out PassData data);

                data.material = material;
                data.source = source;
                data.depth = depth;
                data.clouds = clouds;
                data.shaderPass = shaderPass;

                builder.UseTexture(source);
                builder.UseTexture(depth);
                if (clouds.IsValid())
                    builder.UseTexture(clouds);

                builder.SetRenderAttachment(target, 0);

                // The pass writes a texture that is only claimed as the camera colour after the
                // graph is built, so render graph has no way to see that it is needed.
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(DepthTexId, d.depth);
                    if (d.clouds.IsValid())
                        context.cmd.SetGlobalTexture(CloudTexId, d.clouds);

                    Blitter.BlitTexture(context.cmd, d.source, FullScreenScaleBias, d.material, d.shaderPass);
                });
            }
        }
    }
}
