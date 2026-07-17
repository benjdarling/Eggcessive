using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class ChickenController : MonoBehaviour
{
    private enum ChickenState
    {
        Idle,
        Moving,
        EggLaying
    }

    private static readonly List<ChickenController> ActiveChickens = new List<ChickenController>();
    private static bool hasWarnedAboutMissingNavMesh;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float minIdleTime = 1f;
    [SerializeField, Min(0f)] private float maxIdleTime = 3f;
    [SerializeField, Min(0f)] private float wanderRadius = 1f;
    [SerializeField, Min(0.01f)] private float navMeshSampleDistance = 0.75f;
    [SerializeField, Min(1)] private int destinationAttempts = 12;
    [SerializeField, Min(0.01f)] private float moveSpeed = 0.6f;
    [SerializeField, Min(0.01f)] private float acceleration = 2f;
    [SerializeField, Min(0f)] private float angularSpeed = 360f;

    [Header("Separation")]
    [SerializeField, Min(0f)] private float separationRadius = 0.3f;
    [SerializeField, Min(0f)] private float separationStrength = 0.45f;
    [SerializeField, Min(0f)] private float eggPushRadius = 0.25f;
    [SerializeField, Min(0f)] private float eggPushForce = 0.35f;

    [Header("Egg Laying")]
    [SerializeField] private GameObject eggPrefab = null;
    [SerializeField, Min(0f)] private float minEggLayTime = 6f;
    [SerializeField, Min(0f)] private float maxEggLayTime = 12f;
    [SerializeField, Min(0f)] private float eggLayingDuration = 1f;
    [SerializeField, Min(0f)] private float eggSpawnHeight = 0.08f;
    [SerializeField, Min(0f)] private float eggSpawnBehindDistance = 0.06f;

    private readonly Collider[] eggColliderBuffer = new Collider[16];
    private NavMeshPath path;

    private NavMeshAgent agent;
    private NavMeshQueryFilter navMeshQueryFilter;
    private ChickenState state;
    private float stateEndTime;
    private float nextEggTime;
    private bool navigationReady;

    private void Awake()
    {
        path = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>();
        agent.speed = moveSpeed;
        agent.acceleration = acceleration;
        agent.angularSpeed = angularSpeed;
        agent.stoppingDistance = 0.03f;
        agent.autoBraking = true;
        agent.autoRepath = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance;
        agent.avoidancePriority = Random.Range(20, 81);

        navMeshQueryFilter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };
    }

    private void OnEnable()
    {
        ActiveChickens.Add(this);
        ScheduleNextEgg();
    }

    private void Start()
    {
        TryInitializeNavigation();
        BeginIdle();
    }

    private void OnDisable()
    {
        ActiveChickens.Remove(this);
    }

    private void Update()
    {
        if (!navigationReady)
        {
            TryInitializeNavigation();
            return;
        }

        ApplyChickenSeparation();

        if (state == ChickenState.Idle)
        {
            if (Time.time >= nextEggTime)
            {
                BeginEggLaying();
                return;
            }

            if (Time.time >= stateEndTime)
            {
                ChooseDestination();
            }

            return;
        }

        if (state == ChickenState.EggLaying)
        {
            if (Time.time >= stateEndTime)
            {
                LayEgg();
                ScheduleNextEgg();
                BeginIdle();
            }

            return;
        }

        if (HasReachedDestination() || Time.time >= stateEndTime)
        {
            BeginIdle();
        }
    }

    private void BeginEggLaying()
    {
        state = ChickenState.EggLaying;
        stateEndTime = Time.time + eggLayingDuration;

        if (navigationReady && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }
    }

    private void FixedUpdate()
    {
        PushNearbyEggs();
    }

    private void TryInitializeNavigation()
    {
        if (agent.isOnNavMesh)
        {
            navigationReady = true;
            return;
        }

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navMeshSampleDistance, navMeshQueryFilter)
            && agent.Warp(hit.position))
        {
            navigationReady = true;
            return;
        }

        if (!hasWarnedAboutMissingNavMesh)
        {
            Debug.LogWarning("Chickens could not find a NavMesh. Ensure the pen NavMesh is being built.", this);
            hasWarnedAboutMissingNavMesh = true;
        }
    }

    private void BeginIdle()
    {
        state = ChickenState.Idle;
        stateEndTime = Time.time + Random.Range(minIdleTime, maxIdleTime);

        if (navigationReady && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }
    }

    private void ChooseDestination()
    {
        for (int attempt = 0; attempt < destinationAttempts; attempt++)
        {
            Vector2 randomOffset = Random.insideUnitCircle * wanderRadius;
            Vector3 requestedPosition = transform.position + new Vector3(randomOffset.x, 0f, randomOffset.y);

            if (!NavMesh.SamplePosition(
                    requestedPosition,
                    out NavMeshHit hit,
                    navMeshSampleDistance,
                    navMeshQueryFilter))
            {
                continue;
            }

            if (!NavMesh.CalculatePath(transform.position, hit.position, navMeshQueryFilter, path)
                || path.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }

            agent.SetPath(path);
            state = ChickenState.Moving;
            stateEndTime = Time.time + CalculateMovementTimeout(path);
            return;
        }

        BeginIdle();
    }

    private float CalculateMovementTimeout(NavMeshPath movementPath)
    {
        float pathLength = 0f;

        for (int i = 1; i < movementPath.corners.Length; i++)
        {
            pathLength += Vector3.Distance(movementPath.corners[i - 1], movementPath.corners[i]);
        }

        return Mathf.Max(2f, pathLength / moveSpeed + 2f);
    }

    private bool HasReachedDestination()
    {
        if (agent.pathPending)
        {
            return false;
        }

        return !agent.hasPath
            || agent.remainingDistance <= agent.stoppingDistance + 0.03f;
    }

    private void ApplyChickenSeparation()
    {
        if (separationRadius <= 0f || separationStrength <= 0f || !agent.isOnNavMesh)
        {
            return;
        }

        Vector3 separation = Vector3.zero;
        float radiusSquared = separationRadius * separationRadius;

        foreach (ChickenController other in ActiveChickens)
        {
            if (other == this)
            {
                continue;
            }

            Vector3 offset = transform.position - other.transform.position;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;

            if (distanceSquared >= radiusSquared)
            {
                continue;
            }

            if (distanceSquared < 0.000001f)
            {
                offset = GetInstanceID() < other.GetInstanceID() ? Vector3.left : Vector3.right;
                distanceSquared = 0.000001f;
            }

            float distance = Mathf.Sqrt(distanceSquared);
            separation += offset / distance * (1f - distance / separationRadius);
        }

        if (separation.sqrMagnitude > 1f)
        {
            separation.Normalize();
        }

        agent.Move(separation * (separationStrength * Time.deltaTime));
    }

    private void PushNearbyEggs()
    {
        if (eggPushRadius <= 0f || eggPushForce <= 0f)
        {
            return;
        }

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            eggPushRadius,
            eggColliderBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        float radiusSquared = eggPushRadius * eggPushRadius;

        for (int i = 0; i < hitCount; i++)
        {
            Rigidbody eggBody = eggColliderBuffer[i].attachedRigidbody;

            if (eggBody == null || eggBody.isKinematic || !eggBody.TryGetComponent(out ChickenEgg _))
            {
                continue;
            }

            Vector3 offset = eggBody.worldCenterOfMass - transform.position;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;

            if (distanceSquared >= radiusSquared)
            {
                continue;
            }

            if (distanceSquared < 0.000001f)
            {
                offset = transform.right;
                distanceSquared = 0.000001f;
            }

            float distance = Mathf.Sqrt(distanceSquared);
            float proximity = 1f - distance / eggPushRadius;
            eggBody.AddForce(offset / distance * (eggPushForce * proximity), ForceMode.Acceleration);
        }
    }

    private void LayEgg()
    {
        if (eggPrefab == null)
        {
            return;
        }

        Vector3 eggPosition = transform.position
            + Vector3.up * eggSpawnHeight
            - transform.forward * eggSpawnBehindDistance;
        Quaternion eggRotation = Quaternion.Euler(0f, Random.Range(-180f, 180f), 0f);
        GameObject egg = Instantiate(eggPrefab, eggPosition, eggRotation);

        if (!egg.TryGetComponent(out ChickenEgg _))
        {
            egg.AddComponent<ChickenEgg>();
        }
    }

    private void ScheduleNextEgg()
    {
        nextEggTime = Time.time + Random.Range(minEggLayTime, maxEggLayTime);
    }

    private void OnValidate()
    {
        minIdleTime = Mathf.Max(0f, minIdleTime);
        maxIdleTime = Mathf.Max(minIdleTime, maxIdleTime);
        wanderRadius = Mathf.Max(0f, wanderRadius);
        navMeshSampleDistance = Mathf.Max(0.01f, navMeshSampleDistance);
        destinationAttempts = Mathf.Max(1, destinationAttempts);
        moveSpeed = Mathf.Max(0.01f, moveSpeed);
        acceleration = Mathf.Max(0.01f, acceleration);
        angularSpeed = Mathf.Max(0f, angularSpeed);
        separationRadius = Mathf.Max(0f, separationRadius);
        separationStrength = Mathf.Max(0f, separationStrength);
        eggPushRadius = Mathf.Max(0f, eggPushRadius);
        eggPushForce = Mathf.Max(0f, eggPushForce);
        minEggLayTime = Mathf.Max(0f, minEggLayTime);
        maxEggLayTime = Mathf.Max(minEggLayTime, maxEggLayTime);
        eggLayingDuration = Mathf.Max(0f, eggLayingDuration);
        eggSpawnHeight = Mathf.Max(0f, eggSpawnHeight);
        eggSpawnBehindDistance = Mathf.Max(0f, eggSpawnBehindDistance);
    }
}
