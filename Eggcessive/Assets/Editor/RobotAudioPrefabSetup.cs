#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RobotAudioPrefabSetup
{
    private static readonly string[] RobotPrefabPaths =
    {
        "Assets/Collection/prefabs/prefab_EggCollectorRobot_T1.prefab",
        "Assets/Collection/prefabs/prefab_EggCollectorRobot_T2.prefab",
        "Assets/Collection/prefabs/prefab_EggCollectorRobot_T3.prefab"
    };

    private const string MotorClipPath = "Assets/Sounds/robot_motor.wav";
    private const string ThinkClipPath = "Assets/Sounds/robot_think.wav";
    private const string DoneClipPath = "Assets/Sounds/robot_done.wav";
    private const string SessionKey = "Eggcessive.RobotAudioPrefabSetup.v1";

    static RobotAudioPrefabSetup()
    {
        EditorApplication.delayCall += EnsureRobotAudioOnce;
    }

    [MenuItem("Eggcessive/Prefabs/Configure Robot Audio")]
    public static void ConfigureRobotAudio()
    {
        ConfigureAll(true);
    }

    private static void EnsureRobotAudioOnce()
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
        AudioClip motorClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
            MotorClipPath);
        AudioClip thinkClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
            ThinkClipPath);
        AudioClip doneClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
            DoneClipPath);
        if (motorClip == null || thinkClip == null || doneClip == null)
        {
            if (logSuccess)
            {
                Debug.LogError("One or more robot audio clips are missing.");
            }

            return false;
        }

        bool changedAny = false;
        for (int index = 0; index < RobotPrefabPaths.Length; index++)
        {
            string path = RobotPrefabPaths[index];
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
                bool changed = AssignClip(
                        serializedRobot,
                        "robotMotorClip",
                        motorClip)
                    | AssignClip(
                        serializedRobot,
                        "robotThinkClip",
                        thinkClip)
                    | AssignClip(
                        serializedRobot,
                        "robotDoneClip",
                        doneClip);
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
            Debug.Log("Configured motor, think, and done audio on robot T1-T3.");
        }

        return true;
    }

    private static bool AssignClip(
        SerializedObject serializedRobot,
        string propertyName,
        AudioClip clip)
    {
        SerializedProperty property = serializedRobot.FindProperty(
            propertyName);
        if (property.objectReferenceValue == clip)
        {
            return false;
        }

        property.objectReferenceValue = clip;
        return true;
    }
}
#endif
