using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class IncubatorShopController : MonoBehaviour
{
    private static readonly int[] LevelCosts =
    {
        400, 1000, 2200, 4500, 8000, 13000, 20000, 30000, 44000, 62000
    };

    private static readonly int[] CapacityCosts =
    {
        1000, 2600, 6500, 15000, 34000, 76000, 170000, 380000, 850000
    };

    private static readonly int[] SpeedCosts =
    {
        1400, 3800, 9500, 23000, 55000, 130000, 310000, 740000, 1800000
    };

    [Header("Scene Incubator")]
    [SerializeField] private IncubatorController incubator = null;

    [Header("Authored HUD")]
    [SerializeField] private Button purchaseButton = null;
    [SerializeField] private TMP_Text levelText = null;
    [SerializeField] private TMP_Text detailsText = null;
    [SerializeField] private TMP_Text statusText = null;
    [SerializeField] private TMP_Text purchaseButtonText = null;
    [SerializeField] private Image affordabilityProgressFill = null;

    [Header("Prices")]
    [SerializeField, Min(1)] private int levelOneCostCents = 500;
    [SerializeField, Min(1)] private int levelTwoCostCents = 1500;

    private int PurchasedLevel =>
        incubator != null && incubator.gameObject.activeSelf
            ? incubator.CurrentLevel
            : 0;

    public static IncubatorShopController Instance { get; private set; }
    public bool IsInstalled => PurchasedLevel > 0;
    public int InstallCost => LevelCosts[0];
    public int CurrentLevel => PurchasedLevel;
    public int CapacityLevel => IsInstalled ? incubator.CapacityLevel : 0;
    public int SpeedLevel => IsInstalled ? incubator.SpeedLevel : 0;
    public int NextCapacityCost =>
        IsInstalled && CapacityLevel < IncubatorController.MaximumLevel
            ? CapacityCosts[CapacityLevel - 1]
            : 0;
    public int NextSpeedCost =>
        IsInstalled && SpeedLevel < IncubatorController.MaximumLevel
            ? SpeedCosts[SpeedLevel - 1]
            : 0;
    public bool HasUpgrade => PurchasedLevel < IncubatorController.MaximumLevel;
    public int NextLevel => Mathf.Min(
        PurchasedLevel + 1,
        IncubatorController.MaximumLevel);
    public int NextUpgradeCost => HasUpgrade ? GetCost(NextLevel) : 0;
    public int NextCapacity => IncubatorController.GetCapacity(NextLevel);
    public float NextProductionTime => IncubatorController.GetProductionTime(NextLevel);

    public int NextSplitCapacity => IncubatorController.GetCapacity(
        Mathf.Clamp(CapacityLevel + 1, 1, IncubatorController.MaximumLevel));
    public float NextSplitProductionTime => IncubatorController.GetProductionTime(
        Mathf.Clamp(SpeedLevel + 1, 1, IncubatorController.MaximumLevel));

    public int GetCapacityUpgradeCost(int targetLevel)
    {
        int index = Mathf.Clamp(targetLevel, 2, IncubatorController.MaximumLevel) - 2;
        return CapacityCosts[index];
    }

    public int GetSpeedUpgradeCost(int targetLevel)
    {
        int index = Mathf.Clamp(targetLevel, 2, IncubatorController.MaximumLevel) - 2;
        return SpeedCosts[index];
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        if (purchaseButton != null)
        {
            purchaseButton.onClick.AddListener(PurchaseNextLevel);
        }

        EggScoreHud.BalanceChanged += HandleBalanceChanged;
    }

    private void Start()
    {
        RefreshUi();
    }

    private void OnDisable()
    {
        if (purchaseButton != null)
        {
            purchaseButton.onClick.RemoveListener(PurchaseNextLevel);
        }

        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void PurchaseNextLevel()
    {
        TryPurchaseNextLevel(out string message);
        SetStatus(message);
    }

    public bool TryPurchaseNextLevel(out string message)
    {
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsSuppliesShopOpen)
        {
            message = "Incubator upgrades are sold between rounds";
            return false;
        }

        if (incubator == null)
        {
            message = "Incubator is not connected";
            return false;
        }

        int nextLevel = PurchasedLevel + 1;

        if (nextLevel > IncubatorController.MaximumLevel)
        {
            message = "Maximum incubator level";
            return false;
        }

        int cost = GetCost(nextLevel);

        if (!EggScoreHud.TrySpendCents(cost))
        {
            message = $"Need {FormatMoney(cost)}";
            return false;
        }

        incubator.InstallOrUpgrade(nextLevel);
        message = nextLevel == 1 ? "Incubator installed" : $"Incubator level {nextLevel}";
        RefreshUi();
        return true;
    }

    public bool TryInstall(out string message, bool spendCurrency = true)
    {
        if (incubator == null)
        {
            message = "Incubator is not connected";
            return false;
        }

        if (IsInstalled)
        {
            message = "Incubator already installed";
            return false;
        }

        if (spendCurrency && !EggScoreHud.TrySpendCents(InstallCost))
        {
            message = $"Need {FormatMoney(InstallCost)}";
            return false;
        }

        incubator.InstallOrUpgrade(1, 1);
        message = "Incubator installed";
        RefreshUi();
        return true;
    }

    public bool TryUpgradeCapacity(out string message, bool spendCurrency = true)
    {
        if (!IsInstalled || CapacityLevel >= IncubatorController.MaximumLevel)
        {
            message = IsInstalled ? "Maximum incubator capacity" : "Install the incubator first";
            return false;
        }

        int cost = NextCapacityCost;

        if (spendCurrency && !EggScoreHud.TrySpendCents(cost))
        {
            message = $"Need {FormatMoney(cost)}";
            return false;
        }

        incubator.InstallOrUpgrade(CapacityLevel + 1, SpeedLevel);
        message = $"Incubator capacity level {CapacityLevel}";
        RefreshUi();
        return true;
    }

    public bool TryUpgradeSpeed(out string message, bool spendCurrency = true)
    {
        if (!IsInstalled || SpeedLevel >= IncubatorController.MaximumLevel)
        {
            message = IsInstalled ? "Maximum incubator speed" : "Install the incubator first";
            return false;
        }

        int cost = NextSpeedCost;

        if (spendCurrency && !EggScoreHud.TrySpendCents(cost))
        {
            message = $"Need {FormatMoney(cost)}";
            return false;
        }

        incubator.InstallOrUpgrade(CapacityLevel, SpeedLevel + 1);
        message = $"Incubator speed level {SpeedLevel}";
        RefreshUi();
        return true;
    }

    private void HandleBalanceChanged(int _)
    {
        RefreshUi();
    }

    private void RefreshUi()
    {
        int level = PurchasedLevel;

        if (levelText != null)
        {
            levelText.text = level == 0 ? "NOT INSTALLED" : $"LEVEL {level}";
        }

        if (detailsText != null)
        {
            int shownLevel = level < IncubatorController.MaximumLevel ? level + 1 : level;
            detailsText.text =
                $"{(level < IncubatorController.MaximumLevel ? "Next: " : string.Empty)}" +
                $"{IncubatorController.GetCapacity(shownLevel)} eggs  |  " +
                $"{IncubatorController.GetProductionTime(shownLevel):0.##} sec";
        }

        bool hasUpgrade = level < IncubatorController.MaximumLevel;
        int cost = hasUpgrade ? GetCost(level + 1) : 0;

        if (purchaseButton != null)
        {
            purchaseButton.interactable = hasUpgrade;
        }

        if (purchaseButtonText != null)
        {
            purchaseButtonText.text = hasUpgrade
                ? $"{(level == 0 ? "BUY" : "UPGRADE")}  {FormatMoney(cost)}"
                : "MAX LEVEL";
        }

        if (affordabilityProgressFill != null)
        {
            affordabilityProgressFill.fillAmount = hasUpgrade
                ? Mathf.Clamp01(EggScoreHud.CurrentCents / (float)cost)
                : 1f;
        }
    }

    private int GetCost(int level)
    {
        return LevelCosts[Mathf.Clamp(level, 1, IncubatorController.MaximumLevel) - 1];
    }

    private static string FormatMoney(int cents)
    {
        return $"${cents / 100:N0}.{Mathf.Abs(cents % 100):D2}";
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void OnValidate()
    {
        levelOneCostCents = Mathf.Max(1, levelOneCostCents);
        levelTwoCostCents = Mathf.Max(1, levelTwoCostCents);
    }
}
