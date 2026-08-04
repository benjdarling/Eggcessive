using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ProgressionTreePreview : MonoBehaviour
{
    [SerializeField] private GameObject previewPanel = null;
    [SerializeField] private TMP_Text titleText = null;
    [SerializeField] private TMP_Text levelText = null;
    [SerializeField] private TMP_Text descriptionText = null;
    [SerializeField] private TMP_Text priceText = null;
    [SerializeField] private TMP_Text affordabilityText = null;
    [SerializeField] private Image affordabilityFill = null;
    [SerializeField] private Button buyButton = null;
    [SerializeField] private TMP_Text buyButtonText = null;
    [SerializeField] private Button dismissButton = null;

    private ProgressionNodeButton selectedNode;

    public bool IsOpen => previewPanel != null && previewPanel.activeSelf;

    public bool IsSelected(ProgressionNodeButton node)
    {
        return selectedNode == node && previewPanel != null && previewPanel.activeSelf;
    }

    private void OnEnable()
    {
        if (buyButton != null)
        {
            buyButton.onClick.AddListener(PurchaseSelected);
        }

        if (dismissButton != null)
        {
            dismissButton.onClick.AddListener(Hide);
        }

        ProgressionSystem.Changed += Refresh;
        EggScoreHud.BalanceChanged += HandleBalanceChanged;
        Hide();
    }

    private void OnDisable()
    {
        buyButton?.onClick.RemoveListener(PurchaseSelected);
        dismissButton?.onClick.RemoveListener(Hide);
        ProgressionSystem.Changed -= Refresh;
        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
        selectedNode = null;
    }

    public void Configure(
        GameObject panel,
        TMP_Text title,
        TMP_Text level,
        TMP_Text description,
        TMP_Text price,
        TMP_Text affordability,
        Image fill,
        Button purchaseButton,
        TMP_Text purchaseButtonText,
        Button backgroundDismissButton)
    {
        previewPanel = panel;
        titleText = title;
        levelText = level;
        descriptionText = description;
        priceText = price;
        affordabilityText = affordability;
        affordabilityFill = fill;
        buyButton = purchaseButton;
        buyButtonText = purchaseButtonText;
        dismissButton = backgroundDismissButton;
        Hide();
    }

    public void Select(ProgressionNodeButton node)
    {
        if (node == null)
        {
            Hide();
            return;
        }

        ProgressionNodeButton previous = selectedNode;
        selectedNode = node;
        PositionBeside(node);
        previewPanel.SetActive(true);
        previewPanel.transform.SetAsLastSibling();
        previous?.Refresh();
        selectedNode.Refresh();
        Refresh();
    }

    public void Hide()
    {
        ProgressionNodeButton previous = selectedNode;
        selectedNode = null;

        if (previewPanel != null)
        {
            previewPanel.SetActive(false);
        }

        previous?.Refresh();
    }

    public void Refresh()
    {
        if (selectedNode == null
            || previewPanel == null
            || ProgressionSystem.Instance == null)
        {
            return;
        }

        ProgressionSystem.NodeState state = selectedNode.GetNodeState();

        long balance = EggScoreHud.CurrentCents;
        bool maxed = state.IsMaxed;
        bool affordable = state.Cost <= balance;
        bool canBuy = state.Visible && state.PrerequisiteMet && !maxed && affordable;
        bool ownershipOnly =
            !selectedNode.IsTierNode && state.MaximumLevel == 1;
        bool showSavingsProgress = !ownershipOnly || !maxed;

        titleText.text = $"<b>{state.Title}</b>";
        levelText.text = selectedNode.IsTierNode
            ? maxed
                ? "OWNED"
                : state.PrerequisiteMet
                    ? "AVAILABLE"
                    : $"REQUIRES TIER {selectedNode.TargetLevel - 1}"
            : ownershipOnly
                ? maxed ? "OWNED" : "UNLOCK"
                : state.IsRepeatable
                    ? $"OWNED  {state.Level}"
                    : $"LEVEL  {state.Level} / {state.MaximumLevel}";
        descriptionText.text =
            $"{GetDescription(selectedNode.UpgradeId)}\n\n" +
            $"<color=#FFD95A>{state.Details}</color>";
        priceText.gameObject.SetActive(!ownershipOnly || !maxed);
        priceText.text = maxed
            ? selectedNode.IsTierNode ? "OWNED" : "FULLY UPGRADED"
            : $"COST  {FormatMoney(state.Cost)}";
        affordabilityText.gameObject.SetActive(showSavingsProgress);
        affordabilityText.text = maxed
            ? "COMPLETE"
            : $"{FormatMoney(balance)} / {FormatMoney(state.Cost)}";

        if (affordabilityFill != null)
        {
            affordabilityFill.transform.parent.gameObject.SetActive(
                showSavingsProgress);
            float progress = maxed || state.Cost <= 0
                ? 1f
                : Mathf.Clamp01(balance / (float)state.Cost);
            affordabilityFill.rectTransform.anchorMax = new Vector2(progress, 1f);
            affordabilityFill.rectTransform.offsetMin = Vector2.zero;
            affordabilityFill.rectTransform.offsetMax = Vector2.zero;
        }

        buyButton.gameObject.SetActive(!ownershipOnly || !maxed);
        buyButton.interactable = canBuy;
        buyButtonText.text = maxed
            ? "MAXED"
            : !state.Visible || !state.PrerequisiteMet
                ? "LOCKED"
                : affordable
                    ? $"BUY  {FormatMoney(state.Cost)}"
                    : $"NEED  {FormatMoney(state.Cost - balance)}";

        Image buttonImage = buyButton.targetGraphic as Image;
        if (buttonImage != null)
        {
            buttonImage.color = canBuy
                ? new Color(0.16f, 0.62f, 0.31f, 1f)
                : new Color(0.25f, 0.27f, 0.25f, 1f);
        }
    }

    private void PurchaseSelected()
    {
        if (selectedNode == null || ProgressionSystem.Instance == null)
        {
            return;
        }

        ProgressionSystem.NodeState state = selectedNode.GetNodeState();
        bool purchased = ProgressionSystem.Instance.TryPurchase(
            selectedNode.UpgradeId,
            selectedNode.TargetLevel,
            out string message);
        RoundSystem.Instance?.SetShopStatus(message);
        if (purchased)
        {
            RoundSystem.Instance?.AnimateShopSpend(state.Cost);
        }
        selectedNode.Refresh();
        Refresh();
    }

    private void PositionBeside(ProgressionNodeButton node)
    {
        RectTransform panelRect = previewPanel.GetComponent<RectTransform>();
        RectTransform nodeRect = node.GetComponent<RectTransform>();
        if (panelRect == null || nodeRect == null)
        {
            return;
        }

        RectTransform panelParent = panelRect.parent as RectTransform;
        Vector3 nodeWorldCenter = nodeRect.TransformPoint(nodeRect.rect.center);
        Vector2 nodePosition = panelParent != null
            ? (Vector2)panelParent.InverseTransformPoint(nodeWorldCenter)
            : nodeRect.anchoredPosition;
        float side = nodePosition.x > 180f ? -1f : 1f;
        float horizontalOffset =
            (nodeRect.rect.width + panelRect.rect.width) * 0.5f + 12f;
        panelRect.anchoredPosition = new Vector2(
            Mathf.Clamp(
                nodePosition.x + side * horizontalOffset,
                -445f,
                445f),
            Mathf.Clamp(nodePosition.y, -320f, 270f));
    }

    private void HandleBalanceChanged(long _)
    {
        Refresh();
    }

    private static string GetDescription(ProgressionSystem.UpgradeId id)
    {
        return id switch
        {
            ProgressionSystem.UpgradeId.FoodBag =>
                "Adds one bag of your currently unlocked feed to your supplies.",
            ProgressionSystem.UpgradeId.FeedSpeed =>
                "Unlocks stronger feed so fed chickens lay eggs more frequently.",
            ProgressionSystem.UpgradeId.PrimeFeed =>
                "Multiplies premium egg chances while chickens are benefiting from feed.",
            ProgressionSystem.UpgradeId.RareEggChance =>
                "Increases the chance of rare, epic, legendary, and cosmic eggs being laid.",
            ProgressionSystem.UpgradeId.EggWeight =>
                "Raises the chance and upper range for physically heavier, larger eggs.",
            ProgressionSystem.UpgradeId.EggValue =>
                "Multiplies the sale value of every egg type.",
            ProgressionSystem.UpgradeId.TruckBonus =>
                "Increases the cash bonus paid whenever a truck is filled.",
            ProgressionSystem.UpgradeId.IncubatorInstall =>
                "Installs the incubator so deposited eggs can hatch new chickens.",
            ProgressionSystem.UpgradeId.IncubatorCapacity =>
                "Adds more simultaneous egg slots to the incubator.",
            ProgressionSystem.UpgradeId.IncubatorSpeed =>
                "Reduces the time needed to hatch each chicken.",
            ProgressionSystem.UpgradeId.CrosshatcherInstall =>
                "Installs the machine that combines two chickens into an equal or stronger breed.",
            ProgressionSystem.UpgradeId.CrosshatcherSpeed =>
                "Reduces the crosshatcher processing time.",
            ProgressionSystem.UpgradeId.CrosshatcherQuality =>
                "Raises the chance that mixed breeds produce the next stronger chicken.",
            ProgressionSystem.UpgradeId.BasketCapacity =>
                "Unlocks and expands the cursor-following egg basket.",
            ProgressionSystem.UpgradeId.BasketReach =>
                "Pulls additional loose eggs near the clicked egg into available basket slots.",
            ProgressionSystem.UpgradeId.VacuumUnlock =>
                "Unlocks click-hold vacuum collection after completing the basket upgrades.",
            ProgressionSystem.UpgradeId.VacuumPower =>
                "Increases how quickly the vacuum pulls eggs.",
            ProgressionSystem.UpgradeId.VacuumRange =>
                "Extends the vacuum cone so it can reach more eggs.",
            ProgressionSystem.UpgradeId.RobotUnlock =>
                "Adds an autonomous collector that works alongside your other tools.",
            ProgressionSystem.UpgradeId.RobotSpeed =>
                "Increases the collector robot's movement and delivery speed.",
            ProgressionSystem.UpgradeId.RobotCapacity =>
                "Allows the collector robot to carry more eggs each trip.",
            ProgressionSystem.UpgradeId.RobotSmartness =>
                "Improves incubator routing, value awareness, and rarity-first egg selection.",
            _ => string.Empty
        };
    }

    private static string FormatMoney(int cents)
    {
        return FormatMoney((long)cents);
    }

    private static string FormatMoney(long cents)
    {
        return $"${cents / 100:N0}.{System.Math.Abs(cents % 100):D2}";
    }
}
