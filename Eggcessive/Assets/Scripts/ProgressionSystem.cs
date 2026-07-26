using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProgressionSystem : MonoBehaviour
{
    public enum UpgradeId
    {
        FoodBag,
        FeedSpeed,
        RareEggChance,
        EggValue,
        IncubatorInstall,
        IncubatorCapacity,
        IncubatorSpeed,
        BasketCapacity,
        VacuumPower,
        VacuumRange,
        RobotUnlock,
        RobotSpeed,
        RobotCapacity,
        RobotSmartness
    }

    public readonly struct NodeState
    {
        public NodeState(
            string title,
            string icon,
            string details,
            int level,
            int maximumLevel,
            int cost,
            bool visible,
            bool prerequisiteMet)
        {
            Title = title;
            Icon = icon;
            Details = details;
            Level = level;
            MaximumLevel = maximumLevel;
            Cost = cost;
            Visible = visible;
            PrerequisiteMet = prerequisiteMet;
        }

        public string Title { get; }
        public string Icon { get; }
        public string Details { get; }
        public int Level { get; }
        public int MaximumLevel { get; }
        public int Cost { get; }
        public bool Visible { get; }
        public bool PrerequisiteMet { get; }
        public bool IsMaxed => MaximumLevel > 0 && Level >= MaximumLevel;
        public bool IsRepeatable => MaximumLevel <= 0;
    }

    private static readonly int[] RareChanceCosts =
    {
        1200, 3500, 9000, 22000, 55000, 140000, 350000, 900000
    };

    private static readonly float[] SilverChanceByLevel =
    {
        0f, 0.00025f, 0.00075f, 0.002f, 0.005f,
        0.0125f, 0.03f, 0.065f, 0.12f
    };

    private static readonly float[] GoldChanceByLevel =
    {
        0f, 0f, 0.00005f, 0.0002f, 0.0005f,
        0.0015f, 0.004f, 0.012f, 0.03f
    };

    private static readonly float[] GalaxyChanceByLevel =
    {
        0f, 0f, 0f, 0f, 0.00002f,
        0.0001f, 0.0005f, 0.002f, 0.0075f
    };

    private static readonly int[] EggValueCosts =
    {
        2500, 7500, 20000, 60000, 180000, 550000, 1600000, 5000000
    };

    private static readonly float[] EggValueMultipliers =
    {
        1f, 1.5f, 2.25f, 3.5f, 5.5f, 8.5f, 13f, 20f, 30f
    };

    private static readonly int[] BasketCosts = { 800, 1800, 4200 };
    private static readonly int[] VacuumPowerCosts = { 9000, 28000, 85000 };
    private static readonly int[] VacuumRangeCosts = { 14000, 42000, 130000 };
    private static readonly int[] RobotSpeedCosts = { 180000, 520000, 1600000 };
    private static readonly int[] RobotCapacityCosts = { 240000, 750000, 2400000 };
    private static readonly int[] RobotSmartCosts = { 600000, 3500000 };
    private const int RobotUnlockCost = 120000;

    [SerializeField, Range(0, 8)] private int rareEggChanceLevel;
    [SerializeField, Range(0, 8)] private int eggValueLevel;

    public static ProgressionSystem Instance { get; private set; }
    public static event Action Changed;

    public int RareEggChanceLevel => rareEggChanceLevel;
    public int EggValueLevel => eggValueLevel;
    public float EggValueMultiplier =>
        EggValueMultipliers[Mathf.Clamp(eggValueLevel, 0, EggValueMultipliers.Length - 1)];

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
        Changed = null;
    }

    public NodeState GetNodeState(UpgradeId id)
    {
        FoodShopController food = FoodShopController.Instance;
        IncubatorShopController incubator = IncubatorShopController.Instance;
        EggCarryController collection = EggCarryController.Instance;
        int feedLevel = food != null ? food.UnlockedFeedTier : 1;
        bool installed = incubator != null && incubator.IsInstalled;
        int basketLevel = collection != null ? collection.BasketUpgradeLevel : 0;
        int vacuumPower = collection != null ? collection.VacuumPowerLevel : 0;
        int vacuumRange = collection != null ? collection.VacuumRangeLevel : 0;
        bool robotUnlocked = collection != null && collection.HasRobot;
        int robotSpeed = collection != null ? collection.RobotSpeedLevel : 0;
        int robotCapacity = collection != null ? collection.RobotCapacityLevel : 0;
        int smartness = collection != null ? collection.RobotSmartnessLevel : 0;

        switch (id)
        {
            case UpgradeId.FoodBag:
                return new NodeState(
                    "Feed Bag",
                    "F",
                    food != null
                        ? $"{food.CurrentFeedName} • {food.CurrentFeedSpeedMultiplier:0.##}x production"
                        : "Feed system unavailable",
                    food != null ? food.OwnedFoodCount : 0,
                    0,
                    food != null ? food.CurrentFeedBagCost : 0,
                    true,
                    food != null);

            case UpgradeId.FeedSpeed:
                return new NodeState(
                    "Feed Speed",
                    ">>",
                    food != null && food.HasFeedTierUpgrade
                        ? $"Next feed: {food.NextFeedName} • {food.NextFeedSpeedMultiplier:0.##}x"
                        : "Maximum production feed",
                    feedLevel,
                    FoodShopController.MaximumFeedTier,
                    food != null ? food.NextFeedTierUnlockCost : 0,
                    true,
                    food != null);

            case UpgradeId.RareEggChance:
                return new NodeState(
                    "Premium Eggs",
                    "*",
                    GetRareChanceDescription(rareEggChanceLevel + 1),
                    rareEggChanceLevel,
                    RareChanceCosts.Length,
                    GetArrayCost(RareChanceCosts, rareEggChanceLevel),
                    food != null,
                    food != null);

            case UpgradeId.EggValue:
                return new NodeState(
                    "Egg Value",
                    "$",
                    $"All eggs worth {GetEggValueMultiplier(eggValueLevel + 1):0.##}x",
                    eggValueLevel,
                    EggValueCosts.Length,
                    GetArrayCost(EggValueCosts, eggValueLevel),
                    rareEggChanceLevel >= 2,
                    rareEggChanceLevel >= 2);

            case UpgradeId.IncubatorInstall:
                return new NodeState(
                    "Incubator",
                    "I",
                    installed ? "Installed and operational" : "Unlock automated hatching",
                    installed ? 1 : 0,
                    1,
                    incubator != null ? incubator.InstallCost : 0,
                    true,
                    incubator != null);

            case UpgradeId.IncubatorCapacity:
                return new NodeState(
                    "Capacity",
                    "[]",
                    incubator != null
                        ? $"Next: {incubator.NextSplitCapacity} egg slots"
                        : "Incubator unavailable",
                    incubator != null ? incubator.CapacityLevel : 0,
                    IncubatorController.MaximumLevel,
                    incubator != null ? incubator.NextCapacityCost : 0,
                    installed,
                    installed);

            case UpgradeId.IncubatorSpeed:
                return new NodeState(
                    "Hatch Speed",
                    "T",
                    incubator != null
                        ? $"Next: {incubator.NextSplitProductionTime:0.##} sec per chicken"
                        : "Incubator unavailable",
                    incubator != null ? incubator.SpeedLevel : 0,
                    IncubatorController.MaximumLevel,
                    incubator != null ? incubator.NextSpeedCost : 0,
                    installed,
                    installed);

            case UpgradeId.BasketCapacity:
                return new NodeState(
                    "Egg Basket",
                    "B",
                    basketLevel >= 3
                        ? "5 egg capacity"
                        : $"Next: {new[] { 3, 4, 5 }[Mathf.Clamp(basketLevel, 0, 2)]} egg capacity",
                    basketLevel,
                    3,
                    GetArrayCost(BasketCosts, basketLevel),
                    true,
                    collection != null);

            case UpgradeId.VacuumPower:
                return new NodeState(
                    "Vacuum Power",
                    "V",
                    vacuumPower == 0 ? "Unlock click-hold suction" : "Faster egg suction",
                    vacuumPower,
                    3,
                    GetArrayCost(VacuumPowerCosts, vacuumPower),
                    basketLevel >= 1,
                    collection != null && basketLevel >= 1);

            case UpgradeId.VacuumRange:
                return new NodeState(
                    "Vacuum Range",
                    "<>",
                    "Wider and longer suction cone",
                    vacuumRange,
                    3,
                    GetArrayCost(VacuumRangeCosts, vacuumRange),
                    vacuumPower >= 1,
                    collection != null && vacuumPower >= 1);

            case UpgradeId.RobotUnlock:
                return new NodeState(
                    "Collector Bot",
                    "R",
                    robotUnlocked ? "Works alongside your collection tools" : "Unlock autonomous collection",
                    robotUnlocked ? 1 : 0,
                    1,
                    RobotUnlockCost,
                    basketLevel >= 1,
                    collection != null && basketLevel >= 1);

            case UpgradeId.RobotSpeed:
                return new NodeState(
                    "Robot Speed",
                    ">>",
                    "Faster collection and deliveries",
                    robotSpeed,
                    3,
                    GetArrayCost(RobotSpeedCosts, robotSpeed),
                    robotUnlocked,
                    robotUnlocked);

            case UpgradeId.RobotCapacity:
                return new NodeState(
                    "Robot Capacity",
                    "R+",
                    robotCapacity >= 3 ? "24 egg capacity" : "Carry more eggs per trip",
                    robotCapacity,
                    3,
                    GetArrayCost(RobotCapacityCosts, robotCapacity),
                    robotUnlocked,
                    robotUnlocked);

            case UpgradeId.RobotSmartness:
                return new NodeState(
                    "Robot Logic",
                    "AI",
                    smartness == 0
                        ? "Route spare eggs to incubator"
                        : "Prioritise rare eggs and open incubator slots",
                    smartness,
                    2,
                    GetArrayCost(RobotSmartCosts, smartness),
                    robotUnlocked,
                    robotUnlocked);

            default:
                return default;
        }
    }

    public NodeState GetNodeState(UpgradeId id, int targetLevel)
    {
        if (targetLevel <= 0)
        {
            return GetNodeState(id);
        }

        FoodShopController food = FoodShopController.Instance;
        IncubatorShopController incubator = IncubatorShopController.Instance;
        EggCarryController collection = EggCarryController.Instance;
        bool installed = incubator != null && incubator.IsInstalled;

        switch (id)
        {
            case UpgradeId.FeedSpeed:
            {
                int target = Mathf.Clamp(targetLevel, 2, FoodShopController.MaximumFeedTier);
                int current = food != null ? food.UnlockedFeedTier : 0;
                return new NodeState(
                    $"Feed Speed: {food?.GetFeedName(target) ?? $"Tier {target}"}",
                    "F",
                    food != null
                        ? $"{food.GetFeedSpeedMultiplier(target):0.##}x egg production"
                        : "Feed system unavailable",
                    current,
                    target,
                    food != null ? food.GetFeedUnlockCost(target) : 0,
                    true,
                    food != null && current >= target - 1);
            }
            case UpgradeId.RareEggChance:
            {
                int target = Mathf.Clamp(targetLevel, 1, RareChanceCosts.Length);
                return new NodeState(
                    $"Premium Eggs Tier {target}",
                    "*",
                    GetRareChanceDescription(target),
                    rareEggChanceLevel,
                    target,
                    GetArrayCost(RareChanceCosts, target - 1),
                    true,
                    food != null && rareEggChanceLevel >= target - 1);
            }
            case UpgradeId.EggValue:
            {
                int target = Mathf.Clamp(targetLevel, 1, EggValueCosts.Length);
                return new NodeState(
                    $"Egg Value Tier {target}",
                    "$",
                    $"All eggs worth {GetEggValueMultiplier(target):0.##}x",
                    eggValueLevel,
                    target,
                    GetArrayCost(EggValueCosts, target - 1),
                    true,
                    rareEggChanceLevel >= 2 && eggValueLevel >= target - 1);
            }
            case UpgradeId.IncubatorCapacity:
            {
                int current = incubator != null ? incubator.CapacityLevel : 0;
                int target = Mathf.Clamp(targetLevel, 2, IncubatorController.MaximumLevel);
                return new NodeState(
                    $"Capacity Tier {target}",
                    "C",
                    $"{IncubatorController.GetCapacity(target)} simultaneous egg slots",
                    current,
                    target,
                    incubator != null ? incubator.GetCapacityUpgradeCost(target) : 0,
                    true,
                    installed && current >= target - 1);
            }
            case UpgradeId.IncubatorSpeed:
            {
                int current = incubator != null ? incubator.SpeedLevel : 0;
                int target = Mathf.Clamp(targetLevel, 2, IncubatorController.MaximumLevel);
                return new NodeState(
                    $"Hatch Speed Tier {target}",
                    "S",
                    $"{IncubatorController.GetProductionTime(target):0.##} seconds per chicken",
                    current,
                    target,
                    incubator != null ? incubator.GetSpeedUpgradeCost(target) : 0,
                    true,
                    installed && current >= target - 1);
            }
            case UpgradeId.BasketCapacity:
            {
                int current = collection != null ? collection.BasketUpgradeLevel : 0;
                int target = Mathf.Clamp(targetLevel, 1, 3);
                int[] capacities = { 3, 4, 5 };
                return new NodeState(
                    $"Basket Capacity {target}",
                    "B",
                    $"{capacities[target - 1]} egg capacity",
                    current,
                    target,
                    GetArrayCost(BasketCosts, target - 1),
                    true,
                    collection != null && current >= target - 1);
            }
            case UpgradeId.VacuumPower:
            {
                int current = collection != null ? collection.VacuumPowerLevel : 0;
                int target = Mathf.Clamp(targetLevel, 1, 3);
                return new NodeState(
                    $"Vacuum Power {target}",
                    "V",
                    target == 1 ? "Unlock click-hold suction" : "Faster egg suction",
                    current,
                    target,
                    GetArrayCost(VacuumPowerCosts, target - 1),
                    true,
                    collection != null
                        && collection.BasketUpgradeLevel >= 1
                        && current >= target - 1);
            }
            case UpgradeId.VacuumRange:
            {
                int current = collection != null ? collection.VacuumRangeLevel : 0;
                int target = Mathf.Clamp(targetLevel, 1, 3);
                return new NodeState(
                    $"Vacuum Range {target}",
                    "<>",
                    "Wider and longer suction cone",
                    current,
                    target,
                    GetArrayCost(VacuumRangeCosts, target - 1),
                    true,
                    collection != null
                        && collection.VacuumPowerLevel >= 1
                        && current >= target - 1);
            }
            case UpgradeId.RobotSpeed:
            {
                int current = collection != null ? collection.RobotSpeedLevel : 0;
                int target = Mathf.Clamp(targetLevel, 2, 3);
                return new NodeState(
                    $"Robot Speed Upgrade {target - 1}",
                    "R",
                    "Faster collection and deliveries",
                    current,
                    target,
                    GetArrayCost(RobotSpeedCosts, target - 1),
                    true,
                    collection != null && collection.HasRobot && current >= target - 1);
            }
            case UpgradeId.RobotCapacity:
            {
                int current = collection != null ? collection.RobotCapacityLevel : 0;
                int target = Mathf.Clamp(targetLevel, 2, 3);
                int capacity = target == 2 ? 12 : 24;
                return new NodeState(
                    $"Robot Capacity Upgrade {target - 1}",
                    "R+",
                    $"{capacity} egg carrying capacity",
                    current,
                    target,
                    GetArrayCost(RobotCapacityCosts, target - 1),
                    true,
                    collection != null && collection.HasRobot && current >= target - 1);
            }
            case UpgradeId.RobotSmartness:
            {
                int current = collection != null ? collection.RobotSmartnessLevel : 0;
                int target = Mathf.Clamp(targetLevel, 1, 2);
                return new NodeState(
                    $"Robot Logic Upgrade {target}",
                    "AI",
                    target == 1
                        ? "Route spare eggs to the incubator"
                        : "Prioritise valuable eggs and open incubator slots",
                    current,
                    target,
                    GetArrayCost(RobotSmartCosts, target - 1),
                    true,
                    collection != null && collection.HasRobot && current >= target - 1);
            }
            default:
                return GetNodeState(id);
        }
    }

    public bool TryPurchase(UpgradeId id, out string message)
    {
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsSuppliesShopOpen)
        {
            message = "Upgrades are purchased between rounds";
            return false;
        }

        NodeState state = GetNodeState(id);

        if (!state.Visible || !state.PrerequisiteMet)
        {
            message = "Unlock the previous node first";
            return false;
        }

        if (state.IsMaxed)
        {
            message = $"{state.Title} is fully upgraded";
            return false;
        }

        if (id == UpgradeId.FoodBag)
        {
            if (FoodShopController.Instance == null)
            {
                message = "Feed system unavailable";
                return false;
            }

            bool purchased =
                FoodShopController.Instance.TryBuyCurrentFeed(out message);
            NotifyChanged();
            return purchased;
        }

        if (!EggScoreHud.TrySpendCents(state.Cost))
        {
            message = $"Need {FormatMoney(state.Cost)}";
            return false;
        }

        bool applied = ApplyUpgrade(id, out message);

        if (!applied)
        {
            EggScoreHud.AddCents(state.Cost);
            return false;
        }

        NotifyChanged();
        return true;
    }

    public bool TryPurchase(UpgradeId id, int targetLevel, out string message)
    {
        if (targetLevel <= 0)
        {
            return TryPurchase(id, out message);
        }

        NodeState state = GetNodeState(id, targetLevel);
        if (state.IsMaxed)
        {
            message = $"{state.Title} is already owned";
            return false;
        }

        if (!state.PrerequisiteMet)
        {
            message = $"Unlock tier {targetLevel - 1} first";
            return false;
        }

        return TryPurchase(id, out message);
    }

    public ChickenEgg.EggType RollEggType()
    {
        GetRareChances(
            rareEggChanceLevel,
            out float silver,
            out float gold,
            out float galaxy);
        float roll = UnityEngine.Random.value;

        if (roll < galaxy)
        {
            return ChickenEgg.EggType.Galaxy;
        }

        if (roll < galaxy + gold)
        {
            return ChickenEgg.EggType.Gold;
        }

        if (roll < galaxy + gold + silver)
        {
            return ChickenEgg.EggType.Silver;
        }

        return ChickenEgg.EggType.Standard;
    }

    public int GetEggValueCents(ChickenEgg.EggType type)
    {
        int baseValue = type switch
        {
            ChickenEgg.EggType.Silver => 800,
            ChickenEgg.EggType.Gold => 3500,
            ChickenEgg.EggType.Galaxy => 15000,
            _ => 100
        };
        return Mathf.RoundToInt(baseValue * EggValueMultiplier);
    }

    private bool ApplyUpgrade(UpgradeId id, out string message)
    {
        switch (id)
        {
            case UpgradeId.FeedSpeed:
                return FoodShopController.Instance.TryUnlockNextFeedTier(out message, false);
            case UpgradeId.RareEggChance:
                rareEggChanceLevel++;
                message = $"Premium egg chance level {rareEggChanceLevel}";
                return true;
            case UpgradeId.EggValue:
                eggValueLevel++;
                message = $"Egg values increased to {EggValueMultiplier:0.##}x";
                return true;
            case UpgradeId.IncubatorInstall:
                return IncubatorShopController.Instance.TryInstall(out message, false);
            case UpgradeId.IncubatorCapacity:
                return IncubatorShopController.Instance.TryUpgradeCapacity(out message, false);
            case UpgradeId.IncubatorSpeed:
                return IncubatorShopController.Instance.TryUpgradeSpeed(out message, false);
            case UpgradeId.BasketCapacity:
                EggCarryController.Instance.UpgradeBasket();
                message = $"Basket capacity level {EggCarryController.Instance.BasketUpgradeLevel}";
                return true;
            case UpgradeId.VacuumPower:
                EggCarryController.Instance.UpgradeVacuumPower();
                message = $"Vacuum power level {EggCarryController.Instance.VacuumPowerLevel}";
                return true;
            case UpgradeId.VacuumRange:
                EggCarryController.Instance.UpgradeVacuumRange();
                message = $"Vacuum range level {EggCarryController.Instance.VacuumRangeLevel}";
                return true;
            case UpgradeId.RobotUnlock:
                EggCarryController.Instance.UnlockRobot();
                message = "Collector bot online";
                return true;
            case UpgradeId.RobotSpeed:
                EggCarryController.Instance.UpgradeRobotSpeed();
                message = $"Robot speed level {EggCarryController.Instance.RobotSpeedLevel}";
                return true;
            case UpgradeId.RobotCapacity:
                EggCarryController.Instance.UpgradeRobotCapacity();
                message = $"Robot capacity level {EggCarryController.Instance.RobotCapacityLevel}";
                return true;
            case UpgradeId.RobotSmartness:
                EggCarryController.Instance.UpgradeRobotSmartness();
                message = $"Robot logic level {EggCarryController.Instance.RobotSmartnessLevel}";
                return true;
            default:
                message = "Upgrade unavailable";
                return false;
        }
    }

    private static int GetArrayCost(int[] costs, int level)
    {
        return level >= 0 && level < costs.Length ? costs[level] : 0;
    }

    private static float GetEggValueMultiplier(int level)
    {
        return EggValueMultipliers[
            Mathf.Clamp(level, 0, EggValueMultipliers.Length - 1)];
    }

    private static string GetRareChanceDescription(int level)
    {
        GetRareChances(level, out float silver, out float gold, out float galaxy);
        return
            $"Silver {silver * 100f:0.###}% • " +
            $"Gold {gold * 100f:0.###}% • " +
            $"Galaxy {galaxy * 100f:0.####}%";
    }

    private static void GetRareChances(
        int level,
        out float silver,
        out float gold,
        out float galaxy)
    {
        int index = Mathf.Clamp(level, 0, SilverChanceByLevel.Length - 1);
        silver = SilverChanceByLevel[index];
        gold = GoldChanceByLevel[index];
        galaxy = GalaxyChanceByLevel[index];
    }

    private static string FormatMoney(int cents)
    {
        return $"${cents / 100:N0}.{Mathf.Abs(cents % 100):D2}";
    }

    private static void NotifyChanged()
    {
        Changed?.Invoke();
    }
}
