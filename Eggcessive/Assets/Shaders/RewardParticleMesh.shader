Shader "Eggcessive/Particles/Reward Mesh"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _LightingMatCap ("Lighting MatCap", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _LightingAmbientColor (
            "Lighting Ambient Color",
            Color) = (0.42, 0.46, 0.50, 1)
        _LightingMatCapStrength (
            "Lighting MatCap Strength",
            Range(0, 8)) = 3
        [Toggle] _UseMatCap ("Use MatCap", Float) = 0
        [Toggle] _UseLightingMatCap ("Use Lighting MatCap", Float) = 0
        [HideInInspector] _HasCustomLightingMatCap (
            "Has Custom Lighting MatCap",
            Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "RewardParticle"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:ParticleInstancingSetup

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ParticlesInstancing.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_LightingMatCap);
            SAMPLER(sampler_LightingMatCap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _LightingAmbientColor;
                half _LightingMatCapStrength;
                half _UseMatCap;
                half _UseLightingMatCap;
                half _HasCustomLightingMatCap;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 matCapUv : TEXCOORD1;
                float3 viewNormal : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                float3 worldNormal = TransformObjectToWorldNormal(
                    input.normalOS);
                float3 viewNormal = TransformWorldToViewDir(
                    worldNormal,
                    true);
                output.viewNormal = viewNormal;
                output.matCapUv = viewNormal.xy * 0.5 + 0.5;
                output.color = input.color * _Color;
                return output;
            }

            half4 Frag(
                Varyings input,
                FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float2 sampleUv = lerp(
                    input.uv,
                    input.matCapUv,
                    _UseMatCap);
                half4 color = SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    sampleUv) * input.color;

                if (_UseLightingMatCap > 0.5h)
                {
                    half faceSign = IS_FRONT_VFACE(
                        isFrontFace,
                        1.0h,
                        -1.0h);
                    half3 twoSidedViewNormal =
                        normalize(input.viewNormal) * faceSign;
                    // Expand the normal range a little so curvature remains
                    // readable on the small, fast-moving cash particles.
                    half2 lightingMatCapUv = saturate(
                        twoSidedViewNormal.xy * 0.75h + 0.5h);

                    // The particles render on a camera-relative plane, so real
                    // world lighting would not match their apparent path. Use a
                    // stable view-space lighting lookup instead. The procedural
                    // fallback keeps notes readable until an authored MatCap is
                    // assigned to RoundSystem.
                    half2 normalXY =
                        lightingMatCapUv * 2.0h - 1.0h;
                    half normalZ = sqrt(saturate(
                        1.0h - dot(normalXY, normalXY)));
                    half directional = saturate(
                        normalXY.x * -0.30h
                        + normalXY.y * 0.45h
                        + normalZ * 0.85h);
                    half3 fallbackLighting = directional.xxx;
                    half3 authoredLighting = SAMPLE_TEXTURE2D(
                        _LightingMatCap,
                        sampler_LightingMatCap,
                        lightingMatCapUv).rgb;
                    half3 matCapLighting = lerp(
                        fallbackLighting,
                        authoredLighting,
                        _HasCustomLightingMatCap);
                    // Treat the lookup as contrast over the authored cash
                    // texture, not absolute illumination. This keeps dark
                    // MatCaps from suppressing the bill's base colour while
                    // still allowing their highlights to read clearly.
                    half lightingInfluence = saturate(
                        _LightingMatCapStrength / 3.0h);
                    half3 lighting = 0.85h
                        + matCapLighting;
                    color.rgb *= lerp(
                        1.0h.xxx,
                        lighting,
                        lightingInfluence);
                }

                return color;
            }
            ENDHLSL
        }
    }
}
