using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class WorldHandCursorSetup
{
    private const string ModelPath = "Assets/UI/meshes/ui_hand.fbx";
    private const string AnimationFolder = "Assets/UI/animations";
    private const string AvatarPath =
        AnimationFolder + "/ui_hand_avatar.asset";
    private const string ControllerPath =
        AnimationFolder + "/ui_hand_cursor.controller";
    private const string ResourcesUiFolder = "Assets/Resources/UI";
    private const string PrefabPath =
        ResourcesUiFolder + "/prefab_world_hand_cursor.prefab";
    private const float AuthoredVisualScale = 0.51213443f;
    private const string FingertipBoneName = "DEF-f_index.03.R";
    private const string PointStateName = "Point";
    private const string EggHoldStateName = "Egg Hold";
    private const string EggReadyStateName = "Egg Ready To Grab";
    private const string ChickenHoldStateName = "Chicken Hold";
    private const string ChickenReadyStateName =
        "Chicken Ready To Grab";
    private const string PointClipName = "point";
    private const string EggHoldClipName = "eggHold";
    private const string EggReadyClipName = "eggReadyToGrab";
    private const string ChickenHoldClipName = "chickenHold";
    private const string ChickenReadyClipName =
        "chickenReadyToGrab";
    private static bool rebuildQueuedAfterPlayMode;

    [InitializeOnLoadMethod]
    private static void QueueInitialGeneration()
    {
        QueueInitialGenerationCheck();
    }

    private static void QueueInitialGenerationCheck()
    {
        EditorApplication.delayCall -= GenerateIfNeeded;
        EditorApplication.delayCall += GenerateIfNeeded;
    }

    private static void GenerateIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isCompiling)
        {
            return;
        }

        if (EditorApplication.isUpdating)
        {
            QueueInitialGenerationCheck();
            return;
        }

        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (!IsAuthoredPrefabComplete(prefab))
        {
            Generate();
        }
    }

    [MenuItem("Build/Rebuild World Hand Cursor")]
    public static void Generate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            QueueRebuildAfterPlayMode();
            return;
        }

        EnsureFolder("Assets/UI", "animations");
        EnsureFolder("Assets/Resources", "UI");

        if (EnsurePoseClipDurations())
        {
            EditorApplication.delayCall += Generate;
            return;
        }

        GameObject model =
            AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        AnimatorController animatorController =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimationClip pointClip = FindAnimationClip(PointClipName)
            ?? FindStateClip(animatorController, PointStateName);
        AnimationClip eggHoldClip = FindAnimationClip(EggHoldClipName)
            ?? FindStateClip(animatorController, EggHoldStateName);
        AnimationClip eggReadyClip =
            FindAnimationClip(EggReadyClipName)
            ?? FindStateClip(animatorController, EggReadyStateName);
        AnimationClip chickenHoldClip =
            FindAnimationClip(ChickenHoldClipName)
            ?? FindStateClip(animatorController, ChickenHoldStateName);
        AnimationClip chickenReadyClip =
            FindAnimationClip(ChickenReadyClipName)
            ?? FindStateClip(animatorController, ChickenReadyStateName);
        Avatar avatar = LoadHandAvatar();

        if (avatar == null && model != null)
        {
            avatar = CreateHandAvatar(model);
        }

        if (model == null
            || pointClip == null
            || eggHoldClip == null
            || eggReadyClip == null
            || chickenHoldClip == null
            || chickenReadyClip == null
            || avatar == null)
        {
            Debug.LogError(
                "World hand cursor could not be rebuilt. Missing: "
                + string.Join(
                    ", ",
                    GetMissingDependencyNames(
                        model,
                        avatar,
                        pointClip,
                        eggHoldClip,
                        eggReadyClip,
                        chickenHoldClip,
                        chickenReadyClip))
                + ".");
            return;
        }

        if (animatorController == null)
        {
            animatorController =
                AnimatorController.CreateAnimatorControllerAtPath(
                    ControllerPath);
        }

        AnimatorStateMachine stateMachine =
            animatorController.layers[0].stateMachine;
        AnimatorState pointState = ConfigureAnimationState(
            stateMachine,
            PointStateName,
            pointClip);
        ConfigureAnimationState(
            stateMachine,
            EggHoldStateName,
            eggHoldClip);
        ConfigureAnimationState(
            stateMachine,
            EggReadyStateName,
            eggReadyClip);
        ConfigureAnimationState(
            stateMachine,
            ChickenHoldStateName,
            chickenHoldClip);
        ConfigureAnimationState(
            stateMachine,
            ChickenReadyStateName,
            chickenReadyClip);
        stateMachine.defaultState = pointState;
        EditorUtility.SetDirty(animatorController);

        GameObject existingPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        WorldHandCursorController existingController =
            existingPrefab != null
                ? existingPrefab.GetComponent<WorldHandCursorController>()
                : null;
        GameObject cursorRoot = new GameObject("World Hand Cursor");
        WorldHandCursorController cursorController =
            cursorRoot.AddComponent<WorldHandCursorController>();

        if (existingController != null)
        {
            EditorUtility.CopySerialized(
                existingController,
                cursorController);
        }

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "Visual";
        visual.transform.SetParent(cursorRoot.transform, false);

        Animator animator = visual.GetComponent<Animator>();

        if (animator == null)
        {
            animator = visual.AddComponent<Animator>();
        }

        animator.runtimeAnimatorController = animatorController;
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        visual.transform.localScale =
            Vector3.one * AuthoredVisualScale;
        visual.transform.localRotation = Quaternion.Euler(0f, -30f, 0f);

        SerializedObject serializedController =
            new SerializedObject(cursorController);
        serializedController.FindProperty("visualRoot").objectReferenceValue =
            visual.transform;
        serializedController.FindProperty("handAnimator").objectReferenceValue =
            animator;
        serializedController.FindProperty("heldItemAttachPoint")
            .objectReferenceValue = visual
            .GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(child => child.name == "Bone_Attach");
        serializedController.FindProperty("cursorHotspotBone")
            .objectReferenceValue = visual
            .GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(child => child.name == FingertipBoneName);
        serializedController.FindProperty("showCursorDebugMarker").boolValue =
            false;
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(cursorRoot, PrefabPath);
        UnityEngine.Object.DestroyImmediate(cursorRoot);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Validate();
        Debug.Log("World hand cursor rebuilt.");
    }

    private static void QueueRebuildAfterPlayMode()
    {
        if (rebuildQueuedAfterPlayMode)
        {
            return;
        }

        rebuildQueuedAfterPlayMode = true;
        EditorApplication.playModeStateChanged +=
            HandlePlayModeStateChanged;
    }

    private static void HandlePlayModeStateChanged(
        PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }

        EditorApplication.playModeStateChanged -=
            HandlePlayModeStateChanged;
        rebuildQueuedAfterPlayMode = false;
        EditorApplication.delayCall += Generate;
    }

    [MenuItem("Build/Validate World Hand Cursor")]
    public static void Validate()
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            throw new InvalidOperationException(
                "World hand cursor prefab is missing.");
        }

        WorldHandCursorController controller =
            prefab.GetComponent<WorldHandCursorController>();
        Animator animator = prefab.GetComponentInChildren<Animator>(true);

        if (controller == null
            || animator == null
            || animator.avatar == null
            || !HasRequiredAnimationStates(
                animator.runtimeAnimatorController))
        {
            throw new InvalidOperationException(
                "World hand cursor authoring is incomplete.");
        }

        Debug.Log(
            "World hand cursor validation passed: point, egg, and chicken interaction poses are authored.");
    }

    private static AnimationClip FindAnimationClip(string clipName)
    {
        return AssetDatabase
            .LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(clip =>
                clip.name.Equals(
                    clipName,
                    StringComparison.OrdinalIgnoreCase)
                || clip.name.EndsWith(
                    $"|{clipName}",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static Avatar LoadHandAvatar()
    {
        return AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath)
            ?? AssetDatabase
                .LoadAllAssetsAtPath(ModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
    }

    private static Avatar CreateHandAvatar(GameObject model)
    {
        GameObject instance =
            (GameObject)PrefabUtility.InstantiatePrefab(model);

        if (instance == null)
        {
            instance = UnityEngine.Object.Instantiate(model);
        }

        try
        {
            Transform skeletonRoot = instance
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child =>
                    child.name.Equals(
                        "root",
                        StringComparison.OrdinalIgnoreCase));

            if (skeletonRoot == null)
            {
                Debug.LogError(
                    "ui_hand.fbx does not contain its expected root transform.");
                return null;
            }

            Avatar avatar = AvatarBuilder.BuildGenericAvatar(
                instance,
                skeletonRoot.name);

            if (avatar == null || !avatar.isValid)
            {
                if (avatar != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatar);
                }

                Debug.LogError(
                    "Unity could not build a valid Generic Avatar from ui_hand.fbx.");
                return null;
            }

            avatar.name = "ui_hand_avatar";
            AssetDatabase.CreateAsset(avatar, AvatarPath);
            return avatar;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static IEnumerable<string> GetMissingDependencyNames(
        GameObject model,
        Avatar avatar,
        AnimationClip pointClip,
        AnimationClip eggHoldClip,
        AnimationClip eggReadyClip,
        AnimationClip chickenHoldClip,
        AnimationClip chickenReadyClip)
    {
        if (model == null)
        {
            yield return ModelPath;
        }

        if (avatar == null)
        {
            yield return "Avatar";
        }

        if (pointClip == null)
        {
            yield return PointClipName;
        }

        if (eggHoldClip == null)
        {
            yield return EggHoldClipName;
        }

        if (eggReadyClip == null)
        {
            yield return EggReadyClipName;
        }

        if (chickenHoldClip == null)
        {
            yield return ChickenHoldClipName;
        }

        if (chickenReadyClip == null)
        {
            yield return ChickenReadyClipName;
        }
    }

    private static AnimationClip FindStateClip(
        AnimatorController controller,
        string stateName)
    {
        if (controller == null || controller.layers.Length == 0)
        {
            return null;
        }

        return controller.layers[0].stateMachine.states
            .Select(child => child.state)
            .FirstOrDefault(state => state != null && state.name == stateName)
            ?.motion as AnimationClip;
    }

    private static bool EnsurePoseClipDurations()
    {
        ModelImporter importer =
            AssetImporter.GetAtPath(ModelPath) as ModelImporter;

        if (importer == null)
        {
            return false;
        }

        ModelImporterClipAnimation[] explicitClips =
            importer.clipAnimations;
        ModelImporterClipAnimation[] defaultClips =
            importer.defaultClipAnimations;
        bool usingDefaultClips =
            explicitClips == null || explicitClips.Length == 0;
        List<ModelImporterClipAnimation> clips =
            (usingDefaultClips ? defaultClips : explicitClips)
            ?.ToList()
            ?? new List<ModelImporterClipAnimation>();
        bool changed = usingDefaultClips && clips.Count > 0;
        string[] requiredClipNames =
        {
            PointClipName,
            EggHoldClipName,
            EggReadyClipName,
            ChickenHoldClipName,
            ChickenReadyClipName
        };

        foreach (string requiredClipName in requiredClipNames)
        {
            int clipIndex = clips.FindIndex(clip =>
                ClipNameMatches(clip.name, requiredClipName)
                || ClipNameMatches(clip.takeName, requiredClipName));
            ModelImporterClipAnimation clip;

            if (clipIndex < 0)
            {
                string takeName = $"ui_hand|{requiredClipName}";
                clip = new ModelImporterClipAnimation
                {
                    name = takeName,
                    takeName = takeName,
                    firstFrame = 0f,
                    lastFrame = 1f,
                    loopTime = false,
                    wrapMode = WrapMode.ClampForever
                };
                clips.Add(clip);
                changed = true;
                continue;
            }

            clip = clips[clipIndex];
            float requiredLastFrame = clip.firstFrame + 1f;

            if (clip.lastFrame < requiredLastFrame
                || clip.loopTime
                || clip.wrapMode != WrapMode.ClampForever)
            {
                clip.lastFrame = requiredLastFrame;
                clip.loopTime = false;
                clip.wrapMode = WrapMode.ClampForever;
                clips[clipIndex] = clip;
                changed = true;
            }
        }

        if (!changed)
        {
            return false;
        }

        importer.clipAnimations = clips.ToArray();
        importer.SaveAndReimport();
        return true;
    }

    private static bool ClipNameMatches(
        string importedName,
        string requestedName)
    {
        return importedName.Equals(
                requestedName,
                StringComparison.OrdinalIgnoreCase)
            || importedName.EndsWith(
                $"|{requestedName}",
                StringComparison.OrdinalIgnoreCase);
    }

    private static AnimatorState ConfigureAnimationState(
        AnimatorStateMachine stateMachine,
        string stateName,
        AnimationClip clip)
    {
        AnimatorState state = stateMachine.states
            .Select(child => child.state)
            .FirstOrDefault(candidate =>
                candidate.name == stateName);

        if (state == null)
        {
            state = stateMachine.AddState(stateName);
        }

        state.motion = clip;
        state.writeDefaultValues = true;
        return state;
    }

    private static bool HasRequiredAnimationStates(
        RuntimeAnimatorController runtimeController)
    {
        AnimatorController controller =
            runtimeController as AnimatorController;

        if (controller == null || controller.layers.Length == 0)
        {
            return false;
        }

        AnimatorState[] states = controller.layers[0]
            .stateMachine.states
            .Select(child => child.state)
            .ToArray();
        return HasStateMotion(states, PointStateName)
            && HasStateMotion(states, EggHoldStateName)
            && HasStateMotion(states, EggReadyStateName)
            && HasStateMotion(states, ChickenHoldStateName)
            && HasStateMotion(states, ChickenReadyStateName);
    }

    private static bool IsAuthoredPrefabComplete(GameObject prefab)
    {
        Animator animator = prefab != null
            ? prefab.GetComponentInChildren<Animator>(true)
            : null;
        return animator != null
            && animator.avatar != null
            && HasRequiredAnimationStates(
                animator.runtimeAnimatorController);
    }

    private static bool HasStateMotion(
        AnimatorState[] states,
        string stateName)
    {
        return states.Any(state =>
            state != null
            && state.name == stateName
            && state.motion != null);
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
