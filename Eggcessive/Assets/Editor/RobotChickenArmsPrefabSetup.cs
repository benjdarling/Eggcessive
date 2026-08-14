#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using DitzelGames.FastIK;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RobotChickenArmsPrefabSetup
{
    private const string RobotPrefabPath =
        "Assets/Collection/prefabs/prefab_EggCollectorRobot_T3.prefab";
    private const string UpperArmPrefabPath =
        "Assets/Robot/meshes/robot_arm_upper.fbx";
    private const string ForearmPrefabPath =
        "Assets/Robot/meshes/robot_arm_forearm.fbx";
    private const string HandPrefabPath =
        "Assets/Robot/meshes/robot_hand.fbx";
    private const string EggVisualPrefabPath =
        "Assets/Eggs/meshes/egg_chicken.fbx";
    private const string TargetRootName = "Chicken Arm Targets";
    private const string GrabSocketName = "SOCKET_GRAB";
    private const string GrabBlendShapeName = "grab";
    private const string MigrationKey =
        "Eggcessive.RobotChickenArmsPrefabSetup.v6";

    private sealed class ArmRig
    {
        public Transform Shoulder;
        public Transform Upper;
        public Transform Forearm;
        public Transform Hand;
        public Transform GrabSocket;
    }

    static RobotChickenArmsPrefabSetup()
    {
        EditorApplication.delayCall += EnsurePrefabOnce;
    }

    [MenuItem("Eggcessive/Prefabs/Configure T3 Robot Chicken Arms")]
    public static void ConfigureRobotChickenArms()
    {
        ConfigurePrefab(true);
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
        if (prefab != null && NeedsConfiguration(prefab))
        {
            ConfigurePrefab(false);
        }
    }

    private static bool NeedsConfiguration(GameObject prefab)
    {
        List<Transform> shoulders = FindNamedTransforms(
            prefab.transform,
            "robot_arm");
        List<Transform> uppers = FindNamedTransforms(
            prefab.transform,
            "robot_arm_upper");
        RobotArmIK[] solvers =
            prefab.GetComponentsInChildren<RobotArmIK>(true);
        FastIKFabric[] legacySolvers =
            prefab.GetComponentsInChildren<FastIKFabric>(true);
        EggCollectorRobot robot = prefab.GetComponent<EggCollectorRobot>();
        if (robot == null)
        {
            return true;
        }

        SerializedObject serializedRobot = new SerializedObject(
            robot);
        bool hasEggVisual = serializedRobot
            .FindProperty("carriedEggVisualPrefab")
            .objectReferenceValue != null;
        if (shoulders.Count != 2
            || uppers.Count != 2
            || solvers.Length != 2
            || legacySolvers.Length != 0
            || !hasEggVisual
            || CountEggStackSockets(prefab.transform) != 27)
        {
            return true;
        }

        for (int index = 0; index < solvers.Length; index++)
        {
            RobotArmIK solver = solvers[index];
            if (solver.ShoulderAttachment == null
                || solver.ShoulderAttachment.name != "robot_arm"
                || solver.UpperArm == null
                || solver.UpperArm.name != "robot_arm_upper"
                || solver.Forearm == null
                || solver.Forearm.name != "robot_arm_forearm"
                || solver.Hand == null
                || solver.Hand.name != "robot_hand"
                || solver.GrabSocket == null
                || solver.GrabSocket.name != GrabSocketName
                || solver.Target == null
                || solver.Pole == null
                || solver.CarryTarget == null
                || solver.CarryPole == null)
            {
                return true;
            }
        }

        return false;
    }

    private static void ConfigurePrefab(bool logSuccess)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RobotPrefabPath);
        if (root == null)
        {
            Debug.LogError($"Could not load {RobotPrefabPath}.");
            return;
        }

        try
        {
            EggCollectorRobot robot = root.GetComponent<EggCollectorRobot>();
            GameObject upperPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(UpperArmPrefabPath);
            GameObject forearmPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(ForearmPrefabPath);
            GameObject handPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefabPath);
            GameObject eggVisualPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(EggVisualPrefabPath);
            if (robot == null
                || upperPrefab == null
                || forearmPrefab == null
                || handPrefab == null
                || eggVisualPrefab == null)
            {
                throw new UnityException(
                    "The T3 robot or one of its arm model prefabs is missing.");
            }

            Transform placeholderArms = root.transform.Find("Chicken Arms");
            if (placeholderArms != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    placeholderArms.gameObject);
            }

            FastIKFabric[] oldSolvers =
                root.GetComponentsInChildren<FastIKFabric>(true);
            for (int index = 0; index < oldSolvers.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(oldSolvers[index]);
            }

            RobotArmIK[] oldRobotSolvers =
                root.GetComponentsInChildren<RobotArmIK>(true);
            for (int index = 0; index < oldRobotSolvers.Length; index++)
            {
                UnityEngine.Object.DestroyImmediate(oldRobotSolvers[index]);
            }

            List<Transform> shoulders = FindNamedTransforms(
                root.transform,
                "robot_arm");
            if (shoulders.Count != 2)
            {
                throw new UnityException(
                    $"Expected two fixed robot_arm shoulders, found "
                    + $"{shoulders.Count}.");
            }

            var rigs = new List<ArmRig>(2);
            for (int index = 0; index < shoulders.Count; index++)
            {
                rigs.Add(RebuildArmChain(
                    shoulders[index],
                    upperPrefab,
                    forearmPrefab,
                    handPrefab));
            }

            rigs.Sort((first, second) =>
                root.transform.InverseTransformPoint(
                    first.GrabSocket.position).x.CompareTo(
                    root.transform.InverseTransformPoint(
                        second.GrabSocket.position).x));

            Transform targetsRoot = FindOrCreateChild(
                root.transform,
                TargetRootName);
            var solvers = new RobotArmIK[2];
            var targets = new Transform[2];
            var carrySlots = new Transform[2];
            var carryPoles = new Transform[2];
            var grabSockets = new Transform[2];
            var handRenderers = new SkinnedMeshRenderer[2];

            for (int index = 0; index < rigs.Count; index++)
            {
                float side = index == 0 ? -1f : 1f;
                string label = index == 0 ? "Left" : "Right";
                string carryName = $"{label} Chicken Carry Socket";
                carrySlots[index] = targetsRoot.Find(carryName);
                if (carrySlots[index] == null)
                {
                    carrySlots[index] = FindOrCreateChild(
                        targetsRoot,
                        carryName);
                    carrySlots[index].localPosition = new Vector3(
                        side * 0.49f,
                        0.56f,
                        0.31f);
                }

                string carryPoleName = $"{label} Chicken Carry Pole";
                carryPoles[index] = targetsRoot.Find(carryPoleName);
                if (carryPoles[index] == null)
                {
                    carryPoles[index] = FindOrCreateChild(
                        targetsRoot,
                        carryPoleName);
                    carryPoles[index].localPosition = new Vector3(
                        side * 0.52f,
                        0.62f,
                        -0.12f);
                }

                string targetName = $"{label} Arm IK Target";
                targets[index] = targetsRoot.Find(targetName);
                if (targets[index] == null)
                {
                    targets[index] = FindOrCreateChild(
                        targetsRoot,
                        targetName);
                    targets[index].position = carrySlots[index].position;
                    targets[index].rotation = rigs[index].GrabSocket.rotation;
                }

                string poleName = $"{label} Arm IK Pole";
                Transform pole = targetsRoot.Find(poleName);
                if (pole == null)
                {
                    pole = FindOrCreateChild(targetsRoot, poleName);
                    pole.localPosition = new Vector3(
                        side * 0.52f,
                        0.62f,
                        -0.12f);
                }

                ArmRig rig = rigs[index];
                RobotArmIK solver = rig.Upper.gameObject
                    .AddComponent<RobotArmIK>();
                solver.Configure(
                    rig.Shoulder,
                    rig.Upper,
                    rig.Forearm,
                    rig.Hand,
                    rig.GrabSocket,
                    targets[index],
                    pole,
                    carrySlots[index],
                    carryPoles[index]);
                solvers[index] = solver;
                grabSockets[index] = rig.GrabSocket;
                handRenderers[index] = FindGrabRenderer(rig.GrabSocket);
            }

            SerializedObject serializedRobot = new SerializedObject(robot);
            serializedRobot.FindProperty("carriedEggVisualPrefab")
                .objectReferenceValue = eggVisualPrefab;
            serializedRobot.FindProperty("chickenArmRoot")
                .objectReferenceValue = null;
            AssignArray(
                serializedRobot.FindProperty("chickenArmSolvers"),
                solvers);
            AssignArray(
                serializedRobot.FindProperty("chickenArmTargets"),
                targets);
            AssignArray(
                serializedRobot.FindProperty("chickenCarrySlots"),
                carrySlots);
            AssignArray(
                serializedRobot.FindProperty("chickenGrabSockets"),
                grabSockets);
            AssignArray(
                serializedRobot.FindProperty("chickenHandRenderers"),
                handRenderers);
            serializedRobot.FindProperty("chickenHandGrabBlendShapeName")
                .stringValue = GrabBlendShapeName;
            serializedRobot.FindProperty("chickenHandGrabAmount")
                .floatValue = 1f;
            serializedRobot.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, RobotPrefabPath);
            AssetDatabase.SaveAssets();
            if (logSuccess)
            {
                Debug.Log(
                    "Configured both T3 arms with fixed shoulder housings, "
                    + "ball-joint shoulders, hinged elbows, and swivelling "
                    + "hands.",
                    root);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static ArmRig RebuildArmChain(
        Transform shoulder,
        GameObject upperPrefab,
        GameObject forearmPrefab,
        GameObject handPrefab)
    {
        RemoveNamedDescendants(shoulder, "robot_arm_upper");
        RemoveNamedDescendants(shoulder, "robot_arm_forearm");
        RemoveNamedDescendants(shoulder, "robot_hand");

        Transform upperSocket = FindNamedDescendant(
            shoulder,
            "SOCKET_ARM_UPPER");
        if (upperSocket == null)
        {
            throw new UnityException(
                $"{shoulder.name} is missing SOCKET_ARM_UPPER.");
        }

        Transform upper = InstantiateModel(upperPrefab, upperSocket);
        Transform forearmSocket = FindNamedDescendant(
            upper,
            "SOCKET_ARM_FOREARM");
        Transform forearm = InstantiateModel(forearmPrefab, forearmSocket);
        Transform handSocket = FindNamedDescendant(forearm, "SOCKET_HAND");
        Transform hand = InstantiateModel(handPrefab, handSocket);
        Transform grabSocket = FindNamedDescendant(hand, GrabSocketName);
        if (forearmSocket == null || handSocket == null || grabSocket == null)
        {
            throw new UnityException(
                $"The arm chain below {shoulder.name} is missing a required "
                + "model socket.");
        }

        return new ArmRig
        {
            Shoulder = shoulder,
            Upper = upper,
            Forearm = forearm,
            Hand = hand,
            GrabSocket = grabSocket
        };
    }

    private static Transform InstantiateModel(
        GameObject prefab,
        Transform parent)
    {
        if (parent == null)
        {
            throw new UnityException(
                $"Cannot instantiate {prefab.name} without its parent socket.");
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
            prefab,
            parent);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        return instance.transform;
    }

    private static void RemoveNamedDescendants(
        Transform root,
        string objectName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = transforms.Length - 1; index >= 0; index--)
        {
            Transform candidate = transforms[index];
            if (candidate != root && candidate.name == objectName)
            {
                UnityEngine.Object.DestroyImmediate(candidate.gameObject);
            }
        }
    }

    private static List<Transform> FindNamedTransforms(
        Transform root,
        string objectName)
    {
        var result = new List<Transform>();
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            if (transforms[index].name == objectName)
            {
                result.Add(transforms[index]);
            }
        }

        return result;
    }

    private static Transform FindNamedDescendant(
        Transform root,
        string objectName)
    {
        if (root == null)
        {
            return null;
        }

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            if (transforms[index].name == objectName)
            {
                return transforms[index];
            }
        }

        return null;
    }

    private static SkinnedMeshRenderer FindGrabRenderer(Transform grabSocket)
    {
        SkinnedMeshRenderer[] renderers = grabSocket.parent
            .GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int rendererIndex = 0;
             rendererIndex < renderers.Length;
             rendererIndex++)
        {
            Mesh mesh = renderers[rendererIndex].sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            for (int shapeIndex = 0;
                 shapeIndex < mesh.blendShapeCount;
                 shapeIndex++)
            {
                string shapeName = mesh.GetBlendShapeName(shapeIndex);
                if (string.Equals(
                        shapeName,
                        GrabBlendShapeName,
                        StringComparison.OrdinalIgnoreCase)
                    || shapeName.EndsWith(
                        "." + GrabBlendShapeName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return renderers[rendererIndex];
                }
            }
        }

        return null;
    }

    private static int CountEggStackSockets(Transform root)
    {
        int count = 0;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            Transform current = transforms[index];
            if (!current.name.StartsWith(
                    "SOCKET_EGG_",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Transform ancestor = current.parent;
            while (ancestor != null && ancestor != root)
            {
                if (ancestor.name.StartsWith(
                        "robot_stack",
                        StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    break;
                }

                ancestor = ancestor.parent;
            }
        }

        return count;
    }

    private static void AssignArray<T>(
        SerializedProperty property,
        T[] values)
        where T : UnityEngine.Object
    {
        property.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++)
        {
            property.GetArrayElementAtIndex(index).objectReferenceValue =
                values[index];
        }
    }

    private static Transform FindOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            return child;
        }

        var childObject = new GameObject(name);
        childObject.transform.SetParent(parent, false);
        return childObject.transform;
    }
}
#endif
