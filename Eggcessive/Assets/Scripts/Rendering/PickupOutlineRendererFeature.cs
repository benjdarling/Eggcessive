using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders the currently hovered pickup into a depth-aware screen mask and
/// composites a clean outer silhouette around that mask.
/// </summary>
[DisallowMultipleRendererFeature]
public sealed class PickupOutlineRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public sealed class Settings
    {
        [Header("Authored Materials")]
        public Material maskMaterial;
        public Material outlineMaterial;

        [Header("Pickup Selection")]
        [Tooltip("Rendering-layer bit temporarily applied to the hovered pickup.")]
        public uint pickupRenderingLayerMask =
            PickupOutlinePreview.PickupRenderingLayerMask;
    }

    [SerializeField]
    private Settings settings = new Settings();

    private PickupOutlinePass pass;

    public override void Create()
    {
        pass = new PickupOutlinePass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents
        };
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        CameraType cameraType = renderingData.cameraData.cameraType;

        if (cameraType != CameraType.Game
            && cameraType != CameraType.SceneView)
        {
            return;
        }

        if (renderingData.cameraData.renderType == CameraRenderType.Overlay
            || !PickupOutlinePreview.HasActiveTarget
            || PickupOutlinePreview.ActiveRenderers.Count == 0
            || settings.maskMaterial == null
            || settings.outlineMaterial == null)
        {
            return;
        }

        pass.Setup(settings);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass = null;
    }

    private sealed class PickupOutlinePass : ScriptableRenderPass
    {
        private const string MaskPassName = "Pickup Outline: Selection Mask";
        private const string CompositePassName = "Pickup Outline: Composite";

        private static readonly int PickupMaskId =
            Shader.PropertyToID("_PickupOutlineMask");
        private readonly ProfilingSampler maskSampler =
            new ProfilingSampler(MaskPassName);
        private readonly ProfilingSampler compositeSampler =
            new ProfilingSampler(CompositePassName);

        private Settings settings;

        private sealed class MaskPassData
        {
            public IReadOnlyList<Renderer> renderers;
            public Material material;
        }

        private sealed class CompositePassData
        {
            public TextureHandle source;
            public TextureHandle mask;
            public Material material;
        }

        public PickupOutlinePass()
        {
            requiresIntermediateTexture = true;
        }

        public void Setup(Settings featureSettings)
        {
            settings = featureSettings;
        }

        public override void RecordRenderGraph(
            RenderGraph renderGraph,
            ContextContainer frameData)
        {
            if (settings == null)
            {
                return;
            }

            UniversalResourceData resourceData =
                frameData.Get<UniversalResourceData>();

            if (resourceData.isActiveTargetBackBuffer)
            {
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;

            if (!source.IsValid())
            {
                return;
            }

            TextureDesc maskDesc = renderGraph.GetTextureDesc(source);
            maskDesc.colorFormat = GraphicsFormat.R8_UNorm;
            maskDesc.depthBufferBits = DepthBits.None;
            maskDesc.msaaSamples = MSAASamples.None;
            maskDesc.clearBuffer = true;
            maskDesc.clearColor = Color.clear;
            maskDesc.filterMode = FilterMode.Bilinear;
            maskDesc.wrapMode = TextureWrapMode.Clamp;
            maskDesc.name = "_PickupOutlineMask";
            TextureHandle pickupMask = renderGraph.CreateTexture(maskDesc);

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass<MaskPassData>(
                       MaskPassName,
                       out MaskPassData passData,
                       maskSampler))
            {
                passData.renderers =
                    PickupOutlinePreview.ActiveRenderers;
                passData.material = settings.maskMaterial;

                builder.SetRenderAttachment(
                    pickupMask,
                    0,
                    AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (
                    MaskPassData data,
                    RasterGraphContext context) =>
                {
                    for (int rendererIndex = 0;
                         rendererIndex < data.renderers.Count;
                         rendererIndex++)
                    {
                        Renderer targetRenderer =
                            data.renderers[rendererIndex];

                        if (targetRenderer == null
                            || !targetRenderer.enabled
                            || targetRenderer.forceRenderingOff)
                        {
                            continue;
                        }

                        int submeshCount = Mathf.Max(
                            1,
                            targetRenderer.sharedMaterials.Length);

                        for (int submesh = 0;
                             submesh < submeshCount;
                             submesh++)
                        {
                            context.cmd.DrawRenderer(
                                targetRenderer,
                                data.material,
                                submesh,
                                0);
                        }
                    }
                });
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.clearBuffer = false;
            destinationDesc.name = "_CameraColorAfterPickupOutline";
            TextureHandle destination =
                renderGraph.CreateTexture(destinationDesc);

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass<CompositePassData>(
                       CompositePassName,
                       out CompositePassData passData,
                       compositeSampler))
            {
                passData.source = source;
                passData.mask = pickupMask;
                passData.material = settings.outlineMaterial;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(pickupMask, AccessFlags.Read);
                builder.SetRenderAttachment(
                    destination,
                    0,
                    AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (
                    CompositePassData data,
                    RasterGraphContext context) =>
                {
                    data.material.SetTexture(PickupMaskId, data.mask);
                    Blitter.BlitTexture(
                        context.cmd,
                        data.source,
                        new Vector4(1f, 1f, 0f, 0f),
                        data.material,
                        0);
                });
            }

            resourceData.cameraColor = destination;
        }
    }
}
