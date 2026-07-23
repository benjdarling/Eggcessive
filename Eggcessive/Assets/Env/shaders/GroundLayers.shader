Shader "Eggcessive/Ground Layers"
{
    Properties
    {
        _GrassMap("Grass Albedo", 2D) = "white" {}
        [NoScaleOffset] _GrassHeightMap("Grass Height", 2D) = "gray" {}
        _GrassHeightBlendDepth("Grass Height Blend Depth", Range(0.01, 1)) = 0.18
        _GrassTransitionNoiseStrength("Grass Transition Noise", Range(0, 1)) = 0.35
        _GrassTint("Grass Tint", Color) = (1, 1, 1, 1)
        _DirtMap("Dirt Albedo", 2D) = "white" {}
        _DirtTint("Dirt Tint", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _LayerMask("Layer Mask (R Dirt, G Grass, B Breakup, A Placed)", 2D) = "red" {}
        _GrassMaskStrength("Grass Mask Strength", Range(0, 4)) = 2
        [Toggle] _MaskPreview("Preview Layer Mask", Float) = 0
        [ToggleOff] _ReceiveShadows("Receive Shadows", Float) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0

        // Kept for InteractiveGrassSystem's blade-base colour sync.
        [HideInInspector] _BaseColor("Grass Blade Base Color", Color) = (0.12156863, 0.32156864, 0.05882353, 1)
        [HideInInspector] _MaskWorldRect("Mask World Rect", Vector) = (-2, -2.5, 4, 5)
        [HideInInspector] _PlacedCoverageAvailable("Placed Coverage Available", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Lit"
        }

        Pass
        {
            Name "ForwardLit"
            // Keep the layered ground on the same world-shadow path as the
            // interactive grass. In deferred mode UniversalGBuffer shadows are
            // reconstructed in screen space, which produced camera/frustum-
            // aligned clipping on this large ground mesh.
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex GroundVertex
            #pragma fragment GroundFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // This material is drawn forward-only by the deferred renderer.
            // Sample the main shadow map directly; the screen-space shadow
            // texture is not valid yet at this stage of the frame.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF

            #define _SPECULARHIGHLIGHTS_OFF 1
            #define _ENVIRONMENTREFLECTIONS_OFF 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GrassMap);
            SAMPLER(sampler_GrassMap);
            TEXTURE2D(_GrassHeightMap);
            SAMPLER(sampler_GrassHeightMap);
            TEXTURE2D(_DirtMap);
            SAMPLER(sampler_DirtMap);
            TEXTURE2D(_LayerMask);
            SAMPLER(sampler_LayerMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _GrassMap_ST;
                float4 _DirtMap_ST;
                half4 _GrassTint;
                half4 _DirtTint;
                half4 _BaseColor;
                float4 _MaskWorldRect;
                half _GrassMaskStrength;
                half _GrassHeightBlendDepth;
                half _GrassTransitionNoiseStrength;
                half _PlacedCoverageAvailable;
                half _MaskPreview;
                half _Smoothness;
            CBUFFER_END

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
                float2 grassUV : TEXCOORD2;
                float2 dirtUV : TEXCOORD3;
                half3 vertexSH : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings GroundVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.grassUV = TRANSFORM_TEX(input.uv, _GrassMap);
                output.dirtUV = TRANSFORM_TEX(input.uv, _DirtMap);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 GroundFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 normalWS = normalize(input.normalWS);

                float2 maskUV = (input.positionWS.xz - _MaskWorldRect.xy)
                    / max(_MaskWorldRect.zw, float2(0.0001, 0.0001));
                half4 layerMask = SAMPLE_TEXTURE2D(
                    _LayerMask,
                    sampler_LayerMask,
                    saturate(maskUV));

                half3 dirt = SAMPLE_TEXTURE2D(_DirtMap, sampler_DirtMap, input.dirtUV).rgb
                    * _DirtTint.rgb;
                half3 grass = SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, input.grassUV).rgb
                    * _GrassTint.rgb;

                // Green is the authoritative broad meadow coverage. Height detail
                // is allowed only in a narrow rim around 50% coverage, so it can
                // reveal individual blades without cutting extra holes inside
                // otherwise solid grass.
                half grassControl = saturate(layerMask.g * _GrassMaskStrength);
                half grassHeight = SAMPLE_TEXTURE2D(
                    _GrassHeightMap,
                    sampler_GrassHeightMap,
                    input.grassUV).r;
                half transitionBand = 1.0h - smoothstep(
                    0.14h,
                    0.38h,
                    abs(grassControl - 0.5h));
                half transitionNoise = layerMask.b - 0.5h;
                half noisyGrassControl = saturate(
                    grassControl
                    + transitionNoise
                    * _GrassTransitionNoiseStrength
                    * transitionBand);
                half broadGrassWeight = smoothstep(
                    0.18h,
                    0.82h,
                    noisyGrassControl);
                half heightGrassWeight = saturate(
                    (noisyGrassControl + grassHeight - 1.0h)
                    / max(_GrassHeightBlendDepth, 0.001h));
                half grassWeight = lerp(
                    broadGrassWeight,
                    heightGrassWeight,
                    transitionBand);
                grassWeight *= step(0.0001h, grassControl);
                // Alpha is generated from the actual placed clump footprints.
                // Apply it after height blending so no rendered blade clump can
                // sit over dirt simply because transition noise removed its bed.
                grassWeight = max(
                    grassWeight,
                    layerMask.a * _PlacedCoverageAvailable);
                half3 albedo = dirt * layerMask.r;
                albedo = lerp(albedo, grass, grassWeight);

                if (_MaskPreview > 0.5h)
                {
                    return half4(layerMask.rgb, 1.0h);
                }

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // Cascade selection is discontinuous, so it cannot be chosen at
                // the four corners of this large ground face and interpolated.
                // Select the cascade per pixel from the interpolated world
                // position, as URP's Lit pass does for cascaded shadows.
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSHPixel(input.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.metallic = 0;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1;
                surfaceData.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "GBuffer"
            Tags
            {
                // Retained as dormant source for now. The ground deliberately
                // renders through ForwardLit so it samples the shadow map in
                // world space instead of the deferred screen-space shadow.
                "LightMode" = "EggcessiveDisabledGBuffer"
                "UniversalMaterialType" = "Lit"
            }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex GroundGBufferVertex
            #pragma fragment GroundGBufferFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF

            #define _SPECULARHIGHLIGHTS_OFF 1
            #define _ENVIRONMENTREFLECTIONS_OFF 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"

            TEXTURE2D(_GrassMap);
            SAMPLER(sampler_GrassMap);
            TEXTURE2D(_GrassHeightMap);
            SAMPLER(sampler_GrassHeightMap);
            TEXTURE2D(_DirtMap);
            SAMPLER(sampler_DirtMap);
            TEXTURE2D(_LayerMask);
            SAMPLER(sampler_LayerMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _GrassMap_ST;
                float4 _DirtMap_ST;
                half4 _GrassTint;
                half4 _DirtTint;
                half4 _BaseColor;
                float4 _MaskWorldRect;
                half _GrassMaskStrength;
                half _GrassHeightBlendDepth;
                half _GrassTransitionNoiseStrength;
                half _PlacedCoverageAvailable;
                half _MaskPreview;
                half _Smoothness;
            CBUFFER_END

            struct GroundGBufferAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct GroundGBufferVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 grassUV : TEXCOORD2;
                float2 dirtUV : TEXCOORD3;
                half3 vertexSH : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            GroundGBufferVaryings GroundGBufferVertex(GroundGBufferAttributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                GroundGBufferVaryings output;
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.grassUV = TRANSFORM_TEX(input.uv, _GrassMap);
                output.dirtUV = TRANSFORM_TEX(input.uv, _DirtMap);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            GBufferFragOutput GroundGBufferFragment(GroundGBufferVaryings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 normalWS = normalize(input.normalWS);
                float2 maskUV = (input.positionWS.xz - _MaskWorldRect.xy)
                    / max(_MaskWorldRect.zw, float2(0.0001, 0.0001));
                half4 layerMask = SAMPLE_TEXTURE2D(
                    _LayerMask,
                    sampler_LayerMask,
                    saturate(maskUV));

                half3 dirt = SAMPLE_TEXTURE2D(_DirtMap, sampler_DirtMap, input.dirtUV).rgb
                    * _DirtTint.rgb;
                half3 grass = SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, input.grassUV).rgb
                    * _GrassTint.rgb;
                half grassControl = saturate(layerMask.g * _GrassMaskStrength);
                half grassHeight = SAMPLE_TEXTURE2D(
                    _GrassHeightMap,
                    sampler_GrassHeightMap,
                    input.grassUV).r;
                half transitionBand = 1.0h - smoothstep(
                    0.14h,
                    0.38h,
                    abs(grassControl - 0.5h));
                half transitionNoise = layerMask.b - 0.5h;
                half noisyGrassControl = saturate(
                    grassControl
                    + transitionNoise
                    * _GrassTransitionNoiseStrength
                    * transitionBand);
                half broadGrassWeight = smoothstep(
                    0.18h,
                    0.82h,
                    noisyGrassControl);
                half heightGrassWeight = saturate(
                    (noisyGrassControl + grassHeight - 1.0h)
                    / max(_GrassHeightBlendDepth, 0.001h));
                half grassWeight = lerp(
                    broadGrassWeight,
                    heightGrassWeight,
                    transitionBand);
                grassWeight *= step(0.0001h, grassControl);
                grassWeight = max(
                    grassWeight,
                    layerMask.a * _PlacedCoverageAvailable);
                half3 albedo = lerp(dirt * layerMask.r, grass, grassWeight);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.bakedGI = SampleSHPixel(input.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = _MaskPreview > 0.5h ? half3(0, 0, 0) : albedo;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.metallic = 0;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = _MaskPreview > 0.5h ? layerMask.rgb : half3(0, 0, 0);
                surfaceData.occlusion = 1;
                surfaceData.alpha = 1;

                BRDFData brdfData;
                InitializeBRDFData(surfaceData, brdfData);
                half3 indirectLighting = GlobalIllumination(
                    brdfData,
                    inputData.bakedGI,
                    surfaceData.occlusion,
                    inputData.positionWS,
                    inputData.normalWS,
                    inputData.viewDirectionWS);

                return PackGBuffersBRDFData(
                    brdfData,
                    inputData,
                    surfaceData.smoothness,
                    surfaceData.emission + indirectLighting,
                    surfaceData.occlusion);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
}
