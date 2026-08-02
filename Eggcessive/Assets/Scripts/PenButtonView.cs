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
        background.color = focused
            ? new Color(0.20f, 0.52f, 0.72f, 1f)
            : new Color(0.22f, 0.34f, 0.25f, 1f);
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
        background.color = affordable
            ? new Color(0.55f, 0.34f, 0.10f, 1f)
            : new Color(0.20f, 0.18f, 0.14f, 0.9f);
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
