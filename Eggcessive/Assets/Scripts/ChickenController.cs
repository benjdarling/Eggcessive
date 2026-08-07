using System.Collections.Generic;
using Action = System.Action;
using DitzelGames.FastIK;
using GatorDragonGames.JigglePhysics;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(Rigidbody))]
public sealed class ChickenController : MonoBehaviour
{
    public enum ChickenBreed
    {
        White,
        Brown,
        Black,
        Blue,
        Purple,
        Rainbow,
        Cosmic
    }

    public const int MaximumChickenCount = 50;
    public static event Action EggLaid;

    private enum ChickenState
    {
        Idle,
        Moving,
        LeavingIncubator,
        SeekingFood,
        Eating,
        EggLaying
    }

    private static readonly List<ChickenController> ActiveChickens = new List<ChickenController>();
    private static readonly int IsEatingParameter = Animator.StringToHash("IsEating");
    private static readonly int BlinkParameter = Animator.StringToHash("Blink");
    private static readonly int BlinkSpeedParameter = Animator.StringToHash("BlinkSpeed");
    private static readonly int TurnLeanParameter = Animator.StringToHash("TurnLean");
    private static readonly int MoodParameter = Animator.StringToHash("Mood");
    private static readonly int LayEggParameter = Animator.StringToHash("LayEgg");
    private static readonly int IdleState = Animator.StringToHash("Base Layer.Idle");
    private static readonly int LayEggState = Animator.StringToHash("Base Layer.Lay Egg");
    private static readonly int HeldState = Animator.StringToHash("Base Layer.Held");
    private static readonly System.Reflection.FieldInfo JiggleRigSegmentField =
        typeof(JiggleRig).GetField(
            "segment",
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
    private static readonly int BaseMapTransform = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int MainTextureTransform = Shader.PropertyToID("_MainTex_ST");
    private const int MaximumWanderCrowdSamples = 128;
    private const int MaximumSeparationSamples = 128;
    private const float EggSpawnFrame = 9f;
    private const float DefaultLayEggFrameCount = 22f;
    private const float MissingNavMeshWarningDelay = 2f;
    private const string WingFlutterLayerName = "Wing Flutter Layer";
    private const string TalkLayerName = "Talk Layer";
    private static bool hasWarnedAboutMissingNavMesh;
    private static int aiSchedulerFrame = -1;
    private static int aiSchedulerCursor;
    private static float nextAiSchedulerTime;
    private static int eggPushSchedulerStep = -1;
    private static int eggPushSchedulerCursor;
    private static ChickenUpdateScheduler schedulerDriver;

    [Header("Breed")]
    [SerializeField] private ChickenBreed breed = ChickenBreed.White;

    [Header("Size Variation")]
    [SerializeField, Range(0f, 0.2f)] private float scaleVariation = 0.05f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float minIdleTime = 1f;
    [SerializeField, Min(0f)] private float maxIdleTime = 3f;
    [SerializeField, Min(0f)] private float wanderRadius = 1f;
    [SerializeField, Min(0.01f)] private float navMeshSampleDistance = 0.75f;
    [SerializeField, Min(1)] private int destinationAttempts = 12;
    [Tooltip("Number of NavMesh roam destinations compared before choosing the least crowded one.")]
    [SerializeField, Min(1)] private int wanderDestinationCandidates = 4;
    [Tooltip("Nearby chickens inside this radius make a roam destination less desirable.")]
    [SerializeField, Min(0.01f)] private float wanderCrowdRadius = 0.75f;
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
    [Tooltip("Hungry chickens ignore feed farther away than this planar distance.")]
    [SerializeField, Min(0.01f)] private float foodSearchRadius = 2f;
    [SerializeField, Min(0.01f)] private float eatingDistance = 0.2f;
    [SerializeField, Min(0.01f)] private float foodPerBite = 10f;
    [SerializeField, Min(0.01f)] private float secondsPerBite = 0.65f;
    [Tooltip("Blendshape driven by the chicken's current food score.")]
    [SerializeField] private string fatBlendShapeName = "fat";
    [SerializeField, Range(-1f, 0f)] private float minimumFat = -0.2f;
    [SerializeField, Range(0f, 1f)] private float maximumFat = 1f;
    [SerializeField, Min(0.01f)] private float fatBlendSmoothTime = 0.35f;

    [Header("Animation")]
    [SerializeField] private Animator animator = null;
    [SerializeField, Min(0.01f)] private float minBlinkInterval = 2f;
    [SerializeField, Min(0.01f)] private float maxBlinkInterval = 6f;
    [SerializeField, Range(0f, 0.5f)] private float blinkSpeedVariation = 0.1f;
    [SerializeField, Min(1f)] private float fullLeanTurnRate = 180f;
    [SerializeField, Min(0.01f)] private float leanSmoothTime = 0.08f;
    [SerializeField, Range(0f, 1f)] private float leanStrength = 1f;

    [Header("Mood Expressions")]
    [SerializeField, Min(0.1f)] private float minMoodInterval = 4f;
    [SerializeField, Min(0.1f)] private float maxMoodInterval = 9f;
    [SerializeField, Min(0.1f)] private float minMoodDuration = 2f;
    [SerializeField, Min(0.1f)] private float maxMoodDuration = 4.5f;
    [SerializeField, Min(0.01f)] private float moodNeighbourRadius = 0.8f;
    [Tooltip("Up to this many nearby chickens can contribute to a happy social mood.")]
    [SerializeField, Min(1)] private int comfortableMoodNeighbourCount = 3;
    [Tooltip("At this many nearby chickens, crowding contributes fully to anger.")]
    [SerializeField, Min(2)] private int crowdedMoodNeighbourCount = 6;
    [SerializeField, Min(0.01f)] private float angryMoodBlendTime = 0.05f;
    [SerializeField, Min(0.01f)] private float moodBlendTime = 0.12f;

    [Header("Secondary Motion LOD")]
    [Tooltip(
        "Optional conventional LODGroup used to stop comb, wattle, and tail " +
        "secondary motion. Leave empty when the model uses generated Mesh LODs.")]
    [SerializeField] private LODGroup secondaryMotionLodGroup;
    [Tooltip(
        "Renderer whose generated Mesh LODs control secondary motion. When " +
        "empty, the first child renderer with generated Mesh LODs is used.")]
    [SerializeField] private Renderer secondaryMotionMeshLodRenderer;
    [Tooltip(
        "Highest LOD that keeps secondary motion active. Zero disables it " +
        "as soon as LOD1 becomes active.")]
    [SerializeField, Min(0)] private int lastSecondaryMotionLod;
    [Tooltip(
        "LOD checks are staggered between chickens to avoid checking the " +
        "whole flock in one frame.")]
    [SerializeField, Min(1)] private int secondaryMotionLodCheckIntervalFrames =
        8;
    [Tooltip(
        "Minimum delay before secondary motion wakes after a chicken becomes " +
        "visible. A brief stable pose prevents an immediate physics pop.")]
    [SerializeField, Min(0f)] private float minimumSecondaryMotionWakeDelay =
        0.12f;
    [Tooltip(
        "Maximum randomized wake delay. Spreading activation across this " +
        "window prevents a whole pen's combs, wattles, and tails moving together.")]
    [SerializeField, Min(0f)] private float maximumSecondaryMotionWakeDelay =
        0.9f;
    [Tooltip(
        "Time taken to blend secondary physics from the stable animated pose " +
        "to its full authored influence after waking.")]
    [SerializeField, Min(0.01f)] private float secondaryMotionInfluenceRampDuration =
        1f;

    [Header("Performance")]
    [Tooltip(
        "Maximum chickens allowed to run an AI decision update per scheduler tick.")]
    [SerializeField, Min(1)] private int maximumAiUpdatesPerTick = 32;
    [Tooltip(
        "How often the shared scheduler processes the next fixed-size chicken batch.")]
    [SerializeField, Range(1f, 60f)] private float aiSchedulerUpdateRateHz = 10f;
    [Tooltip("Temporarily disabled while distant chickens use generated Mesh LODs only.")]
    [SerializeField] private bool enableFarImpostor = false;
    [Tooltip(
        "Generated Mesh LOD at which the editable sprite child replaces all " +
        "3D renderers and animation is suspended.")]
    [SerializeField, Min(1)] private int farImpostorMeshLod = 3;
    [SerializeField] private SpriteRenderer farImpostorRenderer;
    [Tooltip(
        "Maximum chickens allowed to perform egg-overlap physics checks per fixed update.")]
    [SerializeField, Min(1)] private int maximumEggPushChecksPerFixedUpdate = 8;

    [Header("Hand Carry")]
    [Tooltip(
        "Chicken bone aligned directly to the UI hand's Bone_Attach. Blender axis suffixes are matched automatically.")]
    [SerializeField] private string heldAttachBoneName = "c_skull_01";
    [Tooltip(
        "Blendshape applied at full weight while this chicken is carried by hand.")]
    [SerializeField] private string heldBlendShapeName = "held";
    [SerializeField, Range(0f, 100f)] private float heldBlendShapeWeight =
        100f;
    [SerializeField, Min(0f)] private float heldAnimationTransitionDuration =
        0.08f;
    [SerializeField, Range(0f, 20f)] private float heldDragMaximumAngle = 8f;
    [SerializeField, Min(0.01f)] private float heldDragSpeedForMaximumAngle =
        2f;
    [SerializeField, Range(0.1f, 10f)] private float heldDragSpringFrequency =
        3.25f;
    [SerializeField, Range(0.05f, 2f)] private float heldDragSpringDamping =
        0.72f;

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
    [Tooltip(
        "Outer part of the separation radius where chickens are considered " +
        "settled. This prevents corrections from repeatedly crossing one threshold.")]
    [SerializeField, Min(0f)] private float separationSettleMargin = 0.035f;
    [SerializeField, Min(0.01f)] private float separationResponseSpeed = 8f;
    [SerializeField, Range(0f, 1f)]
    private float idleSeparationStrengthMultiplier = 0.35f;
    [SerializeField, Min(0f)] private float separationStopSpeed = 0.006f;
    [Tooltip("How far beyond the chicken's body collider eggs begin to receive a gentle nudge.")]
    [SerializeField, Min(0f)] private float eggPushRadius = 0.025f;
    [SerializeField, Min(0f)] private float eggPushForce = 3.25f;
    [SerializeField, Min(0.01f)] private float maximumEggPushSpeed = 0.25f;

    [Header("Egg Laying")]
    [SerializeField] private GameObject eggPrefab = null;
    [SerializeField] private GameObject cosmicEggPrefab = null;
    [SerializeField, Min(0f)] private float minInitialEggLayTime = 1.5f;
    [SerializeField, Min(0f)] private float maxInitialEggLayTime = 3.5f;
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

    [Header("Chicken Audio")]
    [Tooltip("Independent random talk interval for a chicken in a small flock.")]
    [SerializeField, Min(0.1f)] private float minTalkInterval = 7f;
    [SerializeField, Min(0.1f)] private float maxTalkInterval = 16f;
    [Tooltip(
        "Each additional active chicken lengthens every chicken's next interval. " +
        "The timers remain independent, but large flocks do not become a wall of noise.")]
    [SerializeField, Min(0f)] private float talkIntervalScalePerAdditionalChicken = 0.12f;
    [Tooltip(
        "Gives every chicken a persistent chatty or quiet personality by varying " +
        "its individual talk cadence.")]
    [SerializeField, Range(0f, 0.75f)] private float talkCadenceVariation = 0.25f;
    [SerializeField, Range(0.01f, 0.5f)] private float talkPoseBlendInFraction = 0.1f;
    [SerializeField, Range(0f, 0.5f)] private float talkPoseHoldFraction = 0.15f;
    [SerializeField] private AudioClip[] talkSounds = System.Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] layEggSounds = System.Array.Empty<AudioClip>();
    [SerializeField, Range(0f, 1f)] private float talkVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float layEggVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float minLayEggVolumeMultiplier = 0.7f;
    [SerializeField, Range(0f, 1f)] private float maxLayEggVolumeMultiplier = 1f;
    [SerializeField, Range(0f, 0.5f)] private float voicePitchVariation = 0.1f;
    [SerializeField, Range(0f, 0.5f)] private float voiceVolumeVariation = 0.1f;
    [SerializeField, Min(0f)] private float voiceMinDistance = 0f;
    [SerializeField, Min(0.01f)] private float voiceNearSilentDistance = 12f;
    [SerializeField, Min(0.01f)] private float voiceMaxDistance = 16f;

    private readonly Collider[] eggColliderBuffer = new Collider[16];
    private NavMeshPath path;

    private NavMeshAgent agent;
    private CapsuleCollider bodyCollider;
    private Rigidbody physicsBody;
    private NavMeshQueryFilter navMeshQueryFilter;
    private ChickenState state;
    private FoodPile targetFood;
    private float stateEndTime;
    private float nextFoodSearchTime;
    private float nextBiteTime;
    private float eggTimerRemaining;
    private float eggTimerBeforeMachineControl;
    private float stateTimeBeforeMachineControl;
    private ChickenState stateBeforeMachineControl;
    private bool eggSpawnedBeforeMachineControl;
    private bool hasMachineControlSnapshot;
    private float foodScore;
    private float activeFoodProductionSpeed = 1f;
    private float activeFoodPremiumChanceMultiplier = 1f;
    private float nextBlinkTime;
    private float turnLean;
    private float turnLeanVelocity;
    private float nextMoodDecisionTime;
    private float moodEndTime;
    private float moodTarget;
    private float moodValue;
    private float moodVelocity;
    private int wingFlutterLayerIndex = -1;
    private int talkLayerIndex = -1;
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
    private float navigationRetryStartedAt;
    private Transform eggSpawnBone;
    private Transform heldAttachBone;
    private bool eggSpawnedDuringLay;
    private float eggSpawnNormalizedTime = EggSpawnFrame / DefaultLayEggFrameCount;
    private bool hasIncubatorExitDestination;
    private bool isTraversingIncubatorExit;
    private Vector3 incubatorExitDestination;
    private int eggCollisionMask;
    private bool isMachineControlled;
    private bool isHeldByHand;
    private Quaternion heldBaseRotation;
    private Vector3 heldDragAngles;
    private Vector3 heldDragAngularVelocity;
    private Vector3 previousHeldAttachPosition;
    private bool hasPreviousHeldAttachPosition;
    private readonly List<SkinnedMeshRenderer> heldBlendShapeRenderers =
        new List<SkinnedMeshRenderer>();
    private readonly List<int> heldBlendShapeIndices = new List<int>();
    private readonly List<SkinnedMeshRenderer> fatBlendShapeRenderers =
        new List<SkinnedMeshRenderer>();
    private readonly List<int> fatBlendShapeIndices = new List<int>();
    private float fatBlendValue;
    private float fatBlendVelocity;
    private float lastAppliedFatBlendValue = float.PositiveInfinity;
    private MaterialPropertyBlock breedPropertyBlock;
    private Vector3 cachedSeparation;
    private Vector3 targetSeparationVelocity;
    private int separationUpdateOffset;
    private Behaviour[] lodControlledSecondaryMotion =
        System.Array.Empty<Behaviour>();
    private bool[] lodControlledSecondaryMotionDefaults =
        System.Array.Empty<bool>();
    private float secondaryMotionTransitionHeight = -1f;
    private Mesh secondaryMotionMeshLodMesh;
    private bool secondaryMotionLodAvailable;
    private int nextSecondaryMotionLodCheckFrame;
    private bool secondaryMotionActive = true;
    private bool secondaryMotionWakePending;
    private float secondaryMotionWakeTime;
    private bool secondaryMotionInfluenceRampActive;
    private float secondaryMotionInfluenceRampStartTime;
    private JiggleRig[] secondaryMotionJiggleRigs =
        System.Array.Empty<JiggleRig>();
    private readonly List<JigglePointParameters> rampedJiggleParameters =
        new List<JigglePointParameters>();
    private Renderer[] detailedRenderers = System.Array.Empty<Renderer>();
    private bool[] detailedRendererDefaults = System.Array.Empty<bool>();
    private Behaviour[] farDisabledBehaviours =
        System.Array.Empty<Behaviour>();
    private bool[] farDisabledBehaviourDefaults =
        System.Array.Empty<bool>();
    private ObstacleAvoidanceType detailedAvoidanceType;
    private bool animatorDefaultEnabled = true;
    private bool initialAnimatorPhaseApplied;
    private float initialAnimatorPhase;
    private bool usingFarImpostor;
    private bool penVisualsEnabled = true;
    private AudioSource talkAudioSource;
    private AudioSource layEggAudioSource;
    private float nextTalkTime;
    private float talkCadenceMultiplier = 1f;
    private int lastTalkClipIndex = -1;
    private int lastLayEggClipIndex = -1;
    private bool talkPoseActive;
    private float talkPoseStartTime;
    private float talkPoseBlendInEndTime;
    private float talkPoseSustainEndTime;
    private float talkPoseEndTime;
    private float lastAiUpdateTime;
    private int separationSamplePhase;
    private static Camera secondaryMotionCamera;

    public float FoodScore => foodScore;
    public float MaximumFoodScore => maximumFoodScore;
    public float FoodScoreNormalized => maximumFoodScore > 0f ? foodScore / maximumFoodScore : 0f;
    public float FoodSearchRadius => foodSearchRadius;
    public static IReadOnlyList<ChickenController> ActiveInstances => ActiveChickens;
    public ChickenBreed Breed => breed;
    public bool IsMachineControlled => isMachineControlled;
    public bool CanBePickedUp =>
        !isMachineControlled
        && !isTraversingIncubatorExit
        && state != ChickenState.EggLaying;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveChickens.Clear();
        EggLaid = null;
        hasWarnedAboutMissingNavMesh = false;
        aiSchedulerFrame = -1;
        aiSchedulerCursor = 0;
        nextAiSchedulerTime = 0f;
        eggPushSchedulerStep = -1;
        eggPushSchedulerCursor = 0;
        secondaryMotionCamera = null;
        schedulerDriver = null;
    }

    public void SetPenVisualsEnabled(bool enabled)
    {
        if (penVisualsEnabled == enabled)
        {
            return;
        }

        penVisualsEnabled = enabled;
        if (!enabled)
        {
            secondaryMotionWakePending = false;
            if (farImpostorRenderer != null)
            {
                farImpostorRenderer.enabled = false;
            }

            for (int index = 0; index < detailedRenderers.Length; index++)
            {
                if (detailedRenderers[index] != null)
                {
                    detailedRenderers[index].enabled = false;
                }
            }

            if (animator != null)
            {
                animator.enabled = false;
            }

            // Hidden pens do not render any chicken detail, so none of their
            // jiggle, wattle, tail or wind simulation should keep scheduling
            // LateUpdate work either.
            SetSecondaryMotionEnabled(false);

            for (int index = 0;
                 index < farDisabledBehaviours.Length;
                 index++)
            {
                if (farDisabledBehaviours[index] != null)
                {
                    farDisabledBehaviours[index].enabled = false;
                }
            }

            return;
        }

        SetFarImpostorActive(false, true);
        ResetAnimatorForPenFocus();
        BeginSecondaryMotionWake();
    }

    private void ResetAnimatorForPenFocus()
    {
        if (animator == null
            || animator.runtimeAnimatorController == null)
        {
            return;
        }

        // Egg production continues while another pen is focused. Any lay
        // trigger raised while this Animator was disabled would otherwise stay
        // queued and make the whole pen enter Lay Egg on the same frame.
        animator.ResetTrigger(LayEggParameter);
        animator.SetBool(IsEatingParameter, false);
        if (!animator.enabled || !animator.HasState(0, IdleState))
        {
            return;
        }

        animator.Play(IdleState, 0, Random.value);
        animator.Update(0f);
    }

    public void AlignHeldBoneTo(Vector3 attachPosition)
    {
        if (heldAttachBone == null)
        {
            heldAttachBone = FindHeldAttachBone();
        }

        if (heldAttachBone == null)
        {
            transform.position = attachPosition;
            return;
        }

        transform.position += attachPosition - heldAttachBone.position;
    }

    public void UpdateHeldCarryPose(
        Vector3 attachPosition,
        float deltaTime)
    {
        ApplyHeldBlendShape(isHeldByHand);

        if (!isHeldByHand)
        {
            AlignHeldBoneTo(attachPosition);
            return;
        }

        float frameDeltaTime = Mathf.Max(0f, deltaTime);

        if (!hasPreviousHeldAttachPosition
            || frameDeltaTime <= 0.00001f)
        {
            previousHeldAttachPosition = attachPosition;
            hasPreviousHeldAttachPosition = true;
            transform.rotation = heldBaseRotation;
            AlignHeldBoneTo(attachPosition);
            return;
        }

        Vector3 attachVelocity =
            (attachPosition - previousHeldAttachPosition)
            / frameDeltaTime;
        previousHeldAttachPosition = attachPosition;
        Vector3 planarVelocity = Vector3.ProjectOnPlane(
            attachVelocity,
            Vector3.up);
        Vector3 desiredDragAngles =
            Vector3.Cross(Vector3.up, planarVelocity)
            * (heldDragMaximumAngle
                / heldDragSpeedForMaximumAngle);
        desiredDragAngles = Vector3.ClampMagnitude(
            desiredDragAngles,
            heldDragMaximumAngle);
        StepHeldDragSpring(
            desiredDragAngles,
            Mathf.Min(frameDeltaTime, 1f / 30f));
        float dragAngle = heldDragAngles.magnitude;
        Quaternion dragRotation = dragAngle > 0.0001f
            ? Quaternion.AngleAxis(
                dragAngle,
                heldDragAngles / dragAngle)
            : Quaternion.identity;
        transform.rotation = dragRotation * heldBaseRotation;

        // Re-aligning the skull after rotating the root makes Bone_Attach the
        // effective pivot, so only the chicken's lower body swings.
        AlignHeldBoneTo(attachPosition);
    }

    public void SetHeldCarryRotation(Quaternion rotation)
    {
        heldBaseRotation = rotation;
    }

    private void Awake()
    {
        EnsureSchedulerDriver();
        eggCollisionMask = LayerMask.GetMask("Egg");
        float randomScale = Random.Range(
            1f - scaleVariation,
            1f + scaleVariation);
        transform.localScale *= randomScale;
        CacheSecondaryMotionLod();

        path = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>();
        bodyCollider = GetComponent<CapsuleCollider>();
        physicsBody = GetComponent<Rigidbody>();
        if (physicsBody == null)
        {
            physicsBody = gameObject.AddComponent<Rigidbody>();
        }
        physicsBody.isKinematic = true;
        physicsBody.useGravity = false;
        physicsBody.interpolation = RigidbodyInterpolation.None;
        physicsBody.collisionDetectionMode =
            CollisionDetectionMode.Discrete;
        agent.speed = moveSpeed;
        agent.acceleration = acceleration;
        agent.angularSpeed = angularSpeed;
        agent.stoppingDistance = 0.03f;
        agent.autoBraking = true;
        // The pen NavMesh is static and every destination is validated before
        // assignment, so continuous automatic replanning is unnecessary.
        agent.autoRepath = false;
        // Static obstacles are handled by the baked NavMesh. Chicken/chicken
        // spacing is handled by the smoother custom separation below; running
        // both systems makes dense idle flocks oscillate.
        agent.obstacleAvoidanceType =
            ObstacleAvoidanceType.NoObstacleAvoidance;
        detailedAvoidanceType = agent.obstacleAvoidanceType;
        separationUpdateOffset = Mathf.Abs(GetInstanceID());

        navMeshQueryFilter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (animator != null)
        {
            // Pen visual LOD disables the Animator component completely. Keep
            // its controller state so returning to a pen does not restart every
            // chicken on the first frame of Idle.
            animator.keepAnimatorStateOnDisable = true;
            initialAnimatorPhase = Random.value;
        }

        CacheFarImpostor();
        CacheLayEggAnimationTiming();
        eggSpawnBone = FindEggSpawnBone();
        heldAttachBone = FindHeldAttachBone();
        CacheChickenBlendShapes();

        CacheWingFlutterLayer();
        CacheTalkLayer();
        ApplyBreedVisual();
        talkAudioSource = CreateVoiceAudioSource();
        layEggAudioSource = CreateVoiceAudioSource();
        talkCadenceMultiplier = Random.Range(
            1f - talkCadenceVariation,
            1f + talkCadenceVariation);

        foodScore = Mathf.Clamp(startingFoodScore, 0f, maximumFoodScore);
        fatBlendValue = CalculateTargetFatBlendValue();
        fatBlendVelocity = 0f;
        ApplyFatBlendShape(true);
        previousPlanarForward = GetPlanarForward();
    }

    private void OnEnable()
    {
        ActiveChickens.Add(this);
        navigationRetryStartedAt = Time.time;
        SetFarImpostorActive(false, true);
        ScheduleInitialEgg();
        ScheduleNextBlink();
        ScheduleNextWingFlutter();
        ScheduleNextWingMicroTwitch();
        ScheduleNextMoodDecision(true);
        ScheduleNextTalk(true);
        SetWingFlutterWeight(0f);
        SetTalkPoseWeight(0f);
        talkPoseActive = false;
        ApplyHeldBlendShape(false);
        wingFlutterActive = false;
        wingMicroTwitchActive = false;
        talkPoseActive = false;
        SetTalkPoseWeight(0f);
        previousPlanarForward = GetPlanarForward();
        turnLean = 0f;
        turnLeanVelocity = 0f;
        moodTarget = 0f;
        moodValue = 0f;
        moodVelocity = 0f;
        if (animator != null)
        {
            animator.SetFloat(MoodParameter, 0f);
        }
        nextSecondaryMotionLodCheckFrame = Time.frameCount;
        lastAiUpdateTime = Time.time;
        separationSamplePhase = 0;
        BeginSecondaryMotionWake();
    }

    private void Start()
    {
        ApplyInitialAnimatorPhase();
        if (hasIncubatorExitDestination)
        {
            TryBeginIncubatorExit();
            return;
        }

        TryInitializeNavigation();
        BeginIdle();
    }

    private void ApplyInitialAnimatorPhase()
    {
        if (initialAnimatorPhaseApplied
            || animator == null
            || animator.runtimeAnimatorController == null
            || !animator.HasState(0, IdleState))
        {
            return;
        }

        // Chickens are commonly spawned in a single frame. Starting their
        // looping base animation at different points prevents a pen-wide wave
        // even before any individual AI state changes occur.
        animator.Play(IdleState, 0, initialAnimatorPhase);
        if (animator.enabled)
        {
            animator.Update(0f);
        }

        initialAnimatorPhaseApplied = true;
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
            animator.SetFloat(MoodParameter, 0f);
            SetWingFlutterWeight(0f);
        }
        wingFlutterActive = false;
        wingMicroTwitchActive = false;
        ApplyHeldBlendShape(false);
        isHeldByHand = false;
        hasPreviousHeldAttachPosition = false;
        heldDragAngles = Vector3.zero;
        heldDragAngularVelocity = Vector3.zero;
        cachedSeparation = Vector3.zero;
        targetSeparationVelocity = Vector3.zero;
        targetFood = null;
    }

    private void RunContinuousFrameUpdate()
    {
        UpdateTalk();
        UpdateTalkPose();
        UpdateMoodExpression();
        UpdateFatBlendShape();

        if (penVisualsEnabled)
        {
            UpdateSecondaryMotionLod(false);
            UpdateSecondaryMotionInfluenceRamp();
        }

        // NavMeshAgent rotation has been applied by this point, while Animator
        // evaluation has not. Update the parameter now so the visible turn and
        // its additive lean use the same frame's direction.
        if (penVisualsEnabled && !usingFarImpostor)
        {
            UpdateTurnLean();
        }

        // Keep the expensive neighbour search on the budgeted AI scheduler,
        // but apply its cached result every rendered frame. Applying separation
        // only on an AI tick produces a visible push/pause rhythm in dense
        // groups, especially as the scheduler spreads updates across thousands
        // of chickens.
        ApplyChickenSeparation(Time.deltaTime);

        if (isTraversingIncubatorExit)
        {
            UpdateIncubatorExit();
        }

    }

    private void UpdateMoodExpression()
    {
        if (animator == null)
        {
            return;
        }

        float now = Time.time;
        bool canExpressMood = penVisualsEnabled
            && !usingFarImpostor
            && !isMachineControlled;

        if (!canExpressMood)
        {
            moodTarget = 0f;
        }
        else if (Mathf.Abs(moodTarget) > 0.001f && now >= moodEndTime)
        {
            moodTarget = 0f;
            ScheduleNextMoodDecision(false);
        }
        else if (Mathf.Abs(moodTarget) <= 0.001f
            && Mathf.Abs(moodValue) <= 0.01f
            && now >= nextMoodDecisionTime)
        {
            TryBeginMoodExpression(now);
        }

        float activeBlendTime = moodTarget < 0f
            ? angryMoodBlendTime
            : moodBlendTime;
        moodValue = Mathf.SmoothDamp(
            moodValue,
            moodTarget,
            ref moodVelocity,
            activeBlendTime,
            Mathf.Infinity,
            Time.deltaTime);
        if (Mathf.Abs(moodValue) < 0.001f && moodTarget == 0f)
        {
            moodValue = 0f;
            moodVelocity = 0f;
        }

        animator.SetFloat(MoodParameter, Mathf.Clamp(moodValue, -1f, 1f));
    }

    private void TryBeginMoodExpression(float now)
    {
        int nearbyCount = CountMoodNeighbours();
        float foodNormalized = maximumFoodScore > 0f
            ? Mathf.Clamp01(foodScore / maximumFoodScore)
            : 0f;
        float hungerThreshold = Mathf.Max(0.0001f, seekFoodBelowScore);
        float hunger = 1f - Mathf.Clamp01(foodScore / hungerThreshold);
        float criticalHunger = hunger * hunger;
        float crowding = Mathf.InverseLerp(
            comfortableMoodNeighbourCount,
            crowdedMoodNeighbourCount,
            nearbyCount);
        float hasCompany = nearbyCount > 0 ? 1f : 0f;
        float comfortableCompany = hasCompany * (1f - crowding);

        // Hunger and crowding can each cause anger, while their interaction is
        // deliberately strongest so a hungry chicken in a crush is very likely
        // to show it.
        float angryWeight = criticalHunger * 0.3f
            + crowding * 0.25f
            + criticalHunger * crowding * 1.2f;
        // Happiness needs both good food and a small amount of company. Squaring
        // food makes genuinely well-fed chickens much more expressive than merely
        // average ones.
        float happyWeight = foodNormalized
            * foodNormalized
            * comfortableCompany
            * 1.2f;
        const float neutralWeight = 0.8f;
        float totalWeight = angryWeight + happyWeight + neutralWeight;
        float roll = Random.value * totalWeight;

        if (roll < angryWeight)
        {
            moodTarget = -1f;
        }
        else if (roll < angryWeight + happyWeight)
        {
            moodTarget = 1f;
        }
        else
        {
            ScheduleNextMoodDecision(false);
            return;
        }

        moodEndTime = now + Random.Range(minMoodDuration, maxMoodDuration);
    }

    private int CountMoodNeighbours()
    {
        float radiusSquared = moodNeighbourRadius * moodNeighbourRadius;
        Vector3 position = transform.position;
        int nearbyCount = 0;

        for (int index = 0; index < ActiveChickens.Count; index++)
        {
            ChickenController other = ActiveChickens[index];
            if (other == null || other == this || !other.isActiveAndEnabled)
            {
                continue;
            }

            Vector3 offset = other.transform.position - position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= radiusSquared)
            {
                nearbyCount++;
            }
        }

        return nearbyCount;
    }

    private void ScheduleNextMoodDecision(bool initialSchedule)
    {
        float delay = Random.Range(minMoodInterval, maxMoodInterval);
        if (initialSchedule)
        {
            delay *= Random.Range(0.25f, 1f);
        }

        nextMoodDecisionTime = Time.time + delay;
    }

    internal static void TickScheduledUpdates()
    {
        ChickenController schedulerSource = null;
        int count = ActiveChickens.Count;
        for (int index = 0; index < count; index++)
        {
            ChickenController chicken = ActiveChickens[index];
            if (chicken == null || !chicken.isActiveAndEnabled)
            {
                continue;
            }

            schedulerSource ??= chicken;
            chicken.RunContinuousFrameUpdate();
        }

        if (schedulerSource != null)
        {
            RunAiScheduler(schedulerSource);
        }
    }

    private void UpdateTalk()
    {
        float now = Time.time;
        if (now < nextTalkTime)
        {
            return;
        }

        ScheduleNextTalk(false);
        if (!penVisualsEnabled
            || isMachineControlled
            || state == ChickenState.EggLaying
            || talkAudioSource == null
            || talkAudioSource.isPlaying)
        {
            return;
        }

        float playbackDuration = PlayRandomVoice(
            talkAudioSource,
            talkSounds,
            ref lastTalkClipIndex,
            talkVolume,
            1f - voiceVolumeVariation,
            1f + voiceVolumeVariation);
        if (playbackDuration > 0f)
        {
            BeginTalkPose(playbackDuration);
        }
    }

    private void ScheduleNextTalk(bool initialSchedule)
    {
        int talkingPopulation = 0;
        for (int index = 0; index < ActiveChickens.Count; index++)
        {
            ChickenController chicken = ActiveChickens[index];
            if (chicken != null
                && chicken.isActiveAndEnabled
                && chicken.penVisualsEnabled
                && !chicken.isMachineControlled)
            {
                talkingPopulation++;
            }
        }

        float populationScale = 1f
            + Mathf.Max(0, talkingPopulation - 1)
                * talkIntervalScalePerAdditionalChicken;
        float minimum = Mathf.Max(0.1f, minTalkInterval);
        float maximum = Mathf.Max(minimum, maxTalkInterval);
        float delay = Random.Range(minimum, maximum)
            * populationScale
            * talkCadenceMultiplier;
        if (initialSchedule)
        {
            delay *= Random.Range(0.2f, 1f);
        }

        nextTalkTime = Time.time + delay;
    }

    private AudioSource CreateVoiceAudioSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.minDistance = voiceMinDistance;
        source.maxDistance = Mathf.Max(voiceMinDistance, voiceMaxDistance);
        source.rolloffMode = AudioRolloffMode.Custom;
        float normalizedNearSilentDistance = Mathf.Clamp(
            voiceNearSilentDistance / source.maxDistance,
            0f,
            1f);
        float normalizedFirstFalloffPoint = normalizedNearSilentDistance / 3f;
        float normalizedSecondFalloffPoint = normalizedNearSilentDistance * 2f / 3f;
        source.SetCustomCurve(
            AudioSourceCurveType.CustomRolloff,
            new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(normalizedFirstFalloffPoint, 0.2f),
                new Keyframe(normalizedSecondFalloffPoint, 0.03f),
                new Keyframe(normalizedNearSilentDistance, 0.005f),
                new Keyframe(1f, 0f)));
        return source;
    }

    private static bool HasUsableClip(AudioClip[] clips)
    {
        if (clips == null)
        {
            return false;
        }

        for (int index = 0; index < clips.Length; index++)
        {
            if (clips[index] != null)
            {
                return true;
            }
        }

        return false;
    }

    private float PlayRandomVoice(
        AudioSource source,
        AudioClip[] clips,
        ref int lastClipIndex,
        float baseVolume,
        float minimumVolumeMultiplier,
        float maximumVolumeMultiplier)
    {
        if (source == null || !HasUsableClip(clips))
        {
            return 0f;
        }

        int startIndex = Random.Range(0, clips.Length);
        int selectedIndex = -1;
        for (int offset = 0; offset < clips.Length; offset++)
        {
            int candidateIndex = (startIndex + offset) % clips.Length;
            if (clips[candidateIndex] != null
                && (candidateIndex != lastClipIndex || clips.Length == 1))
            {
                selectedIndex = candidateIndex;
                break;
            }
        }

        if (selectedIndex < 0)
        {
            for (int index = 0; index < clips.Length; index++)
            {
                if (clips[index] != null)
                {
                    selectedIndex = index;
                    break;
                }
            }
        }

        if (selectedIndex < 0)
        {
            return 0f;
        }

        lastClipIndex = selectedIndex;
        source.pitch = Random.Range(
            1f - voicePitchVariation,
            1f + voicePitchVariation);
        float variedVolume = baseVolume * Random.Range(
            minimumVolumeMultiplier,
            maximumVolumeMultiplier);
        AudioClip selectedClip = clips[selectedIndex];
        source.PlayOneShot(selectedClip, Mathf.Clamp01(variedVolume));
        return selectedClip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch));
    }

    private void BeginTalkPose(float playbackDuration)
    {
        float totalDuration = Mathf.Max(0.001f, playbackDuration);
        float blendInFraction = Mathf.Clamp(
            talkPoseBlendInFraction,
            0.01f,
            0.5f);
        float holdFraction = Mathf.Clamp(
            talkPoseHoldFraction,
            0f,
            0.95f - blendInFraction);
        talkPoseStartTime = Time.time;
        talkPoseBlendInEndTime = talkPoseStartTime
            + totalDuration * blendInFraction;
        talkPoseSustainEndTime = talkPoseBlendInEndTime
            + totalDuration * holdFraction;
        // The release receives the remaining duration, so the complete pose
        // envelope always ends with the pitch-adjusted audio clip.
        talkPoseEndTime = talkPoseStartTime + totalDuration;
        talkPoseActive = true;
    }

    private void UpdateTalkPose()
    {
        if (!talkPoseActive)
        {
            return;
        }

        if (!penVisualsEnabled || usingFarImpostor || isMachineControlled)
        {
            talkPoseActive = false;
            SetTalkPoseWeight(0f);
            return;
        }

        float now = Time.time;
        float weight;
        if (now < talkPoseBlendInEndTime)
        {
            float blend = Mathf.InverseLerp(
                talkPoseStartTime,
                talkPoseBlendInEndTime,
                now);
            // Ease out of neutral quickly so even the shortest clucks read.
            weight = 1f - Mathf.Pow(1f - blend, 2f);
        }
        else if (now < talkPoseSustainEndTime)
        {
            weight = 1f;
        }
        else
        {
            float blend = Mathf.InverseLerp(
                talkPoseSustainEndTime,
                talkPoseEndTime,
                now);
            // Drop most of the pose early, leaving a soft tail for the rest of
            // the sound instead of holding the beak fully open.
            weight = Mathf.Pow(1f - blend, 2f);
        }

        if (now >= talkPoseEndTime)
        {
            weight = 0f;
            talkPoseActive = false;
        }

        SetTalkPoseWeight(weight);
    }

    private static void RunAiScheduler(ChickenController schedulerSource)
    {
        if (aiSchedulerFrame == Time.frameCount
            || schedulerSource == null)
        {
            return;
        }

        aiSchedulerFrame = Time.frameCount;
        float now = Time.time;
        if (now < nextAiSchedulerTime)
        {
            return;
        }

        float schedulerInterval =
            1f / Mathf.Max(1f, schedulerSource.aiSchedulerUpdateRateHz);
        nextAiSchedulerTime = now + schedulerInterval;
        int count = ActiveChickens.Count;
        if (count <= 0)
        {
            aiSchedulerCursor = 0;
            return;
        }

        int budget = Mathf.Min(
            count,
            Mathf.Max(1, schedulerSource.maximumAiUpdatesPerTick));
        for (int processed = 0; processed < budget; processed++)
        {
            if (ActiveChickens.Count == 0)
            {
                aiSchedulerCursor = 0;
                break;
            }

            aiSchedulerCursor %= ActiveChickens.Count;
            ChickenController chicken = ActiveChickens[aiSchedulerCursor];
            aiSchedulerCursor++;
            chicken?.TryRunScheduledAi(now);
        }
    }

    private void TryRunScheduledAi(float now)
    {
        if (!isActiveAndEnabled || isMachineControlled)
        {
            return;
        }

        float simulationDeltaTime = now - lastAiUpdateTime;
        lastAiUpdateTime = now;
        RefreshChickenSeparationTarget();
        UpdateFoodAndEggTimers(simulationDeltaTime);

        if (penVisualsEnabled && !usingFarImpostor)
        {
            UpdateBlink();
            UpdateWingFlutter();
        }

        if (isTraversingIncubatorExit)
        {
            return;
        }

        if (!navigationReady)
        {
            TryInitializeNavigation();
            return;
        }

        if (eggTimerRemaining <= 0f
            && state != ChickenState.Eating
            && state != ChickenState.EggLaying
            && state != ChickenState.LeavingIncubator)
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
            case ChickenState.LeavingIncubator:
                UpdateIncubatorExit();
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

    internal static void TickScheduledPhysics()
    {
        ChickenController schedulerSource = GetFirstActiveChicken();
        if (schedulerSource != null)
        {
            RunEggPushScheduler(schedulerSource);
        }
    }

    private static void RunEggPushScheduler(ChickenController schedulerSource)
    {
        int physicsStep = Mathf.RoundToInt(
            Time.fixedTime / Mathf.Max(0.0001f, Time.fixedDeltaTime));
        if (eggPushSchedulerStep == physicsStep
            || schedulerSource == null)
        {
            return;
        }

        eggPushSchedulerStep = physicsStep;
        int count = ActiveChickens.Count;
        if (count <= 0)
        {
            eggPushSchedulerCursor = 0;
            return;
        }

        int budget = Mathf.Min(
            count,
            Mathf.Max(
                1,
                schedulerSource.maximumEggPushChecksPerFixedUpdate));
        for (int processed = 0; processed < budget; processed++)
        {
            if (ActiveChickens.Count == 0)
            {
                eggPushSchedulerCursor = 0;
                break;
            }

            eggPushSchedulerCursor %= ActiveChickens.Count;
            ChickenController chicken =
                ActiveChickens[eggPushSchedulerCursor];
            eggPushSchedulerCursor++;
            if (chicken != null
                && chicken.isActiveAndEnabled
                && !chicken.isMachineControlled
                && chicken.penVisualsEnabled
                && !chicken.usingFarImpostor)
            {
                chicken.PushNearbyEggs();
            }
        }
    }

    internal static void TickScheduledLateUpdates()
    {
        for (int index = 0; index < ActiveChickens.Count; index++)
        {
            ChickenController chicken = ActiveChickens[index];
            if (chicken != null
                && chicken.isActiveAndEnabled
                && chicken.penVisualsEnabled
                && !chicken.usingFarImpostor
                && chicken.state == ChickenState.EggLaying
                && !chicken.eggSpawnedDuringLay)
            {
                chicken.TrySpawnEggAtAnimationFrame();
            }
        }
    }

    internal static void NotifySchedulerDestroyed(
        ChickenUpdateScheduler driver)
    {
        if (schedulerDriver == driver)
        {
            schedulerDriver = null;
        }
    }

    private static ChickenController GetFirstActiveChicken()
    {
        for (int index = 0; index < ActiveChickens.Count; index++)
        {
            ChickenController chicken = ActiveChickens[index];
            if (chicken != null && chicken.isActiveAndEnabled)
            {
                return chicken;
            }
        }

        return null;
    }

    private static void EnsureSchedulerDriver()
    {
        if (schedulerDriver != null)
        {
            return;
        }

        GameObject schedulerObject = new GameObject("Chicken Update Scheduler");
        schedulerDriver =
            schedulerObject.AddComponent<ChickenUpdateScheduler>();
    }

    private void UpdateFoodAndEggTimers(float deltaTime)
    {
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsRoundInProgress)
        {
            return;
        }

        foodScore = Mathf.Max(
            0f,
            foodScore - foodScoreDrainPerSecond * deltaTime);

        if (foodScore <= 0f)
        {
            activeFoodProductionSpeed = 1f;
            activeFoodPremiumChanceMultiplier = 1f;
        }

        float eggIntervalMultiplier = Mathf.Lerp(
            emptyFoodEggIntervalMultiplier,
            fullFoodEggIntervalMultiplier,
            FoodScoreNormalized);
        eggTimerRemaining -= deltaTime
            * activeFoodProductionSpeed
            * (RoundSystem.Instance != null
                ? RoundSystem.Instance.StartupProductionMultiplier
                : 1f)
            / Mathf.Max(0.01f, eggIntervalMultiplier);
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

    private void UpdateIncubatorExit()
    {
        Vector3 offset = incubatorExitDestination - transform.position;
        Vector3 planarDirection = offset;
        planarDirection.y = 0f;

        if (planarDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(planarDirection);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                angularSpeed * Time.deltaTime);
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            incubatorExitDestination,
            moveSpeed * Time.deltaTime);

        if ((transform.position - incubatorExitDestination).sqrMagnitude <= 0.0001f
            || Time.time >= stateEndTime)
        {
            FinishIncubatorExit();
        }
    }

    public void BeginIncubatorExit(Vector3 destination)
    {
        incubatorExitDestination = destination;
        hasIncubatorExitDestination = true;

        if (navigationReady)
        {
            TryBeginIncubatorExit();
        }
    }

    public void ConfigureBreed(ChickenBreed newBreed)
    {
        breed = newBreed;
        ApplyBreedVisual();
        ApplyFarImpostorTint();
        gameObject.name = newBreed == ChickenBreed.White
            ? "prefab_chicken"
            : $"chicken_{newBreed.ToString().ToLowerInvariant()}";
    }

    public void SetMachineControlled(bool controlled)
    {
        if (isMachineControlled == controlled)
        {
            return;
        }

        isMachineControlled = controlled;
        targetFood = null;
        targetSeparationVelocity = Vector3.zero;
        cachedSeparation = Vector3.zero;
        SetEatingAnimation(false);

        if (controlled)
        {
            stateBeforeMachineControl = state;
            stateTimeBeforeMachineControl =
                Mathf.Max(0f, stateEndTime - Time.time);
            eggTimerBeforeMachineControl = eggTimerRemaining;
            eggSpawnedBeforeMachineControl = eggSpawnedDuringLay;
            hasMachineControlSnapshot = true;

            if (agent != null && agent.enabled)
            {
                if (agent.isOnNavMesh)
                {
                    agent.ResetPath();
                }

                agent.enabled = false;
            }

            navigationReady = false;
            navigationRetryStartedAt = Time.time;
            state = ChickenState.Idle;
            return;
        }

        if (agent != null && !agent.enabled)
        {
            agent.enabled = true;
        }

        if (hasMachineControlSnapshot)
        {
            eggTimerRemaining = eggTimerBeforeMachineControl;
        }

        TryInitializeNavigation();

        if (hasMachineControlSnapshot
            && stateBeforeMachineControl == ChickenState.EggLaying)
        {
            state = ChickenState.EggLaying;
            stateEndTime = Time.time + stateTimeBeforeMachineControl;
            eggSpawnedDuringLay = eggSpawnedBeforeMachineControl;

            if (!eggSpawnedDuringLay
                && animator != null
                && animator.runtimeAnimatorController != null)
            {
                animator.ResetTrigger(LayEggParameter);
                animator.SetTrigger(LayEggParameter);
            }
        }
        else
        {
            BeginIdle();
        }

        hasMachineControlSnapshot = false;
    }

    public void SetHeldByHand(bool held)
    {
        if (isHeldByHand == held)
        {
            return;
        }

        isHeldByHand = held;
        ApplyHeldBlendShape(held);
        if (held)
        {
            secondaryMotionWakePending = false;
        }
        UpdateSecondaryMotionLod(true);

        if (held)
        {
            heldBaseRotation = transform.rotation;
            heldDragAngles = Vector3.zero;
            heldDragAngularVelocity = Vector3.zero;
            hasPreviousHeldAttachPosition = false;
        }
        else
        {
            bool hadAttachPosition =
                hasPreviousHeldAttachPosition;
            Vector3 releaseAttachPosition =
                previousHeldAttachPosition;
            transform.rotation = heldBaseRotation;

            if (hadAttachPosition)
            {
                AlignHeldBoneTo(releaseAttachPosition);
            }

            heldDragAngles = Vector3.zero;
            heldDragAngularVelocity = Vector3.zero;
            hasPreviousHeldAttachPosition = false;
        }

        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        if (held)
        {
            SetWingFlutterWeight(0f);

            if (animator.HasState(0, HeldState))
            {
                animator.CrossFadeInFixedTime(
                    HeldState,
                    heldAnimationTransitionDuration,
                    0,
                    0f);
            }

            return;
        }

        if (animator.HasState(0, IdleState))
        {
            animator.CrossFadeInFixedTime(
                IdleState,
                heldAnimationTransitionDuration,
                0,
                0f);
        }
    }

    private void CacheChickenBlendShapes()
    {
        CacheBlendShape(
            heldBlendShapeName,
            heldBlendShapeRenderers,
            heldBlendShapeIndices);
        CacheBlendShape(
            fatBlendShapeName,
            fatBlendShapeRenderers,
            fatBlendShapeIndices);
    }

    private void CacheBlendShape(
        string blendShapeName,
        List<SkinnedMeshRenderer> renderersWithShape,
        List<int> shapeIndices)
    {
        renderersWithShape.Clear();
        shapeIndices.Clear();

        if (string.IsNullOrWhiteSpace(blendShapeName))
        {
            return;
        }

        SkinnedMeshRenderer[] renderers =
            GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int rendererIndex = 0;
            rendererIndex < renderers.Length;
            rendererIndex++)
        {
            SkinnedMeshRenderer skinnedRenderer = renderers[rendererIndex];
            Mesh mesh = skinnedRenderer.sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            for (int shapeIndex = 0;
                shapeIndex < mesh.blendShapeCount;
                shapeIndex++)
            {
                string shapeName = mesh.GetBlendShapeName(shapeIndex);
                bool matches = string.Equals(
                    shapeName,
                    blendShapeName,
                    System.StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    matches = shapeName.EndsWith(
                        "." + blendShapeName,
                        System.StringComparison.OrdinalIgnoreCase);
                }

                if (!matches)
                {
                    continue;
                }

                renderersWithShape.Add(skinnedRenderer);
                shapeIndices.Add(shapeIndex);
                break;
            }
        }
    }

    private void ApplyHeldBlendShape(bool held)
    {
        float weight = held ? heldBlendShapeWeight : 0f;
        int entryCount = Mathf.Min(
            heldBlendShapeRenderers.Count,
            heldBlendShapeIndices.Count);
        for (int index = 0; index < entryCount; index++)
        {
            SkinnedMeshRenderer skinnedRenderer =
                heldBlendShapeRenderers[index];
            if (skinnedRenderer == null
                || skinnedRenderer.sharedMesh == null)
            {
                continue;
            }

            int shapeIndex = heldBlendShapeIndices[index];
            if (shapeIndex < 0
                || shapeIndex >= skinnedRenderer.sharedMesh.blendShapeCount)
            {
                continue;
            }

            skinnedRenderer.SetBlendShapeWeight(shapeIndex, weight);
        }
    }

    private void UpdateFatBlendShape()
    {
        float targetValue = CalculateTargetFatBlendValue();
        fatBlendValue = Mathf.SmoothDamp(
            fatBlendValue,
            targetValue,
            ref fatBlendVelocity,
            fatBlendSmoothTime,
            Mathf.Infinity,
            Time.deltaTime);

        if (Mathf.Abs(fatBlendValue - targetValue) < 0.0001f)
        {
            fatBlendValue = targetValue;
            fatBlendVelocity = 0f;
        }

        ApplyFatBlendShape(false);
    }

    private float CalculateTargetFatBlendValue()
    {
        float baselineFood = Mathf.Clamp(
            startingFoodScore,
            0f,
            maximumFoodScore);
        if (foodScore <= baselineFood)
        {
            float depletedFood = baselineFood > 0.0001f
                ? foodScore / baselineFood
                : 0f;
            return Mathf.Lerp(minimumFat, 0f, Mathf.Clamp01(depletedFood));
        }

        float fullyFedFood = Mathf.Clamp(
            returnToWanderingScore,
            baselineFood + 0.0001f,
            maximumFoodScore);
        float fedAmount = Mathf.InverseLerp(
            baselineFood,
            fullyFedFood,
            foodScore);
        return Mathf.Lerp(0f, maximumFat, fedAmount);
    }

    private void ApplyFatBlendShape(bool force)
    {
        if (!force
            && Mathf.Abs(fatBlendValue - lastAppliedFatBlendValue) < 0.001f)
        {
            return;
        }

        // Unity blendshape weights use percentage units. Inspector-facing fat
        // values remain the more useful authored range of -0.2 to 1.0.
        float weight = fatBlendValue * 100f;
        int entryCount = Mathf.Min(
            fatBlendShapeRenderers.Count,
            fatBlendShapeIndices.Count);
        for (int index = 0; index < entryCount; index++)
        {
            SkinnedMeshRenderer skinnedRenderer =
                fatBlendShapeRenderers[index];
            if (skinnedRenderer == null
                || skinnedRenderer.sharedMesh == null)
            {
                continue;
            }

            int shapeIndex = fatBlendShapeIndices[index];
            if (shapeIndex < 0
                || shapeIndex >= skinnedRenderer.sharedMesh.blendShapeCount)
            {
                continue;
            }

            skinnedRenderer.SetBlendShapeWeight(shapeIndex, weight);
        }

        lastAppliedFatBlendValue = fatBlendValue;
    }

    private void StepHeldDragSpring(
        Vector3 desiredAngles,
        float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        if (!IsFinite(desiredAngles)
            || !IsFinite(heldDragAngles)
            || !IsFinite(heldDragAngularVelocity))
        {
            desiredAngles = Vector3.zero;
            heldDragAngles = Vector3.zero;
            heldDragAngularVelocity = Vector3.zero;
        }

        const float Tau = Mathf.PI * 2f;
        float angularFrequency =
            Tau * heldDragSpringFrequency;
        float frequencySquared =
            angularFrequency * angularFrequency;
        float dampingTerm = 1f
            + 2f
            * deltaTime
            * heldDragSpringDamping
            * angularFrequency;
        float velocityToPosition =
            deltaTime * frequencySquared;
        float positionToPosition =
            deltaTime * velocityToPosition;
        float inverseDeterminant = 1f
            / (dampingTerm + positionToPosition);
        Vector3 previousAngles = heldDragAngles;
        Vector3 previousVelocity = heldDragAngularVelocity;
        heldDragAngles = (
            previousAngles * dampingTerm
            + previousVelocity * deltaTime
            + desiredAngles * positionToPosition)
            * inverseDeterminant;
        heldDragAngularVelocity = (
            previousVelocity
            + (desiredAngles - previousAngles)
            * velocityToPosition)
            * inverseDeterminant;
        heldDragAngles = Vector3.ClampMagnitude(
            heldDragAngles,
            heldDragMaximumAngle);
        heldDragAngularVelocity = Vector3.ClampMagnitude(
            heldDragAngularVelocity,
            heldDragMaximumAngle
            * angularFrequency
            * 2f);
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x)
            && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y)
            && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z)
            && !float.IsInfinity(value.z);
    }

    private bool TryBeginIncubatorExit()
    {
        if (!hasIncubatorExitDestination)
        {
            return false;
        }

        if (isTraversingIncubatorExit)
        {
            return true;
        }

        if (agent.enabled)
        {
            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
            }

            agent.enabled = false;
        }

        navigationReady = false;
        navigationRetryStartedAt = Time.time;
        isTraversingIncubatorExit = true;
        state = ChickenState.LeavingIncubator;
        stateEndTime = Time.time
            + Mathf.Max(
                2f,
                Vector3.Distance(transform.position, incubatorExitDestination) / moveSpeed + 2f);
        return true;
    }

    private void FinishIncubatorExit()
    {
        transform.position = incubatorExitDestination;
        hasIncubatorExitDestination = false;
        isTraversingIncubatorExit = false;

        if (!agent.enabled)
        {
            agent.enabled = true;
        }

        TryInitializeNavigation();
        BeginIdle();
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
        float consumedFoodProductionSpeed =
            targetFood.EggProductionSpeedMultiplier;
        float consumedFoodPremiumChance =
            targetFood.PremiumChanceMultiplier;
        float amountConsumed = targetFood.Consume(amountRequested);

        if (amountConsumed <= 0f)
        {
            FinishEating();
            return;
        }

        foodScore = Mathf.Min(maximumFoodScore, foodScore + amountConsumed);
        activeFoodProductionSpeed = Mathf.Max(
            activeFoodProductionSpeed,
            consumedFoodProductionSpeed);
        activeFoodPremiumChanceMultiplier = Mathf.Max(
            activeFoodPremiumChanceMultiplier,
            consumedFoodPremiumChance);

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
        Vector3 chickenPosition = transform.position;

        foreach (FoodPile foodPile in FoodPile.ActivePiles)
        {
            if (foodPile == null || !foodPile.IsAvailable)
            {
                continue;
            }

            Vector3 offset = foodPile.transform.position - chickenPosition;
            float distanceSquared = offset.x * offset.x + offset.z * offset.z;
            float attractionRadius = foodSearchRadius
                + foodPile.AttractionRadiusBonus;
            float attractionRadiusSquared = attractionRadius * attractionRadius;

            if (distanceSquared > attractionRadiusSquared
                || distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestFood = foodPile;
            bestDistanceSquared = distanceSquared;
        }

        // The chicken pen is a single connected static NavMesh. Select first,
        // then validate one path instead of calculating a path for every pile
        // and calculating the winning path a second time.
        if (bestFood == null
            || !TryCalculateCompletePath(bestFood.transform.position))
        {
            return false;
        }

        targetFood = bestFood;
        agent.stoppingDistance = eatingDistance * 0.75f;
        agent.updateRotation = true;
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
        agent.updateRotation = false;

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
        agent.updateRotation = false;

        if (penVisualsEnabled
            && animator != null
            && animator.isActiveAndEnabled
            && animator.runtimeAnimatorController != null)
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

        if (!hasWarnedAboutMissingNavMesh
            && Time.time - navigationRetryStartedAt >= MissingNavMeshWarningDelay)
        {
            Debug.LogWarning(
                "A chicken still could not find a NavMesh after retrying for "
                + $"{MissingNavMeshWarningDelay:0.#} seconds. Ensure its pen "
                + "NavMesh covers the chicken's position.",
                this);
            hasWarnedAboutMissingNavMesh = true;
        }
    }

    private void BeginIdle()
    {
        state = ChickenState.Idle;
        stateEndTime = Time.time + Random.Range(minIdleTime, maxIdleTime);
        agent.stoppingDistance = 0.03f;
        agent.updateRotation = false;
        SetEatingAnimation(false);

        if (navigationReady && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }
    }

    private void ChooseDestination()
    {
        bool foundDestination = false;
        Vector3 bestDestination = transform.position;
        float bestCrowding = float.PositiveInfinity;
        float bestTravelDistanceSquared = 0f;
        int sampledCandidates = 0;
        int candidatesToCompare = Mathf.Min(
            destinationAttempts,
            wanderDestinationCandidates);

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

            float crowding = CalculateDestinationCrowding(hit.position);
            Vector3 travelOffset = hit.position - transform.position;
            float travelDistanceSquared =
                travelOffset.x * travelOffset.x + travelOffset.z * travelOffset.z;

            if (!foundDestination
                || crowding < bestCrowding
                || (Mathf.Approximately(crowding, bestCrowding)
                    && travelDistanceSquared > bestTravelDistanceSquared))
            {
                foundDestination = true;
                bestDestination = hit.position;
                bestCrowding = crowding;
                bestTravelDistanceSquared = travelDistanceSquared;
            }

            sampledCandidates++;
            if (sampledCandidates >= candidatesToCompare)
            {
                break;
            }
        }

        if (!foundDestination
            || !NavMesh.CalculatePath(
                transform.position,
                bestDestination,
                navMeshQueryFilter,
                path)
            || path.status != NavMeshPathStatus.PathComplete)
        {
            BeginIdle();
            return;
        }

        agent.stoppingDistance = 0.03f;
        agent.updateRotation = true;
        agent.SetPath(path);
        state = ChickenState.Moving;
        stateEndTime = Time.time + CalculateMovementTimeout(path);
    }

    private float CalculateDestinationCrowding(Vector3 destination)
    {
        float radiusSquared = wanderCrowdRadius * wanderCrowdRadius;
        int chickenCount = ActiveChickens.Count;
        int sampleStride = Mathf.Max(
            1,
            Mathf.CeilToInt(chickenCount / (float)MaximumWanderCrowdSamples));
        int sampleOffset = separationUpdateOffset % sampleStride;
        float crowding = 0f;

        for (int i = sampleOffset; i < chickenCount; i += sampleStride)
        {
            ChickenController other = ActiveChickens[i];
            if (other == null || other == this)
            {
                continue;
            }

            Vector3 offset = other.transform.position - destination;
            float distanceSquared = offset.x * offset.x + offset.z * offset.z;
            if (distanceSquared >= radiusSquared)
            {
                continue;
            }

            crowding += 1f - distanceSquared / radiusSquared;
        }

        return crowding;
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

    private void RefreshChickenSeparationTarget()
    {
        if (separationRadius <= 0f
            || separationStrength <= 0f
            || isMachineControlled
            || isHeldByHand
            || isTraversingIncubatorExit
            || !penVisualsEnabled
            || usingFarImpostor
            || agent == null
            || !agent.enabled
            || !agent.isOnNavMesh)
        {
            targetSeparationVelocity = Vector3.zero;
            cachedSeparation = Vector3.zero;
            return;
        }

        Vector3 separation = Vector3.zero;
        float activeRadius = Mathf.Max(
            0.001f,
            separationRadius - separationSettleMargin);
        float radiusSquared = activeRadius * activeRadius;
        int chickenCount = ActiveChickens.Count;
        int sampleStride = Mathf.Max(
            1,
            Mathf.CeilToInt(
                chickenCount / (float)MaximumSeparationSamples));
        int sampleOffset =
            (separationUpdateOffset + separationSamplePhase)
            % sampleStride;
        separationSamplePhase++;

        for (int index = sampleOffset;
             index < chickenCount;
             index += sampleStride)
        {
            ChickenController other = ActiveChickens[index];
            if (other == null
                || other == this
                || other.isMachineControlled)
            {
                continue;
            }

            Vector3 offset =
                transform.position - other.transform.position;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;

            if (distanceSquared >= radiusSquared)
            {
                continue;
            }

            if (distanceSquared < 0.000001f)
            {
                offset = GetInstanceID() < other.GetInstanceID()
                    ? Vector3.left
                    : Vector3.right;
                distanceSquared = 0.000001f;
            }

            float distance = Mathf.Sqrt(distanceSquared);
            float penetration = 1f - distance / activeRadius;
            separation += offset / distance
                * (penetration * penetration);
        }

        if (separation.sqrMagnitude > 1f)
        {
            separation.Normalize();
        }

        bool isActivelyTravelling = agent.hasPath
            && agent.desiredVelocity.sqrMagnitude > 0.0025f;
        float stateStrength = isActivelyTravelling
            ? 1f
            : idleSeparationStrengthMultiplier;
        targetSeparationVelocity =
            separation * (separationStrength * stateStrength);
    }

    private void ApplyChickenSeparation(float deltaTime)
    {
        if (separationRadius <= 0f
            || separationStrength <= 0f
            || isMachineControlled
            || isHeldByHand
            || isTraversingIncubatorExit
            || !penVisualsEnabled
            || usingFarImpostor
            || agent == null
            || !agent.enabled
            || !agent.isOnNavMesh)
        {
            targetSeparationVelocity = Vector3.zero;
            cachedSeparation = Vector3.zero;
            return;
        }

        // Cap an unusually long frame so separation cannot teleport a chicken.
        // Normal frames still integrate at their actual duration, which keeps
        // the correction continuous and independent of the AI update rate.
        float safeDeltaTime = Mathf.Min(deltaTime, 0.05f);
        float response = 1f - Mathf.Exp(
            -Mathf.Max(0.01f, separationResponseSpeed)
            * safeDeltaTime);
        cachedSeparation = Vector3.Lerp(
            cachedSeparation,
            targetSeparationVelocity,
            response);

        float stopSpeed = Mathf.Max(0f, separationStopSpeed);
        if (targetSeparationVelocity.sqrMagnitude
                <= stopSpeed * stopSpeed
            && cachedSeparation.sqrMagnitude
                <= stopSpeed * stopSpeed)
        {
            cachedSeparation = Vector3.zero;
            return;
        }

        agent.Move(cachedSeparation * safeDeltaTime);
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
            eggCollisionMask,
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
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsRoundInProgress)
        {
            return;
        }

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
        ProgressionSystem progression = ProgressionSystem.Instance;
        ChickenEgg.EggType eggType = progression != null
            ? progression.RollEggType(
                breed,
                activeFoodPremiumChanceMultiplier)
            : ChickenEgg.EggType.Common;
        GameObject selectedEggPrefab =
            eggType == ChickenEgg.EggType.Cosmic && cosmicEggPrefab != null
                ? cosmicEggPrefab
                : eggPrefab;
        ChickenEgg chickenEgg = ChickenEgg.Spawn(
            selectedEggPrefab,
            eggPosition,
            eggRotation);
        GameObject egg = chickenEgg.gameObject;
        PlayRandomVoice(
            layEggAudioSource,
            layEggSounds,
            ref lastLayEggClipIndex,
            layEggVolume,
            minLayEggVolumeMultiplier,
            maxLayEggVolumeMultiplier);
        EggLaid?.Invoke();

        int eggValue = progression != null
            ? progression.GetEggValueCents(eggType)
            : 100;
        chickenEgg.ConfigureType(eggType, eggValue);

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

    private Transform FindHeldAttachBone()
    {
        if (animator == null
            || string.IsNullOrWhiteSpace(heldAttachBoneName))
        {
            return null;
        }

        Transform[] bones =
            animator.GetComponentsInChildren<Transform>(true);

        for (int index = 0; index < bones.Length; index++)
        {
            if (string.Equals(
                    bones[index].name,
                    heldAttachBoneName,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return bones[index];
            }
        }

        string blenderAxisPrefix = heldAttachBoneName + ".";

        for (int index = 0; index < bones.Length; index++)
        {
            if (bones[index].name.StartsWith(
                    blenderAxisPrefix,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return bones[index];
            }
        }

        Debug.LogWarning(
            $"{nameof(ChickenController)} could not find held attachment bone '{heldAttachBoneName}' below '{animator.name}'. The chicken root will be used instead.",
            this);
        return null;
    }

    private void ScheduleNextEgg()
    {
        eggTimerRemaining = Random.Range(minEggLayTime, maxEggLayTime);
    }

    private void ScheduleInitialEgg()
    {
        eggTimerRemaining = Random.Range(minInitialEggLayTime, maxInitialEggLayTime);
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

    private void CacheTalkLayer()
    {
        talkLayerIndex = animator != null && animator.runtimeAnimatorController != null
            ? animator.GetLayerIndex(TalkLayerName)
            : -1;
    }

    private void SetTalkPoseWeight(float weight)
    {
        if (animator != null && talkLayerIndex >= 0)
        {
            animator.SetLayerWeight(talkLayerIndex, Mathf.Clamp01(weight));
        }
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

    private void CacheSecondaryMotionLod()
    {
        var controlledComponents = new List<Behaviour>();
        secondaryMotionJiggleRigs =
            GetComponentsInChildren<JiggleRig>(true);
        controlledComponents.AddRange(secondaryMotionJiggleRigs);
        controlledComponents.AddRange(
            GetComponentsInChildren<ChickenWattlePendulum>(true));
        controlledComponents.AddRange(
            GetComponentsInChildren<ChickenTailFlutter>(true));
        controlledComponents.AddRange(
            GetComponentsInChildren<ChickenWindResponse>(true));
        lodControlledSecondaryMotion = controlledComponents.ToArray();
        lodControlledSecondaryMotionDefaults =
            new bool[lodControlledSecondaryMotion.Length];

        for (int index = 0;
             index < lodControlledSecondaryMotion.Length;
             index++)
        {
            Behaviour component = lodControlledSecondaryMotion[index];
            lodControlledSecondaryMotionDefaults[index] =
                component != null && component.enabled;
        }

        secondaryMotionMeshLodMesh = GetRendererMesh(
            secondaryMotionMeshLodRenderer);
        if (secondaryMotionMeshLodMesh == null
            || secondaryMotionMeshLodMesh.lodCount < 2)
        {
            secondaryMotionMeshLodRenderer =
                FindGeneratedMeshLodRenderer();
            secondaryMotionMeshLodMesh = GetRendererMesh(
                secondaryMotionMeshLodRenderer);
        }

        if (secondaryMotionMeshLodMesh != null
            && secondaryMotionMeshLodMesh.lodCount >= 2)
        {
            secondaryMotionLodAvailable = true;
        }
        else
        {
            if (secondaryMotionLodGroup == null)
            {
                secondaryMotionLodGroup =
                    GetComponentInChildren<LODGroup>(true);
            }

            if (secondaryMotionLodGroup != null)
            {
                LOD[] lods = secondaryMotionLodGroup.GetLODs();
                if (lods.Length >= 2)
                {
                    int transitionIndex = Mathf.Clamp(
                        lastSecondaryMotionLod,
                        0,
                        lods.Length - 2);
                    secondaryMotionTransitionHeight =
                        lods[transitionIndex]
                            .screenRelativeTransitionHeight;
                    secondaryMotionLodAvailable = true;
                }
            }
        }

        int interval = Mathf.Max(
            1,
            secondaryMotionLodCheckIntervalFrames);
        nextSecondaryMotionLodCheckFrame =
            Time.frameCount + Mathf.Abs(GetInstanceID()) % interval;
    }

    private void UpdateSecondaryMotionLod(bool force)
    {
        if (!penVisualsEnabled)
        {
            return;
        }

        if (secondaryMotionWakePending)
        {
            if (!isHeldByHand && Time.time < secondaryMotionWakeTime)
            {
                return;
            }

            secondaryMotionWakePending = false;
            force = true;
        }

        if (lodControlledSecondaryMotion.Length == 0
            || !secondaryMotionLodAvailable)
        {
            return;
        }

        if (!force
            && Time.frameCount < nextSecondaryMotionLodCheckFrame)
        {
            return;
        }

        int interval = Mathf.Max(
            1,
            secondaryMotionLodCheckIntervalFrames);
        nextSecondaryMotionLodCheckFrame =
            Time.frameCount + interval;
        bool insideDetailedLod = IsInsideSecondaryMotionLod();
        bool shouldSimulate = isHeldByHand || insideDetailedLod;
        SetSecondaryMotionEnabled(shouldSimulate);
    }

    private void BeginSecondaryMotionWake()
    {
        if (lodControlledSecondaryMotion.Length == 0)
        {
            return;
        }

        SetSecondaryMotionEnabled(false);
        secondaryMotionInfluenceRampActive = false;
        ApplySecondaryMotionInfluence(0f);
        secondaryMotionWakePending = true;
        secondaryMotionWakeTime = Time.time + Random.Range(
            minimumSecondaryMotionWakeDelay,
            maximumSecondaryMotionWakeDelay);
        nextSecondaryMotionLodCheckFrame = Time.frameCount;
    }

    private bool IsInsideSecondaryMotionLod()
    {
        if (secondaryMotionCamera == null
            || !secondaryMotionCamera.isActiveAndEnabled)
        {
            secondaryMotionCamera = Camera.main;
        }

        Camera camera = secondaryMotionCamera;
        if (camera == null)
        {
            return true;
        }

        if (secondaryMotionMeshLodMesh != null
            && secondaryMotionMeshLodRenderer != null)
        {
            int activeLod = CalculateActiveMeshLod(
                camera,
                secondaryMotionMeshLodRenderer,
                secondaryMotionMeshLodMesh);
            bool useImpostor = enableFarImpostor
                && !isHeldByHand
                && activeLod >= Mathf.Clamp(
                    farImpostorMeshLod,
                    1,
                    secondaryMotionMeshLodMesh.lodCount - 1);
            SetFarImpostorActive(useImpostor, false);
            if (useImpostor)
            {
                FaceImpostorTowards(camera);
            }

            return activeLod <= Mathf.Clamp(
                lastSecondaryMotionLod,
                0,
                secondaryMotionMeshLodMesh.lodCount - 1);
        }

        if (secondaryMotionLodGroup == null)
        {
            return true;
        }

        Transform lodTransform = secondaryMotionLodGroup.transform;
        Vector3 referencePoint = lodTransform.TransformPoint(
            secondaryMotionLodGroup.localReferencePoint);
        float maximumScale = Mathf.Max(
            Mathf.Abs(lodTransform.lossyScale.x),
            Mathf.Abs(lodTransform.lossyScale.y),
            Mathf.Abs(lodTransform.lossyScale.z));
        float worldSize =
            secondaryMotionLodGroup.size * maximumScale;
        float relativeHeight;

        if (camera.orthographic)
        {
            relativeHeight = worldSize
                / Mathf.Max(0.0001f, camera.orthographicSize * 2f);
        }
        else
        {
            float forwardDistance = Vector3.Dot(
                referencePoint - camera.transform.position,
                camera.transform.forward);
            if (forwardDistance <= 0f)
            {
                return false;
            }

            float halfVerticalFov =
                camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            relativeHeight = worldSize
                / Mathf.Max(
                    0.0001f,
                    forwardDistance
                    * 2f
                    * Mathf.Tan(halfVerticalFov));
        }

        relativeHeight *= Mathf.Max(
            0.01f,
            QualitySettings.lodBias);
        return relativeHeight >= secondaryMotionTransitionHeight;
    }

    private void CacheFarImpostor()
    {
        if (farImpostorRenderer == null)
        {
            farImpostorRenderer =
                GetComponentInChildren<SpriteRenderer>(true);
        }

        var renderers = new List<Renderer>();
        foreach (Renderer renderer
                 in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != null && renderer != farImpostorRenderer)
            {
                renderers.Add(renderer);
            }
        }

        detailedRenderers = renderers.ToArray();
        detailedRendererDefaults = new bool[detailedRenderers.Length];
        for (int index = 0; index < detailedRenderers.Length; index++)
        {
            detailedRendererDefaults[index] =
                detailedRenderers[index] != null
                && detailedRenderers[index].enabled;
        }

        var behaviours = new List<Behaviour>();
        ChickenLookController lookController =
            GetComponent<ChickenLookController>();
        ChickenFootPlacement footPlacement =
            GetComponent<ChickenFootPlacement>();
        ChickenMotionLean motionLean =
            GetComponent<ChickenMotionLean>();
        if (lookController != null)
        {
            behaviours.Add(lookController);
        }
        if (footPlacement != null)
        {
            behaviours.Add(footPlacement);
        }
        if (motionLean != null)
        {
            behaviours.Add(motionLean);
        }

        // FastIK components live on child bones rather than the controller
        // object. Include every solver so invisible pens do not continue
        // running hundreds of IK LateUpdates per frame.
        behaviours.AddRange(GetComponentsInChildren<FastIKFabric>(true));
        behaviours.AddRange(GetComponentsInChildren<FastIKLook>(true));

        farDisabledBehaviours = behaviours.ToArray();
        farDisabledBehaviourDefaults =
            new bool[farDisabledBehaviours.Length];
        for (int index = 0;
             index < farDisabledBehaviours.Length;
             index++)
        {
            farDisabledBehaviourDefaults[index] =
                farDisabledBehaviours[index] != null
                && farDisabledBehaviours[index].enabled;
        }

        animatorDefaultEnabled = animator == null || animator.enabled;
        if (farImpostorRenderer != null)
        {
            farImpostorRenderer.enabled = false;
            ApplyFarImpostorTint();
        }
    }

    private void SetFarImpostorActive(bool active, bool force)
    {
        if (!penVisualsEnabled)
        {
            return;
        }

        if (!force && usingFarImpostor == active)
        {
            return;
        }

        usingFarImpostor = active;

        if (farImpostorRenderer != null)
        {
            farImpostorRenderer.enabled = active;
        }

        for (int index = 0; index < detailedRenderers.Length; index++)
        {
            Renderer renderer = detailedRenderers[index];
            if (renderer != null)
            {
                renderer.enabled = !active
                    && detailedRendererDefaults[index];
            }
        }

        if (animator != null)
        {
            animator.enabled = !active && animatorDefaultEnabled;
        }

        for (int index = 0;
             index < farDisabledBehaviours.Length;
             index++)
        {
            Behaviour behaviour = farDisabledBehaviours[index];
            if (behaviour != null)
            {
                behaviour.enabled = !active
                    && farDisabledBehaviourDefaults[index];
            }
        }

        if (agent != null)
        {
            agent.obstacleAvoidanceType = active
                ? ObstacleAvoidanceType.NoObstacleAvoidance
                : detailedAvoidanceType;
        }
    }

    private void FaceImpostorTowards(Camera camera)
    {
        if (farImpostorRenderer == null || camera == null)
        {
            return;
        }

        farImpostorRenderer.transform.rotation =
            Quaternion.LookRotation(
                camera.transform.forward,
                camera.transform.up);
    }

    private void ApplyFarImpostorTint()
    {
        if (farImpostorRenderer == null)
        {
            return;
        }

        farImpostorRenderer.color = breed switch
        {
            ChickenBreed.Brown =>
                new Color(0.72f, 0.43f, 0.23f, 1f),
            ChickenBreed.Black =>
                new Color(0.28f, 0.29f, 0.32f, 1f),
            ChickenBreed.Blue =>
                new Color(0.38f, 0.65f, 0.95f, 1f),
            ChickenBreed.Purple =>
                new Color(0.69f, 0.43f, 0.9f, 1f),
            ChickenBreed.Rainbow =>
                new Color(1f, 0.58f, 0.83f, 1f),
            ChickenBreed.Cosmic =>
                new Color(0.43f, 0.25f, 0.7f, 1f),
            _ => Color.white
        };
    }

    private Renderer FindGeneratedMeshLodRenderer()
    {
        Renderer[] renderers =
            GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            Mesh mesh = GetRendererMesh(renderers[index]);
            if (mesh != null && mesh.lodCount >= 2)
            {
                return renderers[index];
            }
        }

        return null;
    }

    private static Mesh GetRendererMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinnedRenderer)
        {
            return skinnedRenderer.sharedMesh;
        }

        if (renderer != null
            && renderer.TryGetComponent(out MeshFilter meshFilter))
        {
            return meshFilter.sharedMesh;
        }

        return null;
    }

    private static int CalculateActiveMeshLod(
        Camera camera,
        Renderer renderer,
        Mesh mesh)
    {
        if (renderer.forceMeshLod >= 0)
        {
            return Mathf.Clamp(
                renderer.forceMeshLod,
                0,
                mesh.lodCount - 1);
        }

        Bounds bounds = renderer.bounds;
        float radiusSquared = Mathf.Max(
            bounds.extents.sqrMagnitude,
            0.00001f);
        float diameterSquared = radiusSquared * 4f;
        float screenMetric = camera.orthographic
            ? camera.orthographicSize * 2f
            : 2f * Mathf.Tan(
                camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float meshLodConstant =
            Mathf.Max(0.00001f, QualitySettings.meshLodThreshold)
            * screenMetric
            / Mathf.Max(1f, camera.pixelHeight);
        float cameraHeightAtDistanceSquared;

        if (camera.orthographic)
        {
            cameraHeightAtDistanceSquared =
                meshLodConstant * meshLodConstant;
        }
        else
        {
            cameraHeightAtDistanceSquared =
                (bounds.center - camera.transform.position).sqrMagnitude
                * meshLodConstant
                * meshLodConstant;
        }

        float desiredPercentage = Mathf.Sqrt(
            cameraHeightAtDistanceSquared / diameterSquared);
        Mesh.LodSelectionCurve curve = mesh.lodSelectionCurve;
        float lodLevel = Mathf.Log(
                Mathf.Max(0.00001f, desiredPercentage),
                2f)
            * curve.lodSlope
            + curve.lodBias;
        lodLevel = Mathf.Max(lodLevel, 0f);
        lodLevel += renderer.meshLodSelectionBias;
        lodLevel = Mathf.Clamp(
            lodLevel,
            0f,
            mesh.lodCount - 1);
        return Mathf.FloorToInt(lodLevel);
    }

    private void SetSecondaryMotionEnabled(bool active)
    {
        if (secondaryMotionActive == active)
        {
            return;
        }

        secondaryMotionActive = active;
        for (int index = 0;
             index < lodControlledSecondaryMotion.Length;
             index++)
        {
            Behaviour component = lodControlledSecondaryMotion[index];
            if (component == null)
            {
                continue;
            }

            bool enabledForLod = active
                && lodControlledSecondaryMotionDefaults[index];
            if (component.enabled != enabledForLod)
            {
                component.enabled = enabledForLod;
            }
        }

        if (active)
        {
            secondaryMotionInfluenceRampStartTime = Time.time;
            secondaryMotionInfluenceRampActive = true;
            ApplySecondaryMotionInfluence(0f);
        }
        else
        {
            secondaryMotionInfluenceRampActive = false;
            ApplySecondaryMotionInfluence(0f);
        }
    }

    private void UpdateSecondaryMotionInfluenceRamp()
    {
        if (!secondaryMotionInfluenceRampActive)
        {
            return;
        }

        float progress = Mathf.Clamp01(
            (Time.time - secondaryMotionInfluenceRampStartTime)
            / Mathf.Max(0.01f, secondaryMotionInfluenceRampDuration));
        float influence = Mathf.SmoothStep(0f, 1f, progress);
        ApplySecondaryMotionInfluence(influence);
        if (progress >= 1f)
        {
            secondaryMotionInfluenceRampActive = false;
        }
    }

    private void ApplySecondaryMotionInfluence(float influence)
    {
        influence = Mathf.Clamp01(influence);
        for (int index = 0;
             index < lodControlledSecondaryMotion.Length;
             index++)
        {
            switch (lodControlledSecondaryMotion[index])
            {
                case ChickenWattlePendulum wattle:
                    wattle.SetRuntimeInfluence(influence);
                    break;
                case ChickenTailFlutter tail:
                    tail.SetRuntimeInfluence(influence);
                    break;
                case ChickenWindResponse wind:
                    wind.SetRuntimeInfluence(influence);
                    break;
            }
        }

        if (JiggleRigSegmentField == null)
        {
            return;
        }

        for (int index = 0;
             index < secondaryMotionJiggleRigs.Length;
             index++)
        {
            JiggleRig rig = secondaryMotionJiggleRigs[index];
            if (rig == null)
            {
                continue;
            }

            JiggleTreeSegment segment =
                JiggleRigSegmentField.GetValue(rig) as JiggleTreeSegment;
            while (segment != null && segment.parent != null)
            {
                segment = segment.parent;
            }

            JiggleTree tree = segment?.jiggleTree;
            if (tree?.parameters == null)
            {
                continue;
            }

            rampedJiggleParameters.Clear();
            for (int parameterIndex = 0;
                 parameterIndex < tree.parameters.Length;
                 parameterIndex++)
            {
                JigglePointParameters parameters =
                    tree.parameters[parameterIndex];
                parameters.blend = influence;
                rampedJiggleParameters.Add(parameters);
            }

            tree.SetParameters(rampedJiggleParameters);
        }
    }

    private Vector3 GetPlanarForward()
    {
        Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        return planarForward.sqrMagnitude > 0.0001f ? planarForward.normalized : Vector3.forward;
    }

    private void ApplyBreedVisual()
    {
        // The chicken mesh UVs are already authored over the top-left white
        // tile of the 2x4 atlas. Keep their original scale and translate that
        // tile to the selected row/column. White therefore uses the material's
        // untouched identity transform.
        int atlasIndex = Mathf.Clamp((int)breed, 0, 6);
        const float tileWidth = 0.5f;
        const float tileHeight = 0.25f;
        float offsetX = atlasIndex % 2 * tileWidth;
        float offsetY = -(atlasIndex / 2) * tileHeight;
        Vector4 textureTransform = new Vector4(
            1f,
            1f,
            offsetX,
            offsetY);

        breedPropertyBlock ??= new MaterialPropertyBlock();
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer chickenRenderer = renderers[index];
            chickenRenderer.GetPropertyBlock(breedPropertyBlock);
            breedPropertyBlock.SetVector(BaseMapTransform, textureTransform);
            breedPropertyBlock.SetVector(MainTextureTransform, textureTransform);
            chickenRenderer.SetPropertyBlock(breedPropertyBlock);
        }
    }

    private void OnValidate()
    {
        minIdleTime = Mathf.Max(0f, minIdleTime);
        maxIdleTime = Mathf.Max(minIdleTime, maxIdleTime);
        wanderRadius = Mathf.Max(0f, wanderRadius);
        navMeshSampleDistance = Mathf.Max(0.01f, navMeshSampleDistance);
        destinationAttempts = Mathf.Max(1, destinationAttempts);
        wanderDestinationCandidates = Mathf.Clamp(
            wanderDestinationCandidates,
            1,
            destinationAttempts);
        wanderCrowdRadius = Mathf.Max(0.01f, wanderCrowdRadius);
        moveSpeed = Mathf.Max(0.01f, moveSpeed);
        acceleration = Mathf.Max(0.01f, acceleration);
        angularSpeed = Mathf.Max(0f, angularSpeed);
        minBlinkInterval = Mathf.Max(0.01f, minBlinkInterval);
        maxBlinkInterval = Mathf.Max(minBlinkInterval, maxBlinkInterval);
        blinkSpeedVariation = Mathf.Clamp(blinkSpeedVariation, 0f, 0.5f);
        minMoodInterval = Mathf.Max(0.1f, minMoodInterval);
        maxMoodInterval = Mathf.Max(minMoodInterval, maxMoodInterval);
        minMoodDuration = Mathf.Max(0.1f, minMoodDuration);
        maxMoodDuration = Mathf.Max(minMoodDuration, maxMoodDuration);
        moodNeighbourRadius = Mathf.Max(0.01f, moodNeighbourRadius);
        comfortableMoodNeighbourCount = Mathf.Max(
            1,
            comfortableMoodNeighbourCount);
        crowdedMoodNeighbourCount = Mathf.Max(
            comfortableMoodNeighbourCount + 1,
            crowdedMoodNeighbourCount);
        angryMoodBlendTime = Mathf.Max(0.01f, angryMoodBlendTime);
        moodBlendTime = Mathf.Max(0.01f, moodBlendTime);
        minTalkInterval = Mathf.Max(0.1f, minTalkInterval);
        maxTalkInterval = Mathf.Max(minTalkInterval, maxTalkInterval);
        talkIntervalScalePerAdditionalChicken = Mathf.Max(
            0f,
            talkIntervalScalePerAdditionalChicken);
        talkCadenceVariation = Mathf.Clamp(talkCadenceVariation, 0f, 0.75f);
        talkPoseBlendInFraction = Mathf.Clamp(
            talkPoseBlendInFraction,
            0.01f,
            0.5f);
        talkPoseHoldFraction = Mathf.Clamp(
            talkPoseHoldFraction,
            0f,
            0.95f - talkPoseBlendInFraction);
        talkVolume = Mathf.Clamp01(talkVolume);
        layEggVolume = Mathf.Clamp01(layEggVolume);
        minLayEggVolumeMultiplier = Mathf.Clamp01(minLayEggVolumeMultiplier);
        maxLayEggVolumeMultiplier = Mathf.Clamp(
            maxLayEggVolumeMultiplier,
            minLayEggVolumeMultiplier,
            1f);
        voicePitchVariation = Mathf.Clamp(voicePitchVariation, 0f, 0.5f);
        voiceVolumeVariation = Mathf.Clamp(voiceVolumeVariation, 0f, 0.5f);
        voiceMinDistance = Mathf.Max(0f, voiceMinDistance);
        voiceMaxDistance = Mathf.Max(
            Mathf.Max(0.01f, voiceMinDistance),
            voiceMaxDistance);
        voiceNearSilentDistance = Mathf.Clamp(
            voiceNearSilentDistance,
            voiceMinDistance,
            voiceMaxDistance);
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
        lastSecondaryMotionLod = Mathf.Max(
            0,
            lastSecondaryMotionLod);
        secondaryMotionLodCheckIntervalFrames = Mathf.Max(
            1,
            secondaryMotionLodCheckIntervalFrames);
        minimumSecondaryMotionWakeDelay = Mathf.Max(
            0f,
            minimumSecondaryMotionWakeDelay);
        maximumSecondaryMotionWakeDelay = Mathf.Max(
            minimumSecondaryMotionWakeDelay,
            maximumSecondaryMotionWakeDelay);
        secondaryMotionInfluenceRampDuration = Mathf.Max(
            0.01f,
            secondaryMotionInfluenceRampDuration);
        maximumAiUpdatesPerTick = Mathf.Max(
            1,
            maximumAiUpdatesPerTick);
        aiSchedulerUpdateRateHz = Mathf.Clamp(
            aiSchedulerUpdateRateHz,
            1f,
            60f);
        farImpostorMeshLod = Mathf.Max(1, farImpostorMeshLod);
        maximumEggPushChecksPerFixedUpdate = Mathf.Max(
            1,
            maximumEggPushChecksPerFixedUpdate);
        heldAnimationTransitionDuration = Mathf.Max(
            0f,
            heldAnimationTransitionDuration);
        heldBlendShapeWeight = Mathf.Clamp(
            heldBlendShapeWeight,
            0f,
            100f);
        heldDragMaximumAngle = Mathf.Clamp(
            heldDragMaximumAngle,
            0f,
            20f);
        heldDragSpeedForMaximumAngle = Mathf.Max(
            0.01f,
            heldDragSpeedForMaximumAngle);
        heldDragSpringFrequency = Mathf.Clamp(
            heldDragSpringFrequency,
            0.1f,
            10f);
        heldDragSpringDamping = Mathf.Clamp(
            heldDragSpringDamping,
            0.05f,
            2f);
        maximumFoodScore = Mathf.Max(0.01f, maximumFoodScore);
        startingFoodScore = Mathf.Clamp(startingFoodScore, 0f, maximumFoodScore);
        foodScoreDrainPerSecond = Mathf.Max(0f, foodScoreDrainPerSecond);
        seekFoodBelowScore = Mathf.Clamp(seekFoodBelowScore, 0f, maximumFoodScore);
        returnToWanderingScore = Mathf.Clamp(
            returnToWanderingScore,
            seekFoodBelowScore,
            maximumFoodScore);
        foodSearchInterval = Mathf.Max(0.01f, foodSearchInterval);
        foodSearchRadius = Mathf.Max(0.01f, foodSearchRadius);
        eatingDistance = Mathf.Max(0.01f, eatingDistance);
        foodPerBite = Mathf.Max(0.01f, foodPerBite);
        secondsPerBite = Mathf.Max(0.01f, secondsPerBite);
        minimumFat = Mathf.Clamp(minimumFat, -1f, 0f);
        maximumFat = Mathf.Clamp01(maximumFat);
        fatBlendSmoothTime = Mathf.Max(0.01f, fatBlendSmoothTime);
        separationRadius = Mathf.Max(0f, separationRadius);
        separationStrength = Mathf.Max(0f, separationStrength);
        separationSettleMargin = Mathf.Clamp(
            separationSettleMargin,
            0f,
            Mathf.Max(0f, separationRadius - 0.001f));
        separationResponseSpeed = Mathf.Max(
            0.01f,
            separationResponseSpeed);
        idleSeparationStrengthMultiplier = Mathf.Clamp01(
            idleSeparationStrengthMultiplier);
        separationStopSpeed = Mathf.Max(0f, separationStopSpeed);
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
        minInitialEggLayTime = Mathf.Max(0f, minInitialEggLayTime);
        maxInitialEggLayTime = Mathf.Max(minInitialEggLayTime, maxInitialEggLayTime);
        emptyFoodEggIntervalMultiplier = Mathf.Max(0.01f, emptyFoodEggIntervalMultiplier);
        fullFoodEggIntervalMultiplier = Mathf.Max(0.01f, fullFoodEggIntervalMultiplier);
        eggLayingDuration = Mathf.Max(0f, eggLayingDuration);
        eggLaunchSpeed = Mathf.Max(0f, eggLaunchSpeed);
        eggLaunchSpeedVariation = Mathf.Clamp01(eggLaunchSpeedVariation);
        eggLaunchSpin = Mathf.Max(0f, eggLaunchSpin);
        eggSpawnHeight = Mathf.Max(0f, eggSpawnHeight);
        eggSpawnBehindDistance = Mathf.Max(0f, eggSpawnBehindDistance);
        ApplyBreedVisual();
    }
}

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class ChickenUpdateScheduler : MonoBehaviour
{
    private void Update()
    {
        ChickenController.TickScheduledUpdates();
    }

    private void FixedUpdate()
    {
        ChickenController.TickScheduledPhysics();
    }

    private void LateUpdate()
    {
        ChickenController.TickScheduledLateUpdates();
    }

    private void OnDestroy()
    {
        ChickenController.NotifySchedulerDestroyed(this);
    }
}
