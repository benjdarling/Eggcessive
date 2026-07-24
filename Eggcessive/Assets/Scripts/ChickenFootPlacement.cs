using System;
using UnityEngine;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class ChickenFootPlacement : MonoBehaviour
{
    private sealed class FootState
    {
        public Transform Bone;
        public Transform Target;
        public Behaviour Solver;
        public Vector3 HomeLocalPosition;
        public Quaternion HomeLocalRotation;
        public Vector3 PlantedPosition;
        public Quaternion PlantedRotation;
        public Vector3 StepStartPosition;
        public Quaternion StepStartRotation;
        public Vector3 StepEndPosition;
        public Quaternion StepEndRotation;
        public float StepStartTime;
        public float StepDuration;
        public bool Planted;
        public bool Stepping;
    }

    [Header("Rig")]
    [SerializeField] private string leftFootBoneName = "foot.l";
    [SerializeField] private string rightFootBoneName = "foot.r";
    [SerializeField] private Transform leftFootTarget = null;
    [SerializeField] private Transform rightFootTarget = null;
    [SerializeField] private Behaviour leftIkSolver = null;
    [SerializeField] private Behaviour rightIkSolver = null;

    [Header("Automatic Stepping")]
    [Tooltip("Foot recovery distance for slow pushes and turn-in-place.")]
    [SerializeField, Min(0.001f)] private float stepTriggerDistance = 0.012f;
    [Tooltip("Planted travel allowed at full movement speed before starting a real stride.")]
    [SerializeField, Min(0.001f)] private float movingStrideDistance = 0.05f;
    [SerializeField, Min(0.001f)] private float emergencyStepDistance = 0.07f;
    [SerializeField, Min(0.01f)] private float stepDuration = 0.13f;
    [SerializeField, Min(0.01f)] private float minimumMovingStepDuration = 0.095f;
    [SerializeField, Min(0f)] private float stepHeight = 0.022f;
    [SerializeField, Min(0f)] private float movementLeadTime = 0.065f;
    [SerializeField, Min(0f)] private float maximumLeadSpeed = 0.8f;
    [Tooltip("The landing point follows velocity changes until this portion of the step, then commits for a firm plant.")]
    [SerializeField, Range(0f, 0.9f)] private float landingCommitProgress = 0.55f;
    [SerializeField, Min(0f)] private float landingRetargetSpeed = 20f;
    [Tooltip("An overstretched second foot may lift after the active foot passes this point.")]
    [SerializeField, Range(0f, 1f)] private float emergencyOverlapProgress = 0.5f;

    [Header("Grounding")]
    [SerializeField] private LayerMask groundLayers = Physics.AllLayers;
    [SerializeField, Min(0f)] private float groundOffset = 0.008f;
    [SerializeField, Min(0.01f)] private float raycastHeight = 0.1f;
    [SerializeField, Min(0.01f)] private float raycastDistance = 0.25f;

    private readonly RaycastHit[] groundHits = new RaycastHit[8];
    private readonly FootState leftFoot = new FootState();
    private readonly FootState rightFoot = new FootState();

    private Transform characterRoot;
    private Vector3 previousRootPosition;
    private Vector3 planarVelocity;
    private bool nextFootIsLeft = true;
    private bool automaticStepping = true;
    private bool initialized;

    private void Start()
    {
        Initialize();
    }

    private void OnDisable()
    {
        SetSolverEnabled(leftFoot, false);
        SetSolverEnabled(rightFoot, false);
    }

    private void Update()
    {
        if (!EnsureInitialized())
        {
            return;
        }

        UpdateRootVelocity();
        UpdateFootTarget(leftFoot);
        UpdateFootTarget(rightFoot);

        if (automaticStepping)
        {
            TryStartAutomaticStep();
        }
    }

    private bool EnsureInitialized()
    {
        if (initialized
            && characterRoot != null
            && IsFootConfigured(leftFoot)
            && IsFootConfigured(rightFoot))
        {
            return true;
        }

        initialized = false;
        return Initialize();
    }

    private bool Initialize()
    {
        if (initialized)
        {
            return true;
        }

        ChickenController chicken = GetComponentInParent<ChickenController>();
        characterRoot = chicken != null ? chicken.transform : transform.root;
        Transform[] transforms = characterRoot.GetComponentsInChildren<Transform>(true);

        leftFoot.Bone = Array.Find(transforms, candidate => candidate.name == leftFootBoneName);
        rightFoot.Bone = Array.Find(transforms, candidate => candidate.name == rightFootBoneName);
        leftFoot.Target = leftFootTarget != null
            ? leftFootTarget
            : Array.Find(transforms, candidate => candidate.name == "ikTarget_FootL");
        rightFoot.Target = rightFootTarget != null
            ? rightFootTarget
            : Array.Find(transforms, candidate => candidate.name == "ikTarget_FootR");
        leftFoot.Solver = leftIkSolver != null ? leftIkSolver : FindIkSolver(leftFoot.Bone);
        rightFoot.Solver = rightIkSolver != null ? rightIkSolver : FindIkSolver(rightFoot.Bone);

        if (!IsFootConfigured(leftFoot) || !IsFootConfigured(rightFoot))
        {
            Debug.LogWarning(
                $"{nameof(ChickenFootPlacement)} could not find both foot bones, targets, and IK solvers below '{characterRoot.name}'.",
                this);
            enabled = false;
            return false;
        }

        CacheHomePose(leftFoot);
        CacheHomePose(rightFoot);
        PlantAtCurrentPose(leftFoot);
        PlantAtCurrentPose(rightFoot);
        previousRootPosition = characterRoot.position;
        planarVelocity = Vector3.zero;
        automaticStepping = true;
        initialized = true;
        return true;
    }

    private static Behaviour FindIkSolver(Transform footBone)
    {
        if (footBone == null)
        {
            return null;
        }

        Behaviour[] behaviours = footBone.GetComponents<Behaviour>();
        return Array.Find(
            behaviours,
            candidate => candidate.GetType().Name == "FastIKFabric");
    }

    private static bool IsFootConfigured(FootState foot)
    {
        return foot.Bone != null && foot.Target != null && foot.Solver != null;
    }

    private void CacheHomePose(FootState foot)
    {
        foot.HomeLocalPosition = characterRoot.InverseTransformPoint(foot.Target.position);
        foot.HomeLocalRotation = Quaternion.Inverse(characterRoot.rotation) * foot.Target.rotation;
    }

    private void UpdateRootVelocity()
    {
        if (Time.deltaTime <= 0f)
        {
            return;
        }

        Vector3 displacement = characterRoot.position - previousRootPosition;
        displacement.y = 0f;
        planarVelocity = Vector3.ClampMagnitude(displacement / Time.deltaTime, maximumLeadSpeed);
        previousRootPosition = characterRoot.position;
    }

    private void UpdateFootTarget(FootState foot)
    {
        if (!foot.Planted)
        {
            return;
        }

        if (!foot.Stepping)
        {
            foot.Target.SetPositionAndRotation(foot.PlantedPosition, foot.PlantedRotation);
            return;
        }

        float progress = GetStepProgress(foot);

        if (progress < landingCommitProgress)
        {
            Vector3 updatedDestination = GetDesiredFootPose(
                foot,
                true,
                out Quaternion updatedRotation);
            float retargetAmount = 1f - Mathf.Exp(-landingRetargetSpeed * Time.deltaTime);
            foot.StepEndPosition = Vector3.Lerp(
                foot.StepEndPosition,
                updatedDestination,
                retargetAmount);
            foot.StepEndRotation = Quaternion.Slerp(
                foot.StepEndRotation,
                updatedRotation,
                retargetAmount);
        }

        float easedProgress = progress * progress * (3f - 2f * progress);
        Vector3 position = Vector3.Lerp(foot.StepStartPosition, foot.StepEndPosition, easedProgress);
        position += Vector3.up * (Mathf.Sin(progress * Mathf.PI) * stepHeight);
        Quaternion rotation = Quaternion.Slerp(
            foot.StepStartRotation,
            foot.StepEndRotation,
            easedProgress);
        foot.Target.SetPositionAndRotation(position, rotation);

        if (progress < 1f)
        {
            return;
        }

        foot.Stepping = false;
        foot.PlantedPosition = foot.StepEndPosition;
        foot.PlantedRotation = foot.StepEndRotation;
        nextFootIsLeft = foot != leftFoot;
    }

    private void TryStartAutomaticStep()
    {
        if (leftFoot.Stepping && rightFoot.Stepping)
        {
            return;
        }

        // Trigger from the actual body-relative home position. Velocity lead is
        // deliberately excluded here so it cannot create rapid micro-steps.
        Vector3 leftDestination = GetDesiredFootPose(leftFoot, false, out Quaternion leftRotation);
        Vector3 rightDestination = GetDesiredFootPose(rightFoot, false, out Quaternion rightRotation);
        float leftDistance = PlanarDistance(leftFoot.PlantedPosition, leftDestination);
        float rightDistance = PlanarDistance(rightFoot.PlantedPosition, rightDestination);
        float activeStepTriggerDistance = Mathf.Lerp(
            stepTriggerDistance,
            movingStrideDistance,
            GetSpeedFraction());
        bool anotherFootIsStepping = leftFoot.Stepping || rightFoot.Stepping;
        bool mayOverlapForEmergency = !anotherFootIsStepping
            || GetStepProgress(leftFoot.Stepping ? leftFoot : rightFoot) >= emergencyOverlapProgress;
        bool leftNeedsStep = !leftFoot.Stepping
            && leftDistance >= (anotherFootIsStepping
                ? emergencyStepDistance
                : activeStepTriggerDistance)
            && mayOverlapForEmergency;
        bool rightNeedsStep = !rightFoot.Stepping
            && rightDistance >= (anotherFootIsStepping
                ? emergencyStepDistance
                : activeStepTriggerDistance)
            && mayOverlapForEmergency;

        if (!leftNeedsStep && !rightNeedsStep)
        {
            return;
        }

        FootState chosenFoot;
        Vector3 chosenDestination;
        Quaternion chosenRotation;

        if (leftNeedsStep && leftDistance >= emergencyStepDistance && leftDistance > rightDistance)
        {
            chosenFoot = leftFoot;
            chosenDestination = leftDestination;
            chosenRotation = leftRotation;
        }
        else if (rightNeedsStep && rightDistance >= emergencyStepDistance && rightDistance > leftDistance)
        {
            chosenFoot = rightFoot;
            chosenDestination = rightDestination;
            chosenRotation = rightRotation;
        }
        else if ((nextFootIsLeft && leftNeedsStep) || !rightNeedsStep)
        {
            chosenFoot = leftFoot;
            chosenDestination = leftDestination;
            chosenRotation = leftRotation;
        }
        else
        {
            chosenFoot = rightFoot;
            chosenDestination = rightDestination;
            chosenRotation = rightRotation;
        }

        // Once a stride is justified, land ahead of the body using velocity
        // prediction. This produces horizontal travel instead of an in-place hop.
        chosenDestination = GetDesiredFootPose(chosenFoot, true, out chosenRotation);
        BeginStep(chosenFoot, chosenDestination, chosenRotation);
    }

    private Vector3 GetDesiredFootPose(
        FootState foot,
        bool includeVelocityLead,
        out Quaternion rotation)
    {
        Vector3 desiredPosition = characterRoot.TransformPoint(foot.HomeLocalPosition);

        if (includeVelocityLead)
        {
            // Predict through part of the step itself as well as the configured
            // extra lead, otherwise a moving body can overtake a frozen landing.
            float predictionTime = movementLeadTime + GetMovingStepDuration() * 0.5f;
            desiredPosition += planarVelocity * predictionTime;
        }

        rotation = characterRoot.rotation * foot.HomeLocalRotation;
        return ProjectToGround(desiredPosition);
    }

    private void BeginStep(FootState foot, Vector3 destination, Quaternion rotation)
    {
        if (!IsFootConfigured(foot))
        {
            initialized = false;
            return;
        }

        foot.StepStartPosition = foot.Target.position;
        foot.StepStartRotation = foot.Target.rotation;
        foot.StepEndPosition = destination;
        foot.StepEndRotation = rotation;
        foot.StepStartTime = Time.time;
        foot.StepDuration = GetMovingStepDuration();
        foot.Stepping = true;
        foot.Planted = true;
        SetSolverEnabled(foot, true);
    }

    private void PlantAtCurrentPose(FootState foot)
    {
        foot.Stepping = false;
        foot.Planted = true;
        foot.PlantedPosition = ProjectToGround(foot.Bone.position);
        foot.PlantedRotation = foot.Target.rotation;
        foot.Target.SetPositionAndRotation(foot.PlantedPosition, foot.PlantedRotation);
        SetSolverEnabled(foot, true);
    }

    private void ReleaseFoot(FootState foot)
    {
        foot.Stepping = false;
        foot.Planted = false;
        SetSolverEnabled(foot, false);
    }

    private Vector3 ProjectToGround(Vector3 position)
    {
        Vector3 rayOrigin = position + Vector3.up * raycastHeight;
        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            Vector3.down,
            groundHits,
            raycastHeight + raycastDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        Vector3 closestPoint = position;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHits[i];

            if (hit.collider == null
                || hit.collider.transform == characterRoot
                || hit.collider.transform.IsChildOf(characterRoot)
                || hit.collider.GetComponentInParent<ChickenEgg>() != null
                || hit.collider.GetComponentInParent<FoodPile>() != null
                || hit.distance >= closestDistance)
            {
                continue;
            }

            closestDistance = hit.distance;
            closestPoint = hit.point + Vector3.up * groundOffset;
        }

        return closestPoint;
    }

    private static float PlanarDistance(Vector3 from, Vector3 to)
    {
        Vector3 offset = to - from;
        offset.y = 0f;
        return offset.magnitude;
    }

    private float GetMovingStepDuration()
    {
        return Mathf.Lerp(stepDuration, minimumMovingStepDuration, GetSpeedFraction());
    }

    private float GetSpeedFraction()
    {
        return maximumLeadSpeed > 0f
            ? Mathf.Clamp01(planarVelocity.magnitude / maximumLeadSpeed)
            : 0f;
    }

    private static float GetStepProgress(FootState foot)
    {
        if (!foot.Stepping || foot.StepDuration <= 0f)
        {
            return 1f;
        }

        return Mathf.Clamp01((Time.time - foot.StepStartTime) / foot.StepDuration);
    }

    private static void SetSolverEnabled(FootState foot, bool enabledState)
    {
        if (foot.Solver != null)
        {
            foot.Solver.enabled = enabledState;
        }
    }

    // Animation event API -------------------------------------------------

    public void AnimationUseProceduralFootsteps()
    {
        if (!EnsureInitialized())
        {
            return;
        }

        automaticStepping = true;
        PlantAtCurrentPose(leftFoot);
        PlantAtCurrentPose(rightFoot);
        nextFootIsLeft = true;
    }

    public void AnimationUseFootContacts()
    {
        if (!EnsureInitialized())
        {
            return;
        }

        automaticStepping = false;
        ReleaseFoot(leftFoot);
        ReleaseFoot(rightFoot);
    }

    public void AnimationPlantLeftFoot()
    {
        PlantFromAnimation(leftFoot);
    }

    public void AnimationReleaseLeftFoot()
    {
        ReleaseFromAnimation(leftFoot);
    }

    public void AnimationPlantRightFoot()
    {
        PlantFromAnimation(rightFoot);
    }

    public void AnimationReleaseRightFoot()
    {
        ReleaseFromAnimation(rightFoot);
    }

    private void PlantFromAnimation(FootState foot)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        automaticStepping = false;
        PlantAtCurrentPose(foot);
    }

    private void ReleaseFromAnimation(FootState foot)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        automaticStepping = false;
        ReleaseFoot(foot);
    }

    private void OnValidate()
    {
        stepTriggerDistance = Mathf.Max(0.001f, stepTriggerDistance);
        movingStrideDistance = Mathf.Max(stepTriggerDistance, movingStrideDistance);
        emergencyStepDistance = Mathf.Max(movingStrideDistance, emergencyStepDistance);
        stepDuration = Mathf.Max(0.01f, stepDuration);
        minimumMovingStepDuration = Mathf.Clamp(
            minimumMovingStepDuration,
            0.01f,
            stepDuration);
        stepHeight = Mathf.Max(0f, stepHeight);
        movementLeadTime = Mathf.Max(0f, movementLeadTime);
        maximumLeadSpeed = Mathf.Max(0f, maximumLeadSpeed);
        landingCommitProgress = Mathf.Clamp(landingCommitProgress, 0f, 0.9f);
        landingRetargetSpeed = Mathf.Max(0f, landingRetargetSpeed);
        emergencyOverlapProgress = Mathf.Clamp01(emergencyOverlapProgress);
        groundOffset = Mathf.Max(0f, groundOffset);
        raycastHeight = Mathf.Max(0.01f, raycastHeight);
        raycastDistance = Mathf.Max(0.01f, raycastDistance);
    }
}
