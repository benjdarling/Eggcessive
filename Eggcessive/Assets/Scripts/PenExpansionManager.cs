using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-150)]
[DisallowMultipleComponent]
public sealed class PenExpansionManager : MonoBehaviour
{
    private const float RuntimeGroundMaskPixelsPerMetre = 16f;
    private const int MaximumRuntimeGroundMaskResolution = 1024;

    private sealed class PenSlot
    {
        public int costCents;
        public bool owned;
        public float horizontalOffset;
        public GameObject runtimeRoot;
        public Transform terrain;
        public InteractiveGrassSystem grass;
        public EggContainer eggContainer;
        public IncubatorController incubator;
        public CrosshatcherController crosshatcher;
        public PenTruckController truck;
    }

    private sealed class PenBuffer
    {
        public Transform terrain;
        public Material groundMaterial;
        public InteractiveGrassSystem grass;
    }

    [Header("Pen Layout")]
    [SerializeField, Min(2)] private int penCount = 8;
    [SerializeField, Min(0.1f)] private float penSpacing = 5f;
    [SerializeField, Min(0f)] private float additionalPenSpacing = 1f;
    [SerializeField, Min(0)] private int starterChickensPerPurchasedPen = 3;

    [Header("Purchase Costs")]
    [SerializeField, Min(1)] private int firstAdditionalPenCostCents = 2500;
    [SerializeField, Min(1f)] private float penCostMultiplier = 2f;

    [Header("Distant Pen Visuals")]
    [Tooltip("Chicken animators/meshes and egg meshes beyond this horizontal distance are disabled. Physics remains active.")]
    [SerializeField, Min(1f)] private float visualActivationDistance = 8f;
    [SerializeField, Min(0.1f)] private float visualRefreshInterval = 0.5f;

    private readonly List<PenSlot> slots = new List<PenSlot>();
    private readonly List<Material> runtimeGroundMaterials = new List<Material>();
    private readonly List<Texture2D> runtimeGroundMasks = new List<Texture2D>();
    private Transform terrainTemplate;
    private GameObject volumeTemplate;
    private InteractiveGrassSystem grassTemplate;
    private EggContainer containerTemplate;
    private IncubatorController incubatorTemplate;
    private CrosshatcherController crosshatcherTemplate;
    private GameObject bufferRoot;
    private PenBuffer leftBuffer;
    private PenBuffer rightBuffer;
    private Vector4 baseMaskWorldRect;
    private Vector4 baseOuterMaskWorldRect;
    private Material worldGroundMaterial;
    private Texture2D worldGroundMask;
    private Vector3 baseCameraPivotPosition;
    private int focusedPenIndex;
    private float nextVisualRefreshTime;

    public static PenExpansionManager Instance { get; private set; }
    public event Action StateChanged;
    public bool IsInitialized { get; private set; }
    public int PenCount => slots.Count;
    public int FocusedPenIndex => focusedPenIndex;
    public int OwnedPenCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < slots.Count; index++)
            {
                if (slots[index].owned)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int NextUnownedPenIndex
    {
        get
        {
            for (int index = 0; index < slots.Count; index++)
            {
                if (!slots[index].owned)
                {
                    return index;
                }
            }

            return -1;
        }
    }

    private void Awake()
    {
        Instance = this;
        baseCameraPivotPosition = transform.position;
    }

    private void Start()
    {
        InitializePens();
    }

    private void Update()
    {
        if (!IsInitialized || Time.unscaledTime < nextVisualRefreshTime)
        {
            return;
        }

        nextVisualRefreshTime = Time.unscaledTime + visualRefreshInterval;
        RefreshDistantVisuals();
    }

    private void OnDestroy()
    {
        for (int index = 0; index < runtimeGroundMaterials.Count; index++)
        {
            if (runtimeGroundMaterials[index] != null)
            {
                Destroy(runtimeGroundMaterials[index]);
            }
        }

        runtimeGroundMaterials.Clear();
        for (int index = 0; index < runtimeGroundMasks.Count; index++)
        {
            if (runtimeGroundMasks[index] != null)
            {
                Destroy(runtimeGroundMasks[index]);
            }
        }

        runtimeGroundMasks.Clear();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool IsPenOwned(int index)
    {
        return IsValidIndex(index) && slots[index].owned;
    }

    public int GetPenCostCents(int index)
    {
        return IsValidIndex(index) ? slots[index].costCents : 0;
    }

    public int GetPenIndex(EggContainer container)
    {
        if (container == null)
        {
            return -1;
        }

        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index].eggContainer == container)
            {
                return index;
            }
        }

        return GetClosestPenIndex(container.transform.position);
    }

    public int GetClosestPenIndex(Vector3 worldPosition)
    {
        if (slots.Count == 0)
        {
            return 0;
        }

        float relativeX = worldPosition.x - baseCameraPivotPosition.x;
        return Mathf.Clamp(
            Mathf.RoundToInt(relativeX / Mathf.Max(0.1f, penSpacing)),
            0,
            slots.Count - 1);
    }

    public Vector3 GetPenCenter(int index)
    {
        Vector3 center = baseCameraPivotPosition;
        if (index >= 0 && index < slots.Count)
        {
            center.x += slots[index].horizontalOffset;
        }

        return center;
    }

    public int GetChickenCount(int penIndex)
    {
        int count = 0;
        IReadOnlyList<ChickenController> chickens =
            ChickenController.ActiveInstances;
        for (int index = 0; index < chickens.Count; index++)
        {
            ChickenController chicken = chickens[index];
            if (chicken != null
                && GetClosestPenIndex(chicken.transform.position) == penIndex)
            {
                count++;
            }
        }

        return count;
    }

    public long GetPenEarningsCents(int penIndex)
    {
        return IsValidIndex(penIndex)
            && slots[penIndex].eggContainer != null
                ? slots[penIndex].eggContainer.TotalDepositedCents
                : 0L;
    }

    public bool HasRobotInPen(int penIndex)
    {
        if (!IsValidIndex(penIndex)
            || slots[penIndex].eggContainer == null)
        {
            return false;
        }

        IReadOnlyList<EggCollectorRobot> robots =
            EggCollectorRobot.ActiveInstances;
        for (int index = robots.Count - 1; index >= 0; index--)
        {
            EggCollectorRobot robot = robots[index];
            if (robot != null
                && robot.TargetContainer == slots[penIndex].eggContainer)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsChickenCapReachedAt(Vector3 worldPosition)
    {
        PenExpansionManager manager = Instance;
        if (manager == null || !manager.IsInitialized)
        {
            return ChickenController.ActiveInstances.Count
                >= ChickenController.MaximumChickenCount;
        }

        int penIndex = manager.GetClosestPenIndex(worldPosition);
        return manager.GetChickenCount(penIndex)
            >= ChickenController.MaximumChickenCount;
    }

    public IncubatorController GetFocusedIncubator()
    {
        return IsValidIndex(focusedPenIndex)
            ? slots[focusedPenIndex].incubator
            : null;
    }

    public CrosshatcherController GetFocusedCrosshatcher()
    {
        return IsValidIndex(focusedPenIndex)
            ? slots[focusedPenIndex].crosshatcher
            : null;
    }

    public int GetFocusedTruckEggCount(int primaryPenEggCount)
    {
        if (!IsValidIndex(focusedPenIndex) || focusedPenIndex == 0)
        {
            return primaryPenEggCount;
        }

        PenTruckController focusedTruck = slots[focusedPenIndex].truck;
        return focusedTruck != null
            ? focusedTruck.EggsTowardTruck
            : 0;
    }

    public void SynchronizeEquipmentAcrossPens()
    {
        if (!IsInitialized)
        {
            return;
        }

        for (int index = 0; index < slots.Count; index++)
        {
            PenSlot slot = slots[index];
            if (!slot.owned)
            {
                continue;
            }

            SynchronizeIncubator(slot.incubator);
            SynchronizeCrosshatcher(slot.crosshatcher);
        }
    }

    public bool TryPurchaseNextPen()
    {
        int nextIndex = NextUnownedPenIndex;
        return nextIndex >= 0 && TryActivatePen(nextIndex);
    }

    public bool TryActivatePen(int index)
    {
        if (!IsValidIndex(index))
        {
            return false;
        }

        PenSlot slot = slots[index];
        if (!slot.owned)
        {
            if (!EggScoreHud.TrySpendCents(slot.costCents))
            {
                StateChanged?.Invoke();
                return false;
            }

            CreatePurchasedPen(index, slot);
            slot.owned = true;
            RefreshEdgeBuffers();
            RefreshWorldGroundCoverage();
            RoundSystem.Instance?.PlayCashRegisterSfx();
        }

        FocusPen(index);
        return true;
    }

    public void FocusPen(int index)
    {
        if (!IsValidIndex(index) || !slots[index].owned)
        {
            return;
        }

        focusedPenIndex = index;
        Vector3 pivotPosition = baseCameraPivotPosition;
        pivotPosition.x += slots[index].horizontalOffset;
        transform.position = pivotPosition;
        EggContainer.SetFocusedContainer(slots[index].eggContainer);
        RefreshDistantVisuals();
        RoundSystem.Instance?.NotifyPenTruckProgressChanged();
        StateChanged?.Invoke();
    }

    private void InitializePens()
    {
        terrainTemplate = GameObject.Find("Terrain_Pens")?.transform;
        volumeTemplate = GameObject.Find("VolumePen");
        grassTemplate = FindFirstObjectByType<InteractiveGrassSystem>(
            FindObjectsInactive.Include);
        containerTemplate = EggContainer.Instance != null
            ? EggContainer.Instance
            : FindFirstObjectByType<EggContainer>(FindObjectsInactive.Include);
        incubatorTemplate = FindFirstObjectByType<IncubatorController>(
            FindObjectsInactive.Include);
        crosshatcherTemplate = FindFirstObjectByType<CrosshatcherController>(
            FindObjectsInactive.Include);

        if (terrainTemplate == null
            || volumeTemplate == null
            || grassTemplate == null
            || containerTemplate == null)
        {
            Debug.LogError(
                "Pen expansion needs Terrain_Pens, VolumePen, Interactive Grass, "
                + "and the original EggContainer in the scene.",
                this);
            enabled = false;
            return;
        }

        float measuredTerrainWidth = MeasureTerrainWidth();
        if (measuredTerrainWidth > 0.1f)
        {
            // These surfaces are resized to cover the spacing between pens.
            // Keeping the authored template static can leave its renderer in a
            // pre-baked static batch at the old width while its collider uses
            // the new transform, producing a visible strip at each boundary.
            SetStaticRecursively(terrainTemplate, false);
            penSpacing = measuredTerrainWidth + additionalPenSpacing;
            FitTerrainSurfacesToWidth(
                terrainTemplate,
                penSpacing);
            // Collider.bounds can otherwise retain the pre-resize width until
            // the next physics step. Grass placement is generated immediately
            // below, so synchronize once to keep visual and collision coverage
            // identical from the first frame.
            Physics.SyncTransforms();
            grassTemplate.ConfigureRuntimePen(
                terrainTemplate,
                Vector3.zero,
                null,
                true);
        }

        SynchronizeGroundMaskRects();

        slots.Clear();
        for (int index = 0; index < penCount; index++)
        {
            slots.Add(new PenSlot
            {
                costCents = index == 0 ? 0 : CalculateCost(index),
                owned = index == 0,
                horizontalOffset = index * penSpacing,
                terrain = index == 0 ? terrainTemplate : null,
                grass = index == 0 ? grassTemplate : null,
                eggContainer = index == 0 ? containerTemplate : null,
                incubator = index == 0 ? incubatorTemplate : null,
                crosshatcher = index == 0 ? crosshatcherTemplate : null
            });
        }

        focusedPenIndex = 0;
        EggContainer.SetFocusedContainer(containerTemplate);
        CreateEdgeBuffers();
        RefreshWorldGroundCoverage();
        IsInitialized = true;
        RefreshDistantVisuals();
        StateChanged?.Invoke();
    }

    private int CalculateCost(int index)
    {
        double cost = firstAdditionalPenCostCents
            * Math.Pow(penCostMultiplier, index - 1);
        return (int)Math.Min(int.MaxValue, Math.Round(cost));
    }

    private float MeasureTerrainWidth()
    {
        Transform penSurface = terrainTemplate.Find("grass_pen");
        Renderer renderer = penSurface != null
            ? penSurface.GetComponent<Renderer>()
            : null;
        return renderer != null ? renderer.bounds.size.x : penSpacing;
    }

    private static void FitTerrainSurfacesToWidth(
        Transform terrain,
        float targetWorldWidth)
    {
        if (terrain == null || targetWorldWidth <= 0f)
        {
            return;
        }

        for (int index = 0; index < terrain.childCount; index++)
        {
            Transform surface = terrain.GetChild(index);
            Renderer renderer = surface.GetComponent<Renderer>();
            if (renderer == null || renderer.bounds.size.x <= 0.0001f)
            {
                continue;
            }

            Vector3 scale = surface.localScale;
            scale.x *= targetWorldWidth / renderer.bounds.size.x;
            surface.localScale = scale;
        }
    }

    private void SynchronizeGroundMaskRects()
    {
        Transform penSurface = terrainTemplate.Find("grass_pen");
        Transform backgroundSurface =
            terrainTemplate.Find("grass_background");
        Renderer penRenderer = penSurface != null
            ? penSurface.GetComponent<Renderer>()
            : null;
        Renderer backgroundRenderer = backgroundSurface != null
            ? backgroundSurface.GetComponent<Renderer>()
            : null;
        Material source = grassTemplate.GroundColourSource;
        if (penRenderer == null || backgroundRenderer == null || source == null)
        {
            return;
        }

        Bounds penBounds = penRenderer.bounds;
        Bounds outerBounds = penBounds;
        outerBounds.Encapsulate(backgroundRenderer.bounds);
        baseMaskWorldRect = BoundsToWorldRect(penBounds);
        baseOuterMaskWorldRect = BoundsToWorldRect(outerBounds);

        if (source.HasProperty("_MaskWorldRect"))
        {
            source.SetVector("_MaskWorldRect", baseMaskWorldRect);
        }

        if (source.HasProperty("_OuterMaskWorldRect"))
        {
            source.SetVector("_OuterMaskWorldRect", baseOuterMaskWorldRect);
        }
    }

    private static Vector4 BoundsToWorldRect(Bounds bounds)
    {
        return new Vector4(
            bounds.min.x,
            bounds.min.z,
            Mathf.Max(0.0001f, bounds.size.x),
            Mathf.Max(0.0001f, bounds.size.z));
    }

    private void CreatePurchasedPen(int index, PenSlot slot)
    {
        Vector3 worldOffset = Vector3.right * slot.horizontalOffset;
        GameObject penRoot = new GameObject($"Pen {index + 1}");
        penRoot.transform.position = worldOffset;

        Transform terrain = CreateRuntimeTerrain(
            $"Terrain_Pens_{index + 1}",
            worldOffset,
            penRoot.transform);
        Material groundMaterial =
            ApplyWorldGroundMaterial(terrain);

        GameObject volume = Instantiate(
            volumeTemplate,
            volumeTemplate.transform.position + worldOffset,
            volumeTemplate.transform.rotation,
            penRoot.transform);
        volume.name = $"VolumePen_{index + 1}";
        DebugChickenSpawner clonedSpawner =
            volume.GetComponent<DebugChickenSpawner>();
        if (clonedSpawner != null)
        {
            // Awake has already built this pen's NavMesh. The original spawner
            // remains the sole owner of F5 performance spawning, while every
            // purchased pen receives its own small starter flock.
            clonedSpawner.SpawnStarterChickens(
                starterChickensPerPurchasedPen);
            clonedSpawner.enabled = false;
        }

        EggContainer container = Instantiate(
            containerTemplate,
            containerTemplate.transform.position + worldOffset,
            containerTemplate.transform.rotation,
            penRoot.transform);
        container.name = $"EggContainer_{index + 1}";
        container.SetFocused(false);

        IncubatorController incubator = CloneIncubator(
            index,
            worldOffset,
            penRoot.transform);
        CrosshatcherController crosshatcher = CloneCrosshatcher(
            index,
            worldOffset,
            penRoot.transform);

        PenTruckController truck = penRoot.AddComponent<PenTruckController>();
        truck.Configure(container, worldOffset);

        InteractiveGrassSystem grass = grassTemplate.CreateRuntimeCopy(
            grassTemplate.transform.position + worldOffset,
            grassTemplate.transform.rotation,
            penRoot.transform,
            $"Interactive Grass {index + 1}",
            terrain,
            worldOffset,
            groundMaterial,
            true);

        slot.runtimeRoot = penRoot;
        slot.terrain = terrain;
        slot.grass = grass;
        slot.eggContainer = container;
        slot.incubator = incubator;
        slot.crosshatcher = crosshatcher;
        slot.truck = truck;
    }

    private IncubatorController CloneIncubator(
        int index,
        Vector3 worldOffset,
        Transform parent)
    {
        if (incubatorTemplate == null)
        {
            return null;
        }

        IncubatorController clone = Instantiate(
            incubatorTemplate,
            incubatorTemplate.transform.position + worldOffset,
            incubatorTemplate.transform.rotation,
            parent);
        clone.name = $"Incubator_{index + 1}";
        SynchronizeIncubator(clone);
        return clone;
    }

    private CrosshatcherController CloneCrosshatcher(
        int index,
        Vector3 worldOffset,
        Transform parent)
    {
        if (crosshatcherTemplate == null)
        {
            return null;
        }

        CrosshatcherController clone = Instantiate(
            crosshatcherTemplate,
            crosshatcherTemplate.transform.position + worldOffset,
            crosshatcherTemplate.transform.rotation,
            parent);
        clone.name = $"Crosshatcher_{index + 1}";
        SynchronizeCrosshatcher(clone);
        return clone;
    }

    private void SynchronizeIncubator(IncubatorController target)
    {
        if (target == null || incubatorTemplate == null)
        {
            return;
        }

        bool installed = incubatorTemplate.gameObject.activeSelf;
        if (installed)
        {
            target.InstallOrUpgrade(
                incubatorTemplate.CapacityLevel,
                incubatorTemplate.SpeedLevel);
        }
        else
        {
            target.gameObject.SetActive(false);
        }
    }

    private void SynchronizeCrosshatcher(CrosshatcherController target)
    {
        if (target == null || crosshatcherTemplate == null)
        {
            return;
        }

        bool installed = crosshatcherTemplate.gameObject.activeSelf;
        if (installed)
        {
            target.InstallOrUpgrade(
                crosshatcherTemplate.SpeedLevel,
                crosshatcherTemplate.QualityLevel);
        }
        else
        {
            target.gameObject.SetActive(false);
        }
    }

    private void RefreshDistantVisuals()
    {
        float cameraX = transform.position.x;
        IReadOnlyList<ChickenController> chickens =
            ChickenController.ActiveInstances;
        for (int index = chickens.Count - 1; index >= 0; index--)
        {
            ChickenController chicken = chickens[index];
            if (chicken != null)
            {
                chicken.SetPenVisualsEnabled(
                    Mathf.Abs(chicken.transform.position.x - cameraX)
                    <= visualActivationDistance);
            }
        }

        IReadOnlyList<ChickenEgg> eggs = ChickenEgg.ActiveInstances;
        for (int index = eggs.Count - 1; index >= 0; index--)
        {
            ChickenEgg egg = eggs[index];
            if (egg != null)
            {
                egg.SetPenVisualsEnabled(
                    Mathf.Abs(egg.transform.position.x - cameraX)
                    <= visualActivationDistance);
            }
        }
    }

    private void CreateEdgeBuffers()
    {
        bufferRoot = new GameObject("Pen Edge Buffers");
        leftBuffer = CreateBuffer("Left", -penSpacing);
        rightBuffer = CreateBuffer("Right", penSpacing);
    }

    private PenBuffer CreateBuffer(string side, float horizontalOffset)
    {
        Vector3 worldOffset = Vector3.right * horizontalOffset;
        Transform terrain = CreateRuntimeTerrain(
            $"Terrain_Pens Buffer {side}",
            worldOffset,
            bufferRoot.transform);
        for (int index = 0; index < terrain.childCount; index++)
        {
            Transform surface = terrain.GetChild(index);
            surface.name =
                $"{surface.name.ToLowerInvariant()}_buffer_{side.ToLowerInvariant()}";
        }

        Material groundMaterial =
            ApplyWorldGroundMaterial(terrain);
        InteractiveGrassSystem grass = CreateRuntimeGrass(
            terrain,
            worldOffset,
            groundMaterial,
            $"Interactive Grass Buffer {side}",
            bufferRoot.transform);
        return new PenBuffer
        {
            terrain = terrain,
            groundMaterial = groundMaterial,
            grass = grass
        };
    }

    private InteractiveGrassSystem CreateRuntimeGrass(
        Transform terrain,
        Vector3 worldOffset,
        Material groundMaterial,
        string objectName,
        Transform parent)
    {
        return grassTemplate.CreateRuntimeCopy(
            grassTemplate.transform.position + worldOffset,
            grassTemplate.transform.rotation,
            parent,
            objectName,
            terrain,
            worldOffset,
            groundMaterial,
            true);
    }

    private Transform CreateRuntimeTerrain(
        string objectName,
        Vector3 worldOffset,
        Transform parent)
    {
        GameObject terrainObject = new GameObject(objectName);
        Transform terrain = terrainObject.transform;
        terrain.SetParent(parent, false);
        terrain.SetPositionAndRotation(
            terrainTemplate.position + worldOffset,
            terrainTemplate.rotation);
        terrain.localScale = terrainTemplate.localScale;

        for (int index = 0; index < terrainTemplate.childCount; index++)
        {
            CreateRuntimeSurface(terrainTemplate.GetChild(index), terrain);
        }

        return terrain;
    }

    private static void CreateRuntimeSurface(
        Transform sourceSurface,
        Transform parent)
    {
        MeshFilter sourceFilter = sourceSurface.GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer =
            sourceSurface.GetComponent<MeshRenderer>();
        MeshCollider sourceCollider =
            sourceSurface.GetComponent<MeshCollider>();
        Mesh sourceMesh = sourceCollider != null
            && sourceCollider.sharedMesh != null
                ? sourceCollider.sharedMesh
                : sourceFilter != null
                    ? sourceFilter.sharedMesh
                    : null;
        if (sourceFilter == null
            || sourceMesh == null
            || sourceRenderer == null)
        {
            return;
        }

        GameObject surfaceObject = new GameObject(sourceSurface.name);
        surfaceObject.layer = sourceSurface.gameObject.layer;
        Transform surface = surfaceObject.transform;
        surface.SetParent(parent, false);
        surface.localPosition = sourceSurface.localPosition;
        surface.localRotation = sourceSurface.localRotation;
        surface.localScale = sourceSurface.localScale;

        MeshFilter filter = surfaceObject.AddComponent<MeshFilter>();
        // ProBuilder can replace the render filter's transient mesh during
        // initialization. Its baked collider mesh remains aligned with the
        // authored transform, so use it for both visuals and collision.
        filter.sharedMesh = sourceMesh;

        MeshRenderer renderer = surfaceObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = sourceRenderer.sharedMaterials;
        renderer.enabled = true;
        renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        renderer.receiveShadows = sourceRenderer.receiveShadows;
        renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        renderer.motionVectorGenerationMode =
            sourceRenderer.motionVectorGenerationMode;
        renderer.allowOcclusionWhenDynamic =
            sourceRenderer.allowOcclusionWhenDynamic;
        renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        renderer.rendererPriority = sourceRenderer.rendererPriority;

        if (sourceCollider != null)
        {
            MeshCollider collider =
                surfaceObject.AddComponent<MeshCollider>();
            collider.sharedMesh = sourceMesh;
            collider.sharedMaterial = sourceCollider.sharedMaterial;
            collider.convex = sourceCollider.convex;
            collider.isTrigger = sourceCollider.isTrigger;
            collider.cookingOptions = sourceCollider.cookingOptions;
            collider.enabled = sourceCollider.enabled;
        }
    }

    private void RefreshEdgeBuffers()
    {
        if (leftBuffer == null || rightBuffer == null)
        {
            return;
        }

        int firstOwnedIndex = 0;
        int lastOwnedIndex = 0;
        for (int index = 0; index < slots.Count; index++)
        {
            if (!slots[index].owned)
            {
                continue;
            }

            firstOwnedIndex = Mathf.Min(firstOwnedIndex, index);
            lastOwnedIndex = Mathf.Max(lastOwnedIndex, index);
        }

        PositionBuffer(leftBuffer, (firstOwnedIndex - 1) * penSpacing);
        PositionBuffer(rightBuffer, (lastOwnedIndex + 1) * penSpacing);
    }

    private void PositionBuffer(PenBuffer buffer, float horizontalOffset)
    {
        Vector3 worldOffset = Vector3.right * horizontalOffset;
        buffer.terrain.SetPositionAndRotation(
            terrainTemplate.position + worldOffset,
            terrainTemplate.rotation);
        if (buffer.grass != null)
        {
            buffer.grass.transform.SetPositionAndRotation(
                grassTemplate.transform.position + worldOffset,
                grassTemplate.transform.rotation);
            buffer.grass.ConfigureRuntimePen(
                buffer.terrain,
                worldOffset,
                buffer.groundMaterial,
                true);
        }
    }

    private Material ApplyWorldGroundMaterial(Transform terrain)
    {
        Material source = grassTemplate.GroundColourSource;
        if (source == null)
        {
            return null;
        }

        if (worldGroundMaterial == null)
        {
            worldGroundMaterial = new Material(source)
            {
                name = $"{source.name} Continuous World"
            };
            runtimeGroundMaterials.Add(worldGroundMaterial);
        }

        Renderer[] renderers = terrain.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Material[] materials = renderers[rendererIndex].sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                if (materials[materialIndex] != null)
                {
                    materials[materialIndex] = worldGroundMaterial;
                }
            }

            renderers[rendererIndex].enabled = true;
            renderers[rendererIndex].sharedMaterials = materials;
        }

        return worldGroundMaterial;
    }

    private void RefreshWorldGroundCoverage()
    {
        if (worldGroundMaterial == null)
        {
            ApplyWorldGroundMaterial(terrainTemplate);
        }

        if (worldGroundMaterial == null)
        {
            return;
        }

        var terrains = new List<Transform>();
        var grassSystems = new List<InteractiveGrassSystem>();
        AddWorldGroundSource(terrainTemplate, grassTemplate, terrains, grassSystems);
        AddWorldGroundSource(
            leftBuffer != null ? leftBuffer.terrain : null,
            leftBuffer != null ? leftBuffer.grass : null,
            terrains,
            grassSystems);
        AddWorldGroundSource(
            rightBuffer != null ? rightBuffer.terrain : null,
            rightBuffer != null ? rightBuffer.grass : null,
            terrains,
            grassSystems);
        for (int index = 1; index < slots.Count; index++)
        {
            PenSlot slot = slots[index];
            if (slot.owned)
            {
                AddWorldGroundSource(
                    slot.terrain,
                    slot.grass,
                    terrains,
                    grassSystems);
            }
        }

        if (!TryGetCombinedTerrainBounds(terrains, out Bounds bounds))
        {
            return;
        }

        Vector4 worldRect = BoundsToWorldRect(bounds);
        int maskResolution = Mathf.Clamp(
            Mathf.NextPowerOfTwo(
                Mathf.CeilToInt(
                    Mathf.Max(worldRect.z, worldRect.w)
                    * RuntimeGroundMaskPixelsPerMetre)),
            256,
            MaximumRuntimeGroundMaskResolution);
        if (worldGroundMask != null)
        {
            runtimeGroundMasks.Remove(worldGroundMask);
            Destroy(worldGroundMask);
        }

        worldGroundMask = grassTemplate.CreateRuntimeGroundMask(
            worldRect,
            maskResolution,
            false,
            grassSystems);
        worldGroundMask.name = "Continuous World Ground Mask";
        runtimeGroundMasks.Add(worldGroundMask);
        worldGroundMaterial.SetTexture("_LayerMask", worldGroundMask);
        worldGroundMaterial.SetTexture("_OuterLayerMask", worldGroundMask);
        worldGroundMaterial.SetVector("_MaskWorldRect", worldRect);
        worldGroundMaterial.SetVector("_OuterMaskWorldRect", worldRect);
        worldGroundMaterial.SetFloat("_PlacedCoverageAvailable", 1f);
        worldGroundMaterial.SetFloat("_OuterPlacedCoverageAvailable", 1f);

        for (int index = 0; index < terrains.Count; index++)
        {
            ApplyWorldGroundMaterial(terrains[index]);
        }
    }

    private static void AddWorldGroundSource(
        Transform terrain,
        InteractiveGrassSystem grass,
        List<Transform> terrains,
        List<InteractiveGrassSystem> grassSystems)
    {
        if (terrain != null)
        {
            terrains.Add(terrain);
        }

        if (grass != null)
        {
            grassSystems.Add(grass);
        }
    }

    private static bool TryGetCombinedTerrainBounds(
        List<Transform> terrains,
        out Bounds combined)
    {
        combined = default;
        bool hasBounds = false;
        for (int terrainIndex = 0; terrainIndex < terrains.Count; terrainIndex++)
        {
            Renderer[] renderers = terrains[terrainIndex]
                .GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                if (!hasBounds)
                {
                    combined = renderers[rendererIndex].bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderers[rendererIndex].bounds);
                }
            }
        }

        return hasBounds;
    }

    private static void SetStaticRecursively(Transform root, bool isStatic)
    {
        root.gameObject.isStatic = isStatic;
        for (int index = 0; index < root.childCount; index++)
        {
            SetStaticRecursively(root.GetChild(index), isStatic);
        }
    }

    private bool IsValidIndex(int index)
    {
        return IsInitialized && index >= 0 && index < slots.Count;
    }

    private void OnValidate()
    {
        penCount = Mathf.Max(2, penCount);
        penSpacing = Mathf.Max(0.1f, penSpacing);
        additionalPenSpacing = Mathf.Max(0f, additionalPenSpacing);
        starterChickensPerPurchasedPen = Mathf.Max(
            0,
            starterChickensPerPurchasedPen);
        firstAdditionalPenCostCents = Mathf.Max(1, firstAdditionalPenCostCents);
        penCostMultiplier = Mathf.Max(1f, penCostMultiplier);
        visualActivationDistance = Mathf.Max(1f, visualActivationDistance);
        visualRefreshInterval = Mathf.Max(0.1f, visualRefreshInterval);
    }
}

[DisallowMultipleComponent]
internal sealed class PenTruckController : MonoBehaviour
{
    private EggContainer container;
    private Vector3 penOffset;
    private Transform truck;
    private int eggsTowardTruck;
    private int pendingReplacements;
    private Coroutine replacement;

    public int EggsTowardTruck => eggsTowardTruck;

    public void Configure(EggContainer targetContainer, Vector3 offset)
    {
        container = targetContainer;
        penOffset = offset;
        if (RoundSystem.Instance != null
            && RoundSystem.Instance.IsRoundInProgress)
        {
            eggsTowardTruck = 0;
            pendingReplacements = 0;
            SpawnTruckAtStop();
        }
    }

    private void OnEnable()
    {
        EggContainer.EggCollectedFromContainer += HandleEggCollected;
        RoundSystem.RoundStarted += HandleRoundStarted;
        RoundSystem.RoundEnded += HandleRoundEnded;
    }

    private void OnDisable()
    {
        EggContainer.EggCollectedFromContainer -= HandleEggCollected;
        RoundSystem.RoundStarted -= HandleRoundStarted;
        RoundSystem.RoundEnded -= HandleRoundEnded;
    }

    private void HandleRoundStarted(int _)
    {
        eggsTowardTruck = 0;
        pendingReplacements = 0;
        SpawnTruckAtStop();
    }

    private void HandleRoundEnded(int _)
    {
        eggsTowardTruck = 0;
        pendingReplacements = 0;
        if (replacement != null)
        {
            StopCoroutine(replacement);
            replacement = null;
        }

        DestroyTruck();
    }

    private void HandleEggCollected(EggContainer source, int _)
    {
        RoundSystem round = RoundSystem.Instance;
        if (source != container
            || round == null
            || !round.IsRoundInProgress
            || round.EggTarget <= 0)
        {
            return;
        }

        eggsTowardTruck++;
        while (eggsTowardTruck >= round.EggTarget)
        {
            eggsTowardTruck -= round.EggTarget;
            round.CompleteAdditionalPenTruckQuota(
                truck != null
                    ? truck.position
                    : GetStopPosition());
            pendingReplacements++;
            if (replacement == null)
            {
                replacement = StartCoroutine(ReplaceTruck());
            }
        }

        round.NotifyPenTruckProgressChanged();
    }

    private System.Collections.IEnumerator ReplaceTruck()
    {
        while (pendingReplacements > 0
            && RoundSystem.Instance != null
            && RoundSystem.Instance.IsRoundInProgress)
        {
            pendingReplacements--;
            if (truck != null)
            {
                yield return MoveTruck(
                    GetStopPosition() + Vector3.right * 7f,
                    0.6f);
            }

            DestroyTruck();
            if (RoundSystem.Instance == null
                || !RoundSystem.Instance.IsRoundInProgress)
            {
                break;
            }

            SpawnTruck(GetStopPosition() - Vector3.right * 8f);
            yield return MoveTruck(GetStopPosition(), 0.6f);
        }

        replacement = null;
    }

    private System.Collections.IEnumerator MoveTruck(
        Vector3 destination,
        float duration)
    {
        if (truck == null)
        {
            yield break;
        }

        Vector3 start = truck.position;
        float elapsed = 0f;
        while (truck != null && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(elapsed / duration));
            truck.position = Vector3.Lerp(start, destination, progress);
            yield return null;
        }

        if (truck != null)
        {
            truck.position = destination;
        }
    }

    private void SpawnTruckAtStop()
    {
        DestroyTruck();
        SpawnTruck(GetStopPosition());
    }

    private void SpawnTruck(Vector3 position)
    {
        GameObject root = new GameObject("Pen Delivery Truck");
        truck = root.transform;
        truck.SetParent(transform, true);
        truck.SetPositionAndRotation(
            position,
            Quaternion.Euler(0f, 90f, 0f));
        truck.localScale = Vector3.one * 0.22f;

        Material body = CreateMaterial(new Color(0.92f, 0.18f, 0.11f));
        Material dark = CreateMaterial(new Color(0.16f, 0.17f, 0.18f));
        CreatePart("Chassis", new Vector3(0f, 0.55f, 0f),
            new Vector3(1.55f, 0.45f, 3.35f), body);
        CreatePart("Cab", new Vector3(0f, 1.18f, 0.82f),
            new Vector3(1.5f, 1.05f, 1.42f), body);
        CreatePart("Tray", new Vector3(0f, 0.95f, -0.88f),
            new Vector3(1.48f, 0.3f, 1.55f), dark);
    }

    private void CreatePart(
        string partName,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.transform.SetParent(truck, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
    }

    private static Material CreateMaterial(Color colour)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        return new Material(shader) { color = colour };
    }

    private Vector3 GetStopPosition()
    {
        GameObject marker = GameObject.Find("truck_stop");
        Vector3 basePosition = marker != null
            ? marker.transform.position
            : new Vector3(0f, 0f, -3.5f);
        return basePosition + penOffset;
    }

    private void DestroyTruck()
    {
        if (truck == null)
        {
            return;
        }

        Renderer[] renderers = truck.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            if (renderers[index] != null
                && renderers[index].sharedMaterial != null)
            {
                Destroy(renderers[index].sharedMaterial);
            }
        }

        Destroy(truck.gameObject);
        truck = null;
    }
}
