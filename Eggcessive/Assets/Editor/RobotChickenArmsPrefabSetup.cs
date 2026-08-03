#if UNITY_EDITOR
using DitzelGames.FastIK;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RobotChickenArmsPrefabSetup
{
    private const string RobotPrefabPath =
        "Assets/Collection/prefabs/prefab_EggCollectorRobot_T3.prefab";
    private const string ArmRootName = "Chicken Arms";
    private const string MigrationKey =
        "Eggcessive.RobotChickenArmsPrefabSetup.v1";

    static RobotChickenArmsPrefabSetup()
    {
        EditorApplication.delayCall += EnsurePrefabOnce;
    }

    [MenuItem("Eggcessive/Prefabs/Rebuild Robot Chicken Arms")]
    public static void RebuildRobotChickenArms()
    {
        BuildPrefab(true);
    }

    private static void EnsurePrefabOnce()
    {
        if (SessionState.GetBool(MigrationKey, false))
        {
            return;
        }

        SessionState.SetBool(MigrationKey, true);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            RobotPrefabPath);
        if (prefab == null
            || prefab.transform.Find(ArmRootName) != null)
        {
            return;
        }

        BuildPrefab(false);
    }

    private static void BuildPrefab(bool forceRebuild)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RobotPrefabPath);
        if (root == null)
        {
            Debug.LogError($"Could not load {RobotPrefabPath}.");
            return;
        }

        try
        {
            Transform existing = root.transform.Find(ArmRootName);
            if (existing != null)
            {
                if (!forceRebuild)
                {
                    return;
                }

                Object.DestroyImmediate(existing.gameObject);
            }

            Transform oldTargets = root.transform.Find("Chicken Arm Targets");
            if (oldTargets != null)
            {
                Object.DestroyImmediate(oldTargets.gameObject);
            }

            Material material = FindRobotMaterial(root);
            Transform armRoot = CreateChild(root.transform, ArmRootName);
            Transform targetsRoot = CreateChild(
                root.transform,
                "Chicken Arm Targets");

            var solvers = new FastIKFabric[2];
            var targets = new Transform[2];
            var carrySlots = new Transform[2];
            for (int index = 0; index < 2; index++)
            {
                float side = index == 0 ? -1f : 1f;
                string label = index == 0 ? "Left" : "Right";
                carrySlots[index] = CreateChild(
                    targetsRoot,
                    $"{label} Chicken Carry Socket");
                carrySlots[index].localPosition = new Vector3(
                    side * 0.49f,
                    0.56f,
                    0.31f);

                targets[index] = CreateChild(
                    targetsRoot,
                    $"{label} Arm IK Target");
                targets[index].localPosition =
                    carrySlots[index].localPosition;

                Transform pole = CreateChild(
                    targetsRoot,
                    $"{label} Arm IK Pole");
                pole.localPosition = new Vector3(
                    side * 0.52f,
                    0.62f,
                    -0.12f);

                Transform upper = CreateChild(
                    armRoot,
                    $"{label} Upper Arm");
                upper.localPosition = new Vector3(
                    side * 0.28f,
                    0.36f,
                    0.06f);
                Vector3 upperSegment = new Vector3(
                    side * 0.2f,
                    0.08f,
                    0.08f);
                CreateSegmentMesh(
                    upper,
                    $"{label} Upper Arm Mesh",
                    upperSegment,
                    0.075f,
                    material);

                Transform forearm = CreateChild(
                    upper,
                    $"{label} Forearm");
                forearm.localPosition = upperSegment;
                Vector3 forearmSegment = new Vector3(
                    side * 0.13f,
                    0.06f,
                    0.14f);
                CreateSegmentMesh(
                    forearm,
                    $"{label} Forearm Mesh",
                    forearmSegment,
                    0.065f,
                    material);

                Transform hand = CreateChild(
                    forearm,
                    $"{label} Hand");
                hand.localPosition = forearmSegment;
                CreateBox(
                    hand,
                    $"{label} Hand Mesh",
                    Vector3.zero,
                    new Vector3(0.11f, 0.09f, 0.13f),
                    material);

                FastIKFabric solver = hand.gameObject.AddComponent<FastIKFabric>();
                solver.ChainLength = 2;
                solver.Target = targets[index];
                solver.Pole = pole;
                solver.Iterations = 10;
                solver.Delta = 0.001f;
                solver.SnapBackStrength = 0.2f;
                solvers[index] = solver;
            }

            EggCollectorRobot robot = root.GetComponent<EggCollectorRobot>();
            if (robot == null)
            {
                throw new UnityException(
                    $"{root.name} has no {nameof(EggCollectorRobot)} component.");
            }

            SerializedObject serializedRobot = new SerializedObject(robot);
            serializedRobot.FindProperty("chickenArmRoot").objectReferenceValue =
                armRoot.gameObject;
            AssignArray(
                serializedRobot.FindProperty("chickenArmSolvers"),
                solvers);
            AssignArray(
                serializedRobot.FindProperty("chickenArmTargets"),
                targets);
            AssignArray(
                serializedRobot.FindProperty("chickenCarrySlots"),
                carrySlots);
            serializedRobot.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, RobotPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Authored paired FastIK chicken arms on the tier-3 robot prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AssignArray<T>(
        SerializedProperty property,
        T[] values)
        where T : Object
    {
        property.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++)
        {
            property.GetArrayElementAtIndex(index).objectReferenceValue =
                values[index];
        }
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private static void CreateSegmentMesh(
        Transform parent,
        string name,
        Vector3 segment,
        float thickness,
        Material material)
    {
        GameObject mesh = CreateBox(
            parent,
            name,
            segment * 0.5f,
            new Vector3(thickness, segment.magnitude, thickness),
            material);
        mesh.transform.localRotation = Quaternion.FromToRotation(
            Vector3.up,
            segment.normalized);
    }

    private static GameObject CreateBox(
        Transform parent,
        string name,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = localScale;
        Collider collider = box.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        MeshRenderer renderer = box.GetComponent<MeshRenderer>();
        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }

        return box;
    }

    private static Material FindRobotMaterial(GameObject root)
    {
        MeshRenderer[] renderers =
            root.GetComponentsInChildren<MeshRenderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            if (renderers[index].sharedMaterial != null)
            {
                return renderers[index].sharedMaterial;
            }
        }

        return null;
    }
}
#endif
