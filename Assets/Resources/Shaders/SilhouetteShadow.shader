// 剪影投影：把拍好的剪影（r 通道 = 遮挡）按网格铺到地上。
//   顶点色 a = 这一段影子的深浅；uv2.x = 这一段的模糊半径（贴脚锐利，越往外越模糊 = 半影）。
//   用 SilhouetteShadows.cs 生成的网格；脚下接触阴影也用它（模糊 = 0，贴一张柔边椭圆）。
Shader "SLG/SilhouetteShadow"
{
    Properties
    {
        _MainTex ("Silhouette", 2D) = "black" {}
        _Color ("Shadow Color", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float blur : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.blur = v.uv1.x;
                o.color = v.color;
                return o;
            }

            // 16 点泊松圆盘（单位圆内）
            static const float2 K[16] =
            {
                float2(-0.613, 0.617), float2(0.170, -0.040), float2(-0.299, 0.791), float2(0.645, 0.493),
                float2(-0.651, 0.717), float2(0.421, 0.027), float2(-0.817, -0.271), float2(-0.705, -0.668),
                float2(0.977, -0.108), float2(0.063, -0.997), float2(0.519, 0.767), float2(-0.104, 0.435),
                float2(0.215, -0.668), float2(-0.365, -0.287), float2(0.774, -0.584), float2(-0.074, -0.402)
            };

            fixed4 frag (v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).r * 2.0;
                float wsum = 2.0;
                if (i.blur > 0.0005)
                {
                    [unroll]
                    for (int k = 0; k < 16; k++)
                    {
                        float w = 1.0 - 0.5 * length(K[k]);       // 中间权重大（近似高斯）
                        a += tex2D(_MainTex, i.uv + K[k] * i.blur).r * w;
                        wsum += w;
                    }
                }
                a /= wsum;
                return fixed4(_Color.rgb, saturate(a) * i.color.a * _Color.a);
            }
            ENDCG
        }
    }
}
