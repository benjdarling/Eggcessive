using System;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class ProgressionSystem : MonoBehaviour
{
    public enum UpgradeId
    {
        FoodBag,
        FeedSpeed,
        RareEggChance,
        EggWeight,
        IncubatorInstall,
        IncubatorCapacity,
        IncubatorSpeed,
        CrosshatcherInstall,
        CrosshatcherSpeed,
        CrosshatcherQuality,
        BasketCapacity,
        VacuumPower,
        VacuumRange,
        RobotUnlock,
        RobotSpeed,
        RobotCapacity,
        RobotSmartness,
        VacuumUnlock,
        EggValue,
        PrimeFeed,
        BasketReach,
        TruckBonus,
        ChickenPerks,
        IncubatorTurbo,
        IncubatorTurboPower,
        IncubatorTurboDuration,
        CrosshatcherTurbo,
        CrosshatcherTurboPower,
        CrosshatcherTurboDuration,
        RobotTurbo,
        RobotTurboPower,
        RobotTurboDuration
    }

    public readonly struct NodeState
    {
        public NodeState(
            string title,
            string icon,
            string details,
            int level,
            int maximumLevel,
            long cost,
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
        public long Cost { get; }
        public bool Visible { get; }
        public bool PrerequisiteMet { get; }
        public bool IsMaxed => MaximumLevel > 0 && Level >= MaximumLevel;
        public bool IsRepeatable => MaximumLevel <= 0;
    }

    private static readonly long[] RareChanceCosts =
    {
        1200, 3500, 9000, 22000, 55000, 140000, 350000, 900000,
        2700000, 8000000, 24000000, 75000000
    };

    private static readonly float[] RareChanceByLevel =
    {
        0f, 0.00025f, 0.00075f, 0.002f, 0.005f,
        0.0125f, 0.03f, 0.065f, 0.12f,
        0.15f, 0.18f, 0.215f, 0.25f
    };

    private static readonly float[] EpicChanceByLevel =
    {
        0f, 0f, 0.0001f, 0.0005f, 0.0015f,
        0.004f, 0.01f, 0.025f, 0.055f,
        0.075f, 0.1f, 0.13f, 0.165f
    };

    private static readonly float[] LegendaryChanceByLevel =
    {
        0f, 0f, 0.00005f, 0.0002f, 0.0005f,
        0.0015f, 0.004f, 0.012f, 0.03f,
        0.042f, 0.057f, 0.075f, 0.095f
    };

    private static readonly float[] CosmicChanceByLevel =
    {
        0f, 0f, 0f, 0f, 0.00002f,
        0.0001f, 0.0005f, 0.002f, 0.0075f,
        0.012f, 0.019f, 0.029f, 0.042f
    };

    // Individual breed odds are added to the supplies-shop premium egg
    // upgrades. Values are ordered White through Cosmic.
    private static readonly float[] BreedRareChance =
    {
        0.0005f, 0.005f, 0.015f, 0.04f, 0.08f, 0.14f, 0.2f
    };

    private static readonly float[] BreedEpicChance =
    {
        0f, 0.001f, 0.005f, 0.015f, 0.035f, 0.07f, 0.12f
    };

    private static readonly float[] BreedLegendaryChance =
    {
        0f, 0.0002f, 0.001f, 0.005f, 0.015f, 0.04f, 0.1f
    };

    private static readonly float[] BreedCosmicChance =
    {
        0f, 0f, 0.00002f, 0.0002f, 0.001f, 0.005f, 0.02f
    };

    private static readonly long[] ChickenPerkCosts =
    {
        2500000, 12500000, 60000000, 300000000, 1500000000,
        6000000000L, 24000000000L, 90000000000L,
        350000000000L, 1400000000000L
    };

    // Each purchased tier adds this amount to the premium-egg multiplier for
    // the corresponding breed. Higher breeds therefore turn the same global
    // genetics investment into a stronger late-game rarity boost.
    private static readonly float[] ChickenPerkBoostPerLevel =
    {
        0.05f, 0.08f, 0.1f, 0.12f, 0.15f, 0.18f, 0.2f
    };

    private static readonly long[] EggWeightCosts =
    {
        2500, 7500, 20000, 60000, 180000, 550000, 1600000, 5000000,
        12000000, 30000000, 90000000, 270000000, 800000000,
        2400000000L, 7200000000L
    };

    private static readonly long[] EggValueCosts =
    {
        5000, 17000, 60000, 210000, 730000, 2500000, 9100000, 30000000,
        110000000, 400000000, 1400000000L, 4900000000L,
        18000000000L, 64000000000L, 230000000000L,
        850000000000L, 3100000000000L, 12000000000000L
    };

    private static readonly float[] EggValueMultipliers =
    {
        1f, 1.7f, 3f, 5.5f, 10f, 18f, 33f, 60f, 110f,
        200f, 370f, 680f, 1250f, 2300f, 4200f, 7700f, 14000f,
        26000f, 48000f
    };

    private static readonly long[] TruckBonusCosts =
    {
        20000, 63000, 200000, 630000, 2000000,
        6400000, 20000000, 66000000, 210000000, 690000000,
        2200000000L, 7300000000L, 24000000000L,
        80000000000L, 270000000000L
    };
    private const float TruckBonusGrowthPerLevel = 1.15f;

    private const float EggWeightUpperRangePerLevel = 0.075f;
    private const float EggWeightChancePerLevel = 0.1f;
    private const float RarityScaleStep = 0.075f;
    public const float BaseEggWeightKilograms = 0.1f;

    private static readonly long[] BasketCosts = { 800, 1800, 4200, 8000 };
    private static readonly long[] BasketReachCosts =
    {
        1500, 4000, 10000, 25000
    };
    // Vacuum entry sits beyond both completed basket branches. Its $600 unlock
    // costs more than Basket Reach 4 ($250), while later power and range tiers
    // continue scaling sharply because they multiply collection income.
    private static readonly long[] VacuumPowerCosts =
    {
        60000, 150000, 1500000
    };
    private static readonly long[] VacuumRangeCosts =
    {
        14000, 250000, 2500000
    };
    private static readonly long[] RobotSpeedCosts = { 180000, 520000, 1600000 };
    private static readonly long[] RobotCapacityCosts = { 240000, 750000, 2400000 };
    private static readonly long[] RobotSmartCosts =
        { 600000, 3500000, 12000000, 60000000 };
    private const int RobotUnlockCost = 120000;

    [SerializeField, Range(0, 12)] private int rareEggChanceLevel;
    [FormerlySerializedAs("eggValueLevel")]
    [SerializeField, Range(0, 15)] private int eggWeightLevel;
    [SerializeField, Range(0, 18)] private int eggSaleValueLevel;
    [SerializeField, Range(0, 15)] private int truckBonusLevel;
    [SerializeField, Range(0, 10)] private int chickenPerksLevel;

    public static ProgressionSystem Instance { get; private set; }
    public static event Action Changed;

    public int RareEggChanceLevel => rareEggChanceLevel;
    public int EggWeightLevel => eggWeightLevel;
    public int EggValueLevel => eggSaleValueLevel;
    public int TruckBonusLevel => truckBonusLevel;
    public int ChickenPerksLevel => chickenPerksLevel;
    public static int MaximumRareEggChanceLevel => RareChanceCosts.Length;
    public static int MaximumEggWeightLevel => EggWeightCosts.Length;
    public static int MaximumEggValueLevel => EggValueCosts.Length;
    public static int MaximumTruckBonusLevel => TruckBonusCosts.Length;
    public static int MaximumChickenPerksLevel => ChickenPerkCosts.Length;
    public float TruckBonusMultiplier =>
        Mathf.Pow(TruckBonusGrowthPerLevel, truckBonusLevel);
    public float EggValueMultiplier =>
        EggValueMultipliers[Mathf.Clamp(
            eggSaleValueLevel,
            0,
            EggValueMultipliers.Length - 1)];
    public float EggWeightChance => GetEggWeightChance(eggWeightLevel);
    public float EggWeightUpperMultiplier =>
        GetEggWeightUpperMultiplier(eggWeightLevel);

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
        CrosshatcherShopController crosshatcher = CrosshatcherShopController.Instance;
        EggCarryController collection = EggCarryController.Instance;
        int feedLevel = food != null ? food.UnlockedFeedTier : 1;
        bool installed = incubator != null && incubator.IsInstalled;
        bool crosshatcherInstalled =
            crosshatcher != null && crosshatcher.IsInstalled;
        int basketLevel = collection != null ? collection.BasketUpgradeLevel : 0;
        int basketReach = collection != null ? collection.BasketReachLevel : 0;
        int vacuumPower = collection != null ? collection.VacuumPowerLevel : 0;
        int vacuumRange = collection != null ? collection.VacuumRangeLevel : 0;
        bool robotUnlocked = collection != null && collection.HasRobot;
        int robotSpeed = collection != null ? collection.RobotSpeedLevel : 0;
        int robotCapacity = collection != null ? collection.RobotCapacityLevel : 0;
        int smartness = collection != null ? collection.RobotSmartnessLevel : 0;

        switch (id)
        {
            case UpgradeId.IncubatorTurbo:
            case UpgradeId.CrosshatcherTurbo:
            case UpgradeId.RobotTurbo:
                return GetTurboPurchaseNodeState(id);

            case UpgradeId.IncubatorTurboPower:
            case UpgradeId.IncubatorTurboDuration:
            case UpgradeId.CrosshatcherTurboPower:
            case UpgradeId.CrosshatcherTurboDuration:
            case UpgradeId.RobotTurboPower:
            case UpgradeId.RobotTurboDuration:
                return GetTurboUpgradeNodeState(id, 0);

            case UpgradeId.FoodBag:
                return new NodeState(
                    "Feed Bag",
                    "F",
                    food != null
                        ? $"{food.CurrentFeedName} . {food.CurrentFeedSpeedMultiplier:0.##}x production"
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
                        ? $"Next feed: {food.NextFeedName} . {food.NextFeedSpeedMultiplier:0.##}x"
                        : "Maximum production feed",
                    feedLevel,
                    FoodShopController.MaximumFeedTier,
                    food != null ? food.NextFeedTierUnlockCost : 0,
                    true,
                    food != null);

            case UpgradeId.PrimeFeed:
            {
                int level = food != null ? food.PrimeFeedLevel : 0;
                int nextLevel = Mathf.Min(
                    level + 1,
                    FoodShopController.MaximumPrimeFeedLevel);
                return new NodeState(
                    "Prime Feed",
                    "P+",
                    food != null
                        ? $"Fed chickens get {food.GetPrimeFeedMultiplier(nextLevel):0.0}x premium egg chance"
                        : "Feed system unavailable",
                    level,
                    FoodShopController.MaximumPrimeFeedLevel,
                    food != null
                        ? food.GetPrimeFeedUpgradeCost(nextLevel)
                        : 0,
                    food != null,
                    food != null);
            }

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

            case UpgradeId.ChickenPerks:
                return new NodeState(
                    "Chicken Perks",
                    "CP",
                    GetChickenPerksDescription(chickenPerksLevel + 1),
                    chickenPerksLevel,
                    ChickenPerkCosts.Length,
                    GetArrayCost(
                        ChickenPerkCosts,
                        chickenPerksLevel),
                    rareEggChanceLevel >= 7,
                    rareEggChanceLevel >= 8);

            case UpgradeId.EggWeight:
                return new NodeState(
                    "Egg Weight/Size",
                    "W",
                    GetEggWeightDescription(eggWeightLevel + 1),
                    eggWeightLevel,
                    EggWeightCosts.Length,
                    GetArrayCost(EggWeightCosts, eggWeightLevel),
                    rareEggChanceLevel >= 2,
                    rareEggChanceLevel >= 2);

            case UpgradeId.EggValue:
                return new NodeState(
                    "Egg Value",
                    "$",
                    $"All eggs sell for {GetEggValueMultiplier(eggSaleValueLevel + 1):0.##}x value",
                    eggSaleValueLevel,
                    EggValueCosts.Length,
                    GetArrayCost(EggValueCosts, eggSaleValueLevel),
                    eggWeightLevel >= 1,
                    eggWeightLevel >= 1);

            case UpgradeId.TruckBonus:
                return new NodeState(
                    "Truck Bonus",
                    "T$",
                    $"Filled trucks pay {GetTruckBonusMultiplier(truckBonusLevel + 1):0.0}x bonus cash",
                    truckBonusLevel,
                    TruckBonusCosts.Length,
                    GetArrayCost(TruckBonusCosts, truckBonusLevel),
                    eggSaleValueLevel >= 2,
                    eggSaleValueLevel >= 2);

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

            case UpgradeId.CrosshatcherInstall:
                return new NodeState(
                    "Crosshatcher",
                    "X",
                    crosshatcherInstalled
                        ? "Installed and operational"
                        : "Combine two chickens into a stronger breed",
                    crosshatcherInstalled ? 1 : 0,
                    1,
                    crosshatcher != null ? crosshatcher.InstallCost : 0,
                    true,
                    crosshatcher != null);

            case UpgradeId.CrosshatcherSpeed:
                return new NodeState(
                    "Crosshatch Speed",
                    ">>",
                    crosshatcher != null
                        ? $"Next: {crosshatcher.NextProcessingTime:0.##} sec"
                        : "Crosshatcher unavailable",
                    crosshatcher != null ? crosshatcher.SpeedLevel : 0,
                    CrosshatcherController.MaximumLevel,
                    crosshatcher != null ? crosshatcher.NextSpeedCost : 0,
                    crosshatcherInstalled,
                    crosshatcherInstalled);

            case UpgradeId.CrosshatcherQuality:
                return new NodeState(
                    "Breed Quality",
                    "+",
                    crosshatcher != null
                        ? $"Next: {crosshatcher.NextImprovementChance * 100f:0}% upgrade chance"
                        : "Crosshatcher unavailable",
                    crosshatcher != null ? crosshatcher.QualityLevel : 0,
                    CrosshatcherController.MaximumLevel,
                    crosshatcher != null ? crosshatcher.NextQualityCost : 0,
                    crosshatcherInstalled,
                    crosshatcherInstalled);

            case UpgradeId.BasketCapacity:
                return new NodeState(
                    "Egg Basket",
                    "B",
                    basketLevel >= EggCarryController.MaximumBasketLevel
                        ? "6 egg capacity"
                        : $"Next: {new[] { 3, 4, 5, 6 }[Mathf.Clamp(basketLevel, 0, 3)]} egg capacity",
                    basketLevel,
                    EggCarryController.MaximumBasketLevel,
                    GetArrayCost(BasketCosts, basketLevel),
                    true,
                    collection != null && !collection.HasVacuum);

            case UpgradeId.BasketReach:
                return new NodeState(
                    "Basket Reach",
                    "B<>",
                    basketReach >= EggCarryController.MaximumBasketReachLevel
                        ? "Pull nearby eggs within 0.8m of the clicked egg"
                        : $"Next: pull nearby eggs within {(basketReach + 1) * 0.2f:0.0}m",
                    basketReach,
                    EggCarryController.MaximumBasketReachLevel,
                    GetArrayCost(BasketReachCosts, basketReach),
                    basketLevel >= 1,
                    collection != null
                        && basketLevel >= 1
                        && !collection.HasVacuum);

            case UpgradeId.VacuumUnlock:
                return new NodeState(
                    "Egg Vacuum",
                    "V",
                    collection != null && collection.HasVacuum
                        ? "Vacuum collection unlocked"
                        : "Requires Capacity 4 and Reach 4; replaces the basket with click-hold suction",
                    collection != null && collection.HasVacuum ? 1 : 0,
                    1,
                    VacuumPowerCosts[0],
                    true,
                    collection != null
                        && (basketLevel >= EggCarryController.MaximumBasketLevel
                            && basketReach
                                >= EggCarryController.MaximumBasketReachLevel));

            case UpgradeId.VacuumPower:
                return new NodeState(
                    "Vacuum Power",
                    "V",
                    vacuumPower == 0
                        ? "Replace the full basket with click-hold suction"
                        : "Faster egg suction",
                    vacuumPower,
                    3,
                    GetArrayCost(VacuumPowerCosts, vacuumPower),
                    basketLevel >= EggCarryController.MaximumBasketLevel
                        && basketReach
                            >= EggCarryController.MaximumBasketReachLevel,
                    collection != null
                        && (basketLevel >= EggCarryController.MaximumBasketLevel
                            && basketReach
                                >= EggCarryController.MaximumBasketReachLevel));

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
                    robotCapacity >= 3 ? "27 egg capacity" : "Carry more eggs per trip",
                    robotCapacity,
                    3,
                    GetArrayCost(RobotCapacityCosts, robotCapacity),
                    robotUnlocked,
                    robotUnlocked);

            case UpgradeId.RobotSmartness:
                return new NodeState(
                    "Robot Logic",
                    "AI",
                    smartness switch
                    {
                        0 => "Route spare eggs to the incubator after quota",
                        1 => "Keep the incubator supplied until the pen is full",
                        2 => "Upgrade to collect strictly by egg rarity",
                        3 => "Add paired chicken arms for the crosshatcher",
                        _ => "Carries two chickens to an available crosshatcher"
                    },
                    smartness,
                    EggCollectorRobot.ChickenArmsSmartnessLevel,
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

        if (TryGetTurboUpgrade(
                id,
                out _,
                out _))
        {
            return GetTurboUpgradeNodeState(id, targetLevel);
        }

        FoodShopController food = FoodShopController.Instance;
        IncubatorShopController incubator = IncubatorShopController.Instance;
        CrosshatcherShopController crosshatcher = CrosshatcherShopController.Instance;
        EggCarryController collection = EggCarryController.Instance;
        bool installed = incubator != null && incubator.IsInstalled;
        bool crosshatcherInstalled =
            crosshatcher != null && crosshatcher.IsInstalled;

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
            case UpgradeId.PrimeFeed:
            {
                int target = Mathf.Clamp(
                    targetLevel,
                    1,
                    FoodShopController.MaximumPrimeFeedLevel);
                int current = food != null ? food.PrimeFeedLevel : 0;
                return new NodeState(
                    $"Prime Feed Tier {target}",
                    "P+",
                    food != null
                        ? $"Fed chickens get {food.GetPrimeFeedMultiplier(target):0.0}x premium egg chance"
                        : "Feed system unavailable",
                    current,
                    target,
                    food != null ? food.GetPrimeFeedUpgradeCost(target) : 0,
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
            case UpgradeId.ChickenPerks:
            {
                int target = Mathf.Clamp(
                    targetLevel,
                    1,
                    ChickenPerkCosts.Length);
                return new NodeState(
                    $"Chicken Perks Tier {target}",
                    "CP",
                    GetChickenPerksDescription(target),
                    chickenPerksLevel,
                    target,
                    GetArrayCost(ChickenPerkCosts, target - 1),
                    true,
                    rareEggChanceLevel >= 8
                        && chickenPerksLevel >= target - 1);
            }
            case UpgradeId.EggWeight:
            {
                int target = Mathf.Clamp(targetLevel, 1, EggWeightCosts.Length);
                return new NodeState(
                    $"Egg Weight/Size Tier {target}",
                    "W",
                    GetEggWeightDescription(target),
                    eggWeightLevel,
                    target,
                    GetArrayCost(EggWeightCosts, target - 1),
                    true,
                    rareEggChanceLevel >= 2 && eggWeightLevel >= target - 1);
            }
            case UpgradeId.EggValue:
            {
                int target = Mathf.Clamp(targetLevel, 1, EggValueCosts.Length);
                return new NodeState(
                    $"Egg Value Tier {target}",
                    "$",
                    $"All eggs sell for {GetEggValueMultiplier(target):0.##}x value",
                    eggSaleValueLevel,
                    target,
                    GetArrayCost(EggValueCosts, target - 1),
                    true,
                    eggWeightLevel >= 1 && eggSaleValueLevel >= target - 1);
            }
            case UpgradeId.TruckBonus:
            {
                int target = Mathf.Clamp(
                    targetLevel,
                    1,
                    TruckBonusCosts.Length);
                return new NodeState(
                    $"Truck Bonus Tier {target}",
                    "T$",
                    $"Filled trucks pay {GetTruckBonusMultiplier(target):0.0}x bonus cash",
                    truckBonusLevel,
                    target,
                    GetArrayCost(TruckBonusCosts, target - 1),
                    true,
                    eggSaleValueLevel >= 2
                        && truckBonusLevel >= target - 1);
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
            case UpgradeId.CrosshatcherSpeed:
            {
                int current = crosshatcher != null ? crosshatcher.SpeedLevel : 0;
                int target = Mathf.Clamp(
                    targetLevel,
                    2,
                    CrosshatcherController.MaximumLevel);
                return new NodeState(
                    $"Crosshatch Speed Tier {target}",
                    "X>",
                    $"{CrosshatcherController.GetProcessingTime(target):0.##} seconds per chicken",
                    current,
                    target,
                    crosshatcher != null
                        ? crosshatcher.GetSpeedUpgradeCost(target)
                        : 0,
                    true,
                    crosshatcherInstalled && current >= target - 1);
            }
            case UpgradeId.CrosshatcherQuality:
            {
                int current = crosshatcher != null ? crosshatcher.QualityLevel : 0;
                int target = Mathf.Clamp(
                    targetLevel,
                    2,
                    CrosshatcherController.MaximumLevel);
                return new NodeState(
                    $"Breed Quality Tier {target}",
                    "X+",
                    $"{CrosshatcherController.GetImprovementChance(target) * 100f:0}% chance of the next breed when mixing",
                    current,
                    target,
                    crosshatcher != null
                        ? crosshatcher.GetQualityUpgradeCost(target)
                        : 0,
                    true,
                    crosshatcherInstalled && current >= target - 1);
            }
            case UpgradeId.BasketCapacity:
            {
                int current = collection != null ? collection.BasketUpgradeLevel : 0;
                int target = Mathf.Clamp(
                    targetLevel,
                    1,
                    EggCarryController.MaximumBasketLevel);
                int[] capacities = { 3, 4, 5, 6 };
                return new NodeState(
                    $"Basket Capacity {target}",
                    "B",
                    $"{capacities[target - 1]} egg capacity",
                    current,
                    target,
                    GetArrayCost(BasketCosts, target - 1),
                    true,
                    collection != null
                        && !collection.HasVacuum
                        && current >= target - 1);
            }
            case UpgradeId.BasketReach:
            {
                int current = collection != null
                    ? collection.BasketReachLevel
                    : 0;
                int target = Mathf.Clamp(
                    targetLevel,
                    1,
                    EggCarryController.MaximumBasketReachLevel);
                return new NodeState(
                    $"Basket Reach Tier {target}",
                    "B<>",
                    $"Pull nearby eggs within {target * 0.2f:0.0}m of the clicked egg",
                    current,
                    target,
                    GetArrayCost(BasketReachCosts, target - 1),
                    true,
                    collection != null
                        && collection.BasketUpgradeLevel >= 1
                        && !collection.HasVacuum
                        && current >= target - 1);
            }
            case UpgradeId.VacuumPower:
            {
                int current = collection != null ? collection.VacuumPowerLevel : 0;
                int target = Mathf.Clamp(targetLevel, 1, 3);
                return new NodeState(
                    $"Vacuum Power {target}",
                    "V",
                    target == 1
                        ? "Replace the full basket with click-hold suction"
                        : "Faster egg suction",
                    current,
                    target,
                    GetArrayCost(VacuumPowerCosts, target - 1),
                    true,
                    collection != null
                        && (collection.BasketUpgradeLevel
                                >= EggCarryController.MaximumBasketLevel
                            && collection.BasketReachLevel
                                >= EggCarryController.MaximumBasketReachLevel)
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
                int capacity = target * 9;
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
                int target = Mathf.Clamp(
                    targetLevel,
                    1,
                    EggCollectorRobot.ChickenArmsSmartnessLevel);
                return new NodeState(
                    $"Robot Logic Upgrade {target}",
                    "AI",
                    target switch
                    {
                        1 => "Route spare eggs to the incubator after quota and recover tiny flocks",
                        2 => "Keep the incubator supplied with common eggs until the pen is full",
                        3 => "Collect highest-rarity eggs before closer common eggs",
                        _ => "Add two IK arms and carry paired chickens to the crosshatcher"
                    },
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
        if (id is UpgradeId.IncubatorInstall
            or UpgradeId.IncubatorCapacity
            or UpgradeId.IncubatorSpeed
            or UpgradeId.CrosshatcherInstall
            or UpgradeId.CrosshatcherSpeed
            or UpgradeId.CrosshatcherQuality
            or UpgradeId.RobotUnlock
            or UpgradeId.RobotSpeed
            or UpgradeId.RobotCapacity
            or UpgradeId.RobotSmartness)
        {
            message = "Use the focused pen's tech HUD";
            return false;
        }

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
        RoundSystem.Instance?.PlayCashRegisterSfx();
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
        return RollEggType(ChickenController.ChickenBreed.White);
    }

    public ChickenEgg.EggType RollEggType(
        ChickenController.ChickenBreed breed)
    {
        return RollEggType(breed, 1f);
    }

    public ChickenEgg.EggType RollEggType(
        ChickenController.ChickenBreed breed,
        float premiumChanceMultiplier)
    {
        GetCombinedRareChances(
            breed,
            out float rare,
            out float epic,
            out float legendary,
            out float cosmic);
        float multiplier = Mathf.Max(1f, premiumChanceMultiplier);
        multiplier *= GetChickenPerksMultiplier(breed);
        rare *= multiplier;
        epic *= multiplier;
        legendary *= multiplier;
        cosmic *= multiplier;
        float totalPremiumChance = rare + epic + legendary + cosmic;
        if (totalPremiumChance > 1f)
        {
            float normalization = 1f / totalPremiumChance;
            rare *= normalization;
            epic *= normalization;
            legendary *= normalization;
            cosmic *= normalization;
        }
        float roll = UnityEngine.Random.value;

        if (roll < cosmic)
        {
            return ChickenEgg.EggType.Cosmic;
        }

        if (roll < cosmic + legendary)
        {
            return ChickenEgg.EggType.Legendary;
        }

        if (roll < cosmic + legendary + epic)
        {
            return ChickenEgg.EggType.Epic;
        }

        if (roll < cosmic + legendary + epic + rare)
        {
            return ChickenEgg.EggType.Rare;
        }

        return ChickenEgg.EggType.Common;
    }

    public void GetCombinedRareChances(
        ChickenController.ChickenBreed breed,
        out float rare,
        out float epic,
        out float legendary,
        out float cosmic)
    {
        GetRareChances(
            rareEggChanceLevel,
            out float upgradeRare,
            out float upgradeEpic,
            out float upgradeLegendary,
            out float upgradeCosmic);
        int breedIndex = Mathf.Clamp(
            (int)breed,
            0,
            BreedRareChance.Length - 1);
        rare = BreedRareChance[breedIndex] + upgradeRare;
        epic = BreedEpicChance[breedIndex] + upgradeEpic;
        legendary = BreedLegendaryChance[breedIndex] + upgradeLegendary;
        cosmic = BreedCosmicChance[breedIndex] + upgradeCosmic;
    }

    public int GetEggValueCents(ChickenEgg.EggType type)
    {
        int baseValue = type switch
        {
            ChickenEgg.EggType.Rare => 400,
            ChickenEgg.EggType.Epic => 1200,
            ChickenEgg.EggType.Legendary => 3500,
            ChickenEgg.EggType.Cosmic => 15000,
            _ => 100
        };
        return Mathf.RoundToInt(baseValue * EggValueMultiplier);
    }

    public float GetChickenPerksMultiplier(
        ChickenController.ChickenBreed breed)
    {
        int breedIndex = Mathf.Clamp(
            (int)breed,
            0,
            ChickenPerkBoostPerLevel.Length - 1);
        return 1f + chickenPerksLevel
            * ChickenPerkBoostPerLevel[breedIndex];
    }

    public float RollEggWeightScale(ChickenEgg.EggType type)
    {
        float upperMultiplier = EggWeightUpperMultiplier;
        if (type != ChickenEgg.EggType.Common)
        {
            // Premium eggs sit above the complete common-egg range. Each
            // rarity then receives one additional flat 7.5% size/weight step.
            return upperMultiplier + (int)type * RarityScaleStep;
        }

        if (eggWeightLevel <= 0 || UnityEngine.Random.value >= EggWeightChance)
        {
            return 1f;
        }

        return UnityEngine.Random.Range(1f, upperMultiplier);
    }

    private bool ApplyUpgrade(UpgradeId id, out string message)
    {
        switch (id)
        {
            case UpgradeId.IncubatorTurbo:
                return AddTurboConsumable(
                    TurboConsumableSystem.TurboType.Incubator,
                    out message);
            case UpgradeId.CrosshatcherTurbo:
                return AddTurboConsumable(
                    TurboConsumableSystem.TurboType.Crosshatcher,
                    out message);
            case UpgradeId.RobotTurbo:
                return AddTurboConsumable(
                    TurboConsumableSystem.TurboType.Robot,
                    out message);
            case UpgradeId.IncubatorTurboPower:
            case UpgradeId.IncubatorTurboDuration:
            case UpgradeId.CrosshatcherTurboPower:
            case UpgradeId.CrosshatcherTurboDuration:
            case UpgradeId.RobotTurboPower:
            case UpgradeId.RobotTurboDuration:
                if (TryGetTurboUpgrade(id, out var turboType, out var kind))
                {
                    return TurboConsumableSystem.TryUpgrade(
                        turboType,
                        kind,
                        out message);
                }
                message = "Turbo upgrade unavailable";
                return false;
            case UpgradeId.FeedSpeed:
                return FoodShopController.Instance.TryUnlockNextFeedTier(out message, false);
            case UpgradeId.PrimeFeed:
                return FoodShopController.Instance.TryUpgradePrimeFeed(
                    out message,
                    false);
            case UpgradeId.RareEggChance:
                rareEggChanceLevel++;
                message = $"Premium egg chance level {rareEggChanceLevel}";
                return true;
            case UpgradeId.ChickenPerks:
                chickenPerksLevel++;
                message =
                    $"Chicken perks level {chickenPerksLevel}: "
                    + $"up to {GetChickenPerksMultiplier(ChickenController.ChickenBreed.Cosmic):0.##}x premium egg chance";
                return true;
            case UpgradeId.EggWeight:
                eggWeightLevel++;
                message =
                    $"Egg weight/size: {EggWeightChance * 100f:0}% chance, " +
                    $"up to {EggWeightUpperMultiplier * 100f:0.#}% size and sale value";
                return true;
            case UpgradeId.EggValue:
                eggSaleValueLevel++;
                message = $"All egg values increased to {EggValueMultiplier:0.##}x";
                return true;
            case UpgradeId.TruckBonus:
                truckBonusLevel++;
                message = $"Truck bonuses increased to {TruckBonusMultiplier:0.0}x";
                return true;
            case UpgradeId.IncubatorInstall:
                return IncubatorShopController.Instance.TryInstall(out message, false);
            case UpgradeId.IncubatorCapacity:
                return IncubatorShopController.Instance.TryUpgradeCapacity(out message, false);
            case UpgradeId.IncubatorSpeed:
                return IncubatorShopController.Instance.TryUpgradeSpeed(out message, false);
            case UpgradeId.CrosshatcherInstall:
                return CrosshatcherShopController.Instance.TryInstall(out message, false);
            case UpgradeId.CrosshatcherSpeed:
                return CrosshatcherShopController.Instance.TryUpgradeSpeed(out message, false);
            case UpgradeId.CrosshatcherQuality:
                return CrosshatcherShopController.Instance.TryUpgradeQuality(out message, false);
            case UpgradeId.BasketCapacity:
                EggCarryController.Instance.UpgradeBasket();
                message = $"Basket capacity level {EggCarryController.Instance.BasketUpgradeLevel}";
                return true;
            case UpgradeId.BasketReach:
                EggCarryController.Instance.UpgradeBasketReach();
                message =
                    $"Basket reach increased to {EggCarryController.Instance.BasketReachRadius:0.0}m";
                return true;
            case UpgradeId.VacuumUnlock:
                EggCarryController.Instance.UpgradeVacuumPower();
                message = "Egg vacuum unlocked";
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

    private static NodeState GetTurboPurchaseNodeState(UpgradeId id)
    {
        TurboConsumableSystem.TurboType type = id switch
        {
            UpgradeId.IncubatorTurbo =>
                TurboConsumableSystem.TurboType.Incubator,
            UpgradeId.CrosshatcherTurbo =>
                TurboConsumableSystem.TurboType.Crosshatcher,
            _ => TurboConsumableSystem.TurboType.Robot
        };
        string name = TurboConsumableSystem.GetDisplayName(type);
        bool unlocked = AreTurboPrerequisitesMet(type);
        string details = unlocked
            ? $"Owned {TurboConsumableSystem.GetInventory(type)} . "
                + $"+{TurboConsumableSystem.GetBoostPercent(type):0}% for "
                + $"{TurboConsumableSystem.GetDurationSeconds(type):0}s"
            : GetTurboUnlockDescription(type);
        return new NodeState(
            $"{name} Turbo",
            "⚡",
            details,
            TurboConsumableSystem.GetInventory(type),
            0,
            TurboConsumableSystem.GetPurchaseCost(type),
            true,
            unlocked);
    }

    private static bool AreTurboPrerequisitesMet(
        TurboConsumableSystem.TurboType type)
    {
        PenExpansionManager pens = PenExpansionManager.Instance;
        if (pens != null && pens.IsInitialized)
        {
            PenExpansionManager.EquipmentType equipment = type switch
            {
                TurboConsumableSystem.TurboType.Incubator =>
                    PenExpansionManager.EquipmentType.Incubator,
                TurboConsumableSystem.TurboType.Crosshatcher =>
                    PenExpansionManager.EquipmentType.Crosshatcher,
                _ => PenExpansionManager.EquipmentType.Robot
            };
            return pens.HasCompletedCoreUpgrades(equipment);
        }

        EggCarryController collection = EggCarryController.Instance;
        return type switch
        {
            TurboConsumableSystem.TurboType.Incubator =>
                IncubatorShopController.Instance != null
                && IncubatorShopController.Instance.IsInstalled
                && IncubatorShopController.Instance.CapacityLevel
                    >= IncubatorController.MaximumLevel
                && IncubatorShopController.Instance.SpeedLevel
                    >= IncubatorController.MaximumLevel,
            TurboConsumableSystem.TurboType.Crosshatcher =>
                CrosshatcherShopController.Instance != null
                && CrosshatcherShopController.Instance.IsInstalled
                && CrosshatcherShopController.Instance.SpeedLevel
                    >= CrosshatcherController.MaximumLevel
                && CrosshatcherShopController.Instance.QualityLevel
                    >= CrosshatcherController.MaximumLevel,
            TurboConsumableSystem.TurboType.Robot =>
                collection != null
                && collection.HasRobot
                && collection.RobotSpeedLevel
                    >= EggCarryController.MaximumRobotLevel
                && collection.RobotCapacityLevel
                    >= EggCarryController.MaximumRobotLevel,
            _ => false
        };
    }

    private static string GetTurboUnlockDescription(
        TurboConsumableSystem.TurboType type)
    {
        return type switch
        {
            TurboConsumableSystem.TurboType.Incubator =>
                "Max Incubator Capacity + Hatch Speed in one pen to unlock",
            TurboConsumableSystem.TurboType.Crosshatcher =>
                "Max Crosshatch Speed + Breed Quality in one pen to unlock",
            TurboConsumableSystem.TurboType.Robot =>
                "Max Robot Speed + Capacity in one pen to unlock",
            _ => "Complete both core upgrade branches to unlock"
        };
    }

    private static NodeState GetTurboUpgradeNodeState(
        UpgradeId id,
        int targetLevel)
    {
        if (!TryGetTurboUpgrade(id, out var type, out var kind))
        {
            return default;
        }

        int current = kind == TurboConsumableSystem.UpgradeKind.Power
            ? TurboConsumableSystem.GetPowerLevel(type)
            : TurboConsumableSystem.GetDurationLevel(type);
        int maximum = kind == TurboConsumableSystem.UpgradeKind.Power
            ? TurboConsumableSystem.MaximumPowerLevel
            : TurboConsumableSystem.MaximumDurationLevel;
        int target = targetLevel > 0
            ? Mathf.Clamp(targetLevel, 1, maximum)
            : maximum;
        long cost = TurboConsumableSystem.GetUpgradeCost(
            type,
            kind,
            targetLevel > 0 ? target - 1 : current);
        string name = TurboConsumableSystem.GetDisplayName(type);
        string detail = kind == TurboConsumableSystem.UpgradeKind.Power
            ? $"Current +{TurboConsumableSystem.GetBoostPercent(type):0}% productivity"
            : $"Current duration {TurboConsumableSystem.GetDurationSeconds(type):0} seconds";
        return new NodeState(
            $"{name} Turbo {kind}",
            kind == TurboConsumableSystem.UpgradeKind.Power ? "X%" : "SEC",
            detail,
            current,
            target,
            cost,
            true,
            TurboConsumableSystem.GetTotalPurchased(type) > 0
                && (targetLevel <= 0 || current >= target - 1));
    }

    private static bool AddTurboConsumable(
        TurboConsumableSystem.TurboType type,
        out string message)
    {
        TurboConsumableSystem.AddConsumable(type);
        message = $"Bought {TurboConsumableSystem.GetDisplayName(type)} Turbo "
            + $"(owned {TurboConsumableSystem.GetInventory(type)})";
        return true;
    }

    private static bool TryGetTurboUpgrade(
        UpgradeId id,
        out TurboConsumableSystem.TurboType type,
        out TurboConsumableSystem.UpgradeKind kind)
    {
        switch (id)
        {
            case UpgradeId.IncubatorTurboPower:
                type = TurboConsumableSystem.TurboType.Incubator;
                kind = TurboConsumableSystem.UpgradeKind.Power;
                return true;
            case UpgradeId.IncubatorTurboDuration:
                type = TurboConsumableSystem.TurboType.Incubator;
                kind = TurboConsumableSystem.UpgradeKind.Duration;
                return true;
            case UpgradeId.CrosshatcherTurboPower:
                type = TurboConsumableSystem.TurboType.Crosshatcher;
                kind = TurboConsumableSystem.UpgradeKind.Power;
                return true;
            case UpgradeId.CrosshatcherTurboDuration:
                type = TurboConsumableSystem.TurboType.Crosshatcher;
                kind = TurboConsumableSystem.UpgradeKind.Duration;
                return true;
            case UpgradeId.RobotTurboPower:
                type = TurboConsumableSystem.TurboType.Robot;
                kind = TurboConsumableSystem.UpgradeKind.Power;
                return true;
            case UpgradeId.RobotTurboDuration:
                type = TurboConsumableSystem.TurboType.Robot;
                kind = TurboConsumableSystem.UpgradeKind.Duration;
                return true;
            default:
                type = default;
                kind = default;
                return false;
        }
    }

    private static long GetArrayCost(long[] costs, int level)
    {
        return level >= 0 && level < costs.Length ? costs[level] : 0;
    }

    private static float GetEggValueMultiplier(int level)
    {
        return EggValueMultipliers[
            Mathf.Clamp(level, 0, EggValueMultipliers.Length - 1)];
    }

    private static float GetTruckBonusMultiplier(int level)
    {
        return Mathf.Pow(
            TruckBonusGrowthPerLevel,
            Mathf.Clamp(level, 0, TruckBonusCosts.Length));
    }

    private static float GetEggWeightChance(int level)
    {
        return Mathf.Clamp01(level * EggWeightChancePerLevel);
    }

    private static float GetEggWeightUpperMultiplier(int level)
    {
        return 1f + Mathf.Clamp(level, 0, EggWeightCosts.Length)
            * EggWeightUpperRangePerLevel;
    }

    private static string GetEggWeightDescription(int level)
    {
        int clampedLevel = Mathf.Clamp(level, 0, EggWeightCosts.Length);
        return
            $"{GetEggWeightChance(clampedLevel) * 100f:0}% chance to lay " +
            $"heavier/bigger eggs . Weight multiplies cash value . Up to " +
            $"{GetEggWeightUpperMultiplier(clampedLevel) * 100f:0.#}% size/weight/value";
    }

    private static string GetRareChanceDescription(int level)
    {
        GetRareChances(
            level,
            out float rare,
            out float epic,
            out float legendary,
            out float cosmic);
        return
            $"{rare * 100f:0.###}% chance to lay rare eggs . " +
            $"Epic {epic * 100f:0.###}% . " +
            $"Legendary {legendary * 100f:0.###}% . " +
            $"Cosmic {cosmic * 100f:0.####}%";
    }

    private static string GetChickenPerksDescription(int level)
    {
        int clampedLevel = Mathf.Clamp(
            level,
            0,
            ChickenPerkCosts.Length);
        float white = 1f + clampedLevel
            * ChickenPerkBoostPerLevel[
                (int)ChickenController.ChickenBreed.White];
        float blue = 1f + clampedLevel
            * ChickenPerkBoostPerLevel[
                (int)ChickenController.ChickenBreed.Blue];
        float rainbow = 1f + clampedLevel
            * ChickenPerkBoostPerLevel[
                (int)ChickenController.ChickenBreed.Rainbow];
        float cosmic = 1f + clampedLevel
            * ChickenPerkBoostPerLevel[
                (int)ChickenController.ChickenBreed.Cosmic];
        return
            "Multiplies each breed's final premium egg chance . "
            + $"White {white:0.##}x . Blue {blue:0.##}x . "
            + $"Rainbow {rainbow:0.##}x . Cosmic {cosmic:0.##}x";
    }

    private static void GetRareChances(
        int level,
        out float rare,
        out float epic,
        out float legendary,
        out float cosmic)
    {
        int index = Mathf.Clamp(level, 0, RareChanceByLevel.Length - 1);
        rare = RareChanceByLevel[index];
        epic = EpicChanceByLevel[index];
        legendary = LegendaryChanceByLevel[index];
        cosmic = CosmicChanceByLevel[index];
    }

    private static string FormatMoney(long cents)
    {
        return $"${cents / 100:N0}.{Math.Abs(cents % 100):D2}";
    }

    private static void NotifyChanged()
    {
        Changed?.Invoke();
    }
}
