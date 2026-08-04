using System.Collections;
using System.Collections.Generic;
using DitzelGames.FastIK;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class EggCollectorRobot : MonoBehaviour
{
    private static readonly List<EggCollectorRobot> ActiveRobots =
        new List<EggCollectorRobot>();
    private const int MaximumReachabilityCandidates = 12;
    private const float DestinationRefreshInterval = 0.1f;
    private const float DestinationMoveThreshold = 0.05f;
    private const float MaximumCollectionTripDuration = 3.5f;
    private const float VacuumSuctionSpeed = 6f;
    private const float FinalDeliveryUnloadSeconds = 0.75f;
    private const float FinalDeliveryCongestionMultiplier = 1.35f;
    private const float CrowdProgressSampleInterval = 0.2f;
    private const float CrowdStallDuration = 0.65f;
    private const float CrowdDetourDuration = 0.85f;
    public const int ChickenArmsSmartnessLevel = 4;
    public const int MaximumVacuumLevel = 5;

    [SerializeField] private Transform[] visibleEggSlots = null;
    [SerializeField, Min(0.01f)] private float pickupDistance = 0.24f;
    [SerializeField, Min(0.01f)] private float deliveryDistance = 0.3f;
    [SerializeField, Min(0.05f)] private float targetRefreshInterval = 0.25f;
    [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 1.5f;
    [SerializeField, Min(0.05f)] private float targetNavMeshTolerance = 0.28f;

    [Header("Smart 4 Chicken Arms")]
    [SerializeField] private GameObject chickenArmRoot = null;
    [SerializeField] private FastIKFabric[] chickenArmSolvers = null;
    [SerializeField] private Transform[] chickenArmTargets = null;
    [SerializeField] private Transform[] chickenCarrySlots = null;
    [SerializeField, Min(0.1f)] private float chickenPickupDistance = 0.62f;
    [SerializeField, Range(1f, 45f)] private float chickenFacingTolerance = 15f;
    [SerializeField, Min(1f)] private float chickenTurnSpeed = 300f;
    [SerializeField, Min(0.1f)] private float chickenDeliveryDistance = 0.75f;

    private NavMeshAgent agent;
    private EggContainer eggContainer;
    private IncubatorController incubator;
    private CrosshatcherController crosshatcher;
    private ChickenEgg targetEgg;
    private int capacity = 3;
    private int storedEggs;
    private int smartnessLevel;
    private float vacuumRadius;
    private int targetPenIndex = -1;
    private readonly List<int> storedEggValues = new List<int>();
    private readonly List<float> storedEggWeights = new List<float>();
    private readonly List<ChickenEgg.EggType> storedEggTypes =
        new List<ChickenEgg.EggType>();
    private bool delivering;
    private bool deliveringToIncubator;
    private float nextTargetRefreshTime;
    private float noTargetTime;
    private float collectionTripStartTime;
    private NavMeshPath reachabilityPath;
    private readonly ChickenEgg[] nearestEggCandidates =
        new ChickenEgg[MaximumReachabilityCandidates];
    private readonly float[] nearestEggCandidateScores =
        new float[MaximumReachabilityCandidates];
    private readonly ChickenController[] nearestChickenCandidates =
        new ChickenController[MaximumReachabilityCandidates];
    private readonly float[] nearestChickenCandidateDistances =
        new float[MaximumReachabilityCandidates];
    private readonly RaycastHit[] obstructionHits = new RaycastHit[8];
    private int obstructionMask;
    private Vector3 lastDestination;
    private float nextDestinationRefreshTime;
    private bool hasDestination;
    private Vector3 crowdProgressPosition;
    private float nextCrowdProgressSampleTime;
    private float crowdStallStartTime = -1f;
    private Vector3 crowdDetourTarget;
    private float crowdDetourUntilTime;
    private bool finalDeliveryCommitted;
    private float nextFinalDeliveryAssessmentTime;
    private float cachedFinalDeliverySeconds;
    private readonly ChickenController[] carriedChickens =
        new ChickenController[2];
    private ChickenController targetChicken;
    private bool chickenMissionActive;
    private bool deliveringChickenPair;
    private int carriedChickenCount;
    private float nextChickenMissionCheckTime;
    private readonly HashSet<ChickenEgg> vacuumEggsInFlight =
        new HashSet<ChickenEgg>();

    public int StoredEggs => storedEggs;
    public int Capacity => capacity;
    public float VacuumRadius => vacuumRadius;
    public EggContainer TargetContainer => eggContainer;
    public static IReadOnlyList<EggCollectorRobot> ActiveInstances =>
        ActiveRobots;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveRobots.Clear();
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        reachabilityPath = new NavMeshPath();
        obstructionMask = Physics.DefaultRaycastLayers;
        int chickenLayer = LayerMask.NameToLayer("Chicken");
        int eggLayer = LayerMask.NameToLayer("Egg");
        if (chickenLayer >= 0)
        {
            obstructionMask &= ~(1 << chickenLayer);
        }
        if (eggLayer >= 0)
        {
            obstructionMask &= ~(1 << eggLayer);
        }
        RefreshVisibleEggs();
    }

    private void OnEnable()
    {
        if (!ActiveRobots.Contains(this))
        {
            ActiveRobots.Add(this);
        }
    }

    private void OnDisable()
    {
        CancelChickenMission(true);
        ReleaseVacuumEggs();
        ActiveRobots.Remove(this);
    }

    public void Configure(
        EggContainer targetContainer,
        IncubatorController targetIncubator,
        CrosshatcherController targetCrosshatcher,
        float movementSpeed,
        int eggCapacity,
        int deliverySmartnessLevel,
        int vacuumLevel)
    {
        eggContainer = targetContainer;
        incubator = targetIncubator;
        crosshatcher = targetCrosshatcher;
        targetPenIndex = PenExpansionManager.Instance != null
            ? PenExpansionManager.Instance.GetPenIndex(targetContainer)
            : -1;
        capacity = Mathf.Max(1, eggCapacity);
        smartnessLevel = Mathf.Clamp(
            deliverySmartnessLevel,
            0,
            ChickenArmsSmartnessLevel);
        vacuumRadius = GetVacuumRadius(vacuumLevel);
        agent.speed = Mathf.Max(0.1f, movementSpeed);
        agent.acceleration = agent.speed * 5f;
        agent.angularSpeed = 540f;
        agent.autoBraking = true;
        agent.autoRepath = true;
        agent.avoidancePriority = 10;
        agent.obstacleAvoidanceType =
            ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        TryPlaceOnNavMesh();
        crowdProgressPosition = transform.position;
        nextCrowdProgressSampleTime = Time.time + CrowdProgressSampleInterval;
        nextFinalDeliveryAssessmentTime = 0f;
        SetChickenArmsEnabled(
            smartnessLevel >= ChickenArmsSmartnessLevel);
        RefreshVisibleEggs();
    }

    private void Update()
    {
        UpdateChickenCarryPoses();

        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsRoundInProgress)
        {
            CancelChickenMission(true);
            StopMoving();
            return;
        }

        if (!agent.isOnNavMesh && !TryPlaceOnNavMesh())
        {
            return;
        }

        if (storedEggs > 0 && IsFinalDeliveryWindow())
        {
            finalDeliveryCommitted = true;
            if (!delivering || deliveringToIncubator)
            {
                BeginDelivery(true);
            }

            UpdateDelivery();
            return;
        }

        if (finalDeliveryCommitted && storedEggs <= 0)
        {
            StopMoving();
            return;
        }

        if (chickenMissionActive)
        {
            UpdateChickenMission();
            return;
        }

        if (delivering)
        {
            UpdateDelivery();
            return;
        }

        if (storedEggs <= 0 && TryBeginChickenMission())
        {
            return;
        }

        if (storedEggs >= capacity
            || (storedEggs > 0
                && vacuumEggsInFlight.Count == 0
                && Time.time - collectionTripStartTime
                    >= MaximumCollectionTripDuration))
        {
            if (vacuumEggsInFlight.Count > 0)
            {
                return;
            }

            BeginDelivery();
            return;
        }

        if (targetEgg == null
            || targetEgg.IsCollected
            || targetEgg.IsHeld
            || Time.time >= nextTargetRefreshTime)
        {
            targetEgg = FindNearestAvailableEgg();
            nextTargetRefreshTime = Time.time + targetRefreshInterval;
        }

        if (targetEgg == null)
        {
            noTargetTime += Time.deltaTime;

            if (storedEggs > 0
                && vacuumEggsInFlight.Count == 0
                && noTargetTime >= 0.35f)
            {
                BeginDelivery();
            }

            return;
        }

        noTargetTime = 0f;
        SetDestination(targetEgg.transform.position);

        float collectionDistance = vacuumRadius > 0f
            ? vacuumRadius
            : pickupDistance;
        if (PlanarDistance(transform.position, targetEgg.transform.position)
            <= collectionDistance)
        {
            if (vacuumRadius > 0f)
            {
                BeginVacuumCollection();
            }
            else
            {
                CollectTargetEgg();
            }
        }
    }

    public void FinalizeRound()
    {
        CancelChickenMission(true);
        StopAllCoroutines();
        ReleaseVacuumEggs();
        targetEgg = null;
        StopMoving();
        storedEggs = 0;
        collectionTripStartTime = 0f;
        storedEggValues.Clear();
        storedEggWeights.Clear();
        storedEggTypes.Clear();
        delivering = false;
        deliveringToIncubator = false;
        finalDeliveryCommitted = false;
        nextFinalDeliveryAssessmentTime = 0f;
        cachedFinalDeliverySeconds = 0f;
        crowdStallStartTime = -1f;
        crowdDetourUntilTime = 0f;
        RefreshVisibleEggs();
    }

    private void CollectTargetEgg()
    {
        ChickenEgg egg = targetEgg;
        targetEgg = null;

        if (egg == null || !egg.TryCollect())
        {
            return;
        }

        StoreCollectedEgg(egg);
        egg.ReleaseToPool();
        RefreshVisibleEggs();

        if (storedEggs >= capacity)
        {
            BeginDelivery();
        }
    }

    private void BeginVacuumCollection()
    {
        ChickenEgg egg = targetEgg;
        targetEgg = null;
        if (egg == null
            || storedEggs >= capacity
            || !egg.TryCollectFromTool())
        {
            return;
        }

        StoreCollectedEgg(egg);
        vacuumEggsInFlight.Add(egg);
        StartCoroutine(PullEggIntoRobot(egg));
        RefreshVisibleEggs();
    }

    private void StoreCollectedEgg(ChickenEgg egg)
    {
        if (storedEggs <= 0)
        {
            collectionTripStartTime = Time.time;
        }

        storedEggs++;
        storedEggValues.Add(egg.ValueCents);
        storedEggWeights.Add(egg.WeightKilograms);
        storedEggTypes.Add(egg.Type);
    }

    private IEnumerator PullEggIntoRobot(ChickenEgg egg)
    {
        while (egg != null && egg.gameObject.activeSelf)
        {
            Vector3 target = transform.position + Vector3.up * 0.22f;
            egg.transform.position = Vector3.MoveTowards(
                egg.transform.position,
                target,
                VacuumSuctionSpeed * Time.deltaTime);
            egg.transform.Rotate(Vector3.up, 720f * Time.deltaTime, Space.World);
            if ((egg.transform.position - target).sqrMagnitude <= 0.0025f)
            {
                break;
            }

            yield return null;
        }

        vacuumEggsInFlight.Remove(egg);
        if (egg != null && egg.gameObject.activeSelf)
        {
            egg.ReleaseToPool();
        }

        if (storedEggs >= capacity && vacuumEggsInFlight.Count == 0)
        {
            BeginDelivery();
        }
    }

    private void ReleaseVacuumEggs()
    {
        foreach (ChickenEgg egg in vacuumEggsInFlight)
        {
            if (egg != null && egg.gameObject.activeSelf)
            {
                egg.ReleaseToPool();
            }
        }

        vacuumEggsInFlight.Clear();
    }

    public static float GetVacuumRadius(int level)
    {
        int clampedLevel = Mathf.Clamp(level, 0, MaximumVacuumLevel);
        return clampedLevel > 0 ? 0.5f + clampedLevel * 0.5f : 0f;
    }

    private void BeginDelivery(bool forceContainer = false)
    {
        if (storedEggs <= 0 || eggContainer == null)
        {
            return;
        }

        delivering = true;
        targetEgg = null;
        deliveringToIncubator = !forceContainer
            && !finalDeliveryCommitted
            && CanDeliverToIncubator();
        Vector3 target = deliveringToIncubator
            ? incubator.DepositPosition
            : eggContainer.DepositPosition;
        SetDestination(target);
    }

    private bool IsFinalDeliveryWindow()
    {
        RoundSystem roundSystem = RoundSystem.Instance;
        if (roundSystem == null
            || eggContainer == null
            || !roundSystem.IsRoundInProgress)
        {
            return false;
        }

        if (Time.time >= nextFinalDeliveryAssessmentTime)
        {
            float pathLength = GetPathLengthTo(eggContainer.DepositPosition);
            float travelSeconds = pathLength / Mathf.Max(0.1f, agent.speed);
            cachedFinalDeliverySeconds = travelSeconds
                * FinalDeliveryCongestionMultiplier
                + FinalDeliveryUnloadSeconds;
            nextFinalDeliveryAssessmentTime = Time.time + 0.25f;
        }

        return roundSystem.TimeRemaining <= cachedFinalDeliverySeconds;
    }

    private float GetPathLengthTo(Vector3 target)
    {
        if (agent == null || !agent.isOnNavMesh)
        {
            return PlanarDistance(transform.position, target);
        }

        if (reachabilityPath == null)
        {
            reachabilityPath = new NavMeshPath();
        }

        if (!NavMesh.SamplePosition(
                target,
                out NavMeshHit hit,
                navMeshSampleDistance,
                agent.areaMask)
            || !agent.CalculatePath(hit.position, reachabilityPath)
            || reachabilityPath.status != NavMeshPathStatus.PathComplete)
        {
            return PlanarDistance(transform.position, target);
        }

        Vector3[] corners = reachabilityPath.corners;
        float length = 0f;
        Vector3 previous = transform.position;
        for (int index = 0; index < corners.Length; index++)
        {
            length += PlanarDistance(previous, corners[index]);
            previous = corners[index];
        }

        return Mathf.Max(length, PlanarDistance(transform.position, target));
    }

    private void UpdateDelivery()
    {
        Vector3 target = deliveringToIncubator && incubator != null
            ? incubator.DepositPosition
            : eggContainer != null
                ? eggContainer.DepositPosition
                : transform.position;
        SetDestination(target);

        bool reachedRawTarget =
            PlanarDistance(transform.position, target) <= deliveryDistance;
        bool reachedNavMeshTarget = !agent.pathPending
            && agent.hasPath
            && agent.remainingDistance
                <= Mathf.Max(deliveryDistance, agent.stoppingDistance + 0.05f);

        if (!reachedRawTarget && !reachedNavMeshTarget)
        {
            return;
        }

        if (deliveringToIncubator)
        {
            DepositSmartEggs();

            if (storedEggs > 0)
            {
                deliveringToIncubator = false;
                SetDestination(eggContainer.DepositPosition);
                return;
            }
        }
        else if (eggContainer != null)
        {
            int deposited = eggContainer.DepositEggValues(
                storedEggValues,
                storedEggWeights);
            storedEggs -= deposited;
            storedEggValues.RemoveRange(
                0,
                Mathf.Min(deposited, storedEggValues.Count));
            storedEggWeights.RemoveRange(
                0,
                Mathf.Min(deposited, storedEggWeights.Count));
            storedEggTypes.RemoveRange(
                0,
                Mathf.Min(deposited, storedEggTypes.Count));
        }

        delivering = storedEggs > 0;
        if (!delivering)
        {
            collectionTripStartTime = 0f;
        }
        noTargetTime = 0f;
        RefreshVisibleEggs();
    }

    private void DepositSmartEggs()
    {
        if (!CanDeliverToIncubator())
        {
            return;
        }

        int standardEggs = CountStoredStandardEggs();
        int accepted = incubator.TryAcceptStoredEggs(standardEggs);
        storedEggs -= accepted;

        // Rare eggs are always preserved for cash. Incubation consumes only
        // standard eggs, starting with the least valuable one.
        for (int i = 0; i < accepted && storedEggValues.Count > 0; i++)
        {
            int leastValuableIndex = -1;
            for (int candidate = 0; candidate < storedEggValues.Count; candidate++)
            {
                if (candidate >= storedEggTypes.Count
                    || storedEggTypes[candidate]
                        != ChickenEgg.EggType.Common)
                {
                    continue;
                }

                if (leastValuableIndex < 0
                    || storedEggValues[candidate]
                        < storedEggValues[leastValuableIndex])
                {
                    leastValuableIndex = candidate;
                }
            }

            if (leastValuableIndex < 0)
            {
                break;
            }

            storedEggValues.RemoveAt(leastValuableIndex);
            if (leastValuableIndex < storedEggWeights.Count)
            {
                storedEggWeights.RemoveAt(leastValuableIndex);
            }
            storedEggTypes.RemoveAt(leastValuableIndex);
        }
    }

    private bool CanDeliverToIncubator()
    {
        return smartnessLevel > 0
            && (RoundSystem.Instance == null
                || RoundSystem.Instance.IsCashQuotaMet
                || NeedsPopulationRecovery())
            && incubator != null
            && incubator.isActiveAndEnabled
            && incubator.AvailableCapacity > 0
            && CountStoredStandardEggs() > 0;
    }

    private bool NeedsPopulationRecovery()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        return manager != null
            && manager.IsInitialized
            && targetPenIndex >= 0
            && manager.GetChickenCount(targetPenIndex)
                < CrosshatcherController.MinimumFlockSizeForNewCycle;
    }

    private int CountStoredStandardEggs()
    {
        int count = 0;
        for (int index = 0; index < storedEggTypes.Count; index++)
        {
            if (storedEggTypes[index] == ChickenEgg.EggType.Common)
            {
                count++;
            }
        }

        return count;
    }

    private bool TryBeginChickenMission()
    {
        if (smartnessLevel < ChickenArmsSmartnessLevel
            || Time.time < nextChickenMissionCheckTime
            || crosshatcher == null
            || !crosshatcher.isActiveAndEnabled
            || NeedsPopulationRecovery())
        {
            return false;
        }

        nextChickenMissionCheckTime = Time.time + targetRefreshInterval;
        if (!TryFindChickenPair(out ChickenController first, out _)
            || !crosshatcher.TryReserveChickenPair(this))
        {
            return false;
        }

        chickenMissionActive = true;
        deliveringChickenPair = false;
        targetEgg = null;
        targetChicken = first;
        carriedChickenCount = 0;
        return true;
    }

    private void UpdateChickenMission()
    {
        if (crosshatcher == null || !crosshatcher.isActiveAndEnabled)
        {
            CancelChickenMission(true);
            return;
        }

        if (deliveringChickenPair)
        {
            UpdateChickenPairDelivery();
            return;
        }

        if (!crosshatcher.HasChickenReservation(this))
        {
            CancelChickenMission(true);
            return;
        }

        if (!IsAvailableChickenTarget(targetChicken))
        {
            targetChicken = FindNearestAvailableChicken();
            if (targetChicken == null)
            {
                CancelChickenMission(true);
                return;
            }
        }

        Vector3 targetPosition = targetChicken.transform.position;
        SetDestination(targetPosition);
        if (PlanarDistance(transform.position, targetPosition)
            > chickenPickupDistance)
        {
            return;
        }

        StopMoving();
        Vector3 facing = Vector3.ProjectOnPlane(
            targetPosition - transform.position,
            Vector3.up);
        if (facing.sqrMagnitude <= 0.0001f)
        {
            GrabTargetChicken();
            return;
        }

        Quaternion desiredRotation = Quaternion.LookRotation(
            facing.normalized,
            Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            desiredRotation,
            chickenTurnSpeed * Time.deltaTime);
        float facingAngle = Vector3.Angle(transform.forward, facing);
        if (facingAngle <= chickenFacingTolerance)
        {
            GrabTargetChicken();
        }
    }

    private void GrabTargetChicken()
    {
        ChickenController chicken = targetChicken;
        targetChicken = null;
        if (!IsAvailableChickenTarget(chicken)
            || carriedChickenCount >= carriedChickens.Length
            || !crosshatcher.HasChickenReservation(this))
        {
            return;
        }

        int slotIndex = carriedChickenCount;
        chicken.SetMachineControlled(true);
        chicken.SetHeldByHand(true);
        carriedChickens[slotIndex] = chicken;
        carriedChickenCount++;
        chicken.UpdateHeldCarryPose(
            GetChickenCarryPosition(slotIndex),
            0f);

        if (carriedChickenCount >= carriedChickens.Length)
        {
            deliveringChickenPair = true;
            SetDestination(crosshatcher.RobotDeliveryPosition);
            return;
        }

        targetChicken = FindNearestAvailableChicken();
        if (targetChicken == null)
        {
            CancelChickenMission(true);
        }
    }

    private void UpdateChickenPairDelivery()
    {
        Vector3 target = crosshatcher.RobotDeliveryPosition;
        SetDestination(target);
        bool reachedRawTarget =
            PlanarDistance(transform.position, target)
            <= chickenDeliveryDistance;
        bool reachedNavMeshTarget = !agent.pathPending
            && agent.hasPath
            && agent.remainingDistance
                <= Mathf.Max(
                    chickenDeliveryDistance,
                    agent.stoppingDistance + 0.05f);
        if (!reachedRawTarget && !reachedNavMeshTarget)
        {
            return;
        }

        StopMoving();
        for (int index = 0; index < carriedChickens.Length; index++)
        {
            ChickenController chicken = carriedChickens[index];
            if (chicken != null
                && crosshatcher.TryAcceptReservedChicken(chicken, this))
            {
                carriedChickens[index] = null;
                carriedChickenCount--;
            }
        }

        if (carriedChickenCount > 0)
        {
            if (!crosshatcher.HasChickenReservation(this))
            {
                CancelChickenMission(true);
            }
            return;
        }

        targetChicken = null;
        chickenMissionActive = false;
        deliveringChickenPair = false;
        noTargetTime = 0f;
        ResetArmTargets();
    }

    private void CancelChickenMission(bool releaseCarriedChickens)
    {
        if (crosshatcher != null)
        {
            crosshatcher.ReleaseChickenReservation(this);
        }

        if (releaseCarriedChickens)
        {
            for (int index = 0; index < carriedChickens.Length; index++)
            {
                ChickenController chicken = carriedChickens[index];
                if (chicken == null)
                {
                    continue;
                }

                Vector3 releasePosition = transform.TransformPoint(
                    index == 0
                        ? new Vector3(-0.55f, 0.05f, 0.15f)
                        : new Vector3(0.55f, 0.05f, 0.15f));
                chicken.SetHeldByHand(false);
                chicken.AlignHeldBoneTo(releasePosition);
                chicken.SetMachineControlled(false);
                carriedChickens[index] = null;
            }
        }

        carriedChickenCount = 0;
        targetChicken = null;
        chickenMissionActive = false;
        deliveringChickenPair = false;
        ResetArmTargets();
    }

    private void UpdateChickenCarryPoses()
    {
        for (int index = 0; index < carriedChickens.Length; index++)
        {
            ChickenController chicken = carriedChickens[index];
            if (chicken != null)
            {
                chicken.SetHeldCarryRotation(transform.rotation);
                chicken.UpdateHeldCarryPose(
                    GetChickenCarryPosition(index),
                    Time.deltaTime);
            }
        }

        for (int index = 0;
             chickenArmTargets != null && index < chickenArmTargets.Length;
             index++)
        {
            Transform armTarget = chickenArmTargets[index];
            if (armTarget == null)
            {
                continue;
            }

            if (!deliveringChickenPair
                && targetChicken != null
                && index == carriedChickenCount)
            {
                armTarget.position = targetChicken.transform.position
                    + Vector3.up * 0.22f;
            }
            else
            {
                armTarget.position = GetChickenCarryPosition(index);
            }
        }
    }

    private Vector3 GetChickenCarryPosition(int index)
    {
        if (chickenCarrySlots != null
            && index >= 0
            && index < chickenCarrySlots.Length
            && chickenCarrySlots[index] != null)
        {
            return chickenCarrySlots[index].position;
        }

        return transform.TransformPoint(
            index == 0
                ? new Vector3(-0.38f, 0.55f, 0.35f)
                : new Vector3(0.38f, 0.55f, 0.35f));
    }

    private void ResetArmTargets()
    {
        for (int index = 0;
             chickenArmTargets != null && index < chickenArmTargets.Length;
             index++)
        {
            if (chickenArmTargets[index] != null)
            {
                chickenArmTargets[index].position =
                    GetChickenCarryPosition(index);
            }
        }
    }

    private void SetChickenArmsEnabled(bool enabled)
    {
        if (chickenArmRoot != null)
        {
            chickenArmRoot.SetActive(enabled);
        }

        if (chickenArmSolvers == null)
        {
            return;
        }

        for (int index = 0; index < chickenArmSolvers.Length; index++)
        {
            if (chickenArmSolvers[index] != null)
            {
                chickenArmSolvers[index].enabled = enabled;
            }
        }
    }

    private bool TryFindChickenPair(
        out ChickenController first,
        out ChickenController second)
    {
        first = null;
        second = null;
        int candidateCount = BuildNearestChickenCandidates();
        for (int index = 0; index < candidateCount; index++)
        {
            ChickenController candidate = nearestChickenCandidates[index];
            nearestChickenCandidates[index] = null;
            if (!CanReachChicken(candidate))
            {
                continue;
            }

            if (first == null)
            {
                first = candidate;
            }
            else
            {
                second = candidate;
                break;
            }
        }

        return first != null && second != null;
    }

    private ChickenController FindNearestAvailableChicken()
    {
        int candidateCount = BuildNearestChickenCandidates();
        for (int index = 0; index < candidateCount; index++)
        {
            ChickenController candidate = nearestChickenCandidates[index];
            nearestChickenCandidates[index] = null;
            if (CanReachChicken(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private int BuildNearestChickenCandidates()
    {
        int candidateCount = 0;
        IReadOnlyList<ChickenController> chickens =
            ChickenController.ActiveInstances;
        for (int index = 0; index < chickens.Count; index++)
        {
            ChickenController chicken = chickens[index];
            if (!IsAvailableChickenTarget(chicken))
            {
                continue;
            }

            float distance =
                (chicken.transform.position - transform.position).sqrMagnitude;
            int insertionIndex = candidateCount;
            while (insertionIndex > 0
                && distance
                    < nearestChickenCandidateDistances[insertionIndex - 1])
            {
                insertionIndex--;
            }

            if (insertionIndex >= MaximumReachabilityCandidates)
            {
                continue;
            }

            int newCount = Mathf.Min(
                candidateCount + 1,
                MaximumReachabilityCandidates);
            for (int move = newCount - 1; move > insertionIndex; move--)
            {
                nearestChickenCandidates[move] =
                    nearestChickenCandidates[move - 1];
                nearestChickenCandidateDistances[move] =
                    nearestChickenCandidateDistances[move - 1];
            }

            nearestChickenCandidates[insertionIndex] = chicken;
            nearestChickenCandidateDistances[insertionIndex] = distance;
            candidateCount = newCount;
        }

        return candidateCount;
    }

    private bool IsAvailableChickenTarget(ChickenController chicken)
    {
        if (chicken == null || !chicken.CanBePickedUp)
        {
            return false;
        }

        for (int index = 0; index < carriedChickens.Length; index++)
        {
            if (carriedChickens[index] == chicken)
            {
                return false;
            }
        }

        return targetPenIndex < 0
            || PenExpansionManager.Instance == null
            || PenExpansionManager.Instance.GetClosestPenIndex(
                chicken.transform.position) == targetPenIndex;
    }

    private bool CanReachChicken(ChickenController chicken)
    {
        if (!IsAvailableChickenTarget(chicken)
            || !agent.isOnNavMesh
            || !NavMesh.SamplePosition(
                chicken.transform.position,
                out NavMeshHit targetHit,
                targetNavMeshTolerance,
                agent.areaMask))
        {
            return false;
        }

        if (reachabilityPath == null)
        {
            reachabilityPath = new NavMeshPath();
        }

        return agent.CalculatePath(targetHit.position, reachabilityPath)
            && reachabilityPath.status == NavMeshPathStatus.PathComplete;
    }

    private ChickenEgg FindNearestAvailableEgg()
    {
        int candidateCount = 0;
        var eggs = ChickenEgg.ActiveInstances;

        for (int index = 0; index < eggs.Count; index++)
        {
            ChickenEgg egg = eggs[index];

            if (egg == null
                || egg.IsCollected
                || egg.IsHeld
                || (targetPenIndex >= 0
                    && PenExpansionManager.Instance != null
                    && PenExpansionManager.Instance.GetClosestPenIndex(
                        egg.transform.position) != targetPenIndex))
            {
                continue;
            }

            float distance = (egg.transform.position - transform.position).sqrMagnitude;
            int insertionIndex = candidateCount;
            while (insertionIndex > 0
                && IsHigherPriorityEgg(
                    egg,
                    distance,
                    nearestEggCandidates[insertionIndex - 1],
                    nearestEggCandidateScores[insertionIndex - 1]))
            {
                insertionIndex--;
            }

            if (insertionIndex >= MaximumReachabilityCandidates)
            {
                continue;
            }

            int newCandidateCount = Mathf.Min(
                candidateCount + 1,
                MaximumReachabilityCandidates);
            for (int moveIndex = newCandidateCount - 1;
                 moveIndex > insertionIndex;
                 moveIndex--)
            {
                nearestEggCandidates[moveIndex] =
                    nearestEggCandidates[moveIndex - 1];
                nearestEggCandidateScores[moveIndex] =
                    nearestEggCandidateScores[moveIndex - 1];
            }

            nearestEggCandidates[insertionIndex] = egg;
            nearestEggCandidateScores[insertionIndex] = distance;
            candidateCount = newCandidateCount;
        }

        for (int index = 0; index < candidateCount; index++)
        {
            ChickenEgg candidate = nearestEggCandidates[index];
            nearestEggCandidates[index] = null;
            if (CanReachEgg(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool IsHigherPriorityEgg(
        ChickenEgg candidate,
        float candidateDistance,
        ChickenEgg existing,
        float existingDistance)
    {
        if (existing == null)
        {
            return true;
        }

        if (smartnessLevel >= 3)
        {
            if (candidate.Type != existing.Type)
            {
                return candidate.Type > existing.Type;
            }

            if (candidate.ValueCents != existing.ValueCents)
            {
                return candidate.ValueCents > existing.ValueCents;
            }

            return candidateDistance < existingDistance;
        }

        if (smartnessLevel >= 2)
        {
            float candidateScore = candidateDistance
                / Mathf.Max(1f, candidate.ValueCents / 100f);
            float existingScore = existingDistance
                / Mathf.Max(1f, existing.ValueCents / 100f);
            return candidateScore < existingScore;
        }

        return candidateDistance < existingDistance;
    }

    private bool CanReachEgg(ChickenEgg egg)
    {
        if (egg == null
            || !agent.isOnNavMesh
            || !NavMesh.SamplePosition(
                egg.transform.position,
                out NavMeshHit targetHit,
                targetNavMeshTolerance,
                agent.areaMask))
        {
            return false;
        }

        if (reachabilityPath == null)
        {
            reachabilityPath = new NavMeshPath();
        }

        if (!agent.CalculatePath(targetHit.position, reachabilityPath)
            || reachabilityPath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        Vector3 start = targetHit.position + Vector3.up * 0.08f;
        Vector3 end = egg.transform.position + Vector3.up * 0.08f;
        Vector3 direction = end - start;
        float distance = direction.magnitude;

        if (distance <= 0.01f)
        {
            return true;
        }

        int hitCount = Physics.RaycastNonAlloc(
            start,
            direction / distance,
            obstructionHits,
            distance,
            obstructionMask,
            QueryTriggerInteraction.Ignore);

        return hitCount == 0;
    }

    private bool TryPlaceOnNavMesh()
    {
        if (agent.isOnNavMesh)
        {
            return true;
        }

        if (!NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit hit,
                navMeshSampleDistance,
                agent.areaMask))
        {
            return false;
        }

        return agent.Warp(hit.position);
    }

    private void SetDestination(Vector3 target)
    {
        if (!agent.isOnNavMesh)
        {
            return;
        }

        UpdateCrowdDetour(target);
        Vector3 navigationTarget = Time.time < crowdDetourUntilTime
            ? crowdDetourTarget
            : target;
        float destinationMoveThresholdSquared =
            DestinationMoveThreshold * DestinationMoveThreshold;
        bool destinationMoved = !hasDestination
            || (navigationTarget - lastDestination).sqrMagnitude
                > destinationMoveThresholdSquared;
        if (!destinationMoved
            && (agent.hasPath || agent.pathPending))
        {
            return;
        }

        if (Time.time < nextDestinationRefreshTime)
        {
            return;
        }

        nextDestinationRefreshTime =
            Time.time + DestinationRefreshInterval;
        if (NavMesh.SamplePosition(
                navigationTarget,
                out NavMeshHit hit,
                navMeshSampleDistance,
                agent.areaMask))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
            lastDestination = navigationTarget;
            hasDestination = true;
        }
    }

    private void UpdateCrowdDetour(Vector3 finalTarget)
    {
        if (Time.time < nextCrowdProgressSampleTime)
        {
            return;
        }

        float moved = PlanarDistance(transform.position, crowdProgressPosition);
        bool tryingToTravel = agent.hasPath
            && PlanarDistance(transform.position, finalTarget) > 0.55f;
        if (tryingToTravel && moved < 0.035f)
        {
            if (crowdStallStartTime < 0f)
            {
                crowdStallStartTime = Time.time;
            }
            else if (Time.time - crowdStallStartTime >= CrowdStallDuration
                && TryChooseCrowdDetour(finalTarget, out Vector3 detour))
            {
                crowdDetourTarget = detour;
                crowdDetourUntilTime = Time.time + CrowdDetourDuration;
                crowdStallStartTime = -1f;
                hasDestination = false;
            }
        }
        else
        {
            crowdStallStartTime = -1f;
        }

        crowdProgressPosition = transform.position;
        nextCrowdProgressSampleTime = Time.time + CrowdProgressSampleInterval;
    }

    private bool TryChooseCrowdDetour(
        Vector3 finalTarget,
        out Vector3 detour)
    {
        detour = Vector3.zero;
        Vector3 forward = finalTarget - transform.position;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
        {
            return false;
        }

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float bestScore = float.MaxValue;
        bool found = false;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 requested = transform.position
                + forward * 0.35f
                + right * (side * 0.85f);
            if (!NavMesh.SamplePosition(
                    requested,
                    out NavMeshHit hit,
                    0.45f,
                    agent.areaMask))
            {
                continue;
            }

            if (reachabilityPath == null)
            {
                reachabilityPath = new NavMeshPath();
            }

            if (!agent.CalculatePath(hit.position, reachabilityPath)
                || reachabilityPath.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }

            float crowdPenalty = 0f;
            var chickens = ChickenController.ActiveInstances;
            for (int index = 0; index < chickens.Count; index++)
            {
                ChickenController chicken = chickens[index];
                if (chicken == null)
                {
                    continue;
                }

                float distance = PlanarDistance(
                    chicken.transform.position,
                    hit.position);
                if (distance < 0.8f)
                {
                    crowdPenalty += 1f - distance / 0.8f;
                }
            }

            float score = crowdPenalty * 4f
                + PlanarDistance(hit.position, finalTarget);
            if (score < bestScore)
            {
                bestScore = score;
                detour = hit.position;
                found = true;
            }
        }

        return found;
    }

    private void StopMoving()
    {
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
            hasDestination = false;
        }
    }

    private void RefreshVisibleEggs()
    {
        if (visibleEggSlots == null)
        {
            return;
        }

        for (int index = 0; index < visibleEggSlots.Length; index++)
        {
            if (visibleEggSlots[index] != null)
            {
                ChickenEgg.ApplyTypeVisual(
                    visibleEggSlots[index].gameObject,
                    index < storedEggTypes.Count
                        ? storedEggTypes[index]
                        : ChickenEgg.EggType.Common);
                visibleEggSlots[index].gameObject.SetActive(index < storedEggs);
            }
        }
    }

    private static float PlanarDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }

    private void OnValidate()
    {
        pickupDistance = Mathf.Max(0.01f, pickupDistance);
        deliveryDistance = Mathf.Max(0.01f, deliveryDistance);
        targetRefreshInterval = Mathf.Max(0.05f, targetRefreshInterval);
        navMeshSampleDistance = Mathf.Max(0.1f, navMeshSampleDistance);
        targetNavMeshTolerance = Mathf.Max(0.05f, targetNavMeshTolerance);
        chickenPickupDistance = Mathf.Max(0.1f, chickenPickupDistance);
        chickenFacingTolerance = Mathf.Clamp(
            chickenFacingTolerance,
            1f,
            45f);
        chickenTurnSpeed = Mathf.Max(1f, chickenTurnSpeed);
        chickenDeliveryDistance = Mathf.Max(0.1f, chickenDeliveryDistance);
    }
}
