Shader "Eggcessive/Lighting/Scrolling Cloud Cookie"
{
    Properties
    {
        _ShadowStrength ("Shadow Strength", Range(0, 0.8)) = 0.22
        _Coverage ("Cloud Coverage", Range(0, 1)) = 0.55
        _Softness ("Cloud Edge Softness", Range(0.01, 0.5)) = 0.18
        [IntRange] _MacroScale ("Large Cloud Count", Range(1, 6)) = 2
        _MacroInfluence ("Large Cloud Influence", Range(0, 2)) = 1.2
        [IntRange] _CloudScale ("Medium Cloud Count", Range(2, 14)) = 5
        [IntRange] _SecondaryScale ("Small Cloud Count", Range(4, 24)) = 11
        _SecondaryInfluence ("Small Cloud Influence", Range(0, 1)) = 0.28
        _DetailStrength ("Small Detail", Range(0, 1)) = 0.35
        _Distortion ("Cloud Distortion", Range(0, 1)) = 0.3
        [HideInInspector] _ScrollOffset ("Scroll Offset", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "GenerateCloudCookie"
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _ShadowStrength;
            float _Coverage;
            float _Softness;
            float _MacroScale;
            float _MacroInfluence;
            float _CloudScale;
            float _SecondaryScale;
            float _SecondaryInfluence;
            float _DetailStrength;
            float _Distortion;
            float4 _ScrollOffset;

            float Hash21(float2 value)
            {
                value = frac(value * float2(0.1031, 0.1030));
                value += dot(value, value.yx + 33.33);
                return frac((value.x + value.y) * value.x);
            }

            float2 WrapCell(float2 cell, float period)
            {
                return cell - floor(cell / period) * period;
            }

            float PeriodicValueNoise(float2 position, float period)
            {
                float2 cell = floor(position);
                float2 blend = frac(position);
                blend = blend * blend * (3.0 - 2.0 * blend);

                float bottomLeft = Hash21(WrapCell(cell, period));
                float bottomRight = Hash21(WrapCell(cell + float2(1.0, 0.0), period));
                float topLeft = Hash21(WrapCell(cell + float2(0.0, 1.0), period));
                float topRight = Hash21(WrapCell(cell + 1.0, period));

                float bottom = lerp(bottomLeft, bottomRight, blend.x);
                float top = lerp(topLeft, topRight, blend.x);
                return lerp(bottom, top, blend.y);
            }

            float CloudLayer(
                float2 uv,
                float scale,
                float2 scroll,
                float2 seed,
                float detailStrength)
            {
                scale = max(1.0, round(scale));

                float2 warpPosition = (uv + scroll) * scale;
                float2 warp;
                warp.x = PeriodicValueNoise(
                    warpPosition + seed + float2(1.7, 8.3),
                    scale);
                warp.y = PeriodicValueNoise(
                    warpPosition + seed + float2(7.1, 2.4),
                    scale);
                warp = (warp * 2.0 - 1.0) * (_Distortion / scale);

                float2 cloudUv = uv + scroll + warp;
                float large = PeriodicValueNoise(
                    cloudUv * scale + seed,
                    scale);
                float medium = PeriodicValueNoise(
                    cloudUv * (scale * 2.0) + seed + float2(4.3, 1.9),
                    scale * 2.0);
                float small = PeriodicValueNoise(
                    cloudUv * (scale * 4.0) + seed + float2(2.1, 9.7),
                    scale * 4.0);

                float detailWeight = saturate(detailStrength);
                float mediumWeight = 0.3 * detailWeight;
                float smallWeight = 0.14 * detailWeight;
                return (large + medium * mediumWeight + small * smallWeight)
                    / (1.0 + mediumWeight + smallWeight);
            }

            float CloudField(float2 uv)
            {
                float2 scroll = _ScrollOffset.xy;

                // Integer-coordinate shears preserve tiling at the outer cookie
                // boundary while breaking the repeated axis-aligned silhouettes
                // that a single square noise field produces.
                float2 diagonalUv = float2(uv.x + uv.y, uv.x - uv.y);
                float2 skewUv = float2(
                    2.0 * uv.x + uv.y,
                    uv.x - 2.0 * uv.y);

                float macro = CloudLayer(
                    uv,
                    _MacroScale,
                    scroll,
                    float2(13.7, 5.1),
                    _DetailStrength * 0.25);
                float medium = CloudLayer(
                    diagonalUv,
                    _CloudScale,
                    float2(
                        2.0 * scroll.x + scroll.y,
                        -scroll.x + scroll.y),
                    float2(3.4, 17.8),
                    _DetailStrength);
                float small = CloudLayer(
                    skewUv,
                    _SecondaryScale,
                    float2(
                        -scroll.x + 2.0 * scroll.y,
                        scroll.x + 2.0 * scroll.y),
                    float2(21.2, 9.6),
                    _DetailStrength);

                float macroWeight = max(0.0, _MacroInfluence);
                float smallWeight = saturate(_SecondaryInfluence);
                return (macro * macroWeight + medium + small * smallWeight)
                    / max(0.001, macroWeight + 1.0 + smallWeight);
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float cloudNoise = CloudField(input.uv);
                float halfSoftness = max(0.005, _Softness * 0.5);
                float threshold = 1.0 - _Coverage;
                float cloud = smoothstep(
                    threshold - halfSoftness,
                    threshold + halfSoftness,
                    cloudNoise);

                float cookie = lerp(1.0, 1.0 - saturate(_ShadowStrength), cloud);
                return fixed4(cookie, cookie, cookie, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
