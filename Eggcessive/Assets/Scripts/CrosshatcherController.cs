using System.Collections;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CrosshatcherController : MonoBehaviour
{
    private enum MachineState
    {
        Waiting,
        Loading,
        Processing,
        ReadyToRelease
    }

    private static readonly float[] ProcessingTimes =
    {
        18f, 15f, 12.5f, 10.5f, 9f, 7.5f, 6.25f, 5.25f, 4.5f, 3.75f
    };

    private static readonly float[] ImprovementChances =
    {
        0.25f, 0.3f, 0.35f, 0.4f, 0.45f,
        0.5f, 0.55f, 0.6f, 0.65f, 0.75f
    };

    [Header("Levels")]
    [SerializeField, Range(1, 10)] private int speedLevel = 1;
    [SerializeField, Range(1, 10)] private int qualityLevel = 1;

    [Header("Authored Sockets")]
    [SerializeField] private Transform chickenStartOne = null;
    [SerializeField] private Transform chickenStartTwo = null;
    [SerializeField] private Transform chickenEnd = null;
    [SerializeField] private Transform chickenSpawn = null;
    [SerializeField] private Transform chickenDestination = null;
    [SerializeField] private TMP_Text timerText = null;

    [Header("Production")]
    [SerializeField] private GameObject chickenPrefab = null;
    [SerializeField, Min(0.01f)] private float loadingTravelDuration = 0.8f;
    [SerializeField, Min(0.01f)] private float conveyorTravelDuration = 1.25f;

    [Header("Audio")]
    [SerializeField] private AudioClip processingLoopSfx = null;
    [SerializeField] private AudioClip hatchDoneSfx = null;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

    private ChickenController chickenOne;
    private ChickenController chickenTwo;
    private bool chickenOneLoaded;
    private bool chickenTwoLoaded;
    private float processingTimeRemaining;
    private MachineState state;
    private AudioSource processingAudioSource;
    private AudioSource hatchDoneAudioSource;
    private Object chickenReservationOwner;
    private int reservedChickenSlots;

    public const int MaximumLevel = 10;
    // Automatic crosshatching is population-neutral only when the incubator
    // replaces the chicken consumed by the previous cycle. Requiring a full
    // flock before the robot starts another cycle keeps both machines in
    // balance without slowing the player's purchased crosshatcher upgrades.
    public static int MinimumFlockSizeForNewCycle =>
        ChickenController.MaximumChickenCount;
    public int SpeedLevel => speedLevel;
    public int QualityLevel => qualityLevel;
    public float ProcessingTime => GetProcessingTime(speedLevel);
    public float ImprovementChance => GetImprovementChance(qualityLevel);
    public int OccupiedSlots => (chickenOne != null ? 1 : 0) + (chickenTwo != null ? 1 : 0);
    public bool IsProcessing => state == MachineState.Processing;
    public bool HasReservedChickenOutput =>
        state == MachineState.Processing
        || state == MachineState.ReadyToRelease;
    public bool CanAcceptCarriedChicken
    {
        get
        {
            ClearStaleReservation();
            return isActiveAndEnabled
                && state != MachineState.Processing
                && state != MachineState.ReadyToRelease
                && OccupiedSlots + reservedChickenSlots < 2;
        }
    }
    public bool HasChickenReservation(Object owner) =>
        owner != null
        && chickenReservationOwner == owner
        && reservedChickenSlots > 0;
    public Vector3 RobotDeliveryPosition
    {
        get
        {
            Vector3 socketCenter = chickenStartOne != null
                && chickenStartTwo != null
                    ? (chickenStartOne.position + chickenStartTwo.position) * 0.5f
                    : transform.position;
            return socketCenter - transform.forward * 0.65f;
        }
    }

    public int GetAvailableCarriedChickenTargets(Transform[] targets)
    {
        ClearStaleReservation();
        if (targets == null
            || targets.Length == 0
            || !isActiveAndEnabled
            || state == MachineState.Processing
            || state == MachineState.ReadyToRelease)
        {
            return 0;
        }

        int availableCount = Mathf.Clamp(
            2 - OccupiedSlots - reservedChickenSlots,
            0,
            targets.Length);
        int resultCount = 0;

        if (resultCount < availableCount
            && chickenOne == null
            && chickenStartOne != null)
        {
            targets[resultCount++] = chickenStartOne;
        }

        if (resultCount < availableCount
            && chickenTwo == null
            && chickenStartTwo != null)
        {
            targets[resultCount++] = chickenStartTwo;
        }

        return resultCount;
    }

    public bool TryGetLoadedBreed(
        out ChickenController.ChickenBreed breed)
    {
        ChickenController loadedChicken = chickenOne != null
            ? chickenOne
            : chickenTwo;
        if (loadedChicken == null)
        {
            breed = default;
            return false;
        }

        breed = loadedChicken.Breed;
        return true;
    }

    private void Awake()
    {
        state = MachineState.Waiting;
        InitializeAudio();
        RefreshDisplay();
    }

    private void Update()
    {
        UpdateProcessingAudio();

        if (state != MachineState.Processing
            && state != MachineState.ReadyToRelease)
        {
            return;
        }

        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsRoundInProgress)
        {
            RefreshDisplay();
            return;
        }

        if (state == MachineState.Processing)
        {
            processingTimeRemaining -= Time.deltaTime
                * TurboConsumableSystem.GetProductivityMultiplier(
                    TurboConsumableSystem.TurboType.Crosshatcher);
        }

        if (processingTimeRemaining <= 0f)
        {
            CompleteCrosshatch();
        }

        RefreshDisplay();
    }

    public void InstallOrUpgrade(int nextSpeedLevel, int nextQualityLevel)
    {
        float previousDuration = ProcessingTime;
        float progress = state == MachineState.Processing
            ? 1f - Mathf.Clamp01(processingTimeRemaining / previousDuration)
            : 0f;

        speedLevel = Mathf.Clamp(nextSpeedLevel, 1, MaximumLevel);
        qualityLevel = Mathf.Clamp(nextQualityLevel, 1, MaximumLevel);
        gameObject.SetActive(true);

        if (state == MachineState.Processing)
        {
            processingTimeRemaining = ProcessingTime * (1f - progress);
        }

        RefreshDisplay();
    }

    public bool TryAcceptChicken(Collider other)
    {
        ChickenController chicken = other != null
            ? other.GetComponentInParent<ChickenController>()
            : null;
        return TryAcceptChicken(chicken);
    }

    public bool TryAcceptChicken(ChickenController chicken)
    {
        return TryAcceptChicken(chicken, false);
    }

    public bool TryAcceptCarriedChicken(ChickenController chicken)
    {
        return TryAcceptChicken(chicken, true);
    }

    public bool TryReserveChickenPair(Object owner)
    {
        ClearStaleReservation();
        if (owner == null
            || !isActiveAndEnabled
            || state != MachineState.Waiting
            || OccupiedSlots != 0
            || chickenReservationOwner != null)
        {
            return false;
        }

        chickenReservationOwner = owner;
        reservedChickenSlots = 2;
        return true;
    }

    public void ReleaseChickenReservation(Object owner)
    {
        if (owner != null && chickenReservationOwner == owner)
        {
            chickenReservationOwner = null;
            reservedChickenSlots = 0;
        }
    }

    public bool TryAcceptReservedChicken(
        ChickenController chicken,
        Object owner)
    {
        ClearStaleReservation();
        if (owner == null
            || chickenReservationOwner != owner
            || reservedChickenSlots <= 0)
        {
            return false;
        }

        bool accepted = TryAcceptChicken(chicken, true, true);
        if (!accepted)
        {
            return false;
        }

        reservedChickenSlots--;
        if (reservedChickenSlots <= 0)
        {
            chickenReservationOwner = null;
            reservedChickenSlots = 0;
        }

        return true;
    }

    private bool TryAcceptChicken(
        ChickenController chicken,
        bool allowMachineControlled)
    {
        return TryAcceptChicken(chicken, allowMachineControlled, false);
    }

    private bool TryAcceptChicken(
        ChickenController chicken,
        bool allowMachineControlled,
        bool consumeReservation)
    {
        ClearStaleReservation();
        if (chicken == null
            || state == MachineState.Processing
            || state == MachineState.ReadyToRelease
            || (chicken.IsMachineControlled && !allowMachineControlled)
            || chicken == chickenOne
            || chicken == chickenTwo
            || OccupiedSlots >= 2
            || (!consumeReservation
                && OccupiedSlots + reservedChickenSlots >= 2))
        {
            return false;
        }

        Transform target;

        if (chickenOne == null)
        {
            chickenOne = chicken;
            chickenOneLoaded = false;
            target = chickenStartOne;
        }
        else
        {
            chickenTwo = chicken;
            chickenTwoLoaded = false;
            target = chickenStartTwo;
        }

        state = MachineState.Loading;
        chicken.SetHeldByHand(false);
        chicken.SetMachineControlled(true);
        StartCoroutine(MoveAcceptedChicken(chicken, target));
        RefreshDisplay();
        return true;
    }

    public static float GetProcessingTime(int level)
    {
        return ProcessingTimes[Mathf.Clamp(level, 1, MaximumLevel) - 1];
    }

    public static float GetImprovementChance(int level)
    {
        return ImprovementChances[Mathf.Clamp(level, 1, MaximumLevel) - 1];
    }

    public static ChickenController.ChickenBreed RollResultBreed(
        ChickenController.ChickenBreed first,
        ChickenController.ChickenBreed second,
        float improvementChance)
    {
        int firstIndex = (int)first;
        int secondIndex = (int)second;
        int strongest = Mathf.Max(firstIndex, secondIndex);
        int maximum = (int)ChickenController.ChickenBreed.Cosmic;

        if (first == second)
        {
            return (ChickenController.ChickenBreed)Mathf.Min(strongest + 1, maximum);
        }

        int improved = Mathf.Min(strongest + 1, maximum);
        return Random.value < Mathf.Clamp01(improvementChance)
            ? (ChickenController.ChickenBreed)improved
            : (ChickenController.ChickenBreed)strongest;
    }

    private IEnumerator MoveAcceptedChicken(
        ChickenController chicken,
        Transform destination)
    {
        if (chicken == null || destination == null)
        {
            ReleaseMissingSlot(chicken);
            yield break;
        }

        yield return MoveChicken(
            chicken,
            destination.position,
            destination.rotation,
            loadingTravelDuration);

        if (chicken == chickenOne)
        {
            chickenOneLoaded = true;
        }
        else if (chicken == chickenTwo)
        {
            chickenTwoLoaded = true;
        }

        if (chickenOne != null
            && chickenTwo != null
            && chickenOneLoaded
            && chickenTwoLoaded)
        {
            StartCoroutine(MovePairIntoMachine());
        }
        else
        {
            state = MachineState.Waiting;
            RefreshDisplay();
        }
    }

    private IEnumerator MovePairIntoMachine()
    {
        state = MachineState.Loading;
        RefreshDisplay();
        ChickenController first = chickenOne;
        ChickenController second = chickenTwo;
        Vector3 destination = chickenEnd != null
            ? chickenEnd.position
            : transform.position;
        Quaternion rotation = chickenEnd != null
            ? chickenEnd.rotation
            : transform.rotation;

        StartCoroutine(MoveChicken(
            first,
            destination,
            rotation,
            conveyorTravelDuration));
        yield return MoveChicken(
            second,
            destination,
            rotation,
            conveyorTravelDuration);

        if (first == null || second == null)
        {
            ReleaseMissingSlot(null);
            yield break;
        }

        ChickenController.ChickenBreed firstBreed = first.Breed;
        ChickenController.ChickenBreed secondBreed = second.Breed;
        Destroy(first.gameObject);
        Destroy(second.gameObject);
        chickenOne = null;
        chickenTwo = null;
        chickenOneLoaded = false;
        chickenTwoLoaded = false;
        pendingFirstBreed = firstBreed;
        pendingSecondBreed = secondBreed;
        processingTimeRemaining = ProcessingTime;
        state = MachineState.Processing;
        RefreshDisplay();
    }

    private ChickenController.ChickenBreed pendingFirstBreed;
    private ChickenController.ChickenBreed pendingSecondBreed;

    private static IEnumerator MoveChicken(
        ChickenController chicken,
        Vector3 destination,
        Quaternion destinationRotation,
        float duration)
    {
        if (chicken == null)
        {
            yield break;
        }

        Vector3 startPosition = chicken.transform.position;
        Quaternion startRotation = chicken.transform.rotation;
        float elapsed = 0f;

        while (chicken != null && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            chicken.transform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, destination, t),
                Quaternion.Slerp(startRotation, destinationRotation, t));
            yield return null;
        }

        if (chicken != null)
        {
            chicken.transform.SetPositionAndRotation(destination, destinationRotation);
        }
    }

    private void CompleteCrosshatch()
    {
        if (PenExpansionManager.IsChickenCapReachedAt(
                transform.position,
                includeReservedCrosshatcherOutput: false))
        {
            // Keep the completed output pending if another producer bypassed
            // this machine's reserved chicken-cap slot.
            processingTimeRemaining = 0f;
            state = MachineState.ReadyToRelease;
            UpdateProcessingAudio();
            return;
        }

        if (chickenPrefab == null || chickenSpawn == null)
        {
            Debug.LogError(
                $"{nameof(CrosshatcherController)} on {name} is missing its chicken prefab or spawn socket.",
                this);
            processingTimeRemaining = 0f;
            state = MachineState.Waiting;
            UpdateProcessingAudio();
            RefreshDisplay();
            return;
        }

        ChickenController.ChickenBreed resultBreed = RollResultBreed(
            pendingFirstBreed,
            pendingSecondBreed,
            ImprovementChance);
        GameObject output = Instantiate(
            chickenPrefab,
            chickenSpawn.position,
            chickenSpawn.rotation);
        PlayHatchDoneSfx();

        if (output.TryGetComponent(out ChickenController chicken))
        {
            chicken.ConfigureBreed(resultBreed);
            chicken.SetMachineControlled(true);

            if (chickenDestination != null)
            {
                StartCoroutine(MoveProducedChickenToDestination(chicken));
            }
            else
            {
                chicken.SetMachineControlled(false);
            }
        }

        processingTimeRemaining = 0f;
        state = MachineState.Waiting;
        UpdateProcessingAudio();
        RefreshDisplay();
    }

    private IEnumerator MoveProducedChickenToDestination(
        ChickenController chicken)
    {
        if (chicken == null || chickenDestination == null)
        {
            if (chicken != null)
            {
                chicken.SetMachineControlled(false);
            }

            yield break;
        }

        yield return MoveChicken(
            chicken,
            chickenDestination.position,
            chickenDestination.rotation,
            conveyorTravelDuration);

        if (chicken != null)
        {
            chicken.SetMachineControlled(false);
        }
    }

    private void InitializeAudio()
    {
        processingAudioSource = CreateSpatialAudioSource(true);
        processingAudioSource.clip = processingLoopSfx;
        hatchDoneAudioSource = CreateSpatialAudioSource(false);
    }

    private AudioSource CreateSpatialAudioSource(bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = 2.25f;
        source.maxDistance = 15f;
        source.volume = sfxVolume;
        return source;
    }

    private void UpdateProcessingAudio()
    {
        if (processingAudioSource == null || processingLoopSfx == null)
        {
            return;
        }

        bool shouldPlay = state == MachineState.Processing
            && (RoundSystem.Instance == null
                || RoundSystem.Instance.IsRoundInProgress);

        if (shouldPlay && !processingAudioSource.isPlaying)
        {
            processingAudioSource.Play();
        }
        else if (!shouldPlay && processingAudioSource.isPlaying)
        {
            processingAudioSource.Stop();
        }
    }

    private void PlayHatchDoneSfx()
    {
        if (hatchDoneAudioSource != null && hatchDoneSfx != null)
        {
            hatchDoneAudioSource.PlayOneShot(hatchDoneSfx);
        }
    }

    private void OnDisable()
    {
        chickenReservationOwner = null;
        reservedChickenSlots = 0;
        if (processingAudioSource != null)
        {
            processingAudioSource.Stop();
        }
    }

    private void ClearStaleReservation()
    {
        if (chickenReservationOwner == null)
        {
            chickenReservationOwner = null;
            reservedChickenSlots = 0;
        }
    }

    private void ReleaseMissingSlot(ChickenController missingChicken)
    {
        if (chickenOne == null || chickenOne == missingChicken)
        {
            chickenOne = null;
            chickenOneLoaded = false;
        }

        if (chickenTwo == null || chickenTwo == missingChicken)
        {
            chickenTwo = null;
            chickenTwoLoaded = false;
        }

        state = MachineState.Waiting;
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (timerText == null)
        {
            return;
        }

        switch (state)
        {
            case MachineState.Processing:
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(processingTimeRemaining));
                timerText.text = $"PROCESSING\n{seconds / 60:00}:{seconds % 60:00}";
                timerText.color = new Color(0.5f, 1f, 0.8f, 1f);
                break;
            }
            case MachineState.Loading:
                timerText.text = $"LOADING\n{OccupiedSlots}/2";
                timerText.color = new Color(1f, 0.84f, 0.3f, 1f);
                break;
            case MachineState.ReadyToRelease:
                timerText.text = "OUTPUT READY\nCHICKEN CAP";
                timerText.color = new Color(1f, 0.62f, 0.2f, 1f);
                break;
            default:
                timerText.text = $"STANDBY\n{OccupiedSlots}/2";
                timerText.color = Color.white;
                break;
        }
    }

    private void OnValidate()
    {
        speedLevel = Mathf.Clamp(speedLevel, 1, MaximumLevel);
        qualityLevel = Mathf.Clamp(qualityLevel, 1, MaximumLevel);
        loadingTravelDuration = Mathf.Max(0.01f, loadingTravelDuration);
        conveyorTravelDuration = Mathf.Max(0.01f, conveyorTravelDuration);
        sfxVolume = Mathf.Clamp01(sfxVolume);
    }
}
