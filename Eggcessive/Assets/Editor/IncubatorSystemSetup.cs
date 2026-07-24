using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class IncubatorSystemSetup
{
    private const string IncubatorModelPath = "Assets/Incubator/meshes/incubator.fbx";
    private const string IncubatorPrefabPath = "Assets/Incubator/prefabs/prefab_Incubator.prefab";
    private const string ChickenPrefabPath = "Assets/Chicken/prefabs/prefab_chicken.prefab";
    private const string HudPrefabPath = "Assets/UI/prefab_EggScoreHud.prefab";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string FontPath = "Assets/Fonts/Chewy-Regular SDF.asset";

    [MenuItem("Tools/Eggcessive/Build Incubator System")]
    public static void BuildIncubatorSystem()
    {
        EnsureFolder("Assets/Incubator", "prefabs");
        GameObject incubatorPrefab = CreateIncubatorPrefab();
        ConfigureHudPrefab();
        PlaceAndConnectScene(incubatorPrefab);
        ValidateConfiguredAssets();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Incubator prefab, HUD shop, and SampleScene placement are fully configured.");
    }

    [MenuItem("Tools/Eggcessive/Validate Incubator System")]
    public static void ValidateConfiguredAssets()
    {
        GameObject incubatorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(IncubatorPrefabPath);
        GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);

        if (incubatorPrefab == null || hudPrefab == null)
        {
            throw new InvalidOperationException("The incubator or HUD prefab could not be loaded.");
        }

        IncubatorController prefabController = incubatorPrefab.GetComponent<IncubatorController>();
        IncubatorEggIntake intake = incubatorPrefab.GetComponentInChildren<IncubatorEggIntake>(true);
        Transform nestedModel = incubatorPrefab.transform.Find("Incubator Mesh");
        UnityEngine.Object modelSource = nestedModel != null
            ? PrefabUtility.GetCorrespondingObjectFromSource(nestedModel.gameObject)
            : null;

        if (prefabController == null
            || intake == null
            || !intake.GetComponent<Collider>().isTrigger
            || incubatorPrefab.GetComponentsInChildren<TextMeshPro>(true).Length != 2)
        {
            throw new InvalidOperationException("The incubator prefab is missing its controller, intake, or authored TMP displays.");
        }

        if (modelSource == null || AssetDatabase.GetAssetPath(modelSource) != IncubatorModelPath)
        {
            throw new InvalidOperationException("The incubator mesh is not retained as a nested FBX prefab.");
        }

        SerializedObject serializedIncubator = new SerializedObject(prefabController);
        ValidateReference(serializedIncubator, "eggStart");
        ValidateReference(serializedIncubator, "eggEnd");
        ValidateReference(serializedIncubator, "chickenStart");
        ValidateReference(serializedIncubator, "chickenEnd");
        ValidateReference(serializedIncubator, "capacityText");
        ValidateReference(serializedIncubator, "timerText");
        ValidateReference(serializedIncubator, "chickenPrefab");
        ValidateReference(new SerializedObject(intake), "incubator");

        IncubatorShopController prefabShop = hudPrefab.GetComponent<IncubatorShopController>();

        if (prefabShop == null)
        {
            throw new InvalidOperationException("The HUD prefab is missing IncubatorShopController.");
        }

        SerializedObject serializedPrefabShop = new SerializedObject(prefabShop);
        ValidateReference(serializedPrefabShop, "purchaseButton");
        ValidateReference(serializedPrefabShop, "levelText");
        ValidateReference(serializedPrefabShop, "detailsText");
        ValidateReference(serializedPrefabShop, "statusText");
        ValidateReference(serializedPrefabShop, "purchaseButtonText");
        ValidateReference(serializedPrefabShop, "affordabilityProgressFill");

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform location = FindSceneTransform(scene, "Location_Incubator");
        IncubatorShopController sceneShop = FindSceneComponent<IncubatorShopController>(scene);
        Transform placedTransform = location != null ? location.Find("prefab_Incubator") : null;
        IncubatorController placedIncubator = placedTransform != null
            ? placedTransform.GetComponent<IncubatorController>()
            : null;

        if (placedIncubator == null || placedIncubator.gameObject.activeSelf || sceneShop == null)
        {
            throw new InvalidOperationException("SampleScene is missing the inactive incubator placement or connected HUD.");
        }

        SerializedObject serializedSceneShop = new SerializedObject(sceneShop);

        if (serializedSceneShop.FindProperty("incubator").objectReferenceValue != placedIncubator)
        {
            throw new InvalidOperationException("The scene HUD does not reference the placed incubator.");
        }

        ThrowIfMissingScripts(incubatorPrefab);
        ThrowIfMissingScripts(hudPrefab);

        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            ThrowIfMissingScripts(sceneRoot);
        }

        Debug.Log("Incubator validation passed: prefab, TMP displays, HUD, scene placement, and serialized references are valid.");
    }

    private static GameObject CreateIncubatorPrefab()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(IncubatorModelPath);
        GameObject chickenPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefabPath);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        if (modelAsset == null || chickenPrefab == null || font == null)
        {
            throw new InvalidOperationException("The incubator model, chicken prefab, or TMP font asset is missing.");
        }

        GameObject root = new GameObject("prefab_Incubator");

        try
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            model.name = "Incubator Mesh";
            model.transform.SetParent(root.transform, false);

            Transform eggStart = FindRequired(model.transform, "SOCKET_incubator_start");
            Transform eggEnd = FindRequired(model.transform, "SOCKET_incubator_end");
            Transform chickenStart = FindRequired(model.transform, "SOCKET_incubator_chicken_start");
            Transform chickenEnd = eggEnd;
            Transform capacitySocket = FindRequired(model.transform, "SOCKET_incubator_capacity");
            Transform timerSocket = FindRequired(model.transform, "SOCKET_incubator_timer");

            TextMeshPro capacityText = CreateWorldText(
                "Capacity Text",
                capacitySocket,
                font,
                "0/1",
                new Color(1f, 0.88f, 0.35f, 1f));
            TextMeshPro timerText = CreateWorldText(
                "Timer Text",
                timerSocket,
                font,
                "--:--",
                new Color(0.5f, 1f, 0.8f, 1f));

            IncubatorController controller = root.AddComponent<IncubatorController>();

            GameObject intakeObject = new GameObject("Egg Intake Trigger");
            intakeObject.transform.SetParent(eggStart, false);
            BoxCollider intake = intakeObject.AddComponent<BoxCollider>();
            intake.isTrigger = true;
            intake.center = Vector3.zero;
            intake.size = new Vector3(0.42f, 0.36f, 0.42f);
            IncubatorEggIntake intakeRelay = intakeObject.AddComponent<IncubatorEggIntake>();
            SerializedObject serializedIntake = new SerializedObject(intakeRelay);
            serializedIntake.FindProperty("incubator").objectReferenceValue = controller;
            serializedIntake.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject serializedController = new SerializedObject(controller);
            SetLevel(serializedController.FindProperty("levelOne"), 1, 30f);
            SetLevel(serializedController.FindProperty("levelTwo"), 3, 20f);
            serializedController.FindProperty("currentLevel").intValue = 1;
            serializedController.FindProperty("eggStart").objectReferenceValue = eggStart;
            serializedController.FindProperty("eggEnd").objectReferenceValue = eggEnd;
            serializedController.FindProperty("chickenStart").objectReferenceValue = chickenStart;
            serializedController.FindProperty("chickenEnd").objectReferenceValue = chickenEnd;
            serializedController.FindProperty("capacityText").objectReferenceValue = capacityText;
            serializedController.FindProperty("timerText").objectReferenceValue = timerText;
            serializedController.FindProperty("chickenPrefab").objectReferenceValue = chickenPrefab;
            serializedController.FindProperty("eggTravelDuration").floatValue = 0.65f;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, IncubatorPrefabPath);
            LogModelLayout(model);
            return prefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ConfigureHudPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HudPrefabPath);

        try
        {
            Transform panel = root.transform.Find("Right HUD Panel");
            TMP_Text scoreText = root.GetComponent<EggScoreHud>()
                .GetComponentInChildren<TMP_Text>(true);

            if (panel == null || scoreText == null)
            {
                throw new InvalidOperationException("The HUD panel or its TMP font could not be found.");
            }

            Transform existingShop = panel.Find("Incubator Shop");

            if (existingShop != null)
            {
                UnityEngine.Object.DestroyImmediate(existingShop.gameObject);
            }

            TMP_FontAsset font = scoreText.font;
            RectTransform shop = CreateUiObject("Incubator Shop", panel);
            shop.anchorMin = new Vector2(0f, 1f);
            shop.anchorMax = new Vector2(1f, 1f);
            shop.pivot = new Vector2(0.5f, 1f);
            shop.anchoredPosition = new Vector2(0f, -320f);
            shop.sizeDelta = new Vector2(0f, 250f);
            Image shopBackground = shop.gameObject.AddComponent<Image>();
            shopBackground.color = new Color(0.055f, 0.12f, 0.105f, 0.94f);
            shopBackground.raycastTarget = false;

            TextMeshProUGUI title = CreateText(
                "Title",
                shop,
                font,
                "INCUBATOR",
                23f,
                TextAlignmentOptions.Center);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(250f, 34f));

            TextMeshProUGUI levelText = CreateText(
                "Level",
                shop,
                font,
                "NOT INSTALLED",
                18f,
                TextAlignmentOptions.Center);
            levelText.color = new Color(1f, 0.86f, 0.34f, 1f);
            SetRect(levelText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -53f), new Vector2(250f, 28f));

            TextMeshProUGUI detailsText = CreateText(
                "Details",
                shop,
                font,
                "Level 1  |  1 egg  |  30 sec",
                15f,
                TextAlignmentOptions.Center);
            SetRect(detailsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -82f), new Vector2(260f, 30f));

            RectTransform progress = CreateUiObject("Affordability Progress", shop);
            SetRect(progress, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(228f, 12f));
            Image progressBackground = progress.gameObject.AddComponent<Image>();
            progressBackground.sprite = GetUiSprite();
            progressBackground.type = Image.Type.Sliced;
            progressBackground.color = new Color(0.025f, 0.045f, 0.04f, 0.95f);
            progressBackground.raycastTarget = false;

            RectTransform fillRect = CreateUiObject("Fill", progress);
            Stretch(fillRect, 2f);
            Image progressFill = fillRect.gameObject.AddComponent<Image>();
            progressFill.sprite = GetUiSprite();
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            progressFill.fillAmount = 0f;
            progressFill.color = new Color(0.2f, 0.78f, 0.48f, 1f);
            progressFill.raycastTarget = false;

            RectTransform buyRect = CreateUiObject("Purchase Button", shop);
            SetRect(buyRect, new Vector2(0.5f, 1f), new Vector2(0f, -153f), new Vector2(210f, 58f));
            Image buyBackground = buyRect.gameObject.AddComponent<Image>();
            buyBackground.sprite = GetUiSprite();
            buyBackground.type = Image.Type.Sliced;
            buyBackground.color = new Color(0.12f, 0.58f, 0.38f, 1f);
            Button purchaseButton = buyRect.gameObject.AddComponent<Button>();
            purchaseButton.targetGraphic = buyBackground;
            purchaseButton.navigation = new Navigation { mode = Navigation.Mode.None };
            SetButtonColors(purchaseButton, buyBackground.color);

            TextMeshProUGUI purchaseText = CreateText(
                "Label",
                buyRect,
                font,
                "BUY  $5.00",
                19f,
                TextAlignmentOptions.Center);
            Stretch(purchaseText.rectTransform, 4f);

            TextMeshProUGUI statusText = CreateText(
                "Status",
                shop,
                font,
                "Purchase to install at the coop",
                15f,
                TextAlignmentOptions.Center);
            statusText.color = new Color(0.65f, 1f, 0.82f, 1f);
            SetRect(statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 23f), new Vector2(250f, 40f));

            IncubatorShopController controller = root.GetComponent<IncubatorShopController>();

            if (controller == null)
            {
                controller = root.AddComponent<IncubatorShopController>();
            }

            SerializedObject serializedShop = new SerializedObject(controller);
            serializedShop.FindProperty("purchaseButton").objectReferenceValue = purchaseButton;
            serializedShop.FindProperty("levelText").objectReferenceValue = levelText;
            serializedShop.FindProperty("detailsText").objectReferenceValue = detailsText;
            serializedShop.FindProperty("statusText").objectReferenceValue = statusText;
            serializedShop.FindProperty("purchaseButtonText").objectReferenceValue = purchaseText;
            serializedShop.FindProperty("affordabilityProgressFill").objectReferenceValue = progressFill;
            serializedShop.FindProperty("levelOneCostCents").intValue = 500;
            serializedShop.FindProperty("levelTwoCostCents").intValue = 1500;
            serializedShop.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void PlaceAndConnectScene(GameObject incubatorPrefab)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform location = FindSceneTransform(scene, "Location_Incubator");

        if (location == null)
        {
            throw new InvalidOperationException("SampleScene does not contain Location_Incubator.");
        }

        Transform existing = location.Find("prefab_Incubator");

        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        GameObject incubatorObject = (GameObject)PrefabUtility.InstantiatePrefab(incubatorPrefab, scene);
        incubatorObject.transform.SetParent(location, false);
        incubatorObject.transform.localPosition = Vector3.zero;
        incubatorObject.transform.localRotation = Quaternion.identity;
        incubatorObject.transform.localScale = Vector3.one;
        IncubatorController incubator = incubatorObject.GetComponent<IncubatorController>();
        incubatorObject.SetActive(false);

        IncubatorShopController shopController = FindSceneComponent<IncubatorShopController>(scene);

        if (shopController == null)
        {
            throw new InvalidOperationException("The instantiated HUD does not contain IncubatorShopController.");
        }

        SerializedObject serializedShop = new SerializedObject(shopController);
        serializedShop.FindProperty("incubator").objectReferenceValue = incubator;
        serializedShop.ApplyModifiedPropertiesWithoutUndo();

        RectTransform hudRoot = shopController.GetComponent<RectTransform>();

        if (hudRoot != null)
        {
            hudRoot.localScale = Vector3.one;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static TextMeshPro CreateWorldText(
        string name,
        Transform parent,
        TMP_FontAsset font,
        string content,
        Color color)
    {
        GameObject textObject = new GameObject(name, typeof(TextMeshPro));
        textObject.transform.SetParent(parent, false);
        TextMeshPro text = textObject.GetComponent<TextMeshPro>();
        text.font = font;
        text.fontSharedMaterial = font.material;
        text.text = content;
        text.fontSize = 3f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.fontStyle = FontStyles.Bold;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.rectTransform.sizeDelta = new Vector2(2.4f, 0.5f);
        text.rectTransform.localPosition = Vector3.zero;
        text.rectTransform.localRotation = Quaternion.identity;
        text.rectTransform.localScale = Vector3.one * 0.055f;
        return text;
    }

    private static void SetLevel(SerializedProperty property, int capacity, float secondsPerEgg)
    {
        property.FindPropertyRelative("capacity").intValue = capacity;
        property.FindPropertyRelative("secondsPerEgg").floatValue = secondsPerEgg;
    }

    private static void ValidateReference(SerializedObject serializedObject, string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property == null || property.objectReferenceValue == null)
        {
            throw new InvalidOperationException(
                $"{serializedObject.targetObject.name} is missing serialized reference '{propertyName}'.");
        }
    }

    private static void ThrowIfMissingScripts(GameObject root)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
            {
                throw new InvalidOperationException(
                    $"'{transform.name}' below '{root.name}' contains a missing script.");
            }
        }
    }

    private static Transform FindRequired(Transform root, string name)
    {
        Transform found = FindTransform(root, name);

        if (found == null)
        {
            throw new InvalidOperationException($"The incubator model is missing required socket '{name}'.");
        }

        return found;
    }

    private static Transform FindTransform(Transform root, string name)
    {
        if (root.name == name)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindTransform(root.GetChild(i), name);

            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Transform FindSceneTransform(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = FindTransform(root.transform, name);

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

    private static RectTransform CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rectTransform = gameObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.localScale = Vector3.one;
        return rectTransform;
    }

    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        TMP_FontAsset font,
        string text,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        RectTransform rectTransform = CreateUiObject(name, parent);
        TextMeshProUGUI label = rectTransform.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return label;
    }

    private static void SetRect(
        RectTransform rectTransform,
        Vector2 anchor,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        rectTransform.anchorMin = anchor;
        rectTransform.anchorMax = anchor;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;
    }

    private static void Stretch(RectTransform rectTransform, float inset)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = new Vector2(inset, inset);
        rectTransform.offsetMax = new Vector2(-inset, -inset);
    }

    private static Sprite GetUiSprite()
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }

    private static void SetButtonColors(Button button, Color normalColor)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = normalColor;
        colors.highlightedColor = Color.Lerp(normalColor, Color.white, 0.25f);
        colors.pressedColor = Color.Lerp(normalColor, Color.black, 0.25f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(
            normalColor.r * 0.45f,
            normalColor.g * 0.45f,
            normalColor.b * 0.45f,
            0.65f);
        button.colors = colors;
    }

    private static void EnsureFolder(string parent, string folder)
    {
        string path = $"{parent}/{folder}";

        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, folder);
        }
    }

    private static void LogModelLayout(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        Debug.Log($"Incubator authored bounds: center {bounds.center}, size {bounds.size}.");
    }
}
