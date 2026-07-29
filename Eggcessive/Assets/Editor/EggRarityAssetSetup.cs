using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class EggRarityAssetSetup
{
    private const string CommonEggPrefabPath =
        "Assets/Eggs/prefabs/prefab_egg_chicken.prefab";
    private const string CosmicEggPrefabPath =
        "Assets/Eggs/prefabs/prefab_egg_cosmic.prefab";
    private const string CosmicVfxPrefabPath =
        "Assets/VFX/prefabs/vfx_egg_cosmic.prefab";
    private const string ChickenPrefabPath =
        "Assets/Chicken/prefabs/prefab_chicken.prefab";
    private const string EggTexturePath =
        "Assets/Eggs/textures/t_eggs.psd";
    private const string MaterialFolder = "Assets/Eggs/materials";
    private const string AtlasMaterialPath =
        MaterialFolder + "/mat_egg_atlas.mat";
    private const string LegendaryMaterialPath =
        MaterialFolder + "/mat_egg_legendary.mat";
    private const string CosmicMaterialPath =
        MaterialFolder + "/mat_egg_cosmic.mat";

    [MenuItem("Tools/Eggcessive/Rebuild Egg Rarity Assets")]
    public static void Generate()
    {
        ConfigureAtlasImporter();

        Material atlas = LoadRequiredAsset<Material>(AtlasMaterialPath);
        Material legendary = LoadRequiredAsset<Material>(LegendaryMaterialPath);
        Material cosmic = LoadRequiredAsset<Material>(CosmicMaterialPath);
        GameObject commonPrefab =
            LoadRequiredAsset<GameObject>(CommonEggPrefabPath);
        GameObject cosmicVfx =
            LoadRequiredAsset<GameObject>(CosmicVfxPrefabPath);

        ConfigureEggPalette(
            CommonEggPrefabPath,
            false,
            atlas,
            legendary,
            cosmic);
        commonPrefab = LoadRequiredAsset<GameObject>(CommonEggPrefabPath);
        GameObject cosmicPrefab = CreateCosmicEggPrefab(
            commonPrefab,
            cosmicVfx,
            cosmic);
        ConfigureEggPalette(
            CosmicEggPrefabPath,
            true,
            atlas,
            legendary,
            cosmic);
        WireCosmicEggIntoChicken(cosmicPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Validate();
        Debug.Log(
            "Common, rare (blue), epic, legendary, and cosmic egg assets rebuilt and assigned.");
    }

    [MenuItem("Tools/Eggcessive/Validate Egg Rarity Assets")]
    public static void Validate()
    {
        Material atlas = LoadRequiredAsset<Material>(AtlasMaterialPath);
        Material legendary = LoadRequiredAsset<Material>(LegendaryMaterialPath);
        Material cosmic = LoadRequiredAsset<Material>(CosmicMaterialPath);
        GameObject commonPrefab =
            LoadRequiredAsset<GameObject>(CommonEggPrefabPath);
        GameObject cosmicPrefab =
            LoadRequiredAsset<GameObject>(CosmicEggPrefabPath);
        GameObject cosmicVfx =
            LoadRequiredAsset<GameObject>(CosmicVfxPrefabPath);
        ValidateEggPalette(
            commonPrefab,
            false,
            atlas,
            legendary,
            cosmic);
        ValidateEggPalette(
            cosmicPrefab,
            true,
            atlas,
            legendary,
            cosmic);

        Transform vfx = cosmicPrefab.transform.Find(cosmicVfx.name);
        Object vfxSource = vfx != null
            ? PrefabUtility.GetCorrespondingObjectFromSource(vfx.gameObject)
            : null;

        if (vfx == null
            || vfxSource == null
            || AssetDatabase.GetAssetPath(vfxSource) != CosmicVfxPrefabPath)
        {
            throw new InvalidOperationException(
                "The cosmic egg prefab does not contain the authored cosmic VFX prefab.");
        }

        GameObject chickenPrefab =
            LoadRequiredAsset<GameObject>(ChickenPrefabPath);
        ChickenController chicken =
            chickenPrefab.GetComponent<ChickenController>();

        if (chicken == null)
        {
            throw new MissingComponentException(nameof(ChickenController));
        }

        SerializedObject serializedChicken = new SerializedObject(chicken);

        if (serializedChicken.FindProperty("cosmicEggPrefab").objectReferenceValue
            != cosmicPrefab)
        {
            throw new MissingReferenceException(
                "The chicken prefab is not wired to the cosmic egg prefab.");
        }

        Debug.Log(
            "Egg rarity validation passed: atlas offsets, special materials, cosmic VFX prefab, and chicken reference are valid.");
    }

    private static void ConfigureAtlasImporter()
    {
        TextureImporter importer =
            AssetImporter.GetAtPath(EggTexturePath) as TextureImporter;

        if (importer == null)
        {
            throw new MissingReferenceException(
                $"Missing egg atlas texture at {EggTexturePath}.");
        }

        importer.wrapMode = TextureWrapMode.Clamp;
        importer.sRGBTexture = true;
        importer.SaveAndReimport();
    }

    private static void ConfigureEggPalette(
        string prefabPath,
        bool cosmicVisualPrefab,
        Material atlas,
        Material legendary,
        Material cosmic)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        try
        {
            ChickenEgg egg = root.GetComponent<ChickenEgg>();

            if (egg == null)
            {
                egg = root.AddComponent<ChickenEgg>();
            }

            SerializedObject serializedEgg = new SerializedObject(egg);
            SerializedProperty materials =
                serializedEgg.FindProperty("typeMaterials");
            materials.arraySize = 5;
            materials.GetArrayElementAtIndex(0).objectReferenceValue = atlas;
            materials.GetArrayElementAtIndex(1).objectReferenceValue = atlas;
            materials.GetArrayElementAtIndex(2).objectReferenceValue = atlas;
            materials.GetArrayElementAtIndex(3).objectReferenceValue = legendary;
            materials.GetArrayElementAtIndex(4).objectReferenceValue = cosmic;
            serializedEgg.FindProperty("cosmicVisualPrefab").boolValue =
                cosmicVisualPrefab;
            serializedEgg.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static GameObject CreateCosmicEggPrefab(
        GameObject commonPrefab,
        GameObject cosmicVfx,
        Material cosmicMaterial)
    {
        GameObject root =
            (GameObject)PrefabUtility.InstantiatePrefab(commonPrefab);
        root.name = "prefab_egg_cosmic";

        try
        {
            GameObject vfx =
                (GameObject)PrefabUtility.InstantiatePrefab(
                    cosmicVfx,
                    root.transform);
            vfx.name = cosmicVfx.name;
            vfx.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            vfx.transform.localScale = Vector3.one;

            foreach (MeshRenderer renderer
                in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.transform.IsChildOf(vfx.transform))
                {
                    renderer.sharedMaterial = cosmicMaterial;
                }
            }

            return PrefabUtility.SaveAsPrefabAsset(
                root,
                CosmicEggPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void WireCosmicEggIntoChicken(GameObject cosmicPrefab)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ChickenPrefabPath);

        try
        {
            ChickenController chicken = root.GetComponent<ChickenController>();

            if (chicken == null)
            {
                throw new MissingComponentException(nameof(ChickenController));
            }

            SerializedObject serializedChicken = new SerializedObject(chicken);
            serializedChicken.FindProperty("cosmicEggPrefab").objectReferenceValue =
                cosmicPrefab;
            serializedChicken.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, ChickenPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ValidateEggPalette(
        GameObject prefab,
        bool expectsCosmicVisual,
        Material atlas,
        Material legendary,
        Material cosmic)
    {
        ChickenEgg egg = prefab.GetComponent<ChickenEgg>();

        if (egg == null)
        {
            throw new MissingComponentException(nameof(ChickenEgg));
        }

        SerializedObject serializedEgg = new SerializedObject(egg);
        SerializedProperty materials =
            serializedEgg.FindProperty("typeMaterials");
        Object[] expected = { atlas, atlas, atlas, legendary, cosmic };

        if (materials == null || materials.arraySize != expected.Length)
        {
            throw new InvalidOperationException(
                $"{prefab.name} does not have the five-type egg material palette.");
        }

        for (int index = 0; index < expected.Length; index++)
        {
            if (materials.GetArrayElementAtIndex(index).objectReferenceValue
                != expected[index])
            {
                throw new MissingReferenceException(
                    $"{prefab.name} egg material {index} is incorrect.");
            }
        }

        if (serializedEgg.FindProperty("cosmicVisualPrefab").boolValue
            != expectsCosmicVisual)
        {
            throw new InvalidOperationException(
                $"{prefab.name} has the wrong cosmic pooling identity.");
        }
    }

    private static T LoadRequiredAsset<T>(string path)
        where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);

        if (asset == null)
        {
            throw new MissingReferenceException(
                $"Missing required asset at {path}.");
        }

        return asset;
    }
}
