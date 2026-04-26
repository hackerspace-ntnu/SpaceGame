using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Re-renders objects on the configured layer (default: "Hologram") at
/// AfterRenderingPostProcessing, so they aren't affected by the screen-space
/// glass distortion / chromatic aberration applied earlier in the pipeline.
///
/// Setup:
/// 1. Add this feature to your URP renderer asset (same one that has
///    GlassDistortionRenderFeature). Edit → Project Settings → Graphics →
///    your URP asset → Renderer → Add Renderer Feature.
/// 2. Make sure the layer named "Hologram" exists. If you use a different
///    name, set it in the feature's Settings.
/// 3. On your Main Camera, remove "Hologram" from the Culling Mask so it
///    doesn't render in the regular transparent pass — only this feature
///    draws it.
///
/// With that done, the hologram is invisible to all post-processing and
/// screen-space passes, so the lens distortion produces no red/blue fringes
/// on its edges, while still affecting the rest of the scene normally.
/// </summary>
public class HologramOverlayRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        public string layerName = "Hologram";
    }

    public Settings settings = new Settings();
    private HologramOverlayPass pass;

    public override void Create()
    {
        pass = new HologramOverlayPass(settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!Application.isPlaying) return;
        if (renderingData.cameraData.cameraType != CameraType.Game) return;
        renderer.EnqueuePass(pass);
    }

    class HologramOverlayPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly List<ShaderTagId> shaderTags = new()
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("LightweightForward"),
        };

        public HologramOverlayPass(Settings s)
        {
            settings = s;
            renderPassEvent = settings.renderPassEvent;
        }

        private class PassData
        {
            public RendererListHandle rendererList;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            int layerIndex = LayerMask.NameToLayer(settings.layerName);
            if (layerIndex < 0) return;
            int layerMask = 1 << layerIndex;

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var universalRenderingData = frameData.Get<UniversalRenderingData>();

            var drawingSettings = RenderingUtils.CreateDrawingSettings(
                shaderTags,
                universalRenderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonTransparent);

            var filteringSettings = new FilteringSettings(RenderQueueRange.all, layerMask);

            var listParams = new RendererListParams(
                universalRenderingData.cullResults,
                drawingSettings,
                filteringSettings);
            var rendererList = renderGraph.CreateRendererList(listParams);

            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                "HologramOverlay", out var passData);

            passData.rendererList = rendererList;
            builder.UseRendererList(rendererList);
            builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
            builder.AllowPassCulling(false);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.rendererList);
            });
        }
    }
}
