using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Produces a compact top-down height map for selected renderers, then uses it
/// to add soft, controllable grounding shadows to opaque scene surfaces.
/// </summary>
[DisallowMultipleRendererFeature]
public sealed class GroundingShadowRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public sealed class Settings
    {
        [Header("Authored Materials")]
        public Material casterMaterial;
        public Material compositeMaterial;

        [Header("Caster Selection")]
        [Tooltip("Rendering-layer mask used only by this feature. It does not affect physics layers.")]
        public uint casterRenderingLayerMask = 1u << 8;

        [Header("Top-down Coverage")]
        [Tooltip("World-space X/Z centre of the grounding-shadow map.")]
        public Vector2 coverageCenter = new Vector2(0f, -0.5f);
        [Tooltip("World-space X/Z size covered by the grounding-shadow map.")]
        public Vector2 coverageSize = new Vector2(4.5f, 5.5f);
        [Tooltip("Lowest receiver height included in the map.")]
        public float minimumHeight = -0.25f;
        [Tooltip("Highest caster height included in the map.")]
        public float maximumHeight = 3f;
        [Range(128, 1024)]
        public int resolution = 512;

        [Header("Appearance")]
        [ColorUsage(false, false)]
        public Color shadowColor = new Color(0.08f, 0.46f, 0.69f, 1f);
        [Range(0f, 1f)]
        public float opacity = 0.82f;
        [Tooltip("World-space separation ignored to prevent surface acne.")]
        [Range(0.001f, 0.15f)]
        public float heightBias = 0.025f;
        [Tooltip("Maximum vertical distance over which a caster can darken a receiver.")]
        [Range(0.1f, 2f)]
        public float projectionRange = 1.25f;
        [Tooltip("Screen-space radius of the separable Gaussian blur, in pixels.")]
        [Range(0f, 24f)]
        public float softness = 9f;
        [Tooltip("Higher values keep the effect concentrated near contact.")]
        [Range(0.5f, 4f)]
        public float distanceFalloff = 1.15f;
        [Tooltip("Reduces projection onto nearly vertical surfaces while allowing upper surfaces to receive it.")]
        [Range(0f, 1f)]
        public float minimumUpwardFacing = 0.12f;
    }

    [SerializeField]
    private Settings settings = new Settings();

    private GroundingShadowPass pass;

    public Settings FeatureSettings => settings;

    public override void Create()
    {
        pass = new GroundingShadowPass();
        pass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            return;

        if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
            return;

        if (settings.casterMaterial == null || settings.compositeMaterial == null)
            return;

        pass.Setup(settings);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        pass = null;
    }

    private sealed class GroundingShadowPass : ScriptableRenderPass
    {
        private const string HeightPassName = "Grounding Shadows: Top-down Height";
        private const string MaskPassName = "Grounding Shadows: Screen Mask";
        private const string BlurHorizontalPassName = "Grounding Shadows: Blur Horizontal";
        private const string BlurVerticalPassName = "Grounding Shadows: Blur Vertical";
        private const string CompositePassName = "Grounding Shadows: Composite";

        private static readonly int HeightMapId = Shader.PropertyToID("_GroundingShadowHeightMap");
        private static readonly int CameraDepthId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int WorldToUvId = Shader.PropertyToID("_GroundingShadowWorldToUV");
        private static readonly int HeightParamsId = Shader.PropertyToID("_GroundingShadowHeightParams");
        private static readonly int AppearanceParamsId = Shader.PropertyToID("_GroundingShadowAppearanceParams");
        private static readonly int ShadowColorId = Shader.PropertyToID("_GroundingShadowColor");
        private static readonly int ShadowMaskId = Shader.PropertyToID("_GroundingShadowMask");
        private static readonly int BlurDirectionId = Shader.PropertyToID("_GroundingShadowBlurDirection");
        private static readonly int OpacityId = Shader.PropertyToID("_GroundingShadowOpacity");

        private static readonly ShaderTagId[] ShaderTags =
        {
            new ShaderTagId("UniversalGBuffer"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("LightweightForward")
        };

        private readonly List<ShaderTagId> shaderTagList = new List<ShaderTagId>(ShaderTags.Length);
        private readonly ProfilingSampler heightSampler = new ProfilingSampler(HeightPassName);
        private readonly ProfilingSampler maskSampler = new ProfilingSampler(MaskPassName);
        private readonly ProfilingSampler blurHorizontalSampler = new ProfilingSampler(BlurHorizontalPassName);
        private readonly ProfilingSampler blurVerticalSampler = new ProfilingSampler(BlurVerticalPassName);
        private readonly ProfilingSampler compositeSampler = new ProfilingSampler(CompositePassName);

        private Settings settings;

        private sealed class HeightPassData
        {
            public RendererListHandle rendererList;
            public Matrix4x4 topDownView;
            public Matrix4x4 topDownProjection;
            public Matrix4x4 cameraView;
            public Matrix4x4 cameraProjection;
        }

        private sealed class MaskPassData
        {
            public TextureHandle heightMap;
            public TextureHandle cameraDepth;
            public Material material;
            public Matrix4x4 worldToUv;
            public Vector4 heightParams;
            public Vector4 appearanceParams;
        }

        private sealed class BlurPassData
        {
            public TextureHandle source;
            public Material material;
            public Vector4 blurDirection;
        }

        private sealed class CompositePassData
        {
            public TextureHandle source;
            public TextureHandle shadowMask;
            public Material material;
            public Color shadowColor;
            public float opacity;
        }

        public GroundingShadowPass()
        {
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);

            for (int i = 0; i < ShaderTags.Length; i++)
                shaderTagList.Add(ShaderTags[i]);
        }

        public void Setup(Settings featureSettings)
        {
            settings = featureSettings;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (settings == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            CullContextData cullContextData = frameData.Get<CullContextData>();

            int mapResolution = Mathf.Clamp(settings.resolution, 128, 1024);
            Vector2 coverage = new Vector2(
                Mathf.Max(0.1f, settings.coverageSize.x),
                Mathf.Max(0.1f, settings.coverageSize.y));
            float minimumHeight = settings.minimumHeight;
            float maximumHeight = Mathf.Max(minimumHeight + 0.1f, settings.maximumHeight);

            BuildTopDownMatrices(
                settings.coverageCenter,
                coverage,
                minimumHeight,
                maximumHeight,
                out Matrix4x4 topDownView,
                out Matrix4x4 topDownProjection,
                out Matrix4x4 worldToUv);

            if (!TryCreateCasterRendererList(
                    renderGraph,
                    frameData,
                    cameraData,
                    renderingData,
                    lightData,
                    cullContextData,
                    topDownView,
                    topDownProjection,
                    out RendererListHandle rendererList))
                return;

            TextureDesc heightDesc = new TextureDesc(mapResolution, mapResolution)
            {
                colorFormat = GraphicsFormat.R16_SFloat,
                clearBuffer = true,
                clearColor = new Color(minimumHeight, minimumHeight, minimumHeight, minimumHeight),
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "_GroundingShadowHeightMap"
            };
            TextureHandle heightMap = renderGraph.CreateTexture(heightDesc);

            TextureDesc depthDesc = new TextureDesc(mapResolution, mapResolution)
            {
                depthBufferBits = DepthBits.Depth16,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "_GroundingShadowDepth"
            };
            TextureHandle topDownDepth = renderGraph.CreateTexture(depthDesc);

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass<HeightPassData>(HeightPassName, out HeightPassData passData, heightSampler))
            {
                passData.rendererList = rendererList;
                passData.topDownView = topDownView;
                passData.topDownProjection = topDownProjection;
                passData.cameraView = cameraData.GetViewMatrix();
                passData.cameraProjection = cameraData.GetProjectionMatrix();

                builder.UseRendererList(rendererList);
                builder.SetRenderAttachment(heightMap, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(topDownDepth, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (HeightPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetViewProjectionMatrices(data.topDownView, data.topDownProjection);
                    context.cmd.DrawRendererList(data.rendererList);
                    context.cmd.SetViewProjectionMatrices(data.cameraView, data.cameraProjection);
                });
            }

            TextureHandle source = resourceData.activeColorTexture;
            TextureHandle cameraDepth = resourceData.cameraDepthTexture;
            if (!source.IsValid() || !cameraDepth.IsValid())
                return;

            TextureDesc maskDesc = renderGraph.GetTextureDesc(source);
            maskDesc.colorFormat = GraphicsFormat.R8_UNorm;
            maskDesc.depthBufferBits = DepthBits.None;
            maskDesc.msaaSamples = MSAASamples.None;
            maskDesc.clearBuffer = false;
            maskDesc.filterMode = FilterMode.Bilinear;

            maskDesc.name = "_GroundingShadowHardMask";
            TextureHandle hardMask = renderGraph.CreateTexture(maskDesc);

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass<MaskPassData>(MaskPassName, out MaskPassData passData, maskSampler))
            {
                passData.heightMap = heightMap;
                passData.cameraDepth = cameraDepth;
                passData.material = settings.compositeMaterial;
                passData.worldToUv = worldToUv;
                passData.heightParams = new Vector4(
                    minimumHeight,
                    maximumHeight,
                    Mathf.Max(0.001f, settings.heightBias),
                    Mathf.Max(0.01f, settings.projectionRange));
                passData.appearanceParams = new Vector4(
                    0f,
                    0f,
                    Mathf.Max(0.1f, settings.distanceFalloff),
                    Mathf.Clamp01(settings.minimumUpwardFacing));

                builder.UseTexture(heightMap, AccessFlags.Read);
                builder.UseTexture(cameraDepth, AccessFlags.Read);
                builder.SetRenderAttachment(hardMask, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
                {
                    data.material.SetTexture(HeightMapId, data.heightMap);
                    data.material.SetTexture(CameraDepthId, data.cameraDepth);
                    context.cmd.SetGlobalMatrix(WorldToUvId, data.worldToUv);
                    context.cmd.SetGlobalVector(HeightParamsId, data.heightParams);
                    context.cmd.SetGlobalVector(AppearanceParamsId, data.appearanceParams);
                    Blitter.BlitTexture(
                        context.cmd,
                        data.cameraDepth,
                        new Vector4(1f, 1f, 0f, 0f),
                        data.material,
                        0);
                });
            }

            maskDesc.name = "_GroundingShadowBlurHorizontal";
            TextureHandle horizontalMask = renderGraph.CreateTexture(maskDesc);
            float blurRadius = Mathf.Max(0f, settings.softness);
            Vector4 horizontalDirection = new Vector4(
                blurRadius / (3.23076923f * Mathf.Max(1, maskDesc.width)),
                0f,
                0f,
                0f);

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass<BlurPassData>(
                       BlurHorizontalPassName,
                       out BlurPassData passData,
                       blurHorizontalSampler))
            {
                passData.source = hardMask;
                passData.material = settings.compositeMaterial;
                passData.blurDirection = horizontalDirection;

                builder.UseTexture(hardMask, AccessFlags.Read);
                builder.SetRenderAttachment(horizontalMask, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (BlurPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalVector(BlurDirectionId, data.blurDirection);
                    Blitter.BlitTexture(
                        context.cmd,
                        data.source,
                        new Vector4(1f, 1f, 0f, 0f),
                        data.material,
                        1);
                });
            }

            maskDesc.name = "_GroundingShadowBlurredMask";
            TextureHandle blurredMask = renderGraph.CreateTexture(maskDesc);
            Vector4 verticalDirection = new Vector4(
                0f,
                blurRadius / (3.23076923f * Mathf.Max(1, maskDesc.height)),
                0f,
                0f);

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass<BlurPassData>(
                       BlurVerticalPassName,
                       out BlurPassData passData,
                       blurVerticalSampler))
            {
                passData.source = horizontalMask;
                passData.material = settings.compositeMaterial;
                passData.blurDirection = verticalDirection;

                builder.UseTexture(horizontalMask, AccessFlags.Read);
                builder.SetRenderAttachment(blurredMask, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (BlurPassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalVector(BlurDirectionId, data.blurDirection);
                    Blitter.BlitTexture(
                        context.cmd,
                        data.source,
                        new Vector4(1f, 1f, 0f, 0f),
                        data.material,
                        1);
                });
            }

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_CameraColorAfterGroundingShadows";
            destinationDesc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass<CompositePassData>(CompositePassName, out CompositePassData passData, compositeSampler))
            {
                passData.source = source;
                passData.shadowMask = blurredMask;
                passData.material = settings.compositeMaterial;
                passData.shadowColor = settings.shadowColor;
                passData.opacity = Mathf.Clamp01(settings.opacity);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(blurredMask, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    data.material.SetTexture(ShadowMaskId, data.shadowMask);
                    context.cmd.SetGlobalColor(ShadowColorId, data.shadowColor);
                    context.cmd.SetGlobalFloat(OpacityId, data.opacity);
                    Blitter.BlitTexture(
                        context.cmd,
                        data.source,
                        new Vector4(1f, 1f, 0f, 0f),
                        data.material,
                        2);
                });
            }

            resourceData.cameraColor = destination;
        }

        private bool TryCreateCasterRendererList(
            RenderGraph renderGraph,
            ContextContainer frameData,
            UniversalCameraData cameraData,
            UniversalRenderingData renderingData,
            UniversalLightData lightData,
            CullContextData cullContextData,
            Matrix4x4 viewMatrix,
            Matrix4x4 projectionMatrix,
            out RendererListHandle rendererList)
        {
            rendererList = default;
            if (!cameraData.camera.TryGetCullingParameters(false, out ScriptableCullingParameters cullingParameters))
                return false;

            cullingParameters.cullingMatrix = projectionMatrix * viewMatrix;
            cullingParameters.origin = viewMatrix.inverse.GetColumn(3);
            cullingParameters.isOrthographic = true;
            cullingParameters.shadowDistance = 0f;
            CullingResults casterCullingResults = cullContextData.Cull(ref cullingParameters);

            FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque, -1)
            {
                renderingLayerMask = settings.casterRenderingLayerMask
            };

            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                shaderTagList,
                renderingData,
                cameraData,
                lightData,
                SortingCriteria.CommonOpaque);
            drawingSettings.overrideMaterial = settings.casterMaterial;
            drawingSettings.overrideMaterialPassIndex = 0;

            RendererListParams rendererListParams =
                new RendererListParams(casterCullingResults, drawingSettings, filteringSettings);
            rendererList = renderGraph.CreateRendererList(rendererListParams);
            return rendererList.IsValid();
        }

        private static void BuildTopDownMatrices(
            Vector2 centre,
            Vector2 size,
            float minimumHeight,
            float maximumHeight,
            out Matrix4x4 viewMatrix,
            out Matrix4x4 projectionMatrix,
            out Matrix4x4 worldToUv)
        {
            const float nearPlane = 0.01f;
            Vector3 cameraPosition = new Vector3(centre.x, maximumHeight + nearPlane, centre.y);
            Quaternion cameraRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            // Unity camera view space looks down negative Z. A transform inverse
            // alone maps points in front of this top-down camera to positive Z,
            // which places every caster behind the projection's near plane.
            Matrix4x4 cameraWorldToLocal =
                Matrix4x4.TRS(cameraPosition, cameraRotation, Vector3.one).inverse;
            viewMatrix = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * cameraWorldToLocal;
            projectionMatrix = Matrix4x4.Ortho(
                -size.x * 0.5f,
                size.x * 0.5f,
                -size.y * 0.5f,
                size.y * 0.5f,
                nearPlane,
                maximumHeight - minimumHeight + nearPlane);

            Matrix4x4 textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = 0.5f;
            textureScaleAndBias.m11 = 0.5f;
            textureScaleAndBias.m03 = 0.5f;
            textureScaleAndBias.m13 = 0.5f;

            // RenderGraph exposes textures in Unity's normalized sampling
            // orientation. Using the render-target GPU projection here applies
            // that Y correction a second time, which mirrors the top-down
            // camera's local Y axis (world Z) during compositing.
            worldToUv = textureScaleAndBias * projectionMatrix * viewMatrix;
        }
    }
}
