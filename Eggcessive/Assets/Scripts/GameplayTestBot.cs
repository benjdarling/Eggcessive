using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GameplayTestBot : MonoBehaviour
{
    private static Mouse automationInputMouse;

    private static readonly Vector2[] FoodPlacementViewportPoints =
    {
        new Vector2(0.5f, 0.52f),
        new Vector2(0.42f, 0.5f),
        new Vector2(0.58f, 0.5f),
        new Vector2(0.5f, 0.43f),
        new Vector2(0.38f, 0.42f),
        new Vector2(0.62f, 0.42f),
        new Vector2(0.46f, 0.62f),
        new Vector2(0.56f, 0.62f)
    };
    private const float EfficientCollectionRatio = 0.72f;
    private const int MaximumEfficientRoundLeftovers = 2;
    private const int MaximumDesiredFeedPiles = 4;
    private const int ChickensPerDesiredFoodPile = 4;

    [Header("Operation")]
    [SerializeField] private bool startEnabled = false;
    [SerializeField, Min(100f)] private float pointerSpeed = 3600f;
    [SerializeField, Range(2f, 12f)] private float pointerSpringFrequency = 5.5f;
    [SerializeField, Range(0.35f, 0.95f)] private float pointerSpringDamping = 0.68f;
    [SerializeField, Min(0f)] private float pointerDwellTime = 0.08f;
    [SerializeField, Min(0.05f)] private float actionPause = 0.2f;
    [SerializeField, Min(0.1f)] private float vacuumHoldTime = 0.85f;

    [Header("Strategy")]
    [SerializeField, Min(0)] private int minimumFeedBags = 1;
    [SerializeField, Min(1)] private int maximumShopPurchasesPerVisit = 12;

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
    private int foodPlacementAttempt;
    private int completedActions;
    private int desiredFeedPiles = 1;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private bool cursorStateCaptured;
    private bool isRunning;
    private Mouse physicalMouse;
    private Vector2 pointerVelocity;

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
        SetStatus("OFF  •  F8 TO START");
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

        SetStatus("OFF  •  F8 TO START");
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
                    SetStatus("COUNTDOWN  •  STANDING BY");
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
                    SetStatus($"{round.Phase.ToString().ToUpperInvariant()}  •  WAITING");
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
            UpdateFeedStrategy(RoundSystem.Instance);
        }

        if (phase == RoundSystem.RoundPhase.SuppliesShop)
        {
            shopPurchaseCount = 0;
            shopUpgradeCursor = 0;
        }

        if (phase == RoundSystem.RoundPhase.InProgress)
        {
            collectionActionCount = 0;
            foodPlacementAttempt = 0;
        }
    }

    private IEnumerator PlayRound()
    {
        FoodShopController foodShop = FoodShopController.Instance;

        if (FoodShopController.IsPlacementActive)
        {
            yield return TryPlaceFood();
            yield break;
        }

        int desiredPileCount = GetDesiredFeedPileCount();
        if (foodShop != null
            && foodShop.OwnedFoodCount > 0
            && CountAvailableFoodPiles() < desiredPileCount)
        {
            yield return ClickNamedButton("Food Icon Button", "SELECTING FEED");
            yield break;
        }

        EggCarryController collection = EggCarryController.Instance;

        if (collection == null)
        {
            SetStatus("ROUND  •  WAITING FOR COLLECTION TOOL");
            yield return new WaitForSecondsRealtime(0.2f);
            yield break;
        }

        collection.SetAutomationRareEggProtection(true);
        CrosshatcherController crosshatcher = FindCrosshatcher();

        if (TryFindCrosshatcherChicken(
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
        else if (collection.HasRobot)
        {
            SetStatus($"ROUND  •  SUPERVISING {collection.CurrentCollectionName.ToUpperInvariant()}");
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
            $"CROSSHATCHER {occupiedBefore}/2  •  LOADING {chicken.Breed.ToString().ToUpperInvariant()}");
        yield return MovePointerToChickenPickup(chicken, pickupTarget);

        if (!IsPointerOverChickenPickup(chicken, pickupTarget))
        {
            yield return new WaitForSecondsRealtime(0.06f);
            yield break;
        }

        QueueMouseButton(MouseButton.Left, true);
        float pickupWait = 0f;

        while (collection.HeldChicken != chicken && pickupWait < 0.4f)
        {
            if (!TryGetChickenPickupScreenPoint(
                    chicken,
                    pickupTarget,
                    out Vector2 livePoint))
            {
                break;
            }

            MovePointerSpring(livePoint, MouseButton.Left);
            pickupWait += Time.unscaledDeltaTime;
            yield return null;
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
        QueueMouseButton(MouseButton.Left, false);

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
            SetStatus("ROUND  •  WAITING FOR EGGS");
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
                out Vector3 dropPosition,
                out Vector2 destinationPoint))
        {
            yield return new WaitForSecondsRealtime(0.15f);
            yield break;
        }

        SetStatus($"HAND  •  {(incubate ? "TO INCUBATOR" : "TO CONTAINER")}");
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
        float arrivalWait = 0f;

        while (egg != null
            && collection.HeldEgg == egg
            && Vector3.SqrMagnitude(egg.transform.position - dropPosition) > 0.0016f
            && arrivalWait < 1.2f)
        {
            ForceMouseButton(MouseButton.Left, true);
            arrivalWait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (collection.HeldEgg != egg)
        {
            yield return new WaitForSecondsRealtime(0.08f);
            yield break;
        }

        QueueMouseButton(MouseButton.Left, false);
        collectionActionCount++;
        completedActions++;
        yield return new WaitForSecondsRealtime(Mathf.Max(actionPause, 0.3f));
    }

    private IEnumerator UseBasket(EggCarryController collection)
    {
        bool hasCollectibleEgg =
            TryFindClickableEgg(out ChickenEgg egg, out _);
        bool basketLoaded =
            collection.BasketEggCount >= collection.CurrentBasketCapacity
            || !hasCollectibleEgg;

        if (collection.BasketEggCount > 0
            && basketLoaded
            && !collection.BasketContainsRareEggs
            && CanUseIncubator())
        {
            SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  •  INCUBATING ONE");
            IncubatorController incubator = FindIncubator();
            int transferCount = Mathf.Min(
                collection.BasketEggCount,
                incubator.AvailableCapacity);
            SetStatus(
                $"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}" +
                $"  •  INCUBATING {transferCount}");

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
            SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  •  CASHING IN");
            yield return ClickWorldComponent(EggContainer.Instance);
            yield break;
        }

        if (!hasCollectibleEgg)
        {
            SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  •  WAITING");
            yield return new WaitForSecondsRealtime(0.18f);
            yield break;
        }

        SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  •  COLLECTING");
        yield return ClickMovingEgg(egg);
    }

    private IEnumerator UseVacuum()
    {
        if (!TryFindClickableEgg(out ChickenEgg egg, out _))
        {
            SetStatus("VACUUM  •  WAITING FOR EGGS");
            yield return new WaitForSecondsRealtime(0.16f);
            yield break;
        }

        bool incubate = ShouldUseIncubator(egg)
            && !HasUncollectedRareEggs();
        SetStatus($"VACUUM  •  {(incubate ? "RIGHT SUCK TO INCUBATOR" : "CASH SUCK")}");
        yield return MovePointerToEgg(egg);
        if (!IsPointerOverEgg(egg))
        {
            yield return new WaitForSecondsRealtime(0.06f);
            yield break;
        }

        MouseButton button = incubate ? MouseButton.Right : MouseButton.Left;
        QueueMouseButton(button, true);
        float vacuumTime = 0f;
        ChickenEgg trackedEgg = egg;
        while (vacuumTime < vacuumHoldTime)
        {
            if (!TryGetEggScreenPoint(trackedEgg, out Vector2 liveEggPoint))
            {
                TryFindClickableEgg(out trackedEgg, out liveEggPoint);
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
            yield return null;
        }

        QueueMouseButton(button, false);
        collectionActionCount++;
        completedActions++;
        yield return new WaitForSecondsRealtime(actionPause);
    }

    private IEnumerator TryPlaceFood()
    {
        Vector2 baseViewport = FoodPlacementViewportPoints[
            foodPlacementAttempt % FoodPlacementViewportPoints.Length];
        Vector2 jitter = Random.insideUnitCircle;
        jitter = new Vector2(jitter.x * 0.035f, jitter.y * 0.025f);
        Vector2 viewport = baseViewport + jitter;
        viewport.x = Mathf.Clamp(viewport.x, 0.35f, 0.65f);
        viewport.y = Mathf.Clamp(viewport.y, 0.39f, 0.65f);
        foodPlacementAttempt++;
        Vector2 screenPoint = new Vector2(
            viewport.x * Screen.width,
            viewport.y * Screen.height);
        SetStatus($"FEED  •  PLACING IN PEN ({foodPlacementAttempt})");
        yield return ClickScreen(screenPoint);
    }

    private IEnumerator HandleResults()
    {
        Button shopButton = FindNamedButton("Open Supplies Shop");

        if (IsUsable(shopButton))
        {
            yield return ClickButton(shopButton, "RESULTS  •  OPENING SHOP");
            yield break;
        }

        if (!resultsSkipSent)
        {
            resultsSkipSent = true;
            SetStatus("RESULTS  •  SKIPPING COUNT-UP");
            yield return ClickScreen(new Vector2(Screen.width * 0.12f, Screen.height * 0.18f));
            yield break;
        }

        SetStatus("RESULTS  •  WAITING FOR BUTTONS");
        yield return new WaitForSecondsRealtime(0.12f);
    }

    private IEnumerator HandleShop()
    {
        ProgressionTreePreview preview =
            Object.FindFirstObjectByType<ProgressionTreePreview>();
        if (preview != null && preview.IsOpen)
        {
            SetStatus("SHOP  •  CLOSING DETAILS");
            RectTransform emptySpace = FindNamedRectTransform("Shop Title");
            if (emptySpace != null)
            {
                Canvas canvas = emptySpace.GetComponentInParent<Canvas>();
                Camera uiCamera = canvas != null
                    && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                        ? canvas.worldCamera
                        : null;
                Vector2 point = RectTransformUtility.WorldToScreenPoint(
                    uiCamera,
                    emptySpace.TransformPoint(emptySpace.rect.center));
                yield return ClickScreen(point);
            }
            else
            {
                preview.Hide();
                yield return new WaitForSecondsRealtime(actionPause);
            }

            yield break;
        }

        FoodShopController foodShop = FoodShopController.Instance;
        ProgressionNodeButton[] nodes =
            Object.FindObjectsByType<ProgressionNodeButton>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        int availableFoodPiles = CountAvailableFoodPiles();
        int desiredFeedInventory = Mathf.Max(
            0,
            GetDesiredFeedPileCount() - availableFoodPiles);
        int totalFoodSupply = availableFoodPiles
            + (foodShop != null ? foodShop.OwnedFoodCount : 0);

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

        IncubatorShopController incubatorShop =
            IncubatorShopController.Instance;
        bool needsIncubator = incubatorShop != null
            && !incubatorShop.IsInstalled
            && !IsChickenCapReached();
        if (totalFoodSupply > 0 && needsIncubator)
        {
            ProgressionNodeButton incubatorPriorityNode =
                FindAffordableProgressionNode(
                    nodes,
                    ProgressionSystem.UpgradeId.IncubatorInstall);
            if (incubatorPriorityNode != null
                && shopPurchaseCount < maximumShopPurchasesPerVisit)
            {
                Button incubatorUpgrade =
                    incubatorPriorityNode.GetComponent<Button>();
                yield return ClickButton(
                    incubatorUpgrade,
                    "SHOP - PRIORITISING INCUBATOR");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (IsUsable(previewBuy))
                {
                    shopPurchaseCount++;
                    yield return ClickButton(
                        previewBuy,
                        "SHOP - BUYING INCUBATOR");
                    yield break;
                }
            }

            yield return ClickNamedButton(
                "Done Shopping",
                "SHOP - SAVING FOR INCUBATOR");
            yield break;
        }

        ProgressionNodeButton crosshatcherPriorityNode =
            FindCrosshatcherPriorityNode(nodes);
        if (crosshatcherPriorityNode != null
            && shopPurchaseCount < maximumShopPurchasesPerVisit)
        {
            Button crosshatcherUpgrade =
                crosshatcherPriorityNode.GetComponent<Button>();

            if (CanPurchaseProgressionNode(crosshatcherUpgrade))
            {
                yield return ClickButton(
                    crosshatcherUpgrade,
                    $"SHOP  •  PRIORITISING {crosshatcherUpgrade.name.ToUpperInvariant()}");
                Button previewBuy = FindNamedButton("Preview Buy");

                if (IsUsable(previewBuy))
                {
                    shopPurchaseCount++;
                    yield return ClickButton(
                        previewBuy,
                        $"SHOP  •  BUYING {crosshatcherUpgrade.name.ToUpperInvariant()}");
                    yield break;
                }
            }
        }

        ProgressionNodeButton vacuumPriorityNode =
            FindVacuumPriorityNode(nodes);
        if (crosshatcherPriorityNode != null
            && (CrosshatcherShopController.Instance == null
                || !CrosshatcherShopController.Instance.IsInstalled))
        {
            yield return ClickNamedButton(
                "Done Shopping",
                "SHOP  •  SAVING FOR CROSSHATCHER");
            yield break;
        }

        if (vacuumPriorityNode != null)
        {
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

        ProgressionNodeButton feedSpeedPriorityNode =
            FindAffordableProgressionNode(
                nodes,
                ProgressionSystem.UpgradeId.FeedSpeed);
        if (totalFoodSupply > 0
            && feedSpeedPriorityNode != null
            && shopPurchaseCount < maximumShopPurchasesPerVisit)
        {
            Button feedSpeedUpgrade =
                feedSpeedPriorityNode.GetComponent<Button>();
            yield return ClickButton(
                feedSpeedUpgrade,
                $"SHOP - PRIORITISING {feedSpeedUpgrade.name.ToUpperInvariant()}");
            Button previewBuy = FindNamedButton("Preview Buy");
            if (IsUsable(previewBuy))
            {
                shopPurchaseCount++;
                yield return ClickButton(
                    previewBuy,
                    $"SHOP - BUYING {feedSpeedUpgrade.name.ToUpperInvariant()}");
                yield break;
            }
        }

        if (foodShop != null
            && foodShop.OwnedFoodCount < desiredFeedInventory)
        {
            Button buyFeed = FindNamedButton("Buy Feed");

            if (CanPurchaseProgressionNode(buyFeed))
            {
                yield return ClickButton(buyFeed, "SHOP  •  SELECTING FEED BAG");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (IsUsable(previewBuy))
                {
                    shopPurchaseCount++;
                    yield return ClickButton(
                        previewBuy,
                        "SHOP  •  BUYING FEED BAG");
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
                if (!CanPurchaseProgressionNode(upgrade))
                {
                    continue;
                }

                yield return ClickButton(
                    upgrade,
                    $"SHOP  •  SELECTING {upgrade.name.ToUpperInvariant()}");
                Button previewBuy = FindNamedButton("Preview Buy");
                if (!IsUsable(previewBuy))
                {
                    continue;
                }

                shopUpgradeCursor = (index + 1) % nodes.Length;
                shopPurchaseCount++;
                yield return ClickButton(
                    previewBuy,
                    $"SHOP  •  BUYING {upgrade.name.ToUpperInvariant()}");
                yield break;
            }
        }

        yield return ClickNamedButton("Done Shopping", "SHOP  •  DONE");
    }

    private bool ShouldUseIncubator(ChickenEgg egg)
    {
        return egg != null
            && egg.Type == ChickenEgg.EggType.Common
            && CanUseIncubator();
    }

    private bool CanUseIncubator()
    {
        IncubatorController incubator = FindIncubator();
        return incubator != null
            && incubator.isActiveAndEnabled
            && incubator.AvailableCapacity > 0;
    }

    private static bool HasUncollectedRareEggs()
    {
        var eggs = ChickenEgg.ActiveInstances;
        for (int index = 0; index < eggs.Count; index++)
        {
            ChickenEgg egg = eggs[index];
            if (egg != null
                && !egg.IsCollected
                && egg.Type != ChickenEgg.EggType.Common)
            {
                return true;
            }
        }

        return false;
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
            collection.BasketUpgradeLevel < 3
                ? ProgressionSystem.UpgradeId.BasketCapacity
                : ProgressionSystem.UpgradeId.VacuumPower;

        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node != null
                && node.UpgradeId == priorityId
                && (priorityId != ProgressionSystem.UpgradeId.BasketCapacity
                    || node.TargetLevel == collection.BasketUpgradeLevel + 1)
                && (priorityId != ProgressionSystem.UpgradeId.VacuumPower
                    || node.TargetLevel == 1))
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

            Button button = node.GetComponent<Button>();
            if (!CanPurchaseProgressionNode(button))
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

    private static IncubatorController FindIncubator()
    {
        return Object.FindFirstObjectByType<IncubatorController>();
    }

    private static CrosshatcherController FindCrosshatcher()
    {
        return Object.FindFirstObjectByType<CrosshatcherController>();
    }

    private static bool IsChickenCapReached()
    {
        return ChickenController.ActiveInstances.Count
            >= ChickenController.MaximumChickenCount;
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
        if (round == null || round.RoundEggsLaid < 3)
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

        int scaledTarget = 2 + processed / 12;
        desiredFeedPiles = Mathf.Clamp(
            scaledTarget,
            Mathf.Max(2, baseline),
            Mathf.Max(MaximumDesiredFeedPiles, baseline));
    }

    private int GetDesiredFeedPileCount()
    {
        int activeChickenCount = 0;
        var chickens = ChickenController.ActiveInstances;
        for (int index = 0; index < chickens.Count; index++)
        {
            if (chickens[index] != null && chickens[index].isActiveAndEnabled)
            {
                activeChickenCount++;
            }
        }

        int sharedPileTarget = Mathf.CeilToInt(
            activeChickenCount / (float)ChickensPerDesiredFoodPile);
        return Mathf.Min(desiredFeedPiles, sharedPileTarget);
    }

    private static int CountAvailableFoodPiles()
    {
        int available = 0;
        var piles = FoodPile.ActivePiles;

        for (int index = 0; index < piles.Count; index++)
        {
            if (piles[index] != null && piles[index].IsAvailable)
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
            || crosshatcher.OccupiedSlots >= 2)
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
        int availableChickenCount = 0;
        int bestBreed = int.MaxValue;
        float bestDistance = float.PositiveInfinity;
        Vector2 screenCenter = new Vector2(
            Screen.width * 0.5f,
            Screen.height * 0.5f);

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
                || !TryGetChickenPickupScreenPoint(
                    chicken,
                    target,
                    out Vector2 point)
                || !RayHitsChickenPickup(
                    camera,
                    point,
                    chicken,
                    target))
            {
                continue;
            }

            availableChickenCount++;
            int breed = (int)chicken.Breed;
            float distance = Vector2.SqrMagnitude(
                point - screenCenter);

            if (breed > bestBreed
                || breed == bestBreed
                    && distance >= bestDistance)
            {
                continue;
            }

            selectedChicken = chicken;
            selectedTarget = target;
            bestBreed = breed;
            bestDistance = distance;
        }

        int requiredAvailable = crosshatcher.OccupiedSlots > 0
            ? 1
            : 2;
        bool preservesWorkingFlock =
            crosshatcher.OccupiedSlots > 0
            || ChickenController.ActiveInstances.Count >= 4;
        return selectedChicken != null
            && availableChickenCount >= requiredAvailable
            && preservesWorkingFlock;
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
        out Vector3 dropPosition,
        out Vector2 screenPoint)
    {
        dropPosition = destination switch
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

    private IEnumerator ClickNamedButton(string objectName, string activity)
    {
        Button button = FindNamedButton(objectName);

        if (!IsUsable(button))
        {
            SetStatus($"{activity}  •  WAITING");
            yield return new WaitForSecondsRealtime(0.15f);
            yield break;
        }

        yield return ClickButton(button, activity);
    }

    private IEnumerator ClickButton(Button button, string activity)
    {
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
        yield return ClickScreen(point);
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

        for (int index = 0; index < buttons.Length; index++)
        {
            Button button = buttons[index];

            if (button != null
                && button.name == objectName
                && button.gameObject.scene.IsValid())
            {
                return button;
            }
        }

        return null;
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
        if (node == null || ProgressionSystem.Instance == null)
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
            ? $"TEST BOT  •  F8 STOP  •  ACTIONS {completedActions}\n{activity}"
            : $"TEST BOT\n{activity}";
    }

    private void OnValidate()
    {
        pointerSpeed = Mathf.Max(100f, pointerSpeed);
        pointerSpringFrequency = Mathf.Clamp(pointerSpringFrequency, 2f, 12f);
        pointerSpringDamping = Mathf.Clamp(pointerSpringDamping, 0.35f, 0.95f);
        pointerDwellTime = Mathf.Max(0f, pointerDwellTime);
        actionPause = Mathf.Max(0.05f, actionPause);
        vacuumHoldTime = Mathf.Max(0.1f, vacuumHoldTime);
        minimumFeedBags = Mathf.Max(0, minimumFeedBags);
        maximumShopPurchasesPerVisit = Mathf.Max(1, maximumShopPurchasesPerVisit);
    }
}
