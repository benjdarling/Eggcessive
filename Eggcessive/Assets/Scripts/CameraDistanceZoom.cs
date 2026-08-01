using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class CameraDistanceZoom : MonoBehaviour
{
    private const int ZoomStepCount = 3;
    private const float ArrivalThreshold = 0.001f;

    [SerializeField] private Camera targetCamera = null;
    [Tooltip("Absolute pivot-to-camera distance used by zoom step 2.")]
    [SerializeField, Min(0.01f)] private float secondStepDistance = 8f;
    [Tooltip("Absolute pivot-to-camera distance used by zoom step 3.")]
    [SerializeField, Min(0.01f)] private float thirdStepDistance = 10f;
    [Tooltip("Approximate time taken to settle onto the selected distance.")]
    [SerializeField, Min(0.01f)] private float smoothTime = 0.25f;
    [SerializeField, Min(0.01f)] private float maximumZoomSpeed = 30f;
    [Tooltip("Prevents camera zoom while the pointer is operating UI controls.")]
    [SerializeField] private bool ignoreScrollOverUi = true;

    private Vector3 localZoomDirection = Vector3.back;
    private float closestDistance;
    private float currentDistance;
    private float distanceVelocity;
    private int targetStepIndex;
    private bool initialized;

    public int ZoomStep => targetStepIndex + 1;
    public int MaximumZoomStep => ZoomStepCount;
    public float CurrentDistance => currentDistance;
    public float ClosestDistance => closestDistance;
    public float StepTwoDistance => GetDistanceForStep(1);
    public float StepThreeDistance => GetDistanceForStep(2);

    private void Awake()
    {
        InitializeFromCurrentCameraPosition();
    }

    private void Update()
    {
        if (!initialized)
        {
            InitializeFromCurrentCameraPosition();
        }

        ReadZoomInput();
        UpdateCameraDistance();
    }

    public void SetZoomStep(int step)
    {
        targetStepIndex = Mathf.Clamp(step - 1, 0, ZoomStepCount - 1);
    }

    private void InitializeFromCurrentCameraPosition()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            initialized = false;
            return;
        }

        Vector3 localPosition = targetCamera.transform.localPosition;
        closestDistance = Mathf.Max(0.01f, localPosition.magnitude);
        localZoomDirection = localPosition.sqrMagnitude > 0.000001f
            ? localPosition.normalized
            : Vector3.back;
        currentDistance = closestDistance;
        distanceVelocity = 0f;
        targetStepIndex = 0;
        initialized = true;
    }

    private void ReadZoomInput()
    {
        Mouse mouse = GameplayTestBot.PointerMouse;
        if (mouse == null
            || (ignoreScrollOverUi
                && EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject()))
        {
            return;
        }

        float scrollY = mouse.scroll.ReadValue().y;
        if (scrollY < 0f)
        {
            targetStepIndex = Mathf.Min(targetStepIndex + 1, ZoomStepCount - 1);
        }
        else if (scrollY > 0f)
        {
            targetStepIndex = Mathf.Max(targetStepIndex - 1, 0);
        }
    }

    private void UpdateCameraDistance()
    {
        if (targetCamera == null)
        {
            initialized = false;
            return;
        }

        float targetDistance = GetDistanceForStep(targetStepIndex);
        currentDistance = Mathf.SmoothDamp(
            currentDistance,
            targetDistance,
            ref distanceVelocity,
            smoothTime,
            maximumZoomSpeed,
            Time.unscaledDeltaTime);
        if (Mathf.Abs(currentDistance - targetDistance) <= ArrivalThreshold)
        {
            currentDistance = targetDistance;
            distanceVelocity = 0f;
        }

        targetCamera.transform.localPosition = localZoomDirection * currentDistance;
    }

    private float GetDistanceForStep(int zeroBasedStep)
    {
        if (zeroBasedStep <= 0)
        {
            return closestDistance;
        }

        float resolvedSecondDistance = Mathf.Max(
            closestDistance + 0.01f,
            secondStepDistance);
        return zeroBasedStep == 1
            ? resolvedSecondDistance
            : Mathf.Max(resolvedSecondDistance + 0.01f, thirdStepDistance);
    }

    private void OnValidate()
    {
        float configuredClosestDistance = targetCamera != null
            ? targetCamera.transform.localPosition.magnitude
            : 0f;
        secondStepDistance = Mathf.Max(
            configuredClosestDistance + 0.01f,
            secondStepDistance);
        thirdStepDistance = Mathf.Max(
            secondStepDistance + 0.01f,
            thirdStepDistance);
        smoothTime = Mathf.Max(0.01f, smoothTime);
        maximumZoomSpeed = Mathf.Max(0.01f, maximumZoomSpeed);
    }
}
