using System.Collections.Generic;
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

    [SerializeField] private Transform[] visibleEggSlots = null;
    [SerializeField, Min(0.01f)] private float pickupDistance = 0.24f;
    [SerializeField, Min(0.01f)] private float deliveryDistance = 0.3f;
    [SerializeField, Min(0.05f)] private float targetRefreshInterval = 0.25f;
    [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 1.5f;
    [SerializeField, Min(0.05f)] private float targetNavMeshTolerance = 0.28f;

    private NavMeshAgent agent;
    private EggContainer eggContainer;
    private IncubatorController incubator;
    private ChickenEgg targetEgg;
    private int capacity = 3;
    private int storedEggs;
    private int smartnessLevel;
    private int targetPenIndex = -1;
    private readonly List<int> storedEggValues = new List<int>();
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
    private readonly RaycastHit[] obstructionHits = new RaycastHit[8];
    private int obstructionMask;
    private Vector3 lastDestination;
    private float nextDestinationRefreshTime;
    private bool hasDestination;

    public int StoredEggs => storedEggs;
    public int Capacity => capacity;
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
        ActiveRobots.Remove(this);
    }

    public void Configure(
        EggContainer targetContainer,
        IncubatorController targetIncubator,
        float movementSpeed,
        int eggCapacity,
        int deliverySmartnessLevel)
    {
        eggContainer = targetContainer;
        incubator = targetIncubator;
        targetPenIndex = PenExpansionManager.Instance != null
            ? PenExpansionManager.Instance.GetPenIndex(targetContainer)
            : -1;
        capacity = Mathf.Max(1, eggCapacity);
        smartnessLevel = Mathf.Clamp(deliverySmartnessLevel, 0, 3);
        agent.speed = Mathf.Max(0.1f, movementSpeed);
        agent.acceleration = agent.speed * 5f;
        agent.angularSpeed = 540f;
        TryPlaceOnNavMesh();
        RefreshVisibleEggs();
    }

    private void Update()
    {
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsRoundInProgress)
        {
            StopMoving();
            return;
        }

        if (!agent.isOnNavMesh && !TryPlaceOnNavMesh())
        {
            return;
        }

        if (delivering)
        {
            UpdateDelivery();
            return;
        }

        if (storedEggs >= capacity
            || (storedEggs > 0
                && Time.time - collectionTripStartTime
                    >= MaximumCollectionTripDuration))
        {
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

            if (storedEggs > 0 && noTargetTime >= 0.35f)
            {
                BeginDelivery();
            }

            return;
        }

        noTargetTime = 0f;
        SetDestination(targetEgg.transform.position);

        if (PlanarDistance(transform.position, targetEgg.transform.position)
            <= pickupDistance)
        {
            CollectTargetEgg();
        }
    }

    public void FinalizeRound()
    {
        targetEgg = null;
        StopMoving();
        storedEggs = 0;
        collectionTripStartTime = 0f;
        storedEggValues.Clear();
        storedEggTypes.Clear();
        delivering = false;
        deliveringToIncubator = false;
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

        if (storedEggs <= 0)
        {
            collectionTripStartTime = Time.time;
        }

        storedEggs++;
        storedEggValues.Add(egg.ValueCents);
        storedEggTypes.Add(egg.Type);
        egg.ReleaseToPool();
        RefreshVisibleEggs();

        if (storedEggs >= capacity)
        {
            BeginDelivery();
        }
    }

    private void BeginDelivery()
    {
        if (storedEggs <= 0 || eggContainer == null)
        {
            return;
        }

        delivering = true;
        targetEgg = null;
        deliveringToIncubator = CanDeliverToIncubator();
        Vector3 target = deliveringToIncubator
            ? incubator.DepositPosition
            : eggContainer.DepositPosition;
        SetDestination(target);
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
            int deposited = eggContainer.DepositEggValues(storedEggValues);
            storedEggs -= deposited;
            storedEggValues.RemoveRange(
                0,
                Mathf.Min(deposited, storedEggValues.Count));
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
            storedEggTypes.RemoveAt(leastValuableIndex);
        }
    }

    private bool CanDeliverToIncubator()
    {
        return smartnessLevel > 0
            && (RoundSystem.Instance == null
                || RoundSystem.Instance.IsCashQuotaMet)
            && incubator != null
            && incubator.isActiveAndEnabled
            && incubator.AvailableCapacity > 0
            && CountStoredStandardEggs() > 0;
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

        float destinationMoveThresholdSquared =
            DestinationMoveThreshold * DestinationMoveThreshold;
        bool destinationMoved = !hasDestination
            || (target - lastDestination).sqrMagnitude
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
                target,
                out NavMeshHit hit,
                navMeshSampleDistance,
                agent.areaMask))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
            lastDestination = target;
            hasDestination = true;
        }
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
    }
}
