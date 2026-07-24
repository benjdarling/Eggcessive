using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class IncubatorEggIntake : MonoBehaviour
{
    [SerializeField] private IncubatorController incubator = null;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryAcceptEgg(other);
    }

    private void OnTriggerStay(Collider other)
    {
        // An egg can enter while held. Staying in the trigger accepts it on the
        // first physics step after the player releases it.
        TryAcceptEgg(other);
    }

    private void TryAcceptEgg(Collider other)
    {
        if (incubator != null)
        {
            incubator.TryAcceptEgg(other);
        }
    }

    private void OnValidate()
    {
        Collider intake = GetComponent<Collider>();

        if (intake != null)
        {
            intake.isTrigger = true;
        }
    }
}
