using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class EggCollectionPrefabSetup
{
    private const string RootFolder = "Assets/Collection";
    private const string PrefabFolder = RootFolder + "/prefabs";
    private const string MaterialFolder = RootFolder + "/materials";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string EggModelPath =
        "Assets/Eggs/meshes/egg_chicken.fbx";
    private const string EggMaterialPath =
        "Assets/Eggs/materials/mat_egg_atlas.mat";

    [MenuItem("Tools/Eggcessive/Rebuild Egg Collection Prefabs")]
    public static void Generate()
    {
        EnsureFolders();
        Material basketMaterial = CreateMaterial(
            "mat_Basket",
            new Color(0.55f, 0.25f, 0.07f));
        Material basketRimMaterial = CreateMaterial(
            "mat_BasketRim",
            new Color(0.82f, 0.48f, 0.12f));
        Material eggMaterial = CreateMaterial(
            "mat_CollectorEgg",
            new Color(1f, 0.94f, 0.72f));
        Material vacuumMaterial = CreateMaterial(
            "mat_Vacuum",
            new Color(0.14f, 0.42f, 0.68f),
            0.35f);
        Material vacuumAccentMaterial = CreateMaterial(
            "mat_VacuumAccent",
            new Color(0.25f, 0.82f, 0.95f),
            0.55f);
        Material darkMaterial = CreateMaterial(
            "mat_CollectorDark",
            new Color(0.055f, 0.065f, 0.075f),
            0.2f);
        Material boxMaterial = CreateMaterial(
            "mat_CollectorBox",
            new Color(0.92f, 0.57f, 0.12f));
        GameObject eggModel = AssetDatabase.LoadAssetAtPath<GameObject>(
            EggModelPath);
        Material actualEggMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            EggMaterialPath);

        if (eggModel == null || actualEggMaterial == null)
        {
            throw new MissingReferenceException(
                "The authored chicken egg model or material is missing.");
        }

        GameObject[] basketPrefabs = new GameObject[3];
        GameObject[] vacuumPrefabs = new GameObject[3];
        GameObject[] robotPrefabs = new GameObject[3];

        for (int tier = 0; tier < 3; tier++)
        {
            basketPrefabs[tier] = CreateBasketPrefab(
                tier,
                tier + 3,
                basketMaterial,
                basketRimMaterial,
                eggModel,
                actualEggMaterial);
            vacuumPrefabs[tier] = CreateVacuumPrefab(
                tier,
                vacuumMaterial,
                vacuumAccentMaterial,
                darkMaterial);
            robotPrefabs[tier] = CreateRobotPrefab(
                tier,
                new[] { 6, 12, 24 }[tier],
                darkMaterial,
                tier == 2 ? vacuumAccentMaterial : vacuumMaterial,
                boxMaterial,
                eggModel,
                actualEggMaterial);
        }

        WirePrefabsIntoScene(basketPrefabs, vacuumPrefabs, robotPrefabs);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Basket, vacuum, and robot collector prefabs rebuilt and wired.");
    }

    private static GameObject CreateBasketPrefab(
        int tier,
        int capacity,
        Material basketMaterial,
        Material rimMaterial,
        GameObject eggModel,
        Material eggMaterial)
    {
        GameObject root = new GameObject($"prefab_EggBasket_T{tier + 1}");
        root.transform.localScale = Vector3.one * 0.5f;
        float scale = 1f + tier * 0.08f;
        CreatePrimitive(
            "Basket Body",
            PrimitiveType.Cylinder,
            root.transform,
            new Vector3(0f, 0.13f, 0f),
            Vector3.zero,
            new Vector3(0.3f * scale, 0.12f, 0.3f * scale),
            basketMaterial);
        CreatePrimitive(
            "Basket Rim",
            PrimitiveType.Cylinder,
            root.transform,
            new Vector3(0f, 0.28f, 0f),
            Vector3.zero,
            new Vector3(0.34f * scale, 0.035f, 0.34f * scale),
            rimMaterial);
        CreatePrimitive(
            "Second Hand",
            PrimitiveType.Capsule,
            root.transform,
            new Vector3(0.34f, 0.3f, 0.08f),
            new Vector3(0f, 0f, -55f),
            new Vector3(0.08f, 0.18f, 0.08f),
            rimMaterial);

        for (int index = 0; index < capacity; index++)
        {
            float angle = index * Mathf.PI * 2f / capacity;
            Vector3 position = new Vector3(
                Mathf.Cos(angle) * 0.17f,
                0.35f,
                Mathf.Sin(angle) * 0.17f);
            GameObject egg = (GameObject)PrefabUtility.InstantiatePrefab(
                eggModel);
            egg.name = $"Egg Slot {index + 1}";
            egg.transform.SetParent(root.transform, false);
            egg.transform.localPosition = position;
            egg.transform.localRotation = Quaternion.identity;

            // The basket root is half scale. Compensating here keeps each
            // displayed egg at exactly the laid egg model's world scale.
            egg.transform.localScale = Vector3.one * 2f;

            foreach (Renderer renderer in egg.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = eggMaterial;
            }

            egg.SetActive(false);
        }

        string path = $"{PrefabFolder}/prefab_EggBasket_T{tier + 1}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject CreateVacuumPrefab(
        int tier,
        Material bodyMaterial,
        Material accentMaterial,
        Material darkMaterial)
    {
        GameObject root = new GameObject($"prefab_EggVacuum_T{tier + 1}");
        root.transform.localScale = Vector3.one * 0.75f;
        GameObject visuals = new GameObject("Vacuum Visuals");
        visuals.transform.SetParent(root.transform, false);
        visuals.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
        float scale = 1f + tier * 0.1f;
        CreatePrimitive(
            "Vacuum Body",
            PrimitiveType.Cylinder,
            visuals.transform,
            new Vector3(0f, 0.24f, -0.2f),
            new Vector3(90f, 0f, 0f),
            new Vector3(0.14f * scale, 0.25f, 0.14f * scale),
            bodyMaterial);
        CreatePrimitive(
            "Suction Nozzle",
            PrimitiveType.Cylinder,
            visuals.transform,
            new Vector3(0f, 0.13f, 0.18f),
            new Vector3(90f, 0f, 0f),
            new Vector3(0.12f + tier * 0.025f, 0.18f, 0.12f + tier * 0.025f),
            accentMaterial);
        CreatePrimitive(
            "Handle",
            PrimitiveType.Cube,
            visuals.transform,
            new Vector3(0.24f, 0.35f, -0.28f),
            new Vector3(0f, 0f, -28f),
            new Vector3(0.055f, 0.3f, 0.055f),
            darkMaterial);

        for (int ring = 0; ring <= tier; ring++)
        {
            CreatePrimitive(
                $"Power Ring {ring + 1}",
                PrimitiveType.Cylinder,
                visuals.transform,
                new Vector3(0f, 0.24f, -0.05f - ring * 0.09f),
                new Vector3(90f, 0f, 0f),
                new Vector3(0.16f * scale, 0.018f, 0.16f * scale),
                accentMaterial);
        }

        string path = $"{PrefabFolder}/prefab_EggVacuum_T{tier + 1}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject CreateRobotPrefab(
        int tier,
        int capacity,
        Material darkMaterial,
        Material bodyMaterial,
        Material boxMaterial,
        GameObject eggModel,
        Material eggMaterial)
    {
        GameObject root = new GameObject($"prefab_EggCollectorRobot_T{tier + 1}");
        root.transform.localScale = Vector3.one * 0.68f;
        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.agentTypeID = -1180031551;
        agent.radius = 0.13f;
        agent.height = 0.2f;
        agent.baseOffset = 0f;
        agent.stoppingDistance = 0.08f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        EggCollectorRobot robot = root.AddComponent<EggCollectorRobot>();

        CreatePrimitive(
            "Roomba Base",
            PrimitiveType.Cylinder,
            root.transform,
            new Vector3(0f, 0.1f, 0f),
            Vector3.zero,
            new Vector3(0.34f, 0.09f, 0.34f),
            bodyMaterial);
        CreatePrimitive(
            "Rubber Bumper",
            PrimitiveType.Cylinder,
            root.transform,
            new Vector3(0f, 0.1f, 0f),
            Vector3.zero,
            new Vector3(0.37f, 0.055f, 0.37f),
            darkMaterial);
        CreatePrimitive(
            "Top Plate",
            PrimitiveType.Cylinder,
            root.transform,
            new Vector3(0f, 0.2f, 0f),
            Vector3.zero,
            new Vector3(0.31f, 0.035f, 0.31f),
            bodyMaterial);
        CreatePrimitive(
            "Front Sensor",
            PrimitiveType.Sphere,
            root.transform,
            new Vector3(0f, 0.18f, 0.31f),
            Vector3.zero,
            new Vector3(0.075f, 0.055f, 0.04f),
            tier == 2 ? boxMaterial : darkMaterial);

        Transform box = new GameObject("Visible Egg Box").transform;
        box.SetParent(root.transform, false);
        CreatePrimitive(
            "Box Floor",
            PrimitiveType.Cube,
            box,
            new Vector3(0f, 0.29f, -0.03f),
            Vector3.zero,
            new Vector3(0.26f, 0.025f, 0.21f),
            boxMaterial);
        CreateBoxWall(box, new Vector3(0.28f, 0.37f, -0.03f), new Vector3(0.025f, 0.1f, 0.23f), boxMaterial);
        CreateBoxWall(box, new Vector3(-0.28f, 0.37f, -0.03f), new Vector3(0.025f, 0.1f, 0.23f), boxMaterial);
        CreateBoxWall(box, new Vector3(0f, 0.37f, 0.2f), new Vector3(0.28f, 0.1f, 0.025f), boxMaterial);
        CreateBoxWall(box, new Vector3(0f, 0.37f, -0.26f), new Vector3(0.28f, 0.1f, 0.025f), boxMaterial);

        var slots = new List<Transform>();

        for (int index = 0; index < capacity; index++)
        {
            int row = index / 4;
            int column = index % 4;
            GameObject egg = (GameObject)PrefabUtility.InstantiatePrefab(
                eggModel);
            egg.name = $"Egg Slot {index + 1}";
            egg.transform.SetParent(box, false);
            egg.transform.localPosition = new Vector3(
                -0.18f + column * 0.12f,
                0.41f + row * 0.055f,
                -0.13f + row * 0.12f);
            egg.transform.localRotation = Quaternion.Euler(
                0f,
                index * 47f,
                0f);
            // Compensate for the smaller robot root so the displayed cargo
            // remains the same physical size as eggs in the pen.
            egg.transform.localScale = Vector3.one / root.transform.localScale.x;

            foreach (Renderer renderer in egg.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = eggMaterial;
            }

            egg.SetActive(false);
            slots.Add(egg.transform);
        }

        SerializedObject serializedRobot = new SerializedObject(robot);
        serializedRobot.FindProperty("pickupDistance").floatValue = 0.32f;
        serializedRobot.FindProperty("deliveryDistance").floatValue = 0.38f;
        serializedRobot.FindProperty("targetRefreshInterval").floatValue = 0.12f;
        serializedRobot.FindProperty("targetNavMeshTolerance").floatValue = 0.28f;
        SerializedProperty slotsProperty = serializedRobot.FindProperty("visibleEggSlots");
        slotsProperty.arraySize = slots.Count;

        for (int index = 0; index < slots.Count; index++)
        {
            slotsProperty.GetArrayElementAtIndex(index).objectReferenceValue = slots[index];
        }

        serializedRobot.ApplyModifiedPropertiesWithoutUndo();
        string path = $"{PrefabFolder}/prefab_EggCollectorRobot_T{tier + 1}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void WirePrefabsIntoScene(
        GameObject[] basketPrefabs,
        GameObject[] vacuumPrefabs,
        GameObject[] robotPrefabs)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EggCarryController controller = Object.FindFirstObjectByType<EggCarryController>(
            FindObjectsInactive.Include);

        if (controller == null)
        {
            throw new MissingComponentException(nameof(EggCarryController));
        }

        SerializedObject serializedController = new SerializedObject(controller);
        AssignPrefabArray(
            serializedController.FindProperty("basketPrefabs"),
            basketPrefabs);
        AssignPrefabArray(
            serializedController.FindProperty("vacuumPrefabs"),
            vacuumPrefabs);
        AssignPrefabArray(
            serializedController.FindProperty("robotPrefabs"),
            robotPrefabs);
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
        EditorSceneManager.SaveScene(scene);
    }

    private static void AssignPrefabArray(
        SerializedProperty property,
        GameObject[] prefabs)
    {
        property.arraySize = prefabs.Length;

        for (int index = 0; index < prefabs.Length; index++)
        {
            property.GetArrayElementAtIndex(index).objectReferenceValue = prefabs[index];
        }
    }

    private static GameObject CreatePrimitive(
        string objectName,
        PrimitiveType primitiveType,
        Transform parent,
        Vector3 position,
        Vector3 rotation,
        Vector3 scale,
        Material material)
    {
        GameObject primitive = GameObject.CreatePrimitive(primitiveType);
        primitive.name = objectName;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = position;
        primitive.transform.localEulerAngles = rotation;
        primitive.transform.localScale = scale;
        Collider collider = primitive.GetComponent<Collider>();

        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        primitive.GetComponent<Renderer>().sharedMaterial = material;
        return primitive;
    }

    private static void CreateBoxWall(
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material)
    {
        CreatePrimitive(
            "Box Wall",
            PrimitiveType.Cube,
            parent,
            position,
            Vector3.zero,
            scale,
            material);
    }

    private static Material CreateMaterial(
        string materialName,
        Color color,
        float metallic = 0f)
    {
        string path = $"{MaterialFolder}/{materialName}.mat";
        AssetDatabase.DeleteAsset(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader)
        {
            name = materialName,
            color = color
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.3f + metallic * 0.45f);
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(RootFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Collection");
        }

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            AssetDatabase.CreateFolder(RootFolder, "prefabs");
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder(RootFolder, "materials");
        }
    }
}
