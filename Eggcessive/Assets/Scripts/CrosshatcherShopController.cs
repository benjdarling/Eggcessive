using UnityEngine;

[DisallowMultipleComponent]
public sealed class CrosshatcherShopController : MonoBehaviour
{
    private static readonly int[] SpeedCosts =
    {
        22000, 50000, 110000, 240000, 520000,
        1100000, 2300000, 4800000, 10000000
    };

    private static readonly int[] QualityCosts =
    {
        30000, 70000, 160000, 360000, 800000,
        1750000, 3800000, 8200000, 18000000
    };

    private const int InstallationCost = 15000;

    [SerializeField] private CrosshatcherController crosshatcher = null;

    public static CrosshatcherShopController Instance { get; private set; }
    public bool IsInstalled =>
        crosshatcher != null && crosshatcher.gameObject.activeSelf;
    public int InstallCost => InstallationCost;
    public int SpeedLevel => IsInstalled ? crosshatcher.SpeedLevel : 0;
    public int QualityLevel => IsInstalled ? crosshatcher.QualityLevel : 0;
    public int NextSpeedCost =>
        IsInstalled && SpeedLevel < CrosshatcherController.MaximumLevel
            ? GetSpeedUpgradeCost(SpeedLevel + 1)
            : 0;
    public int NextQualityCost =>
        IsInstalled && QualityLevel < CrosshatcherController.MaximumLevel
            ? GetQualityUpgradeCost(QualityLevel + 1)
            : 0;
    public float NextProcessingTime => CrosshatcherController.GetProcessingTime(
        Mathf.Clamp(SpeedLevel + 1, 1, CrosshatcherController.MaximumLevel));
    public float NextImprovementChance => CrosshatcherController.GetImprovementChance(
        Mathf.Clamp(QualityLevel + 1, 1, CrosshatcherController.MaximumLevel));

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    public int GetSpeedUpgradeCost(int targetLevel)
    {
        return SpeedCosts[
            Mathf.Clamp(targetLevel, 2, CrosshatcherController.MaximumLevel) - 2];
    }

    public int GetQualityUpgradeCost(int targetLevel)
    {
        return QualityCosts[
            Mathf.Clamp(targetLevel, 2, CrosshatcherController.MaximumLevel) - 2];
    }

    public bool TryInstall(out string message, bool spendCurrency = true)
    {
        if (crosshatcher == null)
        {
            message = "Crosshatcher is not connected";
            return false;
        }

        if (IsInstalled)
        {
            message = "Crosshatcher already installed";
            return false;
        }

        if (spendCurrency && !EggScoreHud.TrySpendCents(InstallCost))
        {
            message = $"Need {FormatMoney(InstallCost)}";
            return false;
        }

        crosshatcher.InstallOrUpgrade(1, 1);
        PenExpansionManager.Instance?.SynchronizeEquipmentAcrossPens();
        message = "Crosshatcher installed";

        if (spendCurrency)
        {
            RoundSystem.Instance?.PlayCashRegisterSfx();
        }

        return true;
    }

    public bool TryUpgradeSpeed(out string message, bool spendCurrency = true)
    {
        if (!IsInstalled || SpeedLevel >= CrosshatcherController.MaximumLevel)
        {
            message = IsInstalled
                ? "Maximum crosshatcher speed"
                : "Install the crosshatcher first";
            return false;
        }

        int cost = NextSpeedCost;

        if (spendCurrency && !EggScoreHud.TrySpendCents(cost))
        {
            message = $"Need {FormatMoney(cost)}";
            return false;
        }

        crosshatcher.InstallOrUpgrade(SpeedLevel + 1, QualityLevel);
        PenExpansionManager.Instance?.SynchronizeEquipmentAcrossPens();
        message = $"Crosshatcher speed level {SpeedLevel}";

        if (spendCurrency)
        {
            RoundSystem.Instance?.PlayCashRegisterSfx();
        }

        return true;
    }

    public bool TryUpgradeQuality(out string message, bool spendCurrency = true)
    {
        if (!IsInstalled || QualityLevel >= CrosshatcherController.MaximumLevel)
        {
            message = IsInstalled
                ? "Maximum crosshatcher quality"
                : "Install the crosshatcher first";
            return false;
        }

        int cost = NextQualityCost;

        if (spendCurrency && !EggScoreHud.TrySpendCents(cost))
        {
            message = $"Need {FormatMoney(cost)}";
            return false;
        }

        crosshatcher.InstallOrUpgrade(SpeedLevel, QualityLevel + 1);
        PenExpansionManager.Instance?.SynchronizeEquipmentAcrossPens();
        message = $"Crosshatcher quality level {QualityLevel}";

        if (spendCurrency)
        {
            RoundSystem.Instance?.PlayCashRegisterSfx();
        }

        return true;
    }

    private static string FormatMoney(int cents)
    {
        return $"${cents / 100:N0}.{Mathf.Abs(cents % 100):D2}";
    }
}
