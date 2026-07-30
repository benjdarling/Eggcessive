using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UiModelGraphic : MaskableGraphic
{
    [SerializeField] private GameObject sourceModel;
    [SerializeField, Range(0.1f, 1f)] private float rectFill = 0.9f;

    public GameObject SourceModel => sourceModel;

    public override Texture mainTexture
    {
        get
        {
            if (material != null)
            {
                Texture texture = material.GetTexture("_MainTex");
                if (texture != null)
                {
                    return texture;
                }

                texture = material.GetTexture("_MatCap");
                if (texture != null)
                {
                    return texture;
                }
            }

            return base.mainTexture;
        }
    }

    public void SetSourceModel(GameObject model)
    {
        sourceModel = model;
        SetVerticesDirty();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        if (canvas != null)
        {
            canvas.additionalShaderChannels |=
                AdditionalCanvasShaderChannels.Normal;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();

        if (sourceModel == null)
        {
            return;
        }

        MeshFilter[] meshFilters =
            sourceModel.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters.Length == 0)
        {
            return;
        }

        Matrix4x4 rootWorldToLocal =
            sourceModel.transform.worldToLocalMatrix;
        Bounds modelBounds = default;
        bool hasBounds = false;

        for (int filterIndex = 0;
             filterIndex < meshFilters.Length;
             filterIndex++)
        {
            MeshFilter meshFilter = meshFilters[filterIndex];
            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null || !mesh.isReadable)
            {
                continue;
            }

            Matrix4x4 modelMatrix =
                rootWorldToLocal * meshFilter.transform.localToWorldMatrix;
            Vector3[] positions = mesh.vertices;
            for (int vertexIndex = 0;
                 vertexIndex < positions.Length;
                 vertexIndex++)
            {
                Vector3 position =
                    modelMatrix.MultiplyPoint3x4(positions[vertexIndex]);
                if (hasBounds)
                {
                    modelBounds.Encapsulate(position);
                }
                else
                {
                    modelBounds = new Bounds(position, Vector3.zero);
                    hasBounds = true;
                }
            }
        }

        if (!hasBounds)
        {
            return;
        }

        Rect targetRect = GetPixelAdjustedRect();
        GetProjectionAxes(
            modelBounds.size,
            out int horizontalAxis,
            out int verticalAxis,
            out int depthAxis);
        float modelWidth = GetAxis(modelBounds.size, horizontalAxis);
        float modelHeight = GetAxis(modelBounds.size, verticalAxis);
        float scale = Mathf.Min(
            targetRect.width / Mathf.Max(Mathf.Epsilon, modelWidth),
            targetRect.height / Mathf.Max(Mathf.Epsilon, modelHeight))
            * rectFill;
        Vector3 targetCenter = targetRect.center;

        for (int filterIndex = 0;
             filterIndex < meshFilters.Length;
             filterIndex++)
        {
            MeshFilter meshFilter = meshFilters[filterIndex];
            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null || !mesh.isReadable)
            {
                continue;
            }

            Matrix4x4 modelMatrix =
                rootWorldToLocal * meshFilter.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = modelMatrix.inverse.transpose;
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;
            Vector2[] uv = mesh.uv;
            int vertexOffset = vertexHelper.currentVertCount;

            for (int vertexIndex = 0;
                 vertexIndex < positions.Length;
                 vertexIndex++)
            {
                Vector3 position =
                    modelMatrix.MultiplyPoint3x4(positions[vertexIndex]);
                Vector3 centeredPosition = position - modelBounds.center;
                position = new Vector3(
                    GetAxis(centeredPosition, horizontalAxis),
                    GetAxis(centeredPosition, verticalAxis),
                    GetAxis(centeredPosition, depthAxis)) * scale
                    + targetCenter;

                Vector3 normal = vertexIndex < normals.Length
                    ? normalMatrix.MultiplyVector(normals[vertexIndex]).normalized
                    : Vector3.back;
                normal = new Vector3(
                    GetAxis(normal, horizontalAxis),
                    GetAxis(normal, verticalAxis),
                    GetAxis(normal, depthAxis));
                Vector4 tangent = vertexIndex < tangents.Length
                    ? tangents[vertexIndex]
                    : new Vector4(1f, 0f, 0f, 1f);
                Vector3 tangentDirection = normalMatrix.MultiplyVector(
                    new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                tangentDirection = new Vector3(
                    GetAxis(tangentDirection, horizontalAxis),
                    GetAxis(tangentDirection, verticalAxis),
                    GetAxis(tangentDirection, depthAxis));
                Vector4 transformedTangent = new Vector4(
                    tangentDirection.x,
                    tangentDirection.y,
                    tangentDirection.z,
                    tangent.w);
                Vector2 textureCoordinate = vertexIndex < uv.Length
                    ? uv[vertexIndex]
                    : Vector2.zero;

                vertexHelper.AddVert(
                    position,
                    color,
                    textureCoordinate,
                    Vector2.zero,
                    normal,
                    transformedTangent);
            }

            for (int subMeshIndex = 0;
                 subMeshIndex < mesh.subMeshCount;
                 subMeshIndex++)
            {
                if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
                {
                    continue;
                }

                int[] indices = mesh.GetIndices(subMeshIndex);
                for (int index = 0; index + 2 < indices.Length; index += 3)
                {
                    vertexHelper.AddTriangle(
                        vertexOffset + indices[index],
                        vertexOffset + indices[index + 1],
                        vertexOffset + indices[index + 2]);
                }
            }
        }
    }

    private static void GetProjectionAxes(
        Vector3 size,
        out int horizontalAxis,
        out int verticalAxis,
        out int depthAxis)
    {
        horizontalAxis = 0;
        if (size.y > size.x && size.y >= size.z)
        {
            horizontalAxis = 1;
        }
        else if (size.z > size.x && size.z > size.y)
        {
            horizontalAxis = 2;
        }

        if (horizontalAxis == 0)
        {
            verticalAxis = size.y >= size.z ? 1 : 2;
        }
        else if (horizontalAxis == 1)
        {
            verticalAxis = size.x >= size.z ? 0 : 2;
        }
        else
        {
            verticalAxis = size.x >= size.y ? 0 : 1;
        }

        depthAxis = 3 - horizontalAxis - verticalAxis;
    }

    private static float GetAxis(Vector3 vector, int axis)
    {
        return axis == 0
            ? vector.x
            : axis == 1
                ? vector.y
                : vector.z;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        rectFill = Mathf.Clamp(rectFill, 0.1f, 1f);
        SetVerticesDirty();
    }
#endif
}
