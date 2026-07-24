using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DefaultExecutionOrder(500)]
[DisallowMultipleComponent]
public sealed class InteractiveGrassSystem : MonoBehaviour
{
    private const int MaximumInstancesPerDraw = 1023;
    private static readonly int GrassBendId = Shader.PropertyToID("_GrassBend");
    private static readonly int GrassVariationId = Shader.PropertyToID("_GrassVariation");

    private sealed class GrassInstance
    {
        public Vector3 position;
        public Quaternion rotation;
        public Matrix4x4 matrix;
        public Vector2 interactionBend;
        public Vector2 interactionBendVelocity;
        public float flatten;
        public float flattenVelocity;
        public Vector4 variation;
        public Vector3 windSampleOffset;
        public float interactionWeight = 1f;
        public bool isOuter;
    }

    private sealed class DrawBatch
    {
        public GrassInstance[] instances;
        public Matrix4x4[] matrices;
        public Vector4[] bends;
        public Vector4[] variations;
        public MaterialPropertyBlock properties;
    }

    [Header("Rendering")]
    [SerializeField] private GameObject grassClumpPrefab = null;
    [SerializeField] private Mesh grassClumpMesh = null;
    [SerializeField] private Material grassMaterial = null;
    [SerializeField] private Material groundColourSource = null;
    [SerializeField] private bool castShadows = false;

    [Header("Ground Layer Mask")]
    [SerializeField, Range(64, 2048)] private int groundMaskResolution = 512;
    [SerializeField, Range(64, 2048)] private int outerGroundMaskResolution = 1024;

    [Header("Placement Area")]
    [SerializeField] private Vector2 areaSize = new Vector2(3.2f, 3.2f);
    [SerializeField, Min(1f)] private float densityPerSquareMetre = 140f;
    [SerializeField, Range(0.2f, 1.5f)] private float spacingMultiplier = 0.7f;
    [SerializeField] private int randomSeed = 4187;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField, Min(0f)] private float groundOffset = 0.002f;

    [Header("Outer Extension")]
    [Tooltip("Adds grass outside the protected inner placement area without changing its placement.")]
    [SerializeField] private bool extendIntoOuterArea = true;
    [Tooltip("Local-space centre of the complete outer ground rectangle.")]
    [SerializeField] private Vector2 outerAreaCenter = new Vector2(0f, 2.5f);
    [Tooltip("Local-space size of the complete outer ground rectangle.")]
    [SerializeField] private Vector2 outerAreaSize = new Vector2(12f, 10f);
    [Tooltip("Placement density outside the pen as a proportion of the existing inner density.")]
    [SerializeField, Range(0.01f, 1f)] private float outerDensityMultiplier = 0.35f;
    [Tooltip("Distance outside the pen over which character interaction fades.")]
    [SerializeField, Min(0.01f)] private float outerInteractionFadeDistance = 5f;
    [Tooltip("Interaction retained at and beyond the outer fade distance. Wind is unaffected.")]
    [SerializeField, Range(0f, 1f)] private float minimumOuterInteraction = 0.15f;

    [Header("Natural Meadow Coverage")]
    [Tooltip("Approximate proportion of the area occupied by the broad grass bed.")]
    [SerializeField, Range(0.75f, 0.98f)] private float targetGrassCoverage = 0.88f;
    [Tooltip("World-space frequency of the large dirt pockets. Lower values make fewer, larger regions.")]
    [SerializeField, Min(0.01f)] private float dirtPatchScale = 0.38f;
    [Tooltip("Width of the partially covered rim between solid grass and solid dirt.")]
    [SerializeField, Range(0.02f, 0.3f)] private float dirtEdgeWidth = 0.12f;
    [Tooltip("Fine distortion applied only near dirt boundaries.")]
    [SerializeField, Range(0f, 0.4f)] private float dirtEdgeBreakup = 0.16f;
    [SerializeField, Min(0f)] private float domainWarpStrength = 0.42f;

    [Header("Clump Density Variation")]
    [SerializeField, Min(0.01f)] private float clumpScale = 2.4f;
    [SerializeField, Range(0f, 1f)] private float clumpVariation = 0.45f;

    [Header("Patch Edge Taper")]
    [InspectorName("Minimum Edge Uniform Scale")]
    [SerializeField, Range(0.05f, 1f)] private float minimumEdgeHeightScale = 0.35f;
    [SerializeField, Range(0.05f, 1f)] private float minimumEdgeWidthScale = 0.55f;
    [SerializeField, Min(0.05f)] private float edgeTaperPower = 0.85f;

    [Header("Instance Scale Variation")]
    [InspectorName("Minimum Uniform Scale")]
    [SerializeField, Min(0.01f)] private float minimumHeight = 0.075f;
    [InspectorName("Maximum Uniform Scale")]
    [SerializeField, Min(0.01f)] private float maximumHeight = 0.15f;
    [Tooltip("Additional horizontal multiplier applied after uniform scale. One preserves the authored proportions.")]
    [SerializeField, Min(0.01f)] private float minimumWidthScale = 0.8f;
    [Tooltip("Additional horizontal multiplier applied after uniform scale. One preserves the authored proportions.")]
    [SerializeField, Min(0.01f)] private float maximumWidthScale = 1.25f;

    [Header("Interaction")]
    [SerializeField, Min(0f)] private float interactionBendDistance = 0.75f;
    [SerializeField, Min(0.01f)] private float flattenResponseTime = 0.055f;
    [SerializeField, Min(0.01f)] private float flattenRecoveryTime = 1.15f;
    [SerializeField, Min(0.01f)] private float bendResponseTime = 0.06f;
    [SerializeField, Min(0.01f)] private float bendRecoveryTime = 0.55f;

    [Header("Wind Response")]
    [SerializeField, Min(0f)] private float steadyWindBend = 0.05f;
    [SerializeField, Min(0f)] private float turbulentWindBend = 0.5f;
    [SerializeField, Min(0f)] private float turbulenceDeadZone = 0.012f;
    [SerializeField, Min(0f)] private float maximumWindBend = 0.42f;
    [SerializeField, Range(0f, 1f)] private float minimumWindResponse = 0.7f;
    [SerializeField, Range(0f, 2f)] private float maximumWindResponse = 1f;
    [SerializeField, Min(0f)] private float windNoiseOffsetDistance = 0.2f;

    private readonly RaycastHit[] groundHits = new RaycastHit[16];
    private DrawBatch[] batches = Array.Empty<DrawBatch>();
    private Mesh generatedPlaceholderMesh;
    private Material generatedFallbackMaterial;
    private Mesh activeMesh;
    private Material activeMaterial;
    private bool regenerationRequested;
    private int clumpPrefabSignature;

    public int InstanceCount { get; private set; }
    public Vector2 AreaSize => areaSize;
    public int GroundMaskResolution => groundMaskResolution;
    public int OuterGroundMaskResolution => outerGroundMaskResolution;
    public Material GroundColourSource => groundColourSource;
    public Vector2 OuterAreaCenter => outerAreaCenter;
    public Vector2 OuterAreaSize => outerAreaSize;
    public bool ExtendsIntoOuterArea => extendIntoOuterArea;

    private void OnEnable()
    {
        GenerateGrass();
    }

    private void Update()
    {
        if (!Application.isPlaying && GetClumpPrefabSignature() != clumpPrefabSignature)
        {
            regenerationRequested = true;
        }

        if (regenerationRequested)
        {
            regenerationRequested = false;
            GenerateGrass();
        }

        if (Application.isPlaying)
        {
            UpdateInteractionState();
        }
        else
        {
            ResetInteractionState();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }
    }

    private void LateUpdate()
    {
        DrawGrass();
    }

    [ContextMenu("Regenerate Grass")]
    public void GenerateGrass()
    {
        ReleaseGeneratedAssets();
        activeMesh = null;
        activeMaterial = grassMaterial;

        if (grassClumpPrefab != null)
        {
            generatedPlaceholderMesh = CreateCombinedClumpMesh(grassClumpPrefab);
            activeMesh = generatedPlaceholderMesh;
        }
        else if (grassClumpMesh != null)
        {
            activeMesh = grassClumpMesh;
        }
        else
        {
            generatedPlaceholderMesh = CreatePlaceholderClump();
            activeMesh = generatedPlaceholderMesh;
        }

        if (activeMaterial == null)
        {
            Shader shader = Shader.Find("Eggcessive/Interactive Grass");
            if (shader != null)
            {
                generatedFallbackMaterial = new Material(shader)
                {
                    name = "Generated Interactive Grass Material",
                    enableInstancing = true
                };
                activeMaterial = generatedFallbackMaterial;
            }
        }

        if (activeMesh == null || activeMaterial == null)
        {
            batches = Array.Empty<DrawBatch>();
            InstanceCount = 0;
            Debug.LogWarning($"{nameof(InteractiveGrassSystem)} requires a grass mesh and material.", this);
            return;
        }

        activeMaterial.enableInstancing = true;
        List<GrassInstance> instances = GeneratePlacement();
        InstanceCount = instances.Count;
        BuildBatches(instances);
        clumpPrefabSignature = GetClumpPrefabSignature();
    }

    private List<GrassInstance> GeneratePlacement()
    {
        float area = areaSize.x * areaSize.y;
        int targetCount = Mathf.Max(1, Mathf.RoundToInt(area * densityPerSquareMetre));
        float minimumSpacing = Mathf.Sqrt(1f / densityPerSquareMetre) * spacingMultiplier;
        float cellSize = Mathf.Max(minimumSpacing, 0.001f);
        int maximumAttempts = targetCount * 80;
        var random = new System.Random(randomSeed);
        var accepted = new List<Vector2>(targetCount);
        var grid = new Dictionary<Vector2Int, List<Vector2>>();
        var instances = new List<GrassInstance>(targetCount);

        for (int attempt = 0; attempt < maximumAttempts && accepted.Count < targetCount; attempt++)
        {
            Vector2 point = new Vector2(
                Mathf.Lerp(-areaSize.x * 0.5f, areaSize.x * 0.5f, NextFloat(random)),
                Mathf.Lerp(-areaSize.y * 0.5f, areaSize.y * 0.5f, NextFloat(random)));

            Vector3 worldCandidate = transform.TransformPoint(new Vector3(point.x, 0f, point.y));
            EvaluateGrassDistribution(
                worldCandidate,
                out float patchWeight,
                out float clumpWeight,
                out float acceptance);
            if (NextFloat(random) > acceptance || HasCloseNeighbour(point, grid, cellSize, minimumSpacing))
            {
                continue;
            }

            Vector2Int cell = GetCell(point, cellSize);
            if (!grid.TryGetValue(cell, out List<Vector2> cellPoints))
            {
                cellPoints = new List<Vector2>();
                grid.Add(cell, cellPoints);
            }

            cellPoints.Add(point);
            accepted.Add(point);

            Vector3 position = worldCandidate;
            Vector3 normal = Vector3.up;
            TryPlaceOnGround(ref position, ref normal);
            position += normal * groundOffset;

            float yaw = NextFloat(random) * 360f;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal)
                * Quaternion.AngleAxis(yaw, Vector3.up);
            float edgeWeight = Mathf.Pow(
                Mathf.Clamp01(patchWeight * Mathf.Lerp(0.5f, 1f, clumpWeight)),
                edgeTaperPower);
            float scaleTaper = Mathf.Lerp(minimumEdgeHeightScale, 1f, edgeWeight);
            float widthTaper = Mathf.Lerp(minimumEdgeWidthScale, 1f, edgeWeight);
            float uniformScale = Mathf.Lerp(minimumHeight, maximumHeight, NextFloat(random))
                * scaleTaper;
            float width = Mathf.Lerp(minimumWidthScale, maximumWidthScale, NextFloat(random))
                * widthTaper;

            float colorVariation = NextFloat(random);
            float windVariation = NextFloat(random);
            float windOffsetAngle = NextFloat(random) * Mathf.PI * 2f;
            float windOffsetRadius = Mathf.Sqrt(NextFloat(random)) * windNoiseOffsetDistance;
            instances.Add(new GrassInstance
            {
                position = position,
                rotation = rotation,
                matrix = Matrix4x4.TRS(
                    position,
                    rotation,
                    new Vector3(
                        uniformScale * width,
                        uniformScale,
                        uniformScale * width)),
                variation = new Vector4(colorVariation, windVariation, uniformScale, 1f),
                windSampleOffset = new Vector3(
                    Mathf.Cos(windOffsetAngle) * windOffsetRadius,
                    0f,
                    Mathf.Sin(windOffsetAngle) * windOffsetRadius)
            });
        }

        AddOuterPlacement(instances);
        return instances;
    }

    private void AddOuterPlacement(List<GrassInstance> instances)
    {
        if (!extendIntoOuterArea)
        {
            return;
        }

        float outerArea = outerAreaSize.x * outerAreaSize.y;
        float innerArea = areaSize.x * areaSize.y;
        float extensionArea = Mathf.Max(0f, outerArea - innerArea);
        float outerDensity = densityPerSquareMetre * outerDensityMultiplier;
        int targetCount = Mathf.Max(0, Mathf.RoundToInt(extensionArea * outerDensity));
        if (targetCount == 0)
        {
            return;
        }

        float minimumSpacing = Mathf.Sqrt(1f / outerDensity) * spacingMultiplier;
        float cellSize = Mathf.Max(minimumSpacing, 0.001f);
        int maximumAttempts = targetCount * 80;
        var random = new System.Random(randomSeed ^ 0x5f3759df);
        var accepted = new List<Vector2>(targetCount);
        var grid = new Dictionary<Vector2Int, List<Vector2>>();

        for (int attempt = 0; attempt < maximumAttempts && accepted.Count < targetCount; attempt++)
        {
            Vector2 point = new Vector2(
                outerAreaCenter.x
                    + Mathf.Lerp(-outerAreaSize.x * 0.5f, outerAreaSize.x * 0.5f, NextFloat(random)),
                outerAreaCenter.y
                    + Mathf.Lerp(-outerAreaSize.y * 0.5f, outerAreaSize.y * 0.5f, NextFloat(random)));
            float distanceOutsidePen = GetDistanceOutsideInnerArea(point);
            if (distanceOutsidePen <= 0f)
            {
                continue;
            }

            Vector3 worldCandidate = transform.TransformPoint(new Vector3(point.x, 0f, point.y));
            EvaluateGrassDistribution(
                worldCandidate,
                out float patchWeight,
                out float clumpWeight,
                out float acceptance);
            if (NextFloat(random) > acceptance
                || HasCloseNeighbour(point, grid, cellSize, minimumSpacing))
            {
                continue;
            }

            Vector3 position = worldCandidate;
            Vector3 normal = Vector3.up;
            if (!TryPlaceOuterOnGround(ref position, ref normal))
            {
                continue;
            }

            Vector2Int cell = GetCell(point, cellSize);
            if (!grid.TryGetValue(cell, out List<Vector2> cellPoints))
            {
                cellPoints = new List<Vector2>();
                grid.Add(cell, cellPoints);
            }

            cellPoints.Add(point);
            accepted.Add(point);
            position += normal * groundOffset;

            float yaw = NextFloat(random) * 360f;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal)
                * Quaternion.AngleAxis(yaw, Vector3.up);
            float edgeWeight = Mathf.Pow(
                Mathf.Clamp01(patchWeight * Mathf.Lerp(0.5f, 1f, clumpWeight)),
                edgeTaperPower);
            float scaleTaper = Mathf.Lerp(minimumEdgeHeightScale, 1f, edgeWeight);
            float widthTaper = Mathf.Lerp(minimumEdgeWidthScale, 1f, edgeWeight);
            float uniformScale = Mathf.Lerp(minimumHeight, maximumHeight, NextFloat(random))
                * scaleTaper;
            float width = Mathf.Lerp(minimumWidthScale, maximumWidthScale, NextFloat(random))
                * widthTaper;
            float colorVariation = NextFloat(random);
            float windVariation = NextFloat(random);
            float windOffsetAngle = NextFloat(random) * Mathf.PI * 2f;
            float windOffsetRadius = Mathf.Sqrt(NextFloat(random)) * windNoiseOffsetDistance;
            float attenuation = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(distanceOutsidePen / outerInteractionFadeDistance));

            instances.Add(new GrassInstance
            {
                position = position,
                rotation = rotation,
                matrix = Matrix4x4.TRS(
                    position,
                    rotation,
                    new Vector3(
                        uniformScale * width,
                        uniformScale,
                        uniformScale * width)),
                variation = new Vector4(colorVariation, windVariation, uniformScale, 1f),
                windSampleOffset = new Vector3(
                    Mathf.Cos(windOffsetAngle) * windOffsetRadius,
                    0f,
                    Mathf.Sin(windOffsetAngle) * windOffsetRadius),
                interactionWeight = Mathf.Lerp(1f, minimumOuterInteraction, attenuation),
                isOuter = true
            });
        }
    }

    private float GetDistanceOutsideInnerArea(Vector2 localPoint)
    {
        Vector2 outside = new Vector2(
            Mathf.Max(0f, Mathf.Abs(localPoint.x) - areaSize.x * 0.5f),
            Mathf.Max(0f, Mathf.Abs(localPoint.y) - areaSize.y * 0.5f));
        return outside.magnitude;
    }

    public float EvaluateGrassDistribution(Vector3 worldPosition)
    {
        EvaluateGrassDistribution(
            worldPosition,
            out _,
            out _,
            out float distribution);
        return distribution;
    }

    public float EvaluateGroundGrassCoverage(Vector3 worldPosition)
    {
        EvaluateGrassDistribution(
            worldPosition,
            out float coverage,
            out _,
            out _);
        return coverage;
    }

    public float EvaluateGrassTransitionNoise(Vector3 worldPosition)
    {
        float clumpNoise = Mathf.PerlinNoise(
            worldPosition.x * clumpScale - randomSeed * 0.021f,
            worldPosition.z * clumpScale + randomSeed * 0.017f);
        float scatterNoise = Mathf.PerlinNoise(
            worldPosition.x * clumpScale * 2.37f + randomSeed * 0.031f,
            worldPosition.z * clumpScale * 2.37f - randomSeed * 0.027f);
        return Mathf.Clamp01(clumpNoise * 0.62f + scatterNoise * 0.38f);
    }

    public void GetGroundCoverageDiscs(List<Vector4> coverageDiscs)
    {
        GetGroundCoverageDiscs(coverageDiscs, false);
    }

    public void GetOuterGroundCoverageDiscs(List<Vector4> coverageDiscs)
    {
        GetGroundCoverageDiscs(coverageDiscs, true);
    }

    private void GetGroundCoverageDiscs(List<Vector4> coverageDiscs, bool collectOuter)
    {
        if (coverageDiscs == null)
        {
            throw new ArgumentNullException(nameof(coverageDiscs));
        }

        coverageDiscs.Clear();
        for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
        {
            GrassInstance[] instances = batches[batchIndex].instances;
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i].isOuter == collectOuter)
                {
                    AddGroundCoverageDisc(instances[i], coverageDiscs);
                }
            }
        }

        // Automatic editor setup can run before ExecuteAlways has retained its
        // draw batches. Recreate the same deterministic placement rather than
        // silently writing an empty placed-coverage channel.
        if (coverageDiscs.Count == 0 && isActiveAndEnabled)
        {
            List<GrassInstance> deterministicPlacement = GeneratePlacement();
            for (int i = 0; i < deterministicPlacement.Count; i++)
            {
                if (deterministicPlacement[i].isOuter == collectOuter)
                {
                    AddGroundCoverageDisc(deterministicPlacement[i], coverageDiscs);
                }
            }
        }
    }

    private static void AddGroundCoverageDisc(
        GrassInstance instance,
        List<Vector4> coverageDiscs)
    {
        Vector4 scaleX = instance.matrix.GetColumn(0);
        Vector4 scaleZ = instance.matrix.GetColumn(2);
        float footprintRadius = Mathf.Max(
            new Vector3(scaleX.x, scaleX.y, scaleX.z).magnitude,
            new Vector3(scaleZ.x, scaleZ.y, scaleZ.z).magnitude);
        coverageDiscs.Add(new Vector4(
            instance.position.x,
            instance.position.z,
            Mathf.Max(0.01f, footprintRadius),
            1f));
    }

    private void EvaluateGrassDistribution(
        Vector3 worldPosition,
        out float patchWeight,
        out float clumpWeight,
        out float distribution)
    {
        float warpFrequency = dirtPatchScale * 0.55f;
        float warpX = Mathf.PerlinNoise(
            worldPosition.x * warpFrequency + randomSeed * 0.041f,
            worldPosition.z * warpFrequency - randomSeed * 0.033f) - 0.5f;
        float warpZ = Mathf.PerlinNoise(
            worldPosition.x * warpFrequency - randomSeed * 0.037f,
            worldPosition.z * warpFrequency + randomSeed * 0.047f) - 0.5f;
        float warpedX = worldPosition.x + warpX * domainWarpStrength;
        float warpedZ = worldPosition.z + warpZ * domainWarpStrength;

        // A dominant low octave produces a small number of broad dirt pockets;
        // the secondary octave prevents perfectly round Perlin islands without
        // splitting them into many similarly sized spots.
        float broadDirtNoise = Mathf.PerlinNoise(
            warpedX * dirtPatchScale + randomSeed * 0.0131f,
            warpedZ * dirtPatchScale - randomSeed * 0.0097f);
        float shapeNoise = Mathf.PerlinNoise(
            warpedX * dirtPatchScale * 1.83f - randomSeed * 0.015f,
            warpedZ * dirtPatchScale * 1.83f + randomSeed * 0.019f);
        float dirtSignal = broadDirtNoise * 0.82f + shapeNoise * 0.18f;

        float coverageRange = Mathf.InverseLerp(0.75f, 0.98f, targetGrassCoverage);
        float dirtThreshold = Mathf.Lerp(0.52f, 0.70f, coverageRange);
        float distanceFromBoundary = Mathf.Abs(dirtSignal - dirtThreshold);
        float boundaryInfluence = 1f - Mathf.SmoothStep(
            dirtEdgeWidth,
            dirtEdgeWidth * 2.5f,
            distanceFromBoundary);
        float edgeNoise = Mathf.PerlinNoise(
            warpedX * clumpScale * 1.91f + randomSeed * 0.083f,
            warpedZ * clumpScale * 1.91f - randomSeed * 0.079f) - 0.5f;
        dirtSignal += edgeNoise * dirtEdgeBreakup * boundaryInfluence;

        float dirtWeight = Mathf.SmoothStep(
            0f,
            1f,
            Mathf.InverseLerp(
                dirtThreshold - dirtEdgeWidth * 0.5f,
                dirtThreshold + dirtEdgeWidth * 0.5f,
                dirtSignal));
        patchWeight = 1f - dirtWeight;

        float detailNoise = Mathf.PerlinNoise(
            warpedX * clumpScale - randomSeed * 0.021f,
            warpedZ * clumpScale + randomSeed * 0.017f);
        clumpWeight = Mathf.Lerp(1f, Mathf.Lerp(0.72f, 1f, detailNoise), clumpVariation);
        distribution = Mathf.Clamp01(patchWeight * clumpWeight);
    }

    private void UpdateInteractionState()
    {
        float deltaTime = Mathf.Min(Time.deltaTime, 1f / 20f);
        IReadOnlyList<GrassInteractor> interactors = GrassInteractor.ActiveInstances;

        for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
        {
            DrawBatch batch = batches[batchIndex];
            for (int i = 0; i < batch.instances.Length; i++)
            {
                GrassInstance instance = batch.instances[i];
                Vector2 targetBend = Vector2.zero;
                float targetFlatten = 0f;
                Vector2 grassPosition = new Vector2(instance.position.x, instance.position.z);

                for (int interactorIndex = 0; interactorIndex < interactors.Count; interactorIndex++)
                {
                    GrassInteractor interactor = interactors[interactorIndex];
                    if (interactor == null || !interactor.isActiveAndEnabled)
                    {
                        continue;
                    }

                    Vector3 interactorPosition3D = interactor.Position;
                    Vector2 interactorPosition = new Vector2(interactorPosition3D.x, interactorPosition3D.z);
                    Vector2 away = grassPosition - interactorPosition;
                    float radius = interactor.WorldRadius;
                    float distance = away.magnitude;
                    if (distance >= radius)
                    {
                        continue;
                    }

                    float falloff = 1f - distance / Mathf.Max(radius, 0.0001f);
                    falloff = Mathf.Pow(falloff, interactor.FalloffPower);
                    Vector2 radialDirection = distance > 0.0001f ? away / distance : Vector2.up;
                    Vector3 velocity3D = interactor.PlanarVelocity;
                    Vector2 velocity = new Vector2(velocity3D.x, velocity3D.z);
                    float velocityWeight = Mathf.Clamp01(velocity.magnitude / 0.35f)
                        * interactor.VelocityDirectionInfluence;
                    Vector2 pushDirection = Vector2.Lerp(
                        radialDirection,
                        velocity.sqrMagnitude > 0.0001f ? velocity.normalized : radialDirection,
                        velocityWeight).normalized;

                    targetBend += pushDirection
                        * (interactor.BendStrength * falloff * instance.interactionWeight);
                    targetFlatten = Mathf.Max(
                        targetFlatten,
                        interactor.FlattenStrength * falloff * instance.interactionWeight);
                }

                targetBend = Vector2.ClampMagnitude(targetBend, 1f);
                float activeBendTime = targetBend.sqrMagnitude > instance.interactionBend.sqrMagnitude
                    ? bendResponseTime
                    : bendRecoveryTime;
                instance.interactionBend = Vector2.SmoothDamp(
                    instance.interactionBend,
                    targetBend,
                    ref instance.interactionBendVelocity,
                    activeBendTime,
                    Mathf.Infinity,
                    deltaTime);
                float activeFlattenTime = targetFlatten > instance.flatten
                    ? flattenResponseTime
                    : flattenRecoveryTime;
                instance.flatten = Mathf.SmoothDamp(
                    instance.flatten,
                    targetFlatten,
                    ref instance.flattenVelocity,
                    activeFlattenTime,
                    Mathf.Infinity,
                    deltaTime);

                GlobalWind.WindSample wind = GlobalWind.SampleWindDetailed(
                    instance.position + instance.windSampleOffset);
                Vector3 dynamicWind = wind.gust + wind.turbulence;
                Vector2 dynamicPlanarWind = new Vector2(dynamicWind.x, dynamicWind.z);
                float dynamicMagnitude = dynamicPlanarWind.magnitude;
                if (dynamicMagnitude <= turbulenceDeadZone)
                {
                    dynamicPlanarWind = Vector2.zero;
                }
                else
                {
                    dynamicPlanarWind *= (dynamicMagnitude - turbulenceDeadZone) / dynamicMagnitude;
                }

                float windResponse = Mathf.Lerp(
                    minimumWindResponse,
                    maximumWindResponse,
                    instance.variation.y);
                Vector2 windBend = new Vector2(wind.steady.x, wind.steady.z) * steadyWindBend
                    + dynamicPlanarWind * turbulentWindBend;
                windBend *= windResponse;
                windBend = Vector2.ClampMagnitude(windBend, maximumWindBend);
                Vector2 worldBend = instance.interactionBend * interactionBendDistance + windBend;
                worldBend = Vector2.ClampMagnitude(worldBend, 0.95f);

                Vector3 localBend = Quaternion.Inverse(instance.rotation)
                    * new Vector3(worldBend.x, 0f, worldBend.y);
                batch.bends[i] = new Vector4(localBend.x, localBend.z, instance.flatten, 0f);
            }
        }
    }

    private void ResetInteractionState()
    {
        for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
        {
            DrawBatch batch = batches[batchIndex];
            for (int i = 0; i < batch.instances.Length; i++)
            {
                batch.instances[i].interactionBend = Vector2.zero;
                batch.instances[i].interactionBendVelocity = Vector2.zero;
                batch.instances[i].flatten = 0f;
                batch.instances[i].flattenVelocity = 0f;
                batch.bends[i] = Vector4.zero;
            }
        }
    }

    private void DrawGrass()
    {
        if (activeMesh == null || activeMaterial == null)
        {
            return;
        }

        ShadowCastingMode shadowMode = castShadows
            ? ShadowCastingMode.On
            : ShadowCastingMode.Off;
        for (int i = 0; i < batches.Length; i++)
        {
            DrawBatch batch = batches[i];
            batch.properties.SetVectorArray(GrassBendId, batch.bends);
            Graphics.DrawMeshInstanced(
                activeMesh,
                0,
                activeMaterial,
                batch.matrices,
                batch.matrices.Length,
                batch.properties,
                shadowMode,
                true,
                gameObject.layer,
                null,
                LightProbeUsage.Off,
                null);
        }
    }

    private void BuildBatches(List<GrassInstance> instances)
    {
        int batchCount = Mathf.CeilToInt(instances.Count / (float)MaximumInstancesPerDraw);
        batches = new DrawBatch[batchCount];

        for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            int start = batchIndex * MaximumInstancesPerDraw;
            int count = Mathf.Min(MaximumInstancesPerDraw, instances.Count - start);
            var batch = new DrawBatch
            {
                instances = new GrassInstance[count],
                matrices = new Matrix4x4[count],
                bends = new Vector4[count],
                variations = new Vector4[count],
                properties = new MaterialPropertyBlock()
            };

            for (int i = 0; i < count; i++)
            {
                GrassInstance instance = instances[start + i];
                batch.instances[i] = instance;
                batch.matrices[i] = instance.matrix;
                batch.variations[i] = instance.variation;
            }

            batch.properties.SetVectorArray(GrassVariationId, batch.variations);
            batches[batchIndex] = batch;
        }
    }

    private bool HasCloseNeighbour(
        Vector2 point,
        Dictionary<Vector2Int, List<Vector2>> grid,
        float cellSize,
        float minimumSpacing)
    {
        Vector2Int cell = GetCell(point, cellSize);
        float minimumSpacingSquared = minimumSpacing * minimumSpacing;
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                if (!grid.TryGetValue(cell + new Vector2Int(x, y), out List<Vector2> points))
                {
                    continue;
                }

                for (int i = 0; i < points.Count; i++)
                {
                    if ((points[i] - point).sqrMagnitude < minimumSpacingSquared)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static Vector2Int GetCell(Vector2 point, float cellSize)
    {
        return new Vector2Int(
            Mathf.FloorToInt(point.x / cellSize),
            Mathf.FloorToInt(point.y / cellSize));
    }

    private void TryPlaceOnGround(ref Vector3 position, ref Vector3 normal)
    {
        Vector3 origin = position + Vector3.up * 0.75f;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            groundHits,
            1.5f,
            groundLayers,
            QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHits[i];
            Transform hitTransform = hit.collider.transform;
            if (hitTransform.GetComponentInParent<ChickenController>() != null
                || hitTransform.GetComponentInParent<ChickenEgg>() != null
                || hitTransform.GetComponentInParent<FoodPile>() != null)
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                position = hit.point;
                normal = hit.normal;
            }
        }
    }

    private bool TryPlaceOuterOnGround(ref Vector3 position, ref Vector3 normal)
    {
        Vector3 origin = position + Vector3.up * 0.75f;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            groundHits,
            1.5f,
            groundLayers,
            QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        bool foundGround = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHits[i];
            Transform hitTransform = hit.collider.transform;
            if (hitTransform.GetComponentInParent<ChickenController>() != null
                || hitTransform.GetComponentInParent<ChickenEgg>() != null
                || hitTransform.GetComponentInParent<FoodPile>() != null)
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                position = hit.point;
                normal = hit.normal;
                foundGround = true;
            }
        }

        return foundGround;
    }

    private static Mesh CreatePlaceholderClump()
    {
        var vertices = new List<Vector3>(21);
        var normals = new List<Vector3>(21);
        var uvs = new List<Vector2>(21);
        var triangles = new List<int>(45);

        AddBlade(vertices, normals, uvs, triangles, 0f, new Vector3(-0.018f, 0f, 0.006f), 1f);
        AddBlade(vertices, normals, uvs, triangles, 60f, new Vector3(0.016f, 0f, 0.01f), 0.88f);
        AddBlade(vertices, normals, uvs, triangles, 120f, new Vector3(0.002f, 0f, -0.018f), 0.76f);

        var mesh = new Mesh { name = "Generated Grass Clump Placeholder" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1.2f, 2f));
        mesh.UploadMeshData(true);
        return mesh;
    }

    private static Mesh CreateCombinedClumpMesh(GameObject sourcePrefab)
    {
        MeshFilter[] filters = sourcePrefab.GetComponentsInChildren<MeshFilter>(false);
        var combineInstances = new List<CombineInstance>(filters.Length);
        Matrix4x4 rootWorldToLocal = sourcePrefab.transform.worldToLocalMatrix;

        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter.sharedMesh == null)
            {
                continue;
            }

            combineInstances.Add(new CombineInstance
            {
                mesh = filter.sharedMesh,
                subMeshIndex = 0,
                transform = rootWorldToLocal * filter.transform.localToWorldMatrix
            });
        }

        if (combineInstances.Count == 0)
        {
            Debug.LogWarning($"Grass clump prefab '{sourcePrefab.name}' contains no MeshFilter meshes.");
            return null;
        }

        var mesh = new Mesh { name = $"Combined {sourcePrefab.name}" };
        mesh.CombineMeshes(combineInstances.ToArray(), true, true, false);
        Bounds sourceBounds = mesh.bounds;
        sourceBounds.Expand(new Vector3(2f, 0.2f, 2f));
        mesh.bounds = sourceBounds;
        mesh.UploadMeshData(true);
        return mesh;
    }

    private int GetClumpPrefabSignature()
    {
        if (grassClumpPrefab == null)
        {
            return 0;
        }

        unchecked
        {
            int signature = grassClumpPrefab.GetInstanceID();
            ProceduralGrassClumpAuthoring authoring =
                grassClumpPrefab.GetComponent<ProceduralGrassClumpAuthoring>();
            signature = signature * 31 + (authoring != null
                ? authoring.GenerationRevision
                : 0);
            MeshFilter[] filters = grassClumpPrefab.GetComponentsInChildren<MeshFilter>(false);
            signature = signature * 31 + filters.Length;
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                signature = signature * 31
                    + (filter.sharedMesh != null ? filter.sharedMesh.GetInstanceID() : 0);
                signature = signature * 31 + filter.transform.localPosition.GetHashCode();
                signature = signature * 31 + filter.transform.localRotation.GetHashCode();
                signature = signature * 31 + filter.transform.localScale.GetHashCode();
            }

            return signature;
        }
    }

    private static void AddBlade(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles,
        float angle,
        Vector3 offset,
        float height)
    {
        int start = vertices.Count;
        Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.up);
        Vector3 right = rotation * Vector3.right;
        Vector3 normal = rotation * Vector3.forward;
        float[] heights = { 0f, 0.34f, 0.7f };
        float[] widths = { 0.018f, 0.015f, 0.009f };

        for (int i = 0; i < heights.Length; i++)
        {
            float y = heights[i] * height;
            vertices.Add(offset - right * widths[i] + Vector3.up * y);
            vertices.Add(offset + right * widths[i] + Vector3.up * y);
            normals.Add(normal);
            normals.Add(normal);
            uvs.Add(new Vector2(0f, heights[i]));
            uvs.Add(new Vector2(1f, heights[i]));
        }

        vertices.Add(offset + Vector3.up * height);
        normals.Add(normal);
        uvs.Add(new Vector2(0.5f, 1f));

        triangles.Add(start + 0);
        triangles.Add(start + 2);
        triangles.Add(start + 1);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
        triangles.Add(start + 2);
        triangles.Add(start + 4);
        triangles.Add(start + 3);
        triangles.Add(start + 3);
        triangles.Add(start + 4);
        triangles.Add(start + 5);
        triangles.Add(start + 4);
        triangles.Add(start + 6);
        triangles.Add(start + 5);
    }

    private static float NextFloat(System.Random random)
    {
        return (float)random.NextDouble();
    }

    private void OnDestroy()
    {
        ReleaseGeneratedAssets();
    }

    private void OnDisable()
    {
        ReleaseGeneratedAssets();
        activeMesh = null;
        activeMaterial = null;
        batches = Array.Empty<DrawBatch>();
        InstanceCount = 0;
    }

    private void ReleaseGeneratedAssets()
    {
        if (generatedPlaceholderMesh != null)
        {
            DestroyGeneratedObject(generatedPlaceholderMesh);
            generatedPlaceholderMesh = null;
        }

        if (generatedFallbackMaterial != null)
        {
            DestroyGeneratedObject(generatedFallbackMaterial);
            generatedFallbackMaterial = null;
        }
    }

    private static void DestroyGeneratedObject(UnityEngine.Object generatedObject)
    {
        if (Application.isPlaying)
        {
            Destroy(generatedObject);
        }
        else
        {
            DestroyImmediate(generatedObject);
        }
    }

    private void OnValidate()
    {
        areaSize.x = Mathf.Max(0.01f, areaSize.x);
        areaSize.y = Mathf.Max(0.01f, areaSize.y);
        outerAreaSize.x = Mathf.Max(areaSize.x, outerAreaSize.x);
        outerAreaSize.y = Mathf.Max(areaSize.y, outerAreaSize.y);
        outerDensityMultiplier = Mathf.Clamp(outerDensityMultiplier, 0.01f, 1f);
        outerInteractionFadeDistance = Mathf.Max(0.01f, outerInteractionFadeDistance);
        minimumOuterInteraction = Mathf.Clamp01(minimumOuterInteraction);
        densityPerSquareMetre = Mathf.Max(1f, densityPerSquareMetre);
        minimumHeight = Mathf.Max(0.01f, minimumHeight);
        maximumHeight = Mathf.Max(minimumHeight, maximumHeight);
        minimumWidthScale = Mathf.Max(0.01f, minimumWidthScale);
        maximumWidthScale = Mathf.Max(minimumWidthScale, maximumWidthScale);
        minimumEdgeHeightScale = Mathf.Clamp(minimumEdgeHeightScale, 0.05f, 1f);
        minimumEdgeWidthScale = Mathf.Clamp(minimumEdgeWidthScale, 0.05f, 1f);
        edgeTaperPower = Mathf.Max(0.05f, edgeTaperPower);
        targetGrassCoverage = Mathf.Clamp(targetGrassCoverage, 0.75f, 0.98f);
        dirtPatchScale = Mathf.Max(0.01f, dirtPatchScale);
        dirtEdgeWidth = Mathf.Clamp(dirtEdgeWidth, 0.02f, 0.3f);
        dirtEdgeBreakup = Mathf.Clamp(dirtEdgeBreakup, 0f, 0.4f);
        domainWarpStrength = Mathf.Max(0f, domainWarpStrength);
        interactionBendDistance = Mathf.Max(0f, interactionBendDistance);
        flattenResponseTime = Mathf.Max(0.01f, flattenResponseTime);
        flattenRecoveryTime = Mathf.Max(0.01f, flattenRecoveryTime);
        bendResponseTime = Mathf.Max(0.01f, bendResponseTime);
        bendRecoveryTime = Mathf.Max(0.01f, bendRecoveryTime);
        steadyWindBend = Mathf.Max(0f, steadyWindBend);
        turbulentWindBend = Mathf.Max(0f, turbulentWindBend);
        turbulenceDeadZone = Mathf.Max(0f, turbulenceDeadZone);
        maximumWindBend = Mathf.Max(0f, maximumWindBend);
        minimumWindResponse = Mathf.Clamp01(minimumWindResponse);
        maximumWindResponse = Mathf.Clamp(maximumWindResponse, minimumWindResponse, 2f);
        windNoiseOffsetDistance = Mathf.Max(0f, windNoiseOffsetDistance);
        groundMaskResolution = Mathf.Clamp(groundMaskResolution, 64, 2048);
        outerGroundMaskResolution = Mathf.Clamp(outerGroundMaskResolution, 64, 2048);

        if (isActiveAndEnabled)
        {
            regenerationRequested = true;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.25f, 0.8f, 0.2f, 0.8f);
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(areaSize.x, 0.02f, areaSize.y));
        if (extendIntoOuterArea)
        {
            Gizmos.color = new Color(0.18f, 0.55f, 0.85f, 0.65f);
            Gizmos.DrawWireCube(
                new Vector3(outerAreaCenter.x, 0f, outerAreaCenter.y),
                new Vector3(outerAreaSize.x, 0.015f, outerAreaSize.y));
        }
        Gizmos.matrix = oldMatrix;
    }
}
