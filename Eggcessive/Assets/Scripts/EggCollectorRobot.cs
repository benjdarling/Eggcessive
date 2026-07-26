using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class EggCollectorRobot : MonoBehaviour
{
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
    private bool smartDelivery;
    private bool delivering;
    private bool deliveringToIncubator;
    private float nextTargetRefreshTime;
    private float noTargetTime;
    private NavMeshPath reachabilityPath;

    public int StoredEggs => storedEggs;
    public int Capacity => capacity;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        reachabilityPath = new NavMeshPath();
        RefreshVisibleEggs();
    }

    public void Configure(
        EggContainer targetContainer,
        IncubatorController targetIncubator,
        float movementSpeed,
        int eggCapacity,
        bool useSmartDelivery)
    {
        eggContainer = targetContainer;
        incubator = targetIncubator;
        capacity = Mathf.Max(1, eggCapacity);
        smartDelivery = useSmartDelivery;
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

        if (storedEggs >= capacity)
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

        storedEggs++;
        Destroy(egg.gameObject);
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
            int deposited = eggContainer.DepositEggs(storedEggs);
            storedEggs -= deposited;
        }

        delivering = storedEggs > 0;
        noTargetTime = 0f;
        RefreshVisibleEggs();
    }

    private void DepositSmartEggs()
    {
        if (!CanDeliverToIncubator())
        {
            return;
        }

        int accepted = incubator.TryAcceptStoredEggs(storedEggs);
        storedEggs -= accepted;
    }

    private bool CanDeliverToIncubator()
    {
        return smartDelivery
            && incubator != null
            && incubator.isActiveAndEnabled
            && incubator.AvailableCapacity > 0;
    }

    private ChickenEgg FindNearestAvailableEgg()
    {
        ChickenEgg nearest = null;
        float nearestDistance = float.PositiveInfinity;
        var eggs = ChickenEgg.ActiveInstances;

        for (int index = 0; index < eggs.Count; index++)
        {
            ChickenEgg egg = eggs[index];

            if (egg == null
                || egg.IsCollected
                || egg.IsHeld
                || !CanReachEgg(egg))
            {
                continue;
            }

            float distance = (egg.transform.position - transform.position).sqrMagnitude;

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = egg;
            }
        }

        return nearest;
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

        RaycastHit[] hits = Physics.RaycastAll(
            start,
            direction / distance,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)
                || hit.collider.GetComponentInParent<ChickenEgg>() == egg
                || hit.collider.GetComponentInParent<ChickenController>() != null)
            {
                continue;
            }

            return false;
        }

        return true;
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

        if (NavMesh.SamplePosition(
                target,
                out NavMeshHit hit,
                navMeshSampleDistance,
                agent.areaMask))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

    private void StopMoving()
    {
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
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
