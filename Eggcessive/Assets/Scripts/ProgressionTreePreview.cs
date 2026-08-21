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
    private float scheduledHideTime = -1f;
    private bool selectionPinned;
    private CanvasGroup popupCanvasGroup;
    private Transform popupMotionTransform;
    private Vector3 popupRestingScale = Vector3.one;
    private Quaternion popupRestingRotation = Quaternion.identity;
    private SpringUtils.FloatSpring popupScaleSpring =
        new SpringUtils.FloatSpring(1f);
    private SpringUtils.AngleSpring popupRotationSpring =
        new SpringUtils.AngleSpring(0f);
    private SpringUtils.FloatSpring popupAlphaSpring =
        new SpringUtils.FloatSpring(0f);
    private bool popupAnimatingOut;
    private float popupExitDirection = 1f;

    public bool IsOpen => previewPanel != null
        && previewPanel.activeSelf
        && !popupAnimatingOut;
    public ProgressionNodeButton SelectedNode => selectedNode;

    public bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!IsOpen
            || previewPanel.transform is not RectTransform panelRect)
        {
            return false;
        }

        Canvas canvas = panelRect.GetComponentInParent<Canvas>();
        Camera uiCamera = canvas != null
            && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
        return RectTransformUtility.RectangleContainsScreenPoint(
            panelRect,
            screenPoint,
            uiCamera);
    }

    public bool IsSelected(ProgressionNodeButton node)
    {
        return selectedNode == node && previewPanel != null && previewPanel.activeSelf;
    }

    public bool IsPinned(ProgressionNodeButton node)
    {
        return selectionPinned && IsSelected(node);
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
        HideImmediate();
    }

    private void OnDisable()
    {
        buyButton?.onClick.RemoveListener(PurchaseSelected);
        dismissButton?.onClick.RemoveListener(Hide);
        ProgressionSystem.Changed -= Refresh;
        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
        selectedNode = null;
        scheduledHideTime = -1f;
        selectionPinned = false;
        HideImmediate();
    }

    private void Update()
    {
        if (scheduledHideTime >= 0f
            && Time.unscaledTime >= scheduledHideTime)
        {
            scheduledHideTime = -1f;
            Hide();
        }

        StepPopupMotion();
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
        bool rebind = isActiveAndEnabled;
        if (rebind)
        {
            buyButton?.onClick.RemoveListener(PurchaseSelected);
            dismissButton?.onClick.RemoveListener(Hide);
        }

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
        if (buyButton != null
            && buyButton.GetComponent<SpringMenuButton>() == null)
        {
            SpringMenuButton springButton =
                buyButton.gameObject.AddComponent<SpringMenuButton>();
            springButton.Initialize(buyButton, buyButtonText, null);
        }
        popupMotionTransform = null;
        EnsurePopupMotion();
        if (rebind)
        {
            buyButton?.onClick.AddListener(PurchaseSelected);
            dismissButton?.onClick.AddListener(Hide);
        }
        HideImmediate();
    }

    public void Select(ProgressionNodeButton node)
    {
        Show(node, true);
    }

    public void Preview(ProgressionNodeButton node)
    {
        if (!selectionPinned)
        {
            Show(node, false);
        }
    }

    private void Show(ProgressionNodeButton node, bool pinSelection)
    {
        if (node == null)
        {
            Hide();
            return;
        }

        ProgressionNodeButton previous = selectedNode;
        CancelScheduledHide();
        selectedNode = node;
        selectionPinned = pinSelection;
        PositionBeside(node);
        EnsurePopupMotion();
        bool animateEntrance = !previewPanel.activeSelf || popupAnimatingOut;
        previewPanel.SetActive(true);
        previewPanel.transform.SetAsLastSibling();
        popupAnimatingOut = false;
        popupExitDirection = (node.GetInstanceID() & 1) == 0 ? 1f : -1f;
        if (popupCanvasGroup != null)
        {
            popupCanvasGroup.interactable = true;
            popupCanvasGroup.blocksRaycasts = true;
        }
        if (animateEntrance)
        {
            popupScaleSpring.Reset(0.82f, 1.1f);
            popupRotationSpring.Reset(
                popupExitDirection * 4.5f,
                -popupExitDirection * 18f);
            popupAlphaSpring.Reset(0f, 4f);
            ApplyPopupMotion();
        }
        else
        {
            popupScaleSpring.AddImpulse(0.34f);
            popupRotationSpring.AddImpulse(-popupExitDirection * 18f);
        }
        previous?.Refresh();
        selectedNode.Refresh();
        Refresh();
    }

    public void Hide()
    {
        scheduledHideTime = -1f;
        ProgressionNodeButton previous = selectedNode;
        selectedNode = null;
        selectionPinned = false;

        if (previewPanel != null && previewPanel.activeSelf)
        {
            EnsurePopupMotion();
            popupAnimatingOut = true;
            popupScaleSpring.AddImpulse(-0.32f);
            popupRotationSpring.AddImpulse(popupExitDirection * 22f);
            if (popupCanvasGroup != null)
            {
                popupCanvasGroup.interactable = false;
                popupCanvasGroup.blocksRaycasts = false;
            }
        }

        previous?.Refresh();
    }

    private void HideImmediate()
    {
        scheduledHideTime = -1f;
        ProgressionNodeButton previous = selectedNode;
        selectedNode = null;
        selectionPinned = false;
        popupAnimatingOut = false;
        EnsurePopupMotion();
        popupScaleSpring.Reset(1f);
        popupRotationSpring.Reset(0f);
        popupAlphaSpring.Reset(0f);
        if (popupCanvasGroup != null)
        {
            popupCanvasGroup.alpha = 0f;
            popupCanvasGroup.interactable = false;
            popupCanvasGroup.blocksRaycasts = false;
        }
        if (previewPanel != null)
        {
            previewPanel.SetActive(false);
        }
        previous?.Refresh();
    }

    private void EnsurePopupMotion()
    {
        if (previewPanel == null
            || popupMotionTransform == previewPanel.transform)
        {
            return;
        }

        popupMotionTransform = previewPanel.transform;
        popupRestingScale = popupMotionTransform.localScale;
        popupRestingRotation = popupMotionTransform.localRotation;
        popupCanvasGroup = previewPanel.GetComponent<CanvasGroup>();
        if (popupCanvasGroup == null)
        {
            popupCanvasGroup = previewPanel.AddComponent<CanvasGroup>();
        }
        popupScaleSpring.Reset(1f);
        popupRotationSpring.Reset(0f);
        popupAlphaSpring.Reset(previewPanel.activeSelf ? 1f : 0f);
    }

    private void StepPopupMotion()
    {
        if (previewPanel == null || !previewPanel.activeSelf)
        {
            return;
        }

        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        float scaleTarget = popupAnimatingOut ? 0.88f : 1f;
        float rotationTarget = popupAnimatingOut
            ? popupExitDirection * 2.75f
            : 0f;
        float alphaTarget = popupAnimatingOut ? 0f : 1f;
        popupScaleSpring.Update(
            scaleTarget,
            deltaTime,
            popupAnimatingOut ? 10f : 7f,
            popupAnimatingOut ? 0.72f : 0.5f);
        popupRotationSpring.Update(
            rotationTarget,
            deltaTime,
            popupAnimatingOut ? 9f : 6.5f,
            popupAnimatingOut ? 0.72f : 0.48f);
        popupAlphaSpring.Update(alphaTarget, deltaTime, 12f, 0.82f);
        popupScaleSpring.ClampValue(0.78f, 1.12f);
        popupRotationSpring.ClampValue(-7f, 7f);
        popupAlphaSpring.ClampValue(0f, 1f);
        ApplyPopupMotion();

        if (popupAnimatingOut
            && popupAlphaSpring.Value <= 0.015f
            && Mathf.Abs(popupScaleSpring.Value - scaleTarget) <= 0.015f)
        {
            previewPanel.SetActive(false);
            popupAnimatingOut = false;
        }
    }

    private void ApplyPopupMotion()
    {
        if (popupMotionTransform == null)
        {
            return;
        }

        popupMotionTransform.localScale = Vector3.Scale(
            popupRestingScale,
            Vector3.one * popupScaleSpring.Value);
        popupMotionTransform.localRotation = popupRestingRotation
            * Quaternion.Euler(0f, 0f, popupRotationSpring.Value);
        if (popupCanvasGroup != null)
        {
            popupCanvasGroup.alpha = popupAlphaSpring.Value;
        }
    }

    public void ScheduleHide(float delay = 0.12f)
    {
        if (selectionPinned)
        {
            return;
        }

        scheduledHideTime = Time.unscaledTime + Mathf.Max(0f, delay);
    }

    public void CancelScheduledHide()
    {
        scheduledHideTime = -1f;
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
                    ? $"IN STOCK  {state.Level}"
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
                    ? state.IsRepeatable && state.Level > 0
                        ? $"BUY ANOTHER  {FormatMoney(state.Cost)}"
                        : $"BUY  {FormatMoney(state.Cost)}"
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
            popupScaleSpring.AddImpulse(0.7f);
            popupRotationSpring.AddImpulse(-popupExitDirection * 34f);
        }
        else
        {
            popupRotationSpring.AddImpulse(popupExitDirection * 48f);
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
        Rect parentBounds = panelParent != null
            ? panelParent.rect
            : new Rect(-940f, -460f, 1880f, 920f);
        float rightSpace = parentBounds.xMax - nodePosition.x;
        float leftSpace = nodePosition.x - parentBounds.xMin;
        float side = rightSpace >= panelRect.rect.width + 24f
            ? 1f
            : leftSpace >= panelRect.rect.width + 24f
                ? -1f
                : rightSpace >= leftSpace ? 1f : -1f;
        float horizontalOffset =
            (nodeRect.rect.width + panelRect.rect.width) * 0.5f + 12f;
        float halfWidth = panelRect.rect.width * 0.5f;
        float halfHeight = panelRect.rect.height * 0.5f;
        panelRect.anchoredPosition = new Vector2(
            Mathf.Clamp(
                nodePosition.x + side * horizontalOffset,
                parentBounds.xMin + halfWidth + 18f,
                parentBounds.xMax - halfWidth - 18f),
            Mathf.Clamp(
                nodePosition.y,
                parentBounds.yMin + halfHeight + 18f,
                parentBounds.yMax - halfHeight - 78f));
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
            ProgressionSystem.UpgradeId.IncubatorTurbo
                or ProgressionSystem.UpgradeId.CrosshatcherTurbo
                or ProgressionSystem.UpgradeId.RobotTurbo =>
                "Adds one instant-use turbo to your HUD inventory. Pressing it boosts every installed machine.",
            ProgressionSystem.UpgradeId.IncubatorTurboPower
                or ProgressionSystem.UpgradeId.CrosshatcherTurboPower
                or ProgressionSystem.UpgradeId.RobotTurboPower =>
                "Raises the temporary productivity boost supplied to every machine.",
            ProgressionSystem.UpgradeId.IncubatorTurboDuration
                or ProgressionSystem.UpgradeId.CrosshatcherTurboDuration
                or ProgressionSystem.UpgradeId.RobotTurboDuration =>
                "Extends how many real-time seconds this turbo remains active.",
            ProgressionSystem.UpgradeId.FeedSpeed =>
                "Unlocks stronger feed so fed chickens lay eggs more frequently.",
            ProgressionSystem.UpgradeId.PrimeFeed =>
                "Multiplies premium egg chances while chickens are benefiting from feed.",
            ProgressionSystem.UpgradeId.RareEggChance =>
                "Increases the chance of rare, epic, legendary, and cosmic eggs being laid.",
            ProgressionSystem.UpgradeId.ChickenPerks =>
                "Multiplies premium egg odds by chicken breed, with stronger benefits for advanced breeds.",
            ProgressionSystem.UpgradeId.EggWeight =>
                "Raises the chance and upper range for physically heavier, larger eggs.",
            ProgressionSystem.UpgradeId.EggValue =>
                "Multiplies the sale value of every egg type.",
            ProgressionSystem.UpgradeId.TruckBonus =>
                "Increases the cash bonus paid whenever a truck is filled.",
            ProgressionSystem.UpgradeId.PenBonus =>
                "Boosts each pen's egg value from its own maxed local upgrades. Each additional maxed upgrade contributes more than the previous one.",
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
                "Adds incubator routing, proactive population growth, value awareness, and rarity-first egg selection.",
            _ => string.Empty
        };
    }

    private static string FormatMoney(long cents)
    {
        return $"${cents / 100:N0}.{System.Math.Abs(cents % 100):D2}";
    }
}
