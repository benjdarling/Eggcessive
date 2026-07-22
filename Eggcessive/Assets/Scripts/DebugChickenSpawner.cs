using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(BoxCollider))]
public class DebugChickenSpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private GameObject chickenPrefab = null;
    [SerializeField, Min(0)] private int targetCount = 10;
    [SerializeField, Min(0f)] private float spawnDuration = 5f;
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

    private void Awake()
    {
        spawnVolume = GetComponent<BoxCollider>();

        if (buildNavMeshAtRuntime)
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

    private void SpawnChicken()
    {
        Vector3 position = FindSpawnPosition();
        float yRotation = Random.Range(-180f, 180f);

        Instantiate(chickenPrefab, position, Quaternion.Euler(0f, yRotation, 0f));
        spawnedPositions.Add(position);
    }

    private Vector3 FindSpawnPosition()
    {
        Vector3 bestPosition = GetRandomPointInVolume();
        float bestNearestDistance = NearestSpawnDistanceSquared(bestPosition);
        float minimumSpacingSquared = minimumSpacing * minimumSpacing;

        for (int attempt = 0; attempt < placementAttempts; attempt++)
        {
            Vector3 candidate = GetRandomPointInVolume();
            float nearestDistance = NearestSpawnDistanceSquared(candidate);

            if (nearestDistance >= minimumSpacingSquared)
            {
                return candidate;
            }

            if (nearestDistance > bestNearestDistance)
            {
                bestPosition = candidate;
                bestNearestDistance = nearestDistance;
            }
        }

        // A crowded volume may not have room for the requested spacing. Use the
        // best candidate found so the spawner still reaches its target count.
        return bestPosition;
    }

    private Vector3 GetRandomPointInVolume()
    {
        Vector3 halfSize = spawnVolume.size * 0.5f;
        Vector3 localPoint = spawnVolume.center + new Vector3(
            Random.Range(-halfSize.x, halfSize.x),
            0f,
            Random.Range(-halfSize.z, halfSize.z));

        Vector3 worldPoint = spawnVolume.transform.TransformPoint(localPoint);
        worldPoint.y = 0f;

        NavMeshQueryFilter queryFilter = new NavMeshQueryFilter
        {
            agentTypeID = chickenAgentTypeId,
            areaMask = NavMesh.AllAreas
        };

        if (NavMesh.SamplePosition(worldPoint, out NavMeshHit hit, 0.5f, queryFilter))
        {
            return hit.position;
        }

        return worldPoint;
    }

    private void BuildPenNavMesh()
    {
        NavMeshSurface surface = GetComponent<NavMeshSurface>();

        if (surface == null)
        {
            surface = gameObject.AddComponent<NavMeshSurface>();
        }

        Vector3 volumeSize = spawnVolume.size;
        volumeSize.y = Mathf.Max(volumeSize.y, navMeshVolumeHeight);

        surface.agentTypeID = chickenAgentTypeId;
        surface.collectObjects = CollectObjects.Volume;
        surface.size = volumeSize;
        surface.center = spawnVolume.center;
        surface.layerMask = navMeshSourceLayers;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.minRegionArea = 0f;
        surface.buildHeightMesh = true;
        surface.BuildNavMesh();

        if (surface.navMeshData == null)
        {
            Debug.LogError($"Could not build a NavMesh inside {name}.", this);
        }
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
