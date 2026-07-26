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
        "Eggcelerator"
    };

    private static readonly float[] FeedSpeedMultipliers =
    {
        1.25f, 1.45f, 1.7f, 2f, 2.35f, 2.75f, 3.2f, 3.7f, 4.3f, 5f
    };

    private static readonly float[] FeedAmounts =
    {
        100f, 110f, 120f, 130f, 145f, 160f, 180f, 200f, 225f, 250f
    };

    private static readonly int[] FeedBagCosts =
    {
        150, 250, 400, 600, 900, 1300, 1900, 2700, 3800, 5200
    };

    private static readonly int[] FeedUnlockCosts =
    {
        0, 600, 1400, 2800, 5000, 8500, 14000, 22000, 34000, 50000
    };

    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    [Header("Shop")]
    [SerializeField] private Button foodIconButton = null;
    [SerializeField] private Button buyButton = null;
    [SerializeField] private TMP_Text ownedCountText = null;
    [SerializeField] private TMP_Text placementStatusText = null;
    [SerializeField] private Image affordabilityProgressFill = null;
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
    private int ignorePlacementUntilFrame;
    private bool hasValidPlacement;
    private bool isPlacementActive;

    public const int MaximumFeedTier = 10;
    public static FoodShopController Instance { get; private set; }
    public static bool IsPlacementActive { get; private set; }
    public int OwnedFoodCount => ownedFood;
    public int UnlockedFeedTier => unlockedFeedTier;
    public string CurrentFeedName => FeedTierNames[unlockedFeedTier - 1];
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

        if (buyButton != null)
        {
            buyButton.onClick.AddListener(BuyFood);
        }

        EggScoreHud.BalanceChanged += HandleBalanceChanged;
    }

    private void Start()
    {
        ConfigureCompactHud();
        RefreshUi();
    }

    private void OnDisable()
    {
        if (foodIconButton != null)
        {
            foodIconButton.onClick.RemoveListener(BeginPlacement);
        }

        if (buyButton != null)
        {
            buyButton.onClick.RemoveListener(BuyFood);
        }

        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
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
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsRoundInProgress)
        {
            CancelPlacement();
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
        return true;
    }

    public bool TryUnlockNextFeedTier(out string message)
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

        if (!EggScoreHud.TrySpendCents(cost))
        {
            message = $"Need {FormatMoney(cost)}";
            return false;
        }

        unlockedFeedTier++;
        RefreshUi();
        message = $"{CurrentFeedName} unlocked";
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
                FeedSpeedMultipliers[placedTier - 1]);
        }

        ownedFood--;
        CancelPlacement();
        RefreshUi();
    }

    private void CancelPlacement()
    {
        isPlacementActive = false;
        IsPlacementActive = false;
        hasValidPlacement = false;

        if (placementPreview != null)
        {
            Destroy(placementPreview);
        }

        placementPreview = null;
        previewRenderers = null;
    }

    private void HandleBalanceChanged(int _)
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
    }

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

        foodIconButton.transform.SetParent(rightHudPanel, false);
        RectTransform iconRect = foodIconButton.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 1f);
        iconRect.anchorMax = new Vector2(0.5f, 1f);
        iconRect.pivot = new Vector2(0.5f, 1f);
        iconRect.anchoredPosition = new Vector2(0f, -190f);
        iconRect.sizeDelta = new Vector2(54f, 54f);

        if (ownedCountText != null)
        {
            ownedCountText.transform.SetParent(foodIconButton.transform, false);
            RectTransform countRect = ownedCountText.rectTransform;
            countRect.anchorMin = new Vector2(1f, 0f);
            countRect.anchorMax = new Vector2(1f, 0f);
            countRect.pivot = new Vector2(0.5f, 0.5f);
            countRect.anchoredPosition = new Vector2(4f, -2f);
            countRect.sizeDelta = new Vector2(76f, 34f);
            ownedCountText.fontSize = 18f;
        }

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

    private static string FormatMoney(int cents)
    {
        return $"${cents / 100}.{cents % 100:D2}";
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
    }
}
