// 拍剪影（SilhouetteShadows.cs 用，不直接挂在东西上）：把一个人 / 建筑的所有部件画进一张小图，只留轮廓（白 = 被挡住）。
//   _SilCaptureRect = (框中心 x, y, 1/半宽, 1/半高)，世界坐标直接换成画面坐标，不经过镜头。
//   _SilCaptureFlip = 1 或 -1：D3D / Metal / Vulkan 上渲染到贴图要上下翻一下（C# 按 SystemInfo.graphicsUVStartsAtTop 给）。
Shader "Hidden/SLG/SilhouetteCapture"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _SilCaptureRect;
            float _SilCaptureFlip;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 w = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float2 c = (w.xy - _SilCaptureRect.xy) * _SilCaptureRect.zw;
                c.y *= _SilCaptureFlip;
                o.pos = float4(c, 0.5, 1.0);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a * _Color.a;
                return fixed4(a, a, a, a);
            }
            ENDCG
        }
    }
}
