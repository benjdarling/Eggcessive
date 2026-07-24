using System;
using GatorDragonGames.JigglePhysics;
using UnityEngine;

[DefaultExecutionOrder(10200)]
[DisallowMultipleComponent]
public sealed class JigglePhysicsManager : MonoBehaviour
{
    private static JigglePhysicsManager instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        GameObject managerObject = new GameObject("[JigglePhysics] Manager");
        managerObject.AddComponent<JigglePhysicsManager>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void LateUpdate()
    {
        double currentTime = Time.timeAsDouble;

        try
        {
            JigglePhysics.ScheduleSimulate(
                Time.fixedTimeAsDouble,
                currentTime,
                Time.fixedDeltaTime);
            JigglePhysics.SchedulePose(currentTime);
            JigglePhysics.CompletePose();
        }
        catch (NullReferenceException)
        {
            ReinitializeAfterScriptReload();
        }
    }

    private static void ReinitializeAfterScriptReload()
    {
        // Package statics are reset by a script hot reload, while active
        // JiggleRig components do not receive their normal play-mode startup.
        JigglePhysics.Dispose();

        JiggleRig[] activeRigs = FindObjectsByType<JiggleRig>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (JiggleRig rig in activeRigs)
        {
            if (rig == null
                || !rig.isActiveAndEnabled
                || rig.GetJiggleRigData().rootBone == null)
            {
                continue;
            }

            rig.OnInitialize();
        }
    }

    private void OnApplicationQuit()
    {
        JigglePhysics.Dispose();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}
