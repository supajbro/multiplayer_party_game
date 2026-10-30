Shader "CouchGuys/UI/AnimatedHealthFill"
{
    Properties
    {
        [PerRendererData] _MainTex ("Fill Alpha", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Green ("Healthy Colour", Color) = (0.25,1,0.035,1)
        _Orange ("Warning Colour", Color) = (1,0.42,0.015,1)
        _Red ("Critical Colour", Color) = (1,0.035,0.015,1)
        _ScrollSpeed ("Left Scroll Speed (UI units/sec)", Range(0,100)) = 16
        _StripeSpacing ("Stripe Spacing (UI units)", Range(2,80)) = 18
        _StripeIntensity ("Stripe Intensity", Range(0,1)) = 0.18
        _GradientIntensity ("Gradient Intensity", Range(0,1)) = 0.45
        _Health ("Health", Range(0,1)) = 1
        [HideInInspector] _BarRect ("Local Bar Rect", Vector) = (0,0,284,38)
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
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 local : TEXCOORD1; float4 mask : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            fixed4 _Color, _Green, _Orange, _Red, _TextureSampleAdd;
            float4 _BarRect, _ClipRect;
            float _Health, _ScrollSpeed, _StripeSpacing, _StripeIntensity, _GradientIntensity;
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
                float health = saturate(_Health);
                fixed4 baseColour = health >= 0.3
                    ? lerp(_Orange, _Green, smoothstep(0.3, 0.6, health))
                    : lerp(_Red, _Orange, smoothstep(0.0, 0.3, health));
                float2 p = i.local.xy - _BarRect.xy;
                float y = saturate(p.y / max(_BarRect.w, 0.001));
                // Positive time in this phase moves a fixed stripe toward negative X.
                // Cosine repeats continuously, including across the phase wrap.
                float phase = (p.x - p.y + _Time.y * _ScrollSpeed) / max(_StripeSpacing, 0.001);
                float stripe = pow(saturate(0.5 + 0.5 * cos(phase * 6.2831853)), 8);
                float shade = lerp(0.62, 1.1, y);
                float gloss = smoothstep(0.45, 0.52, y) * (1 - smoothstep(0.52, 1, y));
                fixed4 c = baseColour;
                c.rgb *= lerp(1, shade, _GradientIntensity) * (1 - stripe * _StripeIntensity);
                c.rgb = lerp(c.rgb, fixed3(1,1,1), gloss * _GradientIntensity * 0.65);
                c.a *= (tex2D(_MainTex, i.uv) + _TextureSampleAdd).a;
                c *= i.color;
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
