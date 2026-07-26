using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class UiFontAudit
{
    private const string FontPath = "Assets/Fonts/Cat Song SDF.asset";
    private const string TmpSettingsPath =
        "Assets/TextMesh Pro/Resources/TMP Settings.asset";
    private static readonly string[] GameAssetRoots =
    {
        "Assets/Chicken",
        "Assets/Collection",
        "Assets/Containers",
        "Assets/Eggs",
        "Assets/Env",
        "Assets/Food",
        "Assets/Incubator",
        "Assets/Resources",
        "Assets/Scenes",
        "Assets/Testing",
        "Assets/UI",
        "Assets/VFX"
    };

    [MenuItem("Tools/Eggcessive/Apply Cat Song UI Font")]
    public static void Apply()
    {
        TMP_FontAsset font =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            throw new MissingReferenceException(
                $"Missing required UI font at {FontPath}.");
        }

        int changedComponents = 0;
        ApplyTmpDefault(font);

        string[] prefabGuids = AssetDatabase.FindAssets(
            "t:Prefab",
            GameAssetRoots);
        for (int index = 0; index < prefabGuids.Length; index++)
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGuids[index]);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool changed = ApplyToHierarchy(root, font, ref changedComponents);
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }

            PrefabUtility.UnloadPrefabContents(root);
        }

        string[] sceneGuids = AssetDatabase.FindAssets(
            "t:Scene",
            new[] { "Assets/Scenes" });
        for (int index = 0; index < sceneGuids.Length; index++)
        {
            string path = AssetDatabase.GUIDToAssetPath(sceneGuids[index]);
            Scene existing = SceneManager.GetSceneByPath(path);
            bool alreadyLoaded = existing.IsValid() && existing.isLoaded;
            Scene scene = alreadyLoaded
                ? existing
                : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            bool changed = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                changed |= ApplyToHierarchy(root, font, ref changedComponents);
            }

            if (changed)
            {
                EditorSceneManager.SaveScene(scene);
            }

            if (!alreadyLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"Applied Cat Song to {changedComponents} authored TMP text components.");
        Validate();
    }

    [MenuItem("Tools/Eggcessive/Validate UI Fonts")]
    public static void Validate()
    {
        TMP_FontAsset expectedFont =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (expectedFont == null)
        {
            throw new MissingReferenceException(
                $"Missing required UI font at {FontPath}.");
        }

        var failures = new List<string>();
        int textCount = 0;
        ValidateTmpDefault(expectedFont, failures);

        string[] prefabGuids = AssetDatabase.FindAssets(
            "t:Prefab",
            GameAssetRoots);
        for (int index = 0; index < prefabGuids.Length; index++)
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabGuids[index]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            ValidateHierarchy(prefab, path, expectedFont, failures, ref textCount);
        }

        string[] sceneGuids = AssetDatabase.FindAssets(
            "t:Scene",
            new[] { "Assets/Scenes" });
        for (int index = 0; index < sceneGuids.Length; index++)
        {
            string path = AssetDatabase.GUIDToAssetPath(sceneGuids[index]);
            Scene existing = SceneManager.GetSceneByPath(path);
            bool alreadyLoaded = existing.IsValid() && existing.isLoaded;
            Scene scene = alreadyLoaded
                ? existing
                : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                ValidateHierarchy(
                    root,
                    path,
                    expectedFont,
                    failures,
                    ref textCount);
            }

            if (!alreadyLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "UI font audit failed:\n" + string.Join("\n", failures));
        }

        Debug.Log(
            $"UI font audit passed: {textCount} authored TextMeshPro " +
            "components and the TMP project default use Cat Song.");
    }

    private static void ValidateTmpDefault(
        TMP_FontAsset expectedFont,
        List<string> failures)
    {
        TMP_Settings settings =
            AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
        if (settings == null)
        {
            failures.Add($"Missing TMP settings at {TmpSettingsPath}");
            return;
        }

        SerializedProperty defaultFont =
            new SerializedObject(settings).FindProperty("m_defaultFontAsset");
        if (defaultFont == null
            || defaultFont.objectReferenceValue != expectedFont)
        {
            failures.Add("TextMesh Pro project default is not Cat Song");
        }
    }

    private static void ApplyTmpDefault(TMP_FontAsset font)
    {
        TMP_Settings settings =
            AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
        if (settings == null)
        {
            throw new MissingReferenceException(
                $"Missing TMP settings at {TmpSettingsPath}.");
        }

        SerializedObject serializedSettings = new SerializedObject(settings);
        SerializedProperty defaultFont =
            serializedSettings.FindProperty("m_defaultFontAsset");
        defaultFont.objectReferenceValue = font;
        serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
    }

    private static bool ApplyToHierarchy(
        GameObject root,
        TMP_FontAsset font,
        ref int changedComponents)
    {
        bool changed = false;
        foreach (TMP_Text text in
                 root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font == font)
            {
                continue;
            }

            text.font = font;
            EditorUtility.SetDirty(text);
            changedComponents++;
            changed = true;
        }

        return changed;
    }

    private static void ValidateHierarchy(
        GameObject root,
        string assetPath,
        TMP_FontAsset expectedFont,
        List<string> failures,
        ref int textCount)
    {
        if (root == null)
        {
            return;
        }

        foreach (TMP_Text text in
                 root.GetComponentsInChildren<TMP_Text>(true))
        {
            textCount++;
            if (text.font != expectedFont)
            {
                failures.Add(
                    $"{assetPath}: {GetHierarchyPath(text.transform)} " +
                    $"uses {(text.font != null ? text.font.name : "no font")}");
            }
        }

        foreach (UnityEngine.UI.Text legacyText in
                 root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            failures.Add(
                $"{assetPath}: {GetHierarchyPath(legacyText.transform)} " +
                "still uses legacy UI.Text");
        }
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = $"{transform.name}/{path}";
        }

        return path;
    }
}
