using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[FilePath(
    "ProjectSettings/EggcessiveGroundGrassTextureGenerator.asset",
    FilePathAttribute.Location.ProjectFolder)]
internal sealed class ProceduralGroundGrassTextureSettings
    : ScriptableSingleton<ProceduralGroundGrassTextureSettings>
{
    public bool hasSavedSettings;
    public GameObject grassClumpPrefab;
    public Material bladeMaterial;
    public Material targetMaterial;
    public string outputPath;
    public string heightOutputPath;
    public int resolution;
    public int randomSeed;
    public int settingsVersion;
    public float groundColorVariation;
    public int cloudCells;
    public int cloudOctaves;
    public float cloudContrast;
    public float fineVariation;
    public Vector2 groundHeightRange;
    public int candidateTufts;
    public Vector2 tuftDiameterAt1024;
    public float bladeLayFlat;
    public float clumpBladeCoverage;
    public int looseBladeAttempts;
    public float looseBladeCoverage;
    public float sparseCoverage;
    public float patchConcentration;
    public float tipColorStrength;
    public float bladeColorVariation;
    public Color bladeShadow;
    public float shadowOpacity;
    public Vector2 shadowOffsetAt1024;
    public float shadowBlurAt1024;
    public Vector2 bladeHeightRange;

    public void SaveSettings()
    {
        Save(true);
    }
}

public sealed class ProceduralGroundGrassTextureWindow : EditorWindow
{
    private const string DefaultPrefabPath =
        "Assets/Env/prefabs/prefab_grass_clump.prefab";
    private const string DefaultMaterialPath =
        "Assets/Env/materials/mat_grass.mat";
    private const string DefaultOutputPath =
        "Assets/Env/textures/t_ground_grass_generated.png";
    private const string DefaultHeightOutputPath =
        "Assets/Env/textures/t_ground_grass_generated_height.png";

    private static readonly Vector2[] AntialiasSampleOffsets =
    {
        new Vector2(0.25f, 0.25f),
        new Vector2(0.75f, 0.25f),
        new Vector2(0.25f, 0.75f),
        new Vector2(0.75f, 0.75f)
    };

    [SerializeField] private GameObject grassClumpPrefab;
    [SerializeField] private Material bladeMaterial;
    [SerializeField] private Material targetMaterial;
    [SerializeField] private string outputPath = DefaultOutputPath;
    [SerializeField] private string heightOutputPath = DefaultHeightOutputPath;
    [SerializeField] private int resolution = 1024;
    [SerializeField] private int randomSeed = 91827;
    [SerializeField] private int settingsVersion;

    [SerializeField, Range(0f, 0.75f)] private float groundColorVariation = 0.32f;
    [SerializeField] private int cloudCells = 4;
    [SerializeField] private int cloudOctaves = 5;
    [SerializeField, Range(0f, 1f)] private float cloudContrast = 0.72f;
    [SerializeField, Range(0f, 0.25f)] private float fineVariation = 0.055f;
    [SerializeField] private Vector2 groundHeightRange = new Vector2(0.18f, 0.38f);

    [SerializeField] private int candidateTufts = 1150;
    [SerializeField] private Vector2 tuftDiameterAt1024 = new Vector2(30f, 76f);
    [SerializeField, Range(1f, 4f)] private float bladeLayFlat = 2f;
    [SerializeField, Range(0.05f, 1f)] private float clumpBladeCoverage = 0.68f;
    [SerializeField] private int looseBladeAttempts = 1800;
    [SerializeField, Range(0.02f, 0.5f)] private float looseBladeCoverage = 0.11f;
    [SerializeField, Range(0f, 1f)] private float sparseCoverage = 0.12f;
    [SerializeField, Range(0.1f, 4f)] private float patchConcentration = 1.45f;
    [SerializeField, Range(0f, 1f)] private float tipColorStrength = 0.55f;
    [SerializeField, Range(0f, 0.5f)] private float bladeColorVariation = 0.08f;
    [SerializeField] private Color bladeShadow = new Color(0.045f, 0.12f, 0.025f, 1f);
    [SerializeField, Range(0f, 1f)] private float shadowOpacity = 0.34f;
    [SerializeField] private Vector2 shadowOffsetAt1024 = new Vector2(0.8f, -0.8f);
    [SerializeField, Range(0f, 16f)] private float shadowBlurAt1024 = 4f;
    [SerializeField] private Vector2 bladeHeightRange = new Vector2(0.68f, 1f);

    [SerializeField] private Texture2D previewTexture;
    [SerializeField] private Texture2D previewHeightTexture;
    private Vector2 scrollPosition;

    private struct TuftStamp
    {
        public Vector2 centre;
        public float rotation;
        public float diameter;
        public float brightness;
        public float dryVariation;
        public float bladeKeepProbability;
        public int scatterSeed;
    }

    private sealed class BladeMaterialData : IDisposable
    {
        public readonly Color baseColor;
        public readonly Color tipColor;
        public readonly Color dryColor;
        public readonly float dryBlend;
        public readonly float cutoff;
        public readonly Vector2 alphaScale;
        public readonly Vector2 alphaOffset;
        private readonly Texture2D readableAlpha;

        public BladeMaterialData(Material material)
        {
            baseColor = material.GetColor("_BaseColor");
            tipColor = material.GetColor("_TipColor");
            dryColor = material.HasProperty("_DryColor")
                ? material.GetColor("_DryColor")
                : tipColor;
            dryBlend = material.HasProperty("_DryBlend")
                ? material.GetFloat("_DryBlend")
                : 0f;
            cutoff = material.HasProperty("_Cutoff")
                ? material.GetFloat("_Cutoff")
                : 0.5f;
            alphaScale = material.GetTextureScale("_AlphaMap");
            alphaOffset = material.GetTextureOffset("_AlphaMap");

            Texture alphaTexture = material.GetTexture("_AlphaMap");
            if (alphaTexture != null)
            {
                readableAlpha = CreateReadableCopy(alphaTexture);
            }
        }

        public bool PassesAlphaClip(Vector2 meshUv)
        {
            if (readableAlpha == null)
            {
                return true;
            }

            float u = Mathf.Repeat(meshUv.x * alphaScale.x + alphaOffset.x, 1f);
            float v = Mathf.Repeat(meshUv.y * alphaScale.y + alphaOffset.y, 1f);
            return readableAlpha.GetPixelBilinear(u, v).a >= cutoff;
        }

        public void Dispose()
        {
            if (readableAlpha != null)
            {
                UnityEngine.Object.DestroyImmediate(readableAlpha);
            }
        }

        private static Texture2D CreateReadableCopy(Texture source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                var copy = new Texture2D(
                    source.width,
                    source.height,
                    TextureFormat.RGBA32,
                    false,
                    true)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear
                };
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                copy.Apply(false, false);
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }

    [MenuItem("Eggcessive/Grass/Ground Grass Texture Generator")]
    public static void OpenWindow()
    {
        OpenWindow(null);
    }

    public static void OpenWindow(Material material)
    {
        var window = GetWindow<ProceduralGroundGrassTextureWindow>();
        window.titleContent = new GUIContent("Grass Texture");
        window.minSize = new Vector2(390f, 620f);
        if (material != null)
        {
            window.targetMaterial = material;
        }
        window.LoadDefaults();
        window.SavePersistentSettings();
        window.Show();
    }

    private void OnEnable()
    {
        LoadPersistentSettings();
        if (settingsVersion < 2)
        {
            tuftDiameterAt1024 *= 2f;
            cloudCells = Mathf.Max(1, Mathf.RoundToInt(cloudCells * 0.5f));
            settingsVersion = 2;
        }
        if (settingsVersion < 3)
        {
            groundHeightRange = new Vector2(0.32f, 0.55f);
            settingsVersion = 3;
        }
        LoadDefaults();
        SavePersistentSettings();
    }

    private void OnDisable()
    {
        SavePersistentSettings();
    }

    private void OnLostFocus()
    {
        SavePersistentSettings();
    }

    private void LoadDefaults()
    {
        if (grassClumpPrefab == null)
        {
            grassClumpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPrefabPath);
        }

        if (bladeMaterial == null)
        {
            bladeMaterial = FindBladeMaterial(grassClumpPrefab);
        }

        if (targetMaterial == null)
        {
            targetMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
        }

        if (previewTexture == null && !string.IsNullOrEmpty(outputPath))
        {
            previewTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        }
        if (previewHeightTexture == null && !string.IsNullOrEmpty(heightOutputPath))
        {
            previewHeightTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(heightOutputPath);
        }
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.LabelField("Procedural Ground Grass", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Builds seamless albedo and height textures by projecting the current grass clump "
            + "from above, scattering rotated/scaled copies, and wrapping every tuft "
            + "that crosses a texture edge. It does not generate normals or roughness. "
            + "All generator settings are saved automatically for this project.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        grassClumpPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Grass Clump Prefab",
            grassClumpPrefab,
            typeof(GameObject),
            false);
        if (EditorGUI.EndChangeCheck())
        {
            bladeMaterial = FindBladeMaterial(grassClumpPrefab);
        }
        bladeMaterial = (Material)EditorGUILayout.ObjectField(
            "Blade Material",
            bladeMaterial,
            typeof(Material),
            false);
        targetMaterial = (Material)EditorGUILayout.ObjectField(
            "Target Material",
            targetMaterial,
            typeof(Material),
            false);
        outputPath = EditorGUILayout.TextField("Output PNG", outputPath);
        heightOutputPath = EditorGUILayout.TextField("Height PNG", heightOutputPath);
        resolution = EditorGUILayout.IntPopup(
            "Resolution",
            resolution,
            new[] { "256", "512", "1024", "2048" },
            new[] { 256, 512, 1024, 2048 });
        randomSeed = EditorGUILayout.IntField("Seed", randomSeed);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Cloud Ground", EditorStyles.boldLabel);
        groundColorVariation = EditorGUILayout.Slider(
            "Base Colour Variation",
            groundColorVariation,
            0f,
            0.75f);
        cloudCells = EditorGUILayout.IntSlider("Cloud Frequency", cloudCells, 1, 8);
        cloudOctaves = EditorGUILayout.IntSlider("Patch Detail", cloudOctaves, 1, 7);
        cloudContrast = EditorGUILayout.Slider("Patch Contrast", cloudContrast, 0f, 1f);
        fineVariation = EditorGUILayout.Slider("Fine Variation", fineVariation, 0f, 0.25f);
        groundHeightRange = EditorGUILayout.Vector2Field(
            "Cloud Bed Height Range",
            groundHeightRange);
        EditorGUILayout.HelpBox(
            "Raise the Cloud Bed Height Range to reveal more of the flat grass surface "
            + "earlier. Its width controls how unevenly that surface enters the blend.",
            MessageType.None);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Projected Blade Tufts", EditorStyles.boldLabel);
        candidateTufts = EditorGUILayout.IntSlider("Scatter Attempts", candidateTufts, 50, 4000);
        tuftDiameterAt1024 = EditorGUILayout.Vector2Field(
            "Diameter (px at 1024)",
            tuftDiameterAt1024);
        bladeLayFlat = EditorGUILayout.Slider("Blade Lay-Flat", bladeLayFlat, 1f, 4f);
        clumpBladeCoverage = EditorGUILayout.Slider(
            "Clump Blade Coverage",
            clumpBladeCoverage,
            0.05f,
            1f);
        looseBladeAttempts = EditorGUILayout.IntSlider(
            "Loose Blade Attempts",
            looseBladeAttempts,
            0,
            6000);
        looseBladeCoverage = EditorGUILayout.Slider(
            "Loose Blade Coverage",
            looseBladeCoverage,
            0.02f,
            0.5f);
        sparseCoverage = EditorGUILayout.Slider(
            "Minimum Coverage",
            sparseCoverage,
            0f,
            1f);
        patchConcentration = EditorGUILayout.Slider(
            "Cloud Concentration",
            patchConcentration,
            0.1f,
            4f);
        EditorGUILayout.HelpBox(
            "Blade colour, dry variation, alpha texture, texture transform, and clip "
            + "threshold come directly from the Blade Material. Tip Colour Strength "
            + "scales how much of its base-to-tip colour difference is baked.",
            MessageType.None);
        tipColorStrength = EditorGUILayout.Slider(
            "Tip Colour Strength",
            tipColorStrength,
            0f,
            1f);
        bladeColorVariation = EditorGUILayout.Slider(
            "Colour Variation",
            bladeColorVariation,
            0f,
            0.5f);
        bladeShadow = EditorGUILayout.ColorField("Blade Shadow", bladeShadow);
        shadowOpacity = EditorGUILayout.Slider("Shadow Opacity", shadowOpacity, 0f, 1f);
        shadowOffsetAt1024 = EditorGUILayout.Vector2Field(
            "Shadow Offset (px)",
            shadowOffsetAt1024);
        shadowBlurAt1024 = EditorGUILayout.Slider(
            "Shadow AO Blur (px)",
            shadowBlurAt1024,
            0f,
            16f);
        bladeHeightRange = EditorGUILayout.Vector2Field("Blade Height Range", bladeHeightRange);

        ClampSettings();
        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(!CanGenerate(out _)))
        {
            if (GUILayout.Button("Generate Albedo + Height", GUILayout.Height(30f)))
            {
                Generate(false);
            }

            if (GUILayout.Button("Generate and Assign Grass Textures", GUILayout.Height(34f)))
            {
                Generate(true);
            }
        }

        if (!CanGenerate(out string validationMessage))
        {
            EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);
        }

        if (previewTexture != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generated Preview", EditorStyles.boldLabel);
            Rect previewRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(previewRect, previewTexture, null, ScaleMode.ScaleToFit);
        }
        if (previewHeightTexture != null)
        {
            EditorGUILayout.LabelField("Generated Height", EditorStyles.boldLabel);
            Rect heightPreviewRect = GUILayoutUtility.GetAspectRect(1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(
                heightPreviewRect,
                previewHeightTexture,
                null,
                ScaleMode.ScaleToFit);
        }

        EditorGUILayout.EndScrollView();
        if (GUI.changed)
        {
            SavePersistentSettings();
        }
    }

    private void LoadPersistentSettings()
    {
        ProceduralGroundGrassTextureSettings saved =
            ProceduralGroundGrassTextureSettings.instance;
        if (!saved.hasSavedSettings)
        {
            return;
        }

        grassClumpPrefab = saved.grassClumpPrefab;
        bladeMaterial = saved.bladeMaterial;
        targetMaterial = saved.targetMaterial;
        outputPath = saved.outputPath;
        heightOutputPath = saved.heightOutputPath;
        resolution = saved.resolution;
        randomSeed = saved.randomSeed;
        settingsVersion = saved.settingsVersion;
        groundColorVariation = saved.groundColorVariation;
        cloudCells = saved.cloudCells;
        cloudOctaves = saved.cloudOctaves;
        cloudContrast = saved.cloudContrast;
        fineVariation = saved.fineVariation;
        groundHeightRange = saved.groundHeightRange;
        candidateTufts = saved.candidateTufts;
        tuftDiameterAt1024 = saved.tuftDiameterAt1024;
        bladeLayFlat = saved.bladeLayFlat;
        clumpBladeCoverage = saved.clumpBladeCoverage;
        looseBladeAttempts = saved.looseBladeAttempts;
        looseBladeCoverage = saved.looseBladeCoverage;
        sparseCoverage = saved.sparseCoverage;
        patchConcentration = saved.patchConcentration;
        tipColorStrength = saved.tipColorStrength;
        bladeColorVariation = saved.bladeColorVariation;
        bladeShadow = saved.bladeShadow;
        shadowOpacity = saved.shadowOpacity;
        shadowOffsetAt1024 = saved.shadowOffsetAt1024;
        shadowBlurAt1024 = saved.shadowBlurAt1024;
        bladeHeightRange = saved.bladeHeightRange;
        previewTexture = null;
        previewHeightTexture = null;
    }

    private void SavePersistentSettings()
    {
        ProceduralGroundGrassTextureSettings saved =
            ProceduralGroundGrassTextureSettings.instance;
        saved.hasSavedSettings = true;
        saved.grassClumpPrefab = grassClumpPrefab;
        saved.bladeMaterial = bladeMaterial;
        saved.targetMaterial = targetMaterial;
        saved.outputPath = outputPath;
        saved.heightOutputPath = heightOutputPath;
        saved.resolution = resolution;
        saved.randomSeed = randomSeed;
        saved.settingsVersion = settingsVersion;
        saved.groundColorVariation = groundColorVariation;
        saved.cloudCells = cloudCells;
        saved.cloudOctaves = cloudOctaves;
        saved.cloudContrast = cloudContrast;
        saved.fineVariation = fineVariation;
        saved.groundHeightRange = groundHeightRange;
        saved.candidateTufts = candidateTufts;
        saved.tuftDiameterAt1024 = tuftDiameterAt1024;
        saved.bladeLayFlat = bladeLayFlat;
        saved.clumpBladeCoverage = clumpBladeCoverage;
        saved.looseBladeAttempts = looseBladeAttempts;
        saved.looseBladeCoverage = looseBladeCoverage;
        saved.sparseCoverage = sparseCoverage;
        saved.patchConcentration = patchConcentration;
        saved.tipColorStrength = tipColorStrength;
        saved.bladeColorVariation = bladeColorVariation;
        saved.bladeShadow = bladeShadow;
        saved.shadowOpacity = shadowOpacity;
        saved.shadowOffsetAt1024 = shadowOffsetAt1024;
        saved.shadowBlurAt1024 = shadowBlurAt1024;
        saved.bladeHeightRange = bladeHeightRange;
        saved.SaveSettings();
    }

    private void ClampSettings()
    {
        resolution = Mathf.Clamp(resolution, 256, 2048);
        cloudCells = Mathf.Clamp(cloudCells, 1, 8);
        cloudOctaves = Mathf.Clamp(cloudOctaves, 1, 7);
        candidateTufts = Mathf.Clamp(candidateTufts, 50, 4000);
        looseBladeAttempts = Mathf.Clamp(looseBladeAttempts, 0, 6000);
        tuftDiameterAt1024.x = Mathf.Max(2f, tuftDiameterAt1024.x);
        tuftDiameterAt1024.y = Mathf.Max(tuftDiameterAt1024.x, tuftDiameterAt1024.y);
        groundHeightRange.x = Mathf.Clamp01(groundHeightRange.x);
        groundHeightRange.y = Mathf.Clamp(groundHeightRange.y, groundHeightRange.x, 1f);
        bladeHeightRange.x = Mathf.Clamp01(bladeHeightRange.x);
        bladeHeightRange.y = Mathf.Clamp(bladeHeightRange.y, bladeHeightRange.x, 1f);
        tipColorStrength = Mathf.Clamp01(tipColorStrength);
    }

    private bool CanGenerate(out string message)
    {
        if (grassClumpPrefab == null)
        {
            message = "Assign a grass clump prefab containing ProceduralGrassClumpAuthoring.";
            return false;
        }

        if (grassClumpPrefab.GetComponentInChildren<ProceduralGrassClumpAuthoring>(true) == null)
        {
            message = "The selected prefab has no ProceduralGrassClumpAuthoring component.";
            return false;
        }

        if (bladeMaterial == null)
        {
            message = "Assign the grass clump's blade material.";
            return false;
        }

        if (!bladeMaterial.HasProperty("_BaseColor")
            || !bladeMaterial.HasProperty("_TipColor")
            || !bladeMaterial.HasProperty("_AlphaMap"))
        {
            message = "The blade material must expose _BaseColor, _TipColor, and _AlphaMap.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputPath)
            || !outputPath.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal)
            || !outputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            message = "The albedo output must be a .png path inside Assets/.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(heightOutputPath)
            || !heightOutputPath.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal)
            || !heightOutputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            message = "The height output must be a .png path inside Assets/.";
            return false;
        }

        if (string.Equals(
            outputPath.Replace('\\', '/'),
            heightOutputPath.Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase))
        {
            message = "The albedo and height outputs must use different paths.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static Material FindBladeMaterial(GameObject prefab)
    {
        if (prefab == null)
        {
            return null;
        }

        MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Material[] materials = renderers[rendererIndex].sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material != null
                    && material.HasProperty("_BaseColor")
                    && material.HasProperty("_TipColor")
                    && material.HasProperty("_AlphaMap"))
                {
                    return material;
                }
            }
        }

        return null;
    }

    private void Generate(bool assignToMaterial)
    {
        SavePersistentSettings();
        if (!CanGenerate(out string validationMessage))
        {
            EditorUtility.DisplayDialog("Cannot Generate Grass Texture", validationMessage, "OK");
            return;
        }

        outputPath = outputPath.Replace('\\', '/');
        heightOutputPath = heightOutputPath.Replace('\\', '/');
        ProceduralGrassClumpAuthoring authoring =
            grassClumpPrefab.GetComponentInChildren<ProceduralGrassClumpAuthoring>(true);
        Mesh sourceMesh = null;
        Texture2D generatedAlbedo = null;
        Texture2D generatedHeight = null;
        BladeMaterialData materialData = null;

        try
        {
            EditorUtility.DisplayProgressBar(
                "Procedural Grass Texture",
                "Building the current grass blade mesh...",
                0.05f);
            sourceMesh = authoring.BuildMesh();
            materialData = new BladeMaterialData(bladeMaterial);
            BuildTextures(
                sourceMesh,
                materialData,
                out generatedAlbedo,
                out generatedHeight);

            string absolutePath = Path.GetFullPath(outputPath);
            string absoluteHeightPath = Path.GetFullPath(heightOutputPath);
            string albedoDirectory = Path.GetDirectoryName(absolutePath);
            string heightDirectory = Path.GetDirectoryName(absoluteHeightPath);
            if (!string.IsNullOrEmpty(albedoDirectory))
            {
                Directory.CreateDirectory(albedoDirectory);
            }
            if (!string.IsNullOrEmpty(heightDirectory))
            {
                Directory.CreateDirectory(heightDirectory);
            }

            EditorUtility.DisplayProgressBar(
                "Procedural Grass Texture",
                "Writing the seamless albedo and height PNGs...",
                0.93f);
            File.WriteAllBytes(absolutePath, generatedAlbedo.EncodeToPNG());
            File.WriteAllBytes(absoluteHeightPath, generatedHeight.EncodeToPNG());
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(heightOutputPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporter(outputPath, false);
            ConfigureImporter(heightOutputPath, true);
            previewTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
            previewHeightTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(heightOutputPath);

            if (assignToMaterial)
            {
                AssignGrassMaps(previewTexture, previewHeightTexture);
            }

            Selection.activeObject = previewTexture;
            EditorGUIUtility.PingObject(previewTexture);
            SceneView.RepaintAll();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "Grass Texture Generation Failed",
                exception.Message,
                "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (sourceMesh != null)
            {
                DestroyImmediate(sourceMesh);
            }
            if (generatedAlbedo != null)
            {
                DestroyImmediate(generatedAlbedo);
            }
            if (generatedHeight != null)
            {
                DestroyImmediate(generatedHeight);
            }
            materialData?.Dispose();
        }
    }

    private void BuildTextures(
        Mesh sourceMesh,
        BladeMaterialData materialData,
        out Texture2D albedoTexture,
        out Texture2D heightTexture)
    {
        Vector3[] vertices = sourceMesh.vertices;
        Vector2[] uvs = sourceMesh.uv;
        int[] triangles = sourceMesh.triangles;
        if (vertices.Length == 0 || triangles.Length == 0)
        {
            throw new InvalidOperationException("The generated grass clump mesh is empty.");
        }

        if (uvs == null || uvs.Length != vertices.Length)
        {
            uvs = BuildHeightUvs(vertices);
        }
        int[] vertexBladeIndices = BuildVertexBladeIndices(uvs);
        vertices = BuildFlattenedProjectionVertices(vertices, uvs);
        Vector2 projectionCentre;
        float projectionSpan;
        GetProjectionBounds(vertices, out projectionCentre, out projectionSpan);

        var pixels = new Color[resolution * resolution];
        var heightPixels = new Color[resolution * resolution];
        var shadowMask = new float[resolution * resolution];
        EditorUtility.DisplayProgressBar(
            "Procedural Grass Texture",
            "Generating tileable cloud patches...",
            0.13f);
        BuildCloudGround(pixels, heightPixels, materialData);
        // Keep an immutable copy of the cloud bed. Blade albedo must be derived
        // from the ground below it, rather than from a flat material colour or
        // from pixels already darkened by AO/overdraw.
        var groundPixels = (Color[])pixels.Clone();

        List<TuftStamp> stamps = BuildTuftStamps();
        var projectedVertices = new Vector2[vertices.Length];
        float progressRange = 0.72f;

        for (int pass = 0; pass < 2; pass++)
        {
            bool shadowPass = pass == 0;
            for (int i = 0; i < stamps.Count; i++)
            {
                if ((i & 31) == 0)
                {
                    float passProgress = (pass + i / (float)Mathf.Max(1, stamps.Count)) * 0.5f;
                    EditorUtility.DisplayProgressBar(
                        "Procedural Grass Texture",
                        shadowPass ? "Painting wrapped blade shadows..." : "Painting projected blades...",
                        0.18f + passProgress * progressRange);
                }

                DrawMeshStamp(
                    pixels,
                    groundPixels,
                    heightPixels,
                    shadowMask,
                    vertices,
                    uvs,
                    vertexBladeIndices,
                    triangles,
                    projectedVertices,
                    projectionCentre,
                    projectionSpan,
                    stamps[i],
                    shadowPass,
                    materialData);
            }

            if (shadowPass)
            {
                BlurAndApplyBladeAo(pixels, shadowMask);
            }
        }

        albedoTexture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBA32,
            false,
            false)
        {
            name = Path.GetFileNameWithoutExtension(outputPath),
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear
        };
        albedoTexture.SetPixels(pixels);
        albedoTexture.Apply(false, false);

        heightTexture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBA32,
            false,
            true)
        {
            name = Path.GetFileNameWithoutExtension(heightOutputPath),
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear
        };
        heightTexture.SetPixels(heightPixels);
        heightTexture.Apply(false, false);
    }

    private void BuildCloudGround(
        Color[] pixels,
        Color[] heightPixels,
        BladeMaterialData materialData)
    {
        Color groundDark = ScaleColor(
            materialData.baseColor,
            1f - groundColorVariation);
        Color groundLight = ScaleColor(
            materialData.baseColor,
            1f + groundColorVariation);
        for (int y = 0; y < resolution; y++)
        {
            float v = (y + 0.5f) / resolution;
            for (int x = 0; x < resolution; x++)
            {
                float u = (x + 0.5f) / resolution;
                float cloud = FractalPeriodicNoise(
                    u,
                    v,
                    cloudCells,
                    cloudOctaves,
                    randomSeed);
                float shaped = Mathf.SmoothStep(0f, 1f, cloud);
                shaped = Mathf.Lerp(0.5f, shaped, cloudContrast);
                float detail = PeriodicValueNoise(
                    u,
                    v,
                    cloudCells * 8,
                    randomSeed + 4409) - 0.5f;
                Color color = Color.Lerp(groundDark, groundLight, shaped);
                color.r = Mathf.Clamp01(color.r + detail * fineVariation);
                color.g = Mathf.Clamp01(color.g + detail * fineVariation);
                color.b = Mathf.Clamp01(color.b + detail * fineVariation * 0.65f);
                color.a = 1f;
                int pixelIndex = y * resolution + x;
                pixels[pixelIndex] = color;

                float groundHeight = Mathf.Lerp(
                    groundHeightRange.x,
                    groundHeightRange.y,
                    shaped);
                groundHeight = Mathf.Clamp01(groundHeight + detail * 0.025f);
                heightPixels[pixelIndex] = new Color(
                    groundHeight,
                    groundHeight,
                    groundHeight,
                    1f);
            }
        }
    }

    private List<TuftStamp> BuildTuftStamps()
    {
        var random = new System.Random(randomSeed ^ 0x2f6e2b1);
        var stamps = new List<TuftStamp>(candidateTufts + looseBladeAttempts);
        float resolutionScale = resolution / 1024f;

        for (int i = 0; i < candidateTufts; i++)
        {
            float u = NextFloat(random);
            float v = NextFloat(random);
            float cloud = FractalPeriodicNoise(
                u,
                v,
                cloudCells,
                cloudOctaves,
                randomSeed + 7919);
            float concentrated = Mathf.Pow(Mathf.Clamp01(cloud), patchConcentration);
            float acceptance = Mathf.Lerp(sparseCoverage, 1f, concentrated);
            if (NextFloat(random) > acceptance)
            {
                continue;
            }

            float diameter = Mathf.Lerp(
                tuftDiameterAt1024.x,
                tuftDiameterAt1024.y,
                NextFloat(random));
            diameter *= resolutionScale * Mathf.Lerp(0.78f, 1.12f, concentrated);
            float colorJitter = (NextFloat(random) * 2f - 1f) * bladeColorVariation;
            stamps.Add(new TuftStamp
            {
                centre = new Vector2(u * resolution, v * resolution),
                rotation = NextFloat(random) * Mathf.PI * 2f,
                diameter = diameter,
                brightness = 1f + colorJitter,
                dryVariation = NextFloat(random),
                bladeKeepProbability = Mathf.Clamp01(
                    clumpBladeCoverage * Mathf.Lerp(0.72f, 1.18f, concentrated)),
                scatterSeed = random.Next()
            });
        }

        // A second, independent population keeps individual blades and tiny
        // fragments between tufts. This prevents the texture from reading as
        // binary repeated clumps while retaining cloud-biased density.
        for (int i = 0; i < looseBladeAttempts; i++)
        {
            float u = NextFloat(random);
            float v = NextFloat(random);
            float cloud = FractalPeriodicNoise(
                u,
                v,
                cloudCells,
                cloudOctaves,
                randomSeed + 12011);
            float concentrated = Mathf.Pow(Mathf.Clamp01(cloud), patchConcentration * 0.72f);
            float acceptance = Mathf.Lerp(0.28f, 0.92f, concentrated);
            if (NextFloat(random) > acceptance)
            {
                continue;
            }

            float diameter = Mathf.Lerp(
                tuftDiameterAt1024.x,
                tuftDiameterAt1024.y,
                NextFloat(random));
            diameter *= resolutionScale * Mathf.Lerp(0.72f, 1.08f, NextFloat(random));
            float colorJitter = (NextFloat(random) * 2f - 1f) * bladeColorVariation;
            stamps.Add(new TuftStamp
            {
                centre = new Vector2(u * resolution, v * resolution),
                rotation = NextFloat(random) * Mathf.PI * 2f,
                diameter = diameter,
                brightness = 1f + colorJitter,
                dryVariation = NextFloat(random),
                bladeKeepProbability = looseBladeCoverage,
                scatterSeed = random.Next()
            });
        }

        return stamps;
    }

    private void DrawMeshStamp(
        Color[] pixels,
        Color[] groundPixels,
        Color[] heightPixels,
        float[] shadowMask,
        Vector3[] vertices,
        Vector2[] uvs,
        int[] vertexBladeIndices,
        int[] triangles,
        Vector2[] projectedVertices,
        Vector2 projectionCentre,
        float projectionSpan,
        TuftStamp stamp,
        bool shadowPass,
        BladeMaterialData materialData)
    {
        float cosine = Mathf.Cos(stamp.rotation);
        float sine = Mathf.Sin(stamp.rotation);
        float scale = stamp.diameter / projectionSpan;
        Vector2 shadowOffset = shadowOffsetAt1024 * (resolution / 1024f);

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 local = new Vector2(vertices[i].x, vertices[i].z) - projectionCentre;
            local *= scale;
            Vector2 rotated = new Vector2(
                local.x * cosine - local.y * sine,
                local.x * sine + local.y * cosine);
            projectedVertices[i] = stamp.centre + rotated
                + (shadowPass ? shadowOffset : Vector2.zero);
        }

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int index0 = triangles[i];
            int index1 = triangles[i + 1];
            int index2 = triangles[i + 2];
            int bladeIndex = vertexBladeIndices[index0];
            if (Hash01(stamp.scatterSeed, bladeIndex, randomSeed + 17027)
                > stamp.bladeKeepProbability)
            {
                continue;
            }
            RasterizeTriangle(
                pixels,
                groundPixels,
                heightPixels,
                shadowMask,
                projectedVertices[index0],
                projectedVertices[index1],
                projectedVertices[index2],
                uvs[index0],
                uvs[index1],
                uvs[index2],
                stamp,
                shadowPass,
                materialData);
        }
    }

    private void RasterizeTriangle(
        Color[] pixels,
        Color[] groundPixels,
        Color[] heightPixels,
        float[] shadowMask,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        TuftStamp stamp,
        bool shadowPass,
        BladeMaterialData materialData)
    {
        float area = Cross(b - a, c - a);
        if (Mathf.Abs(area) < 0.0001f)
        {
            return;
        }

        int minimumX = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - 1f);
        int maximumX = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + 1f);
        int minimumY = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - 1f);
        int maximumY = Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) + 1f);

        for (int y = minimumY; y <= maximumY; y++)
        {
            for (int x = minimumX; x <= maximumX; x++)
            {
                int coveredSamples = 0;
                Vector3 accumulatedWeights = Vector3.zero;
                for (int sample = 0; sample < AntialiasSampleOffsets.Length; sample++)
                {
                    Vector2 point = new Vector2(x, y) + AntialiasSampleOffsets[sample];
                    if (TryGetBarycentric(point, a, b, c, area, out Vector3 weights))
                    {
                        Vector2 sampleUv = uvA * weights.x
                            + uvB * weights.y
                            + uvC * weights.z;
                        if (materialData.PassesAlphaClip(sampleUv))
                        {
                            coveredSamples++;
                            accumulatedWeights += weights;
                        }
                    }
                }

                if (coveredSamples == 0)
                {
                    continue;
                }

                float coverage = coveredSamples * 0.25f;
                Vector3 barycentric = accumulatedWeights / coveredSamples;
                int wrappedX = PositiveModulo(x, resolution);
                int wrappedY = PositiveModulo(y, resolution);
                int pixelIndex = wrappedY * resolution + wrappedX;
                if (shadowPass)
                {
                    shadowMask[pixelIndex] = Mathf.Max(shadowMask[pixelIndex], coverage);
                    continue;
                }

                Vector2 uv = uvA * barycentric.x
                    + uvB * barycentric.y
                    + uvC * barycentric.z;
                float height = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(uv.y));
                float generatedBladeHeight = Mathf.Lerp(
                    bladeHeightRange.x,
                    bladeHeightRange.y,
                    height);
                Color localGround = groundPixels[pixelIndex];
                Color permittedTipColor = AddRelativeColorDifference(
                    localGround,
                    materialData.baseColor,
                    materialData.tipColor,
                    tipColorStrength);
                Color source = Color.Lerp(
                    localGround,
                    permittedTipColor,
                    height);
                float dryMask = stamp.dryVariation
                    * materialData.dryBlend
                    * Mathf.SmoothStep(0.28f, 1f, height);
                Color localDryColor = AddRelativeColorDifference(
                    localGround,
                    materialData.baseColor,
                    materialData.dryColor,
                    1f);
                source = Color.Lerp(source, localDryColor, dryMask);
                // Colour Variation is the only remaining blade brightness
                // multiplier. Unlike the former side/density lighting, it is
                // explicit in the generator UI and can be set to zero.
                source = ScaleColor(source, stamp.brightness);
                source.a = coverage;
                pixels[pixelIndex] = AlphaBlend(pixels[pixelIndex], source);
                float existingHeight = heightPixels[pixelIndex].r;
                float combinedHeight = Mathf.Lerp(
                    existingHeight,
                    Mathf.Max(existingHeight, generatedBladeHeight),
                    coverage);
                heightPixels[pixelIndex] = new Color(
                    combinedHeight,
                    combinedHeight,
                    combinedHeight,
                    1f);
            }
        }
    }

    private void BlurAndApplyBladeAo(Color[] pixels, float[] shadowMask)
    {
        int radius = Mathf.Clamp(
            Mathf.RoundToInt(shadowBlurAt1024 * resolution / 1024f),
            0,
            32);
        float[] blurred = shadowMask;
        if (radius > 0)
        {
            float sigma = Mathf.Max(0.75f, radius * 0.55f);
            var weights = new float[radius * 2 + 1];
            float weightTotal = 0f;
            for (int offset = -radius; offset <= radius; offset++)
            {
                float weight = Mathf.Exp(-(offset * offset) / (2f * sigma * sigma));
                weights[offset + radius] = weight;
                weightTotal += weight;
            }
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] /= weightTotal;
            }

            var horizontal = new float[shadowMask.Length];
            blurred = new float[shadowMask.Length];
            for (int y = 0; y < resolution; y++)
            {
                int row = y * resolution;
                for (int x = 0; x < resolution; x++)
                {
                    float value = 0f;
                    for (int offset = -radius; offset <= radius; offset++)
                    {
                        int sampleX = PositiveModulo(x + offset, resolution);
                        value += shadowMask[row + sampleX] * weights[offset + radius];
                    }
                    horizontal[row + x] = value;
                }
            }

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float value = 0f;
                    for (int offset = -radius; offset <= radius; offset++)
                    {
                        int sampleY = PositiveModulo(y + offset, resolution);
                        value += horizontal[sampleY * resolution + x]
                            * weights[offset + radius];
                    }
                    blurred[y * resolution + x] = value;
                }
            }
        }

        for (int i = 0; i < pixels.Length; i++)
        {
            Color ao = bladeShadow;
            ao.a = Mathf.Clamp01(blurred[i] * shadowOpacity);
            pixels[i] = AlphaBlend(pixels[i], ao);
        }
    }

    private void AssignGrassMaps(Texture2D albedoTexture, Texture2D heightTexture)
    {
        if (targetMaterial == null)
        {
            throw new InvalidOperationException("Assign a target material before generating and assigning.");
        }

        if (!targetMaterial.HasProperty("_GrassMap"))
        {
            throw new InvalidOperationException(
                $"Material '{targetMaterial.name}' has no _GrassMap property.");
        }
        if (!targetMaterial.HasProperty("_GrassHeightMap"))
        {
            throw new InvalidOperationException(
                $"Material '{targetMaterial.name}' has no _GrassHeightMap property.");
        }

        Undo.RecordObject(targetMaterial, "Assign Procedural Ground Grass Textures");
        targetMaterial.SetTexture("_GrassMap", albedoTexture);
        targetMaterial.SetTexture("_GrassHeightMap", heightTexture);
        EditorUtility.SetDirty(targetMaterial);
        AssetDatabase.SaveAssets();
    }

    private static void ConfigureImporter(string path, bool isLinear)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = !isLinear;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 2;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();
    }

    private Vector3[] BuildFlattenedProjectionVertices(Vector3[] source, Vector2[] uvs)
    {
        var flattened = (Vector3[])source.Clone();
        Vector2 bladeBase = Vector2.zero;
        for (int i = 0; i + 1 < flattened.Length; i += 2)
        {
            Vector2 left = new Vector2(source[i].x, source[i].z);
            Vector2 right = new Vector2(source[i + 1].x, source[i + 1].z);
            Vector2 centre = (left + right) * 0.5f;
            if (uvs[i].y <= 0.0001f)
            {
                bladeBase = centre;
            }

            Vector2 halfWidth = (right - left) * 0.5f;
            Vector2 flattenedCentre = bladeBase + (centre - bladeBase) * bladeLayFlat;
            Vector2 flattenedLeft = flattenedCentre - halfWidth;
            Vector2 flattenedRight = flattenedCentre + halfWidth;
            flattened[i].x = flattenedLeft.x;
            flattened[i].z = flattenedLeft.y;
            flattened[i + 1].x = flattenedRight.x;
            flattened[i + 1].z = flattenedRight.y;
        }
        return flattened;
    }

    private static int[] BuildVertexBladeIndices(Vector2[] uvs)
    {
        var bladeIndices = new int[uvs.Length];
        int bladeIndex = -1;
        for (int i = 0; i < uvs.Length; i += 2)
        {
            if (uvs[i].y <= 0.0001f)
            {
                bladeIndex++;
            }
            bladeIndices[i] = Mathf.Max(0, bladeIndex);
            if (i + 1 < bladeIndices.Length)
            {
                bladeIndices[i + 1] = Mathf.Max(0, bladeIndex);
            }
        }
        return bladeIndices;
    }

    private static void GetProjectionBounds(
        Vector3[] vertices,
        out Vector2 centre,
        out float span)
    {
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 point = new Vector2(vertices[i].x, vertices[i].z);
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }

        centre = (minimum + maximum) * 0.5f;
        Vector2 size = maximum - minimum;
        span = Mathf.Max(0.0001f, Mathf.Max(size.x, size.y));
    }

    private static Vector2[] BuildHeightUvs(Vector3[] vertices)
    {
        float minimumY = float.PositiveInfinity;
        float maximumY = float.NegativeInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            minimumY = Mathf.Min(minimumY, vertices[i].y);
            maximumY = Mathf.Max(maximumY, vertices[i].y);
        }

        float inverseHeight = 1f / Mathf.Max(0.0001f, maximumY - minimumY);
        var uvs = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            uvs[i] = new Vector2(0.5f, (vertices[i].y - minimumY) * inverseHeight);
        }
        return uvs;
    }

    private static Color AlphaBlend(Color destination, Color source)
    {
        float alpha = Mathf.Clamp01(source.a);
        Color result = Color.Lerp(destination, source, alpha);
        result.a = 1f;
        return result;
    }

    private static Color AddRelativeColorDifference(
        Color localBase,
        Color materialBase,
        Color materialVariant,
        float strength)
    {
        strength = Mathf.Clamp01(strength);
        return new Color(
            Mathf.Clamp01(localBase.r + (materialVariant.r - materialBase.r) * strength),
            Mathf.Clamp01(localBase.g + (materialVariant.g - materialBase.g) * strength),
            Mathf.Clamp01(localBase.b + (materialVariant.b - materialBase.b) * strength),
            1f);
    }

    private static Color ScaleColor(Color color, float scale)
    {
        return new Color(
            Mathf.Clamp01(color.r * scale),
            Mathf.Clamp01(color.g * scale),
            Mathf.Clamp01(color.b * scale),
            1f);
    }

    private static bool TryGetBarycentric(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        float area,
        out Vector3 weights)
    {
        float inverseArea = 1f / area;
        float weightA = Cross(b - point, c - point) * inverseArea;
        float weightB = Cross(c - point, a - point) * inverseArea;
        float weightC = 1f - weightA - weightB;
        const float tolerance = -0.0001f;
        weights = new Vector3(weightA, weightB, weightC);
        return weightA >= tolerance && weightB >= tolerance && weightC >= tolerance;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float FractalPeriodicNoise(
        float u,
        float v,
        int baseCells,
        int octaves,
        int seed)
    {
        float value = 0f;
        float amplitude = 1f;
        float totalAmplitude = 0f;
        int cells = Mathf.Max(1, baseCells);
        for (int octave = 0; octave < octaves; octave++)
        {
            value += PeriodicValueNoise(u, v, cells, seed + octave * 1013) * amplitude;
            totalAmplitude += amplitude;
            amplitude *= 0.5f;
            cells *= 2;
        }
        return value / Mathf.Max(0.0001f, totalAmplitude);
    }

    private static float PeriodicValueNoise(float u, float v, int cells, int seed)
    {
        float x = u * cells;
        float y = v * cells;
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        float tx = x - x0;
        float ty = y - y0;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);

        float bottom = Mathf.Lerp(
            Hash01(PositiveModulo(x0, cells), PositiveModulo(y0, cells), seed),
            Hash01(PositiveModulo(x1, cells), PositiveModulo(y0, cells), seed),
            tx);
        float top = Mathf.Lerp(
            Hash01(PositiveModulo(x0, cells), PositiveModulo(y1, cells), seed),
            Hash01(PositiveModulo(x1, cells), PositiveModulo(y1, cells), seed),
            tx);
        return Mathf.Lerp(bottom, top, ty);
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint hash = (uint)seed;
            hash ^= (uint)x * 0x8da6b343u;
            hash ^= (uint)y * 0xd8163841u;
            hash ^= hash >> 13;
            hash *= 0x85ebca6bu;
            hash ^= hash >> 16;
            return (hash & 0x00ffffffu) / 16777215f;
        }
    }

    private static int PositiveModulo(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static float NextFloat(System.Random random)
    {
        return (float)random.NextDouble();
    }
}
