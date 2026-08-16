using System;
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
