using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class AutoFeederController : MonoBehaviour
{
    public const int MaximumLevel = 5;
    public const int MaximumAttractionRangeLevel = 5;
    private const float AttractionRadiusBonusPerLevel = 0.2f;
    private static readonly float[] DispenseIntervals =
        { 12f, 10f, 8f, 6f, 4f };

    [Header("Authored Parts")]
    [SerializeField] private GameObject foodPrefab;
    [SerializeField] private Transform[] foodSockets;
    [SerializeField] private Transform dialHand;

    [Header("Settings")]
    [SerializeField, Range(1, MaximumLevel)] private int speedLevel = 1;
    [SerializeField, Range(0, MaximumAttractionRangeLevel)]
    private int attractionRangeLevel;
    [SerializeField, Min(0.05f)] private float occupiedSocketRadius = 0.38f;

    private float timeUntilDispense;
    private Quaternion dialHandStartRotation;
    private bool initialized;
    private readonly List<int> freeSocketIndices = new List<int>(4);

    public int SpeedLevel => speedLevel;
    public int AttractionRangeLevel => attractionRangeLevel;
    public float DispenseInterval => GetDispenseInterval(speedLevel);
    public float TimeUntilDispense => timeUntilDispense;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnEnable()
    {
        EnsureInitialized();
        if (timeUntilDispense <= 0f)
        {
            timeUntilDispense = DispenseInterval;
        }

        RefreshDial();
    }

    private void Update()
    {
        if (RoundSystem.Instance != null
            && !RoundSystem.Instance.IsRoundInProgress)
        {
            RefreshDial();
            return;
        }

        BuildFreeSocketList();
        if (freeSocketIndices.Count <= 0)
        {
            // A full feeder is genuinely paused: neither its timer nor its
            // dial advances until a pile is consumed and a socket clears.
            RefreshDial();
            return;
        }

        timeUntilDispense -= Time.deltaTime;
        if (timeUntilDispense <= 0f)
        {
            DispenseAtRandomFreeSocket();
        }

        RefreshDial();
    }

    public void InstallOrUpgrade(int nextSpeedLevel)
    {
        InstallOrUpgrade(nextSpeedLevel, attractionRangeLevel);
    }

    public void InstallOrUpgrade(
        int nextSpeedLevel,
        int nextAttractionRangeLevel)
    {
        EnsureInitialized();
        float previousInterval = DispenseInterval;
        float elapsedNormalized = previousInterval > 0.001f
            ? 1f - Mathf.Clamp01(timeUntilDispense / previousInterval)
            : 0f;

        speedLevel = Mathf.Clamp(nextSpeedLevel, 1, MaximumLevel);
        attractionRangeLevel = Mathf.Clamp(
            nextAttractionRangeLevel,
            0,
            MaximumAttractionRangeLevel);
        timeUntilDispense = DispenseInterval * (1f - elapsedNormalized);
        if (timeUntilDispense <= 0f)
        {
            timeUntilDispense = DispenseInterval;
        }

        gameObject.SetActive(true);
        RefreshDial();
    }

    public static float GetDispenseInterval(int level)
    {
        return DispenseIntervals[
            Mathf.Clamp(level, 1, MaximumLevel) - 1];
    }

    public static float GetAttractionRadiusBonus(int level)
    {
        return Mathf.Clamp(level, 0, MaximumAttractionRangeLevel)
            * AttractionRadiusBonusPerLevel;
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        EnsureNavigationObstacle();
        if (dialHand != null)
        {
            dialHandStartRotation = dialHand.localRotation;
        }

        if (timeUntilDispense <= 0f)
        {
            timeUntilDispense = DispenseInterval;
        }
    }

    private void EnsureNavigationObstacle()
    {
        Collider sourceCollider = GetComponent<Collider>();
        if (sourceCollider == null)
        {
            return;
        }

        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle == null)
        {
            obstacle = gameObject.AddComponent<NavMeshObstacle>();
        }

        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
        if (sourceCollider is CapsuleCollider capsule)
        {
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.center = capsule.center;
            obstacle.radius = capsule.radius;
            obstacle.height = capsule.height;
        }
        else if (sourceCollider is BoxCollider box)
        {
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = box.size;
        }
    }

    private void BuildFreeSocketList()
    {
        freeSocketIndices.Clear();
        if (foodSockets == null)
        {
            return;
        }

        for (int socketIndex = 0;
             socketIndex < foodSockets.Length;
             socketIndex++)
        {
            Transform socket = foodSockets[socketIndex];
            if (socket != null && !IsSocketOccupied(socket.position))
            {
                freeSocketIndices.Add(socketIndex);
            }
        }
    }

    private bool IsSocketOccupied(Vector3 socketPosition)
    {
        float radiusSquared = occupiedSocketRadius * occupiedSocketRadius;
        IReadOnlyList<FoodPile> piles = FoodPile.ActivePiles;
        for (int index = 0; index < piles.Count; index++)
        {
            FoodPile pile = piles[index];
            if (pile != null
                && pile.IsAvailable
                && (pile.transform.position - socketPosition).sqrMagnitude
                    <= radiusSquared)
            {
                return true;
            }
        }

        return false;
    }

    private void DispenseAtRandomFreeSocket()
    {
        if (foodPrefab == null || freeSocketIndices.Count <= 0)
        {
            timeUntilDispense = 0f;
            return;
        }

        int randomListIndex = Random.Range(0, freeSocketIndices.Count);
        Transform socket = foodSockets[freeSocketIndices[randomListIndex]];
        if (socket == null || IsSocketOccupied(socket.position))
        {
            timeUntilDispense = 0f;
            return;
        }

        GameObject food = Instantiate(
            foodPrefab,
            socket.position,
            Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        food.name = $"Auto-Feeder Food ({socket.name})";
        FoodPile pile = food.GetComponent<FoodPile>();
        FoodShopController foodShop = FoodShopController.Instance;
        if (pile != null && foodShop != null)
        {
            // Auto-feeders use the currently unlocked global feed tier. They
            // do not consume a purchased bag or depend on the bag inventory.
            pile.ConfigureFeed(
                foodShop.CurrentFeedAmount,
                foodShop.CurrentFeedSpeedMultiplier,
                foodShop.CurrentPremiumChanceMultiplier,
                GetAttractionRadiusBonus(attractionRangeLevel));
        }

        timeUntilDispense = DispenseInterval;
        RoundSystem.Instance?.PlayFoodPlaceSfx();
    }

    private void RefreshDial()
    {
        if (dialHand == null)
        {
            return;
        }

        float progress = DispenseInterval > 0.001f
            ? 1f - Mathf.Clamp01(timeUntilDispense / DispenseInterval)
            : 0f;
        dialHand.localRotation = dialHandStartRotation
            * Quaternion.AngleAxis(progress * 360f, Vector3.up);
    }

    private void OnValidate()
    {
        speedLevel = Mathf.Clamp(speedLevel, 1, MaximumLevel);
        attractionRangeLevel = Mathf.Clamp(
            attractionRangeLevel,
            0,
            MaximumAttractionRangeLevel);
        occupiedSocketRadius = Mathf.Max(0.05f, occupiedSocketRadius);
    }
}
