Shader "Couch Guys/Rocket Cartoon VFX"
{
    Properties
    {
        [HDR] _BaseColor("Colour", Color) = (1,1,1,1)
        _Opacity("Opacity", Range(0,1)) = 1
        _Shading("Chunky Shading", Range(0,1)) = 0
        _FireRamp("Yellow Core / Orange Rim", Range(0,1)) = 0
        _RingGlow("Soft Ring Glow", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; half4 color : COLOR; float3 positionWS : TEXCOORD1; float2 uv : TEXCOORD2; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Opacity;
            half _Shading;
            half _FireRamp;
            half _RingGlow;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half band = dot(normalize(input.normalWS), normalize(float3(-.4,.8,-.3))) > .1 ? 1 : .7;
                half4 color = input.color * _BaseColor;
                color.rgb *= lerp(1, band, _Shading);
                half facing = abs(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS)));
                half3 hotCore = lerp(color.rgb, half3(1.8, 1.45, .12), .65);
                half3 warmRim = color.rgb * half3(.85, .48, .3);
                half3 fire = facing > .65 ? hotCore : (facing > .28 ? color.rgb : warmRim);
                color.rgb = lerp(color.rgb, fire, _FireRamp);
                half ringBand = pow(saturate(1 - abs(input.uv.y * 2 - 1)), .65);
                color.rgb *= lerp(1, 1 + ringBand * 1.5, _RingGlow);
                color.a *= lerp(1, ringBand, _RingGlow);
                color.a *= _Opacity;
                return color;
            }
            ENDHLSL
        }
    }
}
