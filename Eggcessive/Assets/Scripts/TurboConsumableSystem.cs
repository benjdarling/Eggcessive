using System;
using UnityEngine;

public static class TurboConsumableSystem
{
    public enum TurboType
    {
        Incubator,
        Crosshatcher,
        Robot
    }

    public enum UpgradeKind
    {
        Power,
        Duration
    }

    private static readonly float[] BoostPercentByLevel =
    {
        50f, 75f, 100f, 130f, 165f, 200f
    };

    private static readonly float[] DurationSecondsByLevel =
    {
        5f, 10f, 15f, 20f, 25f, 30f
    };

    private static readonly long[,] PurchaseCostsByTier =
    {
        {
            2000L, 5000L, 12500L,
            32000L, 80000L, 200000L
        },
        {
            4000L, 10000L, 25000L,
            64000L, 160000L, 400000L
        },
        {
            8000L, 20000L, 50000L,
            128000L, 320000L, 800000L
        }
    };

    private static readonly long[,] PowerUpgradeCosts =
    {
        { 1000000L, 7500000L, 60000000L, 500000000L, 5000000000L },
        { 2500000L, 20000000L, 150000000L, 1200000000L, 12000000000L },
        { 5000000L, 40000000L, 300000000L, 2500000000L, 25000000000L }
    };

    private static readonly long[,] DurationUpgradeCosts =
    {
        { 800000L, 6000000L, 50000000L, 400000000L, 4000000000L },
        { 2000000L, 15000000L, 120000000L, 1000000000L, 10000000000L },
        { 4000000L, 30000000L, 250000000L, 2000000000L, 20000000000L }
    };

    private static readonly int[] Inventory = new int[3];
    private static readonly int[] TotalPurchased = new int[3];
    private static readonly int[] PowerLevels = new int[3];
    private static readonly int[] DurationLevels = new int[3];
    private static readonly double[] ActiveUntil = new double[3];

    public const int MaximumPowerLevel = 5;
    public const int MaximumDurationLevel = 5;

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Array.Clear(Inventory, 0, Inventory.Length);
        Array.Clear(TotalPurchased, 0, TotalPurchased.Length);
        Array.Clear(PowerLevels, 0, PowerLevels.Length);
        Array.Clear(DurationLevels, 0, DurationLevels.Length);
        Array.Clear(ActiveUntil, 0, ActiveUntil.Length);
        Changed = null;
    }

    public static int GetInventory(TurboType type) => Inventory[(int)type];
    public static int GetTotalPurchased(TurboType type) =>
        TotalPurchased[(int)type];
    public static int GetPowerLevel(TurboType type) => PowerLevels[(int)type];
    public static int GetDurationLevel(TurboType type) =>
        DurationLevels[(int)type];
    public static float GetBoostPercent(TurboType type) =>
        BoostPercentByLevel[Mathf.Clamp(
            GetPowerLevel(type),
            0,
            MaximumPowerLevel)];
    public static float GetDurationSeconds(TurboType type) =>
        DurationSecondsByLevel[Mathf.Clamp(
            GetDurationLevel(type),
            0,
            MaximumDurationLevel)];
    public static float GetRemainingSeconds(TurboType type) =>
        Mathf.Max(0f, (float)(ActiveUntil[(int)type]
            - Time.realtimeSinceStartupAsDouble));
    public static bool IsActive(TurboType type) =>
        GetRemainingSeconds(type) > 0f;
    public static float GetProductivityMultiplier(TurboType type) =>
        IsActive(type) ? 1f + GetBoostPercent(type) * 0.01f : 1f;
    public static int GetConsumableTier(TurboType type) =>
        Mathf.Max(GetPowerLevel(type), GetDurationLevel(type));
    public static long GetPurchaseCost(TurboType type) =>
        PurchaseCostsByTier[
            (int)type,
            Mathf.Clamp(GetConsumableTier(type), 0, MaximumPowerLevel)];

    public static long GetUpgradeCost(
        TurboType type,
        UpgradeKind kind,
        int currentLevel)
    {
        int maximum = kind == UpgradeKind.Power
            ? MaximumPowerLevel
            : MaximumDurationLevel;
        if (currentLevel < 0 || currentLevel >= maximum)
        {
            return 0L;
        }

        return kind == UpgradeKind.Power
            ? PowerUpgradeCosts[(int)type, currentLevel]
            : DurationUpgradeCosts[(int)type, currentLevel];
    }

    public static void AddConsumable(TurboType type)
    {
        int index = (int)type;
        Inventory[index]++;
        TotalPurchased[index]++;
        Changed?.Invoke();
    }

    public static bool TryUpgrade(
        TurboType type,
        UpgradeKind kind,
        out string message)
    {
        int index = (int)type;
        int[] levels = kind == UpgradeKind.Power
            ? PowerLevels
            : DurationLevels;
        int maximum = kind == UpgradeKind.Power
            ? MaximumPowerLevel
            : MaximumDurationLevel;
        if (levels[index] >= maximum)
        {
            message = $"{GetDisplayName(type)} turbo {kind.ToString().ToLowerInvariant()} is maxed";
            return false;
        }

        levels[index]++;
        message = kind == UpgradeKind.Power
            ? $"{GetDisplayName(type)} turbo now boosts productivity by {GetBoostPercent(type):0}%"
            : $"{GetDisplayName(type)} turbo now lasts {GetDurationSeconds(type):0} seconds";
        Changed?.Invoke();
        return true;
    }

    public static bool TryActivate(TurboType type, out string message)
    {
        if (RoundSystem.Instance != null
            && !RoundSystem.Instance.IsRoundInProgress)
        {
            message = "Turbos can only be used during a round";
            return false;
        }

        if (!HasApplicableMachine(type))
        {
            message = $"Install a {GetDisplayName(type).ToLowerInvariant()} first";
            return false;
        }

        int index = (int)type;
        if (Inventory[index] <= 0)
        {
            message = $"Buy a {GetDisplayName(type)} Turbo in the supplies shop";
            return false;
        }

        Inventory[index]--;
        double now = Time.realtimeSinceStartupAsDouble;
        ActiveUntil[index] = Math.Max(now, ActiveUntil[index])
            + GetDurationSeconds(type);
        message = $"{GetDisplayName(type)} Turbo: +{GetBoostPercent(type):0}% for {GetRemainingSeconds(type):0}s";
        Changed?.Invoke();
        return true;
    }

    public static bool HasApplicableMachine(TurboType type)
    {
        return type switch
        {
            TurboType.Incubator =>
                UnityEngine.Object.FindAnyObjectByType<IncubatorController>()
                    != null,
            TurboType.Crosshatcher =>
                UnityEngine.Object.FindAnyObjectByType<CrosshatcherController>()
                    != null,
            TurboType.Robot => EggCollectorRobot.ActiveInstances.Count > 0,
            _ => false
        };
    }

    public static string GetDisplayName(TurboType type)
    {
        return type switch
        {
            TurboType.Incubator => "Incubator",
            TurboType.Crosshatcher => "Crosshatcher",
            TurboType.Robot => "Robot",
            _ => "Machine"
        };
    }

    public static string GetResourcePath(TurboType type)
    {
        return type switch
        {
            TurboType.Incubator => "UI/TurboIcons/TurboIncubator",
            TurboType.Crosshatcher => "UI/TurboIcons/TurboCrosshatcher",
            TurboType.Robot => "UI/TurboIcons/TurboRobot",
            _ => string.Empty
        };
    }
}
