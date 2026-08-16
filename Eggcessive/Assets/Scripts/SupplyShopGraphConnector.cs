using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class SupplyShopGraphConnector : MonoBehaviour
{
    [SerializeField] private Image lineImage = null;
    [SerializeField, Min(1f)] private float lineWidth = 5f;

    private RectTransform from;
    private RectTransform to;
    private ProgressionNodeButton fromNode;
    private ProgressionNodeButton toNode;
    private Color branchColor = Color.white;

    public void Configure(
        RectTransform source,
        RectTransform destination,
        Color color,
        ProgressionNodeButton sourceNode = null,
        ProgressionNodeButton destinationNode = null)
    {
        from = source;
        to = destination;
        branchColor = color;
        fromNode = sourceNode;
        toNode = destinationNode;
        if (lineImage == null)
        {
            lineImage = GetComponent<Image>();
        }
        Refresh();
    }

    public void Refresh()
    {
        if (from == null || to == null)
        {
            gameObject.SetActive(false);
            return;
        }

        bool sourceVisible = fromNode == null || fromNode.IsGraphVisible;
        bool destinationVisible = toNode == null || toNode.IsGraphVisible;
        gameObject.SetActive(sourceVisible && destinationVisible);
        if (!gameObject.activeSelf)
        {
            return;
        }

        bool owned = toNode == null || toNode.GetNodeState().IsMaxed;
        if (lineImage == null)
        {
            lineImage = GetComponent<Image>();
        }
        lineImage.color = owned
            ? Color.Lerp(branchColor, new Color(1f, 0.82f, 0.28f, 1f), 0.55f)
            : new Color(branchColor.r, branchColor.g, branchColor.b, 0.38f);
        PositionLine();
    }

    private void LateUpdate()
    {
        if (gameObject.activeSelf)
        {
            PositionLine();
        }
    }

    private void PositionLine()
    {
        RectTransform rect = transform as RectTransform;
        RectTransform parent = rect != null ? rect.parent as RectTransform : null;
        if (rect == null || parent == null || from == null || to == null)
        {
            return;
        }

        Vector2 a = parent.InverseTransformPoint(
            from.TransformPoint(from.rect.center));
        Vector2 b = parent.InverseTransformPoint(
            to.TransformPoint(to.rect.center));
        Vector2 delta = b - a;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = a;
        rect.sizeDelta = new Vector2(delta.magnitude, lineWidth);
        rect.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }
}
