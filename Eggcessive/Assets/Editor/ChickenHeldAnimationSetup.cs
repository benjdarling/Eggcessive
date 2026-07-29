using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class ChickenHeldAnimationSetup
{
    internal const string ModelPath =
        "Assets/Chicken/meshes/chicken.fbx";
    private const string ControllerPath =
        "Assets/Chicken/Animations/chicken.controller";
    private const string HeldClipName = "held";
    private const string HeldStateName = "Held";
    private static bool setupQueued;
    private static bool rebuildQueuedAfterPlayMode;

    [InitializeOnLoadMethod]
    private static void QueueInitialSetup()
    {
        QueueSetup();
    }

    [MenuItem("Tools/Eggcessive/Refresh Chicken Held Animation")]
    public static void Configure()
    {
        setupQueued = false;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            QueueAfterPlayMode();
            return;
        }

        if (EnsureHeldClipImported())
        {
            QueueSetup();
            return;
        }

        AnimationClip heldClip = AssetDatabase
            .LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(clip => ClipNameMatches(
                clip.name,
                HeldClipName));
        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(
                ControllerPath);

        if (heldClip == null || controller == null)
        {
            return;
        }

        AnimatorStateMachine stateMachine =
            controller.layers[0].stateMachine;
        AnimatorState heldState = stateMachine.states
            .Select(child => child.state)
            .FirstOrDefault(state =>
                state != null && state.name == HeldStateName);

        if (heldState == null)
        {
            heldState = stateMachine.AddState(
                HeldStateName,
                new Vector3(460f, 20f, 0f));
        }

        if (heldState.motion == heldClip)
        {
            return;
        }

        heldState.motion = heldClip;
        heldState.writeDefaultValues = true;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("Chicken held animation state configured.");
    }

    internal static void QueueSetup()
    {
        if (setupQueued)
        {
            return;
        }

        setupQueued = true;
        EditorApplication.delayCall += Configure;
    }

    private static bool EnsureHeldClipImported()
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
        int heldIndex = clips.FindIndex(clip =>
            ClipNameMatches(clip.name, HeldClipName)
            || ClipNameMatches(clip.takeName, HeldClipName));

        if (heldIndex >= 0)
        {
            ModelImporterClipAnimation heldClip = clips[heldIndex];

            if (heldClip.lastFrame > heldClip.firstFrame
                && !usingDefaultClips)
            {
                return false;
            }

            if (heldClip.lastFrame <= heldClip.firstFrame)
            {
                heldClip.lastFrame = heldClip.firstFrame + 1f;
                heldClip.wrapMode = WrapMode.ClampForever;
                clips[heldIndex] = heldClip;
            }
        }
        else
        {
            ModelImporterClipAnimation heldClip =
                defaultClips?.FirstOrDefault(clip =>
                    ClipNameMatches(clip.name, HeldClipName)
                    || ClipNameMatches(clip.takeName, HeldClipName));

            if (heldClip == null)
            {
                return false;
            }

            if (heldClip.lastFrame <= heldClip.firstFrame)
            {
                heldClip.lastFrame = heldClip.firstFrame + 1f;
                heldClip.wrapMode = WrapMode.ClampForever;
            }

            clips.Add(heldClip);
        }

        importer.clipAnimations = clips.ToArray();
        importer.SaveAndReimport();
        return true;
    }

    private static bool ClipNameMatches(
        string importedName,
        string requestedName)
    {
        return !string.IsNullOrEmpty(importedName)
            && (importedName.Equals(
                    requestedName,
                    StringComparison.OrdinalIgnoreCase)
                || importedName.EndsWith(
                    $"|{requestedName}",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static void QueueAfterPlayMode()
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
        QueueSetup();
    }
}

public sealed class ChickenHeldAnimationPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (Array.IndexOf(
                importedAssets,
                ChickenHeldAnimationSetup.ModelPath) >= 0)
        {
            ChickenHeldAnimationSetup.QueueSetup();
        }
    }
}
