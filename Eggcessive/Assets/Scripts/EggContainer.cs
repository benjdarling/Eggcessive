using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class EggContainer : MonoBehaviour
{
    [SerializeField, Min(1)] private int centsPerEgg = 100;

    public static EggContainer Instance { get; private set; }
    public static event Action<int> EggCollected;
    public Vector3 DepositPosition => transform.position;
    public Vector3 RewardPosition => transform.position + Vector3.up * 0.22f;

    private void Awake()
    {
        Instance = this;
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
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

        DepositEggs(1);
        Destroy(egg.gameObject);
    }

    public int DepositEggs(int eggCount)
    {
        if (eggCount <= 0
            || (RoundSystem.Instance != null
                && !RoundSystem.Instance.IsRoundAcceptingEggs))
        {
            return 0;
        }

        for (int index = 0; index < eggCount; index++)
        {
            Vector3 rewardPosition = RewardPosition
                + UnityEngine.Random.insideUnitSphere * 0.06f;
            RoundSystem.Instance?.ShowCoinReward(rewardPosition, centsPerEgg);
            EggScoreHud.AddCents(centsPerEgg);
            EggCollected?.Invoke(centsPerEgg);
        }

        return eggCount;
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
