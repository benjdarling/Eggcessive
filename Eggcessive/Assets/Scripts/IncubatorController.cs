using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class IncubatorController : MonoBehaviour
{
    private static readonly int IdleAnimationState =
        Animator.StringToHash("Base Layer.Idle");
    private static readonly int PlaceEggAnimationState =
        Animator.StringToHash("Base Layer.Place Egg");
    private static readonly int WorkingAnimationState =
        Animator.StringToHash("Base Layer.Working");
    private static readonly int FinishAnimationState =
        Animator.StringToHash("Base Layer.Finish");

    private static readonly int[] LevelCapacities =
    {
        1, 3, 5
    };

    private static readonly float[] LevelProductionTimes =
    {
        10f, 8f, 6.5f
    };

    [Header("Levels")]
    [SerializeField, Range(1, 3)] private int currentLevel = 1;
    [SerializeField, Range(1, 3)] private int capacityLevel = 1;
    [SerializeField, Range(1, 3)] private int speedLevel = 1;

    [Header("Incubator Sockets")]
    [SerializeField] private Transform eggStart = null;
    [SerializeField] private Transform eggEnd = null;
    [SerializeField] private Transform chickenStart = null;
    [SerializeField] private Transform chickenEnd = null;

    [Header("Authored Displays")]
    [SerializeField] private TMP_Text capacityText = null;
    [SerializeField] private TMP_Text timerText = null;

    [Header("Hatching")]
    [SerializeField] private GameObject chickenPrefab = null;
    [SerializeField, Min(0.01f)] private float eggTravelDuration = 0.65f;
    [Tooltip(
        "Chance that an incubated egg hatches as the chicken breed one tier " +
        "above its egg rarity.")]
    [SerializeField, Range(0f, 1f)] private float nextTierHatchChance = 0.05f;
    [Tooltip(
        "Additional next-tier hatch chance granted by each incubator level " +
        "above level one.")]
    [SerializeField, Range(0f, 0.5f)]
    private float nextTierHatchChancePerLevel = 0.1f;

    [Header("Animation")]
    [SerializeField] private RuntimeAnimatorController animatorController = null;

    [Header("VFX")]
    [SerializeField] private ParticleSystem workingSmoke = null;

    [Header("Audio")]
    [SerializeField] private AudioClip processingLoopSfx = null;
    [SerializeField] private AudioClip hatchDoneSfx = null;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

    private int storedEggs;
    private readonly Queue<ChickenEgg.EggType> storedEggTypes =
        new Queue<ChickenEgg.EggType>();
    private float processingTimeRemaining;
    private AudioSource processingAudioSource;
    private AudioSource hatchDoneAudioSource;
    private Animator animator;
    private bool placeEggAnimationPlaying;
    private bool finishAnimationPlaying;
    private bool chickenSpawnedForFinish;

    public const int MaximumLevel = 3;
    public static event Action ChickenHatched;
    public static event Action<int> EggsAccepted;
    public int CurrentLevel => Mathf.Max(capacityLevel, speedLevel);
    public int CapacityLevel => capacityLevel;
    public int SpeedLevel => speedLevel;
    public int StoredEggs => storedEggs;
    public int Capacity => GetCapacity(capacityLevel);
    public bool IsOffline =>
        PenExpansionManager.IsChickenCapReachedAt(transform.position);
    public int AvailableCapacity =>
        IsOffline ? 0 : Mathf.Max(0, Capacity - storedEggs);
    public float SecondsPerEgg => GetProductionTime(speedLevel);
    public float NextTierHatchChance => Mathf.Clamp01(
        nextTierHatchChance
        + (CurrentLevel - 1) * nextTierHatchChancePerLevel);
    public Vector3 DepositPosition =>
        eggStart != null ? eggStart.position : transform.position;
    public Transform EggDepositTarget => eggStart != null ? eggStart : transform;
    public bool CanAcceptCarriedEgg =>
        isActiveAndEnabled && AvailableCapacity > 0;

    private void Awake()
    {
        currentLevel = Mathf.Clamp(currentLevel, 1, MaximumLevel);
        capacityLevel = Mathf.Clamp(capacityLevel, 1, MaximumLevel);
        speedLevel = Mathf.Clamp(speedLevel, 1, MaximumLevel);
        InitializeAnimator();
        InitializeWorkingVfx();
        InitializeAudio();
        RefreshDisplays();
    }

    private void Update()
    {
        UpdateAnimatorState();
        UpdateWorkingVfx();
        UpdateProcessingAudio();

        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsRoundInProgress)
        {
            return;
        }

        if (IsOffline)
        {
            RefreshDisplays();
            return;
        }

        if (storedEggs <= 0)
        {
            // Population can fall below the cap when chickens are moved into
            // the crosshatcher. Keep an idle incubator's cap state current
            // even though it has no processing timer to redraw.
            RefreshDisplays();
            return;
        }

        processingTimeRemaining -= Time.deltaTime
            * TurboConsumableSystem.GetProductivityMultiplier(
                TurboConsumableSystem.TurboType.Incubator);

        if (processingTimeRemaining <= 0f && !finishAnimationPlaying)
        {
            BeginFinishAnimation();
            UpdateWorkingVfx();
            UpdateProcessingAudio();
        }

        RefreshDisplays();
    }

    public void InstallOrUpgrade(int level)
    {
        int nextLevel = Mathf.Clamp(level, 1, MaximumLevel);
        InstallOrUpgrade(nextLevel, nextLevel);
    }

    public void InstallOrUpgrade(int nextCapacityLevel, int nextSpeedLevel)
    {
        nextCapacityLevel = Mathf.Clamp(nextCapacityLevel, 1, MaximumLevel);
        nextSpeedLevel = Mathf.Clamp(nextSpeedLevel, 1, MaximumLevel);
        float previousDuration = SecondsPerEgg;
        float progress = storedEggs > 0
            ? 1f - Mathf.Clamp01(processingTimeRemaining / previousDuration)
            : 0f;

        capacityLevel = nextCapacityLevel;
        speedLevel = nextSpeedLevel;
        currentLevel = Mathf.Max(capacityLevel, speedLevel);
        gameObject.SetActive(true);

        if (storedEggs > 0)
        {
            processingTimeRemaining = SecondsPerEgg * (1f - progress);
        }

        RefreshDisplays();
    }

    public void TryAcceptEgg(Collider other)
    {
        if (IsOffline || storedEggs >= Capacity)
        {
            return;
        }

        ChickenEgg egg = other.GetComponentInParent<ChickenEgg>();

        if (egg == null || !egg.TryCollect())
        {
            return;
        }

        QueueAcceptedEgg(egg.Type);
        PrepareAcceptedEgg(egg);
        PlayPlaceEggAnimation();
        StartCoroutine(MoveEggIntoIncubator(egg.gameObject));
        UpdateWorkingVfx();
        UpdateProcessingAudio();
        RefreshDisplays();
    }

    public int TryAcceptStoredEgg(ChickenEgg.EggType eggType)
    {
        if (!isActiveAndEnabled || AvailableCapacity <= 0)
        {
            return 0;
        }

        QueueAcceptedEgg(eggType);
        PlayPlaceEggAnimation();
        UpdateWorkingVfx();
        UpdateProcessingAudio();
        RefreshDisplays();
        return 1;
    }

    public int TryAcceptStoredEggs(int eggCount)
    {
        if (!isActiveAndEnabled || eggCount <= 0 || AvailableCapacity <= 0)
        {
            return 0;
        }

        int accepted = Mathf.Min(eggCount, AvailableCapacity);

        bool wasEmpty = storedEggs == 0;
        for (int index = 0; index < accepted; index++)
        {
            storedEggTypes.Enqueue(ChickenEgg.EggType.Common);
        }

        storedEggs = storedEggTypes.Count;
        if (wasEmpty)
        {
            processingTimeRemaining = SecondsPerEgg;
        }

        EggsAccepted?.Invoke(accepted);
        PlayPlaceEggAnimation();
        UpdateWorkingVfx();
        UpdateProcessingAudio();
        RefreshDisplays();
        return accepted;
    }

    private void QueueAcceptedEgg(ChickenEgg.EggType eggType)
    {
        bool wasEmpty = storedEggs == 0;
        storedEggTypes.Enqueue(eggType);
        storedEggs = storedEggTypes.Count;

        if (wasEmpty)
        {
            processingTimeRemaining = SecondsPerEgg;
        }

        EggsAccepted?.Invoke(1);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ChickenHatched = null;
        EggsAccepted = null;
    }

    private void PrepareAcceptedEgg(ChickenEgg egg)
    {
        foreach (Collider eggCollider in egg.GetComponentsInChildren<Collider>(true))
        {
            eggCollider.enabled = false;
        }

        if (egg.TryGetComponent(out Rigidbody eggBody))
        {
            eggBody.linearVelocity = Vector3.zero;
            eggBody.angularVelocity = Vector3.zero;
            eggBody.isKinematic = true;
            eggBody.useGravity = false;
        }
    }

    private IEnumerator MoveEggIntoIncubator(GameObject egg)
    {
        if (egg == null)
        {
            yield break;
        }

        Vector3 startPosition = eggStart != null ? eggStart.position : egg.transform.position;
        Quaternion startRotation = eggStart != null ? eggStart.rotation : egg.transform.rotation;
        Vector3 endPosition = eggEnd != null ? eggEnd.position : startPosition;
        Quaternion endRotation = eggEnd != null ? eggEnd.rotation : startRotation;
        float elapsed = 0f;

        egg.transform.SetPositionAndRotation(startPosition, startRotation);

        while (egg != null && elapsed < eggTravelDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / eggTravelDuration));
            egg.transform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, endPosition, t),
                Quaternion.Slerp(startRotation, endRotation, t));
            yield return null;
        }

        if (egg != null)
        {
            if (egg.TryGetComponent(out ChickenEgg chickenEgg))
            {
                chickenEgg.ReleaseToPool();
            }
            else
            {
                Destroy(egg);
            }
        }
    }

    private void BeginFinishAnimation()
    {
        processingTimeRemaining = 0f;
        placeEggAnimationPlaying = false;
        finishAnimationPlaying = true;
        chickenSpawnedForFinish = false;

        if (CanAnimate && animator.HasState(0, FinishAnimationState))
        {
            animator.Play(FinishAnimationState, 0, 0f);
            return;
        }

        // Preserve hatching if an instance is missing its animation setup.
        OnHatchFrame();
        CompleteFinishAnimation();
    }

    /// <summary>
    /// Called by the incubator_finish clip's animation event on frame 9.
    /// </summary>
    public void OnHatchFrame()
    {
        if (!finishAnimationPlaying || chickenSpawnedForFinish)
        {
            return;
        }

        chickenSpawnedForFinish = true;
        HatchNextEgg();
    }

    private void HatchNextEgg()
    {
        if (IsOffline)
        {
            processingTimeRemaining = 0f;
            RefreshDisplays();
            return;
        }

        if (chickenPrefab == null || chickenStart == null)
        {
            Debug.LogError($"{nameof(IncubatorController)} on {name} cannot hatch without a chicken prefab and start socket.", this);
            processingTimeRemaining = 0f;
            return;
        }

        GameObject chickenObject = Instantiate(
            chickenPrefab,
            chickenStart.position,
            chickenStart.rotation);
        ChickenEgg.EggType eggType = storedEggTypes.Count > 0
            ? storedEggTypes.Dequeue()
            : ChickenEgg.EggType.Common;
        PlayHatchDoneSfx();
        ChickenHatched?.Invoke();

        if (chickenObject.TryGetComponent(out ChickenController chicken))
        {
            chicken.ConfigureBreed(RollHatchedBreed(eggType));

            if (chickenEnd != null)
            {
                chicken.BeginIncubatorExit(chickenEnd.position);
            }
        }

        // Capacity becomes available when the chicken actually spawns.
        storedEggs = storedEggTypes.Count;
        processingTimeRemaining = storedEggs > 0 ? SecondsPerEgg : 0f;
    }

    private bool CanAnimate =>
        animator != null && animator.runtimeAnimatorController != null;

    private bool IsActivelyProcessing =>
        storedEggs > 0
        && !IsOffline
        && (RoundSystem.Instance == null
            || RoundSystem.Instance.IsRoundInProgress);

    private void InitializeAnimator()
    {
        animator = GetComponentInChildren<Animator>(true);

        if (animator == null)
        {
            return;
        }

        if (animatorController != null)
        {
            animator.runtimeAnimatorController = animatorController;
        }

        animator.enabled = true;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0f);
        IncubatorAnimationEvents eventRelay =
            animator.GetComponent<IncubatorAnimationEvents>();

        if (eventRelay == null)
        {
            eventRelay = animator.gameObject.AddComponent<IncubatorAnimationEvents>();
        }

        eventRelay.Initialize(this);

        if (CanAnimate && animator.HasState(0, IdleAnimationState))
        {
            animator.Play(IdleAnimationState, 0, 0f);
        }
    }

    private void PlayPlaceEggAnimation()
    {
        if (finishAnimationPlaying
            || !CanAnimate
            || !animator.HasState(0, PlaceEggAnimationState))
        {
            return;
        }

        placeEggAnimationPlaying = true;
        animator.Play(PlaceEggAnimationState, 0, 0f);
    }

    private void UpdateAnimatorState()
    {
        if (!CanAnimate)
        {
            return;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);

        if (finishAnimationPlaying)
        {
            if (state.fullPathHash == FinishAnimationState
                && state.normalizedTime >= 1f)
            {
                // The event is authoritative. This fallback prevents an old
                // imported clip from permanently blocking the incubator.
                OnHatchFrame();
                CompleteFinishAnimation();
            }

            return;
        }

        if (placeEggAnimationPlaying)
        {
            // Animator.Play can take one animation evaluation to become the
            // current state, so do not replace it with Working prematurely.
            if (state.fullPathHash != PlaceEggAnimationState
                || state.normalizedTime < 1f)
            {
                return;
            }

            placeEggAnimationPlaying = false;
        }

        int desiredState = IsActivelyProcessing
            ? WorkingAnimationState
            : IdleAnimationState;

        if (state.fullPathHash != desiredState && animator.HasState(0, desiredState))
        {
            animator.Play(desiredState, 0, 0f);
        }
    }

    private void CompleteFinishAnimation()
    {
        finishAnimationPlaying = false;
        chickenSpawnedForFinish = false;

        if (!CanAnimate)
        {
            return;
        }

        int nextState = IsActivelyProcessing
            ? WorkingAnimationState
            : IdleAnimationState;

        if (animator.HasState(0, nextState))
        {
            animator.Play(nextState, 0, 0f);
        }
    }

    private ChickenController.ChickenBreed RollHatchedBreed(
        ChickenEgg.EggType eggType)
    {
        int maximumBreed = (int)ChickenController.ChickenBreed.Cosmic;
        int breedIndex = Mathf.Clamp((int)eggType, 0, maximumBreed);

        if (breedIndex < maximumBreed
            && UnityEngine.Random.value < NextTierHatchChance)
        {
            breedIndex++;
        }

        return (ChickenController.ChickenBreed)breedIndex;
    }

    private void InitializeAudio()
    {
        processingAudioSource = CreateSpatialAudioSource(true);
        processingAudioSource.clip = processingLoopSfx;
        hatchDoneAudioSource = CreateSpatialAudioSource(false);
    }

    private void InitializeWorkingVfx()
    {
        if (workingSmoke == null)
        {
            return;
        }

        workingSmoke.Stop(
            true,
            ParticleSystemStopBehavior.StopEmitting);
    }

    private void UpdateWorkingVfx()
    {
        if (workingSmoke == null)
        {
            return;
        }

        if (IsActivelyProcessing)
        {
            if (!workingSmoke.isEmitting)
            {
                workingSmoke.Play(true);
            }
        }
        else if (workingSmoke.isEmitting)
        {
            workingSmoke.Stop(
                true,
                ParticleSystemStopBehavior.StopEmitting);
        }
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

        bool shouldPlay = storedEggs > 0
            && !IsOffline
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
        if (workingSmoke != null)
        {
            workingSmoke.Stop(
                true,
                ParticleSystemStopBehavior.StopEmitting);
        }

        if (processingAudioSource != null)
        {
            processingAudioSource.Stop();
        }
    }

    private void RefreshDisplays()
    {
        if (capacityText != null)
        {
            capacityText.text = IsOffline
                ? $"CAP {ChickenController.MaximumChickenCount}/{ChickenController.MaximumChickenCount}"
                : $"{storedEggs}/{Capacity}";
            capacityText.color = IsOffline
                ? new Color(1f, 0.24f, 0.16f)
                : Color.white;
        }

        if (timerText != null)
        {
            if (IsOffline)
            {
                timerText.text = "OFFLINE";
                timerText.color = new Color(1f, 0.24f, 0.16f);
            }
            else if (storedEggs <= 0)
            {
                timerText.text = "--:--";
                timerText.color = Color.white;
            }
            else
            {
                int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(processingTimeRemaining));
                timerText.text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
                timerText.color = Color.white;
            }
        }
    }

    public static int GetCapacity(int level)
    {
        return LevelCapacities[Mathf.Clamp(level, 1, MaximumLevel) - 1];
    }

    public static float GetProductionTime(int level)
    {
        return LevelProductionTimes[Mathf.Clamp(level, 1, MaximumLevel) - 1];
    }

    private void OnValidate()
    {
        currentLevel = Mathf.Clamp(currentLevel, 1, MaximumLevel);
        capacityLevel = Mathf.Clamp(capacityLevel, 1, MaximumLevel);
        speedLevel = Mathf.Clamp(speedLevel, 1, MaximumLevel);
        eggTravelDuration = Mathf.Max(0.01f, eggTravelDuration);
        nextTierHatchChance = Mathf.Clamp01(nextTierHatchChance);
        nextTierHatchChancePerLevel = Mathf.Clamp(
            nextTierHatchChancePerLevel,
            0f,
            0.5f);

        if (!Application.isPlaying)
        {
            RefreshDisplays();
        }
    }
}
