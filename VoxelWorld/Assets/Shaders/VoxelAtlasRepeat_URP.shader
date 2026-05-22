Shader "Custom/VoxelAtlas_LowPoly_URP"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _AtlasSize ("Atlas Size", Vector) = (4, 4, 0, 0)
        _Fade ("Fade", Range(0, 1)) = 1

        [Header(Cel Shading)]
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.4
        _ShadowSmoothness ("Shadow Smoothness", Range(0, 0.2)) = 0.04
        _ShadowColor ("Shadow Tint", Color) = (0.25, 0.3, 0.45, 1)

        [Header(Hemisphere Ambient)]
        _SkyColor ("Sky Color", Color) = (0.38, 0.58, 0.85, 1)
        _GroundColor ("Ground Color", Color) = (0.18, 0.14, 0.10, 1)
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.45

        [Header(Rim Light)]
        _RimColor ("Rim Color", Color) = (0.5, 0.75, 1.0, 1)
        _RimPower ("Rim Power", Range(1, 8)) = 4.0
        _RimStrength ("Rim Strength", Range(0, 0.5)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _AtlasSize;
                float4 _MainTex_TexelSize;  // Unity 자동 제공: (1/w, 1/h, w, h)
                float  _Fade;

                float  _ShadowThreshold;
                float  _ShadowSmoothness;
                half4  _ShadowColor;

                half4  _SkyColor;
                half4  _GroundColor;
                float  _AmbientStrength;

                half4  _RimColor;
                float  _RimPower;
                float  _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float2 uv          : TEXCOORD1;
                float2 uv2         : TEXCOORD2;
                float  fogFactor   : TEXCOORD3;
                float4 screenPos   : TEXCOORD4;
                float3 positionWS  : TEXCOORD5;
            };

            // Bayer 4x4 dither
            float Dither4x4(float2 screenPos)
            {
                static const float bayer[16] = {
                     0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                    12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                     3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                    15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
                };
                int x = (int)screenPos.x & 3;
                int y = (int)screenPos.y & 3;
                return bayer[y * 4 + x];
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs posInputs    = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS   = normalInputs.normalWS;
                output.uv         = input.uv;
                output.uv2        = input.uv2;
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);
                output.screenPos  = ComputeScreenPos(output.positionCS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // ── Dither Fade ──────────────────────────────────────────
                float2 pixelPos = (input.screenPos.xy / input.screenPos.w) * _ScreenParams.xy;
                clip(_Fade - Dither4x4(pixelPos));

                // ── Atlas UV ─────────────────────────────────────────────
                // frac() 정수 경계에서 ddx/ddy 폭발 → GPU가 고레벨 밉맵 선택
                // LOD 0 고정 샘플링 + 타일 경계 반픽셀 inset으로 해결
                float2 tileSize        = 1.0 / _AtlasSize.xy;
                float2 halfTexelInTile = (_MainTex_TexelSize.xy / tileSize) * 0.5;
                float2 tiledUV         = frac(input.uv);
                tiledUV                = clamp(tiledUV,
                                               halfTexelInTile,
                                               1.0 - halfTexelInTile);
                float2 finalUV         = (input.uv2 + tiledUV) * tileSize;
                half4  col             = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, finalUV, 0);

                float3 N = normalize(input.normalWS);

                // ── URP Main Light ────────────────────────────────────────
#if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);
#else
                Light  mainLight   = GetMainLight();
#endif
                float3 L     = normalize(mainLight.direction);
                float  NdotL = dot(N, L);

                // ── Cel Shading (2-step) ──────────────────────────────────
                // smoothstep로 경계를 살짝 부드럽게 → 만화풍 유지하면서 깨짐 방지
                float celMask = smoothstep(
                    _ShadowThreshold - _ShadowSmoothness,
                    _ShadowThreshold + _ShadowSmoothness,
                    NdotL
                );
                celMask *= mainLight.shadowAttenuation;

                half3 litColor    = col.rgb * mainLight.color;
                half3 shadowColor = col.rgb * _ShadowColor.rgb * mainLight.color;
                half3 celColor    = lerp(shadowColor, litColor, celMask);

                // ── Hemisphere Ambient ────────────────────────────────────
                // 노멀의 위/아래 방향으로 하늘색/땅색을 블렌딩
                float hemi        = N.y * 0.5 + 0.5;            // 0(아래) ~ 1(위)
                half3 ambientCol  = lerp(_GroundColor.rgb, _SkyColor.rgb, hemi);
                half3 ambient     = col.rgb * ambientCol * _AmbientStrength;

                // ── Rim Light ─────────────────────────────────────────────
                // 카메라 방향과 노멀의 각도로 외곽선 느낌
                float3 V          = normalize(GetCameraPositionWS() - input.positionWS);
                float  rim        = 1.0 - saturate(dot(N, V));
                rim               = pow(rim, _RimPower);
                // 빛을 받는 면에만 림 적용 (그림자 면 림 억제)
                half3  rimColor   = _RimColor.rgb * rim * _RimStrength * saturate(NdotL + 0.3);

                // ── Combine ───────────────────────────────────────────────
                half3 finalColor  = celColor + ambient + rimColor;
                finalColor        = MixFog(finalColor, input.fogFactor);

                return half4(finalColor, 1.0);
            }

            ENDHLSL
        }

        // 그림자 캐스팅 패스
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert_shadow
            #pragma fragment frag_shadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct AttrShadow
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct VaryShadow
            {
                float4 positionCS : SV_POSITION;
            };

            VaryShadow vert_shadow(AttrShadow input)
            {
                VaryShadow output;
                float3 posWS  = TransformObjectToWorld(input.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, lightDir));

                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = posCS;
                return output;
            }

            half4 frag_shadow(VaryShadow input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
