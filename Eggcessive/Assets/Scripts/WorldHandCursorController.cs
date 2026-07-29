using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class WorldHandCursorController : MonoBehaviour
{
    private const string CursorPrefabResource =
        "UI/prefab_world_hand_cursor";
    private const string HandLayerName = "UIHand";
    private const string UiShadowMaterialResource =
        "Materials/mat_ui_hand_shadow";
    private static readonly int PointPoseState =
        Animator.StringToHash("Base Layer.Point");
    private static readonly int EggHoldPoseState =
        Animator.StringToHash("Base Layer.Egg Hold");
    private static readonly int EggReadyPoseState =
        Animator.StringToHash("Base Layer.Egg Ready To Grab");
    private static readonly int ChickenHoldPoseState =
        Animator.StringToHash("Base Layer.Chicken Hold");
    private static readonly int ChickenReadyPoseState =
        Animator.StringToHash("Base Layer.Chicken Ready To Grab");
    private static readonly int ShadowColorId =
        Shader.PropertyToID("_ShadowColor");
    private static readonly int UiClipRectId =
        Shader.PropertyToID("_UiClipRect");

    [Header("Authored Hand")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator handAnimator;
    [SerializeField] private Transform heldItemAttachPoint;
    [SerializeField, Min(0f)] private float poseTransitionDuration = 0.08f;

    [Header("Camera-relative Placement")]
    [SerializeField, Min(0f)] private float cursorPlaneHeight = 0.34f;
    [Tooltip(
        "Offsets the rendered hand in camera space without changing the actual click position. "
        + "Positive X moves right, positive Y moves up, and positive Z moves forward.")]
    [SerializeField] private Vector3 cameraRelativeOffset =
        new Vector3(0.08f, -0.04f, 0.02f);
    [SerializeField] private Vector3 worldSpaceEuler =
        new Vector3(-30f, 0f, 0f);
    [SerializeField, Min(0.1f)] private float positionResponse = 42f;
    [SerializeField, Min(0.21f)] private float uiCanvasPlaneDistance = 0.35f;

    [Header("Cursor Alignment Guide")]
    [Tooltip(
        "Shows the exact unmodified pointer ray used by gameplay, before the hand offset is applied.")]
    [SerializeField] private bool showCursorDebugMarker = true;
    [SerializeField, Min(0.005f)] private float cursorDebugMarkerDiameter =
        0.05f;
    [SerializeField] private Color cursorDebugMarkerColor =
        new Color(0.1f, 1f, 0.95f, 1f);
    [SerializeField, Min(0f)] private float cursorDebugMarkerPullForward =
        0.04f;

    [Header("Held Chicken Alignment Guide")]
    [Tooltip(
        "Shows the Bone_Attach target while a chicken is held.")]
    [SerializeField] private bool showHeldChickenOffsetGuide = true;
    [SerializeField, Min(0.005f)] private float heldOffsetGuideDiameter =
        0.04f;
    [SerializeField, Min(0.001f)] private float heldOffsetGuideLineWidth =
        0.006f;
    [SerializeField] private Color heldOffsetGuideColor =
        new Color(1f, 0.12f, 0.7f, 1f);
    [Tooltip(
        "Renders the hand through the world camera while holding a chicken so the hand and chicken depth-sort around each other.")]
    [SerializeField] private bool worldDepthSortWhileHoldingChicken = true;
    [Tooltip(
        "While the Editor is paused, temporarily renders the real hand mesh on the Default layer for Scene view inspection.")]
    [SerializeField] private bool showHandInSceneViewWhenPaused = true;

    [Header("Shadow Presentation")]
    [SerializeField] private bool castWorldShadow = true;
    [SerializeField] private bool showUiShadow = true;
    [SerializeField] private Vector2 uiShadowOffsetPixels =
        new Vector2(8f, -8f);
    [SerializeField, Min(0f)] private float uiShadowBlurRadiusPixels = 3f;
    [SerializeField, Range(1, 9)] private int uiShadowSampleCount = 5;
    [SerializeField, Range(0f, 1f)] private float uiShadowOpacity = 0.22f;

    [Header("Movement Tilt Spring")]
    [SerializeField, Min(0f)] private float maximumTilt = 16f;
    [SerializeField, Min(1f)] private float pointerSpeedForMaximumTilt = 1500f;
    [SerializeField, Range(1f, 20f)] private float tiltSpringFrequency = 7f;
    [SerializeField, Range(0.05f, 2f)] private float tiltSpringDamping = 0.58f;
    [SerializeField, Range(0f, 1f)] private float horizontalYawInfluence = 0.35f;

    private static WorldHandCursorController instance;

    private Camera viewCamera;
    private Camera handCamera;
    private Camera stackedOnCamera;
    private GameObject presentationRoot;
    private Renderer[] handRenderers;
    private readonly List<Animator> handPoseAnimators =
        new List<Animator>();
    private Transform cursorDebugMarker;
    private Renderer cursorDebugMarkerRenderer;
    private Material cursorDebugMarkerMaterial;
    private Transform heldOffsetGuideMarker;
    private Renderer heldOffsetGuideRenderer;
    private LineRenderer heldOffsetGuideLine;
    private Material heldOffsetGuideMaterial;
    private Transform worldShadowRoot;
    private Renderer[] worldShadowRenderers = new Renderer[0];
    private readonly List<Transform> uiShadowRoots =
        new List<Transform>();
    private Renderer[] uiShadowRenderers = new Renderer[0];
    private readonly List<RaycastResult> uiRaycastResults =
        new List<RaycastResult>();
    private readonly Vector3[] uiWorldCorners = new Vector3[4];
    private EventSystem uiEventSystem;
    private PointerEventData uiPointerEventData;
    private MaterialPropertyBlock uiShadowProperties;
    private Color uiShadowSampleColor;
    private int handLayer = -1;
    private float nextCanvasRefreshTime;
    private Vector2 previousPointerPosition;
    private Vector3 springAngles;
    private Vector3 springAngularVelocity;
    private bool hasPointerPosition;
    private bool handVisible;
    private bool cursorDebugMarkerHasPosition;
    private int currentPoseState;
    private bool worldDepthSortingActive;

#if UNITY_EDITOR
    private bool editorSceneInspectionActive;
#endif

    public static bool TryGetHeldItemAttachPosition(out Vector3 position)
    {
        position = default;

        if (instance == null)
        {
            return false;
        }

        instance.ResolveHeldItemAttachPoint();

        if (instance.heldItemAttachPoint == null)
        {
            return false;
        }

        position = instance.heldItemAttachPoint.position;
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateCursor()
    {
        if (instance != null)
        {
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(CursorPrefabResource);

        if (prefab == null)
        {
            Debug.LogWarning(
                $"World hand cursor prefab was not found at Resources/{CursorPrefabResource}.");
            return;
        }

        Instantiate(prefab);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

#if UNITY_EDITOR
        EditorApplication.pauseStateChanged +=
            HandleEditorPauseStateChanged;
#endif

        viewCamera = Camera.main;
        handRenderers = visualRoot != null
            ? visualRoot.GetComponentsInChildren<Renderer>(true)
            : GetComponentsInChildren<Renderer>(true);
        handLayer = LayerMask.NameToLayer(HandLayerName);

        if (handLayer < 0)
        {
            Debug.LogError(
                $"The required {HandLayerName} layer is missing.");
            enabled = false;
            return;
        }

        SetLayerRecursively(
            visualRoot != null ? visualRoot.gameObject : gameObject,
            handLayer);

        foreach (Renderer handRenderer in handRenderers)
        {
            if (handRenderer == null)
            {
                continue;
            }

            handRenderer.shadowCastingMode = ShadowCastingMode.Off;
            handRenderer.receiveShadows = false;
            handRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
        }

        if (handAnimator != null)
        {
            handPoseAnimators.Add(handAnimator);
        }

        ResolveHeldItemAttachPoint();
        SetHandPose(PointPoseState, true);

        if (viewCamera != null)
        {
            viewCamera.cullingMask &= ~(1 << handLayer);
        }

        CreateOverlayPresentation(handLayer);
        CreateShadowPresentation();
        CreateCursorDebugMarker();
        CreateHeldOffsetGuide();
        ConvertOverlayCanvases();
        SetHandVisible(false, false);

#if UNITY_EDITOR
        SetEditorSceneInspection(
            showHandInSceneViewWhenPaused
            && EditorApplication.isPaused);
#endif
    }

    private void Update()
    {
        UpdateHandPose();
        UpdateHandWorldDepthSorting();
        Mouse mouse = GameplayTestBot.PointerMouse;

        if (viewCamera == null)
        {
            viewCamera = Camera.main;

            if (viewCamera != null)
            {
                viewCamera.cullingMask &= ~(1 << handLayer);
                AttachHandCameraToView();
            }
        }

        if (mouse == null || viewCamera == null)
        {
            SetHandVisible(false, false);
            return;
        }

        Vector2 pointerPosition = mouse.position.ReadValue();
        bool shouldShow = ShouldShowHand(pointerPosition);
        bool uiShadowVisible = shouldShow
            && TryUpdateUiShadowClip(pointerPosition);
        SyncHandCamera();

        if (Time.unscaledTime >= nextCanvasRefreshTime)
        {
            nextCanvasRefreshTime = Time.unscaledTime + 0.5f;
            ConvertOverlayCanvases();
        }

        SetHandVisible(shouldShow, uiShadowVisible);

        if (!shouldShow)
        {
            hasPointerPosition = false;
            RelaxSpring(Time.unscaledDeltaTime);
            return;
        }

        float frameDeltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
        float springDeltaTime = Mathf.Min(frameDeltaTime, 1f / 30f);
        Ray pointerRay = viewCamera.ScreenPointToRay(pointerPosition);
        Plane cursorPlane = new Plane(
            Vector3.up,
            new Vector3(0f, cursorPlaneHeight, 0f));

        if (cursorPlane.Raycast(pointerRay, out float distance))
        {
            UpdateCursorDebugMarker(pointerRay, distance);
            Vector3 targetPosition = pointerRay.GetPoint(distance)
                + viewCamera.transform.TransformVector(
                    cameraRelativeOffset);

            if (!hasPointerPosition)
            {
                transform.position = targetPosition;
            }
            else
            {
                float positionBlend = 1f
                    - Mathf.Exp(
                        -positionResponse
                        * Mathf.Min(frameDeltaTime, 0.1f));
                transform.position = Vector3.Lerp(
                    transform.position,
                    targetPosition,
                    positionBlend);
            }
        }
        else
        {
            SetCursorDebugMarkerPositionAvailable(false);
        }

        Vector2 pointerVelocity = hasPointerPosition
                && frameDeltaTime > 0.00001f
            ? (pointerPosition - previousPointerPosition) / frameDeltaTime
            : Vector2.zero;
        previousPointerPosition = pointerPosition;
        hasPointerPosition = true;

        Vector2 normalizedVelocity = Vector2.ClampMagnitude(
            pointerVelocity / Mathf.Max(1f, pointerSpeedForMaximumTilt),
            1f);
        Vector3 desiredAngles = new Vector3(
            -normalizedVelocity.y * maximumTilt,
            normalizedVelocity.x * maximumTilt * horizontalYawInfluence,
            -normalizedVelocity.x * maximumTilt);
        StepTiltSpring(desiredAngles, springDeltaTime);

        if (!IsFinite(springAngles))
        {
            springAngles = Vector3.zero;
            springAngularVelocity = Vector3.zero;
        }

        Quaternion worldSpaceRotation =
            Quaternion.Euler(worldSpaceEuler);
        transform.rotation = worldSpaceRotation
            * Quaternion.Euler(springAngles);
        UpdateShadowPresentation();
    }

    private void LateUpdate()
    {
        UpdateHandWorldDepthSorting();
        UpdateHeldChickenOffsetGuide();
    }

    private void ResolveHeldItemAttachPoint()
    {
        if (heldItemAttachPoint != null)
        {
            return;
        }

        Transform searchRoot = visualRoot != null
            ? visualRoot
            : transform;
        Transform[] descendants =
            searchRoot.GetComponentsInChildren<Transform>(true);

        foreach (Transform descendant in descendants)
        {
            if (descendant.name == "Bone_Attach")
            {
                heldItemAttachPoint = descendant;
                return;
            }
        }
    }

    private bool ShouldShowHand(Vector2 pointerPosition)
    {
        if (!Application.isFocused
            || pointerPosition.x < 0f
            || pointerPosition.y < 0f
            || pointerPosition.x > Screen.width
            || pointerPosition.y > Screen.height)
        {
            return false;
        }

        return true;
    }

    private void SetHandVisible(bool visible, bool uiShadowVisible)
    {
        bool sourceHandVisible = visible;

#if UNITY_EDITOR
        sourceHandVisible |= editorSceneInspectionActive;
#endif

        if (handVisible != visible
#if UNITY_EDITOR
            || editorSceneInspectionActive
#endif
            )
        {
            handVisible = visible;
            SetRenderersEnabled(handRenderers, sourceHandVisible);
        }

        SetRenderersEnabled(
            worldShadowRenderers,
            visible && castWorldShadow);
        SetRenderersEnabled(
            uiShadowRenderers,
            visible && showUiShadow && uiShadowVisible);

        if (cursorDebugMarkerRenderer != null)
        {
            cursorDebugMarkerRenderer.enabled = visible
                && showCursorDebugMarker
                && cursorDebugMarkerHasPosition;
        }

        if (handCamera != null)
        {
            handCamera.enabled = visible
                && !worldDepthSortingActive
#if UNITY_EDITOR
                && !editorSceneInspectionActive
#endif
                ;
        }

        Cursor.visible = !visible;
    }

    private void UpdateHandWorldDepthSorting()
    {
        bool shouldUseWorldDepth = worldDepthSortWhileHoldingChicken
            && EggCarryController.Instance != null
            && EggCarryController.Instance.HeldChicken != null;

        if (worldDepthSortingActive == shouldUseWorldDepth)
        {
            return;
        }

        worldDepthSortingActive = shouldUseWorldDepth;
        ApplySourceHandRenderMode();
    }

    private void ApplySourceHandRenderMode()
    {
        bool renderInWorld = worldDepthSortingActive;
        bool forceSourceVisible = false;

#if UNITY_EDITOR
        renderInWorld |= editorSceneInspectionActive;
        forceSourceVisible = editorSceneInspectionActive;
#endif

        if (visualRoot != null)
        {
            SetLayerRecursively(
                visualRoot.gameObject,
                renderInWorld ? 0 : handLayer);
        }

        SetRenderersEnabled(
            handRenderers,
            handVisible || forceSourceVisible);

        if (handCamera != null)
        {
            handCamera.enabled = handVisible && !renderInWorld;
        }
    }

    private void CreateOverlayPresentation(int handLayer)
    {
        presentationRoot = new GameObject("World Hand UI Presentation");

        GameObject cameraObject = new GameObject("Hand Render Camera");
        cameraObject.transform.SetParent(presentationRoot.transform, false);
        handCamera = cameraObject.AddComponent<Camera>();
        handCamera.clearFlags = CameraClearFlags.Nothing;
        handCamera.cullingMask = 1 << handLayer;
        handCamera.allowHDR = false;
        handCamera.allowMSAA = false;
        handCamera.useOcclusionCulling = false;

        UniversalAdditionalCameraData cameraData =
            handCamera.GetUniversalAdditionalCameraData();
        cameraData.renderType = CameraRenderType.Overlay;
        cameraData.renderPostProcessing = false;
        cameraData.requiresColorOption = CameraOverrideOption.Off;
        cameraData.requiresDepthOption = CameraOverrideOption.Off;

        SyncHandCamera();
        AttachHandCameraToView();
    }

    private void CreateCursorDebugMarker()
    {
        if (presentationRoot == null)
        {
            return;
        }

        GameObject marker = GameObject.CreatePrimitive(
            PrimitiveType.Sphere);
        marker.name = "Cursor Click Position Guide";
        marker.transform.SetParent(presentationRoot.transform, false);
        marker.transform.localScale =
            Vector3.one * cursorDebugMarkerDiameter;
        SetLayerRecursively(marker, handLayer);
        Collider markerCollider = marker.GetComponent<Collider>();

        if (markerCollider != null)
        {
            markerCollider.enabled = false;
            Destroy(markerCollider);
        }

        cursorDebugMarker = marker.transform;
        cursorDebugMarkerRenderer = marker.GetComponent<Renderer>();

        if (cursorDebugMarkerRenderer == null)
        {
            return;
        }

        Shader markerShader = Shader.Find(
            "Universal Render Pipeline/Unlit");

        if (markerShader == null)
        {
            markerShader = Shader.Find("Sprites/Default");
        }

        if (markerShader != null)
        {
            cursorDebugMarkerMaterial =
                new Material(markerShader)
                {
                    name = "Runtime Cursor Click Guide",
                    renderQueue = (int)RenderQueue.Overlay
                };

            if (cursorDebugMarkerMaterial.HasProperty("_BaseColor"))
            {
                cursorDebugMarkerMaterial.SetColor(
                    "_BaseColor",
                    cursorDebugMarkerColor);
            }

            if (cursorDebugMarkerMaterial.HasProperty("_Color"))
            {
                cursorDebugMarkerMaterial.SetColor(
                    "_Color",
                    cursorDebugMarkerColor);
            }

            cursorDebugMarkerRenderer.sharedMaterial =
                cursorDebugMarkerMaterial;
        }

        cursorDebugMarkerRenderer.shadowCastingMode =
            ShadowCastingMode.Off;
        cursorDebugMarkerRenderer.receiveShadows = false;
        cursorDebugMarkerRenderer.motionVectorGenerationMode =
            MotionVectorGenerationMode.ForceNoMotion;
        cursorDebugMarkerRenderer.enabled = false;
    }

    private void UpdateCursorDebugMarker(
        Ray pointerRay,
        float planeDistance)
    {
        if (cursorDebugMarker == null)
        {
            return;
        }

        float markerDistance = Mathf.Max(
            viewCamera.nearClipPlane + 0.01f,
            planeDistance - cursorDebugMarkerPullForward);
        cursorDebugMarker.position =
            pointerRay.GetPoint(markerDistance);
        SetCursorDebugMarkerPositionAvailable(true);
    }

    private void SetCursorDebugMarkerPositionAvailable(bool available)
    {
        cursorDebugMarkerHasPosition = available;

        if (cursorDebugMarkerRenderer != null)
        {
            cursorDebugMarkerRenderer.enabled = available
                && handVisible
                && showCursorDebugMarker;
        }
    }

    private void CreateHeldOffsetGuide()
    {
        if (presentationRoot == null)
        {
            return;
        }

        GameObject marker = GameObject.CreatePrimitive(
            PrimitiveType.Sphere);
        marker.name = "Held Chicken Position Guide";
        marker.transform.SetParent(presentationRoot.transform, false);
        marker.transform.localScale =
            Vector3.one * heldOffsetGuideDiameter;
        SetLayerRecursively(marker, handLayer);
        Collider markerCollider = marker.GetComponent<Collider>();

        if (markerCollider != null)
        {
            markerCollider.enabled = false;
            Destroy(markerCollider);
        }

        heldOffsetGuideMarker = marker.transform;
        heldOffsetGuideRenderer = marker.GetComponent<Renderer>();
        GameObject lineObject = new GameObject(
            "Bone Attach To Held Chicken Guide");
        lineObject.transform.SetParent(presentationRoot.transform, false);
        lineObject.layer = handLayer;
        heldOffsetGuideLine = lineObject.AddComponent<LineRenderer>();
        heldOffsetGuideLine.useWorldSpace = true;
        heldOffsetGuideLine.positionCount = 2;
        heldOffsetGuideLine.startWidth = heldOffsetGuideLineWidth;
        heldOffsetGuideLine.endWidth = heldOffsetGuideLineWidth;
        heldOffsetGuideLine.startColor = heldOffsetGuideColor;
        heldOffsetGuideLine.endColor = heldOffsetGuideColor;
        heldOffsetGuideLine.shadowCastingMode = ShadowCastingMode.Off;
        heldOffsetGuideLine.receiveShadows = false;
        heldOffsetGuideLine.motionVectorGenerationMode =
            MotionVectorGenerationMode.ForceNoMotion;

        Shader guideShader = Shader.Find(
            "Universal Render Pipeline/Unlit");

        if (guideShader == null)
        {
            guideShader = Shader.Find("Sprites/Default");
        }

        if (guideShader != null)
        {
            heldOffsetGuideMaterial = new Material(guideShader)
            {
                name = "Runtime Held Chicken Alignment Guide",
                renderQueue = (int)RenderQueue.Overlay
            };
            SetMaterialColor(
                heldOffsetGuideMaterial,
                heldOffsetGuideColor);

            if (heldOffsetGuideMaterial.HasProperty("_ZWrite"))
            {
                heldOffsetGuideMaterial.SetFloat("_ZWrite", 0f);
            }

            if (heldOffsetGuideMaterial.HasProperty("_ZTest"))
            {
                heldOffsetGuideMaterial.SetFloat(
                    "_ZTest",
                    (float)CompareFunction.Always);
            }

            if (heldOffsetGuideRenderer != null)
            {
                heldOffsetGuideRenderer.sharedMaterial =
                    heldOffsetGuideMaterial;
            }

            heldOffsetGuideLine.sharedMaterial =
                heldOffsetGuideMaterial;
        }

        if (heldOffsetGuideRenderer != null)
        {
            heldOffsetGuideRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            heldOffsetGuideRenderer.receiveShadows = false;
            heldOffsetGuideRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            heldOffsetGuideRenderer.enabled = false;
        }

        heldOffsetGuideLine.enabled = false;
    }

    private void UpdateHeldChickenOffsetGuide()
    {
        ChickenController heldChicken =
            EggCarryController.Instance != null
                ? EggCarryController.Instance.HeldChicken
                : null;
        bool visible = showHeldChickenOffsetGuide
            && (handVisible
#if UNITY_EDITOR
                || editorSceneInspectionActive
#endif
                )
            && heldChicken != null
            && heldItemAttachPoint != null;

        if (!visible)
        {
            if (heldOffsetGuideRenderer != null)
            {
                heldOffsetGuideRenderer.enabled = false;
            }

            if (heldOffsetGuideLine != null)
            {
                heldOffsetGuideLine.enabled = false;
            }

            return;
        }

        Vector3 attachPosition = heldItemAttachPoint.position;
        Vector3 targetPosition = attachPosition;

        if (heldOffsetGuideMarker != null)
        {
            heldOffsetGuideMarker.position = targetPosition;
            heldOffsetGuideMarker.localScale =
                Vector3.one * heldOffsetGuideDiameter;
        }

        if (heldOffsetGuideRenderer != null)
        {
            heldOffsetGuideRenderer.enabled = true;
        }

        if (heldOffsetGuideLine != null)
        {
            heldOffsetGuideLine.startWidth = heldOffsetGuideLineWidth;
            heldOffsetGuideLine.endWidth = heldOffsetGuideLineWidth;
            heldOffsetGuideLine.SetPosition(0, attachPosition);
            heldOffsetGuideLine.SetPosition(1, targetPosition);
            heldOffsetGuideLine.enabled = true;
        }
    }

    private static void SetMaterialColor(
        Material material,
        Color color)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private void CreateShadowPresentation()
    {
        if (presentationRoot == null || visualRoot == null)
        {
            return;
        }

        if (castWorldShadow)
        {
            worldShadowRoot = CreateVisualProxy(
                "World Shadow Proxy",
                0,
                null,
                ShadowCastingMode.ShadowsOnly,
                out worldShadowRenderers);
        }

        if (!showUiShadow)
        {
            return;
        }

        Material shadowMaterial =
            Resources.Load<Material>(UiShadowMaterialResource);

        if (shadowMaterial == null)
        {
            Debug.LogWarning(
                $"UI hand shadow material was not found at Resources/{UiShadowMaterialResource}.");
            return;
        }

        int sampleCount = Mathf.Clamp(uiShadowSampleCount, 1, 9);
        List<Renderer> shadowRenderers = new List<Renderer>();
        Color shadowColor = shadowMaterial.HasProperty(ShadowColorId)
            ? shadowMaterial.GetColor(ShadowColorId)
            : Color.black;
        float combinedOpacity = Mathf.Clamp01(
            shadowColor.a * uiShadowOpacity);
        float perSampleOpacity = 1f - Mathf.Pow(
            1f - combinedOpacity,
            1f / sampleCount);
        shadowColor.a = perSampleOpacity;
        uiShadowSampleColor = shadowColor;
        uiShadowProperties = new MaterialPropertyBlock();
        uiShadowProperties.SetColor(ShadowColorId, shadowColor);

        for (int sample = 0; sample < sampleCount; sample++)
        {
            Transform shadowRoot = CreateVisualProxy(
                $"UI Shadow Proxy {sample + 1}",
                handLayer,
                shadowMaterial,
                ShadowCastingMode.Off,
                out Renderer[] renderers);
            uiShadowRoots.Add(shadowRoot);

            foreach (Renderer shadowRenderer in renderers)
            {
                shadowRenderer.SetPropertyBlock(uiShadowProperties);
                shadowRenderers.Add(shadowRenderer);
            }
        }

        uiShadowRenderers = shadowRenderers.ToArray();
    }

    private bool TryUpdateUiShadowClip(Vector2 pointerPosition)
    {
        EventSystem currentEventSystem = EventSystem.current;

        if (!showUiShadow
            || uiShadowRenderers.Length == 0
            || currentEventSystem == null)
        {
            return false;
        }

        if (uiEventSystem != currentEventSystem
            || uiPointerEventData == null)
        {
            uiEventSystem = currentEventSystem;
            uiPointerEventData =
                new PointerEventData(currentEventSystem);
        }

        uiPointerEventData.Reset();
        uiPointerEventData.position = pointerPosition;
        uiRaycastResults.Clear();
        currentEventSystem.RaycastAll(
            uiPointerEventData,
            uiRaycastResults);

        foreach (RaycastResult raycastResult in uiRaycastResults)
        {
            if (raycastResult.gameObject == null)
            {
                continue;
            }

            Selectable selectable =
                raycastResult.gameObject.GetComponentInParent<Selectable>();
            RectTransform targetRect = selectable != null
                && selectable.targetGraphic != null
                    ? selectable.targetGraphic.rectTransform
                    : raycastResult.gameObject.transform as RectTransform;

            if (targetRect == null)
            {
                continue;
            }

            Canvas targetCanvas =
                targetRect.GetComponentInParent<Canvas>();
            Camera canvasCamera = targetCanvas == null
                || targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : targetCanvas.worldCamera != null
                        ? targetCanvas.worldCamera
                        : viewCamera;
            targetRect.GetWorldCorners(uiWorldCorners);
            Vector2 minimum = new Vector2(
                float.PositiveInfinity,
                float.PositiveInfinity);
            Vector2 maximum = new Vector2(
                float.NegativeInfinity,
                float.NegativeInfinity);

            for (int corner = 0; corner < uiWorldCorners.Length; corner++)
            {
                Vector2 screenCorner =
                    RectTransformUtility.WorldToScreenPoint(
                        canvasCamera,
                        uiWorldCorners[corner]);
                minimum = Vector2.Min(minimum, screenCorner);
                maximum = Vector2.Max(maximum, screenCorner);
            }

            minimum.x = Mathf.Clamp(minimum.x, 0f, Screen.width);
            minimum.y = Mathf.Clamp(minimum.y, 0f, Screen.height);
            maximum.x = Mathf.Clamp(maximum.x, 0f, Screen.width);
            maximum.y = Mathf.Clamp(maximum.y, 0f, Screen.height);

            if (maximum.x - minimum.x < 1f
                || maximum.y - minimum.y < 1f)
            {
                continue;
            }

            Vector4 normalizedClipRect = new Vector4(
                minimum.x / Mathf.Max(1f, Screen.width),
                minimum.y / Mathf.Max(1f, Screen.height),
                maximum.x / Mathf.Max(1f, Screen.width),
                maximum.y / Mathf.Max(1f, Screen.height));
            uiShadowProperties.Clear();
            uiShadowProperties.SetColor(
                ShadowColorId,
                uiShadowSampleColor);
            uiShadowProperties.SetVector(
                UiClipRectId,
                normalizedClipRect);

            foreach (Renderer shadowRenderer in uiShadowRenderers)
            {
                if (shadowRenderer != null)
                {
                    shadowRenderer.SetPropertyBlock(
                        uiShadowProperties);
                }
            }

            return true;
        }

        return false;
    }

    private Transform CreateVisualProxy(
        string proxyName,
        int layer,
        Material overrideMaterial,
        ShadowCastingMode shadowCastingMode,
        out Renderer[] renderers)
    {
        Transform proxyRoot = new GameObject(proxyName).transform;
        proxyRoot.SetParent(presentationRoot.transform, false);
        GameObject proxyVisual = Instantiate(
            visualRoot.gameObject,
            proxyRoot,
            false);
        proxyVisual.name = "Visual";
        SetLayerRecursively(proxyVisual, layer);
        renderers = proxyVisual.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer proxyRenderer in renderers)
        {
            proxyRenderer.shadowCastingMode = shadowCastingMode;
            proxyRenderer.receiveShadows = false;
            proxyRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            if (proxyRenderer is SkinnedMeshRenderer skinnedRenderer)
            {
                skinnedRenderer.updateWhenOffscreen = true;
            }

            if (overrideMaterial != null)
            {
                Material[] materials = proxyRenderer.sharedMaterials;

                for (int index = 0; index < materials.Length; index++)
                {
                    materials[index] = overrideMaterial;
                }

                proxyRenderer.sharedMaterials = materials;
            }
        }

        Animator proxyAnimator =
            proxyVisual.GetComponentInChildren<Animator>(true);

        if (proxyAnimator != null)
        {
            handPoseAnimators.Add(proxyAnimator);
            PlayHandPose(
                proxyAnimator,
                currentPoseState,
                true);
        }

        return proxyRoot;
    }

    private void UpdateHandPose()
    {
        EggCarryController carryController =
            EggCarryController.Instance;
        int desiredPose = PointPoseState;

        if (carryController != null)
        {
            if (carryController.HeldEgg != null)
            {
                desiredPose = EggHoldPoseState;
            }
            else if (carryController.HeldChicken != null)
            {
                desiredPose = ChickenHoldPoseState;
            }
            else if (carryController.IsHoveringGrabbableEgg)
            {
                desiredPose = EggReadyPoseState;
            }
            else if (carryController.IsHoveringGrabbableChicken)
            {
                desiredPose = ChickenReadyPoseState;
            }
        }

        SetHandPose(desiredPose, false);
    }

    private void SetHandPose(int stateHash, bool immediate)
    {
        if (!immediate && currentPoseState == stateHash)
        {
            return;
        }

        currentPoseState = stateHash;

        for (int index = handPoseAnimators.Count - 1;
             index >= 0;
             index--)
        {
            Animator poseAnimator = handPoseAnimators[index];

            if (poseAnimator == null)
            {
                handPoseAnimators.RemoveAt(index);
                continue;
            }

            PlayHandPose(poseAnimator, stateHash, immediate);
        }
    }

    private void PlayHandPose(
        Animator poseAnimator,
        int stateHash,
        bool immediate)
    {
        if (!poseAnimator.HasState(0, stateHash))
        {
            return;
        }

        if (immediate || poseTransitionDuration <= 0f)
        {
            poseAnimator.Play(stateHash, 0, 0f);
            return;
        }

        poseAnimator.CrossFadeInFixedTime(
            stateHash,
            poseTransitionDuration,
            0,
            0f);
    }

    private void UpdateShadowPresentation()
    {
        if (worldShadowRoot != null)
        {
            worldShadowRoot.SetPositionAndRotation(
                transform.position,
                transform.rotation);
        }

        if (viewCamera == null || uiShadowRoots.Count == 0)
        {
            return;
        }

        Vector3 handScreenPosition =
            viewCamera.WorldToScreenPoint(transform.position);
        int sampleCount = uiShadowRoots.Count;

        for (int sample = 0; sample < sampleCount; sample++)
        {
            Vector2 blurOffset = Vector2.zero;

            if (sample > 0)
            {
                float angle = Mathf.PI * 2f
                    * (sample - 1)
                    / Mathf.Max(1, sampleCount - 1);
                blurOffset = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle))
                    * uiShadowBlurRadiusPixels;
            }

            Vector3 shadowScreenPosition = handScreenPosition;
            shadowScreenPosition.x +=
                uiShadowOffsetPixels.x + blurOffset.x;
            shadowScreenPosition.y +=
                uiShadowOffsetPixels.y + blurOffset.y;
            Transform shadowRoot = uiShadowRoots[sample];
            shadowRoot.SetPositionAndRotation(
                viewCamera.ScreenToWorldPoint(shadowScreenPosition),
                transform.rotation);
        }
    }

    private static void SetRenderersEnabled(
        Renderer[] renderers,
        bool enabled)
    {
        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer != null)
            {
                targetRenderer.enabled = enabled;
            }
        }
    }

    private void SyncHandCamera()
    {
        if (handCamera == null || viewCamera == null)
        {
            return;
        }

        handCamera.transform.SetPositionAndRotation(
            viewCamera.transform.position,
            viewCamera.transform.rotation);
        handCamera.orthographic = viewCamera.orthographic;
        handCamera.orthographicSize = viewCamera.orthographicSize;
        handCamera.fieldOfView = viewCamera.fieldOfView;
        handCamera.nearClipPlane = viewCamera.nearClipPlane;
        handCamera.farClipPlane = viewCamera.farClipPlane;
        handCamera.aspect = viewCamera.aspect;
        handCamera.usePhysicalProperties = viewCamera.usePhysicalProperties;
        handCamera.focalLength = viewCamera.focalLength;
        handCamera.sensorSize = viewCamera.sensorSize;
        handCamera.lensShift = viewCamera.lensShift;
        handCamera.projectionMatrix = viewCamera.projectionMatrix;
    }

    private void AttachHandCameraToView()
    {
        if (handCamera == null
            || viewCamera == null
            || stackedOnCamera == viewCamera)
        {
            return;
        }

        if (stackedOnCamera != null)
        {
            UniversalAdditionalCameraData previousData =
                stackedOnCamera.GetUniversalAdditionalCameraData();
            previousData.cameraStack.Remove(handCamera);
        }

        UniversalAdditionalCameraData viewData =
            viewCamera.GetUniversalAdditionalCameraData();

        if (!viewData.cameraStack.Contains(handCamera))
        {
            viewData.cameraStack.Add(handCamera);
        }

        stackedOnCamera = viewCamera;
    }

    private void ConvertOverlayCanvases()
    {
        if (viewCamera == null)
        {
            return;
        }

        Canvas[] canvases = FindObjectsByType<Canvas>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (Canvas canvas in canvases)
        {
            if (canvas == null
                || !canvas.isRootCanvas
                || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                continue;
            }

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = viewCamera;
            canvas.planeDistance = Mathf.Max(
                viewCamera.nearClipPlane + 0.01f,
                uiCanvasPlaneDistance);
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null)
        {
            return;
        }

        root.layer = layer;

        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private void StepTiltSpring(Vector3 desiredAngles, float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        if (!IsFinite(desiredAngles)
            || !IsFinite(springAngles)
            || !IsFinite(springAngularVelocity))
        {
            springAngles = Vector3.zero;
            springAngularVelocity = Vector3.zero;
            desiredAngles = Vector3.zero;
        }

        const float Tau = Mathf.PI * 2f;
        float angularFrequency = Tau * tiltSpringFrequency;
        float frequencySquared =
            angularFrequency * angularFrequency;
        float dampingTerm = 1f
            + 2f
            * deltaTime
            * tiltSpringDamping
            * angularFrequency;
        float velocityToPosition =
            deltaTime * frequencySquared;
        float positionToPosition =
            deltaTime * velocityToPosition;
        float inverseDeterminant = 1f
            / (dampingTerm + positionToPosition);
        Vector3 previousAngles = springAngles;
        Vector3 previousVelocity = springAngularVelocity;

        // Implicit integration remains stable even when a frame stalls.
        springAngles = (
            previousAngles * dampingTerm
            + previousVelocity * deltaTime
            + desiredAngles * positionToPosition)
            * inverseDeterminant;
        springAngularVelocity = (
            previousVelocity
            + (desiredAngles - previousAngles)
            * velocityToPosition)
            * inverseDeterminant;

        float maximumSafeAngle = Mathf.Max(
            1f,
            maximumTilt * 2f);
        springAngles = Vector3.ClampMagnitude(
            springAngles,
            maximumSafeAngle);
        springAngularVelocity = Vector3.ClampMagnitude(
            springAngularVelocity,
            maximumSafeAngle * angularFrequency * 2f);
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x)
            && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y)
            && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z)
            && !float.IsInfinity(value.z);
    }

    private void RelaxSpring(float deltaTime)
    {
        StepTiltSpring(Vector3.zero, Mathf.Min(deltaTime, 1f / 30f));
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            SetHandVisible(false, false);
        }
    }

    private void OnDisable()
    {
        Cursor.visible = true;

#if UNITY_EDITOR
        EditorApplication.pauseStateChanged -=
            HandleEditorPauseStateChanged;
#endif
    }

    private void OnDestroy()
    {
        Cursor.visible = true;

#if UNITY_EDITOR
        EditorApplication.pauseStateChanged -=
            HandleEditorPauseStateChanged;
#endif

        if (stackedOnCamera != null && handCamera != null)
        {
            UniversalAdditionalCameraData viewData =
                stackedOnCamera.GetUniversalAdditionalCameraData();
            viewData.cameraStack.Remove(handCamera);
            stackedOnCamera = null;
        }

        if (presentationRoot != null)
        {
            Destroy(presentationRoot);
            presentationRoot = null;
        }

        if (cursorDebugMarkerMaterial != null)
        {
            Destroy(cursorDebugMarkerMaterial);
            cursorDebugMarkerMaterial = null;
        }

        if (heldOffsetGuideMaterial != null)
        {
            Destroy(heldOffsetGuideMaterial);
            heldOffsetGuideMaterial = null;
        }

        if (instance == this)
        {
            instance = null;
        }
    }

#if UNITY_EDITOR
    private void HandleEditorPauseStateChanged(PauseState pauseState)
    {
        SetEditorSceneInspection(
            showHandInSceneViewWhenPaused
            && pauseState == PauseState.Paused);
        SceneView.RepaintAll();
    }

    private void SetEditorSceneInspection(bool active)
    {
        if (editorSceneInspectionActive == active)
        {
            return;
        }

        editorSceneInspectionActive = active;
        ApplySourceHandRenderMode();
        int inspectionLayer = active ? 0 : handLayer;

        if (heldOffsetGuideMarker != null)
        {
            SetLayerRecursively(
                heldOffsetGuideMarker.gameObject,
                inspectionLayer);
        }

        if (heldOffsetGuideLine != null)
        {
            heldOffsetGuideLine.gameObject.layer = inspectionLayer;
        }

    }
#endif

    private void OnValidate()
    {
        cursorPlaneHeight = Mathf.Max(0f, cursorPlaneHeight);
        positionResponse = Mathf.Max(0.1f, positionResponse);
        uiCanvasPlaneDistance = Mathf.Max(0.21f, uiCanvasPlaneDistance);
        poseTransitionDuration = Mathf.Max(0f, poseTransitionDuration);
        cursorDebugMarkerDiameter = Mathf.Max(
            0.005f,
            cursorDebugMarkerDiameter);
        cursorDebugMarkerPullForward = Mathf.Max(
            0f,
            cursorDebugMarkerPullForward);
        heldOffsetGuideDiameter = Mathf.Max(
            0.005f,
            heldOffsetGuideDiameter);
        heldOffsetGuideLineWidth = Mathf.Max(
            0.001f,
            heldOffsetGuideLineWidth);
        uiShadowBlurRadiusPixels = Mathf.Max(
            0f,
            uiShadowBlurRadiusPixels);
        uiShadowSampleCount = Mathf.Clamp(uiShadowSampleCount, 1, 9);
        uiShadowOpacity = Mathf.Clamp01(uiShadowOpacity);
        maximumTilt = Mathf.Max(0f, maximumTilt);
        pointerSpeedForMaximumTilt =
            Mathf.Max(1f, pointerSpeedForMaximumTilt);
        tiltSpringFrequency = Mathf.Max(1f, tiltSpringFrequency);
        tiltSpringDamping = Mathf.Max(0.05f, tiltSpringDamping);
    }
}
