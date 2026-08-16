using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class ProgressionTreePopupHoverGuard : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private ProgressionTreePreview preview;

    public void Configure(ProgressionTreePreview treePreview)
    {
        preview = treePreview;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        preview?.CancelScheduledHide();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        preview?.ScheduleHide(0.08f);
    }
}
