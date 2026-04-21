Shader "Custom/VoxelAtlasRepeat"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _AtlasSize ("Atlas Size", Vector) = (4, 4, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _AtlasSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1; 
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 normal : TEXCOORD2;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.uv2 = v.uv2;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 atlasGrid = _AtlasSize.xy;
                float2 tileSize = 1.0 / atlasGrid;

                float2 tiledUV = frac(i.uv);
                float2 finalUV = (i.uv2 + tiledUV) * tileSize;

                fixed4 col = tex2D(_MainTex, finalUV);

                // 핵심 추가
                float3 lightDir = normalize(float3(0.3, 1, 0.5));
                float NdotL = saturate(dot(i.normal, lightDir));

                float lighting = 0.3 + 0.7 * NdotL; // ambient + diffuse

                return col * lighting;
            }

            ENDCG
        }
    }
}