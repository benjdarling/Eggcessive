using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CapsuleCollider))]
public sealed class ChickenPickupTarget : MonoBehaviour
{
    [SerializeField] private ChickenController chicken;

    public ChickenController Chicken
    {
        get
        {
            if (chicken == null)
            {
                chicken = GetComponentInParent<ChickenController>();
            }

            return chicken;
        }
    }

    public bool CanPickUp => Chicken != null && Chicken.CanBePickedUp;

    public void Configure(ChickenController targetChicken)
    {
        chicken = targetChicken;
    }

    private void Awake()
    {
        _ = Chicken;
    }
}
