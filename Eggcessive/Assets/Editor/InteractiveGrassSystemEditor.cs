using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(InteractiveGrassSystem))]
public sealed class InteractiveGrassSystemEditor : Editor
{
    private const string GroundShaderName = "Eggcessive/Ground Layers";
    private const string GrassTexturePath = "Assets/Env/textures/t_ground_grass_placeholder.png";
    private const string DirtTexturePath = "Assets/Env/textures/t_ground_dirt_placeholder.png";
    private const string LayerMaskPath = "Assets/Env/textures/t_ground_layer_mask.png";
    private const string MaskGeneratorVersion = "EggcessiveGroundMask:v13";

    private static readonly Color DefaultGrassColor = new Color(
        0.12156863f,
        0.32156864f,
        0.05882353f,
        1f);
    private static readonly Color DefaultDirtColor = new Color(0.52f, 0.34f, 0.18f, 1f);

    [InitializeOnLoadMethod]
    private static void QueueDefaultSetup()
    {
        EditorApplication.delayCall += SetupLoadedGrassSystems;
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += SetupLoadedGrassSystems;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var system = (InteractiveGrassSystem)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "The ground mask uses red for dirt, green for grass coverage, and blue for "
            + "world-scale transition breakup. Alpha guarantees grass beneath actual "
            + "placed clumps. Grass is composited over dirt. "
            + "Regenerating the mask overwrites any painting in the PNG.",
            MessageType.Info);

        if (GUILayout.Button("Set Up Ground Layer Material", GUILayout.Height(28f)))
        {
            EnsureGroundAssets(system, false);
        }

        if (GUILayout.Button("Regenerate Ground Layer Mask", GUILayout.Height(28f)))
        {
            if (EditorUtility.DisplayDialog(
                "Regenerate Ground Layer Mask",
                "This overwrites all generated mask channels, including any painted edits.",
                "Regenerate",
                "Cancel"))
            {
                EnsureGroundAssets(system, true);
            }
        }

        if (GUILayout.Button("Open Procedural Grass Texture Generator", GUILayout.Height(28f)))
        {
            ProceduralGroundGrassTextureWindow.OpenWindow(system.GroundColourSource);
        }

        Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(LayerMaskPath);
        using (new EditorGUI.DisabledScope(mask == null))
        {
            if (GUILayout.Button("Select Ground Layer Mask"))
            {
                Selection.activeObject = mask;
                EditorGUIUtility.PingObject(mask);
            }
        }
    }

    private static void SetupLoadedGrassSystems()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        InteractiveGrassSystem[] systems = Object.FindObjectsByType<InteractiveGrassSystem>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < systems.Length; i++)
        {
            EnsureGroundAssets(systems[i], false);
        }
    }

    private static void EnsureGroundAssets(InteractiveGrassSystem system, bool overwriteMask)
    {
        if (system == null || system.GroundColourSource == null)
        {
            return;
        }

        EnsureTextureFolder();
        system.GenerateGrass();

        if (AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexturePath) == null)
        {
            WriteSolidTexture(GrassTexturePath, DefaultGrassColor);
            ConfigureTextureImporter(GrassTexturePath, false);
        }

        if (AssetDatabase.LoadAssetAtPath<Texture2D>(DirtTexturePath) == null)
        {
            WriteSolidTexture(DirtTexturePath, DefaultDirtColor);
            ConfigureTextureImporter(DirtTexturePath, false);
        }

        TextureImporter existingMaskImporter = AssetImporter.GetAtPath(LayerMaskPath)
            as TextureImporter;
        bool maskNeedsUpgrade = existingMaskImporter == null
            || existingMaskImporter.userData != MaskGeneratorVersion;
        if (overwriteMask
            || AssetDatabase.LoadAssetAtPath<Texture2D>(LayerMaskPath) == null
            || maskNeedsUpgrade)
        {
            WriteDistributionMask(system, LayerMaskPath);
            ConfigureTextureImporter(LayerMaskPath, true);
        }
        TextureImporter configuredMaskImporter = AssetImporter.GetAtPath(LayerMaskPath)
            as TextureImporter;
        bool placedCoverageAvailable = configuredMaskImporter != null
            && configuredMaskImporter.userData == MaskGeneratorVersion;

        Shader shader = Shader.Find(GroundShaderName);
        if (shader == null)
        {
            Debug.LogError($"Could not find shader '{GroundShaderName}'.", system);
            return;
        }

        Material material = system.GroundColourSource;
        bool requiresInitialSetup = material.shader != shader;
        Color bladeBaseColor = material.HasProperty("_BaseColor")
            ? material.GetColor("_BaseColor")
            : DefaultGrassColor;

        Undo.RecordObject(material, "Set Up Ground Layer Material");
        material.shader = shader;
        if (requiresInitialSetup || material.GetTexture("_GrassMap") == null)
        {
            material.SetTexture(
                "_GrassMap",
                AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexturePath));
        }
        if (requiresInitialSetup || material.GetTexture("_DirtMap") == null)
        {
            material.SetTexture(
                "_DirtMap",
                AssetDatabase.LoadAssetAtPath<Texture2D>(DirtTexturePath));
        }
        if (material.GetTexture("_LayerMask") == null)
        {
            material.SetTexture(
                "_LayerMask",
                AssetDatabase.LoadAssetAtPath<Texture2D>(LayerMaskPath));
        }
        if (requiresInitialSetup)
        {
            material.SetColor("_GrassTint", Color.white);
            material.SetColor("_DirtTint", Color.white);
            material.SetColor("_BaseColor", bladeBaseColor);
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_GrassMaskStrength", 2f);
            material.SetFloat("_ReceiveShadows", 1f);
            material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
        }
        material.SetVector("_MaskWorldRect", GetWorldRect(system));
        material.SetFloat("_PlacedCoverageAvailable", placedCoverageAvailable ? 1f : 0f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        system.GenerateGrass();
        SceneView.RepaintAll();
    }

    private static Vector4 GetWorldRect(InteractiveGrassSystem system)
    {
        Vector2 size = system.AreaSize;
        Vector3[] corners =
        {
            system.transform.TransformPoint(new Vector3(-size.x * 0.5f, 0f, -size.y * 0.5f)),
            system.transform.TransformPoint(new Vector3(size.x * 0.5f, 0f, -size.y * 0.5f)),
            system.transform.TransformPoint(new Vector3(-size.x * 0.5f, 0f, size.y * 0.5f)),
            system.transform.TransformPoint(new Vector3(size.x * 0.5f, 0f, size.y * 0.5f))
        };

        float minimumX = corners[0].x;
        float maximumX = corners[0].x;
        float minimumZ = corners[0].z;
        float maximumZ = corners[0].z;
        for (int i = 1; i < corners.Length; i++)
        {
            minimumX = Mathf.Min(minimumX, corners[i].x);
            maximumX = Mathf.Max(maximumX, corners[i].x);
            minimumZ = Mathf.Min(minimumZ, corners[i].z);
            maximumZ = Mathf.Max(maximumZ, corners[i].z);
        }

        return new Vector4(
            minimumX,
            minimumZ,
            Mathf.Max(0.0001f, maximumX - minimumX),
            Mathf.Max(0.0001f, maximumZ - minimumZ));
    }

    private static void WriteDistributionMask(InteractiveGrassSystem system, string path)
    {
        int resolution = system.GroundMaskResolution;
        var texture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBA32,
            false,
            true);
        var pixels = new Color32[resolution * resolution];
        Vector2 size = system.AreaSize;

        for (int y = 0; y < resolution; y++)
        {
            float v = (y + 0.5f) / resolution;
            for (int x = 0; x < resolution; x++)
            {
                float u = (x + 0.5f) / resolution;
                Vector3 localPosition = new Vector3(
                    (u - 0.5f) * size.x,
                    0f,
                    (v - 0.5f) * size.y);
                Vector3 worldPosition = system.transform.TransformPoint(localPosition);
                // This is the exact broad coverage field used by placement.
                // Keep it linear: shader-side re-thresholding previously turned
                // intermediate meadow coverage into unrelated dirt holes.
                float grassCoverage = system.EvaluateGroundGrassCoverage(worldPosition);
                byte grass = (byte)Mathf.RoundToInt(grassCoverage * 255f);
                // Preserve the unflattened distribution in blue. The ground
                // shader uses it only while transitioning, providing world-scale
                // breakup instead of a uniform contour around the green mask.
                byte transitionNoise = (byte)Mathf.RoundToInt(
                    system.EvaluateGrassTransitionNoise(worldPosition) * 255f);

                // Dirt is the complete lower layer; grass is painted over it.
                // Alpha starts empty and is stamped below from the exact set of
                // generated clumps, guaranteeing ground support beneath them.
                pixels[y * resolution + x] = new Color32(
                    255,
                    grass,
                    transitionNoise,
                    0);
            }
        }

        StampPlacedGrassCoverage(system, pixels, resolution);
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void StampPlacedGrassCoverage(
        InteractiveGrassSystem system,
        Color32[] pixels,
        int resolution)
    {
        var coverageDiscs = new List<Vector4>(system.InstanceCount);
        system.GetGroundCoverageDiscs(coverageDiscs);
        Vector2 areaSize = system.AreaSize;
        Vector3 worldScale = system.transform.lossyScale;
        float worldWidth = Mathf.Max(0.0001f, areaSize.x * Mathf.Abs(worldScale.x));
        float worldDepth = Mathf.Max(0.0001f, areaSize.y * Mathf.Abs(worldScale.z));

        for (int discIndex = 0; discIndex < coverageDiscs.Count; discIndex++)
        {
            Vector4 disc = coverageDiscs[discIndex];
            Vector3 localPosition = system.transform.InverseTransformPoint(
                new Vector3(disc.x, system.transform.position.y, disc.y));
            float centreX = (localPosition.x / areaSize.x + 0.5f) * resolution;
            float centreY = (localPosition.z / areaSize.y + 0.5f) * resolution;
            float coverageRadius = Mathf.Max(0.1f, disc.z * 1.6f);
            float radiusX = coverageRadius / worldWidth * resolution;
            float radiusY = coverageRadius / worldDepth * resolution;
            float featheredRadiusX = Mathf.Max(1f, radiusX * 1.35f);
            float featheredRadiusY = Mathf.Max(1f, radiusY * 1.35f);

            int minimumX = Mathf.Max(0, Mathf.FloorToInt(centreX - featheredRadiusX));
            int maximumX = Mathf.Min(resolution - 1, Mathf.CeilToInt(centreX + featheredRadiusX));
            int minimumY = Mathf.Max(0, Mathf.FloorToInt(centreY - featheredRadiusY));
            int maximumY = Mathf.Min(resolution - 1, Mathf.CeilToInt(centreY + featheredRadiusY));

            for (int y = minimumY; y <= maximumY; y++)
            {
                float offsetY = (y + 0.5f - centreY) / Mathf.Max(radiusY, 0.5f);
                for (int x = minimumX; x <= maximumX; x++)
                {
                    float offsetX = (x + 0.5f - centreX) / Mathf.Max(radiusX, 0.5f);
                    float distance = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    float coverage = 1f - Mathf.SmoothStep(1f, 1.35f, distance);
                    if (coverage <= 0f)
                    {
                        continue;
                    }

                    int pixelIndex = y * resolution + x;
                    Color32 pixel = pixels[pixelIndex];
                    byte stampedCoverage = (byte)Mathf.RoundToInt(coverage * 255f);
                    pixel.g = (byte)Mathf.Max(pixel.g, stampedCoverage);
                    pixel.a = (byte)Mathf.Max(pixel.a, stampedCoverage);
                    pixels[pixelIndex] = pixel;
                }
            }
        }
    }

    private static void WriteSolidTexture(string path, Color color)
    {
        const int size = 16;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void ConfigureTextureImporter(string path, bool isMask)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = !isMask;
        importer.mipmapEnabled = !isMask;
        importer.isReadable = isMask;
        importer.wrapMode = isMask ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.userData = isMask ? MaskGeneratorVersion : string.Empty;
        importer.SaveAndReimport();
    }

    private static void EnsureTextureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Env/textures"))
        {
            AssetDatabase.CreateFolder("Assets/Env", "textures");
        }
    }
}
