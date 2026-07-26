using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ProgressionTreePanController : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private ScrollRect scrollRect = null;
    [SerializeField] private RectTransform viewport = null;
    [SerializeField] private ProgressionTreePreview treePreview = null;
    [SerializeField, Min(10f)] private float edgeZonePixels = 72f;
    [SerializeField, Min(0.01f)] private float edgePanSpeed = 0.48f;

    public void Configure(
        ScrollRect treeScrollRect,
        RectTransform treeViewport,
        ProgressionTreePreview preview)
    {
        scrollRect = treeScrollRect;
        viewport = treeViewport;
        treePreview = preview;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && !eventData.dragging)
        {
            treePreview?.Hide();
        }
    }

    private void Start()
    {
        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    private void Update()
    {
        Mouse mouse = GameplayTestBot.PointerMouse;
        if (mouse == null
            || scrollRect == null
            || viewport == null
            || !scrollRect.isActiveAndEnabled)
        {
            return;
        }

        Canvas canvas = viewport.GetComponentInParent<Canvas>();
        Camera uiCamera = canvas != null
            && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport,
                mouse.position.ReadValue(),
                uiCamera,
                out Vector2 localPoint)
            || !viewport.rect.Contains(localPoint))
        {
            return;
        }

        float direction = 0f;
        float topDistance = viewport.rect.yMax - localPoint.y;
        float bottomDistance = localPoint.y - viewport.rect.yMin;

        if (topDistance < edgeZonePixels)
        {
            direction = 1f - topDistance / edgeZonePixels;
        }
        else if (bottomDistance < edgeZonePixels)
        {
            direction = -(1f - bottomDistance / edgeZonePixels);
        }

        if (Mathf.Abs(direction) > 0.001f)
        {
            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                scrollRect.verticalNormalizedPosition
                + direction * edgePanSpeed * Time.unscaledDeltaTime);
        }
    }

    private void OnValidate()
    {
        edgeZonePixels = Mathf.Max(10f, edgeZonePixels);
        edgePanSpeed = Mathf.Max(0.01f, edgePanSpeed);
    }
}
