using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ProgressionTreePanController : MonoBehaviour,
    IPointerClickHandler,
    IBeginDragHandler,
    IScrollHandler
{
    [SerializeField] private ScrollRect scrollRect = null;
    [SerializeField] private RectTransform viewport = null;
    [SerializeField] private ProgressionTreePreview treePreview = null;
    [SerializeField, Min(0f)] private float boundsPadding = 56f;
    [SerializeField, Min(0f)] private float keyboardPanPixelsPerSecond = 620f;
    [SerializeField, Min(0f)] private float homeTransitionDuration = 0.28f;

    private Vector2 minimumPosition;
    private Vector2 maximumPosition;
    private Vector2 initialFocus;
    private Vector2 homeTransitionStart;
    private Vector2 homeTransitionTarget;
    private float homeTransitionElapsed;
    private bool isReturningHome;

    public void Configure(
        ScrollRect treeScrollRect,
        RectTransform treeViewport,
        ProgressionTreePreview preview)
    {
        scrollRect = treeScrollRect;
        viewport = treeViewport;
        treePreview = preview;
    }

    public void SetInitialFocus(Vector2 contentLocalPosition)
    {
        initialFocus = contentLocalPosition;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null
            && !eventData.dragging
            && !IsOverPreview(eventData.position))
        {
            treePreview?.Hide();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        isReturningHome = false;
        if (eventData == null || !IsOverPreview(eventData.position))
        {
            treePreview?.Hide();
        }
    }

    public void OnScroll(PointerEventData eventData)
    {
        isReturningHome = false;
        if (eventData == null || !IsOverPreview(eventData.position))
        {
            treePreview?.Hide();
        }
    }

    private void Start()
    {
        ResetToTop();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null
            || scrollRect == null
            || scrollRect.content == null
            || viewport == null
            || !scrollRect.isActiveAndEnabled)
        {
            return;
        }

        if (keyboard.hKey.wasPressedThisFrame)
        {
            BeginReturnHome();
            return;
        }

        Vector2 movement = Vector2.zero;
        if (keyboard.aKey.isPressed)
        {
            movement.x += 1f;
        }

        if (keyboard.dKey.isPressed)
        {
            movement.x -= 1f;
        }

        if (keyboard.wKey.isPressed)
        {
            movement.y -= 1f;
        }

        if (keyboard.sKey.isPressed)
        {
            movement.y += 1f;
        }

        if (movement.sqrMagnitude <= 0.001f)
        {
            UpdateReturnHome();
            return;
        }

        isReturningHome = false;
        treePreview?.Hide();
        RecalculateLimits();
        scrollRect.StopMovement();
        scrollRect.content.anchoredPosition = Clamp(
            scrollRect.content.anchoredPosition
            + movement.normalized
            * keyboardPanPixelsPerSecond
            * Time.unscaledDeltaTime);
    }

    public void ResetToTop()
    {
        isReturningHome = false;
        Canvas.ForceUpdateCanvases();
        RecalculateLimits();
        FocusOn(initialFocus);
    }

    public bool Reveal(RectTransform target)
    {
        if (target == null
            || scrollRect == null
            || scrollRect.content == null
            || !target.gameObject.activeInHierarchy)
        {
            return false;
        }

        Vector3 worldCenter = target.TransformPoint(target.rect.center);
        Vector2 localCenter = scrollRect.content.InverseTransformPoint(worldCenter);
        FocusOn(localCenter);
        return true;
    }

    private void FocusOn(Vector2 contentLocalPosition)
    {
        if (scrollRect == null || scrollRect.content == null)
        {
            return;
        }

        RecalculateLimits();
        scrollRect.StopMovement();
        scrollRect.content.anchoredPosition = Clamp(-contentLocalPosition);
    }

    private void BeginReturnHome()
    {
        Canvas.ForceUpdateCanvases();
        RecalculateLimits();
        scrollRect.StopMovement();
        treePreview?.Hide();
        homeTransitionStart = scrollRect.content.anchoredPosition;
        homeTransitionTarget = Clamp(-initialFocus);
        homeTransitionElapsed = 0f;
        isReturningHome = true;

        if (homeTransitionDuration <= 0f)
        {
            scrollRect.content.anchoredPosition = homeTransitionTarget;
            isReturningHome = false;
        }
    }

    private void UpdateReturnHome()
    {
        if (!isReturningHome)
        {
            return;
        }

        homeTransitionElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(
            homeTransitionElapsed / Mathf.Max(0.0001f, homeTransitionDuration));
        float easedProgress = progress * progress * (3f - 2f * progress);
        scrollRect.content.anchoredPosition = Vector2.LerpUnclamped(
            homeTransitionStart,
            homeTransitionTarget,
            easedProgress);
        if (progress >= 1f)
        {
            isReturningHome = false;
        }
    }

    private void LateUpdate()
    {
        if (scrollRect == null || scrollRect.content == null || viewport == null)
        {
            return;
        }

        RecalculateLimits();
        Vector2 position = scrollRect.content.anchoredPosition;
        Vector2 clamped = Clamp(position);
        if ((position - clamped).sqrMagnitude > 0.01f)
        {
            scrollRect.content.anchoredPosition = clamped;
            scrollRect.StopMovement();
        }
    }

    private void RecalculateLimits()
    {
        RectTransform content = scrollRect != null ? scrollRect.content : null;
        if (content == null || viewport == null)
        {
            return;
        }

        float horizontal = Mathf.Max(
            0f,
            (content.rect.width - viewport.rect.width) * 0.5f + boundsPadding);
        float vertical = Mathf.Max(
            0f,
            (content.rect.height - viewport.rect.height) * 0.5f + boundsPadding);
        minimumPosition = new Vector2(-horizontal, -vertical);
        maximumPosition = new Vector2(horizontal, vertical);
    }

    private Vector2 Clamp(Vector2 position)
    {
        return new Vector2(
            Mathf.Clamp(position.x, minimumPosition.x, maximumPosition.x),
            Mathf.Clamp(position.y, minimumPosition.y, maximumPosition.y));
    }

    private bool IsOverPreview(Vector2 screenPosition)
    {
        return treePreview != null
            && treePreview.ContainsScreenPoint(screenPosition);
    }

    private void OnValidate()
    {
        boundsPadding = Mathf.Max(0f, boundsPadding);
        keyboardPanPixelsPerSecond = Mathf.Max(0f, keyboardPanPixelsPerSecond);
        homeTransitionDuration = Mathf.Max(0f, homeTransitionDuration);
    }
}
