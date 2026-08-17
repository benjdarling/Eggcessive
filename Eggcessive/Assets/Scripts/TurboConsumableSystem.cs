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

    private static readonly long[] PurchaseCostsByTier =
    {
        2000L, 10000L, 50000L,
        250000L, 1250000L, 6000000L
    };

    private static readonly long[] PowerUpgradeCosts =
    {
        1000000L, 7500000L, 60000000L, 500000000L, 5000000000L
    };

    private static readonly long[] DurationUpgradeCosts =
    {
        800000L, 6000000L, 50000000L, 400000000L, 4000000000L
    };

    // TurboType remains in the public API so existing machine call sites and
    // serialized upgrade ids stay compatible. All types now share one stock,
    // upgrade track, and active timer.
    private static int inventory;
    private static int totalPurchased;
    private static int powerLevel;
    private static int durationLevel;
    private static double activeUntil;

    public const int MaximumPowerLevel = 5;
    public const int MaximumDurationLevel = 5;

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        inventory = 0;
        totalPurchased = 0;
        powerLevel = 0;
        durationLevel = 0;
        activeUntil = 0d;
        Changed = null;
    }

    public static int GetInventory(TurboType type) => inventory;
    public static int GetTotalPurchased(TurboType type) => totalPurchased;
    public static int GetPowerLevel(TurboType type) => powerLevel;
    public static int GetDurationLevel(TurboType type) => durationLevel;
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
        Mathf.Max(0f, (float)(activeUntil
            - Time.realtimeSinceStartupAsDouble));
    public static bool IsActive(TurboType type) =>
        GetRemainingSeconds(type) > 0f;
    public static float GetProductivityMultiplier(TurboType type) =>
        IsActive(type) ? 1f + GetBoostPercent(type) * 0.01f : 1f;
    public static int GetConsumableTier(TurboType type) =>
        Mathf.Max(GetPowerLevel(type), GetDurationLevel(type));
    public static long GetPurchaseCost(TurboType type) =>
        PurchaseCostsByTier[
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
            ? PowerUpgradeCosts[currentLevel]
            : DurationUpgradeCosts[currentLevel];
    }

    public static void AddConsumable(TurboType type)
    {
        inventory++;
        totalPurchased++;
        Changed?.Invoke();
    }

    public static bool TryUpgrade(
        TurboType type,
        UpgradeKind kind,
        out string message)
    {
        int maximum = kind == UpgradeKind.Power
            ? MaximumPowerLevel
            : MaximumDurationLevel;
        int level = kind == UpgradeKind.Power ? powerLevel : durationLevel;
        if (level >= maximum)
        {
            message = $"{GetDisplayName(type)} turbo {kind.ToString().ToLowerInvariant()} is maxed";
            return false;
        }

        if (kind == UpgradeKind.Power)
        {
            powerLevel++;
        }
        else
        {
            durationLevel++;
        }
        message = kind == UpgradeKind.Power
            ? $"Turbo now boosts all machines by {GetBoostPercent(type):0}%"
            : $"Turbo now lasts {GetDurationSeconds(type):0} seconds";
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
            message = "Install a machine first";
            return false;
        }

        if (inventory <= 0)
        {
            message = "Buy a Turbo in the supplies shop";
            return false;
        }

        inventory--;
        double now = Time.realtimeSinceStartupAsDouble;
        activeUntil = Math.Max(now, activeUntil)
            + GetDurationSeconds(type);
        message = $"Turbo: all machines +{GetBoostPercent(type):0}% for {GetRemainingSeconds(type):0}s";
        Changed?.Invoke();
        return true;
    }

    public static bool HasApplicableMachine(TurboType type)
    {
        return UnityEngine.Object.FindAnyObjectByType<IncubatorController>()
                != null
            || UnityEngine.Object.FindAnyObjectByType<CrosshatcherController>()
                != null
            || EggCollectorRobot.ActiveInstances.Count > 0;
    }

    public static string GetDisplayName(TurboType type)
    {
        return "Universal";
    }

    public static string GetResourcePath(TurboType type)
    {
        return "UI/TurboIcons/TurboIncubator";
    }
}
