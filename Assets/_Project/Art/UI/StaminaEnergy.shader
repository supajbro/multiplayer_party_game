Shader "CouchGuys/UI/StaminaEnergy"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Fill ("Stamina", Range(0,1)) = 1
        _Wave ("Speed, Frequency, Amplitude", Vector) = (0.35,2,0.24,0)
        _Glow ("Glow", Range(0,4)) = 1.4
        _Aspect ("Window Aspect", Float) = 8
        _Segment ("Segment Mode", Float) = 0
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
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
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; float4 mask : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            fixed4 _Color;
            float _Fill, _Glow, _Aspect, _Segment;
            float4 _Wave, _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = v.vertex;
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
                float2 uv = i.uv;
                float radius = lerp(0.48, 0.22, _Segment);
                float2 p = (uv - 0.5) * float2(_Aspect, 1);
                float2 q = abs(p) - (float2(_Aspect, 1) * 0.5 - radius);
                float distance = length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
                float aa = max(fwidth(distance), 0.001);
                float alpha = 1 - smoothstep(-aa, aa, distance);
                float phase = (uv.x * _Wave.y - _Time.y * _Wave.x) * 6.2831853;
                float wave = 0.5 + sin(phase) * _Wave.z;
                float d = abs(uv.y - wave);
                float core = 1 - smoothstep(0.032, 0.032 + max(fwidth(d), 0.008), d);
                float halo = exp2(-d * d * 45) * _Glow;
                float gradient = 0.12 + 0.55 * exp2(-d * d * 8);
                float3 energy = i.color.rgb * (gradient + halo);
                energy = lerp(energy, float3(0.93, 1, 1), core);
                // Segment Images use a static rounded glow; their mesh handles partial filling.
                float segmentHighlight = pow(saturate(uv.y), 9) * 0.85;
                float3 segment = lerp(i.color.rgb * (0.85 + 0.2 * uv.y), float3(0.94, 1, 1), segmentHighlight);
                float edgeLight = exp2(-abs(distance + 0.05) * 28) * 0.35;
                segment += i.color.rgb * edgeLight * _Glow;
                float3 rgb = lerp(energy, segment, _Segment);
                alpha *= step(uv.x, _Fill) * step(0.00001, _Fill) * i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                float2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                alpha *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif
                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }
}
