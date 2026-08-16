#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class SupplyShopGraphPrefabSetup
{
    private const string Root = "Assets/Resources/SupplyShop";
    private const string NodePath = Root + "/prefab_SupplyShopNodePlaceholder.prefab";
    private const string PopupPath = Root + "/prefab_SupplyShopPopupPlaceholder.prefab";
    private const string ConnectorPath = Root + "/prefab_SupplyShopConnectorPlaceholder.prefab";
    private const string DotsPath = Root + "/t_supply_shop_dots.asset";
    private const string GridDotsPath = Root + "/t_supply_shop_grid_dots.asset";
    private const string VignettePath = Root + "/t_supply_shop_vignette.asset";
    private const string FontPath = "Assets/Fonts/Cat Song SDF.asset";

    static SupplyShopGraphPrefabSetup()
    {
        EditorApplication.delayCall += EnsurePlaceholderAssets;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
    }

    private static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.delayCall += EnsurePlaceholderAssets;
        }
    }

    [MenuItem("Eggcessive/UI/Supply Shop/Ensure Graph Placeholder Assets")]
    public static void EnsurePlaceholderAssets()
    {
        if (Application.isPlaying)
        {
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += EnsurePlaceholderAssets;
            return;
        }

        EnsureFolders();
        EnsureTextures();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(NodePath) == null)
        {
            CreateNodePrefab();
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PopupPath) == null)
        {
            CreatePopupPrefab();
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ConnectorPath) == null)
        {
            CreateConnectorPrefab();
        }
        AssetDatabase.SaveAssets();
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }
        if (!AssetDatabase.IsValidFolder(Root))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "SupplyShop");
        }
    }

    private static void EnsureTextures()
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(GridDotsPath) == null)
        {
            Texture2D gridDots = new Texture2D(
                64,
                64,
                TextureFormat.RGBA32,
                false)
            {
                name = "t_supply_shop_grid_dots",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            Color[] gridPixels = new Color[64 * 64];
            Color gridDot = new Color(0.5f, 0.57f, 0.54f, 0.48f);
            PaintDot(gridPixels, 64, 8, 8, gridDot);
            gridDots.SetPixels(gridPixels);
            gridDots.Apply(false, false);
            AssetDatabase.CreateAsset(gridDots, GridDotsPath);
        }

        if (AssetDatabase.LoadAssetAtPath<Texture2D>(DotsPath) == null)
        {
            Texture2D dots = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                name = "t_supply_shop_dots",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color dot = new Color(0.46f, 0.52f, 0.49f, 0.34f);
            Color[] pixels = new Color[64 * 64];
            for (int index = 0; index < pixels.Length; index++)
            {
                pixels[index] = clear;
            }
            PaintDot(pixels, 64, 8, 8, dot);
            PaintDot(pixels, 64, 40, 40, dot);
            dots.SetPixels(pixels);
            dots.Apply(false, false);
            AssetDatabase.CreateAsset(dots, DotsPath);
        }

        if (AssetDatabase.LoadAssetAtPath<Texture2D>(VignettePath) == null)
        {
            const int size = 256;
            Texture2D vignette = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false)
            {
                name = "t_supply_shop_vignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float edge = Mathf.Clamp01((Mathf.Sqrt(nx * nx + ny * ny) - 0.36f) / 0.72f);
                    float alpha = edge * edge * 0.72f;
                    pixels[y * size + x] = new Color(0.015f, 0.02f, 0.018f, alpha);
                }
            }
            vignette.SetPixels(pixels);
            vignette.Apply(false, false);
            AssetDatabase.CreateAsset(vignette, VignettePath);
        }
    }

    private static void PaintDot(
        Color[] pixels,
        int width,
        int centerX,
        int centerY,
        Color color)
    {
        for (int y = -2; y <= 2; y++)
        {
            for (int x = -2; x <= 2; x++)
            {
                float distance = Mathf.Sqrt(x * x + y * y);
                if (distance > 2.2f)
                {
                    continue;
                }
                Color pixel = color;
                pixel.a *= Mathf.Clamp01(1f - distance / 2.6f);
                pixels[(centerY + y) * width + centerX + x] = pixel;
            }
        }
    }

    private static void CreateNodePrefab()
    {
        GameObject root = new GameObject(
            "prefab_SupplyShopNodePlaceholder",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(Outline),
            typeof(Shadow),
            typeof(CanvasGroup),
            typeof(ProgressionNodeButton));
        RectTransform rect = root.transform as RectTransform;
        rect.sizeDelta = new Vector2(64f, 64f);
        Image image = root.GetComponent<Image>();
        image.color = new Color(0.17f, 0.2f, 0.19f, 1f);
        Button button = root.GetComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        Outline outline = root.GetComponent<Outline>();
        outline.effectColor = new Color(0.8f, 0.5f, 0.14f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);
        Shadow shadow = root.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.76f);
        shadow.effectDistance = new Vector2(5f, -5f);

        RawImage icon = CreateRawImage("Type Icon", root.transform);
        SetRect(icon.rectTransform, new Vector2(0f, 4f), new Vector2(40f, 40f));
        icon.raycastTarget = false;

        TMP_Text tier = CreateText(
            "Tier",
            root.transform,
            10f,
            TextAlignmentOptions.Center);
        tier.fontStyle = FontStyles.Bold;
        tier.color = new Color(1f, 0.91f, 0.65f, 1f);
        SetRect(tier.rectTransform, new Vector2(0f, -24f), new Vector2(56f, 14f));

        ProgressionNodeButton node = root.GetComponent<ProgressionNodeButton>();
        node.Configure(
            ProgressionSystem.UpgradeId.FoodBag,
            null,
            null,
            null,
            null,
            outline,
            new Color(0.72f, 0.36f, 0.1f, 1f));
        PrefabUtility.SaveAsPrefabAsset(root, NodePath);
        Object.DestroyImmediate(root);
    }

    private static void CreatePopupPrefab()
    {
        GameObject root = new GameObject(
            "prefab_SupplyShopPopupPlaceholder",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Outline),
            typeof(Shadow));
        RectTransform rect = root.transform as RectTransform;
        rect.sizeDelta = new Vector2(390f, 330f);
        Image background = root.GetComponent<Image>();
        background.color = new Color(0.045f, 0.055f, 0.052f, 0.995f);
        Outline outline = root.GetComponent<Outline>();
        outline.effectColor = new Color(0.76f, 0.53f, 0.18f, 1f);
        outline.effectDistance = new Vector2(3f, -3f);
        Shadow shadow = root.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.82f);
        shadow.effectDistance = new Vector2(9f, -9f);

        Image accent = CreateImage("Accent", root.transform);
        accent.color = new Color(0.94f, 0.58f, 0.12f, 1f);
        accent.rectTransform.anchorMin = new Vector2(0f, 1f);
        accent.rectTransform.anchorMax = Vector2.one;
        accent.rectTransform.pivot = new Vector2(0.5f, 1f);
        accent.rectTransform.anchoredPosition = Vector2.zero;
        accent.rectTransform.sizeDelta = new Vector2(0f, 6f);

        TMP_Text title = CreateText("Title", root.transform, 26f, TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold;
        title.color = new Color(1f, 0.88f, 0.55f, 1f);
        SetRect(title.rectTransform, new Vector2(0f, 132f), new Vector2(346f, 42f));

        TMP_Text level = CreateText("Level", root.transform, 12f, TextAlignmentOptions.Left);
        level.fontStyle = FontStyles.Bold;
        level.color = new Color(0.5f, 0.78f, 0.61f, 1f);
        SetRect(level.rectTransform, new Vector2(0f, 101f), new Vector2(346f, 22f));

        TMP_Text description = CreateText("Description", root.transform, 15f, TextAlignmentOptions.TopLeft);
        description.color = new Color(0.9f, 0.92f, 0.9f, 1f);
        description.textWrappingMode = TextWrappingModes.Normal;
        description.overflowMode = TextOverflowModes.Truncate;
        SetRect(description.rectTransform, new Vector2(0f, 32f), new Vector2(346f, 104f));

        TMP_Text price = CreateText("Price", root.transform, 18f, TextAlignmentOptions.Left);
        price.fontStyle = FontStyles.Bold;
        price.color = new Color(1f, 0.83f, 0.3f, 1f);
        SetRect(price.rectTransform, new Vector2(-75f, -36f), new Vector2(196f, 30f));

        TMP_Text savings = CreateText("Savings", root.transform, 11f, TextAlignmentOptions.Right);
        savings.color = new Color(0.7f, 0.74f, 0.7f, 1f);
        SetRect(savings.rectTransform, new Vector2(96f, -36f), new Vector2(150f, 24f));

        Image track = CreateImage("Savings Track", root.transform);
        track.color = new Color(0.12f, 0.15f, 0.14f, 1f);
        SetRect(track.rectTransform, new Vector2(0f, -62f), new Vector2(346f, 8f));
        Image fill = CreateImage("Fill", track.transform);
        fill.color = new Color(0.98f, 0.69f, 0.14f, 1f);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;

        Button buy = CreateButton("Buy Button", root.transform);
        SetRect(buy.GetComponent<RectTransform>(), new Vector2(0f, -115f), new Vector2(346f, 52f));
        TMP_Text label = CreateText("Label", buy.transform, 18f, TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        Stretch(label.rectTransform);

        PrefabUtility.SaveAsPrefabAsset(root, PopupPath);
        Object.DestroyImmediate(root);
    }

    private static void CreateConnectorPrefab()
    {
        GameObject root = new GameObject(
            "prefab_SupplyShopConnectorPlaceholder",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(SupplyShopGraphConnector));
        Image image = root.GetComponent<Image>();
        image.color = new Color(0.65f, 0.65f, 0.65f, 0.4f);
        image.raycastTarget = false;
        RectTransform rect = root.transform as RectTransform;
        rect.sizeDelta = new Vector2(100f, 5f);
        PrefabUtility.SaveAsPrefabAsset(root, ConnectorPath);
        Object.DestroyImmediate(root);
    }

    private static Button CreateButton(string name, Transform parent)
    {
        GameObject instance = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        instance.transform.SetParent(parent, false);
        Image image = instance.GetComponent<Image>();
        image.color = new Color(0.16f, 0.62f, 0.31f, 1f);
        Button button = instance.GetComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        return button;
    }

    private static Image CreateImage(string name, Transform parent)
    {
        GameObject instance = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        instance.transform.SetParent(parent, false);
        Image image = instance.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static RawImage CreateRawImage(string name, Transform parent)
    {
        GameObject instance = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage));
        instance.transform.SetParent(parent, false);
        return instance.GetComponent<RawImage>();
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject instance = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        instance.transform.SetParent(parent, false);
        TextMeshProUGUI text = instance.GetComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath)
            ?? TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
#endif
