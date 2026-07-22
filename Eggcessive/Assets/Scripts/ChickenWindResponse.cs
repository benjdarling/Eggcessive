using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(10600)]
[DisallowMultipleComponent]
public sealed class ChickenWindResponse : MonoBehaviour
{
    private sealed class WindBone
    {
        public Transform transform;
        public Quaternion poseLocalRotation;
        public Vector3 smoothedSteadyWind;
        public Vector3 smoothedDynamicWind;
        public Vector3 sampleOffset;
        public float maximumBend;
    }

    [Header("Bones")]
    [SerializeField] private Transform[] combBones = Array.Empty<Transform>();
    [SerializeField] private Transform tailBone = null;
    [SerializeField] private string[] fallbackCombBoneNames =
    {
        "c_comb_01.x",
        "c_comb_02.x",
        "c_comb_03.x"
    };
    [SerializeField] private string fallbackTailBoneName = "c_tail_00.x";

    [Header("Wind Bending")]
    [SerializeField, Min(0f)] private float steadyBendDegreesPerStrength = 6f;
    [SerializeField, Min(0f)] private float turbulenceBendDegreesPerStrength = 45f;
    [SerializeField, Min(0f)] private float steadyResponseSpeed = 5f;
    [SerializeField, Min(0f)] private float turbulenceResponseSpeed = 22f;
    [SerializeField, Range(0f, 45f)] private float combMaximumBend = 25f;
    [SerializeField, Range(0f, 45f)] private float tailMaximumBend = 20f;
    [SerializeField, Min(0f)] private float perBoneTurbulenceOffset = 0.08f;

    private WindBone[] windBones;

    private void Awake()
    {
        BuildWindBones();
    }

    private void OnEnable()
    {
        if (windBones == null || windBones.Length == 0)
        {
            BuildWindBones();
        }

        for (int i = 0; i < windBones.Length; i++)
        {
            WindBone windBone = windBones[i];
            windBone.poseLocalRotation = windBone.transform.localRotation;
            GlobalWind.WindSample wind = GlobalWind.SampleWindDetailed(
                windBone.transform.position + windBone.sampleOffset);
            windBone.smoothedSteadyWind = wind.steady;
            windBone.smoothedDynamicWind = wind.gust + wind.turbulence;
        }
    }

    private void Update()
    {
        if (windBones == null)
        {
            return;
        }

        // Remove last frame's wind offset before animation and Jiggle Physics run.
        for (int i = 0; i < windBones.Length; i++)
        {
            WindBone windBone = windBones[i];
            if (windBone.transform != null)
            {
                windBone.transform.localRotation = windBone.poseLocalRotation;
            }
        }
    }

    private void LateUpdate()
    {
        if (windBones == null)
        {
            return;
        }

        float deltaTime = Mathf.Min(Time.deltaTime, 1f / 30f);
        float steadyBlend = steadyResponseSpeed <= 0f
            ? 1f
            : 1f - Mathf.Exp(-steadyResponseSpeed * deltaTime);
        float turbulenceBlend = turbulenceResponseSpeed <= 0f
            ? 1f
            : 1f - Mathf.Exp(-turbulenceResponseSpeed * deltaTime);

        for (int i = 0; i < windBones.Length; i++)
        {
            WindBone windBone = windBones[i];
            Transform bone = windBone.transform;
            if (bone == null)
            {
                continue;
            }

            // Preserve the completed jiggle/look/flutter pose, then apply wind
            // around the deform bone's own local axes.
            windBone.poseLocalRotation = bone.localRotation;
            GlobalWind.WindSample sampledWind = GlobalWind.SampleWindDetailed(
                bone.position + windBone.sampleOffset);
            windBone.smoothedSteadyWind = Vector3.Lerp(
                windBone.smoothedSteadyWind,
                sampledWind.steady,
                steadyBlend);
            windBone.smoothedDynamicWind = Vector3.Lerp(
                windBone.smoothedDynamicWind,
                sampledWind.gust + sampledWind.turbulence,
                turbulenceBlend);

            Vector3 bendDegrees = GetLocalBendVector(bone, windBone.smoothedSteadyWind)
                    * steadyBendDegreesPerStrength
                + GetLocalBendVector(bone, windBone.smoothedDynamicWind)
                    * turbulenceBendDegreesPerStrength;
            float requestedBend = bendDegrees.magnitude;
            if (requestedBend < 0.00001f)
            {
                continue;
            }

            float bendAngle = Mathf.Min(requestedBend, windBone.maximumBend);
            Quaternion additiveWindRotation = Quaternion.AngleAxis(
                bendAngle,
                bendDegrees / requestedBend);
            bone.localRotation = windBone.poseLocalRotation * additiveWindRotation;
        }
    }

    private void OnDisable()
    {
        if (windBones == null)
        {
            return;
        }

        for (int i = 0; i < windBones.Length; i++)
        {
            WindBone windBone = windBones[i];
            if (windBone.transform != null)
            {
                windBone.transform.localRotation = windBone.poseLocalRotation;
            }
        }
    }

    private void BuildWindBones()
    {
        Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
        var uniqueBones = new HashSet<Transform>();
        var bones = new List<WindBone>(4);

        if (combBones != null)
        {
            for (int i = 0; i < combBones.Length; i++)
            {
                AddWindBone(bones, uniqueBones, combBones[i], combMaximumBend, bones.Count);
            }
        }

        if (bones.Count == 0 && fallbackCombBoneNames != null)
        {
            for (int i = 0; i < fallbackCombBoneNames.Length; i++)
            {
                Transform bone = FindByName(allTransforms, fallbackCombBoneNames[i]);
                AddWindBone(bones, uniqueBones, bone, combMaximumBend, bones.Count);
            }
        }

        Transform resolvedTail = tailBone != null
            ? tailBone
            : FindByName(allTransforms, fallbackTailBoneName);
        AddWindBone(bones, uniqueBones, resolvedTail, tailMaximumBend, bones.Count);

        windBones = bones.ToArray();
        if (windBones.Length < 4)
        {
            Debug.LogWarning(
                $"{nameof(ChickenWindResponse)} found {windBones.Length} of 4 expected comb/tail bones below '{name}'.",
                this);
        }
    }

    private void AddWindBone(
        List<WindBone> bones,
        HashSet<Transform> uniqueBones,
        Transform bone,
        float maximumBend,
        int index)
    {
        if (bone == null || !uniqueBones.Add(bone))
        {
            return;
        }

        float angle = index * 2.399963f;
        bones.Add(new WindBone
        {
            transform = bone,
            poseLocalRotation = bone.localRotation,
            maximumBend = maximumBend,
            sampleOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle))
                * perBoneTurbulenceOffset
        });
    }

    private static Transform FindByName(Transform[] transforms, string boneName)
    {
        return Array.Find(transforms, candidate => candidate.name == boneName);
    }

    private static Vector3 GetLocalBendVector(Transform bone, Vector3 worldWind)
    {
        Vector3 localWind = bone.InverseTransformDirection(worldWind);
        return new Vector3(localWind.z, 0f, -localWind.x);
    }

    private void OnValidate()
    {
        steadyBendDegreesPerStrength = Mathf.Max(0f, steadyBendDegreesPerStrength);
        turbulenceBendDegreesPerStrength = Mathf.Max(0f, turbulenceBendDegreesPerStrength);
        steadyResponseSpeed = Mathf.Max(0f, steadyResponseSpeed);
        turbulenceResponseSpeed = Mathf.Max(0f, turbulenceResponseSpeed);
        combMaximumBend = Mathf.Clamp(combMaximumBend, 0f, 45f);
        tailMaximumBend = Mathf.Clamp(tailMaximumBend, 0f, 45f);
        perBoneTurbulenceOffset = Mathf.Max(0f, perBoneTurbulenceOffset);
    }
}
