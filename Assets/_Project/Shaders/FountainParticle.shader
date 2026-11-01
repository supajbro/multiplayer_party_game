Shader "CouchGuys/Environment/FountainParticle"
{
    Properties { _BaseColor("Tint", Color) = (0.04,0.8,1,0.8) _Ring("Expanding ripple mode", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _Ring;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; half fog:TEXCOORD1; };
            V vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.color=i.color*_BaseColor; o.uv=i.uv*2-1; o.fog=ComputeFogFactor(o.positionCS.z); return o; }
            half4 frag(V i):SV_Target
            {
                float r=length(i.uv);
                float body=1-smoothstep(.78,1,r);
                float ring=(1-smoothstep(.06,.15,abs(r-.8)))*body;
                float alpha=lerp(body,ring,_Ring)*i.color.a;
                float highlight=pow(saturate(1-length((i.uv-float2(-.26,.32))*2.6)),2);
                half3 col=lerp(i.color.rgb,half3(.8,1,1),highlight*.85);
                col+=pow(saturate(r),5)*.12*(1-_Ring);
                return half4(MixFog(col,i.fog),alpha);
            }
            ENDHLSL
        }
    }
}
