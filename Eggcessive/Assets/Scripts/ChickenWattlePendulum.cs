using UnityEngine;

[DefaultExecutionOrder(10300)]
public sealed class ChickenWattlePendulum : MonoBehaviour
{
    private static readonly int IsEatingParameter = Animator.StringToHash("IsEating");

    [Header("Bones")]
    [SerializeField] private string anchorBoneName = "c_breast_02.l";
    [SerializeField] private string pendulumBoneName = "c_breast_01.l";

    [Header("Pendulum")]
    [SerializeField, Min(0f)] private float returnStrength = 0.65f;
    [SerializeField, Min(0f)] private float damping = 2.5f;
    [SerializeField, Min(0f)] private float gravityScale = 0.12f;
    [SerializeField, Range(0f, 90f)] private float angleLimit = 50f;
    [SerializeField, Range(0f, 1f)] private float animationFollow = 0.35f;
    [SerializeField, Range(0f, 1f)] private float blend = 0.85f;

    [Header("Wind")]
    [SerializeField, Min(0f)] private float windAccelerationMultiplier = 8f;

    [Header("Eating Override")]
    [SerializeField, Range(0f, 1f)] private float eatingAnimationFollow = 0.05f;
    [SerializeField, Min(0f)] private float eatingGravityMultiplier = 2.5f;
    [SerializeField, Range(0f, 1f)] private float eatingBlend = 1f;

    private Animator animator;
    private Transform anchorBone;
    private Transform pendulumBone;
    private Transform pendulumParent;
    private Vector3 animatedLocalPosition;
    private Vector3 simulatedPosition;
    private Vector3 previousPosition;
    private Vector3 previousAnchorPosition;
    private Vector3 previousAnimatedRestPosition;
    private bool simulationInitialized;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>(true);
        Transform[] bones = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].name == anchorBoneName)
            {
                anchorBone = bones[i];
            }
            else if (bones[i].name == pendulumBoneName)
            {
                pendulumBone = bones[i];
            }
        }

        if (anchorBone == null || pendulumBone == null || pendulumBone.parent == null)
        {
            Debug.LogWarning(
                $"{nameof(ChickenWattlePendulum)} could not find '{anchorBoneName}' and '{pendulumBoneName}' below '{name}'.",
                this);
            enabled = false;
            return;
        }

        pendulumParent = pendulumBone.parent;
        animatedLocalPosition = pendulumBone.localPosition;
    }

    private void OnEnable()
    {
        simulationInitialized = false;
    }

    private void Update()
    {
        if (pendulumBone != null)
        {
            // Remove last frame's physics offset before the Animator samples this frame.
            pendulumBone.localPosition = animatedLocalPosition;
        }
    }

    private void LateUpdate()
    {
        if (anchorBone == null || pendulumBone == null)
        {
            return;
        }

        // Update runs before Animator evaluation and LateUpdate runs after it, so this
        // is the current animation pose rather than last frame's simulated position.
        animatedLocalPosition = pendulumBone.localPosition;
        Vector3 anchorPosition = anchorBone.position;
        Vector3 animatedRestPosition = pendulumParent.TransformPoint(animatedLocalPosition);
        Vector3 restOffset = animatedRestPosition - anchorPosition;
        float tetherLength = restOffset.magnitude;
        bool isEating = animator != null && animator.GetBool(IsEatingParameter);
        float activeAnimationFollow = isEating ? eatingAnimationFollow : animationFollow;
        float activeGravityScale = gravityScale * (isEating ? eatingGravityMultiplier : 1f);
        float activeBlend = isEating ? eatingBlend : blend;

        if (tetherLength < 0.00001f)
        {
            return;
        }

        float deltaTime = Mathf.Min(Time.deltaTime, 1f / 30f);
        if (!simulationInitialized || deltaTime <= 0f)
        {
            ResetSimulation(animatedRestPosition, anchorPosition);
            return;
        }

        Vector3 anchorMovement = anchorPosition - previousAnchorPosition;
        if (anchorMovement.sqrMagnitude > tetherLength * tetherLength * 16f)
        {
            // Follow teleports without producing a single-frame physics explosion.
            simulatedPosition += anchorMovement;
            previousPosition += anchorMovement;
        }
        else
        {
            // Follow part of the animated movement directly and let the remainder
            // become pendulum lag. This keeps eating poses readable without removing sag.
            Vector3 animatedMovement = animatedRestPosition - previousAnimatedRestPosition;
            Vector3 followedMovement = animatedMovement * activeAnimationFollow;
            simulatedPosition += followedMovement;
            previousPosition += followedMovement;
        }

        Vector3 velocity = (simulatedPosition - previousPosition) * Mathf.Exp(-damping * deltaTime);
        previousPosition = simulatedPosition;

        Vector3 windAcceleration = GlobalWind.SampleWind(pendulumBone.position)
            * windAccelerationMultiplier;
        Vector3 predictedPosition = simulatedPosition
            + velocity
            + (Physics.gravity * activeGravityScale + windAcceleration) * (deltaTime * deltaTime);

        Vector3 restDirection = restOffset / tetherLength;
        Vector3 simulatedDirection = predictedPosition - anchorPosition;
        if (simulatedDirection.sqrMagnitude < 0.0000001f)
        {
            simulatedDirection = restDirection;
        }
        else
        {
            simulatedDirection.Normalize();
        }

        float returnAmount = 1f - Mathf.Exp(-returnStrength * deltaTime);
        simulatedDirection = Vector3.Slerp(simulatedDirection, restDirection, returnAmount).normalized;
        simulatedDirection = Vector3.RotateTowards(
            restDirection,
            simulatedDirection,
            angleLimit * Mathf.Deg2Rad,
            0f);

        simulatedPosition = anchorPosition + simulatedDirection * tetherLength;
        previousAnchorPosition = anchorPosition;
        previousAnimatedRestPosition = animatedRestPosition;
        pendulumBone.position = Vector3.Lerp(animatedRestPosition, simulatedPosition, activeBlend);
    }

    private void ResetSimulation(Vector3 restPosition, Vector3 anchorPosition)
    {
        simulatedPosition = restPosition;
        previousPosition = restPosition;
        previousAnchorPosition = anchorPosition;
        previousAnimatedRestPosition = restPosition;
        pendulumBone.position = restPosition;
        simulationInitialized = true;
    }

    private void OnDisable()
    {
        if (pendulumBone != null)
        {
            pendulumBone.localPosition = animatedLocalPosition;
        }
    }

    private void OnValidate()
    {
        returnStrength = Mathf.Max(0f, returnStrength);
        damping = Mathf.Max(0f, damping);
        gravityScale = Mathf.Max(0f, gravityScale);
        windAccelerationMultiplier = Mathf.Max(0f, windAccelerationMultiplier);
        angleLimit = Mathf.Clamp(angleLimit, 0f, 90f);
        animationFollow = Mathf.Clamp01(animationFollow);
        blend = Mathf.Clamp01(blend);
        eatingAnimationFollow = Mathf.Clamp01(eatingAnimationFollow);
        eatingGravityMultiplier = Mathf.Max(0f, eatingGravityMultiplier);
        eatingBlend = Mathf.Clamp01(eatingBlend);
    }
}
