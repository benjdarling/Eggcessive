using System;
using System.Collections;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class IncubatorController : MonoBehaviour
{
    private static readonly int[] LevelCapacities =
    {
        1, 3, 5, 8, 12, 17, 23, 30, 38, 48
    };

    private static readonly float[] LevelProductionTimes =
    {
        10f, 8f, 6.5f, 5.25f, 4.25f, 3.5f, 2.9f, 2.4f, 2f, 1.6f
    };

    [Header("Levels")]
    [SerializeField, Range(1, 10)] private int currentLevel = 1;
    [SerializeField, Range(1, 10)] private int capacityLevel = 1;
    [SerializeField, Range(1, 10)] private int speedLevel = 1;

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

    [Header("Audio")]
    [SerializeField] private AudioClip processingLoopSfx = null;
    [SerializeField] private AudioClip hatchDoneSfx = null;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

    private int storedEggs;
    private float processingTimeRemaining;
    private AudioSource processingAudioSource;
    private AudioSource hatchDoneAudioSource;

    public const int MaximumLevel = 10;
    public static event Action ChickenHatched;
    public static event Action<int> EggsAccepted;
    public int CurrentLevel => Mathf.Max(capacityLevel, speedLevel);
    public int CapacityLevel => capacityLevel;
    public int SpeedLevel => speedLevel;
    public int StoredEggs => storedEggs;
    public int Capacity => GetCapacity(capacityLevel);
    public bool IsOffline =>
        ChickenController.ActiveInstances.Count >= ChickenController.MaximumChickenCount;
    public int AvailableCapacity =>
        IsOffline ? 0 : Mathf.Max(0, Capacity - storedEggs);
    public float SecondsPerEgg => GetProductionTime(speedLevel);
    public Vector3 DepositPosition =>
        eggStart != null ? eggStart.position : transform.position;

    private void Awake()
    {
        InitializeAudio();
        RefreshDisplays();
    }

    private void Update()
    {
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
            return;
        }

        processingTimeRemaining -= Time.deltaTime;

        if (processingTimeRemaining <= 0f)
        {
            HatchNextEgg();
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

        if (storedEggs == 0)
        {
            processingTimeRemaining = SecondsPerEgg;
        }

        storedEggs++;
        EggsAccepted?.Invoke(1);
        PrepareAcceptedEgg(egg);
        StartCoroutine(MoveEggIntoIncubator(egg.gameObject));
        UpdateProcessingAudio();
        RefreshDisplays();
    }

    public int TryAcceptStoredEggs(int eggCount)
    {
        if (!isActiveAndEnabled || eggCount <= 0 || AvailableCapacity <= 0)
        {
            return 0;
        }

        int accepted = Mathf.Min(eggCount, AvailableCapacity);

        if (storedEggs == 0)
        {
            processingTimeRemaining = SecondsPerEgg;
        }

        storedEggs += accepted;
        EggsAccepted?.Invoke(accepted);
        UpdateProcessingAudio();
        RefreshDisplays();
        return accepted;
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
            Destroy(egg);
        }
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
        PlayHatchDoneSfx();
        ChickenHatched?.Invoke();

        if (chickenEnd != null
            && chickenObject.TryGetComponent(out ChickenController chicken))
        {
            chicken.BeginIncubatorExit(chickenEnd.position);
        }

        // Capacity becomes available when the chicken actually spawns.
        storedEggs--;
        processingTimeRemaining = storedEggs > 0 ? SecondsPerEgg : 0f;
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

        if (!Application.isPlaying)
        {
            RefreshDisplays();
        }
    }
}
