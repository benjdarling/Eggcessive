using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class GameplayHudPrefabSetup
{
    private const string RoundPrefabPath =
        "Assets/UI/prefab_RoundSystem.prefab";
    private const string ToolHudPrefabPath =
        "Assets/UI/prefab_EggScoreHud.prefab";
    private const string IconAtlasPath =
        "Assets/Resources/UI/HudIconAtlas.png";

    private static readonly string[] StatLabels =
    {
        "EGGS",
        "EGGS / MIN",
        "CASH",
        "CHICKENS",
        "TRUCKS"
    };

    [InitializeOnLoadMethod]
    private static void QueuePrefabMigration()
    {
        EditorApplication.delayCall += MigrateIfRequired;
    }

    [MenuItem("Tools/Eggcessive/Rebuild Editable Gameplay HUD Prefabs")]
    public static void RebuildGameplayHudPrefabs()
    {
        ConfigureSavedPrefab(RoundPrefabPath, ConfigureRoundHud);
        ConfigureSavedPrefab(ToolHudPrefabPath, ConfigureToolHud);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "Gameplay HUD objects are now authored and editable in their prefabs.");
    }

    public static void ConfigureRoundHud(GameObject root)
    {
        RoundSystem roundSystem = root.GetComponent<RoundSystem>();
        Transform stats = FindDescendant(root.transform, "Live Round Stats");

        if (roundSystem == null || stats == null)
        {
            throw new InvalidOperationException(
                "The round prefab is missing RoundSystem or Live Round Stats.");
        }

        SerializedObject serializedRound = new SerializedObject(roundSystem);
        TMP_Text legacyLabels = serializedRound
            .FindProperty("liveStatsText")
            .objectReferenceValue as TMP_Text;
        TMP_Text legacyValues = serializedRound
            .FindProperty("liveStatsValueText")
            .objectReferenceValue as TMP_Text;
        TMP_FontAsset font = legacyLabels != null
            ? legacyLabels.font
            : legacyValues != null
                ? legacyValues.font
                : null;

        RemoveDirectChildren(stats, "Stats Inner Panel", "HUD Stat Row ");

        RectTransform statsRect = stats as RectTransform;
        statsRect.anchorMin = Vector2.one;
        statsRect.anchorMax = Vector2.one;
        statsRect.pivot = Vector2.one;
        statsRect.anchoredPosition = new Vector2(-24f, -62f);
        statsRect.sizeDelta = new Vector2(260f, 170f);

        Image outer = stats.GetComponent<Image>();
        outer.sprite = GetUiSprite();
        outer.type = Image.Type.Sliced;
        outer.color = new Color(0.28f, 0.16f, 0.085f, 0.97f);
        outer.raycastTarget = false;
        ConfigureOutline(
            stats.gameObject,
            new Color(0.12f, 0.07f, 0.035f, 1f),
            new Vector2(3f, -3f));
        ConfigureShadow(
            stats.gameObject,
            new Color(0f, 0f, 0f, 0.62f),
            new Vector2(4f, -5f));

        RectTransform inner = CreateUiObject("Stats Inner Panel", stats);
        Stretch(inner, 6f);
        Image innerImage = inner.gameObject.AddComponent<Image>();
        innerImage.sprite = GetUiSprite();
        innerImage.type = Image.Type.Sliced;
        innerImage.color = new Color(0.055f, 0.06f, 0.048f, 0.98f);
        innerImage.raycastTarget = false;
        inner.SetAsFirstSibling();

        if (legacyLabels != null)
        {
            legacyLabels.gameObject.SetActive(false);
        }

        if (legacyValues != null)
        {
            legacyValues.gameObject.SetActive(false);
        }

        Texture2D atlas =
            AssetDatabase.LoadAssetAtPath<Texture2D>(IconAtlasPath);
        SerializedProperty rowValues =
            serializedRound.FindProperty("liveStatRowValues");
        rowValues.arraySize = StatLabels.Length;

        for (int index = 0; index < StatLabels.Length; index++)
        {
            TMP_Text value = CreateStatRow(
                stats,
                font,
                atlas,
                index,
                StatLabels[index]);
            rowValues.GetArrayElementAtIndex(index).objectReferenceValue =
                value;
        }

        RectTransform coinTarget = serializedRound
            .FindProperty("coinHudTarget")
            .objectReferenceValue as RectTransform;
        if (coinTarget != null)
        {
            coinTarget.anchorMin = Vector2.one;
            coinTarget.anchorMax = Vector2.one;
            coinTarget.pivot = new Vector2(0.5f, 0.5f);
            coinTarget.anchoredPosition = new Vector2(-259f, -147f);
        }

        serializedRound.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(roundSystem);
    }

    public static void ConfigureToolHud(GameObject root)
    {
        FoodShopController controller =
            root.GetComponent<FoodShopController>();
        Transform panel = FindDescendant(root.transform, "Right HUD Panel");

        if (controller == null || panel == null)
        {
            throw new InvalidOperationException(
                "The HUD prefab is missing FoodShopController or Right HUD Panel.");
        }

        SerializedObject serializedController =
            new SerializedObject(controller);
        Button foodButton = serializedController
            .FindProperty("foodIconButton")
            .objectReferenceValue as Button;
        TMP_Text ownedCount = serializedController
            .FindProperty("ownedCountText")
            .objectReferenceValue as TMP_Text;
        TMP_Text placementStatus = serializedController
            .FindProperty("placementStatusText")
            .objectReferenceValue as TMP_Text;
        TMP_FontAsset font = ownedCount != null
            ? ownedCount.font
            : placementStatus != null
                ? placementStatus.font
                : null;

        if (foodButton == null)
        {
            throw new InvalidOperationException(
                "The HUD prefab has no authored food icon button.");
        }

        DestroyNamedDescendant(panel, "Hand Tool Button");
        DestroyNamedDescendant(panel, "Collection Tool Button");

        RectTransform panelRect = panel as RectTransform;
        panelRect.anchorMin = new Vector2(0.85f, panelRect.anchorMin.y);
        panelRect.anchorMax = new Vector2(1f, panelRect.anchorMax.y);
        panelRect.offsetMin = new Vector2(0f, panelRect.offsetMin.y);
        panelRect.offsetMax = new Vector2(0f, panelRect.offsetMax.y);

        Transform oldFoodPanel =
            FindDescendant(root.transform, "Food Shop");
        foodButton.transform.SetParent(panel, false);
        SetTopRightRect(
            foodButton.GetComponent<RectTransform>(),
            new Vector2(-24f, -396f),
            new Vector2(64f, 64f));
        SetLayerRecursively(foodButton.gameObject, 5);
        Image foodImage = foodButton.GetComponent<Image>();
        StyleToolButton(
            foodButton,
            foodImage,
            new Color(0.54f, 0.27f, 0.08f, 1f));

        Transform oldFoodIcon =
            foodButton.transform.Find("Food Sphere Icon");
        if (oldFoodIcon != null)
        {
            oldFoodIcon.gameObject.SetActive(false);
        }

        if (ownedCount != null)
        {
            ownedCount.transform.SetParent(foodButton.transform, false);
            RectTransform countRect = ownedCount.rectTransform;
            countRect.anchorMin = Vector2.zero;
            countRect.anchorMax = Vector2.zero;
            countRect.pivot = Vector2.zero;
            countRect.anchoredPosition = new Vector2(5f, 4f);
            countRect.sizeDelta = new Vector2(36f, 18f);
            ownedCount.fontSize = 11f;
            ownedCount.alignment = TextAlignmentOptions.BottomLeft;
        }

        Texture2D atlas =
            AssetDatabase.LoadAssetAtPath<Texture2D>(IconAtlasPath);
        Button handButton = CreateToolButton(
            panel,
            font,
            atlas,
            "Hand Tool Button",
            new Vector2(-24f, -248f),
            "HAND",
            "1",
            5,
            new Color(0.18f, 0.48f, 0.34f, 1f),
            out Image handImage,
            out RawImage handIcon,
            out _);
        Button collectionButton = CreateToolButton(
            panel,
            font,
            atlas,
            "Collection Tool Button",
            new Vector2(-24f, -322f),
            "BASKET",
            "2",
            6,
            new Color(0.15f, 0.39f, 0.63f, 1f),
            out Image collectionImage,
            out RawImage collectionIcon,
            out TMP_Text collectionLabel);
        RawImage foodIcon = CreateToolIcon(
            foodButton.transform,
            atlas,
            7);
        CreateShortcutBadge(foodButton.transform, font, "3");

        if (oldFoodPanel != null)
        {
            oldFoodPanel.gameObject.SetActive(false);
        }

        Image panelBackground = panel.GetComponent<Image>();
        if (panelBackground != null)
        {
            panelBackground.enabled = false;
        }

        SetDescendantActive(panel, "Score", false);
        SetDescendantActive(panel, "Incubator Shop", false);

        serializedController.FindProperty("handToolButton")
            .objectReferenceValue = handButton;
        serializedController.FindProperty("collectionToolButton")
            .objectReferenceValue = collectionButton;
        serializedController.FindProperty("collectionToolLabel")
            .objectReferenceValue = collectionLabel;
        serializedController.FindProperty("handToolImage")
            .objectReferenceValue = handImage;
        serializedController.FindProperty("collectionToolImage")
            .objectReferenceValue = collectionImage;
        serializedController.FindProperty("foodToolImage")
            .objectReferenceValue = foodImage;
        serializedController.FindProperty("handToolIcon")
            .objectReferenceValue = handIcon;
        serializedController.FindProperty("collectionToolIcon")
            .objectReferenceValue = collectionIcon;
        serializedController.FindProperty("foodToolIcon")
            .objectReferenceValue = foodIcon;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
    }

    private static TMP_Text CreateStatRow(
        Transform parent,
        TMP_FontAsset font,
        Texture2D atlas,
        int index,
        string label)
    {
        RectTransform row = CreateUiObject($"HUD Stat Row {index}", parent);
        SetCenteredRect(
            row,
            new Vector2(0f, 56f - index * 28f),
            new Vector2(228f, 26f));
        HorizontalLayoutGroup layout =
            row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        RectTransform iconRect = CreateUiObject("Icon", row);
        RawImage icon = iconRect.gameObject.AddComponent<RawImage>();
        icon.texture = atlas;
        icon.uvRect = RoundSystem.GetHudIconUv(index);
        icon.color = Color.white;
        icon.raycastTarget = false;
        LayoutElement iconLayout =
            iconRect.gameObject.AddComponent<LayoutElement>();
        iconLayout.minWidth = 26f;
        iconLayout.preferredWidth = 26f;
        iconLayout.minHeight = 26f;
        iconLayout.preferredHeight = 26f;
        iconLayout.flexibleWidth = 0f;

        TMP_Text labelText = CreateText(
            "Label",
            row,
            font,
            label,
            14f,
            TextAlignmentOptions.MidlineLeft);
        labelText.color = Color.white;
        labelText.fontStyle = FontStyles.Bold;
        LayoutElement labelLayout =
            labelText.gameObject.AddComponent<LayoutElement>();
        labelLayout.minWidth = 82f;
        labelLayout.preferredWidth = 82f;
        labelLayout.flexibleWidth = 1f;

        TMP_Text valueText = CreateText(
            "Value",
            row,
            font,
            index == 1 ? "0.0" : "0",
            14f,
            TextAlignmentOptions.MidlineRight);
        valueText.color = new Color(1f, 0.87f, 0.27f, 1f);
        valueText.fontStyle = FontStyles.Bold;
        LayoutElement valueLayout =
            valueText.gameObject.AddComponent<LayoutElement>();
        valueLayout.minWidth = 102f;
        valueLayout.preferredWidth = 102f;
        valueLayout.flexibleWidth = 0f;
        return valueText;
    }

    private static Button CreateToolButton(
        Transform parent,
        TMP_FontAsset font,
        Texture2D atlas,
        string objectName,
        Vector2 position,
        string label,
        string shortcut,
        int atlasIndex,
        Color color,
        out Image image,
        out RawImage icon,
        out TMP_Text labelText)
    {
        RectTransform rect = CreateUiObject(objectName, parent);
        SetTopRightRect(rect, position, new Vector2(64f, 64f));
        image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        StyleToolButton(button, image, color);

        labelText = CreateText(
            "Label",
            rect,
            font,
            label,
            9f,
            TextAlignmentOptions.Center);
        Stretch(labelText.rectTransform, 3f);
        labelText.fontStyle = FontStyles.Bold;
        labelText.gameObject.SetActive(false);
        icon = CreateToolIcon(rect, atlas, atlasIndex);
        CreateShortcutBadge(rect, font, shortcut);
        SetLayerRecursively(rect.gameObject, 5);
        return button;
    }

    private static RawImage CreateToolIcon(
        Transform parent,
        Texture2D atlas,
        int atlasIndex)
    {
        Transform existing = parent.Find("Tool Icon");
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        RectTransform iconRect = CreateUiObject("Tool Icon", parent);
        SetCenteredRect(
            iconRect,
            new Vector2(0f, -1f),
            new Vector2(44f, 44f));
        RawImage icon = iconRect.gameObject.AddComponent<RawImage>();
        icon.texture = atlas;
        icon.uvRect = RoundSystem.GetHudIconUv(atlasIndex);
        icon.color = Color.white;
        icon.raycastTarget = false;
        iconRect.SetAsFirstSibling();
        return icon;
    }

    private static void CreateShortcutBadge(
        Transform parent,
        TMP_FontAsset font,
        string shortcut)
    {
        Transform existing = parent.Find("Shortcut Badge");
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        RectTransform badge = CreateUiObject("Shortcut Badge", parent);
        badge.anchorMin = Vector2.one;
        badge.anchorMax = Vector2.one;
        badge.pivot = Vector2.one;
        badge.anchoredPosition = new Vector2(-2f, -2f);
        badge.sizeDelta = new Vector2(20f, 20f);
        Image image = badge.gameObject.AddComponent<Image>();
        image.sprite = GetUiSprite();
        image.type = Image.Type.Sliced;
        image.color = new Color(0.04f, 0.04f, 0.04f, 0.94f);
        image.raycastTarget = false;
        TMP_Text text = CreateText(
            "Number",
            badge,
            font,
            shortcut,
            12f,
            TextAlignmentOptions.Center);
        Stretch(text.rectTransform, 0f);
        text.fontStyle = FontStyles.Bold;
        badge.SetAsLastSibling();
    }

    private static void StyleToolButton(
        Button button,
        Image image,
        Color color)
    {
        image.sprite = GetUiSprite();
        image.type = Image.Type.Sliced;
        ConfigureOutline(
            button.gameObject,
            new Color(0.08f, 0.06f, 0.035f, 0.9f),
            new Vector2(2f, -2f));
        ConfigureShadow(
            button.gameObject,
            new Color(0f, 0f, 0f, 0.55f),
            new Vector2(3f, -4f));

        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.2f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(
            color.r * 0.45f,
            color.g * 0.45f,
            color.b * 0.45f,
            0.65f);
        button.colors = colors;
    }

    private static void ConfigureOutline(
        GameObject target,
        Color color,
        Vector2 distance)
    {
        Outline outline = target.GetComponent<Outline>();
        if (outline == null)
        {
            outline = target.AddComponent<Outline>();
        }

        outline.effectColor = color;
        outline.effectDistance = distance;
        outline.useGraphicAlpha = false;
    }

    private static void ConfigureShadow(
        GameObject target,
        Color color,
        Vector2 distance)
    {
        Shadow shadow = target
            .GetComponents<Shadow>()
            .FirstOrDefault(component =>
                component != null
                && component.GetType() == typeof(Shadow));
        if (shadow == null)
        {
            shadow = target.AddComponent<Shadow>();
        }

        shadow.effectColor = color;
        shadow.effectDistance = distance;
    }

    private static TMP_Text CreateText(
        string objectName,
        Transform parent,
        TMP_FontAsset font,
        string value,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateUiObject(objectName, parent);
        TextMeshProUGUI text =
            rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateUiObject(
        string objectName,
        Transform parent)
    {
        GameObject gameObject =
            new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        gameObject.layer = parent.gameObject.layer;
        return rect;
    }

    private static void SetTopRightRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
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

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void RemoveDirectChildren(
        Transform parent,
        string exactName,
        string namePrefix)
    {
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            Transform child = parent.GetChild(index);
            if (child.name == exactName
                || child.name.StartsWith(
                    namePrefix,
                    StringComparison.Ordinal))
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }
    }

    private static void DestroyNamedDescendant(
        Transform root,
        string objectName)
    {
        Transform target = FindDescendant(root, objectName);
        if (target != null)
        {
            Object.DestroyImmediate(target.gameObject);
        }
    }

    private static Transform FindDescendant(
        Transform root,
        string objectName)
    {
        return root
            .GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(child => child.name == objectName);
    }

    private static void SetDescendantActive(
        Transform root,
        string objectName,
        bool active)
    {
        Transform target = FindDescendant(root, objectName);
        if (target != null)
        {
            target.gameObject.SetActive(active);
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static Sprite GetUiSprite()
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>(
            "UI/Skin/UISprite.psd");
    }

    private static void ConfigureSavedPrefab(
        string path,
        Action<GameObject> configure)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            configure(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void MigrateIfRequired()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isCompiling
            || EditorApplication.isUpdating)
        {
            return;
        }

        GameObject roundPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(RoundPrefabPath);
        GameObject toolPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(ToolHudPrefabPath);
        bool roundNeedsMigration = roundPrefab != null
            && FindDescendant(
                roundPrefab.transform,
                "HUD Stat Row 0") == null;
        bool toolsNeedMigration = toolPrefab != null
            && FindDescendant(
                toolPrefab.transform,
                "Hand Tool Button") == null;

        if (roundNeedsMigration || toolsNeedMigration)
        {
            RebuildGameplayHudPrefabs();
        }
    }
}
