Shader "Eggcessive/UI Hand Shadow"
{
    Properties
    {
        _ShadowColor("Shadow Color", Color) = (0.0, 0.0, 0.0, 1.0)
        _UiClipRect("UI Clip Rect", Vector) = (0.0, 0.0, 1.0, 1.0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Geometry-1"
        }

        Pass
        {
            Name "UIHandShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 screenPosition : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                float4 _UiClipRect;
            CBUFFER_END

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output;
                output.positionHCS =
                    TransformObjectToHClip(input.positionOS.xyz);
                output.screenPosition =
                    ComputeScreenPos(output.positionHCS);
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                float2 screenUv = input.screenPosition.xy
                    / input.screenPosition.w;
                clip(screenUv.x - _UiClipRect.x);
                clip(screenUv.y - _UiClipRect.y);
                clip(_UiClipRect.z - screenUv.x);
                clip(_UiClipRect.w - screenUv.y);
                return _ShadowColor;
            }
            ENDHLSL
        }
    }
}
