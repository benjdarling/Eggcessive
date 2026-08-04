using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(GraphicRaycaster))]
public sealed class FoodShopController : MonoBehaviour
{
    private static readonly string[] FeedTierNames =
    {
        "Basic Mash",
        "Corn Mix",
        "Layer Pellets",
        "Protein Blend",
        "Omega Feed",
        "Farmer's Choice",
        "Turbo Grain",
        "Golden Crumble",
        "Champion Mix",
        "Eggcelerator",
        "Super Layer",
        "Power Pellets",
        "Royal Ration",
        "Hyper Harvest",
        "Infinite Grain"
    };

    private static readonly float[] FeedSpeedMultipliers =
    {
        1.25f, 1.45f, 1.7f, 2f, 2.35f, 2.75f, 3.2f, 3.7f, 4.3f, 5f,
        5.8f, 6.7f, 7.7f, 8.8f, 10f
    };

    private static readonly float[] FeedAmounts =
    {
        100f, 110f, 120f, 130f, 145f, 160f, 180f, 200f, 225f, 250f,
        280f, 315f, 355f, 400f, 450f
    };

    private static readonly int[] FeedBagCosts =
    {
        150, 250, 400, 600, 900, 1300, 1900, 2700, 3800, 5200,
        7200, 10000, 14000, 19500, 27000
    };

    private static readonly int[] FeedUnlockCosts =
    {
        0, 600, 1400, 2800, 5000, 8500, 14000, 22000, 34000, 50000,
        75000, 110000, 160000, 230000, 330000
    };

    private static readonly float[] PrimeFeedMultipliers =
    {
        1f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f
    };

    private static readonly int[] PrimeFeedUpgradeCosts =
    {
        2000, 7500, 25000, 80000, 250000
    };

    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    [Header("Shop")]
    [SerializeField] private Button foodIconButton = null;
    [SerializeField] private Button buyButton = null;
    [SerializeField] private TMP_Text ownedCountText = null;
    [SerializeField] private TMP_Text placementStatusText = null;
    [SerializeField] private Image affordabilityProgressFill = null;
    [Header("Authored Tool HUD")]
    [SerializeField] private Button handToolButton;
    [SerializeField] private Button collectionToolButton;
    [SerializeField] private TMP_Text collectionToolLabel;
    [SerializeField] private Image handToolImage;
    [SerializeField] private Image collectionToolImage;
    [SerializeField] private Image foodToolImage;
    [SerializeField] private RawImage handToolIcon;
    [SerializeField] private RawImage collectionToolIcon;
    [SerializeField] private RawImage foodToolIcon;
    [SerializeField] private GameObject foodPrefab = null;
    [SerializeField, Min(1)] private int foodCostCents = 200;

    [Header("Placement")]
    [SerializeField] private Camera placementCamera = null;
    [SerializeField] private int chickenAgentTypeId = -1180031551;
    [SerializeField, Min(0.01f)] private float navMeshSampleDistance = 0.5f;
    [SerializeField] private Color validPreviewColor = new Color(0.55f, 1f, 0.35f, 1f);
    [SerializeField] private Color invalidPreviewColor = new Color(1f, 0.3f, 0.25f, 1f);

    private GameObject placementPreview;
    private readonly Queue<int> ownedFoodTiers = new Queue<int>();
    private Renderer[] previewRenderers;
    private MaterialPropertyBlock previewProperties;
    private Vector3 placementPosition;
    private Quaternion placementRotation = Quaternion.identity;
    private int ownedFood;
    private int unlockedFeedTier = 1;
    [SerializeField, Range(0, MaximumPrimeFeedLevel)]
    private int primeFeedLevel;
    private int ignorePlacementUntilFrame;
    private bool hasValidPlacement;
    private bool isPlacementActive;
    public const int MaximumFeedTier = 15;
    public const int MaximumPrimeFeedLevel = 5;
    public static FoodShopController Instance { get; private set; }
    public static bool IsPlacementActive { get; private set; }
    public int OwnedFoodCount => ownedFood;
    public int UnlockedFeedTier => unlockedFeedTier;
    public int PrimeFeedLevel => primeFeedLevel;
    public float CurrentPremiumChanceMultiplier =>
        GetPrimeFeedMultiplier(primeFeedLevel);
    public string CurrentFeedName => FeedTierNames[unlockedFeedTier - 1];
    public float CurrentFeedAmount => FeedAmounts[unlockedFeedTier - 1];
    public float CurrentFeedSpeedMultiplier => FeedSpeedMultipliers[unlockedFeedTier - 1];
    public int CurrentFeedBagCost => FeedBagCosts[unlockedFeedTier - 1];
    public bool HasFeedTierUpgrade => unlockedFeedTier < MaximumFeedTier;
    public int NextFeedTierUnlockCost =>
        HasFeedTierUpgrade ? FeedUnlockCosts[unlockedFeedTier] : 0;
    public string NextFeedName =>
        HasFeedTierUpgrade ? FeedTierNames[unlockedFeedTier] : CurrentFeedName;
    public float NextFeedSpeedMultiplier =>
        HasFeedTierUpgrade
            ? FeedSpeedMultipliers[unlockedFeedTier]
            : CurrentFeedSpeedMultiplier;

    public int GetFeedUnlockCost(int targetTier)
    {
        return FeedUnlockCosts[
            Mathf.Clamp(targetTier, 1, MaximumFeedTier) - 1];
    }

    public string GetFeedName(int tier)
    {
        return FeedTierNames[Mathf.Clamp(tier, 1, MaximumFeedTier) - 1];
    }

    public float GetFeedSpeedMultiplier(int tier)
    {
        return FeedSpeedMultipliers[
            Mathf.Clamp(tier, 1, MaximumFeedTier) - 1];
    }

    public float GetPrimeFeedMultiplier(int level)
    {
        return PrimeFeedMultipliers[
            Mathf.Clamp(level, 0, MaximumPrimeFeedLevel)];
    }

    public int GetPrimeFeedUpgradeCost(int targetLevel)
    {
        int target = Mathf.Clamp(targetLevel, 1, MaximumPrimeFeedLevel);
        return PrimeFeedUpgradeCosts[target - 1];
    }

    private void Awake()
    {
        Instance = this;

        if (placementCamera == null)
        {
            placementCamera = Camera.main;
        }

        previewProperties = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        if (foodIconButton != null)
        {
            foodIconButton.onClick.AddListener(BeginPlacement);
        }

        if (handToolButton != null)
        {
            handToolButton.onClick.AddListener(SelectHandTool);
        }

        if (collectionToolButton != null)
        {
            collectionToolButton.onClick.AddListener(SelectCollectionTool);
        }

        if (buyButton != null)
        {
            buyButton.onClick.AddListener(BuyFood);
        }

        EggScoreHud.BalanceChanged += HandleBalanceChanged;
        EggCarryController.ToolSelectionChanged += RefreshToolButtons;
    }

    private void Start()
    {
        RefreshUi();
    }

    private void OnDisable()
    {
        if (foodIconButton != null)
        {
            foodIconButton.onClick.RemoveListener(BeginPlacement);
        }

        if (handToolButton != null)
        {
            handToolButton.onClick.RemoveListener(SelectHandTool);
        }

        if (collectionToolButton != null)
        {
            collectionToolButton.onClick.RemoveListener(SelectCollectionTool);
        }

        if (buyButton != null)
        {
            buyButton.onClick.RemoveListener(BuyFood);
        }

        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
        EggCarryController.ToolSelectionChanged -= RefreshToolButtons;
        CancelPlacement();
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
        Keyboard keyboard = Keyboard.current;
        bool roundActive = RoundSystem.Instance == null
            || RoundSystem.Instance.IsRoundInProgress;

        if (roundActive && keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                SelectHandTool();
            }
            else if (keyboard.digit2Key.wasPressedThisFrame)
            {
                SelectCollectionTool();
            }
            else if (keyboard.digit3Key.wasPressedThisFrame)
            {
                BeginPlacement();
            }
        }

        if (!roundActive)
        {
            if (isPlacementActive)
            {
                CancelPlacement();
            }

            return;
        }

        if (!isPlacementActive)
        {
            return;
        }

        Mouse mouse = GameplayTestBot.PointerMouse;

        if (mouse == null)
        {
            return;
        }

        UpdatePlacementPreview(mouse.position.ReadValue());

        if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            || mouse.rightButton.wasPressedThisFrame)
        {
            CancelPlacement();
            return;
        }

        if (Time.frameCount <= ignorePlacementUntilFrame
            || !mouse.leftButton.wasPressedThisFrame
            || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            return;
        }

        PlaceFood();
    }

    private void BuyFood()
    {
        if (!TryBuyCurrentFeed(out string message))
        {
            SetStatus(message);
            return;
        }

        SetStatus("Click the food to place");
    }

    public bool TryBuyCurrentFeed(out string message)
    {
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsSuppliesShopOpen)
        {
            message = "Feed is sold between rounds";
            return false;
        }

        int cost = CurrentFeedBagCost;

        if (!EggScoreHud.TrySpendCents(cost))
        {
            message = $"Need {FormatMoney(cost)}";
            return false;
        }

        ownedFoodTiers.Enqueue(unlockedFeedTier);
        ownedFood++;
        RefreshUi();
        message = $"{CurrentFeedName} added";
        RoundSystem.Instance?.PlayCashRegisterSfx();
        return true;
    }

    public bool TryUnlockNextFeedTier(out string message, bool spendCurrency = true)
    {
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsSuppliesShopOpen)
        {
            message = "Feed upgrades are sold between rounds";
            return false;
        }

        if (!HasFeedTierUpgrade)
        {
            message = "Maximum feed tier";
            return false;
        }

        int cost = NextFeedTierUnlockCost;

        if (spendCurrency && !EggScoreHud.TrySpendCents(cost))
        {
            message = $"Need {FormatMoney(cost)}";
            return false;
        }

        unlockedFeedTier++;
        RefreshUi();
        message = $"{CurrentFeedName} unlocked";
        if (spendCurrency)
        {
            RoundSystem.Instance?.PlayCashRegisterSfx();
        }
        return true;
    }

    public bool TryUpgradePrimeFeed(
        out string message,
        bool spendCurrency = true)
    {
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsSuppliesShopOpen)
        {
            message = "Feed upgrades are sold between rounds";
            return false;
        }

        if (primeFeedLevel >= MaximumPrimeFeedLevel)
        {
            message = "Maximum Prime Feed tier";
            return false;
        }

        int cost = GetPrimeFeedUpgradeCost(primeFeedLevel + 1);
        if (spendCurrency && !EggScoreHud.TrySpendCents(cost))
        {
            message = $"Need {FormatMoney(cost)}";
            return false;
        }

        primeFeedLevel++;
        RefreshUi();
        message =
            $"Prime Feed now gives {CurrentPremiumChanceMultiplier:0.0}x premium chance";
        if (spendCurrency)
        {
            RoundSystem.Instance?.PlayCashRegisterSfx();
        }
        return true;
    }

    private void BeginPlacement()
    {
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsRoundInProgress)
        {
            SetStatus("Place feed during a round");
            return;
        }

        if (ownedFood <= 0 || foodPrefab == null)
        {
            SetStatus(ownedFood <= 0 ? "Buy food first" : "Food prefab missing");
            return;
        }

        CancelPlacement();
        EggCarryController.Instance?.CancelPointerInteraction();
        isPlacementActive = true;
        IsPlacementActive = true;
        ignorePlacementUntilFrame = Time.frameCount + 1;
        placementRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        placementPreview = Instantiate(
            foodPrefab,
            Vector3.zero,
            placementRotation);
        placementPreview.name = "Food Placement Preview";

        FoodPile previewPile = placementPreview.GetComponent<FoodPile>();

        if (previewPile != null)
        {
            previewPile.enabled = false;
        }

        foreach (Collider previewCollider in placementPreview.GetComponentsInChildren<Collider>(true))
        {
            previewCollider.enabled = false;
        }

        previewRenderers = placementPreview.GetComponentsInChildren<Renderer>(true);
        placementPreview.SetActive(false);
        SetStatus("Click in the pen to place");
        RoundSystem.Instance?.PlayFoodPickupSfx();
        RefreshToolButtons();
    }

    private void UpdatePlacementPreview(Vector2 pointerPosition)
    {
        if (placementCamera == null)
        {
            placementCamera = Camera.main;
        }

        hasValidPlacement = TryGetPlacementPosition(pointerPosition, out placementPosition);

        if (placementPreview == null)
        {
            return;
        }

        placementPreview.SetActive(true);
        placementPreview.transform.position = placementPosition;
        Color previewColor = hasValidPlacement ? validPreviewColor : invalidPreviewColor;

        foreach (Renderer previewRenderer in previewRenderers)
        {
            previewRenderer.GetPropertyBlock(previewProperties);
            previewProperties.SetColor(BaseColorProperty, previewColor);
            previewProperties.SetColor(ColorProperty, previewColor);
            previewRenderer.SetPropertyBlock(previewProperties);
        }
    }

    private bool TryGetPlacementPosition(Vector2 pointerPosition, out Vector3 position)
    {
        position = Vector3.zero;

        if (placementCamera == null)
        {
            return false;
        }

        Ray ray = placementCamera.ScreenPointToRay(pointerPosition);
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (!groundPlane.Raycast(ray, out float rayDistance))
        {
            return false;
        }

        Vector3 requestedPosition = ray.GetPoint(rayDistance);
        position = requestedPosition;
        NavMeshQueryFilter queryFilter = new NavMeshQueryFilter
        {
            agentTypeID = chickenAgentTypeId,
            areaMask = NavMesh.AllAreas
        };

        if (!NavMesh.SamplePosition(
                requestedPosition,
                out NavMeshHit hit,
                navMeshSampleDistance,
                queryFilter))
        {
            return false;
        }

        // The NavMesh sample validates the XZ location, but food belongs on the
        // known ground plane rather than the rasterized NavMesh polygon height.
        position = hit.position;
        position.y = requestedPosition.y;
        return true;
    }

    private void PlaceFood()
    {
        if (!hasValidPlacement || ownedFood <= 0 || foodPrefab == null)
        {
            SetStatus("Place food inside the pen");
            return;
        }

        GameObject placedFood = Instantiate(foodPrefab, placementPosition, placementRotation);
        int placedTier = ownedFoodTiers.Count > 0
            ? ownedFoodTiers.Dequeue()
            : unlockedFeedTier;
        FoodPile placedPile = placedFood.GetComponent<FoodPile>();

        if (placedPile != null)
        {
            placedPile.ConfigureFeed(
                FeedAmounts[placedTier - 1],
                FeedSpeedMultipliers[placedTier - 1],
                CurrentPremiumChanceMultiplier);
        }

        ownedFood--;
        RoundSystem.Instance?.PlayFoodPlaceSfx();
        if (ownedFood > 0)
        {
            placementRotation = Quaternion.Euler(
                0f,
                Random.Range(0f, 360f),
                0f);
            if (placementPreview != null)
            {
                placementPreview.transform.rotation = placementRotation;
            }

            SetStatus($"Place another food pile  .  {ownedFood} remaining");
        }
        else
        {
            CancelPlacement();
        }
        RefreshUi();
    }

    private void CancelPlacement()
    {
        bool wasActive = isPlacementActive;
        isPlacementActive = false;
        IsPlacementActive = false;
        hasValidPlacement = false;

        if (placementPreview != null)
        {
            Destroy(placementPreview);
        }

        placementPreview = null;
        previewRenderers = null;

        if (wasActive)
        {
            RefreshToolButtons();
        }
    }

    public void CancelActivePlacement()
    {
        CancelPlacement();
    }

    private void SelectHandTool()
    {
        CancelPlacement();
        EggCarryController.Instance?.SelectHandTool();
        RefreshToolButtons();
    }

    private void SelectCollectionTool()
    {
        EggCarryController collection = EggCarryController.Instance;

        if (collection == null || !collection.IsCollectionToolUnlocked)
        {
            SetStatus("Unlock the basket first");
            return;
        }

        CancelPlacement();
        collection.SelectCollectionTool();
        RefreshToolButtons();
    }

    private void HandleBalanceChanged(long _)
    {
        RefreshUi();
    }

    private void RefreshUi()
    {
        bool inventoryEmpty = ownedFood <= 0;

        if (ownedCountText != null)
        {
            ownedCountText.text = $"x {ownedFood}";
            ownedCountText.color = new Color(1f, 0.9f, 0.42f);
        }

        if (foodIconButton != null)
        {
            ColorBlock colors = foodIconButton.colors;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            foodIconButton.colors = colors;
            foodIconButton.interactable = !inventoryEmpty;
        }

        if (buyButton != null)
        {
            // Keep the button clickable when funds are low so BuyFood can show
            // the player how much money is required.
            buyButton.interactable = true;
        }

        if (affordabilityProgressFill != null)
        {
            affordabilityProgressFill.fillAmount = Mathf.Clamp01(
                EggScoreHud.CurrentCents / (float)CurrentFeedBagCost);
        }

        RefreshToolButtons();
    }

#if UNITY_EDITOR
    // Legacy editor-only migration helpers. The live tool HUD is authored in
    // prefab_EggScoreHud and is never assembled during gameplay.
    private void ConfigureCompactHud()
    {
        if (foodIconButton == null)
        {
            return;
        }

        Transform oldFoodPanel = foodIconButton.transform.parent;
        Transform rightHudPanel = oldFoodPanel != null ? oldFoodPanel.parent : null;

        if (rightHudPanel == null)
        {
            return;
        }

        RectTransform rightHudRect = rightHudPanel as RectTransform;
        if (rightHudRect != null)
        {
            rightHudRect.anchorMin =
                new Vector2(0.85f, rightHudRect.anchorMin.y);
            rightHudRect.anchorMax =
                new Vector2(1f, rightHudRect.anchorMax.y);
            rightHudRect.offsetMin =
                new Vector2(0f, rightHudRect.offsetMin.y);
            rightHudRect.offsetMax =
                new Vector2(0f, rightHudRect.offsetMax.y);
        }

        Transform toolPaletteParent = rightHudPanel.parent;
        foodIconButton.transform.SetParent(toolPaletteParent, false);
        RectTransform iconRect = foodIconButton.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.zero;
        iconRect.pivot = Vector2.zero;
        iconRect.anchoredPosition = new Vector2(172f, 24f);
        iconRect.sizeDelta = new Vector2(64f, 64f);
        foodToolImage = foodIconButton.GetComponent<Image>();
        StyleToolButtonFrame(foodIconButton, foodToolImage);
        Transform oldFoodIcon = foodIconButton.transform.Find(
            "Food Sphere Icon");
        if (oldFoodIcon != null)
        {
            oldFoodIcon.gameObject.SetActive(false);
        }

        if (ownedCountText != null)
        {
            ownedCountText.transform.SetParent(foodIconButton.transform, false);
            RectTransform countRect = ownedCountText.rectTransform;
            countRect.anchorMin = Vector2.zero;
            countRect.anchorMax = Vector2.zero;
            countRect.pivot = Vector2.zero;
            countRect.anchoredPosition = new Vector2(5f, 4f);
            countRect.sizeDelta = new Vector2(36f, 18f);
            ownedCountText.fontSize = 11f;
            ownedCountText.alignment = TextAlignmentOptions.BottomLeft;
        }

        handToolButton = CreateToolButton(
            toolPaletteParent,
            "Hand Tool Button",
            new Vector2(24f, 24f),
            "HAND",
            "1",
            new Color(0.18f, 0.48f, 0.34f, 1f),
            out handToolImage,
            out _);
        collectionToolButton = CreateToolButton(
            toolPaletteParent,
            "Collection Tool Button",
            new Vector2(98f, 24f),
            "BASKET",
            "2",
            new Color(0.15f, 0.39f, 0.63f, 1f),
            out collectionToolImage,
            out collectionToolLabel);

        Texture2D iconAtlas = Resources.Load<Texture2D>("UI/HudIconAtlas");
        if (iconAtlas != null)
        {
            handToolIcon = EnsureToolIcon(
                handToolButton.transform,
                iconAtlas,
                5);
            collectionToolIcon = EnsureToolIcon(
                collectionToolButton.transform,
                iconAtlas,
                6);
            foodToolIcon = EnsureToolIcon(
                foodIconButton.transform,
                iconAtlas,
                7);
        }

        EnsureShortcutBadge(foodIconButton.transform, "3");

        oldFoodPanel.gameObject.SetActive(false);
        Image rightHudBackground = rightHudPanel.GetComponent<Image>();

        if (rightHudBackground != null)
        {
            rightHudBackground.enabled = false;
        }

        Transform oldScore = rightHudPanel.Find("Score");

        if (oldScore != null)
        {
            oldScore.gameObject.SetActive(false);
        }

        Transform oldIncubatorPanel = rightHudPanel.Find("Incubator Shop");

        if (oldIncubatorPanel != null)
        {
            oldIncubatorPanel.gameObject.SetActive(false);
        }
    }

    private Button CreateToolButton(
        Transform parent,
        string objectName,
        Vector2 position,
        string label,
        string shortcut,
        Color color,
        out Image image,
        out TMP_Text labelText)
    {
        GameObject buttonObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(64f, 64f);
        image = buttonObject.GetComponent<Image>();
        image.color = color;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        StyleToolButtonFrame(button, image);

        GameObject labelObject = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(3f, 3f);
        labelRect.offsetMax = new Vector2(-3f, -3f);
        labelText = labelObject.GetComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.font = GetHudFont();
        labelText.fontSize = 9f;
        labelText.fontStyle = FontStyles.Bold;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.color = Color.white;
        labelText.raycastTarget = false;
        labelText.gameObject.SetActive(false);
        EnsureShortcutBadge(buttonObject.transform, shortcut);
        return button;
    }

    private void EnsureShortcutBadge(Transform parent, string shortcut)
    {
        RectTransform badgeRect =
            parent.Find("Shortcut Badge") as RectTransform;
        Image badgeImage;
        if (badgeRect == null)
        {
            GameObject badgeObject = new GameObject(
                "Shortcut Badge",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            badgeObject.transform.SetParent(parent, false);
            badgeRect = badgeObject.GetComponent<RectTransform>();
            badgeImage = badgeObject.GetComponent<Image>();
        }
        else
        {
            badgeImage = badgeRect.GetComponent<Image>();
        }

        badgeRect.anchorMin = Vector2.one;
        badgeRect.anchorMax = Vector2.one;
        badgeRect.pivot = Vector2.one;
        badgeRect.anchoredPosition = new Vector2(-2f, -2f);
        badgeRect.sizeDelta = new Vector2(20f, 20f);
        badgeImage.sprite = RoundSystem.GetHudRoundedSprite();
        badgeImage.type = Image.Type.Sliced;
        badgeImage.color = new Color(0.04f, 0.04f, 0.04f, 0.94f);
        badgeImage.raycastTarget = false;

        TMP_Text text = badgeRect.Find("Number")?.GetComponent<TMP_Text>();
        if (text == null)
        {
            GameObject textObject = new GameObject(
                "Number",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(badgeRect, false);
            text = textObject.GetComponent<TextMeshProUGUI>();
        }

        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        text.text = shortcut;
        text.font = GetHudFont();
        text.fontSize = 12f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        badgeRect.SetAsLastSibling();
    }

    private static void StyleToolButtonFrame(Button button, Image image)
    {
        if (button == null || image == null)
        {
            return;
        }

        image.sprite = RoundSystem.GetHudRoundedSprite();
        image.type = Image.Type.Sliced;
        Outline outline = button.GetComponent<Outline>();
        if (outline == null)
        {
            outline = button.gameObject.AddComponent<Outline>();
        }

        outline.effectColor = new Color(0.08f, 0.06f, 0.035f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        Shadow shadow = null;
        Shadow[] shadows = button.GetComponents<Shadow>();
        for (int index = 0; index < shadows.Length; index++)
        {
            if (shadows[index] != null
                && shadows[index].GetType() == typeof(Shadow))
            {
                shadow = shadows[index];
                break;
            }
        }

        if (shadow == null)
        {
            shadow = button.gameObject.AddComponent<Shadow>();
        }

        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(3f, -4f);
    }

    private static RawImage EnsureToolIcon(
        Transform parent,
        Texture2D atlas,
        int atlasIndex)
    {
        RawImage icon = parent.Find("HUD Tool Icon")
            ?.GetComponent<RawImage>();
        if (icon == null)
        {
            GameObject iconObject = new GameObject(
                "HUD Tool Icon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            iconObject.transform.SetParent(parent, false);
            icon = iconObject.GetComponent<RawImage>();
        }

        RectTransform iconRect = icon.rectTransform;
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(0f, -1f);
        iconRect.sizeDelta = new Vector2(44f, 44f);
        icon.texture = atlas;
        icon.uvRect = RoundSystem.GetHudIconUv(atlasIndex);
        icon.color = Color.white;
        icon.raycastTarget = false;
        icon.transform.SetAsFirstSibling();
        return icon;
    }

    private TMP_FontAsset GetHudFont()
    {
        if (ownedCountText != null && ownedCountText.font != null)
        {
            return ownedCountText.font;
        }

        return placementStatusText != null ? placementStatusText.font : null;
    }

#endif

    private void RefreshToolButtons()
    {
        EggCarryController collection = EggCarryController.Instance;
        bool foodSelected = isPlacementActive;
        bool collectionSelected = !foodSelected
            && collection != null
            && collection.SelectedTool == EggCarryController.PlayerTool.Collection;
        bool handSelected = !foodSelected && !collectionSelected;
        SetToolButtonVisual(
            handToolButton,
            handToolImage,
            new Color(0.18f, 0.48f, 0.34f, 1f),
            handSelected);
        SetToolButtonVisual(
            collectionToolButton,
            collectionToolImage,
            new Color(0.15f, 0.39f, 0.63f, 1f),
            collectionSelected);
        SetToolButtonVisual(
            foodIconButton,
            foodToolImage,
            new Color(0.54f, 0.27f, 0.08f, 1f),
            foodSelected);

        if (collectionToolButton != null)
        {
            collectionToolButton.interactable =
                collection != null && collection.IsCollectionToolUnlocked;
        }

        if (collectionToolLabel != null)
        {
            collectionToolLabel.text = collection != null
                ? collection.CollectionToolName
                : "BASKET";
        }

        if (collectionToolIcon != null)
        {
            collectionToolIcon.uvRect = RoundSystem.GetHudIconUv(
                collection != null && collection.HasVacuum ? 8 : 6);
        }
    }

    private static void SetToolButtonVisual(
        Button button,
        Image image,
        Color baseColor,
        bool selected)
    {
        if (button == null || image == null)
        {
            return;
        }

        Color shownColor = selected
            ? Color.Lerp(baseColor, Color.white, 0.28f)
            : baseColor;
        image.color = shownColor;
        ColorBlock colors = button.colors;
        colors.normalColor = shownColor;
        colors.selectedColor = shownColor;
        colors.highlightedColor = Color.Lerp(shownColor, Color.white, 0.18f);
        colors.pressedColor = Color.Lerp(shownColor, Color.black, 0.16f);
        button.colors = colors;

        Outline outline = button.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = selected
                ? new Color(1f, 0.9f, 0.42f, 1f)
                : new Color(0.08f, 0.06f, 0.035f, 0.9f);
            outline.effectDistance = selected
                ? new Vector2(3f, -3f)
                : new Vector2(2f, -2f);
        }
    }

    private static string FormatMoney(int cents)
    {
        return $"${cents / 100:N0}.{Mathf.Abs(cents % 100):D2}";
    }

    private void SetStatus(string message)
    {
        if (placementStatusText != null)
        {
            placementStatusText.text = message;
        }
    }

    private void OnValidate()
    {
        foodCostCents = Mathf.Max(1, foodCostCents);
        navMeshSampleDistance = Mathf.Max(0.01f, navMeshSampleDistance);
        primeFeedLevel = Mathf.Clamp(
            primeFeedLevel,
            0,
            MaximumPrimeFeedLevel);
    }
}
