using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class EggRarityAssetSetup
{
    private const string EggPrefabPath =
        "Assets/Eggs/prefabs/prefab_egg_chicken.prefab";
    private const string MaterialFolder = "Assets/Eggs/materials";
    private const string StandardMaterialPath =
        MaterialFolder + "/mat_egg_atlas.mat";
    private const string SilverMaterialPath =
        MaterialFolder + "/mat_egg_silver.mat";
    private const string GoldMaterialPath =
        MaterialFolder + "/mat_egg_gold.mat";
    private const string GalaxyMaterialPath =
        MaterialFolder + "/mat_egg_galaxy.mat";
    private const string GalaxyTexturePath =
        MaterialFolder + "/tex_egg_galaxy_speckles.asset";

    [MenuItem("Tools/Eggcessive/Rebuild Rare Egg Materials")]
    public static void Generate()
    {
        Material standard = AssetDatabase.LoadAssetAtPath<Material>(
            StandardMaterialPath);

        if (standard == null)
        {
            throw new MissingReferenceException(
                $"Missing standard egg material at {StandardMaterialPath}.");
        }

        Material silver = CreateLitMaterial(
            SilverMaterialPath,
            "mat_egg_silver",
            new Color(0.68f, 0.78f, 0.9f),
            0.92f,
            0.84f);
        Material gold = CreateLitMaterial(
            GoldMaterialPath,
            "mat_egg_gold",
            new Color(1f, 0.52f, 0.035f),
            0.88f,
            0.78f);
        Texture2D galaxyTexture = CreateGalaxyTexture();
        Material galaxy = CreateLitMaterial(
            GalaxyMaterialPath,
            "mat_egg_galaxy",
            new Color(0.13f, 0.018f, 0.24f),
            0.62f,
            0.92f);
        SetTexture(galaxy, "_BaseMap", galaxyTexture);
        SetTexture(galaxy, "_MainTex", galaxyTexture);
        galaxy.EnableKeyword("_EMISSION");
        galaxy.SetColor("_EmissionColor", new Color(0.16f, 0.025f, 0.3f) * 1.4f);

        GameObject root = PrefabUtility.LoadPrefabContents(EggPrefabPath);

        try
        {
            ChickenEgg egg = root.GetComponent<ChickenEgg>();

            if (egg == null)
            {
                egg = root.AddComponent<ChickenEgg>();
            }

            SerializedObject serializedEgg = new SerializedObject(egg);
            SerializedProperty materials = serializedEgg.FindProperty("typeMaterials");
            materials.arraySize = 4;
            materials.GetArrayElementAtIndex(0).objectReferenceValue = standard;
            materials.GetArrayElementAtIndex(1).objectReferenceValue = silver;
            materials.GetArrayElementAtIndex(2).objectReferenceValue = gold;
            materials.GetArrayElementAtIndex(3).objectReferenceValue = galaxy;
            serializedEgg.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, EggPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Silver, gold, and galaxy egg materials rebuilt and assigned.");
    }

    [MenuItem("Tools/Eggcessive/Validate Rare Egg Materials")]
    public static void Validate()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EggPrefabPath);
        ChickenEgg egg = prefab != null ? prefab.GetComponent<ChickenEgg>() : null;

        if (egg == null)
        {
            throw new MissingComponentException(nameof(ChickenEgg));
        }

        SerializedObject serializedEgg = new SerializedObject(egg);
        SerializedProperty materials = serializedEgg.FindProperty("typeMaterials");

        if (materials == null || materials.arraySize != 4)
        {
            throw new InvalidOperationException(
                "Egg rarity material palette is not configured.");
        }

        for (int index = 0; index < materials.arraySize; index++)
        {
            if (materials.GetArrayElementAtIndex(index).objectReferenceValue == null)
            {
                throw new MissingReferenceException(
                    $"Egg rarity material {index} is missing.");
            }
        }

        Debug.Log("Rare egg material validation passed.");
    }

    private static Material CreateLitMaterial(
        string path,
        string materialName,
        Color color,
        float metallic,
        float smoothness)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            AssetDatabase.DeleteAsset(path);
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard");
        Material material = new Material(shader)
        {
            name = materialName,
            color = color
        };
        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        SetFloat(material, "_Metallic", metallic);
        SetFloat(material, "_Smoothness", smoothness);
        SetFloat(material, "_Glossiness", smoothness);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Texture2D CreateGalaxyTexture()
    {
        Object existing = AssetDatabase.LoadAssetAtPath<Object>(GalaxyTexturePath);

        if (existing != null)
        {
            AssetDatabase.DeleteAsset(GalaxyTexturePath);
        }

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = "tex_egg_galaxy_speckles",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat
        };
        var random = new System.Random(78231);
        Color darkA = new Color(0.055f, 0.004f, 0.12f);
        Color darkB = new Color(0.18f, 0.018f, 0.3f);
        Color[] speckles =
        {
            new Color(0.15f, 0.9f, 1f),
            new Color(1f, 0.2f, 0.72f),
            new Color(1f, 0.82f, 0.18f),
            new Color(0.52f, 0.25f, 1f)
        };
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float wave = Mathf.PerlinNoise(x * 0.09f, y * 0.09f);
                Color color = Color.Lerp(darkA, darkB, wave);

                if (random.NextDouble() < 0.075)
                {
                    color = speckles[random.Next(speckles.Length)]
                        * UnityEngine.Random.Range(0.8f, 1.4f);
                }

                pixels[y * size + x] = color;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(true, false);
        AssetDatabase.CreateAsset(texture, GalaxyTexturePath);
        return texture;
    }

    private static void SetColor(Material material, string property, Color value)
    {
        if (material.HasProperty(property))
        {
            material.SetColor(property, value);
        }
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
        {
            material.SetFloat(property, value);
        }
    }

    private static void SetTexture(
        Material material,
        string property,
        Texture texture)
    {
        if (material.HasProperty(property))
        {
            material.SetTexture(property, texture);
        }
    }
}
