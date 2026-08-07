using UnityEngine;

[DisallowMultipleComponent]
public sealed class TruckSpringAnimator : MonoBehaviour
{
    [Header("Driving")]
    [SerializeField, Min(0.1f)] private float speedForFullLean = 7f;
    [SerializeField, Range(0f, 20f)] private float drivingLeanDegrees = 6f;
    [SerializeField, Range(0f, 3f)]
    private float accelerationLeanDegreesPerUnit = 0.55f;
    [SerializeField, Min(0f)] private float maximumAcceleration = 18f;
    [SerializeField, Min(0f)] private float accelerationResponse = 12f;

    [Header("Spring")]
    [SerializeField, Min(0.01f)] private float frequencyHz = 2.4f;
    [SerializeField, Range(0f, 2f)] private float dampingRatio = 0.42f;
    [SerializeField, Range(0f, 30f)] private float maximumPitchDegrees = 14f;
    [SerializeField, Range(0f, 30f)] private float maximumRollDegrees = 12f;

    [Header("Deposits")]
    [SerializeField, Min(0f)] private float minimumDepositImpulse = 30f;
    [SerializeField, Min(0f)] private float maximumDepositImpulse = 115f;
    [SerializeField, Min(1f)] private float depositForMaximumImpulse = 1000f;

    private SpringUtils.AngleSpring pitchSpring;
    private SpringUtils.AngleSpring rollSpring;
    private Transform visual;
    private Quaternion visualRestRotation = Quaternion.identity;
    private Vector3 previousPosition;
    private Vector3 previousVelocity;
    private float smoothedForwardAcceleration;
    private bool hasMotionSample;

    public void SetVisual(Transform visualTransform)
    {
        visual = visualTransform;
        visualRestRotation = visual != null
            ? visual.localRotation
            : Quaternion.identity;
        ResetMotion();
    }

    public void ResetMotion()
    {
        previousPosition = transform.position;
        previousVelocity = Vector3.zero;
        smoothedForwardAcceleration = 0f;
        pitchSpring.Reset(0f);
        rollSpring.Reset(0f);
        hasMotionSample = false;
        ApplyRotation();
    }

    public void AddDepositImpulse(long cents)
    {
        if (cents <= 0L)
        {
            return;
        }

        float dollars = Mathf.Max(0.01f, cents / 100f);
        float logarithmicAmount = Mathf.Log10(dollars + 1f);
        float maximumLogarithmicAmount = Mathf.Log10(
            depositForMaximumImpulse + 1f);
        float amount01 = maximumLogarithmicAmount > Mathf.Epsilon
            ? Mathf.Clamp01(logarithmicAmount / maximumLogarithmicAmount)
            : 1f;
        float impulse = Mathf.Lerp(
            minimumDepositImpulse,
            maximumDepositImpulse,
            amount01);
        rollSpring.AddImpulse(
            (Random.value < 0.5f ? -1f : 1f) * impulse);
        rollSpring.ClampVelocity(maximumDepositImpulse * 1.5f);
    }

    private void LateUpdate()
    {
        if (visual == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        Vector3 currentPosition = transform.position;
        if (!hasMotionSample)
        {
            previousPosition = currentPosition;
            previousVelocity = Vector3.zero;
            hasMotionSample = true;
        }

        Vector3 worldVelocity =
            (currentPosition - previousPosition) / deltaTime;
        Vector3 worldAcceleration =
            (worldVelocity - previousVelocity) / deltaTime;
        float forwardSpeed = transform.InverseTransformDirection(
            worldVelocity).z;
        float forwardAcceleration = transform.InverseTransformDirection(
            worldAcceleration).z;
        forwardAcceleration = Mathf.Clamp(
            forwardAcceleration,
            -maximumAcceleration,
            maximumAcceleration);
        float response = 1f - Mathf.Exp(-accelerationResponse * deltaTime);
        smoothedForwardAcceleration = Mathf.Lerp(
            smoothedForwardAcceleration,
            forwardAcceleration,
            response);

        float speedLean = Mathf.Clamp(
            forwardSpeed / speedForFullLean,
            -1f,
            1f) * -drivingLeanDegrees;
        float accelerationLean = -smoothedForwardAcceleration
            * accelerationLeanDegreesPerUnit;
        float targetPitch = Mathf.Clamp(
            speedLean + accelerationLean,
            -maximumPitchDegrees,
            maximumPitchDegrees);

        SpringUtils.MotionParams motion =
            SpringUtils.CalculateMotionParams(
                deltaTime,
                frequencyHz,
                dampingRatio);
        pitchSpring.Update(
            targetPitch,
            0f,
            deltaTime,
            motion);
        rollSpring.Update(
            0f,
            0f,
            deltaTime,
            motion);
        pitchSpring.ClampValue(
            -maximumPitchDegrees,
            maximumPitchDegrees);
        rollSpring.ClampValue(
            -maximumRollDegrees,
            maximumRollDegrees);

        previousPosition = currentPosition;
        previousVelocity = worldVelocity;
        ApplyRotation();
    }

    private void ApplyRotation()
    {
        if (visual != null)
        {
            visual.localRotation = visualRestRotation
                * Quaternion.Euler(
                    pitchSpring.Value,
                    0f,
                    rollSpring.Value);
        }
    }

    private void OnValidate()
    {
        speedForFullLean = Mathf.Max(0.1f, speedForFullLean);
        frequencyHz = Mathf.Max(0.01f, frequencyHz);
        maximumAcceleration = Mathf.Max(0f, maximumAcceleration);
        accelerationResponse = Mathf.Max(0f, accelerationResponse);
        maximumDepositImpulse = Mathf.Max(
            minimumDepositImpulse,
            maximumDepositImpulse);
        depositForMaximumImpulse = Mathf.Max(
            1f,
            depositForMaximumImpulse);
    }
}
