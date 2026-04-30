using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class GlassDistortionRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        public Material distortionMaterial = null;
        
        [Header("Lens Settings")]
        [Range(0f, 1f)] public float lensCenterX = 0.5f;
        [Range(0f, 1f)] public float lensCenterY = 0.5f;
        [Range(0f, 1.5f)] public float lensRadius = 0.35f;
        [Range(0f, 1f)] public float distortionStrength = 0.7f;
        [Range(0f, 1f)] public float chromaticAberration = 0.2f;
        [Range(1f, 3f)] public float lensZoom = 1.5f;
    }

    public Settings settings = new Settings();
    private GlassDistortionPass customPass;

    public override void Create()
    {
        customPass = new GlassDistortionPass(settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Don't apply effect in edit mode
        if (!Application.isPlaying)
        {
            return;
        }
        
        if (settings.distortionMaterial == null)
        {
            Debug.LogWarning("GlassDistortionRenderFeature: Material not assigned!");
            return;
        }
        
        renderer.EnqueuePass(customPass);
    }

    class GlassDistortionPass : ScriptableRenderPass
    {
        private Settings settings;
        private Material material;
        private const string k_PassName = "GlassDistortion";

        public GlassDistortionPass(Settings settings)
        {
            this.settings = settings;
            this.renderPassEvent = settings.renderPassEvent;
            this.material = settings.distortionMaterial;
        }

        // New RenderGraph path
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
            {
                Debug.LogError("GlassDistortionPass: Material is null!");
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.activeColorTexture;
            
            if (!source.IsValid())
            {
                Debug.LogError("GlassDistortionPass: Source texture is invalid!");
                return;
            }

            // Update material properties
            material.SetVector("_LensCenter", new Vector2(settings.lensCenterX, settings.lensCenterY));
            material.SetFloat("_LensRadius", settings.lensRadius);
            material.SetFloat("_DistortionStrength", settings.distortionStrength);
            material.SetFloat("_ChromaticAberration", settings.chromaticAberration);
            material.SetFloat("_LensZoom", settings.lensZoom);

            // Get descriptor from source and create destination with same settings
            var sourceDesc = renderGraph.GetTextureDesc(source);
            var destDesc = sourceDesc;
            destDesc.name = "_GlassDistortionTemp";
            destDesc.clearBuffer = false;
            destDesc.depthBufferBits = 0;
            
            TextureHandle destination = renderGraph.CreateTexture(destDesc);

            // The _BlitTexture property is what Blit.hlsl expects, not _MainTex
            // But AddBlitPass should handle this automatically
            var blitParams = new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0);
            renderGraph.AddBlitPass(blitParams, passName: k_PassName);

            // Update resourceData to use our new texture
            resourceData.cameraColor = destination;
        }

        public void Dispose()
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        customPass?.Dispose();
    }
}
