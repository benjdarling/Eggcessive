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
    private Sequence countSequence;

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
    }

    private void OnDestroy()
    {
        countSequence?.Kill();

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
        instance.AnimateToTarget();
    }

    private void AnimateToTarget()
    {
        countSequence?.Kill();
        scoreText.rectTransform.localScale = Vector3.one;

        int centsRemaining = targetCents - displayedCents;
        float totalDuration = Mathf.Clamp(
            centsRemaining * secondsPerCent,
            minimumCountDuration,
            maximumCountDuration);
        float tickDuration = totalDuration / centsRemaining;
        float tickPunchDuration = Mathf.Min(punchDuration, tickDuration);

        countSequence = DOTween.Sequence()
            .SetTarget(this);

        for (int cents = displayedCents + 1; cents <= targetCents; cents++)
        {
            int tickValue = cents;
            countSequence
                .AppendCallback(() =>
                {
                    displayedCents = tickValue;
                    RefreshScore();
                })
                .Append(scoreText.rectTransform.DOPunchScale(
                    Vector3.one * punchStrength,
                    tickPunchDuration,
                    punchVibrato,
                    punchElasticity));

            float remainingTickTime = tickDuration - tickPunchDuration;

            if (remainingTickTime > 0f)
            {
                countSequence.AppendInterval(remainingTickTime);
            }
        }

        countSequence.OnComplete(() =>
        {
            scoreText.rectTransform.localScale = Vector3.one;
            countSequence = null;
        });
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
