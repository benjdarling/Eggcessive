Shader "Eggcessive/Interactive Grass"
{
    Properties
    {
        _BaseColor("Blade Base Color", Color) = (0.12, 0.32, 0.06, 1)
        _TipColor("Tip Color", Color) = (0.38, 0.7, 0.16, 1)
        _DryColor("Dry Variation", Color) = (0.58, 0.55, 0.18, 1)
        _DryBlend("Dry Variation Strength", Range(0, 1)) = 0.2
        _AlphaMap("Alpha Map", 2D) = "white" {}
        _Cutoff("Alpha Clip Threshold", Range(0, 1)) = 0.5
        [HideInInspector] _GrassWindParameters0("Grass Wind Parameters 0", Vector) = (0.05, 0.5, 0.012, 0.42)
        [HideInInspector] _GrassWindParameters1("Grass Wind Parameters 1", Vector) = (0.7, 1, 0, 0)
        [HideInInspector] _GrassDistanceParameters("Grass Distance Parameters", Vector) = (8.25, 8.26, 8.26, 10)
        [HideInInspector] _GrassDistanceDensity("Grass Distance Density", Vector) = (1, 1, 0.08, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "ForwardLit"
            // This shader has no GBuffer pass. UniversalForwardOnly makes URP
            // render it once as a forward-lit material in both forward and
            // deferred renderers.
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex GrassVertex
            #pragma fragment GrassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES

            // Match mat_grass: diffuse lighting only, with no direct specular
            // highlights or indirect environment reflections.
            #define _SPECULARHIGHLIGHTS_OFF 1
            #define _ENVIRONMENTREFLECTIONS_OFF 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_AlphaMap);
            SAMPLER(sampler_AlphaMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _TipColor;
                half4 _DryColor;
                half _DryBlend;
                half _Cutoff;
                float4 _AlphaMap_ST;
                float4 _GrassWindParameters0;
                float4 _GrassWindParameters1;
                float4 _GrassDistanceParameters;
                float4 _GrassDistanceDensity;
            CBUFFER_END

            float4 _GlobalWindDirection;
            float _GlobalWindTime;
            int _GlobalWindLayerCount;
            float4 _GlobalWindLayerSpatial[8];
            float4 _GlobalWindLayerAmplitude[8];
            int _GlobalWindLocalInfluenceCount;
            float4 _GlobalWindLocalInfluencePosition[8];
            float4 _GlobalWindLocalInfluenceVector[8];

            UNITY_INSTANCING_BUFFER_START(GrassProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GrassBend)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GrassVariation)
            UNITY_INSTANCING_BUFFER_END(GrassProperties)

            float2 GrassWindHash(float2 position)
            {
                float3 value = frac(
                    float3(position.xyx)
                    * float3(0.1031, 0.1030, 0.0973));
                value += dot(value, value.yzx + 33.33);
                return frac((value.xx + value.yz) * value.zy);
            }

            float2 GrassWindNoise(float2 position)
            {
                float2 cell = floor(position);
                float2 fraction = frac(position);
                float2 blend = fraction * fraction * fraction
                    * (fraction * (fraction * 6.0 - 15.0) + 10.0);
                float2 bottom = lerp(
                    GrassWindHash(cell),
                    GrassWindHash(cell + float2(1.0, 0.0)),
                    blend.x);
                float2 top = lerp(
                    GrassWindHash(cell + float2(0.0, 1.0)),
                    GrassWindHash(cell + float2(1.0, 1.0)),
                    blend.x);
                return lerp(bottom, top, blend.y) * 2.0 - 1.0;
            }

            float2 ClampGrassBend(float2 bend, float maximum)
            {
                float magnitudeSquared = dot(bend, bend);
                float maximumSquared = maximum * maximum;
                return magnitudeSquared > maximumSquared && magnitudeSquared > 0.000001
                    ? bend * (maximum * rsqrt(magnitudeSquared))
                    : bend;
            }

            float GrassInstanceHash(float2 position)
            {
                float3 value = frac(float3(position.xyx) * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            float GrassDither(float2 pixelPosition)
            {
                // Interleaved gradient noise avoids visible concentric fade bands
                // while remaining stable for a stationary camera.
                return frac(
                    52.9829189
                    * frac(dot(floor(pixelPosition), float2(0.06711056, 0.00583715))));
            }

            float2 EvaluateGrassWind(float3 samplePositionWS, float responseVariation)
            {
                float2 windDirection = _GlobalWindDirection.xz;
                float directionLength = length(windDirection);
                windDirection = directionLength > 0.0001
                    ? windDirection / directionLength
                    : float2(1.0, 0.0);
                float2 sidewaysDirection =
                    float2(windDirection.y, -windDirection.x);
                float baseStrength = _GlobalWindDirection.w;
                float gustAmount = 0.0;
                float sidewaysAmount = 0.0;

                [unroll]
                for (int layerIndex = 0; layerIndex < 8; layerIndex++)
                {
                    if (layerIndex >= _GlobalWindLayerCount)
                    {
                        break;
                    }

                    float4 spatial = _GlobalWindLayerSpatial[layerIndex];
                    float4 amplitude = _GlobalWindLayerAmplitude[layerIndex];
                    float2 coordinates = samplePositionWS.xz * spatial.x
                        - windDirection * (_GlobalWindTime * spatial.y);
                    float2 noise = GrassWindNoise(
                        coordinates + float2(spatial.z, -spatial.z * 0.31));
                    gustAmount += noise.x * amplitude.x;
                    sidewaysAmount += noise.y * amplitude.y;
                }

                gustAmount = max(-0.95, gustAmount);
                float2 dynamicWind =
                    windDirection * (baseStrength * gustAmount)
                    + sidewaysDirection * (baseStrength * sidewaysAmount);

                [unroll]
                for (int influenceIndex = 0; influenceIndex < 8; influenceIndex++)
                {
                    if (influenceIndex >= _GlobalWindLocalInfluenceCount)
                    {
                        break;
                    }

                    float4 influencePosition =
                        _GlobalWindLocalInfluencePosition[influenceIndex];
                    float4 influenceVector =
                        _GlobalWindLocalInfluenceVector[influenceIndex];
                    float distanceToInfluence = distance(
                        samplePositionWS,
                        influencePosition.xyz);
                    float falloff = 1.0 - smoothstep(
                        0.0,
                        max(influencePosition.w, 0.0001),
                        distanceToInfluence);
                    dynamicWind += influenceVector.xz
                        * (influenceVector.w * falloff);
                }

                float dynamicMagnitude = length(dynamicWind);
                float deadZone = _GrassWindParameters0.z;
                dynamicWind = dynamicMagnitude > deadZone
                    ? dynamicWind
                        * ((dynamicMagnitude - deadZone)
                            / max(dynamicMagnitude, 0.0001))
                    : float2(0.0, 0.0);

                float2 windBend =
                    windDirection * (baseStrength * _GrassWindParameters0.x)
                    + dynamicWind * _GrassWindParameters0.y;
                float response = lerp(
                    _GrassWindParameters1.x,
                    _GrassWindParameters1.y,
                    responseVariation);
                return ClampGrassBend(
                    windBend * response,
                    _GrassWindParameters0.w);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half2 data : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                float2 uv : TEXCOORD4;
                half3 vertexSH : TEXCOORD5;
                float4 shadowCoord : TEXCOORD6;
                half2 distanceVisibility : TEXCOORD7;
            };

            Varyings GrassVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;

                float4 bend = UNITY_ACCESS_INSTANCED_PROP(GrassProperties, _GrassBend);
                float4 variation = UNITY_ACCESS_INSTANCED_PROP(GrassProperties, _GrassVariation);
                float heightWeight = saturate(input.uv.y);
                float curveWeight = heightWeight * heightWeight;
                float3 positionOS = input.positionOS.xyz;
                float3 instancePositionWS =
                    TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float3 windSamplePositionWS = instancePositionWS
                    + float3(variation.z, 0.0, variation.w);
                float cameraDistance = distance(
                    instancePositionWS.xz,
                    _WorldSpaceCameraPos.xz);
                float densityDistance = smoothstep(
                    _GrassDistanceParameters.x,
                    _GrassDistanceParameters.y,
                    cameraDistance);
                float densityRetention = lerp(
                    1.0,
                    _GrassDistanceDensity.x,
                    pow(densityDistance, _GrassDistanceDensity.y));
                float instanceRank = GrassInstanceHash(instancePositionWS.xz);
                float densityVisibility = densityRetention >= 0.9999
                    ? 1.0
                    : saturate(
                        (densityRetention - instanceRank)
                        / max(_GrassDistanceDensity.z, 0.0001)
                        + 0.5);
                float renderVisibility = 1.0 - smoothstep(
                    _GrassDistanceParameters.z,
                    _GrassDistanceParameters.w,
                    cameraDistance);
                float2 worldBend = bend.xy
                    + EvaluateGrassWind(windSamplePositionWS, variation.y);
                worldBend = ClampGrassBend(worldBend, 0.95);
                float3 worldBendDirection =
                    float3(worldBend.x, 0.0, worldBend.y);
                float3 objectRightWS =
                    TransformObjectToWorldDir(float3(1.0, 0.0, 0.0), true);
                float3 objectForwardWS =
                    TransformObjectToWorldDir(float3(0.0, 0.0, 1.0), true);
                float2 localBend = float2(
                    dot(objectRightWS, worldBendDirection),
                    dot(objectForwardWS, worldBendDirection));
                // The instance matrix supplies uniform clump scale, so authored
                // width, spread and bend retain the proportions seen in Prefab Mode.
                positionOS.xz += localBend * curveWeight;
                positionOS.y *= 1.0 - saturate(bend.z) * curveWeight * 0.84;

                VertexPositionInputs positions = GetVertexPositionInputs(positionOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.data = half2(heightWeight, variation.x);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                output.uv = TRANSFORM_TEX(input.uv, _AlphaMap);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                output.shadowCoord = TransformWorldToShadowCoord(positions.positionWS);
                output.distanceVisibility = half2(
                    densityVisibility,
                    renderVisibility);
                return output;
            }

            half4 GrassFragment(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_AlphaMap, sampler_AlphaMap, input.uv).a;
                clip(alpha - _Cutoff);
                half visibility = input.distanceVisibility.x
                    * input.distanceVisibility.y;
                clip(visibility - GrassDither(input.positionCS.xy));

                // Grass is rendered two-sided, but both sides should retain the
                // authored/upward-blended normal. Flipping the back face normal
                // made one side point downward and become disproportionately dark.
                half3 normalWS = normalize(input.normalWS);
                half3 color = lerp(_BaseColor.rgb, _TipColor.rgb, input.data.x);
                half dryMask = input.data.y * _DryBlend
                    * smoothstep(0.28h, 1.0h, input.data.x);
                color = lerp(color, _DryColor.rgb, dryMask);

                // Graphics.DrawMeshInstanced does not provide the same
                // per-Renderer unity_LightData value as the ground renderer.
                // For a directional light its distance attenuation is always
                // one, so supply that value explicitly instead of allowing the
                // missing per-object value to remove the direct light entirely.
                // This overload samples both the realtime shadow map and the
                // directional light cookie at the blade's world position.
                Light mainLight = GetMainLight(
                    input.shadowCoord,
                    input.positionWS,
                    half4(1.0h, 1.0h, 1.0h, 1.0h));
                mainLight.distanceAttenuation = 1.0h;
                half directAttenuation = mainLight.distanceAttenuation
                    * mainLight.shadowAttenuation
                    * saturate(dot(normalWS, mainLight.direction));

                // This material disables specular highlights and environment
                // reflections. Its PBR path therefore reduces exactly to a
                // dielectric diffuse term, so avoid constructing SurfaceData,
                // InputData and BRDFData for every alpha-tested grass pixel.
                half3 diffuseColor = color * kDielectricSpec.a;
                half3 bakedLighting = SampleSHPixel(input.vertexSH, normalWS);
                half3 lighting = diffuseColor
                    * (bakedLighting + mainLight.color * directAttenuation);

                half4 litColor = half4(lighting, 1.0h);
                litColor.rgb = MixFog(litColor.rgb, input.fogFactor);
                return litColor;
            }
            ENDHLSL
        }
    }
}
