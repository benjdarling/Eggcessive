using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class EggcessiveUnlockedPopupSetup
{
    private const string PrefabPath =
        "Assets/Resources/UI/prefab_EggcessiveUnlocked.prefab";
    private const string VictorySoundPath = "Assets/Sounds/victory.wav";
    private const string ConfettiPrefabPath =
        "Assets/VFX/prefabs/vfx_confetti.prefab";
    private const string DefaultFontPath =
        "Assets/Fonts/Cat Song SDF.asset";

    private static readonly Color EggYellow =
        new Color(1f, 0.78f, 0.2f, 1f);
    private static readonly Color ButtonTextColor =
        new Color(0.055f, 0.04f, 0.012f, 1f);

    static EggcessiveUnlockedPopupSetup()
    {
        EditorApplication.delayCall += EnsurePrefabExists;
    }

    [MenuItem("Tools/Eggcessive/Rebuild Eggcessive Unlocked Popup Prefab...")]
    public static void RebuildPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null
            && !EditorUtility.DisplayDialog(
                "Rebuild Eggcessive Unlocked Popup?",
                "This resets the editable level-100 popup to its generated "
                + "defaults and overwrites manual layout changes.",
                "Rebuild",
                "Cancel"))
        {
            return;
        }

        BuildPrefab();
    }

    private static void EnsurePrefabExists()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            BuildPrefab();
            return;
        }

        EnsureEffectsAreConfigured();
    }

    private static void BuildPrefab()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            DefaultFontPath);
        AudioClip victorySound = AssetDatabase.LoadAssetAtPath<AudioClip>(
            VictorySoundPath);
        GameObject confettiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            ConfettiPrefabPath);
        if (font == null || victorySound == null || confettiPrefab == null)
        {
            Debug.LogError(
                "Cannot build the Eggcessive-unlocked popup prefab because "
                + "its font, victory.wav, or vfx_confetti prefab is missing.");
            return;
        }

        GameObject root = new GameObject(
            "Eggcessive Unlocked",
            typeof(RectTransform));
        try
        {
            ConfigureReferenceCanvas(root);

            AudioSource victoryAudio = root.AddComponent<AudioSource>();
            victoryAudio.clip = victorySound;
            victoryAudio.playOnAwake = false;
            victoryAudio.loop = false;
            victoryAudio.spatialBlend = 0f;
            victoryAudio.ignoreListenerPause = true;

            EggcessiveUnlockedPopupEffects effects =
                root.AddComponent<EggcessiveUnlockedPopupEffects>();
            AssignConfettiPrefab(effects, confettiPrefab);

            CreateText(
                "Heading",
                root.transform,
                "EGGCESSIVE UNLOCKED",
                76f,
                new Vector2(0f, 285f),
                new Vector2(1450f, 150f),
                EggYellow,
                FontStyles.Bold,
                TextWrappingModes.NoWrap,
                font);
            CreateText(
                "Retirement Message",
                root.transform,
                "LEVEL 100 COMPLETE. YOUR LEGACY IS SECURE.\n\n"
                + "RETIRE TO THE MAIN MENU TO BEGIN EGGCESSIVE MODE,\n"
                + "OR CONTINUE PUSHING THIS FARM BEYOND REASON.",
                28f,
                new Vector2(0f, 80f),
                new Vector2(1050f, 230f),
                Color.white,
                FontStyles.Normal,
                TextWrappingModes.Normal,
                font);
            CreateButton(root.transform, "RETIRE", -105f, font);
            CreateButton(root.transform, "CONTINUE", -205f, font);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Created editable Eggcessive-unlocked popup prefab at "
                + PrefabPath
                + ".");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void EnsureEffectsAreConfigured()
    {
        GameObject confettiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            ConfettiPrefabPath);
        if (confettiPrefab == null)
        {
            Debug.LogError(
                "Cannot configure the Eggcessive-unlocked popup because "
                + ConfettiPrefabPath
                + " is missing.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        bool changed = false;
        try
        {
            AudioSource victoryAudio = root.GetComponent<AudioSource>();
            if (victoryAudio != null && !victoryAudio.ignoreListenerPause)
            {
                victoryAudio.ignoreListenerPause = true;
                changed = true;
            }

            EggcessiveUnlockedPopupEffects effects =
                root.GetComponent<EggcessiveUnlockedPopupEffects>();
            if (effects == null)
            {
                effects = root.AddComponent<EggcessiveUnlockedPopupEffects>();
                changed = true;
            }

            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                Image image = button.targetGraphic as Image
                    ?? button.GetComponent<Image>();
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (image == null || image.color.a >= 0.01f)
                {
                    continue;
                }

                image.color = EggYellow;
                if (label != null)
                {
                    label.color = ButtonTextColor;
                    RectTransform rect = button.GetComponent<RectTransform>();
                    rect.SetSizeWithCurrentAnchors(
                        RectTransform.Axis.Horizontal,
                        GetButtonWidth(label.text, label.fontSize));
                }

                changed = true;
            }

            SerializedObject serializedEffects = new SerializedObject(effects);
            SerializedProperty confettiProperty =
                serializedEffects.FindProperty("confettiPrefab");
            if (confettiProperty.objectReferenceValue != confettiPrefab)
            {
                confettiProperty.objectReferenceValue = confettiPrefab;
                serializedEffects.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "Added the confetti effect to the existing editable "
                    + "Eggcessive-unlocked popup prefab without changing its UI layout.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AssignConfettiPrefab(
        EggcessiveUnlockedPopupEffects effects,
        GameObject confettiPrefab)
    {
        SerializedObject serializedEffects = new SerializedObject(effects);
        serializedEffects.FindProperty("confettiPrefab").objectReferenceValue =
            confettiPrefab;
        serializedEffects.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateButton(
        Transform parent,
        string label,
        float y,
        TMP_FontAsset font)
    {
        GameObject buttonObject = CreateUiObject(label + " Button", parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        SetCenteredRect(
            rect,
            new Vector2(0f, y),
            new Vector2(GetButtonWidth(label, 48f), 76f));

        Image image = buttonObject.AddComponent<Image>();
        image.color = EggYellow;
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
            ButtonTextColor,
            FontStyles.Bold,
            TextWrappingModes.NoWrap,
            font);
        StretchToParent(text.rectTransform);
    }

    private static float GetButtonWidth(string label, float fontSize)
    {
        return Mathf.Clamp(
            label.Length * fontSize * 0.62f + 96f,
            240f,
            700f);
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
        TextWrappingModes wrapping,
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
        text.textWrappingMode = wrapping;
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
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5001;

        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<GraphicRaycaster>();
    }
}
