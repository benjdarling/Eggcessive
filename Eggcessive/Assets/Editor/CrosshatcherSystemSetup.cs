using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CrosshatcherSystemSetup
{
    private const string ModelPath =
        "Assets/Crosshatcher/meshes/crosshatcher.fbx";
    private const string PrefabPath =
        "Assets/Crosshatcher/prefabs/prefab_Crosshatcher.prefab";
    private const string ChickenPrefabPath =
        "Assets/Chicken/prefabs/prefab_chicken.prefab";
    private const string ChickenTexturePath =
        "Assets/Eggs/textures/t_chicken.psd";
    private const string HudPrefabPath =
        "Assets/UI/prefab_EggScoreHud.prefab";
    private const string ScenePath =
        "Assets/Scenes/SampleScene.unity";
    private const string FontPath =
        "Assets/Fonts/Cat Song SDF.asset";
    private const string ProcessingLoopSfxPath =
        "Assets/Sounds/UI/sfx_incubator_on.wav";
    private const string HatchDoneSfxPath =
        "Assets/Sounds/UI/sfx_incubator_done.wav";

    [MenuItem("Tools/Eggcessive/Build Crosshatcher System")]
    public static void BuildCrosshatcherSystem()
    {
        EnsureFolder("Assets/Crosshatcher", "prefabs");
        ConfigureChickenAtlas();
        GameObject prefab = CreatePrefab();
        ConfigureHudController();
        PlaceAndConnectScene(prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateConfiguredAssets();
        Debug.Log(
            "Crosshatcher prefab, chicken breed atlas, shop controller, and scene placement configured.");
    }

    [MenuItem("Tools/Eggcessive/Validate Crosshatcher System")]
    public static void ValidateConfiguredAssets()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            throw new MissingReferenceException(
                $"Missing crosshatcher prefab at {PrefabPath}.");
        }

        CrosshatcherController controller =
            prefab.GetComponent<CrosshatcherController>();
        CrosshatcherChickenIntake intake =
            prefab.GetComponentInChildren<CrosshatcherChickenIntake>(true);
        BoxCollider clickTarget = prefab.GetComponent<BoxCollider>();
        Transform model = prefab.transform.Find("Crosshatcher Mesh");
        UnityEngine.Object modelSource = model != null
            ? PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject)
            : null;

        if (controller == null
            || intake == null
            || clickTarget == null
            || !clickTarget.isTrigger
            || intake.GetComponent<Rigidbody>() == null
            || !intake.GetComponent<Rigidbody>().isKinematic
            || prefab.GetComponentsInChildren<TextMeshPro>(true).Length != 1)
        {
            throw new InvalidOperationException(
                "The crosshatcher prefab is missing its controller, intake, click target, or timer.");
        }

        if (modelSource == null || AssetDatabase.GetAssetPath(modelSource) != ModelPath)
        {
            throw new InvalidOperationException(
                "The crosshatcher mesh is not retained as a nested FBX prefab.");
        }

        SerializedObject serializedController = new SerializedObject(controller);
        ValidateReference(serializedController, "chickenStartOne");
        ValidateReference(serializedController, "chickenStartTwo");
        ValidateReference(serializedController, "chickenEnd");
        ValidateReference(serializedController, "chickenSpawn");
        ValidateReference(serializedController, "chickenDestination");
        ValidateReference(serializedController, "timerText");
        ValidateReference(serializedController, "chickenPrefab");
        ValidateReference(serializedController, "processingLoopSfx");
        ValidateReference(serializedController, "hatchDoneSfx");
        ValidateReference(new SerializedObject(intake), "crosshatcher");

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform location = FindSceneTransform(scene, "Location_Crosshatcher");
        Transform placed = location != null
            ? location.Find("prefab_Crosshatcher")
            : null;
        CrosshatcherController placedController = placed != null
            ? placed.GetComponent<CrosshatcherController>()
            : null;
        CrosshatcherShopController shop =
            FindSceneComponent<CrosshatcherShopController>(scene);

        if (placedController == null
            || placedController.gameObject.activeSelf
            || shop == null)
        {
            throw new InvalidOperationException(
                "SampleScene is missing the inactive crosshatcher or its shop controller.");
        }

        SerializedObject serializedShop = new SerializedObject(shop);

        if (serializedShop.FindProperty("crosshatcher").objectReferenceValue
            != placedController)
        {
            throw new InvalidOperationException(
                "The scene shop controller is not connected to the crosshatcher.");
        }

        ThrowIfMissingScripts(prefab);
        Debug.Log("Crosshatcher validation passed.");
    }

    private static GameObject CreatePrefab()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        GameObject chickenPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefabPath);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        AudioClip processingLoopSfx =
            AssetDatabase.LoadAssetAtPath<AudioClip>(
                ProcessingLoopSfxPath);
        AudioClip hatchDoneSfx =
            AssetDatabase.LoadAssetAtPath<AudioClip>(
                HatchDoneSfxPath);

        if (modelAsset == null
            || chickenPrefab == null
            || font == null
            || processingLoopSfx == null
            || hatchDoneSfx == null)
        {
            throw new InvalidOperationException(
                "The crosshatcher model, chicken prefab, UI font, or incubator audio is missing.");
        }

        GameObject root = new GameObject("prefab_Crosshatcher");

        try
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            model.name = "Crosshatcher Mesh";
            model.transform.SetParent(root.transform, false);

            Transform timerSocket = FindRequired(
                model.transform,
                "SOCKET_crosshatcher_timer");
            Transform startOne = FindRequired(
                model.transform,
                "SOCKET_crosshatcher_start_chicken1");
            Transform startTwo = FindRequired(
                model.transform,
                "SOCKET_crosshatcher_start_chicken2");
            Transform end = FindRequired(
                model.transform,
                "SOCKET_crosshatcher_end");
            Transform spawn = FindRequired(
                model.transform,
                "SOCKET_crosshatcher_chicken_spawn");
            Transform destination = FindRequired(
                model.transform,
                "SOCKET_crosshatcher_chicken_destination");

            TextMeshPro timerText = CreateWorldText(
                "Crosshatcher Timer",
                timerSocket,
                font);
            CrosshatcherController controller =
                root.AddComponent<CrosshatcherController>();
            ConfigureClickTarget(root, model);
            CreateIntake(root, controller, startOne, startTwo);

            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("chickenStartOne").objectReferenceValue =
                startOne;
            serialized.FindProperty("chickenStartTwo").objectReferenceValue =
                startTwo;
            serialized.FindProperty("chickenEnd").objectReferenceValue = end;
            serialized.FindProperty("chickenSpawn").objectReferenceValue = spawn;
            serialized.FindProperty("chickenDestination").objectReferenceValue =
                destination;
            serialized.FindProperty("timerText").objectReferenceValue = timerText;
            serialized.FindProperty("chickenPrefab").objectReferenceValue =
                chickenPrefab;
            serialized.FindProperty("processingLoopSfx").objectReferenceValue =
                processingLoopSfx;
            serialized.FindProperty("hatchDoneSfx").objectReferenceValue =
                hatchDoneSfx;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab =
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            LogBounds(model);
            return prefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void CreateIntake(
        GameObject root,
        CrosshatcherController controller,
        Transform startOne,
        Transform startTwo)
    {
        GameObject intakeObject = new GameObject("Chicken Intake Trigger");
        intakeObject.transform.SetParent(root.transform, false);
        Vector3 localOne = root.transform.InverseTransformPoint(startOne.position);
        Vector3 localTwo = root.transform.InverseTransformPoint(startTwo.position);
        intakeObject.transform.localPosition = (localOne + localTwo) * 0.5f;
        BoxCollider collider = intakeObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        Rigidbody body = intakeObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        Vector3 separation = new Vector3(
            Mathf.Abs(localOne.x - localTwo.x),
            Mathf.Abs(localOne.y - localTwo.y),
            Mathf.Abs(localOne.z - localTwo.z));
        collider.size = separation + new Vector3(0.55f, 0.5f, 0.55f);
        CrosshatcherChickenIntake intake =
            intakeObject.AddComponent<CrosshatcherChickenIntake>();
        SerializedObject serialized = new SerializedObject(intake);
        serialized.FindProperty("crosshatcher").objectReferenceValue = controller;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureHudController()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HudPrefabPath);

        try
        {
            if (root.GetComponent<CrosshatcherShopController>() == null)
            {
                root.AddComponent<CrosshatcherShopController>();
            }

            PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void PlaceAndConnectScene(GameObject prefab)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform incubatorLocation =
            FindSceneTransform(scene, "Location_Incubator");
        Transform location =
            FindSceneTransform(scene, "Location_Crosshatcher");

        if (incubatorLocation == null)
        {
            throw new InvalidOperationException(
                "SampleScene does not contain Location_Incubator.");
        }

        if (location == null)
        {
            GameObject locationObject = new GameObject("Location_Crosshatcher");
            location = locationObject.transform;
            location.SetPositionAndRotation(
                incubatorLocation.position + Vector3.right * 2.4f,
                incubatorLocation.rotation);
        }

        Transform existing = location.Find("prefab_Crosshatcher");

        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        GameObject machine =
            (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        machine.transform.SetParent(location, false);
        machine.transform.localPosition = Vector3.zero;
        machine.transform.localRotation = Quaternion.identity;
        machine.transform.localScale = Vector3.one;
        CrosshatcherController controller =
            machine.GetComponent<CrosshatcherController>();
        machine.SetActive(false);

        CrosshatcherShopController shop =
            FindSceneComponent<CrosshatcherShopController>(scene);

        if (shop == null)
        {
            throw new InvalidOperationException(
                "The Egg Score HUD does not contain CrosshatcherShopController.");
        }

        SerializedObject serialized = new SerializedObject(shop);
        serialized.FindProperty("crosshatcher").objectReferenceValue = controller;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void ConfigureChickenAtlas()
    {
        TextureImporter importer =
            AssetImporter.GetAtPath(ChickenTexturePath) as TextureImporter;

        if (importer == null)
        {
            throw new MissingReferenceException(
                $"Missing chicken texture at {ChickenTexturePath}.");
        }

        importer.wrapMode = TextureWrapMode.Clamp;
        importer.sRGBTexture = true;
        importer.SaveAndReimport();
    }

    private static TextMeshPro CreateWorldText(
        string objectName,
        Transform parent,
        TMP_FontAsset font)
    {
        GameObject textObject = new GameObject(objectName, typeof(TextMeshPro));
        textObject.transform.SetParent(parent, false);
        TextMeshPro text = textObject.GetComponent<TextMeshPro>();
        text.font = font;
        text.fontSharedMaterial = font.material;
        text.text = "STANDBY\n0/2";
        text.fontSize = 3f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.fontStyle = FontStyles.Bold;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.rectTransform.sizeDelta = new Vector2(3.2f, 1f);
        text.rectTransform.localPosition = Vector3.zero;
        text.rectTransform.localRotation = Quaternion.identity;
        text.rectTransform.localScale = Vector3.one * 0.055f;
        return text;
    }

    private static void ConfigureClickTarget(GameObject root, GameObject model)
    {
        Bounds bounds = CalculateLocalBounds(root.transform, model);
        BoxCollider clickTarget = root.AddComponent<BoxCollider>();
        clickTarget.isTrigger = true;
        clickTarget.center = bounds.center;
        clickTarget.size = bounds.size + Vector3.one * 0.08f;
    }

    private static Bounds CalculateLocalBounds(
        Transform root,
        GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
        {
            throw new InvalidOperationException(
                "The crosshatcher model has no renderers.");
        }

        Bounds bounds = new Bounds(
            root.InverseTransformPoint(renderers[0].bounds.center),
            Vector3.zero);

        foreach (Renderer renderer in renderers)
        {
            Vector3 center = renderer.bounds.center;
            Vector3 extents = renderer.bounds.extents;

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                bounds.Encapsulate(root.InverseTransformPoint(
                    center + Vector3.Scale(
                        extents,
                        new Vector3(x, y, z))));
            }
        }

        return bounds;
    }

    private static Transform FindRequired(Transform root, string objectName)
    {
        Transform found = FindTransform(root, objectName);

        if (found == null)
        {
            throw new InvalidOperationException(
                $"The crosshatcher model is missing required socket '{objectName}'.");
        }

        return found;
    }

    private static Transform FindTransform(Transform root, string objectName)
    {
        if (root.name == objectName)
        {
            return root;
        }

        for (int index = 0; index < root.childCount; index++)
        {
            Transform found = FindTransform(root.GetChild(index), objectName);

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Transform FindSceneTransform(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = FindTransform(root.transform, objectName);

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static T FindSceneComponent<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);

            if (component != null)
            {
                return component;
            }
        }

        return null;
    }

    private static void ValidateReference(
        SerializedObject serialized,
        string propertyName)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);

        if (property == null || property.objectReferenceValue == null)
        {
            throw new MissingReferenceException(
                $"{serialized.targetObject.name} is missing '{propertyName}'.");
        }
    }

    private static void ThrowIfMissingScripts(GameObject root)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    child.gameObject) > 0)
            {
                throw new MissingComponentException(
                    $"{child.name} below {root.name} has a missing script.");
            }
        }
    }

    private static void EnsureFolder(string parent, string folder)
    {
        string path = $"{parent}/{folder}";

        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, folder);
        }
    }

    private static void LogBounds(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;

        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        Debug.Log(
            $"Crosshatcher authored bounds: center {bounds.center}, size {bounds.size}.");
    }
}
