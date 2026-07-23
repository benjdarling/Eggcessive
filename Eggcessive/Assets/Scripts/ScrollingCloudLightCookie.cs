using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public sealed class ScrollingCloudLightCookie : MonoBehaviour
{
    private static readonly int ScrollOffsetProperty = Shader.PropertyToID("_ScrollOffset");

    [Header("Cookie")]
    [SerializeField] private Material cookieGeneratorMaterial = null;
    [SerializeField, Min(1f)] private float cookieWorldSize = 18f;
    [Tooltip("Quality only: changes the generated cookie's pixel dimensions, not the cloud scale.")]
    [SerializeField, Range(64, 1024)] private int textureResolution = 256;
    [SerializeField, Range(1f, 60f)] private float updatesPerSecond = 30f;

    [Header("Movement")]
    [Tooltip("Cookie UVs travelled per second. One UV unit is one complete cookie tile.")]
    [SerializeField] private Vector2 scrollSpeed = new Vector2(0.006f, 0.0025f);
    [SerializeField] private bool animateInEditMode = true;

    private Light targetLight;
    private UniversalAdditionalLightData urpLightData;
    private Material runtimeMaterial;
    private Material runtimeMaterialSource;
    private RenderTexture cookieTexture;
    private Texture originalCookie;
    private Vector2 originalCookieSize;
    private Vector2 originalUrpCookieSize;
    private bool originalCookieCached;
    private int activeResolution;
    private double lastRenderTime = double.NegativeInfinity;

#if UNITY_EDITOR
    private static readonly HashSet<ScrollingCloudLightCookie> EditModeInstances =
        new HashSet<ScrollingCloudLightCookie>();

    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorUpdate()
    {
        UnityEditor.EditorApplication.update -= UpdateEditModeInstances;
        UnityEditor.EditorApplication.update += UpdateEditModeInstances;
    }

    private static void UpdateEditModeInstances()
    {
        if (Application.isPlaying)
        {
            return;
        }

        double time = UnityEditor.EditorApplication.timeSinceStartup;
        bool rendered = false;

        EditModeInstances.RemoveWhere(instance => instance == null);
        foreach (ScrollingCloudLightCookie instance in EditModeInstances)
        {
            if (!instance.isActiveAndEnabled || !instance.animateInEditMode)
            {
                continue;
            }

            rendered |= instance.RenderCookie(time);
        }

        if (rendered)
        {
            UnityEditor.SceneView.RepaintAll();
        }
    }
#endif

    private void OnEnable()
    {
        targetLight = GetComponent<Light>();
        CacheOriginalLightSettings();

#if UNITY_EDITOR
        EditModeInstances.Add(this);
#endif

        lastRenderTime = double.NegativeInfinity;
        RenderCookie(GetCurrentTime());
    }

    private void Update()
    {
        if (Application.isPlaying)
        {
            RenderCookie(Time.unscaledTimeAsDouble);
        }
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditModeInstances.Remove(this);
#endif

        RestoreOriginalCookie();
        ReleaseResources();
    }

    private void OnDestroy()
    {
        RestoreOriginalCookie();
        ReleaseResources();
    }

    private void OnValidate()
    {
        cookieWorldSize = Mathf.Max(1f, cookieWorldSize);
        textureResolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(textureResolution, 64, 1024));
        updatesPerSecond = Mathf.Clamp(updatesPerSecond, 1f, 60f);
        lastRenderTime = double.NegativeInfinity;

        if (isActiveAndEnabled)
        {
            RenderCookie(GetCurrentTime());
        }
    }

    private bool RenderCookie(double time)
    {
        // Editor time is much larger than a newly started Play-mode clock.
        // Without resetting here, entering Play with domain reload disabled can
        // leave the cookie waiting thousands of seconds before it updates.
        if (time < lastRenderTime)
        {
            lastRenderTime = double.NegativeInfinity;
        }

        double interval = 1.0 / Mathf.Max(1f, updatesPerSecond);
        if (time - lastRenderTime < interval)
        {
            return false;
        }

        if (!EnsureResources())
        {
            return false;
        }

        runtimeMaterial.CopyPropertiesFromMaterial(cookieGeneratorMaterial);

        Vector2 offset = scrollSpeed * (float)time;
        offset.x = Mathf.Repeat(offset.x, 1f);
        offset.y = Mathf.Repeat(offset.y, 1f);
        runtimeMaterial.SetVector(ScrollOffsetProperty, offset);

        RenderTexture previousActive = RenderTexture.active;
        Graphics.Blit(Texture2D.whiteTexture, cookieTexture, runtimeMaterial);
        RenderTexture.active = previousActive;
        targetLight.cookie = cookieTexture;
        ApplyCookieProjectionSize();
        lastRenderTime = time;
        return true;
    }

    private bool EnsureResources()
    {
        if (targetLight == null)
        {
            targetLight = GetComponent<Light>();
        }

        if (targetLight == null || targetLight.type != LightType.Directional)
        {
            return false;
        }

        CacheOriginalLightSettings();

        if (cookieGeneratorMaterial == null || cookieGeneratorMaterial.shader == null)
        {
            return false;
        }

        if (runtimeMaterial == null
            || runtimeMaterialSource != cookieGeneratorMaterial
            || runtimeMaterial.shader != cookieGeneratorMaterial.shader)
        {
            DestroyRuntimeObject(runtimeMaterial);
            runtimeMaterial = new Material(cookieGeneratorMaterial)
            {
                name = $"{cookieGeneratorMaterial.name} (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            runtimeMaterialSource = cookieGeneratorMaterial;
        }

        if (cookieTexture == null || activeResolution != textureResolution)
        {
            if (cookieTexture != null)
            {
                DestroyCookieTexture();
            }

            cookieTexture = new RenderTexture(
                textureResolution,
                textureResolution,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = $"Scrolling Cloud Directional Light Cookie ({textureResolution}x{textureResolution})",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false
            };
            cookieTexture.Create();
            activeResolution = textureResolution;
        }

        return true;
    }

    private void RestoreOriginalCookie()
    {
        if (targetLight == null || !originalCookieCached)
        {
            return;
        }

        if (targetLight.cookie == cookieTexture)
        {
            targetLight.cookie = originalCookie;
            targetLight.cookieSize2D = originalCookieSize;
            if (urpLightData != null)
            {
                urpLightData.lightCookieSize = originalUrpCookieSize;
            }
        }
    }

    private void ApplyCookieProjectionSize()
    {
        Vector2 size = Vector2.one * cookieWorldSize;
        if (targetLight.cookieSize2D != size)
        {
            targetLight.cookieSize2D = size;
        }

        if (urpLightData == null)
        {
            urpLightData = targetLight.GetUniversalAdditionalLightData();
        }

        if (urpLightData.lightCookieSize != size)
        {
            urpLightData.lightCookieSize = size;
        }
    }

    private void CacheOriginalLightSettings()
    {
        if (targetLight == null || originalCookieCached)
        {
            return;
        }

        originalCookie = targetLight.cookie;
        originalCookieSize = targetLight.cookieSize2D;
        urpLightData = targetLight.GetUniversalAdditionalLightData();
        originalUrpCookieSize = urpLightData.lightCookieSize;
        originalCookieCached = true;
    }

    private void ReleaseResources()
    {
        if (cookieTexture != null)
        {
            DestroyCookieTexture();
        }

        DestroyRuntimeObject(runtimeMaterial);
        runtimeMaterial = null;
        runtimeMaterialSource = null;
        urpLightData = null;
        activeResolution = 0;
        originalCookieCached = false;
    }

    private void DestroyCookieTexture()
    {
        if (cookieTexture == null)
        {
            return;
        }

        if (RenderTexture.active == cookieTexture)
        {
            RenderTexture.active = null;
        }

        cookieTexture.Release();
        DestroyRuntimeObject(cookieTexture);
        cookieTexture = null;
    }

    private static double GetCurrentTime()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            return UnityEditor.EditorApplication.timeSinceStartup;
        }
#endif
        return Time.unscaledTimeAsDouble;
    }

    private static void DestroyRuntimeObject(Object runtimeObject)
    {
        if (runtimeObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(runtimeObject);
        }
        else
        {
            DestroyImmediate(runtimeObject);
        }
    }
}
