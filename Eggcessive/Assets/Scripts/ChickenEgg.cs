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
        Common,
        Rare,
        Epic,
        Legendary,
        Cosmic
    }

    private static readonly List<ChickenEgg> ActiveEggs = new List<ChickenEgg>();
    private static readonly Stack<ChickenEgg> CommonEggPool = new Stack<ChickenEgg>();
    private static readonly Stack<ChickenEgg> CosmicEggPool = new Stack<ChickenEgg>();
    private static readonly int BaseMapTransform = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int MainTextureTransform = Shader.PropertyToID("_MainTex_ST");
    private static Material[] sharedTypeMaterials;
    private static MaterialPropertyBlock typePropertyBlock;
    private const float GroundContactMinimumUpDot = 0.45f;

    [SerializeField] private Material[] typeMaterials = null;
    [SerializeField] private bool cosmicVisualPrefab;

    [Header("Far Impostor")]
    [Tooltip(
        "Editable SpriteRenderer child used when this egg becomes very small " +
        "on screen. Leave empty to auto-find the prefab child.")]
    [SerializeField] private SpriteRenderer farImpostorRenderer;
    [SerializeField, Min(1f)]
    private float farImpostorScreenHeightPixels = 18f;
    [SerializeField, Min(1)]
    private int farImpostorCheckIntervalFrames = 12;

    private Rigidbody eggBody;
    private Collider[] eggColliders;
    private GrassInteractor grassInteractor;
    private Renderer[] detailedRenderers = System.Array.Empty<Renderer>();
    private bool[] detailedRendererDefaults = System.Array.Empty<bool>();
    private ParticleSystem[] detailedParticles =
        System.Array.Empty<ParticleSystem>();
    private Vector3 baseLocalScale;
    private float baseRigidbodyMass;
    private readonly HashSet<Collider> groundContacts =
        new HashSet<Collider>();
    private bool isPooled;
    private bool usingFarImpostor;
    private int nextFarImpostorCheckFrame;
    private static Camera impostorCamera;
    private bool penVisualsEnabled = true;

    public static IReadOnlyList<ChickenEgg> ActiveInstances => ActiveEggs;
    public bool IsHeld { get; private set; }
    public bool IsCollected { get; private set; }
    public bool IsGroundedForPickupPreview =>
        !IsHeld && !IsCollected && groundContacts.Count > 0;
    public EggType Type { get; private set; }
    public int ValueCents { get; private set; } = 100;
    public float WeightScaleMultiplier { get; private set; } = 1f;
    public float WeightKilograms =>
        ProgressionSystem.BaseEggWeightKilograms * WeightScaleMultiplier;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ActiveEggs.Clear();
        CommonEggPool.Clear();
        CosmicEggPool.Clear();
        sharedTypeMaterials = null;
        typePropertyBlock = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureCollisionLayers()
    {
        int chickenLayer = LayerMask.NameToLayer(ChickenLayerName);
        int eggLayer = LayerMask.NameToLayer(EggLayerName);
        if (chickenLayer >= 0 && eggLayer >= 0)
        {
            // Chicken movement and egg pushing are handled explicitly. Their
            // trigger colliders do not need PhysX to build chicken/chicken or
            // chicken/egg contact pairs.
            Physics.IgnoreLayerCollision(
                chickenLayer,
                chickenLayer,
                true);
            Physics.IgnoreLayerCollision(chickenLayer, eggLayer, true);
        }
    }

    public static ChickenEgg Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation)
    {
        ChickenEgg prefabEgg = prefab != null
            ? prefab.GetComponent<ChickenEgg>()
            : null;
        Stack<ChickenEgg> pool = prefabEgg != null && prefabEgg.cosmicVisualPrefab
            ? CosmicEggPool
            : CommonEggPool;

        while (pool.Count > 0)
        {
            ChickenEgg pooledEgg = pool.Pop();
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
        baseRigidbodyMass = eggBody.mass;
        CacheFarImpostor();

        if (typeMaterials != null && typeMaterials.Length >= 5)
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

        nextFarImpostorCheckFrame = Time.frameCount
            + Mathf.Abs(GetInstanceID())
            % Mathf.Max(1, farImpostorCheckIntervalFrames);
        UpdateFarImpostor(true);
    }

    private void OnDisable()
    {
        ActiveEggs.Remove(this);
        groundContacts.Clear();
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

    private void Update()
    {
        UpdateFarImpostor(false);
    }

    public void SetPenVisualsEnabled(bool enabled)
    {
        if (penVisualsEnabled == enabled)
        {
            return;
        }

        penVisualsEnabled = enabled;
        if (!enabled)
        {
            if (farImpostorRenderer != null)
            {
                farImpostorRenderer.enabled = false;
            }

            for (int index = 0; index < detailedRenderers.Length; index++)
            {
                if (detailedRenderers[index] != null)
                {
                    detailedRenderers[index].enabled = false;
                }
            }

            for (int index = 0; index < detailedParticles.Length; index++)
            {
                if (detailedParticles[index] != null)
                {
                    detailedParticles[index].Stop(
                        true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            return;
        }

        SetFarImpostorActive(false, true);
        UpdateFarImpostor(true);
    }

    public bool BeginCarry()
    {
        if (IsHeld || IsCollected)
        {
            return false;
        }

        IsHeld = true;
        groundContacts.Clear();

        if (!eggBody.isKinematic)
        {
            eggBody.linearVelocity = Vector3.zero;
            eggBody.angularVelocity = Vector3.zero;
        }

        eggBody.isKinematic = true;
        eggBody.useGravity = false;
        SetGrassInteractionEnabled(true);
        UpdateFarImpostor(true);
        return true;
    }

    public void ConfigureType(EggType type, int valueCents)
    {
        Type = type;
        ValueCents = Mathf.Max(1, valueCents);
        WeightScaleMultiplier = ProgressionSystem.Instance != null
            ? ProgressionSystem.Instance.RollEggWeightScale(type)
            : 1f + (int)type * 0.075f;
        // Weight is a volume measurement, so convert it to a linear scale via
        // the cube root. The root still scales both visuals and colliders while
        // Rigidbody mass continues to use the full rolled weight multiplier.
        float linearScaleMultiplier = Mathf.Pow(
            Mathf.Max(0.001f, WeightScaleMultiplier),
            1f / 3f);
        transform.localScale = baseLocalScale * linearScaleMultiplier;
        if (eggBody != null)
        {
            eggBody.mass = baseRigidbodyMass * WeightScaleMultiplier;
        }
        ApplyTypeVisual(gameObject, type);
        ApplyFarImpostorTint();
        gameObject.name = type == EggType.Common
            ? gameObject.name
            : $"{type} Egg";
    }

    public static void ApplyTypeVisual(GameObject visual, EggType type)
    {
        if (visual == null
            || sharedTypeMaterials == null
            || sharedTypeMaterials.Length < 5)
        {
            return;
        }

        Material material = sharedTypeMaterials[(int)type];

        if (material == null)
        {
            return;
        }

        int atlasIndex = type switch
        {
            EggType.Rare => 1,
            EggType.Epic => 2,
            _ => 0
        };
        float offsetX = atlasIndex * (16f / 512f);
        Vector4 textureTransform = (int)type <= (int)EggType.Epic
            ? new Vector4(1f, 1f, offsetX, 0f)
            : new Vector4(1f, 1f, 0f, 0f);
        typePropertyBlock ??= new MaterialPropertyBlock();

        foreach (MeshRenderer renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.sharedMaterial = material;
            renderer.GetPropertyBlock(typePropertyBlock);
            typePropertyBlock.SetVector(BaseMapTransform, textureTransform);
            typePropertyBlock.SetVector(MainTextureTransform, textureTransform);
            renderer.SetPropertyBlock(typePropertyBlock);
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

    public void SnapWhileHeld(Vector3 position)
    {
        if (!IsHeld || IsCollected)
        {
            return;
        }

        eggBody.position = position;
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
        groundContacts.Clear();
        eggBody.WakeUp();
        SetGrassInteractionEnabled(true);
        UpdateFarImpostor(true);
    }

    public bool TryCollect()
    {
        if (IsHeld || IsCollected)
        {
            return false;
        }

        IsCollected = true;
        SetGrassInteractionEnabled(false);
        SetFarImpostorActive(false, true);
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
        SetFarImpostorActive(false, true);
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
        SetFarImpostorActive(false, true);

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

        transform.SetParent(null, true);
        gameObject.SetActive(false);
        (cosmicVisualPrefab ? CosmicEggPool : CommonEggPool).Push(this);
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
        groundContacts.Clear();
        Type = EggType.Common;
        ValueCents = 100;
        WeightScaleMultiplier = 1f;
        transform.localScale = baseLocalScale;

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
            eggBody.mass = baseRigidbodyMass;
            eggBody.isKinematic = false;
            eggBody.useGravity = true;
            eggBody.linearVelocity = Vector3.zero;
            eggBody.angularVelocity = Vector3.zero;
            eggBody.WakeUp();
        }

        ApplyTypeVisual(gameObject, EggType.Common);
        ApplyFarImpostorTint();
        SetFarImpostorActive(false, true);
        SetGrassInteractionEnabled(true);
    }

    private void CacheFarImpostor()
    {
        if (farImpostorRenderer == null)
        {
            farImpostorRenderer =
                GetComponentInChildren<SpriteRenderer>(true);
        }

        var renderers = new List<Renderer>();
        foreach (Renderer renderer
                 in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != null && renderer != farImpostorRenderer)
            {
                renderers.Add(renderer);
            }
        }

        detailedRenderers = renderers.ToArray();
        detailedRendererDefaults =
            new bool[detailedRenderers.Length];
        for (int index = 0; index < detailedRenderers.Length; index++)
        {
            detailedRendererDefaults[index] =
                detailedRenderers[index] != null
                && detailedRenderers[index].enabled;
        }

        detailedParticles =
            GetComponentsInChildren<ParticleSystem>(true);
        if (farImpostorRenderer != null)
        {
            farImpostorRenderer.enabled = false;
        }
    }

    private void UpdateFarImpostor(bool force)
    {
        if (!penVisualsEnabled)
        {
            return;
        }

        if (farImpostorRenderer == null)
        {
            return;
        }

        if (!force
            && Time.frameCount < nextFarImpostorCheckFrame)
        {
            return;
        }

        nextFarImpostorCheckFrame = Time.frameCount
            + Mathf.Max(1, farImpostorCheckIntervalFrames);
        if (impostorCamera == null
            || !impostorCamera.isActiveAndEnabled)
        {
            impostorCamera = Camera.main;
        }

        Camera camera = impostorCamera;
        if (camera == null)
        {
            return;
        }

        bool shouldUseImpostor = !IsHeld
            && !IsCollected
            && CalculateScreenHeightPixels(camera)
                <= farImpostorScreenHeightPixels;
        SetFarImpostorActive(shouldUseImpostor, force);
        if (shouldUseImpostor)
        {
            farImpostorRenderer.transform.rotation =
                Quaternion.LookRotation(
                    camera.transform.forward,
                    camera.transform.up);
        }
    }

    private float CalculateScreenHeightPixels(Camera camera)
    {
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool hasBounds = false;
        for (int index = 0; index < detailedRenderers.Length; index++)
        {
            Renderer renderer = detailedRenderers[index];
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        float worldHeight = hasBounds
            ? Mathf.Max(0.001f, bounds.size.y)
            : 0.1f;
        if (camera.orthographic)
        {
            return worldHeight
                / Mathf.Max(0.001f, camera.orthographicSize * 2f)
                * camera.pixelHeight;
        }

        float distance = Mathf.Max(
            0.001f,
            Vector3.Distance(
                bounds.center,
                camera.transform.position));
        float visibleWorldHeight = distance
            * 2f
            * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return worldHeight
            / Mathf.Max(0.001f, visibleWorldHeight)
            * camera.pixelHeight;
    }

    private void SetFarImpostorActive(bool active, bool force)
    {
        if (!penVisualsEnabled)
        {
            return;
        }

        if (!force && usingFarImpostor == active)
        {
            return;
        }

        usingFarImpostor = active;
        farImpostorRenderer.enabled = active;

        for (int index = 0; index < detailedRenderers.Length; index++)
        {
            Renderer renderer = detailedRenderers[index];
            if (renderer != null)
            {
                renderer.enabled = !active
                    && detailedRendererDefaults[index];
            }
        }

        for (int index = 0; index < detailedParticles.Length; index++)
        {
            ParticleSystem particles = detailedParticles[index];
            if (particles == null)
            {
                continue;
            }

            if (active)
            {
                particles.Pause(true);
            }
            else if (particles.isPaused)
            {
                particles.Play(true);
            }
        }
    }

    private void ApplyFarImpostorTint()
    {
        if (farImpostorRenderer == null)
        {
            return;
        }

        farImpostorRenderer.color = Type switch
        {
            EggType.Rare => new Color(0.45f, 0.72f, 1f, 1f),
            EggType.Epic => new Color(0.72f, 0.42f, 0.95f, 1f),
            EggType.Legendary => new Color(1f, 0.66f, 0.18f, 1f),
            EggType.Cosmic => new Color(0.45f, 0.2f, 0.72f, 1f),
            _ => Color.white
        };
    }

    private void OnCollisionEnter(Collision collision)
    {
        UpdateGroundContact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        UpdateGroundContact(collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision != null && collision.collider != null)
        {
            groundContacts.Remove(collision.collider);
        }
    }

    private void UpdateGroundContact(Collision collision)
    {
        if (collision == null || collision.collider == null)
        {
            return;
        }

        bool hasSupportingContact = false;

        for (int index = 0; index < collision.contactCount; index++)
        {
            ContactPoint contact = collision.GetContact(index);

            if (Vector3.Dot(contact.normal, Vector3.up)
                >= GroundContactMinimumUpDot)
            {
                hasSupportingContact = true;
                break;
            }
        }

        if (hasSupportingContact)
        {
            groundContacts.Add(collision.collider);
        }
        else
        {
            groundContacts.Remove(collision.collider);
        }
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
        farImpostorScreenHeightPixels = Mathf.Max(
            1f,
            farImpostorScreenHeightPixels);
        farImpostorCheckIntervalFrames = Mathf.Max(
            1,
            farImpostorCheckIntervalFrames);
    }
}
