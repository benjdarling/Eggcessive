using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CapsuleCollider))]
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
    private static readonly int BlinkParameter = Animator.StringToHash("Blink");
    private static readonly int BlinkSpeedParameter = Animator.StringToHash("BlinkSpeed");
    private static readonly int TurnLeanParameter = Animator.StringToHash("TurnLean");
    private static readonly int LayEggParameter = Animator.StringToHash("LayEgg");
    private static readonly int LayEggState = Animator.StringToHash("Base Layer.Lay Egg");
    private const float EggSpawnFrame = 9f;
    private const float DefaultLayEggFrameCount = 22f;
    private const string WingFlutterLayerName = "Wing Flutter Layer";
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
    [SerializeField, Min(0.01f)] private float minBlinkInterval = 2f;
    [SerializeField, Min(0.01f)] private float maxBlinkInterval = 6f;
    [SerializeField, Range(0f, 0.5f)] private float blinkSpeedVariation = 0.1f;
    [SerializeField, Min(1f)] private float fullLeanTurnRate = 180f;
    [SerializeField, Min(0.01f)] private float leanSmoothTime = 0.08f;
    [SerializeField, Range(0f, 1f)] private float leanStrength = 1f;

    [Header("Wing Flutter")]
    [SerializeField, Min(0.01f)] private float minWingFlutterInterval = 6f;
    [SerializeField, Min(0.01f)] private float maxWingFlutterInterval = 14f;
    [SerializeField, Range(0f, 1f)] private float minWingFlutterStrength = 0.125f;
    [SerializeField, Range(0f, 1f)] private float maxWingFlutterStrength = 0.35f;
    [Tooltip("How long a larger sequence of irregular micro-flutters lasts.")]
    [SerializeField, Min(0.01f)] private float minWingFlutterDuration = 0.45f;
    [SerializeField, Min(0.01f)] private float maxWingFlutterDuration = 0.95f;
    [Tooltip("Random on/off timing inside each larger flutter sequence.")]
    [SerializeField, Min(0.01f)] private float minWingFlutterPulseInterval = 0.035f;
    [SerializeField, Min(0.01f)] private float maxWingFlutterPulseInterval = 0.09f;

    [Header("Wing Micro Twitches")]
    [SerializeField, Min(0.01f)] private float minWingMicroTwitchInterval = 0.55f;
    [SerializeField, Min(0.01f)] private float maxWingMicroTwitchInterval = 2f;
    [SerializeField, Range(0f, 1f)] private float minWingMicroTwitchStrength = 0.025f;
    [SerializeField, Range(0f, 1f)] private float maxWingMicroTwitchStrength = 0.12f;
    [SerializeField, Min(0.01f)] private float minWingMicroTwitchDuration = 0.05f;
    [SerializeField, Min(0.01f)] private float maxWingMicroTwitchDuration = 0.14f;

    [Header("Separation")]
    [SerializeField, Min(0f)] private float separationRadius = 0.3f;
    [SerializeField, Min(0f)] private float separationStrength = 0.45f;
    [Tooltip("How far beyond the chicken's body collider eggs begin to receive a gentle nudge.")]
    [SerializeField, Min(0f)] private float eggPushRadius = 0.025f;
    [SerializeField, Min(0f)] private float eggPushForce = 3.25f;
    [SerializeField, Min(0.01f)] private float maximumEggPushSpeed = 0.25f;

    [Header("Egg Laying")]
    [SerializeField] private GameObject eggPrefab = null;
    [SerializeField, Min(0f)] private float minEggLayTime = 6f;
    [SerializeField, Min(0f)] private float maxEggLayTime = 12f;
    [SerializeField, Min(0.01f)] private float emptyFoodEggIntervalMultiplier = 2f;
    [SerializeField, Min(0.01f)] private float fullFoodEggIntervalMultiplier = 0.55f;
    [SerializeField, Min(0f)] private float eggLayingDuration = 1f;
    [Tooltip("Bone used as the physical launch point. A Blender axis suffix such as '.x' is matched automatically.")]
    [SerializeField] private string eggSpawnBoneName = "spine_01";
    [SerializeField, Min(0f)] private float eggLaunchSpeed = 3f;
    [SerializeField, Range(0f, 1f)] private float eggLaunchSpeedVariation = 0.1f;
    [SerializeField, Min(0f)] private float eggLaunchSpin = 12f;
    [Header("Egg Laying Fallback Position")]
    [SerializeField, Min(0f)] private float eggSpawnHeight = 0.08f;
    [SerializeField, Min(0f)] private float eggSpawnBehindDistance = 0.06f;

    private readonly Collider[] eggColliderBuffer = new Collider[16];
    private NavMeshPath path;

    private NavMeshAgent agent;
    private CapsuleCollider bodyCollider;
    private NavMeshQueryFilter navMeshQueryFilter;
    private ChickenState state;
    private FoodPile targetFood;
    private float stateEndTime;
    private float nextFoodSearchTime;
    private float nextBiteTime;
    private float eggTimerRemaining;
    private float foodScore;
    private float nextBlinkTime;
    private float turnLean;
    private float turnLeanVelocity;
    private int wingFlutterLayerIndex = -1;
    private float nextWingFlutterTime;
    private float wingFlutterStartTime;
    private float wingFlutterDuration;
    private float wingFlutterStrength;
    private float wingFlutterWeight;
    private float nextWingFlutterPulseTime;
    private bool wingFlutterPulseOn;
    private bool wingFlutterActive;
    private float nextWingMicroTwitchTime;
    private float wingMicroTwitchStartTime;
    private float wingMicroTwitchDuration;
    private float wingMicroTwitchStrength;
    private bool wingMicroTwitchActive;
    private Vector3 previousPlanarForward;
    private bool navigationReady;
    private Transform eggSpawnBone;
    private bool eggSpawnedDuringLay;
    private float eggSpawnNormalizedTime = EggSpawnFrame / DefaultLayEggFrameCount;

    public float FoodScore => foodScore;
    public float MaximumFoodScore => maximumFoodScore;
    public float FoodScoreNormalized => maximumFoodScore > 0f ? foodScore / maximumFoodScore : 0f;
    public static IReadOnlyList<ChickenController> ActiveInstances => ActiveChickens;

    private void Awake()
    {
        path = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>();
        bodyCollider = GetComponent<CapsuleCollider>();
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

        CacheLayEggAnimationTiming();
        eggSpawnBone = FindEggSpawnBone();

        CacheWingFlutterLayer();

        foodScore = Mathf.Clamp(startingFoodScore, 0f, maximumFoodScore);
        previousPlanarForward = GetPlanarForward();
    }

    private void OnEnable()
    {
        ActiveChickens.Add(this);
        ScheduleNextEgg();
        ScheduleNextBlink();
        ScheduleNextWingFlutter();
        ScheduleNextWingMicroTwitch();
        SetWingFlutterWeight(0f);
        wingFlutterActive = false;
        wingMicroTwitchActive = false;
        previousPlanarForward = GetPlanarForward();
        turnLean = 0f;
        turnLeanVelocity = 0f;
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
        if (animator != null)
        {
            animator.ResetTrigger(BlinkParameter);
            animator.ResetTrigger(LayEggParameter);
            animator.SetFloat(TurnLeanParameter, 0f);
            SetWingFlutterWeight(0f);
        }
        wingFlutterActive = false;
        wingMicroTwitchActive = false;
        targetFood = null;
    }

    private void Update()
    {
        // NavMeshAgent rotation has been applied by this point, while Animator
        // evaluation has not. Update the parameter now so the visible turn and
        // its additive lean use the same frame's direction.
        UpdateTurnLean();
        UpdateFoodAndEggTimers();
        UpdateBlink();
        UpdateWingFlutter();

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

    private void LateUpdate()
    {
        if (state == ChickenState.EggLaying && !eggSpawnedDuringLay)
        {
            TrySpawnEggAtAnimationFrame();
        }
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

        // Preserve egg production if the controller/clip is temporarily
        // missing or the animation was interrupted before frame 9.
        if (!eggSpawnedDuringLay)
        {
            LayEgg();
        }
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
        eggSpawnedDuringLay = false;
        targetFood = null;
        SetEatingAnimation(false);

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.ResetTrigger(LayEggParameter);
            animator.SetTrigger(LayEggParameter);
        }

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
        if (bodyCollider == null || eggPushForce <= 0f)
        {
            return;
        }

        Bounds bodyBounds = bodyCollider.bounds;
        Vector3 searchPadding = new Vector3(
            eggPushRadius,
            Mathf.Max(0.02f, eggPushRadius),
            eggPushRadius);
        int hitCount = Physics.OverlapBoxNonAlloc(
            bodyBounds.center,
            bodyBounds.extents + searchPadding,
            eggColliderBuffer,
            Quaternion.identity,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider eggCollider = eggColliderBuffer[i];
            Rigidbody eggBody = eggCollider.attachedRigidbody;

            if (eggBody == null
                || eggBody.isKinematic
                || !eggBody.TryGetComponent(out ChickenEgg egg)
                || egg.IsHeld
                || egg.IsCollected)
            {
                continue;
            }

            Vector3 pushDirection;
            float proximity;
            bool overlapping = Physics.ComputePenetration(
                eggCollider,
                eggCollider.transform.position,
                eggCollider.transform.rotation,
                bodyCollider,
                bodyCollider.transform.position,
                bodyCollider.transform.rotation,
                out Vector3 separationDirection,
                out float penetrationDepth);

            if (overlapping)
            {
                pushDirection = separationDirection;
                pushDirection.y = 0f;
                proximity = eggPushRadius > 0f
                    ? 1f + Mathf.Clamp01(penetrationDepth / eggPushRadius)
                    : 1f;
            }
            else
            {
                Vector3 pointOnChicken = bodyCollider.ClosestPoint(eggBody.worldCenterOfMass);
                Vector3 pointOnEgg = eggCollider.ClosestPoint(pointOnChicken);
                Vector3 surfaceGap = pointOnEgg - pointOnChicken;
                surfaceGap.y = 0f;
                float gap = surfaceGap.magnitude;

                if (eggPushRadius <= 0f || gap >= eggPushRadius)
                {
                    continue;
                }

                pushDirection = gap > 0.0001f ? surfaceGap / gap : Vector3.zero;
                proximity = 1f - gap / eggPushRadius;
            }

            if (pushDirection.sqrMagnitude < 0.0001f)
            {
                pushDirection = eggBody.worldCenterOfMass - bodyBounds.center;
                pushDirection.y = 0f;
            }

            if (pushDirection.sqrMagnitude < 0.0001f)
            {
                pushDirection = transform.right;
            }
            else
            {
                pushDirection.Normalize();
            }

            Vector3 planarEggVelocity = eggBody.linearVelocity;
            planarEggVelocity.y = 0f;
            float outwardSpeed = Mathf.Max(0f, Vector3.Dot(planarEggVelocity, pushDirection));

            if (outwardSpeed >= maximumEggPushSpeed)
            {
                continue;
            }

            float remainingSpeed = maximumEggPushSpeed - outwardSpeed;
            float pushAcceleration = Mathf.Min(
                eggPushForce * proximity,
                remainingSpeed / Mathf.Max(Time.fixedDeltaTime, 0.0001f));
            eggBody.AddForce(
                pushDirection * pushAcceleration,
                ForceMode.Acceleration);
        }
    }

    private void LayEgg()
    {
        if (eggPrefab == null || eggSpawnedDuringLay)
        {
            return;
        }

        eggSpawnedDuringLay = true;
        Vector3 eggPosition = eggSpawnBone != null
            ? eggSpawnBone.position
            : transform.position
                + Vector3.up * eggSpawnHeight
                - GetPlanarForward() * eggSpawnBehindDistance;
        Quaternion eggRotation = Quaternion.Euler(0f, Random.Range(-180f, 180f), 0f);
        GameObject egg = Instantiate(eggPrefab, eggPosition, eggRotation);

        if (!egg.TryGetComponent(out ChickenEgg _))
        {
            egg.AddComponent<ChickenEgg>();
        }

        if (egg.TryGetComponent(out Rigidbody eggBody) && !eggBody.isKinematic)
        {
            // Equal downward/back components produce a 45-degree launch.
            Vector3 launchDirection = (Vector3.down - GetPlanarForward()).normalized;
            float variedLaunchSpeed = eggLaunchSpeed * Random.Range(
                1f - eggLaunchSpeedVariation,
                1f + eggLaunchSpeedVariation);
            eggBody.linearVelocity = launchDirection * variedLaunchSpeed;
            eggBody.AddTorque(Random.onUnitSphere * eggLaunchSpin, ForceMode.VelocityChange);
        }
    }

    private void TrySpawnEggAtAnimationFrame()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        if (stateInfo.fullPathHash == LayEggState
            && stateInfo.normalizedTime >= eggSpawnNormalizedTime)
        {
            LayEgg();
        }
    }

    private void CacheLayEggAnimationTiming()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null
                || clip.name.IndexOf("layEgg", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            float frameCount = Mathf.Max(1f, Mathf.Round(clip.length * clip.frameRate));
            eggSpawnNormalizedTime = Mathf.Clamp01(EggSpawnFrame / frameCount);
            return;
        }
    }

    private Transform FindEggSpawnBone()
    {
        if (animator == null || string.IsNullOrWhiteSpace(eggSpawnBoneName))
        {
            return null;
        }

        Transform[] bones = animator.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
        {
            if (string.Equals(
                    bones[i].name,
                    eggSpawnBoneName,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return bones[i];
            }
        }

        string blenderAxisPrefix = eggSpawnBoneName + ".";
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].name.StartsWith(
                    blenderAxisPrefix,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return bones[i];
            }
        }

        Debug.LogWarning(
            $"{nameof(ChickenController)} could not find egg spawn bone '{eggSpawnBoneName}' below '{animator.name}'. Using the fallback position.",
            this);
        return null;
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

    private void UpdateBlink()
    {
        if (Time.time < nextBlinkTime)
        {
            return;
        }

        ScheduleNextBlink();
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        float minimumSpeed = Mathf.Max(0.01f, 1f - blinkSpeedVariation);
        float maximumSpeed = 1f + blinkSpeedVariation;
        animator.SetFloat(BlinkSpeedParameter, Random.Range(minimumSpeed, maximumSpeed));
        animator.SetTrigger(BlinkParameter);
    }

    private void ScheduleNextBlink()
    {
        nextBlinkTime = Time.time + Random.Range(minBlinkInterval, maxBlinkInterval);
    }

    private void UpdateWingFlutter()
    {
        if (wingFlutterLayerIndex < 0)
        {
            CacheWingFlutterLayer();
        }

        if (wingFlutterLayerIndex < 0)
        {
            return;
        }

        UpdateWingFlutterSequence();
        float microTwitchWeight = UpdateWingMicroTwitch();
        SetWingFlutterWeight(Mathf.Max(wingFlutterWeight, microTwitchWeight));
    }

    private void UpdateWingFlutterSequence()
    {
        if (!wingFlutterActive)
        {
            wingFlutterWeight = 0f;

            if (Time.time >= nextWingFlutterTime)
            {
                wingFlutterStartTime = Time.time;
                wingFlutterDuration = Random.Range(minWingFlutterDuration, maxWingFlutterDuration);
                wingFlutterStrength = Random.Range(minWingFlutterStrength, maxWingFlutterStrength);
                wingFlutterPulseOn = true;
                wingFlutterWeight = Random.Range(0.55f, 1f) * wingFlutterStrength;
                nextWingFlutterPulseTime = Time.time
                    + Random.Range(minWingFlutterPulseInterval, maxWingFlutterPulseInterval);
                wingFlutterActive = true;
            }

            return;
        }

        if (Time.time - wingFlutterStartTime >= wingFlutterDuration)
        {
            wingFlutterWeight = 0f;
            wingFlutterActive = false;
            ScheduleNextWingFlutter();
            return;
        }

        if (Time.time < nextWingFlutterPulseTime)
        {
            return;
        }

        // Hard, uneven on/off changes make the held additive pose read as a
        // cluster of nervous feather and wing movements rather than one flap.
        wingFlutterPulseOn = !wingFlutterPulseOn;
        wingFlutterWeight = wingFlutterPulseOn
            ? Random.Range(0.55f, 1f) * wingFlutterStrength
            : 0f;
        nextWingFlutterPulseTime = Time.time
            + Random.Range(minWingFlutterPulseInterval, maxWingFlutterPulseInterval);
    }

    private float UpdateWingMicroTwitch()
    {
        if (wingMicroTwitchActive)
        {
            float progress = (Time.time - wingMicroTwitchStartTime) / wingMicroTwitchDuration;

            if (progress < 1f)
            {
                return Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI) * wingMicroTwitchStrength;
            }

            wingMicroTwitchActive = false;
            ScheduleNextWingMicroTwitch();
        }

        if (Time.time < nextWingMicroTwitchTime)
        {
            return 0f;
        }

        wingMicroTwitchStartTime = Time.time;
        wingMicroTwitchDuration = Random.Range(
            minWingMicroTwitchDuration,
            maxWingMicroTwitchDuration);
        wingMicroTwitchStrength = Random.Range(
            minWingMicroTwitchStrength,
            maxWingMicroTwitchStrength);
        wingMicroTwitchActive = true;
        return 0f;
    }

    private void ScheduleNextWingFlutter()
    {
        nextWingFlutterTime = Time.time
            + Random.Range(minWingFlutterInterval, maxWingFlutterInterval);
    }

    private void ScheduleNextWingMicroTwitch()
    {
        nextWingMicroTwitchTime = Time.time
            + Random.Range(minWingMicroTwitchInterval, maxWingMicroTwitchInterval);
    }

    private void CacheWingFlutterLayer()
    {
        wingFlutterLayerIndex = animator != null && animator.runtimeAnimatorController != null
            ? animator.GetLayerIndex(WingFlutterLayerName)
            : -1;
    }

    private void SetWingFlutterWeight(float weight)
    {
        if (animator != null && wingFlutterLayerIndex >= 0)
        {
            animator.SetLayerWeight(wingFlutterLayerIndex, weight);
        }
    }

    private void UpdateTurnLean()
    {
        Vector3 currentForward = GetPlanarForward();
        float targetLean = 0f;

        if (Time.deltaTime > 0f && previousPlanarForward.sqrMagnitude > 0.0001f)
        {
            float signedTurnDegrees = Vector3.SignedAngle(previousPlanarForward, currentForward, Vector3.up);
            float signedTurnRate = signedTurnDegrees / Time.deltaTime;
            float turnDirection = Mathf.Sign(signedTurnRate);

            if (agent != null && agent.enabled && agent.isOnNavMesh && agent.desiredVelocity.sqrMagnitude > 0.0025f)
            {
                Vector3 desiredDirection = Vector3.ProjectOnPlane(agent.desiredVelocity, Vector3.up).normalized;
                float steeringAngle = Vector3.SignedAngle(currentForward, desiredDirection, Vector3.up);
                if (Mathf.Abs(steeringAngle) > 0.5f)
                {
                    // The steering direction is more stable than a one-frame yaw
                    // delta when avoidance makes several rapid path corrections.
                    turnDirection = Mathf.Sign(steeringAngle);
                }
            }

            float normalizedTurnRate = Mathf.Clamp01(Mathf.Abs(signedTurnRate) / fullLeanTurnRate);
            targetLean = normalizedTurnRate * turnDirection * leanStrength;
        }

        if (Mathf.Abs(targetLean) > 0.001f && turnLean * targetLean < 0f)
        {
            // Do not let smoothing momentum display the previous side's lean
            // after the chicken has already begun turning the other way.
            turnLean = 0f;
            turnLeanVelocity = 0f;
        }

        turnLean = Mathf.SmoothDamp(
            turnLean,
            targetLean,
            ref turnLeanVelocity,
            leanSmoothTime,
            Mathf.Infinity,
            Time.deltaTime);
        previousPlanarForward = currentForward;

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetFloat(TurnLeanParameter, turnLean);
        }
    }

    private Vector3 GetPlanarForward()
    {
        Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        return planarForward.sqrMagnitude > 0.0001f ? planarForward.normalized : Vector3.forward;
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
        minBlinkInterval = Mathf.Max(0.01f, minBlinkInterval);
        maxBlinkInterval = Mathf.Max(minBlinkInterval, maxBlinkInterval);
        blinkSpeedVariation = Mathf.Clamp(blinkSpeedVariation, 0f, 0.5f);
        minWingFlutterInterval = Mathf.Max(0.01f, minWingFlutterInterval);
        maxWingFlutterInterval = Mathf.Max(minWingFlutterInterval, maxWingFlutterInterval);
        minWingFlutterStrength = Mathf.Clamp01(minWingFlutterStrength);
        maxWingFlutterStrength = Mathf.Clamp(maxWingFlutterStrength, minWingFlutterStrength, 1f);
        minWingFlutterDuration = Mathf.Max(0.01f, minWingFlutterDuration);
        maxWingFlutterDuration = Mathf.Max(minWingFlutterDuration, maxWingFlutterDuration);
        minWingFlutterPulseInterval = Mathf.Max(0.01f, minWingFlutterPulseInterval);
        maxWingFlutterPulseInterval = Mathf.Max(
            minWingFlutterPulseInterval,
            maxWingFlutterPulseInterval);
        minWingMicroTwitchInterval = Mathf.Max(0.01f, minWingMicroTwitchInterval);
        maxWingMicroTwitchInterval = Mathf.Max(
            minWingMicroTwitchInterval,
            maxWingMicroTwitchInterval);
        minWingMicroTwitchStrength = Mathf.Clamp01(minWingMicroTwitchStrength);
        maxWingMicroTwitchStrength = Mathf.Clamp(
            maxWingMicroTwitchStrength,
            minWingMicroTwitchStrength,
            1f);
        minWingMicroTwitchDuration = Mathf.Max(0.01f, minWingMicroTwitchDuration);
        maxWingMicroTwitchDuration = Mathf.Max(
            minWingMicroTwitchDuration,
            maxWingMicroTwitchDuration);
        fullLeanTurnRate = Mathf.Max(1f, fullLeanTurnRate);
        leanSmoothTime = Mathf.Max(0.01f, leanSmoothTime);
        leanStrength = Mathf.Clamp01(leanStrength);
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
        maximumEggPushSpeed = Mathf.Max(0.01f, maximumEggPushSpeed);
        CapsuleCollider collider = GetComponent<CapsuleCollider>();
        if (collider != null)
        {
            collider.isTrigger = true;
        }
        minEggLayTime = Mathf.Max(0f, minEggLayTime);
        maxEggLayTime = Mathf.Max(minEggLayTime, maxEggLayTime);
        emptyFoodEggIntervalMultiplier = Mathf.Max(0.01f, emptyFoodEggIntervalMultiplier);
        fullFoodEggIntervalMultiplier = Mathf.Max(0.01f, fullFoodEggIntervalMultiplier);
        eggLayingDuration = Mathf.Max(0f, eggLayingDuration);
        eggLaunchSpeed = Mathf.Max(0f, eggLaunchSpeed);
        eggLaunchSpeedVariation = Mathf.Clamp01(eggLaunchSpeedVariation);
        eggLaunchSpin = Mathf.Max(0f, eggLaunchSpin);
        eggSpawnHeight = Mathf.Max(0f, eggSpawnHeight);
        eggSpawnBehindDistance = Mathf.Max(0f, eggSpawnBehindDistance);
    }
}
