Shader "Couch Guys/Dispatcher Waveform"
{
    Properties
    {
        [HDR] _WaveColor ("Wave Colour", Color) = (0.22, 1, 0.015, 1)
        _Amplitude ("Talking Amplitude", Range(0, 0.4)) = 0.12
        _Speed ("Animation Speed", Range(0.1, 5)) = 1
        _Thickness ("Line Thickness", Range(0.001, 0.025)) = 0.004
        _GlowIntensity ("Glow Intensity", Range(0, 4)) = 1.2
        _GlowWidth ("Glow Width", Range(0.005, 0.1)) = 0.035
        _Scanlines ("Subtle Scanlines", Range(0, 0.1)) = 0.025
        [HideInInspector] _Speech ("Speech", Float) = 0
        [HideInInspector] _Emphasis ("Emphasis", Float) = 0
        [HideInInspector] _AnimationTime ("Animation Time", Float) = 0
        [HideInInspector] _Anger ("Angry Expression", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "DispatcherScreen"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _WaveColor;
                float _Amplitude, _Speed, _Thickness, _GlowIntensity, _GlowWidth, _Scanlines;
                float _Speech, _Emphasis, _AnimationTime, _Anger;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif
                float t = _AnimationTime * _Speed;
                float x = input.uv.x;
                // Incommensurate frequencies and a moving envelope avoid a short mechanical loop.
                float envelope = 0.25 + 0.75 * pow(0.5 + 0.5 * sin(t * 4.13 + sin(t * 1.79)), 2);
                float p = x * 67.0 - t * 7.1;
                float signal = (sin(p + 1.8 * sin(x * 19.0 + t * 1.37))
                    + 0.43 * sin(p * 1.73 + t * 2.31) + 0.19 * sin(p * 2.91 - t * 0.83)) / 1.62;
                float idle = 0.0015 * sin(x * 19.0 + t * 0.91)
                    + 0.002 * pow(saturate(sin(t * 0.73 + x * 3.0)), 12) * sin(x * 35.0 - t);
                float edge = smoothstep(0, 0.07, x) * smoothstep(0, 0.07, 1 - x);
                float wave = idle + edge * signal * _Amplitude * _Speech * envelope * lerp(1, 1.9, _Emphasis);
                float d = abs(input.uv.y - 0.5 - wave);
                // The existing UV-mapped TV screen draws the entire expression, on every LOD.
                float zigzag = 1 - 4 * abs(frac((x - .16) * 7.35 + .25) - .5);
                float mouthY = .40 + zigzag * .095 * step(.16, x) * step(x, .84);
                float mouthDistance = abs(input.uv.y - mouthY);
                float browX = abs(x - .5);
                float browDistance = abs(input.uv.y - (.62 + .55 * browX));
                // Finite, sharp slanted brows with the inner ends pointing down at the mouth.
                float browMask = step(.09, browX) * step(browX, .34);
                float angryDistance = min(mouthDistance, lerp(1, max(0, browDistance - .022), browMask));
                float aa = max(fwidth(input.uv.y - wave), 0.0005);
                float core = 1 - smoothstep(_Thickness, _Thickness + aa, d);
                float halo = saturate(1 - d / max(_GlowWidth, 0.001));
                halo = halo * halo * halo;
                float angryAa = max(fwidth(mouthY - input.uv.y), .001);
                float angryCore = 1 - smoothstep(_Thickness, _Thickness + angryAa, angryDistance);
                float angryHalo = pow(saturate(1 - angryDistance / max(_GlowWidth, .001)), 3);
                core = lerp(core, angryCore, saturate(_Anger));
                halo = lerp(halo, angryHalo, saturate(_Anger));
                float scan = 1 - _Scanlines * (0.5 + 0.5 * sin(input.uv.y * 900));
                // Bounded halo supplies glow without bloom; untouched pixels stay pure black.
                half3 colour = _WaveColor.rgb * (core + halo * _GlowIntensity * lerp(1, 1.6, _Emphasis)) * scan;
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}
