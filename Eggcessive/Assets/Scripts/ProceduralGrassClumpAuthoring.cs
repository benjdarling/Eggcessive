using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class ProceduralGrassClumpAuthoring : MonoBehaviour
{
    [Header("Generated Output")]
    [SerializeField] private MeshFilter generatedMeshFilter = null;
    [SerializeField] private string meshAssetPath =
        "Assets/Env/meshes/mesh_grass_clump_generated.asset";
    [SerializeField] private bool autoRegenerate = true;
    [SerializeField] private bool disableOtherMeshObjectsOnGenerate = true;
    [SerializeField, HideInInspector] private int generationRevision;
    [SerializeField, HideInInspector] private int generatedSettingsHash;

    [Header("Clump")]
    [SerializeField, Range(1, 64)] private int bladeCount = 9;
    [SerializeField] private Vector2 bladeHeightRange = new Vector2(0.62f, 1f);
    [SerializeField] private Vector2 bladeWidthRange = new Vector2(0.028f, 0.052f);
    [SerializeField, Range(0f, 0.5f)] private float spreadRadius = 0.055f;
    [SerializeField] private int randomSeed = 7319;

    [Header("Blade Silhouette")]
    [SerializeField, Range(0, 24)] private int verticalCuts = 5;
    [SerializeField] private AnimationCurve sideSilhouette = null;

    [Header("Blade Bend")]
    [SerializeField] private Vector2 bendDegreesRange = new Vector2(3f, 18f);
    [SerializeField] private AnimationCurve bendProfile = null;

    [Header("Normals")]
    [SerializeField, Range(0f, 1f)] private float normalsUpBlend = 0.45f;

    public MeshFilter GeneratedMeshFilter => generatedMeshFilter;
    public string MeshAssetPath => meshAssetPath;
    public bool AutoRegenerate => autoRegenerate;
    public bool DisableOtherMeshObjectsOnGenerate => disableOtherMeshObjectsOnGenerate;
    public int GenerationRevision => generationRevision;
    public float SpreadRadius => spreadRadius;
    public bool GeneratedSettingsAreCurrent =>
        generatedSettingsHash == CalculateSettingsHash();

#if UNITY_EDITOR
    public static event Action<ProceduralGrassClumpAuthoring> EditorValidationRequested;
#endif

    public void MarkMeshGenerated()
    {
        unchecked
        {
            generationRevision++;
        }
    }

    public void MarkSettingsGenerated()
    {
        generatedSettingsHash = CalculateSettingsHash();
    }

    public int CalculateSettingsHash()
    {
        EnsureCurves();
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + bladeCount;
            hash = hash * 31 + bladeHeightRange.GetHashCode();
            hash = hash * 31 + bladeWidthRange.GetHashCode();
            hash = hash * 31 + spreadRadius.GetHashCode();
            hash = hash * 31 + randomSeed;
            hash = hash * 31 + verticalCuts;
            hash = AddCurveHash(hash, sideSilhouette);
            hash = hash * 31 + bendDegreesRange.GetHashCode();
            hash = AddCurveHash(hash, bendProfile);
            hash = hash * 31 + normalsUpBlend.GetHashCode();
            return hash;
        }
    }

    public Mesh BuildMesh()
    {
        EnsureCurves();

        int rowCount = verticalCuts + 2;
        int verticesPerBlade = rowCount * 2;
        int triangleIndexCount = (rowCount - 1) * 6;
        var vertices = new List<Vector3>(bladeCount * verticesPerBlade);
        var normals = new List<Vector3>(bladeCount * verticesPerBlade);
        var uvs = new List<Vector2>(bladeCount * verticesPerBlade);
        var authoredBends = new List<Vector2>(bladeCount * verticesPerBlade);
        var triangles = new List<int>(bladeCount * triangleIndexCount);
        var random = new System.Random(randomSeed);

        for (int bladeIndex = 0; bladeIndex < bladeCount; bladeIndex++)
        {
            float height = Mathf.Lerp(bladeHeightRange.x, bladeHeightRange.y, NextFloat(random));
            float width = Mathf.Lerp(bladeWidthRange.x, bladeWidthRange.y, NextFloat(random));
            float yaw = NextFloat(random) * 360f;
            float bendDegrees = Mathf.Lerp(
                bendDegreesRange.x,
                bendDegreesRange.y,
                NextFloat(random));
            float spreadAngle = NextFloat(random) * Mathf.PI * 2f;
            float spread = Mathf.Sqrt(NextFloat(random)) * spreadRadius;
            Vector3 basePosition = new Vector3(
                Mathf.Cos(spreadAngle) * spread,
                0f,
                Mathf.Sin(spreadAngle) * spread);
            Quaternion orientation = Quaternion.AngleAxis(yaw, Vector3.up);
            Vector3 right = orientation * Vector3.right;
            Vector3 forward = orientation * Vector3.forward;
            int bladeVertexStart = vertices.Count;

            for (int row = 0; row < rowCount; row++)
            {
                float t = row / (float)(rowCount - 1);
                Vector3 centre = GetBladeCentre(
                    basePosition,
                    forward,
                    height,
                    bendDegrees,
                    t);
                float halfWidth = width * 0.5f * Mathf.Max(0f, sideSilhouette.Evaluate(t));
                Vector3 tangent = GetBladeTangent(
                    basePosition,
                    forward,
                    height,
                    bendDegrees,
                    t);
                Vector3 surfaceNormal = Vector3.Cross(right, tangent).normalized;
                Vector3 blendedNormal = Vector3.Slerp(
                    surfaceNormal,
                    Vector3.up,
                    normalsUpBlend).normalized;
                Vector3 authoredBend = centre - basePosition - Vector3.up * (height * t);

                vertices.Add(centre - right * halfWidth);
                vertices.Add(centre + right * halfWidth);
                normals.Add(blendedNormal);
                normals.Add(blendedNormal);
                uvs.Add(new Vector2(0f, t));
                uvs.Add(new Vector2(1f, t));
                authoredBends.Add(new Vector2(authoredBend.x, authoredBend.z));
                authoredBends.Add(new Vector2(authoredBend.x, authoredBend.z));
            }

            for (int row = 0; row < rowCount - 1; row++)
            {
                int lowerLeft = bladeVertexStart + row * 2;
                int upperLeft = lowerLeft + 2;
                triangles.Add(lowerLeft);
                triangles.Add(upperLeft);
                triangles.Add(lowerLeft + 1);
                triangles.Add(lowerLeft + 1);
                triangles.Add(upperLeft);
                triangles.Add(upperLeft + 1);
            }
        }

        var mesh = new Mesh
        {
            name = $"Procedural Grass Clump ({bladeCount} blades)",
            indexFormat = vertices.Count > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        // UV1 carries only the geometric bend, allowing the grass system to
        // shorten it independently from blade width and clump spread.
        mesh.SetUVs(1, authoredBends);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        Bounds bounds = mesh.bounds;
        bounds.Expand(new Vector3(0.03f, 0.02f, 0.03f));
        mesh.bounds = bounds;
        return mesh;
    }

    private Vector3 GetBladeCentre(
        Vector3 basePosition,
        Vector3 forward,
        float height,
        float bendDegrees,
        float t)
    {
        float bendDistance = Mathf.Tan(bendDegrees * Mathf.Deg2Rad)
            * height
            * bendProfile.Evaluate(t);
        return basePosition + Vector3.up * (height * t) + forward * bendDistance;
    }

    private Vector3 GetBladeTangent(
        Vector3 basePosition,
        Vector3 forward,
        float height,
        float bendDegrees,
        float t)
    {
        const float sampleDistance = 0.002f;
        float lowerT = Mathf.Max(0f, t - sampleDistance);
        float upperT = Mathf.Min(1f, t + sampleDistance);
        Vector3 lower = GetBladeCentre(basePosition, forward, height, bendDegrees, lowerT);
        Vector3 upper = GetBladeCentre(basePosition, forward, height, bendDegrees, upperT);
        Vector3 tangent = upper - lower;
        return tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector3.up;
    }

    private void EnsureCurves()
    {
        if (sideSilhouette == null || sideSilhouette.length == 0)
        {
            sideSilhouette = new AnimationCurve(
                new Keyframe(0f, 0.55f),
                new Keyframe(0.16f, 1f),
                new Keyframe(0.72f, 0.46f),
                new Keyframe(1f, 0f));
            SmoothCurve(sideSilhouette);
        }

        if (bendProfile == null || bendProfile.length == 0)
        {
            bendProfile = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.45f, 0.08f),
                new Keyframe(1f, 1f));
            SmoothCurve(bendProfile);
        }
    }

    private static void SmoothCurve(AnimationCurve curve)
    {
        for (int i = 0; i < curve.length; i++)
        {
            curve.SmoothTangents(i, 0f);
        }
    }

    private static float NextFloat(System.Random random)
    {
        return (float)random.NextDouble();
    }

    private static int AddCurveHash(int hash, AnimationCurve curve)
    {
        if (curve == null)
        {
            return hash * 31;
        }

        Keyframe[] keys = curve.keys;
        hash = hash * 31 + keys.Length;
        for (int i = 0; i < keys.Length; i++)
        {
            hash = hash * 31 + keys[i].time.GetHashCode();
            hash = hash * 31 + keys[i].value.GetHashCode();
            hash = hash * 31 + keys[i].inTangent.GetHashCode();
            hash = hash * 31 + keys[i].outTangent.GetHashCode();
            hash = hash * 31 + keys[i].inWeight.GetHashCode();
            hash = hash * 31 + keys[i].outWeight.GetHashCode();
            hash = hash * 31 + (int)keys[i].weightedMode;
        }

        return hash;
    }

    private void Reset()
    {
        EnsureCurves();
    }

    private void OnValidate()
    {
        bladeCount = Mathf.Clamp(bladeCount, 1, 64);
        verticalCuts = Mathf.Clamp(verticalCuts, 0, 24);
        bladeHeightRange.x = Mathf.Max(0.01f, bladeHeightRange.x);
        bladeHeightRange.y = Mathf.Max(bladeHeightRange.x, bladeHeightRange.y);
        bladeWidthRange.x = Mathf.Max(0.001f, bladeWidthRange.x);
        bladeWidthRange.y = Mathf.Max(bladeWidthRange.x, bladeWidthRange.y);
        spreadRadius = Mathf.Max(0f, spreadRadius);
        float minimumBend = Mathf.Clamp(
            Mathf.Min(bendDegreesRange.x, bendDegreesRange.y),
            -80f,
            80f);
        float maximumBend = Mathf.Clamp(
            Mathf.Max(bendDegreesRange.x, bendDegreesRange.y),
            minimumBend,
            80f);
        bendDegreesRange = new Vector2(minimumBend, maximumBend);
        normalsUpBlend = Mathf.Clamp01(normalsUpBlend);
        EnsureCurves();
#if UNITY_EDITOR
        EditorValidationRequested?.Invoke(this);
#endif
    }
}
