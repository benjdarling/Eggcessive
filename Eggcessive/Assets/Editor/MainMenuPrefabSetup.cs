using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class MainMenuPrefabSetup
{
    private const string PrefabPath =
        "Assets/Resources/UI/prefab_MainMenu.prefab";
    private const string BackgroundPath =
        "Assets/Resources/UI/menu_logo_bg.png";
    private const string ChickenPath =
        "Assets/Resources/UI/menu_logo_chicken.png";
    private const string TitlePath =
        "Assets/Resources/UI/menu_logo_title.png";
    private const string DefaultFontPath =
        "Assets/Fonts/Cat Song SDF.asset";
    private const float ReferenceWidth = 1920f;

    private static readonly Color EggYellow =
        new Color(1f, 0.78f, 0.2f, 1f);

    static MainMenuPrefabSetup()
    {
        EditorApplication.delayCall += EnsurePrefabExists;
    }

    [MenuItem("Tools/Eggcessive/Rebuild Editable Main Menu Prefab...")]
    public static void RebuildPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null
            && !EditorUtility.DisplayDialog(
                "Rebuild Main Menu Prefab?",
                "This resets the editable main-menu prefab to its generated "
                + "defaults and overwrites manual layout changes.",
                "Rebuild",
                "Cancel"))
        {
            return;
        }

        BuildPrefab();
    }

    [MenuItem("Tools/Eggcessive/Add 1080p Canvas to Existing Main Menu")]
    public static void AddReferenceCanvasToExistingPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ConfigureReferenceCanvas(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Added the 1920x1080 Canvas context to the existing main-menu "
                + "prefab without rebuilding its children.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsurePrefabExists()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            BuildPrefab();
        }
    }

    private static void BuildPrefab()
    {
        Texture2D background = AssetDatabase.LoadAssetAtPath<Texture2D>(
            BackgroundPath);
        Texture2D chicken = AssetDatabase.LoadAssetAtPath<Texture2D>(
            ChickenPath);
        Texture2D title = AssetDatabase.LoadAssetAtPath<Texture2D>(TitlePath);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            DefaultFontPath);
        if (background == null || chicken == null || title == null || font == null)
        {
            Debug.LogError(
                "Cannot build the main-menu prefab because one or more menu "
                + "artwork textures are missing.");
            return;
        }

        GameObject root = new GameObject("Main Menu", typeof(RectTransform));
        try
        {
            ConfigureReferenceCanvas(root);
            CreateArtworkLayer(
                root.transform,
                "Menu Background",
                background,
                true);
            CreateArtworkLayer(
                root.transform,
                "Menu Chicken",
                chicken,
                false);
            CreateArtworkLayer(
                root.transform,
                "Menu Title",
                title,
                false);

            CreateText(
                "Subtitle",
                root.transform,
                "BUILD THE FLOCK. BREAK THE NUMBERS.",
                24f,
                new Vector2(0f, -15f),
                new Vector2(900f, 45f),
                new Color(0.88f, 0.8f, 0.64f),
                FontStyles.Bold,
                font);
            CreateButton(root.transform, "PLAY", -95f, font);
            CreateButton(root.transform, "EGGCESSIVE", -180f, font);
            CreateText(
                "Eggcessive Lock Note",
                root.transform,
                "LOCKED - REACH LEVEL 100",
                18f,
                new Vector2(0f, -222f),
                new Vector2(900f, 28f),
                new Color(0.6f, 0.55f, 0.47f),
                FontStyles.Bold,
                font);
            CreateButton(root.transform, "OPTIONS", -270f, font);
            CreateButton(root.transform, "LEADERBOARDS", -355f, font);
            CreateButton(root.transform, "QUIT", -440f, font);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Created editable main-menu prefab at " + PrefabPath + ".");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void CreateArtworkLayer(
        Transform parent,
        string objectName,
        Texture2D texture,
        bool fillCanvasWidth)
    {
        GameObject layerObject = CreateUiObject(objectName, parent);
        RectTransform rect = layerObject.GetComponent<RectTransform>();
        float widthFraction = fillCanvasWidth
            ? 1f
            : texture.width / ReferenceWidth;
        float halfWidth = widthFraction * 0.5f;
        rect.anchorMin = new Vector2(0.5f - halfWidth, 1f);
        rect.anchorMax = new Vector2(0.5f + halfWidth, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        AspectRatioFitter fitter =
            layerObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
        fitter.aspectRatio = texture.width / (float)texture.height;

        RawImage image = layerObject.AddComponent<RawImage>();
        image.texture = texture;
        image.color = Color.white;
        image.raycastTarget = false;
    }

    private static void CreateButton(
        Transform parent,
        string label,
        float y,
        TMP_FontAsset font)
    {
        GameObject buttonObject = CreateUiObject(label + " Button", parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        SetCenteredRect(rect, new Vector2(0f, y), new Vector2(900f, 76f));

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.22f, 0.14f, 0.055f, 0.001f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation
        {
            mode = Navigation.Mode.Automatic
        };
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = image.color;
        colors.pressedColor = image.color;
        colors.selectedColor = image.color;
        colors.disabledColor = image.color;
        button.colors = colors;

        TMP_Text text = CreateText(
            "Label",
            buttonObject.transform,
            label,
            48f,
            Vector2.zero,
            rect.sizeDelta,
            EggYellow,
            FontStyles.Bold,
            font);
        StretchToParent(text.rectTransform);
    }

    private static TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        float fontSize,
        Vector2 position,
        Vector2 size,
        Color color,
        FontStyles style,
        TMP_FontAsset font)
    {
        GameObject textObject = CreateUiObject(objectName, parent);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        SetCenteredRect(rect, position, size);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = font;

        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject value = new GameObject(name, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return value;
    }

    private static void SetCenteredRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private static void ConfigureReferenceCanvas(GameObject root)
    {
        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = root.AddComponent<Canvas>();
        }

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5001;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = root.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (root.GetComponent<GraphicRaycaster>() == null)
        {
            root.AddComponent<GraphicRaycaster>();
        }
    }
}
