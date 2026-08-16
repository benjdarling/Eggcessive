using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-150)]
[DisallowMultipleComponent]
public sealed class PenExpansionManager : MonoBehaviour
{
    public const int AdditionalPenUnlockRound = 20;

    public enum EquipmentType
    {
        Incubator,
        Crosshatcher,
        Robot,
        AutoFeeder
    }

    public enum EquipmentUpgrade
    {
        IncubatorCapacity,
        IncubatorSpeed,
        CrosshatcherSpeed,
        CrosshatcherQuality,
        RobotSpeed,
        RobotCapacity,
        RobotSmartness,
        AutoFeederSpeed,
        RobotVacuum,
        AutoFeederRange
    }

    private const int RuntimeGroundMaskResolution = 64;
    private const int IncubatorInstallCost = 800;
    private const int CrosshatcherInstallCost = 15000;
    private const int RobotInstallCost = 120000;
    private const int AutoFeederInstallCost = 2500000;
    private const float PenGap = 20f;

    private static readonly int[] IncubatorCapacityCosts =
    {
        2500, 12000
    };

    private static readonly int[] IncubatorSpeedCosts =
    {
        4000, 20000
    };

    private static readonly int[] CrosshatcherSpeedCosts =
    {
        22000, 55000, 140000, 360000, 900000,
        2200000, 5500000, 14000000, 35000000
    };

    private static readonly int[] CrosshatcherQualityCosts =
    {
        30000, 75000, 190000, 480000, 1200000,
        3000000, 7500000, 19000000, 48000000
    };

    private static readonly int[] RobotSpeedCosts =
    {
        180000, 650000, 2400000, 9000000, 35000000
    };

    private static readonly int[] RobotCapacityCosts =
    {
        240000, 900000, 3400000, 13000000, 50000000
    };

    private static readonly int[] RobotSmartnessCosts =
    {
        600000, 4000000, 20000000, 100000000
    };

    private static readonly int[] RobotVacuumCosts =
    {
        300000, 1200000, 5000000, 22000000, 100000000
    };

    private static readonly int[] AutoFeederSpeedCosts =
    {
        7500000, 30000000
    };

    private static readonly int[] AutoFeederRangeCosts =
    {
        2500000, 10000000, 40000000, 160000000, 640000000
    };

    private static readonly EquipmentUpgrade[] IncubatorUpgrades =
    {
        EquipmentUpgrade.IncubatorCapacity,
        EquipmentUpgrade.IncubatorSpeed
    };

    private static readonly EquipmentUpgrade[] CrosshatcherUpgrades =
    {
        EquipmentUpgrade.CrosshatcherSpeed,
        EquipmentUpgrade.CrosshatcherQuality
    };

    private static readonly EquipmentUpgrade[] RobotUpgrades =
    {
        EquipmentUpgrade.RobotSpeed,
        EquipmentUpgrade.RobotCapacity,
        EquipmentUpgrade.RobotSmartness,
        EquipmentUpgrade.RobotVacuum
    };

    private static readonly EquipmentUpgrade[] AutoFeederUpgrades =
    {
        EquipmentUpgrade.AutoFeederSpeed,
        EquipmentUpgrade.AutoFeederRange
    };

    private sealed class PenSlot
    {
        public int costCents;
        public bool owned;
        public float horizontalOffset;
        public GameObject runtimeRoot;
        public Transform terrain;
        public InteractiveGrassSystem grass;
        public Material groundMaterial;
        public Texture2D groundMask;
        public DebugChickenSpawner spawner;
        public EggContainer eggContainer;
        public IncubatorController incubator;
        public CrosshatcherController crosshatcher;
        public AutoFeederController autoFeeder;
        public EggCollectorRobot robot;
        public bool robotOwned;
        public int robotSpeedLevel;
        public int robotCapacityLevel;
        public int robotSmartnessLevel;
        public int robotVacuumLevel;
        public PenTruckController truck;
        public GameObject sign;
        public TMP_Text chickenCountText;
    }

    [Header("Pen Layout")]
    [SerializeField, Min(2)] private int penCount = 8;
    [SerializeField, Min(0)] private int starterChickensPerPurchasedPen = 3;

    private static readonly int[] PenPurchaseCostsCents =
    {
        0,
        200000,
        1000000,
        5000000,
        25000000,
        100000000,
        500000000,
        2000000000
    };

    [Header("Distant Pen Visuals")]
    [Tooltip("Chicken animators/meshes and egg meshes beyond this horizontal distance are disabled. Physics remains active.")]
    [SerializeField, Min(1f)] private float visualActivationDistance = 8f;
    [SerializeField, Min(0.1f)] private float visualRefreshInterval = 0.5f;

    private readonly List<PenSlot> slots = new List<PenSlot>();
    private readonly List<Material> runtimeGroundMaterials = new List<Material>();
    private readonly List<Texture2D> runtimeGroundMasks = new List<Texture2D>();
    private Transform terrainTemplate;
    private Transform roadTemplate;
    private GameObject volumeTemplate;
    private InteractiveGrassSystem grassTemplate;
    private EggContainer containerTemplate;
    private IncubatorController incubatorTemplate;
    private CrosshatcherController crosshatcherTemplate;
    private AutoFeederController autoFeederTemplate;
    private GameObject signTemplate;
    private Vector3 baseCameraPivotPosition;
    private float penSpacing;
    private int focusedPenIndex;
    private int debugRobotSpawnCount;
    private float nextVisualRefreshTime;
    private Coroutine penPurchaseFinalization;
    private bool additionalPensUnlocked;

    public static PenExpansionManager Instance { get; private set; }
    public event Action StateChanged;
    public bool IsInitialized { get; private set; }
    public int PenCount => slots.Count;
    public int FocusedPenIndex => focusedPenIndex;
    public bool IsPenPurchaseInProgress => penPurchaseFinalization != null;
    public bool AreAdditionalPensUnlocked => additionalPensUnlocked;
    public int OwnedPenCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < slots.Count; index++)
            {
                if (slots[index].owned)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int NextUnownedPenIndex
    {
        get
        {
            for (int index = 0; index < slots.Count; index++)
            {
                if (!slots[index].owned)
                {
                    return index;
                }
            }

            return -1;
        }
    }

    private void Awake()
    {
        Instance = this;
        baseCameraPivotPosition = transform.position;
    }

    private void Start()
    {
        InitializePens();
    }

    private void OnEnable()
    {
        RoundSystem.PhaseChanged += HandleRoundPhaseChanged;
    }

    private void OnDisable()
    {
        RoundSystem.PhaseChanged -= HandleRoundPhaseChanged;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f7Key.wasPressedThisFrame)
        {
            bool spawned = TryDebugSpawnRobot();
            Debug.Log(
                spawned
                    ? $"F7 T3 test: added robot {debugRobotSpawnCount} and "
                        + $"enabled the crosshatcher in Pen {focusedPenIndex + 1}."
                    : "F7 T3 test: could not add a T3 robot and crosshatcher "
                        + "to the focused pen.",
                this);
        }

        if (!IsInitialized || Time.unscaledTime < nextVisualRefreshTime)
        {
            return;
        }

        nextVisualRefreshTime = Time.unscaledTime + visualRefreshInterval;
        RefreshDistantVisuals();
        RefreshPenChickenCounts();
    }

    private void OnDestroy()
    {
        for (int index = 0; index < runtimeGroundMaterials.Count; index++)
        {
            if (runtimeGroundMaterials[index] != null)
            {
                Destroy(runtimeGroundMaterials[index]);
            }
        }

        runtimeGroundMaterials.Clear();
        for (int index = 0; index < runtimeGroundMasks.Count; index++)
        {
            if (runtimeGroundMasks[index] != null)
            {
                Destroy(runtimeGroundMasks[index]);
            }
        }

        runtimeGroundMasks.Clear();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void HandleRoundPhaseChanged(RoundSystem.RoundPhase phase)
    {
        if (phase == RoundSystem.RoundPhase.InProgress)
        {
            return;
        }

        for (int index = 0; index < slots.Count; index++)
        {
            slots[index].robot?.FinalizeRound();
        }
    }

    public bool IsPenOwned(int index)
    {
        return IsValidIndex(index) && slots[index].owned;
    }

    public int GetPenCostCents(int index)
    {
        return IsValidIndex(index) ? slots[index].costCents : 0;
    }

    public void UnlockAdditionalPens()
    {
        if (additionalPensUnlocked)
        {
            return;
        }

        additionalPensUnlocked = true;
        StateChanged?.Invoke();
    }

    public int GetPenIndex(EggContainer container)
    {
        if (container == null)
        {
            return -1;
        }

        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index].eggContainer == container)
            {
                return index;
            }
        }

        return GetClosestPenIndex(container.transform.position);
    }

    public int GetClosestPenIndex(Vector3 worldPosition)
    {
        if (slots.Count == 0)
        {
            return 0;
        }

        float relativeX = worldPosition.x - baseCameraPivotPosition.x;
        return Mathf.Clamp(
            Mathf.RoundToInt(relativeX / Mathf.Max(0.1f, penSpacing)),
            0,
            slots.Count - 1);
    }

    public Vector3 GetPenCenter(int index)
    {
        Vector3 center = baseCameraPivotPosition;
        if (index >= 0 && index < slots.Count)
        {
            center.x += slots[index].horizontalOffset;
        }

        return center;
    }

    public Vector3 GetTruckStopPosition(int penIndex)
    {
        GameObject marker = GameObject.Find("truck_stop");
        Vector3 basePosition = marker != null
            ? marker.transform.position
            : new Vector3(0f, 0f, -3.5f);
        if (IsValidIndex(penIndex))
        {
            basePosition.x += slots[penIndex].horizontalOffset;
        }

        return basePosition;
    }

    public int GetChickenCount(int penIndex)
    {
        int count = 0;
        IReadOnlyList<ChickenController> chickens =
            ChickenController.ActiveInstances;
        for (int index = 0; index < chickens.Count; index++)
        {
            ChickenController chicken = chickens[index];
            if (chicken != null
                && GetClosestPenIndex(chicken.transform.position) == penIndex)
            {
                count++;
            }
        }

        return count;
    }

    public DebugChickenSpawner GetChickenSpawner(int penIndex)
    {
        return IsValidIndex(penIndex) && slots[penIndex].owned
            ? slots[penIndex].spawner
            : null;
    }

    public long GetPenEarningsCents(int penIndex)
    {
        return IsValidIndex(penIndex)
            && slots[penIndex].eggContainer != null
                ? slots[penIndex].eggContainer.TotalDepositedCents
                : 0L;
    }

    public bool HasRobotInPen(int penIndex)
    {
        return IsValidIndex(penIndex)
            && slots[penIndex].robotOwned
            && slots[penIndex].robot != null;
    }

    public EggCollectorRobot GetRobotInPen(int penIndex)
    {
        return IsValidIndex(penIndex) && slots[penIndex].robotOwned
            ? slots[penIndex].robot
            : null;
    }

    public static bool IsChickenCapReachedAt(
        Vector3 worldPosition,
        bool includeReservedCrosshatcherOutput = true)
    {
        PenExpansionManager manager = Instance;
        if (manager == null || !manager.IsInitialized)
        {
            return ChickenController.ActiveInstances.Count
                >= ChickenController.MaximumChickenCount;
        }

        int penIndex = manager.GetClosestPenIndex(worldPosition);
        int reservedOutputs = includeReservedCrosshatcherOutput
            && manager.slots[penIndex].crosshatcher != null
            && manager.slots[penIndex].crosshatcher.HasReservedChickenOutput
                ? 1
                : 0;
        return manager.GetChickenCount(penIndex) + reservedOutputs
            >= ChickenController.MaximumChickenCount;
    }

    public IncubatorController GetFocusedIncubator()
    {
        return GetIncubator(focusedPenIndex);
    }

    public IncubatorController GetIncubator(int penIndex)
    {
        return IsValidIndex(penIndex) && slots[penIndex].owned
            ? slots[penIndex].incubator
            : null;
    }

    public CrosshatcherController GetFocusedCrosshatcher()
    {
        return GetCrosshatcher(focusedPenIndex);
    }

    public CrosshatcherController GetCrosshatcher(int penIndex)
    {
        return IsValidIndex(penIndex) && slots[penIndex].owned
            ? slots[penIndex].crosshatcher
            : null;
    }

    public int GetFocusedTruckEggCount(int primaryPenEggCount)
    {
        if (!IsValidIndex(focusedPenIndex) || focusedPenIndex == 0)
        {
            return primaryPenEggCount;
        }

        PenTruckController focusedTruck = slots[focusedPenIndex].truck;
        return focusedTruck != null
            ? focusedTruck.EggsTowardTruck
            : 0;
    }

    public void SynchronizeEquipmentAcrossPens()
    {
        // Equipment is intentionally local to each pen. Kept as a no-op for
        // compatibility with older scene shop components.
    }

    public bool IsEquipmentOwned(int penIndex, EquipmentType type)
    {
        if (!IsValidIndex(penIndex) || !slots[penIndex].owned)
        {
            return false;
        }

        PenSlot slot = slots[penIndex];
        return type switch
        {
            EquipmentType.Incubator => slot.incubator != null
                && slot.incubator.gameObject.activeSelf,
            EquipmentType.Crosshatcher => slot.crosshatcher != null
                && slot.crosshatcher.gameObject.activeSelf,
            EquipmentType.Robot => slot.robotOwned,
            EquipmentType.AutoFeeder => slot.autoFeeder != null
                && slot.autoFeeder.gameObject.activeSelf,
            _ => false
        };
    }

    public bool HasCompletedCoreUpgrades(EquipmentType type)
    {
        for (int penIndex = 0; penIndex < slots.Count; penIndex++)
        {
            if (!slots[penIndex].owned || !IsEquipmentOwned(penIndex, type))
            {
                continue;
            }

            bool completed = type switch
            {
                EquipmentType.Incubator =>
                    IsUpgradeMaxed(penIndex, EquipmentUpgrade.IncubatorCapacity)
                    && IsUpgradeMaxed(penIndex, EquipmentUpgrade.IncubatorSpeed),
                EquipmentType.Crosshatcher =>
                    IsUpgradeMaxed(penIndex, EquipmentUpgrade.CrosshatcherSpeed)
                    && IsUpgradeMaxed(penIndex, EquipmentUpgrade.CrosshatcherQuality),
                EquipmentType.Robot =>
                    IsUpgradeMaxed(penIndex, EquipmentUpgrade.RobotSpeed)
                    && IsUpgradeMaxed(penIndex, EquipmentUpgrade.RobotCapacity),
                _ => false
            };

            if (completed)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsUpgradeMaxed(int penIndex, EquipmentUpgrade upgrade)
    {
        return GetUpgradeLevel(penIndex, upgrade)
            >= GetMaximumUpgradeLevel(upgrade);
    }

    public int GetEquipmentPurchaseCost(EquipmentType type)
    {
        return type switch
        {
            EquipmentType.Incubator => IncubatorInstallCost,
            EquipmentType.Crosshatcher => CrosshatcherInstallCost,
            EquipmentType.Robot => RobotInstallCost,
            EquipmentType.AutoFeeder => AutoFeederInstallCost,
            _ => 0
        };
    }

    public int GetUpgradeLevel(int penIndex, EquipmentUpgrade upgrade)
    {
        if (!IsValidIndex(penIndex))
        {
            return 0;
        }

        PenSlot slot = slots[penIndex];
        return upgrade switch
        {
            EquipmentUpgrade.IncubatorCapacity =>
                IsEquipmentOwned(penIndex, EquipmentType.Incubator)
                    ? slot.incubator.CapacityLevel : 0,
            EquipmentUpgrade.IncubatorSpeed =>
                IsEquipmentOwned(penIndex, EquipmentType.Incubator)
                    ? slot.incubator.SpeedLevel : 0,
            EquipmentUpgrade.CrosshatcherSpeed =>
                IsEquipmentOwned(penIndex, EquipmentType.Crosshatcher)
                    ? slot.crosshatcher.SpeedLevel : 0,
            EquipmentUpgrade.CrosshatcherQuality =>
                IsEquipmentOwned(penIndex, EquipmentType.Crosshatcher)
                    ? slot.crosshatcher.QualityLevel : 0,
            EquipmentUpgrade.RobotSpeed => slot.robotOwned
                ? slot.robotSpeedLevel : 0,
            EquipmentUpgrade.RobotCapacity => slot.robotOwned
                ? slot.robotCapacityLevel : 0,
            EquipmentUpgrade.RobotSmartness => slot.robotOwned
                ? slot.robotSmartnessLevel : 0,
            EquipmentUpgrade.RobotVacuum => slot.robotOwned
                ? slot.robotVacuumLevel : 0,
            EquipmentUpgrade.AutoFeederSpeed =>
                IsEquipmentOwned(penIndex, EquipmentType.AutoFeeder)
                    ? slot.autoFeeder.SpeedLevel : 0,
            EquipmentUpgrade.AutoFeederRange =>
                IsEquipmentOwned(penIndex, EquipmentType.AutoFeeder)
                    ? slot.autoFeeder.AttractionRangeLevel : 0,
            _ => 0
        };
    }

    public int GetMaximumUpgradeLevel(EquipmentUpgrade upgrade)
    {
        return upgrade switch
        {
            EquipmentUpgrade.IncubatorCapacity
                or EquipmentUpgrade.IncubatorSpeed =>
                    IncubatorController.MaximumLevel,
            EquipmentUpgrade.CrosshatcherSpeed
                or EquipmentUpgrade.CrosshatcherQuality =>
                    CrosshatcherController.MaximumLevel,
            EquipmentUpgrade.RobotSpeed
                or EquipmentUpgrade.RobotCapacity =>
                    EggCarryController.MaximumRobotLevel,
            EquipmentUpgrade.RobotSmartness =>
                EggCollectorRobot.ChickenArmsSmartnessLevel,
            EquipmentUpgrade.RobotVacuum =>
                EggCollectorRobot.MaximumVacuumLevel,
            EquipmentUpgrade.AutoFeederSpeed =>
                AutoFeederController.MaximumLevel,
            EquipmentUpgrade.AutoFeederRange =>
                AutoFeederController.MaximumAttractionRangeLevel,
            _ => 3
        };
    }

    public int GetUpgradeCost(int penIndex, EquipmentUpgrade upgrade)
    {
        EquipmentType owner = GetUpgradeOwner(upgrade);
        if (!IsEquipmentOwned(penIndex, owner))
        {
            return 0;
        }

        int level = GetUpgradeLevel(penIndex, upgrade);
        int maximum = GetMaximumUpgradeLevel(upgrade);
        if (level >= maximum)
        {
            return 0;
        }

        int[] costs = upgrade switch
        {
            EquipmentUpgrade.IncubatorCapacity => IncubatorCapacityCosts,
            EquipmentUpgrade.IncubatorSpeed => IncubatorSpeedCosts,
            EquipmentUpgrade.CrosshatcherSpeed => CrosshatcherSpeedCosts,
            EquipmentUpgrade.CrosshatcherQuality => CrosshatcherQualityCosts,
            EquipmentUpgrade.RobotSpeed => RobotSpeedCosts,
            EquipmentUpgrade.RobotCapacity => RobotCapacityCosts,
            EquipmentUpgrade.RobotSmartness => RobotSmartnessCosts,
            EquipmentUpgrade.RobotVacuum => RobotVacuumCosts,
            EquipmentUpgrade.AutoFeederSpeed => AutoFeederSpeedCosts,
            EquipmentUpgrade.AutoFeederRange => AutoFeederRangeCosts,
            _ => null
        };
        int costIndex = upgrade == EquipmentUpgrade.RobotSmartness
            || upgrade == EquipmentUpgrade.RobotVacuum
            || upgrade == EquipmentUpgrade.AutoFeederRange
            ? level
            : level - 1;
        return costs != null && costIndex >= 0 && costIndex < costs.Length
            ? costs[costIndex]
            : 0;
    }

    public bool TryPurchaseEquipment(EquipmentType type)
    {
        return TryPurchaseEquipment(focusedPenIndex, type);
    }

    public bool TryPurchaseEquipment(int penIndex, EquipmentType type)
    {
        if (!IsValidIndex(penIndex)
            || !slots[penIndex].owned
            || IsEquipmentOwned(penIndex, type)
            || (type == EquipmentType.Incubator
                && slots[penIndex].incubator == null)
            || (type == EquipmentType.Crosshatcher
                && slots[penIndex].crosshatcher == null)
            || (type == EquipmentType.AutoFeeder
                && slots[penIndex].autoFeeder == null)
            || (type == EquipmentType.Robot
                && EggCarryController.Instance == null))
        {
            return false;
        }

        int cost = GetEquipmentPurchaseCost(type);
        if (!EggScoreHud.TrySpendCents(cost))
        {
            StateChanged?.Invoke();
            return false;
        }

        PenSlot slot = slots[penIndex];
        switch (type)
        {
            case EquipmentType.Incubator:
                slot.incubator?.InstallOrUpgrade(1, 1);
                break;
            case EquipmentType.Crosshatcher:
                slot.crosshatcher?.InstallOrUpgrade(1, 1);
                break;
            case EquipmentType.Robot:
                slot.robotOwned = true;
                slot.robotSpeedLevel = 1;
                slot.robotCapacityLevel = 1;
                slot.robotSmartnessLevel = 0;
                slot.robotVacuumLevel = 0;
                RefreshRobot(slot);
                break;
            case EquipmentType.AutoFeeder:
                slot.autoFeeder?.InstallOrUpgrade(1, 0);
                break;
        }

        RoundSystem.Instance?.PlayCashRegisterSfx();
        StateChanged?.Invoke();
        return IsEquipmentOwned(penIndex, type);
    }

    public bool TryUpgradeEquipment(EquipmentUpgrade upgrade)
    {
        return TryUpgradeEquipment(focusedPenIndex, upgrade);
    }

    public bool TryUpgradeEquipment(int penIndex, EquipmentUpgrade upgrade)
    {
        int cost = GetUpgradeCost(penIndex, upgrade);
        if (cost <= 0 || !EggScoreHud.TrySpendCents(cost))
        {
            StateChanged?.Invoke();
            return false;
        }

        PenSlot slot = slots[penIndex];
        switch (upgrade)
        {
            case EquipmentUpgrade.IncubatorCapacity:
                slot.incubator.InstallOrUpgrade(
                    slot.incubator.CapacityLevel + 1,
                    slot.incubator.SpeedLevel);
                break;
            case EquipmentUpgrade.IncubatorSpeed:
                slot.incubator.InstallOrUpgrade(
                    slot.incubator.CapacityLevel,
                    slot.incubator.SpeedLevel + 1);
                break;
            case EquipmentUpgrade.CrosshatcherSpeed:
                slot.crosshatcher.InstallOrUpgrade(
                    slot.crosshatcher.SpeedLevel + 1,
                    slot.crosshatcher.QualityLevel);
                break;
            case EquipmentUpgrade.CrosshatcherQuality:
                slot.crosshatcher.InstallOrUpgrade(
                    slot.crosshatcher.SpeedLevel,
                    slot.crosshatcher.QualityLevel + 1);
                break;
            case EquipmentUpgrade.RobotSpeed:
                slot.robotSpeedLevel++;
                RefreshRobot(slot);
                break;
            case EquipmentUpgrade.RobotCapacity:
                slot.robotCapacityLevel++;
                RefreshRobot(slot);
                break;
            case EquipmentUpgrade.RobotSmartness:
                slot.robotSmartnessLevel++;
                RefreshRobot(slot);
                break;
            case EquipmentUpgrade.RobotVacuum:
                slot.robotVacuumLevel++;
                RefreshRobot(slot);
                break;
            case EquipmentUpgrade.AutoFeederSpeed:
                slot.autoFeeder.InstallOrUpgrade(
                    slot.autoFeeder.SpeedLevel + 1);
                break;
            case EquipmentUpgrade.AutoFeederRange:
                slot.autoFeeder.InstallOrUpgrade(
                    slot.autoFeeder.SpeedLevel,
                    slot.autoFeeder.AttractionRangeLevel + 1);
                break;
        }

        RoundSystem.Instance?.PlayCashRegisterSfx();
        StateChanged?.Invoke();
        return true;
    }

    public bool HasAffordableUpgrade(int penIndex, EquipmentType type)
    {
        EquipmentUpgrade[] upgrades = GetUpgrades(type);
        long balance = EggScoreHud.CurrentCents;
        for (int index = 0; index < upgrades.Length; index++)
        {
            int cost = GetUpgradeCost(penIndex, upgrades[index]);
            if (cost > 0 && balance >= cost)
            {
                return true;
            }
        }

        return false;
    }

    public static EquipmentUpgrade[] GetUpgrades(EquipmentType type)
    {
        return type switch
        {
            EquipmentType.Incubator => IncubatorUpgrades,
            EquipmentType.Crosshatcher => CrosshatcherUpgrades,
            EquipmentType.AutoFeeder => AutoFeederUpgrades,
            _ => RobotUpgrades
        };
    }

    private static EquipmentType GetUpgradeOwner(EquipmentUpgrade upgrade)
    {
        return upgrade switch
        {
            EquipmentUpgrade.IncubatorCapacity
                or EquipmentUpgrade.IncubatorSpeed => EquipmentType.Incubator,
            EquipmentUpgrade.CrosshatcherSpeed
                or EquipmentUpgrade.CrosshatcherQuality => EquipmentType.Crosshatcher,
            EquipmentUpgrade.AutoFeederSpeed
                or EquipmentUpgrade.AutoFeederRange => EquipmentType.AutoFeeder,
            _ => EquipmentType.Robot
        };
    }

    public bool TryPurchaseNextPen()
    {
        int nextIndex = NextUnownedPenIndex;
        return nextIndex >= 0 && TryActivatePen(nextIndex);
    }

    public bool TryDebugActivateNextPen()
    {
        int nextIndex = NextUnownedPenIndex;
        return nextIndex >= 0
            && TryActivatePen(
                nextIndex,
                spendCurrency: false,
                requireRoundInProgress: false);
    }

    public bool TryDebugSpawnRobot()
    {
        if (!IsInitialized
            || !IsValidIndex(focusedPenIndex)
            || !slots[focusedPenIndex].owned
            || EggCarryController.Instance == null)
        {
            return false;
        }

        PenSlot slot = slots[focusedPenIndex];
        if (slot.crosshatcher == null)
        {
            return false;
        }

        if (!slot.crosshatcher.gameObject.activeSelf)
        {
            slot.crosshatcher.InstallOrUpgrade(1, 1);
        }

        const int visualTier = 3;
        int positionIndex = debugRobotSpawnCount;
        float angle = positionIndex * 137.5f * Mathf.Deg2Rad;
        float radius = 0.55f + positionIndex / 6 * 0.25f;
        Vector3 offset = new Vector3(
            Mathf.Cos(angle) * radius,
            0f,
            Mathf.Sin(angle) * radius);
        Vector3 spawnPosition = slot.eggContainer != null
            ? slot.eggContainer.DepositPosition + offset
            : slot.runtimeRoot != null
                ? slot.runtimeRoot.transform.position + offset
                : transform.position + offset;

        EggCollectorRobot robot = EggCarryController.Instance.CreatePenRobot(
            slot.eggContainer,
            slot.incubator,
            slot.crosshatcher,
            visualTier,
            visualTier,
            EggCollectorRobot.ChickenArmsSmartnessLevel,
            0,
            slot.runtimeRoot != null ? slot.runtimeRoot.transform : null,
            spawnPosition,
            Quaternion.Euler(0f, positionIndex * 47f, 0f));
        if (robot == null)
        {
            return false;
        }

        robot.EnableSingleChickenArmDebugMission();
        debugRobotSpawnCount++;
        robot.gameObject.name =
            $"Debug Robot {debugRobotSpawnCount:00} (T{visualTier})";
        return true;
    }

    public bool TryActivatePen(int index)
    {
        return TryActivatePen(
            index,
            spendCurrency: true,
            requireRoundInProgress: true);
    }

    private bool TryActivatePen(
        int index,
        bool spendCurrency,
        bool requireRoundInProgress)
    {
        if (!IsValidIndex(index))
        {
            return false;
        }

        PenSlot slot = slots[index];
        if (!slot.owned)
        {
            if (spendCurrency && !additionalPensUnlocked)
            {
                StateChanged?.Invoke();
                return false;
            }

            if (requireRoundInProgress
                && RoundSystem.Instance != null
                && !RoundSystem.Instance.IsRoundInProgress)
            {
                return false;
            }

            if (penPurchaseFinalization != null)
            {
                return false;
            }

            if (spendCurrency
                && !EggScoreHud.TrySpendCents(slot.costCents))
            {
                StateChanged?.Invoke();
                return false;
            }

            CreatePurchasedPen(index, slot);
            slot.owned = true;
            penPurchaseFinalization = StartCoroutine(
                FinalizePurchasedPen(slot));
            if (spendCurrency)
            {
                RoundSystem.Instance?.PlayCashRegisterSfx();
            }
        }

        FocusPen(index);
        return true;
    }

    public void FocusPen(int index)
    {
        if (!IsValidIndex(index) || !slots[index].owned)
        {
            return;
        }

        focusedPenIndex = index;
        Vector3 pivotPosition = baseCameraPivotPosition;
        pivotPosition.x += slots[index].horizontalOffset;
        transform.position = pivotPosition;
        EggContainer.SetFocusedContainer(slots[index].eggContainer);
        RefreshPenSigns();
        RefreshDistantVisuals();
        RoundSystem.Instance?.NotifyPenTruckProgressChanged();
        StateChanged?.Invoke();
    }

    private void InitializePens()
    {
        terrainTemplate = GameObject.Find("Terrain_Pens")?.transform;
        roadTemplate = GameObject.Find("Roads")?.transform;
        volumeTemplate = GameObject.Find("VolumePen");
        grassTemplate = FindFirstObjectByType<InteractiveGrassSystem>(
            FindObjectsInactive.Include);
        containerTemplate = EggContainer.Instance != null
            ? EggContainer.Instance
            : FindFirstObjectByType<EggContainer>(FindObjectsInactive.Include);
        incubatorTemplate = FindFirstObjectByType<IncubatorController>(
            FindObjectsInactive.Include);
        crosshatcherTemplate = FindFirstObjectByType<CrosshatcherController>(
            FindObjectsInactive.Include);
        autoFeederTemplate = FindFirstObjectByType<AutoFeederController>(
            FindObjectsInactive.Include);
        if (autoFeederTemplate == null)
        {
            Transform autoFeederLocation =
                GameObject.Find("Location_AutoFeeder")?.transform;
            GameObject autoFeederPrefab = Resources.Load<GameObject>(
                "AutoFeeder/prefab_AutoFeeder");
            if (autoFeederLocation != null && autoFeederPrefab != null)
            {
                GameObject instance = Instantiate(
                    autoFeederPrefab,
                    autoFeederLocation);
                instance.name = "AutoFeeder_1";
                instance.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                autoFeederTemplate =
                    instance.GetComponent<AutoFeederController>();
                instance.SetActive(false);
            }
        }
        signTemplate = GameObject.Find("Pen Sign Template");
        if (signTemplate == null)
        {
            Debug.LogWarning(
                "Pen Sign Template was not found. Pen signs will remain "
                + "disabled until an authored scene template is provided.",
                this);
        }

        if (terrainTemplate == null
            || roadTemplate == null
            || volumeTemplate == null
            || grassTemplate == null
            || containerTemplate == null)
        {
            Debug.LogError(
                "Pen expansion needs Terrain_Pens, Roads, VolumePen, Interactive "
                + "Grass, and the original EggContainer in the scene.",
                this);
            enabled = false;
            return;
        }

        float measuredTerrainWidth = MeasureTerrainWidth();
        if (measuredTerrainWidth > 0.1f)
        {
            // Keep the authored ProBuilder mesh at scale one so its renderer
            // and collider retain exactly the authored dimensions. Pen centres
            // are separated by the surface width plus a clear twenty-metre gap.
            penSpacing = measuredTerrainWidth + PenGap;
            grassTemplate.ConfigureRuntimePen(
                terrainTemplate,
                Vector3.zero,
                null,
                true);
        }
        else
        {
            Debug.LogError(
                "Terrain_Pens must contain a rendered grass_pen surface.",
                this);
            enabled = false;
            return;
        }

        SynchronizeGroundMaskRects();
        Material baseGroundMaterial =
            CreateRuntimeGroundMaterial(0, terrainTemplate);
        grassTemplate.ConfigureRuntimePen(
            terrainTemplate,
            Vector3.zero,
            baseGroundMaterial,
            true,
            false);

        slots.Clear();
        bool starterIncubatorInstalled = incubatorTemplate != null
            && incubatorTemplate.gameObject.activeSelf;
        bool starterCrosshatcherInstalled = crosshatcherTemplate != null
            && crosshatcherTemplate.gameObject.activeSelf;
        bool starterAutoFeederInstalled = autoFeederTemplate != null
            && autoFeederTemplate.gameObject.activeSelf;
        for (int index = 0; index < penCount; index++)
        {
            slots.Add(new PenSlot
            {
                costCents = index == 0 ? 0 : CalculateCost(index),
                owned = index == 0,
                horizontalOffset = index * penSpacing,
                terrain = index == 0 ? terrainTemplate : null,
                grass = index == 0 ? grassTemplate : null,
                groundMaterial = index == 0 ? baseGroundMaterial : null,
                spawner = index == 0
                    ? volumeTemplate.GetComponent<DebugChickenSpawner>()
                    : null,
                eggContainer = index == 0 ? containerTemplate : null,
                incubator = index == 0 ? incubatorTemplate : null,
                crosshatcher = index == 0 ? crosshatcherTemplate : null,
                autoFeeder = index == 0 ? autoFeederTemplate : null
            });
        }

        focusedPenIndex = 0;
        if (incubatorTemplate != null && !starterIncubatorInstalled)
        {
            incubatorTemplate.gameObject.SetActive(false);
        }
        if (crosshatcherTemplate != null && !starterCrosshatcherInstalled)
        {
            crosshatcherTemplate.gameObject.SetActive(false);
        }
        if (autoFeederTemplate != null && !starterAutoFeederInstalled)
        {
            autoFeederTemplate.gameObject.SetActive(false);
        }
        EggContainer.SetFocusedContainer(containerTemplate);
        slots[0].sign = signTemplate;
        ConfigurePenSign(slots[0].sign, 0);
        slots[0].chickenCountText = FindSignText(
            slots[0].sign,
            "Chicken Count");
        RefreshPenSigns();
        RefreshPenChickenCounts();
        IsInitialized = true;
        StartCoroutine(RefreshPenGroundCoverageTimeSliced(slots[0]));
        RefreshDistantVisuals();
        StateChanged?.Invoke();
    }

    private int CalculateCost(int index)
    {
        return index >= 0 && index < PenPurchaseCostsCents.Length
            ? PenPurchaseCostsCents[index]
            : int.MaxValue;
    }

    private float MeasureTerrainWidth()
    {
        Transform penSurface = terrainTemplate.Find("grass_pen");
        Renderer renderer = penSurface != null
            ? penSurface.GetComponent<Renderer>()
            : null;
        return renderer != null ? renderer.bounds.size.x : 0f;
    }

    private void SynchronizeGroundMaskRects()
    {
        Material source = grassTemplate.GroundColourSource;
        if (source == null)
        {
            return;
        }

        Vector4 innerWorldRect = GetGrassWorldRect(grassTemplate, false);
        Vector4 outerWorldRect = GetGrassWorldRect(grassTemplate, true);

        if (source.HasProperty("_MaskWorldRect"))
        {
            source.SetVector("_MaskWorldRect", innerWorldRect);
        }

        if (source.HasProperty("_OuterMaskWorldRect"))
        {
            source.SetVector("_OuterMaskWorldRect", outerWorldRect);
        }
    }

    private void CreatePurchasedPen(int index, PenSlot slot)
    {
        Vector3 worldOffset = Vector3.right * slot.horizontalOffset;
        GameObject penRoot = new GameObject($"Pen {index + 1}");
        penRoot.transform.position = worldOffset;

        Transform terrain = CreateRuntimeTerrain(
            $"Terrain_Pens_{index + 1}",
            worldOffset,
            penRoot.transform);
        CreateRuntimeRoad(
            $"Roads_{index + 1}",
            worldOffset,
            penRoot.transform);
        Material groundMaterial =
            CreateRuntimeGroundMaterial(index, terrain);

        // Make the new terrain visible to the NavMesh bake performed by the
        // cloned spawner's Awake. This avoids baking it a second time below.
        Physics.SyncTransforms();
        GameObject volume;
        DebugChickenSpawner.BeginSuppressAutomaticNavMeshBuild();
        try
        {
            volume = Instantiate(
                volumeTemplate,
                volumeTemplate.transform.position + worldOffset,
                volumeTemplate.transform.rotation,
                penRoot.transform);
        }
        finally
        {
            DebugChickenSpawner.EndSuppressAutomaticNavMeshBuild();
        }
        volume.name = $"VolumePen_{index + 1}";
        DebugChickenSpawner clonedSpawner =
            volume.GetComponent<DebugChickenSpawner>();
        if (clonedSpawner != null)
        {
            clonedSpawner.enabled = false;
        }

        EggContainer container = Instantiate(
            containerTemplate,
            containerTemplate.transform.position + worldOffset,
            containerTemplate.transform.rotation,
            penRoot.transform);
        container.name = $"EggContainer_{index + 1}";
        container.SetFocused(false);

        IncubatorController incubator = CloneIncubator(
            index,
            worldOffset,
            penRoot.transform);
        CrosshatcherController crosshatcher = CloneCrosshatcher(
            index,
            worldOffset,
            penRoot.transform);
        AutoFeederController autoFeeder = CloneAutoFeeder(
            index,
            worldOffset,
            penRoot.transform);

        PenTruckController truck = penRoot.AddComponent<PenTruckController>();
        truck.Configure(container, worldOffset);

        InteractiveGrassSystem grass = grassTemplate.CreateRuntimeCopy(
            grassTemplate.transform.position + worldOffset,
            grassTemplate.transform.rotation,
            penRoot.transform,
            $"Interactive Grass {index + 1}",
            terrain,
            worldOffset,
            groundMaterial,
            true,
            false);

        slot.runtimeRoot = penRoot;
        slot.terrain = terrain;
        slot.grass = grass;
        slot.groundMaterial = groundMaterial;
        slot.spawner = clonedSpawner;
        slot.eggContainer = container;
        slot.incubator = incubator;
        slot.crosshatcher = crosshatcher;
        slot.autoFeeder = autoFeeder;
        slot.truck = truck;
        slot.sign = CreatePenSign(index, penRoot.transform);
        slot.chickenCountText = FindSignText(slot.sign, "Chicken Count");
        RefreshPenChickenCount(index);
    }

    private GameObject CreatePenSign(int penIndex, Transform parent)
    {
        if (signTemplate == null)
        {
            return null;
        }

        Vector3 worldPosition = signTemplate.transform.position
            + Vector3.right * slots[penIndex].horizontalOffset;
        GameObject root = Instantiate(
            signTemplate,
            worldPosition,
            signTemplate.transform.rotation,
            parent);
        root.name = $"Pen {penIndex + 1} Sign";
        ConfigurePenSign(root, penIndex);
        return root;
    }

    private static void ConfigurePenSign(GameObject sign, int penIndex)
    {
        if (sign == null)
        {
            return;
        }

        TMP_Text text = FindSignText(sign, "Pen Number");
        if (text != null)
        {
            text.text = (penIndex + 1).ToString();
            text.color = PenUiPalette.GetColour(penIndex);
        }
    }

    private static TMP_Text FindSignText(GameObject sign, string objectName)
    {
        if (sign == null)
        {
            return null;
        }

        TMP_Text[] texts = sign.GetComponentsInChildren<TMP_Text>(true);
        for (int index = 0; index < texts.Length; index++)
        {
            TMP_Text text = texts[index];
            if (text != null && text.gameObject.name == objectName)
            {
                return text;
            }
        }

        return null;
    }

    private void RefreshPenChickenCounts()
    {
        for (int index = 0; index < slots.Count; index++)
        {
            RefreshPenChickenCount(index);
        }
    }

    private void RefreshPenChickenCount(int penIndex)
    {
        if (penIndex < 0 || penIndex >= slots.Count)
        {
            return;
        }

        TMP_Text text = slots[penIndex].chickenCountText;
        if (text != null)
        {
            text.text = $"CHICKS: {GetChickenCount(penIndex)}";
        }
    }

    private void RefreshPenSigns()
    {
        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index].sign != null)
            {
                slots[index].sign.SetActive(
                    slots[index].owned && index == focusedPenIndex);
            }
        }
    }

    private IEnumerator FinalizePurchasedPen(PenSlot slot)
    {
        // Return control immediately after the purchase click, then divide the
        // expensive grass and this pen's small ground masks across frames.
        yield return null;
        if (slot.spawner != null)
        {
            DebugChickenSpawner sourceSpawner = volumeTemplate != null
                ? volumeTemplate.GetComponent<DebugChickenSpawner>()
                : null;
            if (!slot.spawner.TryUseNavMeshDataFrom(sourceSpawner))
            {
                // This should only be needed if the authored pen's bake failed.
                slot.spawner.RebuildPenNavMesh();
            }

            // Let the newly registered NavMesh instance enter the navigation
            // world before sampling it for starter chicken positions.
            yield return null;
            if (!slot.spawner.HasChickenNavMeshInVolume())
            {
                // Customised pen geometry cannot safely share the authored
                // bake. Rebuild only this exceptional case, then sample again.
                slot.spawner.RebuildPenNavMesh();
                yield return null;
            }

            slot.spawner.SpawnStarterChickens(
                starterChickensPerPurchasedPen);
        }

        yield return null;
        if (slot.grass != null)
        {
            yield return slot.grass.GenerateGrassTimeSliced();
        }

        yield return null;
        yield return RefreshPenGroundCoverageTimeSliced(slot);
        penPurchaseFinalization = null;
        StateChanged?.Invoke();
    }

    private IncubatorController CloneIncubator(
        int index,
        Vector3 worldOffset,
        Transform parent)
    {
        if (incubatorTemplate == null)
        {
            return null;
        }

        IncubatorController clone = Instantiate(
            incubatorTemplate,
            incubatorTemplate.transform.position + worldOffset,
            incubatorTemplate.transform.rotation,
            parent);
        clone.name = $"Incubator_{index + 1}";
        clone.gameObject.SetActive(false);
        return clone;
    }

    private CrosshatcherController CloneCrosshatcher(
        int index,
        Vector3 worldOffset,
        Transform parent)
    {
        if (crosshatcherTemplate == null)
        {
            return null;
        }

        CrosshatcherController clone = Instantiate(
            crosshatcherTemplate,
            crosshatcherTemplate.transform.position + worldOffset,
            crosshatcherTemplate.transform.rotation,
            parent);
        clone.name = $"Crosshatcher_{index + 1}";
        clone.gameObject.SetActive(false);
        return clone;
    }

    private AutoFeederController CloneAutoFeeder(
        int index,
        Vector3 worldOffset,
        Transform parent)
    {
        if (autoFeederTemplate == null)
        {
            return null;
        }

        AutoFeederController clone = Instantiate(
            autoFeederTemplate,
            autoFeederTemplate.transform.position + worldOffset,
            autoFeederTemplate.transform.rotation,
            parent);
        clone.name = $"AutoFeeder_{index + 1}";
        clone.gameObject.SetActive(false);
        return clone;
    }

    private void SynchronizeIncubator(IncubatorController target)
    {
        if (target == null || incubatorTemplate == null)
        {
            return;
        }

        bool installed = incubatorTemplate.gameObject.activeSelf;
        if (installed)
        {
            target.InstallOrUpgrade(
                incubatorTemplate.CapacityLevel,
                incubatorTemplate.SpeedLevel);
        }
        else
        {
            target.gameObject.SetActive(false);
        }
    }

    private void SynchronizeCrosshatcher(CrosshatcherController target)
    {
        if (target == null || crosshatcherTemplate == null)
        {
            return;
        }

        bool installed = crosshatcherTemplate.gameObject.activeSelf;
        if (installed)
        {
            target.InstallOrUpgrade(
                crosshatcherTemplate.SpeedLevel,
                crosshatcherTemplate.QualityLevel);
        }
        else
        {
            target.gameObject.SetActive(false);
        }
    }

    private void RefreshRobot(PenSlot slot)
    {
        if (slot == null || !slot.robotOwned)
        {
            return;
        }

        Vector3? existingPosition = null;
        Quaternion? existingRotation = null;
        if (slot.robot != null)
        {
            existingPosition = slot.robot.transform.position;
            existingRotation = slot.robot.transform.rotation;
            slot.robot.FinalizeRound();
            Destroy(slot.robot.gameObject);
            slot.robot = null;
        }

        EggCarryController collection = EggCarryController.Instance;
        if (collection == null)
        {
            return;
        }

        slot.robot = collection.CreatePenRobot(
            slot.eggContainer,
            slot.incubator,
            slot.crosshatcher,
            slot.robotSpeedLevel,
            slot.robotCapacityLevel,
            slot.robotSmartnessLevel,
            slot.robotVacuumLevel,
            slot.runtimeRoot != null ? slot.runtimeRoot.transform : null,
            existingPosition,
            existingRotation);
    }

    private void RefreshDistantVisuals()
    {
        for (int index = 0; index < slots.Count; index++)
        {
            InteractiveGrassSystem grass = slots[index].grass;
            if (grass == null)
            {
                continue;
            }

            bool shouldProcessGrass = slots[index].owned
                && index == focusedPenIndex;
            if (grass.enabled != shouldProcessGrass)
            {
                grass.enabled = shouldProcessGrass;
            }
        }

        float cameraX = transform.position.x;
        IReadOnlyList<ChickenController> chickens =
            ChickenController.ActiveInstances;
        for (int index = chickens.Count - 1; index >= 0; index--)
        {
            ChickenController chicken = chickens[index];
            if (chicken != null)
            {
                chicken.SetPenVisualsEnabled(
                    Mathf.Abs(chicken.transform.position.x - cameraX)
                    <= visualActivationDistance);
            }
        }

        IReadOnlyList<ChickenEgg> eggs = ChickenEgg.ActiveInstances;
        for (int index = eggs.Count - 1; index >= 0; index--)
        {
            ChickenEgg egg = eggs[index];
            if (egg != null)
            {
                egg.SetPenVisualsEnabled(
                    Mathf.Abs(egg.transform.position.x - cameraX)
                    <= visualActivationDistance);
            }
        }
    }

    private Transform CreateRuntimeTerrain(
        string objectName,
        Vector3 worldOffset,
        Transform parent)
    {
        return CreateRuntimeSurfaceGroup(
            terrainTemplate,
            objectName,
            worldOffset,
            parent);
    }

    private Transform CreateRuntimeRoad(
        string objectName,
        Vector3 worldOffset,
        Transform parent)
    {
        return CreateRuntimeSurfaceGroup(
            roadTemplate,
            objectName,
            worldOffset,
            parent);
    }

    private static Transform CreateRuntimeSurfaceGroup(
        Transform source,
        string objectName,
        Vector3 worldOffset,
        Transform parent)
    {
        GameObject groupObject = new GameObject(objectName);
        Transform group = groupObject.transform;
        group.SetParent(parent, false);
        group.SetPositionAndRotation(
            source.position + worldOffset,
            source.rotation);
        group.localScale = source.localScale;

        for (int index = 0; index < source.childCount; index++)
        {
            CreateRuntimeSurface(source.GetChild(index), group);
        }

        return group;
    }

    private static void CreateRuntimeSurface(
        Transform sourceSurface,
        Transform parent)
    {
        MeshFilter sourceFilter = sourceSurface.GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer =
            sourceSurface.GetComponent<MeshRenderer>();
        MeshCollider sourceCollider =
            sourceSurface.GetComponent<MeshCollider>();
        Mesh sourceMesh = sourceCollider != null
            && sourceCollider.sharedMesh != null
                ? sourceCollider.sharedMesh
                : sourceFilter != null
                    ? sourceFilter.sharedMesh
                    : null;
        if (sourceFilter == null
            || sourceMesh == null
            || sourceRenderer == null)
        {
            // Authored child groups, such as the pre-baked pen fence prefab,
            // should be copied intact. They are already generated in the
            // editor and require no runtime construction of their contents.
            if (sourceSurface.GetComponentInChildren<Renderer>(true) != null)
            {
                GameObject authoredGroup = UnityEngine.Object.Instantiate(
                    sourceSurface.gameObject,
                    parent,
                    false);
                authoredGroup.name = sourceSurface.name;
            }

            return;
        }

        GameObject surfaceObject = new GameObject(sourceSurface.name);
        surfaceObject.layer = sourceSurface.gameObject.layer;
        Transform surface = surfaceObject.transform;
        surface.SetParent(parent, false);
        surface.localPosition = sourceSurface.localPosition;
        surface.localRotation = sourceSurface.localRotation;
        surface.localScale = sourceSurface.localScale;

        MeshFilter filter = surfaceObject.AddComponent<MeshFilter>();
        // ProBuilder can replace the render filter's transient mesh during
        // initialization. Its baked collider mesh remains aligned with the
        // authored transform, so use it for both visuals and collision.
        filter.sharedMesh = sourceMesh;

        MeshRenderer renderer = surfaceObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = sourceRenderer.sharedMaterials;
        renderer.enabled = true;
        renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        renderer.receiveShadows = sourceRenderer.receiveShadows;
        renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        renderer.motionVectorGenerationMode =
            sourceRenderer.motionVectorGenerationMode;
        renderer.allowOcclusionWhenDynamic =
            sourceRenderer.allowOcclusionWhenDynamic;
        renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        renderer.rendererPriority = sourceRenderer.rendererPriority;

        if (sourceCollider != null)
        {
            MeshCollider collider =
                surfaceObject.AddComponent<MeshCollider>();
            collider.sharedMesh = sourceMesh;
            collider.sharedMaterial = sourceCollider.sharedMaterial;
            collider.convex = sourceCollider.convex;
            collider.isTrigger = sourceCollider.isTrigger;
            collider.cookingOptions = sourceCollider.cookingOptions;
            collider.enabled = sourceCollider.enabled;
        }
    }

    private Material CreateRuntimeGroundMaterial(int penIndex, Transform terrain)
    {
        Material source = grassTemplate.GroundColourSource;
        if (source == null)
        {
            return null;
        }

        var groundMaterial = new Material(source)
        {
            name = $"{source.name} Pen {penIndex + 1}"
        };
        runtimeGroundMaterials.Add(groundMaterial);

        // The fence and any future authored props are also children of the
        // terrain template. Only grass_pen is ground and should receive this
        // per-pen material instance.
        Transform groundSurface = terrain.Find("grass_pen");
        Renderer renderer = groundSurface != null
            ? groundSurface.GetComponent<Renderer>()
            : null;
        if (renderer != null)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                if (materials[materialIndex] != null)
                {
                    materials[materialIndex] = groundMaterial;
                }
            }

            renderer.enabled = true;
            renderer.sharedMaterials = materials;
        }

        return groundMaterial;
    }

    private IEnumerator RefreshPenGroundCoverageTimeSliced(PenSlot slot)
    {
        if (slot == null
            || slot.grass == null
            || slot.groundMaterial == null)
        {
            yield break;
        }

        Vector4 worldRect = GetGrassWorldRect(slot.grass, true);
        if (worldRect.z <= 0f
            || worldRect.w <= 0f)
        {
            yield break;
        }

        Texture2D groundMask = null;
        InteractiveGrassSystem[] coverageSources = { slot.grass };
        yield return slot.grass.CreateRuntimeGroundMaskTimeSliced(
            worldRect,
            RuntimeGroundMaskResolution,
            false,
            coverageSources,
            generatedMask => groundMask = generatedMask);

        if (groundMask == null)
        {
            yield break;
        }

        groundMask.name = "Runtime Pen Ground Mask";
        slot.groundMask = groundMask;
        runtimeGroundMasks.Add(groundMask);

        // A pen is now one isolated surface, so both shader slots sample the
        // same full-surface mask instead of maintaining inner/outer textures.
        slot.groundMaterial.SetTexture("_LayerMask", groundMask);
        slot.groundMaterial.SetTexture("_OuterLayerMask", groundMask);
        slot.groundMaterial.SetVector("_MaskWorldRect", worldRect);
        slot.groundMaterial.SetVector("_OuterMaskWorldRect", worldRect);
        slot.groundMaterial.SetFloat("_PlacedCoverageAvailable", 1f);
        slot.groundMaterial.SetFloat("_OuterPlacedCoverageAvailable", 1f);
    }

    private static Vector4 GetGrassWorldRect(
        InteractiveGrassSystem grass,
        bool outer)
    {
        Vector2 centre = outer ? grass.OuterAreaCenter : Vector2.zero;
        Vector2 size = outer ? grass.OuterAreaSize : grass.AreaSize;
        Vector2 halfSize = size * 0.5f;
        Vector3 minimum = new Vector3(
            float.PositiveInfinity,
            0f,
            float.PositiveInfinity);
        Vector3 maximum = new Vector3(
            float.NegativeInfinity,
            0f,
            float.NegativeInfinity);
        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = grass.transform.TransformPoint(
                    new Vector3(
                        centre.x + halfSize.x * x,
                        0f,
                        centre.y + halfSize.y * z));
                minimum = Vector3.Min(minimum, corner);
                maximum = Vector3.Max(maximum, corner);
            }
        }

        return new Vector4(
            minimum.x,
            minimum.z,
            Mathf.Max(0.0001f, maximum.x - minimum.x),
            Mathf.Max(0.0001f, maximum.z - minimum.z));
    }

    private bool IsValidIndex(int index)
    {
        return IsInitialized && index >= 0 && index < slots.Count;
    }

    private void OnValidate()
    {
        penCount = Mathf.Max(2, penCount);
        starterChickensPerPurchasedPen = Mathf.Max(
            0,
            starterChickensPerPurchasedPen);
        visualActivationDistance = Mathf.Max(1f, visualActivationDistance);
        visualRefreshInterval = Mathf.Max(0.1f, visualRefreshInterval);
    }
}

[DisallowMultipleComponent]
internal sealed class PenTruckController : MonoBehaviour
{
    private EggContainer container;
    private Vector3 penOffset;
    private Transform truck;
    private int eggsTowardTruck;
    private int pendingReplacements;
    private Coroutine replacement;

    public int EggsTowardTruck => eggsTowardTruck;

    public void Configure(EggContainer targetContainer, Vector3 offset)
    {
        container = targetContainer;
        penOffset = offset;
        if (RoundSystem.Instance != null
            && RoundSystem.Instance.IsRoundInProgress)
        {
            eggsTowardTruck = 0;
            pendingReplacements = 0;
            SpawnTruckAtStop();
        }
    }

    private void OnEnable()
    {
        EggContainer.EggCollectedFromContainer += HandleEggCollected;
        RoundSystem.RoundStarted += HandleRoundStarted;
        RoundSystem.RoundEnded += HandleRoundEnded;
    }

    private void OnDisable()
    {
        EggContainer.EggCollectedFromContainer -= HandleEggCollected;
        RoundSystem.RoundStarted -= HandleRoundStarted;
        RoundSystem.RoundEnded -= HandleRoundEnded;
    }

    private void HandleRoundStarted(int _)
    {
        eggsTowardTruck = 0;
        pendingReplacements = 0;
        SpawnTruckAtStop();
    }

    private void HandleRoundEnded(int _)
    {
        eggsTowardTruck = 0;
        pendingReplacements = 0;
        if (replacement != null)
        {
            StopCoroutine(replacement);
            replacement = null;
        }

        DestroyTruck();
    }

    private void HandleEggCollected(EggContainer source, int _)
    {
        RoundSystem round = RoundSystem.Instance;
        if (source != container
            || round == null
            || !round.IsRoundInProgress
            || round.EggTarget <= 0)
        {
            return;
        }

        eggsTowardTruck++;
        while (eggsTowardTruck >= round.EggTarget)
        {
            eggsTowardTruck -= round.EggTarget;
            round.CompleteAdditionalPenTruckQuota(
                truck != null
                    ? truck.position
                    : GetStopPosition(),
                PenExpansionManager.Instance != null
                    ? PenExpansionManager.Instance.GetPenIndex(container)
                    : 0);
            pendingReplacements++;
            if (replacement == null)
            {
                replacement = StartCoroutine(ReplaceTruck());
            }
        }

        round.NotifyPenTruckProgressChanged();
    }

    private System.Collections.IEnumerator ReplaceTruck()
    {
        while (pendingReplacements > 0
            && RoundSystem.Instance != null
            && RoundSystem.Instance.IsRoundInProgress)
        {
            pendingReplacements--;
            if (truck != null)
            {
                RoundSystem.Instance?.PlayTruckBonusHornSfx();
                yield return MoveTruck(
                    GetStopPosition() + Vector3.right * 7f,
                    0.6f);
            }

            DestroyTruck();
            if (RoundSystem.Instance == null
                || !RoundSystem.Instance.IsRoundInProgress)
            {
                break;
            }

            SpawnTruck(GetStopPosition() - Vector3.right * 8f);
            yield return MoveTruck(GetStopPosition(), 0.6f);
        }

        replacement = null;
    }

    private System.Collections.IEnumerator MoveTruck(
        Vector3 destination,
        float duration)
    {
        if (truck == null)
        {
            yield break;
        }

        Vector3 start = truck.position;
        float elapsed = 0f;
        while (truck != null && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(elapsed / duration));
            truck.position = Vector3.Lerp(start, destination, progress);
            yield return null;
        }

        if (truck != null)
        {
            truck.position = destination;
        }
    }

    private void SpawnTruckAtStop()
    {
        DestroyTruck();
        SpawnTruck(GetStopPosition());
    }

    private void SpawnTruck(Vector3 position)
    {
        RoundSystem round = RoundSystem.Instance;
        GameObject visualPrefab = round != null
            ? round.TruckVisualPrefab
            : null;
        if (visualPrefab == null)
        {
            Debug.LogError("Pen truck is missing the shared truck visual prefab.", this);
            return;
        }

        GameObject root = Instantiate(visualPrefab);
        root.name = "Pen Delivery Truck";
        truck = root.transform;
        truck.SetParent(transform, true);
        truck.SetPositionAndRotation(
            position,
            Quaternion.Euler(0f, 90f, 0f));
    }

    private Vector3 GetStopPosition()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        int penIndex = manager != null
            ? manager.GetPenIndex(container)
            : -1;
        return manager != null && penIndex >= 0
            ? manager.GetTruckStopPosition(penIndex)
            : new Vector3(0f, 0f, -3.5f) + penOffset;
    }

    private void DestroyTruck()
    {
        if (truck == null)
        {
            return;
        }

        Destroy(truck.gameObject);
        truck = null;
    }
}
