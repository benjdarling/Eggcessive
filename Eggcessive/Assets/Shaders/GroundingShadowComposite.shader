Shader "Hidden/Eggcessive/Grounding Shadow Composite"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Overlay"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D(_GroundingShadowHeightMap);
        SAMPLER(sampler_GroundingShadowHeightMap);
        TEXTURE2D(_GroundingShadowMask);
        SAMPLER(sampler_GroundingShadowMask);

        float4x4 _GroundingShadowWorldToUV;
        float4 _GroundingShadowHeightParams;
        float4 _GroundingShadowAppearanceParams;
        float4 _GroundingShadowBlurDirection;
        half4 _GroundingShadowColor;
        float _GroundingShadowOpacity;

        float SampleHardOcclusion(float2 uv, float receiverHeight)
        {
            float casterHeight = SAMPLE_TEXTURE2D(
                _GroundingShadowHeightMap,
                sampler_GroundingShadowHeightMap,
                uv).r;
            float separation = casterHeight - receiverHeight;
            float bias = _GroundingShadowHeightParams.z;
            float range = _GroundingShadowHeightParams.w;
            float insideRange = step(bias, separation) * step(separation, bias + range);
            float proximity = saturate(1.0 - (separation - bias) / range);
            return insideRange * pow(proximity, _GroundingShadowAppearanceParams.z);
        }

        half4 MaskFragment(Varyings input) : SV_Target
        {
            float2 screenUv = input.texcoord;
            float deviceDepth = SampleSceneDepth(screenUv);

            #if UNITY_REVERSED_Z
            if (deviceDepth <= 0.00001)
                return 0.0;
            #else
            if (deviceDepth >= 0.99999)
                return 0.0;
            #endif

            float3 positionWS = ComputeWorldSpacePosition(
                screenUv,
                deviceDepth,
                UNITY_MATRIX_I_VP);
            float4 mapPosition = mul(
                _GroundingShadowWorldToUV,
                float4(positionWS, 1.0));
            float2 mapUv = mapPosition.xy / max(0.0001, mapPosition.w);

            if (any(mapUv < 0.0) || any(mapUv > 1.0))
                return 0.0;

            float minimumHeight = _GroundingShadowHeightParams.x;
            float maximumHeight = _GroundingShadowHeightParams.y;
            if (positionWS.y < minimumHeight || positionWS.y > maximumHeight)
                return 0.0;

            float3 dpdx = ddx(positionWS);
            float3 dpdy = ddy(positionWS);
            float3 receiverNormal = normalize(cross(dpdy, dpdx));
            receiverNormal *= receiverNormal.y < 0.0 ? -1.0 : 1.0;
            float minimumUpwardFacing = _GroundingShadowAppearanceParams.w;
            float receiverWeight = smoothstep(
                minimumUpwardFacing,
                min(1.0, minimumUpwardFacing + 0.35),
                receiverNormal.y);

            float mask = SampleHardOcclusion(mapUv, positionWS.y) * receiverWeight;
            return half4(mask, mask, mask, 1.0);
        }

        half4 BlurFragment(Varyings input) : SV_Target
        {
            const float CenterWeight = 0.2270270270;
            const float InnerWeight = 0.3162162162;
            const float OuterWeight = 0.0702702703;
            float2 direction = _GroundingShadowBlurDirection.xy;
            float2 uv = input.texcoord;

            half mask = SAMPLE_TEXTURE2D_X(
                _BlitTexture,
                sampler_LinearClamp,
                uv).r * CenterWeight;
            mask += SAMPLE_TEXTURE2D_X(
                _BlitTexture,
                sampler_LinearClamp,
                uv + direction * 1.38461538).r * InnerWeight;
            mask += SAMPLE_TEXTURE2D_X(
                _BlitTexture,
                sampler_LinearClamp,
                uv - direction * 1.38461538).r * InnerWeight;
            mask += SAMPLE_TEXTURE2D_X(
                _BlitTexture,
                sampler_LinearClamp,
                uv + direction * 3.23076923).r * OuterWeight;
            mask += SAMPLE_TEXTURE2D_X(
                _BlitTexture,
                sampler_LinearClamp,
                uv - direction * 3.23076923).r * OuterWeight;

            return half4(mask, mask, mask, 1.0);
        }

        half4 CompositeFragment(Varyings input) : SV_Target
        {
            float2 screenUv = input.texcoord;
            half4 source = SAMPLE_TEXTURE2D_X(
                _BlitTexture,
                sampler_LinearClamp,
                screenUv);
            half mask = SAMPLE_TEXTURE2D(
                _GroundingShadowMask,
                sampler_GroundingShadowMask,
                screenUv).r;
            half strength = saturate(mask * _GroundingShadowOpacity);
            source.rgb = lerp(
                source.rgb,
                source.rgb * _GroundingShadowColor.rgb,
                strength);
            return source;
        }
        ENDHLSL

        Pass
        {
            Name "GroundingShadowMask"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment MaskFragment
            ENDHLSL
        }

        Pass
        {
            Name "GroundingShadowBlur"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BlurFragment
            ENDHLSL
        }

        Pass
        {
            Name "GroundingShadowComposite"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeFragment
            ENDHLSL
        }
    }
}
