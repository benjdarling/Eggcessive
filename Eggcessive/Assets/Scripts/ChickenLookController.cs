using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(10400)]
[DisallowMultipleComponent]
public sealed class ChickenLookController : MonoBehaviour
{
    private enum LookTargetType
    {
        None,
        Food,
        TravelDirection,
        Chicken,
        Cursor,
        Egg
    }

    private sealed class LookBone
    {
        public Transform Transform;
        public Quaternion AnimatedLocalRotation;
        public Vector3 LocalAimAxis;
        public float Weight;
        public float MaxAngle;
    }

    [Header("Bones")]
    [SerializeField] private string leftEyeName = "c_eye.l";
    [SerializeField] private string rightEyeName = "c_eye.r";
    [SerializeField] private string headName = "head.x";
    [SerializeField] private string upperSpineName = "spine_02.x";
    [SerializeField] private string lowerSpineName = "spine_01.x";

    [Header("Vision")]
    [SerializeField, Range(1f, 359f)] private float fieldOfView = 190f;
    [SerializeField, Min(0.02f)] private float targetScanInterval = 0.12f;
    [SerializeField, Min(0.01f)] private float targetSmoothTime = 0.18f;
    [SerializeField, Min(0.01f)] private float lookInSmoothTime = 0.16f;
    [SerializeField, Min(0.01f)] private float lookOutSmoothTime = 0.28f;

    [Header("Interest Ranges")]
    [SerializeField, Min(0f)] private float foodRange = 1.5f;
    [SerializeField, Min(0f)] private float travelLookDistance = 0.65f;
    [SerializeField, Min(0f)] private float chickenRange = 0.9f;
    [SerializeField, Min(0f)] private float cursorRange = 0.35f;
    [SerializeField, Min(0f)] private float eggRange = 0.7f;

    [Header("Rotation Influence")]
    [SerializeField, Range(0f, 1f)] private float eyeWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float headWeight = 0.7f;
    [SerializeField, Range(0f, 1f)] private float upperSpineWeight = 0.4f;
    [SerializeField, Range(0f, 1f)] private float lowerSpineWeight = 0.25f;
    [SerializeField, Range(0f, 90f)] private float eyeAngleLimit = 55f;
    [SerializeField, Range(0f, 90f)] private float headAngleLimit = 40f;
    [SerializeField, Range(0f, 90f)] private float upperSpineAngleLimit = 22.5f;
    [SerializeField, Range(0f, 90f)] private float lowerSpineAngleLimit = 12f;

    private readonly LookBone[] lookBones = new LookBone[5];
    private NavMeshAgent agent;
    private Camera viewCamera;
    private Transform currentTargetTransform;
    private Vector3 currentTargetOffset;
    private Vector3 desiredTargetPosition;
    private Vector3 smoothedTargetPosition;
    private Vector3 targetPositionVelocity;
    private float lookWeight;
    private float lookWeightVelocity;
    private float nextTargetScanTime;
    private bool hasTarget;
    private bool targetPositionInitialized;
    private LookTargetType currentTargetType;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        viewCamera = Camera.main;

        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        lookBones[0] = CreateLookBone(transforms, lowerSpineName, lowerSpineWeight, lowerSpineAngleLimit);
        lookBones[1] = CreateLookBone(transforms, upperSpineName, upperSpineWeight, upperSpineAngleLimit);
        lookBones[2] = CreateLookBone(transforms, headName, headWeight, headAngleLimit);
        lookBones[3] = CreateLookBone(transforms, leftEyeName, eyeWeight, eyeAngleLimit);
        lookBones[4] = CreateLookBone(transforms, rightEyeName, eyeWeight, eyeAngleLimit);

        for (int i = 0; i < lookBones.Length; i++)
        {
            if (lookBones[i] != null)
            {
                continue;
            }

            Debug.LogWarning($"{nameof(ChickenLookController)} is missing one or more configured look bones below '{name}'.", this);
            enabled = false;
            return;
        }
    }

    private LookBone CreateLookBone(Transform[] transforms, string boneName, float weight, float maxAngle)
    {
        Transform bone = Array.Find(transforms, candidate => candidate.name == boneName);
        if (bone == null)
        {
            return null;
        }

        return new LookBone
        {
            Transform = bone,
            AnimatedLocalRotation = bone.localRotation,
            LocalAimAxis = Quaternion.Inverse(bone.rotation) * transform.forward,
            Weight = weight,
            MaxAngle = maxAngle
        };
    }

    private void OnEnable()
    {
        hasTarget = false;
        targetPositionInitialized = false;
        currentTargetType = LookTargetType.None;
        lookWeight = 0f;
        lookWeightVelocity = 0f;
        nextTargetScanTime = 0f;
    }

    private void Update()
    {
        RestoreAnimatedPose();
    }

    private void LateUpdate()
    {
        CaptureAnimatedPose();
        RefreshLookTarget();

        float weightSmoothTime = hasTarget ? lookInSmoothTime : lookOutSmoothTime;
        lookWeight = Mathf.SmoothDamp(
            lookWeight,
            hasTarget ? 1f : 0f,
            ref lookWeightVelocity,
            weightSmoothTime,
            Mathf.Infinity,
            Time.deltaTime);

        if (hasTarget)
        {
            if (!targetPositionInitialized)
            {
                smoothedTargetPosition = desiredTargetPosition;
                targetPositionVelocity = Vector3.zero;
                targetPositionInitialized = true;
            }
            else
            {
                smoothedTargetPosition = Vector3.SmoothDamp(
                    smoothedTargetPosition,
                    desiredTargetPosition,
                    ref targetPositionVelocity,
                    targetSmoothTime,
                    Mathf.Infinity,
                    Time.deltaTime);
            }
        }

        if (lookWeight <= 0.0001f || !targetPositionInitialized)
        {
            return;
        }

        // Parents are applied first. Each child then compensates from its newly
        // inherited world pose, producing progressively reduced whole-body turns.
        for (int i = 0; i < lookBones.Length; i++)
        {
            ApplyLookRotation(lookBones[i]);
        }
    }

    private void RestoreAnimatedPose()
    {
        for (int i = 0; i < lookBones.Length; i++)
        {
            LookBone bone = lookBones[i];
            if (bone != null && bone.Transform != null)
            {
                bone.Transform.localRotation = bone.AnimatedLocalRotation;
            }
        }
    }

    private void CaptureAnimatedPose()
    {
        for (int i = 0; i < lookBones.Length; i++)
        {
            LookBone bone = lookBones[i];
            if (bone != null && bone.Transform != null)
            {
                bone.AnimatedLocalRotation = bone.Transform.localRotation;
            }
        }
    }

    private void ApplyLookRotation(LookBone bone)
    {
        Vector3 targetDirection = smoothedTargetPosition - bone.Transform.position;
        if (targetDirection.sqrMagnitude < 0.000001f)
        {
            return;
        }

        Vector3 currentAimDirection = bone.Transform.rotation * bone.LocalAimAxis;
        Quaternion lookDelta = Quaternion.FromToRotation(currentAimDirection, targetDirection.normalized);
        Quaternion limitedDelta = Quaternion.RotateTowards(Quaternion.identity, lookDelta, bone.MaxAngle);
        Quaternion weightedDelta = Quaternion.Slerp(
            Quaternion.identity,
            limitedDelta,
            bone.Weight * lookWeight);
        bone.Transform.rotation = weightedDelta * bone.Transform.rotation;
    }

    private void RefreshLookTarget()
    {
        if (currentTargetTransform != null)
        {
            desiredTargetPosition = currentTargetTransform.position + currentTargetOffset;
        }

        if (Time.time < nextTargetScanTime)
        {
            return;
        }

        nextTargetScanTime = Time.time + targetScanInterval;
        bool previouslyHadTarget = hasTarget;
        hasTarget = TrySelectTarget(out Vector3 position, out Transform target, out Vector3 offset, out LookTargetType type);

        if (!hasTarget)
        {
            currentTargetTransform = null;
            currentTargetType = LookTargetType.None;
            return;
        }

        bool targetChanged = target != currentTargetTransform || type != currentTargetType;
        currentTargetTransform = target;
        currentTargetOffset = offset;
        currentTargetType = type;
        desiredTargetPosition = position;

        if (!previouslyHadTarget)
        {
            smoothedTargetPosition = position;
            targetPositionVelocity = Vector3.zero;
            targetPositionInitialized = true;
        }
        else if (targetChanged)
        {
            targetPositionVelocity *= 0.25f;
        }
    }

    private bool TrySelectTarget(
        out Vector3 position,
        out Transform target,
        out Vector3 offset,
        out LookTargetType type)
    {
        if (TryFindFood(out position, out target, out offset))
        {
            type = LookTargetType.Food;
            return true;
        }

        if (TryGetTravelDirection(out position))
        {
            target = null;
            offset = Vector3.zero;
            type = LookTargetType.TravelDirection;
            return true;
        }

        if (TryFindChicken(out position, out target, out offset))
        {
            type = LookTargetType.Chicken;
            return true;
        }

        if (TryGetCursorPosition(out position))
        {
            target = null;
            offset = Vector3.zero;
            type = LookTargetType.Cursor;
            return true;
        }

        if (TryFindEgg(out position, out target, out offset))
        {
            type = LookTargetType.Egg;
            return true;
        }

        position = Vector3.zero;
        target = null;
        offset = Vector3.zero;
        type = LookTargetType.None;
        return false;
    }

    private bool TryFindFood(out Vector3 position, out Transform target, out Vector3 offset)
    {
        target = null;
        offset = Vector3.up * 0.04f;
        float closestDistanceSquared = float.PositiveInfinity;

        foreach (FoodPile food in FoodPile.ActivePiles)
        {
            if (food == null || !food.IsAvailable)
            {
                continue;
            }

            Vector3 candidate = food.transform.position + offset;
            float distanceSquared = (candidate - GetViewOrigin()).sqrMagnitude;
            if (distanceSquared >= closestDistanceSquared || !IsInsideView(candidate, foodRange))
            {
                continue;
            }

            target = food.transform;
            closestDistanceSquared = distanceSquared;
        }

        position = target != null ? target.position + offset : Vector3.zero;
        return target != null;
    }

    private bool TryGetTravelDirection(out Vector3 position)
    {
        position = Vector3.zero;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return false;
        }

        Vector3 travelDirection = agent.desiredVelocity;
        if (travelDirection.sqrMagnitude < 0.0025f)
        {
            return false;
        }

        position = GetViewOrigin() + travelDirection.normalized * travelLookDistance;
        return IsInsideView(position, travelLookDistance + 0.01f);
    }

    private bool TryFindChicken(out Vector3 position, out Transform target, out Vector3 offset)
    {
        target = null;
        offset = Vector3.up * 0.2f;
        float closestDistanceSquared = float.PositiveInfinity;

        foreach (ChickenController chicken in ChickenController.ActiveInstances)
        {
            if (chicken == null || chicken.gameObject == gameObject)
            {
                continue;
            }

            Vector3 candidate = chicken.transform.position + offset;
            float distanceSquared = (candidate - GetViewOrigin()).sqrMagnitude;
            if (distanceSquared >= closestDistanceSquared || !IsInsideView(candidate, chickenRange))
            {
                continue;
            }

            target = chicken.transform;
            closestDistanceSquared = distanceSquared;
        }

        position = target != null ? target.position + offset : Vector3.zero;
        return target != null;
    }

    private bool TryGetCursorPosition(out Vector3 position)
    {
        position = Vector3.zero;
        Mouse mouse = Mouse.current;
        if (mouse == null || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            return false;
        }

        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }

        if (viewCamera == null)
        {
            return false;
        }

        Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
        Plane chickenPlane = new Plane(Vector3.up, transform.position);
        if (!chickenPlane.Raycast(ray, out float rayDistance))
        {
            return false;
        }

        position = ray.GetPoint(rayDistance) + Vector3.up * 0.02f;
        return IsInsideView(position, cursorRange);
    }

    private bool TryFindEgg(out Vector3 position, out Transform target, out Vector3 offset)
    {
        target = null;
        offset = Vector3.up * 0.03f;
        float closestDistanceSquared = float.PositiveInfinity;

        foreach (ChickenEgg egg in ChickenEgg.ActiveInstances)
        {
            if (egg == null || egg.IsCollected)
            {
                continue;
            }

            Vector3 candidate = egg.transform.position + offset;
            float distanceSquared = (candidate - GetViewOrigin()).sqrMagnitude;
            if (distanceSquared >= closestDistanceSquared || !IsInsideView(candidate, eggRange))
            {
                continue;
            }

            target = egg.transform;
            closestDistanceSquared = distanceSquared;
        }

        position = target != null ? target.position + offset : Vector3.zero;
        return target != null;
    }

    private Vector3 GetViewOrigin()
    {
        return (lookBones[3].Transform.position + lookBones[4].Transform.position) * 0.5f;
    }

    private bool IsInsideView(Vector3 point, float range)
    {
        Vector3 offset = point - GetViewOrigin();
        float distanceSquared = offset.sqrMagnitude;
        if (distanceSquared < 0.000001f || distanceSquared > range * range)
        {
            return false;
        }

        float minimumDot = Mathf.Cos(fieldOfView * 0.5f * Mathf.Deg2Rad);
        return Vector3.Dot(transform.forward, offset.normalized) >= minimumDot;
    }

    private void OnDisable()
    {
        RestoreAnimatedPose();
    }

    private void OnValidate()
    {
        fieldOfView = Mathf.Clamp(fieldOfView, 1f, 359f);
        targetScanInterval = Mathf.Max(0.02f, targetScanInterval);
        targetSmoothTime = Mathf.Max(0.01f, targetSmoothTime);
        lookInSmoothTime = Mathf.Max(0.01f, lookInSmoothTime);
        lookOutSmoothTime = Mathf.Max(0.01f, lookOutSmoothTime);
        foodRange = Mathf.Max(0f, foodRange);
        travelLookDistance = Mathf.Max(0f, travelLookDistance);
        chickenRange = Mathf.Max(0f, chickenRange);
        cursorRange = Mathf.Max(0f, cursorRange);
        eggRange = Mathf.Max(0f, eggRange);
        eyeWeight = Mathf.Clamp01(eyeWeight);
        headWeight = Mathf.Clamp01(headWeight);
        upperSpineWeight = Mathf.Clamp01(upperSpineWeight);
        lowerSpineWeight = Mathf.Clamp01(lowerSpineWeight);
        eyeAngleLimit = Mathf.Clamp(eyeAngleLimit, 0f, 90f);
        headAngleLimit = Mathf.Clamp(headAngleLimit, 0f, 90f);
        upperSpineAngleLimit = Mathf.Clamp(upperSpineAngleLimit, 0f, 90f);
        lowerSpineAngleLimit = Mathf.Clamp(lowerSpineAngleLimit, 0f, 90f);
    }
}
