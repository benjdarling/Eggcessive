using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class EggContainer : MonoBehaviour
{
    private static readonly List<EggContainer> ActiveContainers =
        new List<EggContainer>();

    [SerializeField, Min(1)] private int centsPerEgg = 100;

    public static EggContainer Instance { get; private set; }
    public static event Action<int> EggCollected;
    public static event Action<EggContainer, int> EggCollectedFromContainer;
    public static event Action<EggContainer, int, float>
        EggCollectedWithWeightFromContainer;
    public static event Action<EggContainer> FocusedContainerChanged;
    public Vector3 DepositPosition => transform.position;
    public Vector3 RewardPosition => transform.position + Vector3.up * 0.22f;
    public long TotalDepositedCents { get; private set; }

    private bool isFocused;
    private Collider depositCollider;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveContainers.Clear();
        Instance = null;
        EggCollected = null;
        EggCollectedFromContainer = null;
        EggCollectedWithWeightFromContainer = null;
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

        depositCollider = GetComponent<Collider>();
        depositCollider.isTrigger = true;
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

    public bool IsWithinDepositRange(Vector3 worldPosition, float range)
    {
        depositCollider ??= GetComponent<Collider>();
        Vector3 closestPoint = depositCollider != null
            ? depositCollider.ClosestPoint(worldPosition)
            : transform.position;
        worldPosition.y = 0f;
        closestPoint.y = 0f;
        return Vector3.Distance(worldPosition, closestPoint)
            <= Mathf.Max(0f, range);
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

        DepositEggValue(egg.ValueCents, egg.WeightKilograms);
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

    public int DepositEggValues(
        System.Collections.Generic.IReadOnlyList<int> values,
        System.Collections.Generic.IReadOnlyList<float> weights = null)
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
            float weight = weights != null && index < weights.Count
                ? weights[index]
                : ProgressionSystem.BaseEggWeightKilograms;
            if (DepositEggValue(values[index], weight))
            {
                deposited++;
            }
        }

        return deposited;
    }

    public bool DepositEggValue(
        int valueCents,
        float weightKilograms = ProgressionSystem.BaseEggWeightKilograms)
    {
        if (RoundSystem.Instance != null
            && !RoundSystem.Instance.IsRoundAcceptingEggs)
        {
            return false;
        }

        int value = CalculateSaleValueCents(
            valueCents,
            weightKilograms);
        PenExpansionManager penManager = PenExpansionManager.Instance;
        int penIndex = penManager != null
            ? penManager.GetPenIndex(this)
            : -1;
        if (penIndex >= 0 && ProgressionSystem.Instance != null)
        {
            value = (int)Math.Min(
                int.MaxValue,
                Math.Max(
                    1d,
                    Math.Round(
                        value * (double)ProgressionSystem.Instance
                            .GetPenBonusMultiplier(penIndex),
                        MidpointRounding.AwayFromZero)));
        }

        TotalDepositedCents = value > long.MaxValue - TotalDepositedCents
            ? long.MaxValue
            : TotalDepositedCents + value;
        bool isCurrentLocalPen = penIndex >= 0
            ? penIndex == penManager.FocusedPenIndex
            : isFocused;

        if (isCurrentLocalPen)
        {
            RoundSystem.Instance?.ShowContainerCoinReward(this, value);
        }
        else if (penIndex >= 0)
        {
            PenHudController.ShowPenEarnings(penIndex, value);
        }

        EggScoreHud.AddCents(value);
        EggCollected?.Invoke(value);
        EggCollectedFromContainer?.Invoke(this, value);
        EggCollectedWithWeightFromContainer?.Invoke(
            this,
            value,
            Mathf.Max(0f, weightKilograms));
        return true;
    }

    public static int CalculateSaleValueCents(
        int valueCents,
        float weightKilograms)
    {
        float safeWeight = weightKilograms > 0f
            ? weightKilograms
            : ProgressionSystem.BaseEggWeightKilograms;
        double weightMultiplier = safeWeight
            / ProgressionSystem.BaseEggWeightKilograms;
        return (int)Math.Min(
            int.MaxValue,
            Math.Max(
                1d,
                Math.Round(
                    Mathf.Max(1, valueCents) * weightMultiplier,
                    MidpointRounding.AwayFromZero)));
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
