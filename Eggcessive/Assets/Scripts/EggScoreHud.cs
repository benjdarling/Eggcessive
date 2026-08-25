using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed class EggScoreHud : MonoBehaviour
{
    private static EggScoreHud instance;

    [Header("References")]
    [SerializeField] private TMP_Text scoreText = null;

    [Header("Count Animation")]
    [SerializeField, Min(0.001f)] private float secondsPerCent = 0.012f;
    [SerializeField, Min(0f)] private float minimumCountDuration = 0.08f;
    [SerializeField, Min(0f)] private float maximumCountDuration = 0.7f;

    [Header("Punch Animation")]
    [SerializeField, Min(0.001f)] private float punchDuration = 0.012f;
    [SerializeField, Min(0f)] private float punchStrength = 0.2f;
    [SerializeField, Min(1)] private int punchVibrato = 3;
    [SerializeField, Range(0f, 1f)] private float punchElasticity = 0.72f;

    private long displayedCents;
    private long targetCents;
    private Tweener countTween;
    private Tweener punchTween;
    private Sequence entranceSequence;

    public static event Action<long> BalanceChanged;
    public static long CurrentCents => instance != null ? instance.targetCents : 0L;
    public RectTransform CashTarget => scoreText != null
        ? scoreText.rectTransform
        : null;

    private void Awake()
    {
        if (scoreText == null)
        {
            Debug.LogError($"{nameof(EggScoreHud)} on {name} needs a score text reference.", this);
            enabled = false;
            return;
        }

        instance = this;
        displayedCents = 0;
        targetCents = 0;
        RefreshScore();
        CreatePunchTween();
    }

    private void Start()
    {
        // Install after every HUD controller has completed Awake so dynamically
        // configured tool, turbo, pen, and equipment buttons are included.
        HudButtonSpring.InstallUnder(transform);
    }

    private void OnDestroy()
    {
        countTween?.Kill();
        punchTween?.Kill();
        entranceSequence?.Kill();

        if (instance == this)
        {
            instance = null;
        }
    }

    public static void AddCents(long amount)
    {
        if (instance == null)
        {
            Debug.LogWarning("No EggScoreHud is present to receive egg score.");
            return;
        }

        long centsToAdd = Math.Max(0L, amount);

        if (centsToAdd == 0)
        {
            return;
        }

        instance.targetCents = centsToAdd > long.MaxValue - instance.targetCents
            ? long.MaxValue
            : instance.targetCents + centsToAdd;
        BalanceChanged?.Invoke(instance.targetCents);
        instance.AnimateToTarget();
    }

    public static bool TrySpendCents(long amount)
    {
        if (instance == null || amount <= 0 || instance.targetCents < amount)
        {
            return false;
        }

        instance.targetCents -= amount;
        BalanceChanged?.Invoke(instance.targetCents);
        instance.AnimateToTarget();
        return true;
    }

    public static void PlayRoundEntrance()
    {
        instance?.PlayEntrance();
    }

    public static bool PlayRoundOutro(Action onComplete)
    {
        return instance != null && instance.PlayOutro(onComplete);
    }

    private void PlayEntrance()
    {
        entranceSequence?.Kill();
        entranceSequence = HudEntranceAnimation.CreateForDirectChildren(
            transform,
            this);
    }

    private bool PlayOutro(Action onComplete)
    {
        entranceSequence?.Kill();
        entranceSequence = HudEntranceAnimation.CreateOutroForDirectChildren(
            transform,
            this,
            onComplete);
        return entranceSequence != null;
    }

    private void AnimateToTarget()
    {
        countTween?.Kill();
        countTween = null;
        punchTween?.Rewind();
        scoreText.rectTransform.localScale = Vector3.one;

        long centsDifference = targetCents - displayedCents;

        if (centsDifference == 0)
        {
            RefreshScore();
            return;
        }

        long centsRemaining = Math.Abs(centsDifference);
        float totalDuration = Mathf.Clamp(
            (float)Math.Min(
                maximumCountDuration,
                centsRemaining * (double)secondsPerCent),
            minimumCountDuration,
            maximumCountDuration);

        countTween = DOTween.To(
                () => displayedCents,
                value =>
                {
                    if (displayedCents == value)
                    {
                        return;
                    }

                    displayedCents = value;
                    RefreshScore();
                    RestartPunchTween();
                },
                targetCents,
                totalDuration)
            .SetEase(Ease.Linear)
            .SetTarget(this)
            .OnComplete(() => countTween = null);
    }

    private void CreatePunchTween()
    {
        punchTween = scoreText.rectTransform.DOPunchScale(
                Vector3.one * punchStrength,
                punchDuration,
                punchVibrato,
                punchElasticity)
            .SetAutoKill(false)
            .SetTarget(this)
            .Pause();
    }

    private void RestartPunchTween()
    {
        if (punchTween == null || !punchTween.IsActive())
        {
            CreatePunchTween();
        }

        scoreText.rectTransform.localScale = Vector3.one;
        punchTween.Restart();
    }

    private void RefreshScore()
    {
        if (scoreText != null)
        {
            scoreText.text =
                $"${displayedCents / 100:N0}.{Math.Abs(displayedCents % 100):D2}";
        }
    }

    private void OnValidate()
    {
        secondsPerCent = Mathf.Max(0.001f, secondsPerCent);
        minimumCountDuration = Mathf.Max(0f, minimumCountDuration);
        maximumCountDuration = Mathf.Max(minimumCountDuration, maximumCountDuration);
        punchDuration = Mathf.Max(0.001f, punchDuration);
        punchStrength = Mathf.Max(0f, punchStrength);
        punchVibrato = Mathf.Max(1, punchVibrato);
        punchElasticity = Mathf.Clamp01(punchElasticity);
    }
}

internal static class HudEntranceAnimation
{
    private const float Duration = 0.42f;
    private const float Stagger = 0.035f;
    private const float EdgePadding = 40f;
    private const float Overshoot = 1.3f;

    public static Sequence CreateForDirectChildren(
        Transform root,
        UnityEngine.Object tweenTarget)
    {
        if (root == null)
        {
            return null;
        }

        List<RectTransform> elements = new List<RectTransform>();
        for (int index = 0; index < root.childCount; index++)
        {
            RectTransform rect = root.GetChild(index) as RectTransform;
            if (IsEntranceElement(rect))
            {
                elements.Add(rect);
            }
        }

        return Create(elements, tweenTarget);
    }

    public static Sequence CreateOutroForDirectChildren(
        Transform root,
        UnityEngine.Object tweenTarget,
        Action onComplete)
    {
        if (root == null)
        {
            return null;
        }

        List<RectTransform> elements = new List<RectTransform>();
        for (int index = 0; index < root.childCount; index++)
        {
            RectTransform rect = root.GetChild(index) as RectTransform;
            if (IsEntranceElement(rect))
            {
                elements.Add(rect);
            }
        }

        return CreateOutro(elements, tweenTarget, onComplete);
    }

    public static Sequence Create(
        IReadOnlyList<RectTransform> elements,
        UnityEngine.Object tweenTarget)
    {
        if (elements == null || elements.Count == 0)
        {
            return null;
        }

        List<RectTransform> activeElements = new List<RectTransform>(elements.Count);
        for (int index = 0; index < elements.Count; index++)
        {
            RectTransform rect = elements[index];
            if (IsEntranceElement(rect))
            {
                activeElements.Add(rect);
            }
        }

        activeElements.Sort(CompareEntranceOrder);
        if (activeElements.Count == 0)
        {
            return null;
        }

        List<Vector2> restingPositions = new List<Vector2>(activeElements.Count);
        Sequence sequence = DOTween.Sequence()
            .SetUpdate(true)
            .SetTarget(tweenTarget);
        for (int index = 0; index < activeElements.Count; index++)
        {
            RectTransform rect = activeElements[index];
            Vector2 restingPosition = rect.anchoredPosition;
            restingPositions.Add(restingPosition);
            rect.anchoredPosition = restingPosition + GetOffscreenOffset(rect);
            sequence.Insert(
                index * Stagger,
                rect.DOAnchorPos(restingPosition, Duration)
                    .SetEase(Ease.OutBack, Overshoot));
        }

        sequence.OnKill(() => RestorePositions(activeElements, restingPositions));

        return sequence;
    }

    public static Sequence CreateOutro(
        IReadOnlyList<RectTransform> elements,
        UnityEngine.Object tweenTarget,
        Action onComplete)
    {
        if (elements == null || elements.Count == 0)
        {
            return null;
        }

        List<RectTransform> activeElements = new List<RectTransform>(elements.Count);
        for (int index = 0; index < elements.Count; index++)
        {
            RectTransform rect = elements[index];
            if (IsEntranceElement(rect))
            {
                activeElements.Add(rect);
            }
        }

        activeElements.Sort(CompareEntranceOrder);
        if (activeElements.Count == 0)
        {
            return null;
        }

        List<Vector2> restingPositions = new List<Vector2>(activeElements.Count);
        Sequence sequence = DOTween.Sequence()
            .SetUpdate(true)
            .SetTarget(tweenTarget);
        for (int index = 0; index < activeElements.Count; index++)
        {
            RectTransform rect = activeElements[index];
            Vector2 restingPosition = rect.anchoredPosition;
            restingPositions.Add(restingPosition);
            sequence.Insert(
                index * Stagger,
                rect.DOAnchorPos(
                        restingPosition + GetOffscreenOffset(rect),
                        Duration)
                    .SetEase(Ease.InBack, 1.15f));
        }

        sequence.OnComplete(() => onComplete?.Invoke());
        sequence.OnKill(() => RestorePositions(activeElements, restingPositions));

        return sequence;
    }

    private static void RestorePositions(
        IReadOnlyList<RectTransform> elements,
        IReadOnlyList<Vector2> restingPositions)
    {
        int count = Mathf.Min(elements.Count, restingPositions.Count);
        for (int index = 0; index < count; index++)
        {
            if (elements[index] != null)
            {
                elements[index].anchoredPosition = restingPositions[index];
            }
        }
    }

    private static bool IsEntranceElement(RectTransform rect)
    {
        if (rect == null || !rect.gameObject.activeInHierarchy)
        {
            return false;
        }

        // Full-screen layers are modal backdrops/effect canvases rather than
        // HUD cards. Moving them would expose the scene around their edges.
        bool stretchesAcrossCanvas = rect.anchorMin == Vector2.zero
            && rect.anchorMax == Vector2.one;
        return !stretchesAcrossCanvas;
    }

    private static int CompareEntranceOrder(RectTransform left, RectTransform right)
    {
        int leftEdge = GetEntranceEdge(left);
        int rightEdge = GetEntranceEdge(right);
        int edgeComparison = leftEdge.CompareTo(rightEdge);
        if (edgeComparison != 0)
        {
            return edgeComparison;
        }

        return left.position.x.CompareTo(right.position.x);
    }

    private static Vector2 GetOffscreenOffset(RectTransform rect)
    {
        Vector2 position = rect.anchoredPosition;
        Rect bounds = rect.rect;
        switch (GetEntranceEdge(rect))
        {
            case 0: // Left-side panels.
                return Vector2.left
                    * (bounds.width + Mathf.Abs(position.x) + EdgePadding);
            case 1: // Top HUD cards.
                return Vector2.up
                    * (bounds.height + Mathf.Abs(position.y) + EdgePadding);
            case 2: // Right-side panels.
                return Vector2.right
                    * (bounds.width + Mathf.Abs(position.x) + EdgePadding);
            default: // Tool palettes and navigation along the bottom.
                return Vector2.down
                    * (bounds.height + Mathf.Abs(position.y) + EdgePadding);
        }
    }

    private static int GetEntranceEdge(RectTransform rect)
    {
        Vector2 anchor = (rect.anchorMin + rect.anchorMax) * 0.5f;
        if (anchor.y <= 0.25f)
        {
            return 3;
        }
        if (anchor.x <= 0.25f)
        {
            return 0;
        }
        if (anchor.x >= 0.75f && anchor.y < 0.75f)
        {
            return 2;
        }

        return 1;
    }
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
internal sealed class HudButtonSpring : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    private const float HoverScale = 1.08f;
    private const float PressedScale = 0.88f;
    private const float HoverTilt = 1.9f;

    private Button button;
    private Vector3 restingScale = Vector3.one;
    private Quaternion restingRotation = Quaternion.identity;
    private Vector3 lastAppliedScale = Vector3.one;
    private Quaternion lastAppliedRotation = Quaternion.identity;
    private SpringUtils.FloatSpring scaleSpring =
        new SpringUtils.FloatSpring(1f);
    private SpringUtils.AngleSpring rotationSpring =
        new SpringUtils.AngleSpring(0f);
    private bool initialized;
    private bool hasAppliedTransform;
    private bool isHovered;
    private bool isPointerDown;
    private float tiltDirection = 1f;

    public static void InstallUnder(Transform root)
    {
        if (root == null)
        {
            return;
        }

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int index = 0; index < buttons.Length; index++)
        {
            Button candidate = buttons[index];
            if (candidate == null
                || candidate.GetComponent<HudButtonSpring>() != null
                || candidate.GetComponent<ProgressionNodeButton>() != null
                || candidate.GetComponent<SpringMenuButton>() != null)
            {
                continue;
            }

            candidate.gameObject.AddComponent<HudButtonSpring>();
        }
    }

    private void Awake()
    {
        button = GetComponent<Button>();
        CaptureRestingTransform();
    }

    private void OnEnable()
    {
        if (!initialized)
        {
            button = GetComponent<Button>();
            CaptureRestingTransform();
        }
        else
        {
            restingScale = transform.localScale;
            restingRotation = transform.localRotation;
        }

        isHovered = false;
        isPointerDown = false;
        scaleSpring.Reset(1f);
        rotationSpring.Reset(0f);
        hasAppliedTransform = false;
    }

    private void OnDisable()
    {
        isHovered = false;
        isPointerDown = false;
        scaleSpring.Reset(1f);
        rotationSpring.Reset(0f);
        if (initialized)
        {
            transform.localScale = restingScale;
            transform.localRotation = restingRotation;
        }
        hasAppliedTransform = false;
    }

    private void LateUpdate()
    {
        ObserveExternalTransformChanges();

        bool interactable = button != null && button.IsInteractable();
        if (!interactable)
        {
            isHovered = false;
            isPointerDown = false;
        }

        float targetScale = isPointerDown
            ? PressedScale
            : isHovered
                ? HoverScale
                : 1f;
        float targetRotation = isPointerDown
            ? -tiltDirection * 1.1f
            : isHovered
                ? tiltDirection * HoverTilt
                : 0f;
        float frequency = isPointerDown ? 18f : 8f;
        float damping = isPointerDown ? 0.8f : 0.52f;
        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        SpringUtils.MotionParams motion = SpringUtils.CalculateMotionParams(
            deltaTime,
            frequency,
            damping);
        scaleSpring.Update(targetScale, 0f, deltaTime, motion);
        rotationSpring.Update(targetRotation, 0f, deltaTime, motion);
        scaleSpring.ClampValue(0.84f, 1.15f);
        rotationSpring.ClampValue(-4.5f, 4.5f);

        lastAppliedScale = Vector3.Scale(
            restingScale,
            Vector3.one * scaleSpring.Value);
        lastAppliedRotation = restingRotation
            * Quaternion.Euler(0f, 0f, rotationSpring.Value);
        transform.localScale = lastAppliedScale;
        transform.localRotation = lastAppliedRotation;
        hasAppliedTransform = true;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (button == null || !button.IsInteractable())
        {
            return;
        }

        isHovered = true;
        scaleSpring.AddImpulse(0.55f);
        rotationSpring.AddImpulse(tiltDirection * 29f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        isPointerDown = false;
        scaleSpring.AddImpulse(-0.18f);
        rotationSpring.AddImpulse(-tiltDirection * 12f);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (button == null
            || !button.IsInteractable()
            || eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        isPointerDown = true;
        scaleSpring.AddImpulse(-1.15f);
        rotationSpring.AddImpulse(-tiltDirection * 34f);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isPointerDown)
        {
            return;
        }

        isPointerDown = false;
        scaleSpring.AddImpulse(0.72f);
        rotationSpring.AddImpulse(tiltDirection * 24f);
    }

    private void CaptureRestingTransform()
    {
        restingScale = transform.localScale;
        restingRotation = transform.localRotation;
        tiltDirection = (GetInstanceID() & 1) == 0 ? 1f : -1f;
        initialized = true;
        hasAppliedTransform = false;
    }

    private void ObserveExternalTransformChanges()
    {
        if (!initialized)
        {
            CaptureRestingTransform();
            return;
        }

        if (!hasAppliedTransform)
        {
            restingScale = transform.localScale;
            restingRotation = transform.localRotation;
            return;
        }

        // Pen navigation and other HUD systems can change their own selected
        // scale. Treat an overwrite since our previous LateUpdate as the new
        // resting pose, then layer the interaction spring on top of it.
        if ((transform.localScale - lastAppliedScale).sqrMagnitude > 0.000001f)
        {
            restingScale = transform.localScale;
        }
        if (Quaternion.Angle(transform.localRotation, lastAppliedRotation) > 0.01f)
        {
            restingRotation = transform.localRotation;
        }
    }
}
