using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public sealed class CrosshatcherChickenIntake : MonoBehaviour
{
    [SerializeField] private CrosshatcherController crosshatcher = null;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        Rigidbody body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        crosshatcher?.TryAcceptChicken(other);
    }

    private void OnValidate()
    {
        Collider intake = GetComponent<Collider>();

        if (intake != null)
        {
            intake.isTrigger = true;
        }

        Rigidbody body = GetComponent<Rigidbody>();

        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
    }
}
