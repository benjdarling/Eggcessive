#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class AutoFeederPrefabSetup
{
    private const string ModelPath =
        "Assets/AutoFeeder/meshes/autoFeeder.fbx";
    private const string FoodPrefabPath =
        "Assets/Food/prefabs/prefab_food.prefab";
    private const string PrefabFolder =
        "Assets/Resources/AutoFeeder";
    private const string PrefabPath =
        PrefabFolder + "/prefab_AutoFeeder.prefab";
    private const string DialMaterialPath =
        PrefabFolder + "/mat_AutoFeederDialHand.mat";
    private const string MigrationKey =
        "Eggcessive.AutoFeederPrefabSetup.v1";

    static AutoFeederPrefabSetup()
    {
        EditorApplication.delayCall += EnsurePrefabOnce;
    }

    [MenuItem("Eggcessive/Prefabs/Rebuild Auto-Feeder")]
    public static void RebuildAutoFeederPrefab()
    {
        BuildPrefab();
    }

    private static void EnsurePrefabOnce()
    {
        if (SessionState.GetBool(MigrationKey, false))
        {
            return;
        }

        SessionState.SetBool(MigrationKey, true);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            BuildPrefab();
        }
    }

    private static void BuildPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        GameObject foodPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(FoodPrefabPath);
        if (model == null || foodPrefab == null)
        {
            Debug.LogError(
                "Auto-Feeder setup needs autoFeeder.fbx and prefab_food.prefab.");
            return;
        }

        EnsureFolder("Assets/Resources");
        EnsureFolder(PrefabFolder);

        GameObject root = new GameObject("prefab_AutoFeeder");
        try
        {
            GameObject modelInstance = Object.Instantiate(model, root.transform);
            modelInstance.name = "autoFeeder";
            modelInstance.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);

            Transform[] sockets = modelInstance
                .GetComponentsInChildren<Transform>(true)
                .Where(item => item.name.StartsWith(
                    "SOCKET_food_",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.name)
                .ToArray();
            if (sockets.Length <= 0)
            {
                throw new InvalidOperationException(
                    "autoFeeder.fbx has no SOCKET_food_X transforms.");
            }

            Bounds bounds = CalculateBounds(modelInstance);
            AddMachineCollider(root, bounds);
            Transform dialPivot = CreateAnalogDial(root.transform, bounds);

            AutoFeederController controller =
                root.AddComponent<AutoFeederController>();
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("foodPrefab").objectReferenceValue =
                foodPrefab;
            serialized.FindProperty("dialHand").objectReferenceValue =
                dialPivot;
            SerializedProperty socketProperty =
                serialized.FindProperty("foodSockets");
            socketProperty.arraySize = sockets.Length;
            for (int index = 0; index < sockets.Length; index++)
            {
                socketProperty.GetArrayElementAtIndex(index)
                    .objectReferenceValue = sockets[index];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Authored Auto-Feeder prefab with {sockets.Length} food sockets and an analog dial.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static Bounds CalculateBounds(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length <= 0)
        {
            return new Bounds(Vector3.zero, Vector3.one);
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds;
    }

    private static void AddMachineCollider(GameObject root, Bounds bounds)
    {
        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.center = root.transform.InverseTransformPoint(bounds.center);
        Vector3 localSize = root.transform.InverseTransformVector(bounds.size);
        collider.size = new Vector3(
            Mathf.Abs(localSize.x),
            Mathf.Abs(localSize.y),
            Mathf.Abs(localSize.z));
    }

    private static Transform CreateAnalogDial(
        Transform parent,
        Bounds bounds)
    {
        GameObject pivotObject = new GameObject("Analog Dial Hand Pivot");
        Transform pivot = pivotObject.transform;
        pivot.SetParent(parent, false);
        Vector3 localCenter = parent.InverseTransformPoint(bounds.center);
        Vector3 localTop = parent.InverseTransformPoint(
            new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
        pivot.localPosition = new Vector3(
            localCenter.x,
            localTop.y + Mathf.Max(0.015f, bounds.size.y * 0.025f),
            localCenter.z);

        float handLength = Mathf.Clamp(
            Mathf.Min(bounds.size.x, bounds.size.z) * 0.28f,
            0.08f,
            0.4f);
        GameObject hand = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hand.name = "Analog Dial Hand";
        hand.transform.SetParent(pivot, false);
        hand.transform.localPosition = new Vector3(0f, 0f, handLength * 0.5f);
        hand.transform.localScale = new Vector3(
            Mathf.Max(0.025f, handLength * 0.16f),
            Mathf.Max(0.012f, handLength * 0.07f),
            handLength);
        Collider handCollider = hand.GetComponent<Collider>();
        if (handCollider != null)
        {
            Object.DestroyImmediate(handCollider);
        }

        MeshRenderer renderer = hand.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetOrCreateDialMaterial();
        }

        return pivot;
    }

    private static Material GetOrCreateDialMaterial()
    {
        Material material =
            AssetDatabase.LoadAssetAtPath<Material>(DialMaterialPath);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Standard");
        material = new Material(shader)
        {
            name = "mat_AutoFeederDialHand",
            color = new Color(1f, 0.2f, 0.05f, 1f)
        };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor(
                "_BaseColor",
                new Color(1f, 0.2f, 0.05f, 1f));
        }

        AssetDatabase.CreateAsset(material, DialMaterialPath);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        string folder = path.Substring(separator + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder);
    }
}
#endif
