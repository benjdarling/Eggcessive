using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Identifies eggs laid by chickens so their separation force only affects eggs.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class ChickenEgg : MonoBehaviour
{
    private const string ChickenLayerName = "Chicken";
    private const string EggLayerName = "Egg";

    public enum EggType
    {
        Standard,
        Silver,
        Gold,
        Galaxy
    }

    private static readonly List<ChickenEgg> ActiveEggs = new List<ChickenEgg>();
    private static readonly Stack<ChickenEgg> EggPool = new Stack<ChickenEgg>();
    private static Material[] sharedTypeMaterials;

    [Header("Size Variation")]
    [SerializeField, Min(0.01f)] private float minimumScale = 0.95f;
    [SerializeField, Min(0.01f)] private float maximumScale = 1.05f;
    [SerializeField] private Material[] typeMaterials = null;

    private Rigidbody eggBody;
    private Collider[] eggColliders;
    private GrassInteractor grassInteractor;
    private Vector3 baseLocalScale;
    private bool isPooled;

    public static IReadOnlyList<ChickenEgg> ActiveInstances => ActiveEggs;
    public bool IsHeld { get; private set; }
    public bool IsCollected { get; private set; }
    public EggType Type { get; private set; }
    public int ValueCents { get; private set; } = 100;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveEggs.Clear();
        EggPool.Clear();
        sharedTypeMaterials = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureCollisionLayers()
    {
        int chickenLayer = LayerMask.NameToLayer(ChickenLayerName);
        int eggLayer = LayerMask.NameToLayer(EggLayerName);
        if (chickenLayer >= 0 && eggLayer >= 0)
        {
            Physics.IgnoreLayerCollision(chickenLayer, eggLayer, true);
        }
    }

    public static ChickenEgg Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation)
    {
        while (EggPool.Count > 0)
        {
            ChickenEgg pooledEgg = EggPool.Pop();
            if (pooledEgg == null)
            {
                continue;
            }

            pooledEgg.transform.SetParent(null, true);
            pooledEgg.transform.SetPositionAndRotation(position, rotation);
            pooledEgg.gameObject.SetActive(true);
            return pooledEgg;
        }

        GameObject instance = Instantiate(prefab, position, rotation);
        if (!instance.TryGetComponent(out ChickenEgg egg))
        {
            egg = instance.AddComponent<ChickenEgg>();
        }

        return egg;
    }

    private void Awake()
    {
        eggBody = GetComponent<Rigidbody>();
        eggColliders = GetComponentsInChildren<Collider>(true);
        grassInteractor = GetComponent<GrassInteractor>();
        baseLocalScale = transform.localScale;

        if (typeMaterials != null && typeMaterials.Length >= 4)
        {
            sharedTypeMaterials = typeMaterials;
        }
    }

    private void OnEnable()
    {
        ResetForSpawn();
        if (!ActiveEggs.Contains(this))
        {
            ActiveEggs.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveEggs.Remove(this);
    }

    private void FixedUpdate()
    {
        bool shouldInteractWithGrass = IsHeld
            || (!IsCollected
                && eggBody != null
                && !eggBody.isKinematic
                && !eggBody.IsSleeping());
        SetGrassInteractionEnabled(shouldInteractWithGrass);
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
        SetGrassInteractionEnabled(true);
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
        eggBody.WakeUp();
        SetGrassInteractionEnabled(true);
    }

    public bool TryCollect()
    {
        if (IsHeld || IsCollected)
        {
            return false;
        }

        IsCollected = true;
        SetGrassInteractionEnabled(false);
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

        SetGrassInteractionEnabled(false);
        return true;
    }

    public void ReleaseToPool()
    {
        if (isPooled)
        {
            return;
        }

        isPooled = true;
        IsHeld = false;
        IsCollected = true;
        SetGrassInteractionEnabled(false);

        if (eggBody != null)
        {
            eggBody.linearVelocity = Vector3.zero;
            eggBody.angularVelocity = Vector3.zero;
            eggBody.isKinematic = true;
            eggBody.useGravity = false;
        }

        transform.SetParent(null, true);
        gameObject.SetActive(false);
        EggPool.Push(this);
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

            if (egg.IsHeld || egg.IsCollected)
            {
                egg.gameObject.SetActive(false);
                Destroy(egg.gameObject);
                continue;
            }

            egg.ReleaseToPool();
        }

        ActiveEggs.Clear();
    }

    private void ResetForSpawn()
    {
        isPooled = false;
        IsHeld = false;
        IsCollected = false;
        Type = EggType.Standard;
        ValueCents = 100;
        transform.localScale = baseLocalScale
            * Random.Range(minimumScale, maximumScale);

        int eggLayer = LayerMask.NameToLayer(EggLayerName);
        if (eggLayer >= 0)
        {
            gameObject.layer = eggLayer;
        }

        if (eggColliders != null)
        {
            for (int index = 0; index < eggColliders.Length; index++)
            {
                if (eggColliders[index] != null)
                {
                    eggColliders[index].enabled = true;
                }
            }
        }

        if (eggBody != null)
        {
            eggBody.isKinematic = false;
            eggBody.useGravity = true;
            eggBody.linearVelocity = Vector3.zero;
            eggBody.angularVelocity = Vector3.zero;
            eggBody.WakeUp();
        }

        ApplyTypeVisual(gameObject, EggType.Standard);
        SetGrassInteractionEnabled(true);
    }

    private void SetGrassInteractionEnabled(bool enabled)
    {
        if (grassInteractor != null && grassInteractor.enabled != enabled)
        {
            grassInteractor.enabled = enabled;
        }
    }

    private void OnValidate()
    {
        minimumScale = Mathf.Max(0.01f, minimumScale);
        maximumScale = Mathf.Max(minimumScale, maximumScale);
    }
}
