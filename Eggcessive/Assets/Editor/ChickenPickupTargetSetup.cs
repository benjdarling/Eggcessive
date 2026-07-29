using System;
using UnityEditor;
using UnityEngine;

public static class ChickenPickupTargetSetup
{
    private const string ChickenPrefabPath =
        "Assets/Chicken/prefabs/prefab_chicken.prefab";
    private const string TargetName = "Chicken Neck Pickup Target";
    private static readonly Vector3 TargetPosition =
        new Vector3(0f, 0.285f, 0.025f);

    [MenuItem("Tools/Eggcessive/Build Chicken Neck Pickup Target")]
    public static void Generate()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ChickenPrefabPath);

        try
        {
            ChickenController chicken = root.GetComponent<ChickenController>();

            if (chicken == null)
            {
                throw new MissingComponentException(nameof(ChickenController));
            }

            Transform targetTransform = root.transform.Find(TargetName);
            GameObject targetObject;

            if (targetTransform == null)
            {
                targetObject = new GameObject(TargetName);
                targetObject.transform.SetParent(root.transform, false);
            }
            else
            {
                targetObject = targetTransform.gameObject;
            }

            targetObject.layer = root.layer;
            targetObject.transform.SetLocalPositionAndRotation(
                TargetPosition,
                Quaternion.identity);
            targetObject.transform.localScale = Vector3.one;

            CapsuleCollider capsule =
                targetObject.GetComponent<CapsuleCollider>();

            if (capsule == null)
            {
                capsule = targetObject.AddComponent<CapsuleCollider>();
            }

            capsule.isTrigger = true;
            capsule.direction = 1;
            capsule.center = Vector3.zero;
            capsule.radius = 0.052f;
            capsule.height = 0.13f;

            ChickenPickupTarget pickupTarget =
                targetObject.GetComponent<ChickenPickupTarget>();

            if (pickupTarget == null)
            {
                pickupTarget = targetObject.AddComponent<ChickenPickupTarget>();
            }
            pickupTarget.Configure(chicken);
            EditorUtility.SetDirty(pickupTarget);
            PrefabUtility.SaveAsPrefabAsset(root, ChickenPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Validate();
        Debug.Log("Chicken neck pickup target rebuilt.");
    }

    [MenuItem("Tools/Eggcessive/Validate Chicken Neck Pickup Target")]
    public static void Validate()
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(ChickenPrefabPath);
        ChickenController chicken =
            prefab != null ? prefab.GetComponent<ChickenController>() : null;
        ChickenPickupTarget target =
            prefab != null
                ? prefab.GetComponentInChildren<ChickenPickupTarget>(true)
                : null;
        CapsuleCollider capsule =
            target != null ? target.GetComponent<CapsuleCollider>() : null;

        if (chicken == null
            || target == null
            || target.Chicken != chicken
            || capsule == null
            || !capsule.isTrigger
            || capsule.direction != 1
            || !Approximately(capsule.radius, 0.052f)
            || !Approximately(capsule.height, 0.13f)
            || Vector3.Distance(
                target.transform.localPosition,
                TargetPosition) > 0.0001f)
        {
            throw new InvalidOperationException(
                "The chicken neck pickup target is missing or incorrectly configured.");
        }

        Debug.Log(
            "Chicken pickup validation passed: only the authored neck capsule is draggable.");
    }

    private static bool Approximately(float left, float right)
    {
        return Mathf.Abs(left - right) <= 0.0001f;
    }
}
