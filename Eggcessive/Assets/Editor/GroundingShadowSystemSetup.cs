using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class GroundingShadowSystemSetup
{
    private const string RenderingFolder = "Assets/Rendering";
    private const string SystemFolder = RenderingFolder + "/GroundingShadows";
    private const string CasterMaterialPath = SystemFolder + "/mat_GroundingShadowCaster.mat";
    private const string CompositeMaterialPath = SystemFolder + "/mat_GroundingShadowComposite.mat";
    private const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";
    private const string MobileRendererPath = "Assets/Settings/Mobile_Renderer.asset";
    private const string EggPrefabPath = "Assets/Eggs/prefabs/prefab_egg_chicken.prefab";
    private const string ChickenPrefabPath = "Assets/Chicken/prefabs/prefab_chicken.prefab";
    private const string CasterRenderingLayerName = "Grounding Shadow Caster";
    private const int CasterRenderingLayerIndex = 8;
    private const uint CasterRenderingLayerMask = 1u << CasterRenderingLayerIndex;

    [MenuItem("Eggcessive/Rendering/Configure Grounding Shadows")]
    public static void Configure()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EnsureFolder(RenderingFolder);
        EnsureFolder(SystemFolder);
        ConfigureRenderingLayerName();

        Material casterMaterial = GetOrCreateMaterial(
            CasterMaterialPath,
            "Hidden/Eggcessive/Grounding Shadow Caster");
        Material compositeMaterial = GetOrCreateMaterial(
            CompositeMaterialPath,
            "Hidden/Eggcessive/Grounding Shadow Composite");

        ConfigureRenderer(PcRendererPath, casterMaterial, compositeMaterial, 512);
        ConfigureRenderer(MobileRendererPath, casterMaterial, compositeMaterial, 256);
        MarkPrefabRenderers(EggPrefabPath);
        MarkPrefabRenderers(ChickenPrefabPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Validate(logSuccess: false);
        Debug.Log("Grounding shadows configured: renderer features, authored materials, and egg/chicken caster masks are ready.");
    }

    [MenuItem("Eggcessive/Rendering/Validate Grounding Shadows")]
    public static void ValidateMenu()
    {
        Validate(logSuccess: true);
    }

    public static void ConfigureForBatch()
    {
        Configure();
    }

    public static void ValidateForBatch()
    {
        Validate(logSuccess: true);
    }

    private static void ConfigureRenderer(
        string rendererPath,
        Material casterMaterial,
        Material compositeMaterial,
        int resolution)
    {
        UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (rendererData == null)
            throw new InvalidOperationException($"Could not load Universal Renderer Data at {rendererPath}.");

        GroundingShadowRendererFeature feature = null;
        for (int i = 0; i < rendererData.rendererFeatures.Count; i++)
        {
            if (rendererData.rendererFeatures[i] is GroundingShadowRendererFeature existing)
            {
                feature = existing;
                break;
            }
        }

        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<GroundingShadowRendererFeature>();
            feature.name = "Grounding Shadows";
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);
        }

        GroundingShadowRendererFeature.Settings settings = feature.FeatureSettings;
        settings.casterMaterial = casterMaterial;
        settings.compositeMaterial = compositeMaterial;
        settings.casterRenderingLayerMask = CasterRenderingLayerMask;
        settings.coverageCenter = new Vector2(0f, -0.5f);
        settings.coverageSize = new Vector2(4.5f, 5.5f);
        settings.minimumHeight = -0.25f;
        settings.maximumHeight = 3f;
        settings.resolution = resolution;
        settings.shadowColor = new Color(0.08f, 0.46f, 0.69f, 1f);
        settings.opacity = 0.82f;
        settings.heightBias = 0.025f;
        settings.projectionRange = 1.25f;
        settings.softness = 9f;
        settings.distanceFalloff = 1.15f;
        settings.minimumUpwardFacing = 0.12f;
        feature.SetActive(true);
        feature.Create();

        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(rendererData);
        rendererData.SetDirty();
        AssetDatabase.SaveAssetIfDirty(rendererData);
        SynchronizeRendererFeatureMap(rendererData, feature);
    }

    private static void SynchronizeRendererFeatureMap(
        UniversalRendererData rendererData,
        GroundingShadowRendererFeature feature)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
        SerializedObject serializedRenderer = new SerializedObject(rendererData);
        SerializedProperty featureList = serializedRenderer.FindProperty("m_RendererFeatures");
        SerializedProperty featureMap = serializedRenderer.FindProperty("m_RendererFeatureMap");
        int featureIndex = rendererData.rendererFeatures.IndexOf(feature);

        while (featureMap.arraySize < featureList.arraySize)
            featureMap.InsertArrayElementAtIndex(featureMap.arraySize);

        while (featureMap.arraySize > featureList.arraySize)
            featureMap.DeleteArrayElementAtIndex(featureMap.arraySize - 1);

        if (featureIndex >= 0 && featureIndex < featureMap.arraySize)
            featureMap.GetArrayElementAtIndex(featureIndex).longValue = localId;

        serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rendererData);
    }

    private static void MarkPrefabRenderers(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException($"Prefab has no renderers: {prefabPath}");

            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].renderingLayerMask |= CasterRenderingLayerMask;
                EditorUtility.SetDirty(renderers[i]);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Material GetOrCreateMaterial(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
            throw new InvalidOperationException($"Required shader was not found: {shaderName}");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader)
            {
                name = System.IO.Path.GetFileNameWithoutExtension(path)
            };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
        }

        return material;
    }

    private static void ConfigureRenderingLayerName()
    {
        UnityEngine.Object[] tagManagerAssets =
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagerAssets.Length == 0)
            throw new InvalidOperationException("Could not load ProjectSettings/TagManager.asset.");

        SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
        SerializedProperty renderingLayers = tagManager.FindProperty("m_RenderingLayers");
        if (renderingLayers == null)
            throw new InvalidOperationException("The project does not expose rendering layers.");

        if (renderingLayers.arraySize <= CasterRenderingLayerIndex)
            renderingLayers.arraySize = CasterRenderingLayerIndex + 1;

        renderingLayers.GetArrayElementAtIndex(CasterRenderingLayerIndex).stringValue =
            CasterRenderingLayerName;
        tagManager.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            throw new InvalidOperationException($"Invalid asset folder path: {path}");

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void Validate(bool logSuccess)
    {
        ValidateRenderer(PcRendererPath, 512);
        ValidateRenderer(MobileRendererPath, 256);
        ValidatePrefab(EggPrefabPath);
        ValidatePrefab(ChickenPrefabPath);

        Material casterMaterial = AssetDatabase.LoadAssetAtPath<Material>(CasterMaterialPath);
        Material compositeMaterial = AssetDatabase.LoadAssetAtPath<Material>(CompositeMaterialPath);
        if (casterMaterial == null || casterMaterial.shader == null ||
            casterMaterial.shader.name != "Hidden/Eggcessive/Grounding Shadow Caster")
            throw new InvalidOperationException("Grounding-shadow caster material is missing or misconfigured.");
        if (compositeMaterial == null || compositeMaterial.shader == null ||
            compositeMaterial.shader.name != "Hidden/Eggcessive/Grounding Shadow Composite")
            throw new InvalidOperationException("Grounding-shadow composite material is missing or misconfigured.");

        if (logSuccess)
            Debug.Log("Grounding shadow validation passed.");
    }

    private static void ValidateRenderer(string rendererPath, int expectedResolution)
    {
        UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (rendererData == null)
            throw new InvalidOperationException($"Renderer data is missing: {rendererPath}");

        for (int i = 0; i < rendererData.rendererFeatures.Count; i++)
        {
            if (!(rendererData.rendererFeatures[i] is GroundingShadowRendererFeature feature))
                continue;

            GroundingShadowRendererFeature.Settings settings = feature.FeatureSettings;
            if (!feature.isActive ||
                settings.casterMaterial == null ||
                settings.compositeMaterial == null ||
                settings.casterRenderingLayerMask != CasterRenderingLayerMask ||
                settings.resolution != expectedResolution)
                throw new InvalidOperationException($"Grounding shadow feature is misconfigured: {rendererPath}");
            return;
        }

        throw new InvalidOperationException($"Grounding shadow feature is missing: {rendererPath}");
    }

    private static void ValidatePrefab(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new InvalidOperationException($"Prefab is missing: {prefabPath}");

        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException($"Prefab has no renderers: {prefabPath}");

        for (int i = 0; i < renderers.Length; i++)
        {
            if ((renderers[i].renderingLayerMask & CasterRenderingLayerMask) == 0)
                throw new InvalidOperationException(
                    $"Renderer {renderers[i].name} is not marked as a grounding-shadow caster in {prefabPath}.");
        }
    }
}
