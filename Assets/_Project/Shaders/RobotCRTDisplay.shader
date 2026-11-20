Shader "Couch Guys/Robot CRT Display"
{
    Properties
    {
        [NoScaleOffset] _ExpressionAtlas ("Expressions (R eyes, G accents, B halo)", 2D) = "black" {}
        [HDR] _Cyan ("Cyan", Color) = (0.04, 0.85, 1, 1)
        _ScreenColor ("Navy Glass", Color) = (0.002, 0.008, 0.016, 1)
        _Display ("Brightness, Scanlines, Pixels, Curvature", Vector) = (2.6, 0.16, 128, 0.035)
        _Effects ("Glitch, Static, Low Health, Shutdown", Vector) = (0, 0.0015, 0, 0)
        _State ("From, To, Blend, Boot", Vector) = (0, 0, 1, 1)
        _Motion ("Eye Scale, Time, Heart Pulse, Power", Vector) = (1, 0, 0, 1)
        _ScreenUVRect ("Screen UV Island", Vector) = (0.38040865, 0.7526041, 0.23918271, 0.24479163)
        _RotateUV ("Rotate Screen UV", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "CRTDisplay"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_ExpressionAtlas); SAMPLER(sampler_ExpressionAtlas);
            CBUFFER_START(UnityPerMaterial)
                float4 _Cyan, _ScreenColor, _Display, _Effects, _State, _Motion, _ScreenUVRect;
                float _RotateUV;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }
            float noise(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float3 sampleExpression(float2 uv, float index)
            {
                // PNG rows are authored top to bottom; Unity texture V points up.
                float2 tile = float2(fmod(index, 5.0), 1.0 - floor(index / 5.0));
                float inside = step(0.0, min(uv.x, uv.y)) * step(max(uv.x, uv.y), 1.0);
                uv = clamp(uv, 0.004, 0.996);
                return SAMPLE_TEXTURE2D_LOD(_ExpressionAtlas, sampler_ExpressionAtlas, (uv + tile) / float2(5,2), 0).rgb * inside;
            }
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 island = (input.uv - _ScreenUVRect.xy) / max(_ScreenUVRect.zw, 0.0001);
                float2 uv = lerp(island, float2(1-island.y, island.x), _RotateUV);
                float inScreen = step(0.0, min(uv.x,uv.y)) * step(max(uv.x,uv.y),1.0);
                float2 p = uv * 2 - 1;
                float2 curved = 0.5 + p * (1 + dot(p,p) * _Display.w) * 0.5;
                float t = _Motion.y;
                float tick = floor(t * 35);
                float band = step(0.83, noise(float2(floor(curved.y * 18),tick)));
                curved.x += (noise(float2(tick,3))-0.5) * band * _Effects.x * 0.16;
                float2 pixels = float2(_Display.z, _Display.z * 0.48);
                float2 cellUV = (floor(curved * pixels) + 0.5) / pixels;
                // LED gaps and scanlines fade under minification to prevent shimmering.
                float fade = 1-saturate(max(fwidth(curved.x)*pixels.x, fwidth(curved.y)*pixels.y));
                float2 cell = frac(curved * pixels);
                float gaps = smoothstep(0.025,0.15,min(min(cell.x,1-cell.x),min(cell.y,1-cell.y)));
                float led = lerp(1, gaps, fade * 0.5);
                float scan = 1 - _Display.y * (0.5 + 0.5 * cos(curved.y * pixels.y * 6.28318)) * fade;
                float heart = lerp(1,1 + sin(t*7)*0.07,_Motion.z);
                float2 graphicUV = 0.5 + (cellUV - 0.5) / float2(heart, max(0.025,_Motion.x * heart));
                float3 graphics = lerp(sampleExpression(graphicUV,_State.x), sampleExpression(graphicUV,_State.y), _State.z);
                float accents = 0.65 + 0.35 * sin(t * 6);
                // Sweat is occasional, while eye graphics remain fully readable.
                float sweat = lerp(1,step(0.5,sin(t*1.8)),step(4.5,_State.y)*step(_State.y,5.5));
                float mask = max(graphics.r,graphics.g * accents * sweat);
                float lowFlicker = 1 - _Effects.z * step(0.92,noise(float2(tick,5))) * 0.38;
                float flicker = 0.985 + noise(float2(tick,9))*0.015;
                float boot = smoothstep(uv.y,uv.y+0.08,_State.w*1.08);
                float edge = 1 - smoothstep(0.83,1.04,max(abs(p.x),abs(p.y)));
                float glow = graphics.b * 0.24;
                float energy = (mask * led * scan + glow) * _Display.x * flicker * lowFlicker * _Motion.w * boot;
                energy *= 1 - _Effects.w;
                energy += exp2(-abs(uv.y-0.5)*350) * _Effects.w * _Display.x;
                float reflection = exp2(-pow((uv.y - (0.88 - uv.x*0.15))*24,2)) * 0.045;
                reflection *= smoothstep(0.05,0.3,uv.x) * (1-smoothstep(0.6,0.96,uv.x));
                float3 glass = _ScreenColor.rgb * (0.65 + 0.35*edge) + float3(0.5,0.8,1)*reflection;
                glass += (noise(floor(curved*pixels)+tick)-0.5) * _Effects.y;
                return half4(lerp(_ScreenColor.rgb*0.55,glass + _Cyan.rgb*energy*edge,inScreen),1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
