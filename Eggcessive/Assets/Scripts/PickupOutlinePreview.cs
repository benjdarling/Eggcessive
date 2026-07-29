using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a hovered pickup for the screen-space pickup outline renderer feature.
/// Rendering layers are independent of physics layers and regular visibility.
/// </summary>
internal sealed class PickupOutlinePreview : IDisposable
{
    public const uint PickupRenderingLayerMask = 1u << 9;

    private static readonly List<Renderer> activeRenderers =
        new List<Renderer>();
    private readonly List<RendererState> rendererStates =
        new List<RendererState>();
    private Component target;
    private bool registeredActive;

    public static bool HasActiveTarget { get; private set; }
    public static IReadOnlyList<Renderer> ActiveRenderers =>
        activeRenderers;

    private readonly struct RendererState
    {
        public readonly Renderer Renderer;
        public readonly uint OriginalMask;

        public RendererState(Renderer renderer)
        {
            Renderer = renderer;
            OriginalMask = renderer.renderingLayerMask;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        HasActiveTarget = false;
        activeRenderers.Clear();
    }

    public void SetTarget(Component newTarget)
    {
        if (target == newTarget)
        {
            return;
        }

        Clear();
        target = newTarget;

        if (target == null)
        {
            return;
        }

        MeshRenderer[] meshRenderers =
            target.GetComponentsInChildren<MeshRenderer>(false);
        SkinnedMeshRenderer[] skinnedRenderers =
            target.GetComponentsInChildren<SkinnedMeshRenderer>(false);

        foreach (MeshRenderer source in meshRenderers)
        {
            MarkRenderer(source);
        }

        foreach (SkinnedMeshRenderer source in skinnedRenderers)
        {
            MarkRenderer(source);
        }

        if (rendererStates.Count > 0)
        {
            registeredActive = true;
            HasActiveTarget = true;
        }
    }

    public void Clear()
    {
        target = null;

        foreach (RendererState state in rendererStates)
        {
            if (state.Renderer != null)
            {
                activeRenderers.Remove(state.Renderer);
                uint currentMask = state.Renderer.renderingLayerMask;
                state.Renderer.renderingLayerMask =
                    (currentMask & ~PickupRenderingLayerMask)
                    | (state.OriginalMask & PickupRenderingLayerMask);
            }
        }

        rendererStates.Clear();

        if (registeredActive)
        {
            registeredActive = false;
            HasActiveTarget = false;
        }
    }

    public void Dispose()
    {
        Clear();
    }

    private void MarkRenderer(Renderer renderer)
    {
        if (renderer == null
            || !renderer.enabled
            || renderer.forceRenderingOff)
        {
            return;
        }

        rendererStates.Add(new RendererState(renderer));
        activeRenderers.Add(renderer);
        renderer.renderingLayerMask |= PickupRenderingLayerMask;
    }
}
