Shader "Eggcessive/Interactive Grass"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.12, 0.32, 0.06, 1)
        _TipColor("Tip Color", Color) = (0.38, 0.7, 0.16, 1)
        _DryColor("Dry Variation", Color) = (0.58, 0.55, 0.18, 1)
        _DryBlend("Dry Variation Strength", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex GrassVertex
            #pragma fragment GrassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _TipColor;
                half4 _DryColor;
                half _DryBlend;
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
                return output;
            }

            half4 GrassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Grass is rendered two-sided, but both sides should retain the
                // authored/upward-blended normal. Flipping the back face normal
                // made one side point downward and become disproportionately dark.
                half3 normalWS = normalize(input.normalWS);
                half3 color = lerp(_BaseColor.rgb, _TipColor.rgb, input.data.x);
                half dryMask = input.data.y * _DryBlend
                    * smoothstep(0.28h, 1.0h, input.data.x);
                color = lerp(color, _DryColor.rgb, dryMask);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half lighting = 0.42h + saturate(dot(normalWS, mainLight.direction))
                    * mainLight.shadowAttenuation * 0.58h;
                color *= lighting * mainLight.color;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
