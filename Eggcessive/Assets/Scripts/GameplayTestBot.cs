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

    private static readonly string[] ShopUpgradeButtonNames =
    {
        "Upgrade Collection",
        "Upgrade Incubator",
        "Upgrade Feed"
    };

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

    [Header("Operation")]
    [SerializeField] private bool startEnabled = false;
    [SerializeField, Min(100f)] private float pointerSpeed = 1350f;
    [SerializeField, Min(0f)] private float pointerDwellTime = 0.08f;
    [SerializeField, Min(0.05f)] private float actionPause = 0.2f;
    [SerializeField, Min(0.1f)] private float vacuumHoldTime = 0.85f;

    [Header("Strategy")]
    [SerializeField, Min(0)] private int minimumFeedBags = 1;
    [SerializeField, Min(1)] private int maximumShopPurchasesPerVisit = 12;
    [SerializeField, Min(1)] private int incubatorEggInterval = 3;

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
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private bool cursorStateCaptured;
    private bool isRunning;
    private Mouse physicalMouse;

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
        isRunning = true;
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

        if (phase == RoundSystem.RoundPhase.Results)
        {
            resultsSkipSent = false;
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

        if (!HasAvailableFood()
            && foodShop != null
            && foodShop.OwnedFoodCount > 0)
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

        int level = collection.CurrentCollectionLevel;

        if (level >= 7)
        {
            SetStatus($"ROUND  •  SUPERVISING {collection.CurrentCollectionName.ToUpperInvariant()}");
            yield return new WaitForSecondsRealtime(0.25f);
            yield break;
        }

        if (level >= 4)
        {
            yield return UseVacuum();
        }
        else if (level >= 1)
        {
            yield return UseBasket(collection);
        }
        else
        {
            yield return UseHand();
        }
    }

    private IEnumerator UseHand()
    {
        if (!TryFindClickableEgg(out ChickenEgg egg, out Vector2 eggPoint))
        {
            SetStatus("ROUND  •  WAITING FOR EGGS");
            yield return new WaitForSecondsRealtime(0.18f);
            yield break;
        }

        bool incubate = ShouldUseIncubator();
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
        yield return MovePointer(eggPoint);
        QueueMouseButton(MouseButton.Left, true);
        float pickupWait = 0f;

        while (collection.HeldEgg != egg && pickupWait < 0.35f)
        {
            pickupWait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (collection.HeldEgg != egg)
        {
            QueueMouseButton(MouseButton.Left, false);
            yield return new WaitForSecondsRealtime(actionPause);
            yield break;
        }

        yield return MovePointer(destinationPoint);
        float arrivalWait = 0f;

        while (egg != null
            && collection.HeldEgg == egg
            && Vector3.SqrMagnitude(egg.transform.position - dropPosition) > 0.0016f
            && arrivalWait < 0.75f)
        {
            arrivalWait += Time.unscaledDeltaTime;
            yield return null;
        }

        QueueMouseButton(MouseButton.Left, false);
        collectionActionCount++;
        completedActions++;
        yield return new WaitForSecondsRealtime(Mathf.Max(actionPause, 0.3f));
    }

    private IEnumerator UseBasket(EggCarryController collection)
    {
        bool incubatorAvailable = CanUseIncubator();

        if (collection.BasketEggCount > 0
            && incubatorAvailable
            && collectionActionCount % incubatorEggInterval == 0)
        {
            SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  •  INCUBATING ONE");
            yield return ClickWorldComponent(FindIncubator());
            collectionActionCount++;
            yield break;
        }

        if (collection.BasketEggCount >= collection.CurrentBasketCapacity
            || (collection.BasketEggCount > 0
                && !TryFindClickableEgg(out _, out _)))
        {
            SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  •  CASHING IN");
            yield return ClickWorldComponent(EggContainer.Instance);
            yield break;
        }

        if (!TryFindClickableEgg(out ChickenEgg egg, out Vector2 eggPoint))
        {
            SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  •  WAITING");
            yield return new WaitForSecondsRealtime(0.18f);
            yield break;
        }

        SetStatus($"BASKET {collection.BasketEggCount}/{collection.CurrentBasketCapacity}  •  COLLECTING");
        yield return ClickScreen(eggPoint);
    }

    private IEnumerator UseVacuum()
    {
        if (!TryFindClickableEgg(out ChickenEgg egg, out Vector2 eggPoint))
        {
            SetStatus("VACUUM  •  WAITING FOR EGGS");
            yield return new WaitForSecondsRealtime(0.16f);
            yield break;
        }

        bool incubate = ShouldUseIncubator();
        SetStatus($"VACUUM  •  {(incubate ? "RIGHT SUCK TO INCUBATOR" : "CASH SUCK")}");
        yield return MovePointer(eggPoint);
        MouseButton button = incubate ? MouseButton.Right : MouseButton.Left;
        QueueMouseButton(button, true);
        yield return new WaitForSecondsRealtime(vacuumHoldTime);
        QueueMouseButton(button, false);
        collectionActionCount++;
        completedActions++;
        yield return new WaitForSecondsRealtime(actionPause);
    }

    private IEnumerator TryPlaceFood()
    {
        Vector2 viewport = FoodPlacementViewportPoints[
            foodPlacementAttempt % FoodPlacementViewportPoints.Length];
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
        FoodShopController foodShop = FoodShopController.Instance;

        if (foodShop != null && foodShop.OwnedFoodCount < minimumFeedBags)
        {
            Button buyFeed = FindNamedButton("Buy Feed");

            if (IsUsable(buyFeed))
            {
                shopPurchaseCount++;
                yield return ClickButton(buyFeed, "SHOP  •  BUYING FEED BAG");
                yield break;
            }
        }

        if (shopPurchaseCount < maximumShopPurchasesPerVisit)
        {
            for (int offset = 0; offset < ShopUpgradeButtonNames.Length; offset++)
            {
                int index = (shopUpgradeCursor + offset) % ShopUpgradeButtonNames.Length;
                Button upgrade = FindNamedButton(ShopUpgradeButtonNames[index]);

                if (!IsUsable(upgrade))
                {
                    continue;
                }

                shopUpgradeCursor = (index + 1) % ShopUpgradeButtonNames.Length;
                shopPurchaseCount++;
                yield return ClickButton(
                    upgrade,
                    $"SHOP  •  {ShopUpgradeButtonNames[index].ToUpperInvariant()}");
                yield break;
            }
        }

        yield return ClickNamedButton("Done Shopping", "SHOP  •  DONE");
    }

    private bool ShouldUseIncubator()
    {
        return CanUseIncubator()
            && collectionActionCount % incubatorEggInterval == 0;
    }

    private bool CanUseIncubator()
    {
        IncubatorController incubator = FindIncubator();
        return incubator != null
            && incubator.isActiveAndEnabled
            && incubator.AvailableCapacity > 0;
    }

    private static IncubatorController FindIncubator()
    {
        return Object.FindFirstObjectByType<IncubatorController>();
    }

    private static bool HasAvailableFood()
    {
        var piles = FoodPile.ActivePiles;

        for (int index = 0; index < piles.Count; index++)
        {
            if (piles[index] != null && piles[index].IsAvailable)
            {
                return true;
            }
        }

        return false;
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

            if (distance >= bestDistance)
            {
                continue;
            }

            selectedEgg = egg;
            selectedPoint = point;
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

    private IEnumerator MovePointer(Vector2 destination)
    {
        Mouse mouse = automationInputMouse;

        if (mouse == null)
        {
            yield break;
        }

        destination.x = Mathf.Clamp(destination.x, 1f, Screen.width - 1f);
        destination.y = Mathf.Clamp(destination.y, 1f, Screen.height - 1f);
        Vector2 current = mouse.position.ReadValue();

        while (Vector2.SqrMagnitude(destination - current) > 4f)
        {
            current = Vector2.MoveTowards(
                current,
                destination,
                pointerSpeed * Mathf.Max(0.001f, Time.unscaledDeltaTime));
            SetPointerPosition(current);
            yield return null;
        }

        SetPointerPosition(destination);
        yield return null;
    }

    private void SetPointerPosition(Vector2 position)
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

    private static bool IsUsable(Button button)
    {
        return button != null
            && button.isActiveAndEnabled
            && button.gameObject.activeInHierarchy
            && button.interactable;
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
        pointerDwellTime = Mathf.Max(0f, pointerDwellTime);
        actionPause = Mathf.Max(0.05f, actionPause);
        vacuumHoldTime = Mathf.Max(0.1f, vacuumHoldTime);
        minimumFeedBags = Mathf.Max(0, minimumFeedBags);
        maximumShopPurchasesPerVisit = Mathf.Max(1, maximumShopPurchasesPerVisit);
        incubatorEggInterval = Mathf.Max(1, incubatorEggInterval);
    }
}
