using UnityEngine;

[DisallowMultipleComponent]
public sealed class IncubatorAnimationEvents : MonoBehaviour
{
    private IncubatorController incubator;

    public void Initialize(IncubatorController controller)
    {
        incubator = controller;
    }

    // Animation events are delivered to the GameObject holding the Animator.
    public void OnHatchFrame()
    {
        if (incubator != null)
        {
            incubator.OnHatchFrame();
        }
    }
}
