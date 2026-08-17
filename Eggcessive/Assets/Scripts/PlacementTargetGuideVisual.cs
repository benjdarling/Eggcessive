using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlacementTargetGuideVisual : MonoBehaviour
{
    [SerializeField] private Transform artRoot;
    [SerializeField, Min(0f)] private float bobHeight = 0.035f;
    [SerializeField, Min(0f)] private float bobSpeed = 2.4f;
    [SerializeField, Range(0f, 0.5f)] private float pulseAmount = 0.1f;
    [SerializeField, Min(0f)] private float pulseSpeed = 3.2f;

    private Vector3 artLocalPosition;
    private Vector3 artLocalScale;
    private float phaseOffset;

    public void ConfigureArtRoot(Transform value)
    {
        artRoot = value;
        CaptureAuthoredTransform();
    }

    public void SetTarget(Vector3 worldPosition, float animationOffset)
    {
        transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
        phaseOffset = animationOffset;
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }

    private void Awake()
    {
        ResolveArtRoot();
        CaptureAuthoredTransform();
    }

    private void OnEnable()
    {
        ResolveArtRoot();
        CaptureAuthoredTransform();
    }

    private void LateUpdate()
    {
        if (artRoot == null)
        {
            return;
        }

        float time = Time.unscaledTime + phaseOffset;
        float bob = Mathf.Sin(time * bobSpeed * Mathf.PI * 2f) * bobHeight;
        float pulse = 1f
            + Mathf.Sin(time * pulseSpeed * Mathf.PI * 2f) * pulseAmount;
        artRoot.localPosition = artLocalPosition + Vector3.up * bob;
        artRoot.localScale = artLocalScale * pulse;
    }

    private void OnDisable()
    {
        if (artRoot == null)
        {
            return;
        }

        artRoot.localPosition = artLocalPosition;
        artRoot.localScale = artLocalScale;
    }

    private void ResolveArtRoot()
    {
        if (artRoot == null && transform.childCount > 0)
        {
            artRoot = transform.GetChild(0);
        }
    }

    private void CaptureAuthoredTransform()
    {
        if (artRoot == null)
        {
            return;
        }

        artLocalPosition = artRoot.localPosition;
        artLocalScale = artRoot.localScale;
    }
}
