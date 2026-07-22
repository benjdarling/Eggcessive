using System;
using UnityEngine;

[DefaultExecutionOrder(-500)]
[DisallowMultipleComponent]
public sealed class GlobalWind : MonoBehaviour
{
    public struct WindSample
    {
        public Vector3 steady;
        public Vector3 gust;
        public Vector3 turbulence;

        public Vector3 Total => steady + gust + turbulence;
    }

    [Serializable]
    public struct NoiseLayer
    {
        [Min(0.001f)] public float spatialScale;
        [Min(0f)] public float scrollSpeed;
        [Min(0f)] public float strengthVariation;
        [Min(0f)] public float sidewaysTurbulence;
        [Min(0f)] public float verticalTurbulence;
    }

    private static readonly int WindDirectionId = Shader.PropertyToID("_GlobalWindDirection");
    private static readonly int WindVectorId = Shader.PropertyToID("_GlobalWindVector");
    private static readonly int WindTimeId = Shader.PropertyToID("_GlobalWindTime");
    private static readonly int WindLayerCountId = Shader.PropertyToID("_GlobalWindLayerCount");
    private static readonly int WindLayerSpatialId = Shader.PropertyToID("_GlobalWindLayerSpatial");
    private static readonly int WindLayerAmplitudeId = Shader.PropertyToID("_GlobalWindLayerAmplitude");

    private static GlobalWind instance;

    [Header("Base Wind")]
    [SerializeField] private Vector3 direction = new Vector3(1f, 0f, 0.3f);
    [SerializeField, Min(0f)] private float baseStrength = 0.65f;

    [Header("Moving Noise Layers")]
    [SerializeField] private NoiseLayer[] noiseLayers = CreateDefaultLayers();

    private Vector4[] shaderLayerSpatial;
    private Vector4[] shaderLayerAmplitude;

    public static bool IsAvailable => instance != null && instance.isActiveAndEnabled;

    public static Vector3 SampleWind(Vector3 worldPosition)
    {
        return SampleWindDetailed(worldPosition, Time.time).Total;
    }

    public static Vector3 SampleWind(Vector3 worldPosition, float time)
    {
        return SampleWindDetailed(worldPosition, time).Total;
    }

    public static WindSample SampleWindDetailed(Vector3 worldPosition)
    {
        return SampleWindDetailed(worldPosition, Time.time);
    }

    public static WindSample SampleWindDetailed(Vector3 worldPosition, float time)
    {
        return IsAvailable ? instance.Sample(worldPosition, time) : default;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (instance != null)
        {
            return;
        }

        GameObject windObject = new GameObject("[Global Wind]");
        windObject.AddComponent<GlobalWind>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            Destroy(this);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        RefreshShaderLayerData();
    }

    private void OnEnable()
    {
        if (instance == null)
        {
            instance = this;
        }

        PublishShaderGlobals();
    }

    private void Update()
    {
        PublishShaderGlobals();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private WindSample Sample(Vector3 worldPosition, float time)
    {
        Vector3 windDirection = GetHorizontalDirection();
        Vector3 sidewaysDirection = Vector3.Cross(Vector3.up, windDirection).normalized;
        Vector2 position = new Vector2(worldPosition.x, worldPosition.z);
        Vector2 travelDirection = new Vector2(windDirection.x, windDirection.z);

        float gustAmount = 0f;
        float sidewaysAmount = 0f;
        float verticalAmount = 0f;

        int layerCount = noiseLayers != null ? noiseLayers.Length : 0;
        for (int i = 0; i < layerCount; i++)
        {
            NoiseLayer layer = noiseLayers[i];
            Vector2 coordinates = position * layer.spatialScale
                - travelDirection * (time * layer.scrollSpeed);
            float seed = 19.19f + i * 37.71f;

            float gust = SignedPerlin(coordinates.x + seed, coordinates.y - seed * 0.31f);
            float sideways = SignedPerlin(
                coordinates.x - seed * 0.73f,
                coordinates.y + seed * 1.17f);
            float vertical = SignedPerlin(
                coordinates.x + seed * 1.91f,
                coordinates.y + seed * 0.53f);

            gustAmount += gust * layer.strengthVariation;
            sidewaysAmount += sideways * layer.sidewaysTurbulence;
            verticalAmount += vertical * layer.verticalTurbulence;
        }

        gustAmount = Mathf.Max(-0.95f, gustAmount);
        return new WindSample
        {
            steady = windDirection * baseStrength,
            gust = windDirection * (baseStrength * gustAmount),
            turbulence = sidewaysDirection * (baseStrength * sidewaysAmount)
                + Vector3.up * (baseStrength * verticalAmount)
        };
    }

    private void PublishShaderGlobals()
    {
        Vector3 windDirection = GetHorizontalDirection();
        Vector3 windAtOrigin = Sample(Vector3.zero, Time.time).Total;

        Shader.SetGlobalVector(
            WindDirectionId,
            new Vector4(windDirection.x, windDirection.y, windDirection.z, baseStrength));
        Shader.SetGlobalVector(
            WindVectorId,
            new Vector4(windAtOrigin.x, windAtOrigin.y, windAtOrigin.z, windAtOrigin.magnitude));
        Shader.SetGlobalFloat(WindTimeId, Time.time);

        int layerCount = noiseLayers != null ? noiseLayers.Length : 0;
        if (shaderLayerSpatial == null || shaderLayerSpatial.Length != layerCount)
        {
            RefreshShaderLayerData();
        }

        Shader.SetGlobalInt(WindLayerCountId, layerCount);
        if (layerCount > 0)
        {
            Shader.SetGlobalVectorArray(WindLayerSpatialId, shaderLayerSpatial);
            Shader.SetGlobalVectorArray(WindLayerAmplitudeId, shaderLayerAmplitude);
        }
    }

    private void RefreshShaderLayerData()
    {
        int layerCount = noiseLayers != null ? noiseLayers.Length : 0;
        shaderLayerSpatial = new Vector4[layerCount];
        shaderLayerAmplitude = new Vector4[layerCount];

        for (int i = 0; i < layerCount; i++)
        {
            NoiseLayer layer = noiseLayers[i];
            shaderLayerSpatial[i] = new Vector4(
                layer.spatialScale,
                layer.scrollSpeed,
                19.19f + i * 37.71f,
                0f);
            shaderLayerAmplitude[i] = new Vector4(
                layer.strengthVariation,
                layer.sidewaysTurbulence,
                layer.verticalTurbulence,
                0f);
        }
    }

    private Vector3 GetHorizontalDirection()
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
        return horizontal.sqrMagnitude > 0.000001f ? horizontal.normalized : Vector3.right;
    }

    private static float SignedPerlin(float x, float y)
    {
        return Mathf.PerlinNoise(x, y) * 2f - 1f;
    }

    private static NoiseLayer[] CreateDefaultLayers()
    {
        return new[]
        {
            new NoiseLayer
            {
                spatialScale = 0.18f,
                scrollSpeed = 0.16f,
                strengthVariation = 0.55f,
                sidewaysTurbulence = 0.15f,
                verticalTurbulence = 0.04f
            },
            new NoiseLayer
            {
                spatialScale = 0.9f,
                scrollSpeed = 0.75f,
                strengthVariation = 0.35f,
                sidewaysTurbulence = 0.35f,
                verticalTurbulence = 0.12f
            },
            new NoiseLayer
            {
                spatialScale = 3.2f,
                scrollSpeed = 2.2f,
                strengthVariation = 0.22f,
                sidewaysTurbulence = 0.3f,
                verticalTurbulence = 0.15f
            }
        };
    }

    private void OnValidate()
    {
        baseStrength = Mathf.Max(0f, baseStrength);
        if (Vector3.ProjectOnPlane(direction, Vector3.up).sqrMagnitude < 0.000001f)
        {
            direction = Vector3.right;
        }

        if (noiseLayers == null || noiseLayers.Length == 0)
        {
            noiseLayers = CreateDefaultLayers();
        }

        for (int i = 0; i < noiseLayers.Length; i++)
        {
            NoiseLayer layer = noiseLayers[i];
            layer.spatialScale = Mathf.Max(0.001f, layer.spatialScale);
            layer.scrollSpeed = Mathf.Max(0f, layer.scrollSpeed);
            layer.strengthVariation = Mathf.Max(0f, layer.strengthVariation);
            layer.sidewaysTurbulence = Mathf.Max(0f, layer.sidewaysTurbulence);
            layer.verticalTurbulence = Mathf.Max(0f, layer.verticalTurbulence);
            noiseLayers[i] = layer;
        }

        RefreshShaderLayerData();
    }
}
