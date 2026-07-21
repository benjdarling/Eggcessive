using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

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

    private int displayedCents;
    private int targetCents;
    private Tweener countTween;
    private Tweener punchTween;

    public static event Action<int> BalanceChanged;
    public static int CurrentCents => instance != null ? instance.targetCents : 0;

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

    private void OnDestroy()
    {
        countTween?.Kill();
        punchTween?.Kill();

        if (instance == this)
        {
            instance = null;
        }
    }

    public static void AddCents(int amount)
    {
        if (instance == null)
        {
            Debug.LogWarning("No EggScoreHud is present to receive egg score.");
            return;
        }

        int centsToAdd = Mathf.Max(0, amount);

        if (centsToAdd == 0)
        {
            return;
        }

        instance.targetCents += centsToAdd;
        BalanceChanged?.Invoke(instance.targetCents);
        instance.AnimateToTarget();
    }

    public static bool TrySpendCents(int amount)
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

        int centsDifference = targetCents - displayedCents;

        if (centsDifference == 0)
        {
            RefreshScore();
            return;
        }

        int centsRemaining = Mathf.Abs(centsDifference);
        float totalDuration = Mathf.Clamp(
            centsRemaining * secondsPerCent,
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
            scoreText.text = $"${displayedCents / 100}.{displayedCents % 100:D2}";
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
