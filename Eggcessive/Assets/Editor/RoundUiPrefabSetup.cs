using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class RoundUiPrefabSetup
{
    private const string RoundPrefabPath = "Assets/UI/prefab_RoundSystem.prefab";
    private const string FlyingCoinPrefabPath = "Assets/UI/prefab_FlyingCoin.prefab";
    private const string FloatingRewardPrefabPath = "Assets/UI/prefab_FloatingReward.prefab";
    private const string UiInputActionsPath = "Assets/UI/RoundUiInputActions.asset";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/Eggcessive/Rebuild Round UI Prefabs")]
    public static void Generate()
    {
        GameObject flyingCoinPrefab = CreateFlyingCoinPrefab();
        GameObject floatingRewardPrefab = CreateFloatingRewardPrefab();
        InputActionAsset uiInputActions = CreateUiInputActions();

        GameObject root = new GameObject("Round System");

        try
        {
            RoundSystem roundSystem = root.AddComponent<RoundSystem>();
            MethodInfo buildMethod = typeof(RoundSystem).GetMethod(
                "BuildRoundUi",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (buildMethod == null)
            {
                throw new MissingMethodException(
                    nameof(RoundSystem),
                    "BuildRoundUi");
            }

            buildMethod.Invoke(roundSystem, null);
            ConfigureEventSystem(root, uiInputActions);

            SerializedObject serializedSystem = new SerializedObject(roundSystem);
            serializedSystem.FindProperty("flyingCoinPrefab").objectReferenceValue =
                flyingCoinPrefab;
            serializedSystem.FindProperty("floatingRewardPrefab").objectReferenceValue =
                floatingRewardPrefab;
            serializedSystem.ApplyModifiedPropertiesWithoutUndo();

            GameObject roundPrefab = PrefabUtility.SaveAsPrefabAsset(root, RoundPrefabPath);
            WireRoundPrefabIntoScene(roundPrefab);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Round UI, input actions, flying coin, and floating reward prefabs rebuilt.");
    }

    private static InputActionAsset CreateUiInputActions()
    {
        AssetDatabase.DeleteAsset(UiInputActionsPath);

        DefaultInputActions defaults = new DefaultInputActions();
        InputActionAsset actions = Object.Instantiate(defaults.asset);
        actions.name = "Round UI Input Actions";
        AssetDatabase.CreateAsset(actions, UiInputActionsPath);
        AddActionReference(actions, "UI/Point");
        AddActionReference(actions, "UI/Navigate");
        AddActionReference(actions, "UI/Submit");
        AddActionReference(actions, "UI/Cancel");
        AddActionReference(actions, "UI/Click");
        AddActionReference(actions, "UI/MiddleClick");
        AddActionReference(actions, "UI/RightClick");
        AddActionReference(actions, "UI/ScrollWheel");
        AddActionReference(actions, "UI/TrackedDevicePosition");
        AddActionReference(actions, "UI/TrackedDeviceOrientation");
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(UiInputActionsPath, ImportAssetOptions.ForceUpdate);
        Object.DestroyImmediate(defaults.asset);
        return AssetDatabase.LoadAssetAtPath<InputActionAsset>(UiInputActionsPath);
    }

    private static void ConfigureEventSystem(GameObject root, InputActionAsset actions)
    {
        InputSystemUIInputModule inputModule =
            root.GetComponentInChildren<InputSystemUIInputModule>(true);

        if (inputModule == null)
        {
            throw new MissingComponentException(nameof(InputSystemUIInputModule));
        }

        SerializedObject serializedModule = new SerializedObject(inputModule);
        serializedModule.FindProperty("m_ActionsAsset").objectReferenceValue = actions;
        SetActionReference(serializedModule, "m_PointAction", "UI/Point");
        SetActionReference(serializedModule, "m_MoveAction", "UI/Navigate");
        SetActionReference(serializedModule, "m_SubmitAction", "UI/Submit");
        SetActionReference(serializedModule, "m_CancelAction", "UI/Cancel");
        SetActionReference(serializedModule, "m_LeftClickAction", "UI/Click");
        SetActionReference(serializedModule, "m_MiddleClickAction", "UI/MiddleClick");
        SetActionReference(serializedModule, "m_RightClickAction", "UI/RightClick");
        SetActionReference(serializedModule, "m_ScrollWheelAction", "UI/ScrollWheel");
        SetActionReference(
            serializedModule,
            "m_TrackedDevicePositionAction",
            "UI/TrackedDevicePosition");
        SetActionReference(
            serializedModule,
            "m_TrackedDeviceOrientationAction",
            "UI/TrackedDeviceOrientation");
        serializedModule.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddActionReference(
        InputActionAsset actions,
        string actionPath)
    {
        InputActionReference reference = InputActionReference.Create(
            actions.FindAction(actionPath, true));
        reference.name = actionPath.Replace('/', ' ');
        AssetDatabase.AddObjectToAsset(reference, actions);
    }

    private static InputActionReference LoadActionReference(string actionPath)
    {
        string referenceName = actionPath.Replace('/', ' ');

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(UiInputActionsPath))
        {
            if (asset is InputActionReference reference
                && reference.name == referenceName)
            {
                return reference;
            }
        }

        throw new MissingReferenceException(
            $"Missing input action reference {actionPath}.");
    }

    private static void SetActionReference(
        SerializedObject serializedModule,
        string propertyName,
        string actionPath)
    {
        serializedModule.FindProperty(propertyName).objectReferenceValue =
            LoadActionReference(actionPath);
    }

    private static void WireRoundPrefabIntoScene(GameObject roundPrefab)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RoundSystem existingSystem = Object.FindFirstObjectByType<RoundSystem>(
            FindObjectsInactive.Include);

        if (existingSystem == null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(roundPrefab);
            SceneManager.MoveGameObjectToScene(instance, scene);
        }

        EditorSceneManager.SaveScene(scene);
    }

    private static GameObject CreateFlyingCoinPrefab()
    {
        GameObject root = new GameObject(
            "prefab_FlyingCoin",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(34f, 34f);
        Image image = root.GetComponent<Image>();
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        image.color = new Color(0.96f, 0.64f, 0.08f);
        image.raycastTarget = false;

        GameObject symbolObject = new GameObject(
            "Symbol",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform symbolRect = symbolObject.GetComponent<RectTransform>();
        symbolRect.SetParent(rect, false);
        Stretch(symbolRect);
        TextMeshProUGUI symbol = symbolObject.GetComponent<TextMeshProUGUI>();
        symbol.text = "$";
        symbol.fontSize = 17f;
        symbol.alignment = TextAlignmentOptions.Center;
        symbol.color = Color.white;
        symbol.fontStyle = FontStyles.Bold;
        symbol.raycastTarget = false;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, FlyingCoinPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject CreateFloatingRewardPrefab()
    {
        GameObject root = new GameObject(
            "prefab_FloatingReward",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(260f, 70f);
        TextMeshProUGUI reward = root.GetComponent<TextMeshProUGUI>();
        reward.fontSize = 34f;
        reward.alignment = TextAlignmentOptions.Center;
        reward.color = new Color(1f, 0.82f, 0.18f);
        reward.fontStyle = FontStyles.Bold;
        reward.outlineWidth = 0.2f;
        reward.outlineColor = new Color32(70, 38, 8, 255);
        reward.raycastTarget = false;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, FloatingRewardPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
