using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(ProceduralGrassClumpAuthoring))]
public sealed class ProceduralGrassClumpAuthoringEditor : Editor
{
    private static readonly HashSet<int> QueuedAuthoringIds = new HashSet<int>();
    private static bool previewRefreshQueued;

    [InitializeOnLoadMethod]
    private static void QueueMissingPrefabMeshes()
    {
        ProceduralGrassClumpAuthoring.EditorValidationRequested -= OnAuthoringValidated;
        ProceduralGrassClumpAuthoring.EditorValidationRequested += OnAuthoringValidated;
        EditorApplication.delayCall += GenerateMissingPrefabMeshes;
    }

    private static void OnAuthoringValidated(ProceduralGrassClumpAuthoring authoring)
    {
        if (authoring != null
            && authoring.AutoRegenerate
            && !authoring.GeneratedSettingsAreCurrent)
        {
            QueueGeneration(authoring);
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool changed = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();

        var authoring = (ProceduralGrassClumpAuthoring)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Side Silhouette controls half-width from root (0) to tip (1). "
            + "Bend Profile controls how the geometric bend accumulates over the same height.",
            MessageType.Info);

        if (GUILayout.Button("Generate / Update Grass Mesh", GUILayout.Height(30f)))
        {
            GenerateAndSave(authoring, true);
        }

        using (new EditorGUI.DisabledScope(authoring.GeneratedMeshFilter == null
            || authoring.GeneratedMeshFilter.sharedMesh == null))
        {
            if (GUILayout.Button("Select Generated Mesh Asset"))
            {
                Selection.activeObject = authoring.GeneratedMeshFilter.sharedMesh;
                EditorGUIUtility.PingObject(authoring.GeneratedMeshFilter.sharedMesh);
            }
        }

        if (changed && authoring.AutoRegenerate)
        {
            UpdateLiveMesh(authoring);
            QueueGeneration(authoring);
        }
    }

    private void OnSceneGUI()
    {
        var authoring = (ProceduralGrassClumpAuthoring)target;
        if (authoring == null)
        {
            return;
        }

        Transform grassTransform = authoring.transform;
        Color previousColor = Handles.color;
        Handles.color = new Color(0.35f, 1f, 0.2f, 0.85f);
        Handles.DrawWireDisc(
            grassTransform.position,
            grassTransform.up,
            authoring.SpreadRadius);
        Handles.Label(
            grassTransform.position + grassTransform.right * authoring.SpreadRadius,
            $" Spread {authoring.SpreadRadius:0.###} m");
        Handles.color = previousColor;
    }

    [MenuItem("Eggcessive/Grass/Generate Selected Grass Clump")]
    private static void GenerateSelectedGrassClump()
    {
        GameObject selected = Selection.activeGameObject;
        ProceduralGrassClumpAuthoring authoring = selected != null
            ? selected.GetComponentInParent<ProceduralGrassClumpAuthoring>()
            : null;
        if (authoring == null)
        {
            EditorUtility.DisplayDialog(
                "Procedural Grass",
                "Select a grass clump with ProceduralGrassClumpAuthoring.",
                "OK");
            return;
        }

        GenerateAndSave(authoring, true);
    }

    private static void QueueGeneration(ProceduralGrassClumpAuthoring authoring)
    {
        if (authoring == null || !QueuedAuthoringIds.Add(authoring.GetInstanceID()))
        {
            return;
        }

        int instanceId = authoring.GetInstanceID();
        EditorApplication.delayCall += () =>
        {
            QueuedAuthoringIds.Remove(instanceId);
            if (authoring != null)
            {
                GenerateAndSave(authoring, false);
            }
        };
    }

    private static void UpdateLiveMesh(ProceduralGrassClumpAuthoring authoring)
    {
        if (authoring == null || authoring.GeneratedMeshFilter == null)
        {
            return;
        }

        Mesh savedMesh = authoring.GeneratedMeshFilter.sharedMesh;
        if (savedMesh == null || !AssetDatabase.Contains(savedMesh))
        {
            return;
        }

        Mesh generatedMesh = authoring.BuildMesh();
        generatedMesh.name = savedMesh.name;
        ReplaceMeshData(generatedMesh, savedMesh);
        Object.DestroyImmediate(generatedMesh);
        EditorUtility.SetDirty(savedMesh);
        authoring.GeneratedMeshFilter.gameObject.SetActive(true);
        SceneView.RepaintAll();
    }

    private static void GenerateAndSave(
        ProceduralGrassClumpAuthoring authoring,
        bool recordUndo)
    {
        if (authoring == null || authoring.GeneratedMeshFilter == null)
        {
            Debug.LogWarning("Procedural grass authoring requires a Generated Mesh Filter.", authoring);
            return;
        }

        string assetPath = authoring.MeshAssetPath;
        if (string.IsNullOrWhiteSpace(assetPath)
            || !assetPath.StartsWith("Assets/")
            || !assetPath.EndsWith(".asset"))
        {
            Debug.LogError("Grass mesh asset path must be an .asset path below Assets/.", authoring);
            return;
        }

        EnsureFolder(System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
        Mesh generatedMesh = authoring.BuildMesh();
        generatedMesh.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (savedMesh == null)
        {
            AssetDatabase.CreateAsset(generatedMesh, assetPath);
            savedMesh = generatedMesh;
        }
        else
        {
            ReplaceMeshData(generatedMesh, savedMesh);
            Object.DestroyImmediate(generatedMesh);
            EditorUtility.SetDirty(savedMesh);
        }

        if (recordUndo)
        {
            Undo.RecordObject(authoring.GeneratedMeshFilter, "Generate grass clump mesh");
            Undo.RecordObject(authoring, "Generate grass clump mesh");
        }

        authoring.GeneratedMeshFilter.sharedMesh = savedMesh;
        authoring.MarkSettingsGenerated();
        authoring.GeneratedMeshFilter.gameObject.SetActive(true);
        if (authoring.DisableOtherMeshObjectsOnGenerate)
        {
            MeshFilter[] filters = authoring.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] == authoring.GeneratedMeshFilter)
                {
                    continue;
                }

                if (recordUndo)
                {
                    Undo.RecordObject(filters[i].gameObject, "Disable placeholder grass blade");
                }

                filters[i].gameObject.SetActive(false);
                EditorUtility.SetDirty(filters[i].gameObject);
            }
        }

        EditorUtility.SetDirty(authoring.GeneratedMeshFilter);
        EditorUtility.SetDirty(authoring);
        if (PrefabUtility.IsPartOfPrefabInstance(authoring.GeneratedMeshFilter))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(
                authoring.GeneratedMeshFilter);
        }
        AssetDatabase.SaveAssets();
        QueueGrassPreviewRefresh();
        SceneView.RepaintAll();
    }

    private static void ReplaceMeshData(Mesh source, Mesh destination)
    {
        string destinationName = destination.name;
        destination.Clear();
        destination.indexFormat = source.indexFormat;
        destination.vertices = source.vertices;
        destination.normals = source.normals;
        destination.tangents = source.tangents;
        destination.uv = source.uv;
        destination.uv2 = source.uv2;
        destination.triangles = source.triangles;
        destination.bounds = source.bounds;
        destination.name = destinationName;
        destination.UploadMeshData(false);
    }

    private static void EnsureFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parent = System.IO.Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folderPath));
    }

    private static void GenerateMissingPrefabMeshes()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            ProceduralGrassClumpAuthoring assetAuthoring = prefabAsset != null
                ? prefabAsset.GetComponentInChildren<ProceduralGrassClumpAuthoring>(true)
                : null;
            if (assetAuthoring == null
                || assetAuthoring.GeneratedMeshFilter == null
                || (MeshUsesCurrentFormat(assetAuthoring.GeneratedMeshFilter.sharedMesh)
                    && assetAuthoring.GeneratedSettingsAreCurrent))
            {
                continue;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                ProceduralGrassClumpAuthoring authoring =
                    prefabRoot.GetComponentInChildren<ProceduralGrassClumpAuthoring>(true);
                GenerateAndSave(authoring, false);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }

    private static bool MeshUsesCurrentFormat(Mesh mesh)
    {
        return mesh != null && mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
    }

    private static void QueueGrassPreviewRefresh()
    {
        if (previewRefreshQueued)
        {
            return;
        }

        previewRefreshQueued = true;
        EditorApplication.delayCall += () =>
        {
            previewRefreshQueued = false;
            InteractiveGrassSystem[] systems = Object.FindObjectsByType<InteractiveGrassSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null && systems[i].gameObject.scene.IsValid())
                {
                    systems[i].GenerateGrass();
                }
            }
        };
    }
}
