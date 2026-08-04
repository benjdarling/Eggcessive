using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PenEquipmentHudController : MonoBehaviour
{
    private sealed class EquipmentView
    {
        public PenExpansionManager.EquipmentType type;
        public Button button;
        public Image background;
        public TMP_Text title;
        public TMP_Text details;
        public TMP_Text action;
        public GameObject progressRoot;
        public Image progressFill;
        public TMP_Text progressText;
    }

    private sealed class UpgradeView
    {
        public PenExpansionManager.EquipmentUpgrade upgrade;
        public Button button;
        public TMP_Text label;
        public Image progressFill;
    }

    private readonly List<EquipmentView> equipmentViews =
        new List<EquipmentView>();
    private readonly List<UpgradeView> upgradeViews =
        new List<UpgradeView>();

    private PenExpansionManager manager;
    private TMP_FontAsset font;
    private GameObject panel;
    private TMP_Text panelTitle;
    private GameObject dialogOverlay;
    private TMP_Text dialogTitle;
    private RectTransform dialogRows;
    private Button dialogCloseButton;
    private PenExpansionManager.EquipmentType dialogType;
    private bool initialized;

    private const float PanelWidth = 260f;
    private const float PanelHeight = 310f;

    public static PenEquipmentHudController Instance { get; private set; }
    public bool IsUpgradeDialogOpen =>
        dialogOverlay != null && dialogOverlay.activeSelf;

    public void Initialize(
        Transform canvasRoot,
        RectTransform penNavigationPanel,
        TMP_Text styleSource)
    {
        if (initialized || canvasRoot == null)
        {
            return;
        }

        initialized = true;
        Instance = this;
        font = styleSource != null ? styleSource.font : TMP_Settings.defaultFontAsset;
        BuildPanel(canvasRoot, penNavigationPanel);
        BuildDialog(canvasRoot);
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
                return dialogCloseButton;
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

            return dialogCloseButton;
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
        CloseDialog();
    }

    private void BuildPanel(
        Transform canvasRoot,
        RectTransform penNavigationPanel)
    {
        panel = CreateUiObject("Local Pen Equipment", canvasRoot);
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -24f);

        rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        CopyPanelRenderingStyle(
            penNavigationPanel != null
                ? penNavigationPanel.gameObject
                : null,
            panel);

        panelTitle = CreateText(
            "Panel Title",
            panel.transform,
            "PEN 1 TECH",
            15f,
            TextAlignmentOptions.Center,
            FontStyles.Bold);
        SetRect(panelTitle.rectTransform, new Vector2(8f, -7f),
            new Vector2(244f, 25f));

        CreateEquipmentView(
            PenExpansionManager.EquipmentType.Incubator,
            "INCUBATOR",
            new Vector2(8f, -38f));
        CreateEquipmentView(
            PenExpansionManager.EquipmentType.Crosshatcher,
            "CROSSHATCHER",
            new Vector2(8f, -104f));
        CreateEquipmentView(
            PenExpansionManager.EquipmentType.Robot,
            "ROBOT",
            new Vector2(8f, -170f));
        CreateEquipmentView(
            PenExpansionManager.EquipmentType.AutoFeeder,
            "AUTO-FEEDER",
            new Vector2(8f, -236f));
    }

    private void CreateEquipmentView(
        PenExpansionManager.EquipmentType type,
        string title,
        Vector2 position)
    {
        GameObject card = CreateUiObject($"Local {title} Button", panel.transform);
        RectTransform rect = card.GetComponent<RectTransform>();
        SetRect(rect, position, new Vector2(244f, 59f));
        Image background = card.AddComponent<Image>();
        Button button = card.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => HandleEquipmentClicked(type));

        TMP_Text titleText = CreateText(
            "Title", card.transform, title, 14f,
            TextAlignmentOptions.TopLeft, FontStyles.Bold);
        SetRect(titleText.rectTransform, new Vector2(8f, -5f),
            new Vector2(130f, 22f));

        TMP_Text details = CreateText(
            "Details", card.transform, string.Empty, 10f,
            TextAlignmentOptions.BottomLeft, FontStyles.Normal);
        details.color = new Color(0.72f, 0.78f, 0.68f);
        SetRect(details.rectTransform, new Vector2(8f, -28f),
            new Vector2(135f, 23f));

        TMP_Text action = CreateText(
            "Action", card.transform, string.Empty, 11f,
            TextAlignmentOptions.Center, FontStyles.Bold);
        action.color = new Color(1f, 0.84f, 0.25f);
        SetRect(action.rectTransform, new Vector2(142f, -8f),
            new Vector2(94f, 38f));

        GameObject progress = CreateUiObject("Cash Progress", card.transform);
        SetRect(progress.GetComponent<RectTransform>(), new Vector2(142f, -42f),
            new Vector2(94f, 10f));
        Image progressBack = progress.AddComponent<Image>();
        progressBack.color = new Color(0.08f, 0.08f, 0.06f, 1f);
        GameObject fillObject = CreateUiObject("Fill", progress.transform);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        Stretch(fillRect);
        Image fill = fillObject.AddComponent<Image>();
        fill.color = new Color(1f, 0.68f, 0.08f, 1f);
        fill.type = Image.Type.Simple;
        TMP_Text progressText = CreateText(
            "Cash Text", card.transform, string.Empty, 8f,
            TextAlignmentOptions.Center, FontStyles.Bold);
        SetRect(progressText.rectTransform, new Vector2(137f, -32f),
            new Vector2(104f, 12f));

        equipmentViews.Add(new EquipmentView
        {
            type = type,
            button = button,
            background = background,
            title = titleText,
            details = details,
            action = action,
            progressRoot = progress,
            progressFill = fill,
            progressText = progressText
        });
    }

    private void BuildDialog(Transform canvasRoot)
    {
        dialogOverlay = CreateUiObject("Pen Equipment Upgrade Dialog", canvasRoot);
        RectTransform overlayRect = dialogOverlay.GetComponent<RectTransform>();
        Stretch(overlayRect);
        Image overlay = dialogOverlay.AddComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0.68f);

        GameObject card = CreateUiObject("Upgrade Card", dialogOverlay.transform);
        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;
        cardRect.sizeDelta = new Vector2(520f, 330f);
        Image cardImage = card.AddComponent<Image>();
        cardImage.color = new Color(0.055f, 0.06f, 0.045f, 1f);
        AddOutline(card, new Color(0.35f, 0.22f, 0.06f, 1f), 4f);

        dialogTitle = CreateText(
            "Dialog Title", card.transform, string.Empty, 24f,
            TextAlignmentOptions.Center, FontStyles.Bold);
        SetRect(dialogTitle.rectTransform, new Vector2(50f, -18f),
            new Vector2(420f, 42f));

        dialogCloseButton = CreateSimpleButton(
            "Close Local Upgrade Dialog", card.transform, "X",
            new Vector2(466f, -16f), new Vector2(38f, 38f));
        dialogCloseButton.onClick.AddListener(CloseDialog);

        GameObject rows = CreateUiObject("Upgrade Rows", card.transform);
        dialogRows = rows.GetComponent<RectTransform>();
        SetRect(dialogRows, new Vector2(28f, -78f), new Vector2(464f, 224f));
        dialogOverlay.SetActive(false);
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

        OpenDialog(type);
    }

    private void OpenDialog(PenExpansionManager.EquipmentType type)
    {
        dialogType = type;
        dialogOverlay.SetActive(true);
        dialogOverlay.transform.SetAsLastSibling();
        BuildUpgradeRows();
    }

    private void CloseDialog()
    {
        if (dialogOverlay != null)
        {
            dialogOverlay.SetActive(false);
        }
    }

    private void BuildUpgradeRows()
    {
        for (int index = dialogRows.childCount - 1; index >= 0; index--)
        {
            Destroy(dialogRows.GetChild(index).gameObject);
        }
        upgradeViews.Clear();

        int penIndex = manager.FocusedPenIndex;
        dialogTitle.text = $"PEN {penIndex + 1}  {GetEquipmentName(dialogType)} UPGRADES";
        PenExpansionManager.EquipmentUpgrade[] upgrades =
            PenExpansionManager.GetUpgrades(dialogType);
        float rowHeight = upgrades.Length >= 3 ? 64f : 82f;
        for (int index = 0; index < upgrades.Length; index++)
        {
            PenExpansionManager.EquipmentUpgrade upgrade = upgrades[index];
            GameObject row = CreateUiObject(
                $"Upgrade {GetUpgradeName(upgrade)} Button", dialogRows);
            SetRect(row.GetComponent<RectTransform>(),
                new Vector2(0f, -index * (rowHeight + 8f)),
                new Vector2(464f, rowHeight));
            Image background = row.AddComponent<Image>();
            background.color = new Color(0.16f, 0.24f, 0.14f, 1f);
            Button button = row.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() => HandleUpgradeClicked(upgrade));
            TMP_Text label = CreateText(
                "Label", row.transform, string.Empty, 15f,
                TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(label.rectTransform, 10f);

            GameObject progressObject = CreateUiObject("Cash Progress", row.transform);
            RectTransform progressRect = progressObject.GetComponent<RectTransform>();
            progressRect.anchorMin = new Vector2(0f, 0f);
            progressRect.anchorMax = new Vector2(1f, 0f);
            progressRect.pivot = new Vector2(0.5f, 0f);
            progressRect.anchoredPosition = new Vector2(0f, 3f);
            progressRect.sizeDelta = new Vector2(-12f, 6f);
            Image progressBackground = progressObject.AddComponent<Image>();
            progressBackground.color = new Color(0.06f, 0.06f, 0.05f, 1f);
            GameObject fillObject = CreateUiObject("Fill", progressObject.transform);
            Stretch(fillObject.GetComponent<RectTransform>());
            Image fill = fillObject.AddComponent<Image>();
            fill.type = Image.Type.Simple;
            fill.color = new Color(1f, 0.68f, 0.08f, 1f);

            upgradeViews.Add(new UpgradeView
            {
                upgrade = upgrade,
                button = button,
                label = label,
                progressFill = fill
            });
        }

        RefreshDialog();
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
            view.details.text = GetEquipmentDetails(penIndex, view.type);
            view.action.text = hasUpgrade ? "UPGRADE" : "MAXED";
            view.progressRoot.SetActive(false);
            view.progressText.gameObject.SetActive(false);
            view.button.interactable = hasUpgrade;
            view.background.color = new Color(0.12f, 0.28f, 0.16f, 1f);
        }

        if (dialogOverlay.activeSelf)
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
            if (view.upgrade == PenExpansionManager.EquipmentUpgrade.RobotVacuum)
            {
                float currentRadius = EggCollectorRobot.GetVacuumRadius(level);
                float nextRadius = EggCollectorRobot.GetVacuumRadius(level + 1);
                string current = level > 0 ? $"{currentRadius:0.#}M" : "OFF";
                view.label.text = atMaximum
                    ? $"VACUUM  {current}  MAX"
                    : $"VACUUM  {current} > {nextRadius:0.#}M    "
                        + (affordable
                            ? $"UPGRADE {FormatMoney(cost)}"
                            : $"{FormatMoney(balance)} / {FormatMoney(cost)}");
            }
            else
            {
                view.label.text = atMaximum
                    ? $"{GetUpgradeName(view.upgrade)}  LEVEL {level}  MAX"
                    : $"{GetUpgradeName(view.upgrade)}  {level} > {level + 1}    "
                        + (affordable
                            ? $"UPGRADE {FormatMoney(cost)}"
                            : $"{FormatMoney(balance)} / {FormatMoney(cost)}");
            }
            SetProgressFill(
                view.progressFill,
                atMaximum || cost <= 0
                    ? 1f
                    : balance / (float)cost);
        }
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
            int level = manager.GetUpgradeLevel(penIndex, upgrades[0]);
            return $"EVERY {AutoFeederController.GetDispenseInterval(level):0} SEC";
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
            _ => "LOGIC"
        };
    }

    private TMP_Text CreateText(
        string name,
        Transform parent,
        string value,
        float size,
        TextAlignmentOptions alignment,
        FontStyles style)
    {
        GameObject gameObject = CreateUiObject(name, parent);
        TextMeshProUGUI text = gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.fontStyle = style;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Truncate;
        return text;
    }

    private Button CreateSimpleButton(
        string name,
        Transform parent,
        string label,
        Vector2 position,
        Vector2 size)
    {
        GameObject gameObject = CreateUiObject(name, parent);
        SetRect(gameObject.GetComponent<RectTransform>(), position, size);
        Image image = gameObject.AddComponent<Image>();
        image.color = new Color(0.55f, 0.16f, 0.08f, 1f);
        Button button = gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        TMP_Text text = CreateText(
            "Label", gameObject.transform, label, 22f,
            TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(text.rectTransform);
        return button;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.layer = LayerMask.NameToLayer("UI");
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.one * inset;
        rect.offsetMax = Vector2.one * -inset;
    }

    private static void AddOutline(
        GameObject gameObject,
        Color color,
        float distance)
    {
        Outline outline = gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
        outline.useGraphicAlpha = false;
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

    private static void CopyPanelRenderingStyle(
        GameObject source,
        GameObject target)
    {
        Image targetImage = target.AddComponent<Image>();
        Image sourceImage = source != null
            ? source.GetComponent<Image>()
            : null;
        if (sourceImage != null)
        {
            targetImage.sprite = sourceImage.sprite;
            targetImage.overrideSprite = sourceImage.overrideSprite;
            targetImage.type = sourceImage.type;
            targetImage.preserveAspect = sourceImage.preserveAspect;
            targetImage.fillCenter = sourceImage.fillCenter;
            targetImage.pixelsPerUnitMultiplier =
                sourceImage.pixelsPerUnitMultiplier;
            targetImage.material = sourceImage.material;
            targetImage.color = sourceImage.color.a > 0.01f
                ? sourceImage.color
                : new Color(0.055f, 0.06f, 0.048f, 0.94f);
            targetImage.raycastTarget = sourceImage.raycastTarget;
        }
        else
        {
            targetImage.color = new Color(0.055f, 0.06f, 0.048f, 0.94f);
        }

        Outline sourceOutline = source != null
            ? source.GetComponent<Outline>()
            : null;
        Outline targetOutline = target.AddComponent<Outline>();
        targetOutline.effectColor = sourceOutline != null
            ? sourceOutline.effectColor
            : new Color(0.12f, 0.07f, 0.035f, 1f);
        targetOutline.effectDistance = sourceOutline != null
            ? sourceOutline.effectDistance
            : new Vector2(2f, -2f);
        targetOutline.useGraphicAlpha = sourceOutline != null
            && sourceOutline.useGraphicAlpha;

        Shadow sourceShadow = null;
        if (source != null)
        {
            Shadow[] shadows = source.GetComponents<Shadow>();
            for (int index = 0; index < shadows.Length; index++)
            {
                if (shadows[index] != null
                    && shadows[index].GetType() == typeof(Shadow))
                {
                    sourceShadow = shadows[index];
                    break;
                }
            }
        }

        Shadow targetShadow = target.AddComponent<Shadow>();
        targetShadow.effectColor = sourceShadow != null
            ? sourceShadow.effectColor
            : new Color(0f, 0f, 0f, 0.5f);
        targetShadow.effectDistance = sourceShadow != null
            ? sourceShadow.effectDistance
            : new Vector2(3f, -4f);
        targetShadow.useGraphicAlpha = sourceShadow == null
            || sourceShadow.useGraphicAlpha;
    }

    private static string FormatMoney(long cents)
    {
        return $"${cents / 100:N0}.{Mathf.Abs((int)(cents % 100)):D2}";
    }
}
