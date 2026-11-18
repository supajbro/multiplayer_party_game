Shader "CouchGuys/UI/LoadingProgress"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _TopColor ("Yellow", Color) = (1,0.87,0.19,1)
        _BottomColor ("Orange", Color) = (1,0.51,0,1)
        _Progress ("Progress", Range(0,1)) = 0
        _StripeSpacing ("Stripe Spacing (UI units)", Float) = 100
        _ScrollSpeed ("Stripe Speed (UI units/sec)", Float) = 45
        _StripeIntensity ("Stripe Intensity", Range(0,1)) = 0.18
        _Gloss ("Gloss", Range(0,1)) = 0.35
        [HideInInspector] _BarSize ("Bar Size", Vector) = (900,64,0,0)
        [HideInInspector] _AnimationTime ("Unscaled Time", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 local : TEXCOORD1; float4 mask : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            fixed4 _Color, _TopColor, _BottomColor;
            float4 _BarSize, _ClipRect;
            float _Progress, _ScrollSpeed, _StripeSpacing, _StripeIntensity, _Gloss, _AnimationTime;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.local = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                float2 pixelSize = o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clipRect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(v.vertex.xy * 2 - clipRect.xy - clipRect.zw,
                    0.25 / (0.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 size = max(_BarSize.xy, float2(0.001, 0.001));
                float2 p = i.uv * size;
                float width = size.x * saturate(_Progress);
                float radius = min(size.y * 0.5, width * 0.5);
                float2 q = abs(p - float2(width, size.y) * 0.5)
                    - (float2(width, size.y) * 0.5 - radius);
                float distance = length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
                float aa = max(fwidth(distance), 0.001);
                float coverage = (1 - smoothstep(-aa, aa, distance)) * step(0.00001, _Progress);
                float phase = (p.x - p.y - _AnimationTime * _ScrollSpeed) / max(_StripeSpacing, 1);
                float wave = sin(phase * 6.2831853);
                float stripe = smoothstep(-0.06, 0.06, wave);
                fixed4 c = lerp(_BottomColor, _TopColor, i.uv.y);
                c.rgb *= 1 - stripe * _StripeIntensity;
                float gloss = smoothstep(0.48, 0.54, i.uv.y) * (1 - smoothstep(0.54, 1, i.uv.y));
                c.rgb = lerp(c.rgb, float3(1, 1, 0.94), gloss * _Gloss);
                c *= i.color;
                c.a *= coverage;
                #ifdef UNITY_UI_CLIP_RECT
                float2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                c.a *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
