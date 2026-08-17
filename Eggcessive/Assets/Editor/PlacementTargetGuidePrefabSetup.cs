using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PlacementTargetGuidePrefabSetup
{
    private const string FolderPath = "Assets/Resources/Guides";
    private const string MaterialPath =
        FolderPath + "/mat_PlacementTargetGuide.mat";
    private const string PrefabPath =
        FolderPath + "/prefab_PlacementTargetGuide.prefab";

    static PlacementTargetGuidePrefabSetup()
    {
        EditorApplication.delayCall += BuildIfMissing;
    }

    [MenuItem("Tools/Eggcessive/Rebuild Placement Target Guide Prefab")]
    public static void Rebuild()
    {
        EnsureFolder();
        Material material = GetOrCreateMaterial();
        GameObject root = new GameObject("prefab_PlacementTargetGuide");

        try
        {
            PlacementTargetGuideVisual visual =
                root.AddComponent<PlacementTargetGuideVisual>();
            GameObject art = new GameObject("Art - Replace Me");
            art.transform.SetParent(root.transform, false);
            art.transform.localPosition = Vector3.up * 0.035f;

            CreateRing(
                art.transform,
                "Outer Target Ring",
                material,
                0.19f,
                0.027f,
                new Color(1f, 0.76f, 0.12f, 0.95f));
            CreateRing(
                art.transform,
                "Inner Target Ring",
                material,
                0.105f,
                0.018f,
                new Color(1f, 0.93f, 0.52f, 0.9f));

            visual.ConfigureArtRoot(art.transform);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"Built editable placement target guide prefab at {PrefabPath}. "
            + "Replace the 'Art - Replace Me' child to swap its artwork.");
    }

    private static void BuildIfMissing()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += BuildIfMissing;
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Rebuild();
        }
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        if (!AssetDatabase.IsValidFolder(FolderPath))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "Guides");
        }
    }

    private static Material GetOrCreateMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        material = new Material(shader)
        {
            name = "mat_PlacementTargetGuide",
            color = Color.white
        };
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    private static void CreateRing(
        Transform parent,
        string name,
        Material material,
        float radius,
        float width,
        Color color)
    {
        const int SegmentCount = 40;
        GameObject ringObject = new GameObject(name);
        ringObject.transform.SetParent(parent, false);
        LineRenderer ring = ringObject.AddComponent<LineRenderer>();
        ring.sharedMaterial = material;
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = SegmentCount;
        ring.widthMultiplier = width;
        ring.numCornerVertices = 3;
        ring.numCapVertices = 3;
        ring.startColor = color;
        ring.endColor = color;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;

        for (int index = 0; index < SegmentCount; index++)
        {
            float angle = index * Mathf.PI * 2f / SegmentCount;
            ring.SetPosition(
                index,
                new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }
}
