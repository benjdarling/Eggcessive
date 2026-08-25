using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PenButtonView : MonoBehaviour
{
    private static readonly Color PenTextColour = Color.white;
    private static readonly Color RateTextColour =
        new Color(1f, 0.89f, 0.46f, 1f);
    private static readonly Color DefaultOutlineColour =
        new Color(0.12f, 0.07f, 0.035f, 1f);
    private static readonly Color AvailableOutlineColour =
        new Color(1f, 0.72f, 0.14f, 1f);
    private static readonly Color FocusedOutlineColour =
        new Color(1f, 0.92f, 0.42f, 1f);
    private const float FocusedScale = 1.15f;
    private static readonly Color UnavailablePenTextColour =
        new Color(0.56f, 0.56f, 0.54f, 1f);
    private static readonly Color UnavailableCostTextColour =
        new Color(0.48f, 0.42f, 0.27f, 1f);

    [SerializeField] private Button button = null;
    [SerializeField] private Image background = null;
    [SerializeField] private TMP_Text penLabel = null;
    [SerializeField] private TMP_Text purchaseLabel = null;
    [SerializeField] private GameObject progressRoot = null;
    [SerializeField] private Image progressFill = null;
    [SerializeField] private TMP_Text earningsText = null;
    [SerializeField] private CanvasGroup earningsCanvasGroup = null;
    [SerializeField] private GameObject purchaseLockRoot = null;

    private int penIndex;
    private bool purchaseButton;
    private PenHudController owner;
    private Coroutine earningsAnimation;
    private long accumulatedEarningsCents;
    private Outline buttonOutline;
    private bool purchaseAffordable;
    private Color purchaseBaseColour;
    private Vector3 baseLocalScale = Vector3.one;

    public int PenIndex => penIndex;
    public Button Button => button;

    private void Awake()
    {
        baseLocalScale = transform.localScale;
        if (earningsText != null)
        {
            earningsText.enableAutoSizing = false;
            earningsText.fontSize *= 1.2f;
        }
    }

    private void Update()
    {
        if (!purchaseButton || !purchaseAffordable || background == null)
        {
            return;
        }

        float pulse = 0.5f + Mathf.Sin(Time.unscaledTime * 3.5f) * 0.5f;
        background.color = Color.Lerp(
            purchaseBaseColour,
            Color.white,
            Mathf.Lerp(0.035f, 0.12f, pulse));
        SetOutlineColour(Color.Lerp(
            AvailableOutlineColour,
            Color.white,
            pulse * 0.28f));
    }

    public void ConfigureEditorPreview(
        int index,
        bool focused,
        float eggsPerMinute)
    {
        penIndex = index;
        purchaseButton = false;
        purchaseAffordable = false;
        RefreshOwned(focused, eggsPerMinute);
        if (earningsCanvasGroup != null)
        {
            earningsCanvasGroup.alpha = 0f;
        }

        gameObject.SetActive(true);
    }

    public void InitializePen(PenHudController controller, int index)
    {
        owner = controller;
        penIndex = index;
        purchaseButton = false;
        purchaseAffordable = false;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(HandleClicked);
        if (earningsCanvasGroup != null)
        {
            earningsCanvasGroup.alpha = 0f;
        }
        gameObject.SetActive(true);
    }

    public void InitializePurchase(PenHudController controller, int index)
    {
        owner = controller;
        penIndex = index;
        purchaseButton = true;
        purchaseAffordable = false;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(HandleClicked);
        if (earningsCanvasGroup != null)
        {
            earningsCanvasGroup.alpha = 0f;
        }

        gameObject.SetActive(true);
    }

    public void RefreshOwned(bool focused, float eggsPerMinute)
    {
        penLabel.text = $"PEN {penIndex + 1}";
        penLabel.color = PenTextColour;
        purchaseLabel.gameObject.SetActive(true);
        purchaseLabel.text = $"{Mathf.Max(0f, eggsPerMinute):0}\nE/MIN";
        purchaseLabel.color = RateTextColour;
        progressRoot.SetActive(false);
        button.interactable = true;
        purchaseAffordable = false;
        transform.localScale = focused
            ? baseLocalScale * FocusedScale
            : baseLocalScale;
        SetOutlineStyle(
            focused ? FocusedOutlineColour : DefaultOutlineColour,
            focused ? 4f : 2f);
        Color penColour = PenUiPalette.GetColour(penIndex);
        background.color = focused
            ? Color.Lerp(penColour, Color.white, 0.16f)
            : new Color(
                penColour.r * 0.82f,
                penColour.g * 0.82f,
                penColour.b * 0.82f,
                1f);
    }

    public void RefreshPurchase(
        int nextPenIndex,
        bool progressionUnlocked,
        bool affordable,
        int costCents,
        long currentCents)
    {
        penIndex = nextPenIndex;
        transform.localScale = baseLocalScale;
        penLabel.text = $"BUY\nPEN {nextPenIndex + 1}";
        penLabel.color = affordable
            ? PenTextColour
            : UnavailablePenTextColour;
        purchaseLabel.gameObject.SetActive(true);
        purchaseLabel.color = affordable
            ? RateTextColour
            : UnavailableCostTextColour;
        progressRoot.SetActive(progressionUnlocked);
        button.interactable = affordable;
        purchaseAffordable = affordable;
        purchaseLabel.text = progressionUnlocked
            ? FormatCompactMoney(costCents)
            : $"ROUND {PenExpansionManager.AdditionalPenUnlockRound}\nMILESTONE";
        if (purchaseLockRoot != null)
        {
            purchaseLockRoot.SetActive(!progressionUnlocked);
            purchaseLockRoot.transform.SetAsLastSibling();
        }
        Color penColour = PenUiPalette.GetColour(nextPenIndex);
        float brightness = affordable ? 0.95f : 0.16f;
        purchaseBaseColour = new Color(
            penColour.r * brightness,
            penColour.g * brightness,
            penColour.b * brightness,
            1f);
        background.color = purchaseBaseColour;
        SetOutlineColour(
            affordable ? AvailableOutlineColour : DefaultOutlineColour * 0.55f);
        SetProgressFill(
            progressionUnlocked && costCents > 0
                ? currentCents / (float)costCents
                : 0f);
    }

    public void ShowEarnings(int cents)
    {
        if (purchaseButton || cents <= 0)
        {
            return;
        }

        if (earningsText == null || earningsCanvasGroup == null)
        {
            return;
        }

        accumulatedEarningsCents = cents > long.MaxValue - accumulatedEarningsCents
            ? long.MaxValue
            : accumulatedEarningsCents + cents;
        earningsText.text = $"+{FormatCompactMoney(accumulatedEarningsCents)}";

        if (earningsAnimation != null)
        {
            StopCoroutine(earningsAnimation);
        }

        earningsAnimation = StartCoroutine(AnimateEarnings());
    }

    private void HandleClicked()
    {
        if (purchaseButton)
        {
            owner?.PurchaseNextPen();
        }
        else
        {
            owner?.ActivatePen(penIndex);
        }
    }

    private IEnumerator AnimateEarnings()
    {
        const float duration = 1.05f;
        const float fadeStart = 0.28f;
        const float riseDistance = 24f;
        Vector2 startPosition = new Vector2(0f, 8f);
        RectTransform rect = earningsText.rectTransform;
        rect.anchoredPosition = startPosition;
        earningsCanvasGroup.alpha = 1f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            rect.anchoredPosition = startPosition
                + Vector2.up * Mathf.SmoothStep(0f, riseDistance, progress);
            earningsCanvasGroup.alpha = progress <= fadeStart
                ? 1f
                : 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(fadeStart, 1f, progress));
            yield return null;
        }

        earningsCanvasGroup.alpha = 0f;
        accumulatedEarningsCents = 0;
        earningsAnimation = null;
    }

    private static string FormatCompactMoney(long cents)
    {
        double dollars = Math.Max(0d, cents / 100d);
        string[] suffixes = { string.Empty, "k", "m", "b", "t" };
        int suffixIndex = 0;
        while (dollars >= 999.5d && suffixIndex < suffixes.Length - 1)
        {
            dollars /= 1000d;
            suffixIndex++;
        }

        string number = dollars < 10d && suffixIndex > 0
            ? dollars.ToString("0.0")
            : dollars < 10d && dollars % 1d >= 0.05d
                ? dollars.ToString("0.0")
                : dollars.ToString("0");
        return $"${number}{suffixes[suffixIndex]}";
    }

    private void SetProgressFill(float amount)
    {
        if (progressFill == null)
        {
            return;
        }

        progressFill.type = Image.Type.Sliced;
        RectTransform rect = progressFill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void SetOutlineColour(Color colour)
    {
        SetOutlineStyle(colour, 2f);
    }

    private void SetOutlineStyle(Color colour, float thickness)
    {
        if (buttonOutline == null)
        {
            buttonOutline = GetComponent<Outline>();
        }

        if (buttonOutline != null)
        {
            buttonOutline.effectColor = colour;
            buttonOutline.effectDistance = new Vector2(
                Mathf.Max(1f, thickness),
                -Mathf.Max(1f, thickness));
        }
    }
}

public static class PenUiPalette
{
    private static readonly Color[] Colours =
    {
        new Color(0.03f, 0.32f, 0.90f, 1f), // Royal blue
        new Color(0.02f, 0.42f, 0.12f, 1f), // Emerald green
        new Color(0.72f, 0.16f, 0.02f, 1f), // Burnt orange
        new Color(0.75f, 0.02f, 0.40f, 1f), // Hot magenta
        new Color(0.00f, 0.40f, 0.46f, 1f), // Teal
        new Color(0.78f, 0.03f, 0.05f, 1f), // Vivid red
        new Color(0.46f, 0.08f, 0.78f, 1f), // Violet
        new Color(0.18f, 0.10f, 0.64f, 1f)  // Deep indigo
    };

    public static int Count => Colours.Length;

    public static Color GetColour(int penIndex)
    {
        return Colours[Mathf.Abs(penIndex) % Colours.Length];
    }
}
