// Layer 2: what makes it impossible to see through.
//
// Two passes. The first marches the view ray through the storm's own shape function and writes
// sand colour plus coverage, at a fraction of the screen resolution — fog has no edges, so it
// survives being computed small better than almost anything else on screen. The second composites
// that back over the scene at full resolution and adds the grit: a little screen warp and a little
// grain, both scaled by coverage so they cost nothing in clear air.
//
// The pass is not enqueued at all unless the camera is actually in a storm, which is what keeps
// the storm off the frame budget of every scene that does not have one.
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SpaceGame.World.Weather
{
    public enum SandstormQuality
    {
        /// <summary>Four steps at quarter resolution. Reads as flat coloured fog; costs almost nothing.</summary>
        Low = 0,

        /// <summary>Eight steps at half resolution. The tier the look was tuned for.</summary>
        Medium = 1,

        /// <summary>Sixteen steps at full resolution. Visibly softer billows, several times the cost.</summary>
        High = 2,
    }

    public class SandstormRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("Must use the SpaceGame/Sandstorm shader.")]
            public Material material;

            [Tooltip("Where the fog lands in the frame. It MUST be before transparents: the fog " +
                     "reaches full opacity within the profile's visibility, so running it after " +
                     "them erases the near-detail grit that is supposed to be flying past your " +
                     "face — the storm interior comes out as a flat coloured screen with nothing " +
                     "moving in it.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;

            [Tooltip("Starting quality. A settings menu can change it at runtime through " +
                     "SandstormRenderFeature.Quality.")]
            public SandstormQuality quality = SandstormQuality.Medium;

            [Tooltip("How far the ray marches, in metres. Past this the fog is already opaque in " +
                     "any storm worth the name, so marching further only costs.")]
            [Min(50f)] public float maxDistance = 900f;
        }

        /// <summary>Live quality tier. Settable from a graphics options menu.</summary>
        public static SandstormQuality Quality { get; set; } = SandstormQuality.Medium;

        /// <summary>
        /// March steps for the silhouette shell. Higher than the fullscreen pass's because the
        /// shell is where the storm's shape is read — banding there is visible as terracing across
        /// the front, while inside the storm there is nothing left to band.
        /// </summary>
        public static float WallStepsFor(SandstormQuality quality) => quality switch
        {
            SandstormQuality.Low => 16f,
            SandstormQuality.High => 64f,
            _ => 36f,
        };

        /// <summary>
        /// Format of the half-resolution fog target. It MUST have an alpha channel: the fog pass
        /// writes sand colour in rgb and coverage in alpha, and the composite is nothing but
        /// <c>lerp(scene, fog.rgb, fog.a)</c>. Inheriting the camera colour's format does not give
        /// it one — URP's 32-bit HDR mode is B10G11R11_UFloatPack32, three channels and no alpha —
        /// and the write then goes nowhere, silently, with the composite reading back a = 1 and
        /// painting the whole screen with a colour that is black wherever the ray misses the storm.
        /// </summary>
        public static GraphicsFormat FogFormat => GraphicsFormat.R16G16B16A16_SFloat;

        /// <summary>Steps of the sun march that gives the billows a light and a dark side.</summary>
        public static float LightStepsFor(SandstormQuality quality) => quality switch
        {
            SandstormQuality.Low => 2f,
            SandstormQuality.High => 6f,
            _ => 4f,
        };

        public Settings settings = new Settings();

        private SandstormPass pass;

        public override void Create()
        {
            Quality = settings.quality;
            pass = new SandstormPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.material == null)
                return;

            // Clear air, no work. This is the single most important line in the file for the frame
            // budget: most of the time, in most scenes, there is no storm anywhere near the player.
            if (SandstormVisuals.CameraDensity <= 0.002f)
                return;

            CameraType type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView)
                return;

            pass.renderPassEvent = settings.renderPassEvent;
            renderer.EnqueuePass(pass);
        }

        private class SandstormPass : ScriptableRenderPass
        {
            private static readonly int StepsId = Shader.PropertyToID("_Steps");
            private static readonly int LightStepsId = Shader.PropertyToID("_LightSteps");
            private static readonly int MaxDistanceId = Shader.PropertyToID("_MaxDistance");
            private static readonly int FogTexId = Shader.PropertyToID("_SandstormFogTex");
            private static readonly int DepthTexId = Shader.PropertyToID("_CameraDepthTexture");
            private static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

            private readonly Settings settings;

            public SandstormPass(Settings settings)
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
                    // Without scene depth the march has no idea where the ground is and would fog
                    // the whole screen uniformly. Better to say why than to look broken.
                    Debug.LogWarning("[Sandstorm] No camera depth texture. Enable Depth Texture on " +
                                     "the URP asset or the storm cannot be rendered.");
                    return;
                }

                Material material = settings.material;
                material.SetFloat(StepsId, StepsFor(Quality));
                material.SetFloat(LightStepsId, LightStepsFor(Quality));
                material.SetFloat(MaxDistanceId, settings.maxDistance);

                TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
                float scale = ResolutionFor(Quality);

                TextureDesc fogDesc = sourceDesc;
                fogDesc.name = "_SandstormFog";
                fogDesc.width = Mathf.Max(1, Mathf.RoundToInt(sourceDesc.width * scale));
                fogDesc.height = Mathf.Max(1, Mathf.RoundToInt(sourceDesc.height * scale));
                fogDesc.depthBufferBits = 0;
                fogDesc.clearBuffer = false;

                // Never inherit the source format here — see FogFormat. The coverage lives in
                // alpha and the camera colour has none.
                fogDesc.format = FogFormat;

                TextureHandle fog = renderGraph.CreateTexture(fogDesc);

                TextureDesc outputDesc = sourceDesc;
                outputDesc.name = "_SandstormComposite";
                outputDesc.depthBufferBits = 0;
                outputDesc.clearBuffer = false;
                TextureHandle output = renderGraph.CreateTexture(outputDesc);

                AddFullscreenPass(renderGraph, "SandstormFog", material, ShaderPassFog, source, depth, fog, TextureHandle.nullHandle);
                AddFullscreenPass(renderGraph, "SandstormComposite", material, ShaderPassComposite, source, depth, output, fog);

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

                // Required before a pass may touch global shader state, which binding the depth
                // and fog textures by name is.
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(DepthTexId, d.depth);
                    if (d.fog.IsValid())
                        context.cmd.SetGlobalTexture(FogTexId, d.fog);

                    Blitter.BlitTexture(context.cmd, d.source, FullScreenScaleBias, d.material, d.shaderPass);
                });
            }

            private const int ShaderPassFog = 0;
            private const int ShaderPassComposite = 1;

            private static float StepsFor(SandstormQuality quality) => quality switch
            {
                SandstormQuality.Low => 4f,
                SandstormQuality.High => 16f,
                _ => 8f,
            };

            private static float ResolutionFor(SandstormQuality quality) => quality switch
            {
                SandstormQuality.Low => 0.25f,
                SandstormQuality.High => 1f,
                _ => 0.5f,
            };
        }
    }
}
