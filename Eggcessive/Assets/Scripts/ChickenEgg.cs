using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Identifies eggs laid by chickens so their separation force only affects eggs.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class ChickenEgg : MonoBehaviour
{
    public enum EggType
    {
        Standard,
        Silver,
        Gold,
        Galaxy
    }

    private static readonly List<ChickenEgg> ActiveEggs = new List<ChickenEgg>();
    private static Material[] sharedTypeMaterials;

    [Header("Size Variation")]
    [SerializeField, Min(0.01f)] private float minimumScale = 0.95f;
    [SerializeField, Min(0.01f)] private float maximumScale = 1.05f;
    [SerializeField] private Material[] typeMaterials = null;

    private Rigidbody eggBody;

    public static IReadOnlyList<ChickenEgg> ActiveInstances => ActiveEggs;
    public bool IsHeld { get; private set; }
    public bool IsCollected { get; private set; }
    public EggType Type { get; private set; }
    public int ValueCents { get; private set; } = 100;

    private void Awake()
    {
        eggBody = GetComponent<Rigidbody>();

        if (typeMaterials != null && typeMaterials.Length >= 4)
        {
            sharedTypeMaterials = typeMaterials;
        }

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

    public void ConfigureType(EggType type, int valueCents)
    {
        Type = type;
        ValueCents = Mathf.Max(1, valueCents);
        ApplyTypeVisual(gameObject, type);
        gameObject.name = type == EggType.Standard
            ? gameObject.name
            : $"{type} Egg";
    }

    public static void ApplyTypeVisual(GameObject visual, EggType type)
    {
        if (visual == null
            || sharedTypeMaterials == null
            || sharedTypeMaterials.Length < 4)
        {
            return;
        }

        Material material = sharedTypeMaterials[(int)type];

        if (material == null)
        {
            return;
        }

        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterial = material;
        }
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

    public bool TryCollectFromTool()
    {
        if (IsCollected)
        {
            return false;
        }

        IsHeld = false;
        IsCollected = true;

        if (eggBody != null)
        {
            if (!eggBody.isKinematic)
            {
                eggBody.linearVelocity = Vector3.zero;
                eggBody.angularVelocity = Vector3.zero;
            }

            eggBody.isKinematic = true;
            eggBody.useGravity = false;
        }

        return true;
    }

    public static void ClearAllActive()
    {
        for (int index = ActiveEggs.Count - 1; index >= 0; index--)
        {
            ChickenEgg egg = ActiveEggs[index];

            if (egg == null)
            {
                continue;
            }

            egg.gameObject.SetActive(false);
            Destroy(egg.gameObject);
        }

        ActiveEggs.Clear();
    }

    private void OnValidate()
    {
        minimumScale = Mathf.Max(0.01f, minimumScale);
        maximumScale = Mathf.Max(minimumScale, maximumScale);
    }
}
