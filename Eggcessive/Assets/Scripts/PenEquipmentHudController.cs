using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PenEquipmentHudController : MonoBehaviour
{
    [Serializable]
    private sealed class EquipmentView
    {
        public PenExpansionManager.EquipmentType type =
            PenExpansionManager.EquipmentType.Incubator;
        public Button button = null;
        public Image background = null;
        public TMP_Text title = null;
        public TMP_Text details = null;
        public TMP_Text action = null;
        public GameObject progressRoot = null;
        public Image progressFill = null;
        public TMP_Text progressText = null;
    }

    [Serializable]
    private sealed class UpgradeView
    {
        public PenExpansionManager.EquipmentUpgrade upgrade =
            PenExpansionManager.EquipmentUpgrade.IncubatorCapacity;
        public Button button = null;
        public TMP_Text label = null;
        public Image progressFill = null;
    }

    [SerializeField] private List<EquipmentView> equipmentViews =
        new List<EquipmentView>();
    [SerializeField] private List<UpgradeView> upgradeViews =
        new List<UpgradeView>();

    private PenExpansionManager manager;
    [SerializeField] private GameObject panel = null;
    [SerializeField] private TMP_Text panelTitle = null;
    [SerializeField] private GameObject dialogOverlay = null;
    [SerializeField] private TMP_Text dialogTitle = null;
    [SerializeField] private Button dialogCloseButton = null;
    private GameObject inlineUpgradePanel;
    private PenExpansionManager.EquipmentType dialogType;
    private int dialogOpenedFrame = -1;
    private bool initialized;

    public static PenEquipmentHudController Instance { get; private set; }
    public bool IsUpgradeDialogOpen =>
        inlineUpgradePanel != null && inlineUpgradePanel.activeSelf;

    public void Initialize(
        Transform canvasRoot,
        RectTransform penNavigationPanel,
        TMP_Text styleSource)
    {
        if (initialized)
        {
            return;
        }

        if (panel == null
            || panelTitle == null
            || dialogOverlay == null
            || dialogTitle == null
            || dialogCloseButton == null
            || equipmentViews == null
            || equipmentViews.Count != 4
            || upgradeViews == null
            || upgradeViews.Count != 4)
        {
            Debug.LogError(
                $"{nameof(PenEquipmentHudController)} on {name} is missing "
                + "its authored prefab UI references.",
                this);
            enabled = false;
            return;
        }

        initialized = true;
        Instance = this;
        for (int index = 0; index < equipmentViews.Count; index++)
        {
            EquipmentView view = equipmentViews[index];
            if (view?.button == null)
            {
                continue;
            }

            PenExpansionManager.EquipmentType capturedType = view.type;
            view.button.onClick.AddListener(
                () => HandleEquipmentClicked(capturedType));
        }

        for (int index = 0; index < upgradeViews.Count; index++)
        {
            int capturedIndex = index;
            upgradeViews[index].button?.onClick.AddListener(
                () => HandleUpgradeSlotClicked(capturedIndex));
        }
        dialogCloseButton.onClick.AddListener(CloseDialog);
        ConfigureInlineUpgradePanel();
        dialogOverlay.SetActive(false);
        TryBindManager();
    }

    private void OnEnable()
    {
        EggScoreHud.BalanceChanged += HandleBalanceChanged;
        RoundSystem.PhaseChanged += HandleRoundPhaseChanged;
        if (initialized)
        {
            TryBindManager();
        }
    }

    private void Update()
    {
        if (initialized && manager == null)
        {
            TryBindManager();
        }

        Pointer pointer = Pointer.current;
        if (!IsUpgradeDialogOpen
            || pointer == null
            || !pointer.press.wasPressedThisFrame
            || Time.frameCount == dialogOpenedFrame)
        {
            return;
        }

        Vector2 screenPoint = pointer.position.ReadValue();
        if (ContainsScreenPoint(inlineUpgradePanel, screenPoint))
        {
            return;
        }

        // Equipment buttons already own their click behavior (collapse the
        // current panel or switch to another machine), so leave those clicks
        // for HandleEquipmentClicked.
        for (int index = 0; index < equipmentViews.Count; index++)
        {
            if (ContainsScreenPoint(
                    equipmentViews[index]?.button?.gameObject,
                    screenPoint))
            {
                return;
            }
        }

        CloseDialog();
    }

    private void OnDisable()
    {
        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
        RoundSystem.PhaseChanged -= HandleRoundPhaseChanged;
        if (manager != null)
        {
            manager.StateChanged -= Refresh;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public Button GetRecommendedAutomationButton()
    {
        if (IsUpgradeDialogOpen)
        {
            int focusedPenIndex = manager != null
                ? manager.FocusedPenIndex
                : -1;
            if (dialogType == PenExpansionManager.EquipmentType.Incubator
                && manager != null
                && manager.GetChickenCount(focusedPenIndex)
                    >= ChickenController.MaximumChickenCount)
            {
                return GetEquipmentButton(dialogType);
            }

            for (int index = 0; index < upgradeViews.Count; index++)
            {
                Button button = upgradeViews[index].button;
                if (button != null && button.gameObject.activeInHierarchy
                    && button.interactable)
                {
                    return button;
                }
            }

            return GetEquipmentButton(dialogType);
        }

        if (manager == null)
        {
            return null;
        }

        int penIndex = manager.FocusedPenIndex;
        long balance = EggScoreHud.CurrentCents;
        EggCarryController collection = EggCarryController.Instance;
        bool botReadyForRobot = collection != null
            && collection.BasketUpgradeLevel
                >= EggCarryController.MaximumBasketLevel
            && collection.HasVacuum
            && GameplayTestBot.HasRecommendedPremiumEggProgression();
        bool ownsRobot = manager.IsEquipmentOwned(
            penIndex,
            PenExpansionManager.EquipmentType.Robot);
        bool ownsAutoFeeder = manager.IsEquipmentOwned(
            penIndex,
            PenExpansionManager.EquipmentType.AutoFeeder);

        // Once a robot has been installed, completing the automatic
        // production loop with an Auto-Feeder takes precedence over every
        // optional local upgrade. Returning null while it is unaffordable
        // makes the automation save instead of spending that money elsewhere.
        if (ownsRobot && !ownsAutoFeeder)
        {
            for (int index = 0; index < equipmentViews.Count; index++)
            {
                EquipmentView view = equipmentViews[index];
                if (view.type
                        == PenExpansionManager.EquipmentType.AutoFeeder
                    && balance >= manager.GetEquipmentPurchaseCost(view.type))
                {
                    return view.button;
                }
            }

            return null;
        }

        // Establish each part of a pen's production chain before spending the
        // automation budget on deeper upgrades to the first owned machine.
        for (int index = 0; index < equipmentViews.Count; index++)
        {
            EquipmentView view = equipmentViews[index];
            if (view.type == PenExpansionManager.EquipmentType.Robot
                && !botReadyForRobot)
            {
                // Finish the affordable basket progression before committing
                // the bot to the much larger robot -> Auto-Feeder investment.
                continue;
            }

            if (!manager.IsEquipmentOwned(penIndex, view.type)
                && balance >= manager.GetEquipmentPurchaseCost(view.type))
            {
                return view.button;
            }
        }

        for (int index = 0; index < equipmentViews.Count; index++)
        {
            EquipmentView view = equipmentViews[index];
            if (view.type == PenExpansionManager.EquipmentType.Robot
                && !botReadyForRobot)
            {
                continue;
            }

            if (view.type == PenExpansionManager.EquipmentType.Incubator
                && manager.GetChickenCount(penIndex)
                    >= ChickenController.MaximumChickenCount)
            {
                continue;
            }

            if (manager.IsEquipmentOwned(penIndex, view.type)
                && manager.HasAffordableUpgrade(penIndex, view.type))
            {
                return view.button;
            }
        }

        return null;
    }

    public void CloseUpgradeDialog()
    {
        CloseDialog();
    }

    private void TryBindManager()
    {
        PenExpansionManager candidate = PenExpansionManager.Instance;
        if (candidate == null || !candidate.IsInitialized)
        {
            return;
        }

        if (manager != null)
        {
            manager.StateChanged -= Refresh;
        }

        manager = candidate;
        manager.StateChanged += Refresh;
        Refresh();
    }

    private void HandleBalanceChanged(long _)
    {
        Refresh();
    }

    private void HandleRoundPhaseChanged(RoundSystem.RoundPhase _)
    {
        Refresh();
    }

    private void HandleEquipmentClicked(PenExpansionManager.EquipmentType type)
    {
        if (manager == null)
        {
            return;
        }

        int penIndex = manager.FocusedPenIndex;
        if (!manager.IsEquipmentOwned(penIndex, type))
        {
            manager.TryPurchaseEquipment(type);
            Refresh();
            return;
        }

        if (IsUpgradeDialogOpen && dialogType == type)
        {
            CloseDialog();
        }
        else
        {
            OpenDialog(type);
        }
    }

    private void OpenDialog(PenExpansionManager.EquipmentType type)
    {
        dialogType = type;
        dialogOpenedFrame = Time.frameCount;
        inlineUpgradePanel.SetActive(true);
        inlineUpgradePanel.transform.SetAsLastSibling();
        ConfigureUpgradeRows();
        Refresh();
    }

    private void CloseDialog()
    {
        if (inlineUpgradePanel != null)
        {
            inlineUpgradePanel.SetActive(false);
        }

        Refresh();
    }

    private static bool ContainsScreenPoint(
        GameObject target,
        Vector2 screenPoint)
    {
        if (target == null
            || !target.activeInHierarchy
            || target.transform is not RectTransform rect)
        {
            return false;
        }

        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera camera = canvas != null
            && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
        return RectTransformUtility.RectangleContainsScreenPoint(
            rect,
            screenPoint,
            camera);
    }

    private void ConfigureInlineUpgradePanel()
    {
        inlineUpgradePanel = dialogTitle.transform.parent.gameObject;
        RectTransform rect = inlineUpgradePanel.transform as RectTransform;
        rect.SetParent(panel.transform, false);
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(10f, 0f);
        rect.sizeDelta = new Vector2(520f, 330f);
        dialogCloseButton.gameObject.SetActive(true);
        inlineUpgradePanel.SetActive(false);

        Image overlayImage = dialogOverlay.GetComponent<Image>();
        if (overlayImage != null)
        {
            overlayImage.raycastTarget = false;
        }
    }

    private void ConfigureUpgradeRows()
    {
        int penIndex = manager.FocusedPenIndex;
        dialogTitle.text = $"PEN {penIndex + 1}  {GetEquipmentName(dialogType)} UPGRADES";
        PenExpansionManager.EquipmentUpgrade[] upgrades =
            PenExpansionManager.GetUpgrades(dialogType);
        float rowHeight = upgrades.Length >= 3 ? 64f : 82f;
        for (int index = 0; index < upgradeViews.Count; index++)
        {
            UpgradeView view = upgradeViews[index];
            bool isUsed = index < upgrades.Length;
            view.button.gameObject.SetActive(isUsed);
            if (!isUsed)
            {
                continue;
            }

            view.upgrade = upgrades[index];
            view.button.name =
                $"Upgrade {GetUpgradeName(view.upgrade)} Button";
            RectTransform rowRect = view.button.transform as RectTransform;
            if (rowRect != null)
            {
                rowRect.anchoredPosition =
                    new Vector2(0f, -index * (rowHeight + 8f));
                rowRect.sizeDelta = new Vector2(464f, rowHeight);
            }
            if (view.label != null)
            {
                view.label.enableAutoSizing = true;
                view.label.fontSizeMin = 11f;
                view.label.fontSizeMax = 15f;
                view.label.textWrappingMode = TextWrappingModes.NoWrap;
                view.label.overflowMode = TextOverflowModes.Truncate;
                view.label.lineSpacing = -6f;
            }
        }

        RefreshDialog();
    }

    private void HandleUpgradeSlotClicked(int index)
    {
        if (index < 0 || index >= upgradeViews.Count)
        {
            return;
        }

        HandleUpgradeClicked(upgradeViews[index].upgrade);
    }

    private void HandleUpgradeClicked(PenExpansionManager.EquipmentUpgrade upgrade)
    {
        manager?.TryUpgradeEquipment(upgrade);
        Refresh();
    }

    private void Refresh()
    {
        if (manager == null || panel == null)
        {
            return;
        }

        int penIndex = manager.FocusedPenIndex;
        panelTitle.text = $"PEN {penIndex + 1} TECH";
        long balance = EggScoreHud.CurrentCents;
        for (int index = 0; index < equipmentViews.Count; index++)
        {
            EquipmentView view = equipmentViews[index];
            bool owned = manager.IsEquipmentOwned(penIndex, view.type);
            if (!owned)
            {
                int cost = manager.GetEquipmentPurchaseCost(view.type);
                bool affordable = balance >= cost;
                view.details.text = "NOT OWNED";
                view.action.text = affordable
                    ? $"BUY\n{FormatMoney(cost)}"
                    : "SAVING";
                view.progressRoot.SetActive(!affordable);
                view.progressText.gameObject.SetActive(!affordable);
                view.progressText.text =
                    $"{FormatMoney(balance)} / {FormatMoney(cost)}";
                SetProgressFill(
                    view.progressFill,
                    cost > 0 ? balance / (float)cost : 1f);
                view.button.interactable = affordable;
                view.background.color = affordable
                    ? new Color(0.32f, 0.28f, 0.09f, 1f)
                    : new Color(0.12f, 0.12f, 0.10f, 1f);
                continue;
            }

            bool hasUpgrade = HasAnyUpgrade(penIndex, view.type);
            bool expanded = IsUpgradeDialogOpen && dialogType == view.type;
            int nextCost = GetCheapestUpgradeCost(penIndex, view.type);
            bool ready = hasUpgrade && nextCost > 0 && balance >= nextCost;
            view.details.text = GetEquipmentDetails(penIndex, view.type);
            view.action.text = !hasUpgrade
                ? "MAXED"
                : expanded
                    ? "COLLAPSE."
                    : ready
                        ? "READY."
                        : "UPGRADES.";
            view.progressRoot.SetActive(hasUpgrade && !ready);
            view.progressText.gameObject.SetActive(hasUpgrade && !ready);
            if (hasUpgrade && !ready)
            {
                view.progressText.text =
                    $"{FormatMoney(balance)} / {FormatMoney(nextCost)}";
                SetProgressFill(
                    view.progressFill,
                    nextCost > 0 ? balance / (float)nextCost : 1f);
            }
            // Keep the selected card clickable after its final upgrade so it
            // can still collapse the now-maxed popup. Once collapsed, maxed
            // cards become non-interactable again.
            view.button.interactable = hasUpgrade || expanded;
            view.background.color = expanded
                ? new Color(0.08f, 0.36f, 0.38f, 1f)
                : ready
                    ? new Color(0.48f, 0.38f, 0.06f, 1f)
                    : new Color(0.12f, 0.28f, 0.16f, 1f);
        }

        if (IsUpgradeDialogOpen)
        {
            if (!manager.IsEquipmentOwned(penIndex, dialogType))
            {
                CloseDialog();
            }
            else
            {
                RefreshDialog();
            }
        }
    }

    private void RefreshDialog()
    {
        int penIndex = manager.FocusedPenIndex;
        long balance = EggScoreHud.CurrentCents;
        for (int index = 0; index < upgradeViews.Count; index++)
        {
            UpgradeView view = upgradeViews[index];
            int level = manager.GetUpgradeLevel(penIndex, view.upgrade);
            int maximum = manager.GetMaximumUpgradeLevel(view.upgrade);
            int cost = manager.GetUpgradeCost(penIndex, view.upgrade);
            bool atMaximum = level >= maximum;
            bool affordable = cost > 0 && balance >= cost;
            view.button.interactable = !atMaximum && affordable;
            string heading = atMaximum
                ? $"{GetUpgradeName(view.upgrade)}  LEVEL {level}  MAX"
                : $"{GetUpgradeName(view.upgrade)}  LEVEL {level} > {level + 1}    "
                    + (affordable
                        ? $"BUY {FormatMoney(cost)}"
                        : $"{FormatMoney(balance)} / {FormatMoney(cost)}");
            view.label.text = heading + "\n"
                + GetUpgradeEffectDescription(view.upgrade, level, atMaximum);
            SetProgressFill(
                view.progressFill,
                atMaximum || cost <= 0
                    ? 1f
                    : balance / (float)cost);
        }
    }

    private static string GetUpgradeEffectDescription(
        PenExpansionManager.EquipmentUpgrade upgrade,
        int level,
        bool atMaximum)
    {
        int nextLevel = atMaximum ? level : level + 1;
        switch (upgrade)
        {
            case PenExpansionManager.EquipmentUpgrade.IncubatorCapacity:
            {
                int current = IncubatorController.GetCapacity(level);
                int next = IncubatorController.GetCapacity(nextLevel);
                return atMaximum
                    ? $"{current} EGG SLOTS"
                    : $"{current} > {next} EGG SLOTS";
            }
            case PenExpansionManager.EquipmentUpgrade.IncubatorSpeed:
                return FormatTimeUpgrade(
                    IncubatorController.GetProductionTime(level),
                    IncubatorController.GetProductionTime(nextLevel),
                    "SECONDS PER HATCH",
                    atMaximum);
            case PenExpansionManager.EquipmentUpgrade.CrosshatcherSpeed:
                return FormatTimeUpgrade(
                    CrosshatcherController.GetProcessingTime(level),
                    CrosshatcherController.GetProcessingTime(nextLevel),
                    "SECONDS PER CROSSHATCH",
                    atMaximum);
            case PenExpansionManager.EquipmentUpgrade.CrosshatcherQuality:
            {
                float current = CrosshatcherController.GetImprovementChance(level)
                    * 100f;
                float next = CrosshatcherController.GetImprovementChance(nextLevel)
                    * 100f;
                return atMaximum
                    ? $"{current:0.#}% NEXT-BREED CHANCE"
                    : $"{current:0.#}% > {next:0.#}% NEXT-BREED CHANCE  (+{next - current:0.#}%)";
            }
            case PenExpansionManager.EquipmentUpgrade.RobotSpeed:
            {
                float current = EggCarryController.GetRobotSpeed(level);
                float next = EggCarryController.GetRobotSpeed(nextLevel);
                return atMaximum
                    ? $"{current:0.#} M/S MOVEMENT SPEED"
                    : $"{current:0.#} > {next:0.#} M/S MOVEMENT SPEED  (+{GetIncreasePercent(current, next):0.#}%)";
            }
            case PenExpansionManager.EquipmentUpgrade.RobotCapacity:
            {
                int current = EggCarryController.GetRobotCapacity(level);
                int next = EggCarryController.GetRobotCapacity(nextLevel);
                return atMaximum
                    ? $"CARRIES {current} EGGS"
                    : $"CARRIES {current} > {next} EGGS";
            }
            case PenExpansionManager.EquipmentUpgrade.RobotSmartness:
                return GetRobotLogicDescription(nextLevel);
            case PenExpansionManager.EquipmentUpgrade.RobotVacuum:
            {
                float current = EggCollectorRobot.GetVacuumRadius(level);
                float next = EggCollectorRobot.GetVacuumRadius(nextLevel);
                string currentText = level > 0 ? $"{current:0.#}M" : "OFF";
                return atMaximum
                    ? $"{currentText} EGG PICKUP RADIUS"
                    : $"{currentText} > {next:0.#}M EGG PICKUP RADIUS";
            }
            case PenExpansionManager.EquipmentUpgrade.AutoFeederSpeed:
                return FormatTimeUpgrade(
                    AutoFeederController.GetDispenseInterval(level),
                    AutoFeederController.GetDispenseInterval(nextLevel),
                    "SECONDS PER FEED DROP",
                    atMaximum);
            case PenExpansionManager.EquipmentUpgrade.AutoFeederRange:
            {
                float current = AutoFeederController.GetAttractionRadiusBonus(level);
                float next = AutoFeederController.GetAttractionRadiusBonus(nextLevel);
                string currentText = level > 0 ? $"+{current:0.0}M" : "OFF";
                return atMaximum
                    ? $"{currentText} CHICKEN ATTRACTION RANGE"
                    : $"{currentText} > +{next:0.0}M CHICKEN ATTRACTION RANGE";
            }
            default:
                return $"LEVEL {level}";
        }
    }

    private static string FormatTimeUpgrade(
        float current,
        float next,
        string unit,
        bool atMaximum)
    {
        if (atMaximum)
        {
            return $"{current:0.##} {unit}";
        }

        float faster = current > 0f
            ? (current - next) / current * 100f
            : 0f;
        return $"{current:0.##} > {next:0.##} {unit}  ({faster:0.#}% FASTER)";
    }

    private static float GetIncreasePercent(float current, float next)
    {
        return current > 0f ? (next - current) / current * 100f : 0f;
    }

    private static string GetRobotLogicDescription(int level)
    {
        return level switch
        {
            1 => "ROUTE SURPLUS COMMON EGGS TO THE INCUBATOR",
            2 => "KEEP THE INCUBATOR SUPPLIED FOR FLOCK GROWTH",
            3 => "PRIORITISE HIGH-RARITY EGGS",
            _ => "CARRY CHICKEN PAIRS TO THE CROSSHATCHER"
        };
    }

    private bool HasAnyUpgrade(
        int penIndex,
        PenExpansionManager.EquipmentType type)
    {
        PenExpansionManager.EquipmentUpgrade[] upgrades =
            PenExpansionManager.GetUpgrades(type);
        for (int index = 0; index < upgrades.Length; index++)
        {
            if (manager.GetUpgradeCost(penIndex, upgrades[index]) > 0)
            {
                return true;
            }
        }

        return false;
    }

    private int GetCheapestUpgradeCost(
        int penIndex,
        PenExpansionManager.EquipmentType type)
    {
        int cheapest = int.MaxValue;
        PenExpansionManager.EquipmentUpgrade[] upgrades =
            PenExpansionManager.GetUpgrades(type);
        for (int index = 0; index < upgrades.Length; index++)
        {
            int cost = manager.GetUpgradeCost(penIndex, upgrades[index]);
            if (cost > 0)
            {
                cheapest = Mathf.Min(cheapest, cost);
            }
        }

        return cheapest == int.MaxValue ? 0 : cheapest;
    }

    private Button GetEquipmentButton(
        PenExpansionManager.EquipmentType type)
    {
        for (int index = 0; index < equipmentViews.Count; index++)
        {
            if (equipmentViews[index].type == type)
            {
                return equipmentViews[index].button;
            }
        }

        return null;
    }

    private string GetEquipmentDetails(
        int penIndex,
        PenExpansionManager.EquipmentType type)
    {
        PenExpansionManager.EquipmentUpgrade[] upgrades =
            PenExpansionManager.GetUpgrades(type);
        if (type == PenExpansionManager.EquipmentType.Incubator)
        {
            return $"CAP {manager.GetUpgradeLevel(penIndex, upgrades[0])}  "
                + $"SPD {manager.GetUpgradeLevel(penIndex, upgrades[1])}";
        }
        if (type == PenExpansionManager.EquipmentType.Crosshatcher)
        {
            return $"SPD {manager.GetUpgradeLevel(penIndex, upgrades[0])}  "
                + $"QLTY {manager.GetUpgradeLevel(penIndex, upgrades[1])}";
        }
        if (type == PenExpansionManager.EquipmentType.AutoFeeder)
        {
            int speedLevel = manager.GetUpgradeLevel(penIndex, upgrades[0]);
            int rangeLevel = manager.GetUpgradeLevel(penIndex, upgrades[1]);
            float attractionBonus =
                AutoFeederController.GetAttractionRadiusBonus(rangeLevel);
            string range = rangeLevel > 0
                ? $"+{attractionBonus:0.0}M"
                : "OFF";
            return $"EVERY {AutoFeederController.GetDispenseInterval(speedLevel):0} SEC  "
                + $"RNG {range}";
        }

        float vacuumRadius = EggCollectorRobot.GetVacuumRadius(
            manager.GetUpgradeLevel(penIndex, upgrades[3]));
        return $"SPD {manager.GetUpgradeLevel(penIndex, upgrades[0])}  "
            + $"CAP {manager.GetUpgradeLevel(penIndex, upgrades[1])}  "
            + $"AI {manager.GetUpgradeLevel(penIndex, upgrades[2])}  "
            + $"VAC {vacuumRadius:0.#}M";
    }

    private static string GetEquipmentName(
        PenExpansionManager.EquipmentType type)
    {
        return type switch
        {
            PenExpansionManager.EquipmentType.Crosshatcher => "CROSSHATCHER",
            PenExpansionManager.EquipmentType.AutoFeeder => "AUTO-FEEDER",
            _ => type.ToString().ToUpperInvariant()
        };
    }

    private static string GetUpgradeName(
        PenExpansionManager.EquipmentUpgrade upgrade)
    {
        return upgrade switch
        {
            PenExpansionManager.EquipmentUpgrade.IncubatorCapacity => "CAPACITY",
            PenExpansionManager.EquipmentUpgrade.IncubatorSpeed => "SPEED",
            PenExpansionManager.EquipmentUpgrade.CrosshatcherSpeed => "SPEED",
            PenExpansionManager.EquipmentUpgrade.CrosshatcherQuality => "QUALITY",
            PenExpansionManager.EquipmentUpgrade.RobotSpeed => "SPEED",
            PenExpansionManager.EquipmentUpgrade.RobotCapacity => "CAPACITY",
            PenExpansionManager.EquipmentUpgrade.RobotVacuum => "VACUUM",
            PenExpansionManager.EquipmentUpgrade.AutoFeederSpeed => "SPEED",
            PenExpansionManager.EquipmentUpgrade.AutoFeederRange => "RANGE",
            _ => "LOGIC"
        };
    }

    private static void SetProgressFill(Image fill, float amount)
    {
        if (fill == null)
        {
            return;
        }

        float clampedAmount = Mathf.Clamp01(amount);
        RectTransform rect = fill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(clampedAmount, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static string FormatMoney(long cents)
    {
        return $"${cents / 100:N0}.{Mathf.Abs((int)(cents % 100)):D2}";
    }
}
