using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Identifies eggs laid by chickens so their separation force only affects eggs.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class ChickenEgg : MonoBehaviour
{
    private static readonly List<ChickenEgg> ActiveEggs = new List<ChickenEgg>();

    [Header("Size Variation")]
    [SerializeField, Min(0.01f)] private float minimumScale = 0.95f;
    [SerializeField, Min(0.01f)] private float maximumScale = 1.05f;

    private Rigidbody eggBody;

    public static IReadOnlyList<ChickenEgg> ActiveInstances => ActiveEggs;
    public bool IsHeld { get; private set; }
    public bool IsCollected { get; private set; }

    private void Awake()
    {
        eggBody = GetComponent<Rigidbody>();
        float randomScale = Random.Range(minimumScale, maximumScale);
        transform.localScale *= randomScale;
    }

    private void OnEnable()
    {
        if (!ActiveEggs.Contains(this))
        {
            ActiveEggs.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveEggs.Remove(this);
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

    private void OnValidate()
    {
        minimumScale = Mathf.Max(0.01f, minimumScale);
        maximumScale = Mathf.Max(minimumScale, maximumScale);
    }
}
