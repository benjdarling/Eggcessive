using System.Collections;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class IncubatorController : MonoBehaviour
{
    [System.Serializable]
    private struct LevelSettings
    {
        [Min(1)] public int capacity;
        [Min(0.1f)] public float secondsPerEgg;
    }

    [Header("Levels")]
    [SerializeField] private LevelSettings levelOne = new LevelSettings
    {
        capacity = 1,
        secondsPerEgg = 30f
    };
    [SerializeField] private LevelSettings levelTwo = new LevelSettings
    {
        capacity = 3,
        secondsPerEgg = 20f
    };
    [SerializeField, Range(1, 2)] private int currentLevel = 1;

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

    private int storedEggs;
    private float processingTimeRemaining;

    public int CurrentLevel => currentLevel;
    public int StoredEggs => storedEggs;
    public int Capacity => GetSettings(currentLevel).capacity;
    public float SecondsPerEgg => GetSettings(currentLevel).secondsPerEgg;

    private void Awake()
    {
        RefreshDisplays();
    }

    private void Update()
    {
        if (storedEggs <= 0)
        {
            return;
        }

        processingTimeRemaining -= Time.deltaTime;

        if (processingTimeRemaining <= 0f)
        {
            HatchNextEgg();
        }

        RefreshDisplays();
    }

    public void InstallOrUpgrade(int level)
    {
        int nextLevel = Mathf.Clamp(level, 1, 2);
        float previousDuration = SecondsPerEgg;
        float progress = storedEggs > 0
            ? 1f - Mathf.Clamp01(processingTimeRemaining / previousDuration)
            : 0f;

        currentLevel = nextLevel;
        gameObject.SetActive(true);

        if (storedEggs > 0)
        {
            processingTimeRemaining = SecondsPerEgg * (1f - progress);
        }

        RefreshDisplays();
    }

    public void TryAcceptEgg(Collider other)
    {
        if (storedEggs >= Capacity)
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
        PrepareAcceptedEgg(egg);
        StartCoroutine(MoveEggIntoIncubator(egg.gameObject));
        RefreshDisplays();
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

        if (chickenEnd != null
            && chickenObject.TryGetComponent(out ChickenController chicken))
        {
            chicken.BeginIncubatorExit(chickenEnd.position);
        }

        // Capacity becomes available when the chicken actually spawns.
        storedEggs--;
        processingTimeRemaining = storedEggs > 0 ? SecondsPerEgg : 0f;
    }

    private void RefreshDisplays()
    {
        if (capacityText != null)
        {
            capacityText.text = $"{storedEggs}/{Capacity}";
        }

        if (timerText != null)
        {
            if (storedEggs <= 0)
            {
                timerText.text = "--:--";
            }
            else
            {
                int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(processingTimeRemaining));
                timerText.text = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
            }
        }
    }

    private LevelSettings GetSettings(int level)
    {
        return level >= 2 ? levelTwo : levelOne;
    }

    private void OnValidate()
    {
        levelOne.capacity = Mathf.Max(1, levelOne.capacity);
        levelOne.secondsPerEgg = Mathf.Max(0.1f, levelOne.secondsPerEgg);
        levelTwo.capacity = Mathf.Max(1, levelTwo.capacity);
        levelTwo.secondsPerEgg = Mathf.Max(0.1f, levelTwo.secondsPerEgg);
        currentLevel = Mathf.Clamp(currentLevel, 1, 2);
        eggTravelDuration = Mathf.Max(0.01f, eggTravelDuration);

        if (!Application.isPlaying)
        {
            RefreshDisplays();
        }
    }
}
