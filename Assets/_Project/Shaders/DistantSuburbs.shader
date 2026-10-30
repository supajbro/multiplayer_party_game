Shader "Couch Guys/Distant Suburbs"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 colour:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; half4 colour:COLOR; half3 normalWS:TEXCOORD0; half fog:TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.colour = v.colour;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                Light light = GetMainLight();
                half band = smoothstep(0.32h, 0.44h, saturate(dot(normalize(i.normalWS), light.direction)));
                half3 shade = lerp(half3(0.66h,0.74h,0.70h), half3(1,1,1), band);
                half3 colour = i.colour.rgb * max(shade * light.color, SampleSH(normalize(i.normalWS)) * 0.72h);
                return half4(MixFog(colour, i.fog),1);
            }
            ENDHLSL
        }
    }
}
