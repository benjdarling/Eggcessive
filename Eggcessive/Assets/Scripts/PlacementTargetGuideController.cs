using UnityEngine;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class PlacementTargetGuideController : MonoBehaviour
{
    private const string GuidePrefabResource =
        "Guides/prefab_PlacementTargetGuide";
    private const int MaximumGuideCount = 2;

    [SerializeField] private PlacementTargetGuideVisual guidePrefab;

    private readonly PlacementTargetGuideVisual[] guides =
        new PlacementTargetGuideVisual[MaximumGuideCount];
    private readonly Transform[] crosshatcherTargets =
        new Transform[MaximumGuideCount];
    private EggCarryController carryController;

    private void Awake()
    {
        carryController = GetComponent<EggCarryController>();
        if (guidePrefab == null)
        {
            GameObject prefabObject = Resources.Load<GameObject>(
                GuidePrefabResource);
            guidePrefab = prefabObject != null
                ? prefabObject.GetComponent<PlacementTargetGuideVisual>()
                : null;
        }

        if (guidePrefab == null)
        {
            Debug.LogWarning(
                $"Missing placement guide prefab at Resources/{GuidePrefabResource}.",
                this);
        }
    }

    private void LateUpdate()
    {
        if (!CanShowGuides())
        {
            HideFrom(0);
            return;
        }

        if (carryController.HeldEgg != null)
        {
            ShowIncubatorTarget();
            return;
        }

        if (carryController.HeldChicken != null)
        {
            ShowCrosshatcherTargets();
            return;
        }

        HideFrom(0);
    }

    private bool CanShowGuides()
    {
        return carryController != null
            && guidePrefab != null
            && !FoodShopController.IsPlacementActive
            && (RoundSystem.Instance == null
                || RoundSystem.Instance.IsRoundInProgress)
            && (GameMenuController.Instance == null
                || !GameMenuController.Instance.IsMenuOpen);
    }

    private void ShowIncubatorTarget()
    {
        IncubatorController target = PenExpansionManager.Instance != null
            ? PenExpansionManager.Instance.GetFocusedIncubator()
            : FindFirstObjectByType<IncubatorController>();
        if (target == null || !target.CanAcceptCarriedEgg)
        {
            HideFrom(0);
            return;
        }

        ShowAt(0, target.EggDepositTarget.position);
        HideFrom(1);
    }

    private void ShowCrosshatcherTargets()
    {
        CrosshatcherController target = PenExpansionManager.Instance != null
            ? PenExpansionManager.Instance.GetFocusedCrosshatcher()
            : FindFirstObjectByType<CrosshatcherController>();
        int count = target != null
            ? target.GetAvailableCarriedChickenTargets(crosshatcherTargets)
            : 0;

        for (int index = 0; index < count; index++)
        {
            ShowAt(index, crosshatcherTargets[index].position);
        }

        HideFrom(count);
    }

    private void ShowAt(int index, Vector3 worldPosition)
    {
        PlacementTargetGuideVisual guide = GetOrCreateGuide(index);
        if (guide != null)
        {
            guide.SetTarget(worldPosition, index * 0.13f);
        }
    }

    private PlacementTargetGuideVisual GetOrCreateGuide(int index)
    {
        if (index < 0 || index >= guides.Length || guidePrefab == null)
        {
            return null;
        }

        if (guides[index] == null)
        {
            guides[index] = Instantiate(guidePrefab);
            guides[index].name = $"Placement Target Guide {index + 1}";
        }

        return guides[index];
    }

    private void HideFrom(int firstIndex)
    {
        for (int index = Mathf.Max(0, firstIndex); index < guides.Length; index++)
        {
            if (guides[index] != null)
            {
                guides[index].gameObject.SetActive(false);
            }
        }
    }

    private void OnDestroy()
    {
        for (int index = 0; index < guides.Length; index++)
        {
            if (guides[index] != null)
            {
                Destroy(guides[index].gameObject);
            }
        }
    }

    private void OnDisable()
    {
        HideFrom(0);
    }
}
