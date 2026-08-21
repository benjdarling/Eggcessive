using UnityEditor;
using UnityEngine;

public sealed class EggcessiveUnlockVfxTunerWindow : EditorWindow
{
    private const string PopupPrefabPath =
        "Assets/Resources/UI/prefab_EggcessiveUnlocked.prefab";
    private const string LiveReplayPreference =
        "Eggcessive.UnlockVfxTuner.LiveReplay";

    private GameObject popupPrefab;
    private EggcessiveUnlockedPopupEffects effects;
    private SerializedObject serializedEffects;
    private bool liveReplay;

    [MenuItem("Tools/Eggcessive/Unlock Popup VFX Tuner")]
    private static void Open()
    {
        EggcessiveUnlockVfxTunerWindow window =
            GetWindow<EggcessiveUnlockVfxTunerWindow>(
                "Unlock VFX Tuner");
        window.minSize = new Vector2(390f, 330f);
        window.Show();
    }

    private void OnEnable()
    {
        liveReplay = EditorPrefs.GetBool(LiveReplayPreference, true);
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        LoadPrefabSettings();
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
    }

    private void HandlePlayModeStateChanged(PlayModeStateChange _)
    {
        Repaint();
    }

    private void LoadPrefabSettings()
    {
        popupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            PopupPrefabPath);
        effects = popupPrefab != null
            ? popupPrefab.GetComponent<EggcessiveUnlockedPopupEffects>()
            : null;
        serializedEffects = effects != null
            ? new SerializedObject(effects)
            : null;
    }

    private void OnGUI()
    {
        if (effects == null || serializedEffects == null)
        {
            LoadPrefabSettings();
        }

        EditorGUILayout.LabelField(
            "Level-100 Confetti",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "These values are saved directly to the unlock popup prefab. "
            + "Enter Play Mode, show the popup, then tune and replay here "
            + "against the real gameplay camera.",
            MessageType.Info);

        if (effects == null || serializedEffects == null)
        {
            EditorGUILayout.HelpBox(
                "The editable unlock popup prefab or its effects component "
                + "could not be found.",
                MessageType.Error);
            if (GUILayout.Button("Reload Prefab"))
            {
                LoadPrefabSettings();
            }

            return;
        }

        serializedEffects.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(
            serializedEffects.FindProperty("confettiPrefab"));
        EditorGUILayout.Space(4f);
        SerializedProperty offsetProperty =
            serializedEffects.FindProperty("cameraLocalPosition");
        Vector3 offset = offsetProperty.vector3Value;
        EditorGUILayout.LabelField("Camera-relative Position", EditorStyles.boldLabel);
        offset.x = EditorGUILayout.Slider("Horizontal", offset.x, -20f, 20f);
        offset.y = EditorGUILayout.Slider("Vertical", offset.y, -12f, 12f);
        offset.z = EditorGUILayout.Slider(
            new GUIContent(
                "Distance",
                "Distance in front of the actual gameplay camera."),
            offset.z,
            0.01f,
            50f);
        offsetProperty.vector3Value = offset;
        Camera gameplayCamera = EditorApplication.isPlaying
            ? Camera.main
            : null;
        if (gameplayCamera != null
            && offset.z < gameplayCamera.nearClipPlane)
        {
            EditorGUILayout.HelpBox(
                $"The emitter is inside the gameplay camera's "
                + $"{gameplayCamera.nearClipPlane:0.###} near clip plane. "
                + "It is allowed, but particles will only become visible "
                + "after moving in front of that plane.",
                MessageType.Warning);
        }

        EditorGUILayout.PropertyField(
            serializedEffects.FindProperty("cameraLocalEulerAngles"),
            new GUIContent("Camera-relative Rotation"));
        SerializedProperty scaleProperty =
            serializedEffects.FindProperty("effectScale");
        scaleProperty.floatValue = EditorGUILayout.Slider(
            new GUIContent(
                "Whole-effect Scale",
                "Scales every nested Particle System through hierarchy scaling."),
            scaleProperty.floatValue,
            0.01f,
            2f);
        SerializedProperty minimumSizeProperty =
            serializedEffects.FindProperty("minimumConfettiScreenSize");
        minimumSizeProperty.floatValue = EditorGUILayout.Slider(
            new GUIContent(
                "Confetti Minimum Screen Size",
                "Prevents the especially thin confetti child particles "
                + "from collapsing below a visible pixel size at low scales."),
            minimumSizeProperty.floatValue,
            0f,
            0.02f);
        SerializedProperty cleanupProperty =
            serializedEffects.FindProperty("cleanupDelay");
        cleanupProperty.floatValue = EditorGUILayout.Slider(
            "Cleanup Delay",
            cleanupProperty.floatValue,
            1f,
            30f);
        bool valuesChanged = EditorGUI.EndChangeCheck();
        serializedEffects.ApplyModifiedProperties();

        if (valuesChanged)
        {
            EditorUtility.SetDirty(effects);
            PrefabUtility.SavePrefabAsset(popupPrefab);
            AssetDatabase.SaveAssetIfDirty(popupPrefab);
        }

        EditorGUILayout.Space(10f);
        bool requestedLiveReplay = EditorGUILayout.ToggleLeft(
            "Automatically replay after every value change",
            liveReplay);
        if (requestedLiveReplay != liveReplay)
        {
            liveReplay = requestedLiveReplay;
            EditorPrefs.SetBool(LiveReplayPreference, liveReplay);
        }

        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Show Popup + Replay Confetti", GUILayout.Height(34f)))
            {
                PreviewPopup();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Replay Confetti", GUILayout.Height(28f)))
            {
                ReplayConfetti();
            }

            if (GUILayout.Button("Stop Confetti", GUILayout.Height(28f)))
            {
                GameMenuController.Instance?.DebugStopRetirementConfetti();
            }
            EditorGUILayout.EndHorizontal();
        }

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode to enable the preview controls. The tuner "
                + "can remain open while the game runs.",
                MessageType.Warning);
        }

        if (valuesChanged && liveReplay && EditorApplication.isPlaying)
        {
            ReplayConfetti();
        }
    }

    private void PreviewPopup()
    {
        if (GameMenuController.Instance == null)
        {
            Debug.LogWarning(
                "The unlock VFX preview needs an active GameMenuController.");
            return;
        }

        GameMenuController.Instance.DebugPreviewRetirementPopup(effects);
    }

    private void ReplayConfetti()
    {
        if (GameMenuController.Instance == null)
        {
            return;
        }

        GameMenuController.Instance.DebugReplayRetirementConfetti(effects);
    }
}
