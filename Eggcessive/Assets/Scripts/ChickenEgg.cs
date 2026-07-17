using UnityEngine;

/// <summary>
/// Identifies eggs laid by chickens so their separation force only affects eggs.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class ChickenEgg : MonoBehaviour
{
    private Rigidbody eggBody;

    public bool IsHeld { get; private set; }
    public bool IsCollected { get; private set; }

    private void Awake()
    {
        eggBody = GetComponent<Rigidbody>();
    }

    public bool BeginCarry()
    {
        if (IsHeld || IsCollected)
        {
            return false;
        }

        IsHeld = true;

        if (!eggBody.isKinematic)
        {
            eggBody.linearVelocity = Vector3.zero;
            eggBody.angularVelocity = Vector3.zero;
        }

        eggBody.isKinematic = true;
        eggBody.useGravity = false;
        return true;
    }

    public void MoveWhileHeld(Vector3 target, float followSpeed)
    {
        if (!IsHeld || IsCollected)
        {
            return;
        }

        float followAmount = 1f - Mathf.Exp(-followSpeed * Time.fixedDeltaTime);
        eggBody.MovePosition(Vector3.Lerp(eggBody.position, target, followAmount));
    }

    public void Release(Vector3 position)
    {
        if (!IsHeld || IsCollected)
        {
            return;
        }

        eggBody.position = position;
        eggBody.isKinematic = false;
        eggBody.useGravity = true;
        eggBody.linearVelocity = Vector3.zero;
        eggBody.angularVelocity = Vector3.zero;
        IsHeld = false;
    }

    public bool TryCollect()
    {
        if (IsHeld || IsCollected)
        {
            return false;
        }

        IsCollected = true;
        return true;
    }
}
