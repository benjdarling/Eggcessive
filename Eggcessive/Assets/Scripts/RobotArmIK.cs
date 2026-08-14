using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class RobotArmIK : MonoBehaviour
{
    public enum AuthoredPose
    {
        Idle,
        Carry
    }

    [Header("Fixed Attachment")]
    [Tooltip("The fixed shoulder housing. This transform is never moved.")]
    public Transform ShoulderAttachment;

    [Header("Moving Joints")]
    [Tooltip("Ball joint at the shoulder.")]
    public Transform UpperArm;
    [Tooltip("Single bending joint at the elbow.")]
    public Transform Forearm;
    [Tooltip("Axial swivel joint at the wrist.")]
    public Transform Hand;
    public Transform GrabSocket;

    [Header("Idle Pose / Runtime Controls")]
    [Tooltip("The saved idle pose target. Gameplay temporarily moves this "
        + "transform while reaching for a chicken.")]
    public Transform Target;
    public Transform Pole;

    [Header("Carry Pose Controls")]
    public Transform CarryTarget;
    public Transform CarryPole;

    [Header("IK Settings")]
    [Range(0f, 1f)] public float HandSwivelWeight = 1f;

    [Header("Scene Authoring")]
    [Tooltip("Solve the arm in Edit Mode so its pose can be authored with "
        + "the target and pole Scene handles.")]
    public bool PreviewInEditMode = true;
    [Tooltip("Chooses which saved pose is displayed and edited in the "
        + "Scene view. This does not affect gameplay state.")]
    public AuthoredPose PoseToAuthor = AuthoredPose.Idle;

    private Transform cachedUpper;
    private Transform cachedForearm;
    private Transform cachedHand;
    [SerializeField, HideInInspector]
    private Quaternion upperBindLocalRotation = Quaternion.identity;
    [SerializeField, HideInInspector]
    private Quaternion forearmBindLocalRotation = Quaternion.identity;
    [SerializeField, HideInInspector]
    private Quaternion handBindLocalRotation = Quaternion.identity;
    [SerializeField, HideInInspector] private bool bindPoseCaptured;
    private bool initialized;
    private Vector3 runtimeIdleTargetLocalPosition;
    private Quaternion runtimeIdleTargetLocalRotation;
    private Vector3 runtimeIdlePoleLocalPosition;
    private Quaternion runtimeIdlePoleLocalRotation;
    private bool runtimeIdlePoseCaptured;

    public void Configure(
        Transform shoulderAttachment,
        Transform upperArm,
        Transform forearm,
        Transform hand,
        Transform grabSocket,
        Transform target,
        Transform pole,
        Transform carryTarget,
        Transform carryPole)
    {
        bool jointsChanged = UpperArm != upperArm
            || Forearm != forearm
            || Hand != hand;
        ShoulderAttachment = shoulderAttachment;
        UpperArm = upperArm;
        Forearm = forearm;
        Hand = hand;
        GrabSocket = grabSocket;
        Target = target;
        Pole = pole;
        CarryTarget = carryTarget;
        CarryPole = carryPole;
        if (jointsChanged || !bindPoseCaptured)
        {
            CaptureCurrentAsBindPose();
        }
        else
        {
            Initialize();
        }
    }

    [ContextMenu("Capture Current Rotations As Bind Pose")]
    public void CaptureCurrentAsBindPose()
    {
        if (!HasJointReferences())
        {
            return;
        }

        upperBindLocalRotation = UpperArm.localRotation;
        forearmBindLocalRotation = Forearm.localRotation;
        handBindLocalRotation = Hand.localRotation;
        bindPoseCaptured = true;
        CacheJointReferences();
        initialized = HasRequiredReferences();
    }

    [ContextMenu("Reset To Bind Pose")]
    public void ResetToBindPose()
    {
        RestoreBindPose();
    }

    public void SolveNow()
    {
        Solve();
    }

    public Transform GetAuthoringTarget()
    {
        return GetPoseTarget(PoseToAuthor);
    }

    public Transform GetAuthoringPole()
    {
        return GetPosePole(PoseToAuthor);
    }

    public Transform GetPoseTarget(AuthoredPose pose)
    {
        return pose == AuthoredPose.Carry && CarryTarget != null
            ? CarryTarget
            : Target;
    }

    public Transform GetPosePole(AuthoredPose pose)
    {
        return pose == AuthoredPose.Carry && CarryPole != null
            ? CarryPole
            : Pole;
    }

    public void CaptureRuntimeIdlePose()
    {
        if (Target == null || Pole == null)
        {
            runtimeIdlePoseCaptured = false;
            return;
        }

        runtimeIdleTargetLocalPosition = Target.localPosition;
        runtimeIdleTargetLocalRotation = Target.localRotation;
        runtimeIdlePoleLocalPosition = Pole.localPosition;
        runtimeIdlePoleLocalRotation = Pole.localRotation;
        runtimeIdlePoseCaptured = true;
    }

    public void ApplyIdlePose()
    {
        EnsureRuntimeIdlePose();
        if (!runtimeIdlePoseCaptured)
        {
            return;
        }

        Target.SetLocalPositionAndRotation(
            runtimeIdleTargetLocalPosition,
            runtimeIdleTargetLocalRotation);
        Pole.SetLocalPositionAndRotation(
            runtimeIdlePoleLocalPosition,
            runtimeIdlePoleLocalRotation);
    }

    public void ApplyCarryPose()
    {
        if (Target != null && CarryTarget != null)
        {
            Target.SetPositionAndRotation(
                CarryTarget.position,
                CarryTarget.rotation);
        }

        if (Pole != null && CarryPole != null)
        {
            Pole.SetPositionAndRotation(CarryPole.position, CarryPole.rotation);
        }
    }

    public void ApplyReachPose(Vector3 grabPosition)
    {
        ApplyIdlePose();
        if (Target != null)
        {
            Target.position = grabPosition;
        }
    }

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();
    }

    private void LateUpdate()
    {
        if (!Application.IsPlaying(gameObject) && !PreviewInEditMode)
        {
            RestoreBindPose();
            return;
        }

        Transform solvedTarget = Application.IsPlaying(gameObject)
            ? Target
            : GetAuthoringTarget();
        Transform solvedPole = Application.IsPlaying(gameObject)
            ? Pole
            : GetAuthoringPole();
        Solve(solvedTarget, solvedPole);
    }

    private void OnDisable()
    {
        RestoreBindPose();
    }

    private void Initialize()
    {
        if (!HasRequiredReferences())
        {
            initialized = false;
            return;
        }

        if (!bindPoseCaptured)
        {
            CaptureCurrentAsBindPose();
            return;
        }

        CacheJointReferences();
        initialized = true;
    }

    private void Solve()
    {
        Transform solvedTarget = Application.IsPlaying(gameObject)
            ? Target
            : GetAuthoringTarget();
        Transform solvedPole = Application.IsPlaying(gameObject)
            ? Pole
            : GetAuthoringPole();
        Solve(solvedTarget, solvedPole);
    }

    private void Solve(Transform solvedTarget, Transform solvedPole)
    {
        if (!HasRequiredReferences() || solvedTarget == null)
        {
            return;
        }

        if (!initialized
            || cachedUpper != UpperArm
            || cachedForearm != Forearm
            || cachedHand != Hand)
        {
            Initialize();
        }

        RestoreBindPose();

        Vector3 shoulderPosition = UpperArm.position;
        Vector3 targetOffset = solvedTarget.position - shoulderPosition;
        float targetDistance = targetOffset.magnitude;
        if (targetDistance < 0.0001f)
        {
            return;
        }

        float upperLength = Vector3.Distance(
            shoulderPosition,
            Forearm.position);
        float lowerLength = Vector3.Distance(
            Forearm.position,
            GrabSocket.position);
        if (upperLength < 0.0001f || lowerLength < 0.0001f)
        {
            return;
        }

        Vector3 reachDirection = targetOffset / targetDistance;
        float minimumReach = Mathf.Abs(upperLength - lowerLength) + 0.0001f;
        float maximumReach = upperLength + lowerLength - 0.0001f;
        float solvedDistance = Mathf.Clamp(
            targetDistance,
            minimumReach,
            maximumReach);
        Vector3 solvedTargetPosition = shoulderPosition
            + reachDirection * solvedDistance;

        Vector3 bendDirection = GetBendDirection(
            shoulderPosition,
            reachDirection,
            solvedPole);
        float elbowAlong = (
            upperLength * upperLength
            - lowerLength * lowerLength
            + solvedDistance * solvedDistance)
            / (2f * solvedDistance);
        float elbowHeight = Mathf.Sqrt(Mathf.Max(
            0f,
            upperLength * upperLength - elbowAlong * elbowAlong));
        Vector3 desiredElbow = shoulderPosition
            + reachDirection * elbowAlong
            + bendDirection * elbowHeight;

        Vector3 currentUpperDirection =
            Forearm.position - shoulderPosition;
        Vector3 desiredUpperDirection = desiredElbow - shoulderPosition;
        UpperArm.rotation = Quaternion.FromToRotation(
            currentUpperDirection,
            desiredUpperDirection) * UpperArm.rotation;

        Vector3 elbowPosition = Forearm.position;
        Vector3 currentLowerDirection =
            GrabSocket.position - elbowPosition;
        Vector3 desiredLowerDirection = solvedTargetPosition - elbowPosition;
        Forearm.rotation = Quaternion.FromToRotation(
            currentLowerDirection,
            desiredLowerDirection) * Forearm.rotation;

        ApplyHandSwivel(solvedTarget);
    }

    private Vector3 GetBendDirection(
        Vector3 shoulderPosition,
        Vector3 reachDirection,
        Transform solvedPole)
    {
        Vector3 poleOffset = solvedPole != null
            ? solvedPole.position - shoulderPosition
            : Forearm.position - shoulderPosition;
        Vector3 bendDirection = Vector3.ProjectOnPlane(
            poleOffset,
            reachDirection);
        if (bendDirection.sqrMagnitude > 0.000001f)
        {
            return bendDirection.normalized;
        }

        bendDirection = Vector3.ProjectOnPlane(
            UpperArm.up,
            reachDirection);
        if (bendDirection.sqrMagnitude > 0.000001f)
        {
            return bendDirection.normalized;
        }

        return Vector3.ProjectOnPlane(
            UpperArm.right,
            reachDirection).normalized;
    }

    private void ApplyHandSwivel(Transform solvedTarget)
    {
        if (HandSwivelWeight <= 0f || solvedTarget == null)
        {
            return;
        }

        Vector3 swivelAxis = GrabSocket.position - Hand.position;
        if (swivelAxis.sqrMagnitude < 0.000001f)
        {
            swivelAxis = GrabSocket.forward;
        }

        swivelAxis.Normalize();
        Vector3 currentReference = Vector3.ProjectOnPlane(
            GrabSocket.up,
            swivelAxis);
        Vector3 targetReference = Vector3.ProjectOnPlane(
            solvedTarget.up,
            swivelAxis);
        if (currentReference.sqrMagnitude < 0.000001f
            || targetReference.sqrMagnitude < 0.000001f)
        {
            currentReference = Vector3.ProjectOnPlane(
                GrabSocket.right,
                swivelAxis);
            targetReference = Vector3.ProjectOnPlane(
                solvedTarget.right,
                swivelAxis);
        }

        if (currentReference.sqrMagnitude < 0.000001f
            || targetReference.sqrMagnitude < 0.000001f)
        {
            return;
        }

        float swivelAngle = Vector3.SignedAngle(
            currentReference,
            targetReference,
            swivelAxis) * HandSwivelWeight;
        Hand.rotation = Quaternion.AngleAxis(swivelAngle, swivelAxis)
            * Hand.rotation;
    }

    private bool HasRequiredReferences()
    {
        return HasJointReferences()
            && GrabSocket != null
            && Target != null;
    }

    private bool HasJointReferences()
    {
        return UpperArm != null && Forearm != null && Hand != null;
    }

    private void CacheJointReferences()
    {
        cachedUpper = UpperArm;
        cachedForearm = Forearm;
        cachedHand = Hand;
    }

    private void EnsureRuntimeIdlePose()
    {
        if (!runtimeIdlePoseCaptured)
        {
            CaptureRuntimeIdlePose();
        }
    }

    private void RestoreBindPose()
    {
        if (!bindPoseCaptured || !HasJointReferences())
        {
            return;
        }

        UpperArm.localRotation = upperBindLocalRotation;
        Forearm.localRotation = forearmBindLocalRotation;
        Hand.localRotation = handBindLocalRotation;
    }

    private void OnDrawGizmosSelected()
    {
        if (UpperArm == null || Forearm == null || GrabSocket == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(UpperArm.position, Forearm.position);
        Gizmos.DrawLine(Forearm.position, GrabSocket.position);
        Transform authoredTarget = GetAuthoringTarget();
        if (authoredTarget != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(authoredTarget.position, 0.025f);
        }
    }
}
