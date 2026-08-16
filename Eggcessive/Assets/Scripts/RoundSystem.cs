using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(ProgressionSystem))]
public sealed class RoundSystem : MonoBehaviour
{
    private const float RewardAccumulationWindow = 0.75f;
    private static Sprite hudRoundedSprite;
    private const float ContainerRewardVisualDelay = 0.06f;
    private const float RewardFlightDuration = 0.62f;
    private const int RewardParticleCapacity = 8000;
    private const string RewardParticleShaderName =
        "Eggcessive/Particles/Reward Mesh";
    private const float RewardParticlePlaneDistance = 2f;
    private const float CoinParticlePixelSize = 40f;
    private const float CashParticlePixelSize = 82f;
    private const int MaximumParticleEmissionsPerFrame = 256;
    private const int MaximumUsefulRewardParticlesPerBurst = 1200;
    private const float MaximumCashBlend = 0.85f;
    private const float MaximumCashNoteRandomRotationDegrees = 30f;

    public enum RoundPhase
    {
        Intermission,
        Countdown,
        InProgress,
        Settling,
        TruckDeparting,
        Results,
        SuppliesShop
    }

    private sealed class RewardParticleTrail
    {
        public ParticleSystem ParticleSystem;
        public int RemainingCount;
        public Vector3 StartWorldPosition;
        public Vector2 TargetScreenPosition;
        public float PixelSize;
        public float EmitterRadiusPixels;
        public float EmissionInterval;
        public float NextEmissionTime;
        public bool IsCashNote;
    }

    private struct RewardParticleLanding
    {
        public float ArrivalTime;
        public bool IsCashNote;
    }

    private const string StartMarkerName = "truck_start";
    private const string StopMarkerName = "truck_stop";
    private const string EndMarkerName = "truck_end";
#if UNITY_EDITOR
    private const string UiFontAssetPath =
        "Assets/Fonts/Cat Song SDF.asset";
#endif

    [Header("Round")]
    [SerializeField, Min(1f)] private float roundDuration = 30f;
    [SerializeField, Min(0.1f)] private float countdownStepDuration = 1f;
    [SerializeField, Min(0.05f)] private float settlementDuration = 0.35f;
    [SerializeField, Min(1)] private int baseTruckEggTarget = 7;
    [SerializeField, Range(1f, 1.5f)]
    private float earlyTruckTargetGrowth = 1.16f;
    [SerializeField, Min(1)] private int earlyTruckTargetRounds = 10;
    [SerializeField, Min(0f)]
    private float lateTruckTargetIncreasePerRound = 3f;
    [SerializeField, Min(1)] private int maximumTruckEggTarget = 150;

    [Header("Round Cash Quota")]
    [Tooltip(
        "Cash that must be earned during the active round from egg sales and " +
        "truck bonuses. Spending and carried balance do not affect progress.")]
    [SerializeField, Min(100)] private long baseRoundCashQuotaCents = 800L;
    [SerializeField, Range(1f, 2f)] private float earlyCashQuotaGrowth = 1.34f;
    [SerializeField, Min(1)] private int earlyCashQuotaEndRound = 10;
    [SerializeField, Range(1f, 2f)] private float midCashQuotaGrowth = 1.32f;
    [SerializeField, Min(2)] private int midCashQuotaEndRound = 25;
    [SerializeField, Range(1f, 2f)] private float lateCashQuotaGrowth = 1.25f;
    [SerializeField, Min(3)] private int endgameCashQuotaStartRound = 30;
    [SerializeField, Range(1f, 2f)] private float endgameCashQuotaGrowth = 1.355f;
    [SerializeField, Min(4)] private int sustainedCashQuotaStartRound = 35;
    // Level 100 is the standard-mode finish. Fourteen percent sustained
    // growth keeps the final quota within fully developed farm output; the old
    // 25% curve compounded the level-35 quota more than two million times.
    [SerializeField, Range(1f, 2f)] private float sustainedCashQuotaGrowth = 1.14f;
    [SerializeField, Min(100)]
    private long maximumRoundCashQuotaCents = 9_000_000_000_000_000_000L;

    [Header("Truck")]
    [SerializeField] private GameObject truckVisualPrefab = null;
    [SerializeField, Min(0.1f)] private float truckDepartureDuration = 2.5f;

    private Transform truckStart;
    private Transform truckStop;
    private Transform truckEnd;
    private Transform truck;
    private TruckSpringAnimator truckSpringAnimator;
    private Canvas gameplayHudCanvas;
    private RectTransform gameplayCashHudTarget;

    [Header("Authored UI")]
    [SerializeField] private GameObject intermissionScreen;
    [SerializeField] private GameObject countdownDisplay;
    [SerializeField] private GameObject timerDisplay;
    [SerializeField] private GameObject quotaDisplay;
    [SerializeField] private GameObject liveStatsDisplay;
    [SerializeField] private GameObject resultsScreen;
    [SerializeField] private GameObject suppliesShopScreen;
    [SerializeField] private TMP_Text intermissionTitle;
    [SerializeField] private TMP_Text countdownText;
    [SerializeField] private TMP_Text roundNumberText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text quotaTitleText;
    [SerializeField] private TMP_Text quotaValueText;
    [SerializeField] private TMP_Text liveStatsText;
    [SerializeField] private TMP_Text liveStatsValueText;
    [SerializeField] private TMP_Text[] liveStatRowValues =
        new TMP_Text[6];
    [SerializeField] private TMP_Text resultsTitleText;
    [SerializeField] private TMP_Text resultsCashText;
    [SerializeField] private TMP_Text resultsCollectedText;
    [SerializeField] private TMP_Text resultsLaidText;
    [SerializeField] private TMP_Text resultsPerMinuteText;
    [SerializeField] private TMP_Text resultsHatchedText;
    [SerializeField] private TMP_Text resultsChickenCountText;
    [SerializeField] private TMP_Text resultsQuotaText;
    [SerializeField] private TMP_Text shopBalanceText;
    [SerializeField] private TMP_Text shopFeedDetailsText;
    [SerializeField] private TMP_Text shopIncubatorDetailsText;
    [SerializeField] private TMP_Text shopCollectionDetailsText;
    [SerializeField] private TMP_Text shopStatusText;
    [SerializeField] private TMP_Text feedBagProgressText;
    [SerializeField] private TMP_Text feedUnlockProgressText;
    [SerializeField] private TMP_Text incubatorProgressText;
    [SerializeField] private TMP_Text collectionProgressText;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button intermissionShopButton;
    [SerializeField] private Button resultsShopButton;
    [SerializeField] private Button resultsContinueButton;
    [SerializeField] private Button buyFeedButton;
    [SerializeField] private Button upgradeFeedButton;
    [SerializeField] private Button upgradeIncubatorButton;
    [SerializeField] private Button upgradeCollectionButton;
    [SerializeField] private Button doneShoppingButton;
    [SerializeField] private Image feedBagProgressFill;
    [SerializeField] private Image feedUnlockProgressFill;
    [SerializeField] private Image incubatorProgressFill;
    [SerializeField] private Image collectionProgressFill;
    [SerializeField] private Image[] quotaContributionFills =
        new Image[8];
    [SerializeField] private RectTransform roundCanvasRect;
    [SerializeField] private RectTransform coinEffectLayer;
    [SerializeField] private RectTransform coinHudTarget;
    [SerializeField] private GameObject flyingCoinPrefab = null;
    [SerializeField, Min(0f)]
    private float flyingCoinSpinDegreesPerSecond = 2880f;
    [SerializeField] private GameObject[] flyingCashModels = Array.Empty<GameObject>();
    [SerializeField] private Material flyingCashMaterial = null;
    [Tooltip(
        "Optional view-space RGB lighting lookup multiplied over the cash " +
        "texture. The center represents a surface facing the camera.")]
    [SerializeField] private Texture2D flyingCashLightingMatCap = null;
    [Tooltip("Brightness of the directional cash MatCap lighting.")]
    [SerializeField, Range(0f, 8f)]
    private float flyingCashMatCapLightStrength = 3f;
    [SerializeField] private Shader rewardParticleShader = null;
    [SerializeField, Min(1)] private int cashTransitionStartCents = 50000;
    [SerializeField, Min(1)] private int cashRewardThresholdCents = 300000;
    [SerializeField, Range(250, 10000)]
    private int maximumRewardParticlesPerBurst = 1200;
    [Tooltip("Cash notes are visually much larger than coins, so emit only this fraction.")]
    [SerializeField, Range(0.05f, 1f)]
    private float cashNoteParticleDensity = 0.1f;
    [SerializeField, Range(25, 1000)]
    private int maximumCashNotesPerBurst = 120;
    [SerializeField, Range(0.5f, 5f)]
    private float rewardParticleTrailDuration = 1f;
    [SerializeField, Range(100f, 5000f)]
    private float maximumRewardParticlesPerSecond = 1200f;
    [Tooltip(
        "Random timing variation applied between consecutive reward particles. "
        + "This prevents simultaneous trails from locking into visible clumps.")]
    [SerializeField, Range(0f, 0.75f)]
    private float rewardParticleEmissionJitter = 0.35f;
    [Tooltip(
        "Radius of the circular coin emission area in screen pixels.")]
    [SerializeField, Range(0f, 100f)]
    private float coinRewardEmitterRadiusPixels = 12f;
    [Tooltip(
        "Radius of the circular cash-note emission area in screen pixels.")]
    [SerializeField, Range(0f, 100f)]
    private float cashRewardEmitterRadiusPixels = 18f;
    [SerializeField, Min(0f)]
    private float flyingCashMinimumSpinDegreesPerSecond = 60f;
    [SerializeField, Min(0f)]
    private float flyingCashMaximumSpinDegreesPerSecond = 120f;
    [SerializeField] private GameObject floatingRewardPrefab = null;

    [Header("UI Audio")]
    [SerializeField] private AudioClip buttonClickSfx = null;
    [SerializeField] private AudioClip cashRegisterSfx = null;
    [SerializeField] private AudioClip countdownTickSfx = null;
    [SerializeField] private AudioClip resultsTickSfx = null;
    [SerializeField, Range(0f, 1f)]
    private float resultsTickSfxVolume = 0.65f;
    [SerializeField, Min(0f)]
    private float resultsTickMinimumInterval = 0.035f;
    [SerializeField] private AudioClip roundStartSfx = null;
    [SerializeField] private AudioClip roundEndSfx = null;
    [SerializeField] private AudioClip grabSfx = null;
    [SerializeField] private AudioClip vacuumOnSfx = null;
    [SerializeField] private AudioClip vacuumEggSfx = null;
    [SerializeField] private AudioClip foodPickupSfx = null;
    [SerializeField] private AudioClip foodPlaceSfx = null;
    [SerializeField] private AudioClip cursorMovementSfx = null;
    [SerializeField] private AudioClip[] coinLandingSfx = null;
    [SerializeField] private AudioClip[] cashLandingSfx = null;
    [SerializeField, Range(0f, 0.25f)]
    private float rewardLandingPitchVariation = 0.05f;
    [SerializeField, Range(0f, 0.25f)]
    private float rewardLandingVolumeVariation = 0.05f;
    [SerializeField, Range(0f, 1f)]
    private float cashLandingVolumeScale = 0.5f;
    [SerializeField, Range(5f, 60f)]
    private float maximumRewardLandingSoundsPerSecond = 24f;
    [SerializeField, Range(0f, 0.25f)]
    private float coalescedRewardLandingVolumeBoost = 0.08f;
    [SerializeField, Range(0f, 1f)] private float uiSfxVolume = 1f;
    [SerializeField, Range(0f, 1f)]
    private float vacuumSfxVolumeScale = 0.25f;
    [SerializeField, Min(0f)] private float vacuumSfxFadeDuration = 0.08f;
    [SerializeField, Range(0f, 1f)] private float cashRegisterSfxVolume = 0.65f;
    [SerializeField, Min(1)] private int coinAudioVoiceCount = 12;

    [Header("Cursor Movement Audio")]
    [SerializeField, Range(0f, 1f)]
    private float cursorMovementMaximumVolume = 0.75f;
    [SerializeField, Range(0.1f, 3f)]
    private float cursorMovementMinimumPitch = 0.85f;
    [SerializeField, Range(0.1f, 3f)]
    private float cursorMovementMaximumPitch = 1.35f;
    [SerializeField, Min(0.01f)]
    private float cursorMovementSpeedForMaximum = 1.25f;
    [SerializeField, Min(0f)]
    private float cursorMovementResponse = 10f;

    [Header("Truck Audio")]
    [SerializeField] private AudioClip truckEnterSfx = null;
    [SerializeField] private AudioClip truckExitSfx = null;
    [SerializeField] private AudioClip truckBonusHornSfx = null;
    [SerializeField, Range(0f, 1f)] private float truckSfxVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float truckBonusHornVolume = 0.9f;
    [SerializeField, Min(0f)] private float truckExitSoundLeadTime = 0.3f;

    [Header("Ambience")]
    [SerializeField] private AudioClip farmAmbienceSfx = null;
    [SerializeField, Range(0f, 1f)] private float farmAmbienceVolume = 1f;

    private Coroutine truckMovement;
    private Coroutine resultsAnimation;
    private float roundTimeRemaining;
    private float roundElapsed;
    private double roundStartedRealtime;
    private double roundEndsRealtime;
    private double externalPauseStartedRealtime;
    private bool isExternallyPaused;
    private float liveStatsRefreshTime;
    private int roundNumber;
    private int roundEggsCollected;
    private double totalCollectedEggWeight;
    private long roundCashMade;
    private int roundEggsLaid;
    private int roundEggsIncubated;
    private int roundChickensHatched;
    private int finalChickenCount;
    private long roundCashQuotaCents;
    private int roundEggTarget;
    private int eggsTowardTruck;
    private int trucksFilled;
    private long roundTruckCashMade;
    private int pendingTruckReplacements;
    private Coroutine rewardDisplayCoroutine;
    private TMP_Text activeRewardText;
    private Vector2 activeRewardStartPosition;
    private float lastRewardAddedTime;
    private long accumulatedRewardCents;
    private Coroutine containerRewardVisualCoroutine;
    private float lastContainerRewardAddedTime;
    private long accumulatedContainerRewardCents;
    private long containerRewardSequenceCents;
    private float lastContainerRewardSequenceTime;
    private Vector3 containerRewardStartWorldPosition;
    private int activeCoinAnimations;
    private ParticleSystem coinRewardParticleSystem;
    private ParticleSystem cashRewardParticleSystem;
    private Material coinRewardParticleMaterial;
    private Material cashRewardParticleMaterial;
    private readonly List<Mesh> rewardParticleMeshes = new List<Mesh>();
    private readonly List<RewardParticleTrail> pendingRewardParticleTrails =
        new List<RewardParticleTrail>();
    private readonly Queue<RewardParticleLanding>
        pendingRewardParticleLandings =
            new Queue<RewardParticleLanding>();
    private float rewardParticleEmissionAllowance;
    private bool forceCashNotesForTesting;
    private long shopDisplayedBalanceCents;
    private Tweener shopBalanceTween;
    private bool skipResultsAnimation;
    private bool roundPassed;
    private bool retryCurrentRound;
    private bool configured;
    private readonly List<Button> uiClickButtons = new List<Button>();
    private AudioSource buttonClickAudioSource;
    private AudioSource cashRegisterAudioSource;
    private AudioSource roundCueAudioSource;
    private AudioSource resultsTickAudioSource;
    private AudioSource grabAudioSource;
    private AudioSource vacuumAudioSource;
    private Coroutine vacuumSfxFade;
    private bool vacuumSfxRequestedActive;
    private AudioSource vacuumEggAudioSource;
    private AudioSource foodAudioSource;
    private AudioSource cursorMovementAudioSource;
    private AudioSource[] coinAudioSources = Array.Empty<AudioSource>();
    private AudioSource truckEnterAudioSource;
    private AudioSource truckExitAudioSource;
    private AudioSource truckBonusHornAudioSource;
    private AudioSource farmAmbienceAudioSource;
    private Vector2 lastCursorPosition;
    private float cursorMovementIntensity;
    private bool hasCursorPosition;
    private int nextCoinAudioSource;
    private int lastCoinLandingClipIndex = -1;
    private int lastCashLandingClipIndex = -1;
    private int pendingCoinAudioArrivals;
    private int pendingCashAudioArrivals;
    private float nextRewardLandingSoundTime;
    private readonly long[] roundCashContributionsCents =
        new long[8];
    private readonly int[] roundEggContributions = new int[8];
    private TMP_Text resultsSubtitleText;
    private TMP_Text resultsQuotaLabelText;
    [SerializeField] private GameObject additionalPenMilestoneScreen = null;
    [SerializeField] private Button additionalPenMilestoneButton = null;
    private bool milestoneReturnsToShop;

    public static RoundSystem Instance { get; private set; }
    public RoundPhase Phase { get; private set; } = RoundPhase.Intermission;
    public float TimeRemaining => roundTimeRemaining;
    public int RoundNumber => roundNumber;
    public long CashQuotaCents => roundCashQuotaCents;
    public long CashQuotaProgressCents => roundCashMade;
    public int EggTarget => roundEggTarget;
    public int EggsTowardTruck => eggsTowardTruck;
    public int TrucksFilled => trucksFilled;
    public GameObject TruckVisualPrefab => truckVisualPrefab;
    public int RoundEggsCollected => roundEggsCollected;
    public int RoundEggsLaid => roundEggsLaid;
    public int RoundEggsIncubated => roundEggsIncubated;
    public int RoundEggsProcessed =>
        roundEggsCollected + roundEggsIncubated;
    public float StartupProductionMultiplier =>
        roundNumber <= 0
            ? 1.75f
            : Mathf.Lerp(1f, 1.75f, Mathf.Clamp01((5f - roundNumber) / 4f));
    public bool IsRoundInProgress => Phase == RoundPhase.InProgress;
    public bool IsRoundAcceptingEggs =>
        Phase == RoundPhase.InProgress || Phase == RoundPhase.Settling;
    public bool IsSuppliesShopOpen => Phase == RoundPhase.SuppliesShop;
    public bool DidPassRound => roundPassed;
    public bool IsExternallyPaused => isExternallyPaused;

    public void SetExternalPause(bool paused)
    {
        if (isExternallyPaused == paused)
        {
            return;
        }

        double realtimeNow = Time.realtimeSinceStartupAsDouble;
        if (paused)
        {
            isExternallyPaused = true;
            externalPauseStartedRealtime = realtimeNow;
            return;
        }

        if (Phase == RoundPhase.InProgress)
        {
            double pausedDuration = Math.Max(
                0d,
                realtimeNow - externalPauseStartedRealtime);
            roundStartedRealtime += pausedDuration;
            roundEndsRealtime += pausedDuration;
        }

        isExternallyPaused = false;
    }

    public long GetPenCashPerMinuteCents(int penIndex)
    {
        if (penIndex < 0
            || penIndex >= roundCashContributionsCents.Length
            || roundElapsed <= 0.01f)
        {
            return 0L;
        }

        double centsPerMinute = roundCashContributionsCents[penIndex]
            * 60d
            / roundElapsed;
        return (long)Math.Min(long.MaxValue, Math.Round(centsPerMinute));
    }

    public float GetPenEggsPerMinute(int penIndex)
    {
        if (penIndex < 0
            || penIndex >= roundEggContributions.Length
            || roundElapsed <= 0.01f)
        {
            return 0f;
        }

        return roundEggContributions[penIndex] * 60f / roundElapsed;
    }
    public bool IsCashQuotaMet => roundCashQuotaCents > 0
        && roundCashMade >= roundCashQuotaCents;

    public static event Action<RoundPhase> PhaseChanged;
    public static event Action<int> RoundStarted;
    public static event Action<int> RoundEnded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        PhaseChanged = null;
        RoundStarted = null;
        RoundEnded = null;
    }

    private void Configure(Transform start, Transform stop, Transform end)
    {
        truckStart = start;
        truckStop = stop;
        truckEnd = end;
        configured = true;
    }

    private void Start()
    {
        Application.runInBackground = true;

        if (!configured)
        {
            GameObject start = GameObject.Find(StartMarkerName);
            GameObject stop = GameObject.Find(StopMarkerName);
            GameObject end = GameObject.Find(EndMarkerName);

            if (start == null || stop == null || end == null)
            {
                Debug.LogError(
                    $"{nameof(RoundSystem)} requires scene objects named " +
                    $"{StartMarkerName}, {StopMarkerName}, and {EndMarkerName}.",
                    this);
                enabled = false;
                return;
            }

            Configure(start.transform, stop.transform, end.transform);
        }

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        InitializeUiAudio();
        if (!HasAuthoredUi())
        {
            Debug.LogError(
                $"{nameof(RoundSystem)} on {name} is missing its authored UI prefab references.",
                this);
            enabled = false;
            return;
        }

        coinEffectLayer.SetAsLastSibling();
        InitializeRewardParticleSystems();
        SupplyShopGraphController.Install(suppliesShopScreen);
        BindButtonClickSfx();
        BindUiEvents();
        EggScoreHud gameplayHud = FindFirstObjectByType<EggScoreHud>(
            FindObjectsInactive.Include);
        gameplayHudCanvas = gameplayHud != null
            ? gameplayHud.GetComponent<Canvas>()
            : null;
        gameplayCashHudTarget = gameplayHud != null
            ? gameplayHud.CashTarget
            : null;
        ResolveResultsPresentationReferences();
        InitializeQuotaHud();
        EggContainer.EggCollectedWithWeightFromContainer += HandleEggCollected;
        EggContainer.FocusedContainerChanged += HandleFocusedContainerChanged;
        ChickenController.EggLaid += HandleEggLaid;
        IncubatorController.ChickenHatched += HandleChickenHatched;
        IncubatorController.EggsAccepted += HandleEggsAccepted;
        EggScoreHud.BalanceChanged += HandleBalanceChanged;
        ProgressionSystem.Changed += HandleProgressionChanged;
        ShowIntermission();
    }

    private void InitializeUiAudio()
    {
        buttonClickAudioSource = Create2dAudioSource();
        cashRegisterAudioSource = Create2dAudioSource();
        roundCueAudioSource = Create2dAudioSource();
        resultsTickAudioSource = Create2dAudioSource();
        grabAudioSource = Create2dAudioSource();
        vacuumAudioSource = Create2dAudioSource();
        vacuumAudioSource.clip = vacuumOnSfx;
        vacuumAudioSource.loop = true;
        vacuumAudioSource.volume = 0f;
        vacuumEggAudioSource = Create2dAudioSource();
        foodAudioSource = Create2dAudioSource();
        StartCursorMovementSfx();
        coinAudioSources = new AudioSource[
            Mathf.Max(1, coinAudioVoiceCount)];

        for (int index = 0; index < coinAudioSources.Length; index++)
        {
            coinAudioSources[index] = Create2dAudioSource();
        }

        truckEnterAudioSource = Create2dAudioSource();
        truckExitAudioSource = Create2dAudioSource();
        truckBonusHornAudioSource = Create2dAudioSource();
        StartFarmAmbience();
    }

    private AudioSource Create2dAudioSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        return source;
    }

    private void StartCursorMovementSfx()
    {
        if (cursorMovementSfx == null)
        {
            return;
        }

        cursorMovementAudioSource = Create2dAudioSource();
        cursorMovementAudioSource.clip = cursorMovementSfx;
        cursorMovementAudioSource.loop = true;
        cursorMovementAudioSource.volume = 0f;
        cursorMovementAudioSource.pitch = cursorMovementMinimumPitch;
        cursorMovementAudioSource.Play();
    }

    private void StartFarmAmbience()
    {
        if (farmAmbienceSfx == null)
        {
            return;
        }

        farmAmbienceAudioSource = Create2dAudioSource();
        farmAmbienceAudioSource.clip = farmAmbienceSfx;
        farmAmbienceAudioSource.loop = true;
        farmAmbienceAudioSource.volume = farmAmbienceVolume;
        farmAmbienceAudioSource.Play();
    }

    private void BindButtonClickSfx()
    {
        Button[] buttons = FindObjectsByType<Button>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int index = 0; index < buttons.Length; index++)
        {
            Button button = buttons[index];
            button.onClick.AddListener(PlayButtonClickSfx);
            uiClickButtons.Add(button);
        }
    }

    private void PlayButtonClickSfx()
    {
        if (buttonClickAudioSource == null || buttonClickSfx == null)
        {
            return;
        }

        buttonClickAudioSource.pitch = 1f;
        buttonClickAudioSource.PlayOneShot(buttonClickSfx, uiSfxVolume);
    }

    public void PlayCashRegisterSfx()
    {
        if (cashRegisterAudioSource == null || cashRegisterSfx == null)
        {
            return;
        }

        cashRegisterAudioSource.pitch = 1f;
        cashRegisterAudioSource.PlayOneShot(
            cashRegisterSfx,
            uiSfxVolume * cashRegisterSfxVolume);
    }

    public void PlayGrabSfx()
    {
        if (grabAudioSource == null || grabSfx == null)
        {
            return;
        }

        grabAudioSource.pitch = 1f;
        grabAudioSource.PlayOneShot(grabSfx, uiSfxVolume);
    }

    public void SetVacuumSfxActive(bool active)
    {
        if (vacuumAudioSource == null || vacuumOnSfx == null)
        {
            return;
        }

        if (vacuumSfxRequestedActive == active)
        {
            return;
        }

        vacuumSfxRequestedActive = active;

        if (vacuumSfxFade != null)
        {
            StopCoroutine(vacuumSfxFade);
            vacuumSfxFade = null;
        }

        if (active)
        {
            if (!vacuumAudioSource.isPlaying)
            {
                vacuumAudioSource.pitch = 1f;
                vacuumAudioSource.volume = 0f;
                vacuumAudioSource.Play();
            }

            vacuumSfxFade = StartCoroutine(FadeVacuumSfx(
                uiSfxVolume * vacuumSfxVolumeScale,
                false));
            return;
        }

        if (vacuumAudioSource.isPlaying)
        {
            vacuumSfxFade = StartCoroutine(FadeVacuumSfx(0f, true));
        }
    }

    private IEnumerator FadeVacuumSfx(
        float targetVolume,
        bool stopWhenSilent)
    {
        float startVolume = vacuumAudioSource.volume;
        float duration = Mathf.Max(0f, vacuumSfxFadeDuration);

        if (duration > 0f)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                progress = progress * progress * (3f - 2f * progress);
                vacuumAudioSource.volume = Mathf.Lerp(
                    startVolume,
                    targetVolume,
                    progress);
                yield return null;
            }
        }

        vacuumAudioSource.volume = targetVolume;

        if (stopWhenSilent)
        {
            vacuumAudioSource.Stop();
        }

        vacuumSfxFade = null;
    }

    public void PlayVacuumEggSfx()
    {
        if (vacuumEggAudioSource == null || vacuumEggSfx == null)
        {
            return;
        }

        vacuumEggAudioSource.pitch = 1f;
        vacuumEggAudioSource.PlayOneShot(vacuumEggSfx, uiSfxVolume);
    }

    public void PlayFoodPickupSfx()
    {
        PlayFoodSfx(foodPickupSfx);
    }

    public void PlayFoodPlaceSfx()
    {
        PlayFoodSfx(foodPlaceSfx);
    }

    private void PlayFoodSfx(AudioClip clip)
    {
        if (foodAudioSource == null || clip == null)
        {
            return;
        }

        foodAudioSource.pitch = 1f;
        foodAudioSource.PlayOneShot(clip, uiSfxVolume);
    }

    private void PlayRoundCueSfx(AudioClip clip)
    {
        if (roundCueAudioSource == null || clip == null)
        {
            return;
        }

        roundCueAudioSource.pitch = 1f;
        roundCueAudioSource.PlayOneShot(clip, uiSfxVolume);
    }

    private void PlayCoinLandingSfx(float arrivalVolumeScale)
    {
        PlayRewardLandingSfx(
            coinLandingSfx,
            ref lastCoinLandingClipIndex,
            arrivalVolumeScale);
    }

    private void PlayCashLandingSfx(float arrivalVolumeScale)
    {
        PlayRewardLandingSfx(
            cashLandingSfx,
            ref lastCashLandingClipIndex,
            cashLandingVolumeScale * arrivalVolumeScale);
    }

    private void PlayRewardLandingSfx(
        AudioClip[] clips,
        ref int lastClipIndex,
        float baseVolumeScale)
    {
        if (coinAudioSources.Length == 0
            || clips == null
            || clips.Length == 0)
        {
            return;
        }

        int clipIndex = UnityEngine.Random.Range(0, clips.Length);
        if (clips.Length > 1
            && clipIndex == lastClipIndex)
        {
            clipIndex = (clipIndex + UnityEngine.Random.Range(
                1,
                clips.Length)) % clips.Length;
        }

        AudioClip clip = clips[clipIndex];
        if (clip == null)
        {
            return;
        }

        lastClipIndex = clipIndex;
        AudioSource source = coinAudioSources[nextCoinAudioSource];
        nextCoinAudioSource =
            (nextCoinAudioSource + 1) % coinAudioSources.Length;
        source.pitch = 1f + UnityEngine.Random.Range(
            -rewardLandingPitchVariation,
            rewardLandingPitchVariation);
        source.volume = 1f;
        float volumeScale = uiSfxVolume * baseVolumeScale * (
            1f + UnityEngine.Random.Range(
                -rewardLandingVolumeVariation,
                rewardLandingVolumeVariation));
        source.PlayOneShot(clip, volumeScale);
    }

    private bool HasAuthoredUi()
    {
        return intermissionScreen != null
            && countdownDisplay != null
            && timerDisplay != null
            && quotaDisplay != null
            && liveStatsDisplay != null
            && resultsTitleText != null
            && resultsScreen != null
            && suppliesShopScreen != null
            && readyButton != null
            && intermissionShopButton != null
            && resultsShopButton != null
            && resultsContinueButton != null
            && additionalPenMilestoneScreen != null
            && additionalPenMilestoneButton != null
            && doneShoppingButton != null
            && shopBalanceText != null
            && shopStatusText != null
            && roundCanvasRect != null
            && coinEffectLayer != null
            && coinHudTarget != null
            && quotaTitleText != null
            && quotaValueText != null
            && quotaContributionFills != null
            && quotaContributionFills.Length == PenUiPalette.Count
            && flyingCoinPrefab != null
            && floatingRewardPrefab != null;
    }

    private void BindUiEvents()
    {
        readyButton.onClick.AddListener(HandleReadyClicked);
        intermissionShopButton.onClick.AddListener(HandleIntermissionShopClicked);
        resultsShopButton.onClick.AddListener(HandleResultsShopClicked);
        resultsContinueButton.onClick.AddListener(HandleResultsContinueClicked);
        additionalPenMilestoneButton.onClick.AddListener(
            HandleAdditionalPenMilestoneClicked);
        doneShoppingButton.onClick.AddListener(ShowIntermission);
        BindSupplyShopTabs();
    }

    private void BindSupplyShopTabs()
    {
        if (suppliesShopScreen != null
            && suppliesShopScreen.GetComponentInChildren<SupplyShopGraphController>(true)
                != null)
        {
            return;
        }

        RectTransform card = suppliesShopScreen != null
            ? suppliesShopScreen.transform.Find("Supplies") as RectTransform
            : null;
        RectTransform treeContent = card != null
            ? card.Find("Progression Scroll View/Tree Viewport/Tree Content")
                as RectTransform
            : null;
        if (treeContent == null)
        {
            return;
        }

        RectTransform[] headers =
        {
            treeContent.Find("FOOD Branch") as RectTransform,
            treeContent.Find("TECH Branch") as RectTransform,
            treeContent.Find("COLLECTION Branch") as RectTransform
        };
        RectTransform[] groups =
        {
            treeContent.Find("Food Tree Group") as RectTransform,
            treeContent.Find("Tech Tree Group") as RectTransform,
            treeContent.Find("Collection Tree Group") as RectTransform
        };
        Color[] tabColors =
        {
            new Color(0.65f, 0.28f, 0.055f, 1f),
            new Color(0.34f, 0.17f, 0.48f, 1f),
            new Color(0.12f, 0.34f, 0.57f, 1f)
        };

        for (int index = 0; index < headers.Length; index++)
        {
            Button tab = headers[index] != null
                ? headers[index].GetComponent<Button>()
                : null;
            if (tab == null || groups[index] == null)
            {
                Debug.LogError(
                    "The authored supplies-shop prefab is missing a tab "
                    + "button or tree group.",
                    this);
                continue;
            }

            int selectedIndex = index;
            tab.onClick.AddListener(
                () => SetSupplyShopTreeTab(
                    selectedIndex,
                    headers,
                    groups,
                    tabColors));
        }
    }

    private void Update()
    {
        if (isExternallyPaused)
        {
            return;
        }

        UpdateRewardParticleStatus();

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f9Key.wasPressedThisFrame)
        {
            forceCashNotesForTesting = !forceCashNotesForTesting;
            Debug.Log(
                $"Cash reward test mode: " +
                $"{(forceCashNotesForTesting ? "NOTES" : "COINS/AUTO")} " +
                "(F9 to toggle).",
                this);
        }

        Mouse pointerMouse = GameplayTestBot.PointerMouse;
        UpdateCursorMovementSfx(pointerMouse);

        if (Phase == RoundPhase.Results
            && resultsAnimation != null
            && pointerMouse != null
            && pointerMouse.leftButton.wasPressedThisFrame)
        {
            skipResultsAnimation = true;
            resultsTickAudioSource?.Stop();
        }

        if (Phase != RoundPhase.InProgress)
        {
            return;
        }

        double realtimeNow = Time.realtimeSinceStartupAsDouble;
        roundElapsed = Mathf.Clamp(
            (float)(realtimeNow - roundStartedRealtime),
            0f,
            roundDuration);
        roundTimeRemaining = Mathf.Max(
            0f,
            (float)(roundEndsRealtime - realtimeNow));
        RefreshTimer();
        liveStatsRefreshTime -= Time.unscaledDeltaTime;

        if (liveStatsRefreshTime <= 0f)
        {
            liveStatsRefreshTime = 0.2f;
            RefreshLiveStats();
        }

        if (roundTimeRemaining <= 0f)
        {
            BeginRoundSettlement();
        }
    }

    private void UpdateCursorMovementSfx(Mouse pointerMouse)
    {
        if (cursorMovementAudioSource == null)
        {
            return;
        }

        float targetIntensity = 0f;

        if (Phase == RoundPhase.InProgress && pointerMouse != null)
        {
            Vector2 cursorPosition = pointerMouse.position.ReadValue();

            if (hasCursorPosition)
            {
                float screenDiagonal = Mathf.Max(
                    1f,
                    Mathf.Sqrt(Screen.width * Screen.width
                        + Screen.height * Screen.height));
                float deltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
                float normalizedSpeed =
                    Vector2.Distance(cursorPosition, lastCursorPosition)
                    / screenDiagonal
                    / deltaTime;
                targetIntensity = Mathf.Clamp01(
                    normalizedSpeed / cursorMovementSpeedForMaximum);
            }

            lastCursorPosition = cursorPosition;
            hasCursorPosition = true;
        }
        else
        {
            hasCursorPosition = false;
        }

        float response = 1f - Mathf.Exp(
            -cursorMovementResponse * Time.unscaledDeltaTime);
        cursorMovementIntensity = Mathf.Lerp(
            cursorMovementIntensity,
            targetIntensity,
            response);
        cursorMovementAudioSource.volume =
            cursorMovementMaximumVolume * Mathf.Sqrt(cursorMovementIntensity);
        cursorMovementAudioSource.pitch = Mathf.Lerp(
            cursorMovementMinimumPitch,
            cursorMovementMaximumPitch,
            cursorMovementIntensity);
    }

    private void OnDestroy()
    {
        if (vacuumSfxFade != null)
        {
            StopCoroutine(vacuumSfxFade);
            vacuumSfxFade = null;
        }

        for (int index = 0; index < uiClickButtons.Count; index++)
        {
            Button button = uiClickButtons[index];
            if (button != null)
            {
                button.onClick.RemoveListener(PlayButtonClickSfx);
            }
        }
        uiClickButtons.Clear();

        if (readyButton != null)
        {
            readyButton.onClick.RemoveListener(HandleReadyClicked);
        }

        intermissionShopButton?.onClick.RemoveListener(HandleIntermissionShopClicked);
        resultsShopButton?.onClick.RemoveListener(HandleResultsShopClicked);
        resultsContinueButton?.onClick.RemoveListener(HandleResultsContinueClicked);
        additionalPenMilestoneButton?.onClick.RemoveListener(
            HandleAdditionalPenMilestoneClicked);
        doneShoppingButton?.onClick.RemoveListener(ShowIntermission);

        EggContainer.EggCollectedWithWeightFromContainer -= HandleEggCollected;
        EggContainer.FocusedContainerChanged -= HandleFocusedContainerChanged;
        ChickenController.EggLaid -= HandleEggLaid;
        IncubatorController.ChickenHatched -= HandleChickenHatched;
        IncubatorController.EggsAccepted -= HandleEggsAccepted;
        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
        ProgressionSystem.Changed -= HandleProgressionChanged;
        shopBalanceTween?.Kill();
        ClearRewardPresentation();
        ClearPendingContainerRewardVisuals();
        DestroyRewardParticleSystems();

        if (Instance == this)
        {
            Instance = null;
        }

        DestroyTruck();
    }

    private void HandleReadyClicked()
    {
        if (Phase != RoundPhase.Intermission)
        {
            return;
        }

        readyButton.interactable = false;
        StartCoroutine(BeginRoundSequence());
    }

    private IEnumerator BeginRoundSequence()
    {
        ChickenEgg.ClearAllActive();
        FoodPile.ClearAllActive();
        SetPhase(RoundPhase.Countdown);
        intermissionScreen.SetActive(false);
        resultsScreen.SetActive(false);
        suppliesShopScreen.SetActive(false);
        countdownDisplay.SetActive(true);
        timerDisplay.SetActive(false);
        liveStatsDisplay.SetActive(false);
        SpawnTruck();

        float arrivalDuration = GetTruckArrivalDuration();
        truckMovement = StartCoroutine(MoveTruck(truckStop, arrivalDuration));

        for (int count = 3; count >= 1; count--)
        {
            countdownText.text = count.ToString();
            PlayRoundCueSfx(countdownTickSfx);
            yield return PulseCountdown(countdownStepDuration);
        }

        if (truckMovement != null)
        {
            StopCoroutine(truckMovement);
            truckMovement = null;
        }

        PlaceTruckAt(truckStop);
        bool isRetry = retryCurrentRound;
        if (!isRetry)
        {
            roundNumber++;
        }
        retryCurrentRound = false;
        roundPassed = false;
        roundTimeRemaining = roundDuration;
        roundElapsed = 0f;
        roundStartedRealtime = Time.realtimeSinceStartupAsDouble;
        roundEndsRealtime = roundStartedRealtime + roundDuration;
        roundEggsCollected = 0;
        roundCashMade = 0;
        Array.Clear(
            roundCashContributionsCents,
            0,
            roundCashContributionsCents.Length);
        Array.Clear(
            roundEggContributions,
            0,
            roundEggContributions.Length);
        roundEggsLaid = 0;
        roundEggsIncubated = 0;
        roundChickensHatched = 0;
        roundCashQuotaCents = CalculateRoundCashQuotaCents(roundNumber);
        roundEggTarget = CalculateTruckEggTarget(roundNumber);
        eggsTowardTruck = 0;
        trucksFilled = 0;
        roundTruckCashMade = 0;
        pendingTruckReplacements = 0;
        liveStatsRefreshTime = 0f;
        SetPhase(RoundPhase.InProgress);
        timerDisplay.SetActive(true);
        liveStatsDisplay.SetActive(true);
        RefreshTimer();
        RefreshLiveStats();
        RefreshQuotaHud();
        RoundStarted?.Invoke(roundNumber);

        countdownText.text = "GO!";
        PlayRoundCueSfx(roundStartSfx);
        yield return PulseCountdown(0.7f);

        if (Phase == RoundPhase.InProgress)
        {
            countdownDisplay.SetActive(false);
        }
    }

    private IEnumerator PulseCountdown(float duration)
    {
        RectTransform textTransform = countdownText.rectTransform;
        float elapsed = 0f;
        textTransform.localScale = Vector3.one * 1.35f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float scale = Mathf.Lerp(1.35f, 0.92f, Mathf.SmoothStep(0f, 1f, progress));
            textTransform.localScale = Vector3.one * scale;
            yield return null;
        }

        textTransform.localScale = Vector3.one;
    }

    private void BeginRoundSettlement()
    {
        if (Phase != RoundPhase.InProgress)
        {
            return;
        }

        SetPhase(RoundPhase.Settling);
        roundTimeRemaining = 0f;
        RefreshTimer();
        if (roundNumberText != null)
        {
            roundNumberText.text = $"ROUND {roundNumber}";
        }
        StartCoroutine(FinalizeRoundAfterSettlement());
    }

    private IEnumerator FinalizeRoundAfterSettlement()
    {
        float elapsed = 0f;

        // Trigger callbacks are processed on fixed steps. Waiting for at least
        // two guarantees a just-released egg can settle into the container.
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        const float maximumToolSettlementDuration = 1.5f;

        while (elapsed < settlementDuration
            || (elapsed < maximumToolSettlementDuration
                && EggCarryController.Instance != null
                && EggCarryController.Instance.HasPendingCollection))
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        yield return new WaitForFixedUpdate();

        while (activeCoinAnimations > 0
            || rewardDisplayCoroutine != null
            || containerRewardVisualCoroutine != null)
        {
            yield return null;
        }

        BeginTruckDeparture();
    }

    private void BeginTruckDeparture()
    {
        if (Phase != RoundPhase.Settling)
        {
            return;
        }

        SetPhase(RoundPhase.TruckDeparting);
        ChickenEgg.ClearAllActive();
        FoodPile.ClearAllActive();
        timerDisplay.SetActive(false);
        liveStatsDisplay.SetActive(false);
        countdownDisplay.SetActive(false);
        finalChickenCount = CountChickens();
        PlayRoundCueSfx(roundEndSfx);
        RoundEnded?.Invoke(roundNumber);

        if (truckMovement != null)
        {
            StopCoroutine(truckMovement);
        }

        truckMovement = StartCoroutine(DepartTruckAndShowResults());
    }

    private IEnumerator DepartTruckAndShowResults()
    {
        yield return MoveTruck(truckEnd, truckDepartureDuration);
        truckMovement = null;
        DestroyTruck();
        ShowResults();
    }

    private IEnumerator MoveTruck(
        Transform destination,
        float duration,
        bool playBonusHorn = false)
    {
        if (truck == null)
        {
            yield break;
        }

        PlayTruckMovementSfx(destination);
        if (destination == truckEnd && truckExitSoundLeadTime > 0f)
        {
            yield return new WaitForSeconds(truckExitSoundLeadTime);

            if (truck == null)
            {
                yield break;
            }
        }

        if (playBonusHorn)
        {
            PlayTruckBonusHornSfx();
        }

        Vector3 startPosition = truck.position;
        Quaternion startRotation = truck.rotation;
        Vector3 direction = destination.position - startPosition;
        Quaternion targetRotation = direction.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(direction.normalized, Vector3.up)
            : destination.rotation;
        float elapsed = 0f;

        while (elapsed < duration && truck != null)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            truck.SetPositionAndRotation(
                Vector3.Lerp(startPosition, destination.position, easedProgress),
                Quaternion.Slerp(startRotation, targetRotation, easedProgress));
            yield return null;
        }

        if (truck != null)
        {
            truck.SetPositionAndRotation(destination.position, targetRotation);
        }
    }

    private void PlayTruckMovementSfx(Transform destination)
    {
        AudioSource source;
        AudioClip clip;

        if (destination == truckStop)
        {
            source = truckEnterAudioSource;
            clip = truckEnterSfx;
        }
        else if (destination == truckEnd)
        {
            source = truckExitAudioSource;
            clip = truckExitSfx;
        }
        else
        {
            return;
        }

        if (source == null || clip == null)
        {
            return;
        }

        source.pitch = 1f;
        source.PlayOneShot(clip, truckSfxVolume);
    }

    public void PlayTruckBonusHornSfx()
    {
        if (truckBonusHornAudioSource == null || truckBonusHornSfx == null)
        {
            return;
        }

        truckBonusHornAudioSource.pitch = 1f;
        truckBonusHornAudioSource.PlayOneShot(
            truckBonusHornSfx,
            truckBonusHornVolume);
    }

    private float GetTruckArrivalDuration()
    {
        return Mathf.Max(0.1f, countdownStepDuration * 3f - 1f);
    }

    private void ShowIntermission()
    {
        SetPhase(RoundPhase.Intermission);
        intermissionTitle.text = roundNumber == 0
            ? "FIRST DELIVERY"
            : retryCurrentRound
                ? $"RETRY ROUND {roundNumber}"
                : $"ROUND {roundNumber + 1}";
        SetButtonText(
            readyButton,
            retryCurrentRound ? "RETRY ROUND" : "READY");
        readyButton.interactable = true;
        RectTransform readyRect = readyButton.GetComponent<RectTransform>();
        bool canReturnToShop = roundNumber > 0;
        readyRect.anchoredPosition = new Vector2(canReturnToShop ? -120f : 0f, -92f);
        intermissionShopButton.gameObject.SetActive(canReturnToShop);
        intermissionScreen.SetActive(true);
        countdownDisplay.SetActive(false);
        timerDisplay.SetActive(false);
        liveStatsDisplay.SetActive(false);
        resultsScreen.SetActive(false);
        suppliesShopScreen.SetActive(false);
    }

    private void ShowResults()
    {
        roundPassed = roundCashMade >= roundCashQuotaCents;
        retryCurrentRound = !roundPassed;
        SetPhase(RoundPhase.Results);
        resultsTitleText.text = roundPassed
            ? $"ROUND {roundNumber} PASSED"
            : $"ROUND {roundNumber} FAILED";
        resultsTitleText.color = roundPassed
            ? new Color(0.42f, 0.94f, 0.5f)
            : new Color(1f, 0.34f, 0.22f);
        if (resultsSubtitleText != null)
        {
            resultsSubtitleText.text = roundPassed
                ? $"QUOTA MET  {FormatMoney(roundCashMade)} / {FormatMoney(roundCashQuotaCents)}"
                : $"QUOTA MISSED  {FormatMoney(roundCashMade)} / {FormatMoney(roundCashQuotaCents)}";
            resultsSubtitleText.color = roundPassed
                ? new Color(0.66f, 0.9f, 0.68f)
                : new Color(1f, 0.62f, 0.42f);
        }
        SetButtonText(
            resultsContinueButton,
            roundPassed ? "NEXT ROUND" : "RETRY");
        intermissionScreen.SetActive(false);
        suppliesShopScreen.SetActive(false);
        resultsScreen.SetActive(true);
        resultsShopButton.gameObject.SetActive(false);
        resultsContinueButton.gameObject.SetActive(false);
        skipResultsAnimation = false;

        if (resultsAnimation != null)
        {
            StopCoroutine(resultsAnimation);
        }

        resultsAnimation = StartCoroutine(AnimateResults());
    }

    private IEnumerator AnimateResults()
    {
        resultsCashText.text = ".";
        resultsCollectedText.text = ".";
        resultsLaidText.text = ".";
        resultsPerMinuteText.text = ".";
        resultsHatchedText.text = ".";
        resultsChickenCountText.text = ".";
        resultsQuotaText.text = ".";

        yield return CountLongResult(
            resultsCashText,
            roundCashMade,
            FormatMoney);
        yield return CountResult(
            resultsCollectedText,
            roundEggsCollected,
            value => Mathf.RoundToInt(value).ToString());
        yield return CountResult(
            resultsLaidText,
            roundEggsLaid,
            value => Mathf.RoundToInt(value).ToString());

        float eggsPerMinute = roundElapsed > 0.01f
            ? roundEggsCollected * 60f / roundElapsed
            : 0f;
        yield return CountResult(
            resultsPerMinuteText,
            eggsPerMinute,
            value => Mathf.RoundToInt(value).ToString());
        yield return CountResult(
            resultsHatchedText,
            roundChickensHatched,
            value => Mathf.RoundToInt(value).ToString());
        yield return CountResult(
            resultsChickenCountText,
            finalChickenCount,
            value => Mathf.RoundToInt(value).ToString());
        yield return CountLongResult(
            resultsQuotaText,
            roundCashMade,
            value => $"{FormatMoney(value)} / {FormatMoney(roundCashQuotaCents)}");

        resultsAnimation = null;
        resultsShopButton.gameObject.SetActive(true);
        resultsContinueButton.gameObject.SetActive(true);
    }

    private IEnumerator CountResult(
        TMP_Text label,
        float target,
        Func<float, string> formatter)
    {
        const float countDuration = 0.55f;
        float elapsed = 0f;
        string previousFormattedValue = null;
        float nextTickTime = 0f;

        while (elapsed < countDuration && !skipResultsAnimation)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / countDuration);
            float value = Mathf.Lerp(0f, target, progress);
            string formattedValue = formatter(value);
            label.text = $"<color=#FFD95A>{formattedValue}</color>";

            if (formattedValue != previousFormattedValue
                && Time.unscaledTime >= nextTickTime)
            {
                PlayResultsTickSfx(progress);
                nextTickTime = Time.unscaledTime
                    + resultsTickMinimumInterval;
            }

            previousFormattedValue = formattedValue;
            yield return null;
        }

        label.text = $"<color=#FFD95A>{formatter(target)}</color>";

        if (!skipResultsAnimation)
        {
            yield return new WaitForSeconds(0.12f);
        }
    }

    private IEnumerator CountLongResult(
        TMP_Text label,
        long target,
        Func<long, string> formatter)
    {
        const float countDuration = 0.55f;
        float elapsed = 0f;
        string previousFormattedValue = null;
        float nextTickTime = 0f;

        while (elapsed < countDuration && !skipResultsAnimation)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / countDuration);
            long value = progress >= 1f
                ? target
                : (long)(target * (double)progress);
            string formattedValue = formatter(value);
            label.text = $"<color=#FFD95A>{formattedValue}</color>";

            if (formattedValue != previousFormattedValue
                && Time.unscaledTime >= nextTickTime)
            {
                PlayResultsTickSfx(progress);
                nextTickTime = Time.unscaledTime
                    + resultsTickMinimumInterval;
            }

            previousFormattedValue = formattedValue;
            yield return null;
        }

        label.text = $"<color=#FFD95A>{formatter(target)}</color>";

        if (!skipResultsAnimation)
        {
            yield return new WaitForSeconds(0.12f);
        }
    }

    private void PlayResultsTickSfx(float progress)
    {
        if (resultsTickAudioSource == null
            || resultsTickSfx == null)
        {
            return;
        }

        resultsTickAudioSource.Stop();
        resultsTickAudioSource.pitch = Mathf.Lerp(
            0.96f,
            1.06f,
            Mathf.Clamp01(progress));
        resultsTickAudioSource.PlayOneShot(
            resultsTickSfx,
            uiSfxVolume * resultsTickSfxVolume);
    }

    private void ShowSuppliesShop()
    {
        SetPhase(RoundPhase.SuppliesShop);
        resultsScreen.SetActive(false);
        intermissionScreen.SetActive(false);
        suppliesShopScreen.SetActive(true);
        shopStatusText.text = string.Empty;
        SetShopBalanceImmediate(EggScoreHud.CurrentCents);
        RefreshShopUi();
    }

    private void HandleIntermissionShopClicked()
    {
        if (Phase == RoundPhase.Intermission && roundNumber > 0)
        {
            ShowSuppliesShop();
        }
    }

    private void HandleResultsContinueClicked()
    {
        if (Phase == RoundPhase.Results)
        {
            if (!TryShowAdditionalPenMilestone(false))
            {
                ShowIntermission();
            }
        }
    }

    private void HandleResultsShopClicked()
    {
        if (Phase == RoundPhase.Results)
        {
            if (!TryShowAdditionalPenMilestone(true))
            {
                ShowSuppliesShop();
            }
        }
    }

    private bool TryShowAdditionalPenMilestone(bool returnToShop)
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        if (!roundPassed
            || roundNumber < PenExpansionManager.AdditionalPenUnlockRound
            || manager == null
            || manager.AreAdditionalPensUnlocked
            || additionalPenMilestoneScreen == null)
        {
            return false;
        }

        milestoneReturnsToShop = returnToShop;
        manager.UnlockAdditionalPens();
        resultsScreen.SetActive(false);
        additionalPenMilestoneScreen.SetActive(true);
        additionalPenMilestoneScreen.transform.SetAsLastSibling();
        return true;
    }

    private void HandleAdditionalPenMilestoneClicked()
    {
        if (additionalPenMilestoneScreen == null
            || !additionalPenMilestoneScreen.activeSelf)
        {
            return;
        }

        additionalPenMilestoneScreen.SetActive(false);
        if (milestoneReturnsToShop)
        {
            ShowSuppliesShop();
        }
        else
        {
            ShowIntermission();
        }
    }

    private void HandleBuyFeedClicked()
    {
        FoodShopController foodShop = FoodShopController.Instance;

        if (foodShop != null)
        {
            foodShop.TryBuyCurrentFeed(out string message);
            shopStatusText.text = message;
        }
        else
        {
            shopStatusText.text = "Feed system unavailable";
        }

        RefreshShopUi();
    }

    private void HandleUpgradeFeedClicked()
    {
        FoodShopController foodShop = FoodShopController.Instance;

        if (foodShop != null)
        {
            foodShop.TryUnlockNextFeedTier(out string message);
            shopStatusText.text = message;
        }
        else
        {
            shopStatusText.text = "Feed system unavailable";
        }

        RefreshShopUi();
    }

    private void HandleUpgradeIncubatorClicked()
    {
        IncubatorShopController incubatorShop = IncubatorShopController.Instance;

        if (incubatorShop == null)
        {
            shopStatusText.text = "Incubator system unavailable";
        }
        else
        {
            incubatorShop.TryPurchaseNextLevel(out string message);
            shopStatusText.text = message;
        }

        RefreshShopUi();
    }

    private void HandleUpgradeCollectionClicked()
    {
        EggCarryController collection = EggCarryController.Instance;

        if (collection == null)
        {
            shopStatusText.text = "Collection system unavailable";
        }
        else
        {
            collection.TryPurchaseNextCollectionLevel(out string message);
            shopStatusText.text = message;
        }

        RefreshShopUi();
    }

    private void RefreshShopUi()
    {
        long balance = EggScoreHud.CurrentCents;
        shopBalanceText.text = FormatMoney(shopDisplayedBalanceCents);

        if (suppliesShopScreen != null)
        {
            ProgressionNodeButton[] nodes =
                suppliesShopScreen.GetComponentsInChildren<ProgressionNodeButton>(true);

            for (int index = 0; index < nodes.Length; index++)
            {
                nodes[index].Refresh();
            }

            SupplyShopGraphController graph =
                suppliesShopScreen.GetComponentInChildren<SupplyShopGraphController>(true);
            graph?.RefreshAll();

            RefreshSupplyShopTabIndicators();
            return;
        }

        FoodShopController foodShop = FoodShopController.Instance;

        if (foodShop != null)
        {
            string bagStatus = foodShop.OwnedFoodCount <= 0
                ? "<color=#FF3D24><b>EMPTY</b></color>"
                : $"{foodShop.OwnedFoodCount} BAGS";

            shopFeedDetailsText.text =
                $"<color=#FFD95A>{foodShop.CurrentFeedName}</color>  " +
                $"TIER {foodShop.UnlockedFeedTier}/{FoodShopController.MaximumFeedTier}\n" +
                $"{foodShop.CurrentFeedSpeedMultiplier:0.##}x SPEED   .   " +
                bagStatus +
                (foodShop.HasFeedTierUpgrade
                    ? $"\nNEXT: {foodShop.NextFeedName}  " +
                      $"{foodShop.NextFeedSpeedMultiplier:0.##}x"
                    : "\nMAX TIER");
            buyFeedButton.GetComponentInChildren<TMP_Text>().text =
                $"BUY BAG  {FormatMoney(foodShop.CurrentFeedBagCost)}";
            SetAffordability(
                buyFeedButton,
                feedBagProgressFill,
                feedBagProgressText,
                balance,
                foodShop.CurrentFeedBagCost,
                true);
            upgradeFeedButton.GetComponentInChildren<TMP_Text>().text =
                foodShop.HasFeedTierUpgrade
                    ? $"UNLOCK NEXT TIER  {FormatMoney(foodShop.NextFeedTierUnlockCost)}"
                    : "MAX FEED TIER";
            SetAffordability(
                upgradeFeedButton,
                feedUnlockProgressFill,
                feedUnlockProgressText,
                balance,
                foodShop.NextFeedTierUnlockCost,
                foodShop.HasFeedTierUpgrade);
        }

        IncubatorShopController incubatorShop = IncubatorShopController.Instance;

        if (incubatorShop != null)
        {
            int currentLevel = incubatorShop.CurrentLevel;
            shopIncubatorDetailsText.text =
                $"<color=#FFD95A>LEVEL {currentLevel}/{IncubatorController.MaximumLevel}</color>\n" +
                (currentLevel == 0
                    ? "NOT INSTALLED"
                    : $"{IncubatorController.GetCapacity(currentLevel)} CAPACITY   .   " +
                      $"{IncubatorController.GetProductionTime(currentLevel):0.##} SEC") +
                (incubatorShop.HasUpgrade
                    ? $"\nNEXT: {incubatorShop.NextCapacity} CAPACITY   .   " +
                      $"{incubatorShop.NextProductionTime:0.##} SEC"
                    : "\nMAX LEVEL");
            upgradeIncubatorButton.GetComponentInChildren<TMP_Text>().text =
                incubatorShop.HasUpgrade
                    ? $"{(currentLevel == 0 ? "INSTALL" : "UPGRADE")}  " +
                      $"{FormatMoney(incubatorShop.NextUpgradeCost)}"
                    : "MAX INCUBATOR LEVEL";
            SetAffordability(
                upgradeIncubatorButton,
                incubatorProgressFill,
                incubatorProgressText,
                balance,
                incubatorShop.NextUpgradeCost,
                incubatorShop.HasUpgrade);
        }

        EggCarryController collection = EggCarryController.Instance;

        if (collection != null)
        {
            shopCollectionDetailsText.text =
                $"<color=#FFD95A>{collection.CurrentCollectionName}</color>  " +
                $"TIER {collection.CurrentCollectionLevel}/" +
                $"{EggCarryController.MaximumCollectionLevel}\n" +
                $"{collection.CurrentCollectionDetails}" +
                (collection.HasCollectionUpgrade
                    ? $"\nNEXT: {collection.NextCollectionName}  |  " +
                      collection.NextCollectionDetails
                    : "\nMAX COLLECTION TIER");
            upgradeCollectionButton.GetComponentInChildren<TMP_Text>().text =
                collection.HasCollectionUpgrade
                    ? $"UPGRADE  {FormatMoney(collection.NextCollectionUpgradeCost)}"
                    : "MAX COLLECTION TIER";
            SetAffordability(
                upgradeCollectionButton,
                collectionProgressFill,
                collectionProgressText,
                balance,
                collection.NextCollectionUpgradeCost,
                collection.HasCollectionUpgrade);
        }
    }

    public void SetShopStatus(string message)
    {
        if (shopStatusText != null)
        {
            shopStatusText.text = message;
        }

        RefreshShopUi();
    }

    private static void SetAffordability(
        Button button,
        Image progressFill,
        TMP_Text progressText,
        long currentCash,
        int cost,
        bool isAvailable)
    {
        bool isAffordable = isAvailable && currentCash >= cost;
        button.interactable = isAffordable;
        progressFill.transform.parent.gameObject.SetActive(isAvailable);

        if (!isAvailable)
        {
            return;
        }

        float progress = cost > 0 ? Mathf.Clamp01(currentCash / (float)cost) : 1f;
        RectTransform fillRect = progressFill.rectTransform;
        fillRect.anchorMax = new Vector2(progress, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        progressText.text = $"{FormatMoney(currentCash)} / {FormatMoney(cost)}";
    }

    private static string FormatMoney(int cents)
    {
        return FormatMoney((long)cents);
    }

    private static string FormatMoney(long cents)
    {
        return $"${cents / 100:N0}.{Math.Abs(cents % 100):D2}";
    }

    private void SetPhase(RoundPhase phase)
    {
        SetGameplayHudVisible(
            phase == RoundPhase.InProgress || phase == RoundPhase.Settling);

        if (Phase == phase)
        {
            return;
        }

        Phase = phase;
        PhaseChanged?.Invoke(phase);
    }

    private void SetGameplayHudVisible(bool visible)
    {
        if (gameplayHudCanvas != null)
        {
            gameplayHudCanvas.enabled = visible;
        }

        if (quotaDisplay != null)
        {
            quotaDisplay.SetActive(visible);
        }
    }

    private void ResolveResultsPresentationReferences()
    {
        if (resultsScreen == null)
        {
            return;
        }

        TMP_Text[] texts = resultsScreen.GetComponentsInChildren<TMP_Text>(true);
        for (int index = 0; index < texts.Length; index++)
        {
            TMP_Text text = texts[index];
            if (text == null)
            {
                continue;
            }

            if (text.name == "Results Subtitle")
            {
                resultsSubtitleText = text;
            }
            else if (text.name == "Cash Quota Label"
                || text.name == "Truck Quota Label"
                || text.name == "Egg Quota Label")
            {
                resultsQuotaLabelText = text;
                resultsQuotaLabelText.text = "CASH QUOTA";
            }
        }
    }

    private void InitializeQuotaHud()
    {
        if (quotaDisplay == null)
        {
            return;
        }

        if (quotaValueText != null)
        {
            quotaValueText.enableAutoSizing = true;
            quotaValueText.fontSizeMin = 14f;
            quotaValueText.fontSizeMax = 24f;
        }

        for (int index = 0; index < quotaContributionFills.Length; index++)
        {
            if (quotaContributionFills[index] != null)
            {
                quotaContributionFills[index].color =
                    PenUiPalette.GetColour(index);
            }
        }

        timerDisplay.SetActive(false);
        SetQuotaContributionFills();
        quotaDisplay.SetActive(false);
    }

    private void RefreshQuotaHud()
    {
        if (quotaValueText == null)
        {
            return;
        }

        bool met = roundCashQuotaCents > 0
            && roundCashMade >= roundCashQuotaCents;
        quotaValueText.text =
            $"{FormatQuotaMoney(roundCashMade)} / {FormatQuotaMoney(roundCashQuotaCents)}";
        quotaValueText.color = met
            ? new Color(0.42f, 0.94f, 0.5f)
            : new Color(1f, 0.84f, 0.3f);
        if (quotaTitleText != null)
        {
            quotaTitleText.text = "CASH QUOTA";
        }
        SetQuotaContributionFills();
    }

    private void SetQuotaContributionFills()
    {
        if (quotaContributionFills == null
            || quotaContributionFills.Length == 0)
        {
            return;
        }

        double quota = Math.Max(1d, roundCashQuotaCents);
        float cursor = 0f;
        for (int index = 0; index < quotaContributionFills.Length; index++)
        {
            Image image = quotaContributionFills[index];
            if (image == null)
            {
                continue;
            }

            float remaining = Mathf.Max(0f, 1f - cursor);
            float width = Mathf.Min(
                remaining,
                (float)(roundCashContributionsCents[index] / quota));
            RectTransform fill = image.rectTransform;
            fill.anchorMin = new Vector2(cursor, 0f);
            cursor += Mathf.Max(0f, width);
            fill.anchorMax = new Vector2(cursor, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            image.gameObject.SetActive(width > 0f);
        }
    }

    private static void SetButtonText(Button button, string value)
    {
        TMP_Text text = button != null
            ? button.GetComponentInChildren<TMP_Text>(true)
            : null;
        if (text != null)
        {
            text.text = value;
        }
    }

    private void RefreshTimer()
    {
        int seconds = Mathf.Max(0, Mathf.CeilToInt(roundTimeRemaining));
        if (roundNumberText != null)
        {
            roundNumberText.text = $"ROUND {roundNumber}";
        }
        timerText.text = $"{seconds / 60:00}:{seconds % 60:00}";
    }

    public void NotifyPenTruckProgressChanged()
    {
        if (timerText != null && IsRoundAcceptingEggs)
        {
            RefreshTimer();
        }
    }

    private void HandleEggCollected(
        EggContainer container,
        int cents,
        float weightKilograms)
    {
        if (!IsRoundAcceptingEggs)
        {
            return;
        }

        roundEggsCollected++;
        totalCollectedEggWeight += Mathf.Max(0f, weightKilograms);
        roundCashMade = SaturatingAdd(roundCashMade, cents);

        int penIndex = PenExpansionManager.Instance != null
            ? PenExpansionManager.Instance.GetPenIndex(container)
            : 0;
        int safePenIndex = Mathf.Clamp(
            penIndex,
            0,
            roundEggContributions.Length - 1);
        roundEggContributions[safePenIndex] = SaturatingAdd(
            roundEggContributions[safePenIndex],
            1);
        AddPenCashContribution(penIndex, cents);
        bool isPrimaryPen = penIndex <= 0;
        if (isPrimaryPen && IsRoundAcceptingEggs && roundEggTarget > 0)
        {
            truckSpringAnimator?.AddDepositImpulse(cents);
            eggsTowardTruck++;

            while (eggsTowardTruck >= roundEggTarget)
            {
                eggsTowardTruck -= roundEggTarget;
                CompleteTruckQuota();
            }
        }

        RefreshTimer();
        RefreshLiveStats();
        RefreshQuotaHud();
    }

    private void CompleteTruckQuota()
    {
        trucksFilled++;
        long bonus = RoundPositiveCents(
            roundEggTarget
            * 75d
            * trucksFilled
            * Math.Pow(1.08d, Mathf.Max(0, roundNumber - 1))
            * (ProgressionSystem.Instance != null
                ? ProgressionSystem.Instance.TruckBonusMultiplier
                : 1f));
        roundTruckCashMade = SaturatingAdd(roundTruckCashMade, bonus);
        roundCashMade = SaturatingAdd(roundCashMade, bonus);
        AddPenCashContribution(0, bonus);
        EggScoreHud.AddCents(bonus);

        if (truck != null)
        {
            ShowTruckBonusReward(
                truck.position + Vector3.up * 0.45f,
                bonus,
                trucksFilled,
                0);
        }

        pendingTruckReplacements++;

        if (truckMovement == null)
        {
            truckMovement = StartCoroutine(ReplaceFilledTrucks());
        }
    }

    public void CompleteAdditionalPenTruckQuota(
        Vector3 rewardPosition,
        int penIndex)
    {
        if (!IsRoundInProgress || roundEggTarget <= 0)
        {
            return;
        }

        trucksFilled++;
        long bonus = RoundPositiveCents(
            roundEggTarget
            * 75d
            * trucksFilled
            * Math.Pow(1.08d, Mathf.Max(0, roundNumber - 1))
            * (ProgressionSystem.Instance != null
                ? ProgressionSystem.Instance.TruckBonusMultiplier
                : 1f));
        roundTruckCashMade = SaturatingAdd(roundTruckCashMade, bonus);
        roundCashMade = SaturatingAdd(roundCashMade, bonus);
        AddPenCashContribution(penIndex, bonus);
        EggScoreHud.AddCents(bonus);
        if (IsFocusedPen(penIndex))
        {
            ShowTruckBonusReward(
                rewardPosition + Vector3.up * 0.45f,
                bonus,
                trucksFilled,
                penIndex);
        }
        RefreshTimer();
        RefreshLiveStats();
        RefreshQuotaHud();
    }

    private void AddPenCashContribution(int penIndex, long cents)
    {
        if (cents <= 0)
        {
            return;
        }

        int safeIndex = Mathf.Clamp(
            penIndex,
            0,
            roundCashContributionsCents.Length - 1);
        roundCashContributionsCents[safeIndex] = SaturatingAdd(
            roundCashContributionsCents[safeIndex],
            cents);
    }

    private IEnumerator ReplaceFilledTrucks()
    {
        while (pendingTruckReplacements > 0
            && Phase == RoundPhase.InProgress)
        {
            pendingTruckReplacements--;
            yield return MoveTruck(
                truckEnd,
                truckDepartureDuration * 0.5f,
                true);
            DestroyTruck();

            if (Phase != RoundPhase.InProgress)
            {
                break;
            }

            SpawnTruck();
            yield return MoveTruck(
                truckStop,
                GetTruckArrivalDuration() * 0.5f);
        }

        truckMovement = null;
    }

    public void ShowContainerCoinReward(EggContainer container, int cents)
    {
        if (container == null)
        {
            return;
        }

        PenExpansionManager manager = PenExpansionManager.Instance;
        int penIndex = manager != null
            ? manager.GetPenIndex(container)
            : -1;
        bool isFocusedSource = penIndex >= 0
            ? IsFocusedPen(penIndex)
            : EggContainer.Instance == container;
        if (isFocusedSource)
        {
            ShowContainerCoinRewardVisuals(container.RewardPosition, cents);
        }
    }

    private void ShowTruckBonusReward(
        Vector3 worldPosition,
        long cents,
        int multiplier,
        int penIndex)
    {
        if (!IsFocusedPen(penIndex)
            || roundCanvasRect == null
            || coinEffectLayer == null
            || coinHudTarget == null)
        {
            return;
        }

        PenExpansionManager manager = PenExpansionManager.Instance;
        if (manager != null && manager.IsInitialized)
        {
            worldPosition = manager.GetTruckStopPosition(penIndex)
                + Vector3.up * 0.45f;
        }
        else if (truckStop != null)
        {
            worldPosition = truckStop.position + Vector3.up * 0.45f;
        }

        Camera worldCamera = Camera.main;
        Camera canvasCamera = GetRoundCanvasCamera();
        if (worldCamera == null)
        {
            return;
        }

        Vector3 screenPosition =
            worldCamera.WorldToScreenPoint(worldPosition);
        if (screenPosition.z <= 0f
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                roundCanvasRect,
                screenPosition,
                canvasCamera,
                out Vector2 startPosition))
        {
            return;
        }

        ShowTruckBonusText(
            startPosition,
            cents,
            multiplier);

        Vector2 targetScreenPosition = GetCashHudTargetScreenPosition();
        int particleCount = CalculateRewardParticleCount(cents);
        CalculateRewardParticleMix(
            cents,
            particleCount,
            out int coinParticleCount,
            out int cashNoteParticleCount);
        SpawnRewardParticles(
            worldPosition,
            targetScreenPosition,
            coinParticleCount,
            cashNoteParticleCount);
    }

    private void ShowTruckBonusText(
        Vector2 startPosition,
        long cents,
        int multiplier)
    {
        GameObject rewardObject = Instantiate(
            floatingRewardPrefab,
            coinEffectLayer);
        rewardObject.name = "Truck Bonus Reward";
        TMP_Text rewardText = rewardObject.GetComponent<TMP_Text>();
        rewardText.enableAutoSizing = false;
        rewardText.textWrappingMode = TextWrappingModes.NoWrap;
        rewardText.overflowMode = TextOverflowModes.Overflow;
        rewardText.alignment = TextAlignmentOptions.Center;
        rewardText.text =
            $"x{Mathf.Max(1, multiplier)} TRUCK BONUS!\n" +
            $"+{FormatMoney(cents)}";
        rewardText.fontSize = Mathf.Clamp(
            36f + (float)Math.Log10(1d + cents / 100d) * 5f,
            36f,
            50f);
        rewardText.rectTransform.sizeDelta =
            new Vector2(900f, 150f);
        rewardText.rectTransform.anchoredPosition = startPosition;
        rewardText.rectTransform.localScale = Vector3.one;
        rewardText.rectTransform.DOPunchScale(
            Vector3.one * 0.24f,
            0.3f,
            7,
            0.55f);
        StartCoroutine(AnimateTruckBonusText(
            rewardText,
            startPosition));
    }

    private static IEnumerator AnimateTruckBonusText(
        TMP_Text rewardText,
        Vector2 startPosition)
    {
        const float holdDuration = 0.75f;
        float elapsed = 0f;
        while (elapsed < holdDuration && rewardText != null)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        const float fadeDuration = 0.55f;
        elapsed = 0f;
        while (elapsed < fadeDuration && rewardText != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / fadeDuration);
            rewardText.rectTransform.anchoredPosition =
                startPosition
                + Vector2.up * Mathf.Lerp(0f, 72f, progress);
            Color color = rewardText.color;
            color.a = 1f - Mathf.SmoothStep(0f, 1f, progress);
            rewardText.color = color;
            yield return null;
        }

        if (rewardText != null)
        {
            rewardText.rectTransform.DOKill();
            Destroy(rewardText.gameObject);
        }
    }

    private void ShowContainerCoinRewardVisuals(
        Vector3 worldPosition,
        int cents)
    {
        if (roundCanvasRect == null || coinEffectLayer == null || coinHudTarget == null)
        {
            return;
        }

        Camera worldCamera = Camera.main;
        Camera canvasCamera = GetRoundCanvasCamera();

        if (worldCamera == null)
        {
            return;
        }

        Vector3 screenPosition = worldCamera.WorldToScreenPoint(worldPosition);

        if (screenPosition.z <= 0f
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                roundCanvasRect,
                screenPosition,
                canvasCamera,
                out Vector2 startPosition))
        {
            return;
        }

        AccumulateRewardNumber(startPosition, cents, true);
        QueueContainerRewardVisuals(
            worldPosition,
            cents);
    }

    private void AccumulateRewardNumber(
        Vector2 startPosition,
        int cents,
        bool playCashRegisterOnStart)
    {
        float now = Time.unscaledTime;
        bool joinsCurrentReward = activeRewardText != null
            && now - lastRewardAddedTime <= RewardAccumulationWindow;
        bool playCashRegisterNow =
            playCashRegisterOnStart && !joinsCurrentReward;

        if (!joinsCurrentReward)
        {
            ClearRewardPresentation();
            GameObject rewardObject = Instantiate(
                floatingRewardPrefab,
                coinEffectLayer);
            rewardObject.name = "Combined Coin Reward";
            activeRewardText = rewardObject.GetComponent<TMP_Text>();
            activeRewardText.enableAutoSizing = false;
            activeRewardText.textWrappingMode = TextWrappingModes.NoWrap;
            activeRewardText.overflowMode = TextOverflowModes.Overflow;
            activeRewardText.rectTransform.sizeDelta =
                new Vector2(1100f, 90f);
            activeRewardStartPosition = startPosition;
            accumulatedRewardCents = 0;
            lastRewardAddedTime = now;
            rewardDisplayCoroutine = StartCoroutine(AnimateRewardNumber());
        }

        accumulatedRewardCents = SaturatingAdd(
            accumulatedRewardCents,
            cents);
        lastRewardAddedTime = now;
        activeRewardStartPosition = Vector2.Lerp(
            activeRewardStartPosition,
            startPosition,
            0.35f);
        activeRewardText.text = $"+{FormatMoney(accumulatedRewardCents)}";
        activeRewardText.fontSize = Mathf.Clamp(
            34f + (float)Math.Log10(
                1d + accumulatedRewardCents / 100d) * 12f,
            34f,
            58f);
        activeRewardText.rectTransform.anchoredPosition =
            activeRewardStartPosition;
        activeRewardText.rectTransform.DOKill();
        activeRewardText.rectTransform.localScale = Vector3.one;
        activeRewardText.rectTransform.DOPunchScale(
            Vector3.one * 0.24f,
            0.3f,
            7,
            0.55f);
        if (playCashRegisterNow)
        {
            PlayCashRegisterSfx();
        }
    }

    private IEnumerator AnimateRewardNumber()
    {
        while (activeRewardText != null
            && Time.unscaledTime - lastRewardAddedTime
                < RewardAccumulationWindow)
        {
            yield return null;
        }

        const float duration = 0.45f;
        float elapsed = 0f;

        while (elapsed < duration && activeRewardText != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            activeRewardText.rectTransform.anchoredPosition =
                activeRewardStartPosition
                + Vector2.up * Mathf.Lerp(0f, 54f, progress);
            Color color = activeRewardText.color;
            color.a = 1f - Mathf.SmoothStep(0f, 1f, progress);
            activeRewardText.color = color;
            yield return null;
        }

        if (activeRewardText != null)
        {
            activeRewardText.rectTransform.DOKill();
            Destroy(activeRewardText.gameObject);
        }

        activeRewardText = null;
        rewardDisplayCoroutine = null;
        accumulatedRewardCents = 0;
    }

    private void ClearRewardPresentation()
    {
        if (rewardDisplayCoroutine != null)
        {
            StopCoroutine(rewardDisplayCoroutine);
            rewardDisplayCoroutine = null;
        }

        if (activeRewardText != null)
        {
            activeRewardText.rectTransform.DOKill();
            Destroy(activeRewardText.gameObject);
            activeRewardText = null;
        }

        accumulatedRewardCents = 0;
    }

    private void QueueContainerRewardVisuals(
        Vector3 startWorldPosition,
        int cents)
    {
        // Deposits can arrive several times in one frame. Keep the exact latest
        // world anchor instead of averaging screen/canvas coordinates together.
        containerRewardStartWorldPosition = startWorldPosition;

        float now = Time.unscaledTime;
        if (now - lastContainerRewardSequenceTime > RewardAccumulationWindow)
        {
            containerRewardSequenceCents = 0;
        }

        containerRewardSequenceCents = SaturatingAdd(
            containerRewardSequenceCents,
            cents);
        lastContainerRewardSequenceTime = now;
        accumulatedContainerRewardCents = SaturatingAdd(
            accumulatedContainerRewardCents,
            cents);
        lastContainerRewardAddedTime = now;

        if (containerRewardVisualCoroutine == null)
        {
            containerRewardVisualCoroutine =
                StartCoroutine(FlushContainerRewardVisuals());
        }
    }

    private IEnumerator FlushContainerRewardVisuals()
    {
        while (Time.unscaledTime - lastContainerRewardAddedTime
            < ContainerRewardVisualDelay)
        {
            yield return null;
        }

        long rewardCents = accumulatedContainerRewardCents;
        long denominationCents =
            Math.Max(rewardCents, containerRewardSequenceCents);
        Vector3 startWorldPosition = containerRewardStartWorldPosition;
        Vector2 targetScreenPosition = GetCashHudTargetScreenPosition();
        accumulatedContainerRewardCents = 0;
        containerRewardVisualCoroutine = null;

        int rewardParticleCount = CalculateRewardParticleCount(rewardCents);
        CalculateRewardParticleMix(
            denominationCents,
            rewardParticleCount,
            out int coinParticleCount,
            out int cashNoteParticleCount);
        SpawnRewardParticles(
            startWorldPosition,
            targetScreenPosition,
            coinParticleCount,
            cashNoteParticleCount);
    }

    private void CalculateRewardParticleMix(
        long denominationCents,
        int rewardParticleCount,
        out int coinParticleCount,
        out int cashNoteParticleCount)
    {
        bool canShowCashNotes = CanShowCashNotes();
        float cashBlend = 0f;
        if (forceCashNotesForTesting && canShowCashNotes)
        {
            cashBlend = 1f;
        }
        else if (denominationCents >= cashTransitionStartCents
            && canShowCashNotes)
        {
            cashBlend = denominationCents >= cashRewardThresholdCents
                ? MaximumCashBlend
                : Mathf.Lerp(
                    0.25f,
                    MaximumCashBlend,
                    Mathf.InverseLerp(
                    cashTransitionStartCents,
                    cashRewardThresholdCents,
                    (float)Math.Min(
                        denominationCents,
                        cashRewardThresholdCents)));
        }

        int requestedCashNoteCount = cashBlend > 0f
            ? Mathf.Clamp(
                Mathf.RoundToInt(rewardParticleCount * cashBlend),
                1,
                rewardParticleCount)
            : 0;
        cashNoteParticleCount =
            CalculateCashNoteParticleCount(requestedCashNoteCount);
        coinParticleCount =
            rewardParticleCount - requestedCashNoteCount;
    }

    private int CalculateRewardParticleCount(long rewardCents)
    {
        double rewardDollars = Math.Max(
            1d,
            rewardCents / 100d);
        double magnitude = Math.Log10(rewardDollars + 1d);
        double largeRewardFalloff = 1d / (
            1d + Math.Max(0d, magnitude - 3d) * 0.35d);
        int scaledParticleCount = 30 + Mathf.RoundToInt(
            (float)(magnitude * magnitude * 35d * largeRewardFalloff));
        return Mathf.Clamp(
            scaledParticleCount,
            1,
            Mathf.Min(
                maximumRewardParticlesPerBurst,
                MaximumUsefulRewardParticlesPerBurst));
    }

    private int CalculateCashNoteParticleCount(int requestedCount)
    {
        if (requestedCount <= 0)
        {
            return 0;
        }

        return Mathf.Clamp(
            Mathf.RoundToInt(requestedCount * cashNoteParticleDensity),
            1,
            Mathf.Min(requestedCount, maximumCashNotesPerBurst));
    }

    private void SpawnRewardParticles(
        Vector3 startWorldPosition,
        Vector2 targetScreenPosition,
        int coinCount,
        int cashNoteCount)
    {
        if (coinCount + cashNoteCount <= 0)
        {
            return;
        }

        if (cashNoteCount > 0 && cashRewardParticleMaterial != null)
        {
            cashRewardParticleMaterial.SetColor(
                "_LightingAmbientColor",
                RenderSettings.ambientLight);
        }

        Camera worldCamera = Camera.main;
        if (worldCamera == null)
        {
            return;
        }

        ScheduleRewardParticleTrail(
            coinRewardParticleSystem,
            coinCount,
            startWorldPosition,
            targetScreenPosition,
            CoinParticlePixelSize,
            coinRewardEmitterRadiusPixels,
            false);
        ScheduleRewardParticleTrail(
            cashRewardParticleSystem,
            cashNoteCount,
            startWorldPosition,
            targetScreenPosition,
            CashParticlePixelSize,
            cashRewardEmitterRadiusPixels,
            true);

        activeCoinAnimations = 1;
    }

    private void ClearPendingContainerRewardVisuals()
    {
        if (containerRewardVisualCoroutine != null)
        {
            StopCoroutine(containerRewardVisualCoroutine);
            containerRewardVisualCoroutine = null;
        }

        accumulatedContainerRewardCents = 0;
        containerRewardSequenceCents = 0;
        lastContainerRewardSequenceTime = 0f;
    }

    private void HandleFocusedContainerChanged(EggContainer container)
    {
        ClearRewardPresentation();
        ClearPendingContainerRewardVisuals();
        pendingRewardParticleTrails.Clear();
        pendingRewardParticleLandings.Clear();
        pendingCoinAudioArrivals = 0;
        pendingCashAudioArrivals = 0;
        rewardParticleEmissionAllowance = 0f;
        activeCoinAnimations = 0;
        coinRewardParticleSystem?.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear);
        cashRewardParticleSystem?.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static bool IsFocusedPen(int penIndex)
    {
        PenExpansionManager manager = PenExpansionManager.Instance;
        return manager == null
            ? penIndex <= 0
            : penIndex == manager.FocusedPenIndex;
    }

    private void InitializeRewardParticleSystems()
    {
        Camera worldCamera = Camera.main;
        UiModelGraphic coinGraphic = flyingCoinPrefab != null
            ? flyingCoinPrefab.GetComponent<UiModelGraphic>()
            : null;
        Shader particleShader = rewardParticleShader != null
            ? rewardParticleShader
            : Shader.Find(RewardParticleShaderName);
        if (worldCamera == null
            || coinGraphic == null
            || coinGraphic.SourceModel == null
            || particleShader == null)
        {
            Debug.LogError(
                "Cash reward particles need the main camera, flying coin " +
                $"prefab, and {RewardParticleShaderName} shader.",
                this);
            return;
        }

        Mesh coinMesh = CreateProjectedParticleMesh(
            coinGraphic.SourceModel,
            "Coin Reward Particle Mesh");
        if (coinMesh == null)
        {
            Debug.LogError(
                "The flying coin source model has no readable mesh.",
                this);
            return;
        }

        rewardParticleMeshes.Add(coinMesh);
        coinRewardParticleMaterial = CreateRewardParticleMaterial(
            particleShader,
            coinGraphic.material,
            "Coin Reward Particle Material",
            true,
            null,
            Color.black,
            1f);
        coinRewardParticleSystem = CreateRewardParticleSystem(
            "Coin Reward Particles",
            worldCamera.transform,
            new[] { coinMesh },
            coinRewardParticleMaterial,
            new Vector3(0f, 180f, 0f),
            new Vector3(0f, 1f, 0f),
            flyingCoinSpinDegreesPerSecond,
            flyingCoinSpinDegreesPerSecond);

        List<Mesh> cashMeshes = new List<Mesh>();
        if (flyingCashModels != null)
        {
            for (int index = 0; index < flyingCashModels.Length; index++)
            {
                Mesh cashMesh = CreateProjectedParticleMesh(
                    flyingCashModels[index],
                    $"Cash Reward Particle Mesh {index + 1}");
                if (cashMesh != null)
                {
                    cashMeshes.Add(cashMesh);
                    rewardParticleMeshes.Add(cashMesh);
                }
            }
        }

        if (cashMeshes.Count == 0)
        {
            for (int index = 0; index < 2; index++)
            {
                Mesh cashMesh = CreateFallbackCashParticleMesh(index);
                cashMeshes.Add(cashMesh);
                rewardParticleMeshes.Add(cashMesh);
            }
        }

        if (flyingCashMaterial == null)
        {
            Debug.LogWarning(
                "Cash note particles are unavailable because no cash " +
                "material is assigned.",
                this);
            return;
        }

        cashRewardParticleMaterial = CreateRewardParticleMaterial(
            particleShader,
            flyingCashMaterial,
            "Cash Reward Particle Material",
            false,
            flyingCashLightingMatCap,
            RenderSettings.ambientLight,
            flyingCashMatCapLightStrength);
        cashRewardParticleSystem = CreateRewardParticleSystem(
            "Cash Note Reward Particles",
            worldCamera.transform,
            cashMeshes.ToArray(),
            cashRewardParticleMaterial,
            new Vector3(30f, 30f, 30f),
            new Vector3(0.4f, 1f, 0.65f),
            flyingCashMinimumSpinDegreesPerSecond,
            flyingCashMaximumSpinDegreesPerSecond);
    }

    private static ParticleSystem CreateRewardParticleSystem(
        string objectName,
        Transform cameraTransform,
        Mesh[] meshes,
        Material material,
        Vector3 maximumStartRotationDegrees,
        Vector3 rotationAxisWeights,
        float minimumSpinDegreesPerSecond,
        float maximumSpinDegreesPerSecond)
    {
        GameObject particleObject = new GameObject(objectName);
        particleObject.transform.SetParent(cameraTransform, false);
        ParticleSystem particleSystem =
            particleObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particleSystem.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = RewardFlightDuration;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(
            -maximumStartRotationDegrees.x * Mathf.Deg2Rad,
            maximumStartRotationDegrees.x * Mathf.Deg2Rad);
        main.startRotationY = new ParticleSystem.MinMaxCurve(
            -maximumStartRotationDegrees.y * Mathf.Deg2Rad,
            maximumStartRotationDegrees.y * Mathf.Deg2Rad);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(
            -maximumStartRotationDegrees.z * Mathf.Deg2Rad,
            maximumStartRotationDegrees.z * Mathf.Deg2Rad);
        main.maxParticles = RewardParticleCapacity;

        ParticleSystem.EmissionModule emission = particleSystem.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = particleSystem.shape;
        shape.enabled = false;
        ParticleSystem.RotationOverLifetimeModule rotation =
            particleSystem.rotationOverLifetime;
        rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(
            minimumSpinDegreesPerSecond
                * rotationAxisWeights.x * Mathf.Deg2Rad,
            maximumSpinDegreesPerSecond
                * rotationAxisWeights.x * Mathf.Deg2Rad);
        rotation.y = new ParticleSystem.MinMaxCurve(
            minimumSpinDegreesPerSecond
                * rotationAxisWeights.y * Mathf.Deg2Rad,
            maximumSpinDegreesPerSecond
                * rotationAxisWeights.y * Mathf.Deg2Rad);
        rotation.z = new ParticleSystem.MinMaxCurve(
            minimumSpinDegreesPerSecond
                * rotationAxisWeights.z * Mathf.Deg2Rad,
            maximumSpinDegreesPerSecond
                * rotationAxisWeights.z * Mathf.Deg2Rad);

        ParticleSystemRenderer particleRenderer =
            particleObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        particleRenderer.alignment = ParticleSystemRenderSpace.Local;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;
        particleRenderer.sortingOrder = 1000;
        particleRenderer.enableGPUInstancing = true;
        particleRenderer.sharedMaterial = material;
        particleRenderer.meshDistribution =
            ParticleSystemMeshDistribution.UniformRandom;
        particleRenderer.SetMeshes(meshes, meshes.Length);

        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particleSystem;
    }

    private void ScheduleRewardParticleTrail(
        ParticleSystem particleSystem,
        int particleCount,
        Vector3 startWorldPosition,
        Vector2 targetScreenPosition,
        float pixelSize,
        float emitterRadiusPixels,
        bool isCashNote)
    {
        if (particleSystem == null || particleCount <= 0)
        {
            return;
        }

        particleSystem.Play();
        float countScale = Mathf.Clamp01(
            particleCount / (float)maximumRewardParticlesPerBurst);
        float trailDuration = rewardParticleTrailDuration
            * Mathf.Lerp(0.75f, 1.15f, countScale);
        pendingRewardParticleTrails.Add(new RewardParticleTrail
        {
            ParticleSystem = particleSystem,
            RemainingCount = particleCount,
            StartWorldPosition = startWorldPosition,
            TargetScreenPosition = targetScreenPosition,
            PixelSize = pixelSize,
            EmitterRadiusPixels = emitterRadiusPixels,
            EmissionInterval = trailDuration
                / Mathf.Max(1, particleCount - 1),
            NextEmissionTime = Time.time
                + UnityEngine.Random.value
                    * trailDuration
                    / Mathf.Max(1, particleCount - 1),
            IsCashNote = isCashNote
        });
    }

    private void UpdatePendingRewardParticleTrails()
    {
        if (pendingRewardParticleTrails.Count == 0)
        {
            rewardParticleEmissionAllowance = 0f;
            StopCompletedRewardParticleSystem(coinRewardParticleSystem);
            StopCompletedRewardParticleSystem(cashRewardParticleSystem);
            return;
        }

        rewardParticleEmissionAllowance = Mathf.Min(
            MaximumParticleEmissionsPerFrame,
            rewardParticleEmissionAllowance
                + maximumRewardParticlesPerSecond * Time.deltaTime);
        int emissionBudget = Mathf.Min(
            MaximumParticleEmissionsPerFrame,
            Mathf.FloorToInt(rewardParticleEmissionAllowance));

        while (pendingRewardParticleTrails.Count > 0
            && emissionBudget > 0)
        {
            bool emittedInPass = false;
            for (int trailIndex =
                    pendingRewardParticleTrails.Count - 1;
                 trailIndex >= 0 && emissionBudget > 0;
                 trailIndex--)
            {
                RewardParticleTrail trail =
                    pendingRewardParticleTrails[trailIndex];
                float scheduledEmissionTime = trail.NextEmissionTime;
                if (trail.RemainingCount > 0
                    && scheduledEmissionTime <= Time.time
                    && EmitRewardParticle(
                        trail,
                        scheduledEmissionTime))
                {
                    trail.RemainingCount--;
                    trail.NextEmissionTime += trail.EmissionInterval
                        * UnityEngine.Random.Range(
                            1f - rewardParticleEmissionJitter,
                            1f + rewardParticleEmissionJitter);
                    emissionBudget--;
                    rewardParticleEmissionAllowance -= 1f;
                    emittedInPass = true;
                }

                if (trail.RemainingCount <= 0)
                {
                    pendingRewardParticleTrails.RemoveAt(trailIndex);
                }
            }

            if (!emittedInPass)
            {
                break;
            }
        }

        if (pendingRewardParticleTrails.Count == 0)
        {
            rewardParticleEmissionAllowance = 0f;
        }
        StopCompletedRewardParticleSystem(coinRewardParticleSystem);
        StopCompletedRewardParticleSystem(cashRewardParticleSystem);
    }

    private bool EmitRewardParticle(
        RewardParticleTrail trail,
        float scheduledEmissionTime)
    {
        ParticleSystem particleSystem = trail.ParticleSystem;
        if (particleSystem == null
            || particleSystem.particleCount >= RewardParticleCapacity)
        {
            return false;
        }

        Camera worldCamera = Camera.main;
        if (worldCamera == null)
        {
            return false;
        }

        Vector3 startScreenPosition =
            worldCamera.WorldToScreenPoint(trail.StartWorldPosition);
        if (startScreenPosition.z <= 0f)
        {
            return false;
        }

        float planeDistance = Mathf.Max(
            worldCamera.nearClipPlane + 0.25f,
            RewardParticlePlaneDistance);
        Vector3 targetPosition = ScreenToCameraLocalPoint(
            worldCamera,
            trail.TargetScreenPosition,
            planeDistance);
        float worldUnitsPerPixel = GetWorldUnitsPerPixel(
            worldCamera,
            planeDistance);
        float arcHeight = 90f * worldUnitsPerPixel;
        float baseAcceleration =
            -8f * arcHeight / (RewardFlightDuration * RewardFlightDuration);
        ParticleSystem.ForceOverLifetimeModule force =
            particleSystem.forceOverLifetime;
        force.enabled = true;
        force.space = ParticleSystemSimulationSpace.Local;
        force.y = baseAcceleration;

        float lifetime = RewardFlightDuration;
        Vector3 startPosition = ScreenToCameraLocalPoint(
            worldCamera,
            startScreenPosition,
            planeDistance);
        Vector2 emitterOffset = UnityEngine.Random.insideUnitCircle
            * trail.EmitterRadiusPixels
            * worldUnitsPerPixel;
        startPosition += new Vector3(
            emitterOffset.x,
            emitterOffset.y,
            0f);
        Vector3 initialVelocity =
            (targetPosition - startPosition) / lifetime;
        initialVelocity.y -= 0.5f * baseAcceleration * lifetime;
        float emissionAge = Mathf.Max(
            0f,
            Time.time - scheduledEmissionTime);

        if (emissionAge >= lifetime)
        {
            pendingRewardParticleLandings.Enqueue(
                new RewardParticleLanding
                {
                    ArrivalTime = Time.time,
                    IsCashNote = trail.IsCashNote
                });
            return true;
        }

        float remainingLifetime = lifetime - emissionAge;
        Vector3 position = startPosition
            + initialVelocity * emissionAge;
        position.y += 0.5f
            * baseAcceleration
            * emissionAge
            * emissionAge;
        Vector3 velocity = initialVelocity;
        velocity.y += baseAcceleration * emissionAge;

        ParticleSystem.EmitParams emitParams =
            new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startLifetime = remainingLifetime,
                startSize = trail.PixelSize * worldUnitsPerPixel,
                startColor = Color.white,
                rotation3D = trail.IsCashNote
                    ? new Vector3(
                        UnityEngine.Random.Range(
                            -MaximumCashNoteRandomRotationDegrees,
                            MaximumCashNoteRandomRotationDegrees),
                        UnityEngine.Random.Range(
                            -MaximumCashNoteRandomRotationDegrees,
                            MaximumCashNoteRandomRotationDegrees),
                        UnityEngine.Random.Range(
                            -MaximumCashNoteRandomRotationDegrees,
                            MaximumCashNoteRandomRotationDegrees))
                    : new Vector3(
                        0f,
                        UnityEngine.Random.Range(-180f, 180f),
                        0f)
            };
        particleSystem.Emit(emitParams, 1);
        pendingRewardParticleLandings.Enqueue(
            new RewardParticleLanding
            {
                ArrivalTime = Time.time + remainingLifetime,
                IsCashNote = trail.IsCashNote
            });
        return true;
    }

    private void StopCompletedRewardParticleSystem(
        ParticleSystem particleSystem)
    {
        if (particleSystem == null || !particleSystem.isPlaying)
        {
            return;
        }

        for (int index = 0;
             index < pendingRewardParticleTrails.Count;
             index++)
        {
            if (pendingRewardParticleTrails[index].ParticleSystem
                == particleSystem)
            {
                return;
            }
        }

        particleSystem.Stop(
            true,
            ParticleSystemStopBehavior.StopEmitting);
    }

    private void UpdateRewardParticleStatus()
    {
        UpdatePendingRewardParticleTrails();
        UpdatePendingRewardParticleLandings();
        bool hasPendingParticles =
            pendingRewardParticleTrails.Count > 0;
        bool particlesAreAlive =
            coinRewardParticleSystem != null
                && coinRewardParticleSystem.IsAlive(true)
            || cashRewardParticleSystem != null
                && cashRewardParticleSystem.IsAlive(true);
        activeCoinAnimations =
            hasPendingParticles || particlesAreAlive ? 1 : 0;
    }

    private void UpdatePendingRewardParticleLandings()
    {
        while (pendingRewardParticleLandings.Count > 0
            && pendingRewardParticleLandings.Peek().ArrivalTime
                <= Time.time)
        {
            RewardParticleLanding landing =
                pendingRewardParticleLandings.Dequeue();

            if (landing.IsCashNote)
            {
                pendingCashAudioArrivals++;
            }
            else
            {
                pendingCoinAudioArrivals++;
            }
        }

        int pendingArrivalCount =
            pendingCoinAudioArrivals + pendingCashAudioArrivals;
        if (pendingArrivalCount <= 0
            || Time.time < nextRewardLandingSoundTime)
        {
            return;
        }

        bool playCash = pendingCashAudioArrivals > 0
            && (pendingCoinAudioArrivals <= 0
                || UnityEngine.Random.value
                    < pendingCashAudioArrivals
                        / (float)pendingArrivalCount);
        float densityVolumeScale = Mathf.Min(
            1.25f,
            1f + Mathf.Log10(Mathf.Max(1, pendingArrivalCount))
                * coalescedRewardLandingVolumeBoost);

        if (playCash)
        {
            PlayCashLandingSfx(densityVolumeScale);
        }
        else
        {
            PlayCoinLandingSfx(densityVolumeScale);
        }

        pendingCoinAudioArrivals = 0;
        pendingCashAudioArrivals = 0;
        nextRewardLandingSoundTime = Time.time
            + 1f / Mathf.Max(
                1f,
                maximumRewardLandingSoundsPerSecond);
    }

    private bool CanShowCashNotes()
    {
        return cashRewardParticleSystem != null;
    }

    private static Vector3 ScreenToCameraLocalPoint(
        Camera worldCamera,
        Vector2 screenPosition,
        float planeDistance)
    {
        Vector3 worldPosition = worldCamera.ScreenToWorldPoint(
            new Vector3(
                screenPosition.x,
                screenPosition.y,
                planeDistance));
        return worldCamera.transform.InverseTransformPoint(worldPosition);
    }

    private static float GetWorldUnitsPerPixel(
        Camera worldCamera,
        float planeDistance)
    {
        if (worldCamera.orthographic)
        {
            return worldCamera.orthographicSize * 2f
                / Mathf.Max(1, worldCamera.pixelHeight);
        }

        float verticalWorldSize = 2f * planeDistance * Mathf.Tan(
            worldCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return verticalWorldSize / Mathf.Max(1, worldCamera.pixelHeight);
    }

    private static Material CreateRewardParticleMaterial(
        Shader particleShader,
        Material sourceMaterial,
        string materialName,
        bool useMatCap,
        Texture lightingMatCap,
        Color lightingAmbientColor,
        float lightingMatCapStrength)
    {
        Material material = new Material(particleShader)
        {
            name = materialName,
            enableInstancing = true
        };
        if (sourceMaterial != null)
        {
            Texture texture = sourceMaterial.GetTexture("_MainTex");
            if (texture == null)
            {
                texture = sourceMaterial.GetTexture("_BaseMap");
            }
            if (texture == null)
            {
                texture = sourceMaterial.GetTexture("_MatCap");
            }

            material.SetTexture("_MainTex", texture);
            material.SetFloat("_UseMatCap", useMatCap ? 1f : 0f);
            material.SetFloat(
                "_UseLightingMatCap",
                useMatCap ? 0f : 1f);
            material.SetFloat(
                "_HasCustomLightingMatCap",
                lightingMatCap != null ? 1f : 0f);
            material.SetColor(
                "_LightingAmbientColor",
                lightingAmbientColor);
            material.SetFloat(
                "_LightingMatCapStrength",
                lightingMatCapStrength);
            if (lightingMatCap != null)
            {
                material.SetTexture(
                    "_LightingMatCap",
                    lightingMatCap);
            }
            if (sourceMaterial.HasProperty("_Color"))
            {
                material.SetColor(
                    "_Color",
                    sourceMaterial.GetColor("_Color"));
            }
        }

        return material;
    }

    private static Mesh CreateProjectedParticleMesh(
        GameObject sourceModel,
        string meshName)
    {
        if (sourceModel == null)
        {
            return null;
        }

        MeshFilter[] meshFilters;
        Matrix4x4 rootWorldToLocal;
        try
        {
            meshFilters =
                sourceModel.GetComponentsInChildren<MeshFilter>(true);
            rootWorldToLocal = sourceModel.transform.worldToLocalMatrix;
        }
        catch (MissingReferenceException)
        {
            // Asset reimports can invalidate an editor preview's serialized
            // model reference between the null check and component lookup.
            return null;
        }

        List<CombineInstance> combineInstances =
            new List<CombineInstance>(meshFilters.Length);
        for (int index = 0; index < meshFilters.Length; index++)
        {
            MeshFilter meshFilter = meshFilters[index];
            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null || !mesh.isReadable)
            {
                continue;
            }

            combineInstances.Add(new CombineInstance
            {
                mesh = mesh,
                transform = rootWorldToLocal
                    * meshFilter.transform.localToWorldMatrix
            });
        }

        if (combineInstances.Count == 0)
        {
            return null;
        }

        Mesh particleMesh = new Mesh
        {
            name = meshName,
            indexFormat = IndexFormat.UInt32
        };
        particleMesh.CombineMeshes(
            combineInstances.ToArray(),
            true,
            true,
            false);

        Bounds bounds = particleMesh.bounds;
        GetProjectionAxes(
            bounds.size,
            out int horizontalAxis,
            out int verticalAxis,
            out int depthAxis);
        float maximumProjectedSize = Mathf.Max(
            GetAxis(bounds.size, horizontalAxis),
            GetAxis(bounds.size, verticalAxis));
        float inverseSize = maximumProjectedSize > Mathf.Epsilon
            ? 1f / maximumProjectedSize
            : 1f;

        Vector3[] vertices = particleMesh.vertices;
        for (int index = 0; index < vertices.Length; index++)
        {
            Vector3 centered = vertices[index] - bounds.center;
            vertices[index] = new Vector3(
                GetAxis(centered, horizontalAxis),
                GetAxis(centered, verticalAxis),
                GetAxis(centered, depthAxis)) * inverseSize;
        }
        particleMesh.vertices = vertices;
        // The axis remap can include a reflection, so imported normals can
        // disagree with the transformed triangle winding. Rebuild the final
        // shading basis from the geometry the particle renderer actually uses.
        particleMesh.RecalculateNormals();
        particleMesh.RecalculateTangents();
        particleMesh.RecalculateBounds();
        return particleMesh;
    }

    private static Mesh CreateFallbackCashParticleMesh(int variant)
    {
        const int HorizontalSegments = 4;
        const int VerticalSegments = 2;
        const float NoteHeight = 0.46f;
        int verticesPerRow = HorizontalSegments + 1;
        Vector3[] vertices = new Vector3[
            verticesPerRow * (VerticalSegments + 1)];
        Vector2[] uvs = new Vector2[vertices.Length];
        Color32[] colors = new Color32[vertices.Length];

        for (int row = 0; row <= VerticalSegments; row++)
        {
            float vertical01 = row / (float)VerticalSegments;
            for (int column = 0; column <= HorizontalSegments; column++)
            {
                float horizontal01 = column / (float)HorizontalSegments;
                float x = horizontal01 - 0.5f;
                float y = (vertical01 - 0.5f) * NoteHeight;
                float z = variant == 0
                    ? x * x * 0.32f - 0.04f
                    : Mathf.Sin(horizontal01 * Mathf.PI * 2f) * 0.055f
                        + x * (vertical01 - 0.5f) * 0.08f;
                int vertexIndex = row * verticesPerRow + column;
                vertices[vertexIndex] = new Vector3(x, y, z);
                uvs[vertexIndex] = new Vector2(horizontal01, vertical01);
                colors[vertexIndex] = new Color32(255, 255, 255, 255);
            }
        }

        int[] triangles = new int[
            HorizontalSegments * VerticalSegments * 6];
        int triangleIndex = 0;
        for (int row = 0; row < VerticalSegments; row++)
        {
            for (int column = 0; column < HorizontalSegments; column++)
            {
                int lowerLeft = row * verticesPerRow + column;
                int lowerRight = lowerLeft + 1;
                int upperLeft = lowerLeft + verticesPerRow;
                int upperRight = upperLeft + 1;
                triangles[triangleIndex++] = lowerLeft;
                triangles[triangleIndex++] = lowerRight;
                triangles[triangleIndex++] = upperLeft;
                triangles[triangleIndex++] = lowerRight;
                triangles[triangleIndex++] = upperRight;
                triangles[triangleIndex++] = upperLeft;
            }
        }

        Mesh mesh = new Mesh
        {
            name = $"Generated Cash Reward Particle Mesh {variant + 1}",
            vertices = vertices,
            uv = uvs,
            colors32 = colors,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void GetProjectionAxes(
        Vector3 size,
        out int horizontalAxis,
        out int verticalAxis,
        out int depthAxis)
    {
        horizontalAxis = 0;
        if (size.y > size.x && size.y >= size.z)
        {
            horizontalAxis = 1;
        }
        else if (size.z > size.x && size.z > size.y)
        {
            horizontalAxis = 2;
        }

        if (horizontalAxis == 0)
        {
            verticalAxis = size.y >= size.z ? 1 : 2;
        }
        else if (horizontalAxis == 1)
        {
            verticalAxis = size.x >= size.z ? 0 : 2;
        }
        else
        {
            verticalAxis = size.x >= size.y ? 0 : 1;
        }

        depthAxis = 3 - horizontalAxis - verticalAxis;
    }

    private static float GetAxis(Vector3 vector, int axis)
    {
        return axis == 0
            ? vector.x
            : axis == 1
                ? vector.y
                : vector.z;
    }

    private void DestroyRewardParticleSystems()
    {
        pendingRewardParticleTrails.Clear();
        pendingRewardParticleLandings.Clear();
        pendingCoinAudioArrivals = 0;
        pendingCashAudioArrivals = 0;
        nextRewardLandingSoundTime = 0f;
        rewardParticleEmissionAllowance = 0f;
        activeCoinAnimations = 0;

        if (coinRewardParticleSystem != null)
        {
            Destroy(coinRewardParticleSystem.gameObject);
            coinRewardParticleSystem = null;
        }
        if (cashRewardParticleSystem != null)
        {
            Destroy(cashRewardParticleSystem.gameObject);
            cashRewardParticleSystem = null;
        }

        if (coinRewardParticleMaterial != null)
        {
            Destroy(coinRewardParticleMaterial);
            coinRewardParticleMaterial = null;
        }
        if (cashRewardParticleMaterial != null)
        {
            Destroy(cashRewardParticleMaterial);
            cashRewardParticleMaterial = null;
        }

        for (int index = 0; index < rewardParticleMeshes.Count; index++)
        {
            if (rewardParticleMeshes[index] != null)
            {
                Destroy(rewardParticleMeshes[index]);
            }
        }
        rewardParticleMeshes.Clear();
    }

    private Camera GetRoundCanvasCamera()
    {
        if (roundCanvasRect == null)
        {
            return null;
        }

        Canvas roundCanvas = roundCanvasRect.GetComponent<Canvas>();

        if (roundCanvas == null)
        {
            roundCanvas = roundCanvasRect.GetComponentInParent<Canvas>();
        }

        if (roundCanvas == null
            || roundCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return roundCanvas.worldCamera != null
            ? roundCanvas.worldCamera
            : Camera.main;
    }

    private Vector2 GetCashHudTargetScreenPosition()
    {
        RectTransform target = gameplayCashHudTarget != null
            ? gameplayCashHudTarget
            : coinHudTarget;
        if (target == null)
        {
            return new Vector2(Screen.width, Screen.height);
        }

        Canvas targetCanvas = target.GetComponentInParent<Canvas>();
        Camera targetCamera = null;
        if (targetCanvas != null
            && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            targetCamera = targetCanvas.worldCamera != null
                ? targetCanvas.worldCamera
                : Camera.main;
        }

        // Transform the center of the live cash text instead of aiming at a
        // duplicated marker whose position can drift at other aspect ratios.
        Vector3 targetWorldPosition = target.TransformPoint(target.rect.center);
        return RectTransformUtility.WorldToScreenPoint(
            targetCamera,
            targetWorldPosition);
    }

    private void HandleEggLaid()
    {
        if (Phase == RoundPhase.InProgress)
        {
            roundEggsLaid++;
        }
    }

    private void HandleChickenHatched()
    {
        if (Phase == RoundPhase.InProgress)
        {
            roundChickensHatched++;
        }
    }

    private void HandleEggsAccepted(int count)
    {
        if (IsRoundAcceptingEggs)
        {
            roundEggsIncubated += Mathf.Max(0, count);
        }
    }

    private void HandleBalanceChanged(long _)
    {
        if (Phase == RoundPhase.SuppliesShop)
        {
            AnimateShopBalanceTo(EggScoreHud.CurrentCents);
            RefreshShopUi();
        }
    }

    private void HandleProgressionChanged()
    {
        if (Phase == RoundPhase.SuppliesShop)
        {
            RefreshShopUi();
        }
    }

    private void RefreshSupplyShopTabIndicators()
    {
        if (suppliesShopScreen != null
            && suppliesShopScreen.GetComponentInChildren<SupplyShopGraphController>(true)
                != null)
        {
            return;
        }

        RectTransform card = suppliesShopScreen != null
            ? suppliesShopScreen.transform.Find("Supplies") as RectTransform
            : null;
        RectTransform treeContent = card != null
            ? card.Find("Progression Scroll View/Tree Viewport/Tree Content")
                as RectTransform
            : null;
        if (treeContent == null)
        {
            return;
        }

        RectTransform[] headers =
        {
            treeContent.Find("FOOD Branch") as RectTransform,
            treeContent.Find("TECH Branch") as RectTransform,
            treeContent.Find("COLLECTION Branch") as RectTransform
        };
        RectTransform[] groups =
        {
            treeContent.Find("Food Tree Group") as RectTransform,
            treeContent.Find("Tech Tree Group") as RectTransform,
            treeContent.Find("Collection Tree Group") as RectTransform
        };
        int selectedIndex = 0;
        for (int index = 0; index < groups.Length; index++)
        {
            if (groups[index] != null && groups[index].gameObject.activeSelf)
            {
                selectedIndex = index;
                break;
            }
        }
        RefreshSupplyShopTabIndicators(headers, groups, selectedIndex);
    }

    public void AnimateShopSpend(long cents)
    {
        if (cents <= 0
            || Phase != RoundPhase.SuppliesShop
            || shopBalanceText == null
            || floatingRewardPrefab == null)
        {
            return;
        }

        Camera canvasCamera = GetRoundCanvasCamera();
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(
            canvasCamera,
            shopBalanceText.rectTransform.TransformPoint(
                shopBalanceText.rectTransform.rect.center));
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                roundCanvasRect,
                screenPoint,
                canvasCamera,
                out Vector2 localPoint))
        {
            return;
        }

        GameObject spendObject = Instantiate(floatingRewardPrefab, roundCanvasRect);
        spendObject.name = "Shop Spend Reward";
        spendObject.transform.SetAsLastSibling();
        TMP_Text spendText = spendObject.GetComponent<TMP_Text>();
        spendText.text = $"-{FormatMoney(cents)}";
        spendText.color = new Color(1f, 0.31f, 0.22f);
        spendText.fontSize = Mathf.Clamp(
            30f + Mathf.Log10(1f + cents / 100f) * 6f,
            30f,
            48f);
        RectTransform spendRect = spendText.rectTransform;
        spendRect.anchoredPosition = localPoint + new Vector2(0f, -8f);
        spendRect.localScale = Vector3.one;
        Sequence spendSequence = DOTween.Sequence()
            .SetUpdate(true)
            .SetTarget(spendObject);
        spendSequence.Append(
            spendRect.DOPunchScale(Vector3.one * 0.22f, 0.24f, 6, 0.55f));
        spendSequence.Join(
            spendRect.DOAnchorPosY(spendRect.anchoredPosition.y + 62f, 0.72f)
                .SetEase(Ease.OutCubic));
        spendSequence.Join(
            spendText.DOFade(0f, 0.72f)
                .SetEase(Ease.InQuad));
        spendSequence.OnComplete(() => Destroy(spendObject));
    }

    private void SetShopBalanceImmediate(long cents)
    {
        shopBalanceTween?.Kill();
        shopBalanceTween = null;
        shopDisplayedBalanceCents = Math.Max(0L, cents);
        if (shopBalanceText != null)
        {
            shopBalanceText.text = FormatMoney(shopDisplayedBalanceCents);
        }
    }

    private void AnimateShopBalanceTo(long targetCents)
    {
        targetCents = Math.Max(0L, targetCents);
        shopBalanceTween?.Kill();
        shopBalanceTween = DOTween.To(
                () => shopDisplayedBalanceCents,
                value =>
                {
                    shopDisplayedBalanceCents = value;
                    if (shopBalanceText != null)
                    {
                        shopBalanceText.text = FormatMoney(value);
                    }
                },
                targetCents,
                0.55f)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .SetTarget(this)
            .OnComplete(() => shopBalanceTween = null);
    }

    private void RefreshLiveStats()
    {
        float eggsPerMinute = roundElapsed > 0.01f
            ? roundEggsCollected * 60f / roundElapsed
            : 0f;
        string[] values =
        {
            roundEggsCollected.ToString(),
            $"{eggsPerMinute:0}",
            FormatMoney(EggScoreHud.CurrentCents),
            CountChickens().ToString(),
            trucksFilled.ToString(),
            FormatWeight(totalCollectedEggWeight)
        };

        bool hasRows = liveStatRowValues[0] != null;
        if (hasRows)
        {
            for (int index = 0; index < liveStatRowValues.Length; index++)
            {
                if (liveStatRowValues[index] != null)
                {
                    liveStatRowValues[index].text = values[index];
                }
            }
        }
        else if (liveStatsValueText != null)
        {
            liveStatsValueText.text = string.Join("\n", values);
        }
    }

    private static string FormatWeight(double weight)
    {
        if (weight >= 1000000d)
        {
            return $"{weight / 1000000d:0.##}M KG";
        }

        if (weight >= 1000d)
        {
            return $"{weight / 1000d:0.##}K KG";
        }

        return $"{weight:0.##} KG";
    }

#if UNITY_EDITOR
    // Legacy editor-only migration helpers. Gameplay uses the serialized
    // objects in prefab_RoundSystem and never constructs this HUD at runtime.
    private void ApplyGameplayHudVisualPolish()
    {
        if (liveStatsDisplay == null)
        {
            return;
        }

        RectTransform statsRect =
            liveStatsDisplay.GetComponent<RectTransform>();
        statsRect.anchorMin = Vector2.one;
        statsRect.anchorMax = Vector2.one;
        statsRect.pivot = Vector2.one;
        statsRect.anchoredPosition = new Vector2(-24f, -24f);
        statsRect.sizeDelta = new Vector2(260f, 198f);

        Image outer = liveStatsDisplay.GetComponent<Image>();
        if (outer != null)
        {
            outer.sprite = GetHudRoundedSprite();
            outer.type = Image.Type.Sliced;
            outer.color = new Color(0.055f, 0.06f, 0.048f, 0.94f);
            outer.raycastTarget = false;
        }

        Outline outline = liveStatsDisplay.GetComponent<Outline>();
        if (outline == null)
        {
            outline = liveStatsDisplay.AddComponent<Outline>();
        }

        outline.effectColor = new Color(0.12f, 0.07f, 0.035f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = false;

        Shadow shadow = null;
        Shadow[] shadows = liveStatsDisplay.GetComponents<Shadow>();
        for (int index = 0; index < shadows.Length; index++)
        {
            if (shadows[index] != null
                && shadows[index].GetType() == typeof(Shadow))
            {
                shadow = shadows[index];
                break;
            }
        }

        if (shadow == null)
        {
            shadow = liveStatsDisplay.AddComponent<Shadow>();
        }

        shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
        shadow.effectDistance = new Vector2(3f, -4f);

        RectTransform inner =
            liveStatsDisplay.transform.Find("Stats Inner Panel")
                as RectTransform;
        if (inner == null)
        {
            GameObject innerObject = new GameObject(
                "Stats Inner Panel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            inner = innerObject.transform as RectTransform;
            inner.SetParent(liveStatsDisplay.transform, false);
        }

        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(6f, 6f);
        inner.offsetMax = new Vector2(-6f, -6f);
        Image innerImage = inner.GetComponent<Image>();
        innerImage.sprite = GetHudRoundedSprite();
        innerImage.type = Image.Type.Sliced;
        innerImage.color = new Color(0.055f, 0.06f, 0.048f, 0.98f);
        innerImage.raycastTarget = false;
        inner.SetAsFirstSibling();

        if (liveStatsText != null)
        {
            liveStatsText.gameObject.SetActive(false);
        }

        if (liveStatsValueText != null)
        {
            liveStatsValueText.gameObject.SetActive(false);
        }

        Texture2D atlas = Resources.Load<Texture2D>("UI/HudIconAtlas");
        if (atlas != null)
        {
            string[] labels =
            {
                "EGGS",
                "EGGS / MIN",
                "CASH",
                "CHICKENS",
                "TRUCKS"
            };

            for (int index = 0; index < labels.Length; index++)
            {
                Transform oldIcon =
                    liveStatsDisplay.transform.Find($"HUD Stat Icon {index}");
                if (oldIcon != null)
                {
                    oldIcon.gameObject.SetActive(false);
                }

                liveStatRowValues[index] = EnsureHudStatRow(
                    liveStatsDisplay.transform,
                    index,
                    labels[index],
                    atlas);
            }
        }

        if (coinHudTarget != null)
        {
            coinHudTarget.anchorMin = Vector2.one;
            coinHudTarget.anchorMax = Vector2.one;
            coinHudTarget.pivot = new Vector2(0.5f, 0.5f);
            coinHudTarget.anchoredPosition = new Vector2(-259f, -147f);
        }
    }

    private TMP_Text EnsureHudStatRow(
        Transform parent,
        int index,
        string label,
        Texture2D atlas)
    {
        string rowName = $"HUD Stat Row {index}";
        RectTransform row = parent.Find(rowName) as RectTransform;
        if (row == null)
        {
            GameObject rowObject = new GameObject(
                rowName,
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            rowObject.transform.SetParent(parent, false);
            row = rowObject.GetComponent<RectTransform>();
        }

        SetRuntimeRect(
            row,
            new Vector2(0f, 56f - index * 28f),
            new Vector2(228f, 26f));
        HorizontalLayoutGroup layout =
            row.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        RawImage icon = row.Find("Icon")?.GetComponent<RawImage>();
        if (icon == null)
        {
            GameObject iconObject = new GameObject(
                "Icon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage),
                typeof(LayoutElement));
            iconObject.transform.SetParent(row, false);
            icon = iconObject.GetComponent<RawImage>();
        }

        icon.texture = atlas;
        icon.uvRect = GetHudIconUv(index);
        icon.color = Color.white;
        icon.raycastTarget = false;
        LayoutElement iconLayout = icon.GetComponent<LayoutElement>();
        iconLayout.minWidth = 26f;
        iconLayout.preferredWidth = 26f;
        iconLayout.minHeight = 26f;
        iconLayout.preferredHeight = 26f;
        iconLayout.flexibleWidth = 0f;

        TMP_Text labelText = EnsureHudStatText(
            row,
            "Label",
            TextAlignmentOptions.MidlineLeft);
        labelText.text = label;
        labelText.color = Color.white;
        labelText.fontStyle = FontStyles.Bold;
        LayoutElement labelLayout =
            labelText.GetComponent<LayoutElement>();
        labelLayout.minWidth = 82f;
        labelLayout.preferredWidth = 82f;
        labelLayout.flexibleWidth = 1f;

        TMP_Text valueText = EnsureHudStatText(
            row,
            "Value",
            TextAlignmentOptions.MidlineRight);
        valueText.color = new Color(1f, 0.87f, 0.27f, 1f);
        valueText.fontStyle = FontStyles.Bold;
        LayoutElement valueLayout =
            valueText.GetComponent<LayoutElement>();
        valueLayout.minWidth = 102f;
        valueLayout.preferredWidth = 102f;
        valueLayout.flexibleWidth = 0f;
        return valueText;
    }

    private TMP_Text EnsureHudStatText(
        Transform parent,
        string objectName,
        TextAlignmentOptions alignment)
    {
        TMP_Text text = parent.Find(objectName)?.GetComponent<TMP_Text>();
        if (text == null)
        {
            GameObject textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI),
                typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);
            text = textObject.GetComponent<TMP_Text>();
        }

        text.font = liveStatsText != null
            ? liveStatsText.font
            : liveStatsValueText != null
                ? liveStatsValueText.font
                : null;
        text.fontSize = 14f;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

#endif

    public static Rect GetHudIconUv(int atlasIndex)
    {
        int clamped = Mathf.Clamp(atlasIndex, 0, 11);
        int column = clamped % 4;
        int rowFromTop = clamped / 4;
        return new Rect(
            column * 0.25f,
            (2 - rowFromTop) / 3f,
            0.25f,
            1f / 3f);
    }

    public static Sprite GetHudRoundedSprite()
    {
        if (hudRoundedSprite != null)
        {
            return hudRoundedSprite;
        }

        const int size = 48;
        const float radius = 11f;
        Texture2D texture = new Texture2D(
            size,
            size,
            TextureFormat.RGBA32,
            false);
        texture.name = "Runtime HUD Rounded Rect";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nearestX = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float nearestY = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float dx = x + 0.5f - nearestX;
                float dy = y + 0.5f - nearestY;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                byte alpha = (byte)Mathf.RoundToInt(
                    Mathf.Clamp01(radius + 0.75f - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        hudRoundedSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(12f, 12f, 12f, 12f));
        hudRoundedSprite.name = "Runtime HUD Rounded Sprite";
        return hudRoundedSprite;
    }

    private long CalculateRoundCashQuotaCents(int round)
    {
        int safeRound = Mathf.Max(1, round);
        int earlyGrowthSteps = Mathf.Min(
            safeRound - 1,
            earlyCashQuotaEndRound - 1);
        int midGrowthSteps = Mathf.Clamp(
            safeRound - earlyCashQuotaEndRound,
            0,
            midCashQuotaEndRound - earlyCashQuotaEndRound);
        int lateGrowthSteps = Mathf.Clamp(
            safeRound - midCashQuotaEndRound,
            0,
            endgameCashQuotaStartRound - midCashQuotaEndRound);
        int endgameGrowthSteps = Mathf.Clamp(
            safeRound - endgameCashQuotaStartRound,
            0,
            sustainedCashQuotaStartRound - endgameCashQuotaStartRound);
        int sustainedGrowthSteps = Mathf.Max(
            0,
            safeRound - sustainedCashQuotaStartRound);
        double target = baseRoundCashQuotaCents
            * Math.Pow(earlyCashQuotaGrowth, earlyGrowthSteps)
            * Math.Pow(midCashQuotaGrowth, midGrowthSteps)
            * Math.Pow(lateCashQuotaGrowth, lateGrowthSteps)
            * Math.Pow(endgameCashQuotaGrowth, endgameGrowthSteps)
            * Math.Pow(sustainedCashQuotaGrowth, sustainedGrowthSteps);

        // Whole-dollar targets are easier to scan during a fast 30-second round.
        double roundedToDollar = Math.Round(
            target / 100d,
            MidpointRounding.AwayFromZero) * 100d;
        double cappedTarget = Math.Min(
            maximumRoundCashQuotaCents,
            Math.Max(100d, roundedToDollar));
        return cappedTarget >= long.MaxValue
            ? long.MaxValue
            : (long)cappedTarget;
    }

    private int CalculateTruckEggTarget(int round)
    {
        int safeRound = Mathf.Max(1, round);
        int exponentialRound = Mathf.Min(
            safeRound,
            earlyTruckTargetRounds);
        float target = baseTruckEggTarget * Mathf.Pow(
            earlyTruckTargetGrowth,
            exponentialRound - 1);

        if (safeRound > earlyTruckTargetRounds)
        {
            target += (safeRound - earlyTruckTargetRounds)
                * lateTruckTargetIncreasePerRound;
        }

        return Mathf.Clamp(
            Mathf.CeilToInt(target),
            1,
            maximumTruckEggTarget);
    }

    private static int SaturatingAdd(int current, int amount)
    {
        return (int)Math.Min(
            int.MaxValue,
            Math.Max(0L, (long)current + Mathf.Max(0, amount)));
    }

    private static long SaturatingAdd(long current, long amount)
    {
        current = Math.Max(0L, current);
        amount = Math.Max(0L, amount);
        return amount > long.MaxValue - current
            ? long.MaxValue
            : current + amount;
    }

    private static long RoundPositiveCents(double cents)
    {
        if (double.IsNaN(cents) || cents <= 0d)
        {
            return 0L;
        }

        if (double.IsPositiveInfinity(cents) || cents >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)Math.Round(cents, MidpointRounding.AwayFromZero);
    }

    private static string FormatQuotaMoney(long cents)
    {
        double dollars = Math.Max(0, cents) / 100d;
        // Keep early and mid-game quotas exact. Compact labels previously used
        // one decimal for thousands, which could show both sides as "$3.1K"
        // even when the player was still short of the exact cent comparison.
        if (dollars < 100000d)
        {
            return FormatMoney(cents);
        }

        if (dollars < 1000000d)
        {
            return $"${dollars / 1000d:0.00}K";
        }

        if (dollars < 1000000000d)
        {
            return $"${dollars / 1000000d:0.000}M";
        }

        if (dollars < 1000000000000d)
        {
            return $"${dollars / 1000000000d:0.000}B";
        }

        if (dollars < 1000000000000000d)
        {
            return $"${dollars / 1000000000000d:0.000}T";
        }

        return $"${dollars / 1000000000000000d:0.000}Qa";
    }

    private static int CountChickens()
    {
        return FindObjectsByType<ChickenController>(FindObjectsSortMode.None).Length;
    }

    private void SpawnTruck()
    {
        DestroyTruck();

        if (truckVisualPrefab == null)
        {
            Debug.LogError(
                $"{nameof(RoundSystem)} is missing its truck visual prefab.",
                this);
            return;
        }

        GameObject root = new GameObject("Delivery Truck");
        GameObject visual = Instantiate(truckVisualPrefab, root.transform);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        root.name = "Delivery Truck";
        truck = root.transform;
        truckSpringAnimator = root.AddComponent<TruckSpringAnimator>();
        truckSpringAnimator.SetVisual(visual.transform);
        PlaceTruckAt(truckStart);
        truckSpringAnimator.ResetMotion();
    }

    private void PlaceTruckAt(Transform marker)
    {
        if (truck == null)
        {
            return;
        }

        Vector3 direction = truckStop != null ? truckStop.position - marker.position : marker.forward;
        Quaternion rotation = direction.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(direction.normalized, Vector3.up)
            : marker.rotation;
        truck.SetPositionAndRotation(marker.position, rotation);
    }

    private void DestroyTruck()
    {
        if (truck != null)
        {
            Destroy(truck.gameObject);
            truck = null;
        }

        truckSpringAnimator = null;
    }

#if UNITY_EDITOR
    private void BuildRoundUi()
    {
        GameObject eventSystemObject = new GameObject(
            "EventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        eventSystemObject.transform.SetParent(transform, false);

        GameObject canvasObject = new GameObject(
            "Round HUD",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        roundCanvasRect = canvasObject.GetComponent<RectTransform>();

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        intermissionScreen = CreateUiObject("Intermission Screen", canvasObject.transform).gameObject;
        StretchToParent(intermissionScreen.GetComponent<RectTransform>());
        Image backdrop = intermissionScreen.AddComponent<Image>();
        backdrop.color = new Color(0.055f, 0.075f, 0.08f, 0.86f);

        RectTransform card = CreateUiObject("Intermission Card", intermissionScreen.transform);
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(540f, 300f);
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.color = new Color(1f, 0.91f, 0.67f, 1f);

        intermissionTitle = CreateText("Round Title", card, 42f, TextAlignmentOptions.Center);
        SetRect(intermissionTitle.rectTransform, new Vector2(0f, 80f), new Vector2(500f, 62f));
        intermissionTitle.color = new Color(0.18f, 0.12f, 0.07f);
        intermissionTitle.fontStyle = FontStyles.Bold;

        TMP_Text message = CreateText(
            "Round Message",
            card,
            21f,
            TextAlignmentOptions.Center);
        message.text =
            $"The delivery truck is on its way.\n" +
            $"Get ready for a {Mathf.RoundToInt(roundDuration)} second round!";
        message.color = new Color(0.28f, 0.21f, 0.13f);
        SetRect(message.rectTransform, new Vector2(0f, 12f), new Vector2(480f, 64f));

        RectTransform buttonRect = CreateUiObject("Ready Button", card);
        SetRect(buttonRect, new Vector2(0f, -92f), new Vector2(210f, 54f));
        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = new Color(0.93f, 0.28f, 0.12f);
        readyButton = buttonRect.gameObject.AddComponent<Button>();
        readyButton.targetGraphic = buttonImage;
        readyButton.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock buttonColors = readyButton.colors;
        buttonColors.highlightedColor = new Color(1f, 0.4f, 0.19f);
        buttonColors.pressedColor = new Color(0.72f, 0.15f, 0.06f);
        readyButton.colors = buttonColors;

        TMP_Text buttonLabel = CreateText(
            "Ready Label",
            buttonRect,
            25f,
            TextAlignmentOptions.Center);
        buttonLabel.text = "READY";
        buttonLabel.color = Color.white;
        buttonLabel.fontStyle = FontStyles.Bold;
        StretchToParent(buttonLabel.rectTransform);

        intermissionShopButton = CreateButton(
            "Intermission Shop Button",
            card,
            new Vector2(120f, -92f),
            new Vector2(210f, 54f),
            "SHOP",
            new Color(0.18f, 0.54f, 0.34f));
        intermissionShopButton.gameObject.SetActive(false);

        countdownDisplay = CreateUiObject("Countdown Display", canvasObject.transform).gameObject;
        StretchToParent(countdownDisplay.GetComponent<RectTransform>());
        countdownText = CreateText(
            "Countdown Text",
            countdownDisplay.transform,
            180f,
            TextAlignmentOptions.Center);
        countdownText.color = new Color(1f, 0.86f, 0.2f);
        countdownText.fontStyle = FontStyles.Bold;
        countdownText.outlineWidth = 0.28f;
        countdownText.outlineColor = new Color32(76, 35, 12, 255);
        StretchToParent(countdownText.rectTransform);
        countdownText.raycastTarget = false;

        timerDisplay = CreateUiObject("Round Timer", canvasObject.transform).gameObject;
        RectTransform timerRect = timerDisplay.GetComponent<RectTransform>();
        timerRect.anchorMin = new Vector2(0.5f, 1f);
        timerRect.anchorMax = new Vector2(0.5f, 1f);
        timerRect.pivot = new Vector2(0.5f, 1f);
        timerRect.anchoredPosition = new Vector2(0f, -104f);
        timerRect.sizeDelta = new Vector2(200f, 78f);
        Image timerBackground = timerDisplay.AddComponent<Image>();
        StyleAuthoredHudPanel(timerDisplay, timerBackground);

        roundNumberText = CreateText(
            "Round Number",
            timerDisplay.transform,
            20f,
            TextAlignmentOptions.Center);
        roundNumberText.fontStyle = FontStyles.Bold;
        roundNumberText.color = Color.white;
        roundNumberText.raycastTarget = false;
        SetRect(
            roundNumberText.rectTransform,
            new Vector2(0f, 22f),
            new Vector2(184f, 27f));
        roundNumberText.text = "ROUND 1";

        timerText = CreateText(
            "Timer Text",
            timerDisplay.transform,
            34f,
            TextAlignmentOptions.Center);
        timerText.color = new Color(1f, 0.9f, 0.42f);
        timerText.fontStyle = FontStyles.Bold;
        timerText.raycastTarget = false;
        SetRect(
            timerText.rectTransform,
            new Vector2(0f, -13f),
            new Vector2(184f, 42f));
        timerText.text = "00:00";

        quotaDisplay = CreateUiObject(
            "Cash Quota HUD",
            canvasObject.transform).gameObject;
        RectTransform quotaRect = quotaDisplay.GetComponent<RectTransform>();
        quotaRect.anchorMin = new Vector2(0.5f, 1f);
        quotaRect.anchorMax = new Vector2(0.5f, 1f);
        quotaRect.pivot = new Vector2(0.5f, 1f);
        quotaRect.anchoredPosition = new Vector2(0f, -24f);
        quotaRect.sizeDelta = new Vector2(320f, 82f);
        Image quotaBackground = quotaDisplay.AddComponent<Image>();
        StyleAuthoredHudPanel(quotaDisplay, quotaBackground);

        quotaTitleText = CreateText(
            "Quota Title",
            quotaDisplay.transform,
            12f,
            TextAlignmentOptions.Center);
        quotaTitleText.fontStyle = FontStyles.Bold;
        quotaTitleText.color = Color.white;
        SetRect(
            quotaTitleText.rectTransform,
            new Vector2(0f, 29f),
            new Vector2(300f, 17f));
        quotaTitleText.text = "CASH QUOTA";

        quotaValueText = CreateText(
            "Quota Value",
            quotaDisplay.transform,
            24f,
            TextAlignmentOptions.Center);
        quotaValueText.fontStyle = FontStyles.Bold;
        quotaValueText.color = new Color(1f, 0.84f, 0.3f);
        quotaValueText.enableAutoSizing = true;
        quotaValueText.fontSizeMin = 14f;
        quotaValueText.fontSizeMax = 24f;
        SetRect(
            quotaValueText.rectTransform,
            new Vector2(0f, 5f),
            new Vector2(300f, 32f));
        quotaValueText.text = "$0 / $0";

        RectTransform quotaTrack = CreateUiObject(
            "Pen Contribution Track",
            quotaDisplay.transform);
        SetRect(quotaTrack, new Vector2(0f, -29f), new Vector2(292f, 8f));
        Image quotaTrackImage = quotaTrack.gameObject.AddComponent<Image>();
        quotaTrackImage.color = new Color(0.025f, 0.03f, 0.025f, 1f);
        quotaTrackImage.raycastTarget = false;
        quotaContributionFills = new Image[PenUiPalette.Count];
        for (int index = 0; index < quotaContributionFills.Length; index++)
        {
            RectTransform fillRect = CreateUiObject(
                $"Pen {index + 1} Contribution",
                quotaTrack);
            float previewStart = (float)index / PenUiPalette.Count;
            float previewEnd = (float)(index + 1) / PenUiPalette.Count;
            fillRect.anchorMin = new Vector2(previewStart, 0f);
            fillRect.anchorMax = new Vector2(previewEnd, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = PenUiPalette.GetColour(index);
            fill.raycastTarget = false;
            quotaContributionFills[index] = fill;
        }

        liveStatsDisplay = CreateUiObject("Live Round Stats", canvasObject.transform).gameObject;
        RectTransform liveStatsRect = liveStatsDisplay.GetComponent<RectTransform>();
        liveStatsRect.anchorMin = Vector2.one;
        liveStatsRect.anchorMax = Vector2.one;
        liveStatsRect.pivot = Vector2.one;
        liveStatsRect.anchoredPosition = new Vector2(-24f, -24f);
        liveStatsRect.sizeDelta = new Vector2(260f, 170f);
        Image liveStatsBackground = liveStatsDisplay.AddComponent<Image>();
        liveStatsBackground.color = new Color(0.055f, 0.065f, 0.055f, 0.82f);
        liveStatsBackground.raycastTarget = false;
        liveStatsText = CreateText(
            "Live Stats Text",
            liveStatsDisplay.transform,
            15f,
            TextAlignmentOptions.Left);
        liveStatsText.color = Color.white;
        liveStatsText.lineSpacing = 1f;
        liveStatsText.margin = new Vector4(12f, 8f, 8f, 6f);
        StretchToParent(liveStatsText.rectTransform);
        liveStatsText.text = "EGGS\nEGGS/MIN\nCASH\nCHICKENS\nTRUCKS";

        liveStatsValueText = CreateText(
            "Live Stats Values",
            liveStatsDisplay.transform,
            15f,
            TextAlignmentOptions.Right);
        liveStatsValueText.color = new Color(1f, 0.9f, 0.42f);
        liveStatsValueText.fontStyle = FontStyles.Bold;
        liveStatsValueText.lineSpacing = 1f;
        liveStatsValueText.margin = new Vector4(8f, 8f, 12f, 6f);
        StretchToParent(liveStatsValueText.rectTransform);

        coinHudTarget = CreateUiObject("Coin HUD Target", canvasObject.transform);
        coinHudTarget.anchorMin = new Vector2(0.915f, 1f);
        coinHudTarget.anchorMax = new Vector2(0.915f, 1f);
        coinHudTarget.pivot = new Vector2(0.5f, 0.5f);
        coinHudTarget.anchoredPosition = new Vector2(0f, -145f);
        coinHudTarget.sizeDelta = new Vector2(1f, 1f);

        coinEffectLayer = CreateUiObject("Coin Effects", canvasObject.transform);
        StretchToParent(coinEffectLayer);
        coinEffectLayer.SetAsLastSibling();

        BuildResultsUi(canvasObject.transform);
        BuildSuppliesShopUi(canvasObject.transform);
    }

    private static void StyleAuthoredHudPanel(
        GameObject panel,
        Image background)
    {
        background.sprite = UnityEditor.AssetDatabase
            .GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        background.type = Image.Type.Sliced;
        background.color = new Color(0.055f, 0.06f, 0.048f, 0.94f);
        background.raycastTarget = false;

        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(0.12f, 0.07f, 0.035f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = false;

        Shadow shadow = panel.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
        shadow.effectDistance = new Vector2(3f, -4f);
        shadow.useGraphicAlpha = true;
    }

    private void BuildResultsUi(Transform canvasTransform)
    {
        resultsScreen = CreateUiObject("Round Results Screen", canvasTransform).gameObject;
        StretchToParent(resultsScreen.GetComponent<RectTransform>());
        Image backdrop = resultsScreen.AddComponent<Image>();
        backdrop.color = new Color(0.035f, 0.045f, 0.04f, 0.94f);

        RectTransform card = CreateUiObject("Results Card", resultsScreen.transform);
        SetRect(card, Vector2.zero, new Vector2(650f, 570f));
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.color = new Color(0.12f, 0.15f, 0.12f, 0.98f);

        resultsTitleText = CreateText(
            "Results Title",
            card,
            42f,
            TextAlignmentOptions.Left);
        resultsTitleText.text = "ROUND 1 COMPLETE";
        resultsTitleText.fontStyle = FontStyles.Bold;
        resultsTitleText.color = new Color(1f, 0.84f, 0.3f);
        SetRect(
            resultsTitleText.rectTransform,
            new Vector2(0f, 235f),
            new Vector2(590f, 58f));

        TMP_Text subtitle = CreateText(
            "Results Subtitle",
            card,
            17f,
            TextAlignmentOptions.Left);
        subtitle.text = "DELIVERY COMPLETE";
        subtitle.color = new Color(0.66f, 0.78f, 0.65f);
        SetRect(subtitle.rectTransform, new Vector2(0f, 198f), new Vector2(590f, 28f));

        resultsCashText = CreateResultStat(card, "Cash Made", "CASH MADE", 135f);
        resultsCollectedText = CreateResultStat(card, "Eggs Collected", "EGGS COLLECTED", 90f);
        resultsLaidText = CreateResultStat(card, "Eggs Laid", "EGGS LAID", 45f);
        resultsPerMinuteText = CreateResultStat(card, "Eggs Per Minute", "EGGS PER MINUTE", 0f);
        resultsHatchedText = CreateResultStat(card, "Chickens Hatched", "CHICKENS HATCHED", -45f);
        resultsChickenCountText = CreateResultStat(card, "Chicken Count", "CHICKEN COUNT", -90f);
        resultsQuotaText = CreateResultStat(card, "Cash Quota", "CASH QUOTA", -135f);

        resultsShopButton = CreateButton(
            "Open Supplies Shop",
            card,
            new Vector2(-140f, -220f),
            new Vector2(250f, 52f),
            "SUPPLIES SHOP",
            new Color(0.18f, 0.57f, 0.34f));

        resultsContinueButton = CreateButton(
            "Continue Button",
            card,
            new Vector2(140f, -220f),
            new Vector2(250f, 52f),
            "NEXT ROUND",
            new Color(0.82f, 0.26f, 0.1f));
        resultsScreen.SetActive(false);
    }

    private TMP_Text CreateResultStat(
        Transform parent,
        string objectName,
        string labelText,
        float y)
    {
        RectTransform row = CreateUiObject($"{objectName} Row", parent);
        SetRect(row, new Vector2(0f, y), new Vector2(560f, 38f));

        TMP_Text label = CreateText(
            $"{objectName} Label",
            row,
            20f,
            TextAlignmentOptions.Left);
        label.text = labelText;
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        SetRect(label.rectTransform, new Vector2(-165f, 0f), new Vector2(230f, 34f));

        TMP_Text dots = CreateText(
            $"{objectName} Dots",
            row,
            16f,
            TextAlignmentOptions.Center);
        dots.text = "................................";
        dots.color = new Color(0.28f, 0.36f, 0.28f);
        SetRect(dots.rectTransform, new Vector2(45f, -1f), new Vector2(220f, 30f));

        TMP_Text value = CreateText(
            objectName,
            row,
            22f,
            TextAlignmentOptions.Right);
        value.color = new Color(1f, 0.84f, 0.3f);
        value.fontStyle = FontStyles.Bold;
        SetRect(value.rectTransform, new Vector2(190f, 0f), new Vector2(180f, 34f));
        return value;
    }

    private void BuildSuppliesShopUi(Transform canvasTransform)
    {
        suppliesShopScreen = CreateUiObject(
            "Supplies Shop Screen",
            canvasTransform).gameObject;
        StretchToParent(suppliesShopScreen.GetComponent<RectTransform>());
        Image backdrop = suppliesShopScreen.AddComponent<Image>();
        backdrop.color = new Color(0.025f, 0.035f, 0.03f, 0.97f);
        Button dismissPreviewButton = suppliesShopScreen.AddComponent<Button>();
        dismissPreviewButton.targetGraphic = backdrop;
        dismissPreviewButton.transition = Selectable.Transition.None;
        dismissPreviewButton.navigation = new Navigation
        {
            mode = Navigation.Mode.None
        };

        RectTransform card = CreateUiObject(
            "Supplies",
            suppliesShopScreen.transform);
        SetRect(card, Vector2.zero, new Vector2(1880f, 920f));
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.color = new Color(0.08f, 0.095f, 0.075f, 0.99f);
        ProgressionTreePreview treePreview =
            card.gameObject.AddComponent<ProgressionTreePreview>();

        TMP_Text title = CreateText(
            "Shop Title",
            card,
            38f,
            TextAlignmentOptions.Left);
        title.text = "SUPPLIES SHOP";
        title.fontStyle = FontStyles.Bold;
        title.color = new Color(1f, 0.84f, 0.3f);
        SetRect(title.rectTransform, new Vector2(-640f, 410f), new Vector2(500f, 52f));

        CreateIconBadge(
            "Balance Coin",
            card,
            new Vector2(480f, 410f),
            new Vector2(40f, 40f),
            "$",
            new Color(1f, 0.73f, 0.16f));

        shopBalanceText = CreateText(
            "Available Cash",
            card,
            30f,
            TextAlignmentOptions.Right);
        shopBalanceText.text = "$0.00";
        shopBalanceText.fontStyle = FontStyles.Bold;
        shopBalanceText.color = Color.white;
        SetRect(
            shopBalanceText.rectTransform,
            new Vector2(665f, 410f),
            new Vector2(330f, 44f));

        RectTransform scrollRoot = CreateUiObject("Progression Scroll View", card);
        SetRect(scrollRoot, new Vector2(0f, -28f), new Vector2(1830f, 800f));
        Image scrollBackground = scrollRoot.gameObject.AddComponent<Image>();
        scrollBackground.color = new Color(0f, 0f, 0f, 0.001f);
        ScrollRect treeScroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        treeScroll.horizontal = false;
        treeScroll.vertical = true;
        treeScroll.movementType = ScrollRect.MovementType.Clamped;
        treeScroll.inertia = true;
        treeScroll.decelerationRate = 0.08f;
        treeScroll.scrollSensitivity = 42f;

        RectTransform treeViewport = CreateUiObject("Tree Viewport", scrollRoot);
        StretchToParent(treeViewport);
        Image viewportImage = treeViewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.001f);
        treeViewport.gameObject.AddComponent<RectMask2D>();

        RectTransform treeContent = CreateUiObject("Tree Content", treeViewport);
        treeContent.anchorMin = new Vector2(0.5f, 1f);
        treeContent.anchorMax = new Vector2(0.5f, 1f);
        treeContent.pivot = new Vector2(0.5f, 1f);
        treeContent.anchoredPosition = Vector2.zero;
        treeContent.sizeDelta = new Vector2(1800f, 1450f);
        treeScroll.viewport = treeViewport;
        treeScroll.content = treeContent;
        ProgressionTreePanController panController =
            scrollRoot.gameObject.AddComponent<ProgressionTreePanController>();
        panController.Configure(treeScroll, treeViewport, treePreview);

        CreateProgressionHeader(
            treeContent,
            "CONSUMABLES",
            "+",
            new Vector2(-750f, 650f),
            new Color(0.5f, 0.43f, 0.2f),
            280f);
        CreateProgressionHeader(
            treeContent,
            "FOOD",
            "F",
            new Vector2(-430f, 650f),
            new Color(0.86f, 0.46f, 0.1f),
            250f);
        CreateProgressionHeader(
            treeContent,
            "COLLECTION",
            "C",
            new Vector2(690f, 650f),
            new Color(0.16f, 0.52f, 0.84f),
            300f);

        Color foodColor = new Color(0.62f, 0.31f, 0.07f);
        Color primeFeedColor = new Color(0.72f, 0.43f, 0.08f);
        Color premiumColor = new Color(0.44f, 0.2f, 0.62f);
        Color chickenPerksColor = new Color(0.68f, 0.22f, 0.42f);
        Color valueColor = new Color(0.66f, 0.48f, 0.08f);
        Color incubationColor = new Color(0.08f, 0.48f, 0.28f);
        Color crosshatcherColor = new Color(0.18f, 0.5f, 0.46f);
        Color collectionColor = new Color(0.08f, 0.35f, 0.64f);
        Color robotColor = new Color(0.37f, 0.25f, 0.62f);

        buyFeedButton = CreateProgressionNode(
            "Buy Feed",
            treeContent,
            new Vector2(-780f, 550f),
            ProgressionSystem.UpgradeId.FoodBag,
            foodColor,
            0,
            150f);

        Vector2 feedPrevious = new Vector2(-430f, 620f);
        Vector2 feedTierTwoPosition = Vector2.zero;
        for (int tier = 2; tier <= FoodShopController.MaximumFeedTier; tier++)
        {
            Vector2 position = new Vector2(-570f, 550f - (tier - 2) * 95f);
            CreateTreeConnector(treeContent, feedPrevious, position, foodColor);
            Button node = CreateProgressionNode(
                $"Upgrade Feed Speed {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.FeedSpeed,
                foodColor,
                tier);
            if (tier == 2)
            {
                upgradeFeedButton = node;
                feedTierTwoPosition = position;
            }
            feedPrevious = position;
        }

        Vector2 primeFeedPrevious = feedTierTwoPosition;
        for (int tier = 1;
            tier <= FoodShopController.MaximumPrimeFeedLevel;
            tier++)
        {
            Vector2 position = new Vector2(
                -500f,
                455f - (tier - 1) * 95f);
            CreateTreeConnector(
                treeContent,
                primeFeedPrevious,
                position,
                primeFeedColor);
            CreateProgressionNode(
                $"Upgrade Prime Feed {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.PrimeFeed,
                primeFeedColor,
                tier);
            primeFeedPrevious = position;
        }

        Vector2 premiumPrevious = new Vector2(-430f, 620f);
        Vector2 premiumTierTwoPosition = Vector2.zero;
        Vector2 premiumTierEightPosition = Vector2.zero;
        for (int tier = 1;
            tier <= ProgressionSystem.MaximumRareEggChanceLevel;
            tier++)
        {
            Vector2 position = new Vector2(-430f, 550f - (tier - 1) * 95f);
            CreateTreeConnector(treeContent, premiumPrevious, position, premiumColor);
            CreateProgressionNode(
                $"Upgrade Premium Eggs {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.RareEggChance,
                premiumColor,
                tier);
            if (tier == 2)
            {
                premiumTierTwoPosition = position;
            }
            if (tier == 8)
            {
                premiumTierEightPosition = position;
            }
            premiumPrevious = position;
        }

        premiumPrevious = premiumTierEightPosition;
        for (int tier = 1;
            tier <= ProgressionSystem.MaximumChickenPerksLevel;
            tier++)
        {
            Vector2 position = new Vector2(
                -570f,
                -210f - (tier - 1) * 95f);
            CreateTreeConnector(
                treeContent,
                premiumPrevious,
                position,
                chickenPerksColor);
            CreateProgressionNode(
                $"Upgrade Chicken Perks {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.ChickenPerks,
                chickenPerksColor,
                tier);
            premiumPrevious = position;
        }

        Vector2 weightPrevious = premiumTierTwoPosition;
        Vector2 weightTierOnePosition = Vector2.zero;
        for (int tier = 1;
            tier <= ProgressionSystem.MaximumEggWeightLevel;
            tier++)
        {
            Vector2 position = new Vector2(-290f, 360f - (tier - 1) * 95f);
            CreateTreeConnector(treeContent, weightPrevious, position, valueColor);
            CreateProgressionNode(
                $"Upgrade Egg Weight {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.EggWeight,
                valueColor,
                tier);
            if (tier == 1)
            {
                weightTierOnePosition = position;
            }
            weightPrevious = position;
        }

        Vector2 valuePrevious = weightTierOnePosition;
        Color eggValueColor = new Color(0.12f, 0.52f, 0.2f);
        for (int tier = 1;
             tier <= ProgressionSystem.MaximumEggValueLevel;
             tier++)
        {
            Vector2 position = new Vector2(-150f, 360f - (tier - 1) * 95f);
            CreateTreeConnector(treeContent, valuePrevious, position, eggValueColor);
            CreateProgressionNode(
                $"Upgrade Egg Value {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.EggValue,
                eggValueColor,
                tier);
            valuePrevious = position;
        }

        // Incubators and crosshatchers are purchased and upgraded from the
        // focused pen's local HUD, not from the global supplies tree.
        upgradeIncubatorButton = null;

        Vector2 basketPrevious = new Vector2(690f, 620f);
        Vector2 basketOnePosition = Vector2.zero;
        for (int tier = 1;
            tier <= EggCarryController.MaximumBasketLevel;
            tier++)
        {
            Vector2 position = new Vector2(640f, 550f - (tier - 1) * 95f);
            CreateTreeConnector(treeContent, basketPrevious, position, collectionColor);
            Button node = CreateProgressionNode(
                $"Upgrade Basket Capacity {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.BasketCapacity,
                collectionColor,
                tier);
            if (tier == 1)
            {
                upgradeCollectionButton = node;
                basketOnePosition = position;
            }
            basketPrevious = position;
        }

        Vector2 basketReachPrevious = basketOnePosition;
        for (int tier = 1;
            tier <= EggCarryController.MaximumBasketReachLevel;
            tier++)
        {
            Vector2 position = new Vector2(
                800f,
                550f - (tier - 1) * 95f);
            CreateTreeConnector(
                treeContent,
                basketReachPrevious,
                position,
                collectionColor);
            CreateProgressionNode(
                $"Upgrade Basket Reach {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.BasketReach,
                collectionColor,
                tier);
            basketReachPrevious = position;
        }

        Vector2 vacuumUnlockPosition = new Vector2(530f, 150f);
        CreateTreeConnector(
            treeContent,
            basketPrevious,
            vacuumUnlockPosition,
            collectionColor);
        CreateTreeConnector(
            treeContent,
            basketReachPrevious,
            vacuumUnlockPosition,
            collectionColor);
        CreateProgressionNode(
            "Unlock Egg Vacuum",
            treeContent,
            vacuumUnlockPosition,
            ProgressionSystem.UpgradeId.VacuumUnlock,
            collectionColor,
            0,
            170f);

        Vector2 vacuumPowerPrevious = vacuumUnlockPosition;
        for (int tier = 2; tier <= 3; tier++)
        {
            Vector2 position = new Vector2(530f, 55f - (tier - 2) * 95f);
            CreateTreeConnector(treeContent, vacuumPowerPrevious, position, collectionColor);
            CreateProgressionNode(
                $"Upgrade Vacuum Power {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.VacuumPower,
                collectionColor,
                tier);
            vacuumPowerPrevious = position;
        }

        Vector2 vacuumRangePrevious = vacuumUnlockPosition;
        for (int tier = 2; tier <= 3; tier++)
        {
            Vector2 position = new Vector2(640f, 55f - (tier - 2) * 95f);
            CreateTreeConnector(treeContent, vacuumRangePrevious, position, collectionColor);
            CreateProgressionNode(
                $"Upgrade Vacuum Range {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.VacuumRange,
                collectionColor,
                tier);
            vacuumRangePrevious = position;
        }

        // Collector robots are also local pen equipment. Basket and vacuum
        // progression remain global and stay in this tree.

        CreateProgressionPreview(
            card,
            treePreview,
            dismissPreviewButton);

        shopStatusText = CreateText(
            "Shop Status",
            card,
            14f,
            TextAlignmentOptions.Center);
        shopStatusText.color = new Color(1f, 0.84f, 0.3f);
        SetRect(
            shopStatusText.rectTransform,
            new Vector2(260f, 410f),
            new Vector2(370f, 30f));

        doneShoppingButton = CreateButton(
            "Done Shopping",
            card,
            new Vector2(860f, 410f),
            new Vector2(44f, 44f),
            "X",
            new Color(0.82f, 0.26f, 0.1f));
        TMP_Text closeText = doneShoppingButton.GetComponentInChildren<TMP_Text>();
        closeText.text = "X";
        closeText.color = Color.white;
        closeText.fontSize = 30f;
        closeText.fontStyle = FontStyles.Bold;
        ApplyWideSuppliesShopLayout();
        suppliesShopScreen.SetActive(false);
    }

#endif

    private void ApplyWideSuppliesShopLayout()
    {
        if (suppliesShopScreen == null)
        {
            return;
        }

        RectTransform card = suppliesShopScreen.transform.Find("Supplies")
            as RectTransform;
        if (card == null)
        {
            return;
        }

        SetRuntimeRect(card, Vector2.zero, new Vector2(1880f, 920f));
        SetChildRect(
            card,
            "Shop Title",
            new Vector2(-640f, 410f),
            new Vector2(560f, 52f));
        TMP_Text title = card.Find("Shop Title")?.GetComponent<TMP_Text>();
        if (title != null)
        {
            title.text = "SUPPLIES SHOP";
        }

        SetChildRect(
            card,
            "Balance Coin",
            new Vector2(480f, 410f),
            new Vector2(40f, 40f));
        SetChildRect(
            card,
            "Available Cash",
            new Vector2(665f, 410f),
            new Vector2(330f, 44f));
        SetChildRect(
            card,
            "Shop Status",
            new Vector2(260f, 410f),
            new Vector2(370f, 30f));
        SetChildRect(
            card,
            "Done Shopping",
            new Vector2(860f, 410f),
            new Vector2(44f, 44f));

        TMP_Text closeText = doneShoppingButton != null
            ? doneShoppingButton.GetComponentInChildren<TMP_Text>(true)
            : card.Find("Done Shopping")?.GetComponentInChildren<TMP_Text>(true);
        if (closeText != null)
        {
            closeText.text = "X";
            closeText.color = Color.white;
            closeText.fontSize = 30f;
            closeText.fontStyle = FontStyles.Bold;
        }

        if (shopBalanceText != null)
        {
            shopBalanceText.alignment = TextAlignmentOptions.Right;
            shopBalanceText.textWrappingMode = TextWrappingModes.NoWrap;
            shopBalanceText.overflowMode = TextOverflowModes.Overflow;
        }

        RectTransform scrollRoot = card.Find("Progression Scroll View")
            as RectTransform;
        if (scrollRoot == null)
        {
            return;
        }

        SetRuntimeRect(scrollRoot, new Vector2(0f, -28f), new Vector2(1830f, 800f));
        RectTransform treeContent = scrollRoot.Find("Tree Viewport/Tree Content")
            as RectTransform;
        if (treeContent == null)
        {
            return;
        }

        treeContent.sizeDelta = new Vector2(
            1800f,
            Mathf.Max(3000f, treeContent.sizeDelta.y));
        EnsureRobotRarityLogicTier(treeContent);
        EnsureVacuumUnlockNode(treeContent);
        EnsureBasketCapacityTierFour(treeContent);
        EnsureBasketReachTiers(treeContent);
        EnsureFeedSpeedTiers(treeContent);
        EnsurePrimeFeedTiers(treeContent);
        EnsurePremiumEggTiers(treeContent);
        EnsureEggProgressionTiers(treeContent);
        EnsureChickenPerkTiers(treeContent);
        EnsureTruckBonusTiers(treeContent);
        EnsureTurboConsumableNodes(treeContent);
        ApplyProgressionTreeLayout(treeContent);
        ApplySuppliesShopVisualPolish(card, treeContent);
    }

    private static void ApplySuppliesShopVisualPolish(
        RectTransform card,
        RectTransform treeContent)
    {
        Color cream = new Color(1f, 0.91f, 0.68f, 1f);
        Color wood = new Color(0.34f, 0.16f, 0.045f, 1f);
        Color woodBorder = new Color(0.7f, 0.4f, 0.1f, 1f);
        Color cashGreen = new Color(0.08f, 0.31f, 0.08f, 1f);

        RectTransform titleFrame = EnsureShopFrame(
            card,
            "Shop Title Frame",
            new Vector2(-620f, 410f),
            new Vector2(560f, 72f),
            wood,
            woodBorder,
            3f);
        titleFrame.SetAsFirstSibling();
        TMP_Text title = card.Find("Shop Title")?.GetComponent<TMP_Text>();
        if (title != null)
        {
            SetRuntimeRect(
                title.rectTransform,
                new Vector2(-620f, 410f),
                new Vector2(520f, 64f));
            title.alignment = TextAlignmentOptions.Center;
            title.fontSize = 40f;
            title.color = Color.white;
            title.fontStyle = FontStyles.Bold;
        }

        RectTransform cashFrame = EnsureShopFrame(
            card,
            "Cash Banner Frame",
            new Vector2(585f, 410f),
            new Vector2(410f, 64f),
            cream,
            new Color(0.74f, 0.57f, 0.24f, 1f),
            3f);
        cashFrame.SetAsFirstSibling();
        SetChildRect(
            card,
            "Balance Coin",
            new Vector2(405f, 410f),
            new Vector2(48f, 48f));
        SetChildRect(
            card,
            "Available Cash",
            new Vector2(590f, 410f),
            new Vector2(340f, 64f));
        TMP_Text balance = card.Find("Available Cash")?.GetComponent<TMP_Text>();
        if (balance != null)
        {
            balance.alignment = TextAlignmentOptions.Right;
            balance.fontSize = 34f;
            balance.enableAutoSizing = true;
            balance.fontSizeMin = 22f;
            balance.fontSizeMax = 34f;
            balance.fontStyle = FontStyles.Bold;
            balance.color = cashGreen;
            balance.margin = new Vector4(0f, 0f, 10f, 0f);
        }

        SetChildRect(
            card,
            "Done Shopping",
            new Vector2(865f, 410f),
            new Vector2(64f, 64f));
        Button closeButton =
            card.Find("Done Shopping")?.GetComponent<Button>();
        if (closeButton != null)
        {
            Image closeImage = closeButton.targetGraphic as Image;
            if (closeImage != null)
            {
                closeImage.color = new Color(0.78f, 0.12f, 0.055f, 1f);
            }

            TMP_Text closeLabel =
                closeButton.GetComponentInChildren<TMP_Text>(true);
            if (closeLabel != null)
            {
                closeLabel.fontSize = 38f;
            }
        }

        SetChildRect(
            card,
            "Shop Status",
            new Vector2(220f, 410f),
            new Vector2(310f, 38f));

        CreateTreePanel(
            treeContent,
            "Consumables Column Frame",
            new Vector2(-735f, 350f),
            new Vector2(330f, 620f),
            new Color(0.15f, 0.13f, 0.065f, 0.98f),
            new Color(0.66f, 0.48f, 0.13f, 1f));

        RectTransform foodGroup = treeContent.Find("Food Tree Group")
            as RectTransform;
        RectTransform techGroup = treeContent.Find("Tech Tree Group")
            as RectTransform;
        RectTransform collectionGroup =
            treeContent.Find("Collection Tree Group") as RectTransform;
        if (foodGroup != null)
        {
            CreateTreePanel(
                foodGroup,
                "Active Tree Frame",
                new Vector2(175f, 105f),
                new Vector2(1450f, 1090f),
                new Color(0.14f, 0.085f, 0.035f, 0.98f),
                new Color(0.78f, 0.35f, 0.075f, 1f));
        }

        if (techGroup != null)
        {
            CreateTreePanel(
                techGroup,
                "Active Tree Frame",
                new Vector2(175f, 105f),
                new Vector2(1450f, 1090f),
                new Color(0.105f, 0.065f, 0.14f, 0.98f),
                new Color(0.62f, 0.35f, 0.82f, 1f));
        }

        if (collectionGroup != null)
        {
            CreateTreePanel(
                collectionGroup,
                "Active Tree Frame",
                new Vector2(175f, 105f),
                new Vector2(1450f, 1090f),
                new Color(0.04f, 0.095f, 0.14f, 0.98f),
                new Color(0.18f, 0.5f, 0.78f, 1f));
        }

        StyleTreeHeader(
            treeContent,
            "CONSUMABLES Branch",
            new Color(0.42f, 0.33f, 0.11f, 1f),
            new Color(0.72f, 0.52f, 0.16f, 1f));
        StyleTreeHeader(
            treeContent,
            "FOOD Branch",
            new Color(0.65f, 0.28f, 0.055f, 1f),
            new Color(0.9f, 0.47f, 0.1f, 1f));
        StyleTreeHeader(
            treeContent,
            "TECH Branch",
            new Color(0.34f, 0.17f, 0.48f, 1f),
            new Color(0.68f, 0.42f, 0.9f, 1f));
        StyleTreeHeader(
            treeContent,
            "COLLECTION Branch",
            new Color(0.12f, 0.34f, 0.57f, 1f),
            new Color(0.27f, 0.61f, 0.9f, 1f));

        Texture2D iconAtlas =
            Resources.Load<Texture2D>("UI/SuppliesIconAtlas");
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);
        for (int index = 0; index < nodes.Length; index++)
        {
            StyleProgressionNode(nodes[index], iconAtlas);
        }

        ConfigureSupplyShopTabs(
            treeContent,
            foodGroup,
            techGroup,
            collectionGroup);
    }

    private static void ConfigureSupplyShopTabs(
        RectTransform treeContent,
        RectTransform foodGroup,
        RectTransform techGroup,
        RectTransform collectionGroup)
    {
        RectTransform[] headers =
        {
            treeContent.Find("FOOD Branch") as RectTransform,
            treeContent.Find("TECH Branch") as RectTransform,
            treeContent.Find("COLLECTION Branch") as RectTransform
        };
        RectTransform[] groups =
        {
            foodGroup,
            techGroup,
            collectionGroup
        };
        Color[] tabColors =
        {
            new Color(0.65f, 0.28f, 0.055f, 1f),
            new Color(0.34f, 0.17f, 0.48f, 1f),
            new Color(0.12f, 0.34f, 0.57f, 1f)
        };

        for (int index = 0; index < headers.Length; index++)
        {
            RectTransform header = headers[index];
            if (header == null)
            {
                continue;
            }

            Button tab = header.GetComponent<Button>();
            if (tab == null)
            {
                tab = header.gameObject.AddComponent<Button>();
            }

            tab.targetGraphic = header.GetComponent<Image>();
            if (tab.targetGraphic != null)
            {
                tab.targetGraphic.raycastTarget = true;
            }

            tab.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = tab.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            tab.colors = colors;
            tab.onClick.RemoveAllListeners();
            int selectedIndex = index;
            tab.onClick.AddListener(
                () => SetSupplyShopTreeTab(
                    selectedIndex,
                    headers,
                    groups,
                    tabColors));
        }

        SetSupplyShopTreeTab(0, headers, groups, tabColors);
    }

    private static void SetSupplyShopTreeTab(
        int selectedIndex,
        RectTransform[] headers,
        RectTransform[] groups,
        Color[] tabColors)
    {
        ProgressionTreePreview preview = headers.Length > 0
            && headers[0] != null
            ? headers[0].GetComponentInParent<ProgressionTreePreview>(true)
            : null;
        preview?.Hide();

        for (int index = 0; index < groups.Length; index++)
        {
            bool selected = index == selectedIndex;
            if (groups[index] != null)
            {
                groups[index].gameObject.SetActive(selected);
            }

            Image headerImage = headers[index] != null
                ? headers[index].GetComponent<Image>()
                : null;
            if (headerImage != null)
            {
                headerImage.color = selected
                    ? Color.Lerp(tabColors[index], Color.white, 0.14f)
                    : Color.Lerp(tabColors[index], Color.black, 0.28f);
            }

            Outline outline = headers[index] != null
                ? headers[index].GetComponent<Outline>()
                : null;
            if (outline != null)
            {
                outline.effectDistance = selected
                    ? new Vector2(3f, -3f)
                    : new Vector2(1.5f, -1.5f);
                outline.effectColor = selected
                    ? Color.Lerp(tabColors[index], Color.white, 0.42f)
                    : Color.Lerp(tabColors[index], Color.black, 0.08f);
            }
        }

        RefreshSupplyShopTabIndicators(headers, groups, selectedIndex);
        ProgressionTreePanController panController = headers.Length > 0
            && headers[0] != null
                ? headers[0].GetComponentInParent<ProgressionTreePanController>(
                    true)
                : null;
        panController?.ResetToTop();
    }

    private static void EnsureSupplyShopTabIndicator(RectTransform header)
    {
        if (header == null || header.Find("New Unlock Indicator") != null)
        {
            return;
        }

        GameObject indicatorObject = new GameObject(
            "New Unlock Indicator",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform rect = indicatorObject.GetComponent<RectTransform>();
        rect.SetParent(header, false);
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-12f, -10f);
        rect.sizeDelta = new Vector2(36f, 36f);
        TMP_Text indicator = indicatorObject.GetComponent<TMP_Text>();
        TMP_Text headerText = header.GetComponentInChildren<TMP_Text>(true);
        if (headerText != null)
        {
            indicator.font = headerText.font;
        }
        indicator.text = "*";
        indicator.fontSize = 32f;
        indicator.fontStyle = FontStyles.Bold;
        indicator.alignment = TextAlignmentOptions.Center;
        indicator.color = new Color(1f, 0.88f, 0.28f, 1f);
        indicator.raycastTarget = false;
        indicatorObject.SetActive(false);
    }

    private static void RefreshSupplyShopTabIndicators(
        RectTransform[] headers,
        RectTransform[] groups,
        int selectedIndex)
    {
        for (int index = 0; index < headers.Length; index++)
        {
            EnsureSupplyShopTabIndicator(headers[index]);
            Transform indicator = headers[index] != null
                ? headers[index].Find("New Unlock Indicator")
                : null;
            if (indicator == null)
            {
                continue;
            }

            bool hasAffordableUnlock = false;
            if (groups[index] != null)
            {
                ProgressionNodeButton[] nodes =
                    groups[index].GetComponentsInChildren<ProgressionNodeButton>(true);
                for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
                {
                    ProgressionSystem.NodeState state = nodes[nodeIndex].GetNodeState();
                    if (state.Visible
                        && state.PrerequisiteMet
                        && !state.IsMaxed
                        && state.Cost > 0
                        && state.Cost <= EggScoreHud.CurrentCents)
                    {
                        hasAffordableUnlock = true;
                        break;
                    }
                }
            }

            indicator.gameObject.SetActive(
                index != selectedIndex && hasAffordableUnlock);
        }
    }

    private static RectTransform EnsureShopFrame(
        Transform parent,
        string objectName,
        Vector2 position,
        Vector2 size,
        Color backgroundColor,
        Color borderColor,
        float borderWidth)
    {
        RectTransform frame = parent.Find(objectName) as RectTransform;
        if (frame == null)
        {
            GameObject frameObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Outline));
            frame = frameObject.transform as RectTransform;
            frame.SetParent(parent, false);
        }

        SetRuntimeRect(frame, position, size);
        Image image = frame.GetComponent<Image>();
        image.color = backgroundColor;
        image.raycastTarget = false;
        Outline outline = frame.GetComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(borderWidth, -borderWidth);
        outline.useGraphicAlpha = false;
        return frame;
    }

    private static void CreateTreePanel(
        Transform parent,
        string objectName,
        Vector2 position,
        Vector2 size,
        Color backgroundColor,
        Color borderColor)
    {
        RectTransform panel = EnsureShopFrame(
            parent,
            objectName,
            position,
            size,
            backgroundColor,
            borderColor,
            3f);
        panel.SetAsFirstSibling();
    }

    private static void StyleTreeHeader(
        Transform parent,
        string headerName,
        Color backgroundColor,
        Color borderColor)
    {
        RectTransform header = parent.Find(headerName) as RectTransform;
        if (header == null)
        {
            return;
        }

        Image image = header.GetComponent<Image>();
        if (image != null)
        {
            image.color = backgroundColor;
        }

        Outline outline = header.GetComponent<Outline>();
        if (outline == null)
        {
            outline = header.gameObject.AddComponent<Outline>();
        }

        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(2f, -2f);
        outline.useGraphicAlpha = false;
    }

    private static void StyleProgressionNode(
        ProgressionNodeButton node,
        Texture2D iconAtlas)
    {
        if (node == null
            || node.transform is not RectTransform nodeRect)
        {
            return;
        }

        bool featured = IsFeaturedShopNode(node);
        bool root = !node.IsTierNode;
        if (featured)
        {
            nodeRect.sizeDelta = node.UpgradeId switch
            {
                ProgressionSystem.UpgradeId.IncubatorInstall
                    or ProgressionSystem.UpgradeId.CrosshatcherInstall
                    or ProgressionSystem.UpgradeId.RobotUnlock
                    or ProgressionSystem.UpgradeId.VacuumUnlock =>
                    new Vector2(250f, 104f),
                ProgressionSystem.UpgradeId.FoodBag =>
                    new Vector2(230f, 92f),
                _ => new Vector2(190f, 90f)
            };
        }
        else if (node.IsTierNode)
        {
            nodeRect.sizeDelta = new Vector2(150f, 74f);
        }
        else
        {
            nodeRect.sizeDelta = new Vector2(220f, 92f);
        }

        Button button = node.GetComponent<Button>();
        if (button == null)
        {
            return;
        }

        Color borderColor = GetNodeBorderColor(node.UpgradeId);
        Outline[] outlines = node.GetComponents<Outline>();
        Outline decorativeOutline = outlines.Length > 1
            ? outlines[outlines.Length - 1]
            : node.gameObject.AddComponent<Outline>();
        decorativeOutline.effectColor = borderColor;
        decorativeOutline.effectDistance = new Vector2(2f, -2f);
        decorativeOutline.useGraphicAlpha = false;

        Shadow shadow = null;
        Shadow[] shadows = node.GetComponents<Shadow>();
        for (int index = 0; index < shadows.Length; index++)
        {
            if (shadows[index] != null
                && shadows[index].GetType() == typeof(Shadow))
            {
                shadow = shadows[index];
                break;
            }
        }

        if (shadow == null)
        {
            shadow = node.gameObject.AddComponent<Shadow>();
        }

        shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
        shadow.effectDistance = new Vector2(3f, -3f);
        shadow.useGraphicAlpha = true;

        int atlasIndex = GetShopIconAtlasIndex(node);
        Texture2D standaloneIcon = GetTurboShopIcon(node.UpgradeId);
        Transform oldBadge = node.transform.Find("Node Icon");
        if (oldBadge != null && (atlasIndex >= 0 || standaloneIcon != null))
        {
            oldBadge.gameObject.SetActive(false);
        }

        TMP_Text label = node.transform.Find("Label")?.GetComponent<TMP_Text>();
        TMP_Text cost =
            node.transform.Find("Node Cost")?.GetComponent<TMP_Text>();
        if ((atlasIndex >= 0 && iconAtlas != null) || standaloneIcon != null)
        {
            RawImage icon = EnsureAtlasIcon(node.transform);
            icon.texture = standaloneIcon != null ? standaloneIcon : iconAtlas;
            icon.uvRect = standaloneIcon != null
                ? new Rect(0f, 0f, 1f, 1f)
                : GetShopIconUv(atlasIndex);
            icon.color = Color.white;
            icon.raycastTarget = false;
            float iconSize = root ? 68f : 56f;
            float iconX = root
                ? -nodeRect.sizeDelta.x * 0.5f + 43f
                : -nodeRect.sizeDelta.x * 0.5f + 34f;
            SetRuntimeRect(
                icon.rectTransform,
                new Vector2(iconX, root ? 7f : 8f),
                new Vector2(iconSize, iconSize));

            if (label != null)
            {
                label.alignment = TextAlignmentOptions.Left;
                label.fontSize = root ? 14f : 12f;
                label.margin = root
                    ? new Vector4(86f, 9f, 7f, 28f)
                    : new Vector4(68f, 7f, 5f, 22f);
            }
        }
        else if (label != null)
        {
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 12.5f;
            label.margin = new Vector4(5f, 5f, 5f, 21f);
        }

        if (cost != null)
        {
            cost.fontSize = featured ? 11.5f : 10.5f;
            cost.color = new Color(1f, 0.88f, 0.2f, 1f);
            cost.fontStyle = FontStyles.Bold;
            if (root)
            {
                SetRuntimeRect(
                    cost.rectTransform,
                    new Vector2(
                        nodeRect.sizeDelta.x * 0.5f - 64f,
                        -31f),
                    new Vector2(116f, 16f));
            }
            else if (featured)
            {
                SetRuntimeRect(
                    cost.rectTransform,
                    new Vector2(35f, -29f),
                    new Vector2(104f, 15f));
            }
        }

        RectTransform affordability =
            node.transform.Find("Node Affordability") as RectTransform;
        if (affordability != null && root)
        {
            SetRuntimeRect(
                affordability,
                new Vector2(0f, -nodeRect.sizeDelta.y * 0.5f + 7f),
                new Vector2(nodeRect.sizeDelta.x - 24f, 7f));
        }
    }

    private static RawImage EnsureAtlasIcon(Transform parent)
    {
        RawImage icon =
            parent.Find("Generated Shop Icon")?.GetComponent<RawImage>();
        if (icon != null)
        {
            return icon;
        }

        GameObject iconObject = new GameObject(
            "Generated Shop Icon",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage));
        iconObject.transform.SetParent(parent, false);
        icon = iconObject.GetComponent<RawImage>();
        icon.transform.SetAsLastSibling();
        return icon;
    }

    private static bool IsFeaturedShopNode(ProgressionNodeButton node)
    {
        if (!node.IsTierNode)
        {
            return true;
        }

        return node.UpgradeId switch
        {
            ProgressionSystem.UpgradeId.FeedSpeed => node.TargetLevel == 2,
            ProgressionSystem.UpgradeId.PrimeFeed => node.TargetLevel == 1,
            ProgressionSystem.UpgradeId.RareEggChance => node.TargetLevel == 1,
            ProgressionSystem.UpgradeId.ChickenPerks => node.TargetLevel == 1,
            ProgressionSystem.UpgradeId.EggWeight => node.TargetLevel == 1,
            ProgressionSystem.UpgradeId.EggValue => node.TargetLevel == 1,
            ProgressionSystem.UpgradeId.TruckBonus => node.TargetLevel == 1,
            ProgressionSystem.UpgradeId.BasketCapacity => node.TargetLevel == 1,
            ProgressionSystem.UpgradeId.BasketReach => node.TargetLevel == 1,
            _ => false
        };
    }

    private static int GetShopIconAtlasIndex(ProgressionNodeButton node)
    {
        return node.UpgradeId switch
        {
            ProgressionSystem.UpgradeId.FoodBag => 0,
            ProgressionSystem.UpgradeId.FeedSpeed
                when node.TargetLevel == 2 => 1,
            ProgressionSystem.UpgradeId.PrimeFeed
                when node.TargetLevel == 1 => 1,
            ProgressionSystem.UpgradeId.RareEggChance
                when node.TargetLevel == 1 => 2,
            ProgressionSystem.UpgradeId.ChickenPerks
                when node.TargetLevel == 1 => 2,
            ProgressionSystem.UpgradeId.EggWeight
                when node.TargetLevel == 1 => 3,
            ProgressionSystem.UpgradeId.EggValue
                when node.TargetLevel == 1 => 3,
            ProgressionSystem.UpgradeId.IncubatorInstall => 4,
            ProgressionSystem.UpgradeId.CrosshatcherInstall => 5,
            ProgressionSystem.UpgradeId.BasketCapacity
                when node.TargetLevel == 1 => 6,
            ProgressionSystem.UpgradeId.BasketReach
                when node.TargetLevel == 1 => 6,
            ProgressionSystem.UpgradeId.RobotUnlock => 7,
            ProgressionSystem.UpgradeId.TruckBonus
                when node.TargetLevel == 1 => 7,
            _ => -1
        };
    }

    private static Texture2D GetTurboShopIcon(
        ProgressionSystem.UpgradeId id)
    {
        string path = id switch
        {
            ProgressionSystem.UpgradeId.IncubatorTurbo =>
                TurboConsumableSystem.GetResourcePath(
                    TurboConsumableSystem.TurboType.Incubator),
            ProgressionSystem.UpgradeId.CrosshatcherTurbo =>
                TurboConsumableSystem.GetResourcePath(
                    TurboConsumableSystem.TurboType.Crosshatcher),
            ProgressionSystem.UpgradeId.RobotTurbo =>
                TurboConsumableSystem.GetResourcePath(
                    TurboConsumableSystem.TurboType.Robot),
            _ => string.Empty
        };
        return string.IsNullOrEmpty(path)
            ? null
            : Resources.Load<Texture2D>(path);
    }

    private static Rect GetShopIconUv(int atlasIndex)
    {
        int column = Mathf.Clamp(atlasIndex, 0, 7) % 4;
        int row = Mathf.Clamp(atlasIndex, 0, 7) / 4;
        return new Rect(
            column * 0.25f,
            row == 0 ? 0.5f : 0f,
            0.25f,
            0.5f);
    }

    private static Color GetNodeBorderColor(
        ProgressionSystem.UpgradeId id)
    {
        return id switch
        {
            ProgressionSystem.UpgradeId.FoodBag
                or ProgressionSystem.UpgradeId.FeedSpeed
                or ProgressionSystem.UpgradeId.PrimeFeed =>
                new Color(0.82f, 0.45f, 0.11f, 1f),
            ProgressionSystem.UpgradeId.IncubatorTurbo
                or ProgressionSystem.UpgradeId.IncubatorTurboPower
                or ProgressionSystem.UpgradeId.IncubatorTurboDuration =>
                new Color(0.95f, 0.48f, 0.12f, 1f),
            ProgressionSystem.UpgradeId.CrosshatcherTurbo
                or ProgressionSystem.UpgradeId.CrosshatcherTurboPower
                or ProgressionSystem.UpgradeId.CrosshatcherTurboDuration =>
                new Color(0.35f, 0.76f, 0.32f, 1f),
            ProgressionSystem.UpgradeId.RobotTurbo
                or ProgressionSystem.UpgradeId.RobotTurboPower
                or ProgressionSystem.UpgradeId.RobotTurboDuration =>
                new Color(0.68f, 0.42f, 0.9f, 1f),
            ProgressionSystem.UpgradeId.RareEggChance =>
                new Color(0.62f, 0.34f, 0.74f, 1f),
            ProgressionSystem.UpgradeId.ChickenPerks =>
                new Color(0.82f, 0.3f, 0.5f, 1f),
            ProgressionSystem.UpgradeId.EggWeight =>
                new Color(0.8f, 0.62f, 0.13f, 1f),
            ProgressionSystem.UpgradeId.EggValue =>
                new Color(0.2f, 0.62f, 0.28f, 1f),
            ProgressionSystem.UpgradeId.IncubatorInstall
                or ProgressionSystem.UpgradeId.IncubatorCapacity
                or ProgressionSystem.UpgradeId.IncubatorSpeed =>
                new Color(0.38f, 0.7f, 0.16f, 1f),
            ProgressionSystem.UpgradeId.CrosshatcherInstall
                or ProgressionSystem.UpgradeId.CrosshatcherSpeed
                or ProgressionSystem.UpgradeId.CrosshatcherQuality =>
                new Color(0.32f, 0.65f, 0.38f, 1f),
            ProgressionSystem.UpgradeId.BasketCapacity
                or ProgressionSystem.UpgradeId.BasketReach
                or ProgressionSystem.UpgradeId.VacuumUnlock
                or ProgressionSystem.UpgradeId.VacuumPower
                or ProgressionSystem.UpgradeId.VacuumRange
                or ProgressionSystem.UpgradeId.TruckBonus =>
                new Color(0.32f, 0.62f, 0.82f, 1f),
            _ => new Color(0.48f, 0.36f, 0.74f, 1f)
        };
    }

    private static void ApplyProgressionTreeLayout(RectTransform treeContent)
    {
        EnsureTechTreeHeader(treeContent);
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);

        SetRuntimeTreeHeader(
            treeContent,
            "CONSUMABLES Branch",
            new Vector2(-735f, 650f),
            330f);
        SetRuntimeTreeHeader(
            treeContent,
            "FOOD Branch",
            new Vector2(-315f, 650f),
            450f);
        SetRuntimeTreeHeader(
            treeContent,
            "TECH Branch",
            new Vector2(175f, 650f),
            450f);
        SetRuntimeTreeHeader(
            treeContent,
            "COLLECTION Branch",
            new Vector2(665f, 650f),
            450f);

        RectTransform[] treeRects =
            treeContent.GetComponentsInChildren<RectTransform>(true);
        for (int index = 0; index < treeRects.Length; index++)
        {
            RectTransform rect = treeRects[index];
            if (rect != null
                && rect != treeContent
                && rect.name == "Branch Connector")
            {
                rect.gameObject.SetActive(false);
                Destroy(rect.gameObject);
            }
        }

        RectTransform foodGroup = EnsureSupplyShopTreeGroup(
            treeContent,
            "Food Tree Group");
        RectTransform techGroup = EnsureSupplyShopTreeGroup(
            treeContent,
            "Tech Tree Group");
        RectTransform collectionGroup = EnsureSupplyShopTreeGroup(
            treeContent,
            "Collection Tree Group");
        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            bool localPenEquipment = node != null
                && node.UpgradeId is
                    ProgressionSystem.UpgradeId.IncubatorInstall
                    or ProgressionSystem.UpgradeId.IncubatorCapacity
                    or ProgressionSystem.UpgradeId.IncubatorSpeed
                    or ProgressionSystem.UpgradeId.CrosshatcherInstall
                    or ProgressionSystem.UpgradeId.CrosshatcherSpeed
                    or ProgressionSystem.UpgradeId.CrosshatcherQuality
                    or ProgressionSystem.UpgradeId.RobotUnlock
                    or ProgressionSystem.UpgradeId.RobotSpeed
                    or ProgressionSystem.UpgradeId.RobotCapacity
                    or ProgressionSystem.UpgradeId.RobotSmartness;
            if (localPenEquipment)
            {
                node.gameObject.SetActive(false);
                Destroy(node.gameObject);
                continue;
            }

            bool obsoleteVacuumEntry =
                node != null
                && node.TargetLevel == 1
                && (node.UpgradeId == ProgressionSystem.UpgradeId.VacuumPower
                    || node.UpgradeId == ProgressionSystem.UpgradeId.VacuumRange);
            if (obsoleteVacuumEntry)
            {
                node.gameObject.SetActive(false);
                Destroy(node.gameObject);
                continue;
            }

            bool obsoleteBasketReachTier = node != null
                && node.UpgradeId
                    == ProgressionSystem.UpgradeId.BasketReach
                && node.TargetLevel
                    > EggCarryController.MaximumBasketReachLevel;
            if (obsoleteBasketReachTier)
            {
                node.gameObject.SetActive(false);
                Destroy(node.gameObject);
                continue;
            }

            if (node == null
                || node.UpgradeId == ProgressionSystem.UpgradeId.FoodBag)
            {
                continue;
            }

            RectTransform group = GetSupplyShopTreeGroup(
                node.UpgradeId,
                foodGroup,
                techGroup,
                collectionGroup);
            if (group != null && node.transform.parent != group)
            {
                node.transform.SetParent(group, false);
            }
        }

        Color foodColor = new Color(0.62f, 0.31f, 0.07f);
        Color premiumColor = new Color(0.44f, 0.2f, 0.62f);
        Color chickenPerksColor = new Color(0.68f, 0.22f, 0.42f);
        Color valueColor = new Color(0.66f, 0.48f, 0.08f);
        Color incubationColor = new Color(0.08f, 0.48f, 0.28f);
        Color crosshatcherColor = new Color(0.18f, 0.5f, 0.46f);
        Color collectionColor = new Color(0.08f, 0.35f, 0.64f);
        Color robotColor = new Color(0.37f, 0.25f, 0.62f);

        SetRuntimeNodePosition(
            nodes,
            ProgressionSystem.UpgradeId.FoodBag,
            0,
            new Vector2(-735f, 535f));

        SetRuntimeNodePosition(
            nodes,
            ProgressionSystem.UpgradeId.IncubatorTurbo,
            0,
            new Vector2(-735f, 400f));
        SetRuntimeNodePosition(
            nodes,
            ProgressionSystem.UpgradeId.CrosshatcherTurbo,
            0,
            new Vector2(-735f, 265f));
        SetRuntimeNodePosition(
            nodes,
            ProgressionSystem.UpgradeId.RobotTurbo,
            0,
            new Vector2(-735f, 130f));

        LayoutTurboTechBranch(
            techGroup,
            nodes,
            ProgressionSystem.UpgradeId.IncubatorTurboPower,
            ProgressionSystem.UpgradeId.IncubatorTurboDuration,
            TurboConsumableSystem.TurboType.Incubator,
            -300f,
            new Color(0.95f, 0.48f, 0.12f, 1f));
        LayoutTurboTechBranch(
            techGroup,
            nodes,
            ProgressionSystem.UpgradeId.CrosshatcherTurboPower,
            ProgressionSystem.UpgradeId.CrosshatcherTurboDuration,
            TurboConsumableSystem.TurboType.Crosshatcher,
            175f,
            new Color(0.35f, 0.76f, 0.32f, 1f));
        LayoutTurboTechBranch(
            techGroup,
            nodes,
            ProgressionSystem.UpgradeId.RobotTurboPower,
            ProgressionSystem.UpgradeId.RobotTurboDuration,
            TurboConsumableSystem.TurboType.Robot,
            650f,
            new Color(0.68f, 0.42f, 0.9f, 1f));

        Vector2 previousFeed = Vector2.zero;
        Vector2 feedTierTwo = Vector2.zero;
        for (int tier = 2; tier <= FoodShopController.MaximumFeedTier; tier++)
        {
            Vector2 position = new Vector2(
                -410f,
                520f - (tier - 2) * 95f);
            SetRuntimeNodePosition(
                nodes,
                ProgressionSystem.UpgradeId.FeedSpeed,
                tier,
                position);
            if (tier > 2)
            {
                CreateRuntimeTreeConnector(
                    foodGroup,
                    previousFeed,
                    position,
                    foodColor);
            }

            if (tier == 2)
            {
                feedTierTwo = position;
            }

            previousFeed = position;
        }

        Vector2 previousPrimeFeed = feedTierTwo;
        Color runtimePrimeFeedColor = new Color(0.72f, 0.43f, 0.08f);
        for (int tier = 1;
            tier <= FoodShopController.MaximumPrimeFeedLevel;
            tier++)
        {
            Vector2 position = new Vector2(
                -115f,
                520f - (tier - 1) * 108f);
            SetRuntimeNodePosition(
                nodes,
                ProgressionSystem.UpgradeId.PrimeFeed,
                tier,
                position);
            CreateRuntimeTreeConnector(
                foodGroup,
                previousPrimeFeed,
                position,
                runtimePrimeFeedColor);
            previousPrimeFeed = position;
        }

        Vector2 previousPremium = Vector2.zero;
        Vector2 premiumTierTwo = Vector2.zero;
        Vector2 premiumTierEight = Vector2.zero;
        for (int tier = 1;
            tier <= ProgressionSystem.MaximumRareEggChanceLevel;
            tier++)
        {
            Vector2 position = new Vector2(
                175f,
                520f - (tier - 1) * 108f);
            SetRuntimeNodePosition(
                nodes,
                ProgressionSystem.UpgradeId.RareEggChance,
                tier,
                position);
            if (tier > 1)
            {
                CreateRuntimeTreeConnector(
                    foodGroup,
                    previousPremium,
                    position,
                    premiumColor);
            }

            if (tier == 2)
            {
                premiumTierTwo = position;
            }
            if (tier == 8)
            {
                premiumTierEight = position;
            }

            previousPremium = position;
        }

        Vector2 previousChickenPerk = premiumTierEight;
        for (int tier = 1;
            tier <= ProgressionSystem.MaximumChickenPerksLevel;
            tier++)
        {
            Vector2 position = new Vector2(
                -100f,
                -326f - (tier - 1) * 90f);
            SetRuntimeNodePosition(
                nodes,
                ProgressionSystem.UpgradeId.ChickenPerks,
                tier,
                position);
            CreateRuntimeTreeConnector(
                foodGroup,
                previousChickenPerk,
                position,
                chickenPerksColor);
            previousChickenPerk = position;
        }

        Vector2 previousWeight = premiumTierTwo;
        Vector2 weightTierOne = Vector2.zero;
        for (int tier = 1;
            tier <= ProgressionSystem.MaximumEggWeightLevel;
            tier++)
        {
            Vector2 position = new Vector2(
                505f,
                412f - (tier - 1) * 95f);
            SetRuntimeNodePosition(
                nodes,
                ProgressionSystem.UpgradeId.EggWeight,
                tier,
                position);
            CreateRuntimeTreeConnector(
                foodGroup,
                previousWeight,
                position,
                valueColor);
            if (tier == 1)
            {
                weightTierOne = position;
            }
            previousWeight = position;
        }

        Vector2 previousValue = weightTierOne;
        Color eggValueColor = new Color(0.12f, 0.52f, 0.2f);
        for (int tier = 1;
             tier <= ProgressionSystem.MaximumEggValueLevel;
             tier++)
        {
            Vector2 position = new Vector2(
                790f,
                412f - (tier - 1) * 95f);
            SetRuntimeNodePosition(
                nodes,
                ProgressionSystem.UpgradeId.EggValue,
                tier,
                position);
            CreateRuntimeTreeConnector(
                foodGroup,
                previousValue,
                position,
                eggValueColor);
            previousValue = position;
        }

        Vector2 basketOne = new Vector2(-300f, 510f);
        Vector2 previousBasket = Vector2.zero;
        for (int tier = 1;
            tier <= EggCarryController.MaximumBasketLevel;
            tier++)
        {
            Vector2 position = new Vector2(
                basketOne.x,
                basketOne.y - (tier - 1) * 125f);
            SetRuntimeNodePosition(
                nodes,
                ProgressionSystem.UpgradeId.BasketCapacity,
                tier,
                position);
            if (tier > 1)
            {
                CreateRuntimeTreeConnector(
                    collectionGroup,
                    previousBasket,
                    position,
                    collectionColor);
            }

            previousBasket = position;
        }

        Vector2 previousBasketReach = basketOne;
        for (int tier = 1;
            tier <= EggCarryController.MaximumBasketReachLevel;
            tier++)
        {
            Vector2 position = new Vector2(
                80f,
                510f - (tier - 1) * 105f);
            SetRuntimeNodePosition(
                nodes,
                ProgressionSystem.UpgradeId.BasketReach,
                tier,
                position);
            CreateRuntimeTreeConnector(
                collectionGroup,
                previousBasketReach,
                position,
                collectionColor);
            previousBasketReach = position;
        }

        Vector2 vacuumUnlock = new Vector2(-110f, -55f);
        SetRuntimeNodePosition(
            nodes,
            ProgressionSystem.UpgradeId.VacuumUnlock,
            0,
            vacuumUnlock);
        CreateRuntimeTreeConnector(
            collectionGroup,
            previousBasket,
            vacuumUnlock,
            collectionColor);
        CreateRuntimeTreeConnector(
            collectionGroup,
            previousBasketReach,
            vacuumUnlock,
            collectionColor);

        Vector2 previousVacuumPower = vacuumUnlock;
        for (int tier = 2; tier <= 3; tier++)
        {
            Vector2 position = new Vector2(
                -230f,
                -215f - (tier - 2) * 125f);
            SetRuntimeNodePosition(
                nodes,
                ProgressionSystem.UpgradeId.VacuumPower,
                tier,
                position);
            CreateRuntimeTreeConnector(
                collectionGroup,
                previousVacuumPower,
                position,
                collectionColor);
            previousVacuumPower = position;
        }

        Vector2 previousVacuumRange = vacuumUnlock;
        for (int tier = 2; tier <= 3; tier++)
        {
            Vector2 position = new Vector2(
                10f,
                -215f - (tier - 2) * 125f);
            SetRuntimeNodePosition(
                nodes,
                ProgressionSystem.UpgradeId.VacuumRange,
                tier,
                position);
            CreateRuntimeTreeConnector(
                collectionGroup,
                previousVacuumRange,
                position,
                collectionColor);
            previousVacuumRange = position;
        }

        Vector2 previousTruckBonus = new Vector2(430f, 620f);
        Color truckColor = new Color(0.12f, 0.5f, 0.68f);
        for (int tier = 1;
             tier <= ProgressionSystem.MaximumTruckBonusLevel;
             tier++)
        {
            Vector2 position = new Vector2(
                430f,
                510f - (tier - 1) * 105f);
            SetRuntimeNodePosition(
                nodes,
                ProgressionSystem.UpgradeId.TruckBonus,
                tier,
                position);
            CreateRuntimeTreeConnector(
                collectionGroup,
                previousTruckBonus,
                position,
                truckColor);
            previousTruckBonus = position;
        }

        // Per-pen machines and robots are deliberately absent from this
        // global supplies layout.
    }

    private static RectTransform EnsureSupplyShopTreeGroup(
        RectTransform treeContent,
        string objectName)
    {
        RectTransform group = treeContent.Find(objectName) as RectTransform;
        if (group == null)
        {
            GameObject groupObject = new GameObject(
                objectName,
                typeof(RectTransform));
            group = groupObject.transform as RectTransform;
            group.SetParent(treeContent, false);
        }

        SetRuntimeRect(group, Vector2.zero, treeContent.sizeDelta);
        group.SetAsFirstSibling();
        return group;
    }

    private static RectTransform GetSupplyShopTreeGroup(
        ProgressionSystem.UpgradeId id,
        RectTransform foodGroup,
        RectTransform techGroup,
        RectTransform collectionGroup)
    {
        return id switch
        {
            ProgressionSystem.UpgradeId.FeedSpeed
                or ProgressionSystem.UpgradeId.PrimeFeed
                or ProgressionSystem.UpgradeId.RareEggChance
                or ProgressionSystem.UpgradeId.ChickenPerks
                or ProgressionSystem.UpgradeId.EggWeight
                or ProgressionSystem.UpgradeId.EggValue => foodGroup,
            ProgressionSystem.UpgradeId.IncubatorInstall
                or ProgressionSystem.UpgradeId.IncubatorCapacity
                or ProgressionSystem.UpgradeId.IncubatorSpeed
                or ProgressionSystem.UpgradeId.CrosshatcherInstall
                or ProgressionSystem.UpgradeId.CrosshatcherSpeed
                or ProgressionSystem.UpgradeId.CrosshatcherQuality
                or ProgressionSystem.UpgradeId.IncubatorTurboPower
                or ProgressionSystem.UpgradeId.IncubatorTurboDuration
                or ProgressionSystem.UpgradeId.CrosshatcherTurboPower
                or ProgressionSystem.UpgradeId.CrosshatcherTurboDuration
                or ProgressionSystem.UpgradeId.RobotTurboPower
                or ProgressionSystem.UpgradeId.RobotTurboDuration => techGroup,
            ProgressionSystem.UpgradeId.BasketCapacity
                or ProgressionSystem.UpgradeId.BasketReach
                or ProgressionSystem.UpgradeId.VacuumUnlock
                or ProgressionSystem.UpgradeId.VacuumPower
                or ProgressionSystem.UpgradeId.VacuumRange
                or ProgressionSystem.UpgradeId.TruckBonus
                or ProgressionSystem.UpgradeId.RobotUnlock
                or ProgressionSystem.UpgradeId.RobotSpeed
                or ProgressionSystem.UpgradeId.RobotCapacity
                or ProgressionSystem.UpgradeId.RobotSmartness => collectionGroup,
            _ => null
        };
    }

    private static void EnsureTechTreeHeader(RectTransform treeContent)
    {
        RectTransform techHeader = treeContent.Find("TECH Branch")
            as RectTransform;
        if (techHeader == null)
        {
            RectTransform foodHeader = treeContent.Find("FOOD Branch")
                as RectTransform;
            if (foodHeader == null)
            {
                return;
            }

            GameObject clone = Instantiate(foodHeader.gameObject, treeContent);
            clone.name = "TECH Branch";
            techHeader = clone.transform as RectTransform;
            TMP_Text[] texts = clone.GetComponentsInChildren<TMP_Text>(true);
            for (int index = 0; index < texts.Length; index++)
            {
                if (texts[index].text == "FOOD")
                {
                    texts[index].text = "TECH";
                }
                else if (texts[index].text == "F")
                {
                    texts[index].text = "T";
                }
            }
        }

        Transform oldIcon = techHeader.Find("FOOD Icon");
        if (oldIcon != null)
        {
            oldIcon.name = "TECH Icon";
        }
        Transform oldLabel = techHeader.Find("FOOD Label");
        if (oldLabel != null)
        {
            oldLabel.name = "TECH Label";
        }
        techHeader.gameObject.SetActive(true);
    }

    private static void SetRuntimeTreeHeader(
        RectTransform treeContent,
        string headerName,
        Vector2 position,
        float width)
    {
        RectTransform header = treeContent.Find(headerName) as RectTransform;
        if (header == null)
        {
            return;
        }

        SetRuntimeRect(header, position, new Vector2(width, 54f));
        SetChildRect(
            header,
            headerName.Replace(" Branch", " Icon"),
            new Vector2(-width * 0.5f + 24f, 0f),
            new Vector2(40f, 40f));
        SetChildRect(
            header,
            headerName.Replace(" Branch", " Label"),
            new Vector2(20f, 0f),
            new Vector2(width - 64f, 46f));
    }

    private static void LayoutTurboTechBranch(
        RectTransform techGroup,
        ProgressionNodeButton[] nodes,
        ProgressionSystem.UpgradeId powerId,
        ProgressionSystem.UpgradeId durationId,
        TurboConsumableSystem.TurboType type,
        float centerX,
        Color color)
    {
        if (techGroup == null)
        {
            return;
        }

        Vector2 headerPosition = new Vector2(centerX, 535f);
        EnsureTurboTechSectionHeader(
            techGroup,
            type,
            headerPosition,
            color);
        Vector2 previousPower = headerPosition;
        Vector2 previousDuration = headerPosition;
        int maximum = Mathf.Max(
            TurboConsumableSystem.MaximumPowerLevel,
            TurboConsumableSystem.MaximumDurationLevel);
        for (int tier = 1; tier <= maximum; tier++)
        {
            if (tier <= TurboConsumableSystem.MaximumPowerLevel)
            {
                Vector2 position = new Vector2(
                    centerX - 82f,
                    420f - (tier - 1) * 95f);
                SetRuntimeNodePosition(nodes, powerId, tier, position);
                CreateRuntimeTreeConnector(
                    techGroup,
                    previousPower,
                    position,
                    color);
                previousPower = position;
            }

            if (tier <= TurboConsumableSystem.MaximumDurationLevel)
            {
                Vector2 position = new Vector2(
                    centerX + 82f,
                    420f - (tier - 1) * 95f);
                SetRuntimeNodePosition(nodes, durationId, tier, position);
                CreateRuntimeTreeConnector(
                    techGroup,
                    previousDuration,
                    position,
                    color);
                previousDuration = position;
            }
        }
    }

    private static void EnsureTurboTechSectionHeader(
        RectTransform parent,
        TurboConsumableSystem.TurboType type,
        Vector2 position,
        Color color)
    {
        string displayName = TurboConsumableSystem.GetDisplayName(type);
        string objectName = $"{displayName} Turbo Tech Header";
        RectTransform header = parent.Find(objectName) as RectTransform;
        if (header == null)
        {
            GameObject headerObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Outline));
            headerObject.transform.SetParent(parent, false);
            header = headerObject.transform as RectTransform;
        }

        SetRuntimeRect(header, position, new Vector2(390f, 72f));
        Image background = header.GetComponent<Image>();
        background.sprite = GetHudRoundedSprite();
        background.type = Image.Type.Sliced;
        background.color = Color.Lerp(color, Color.black, 0.28f);
        background.raycastTarget = false;
        Outline outline = header.GetComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(2f, -2f);

        RawImage icon = header.Find("Machine Icon")?.GetComponent<RawImage>();
        if (icon == null)
        {
            GameObject iconObject = new GameObject(
                "Machine Icon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            iconObject.transform.SetParent(header, false);
            icon = iconObject.GetComponent<RawImage>();
        }
        icon.texture = Resources.Load<Texture2D>(
            TurboConsumableSystem.GetResourcePath(type));
        icon.color = Color.white;
        icon.raycastTarget = false;
        SetRuntimeRect(
            icon.rectTransform,
            new Vector2(-153f, 0f),
            new Vector2(58f, 58f));

        TMP_Text label = header.Find("Machine Label")?.GetComponent<TMP_Text>();
        if (label == null)
        {
            GameObject labelObject = new GameObject(
                "Machine Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(header, false);
            label = labelObject.GetComponent<TMP_Text>();
            TMP_Text fontSource = parent.GetComponentInChildren<TMP_Text>(true);
            if (fontSource != null)
            {
                label.font = fontSource.font;
            }
        }
        label.text = $"{displayName.ToUpperInvariant()} TURBO TECH";
        label.fontSize = 19f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        SetRuntimeRect(
            label.rectTransform,
            new Vector2(27f, 0f),
            new Vector2(295f, 48f));
    }

    private static void SetRuntimeNodePosition(
        ProgressionNodeButton[] nodes,
        ProgressionSystem.UpgradeId id,
        int targetLevel,
        Vector2 position)
    {
        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node != null
                && node.UpgradeId == id
                && node.TargetLevel == targetLevel
                && node.transform is RectTransform rect)
            {
                rect.anchoredPosition = position;
                return;
            }
        }
    }

    private static void CreateRuntimeTreeConnector(
        Transform parent,
        Vector2 start,
        Vector2 end,
        Color color)
    {
        GameObject connectorObject = new GameObject(
            "Branch Connector",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform connector = connectorObject.transform as RectTransform;
        connector.SetParent(parent, false);
        Vector2 direction = end - start;
        SetRuntimeRect(
            connector,
            (start + end) * 0.5f,
            new Vector2(direction.magnitude, 4f));
        connector.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        Image connectorImage = connectorObject.GetComponent<Image>();
        connectorImage.color = new Color(color.r, color.g, color.b, 0.45f);
        connectorImage.raycastTarget = false;
        connector.SetAsFirstSibling();
    }

    private static void EnsureTurboConsumableNodes(RectTransform treeContent)
    {
        ProgressionNodeButton[] existing =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);
        ProgressionSystem.UpgradeId[] roots =
        {
            ProgressionSystem.UpgradeId.IncubatorTurbo,
            ProgressionSystem.UpgradeId.CrosshatcherTurbo,
            ProgressionSystem.UpgradeId.RobotTurbo
        };
        ProgressionSystem.UpgradeId[] powers =
        {
            ProgressionSystem.UpgradeId.IncubatorTurboPower,
            ProgressionSystem.UpgradeId.CrosshatcherTurboPower,
            ProgressionSystem.UpgradeId.RobotTurboPower
        };
        ProgressionSystem.UpgradeId[] durations =
        {
            ProgressionSystem.UpgradeId.IncubatorTurboDuration,
            ProgressionSystem.UpgradeId.CrosshatcherTurboDuration,
            ProgressionSystem.UpgradeId.RobotTurboDuration
        };
        Color[] colors =
        {
            new Color(0.78f, 0.31f, 0.07f, 1f),
            new Color(0.18f, 0.55f, 0.25f, 1f),
            new Color(0.46f, 0.25f, 0.67f, 1f)
        };

        ProgressionNodeButton rootTemplate = null;
        ProgressionNodeButton tierTemplate = null;
        for (int index = 0; index < existing.Length; index++)
        {
            ProgressionNodeButton node = existing[index];
            if (node == null)
            {
                continue;
            }

            if (node.IsTierNode)
            {
                tierTemplate ??= node;
            }
            else if (node.UpgradeId == ProgressionSystem.UpgradeId.FoodBag)
            {
                rootTemplate = node;
            }
        }

        if (rootTemplate == null || tierTemplate == null)
        {
            return;
        }

        for (int typeIndex = 0; typeIndex < roots.Length; typeIndex++)
        {
            if (!HasProgressionNode(existing, roots[typeIndex], 0))
            {
                GameObject clone = Instantiate(
                    rootTemplate.gameObject,
                    treeContent);
                clone.name = $"Buy {roots[typeIndex]}";
                ProgressionNodeButton node =
                    clone.GetComponent<ProgressionNodeButton>();
                node?.SetUpgrade(roots[typeIndex]);
                node?.SetVisualColor(colors[typeIndex]);
            }

            for (int tier = 1;
                 tier <= TurboConsumableSystem.MaximumPowerLevel;
                 tier++)
            {
                if (!HasProgressionNode(existing, powers[typeIndex], tier))
                {
                    GameObject clone = Instantiate(
                        tierTemplate.gameObject,
                        treeContent);
                    clone.name = $"Upgrade {powers[typeIndex]} {tier}";
                    ProgressionNodeButton node =
                        clone.GetComponent<ProgressionNodeButton>();
                    node?.SetUpgrade(powers[typeIndex], tier);
                    node?.SetVisualColor(colors[typeIndex]);
                }
            }

            for (int tier = 1;
                 tier <= TurboConsumableSystem.MaximumDurationLevel;
                 tier++)
            {
                if (!HasProgressionNode(existing, durations[typeIndex], tier))
                {
                    GameObject clone = Instantiate(
                        tierTemplate.gameObject,
                        treeContent);
                    clone.name = $"Upgrade {durations[typeIndex]} {tier}";
                    ProgressionNodeButton node =
                        clone.GetComponent<ProgressionNodeButton>();
                    node?.SetUpgrade(durations[typeIndex], tier);
                    node?.SetVisualColor(colors[typeIndex]);
                }
            }
        }
    }

    private static bool HasProgressionNode(
        ProgressionNodeButton[] nodes,
        ProgressionSystem.UpgradeId id,
        int targetLevel)
    {
        for (int index = 0; index < nodes.Length; index++)
        {
            if (nodes[index] != null
                && nodes[index].UpgradeId == id
                && nodes[index].TargetLevel == targetLevel)
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureRobotRarityLogicTier(RectTransform treeContent)
    {
        ProgressionNodeButton tierTwo = null;
        ProgressionNodeButton tierThree = null;
        ProgressionNodeButton tierFour = null;
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);
        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node.UpgradeId != ProgressionSystem.UpgradeId.RobotSmartness)
            {
                continue;
            }

            if (node.TargetLevel == 4)
            {
                tierFour = node;
            }
            else if (node.TargetLevel == 3)
            {
                tierThree = node;
            }
            else if (node.TargetLevel == 2)
            {
                tierTwo = node;
            }
        }

        if (tierFour != null || tierTwo == null)
        {
            return;
        }

        if (tierThree == null)
        {
            GameObject tierThreeObject = Instantiate(
                tierTwo.gameObject,
                tierTwo.transform.parent);
            tierThreeObject.name = "Upgrade Robot Logic 3";
            tierThree = tierThreeObject.GetComponent<ProgressionNodeButton>();
            tierThree?.SetTargetLevel(3);
            PositionLogicTier(treeContent, tierTwo, tierThree);
        }

        if (tierThree == null)
        {
            return;
        }

        GameObject tierFourObject = Instantiate(
            tierThree.gameObject,
            tierThree.transform.parent);
        tierFourObject.name = "Upgrade Robot Logic 4 - Chicken Arms";
        tierFour = tierFourObject.GetComponent<ProgressionNodeButton>();
        tierFour?.SetTargetLevel(4);
        PositionLogicTier(treeContent, tierThree, tierFour);
    }

    private static void PositionLogicTier(
        RectTransform treeContent,
        ProgressionNodeButton previous,
        ProgressionNodeButton next)
    {
        RectTransform previousRect = previous != null
            ? previous.transform as RectTransform
            : null;
        RectTransform nextRect = next != null
            ? next.transform as RectTransform
            : null;
        if (previousRect == null || nextRect == null)
        {
            return;
        }

        nextRect.anchoredPosition =
            previousRect.anchoredPosition + Vector2.down * 95f;
        CreateRuntimeTreeConnector(
            treeContent,
            previousRect.anchoredPosition,
            nextRect.anchoredPosition,
            new Color(0.37f, 0.25f, 0.62f, 1f));
    }

    private static void EnsureVacuumUnlockNode(RectTransform treeContent)
    {
        ProgressionNodeButton robotTemplate = null;
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);
        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node == null)
            {
                continue;
            }

            if (node.UpgradeId == ProgressionSystem.UpgradeId.VacuumUnlock)
            {
                return;
            }

            if (node.UpgradeId == ProgressionSystem.UpgradeId.RobotUnlock)
            {
                robotTemplate = node;
            }
        }

        if (robotTemplate == null)
        {
            return;
        }

        GameObject clone = Instantiate(
            robotTemplate.gameObject,
            robotTemplate.transform.parent);
        clone.name = "Unlock Egg Vacuum";
        ProgressionNodeButton vacuumNode =
            clone.GetComponent<ProgressionNodeButton>();
        vacuumNode?.SetUpgrade(
            ProgressionSystem.UpgradeId.VacuumUnlock,
            0);
    }

    private static void EnsureBasketCapacityTierFour(RectTransform treeContent)
    {
        ProgressionNodeButton tierThree = null;
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);
        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node == null
                || node.UpgradeId
                    != ProgressionSystem.UpgradeId.BasketCapacity)
            {
                continue;
            }

            if (node.TargetLevel == EggCarryController.MaximumBasketLevel)
            {
                return;
            }

            if (node.TargetLevel
                == EggCarryController.MaximumBasketLevel - 1)
            {
                tierThree = node;
            }
        }

        if (tierThree == null)
        {
            return;
        }

        GameObject clone = Instantiate(
            tierThree.gameObject,
            tierThree.transform.parent);
        clone.name = "Upgrade Basket Capacity 4";
        clone.GetComponent<ProgressionNodeButton>()?.SetTargetLevel(
            EggCarryController.MaximumBasketLevel);
    }

    private static void EnsureFeedSpeedTiers(RectTransform treeContent)
    {
        ProgressionNodeButton template = null;
        bool[] existing = new bool[FoodShopController.MaximumFeedTier + 1];
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);
        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node == null
                || node.UpgradeId != ProgressionSystem.UpgradeId.FeedSpeed
                || node.TargetLevel < 2)
            {
                continue;
            }

            template = node;
            if (node.TargetLevel <= FoodShopController.MaximumFeedTier)
            {
                existing[node.TargetLevel] = true;
            }
        }

        if (template == null)
        {
            return;
        }

        for (int tier = 2; tier <= FoodShopController.MaximumFeedTier; tier++)
        {
            if (existing[tier])
            {
                continue;
            }

            GameObject clone = Instantiate(
                template.gameObject,
                template.transform.parent);
            clone.name = $"Upgrade Feed Speed {tier}";
            clone.GetComponent<ProgressionNodeButton>()?.SetUpgrade(
                ProgressionSystem.UpgradeId.FeedSpeed,
                tier);
        }
    }

    private static void EnsurePrimeFeedTiers(RectTransform treeContent)
    {
        ProgressionNodeButton template = null;
        bool[] existing = new bool[
            FoodShopController.MaximumPrimeFeedLevel + 1];
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);

        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node == null)
            {
                continue;
            }

            if (node.UpgradeId == ProgressionSystem.UpgradeId.PrimeFeed
                && node.TargetLevel > 0
                && node.TargetLevel <= FoodShopController.MaximumPrimeFeedLevel)
            {
                existing[node.TargetLevel] = true;
            }
            else if (template == null
                && node.UpgradeId
                    == ProgressionSystem.UpgradeId.RareEggChance
                && node.TargetLevel == 1)
            {
                template = node;
            }
        }

        if (template == null)
        {
            return;
        }

        for (int tier = 1;
            tier <= FoodShopController.MaximumPrimeFeedLevel;
            tier++)
        {
            if (existing[tier])
            {
                continue;
            }

            GameObject clone = Instantiate(
                template.gameObject,
                template.transform.parent);
            clone.name = $"Upgrade Prime Feed {tier}";
            clone.GetComponent<ProgressionNodeButton>()?.SetUpgrade(
                ProgressionSystem.UpgradeId.PrimeFeed,
                tier);
        }
    }

    private static void EnsureBasketReachTiers(RectTransform treeContent)
    {
        ProgressionNodeButton template = null;
        bool[] existing = new bool[
            EggCarryController.MaximumBasketReachLevel + 1];
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);

        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node == null)
            {
                continue;
            }

            if (node.UpgradeId == ProgressionSystem.UpgradeId.BasketReach
                && node.TargetLevel > 0
                && node.TargetLevel
                    <= EggCarryController.MaximumBasketReachLevel)
            {
                existing[node.TargetLevel] = true;
            }
            else if (template == null
                && node.UpgradeId
                    == ProgressionSystem.UpgradeId.BasketCapacity
                && node.TargetLevel == 1)
            {
                template = node;
            }
        }

        if (template == null)
        {
            return;
        }

        for (int tier = 1;
            tier <= EggCarryController.MaximumBasketReachLevel;
            tier++)
        {
            if (existing[tier])
            {
                continue;
            }

            GameObject clone = Instantiate(
                template.gameObject,
                template.transform.parent);
            clone.name = $"Upgrade Basket Reach {tier}";
            clone.GetComponent<ProgressionNodeButton>()?.SetUpgrade(
                ProgressionSystem.UpgradeId.BasketReach,
                tier);
        }
    }

    private static void EnsureEggProgressionTiers(RectTransform treeContent)
    {
        int maximumWeightTier = ProgressionSystem.MaximumEggWeightLevel;
        int maximumValueTier = ProgressionSystem.MaximumEggValueLevel;
        ProgressionNodeButton[] weightNodes =
            new ProgressionNodeButton[maximumWeightTier + 1];
        ProgressionNodeButton[] valueNodes =
            new ProgressionNodeButton[maximumValueTier + 1];
        ProgressionNodeButton template = null;
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);

        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node == null || node.TargetLevel <= 0)
            {
                continue;
            }

            if (node.UpgradeId == ProgressionSystem.UpgradeId.EggWeight
                && node.TargetLevel <= maximumWeightTier)
            {
                weightNodes[node.TargetLevel] = node;
                template ??= node;
            }
            else if (node.UpgradeId == ProgressionSystem.UpgradeId.EggValue
                && node.TargetLevel <= maximumValueTier)
            {
                valueNodes[node.TargetLevel] = node;
            }
        }

        if (template == null)
        {
            return;
        }

        for (int tier = 1; tier <= maximumWeightTier; tier++)
        {
            if (weightNodes[tier] != null)
            {
                template = weightNodes[tier];
                continue;
            }

            GameObject clone = Instantiate(
                template.gameObject,
                template.transform.parent);
            clone.name = $"Upgrade Egg Weight Size {tier}";
            ProgressionNodeButton node =
                clone.GetComponent<ProgressionNodeButton>();
            node?.SetUpgrade(ProgressionSystem.UpgradeId.EggWeight, tier);
            weightNodes[tier] = node;
            if (node != null)
            {
                template = node;
            }
        }

        for (int tier = 1; tier <= maximumValueTier; tier++)
        {
            if (valueNodes[tier] != null)
            {
                continue;
            }

            ProgressionNodeButton source = weightNodes[
                Mathf.Clamp(tier, 1, maximumWeightTier)];
            if (source == null)
            {
                continue;
            }

            GameObject clone = Instantiate(
                source.gameObject,
                source.transform.parent);
            clone.name = $"Upgrade Egg Value {tier}";
            clone.GetComponent<ProgressionNodeButton>()?.SetUpgrade(
                ProgressionSystem.UpgradeId.EggValue,
                tier);
        }
    }

    private static void EnsurePremiumEggTiers(RectTransform treeContent)
    {
        int maximumTier = ProgressionSystem.MaximumRareEggChanceLevel;
        bool[] existing = new bool[maximumTier + 1];
        ProgressionNodeButton template = null;
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);
        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node == null
                || node.UpgradeId
                    != ProgressionSystem.UpgradeId.RareEggChance
                || node.TargetLevel <= 0)
            {
                continue;
            }

            if (node.TargetLevel <= maximumTier)
            {
                existing[node.TargetLevel] = true;
            }
            if (template == null || node.TargetLevel == 8)
            {
                template = node;
            }
        }

        if (template == null)
        {
            return;
        }

        for (int tier = 1; tier <= maximumTier; tier++)
        {
            if (existing[tier])
            {
                continue;
            }

            GameObject clone = Instantiate(
                template.gameObject,
                template.transform.parent);
            clone.name = $"Upgrade Premium Eggs {tier}";
            clone.GetComponent<ProgressionNodeButton>()?.SetUpgrade(
                ProgressionSystem.UpgradeId.RareEggChance,
                tier);
        }
    }

    private static void EnsureChickenPerkTiers(RectTransform treeContent)
    {
        int maximumTier = ProgressionSystem.MaximumChickenPerksLevel;
        bool[] existing = new bool[maximumTier + 1];
        ProgressionNodeButton template = null;
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);

        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node == null)
            {
                continue;
            }

            if (node.UpgradeId == ProgressionSystem.UpgradeId.ChickenPerks
                && node.TargetLevel > 0
                && node.TargetLevel <= maximumTier)
            {
                existing[node.TargetLevel] = true;
            }
            else if (node.UpgradeId
                    == ProgressionSystem.UpgradeId.RareEggChance
                && (template == null || node.TargetLevel == 8))
            {
                template = node;
            }
        }

        if (template == null)
        {
            return;
        }

        for (int tier = 1; tier <= maximumTier; tier++)
        {
            if (existing[tier])
            {
                continue;
            }

            GameObject clone = Instantiate(
                template.gameObject,
                template.transform.parent);
            clone.name = $"Upgrade Chicken Perks {tier}";
            clone.GetComponent<ProgressionNodeButton>()?.SetUpgrade(
                ProgressionSystem.UpgradeId.ChickenPerks,
                tier);
        }
    }

    private static void EnsureTruckBonusTiers(RectTransform treeContent)
    {
        ProgressionNodeButton template = null;
        bool[] existing = new bool[
            ProgressionSystem.MaximumTruckBonusLevel + 1];
        ProgressionNodeButton[] nodes =
            treeContent.GetComponentsInChildren<ProgressionNodeButton>(true);
        for (int index = 0; index < nodes.Length; index++)
        {
            ProgressionNodeButton node = nodes[index];
            if (node == null)
            {
                continue;
            }

            if (node.UpgradeId == ProgressionSystem.UpgradeId.TruckBonus
                && node.TargetLevel > 0
                && node.TargetLevel
                    <= ProgressionSystem.MaximumTruckBonusLevel)
            {
                existing[node.TargetLevel] = true;
            }
            else if (template == null
                && node.UpgradeId
                    == ProgressionSystem.UpgradeId.BasketCapacity
                && node.TargetLevel == 1)
            {
                template = node;
            }
        }

        if (template == null)
        {
            return;
        }

        for (int tier = 1;
             tier <= ProgressionSystem.MaximumTruckBonusLevel;
             tier++)
        {
            if (existing[tier])
            {
                continue;
            }

            GameObject clone = Instantiate(
                template.gameObject,
                template.transform.parent);
            clone.name = $"Upgrade Truck Bonus {tier}";
            clone.GetComponent<ProgressionNodeButton>()?.SetUpgrade(
                ProgressionSystem.UpgradeId.TruckBonus,
                tier);
        }
    }

    private static void SetChildRect(
        Transform parent,
        string childName,
        Vector2 position,
        Vector2 size)
    {
        RectTransform child = parent.Find(childName) as RectTransform;
        if (child != null)
        {
            SetRuntimeRect(child, position, size);
        }
    }

    private static void SetRuntimeRect(
        RectTransform rectTransform,
        Vector2 position,
        Vector2 size)
    {
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = size;
    }

#if UNITY_EDITOR

    private static void CreateProgressionHeader(
        Transform parent,
        string title,
        string glyph,
        Vector2 position,
        Color color,
        float width = 220f)
    {
        RectTransform header = CreateUiObject($"{title} Branch", parent);
        SetRect(header, position, new Vector2(width, 54f));
        Image background = header.gameObject.AddComponent<Image>();
        background.color = new Color(color.r, color.g, color.b, 0.32f);
        background.raycastTarget = false;
        CreateIconBadge(
            $"{title} Icon",
            header,
            new Vector2(-width * 0.5f + 24f, 0f),
            new Vector2(40f, 40f),
            glyph,
            color);
        TMP_Text label = CreateText(
            $"{title} Label",
            header,
            23f,
            TextAlignmentOptions.Left);
        label.text = title;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        SetRect(
            label.rectTransform,
            new Vector2(20f, 0f),
            new Vector2(width - 64f, 46f));
    }

    private static void CreateTreeConnector(
        Transform parent,
        Vector2 start,
        Vector2 end,
        Color color)
    {
        RectTransform connector = CreateUiObject("Branch Connector", parent);
        Vector2 direction = end - start;
        SetRect(
            connector,
            (start + end) * 0.5f,
            new Vector2(direction.magnitude, 4f));
        connector.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        Image image = connector.gameObject.AddComponent<Image>();
        image.color = new Color(color.r, color.g, color.b, 0.45f);
        image.raycastTarget = false;
        connector.SetAsFirstSibling();
    }

    private static Button CreateProgressionNode(
        string objectName,
        Transform parent,
        Vector2 position,
        ProgressionSystem.UpgradeId id,
        Color color,
        int targetLevel = 0,
        float rootWidth = 140f)
    {
        bool tierNode = targetLevel > 0;
        float nodeWidth = tierNode
            ? id == ProgressionSystem.UpgradeId.CrosshatcherQuality
                ? 108f
                : 92f
            : rootWidth;
        Button button = CreateButton(
            objectName,
            parent,
            position,
            new Vector2(nodeWidth, tierNode ? 62f : 72f),
            string.Empty,
            color);
        Outline selectionOutline = button.gameObject.AddComponent<Outline>();
        selectionOutline.effectColor = new Color(0f, 0f, 0f, 0.48f);
        selectionOutline.effectDistance = new Vector2(1.5f, -1.5f);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        label.alignment = tierNode
            ? TextAlignmentOptions.Center
            : TextAlignmentOptions.Left;
        label.fontSize = tierNode
            ? id == ProgressionSystem.UpgradeId.CrosshatcherQuality
                ? 10.5f
                : 11.5f
            : 12.5f;
        label.margin = tierNode
            ? new Vector4(3f, 3f, 3f, 18f)
            : new Vector4(38f, 5f, 4f, 19f);
        label.textWrappingMode = TextWrappingModes.NoWrap;

        TMP_Text icon = null;
        TMP_Text cost = null;
        Image fill = null;
        if (tierNode)
        {
            cost = CreateText(
                "Node Cost",
                button.transform,
                9f,
                TextAlignmentOptions.Center);
            cost.fontStyle = FontStyles.Bold;
            cost.color = new Color(1f, 0.88f, 0.35f);
            SetRect(
                cost.rectTransform,
                new Vector2(0f, -22f),
                new Vector2(nodeWidth - 8f, 14f));
        }
        else
        {
            RectTransform badge = CreateIconBadge(
                "Node Icon",
                button.transform,
                new Vector2(-nodeWidth * 0.5f + 17f, 7f),
                new Vector2(30f, 30f),
                "?",
                new Color(
                    Mathf.Min(1f, color.r + 0.18f),
                    Mathf.Min(1f, color.g + 0.18f),
                    Mathf.Min(1f, color.b + 0.18f)));
            icon = badge.GetComponentInChildren<TMP_Text>();

            cost = CreateText(
                "Node Cost",
                button.transform,
                11f,
                TextAlignmentOptions.Right);
            cost.fontStyle = FontStyles.Bold;
            cost.color = new Color(1f, 0.88f, 0.35f);
            SetRect(
                cost.rectTransform,
                new Vector2(nodeWidth * 0.5f - 46f, -20f),
                new Vector2(84f, 15f));

            fill = CreateProgressBar(
                "Node Affordability",
                button.transform,
                new Vector2(0f, -31f),
                new Vector2(nodeWidth - 20f, 5f),
                new Color(1f, 0.73f, 0.16f),
                out TMP_Text hiddenProgressText);
            hiddenProgressText.gameObject.SetActive(false);
        }

        ProgressionNodeButton node = button.gameObject.AddComponent<ProgressionNodeButton>();
        node.Configure(
            id,
            icon,
            label,
            cost,
            fill,
            selectionOutline,
            color,
            targetLevel);
        return button;
    }

    private static void CreateProgressionPreview(
        Transform parent,
        ProgressionTreePreview treePreview,
        Button dismissButton)
    {
        RectTransform panel = CreateUiObject("Node Preview", parent);
        SetRect(panel, Vector2.zero, new Vector2(320f, 270f));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = new Color(0.055f, 0.065f, 0.055f, 0.995f);
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.76f, 0.22f, 0.88f);
        outline.effectDistance = new Vector2(3f, -3f);

        RectTransform accent = CreateUiObject("Accent", panel);
        accent.anchorMin = new Vector2(0f, 1f);
        accent.anchorMax = Vector2.one;
        accent.pivot = new Vector2(0.5f, 1f);
        accent.anchoredPosition = Vector2.zero;
        accent.sizeDelta = new Vector2(0f, 7f);
        Image accentImage = accent.gameObject.AddComponent<Image>();
        accentImage.color = new Color(1f, 0.72f, 0.16f);
        accentImage.raycastTarget = false;

        TMP_Text title = CreateText(
            "Preview Title",
            panel,
            25f,
            TextAlignmentOptions.Left);
        title.color = Color.white;
        title.fontStyle = FontStyles.Bold;
        SetRect(title.rectTransform, new Vector2(0f, 105f), new Vector2(286f, 38f));

        TMP_Text level = CreateText(
            "Preview Level",
            panel,
            13f,
            TextAlignmentOptions.Left);
        level.color = new Color(0.7f, 0.75f, 0.68f);
        level.fontStyle = FontStyles.Bold;
        SetRect(level.rectTransform, new Vector2(0f, 77f), new Vector2(286f, 24f));

        TMP_Text description = CreateText(
            "Preview Description",
            panel,
            15f,
            TextAlignmentOptions.TopLeft);
        description.color = new Color(0.93f, 0.95f, 0.9f);
        description.textWrappingMode = TextWrappingModes.Normal;
        SetRect(
            description.rectTransform,
            new Vector2(0f, 18f),
            new Vector2(286f, 92f));

        TMP_Text price = CreateText(
            "Preview Price",
            panel,
            16f,
            TextAlignmentOptions.Left);
        price.color = new Color(1f, 0.86f, 0.32f);
        price.fontStyle = FontStyles.Bold;
        SetRect(price.rectTransform, new Vector2(0f, -44f), new Vector2(286f, 26f));

        Image affordabilityFill = CreateProgressBar(
            "Preview Affordability",
            panel,
            new Vector2(0f, -69f),
            new Vector2(286f, 15f),
            new Color(1f, 0.69f, 0.13f),
            out TMP_Text affordabilityText);

        Button buy = CreateButton(
            "Preview Buy",
            panel,
            new Vector2(0f, -105f),
            new Vector2(286f, 42f),
            "BUY",
            new Color(0.16f, 0.62f, 0.31f));
        TMP_Text buyText = buy.GetComponentInChildren<TMP_Text>();
        buyText.fontSize = 17f;
        buyText.fontStyle = FontStyles.Bold;

        treePreview.Configure(
            panel.gameObject,
            title,
            level,
            description,
            price,
            affordabilityText,
            affordabilityFill,
            buy,
            buyText,
            dismissButton);
        panel.gameObject.SetActive(false);
    }

    private void BuildLegacySuppliesShopUi(Transform canvasTransform)
    {
        suppliesShopScreen = CreateUiObject("Supplies Shop Screen", canvasTransform).gameObject;
        StretchToParent(suppliesShopScreen.GetComponent<RectTransform>());
        Image backdrop = suppliesShopScreen.AddComponent<Image>();
        backdrop.color = new Color(0.035f, 0.045f, 0.04f, 0.96f);

        RectTransform card = CreateUiObject("Supplies Shop Card", suppliesShopScreen.transform);
        SetRect(card, Vector2.zero, new Vector2(940f, 760f));
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.color = new Color(0.98f, 0.88f, 0.62f, 1f);

        TMP_Text title = CreateText(
            "Shop Title",
            card,
            42f,
            TextAlignmentOptions.Left);
        title.text = "SUPPLIES SHOP";
        title.fontStyle = FontStyles.Bold;
        title.color = new Color(0.2f, 0.13f, 0.07f);
        SetRect(title.rectTransform, new Vector2(-250f, 310f), new Vector2(390f, 58f));

        CreateIconBadge(
            "Cash Coin",
            card,
            new Vector2(265f, 310f),
            new Vector2(44f, 44f),
            "$",
            new Color(0.94f, 0.63f, 0.08f));
        shopBalanceText = CreateText(
            "Shop Balance",
            card,
            25f,
            TextAlignmentOptions.Left);
        shopBalanceText.color = new Color(0.2f, 0.16f, 0.08f);
        shopBalanceText.fontStyle = FontStyles.Bold;
        SetRect(shopBalanceText.rectTransform, new Vector2(375f, 310f), new Vector2(160f, 44f));

        RectTransform feedCard = CreateShopCard(
            "Feed Card",
            card,
            new Vector2(0f, 160f),
            new Vector2(840f, 190f));
        CreateIconBadge(
            "Feed Icon",
            feedCard,
            new Vector2(-350f, 25f),
            new Vector2(82f, 82f),
            "F",
            new Color(0.76f, 0.46f, 0.1f));
        TMP_Text feedTitle = CreateText(
            "Feed Title",
            feedCard,
            27f,
            TextAlignmentOptions.Left);
        feedTitle.text = "FEED";
        feedTitle.fontStyle = FontStyles.Bold;
        feedTitle.color = new Color(1f, 0.82f, 0.28f);
        SetRect(feedTitle.rectTransform, new Vector2(0f, 60f), new Vector2(560f, 42f));
        shopFeedDetailsText = CreateText(
            "Feed Details",
            feedCard,
            18f,
            TextAlignmentOptions.Left);
        shopFeedDetailsText.color = Color.white;
        shopFeedDetailsText.textWrappingMode = TextWrappingModes.Normal;
        shopFeedDetailsText.lineSpacing = 1f;
        SetRect(shopFeedDetailsText.rectTransform, new Vector2(0f, 12f), new Vector2(560f, 66f));
        buyFeedButton = CreateButton(
            "Buy Feed",
            feedCard,
            new Vector2(-125f, -52f),
            new Vector2(300f, 46f),
            "BUY BAG",
            new Color(0.72f, 0.38f, 0.08f));
        feedBagProgressFill = CreateProgressBar(
            "Feed Bag Affordability",
            feedCard,
            new Vector2(-125f, -78f),
            new Vector2(300f, 12f),
            new Color(0.95f, 0.64f, 0.12f),
            out feedBagProgressText);
        upgradeFeedButton = CreateButton(
            "Upgrade Feed",
            feedCard,
            new Vector2(215f, -52f),
            new Vector2(340f, 46f),
            "UNLOCK NEXT TIER",
            new Color(0.48f, 0.25f, 0.65f));
        feedUnlockProgressFill = CreateProgressBar(
            "Feed Unlock Affordability",
            feedCard,
            new Vector2(215f, -78f),
            new Vector2(340f, 12f),
            new Color(0.65f, 0.38f, 0.82f),
            out feedUnlockProgressText);

        RectTransform incubatorCard = CreateShopCard(
            "Incubator Card",
            card,
            new Vector2(0f, -25f),
            new Vector2(840f, 150f));
        CreateIconBadge(
            "Incubator Icon",
            incubatorCard,
            new Vector2(-350f, 5f),
            new Vector2(82f, 82f),
            "I",
            new Color(0.12f, 0.63f, 0.4f));
        TMP_Text incubatorTitle = CreateText(
            "Incubator Title",
            incubatorCard,
            27f,
            TextAlignmentOptions.Left);
        incubatorTitle.text = "INCUBATOR";
        incubatorTitle.fontStyle = FontStyles.Bold;
        incubatorTitle.color = new Color(0.45f, 0.95f, 0.68f);
        SetRect(incubatorTitle.rectTransform, new Vector2(0f, 42f), new Vector2(560f, 38f));
        shopIncubatorDetailsText = CreateText(
            "Incubator Details",
            incubatorCard,
            18f,
            TextAlignmentOptions.Left);
        shopIncubatorDetailsText.color = Color.white;
        shopIncubatorDetailsText.textWrappingMode = TextWrappingModes.Normal;
        shopIncubatorDetailsText.lineSpacing = 1f;
        SetRect(
            shopIncubatorDetailsText.rectTransform,
            new Vector2(0f, 0f),
            new Vector2(560f, 62f));
        upgradeIncubatorButton = CreateButton(
            "Upgrade Incubator",
            incubatorCard,
            new Vector2(215f, -35f),
            new Vector2(340f, 46f),
            "INSTALL",
            new Color(0.1f, 0.55f, 0.34f));
        incubatorProgressFill = CreateProgressBar(
            "Incubator Affordability",
            incubatorCard,
            new Vector2(215f, -62f),
            new Vector2(340f, 12f),
            new Color(0.25f, 0.8f, 0.5f),
            out incubatorProgressText);

        RectTransform collectionCard = CreateShopCard(
            "Egg Collection Card",
            card,
            new Vector2(0f, -195f),
            new Vector2(840f, 170f));
        CreateIconBadge(
            "Collection Icon",
            collectionCard,
            new Vector2(-350f, 5f),
            new Vector2(82f, 82f),
            "C",
            new Color(0.18f, 0.55f, 0.82f));
        TMP_Text collectionTitle = CreateText(
            "Collection Title",
            collectionCard,
            27f,
            TextAlignmentOptions.Left);
        collectionTitle.text = "EGG COLLECTION";
        collectionTitle.fontStyle = FontStyles.Bold;
        collectionTitle.color = new Color(0.52f, 0.82f, 1f);
        SetRect(
            collectionTitle.rectTransform,
            new Vector2(0f, 48f),
            new Vector2(560f, 38f));
        shopCollectionDetailsText = CreateText(
            "Collection Details",
            collectionCard,
            17f,
            TextAlignmentOptions.Left);
        shopCollectionDetailsText.color = Color.white;
        shopCollectionDetailsText.textWrappingMode = TextWrappingModes.Normal;
        shopCollectionDetailsText.lineSpacing = 0f;
        SetRect(
            shopCollectionDetailsText.rectTransform,
            new Vector2(0f, 0f),
            new Vector2(560f, 68f));
        upgradeCollectionButton = CreateButton(
            "Upgrade Collection",
            collectionCard,
            new Vector2(215f, -42f),
            new Vector2(340f, 46f),
            "UPGRADE",
            new Color(0.12f, 0.42f, 0.72f));
        collectionProgressFill = CreateProgressBar(
            "Collection Affordability",
            collectionCard,
            new Vector2(215f, -69f),
            new Vector2(340f, 12f),
            new Color(0.3f, 0.68f, 0.95f),
            out collectionProgressText);

        shopStatusText = CreateText(
            "Shop Status",
            card,
            16f,
            TextAlignmentOptions.Center);
        shopStatusText.color = new Color(0.27f, 0.18f, 0.09f);
        SetRect(shopStatusText.rectTransform, new Vector2(0f, -300f), new Vector2(800f, 28f));

        doneShoppingButton = CreateButton(
            "Done Shopping",
            card,
            new Vector2(0f, -340f),
            new Vector2(280f, 52f),
            "DONE - NEXT ROUND",
            new Color(0.82f, 0.26f, 0.1f));
        suppliesShopScreen.SetActive(false);
    }

    private static RectTransform CreateShopCard(
        string objectName,
        Transform parent,
        Vector2 position,
        Vector2 size)
    {
        RectTransform card = CreateUiObject(objectName, parent);
        SetRect(card, position, size);
        Image image = card.gameObject.AddComponent<Image>();
        image.color = new Color(0.11f, 0.13f, 0.1f, 0.98f);
        return card;
    }

    private static RectTransform CreateIconBadge(
        string objectName,
        Transform parent,
        Vector2 position,
        Vector2 size,
        string glyph,
        Color color)
    {
        RectTransform icon = CreateUiObject(objectName, parent);
        SetRect(icon, position, size);
        Image background = icon.gameObject.AddComponent<Image>();
        background.sprite =
            UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        background.color = color;
        background.raycastTarget = false;

        TMP_Text symbol = CreateText(
            "Symbol",
            icon,
            size.y * 0.46f,
            TextAlignmentOptions.Center);
        symbol.text = glyph;
        symbol.color = Color.white;
        symbol.fontStyle = FontStyles.Bold;
        StretchToParent(symbol.rectTransform);
        return icon;
    }

    private static Image CreateProgressBar(
        string objectName,
        Transform parent,
        Vector2 position,
        Vector2 size,
        Color fillColor,
        out TMP_Text progressText)
    {
        RectTransform backgroundRect = CreateUiObject(objectName, parent);
        SetRect(backgroundRect, position, size);
        Image background = backgroundRect.gameObject.AddComponent<Image>();
        background.color = new Color(0.04f, 0.045f, 0.04f, 0.9f);

        RectTransform fillRect = CreateUiObject("Fill", backgroundRect);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fill = fillRect.gameObject.AddComponent<Image>();
        fill.color = fillColor;
        fill.raycastTarget = false;

        progressText = CreateText(
            "Progress Text",
            backgroundRect,
            10f,
            TextAlignmentOptions.Center);
        progressText.color = Color.white;
        progressText.fontStyle = FontStyles.Bold;
        progressText.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        progressText.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        progressText.rectTransform.anchoredPosition = Vector2.zero;
        progressText.rectTransform.sizeDelta = new Vector2(0f, 14f);
        return fill;
    }

    private static Button CreateButton(
        string objectName,
        Transform parent,
        Vector2 position,
        Vector2 size,
        string labelText,
        Color color)
    {
        RectTransform buttonRect = CreateUiObject(objectName, parent);
        SetRect(buttonRect, position, size);
        Image image = buttonRect.gameObject.AddComponent<Image>();
        image.color = color;
        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.2f);
        colors.disabledColor = new Color(0.3f, 0.31f, 0.3f, 0.9f);
        button.colors = colors;

        TMP_Text label = CreateText(
            "Label",
            buttonRect,
            Mathf.Clamp(size.y * 0.35f, 15f, 22f),
            TextAlignmentOptions.Center);
        label.text = labelText;
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        StretchToParent(label.rectTransform);
        return button;
    }

    private static RectTransform CreateUiObject(string objectName, Transform parent)
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform));
        RectTransform rectTransform = uiObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        return rectTransform;
    }

    private static TMP_Text CreateText(
        string objectName,
        Transform parent,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        RectTransform rectTransform = CreateUiObject(objectName, parent);
        TextMeshProUGUI text = rectTransform.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            UiFontAssetPath);
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rectTransform, Vector2 position, Vector2 size)
    {
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = size;
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }
#endif

    private void OnValidate()
    {
        roundDuration = Mathf.Max(1f, roundDuration);
        countdownStepDuration = Mathf.Max(0.1f, countdownStepDuration);
        baseTruckEggTarget = Mathf.Max(1, baseTruckEggTarget);
        earlyTruckTargetGrowth = Mathf.Clamp(
            earlyTruckTargetGrowth,
            1f,
            1.5f);
        earlyTruckTargetRounds = Mathf.Max(1, earlyTruckTargetRounds);
        lateTruckTargetIncreasePerRound = Mathf.Max(
            0f,
            lateTruckTargetIncreasePerRound);
        maximumTruckEggTarget = Mathf.Max(
            baseTruckEggTarget,
            maximumTruckEggTarget);
        baseRoundCashQuotaCents = Math.Max(100L, baseRoundCashQuotaCents);
        earlyCashQuotaGrowth = Mathf.Clamp(earlyCashQuotaGrowth, 1f, 2f);
        earlyCashQuotaEndRound = Mathf.Max(1, earlyCashQuotaEndRound);
        midCashQuotaGrowth = Mathf.Clamp(midCashQuotaGrowth, 1f, 2f);
        midCashQuotaEndRound = Mathf.Max(
            earlyCashQuotaEndRound + 1,
            midCashQuotaEndRound);
        lateCashQuotaGrowth = Mathf.Clamp(lateCashQuotaGrowth, 1f, 2f);
        endgameCashQuotaStartRound = Mathf.Max(
            midCashQuotaEndRound + 1,
            endgameCashQuotaStartRound);
        endgameCashQuotaGrowth = Mathf.Clamp(
            endgameCashQuotaGrowth,
            1f,
            2f);
        sustainedCashQuotaStartRound = Mathf.Max(
            endgameCashQuotaStartRound + 1,
            sustainedCashQuotaStartRound);
        sustainedCashQuotaGrowth = Mathf.Clamp(
            sustainedCashQuotaGrowth,
            1f,
            2f);
        maximumRoundCashQuotaCents = Math.Max(
            baseRoundCashQuotaCents,
            maximumRoundCashQuotaCents);
        truckDepartureDuration = Mathf.Max(0.1f, truckDepartureDuration);
        cursorMovementMaximumPitch = Mathf.Max(
            cursorMovementMinimumPitch,
            cursorMovementMaximumPitch);
        cursorMovementSpeedForMaximum = Mathf.Max(
            0.01f,
            cursorMovementSpeedForMaximum);
        cursorMovementResponse = Mathf.Max(0f, cursorMovementResponse);
        vacuumSfxVolumeScale = Mathf.Clamp01(vacuumSfxVolumeScale);
        vacuumSfxFadeDuration = Mathf.Max(0f, vacuumSfxFadeDuration);
        resultsTickSfxVolume = Mathf.Clamp01(
            resultsTickSfxVolume);
        resultsTickMinimumInterval = Mathf.Max(
            0f,
            resultsTickMinimumInterval);
        cashRewardThresholdCents = Mathf.Max(1, cashRewardThresholdCents);
        cashTransitionStartCents = Mathf.Clamp(
            cashTransitionStartCents,
            1,
            cashRewardThresholdCents);
        maximumRewardParticlesPerBurst = Mathf.Clamp(
            maximumRewardParticlesPerBurst,
            250,
            10000);
        cashNoteParticleDensity = Mathf.Clamp(
            cashNoteParticleDensity,
            0.05f,
            1f);
        maximumCashNotesPerBurst = Mathf.Clamp(
            maximumCashNotesPerBurst,
            25,
            1000);
        rewardParticleTrailDuration = Mathf.Clamp(
            rewardParticleTrailDuration,
            0.5f,
            5f);
        maximumRewardParticlesPerSecond = Mathf.Clamp(
            maximumRewardParticlesPerSecond,
            100f,
            5000f);
        rewardParticleEmissionJitter = Mathf.Clamp(
            rewardParticleEmissionJitter,
            0f,
            0.75f);
        coinRewardEmitterRadiusPixels = Mathf.Clamp(
            coinRewardEmitterRadiusPixels,
            0f,
            100f);
        cashRewardEmitterRadiusPixels = Mathf.Clamp(
            cashRewardEmitterRadiusPixels,
            0f,
            100f);
        rewardLandingPitchVariation = Mathf.Clamp(
            rewardLandingPitchVariation,
            0f,
            0.25f);
        rewardLandingVolumeVariation = Mathf.Clamp(
            rewardLandingVolumeVariation,
            0f,
            0.25f);
        cashLandingVolumeScale = Mathf.Clamp01(
            cashLandingVolumeScale);
        maximumRewardLandingSoundsPerSecond = Mathf.Clamp(
            maximumRewardLandingSoundsPerSecond,
            5f,
            60f);
        coalescedRewardLandingVolumeBoost = Mathf.Clamp(
            coalescedRewardLandingVolumeBoost,
            0f,
            0.25f);
        flyingCashMatCapLightStrength = Mathf.Clamp(
            flyingCashMatCapLightStrength,
            0f,
            8f);
        flyingCashMinimumSpinDegreesPerSecond = Mathf.Max(
            0f,
            flyingCashMinimumSpinDegreesPerSecond);
        flyingCashMaximumSpinDegreesPerSecond = Mathf.Max(
            flyingCashMinimumSpinDegreesPerSecond,
            flyingCashMaximumSpinDegreesPerSecond);
    }
}
