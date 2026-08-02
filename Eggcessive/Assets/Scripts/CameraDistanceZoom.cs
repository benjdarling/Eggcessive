using UnityEngine;

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class CameraDistanceZoom : MonoBehaviour
{
    [SerializeField] private Camera targetCamera = null;
    [Tooltip("Fixed pivot-to-camera distance while zoom controls are disabled.")]
    [SerializeField, Min(0.01f)] private float fixedDistance = 7f;

    private Vector3 localZoomDirection = Vector3.back;
    private bool initialized;

    public int ZoomStep => 1;
    public int MaximumZoomStep => 1;
    public float CurrentDistance => fixedDistance;
    public float ClosestDistance => fixedDistance;
    public float StepTwoDistance => fixedDistance;
    public float StepThreeDistance => fixedDistance;

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

        ApplyFixedDistance();
    }

    public void SetZoomStep(int step)
    {
        ApplyFixedDistance();
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
        localZoomDirection = localPosition.sqrMagnitude > 0.000001f
            ? localPosition.normalized
            : Vector3.back;
        initialized = true;
        ApplyFixedDistance();
    }

    private void ApplyFixedDistance()
    {
        if (targetCamera == null)
        {
            initialized = false;
            return;
        }

        targetCamera.transform.localPosition =
            localZoomDirection * fixedDistance;
    }

    private void OnValidate()
    {
        fixedDistance = 7f;
    }
}
