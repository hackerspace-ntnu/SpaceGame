// Draws every fog volume near the camera, in one march.
//
// Two passes, the same shape as the sandstorm's: the first marches the volumes at a fraction of the
// screen resolution and writes scattered colour plus coverage; the second composites that back at
// full resolution with a depth-aware upsample. Fog has no edges of its own, so it survives being
// computed small better than almost anything else on screen — but the geometry standing in front of
// it does have edges, which is why the upsample is bilateral rather than bilinear.
//
// The pass is not enqueued at all when no volume is within range, which is what keeps fog off the
// frame budget of every scene that does not have any.
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SpaceGame.World.Environment
{
    public enum FogQuality
    {
        /// <summary>Eight steps at quarter resolution. Reads as soft coloured haze; costs little.</summary>
        Low = 0,

        /// <summary>Sixteen steps at half resolution. The tier the look was tuned for.</summary>
        Medium = 1,

        /// <summary>Thirty-two steps at half resolution. Visibly rounder billows.</summary>
        High = 2,

        /// <summary>Forty-eight steps at full resolution. For screenshots, not for play.</summary>
        Ultra = 3,
    }

    public class FogRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("Must use the SpaceGame/VolumetricFog shader.")]
            public Material material;

            [Tooltip("Where the fog lands in the frame. It must be before transparents: fog that " +
                     "runs after them is drawn over every particle and every piece of glass in the " +
                     "scene, so a lamp's own glow ends up in front of the mist it is lighting.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

            [Tooltip("Starting quality. A settings menu can change it at runtime through " +
                     "FogRenderFeature.Quality.")]
            public FogQuality quality = FogQuality.Medium;

            [Tooltip("How far the ray marches, in metres. Also the radius volumes are gathered in, " +
                     "so a volume further away than this is not uploaded at all.")]
            [Min(20f)] public float maxDistance = 600f;
        }

        /// <summary>Live quality tier. Settable from a graphics options menu.</summary>
        public static FogQuality Quality { get; set; } = FogQuality.Medium;

        /// <summary>
        /// Format of the reduced-resolution march target. It MUST have an alpha channel: the march
        /// writes colour in rgb and coverage in alpha, and the composite is nothing but
        /// <c>lerp(scene, fog.rgb, fog.a)</c>. Inheriting the camera colour's format does not give
        /// it one — URP's 32-bit HDR mode is B10G11R11_UFloatPack32, three channels and no alpha —
        /// and the write then goes nowhere, silently, with the composite reading back a = 1 and
        /// painting the whole screen a colour that is black wherever the ray missed every volume.
        /// </summary>
        public static GraphicsFormat FogFormat => GraphicsFormat.R16G16B16A16_SFloat;

        public static float StepsFor(FogQuality quality) => quality switch
        {
            FogQuality.Low => 8f,
            FogQuality.High => 32f,
            FogQuality.Ultra => 48f,
            _ => 16f,
        };

        /// <summary>
        /// Steps of the march toward the sun that gives the billows a light and a dark side.
        ///
        /// <para>
        /// Kept far lower than the view march because it runs once per view sample per volume, so
        /// its cost is multiplied by both. Two steps is enough to tell a lit face from a shadowed
        /// one; the multi-scatter approximation does the rest.
        /// </para>
        /// </summary>
        public static float LightStepsFor(FogQuality quality) => quality switch
        {
            FogQuality.Low => 1f,
            FogQuality.High => 4f,
            FogQuality.Ultra => 6f,
            _ => 2f,
        };

        public static float ResolutionFor(FogQuality quality) => quality switch
        {
            FogQuality.Low => 0.25f,
            FogQuality.Ultra => 1f,
            _ => 0.5f,
        };

        public Settings settings = new Settings();

        private FogPass pass;

        public override void Create()
        {
            Quality = settings.quality;
            pass = new FogPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.material == null)
                return;

            CameraType type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView)
                return;

            // Gathering and uploading happens here rather than in a scene component so that dropping
            // a FogVolume into a scene is the whole setup. It also means the volumes uploaded are
            // the ones nearest THIS camera, which a component updating in LateUpdate could not know.
            Camera camera = renderingData.cameraData.camera;
            if (FogVolumes.Push(camera.transform.position, settings.maxDistance) == 0)
                return;

            pass.renderPassEvent = settings.renderPassEvent;
            renderer.EnqueuePass(pass);
        }

        private class FogPass : ScriptableRenderPass
        {
            private static readonly int StepsId = Shader.PropertyToID("_FogSteps");
            private static readonly int LightStepsId = Shader.PropertyToID("_FogLightSteps");
            private static readonly int MaxDistanceId = Shader.PropertyToID("_FogMaxDistance");
            private static readonly int FogTexId = Shader.PropertyToID("_FogTex");
            private static readonly int TexelSizeId = Shader.PropertyToID("_FogTexelSize");
            private static readonly int DepthTexId = Shader.PropertyToID("_CameraDepthTexture");
            private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

            private const int ShaderPassMarch = 0;
            private const int ShaderPassComposite = 1;

            private readonly Settings settings;

            public FogPass(Settings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;
            }

            private class PassData
            {
                public Material material;
                public TextureHandle source;
                public TextureHandle depth;
                public TextureHandle fog;
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
                    // Without scene depth the march has no idea where the ground is and would draw
                    // fog over everything uniformly. Better to say why than to look broken.
                    Debug.LogWarning("[Fog] No camera depth texture. Enable Depth Texture on the " +
                                     "URP asset or the fog volumes cannot be rendered.");
                    return;
                }

                Material material = settings.material;
                material.SetFloat(StepsId, StepsFor(Quality));
                material.SetFloat(LightStepsId, LightStepsFor(Quality));
                material.SetFloat(MaxDistanceId, settings.maxDistance);

                TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
                float scale = ResolutionFor(Quality);

                int fogWidth = Mathf.Max(1, Mathf.RoundToInt(sourceDesc.width * scale));
                int fogHeight = Mathf.Max(1, Mathf.RoundToInt(sourceDesc.height * scale));

                TextureDesc fogDesc = sourceDesc;
                fogDesc.name = "_VolumetricFog";
                fogDesc.width = fogWidth;
                fogDesc.height = fogHeight;
                fogDesc.depthBufferBits = 0;
                fogDesc.clearBuffer = false;

                // Never inherit the source format here — see FogFormat. The coverage lives in alpha
                // and the camera colour has none.
                fogDesc.format = FogFormat;

                TextureHandle fog = renderGraph.CreateTexture(fogDesc);

                TextureDesc outputDesc = sourceDesc;
                outputDesc.name = "_VolumetricFogComposite";
                outputDesc.depthBufferBits = 0;
                outputDesc.clearBuffer = false;
                TextureHandle output = renderGraph.CreateTexture(outputDesc);

                // The composite's bilateral upsample needs to know where the low-resolution texel
                // centres are, and they are not derivable from the full-resolution size when the
                // rounding above has landed on an odd number.
                material.SetVector(TexelSizeId, new Vector4(1f / fogWidth, 1f / fogHeight,
                                                            fogWidth, fogHeight));

                AddFullscreenPass(renderGraph, "VolumetricFogMarch", material, ShaderPassMarch,
                                  source, depth, fog, TextureHandle.nullHandle);
                AddFullscreenPass(renderGraph, "VolumetricFogComposite", material, ShaderPassComposite,
                                  source, depth, output, fog);

                resources.cameraColor = output;
            }

            // Both passes are the same shape — blit through the material into a target, with one
            // extra texture bound globally — so they are one method rather than two near-copies.
            private static void AddFullscreenPass(RenderGraph renderGraph, string passName, Material material,
                                                  int shaderPass, TextureHandle source, TextureHandle depth,
                                                  TextureHandle target, TextureHandle fog)
            {
                using IRasterRenderGraphBuilder builder =
                    renderGraph.AddRasterRenderPass(passName, out PassData data);

                data.material = material;
                data.source = source;
                data.depth = depth;
                data.fog = fog;
                data.shaderPass = shaderPass;

                builder.UseTexture(source);
                builder.UseTexture(depth);
                if (fog.IsValid())
                    builder.UseTexture(fog);

                builder.SetRenderAttachment(target, 0);

                // The pass writes a texture that is only claimed as the camera colour after the
                // graph is built, so render graph has no way to see that it is needed.
                builder.AllowPassCulling(false);

                // Required before a pass may touch global shader state, which binding the depth and
                // fog textures by name is.
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(DepthTexId, d.depth);
                    if (d.fog.IsValid())
                        context.cmd.SetGlobalTexture(FogTexId, d.fog);

                    Blitter.BlitTexture(context.cmd, d.source, FullScreenScaleBias, d.material, d.shaderPass);
                });
            }
        }
    }
}
