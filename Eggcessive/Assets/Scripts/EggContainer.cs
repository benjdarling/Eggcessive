using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class EggContainer : MonoBehaviour
{
    [SerializeField, Min(1)] private int centsPerEgg = 50;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
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
        ChickenEgg egg = other.GetComponentInParent<ChickenEgg>();

        if (egg == null || !egg.TryCollect())
        {
            return;
        }

        EggScoreHud.AddCents(centsPerEgg);
        Destroy(egg.gameObject);
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
