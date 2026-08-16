using System.Collections;
using System.Collections.Generic;
using DitzelGames.FastIK;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[DefaultExecutionOrder(100)]
public sealed class EggCollectorRobot : MonoBehaviour
{
    private enum RobotDuty
    {
        None,
        GatherEggs,
        CashIn,
        Incubator,
        Crosshatcher
    }

    private static readonly List<EggCollectorRobot> ActiveRobots =
        new List<EggCollectorRobot>();
    private const int MaximumReachabilityCandidates = 12;
    private const float DestinationRefreshInterval = 0.1f;
    private const float DestinationMoveThreshold = 0.05f;
    private const float MaximumCollectionTripDuration = 3.5f;
    private const float VacuumSuctionSpeed = 6f;
    private const float VacuumLiftDistance = 0.32f;
    private const float FinalDeliveryUnloadSeconds = 0.75f;
    private const float FinalDeliveryCongestionMultiplier = 1.35f;
    private const float CrowdProgressSampleInterval = 0.2f;
    private const float CrowdStallDuration = 0.65f;
    private const float CrowdDetourDuration = 0.85f;
    private const float MaximumChickenMissionDuration = 12f;
    private const float ChickenMissionRetryDelay = 1f;
    private const string EggSocketPrefix = "SOCKET_EGG_";
    private const string EggStackPrefix = "robot_stack";
    private const int EggsPerStack = 9;
    private static readonly int BaseMapProperty = Shader.PropertyToID(
        "_BaseMap");
    private static readonly int MainTextureProperty = Shader.PropertyToID(
        "_MainTex");
    public const int IncubatorRoutingSmartnessLevel = 1;
    public const int PopulationGrowthSmartnessLevel = 2;
    public const int ChickenArmsSmartnessLevel = 4;
    public const int MaximumVacuumLevel = 5;

    [Header("Carried Egg Visuals")]
    [SerializeField] private GameObject carriedEggVisualPrefab = null;
    [SerializeField] private Vector3 carriedEggVisualScale = Vector3.one;
    [SerializeField, HideInInspector] private Transform[] visibleEggSlots = null;
    [SerializeField, Min(0.01f)] private float pickupDistance = 0.24f;
    [SerializeField, Min(0.01f)] private float deliveryDistance = 0.3f;
    [SerializeField, Min(0.05f)] private float targetRefreshInterval = 0.25f;
    [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 1.5f;
    [SerializeField, Min(0.05f)] private float targetNavMeshTolerance = 0.28f;

    [Header("Tank Locomotion")]
    [SerializeField, Range(1f, 90f)]
    private float turnInPlaceStartAngle = 10f;
    [SerializeField, Range(0f, 45f)]
    private float turnInPlaceFinishAngle = 2.5f;
    [SerializeField, Min(1f)] private float turnInPlaceSpeed = 360f;
    [SerializeField, Min(0f)] private float turnSpeedBonusPerTier = 0.25f;
    [SerializeField, Min(0f)] private float movingHeadingCorrectionSpeed = 35f;

    [Header("Robot Audio")]
    [SerializeField] private AudioClip robotMotorClip = null;
    [SerializeField] private AudioClip robotThinkClip = null;
    [SerializeField] private AudioClip robotDoneClip = null;
    [SerializeField, Range(0f, 1f)] private float motorMaximumVolume = 0.45f;
    [SerializeField, Range(0.1f, 3f)] private float motorMinimumPitch = 0.75f;
    [SerializeField, Range(0.1f, 3f)] private float motorMaximumPitch = 1.3f;
    [SerializeField, Min(0.1f)] private float motorAudioResponse = 5f;
    [SerializeField, Range(0f, 1f)] private float robotCueVolume = 0.75f;

    [Header("Robot Face")]
    [SerializeField] private Texture2D robotFaceIdleTexture = null;
    [SerializeField] private Texture2D robotFaceCarryEggsTexture = null;
    [SerializeField] private Texture2D robotFaceCashInTexture = null;
    [SerializeField] private Texture2D robotFaceCarryChickensTexture = null;

    [Header("Chicken Bumper")]
    [SerializeField] private bool pushChickens = true;
    [SerializeField, Min(0.05f)] private float chickenPushRadius = 0.15f;
    [SerializeField, Min(0f)] private float chickenPushForwardOffset = 0.05f;
    [SerializeField, Min(0f)] private float chickenPushSpeed = 4f;
    [SerializeField, Min(0f)] private float maximumChickenPushSpeed = 6f;

    [Header("Procedural Robot Motion")]
    [SerializeField] private bool animateVisuals = true;
    [SerializeField, Min(0.1f)] private float speedForFullLean = 4f;
    [SerializeField, Range(0f, 15f)] private float velocityLeanDegrees = 4.5f;
    [SerializeField, Range(0f, 3f)]
    private float accelerationLeanDegreesPerUnit = 0.4f;
    [SerializeField, Min(0f)] private float maximumVisualAcceleration = 18f;
    [SerializeField, Min(0f)] private float accelerationVisualResponse = 10f;
    [SerializeField, Min(1f)] private float turnRateForFullLean = 180f;
    [SerializeField, Range(0f, 10f)] private float turnLeanDegrees = 4f;

    [Header("Procedural Body")]
    [SerializeField, Min(0.01f)] private float bodyLeanFrequencyHz = 3.2f;
    [SerializeField, Range(0f, 2f)] private float bodyLeanDamping = 0.55f;
    [SerializeField, Range(0f, 20f)] private float maximumBodyLeanDegrees = 9f;
    [SerializeField, Range(0f, 2f)] private float bodyLeanMultiplier = 0.75f;

    [Header("Procedural Legs")]
    [SerializeField, Min(0.01f)] private float legYawFrequencyHz = 2.6f;
    [SerializeField, Range(0f, 2f)] private float legYawDamping = 0.7f;
    [SerializeField, Range(0f, 180f)] private float maximumLegYawOffset = 55f;
    [SerializeField, Min(0.01f)] private float legLeanFrequencyHz = 4f;
    [SerializeField, Range(0f, 2f)] private float legLeanDamping = 0.5f;
    [FormerlySerializedAs("maximumLegLeanDegrees")]
    [SerializeField, Range(0f, 20f)] private float maximumLegPitchDegrees = 12f;
    [SerializeField, Range(0f, 20f)] private float maximumLegRollDegrees = 2.5f;

    [Header("Procedural Wheels")]
    [SerializeField, Min(0.001f)] private float wheelRadius = 0.08f;
    [SerializeField] private float wheelSpinDirection = 1f;

    [Header("Procedural Egg Stacks")]
    [SerializeField, Min(0.01f)] private float stackLeanFrequencyHz = 2.3f;
    [SerializeField, Range(0f, 2f)] private float stackLeanDamping = 0.38f;
    [SerializeField, Range(0f, 15f)] private float maximumStackLeanDegrees = 7.5f;
    [SerializeField, Range(0f, 2f)] private float stackLeanMultiplier = 0.65f;
    [SerializeField, Range(0f, 0.75f)] private float stackFrequencyFalloff = 0.18f;

    [Header("Smart 4 Chicken Arms")]
    [SerializeField] private GameObject chickenArmRoot = null;
    [SerializeField] private RobotArmIK[] chickenArmSolvers = null;
    [SerializeField] private Transform[] chickenArmTargets = null;
    [SerializeField] private Transform[] chickenCarrySlots = null;
    [SerializeField] private Transform[] chickenGrabSockets = null;
    [SerializeField] private SkinnedMeshRenderer[] chickenHandRenderers = null;
    [SerializeField] private string chickenHandGrabBlendShapeName = "grab";
    [SerializeField, Range(0f, 1f)]
    private float chickenHandGrabAmount = 1f;
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
    private float configuredMovementSpeed = 1f;
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
    private float chickenMissionStartedTime;
    private bool debugChickenArmMissionPending;
    private readonly int[] chickenHandGrabBlendShapeIndices = { -1, -1 };
    private readonly HashSet<ChickenEgg> vacuumEggsInFlight =
        new HashSet<ChickenEgg>();
    private readonly List<GameObject> carriedEggVisuals =
        new List<GameObject>();
    private readonly List<Transform> eggStackRoots =
        new List<Transform>();
    private readonly List<EggStackAnimationState> eggStackAnimationStates =
        new List<EggStackAnimationState>();
    private readonly List<WheelAnimationState> wheelAnimationStates =
        new List<WheelAnimationState>();
    private bool usesEggStackSockets;
    private Transform robotBodyVisual;
    private Transform robotLegVisual;
    private Quaternion robotBodyRestRotation = Quaternion.identity;
    private Quaternion robotLegRestRotation = Quaternion.identity;
    private SpringUtils.Vector2Spring bodyLeanSpring;
    private SpringUtils.Vector2Spring legLeanSpring;
    private SpringUtils.AngleSpring legYawSpring;
    private Vector3 previousVisualVelocity;
    private Vector3 smoothedVisualAcceleration;
    private float previousRobotYaw;
    private bool hasVisualMotionSample;
    private bool tankTurningInPlace;
    private int robotVisualTier = 1;
    private AudioSource motorAudioSource;
    private AudioSource cueAudioSource;
    private readonly Queue<AudioClip> robotCueQueue = new Queue<AudioClip>();
    private RobotDuty currentRobotDuty;
    private float previousAudioYaw;
    private bool hasAudioMotionSample;
    private Renderer robotFaceRenderer;
    private int robotFaceMaterialIndex = -1;
    private MaterialPropertyBlock robotFaceProperties;
    private Texture currentRobotFaceTexture;
    private sealed class EggStackAnimationState
    {
        public readonly Transform Transform;
        public readonly Quaternion RestRotation;
        public SpringUtils.Vector2Spring LeanSpring;

        public EggStackAnimationState(Transform stack)
        {
            Transform = stack;
            RestRotation = stack != null
                ? stack.localRotation
                : Quaternion.identity;
            LeanSpring = new SpringUtils.Vector2Spring(Vector2.zero);
        }
    }

    private sealed class WheelAnimationState
    {
        public readonly Transform Transform;
        public readonly Quaternion RestRotation;
        public readonly float LocalSideOffset;
        public float SpinDegrees;

        public WheelAnimationState(
            Transform wheel,
            Transform robotRoot)
        {
            Transform = wheel;
            RestRotation = wheel != null
                ? wheel.localRotation
                : Quaternion.identity;
            LocalSideOffset = wheel != null && robotRoot != null
                ? robotRoot.InverseTransformPoint(wheel.position).x
                : 0f;
        }
    }

    public int StoredEggs => storedEggs;
    public int Capacity => capacity;
    public float VacuumRadius => vacuumRadius;
    public EggContainer TargetContainer => eggContainer;
    public static IReadOnlyList<EggCollectorRobot> ActiveInstances =>
        ActiveRobots;

    public void EnableSingleChickenArmDebugMission()
    {
        debugChickenArmMissionPending = true;
        nextChickenMissionCheckTime = 0f;
    }

    private void RefreshTurboMovementSpeed()
    {
        if (agent == null)
        {
            return;
        }

        float multiplier =
            TurboConsumableSystem.GetProductivityMultiplier(
                TurboConsumableSystem.TurboType.Robot);
        agent.speed = configuredMovementSpeed * multiplier;
        agent.acceleration = agent.speed * 5f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveRobots.Clear();
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;
        robotVisualTier = ResolveRobotVisualTier();
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
        ResolveChickenArmBindings();
        SetAllChickenHandsGrabbed(false);
        InitializeCarriedEggVisuals();
        InitializeProceduralAnimation();
        InitializeRobotAudio();
        InitializeRobotFace();
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
        ResetRobotAudio();
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
        configuredMovementSpeed = Mathf.Max(0.1f, movementSpeed);
        RefreshTurboMovementSpeed();
        agent.angularSpeed = 540f;
        agent.updateRotation = false;
        agent.autoBraking = true;
        agent.autoRepath = true;
        agent.avoidancePriority = 10;
        agent.obstacleAvoidanceType =
            ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        TryPlaceOnNavMesh();
        crowdProgressPosition = transform.position;
        nextCrowdProgressSampleTime = Time.time + CrowdProgressSampleInterval;
        nextFinalDeliveryAssessmentTime = 0f;
        // The arms are part of the visible T3 model even before chicken-carry
        // logic unlocks. Keep their IK active so both sides hold the authored
        // idle pose; TryBeginChickenMission remains the gameplay gate for
        // smartness level 4. Disabling these solvers exposed the asymmetric
        // bind pose, leaving the right arm pointing upright.
        SetChickenArmsEnabled(true);
        RefreshVisibleEggs();
    }

    private void Update()
    {
        ReconcileCarriedChickenState();
        UpdateChickenArmTargets();

        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsRoundInProgress)
        {
            CancelChickenMission(true);
            SetRobotDuty(RobotDuty.None, false);
            StopMoving();
            return;
        }

        RefreshTurboMovementSpeed();

        if (!agent.isOnNavMesh && !TryPlaceOnNavMesh())
        {
            return;
        }

        UpdateTankLocomotion(Time.deltaTime);
        UpdateChickenBumper();

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

        // Once the round quota is safe, chicken improvement outranks both an
        // active egg delivery and a partially filled vacuum load. Eggs can
        // remain on the stack while the arms service the crosshatcher, then
        // resume their previous delivery normally afterward.
        bool prioritizeChickenMission = RoundSystem.Instance != null
            && RoundSystem.Instance.IsCashQuotaMet;
        if (prioritizeChickenMission && TryBeginChickenMission())
        {
            return;
        }

        if (delivering)
        {
            UpdateDelivery();
            return;
        }

        if (!prioritizeChickenMission
            && storedEggs <= 0
            && TryBeginChickenMission())
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

        SetRobotDuty(RobotDuty.GatherEggs);
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

    private void LateUpdate()
    {
        UpdateProceduralAnimation();
        UpdateCarriedChickenPoses();
        UpdateRobotAudio(Time.deltaTime);
        UpdateRobotFace();
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
        SetRobotDuty(RobotDuty.None, false);
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
        float groundHeight = egg != null
            ? egg.transform.position.y
            : transform.position.y;
        while (egg != null && egg.gameObject.activeSelf)
        {
            Vector3 target = transform.position + Vector3.up * 0.22f;
            Vector3 planarEgg = egg.transform.position;
            Vector3 planarTarget = target;
            planarEgg.y = 0f;
            planarTarget.y = 0f;
            float planarDistance = Vector3.Distance(planarEgg, planarTarget);
            float liftProgress = Mathf.SmoothStep(
                0f,
                1f,
                1f - Mathf.Clamp01(planarDistance / VacuumLiftDistance));
            Vector3 pathTarget = target;
            pathTarget.y = Mathf.Lerp(
                groundHeight,
                target.y,
                liftProgress);
            egg.transform.position = Vector3.MoveTowards(
                egg.transform.position,
                pathTarget,
                VacuumSuctionSpeed
                    * TurboConsumableSystem.GetProductivityMultiplier(
                        TurboConsumableSystem.TurboType.Robot)
                    * Time.deltaTime);
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

        if (!chickenMissionActive
            && storedEggs >= capacity
            && vacuumEggsInFlight.Count == 0)
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
        return clampedLevel > 0 ? 0.4f + clampedLevel * 0.4f : 0f;
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
        SetRobotDuty(
            deliveringToIncubator
                ? RobotDuty.Incubator
                : RobotDuty.CashIn);
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
        // Incubator availability can change after a delivery begins. If it
        // fills, goes offline, or is disabled while the robot is travelling,
        // abandon that route immediately so carried eggs cannot become stuck.
        if (deliveringToIncubator && !CanDeliverToIncubator())
        {
            RerouteDeliveryToContainer();
        }

        Vector3 target = deliveringToIncubator && incubator != null
            ? incubator.DepositPosition
            : eggContainer != null
                ? eggContainer.DepositPosition
                : transform.position;
        SetDestination(target);

        // The incubator's raw deposit point can sit beyond the walkable edge,
        // so its nearest completed NavMesh endpoint also counts once a crowd
        // detour has cleared. Container delivery continues to use its physical
        // trigger volume.
        bool reachedIncubatorTarget = deliveringToIncubator
            && (PlanarDistance(transform.position, target) <= deliveryDistance
                || Time.time >= crowdDetourUntilTime
                    && !agent.pathPending
                    && agent.hasPath
                    && agent.remainingDistance
                        <= Mathf.Max(
                            deliveryDistance,
                            agent.stoppingDistance + 0.05f));
        bool reachedTarget = deliveringToIncubator
            ? reachedIncubatorTarget
            : eggContainer != null
                && eggContainer.IsWithinDepositRange(
                    transform.position,
                    deliveryDistance);
        if (!reachedTarget)
        {
            return;
        }

        if (deliveringToIncubator)
        {
            DepositSmartEggs();

            if (storedEggs > 0)
            {
                RerouteDeliveryToContainer();
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
            SetRobotDuty(RobotDuty.None);
        }
        noTargetTime = 0f;
        RefreshVisibleEggs();
    }

    private void RerouteDeliveryToContainer()
    {
        deliveringToIncubator = false;
        SetRobotDuty(RobotDuty.CashIn);

        // Discard any incubator path or temporary crowd detour so the new cash
        // destination is calculated immediately, even when both machines are
        // close enough to fall within the normal destination-change threshold.
        hasDestination = false;
        crowdStallStartTime = -1f;
        crowdDetourUntilTime = 0f;
        nextDestinationRefreshTime = 0f;

        if (eggContainer != null)
        {
            SetDestination(eggContainer.DepositPosition);
        }
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

                float candidateWeight = candidate < storedEggWeights.Count
                    ? storedEggWeights[candidate]
                    : ProgressionSystem.BaseEggWeightKilograms;
                float currentWeight = leastValuableIndex >= 0
                        && leastValuableIndex < storedEggWeights.Count
                    ? storedEggWeights[leastValuableIndex]
                    : ProgressionSystem.BaseEggWeightKilograms;
                if (leastValuableIndex < 0
                    || EggContainer.CalculateSaleValueCents(
                        storedEggValues[candidate],
                        candidateWeight)
                        < EggContainer.CalculateSaleValueCents(
                            storedEggValues[leastValuableIndex],
                            currentWeight))
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
        return smartnessLevel >= IncubatorRoutingSmartnessLevel
            && incubator != null
            && incubator.isActiveAndEnabled
            && incubator.AvailableCapacity > 0
            && CountStoredStandardEggs() > 0
            && (RoundSystem.Instance == null
                || RoundSystem.Instance.IsCashQuotaMet
                || NeedsPopulationRecovery()
                || (smartnessLevel >= PopulationGrowthSmartnessLevel
                    && NeedsPopulationGrowth()));
    }

    private bool NeedsPopulationRecovery()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        // Crosshatching consumes two chickens and produces one. Keep routing
        // common eggs into the incubator until the pen has both a protected
        // 80% flock and the two surplus parents needed for a new cycle.
        return manager != null
            && manager.IsInitialized
            && targetPenIndex >= 0
            && manager.GetChickenCount(targetPenIndex)
                < CrosshatcherController.MinimumFlockSizeForNewCycle;
    }

    private bool NeedsPopulationGrowth()
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        return manager != null
            && manager.IsInitialized
            && targetPenIndex >= 0
            && manager.GetChickenCount(targetPenIndex)
                < ChickenController.MaximumChickenCount;
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
        // Re-evaluate this immediately before reserving parents. A max-speed
        // crosshatcher can otherwise consume population faster than a maxed
        // incubator replaces it and permanently stall a fresh pen.
        if (smartnessLevel < ChickenArmsSmartnessLevel
            || Time.time < nextChickenMissionCheckTime
            || crosshatcher == null
            || !crosshatcher.isActiveAndEnabled
            || (NeedsPopulationRecovery()
                && !debugChickenArmMissionPending))
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
        chickenMissionStartedTime = Time.time;
        deliveringChickenPair = false;
        debugChickenArmMissionPending = false;
        targetEgg = null;
        targetChicken = first;
        carriedChickenCount = 0;
        SetRobotDuty(RobotDuty.Crosshatcher);
        return true;
    }

    private void UpdateChickenMission()
    {
        if (crosshatcher == null || !crosshatcher.isActiveAndEnabled)
        {
            CancelChickenMission(true);
            return;
        }

        if (Time.time - chickenMissionStartedTime
            >= MaximumChickenMissionDuration)
        {
            // Never let an unreachable moving chicken or pathing failure hold
            // the machine reservation forever. Manual automation can take over
            // during the retry window, and a lone robot will try again shortly.
            CancelChickenMission(true);
            nextChickenMissionCheckTime =
                Time.time + ChickenMissionRetryDelay;
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
            chickenTurnSpeed
                * TurboConsumableSystem.GetProductivityMultiplier(
                    TurboConsumableSystem.TurboType.Robot)
                * Time.deltaTime);
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
        SetChickenHandGrabbed(slotIndex, true);
        chicken.UpdateHeldCarryPose(
            GetChickenGrabPosition(slotIndex),
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
                SetChickenHandGrabbed(index, false);
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
        SetRobotDuty(RobotDuty.None);
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
                SetChickenHandGrabbed(index, false);
            }
        }

        carriedChickenCount = 0;
        targetChicken = null;
        chickenMissionActive = false;
        deliveringChickenPair = false;
        if (currentRobotDuty == RobotDuty.Crosshatcher)
        {
            SetRobotDuty(RobotDuty.None, false);
        }
        SetAllChickenHandsGrabbed(false);
        ResetArmTargets();
    }

    private void UpdateCarriedChickenPoses()
    {
        for (int index = 0; index < carriedChickens.Length; index++)
        {
            ChickenController chicken = carriedChickens[index];
            if (chicken != null)
            {
                chicken.SetHeldCarryRotation(transform.rotation);
                chicken.UpdateHeldCarryPose(
                    GetChickenGrabPosition(index),
                    Time.deltaTime);
            }
        }
    }

    private void ReconcileCarriedChickenState()
    {
        int actualCarriedCount = 0;
        for (int index = 0; index < carriedChickens.Length; index++)
        {
            ChickenController chicken = carriedChickens[index];
            bool hasChicken = chicken != null && chicken.IsHeldByHand;
            if (hasChicken)
            {
                actualCarriedCount++;
            }
            else
            {
                carriedChickens[index] = null;
            }

            // The hand grip is visual state separate from the chicken
            // reference, so keep it synchronized even when a chicken is
            // destroyed or removed outside the normal delivery callback.
            SetChickenHandGrabbed(index, hasChicken);
        }

        carriedChickenCount = actualCarriedCount;
        if (deliveringChickenPair && actualCarriedCount <= 0)
        {
            CancelChickenMission(true);
        }
    }

    private void UpdateChickenArmTargets()
    {
        for (int index = 0;
             chickenArmTargets != null && index < chickenArmTargets.Length;
             index++)
        {
            Transform armTarget = chickenArmTargets[index];
            RobotArmIK solver = chickenArmSolvers != null
                && index < chickenArmSolvers.Length
                ? chickenArmSolvers[index]
                : null;
            if (armTarget == null || solver == null)
            {
                continue;
            }

            if (chickenMissionActive
                && !deliveringChickenPair
                && targetChicken != null
                && index == carriedChickenCount
                && PlanarDistance(
                    transform.position,
                    targetChicken.transform.position)
                    <= chickenPickupDistance)
            {
                solver.ApplyReachPose(
                    targetChicken.transform.position + Vector3.up * 0.22f);
            }
            else if (index < carriedChickens.Length
                && carriedChickens[index] != null)
            {
                solver.ApplyCarryPose();
            }
            else
            {
                solver.ApplyIdlePose();
            }
        }
    }

    private Vector3 GetChickenGrabPosition(int index)
    {
        if (chickenGrabSockets != null
            && index >= 0
            && index < chickenGrabSockets.Length
            && chickenGrabSockets[index] != null)
        {
            return chickenGrabSockets[index].position;
        }

        return GetChickenCarryPosition(index);
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
                RobotArmIK solver = chickenArmSolvers != null
                    && index < chickenArmSolvers.Length
                    ? chickenArmSolvers[index]
                    : null;
                if (solver != null)
                {
                    solver.ApplyIdlePose();
                }
            }
        }
    }

    private void SetChickenArmsEnabled(bool enabled)
    {
        if (chickenArmRoot != null)
        {
            chickenArmRoot.SetActive(enabled);
        }

        for (int index = 0;
             chickenArmSolvers != null && index < chickenArmSolvers.Length;
             index++)
        {
            if (chickenArmSolvers[index] != null)
            {
                chickenArmSolvers[index].enabled = enabled;
            }
        }

        if (!enabled)
        {
            SetAllChickenHandsGrabbed(false);
        }
    }

    private void ResolveChickenArmBindings()
    {
        var sockets = new List<Transform>();
        Transform[] descendants = GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < descendants.Length; index++)
        {
            if (descendants[index].name == "SOCKET_GRAB")
            {
                sockets.Add(descendants[index]);
            }
        }

        sockets.Sort((first, second) =>
            transform.InverseTransformPoint(first.position).x.CompareTo(
                transform.InverseTransformPoint(second.position).x));
        if (sockets.Count >= carriedChickens.Length)
        {
            chickenGrabSockets = new Transform[carriedChickens.Length];
            for (int index = 0; index < chickenGrabSockets.Length; index++)
            {
                chickenGrabSockets[index] = sockets[index];
            }
        }

        ResolveChickenArmSolvers();

        if (chickenHandRenderers == null
            || chickenHandRenderers.Length != carriedChickens.Length)
        {
            chickenHandRenderers =
                new SkinnedMeshRenderer[carriedChickens.Length];
        }

        for (int index = 0; index < chickenHandGrabBlendShapeIndices.Length;
             index++)
        {
            chickenHandGrabBlendShapeIndices[index] = -1;
            Transform socket = chickenGrabSockets != null
                && index < chickenGrabSockets.Length
                ? chickenGrabSockets[index]
                : null;
            if (socket == null)
            {
                continue;
            }

            SkinnedMeshRenderer renderer = FindGrabBlendShapeRenderer(socket);
            chickenHandRenderers[index] = renderer;
            if (renderer != null)
            {
                chickenHandGrabBlendShapeIndices[index] =
                    FindBlendShapeIndex(renderer.sharedMesh);
            }
        }
    }

    private void ResolveChickenArmSolvers()
    {
        if (chickenGrabSockets == null
            || chickenGrabSockets.Length < carriedChickens.Length)
        {
            return;
        }

        FastIKFabric[] legacySolvers =
            GetComponentsInChildren<FastIKFabric>(true);
        for (int index = 0; index < legacySolvers.Length; index++)
        {
            legacySolvers[index].enabled = false;
        }

        RobotArmIK[] existingSolvers =
            GetComponentsInChildren<RobotArmIK>(true);
        for (int index = 0; index < existingSolvers.Length; index++)
        {
            existingSolvers[index].enabled = false;
        }

        var resolvedSolvers = new RobotArmIK[carriedChickens.Length];
        for (int index = 0; index < resolvedSolvers.Length; index++)
        {
            Transform socket = chickenGrabSockets[index];
            Transform target = chickenArmTargets != null
                && index < chickenArmTargets.Length
                ? chickenArmTargets[index]
                : null;
            if (socket == null || target == null)
            {
                continue;
            }

            Transform hand = FindNamedAncestor(socket, "robot_hand");
            Transform forearm = FindNamedAncestor(
                socket,
                "robot_arm_forearm");
            Transform upper = FindNamedAncestor(
                socket,
                "robot_arm_upper");
            Transform shoulder = upper != null
                ? FindNamedAncestor(upper.parent, "robot_arm")
                : null;
            if (upper == null || forearm == null || hand == null)
            {
                continue;
            }

            RobotArmIK solver = upper.GetComponent<RobotArmIK>();
            if (solver == null)
            {
                solver = upper.gameObject.AddComponent<RobotArmIK>();
            }

            solver.Configure(
                shoulder,
                upper,
                forearm,
                hand,
                socket,
                target,
                FindChickenArmPole(index, false),
                chickenCarrySlots != null
                    && index < chickenCarrySlots.Length
                    ? chickenCarrySlots[index]
                    : null,
                FindChickenArmPole(index, true));
            solver.CaptureRuntimeIdlePose();
            // Visible arms always need an active solver to maintain their idle
            // pose. Chicken mission eligibility is controlled separately.
            solver.enabled = true;
            resolvedSolvers[index] = solver;
        }

        chickenArmSolvers = resolvedSolvers;
    }

    private static Transform FindNamedAncestor(
        Transform descendant,
        string expectedName)
    {
        Transform current = descendant;
        while (current != null)
        {
            if (string.Equals(
                    current.name,
                    expectedName,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private Transform FindChickenArmPole(int index, bool carryPose)
    {
        string side = index == 0 ? "Left" : "Right";
        string expectedName = carryPose
            ? $"{side} Chicken Carry Pole"
            : $"{side} Arm IK Pole";
        Transform[] descendants = GetComponentsInChildren<Transform>(true);
        for (int descendantIndex = 0;
             descendantIndex < descendants.Length;
             descendantIndex++)
        {
            if (descendants[descendantIndex].name == expectedName)
            {
                return descendants[descendantIndex];
            }
        }

        return null;
    }

    private SkinnedMeshRenderer FindGrabBlendShapeRenderer(Transform socket)
    {
        Transform handRoot = socket.parent;
        if (handRoot == null)
        {
            return null;
        }

        SkinnedMeshRenderer[] renderers =
            handRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            if (FindBlendShapeIndex(renderers[index].sharedMesh) >= 0)
            {
                return renderers[index];
            }
        }

        return null;
    }

    private int FindBlendShapeIndex(Mesh mesh)
    {
        if (mesh == null || string.IsNullOrWhiteSpace(
                chickenHandGrabBlendShapeName))
        {
            return -1;
        }

        for (int index = 0; index < mesh.blendShapeCount; index++)
        {
            string shapeName = mesh.GetBlendShapeName(index);
            if (string.Equals(
                    shapeName,
                    chickenHandGrabBlendShapeName,
                    System.StringComparison.OrdinalIgnoreCase)
                || shapeName.EndsWith(
                    "." + chickenHandGrabBlendShapeName,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void SetAllChickenHandsGrabbed(bool grabbed)
    {
        for (int index = 0; index < carriedChickens.Length; index++)
        {
            SetChickenHandGrabbed(index, grabbed);
        }
    }

    private void SetChickenHandGrabbed(int index, bool grabbed)
    {
        if (chickenHandRenderers == null
            || index < 0
            || index >= chickenHandRenderers.Length
            || index >= chickenHandGrabBlendShapeIndices.Length)
        {
            return;
        }

        SkinnedMeshRenderer renderer = chickenHandRenderers[index];
        int shapeIndex = chickenHandGrabBlendShapeIndices[index];
        if (renderer == null || shapeIndex < 0)
        {
            return;
        }

        // Blender's normalized 0..1 shape-key value is represented as a
        // 0..100 blendshape weight by Unity.
        renderer.SetBlendShapeWeight(
            shapeIndex,
            grabbed ? chickenHandGrabAmount * 100f : 0f);
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
            agent.SetDestination(hit.position);
            lastDestination = navigationTarget;
            hasDestination = true;
            UpdateTankLocomotion(0f);
        }
    }

    private void UpdateTankLocomotion(float deltaTime)
    {
        if (agent == null
            || !agent.isOnNavMesh
            || !hasDestination
            || (!agent.hasPath && !agent.pathPending))
        {
            tankTurningInPlace = false;
            return;
        }

        Vector3 headingTarget = !agent.pathPending && agent.hasPath
            ? agent.steeringTarget
            : lastDestination;
        Vector3 heading = Vector3.ProjectOnPlane(
            headingTarget - transform.position,
            Vector3.up);
        if (heading.sqrMagnitude <= 0.0001f)
        {
            tankTurningInPlace = false;
            agent.isStopped = false;
            return;
        }

        float signedAngle = Vector3.SignedAngle(
            transform.forward,
            heading,
            Vector3.up);
        float absoluteAngle = Mathf.Abs(signedAngle);
        if (tankTurningInPlace)
        {
            if (absoluteAngle <= turnInPlaceFinishAngle)
            {
                tankTurningInPlace = false;
                agent.isStopped = false;
                return;
            }
        }
        else if (absoluteAngle >= turnInPlaceStartAngle)
        {
            tankTurningInPlace = true;
        }

        float turboMultiplier =
            TurboConsumableSystem.GetProductivityMultiplier(
                TurboConsumableSystem.TurboType.Robot);
        if (tankTurningInPlace)
        {
            agent.isStopped = true;
            float tierTurnMultiplier = 1f
                + Mathf.Max(0, robotVisualTier - 1)
                * turnSpeedBonusPerTier;
            RotateTowardsHeading(
                heading,
                turnInPlaceSpeed
                    * tierTurnMultiplier
                    * turboMultiplier
                    * deltaTime);
            return;
        }

        agent.isStopped = false;
        if (movingHeadingCorrectionSpeed > 0f)
        {
            RotateTowardsHeading(
                heading,
                movingHeadingCorrectionSpeed
                    * turboMultiplier
                    * deltaTime);
        }
    }

    private void RotateTowardsHeading(Vector3 heading, float maximumDegrees)
    {
        if (heading.sqrMagnitude <= 0.0001f || maximumDegrees <= 0f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(
            heading.normalized,
            Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            maximumDegrees);
    }

    private void UpdateChickenBumper()
    {
        if (!pushChickens
            || chickenPushSpeed <= 0f
            || agent == null
            || !agent.isOnNavMesh
            || tankTurningInPlace)
        {
            return;
        }

        Vector3 travelVelocity = Vector3.ProjectOnPlane(
            agent.velocity,
            Vector3.up);
        if (travelVelocity.sqrMagnitude <= 0.0025f)
        {
            return;
        }

        Vector3 travelDirection = travelVelocity.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, travelDirection);
        Vector3 bumperCenter = transform.position
            + travelDirection * chickenPushForwardOffset;
        float activePushRadius = chickenPushRadius;
        float radiusSquared = activePushRadius * activePushRadius;
        IReadOnlyList<ChickenController> chickens =
            ChickenController.ActiveInstances;
        for (int index = 0; index < chickens.Count; index++)
        {
            ChickenController chicken = chickens[index];
            if (chicken == null
                || chicken == targetChicken
                || chicken.IsMachineControlled)
            {
                continue;
            }

            Vector3 offset = chicken.transform.position - bumperCenter;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared >= radiusSquared)
            {
                continue;
            }

            float side = Vector3.Dot(offset, right);
            if (Mathf.Abs(side) < 0.01f)
            {
                side = (chicken.GetInstanceID() & 1) == 0 ? -1f : 1f;
            }

            Vector3 outward = distanceSquared > 0.000001f
                ? offset / Mathf.Sqrt(distanceSquared)
                : right * Mathf.Sign(side);
            Vector3 pushDirection = (
                right * Mathf.Sign(side) * 0.8f
                + outward * 0.45f
                + travelDirection * 0.35f).normalized;
            float proximity = 1f - Mathf.Sqrt(distanceSquared)
                / activePushRadius;
            float impactSpeed = chickenPushSpeed
                * Mathf.Lerp(0.55f, 1f, proximity)
                + travelVelocity.magnitude * 0.35f;
            chicken.ApplyRobotPush(
                pushDirection * impactSpeed,
                maximumChickenPushSpeed);
        }
    }

    private int ResolveRobotVisualTier()
    {
        const string tierMarker = "_T";
        int markerIndex = gameObject.name.LastIndexOf(
            tierMarker,
            System.StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || markerIndex + tierMarker.Length >= gameObject.name.Length)
        {
            return 1;
        }

        int tier = 0;
        for (int index = markerIndex + tierMarker.Length;
             index < gameObject.name.Length
                 && char.IsDigit(gameObject.name[index]);
             index++)
        {
            tier = tier * 10 + gameObject.name[index] - '0';
        }

        return Mathf.Max(1, tier);
    }

    private void UpdateCrowdDetour(Vector3 finalTarget)
    {
        if (Time.time < nextCrowdProgressSampleTime)
        {
            return;
        }

        float moved = PlanarDistance(transform.position, crowdProgressPosition);
        bool tryingToTravel = agent.hasPath
            && !tankTurningInPlace
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
            tankTurningInPlace = false;
        }
    }

    private void InitializeRobotAudio()
    {
        motorAudioSource = FindOrCreateRobotAudioSource(
            "Robot Motor Audio");
        cueAudioSource = FindOrCreateRobotAudioSource("Robot Cue Audio");

        ConfigureRobotAudioSource(motorAudioSource);
        motorAudioSource.clip = robotMotorClip;
        motorAudioSource.loop = true;
        motorAudioSource.volume = 0f;
        motorAudioSource.pitch = motorMinimumPitch;

        ConfigureRobotAudioSource(cueAudioSource);
        cueAudioSource.loop = false;
        cueAudioSource.volume = robotCueVolume;

        previousAudioYaw = transform.eulerAngles.y;
        hasAudioMotionSample = false;
    }

    private AudioSource FindOrCreateRobotAudioSource(string childName)
    {
        Transform child = transform.Find(childName);
        if (child == null)
        {
            var audioObject = new GameObject(childName);
            child = audioObject.transform;
            child.SetParent(transform, false);
        }

        AudioSource source = child.GetComponent<AudioSource>();
        return source != null
            ? source
            : child.gameObject.AddComponent<AudioSource>();
    }

    private static void ConfigureRobotAudioSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1.5f;
        source.maxDistance = 18f;
    }

    private void UpdateRobotAudio(float deltaTime)
    {
        UpdateMotorAudio(deltaTime);
        UpdateRobotCueAudio();
    }

    private void UpdateMotorAudio(float deltaTime)
    {
        if (motorAudioSource == null || robotMotorClip == null)
        {
            return;
        }

        float currentYaw = transform.eulerAngles.y;
        if (!hasAudioMotionSample || deltaTime <= 0f)
        {
            previousAudioYaw = currentYaw;
            hasAudioMotionSample = true;
            return;
        }

        Vector3 velocity = agent != null && agent.isOnNavMesh
            ? agent.velocity
            : Vector3.zero;
        float forwardSpeed = Mathf.Abs(Vector3.Dot(velocity, transform.forward));
        float yawRateRadians = Mathf.Abs(Mathf.DeltaAngle(
            previousAudioYaw,
            currentYaw) / deltaTime) * Mathf.Deg2Rad;
        previousAudioYaw = currentYaw;

        float trackRadius = 0.12f;
        for (int index = 0; index < wheelAnimationStates.Count; index++)
        {
            trackRadius = Mathf.Max(
                trackRadius,
                Mathf.Abs(wheelAnimationStates[index].LocalSideOffset));
        }

        float wheelMotionSpeed = forwardSpeed + yawRateRadians * trackRadius;
        float referenceSpeed = agent != null
            ? Mathf.Max(0.1f, agent.speed)
            : Mathf.Max(0.1f, configuredMovementSpeed);
        float speedAmount = Mathf.Clamp01(wheelMotionSpeed / referenceSpeed);
        float targetVolume = speedAmount > 0.005f
            ? Mathf.Lerp(motorMaximumVolume * 0.2f, motorMaximumVolume,
                speedAmount)
            : 0f;
        float targetPitch = Mathf.Lerp(
            motorMinimumPitch,
            motorMaximumPitch,
            speedAmount);
        float response = 1f - Mathf.Exp(
            -Mathf.Max(0.1f, motorAudioResponse) * deltaTime);

        motorAudioSource.volume = Mathf.Lerp(
            motorAudioSource.volume,
            targetVolume,
            response);
        motorAudioSource.pitch = Mathf.Lerp(
            motorAudioSource.pitch,
            targetPitch,
            response);

        if (targetVolume > 0f && !motorAudioSource.isPlaying)
        {
            motorAudioSource.Play();
        }
        else if (targetVolume <= 0f
            && motorAudioSource.volume <= 0.005f
            && motorAudioSource.isPlaying)
        {
            motorAudioSource.Stop();
            motorAudioSource.volume = 0f;
        }
    }

    private void SetRobotDuty(
        RobotDuty nextDuty,
        bool completedPrevious = true)
    {
        if (currentRobotDuty == nextDuty)
        {
            if (!completedPrevious && nextDuty == RobotDuty.None)
            {
                ClearRobotCueQueue();
            }

            return;
        }

        if (currentRobotDuty != RobotDuty.None && completedPrevious)
        {
            QueueRobotCue(robotDoneClip);
        }

        currentRobotDuty = nextDuty;
        if (nextDuty != RobotDuty.None)
        {
            QueueRobotCue(robotThinkClip);
        }
        else if (!completedPrevious)
        {
            ClearRobotCueQueue();
        }
    }

    private void QueueRobotCue(AudioClip clip)
    {
        if (clip != null)
        {
            robotCueQueue.Enqueue(clip);
        }
    }

    private void UpdateRobotCueAudio()
    {
        if (cueAudioSource == null
            || cueAudioSource.isPlaying
            || robotCueQueue.Count <= 0)
        {
            return;
        }

        cueAudioSource.clip = robotCueQueue.Dequeue();
        cueAudioSource.volume = robotCueVolume;
        cueAudioSource.pitch = 1f;
        cueAudioSource.Play();
    }

    private void ClearRobotCueQueue()
    {
        robotCueQueue.Clear();
        if (cueAudioSource != null)
        {
            cueAudioSource.Stop();
            cueAudioSource.clip = null;
        }
    }

    private void ResetRobotAudio()
    {
        currentRobotDuty = RobotDuty.None;
        ClearRobotCueQueue();
        if (motorAudioSource != null)
        {
            motorAudioSource.Stop();
            motorAudioSource.volume = 0f;
        }

        hasAudioMotionSample = false;
    }

    private void InitializeRobotFace()
    {
        robotFaceRenderer = null;
        robotFaceMaterialIndex = -1;
        robotFaceProperties = new MaterialPropertyBlock();
        currentRobotFaceTexture = null;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0;
             rendererIndex < renderers.Length;
             rendererIndex++)
        {
            Material[] materials = renderers[rendererIndex].sharedMaterials;
            for (int materialIndex = 0;
                 materialIndex < materials.Length;
                 materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null
                    || !material.name.StartsWith(
                        "mat_robot_face",
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                robotFaceRenderer = renderers[rendererIndex];
                robotFaceMaterialIndex = materialIndex;
                UpdateRobotFace(true);
                return;
            }
        }
    }

    private void UpdateRobotFace(bool force = false)
    {
        if (robotFaceRenderer == null
            || robotFaceMaterialIndex < 0
            || robotFaceProperties == null)
        {
            return;
        }

        Texture desiredTexture = GetDesiredRobotFaceTexture();
        if (desiredTexture == null
            || (!force && desiredTexture == currentRobotFaceTexture))
        {
            return;
        }

        robotFaceRenderer.GetPropertyBlock(
            robotFaceProperties,
            robotFaceMaterialIndex);
        robotFaceProperties.SetTexture(BaseMapProperty, desiredTexture);
        robotFaceProperties.SetTexture(MainTextureProperty, desiredTexture);
        robotFaceRenderer.SetPropertyBlock(
            robotFaceProperties,
            robotFaceMaterialIndex);
        currentRobotFaceTexture = desiredTexture;
    }

    private Texture GetDesiredRobotFaceTexture()
    {
        if (carriedChickenCount > 0
            && robotFaceCarryChickensTexture != null)
        {
            return robotFaceCarryChickensTexture;
        }

        if (currentRobotDuty == RobotDuty.CashIn
            && robotFaceCashInTexture != null)
        {
            return robotFaceCashInTexture;
        }

        if (storedEggs > 0 && robotFaceCarryEggsTexture != null)
        {
            return robotFaceCarryEggsTexture;
        }

        return robotFaceIdleTexture;
    }

    private void InitializeProceduralAnimation()
    {
        robotBodyVisual = FindVisualTransform("robot_body");
        robotLegVisual = FindVisualTransform("robot_legs");
        robotBodyRestRotation = robotBodyVisual != null
            ? robotBodyVisual.localRotation
            : Quaternion.identity;
        robotLegRestRotation = robotLegVisual != null
            ? robotLegVisual.localRotation
            : Quaternion.identity;

        eggStackAnimationStates.Clear();
        for (int index = 0; index < eggStackRoots.Count; index++)
        {
            if (eggStackRoots[index] != null)
            {
                eggStackAnimationStates.Add(
                    new EggStackAnimationState(eggStackRoots[index]));
            }
        }

        wheelAnimationStates.Clear();
        Transform[] descendants = GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < descendants.Length; index++)
        {
            Transform candidate = descendants[index];
            if (candidate != null
                && candidate.name.StartsWith(
                    "robot_wheel",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                wheelAnimationStates.Add(
                    new WheelAnimationState(candidate, transform));
            }
        }

        ResetProceduralAnimation();
    }

    private Transform FindVisualTransform(string namePrefix)
    {
        Transform[] descendants = GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < descendants.Length; index++)
        {
            Transform candidate = descendants[index];
            if (candidate != null
                && candidate.name.StartsWith(
                    namePrefix,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private void ResetProceduralAnimation()
    {
        bodyLeanSpring.Reset(Vector2.zero);
        legLeanSpring.Reset(Vector2.zero);
        previousVisualVelocity = Vector3.zero;
        smoothedVisualAcceleration = Vector3.zero;
        previousRobotYaw = transform.eulerAngles.y;
        legYawSpring.Reset(previousRobotYaw);
        hasVisualMotionSample = false;

        for (int index = 0;
             index < eggStackAnimationStates.Count;
             index++)
        {
            eggStackAnimationStates[index].LeanSpring.Reset(Vector2.zero);
        }

        for (int index = 0;
             index < wheelAnimationStates.Count;
             index++)
        {
            wheelAnimationStates[index].SpinDegrees = 0f;
        }

        ApplyProceduralVisualRotations();
    }

    private void UpdateProceduralAnimation()
    {
        if (!animateVisuals)
        {
            if (hasVisualMotionSample)
            {
                ResetProceduralAnimation();
            }

            return;
        }

        float deltaTime = Mathf.Min(Time.deltaTime, 1f / 20f);
        if (deltaTime <= 0f
            || (robotBodyVisual == null
                && robotLegVisual == null
                && eggStackAnimationStates.Count == 0))
        {
            return;
        }

        Vector3 worldVelocity = agent != null && agent.isOnNavMesh
            ? agent.velocity
            : Vector3.zero;
        float robotYaw = transform.eulerAngles.y;
        if (!hasVisualMotionSample)
        {
            previousVisualVelocity = worldVelocity;
            previousRobotYaw = robotYaw;
            legYawSpring.Reset(robotYaw);
            hasVisualMotionSample = true;
        }

        Vector3 worldAcceleration =
            (worldVelocity - previousVisualVelocity) / deltaTime;
        worldAcceleration = Vector3.ClampMagnitude(
            worldAcceleration,
            maximumVisualAcceleration);
        float accelerationResponse =
            1f - Mathf.Exp(-accelerationVisualResponse * deltaTime);
        smoothedVisualAcceleration = Vector3.Lerp(
            smoothedVisualAcceleration,
            worldAcceleration,
            accelerationResponse);

        Vector3 localVelocity = transform.InverseTransformDirection(
            worldVelocity);
        Vector3 localAcceleration = transform.InverseTransformDirection(
            smoothedVisualAcceleration);
        float targetPitch = -Mathf.Clamp(
                localVelocity.z / speedForFullLean,
                -1f,
                1f) * velocityLeanDegrees
            - localAcceleration.z * accelerationLeanDegreesPerUnit;
        float yawRate = Mathf.DeltaAngle(previousRobotYaw, robotYaw)
            / deltaTime;
        float targetRoll = -Mathf.Clamp(
                localVelocity.x / speedForFullLean,
                -1f,
                1f) * velocityLeanDegrees
            - localAcceleration.x * accelerationLeanDegreesPerUnit
            + Mathf.Clamp(
                yawRate / turnRateForFullLean,
                -1f,
                1f) * turnLeanDegrees;
        Vector2 targetLean = new Vector2(targetPitch, targetRoll);

        bodyLeanSpring.Update(
            targetLean * bodyLeanMultiplier,
            deltaTime,
            bodyLeanFrequencyHz,
            bodyLeanDamping);
        bodyLeanSpring.ClampValue(
            Vector2.one * -maximumBodyLeanDegrees,
            Vector2.one * maximumBodyLeanDegrees);

        legLeanSpring.Update(
            targetLean,
            deltaTime,
            legLeanFrequencyHz,
            legLeanDamping);
        legLeanSpring.ClampValue(
            new Vector2(
                -maximumLegPitchDegrees,
                -maximumLegRollDegrees),
            new Vector2(
                maximumLegPitchDegrees,
                maximumLegRollDegrees));
        legYawSpring.Update(
            robotYaw,
            deltaTime,
            legYawFrequencyHz,
            legYawDamping);

        for (int index = 0;
             index < eggStackAnimationStates.Count;
             index++)
        {
            EggStackAnimationState stack = eggStackAnimationStates[index];
            float frequency = stackLeanFrequencyHz
                / (1f + index * stackFrequencyFalloff);
            stack.LeanSpring.Update(
                targetLean * stackLeanMultiplier,
                deltaTime,
                frequency,
                stackLeanDamping);
            stack.LeanSpring.ClampValue(
                Vector2.one * -maximumStackLeanDegrees,
                Vector2.one * maximumStackLeanDegrees);
        }

        UpdateWheelAnimation(localVelocity.z, yawRate, deltaTime);

        previousVisualVelocity = worldVelocity;
        previousRobotYaw = robotYaw;
        ApplyProceduralVisualRotations();
    }

    private void ApplyProceduralVisualRotations()
    {
        if (robotBodyVisual != null)
        {
            robotBodyVisual.localRotation = robotBodyRestRotation
                * Quaternion.Euler(
                    bodyLeanSpring.Value.x,
                    0f,
                    bodyLeanSpring.Value.y);
        }

        if (robotLegVisual != null)
        {
            float yawOffset = Mathf.Clamp(
                Mathf.DeltaAngle(
                    transform.eulerAngles.y,
                    legYawSpring.Value),
                -maximumLegYawOffset,
                maximumLegYawOffset);
            robotLegVisual.localRotation = robotLegRestRotation
                * Quaternion.Euler(
                    legLeanSpring.Value.x,
                    yawOffset,
                    legLeanSpring.Value.y);
        }

        for (int index = 0;
             index < eggStackAnimationStates.Count;
             index++)
        {
            EggStackAnimationState stack = eggStackAnimationStates[index];
            if (stack.Transform != null)
            {
                stack.Transform.localRotation = stack.RestRotation
                    * Quaternion.Euler(
                        stack.LeanSpring.Value.x,
                        0f,
                        stack.LeanSpring.Value.y);
            }
        }

        for (int index = 0;
             index < wheelAnimationStates.Count;
             index++)
        {
            WheelAnimationState wheel = wheelAnimationStates[index];
            if (wheel.Transform != null)
            {
                wheel.Transform.localRotation = wheel.RestRotation
                    * Quaternion.Euler(wheel.SpinDegrees, 0f, 0f);
            }
        }
    }

    private void UpdateWheelAnimation(
        float forwardSpeed,
        float yawRateDegrees,
        float deltaTime)
    {
        float yawRateRadians = yawRateDegrees * Mathf.Deg2Rad;
        float safeRadius = Mathf.Max(0.001f, wheelRadius);
        for (int index = 0;
             index < wheelAnimationStates.Count;
             index++)
        {
            WheelAnimationState wheel = wheelAnimationStates[index];
            float wheelTravelSpeed = forwardSpeed
                - yawRateRadians * wheel.LocalSideOffset;
            float spinDelta = wheelTravelSpeed
                / safeRadius
                * Mathf.Rad2Deg
                * wheelSpinDirection
                * deltaTime;
            wheel.SpinDegrees = Mathf.Repeat(
                wheel.SpinDegrees + spinDelta,
                360f);
        }
    }

    private void RefreshVisibleEggs()
    {
        if (usesEggStackSockets)
        {
            for (int stackIndex = 0;
                 stackIndex < eggStackRoots.Count;
                 stackIndex++)
            {
                Transform stack = eggStackRoots[stackIndex];
                if (stack != null)
                {
                    stack.gameObject.SetActive(
                        stackIndex == 0
                        || storedEggs > stackIndex * EggsPerStack);
                }
            }
        }

        for (int index = 0; index < carriedEggVisuals.Count; index++)
        {
            GameObject visual = carriedEggVisuals[index];
            if (visual != null)
            {
                ChickenEgg.ApplyTypeVisual(
                    visual,
                    index < storedEggTypes.Count
                        ? storedEggTypes[index]
                        : ChickenEgg.EggType.Common);
                visual.SetActive(index < storedEggs);
            }
        }
    }

    private void InitializeCarriedEggVisuals()
    {
        carriedEggVisuals.Clear();
        eggStackRoots.Clear();
        usesEggStackSockets = false;

        var socketsByStack = new Dictionary<Transform, List<Transform>>();
        Transform[] descendants = GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < descendants.Length; index++)
        {
            Transform socket = descendants[index];
            if (socket == null
                || !socket.name.StartsWith(
                    EggSocketPrefix,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Transform stack = FindEggStackRoot(socket);
            if (stack == null)
            {
                continue;
            }

            if (!socketsByStack.TryGetValue(
                    stack,
                    out List<Transform> stackSockets))
            {
                stackSockets = new List<Transform>(EggsPerStack);
                socketsByStack.Add(stack, stackSockets);
            }

            stackSockets.Add(socket);
        }

        GameObject visualPrefab = carriedEggVisualPrefab;
        Transform legacyVisual = GetFirstLegacyVisibleEgg();
        if (visualPrefab == null && legacyVisual != null)
        {
            visualPrefab = legacyVisual.gameObject;
        }

        if (visualPrefab != null && socketsByStack.Count > 0)
        {
            eggStackRoots.AddRange(socketsByStack.Keys);
            eggStackRoots.Sort(CompareEggStackRoots);

            // Each higher tray is a child of the tray immediately below it.
            // Its local spring therefore adds another rotation on top of all
            // lower tray springs, producing a genuinely cumulative wobble
            // instead of several sibling trays leaning in parallel.
            for (int stackIndex = 1;
                 stackIndex < eggStackRoots.Count;
                 stackIndex++)
            {
                Transform lowerStack = eggStackRoots[stackIndex - 1];
                Transform upperStack = eggStackRoots[stackIndex];
                if (lowerStack != null
                    && upperStack != null
                    && upperStack.parent != lowerStack)
                {
                    upperStack.SetParent(lowerStack, true);
                }
            }

            Vector3 visualScale = carriedEggVisualPrefab != null
                ? carriedEggVisualScale
                : legacyVisual.localScale;
            for (int stackIndex = 0;
                 stackIndex < eggStackRoots.Count;
                 stackIndex++)
            {
                Transform stack = eggStackRoots[stackIndex];
                List<Transform> stackSockets = socketsByStack[stack];
                stackSockets.Sort((first, second) =>
                    string.CompareOrdinal(first.name, second.name));

                for (int socketIndex = 0;
                     socketIndex < stackSockets.Count;
                     socketIndex++)
                {
                    Transform socket = stackSockets[socketIndex];
                    GameObject visual = Instantiate(
                        visualPrefab,
                        socket,
                        false);
                    visual.name =
                        $"Carried Egg {carriedEggVisuals.Count + 1:00}";
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.identity;
                    visual.transform.localScale = visualScale;
                    visual.SetActive(false);
                    carriedEggVisuals.Add(visual);
                }
            }

            usesEggStackSockets = carriedEggVisuals.Count > 0;
            if (usesEggStackSockets && legacyVisual != null)
            {
                GameObject legacyRoot = legacyVisual.parent.gameObject;
                legacyRoot.SetActive(false);
                Destroy(legacyRoot);
            }
        }

        if (usesEggStackSockets || visibleEggSlots == null)
        {
            return;
        }

        for (int index = 0; index < visibleEggSlots.Length; index++)
        {
            if (visibleEggSlots[index] != null)
            {
                visibleEggSlots[index].localScale = carriedEggVisualScale;
                carriedEggVisuals.Add(visibleEggSlots[index].gameObject);
            }
        }
    }

    private Transform FindEggStackRoot(Transform socket)
    {
        Transform candidate = socket.parent;
        while (candidate != null && candidate != transform)
        {
            if (candidate.name.StartsWith(
                    EggStackPrefix,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            candidate = candidate.parent;
        }

        return null;
    }

    private Transform GetFirstLegacyVisibleEgg()
    {
        if (visibleEggSlots == null)
        {
            return null;
        }

        for (int index = 0; index < visibleEggSlots.Length; index++)
        {
            if (visibleEggSlots[index] != null)
            {
                return visibleEggSlots[index];
            }
        }

        return null;
    }

    private int CompareEggStackRoots(Transform first, Transform second)
    {
        if (first.gameObject.activeSelf != second.gameObject.activeSelf)
        {
            return first.gameObject.activeSelf ? -1 : 1;
        }

        float firstHeight = transform.InverseTransformPoint(first.position).y;
        float secondHeight = transform.InverseTransformPoint(second.position).y;
        int heightComparison = firstHeight.CompareTo(secondHeight);
        return heightComparison != 0
            ? heightComparison
            : string.CompareOrdinal(first.name, second.name);
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
        turnInPlaceStartAngle = Mathf.Max(1f, turnInPlaceStartAngle);
        turnInPlaceFinishAngle = Mathf.Clamp(
            turnInPlaceFinishAngle,
            0f,
            turnInPlaceStartAngle);
        turnInPlaceSpeed = Mathf.Max(1f, turnInPlaceSpeed);
        turnSpeedBonusPerTier = Mathf.Max(0f, turnSpeedBonusPerTier);
        movingHeadingCorrectionSpeed = Mathf.Max(
            0f,
            movingHeadingCorrectionSpeed);
        chickenPushRadius = Mathf.Max(0.05f, chickenPushRadius);
        chickenPushForwardOffset = Mathf.Max(0f, chickenPushForwardOffset);
        chickenPushSpeed = Mathf.Max(0f, chickenPushSpeed);
        maximumChickenPushSpeed = Mathf.Max(
            chickenPushSpeed,
            maximumChickenPushSpeed);
        speedForFullLean = Mathf.Max(0.1f, speedForFullLean);
        maximumVisualAcceleration = Mathf.Max(0f, maximumVisualAcceleration);
        accelerationVisualResponse = Mathf.Max(0f, accelerationVisualResponse);
        turnRateForFullLean = Mathf.Max(1f, turnRateForFullLean);
        bodyLeanFrequencyHz = Mathf.Max(0.01f, bodyLeanFrequencyHz);
        legYawFrequencyHz = Mathf.Max(0.01f, legYawFrequencyHz);
        legLeanFrequencyHz = Mathf.Max(0.01f, legLeanFrequencyHz);
        wheelRadius = Mathf.Max(0.001f, wheelRadius);
        stackLeanFrequencyHz = Mathf.Max(0.01f, stackLeanFrequencyHz);
        chickenPickupDistance = Mathf.Max(0.1f, chickenPickupDistance);
        chickenFacingTolerance = Mathf.Clamp(
            chickenFacingTolerance,
            1f,
            45f);
        chickenTurnSpeed = Mathf.Max(1f, chickenTurnSpeed);
        chickenDeliveryDistance = Mathf.Max(0.1f, chickenDeliveryDistance);
        chickenHandGrabAmount = Mathf.Clamp01(chickenHandGrabAmount);
    }
}
