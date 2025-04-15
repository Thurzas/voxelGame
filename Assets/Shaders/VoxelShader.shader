Shader "Custom/VoxelShader"
{
    Properties
    {
        _MainTex ("Block Atlas", 2D) = "white" {}
        _TextureSize ("Texture Size", Float) = 16
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _TextureSize;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                
                // Les UVs sont déjà transformées par VoxelTypeToTexture dans le C#
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Ajout d'un petit offset pour éviter le bleeding entre les textures
                float2 uv = i.uv;
                float pixelOffset = 0.001;
                uv = lerp(uv + pixelOffset, uv - pixelOffset, uv);
                
                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }
}




