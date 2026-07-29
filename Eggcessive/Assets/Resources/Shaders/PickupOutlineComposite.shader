Shader "Hidden/Eggcessive/Pickup Outline Composite"
{
    Properties
    {
        [HDR] _OutlineColor("Outline Color", Color) = (1.0, 0.78, 0.18, 1.0)
        _OutlineThickness("Outline Thickness (Pixels)", Range(1.0, 8.0)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Overlay"
        }

        Pass
        {
            Name "PickupOutlineComposite"
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_PickupOutlineMask);
            SAMPLER(sampler_PickupOutlineMask);

            half4 _OutlineColor;
            float _OutlineThickness;

            half SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D(
                    _PickupOutlineMask,
                    sampler_PickupOutlineMask,
                    uv).r;
            }

            half4 CompositeFragment(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv);

                float thickness = max(1.0, _OutlineThickness);
                half center = SampleMask(uv);
                half outer = 0.0;

                [unroll]
                for (int ring = 1; ring <= 4; ring++)
                {
                    float radius = thickness * ring * 0.25;
                    float2 offset =
                        _BlitTexture_TexelSize.xy * radius;
                    outer = max(
                        outer,
                        SampleMask(uv + float2(offset.x, 0.0)));
                    outer = max(
                        outer,
                        SampleMask(uv - float2(offset.x, 0.0)));
                    outer = max(
                        outer,
                        SampleMask(uv + float2(0.0, offset.y)));
                    outer = max(
                        outer,
                        SampleMask(uv - float2(0.0, offset.y)));
                    outer = max(outer, SampleMask(uv + offset));
                    outer = max(outer, SampleMask(uv - offset));
                    outer = max(
                        outer,
                        SampleMask(
                            uv + float2(offset.x, -offset.y)));
                    outer = max(
                        outer,
                        SampleMask(
                            uv + float2(-offset.x, offset.y)));
                }

                half edge = saturate(outer - center);
                edge = smoothstep(0.03, 0.35, edge) * _OutlineColor.a;
                source.rgb = lerp(source.rgb, _OutlineColor.rgb, edge);
                return source;
            }
            ENDHLSL
        }
    }
}
