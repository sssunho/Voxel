Shader "Custom/VoxelWater_LowPoly_URP"
{
    Properties
    {
        [Header(Water Color)]
        _ShallowColor   ("Shallow Color",   Color) = (0.18, 0.78, 0.85, 0.6)
        _DeepColor      ("Deep Color",      Color) = (0.05, 0.35, 0.65, 0.85)
        _DepthDistance  ("Depth Distance",  Range(0.5, 20)) = 6.0

        [Header(Wave)]
        _WaveColor      ("Wave Crest Color",Color) = (0.7, 0.95, 1.0, 1.0)
        _WaveTex        ("Wave Texture (R채널 사용)", 2D) = "white" {}
        _WaveSpeed      ("Wave Speed",      Range(0, 2)) = 0.4
        _WaveScale      ("Wave Scale",      Range(0.1, 5)) = 1.0
        _WaveThreshold  ("Wave Threshold",  Range(0, 1)) = 0.72
        _WaveSmoothness ("Wave Smoothness", Range(0, 0.2)) = 0.05

        [Header(Foam)]
        _FoamColor      ("Foam Color",      Color) = (0.9, 0.98, 1.0, 1.0)
        _FoamDistance   ("Foam Distance",   Range(0.1, 3)) = 0.6
        _FoamSmoothness ("Foam Smoothness", Range(0, 0.5)) = 0.2

        [Header(Fresnel)]
        _FresnelPower   ("Fresnel Power",   Range(1, 8)) = 3.0
        _FresnelStrength("Fresnel Strength",Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_WaveTex);   SAMPLER(sampler_WaveTex);

            CBUFFER_START(UnityPerMaterial)
                half4  _ShallowColor;
                half4  _DeepColor;
                float  _DepthDistance;

                half4  _WaveColor;
                float4 _WaveTex_ST;
                float  _WaveSpeed;
                float  _WaveScale;
                float  _WaveThreshold;
                float  _WaveSmoothness;

                half4  _FoamColor;
                float  _FoamDistance;
                float  _FoamSmoothness;

                float  _FresnelPower;
                float  _FresnelStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float4 screenPos   : TEXCOORD2;
                float  fogFactor   : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.uv         = input.uv;
                output.screenPos  = ComputeScreenPos(output.positionCS);
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // ── Depth 계산 (물 깊이) ────────────────────────────────
                float2 screenUV   = input.screenPos.xy / input.screenPos.w;
                float  sceneDepth = LinearEyeDepth(
                    SampleSceneDepth(screenUV),
                    _ZBufferParams
                );
                float  waterDepth = sceneDepth - input.screenPos.w;

                // ── 얕음/깊음 색상 블렌딩 ──────────────────────────────
                float  depthT   = saturate(waterDepth / _DepthDistance);
                half4  baseColor = lerp(_ShallowColor, _DeepColor, depthT);

                // ── Wave 텍스처 UV 스크롤 (두 방향 합산 → 자연스러운 물결) ─
                float2 uv1 = input.positionWS.xz * _WaveScale * 0.05
                             + float2(_Time.y * _WaveSpeed, _Time.y * _WaveSpeed * 0.6);
                float2 uv2 = input.positionWS.xz * _WaveScale * 0.04
                             + float2(-_Time.y * _WaveSpeed * 0.7, _Time.y * _WaveSpeed * 0.4);

                float wave1 = SAMPLE_TEXTURE2D_LOD(_WaveTex, sampler_WaveTex, uv1, 0).r;
                float wave2 = SAMPLE_TEXTURE2D_LOD(_WaveTex, sampler_WaveTex, uv2, 0).r;
                float wave  = (wave1 + wave2) * 0.5;

                // 셀 스타일 파도 마스크 (smoothstep으로 경계를 살짝만 부드럽게)
                float waveMask = smoothstep(
                    _WaveThreshold - _WaveSmoothness,
                    _WaveThreshold + _WaveSmoothness,
                    wave
                );
                baseColor.rgb = lerp(baseColor.rgb, _WaveColor.rgb, waveMask * 0.4);

                // ── Foam (해안선 흰 거품) ───────────────────────────────
                float foamMask = 1.0 - smoothstep(0.0, _FoamDistance, waterDepth);
                // 파도 텍스처와 합산해서 거품도 살짝 움직이게
                foamMask = saturate(foamMask + waveMask * saturate(1.0 - waterDepth));
                baseColor.rgb = lerp(baseColor.rgb, _FoamColor.rgb, foamMask);

                // ── Fresnel (시선 각도에 따라 투명도 조절) ─────────────
                float3 N       = float3(0, 1, 0); // 물은 flat normal
                float3 V       = normalize(GetCameraPositionWS() - input.positionWS);
                float  fresnel = pow(1.0 - saturate(dot(N, V)), _FresnelPower);
                baseColor.a    = saturate(baseColor.a + fresnel * _FresnelStrength);

                // ── 알파: 얕은 부분 더 투명하게 ────────────────────────
                baseColor.a = lerp(baseColor.a * 0.5, baseColor.a, depthT);
                // 폼 영역은 불투명하게
                baseColor.a = lerp(baseColor.a, 1.0, foamMask * 0.8);

                // ── Fog ────────────────────────────────────────────────
                baseColor.rgb = MixFog(baseColor.rgb, input.fogFactor);

                return baseColor;
            }
            ENDHLSL
        }
    }
}
