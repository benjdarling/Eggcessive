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

    private static Mouse automationInputMouse;

    private static readonly PenExpansionManager.EquipmentType[]
        PenEquipmentInvestmentOrder =
    {
        PenExpansionManager.EquipmentType.Incubator,
        PenExpansionManager.EquipmentType.Crosshatcher,
        PenExpansionManager.EquipmentType.Robot,
        PenExpansionManager.EquipmentType.AutoFeeder
    };

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
    private const float EfficientCollectionRatio = 0.72f;
    private const int MaximumEfficientRoundLeftovers = 2;
    private const int MaximumDesiredFeedPiles = 16;
    private const int ChickensPerDesiredFoodPile = 10;
    private const int ChickensPerVacuumFeedPile = 8;
    private const int ManualCollectionBacklogLoads = 2;
    private const int BasketRequiredChickenCount = 30;
    private const int MaximumRecoveryShopPurchases = 64;

    [Header("Operation")]
    [SerializeField] private bool startEnabled = false;
    [SerializeField, Min(100f)] private float pointerSpeed = 3600f;
    [SerializeField, Range(2f, 12f)] private float pointerSpringFrequency = 5.5f;
    [SerializeField, Range(0.35f, 0.95f)] private float pointerSpringDamping = 0.68f;
    [SerializeField, Min(0f)] private float pointerDwellTime = 0.08f;
    [SerializeField, Min(0.05f)] private float actionPause = 0.2f;
    [SerializeField, Min(0.5f)] private float vacuumHoldTime = 4f;

    [Header("Strategy")]
    [SerializeField, Min(0)] private int minimumFeedBags = 1;
    [SerializeField, Min(1)] private int maximumShopPurchasesPerVisit = 12;
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
    private bool lastFailureWasCollectionLimited;
    private readonly Dictionary<int, int> foodPlacementAttemptsByPen =
        new Dictionary<int, int>();
    private readonly List<ChickenEgg> vacuumTargetCandidates =
        new List<ChickenEgg>();
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private bool cursorStateCaptured;
    private bool isRunning;
    private Mouse physicalMouse;
    private Vector2 pointerVelocity;
    private float nextPenNavigationTime;

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
        lastFailureWasCollectionLimited = false;
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
            lastFailureWasCollectionLimited = !passed
                && round != null
                && round.RoundEggsLaid > 0
                && round.RoundEggsCollected
                    < Mathf.CeilToInt(
                        round.RoundEggsLaid * EfficientCollectionRatio);
            UpdateFeedStrategy(round);
        }

        if (phase == RoundSystem.RoundPhase.SuppliesShop)
        {
            shopPurchaseCount = 0;
            shopUpgradeCursor = 0;
        }

        if (phase == RoundSystem.RoundPhase.InProgress)
        {
            collectionActionCount = 0;
            foodPlacementAttemptsByPen.Clear();
            nextPenNavigationTime = Time.unscaledTime + 0.5f;
        }
    }

    private IEnumerator PlayRound()
    {
        FoodShopController foodShop = FoodShopController.Instance;
        PenEquipmentHudController equipmentHud =
            PenEquipmentHudController.Instance;

        // A local-tech dialog blocks the entire playfield. Always finish or
        // dismiss it before considering pens, food, or egg collection; those
        // controls may still be active but cannot actually receive a click.
        if (equipmentHud != null && equipmentHud.IsUpgradeDialogOpen)
        {
            Button dialogAction = equipmentHud.GetRecommendedAutomationButton();
            if (IsUsable(dialogAction))
            {
                yield return ClickButton(
                    dialogAction,
                    $"ROUND  .  LOCAL PEN TECH DIALOG  .  {dialogAction.name.ToUpperInvariant()}");
            }
            else
            {
                equipmentHud.CloseUpgradeDialog();
                ReleaseMouseButtons();
                SetStatus("ROUND  .  RECOVERING FROM LOCAL TECH DIALOG");
                yield return new WaitForSecondsRealtime(actionPause);
            }

            yield break;
        }

        if (FoodShopController.IsPlacementActive)
        {
            PenExpansionManager placementManager =
                PenExpansionManager.Instance;
            int placementPenIndex = placementManager != null
                ? placementManager.FocusedPenIndex
                : 0;
            EggCarryController placementCollection =
                EggCarryController.Instance;
            bool placementPenUsesAutoFeeder = placementManager != null
                && placementManager.IsEquipmentOwned(
                    placementPenIndex,
                    PenExpansionManager.EquipmentType.AutoFeeder);
            bool shouldContinuePlacement = foodShop != null
                && foodShop.OwnedFoodCount > 0
                && !placementPenUsesAutoFeeder
                && !ShouldPauseFeedForEggBacklog(
                    placementManager,
                    placementPenIndex,
                    placementCollection)
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
        bool autoFeederRequired = IsAutoFeederRequiredForFocusedPen(
            penManager);
        bool focusedPenReadyForExpansion =
            IsFocusedPenReadyForExpansion(penManager);
        bool hasPendingPenInvestment = TryGetNextOwnedPenInvestment(
            penManager,
            out _,
            out _,
            out _);
        if (autoFeederRequired)
        {
            Button autoFeederAction =
                equipmentHud?.GetRecommendedAutomationButton();
            if (IsUsable(autoFeederAction))
            {
                yield return ClickButton(
                    autoFeederAction,
                    $"ROUND  .  BUYING REQUIRED AUTO-FEEDER FOR PEN {focusedPenIndex + 1}");
                yield break;
            }
        }

        if (focusedPenReadyForExpansion
            && (consecutiveFailedRounds > 0
                || !hasPendingPenInvestment)
            && TryGetAffordablePenPurchase(
                penManager,
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
                nextPenNavigationTime = Time.unscaledTime;
            }
            else
            {
                SetStatus($"ROUND  .  PEN {nextPenIndex + 1} PURCHASE NOT CONFIRMED");
                yield return new WaitForSecondsRealtime(actionPause);
            }

            yield break;
        }

        EggCarryController collection = EggCarryController.Instance;
        Button localEquipmentAction =
            equipmentHud?.GetRecommendedAutomationButton();
        if (IsUsable(localEquipmentAction))
        {
            yield return ClickButton(
                localEquipmentAction,
                $"ROUND  .  LOCAL PEN TECH  .  {localEquipmentAction.name.ToUpperInvariant()}");
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

        int desiredPileCount = GetDesiredFeedPileCount(focusedPenIndex);
        if (foodShop != null
            && foodShop.OwnedFoodCount > 0
            && (penManager == null
                || !penManager.IsEquipmentOwned(
                    focusedPenIndex,
                    PenExpansionManager.EquipmentType.AutoFeeder))
            && !ShouldPauseFeedForEggBacklog(
                penManager,
                focusedPenIndex,
                collection)
            && CountAvailableFoodPiles(focusedPenIndex) < desiredPileCount)
        {
            yield return ClickNamedButton("Food Icon Button", "SELECTING FEED");
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
        bool focusedIncubatorNeedsEgg = IsFocusedIncubatorIdle()
            && (activePenManager != null
                && activePenManager.IsInitialized
                    ? GetAvailableEggCount(
                    activePenManager,
                    activePenManager.FocusedPenIndex) > 0
                    : ChickenEgg.ActiveInstances.Count > 0);
        bool canManuallyLoadCrosshatcher = ShouldServiceCrosshatchers()
            && !focusedIncubatorNeedsEgg;

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
        if (!TryFindClickableEgg(out ChickenEgg egg, out _))
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
        bool hasCollectibleEgg =
            TryFindClickableEgg(out ChickenEgg egg, out _);
        bool basketLoaded =
            collection.BasketEggCount >= collection.CurrentBasketCapacity
            || !hasCollectibleEgg;
        bool seedIdleIncubator = IsFocusedIncubatorIdle();

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

    private IEnumerator UseVacuum()
    {
        if (!TryFindVacuumClusterTarget(
                out ChickenEgg egg,
                out Vector2 initialEggPoint))
        {
            SetStatus("VACUUM  .  WAITING FOR EGGS");
            yield return new WaitForSecondsRealtime(0.08f);
            yield break;
        }

        bool seedIdleIncubator = IsFocusedIncubatorIdle();
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
            if (incubate
                && (!CanContinueVacuumingToIncubator()
                    || idleIncubatorSeedLaunched))
            {
                ForceMouseButton(button, false);
                incubate = false;
                button = MouseButton.Left;
                ForceMouseButton(button, true);
                SetStatus(idleIncubatorSeedLaunched
                    ? "VACUUM  .  INCUBATOR SEEDED  .  CASH SUCK"
                    : "VACUUM  .  INCUBATOR FULL  .  CASH SUCK");
            }

            if (!TryGetEggScreenPoint(trackedEgg, out Vector2 liveEggPoint))
            {
                trackedEgg = null;
                if (TryFindVacuumClusterTarget(
                        out trackedEgg,
                        out liveEggPoint))
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

        Vector2 placementAnchor = GetFoodPlacementAnchorViewport(penIndex);
        Vector2 baseViewport = placementAnchor
            + FoodPlacementViewportOffsets[
                placementAttempt % FoodPlacementViewportOffsets.Length];
        Vector2 jitter = Random.insideUnitCircle;
        jitter = new Vector2(jitter.x * 0.02f, jitter.y * 0.015f);
        Vector2 viewport = baseViewport + jitter;
        viewport.x = Mathf.Clamp(viewport.x, 0.25f, 0.76f);
        viewport.y = Mathf.Clamp(viewport.y, 0.31f, 0.73f);
        foodPlacementAttemptsByPen[penIndex] = placementAttempt + 1;
        Vector2 screenPoint = new Vector2(
            viewport.x * Screen.width,
            viewport.y * Screen.height);
        SetStatus($"FEED  .  PLACING IN PEN ({placementAttempt + 1})");
        yield return ClickScreen(screenPoint);
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
        bool failureExpansionAvailable = penManager != null
            && consecutiveFailedRounds > 0
            && penManager.OwnedPenCount < automatedOwnedPenTarget
            && nextPenIndex >= 0
            && IsFocusedPenReadyForExpansion(penManager)
            && !penManager.IsPenPurchaseInProgress
            && EggScoreHud.CurrentCents
                >= penManager.GetPenCostCents(nextPenIndex);
        bool wantsAnotherPen = penManager != null
            && penManager.OwnedPenCount < automatedOwnedPenTarget
            && nextPenIndex >= 0
            && IsFocusedPenReadyForExpansion(penManager)
            && (consecutiveFailedRounds > 0
                || !hasPendingPenInvestment);
        ProgressionNodeButton[] nodes =
            Object.FindObjectsByType<ProgressionNodeButton>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        int availableFoodPiles = CountAvailableFoodPiles();
        int desiredFeedInventory = Mathf.Max(
            0,
            GetDesiredTotalFeedPileCount() - availableFoodPiles);
        int totalFoodSupply = availableFoodPiles
            + (foodShop != null ? foodShop.OwnedFoodCount : 0);
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
            && shopPurchaseCount < (prioritizeRetryFeed
                ? MaximumRecoveryShopPurchases
                : maximumShopPurchasesPerVisit))
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
            && ChickenController.ActiveInstances.Count
                >= BasketRequiredChickenCount;
        if (needsStarterBasket)
        {
            ProgressionNodeButton starterBasket =
                FindVacuumPriorityNode(nodes);
            if (starterBasket != null)
            {
                yield return EnsureShopTabVisible(starterBasket);
                Button basketButton = starterBasket.GetComponent<Button>();
                if (CanPurchaseProgressionNode(basketButton))
                {
                    yield return ClickButton(
                        basketButton,
                        "SHOP  .  30 CHICKENS  .  PRIORITISING BASKET");
                    Button previewBuy = FindNamedButton("Preview Buy");
                    if (IsUsable(previewBuy))
                    {
                        shopPurchaseCount++;
                        yield return ClickButton(
                            previewBuy,
                            "SHOP  .  BUYING STARTER BASKET");
                        yield break;
                    }
                }

                if (consecutiveFailedRounds <= 0)
                {
                    ProgressionSystem.NodeState basketState =
                        starterBasket.GetNodeState();
                    SetStatus(
                        "SHOP  .  30 CHICKENS  .  SAVING FOR BASKET  "
                        + $"{EggScoreHud.CurrentCents}/{basketState.Cost}");
                    yield return ClickNamedButton(
                        "Done Shopping",
                        "SHOP  .  SAVING FOR REQUIRED BASKET");
                    yield break;
                }
            }
        }

        if (hasPendingPenInvestment
            && consecutiveFailedRounds <= 0)
        {
            SetStatus(
                affordablePenInvestment
                    ? $"SHOP  .  RETURNING FOR PEN {investmentPenIndex + 1} {investmentLabel}"
                    : $"SHOP  .  SAVING FOR PEN {investmentPenIndex + 1} {investmentLabel}  "
                        + $"{EggScoreHud.CurrentCents}/{investmentCost}");
            yield return ClickNamedButton(
                "Done Shopping",
                affordablePenInvestment
                    ? $"SHOP  .  UPGRADING PEN {investmentPenIndex + 1} NEXT"
                    : $"SHOP  .  SAVING FOR PEN {investmentPenIndex + 1} UPGRADE");
            yield break;
        }

        // A failed round switches the shop into recovery mode. Spend what is
        // available on the upgrade most relevant to the observed bottleneck;
        // never hold the same balance for an unaffordable target and retry the
        // round unchanged.
        if (consecutiveFailedRounds > 0)
        {
            // Structural recovery happens on the playfield rather than in the
            // supplies tree. Leave immediately while the money is still
            // available instead of spending dozens of shop actions first.
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

            if (shopPurchaseCount >= MaximumRecoveryShopPurchases)
            {
                yield return ClickNamedButton(
                    "Done Shopping",
                    "SHOP  .  RECOVERY PURCHASES COMPLETE");
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
                if (CanPurchaseProgressionNode(recoveryButton))
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
                        yield break;
                    }
                }
            }

            if (foodShop != null
                && desiredFeedInventory > 0
                && foodShop.OwnedFoodCount < desiredFeedInventory)
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

        // Egg economy must stay ahead of collection capacity. Previously the
        // basket/vacuum branches below could leave the shop early every visit,
        // which starved Premium Eggs, Prime Feed, weight, and value upgrades.
        ProgressionNodeButton economyPriority =
            FindEggEconomyPriorityNode(nodes);
        if (economyPriority != null
            && shopPurchaseCount < maximumShopPurchasesPerVisit)
        {
            yield return EnsureShopTabVisible(economyPriority);
            Button economyButton = economyPriority.GetComponent<Button>();
            if (CanPurchaseProgressionNode(economyButton))
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
                && RoundSystem.Instance.RoundNumber >= 4;
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

        if (collection != null
            && !collection.HasVacuum
            && collection.BasketUpgradeLevel
                < EggCarryController.MaximumBasketLevel)
        {
            ProgressionNodeButton nextBasket =
                FindVacuumPriorityNode(nodes);
            if (nextBasket != null)
            {
                yield return EnsureShopTabVisible(nextBasket);
                Button basketButton = nextBasket.GetComponent<Button>();
                if (CanPurchaseProgressionNode(basketButton))
                {
                    yield return ClickButton(
                        basketButton,
                        $"SHOP  .  PRIORITISING BASKET LEVEL {collection.BasketUpgradeLevel + 1}");
                    Button previewBuy = FindNamedButton("Preview Buy");
                    if (IsUsable(previewBuy))
                    {
                        shopPurchaseCount++;
                        yield return ClickButton(
                            previewBuy,
                            $"SHOP  .  BUYING BASKET LEVEL {collection.BasketUpgradeLevel + 1}");
                        yield break;
                    }
                }

                ProgressionSystem.NodeState basketState =
                    nextBasket.GetNodeState();
                SetStatus(
                    $"SHOP  .  SAVING FOR BASKET LEVEL "
                    + $"{collection.BasketUpgradeLevel + 1}  "
                    + $"{EggScoreHud.CurrentCents}/{basketState.Cost}");
                yield return ClickNamedButton(
                    "Done Shopping",
                    $"SHOP  .  SAVING FOR BASKET LEVEL "
                    + $"{collection.BasketUpgradeLevel + 1}");
                yield break;
            }
        }

        if (collection != null
            && !collection.HasVacuum
            && collection.BasketUpgradeLevel
                >= EggCarryController.MaximumBasketLevel)
        {
            ProgressionNodeButton vacuumUnlock =
                FindVacuumPriorityNode(nodes);
            if (vacuumUnlock != null)
            {
                bool basketReachUpgrade = vacuumUnlock.UpgradeId
                    == ProgressionSystem.UpgradeId.BasketReach;
                string upgradeName = basketReachUpgrade
                    ? $"BASKET REACH {collection.BasketReachLevel + 1}"
                    : "EGG VACUUM";
                yield return EnsureShopTabVisible(vacuumUnlock);
                Button vacuumButton = vacuumUnlock.GetComponent<Button>();
                if (CanPurchaseProgressionNode(vacuumButton))
                {
                    yield return ClickButton(
                        vacuumButton,
                        $"SHOP  .  PRIORITISING {upgradeName}");
                    Button previewBuy = FindNamedButton("Preview Buy");
                    if (IsUsable(previewBuy))
                    {
                        shopPurchaseCount++;
                        yield return ClickButton(
                            previewBuy,
                            $"SHOP  .  BUYING {upgradeName}");
                        yield break;
                    }
                }

                ProgressionSystem.NodeState vacuumState =
                    vacuumUnlock.GetNodeState();
                SetStatus(
                    $"SHOP  .  SAVING FOR {upgradeName}  "
                    + $"{EggScoreHud.CurrentCents}/{vacuumState.Cost}");
                yield return ClickNamedButton(
                    "Done Shopping",
                    $"SHOP  .  SAVING FOR {upgradeName}");
                yield break;
            }
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
            && shopPurchaseCount < maximumShopPurchasesPerVisit)
        {
            yield return EnsureShopTabVisible(feedSpeedPriorityNode);
            Button feedSpeedUpgrade =
                feedSpeedPriorityNode.GetComponent<Button>();
            if (CanPurchaseProgressionNode(feedSpeedUpgrade))
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

        // Buy only a compact spread of the strongest available feed. This
        // preserves round time for vacuuming instead of spending it placing a
        // large number of weak individual piles.
        if (foodShop != null
            && desiredFeedInventory > 0
            && foodShop.OwnedFoodCount < desiredFeedInventory)
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

        if (IsAutoFeederRequiredForFocusedPen(penManager))
        {
            int penIndex = penManager.FocusedPenIndex;
            int cost = penManager.GetEquipmentPurchaseCost(
                PenExpansionManager.EquipmentType.AutoFeeder);
            SetStatus(
                $"SHOP  .  SAVING FOR PEN {penIndex + 1} AUTO-FEEDER  "
                + $"{EggScoreHud.CurrentCents}/{cost}");
            yield return ClickNamedButton(
                "Done Shopping",
                $"SHOP  .  AUTO-FEEDER REQUIRED FOR PEN {penIndex + 1}");
            yield break;
        }

        if (collection != null
            && collection.HasVacuum
            && collection.BasketUpgradeLevel
                >= EggCarryController.MaximumBasketLevel
            && IsRobotRequiredForFocusedPen(penManager))
        {
            int penIndex = penManager.FocusedPenIndex;
            int cost = penManager.GetEquipmentPurchaseCost(
                PenExpansionManager.EquipmentType.Robot);
            SetStatus(
                $"SHOP  .  SAVING FOR PEN {penIndex + 1} ROBOT  "
                + $"{EggScoreHud.CurrentCents}/{cost}");
            yield return ClickNamedButton(
                "Done Shopping",
                $"SHOP  .  ROBOT REQUIRED BEFORE PEN EXPANSION");
            yield break;
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

        ProgressionNodeButton vacuumPriorityNode =
            FindVacuumPriorityNode(nodes);
        if (vacuumPriorityNode != null)
        {
            yield return EnsureShopTabVisible(vacuumPriorityNode);
            Button priorityUpgrade = vacuumPriorityNode.GetComponent<Button>();
            if (CanPurchaseProgressionNode(priorityUpgrade))
            {
                yield return ClickButton(
                    priorityUpgrade,
                    $"SHOP  â€¢  PRIORITISING {priorityUpgrade.name.ToUpperInvariant()}");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (IsUsable(previewBuy))
                {
                    shopPurchaseCount++;
                    yield return ClickButton(
                        previewBuy,
                        $"SHOP  â€¢  BUYING {priorityUpgrade.name.ToUpperInvariant()}");
                    yield break;
                }
            }
        }

        if (vacuumPriorityNode != null)
        {
            yield return ClickNamedButton(
                "Done Shopping",
                "SHOP  â€¢  SAVING FOR VACUUM");
            yield break;
        }

        if (shopPurchaseCount < maximumShopPurchasesPerVisit)
        {
            for (int offset = 0; offset < nodes.Length; offset++)
            {
                int index = (shopUpgradeCursor + offset) % nodes.Length;
                ProgressionNodeButton node = nodes[index];
                if (node.UpgradeId == ProgressionSystem.UpgradeId.FoodBag)
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
                if (!CanPurchaseProgressionNode(node))
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
            && (IsFocusedIncubatorIdle()
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

        ProgressionSystem.UpgradeId priorityId =
            collection.BasketUpgradeLevel
                    >= EggCarryController.MaximumBasketLevel
                || collection.BasketReachLevel
                    >= EggCarryController.MaximumBasketReachLevel
                ? ProgressionSystem.UpgradeId.VacuumUnlock
                : ProgressionSystem.UpgradeId.BasketCapacity;

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
        int bestCost = int.MaxValue;
        long spendableCents = EggScoreHud.CurrentCents - reservedCents;
        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (!CanPurchaseProgressionNode(node)
                || node.UpgradeId == ProgressionSystem.UpgradeId.FoodBag
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
                ProgressionSystem.UpgradeId.RareEggChance => 750,
                ProgressionSystem.UpgradeId.FeedSpeed => 700,
                ProgressionSystem.UpgradeId.PrimeFeed => 675,
                ProgressionSystem.UpgradeId.EggWeight => 650,
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
            ProgressionSystem.UpgradeId.TruckBonus => 1150,
            ProgressionSystem.UpgradeId.RareEggChance => 1100,
            ProgressionSystem.UpgradeId.FeedSpeed => 1050,
            ProgressionSystem.UpgradeId.PrimeFeed => 1000,
            ProgressionSystem.UpgradeId.EggWeight => 950,
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
        int desiredWeightLevel = GetDesiredEggValueLevel(roundNumber);
        int desiredValueLevel = GetDesiredExtendedEggValueLevel(roundNumber);
        int desiredTruckBonusLevel = GetDesiredTruckBonusLevel(roundNumber);
        int premiumLevel = progression.RareEggChanceLevel;
        int primeFeedLevel = FoodShopController.Instance != null
            ? FoodShopController.Instance.PrimeFeedLevel
            : 0;
        int weightLevel = progression.EggWeightLevel;
        int valueLevel = progression.EggValueLevel;
        int truckBonusLevel = progression.TruckBonusLevel;

        ProgressionSystem.UpgradeId upgradeId;
        int targetLevel;
        if (premiumLevel < 2)
        {
            upgradeId = ProgressionSystem.UpgradeId.RareEggChance;
            targetLevel = premiumLevel + 1;
        }
        else if (primeFeedLevel < desiredPrimeFeedLevel)
        {
            upgradeId = ProgressionSystem.UpgradeId.PrimeFeed;
            targetLevel = primeFeedLevel + 1;
        }
        else if (weightLevel < desiredWeightLevel
            && (weightLevel + 1 < premiumLevel
                || premiumLevel >= desiredPremiumLevel))
        {
            upgradeId = ProgressionSystem.UpgradeId.EggWeight;
            targetLevel = weightLevel + 1;
        }
        else if (truckBonusLevel < desiredTruckBonusLevel
            && valueLevel >= 2
            && truckBonusLevel + 2 < valueLevel)
        {
            upgradeId = ProgressionSystem.UpgradeId.TruckBonus;
            targetLevel = truckBonusLevel + 1;
        }
        else if (valueLevel < desiredValueLevel
            && (valueLevel + 1 < premiumLevel
                || premiumLevel >= desiredPremiumLevel))
        {
            upgradeId = ProgressionSystem.UpgradeId.EggValue;
            targetLevel = valueLevel + 1;
        }
        else if (premiumLevel < desiredPremiumLevel)
        {
            upgradeId = ProgressionSystem.UpgradeId.RareEggChance;
            targetLevel = premiumLevel + 1;
        }
        else if (weightLevel < desiredWeightLevel)
        {
            upgradeId = ProgressionSystem.UpgradeId.EggWeight;
            targetLevel = weightLevel + 1;
        }
        else if (valueLevel < desiredValueLevel)
        {
            upgradeId = ProgressionSystem.UpgradeId.EggValue;
            targetLevel = valueLevel + 1;
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
                >= GetDesiredEggValueLevel(roundNumber)
            && progression.EggValueLevel
                >= GetDesiredEggValueLevel(roundNumber);
    }

    private static int GetDesiredPremiumEggLevel(int roundNumber)
    {
        return Mathf.Clamp(2 + Mathf.Max(0, roundNumber) / 4, 2, 6);
    }

    private static int GetDesiredEggValueLevel(int roundNumber)
    {
        return Mathf.Clamp(1 + Mathf.Max(0, roundNumber) / 4, 1, 5);
    }

    private static int GetDesiredExtendedEggValueLevel(int roundNumber)
    {
        return Mathf.Clamp(
            1 + Mathf.Max(0, roundNumber) / 4,
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

        // Keep every owned incubator producing. An empty incubator is seeded
        // before routine collection as long as its pen can hatch another bird.
        if (TryGetIdleIncubatorPen(manager, out int incubatorPenIndex))
        {
            if (incubatorPenIndex == currentPenIndex)
            {
                return false;
            }

            targetPenIndex = incubatorPenIndex;
            return true;
        }

        // Once this round's quota is safe, or while recovering from a failed
        // round, service any available crosshatcher.
        // A smart robot reservation makes the machine unavailable, so manual
        // loading remains safe even in robot-equipped pens.
        if (ShouldServiceCrosshatchers()
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

    private static bool TryGetIdleIncubatorPen(
        PenExpansionManager manager,
        out int targetPenIndex)
    {
        targetPenIndex = -1;
        int bestPopulationDeficit = -1;
        int bestChickenCount = -1;
        int bestEggCount = -1;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index))
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
                CrosshatcherController.MinimumFlockSizeForNewCycle
                    - chickenCount);
            if (populationDeficit > bestPopulationDeficit
                || (populationDeficit == bestPopulationDeficit
                    && chickenCount > bestChickenCount)
                || (populationDeficit == bestPopulationDeficit
                    && chickenCount == bestChickenCount
                    && eggCount > bestEggCount))
            {
                targetPenIndex = index;
                bestPopulationDeficit = populationDeficit;
                bestChickenCount = chickenCount;
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
                        < CrosshatcherController.MinimumFlockSizeForNewCycle)
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

    private bool ShouldServiceCrosshatchers()
    {
        return !NeedsCashQuotaDelivery() || consecutiveFailedRounds > 0;
    }

    private static bool TryGetNextOwnedPenInvestment(
        PenExpansionManager manager,
        out int targetPenIndex,
        out string investmentLabel,
        out int investmentCost)
    {
        targetPenIndex = -1;
        investmentLabel = string.Empty;
        investmentCost = int.MaxValue;
        if (manager == null || !manager.IsInitialized)
        {
            return false;
        }

        EggCarryController collection = EggCarryController.Instance;
        bool botReadyForRobot = collection != null
            && collection.BasketUpgradeLevel
                >= EggCarryController.MaximumBasketLevel
            && collection.HasVacuum
            && HasRecommendedPremiumEggProgression();

        for (int penIndex = 0; penIndex < manager.PenCount; penIndex++)
        {
            if (!manager.IsPenOwned(penIndex))
            {
                continue;
            }

            bool ownsRobot = manager.IsEquipmentOwned(
                penIndex,
                PenExpansionManager.EquipmentType.Robot);
            bool ownsAutoFeeder = manager.IsEquipmentOwned(
                penIndex,
                PenExpansionManager.EquipmentType.AutoFeeder);

            // This is the equipment HUD's hard automation dependency. Do not
            // target cheaper optional upgrades that the HUD will intentionally
            // defer until the feeder has been installed.
            if (ownsRobot && !ownsAutoFeeder)
            {
                ConsiderPenInvestment(
                    penIndex,
                    "AUTO-FEEDER",
                    manager.GetEquipmentPurchaseCost(
                        PenExpansionManager.EquipmentType.AutoFeeder),
                    ref targetPenIndex,
                    ref investmentLabel,
                    ref investmentCost);
                continue;
            }

            for (int typeIndex = 0;
                typeIndex < PenEquipmentInvestmentOrder.Length;
                typeIndex++)
            {
                PenExpansionManager.EquipmentType type =
                    PenEquipmentInvestmentOrder[typeIndex];
                if (type == PenExpansionManager.EquipmentType.Robot
                    && !botReadyForRobot)
                {
                    continue;
                }
                if (type == PenExpansionManager.EquipmentType.AutoFeeder
                    && !ownsRobot)
                {
                    continue;
                }

                if (!manager.IsEquipmentOwned(penIndex, type))
                {
                    ConsiderPenInvestment(
                        penIndex,
                        type.ToString().ToUpperInvariant(),
                        manager.GetEquipmentPurchaseCost(type),
                        ref targetPenIndex,
                        ref investmentLabel,
                        ref investmentCost);
                    continue;
                }

                if (type == PenExpansionManager.EquipmentType.Incubator
                    && manager.GetChickenCount(penIndex)
                        >= ChickenController.MaximumChickenCount)
                {
                    continue;
                }

                PenExpansionManager.EquipmentUpgrade[] upgrades =
                    PenExpansionManager.GetUpgrades(type);
                for (int upgradeIndex = 0;
                    upgradeIndex < upgrades.Length;
                    upgradeIndex++)
                {
                    PenExpansionManager.EquipmentUpgrade upgrade =
                        upgrades[upgradeIndex];
                    ConsiderPenInvestment(
                        penIndex,
                        upgrade.ToString().ToUpperInvariant(),
                        manager.GetUpgradeCost(penIndex, upgrade),
                        ref targetPenIndex,
                        ref investmentLabel,
                        ref investmentCost);
                }
            }
        }

        if (targetPenIndex >= 0)
        {
            return true;
        }

        investmentCost = 0;
        return false;
    }

    private static void ConsiderPenInvestment(
        int penIndex,
        string label,
        int cost,
        ref int targetPenIndex,
        ref string investmentLabel,
        ref int investmentCost)
    {
        if (cost <= 0 || cost >= investmentCost)
        {
            return;
        }

        targetPenIndex = penIndex;
        investmentLabel = label;
        investmentCost = cost;
    }

    private static bool AreAllOwnedPensRobotEquipped(
        PenExpansionManager manager)
    {
        if (manager == null || !manager.IsInitialized)
        {
            return false;
        }

        bool foundOwnedPen = false;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index))
            {
                continue;
            }

            foundOwnedPen = true;
            if (!manager.HasRobotInPen(index))
            {
                return false;
            }
        }

        return foundOwnedPen;
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

    private bool TryGetPenNeedingFeedCoverage(
        PenExpansionManager manager,
        out int targetPenIndex)
    {
        targetPenIndex = -1;
        int largestDeficit = 0;
        int largestFlock = -1;
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index)
                || manager.IsEquipmentOwned(
                    index,
                    PenExpansionManager.EquipmentType.AutoFeeder))
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

    private bool TryGetAffordablePenPurchase(
        PenExpansionManager manager,
        out int nextPenIndex)
    {
        nextPenIndex = manager != null
            ? manager.NextUnownedPenIndex
            : -1;
        return manager != null
            && manager.OwnedPenCount < automatedOwnedPenTarget
            && !manager.IsPenPurchaseInProgress
            && nextPenIndex >= 0
            && EggScoreHud.CurrentCents
                >= manager.GetPenCostCents(nextPenIndex);
    }

    private static bool IsAutoFeederRequiredForFocusedPen(
        PenExpansionManager manager)
    {
        if (manager == null || manager.FocusedPenIndex < 0)
        {
            return false;
        }

        int penIndex = manager.FocusedPenIndex;
        return manager.IsEquipmentOwned(
                penIndex,
                PenExpansionManager.EquipmentType.Robot)
            && !manager.IsEquipmentOwned(
                penIndex,
                PenExpansionManager.EquipmentType.AutoFeeder);
    }

    private static bool IsRobotRequiredForFocusedPen(
        PenExpansionManager manager)
    {
        return manager != null
            && manager.FocusedPenIndex >= 0
            && manager.IsPenOwned(manager.FocusedPenIndex)
            && !manager.IsEquipmentOwned(
                manager.FocusedPenIndex,
                PenExpansionManager.EquipmentType.Robot);
    }

    private static bool IsFocusedPenReadyForExpansion(
        PenExpansionManager manager)
    {
        if (manager == null || manager.FocusedPenIndex < 0)
        {
            return false;
        }

        int penIndex = manager.FocusedPenIndex;
        return manager.IsEquipmentOwned(
                penIndex,
                PenExpansionManager.EquipmentType.Robot)
            && manager.IsEquipmentOwned(
                penIndex,
                PenExpansionManager.EquipmentType.AutoFeeder);
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

    private static bool ShouldPauseFeedForEggBacklog(
        PenExpansionManager manager,
        int penIndex,
        EggCarryController collection)
    {
        if (manager == null
            || penIndex < 0
            || (collection != null && collection.HasVacuum)
            || manager.HasRobotInPen(penIndex))
        {
            return false;
        }

        int manualCapacity = collection != null
            && collection.BasketUpgradeLevel > 0
                ? collection.CurrentBasketCapacity
                : 1;
        int maximumManualBacklog = Mathf.Max(
            3,
            manualCapacity * ManualCollectionBacklogLoads);
        return GetAvailableEggCount(manager, penIndex)
            >= maximumManualBacklog;
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

        if (!round.DidPassRound)
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
        if (collectionRatio < EfficientCollectionRatio
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

        int targetTier = 1 + round.RoundNumber / 3;
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

    private int GetDesiredFeedPileCount(int penIndex = -1)
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
        if (hasVacuum)
        {
            return Mathf.Clamp(
                sharedPileTarget,
                0,
                MaximumDesiredFeedPiles);
        }

        return Mathf.Min(desiredFeedPiles, sharedPileTarget);
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

    private static int CountAvailableFoodPiles(int penIndex = -1)
    {
        int available = 0;
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
                available++;
            }
        }

        return available;
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
        if (crosshatcher.OccupiedSlots == 0
            && focusedChickenCount
                < CrosshatcherController.MinimumFlockSizeForNewCycle)
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
                || chicken.Breed
                    == ChickenController.ChickenBreed.Cosmic
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
        return remainingChance * progression.GetEggValueCents(
                ChickenEgg.EggType.Common)
            + rareProbability * progression.GetEggValueCents(
                ChickenEgg.EggType.Rare)
            + epicProbability * progression.GetEggValueCents(
                ChickenEgg.EggType.Epic)
            + legendaryProbability * progression.GetEggValueCents(
                ChickenEgg.EggType.Legendary)
            + cosmicProbability * progression.GetEggValueCents(
                ChickenEgg.EggType.Cosmic);
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

        int bestRarity = -1;
        int bestValue = -1;
        float bestDistance = float.PositiveInfinity;
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var eggs = ChickenEgg.ActiveInstances;

        for (int index = eggs.Count - 1; index >= 0; index--)
        {
            ChickenEgg egg = eggs[index];

            if (egg == null || egg.IsHeld || egg.IsCollected)
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

            float distance = Vector2.SqrMagnitude(point - screenCenter);
            int rarity = (int)egg.Type;

            if (rarity < bestRarity
                || (rarity == bestRarity && egg.ValueCents < bestValue)
                || (rarity == bestRarity
                    && egg.ValueCents == bestValue
                    && distance >= bestDistance))
            {
                continue;
            }

            selectedEgg = egg;
            selectedPoint = point;
            bestRarity = rarity;
            bestValue = egg.ValueCents;
            bestDistance = distance;
        }

        return selectedEgg != null;
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
                clusterValue += neighbour.ValueCents;
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
        float elapsed = 0f;
        while (elapsed < 1.25f)
        {
            Vector2 current = mouse.position.ReadValue();
            if (Vector2.SqrMagnitude(destination - current) <= 4f
                && pointerVelocity.sqrMagnitude <= 900f)
            {
                break;
            }

            MovePointerSpring(destination, heldButton);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        SetPointerPosition(destination, heldButton);
        pointerVelocity *= 0.35f;
        yield return null;
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
        float remainingTime = Mathf.Clamp(
            Time.unscaledDeltaTime,
            0.001f,
            0.05f);
        float angularFrequency = pointerSpringFrequency * Mathf.PI * 2f;
        float stiffness = angularFrequency * angularFrequency;
        float damping = 2f * pointerSpringDamping * angularFrequency;

        while (remainingTime > 0f)
        {
            float step = Mathf.Min(remainingTime, 1f / 120f);
            Vector2 acceleration =
                (destination - current) * stiffness
                - pointerVelocity * damping;
            pointerVelocity += acceleration * step;
            pointerVelocity = Vector2.ClampMagnitude(
                pointerVelocity,
                pointerSpeed);
            current += pointerVelocity * step;
            remainingTime -= step;
        }

        SetPointerPosition(current, heldButton);
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
        pointerSpringFrequency = Mathf.Clamp(pointerSpringFrequency, 2f, 12f);
        pointerSpringDamping = Mathf.Clamp(pointerSpringDamping, 0.35f, 0.95f);
        pointerDwellTime = Mathf.Max(0f, pointerDwellTime);
        actionPause = Mathf.Max(0.05f, actionPause);
        vacuumHoldTime = Mathf.Max(0.5f, vacuumHoldTime);
        minimumFeedBags = Mathf.Max(0, minimumFeedBags);
        maximumShopPurchasesPerVisit = Mathf.Max(1, maximumShopPurchasesPerVisit);
        automatedOwnedPenTarget = Mathf.Max(1, automatedOwnedPenTarget);
        penNavigationInterval = Mathf.Max(1f, penNavigationInterval);
    }
}
