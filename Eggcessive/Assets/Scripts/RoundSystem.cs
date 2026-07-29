using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(ProgressionSystem))]
public sealed class RoundSystem : MonoBehaviour
{
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

    [Header("Truck")]
    [SerializeField, Range(0.1f, 0.5f)] private float truckVisualScale = 0.22f;
    [SerializeField, Min(0.1f)] private float truckDepartureDuration = 2.5f;

    private Transform truckStart;
    private Transform truckStop;
    private Transform truckEnd;
    private Transform truck;
    private readonly List<Material> truckMaterials = new List<Material>();
    private Canvas gameplayHudCanvas;

    [Header("Authored UI")]
    [SerializeField] private GameObject intermissionScreen;
    [SerializeField] private GameObject countdownDisplay;
    [SerializeField] private GameObject timerDisplay;
    [SerializeField] private GameObject liveStatsDisplay;
    [SerializeField] private GameObject resultsScreen;
    [SerializeField] private GameObject suppliesShopScreen;
    [SerializeField] private TMP_Text intermissionTitle;
    [SerializeField] private TMP_Text countdownText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text liveStatsText;
    [SerializeField] private TMP_Text liveStatsValueText;
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
    [SerializeField] private RectTransform roundCanvasRect;
    [SerializeField] private RectTransform coinEffectLayer;
    [SerializeField] private RectTransform coinHudTarget;
    [SerializeField] private GameObject flyingCoinPrefab = null;
    [SerializeField, Min(0f)]
    private float flyingCoinSpinDegreesPerSecond = 1440f;
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
    [SerializeField, Range(0f, 1f)] private float truckSfxVolume = 0.8f;
    [SerializeField, Min(0f)] private float truckExitSoundLeadTime = 0.3f;

    [Header("Ambience")]
    [SerializeField] private AudioClip farmAmbienceSfx = null;
    [SerializeField, Range(0f, 1f)] private float farmAmbienceVolume = 1f;

    private Coroutine truckMovement;
    private Coroutine resultsAnimation;
    private float roundTimeRemaining;
    private float roundElapsed;
    private float liveStatsRefreshTime;
    private int roundNumber;
    private int roundEggsCollected;
    private int roundCashMade;
    private int roundEggsLaid;
    private int roundEggsIncubated;
    private int roundChickensHatched;
    private int finalChickenCount;
    private int roundEggTarget;
    private int eggsTowardTruck;
    private int trucksFilled;
    private int roundQuotaBonus;
    private int pendingTruckReplacements;
    private Coroutine rewardDisplayCoroutine;
    private TMP_Text activeRewardText;
    private Vector2 activeRewardStartPosition;
    private float lastRewardAddedTime;
    private int accumulatedRewardCents;
    private int activeCoinAnimations;
    private int shopDisplayedBalanceCents;
    private Tweener shopBalanceTween;
    private bool skipResultsAnimation;
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
    private AudioSource farmAmbienceAudioSource;
    private Vector2 lastCursorPosition;
    private float cursorMovementIntensity;
    private bool hasCursorPosition;
    private int nextCoinAudioSource;
    private int lastCoinLandingClipIndex = -1;

    public static RoundSystem Instance { get; private set; }
    public RoundPhase Phase { get; private set; } = RoundPhase.Intermission;
    public float TimeRemaining => roundTimeRemaining;
    public int RoundNumber => roundNumber;
    public int EggTarget => roundEggTarget;
    public int EggsTowardTruck => eggsTowardTruck;
    public int TrucksFilled => trucksFilled;
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
        ApplyWideSuppliesShopLayout();
        BindButtonClickSfx();
        BindUiEvents();
        EggScoreHud gameplayHud = FindFirstObjectByType<EggScoreHud>(
            FindObjectsInactive.Include);
        gameplayHudCanvas = gameplayHud != null
            ? gameplayHud.GetComponent<Canvas>()
            : null;
        EggContainer.EggCollected += HandleEggCollected;
        ChickenController.EggLaid += HandleEggLaid;
        IncubatorController.ChickenHatched += HandleChickenHatched;
        IncubatorController.EggsAccepted += HandleEggsAccepted;
        EggScoreHud.BalanceChanged += HandleBalanceChanged;
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

    private void PlayCoinLandingSfx()
    {
        if (coinAudioSources.Length == 0
            || coinLandingSfx == null
            || coinLandingSfx.Length == 0)
        {
            return;
        }

        int clipIndex = UnityEngine.Random.Range(0, coinLandingSfx.Length);
        if (coinLandingSfx.Length > 1
            && clipIndex == lastCoinLandingClipIndex)
        {
            clipIndex = (clipIndex + UnityEngine.Random.Range(
                1,
                coinLandingSfx.Length)) % coinLandingSfx.Length;
        }

        AudioClip clip = coinLandingSfx[clipIndex];
        if (clip == null)
        {
            return;
        }

        lastCoinLandingClipIndex = clipIndex;
        AudioSource source = coinAudioSources[nextCoinAudioSource];
        nextCoinAudioSource =
            (nextCoinAudioSource + 1) % coinAudioSources.Length;
        source.Stop();
        source.clip = clip;
        source.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
        source.volume = uiSfxVolume;
        source.Play();
    }

    private bool HasAuthoredUi()
    {
        return intermissionScreen != null
            && countdownDisplay != null
            && timerDisplay != null
            && liveStatsDisplay != null
            && resultsTitleText != null
            && resultsScreen != null
            && suppliesShopScreen != null
            && readyButton != null
            && intermissionShopButton != null
            && resultsShopButton != null
            && resultsContinueButton != null
            && doneShoppingButton != null
            && shopBalanceText != null
            && shopStatusText != null
            && roundCanvasRect != null
            && coinEffectLayer != null
            && coinHudTarget != null
            && flyingCoinPrefab != null
            && floatingRewardPrefab != null;
    }

    private void BindUiEvents()
    {
        readyButton.onClick.AddListener(HandleReadyClicked);
        intermissionShopButton.onClick.AddListener(HandleIntermissionShopClicked);
        resultsShopButton.onClick.AddListener(HandleResultsShopClicked);
        resultsContinueButton.onClick.AddListener(HandleResultsContinueClicked);
        doneShoppingButton.onClick.AddListener(ShowIntermission);
    }

    private void Update()
    {
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

        roundElapsed += Time.deltaTime;
        roundTimeRemaining = Mathf.Max(0f, roundTimeRemaining - Time.deltaTime);
        RefreshTimer();
        liveStatsRefreshTime -= Time.deltaTime;

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
        doneShoppingButton?.onClick.RemoveListener(ShowIntermission);

        EggContainer.EggCollected -= HandleEggCollected;
        ChickenController.EggLaid -= HandleEggLaid;
        IncubatorController.ChickenHatched -= HandleChickenHatched;
        IncubatorController.EggsAccepted -= HandleEggsAccepted;
        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
        shopBalanceTween?.Kill();
        ClearRewardPresentation();

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
        roundNumber++;
        roundTimeRemaining = roundDuration;
        roundElapsed = 0f;
        roundEggsCollected = 0;
        roundCashMade = 0;
        roundEggsLaid = 0;
        roundEggsIncubated = 0;
        roundChickensHatched = 0;
        roundEggTarget = CalculateEggTarget(roundNumber);
        eggsTowardTruck = 0;
        trucksFilled = 0;
        roundQuotaBonus = 0;
        pendingTruckReplacements = 0;
        liveStatsRefreshTime = 0f;
        SetPhase(RoundPhase.InProgress);
        timerDisplay.SetActive(true);
        liveStatsDisplay.SetActive(true);
        RefreshTimer();
        RefreshLiveStats();
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
        timerText.text = $"ROUND {roundNumber}\nFINALISING";
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

        while (activeCoinAnimations > 0 || rewardDisplayCoroutine != null)
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

    private IEnumerator MoveTruck(Transform destination, float duration)
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

    private float GetTruckArrivalDuration()
    {
        return Mathf.Max(0.1f, countdownStepDuration * 3f - 1f);
    }

    private void ShowIntermission()
    {
        SetPhase(RoundPhase.Intermission);
        intermissionTitle.text = roundNumber == 0
            ? "FIRST DELIVERY"
            : $"ROUND {roundNumber + 1}";
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
        SetPhase(RoundPhase.Results);
        resultsTitleText.text = $"ROUND {roundNumber} COMPLETE";
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
        resultsCashText.text = "—";
        resultsCollectedText.text = "—";
        resultsLaidText.text = "—";
        resultsPerMinuteText.text = "—";
        resultsHatchedText.text = "—";
        resultsChickenCountText.text = "—";
        resultsQuotaText.text = "—";

        yield return CountResult(
            resultsCashText,
            roundCashMade,
            value => FormatMoney(Mathf.RoundToInt(value)));
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
            value => value.ToString("0.0"));
        yield return CountResult(
            resultsHatchedText,
            roundChickensHatched,
            value => Mathf.RoundToInt(value).ToString());
        yield return CountResult(
            resultsChickenCountText,
            finalChickenCount,
            value => Mathf.RoundToInt(value).ToString());
        yield return CountResult(
            resultsQuotaText,
            trucksFilled,
            value => Mathf.RoundToInt(value).ToString());
        resultsQuotaText.text =
            $"{trucksFilled}  <size=16><color=#86A382>+{FormatMoney(roundQuotaBonus)}</color></size>";

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
            ShowIntermission();
        }
    }

    private void HandleResultsShopClicked()
    {
        if (Phase == RoundPhase.Results)
        {
            ShowSuppliesShop();
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
        int balance = EggScoreHud.CurrentCents;
        shopBalanceText.text = FormatMoney(shopDisplayedBalanceCents);

        if (suppliesShopScreen != null)
        {
            ProgressionNodeButton[] nodes =
                suppliesShopScreen.GetComponentsInChildren<ProgressionNodeButton>(true);

            for (int index = 0; index < nodes.Length; index++)
            {
                nodes[index].Refresh();
            }

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
                $"{foodShop.CurrentFeedSpeedMultiplier:0.##}x SPEED   •   " +
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
                    : $"{IncubatorController.GetCapacity(currentLevel)} CAPACITY   •   " +
                      $"{IncubatorController.GetProductionTime(currentLevel):0.##} SEC") +
                (incubatorShop.HasUpgrade
                    ? $"\nNEXT: {incubatorShop.NextCapacity} CAPACITY   •   " +
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
        int currentCash,
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
        return $"${cents / 100:N0}.{Mathf.Abs(cents % 100):D2}";
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
    }

    private void RefreshTimer()
    {
        int seconds = Mathf.CeilToInt(roundTimeRemaining);
        timerText.text =
            $"ROUND {roundNumber}  .  TRUCK {trucksFilled + 1}\n" +
            $"{seconds / 60}:{seconds % 60:D2}   .   " +
            $"EGGS {eggsTowardTruck}/{roundEggTarget}";
    }

    private void HandleEggCollected(int cents)
    {
        if (!IsRoundAcceptingEggs)
        {
            return;
        }

        roundEggsCollected++;
        roundCashMade += cents;

        if (IsRoundAcceptingEggs && roundEggTarget > 0)
        {
            eggsTowardTruck++;

            while (eggsTowardTruck >= roundEggTarget)
            {
                eggsTowardTruck -= roundEggTarget;
                CompleteTruckQuota();
            }
        }

        RefreshTimer();
        RefreshLiveStats();
    }

    private void CompleteTruckQuota()
    {
        trucksFilled++;
        int bonus = Mathf.RoundToInt(
            roundEggTarget
            * 75f
            * trucksFilled
            * Mathf.Pow(1.08f, Mathf.Max(0, roundNumber - 1)));
        roundQuotaBonus += bonus;
        roundCashMade += bonus;
        EggScoreHud.AddCents(bonus);

        if (truck != null)
        {
            ShowCoinReward(truck.position + Vector3.up * 0.45f, bonus);
        }

        pendingTruckReplacements++;

        if (truckMovement == null)
        {
            truckMovement = StartCoroutine(ReplaceFilledTrucks());
        }
    }

    private IEnumerator ReplaceFilledTrucks()
    {
        while (pendingTruckReplacements > 0
            && Phase == RoundPhase.InProgress)
        {
            pendingTruckReplacements--;
            yield return MoveTruck(
                truckEnd,
                truckDepartureDuration * 0.5f);
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

    public void ShowCoinReward(Vector3 worldPosition, int cents)
    {
        ShowCoinReward(worldPosition, cents, false);
    }

    public void ShowContainerCoinReward(Vector3 worldPosition, int cents)
    {
        ShowCoinReward(worldPosition, cents, true);
    }

    private void ShowCoinReward(
        Vector3 worldPosition,
        int cents,
        bool useTenCentCoins)
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

        Vector2 targetScreenPosition = RectTransformUtility.WorldToScreenPoint(
            canvasCamera,
            coinHudTarget.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            roundCanvasRect,
            targetScreenPosition,
            canvasCamera,
            out Vector2 targetPosition);

        int coinCount = useTenCentCoins
            ? Mathf.Clamp(Mathf.CeilToInt(cents / 10f), 1, 500)
            : Mathf.Clamp(Mathf.CeilToInt(cents / 100f), 1, 12);
        float coinStagger = Mathf.Min(0.055f, 0.65f / coinCount);
        AccumulateRewardNumber(startPosition, cents, useTenCentCoins);

        for (int index = 0; index < coinCount; index++)
        {
            activeCoinAnimations++;
            StartCoroutine(FlyCoinToHud(
                startPosition + UnityEngine.Random.insideUnitCircle * 18f,
                targetPosition,
                index * coinStagger));
        }
    }

    private void AccumulateRewardNumber(
        Vector2 startPosition,
        int cents,
        bool playCashRegisterOnStart)
    {
        const float accumulationWindow = 0.75f;
        float now = Time.unscaledTime;
        bool joinsCurrentReward = activeRewardText != null
            && now - lastRewardAddedTime <= accumulationWindow;
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

        accumulatedRewardCents += cents;
        lastRewardAddedTime = now;
        activeRewardStartPosition = Vector2.Lerp(
            activeRewardStartPosition,
            startPosition,
            0.35f);
        activeRewardText.text = $"+{FormatMoney(accumulatedRewardCents)}";
        activeRewardText.fontSize = Mathf.Clamp(
            34f + Mathf.Log10(
                1f + accumulatedRewardCents / 100f) * 12f,
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
        const float accumulationWindow = 0.75f;

        while (activeRewardText != null
            && Time.unscaledTime - lastRewardAddedTime < accumulationWindow)
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

    private IEnumerator FlyCoinToHud(
        Vector2 startPosition,
        Vector2 targetPosition,
        float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        GameObject coinObject = Instantiate(flyingCoinPrefab, coinEffectLayer);
        coinObject.name = "Flying Coin";
        RectTransform coin = coinObject.GetComponent<RectTransform>();
        coin.anchoredPosition = startPosition;
        float spinAngle = UnityEngine.Random.Range(0f, 360f);
        coin.localRotation = Quaternion.Euler(0f, spinAngle, 0f);

        const float duration = 0.62f;
        float elapsed = 0f;

        while (elapsed < duration && coin != null)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            Vector2 arc = Vector2.up * Mathf.Sin(progress * Mathf.PI) * 90f;
            coin.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, eased) + arc;
            coin.localScale = Vector3.one * Mathf.Lerp(1f, 0.65f, eased);
            spinAngle += flyingCoinSpinDegreesPerSecond * Time.deltaTime;
            coin.localRotation = Quaternion.Euler(0f, spinAngle, 0f);
            yield return null;
        }

        if (coin != null)
        {
            PlayCoinLandingSfx();
            Destroy(coin.gameObject);
        }

        activeCoinAnimations = Mathf.Max(0, activeCoinAnimations - 1);
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

    private void HandleBalanceChanged(int _)
    {
        if (Phase == RoundPhase.SuppliesShop)
        {
            AnimateShopBalanceTo(EggScoreHud.CurrentCents);
            RefreshShopUi();
        }
    }

    public void AnimateShopSpend(int cents)
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

    private void SetShopBalanceImmediate(int cents)
    {
        shopBalanceTween?.Kill();
        shopBalanceTween = null;
        shopDisplayedBalanceCents = Mathf.Max(0, cents);
        if (shopBalanceText != null)
        {
            shopBalanceText.text = FormatMoney(shopDisplayedBalanceCents);
        }
    }

    private void AnimateShopBalanceTo(int targetCents)
    {
        targetCents = Mathf.Max(0, targetCents);
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
        liveStatsValueText.text =
            $"{roundEggsCollected}\n" +
            $"{eggsPerMinute:0.0}\n" +
            $"+{FormatMoney(roundCashMade)}\n" +
            $"{CountChickens()}/{ChickenController.MaximumChickenCount}\n" +
            $"{trucksFilled}";
    }

    private static int CalculateEggTarget(int round)
    {
        return Mathf.Max(
            5,
            Mathf.CeilToInt(7f * Mathf.Pow(1.16f, Mathf.Max(0, round - 1))));
    }

    private static int CountChickens()
    {
        return FindObjectsByType<ChickenController>(FindObjectsSortMode.None).Length;
    }

    private void SpawnTruck()
    {
        DestroyTruck();

        GameObject root = new GameObject("Placeholder Suzuki Carry");
        truck = root.transform;
        PlaceTruckAt(truckStart);
        truck.localScale = Vector3.one * truckVisualScale;

        Material bodyMaterial = CreateMaterial(new Color(0.92f, 0.18f, 0.11f));
        Material darkBodyMaterial = CreateMaterial(new Color(0.58f, 0.07f, 0.04f));
        Material windowMaterial = CreateMaterial(new Color(0.12f, 0.27f, 0.34f));
        Material tyreMaterial = CreateMaterial(new Color(0.035f, 0.035f, 0.04f));
        Material hubMaterial = CreateMaterial(new Color(0.65f, 0.68f, 0.7f));
        Material lightMaterial = CreateMaterial(new Color(1f, 0.88f, 0.42f));

        CreateTruckPart("Chassis", PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f),
            new Vector3(1.55f, 0.45f, 3.35f), bodyMaterial);
        CreateTruckPart("Cab", PrimitiveType.Cube, new Vector3(0f, 1.18f, 0.82f),
            new Vector3(1.5f, 1.05f, 1.42f), bodyMaterial);
        CreateTruckPart("Cab Roof", PrimitiveType.Cube, new Vector3(0f, 1.75f, 0.82f),
            new Vector3(1.54f, 0.12f, 1.46f), bodyMaterial);
        CreateTruckPart("Front Bumper", PrimitiveType.Cube, new Vector3(0f, 0.48f, 1.74f),
            new Vector3(1.58f, 0.22f, 0.16f), hubMaterial);
        CreateTruckPart("Tray Floor", PrimitiveType.Cube, new Vector3(0f, 0.91f, -0.88f),
            new Vector3(1.48f, 0.15f, 1.55f), darkBodyMaterial);
        CreateTruckPart("Tray Left", PrimitiveType.Cube, new Vector3(-0.7f, 1.16f, -0.88f),
            new Vector3(0.1f, 0.48f, 1.55f), bodyMaterial);
        CreateTruckPart("Tray Right", PrimitiveType.Cube, new Vector3(0.7f, 1.16f, -0.88f),
            new Vector3(0.1f, 0.48f, 1.55f), bodyMaterial);
        CreateTruckPart("Tray Tailgate", PrimitiveType.Cube, new Vector3(0f, 1.16f, -1.62f),
            new Vector3(1.5f, 0.48f, 0.1f), bodyMaterial);
        CreateTruckPart("Windshield", PrimitiveType.Cube, new Vector3(0f, 1.39f, 1.545f),
            new Vector3(1.2f, 0.55f, 0.035f), windowMaterial);
        CreateTruckPart("Left Window", PrimitiveType.Cube, new Vector3(-0.756f, 1.4f, 0.85f),
            new Vector3(0.035f, 0.5f, 0.65f), windowMaterial);
        CreateTruckPart("Right Window", PrimitiveType.Cube, new Vector3(0.756f, 1.4f, 0.85f),
            new Vector3(0.035f, 0.5f, 0.65f), windowMaterial);
        CreateTruckPart("Left Headlight", PrimitiveType.Cube, new Vector3(-0.48f, 0.72f, 1.76f),
            new Vector3(0.34f, 0.25f, 0.06f), lightMaterial);
        CreateTruckPart("Right Headlight", PrimitiveType.Cube, new Vector3(0.48f, 0.72f, 1.76f),
            new Vector3(0.34f, 0.25f, 0.06f), lightMaterial);

        CreateWheel("Front Left Wheel", new Vector3(-0.79f, 0.38f, 1.08f), tyreMaterial, hubMaterial);
        CreateWheel("Front Right Wheel", new Vector3(0.79f, 0.38f, 1.08f), tyreMaterial, hubMaterial);
        CreateWheel("Rear Left Wheel", new Vector3(-0.79f, 0.38f, -1.08f), tyreMaterial, hubMaterial);
        CreateWheel("Rear Right Wheel", new Vector3(0.79f, 0.38f, -1.08f), tyreMaterial, hubMaterial);
    }

    private void CreateWheel(string wheelName, Vector3 position, Material tyre, Material hub)
    {
        GameObject wheel = CreateTruckPart(wheelName, PrimitiveType.Cylinder, position,
            new Vector3(0.42f, 0.16f, 0.42f), tyre);
        wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        GameObject hubcap = CreateTruckPart($"{wheelName} Hubcap", PrimitiveType.Cylinder, position,
            new Vector3(0.23f, 0.17f, 0.23f), hub);
        hubcap.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
    }

    private GameObject CreateTruckPart(
        string partName,
        PrimitiveType primitiveType,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = partName;
        part.transform.SetParent(truck, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.GetComponent<Renderer>().sharedMaterial = material;

        Collider partCollider = part.GetComponent<Collider>();
        if (partCollider != null)
        {
            Destroy(partCollider);
        }

        return part;
    }

    private Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader)
        {
            color = color
        };
        truckMaterials.Add(material);
        return material;
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

        for (int index = 0; index < truckMaterials.Count; index++)
        {
            if (truckMaterials[index] != null)
            {
                Destroy(truckMaterials[index]);
            }
        }

        truckMaterials.Clear();
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
        timerRect.anchorMin = new Vector2(0.5f, 0f);
        timerRect.anchorMax = new Vector2(0.5f, 0f);
        timerRect.pivot = new Vector2(0.5f, 0f);
        timerRect.anchoredPosition = new Vector2(0f, 24f);
        timerRect.sizeDelta = new Vector2(430f, 82f);
        Image timerBackground = timerDisplay.AddComponent<Image>();
        timerBackground.color = new Color(0.08f, 0.09f, 0.075f, 0.88f);
        timerBackground.raycastTarget = false;

        timerText = CreateText("Timer Text", timerDisplay.transform, 28f, TextAlignmentOptions.Center);
        timerText.color = new Color(1f, 0.9f, 0.42f);
        timerText.fontStyle = FontStyles.Bold;
        timerText.lineSpacing = -8f;
        timerText.raycastTarget = false;
        StretchToParent(timerText.rectTransform);

        liveStatsDisplay = CreateUiObject("Live Round Stats", canvasObject.transform).gameObject;
        RectTransform liveStatsRect = liveStatsDisplay.GetComponent<RectTransform>();
        liveStatsRect.anchorMin = new Vector2(0.915f, 1f);
        liveStatsRect.anchorMax = new Vector2(0.915f, 1f);
        liveStatsRect.pivot = new Vector2(0.5f, 1f);
        liveStatsRect.anchoredPosition = new Vector2(0f, -78f);
        liveStatsRect.sizeDelta = new Vector2(210f, 128f);
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
        resultsQuotaText = CreateResultStat(card, "Truck Quota", "TRUCKS / BONUS", -135f);

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
            new Vector2(590f, 410f),
            new Vector2(40f, 40f),
            "$",
            new Color(1f, 0.73f, 0.16f));

        shopBalanceText = CreateText(
            "Available Cash",
            card,
            30f,
            TextAlignmentOptions.Left);
        shopBalanceText.text = "$0.00";
        shopBalanceText.fontStyle = FontStyles.Bold;
        shopBalanceText.color = Color.white;
        SetRect(
            shopBalanceText.rectTransform,
            new Vector2(720f, 410f),
            new Vector2(210f, 44f));

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
            "TECH",
            "T",
            new Vector2(65f, 650f),
            new Color(0.12f, 0.65f, 0.38f),
            420f);
        CreateProgressionHeader(
            treeContent,
            "COLLECTION",
            "C",
            new Vector2(690f, 650f),
            new Color(0.16f, 0.52f, 0.84f),
            300f);

        Color foodColor = new Color(0.62f, 0.31f, 0.07f);
        Color premiumColor = new Color(0.44f, 0.2f, 0.62f);
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
            }
            feedPrevious = position;
        }

        Vector2 premiumPrevious = new Vector2(-430f, 620f);
        Vector2 premiumTierTwoPosition = Vector2.zero;
        for (int tier = 1; tier <= 8; tier++)
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
            premiumPrevious = position;
        }

        Vector2 valuePrevious = premiumTierTwoPosition;
        for (int tier = 1; tier <= 8; tier++)
        {
            Vector2 position = new Vector2(-290f, 360f - (tier - 1) * 95f);
            CreateTreeConnector(treeContent, valuePrevious, position, valueColor);
            CreateProgressionNode(
                $"Upgrade Egg Weight {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.EggValue,
                valueColor,
                tier);
            valuePrevious = position;
        }

        upgradeIncubatorButton = CreateProgressionNode(
            "Upgrade Incubator",
            treeContent,
            new Vector2(-80f, 550f),
            ProgressionSystem.UpgradeId.IncubatorInstall,
            incubationColor,
            0,
            150f);
        CreateTreeConnector(
            treeContent,
            new Vector2(65f, 620f),
            new Vector2(-80f, 550f),
            incubationColor);
        Vector2 capacityPrevious = new Vector2(-80f, 515f);
        Vector2 speedPrevious = new Vector2(-80f, 515f);
        for (int tier = 2; tier <= IncubatorController.MaximumLevel; tier++)
        {
            float y = 430f - (tier - 2) * 95f;
            Vector2 capacityPosition = new Vector2(-135f, y);
            Vector2 speedPosition = new Vector2(-20f, y);
            CreateTreeConnector(treeContent, capacityPrevious, capacityPosition, incubationColor);
            CreateTreeConnector(treeContent, speedPrevious, speedPosition, incubationColor);
            CreateProgressionNode(
                $"Upgrade Incubator Capacity {tier}",
                treeContent,
                capacityPosition,
                ProgressionSystem.UpgradeId.IncubatorCapacity,
                incubationColor,
                tier);
            CreateProgressionNode(
                $"Upgrade Incubator Speed {tier}",
                treeContent,
                speedPosition,
                ProgressionSystem.UpgradeId.IncubatorSpeed,
                incubationColor,
                tier);
            capacityPrevious = capacityPosition;
            speedPrevious = speedPosition;
        }

        Vector2 crosshatcherRoot = new Vector2(190f, 550f);
        CreateTreeConnector(
            treeContent,
            new Vector2(65f, 620f),
            crosshatcherRoot,
            crosshatcherColor);
        CreateProgressionNode(
            "Install Crosshatcher",
            treeContent,
            crosshatcherRoot,
            ProgressionSystem.UpgradeId.CrosshatcherInstall,
            crosshatcherColor,
            0,
            180f);
        Vector2 crosshatcherSpeedPrevious = new Vector2(
            crosshatcherRoot.x,
            crosshatcherRoot.y - 35f);
        Vector2 crosshatcherQualityPrevious = crosshatcherSpeedPrevious;

        for (int tier = 2; tier <= CrosshatcherController.MaximumLevel; tier++)
        {
            float y = 430f - (tier - 2) * 95f;
            Vector2 speedPosition = new Vector2(135f, y);
            Vector2 qualityPosition = new Vector2(250f, y);
            CreateTreeConnector(
                treeContent,
                crosshatcherSpeedPrevious,
                speedPosition,
                crosshatcherColor);
            CreateTreeConnector(
                treeContent,
                crosshatcherQualityPrevious,
                qualityPosition,
                crosshatcherColor);
            CreateProgressionNode(
                $"Upgrade Crosshatcher Speed {tier}",
                treeContent,
                speedPosition,
                ProgressionSystem.UpgradeId.CrosshatcherSpeed,
                crosshatcherColor,
                tier);
            CreateProgressionNode(
                $"Upgrade Crosshatcher Quality {tier}",
                treeContent,
                qualityPosition,
                ProgressionSystem.UpgradeId.CrosshatcherQuality,
                crosshatcherColor,
                tier);
            crosshatcherSpeedPrevious = speedPosition;
            crosshatcherQualityPrevious = qualityPosition;
        }

        Vector2 basketPrevious = new Vector2(690f, 620f);
        Vector2 basketOnePosition = Vector2.zero;
        for (int tier = 1; tier <= 3; tier++)
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

        Vector2 vacuumPowerPrevious = basketPrevious;
        Vector2 vacuumPowerOnePosition = Vector2.zero;
        for (int tier = 1; tier <= 3; tier++)
        {
            Vector2 position = new Vector2(530f, 250f - (tier - 1) * 95f);
            CreateTreeConnector(treeContent, vacuumPowerPrevious, position, collectionColor);
            CreateProgressionNode(
                $"Upgrade Vacuum Power {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.VacuumPower,
                collectionColor,
                tier);
            if (tier == 1)
            {
                vacuumPowerOnePosition = position;
            }
            vacuumPowerPrevious = position;
        }

        Vector2 vacuumRangePrevious = vacuumPowerOnePosition;
        for (int tier = 1; tier <= 3; tier++)
        {
            Vector2 position = new Vector2(640f, 155f - (tier - 1) * 95f);
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

        Button robotUnlock = CreateProgressionNode(
            "Unlock Collector Robot",
            treeContent,
            new Vector2(800f, 460f),
            ProgressionSystem.UpgradeId.RobotUnlock,
            robotColor,
            0,
            160f);
        CreateTreeConnector(
            treeContent,
            basketOnePosition,
            robotUnlock.transform.GetComponent<RectTransform>().anchoredPosition,
            robotColor);

        Vector2 robotRoot = new Vector2(800f, 425f);
        Vector2 robotSpeedPrevious = robotRoot;
        Vector2 robotCapacityPrevious = robotRoot;
        Vector2 robotLogicPrevious = robotRoot;
        for (int tier = 2; tier <= 3; tier++)
        {
            float y = 275f - (tier - 2) * 95f;
            Vector2 speedPosition = new Vector2(735f, y);
            Vector2 capacityPosition = new Vector2(845f, y);
            CreateTreeConnector(treeContent, robotSpeedPrevious, speedPosition, robotColor);
            CreateTreeConnector(treeContent, robotCapacityPrevious, capacityPosition, robotColor);
            CreateProgressionNode(
                $"Upgrade Robot Speed {tier}",
                treeContent,
                speedPosition,
                ProgressionSystem.UpgradeId.RobotSpeed,
                robotColor,
                tier);
            CreateProgressionNode(
                $"Upgrade Robot Capacity {tier}",
                treeContent,
                capacityPosition,
                ProgressionSystem.UpgradeId.RobotCapacity,
                robotColor,
                tier);
            robotSpeedPrevious = speedPosition;
            robotCapacityPrevious = capacityPosition;
        }

        for (int tier = 1; tier <= 2; tier++)
        {
            Vector2 position = new Vector2(790f, 85f - (tier - 1) * 95f);
            CreateTreeConnector(treeContent, robotLogicPrevious, position, robotColor);
            CreateProgressionNode(
                $"Upgrade Robot Logic {tier}",
                treeContent,
                position,
                ProgressionSystem.UpgradeId.RobotSmartness,
                robotColor,
                tier);
            robotLogicPrevious = position;
        }

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
            new Vector2(350f, 410f),
            new Vector2(220f, 30f));

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
            new Vector2(590f, 410f),
            new Vector2(40f, 40f));
        SetChildRect(
            card,
            "Available Cash",
            new Vector2(720f, 410f),
            new Vector2(210f, 44f));
        SetChildRect(
            card,
            "Shop Status",
            new Vector2(350f, 410f),
            new Vector2(220f, 30f));
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

        treeContent.sizeDelta = new Vector2(1800f, treeContent.sizeDelta.y);
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
        truckVisualScale = Mathf.Clamp(truckVisualScale, 0.1f, 0.5f);
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
    }
}
