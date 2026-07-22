using System;
using UnityEngine;

[DefaultExecutionOrder(10500)]
[DisallowMultipleComponent]
public sealed class ChickenTailFlutter : MonoBehaviour
{
    [Header("Bone")]
    [SerializeField] private string tailBoneName = "c_tail_00.x";

    [Header("Main Flutter")]
    [SerializeField] private float minMainRotation = -6f;
    [SerializeField] private float maxMainRotation = 6f;
    [SerializeField, Min(0.01f)] private float minMainInterval = 7f;
    [SerializeField, Min(0.01f)] private float maxMainInterval = 16f;
    [SerializeField, Min(0.01f)] private float minMainDuration = 0.4f;
    [SerializeField, Min(0.01f)] private float maxMainDuration = 0.9f;
    [SerializeField, Min(0.01f)] private float minMainPulseInterval = 0.04f;
    [SerializeField, Min(0.01f)] private float maxMainPulseInterval = 0.11f;

    [Header("Micro Flutter")]
    [SerializeField] private float minMicroRotation = -1.5f;
    [SerializeField] private float maxMicroRotation = 1.5f;
    [SerializeField, Min(0.01f)] private float minMicroInterval = 0.7f;
    [SerializeField, Min(0.01f)] private float maxMicroInterval = 2.4f;
    [SerializeField, Min(0.01f)] private float minMicroDuration = 0.05f;
    [SerializeField, Min(0.01f)] private float maxMicroDuration = 0.16f;

    private Transform tailBone;
    private Quaternion animatedLocalRotation;

    private float nextMainTime;
    private float mainStartTime;
    private float mainDuration;
    private float nextMainPulseTime;
    private float mainAngle;
    private bool mainPulseOn;
    private bool mainActive;

    private float nextMicroTime;
    private float microStartTime;
    private float microDuration;
    private float microAngle;
    private bool microActive;

    private void Awake()
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        tailBone = Array.Find(transforms, candidate => candidate.name == tailBoneName);

        if (tailBone == null)
        {
            Debug.LogWarning(
                $"{nameof(ChickenTailFlutter)} could not find '{tailBoneName}' below '{name}'.",
                this);
            enabled = false;
            return;
        }

        animatedLocalRotation = tailBone.localRotation;
    }

    private void OnEnable()
    {
        if (tailBone == null)
        {
            return;
        }

        animatedLocalRotation = tailBone.localRotation;
        mainActive = false;
        microActive = false;
        mainAngle = 0f;
        ScheduleNextMainFlutter();
        ScheduleNextMicroFlutter();
    }

    private void OnDisable()
    {
        if (tailBone != null)
        {
            tailBone.localRotation = animatedLocalRotation;
        }
    }

    private void Update()
    {
        if (tailBone == null)
        {
            return;
        }

        // Remove our previous offset before the Animator samples this frame.
        tailBone.localRotation = animatedLocalRotation;
        UpdateMainFlutter();
        UpdateMicroFlutter();
    }

    private void LateUpdate()
    {
        if (tailBone == null)
        {
            return;
        }

        // This executes after animation and Jiggle Physics. Preserve that final
        // pose, then rotate additively around the tail bone's own local X axis.
        animatedLocalRotation = tailBone.localRotation;
        float additiveAngle = Mathf.Clamp(
            mainAngle + GetMicroAngle(),
            minMainRotation,
            maxMainRotation);
        tailBone.localRotation = animatedLocalRotation
            * Quaternion.AngleAxis(additiveAngle, Vector3.right);
    }

    private void UpdateMainFlutter()
    {
        if (!mainActive)
        {
            mainAngle = 0f;

            if (Time.time >= nextMainTime)
            {
                mainStartTime = Time.time;
                mainDuration = UnityEngine.Random.Range(minMainDuration, maxMainDuration);
                mainPulseOn = true;
                mainAngle = UnityEngine.Random.Range(minMainRotation, maxMainRotation);
                nextMainPulseTime = Time.time
                    + UnityEngine.Random.Range(minMainPulseInterval, maxMainPulseInterval);
                mainActive = true;
            }

            return;
        }

        if (Time.time - mainStartTime >= mainDuration)
        {
            mainAngle = 0f;
            mainActive = false;
            ScheduleNextMainFlutter();
            return;
        }

        if (Time.time < nextMainPulseTime)
        {
            return;
        }

        mainPulseOn = !mainPulseOn;
        mainAngle = mainPulseOn
            ? UnityEngine.Random.Range(minMainRotation, maxMainRotation)
            : 0f;
        nextMainPulseTime = Time.time
            + UnityEngine.Random.Range(minMainPulseInterval, maxMainPulseInterval);
    }

    private void UpdateMicroFlutter()
    {
        if (microActive)
        {
            if (Time.time - microStartTime < microDuration)
            {
                return;
            }

            microActive = false;
            ScheduleNextMicroFlutter();
        }

        if (Time.time < nextMicroTime)
        {
            return;
        }

        microStartTime = Time.time;
        microDuration = UnityEngine.Random.Range(minMicroDuration, maxMicroDuration);
        microAngle = UnityEngine.Random.Range(minMicroRotation, maxMicroRotation);
        microActive = true;
    }

    private float GetMicroAngle()
    {
        if (!microActive)
        {
            return 0f;
        }

        float progress = (Time.time - microStartTime) / microDuration;
        return Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI) * microAngle;
    }

    private void ScheduleNextMainFlutter()
    {
        nextMainTime = Time.time + UnityEngine.Random.Range(minMainInterval, maxMainInterval);
    }

    private void ScheduleNextMicroFlutter()
    {
        nextMicroTime = Time.time + UnityEngine.Random.Range(minMicroInterval, maxMicroInterval);
    }

    private void OnValidate()
    {
        if (maxMainRotation < minMainRotation)
        {
            maxMainRotation = minMainRotation;
        }

        minMainInterval = Mathf.Max(0.01f, minMainInterval);
        maxMainInterval = Mathf.Max(minMainInterval, maxMainInterval);
        minMainDuration = Mathf.Max(0.01f, minMainDuration);
        maxMainDuration = Mathf.Max(minMainDuration, maxMainDuration);
        minMainPulseInterval = Mathf.Max(0.01f, minMainPulseInterval);
        maxMainPulseInterval = Mathf.Max(minMainPulseInterval, maxMainPulseInterval);

        if (maxMicroRotation < minMicroRotation)
        {
            maxMicroRotation = minMicroRotation;
        }

        minMicroInterval = Mathf.Max(0.01f, minMicroInterval);
        maxMicroInterval = Mathf.Max(minMicroInterval, maxMicroInterval);
        minMicroDuration = Mathf.Max(0.01f, minMicroDuration);
        maxMicroDuration = Mathf.Max(minMicroDuration, maxMicroDuration);
    }
}
