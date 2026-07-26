using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class GameplayTestBotSetup
{
    private const string PrefabPath =
        "Assets/Testing/prefabs/prefab_GameplayTestBot.prefab";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string FontPath = "Assets/Fonts/Cat Song SDF.asset";

    [MenuItem("Tools/Eggcessive/Testing/Build Automated Gameplay Test Bot")]
    public static void Build()
    {
        EnsureFolder("Assets", "Testing");
        EnsureFolder("Assets/Testing", "prefabs");
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        GameObject root = new GameObject("[TESTING] Gameplay Test Bot");

        try
        {
            GameplayTestBot bot = root.AddComponent<GameplayTestBot>();
            GameObject canvasObject = new GameObject(
                "Test Bot Status Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(CanvasGroup));
            canvasObject.transform.SetParent(root.transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            CanvasGroup canvasGroup = canvasObject.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            RectTransform panel = CreateUiObject("Status Panel", canvasObject.transform);
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(18f, -18f);
            panel.sizeDelta = new Vector2(470f, 74f);
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.05f, 0.075f, 0.07f, 0.88f);
            panelImage.raycastTarget = false;

            RectTransform accent = CreateUiObject("Accent", panel);
            accent.anchorMin = new Vector2(0f, 0f);
            accent.anchorMax = new Vector2(0f, 1f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.anchoredPosition = Vector2.zero;
            accent.sizeDelta = new Vector2(7f, 0f);
            Image accentImage = accent.gameObject.AddComponent<Image>();
            accentImage.color = new Color(1f, 0.72f, 0.14f);
            accentImage.raycastTarget = false;

            RectTransform textRect = CreateUiObject("Status Text", panel);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(22f, 8f);
            textRect.offsetMax = new Vector2(-12f, -8f);
            TextMeshProUGUI status = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            status.font = font;
            status.fontSize = 18f;
            status.color = Color.white;
            status.alignment = TextAlignmentOptions.MidlineLeft;
            status.textWrappingMode = TextWrappingModes.NoWrap;
            status.raycastTarget = false;
            status.text = "TEST BOT\nOFF  •  F8 TO START";

            SerializedObject serializedBot = new SerializedObject(bot);
            serializedBot.FindProperty("statusText").objectReferenceValue = status;
            serializedBot.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            WireIntoScene(prefab);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "Automated gameplay test bot prefab built and added to SampleScene. " +
            "Press F8 in Play Mode to start or stop it.");
    }

    [MenuItem("Tools/Eggcessive/Testing/Enable Test Bot On Play")]
    public static void EnableOnPlay()
    {
        SetSceneStartEnabled(true);
    }

    [MenuItem("Tools/Eggcessive/Testing/Disable Test Bot On Play")]
    public static void DisableOnPlay()
    {
        SetSceneStartEnabled(false);
    }

    [MenuItem("Tools/Eggcessive/Testing/Validate Gameplay Test Bot")]
    public static void Validate()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            throw new InvalidOperationException($"Missing test bot prefab at {PrefabPath}.");
        }

        GameplayTestBot prefabBot = prefab.GetComponent<GameplayTestBot>();

        if (prefabBot == null)
        {
            throw new MissingComponentException(nameof(GameplayTestBot));
        }

        SerializedObject serializedBot = new SerializedObject(prefabBot);

        if (serializedBot.FindProperty("statusText").objectReferenceValue == null)
        {
            throw new MissingReferenceException("The test bot status UI is not authored.");
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameplayTestBot sceneBot = FindSceneBot(scene);

        if (sceneBot == null)
        {
            throw new MissingReferenceException(
                "SampleScene does not contain the automated gameplay test bot.");
        }

        Debug.Log("Gameplay test bot validation passed: prefab, authored UI, and scene instance are valid.");
    }

    private static void WireIntoScene(GameObject prefab)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameplayTestBot existing = FindSceneBot(scene);
        bool wasEnabled = false;

        if (existing != null)
        {
            SerializedObject existingBot = new SerializedObject(existing);
            wasEnabled = existingBot.FindProperty("startEnabled").boolValue;
            Object.DestroyImmediate(existing.gameObject);
        }

        GameObject instance =
            PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;

        if (instance == null)
        {
            throw new InvalidOperationException("Could not instantiate the gameplay test bot.");
        }

        SerializedObject serializedBot =
            new SerializedObject(instance.GetComponent<GameplayTestBot>());
        serializedBot.FindProperty("startEnabled").boolValue = wasEnabled;
        serializedBot.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetSceneStartEnabled(bool enabled)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameplayTestBot bot = FindSceneBot(scene);

        if (bot == null)
        {
            Build();
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            bot = FindSceneBot(scene);
        }

        if (bot == null)
        {
            throw new MissingReferenceException("Could not create the gameplay test bot.");
        }

        SerializedObject serializedBot = new SerializedObject(bot);
        serializedBot.FindProperty("startEnabled").boolValue = enabled;
        serializedBot.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(bot);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Gameplay test bot will {(enabled ? "start automatically" : "wait for F8")} in Play Mode.");
    }

    private static GameplayTestBot FindSceneBot(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            GameplayTestBot bot = root.GetComponentInChildren<GameplayTestBot>(true);

            if (bot != null)
            {
                return bot;
            }
        }

        return null;
    }

    private static RectTransform CreateUiObject(string objectName, Transform parent)
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform));
        RectTransform rectTransform = uiObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        return rectTransform;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";

        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
