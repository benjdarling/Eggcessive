using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GameplayTestBot : MonoBehaviour
{
    private readonly struct CrosshatchCandidate
    {
        public CrosshatchCandidate(
            ChickenController chicken,
            ChickenPickupTarget target,
            float pointerDistance)
        {
            Chicken = chicken;
            Target = target;
            PointerDistance = pointerDistance;
        }

        public ChickenController Chicken { get; }
        public ChickenPickupTarget Target { get; }
        public float PointerDistance { get; }
    }

    private readonly struct LocalInvestmentDecision
    {
        public LocalInvestmentDecision(
            int penIndex,
            PenExpansionManager.EquipmentType type,
            PenExpansionManager.EquipmentUpgrade? upgrade,
            int cost,
            int score,
            string label)
        {
            PenIndex = penIndex;
            Type = type;
            Upgrade = upgrade;
            Cost = cost;
            Score = score;
            Label = label;
        }

        public int PenIndex { get; }
        public PenExpansionManager.EquipmentType Type { get; }
        public PenExpansionManager.EquipmentUpgrade? Upgrade { get; }
        public int Cost { get; }
        public int Score { get; }
        public string Label { get; }
        public bool IsUpgrade => Upgrade.HasValue;
    }

    private static Mouse automationInputMouse;

    private static readonly Vector2[] FoodPlacementViewportOffsets =
    {
        Vector2.zero,
        new Vector2(-0.2f, 0f),
        new Vector2(0.2f, 0f),
        new Vector2(0f, -0.18f),
        new Vector2(0f, 0.18f),
        new Vector2(-0.19f, -0.16f),
        new Vector2(0.19f, -0.16f),
        new Vector2(-0.19f, 0.16f),
        new Vector2(0.19f, 0.16f),
        new Vector2(-0.1f, -0.09f),
        new Vector2(0.1f, -0.09f),
        new Vector2(-0.1f, 0.09f),
        new Vector2(0.1f, 0.09f),
        new Vector2(-0.1f, 0f),
        new Vector2(0.1f, 0f),
        new Vector2(0f, -0.09f),
        new Vector2(0f, 0.09f)
    };
    private const int MaximumEfficientRoundLeftovers = 2;
    private const int MaximumDesiredFeedPiles = 16;
    private const int ChickensPerDesiredFoodPile = 10;
    private const int ChickensPerVacuumFeedPile = 8;
    private const int FeedReserveBagsPerManualPen = 1;
    private const int MinimumLooseEggsForFeedThrottle = 6;
    private const float LooseEggsPerChickenForFeedThrottle = 0.25f;
    private const float CollectionLimitedFeedMultiplier = 0.7f;
    private const float WellFedAverageScore = 0.75f;
    private const float HungryFoodScore = 0.6f;
    private const int BasketRequiredChickenCount = 8;
    private const int MaximumRecoveryShopPurchases = 64;
    private const float BasketClusterSearchRadius = 1.25f;
    private const float ComfortableQuotaRatio = 1.25f;
    private const float HealthyCollectionRatio = 0.86f;
    private const int NormalLocalPurchasesPerRound = 32;
    private const int RecoveryLocalPurchasesPerRound = 64;
    private const int ProactiveShopPurchaseLimit = 64;
    private const int CriticalPopulationGrowthFlock = 12;
    private static int MinimumStrategicCrosshatchFlock =>
        CrosshatcherController.MinimumFlockSizeForNewCycle;
    private const float MinimumManualCrosshatchTimeRemaining = 6f;

    [Header("Operation")]
    [SerializeField] private bool startEnabled = false;
    [SerializeField, Min(100f)] private float pointerSpeed = 6000f;
    [SerializeField, Min(100f)] private float pointerAcceleration = 40000f;
    [SerializeField, Range(2f, 12f)] private float pointerSpringFrequency = 3.8f;
    [SerializeField, Range(0.35f, 0.95f)] private float pointerSpringDamping = 0.92f;
    [SerializeField, Range(0.9f, 1.4f)] private float pointerPrecisionDamping = 1.12f;
    [SerializeField, Min(10f)] private float pointerPrecisionRadius = 85f;
    [SerializeField, Range(0f, 30f)] private float pointerMaximumOvershoot = 1.5f;
    [SerializeField, Range(0.5f, 0.95f)] private float pointerOvershootSpeedRatio = 0.88f;
    [SerializeField, Min(0f)] private float pointerDwellTime = 0.08f;
    [SerializeField, Min(0.05f)] private float actionPause = 0.2f;
    [SerializeField, Min(0.5f)] private float vacuumHoldTime = 4f;

    [Header("Strategy")]
    [SerializeField, Min(0)] private int minimumFeedBags = 1;
    [SerializeField, Min(1)] private int maximumShopPurchasesPerVisit = 20;
    [SerializeField, Min(1)] private int automatedOwnedPenTarget = 8;
    [SerializeField, Min(1f)] private float penNavigationInterval = 8f;

    [Header("Authored Status UI")]
    [SerializeField] private TMP_Text statusText = null;

    private Coroutine automation;
    private Camera gameplayCamera;
    private RoundSystem.RoundPhase lastPhase;
    private bool hasLastPhase;
    private bool resultsSkipSent;
    private int shopPurchaseCount;
    private int shopUpgradeCursor;
    private int collectionActionCount;
    private int completedActions;
    private int desiredFeedPiles = 1;
    private int consecutiveFailedRounds;
    private int recoveryExpansionFailureCount = -1;
    private bool lastFailureWasCollectionLimited;
    private float lastQuotaRatio = 1f;
    private float lastCollectionRatio = 1f;
    private int lastRoundEggsLaid;
    private bool collectionRecoveryPurchasedThisShop;
    private bool collectionRecoveryPlannedThisShop;
    private long plannedCollectionRecoveryReserveCents;
    private int roundLocalPurchaseCount;
    private PenExpansionManager.EquipmentType? automationDialogType;
    private readonly Dictionary<int, int> foodPlacementAttemptsByPen =
        new Dictionary<int, int>();
    private readonly Dictionary<int, int> lastLooseEggsByPen =
        new Dictionary<int, int>();
    private readonly Dictionary<int, int> lastChickenCountsByPen =
        new Dictionary<int, int>();
    private readonly HashSet<int> plannedRobotRecoveryPens =
        new HashSet<int>();
    private readonly HashSet<int> pendingRobotRecoveryPens =
        new HashSet<int>();
    private readonly HashSet<int> foodPlacementSpacingBlockedPens =
        new HashSet<int>();
    private readonly List<ChickenEgg> vacuumTargetCandidates =
        new List<ChickenEgg>();
    private readonly List<ChickenEgg> basketTargetCandidates =
        new List<ChickenEgg>();
    private readonly List<ChickenEgg> manualTargetCandidates =
        new List<ChickenEgg>();
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private bool cursorStateCaptured;
    private bool isRunning;
    private Mouse physicalMouse;
    private Vector2 pointerVelocity;
    private float nextPenNavigationTime;
    private float nextTurboUseTime;
    private int vacuumReturnPenIndex = -1;

    public bool IsRunning => isRunning;
    public static Mouse PointerMouse =>
        automationInputMouse != null && automationInputMouse.added
            ? automationInputMouse
            : Mouse.current;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        automationInputMouse = null;
    }

    private void Awake()
    {
        SetStatus("OFF  .  F8 TO START");
    }

    private void Start()
    {
        if (startEnabled)
        {
            StartAutomation();
        }
    }

    private void Update()
    {
        if (Keyboard.current != null
            && Keyboard.current.f8Key.wasPressedThisFrame)
        {
            if (IsRunning)
            {
                StopAutomation();
            }
            else
            {
                StartAutomation();
            }
        }
    }

    private void OnDisable()
    {
        StopAutomation();
    }

    public void StartAutomation()
    {
        if (IsRunning)
        {
            return;
        }

        physicalMouse = Mouse.current;

        if (physicalMouse == null)
        {
            SetStatus("NO INPUT SYSTEM MOUSE");
            return;
        }

        automationInputMouse = InputSystem.AddDevice<Mouse>(
            "Gameplay Test Bot Mouse");
        Vector2 initialPosition = physicalMouse.position.ReadValue();
        InputState.Change(
            automationInputMouse,
            new MouseState { position = initialPosition });
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        cursorStateCaptured = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        hasLastPhase = false;
        completedActions = 0;
        desiredFeedPiles = Mathf.Max(1, minimumFeedBags);
        consecutiveFailedRounds = 0;
        recoveryExpansionFailureCount = -1;
        lastFailureWasCollectionLimited = false;
        lastQuotaRatio = 1f;
        lastCollectionRatio = 1f;
        lastRoundEggsLaid = 0;
        collectionRecoveryPurchasedThisShop = false;
        collectionRecoveryPlannedThisShop = false;
        plannedCollectionRecoveryReserveCents = 0;
        lastLooseEggsByPen.Clear();
        lastChickenCountsByPen.Clear();
        plannedRobotRecoveryPens.Clear();
        pendingRobotRecoveryPens.Clear();
        foodPlacementSpacingBlockedPens.Clear();
        roundLocalPurchaseCount = 0;
        automationDialogType = null;
        vacuumReturnPenIndex = -1;
        pointerVelocity = Vector2.zero;
        isRunning = true;
        EggCarryController.Instance?.SetAutomationRareEggProtection(true);
        automation = StartCoroutine(AutomationLoop());
    }

    public void StopAutomation()
    {
        if (automation != null)
        {
            StopCoroutine(automation);
            automation = null;
        }

        isRunning = false;
        ReleaseMouseButtons();
        EggCarryController.Instance?.SetAutomationRareEggProtection(false);
        EggCarryController.Instance?.CancelPointerInteraction();

        if (automationInputMouse != null)
        {
            if (automationInputMouse.added)
            {
                InputSystem.RemoveDevice(automationInputMouse);
            }

            automationInputMouse = null;
        }

        if (cursorStateCaptured)
        {
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            cursorStateCaptured = false;
        }

        SetStatus("OFF  .  F8 TO START");
    }

    private IEnumerator AutomationLoop()
    {
        while (true)
        {
            RoundSystem round = RoundSystem.Instance;

            if (round == null)
            {
                SetStatus("WAITING FOR ROUND SYSTEM");
                yield return new WaitForSecondsRealtime(0.25f);
                continue;
            }

            HandlePhaseChange(round.Phase);

            switch (round.Phase)
            {
                case RoundSystem.RoundPhase.Intermission:
                    yield return ClickNamedButton("Ready Button", "STARTING NEXT ROUND");
                    break;

                case RoundSystem.RoundPhase.Countdown:
                    SetStatus("COUNTDOWN  .  STANDING BY");
                    yield return new WaitForSecondsRealtime(0.15f);
                    break;

                case RoundSystem.RoundPhase.InProgress:
                    yield return PlayRound();
                    break;

                case RoundSystem.RoundPhase.Results:
                    yield return HandleResults();
                    break;

                case RoundSystem.RoundPhase.SuppliesShop:
                    yield return HandleShop();
                    break;

                default:
                    SetStatus($"{round.Phase.ToString().ToUpperInvariant()}  .  WAITING");
                    yield return new WaitForSecondsRealtime(0.15f);
                    break;
            }
        }
    }

    private void HandlePhaseChange(RoundSystem.RoundPhase phase)
    {
        if (hasLastPhase && lastPhase == phase)
        {
            return;
        }

        lastPhase = phase;
        hasLastPhase = true;
        ReleaseMouseButtons();
        pointerVelocity = Vector2.zero;

        if (phase == RoundSystem.RoundPhase.Results)
        {
            resultsSkipSent = false;
            RoundSystem round = RoundSystem.Instance;
            bool passed = round != null && round.DidPassRound;
            consecutiveFailedRounds = passed
                ? 0
                : consecutiveFailedRounds + 1;
            if (passed)
            {
                recoveryExpansionFailureCount = -1;
            }
            if (round != null)
            {
                lastRoundEggsLaid = round.RoundEggsLaid;
                lastQuotaRatio = round.CashQuotaCents > 0
                    ? Mathf.Max(
                        0f,
                        (float)(round.CashQuotaProgressCents
                            / (double)round.CashQuotaCents))
                    : 1f;
            lastCollectionRatio = round.RoundEggsLaid > 0
                    ? Mathf.Clamp01(
                        round.RoundEggsProcessed
                            / (float)round.RoundEggsLaid)
                    : 1f;
            }

            // Eggs intentionally sent to an incubator were successfully
            // processed, not missed collection opportunities. Only loose eggs
            // left behind should push recovery spending toward collection.
            lastFailureWasCollectionLimited = !passed
                && lastRoundEggsLaid > 0
                && lastCollectionRatio < HealthyCollectionRatio;
            CaptureRoundEndCollectionBacklog();
            UpdateFeedStrategy(round);
        }

        if (phase == RoundSystem.RoundPhase.SuppliesShop)
        {
            shopPurchaseCount = 0;
            shopUpgradeCursor = 0;
            collectionRecoveryPurchasedThisShop = false;
            collectionRecoveryPlannedThisShop = false;
            plannedCollectionRecoveryReserveCents = 0;
            plannedRobotRecoveryPens.Clear();
        }

        if (phase == RoundSystem.RoundPhase.InProgress)
        {
            collectionActionCount = 0;
            roundLocalPurchaseCount = 0;
            pendingRobotRecoveryPens.Clear();
            pendingRobotRecoveryPens.UnionWith(plannedRobotRecoveryPens);
            automationDialogType = null;
            foodPlacementAttemptsByPen.Clear();
            foodPlacementSpacingBlockedPens.Clear();
            vacuumReturnPenIndex = -1;
            nextPenNavigationTime = Time.unscaledTime + 0.5f;
            nextTurboUseTime = Time.unscaledTime + 0.35f;
        }
    }

    private IEnumerator PlayRound()
    {
        FoodShopController foodShop = FoodShopController.Instance;
        PenEquipmentHudController equipmentHud =
            PenEquipmentHudController.Instance;

        if (Time.unscaledTime >= nextTurboUseTime
            && RoundSystem.Instance != null
            && !RoundSystem.Instance.IsCashQuotaMet
            && RoundSystem.Instance.TimeRemaining > 5f)
        {
            nextTurboUseTime = Time.unscaledTime + 0.6f;
            for (int index = 0; index < 3; index++)
            {
                TurboConsumableSystem.TurboType type =
                    (TurboConsumableSystem.TurboType)index;
                if (TurboConsumableSystem.GetInventory(type) <= 0
                    || TurboConsumableSystem.IsActive(type)
                    || !TurboConsumableSystem.HasApplicableMachine(type))
                {
                    continue;
                }

                Button turboButton = FindNamedButton(
                    $"{TurboConsumableSystem.GetDisplayName(type)} Turbo Button");
                if (IsUsable(turboButton))
                {
                    yield return ClickButton(
                        turboButton,
                        $"ROUND  .  USING {TurboConsumableSystem.GetDisplayName(type).ToUpperInvariant()} TURBO");
                    yield break;
                }
            }
        }

        // A local-tech dialog blocks the entire playfield. Always finish or
        // dismiss it before considering pens, food, or egg collection; those
        // controls may still be active but cannot actually receive a click.
        if (equipmentHud != null && equipmentHud.IsUpgradeDialogOpen)
        {
            yield return HandleLocalTechDialog(equipmentHud);
            yield break;
        }

        if (FoodShopController.IsPlacementActive)
        {
            PenExpansionManager placementManager =
                PenExpansionManager.Instance;
            int placementPenIndex = placementManager != null
                ? placementManager.FocusedPenIndex
                : 0;
            bool placementPenUsesAutoFeeder = placementManager != null
                && placementManager.IsEquipmentOwned(
                    placementPenIndex,
                    PenExpansionManager.EquipmentType.AutoFeeder);
            bool finishRecoveryCoverage = ShouldForceRecoveryFeedCoverage();
            bool shouldContinuePlacement = foodShop != null
                && foodShop.OwnedFoodCount > 0
                && !placementPenUsesAutoFeeder
                && (finishRecoveryCoverage
                    || !ArePenChickensWellFed(
                        placementManager,
                        placementPenIndex))
                && CountAvailableFoodPiles(placementPenIndex)
                    < GetDesiredFeedPileCount(placementPenIndex);
            if (shouldContinuePlacement)
            {
                // Food placement now stays active. Keep clicking placement
                // positions for this pen's whole batch without reselecting the
                // food tool between piles.
                yield return TryPlaceFood();
            }
            else
            {
                foodShop?.CancelActivePlacement();
                SetStatus("FEED  .  BATCH COMPLETE");
                yield return new WaitForSecondsRealtime(0.05f);
            }

            yield break;
        }

        PenExpansionManager penManager = PenExpansionManager.Instance;
        int focusedPenIndex = penManager != null
            ? penManager.FocusedPenIndex
            : -1;
        EggCarryController collection = EggCarryController.Instance;

        // Once one parent has been committed, finishing the cycle recovers the
        // otherwise stranded chicken and must not wait for quota, spare time,
        // feeding, or another round. Fresh cycles keep the stricter safeguards
        // below; partial cycles are treated as mandatory recovery work.
        if (collection != null
            && TryGetPartiallyLoadedCrosshatcherPen(
                penManager,
                out int partialCrosshatcherPenIndex))
        {
            if (partialCrosshatcherPenIndex != focusedPenIndex)
            {
                yield return ClickNamedButton(
                    $"Pen {partialCrosshatcherPenIndex + 1} Button",
                    $"CROSSHATCHER  .  FINISHING PEN {partialCrosshatcherPenIndex + 1}");
                yield break;
            }

            CrosshatcherController partialCrosshatcher =
                penManager.GetCrosshatcher(partialCrosshatcherPenIndex);
            if (TryFindCrosshatcherChicken(
                    partialCrosshatcher,
                    out ChickenController secondParent,
                    out ChickenPickupTarget secondParentTarget))
            {
                collection.SelectHandTool();
                yield return LoadCrosshatcherChicken(
                    collection,
                    partialCrosshatcher,
                    secondParent,
                    secondParentTarget);
            }
            else
            {
                SetStatus("CROSSHATCHER 1/2  .  WAITING FOR SECOND PARENT");
                yield return new WaitForSecondsRealtime(0.1f);
            }

            yield break;
        }

        // A newly purchased pen cannot develop if every one of its few eggs is
        // immediately sold toward the current quota. Seed one egg into a
        // critically under-populated pen before cash collection, then let the
        // normal quota-first policy resume while the incubator is occupied.
        if (TryGetCriticalIdleIncubatorPen(
                penManager,
                out int growthPenIndex))
        {
            if (growthPenIndex != focusedPenIndex)
            {
                yield return ClickNamedButton(
                    $"Pen {growthPenIndex + 1} Button",
                    $"GROWTH  .  SEEDING PEN {growthPenIndex + 1} INCUBATOR");
                yield break;
            }

            if (collection != null && collection.HasVacuum)
            {
                collection.SelectCollectionTool();
                yield return UseVacuum(true);
            }
            else
            {
                collection?.SelectHandTool();
                yield return UseHand();
            }
            yield break;
        }

        // Purchased feed is productive only after it is placed. Cover every
        // manual pen before upgrades or collection, and finish the entire
        // placement batch even if loose eggs appear while the bot is placing.
        if (foodShop != null && foodShop.OwnedFoodCount > 0)
        {
            if (penManager != null
                && penManager.IsInitialized
                && TryGetPenNeedingFeedCoverage(
                    penManager,
                    out int feedPenIndex))
            {
                if (feedPenIndex != focusedPenIndex)
                {
                    yield return ClickNamedButton(
                        $"Pen {feedPenIndex + 1} Button",
                        $"FEED  .  COVERING PEN {feedPenIndex + 1}");
                }
                else
                {
                    yield return ClickNamedButton(
                        "Food Icon Button",
                        $"FEED  .  STOCKING PEN {feedPenIndex + 1}");
                }

                yield break;
            }

            if ((penManager == null || !penManager.IsInitialized)
                && CountAvailableFoodPiles()
                    < GetDesiredFeedPileCount())
            {
                yield return ClickNamedButton(
                    "Food Icon Button",
                    "FEED  .  STOCKING PRODUCTION PEN");
                yield break;
            }
        }

        bool hasPendingPenInvestment = TryGetNextOwnedPenInvestment(
            penManager,
            out _,
            out _,
            out int pendingPenInvestmentCost);
        LocalInvestmentDecision localInvestment = default;
        bool hasStrategicLocalInvestment = CanPurchaseMoreLocalTech();
        if (hasStrategicLocalInvestment)
        {
            hasStrategicLocalInvestment =
                TryGetPendingRobotRecoveryInvestment(
                    penManager,
                    out localInvestment)
                || TryGetStrategicLocalInvestment(
                    penManager,
                    -1,
                    null,
                    true,
                    out localInvestment);
        }

        // Spend on survival and throughput before collection. A busy farm can
        // have loose eggs continuously, so placing this behind the vacuum made
        // affordable incubators and pen upgrades unreachable during a failed
        // quota attempt.
        if (hasStrategicLocalInvestment
            && localInvestment.Score >= 2000)
        {
            yield return ExecuteLocalInvestment(localInvestment);
            yield break;
        }

        // Once production coverage and essential tech are in place, loose eggs
        // are already-produced quota value and should be cleared immediately.
        // Robot-equipped pens are included because a late-game robot can be
        // outpaced by its flock or occupied with a chicken-arm mission.
        if (NeedsCashQuotaDelivery()
            && collection != null
            && collection.HasVacuum
            && TryGetQuotaVacuumPen(
                penManager,
                focusedPenIndex,
                out int quotaVacuumPenIndex))
        {
            vacuumReturnPenIndex = -1;
            collection.SetAutomationRareEggProtection(true);
            if (quotaVacuumPenIndex >= 0
                && quotaVacuumPenIndex != focusedPenIndex)
            {
                yield return ClickNamedButton(
                    $"Pen {quotaVacuumPenIndex + 1} Button",
                    $"QUOTA  .  VACUUMING PEN {quotaVacuumPenIndex + 1} BACKLOG");
                yield break;
            }

            collection.SelectCollectionTool();
            yield return UseVacuum();
            yield break;
        }

        if (ShouldBuyNextPen(
                penManager,
                hasPendingPenInvestment,
                pendingPenInvestmentCost,
                out int nextPenIndex))
        {
            int ownedBefore = penManager.OwnedPenCount;
            Button buyPenButton = PenHudController.Instance != null
                ? PenHudController.Instance.GetPurchaseButton()
                : null;
            if (IsUsable(buyPenButton))
            {
                yield return ClickButton(
                    buyPenButton,
                    $"ROUND  .  BUYING PEN {nextPenIndex + 1}");
            }
            else
            {
                SetStatus($"ROUND  .  PEN {nextPenIndex + 1} PURCHASE UNAVAILABLE");
                yield return new WaitForSecondsRealtime(actionPause);
            }

            if (penManager.OwnedPenCount > ownedBefore)
            {
                if (consecutiveFailedRounds > 0)
                {
                    recoveryExpansionFailureCount =
                        consecutiveFailedRounds;
                }
                nextPenNavigationTime = Time.unscaledTime;
            }
            else
            {
                SetStatus($"ROUND  .  PEN {nextPenIndex + 1} PURCHASE NOT CONFIRMED");
                yield return new WaitForSecondsRealtime(actionPause);
            }

            yield break;
        }

        if (hasStrategicLocalInvestment)
        {
            yield return ExecuteLocalInvestment(localInvestment);
            yield break;
        }

        // Finish affordable equipment and upgrades throughout the existing
        // pens before considering expansion or returning to routine work.
        if (ShouldNavigateToAnotherPen(out int navigationTarget))
        {
            nextPenNavigationTime =
                Time.unscaledTime + penNavigationInterval;
            yield return ClickNamedButton(
                $"Pen {navigationTarget + 1} Button",
                $"ROUND  .  NAVIGATING TO PEN {navigationTarget + 1}");
            yield break;
        }

        if (collection == null)
        {
            SetStatus("ROUND  .  WAITING FOR COLLECTION TOOL");
            yield return new WaitForSecondsRealtime(0.2f);
            yield break;
        }

        collection.SetAutomationRareEggProtection(
            !NeedsHigherTierChickens());
        CrosshatcherController crosshatcher = FindCrosshatcher();
        PenExpansionManager activePenManager = PenExpansionManager.Instance;
        bool focusedIncubatorNeedsEgg = ShouldManuallySeedFocusedIncubator()
            && (activePenManager != null
                && activePenManager.IsInitialized
                    ? GetAvailableEggCount(
                    activePenManager,
                    activePenManager.FocusedPenIndex) > 0
                    : ChickenEgg.ActiveInstances.Count > 0);
        bool canManuallyLoadCrosshatcher = !focusedIncubatorNeedsEgg
            && CanSpendTimeOnManualCrosshatching();

        if (focusedIncubatorNeedsEgg)
        {
            // An owned vacuum should remain the bot's collection tool in every
            // pen. Its forced seed mode targets the lowest-value egg and stops
            // suction as soon as the incubator transfer has launched.
            if (collection.HasVacuum)
            {
                collection.SelectCollectionTool();
                yield return UseVacuum(true);
            }
            else
            {
                collection.SelectHandTool();
                yield return UseHand();
            }
            yield break;
        }

        if (canManuallyLoadCrosshatcher
            && TryFindCrosshatcherChicken(
                crosshatcher,
                out ChickenController crosshatchChicken,
                out ChickenPickupTarget pickupTarget))
        {
            collection.SelectHandTool();
            yield return LoadCrosshatcherChicken(
                collection,
                crosshatcher,
                crosshatchChicken,
                pickupTarget);
            yield break;
        }

        if (collection.HasVacuum)
        {
            collection.SelectCollectionTool();
            yield return UseVacuum();
        }
        else if (collection.BasketUpgradeLevel > 0)
        {
            collection.SelectCollectionTool();
            yield return UseBasket(collection);
        }
        else if (PenExpansionManager.Instance != null
            && PenExpansionManager.Instance.HasRobotInPen(
                PenExpansionManager.Instance.FocusedPenIndex)
            && !(IsFocusedIncubatorIdle()
                && GetAvailableEggCount(
                    PenExpansionManager.Instance,
                    PenExpansionManager.Instance.FocusedPenIndex) > 0))
        {
            SetStatus("ROUND  .  SUPERVISING LOCAL PEN ROBOT");
            yield return new WaitForSecondsRealtime(0.25f);
        }
        else
        {
            collection.SelectHandTool();
            yield return UseHand();
        }
    }

    private IEnumerator LoadCrosshatcherChicken(
        EggCarryController collection,
        CrosshatcherController crosshatcher,
        ChickenController chicken,
        ChickenPickupTarget pickupTarget)
    {
        if (collection == null
            || crosshatcher == null
            || chicken == null
            || pickupTarget == null)
        {
            yield break;
        }

        int occupiedBefore = crosshatcher.OccupiedSlots;
        SetStatus(
            $"CROSSHATCHER {occupiedBefore}/2  .  LOADING {chicken.Breed.ToString().ToUpperInvariant()}");
        yield return MovePointerToChickenPickup(chicken, pickupTarget);

        if (chicken == null
            || pickupTarget == null
            || !pickupTarget.CanPickUp)
        {
            yield return new WaitForSecondsRealtime(0.06f);
            yield break;
        }

        // An exact automation pickup keeps nearby eggs from stealing the hand
        // click in a crowded, capped pen. The pointer still shows the action.
        QueueMouseButton(MouseButton.Left, false);
        if (!collection.TryBeginCarryChicken(chicken))
        {
            SetStatus("CROSSHATCHER  .  CHICKEN BECAME UNAVAILABLE");
            yield return new WaitForSecondsRealtime(0.06f);
            yield break;
        }

        if (collection.HeldChicken != chicken
            || !TryGetWorldScreenPoint(
                crosshatcher,
                out Vector2 machinePoint))
        {
            QueueMouseButton(MouseButton.Left, false);
            yield return new WaitForSecondsRealtime(actionPause);
            yield break;
        }

        yield return MovePointer(machinePoint, MouseButton.Left);
        yield return new WaitForSecondsRealtime(pointerDwellTime);
        bool delivered = collection.TryDeliverHeldChickenToCrosshatcher(
            crosshatcher);
        QueueMouseButton(MouseButton.Left, false);

        if (!delivered)
        {
            SetStatus("CROSSHATCHER  .  DELIVERY SLOT BECAME UNAVAILABLE");
        }

        float acceptanceWait = 0f;
        while (crosshatcher != null
            && crosshatcher.OccupiedSlots <= occupiedBefore
            && acceptanceWait < 0.5f)
        {
            acceptanceWait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (crosshatcher != null
            && crosshatcher.OccupiedSlots > occupiedBefore)
        {
            completedActions++;
            collectionActionCount++;
        }

        yield return new WaitForSecondsRealtime(
            Mathf.Max(actionPause, 0.3f));
    }

    private IEnumerator UseHand()
    {
        bool seedIdleIncubator = ShouldManuallySeedFocusedIncubator();
        if (!TryFindClickableEgg(
                seedIdleIncubator,
                out ChickenEgg egg,
                out _))
        {
            SetStatus("ROUND  .  WAITING FOR EGGS");
            yield return new WaitForSecondsRealtime(0.18f);
            yield break;
        }

        bool incubate = ShouldUseIncubator(egg);
        Component destination = incubate
            ? FindIncubator()
            : EggContainer.Instance;
        EggCarryController collection = EggCarryController.Instance;

        if (destination == null
            || collection == null
            || !TryGetHandDropPoint(
                destination,
                collection.HandCarryHeight,
                out Vector2 destinationPoint))
        {
            yield return new WaitForSecondsRealtime(0.15f);
            yield break;
        }

        SetStatus($"HAND  .  {(incubate ? "TO INCUBATOR" : "TO CONTAINER")}");
        yield return MovePointerToEgg(egg);
        if (!IsPointerOverEgg(egg))
        {
            yield return new WaitForSecondsRealtime(0.06f);
            yield break;
        }

        QueueMouseButton(MouseButton.Left, true);
        float pickupWait = 0f;

        while (collection.HeldEgg != egg && pickupWait < 0.35f)
        {
            if (!TryGetEggScreenPoint(egg, out Vector2 liveEggPoint))
            {
                break;
            }

            MovePointerSpring(liveEggPoint, MouseButton.Left);
            pickupWait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (collection.HeldEgg != egg)
        {
            QueueMouseButton(MouseButton.Left, false);
            yield return new WaitForSecondsRealtime(actionPause);
            yield break;
        }

        yield return MovePointer(destinationPoint, MouseButton.Left);
        ForceMouseButton(MouseButton.Left, true);
        yield return null;
        QueueMouseButton(MouseButton.Left, false);
        collectionActionCount++;
        completedActions++;
        yield return new WaitForSecondsRealtime(actionPause);
    }

    private IEnumerator UseBasket(EggCarryController collection)
    {
        bool seedIdleIncubator = ShouldManuallySeedFocusedIncubator();
        ChickenEgg egg;
        bool hasCollectibleEgg = seedIdleIncubator
                && collection.BasketEggCount <= 0
            ? TryFindClickableEgg(true, out egg, out _)
            : TryFindBasketClusterEgg(out egg, out _);
        bool basketLoaded =
            collection.BasketEggCount >= collection.CurrentBasketCapacity
            || !hasCollectibleEgg;

        if (collection.BasketEggCount > 0
            && (basketLoaded || seedIdleIncubator)
            && (!collection.BasketContainsRareEggs
                || NeedsHigherTierChickens())
            && (seedIdleIncubator || !NeedsCashQuotaDelivery())
            && CanUseIncubator())
        {
            SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  .  INCUBATING ONE");
            IncubatorController incubator = FindIncubator();
            int transferCount = seedIdleIncubator
                ? 1
                : Mathf.Min(
                    collection.BasketEggCount,
                    incubator.AvailableCapacity);
            SetStatus(
                $"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}" +
                $"  .  INCUBATING {transferCount}");

            for (int index = 0; index < transferCount; index++)
            {
                int eggsBefore = collection.BasketEggCount;
                yield return ClickWorldComponent(incubator);
                if (collection.BasketEggCount >= eggsBefore)
                {
                    break;
                }

                collectionActionCount++;
            }
            yield break;
        }

        if (collection.BasketEggCount > 0 && basketLoaded)
        {
            SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  .  CASHING IN");
            yield return ClickWorldComponent(EggContainer.Instance);
            yield break;
        }

        if (!hasCollectibleEgg)
        {
            SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  .  WAITING");
            yield return new WaitForSecondsRealtime(0.18f);
            yield break;
        }

        SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  .  COLLECTING");
        yield return ClickMovingEgg(egg);
    }

    private IEnumerator UseVacuum(bool forceIncubatorSeed = false)
    {
        // Routine suction stays quota-first. Explicit population-recovery
        // calls may force exactly one low-value egg into an idle incubator so
        // a newly purchased pen cannot stall while its quota is outstanding.
        bool seedIdleIncubator = CanUseIncubator()
            && (forceIncubatorSeed
                || (!NeedsCashQuotaDelivery()
                    && ShouldManuallySeedFocusedIncubator()));
        ChickenEgg egg;
        Vector2 initialEggPoint;
        bool foundTarget = seedIdleIncubator
            ? TryFindClickableEgg(
                true,
                out egg,
                out initialEggPoint)
            : TryFindVacuumClusterTarget(
                out egg,
                out initialEggPoint);
        if (!foundTarget)
        {
            SetStatus("VACUUM  .  WAITING FOR EGGS");
            yield return new WaitForSecondsRealtime(0.08f);
            yield break;
        }

        bool incubate = CanUseIncubator()
            && (seedIdleIncubator || !NeedsCashQuotaDelivery());
        SetStatus($"VACUUM  .  {(incubate ? "RIGHT SUCK TO INCUBATOR" : "CASH SUCK")}");
        // Vacuuming only needs the egg inside its cone. Moving directly to the
        // validated screen point avoids spending up to 1.5 seconds waiting for
        // pixel-perfect hover on a rolling egg.
        yield return MovePointer(initialEggPoint);

        MouseButton button = incubate ? MouseButton.Right : MouseButton.Left;
        QueueMouseButton(button, true);
        float vacuumTime = 0f;
        float idleTime = 0f;
        ChickenEgg trackedEgg = egg;
        while (vacuumTime < vacuumHoldTime)
        {
            bool idleIncubatorSeedLaunched = seedIdleIncubator
                && EggCarryController.Instance != null
                && EggCarryController.Instance.HasPendingCollection;
            if (idleIncubatorSeedLaunched)
            {
                ForceMouseButton(button, false);
                QueueVacuumReturnToHighestOutputPen();
                SetStatus(
                    "VACUUM  .  INCUBATOR SEEDED  .  RETURNING TO PRODUCTION PEN");
                collectionActionCount++;
                completedActions++;
                yield return new WaitForSecondsRealtime(0.05f);
                yield break;
            }

            if (incubate && !CanContinueVacuumingToIncubator())
            {
                ForceMouseButton(button, false);
                incubate = false;
                button = MouseButton.Left;
                ForceMouseButton(button, true);
                SetStatus("VACUUM  .  INCUBATOR FULL  .  CASH SUCK");
            }

            if (!TryGetEggScreenPoint(trackedEgg, out Vector2 liveEggPoint))
            {
                trackedEgg = null;
                bool foundReplacement = seedIdleIncubator
                    ? TryFindClickableEgg(
                        true,
                        out trackedEgg,
                        out liveEggPoint)
                    : TryFindVacuumClusterTarget(
                        out trackedEgg,
                        out liveEggPoint);
                if (foundReplacement)
                {
                    idleTime = 0f;
                }
                else
                {
                    idleTime += Time.unscaledDeltaTime;
                }
            }

            if (trackedEgg != null)
            {
                MovePointerSpring(liveEggPoint, button);
            }
            else
            {
                ForceMouseButton(button, true);
            }

            vacuumTime += Time.unscaledDeltaTime;

            if (idleTime >= 0.14f)
            {
                break;
            }

            yield return null;
        }

        QueueMouseButton(button, false);
        collectionActionCount++;
        completedActions++;
        yield return new WaitForSecondsRealtime(0.05f);
    }

    private IEnumerator TryPlaceFood()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        int penIndex = manager != null ? manager.FocusedPenIndex : 0;
        if (!foodPlacementAttemptsByPen.TryGetValue(
                penIndex,
                out int placementAttempt))
        {
            placementAttempt = CountAvailableFoodPiles(penIndex);
        }

        if (!TryGetSpacedFoodPlacementScreenPoint(
                penIndex,
                placementAttempt,
                out Vector2 screenPoint))
        {
            foodPlacementSpacingBlockedPens.Add(penIndex);
            FoodShopController.Instance?.CancelActivePlacement();
            SetStatus("FEED  .  EXISTING PILES ALREADY COVER PEN");
            yield return new WaitForSecondsRealtime(0.05f);
            yield break;
        }

        foodPlacementAttemptsByPen[penIndex] = placementAttempt + 1;
        SetStatus($"FEED  .  PLACING IN PEN ({placementAttempt + 1})");
        yield return ClickScreen(screenPoint);
    }

    private bool TryGetSpacedFoodPlacementScreenPoint(
        int penIndex,
        int placementAttempt,
        out Vector2 screenPoint)
    {
        screenPoint = default;
        Camera camera = GetGameplayCamera();
        PenExpansionManager manager = PenExpansionManager.Instance;
        if (camera == null)
        {
            return false;
        }

        Vector2 placementAnchor = GetFoodPlacementAnchorViewport(penIndex);
        float minimumSpacing = GetMinimumFoodSearchRadius(
            manager,
            penIndex);
        bool hasExistingPile = HasAvailableFoodPile(penIndex);
        bool foundCandidate = false;
        float bestNearestDistance = float.NegativeInfinity;

        for (int offset = 0;
             offset < FoodPlacementViewportOffsets.Length;
             offset++)
        {
            int offsetIndex = (placementAttempt + offset)
                % FoodPlacementViewportOffsets.Length;
            Vector2 viewport = placementAnchor
                + FoodPlacementViewportOffsets[offsetIndex];
            viewport.x = Mathf.Clamp(viewport.x, 0.25f, 0.76f);
            viewport.y = Mathf.Clamp(viewport.y, 0.31f, 0.73f);
            Ray ray = camera.ViewportPointToRay(
                new Vector3(viewport.x, viewport.y, 0f));
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float distance))
            {
                continue;
            }

            Vector3 candidate = ray.GetPoint(distance);
            if (manager != null
                && manager.IsInitialized
                && manager.GetClosestPenIndex(candidate) != penIndex)
            {
                continue;
            }

            float nearestDistance = GetNearestFoodPileDistance(
                manager,
                penIndex,
                candidate);
            if (!hasExistingPile || nearestDistance > bestNearestDistance)
            {
                foundCandidate = true;
                bestNearestDistance = nearestDistance;
                screenPoint = new Vector2(
                    viewport.x * Screen.width,
                    viewport.y * Screen.height);
            }
        }

        return foundCandidate
            && (!hasExistingPile
                || bestNearestDistance >= minimumSpacing);
    }

    private Vector2 GetFoodPlacementAnchorViewport(int penIndex)
    {
        Vector2 penCenter = new Vector2(0.5f, 0.52f);
        Camera camera = GetGameplayCamera();
        PenExpansionManager manager = PenExpansionManager.Instance;
        if (camera == null || manager == null || !manager.IsInitialized)
        {
            return penCenter;
        }

        Vector3 chickenCentroid = Vector3.zero;
        int chickenCount = 0;
        var chickens = ChickenController.ActiveInstances;
        for (int index = 0; index < chickens.Count; index++)
        {
            ChickenController chicken = chickens[index];
            if (chicken == null
                || manager.GetClosestPenIndex(chicken.transform.position)
                    != penIndex)
            {
                continue;
            }

            chickenCentroid += chicken.transform.position;
            chickenCount++;
        }

        if (chickenCount <= 0)
        {
            return penCenter;
        }

        Vector3 projected = camera.WorldToViewportPoint(
            chickenCentroid / chickenCount);
        if (projected.z <= 0f)
        {
            return penCenter;
        }

        Vector2 flockCenter = new Vector2(projected.x, projected.y);
        // Keep enough central bias that the wide coverage offsets do not bunch
        // up against an edge when the flock temporarily wanders to one side.
        return Vector2.Lerp(penCenter, flockCenter, 0.4f);
    }

    private IEnumerator HandleResults()
    {
        Button milestoneButton = FindNamedButton(
            "Additional Pens Unlocked");
        if (IsUsable(milestoneButton))
        {
            yield return ClickButton(
                milestoneButton,
                "MILESTONE  .  ADDITIONAL PENS UNLOCKED");
            yield break;
        }

        Button shopButton = FindNamedButton("Open Supplies Shop");

        if (IsUsable(shopButton))
        {
            bool passed = RoundSystem.Instance != null
                && RoundSystem.Instance.DidPassRound;
            yield return ClickButton(
                shopButton,
                passed
                    ? "RESULTS  .  PASSED  .  OPENING SHOP"
                    : "RESULTS  .  FAILED  .  PREPARING RETRY");
            yield break;
        }

        if (!resultsSkipSent)
        {
            resultsSkipSent = true;
            SetStatus("RESULTS  .  SKIPPING COUNT-UP");
            yield return ClickScreen(new Vector2(Screen.width * 0.12f, Screen.height * 0.18f));
            yield break;
        }

        SetStatus("RESULTS  .  WAITING FOR BUTTONS");
        yield return new WaitForSecondsRealtime(0.12f);
    }

    private IEnumerator HandleShop()
    {
        ProgressionTreePreview preview =
            Object.FindFirstObjectByType<ProgressionTreePreview>();
        if (preview != null && preview.IsOpen)
        {
            SetStatus("SHOP  .  CLOSING DETAILS");
            preview.Hide();
            yield return new WaitForSecondsRealtime(actionPause);
            yield break;
        }

        FoodShopController foodShop = FoodShopController.Instance;
        PenExpansionManager penManager = PenExpansionManager.Instance;
        int nextPenIndex = penManager != null
            ? penManager.NextUnownedPenIndex
            : -1;
        bool hasPendingPenInvestment = TryGetNextOwnedPenInvestment(
            penManager,
            out int investmentPenIndex,
            out string investmentLabel,
            out int investmentCost);
        bool affordableExpansion = TryGetAffordablePenPurchase(
            penManager,
            out nextPenIndex);
        bool failureExpansionAvailable = consecutiveFailedRounds > 0
            && recoveryExpansionFailureCount != consecutiveFailedRounds
            && affordableExpansion
            && AreOwnedPensReadyForExpansion(penManager, true);
        bool canFundExpansionAndInvestment = affordableExpansion
            && CanFundPenAndPendingInvestment(
                penManager,
                nextPenIndex,
                hasPendingPenInvestment,
                investmentCost);
        bool wantsAnotherPen = penManager != null
            && penManager.OwnedPenCount < automatedOwnedPenTarget
            && nextPenIndex >= 0
            && (failureExpansionAvailable
                || (AreOwnedPensReadyForExpansion(penManager, false)
                    && (!hasPendingPenInvestment
                        || canFundExpansionAndInvestment)));
        ProgressionNodeButton[] nodes =
            Object.FindObjectsByType<ProgressionNodeButton>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        int availableFoodPiles = CountAvailableManualFeedCoverage(penManager);
        int totalFoodSupply = availableFoodPiles
            + (foodShop != null ? foodShop.OwnedFoodCount : 0);
        RefreshFundedCollectionRecoveryPlans(
            penManager,
            nodes,
            foodShop,
            totalFoodSupply);
        int desiredFeedInventory = GetRequiredFeedInventory(penManager);
        bool hasAnyAutoFeeder = HasAnyOwnedAutoFeeder(penManager);
        bool affordablePenInvestment = hasPendingPenInvestment
            && EggScoreHud.CurrentCents >= investmentCost;

        if (foodShop != null
            && totalFoodSupply <= 0
            && desiredFeedInventory > 0)
        {
            Button essentialFeed = FindNamedButton("Buy Feed");
            if (CanPurchaseProgressionNode(essentialFeed))
            {
                yield return ClickButton(
                    essentialFeed,
                    "SHOP - SELECTING ESSENTIAL FEED");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (IsUsable(previewBuy))
                {
                    shopPurchaseCount++;
                    yield return ClickButton(
                        previewBuy,
                        "SHOP - BUYING ESSENTIAL FEED");
                    yield break;
                }
            }
        }

        EggCarryController collection = EggCarryController.Instance;
        bool prioritizeRetryFeed = consecutiveFailedRounds > 0;
        bool prioritizeVacuumFeed = collection != null
            && collection.HasVacuum;
        if ((prioritizeRetryFeed || prioritizeVacuumFeed)
            && foodShop != null
            && desiredFeedInventory > 0
            && foodShop.OwnedFoodCount < desiredFeedInventory
            && CanBuyFeedWithoutUsingRecoveryReserve(foodShop)
            && shopPurchaseCount < GetShopPurchaseLimit())
        {
            Button coverageFeed = FindNamedButton("Buy Feed");
            if (CanPurchaseProgressionNode(coverageFeed))
            {
                yield return ClickButton(
                    coverageFeed,
                    $"SHOP  .  {(prioritizeRetryFeed ? "RETRY" : "VACUUM")} FEED "
                    + $"{foodShop.OwnedFoodCount + 1}/{desiredFeedInventory}");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (IsUsable(previewBuy))
                {
                    shopPurchaseCount++;
                    yield return ClickButton(
                        previewBuy,
                        prioritizeRetryFeed
                            ? "SHOP  .  BUYING RETRY FEED"
                            : "SHOP  .  BUYING VACUUM FEED COVERAGE");
                    yield break;
                }
            }
        }

        bool needsStarterBasket = collection != null
            && collection.BasketUpgradeLevel <= 0
            && (ChickenController.ActiveInstances.Count
                    >= BasketRequiredChickenCount
                || lastFailureWasCollectionLimited
                || (RoundSystem.Instance != null
                    && RoundSystem.Instance.RoundNumber >= 1));
        if (needsStarterBasket)
        {
            ProgressionNodeButton starterBasket =
                FindVacuumPriorityNode(nodes);
            if (starterBasket != null)
            {
                yield return EnsureShopTabVisible(starterBasket);
                Button basketButton = starterBasket.GetComponent<Button>();
                if (IsUsable(basketButton)
                    && CanPurchaseWithoutUsingRecoveryReserve(
                        starterBasket))
                {
                    yield return ClickButton(
                        basketButton,
                        "SHOP  .  FLOCK GROWTH  .  PRIORITISING STARTER BASKET");
                    Button previewBuy = FindNamedButton("Preview Buy");
                    if (IsUsable(previewBuy))
                    {
                        shopPurchaseCount++;
                        yield return ClickButton(
                            previewBuy,
                            "SHOP  .  BUYING STARTER BASKET");
                        collectionRecoveryPurchasedThisShop = true;
                        yield break;
                    }
                }

                if (consecutiveFailedRounds <= 0)
                {
                    ProgressionSystem.NodeState basketState =
                        starterBasket.GetNodeState();
                    SetStatus(
                        "SHOP  .  SAVING FOR STARTER BASKET  "
                        + $"{EggScoreHud.CurrentCents}/{basketState.Cost}");
                    yield return ClickNamedButton(
                        "Done Shopping",
                        "SHOP  .  SAVING FOR REQUIRED BASKET");
                    yield break;
                }
            }
        }

        // A failed round switches the shop into recovery mode. Spend what is
        // available on the upgrade most relevant to the observed bottleneck;
        // never hold the same balance for an unaffordable target and retry the
        // round unchanged.
        if (consecutiveFailedRounds > 0)
        {
            if (shopPurchaseCount >= MaximumRecoveryShopPurchases)
            {
                yield return ClickNamedButton(
                    "Done Shopping",
                    "SHOP  .  RECOVERY PURCHASES COMPLETE");
                yield break;
            }

            // Buy a useful global package, then actually execute an affordable
            // pen-development or expansion decision. Without this handoff the
            // recovery loop could consume the entire balance on tree nodes and
            // never reach the local production bottleneck it had identified.
            if (shopPurchaseCount >= 4 && failureExpansionAvailable)
            {
                yield return ClickNamedButton(
                    "Done Shopping",
                    $"SHOP  .  RECOVERY PACKAGE COMPLETE  .  BUYING PEN {nextPenIndex + 1}");
                yield break;
            }

            if (shopPurchaseCount >= 4 && affordablePenInvestment)
            {
                yield return ClickNamedButton(
                    "Done Shopping",
                    $"SHOP  .  RECOVERY PACKAGE COMPLETE  .  BUYING PEN {investmentPenIndex + 1} {investmentLabel}");
                yield break;
            }

            ProgressionNodeButton recoveryUpgrade =
                FindFailureRecoveryUpgrade(
                    nodes,
                    0);
            if (recoveryUpgrade != null)
            {
                yield return EnsureShopTabVisible(recoveryUpgrade);
                Button recoveryButton = recoveryUpgrade.GetComponent<Button>();
                if (IsUsable(recoveryButton)
                    && CanPurchaseWithoutUsingRecoveryReserve(
                        recoveryUpgrade))
                {
                    string bottleneck = lastFailureWasCollectionLimited
                        ? "COLLECTION"
                        : "EARNINGS";
                    yield return ClickButton(
                        recoveryButton,
                        $"SHOP  .  {bottleneck} RECOVERY  .  {recoveryButton.name.ToUpperInvariant()}");
                    Button previewBuy = FindNamedButton("Preview Buy");
                    if (IsUsable(previewBuy))
                    {
                        shopPurchaseCount++;
                        yield return ClickButton(
                            previewBuy,
                            $"SHOP  .  BUYING {recoveryButton.name.ToUpperInvariant()}");
                        if (IsCollectionProgressionUpgrade(
                                recoveryUpgrade.UpgradeId))
                        {
                            collectionRecoveryPurchasedThisShop = true;
                        }
                        yield break;
                    }
                }
            }

            // Tree upgrades get first claim on recovery cash. Add one pen next
            // when affordable, then PlayRound spends through local equipment
            // and upgrades across the entire enlarged farm.
            if (failureExpansionAvailable)
            {
                yield return ClickNamedButton(
                    "Done Shopping",
                    $"SHOP  .  RECOVERY  .  BUYING PEN {nextPenIndex + 1} NEXT");
                yield break;
            }

            if (affordablePenInvestment)
            {
                yield return ClickNamedButton(
                    "Done Shopping",
                    $"SHOP  .  RECOVERY  .  BUYING PEN {investmentPenIndex + 1} {investmentLabel} NEXT");
                yield break;
            }

            if (foodShop != null
                && desiredFeedInventory > 0
                && foodShop.OwnedFoodCount < desiredFeedInventory
                && CanBuyFeedWithoutUsingRecoveryReserve(foodShop))
            {
                Button recoveryFeed = FindNamedButton("Buy Feed");
                if (CanPurchaseProgressionNode(recoveryFeed))
                {
                    yield return ClickButton(
                        recoveryFeed,
                        "SHOP  .  RECOVERY  .  SELECTING FEED");
                    Button previewBuy = FindNamedButton("Preview Buy");
                    if (IsUsable(previewBuy))
                    {
                        shopPurchaseCount++;
                        yield return ClickButton(
                            previewBuy,
                            "SHOP  .  RECOVERY  .  BUYING FEED");
                        yield break;
                    }
                }
            }

            yield return ClickNamedButton(
                "Done Shopping",
                "SHOP  .  RECOVERY  .  NO AFFORDABLE PURCHASES");
            yield break;
        }

        int desiredFeedTier = GetDesiredFeedTier(RoundSystem.Instance);
        ProgressionNodeButton feedSpeedPriorityNode =
            foodShop != null
            && foodShop.UnlockedFeedTier < desiredFeedTier
                ? FindAffordableProgressionNode(
                    nodes,
                    ProgressionSystem.UpgradeId.FeedSpeed)
                : null;
        if ((hasAnyAutoFeeder || totalFoodSupply > 0)
            && feedSpeedPriorityNode != null
            && shopPurchaseCount < GetShopPurchaseLimit())
        {
            yield return EnsureShopTabVisible(feedSpeedPriorityNode);
            Button feedSpeedUpgrade =
                feedSpeedPriorityNode.GetComponent<Button>();
            if (IsUsable(feedSpeedUpgrade)
                && CanPurchaseWithoutUsingRecoveryReserve(
                    feedSpeedPriorityNode))
            {
                yield return ClickButton(
                    feedSpeedUpgrade,
                    $"SHOP  .  PRIORITISING FEED POWER {foodShop.UnlockedFeedTier + 1}/{desiredFeedTier}");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (IsUsable(previewBuy))
                {
                    shopPurchaseCount++;
                    yield return ClickButton(
                        previewBuy,
                        $"SHOP  .  UPGRADING FEED POWER {foodShop.UnlockedFeedTier + 1}/{desiredFeedTier}");
                    yield break;
                }
            }
        }

        // Reliable production and sale multipliers stay ahead of optional
        // collection depth. Previously the basket/vacuum branches below could
        // leave the shop early and starve feed, weight, and value upgrades.
        ProgressionNodeButton economyPriority =
            FindEggEconomyPriorityNode(nodes);
        if (economyPriority != null
            && shopPurchaseCount < GetShopPurchaseLimit())
        {
            yield return EnsureShopTabVisible(economyPriority);
            Button economyButton = economyPriority.GetComponent<Button>();
            if (IsUsable(economyButton)
                && CanPurchaseWithoutUsingRecoveryReserve(
                    economyPriority))
            {
                yield return ClickButton(
                    economyButton,
                    $"SHOP  .  PRIORITISING {economyButton.name.ToUpperInvariant()}");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (IsUsable(previewBuy))
                {
                    shopPurchaseCount++;
                    yield return ClickButton(
                        previewBuy,
                        $"SHOP  .  BUYING {economyButton.name.ToUpperInvariant()}");
                    yield break;
                }
            }

            ProgressionSystem.NodeState economyState =
                economyPriority.GetNodeState();
            bool shouldSaveForEconomy = RoundSystem.Instance != null
                && RoundSystem.Instance.RoundNumber >= 4
                && !IsStrategyUnderPressure();
            if (shouldSaveForEconomy
                && economyState.Visible
                && economyState.PrerequisiteMet
                && !economyState.IsMaxed
                && economyState.Cost > EggScoreHud.CurrentCents)
            {
                SetStatus(
                    $"SHOP  .  SAVING FOR {economyState.Title.ToUpperInvariant()}  "
                    + $"{EggScoreHud.CurrentCents}/{economyState.Cost}");
                yield return ClickNamedButton(
                    "Done Shopping",
                    $"SHOP  .  SAVING FOR {economyState.Title.ToUpperInvariant()}");
                yield break;
            }
        }

        ProgressionNodeButton collectionPriority =
            FindAdaptiveCollectionPriorityNode(nodes);
        if (collectionPriority != null
            && shopPurchaseCount < GetShopPurchaseLimit())
        {
            yield return EnsureShopTabVisible(collectionPriority);
            Button collectionButton =
                collectionPriority.GetComponent<Button>();
            if (IsUsable(collectionButton)
                && CanPurchaseWithoutUsingRecoveryReserve(
                    collectionPriority))
            {
                string upgradeName = collectionPriority.GetNodeState().Title;
                yield return ClickButton(
                    collectionButton,
                    $"SHOP  .  ADAPTIVE COLLECTION  .  {upgradeName.ToUpperInvariant()}");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (IsUsable(previewBuy))
                {
                    shopPurchaseCount++;
                    yield return ClickButton(
                        previewBuy,
                        $"SHOP  .  BUYING {upgradeName.ToUpperInvariant()}");
                    collectionRecoveryPurchasedThisShop = true;
                    yield break;
                }
            }
        }

        // Buy only a compact spread of the strongest available feed. This
        // preserves round time for vacuuming instead of spending it placing a
        // large number of weak individual piles.
        if (foodShop != null
            && desiredFeedInventory > 0
            && foodShop.OwnedFoodCount < desiredFeedInventory
            && CanBuyFeedWithoutUsingRecoveryReserve(foodShop))
        {
            Button buyFeed = FindNamedButton("Buy Feed");
            if (CanPurchaseProgressionNode(buyFeed))
            {
                yield return ClickButton(
                    buyFeed,
                    $"SHOP  .  RESTOCKING FEED {foodShop.OwnedFoodCount + 1}/{desiredFeedInventory}");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (IsUsable(previewBuy))
                {
                    shopPurchaseCount++;
                    yield return ClickButton(
                        previewBuy,
                        $"SHOP  .  BUYING FEED BAG {foodShop.OwnedFoodCount + 1}/{desiredFeedInventory}");
                    yield break;
                }
            }
        }

        int desiredTurboReserve = consecutiveFailedRounds > 0 ? 2 : 1;
        if (shopPurchaseCount < GetShopPurchaseLimit())
        {
            ProgressionSystem.UpgradeId[] turboRoots =
            {
                ProgressionSystem.UpgradeId.IncubatorTurbo,
                ProgressionSystem.UpgradeId.CrosshatcherTurbo,
                ProgressionSystem.UpgradeId.RobotTurbo
            };
            for (int index = 0; index < turboRoots.Length; index++)
            {
                TurboConsumableSystem.TurboType type =
                    (TurboConsumableSystem.TurboType)index;
                if (TurboConsumableSystem.GetInventory(type)
                        >= desiredTurboReserve
                    || !TurboConsumableSystem.HasApplicableMachine(type)
                    || wantsAnotherPen)
                {
                    continue;
                }

                ProgressionNodeButton turboNode =
                    FindAffordableProgressionNode(nodes, turboRoots[index]);
                if (turboNode == null
                    || !CanPurchaseWithoutUsingRecoveryReserve(turboNode)
                    || !CanSpendPremiumTurboBudget(turboNode))
                {
                    continue;
                }

                yield return ClickButton(
                    turboNode.GetComponent<Button>(),
                    $"SHOP  .  SELECTING {TurboConsumableSystem.GetDisplayName(type).ToUpperInvariant()} TURBO");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (IsUsable(previewBuy))
                {
                    shopPurchaseCount++;
                    yield return ClickButton(
                        previewBuy,
                        $"SHOP  .  BUYING {TurboConsumableSystem.GetDisplayName(type).ToUpperInvariant()} TURBO");
                    yield break;
                }
            }
        }

        if (wantsAnotherPen)
        {
            int cost = penManager.GetPenCostCents(nextPenIndex);
            SetStatus(
                $"SHOP  .  SAVING FOR PEN {nextPenIndex + 1}  "
                + $"{EggScoreHud.CurrentCents}/{cost}");
            yield return ClickNamedButton(
                "Done Shopping",
                $"SHOP  .  SAVING FOR PEN {nextPenIndex + 1}");
            yield break;
        }

        if (affordablePenInvestment)
        {
            yield return ClickNamedButton(
                "Done Shopping",
                $"SHOP  .  BUYING PEN {investmentPenIndex + 1} {investmentLabel} NEXT");
            yield break;
        }

        if (shopPurchaseCount < GetShopPurchaseLimit())
        {
            for (int offset = 0; offset < nodes.Length; offset++)
            {
                int index = (shopUpgradeCursor + offset) % nodes.Length;
                ProgressionNodeButton node = nodes[index];
                if (node.UpgradeId == ProgressionSystem.UpgradeId.FoodBag
                    || IsTurboConsumableRoot(node.UpgradeId))
                {
                    continue;
                }

                if (IsLocalEquipmentProgressionUpgrade(node.UpgradeId))
                {
                    continue;
                }

                if (IsCollectionProgressionUpgrade(node.UpgradeId))
                {
                    continue;
                }

                if (IsChickenCapReached()
                    && IsIncubatorUpgrade(node.UpgradeId))
                {
                    continue;
                }

                if (EggCarryController.Instance != null
                    && EggCarryController.Instance.HasVacuum
                    && node.UpgradeId == ProgressionSystem.UpgradeId.BasketCapacity)
                {
                    continue;
                }

                Button upgrade = node.GetComponent<Button>();
                if (!CanPurchaseWithoutUsingRecoveryReserve(node))
                {
                    continue;
                }

                yield return EnsureShopTabVisible(node);
                if (!CanPurchaseProgressionNode(upgrade))
                {
                    continue;
                }

                yield return ClickButton(
                    upgrade,
                    $"SHOP  .  SELECTING {upgrade.name.ToUpperInvariant()}");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (!IsUsable(previewBuy))
                {
                    continue;
                }

                shopUpgradeCursor = (index + 1) % nodes.Length;
                shopPurchaseCount++;
                yield return ClickButton(
                    previewBuy,
                    $"SHOP  .  BUYING {upgrade.name.ToUpperInvariant()}");
                yield break;
            }
        }

        yield return ClickNamedButton("Done Shopping", "SHOP  .  DONE");
    }

    private bool ShouldUseIncubator(ChickenEgg egg)
    {
        return egg != null
            && CanUseIncubator()
            && (ShouldManuallySeedFocusedIncubator()
                || (!NeedsCashQuotaDelivery()
                    && (egg.Type == ChickenEgg.EggType.Common
                        || NeedsHigherTierChickens())));
    }

    private static bool NeedsCashQuotaDelivery()
    {
        RoundSystem round = RoundSystem.Instance;
        return round != null
            && round.IsRoundAcceptingEggs
            && !round.IsCashQuotaMet;
    }

    private static bool CanSpendTimeOnManualCrosshatching()
    {
        RoundSystem round = RoundSystem.Instance;
        return round != null
            && round.IsRoundAcceptingEggs
            && round.IsCashQuotaMet
            && round.TimeRemaining >= MinimumManualCrosshatchTimeRemaining;
    }

    private bool CanUseIncubator()
    {
        IncubatorController incubator = FindIncubator();
        return incubator != null
            && incubator.isActiveAndEnabled
            && incubator.AvailableCapacity > 0;
    }

    private static bool IsFocusedIncubatorIdle()
    {
        IncubatorController incubator = FindIncubator();
        return incubator != null
            && incubator.isActiveAndEnabled
            && incubator.StoredEggs <= 0
            && incubator.AvailableCapacity > 0;
    }

    private static bool ShouldManuallySeedFocusedIncubator()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        return IsFocusedIncubatorIdle()
            && (manager == null
                || !manager.IsInitialized
                || !HasAutomaticIncubatorLoader(
                    manager,
                    manager.FocusedPenIndex));
    }

    private static bool HasAutomaticIncubatorLoader(
        PenExpansionManager manager,
        int penIndex)
    {
        return manager != null
            && manager.HasRobotInPen(penIndex)
            && manager.GetUpgradeLevel(
                penIndex,
                PenExpansionManager.EquipmentUpgrade.RobotSmartness)
                >= EggCollectorRobot.PopulationGrowthSmartnessLevel;
    }

    private static bool CanContinueVacuumingToIncubator()
    {
        EggCarryController collection = EggCarryController.Instance;
        return collection != null
            ? collection.HasVacuumIncubatorCapacity
            : CanUseFocusedIncubatorFallback();
    }

    private static bool CanUseFocusedIncubatorFallback()
    {
        IncubatorController incubator = FindIncubator();
        return incubator != null
            && incubator.isActiveAndEnabled
            && incubator.AvailableCapacity > 0;
    }

    private static ProgressionNodeButton FindCrosshatcherPriorityNode(
        ProgressionNodeButton[] nodes)
    {
        CrosshatcherShopController shop =
            CrosshatcherShopController.Instance;

        if (shop == null || nodes == null)
        {
            return null;
        }

        ProgressionSystem.UpgradeId priorityId;
        int targetLevel = 0;

        if (!shop.IsInstalled)
        {
            priorityId =
                ProgressionSystem.UpgradeId.CrosshatcherInstall;
        }
        else if (shop.SpeedLevel < CrosshatcherController.MaximumLevel
            && (shop.SpeedLevel <= shop.QualityLevel
                || shop.QualityLevel
                    >= CrosshatcherController.MaximumLevel))
        {
            priorityId =
                ProgressionSystem.UpgradeId.CrosshatcherSpeed;
            targetLevel = shop.SpeedLevel + 1;
        }
        else if (shop.QualityLevel
            < CrosshatcherController.MaximumLevel)
        {
            priorityId =
                ProgressionSystem.UpgradeId.CrosshatcherQuality;
            targetLevel = shop.QualityLevel + 1;
        }
        else
        {
            return null;
        }

        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];

            if (node != null
                && node.UpgradeId == priorityId
                && (targetLevel <= 0
                    || node.TargetLevel == targetLevel))
            {
                return node;
            }
        }

        return null;
    }

    private static ProgressionNodeButton FindVacuumPriorityNode(
        ProgressionNodeButton[] nodes)
    {
        EggCarryController collection = EggCarryController.Instance;
        if (collection == null || collection.HasVacuum || nodes == null)
        {
            return null;
        }

        ProgressionSystem.UpgradeId priorityId;
        if (collection.BasketUpgradeLevel
            < EggCarryController.MaximumBasketLevel)
        {
            priorityId = ProgressionSystem.UpgradeId.BasketCapacity;
        }
        else if (collection.BasketReachLevel
            < EggCarryController.MaximumBasketReachLevel)
        {
            priorityId = ProgressionSystem.UpgradeId.BasketReach;
        }
        else
        {
            priorityId = ProgressionSystem.UpgradeId.VacuumUnlock;
        }

        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node != null
                && node.UpgradeId == priorityId
                && (priorityId != ProgressionSystem.UpgradeId.BasketCapacity
                    || node.TargetLevel == collection.BasketUpgradeLevel + 1)
                && (priorityId != ProgressionSystem.UpgradeId.BasketReach
                    || node.TargetLevel == collection.BasketReachLevel + 1)
                && (priorityId != ProgressionSystem.UpgradeId.VacuumUnlock
                    || node.TargetLevel == 0))
            {
                return node;
            }
        }

        return null;
    }

    private ProgressionNodeButton FindAdaptiveCollectionPriorityNode(
        ProgressionNodeButton[] nodes)
    {
        EggCarryController collection = EggCarryController.Instance;
        if (collection == null || nodes == null)
        {
            return null;
        }

        int totalChickens = ChickenController.ActiveInstances.Count;
        bool collectionPressure = lastFailureWasCollectionLimited
            || lastCollectionRatio < HealthyCollectionRatio;
        int desiredBasketLevel = Mathf.Clamp(
            1 + Mathf.Max(0, totalChickens - BasketRequiredChickenCount) / 12
                + (collectionPressure ? 1 : 0),
            1,
            EggCarryController.MaximumBasketLevel);
        int desiredReachLevel = Mathf.Clamp(
            Mathf.Max(0, totalChickens - 14) / 12
                + (collectionPressure ? 1 : 0),
            0,
            EggCarryController.MaximumBasketReachLevel);

        ProgressionSystem.UpgradeId desiredId;
        int desiredTarget;
        if (!collection.HasVacuum
            && collection.BasketUpgradeLevel < desiredBasketLevel)
        {
            desiredId = ProgressionSystem.UpgradeId.BasketCapacity;
            desiredTarget = collection.BasketUpgradeLevel + 1;
        }
        else if (!collection.HasVacuum
            && collection.BasketReachLevel < desiredReachLevel)
        {
            desiredId = ProgressionSystem.UpgradeId.BasketReach;
            desiredTarget = collection.BasketReachLevel + 1;
        }
        else if (!collection.HasVacuum
            && collection.BasketUpgradeLevel
                >= EggCarryController.MaximumBasketLevel
            && collection.BasketReachLevel
                >= EggCarryController.MaximumBasketReachLevel
            && (collectionPressure
                || totalChickens >= 35
                || (PenExpansionManager.Instance != null
                    && PenExpansionManager.Instance.OwnedPenCount > 1)))
        {
            desiredId = ProgressionSystem.UpgradeId.VacuumUnlock;
            desiredTarget = 0;
        }
        else if (collection.HasVacuum)
        {
            int roundNumber = RoundSystem.Instance != null
                ? RoundSystem.Instance.RoundNumber
                : 0;
            int desiredVacuumPower = Mathf.Clamp(
                1 + roundNumber / 10 + (collectionPressure ? 1 : 0),
                1,
                3);
            int desiredVacuumRange = Mathf.Clamp(
                1 + roundNumber / 8 + (collectionPressure ? 1 : 0),
                1,
                3);
            if (collection.VacuumRangeLevel < desiredVacuumRange)
            {
                desiredId = ProgressionSystem.UpgradeId.VacuumRange;
                desiredTarget = collection.VacuumRangeLevel + 1;
            }
            else if (collection.VacuumPowerLevel < desiredVacuumPower)
            {
                desiredId = ProgressionSystem.UpgradeId.VacuumPower;
                desiredTarget = collection.VacuumPowerLevel + 1;
            }
            else
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node != null
                && node.UpgradeId == desiredId
                && node.TargetLevel == desiredTarget)
            {
                return node;
            }
        }

        return null;
    }

    private void RefreshFundedCollectionRecoveryPlans(
        PenExpansionManager manager,
        ProgressionNodeButton[] nodes,
        FoodShopController foodShop,
        int totalFoodSupply)
    {
        collectionRecoveryPlannedThisShop = false;
        plannedCollectionRecoveryReserveCents = 0;
        plannedRobotRecoveryPens.Clear();

        if (manager == null || !manager.IsInitialized)
        {
            return;
        }

        bool hasBackloggedPen = false;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (manager.IsPenOwned(index)
                && HasLargeRoundEndCollectionBacklog(index))
            {
                hasBackloggedPen = true;
                break;
            }
        }

        if (!hasBackloggedPen)
        {
            return;
        }

        ProgressionNodeButton collectionNode =
            FindAdaptiveCollectionPriorityNode(nodes);
        if (!collectionRecoveryPurchasedThisShop
            && CanPurchaseProgressionNode(collectionNode))
        {
            ProgressionSystem.NodeState state = collectionNode.GetNodeState();
            collectionRecoveryPlannedThisShop = true;
            plannedCollectionRecoveryReserveCents = state.Cost;
        }

        long spendableCents = EggScoreHud.CurrentCents
            - plannedCollectionRecoveryReserveCents;
        if (totalFoodSupply <= 0 && foodShop != null)
        {
            spendableCents -= foodShop.CurrentFeedBagCost;
        }

        // Reserve only concrete, affordable robot work. PlayRound prioritises
        // these decisions so a merely hypothetical robot does not suppress
        // the feed reduction for its pen.
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index)
                || !HasLargeRoundEndCollectionBacklog(index)
                || !TryGetStrategicLocalInvestment(
                    manager,
                    index,
                    PenExpansionManager.EquipmentType.Robot,
                    false,
                    out LocalInvestmentDecision decision)
                || (decision.IsUpgrade
                    && decision.Upgrade
                        is not PenExpansionManager.EquipmentUpgrade.RobotCapacity
                        and not PenExpansionManager.EquipmentUpgrade.RobotSpeed
                        and not PenExpansionManager.EquipmentUpgrade.RobotVacuum)
                || decision.Cost <= 0
                || decision.Cost > spendableCents)
            {
                continue;
            }

            plannedRobotRecoveryPens.Add(index);
            plannedCollectionRecoveryReserveCents += decision.Cost;
            spendableCents -= decision.Cost;
        }
    }

    private bool CanBuyFeedWithoutUsingRecoveryReserve(
        FoodShopController foodShop)
    {
        return foodShop != null
            && EggScoreHud.CurrentCents - foodShop.CurrentFeedBagCost
                >= plannedCollectionRecoveryReserveCents;
    }

    private bool CanPurchaseWithoutUsingRecoveryReserve(
        ProgressionNodeButton node)
    {
        if (!CanPurchaseProgressionNode(node))
        {
            return false;
        }

        ProgressionSystem.NodeState state = node.GetNodeState();
        long reserveAfterPurchase = plannedCollectionRecoveryReserveCents;
        if (collectionRecoveryPlannedThisShop
            && IsCollectionProgressionUpgrade(node.UpgradeId))
        {
            reserveAfterPurchase = reserveAfterPurchase > state.Cost
                ? reserveAfterPurchase - state.Cost
                : 0;
        }

        return EggScoreHud.CurrentCents - state.Cost
            >= reserveAfterPurchase;
    }

    private bool CanSpendPremiumTurboBudget(ProgressionNodeButton node)
    {
        if (node == null)
        {
            return false;
        }

        ProgressionSystem.NodeState state = node.GetNodeState();
        long spendable = EggScoreHud.CurrentCents
            > plannedCollectionRecoveryReserveCents
                ? EggScoreHud.CurrentCents
                    - plannedCollectionRecoveryReserveCents
                : 0L;
        if (spendable <= 0L)
        {
            return false;
        }

        double maximumShare = consecutiveFailedRounds > 0 ? 0.35d : 0.12d;
        return state.Cost <= spendable * maximumShare;
    }

    private static ProgressionNodeButton FindAffordableProgressionNode(
        ProgressionNodeButton[] nodes,
        ProgressionSystem.UpgradeId upgradeId)
    {
        if (nodes == null)
        {
            return null;
        }

        ProgressionNodeButton bestNode = null;
        int bestTargetLevel = int.MaxValue;
        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node == null || node.UpgradeId != upgradeId)
            {
                continue;
            }

            if (!CanPurchaseProgressionNode(node))
            {
                continue;
            }

            int targetLevel = node.TargetLevel > 0
                ? node.TargetLevel
                : int.MaxValue - 1;
            if (targetLevel < bestTargetLevel)
            {
                bestNode = node;
                bestTargetLevel = targetLevel;
            }
        }

        return bestNode;
    }

    private ProgressionNodeButton FindFailureRecoveryUpgrade(
        ProgressionNodeButton[] nodes,
        int reservedCents)
    {
        if (nodes == null)
        {
            return null;
        }

        ProgressionNodeButton bestNode = null;
        int bestScore = int.MinValue;
        long bestCost = long.MaxValue;
        long spendableCents = EggScoreHud.CurrentCents - reservedCents;
        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (!CanPurchaseProgressionNode(node)
                || node.UpgradeId == ProgressionSystem.UpgradeId.FoodBag
                || IsTurboConsumableRoot(node.UpgradeId)
                || IsLocalEquipmentProgressionUpgrade(node.UpgradeId)
                || (IsChickenCapReached()
                    && IsIncubatorUpgrade(node.UpgradeId))
                || (EggCarryController.Instance != null
                    && EggCarryController.Instance.HasVacuum
                    && node.UpgradeId
                        == ProgressionSystem.UpgradeId.BasketCapacity))
            {
                continue;
            }

            ProgressionSystem.NodeState state = node.GetNodeState();
            if (state.Cost > spendableCents)
            {
                continue;
            }

            int score = GetFailureRecoveryScore(
                node.UpgradeId,
                lastFailureWasCollectionLimited);
            // Prefer a useful spread of early tiers over exhausting one very
            // deep branch while other affordable multipliers remain at zero.
            score -= Mathf.Max(0, node.TargetLevel - 1) * 75;
            if (score > bestScore
                || (score == bestScore && state.Cost < bestCost))
            {
                bestNode = node;
                bestScore = score;
                bestCost = state.Cost;
            }
        }

        return bestNode;
    }

    private static int GetFailureRecoveryScore(
        ProgressionSystem.UpgradeId upgradeId,
        bool collectionLimited)
    {
        if (collectionLimited)
        {
            return upgradeId switch
            {
                ProgressionSystem.UpgradeId.BasketCapacity => 1200,
                ProgressionSystem.UpgradeId.VacuumUnlock => 1150,
                ProgressionSystem.UpgradeId.BasketReach => 1100,
                ProgressionSystem.UpgradeId.VacuumPower => 1050,
                ProgressionSystem.UpgradeId.VacuumRange => 1000,
                ProgressionSystem.UpgradeId.RobotUnlock => 950,
                ProgressionSystem.UpgradeId.RobotCapacity => 900,
                ProgressionSystem.UpgradeId.RobotSpeed => 850,
                ProgressionSystem.UpgradeId.RobotSmartness => 825,
                ProgressionSystem.UpgradeId.EggValue => 800,
                ProgressionSystem.UpgradeId.TruckBonus => 775,
                ProgressionSystem.UpgradeId.ChickenPerks => 765,
                ProgressionSystem.UpgradeId.RareEggChance => 750,
                ProgressionSystem.UpgradeId.EggWeight => 725,
                ProgressionSystem.UpgradeId.FeedSpeed => 700,
                ProgressionSystem.UpgradeId.PrimeFeed => 675,
                ProgressionSystem.UpgradeId.CrosshatcherQuality => 600,
                ProgressionSystem.UpgradeId.CrosshatcherSpeed => 550,
                ProgressionSystem.UpgradeId.CrosshatcherInstall => 525,
                ProgressionSystem.UpgradeId.IncubatorSpeed => 450,
                ProgressionSystem.UpgradeId.IncubatorCapacity => 425,
                ProgressionSystem.UpgradeId.IncubatorInstall => 400,
                _ => 100
            };
        }

        return upgradeId switch
        {
            ProgressionSystem.UpgradeId.EggValue => 1200,
            ProgressionSystem.UpgradeId.FeedSpeed => 1175,
            ProgressionSystem.UpgradeId.EggWeight => 1125,
            ProgressionSystem.UpgradeId.TruckBonus => 1050,
            ProgressionSystem.UpgradeId.ChickenPerks => 950,
            ProgressionSystem.UpgradeId.RareEggChance => 850,
            ProgressionSystem.UpgradeId.PrimeFeed => 800,
            ProgressionSystem.UpgradeId.CrosshatcherQuality => 900,
            ProgressionSystem.UpgradeId.VacuumUnlock => 800,
            ProgressionSystem.UpgradeId.BasketCapacity => 775,
            ProgressionSystem.UpgradeId.BasketReach => 750,
            ProgressionSystem.UpgradeId.VacuumPower => 725,
            ProgressionSystem.UpgradeId.VacuumRange => 700,
            ProgressionSystem.UpgradeId.RobotUnlock => 675,
            ProgressionSystem.UpgradeId.RobotCapacity => 650,
            ProgressionSystem.UpgradeId.RobotSpeed => 625,
            ProgressionSystem.UpgradeId.RobotSmartness => 600,
            ProgressionSystem.UpgradeId.CrosshatcherSpeed => 550,
            ProgressionSystem.UpgradeId.CrosshatcherInstall => 525,
            ProgressionSystem.UpgradeId.IncubatorSpeed => 450,
            ProgressionSystem.UpgradeId.IncubatorCapacity => 425,
            ProgressionSystem.UpgradeId.IncubatorInstall => 400,
            _ => 100
        };
    }

    private static ProgressionNodeButton FindEggEconomyPriorityNode(
        ProgressionNodeButton[] nodes)
    {
        ProgressionSystem progression = ProgressionSystem.Instance;
        if (progression == null || nodes == null)
        {
            return null;
        }

        int roundNumber = RoundSystem.Instance != null
            ? RoundSystem.Instance.RoundNumber
            : 0;
        int desiredPremiumLevel = GetDesiredPremiumEggLevel(roundNumber);
        int desiredPrimeFeedLevel = GetDesiredEggValueLevel(roundNumber);
        int desiredWeightLevel = GetDesiredEggWeightLevel(roundNumber);
        int desiredValueLevel = GetDesiredExtendedEggValueLevel(roundNumber);
        int desiredTruckBonusLevel = GetDesiredTruckBonusLevel(roundNumber);
        int desiredChickenPerksLevel =
            GetDesiredChickenPerksLevel(roundNumber);
        int premiumLevel = progression.RareEggChanceLevel;
        int primeFeedLevel = FoodShopController.Instance != null
            ? FoodShopController.Instance.PrimeFeedLevel
            : 0;
        int weightLevel = progression.EggWeightLevel;
        int valueLevel = progression.EggValueLevel;
        int truckBonusLevel = progression.TruckBonusLevel;
        int chickenPerksLevel = progression.ChickenPerksLevel;

        ProgressionSystem.UpgradeId upgradeId;
        int targetLevel;
        if (premiumLevel < 2)
        {
            upgradeId = ProgressionSystem.UpgradeId.RareEggChance;
            targetLevel = premiumLevel + 1;
        }
        else if (weightLevel < 1)
        {
            // The first weight tier unlocks Egg Value, and weight now also
            // multiplies sale cash directly.
            upgradeId = ProgressionSystem.UpgradeId.EggWeight;
            targetLevel = 1;
        }
        else if (valueLevel < desiredValueLevel)
        {
            // This is the strongest reliable quota multiplier and should not
            // wait behind low-probability premium-egg improvements.
            upgradeId = ProgressionSystem.UpgradeId.EggValue;
            targetLevel = valueLevel + 1;
        }
        else if (truckBonusLevel < desiredTruckBonusLevel
            && valueLevel >= 2
            && truckBonusLevel + 2 < valueLevel)
        {
            upgradeId = ProgressionSystem.UpgradeId.TruckBonus;
            targetLevel = truckBonusLevel + 1;
        }
        else if (weightLevel < desiredWeightLevel)
        {
            upgradeId = ProgressionSystem.UpgradeId.EggWeight;
            targetLevel = weightLevel + 1;
        }
        else if (premiumLevel < desiredPremiumLevel)
        {
            upgradeId = ProgressionSystem.UpgradeId.RareEggChance;
            targetLevel = premiumLevel + 1;
        }
        else if (premiumLevel >= 8
            && chickenPerksLevel < desiredChickenPerksLevel)
        {
            upgradeId = ProgressionSystem.UpgradeId.ChickenPerks;
            targetLevel = chickenPerksLevel + 1;
        }
        else if (primeFeedLevel < desiredPrimeFeedLevel)
        {
            upgradeId = ProgressionSystem.UpgradeId.PrimeFeed;
            targetLevel = primeFeedLevel + 1;
        }
        else if (truckBonusLevel < desiredTruckBonusLevel
            && valueLevel >= 2)
        {
            upgradeId = ProgressionSystem.UpgradeId.TruckBonus;
            targetLevel = truckBonusLevel + 1;
        }
        else
        {
            return null;
        }

        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node != null
                && node.UpgradeId == upgradeId
                && node.TargetLevel == targetLevel)
            {
                return node;
            }
        }

        return null;
    }

    public static bool HasRecommendedPremiumEggProgression()
    {
        ProgressionSystem progression = ProgressionSystem.Instance;
        if (progression == null)
        {
            return false;
        }

        int roundNumber = RoundSystem.Instance != null
            ? RoundSystem.Instance.RoundNumber
            : 0;
        FoodShopController food = FoodShopController.Instance;
        return food != null
            && progression.RareEggChanceLevel
                >= GetDesiredPremiumEggLevel(roundNumber)
            && food.PrimeFeedLevel
                >= GetDesiredEggValueLevel(roundNumber)
            && progression.EggWeightLevel
                >= GetDesiredEggWeightLevel(roundNumber)
            && progression.EggValueLevel
                >= GetDesiredEggValueLevel(roundNumber)
            && progression.ChickenPerksLevel
                >= GetDesiredChickenPerksLevel(roundNumber);
    }

    private static int GetDesiredPremiumEggLevel(int roundNumber)
    {
        return Mathf.Clamp(2 + Mathf.Max(0, roundNumber) / 4, 2, 8);
    }

    private static int GetDesiredEggValueLevel(int roundNumber)
    {
        return Mathf.Clamp(1 + Mathf.Max(0, roundNumber) / 4, 1, 5);
    }

    private static int GetDesiredEggWeightLevel(int roundNumber)
    {
        return Mathf.Clamp(1 + Mathf.Max(0, roundNumber) / 6, 1, 6);
    }

    private static int GetDesiredExtendedEggValueLevel(int roundNumber)
    {
        return Mathf.Clamp(
            1 + Mathf.Max(0, roundNumber) / 3,
            1,
            ProgressionSystem.MaximumEggValueLevel);
    }

    private static int GetDesiredTruckBonusLevel(int roundNumber)
    {
        return Mathf.Clamp(
            (Mathf.Max(0, roundNumber) - 10) / 5,
            0,
            ProgressionSystem.MaximumTruckBonusLevel);
    }

    private static int GetDesiredChickenPerksLevel(int roundNumber)
    {
        return Mathf.Clamp(
            (Mathf.Max(0, roundNumber) - 18) / 6,
            0,
            ProgressionSystem.MaximumChickenPerksLevel);
    }

    private static IncubatorController FindIncubator()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        IncubatorController focused = manager != null
            ? manager.GetFocusedIncubator()
            : null;
        return focused != null
            ? focused
            : Object.FindFirstObjectByType<IncubatorController>();
    }

    private static CrosshatcherController FindCrosshatcher()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        CrosshatcherController focused = manager != null
            ? manager.GetFocusedCrosshatcher()
            : null;
        return focused != null
            ? focused
            : Object.FindFirstObjectByType<CrosshatcherController>();
    }

    private static bool IsChickenCapReached()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        return manager != null && manager.IsInitialized
            ? manager.GetChickenCount(manager.FocusedPenIndex)
                >= ChickenController.MaximumChickenCount
            : ChickenController.ActiveInstances.Count
                >= ChickenController.MaximumChickenCount;
    }

    private bool ShouldNavigateToAnotherPen(out int targetPenIndex)
    {
        targetPenIndex = -1;
        PenExpansionManager manager = PenExpansionManager.Instance;
        if (manager == null
            || !manager.IsInitialized
            || manager.OwnedPenCount <= 1)
        {
            return false;
        }

        EggCarryController collection = EggCarryController.Instance;
        if (collection != null
            && (collection.HeldEgg != null
                || collection.HeldChicken != null
                || collection.HasPendingCollection))
        {
            return false;
        }

        int currentPenIndex = manager.FocusedPenIndex;
        bool currentHasRobot = manager.HasRobotInPen(currentPenIndex);
        int currentEggCount = GetAvailableEggCount(
            manager,
            currentPenIndex);

        if (collection != null
            && collection.HasVacuum
            && vacuumReturnPenIndex >= 0)
        {
            if (!manager.IsPenOwned(vacuumReturnPenIndex)
                || manager.HasRobotInPen(vacuumReturnPenIndex))
            {
                vacuumReturnPenIndex = -1;
            }
            else if (vacuumReturnPenIndex != currentPenIndex)
            {
                targetPenIndex = vacuumReturnPenIndex;
                return true;
            }
            else
            {
                vacuumReturnPenIndex = -1;
                return false;
            }
        }

        if (TryGetNextOwnedPenInvestment(
                manager,
                out int investmentPenIndex,
                out _,
                out int investmentCost)
            && EggScoreHud.CurrentCents >= investmentCost)
        {
            if (investmentPenIndex == currentPenIndex)
            {
                return false;
            }

            targetPenIndex = investmentPenIndex;
            return true;
        }

        if (collection != null
            && collection.HasVacuum
            && FoodShopController.Instance != null
            && FoodShopController.Instance.OwnedFoodCount > 0
            && TryGetPenNeedingFeedCoverage(
                manager,
                out int feedCoveragePenIndex))
        {
            if (feedCoveragePenIndex == currentPenIndex)
            {
                return false;
            }

            targetPenIndex = feedCoveragePenIndex;
            return true;
        }

        // Keep every manually managed incubator producing. Smart 2+ robots own
        // this job in their pen; every other idle incubator gets one egg before
        // routine collection or crosshatching.
        if (TryGetIdleIncubatorPen(manager, out int incubatorPenIndex))
        {
            if (incubatorPenIndex == currentPenIndex)
            {
                return false;
            }

            targetPenIndex = incubatorPenIndex;
            return true;
        }

        // Service any available crosshatcher whenever its pen has a safe flock.
        // A smart robot reservation makes the machine unavailable, so manual
        // loading remains safe even in robot-equipped pens.
        if (CanSpendTimeOnManualCrosshatching()
            && TryGetManualCrosshatcherPen(
                manager,
                out int crosshatcherPenIndex))
        {
            if (crosshatcherPenIndex == currentPenIndex)
            {
                return false;
            }

            targetPenIndex = crosshatcherPenIndex;
            return true;
        }

        // A vacuum belongs with the largest flock, which is the pen with the
        // greatest ongoing eggs-per-minute output. Egg count only breaks ties.
        if (collection != null && collection.HasVacuum)
        {
            int highestOutputPen = GetHighestOutputPen(
                manager,
                currentPenIndex,
                currentEggCount,
                true);
            // Robots own egg collection in their pens. If every pen is
            // automated, stay put instead of chasing eggs that disappear
            // during the camera transition.
            if (highestOutputPen < 0)
            {
                return false;
            }

            if (highestOutputPen != currentPenIndex)
            {
                targetPenIndex = highestOutputPen;
                return true;
            }

            return false;
        }

        if (!NeedsCashQuotaDelivery()
            && TryGetRobotBacklogDevelopmentTarget(
                manager,
                out int automatedPenIndex,
                out int developmentPenIndex))
        {
            if (developmentPenIndex == currentPenIndex)
            {
                return false;
            }

            // Leave a backed-up robot immediately, then give the development
            // pen a useful work window before reconsidering the flock leader.
            if (currentPenIndex == automatedPenIndex
                || Time.unscaledTime >= nextPenNavigationTime)
            {
                targetPenIndex = developmentPenIndex;
                return true;
            }

            return false;
        }

        // While the cash quota is outstanding, non-vacuum manual collection
        // also belongs in the pen with the largest flock.
        if (NeedsCashQuotaDelivery())
        {
            int highestOutputPen = GetHighestOutputPen(
                manager,
                currentPenIndex,
                currentEggCount,
                true);

            if (highestOutputPen >= 0
                && highestOutputPen != currentPenIndex)
            {
                targetPenIndex = highestOutputPen;
                return true;
            }

            return false;
        }

        // A manually managed pen remains selected until all of its loose eggs
        // are dealt with. Robot pens may be left because their collector keeps
        // working while the bot is elsewhere.
        if ((!currentHasRobot && currentEggCount > 0)
            || (currentEggCount > 0 && IsFocusedIncubatorIdle()))
        {
            return false;
        }

        int bestChickenCount = -1;
        int bestEggCount = -1;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (index == currentPenIndex
                || !manager.IsPenOwned(index)
                || manager.HasRobotInPen(index))
            {
                continue;
            }

            int eggCount = GetAvailableEggCount(manager, index);
            if (eggCount <= 0)
            {
                continue;
            }

            int chickenCount = manager.GetChickenCount(index);
            if (chickenCount > bestChickenCount
                || (chickenCount == bestChickenCount
                    && eggCount > bestEggCount))
            {
                bestChickenCount = chickenCount;
                bestEggCount = eggCount;
                targetPenIndex = index;
            }
        }

        if (targetPenIndex >= 0)
        {
            return true;
        }

        // With no loose eggs elsewhere, wait in the most productive pen that
        // still needs manual collection. Throttle only this idle relocation;
        // actionable eggs should never wait on the navigation timer.
        if (Time.unscaledTime >= nextPenNavigationTime)
        {
            bestChickenCount = currentHasRobot
                ? -1
                : manager.GetChickenCount(currentPenIndex);
            for (int index = 0; index < manager.PenCount; index++)
            {
                if (index == currentPenIndex
                    || !manager.IsPenOwned(index)
                    || manager.HasRobotInPen(index))
                {
                    continue;
                }

                int chickenCount = manager.GetChickenCount(index);
                if (chickenCount > bestChickenCount)
                {
                    bestChickenCount = chickenCount;
                    targetPenIndex = index;
                }
            }
        }

        if (targetPenIndex < 0)
        {
            // Re-evaluate often enough to notice newly laid eggs without
            // bouncing through empty pens and sacrificing production time.
            nextPenNavigationTime = Time.unscaledTime + 0.5f;
            return false;
        }

        return true;
    }

    private static int GetHighestOutputPen(
        PenExpansionManager manager,
        int currentPenIndex,
        int currentEggCount,
        bool excludeRobotPens = false)
    {
        bool currentPenEligible = manager.IsPenOwned(currentPenIndex)
            && (!excludeRobotPens
                || !manager.HasRobotInPen(currentPenIndex));
        int highestOutputPen = currentPenEligible ? currentPenIndex : -1;
        int highestChickenCount = currentPenEligible
            ? manager.GetChickenCount(currentPenIndex)
            : -1;
        int highestEggCount = currentPenEligible ? currentEggCount : -1;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index)
                || (excludeRobotPens && manager.HasRobotInPen(index)))
            {
                continue;
            }

            int chickenCount = manager.GetChickenCount(index);
            int eggCount = GetAvailableEggCount(manager, index);
            if (chickenCount > highestChickenCount
                || (chickenCount == highestChickenCount
                    && eggCount > highestEggCount))
            {
                highestOutputPen = index;
                highestChickenCount = chickenCount;
                highestEggCount = eggCount;
            }
        }

        return highestOutputPen;
    }

    private void QueueVacuumReturnToHighestOutputPen()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        if (manager == null || !manager.IsInitialized)
        {
            vacuumReturnPenIndex = -1;
            return;
        }

        int currentPenIndex = manager.FocusedPenIndex;
        int highestOutputPen = GetHighestOutputPen(
            manager,
            currentPenIndex,
            GetAvailableEggCount(manager, currentPenIndex),
            true);
        vacuumReturnPenIndex = highestOutputPen != currentPenIndex
            && highestOutputPen >= 0
            && manager.GetChickenCount(highestOutputPen)
                > manager.GetChickenCount(currentPenIndex)
                    ? highestOutputPen
                    : -1;
    }

    private static bool TryGetIdleIncubatorPen(
        PenExpansionManager manager,
        out int targetPenIndex)
    {
        targetPenIndex = -1;
        int bestPopulationDeficit = -1;
        int bestEggCount = -1;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index)
                || HasAutomaticIncubatorLoader(manager, index))
            {
                continue;
            }

            IncubatorController incubator = manager.GetIncubator(index);
            int eggCount = GetAvailableEggCount(manager, index);
            int chickenCount = manager.GetChickenCount(index);
            if (incubator == null
                || !incubator.isActiveAndEnabled
                || incubator.StoredEggs > 0
                || incubator.AvailableCapacity <= 0
                || chickenCount >= ChickenController.MaximumChickenCount
                || eggCount <= 0)
            {
                continue;
            }

            int populationDeficit = Mathf.Max(
                0,
                ChickenController.MaximumChickenCount - chickenCount);
            if (populationDeficit > bestPopulationDeficit
                || (populationDeficit == bestPopulationDeficit
                    && eggCount > bestEggCount))
            {
                targetPenIndex = index;
                bestPopulationDeficit = populationDeficit;
                bestEggCount = eggCount;
            }
        }

        return targetPenIndex >= 0;
    }

    private static bool TryGetManualCrosshatcherPen(
        PenExpansionManager manager,
        out int targetPenIndex)
    {
        targetPenIndex = -1;
        int bestOccupiedSlots = -1;
        int bestChickenCount = -1;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index))
            {
                continue;
            }

            CrosshatcherController crosshatcher =
                manager.GetCrosshatcher(index);
            int chickenCount = manager.GetChickenCount(index);
            if (crosshatcher == null
                || !crosshatcher.isActiveAndEnabled
                || crosshatcher.IsProcessing
                || !crosshatcher.CanAcceptCarriedChicken
                || (crosshatcher.OccupiedSlots == 0
                    && chickenCount
                        < MinimumStrategicCrosshatchFlock)
                || (crosshatcher.OccupiedSlots > 0 && chickenCount < 1))
            {
                continue;
            }

            // Finish a partially loaded machine before starting another one.
            if (crosshatcher.OccupiedSlots > bestOccupiedSlots
                || (crosshatcher.OccupiedSlots == bestOccupiedSlots
                    && chickenCount > bestChickenCount))
            {
                targetPenIndex = index;
                bestOccupiedSlots = crosshatcher.OccupiedSlots;
                bestChickenCount = chickenCount;
            }
        }

        return targetPenIndex >= 0;
    }

    private static bool TryGetPartiallyLoadedCrosshatcherPen(
        PenExpansionManager manager,
        out int targetPenIndex)
    {
        targetPenIndex = -1;
        if (manager == null || !manager.IsInitialized)
        {
            return false;
        }

        int bestChickenCount = -1;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index))
            {
                continue;
            }

            CrosshatcherController crosshatcher =
                manager.GetCrosshatcher(index);
            int chickenCount = manager.GetChickenCount(index);
            if (crosshatcher == null
                || !crosshatcher.isActiveAndEnabled
                || crosshatcher.IsProcessing
                || crosshatcher.OccupiedSlots != 1
                || !crosshatcher.CanAcceptCarriedChicken
                || chickenCount <= 0)
            {
                continue;
            }

            if (chickenCount > bestChickenCount)
            {
                targetPenIndex = index;
                bestChickenCount = chickenCount;
            }
        }

        return targetPenIndex >= 0;
    }

    private bool IsStrategyUnderPressure()
    {
        return consecutiveFailedRounds > 0
            || lastQuotaRatio < ComfortableQuotaRatio;
    }

    private int GetShopPurchaseLimit()
    {
        return Mathf.Max(maximumShopPurchasesPerVisit, ProactiveShopPurchaseLimit);
    }

    private bool CanPurchaseMoreLocalTech()
    {
        int limit = consecutiveFailedRounds > 0
            ? RecoveryLocalPurchasesPerRound
            : NormalLocalPurchasesPerRound;
        return roundLocalPurchaseCount < limit;
    }

    private bool TryGetPendingRobotRecoveryInvestment(
        PenExpansionManager manager,
        out LocalInvestmentDecision decision)
    {
        decision = default;
        if (manager == null || !manager.IsInitialized)
        {
            return false;
        }

        int bestLooseEggCount = -1;
        foreach (int penIndex in pendingRobotRecoveryPens)
        {
            if (!TryGetStrategicLocalInvestment(
                    manager,
                    penIndex,
                    PenExpansionManager.EquipmentType.Robot,
                    true,
                    out LocalInvestmentDecision candidate))
            {
                continue;
            }

            int looseEggCount = lastLooseEggsByPen.TryGetValue(
                penIndex,
                out int recordedLooseEggs)
                    ? recordedLooseEggs
                    : 0;
            if (looseEggCount > bestLooseEggCount)
            {
                decision = candidate;
                bestLooseEggCount = looseEggCount;
            }
        }

        return bestLooseEggCount >= 0;
    }

    private IEnumerator HandleLocalTechDialog(
        PenEquipmentHudController equipmentHud)
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        if (!CanPurchaseMoreLocalTech()
            || manager == null
            || !automationDialogType.HasValue
            || !TryGetStrategicLocalInvestment(
                manager,
                manager.FocusedPenIndex,
                automationDialogType,
                true,
                out LocalInvestmentDecision decision)
            || !decision.IsUpgrade)
        {
            equipmentHud.CloseUpgradeDialog();
            automationDialogType = null;
            ReleaseMouseButtons();
            SetStatus("ROUND  .  LOCAL TECH PLAN COMPLETE");
            yield return new WaitForSecondsRealtime(0.05f);
            yield break;
        }

        Button upgradeButton = FindNamedButton(
            GetLocalUpgradeButtonName(decision.Upgrade.Value));
        if (!IsUsable(upgradeButton))
        {
            equipmentHud.CloseUpgradeDialog();
            automationDialogType = null;
            SetStatus("ROUND  .  LOCAL TECH BUTTON UNAVAILABLE");
            yield return new WaitForSecondsRealtime(0.05f);
            yield break;
        }

        yield return ClickButton(
            upgradeButton,
            $"ROUND  .  PEN {decision.PenIndex + 1}  .  BUYING {decision.Label}");
        roundLocalPurchaseCount++;
        if (decision.Type == PenExpansionManager.EquipmentType.Robot)
        {
            pendingRobotRecoveryPens.Remove(decision.PenIndex);
        }
    }

    private IEnumerator ExecuteLocalInvestment(
        LocalInvestmentDecision decision)
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        if (manager == null || !manager.IsInitialized)
        {
            yield break;
        }

        if (manager.FocusedPenIndex != decision.PenIndex)
        {
            yield return ClickNamedButton(
                $"Pen {decision.PenIndex + 1} Button",
                $"ROUND  .  DEVELOPING PEN {decision.PenIndex + 1}  .  {decision.Label}");
            yield break;
        }

        Button equipmentButton = FindNamedButton(
            GetLocalEquipmentButtonName(decision.Type));
        if (!IsUsable(equipmentButton))
        {
            SetStatus(
                $"ROUND  .  PEN {decision.PenIndex + 1} {decision.Label} UNAVAILABLE");
            yield return new WaitForSecondsRealtime(0.05f);
            yield break;
        }

        bool alreadyOwned = manager.IsEquipmentOwned(
            decision.PenIndex,
            decision.Type);
        if (alreadyOwned)
        {
            automationDialogType = decision.Type;
            yield return ClickButton(
                equipmentButton,
                $"ROUND  .  OPENING PEN {decision.PenIndex + 1} {decision.Type.ToString().ToUpperInvariant()} TECH");
            yield break;
        }

        yield return ClickButton(
            equipmentButton,
            $"ROUND  .  PEN {decision.PenIndex + 1}  .  BUYING {decision.Label}");
        if (manager.IsEquipmentOwned(decision.PenIndex, decision.Type))
        {
            roundLocalPurchaseCount++;
            if (decision.Type == PenExpansionManager.EquipmentType.Robot)
            {
                pendingRobotRecoveryPens.Remove(decision.PenIndex);
            }
        }
    }

    private static string GetLocalEquipmentButtonName(
        PenExpansionManager.EquipmentType type)
    {
        return type switch
        {
            PenExpansionManager.EquipmentType.Incubator =>
                "Local INCUBATOR Button",
            PenExpansionManager.EquipmentType.Crosshatcher =>
                "Local CROSSHATCHER Button",
            PenExpansionManager.EquipmentType.Robot =>
                "Local ROBOT Button",
            _ => "Local AUTO-FEEDER Button"
        };
    }

    private static string GetLocalUpgradeButtonName(
        PenExpansionManager.EquipmentUpgrade upgrade)
    {
        return upgrade switch
        {
            PenExpansionManager.EquipmentUpgrade.IncubatorCapacity
                or PenExpansionManager.EquipmentUpgrade.RobotCapacity =>
                    "Upgrade CAPACITY Button",
            PenExpansionManager.EquipmentUpgrade.CrosshatcherQuality =>
                "Upgrade QUALITY Button",
            PenExpansionManager.EquipmentUpgrade.RobotSmartness =>
                "Upgrade LOGIC Button",
            PenExpansionManager.EquipmentUpgrade.RobotVacuum =>
                "Upgrade VACUUM Button",
            PenExpansionManager.EquipmentUpgrade.AutoFeederRange =>
                "Upgrade RANGE Button",
            _ => "Upgrade SPEED Button"
        };
    }

    private bool TryGetStrategicLocalInvestment(
        PenExpansionManager manager,
        int onlyPenIndex,
        PenExpansionManager.EquipmentType? onlyType,
        bool affordableOnly,
        out LocalInvestmentDecision best)
    {
        best = default;
        if (manager == null || !manager.IsInitialized)
        {
            return false;
        }

        bool found = false;
        bool underPressure = IsStrategyUnderPressure();
        bool collectionLimited = lastFailureWasCollectionLimited
            || lastCollectionRatio < HealthyCollectionRatio;
        int roundNumber = RoundSystem.Instance != null
            ? RoundSystem.Instance.RoundNumber
            : 0;
        long balance = EggScoreHud.CurrentCents;

        for (int penIndex = 0; penIndex < manager.PenCount; penIndex++)
        {
            if (!manager.IsPenOwned(penIndex)
                || (onlyPenIndex >= 0 && penIndex != onlyPenIndex))
            {
                continue;
            }

            int chickens = manager.GetChickenCount(penIndex);
            int populationDeficit = Mathf.Max(
                0,
                ChickenController.MaximumChickenCount - chickens);
            int looseEggs = GetAvailableEggCount(manager, penIndex);
            bool ownsIncubator = manager.IsEquipmentOwned(
                penIndex,
                PenExpansionManager.EquipmentType.Incubator);
            bool ownsRobot = manager.IsEquipmentOwned(
                penIndex,
                PenExpansionManager.EquipmentType.Robot);
            bool ownsAutoFeeder = manager.IsEquipmentOwned(
                penIndex,
                PenExpansionManager.EquipmentType.AutoFeeder);
            bool ownsCrosshatcher = manager.IsEquipmentOwned(
                penIndex,
                PenExpansionManager.EquipmentType.Crosshatcher);

            if (!onlyType.HasValue
                || onlyType.Value
                    == PenExpansionManager.EquipmentType.Incubator)
            {
                if (!ownsIncubator && populationDeficit > 0)
                {
                    ConsiderLocalInvestment(
                        penIndex,
                        PenExpansionManager.EquipmentType.Incubator,
                        null,
                        manager.GetEquipmentPurchaseCost(
                            PenExpansionManager.EquipmentType.Incubator),
                        5000 + populationDeficit,
                        "INCUBATOR",
                        balance,
                        affordableOnly,
                        ref found,
                        ref best);
                }
                else if (ownsIncubator && populationDeficit > 0)
                {
                    int speedLevel = manager.GetUpgradeLevel(
                        penIndex,
                        PenExpansionManager.EquipmentUpgrade.IncubatorSpeed);
                    int capacityLevel = manager.GetUpgradeLevel(
                        penIndex,
                        PenExpansionManager.EquipmentUpgrade.IncubatorCapacity);
                    ConsiderLocalInvestment(
                        penIndex,
                        PenExpansionManager.EquipmentType.Incubator,
                        PenExpansionManager.EquipmentUpgrade.IncubatorSpeed,
                        manager.GetUpgradeCost(
                            penIndex,
                            PenExpansionManager.EquipmentUpgrade.IncubatorSpeed),
                        4700 - speedLevel * 100 + populationDeficit,
                        $"INCUBATOR SPEED {speedLevel + 1}",
                        balance,
                        affordableOnly,
                        ref found,
                        ref best);
                    ConsiderLocalInvestment(
                        penIndex,
                        PenExpansionManager.EquipmentType.Incubator,
                        PenExpansionManager.EquipmentUpgrade.IncubatorCapacity,
                        manager.GetUpgradeCost(
                            penIndex,
                            PenExpansionManager.EquipmentUpgrade.IncubatorCapacity),
                        4500 - capacityLevel * 100 + populationDeficit,
                        $"INCUBATOR CAPACITY {capacityLevel + 1}",
                        balance,
                        affordableOnly,
                        ref found,
                        ref best);
                }
            }

            if (!onlyType.HasValue
                || onlyType.Value == PenExpansionManager.EquipmentType.Robot)
            {
                bool robotIsUseful = chickens >= CriticalPopulationGrowthFlock
                    || (chickens >= 8
                        && (collectionLimited || manager.OwnedPenCount > 1));
                if (!ownsRobot && robotIsUseful)
                {
                    ConsiderLocalInvestment(
                        penIndex,
                        PenExpansionManager.EquipmentType.Robot,
                        null,
                        manager.GetEquipmentPurchaseCost(
                            PenExpansionManager.EquipmentType.Robot),
                        collectionLimited ? 4400 : 2800,
                        "ROBOT",
                        balance,
                        affordableOnly,
                        ref found,
                        ref best);
                }
                else if (ownsRobot)
                {
                    int smartness = manager.GetUpgradeLevel(
                        penIndex,
                        PenExpansionManager.EquipmentUpgrade.RobotSmartness);
                    if (ownsIncubator
                        && populationDeficit > 0
                        && smartness
                            < EggCollectorRobot.PopulationGrowthSmartnessLevel)
                    {
                        ConsiderLocalInvestment(
                            penIndex,
                            PenExpansionManager.EquipmentType.Robot,
                            PenExpansionManager.EquipmentUpgrade.RobotSmartness,
                            manager.GetUpgradeCost(
                                penIndex,
                                PenExpansionManager.EquipmentUpgrade.RobotSmartness),
                            4600 - smartness * 100 + populationDeficit,
                            $"ROBOT LOGIC {smartness + 1}",
                            balance,
                            affordableOnly,
                            ref found,
                            ref best);
                    }

                    if (ownsCrosshatcher
                        && chickens >= MinimumStrategicCrosshatchFlock
                        && smartness
                            < EggCollectorRobot.ChickenArmsSmartnessLevel)
                    {
                        int nextSmartness = smartness + 1;
                        ConsiderLocalInvestment(
                            penIndex,
                            PenExpansionManager.EquipmentType.Robot,
                            PenExpansionManager.EquipmentUpgrade.RobotSmartness,
                            manager.GetUpgradeCost(
                                penIndex,
                                PenExpansionManager.EquipmentUpgrade.RobotSmartness),
                            4350 - smartness * 50,
                            nextSmartness
                                == EggCollectorRobot.ChickenArmsSmartnessLevel
                                    ? "ROBOT CHICKEN ARMS"
                                    : $"ROBOT LOGIC {nextSmartness}",
                            balance,
                            affordableOnly,
                            ref found,
                            ref best);
                    }

                    EggCollectorRobot robot = manager.GetRobotInPen(penIndex);
                    int desiredRobotLevel = Mathf.Clamp(
                        1 + roundNumber / 6 + (collectionLimited ? 1 : 0),
                        1,
                        EggCarryController.MaximumRobotLevel);
                    int capacityLevel = manager.GetUpgradeLevel(
                        penIndex,
                        PenExpansionManager.EquipmentUpgrade.RobotCapacity);
                    int speedLevel = manager.GetUpgradeLevel(
                        penIndex,
                        PenExpansionManager.EquipmentUpgrade.RobotSpeed);
                    bool robotBacklogged = robot != null
                        && robot.StoredEggs + looseEggs
                            >= Mathf.Max(4, robot.Capacity / 2);
                    if (capacityLevel < desiredRobotLevel)
                    {
                        ConsiderLocalInvestment(
                            penIndex,
                            PenExpansionManager.EquipmentType.Robot,
                            PenExpansionManager.EquipmentUpgrade.RobotCapacity,
                            manager.GetUpgradeCost(
                                penIndex,
                                PenExpansionManager.EquipmentUpgrade.RobotCapacity),
                            collectionLimited ? 3700 : 2500,
                            $"ROBOT CAPACITY {capacityLevel + 1}",
                            balance,
                            affordableOnly,
                            ref found,
                            ref best);
                    }
                    if (speedLevel < desiredRobotLevel)
                    {
                        ConsiderLocalInvestment(
                            penIndex,
                            PenExpansionManager.EquipmentType.Robot,
                            PenExpansionManager.EquipmentUpgrade.RobotSpeed,
                            manager.GetUpgradeCost(
                                penIndex,
                                PenExpansionManager.EquipmentUpgrade.RobotSpeed),
                            collectionLimited ? 3600 : 2400,
                            $"ROBOT SPEED {speedLevel + 1}",
                            balance,
                            affordableOnly,
                            ref found,
                            ref best);
                    }

                    int vacuumLevel = manager.GetUpgradeLevel(
                        penIndex,
                        PenExpansionManager.EquipmentUpgrade.RobotVacuum);
                    if (vacuumLevel < desiredRobotLevel)
                    {
                        ConsiderLocalInvestment(
                            penIndex,
                            PenExpansionManager.EquipmentType.Robot,
                            PenExpansionManager.EquipmentUpgrade.RobotVacuum,
                            manager.GetUpgradeCost(
                                penIndex,
                                PenExpansionManager.EquipmentUpgrade.RobotVacuum),
                            collectionLimited || robotBacklogged ? 3550 : 2350,
                            $"ROBOT VACUUM {vacuumLevel + 1}",
                            balance,
                            affordableOnly,
                            ref found,
                            ref best);
                    }
                }
            }

            if (!onlyType.HasValue
                || onlyType.Value
                    == PenExpansionManager.EquipmentType.AutoFeeder)
            {
                bool feederIsUseful = chickens >= 20
                    && (underPressure || roundNumber >= 6);
                if (!ownsAutoFeeder && feederIsUseful)
                {
                    ConsiderLocalInvestment(
                        penIndex,
                        PenExpansionManager.EquipmentType.AutoFeeder,
                        null,
                        manager.GetEquipmentPurchaseCost(
                            PenExpansionManager.EquipmentType.AutoFeeder),
                        underPressure ? 3300 : 2100,
                        "AUTO-FEEDER",
                        balance,
                        affordableOnly,
                        ref found,
                        ref best);
                }
                else if (ownsAutoFeeder)
                {
                    int desiredFeederLevel = Mathf.Clamp(
                        1 + roundNumber / 10 + (underPressure ? 1 : 0),
                        1,
                        AutoFeederController.MaximumLevel);
                    int speedLevel = manager.GetUpgradeLevel(
                        penIndex,
                        PenExpansionManager.EquipmentUpgrade.AutoFeederSpeed);
                    int rangeLevel = manager.GetUpgradeLevel(
                        penIndex,
                        PenExpansionManager.EquipmentUpgrade.AutoFeederRange);
                    if (speedLevel < desiredFeederLevel)
                    {
                        ConsiderLocalInvestment(
                            penIndex,
                            PenExpansionManager.EquipmentType.AutoFeeder,
                            PenExpansionManager.EquipmentUpgrade.AutoFeederSpeed,
                            manager.GetUpgradeCost(
                                penIndex,
                                PenExpansionManager.EquipmentUpgrade.AutoFeederSpeed),
                            underPressure ? 3000 : 2000,
                            $"AUTO-FEEDER SPEED {speedLevel + 1}",
                            balance,
                            affordableOnly,
                            ref found,
                            ref best);
                    }
                    if (rangeLevel < desiredFeederLevel)
                    {
                        ConsiderLocalInvestment(
                            penIndex,
                            PenExpansionManager.EquipmentType.AutoFeeder,
                            PenExpansionManager.EquipmentUpgrade.AutoFeederRange,
                            manager.GetUpgradeCost(
                                penIndex,
                                PenExpansionManager.EquipmentUpgrade.AutoFeederRange),
                            underPressure ? 2900 : 1900,
                            $"AUTO-FEEDER RANGE {rangeLevel + 1}",
                            balance,
                            affordableOnly,
                            ref found,
                            ref best);
                    }
                }
            }

            if (!onlyType.HasValue
                || onlyType.Value
                    == PenExpansionManager.EquipmentType.Crosshatcher)
            {
                bool crosshatcherIsSafe = chickens
                        >= MinimumStrategicCrosshatchFlock
                    || chickens >= ChickenController.MaximumChickenCount;
                if (!ownsCrosshatcher && crosshatcherIsSafe)
                {
                    ConsiderLocalInvestment(
                        penIndex,
                        PenExpansionManager.EquipmentType.Crosshatcher,
                        null,
                        manager.GetEquipmentPurchaseCost(
                            PenExpansionManager.EquipmentType.Crosshatcher),
                        1500,
                        "CROSSHATCHER",
                        balance,
                        affordableOnly,
                        ref found,
                        ref best);
                }
                else if (ownsCrosshatcher && crosshatcherIsSafe)
                {
                    int desiredLevel = Mathf.Clamp(
                        1 + roundNumber / 6,
                        1,
                        CrosshatcherController.MaximumLevel);
                    int qualityLevel = manager.GetUpgradeLevel(
                        penIndex,
                        PenExpansionManager.EquipmentUpgrade.CrosshatcherQuality);
                    int speedLevel = manager.GetUpgradeLevel(
                        penIndex,
                        PenExpansionManager.EquipmentUpgrade.CrosshatcherSpeed);
                    if (qualityLevel < desiredLevel)
                    {
                        ConsiderLocalInvestment(
                            penIndex,
                            PenExpansionManager.EquipmentType.Crosshatcher,
                            PenExpansionManager.EquipmentUpgrade.CrosshatcherQuality,
                            manager.GetUpgradeCost(
                                penIndex,
                                PenExpansionManager.EquipmentUpgrade.CrosshatcherQuality),
                            1700,
                            $"CROSSHATCHER QUALITY {qualityLevel + 1}",
                            balance,
                            affordableOnly,
                            ref found,
                            ref best);
                    }
                    if (speedLevel < desiredLevel)
                    {
                        ConsiderLocalInvestment(
                            penIndex,
                            PenExpansionManager.EquipmentType.Crosshatcher,
                            PenExpansionManager.EquipmentUpgrade.CrosshatcherSpeed,
                            manager.GetUpgradeCost(
                                penIndex,
                                PenExpansionManager.EquipmentUpgrade.CrosshatcherSpeed),
                            1600,
                            $"CROSSHATCHER SPEED {speedLevel + 1}",
                            balance,
                            affordableOnly,
                            ref found,
                            ref best);
                    }
                }
            }
        }

        return found;
    }

    private static void ConsiderLocalInvestment(
        int penIndex,
        PenExpansionManager.EquipmentType type,
        PenExpansionManager.EquipmentUpgrade? upgrade,
        int cost,
        int score,
        string label,
        long balance,
        bool affordableOnly,
        ref bool found,
        ref LocalInvestmentDecision best)
    {
        if (cost <= 0 || (affordableOnly && cost > balance))
        {
            return;
        }

        if (!found
            || score > best.Score
            || (score == best.Score && cost < best.Cost))
        {
            found = true;
            best = new LocalInvestmentDecision(
                penIndex,
                type,
                upgrade,
                cost,
                score,
                label);
        }
    }

    private bool TryGetNextOwnedPenInvestment(
        PenExpansionManager manager,
        out int targetPenIndex,
        out string investmentLabel,
        out int investmentCost)
    {
        if (TryGetStrategicLocalInvestment(
                manager,
                -1,
                null,
                false,
                out LocalInvestmentDecision decision))
        {
            targetPenIndex = decision.PenIndex;
            investmentLabel = decision.Label;
            investmentCost = decision.Cost;
            return true;
        }

        targetPenIndex = -1;
        investmentLabel = string.Empty;
        investmentCost = 0;
        return false;
    }

    private static int GetAvailableEggCount(
        PenExpansionManager manager,
        int penIndex)
    {
        int availableEggs = 0;
        var eggs = ChickenEgg.ActiveInstances;
        for (int index = eggs.Count - 1; index >= 0; index--)
        {
            ChickenEgg egg = eggs[index];
            if (egg != null
                && !egg.IsHeld
                && !egg.IsCollected
                && manager.GetClosestPenIndex(egg.transform.position)
                    == penIndex)
            {
                availableEggs++;
            }
        }

        return availableEggs;
    }

    private void CaptureRoundEndCollectionBacklog()
    {
        lastLooseEggsByPen.Clear();
        lastChickenCountsByPen.Clear();

        PenExpansionManager manager = PenExpansionManager.Instance;
        if (manager == null || !manager.IsInitialized)
        {
            return;
        }

        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index))
            {
                continue;
            }

            lastLooseEggsByPen[index] = GetAvailableEggCount(manager, index);
            lastChickenCountsByPen[index] = manager.GetChickenCount(index);
        }
    }

    private bool HasLargeRoundEndCollectionBacklog(int penIndex)
    {
        if (!lastLooseEggsByPen.TryGetValue(penIndex, out int looseEggs)
            || !lastChickenCountsByPen.TryGetValue(
                penIndex,
                out int chickenCount))
        {
            return false;
        }

        int threshold = Mathf.Max(
            MinimumLooseEggsForFeedThrottle,
            Mathf.CeilToInt(
                chickenCount * LooseEggsPerChickenForFeedThrottle));
        return looseEggs >= threshold;
    }

    private bool ShouldThrottleFeedForCollection(int penIndex)
    {
        return HasLargeRoundEndCollectionBacklog(penIndex)
            && !collectionRecoveryPurchasedThisShop
            && !collectionRecoveryPlannedThisShop
            && !plannedRobotRecoveryPens.Contains(penIndex);
    }

    private static bool TryGetCriticalIdleIncubatorPen(
        PenExpansionManager manager,
        out int targetPenIndex)
    {
        targetPenIndex = -1;
        if (manager == null || !manager.IsInitialized)
        {
            return false;
        }

        int lowestPopulation = int.MaxValue;
        int bestEggCount = -1;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index)
                || HasAutomaticIncubatorLoader(manager, index))
            {
                continue;
            }

            int chickenCount = manager.GetChickenCount(index);
            int eggCount = GetAvailableEggCount(manager, index);
            IncubatorController incubator = manager.GetIncubator(index);
            if (chickenCount >= CriticalPopulationGrowthFlock
                || eggCount <= 0
                || incubator == null
                || !incubator.isActiveAndEnabled
                || incubator.StoredEggs > 0
                || incubator.AvailableCapacity <= 0)
            {
                continue;
            }

            if (chickenCount < lowestPopulation
                || (chickenCount == lowestPopulation && eggCount > bestEggCount))
            {
                targetPenIndex = index;
                lowestPopulation = chickenCount;
                bestEggCount = eggCount;
            }
        }

        return targetPenIndex >= 0;
    }

    private static bool HasAvailableEggs(PenExpansionManager manager)
    {
        var eggs = ChickenEgg.ActiveInstances;
        for (int index = eggs.Count - 1; index >= 0; index--)
        {
            ChickenEgg egg = eggs[index];
            if (egg == null || egg.IsHeld || egg.IsCollected)
            {
                continue;
            }

            if (manager == null
                || !manager.IsInitialized
                || manager.IsPenOwned(
                    manager.GetClosestPenIndex(egg.transform.position)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetQuotaVacuumPen(
        PenExpansionManager manager,
        int currentPenIndex,
        out int targetPenIndex)
    {
        targetPenIndex = -1;
        if (manager == null || !manager.IsInitialized)
        {
            return HasAvailableEggs(manager);
        }

        long bestLooseValue = -1;
        int bestEggCount = -1;
        var eggs = ChickenEgg.ActiveInstances;
        for (int penIndex = 0; penIndex < manager.PenCount; penIndex++)
        {
            if (!manager.IsPenOwned(penIndex))
            {
                continue;
            }

            long looseValue = 0;
            int eggCount = 0;
            for (int eggIndex = eggs.Count - 1; eggIndex >= 0; eggIndex--)
            {
                ChickenEgg egg = eggs[eggIndex];
                if (egg == null
                    || egg.IsHeld
                    || egg.IsCollected
                    || manager.GetClosestPenIndex(egg.transform.position)
                        != penIndex)
                {
                    continue;
                }

                int saleValue = EggContainer.CalculateSaleValueCents(
                    egg.ValueCents,
                    egg.WeightKilograms);
                looseValue = saleValue > long.MaxValue - looseValue
                    ? long.MaxValue
                    : looseValue + saleValue;
                eggCount++;
            }

            if (eggCount <= 0)
            {
                continue;
            }

            bool preferCurrentOnExactTie = looseValue == bestLooseValue
                && eggCount == bestEggCount
                && penIndex == currentPenIndex;
            if (looseValue > bestLooseValue
                || (looseValue == bestLooseValue
                    && eggCount > bestEggCount)
                || preferCurrentOnExactTie)
            {
                bestLooseValue = looseValue;
                bestEggCount = eggCount;
                targetPenIndex = penIndex;
            }
        }

        return targetPenIndex >= 0;
    }

    private bool TryGetPenNeedingFeedCoverage(
        PenExpansionManager manager,
        out int targetPenIndex)
    {
        targetPenIndex = -1;
        int largestDeficit = 0;
        int largestFlock = -1;
        bool finishRecoveryCoverage = ShouldForceRecoveryFeedCoverage();
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index)
                || manager.IsEquipmentOwned(
                    index,
                    PenExpansionManager.EquipmentType.AutoFeeder)
                || foodPlacementSpacingBlockedPens.Contains(index)
                || (!finishRecoveryCoverage
                    && ArePenChickensWellFed(manager, index)))
            {
                continue;
            }

            int deficit = GetDesiredFeedPileCount(index)
                - CountAvailableFoodPiles(index);
            int chickenCount = manager.GetChickenCount(index);
            if (deficit > largestDeficit
                || (deficit == largestDeficit
                    && deficit > 0
                    && chickenCount > largestFlock))
            {
                targetPenIndex = index;
                largestDeficit = deficit;
                largestFlock = chickenCount;
            }
        }

        return targetPenIndex >= 0;
    }

    private bool ShouldForceRecoveryFeedCoverage()
    {
        return consecutiveFailedRounds > 0
            && !lastFailureWasCollectionLimited;
    }

    private bool TryGetAffordablePenPurchase(
        PenExpansionManager manager,
        out int nextPenIndex)
    {
        nextPenIndex = manager != null
            ? manager.NextUnownedPenIndex
            : -1;
        return manager != null
            && manager.AreAdditionalPensUnlocked
            && manager.OwnedPenCount < automatedOwnedPenTarget
            && !manager.IsPenPurchaseInProgress
            && nextPenIndex >= 0
            && EggScoreHud.CurrentCents
                >= manager.GetPenCostCents(nextPenIndex);
    }

    private bool ShouldBuyNextPen(
        PenExpansionManager manager,
        bool hasPendingPenInvestment,
        int pendingPenInvestmentCost,
        out int nextPenIndex)
    {
        if (!TryGetAffordablePenPurchase(manager, out nextPenIndex))
        {
            return false;
        }

        // A failed quota means the current production plan is insufficient.
        // Expand only after existing pens have enough flock to make another
        // three-chicken starter pen worthwhile. Early failures are recovered
        // with incubators, feed, value, and collection instead of dilution.
        if (consecutiveFailedRounds > 0)
        {
            return recoveryExpansionFailureCount != consecutiveFailedRounds
                && AreOwnedPensReadyForExpansion(manager, true);
        }

        // Full robot/feeder automation is not required before expanding. A
        // healthy, incubator-backed flock plus a modest development reserve is
        // enough; this also prevents large late-game balances being hoarded.
        return AreOwnedPensReadyForExpansion(manager, false)
            && CanFundPenAndPendingInvestment(
                manager,
                nextPenIndex,
                hasPendingPenInvestment,
                pendingPenInvestmentCost);
    }

    private static bool CanFundPenAndPendingInvestment(
        PenExpansionManager manager,
        int nextPenIndex,
        bool hasPendingPenInvestment,
        int pendingPenInvestmentCost)
    {
        if (manager == null || nextPenIndex < 0)
        {
            return false;
        }

        long requiredCents = manager.GetPenCostCents(nextPenIndex);
        if (hasPendingPenInvestment)
        {
            long developmentReserve = System.Math.Min(
                Mathf.Max(0, pendingPenInvestmentCost),
                System.Math.Max(400L, requiredCents / 2L));
            requiredCents += developmentReserve;
        }

        return EggScoreHud.CurrentCents >= requiredCents;
    }

    private static bool AreOwnedPensReadyForExpansion(
        PenExpansionManager manager,
        bool recovery)
    {
        if (manager == null || !manager.IsInitialized)
        {
            return false;
        }

        int requiredPopulation = recovery
            ? 12
            : Mathf.Clamp(16 + manager.OwnedPenCount * 2, 18, 30);
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index))
            {
                continue;
            }

            if (!manager.IsEquipmentOwned(
                    index,
                    PenExpansionManager.EquipmentType.Incubator)
                || manager.GetChickenCount(index) < requiredPopulation)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAnyOwnedAutoFeeder(
        PenExpansionManager manager)
    {
        if (manager == null)
        {
            return false;
        }

        for (int penIndex = 0; penIndex < manager.PenCount; penIndex++)
        {
            if (manager.IsEquipmentOwned(
                    penIndex,
                    PenExpansionManager.EquipmentType.AutoFeeder))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetRobotBacklogDevelopmentTarget(
        PenExpansionManager manager,
        out int automatedPenIndex,
        out int developmentPenIndex)
    {
        automatedPenIndex = -1;
        developmentPenIndex = -1;
        int largestBacklog = -1;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index))
            {
                continue;
            }

            EggCollectorRobot robot = manager.GetRobotInPen(index);
            if (robot == null)
            {
                continue;
            }

            int looseEggs = GetAvailableEggCount(manager, index);
            int backlog = robot.StoredEggs + looseEggs;
            int sufficientBacklog = Mathf.Clamp(
                Mathf.CeilToInt(robot.Capacity * 0.35f),
                4,
                12);
            if (backlog >= sufficientBacklog && backlog > largestBacklog)
            {
                automatedPenIndex = index;
                largestBacklog = backlog;
            }
        }

        if (automatedPenIndex < 0)
        {
            return false;
        }

        bool bestHasRobot = true;
        int fewestChickens = int.MaxValue;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (index == automatedPenIndex || !manager.IsPenOwned(index))
            {
                continue;
            }

            bool hasRobot = manager.HasRobotInPen(index);
            int chickenCount = manager.GetChickenCount(index);
            if (developmentPenIndex < 0
                || (bestHasRobot && !hasRobot)
                || (bestHasRobot == hasRobot
                    && chickenCount < fewestChickens))
            {
                developmentPenIndex = index;
                bestHasRobot = hasRobot;
                fewestChickens = chickenCount;
            }
        }

        return developmentPenIndex >= 0;
    }

    private static bool NeedsHigherTierChickens()
    {
        var chickens = ChickenController.ActiveInstances;
        for (int index = 0; index < chickens.Count; index++)
        {
            ChickenController chicken = chickens[index];
            if (chicken != null
                && chicken.Breed
                    == ChickenController.ChickenBreed.Cosmic)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIncubatorUpgrade(
        ProgressionSystem.UpgradeId upgradeId)
    {
        return upgradeId == ProgressionSystem.UpgradeId.IncubatorInstall
            || upgradeId == ProgressionSystem.UpgradeId.IncubatorCapacity
            || upgradeId == ProgressionSystem.UpgradeId.IncubatorSpeed;
    }

    private static bool IsTurboConsumableRoot(
        ProgressionSystem.UpgradeId upgradeId)
    {
        return upgradeId is ProgressionSystem.UpgradeId.IncubatorTurbo
            or ProgressionSystem.UpgradeId.CrosshatcherTurbo
            or ProgressionSystem.UpgradeId.RobotTurbo;
    }

    private static bool IsLocalEquipmentProgressionUpgrade(
        ProgressionSystem.UpgradeId upgradeId)
    {
        return upgradeId is ProgressionSystem.UpgradeId.IncubatorInstall
            or ProgressionSystem.UpgradeId.IncubatorCapacity
            or ProgressionSystem.UpgradeId.IncubatorSpeed
            or ProgressionSystem.UpgradeId.CrosshatcherInstall
            or ProgressionSystem.UpgradeId.CrosshatcherSpeed
            or ProgressionSystem.UpgradeId.CrosshatcherQuality
            or ProgressionSystem.UpgradeId.RobotUnlock
            or ProgressionSystem.UpgradeId.RobotSpeed
            or ProgressionSystem.UpgradeId.RobotCapacity
            or ProgressionSystem.UpgradeId.RobotSmartness;
    }

    private static bool IsCollectionProgressionUpgrade(
        ProgressionSystem.UpgradeId upgradeId)
    {
        return upgradeId is ProgressionSystem.UpgradeId.BasketCapacity
            or ProgressionSystem.UpgradeId.BasketReach
            or ProgressionSystem.UpgradeId.VacuumUnlock
            or ProgressionSystem.UpgradeId.VacuumPower
            or ProgressionSystem.UpgradeId.VacuumRange;
    }

    private void UpdateFeedStrategy(RoundSystem round)
    {
        int baseline = Mathf.Max(1, minimumFeedBags);
        desiredFeedPiles = baseline;
        if (round == null)
        {
            return;
        }

        // Production coverage must continue growing even after a weak round.
        // Otherwise a failed round resets the bot to one feed pile and makes
        // the following retry still less productive.
        int roundScaledFloor = 2 + round.RoundNumber / 4;
        desiredFeedPiles = Mathf.Clamp(
            Mathf.Max(baseline, roundScaledFloor),
            baseline,
            Mathf.Max(MaximumDesiredFeedPiles, baseline));

        bool collectionWasHealthy = round.RoundEggsLaid <= 0
            || round.RoundEggsProcessed
                >= Mathf.CeilToInt(
                    round.RoundEggsLaid * HealthyCollectionRatio);
        if (!round.DidPassRound && collectionWasHealthy)
        {
            // A missed quota with functioning collection is a production
            // problem. Cover the full flock next round instead of increasing
            // feed by only one pile every few rounds.
            desiredFeedPiles = Mathf.Max(
                desiredFeedPiles,
                GetMaximumChickenCoveragePileTarget());
        }

        if (round.RoundEggsLaid < 3)
        {
            return;
        }

        int laid = round.RoundEggsLaid;
        int processed = Mathf.Min(laid, round.RoundEggsProcessed);
        int leftovers = Mathf.Max(0, laid - processed);
        float collectionRatio = processed / (float)laid;
        int allowedLeftovers = Mathf.Max(
            MaximumEfficientRoundLeftovers,
            Mathf.CeilToInt(laid * 0.2f));
        if (collectionRatio < HealthyCollectionRatio
            || leftovers > allowedLeftovers)
        {
            return;
        }

        int scaledTarget = 3 + processed / 8;
        desiredFeedPiles = Mathf.Max(
            desiredFeedPiles,
            Mathf.Clamp(
                scaledTarget,
                Mathf.Max(2, baseline),
                Mathf.Max(MaximumDesiredFeedPiles, baseline)));
    }

    private static int GetDesiredFeedTier(RoundSystem round)
    {
        if (round == null)
        {
            return 2;
        }

        int targetTier = 2 + round.RoundNumber / 3;
        if (!round.DidPassRound)
        {
            targetTier++;
        }

        return Mathf.Clamp(
            targetTier,
            2,
            FoodShopController.MaximumFeedTier);
    }

    private static int GetMaximumChickenCoveragePileTarget()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        int largestFlock = 0;
        if (manager != null && manager.IsInitialized)
        {
            for (int index = 0; index < manager.PenCount; index++)
            {
                if (manager.IsPenOwned(index))
                {
                    largestFlock = Mathf.Max(
                        largestFlock,
                        manager.GetChickenCount(index));
                }
            }
        }
        else
        {
            IReadOnlyList<ChickenController> chickens =
                ChickenController.ActiveInstances;
            for (int index = 0; index < chickens.Count; index++)
            {
                if (chickens[index] != null
                    && chickens[index].isActiveAndEnabled)
                {
                    largestFlock++;
                }
            }
        }

        return Mathf.Clamp(
            Mathf.CeilToInt(
                largestFlock / (float)ChickensPerDesiredFoodPile),
            1,
            MaximumDesiredFeedPiles);
    }

    private int GetDesiredFeedPileCount(
        int penIndex = -1,
        bool ignoreCollectionThrottle = false)
    {
        int activeChickenCount = 0;
        PenExpansionManager manager = PenExpansionManager.Instance;
        if (manager != null
            && manager.IsInitialized
            && penIndex >= 0
            && manager.IsPenOwned(penIndex))
        {
            if (manager.IsEquipmentOwned(
                    penIndex,
                    PenExpansionManager.EquipmentType.AutoFeeder))
            {
                return 0;
            }

            activeChickenCount = manager.GetChickenCount(penIndex);
        }
        else
        {
            var chickens = ChickenController.ActiveInstances;
            for (int index = 0; index < chickens.Count; index++)
            {
                if (chickens[index] != null
                    && chickens[index].isActiveAndEnabled)
                {
                    activeChickenCount++;
                }
            }
        }

        bool hasVacuum = EggCarryController.Instance != null
            && EggCarryController.Instance.HasVacuum;
        int chickensPerPile = hasVacuum
            ? ChickensPerVacuumFeedPile
            : ChickensPerDesiredFoodPile;
        int sharedPileTarget = Mathf.CeilToInt(
            activeChickenCount / (float)chickensPerPile);
        int desiredCount;
        if (hasVacuum)
        {
            desiredCount = Mathf.Clamp(
                sharedPileTarget,
                0,
                MaximumDesiredFeedPiles);
        }
        else
        {
            desiredCount = Mathf.Min(desiredFeedPiles, sharedPileTarget);
        }

        if (!ignoreCollectionThrottle
            && penIndex >= 0
            && desiredCount > 1
            && ShouldThrottleFeedForCollection(penIndex))
        {
            desiredCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    desiredCount * CollectionLimitedFeedMultiplier));
        }

        return desiredCount;
    }

    private int GetDesiredTotalFeedPileCount()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        if (manager == null || !manager.IsInitialized)
        {
            return GetDesiredFeedPileCount();
        }

        int total = 0;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (manager.IsPenOwned(index)
                && !manager.IsEquipmentOwned(
                    index,
                    PenExpansionManager.EquipmentType.AutoFeeder))
            {
                total += GetDesiredFeedPileCount(index);
            }
        }

        return total;
    }

    private int GetRequiredFeedInventory(
        PenExpansionManager manager,
        bool ignoreCollectionThrottle = false)
    {
        if (manager == null || !manager.IsInitialized)
        {
            return Mathf.Max(
                0,
                GetDesiredFeedPileCount(-1, ignoreCollectionThrottle)
                    + FeedReserveBagsPerManualPen
                    - CountAvailableFoodPiles());
        }

        int required = 0;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index)
                || manager.IsEquipmentOwned(
                    index,
                    PenExpansionManager.EquipmentType.AutoFeeder))
            {
                continue;
            }

            required += Mathf.Max(
                0,
                GetDesiredFeedPileCount(index, ignoreCollectionThrottle)
                    + FeedReserveBagsPerManualPen
                    - CountAvailableFoodPiles(index));
        }

        return required;
    }

    private static int CountAvailableManualFeedCoverage(
        PenExpansionManager manager)
    {
        if (manager == null || !manager.IsInitialized)
        {
            return CountAvailableFoodPiles();
        }

        int available = 0;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (manager.IsPenOwned(index)
                && !manager.IsEquipmentOwned(
                    index,
                    PenExpansionManager.EquipmentType.AutoFeeder))
            {
                available += CountAvailableFoodPiles(index);
            }
        }

        return available;
    }

    private static int CountAvailableFoodPiles(int penIndex = -1)
    {
        float available = 0f;
        PenExpansionManager manager = PenExpansionManager.Instance;
        var piles = FoodPile.ActivePiles;

        for (int index = 0; index < piles.Count; index++)
        {
            FoodPile pile = piles[index];
            if (pile != null
                && pile.IsAvailable
                && (penIndex < 0
                    || manager == null
                    || manager.GetClosestPenIndex(pile.transform.position)
                        == penIndex))
            {
                // A nearly empty pile is not next-round coverage. Summing
                // full-pile equivalents makes the shop buy its replacement
                // before it disappears moments into the next round.
                available += pile.RemainingFraction;
            }
        }

        return Mathf.FloorToInt(available + 0.0001f);
    }

    private static bool HasAvailableFoodPile(int penIndex)
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        IReadOnlyList<FoodPile> piles = FoodPile.ActivePiles;
        for (int index = 0; index < piles.Count; index++)
        {
            FoodPile pile = piles[index];
            if (pile != null
                && pile.IsAvailable
                && (manager == null
                    || manager.GetClosestPenIndex(pile.transform.position)
                        == penIndex))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ArePenChickensWellFed(
        PenExpansionManager manager,
        int penIndex)
    {
        if (!HasAvailableFoodPile(penIndex))
        {
            return false;
        }

        int chickenCount = 0;
        float totalFoodScore = 0f;
        IReadOnlyList<ChickenController> chickens =
            ChickenController.ActiveInstances;
        for (int index = 0; index < chickens.Count; index++)
        {
            ChickenController chicken = chickens[index];
            if (chicken == null
                || (manager != null
                    && manager.IsInitialized
                    && manager.GetClosestPenIndex(chicken.transform.position)
                        != penIndex))
            {
                continue;
            }

            float foodScore = chicken.FoodScoreNormalized;
            if (foodScore < HungryFoodScore)
            {
                return false;
            }

            totalFoodScore += foodScore;
            chickenCount++;
        }

        return chickenCount <= 0
            || totalFoodScore / chickenCount >= WellFedAverageScore;
    }

    private static float GetMinimumFoodSearchRadius(
        PenExpansionManager manager,
        int penIndex)
    {
        float minimumRadius = float.PositiveInfinity;
        IReadOnlyList<ChickenController> chickens =
            ChickenController.ActiveInstances;
        for (int index = 0; index < chickens.Count; index++)
        {
            ChickenController chicken = chickens[index];
            if (chicken != null
                && (manager == null
                    || !manager.IsInitialized
                    || manager.GetClosestPenIndex(chicken.transform.position)
                        == penIndex))
            {
                minimumRadius = Mathf.Min(
                    minimumRadius,
                    chicken.FoodSearchRadius);
            }
        }

        return float.IsPositiveInfinity(minimumRadius)
            ? 2f
            : Mathf.Max(0.5f, minimumRadius);
    }

    private static float GetNearestFoodPileDistance(
        PenExpansionManager manager,
        int penIndex,
        Vector3 candidate)
    {
        float nearestDistance = float.PositiveInfinity;
        IReadOnlyList<FoodPile> piles = FoodPile.ActivePiles;
        for (int index = 0; index < piles.Count; index++)
        {
            FoodPile pile = piles[index];
            if (pile == null
                || !pile.IsAvailable
                || (manager != null
                    && manager.IsInitialized
                    && manager.GetClosestPenIndex(pile.transform.position)
                        != penIndex))
            {
                continue;
            }

            Vector3 offset = pile.transform.position - candidate;
            offset.y = 0f;
            nearestDistance = Mathf.Min(
                nearestDistance,
                offset.magnitude);
        }

        return nearestDistance;
    }

    private bool TryFindCrosshatcherChicken(
        CrosshatcherController crosshatcher,
        out ChickenController selectedChicken,
        out ChickenPickupTarget selectedTarget)
    {
        selectedChicken = null;
        selectedTarget = null;

        if (crosshatcher == null
            || !crosshatcher.isActiveAndEnabled
            || crosshatcher.IsProcessing
            || !crosshatcher.CanAcceptCarriedChicken)
        {
            return false;
        }

        Camera camera = GetGameplayCamera();

        if (camera == null)
        {
            return false;
        }

        ChickenPickupTarget[] targets =
            Object.FindObjectsByType<ChickenPickupTarget>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        var candidates = new List<CrosshatchCandidate>(targets.Length);
        PenExpansionManager penManager = PenExpansionManager.Instance;
        int focusedPenIndex = penManager != null && penManager.IsInitialized
            ? penManager.FocusedPenIndex
            : -1;
        int focusedChickenCount = focusedPenIndex >= 0
            ? penManager.GetChickenCount(focusedPenIndex)
            : ChickenController.ActiveInstances.Count;
        bool completingPartialCycle = crosshatcher.OccupiedSlots > 0;
        if (crosshatcher.OccupiedSlots == 0
            && focusedChickenCount
                < MinimumStrategicCrosshatchFlock)
        {
            return false;
        }

        Vector2 pointerPosition = automationInputMouse != null
            ? automationInputMouse.position.ReadValue()
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        for (int index = 0; index < targets.Length; index++)
        {
            ChickenPickupTarget target = targets[index];
            ChickenController chicken = target != null
                ? target.Chicken
                : null;

            if (target == null
                || !target.CanPickUp
                || chicken == null
                || (!completingPartialCycle
                    && chicken.Breed
                        == ChickenController.ChickenBreed.Cosmic)
                || focusedPenIndex >= 0
                    && penManager.GetClosestPenIndex(
                        chicken.transform.position) != focusedPenIndex
                || !TryGetChickenPickupScreenPoint(
                    chicken,
                    target,
                    out Vector2 point))
            {
                continue;
            }

            candidates.Add(new CrosshatchCandidate(
                chicken,
                target,
                Vector2.SqrMagnitude(point - pointerPosition)));
        }

        if (crosshatcher.OccupiedSlots > 0)
        {
            return TrySelectSecondCrosshatchChicken(
                crosshatcher,
                candidates,
                out selectedChicken,
                out selectedTarget);
        }

        return TrySelectBestCrosshatchPair(
                crosshatcher,
                candidates,
                out selectedChicken,
                out selectedTarget);
    }

    private static bool TrySelectBestCrosshatchPair(
        CrosshatcherController crosshatcher,
        List<CrosshatchCandidate> candidates,
        out ChickenController selectedChicken,
        out ChickenPickupTarget selectedTarget)
    {
        selectedChicken = null;
        selectedTarget = null;
        if (candidates == null || candidates.Count < 2)
        {
            return false;
        }

        if (IsChickenCapReached()
            && TrySelectLowestMatchingCrosshatchPair(
                candidates,
                out selectedChicken,
                out selectedTarget))
        {
            return true;
        }

        bool canReplaceChicken = HasWorkingIncubator();
        float bestProfit = float.NegativeInfinity;
        float bestResultTier = -1f;
        float bestDistance = float.PositiveInfinity;
        int bestFirstIndex = -1;

        for (int firstIndex = 0;
            firstIndex < candidates.Count - 1;
            firstIndex++)
        {
            for (int secondIndex = firstIndex + 1;
                secondIndex < candidates.Count;
                secondIndex++)
            {
                CrosshatchCandidate first = candidates[firstIndex];
                CrosshatchCandidate second = candidates[secondIndex];
                float profit = GetExpectedCrosshatchProfit(
                    first.Chicken.Breed,
                    second.Chicken.Breed,
                    crosshatcher.ImprovementChance,
                    canReplaceChicken);
                float resultTier = GetExpectedCrosshatchTier(
                    first.Chicken.Breed,
                    second.Chicken.Breed,
                    crosshatcher.ImprovementChance);
                float distance = first.PointerDistance
                    + second.PointerDistance;

                if (profit < bestProfit
                    || Mathf.Approximately(profit, bestProfit)
                        && resultTier < bestResultTier
                    || Mathf.Approximately(profit, bestProfit)
                        && Mathf.Approximately(
                            resultTier,
                            bestResultTier)
                        && distance >= bestDistance)
                {
                    continue;
                }

                bestProfit = profit;
                bestResultTier = resultTier;
                bestDistance = distance;
                bestFirstIndex = SelectFirstParentIndex(
                    candidates,
                    firstIndex,
                    secondIndex);
            }
        }

        if (bestFirstIndex < 0)
        {
            return false;
        }

        CrosshatchCandidate selected = candidates[bestFirstIndex];
        selectedChicken = selected.Chicken;
        selectedTarget = selected.Target;
        return true;
    }

    private static bool TrySelectSecondCrosshatchChicken(
        CrosshatcherController crosshatcher,
        List<CrosshatchCandidate> candidates,
        out ChickenController selectedChicken,
        out ChickenPickupTarget selectedTarget)
    {
        selectedChicken = null;
        selectedTarget = null;
        if (candidates == null
            || candidates.Count == 0
            || !crosshatcher.TryGetLoadedBreed(
                out ChickenController.ChickenBreed loadedBreed))
        {
            return false;
        }

        if (IsChickenCapReached())
        {
            float nearestMatchDistance = float.PositiveInfinity;
            for (int index = 0; index < candidates.Count; index++)
            {
                CrosshatchCandidate candidate = candidates[index];
                if (candidate.Chicken.Breed != loadedBreed
                    || candidate.PointerDistance >= nearestMatchDistance)
                {
                    continue;
                }

                nearestMatchDistance = candidate.PointerDistance;
                selectedChicken = candidate.Chicken;
                selectedTarget = candidate.Target;
            }

            if (selectedChicken != null)
            {
                return true;
            }
        }

        bool canReplaceChicken = HasWorkingIncubator();
        float bestProfit = float.NegativeInfinity;
        float bestResultTier = -1f;
        float bestDistance = float.PositiveInfinity;

        for (int index = 0; index < candidates.Count; index++)
        {
            CrosshatchCandidate candidate = candidates[index];
            float profit = GetExpectedCrosshatchProfit(
                loadedBreed,
                candidate.Chicken.Breed,
                crosshatcher.ImprovementChance,
                canReplaceChicken);
            float resultTier = GetExpectedCrosshatchTier(
                loadedBreed,
                candidate.Chicken.Breed,
                crosshatcher.ImprovementChance);
            float distance = candidate.PointerDistance;

            if (profit < bestProfit
                || Mathf.Approximately(profit, bestProfit)
                    && resultTier < bestResultTier
                || Mathf.Approximately(profit, bestProfit)
                    && Mathf.Approximately(resultTier, bestResultTier)
                    && distance >= bestDistance)
            {
                continue;
            }

            bestProfit = profit;
            bestResultTier = resultTier;
            bestDistance = distance;
            selectedChicken = candidate.Chicken;
            selectedTarget = candidate.Target;
        }

        return selectedChicken != null;
    }

    private static bool TrySelectLowestMatchingCrosshatchPair(
        List<CrosshatchCandidate> candidates,
        out ChickenController selectedChicken,
        out ChickenPickupTarget selectedTarget)
    {
        selectedChicken = null;
        selectedTarget = null;
        int bestBreed = int.MaxValue;
        float bestDistance = float.PositiveInfinity;

        for (int firstIndex = 0;
            firstIndex < candidates.Count - 1;
            firstIndex++)
        {
            CrosshatchCandidate first = candidates[firstIndex];
            int breed = (int)first.Chicken.Breed;
            for (int secondIndex = firstIndex + 1;
                secondIndex < candidates.Count;
                secondIndex++)
            {
                CrosshatchCandidate second = candidates[secondIndex];
                if (second.Chicken.Breed != first.Chicken.Breed)
                {
                    continue;
                }

                float distance = first.PointerDistance
                    + second.PointerDistance;
                if (breed > bestBreed
                    || breed == bestBreed && distance >= bestDistance)
                {
                    continue;
                }

                int selectedIndex = SelectFirstParentIndex(
                    candidates,
                    firstIndex,
                    secondIndex);
                bestBreed = breed;
                bestDistance = distance;
                selectedChicken = candidates[selectedIndex].Chicken;
                selectedTarget = candidates[selectedIndex].Target;
            }
        }

        return selectedChicken != null;
    }

    private static int SelectFirstParentIndex(
        List<CrosshatchCandidate> candidates,
        int firstIndex,
        int secondIndex)
    {
        CrosshatchCandidate first = candidates[firstIndex];
        CrosshatchCandidate second = candidates[secondIndex];
        int firstBreed = (int)first.Chicken.Breed;
        int secondBreed = (int)second.Chicken.Breed;
        if (firstBreed != secondBreed)
        {
            // Keep the more valuable layer producing until the second pickup.
            return firstBreed < secondBreed
                ? firstIndex
                : secondIndex;
        }

        return first.PointerDistance
            <= second.PointerDistance
                ? firstIndex
                : secondIndex;
    }

    private static float GetExpectedCrosshatchProfit(
        ChickenController.ChickenBreed first,
        ChickenController.ChickenBreed second,
        float improvementChance,
        bool canReplaceChicken)
    {
        int maximumBreed =
            (int)ChickenController.ChickenBreed.Cosmic;
        int strongestBreed = Mathf.Max((int)first, (int)second);
        int improvedBreed = Mathf.Min(
            strongestBreed + 1,
            maximumBreed);
        float strongestValue = GetExpectedEggValue(
            (ChickenController.ChickenBreed)strongestBreed);
        float resultValue = first == second
            ? GetExpectedEggValue(
                (ChickenController.ChickenBreed)improvedBreed)
            : Mathf.Lerp(
                strongestValue,
                GetExpectedEggValue(
                    (ChickenController.ChickenBreed)improvedBreed),
                Mathf.Clamp01(improvementChance));
        float replacementValue = canReplaceChicken
            ? GetExpectedEggValue(
                ChickenController.ChickenBreed.White)
            : 0f;
        return resultValue
            + replacementValue
            - GetExpectedEggValue(first)
            - GetExpectedEggValue(second);
    }

    private static float GetExpectedCrosshatchTier(
        ChickenController.ChickenBreed first,
        ChickenController.ChickenBreed second,
        float improvementChance)
    {
        int maximumBreed =
            (int)ChickenController.ChickenBreed.Cosmic;
        int strongestBreed = Mathf.Max((int)first, (int)second);
        if (first == second)
        {
            return Mathf.Min(strongestBreed + 1, maximumBreed);
        }

        return strongestBreed
            + (strongestBreed < maximumBreed
                ? Mathf.Clamp01(improvementChance)
                : 0f);
    }

    private static float GetExpectedEggValue(
        ChickenController.ChickenBreed breed)
    {
        ProgressionSystem progression = ProgressionSystem.Instance;
        if (progression == null)
        {
            return 100f;
        }

        progression.GetCombinedRareChances(
            breed,
            out float rareChance,
            out float epicChance,
            out float legendaryChance,
            out float cosmicChance);
        float remainingChance = 1f;
        float cosmicProbability = TakeProbability(
            cosmicChance,
            ref remainingChance);
        float legendaryProbability = TakeProbability(
            legendaryChance,
            ref remainingChance);
        float epicProbability = TakeProbability(
            epicChance,
            ref remainingChance);
        float rareProbability = TakeProbability(
            rareChance,
            ref remainingChance);
        return remainingChance * GetExpectedWeightedEggValue(
                progression,
                ChickenEgg.EggType.Common)
            + rareProbability * GetExpectedWeightedEggValue(
                progression,
                ChickenEgg.EggType.Rare)
            + epicProbability * GetExpectedWeightedEggValue(
                progression,
                ChickenEgg.EggType.Epic)
            + legendaryProbability * GetExpectedWeightedEggValue(
                progression,
                ChickenEgg.EggType.Legendary)
            + cosmicProbability * GetExpectedWeightedEggValue(
                progression,
                ChickenEgg.EggType.Cosmic);
    }

    private static float GetExpectedWeightedEggValue(
        ProgressionSystem progression,
        ChickenEgg.EggType type)
    {
        float expectedWeightMultiplier;
        if (type == ChickenEgg.EggType.Common)
        {
            expectedWeightMultiplier = 1f
                + progression.EggWeightChance
                    * (progression.EggWeightUpperMultiplier - 1f)
                    * 0.5f;
        }
        else
        {
            expectedWeightMultiplier =
                progression.EggWeightUpperMultiplier
                + (int)type * 0.075f;
        }

        return progression.GetEggValueCents(type)
            * expectedWeightMultiplier;
    }

    private static float TakeProbability(
        float chance,
        ref float remainingChance)
    {
        float probability = Mathf.Min(
            remainingChance,
            Mathf.Clamp01(chance));
        remainingChance -= probability;
        return probability;
    }

    private static bool HasWorkingIncubator()
    {
        IncubatorController incubator = FindIncubator();
        return incubator != null && incubator.isActiveAndEnabled;
    }

    private bool TryGetChickenPickupScreenPoint(
        ChickenController chicken,
        ChickenPickupTarget pickupTarget,
        out Vector2 point)
    {
        point = default;
        Camera camera = GetGameplayCamera();

        if (camera == null
            || chicken == null
            || pickupTarget == null
            || !pickupTarget.CanPickUp)
        {
            return false;
        }

        Collider pickupCollider =
            pickupTarget.GetComponent<Collider>();
        Vector3 worldPoint = pickupCollider != null
            ? pickupCollider.bounds.center
            : pickupTarget.transform.position;
        Vector3 projected = camera.WorldToScreenPoint(worldPoint);

        if (projected.z <= 0f
            || projected.x < 2f
            || projected.y < 2f
            || projected.x > Screen.width - 2f
            || projected.y > Screen.height - 2f)
        {
            return false;
        }

        point = projected;
        return true;
    }

    private static bool RayHitsChickenPickup(
        Camera camera,
        Vector2 screenPoint,
        ChickenController chicken,
        ChickenPickupTarget pickupTarget)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            camera.ScreenPointToRay(screenPoint),
            100f,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int index = 0; index < hits.Length; index++)
        {
            ChickenPickupTarget hitTarget =
                hits[index].collider.GetComponent<ChickenPickupTarget>();

            if (hitTarget == pickupTarget
                && hitTarget.Chicken == chicken)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPointerOverChickenPickup(
        ChickenController chicken,
        ChickenPickupTarget pickupTarget)
    {
        Mouse mouse = automationInputMouse;
        Camera camera = GetGameplayCamera();
        return mouse != null
            && camera != null
            && chicken != null
            && pickupTarget != null
            && pickupTarget.CanPickUp
            && RayHitsChickenPickup(
                camera,
                mouse.position.ReadValue(),
                chicken,
                pickupTarget);
    }

    private IEnumerator MovePointerToChickenPickup(
        ChickenController chicken,
        ChickenPickupTarget pickupTarget)
    {
        float elapsed = 0f;

        while (elapsed < 1.5f
            && TryGetChickenPickupScreenPoint(
                chicken,
                pickupTarget,
                out Vector2 livePoint))
        {
            MovePointerSpring(livePoint);
            elapsed += Time.unscaledDeltaTime;
            yield return null;

            if (IsPointerOverChickenPickup(
                    chicken,
                    pickupTarget))
            {
                yield break;
            }
        }
    }

    private bool TryFindClickableEgg(
        bool preferIncubationEgg,
        out ChickenEgg selectedEgg,
        out Vector2 selectedPoint)
    {
        selectedEgg = null;
        selectedPoint = default;
        Camera camera = GetGameplayCamera();

        if (camera == null)
        {
            return false;
        }

        manualTargetCandidates.Clear();
        PenExpansionManager manager = PenExpansionManager.Instance;
        int focusedPenIndex = manager != null && manager.IsInitialized
            ? manager.FocusedPenIndex
            : -1;
        var eggs = ChickenEgg.ActiveInstances;

        for (int index = eggs.Count - 1; index >= 0; index--)
        {
            ChickenEgg egg = eggs[index];

            if (egg == null
                || egg.IsHeld
                || egg.IsCollected
                || (focusedPenIndex >= 0
                    && manager.GetClosestPenIndex(egg.transform.position)
                        != focusedPenIndex))
            {
                continue;
            }

            Collider eggCollider = egg.GetComponentInChildren<Collider>();
            Vector3 worldPoint = eggCollider != null
                ? eggCollider.bounds.center
                : egg.transform.position;
            Vector3 projected = camera.WorldToScreenPoint(worldPoint);

            if (projected.z <= 0f
                || projected.x < 12f
                || projected.y < 12f
                || projected.x > Screen.width - 12f
                || projected.y > Screen.height - 12f)
            {
                continue;
            }

            Vector2 point = projected;

            if (!RayHitsEgg(camera, point, egg))
            {
                continue;
            }

            manualTargetCandidates.Add(egg);
        }

        Vector2 pointerPosition = automationInputMouse != null
            ? automationInputMouse.position.ReadValue()
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (preferIncubationEgg)
        {
            int bestRarity = int.MaxValue;
            int bestValue = int.MaxValue;
            float bestTravel = float.PositiveInfinity;
            for (int index = 0; index < manualTargetCandidates.Count; index++)
            {
                ChickenEgg candidate = manualTargetCandidates[index];
                if (!TryGetEggScreenPoint(candidate, out Vector2 point))
                {
                    continue;
                }

                int rarity = (int)candidate.Type;
                int saleValue = EggContainer.CalculateSaleValueCents(
                    candidate.ValueCents,
                    candidate.WeightKilograms);
                float travel = GetNormalizedCursorTravel(
                    point,
                    pointerPosition);
                if (rarity > bestRarity
                    || (rarity == bestRarity && saleValue > bestValue)
                    || (rarity == bestRarity
                        && saleValue == bestValue
                        && travel >= bestTravel))
                {
                    continue;
                }

                selectedEgg = candidate;
                selectedPoint = point;
                bestRarity = rarity;
                bestValue = saleValue;
                bestTravel = travel;
            }

            return selectedEgg != null;
        }

        float clusterRadiusSquared =
            BasketClusterSearchRadius * BasketClusterSearchRadius;
        float bestUtility = float.NegativeInfinity;
        float bestTravelCost = float.PositiveInfinity;
        for (int candidateIndex = 0;
             candidateIndex < manualTargetCandidates.Count;
             candidateIndex++)
        {
            ChickenEgg candidate = manualTargetCandidates[candidateIndex];
            if (!TryGetEggScreenPoint(candidate, out Vector2 candidatePoint))
            {
                continue;
            }

            Vector3 candidatePosition = candidate.transform.position;
            int clusterCount = 0;
            float density = 0f;
            for (int neighbourIndex = 0;
                 neighbourIndex < manualTargetCandidates.Count;
                 neighbourIndex++)
            {
                Vector3 offset = manualTargetCandidates[neighbourIndex]
                    .transform.position - candidatePosition;
                offset.y = 0f;
                float distanceSquared = offset.sqrMagnitude;
                if (distanceSquared > clusterRadiusSquared)
                {
                    continue;
                }

                clusterCount++;
                density += 1f - Mathf.Sqrt(distanceSquared)
                    / BasketClusterSearchRadius;
            }

            int saleValue = EggContainer.CalculateSaleValueCents(
                candidate.ValueCents,
                candidate.WeightKilograms);
            float travelCost = GetNormalizedCursorTravel(
                candidatePoint,
                pointerPosition);
            float utility = clusterCount * 1.25f
                + density * 0.35f
                + (int)candidate.Type * 0.12f
                + Mathf.Log10(Mathf.Max(1f, saleValue)) * 0.08f
                - travelCost * 2.25f;
            if (utility < bestUtility
                || (Mathf.Approximately(utility, bestUtility)
                    && travelCost >= bestTravelCost))
            {
                continue;
            }

            selectedEgg = candidate;
            selectedPoint = candidatePoint;
            bestUtility = utility;
            bestTravelCost = travelCost;
        }

        return selectedEgg != null;
    }

    private bool TryFindBasketClusterEgg(
        out ChickenEgg selectedEgg,
        out Vector2 selectedPoint)
    {
        selectedEgg = null;
        selectedPoint = default;
        Camera camera = GetGameplayCamera();
        EggCarryController collection = EggCarryController.Instance;
        if (camera == null
            || collection == null
            || collection.BasketUpgradeLevel <= 0)
        {
            return false;
        }

        basketTargetCandidates.Clear();
        PenExpansionManager manager = PenExpansionManager.Instance;
        int focusedPenIndex = manager != null && manager.IsInitialized
            ? manager.FocusedPenIndex
            : -1;
        var eggs = ChickenEgg.ActiveInstances;
        for (int index = eggs.Count - 1; index >= 0; index--)
        {
            ChickenEgg egg = eggs[index];
            if (egg == null
                || egg.IsHeld
                || egg.IsCollected
                || (focusedPenIndex >= 0
                    && manager.GetClosestPenIndex(egg.transform.position)
                        != focusedPenIndex)
                || !TryGetEggScreenPoint(egg, out Vector2 point)
                || !RayHitsEgg(camera, point, egg))
            {
                continue;
            }

            basketTargetCandidates.Add(egg);
        }

        float clusterRadius = Mathf.Max(
            BasketClusterSearchRadius,
            collection.BasketReachRadius);
        float clusterRadiusSquared = clusterRadius * clusterRadius;
        float bestUtility = float.NegativeInfinity;
        float bestPointerTravel = float.PositiveInfinity;
        Vector2 pointerPosition = automationInputMouse != null
            ? automationInputMouse.position.ReadValue()
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        int availableBasketSlots = Mathf.Max(
            1,
            collection.CurrentBasketCapacity - collection.BasketEggCount);

        for (int candidateIndex = 0;
             candidateIndex < basketTargetCandidates.Count;
             candidateIndex++)
        {
            ChickenEgg candidate = basketTargetCandidates[candidateIndex];
            Vector3 candidatePosition = candidate.transform.position;
            int clusterCount = 0;
            float density = 0f;
            long clusterValue = 0;
            for (int neighbourIndex = 0;
                 neighbourIndex < basketTargetCandidates.Count;
                 neighbourIndex++)
            {
                ChickenEgg neighbour = basketTargetCandidates[neighbourIndex];
                Vector3 offset = neighbour.transform.position
                    - candidatePosition;
                offset.y = 0f;
                float distanceSquared = offset.sqrMagnitude;
                if (distanceSquared > clusterRadiusSquared)
                {
                    continue;
                }

                clusterCount++;
                density += 1f - Mathf.Sqrt(distanceSquared) / clusterRadius;
                clusterValue += EggContainer.CalculateSaleValueCents(
                    neighbour.ValueCents,
                    neighbour.WeightKilograms);
            }

            TryGetEggScreenPoint(candidate, out Vector2 candidatePoint);
            int rarity = (int)candidate.Type;
            int collectibleCount = Mathf.Min(
                clusterCount,
                availableBasketSlots);
            float pointerTravel = GetNormalizedCursorTravel(
                candidatePoint,
                pointerPosition);
            float utility = collectibleCount * 2f
                + density * 0.35f
                + rarity * 0.08f
                + Mathf.Log10(Mathf.Max(1f, clusterValue)) * 0.05f
                - pointerTravel * 2.5f;
            if (utility < bestUtility
                || (Mathf.Approximately(utility, bestUtility)
                    && pointerTravel >= bestPointerTravel))
            {
                continue;
            }

            selectedEgg = candidate;
            selectedPoint = candidatePoint;
            bestUtility = utility;
            bestPointerTravel = pointerTravel;
        }

        return selectedEgg != null;
    }

    private static float GetNormalizedCursorTravel(
        Vector2 target,
        Vector2 pointerPosition)
    {
        float screenDiagonal = Mathf.Sqrt(
            Screen.width * (float)Screen.width
                + Screen.height * (float)Screen.height);
        return Vector2.Distance(target, pointerPosition)
            / Mathf.Max(1f, screenDiagonal);
    }

    private bool TryFindVacuumClusterTarget(
        out ChickenEgg selectedEgg,
        out Vector2 selectedPoint)
    {
        selectedEgg = null;
        selectedPoint = default;
        Camera camera = GetGameplayCamera();
        EggCarryController collection = EggCarryController.Instance;
        if (camera == null || collection == null || !collection.HasVacuum)
        {
            return false;
        }

        vacuumTargetCandidates.Clear();
        PenExpansionManager manager = PenExpansionManager.Instance;
        int focusedPenIndex = manager != null && manager.IsInitialized
            ? manager.FocusedPenIndex
            : -1;
        var eggs = ChickenEgg.ActiveInstances;
        for (int index = eggs.Count - 1; index >= 0; index--)
        {
            ChickenEgg egg = eggs[index];
            if (egg == null
                || egg.IsHeld
                || egg.IsCollected
                || (focusedPenIndex >= 0
                    && manager.GetClosestPenIndex(egg.transform.position)
                        != focusedPenIndex)
                || !TryGetEggScreenPoint(egg, out Vector2 point)
                || !RayHitsEgg(camera, point, egg))
            {
                continue;
            }

            vacuumTargetCandidates.Add(egg);
        }

        float clusterRadius = Mathf.Max(
            0.25f,
            collection.CurrentVacuumRange * 0.65f);
        float clusterRadiusSquared = clusterRadius * clusterRadius;
        int bestClusterCount = -1;
        float bestDensity = float.NegativeInfinity;
        long bestClusterValue = -1;
        float bestScreenDistance = float.PositiveInfinity;
        Vector2 screenCenter = new Vector2(
            Screen.width * 0.5f,
            Screen.height * 0.5f);

        for (int candidateIndex = 0;
            candidateIndex < vacuumTargetCandidates.Count;
            candidateIndex++)
        {
            ChickenEgg candidate = vacuumTargetCandidates[candidateIndex];
            Vector3 candidatePosition = candidate.transform.position;
            int clusterCount = 0;
            float density = 0f;
            long clusterValue = 0;
            for (int neighbourIndex = 0;
                neighbourIndex < vacuumTargetCandidates.Count;
                neighbourIndex++)
            {
                ChickenEgg neighbour = vacuumTargetCandidates[neighbourIndex];
                Vector3 offset = neighbour.transform.position
                    - candidatePosition;
                offset.y = 0f;
                float distanceSquared = offset.sqrMagnitude;
                if (distanceSquared > clusterRadiusSquared)
                {
                    continue;
                }

                clusterCount++;
                density += 1f - Mathf.Sqrt(distanceSquared) / clusterRadius;
                clusterValue += EggContainer.CalculateSaleValueCents(
                    neighbour.ValueCents,
                    neighbour.WeightKilograms);
            }

            TryGetEggScreenPoint(candidate, out Vector2 candidatePoint);
            float screenDistance = Vector2.SqrMagnitude(
                candidatePoint - screenCenter);
            if (clusterCount < bestClusterCount
                || (clusterCount == bestClusterCount
                    && density < bestDensity)
                || (clusterCount == bestClusterCount
                    && Mathf.Approximately(density, bestDensity)
                    && clusterValue < bestClusterValue)
                || (clusterCount == bestClusterCount
                    && Mathf.Approximately(density, bestDensity)
                    && clusterValue == bestClusterValue
                    && screenDistance >= bestScreenDistance))
            {
                continue;
            }

            selectedEgg = candidate;
            selectedPoint = candidatePoint;
            bestClusterCount = clusterCount;
            bestDensity = density;
            bestClusterValue = clusterValue;
            bestScreenDistance = screenDistance;
        }

        return selectedEgg != null;
    }

    private static bool RayHitsEgg(Camera camera, Vector2 screenPoint, ChickenEgg egg)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            camera.ScreenPointToRay(screenPoint),
            100f,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int index = 0; index < hits.Length; index++)
        {
            if (hits[index].collider.GetComponentInParent<ChickenEgg>() == egg)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetEggScreenPoint(ChickenEgg egg, out Vector2 point)
    {
        point = default;
        Camera camera = GetGameplayCamera();
        if (camera == null || egg == null || egg.IsHeld || egg.IsCollected)
        {
            return false;
        }

        Collider eggCollider = egg.GetComponentInChildren<Collider>();
        Vector3 worldPoint = eggCollider != null
            ? eggCollider.bounds.center
            : egg.transform.position;
        Vector3 projected = camera.WorldToScreenPoint(worldPoint);
        if (projected.z <= 0f
            || projected.x < 2f
            || projected.y < 2f
            || projected.x > Screen.width - 2f
            || projected.y > Screen.height - 2f)
        {
            return false;
        }

        point = projected;
        return true;
    }

    private bool IsPointerOverEgg(ChickenEgg egg)
    {
        Mouse mouse = automationInputMouse;
        Camera camera = GetGameplayCamera();
        return mouse != null
            && camera != null
            && egg != null
            && !egg.IsHeld
            && !egg.IsCollected
            && RayHitsEgg(camera, mouse.position.ReadValue(), egg);
    }

    private IEnumerator MovePointerToEgg(ChickenEgg egg)
    {
        float elapsed = 0f;
        while (elapsed < 1.5f
            && TryGetEggScreenPoint(egg, out Vector2 livePoint))
        {
            MovePointerSpring(livePoint);
            elapsed += Time.unscaledDeltaTime;
            yield return null;

            if (IsPointerOverEgg(egg))
            {
                yield break;
            }
        }
    }

    private IEnumerator ClickMovingEgg(ChickenEgg egg)
    {
        yield return MovePointerToEgg(egg);
        float dwell = 0f;
        while (dwell < pointerDwellTime
            && TryGetEggScreenPoint(egg, out Vector2 livePoint))
        {
            MovePointerSpring(livePoint);
            dwell += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!IsPointerOverEgg(egg))
        {
            yield return new WaitForSecondsRealtime(0.05f);
            yield break;
        }

        QueueMouseButton(MouseButton.Left, true);
        yield return null;
        QueueMouseButton(MouseButton.Left, false);
        completedActions++;
        collectionActionCount++;
        yield return new WaitForSecondsRealtime(actionPause);
    }

    private IEnumerator ClickWorldComponent(Component component)
    {
        if (component == null
            || !TryGetWorldScreenPoint(component, out Vector2 screenPoint))
        {
            yield return new WaitForSecondsRealtime(0.12f);
            yield break;
        }

        yield return ClickScreen(screenPoint);
    }

    private bool TryGetWorldScreenPoint(Component component, out Vector2 point)
    {
        point = default;
        Camera camera = GetGameplayCamera();

        if (camera == null || component == null)
        {
            return false;
        }

        Collider[] colliders = component.GetComponentsInChildren<Collider>();
        Vector3 worldPoint = component.transform.position;

        if (colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;

            for (int index = 1; index < colliders.Length; index++)
            {
                bounds.Encapsulate(colliders[index].bounds);
            }

            worldPoint = bounds.center;
        }

        Vector3 projected = camera.WorldToScreenPoint(worldPoint);

        if (projected.z <= 0f)
        {
            return false;
        }

        point = projected;
        return true;
    }

    private bool TryGetHandDropPoint(
        Component destination,
        float carryHeight,
        out Vector2 screenPoint)
    {
        Vector3 dropPosition = destination switch
        {
            EggContainer container => container.DepositPosition,
            IncubatorController incubator => incubator.DepositPosition,
            _ => destination != null ? destination.transform.position : Vector3.zero
        };
        dropPosition.y = carryHeight;
        screenPoint = default;
        Camera camera = GetGameplayCamera();

        if (camera == null || destination == null)
        {
            return false;
        }

        Vector3 projected = camera.WorldToScreenPoint(dropPosition);

        if (projected.z <= 0f)
        {
            return false;
        }

        screenPoint = projected;
        return true;
    }

    private Camera GetGameplayCamera()
    {
        if (gameplayCamera == null)
        {
            gameplayCamera = Camera.main;
        }

        return gameplayCamera;
    }

    private IEnumerator EnsureShopTabVisible(
        ProgressionNodeButton node)
    {
        if (node == null
            || !TryGetShopTabNames(
                node.UpgradeId,
                out string tabName,
                out string groupName))
        {
            yield break;
        }

        Transform group = node.transform;
        while (group != null && group.name != groupName)
        {
            group = group.parent;
        }

        if (group != null && group.gameObject.activeInHierarchy)
        {
            yield break;
        }

        Button tab = FindNamedButton(tabName);
        if (!IsUsable(tab))
        {
            SetStatus(
                $"SHOP  .  WAITING FOR {tabName.Replace(" Branch", string.Empty)} TAB");
            yield return new WaitForSecondsRealtime(0.15f);
            yield break;
        }

        yield return ClickButton(
            tab,
            $"SHOP  .  OPENING {tabName.Replace(" Branch", string.Empty)} TAB");
        yield return null;
    }

    private static bool TryGetShopTabNames(
        ProgressionSystem.UpgradeId id,
        out string tabName,
        out string groupName)
    {
        switch (id)
        {
            case ProgressionSystem.UpgradeId.FeedSpeed:
            case ProgressionSystem.UpgradeId.PrimeFeed:
            case ProgressionSystem.UpgradeId.RareEggChance:
            case ProgressionSystem.UpgradeId.ChickenPerks:
            case ProgressionSystem.UpgradeId.EggWeight:
            case ProgressionSystem.UpgradeId.EggValue:
                tabName = "FOOD Branch";
                groupName = "Food Tree Group";
                return true;

            case ProgressionSystem.UpgradeId.IncubatorInstall:
            case ProgressionSystem.UpgradeId.IncubatorCapacity:
            case ProgressionSystem.UpgradeId.IncubatorSpeed:
            case ProgressionSystem.UpgradeId.CrosshatcherInstall:
            case ProgressionSystem.UpgradeId.CrosshatcherSpeed:
            case ProgressionSystem.UpgradeId.CrosshatcherQuality:
            case ProgressionSystem.UpgradeId.IncubatorTurboPower:
            case ProgressionSystem.UpgradeId.IncubatorTurboDuration:
            case ProgressionSystem.UpgradeId.CrosshatcherTurboPower:
            case ProgressionSystem.UpgradeId.CrosshatcherTurboDuration:
            case ProgressionSystem.UpgradeId.RobotTurboPower:
            case ProgressionSystem.UpgradeId.RobotTurboDuration:
                tabName = "TECH Branch";
                groupName = "Tech Tree Group";
                return true;

            case ProgressionSystem.UpgradeId.BasketCapacity:
            case ProgressionSystem.UpgradeId.BasketReach:
            case ProgressionSystem.UpgradeId.VacuumUnlock:
            case ProgressionSystem.UpgradeId.VacuumPower:
            case ProgressionSystem.UpgradeId.VacuumRange:
            case ProgressionSystem.UpgradeId.TruckBonus:
            case ProgressionSystem.UpgradeId.RobotUnlock:
            case ProgressionSystem.UpgradeId.RobotSpeed:
            case ProgressionSystem.UpgradeId.RobotCapacity:
            case ProgressionSystem.UpgradeId.RobotSmartness:
                tabName = "COLLECTION Branch";
                groupName = "Collection Tree Group";
                return true;

            default:
                tabName = null;
                groupName = null;
                return false;
        }
    }

    private IEnumerator ClickNamedButton(string objectName, string activity)
    {
        Button button = FindNamedButton(objectName);

        if (!IsUsable(button))
        {
            SetStatus($"{activity}  .  WAITING");
            yield return new WaitForSecondsRealtime(0.15f);
            yield break;
        }

        yield return ClickButton(button, activity);
    }

    private IEnumerator ClickButton(Button button, string activity)
    {
        ProgressionNodeButton progressionNode =
            button != null ? button.GetComponent<ProgressionNodeButton>() : null;
        ProgressionTreePreview preview = progressionNode != null
            ? button.GetComponentInParent<ProgressionTreePreview>(true)
            : null;
        if (preview != null && preview.IsOpen)
        {
            preview.Hide();
            yield return null;
        }

        yield return EnsureButtonVisible(button);
        RectTransform rect = button.transform as RectTransform;

        if (rect == null)
        {
            yield break;
        }

        Canvas canvas = button.GetComponentInParent<Canvas>();
        Camera uiCamera = canvas != null
            && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(
            uiCamera,
            rect.TransformPoint(rect.rect.center));
        SetStatus(activity);
        yield return ClickUiButton(button, point);

        // The preview panel can overlap the lowest tree nodes. If UI layout or
        // scaling still intercepted the physical click, select the intended node
        // directly so automation never buys the wrong tier.
        if (progressionNode != null
            && preview != null
            && !preview.IsSelected(progressionNode))
        {
            preview.Select(progressionNode);
            yield return null;
        }
    }

    private IEnumerator ClickUiButton(Button button, Vector2 point)
    {
        yield return MovePointer(point);
        yield return new WaitForSecondsRealtime(pointerDwellTime);

        if (!IsUsable(button))
        {
            yield break;
        }

        ReleaseMouseButtons();
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
        {
            var pointerEvent = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                position = point,
                pointerId = -1
            };
            ExecuteEvents.Execute(
                button.gameObject,
                pointerEvent,
                ExecuteEvents.pointerDownHandler);
            yield return null;
            ExecuteEvents.Execute(
                button.gameObject,
                pointerEvent,
                ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(
                button.gameObject,
                pointerEvent,
                ExecuteEvents.pointerClickHandler);
        }
        else
        {
            // Authored UI should always have an EventSystem, but invoking the
            // button is safer than leaving automation permanently stalled.
            button.onClick.Invoke();
        }

        completedActions++;
        yield return new WaitForSecondsRealtime(actionPause);
    }

    private IEnumerator EnsureButtonVisible(Button button)
    {
        ScrollRect scrollRect = button != null
            ? button.GetComponentInParent<ScrollRect>()
            : null;
        RectTransform buttonRect = button != null
            ? button.transform as RectTransform
            : null;
        RectTransform viewport = scrollRect != null
            ? scrollRect.viewport
            : null;
        if (scrollRect == null || buttonRect == null || viewport == null)
        {
            yield break;
        }

        ProgressionTreePanController panController =
            scrollRect.GetComponent<ProgressionTreePanController>();
        if (panController != null && panController.Reveal(buttonRect))
        {
            // Let the mask and button geometry settle before calculating the
            // click position. This avoids wheel input fighting the tree's
            // custom meaningful-content bounds and visibly flickering.
            yield return null;
            yield break;
        }

        Canvas canvas = button.GetComponentInParent<Canvas>();
        Camera uiCamera = canvas != null
            && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
        Vector2 viewportCenter = RectTransformUtility.WorldToScreenPoint(
            uiCamera,
            viewport.TransformPoint(viewport.rect.center));
        yield return MovePointer(viewportCenter);

        for (int attempt = 0; attempt < 48; attempt++)
        {
            Vector2 buttonPoint = RectTransformUtility.WorldToScreenPoint(
                uiCamera,
                buttonRect.TransformPoint(buttonRect.rect.center));
            Vector2 viewportBottom = RectTransformUtility.WorldToScreenPoint(
                uiCamera,
                viewport.TransformPoint(new Vector2(0f, viewport.rect.yMin + 36f)));
            Vector2 viewportTop = RectTransformUtility.WorldToScreenPoint(
                uiCamera,
                viewport.TransformPoint(new Vector2(0f, viewport.rect.yMax - 36f)));

            if (buttonPoint.y >= viewportBottom.y && buttonPoint.y <= viewportTop.y)
            {
                yield break;
            }

            float direction = buttonPoint.y > viewportTop.y ? 1f : -1f;
            QueueMouseScroll(direction * 120f);
            yield return null;
            yield return new WaitForSecondsRealtime(0.025f);
        }
    }

    private IEnumerator ClickScreen(Vector2 point)
    {
        yield return MovePointer(point);
        yield return new WaitForSecondsRealtime(pointerDwellTime);
        QueueMouseButton(MouseButton.Left, true);
        yield return null;
        QueueMouseButton(MouseButton.Left, false);
        completedActions++;
        yield return new WaitForSecondsRealtime(actionPause);
    }

    private IEnumerator MovePointer(
        Vector2 destination,
        MouseButton? heldButton = null)
    {
        Mouse mouse = automationInputMouse;

        if (mouse == null)
        {
            yield break;
        }

        destination.x = Mathf.Clamp(destination.x, 1f, Screen.width - 1f);
        destination.y = Mathf.Clamp(destination.y, 1f, Screen.height - 1f);
        Vector2 start = mouse.position.ReadValue();
        Vector2 travel = destination - start;
        float initialDistance = travel.magnitude;
        if (initialDistance <= 0.01f)
        {
            pointerVelocity = Vector2.zero;
            SetPointerPosition(destination, heldButton);
            yield return null;
            yield break;
        }

        Vector2 travelDirection = initialDistance > 0.001f
            ? travel / initialDistance
            : Vector2.zero;
        float resolutionScale = GetPointerResolutionScale();
        float maximumSpeed = pointerSpeed * resolutionScale;
        float maximumAcceleration = pointerAcceleration * resolutionScale;

        // A minimum-jerk movement has a bell-shaped velocity curve. Its peak
        // normalized speed is 1.875 and peak normalized acceleration is about
        // 5.774, so these limits produce a fast motion that still eases at both
        // ends instead of behaving like a motorized spring.
        const float MinimumJerkPeakSpeed = 1.875f;
        const float MinimumJerkPeakAcceleration = 5.7735f;
        float speedLimitedDuration = MinimumJerkPeakSpeed
            * initialDistance
            / Mathf.Max(100f, maximumSpeed);
        float accelerationLimitedDuration = Mathf.Sqrt(
            MinimumJerkPeakAcceleration
            * initialDistance
            / Mathf.Max(100f, maximumAcceleration));
        float movementDuration = Mathf.Max(
            0.075f,
            speedLimitedDuration,
            accelerationLimitedDuration);
        float plannedPeakSpeed = MinimumJerkPeakSpeed
            * initialDistance
            / movementDuration;
        float fastMovementBlend = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(
                maximumSpeed * pointerOvershootSpeedRatio,
                maximumSpeed,
                plannedPeakSpeed));
        float overshootDistance = pointerMaximumOvershoot
            * resolutionScale
            * fastMovementBlend;
        Vector2 movementDestination = destination
            + travelDirection * overshootDistance;
        movementDestination.x = Mathf.Clamp(
            movementDestination.x,
            1f,
            Screen.width - 1f);
        movementDestination.y = Mathf.Clamp(
            movementDestination.y,
            1f,
            Screen.height - 1f);
        Vector2 movementTravel = movementDestination - start;

        float elapsed = 0f;
        while (elapsed < movementDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalizedTime = Mathf.Clamp01(
                elapsed / movementDuration);
            float positionBlend = MinimumJerk(normalizedTime);
            float velocityBlend = MinimumJerkDerivative(normalizedTime);
            SetPointerPosition(
                start + movementTravel * positionBlend,
                heldButton);
            pointerVelocity = movementTravel
                * (velocityBlend / movementDuration);
            yield return null;
        }

        SetPointerPosition(movementDestination, heldButton);
        pointerVelocity = Vector2.zero;

        float actualOvershoot = Vector2.Distance(
            movementDestination,
            destination);
        if (actualOvershoot > 0.01f)
        {
            // The corrective submovement is short and explicitly bounded, so
            // momentum can never turn a 1-2 pixel miss into a large rebound.
            Vector2 correctionStart = movementDestination;
            Vector2 correctionTravel = destination - correctionStart;
            float correctionDuration = Mathf.Lerp(
                0.045f,
                0.07f,
                fastMovementBlend);
            elapsed = 0f;
            while (elapsed < correctionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalizedTime = Mathf.Clamp01(
                    elapsed / correctionDuration);
                float positionBlend = MinimumJerk(normalizedTime);
                float velocityBlend = MinimumJerkDerivative(normalizedTime);
                SetPointerPosition(
                    correctionStart + correctionTravel * positionBlend,
                    heldButton);
                pointerVelocity = correctionTravel
                    * (velocityBlend / correctionDuration);
                yield return null;
            }
        }

        SetPointerPosition(destination, heldButton);
        pointerVelocity = Vector2.zero;
        yield return null;
    }

    private static float MinimumJerk(float normalizedTime)
    {
        float t = Mathf.Clamp01(normalizedTime);
        float t2 = t * t;
        float t3 = t2 * t;
        return t3 * (10f + t * (-15f + 6f * t));
    }

    private static float MinimumJerkDerivative(float normalizedTime)
    {
        float t = Mathf.Clamp01(normalizedTime);
        float oneMinusT = 1f - t;
        return 30f * t * t * oneMinusT * oneMinusT;
    }

    private void MovePointerSpring(
        Vector2 destination,
        MouseButton? heldButton = null)
    {
        Mouse mouse = automationInputMouse;
        if (mouse == null || !mouse.added)
        {
            return;
        }

        destination.x = Mathf.Clamp(destination.x, 1f, Screen.width - 1f);
        destination.y = Mathf.Clamp(destination.y, 1f, Screen.height - 1f);
        Vector2 current = mouse.position.ReadValue();
        Vector2 error = destination - current;
        float resolutionScale = GetPointerResolutionScale();
        float distance = error.magnitude;
        float distanceBlend = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.Clamp01(
                distance
                / Mathf.Max(1f, pointerPrecisionRadius * resolutionScale)));
        float remainingTime = Mathf.Clamp(
            Time.unscaledDeltaTime,
            0.001f,
            0.05f);
        float adaptiveFrequency = pointerSpringFrequency
            * Mathf.Lerp(1.18f, 1f, distanceBlend);
        float adaptiveDamping = Mathf.Lerp(
            pointerPrecisionDamping,
            pointerSpringDamping,
            distanceBlend);
        float angularFrequency = adaptiveFrequency * Mathf.PI * 2f;
        float stiffness = angularFrequency * angularFrequency;
        float damping = 2f * adaptiveDamping * angularFrequency;
        float maximumSpeed = pointerSpeed * resolutionScale;
        float maximumAcceleration = pointerAcceleration * resolutionScale;

        while (remainingTime > 0f)
        {
            float step = Mathf.Min(remainingTime, 1f / 120f);
            Vector2 acceleration =
                (destination - current) * stiffness
                - pointerVelocity * damping;
            acceleration = Vector2.ClampMagnitude(
                acceleration,
                maximumAcceleration);
            pointerVelocity += acceleration * step;
            pointerVelocity = Vector2.ClampMagnitude(
                pointerVelocity,
                maximumSpeed);

            float currentDistance = Vector2.Distance(current, destination);
            float brakingSpeed = Mathf.Sqrt(
                2f * maximumAcceleration * Mathf.Max(2f, currentDistance));
            float easedSpeedLimit = Mathf.Min(maximumSpeed, brakingSpeed);
            if (pointerVelocity.magnitude > easedSpeedLimit)
            {
                Vector2 easedVelocity = Vector2.ClampMagnitude(
                    pointerVelocity,
                    easedSpeedLimit);
                pointerVelocity = Vector2.MoveTowards(
                    pointerVelocity,
                    easedVelocity,
                    maximumAcceleration * 0.65f * step);
            }

            current += pointerVelocity * step;
            remainingTime -= step;
        }

        SetPointerPosition(current, heldButton);
    }

    private static float GetPointerResolutionScale()
    {
        float horizontalScale = Screen.width / 1920f;
        float verticalScale = Screen.height / 1080f;
        return Mathf.Clamp(
            Mathf.Sqrt(Mathf.Max(horizontalScale, verticalScale)),
            0.8f,
            1.5f);
    }

    private void SetPointerPosition(
        Vector2 position,
        MouseButton? heldButton = null)
    {
        if (physicalMouse != null && physicalMouse.added)
        {
            physicalMouse.WarpCursorPosition(position);
        }

        Mouse mouse = automationInputMouse;

        if (mouse == null || !mouse.added)
        {
            return;
        }

        mouse.CopyState(out MouseState state);
        state.delta = position - state.position;
        state.position = position;
        state.scroll = Vector2.zero;
        if (heldButton.HasValue)
        {
            state.WithButton(heldButton.Value, true);
        }

        InputSystem.QueueStateEvent(mouse, state);
    }

    private static Button FindNamedButton(string objectName)
    {
        Button[] buttons = Object.FindObjectsByType<Button>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        Button inactiveMatch = null;
        for (int index = 0; index < buttons.Length; index++)
        {
            Button button = buttons[index];

            if (button != null
                && button.name == objectName
                && button.gameObject.scene.IsValid())
            {
                if (button.gameObject.activeInHierarchy)
                {
                    return button;
                }

                inactiveMatch ??= button;
            }
        }

        return inactiveMatch;
    }

    private static RectTransform FindNamedRectTransform(string objectName)
    {
        RectTransform[] rects = Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int index = 0; index < rects.Length; index++)
        {
            RectTransform rect = rects[index];
            if (rect != null
                && rect.name == objectName
                && rect.gameObject.scene.IsValid())
            {
                return rect;
            }
        }

        return null;
    }

    private static bool IsUsable(Button button)
    {
        return button != null
            && button.isActiveAndEnabled
            && button.gameObject.activeInHierarchy
            && button.interactable;
    }

    private static bool CanPurchaseProgressionNode(Button button)
    {
        if (!IsUsable(button))
        {
            return false;
        }

        ProgressionNodeButton node = button.GetComponent<ProgressionNodeButton>();
        return node == null || CanPurchaseProgressionNode(node);
    }

    private static bool CanPurchaseProgressionNode(
        ProgressionNodeButton node)
    {
        if (node == null)
        {
            return false;
        }

        if (ProgressionSystem.Instance == null)
        {
            return true;
        }

        ProgressionSystem.NodeState state = node.GetNodeState();
        return state.Visible
            && state.PrerequisiteMet
            && !state.IsMaxed
            && state.Cost <= EggScoreHud.CurrentCents;
    }

    private static void QueueMouseButton(MouseButton button, bool pressed)
    {
        Mouse mouse = automationInputMouse;

        if (mouse == null || !mouse.added)
        {
            return;
        }

        mouse.CopyState(out MouseState state);
        state.delta = Vector2.zero;
        state.scroll = Vector2.zero;
        state.WithButton(button, pressed);
        InputSystem.QueueStateEvent(mouse, state);
    }

    private static void ForceMouseButton(MouseButton button, bool pressed)
    {
        Mouse mouse = automationInputMouse;
        if (mouse == null || !mouse.added)
        {
            return;
        }

        mouse.CopyState(out MouseState state);
        state.delta = Vector2.zero;
        state.scroll = Vector2.zero;
        state.WithButton(button, pressed);
        InputState.Change(mouse, state);
    }

    private static void QueueMouseScroll(float verticalDelta)
    {
        Mouse mouse = automationInputMouse;
        if (mouse == null || !mouse.added)
        {
            return;
        }

        mouse.CopyState(out MouseState state);
        state.delta = Vector2.zero;
        state.scroll = new Vector2(0f, verticalDelta);
        InputSystem.QueueStateEvent(mouse, state);
    }

    private static void ReleaseMouseButtons()
    {
        Mouse mouse = automationInputMouse;

        if (mouse == null || !mouse.added)
        {
            return;
        }

        mouse.CopyState(out MouseState state);
        state.delta = Vector2.zero;
        state.scroll = Vector2.zero;
        state.WithButton(MouseButton.Left, false);
        state.WithButton(MouseButton.Right, false);
        InputState.Change(mouse, state);
    }

    private void SetStatus(string activity)
    {
        if (statusText == null)
        {
            return;
        }

        Canvas statusCanvas = statusText.GetComponentInParent<Canvas>();
        bool shouldShow = IsRunning || activity.StartsWith("NO ");

        if (statusCanvas != null
            && statusCanvas.gameObject.activeSelf != shouldShow)
        {
            statusCanvas.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        statusText.text = IsRunning
            ? $"TEST BOT  .  F8 STOP  .  ACTIONS {completedActions}\n{activity}"
            : $"TEST BOT\n{activity}";
    }

    private void OnValidate()
    {
        pointerSpeed = Mathf.Max(100f, pointerSpeed);
        pointerAcceleration = Mathf.Max(100f, pointerAcceleration);
        pointerSpringFrequency = Mathf.Clamp(pointerSpringFrequency, 2f, 12f);
        pointerSpringDamping = Mathf.Clamp(pointerSpringDamping, 0.35f, 0.95f);
        pointerPrecisionDamping = Mathf.Clamp(pointerPrecisionDamping, 0.9f, 1.4f);
        pointerPrecisionRadius = Mathf.Max(10f, pointerPrecisionRadius);
        pointerMaximumOvershoot = Mathf.Clamp(pointerMaximumOvershoot, 0f, 30f);
        pointerOvershootSpeedRatio = Mathf.Clamp(pointerOvershootSpeedRatio, 0.5f, 0.95f);
        pointerDwellTime = Mathf.Max(0f, pointerDwellTime);
        actionPause = Mathf.Max(0.05f, actionPause);
        vacuumHoldTime = Mathf.Max(0.5f, vacuumHoldTime);
        minimumFeedBags = Mathf.Max(0, minimumFeedBags);
        maximumShopPurchasesPerVisit = Mathf.Max(1, maximumShopPurchasesPerVisit);
        automatedOwnedPenTarget = Mathf.Max(1, automatedOwnedPenTarget);
        penNavigationInterval = Mathf.Max(1f, penNavigationInterval);
    }
}
