Shader "Couch Guys/Robot Digital Face"
{
    Properties
    {
        [HDR] _EyeColor ("Eye Colour", Color) = (0.03, 0.72, 1.0, 1.0)
        _GlowIntensity ("Glow Intensity", Range(0, 12)) = 5.5
        _ScreenColor ("Screen Colour", Color) = (0.002, 0.006, 0.012, 1)
        [HideInInspector] _ExpressionFrom ("From Expression", Float) = 0
        [HideInInspector] _ExpressionTo ("To Expression", Float) = 0
        [HideInInspector] _ExpressionBlend ("Expression Blend", Range(0, 1)) = 1
        [HideInInspector] _Blink ("Blink", Range(0, 1)) = 1
        [HideInInspector] _LookOffset ("Look Offset", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Pulse ("Pulse", Range(0, 2)) = 1
    }

    HLSLINCLUDE
    float sdEllipse(float2 p, float2 radius)
    {
        return (length(p / radius) - 1.0) * min(radius.x, radius.y);
    }

    float sdBox(float2 p, float2 halfSize)
    {
        float2 q = abs(p) - halfSize;
        return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
    }

    float2 rotatePoint(float2 p, float angle)
    {
        float s = sin(angle);
        float c = cos(angle);
        return float2(c * p.x - s * p.y, s * p.x + c * p.y);
    }

    float eyeDistance(float2 p, float expression)
    {
        float side = p.x < 0.0 ? -1.0 : 1.0;
        float2 eye = p - float2(side * 0.38, 0.0);
        float normalEye = sdEllipse(eye, float2(0.17, 0.225));

        float happyCurve = abs(eye.y - (0.075 - 1.65 * eye.x * eye.x)) - 0.045;
        float happyEye = max(happyCurve, abs(eye.x) - 0.19);

        float angryAngle = side * 0.48;
        float angryEye = sdBox(rotatePoint(eye - float2(0.0, -0.005), angryAngle),
            float2(0.205, 0.072));

        float surprisedEye = abs(sdEllipse(eye, float2(0.235, 0.285))) - 0.052;

        float focusedAngle = side * 0.20;
        float focusedEye = sdBox(rotatePoint(eye - float2(0.0, -0.015), focusedAngle),
            float2(0.205, 0.045));

        float d = normalEye;
        d = lerp(d, happyEye, step(0.5, expression));
        d = lerp(d, angryEye, step(1.5, expression));
        d = lerp(d, surprisedEye, step(2.5, expression));
        d = lerp(d, focusedEye, step(3.5, expression));
        return d;
    }

    float expressionMask(float2 p, float expression)
    {
        float distanceToEye = eyeDistance(p, expression);
        float aa = max(fwidth(distanceToEye) * 1.25, 0.004);
        return 1.0 - smoothstep(-aa, aa, distanceToEye);
    }

    float3 renderFace(float2 uv, float4 eyeColor, float4 screenColor,
        float glowIntensity, float expressionFrom, float expressionTo,
        float expressionBlend, float blink, float2 lookOffset, float pulse)
    {
        // Evaluate at LED cell centres. The fine grid remains readable at distance,
        // while the cell gap gives a subtle electronic display texture up close.
        const float2 gridSize = float2(96.0, 44.0);
        float2 cellUv = (floor(uv * gridSize) + 0.5) / gridSize;
        float2 cell = frac(uv * gridSize);
        float cellDot = smoothstep(0.02, 0.16, min(min(cell.x, cell.y), min(1.0 - cell.x, 1.0 - cell.y)));

        float2 p = (cellUv - 0.5) * float2(2.0, 1.65);
        p -= lookOffset;
        float openAmount = max(0.045, blink);
        p.y /= openAmount;

        float fromMask = expressionMask(p, expressionFrom);
        float toMask = expressionMask(p, expressionTo);
        float eyeMask = lerp(fromMask, toMask, expressionBlend) * cellDot;
        eyeMask *= smoothstep(0.0, 0.16, blink);

        // A second, non-quantised evaluation makes a restrained halo around the LEDs.
        float2 smoothP = (uv - 0.5) * float2(2.0, 1.65) - lookOffset;
        smoothP.y /= openAmount;
        float fromHalo = exp2(-abs(eyeDistance(smoothP, expressionFrom)) * 34.0);
        float toHalo = exp2(-abs(eyeDistance(smoothP, expressionTo)) * 34.0);
        float halo = lerp(fromHalo, toHalo, expressionBlend) * 0.22 *
            smoothstep(0.0, 0.18, blink);

        float vignette = saturate(1.0 - dot((uv - 0.5) * float2(1.15, 1.35),
            (uv - 0.5) * float2(1.15, 1.35)));
        float reflection = pow(saturate(1.0 - abs(uv.y - (0.82 - uv.x * 0.12)) * 15.0), 3.0) *
            smoothstep(0.08, 0.65, uv.x) * 0.018;
        float3 blackGlass = screenColor.rgb * (0.72 + 0.28 * vignette) + reflection.xxx;
        float energy = (eyeMask + halo) * glowIntensity * pulse;
        float3 colour = blackGlass + eyeColor.rgb * energy;
        colour += eyeMask * eyeColor.rgb * 0.85;
        return colour;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Cull Back
        ZWrite On

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                float4 _EyeColor;
                float4 _ScreenColor;
                float4 _LookOffset;
                float _GlowIntensity;
                float _ExpressionFrom;
                float _ExpressionTo;
                float _ExpressionBlend;
                float _Blink;
                float _Pulse;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(renderFace(input.uv, _EyeColor, _ScreenColor,
                    _GlowIntensity, _ExpressionFrom, _ExpressionTo, _ExpressionBlend,
                    _Blink, _LookOffset.xy, _Pulse), 1.0);
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Cull Back
        ZWrite On

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _EyeColor;
            float4 _ScreenColor;
            float4 _LookOffset;
            float _GlowIntensity;
            float _ExpressionFrom;
            float _ExpressionTo;
            float _ExpressionBlend;
            float _Blink;
            float _Pulse;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            float4 frag(v2f input) : SV_Target
            {
                return float4(renderFace(input.uv, _EyeColor, _ScreenColor,
                    _GlowIntensity, _ExpressionFrom, _ExpressionTo, _ExpressionBlend,
                    _Blink, _LookOffset.xy, _Pulse), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Unlit/Color"
}
