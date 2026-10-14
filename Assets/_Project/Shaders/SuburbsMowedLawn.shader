Shader "Couch Guys/Suburbs Mowed Lawn"
{
    Properties
    {
        _BaseColor("Base Grass Colour", Color) = (0.333, 0.725, 0.416, 1)
        _LightStripeColor("Light Stripe Colour", Color) = (0.451, 0.800, 0.475, 1)
        _DarkStripeColor("Dark Stripe Colour", Color) = (0.259, 0.604, 0.333, 1)
        _StripeWidth("Stripe Width", Float) = 3.5
        _StripeContrast("Stripe Contrast", Range(0, 1)) = 0.42
        _DetailStrength("Grass Detail Strength", Range(0, 1)) = 0.32
        [Toggle] _WindEnabled("Wind Enabled", Float) = 1
        _WindStrength("Wind Strength", Range(0, 1)) = 0.22
        _WindSpeed("Wind Speed", Float) = 0.65
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 180

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _LightStripeColor;
                half4 _DarkStripeColor;
                float _StripeWidth;
                half _StripeContrast;
                half _DetailStrength;
                half _WindEnabled;
                half _WindStrength;
                float _WindSpeed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + 1.0), f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 worldXZ = input.positionWS.xz;
                float stripeCoordinate = worldXZ.x / max(0.5, _StripeWidth);
                half alternating = step(0.5, frac(stripeCoordinate * 0.5));
                half3 stripe = lerp(_DarkStripeColor.rgb, _LightStripeColor.rgb, alternating);
                half3 albedo = lerp(_BaseColor.rgb, stripe, _StripeContrast);

                float cameraDistance = distance(_WorldSpaceCameraPos, input.positionWS);
                half detailFade = saturate(1.0 - cameraDistance / 34.0);
                half fineNoise = (ValueNoise(worldXZ * 3.1) - 0.5) * _DetailStrength * detailFade;
                half broadVariation = (ValueNoise(worldXZ * 0.115) - 0.5) * 0.10;

                float timeOffset = _Time.y * _WindSpeed;
                float windA = ValueNoise(worldXZ * 0.055 + float2(timeOffset * 0.22, timeOffset * 0.13));
                float windB = ValueNoise(worldXZ * 0.10 + float2(-timeOffset * 0.09, timeOffset * 0.16));
                half wind = ((windA + windB) * 0.5 - 0.5) * _WindStrength * _WindEnabled;
                albedo *= 1.0 + fineNoise * 0.20 + broadVariation + wind * 0.12;

                half3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half toonBand = smoothstep(0.32h, 0.44h, ndotl * mainLight.shadowAttenuation);
                half3 shade = lerp(half3(0.66h, 0.74h, 0.70h), 1.0h.xxx, toonBand);
                half3 baked = SampleSH(normalWS);
                half3 colour = albedo * max(shade * mainLight.color, baked * 0.72h);
                colour = MixFog(colour, input.fogFactor);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
