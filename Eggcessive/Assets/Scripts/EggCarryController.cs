using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class EggCarryController : MonoBehaviour
{
    private static readonly int[] UpgradeCosts =
    {
        800, 1800, 3500, 6500, 11000, 17500, 27500, 45000, 75000
    };

    private static readonly string[] TierNames =
    {
        "Single Hand",
        "Basket I",
        "Basket II",
        "Basket III",
        "Vacuum I",
        "Vacuum II",
        "Vacuum III",
        "Collector Bot I",
        "Collector Bot II",
        "Smart Collector Bot"
    };

    private static readonly int[] BasketCapacities = { 3, 4, 5 };
    private static readonly float[] VacuumRanges = { 0.675f, 0.95f, 1.3f };
    private static readonly float[] VacuumPowers = { 0.5f, 0.825f, 1.25f };
    private static readonly float[] VacuumConeAngles = { 34f, 43f, 52f };
    private static readonly float[] RobotSpeeds = { 1.55f, 2.2f, 3f };
    private static readonly int[] RobotCapacities = { 3, 5, 8 };

    [Header("Pickup")]
    [SerializeField, Min(0.1f)] private float pickupDistance = 100f;
    [SerializeField] private LayerMask pickupLayers = ~0;

    [Header("Hand Carrying")]
    [SerializeField, Min(0f)] private float carryHeight = 0.3f;
    [SerializeField, Min(0.01f)] private float followSpeed = 25f;

    [Header("Collection Progression")]
    [SerializeField, Range(0, 9)] private int collectionLevel;
    [SerializeField] private GameObject[] basketPrefabs = null;
    [SerializeField] private GameObject[] vacuumPrefabs = null;
    [SerializeField] private GameObject[] robotPrefabs = null;

    [Header("Cursor Tool Follow")]
    [SerializeField, Min(0f)] private float toolHeight = 0.28f;
    [SerializeField, Min(0.01f)] private float toolSmoothTime = 0.1f;
    [SerializeField, Min(0f)] private float toolSideDistance = 0.62f;
    [SerializeField, Min(0f)] private float toolBackDistance = 0.18f;

    private Camera viewCamera;
    private ChickenEgg heldEgg;
    private Vector3 carryTarget;
    private Vector3 cursorGroundPosition;
    private Vector3 toolVelocity;
    private GameObject activeCursorTool;
    private Transform[] activeToolEggSlots = Array.Empty<Transform>();
    private EggCollectorRobot activeRobot;
    private EggContainer eggContainer;
    private IncubatorController incubator;
    private int basketEggCount;
    private int basketAnimationGeneration;
    private float nextVacuumScanTime;
    private readonly HashSet<ChickenEgg> vacuumInFlight = new HashSet<ChickenEgg>();
    private readonly HashSet<ChickenEgg> vacuumIncubatorInFlight =
        new HashSet<ChickenEgg>();

    public const int MaximumCollectionLevel = 9;
    public static EggCarryController Instance { get; private set; }
    public static event Action CollectionLevelChanged;

    public int CurrentCollectionLevel => collectionLevel;
    public string CurrentCollectionName => TierNames[collectionLevel];
    public bool HasCollectionUpgrade => collectionLevel < MaximumCollectionLevel;
    public int NextCollectionLevel =>
        Mathf.Min(collectionLevel + 1, MaximumCollectionLevel);
    public string NextCollectionName => TierNames[NextCollectionLevel];
    public int NextCollectionUpgradeCost =>
        HasCollectionUpgrade ? UpgradeCosts[collectionLevel] : 0;
    public string CurrentCollectionDetails => GetTierDetails(collectionLevel);
    public string NextCollectionDetails => GetTierDetails(NextCollectionLevel);
    public bool HasPendingCollection => vacuumInFlight.Count > 0;
    public int BasketEggCount => basketEggCount;
    public int CurrentBasketCapacity => BasketCapacity;
    public ChickenEgg HeldEgg => heldEgg;
    public float HandCarryHeight => carryHeight;

    private bool IsBasketMode => collectionLevel is >= 1 and <= 3;
    private bool IsVacuumMode => collectionLevel is >= 4 and <= 6;
    private bool IsRobotMode => collectionLevel is >= 7 and <= 9;
    private int BasketCapacity =>
        IsBasketMode ? BasketCapacities[collectionLevel - 1] : 0;

    private void Awake()
    {
        Instance = this;
        viewCamera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        RoundSystem.PhaseChanged += HandleRoundPhaseChanged;
    }

    private void Start()
    {
        eggContainer = EggContainer.Instance != null
            ? EggContainer.Instance
            : FindFirstObjectByType<EggContainer>(FindObjectsInactive.Include);
        incubator = FindFirstObjectByType<IncubatorController>(
            FindObjectsInactive.Include);
        ApplyCollectionLevel();
    }

    private void OnDisable()
    {
        RoundSystem.PhaseChanged -= HandleRoundPhaseChanged;
        ReleaseEgg();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        bool roundActive = RoundSystem.Instance == null
            || RoundSystem.Instance.IsRoundInProgress;
        SetCursorToolVisible(roundActive && !FoodShopController.IsPlacementActive);

        if (!roundActive)
        {
            ReleaseEgg();
            return;
        }

        if (FoodShopController.IsPlacementActive)
        {
            ReleaseEgg();
            return;
        }

        Mouse mouse = GameplayTestBot.PointerMouse;

        if (mouse == null)
        {
            return;
        }

        Vector2 pointerPosition = mouse.position.ReadValue();
        UpdateCursorGround(pointerPosition);
        UpdateCursorTool();

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            ReleaseEgg();
            return;
        }

        if (IsBasketMode)
        {
            UpdateBasket(pointerPosition, mouse);
        }
        else if (IsVacuumMode)
        {
            UpdateVacuum(mouse);
        }
        else if (!IsRobotMode)
        {
            UpdateHand(pointerPosition, mouse);
        }
    }

    private void FixedUpdate()
    {
        if (heldEgg != null)
        {
            heldEgg.MoveWhileHeld(carryTarget, followSpeed);
        }
    }

    public bool TryPurchaseNextCollectionLevel(out string message)
    {
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsSuppliesShopOpen)
        {
            message = "Collection upgrades are sold between rounds";
            return false;
        }

        if (!HasCollectionUpgrade)
        {
            message = "Maximum collection tier";
            return false;
        }

        int cost = NextCollectionUpgradeCost;

        if (!EggScoreHud.TrySpendCents(cost))
        {
            message = $"Need {FormatMoney(cost)}";
            return false;
        }

        collectionLevel++;
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
        message = $"{CurrentCollectionName} unlocked";
        return true;
    }

    public void InstallCollectionLevel(int level)
    {
        collectionLevel = Mathf.Clamp(level, 0, MaximumCollectionLevel);
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
    }

    public void CancelPointerInteraction()
    {
        ReleaseEgg();
    }

    private void UpdateHand(Vector2 pointerPosition, Mouse mouse)
    {
        if (heldEgg == null && mouse.leftButton.wasPressedThisFrame)
        {
            TryPickUpEgg(pointerPosition);
        }

        if (heldEgg == null)
        {
            return;
        }

        UpdateCarryTarget(pointerPosition);

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            ReleaseEgg();
        }
    }

    private void UpdateBasket(Vector2 pointerPosition, Mouse mouse)
    {
        if (!mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        if (TryGetContainerUnderPointer(pointerPosition, out EggContainer targetContainer))
        {
            EmptyBasket(targetContainer);
            return;
        }

        if (TryGetIncubatorUnderPointer(
                pointerPosition,
                out IncubatorController targetIncubator))
        {
            DepositBasketEggInIncubator(targetIncubator);
            return;
        }

        if (basketEggCount >= BasketCapacity)
        {
            return;
        }

        ChickenEgg egg = FindEggUnderPointer(pointerPosition);

        if (egg == null || !egg.TryCollectFromTool())
        {
            return;
        }

        int slotIndex = basketEggCount;
        basketEggCount++;
        StartCoroutine(AnimateEggIntoBasket(
            egg,
            slotIndex,
            basketAnimationGeneration));
    }

    private void EmptyBasket(EggContainer targetContainer)
    {
        if (basketEggCount <= 0 || targetContainer == null)
        {
            return;
        }

        int deposited = targetContainer.DepositEggs(basketEggCount);
        basketAnimationGeneration++;
        basketEggCount -= deposited;
        RefreshToolEggSlots();
    }

    private void DepositBasketEggInIncubator(IncubatorController targetIncubator)
    {
        if (basketEggCount <= 0 || targetIncubator == null)
        {
            return;
        }

        int deposited = targetIncubator.TryAcceptStoredEggs(1);
        basketAnimationGeneration++;
        basketEggCount -= deposited;
        RefreshToolEggSlots();
    }

    private IEnumerator AnimateEggIntoBasket(
        ChickenEgg egg,
        int slotIndex,
        int generation)
    {
        if (egg == null)
        {
            yield break;
        }

        foreach (Collider eggCollider in egg.GetComponentsInChildren<Collider>(true))
        {
            eggCollider.enabled = false;
        }

        Vector3 startPosition = egg.transform.position;
        Quaternion startRotation = egg.transform.rotation;
        Vector3 startScale = egg.transform.localScale;
        const float duration = 0.3f;
        float elapsed = 0f;

        while (egg != null
            && elapsed < duration
            && generation == basketAnimationGeneration
            && slotIndex < activeToolEggSlots.Length
            && activeToolEggSlots[slotIndex] != null)
        {
            elapsed += Time.deltaTime;
            Transform targetSlot = activeToolEggSlots[slotIndex];
            float progress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(elapsed / duration));
            egg.transform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, targetSlot.position, progress),
                Quaternion.Slerp(startRotation, targetSlot.rotation, progress));
            egg.transform.localScale = Vector3.Lerp(
                startScale,
                targetSlot.lossyScale,
                progress);
            yield return null;
        }

        if (egg != null)
        {
            Destroy(egg.gameObject);
        }

        if (generation == basketAnimationGeneration
            && slotIndex < basketEggCount
            && slotIndex < activeToolEggSlots.Length
            && activeToolEggSlots[slotIndex] != null)
        {
            activeToolEggSlots[slotIndex].gameObject.SetActive(true);
        }
    }

    private void UpdateVacuum(Mouse mouse)
    {
        bool routeToIncubator = mouse.rightButton.isPressed;
        bool routeToContainer = mouse.leftButton.isPressed;

        if (!routeToContainer && !routeToIncubator)
        {
            return;
        }

        if (routeToIncubator
            && (incubator == null
                || !incubator.isActiveAndEnabled
                || incubator.AvailableCapacity
                    <= vacuumIncubatorInFlight.Count))
        {
            return;
        }

        int tierIndex = collectionLevel - 4;
        float range = VacuumRanges[tierIndex];
        float halfAngle = VacuumConeAngles[tierIndex];
        Vector3 origin = activeCursorTool != null
            ? activeCursorTool.transform.position
            : cursorGroundPosition + Vector3.up * toolHeight;
        Vector3 direction = cursorGroundPosition - origin;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.ProjectOnPlane(viewCamera.transform.up, Vector3.up);
        }

        direction.Normalize();
        int windSourceId = activeCursorTool != null
            ? activeCursorTool.GetInstanceID()
            : GetInstanceID();
        GlobalWind.SetTransientInfluence(
            windSourceId,
            origin + direction * (range * 0.45f),
            -direction,
            range * 0.9f,
            Mathf.Max(0.55f, VacuumPowers[tierIndex] * 1.5f));

        if (Time.time < nextVacuumScanTime)
        {
            return;
        }

        nextVacuumScanTime = Time.time + 0.075f;
        float minimumDot = Mathf.Cos(halfAngle * Mathf.Deg2Rad);
        var eggs = ChickenEgg.ActiveInstances;

        for (int index = eggs.Count - 1; index >= 0; index--)
        {
            ChickenEgg egg = eggs[index];

            if (egg == null
                || egg.IsHeld
                || egg.IsCollected
                || vacuumInFlight.Contains(egg))
            {
                continue;
            }

            Vector3 toEgg = egg.transform.position - origin;
            toEgg.y = 0f;
            float distance = toEgg.magnitude;

            if (distance > range
                || distance < 0.02f
                || Vector3.Dot(direction, toEgg / distance) < minimumDot)
            {
                continue;
            }

            vacuumInFlight.Add(egg);

            if (routeToIncubator)
            {
                vacuumIncubatorInFlight.Add(egg);
            }

            StartCoroutine(VacuumEgg(
                egg,
                VacuumPowers[tierIndex],
                routeToIncubator));

            if (routeToIncubator
                && incubator.AvailableCapacity
                    <= vacuumIncubatorInFlight.Count)
            {
                break;
            }
        }
    }

    private IEnumerator VacuumEgg(
        ChickenEgg egg,
        float power,
        bool routeToIncubator)
    {
        if (egg == null || !egg.BeginCarry())
        {
            vacuumInFlight.Remove(egg);
            vacuumIncubatorInFlight.Remove(egg);
            yield break;
        }

        Vector3 start = egg.transform.position;
        float suctionDuration = 0.48f / Mathf.Max(0.1f, power);
        float elapsed = 0f;

        while (egg != null && elapsed < suctionDuration)
        {
            elapsed += Time.deltaTime;
            Vector3 nozzle = activeCursorTool != null
                ? activeCursorTool.transform.position + Vector3.up * 0.08f
                : cursorGroundPosition + Vector3.up * toolHeight;
            float progress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(elapsed / suctionDuration));
            egg.transform.position = Vector3.Lerp(start, nozzle, progress);
            yield return null;
        }

        if (egg == null)
        {
            vacuumInFlight.Remove(egg);
            vacuumIncubatorInFlight.Remove(egg);
            yield break;
        }

        Vector3 routeStart = egg.transform.position;
        Vector3 destination = routeToIncubator && incubator != null
            ? incubator.DepositPosition
            : eggContainer != null
                ? eggContainer.DepositPosition
                : routeStart;
        elapsed = 0f;
        const float routeDuration = 0.24f;

        while (egg != null && elapsed < routeDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(elapsed / routeDuration));
            Vector3 arc = Vector3.up * Mathf.Sin(progress * Mathf.PI) * 0.35f;
            egg.transform.position = Vector3.Lerp(routeStart, destination, progress) + arc;
            yield return null;
        }

        if (egg != null
            && (RoundSystem.Instance == null
                || RoundSystem.Instance.IsRoundAcceptingEggs))
        {
            int deposited = routeToIncubator && incubator != null
                ? incubator.TryAcceptStoredEggs(1)
                : eggContainer != null
                    ? eggContainer.DepositEggs(1)
                    : 0;

            if (deposited > 0 && egg.TryCollectFromTool())
            {
                Destroy(egg.gameObject);
            }
            else
            {
                egg.Release(egg.transform.position);
            }
        }
        else if (egg != null)
        {
            egg.Release(egg.transform.position);
        }

        vacuumInFlight.Remove(egg);
        vacuumIncubatorInFlight.Remove(egg);
    }

    private void ApplyCollectionLevel()
    {
        ReleaseEgg();
        basketAnimationGeneration++;

        if (activeCursorTool != null)
        {
            Destroy(activeCursorTool);
            activeCursorTool = null;
        }

        if (activeRobot != null)
        {
            activeRobot.FinalizeRound();
            Destroy(activeRobot.gameObject);
            activeRobot = null;
        }

        activeToolEggSlots = Array.Empty<Transform>();

        if (IsBasketMode)
        {
            activeCursorTool = InstantiateTierPrefab(
                basketPrefabs,
                collectionLevel - 1,
                "basket");
            CacheToolEggSlots();
            RefreshToolEggSlots();
        }
        else if (IsVacuumMode)
        {
            activeCursorTool = InstantiateTierPrefab(
                vacuumPrefabs,
                collectionLevel - 4,
                "vacuum");
        }
        else if (IsRobotMode)
        {
            SpawnRobot();
        }

        bool shouldShow = RoundSystem.Instance != null
            && RoundSystem.Instance.IsRoundInProgress;
        SetCursorToolVisible(shouldShow);
    }

    private GameObject InstantiateTierPrefab(
        GameObject[] prefabs,
        int index,
        string toolName)
    {
        if (prefabs == null
            || index < 0
            || index >= prefabs.Length
            || prefabs[index] == null)
        {
            Debug.LogError($"Missing authored {toolName} tier {index + 1} prefab.", this);
            return null;
        }

        GameObject tool = Instantiate(prefabs[index]);
        tool.name = $"{prefabs[index].name} (Active)";
        tool.transform.position = cursorGroundPosition + Vector3.up * toolHeight;
        return tool;
    }

    private void SpawnRobot()
    {
        int index = collectionLevel - 7;

        if (robotPrefabs == null
            || index < 0
            || index >= robotPrefabs.Length
            || robotPrefabs[index] == null)
        {
            Debug.LogError($"Missing authored robot tier {index + 1} prefab.", this);
            return;
        }

        Vector3 spawnPosition = eggContainer != null
            ? eggContainer.DepositPosition + Vector3.right * 0.45f
            : Vector3.zero;
        GameObject robotObject = Instantiate(
            robotPrefabs[index],
            spawnPosition,
            Quaternion.identity);
        activeRobot = robotObject.GetComponent<EggCollectorRobot>();

        if (activeRobot == null)
        {
            Debug.LogError($"{robotPrefabs[index].name} needs an EggCollectorRobot.", robotObject);
            return;
        }

        activeRobot.Configure(
            eggContainer,
            incubator,
            RobotSpeeds[index],
            RobotCapacities[index],
            index == 2);
    }

    private void UpdateCursorGround(Vector2 pointerPosition)
    {
        Ray ray = viewCamera.ScreenPointToRay(pointerPosition);
        Plane ground = new Plane(Vector3.up, Vector3.zero);

        if (ground.Raycast(ray, out float distance))
        {
            cursorGroundPosition = ray.GetPoint(distance);
        }
    }

    private void UpdateCursorTool()
    {
        if (activeCursorTool == null)
        {
            return;
        }

        Vector3 screenRight = Vector3.ProjectOnPlane(
            viewCamera.transform.right,
            Vector3.up).normalized;
        Vector3 screenUp = Vector3.ProjectOnPlane(
            viewCamera.transform.up,
            Vector3.up).normalized;
        float offsetScale = IsBasketMode ? 0.5f : 1f;
        Vector3 target = cursorGroundPosition
            + screenRight * (toolSideDistance * offsetScale)
            - screenUp * (toolBackDistance * offsetScale);
        target.y = toolHeight;
        activeCursorTool.transform.position = Vector3.SmoothDamp(
            activeCursorTool.transform.position,
            target,
            ref toolVelocity,
            toolSmoothTime);

        Vector3 lookDirection = cursorGroundPosition - activeCursorTool.transform.position;
        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude > 0.001f)
        {
            activeCursorTool.transform.rotation = Quaternion.Slerp(
                activeCursorTool.transform.rotation,
                Quaternion.LookRotation(lookDirection, Vector3.up),
                1f - Mathf.Exp(-14f * Time.deltaTime));
        }
    }

    private void TryPickUpEgg(Vector2 pointerPosition)
    {
        ChickenEgg nearestEgg = FindEggUnderPointer(pointerPosition);

        if (nearestEgg == null || !nearestEgg.BeginCarry())
        {
            return;
        }

        heldEgg = nearestEgg;
        carryTarget = heldEgg.transform.position;
        UpdateCarryTarget(pointerPosition);
    }

    private ChickenEgg FindEggUnderPointer(Vector2 pointerPosition)
    {
        Ray ray = viewCamera.ScreenPointToRay(pointerPosition);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            pickupDistance,
            pickupLayers,
            QueryTriggerInteraction.Ignore);
        ChickenEgg nearestEgg = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            ChickenEgg egg = hit.collider.GetComponentInParent<ChickenEgg>();

            if (egg == null
                || egg.IsHeld
                || egg.IsCollected
                || hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestEgg = egg;
            nearestDistance = hit.distance;
        }

        return nearestEgg;
    }

    private bool TryGetContainerUnderPointer(
        Vector2 pointerPosition,
        out EggContainer targetContainer)
    {
        Ray ray = viewCamera.ScreenPointToRay(pointerPosition);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            pickupDistance,
            pickupLayers,
            QueryTriggerInteraction.Collide);
        targetContainer = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            EggContainer candidate = hit.collider.GetComponentInParent<EggContainer>();

            if (candidate != null && hit.distance < nearestDistance)
            {
                targetContainer = candidate;
                nearestDistance = hit.distance;
            }
        }

        return targetContainer != null;
    }

    private bool TryGetIncubatorUnderPointer(
        Vector2 pointerPosition,
        out IncubatorController targetIncubator)
    {
        Ray ray = viewCamera.ScreenPointToRay(pointerPosition);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            pickupDistance,
            pickupLayers,
            QueryTriggerInteraction.Collide);
        targetIncubator = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            IncubatorController candidate =
                hit.collider.GetComponentInParent<IncubatorController>();

            if (candidate != null
                && candidate.isActiveAndEnabled
                && hit.distance < nearestDistance)
            {
                targetIncubator = candidate;
                nearestDistance = hit.distance;
            }
        }

        return targetIncubator != null;
    }

    private void UpdateCarryTarget(Vector2 pointerPosition)
    {
        Ray ray = viewCamera.ScreenPointToRay(pointerPosition);
        Plane carryPlane = new Plane(Vector3.up, new Vector3(0f, carryHeight, 0f));

        if (carryPlane.Raycast(ray, out float distance))
        {
            carryTarget = ray.GetPoint(distance);
        }
    }

    private void ReleaseEgg()
    {
        if (heldEgg == null)
        {
            return;
        }

        heldEgg.Release(carryTarget);
        heldEgg = null;
    }

    private void HandleRoundPhaseChanged(RoundSystem.RoundPhase phase)
    {
        if (phase != RoundSystem.RoundPhase.Settling)
        {
            return;
        }

        ReleaseEgg();

        if (basketEggCount > 0)
        {
            basketAnimationGeneration++;
            basketEggCount = 0;
            RefreshToolEggSlots();
        }

        activeRobot?.FinalizeRound();
    }

    private void SetCursorToolVisible(bool visible)
    {
        if (activeCursorTool != null && activeCursorTool.activeSelf != visible)
        {
            activeCursorTool.SetActive(visible);
        }
    }

    private void CacheToolEggSlots()
    {
        if (activeCursorTool == null)
        {
            activeToolEggSlots = Array.Empty<Transform>();
            return;
        }

        var slots = new List<Transform>();

        foreach (Transform child in activeCursorTool.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("Egg Slot", StringComparison.Ordinal))
            {
                slots.Add(child);
            }
        }

        activeToolEggSlots = slots.ToArray();
    }

    private void RefreshToolEggSlots()
    {
        for (int index = 0; index < activeToolEggSlots.Length; index++)
        {
            activeToolEggSlots[index].gameObject.SetActive(index < basketEggCount);
        }
    }

    public static string GetTierDetails(int level)
    {
        level = Mathf.Clamp(level, 0, MaximumCollectionLevel);

        if (level == 0)
        {
            return "Pick up and carry one egg at a time";
        }

        if (level <= 3)
        {
            return $"CLICK EGGS > CONTAINER / INCUBATOR  |  CAP {BasketCapacities[level - 1]}";
        }

        if (level <= 6)
        {
            int index = level - 4;
            return $"LMB CASH / RMB INCUBATE  |  {VacuumRanges[index]:0.##}m  |  " +
                $"{VacuumPowers[index]:0.##}x POWER";
        }

        int robotIndex = level - 7;
        return (robotIndex == 2 ? "SMART INCUBATOR  |  " : "AUTOMATIC  |  ") +
            $"CAP {RobotCapacities[robotIndex]}  |  " +
            $"{RobotSpeeds[robotIndex]:0.##} SPEED";
    }

    private static string FormatMoney(int cents)
    {
        return $"${cents / 100}.{cents % 100:D2}";
    }

    private void OnValidate()
    {
        pickupDistance = Mathf.Max(0.1f, pickupDistance);
        carryHeight = Mathf.Max(0f, carryHeight);
        followSpeed = Mathf.Max(0.01f, followSpeed);
        collectionLevel = Mathf.Clamp(collectionLevel, 0, MaximumCollectionLevel);
        toolHeight = Mathf.Max(0f, toolHeight);
        toolSmoothTime = Mathf.Max(0.01f, toolSmoothTime);
        toolSideDistance = Mathf.Max(0f, toolSideDistance);
        toolBackDistance = Mathf.Max(0f, toolBackDistance);
    }
}
