using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Draws the view from under the water: a haze that thickens with distance and darkens
    /// with depth, caustic light drifting over everything below the surface, the surface itself
    /// seen from beneath (Snell's window onto the world above, ringed by the depths mirrored in
    /// it), and a slow wobble over the whole picture.
    ///
    /// The view takes on the water it is under: the haze, its density, the scene tint, the
    /// caustics and the surface from below all come from that water's SpaceGame/StylizedWater
    /// material, so muddy water is murky brown below and a lagoon is clear and bright, and the
    /// surface from below ripples exactly as it does from above. A volume
    /// without such a material keeps the look authored on <see cref="Settings.material"/>.
    ///
    /// One fullscreen pass, and only for a camera that is actually inside an
    /// <see cref="UnderwaterVolume"/> — above water it costs nothing. It runs before
    /// post-processing so bloom and the grade treat the haze like the rest of the frame.
    /// </summary>
    public class UnderwaterRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

            [Tooltip("A material using Hidden/UnderwaterView: the wobble, caustic scale and fades, and " +
                     "the colours used under water that has no StylizedWater material.")]
            public Material material;

            [Tooltip("The haze looking down is the water's deep colour times this: below, the deep " +
                     "reads darker than it does from above.")]
            [Range(0f, 1f)] public float deepHazeBrightness = 0.65f;

            [Tooltip("Haze density is this divided by the water's clarity (metres), so murky water " +
                     "closes in within a few metres and clear water lets you see across.")]
            [Min(0f)] public float hazeDensityTimesClarity = 0.135f;
        }

        public Settings settings = new Settings();
        private UnderwaterPass pass;

        public override void Create()
        {
            pass = new UnderwaterPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView)
                return;
            if (settings.material == null)
                return;

            UnderwaterVolume volume = UnderwaterVolume.Find(renderingData.cameraData.camera.transform.position);
            if (volume == null)
                return;

            pass.MatchWater(volume);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
        }

        private class UnderwaterPass : ScriptableRenderPass
        {
            private const string k_PassName = "Underwater";
            private static readonly Vector4 ScaleBias = new Vector4(1f, 1f, 0f, 0f);
            private static readonly int SurfaceHeightId = Shader.PropertyToID("_WaterSurfaceHeight");

            // The water surface's properties, and the underwater view's they become.
            private static readonly int ShallowColorId = Shader.PropertyToID("_ShallowColor");
            private static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
            private static readonly int BedTintId = Shader.PropertyToID("_BedTint");
            private static readonly int ClarityId = Shader.PropertyToID("_Clarity");
            private static readonly int CausticColorId = Shader.PropertyToID("_CausticColor");
            private static readonly int FogColorShallowId = Shader.PropertyToID("_FogColorShallow");
            private static readonly int FogColorDeepId = Shader.PropertyToID("_FogColorDeep");
            private static readonly int FogDensityId = Shader.PropertyToID("_FogDensity");
            private static readonly int SceneTintId = Shader.PropertyToID("_SceneTint");
            private static readonly int CausticsColorId = Shader.PropertyToID("_CausticsColor");
            private static readonly int WaterNoiseId = Shader.PropertyToID("_WaterNoise");

            // Copied as they are, so the surface ripples the same seen from either side.
            private static readonly int[] RippleIds =
            {
                Shader.PropertyToID("_RippleScale"),
                Shader.PropertyToID("_RippleStrength"),
                Shader.PropertyToID("_RippleDrift"),
                Shader.PropertyToID("_RippleChange"),
                Shader.PropertyToID("_RippleFadeDistance"),
            };

            private readonly Settings settings;

            // A private copy, so per-water colours are never written into the shared asset.
            private Material material;

            public UnderwaterPass(Settings settings)
            {
                this.settings = settings;
                renderPassEvent = settings.renderPassEvent;

                // Reads the camera colour and writes a replacement, so it needs an intermediate
                // texture rather than the back buffer — same as the pastel filter.
                requiresIntermediateTexture = true;

                // The haze and caustics are placed from the depth texture; without asking for
                // it SampleSceneDepth returns the far plane everywhere and the whole view
                // turns to solid fog, with no error.
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            /// <summary>Sets this camera's view up for the water it is under.</summary>
            public void MatchWater(UnderwaterVolume volume)
            {
                if (material == null)
                    material = new Material(settings.material) { hideFlags = HideFlags.DontSave };
                material.CopyPropertiesFromMaterial(settings.material);
                material.SetFloat(SurfaceHeightId, volume.SurfaceHeight);

                Material water = volume.SurfaceMaterial;
                if (water == null || !water.HasProperty(ClarityId))
                    return;
                material.SetColor(FogColorShallowId, water.GetColor(ShallowColorId));
                material.SetColor(FogColorDeepId, water.GetColor(DeepColorId) * settings.deepHazeBrightness);
                material.SetFloat(FogDensityId, settings.hazeDensityTimesClarity / Mathf.Max(water.GetFloat(ClarityId), 0.05f));
                material.SetColor(SceneTintId, water.GetColor(BedTintId));
                material.SetColor(CausticsColorId, water.GetColor(CausticColorId));
                material.SetTexture(WaterNoiseId, water.GetTexture(WaterNoiseId));
                foreach (int id in RippleIds)
                    material.SetFloat(id, water.GetFloat(id));
            }

            public void Dispose() => CoreUtils.Destroy(material);

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                TextureHandle source = resourceData.activeColorTexture;
                if (!source.IsValid() || resourceData.isActiveTargetBackBuffer)
                    return;

                var destDesc = renderGraph.GetTextureDesc(source);
                destDesc.name = "_UnderwaterTemp";
                destDesc.clearBuffer = false;
                destDesc.depthBufferBits = 0;
                TextureHandle destination = renderGraph.CreateTexture(destDesc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(k_PassName, out PassData passData))
                {
                    passData.material = material;
                    passData.source = source;

                    builder.UseTexture(source);
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
