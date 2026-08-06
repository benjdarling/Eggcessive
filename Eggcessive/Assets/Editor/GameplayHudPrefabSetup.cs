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
    private const int AuthoredPenButtonCount = 8;

    private static readonly string[] StatLabels =
    {
        "EGGS",
        "EGGS / MIN",
        "CASH",
        "CHICKENS",
        "TRUCKS",
        "WEIGHT"
    };

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

    [MenuItem("Tools/Eggcessive/Rebuild Editable Pen Buttons")]
    public static void RebuildEditablePenButtons()
    {
        ConfigureSavedPrefab(ToolHudPrefabPath, ConfigurePenHud);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("The eight pen buttons are now authored in the HUD prefab.");
    }

    public static void ConfigurePenHud(GameObject root)
    {
        PenHudController controller = root.GetComponent<PenHudController>();
        if (controller == null)
        {
            controller = root.AddComponent<PenHudController>();
        }

        TMP_Text existingText = root.GetComponentsInChildren<TMP_Text>(true)
            .FirstOrDefault(text => text.font != null);
        ConfigurePenNavigation(
            root.transform,
            existingText != null ? existingText.font : null,
            controller);
        ConfigurePenEquipmentHud(
            root,
            existingText != null ? existingText.font : null);
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
        statsRect.anchoredPosition = new Vector2(-24f, -24f);
        statsRect.sizeDelta = new Vector2(260f, 198f);

        Image outer = stats.GetComponent<Image>();
        outer.sprite = GetUiSprite();
        outer.type = Image.Type.Sliced;
        outer.color = new Color(0.055f, 0.06f, 0.048f, 0.94f);
        outer.raycastTarget = false;
        ConfigureOutline(
            stats.gameObject,
            new Color(0.12f, 0.07f, 0.035f, 1f),
            new Vector2(2f, -2f));
        ConfigureShadow(
            stats.gameObject,
            new Color(0f, 0f, 0f, 0.5f),
            new Vector2(3f, -4f));

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

        DestroyNamedDescendant(root.transform, "Hand Tool Button");
        DestroyNamedDescendant(root.transform, "Collection Tool Button");
        DestroyNamedDescendant(root.transform, "Incubator Turbo Button");
        DestroyNamedDescendant(root.transform, "Crosshatcher Turbo Button");
        DestroyNamedDescendant(root.transform, "Robot Turbo Button");

        RectTransform panelRect = panel as RectTransform;
        panelRect.anchorMin = new Vector2(0.85f, panelRect.anchorMin.y);
        panelRect.anchorMax = new Vector2(1f, panelRect.anchorMax.y);
        panelRect.offsetMin = new Vector2(0f, panelRect.offsetMin.y);
        panelRect.offsetMax = new Vector2(0f, panelRect.offsetMax.y);

        Transform oldFoodPanel =
            FindDescendant(root.transform, "Food Shop");
        Transform toolPaletteParent = root.transform;
        foodButton.transform.SetParent(toolPaletteParent, false);
        SetBottomLeftRect(
            foodButton.GetComponent<RectTransform>(),
            new Vector2(172f, 24f),
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
            toolPaletteParent,
            font,
            atlas,
            "Hand Tool Button",
            new Vector2(24f, 24f),
            "HAND",
            "1",
            5,
            new Color(0.18f, 0.48f, 0.34f, 1f),
            out Image handImage,
            out RawImage handIcon,
            out _);
        Button collectionButton = CreateToolButton(
            toolPaletteParent,
            font,
            atlas,
            "Collection Tool Button",
            new Vector2(98f, 24f),
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

        Button[] turboButtons = new Button[3];
        Image[] turboImages = new Image[3];
        TMP_Text[] turboCounts = new TMP_Text[3];
        TMP_Text[] turboTimers = new TMP_Text[3];
        Color[] turboColors =
        {
            new Color(0.72f, 0.27f, 0.06f, 1f),
            new Color(0.12f, 0.5f, 0.22f, 1f),
            new Color(0.42f, 0.2f, 0.64f, 1f)
        };
        for (int index = 0; index < turboButtons.Length; index++)
        {
            TurboConsumableSystem.TurboType type =
                (TurboConsumableSystem.TurboType)index;
            turboButtons[index] = CreateTurboButton(
                toolPaletteParent,
                font,
                type,
                new Vector2(264f + index * 74f, 24f),
                turboColors[index],
                out turboImages[index],
                out turboCounts[index],
                out turboTimers[index]);
        }

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

        PenHudController penHud = root.GetComponent<PenHudController>();
        if (penHud == null)
        {
            penHud = root.AddComponent<PenHudController>();
        }

        ConfigurePenNavigation(root.transform, font, penHud);
        ConfigurePenEquipmentHud(root, font);

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
        AssignObjectArray(
            serializedController.FindProperty("turboButtons"),
            turboButtons);
        AssignObjectArray(
            serializedController.FindProperty("turboButtonImages"),
            turboImages);
        AssignObjectArray(
            serializedController.FindProperty("turboCountTexts"),
            turboCounts);
        AssignObjectArray(
            serializedController.FindProperty("turboTimerTexts"),
            turboTimers);
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(penHud);
    }

    private static void ConfigurePenNavigation(
        Transform parent,
        TMP_FontAsset font,
        PenHudController controller)
    {
        DestroyNamedDescendant(parent, "Pen Navigation");

        RectTransform panel = CreateUiObject("Pen Navigation", parent);
        panel.anchorMin = new Vector2(1f, 0f);
        panel.anchorMax = new Vector2(1f, 0f);
        panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-24f, 24f);
        panel.sizeDelta = new Vector2(554f, 64f);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = GetUiSprite();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = Color.clear;
        panelImage.raycastTarget = false;

        TMP_Text title = CreateText(
            "Title",
            panel,
            font,
            "PENS",
            14f,
            TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        title.color = new Color(1f, 0.87f, 0.27f, 1f);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = Vector2.one;
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -5f);
        titleRect.sizeDelta = new Vector2(0f, 22f);
        title.gameObject.SetActive(false);

        RectTransform content = CreateUiObject("Buttons", panel);
        Stretch(content, 0f);
        HorizontalLayoutGroup layout =
            content.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        RectTransform templateRect = CreateUiObject("Pen Button Template", content);
        templateRect.sizeDelta = new Vector2(64f, 64f);
        LayoutElement templateLayout =
            templateRect.gameObject.AddComponent<LayoutElement>();
        templateLayout.minWidth = 64f;
        templateLayout.preferredWidth = 64f;
        templateLayout.flexibleWidth = 0f;
        templateLayout.minHeight = 64f;
        templateLayout.preferredHeight = 64f;
        templateLayout.flexibleHeight = 0f;
        Image background = templateRect.gameObject.AddComponent<Image>();
        background.sprite = GetUiSprite();
        background.type = Image.Type.Sliced;
        background.color = new Color(0.22f, 0.34f, 0.25f, 1f);
        ConfigureOutline(
            templateRect.gameObject,
            new Color(0.12f, 0.07f, 0.035f, 1f),
            new Vector2(2f, -2f));
        ConfigureShadow(
            templateRect.gameObject,
            new Color(0f, 0f, 0f, 0.5f),
            new Vector2(3f, -4f));
        Button button = templateRect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        TMP_Text penLabel = CreateText(
            "Pen Label",
            templateRect,
            font,
            "PEN 1",
            12f,
            TextAlignmentOptions.Center);
        penLabel.fontStyle = FontStyles.Bold;
        penLabel.rectTransform.anchorMin = new Vector2(0f, 0.47f);
        penLabel.rectTransform.anchorMax = Vector2.one;
        penLabel.rectTransform.offsetMin = new Vector2(2f, 0f);
        penLabel.rectTransform.offsetMax = new Vector2(-2f, -1f);

        TMP_Text purchaseLabel = CreateText(
            "Purchase Label",
            templateRect,
            font,
            "0\nE/MIN",
            11f,
            TextAlignmentOptions.Center);
        purchaseLabel.color = new Color(1f, 0.89f, 0.46f, 1f);
        purchaseLabel.rectTransform.anchorMin = Vector2.zero;
        purchaseLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
        purchaseLabel.rectTransform.offsetMin = new Vector2(2f, 2f);
        purchaseLabel.rectTransform.offsetMax = new Vector2(-2f, 30f);
        purchaseLabel.enableAutoSizing = true;
        purchaseLabel.fontSizeMin = 8f;
        purchaseLabel.fontSizeMax = 11f;
        purchaseLabel.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform progress = CreateUiObject("Savings Progress", templateRect);
        progress.anchorMin = new Vector2(0f, 0f);
        progress.anchorMax = new Vector2(1f, 0f);
        progress.pivot = new Vector2(0.5f, 0f);
        progress.anchoredPosition = new Vector2(0f, 2f);
        progress.sizeDelta = new Vector2(-6f, 3f);
        Image progressTrack = progress.gameObject.AddComponent<Image>();
        progressTrack.color = new Color(0.04f, 0.04f, 0.03f, 0.9f);
        progressTrack.raycastTarget = false;

        RectTransform fillRect = CreateUiObject("Fill", progress);
        Stretch(fillRect, 0f);
        Image fill = fillRect.gameObject.AddComponent<Image>();
        fill.color = new Color(1f, 0.72f, 0.12f, 1f);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0f;
        fill.raycastTarget = false;

        TMP_Text earningsText = CreateText(
            "Pen Earnings",
            templateRect,
            font,
            "+$2.3k",
            12f,
            TextAlignmentOptions.Bottom);
        earningsText.fontStyle = FontStyles.Bold;
        earningsText.color = new Color(1f, 0.84f, 0.16f, 1f);
        earningsText.textWrappingMode = TextWrappingModes.NoWrap;
        earningsText.overflowMode = TextOverflowModes.Overflow;
        earningsText.raycastTarget = false;
        RectTransform earningsRect = earningsText.rectTransform;
        earningsRect.anchorMin = new Vector2(0.5f, 1f);
        earningsRect.anchorMax = new Vector2(0.5f, 1f);
        earningsRect.pivot = new Vector2(0.5f, 0f);
        earningsRect.anchoredPosition = new Vector2(0f, 8f);
        earningsRect.sizeDelta = new Vector2(90f, 26f);
        CanvasGroup earningsCanvas = earningsText.gameObject
            .AddComponent<CanvasGroup>();
        earningsCanvas.alpha = 0f;
        earningsCanvas.interactable = false;
        earningsCanvas.blocksRaycasts = false;

        RectTransform purchaseLock = CreateUiObject(
            "Additional Pen Lock Icon",
            templateRect);
        SetCenteredRect(
            purchaseLock,
            new Vector2(0f, 7f),
            new Vector2(34f, 40f));
        CreateLockPiece(
            purchaseLock,
            "Lock Body",
            new Vector2(0f, -5f),
            new Vector2(28f, 22f));
        CreateLockPiece(
            purchaseLock,
            "Left Shackle",
            new Vector2(-10f, 10f),
            new Vector2(5f, 15f));
        CreateLockPiece(
            purchaseLock,
            "Right Shackle",
            new Vector2(10f, 10f),
            new Vector2(5f, 15f));
        CreateLockPiece(
            purchaseLock,
            "Top Shackle",
            new Vector2(0f, 17f),
            new Vector2(25f, 5f));
        purchaseLock.gameObject.SetActive(false);

        PenButtonView view = templateRect.gameObject.AddComponent<PenButtonView>();
        SerializedObject serializedView = new SerializedObject(view);
        serializedView.FindProperty("button").objectReferenceValue = button;
        serializedView.FindProperty("background").objectReferenceValue = background;
        serializedView.FindProperty("penLabel").objectReferenceValue = penLabel;
        serializedView.FindProperty("purchaseLabel").objectReferenceValue = purchaseLabel;
        serializedView.FindProperty("progressRoot").objectReferenceValue = progress.gameObject;
        serializedView.FindProperty("progressFill").objectReferenceValue = fill;
        serializedView.FindProperty("earningsText").objectReferenceValue = earningsText;
        serializedView.FindProperty("earningsCanvasGroup").objectReferenceValue = earningsCanvas;
        serializedView.FindProperty("purchaseLockRoot").objectReferenceValue =
            purchaseLock.gameObject;
        serializedView.ApplyModifiedPropertiesWithoutUndo();
        PenButtonView[] authoredButtons =
            new PenButtonView[AuthoredPenButtonCount];
        for (int index = 0; index < authoredButtons.Length; index++)
        {
            PenButtonView authoredView = index == 0
                ? view
                : Object.Instantiate(view, content);
            authoredView.name = $"Pen {index + 1} Button";
            authoredView.ConfigureEditorPreview(
                index,
                focused: index == 0,
                eggsPerMinute: 0f);
            authoredButtons[index] = authoredView;
        }

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("buttonContent").objectReferenceValue = content;
        SerializedProperty buttonsProperty =
            serializedController.FindProperty("authoredButtons");
        buttonsProperty.arraySize = authoredButtons.Length;
        for (int index = 0; index < authoredButtons.Length; index++)
        {
            buttonsProperty.GetArrayElementAtIndex(index).objectReferenceValue =
                authoredButtons[index];
        }

        serializedController.ApplyModifiedPropertiesWithoutUndo();
        SetLayerRecursively(panel.gameObject, 5);
    }

    private static void ConfigurePenEquipmentHud(
        GameObject root,
        TMP_FontAsset font)
    {
        DestroyNamedDescendant(root.transform, "Local Pen Equipment");
        DestroyNamedDescendant(
            root.transform,
            "Pen Equipment Upgrade Dialog");

        PenEquipmentHudController controller =
            root.GetComponent<PenEquipmentHudController>();
        if (controller == null)
        {
            controller = root.AddComponent<PenEquipmentHudController>();
        }

        RectTransform panel = CreateUiObject(
            "Local Pen Equipment",
            root.transform);
        SetTopLeftRect(
            panel,
            new Vector2(24f, -24f),
            new Vector2(260f, 310f));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = GetUiSprite();
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.055f, 0.06f, 0.048f, 0.94f);
        ConfigureOutline(
            panel.gameObject,
            new Color(0.12f, 0.07f, 0.035f, 1f),
            new Vector2(2f, -2f));
        ConfigureShadow(
            panel.gameObject,
            new Color(0f, 0f, 0f, 0.5f),
            new Vector2(3f, -4f));

        TMP_Text panelTitle = CreateText(
            "Panel Title",
            panel,
            font,
            "PEN 1 TECH",
            15f,
            TextAlignmentOptions.Center);
        panelTitle.fontStyle = FontStyles.Bold;
        SetTopLeftRect(
            panelTitle.rectTransform,
            new Vector2(8f, -7f),
            new Vector2(244f, 25f));

        PenExpansionManager.EquipmentType[] types =
        {
            PenExpansionManager.EquipmentType.Incubator,
            PenExpansionManager.EquipmentType.Crosshatcher,
            PenExpansionManager.EquipmentType.Robot,
            PenExpansionManager.EquipmentType.AutoFeeder
        };
        string[] titles =
        {
            "INCUBATOR",
            "CROSSHATCHER",
            "ROBOT",
            "AUTO-FEEDER"
        };

        Button[] equipmentButtons = new Button[types.Length];
        Image[] equipmentBackgrounds = new Image[types.Length];
        TMP_Text[] equipmentTitles = new TMP_Text[types.Length];
        TMP_Text[] equipmentDetails = new TMP_Text[types.Length];
        TMP_Text[] equipmentActions = new TMP_Text[types.Length];
        GameObject[] equipmentProgressRoots = new GameObject[types.Length];
        Image[] equipmentProgressFills = new Image[types.Length];
        TMP_Text[] equipmentProgressTexts = new TMP_Text[types.Length];

        for (int index = 0; index < types.Length; index++)
        {
            RectTransform card = CreateUiObject(
                $"Local {titles[index]} Button",
                panel);
            SetTopLeftRect(
                card,
                new Vector2(8f, -38f - index * 66f),
                new Vector2(244f, 59f));
            Image background = card.gameObject.AddComponent<Image>();
            background.sprite = GetUiSprite();
            background.type = Image.Type.Sliced;
            background.color = new Color(0.16f, 0.24f, 0.14f, 1f);
            Button button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            TMP_Text title = CreateText(
                "Title", card, font, titles[index], 14f,
                TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            SetTopLeftRect(
                title.rectTransform,
                new Vector2(8f, -5f),
                new Vector2(130f, 22f));
            TMP_Text details = CreateText(
                "Details", card, font, "NOT OWNED", 10f,
                TextAlignmentOptions.BottomLeft);
            details.color = new Color(0.72f, 0.78f, 0.68f);
            SetTopLeftRect(
                details.rectTransform,
                new Vector2(8f, -28f),
                new Vector2(135f, 23f));
            TMP_Text action = CreateText(
                "Action", card, font, "SAVING", 11f,
                TextAlignmentOptions.Center);
            action.fontStyle = FontStyles.Bold;
            action.color = new Color(1f, 0.84f, 0.25f);
            SetTopLeftRect(
                action.rectTransform,
                new Vector2(142f, -8f),
                new Vector2(94f, 38f));

            RectTransform progress = CreateUiObject("Cash Progress", card);
            SetTopLeftRect(
                progress,
                new Vector2(142f, -42f),
                new Vector2(94f, 10f));
            Image progressBack = progress.gameObject.AddComponent<Image>();
            progressBack.color = new Color(0.08f, 0.08f, 0.06f, 1f);
            progressBack.raycastTarget = false;
            RectTransform fillRect = CreateUiObject("Fill", progress);
            Stretch(fillRect, 0f);
            Image fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = new Color(1f, 0.68f, 0.08f, 1f);
            fill.raycastTarget = false;
            TMP_Text progressText = CreateText(
                "Cash Text", card, font, "$0.00 / $0.00", 8f,
                TextAlignmentOptions.Center);
            progressText.fontStyle = FontStyles.Bold;
            SetTopLeftRect(
                progressText.rectTransform,
                new Vector2(137f, -32f),
                new Vector2(104f, 12f));

            equipmentButtons[index] = button;
            equipmentBackgrounds[index] = background;
            equipmentTitles[index] = title;
            equipmentDetails[index] = details;
            equipmentActions[index] = action;
            equipmentProgressRoots[index] = progress.gameObject;
            equipmentProgressFills[index] = fill;
            equipmentProgressTexts[index] = progressText;
        }

        RectTransform overlayRect = CreateUiObject(
            "Pen Equipment Upgrade Dialog",
            root.transform);
        Stretch(overlayRect, 0f);
        Image overlayImage = overlayRect.gameObject.AddComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.68f);
        RectTransform dialogCard = CreateUiObject("Upgrade Card", overlayRect);
        SetCenteredRect(dialogCard, Vector2.zero, new Vector2(520f, 330f));
        Image dialogCardImage = dialogCard.gameObject.AddComponent<Image>();
        dialogCardImage.sprite = GetUiSprite();
        dialogCardImage.type = Image.Type.Sliced;
        dialogCardImage.color = new Color(0.055f, 0.06f, 0.045f, 1f);
        ConfigureOutline(
            dialogCard.gameObject,
            new Color(0.35f, 0.22f, 0.06f, 1f),
            new Vector2(4f, -4f));

        TMP_Text dialogTitle = CreateText(
            "Dialog Title", dialogCard, font, "PEN 1 UPGRADES", 24f,
            TextAlignmentOptions.Center);
        dialogTitle.fontStyle = FontStyles.Bold;
        SetTopLeftRect(
            dialogTitle.rectTransform,
            new Vector2(50f, -18f),
            new Vector2(420f, 42f));

        RectTransform closeRect = CreateUiObject(
            "Close Local Upgrade Dialog",
            dialogCard);
        SetTopLeftRect(
            closeRect,
            new Vector2(466f, -16f),
            new Vector2(38f, 38f));
        Image closeImage = closeRect.gameObject.AddComponent<Image>();
        closeImage.sprite = GetUiSprite();
        closeImage.type = Image.Type.Sliced;
        closeImage.color = new Color(0.55f, 0.16f, 0.08f, 1f);
        Button closeButton = closeRect.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;
        TMP_Text closeLabel = CreateText(
            "Label", closeRect, font, "X", 22f,
            TextAlignmentOptions.Center);
        closeLabel.fontStyle = FontStyles.Bold;
        Stretch(closeLabel.rectTransform, 0f);

        RectTransform rows = CreateUiObject("Upgrade Rows", dialogCard);
        SetTopLeftRect(
            rows,
            new Vector2(28f, -78f),
            new Vector2(464f, 224f));
        Button[] upgradeButtons = new Button[4];
        TMP_Text[] upgradeLabels = new TMP_Text[4];
        Image[] upgradeFills = new Image[4];
        for (int index = 0; index < upgradeButtons.Length; index++)
        {
            RectTransform row = CreateUiObject(
                $"Authored Upgrade Slot {index + 1}",
                rows);
            SetTopLeftRect(
                row,
                new Vector2(0f, -index * 72f),
                new Vector2(464f, 64f));
            Image rowImage = row.gameObject.AddComponent<Image>();
            rowImage.sprite = GetUiSprite();
            rowImage.type = Image.Type.Sliced;
            rowImage.color = new Color(0.16f, 0.24f, 0.14f, 1f);
            Button rowButton = row.gameObject.AddComponent<Button>();
            rowButton.targetGraphic = rowImage;
            TMP_Text rowLabel = CreateText(
                "Label", row, font, "UPGRADE", 15f,
                TextAlignmentOptions.Center);
            rowLabel.fontStyle = FontStyles.Bold;
            Stretch(rowLabel.rectTransform, 10f);
            RectTransform rowProgress = CreateUiObject("Cash Progress", row);
            rowProgress.anchorMin = new Vector2(0f, 0f);
            rowProgress.anchorMax = new Vector2(1f, 0f);
            rowProgress.pivot = new Vector2(0.5f, 0f);
            rowProgress.anchoredPosition = new Vector2(0f, 3f);
            rowProgress.sizeDelta = new Vector2(-12f, 6f);
            Image rowProgressBack =
                rowProgress.gameObject.AddComponent<Image>();
            rowProgressBack.color = new Color(0.06f, 0.06f, 0.05f, 1f);
            rowProgressBack.raycastTarget = false;
            RectTransform rowFillRect = CreateUiObject("Fill", rowProgress);
            Stretch(rowFillRect, 0f);
            Image rowFill = rowFillRect.gameObject.AddComponent<Image>();
            rowFill.color = new Color(1f, 0.68f, 0.08f, 1f);
            rowFill.raycastTarget = false;
            upgradeButtons[index] = rowButton;
            upgradeLabels[index] = rowLabel;
            upgradeFills[index] = rowFill;
        }
        overlayRect.gameObject.SetActive(false);

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("panel").objectReferenceValue =
            panel.gameObject;
        serializedController.FindProperty("panelTitle").objectReferenceValue =
            panelTitle;
        serializedController.FindProperty("dialogOverlay").objectReferenceValue =
            overlayRect.gameObject;
        serializedController.FindProperty("dialogTitle").objectReferenceValue =
            dialogTitle;
        serializedController.FindProperty("dialogCloseButton")
            .objectReferenceValue = closeButton;

        SerializedProperty equipmentProperty =
            serializedController.FindProperty("equipmentViews");
        equipmentProperty.arraySize = types.Length;
        for (int index = 0; index < types.Length; index++)
        {
            SerializedProperty view =
                equipmentProperty.GetArrayElementAtIndex(index);
            view.FindPropertyRelative("type").enumValueIndex = (int)types[index];
            view.FindPropertyRelative("button").objectReferenceValue =
                equipmentButtons[index];
            view.FindPropertyRelative("background").objectReferenceValue =
                equipmentBackgrounds[index];
            view.FindPropertyRelative("title").objectReferenceValue =
                equipmentTitles[index];
            view.FindPropertyRelative("details").objectReferenceValue =
                equipmentDetails[index];
            view.FindPropertyRelative("action").objectReferenceValue =
                equipmentActions[index];
            view.FindPropertyRelative("progressRoot").objectReferenceValue =
                equipmentProgressRoots[index];
            view.FindPropertyRelative("progressFill").objectReferenceValue =
                equipmentProgressFills[index];
            view.FindPropertyRelative("progressText").objectReferenceValue =
                equipmentProgressTexts[index];
        }

        SerializedProperty upgradesProperty =
            serializedController.FindProperty("upgradeViews");
        upgradesProperty.arraySize = upgradeButtons.Length;
        for (int index = 0; index < upgradeButtons.Length; index++)
        {
            SerializedProperty view =
                upgradesProperty.GetArrayElementAtIndex(index);
            view.FindPropertyRelative("button").objectReferenceValue =
                upgradeButtons[index];
            view.FindPropertyRelative("label").objectReferenceValue =
                upgradeLabels[index];
            view.FindPropertyRelative("progressFill").objectReferenceValue =
                upgradeFills[index];
        }
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        SetLayerRecursively(panel.gameObject, 5);
        SetLayerRecursively(overlayRect.gameObject, 5);
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
            new Vector2(0f, 70f - index * 28f),
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
            "0",
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

    private static void CreateLockPiece(
        Transform parent,
        string objectName,
        Vector2 position,
        Vector2 size)
    {
        RectTransform rect = CreateUiObject(objectName, parent);
        SetCenteredRect(rect, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(1f, 0.72f, 0.14f, 1f);
        image.raycastTarget = false;
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
        SetBottomLeftRect(rect, position, new Vector2(64f, 64f));
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

    private static Button CreateTurboButton(
        Transform parent,
        TMP_FontAsset font,
        TurboConsumableSystem.TurboType type,
        Vector2 position,
        Color color,
        out Image frameImage,
        out TMP_Text countText,
        out TMP_Text timerText)
    {
        RectTransform rect = CreateUiObject(
            $"{TurboConsumableSystem.GetDisplayName(type)} Turbo Button",
            parent);
        SetBottomLeftRect(rect, position, new Vector2(64f, 64f));
        frameImage = rect.gameObject.AddComponent<Image>();
        frameImage.sprite = GetUiSprite();
        frameImage.type = Image.Type.Sliced;
        frameImage.color = color;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = frameImage;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        StyleToolButton(button, frameImage, color);

        RectTransform iconRect = CreateUiObject("Turbo Icon", rect);
        SetCenteredRect(iconRect, new Vector2(0f, 2f), new Vector2(55f, 55f));
        RawImage icon = iconRect.gameObject.AddComponent<RawImage>();
        icon.texture = Resources.Load<Texture2D>(
            TurboConsumableSystem.GetResourcePath(type));
        icon.color = Color.white;
        icon.raycastTarget = false;

        countText = CreateHudCounterText(
            "Owned Count",
            rect,
            font,
            new Vector2(4f, 3f),
            new Vector2(34f, 18f),
            12f,
            TextAlignmentOptions.BottomLeft);
        countText.text = "x0";
        timerText = CreateHudCounterText(
            "Active Timer",
            rect,
            font,
            new Vector2(3f, 44f),
            new Vector2(58f, 17f),
            11f,
            TextAlignmentOptions.TopRight);
        SetLayerRecursively(rect.gameObject, 5);
        return button;
    }

    private static TMP_Text CreateHudCounterText(
        string objectName,
        Transform parent,
        TMP_FontAsset font,
        Vector2 position,
        Vector2 size,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        TMP_Text text = CreateText(
            objectName,
            parent,
            font,
            string.Empty,
            fontSize,
            alignment);
        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        text.fontStyle = FontStyles.Bold;
        text.outlineWidth = 0.25f;
        text.outlineColor = new Color32(22, 12, 4, 255);
        return text;
    }

    private static void AssignObjectArray<T>(
        SerializedProperty property,
        T[] values)
        where T : Object
    {
        property.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++)
        {
            property.GetArrayElementAtIndex(index).objectReferenceValue =
                values[index];
        }
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

    private static void SetBottomLeftRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetTopLeftRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
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

}
