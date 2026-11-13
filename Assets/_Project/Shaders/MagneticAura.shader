Shader "CouchGuys/Magnetic Aura"
{
    Properties { _Intensity ("Intensity", Float) = 2 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            half _Intensity;
            struct Input { float4 vertex : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Output { float4 vertex : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            Output vert(Input v) { Output o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o; }
            half4 frag(Output i) : SV_Target
            {
                half edge = saturate(1.0 - abs(i.uv.y * 2.0 - 1.0));
                return half4(i.color.rgb * _Intensity, i.color.a * edge * edge);
            }
            ENDCG
        }
    }
}
