using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(BoxCollider))]
public class DebugChickenSpawner : MonoBehaviour
{
    private const int PerformanceTestSpawnsPerFrame = 10;
    private const float SpawnNavMeshSampleDistance = 0.75f;
    private static int automaticNavMeshBuildSuppressionDepth;

    [Header("Spawn Settings")]
    [SerializeField] private GameObject chickenPrefab = null;
    [SerializeField, Min(0)] private int targetCount = 3;
    [SerializeField, Min(0f)] private float spawnDuration = 0f;
    [SerializeField, Min(0f)] private float minimumSpacing = 0.5f;

    [Header("Placement")]
    [SerializeField, Min(1)] private int placementAttempts = 30;

    [Header("Pen NavMesh")]
    [SerializeField] private bool buildNavMeshAtRuntime = true;
    [SerializeField] private int chickenAgentTypeId = -1180031551;
    [SerializeField] private LayerMask navMeshSourceLayers = ~0;
    [SerializeField, Min(0.1f)] private float navMeshVolumeHeight = 2f;

    private readonly List<Vector3> spawnedPositions = new List<Vector3>();
    private BoxCollider spawnVolume;
    private Coroutine performanceTestSpawnCoroutine;
    private bool hasReportedMissingSpawnNavMesh;

    private void Awake()
    {
        spawnVolume = GetComponent<BoxCollider>();

        if (buildNavMeshAtRuntime
            && automaticNavMeshBuildSuppressionDepth == 0)
        {
            BuildPenNavMesh();
        }
    }

    private void Start()
    {
        if (chickenPrefab == null)
        {
            Debug.LogError($"{nameof(DebugChickenSpawner)} on {name} needs a chicken prefab.", this);
            return;
        }

        StartCoroutine(SpawnChickens());
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.f6Key.wasPressedThisFrame)
        {
            PenExpansionManager manager = PenExpansionManager.Instance;
            bool added = manager != null
                && manager.TryDebugActivateNextPen();
            Debug.Log(
                added
                    ? $"F6 debug pen: activated Pen {manager.FocusedPenIndex + 1} without spending cash."
                    : "F6 debug pen: no pen could be activated right now.",
                this);
        }

        if (keyboard.f5Key.wasPressedThisFrame
            && performanceTestSpawnCoroutine == null)
        {
            performanceTestSpawnCoroutine =
                StartCoroutine(FillFocusedPenToChickenCap());
        }
    }

    private IEnumerator FillFocusedPenToChickenCap()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        int penIndex = manager != null && manager.IsInitialized
            ? manager.FocusedPenIndex
            : 0;
        while (manager != null && manager.IsPenPurchaseInProgress)
        {
            yield return null;
        }

        DebugChickenSpawner targetSpawner = manager != null
            ? manager.GetChickenSpawner(penIndex)
            : this;
        if (targetSpawner == null)
        {
            targetSpawner = this;
        }

        int spawnedThisFrame = 0;
        int existingCount = manager != null
            ? manager.GetChickenCount(penIndex)
            : ChickenController.ActiveInstances.Count;
        int requestedSpawnCount = Mathf.Max(
            0,
            ChickenController.MaximumChickenCount - existingCount);
        int successfulSpawnCount = 0;

        for (int spawnedCount = 0;
            spawnedCount < requestedSpawnCount;
            spawnedCount++)
        {
            if (targetSpawner.SpawnChicken())
            {
                successfulSpawnCount++;
            }

            spawnedThisFrame++;

            if (spawnedThisFrame >= PerformanceTestSpawnsPerFrame)
            {
                spawnedThisFrame = 0;
                yield return null;
            }
        }

        performanceTestSpawnCoroutine = null;
        Debug.Log(
            $"F5 chicken performance test: spawned {successfulSpawnCount} chickens "
            + $"in Pen {penIndex + 1}. Pen total: "
            + $"{(manager != null ? manager.GetChickenCount(penIndex) : ChickenController.ActiveInstances.Count)}"
            + $"/{ChickenController.MaximumChickenCount}.",
            this);
    }

    private IEnumerator SpawnChickens()
    {
        if (targetCount <= 0)
        {
            yield break;
        }

        float interval = targetCount > 1 ? spawnDuration / (targetCount - 1) : 0f;

        for (int i = 0; i < targetCount; i++)
        {
            SpawnChicken();

            if (i < targetCount - 1 && interval > 0f)
            {
                yield return new WaitForSeconds(interval);
            }
        }
    }

    private bool SpawnChicken()
    {
        return SpawnChicken(spawnVolume.bounds);
    }

    public void SpawnStarterChickens(int count)
    {
        if (chickenPrefab == null || spawnVolume == null)
        {
            return;
        }

        int availableSlots = Mathf.Max(
            0,
            ChickenController.MaximumChickenCount
            - (PenExpansionManager.Instance != null
                ? PenExpansionManager.Instance.GetChickenCount(
                    PenExpansionManager.Instance.GetClosestPenIndex(
                        spawnVolume.bounds.center))
                : ChickenController.ActiveInstances.Count));
        int spawnCount = Mathf.Min(Mathf.Max(0, count), availableSlots);
        for (int index = 0; index < spawnCount; index++)
        {
            SpawnChicken();
        }
    }

    public bool HasChickenNavMeshInVolume()
    {
        if (spawnVolume == null)
        {
            spawnVolume = GetComponent<BoxCollider>();
        }

        return spawnVolume != null
            && TryFindSpawnPosition(spawnVolume.bounds, out _);
    }

    private bool SpawnChicken(Bounds penBounds)
    {
        if (!TryFindSpawnPosition(penBounds, out Vector3 position))
        {
            if (!hasReportedMissingSpawnNavMesh)
            {
                Debug.LogError(
                    $"Could not find a chicken NavMesh inside {name}; no chicken was spawned.",
                    this);
                hasReportedMissingSpawnNavMesh = true;
            }

            return false;
        }

        float yRotation = Random.Range(-180f, 180f);

        Instantiate(chickenPrefab, position, Quaternion.Euler(0f, yRotation, 0f));
        spawnedPositions.Add(position);
        return true;
    }

    private bool TryFindSpawnPosition(Bounds penBounds, out Vector3 position)
    {
        Vector3 bestPosition = default;
        float bestNearestDistance = float.NegativeInfinity;
        float minimumSpacingSquared = minimumSpacing * minimumSpacing;
        bool foundNavMeshPosition = false;

        for (int attempt = 0; attempt < placementAttempts; attempt++)
        {
            if (!TryGetRandomPointInBounds(penBounds, out Vector3 candidate))
            {
                continue;
            }

            foundNavMeshPosition = true;
            float nearestDistance = NearestSpawnDistanceSquared(candidate);

            if (nearestDistance >= minimumSpacingSquared)
            {
                position = candidate;
                return true;
            }

            if (nearestDistance > bestNearestDistance)
            {
                bestPosition = candidate;
                bestNearestDistance = nearestDistance;
            }
        }

        // A crowded volume may not have room for the requested spacing. Use the
        // best candidate found so the spawner still reaches its target count.
        position = bestPosition;
        return foundNavMeshPosition;
    }

    private bool TryGetRandomPointInBounds(Bounds bounds, out Vector3 position)
    {
        float insetX = Mathf.Min(0.2f, bounds.size.x * 0.1f);
        float insetZ = Mathf.Min(0.2f, bounds.size.z * 0.1f);
        Vector3 worldPoint = new Vector3(
            Random.Range(bounds.min.x + insetX, bounds.max.x - insetX),
            bounds.center.y,
            Random.Range(bounds.min.z + insetZ, bounds.max.z - insetZ));

        NavMeshQueryFilter queryFilter = new NavMeshQueryFilter
        {
            agentTypeID = chickenAgentTypeId,
            areaMask = NavMesh.AllAreas
        };

        if (NavMesh.SamplePosition(
                worldPoint,
                out NavMeshHit hit,
                SpawnNavMeshSampleDistance,
                queryFilter)
            && hit.position.x >= bounds.min.x
            && hit.position.x <= bounds.max.x
            && hit.position.z >= bounds.min.z
            && hit.position.z <= bounds.max.z)
        {
            position = hit.position;
            return true;
        }

        position = default;
        return false;
    }

    public void RebuildPenNavMesh()
    {
        if (!buildNavMeshAtRuntime)
        {
            return;
        }

        if (spawnVolume == null)
        {
            spawnVolume = GetComponent<BoxCollider>();
        }

        BuildPenNavMesh();
    }

    public bool TryUseNavMeshDataFrom(DebugChickenSpawner source)
    {
        if (!buildNavMeshAtRuntime || source == null)
        {
            return false;
        }

        if (spawnVolume == null)
        {
            spawnVolume = GetComponent<BoxCollider>();
        }

        NavMeshSurface sourceSurface = source.GetComponent<NavMeshSurface>();
        if (sourceSurface == null || sourceSurface.navMeshData == null)
        {
            return false;
        }

        // Every runtime pen is an offset copy of the authored pen. NavMeshData
        // is local to its surface transform, so the original bake can be
        // instanced at the cloned volume without rebuilding identical geometry.
        NavMeshSurface surface = ConfigurePenNavMeshSurface();
        surface.RemoveData();
        surface.navMeshData = sourceSurface.navMeshData;
        if (surface.isActiveAndEnabled)
        {
            surface.AddData();
        }

        return surface.navMeshData != null;
    }

    public static void BeginSuppressAutomaticNavMeshBuild()
    {
        automaticNavMeshBuildSuppressionDepth++;
    }

    public static void EndSuppressAutomaticNavMeshBuild()
    {
        automaticNavMeshBuildSuppressionDepth = Mathf.Max(
            0,
            automaticNavMeshBuildSuppressionDepth - 1);
    }

    private void BuildPenNavMesh()
    {
        NavMeshSurface surface = ConfigurePenNavMeshSurface();
        surface.BuildNavMesh();

        if (surface.navMeshData == null)
        {
            Debug.LogError($"Could not build a NavMesh inside {name}.", this);
        }
    }

    private NavMeshSurface ConfigurePenNavMeshSurface()
    {
        NavMeshSurface surface = GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            surface = gameObject.AddComponent<NavMeshSurface>();
        }

        Bounds worldBounds = spawnVolume.bounds;
        Bounds localBounds = TransformWorldBoundsToLocal(worldBounds, transform);
        Vector3 volumeSize = localBounds.size;
        volumeSize.y = Mathf.Max(volumeSize.y, navMeshVolumeHeight);

        surface.agentTypeID = chickenAgentTypeId;
        surface.collectObjects = CollectObjects.Volume;
        surface.size = volumeSize;
        surface.center = localBounds.center;
        surface.layerMask = navMeshSourceLayers;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.minRegionArea = 0f;
        surface.buildHeightMesh = true;
        return surface;
    }

    private static Bounds TransformWorldBoundsToLocal(Bounds worldBounds, Transform target)
    {
        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 worldCorner = worldBounds.center + Vector3.Scale(
                        worldBounds.extents,
                        new Vector3(x, y, z));
                    Vector3 localCorner = target.InverseTransformPoint(worldCorner);
                    minimum = Vector3.Min(minimum, localCorner);
                    maximum = Vector3.Max(maximum, localCorner);
                }
            }
        }

        return new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
    }

    private float NearestSpawnDistanceSquared(Vector3 candidate)
    {
        if (spawnedPositions.Count == 0)
        {
            return float.PositiveInfinity;
        }

        float nearestDistance = float.PositiveInfinity;

        foreach (Vector3 position in spawnedPositions)
        {
            Vector2 offset = new Vector2(candidate.x - position.x, candidate.z - position.z);
            nearestDistance = Mathf.Min(nearestDistance, offset.sqrMagnitude);
        }

        return nearestDistance;
    }

    private void OnValidate()
    {
        targetCount = Mathf.Max(0, targetCount);
        spawnDuration = Mathf.Max(0f, spawnDuration);
        minimumSpacing = Mathf.Max(0f, minimumSpacing);
        placementAttempts = Mathf.Max(1, placementAttempts);
        navMeshVolumeHeight = Mathf.Max(0.1f, navMeshVolumeHeight);
    }
}
