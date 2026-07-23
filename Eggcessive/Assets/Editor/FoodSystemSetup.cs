using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;

public static class FoodSystemSetup
{
    private const string ChickenModelPath = "Assets/Chicken/meshes/chicken.fbx";
    private const string ChickenPrefabPath = "Assets/Chicken/prefabs/prefab_chicken.prefab";
    private const string HudPrefabPath = "Assets/UI/prefab_EggScoreHud.prefab";
    private const string FoodPrefabPath = "Assets/Food/prefabs/prefab_food.prefab";
    private const string FoodMaterialPath = "Assets/Food/materials/mat_food.mat";
    private const string AnimatorControllerPath = "Assets/Chicken/Animations/chicken.controller";

    [InitializeOnLoadMethod]
    private static void ConfigureOnFirstImport()
    {
        EditorApplication.delayCall += () =>
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FoodPrefabPath) == null)
            {
                ConfigureAll();
            }
        };
    }

    [MenuItem("Tools/Eggcessive/Rebuild Food System Assets")]
    public static void ConfigureAll()
    {
        EnsureFolders();
        ConfigureImportedAnimationClips();
        AnimatorController animatorController = CreateAnimatorController();
        ConfigureChickenPrefab(animatorController);
        GameObject foodPrefab = CreateFoodPrefab();
        ConfigureHudPrefab(foodPrefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Food system assets configured successfully.");
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "Editor");
        EnsureFolder("Assets", "Food");
        EnsureFolder("Assets/Food", "prefabs");
        EnsureFolder("Assets/Food", "materials");
        EnsureFolder("Assets/Chicken", "Animations");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";

        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static void ConfigureImportedAnimationClips()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ChickenModelPath) as ModelImporter;

        if (importer == null)
        {
            throw new InvalidOperationException($"Could not find chicken model at {ChickenModelPath}.");
        }

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;

        foreach (ModelImporterClipAnimation clip in clips)
        {
            if (clip.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0
                || clip.name.IndexOf("eat", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                clip.loopTime = true;
                clip.loopPose = true;
            }
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    private static AnimatorController CreateAnimatorController()
    {
        AssetDatabase.DeleteAsset(AnimatorControllerPath);
        AnimatorController controller =
            AnimatorController.CreateAnimatorControllerAtPath(AnimatorControllerPath);
        controller.AddParameter("IsEating", AnimatorControllerParameterType.Bool);
        controller.AddParameter("LayEgg", AnimatorControllerParameterType.Trigger);

        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ChickenModelPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            .ToArray();
        AnimationClip idleClip = FindClip(clips, "idle");
        AnimationClip eatClip = FindClip(clips, "eat");
        AnimationClip layEggClip = FindClip(clips, "layEgg");

        if (idleClip == null || eatClip == null || layEggClip == null)
        {
            string available = string.Join(", ", clips.Select(clip => clip.name));
            throw new InvalidOperationException(
                $"Chicken idle/eat/layEgg clips were not all found. Imported clips: {available}");
        }

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState idleState = stateMachine.AddState("Idle");
        idleState.motion = idleClip;
        AnimatorState eatState = stateMachine.AddState("Eat");
        eatState.motion = eatClip;
        AnimatorState layEggState = stateMachine.AddState("Lay Egg");
        layEggState.motion = layEggClip;
        stateMachine.defaultState = idleState;

        AnimatorStateTransition startEating = idleState.AddTransition(eatState);
        startEating.hasExitTime = false;
        startEating.duration = 0.08f;
        startEating.AddCondition(AnimatorConditionMode.If, 0f, "IsEating");

        AnimatorStateTransition stopEating = eatState.AddTransition(idleState);
        stopEating.hasExitTime = false;
        stopEating.duration = 0.08f;
        stopEating.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsEating");

        AnimatorStateTransition startLaying = stateMachine.AddAnyStateTransition(layEggState);
        startLaying.hasExitTime = false;
        startLaying.duration = 0.04f;
        startLaying.canTransitionToSelf = false;
        startLaying.AddCondition(AnimatorConditionMode.If, 0f, "LayEgg");

        AnimatorStateTransition finishLaying = layEggState.AddTransition(idleState);
        finishLaying.hasExitTime = true;
        finishLaying.exitTime = 1f;
        finishLaying.duration = 0.05f;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimationClip FindClip(AnimationClip[] clips, string namePart)
    {
        return clips.FirstOrDefault(
            clip => clip.name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void ConfigureChickenPrefab(RuntimeAnimatorController animatorController)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ChickenPrefabPath);

        try
        {
            ChickenController chicken = root.GetComponent<ChickenController>();
            Animator animator = root.GetComponentInChildren<Animator>(true);

            if (chicken == null)
            {
                throw new InvalidOperationException(
                    "The chicken prefab must contain ChickenController.");
            }

            if (animator == null)
            {
                if (root.transform.childCount == 0)
                {
                    throw new InvalidOperationException(
                        "The chicken prefab does not contain a model root for its Animator.");
                }

                animator = root.transform.GetChild(0).gameObject.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController = animatorController;
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(ChickenModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
            animator.applyRootMotion = false;
            SerializedObject serializedChicken = new SerializedObject(chicken);
            serializedChicken.FindProperty("animator").objectReferenceValue = animator;
            serializedChicken.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, ChickenPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static GameObject CreateFoodPrefab()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(FoodMaterialPath);

        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            material = new Material(shader)
            {
                name = "mat_food",
                color = new Color(0.62f, 0.36f, 0.11f, 1f)
            };
            AssetDatabase.CreateAsset(material, FoodMaterialPath);
        }

        GameObject root = new GameObject("prefab_food");
        FoodPile pile = root.AddComponent<FoodPile>();
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Food Sphere";
        sphere.transform.SetParent(root.transform, false);
        sphere.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        sphere.transform.localScale = new Vector3(0.3f, 0.1f, 0.3f);
        sphere.GetComponent<Renderer>().sharedMaterial = material;

        SerializedObject serializedPile = new SerializedObject(pile);
        serializedPile.FindProperty("visualRoot").objectReferenceValue = sphere.transform;
        serializedPile.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, FoodPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    private static void ConfigureHudPrefab(GameObject foodPrefab)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HudPrefabPath);

        try
        {
            if (root.GetComponent<GraphicRaycaster>() == null)
            {
                root.AddComponent<GraphicRaycaster>();
            }

            Transform panel = root.transform.Find("Right HUD Panel");
            TMP_Text scoreText = root.GetComponent<EggScoreHud>()
                .GetComponentInChildren<TMP_Text>(true);

            if (panel == null || scoreText == null)
            {
                throw new InvalidOperationException("The HUD panel or score font could not be found.");
            }

            Transform oldShop = panel.Find("Food Shop");

            if (oldShop != null)
            {
                UnityEngine.Object.DestroyImmediate(oldShop.gameObject);
            }

            TMP_FontAsset font = scoreText.font;
            RectTransform shop = CreateUiObject("Food Shop", panel);
            shop.anchorMin = new Vector2(0f, 1f);
            shop.anchorMax = new Vector2(1f, 1f);
            shop.pivot = new Vector2(0.5f, 1f);
            shop.anchoredPosition = new Vector2(0f, -115f);
            shop.sizeDelta = new Vector2(0f, 190f);
            Image shopBackground = shop.gameObject.AddComponent<Image>();
            shopBackground.color = new Color(0.12f, 0.075f, 0.035f, 0.9f);
            shopBackground.raycastTarget = false;

            TextMeshProUGUI title = CreateText(
                "Title",
                shop,
                font,
                "CHICKEN FEED",
                22f,
                TextAlignmentOptions.Center);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(250f, 34f));

            RectTransform iconRect = CreateUiObject("Food Icon Button", shop);
            SetRect(iconRect, new Vector2(0f, 1f), new Vector2(42f, -73f), new Vector2(68f, 68f));
            Image iconBackground = iconRect.gameObject.AddComponent<Image>();
            iconBackground.sprite = GetUiSprite();
            iconBackground.type = Image.Type.Sliced;
            iconBackground.color = new Color(0.25f, 0.15f, 0.07f, 1f);
            Button iconButton = iconRect.gameObject.AddComponent<Button>();
            iconButton.targetGraphic = iconBackground;
            iconButton.navigation = new Navigation { mode = Navigation.Mode.None };
            SetButtonColors(iconButton, new Color(0.25f, 0.15f, 0.07f, 1f));

            RectTransform foodSymbol = CreateUiObject("Food Sphere Icon", iconRect);
            SetRect(foodSymbol, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 30f));
            Image foodImage = foodSymbol.gameObject.AddComponent<Image>();
            foodImage.sprite = GetKnobSprite();
            foodImage.preserveAspect = false;
            foodImage.color = new Color(0.76f, 0.48f, 0.16f, 1f);
            foodImage.raycastTarget = false;

            TextMeshProUGUI ownedText = CreateText(
                "Owned Count",
                shop,
                font,
                "x 0",
                26f,
                TextAlignmentOptions.Left);
            SetRect(ownedText.rectTransform, new Vector2(0f, 1f), new Vector2(82f, -73f), new Vector2(62f, 50f));

            RectTransform buyRect = CreateUiObject("Buy Button", shop);
            SetRect(buyRect, new Vector2(1f, 1f), new Vector2(-69f, -73f), new Vector2(112f, 58f));
            Image buyBackground = buyRect.gameObject.AddComponent<Image>();
            buyBackground.sprite = GetUiSprite();
            buyBackground.type = Image.Type.Sliced;
            buyBackground.color = new Color(0.2f, 0.55f, 0.18f, 1f);
            Button buyButton = buyRect.gameObject.AddComponent<Button>();
            buyButton.targetGraphic = buyBackground;
            buyButton.navigation = new Navigation { mode = Navigation.Mode.None };
            SetButtonColors(buyButton, new Color(0.2f, 0.55f, 0.18f, 1f));

            TextMeshProUGUI buyText = CreateText(
                "Label",
                buyRect,
                font,
                "BUY\n$2.00",
                19f,
                TextAlignmentOptions.Center);
            Stretch(buyText.rectTransform, 4f);

            TextMeshProUGUI statusText = CreateText(
                "Placement Status",
                shop,
                font,
                "Buy food, then click its icon",
                15f,
                TextAlignmentOptions.Center);
            statusText.color = new Color(1f, 0.88f, 0.58f, 1f);
            SetRect(statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(250f, 42f));

            FoodShopController shopController = root.GetComponent<FoodShopController>();

            if (shopController == null)
            {
                shopController = root.AddComponent<FoodShopController>();
            }

            SerializedObject serializedShop = new SerializedObject(shopController);
            serializedShop.FindProperty("foodIconButton").objectReferenceValue = iconButton;
            serializedShop.FindProperty("buyButton").objectReferenceValue = buyButton;
            serializedShop.FindProperty("ownedCountText").objectReferenceValue = ownedText;
            serializedShop.FindProperty("placementStatusText").objectReferenceValue = statusText;
            serializedShop.FindProperty("foodPrefab").objectReferenceValue = foodPrefab;
            serializedShop.FindProperty("foodCostCents").intValue = 200;
            serializedShop.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
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

    private static Sprite GetKnobSprite()
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
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
}
