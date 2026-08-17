using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class EggCarryController : MonoBehaviour
{
    public enum PlayerTool
    {
        Hand,
        Collection
    }

    private static readonly int[] UpgradeCosts =
    {
        800, 1800, 4200, 8000, 74000,
        400000, 4000000,
        120000, 1550000, 6400000
    };

    private static readonly string[] TierNames =
    {
        "Single Hand",
        "Basket I",
        "Basket II",
        "Basket III",
        "Basket IV",
        "Vacuum I",
        "Vacuum II",
        "Vacuum III",
        "Collector Bot I",
        "Collector Bot II",
        "Smart Collector Bot"
    };

    private static readonly int[] BasketCapacities = { 3, 4, 5, 6 };
    private static readonly float[] BasketReachRadii =
    {
        0f, 0.2f, 0.4f, 0.6f, 0.8f
    };
    private static readonly float[] VacuumRanges = { 0.675f, 0.95f, 1.3f };
    private static readonly float[] VacuumPowers = { 0.5f, 0.825f, 1.25f };
    private static readonly float[] VacuumConeAngles = { 34f, 43f, 52f };
    private const float VacuumSuctionBaseDuration = 0.3f;
    private const float BasketLobDuration = 0.22f;
    private const float BasketMinimumLobHeight = 0.18f;
    private const float BasketMaximumLobHeight = 0.42f;
    private static readonly float[] RobotSpeeds =
    {
        2.4f, 3f, 3.6f, 4.2f, 4.8f, 5.4f
    };
    private static readonly int[] RobotCapacities =
    {
        9, 18, 27, 36, 45, 54
    };

    [Header("Pickup")]
    [SerializeField, Min(0.1f)] private float pickupDistance = 100f;
    [Tooltip(
        "World-space radius around the pointer ray used when the exact ray does not hit a hand pickup target.")]
    [SerializeField, Min(0f)] private float handPickupRadius = 0.08f;
    [SerializeField] private LayerMask pickupLayers = ~0;

    [Header("Hand Carrying")]
    [SerializeField, Min(0f)] private float carryHeight = 0.3f;
    [SerializeField, Min(0.01f)] private float followSpeed = 25f;
    [SerializeField] private PlayerTool selectedTool = PlayerTool.Hand;

    [Header("Collection Progression")]
    [SerializeField, Range(0, MaximumCollectionLevel)] private int collectionLevel;
    [SerializeField, Range(0, MaximumBasketLevel)] private int basketUpgradeLevel;
    [SerializeField, Range(0, MaximumBasketReachLevel)]
    private int basketReachLevel;
    [SerializeField, Range(0, 3)] private int vacuumPowerLevel;
    [SerializeField, Range(0, 3)] private int vacuumRangeLevel;
    [SerializeField] private bool robotUnlocked;
    [SerializeField, Range(0, MaximumRobotLevel)] private int robotSpeedLevel;
    [SerializeField, Range(0, MaximumRobotLevel)] private int robotCapacityLevel;
    [SerializeField, Range(0, EggCollectorRobot.ChickenArmsSmartnessLevel)]
    private int robotSmartnessLevel;
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
    private ChickenEgg hoveredHandEgg;
    private ChickenController hoveredHandChicken;
    private ChickenController heldChicken;
    private readonly PickupOutlinePreview pickupOutline =
        new PickupOutlinePreview();
    private Vector3 carryTarget;
    private Vector3 cursorGroundPosition;
    private Vector3 toolVelocity;
    private GameObject activeCursorTool;
    private Transform activeVacuumNozzle;
    private GameObject basketFullIndicator;
    private Transform[] activeToolEggSlots = Array.Empty<Transform>();
    private EggCollectorRobot activeRobot;
    private EggContainer eggContainer;
    private IncubatorController incubator;
    private int basketEggCount;
    private readonly List<int> basketEggValues = new List<int>();
    private readonly List<float> basketEggWeights = new List<float>();
    private readonly List<ChickenEgg.EggType> basketEggTypes =
        new List<ChickenEgg.EggType>();
    private int basketAnimationGeneration;
    private float nextVacuumScanTime;
    private readonly HashSet<ChickenEgg> vacuumInFlight = new HashSet<ChickenEgg>();
    private readonly HashSet<ChickenEgg> vacuumIncubatorInFlight =
        new HashSet<ChickenEgg>();
    private bool automationPreservesRareEggs;

    public const int MaximumBasketLevel = 4;
    public const int MaximumBasketReachLevel = 4;
    public const int MaximumCollectionLevel = 10;
    public const int MaximumRobotLevel = 6;
    public static EggCarryController Instance { get; private set; }
    public static event Action CollectionLevelChanged;
    public static event Action ToolSelectionChanged;

    public int CurrentCollectionLevel => GetLegacyCollectionLevel();
    public string CurrentCollectionName => HasRobot && HasVacuum
        ? "Vacuum + Collector Bot"
        : TierNames[CurrentCollectionLevel];
    public bool HasCollectionUpgrade => CurrentCollectionLevel < MaximumCollectionLevel;
    public int NextCollectionLevel =>
        Mathf.Min(CurrentCollectionLevel + 1, MaximumCollectionLevel);
    public string NextCollectionName => TierNames[NextCollectionLevel];
    public int NextCollectionUpgradeCost =>
        HasCollectionUpgrade ? UpgradeCosts[CurrentCollectionLevel] : 0;
    public string CurrentCollectionDetails => GetTierDetails(CurrentCollectionLevel);
    public string NextCollectionDetails => GetTierDetails(NextCollectionLevel);
    public bool HasPendingCollection => vacuumInFlight.Count > 0;
    public bool HasVacuumIncubatorCapacity =>
        incubator != null
        && incubator.isActiveAndEnabled
        && incubator.AvailableCapacity > vacuumIncubatorInFlight.Count;
    public int BasketEggCount => basketEggCount;
    public bool BasketContainsRareEggs =>
        basketEggTypes.Exists(type => type != ChickenEgg.EggType.Common);
    public int CurrentBasketCapacity => BasketCapacity;
    public ChickenEgg HeldEgg => heldEgg;
    public bool IsHoveringGrabbableEgg =>
        hoveredHandEgg != null
        && !hoveredHandEgg.IsHeld
        && !hoveredHandEgg.IsCollected
        && hoveredHandEgg.IsGroundedForPickupPreview;
    public bool IsHoveringGrabbableChicken =>
        hoveredHandChicken != null
        && hoveredHandChicken.CanBePickedUp;
    public ChickenController HeldChicken => heldChicken;
    public float HandCarryHeight => carryHeight;
    public int BasketUpgradeLevel => basketUpgradeLevel;
    public int BasketReachLevel => basketReachLevel;
    public float BasketReachRadius =>
        BasketReachRadii[Mathf.Clamp(
            basketReachLevel,
            0,
            MaximumBasketReachLevel)];
    public int VacuumPowerLevel => vacuumPowerLevel;
    public int VacuumRangeLevel => vacuumRangeLevel;
    public bool HasVacuum => vacuumPowerLevel > 0;
    public float CurrentVacuumRange
    {
        get
        {
            if (!HasVacuum)
            {
                return 0f;
            }

            int rangeIndex = Mathf.Clamp(
                (vacuumRangeLevel > 0
                    ? vacuumRangeLevel
                    : vacuumPowerLevel) - 1,
                0,
                VacuumRanges.Length - 1);
            return VacuumRanges[rangeIndex];
        }
    }
    public bool HasRobot => robotUnlocked;
    public PlayerTool SelectedTool => selectedTool;
    public bool IsCollectionToolUnlocked => IsBasketMode || IsVacuumMode;
    public string CollectionToolName => HasVacuum ? "VACUUM" : "BASKET";
    public int RobotSpeedLevel => robotSpeedLevel;
    public int RobotCapacityLevel => robotCapacityLevel;
    public int RobotSmartnessLevel => robotSmartnessLevel;

    public static float GetRobotSpeed(int level)
    {
        return RobotSpeeds[Mathf.Clamp(level, 1, MaximumRobotLevel) - 1];
    }

    public static int GetRobotCapacity(int level)
    {
        return RobotCapacities[Mathf.Clamp(level, 1, MaximumRobotLevel) - 1];
    }

    private bool IsBasketMode => basketUpgradeLevel > 0 && !HasVacuum;
    private bool IsVacuumMode => HasVacuum;
    private bool IsRobotMode => HasRobot;
    private int BasketCapacity =>
        IsBasketMode ? BasketCapacities[basketUpgradeLevel - 1] : 0;

    private void Awake()
    {
        Instance = this;
        viewCamera = GetComponent<Camera>();
        if (GetComponent<PlacementTargetGuideController>() == null)
        {
            gameObject.AddComponent<PlacementTargetGuideController>();
        }
        basketReachLevel = Mathf.Clamp(
            basketReachLevel,
            0,
            MaximumBasketReachLevel);
        MigrateLegacyCollectionLevel();
    }

    private void OnEnable()
    {
        RoundSystem.PhaseChanged += HandleRoundPhaseChanged;
        EggContainer.FocusedContainerChanged += HandleFocusedContainerChanged;
    }

    private void Start()
    {
        eggContainer = EggContainer.Instance != null
            ? EggContainer.Instance
            : FindFirstObjectByType<EggContainer>(FindObjectsInactive.Include);
        incubator = PenExpansionManager.Instance != null
            ? PenExpansionManager.Instance.GetFocusedIncubator()
            : FindFirstObjectByType<IncubatorController>(
                FindObjectsInactive.Include);
        ApplyCollectionLevel();
    }

    private void OnDisable()
    {
        hoveredHandEgg = null;
        hoveredHandChicken = null;
        RoundSystem.PhaseChanged -= HandleRoundPhaseChanged;
        EggContainer.FocusedContainerChanged -= HandleFocusedContainerChanged;
        RoundSystem.Instance?.SetVacuumSfxActive(false);
        pickupOutline.Clear();
        ReleaseHandItems();
    }

    private void HandleFocusedContainerChanged(EggContainer container)
    {
        eggContainer = container;
        incubator = PenExpansionManager.Instance != null
            ? PenExpansionManager.Instance.GetFocusedIncubator()
            : incubator;
    }

    private void OnDestroy()
    {
        pickupOutline.Dispose();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        hoveredHandEgg = null;
        hoveredHandChicken = null;
        bool roundActive = RoundSystem.Instance == null
            || RoundSystem.Instance.IsRoundInProgress;
        bool collectionSelected = selectedTool == PlayerTool.Collection
            && IsCollectionToolUnlocked;
        SetCursorToolVisible(
            roundActive
            && !FoodShopController.IsPlacementActive
            && collectionSelected);

        if (!roundActive)
        {
            RoundSystem.Instance?.SetVacuumSfxActive(false);
            pickupOutline.Clear();
            ReleaseHandItems();
            return;
        }

        if (FoodShopController.IsPlacementActive)
        {
            RoundSystem.Instance?.SetVacuumSfxActive(false);
            pickupOutline.Clear();
            ReleaseHandItems();
            return;
        }

        Mouse mouse = GameplayTestBot.PointerMouse;

        if (mouse == null)
        {
            RoundSystem.Instance?.SetVacuumSfxActive(false);
            pickupOutline.Clear();
            return;
        }

        Vector2 pointerPosition = mouse.position.ReadValue();
        UpdateCursorGround(pointerPosition);
        UpdateCursorTool();

        if (EventSystem.current != null
            && EventSystem.current.IsPointerOverGameObject()
            && heldEgg == null
            && heldChicken == null)
        {
            RoundSystem.Instance?.SetVacuumSfxActive(false);
            pickupOutline.Clear();
            return;
        }

        if (selectedTool == PlayerTool.Hand || !IsCollectionToolUnlocked)
        {
            RoundSystem.Instance?.SetVacuumSfxActive(false);
            UpdateHand(pointerPosition, mouse);
        }
        else if (IsBasketMode)
        {
            RoundSystem.Instance?.SetVacuumSfxActive(false);
            pickupOutline.Clear();
            UpdateBasket(pointerPosition, mouse);
        }
        else if (IsVacuumMode)
        {
            pickupOutline.Clear();
            RoundSystem.Instance?.SetVacuumSfxActive(
                mouse.leftButton.isPressed || mouse.rightButton.isPressed);
            UpdateVacuum(mouse);
        }
    }

    private void FixedUpdate()
    {
        bool hasHandAttachPosition =
            WorldHandCursorController.TryGetHeldItemAttachPosition(out _);

        if (!hasHandAttachPosition && heldEgg != null)
        {
            heldEgg.MoveWhileHeld(carryTarget, followSpeed);
        }

        if (!hasHandAttachPosition && heldChicken != null)
        {
            float follow = 1f - Mathf.Exp(-followSpeed * Time.fixedDeltaTime);
            heldChicken.transform.position = Vector3.Lerp(
                heldChicken.transform.position,
                carryTarget,
                follow);
        }
    }

    private void LateUpdate()
    {
        if (!WorldHandCursorController.TryGetHeldItemAttachPosition(
                out Vector3 attachPosition))
        {
            return;
        }

        if (heldEgg != null)
        {
            heldEgg.SnapWhileHeld(attachPosition);
        }

        if (heldChicken != null)
        {
            heldChicken.UpdateHeldCarryPose(
                attachPosition,
                Time.unscaledDeltaTime);
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
        ApplyLegacyCollectionLevel(collectionLevel);
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
        message = $"{CurrentCollectionName} unlocked";
        RoundSystem.Instance?.PlayCashRegisterSfx();
        return true;
    }

    public void InstallCollectionLevel(int level)
    {
        collectionLevel = Mathf.Clamp(level, 0, MaximumCollectionLevel);
        ApplyLegacyCollectionLevel(collectionLevel);
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
    }

    public void CancelPointerInteraction()
    {
        RoundSystem.Instance?.SetVacuumSfxActive(false);
        pickupOutline.Clear();
        ReleaseHandItems();
    }

    public void SelectHandTool()
    {
        if (selectedTool == PlayerTool.Hand)
        {
            return;
        }

        RoundSystem.Instance?.SetVacuumSfxActive(false);
        pickupOutline.Clear();
        ReleaseHandItems();
        selectedTool = PlayerTool.Hand;
        SetCursorToolVisible(false);
        ToolSelectionChanged?.Invoke();
    }

    public bool SelectCollectionTool()
    {
        if (!IsCollectionToolUnlocked)
        {
            return false;
        }

        if (selectedTool == PlayerTool.Collection)
        {
            return true;
        }

        ReleaseHandItems();
        pickupOutline.Clear();
        selectedTool = PlayerTool.Collection;
        bool shouldShow = RoundSystem.Instance != null
            && RoundSystem.Instance.IsRoundInProgress
            && !FoodShopController.IsPlacementActive;
        SetCursorToolVisible(shouldShow);
        ToolSelectionChanged?.Invoke();
        return true;
    }

    public void SetAutomationRareEggProtection(bool enabled)
    {
        automationPreservesRareEggs = enabled;
    }

    private void UpdateHand(Vector2 pointerPosition, Mouse mouse)
    {
        bool canPreviewPickup = heldEgg == null
            && heldChicken == null
            && !mouse.leftButton.isPressed;

        if (canPreviewPickup)
        {
            ResolveHandPickup(
                pointerPosition,
                out ChickenEgg previewEgg,
                out ChickenController previewChicken);
            bool eggCanShowPreview = previewEgg != null
                && previewEgg.IsGroundedForPickupPreview;
            hoveredHandEgg = eggCanShowPreview
                ? previewEgg
                : null;
            hoveredHandChicken = previewEgg == null
                ? previewChicken
                : null;
            pickupOutline.SetTarget(
                eggCanShowPreview
                    ? (Component)previewEgg
                    : previewEgg == null
                        ? previewChicken
                        : null);
        }
        else
        {
            pickupOutline.Clear();
        }

        if (heldEgg == null
            && heldChicken == null
            && mouse.leftButton.wasPressedThisFrame)
        {
            TryPickUpHandItem(pointerPosition);
        }

        if (heldEgg == null && heldChicken == null)
        {
            return;
        }

        UpdateCarryTarget(pointerPosition);

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            ReleaseHandItem(pointerPosition);
        }
    }

    private void UpdateBasket(Vector2 pointerPosition, Mouse mouse)
    {
        if (!mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        // A loose egg under the pointer must win over nearby machine trigger
        // volumes. Otherwise an incubator intake overlapping the ray consumes
        // the click as an empty basket deposit and the egg cannot be picked up.
        ChickenEgg egg = FindEggUnderPointer(pointerPosition);
        if (egg != null && basketEggCount < BasketCapacity)
        {
            Vector3 pickupPosition = egg.transform.position;
            if (TryAddEggToBasket(egg))
            {
                RoundSystem.Instance?.PlayGrabSfx();
                PullNearbyEggsIntoBasket(pickupPosition, egg);
            }

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
    }

    private bool TryAddEggToBasket(ChickenEgg egg)
    {
        if (egg == null
            || basketEggCount >= BasketCapacity
            || !egg.TryCollectFromTool())
        {
            return false;
        }

        int slotIndex = basketEggCount;
        basketEggValues.Add(egg.ValueCents);
        basketEggWeights.Add(egg.WeightKilograms);
        basketEggTypes.Add(egg.Type);
        basketEggCount++;
        StartCoroutine(AnimateEggIntoBasket(
            egg,
            slotIndex,
            basketAnimationGeneration));
        return true;
    }

    private void PullNearbyEggsIntoBasket(
        Vector3 pickupPosition,
        ChickenEgg clickedEgg)
    {
        float radius = BasketReachRadius;
        if (radius <= 0f || basketEggCount >= BasketCapacity)
        {
            return;
        }

        float radiusSquared = radius * radius;
        var candidates = new List<ChickenEgg>();
        IReadOnlyList<ChickenEgg> activeEggs = ChickenEgg.ActiveInstances;
        for (int index = 0; index < activeEggs.Count; index++)
        {
            ChickenEgg candidate = activeEggs[index];
            if (candidate == null
                || candidate == clickedEgg
                || candidate.IsHeld
                || candidate.IsCollected)
            {
                continue;
            }

            Vector3 offset = candidate.transform.position - pickupPosition;
            offset.y = 0f;
            if (offset.sqrMagnitude <= radiusSquared)
            {
                candidates.Add(candidate);
            }
        }

        candidates.Sort((left, right) =>
        {
            Vector3 leftOffset = left.transform.position - pickupPosition;
            Vector3 rightOffset = right.transform.position - pickupPosition;
            leftOffset.y = 0f;
            rightOffset.y = 0f;
            return leftOffset.sqrMagnitude.CompareTo(rightOffset.sqrMagnitude);
        });

        for (int index = 0;
            index < candidates.Count && basketEggCount < BasketCapacity;
            index++)
        {
            TryAddEggToBasket(candidates[index]);
        }
    }

    private void EmptyBasket(EggContainer targetContainer)
    {
        if (basketEggCount <= 0 || targetContainer == null)
        {
            return;
        }

        int deposited = targetContainer.DepositEggValues(
            basketEggValues,
            basketEggWeights);
        basketAnimationGeneration++;
        basketEggCount -= deposited;
        basketEggValues.RemoveRange(0, Mathf.Min(deposited, basketEggValues.Count));
        basketEggWeights.RemoveRange(
            0,
            Mathf.Min(deposited, basketEggWeights.Count));
        basketEggTypes.RemoveRange(0, Mathf.Min(deposited, basketEggTypes.Count));
        RefreshToolEggSlots();
    }

    private void DepositBasketEggInIncubator(IncubatorController targetIncubator)
    {
        if (basketEggCount <= 0 || targetIncubator == null)
        {
            return;
        }

        ChickenEgg.EggType eggType = basketEggTypes.Count > 0
            ? basketEggTypes[0]
            : ChickenEgg.EggType.Common;
        int deposited = targetIncubator.TryAcceptStoredEgg(eggType);
        basketAnimationGeneration++;
        basketEggCount -= deposited;

        if (deposited > 0 && basketEggValues.Count > 0)
        {
            basketEggValues.RemoveAt(0);
            if (basketEggWeights.Count > 0)
            {
                basketEggWeights.RemoveAt(0);
            }
            basketEggTypes.RemoveAt(0);
        }

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
        Vector3 initialTarget = slotIndex < activeToolEggSlots.Length
            && activeToolEggSlots[slotIndex] != null
                ? activeToolEggSlots[slotIndex].position
                : startPosition;
        float lobHeight = Mathf.Clamp(
            Vector3.Distance(startPosition, initialTarget) * 0.35f,
            BasketMinimumLobHeight,
            BasketMaximumLobHeight);
        float elapsed = 0f;

        while (egg != null
            && elapsed < BasketLobDuration
            && generation == basketAnimationGeneration
            && slotIndex < activeToolEggSlots.Length
            && activeToolEggSlots[slotIndex] != null)
        {
            elapsed += Time.deltaTime;
            Transform targetSlot = activeToolEggSlots[slotIndex];
            float progress = Mathf.Clamp01(elapsed / BasketLobDuration);
            float rotationProgress = Mathf.SmoothStep(0f, 1f, progress);
            Vector3 lobOffset = Vector3.up
                * (4f * progress * (1f - progress) * lobHeight);
            egg.transform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, targetSlot.position, progress)
                    + lobOffset,
                Quaternion.Slerp(
                    startRotation,
                    targetSlot.rotation,
                    rotationProgress));
            egg.transform.localScale = Vector3.Lerp(
                startScale,
                targetSlot.lossyScale,
                rotationProgress);
            yield return null;
        }

        if (egg != null)
        {
            egg.ReleaseToPool();
        }

        if (generation == basketAnimationGeneration
            && slotIndex < basketEggCount
            && slotIndex < activeToolEggSlots.Length
            && activeToolEggSlots[slotIndex] != null)
        {
            ChickenEgg.ApplyTypeVisual(
                activeToolEggSlots[slotIndex].gameObject,
                slotIndex < basketEggTypes.Count
                    ? basketEggTypes[slotIndex]
                    : ChickenEgg.EggType.Common);
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

        int powerIndex = Mathf.Clamp(vacuumPowerLevel - 1, 0, 2);
        int rangeIndex = Mathf.Clamp(
            (vacuumRangeLevel > 0 ? vacuumRangeLevel : vacuumPowerLevel) - 1,
            0,
            2);
        float range = VacuumRanges[rangeIndex];
        float halfAngle = VacuumConeAngles[rangeIndex];
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
            Mathf.Max(0.55f, VacuumPowers[powerIndex] * 1.5f));

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

            bool eggRoutesToIncubator = routeToIncubator
                && (!automationPreservesRareEggs
                    || egg.Type == ChickenEgg.EggType.Common);
            if (eggRoutesToIncubator
                && (incubator == null
                    || !incubator.isActiveAndEnabled
                    || incubator.AvailableCapacity
                        <= vacuumIncubatorInFlight.Count))
            {
                continue;
            }

            vacuumInFlight.Add(egg);

            if (eggRoutesToIncubator)
            {
                vacuumIncubatorInFlight.Add(egg);
            }

            StartCoroutine(VacuumEgg(
                egg,
                VacuumPowers[powerIndex],
                eggRoutesToIncubator));

            if (eggRoutesToIncubator
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

        RoundSystem.Instance?.PlayVacuumEggSfx();
        Vector3 start = egg.transform.position;
        float suctionDuration = VacuumSuctionBaseDuration
            / Mathf.Max(0.1f, power);
        float elapsed = 0f;

        while (egg != null && elapsed < suctionDuration)
        {
            elapsed += Time.deltaTime;
            Vector3 nozzle = GetVacuumNozzleTipPosition();
            float normalizedTime = Mathf.Clamp01(elapsed / suctionDuration);
            // Quadratic ease-in keeps the initial tug readable, then rapidly
            // accelerates the egg as it nears the end of the barrel.
            float progress = normalizedTime * normalizedTime;
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
                ? incubator.TryAcceptStoredEgg(egg.Type)
                : eggContainer != null
                    ? (eggContainer.DepositEggValue(
                        egg.ValueCents,
                        egg.WeightKilograms) ? 1 : 0)
                    : 0;

            if (deposited > 0 && egg.TryCollectFromTool())
            {
                egg.ReleaseToPool();
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
        ReleaseHandItems();
        basketAnimationGeneration++;

        if (activeCursorTool != null)
        {
            Destroy(activeCursorTool);
            activeCursorTool = null;
        }
        activeVacuumNozzle = null;
        basketFullIndicator = null;

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
                basketPrefabs != null && basketPrefabs.Length > 0
                    ? Mathf.Min(
                        basketUpgradeLevel - 1,
                        basketPrefabs.Length - 1)
                    : basketUpgradeLevel - 1,
                "basket");
            CreateBasketFullIndicator();
            EnsureBasketEggSlotCapacity(BasketCapacity);
            CacheToolEggSlots();
            RefreshToolEggSlots();
        }
        else if (IsVacuumMode)
        {
            activeCursorTool = InstantiateTierPrefab(
                vacuumPrefabs,
                Mathf.Clamp(
                    Mathf.Max(vacuumPowerLevel, vacuumRangeLevel) - 1,
                    0,
                    2),
                "vacuum");
            activeVacuumNozzle = FindToolChild("Suction Nozzle");
        }

        bool shouldShow = RoundSystem.Instance != null
            && RoundSystem.Instance.IsRoundInProgress
            && selectedTool == PlayerTool.Collection
            && !FoodShopController.IsPlacementActive;
        SetCursorToolVisible(shouldShow);
        ToolSelectionChanged?.Invoke();
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
        if (robotPrefabs == null || robotPrefabs.Length == 0)
        {
            Debug.LogError("Missing authored robot prefabs.", this);
            return;
        }

        int index = Mathf.Clamp(
            Mathf.Max(
                Mathf.Max(robotSpeedLevel, robotCapacityLevel),
                robotSmartnessLevel) - 1,
            0,
            robotPrefabs.Length - 1);

        if (robotPrefabs[index] == null)
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
            FindFirstObjectByType<CrosshatcherController>(),
            RobotSpeeds[Mathf.Clamp(
                robotSpeedLevel - 1,
                0,
                MaximumRobotLevel - 1)],
            RobotCapacities[Mathf.Clamp(
                robotCapacityLevel - 1,
                0,
                MaximumRobotLevel - 1)],
            robotSmartnessLevel,
            0);
    }

    public EggCollectorRobot CreatePenRobot(
        EggContainer targetContainer,
        IncubatorController targetIncubator,
        CrosshatcherController targetCrosshatcher,
        int speedLevel,
        int capacityLevel,
        int smartnessLevel,
        int vacuumLevel,
        Transform parent,
        Vector3? existingWorldPosition = null,
        Quaternion? existingWorldRotation = null)
    {
        int resolvedSpeed = Mathf.Clamp(speedLevel, 1, MaximumRobotLevel);
        int resolvedCapacity = Mathf.Clamp(capacityLevel, 1, MaximumRobotLevel);
        int resolvedSmartness = Mathf.Clamp(
            smartnessLevel,
            0,
            EggCollectorRobot.ChickenArmsSmartnessLevel);
        if (robotPrefabs == null || robotPrefabs.Length == 0)
        {
            Debug.LogError("Missing authored robot prefabs.", this);
            return null;
        }

        int prefabIndex = Mathf.Clamp(
            Mathf.Max(
                Mathf.Max(resolvedSpeed, resolvedCapacity),
                resolvedSmartness) - 1,
            0,
            robotPrefabs.Length - 1);
        if (robotPrefabs[prefabIndex] == null)
        {
            Debug.LogError(
                $"Missing authored robot tier {prefabIndex + 1} prefab.",
                this);
            return null;
        }

        Vector3 spawnPosition = existingWorldPosition
            ?? (targetContainer != null
                ? targetContainer.DepositPosition + Vector3.right * 0.45f
                : Vector3.zero);
        Quaternion spawnRotation = existingWorldRotation
            ?? Quaternion.identity;
        GameObject robotObject = Instantiate(
            robotPrefabs[prefabIndex],
            spawnPosition,
            spawnRotation,
            parent);
        robotObject.name = $"{robotPrefabs[prefabIndex].name} (Pen Robot)";
        EggCollectorRobot robot = robotObject.GetComponent<EggCollectorRobot>();
        if (robot == null)
        {
            Debug.LogError(
                $"{robotPrefabs[prefabIndex].name} needs an EggCollectorRobot.",
                robotObject);
            Destroy(robotObject);
            return null;
        }

        robot.Configure(
            targetContainer,
            targetIncubator,
            targetCrosshatcher,
            RobotSpeeds[resolvedSpeed - 1],
            RobotCapacities[resolvedCapacity - 1],
            resolvedSmartness,
            vacuumLevel);
        return robot;
    }

    public bool TryDeliverHeldChickenToCrosshatcher(
        CrosshatcherController targetCrosshatcher)
    {
        if (heldChicken == null
            || targetCrosshatcher == null
            || !targetCrosshatcher.TryAcceptCarriedChicken(heldChicken))
        {
            return false;
        }

        heldChicken = null;
        return true;
    }

    public bool TryBeginCarryChicken(ChickenController chicken)
    {
        if (selectedTool != PlayerTool.Hand
            || heldEgg != null
            || heldChicken != null
            || chicken == null
            || !chicken.CanBePickedUp
            || (RoundSystem.Instance != null
                && !RoundSystem.Instance.IsRoundInProgress))
        {
            return false;
        }

        pickupOutline.Clear();
        heldChicken = chicken;
        heldChicken.SetMachineControlled(true);
        heldChicken.SetHeldByHand(true);
        carryTarget = heldChicken.transform.position;
        RoundSystem.Instance?.PlayGrabSfx();
        return true;
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

        if (basketFullIndicator != null && basketFullIndicator.activeSelf)
        {
            basketFullIndicator.transform.rotation = viewCamera.transform.rotation;
        }
    }

    private void TryPickUpHandItem(Vector2 pointerPosition)
    {
        if (!ResolveHandPickup(
                pointerPosition,
                out ChickenEgg egg,
                out ChickenController chicken))
        {
            return;
        }

        pickupOutline.Clear();

        if (egg != null)
        {
            if (egg.BeginCarry())
            {
                heldEgg = egg;
                carryTarget = egg.transform.position;
                UpdateCarryTarget(pointerPosition);
                RoundSystem.Instance?.PlayGrabSfx();
            }

            return;
        }

        heldChicken = chicken;
        heldChicken.SetMachineControlled(true);
        heldChicken.SetHeldByHand(true);
        carryTarget = heldChicken.transform.position;
        UpdateCarryTarget(pointerPosition);
        RoundSystem.Instance?.PlayGrabSfx();
    }

    private bool ResolveHandPickup(
        Vector2 pointerPosition,
        out ChickenEgg targetEgg,
        out ChickenController targetChicken)
    {
        targetEgg = null;
        targetChicken = null;
        bool allowEggPickup = !IsCollectionToolUnlocked;
        Ray ray = viewCamera.ScreenPointToRay(pointerPosition);
        RaycastHit[] directHits = Physics.RaycastAll(
            ray,
            pickupDistance,
            pickupLayers,
            QueryTriggerInteraction.Collide);
        Array.Sort(
            directHits,
            (left, right) => left.distance.CompareTo(right.distance));

        if (ResolveHandPickupHits(
                directHits,
                allowEggPickup,
                out targetEgg,
                out targetChicken))
        {
            return true;
        }

        if (handPickupRadius <= 0f)
        {
            return false;
        }

        RaycastHit[] radiusHits = Physics.SphereCastAll(
            ray,
            handPickupRadius,
            pickupDistance,
            pickupLayers,
            QueryTriggerInteraction.Collide);
        Array.Sort(
            radiusHits,
            (left, right) => left.distance.CompareTo(right.distance));
        return ResolveHandPickupHits(
            radiusHits,
            allowEggPickup,
            out targetEgg,
            out targetChicken);
    }

    private static bool ResolveHandPickupHits(
        RaycastHit[] hits,
        bool allowEggPickup,
        out ChickenEgg targetEgg,
        out ChickenController targetChicken)
    {
        targetEgg = null;
        targetChicken = null;

        // Loose eggs are hand targets only before a collection tool is
        // unlocked. Afterwards the hand can reach a chicken standing over an
        // egg without accidentally grabbing the egg instead.
        if (allowEggPickup)
        {
            foreach (RaycastHit hit in hits)
            {
                ChickenEgg egg = hit.collider.GetComponentInParent<ChickenEgg>();

                if (egg != null && !egg.IsHeld && !egg.IsCollected)
                {
                    targetEgg = egg;
                    return true;
                }
            }
        }

        // Chickens are draggable only through the small authored neck capsule.
        foreach (RaycastHit hit in hits)
        {
            ChickenPickupTarget pickupTarget =
                hit.collider.GetComponent<ChickenPickupTarget>();
            ChickenController chicken = pickupTarget != null
                ? pickupTarget.Chicken
                : null;

            if (pickupTarget == null
                || !pickupTarget.CanPickUp
                || chicken == null)
            {
                continue;
            }

            targetChicken = chicken;
            return true;
        }

        return false;
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

    private bool TryGetCrosshatcherUnderPointer(
        Vector2 pointerPosition,
        out CrosshatcherController targetCrosshatcher)
    {
        Ray ray = viewCamera.ScreenPointToRay(pointerPosition);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            pickupDistance,
            pickupLayers,
            QueryTriggerInteraction.Collide);
        targetCrosshatcher = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            CrosshatcherController candidate =
                hit.collider.GetComponentInParent<CrosshatcherController>();

            if (candidate != null
                && candidate.isActiveAndEnabled
                && hit.distance < nearestDistance)
            {
                targetCrosshatcher = candidate;
                nearestDistance = hit.distance;
            }
        }

        return targetCrosshatcher != null;
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

        heldEgg.Release(heldEgg.transform.position);
        heldEgg = null;
    }

    private void ReleaseHandItem(Vector2 pointerPosition)
    {
        if (heldChicken != null)
        {
            ChickenController chicken = heldChicken;

            if (TryGetCrosshatcherUnderPointer(
                    pointerPosition,
                    out CrosshatcherController crosshatcher)
                && crosshatcher.TryAcceptCarriedChicken(chicken))
            {
                heldChicken = null;
                return;
            }

            ReleaseChicken();
            return;
        }

        ReleaseEgg();
    }

    private void ReleaseChicken()
    {
        if (heldChicken == null)
        {
            return;
        }

        ChickenController chicken = heldChicken;
        heldChicken = null;
        chicken.SetHeldByHand(false);
        chicken.SetMachineControlled(false);
    }

    private void ReleaseHandItems()
    {
        ReleaseEgg();
        ReleaseChicken();
    }

    private void HandleRoundPhaseChanged(RoundSystem.RoundPhase phase)
    {
        if (phase != RoundSystem.RoundPhase.Settling)
        {
            return;
        }

        ReleaseHandItems();

        if (basketEggCount > 0)
        {
            basketAnimationGeneration++;
            basketEggCount = 0;
            basketEggValues.Clear();
            basketEggWeights.Clear();
            basketEggTypes.Clear();
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

    private Transform FindToolChild(string childName)
    {
        if (activeCursorTool == null)
        {
            return null;
        }

        foreach (Transform child in
            activeCursorTool.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, childName, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    private Vector3 GetVacuumNozzleTipPosition()
    {
        if (activeVacuumNozzle == null)
        {
            return activeCursorTool != null
                ? activeCursorTool.transform.position + Vector3.up * 0.08f
                : cursorGroundPosition + Vector3.up * toolHeight;
        }

        Vector3 nozzleAxis = activeVacuumNozzle.up;
        if (activeCursorTool != null
            && Vector3.Dot(nozzleAxis, activeCursorTool.transform.forward) < 0f)
        {
            nozzleAxis = -nozzleAxis;
        }

        // The authored nozzle is a unit cylinder scaled along its local Y axis.
        // Its transform sits at the cylinder centre, so advance by its scaled
        // half-length to reach the visible mouth at the front of the barrel.
        return activeVacuumNozzle.position
            + nozzleAxis * Mathf.Abs(activeVacuumNozzle.lossyScale.y);
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

        slots.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        activeToolEggSlots = slots.ToArray();
    }

    private void EnsureBasketEggSlotCapacity(int desiredCapacity)
    {
        if (activeCursorTool == null || desiredCapacity <= 0)
        {
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

        if (slots.Count == 0)
        {
            return;
        }

        slots.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        while (slots.Count < desiredCapacity)
        {
            Transform source = slots[slots.Count - 1];
            Transform slot = Instantiate(source.gameObject, source.parent).transform;
            slot.name = $"Egg Slot {slots.Count + 1}";
            // Basket IV reuses the largest authored basket and places its sixth
            // visible egg in the centre, slightly above the outer ring.
            slot.localPosition = new Vector3(
                0f,
                source.localPosition.y + 0.05f,
                0f);
            slot.gameObject.SetActive(false);
            slots.Add(slot);
        }
    }

    private void RefreshToolEggSlots()
    {
        for (int index = 0; index < activeToolEggSlots.Length; index++)
        {
            ChickenEgg.ApplyTypeVisual(
                activeToolEggSlots[index].gameObject,
                index < basketEggTypes.Count
                    ? basketEggTypes[index]
                    : ChickenEgg.EggType.Common);
            activeToolEggSlots[index].gameObject.SetActive(index < basketEggCount);
        }

        basketFullIndicator?.SetActive(
            IsBasketMode
            && BasketCapacity > 0
            && basketEggCount >= BasketCapacity);
    }

    private void CreateBasketFullIndicator()
    {
        if (activeCursorTool == null)
        {
            return;
        }

        basketFullIndicator = new GameObject(
            "Basket Full Indicator",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(Image));
        RectTransform indicatorRect =
            basketFullIndicator.GetComponent<RectTransform>();
        indicatorRect.SetParent(activeCursorTool.transform, false);
        indicatorRect.localPosition = new Vector3(0f, 0.82f, 0f);
        indicatorRect.localScale = Vector3.one * 0.004f;
        indicatorRect.sizeDelta = new Vector2(180f, 64f);

        Canvas canvas = basketFullIndicator.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = viewCamera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 250;

        Image background = basketFullIndicator.GetComponent<Image>();
        background.color = new Color(0.08f, 0.72f, 0.24f, 0.96f);
        background.raycastTarget = false;
        Outline outline = basketFullIndicator.AddComponent<Outline>();
        outline.effectColor = new Color(0.015f, 0.08f, 0.025f, 0.95f);
        outline.effectDistance = new Vector2(4f, -4f);

        CreateFullIndicatorSegment(
            indicatorRect,
            "Check Short",
            new Vector2(-57f, -3f),
            new Vector2(30f, 10f),
            -42f);
        CreateFullIndicatorSegment(
            indicatorRect,
            "Check Long",
            new Vector2(-35f, 6f),
            new Vector2(48f, 10f),
            42f);

        GameObject labelObject = new GameObject(
            "Full Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(indicatorRect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(55f, 0f);
        labelRect.offsetMax = new Vector2(-8f, 0f);
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = "FULL";
        label.fontSize = 34f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;

        basketFullIndicator.SetActive(false);
    }

    private static void CreateFullIndicatorSegment(
        Transform parent,
        string objectName,
        Vector2 position,
        Vector2 size,
        float angle)
    {
        GameObject segment = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform rect = segment.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        Image image = segment.GetComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = false;
    }

    public void UpgradeBasket()
    {
        basketUpgradeLevel = Mathf.Min(
            MaximumBasketLevel,
            basketUpgradeLevel + 1);
        selectedTool = PlayerTool.Collection;
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
    }

    public void UpgradeBasketReach()
    {
        basketReachLevel = Mathf.Min(
            MaximumBasketReachLevel,
            basketReachLevel + 1);
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
    }

    public void UpgradeVacuumPower()
    {
        vacuumPowerLevel = Mathf.Min(3, vacuumPowerLevel + 1);
        vacuumRangeLevel = Mathf.Max(1, vacuumRangeLevel);
        selectedTool = PlayerTool.Collection;
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
    }

    public void UpgradeVacuumRange()
    {
        vacuumRangeLevel = Mathf.Min(3, vacuumRangeLevel + 1);
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
    }

    public void UnlockRobot()
    {
        robotUnlocked = true;
        robotSpeedLevel = Mathf.Max(1, robotSpeedLevel);
        robotCapacityLevel = Mathf.Max(1, robotCapacityLevel);
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
    }

    public void UpgradeRobotSpeed()
    {
        robotSpeedLevel = Mathf.Min(
            MaximumRobotLevel,
            robotSpeedLevel + 1);
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
    }

    public void UpgradeRobotCapacity()
    {
        robotCapacityLevel = Mathf.Min(
            MaximumRobotLevel,
            robotCapacityLevel + 1);
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
    }

    public void UpgradeRobotSmartness()
    {
        robotSmartnessLevel = Mathf.Min(
            EggCollectorRobot.ChickenArmsSmartnessLevel,
            robotSmartnessLevel + 1);
        ApplyCollectionLevel();
        CollectionLevelChanged?.Invoke();
    }

    private int GetLegacyCollectionLevel()
    {
        if (HasRobot)
        {
            return 8 + Mathf.Clamp(
                Mathf.Max(robotSpeedLevel, robotCapacityLevel) - 1,
                0,
                2);
        }

        if (HasVacuum)
        {
            return 5 + Mathf.Clamp(
                Mathf.Max(vacuumPowerLevel, vacuumRangeLevel) - 1,
                0,
                2);
        }

        return Mathf.Clamp(
            basketUpgradeLevel,
            0,
            MaximumBasketLevel);
    }

    private void MigrateLegacyCollectionLevel()
    {
        if (basketUpgradeLevel > 0
            || vacuumPowerLevel > 0
            || robotUnlocked)
        {
            return;
        }

        ApplyLegacyCollectionLevel(collectionLevel);
    }

    private void ApplyLegacyCollectionLevel(int level)
    {
        level = Mathf.Clamp(level, 0, MaximumCollectionLevel);
        basketUpgradeLevel = Mathf.Clamp(
            level,
            0,
            MaximumBasketLevel);

        if (level >= 5)
        {
            vacuumPowerLevel = Mathf.Clamp(level - 4, 1, 3);
            vacuumRangeLevel = vacuumPowerLevel;
        }

        if (level >= 8)
        {
            robotUnlocked = true;
            robotSpeedLevel = Mathf.Clamp(level - 7, 1, 3);
            robotCapacityLevel = robotSpeedLevel;
            robotSmartnessLevel = level >= 10 ? 1 : 0;
        }
    }

    public static string GetTierDetails(int level)
    {
        level = Mathf.Clamp(level, 0, MaximumCollectionLevel);

        if (level == 0)
        {
            return "Pick up and carry one egg at a time";
        }

        if (level <= MaximumBasketLevel)
        {
            return $"CLICK EGGS > CONTAINER / INCUBATOR  |  CAP {BasketCapacities[level - 1]}";
        }

        if (level <= 7)
        {
            int index = level - 5;
            return $"LMB CASH / RMB INCUBATE  |  {VacuumRanges[index]:0.##}m  |  " +
                $"{VacuumPowers[index]:0.##}x POWER";
        }

        int robotIndex = level - 8;
        return (robotIndex == 2 ? "SMART INCUBATOR  |  " : "AUTOMATIC  |  ") +
            $"CAP {RobotCapacities[robotIndex]}  |  " +
            $"{RobotSpeeds[robotIndex]:0.##} SPEED";
    }

    private static string FormatMoney(int cents)
    {
        return $"${cents / 100:N0}.{Mathf.Abs(cents % 100):D2}";
    }

    private void OnValidate()
    {
        pickupDistance = Mathf.Max(0.1f, pickupDistance);
        handPickupRadius = Mathf.Max(0f, handPickupRadius);
        carryHeight = Mathf.Max(0f, carryHeight);
        followSpeed = Mathf.Max(0.01f, followSpeed);
        collectionLevel = Mathf.Clamp(collectionLevel, 0, MaximumCollectionLevel);
        basketUpgradeLevel = Mathf.Clamp(
            basketUpgradeLevel,
            0,
            MaximumBasketLevel);
        basketReachLevel = Mathf.Clamp(
            basketReachLevel,
            0,
            MaximumBasketReachLevel);
        vacuumPowerLevel = Mathf.Clamp(vacuumPowerLevel, 0, 3);
        vacuumRangeLevel = Mathf.Clamp(vacuumRangeLevel, 0, 3);
        robotSpeedLevel = Mathf.Clamp(
            robotSpeedLevel,
            0,
            MaximumRobotLevel);
        robotCapacityLevel = Mathf.Clamp(
            robotCapacityLevel,
            0,
            MaximumRobotLevel);
        robotSmartnessLevel = Mathf.Clamp(
            robotSmartnessLevel,
            0,
            EggCollectorRobot.ChickenArmsSmartnessLevel);
        toolHeight = Mathf.Max(0f, toolHeight);
        toolSmoothTime = Mathf.Max(0.01f, toolSmoothTime);
        toolSideDistance = Mathf.Max(0f, toolSideDistance);
        toolBackDistance = Mathf.Max(0f, toolBackDistance);
    }
}
