using UnityEngine;

[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class ChickenMotionLean : MonoBehaviour
{
    [Header("Visual Root")]
    [SerializeField] private Transform visualRoot = null;

    [Header("Velocity Lean")]
    [SerializeField, Min(0.01f)] private float fullLeanSpeed = 0.6f;
    [SerializeField, Range(0f, 45f)] private float forwardVelocityLean = 7f;
    [SerializeField, Range(0f, 45f)] private float sidewaysVelocityLean = 6f;

    [Header("Acceleration Lean")]
    [SerializeField, Min(0.01f)] private float fullLeanAcceleration = 2.5f;
    [SerializeField, Range(0f, 45f)] private float forwardAccelerationLean = 4f;
    [SerializeField, Range(0f, 45f)] private float sidewaysAccelerationLean = 4f;

    [Header("Response")]
    [SerializeField, Min(0f)] private float velocityTrackingSpeed = 18f;
    [SerializeField, Min(0f)] private float accelerationTrackingSpeed = 12f;
    [SerializeField, Min(0.01f)] private float leanSmoothTime = 0.08f;

    private Quaternion animatedLocalRotation;
    private Vector3 previousPosition;
    private Vector3 trackedVelocity;
    private Vector3 previousTrackedVelocity;
    private Vector3 trackedAcceleration;
    private float currentPitch;
    private float currentRoll;
    private float pitchVelocity;
    private float rollVelocity;
    private bool initialized;

    private void Awake()
    {
        if (visualRoot == null)
        {
            Animator animator = GetComponentInChildren<Animator>(true);
            visualRoot = animator != null ? animator.transform : null;
        }

        if (visualRoot == null)
        {
            Debug.LogWarning(
                $"{nameof(ChickenMotionLean)} could not find an Animator below '{name}'.",
                this);
            enabled = false;
            return;
        }

        animatedLocalRotation = visualRoot.localRotation;
    }

    private void OnEnable()
    {
        if (visualRoot == null)
        {
            return;
        }

        animatedLocalRotation = visualRoot.localRotation;
        previousPosition = transform.position;
        trackedVelocity = Vector3.zero;
        previousTrackedVelocity = Vector3.zero;
        trackedAcceleration = Vector3.zero;
        currentPitch = 0f;
        currentRoll = 0f;
        pitchVelocity = 0f;
        rollVelocity = 0f;
        initialized = true;
    }

    private void OnDisable()
    {
        if (visualRoot != null)
        {
            visualRoot.localRotation = animatedLocalRotation;
        }

        initialized = false;
    }

    private void Update()
    {
        if (visualRoot != null)
        {
            // Remove the previous frame's procedural lean before animation is sampled.
            visualRoot.localRotation = animatedLocalRotation;
        }
    }

    private void LateUpdate()
    {
        if (!initialized || visualRoot == null)
        {
            return;
        }

        // Capture the Animator pose, then add a visual-only local pitch and roll.
        animatedLocalRotation = visualRoot.localRotation;
        float deltaTime = Time.deltaTime;

        if (deltaTime <= 0f)
        {
            return;
        }

        Vector3 measuredVelocity = (transform.position - previousPosition) / deltaTime;
        measuredVelocity.y = 0f;
        previousPosition = transform.position;

        float velocityFollow = 1f - Mathf.Exp(-velocityTrackingSpeed * deltaTime);
        trackedVelocity = Vector3.Lerp(trackedVelocity, measuredVelocity, velocityFollow);

        Vector3 measuredAcceleration = (trackedVelocity - previousTrackedVelocity) / deltaTime;
        measuredAcceleration.y = 0f;
        previousTrackedVelocity = trackedVelocity;
        float accelerationFollow = 1f - Mathf.Exp(-accelerationTrackingSpeed * deltaTime);
        trackedAcceleration = Vector3.Lerp(
            trackedAcceleration,
            measuredAcceleration,
            accelerationFollow);

        Vector3 localVelocity = transform.InverseTransformDirection(trackedVelocity);
        Vector3 localAcceleration = transform.InverseTransformDirection(trackedAcceleration);
        float targetPitch = Mathf.Clamp(localVelocity.z / fullLeanSpeed, -1f, 1f)
            * forwardVelocityLean
            + Mathf.Clamp(localAcceleration.z / fullLeanAcceleration, -1f, 1f)
            * forwardAccelerationLean;
        float targetRoll = -Mathf.Clamp(localVelocity.x / fullLeanSpeed, -1f, 1f)
            * sidewaysVelocityLean
            - Mathf.Clamp(localAcceleration.x / fullLeanAcceleration, -1f, 1f)
            * sidewaysAccelerationLean;

        currentPitch = Mathf.SmoothDampAngle(
            currentPitch,
            targetPitch,
            ref pitchVelocity,
            leanSmoothTime,
            Mathf.Infinity,
            deltaTime);
        currentRoll = Mathf.SmoothDampAngle(
            currentRoll,
            targetRoll,
            ref rollVelocity,
            leanSmoothTime,
            Mathf.Infinity,
            deltaTime);
        visualRoot.localRotation = animatedLocalRotation
            * Quaternion.Euler(currentPitch, 0f, currentRoll);
    }

    private void OnValidate()
    {
        fullLeanSpeed = Mathf.Max(0.01f, fullLeanSpeed);
        forwardVelocityLean = Mathf.Clamp(forwardVelocityLean, 0f, 45f);
        sidewaysVelocityLean = Mathf.Clamp(sidewaysVelocityLean, 0f, 45f);
        fullLeanAcceleration = Mathf.Max(0.01f, fullLeanAcceleration);
        forwardAccelerationLean = Mathf.Clamp(forwardAccelerationLean, 0f, 45f);
        sidewaysAccelerationLean = Mathf.Clamp(sidewaysAccelerationLean, 0f, 45f);
        velocityTrackingSpeed = Mathf.Max(0f, velocityTrackingSpeed);
        accelerationTrackingSpeed = Mathf.Max(0f, accelerationTrackingSpeed);
        leanSmoothTime = Mathf.Max(0.01f, leanSmoothTime);
    }
}
