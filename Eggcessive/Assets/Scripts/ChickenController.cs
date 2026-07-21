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
        SeekingFood,
        Eating,
        EggLaying
    }

    private static readonly List<ChickenController> ActiveChickens = new List<ChickenController>();
    private static readonly int IsEatingParameter = Animator.StringToHash("IsEating");
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

    [Header("Food")]
    [SerializeField, Min(0.01f)] private float maximumFoodScore = 100f;
    [SerializeField, Min(0f)] private float startingFoodScore = 45f;
    [SerializeField, Min(0f)] private float foodScoreDrainPerSecond = 0.75f;
    [SerializeField, Min(0f)] private float seekFoodBelowScore = 60f;
    [SerializeField, Min(0f)] private float returnToWanderingScore = 90f;
    [SerializeField, Min(0.01f)] private float foodSearchInterval = 1f;
    [SerializeField, Min(0.01f)] private float eatingDistance = 0.2f;
    [SerializeField, Min(0.01f)] private float foodPerBite = 10f;
    [SerializeField, Min(0.01f)] private float secondsPerBite = 0.65f;

    [Header("Animation")]
    [SerializeField] private Animator animator = null;

    [Header("Separation")]
    [SerializeField, Min(0f)] private float separationRadius = 0.3f;
    [SerializeField, Min(0f)] private float separationStrength = 0.45f;
    [SerializeField, Min(0f)] private float eggPushRadius = 0.25f;
    [SerializeField, Min(0f)] private float eggPushForce = 0.35f;

    [Header("Egg Laying")]
    [SerializeField] private GameObject eggPrefab = null;
    [SerializeField, Min(0f)] private float minEggLayTime = 6f;
    [SerializeField, Min(0f)] private float maxEggLayTime = 12f;
    [SerializeField, Min(0.01f)] private float emptyFoodEggIntervalMultiplier = 2f;
    [SerializeField, Min(0.01f)] private float fullFoodEggIntervalMultiplier = 0.55f;
    [SerializeField, Min(0f)] private float eggLayingDuration = 1f;
    [SerializeField, Min(0f)] private float eggSpawnHeight = 0.08f;
    [SerializeField, Min(0f)] private float eggSpawnBehindDistance = 0.06f;

    private readonly Collider[] eggColliderBuffer = new Collider[16];
    private NavMeshPath path;

    private NavMeshAgent agent;
    private NavMeshQueryFilter navMeshQueryFilter;
    private ChickenState state;
    private FoodPile targetFood;
    private float stateEndTime;
    private float nextFoodSearchTime;
    private float nextBiteTime;
    private float eggTimerRemaining;
    private float foodScore;
    private bool navigationReady;

    public float FoodScore => foodScore;
    public float MaximumFoodScore => maximumFoodScore;
    public float FoodScoreNormalized => maximumFoodScore > 0f ? foodScore / maximumFoodScore : 0f;

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

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        foodScore = Mathf.Clamp(startingFoodScore, 0f, maximumFoodScore);
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
        SetEatingAnimation(false);
        targetFood = null;
    }

    private void Update()
    {
        UpdateFoodAndEggTimers();

        if (!navigationReady)
        {
            TryInitializeNavigation();
            return;
        }

        ApplyChickenSeparation();

        if (eggTimerRemaining <= 0f
            && state != ChickenState.Eating
            && state != ChickenState.EggLaying)
        {
            BeginEggLaying();
            return;
        }

        switch (state)
        {
            case ChickenState.Idle:
                UpdateIdle();
                break;
            case ChickenState.Moving:
                UpdateMoving();
                break;
            case ChickenState.SeekingFood:
                UpdateSeekingFood();
                break;
            case ChickenState.Eating:
                UpdateEating();
                break;
            case ChickenState.EggLaying:
                UpdateEggLaying();
                break;
        }
    }

    private void FixedUpdate()
    {
        PushNearbyEggs();
    }

    private void UpdateFoodAndEggTimers()
    {
        foodScore = Mathf.Max(0f, foodScore - foodScoreDrainPerSecond * Time.deltaTime);

        float eggIntervalMultiplier = Mathf.Lerp(
            emptyFoodEggIntervalMultiplier,
            fullFoodEggIntervalMultiplier,
            FoodScoreNormalized);
        eggTimerRemaining -= Time.deltaTime / Mathf.Max(0.01f, eggIntervalMultiplier);
    }

    private void UpdateIdle()
    {
        if (TrySeekFoodWhenHungry())
        {
            return;
        }

        if (Time.time >= stateEndTime)
        {
            ChooseDestination();
        }
    }

    private void UpdateMoving()
    {
        if (TrySeekFoodWhenHungry())
        {
            return;
        }

        if (HasReachedDestination() || Time.time >= stateEndTime)
        {
            BeginIdle();
        }
    }

    private void UpdateSeekingFood()
    {
        if (foodScore >= returnToWanderingScore)
        {
            BeginIdle();
            return;
        }

        if (targetFood == null || !targetFood.IsAvailable)
        {
            targetFood = null;
            nextFoodSearchTime = Time.time + foodSearchInterval;
            BeginIdle();
            return;
        }

        Vector3 planarOffset = targetFood.transform.position - transform.position;
        planarOffset.y = 0f;

        if (planarOffset.sqrMagnitude <= eatingDistance * eatingDistance || HasReachedDestination())
        {
            BeginEating();
            return;
        }

        if (Time.time >= stateEndTime)
        {
            targetFood = null;
            nextFoodSearchTime = Time.time + foodSearchInterval;
            BeginIdle();
        }
    }

    private void UpdateEating()
    {
        if (targetFood == null || !targetFood.IsAvailable || foodScore >= returnToWanderingScore)
        {
            FinishEating();
            return;
        }

        FaceTargetFood();

        if (Time.time < nextBiteTime)
        {
            return;
        }

        float missingFood = returnToWanderingScore - foodScore;
        float amountRequested = Mathf.Min(foodPerBite, missingFood);
        float amountConsumed = targetFood.Consume(amountRequested);

        if (amountConsumed <= 0f)
        {
            FinishEating();
            return;
        }

        foodScore = Mathf.Min(maximumFoodScore, foodScore + amountConsumed);

        // Leave the eating state on the same frame that this bite satisfies the
        // chicken. Otherwise the hunger drain on the next frame drops the score
        // just below the threshold and causes endless tiny top-up bites.
        if (foodScore >= returnToWanderingScore - 0.0001f)
        {
            FinishEating();
            return;
        }

        nextBiteTime = Time.time + secondsPerBite;
    }

    private void UpdateEggLaying()
    {
        if (Time.time < stateEndTime)
        {
            return;
        }

        LayEgg();
        ScheduleNextEgg();
        BeginIdle();
    }

    private bool TrySeekFoodWhenHungry()
    {
        if (foodScore >= seekFoodBelowScore || Time.time < nextFoodSearchTime)
        {
            return false;
        }

        nextFoodSearchTime = Time.time + foodSearchInterval;

        FoodPile bestFood = null;
        float bestDistanceSquared = float.PositiveInfinity;

        foreach (FoodPile foodPile in FoodPile.ActivePiles)
        {
            if (foodPile == null || !foodPile.IsAvailable)
            {
                continue;
            }

            float distanceSquared = (foodPile.transform.position - transform.position).sqrMagnitude;

            if (distanceSquared >= bestDistanceSquared
                || !TryCalculateCompletePath(foodPile.transform.position))
            {
                continue;
            }

            bestFood = foodPile;
            bestDistanceSquared = distanceSquared;
        }

        if (bestFood == null || !TryCalculateCompletePath(bestFood.transform.position))
        {
            return false;
        }

        targetFood = bestFood;
        agent.stoppingDistance = eatingDistance * 0.75f;
        agent.SetPath(path);
        state = ChickenState.SeekingFood;
        stateEndTime = Time.time + CalculateMovementTimeout(path);
        return true;
    }

    private bool TryCalculateCompletePath(Vector3 destination)
    {
        if (!NavMesh.SamplePosition(
                destination,
                out NavMeshHit hit,
                navMeshSampleDistance,
                navMeshQueryFilter))
        {
            return false;
        }

        return NavMesh.CalculatePath(transform.position, hit.position, navMeshQueryFilter, path)
            && path.status == NavMeshPathStatus.PathComplete;
    }

    private void BeginEating()
    {
        state = ChickenState.Eating;
        nextBiteTime = Time.time;

        if (agent.isOnNavMesh)
        {
            agent.ResetPath();
        }

        SetEatingAnimation(true);
    }

    private void FinishEating()
    {
        SetEatingAnimation(false);
        targetFood = null;
        nextFoodSearchTime = Time.time + foodSearchInterval;
        BeginIdle();
    }

    private void FaceTargetFood()
    {
        Vector3 direction = targetFood.transform.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            angularSpeed * Time.deltaTime);
    }

    private void BeginEggLaying()
    {
        state = ChickenState.EggLaying;
        stateEndTime = Time.time + eggLayingDuration;
        targetFood = null;
        SetEatingAnimation(false);

        if (navigationReady && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }
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
        agent.stoppingDistance = 0.03f;
        SetEatingAnimation(false);

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

            agent.stoppingDistance = 0.03f;
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
        eggTimerRemaining = Random.Range(minEggLayTime, maxEggLayTime);
    }

    private void SetEatingAnimation(bool isEating)
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetBool(IsEatingParameter, isEating);
        }
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
        maximumFoodScore = Mathf.Max(0.01f, maximumFoodScore);
        startingFoodScore = Mathf.Clamp(startingFoodScore, 0f, maximumFoodScore);
        foodScoreDrainPerSecond = Mathf.Max(0f, foodScoreDrainPerSecond);
        seekFoodBelowScore = Mathf.Clamp(seekFoodBelowScore, 0f, maximumFoodScore);
        returnToWanderingScore = Mathf.Clamp(
            returnToWanderingScore,
            seekFoodBelowScore,
            maximumFoodScore);
        foodSearchInterval = Mathf.Max(0.01f, foodSearchInterval);
        eatingDistance = Mathf.Max(0.01f, eatingDistance);
        foodPerBite = Mathf.Max(0.01f, foodPerBite);
        secondsPerBite = Mathf.Max(0.01f, secondsPerBite);
        separationRadius = Mathf.Max(0f, separationRadius);
        separationStrength = Mathf.Max(0f, separationStrength);
        eggPushRadius = Mathf.Max(0f, eggPushRadius);
        eggPushForce = Mathf.Max(0f, eggPushForce);
        minEggLayTime = Mathf.Max(0f, minEggLayTime);
        maxEggLayTime = Mathf.Max(minEggLayTime, maxEggLayTime);
        emptyFoodEggIntervalMultiplier = Mathf.Max(0.01f, emptyFoodEggIntervalMultiplier);
        fullFoodEggIntervalMultiplier = Mathf.Max(0.01f, fullFoodEggIntervalMultiplier);
        eggLayingDuration = Mathf.Max(0f, eggLayingDuration);
        eggSpawnHeight = Mathf.Max(0f, eggSpawnHeight);
        eggSpawnBehindDistance = Mathf.Max(0f, eggSpawnBehindDistance);
    }
}
