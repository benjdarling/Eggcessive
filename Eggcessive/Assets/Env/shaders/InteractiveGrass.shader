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
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(GrassProperties)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GrassBend)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GrassVariation)
            UNITY_INSTANCING_BUFFER_END(GrassProperties)

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings GrassVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4 bend = UNITY_ACCESS_INSTANCED_PROP(GrassProperties, _GrassBend);
                float4 variation = UNITY_ACCESS_INSTANCED_PROP(GrassProperties, _GrassVariation);
                float heightWeight = saturate(input.uv.y);
                float curveWeight = heightWeight * heightWeight;
                float3 positionOS = input.positionOS.xyz;
                // The instance matrix supplies uniform clump scale, so authored
                // width, spread and bend retain the proportions seen in Prefab Mode.
                positionOS.xz += bend.xy * curveWeight;
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
                return output;
            }

            half4 GrassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half alpha = SAMPLE_TEXTURE2D(_AlphaMap, sampler_AlphaMap, input.uv).a;
                clip(alpha - _Cutoff);

                // Grass is rendered two-sided, but both sides should retain the
                // authored/upward-blended normal. Flipping the back face normal
                // made one side point downward and become disproportionately dark.
                half3 normalWS = normalize(input.normalWS);
                half3 color = lerp(_BaseColor.rgb, _TipColor.rgb, input.data.x);
                half dryMask = input.data.y * _DryBlend
                    * smoothstep(0.28h, 1.0h, input.data.x);
                color = lerp(color, _DryColor.rgb, dryMask);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = half3(0.0h, 0.0h, 0.0h);
                inputData.bakedGI = SampleSHPixel(input.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = color;
                surfaceData.specular = half3(0.0h, 0.0h, 0.0h);
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = 0.0h;
                surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
                surfaceData.emission = half3(0.0h, 0.0h, 0.0h);
                surfaceData.occlusion = 1.0h;
                surfaceData.alpha = 1.0h;
                surfaceData.clearCoatMask = 0.0h;
                surfaceData.clearCoatSmoothness = 0.0h;

                BRDFData brdfData;
                InitializeBRDFData(surfaceData, brdfData);

                half3 lighting = GlobalIllumination(
                    brdfData,
                    inputData.bakedGI,
                    surfaceData.occlusion,
                    inputData.positionWS,
                    inputData.normalWS,
                    inputData.viewDirectionWS);

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
                    inputData.shadowMask);
                mainLight.distanceAttenuation = 1.0h;
                lighting += LightingPhysicallyBased(
                    brdfData,
                    mainLight,
                    inputData.normalWS,
                    inputData.viewDirectionWS,
                    true);

                half4 litColor = half4(lighting + surfaceData.emission, surfaceData.alpha);
                litColor.rgb = MixFog(litColor.rgb, inputData.fogCoord);
                return litColor;
            }
            ENDHLSL
        }
    }
}
