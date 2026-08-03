using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PenButtonView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text penLabel;
    [SerializeField] private TMP_Text purchaseLabel;
    [SerializeField] private GameObject progressRoot;
    [SerializeField] private Image progressFill;

    private int penIndex;
    private bool purchaseButton;
    private PenHudController owner;
    private TMP_Text earningsText;
    private CanvasGroup earningsCanvasGroup;
    private Coroutine earningsAnimation;
    private long accumulatedEarningsCents;

    private static readonly Color EarningsColor =
        new Color(1f, 0.84f, 0.16f, 1f);

    public int PenIndex => penIndex;

    public void InitializePen(PenHudController controller, int index)
    {
        owner = controller;
        penIndex = index;
        purchaseButton = false;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(HandleClicked);
        gameObject.SetActive(true);
    }

    public void InitializePurchase(PenHudController controller, int index)
    {
        owner = controller;
        penIndex = index;
        purchaseButton = true;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(HandleClicked);
        gameObject.SetActive(true);
    }

    public void RefreshOwned(bool focused)
    {
        penLabel.text = $"PEN {penIndex + 1}";
        purchaseLabel.gameObject.SetActive(false);
        progressRoot.SetActive(false);
        button.interactable = true;
        Color penColour = PenUiPalette.GetColour(penIndex);
        background.color = focused
            ? penColour
            : new Color(
                penColour.r * 0.58f,
                penColour.g * 0.58f,
                penColour.b * 0.58f,
                1f);
    }

    public void RefreshPurchase(
        int nextPenIndex,
        bool affordable,
        int costCents,
        long currentCents)
    {
        penIndex = nextPenIndex;
        penLabel.text = "BUY NEW PEN";
        purchaseLabel.gameObject.SetActive(true);
        progressRoot.SetActive(true);
        button.interactable = affordable;
        purchaseLabel.text =
            $"CASH {FormatMoney(currentCents)} / {FormatMoney(costCents)}";
        Color penColour = PenUiPalette.GetColour(nextPenIndex);
        float brightness = affordable ? 0.72f : 0.28f;
        background.color = new Color(
            penColour.r * brightness,
            penColour.g * brightness,
            penColour.b * brightness,
            affordable ? 1f : 0.9f);
        SetProgressFill(
            costCents > 0 ? currentCents / (float)costCents : 1f);
    }

    public void ShowEarnings(int cents)
    {
        if (purchaseButton || cents <= 0)
        {
            return;
        }

        EnsureEarningsText();
        accumulatedEarningsCents += cents;
        earningsText.text = $"+{FormatMoney(accumulatedEarningsCents)}";

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

    private void EnsureEarningsText()
    {
        if (earningsText != null)
        {
            return;
        }

        GameObject textObject = new GameObject(
            "Pen Earnings",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(TextMeshProUGUI));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-8f, 0f);
        rect.sizeDelta = new Vector2(140f, 30f);

        earningsCanvasGroup = textObject.GetComponent<CanvasGroup>();
        earningsCanvasGroup.interactable = false;
        earningsCanvasGroup.blocksRaycasts = false;

        earningsText = textObject.GetComponent<TextMeshProUGUI>();
        earningsText.font = penLabel != null
            ? penLabel.font
            : TMP_Settings.defaultFontAsset;
        earningsText.fontSize = penLabel != null
            ? Mathf.Clamp(penLabel.fontSize * 0.72f, 11f, 16f)
            : 13f;
        earningsText.fontStyle = FontStyles.Bold;
        earningsText.alignment = TextAlignmentOptions.MidlineRight;
        earningsText.color = EarningsColor;
        earningsText.textWrappingMode = TextWrappingModes.NoWrap;
        earningsText.overflowMode = TextOverflowModes.Overflow;
        earningsText.raycastTarget = false;
    }

    private IEnumerator AnimateEarnings()
    {
        const float duration = 1.05f;
        const float fadeStart = 0.28f;
        const float riseDistance = 28f;
        Vector2 startPosition = new Vector2(-8f, 0f);
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

    private static string FormatMoney(long cents)
    {
        return $"${cents / 100:N0}.{Mathf.Abs((int)(cents % 100)):D2}";
    }

    private void SetProgressFill(float amount)
    {
        if (progressFill == null)
        {
            return;
        }

        progressFill.type = Image.Type.Simple;
        RectTransform rect = progressFill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

internal static class PenUiPalette
{
    private static readonly Color[] Colours =
    {
        new Color(0.12f, 0.55f, 0.95f, 1f),
        new Color(0.18f, 0.78f, 0.32f, 1f),
        new Color(1f, 0.50f, 0.08f, 1f),
        new Color(0.96f, 0.18f, 0.58f, 1f),
        new Color(0.05f, 0.82f, 0.86f, 1f),
        new Color(0.94f, 0.16f, 0.12f, 1f),
        new Color(0.57f, 0.25f, 0.92f, 1f),
        new Color(0.98f, 0.82f, 0.08f, 1f)
    };

    public static int Count => Colours.Length;

    public static Color GetColour(int penIndex)
    {
        return Colours[Mathf.Abs(penIndex) % Colours.Length];
    }
}
