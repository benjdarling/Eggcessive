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
    [SerializeField, Min(0f)] private float boundsPadding = 28f;

    private float upperLimitY;
    private float lowerLimitY;
    private float nextBoundsRefreshTime;
    private bool hasScrollLimits;
    private readonly Vector3[] worldCorners = new Vector3[4];

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
        Canvas.ForceUpdateCanvases();
        RecalculateLimits(true);
    }

    public void ResetToTop()
    {
        Canvas.ForceUpdateCanvases();
        RecalculateLimits(true);
    }

    public bool Reveal(RectTransform target)
    {
        RectTransform content = scrollRect != null ? scrollRect.content : null;
        if (target == null
            || content == null
            || viewport == null
            || !target.gameObject.activeInHierarchy)
        {
            return false;
        }

        Canvas.ForceUpdateCanvases();
        RecalculateLimits(false);
        Vector3 targetWorldCenter = target.TransformPoint(target.rect.center);
        float targetViewportY = viewport.InverseTransformPoint(
            targetWorldCenter).y;
        Vector2 position = content.anchoredPosition;
        position.y = Mathf.Clamp(
            position.y + viewport.rect.center.y - targetViewportY,
            upperLimitY,
            lowerLimitY);
        scrollRect.StopMovement();
        content.anchoredPosition = position;
        Canvas.ForceUpdateCanvases();
        return true;
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
            RectTransform content = scrollRect.content;
            if (content != null)
            {
                Vector2 position = content.anchoredPosition;
                float range = Mathf.Max(1f, lowerLimitY - upperLimitY);
                position.y = Mathf.Clamp(
                    position.y - direction
                        * edgePanSpeed
                        * range
                        * Time.unscaledDeltaTime,
                    upperLimitY,
                    lowerLimitY);
                content.anchoredPosition = position;
            }
        }
    }

    private void LateUpdate()
    {
        if (scrollRect == null
            || scrollRect.content == null
            || viewport == null)
        {
            return;
        }

        if (!hasScrollLimits || Time.unscaledTime >= nextBoundsRefreshTime)
        {
            nextBoundsRefreshTime = Time.unscaledTime + 0.2f;
            RecalculateLimits(false);
        }

        Vector2 position = scrollRect.content.anchoredPosition;
        float clampedY = Mathf.Clamp(position.y, upperLimitY, lowerLimitY);
        if (!Mathf.Approximately(position.y, clampedY))
        {
            position.y = clampedY;
            scrollRect.content.anchoredPosition = position;
            scrollRect.StopMovement();
        }
    }

    private void RecalculateLimits(bool snapToTop)
    {
        RectTransform content = scrollRect != null ? scrollRect.content : null;
        if (content == null || viewport == null)
        {
            return;
        }

        bool foundBounds = false;
        float highest = float.NegativeInfinity;
        float lowest = float.PositiveInfinity;
        ProgressionNodeButton[] nodes =
            content.GetComponentsInChildren<ProgressionNodeButton>(true);
        for (int index = 0; index < nodes.Length; index++)
        {
            RectTransform rect = nodes[index] != null
                && nodes[index].gameObject.activeInHierarchy
                    ? nodes[index].transform as RectTransform
                    : null;
            IncludeRectBounds(content, rect, ref foundBounds, ref highest, ref lowest);
        }

        string[] headerNames =
        {
            "CONSUMABLES Branch",
            "FOOD Branch",
            "TECH Branch",
            "COLLECTION Branch"
        };
        for (int index = 0; index < headerNames.Length; index++)
        {
            RectTransform header = content.Find(headerNames[index])
                as RectTransform;
            IncludeRectBounds(content, header, ref foundBounds, ref highest, ref lowest);
        }

        if (!foundBounds)
        {
            upperLimitY = content.anchoredPosition.y;
            lowerLimitY = upperLimitY;
        }
        else
        {
            upperLimitY = -boundsPadding - highest;
            lowerLimitY = Mathf.Max(
                upperLimitY,
                -viewport.rect.height + boundsPadding - lowest);
        }

        hasScrollLimits = true;
        Vector2 position = content.anchoredPosition;
        position.y = snapToTop
            ? upperLimitY
            : Mathf.Clamp(position.y, upperLimitY, lowerLimitY);
        content.anchoredPosition = position;
        if (snapToTop)
        {
            scrollRect.StopMovement();
        }
    }

    private void IncludeRectBounds(
        RectTransform content,
        RectTransform rect,
        ref bool foundBounds,
        ref float highest,
        ref float lowest)
    {
        if (rect == null || !rect.gameObject.activeInHierarchy)
        {
            return;
        }

        rect.GetWorldCorners(worldCorners);
        for (int index = 0; index < worldCorners.Length; index++)
        {
            float y = content.InverseTransformPoint(worldCorners[index]).y;
            highest = Mathf.Max(highest, y);
            lowest = Mathf.Min(lowest, y);
        }
        foundBounds = true;
    }

    private void OnValidate()
    {
        edgeZonePixels = Mathf.Max(10f, edgeZonePixels);
        edgePanSpeed = Mathf.Max(0.01f, edgePanSpeed);
        boundsPadding = Mathf.Max(0f, boundsPadding);
    }
}
