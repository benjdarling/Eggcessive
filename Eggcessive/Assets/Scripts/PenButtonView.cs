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
        progressFill.fillAmount = costCents > 0
            ? Mathf.Clamp01(currentCents / (float)costCents)
            : 1f;
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

    private static string FormatMoney(long cents)
    {
        return $"${cents / 100:N0}.{Mathf.Abs((int)(cents % 100)):D2}";
    }
}
