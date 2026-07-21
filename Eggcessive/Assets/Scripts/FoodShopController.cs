using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(GraphicRaycaster))]
public sealed class FoodShopController : MonoBehaviour
{
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    [Header("Shop")]
    [SerializeField] private Button foodIconButton = null;
    [SerializeField] private Button buyButton = null;
    [SerializeField] private TMP_Text ownedCountText = null;
    [SerializeField] private TMP_Text placementStatusText = null;
    [SerializeField] private GameObject foodPrefab = null;
    [SerializeField, Min(1)] private int foodCostCents = 200;

    [Header("Placement")]
    [SerializeField] private Camera placementCamera = null;
    [SerializeField] private int chickenAgentTypeId = -1180031551;
    [SerializeField, Min(0.01f)] private float navMeshSampleDistance = 0.5f;
    [SerializeField] private Color validPreviewColor = new Color(0.55f, 1f, 0.35f, 1f);
    [SerializeField] private Color invalidPreviewColor = new Color(1f, 0.3f, 0.25f, 1f);

    private GameObject placementPreview;
    private Renderer[] previewRenderers;
    private MaterialPropertyBlock previewProperties;
    private Vector3 placementPosition;
    private int ownedFood;
    private int ignorePlacementUntilFrame;
    private bool hasValidPlacement;
    private bool isPlacementActive;

    public static bool IsPlacementActive { get; private set; }

    private void Awake()
    {
        if (!TryGetComponent(out GraphicRaycaster _))
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        EnsureEventSystem();

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

    private void Update()
    {
        if (!isPlacementActive)
        {
            return;
        }

        Mouse mouse = Mouse.current;

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
        if (!EggScoreHud.TrySpendCents(foodCostCents))
        {
            SetStatus($"Need ${foodCostCents / 100}.{foodCostCents % 100:D2}");
            return;
        }

        ownedFood++;
        RefreshUi();
        SetStatus("Click the food to place");
    }

    private void BeginPlacement()
    {
        if (ownedFood <= 0 || foodPrefab == null)
        {
            SetStatus(ownedFood <= 0 ? "Buy food first" : "Food prefab missing");
            return;
        }

        CancelPlacement();
        isPlacementActive = true;
        IsPlacementActive = true;
        ignorePlacementUntilFrame = Time.frameCount + 1;
        placementPreview = Instantiate(foodPrefab);
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

        position = hit.position;
        return true;
    }

    private void PlaceFood()
    {
        if (!hasValidPlacement || ownedFood <= 0 || foodPrefab == null)
        {
            SetStatus("Place food inside the pen");
            return;
        }

        Instantiate(foodPrefab, placementPosition, Quaternion.identity);
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
        if (ownedCountText != null)
        {
            ownedCountText.text = $"x {ownedFood}";
        }

        if (foodIconButton != null)
        {
            foodIconButton.interactable = ownedFood > 0;
        }

        if (buyButton != null)
        {
            // Keep the button clickable when funds are low so BuyFood can show
            // the player how much money is required.
            buyButton.interactable = true;
        }
    }

    private void SetStatus(string message)
    {
        if (placementStatusText != null)
        {
            placementStatusText.text = message;
        }
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject(
            "EventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    private void OnValidate()
    {
        foodCostCents = Mathf.Max(1, foodCostCents);
        navMeshSampleDistance = Mathf.Max(0.01f, navMeshSampleDistance);
    }
}
