using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class EggCarryController : MonoBehaviour
{
    [Header("Pickup")]
    [SerializeField, Min(0.1f)] private float pickupDistance = 100f;
    [SerializeField] private LayerMask pickupLayers = ~0;

    [Header("Carrying")]
    [SerializeField, Min(0f)] private float carryHeight = 0.3f;
    [SerializeField, Min(0.01f)] private float followSpeed = 25f;

    private Camera viewCamera;
    private ChickenEgg heldEgg;
    private Vector3 carryTarget;

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null)
        {
            return;
        }

        Vector2 pointerPosition = mouse.position.ReadValue();

        if (heldEgg == null && mouse.leftButton.wasPressedThisFrame)
        {
            TryPickUpEgg(pointerPosition);
        }

        if (heldEgg == null)
        {
            return;
        }

        UpdateCarryTarget(pointerPosition);

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            ReleaseEgg();
        }
    }

    private void FixedUpdate()
    {
        if (heldEgg != null)
        {
            heldEgg.MoveWhileHeld(carryTarget, followSpeed);
        }
    }

    private void OnDisable()
    {
        ReleaseEgg();
    }

    private void TryPickUpEgg(Vector2 pointerPosition)
    {
        Ray ray = viewCamera.ScreenPointToRay(pointerPosition);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            pickupDistance,
            pickupLayers,
            QueryTriggerInteraction.Ignore);

        ChickenEgg nearestEgg = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            ChickenEgg egg = hit.collider.GetComponentInParent<ChickenEgg>();

            if (egg == null || egg.IsHeld || egg.IsCollected || hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestEgg = egg;
            nearestDistance = hit.distance;
        }

        if (nearestEgg == null || !nearestEgg.BeginCarry())
        {
            return;
        }

        heldEgg = nearestEgg;
        carryTarget = heldEgg.transform.position;
        UpdateCarryTarget(pointerPosition);
    }

    private void UpdateCarryTarget(Vector2 pointerPosition)
    {
        Ray ray = viewCamera.ScreenPointToRay(pointerPosition);
        Plane carryPlane = new Plane(Vector3.up, new Vector3(0f, carryHeight, 0f));

        if (carryPlane.Raycast(ray, out float distance))
        {
            carryTarget = ray.GetPoint(distance);
        }
    }

    private void ReleaseEgg()
    {
        if (heldEgg == null)
        {
            return;
        }

        heldEgg.Release(carryTarget);
        heldEgg = null;
    }

    private void OnValidate()
    {
        pickupDistance = Mathf.Max(0.1f, pickupDistance);
        carryHeight = Mathf.Max(0f, carryHeight);
        followSpeed = Mathf.Max(0.01f, followSpeed);
    }
}
