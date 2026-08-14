#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RobotFacePrefabSetup
{
    private static readonly string[] RobotPrefabPaths =
    {
        "Assets/Collection/prefabs/prefab_EggCollectorRobot_T1.prefab",
        "Assets/Collection/prefabs/prefab_EggCollectorRobot_T2.prefab",
        "Assets/Collection/prefabs/prefab_EggCollectorRobot_T3.prefab"
    };

    private static readonly string[] TexturePropertyNames =
    {
        "robotFaceIdleTexture",
        "robotFaceCarryEggsTexture",
        "robotFaceCashInTexture",
        "robotFaceCarryChickensTexture"
    };

    private static readonly string[] TexturePaths =
    {
        "Assets/Textures/t_robot_face_idle.png",
        "Assets/Textures/t_robot_face_carryEggs.png",
        "Assets/Textures/t_robot_face_cashIn.png",
        "Assets/Textures/t_robot_face_carryChickens.png"
    };

    private const string SessionKey = "Eggcessive.RobotFacePrefabSetup.v1";

    static RobotFacePrefabSetup()
    {
        EditorApplication.delayCall += EnsureRobotFacesOnce;
    }

    [MenuItem("Eggcessive/Prefabs/Configure Robot Faces")]
    public static void ConfigureRobotFaces()
    {
        ConfigureAll(true);
    }

    private static void EnsureRobotFacesOnce()
    {
        if (SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        if (ConfigureAll(false))
        {
            SessionState.SetBool(SessionKey, true);
        }
    }

    private static bool ConfigureAll(bool logSuccess)
    {
        var textures = new Texture2D[TexturePaths.Length];
        for (int index = 0; index < textures.Length; index++)
        {
            textures[index] = AssetDatabase.LoadAssetAtPath<Texture2D>(
                TexturePaths[index]);
            if (textures[index] == null)
            {
                if (logSuccess)
                {
                    Debug.LogError($"Missing robot face: {TexturePaths[index]}");
                }

                return false;
            }
        }

        bool changedAny = false;
        for (int prefabIndex = 0;
             prefabIndex < RobotPrefabPaths.Length;
             prefabIndex++)
        {
            string path = RobotPrefabPaths[prefabIndex];
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                continue;
            }

            try
            {
                EggCollectorRobot robot = root.GetComponent<EggCollectorRobot>();
                if (robot == null)
                {
                    continue;
                }

                SerializedObject serializedRobot = new SerializedObject(robot);
                bool changed = false;
                for (int textureIndex = 0;
                     textureIndex < textures.Length;
                     textureIndex++)
                {
                    SerializedProperty property = serializedRobot.FindProperty(
                        TexturePropertyNames[textureIndex]);
                    if (property.objectReferenceValue != textures[textureIndex])
                    {
                        property.objectReferenceValue = textures[textureIndex];
                        changed = true;
                    }
                }

                if (!changed)
                {
                    continue;
                }

                serializedRobot.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                changedAny = true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        if (changedAny)
        {
            AssetDatabase.SaveAssets();
        }

        if (logSuccess)
        {
            Debug.Log("Configured state-driven robot faces on robot T1-T3.");
        }

        return true;
    }
}
#endif
