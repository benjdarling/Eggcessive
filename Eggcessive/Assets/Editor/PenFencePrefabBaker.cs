using System;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

internal static class PenFencePrefabBaker
{
    private const string MenuPath =
        "Tools/Eggcessive/Fence/Bake Pen Fence Prefab";
    private const string FencePrefabPath =
        "Assets/Env/prefab_PenFence.prefab";
    private const string PostModelPath =
        "Assets/Env/meshes/fence_post.fbx";
    private const string BeamModelPath =
        "Assets/Env/meshes/fence_beams.fbx";
    private const string PostCollisionModelPath =
        "Assets/Env/meshes/fence_post_COL.fbx";
    private const string BeamCollisionModelPath =
        "Assets/Env/meshes/fence_beams_COL.fbx";
    private const string SourceName = "Spline_Fence";
    private const string PenTemplateName = "Terrain_Pens";
    private const string GeneratedRootName = "Generated Fence";
    private const float PreferredPostSpacing = 1f;
    private const float MinimumBeamLength = 0.001f;
    private const double RebuildDelaySeconds = 0.35d;

    private static SplineContainer pendingRebuild;
    private static double rebuildAt;
    private static bool rebuildQueued;
    private static bool isBaking;

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        Spline.Changed -= HandleSplineChanged;
        Spline.Changed += HandleSplineChanged;

        GameObject fencePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            FencePrefabPath);
        if (FenceCollisionNeedsBake(fencePrefab))
        {
            EditorApplication.delayCall += TryInitialBake;
        }
    }

    private static void HandleSplineChanged(
        Spline changedSpline,
        int knotIndex,
        SplineModification modification)
    {
        if (isBaking || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        SplineContainer source = FindContainerForSpline(changedSpline);
        if (source == null || !IsFenceSource(source))
        {
            return;
        }

        pendingRebuild = source;
        rebuildAt = EditorApplication.timeSinceStartup + RebuildDelaySeconds;
        if (!rebuildQueued)
        {
            rebuildQueued = true;
            EditorApplication.update += ProcessPendingRebuild;
        }
    }

    private static void ProcessPendingRebuild()
    {
        if (!rebuildQueued
            || EditorApplication.timeSinceStartup < rebuildAt)
        {
            return;
        }

        EditorApplication.update -= ProcessPendingRebuild;
        rebuildQueued = false;
        SplineContainer source = pendingRebuild;
        pendingRebuild = null;
        if (source != null
            && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Bake(source, false);
        }
    }

    [MenuItem(MenuPath)]
    private static void BakeFromMenu()
    {
        SplineContainer source = FindSourceSpline();
        if (source == null)
        {
            EditorUtility.DisplayDialog(
                "Pen Fence",
                "Select a SplineContainer, or open the scene containing " +
                $"'{SourceName}', then try again.",
                "OK");
            return;
        }

        Bake(source, true);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateBakeFromMenu()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static void TryInitialBake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        GameObject fencePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            FencePrefabPath);
        if (!FenceCollisionNeedsBake(fencePrefab))
        {
            return;
        }

        SplineContainer source = FindSourceSpline();
        if (source != null)
        {
            Bake(source, false);
        }
    }

    private static bool FenceCollisionNeedsBake(GameObject fencePrefab)
    {
        if (fencePrefab == null)
        {
            return true;
        }

        bool foundPostCollider = false;
        bool foundBeamCollider = false;
        MeshCollider[] colliders = fencePrefab.GetComponentsInChildren<
            MeshCollider>(true);
        for (int index = 0; index < colliders.Length; index++)
        {
            MeshCollider collider = colliders[index];
            Transform current = collider.transform;
            while (current != null && current != fencePrefab.transform)
            {
                if (current.name.Equals(
                        "fence_post_COL",
                        StringComparison.OrdinalIgnoreCase))
                {
                    foundPostCollider = true;
                    if (!collider.convex)
                    {
                        return true;
                    }

                    break;
                }

                if (current.name.Equals(
                        "fence_beams_COL",
                        StringComparison.OrdinalIgnoreCase))
                {
                    foundBeamCollider = true;
                    if (collider.convex)
                    {
                        return true;
                    }

                    break;
                }

                current = current.parent;
            }
        }

        return !foundPostCollider || !foundBeamCollider;
    }

    private static SplineContainer FindSourceSpline()
    {
        GameObject selected = Selection.activeGameObject;
        SplineContainer selectedSpline = selected != null
            ? selected.GetComponent<SplineContainer>()
            : null;
        if (selectedSpline != null && selected.scene.IsValid())
        {
            return selectedSpline;
        }

        SplineContainer[] splines = UnityEngine.Object.FindObjectsByType<
            SplineContainer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < splines.Length; index++)
        {
            if (splines[index].name.Equals(
                    SourceName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return splines[index];
            }
        }

        return null;
    }

    private static SplineContainer FindContainerForSpline(Spline spline)
    {
        SplineContainer[] containers = UnityEngine.Object.FindObjectsByType<
            SplineContainer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int containerIndex = 0;
             containerIndex < containers.Length;
             containerIndex++)
        {
            SplineContainer container = containers[containerIndex];
            for (int splineIndex = 0;
                 splineIndex < container.Splines.Count;
                 splineIndex++)
            {
                if (ReferenceEquals(container.Splines[splineIndex], spline))
                {
                    return container;
                }
            }
        }

        return null;
    }

    private static bool IsFenceSource(SplineContainer source)
    {
        if (source.name.Equals(
                SourceName,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
            source.gameObject);
        if (assetPath.Equals(
                FencePrefabPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        return stage != null
            && stage.prefabContentsRoot == source.gameObject
            && stage.assetPath.Equals(
                FencePrefabPath,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void Bake(SplineContainer source, bool selectResult)
    {
        if (isBaking)
        {
            return;
        }

        isBaking = true;
        try
        {
            BakeInternal(source, selectResult);
        }
        finally
        {
            isBaking = false;
        }
    }

    private static void BakeInternal(
        SplineContainer source,
        bool selectResult)
    {
        GameObject postModel = AssetDatabase.LoadAssetAtPath<GameObject>(
            PostModelPath);
        GameObject beamModel = AssetDatabase.LoadAssetAtPath<GameObject>(
            BeamModelPath);
        GameObject postCollisionModel =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                PostCollisionModelPath);
        GameObject beamCollisionModel =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                BeamCollisionModelPath);
        if (postModel == null
            || beamModel == null
            || postCollisionModel == null
            || beamCollisionModel == null)
        {
            Debug.LogError(
                "Pen fence bake needs all four visual/collision models: " +
                $"'{PostModelPath}', '{BeamModelPath}', " +
                $"'{PostCollisionModelPath}', and " +
                $"'{BeamCollisionModelPath}'.",
                source);
            return;
        }

        Spline spline = source.Spline;
        if (spline == null || spline.Count < 2)
        {
            Debug.LogError("Pen fence spline needs at least two knots.", source);
            return;
        }

        GameObject root = source.gameObject;
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        bool editingFencePrefab = prefabStage != null
            && prefabStage.prefabContentsRoot == root
            && prefabStage.assetPath.Equals(
                FencePrefabPath,
                StringComparison.OrdinalIgnoreCase);
        if (!editingFencePrefab && PrefabUtility.IsPartOfPrefabInstance(root))
        {
            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(
                root);
            if (instanceRoot != root)
            {
                Debug.LogError(
                    "The fence spline must be the root of its prefab instance.",
                    root);
                return;
            }

            PrefabUtility.UnpackPrefabInstance(
                root,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
        }

        Transform existingGenerated = root.transform.Find(GeneratedRootName);
        if (existingGenerated != null)
        {
            UnityEngine.Object.DestroyImmediate(existingGenerated.gameObject);
        }

        Transform penTemplate = editingFencePrefab
            ? null
            : FindSceneObject(PenTemplateName)?.transform;
        if (!editingFencePrefab && penTemplate == null)
        {
            Debug.LogError(
                $"Pen fence bake could not find '{PenTemplateName}'.",
                root);
            return;
        }

        root.name = SourceName;
        if (!editingFencePrefab)
        {
            root.transform.SetParent(penTemplate, true);
        }

        GameObject generatedRoot = new GameObject(GeneratedRootName);
        generatedRoot.transform.SetParent(root.transform, false);
        Transform postsRoot = CreateGroup("Posts", generatedRoot.transform);
        Transform beamsRoot = CreateGroup("Beams", generatedRoot.transform);

        int postCount = 0;
        int beamCount = 0;
        int curveCount = spline.GetCurveCount();
        Vector3 previousPosition = Vector3.zero;
        for (int curveIndex = 0; curveIndex < curveCount; curveIndex++)
        {
            float curveLength = spline.GetCurveLength(curveIndex);
            int intervalCount = Mathf.Max(
                1,
                Mathf.RoundToInt(curveLength / PreferredPostSpacing));

            if (curveIndex == 0)
            {
                previousPosition = EvaluateCurvePosition(
                    spline,
                    curveIndex,
                    0f);
                CreatePost(
                    postModel,
                    postCollisionModel,
                    postsRoot,
                    previousPosition,
                    EvaluateCurveTangent(spline, curveIndex, 0f),
                    postCount++);
            }

            for (int intervalIndex = 1;
                 intervalIndex <= intervalCount;
                 intervalIndex++)
            {
                float distance = curveLength
                    * intervalIndex
                    / intervalCount;
                float curveT = spline.GetCurveInterpolation(
                    curveIndex,
                    distance);
                Vector3 position = EvaluateCurvePosition(
                    spline,
                    curveIndex,
                    curveT);

                CreateBeam(
                    beamModel,
                    beamCollisionModel,
                    beamsRoot,
                    previousPosition,
                    position,
                    beamCount++);

                bool closesSpline = spline.Closed
                    && curveIndex == curveCount - 1
                    && intervalIndex == intervalCount;
                if (!closesSpline)
                {
                    CreatePost(
                        postModel,
                        postCollisionModel,
                        postsRoot,
                        position,
                        EvaluateCurveTangent(spline, curveIndex, curveT),
                        postCount++);
                }

                previousPosition = position;
            }
        }

        GameObject savedPrefab = editingFencePrefab
            ? PrefabUtility.SaveAsPrefabAsset(root, FencePrefabPath)
            : PrefabUtility.SaveAsPrefabAssetAndConnect(
                root,
                FencePrefabPath,
                InteractionMode.AutomatedAction);
        if (savedPrefab == null)
        {
            Debug.LogError("Failed to save the pen fence prefab.", root);
            return;
        }

        Scene scene = root.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        if (!editingFencePrefab)
        {
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();

        if (selectResult)
        {
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(savedPrefab);
        }

        Debug.Log(
            $"Baked {postCount} fence posts and {beamCount} beam spans to " +
            $"'{FencePrefabPath}'. Posts are approximately " +
            $"{PreferredPostSpacing:0.##}m apart and every spline knot has " +
            "a post.",
            root);
    }

    private static Transform CreateGroup(string name, Transform parent)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group.transform;
    }

    private static void CreatePost(
        GameObject postModel,
        GameObject postCollisionModel,
        Transform parent,
        Vector3 position,
        Vector3 tangent,
        int index)
    {
        GameObject postRoot = new GameObject($"Fence Post {index + 1:00}");
        postRoot.transform.SetParent(parent, false);
        postRoot.transform.localPosition = position;
        postRoot.transform.localRotation = HorizontalRotation(tangent);

        GameObject post = (GameObject)PrefabUtility.InstantiatePrefab(postModel);
        post.name = "fence_post";
        post.transform.SetParent(postRoot.transform, false);
        ResetLocalTransform(post.transform);

        GameObject collision = (GameObject)PrefabUtility.InstantiatePrefab(
            postCollisionModel);
        collision.name = "fence_post_COL";
        collision.transform.SetParent(postRoot.transform, false);
        ResetLocalTransform(collision.transform);
        ConfigureCollisionModel(collision, true);
    }

    private static void CreateBeam(
        GameObject beamModel,
        GameObject beamCollisionModel,
        Transform parent,
        Vector3 start,
        Vector3 end,
        int index)
    {
        Vector3 direction = end - start;
        float length = direction.magnitude;
        if (length <= MinimumBeamLength)
        {
            return;
        }

        GameObject span = new GameObject($"Fence Beam Span {index + 1:00}");
        span.transform.SetParent(parent, false);
        span.transform.localPosition = (start + end) * 0.5f;
        span.transform.localRotation = HorizontalRotation(direction);

        GameObject beam = (GameObject)PrefabUtility.InstantiatePrefab(beamModel);
        beam.name = "fence_beams";
        beam.transform.SetParent(span.transform, false);
        ResetLocalTransform(beam.transform);
        FitModelToSpan(beam, length);

        GameObject collision = (GameObject)PrefabUtility.InstantiatePrefab(
            beamCollisionModel);
        collision.name = "fence_beams_COL";
        collision.transform.SetParent(span.transform, false);
        ResetLocalTransform(collision.transform);
        FitModelToSpan(collision, length);
        ConfigureCollisionModel(collision, false);
    }

    private static void FitModelToSpan(GameObject model, float spanLength)
    {
        Bounds bounds = CalculateBoundsInRootSpace(model);
        bool lengthAlongX = bounds.size.x >= bounds.size.z;
        float modelLength = lengthAlongX ? bounds.size.x : bounds.size.z;
        if (modelLength <= MinimumBeamLength)
        {
            Debug.LogWarning(
                $"'{model.name}' has no usable horizontal length; " +
                "span scaling was skipped.",
                model);
            return;
        }

        Vector3 scale = Vector3.one;
        float lengthScale = spanLength / modelLength;
        if (lengthAlongX)
        {
            scale.x = lengthScale;
            model.transform.localRotation = Quaternion.FromToRotation(
                Vector3.right,
                Vector3.forward);
            model.transform.localPosition = Vector3.back
                * bounds.center.x
                * lengthScale;
        }
        else
        {
            scale.z = lengthScale;
            model.transform.localPosition = Vector3.back
                * bounds.center.z
                * lengthScale;
        }

        model.transform.localScale = scale;
    }

    private static void ConfigureCollisionModel(
        GameObject collisionRoot,
        bool convex)
    {
        MeshFilter[] filters = collisionRoot.GetComponentsInChildren<
            MeshFilter>(true);
        for (int index = 0; index < filters.Length; index++)
        {
            MeshFilter filter = filters[index];
            if (filter.sharedMesh == null)
            {
                continue;
            }

            MeshCollider collider = filter.GetComponent<MeshCollider>();
            if (collider == null)
            {
                collider = filter.gameObject.AddComponent<MeshCollider>();
            }

            collider.sharedMesh = filter.sharedMesh;
            collider.convex = convex;
            collider.isTrigger = false;
        }

        Renderer[] renderers = collisionRoot.GetComponentsInChildren<Renderer>(
            true);
        for (int index = 0; index < renderers.Length; index++)
        {
            renderers[index].enabled = false;
        }
    }

    private static void ResetLocalTransform(Transform target)
    {
        target.localPosition = Vector3.zero;
        target.localRotation = Quaternion.identity;
        target.localScale = Vector3.one;
    }

    private static Bounds CalculateBoundsInRootSpace(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;
        for (int rendererIndex = 0;
             rendererIndex < renderers.Length;
             rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            Bounds rendererBounds = renderer.localBounds;
            Vector3 min = rendererBounds.min;
            Vector3 max = rendererBounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 localCorner = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 point = root.transform.InverseTransformPoint(
                    renderer.transform.TransformPoint(localCorner));
                if (!hasBounds)
                {
                    result = new Bounds(point, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    result.Encapsulate(point);
                }
            }
        }

        return result;
    }

    private static Vector3 EvaluateCurvePosition(
        Spline spline,
        int curveIndex,
        float curveT)
    {
        float3 position = CurveUtility.EvaluatePosition(
            spline.GetCurve(curveIndex),
            curveT);
        return new Vector3(position.x, position.y, position.z);
    }

    private static Vector3 EvaluateCurveTangent(
        Spline spline,
        int curveIndex,
        float curveT)
    {
        float3 tangent = CurveUtility.EvaluateTangent(
            spline.GetCurve(curveIndex),
            curveT);
        return new Vector3(tangent.x, tangent.y, tangent.z);
    }

    private static Quaternion HorizontalRotation(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.000001f
            ? Quaternion.LookRotation(direction.normalized, Vector3.up)
            : Quaternion.identity;
    }

    private static GameObject FindSceneObject(string objectName)
    {
        GameObject[] objects = UnityEngine.Object.FindObjectsByType<GameObject>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < objects.Length; index++)
        {
            GameObject candidate = objects[index];
            if (candidate.scene.IsValid()
                && candidate.name.Equals(
                    objectName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
