using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class EggContainer : MonoBehaviour
{
    private const int InactiveRewardSortingOrder = 2000;
    private static readonly List<EggContainer> ActiveContainers =
        new List<EggContainer>();

    [SerializeField, Min(1)] private int centsPerEgg = 100;

    public static EggContainer Instance { get; private set; }
    public static event Action<int> EggCollected;
    public static event Action<EggContainer, int> EggCollectedFromContainer;
    public static event Action<EggContainer> FocusedContainerChanged;
    public Vector3 DepositPosition => transform.position;
    public Vector3 RewardPosition => transform.position + Vector3.up * 0.22f;
    public long TotalDepositedCents { get; private set; }

    private TMP_Text inactiveRewardText;
    private long inactiveRewardCents;
    private bool isFocused;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveContainers.Clear();
        Instance = null;
        EggCollected = null;
        EggCollectedFromContainer = null;
        FocusedContainerChanged = null;
    }

    private void Awake()
    {
        ActiveContainers.Add(this);
        if (Instance == null)
        {
            Instance = this;
            isFocused = true;
        }

        GetComponent<Collider>().isTrigger = true;
    }

    private void LateUpdate()
    {
        if (inactiveRewardText == null || !inactiveRewardText.gameObject.activeSelf)
        {
            return;
        }

        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector3 direction = inactiveRewardText.transform.position
                - camera.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                inactiveRewardText.transform.rotation =
                    Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }
    }

    private void OnDestroy()
    {
        ActiveContainers.Remove(this);
        if (Instance == this)
        {
            Instance = ActiveContainers.Count > 0 ? ActiveContainers[0] : null;
            if (Instance != null)
            {
                Instance.SetFocused(true);
            }
        }
    }

    public static void SetFocusedContainer(EggContainer container)
    {
        if (container == null)
        {
            return;
        }

        for (int index = 0; index < ActiveContainers.Count; index++)
        {
            if (ActiveContainers[index] != null)
            {
                ActiveContainers[index].SetFocused(
                    ActiveContainers[index] == container);
            }
        }

        Instance = container;
        FocusedContainerChanged?.Invoke(container);
    }

    public void SetFocused(bool focused)
    {
        isFocused = focused;
        if (!focused)
        {
            return;
        }

        inactiveRewardCents = 0;
        if (inactiveRewardText != null)
        {
            inactiveRewardText.gameObject.SetActive(false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    private void OnTriggerStay(Collider other)
    {
        // An egg can enter while being held. Staying in the trigger allows it
        // to be collected on the first physics step after the mouse is released.
        TryCollect(other);
    }

    private void TryCollect(Collider other)
    {
        if (RoundSystem.Instance != null && !RoundSystem.Instance.IsRoundAcceptingEggs)
        {
            return;
        }

        ChickenEgg egg = other.GetComponentInParent<ChickenEgg>();

        if (egg == null || !egg.TryCollect())
        {
            return;
        }

        DepositEggValue(egg.ValueCents);
        egg.ReleaseToPool();
    }

    public int DepositEggs(int eggCount)
    {
        if (eggCount <= 0
            || (RoundSystem.Instance != null
                && !RoundSystem.Instance.IsRoundAcceptingEggs))
        {
            return 0;
        }

        int commonValue = ProgressionSystem.Instance != null
            ? ProgressionSystem.Instance.GetEggValueCents(ChickenEgg.EggType.Common)
            : centsPerEgg;

        for (int index = 0; index < eggCount; index++)
        {
            DepositEggValue(commonValue);
        }

        return eggCount;
    }

    public int DepositEggValues(System.Collections.Generic.IReadOnlyList<int> values)
    {
        if (values == null
            || (RoundSystem.Instance != null
                && !RoundSystem.Instance.IsRoundAcceptingEggs))
        {
            return 0;
        }

        int deposited = 0;

        for (int index = 0; index < values.Count; index++)
        {
            if (DepositEggValue(values[index]))
            {
                deposited++;
            }
        }

        return deposited;
    }

    public bool DepositEggValue(int valueCents)
    {
        if (RoundSystem.Instance != null
            && !RoundSystem.Instance.IsRoundAcceptingEggs)
        {
            return false;
        }

        int value = Mathf.Max(1, valueCents);
        TotalDepositedCents += value;
        if (isFocused)
        {
            RoundSystem.Instance?.ShowContainerCoinReward(RewardPosition, value);
        }
        else
        {
            ShowInactiveReward(value);
        }

        EggScoreHud.AddCents(value);
        EggCollected?.Invoke(value);
        EggCollectedFromContainer?.Invoke(this, value);
        return true;
    }

    private void ShowInactiveReward(int valueCents)
    {
        inactiveRewardCents += valueCents;
        EnsureInactiveRewardText();
        inactiveRewardText.text = $"+{FormatMoney(inactiveRewardCents)}";
        inactiveRewardText.gameObject.SetActive(true);
    }

    private void EnsureInactiveRewardText()
    {
        if (inactiveRewardText != null)
        {
            return;
        }

        GameObject textObject = new GameObject("Inactive Pen Earnings");
        textObject.transform.SetParent(transform, false);
        textObject.transform.localPosition = new Vector3(0f, 0.65f, 0f);
        textObject.transform.localScale = Vector3.one * 0.1f;
        inactiveRewardText = textObject.AddComponent<TextMeshPro>();
        inactiveRewardText.font = TMP_Settings.defaultFontAsset;
        inactiveRewardText.fontSize = 3f;
        inactiveRewardText.alignment = TextAlignmentOptions.Center;
        inactiveRewardText.color = new Color(1f, 0.86f, 0.22f, 1f);
        inactiveRewardText.fontStyle = FontStyles.Bold;
        inactiveRewardText.textWrappingMode = TextWrappingModes.NoWrap;
        MeshRenderer textRenderer = textObject.GetComponent<MeshRenderer>();
        if (textRenderer != null)
        {
            // Coin and cash-note reward particle renderers use order 1000.
            // Keep the accumulated off-screen pen reward readable above them.
            textRenderer.sortingOrder = InactiveRewardSortingOrder;
        }
    }

    private static string FormatMoney(long cents)
    {
        return $"${cents / 100:N0}.{Math.Abs(cents % 100):D2}";
    }

    private void OnValidate()
    {
        centsPerEgg = Mathf.Max(1, centsPerEgg);

        Collider containerCollider = GetComponent<Collider>();

        if (containerCollider != null)
        {
            containerCollider.isTrigger = true;
        }
    }
}
