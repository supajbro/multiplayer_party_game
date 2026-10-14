Shader "Couch Guys/Suburbs Grass Tuft"
{
    Properties
    {
        _BaseColor("Base Colour", Color) = (0.32, 0.72, 0.30, 1)
        _TipColor("Tip Colour", Color) = (0.62, 0.86, 0.38, 1)
        [Toggle] _WindEnabled("Wind Enabled", Float) = 1
        _WindStrength("Wind Strength", Range(0, 1)) = 0.22
        _WindSpeed("Wind Speed", Float) = 0.65
        _DrawDistance("Draw Distance", Float) = 42
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _TipColor;
                half _WindEnabled;
                half _WindStrength;
                float _WindSpeed;
                float _DrawDistance;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 phase : TEXCOORD1; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half height : TEXCOORD2; half fogFactor : TEXCOORD3; half3 tint : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                half heightMask = input.uv.y * input.uv.y;
                float phase = input.phase.x * 6.2831853;
                float gust = sin(_Time.y * _WindSpeed * 1.7 + phase + positionWS.x * 0.07 + positionWS.z * 0.05);
                float flutter = sin(_Time.y * _WindSpeed * 2.3 + phase * 1.73 + positionWS.z * 0.11);
                float2 displacement = float2(0.82, 0.57) * (gust * 0.72 + flutter * 0.28) *
                                      _WindStrength * _WindEnabled * heightMask * 0.16;
                positionWS.xz += displacement;
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.height = input.uv.y;
                output.tint = input.color.rgb;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                clip(_DrawDistance - distance(_WorldSpaceCameraPos, input.positionWS));
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half lightTerm = lerp(0.70h, 1.0h, saturate(dot(normalWS, mainLight.direction)));
                half3 colour = lerp(_BaseColor.rgb, _TipColor.rgb, input.height) * input.tint * lightTerm;
                return half4(MixFog(colour, input.fogFactor), 1);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
