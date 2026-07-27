using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UiModelGraphic : MaskableGraphic
{
    [SerializeField] private GameObject sourceModel;
    [SerializeField, Range(0.1f, 1f)] private float rectFill = 0.9f;

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
        float modelSize = Mathf.Max(
            modelBounds.size.x,
            modelBounds.size.y);
        float targetSize = Mathf.Min(targetRect.width, targetRect.height);
        float scale = modelSize > Mathf.Epsilon
            ? targetSize * rectFill / modelSize
            : 1f;
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
                position = (position - modelBounds.center) * scale
                    + targetCenter;

                Vector3 normal = vertexIndex < normals.Length
                    ? normalMatrix.MultiplyVector(normals[vertexIndex]).normalized
                    : Vector3.back;
                Vector4 tangent = vertexIndex < tangents.Length
                    ? tangents[vertexIndex]
                    : new Vector4(1f, 0f, 0f, 1f);
                Vector3 tangentDirection = normalMatrix.MultiplyVector(
                    new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
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

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        rectFill = Mathf.Clamp(rectFill, 0.1f, 1f);
        SetVerticesDirty();
    }
#endif
}
