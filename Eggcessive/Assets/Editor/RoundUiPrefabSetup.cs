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
    private const string FontPath = "Assets/Fonts/Cat Song SDF.asset";
    private const string CoinModelPath = "Assets/UI/meshes/ui_coin.fbx";
    private const string CoinMaterialPath = "Assets/UI/materials/mat_ui_coin.mat";
    private const string TruckVisualPath = "Assets/Truck/meshes/truck.fbx";
    private const string CashRegisterSfxPath =
        "Assets/Sounds/UI/sfx_ui_cashregister.wav";
    private const string ButtonClickSfxPath =
        "Assets/Sounds/UI/sfx_ui_click.wav";
    private const string CountdownTickSfxPath =
        "Assets/Sounds/UI/sfx_ui_countdown_tick.wav";
    private const string RoundStartSfxPath =
        "Assets/Sounds/UI/sfx_ui_round_start.wav";
    private const string RoundEndSfxPath =
        "Assets/Sounds/UI/sfx_ui_round_end.wav";
    private const string TruckEnterSfxPath =
        "Assets/Sounds/UI/sfx_truck_enter.wav";
    private const string TruckExitSfxPath =
        "Assets/Sounds/UI/sfx_truck_exit.wav";
    private const string FarmAmbienceSfxPath =
        "Assets/Sounds/UI/sfx_farm_ambience.wav";
    private const string GrabSfxPath =
        "Assets/Sounds/UI/sfx_grab.wav";
    private const string VacuumOnSfxPath =
        "Assets/Sounds/UI/sfx_vacuum_on.wav";
    private const string VacuumEggSfxPath =
        "Assets/Sounds/UI/sfx_vacuum_egg.wav";
    private const string FoodPickupSfxPath =
        "Assets/Sounds/UI/sfx_food_pickup.wav";
    private const string FoodPlaceSfxPath =
        "Assets/Sounds/UI/sfx_food_place.wav";
    private const string CursorMovementSfxPath =
        "Assets/Sounds/UI/sfx_cursor_movement.wav";
    private static readonly string[] CoinLandingSfxPaths =
    {
        "Assets/Sounds/UI/sfx_ui_coin_01.wav",
        "Assets/Sounds/UI/sfx_ui_coin_02.wav",
        "Assets/Sounds/UI/sfx_ui_coin_03.wav",
        "Assets/Sounds/UI/sfx_ui_coin_04.wav"
    };

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
            GameplayHudPrefabSetup.ConfigureRoundHud(root);
            ConfigureEventSystem(root, uiInputActions);

            SerializedObject serializedSystem = new SerializedObject(roundSystem);
            serializedSystem.FindProperty("flyingCoinPrefab").objectReferenceValue =
                flyingCoinPrefab;
            serializedSystem.FindProperty("floatingRewardPrefab").objectReferenceValue =
                floatingRewardPrefab;
            serializedSystem.FindProperty("truckVisualPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(TruckVisualPath);
            serializedSystem.FindProperty("cashRegisterSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(CashRegisterSfxPath);
            serializedSystem.FindProperty("buttonClickSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(ButtonClickSfxPath);
            serializedSystem.FindProperty("countdownTickSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(CountdownTickSfxPath);
            serializedSystem.FindProperty("roundStartSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(RoundStartSfxPath);
            serializedSystem.FindProperty("roundEndSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(RoundEndSfxPath);
            serializedSystem.FindProperty("truckEnterSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(TruckEnterSfxPath);
            serializedSystem.FindProperty("truckExitSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(TruckExitSfxPath);
            serializedSystem.FindProperty("farmAmbienceSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(FarmAmbienceSfxPath);
            serializedSystem.FindProperty("grabSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(GrabSfxPath);
            serializedSystem.FindProperty("vacuumOnSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(VacuumOnSfxPath);
            serializedSystem.FindProperty("vacuumEggSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(VacuumEggSfxPath);
            serializedSystem.FindProperty("foodPickupSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(FoodPickupSfxPath);
            serializedSystem.FindProperty("foodPlaceSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(FoodPlaceSfxPath);
            serializedSystem.FindProperty("cursorMovementSfx").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(CursorMovementSfxPath);
            SerializedProperty coinSfx =
                serializedSystem.FindProperty("coinLandingSfx");
            coinSfx.arraySize = CoinLandingSfxPaths.Length;
            for (int index = 0; index < CoinLandingSfxPaths.Length; index++)
            {
                coinSfx.GetArrayElementAtIndex(index).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>(
                        CoinLandingSfxPaths[index]);
            }
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
        ValidateInputReferences();
        Debug.Log("Round UI, input actions, flying coin, and floating reward prefabs rebuilt.");
    }

    [MenuItem("Tools/Eggcessive/Validate Round UI Input")]
    public static void ValidateInputReferences()
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(RoundPrefabPath);
        if (prefab == null)
        {
            throw new MissingReferenceException(
                $"Missing round UI prefab at {RoundPrefabPath}.");
        }

        InputSystemUIInputModule inputModule =
            prefab.GetComponentInChildren<InputSystemUIInputModule>(true);
        if (inputModule == null)
        {
            throw new MissingComponentException(
                nameof(InputSystemUIInputModule));
        }

        SerializedObject serializedModule = new SerializedObject(inputModule);
        string[] requiredReferences =
        {
            "m_ActionsAsset",
            "m_PointAction",
            "m_MoveAction",
            "m_SubmitAction",
            "m_CancelAction",
            "m_LeftClickAction",
            "m_RightClickAction",
            "m_ScrollWheelAction"
        };
        for (int index = 0; index < requiredReferences.Length; index++)
        {
            SerializedProperty property =
                serializedModule.FindProperty(requiredReferences[index]);
            if (property == null || property.objectReferenceValue == null)
            {
                throw new MissingReferenceException(
                    $"Round UI input reference {requiredReferences[index]} " +
                    "is missing or unresolved.");
            }
        }

        Button readyButton = null;
        foreach (Button button in prefab.GetComponentsInChildren<Button>(true))
        {
            if (button.name == "Ready Button")
            {
                readyButton = button;
                break;
            }
        }

        if (readyButton == null)
        {
            throw new MissingReferenceException(
                "The authored Ready Button is missing.");
        }

        ValidateProgressionLayout(prefab);
        Debug.Log(
            "Round UI input validation passed: Point, Click, navigation, " +
            "the authored Ready Button, and progression-tree spacing are valid.");
    }

    private static void ValidateProgressionLayout(GameObject prefab)
    {
        RectTransform treeContent = Array.Find(
            prefab.GetComponentsInChildren<RectTransform>(true),
            rect => rect.name == "Tree Content");

        if (treeContent == null)
        {
            throw new MissingReferenceException(
                "The supplies-shop progression tree content is missing.");
        }

        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);

        for (int firstIndex = 0; firstIndex < nodes.Length; firstIndex++)
        {
            RectTransform first = nodes[firstIndex].GetComponent<RectTransform>();
            Rect firstBounds = GetAnchoredRect(first);

            for (int secondIndex = firstIndex + 1;
                secondIndex < nodes.Length;
                secondIndex++)
            {
                RectTransform second =
                    nodes[secondIndex].GetComponent<RectTransform>();
                Rect secondBounds = GetAnchoredRect(second);

                if (firstBounds.Overlaps(secondBounds))
                {
                    throw new InvalidOperationException(
                        $"Progression nodes '{first.name}' and '{second.name}' overlap.");
                }
            }
        }

        ProgressionNodeButton crosshatcher = Array.Find(
            nodes,
            node => node.UpgradeId
                == ProgressionSystem.UpgradeId.CrosshatcherInstall);
        TMP_Text crosshatcherLabel = crosshatcher != null
            ? crosshatcher.GetComponentInChildren<TMP_Text>(true)
            : null;

        if (crosshatcher == null
            || crosshatcher.GetComponent<RectTransform>().rect.width < 180f
            || crosshatcherLabel == null
            || crosshatcherLabel.textWrappingMode != TextWrappingModes.NoWrap)
        {
            throw new InvalidOperationException(
                "The Crosshatcher root node must be at least 180px wide and use no-wrap text.");
        }
    }

    private static Rect GetAnchoredRect(RectTransform rectTransform)
    {
        Rect rect = rectTransform.rect;
        Vector2 position = rectTransform.anchoredPosition;
        return new Rect(
            position.x + rect.xMin,
            position.y + rect.yMin,
            rect.width,
            rect.height);
    }

    private static InputActionAsset CreateUiInputActions()
    {
        InputActionAsset existing =
            AssetDatabase.LoadAssetAtPath<InputActionAsset>(UiInputActionsPath);
        if (existing != null)
        {
            return existing;
        }

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
        GameObject coinModel =
            AssetDatabase.LoadAssetAtPath<GameObject>(CoinModelPath);
        Material coinMaterial =
            AssetDatabase.LoadAssetAtPath<Material>(CoinMaterialPath);
        if (coinModel == null || coinMaterial == null)
        {
            throw new MissingReferenceException(
                "The flying coin model or material could not be loaded.");
        }

        GameObject root = new GameObject(
            "prefab_FlyingCoin",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(UiModelGraphic));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(40f, 40f);
        UiModelGraphic graphic = root.GetComponent<UiModelGraphic>();
        graphic.SetSourceModel(coinModel);
        graphic.material = coinMaterial;
        graphic.raycastTarget = false;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, FlyingCoinPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject CreateFloatingRewardPrefab()
    {
        TMP_FontAsset font = LoadUiFont();
        GameObject root = new GameObject(
            "prefab_FloatingReward",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1100f, 90f);
        TextMeshProUGUI reward = root.GetComponent<TextMeshProUGUI>();
        reward.font = font;
        reward.fontSize = 34f;
        reward.enableAutoSizing = false;
        reward.textWrappingMode = TextWrappingModes.NoWrap;
        reward.overflowMode = TextOverflowModes.Overflow;
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

    private static TMP_FontAsset LoadUiFont()
    {
        TMP_FontAsset font =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            throw new MissingReferenceException(
                $"Missing UI font at {FontPath}.");
        }

        return font;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
